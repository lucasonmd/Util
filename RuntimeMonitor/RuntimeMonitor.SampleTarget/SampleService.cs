namespace RuntimeMonitor.SampleTarget;

/// <summary>
/// 모니터링 대상 서비스 예시
/// 실제로는 이 서비스가 어떻게 구현되어 있는지 모르는 상태에서
/// Harmony로 외부에서 Hook을 거는 시나리오
/// </summary>
public sealed class OrderService
{
    private static int _orderCounter = 0;

    /// <summary>이 메서드의 'request' 파라미터가 모니터링됨</summary>
    public string ProcessOrder(OrderRequest request)
    {
        // 실제 비즈니스 로직 (모니터링과 무관)
        var orderId = $"ORD-{Interlocked.Increment(ref _orderCounter):D5}";
        return orderId;
    }

    public void HandleBulkOrders(IEnumerable<OrderRequest> requests)
    {
        foreach (var req in requests)
            ProcessOrder(req);
    }
}

public sealed class SensorService
{
    public void IngestData(SensorData data)
    {
        // 센서 데이터 처리
        if (data.IsAlert)
            Console.WriteLine($"[ALERT] Sensor {data.SensorId}: Temp={data.Temperature:F1}°C");
    }
}

public sealed class UserActivityService
{
    public bool LogAction(UserAction action)
    {
        // 유저 액션 로깅
        return true;
    }
}
