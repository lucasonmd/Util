using System.Text;
using DdsScope.Core.Payload;

namespace DdsScope.Core.Capture;

/// <summary>
/// Identity of a discovered DataWriter, interned once per writer.
///
/// Capture records hold a reference to this rather than copies of the strings, so a
/// captured sample costs no string allocations for its topic/writer identity.
/// </summary>
public sealed class WriterRef
{
    public WriterRef(string id, string topicName, string typeName, string displayName)
    {
        Id = id;
        TopicName = topicName;
        TypeName = typeName;
        DisplayName = string.IsNullOrEmpty(displayName) ? ShortenGuid(id) : displayName;
    }

    /// <summary>Writer GUID text; unique within the domain.</summary>
    public string Id { get; }

    public string TopicName { get; }

    public string TypeName { get; }

    /// <summary>Publication name when the writer advertises one, else a shortened GUID.</summary>
    public string DisplayName { get; }

    public static string ShortenGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return "(unknown)";
        }

        return guid.Length <= 12 ? guid : guid[^12..];
    }

    public override string ToString() => DisplayName;
}

[Flags]
public enum CaptureFlags
{
    None = 0,

    /// <summary>The payload could not be decoded; the record is kept as evidence.</summary>
    DecodeFailed = 1,

    /// <summary>The topic's type was never resolved, so no payload was decoded.</summary>
    TypeUnavailable = 2
}

/// <summary>
/// One captured DDS sample.
///
/// Records are immutable and never merged by instance key: three samples of the same key
/// are three records, in receive order.
/// </summary>
public sealed class CaptureRecord
{
    private string instanceKey;

    public CaptureRecord(
        long sequence,
        DateTime receiveTime,
        DateTime? sourceTimestamp,
        WriterRef writer,
        PayloadSnapshot payload,
        CaptureFlags flags = CaptureFlags.None,
        string error = null)
    {
        Sequence = sequence;
        ReceiveTime = receiveTime;
        SourceTimestamp = sourceTimestamp;
        Writer = writer;
        Payload = payload;
        Flags = flags;
        Error = error;

        EstimatedBytes = 96 + (payload?.EstimatedBytes ?? 0) + (error?.Length ?? 0) * 2;
    }

    /// <summary>Capture-order identity, assigned by the pipeline. Never reused.</summary>
    public long Sequence { get; }

    public DateTime ReceiveTime { get; }

    public DateTime? SourceTimestamp { get; }

    public WriterRef Writer { get; }

    public string TopicName => Writer.TopicName;

    public PayloadSnapshot Payload { get; }

    public CaptureFlags Flags { get; }

    public string Error { get; }

    public int EstimatedBytes { get; }

    /// <summary>
    /// Key fields rendered as text. Computed on demand - the grid shows it, the receive
    /// path must not pay for it.
    /// </summary>
    public string InstanceKey
    {
        get
        {
            if (instanceKey != null)
            {
                return instanceKey;
            }

            instanceKey = BuildInstanceKey();
            return instanceKey;
        }
    }

    private string BuildInstanceKey()
    {
        var schema = Payload?.Schema;
        if (schema == null || schema.KeyFields.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var field in schema.KeyFields)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(field.Name).Append('=').Append(Payload.FormatValue(field.Index));
        }

        return builder.ToString();
    }

    /// <summary>Short one-line rendering of the payload, shown in the all-topics grid.</summary>
    public string BuildPayloadSummary(int maxFields = 6)
    {
        if ((Flags & CaptureFlags.TypeUnavailable) != 0)
        {
            return "(type unavailable)";
        }

        if ((Flags & CaptureFlags.DecodeFailed) != 0)
        {
            return "(decode failed: " + Error + ")";
        }

        var schema = Payload?.Schema;
        if (schema == null || schema.Fields.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var shown = 0;
        foreach (var field in schema.Fields)
        {
            if (shown == maxFields)
            {
                builder.Append(", ...");
                break;
            }

            if (!Payload.IsPresent(field.Index))
            {
                continue;
            }

            if (shown > 0)
            {
                builder.Append(", ");
            }

            builder.Append(field.Name).Append('=').Append(Payload.FormatValue(field.Index));
            shown++;
        }

        return builder.ToString();
    }
}
