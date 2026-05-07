using RuntimeMonitor.Core.DTOs;

namespace RuntimeMonitor.Viewer.Models;

/// <summary>
/// UI 표시용 패킷 모델 (MonitorPacket의 View 전용 변환체)
/// init-only 프로퍼티로 불변성 보장
/// </summary>
public sealed class PacketItem
{
    public required string PacketId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string ProcessName { get; init; }
    public required int ProcessId { get; init; }
    public required string MethodFullName { get; init; }
    public required string ObjectTypeName { get; init; }
    public required string PayloadJson { get; init; }
    public required int ThreadId { get; init; }
    public required string ParameterName { get; init; }
    public required int ParameterIndex { get; init; }
    public string? SerializationError { get; init; }

    /// <summary>MonitorPacket을 PacketItem으로 변환 (UTC → Local 시간 변환 포함)</summary>
    public static PacketItem FromPacket(MonitorPacket packet) => new()
    {
        PacketId = packet.PacketId,
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(packet.TimestampMs).LocalDateTime,
        ProcessName = packet.ProcessName,
        ProcessId = packet.ProcessId,
        MethodFullName = packet.MethodFullName,
        ObjectTypeName = packet.ObjectTypeName,
        PayloadJson = packet.PayloadJson,
        ThreadId = packet.ThreadId,
        ParameterName = packet.ParameterName,
        ParameterIndex = packet.ParameterIndex,
        SerializationError = packet.SerializationError
    };
}
