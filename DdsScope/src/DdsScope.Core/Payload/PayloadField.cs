namespace DdsScope.Core.Payload;

/// <summary>How a captured value is interpreted.</summary>
public enum PayloadValueKind
{
    Unsupported = 0,
    Bool,
    Int,
    UInt,
    Float,
    Char,
    Enum,
    String,
    Collection
}

/// <summary>Which backing array of a <see cref="PayloadSnapshot"/> holds the value.</summary>
public enum PayloadSlot
{
    None = 0,
    Numeric,
    Text,
    Collection
}

/// <summary>
/// One leaf field of a flattened payload type. Created once per type, never per sample.
/// </summary>
public sealed class PayloadField
{
    /// <summary>Index into <see cref="PayloadSchema.Fields"/>.</summary>
    public int Index { get; init; }

    /// <summary>Dotted path from the payload root, e.g. <c>Position.X</c>.</summary>
    public string Path { get; init; }

    /// <summary>Leaf member name, e.g. <c>X</c>.</summary>
    public string Name { get; init; }

    public PayloadValueKind Kind { get; init; }

    public PayloadSlot Slot { get; init; }

    /// <summary>Index within the slot array the value lives in.</summary>
    public int SlotIndex { get; init; }

    /// <summary>True when the IDL member is part of the instance key.</summary>
    public bool IsKey { get; init; }

    /// <summary>IDL type name, for display in the writer/sample detail panes.</summary>
    public string TypeName { get; init; }

    /// <summary>Label table for enum fields; null otherwise.</summary>
    public IReadOnlyDictionary<long, string> EnumLabels { get; init; }

    /// <summary>
    /// True when the field lives under a union branch, so it is absent from most samples.
    /// </summary>
    public bool IsOptional { get; init; }

    public override string ToString() => $"{Path} : {TypeName}";
}
