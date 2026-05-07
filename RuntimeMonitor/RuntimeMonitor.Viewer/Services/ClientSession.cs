using RuntimeMonitor.Core.DTOs;
using RuntimeMonitor.Core.Serialization;
using System.Net.Sockets;

namespace RuntimeMonitor.Viewer.Services;

/// <summary>
/// 개별 클라이언트 TCP 연결 세션
///
/// 프로토콜: [4바이트 리틀엔디안 길이][MessagePack 데이터]
/// 최대 패킷 크기: 1MB (악의적 또는 잘못된 패킷으로부터 서버 보호)
/// </summary>
public sealed class ClientSession : IDisposable
{
    private readonly string _sessionId;
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private bool _disposed;

    private const int MaxPacketSize = 1024 * 1024; // 1MB

    public event Action<MonitorPacket>? PacketReceived;
    public event Action<string>? Disconnected;

    public string SessionId => _sessionId;
    public string RemoteEndPoint => _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

    public ClientSession(string sessionId, TcpClient tcpClient)
    {
        _sessionId = sessionId;
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
    }

    /// <summary>
    /// 수신 루프 - CancellationToken으로 정상 종료 가능
    /// 헤더 → 데이터 순서로 정확히 읽는 프레임 기반 수신
    /// </summary>
    public async Task StartReceivingAsync(CancellationToken ct)
    {
        var headerBuffer = new byte[PacketSerializer.HeaderSize];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 헤더(4바이트) 정확히 읽기
                if (!await ReadExactAsync(headerBuffer, PacketSerializer.HeaderSize, ct))
                    break; // 연결 종료

                if (!PacketSerializer.TryReadLength(headerBuffer, out var length))
                    break; // 잘못된 헤더

                if (length > MaxPacketSize)
                {
                    // 비정상 패킷 크기 - 연결 종료
                    break;
                }

                // 데이터 부분 읽기
                var dataBuffer = new byte[length];
                if (!await ReadExactAsync(dataBuffer, length, ct))
                    break;

                try
                {
                    var packet = PacketSerializer.Deserialize(dataBuffer);
                    PacketReceived?.Invoke(packet);
                }
                catch (Exception ex)
                {
                    // 역직렬화 실패 - 이 패킷만 스킵, 연결은 유지
                    Console.Error.WriteLine($"[ClientSession] Deserialize error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ClientSession:{_sessionId}] Receive error: {ex.Message}");
        }
        finally
        {
            Disconnected?.Invoke(_sessionId);
        }
    }

    /// <summary>count 바이트를 정확히 읽을 때까지 반복 (TCP는 분할 수신될 수 있음)</summary>
    private async Task<bool> ReadExactAsync(byte[] buffer, int count, CancellationToken ct)
    {
        var offset = 0;
        while (offset < count)
        {
            int read;
            try
            {
                read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            }
            catch
            {
                return false;
            }

            if (read == 0) return false; // 연결 종료 (EOF)
            offset += read;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _stream.Dispose(); } catch { /* ignore */ }
        try { _tcpClient.Dispose(); } catch { /* ignore */ }
    }
}
