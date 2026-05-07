namespace RuntimeMonitor.Viewer.Models;

/// <summary>패킷 필터링 설정 모델</summary>
public sealed class FilterSettings
{
    public string TypeNameFilter { get; set; } = string.Empty;
    public string MethodNameFilter { get; set; } = string.Empty;
    public string ProcessNameFilter { get; set; } = string.Empty;

    /// <summary>필터 조건 일치 여부 검사</summary>
    public bool Matches(PacketItem packet)
    {
        if (!string.IsNullOrEmpty(TypeNameFilter) &&
            !packet.ObjectTypeName.Contains(TypeNameFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(MethodNameFilter) &&
            !packet.MethodFullName.Contains(MethodNameFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(ProcessNameFilter) &&
            !packet.ProcessName.Contains(ProcessNameFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public bool IsEmpty =>
        string.IsNullOrEmpty(TypeNameFilter) &&
        string.IsNullOrEmpty(MethodNameFilter) &&
        string.IsNullOrEmpty(ProcessNameFilter);
}
