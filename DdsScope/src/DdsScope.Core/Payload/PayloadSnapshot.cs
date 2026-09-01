using System.Globalization;

namespace DdsScope.Core.Payload;

/// <summary>
/// An immutable, vendor-neutral copy of one sample's payload.
///
/// Values live in flat slot arrays rather than a per-sample dictionary: at the design load
/// of ~4,000 samples/s with 10-20 fields each, a dictionary per sample would allocate tens
/// of megabytes per second and dominate GC time.
/// </summary>
public sealed class PayloadSnapshot
{
    private readonly long[] numeric;
    private readonly string[] text;
    private readonly CollectionValue[] collections;
    private readonly ulong[] presence;

    internal PayloadSnapshot(
        PayloadSchema schema,
        long[] numeric,
        string[] text,
        CollectionValue[] collections,
        ulong[] presence,
        int estimatedBytes)
    {
        Schema = schema;
        this.numeric = numeric;
        this.text = text;
        this.collections = collections;
        this.presence = presence;
        EstimatedBytes = estimatedBytes;
    }

    public PayloadSchema Schema { get; }

    /// <summary>Approximate managed footprint, used by the capture store's memory budget.</summary>
    public int EstimatedBytes { get; }

    public static PayloadSnapshot Empty(PayloadSchema schema) => new(
        schema,
        Array.Empty<long>(),
        Array.Empty<string>(),
        Array.Empty<CollectionValue>(),
        new ulong[Math.Max(1, schema.PresenceWords)],
        32);

    /// <summary>
    /// False when the field was not carried by this sample - an inactive union branch, or a
    /// field the decoder could not read.
    /// </summary>
    public bool IsPresent(int fieldIndex)
    {
        if ((uint)fieldIndex >= (uint)Schema.Fields.Count)
        {
            return false;
        }

        return (presence[fieldIndex >> 6] & (1UL << (fieldIndex & 63))) != 0;
    }

    public bool TryGetInt64(int fieldIndex, out long value)
    {
        value = 0;
        if (!IsPresent(fieldIndex))
        {
            return false;
        }

        var field = Schema.Fields[fieldIndex];
        switch (field.Kind)
        {
            case PayloadValueKind.Bool:
            case PayloadValueKind.Int:
            case PayloadValueKind.UInt:
            case PayloadValueKind.Char:
            case PayloadValueKind.Enum:
                value = numeric[field.SlotIndex];
                return true;
            case PayloadValueKind.Float:
                value = (long)BitConverter.Int64BitsToDouble(numeric[field.SlotIndex]);
                return true;
            default:
                return false;
        }
    }

    public bool TryGetDouble(int fieldIndex, out double value)
    {
        value = 0;
        if (!IsPresent(fieldIndex))
        {
            return false;
        }

        var field = Schema.Fields[fieldIndex];
        switch (field.Kind)
        {
            case PayloadValueKind.Float:
                value = BitConverter.Int64BitsToDouble(numeric[field.SlotIndex]);
                return true;
            case PayloadValueKind.UInt:
                value = (ulong)numeric[field.SlotIndex];
                return true;
            case PayloadValueKind.Bool:
            case PayloadValueKind.Int:
            case PayloadValueKind.Char:
            case PayloadValueKind.Enum:
                value = numeric[field.SlotIndex];
                return true;
            default:
                return false;
        }
    }

    public bool TryGetString(int fieldIndex, out string value)
    {
        value = null;
        if (!IsPresent(fieldIndex))
        {
            return false;
        }

        var field = Schema.Fields[fieldIndex];
        if (field.Kind != PayloadValueKind.String)
        {
            return false;
        }

        value = text[field.SlotIndex];
        return value != null;
    }

    public CollectionValue GetCollection(int fieldIndex)
    {
        if (!IsPresent(fieldIndex))
        {
            return null;
        }

        var field = Schema.Fields[fieldIndex];
        return field.Kind == PayloadValueKind.Collection ? collections[field.SlotIndex] : null;
    }

    /// <summary>Boxed access, for grid cells and CSV export. Returns null when absent.</summary>
    public object GetValue(int fieldIndex)
    {
        if (!IsPresent(fieldIndex))
        {
            return null;
        }

        var field = Schema.Fields[fieldIndex];
        return field.Kind switch
        {
            PayloadValueKind.Bool => numeric[field.SlotIndex] != 0,
            PayloadValueKind.Int => numeric[field.SlotIndex],
            PayloadValueKind.UInt => (ulong)numeric[field.SlotIndex],
            PayloadValueKind.Char => (char)numeric[field.SlotIndex],
            PayloadValueKind.Float => BitConverter.Int64BitsToDouble(numeric[field.SlotIndex]),
            PayloadValueKind.Enum => numeric[field.SlotIndex],
            PayloadValueKind.String => text[field.SlotIndex],
            PayloadValueKind.Collection => collections[field.SlotIndex],
            _ => null
        };
    }

    /// <summary>Display text for a field, resolving enum labels.</summary>
    public string FormatValue(int fieldIndex)
    {
        if (!IsPresent(fieldIndex))
        {
            return string.Empty;
        }

        var field = Schema.Fields[fieldIndex];
        switch (field.Kind)
        {
            case PayloadValueKind.Enum:
            {
                var raw = numeric[field.SlotIndex];
                if (field.EnumLabels != null && field.EnumLabels.TryGetValue(raw, out var label))
                {
                    return label + " (" + raw.ToString(CultureInfo.InvariantCulture) + ")";
                }

                return raw.ToString(CultureInfo.InvariantCulture);
            }

            case PayloadValueKind.Float:
                return BitConverter.Int64BitsToDouble(numeric[field.SlotIndex])
                    .ToString("G6", CultureInfo.InvariantCulture);

            case PayloadValueKind.String:
                return text[field.SlotIndex] ?? string.Empty;

            case PayloadValueKind.Collection:
                return collections[field.SlotIndex]?.ToString() ?? "[]";

            default:
            {
                var value = GetValue(fieldIndex);
                return value is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : value?.ToString() ?? string.Empty;
            }
        }
    }
}
