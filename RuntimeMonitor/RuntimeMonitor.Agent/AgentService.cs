using RuntimeMonitor.Agent.Hook;
using RuntimeMonitor.Agent.Network;

namespace RuntimeMonitor.Agent;

/// <summary>
/// 모니터링 에이전트 진입점 및 생명주기 관리
///
/// 사용 예시:
///   var agent = new AgentService("192.168.1.100", 9000);
///   await agent.StartAsync();
///   agent.RegisterHook&lt;MyService&gt;("ProcessData", parameterIndex: 0);
///   // ... 프로그램 실행 ...
///   await agent.DisposeAsync(); // 종료 시 반드시 호출
/// </summary>
public sealed class AgentService : IAsyncDisposable
{
    private readonly HarmonyHookManager _hookManager;
    private readonly MonitorClient _client;
    private readonly PacketSendQueue _sendQueue;
    private readonly CancellationTokenSource _cts = new();
    private Task? _sendLoopTask;
    private bool _disposed;

    public bool IsConnected => _client.IsConnected;

    public event Action<string>? StatusChanged
    {
        add => _client.StatusChanged += value;
        remove => _client.StatusChanged -= value;
    }

    public AgentService(string serverHost = "127.0.0.1", int serverPort = 9000)
    {
        _sendQueue = new PacketSendQueue();
        _client = new MonitorClient(serverHost, serverPort, _sendQueue);
        _hookManager = new HarmonyHookManager(_sendQueue);
    }

    /// <summary>에이전트 시작 - 서버 연결 후 송신 루프 시작</summary>
    public async Task StartAsync()
    {
        await _client.ConnectAsync(_cts.Token);

        // 송신 루프를 백그라운드 Task로 실행 (Hook 스레드와 독립)
        _sendLoopTask = Task.Run(
            () => _client.StartSendLoopAsync(_cts.Token),
            _cts.Token);
    }

    /// <summary>특정 메서드에 Hook 등록 (타입 안전 제네릭 버전)</summary>
    public void RegisterHook<TTarget>(string methodName, int parameterIndex = 0)
    {
        _hookManager.Patch<TTarget>(methodName, parameterIndex);
    }

    /// <summary>런타임 타입으로 Hook 등록 (리플렉션으로 타입을 가져올 때 사용)</summary>
    public void RegisterHook(Type targetType, string methodName, int parameterIndex = 0)
    {
        _hookManager.PatchMethod(targetType, methodName, parameterIndex);
    }

    /// <summary>
    /// 안전한 종료 순서:
    /// 1. Harmony Unpatch (새 Hook 데이터 수집 중지)
    /// 2. 큐에 남은 데이터 플러시 (최대 3초)
    /// 3. 송신 루프 취소
    /// 4. TCP 연결 정상 종료
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // 1. Hook 해제
        _hookManager.Dispose();

        // 2. 미전송 데이터 플러시
        await _sendQueue.FlushAsync(TimeSpan.FromSeconds(3));

        // 3. 루프 취소
        _cts.Cancel();
        if (_sendLoopTask is not null)
        {
            try { await _sendLoopTask; } catch { /* ignore */ }
        }

        // 4. 리소스 정리
        await _client.DisposeAsync();
        await _sendQueue.DisposeAsync();
        _cts.Dispose();
    }
}
