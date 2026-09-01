namespace DdsScope.Core.Payload;

/// <summary>
/// The flattened, DDS-vendor-neutral description of a topic's payload type.
///
/// One instance exists per discovered type and is shared by every sample of that type,
/// so per-sample cost is limited to the value arrays in <see cref="PayloadSnapshot"/>.
/// </summary>
public sealed class PayloadSchema
{
    private static int nextId;

    private readonly Dictionary<string, int> indexByPath;

    public PayloadSchema(string typeName, IReadOnlyList<PayloadField> fields)
    {
        Id = Interlocked.Increment(ref nextId);
        TypeName = typeName ?? "(unknown)";
        Fields = fields ?? Array.Empty<PayloadField>();

        indexByPath = new Dictionary<string, int>(Fields.Count, StringComparer.OrdinalIgnoreCase);
        var keys = new List<PayloadField>();

        foreach (var field in Fields)
        {
            indexByPath[field.Path] = field.Index;
            if (field.IsKey)
            {
                keys.Add(field);
            }

            switch (field.Slot)
            {
                case PayloadSlot.Numeric:
                    NumericSlots = Math.Max(NumericSlots, field.SlotIndex + 1);
                    break;
                case PayloadSlot.Text:
                    TextSlots = Math.Max(TextSlots, field.SlotIndex + 1);
                    break;
                case PayloadSlot.Collection:
                    CollectionSlots = Math.Max(CollectionSlots, field.SlotIndex + 1);
                    break;
            }
        }

        KeyFields = keys;
        PresenceWords = (Fields.Count + 63) / 64;
    }

    /// <summary>Process-wide id, used as the key of filter accessor caches.</summary>
    public int Id { get; }

    public string TypeName { get; }

    public IReadOnlyList<PayloadField> Fields { get; }

    public IReadOnlyList<PayloadField> KeyFields { get; }

    public int NumericSlots { get; }

    public int TextSlots { get; }

    public int CollectionSlots { get; }

    internal int PresenceWords { get; }

    /// <summary>Resolves a dotted field path. Case-insensitive, as display filters are.</summary>
    public bool TryGetFieldIndex(string path, out int index)
    {
        if (string.IsNullOrEmpty(path))
        {
            index = -1;
            return false;
        }

        return indexByPath.TryGetValue(path, out index);
    }

    public PayloadField GetField(int index) => Fields[index];

    /// <summary>An empty schema, used for topics whose type could not be resolved.</summary>
    public static PayloadSchema Unavailable(string typeName) =>
        new(typeName, Array.Empty<PayloadField>());
}
