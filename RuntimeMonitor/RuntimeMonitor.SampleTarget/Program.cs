using RuntimeMonitor.Agent;
using RuntimeMonitor.SampleTarget;

Console.WriteLine("RuntimeMonitor SampleTarget 시작");
Console.WriteLine("Viewer를 먼저 실행하고 '시작' 버튼을 누르세요.");
Console.WriteLine();

// ============================================================
// 에이전트 초기화
// ============================================================
await using var agent = new AgentService(
    serverHost: "127.0.0.1",  // Viewer가 실행 중인 주소
    serverPort: 9000);

// 서버 상태 출력
agent.StatusChanged += msg => Console.WriteLine($"[Agent] {msg}");

await agent.StartAsync();

// ============================================================
// Hook 등록 - 모니터링할 메서드 지정
// ============================================================

// OrderService.ProcessOrder의 첫 번째 파라미터(OrderRequest) 캡처
agent.RegisterHook<OrderService>(nameof(OrderService.ProcessOrder), parameterIndex: 0);

// SensorService.IngestData의 첫 번째 파라미터(SensorData) 캡처
agent.RegisterHook<SensorService>(nameof(SensorService.IngestData), parameterIndex: 0);

// UserActivityService.LogAction의 첫 번째 파라미터(UserAction) 캡처
agent.RegisterHook<UserActivityService>(nameof(UserActivityService.LogAction), parameterIndex: 0);

Console.WriteLine("Hook 등록 완료. 데이터 생성 시작...");
Console.WriteLine("종료하려면 아무 키나 누르세요.");
Console.WriteLine();

// ============================================================
// 서비스 인스턴스 생성
// ============================================================
var orderService = new OrderService();
var sensorService = new SensorService();
var userService = new UserActivityService();
var rng = new Random();

// ============================================================
// 백그라운드에서 지속적으로 데이터 생성 (모니터링 테스트용)
// ============================================================
using var cts = new CancellationTokenSource();

var dataTask = Task.Run(async () =>
{
    var counter = 0;
    var regions = new[] { "Seoul", "Busan", "Daegu", "Incheon", "Gwangju" };
    var products = new[] { "노트북", "스마트폰", "태블릿", "이어폰", "키보드" };
    var actions = new[] { "LOGIN", "PURCHASE", "SEARCH", "LOGOUT", "VIEW_ITEM" };

    while (!cts.Token.IsCancellationRequested)
    {
        counter++;

        // 주문 처리 (OrderRequest 캡처)
        orderService.ProcessOrder(new OrderRequest
        {
            OrderId = $"REQ-{counter:D5}",
            ProductName = products[rng.Next(products.Length)],
            Quantity = rng.Next(1, 10),
            Price = (decimal)(rng.NextDouble() * 1_000_000 + 10_000),
            OrderedAt = DateTime.UtcNow,
            Customer = new CustomerInfo
            {
                Name = $"고객_{counter}",
                Email = $"user{counter}@example.com",
                Region = regions[rng.Next(regions.Length)]
            }
        });

        // 센서 데이터 수집 (SensorData 캡처)
        sensorService.IngestData(new SensorData
        {
            SensorId = $"SENSOR-{rng.Next(1, 6):D2}",
            Temperature = 20.0 + rng.NextDouble() * 30.0,
            Humidity = 30.0 + rng.NextDouble() * 50.0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IsAlert = rng.NextDouble() < 0.1  // 10% 확률로 알림
        });

        // 유저 액션 로깅 (UserAction 캡처)
        userService.LogAction(new UserAction
        {
            UserId = $"U{rng.Next(1000, 9999)}",
            Action = actions[rng.Next(actions.Length)],
            Payload = $"{{\"item\":\"{counter}\"}}",
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Console.Write($"\r[{DateTime.Now:HH:mm:ss}] 생성된 이벤트: {counter * 3}개");

        // 0.5초마다 배치 전송
        await Task.Delay(500, cts.Token);
    }
}, cts.Token);

// 키 입력 대기
Console.ReadKey(intercept: true);
Console.WriteLine("\n\n종료 중...");

// ============================================================
// 안전한 종료 처리
// ============================================================
cts.Cancel();

try { await dataTask; } catch (OperationCanceledException) { }

// AgentService.DisposeAsync()가 자동 호출됨 (await using)
// - Harmony Unpatch
// - 큐에 남은 데이터 플러시 (최대 3초)
// - TCP 연결 정상 종료
Console.WriteLine("안전하게 종료되었습니다.");
