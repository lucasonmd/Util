using RuntimeMonitor.Viewer.Models;
using System.Text.Json;

namespace RuntimeMonitor.Viewer.ViewModels;

/// <summary>
/// ListView 행 하나에 해당하는 ViewModel
/// PacketItem 모델을 UI 표시 형식으로 변환
/// </summary>
public sealed class PacketItemViewModel
{
    private readonly PacketItem _model;
    private string? _formattedJson; // 지연 계산 캐싱

    public PacketItemViewModel(PacketItem model)
    {
        _model = model;
    }

    // 기본 표시 프로퍼티
    public string PacketId => _model.PacketId;
    public string TimestampDisplay => _model.Timestamp.ToString("HH:mm:ss.fff");
    public string DateDisplay => _model.Timestamp.ToString("yyyy-MM-dd");
    public string ProcessDisplay => $"{_model.ProcessName} ({_model.ProcessId})";
    public string ProcessName => _model.ProcessName;
    public string MethodDisplay => _model.MethodFullName;
    public string TypeDisplay => _model.ObjectTypeName;
    public string ShortTypeName => _model.ObjectTypeName.Split('.').LastOrDefault() ?? _model.ObjectTypeName;
    public string ParameterDisplay => $"[{_model.ParameterIndex}] {_model.ParameterName}";
    public string ThreadDisplay => $"Thread {_model.ThreadId}";
    public string RawJson => _model.PayloadJson;
    public bool HasError => _model.SerializationError is not null;
    public string? ErrorMessage => _model.SerializationError;

    /// <summary>들여쓰기된 JSON (상세 보기용, 지연 계산)</summary>
    public string FormattedJson => _formattedJson ??= FormatJson(_model.PayloadJson);

    /// <summary>요약 표시 (목록에서 한 줄로 보여줄 payload 미리보기)</summary>
    public string PayloadSummary
    {
        get
        {
            var json = _model.PayloadJson;
            if (string.IsNullOrEmpty(json)) return "(empty)";
            return json.Length > 120 ? json[..120] + "..." : json;
        }
    }

    private static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json; // JSON 파싱 실패 시 원본 반환
        }
    }
}
