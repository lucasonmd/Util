using System.Globalization;
using DdsScope.Core.Capture;
using DdsScope.Core.Payload;

namespace DdsScope.Filtering;

/// <summary>
/// Reads one filterable value out of a capture record.
///
/// Accessors are created once when a filter is compiled and reused for every record, so no
/// field name is ever resolved by string lookup during evaluation of a payload field.
/// </summary>
public interface IFieldAccessor
{
    string Path { get; }

    bool IsPresent(CaptureRecord record);

    bool TryGetNumber(CaptureRecord record, out double value);

    bool TryGetText(CaptureRecord record, out string value);
}

internal sealed class TopicAccessor : IFieldAccessor
{
    public string Path => "topic";

    public bool IsPresent(CaptureRecord record) => true;

    public bool TryGetNumber(CaptureRecord record, out double value)
    {
        value = 0;
        return false;
    }

    public bool TryGetText(CaptureRecord record, out string value)
    {
        value = record.TopicName;
        return value != null;
    }
}

internal sealed class WriterAccessor : IFieldAccessor
{
    public string Path => "writer";

    public bool IsPresent(CaptureRecord record) => true;

    public bool TryGetNumber(CaptureRecord record, out double value)
    {
        value = 0;
        return false;
    }

    public bool TryGetText(CaptureRecord record, out string value)
    {
        value = record.Writer?.DisplayName;
        return value != null;
    }
}

internal sealed class WriterIdAccessor : IFieldAccessor
{
    public string Path => "writerid";

    public bool IsPresent(CaptureRecord record) => true;

    public bool TryGetNumber(CaptureRecord record, out double value)
    {
        value = 0;
        return false;
    }

    public bool TryGetText(CaptureRecord record, out string value)
    {
        value = record.Writer?.Id;
        return value != null;
    }
}

internal sealed class TypeNameAccessor : IFieldAccessor
{
    public string Path => "type";

    public bool IsPresent(CaptureRecord record) => true;

    public bool TryGetNumber(CaptureRecord record, out double value)
    {
        value = 0;
        return false;
    }

    public bool TryGetText(CaptureRecord record, out string value)
    {
        value = record.Writer?.TypeName;
        return value != null;
    }
}

internal sealed class InstanceKeyAccessor : IFieldAccessor
{
    public string Path => "key";

    public bool IsPresent(CaptureRecord record) => true;

    public bool TryGetNumber(CaptureRecord record, out double value)
    {
        value = 0;
        return false;
    }

    public bool TryGetText(CaptureRecord record, out string value)
    {
        value = record.InstanceKey;
        return value != null;
    }
}

internal sealed class SequenceAccessor : IFieldAccessor
{
    public string Path => "seq";

    public bool IsPresent(CaptureRecord record) => true;

    public bool TryGetNumber(CaptureRecord record, out double value)
    {
        value = record.Sequence;
        return true;
    }

    public bool TryGetText(CaptureRecord record, out string value)
    {
        value = record.Sequence.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>
/// Reads a payload field addressed as <c>data.Position.X</c>.
///
/// The all-topics view mixes records of many schemas, so the accessor keeps a small
/// schema-id to field-index map. Lookups are a plain array index; the map only grows under a
/// lock when a schema is seen for the first time, and readers always observe a complete array.
/// </summary>
internal sealed class PayloadFieldAccessor : IFieldAccessor
{
    private const int Unresolved = -2;

    private readonly object growGate = new();
    private volatile int[] indexBySchemaId = Array.Empty<int>();

    public PayloadFieldAccessor(string path)
    {
        Path = path;
    }

    public string Path { get; }

    private int ResolveIndex(PayloadSchema schema)
    {
        var map = indexBySchemaId;
        if (schema.Id < map.Length)
        {
            var cached = map[schema.Id];
            if (cached != Unresolved)
            {
                return cached;
            }
        }

        var resolved = schema.TryGetFieldIndex(Path, out var index) ? index : -1;

        lock (growGate)
        {
            var current = indexBySchemaId;
            if (schema.Id >= current.Length)
            {
                var grown = new int[Math.Max(schema.Id + 1, Math.Max(8, current.Length * 2))];
                Array.Fill(grown, Unresolved);
                Array.Copy(current, grown, current.Length);
                current = grown;
            }
            else
            {
                current = (int[])current.Clone();
            }

            current[schema.Id] = resolved;
            indexBySchemaId = current;
        }

        return resolved;
    }

    private bool TryResolve(CaptureRecord record, out PayloadSnapshot payload, out int index)
    {
        payload = record.Payload;
        index = -1;

        if (payload?.Schema == null)
        {
            return false;
        }

        index = ResolveIndex(payload.Schema);
        return index >= 0;
    }

    public bool IsPresent(CaptureRecord record) =>
        TryResolve(record, out var payload, out var index) && payload.IsPresent(index);

    public bool TryGetNumber(CaptureRecord record, out double value)
    {
        value = 0;
        if (!TryResolve(record, out var payload, out var index))
        {
            return false;
        }

        if (payload.TryGetDouble(index, out value))
        {
            return true;
        }

        // Allow numeric comparison against a string field that happens to hold digits.
        return payload.TryGetString(index, out var text) &&
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public bool TryGetText(CaptureRecord record, out string value)
    {
        value = null;
        if (!TryResolve(record, out var payload, out var index) || !payload.IsPresent(index))
        {
            return false;
        }

        var field = payload.Schema.Fields[index];
        if (field.Kind == PayloadValueKind.Enum)
        {
            // Compare against the label, so `data.Status == Enabled` works.
            if (payload.TryGetInt64(index, out var raw))
            {
                if (field.EnumLabels != null && field.EnumLabels.TryGetValue(raw, out var label))
                {
                    value = label;
                    return true;
                }

                value = raw.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        value = payload.FormatValue(index);
        return true;
    }
}
