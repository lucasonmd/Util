using RuntimeMonitor.Core.DTOs;
using RuntimeMonitor.Core.Serialization;
using System.Net.Sockets;

namespace RuntimeMonitor.Agent.Network;

/// <summary>
/// TCP 기반 모니터 서버 연결 클라이언트
///
/// 기능:
///   - 서버에 TCP 연결 및 자동 재연결
///   - PacketSendQueue에서 패킷을 읽어 TCP로 전송
///   - 연결 실패 시 패킷 드롭 (대상 프로그램 성능 우선)
///   - 정상 종료 시 남은 패킷 플러시
/// </summary>
public sealed class MonitorClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly PacketSendQueue _sendQueue;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private volatile bool _connected;
    private bool _disposed;

    private const int ReconnectDelayMs = 3_000;
    private const int ConnectTimeoutMs = 5_000;
    private const int SendBufferSize = 64 * 1024; // 64KB 송신 버퍼

    public bool IsConnected => _connected && (_tcpClient?.Connected ?? false);

    public event Action<string>? StatusChanged;

    public MonitorClient(string host, int port, PacketSendQueue sendQueue)
    {
        _host = host;
        _port = port;
        _sendQueue = sendQueue;
    }

    /// <summary>서버에 초기 연결 시도</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectTimeoutMs);

        try
        {
            _tcpClient = new TcpClient
            {
                SendBufferSize = SendBufferSize,
                NoDelay = true  // Nagle 알고리즘 비활성화 - 낮은 레이턴시 우선
            };
            await _tcpClient.ConnectAsync(_host, _port, timeoutCts.Token);
            _stream = _tcpClient.GetStream();
            _connected = true;
            StatusChanged?.Invoke($"Connected to {_host}:{_port}");
        }
        catch (Exception ex)
        {
            _connected = false;
            StatusChanged?.Invoke($"Connect failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 패킷 큐를 읽어 TCP로 전송하는 메인 루프
    /// 이 메서드는 큐가 완료되거나 CancellationToken이 취소될 때까지 실행됨
    /// </summary>
    public async Task StartSendLoopAsync(CancellationToken ct)
    {
        await foreach (var packet in _sendQueue.PacketReader.ReadAllAsync(ct))
        {
            await TrySendPacketAsync(packet, ct);
        }
    }

    private async Task TrySendPacketAsync(MonitorPacket packet, CancellationToken ct)
    {
        // 연결 끊김 시 재연결 시도
        if (!IsConnected)
        {
            await TryReconnectAsync(ct);
            if (!IsConnected) return; // 재연결 실패 시 패킷 드롭 (대상 프로그램 보호 우선)
        }

        try
        {
            var data = PacketSerializer.Serialize(packet);
            await _stream!.WriteAsync(data, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _connected = false;
            StatusChanged?.Invoke($"Send failed: {ex.Message}");
        }
    }

    private async Task TryReconnectAsync(CancellationToken ct)
    {
        CleanupConnection();

        try
        {
            await Task.Delay(ReconnectDelayMs, ct);
            await ConnectAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
    }

    private void CleanupConnection()
    {
        _connected = false;
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _stream = null;
        _tcpClient = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        CleanupConnection();
        await ValueTask.CompletedTask;
    }
}
