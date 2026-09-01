using DdsScope.Core.Capture;
using DdsScope.Core.Payload;

namespace DdsScope.Dds.Abstractions;

/// <summary>Whether a topic's type could be resolved from discovery.</summary>
public enum TopicTypeState
{
    Unknown = 0,

    /// <summary>A full type description arrived, so samples can be decoded.</summary>
    Available,

    /// <summary>
    /// No type description was propagated. The topic is listed and its writers are tracked,
    /// but its samples cannot be decoded - shown as "Type unavailable".
    /// </summary>
    Unavailable
}

/// <summary>One discovered topic.</summary>
public sealed class DdsTopicInfo
{
    public DdsTopicInfo(string topicName, string typeName)
    {
        TopicName = topicName;
        TypeName = typeName;
        FirstSeen = DateTime.Now;
    }

    public string TopicName { get; }

    public string TypeName { get; internal set; }

    public TopicTypeState TypeState { get; set; } = TopicTypeState.Unknown;

    /// <summary>Human-readable reason when <see cref="TypeState"/> is Unavailable.</summary>
    public string TypeStateDetail { get; set; }

    /// <summary>The flattened schema; null while the type is unavailable.</summary>
    public PayloadSchema Schema { get; set; }

    public DateTime FirstSeen { get; }

    public override string ToString() => TopicName;
}

/// <summary>One QoS or discovery attribute of a writer, for the detail pane.</summary>
public sealed class DdsQosItem
{
    public DdsQosItem(string group, string name, string value)
    {
        Group = group;
        Name = name;
        Value = value;
    }

    public string Group { get; }

    public string Name { get; }

    public string Value { get; }
}

/// <summary>One discovered DataWriter.</summary>
public sealed class DdsWriterInfo
{
    public DdsWriterInfo(WriterRef reference, string participantId)
    {
        Reference = reference;
        ParticipantId = participantId;
        FirstSeen = DateTime.Now;
        LastSeen = FirstSeen;
        IsOnline = true;
    }

    /// <summary>The interned identity shared with every capture record from this writer.</summary>
    public WriterRef Reference { get; }

    public string Id => Reference.Id;

    public string TopicName => Reference.TopicName;

    public string TypeName => Reference.TypeName;

    public string DisplayName => Reference.DisplayName;

    public string ParticipantId { get; }

    /// <summary>
    /// False once the writer leaves the network. The entry is kept, not removed: its captured
    /// history is still in the store and still worth browsing.
    /// </summary>
    public bool IsOnline { get; set; }

    public DateTime FirstSeen { get; }

    public DateTime LastSeen { get; set; }

    public IReadOnlyList<DdsQosItem> Qos { get; set; } = Array.Empty<DdsQosItem>();

    public override string ToString() => DisplayName;
}

public enum DiagnosticSeverity
{
    Info = 0,
    Warning,
    Error
}

/// <summary>A non-fatal problem, scoped so one bad topic cannot stop the rest.</summary>
public sealed class DdsDiagnostic
{
    public DdsDiagnostic(DiagnosticSeverity severity, string scope, string message)
    {
        Time = DateTime.Now;
        Severity = severity;
        Scope = scope;
        Message = message;
    }

    public DateTime Time { get; }

    public DiagnosticSeverity Severity { get; }

    /// <summary>Topic name, writer id or "domain" - whatever the failure was isolated to.</summary>
    public string Scope { get; }

    public string Message { get; }

    public override string ToString() => $"[{Severity}] {Scope}: {Message}";
}
