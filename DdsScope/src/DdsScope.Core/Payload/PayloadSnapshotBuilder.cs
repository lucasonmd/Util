namespace DdsScope.Core.Payload;

/// <summary>
/// Fills the slot arrays of one <see cref="PayloadSnapshot"/>.
///
/// A builder belongs to a single reader thread and is reused across samples; each
/// <see cref="Build"/> hands its arrays to the snapshot and allocates fresh ones, so no
/// snapshot ever shares mutable state with the next sample.
/// </summary>
public sealed class PayloadSnapshotBuilder
{
    private readonly PayloadSchema schema;

    private long[] numeric;
    private string[] text;
    private CollectionValue[] collections;
    private ulong[] presence;
    private int variableBytes;

    public PayloadSnapshotBuilder(PayloadSchema schema)
    {
        this.schema = schema ?? throw new ArgumentNullException(nameof(schema));
        Allocate();
    }

    public PayloadSchema Schema => schema;

    private void Allocate()
    {
        numeric = schema.NumericSlots > 0 ? new long[schema.NumericSlots] : Array.Empty<long>();
        text = schema.TextSlots > 0 ? new string[schema.TextSlots] : Array.Empty<string>();
        collections = schema.CollectionSlots > 0
            ? new CollectionValue[schema.CollectionSlots]
            : Array.Empty<CollectionValue>();
        presence = new ulong[Math.Max(1, schema.PresenceWords)];
        variableBytes = 0;
    }

    public void SetInt64(PayloadField field, long value)
    {
        numeric[field.SlotIndex] = value;
        MarkPresent(field.Index);
    }

    public void SetUInt64(PayloadField field, ulong value)
    {
        numeric[field.SlotIndex] = unchecked((long)value);
        MarkPresent(field.Index);
    }

    public void SetDouble(PayloadField field, double value)
    {
        numeric[field.SlotIndex] = BitConverter.DoubleToInt64Bits(value);
        MarkPresent(field.Index);
    }

    public void SetBool(PayloadField field, bool value)
    {
        numeric[field.SlotIndex] = value ? 1 : 0;
        MarkPresent(field.Index);
    }

    public void SetString(PayloadField field, string value)
    {
        text[field.SlotIndex] = value;
        variableBytes += 24 + (value?.Length ?? 0) * 2;
        MarkPresent(field.Index);
    }

    public void SetCollection(PayloadField field, CollectionValue value)
    {
        collections[field.SlotIndex] = value;
        variableBytes += value?.EstimatedBytes ?? 0;
        MarkPresent(field.Index);
    }

    private void MarkPresent(int fieldIndex) =>
        presence[fieldIndex >> 6] |= 1UL << (fieldIndex & 63);

    /// <summary>Seals the current sample and prepares the builder for the next one.</summary>
    public PayloadSnapshot Build()
    {
        var estimated =
            48 +
            numeric.Length * 8 +
            text.Length * 8 +
            collections.Length * 8 +
            presence.Length * 8 +
            variableBytes;

        var snapshot = new PayloadSnapshot(schema, numeric, text, collections, presence, estimated);
        Allocate();
        return snapshot;
    }
}
