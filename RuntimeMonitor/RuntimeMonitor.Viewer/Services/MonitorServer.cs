using RuntimeMonitor.Core.DTOs;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace RuntimeMonitor.Viewer.Services;

/// <summary>
/// 멀티 클라이언트 TCP 모니터 서버
///
/// 기능:
///   - 여러 프로세스/PC에서 동시 연결 지원
///   - 각 연결을 독립적인 ClientSession으로 관리
///   - 연결/해제 이벤트 노티피케이션
///   - 안전한 종료 (모든 세션 정리)
/// </summary>
public sealed class MonitorServer : IAsyncDisposable
{
    private TcpListener? _listener;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _started;
    private bool _disposed;

    /// <summary>패킷 수신 이벤트 (어느 클라이언트에서 왔든 모두 여기로)</summary>
    public event Action<MonitorPacket>? PacketReceived;

    /// <summary>새 클라이언트 연결 이벤트 (세션 ID, 원격 주소)</summary>
    public event Action<string, string>? ClientConnected;

    /// <summary>클라이언트 연결 해제 이벤트 (세션 ID)</summary>
    public event Action<string>? ClientDisconnected;

    /// <summary>현재 연결된 클라이언트 수</summary>
    public int ConnectedCount => _sessions.Count;

    /// <summary>서버 리스닝 포트</summary>
    public int Port { get; private set; }

    /// <summary>지정 포트에서 수신 대기 시작</summary>
    public async Task StartAsync(int port = 9000)
    {
        if (_started) throw new InvalidOperationException("서버가 이미 실행 중입니다.");

        Port = port;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Server.SetSocketOption(
            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(backlog: 20);
        _started = true;

        // 클라이언트 수락 루프를 백그라운드에서 실행
        _ = Task.Run(() => AcceptClientsAsync(_cts.Token));

        await Task.CompletedTask;
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MonitorServer] Accept error: {ex.Message}");
                continue;
            }

            // 세션 생성 및 등록
            var sessionId = Guid.NewGuid().ToString("N")[..8]; // 8자리로 축약
            var session = new ClientSession(sessionId, tcpClient);

            session.PacketReceived += pkt => PacketReceived?.Invoke(pkt);
            session.Disconnected += OnSessionDisconnected;

            _sessions[sessionId] = session;
            ClientConnected?.Invoke(sessionId, session.RemoteEndPoint);

            // 각 세션을 독립적인 Task로 실행
            _ = Task.Run(() => session.StartReceivingAsync(ct), ct);
        }
    }

    private void OnSessionDisconnected(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.Dispose();
            ClientDisconnected?.Invoke(sessionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _listener?.Stop();

        // 모든 세션 정리
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();

        _cts.Dispose();
        await ValueTask.CompletedTask;
    }
}
