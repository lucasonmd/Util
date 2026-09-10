using System.Globalization;
using System.Text;

namespace DdsScope.Core.Payload;

/// <summary>Why a <see cref="CollectionValue"/> holds fewer elements than the wire carried.</summary>
public enum CollectionTruncation
{
    /// <summary>Every element was captured.</summary>
    None = 0,

    /// <summary>The element count was over the decoder's per-sample cap.</summary>
    ElementCap,

    /// <summary>The elements are of a shape the decoder cannot read, so only the length is known.</summary>
    Unreadable
}

/// <summary>One member of a <see cref="CollectionElement"/>, named by its path within the element.</summary>
public readonly struct CollectionMember
{
    public CollectionMember(string name, object value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public object Value { get; }
}

/// <summary>
/// One element of a collection whose element type is a struct or union - the elements of a
/// <c>sequence&lt;SourceId&gt;</c>, where each carries members of its own.
///
/// Members are held in schema order rather than in a dictionary: elements are small and there
/// are many of them, so a dictionary per element would cost more than the values it holds.
/// </summary>
public sealed class CollectionElement
{
    public CollectionElement(IReadOnlyList<CollectionMember> members)
    {
        Members = members ?? Array.Empty<CollectionMember>();
    }

    public IReadOnlyList<CollectionMember> Members { get; }

    public int EstimatedBytes => 24 + Members.Count * 24;

    public override string ToString()
    {
        if (Members.Count == 0)
        {
            return "{}";
        }

        var builder = new StringBuilder("{");
        for (var i = 0; i < Members.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder
                .Append(Members[i].Name)
                .Append('=')
                .Append(Convert.ToString(Members[i].Value, CultureInfo.InvariantCulture));
        }

        return builder.Append('}').ToString();
    }
}

/// <summary>
/// A captured array/sequence field.
///
/// Large collections are recorded by length only: materialising, say, a
/// sequence&lt;octet, 3000000&gt; for every sample would dominate both the receive thread
/// and the capture store. <see cref="Truncation"/> says which of the two reasons left
/// <see cref="Items"/> short, so the detail pane can tell "too big to keep" apart from
/// "the decoder could not read this".
/// </summary>
public sealed class CollectionValue
{
    public static readonly CollectionValue Empty =
        new(0, Array.Empty<object>(), CollectionTruncation.None);

    public CollectionValue(int length, IReadOnlyList<object> items, CollectionTruncation truncation)
    {
        Length = length;
        Items = items ?? Array.Empty<object>();
        Truncation = truncation;
    }

    /// <summary>Actual element count on the wire.</summary>
    public int Length { get; }

    /// <summary>
    /// The elements that were captured; may be shorter than <see cref="Length"/>. Elements of a
    /// collection of structs are <see cref="CollectionElement"/>; everything else is the boxed
    /// primitive.
    /// </summary>
    public IReadOnlyList<object> Items { get; }

    public CollectionTruncation Truncation { get; }

    /// <summary>True when <see cref="Items"/> holds fewer elements than <see cref="Length"/>.</summary>
    public bool Truncated => Truncation != CollectionTruncation.None;

    /// <summary>
    /// Approximate managed footprint, used by the capture store's memory budget. One collection
    /// carries one element type, so the first element sizes them all.
    /// </summary>
    public int EstimatedBytes => 32 + Items.Count * (Items.Count > 0 && Items[0] is CollectionElement element
        ? 16 + element.EstimatedBytes
        : 16);

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
