using MessagePack;

namespace RuntimeMonitor.SampleTarget;

/// <summary>샘플 주문 모델 - MessagePack 직렬화 가능</summary>
[MessagePackObject]
public sealed class OrderRequest
{
    [Key(0)] public string OrderId { get; set; } = string.Empty;
    [Key(1)] public string ProductName { get; set; } = string.Empty;
    [Key(2)] public int Quantity { get; set; }
    [Key(3)] public decimal Price { get; set; }
    [Key(4)] public DateTime OrderedAt { get; set; }
    [Key(5)] public CustomerInfo Customer { get; set; } = new();
}

[MessagePackObject]
public sealed class CustomerInfo
{
    [Key(0)] public string Name { get; set; } = string.Empty;
    [Key(1)] public string Email { get; set; } = string.Empty;
    [Key(2)] public string Region { get; set; } = string.Empty;
}

/// <summary>샘플 센서 데이터</summary>
[MessagePackObject]
public sealed class SensorData
{
    [Key(0)] public string SensorId { get; set; } = string.Empty;
    [Key(1)] public double Temperature { get; set; }
    [Key(2)] public double Humidity { get; set; }
    [Key(3)] public long Timestamp { get; set; }
    [Key(4)] public bool IsAlert { get; set; }
}

/// <summary>샘플 유저 액션</summary>
[MessagePackObject]
public sealed class UserAction
{
    [Key(0)] public string UserId { get; set; } = string.Empty;
    [Key(1)] public string Action { get; set; } = string.Empty;
    [Key(2)] public string Payload { get; set; } = string.Empty;
    [Key(3)] public long TimestampMs { get; set; }
}
