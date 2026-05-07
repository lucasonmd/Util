using MessagePack;
using System.Diagnostics;

namespace RuntimeMonitor.Core.DTOs;

/// <summary>
/// 모니터링 데이터 전송 패킷 DTO
/// MessagePack으로 직렬화되어 TCP로 전송됨
/// Key 번호는 변경하지 말 것 (하위 호환성)
/// </summary>
[MessagePackObject]
public sealed class MonitorPacket
{
    /// <summary>패킷 고유 ID (16진수 GUID)</summary>
    [Key(0)]
    public string PacketId { get; set; } = string.Empty;

    /// <summary>수집 시각 (Unix timestamp milliseconds, UTC)</summary>
    [Key(1)]
    public long TimestampMs { get; set; }

    /// <summary>수집된 프로세스 이름</summary>
    [Key(2)]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>수집된 프로세스 ID</summary>
    [Key(3)]
    public int ProcessId { get; set; }

    /// <summary>Hook된 메서드 전체 이름 (Namespace.ClassName.MethodName)</summary>
    [Key(4)]
    public string MethodFullName { get; set; } = string.Empty;

    /// <summary>수집된 object의 타입 전체 이름</summary>
    [Key(5)]
    public string ObjectTypeName { get; set; } = string.Empty;

    /// <summary>직렬화된 object의 JSON 표현 (Viewer 표시용)</summary>
    [Key(6)]
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>수집 스레드 관리 ID</summary>
    [Key(7)]
    public int ThreadId { get; set; }

    /// <summary>캡처된 파라미터의 인덱스</summary>
    [Key(8)]
    public int ParameterIndex { get; set; }

    /// <summary>캡처된 파라미터 이름</summary>
    [Key(9)]
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>직렬화 오류 메시지 (실패 시에만 설정됨)</summary>
    [Key(10)]
    public string? SerializationError { get; set; }
}

/// <summary>
/// 프로세스 정보를 정적으로 캐싱 - Process.GetCurrentProcess() 반복 호출 최소화
/// </summary>
internal static class ProcessInfoCache
{
    private static readonly Process _process = Process.GetCurrentProcess();
    public static readonly string Name = _process.ProcessName;
    public static readonly int Id = _process.Id;
}
