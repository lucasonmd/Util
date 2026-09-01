namespace DdsScope.Core.Payload;

/// <summary>
/// A captured array/sequence field.
///
/// Large collections are recorded by length only: materialising, say, a
/// sequence&lt;octet, 3000000&gt; for every sample would dominate both the receive thread
/// and the capture store.
/// </summary>
public sealed class CollectionValue
{
    public static readonly CollectionValue Empty = new(0, Array.Empty<object>(), false);

    public CollectionValue(int length, IReadOnlyList<object> items, bool truncated)
    {
        Length = length;
        Items = items ?? Array.Empty<object>();
        Truncated = truncated;
    }

    /// <summary>Actual element count on the wire.</summary>
    public int Length { get; }

    /// <summary>The elements that were captured; may be shorter than <see cref="Length"/>.</summary>
    public IReadOnlyList<object> Items { get; }

    /// <summary>True when <see cref="Items"/> holds fewer elements than <see cref="Length"/>.</summary>
    public bool Truncated { get; }

    public int EstimatedBytes => 32 + Items.Count * 16;

    public override string ToString()
    {
        if (Length == 0)
        {
            return "[]";
        }

        if (Items.Count == 0)
        {
            return $"[{Length} items]";
        }

        var shown = string.Join(", ", Items.Take(8));
        return Truncated || Items.Count > 8
            ? $"[{shown}, ... ({Length} items)]"
            : $"[{shown}]";
    }
}
