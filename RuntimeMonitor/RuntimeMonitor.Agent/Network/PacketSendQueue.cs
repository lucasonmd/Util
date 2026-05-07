using RuntimeMonitor.Core.DTOs;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace RuntimeMonitor.Agent.Network;

/// <summary>
/// 비동기 Channel 기반 패킷 송신 큐
///
/// 데이터 흐름:
///   Hook Thread → EnqueueRaw() → _rawChannel → ProcessRawItems() → _packetChannel → MonitorClient
///
/// 성능 설계:
///   - Channel(BoundedChannel): lock-free, 고성능 생산자-소비자 구조
///   - Hook 스레드(생산자)는 TryWrite만 호출하고 즉시 반환
///   - 직렬화(JSON)는 단일 백그라운드 스레드에서 순차 처리
///   - 메모리 제한: DropOldest 정책으로 메모리 폭증 방지
///
/// GC 최적화:
///   - RawCaptureItem은 record struct로 힙 할당 최소화
///   - JsonSerializer Options 정적 캐싱
/// </summary>
public sealed class PacketSendQueue : IAsyncDisposable
{
    // Hook 스레드 → 직렬화 워커
    private readonly Channel<RawCaptureItem> _rawChannel;

    // 직렬화 워커 → 네트워크 송신기
    private readonly Channel<MonitorPacket> _packetChannel;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;

    // JSON 직렬화 옵션 정적 캐싱 (매번 생성하면 GC 압박)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        MaxDepth = 8,                          // 순환 참조 방지
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>네트워크 송신기가 패킷을 읽는 채널 리더</summary>
    public ChannelReader<MonitorPacket> PacketReader => _packetChannel.Reader;

    public PacketSendQueue()
    {
        // Raw 채널: 여러 Hook 스레드가 쓰고, 단일 워커가 읽음
        _rawChannel = Channel.CreateBounded<RawCaptureItem>(
            new BoundedChannelOptions(10_000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,  // 메모리 보호
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false           // Hook 스레드 블로킹 방지
            });

        // Packet 채널: 워커가 쓰고, 네트워크 클라이언트가 읽음
        _packetChannel = Channel.CreateBounded<MonitorPacket>(
            new BoundedChannelOptions(5_000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });

        // 직렬화 백그라운드 태스크 시작
        _processingTask = Task.Run(ProcessRawItemsAsync);
    }

    /// <summary>
    /// Hook에서 호출 - 최소 작업만 수행 (non-blocking)
    /// object 참조와 메타데이터만 채널에 넣고 즉시 반환
    /// </summary>
    public void EnqueueRaw(
        string methodKey, object obj,
        int paramIdx, string paramName,
        long timestampMs, int threadId)
    {
        // TryWrite: 채널이 가득 차도 블로킹 없음 (DropOldest 정책)
        _rawChannel.Writer.TryWrite(
            new RawCaptureItem(methodKey, obj, paramIdx, paramName, timestampMs, threadId));
    }

    /// <summary>
    /// 백그라운드 직렬화 워커
    /// raw object를 MonitorPacket으로 변환하여 패킷 채널에 전달
    /// </summary>
    private async Task ProcessRawItemsAsync()
    {
        try
        {
            await foreach (var item in _rawChannel.Reader.ReadAllAsync(_cts.Token))
            {
                MonitorPacket packet;
                try
                {
                    var typeName = GetCachedTypeName(item.Object);

                    string payloadJson;
                    try
                    {
                        payloadJson = JsonSerializer.Serialize(item.Object, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        // 직렬화 실패 시 ToString() 폴백
                        payloadJson = $"{{\"_error\":\"{EscapeJson(ex.Message)}\"," +
                                      $"\"_toString\":\"{EscapeJson(item.Object.ToString() ?? "null")}\"}}";
                    }

                    packet = new MonitorPacket
                    {
                        PacketId = Guid.NewGuid().ToString("N"),
                        TimestampMs = item.TimestampMs,
                        ProcessName = ProcessInfoCache.Name,
                        ProcessId = ProcessInfoCache.Id,
                        MethodFullName = item.MethodKey,
                        ObjectTypeName = typeName,
                        PayloadJson = payloadJson,
                        ThreadId = item.ThreadId,
                        ParameterIndex = item.ParameterIndex,
                        ParameterName = item.ParameterName
                    };
                }
                catch (Exception ex)
                {
                    // 패킷 생성 자체 실패 - 오류 패킷 전송
                    packet = new MonitorPacket
                    {
                        PacketId = Guid.NewGuid().ToString("N"),
                        TimestampMs = item.TimestampMs,
                        ProcessName = ProcessInfoCache.Name,
                        ProcessId = ProcessInfoCache.Id,
                        MethodFullName = item.MethodKey,
                        ObjectTypeName = "Error",
                        PayloadJson = $"{{\"_error\":\"{EscapeJson(ex.Message)}\"}}",
                        SerializationError = ex.Message,
                        ThreadId = item.ThreadId
                    };
                }

                // WriteAsync: 패킷 채널 가득 차면 잠깐 대기 (하지만 Hook 스레드는 이미 해제됨)
                await _packetChannel.Writer.WriteAsync(packet, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
        finally
        {
            _packetChannel.Writer.TryComplete();
        }
    }

    /// <summary>타입 이름 캐싱 - GetType().FullName 반복 Reflection 최소화</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> TypeNameCache = new();

    private static string GetCachedTypeName(object obj)
    {
        var type = obj.GetType();
        return TypeNameCache.GetOrAdd(type, t => t.FullName ?? t.Name);
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    /// <summary>남은 아이템 처리 후 완료 대기 (프로그램 종료 시 호출)</summary>
    public async Task FlushAsync(TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(3);

        _rawChannel.Writer.TryComplete();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _processingTask.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 타임아웃 - 남은 아이템 포기
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _rawChannel.Writer.TryComplete();
        _packetChannel.Writer.TryComplete();

        try { await _processingTask; } catch { /* ignore */ }

        _cts.Dispose();
    }

    // record struct로 힙 할당 최소화
    private readonly record struct RawCaptureItem(
        string MethodKey,
        object Object,
        int ParameterIndex,
        string ParameterName,
        long TimestampMs,
        int ThreadId);
}
