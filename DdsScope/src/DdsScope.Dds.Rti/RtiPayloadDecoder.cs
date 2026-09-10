using System.Collections;
using System.Globalization;
using System.Text;
using DdsScope.Core.Payload;
using Rti.Types.Dynamic;
using RtiTypeKind = Omg.Types.Dynamic.TypeKind;

namespace DdsScope.Dds.Rti;

/// <summary>
/// Turns a loaned RTI <see cref="DynamicData"/> into a vendor-neutral
/// <see cref="PayloadSnapshot"/>.
///
/// This is the one non-trivial thing done on a DDS receive thread, and deliberately the
/// cheapest form of it: a walk of the precomputed plan that writes primitives straight into
/// flat slot arrays. No dictionaries, no boxing for primitives, no string formatting, and no
/// tree of node objects. Copying here is what lets the loan be returned immediately and lets
/// capture history outlive the middleware's sample buffers.
///
/// One decoder belongs to one reader, so it is used by a single thread at a time.
/// </summary>
internal sealed class RtiPayloadDecoder
{
    private readonly RtiTypeSchema typeSchema;
    private readonly PayloadSnapshotBuilder builder;
    private readonly int maxCollectionElements;

    public RtiPayloadDecoder(RtiTypeSchema typeSchema, int maxCollectionElements)
    {
        this.typeSchema = typeSchema;
        this.maxCollectionElements = Math.Max(0, maxCollectionElements);
        builder = new PayloadSnapshotBuilder(typeSchema.Schema);
    }

    public PayloadSchema Schema => typeSchema.Schema;

    /// <summary>
    /// Decodes one sample. On failure the partially filled snapshot is still returned with
    /// <paramref name="error"/> set, so a single malformed sample is visible in the capture
    /// instead of silently vanishing.
    /// </summary>
    public PayloadSnapshot Decode(DynamicData data, out string error)
    {
        error = null;

        try
        {
            ReadAggregate(data, typeSchema.Root);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return builder.Build();
    }

    private void ReadAggregate(DynamicData data, ReadNode node)
    {
        if (node.Kind == ReadNodeKind.Union)
        {
            ReadUnion(data, node);
            return;
        }

        var children = node.Children;
        for (var i = 0; i < children.Length; i++)
        {
            ReadChild(data, children[i]);
        }
    }

    private void ReadChild(DynamicData data, ReadNode child)
    {
        switch (child.Kind)
        {
            case ReadNodeKind.Struct:
            case ReadNodeKind.Union:
            {
                using var loaned = data.LoanValue(child.Name);
                ReadAggregate(loaned.Data, child);
                break;
            }

            case ReadNodeKind.Collection:
                ReadCollection(data, child);
                break;

            case ReadNodeKind.CharArray:
                ReadCharArray(data, child);
                break;

            default:
                ReadLeaf(data, child);
                break;
        }
    }

    /// <summary>
    /// Only the active branch of a union carries data, so the members actually present in the
    /// sample are enumerated rather than assumed.
    /// </summary>
    private void ReadUnion(DynamicData data, ReadNode node)
    {
        if (node.ChildrenByName == null)
        {
            return;
        }

        var count = data.MemberCount;
        for (uint i = 0; i < count; i++)
        {
            var info = data.GetMemberInfoByIndex(i);
            if (info.MemberName == null || !node.ChildrenByName.TryGetValue(info.MemberName, out var child))
            {
                continue;
            }

            ReadChild(data, child);
        }
    }

    private void ReadLeaf(DynamicData data, ReadNode node)
    {
        var field = node.Field;
        if (field == null)
        {
            return;
        }

        if (field.IsOptional && !data.MemberExists(node.Name))
        {
            return;
        }

        switch (node.LeafKind)
        {
            case RtiTypeKind.Boolean:
                builder.SetBool(field, data.GetValue<bool>(node.Name));
                break;

            case RtiTypeKind.Int16:
                builder.SetInt64(field, data.GetValue<short>(node.Name));
                break;

            case RtiTypeKind.Int32:
                builder.SetInt64(field, data.GetValue<int>(node.Name));
                break;

            case RtiTypeKind.Int64:
                builder.SetInt64(field, data.GetValue<long>(node.Name));
                break;

            case RtiTypeKind.Uint8:
                builder.SetUInt64(field, data.GetValue<byte>(node.Name));
                break;

            case RtiTypeKind.Uint16:
                builder.SetUInt64(field, data.GetValue<ushort>(node.Name));
                break;

            case RtiTypeKind.UInt32:
                builder.SetUInt64(field, data.GetValue<uint>(node.Name));
                break;

            case RtiTypeKind.UInt64:
                builder.SetUInt64(field, data.GetValue<ulong>(node.Name));
                break;

            case RtiTypeKind.Float32:
                builder.SetDouble(field, data.GetValue<float>(node.Name));
                break;

            case RtiTypeKind.Float64:
                builder.SetDouble(field, data.GetValue<double>(node.Name));
                break;

            case RtiTypeKind.Char8:
            case RtiTypeKind.Char16:
                builder.SetInt64(field, data.GetValue<char>(node.Name));
                break;

            case RtiTypeKind.Enumeration:
                builder.SetInt64(field, data.GetValue<int>(node.Name));
                break;

            case RtiTypeKind.String:
            case RtiTypeKind.WideString:
                builder.SetString(field, data.GetValue<string>(node.Name));
                break;

            // Int8 and Octet are not switch labels because the 7.3.0 baseline does not
            // define them. Compared by value they cost one branch on a rare kind.
            default:
                if (node.LeafKind == RtiKindCompat.Int8)
                {
                    builder.SetInt64(field, data.GetValue<sbyte>(node.Name));
                }
                else if (node.LeafKind == RtiKindCompat.Octet)
                {
                    builder.SetUInt64(field, data.GetValue<byte>(node.Name));
                }
                else
                {
                    ReadLeafFallback(data, node, field);
                }

                break;
        }
    }

    private void ReadLeafFallback(DynamicData data, ReadNode node, PayloadField field)
    {
        var value = data.GetAnyValue(node.Name);
        switch (value)
        {
            case null:
                return;
            case string text:
                builder.SetString(field, text);
                break;
            case bool flag:
                builder.SetBool(field, flag);
                break;
            case IConvertible convertible:
                builder.SetDouble(field, convertible.ToDouble(null));
                break;
            default:
                builder.SetString(field, value.ToString());
                break;
        }
    }

    /// <summary>
    /// Captures <c>char[N]</c> and <c>sequence&lt;char&gt;</c> as text.
    ///
    /// IDL uses a char array where other languages use a string, so the sample reads as
    /// <c>abc</c> rather than <c>[a, b, c]</c>, and the value is filtered and exported as the
    /// text it is. The schema already assigns such a field a string slot.
    /// </summary>
    private void ReadCharArray(DynamicData data, ReadNode node)
    {
        var field = node.Field;
        if (field == null)
        {
            return;
        }

        if (field.IsOptional && !data.MemberExists(node.Name))
        {
            return;
        }

        string text;
        try
        {
            text = AsText(data.GetAnyValue(node.Name));
        }
        catch (Exception)
        {
            // One unreadable field must not cost us the rest of the sample.
            return;
        }

        if (text != null)
        {
            builder.SetString(field, text);
        }
    }

    /// <summary>Renders whatever shape a char collection arrives in as a string.</summary>
    private static string AsText(object raw)
    {
        switch (raw)
        {
            case null:
                return null;

            case string text:
                return TrimPadding(text);

            case char[] chars:
                return TrimPadding(new string(chars));
        }

        if (raw is not IEnumerable enumerable)
        {
            return null;
        }

        // Named apart from the snapshot builder this class writes into, which it is not.
        var result = new StringBuilder();
        foreach (var element in enumerable)
        {
            switch (element)
            {
                case char c:
                    result.Append(c);
                    break;

                // Char8 can arrive as its numeric kind, which is the same character.
                case IConvertible convertible:
                    result.Append((char)convertible.ToInt32(CultureInfo.InvariantCulture));
                    break;
            }
        }

        return TrimPadding(result.ToString());
    }

    /// <summary>
    /// A fixed-width char array is padded to its declared length. The padding is not part of
    /// the value, and rendering it would put stray NULs in the grid and the CSV.
    /// </summary>
    private static string TrimPadding(string value)
    {
        var end = value.IndexOf('\0');
        return end < 0 ? value : value[..end];
    }

    /// <summary>
    /// Arrays and sequences are recorded by length, and their elements copied only while the
    /// element count stays under the configured cap. That is what stops a
    /// <c>sequence&lt;octet, 3000000&gt;</c> from being materialised on the receive thread.
    /// </summary>
    private void ReadCollection(DynamicData data, ReadNode node)
    {
        var field = node.Field;
        if (field == null)
        {
            return;
        }

        if (field.IsOptional && !data.MemberExists(node.Name))
        {
            return;
        }

        var info = data.GetMemberInfo(node.Name);
        var length = (int)info.ElementCount;

        if (length <= 0)
        {
            builder.SetCollection(field, CollectionValue.Empty);
            return;
        }

        if (length > maxCollectionElements)
        {
            builder.SetCollection(field, LengthOnly(length, CollectionTruncation.ElementCap));
            return;
        }

        object[] items;
        try
        {
            // Which path to take is decided by the type plan, not by what GetAnyValue hands
            // back: GetAnyValue only understands primitives, so on a sequence of structs it
            // throws - or returns something that is not the elements at all.
            items = node.ElementMembers != null
                ? ReadAggregateElements(data, node, length)
                : Materialize(data.GetAnyValue(node.Name), length);
        }
        catch (Exception)
        {
            // A collection we cannot read must not cost us the rest of the sample.
            builder.SetCollection(field, LengthOnly(length, CollectionTruncation.Unreadable));
            return;
        }

        builder.SetCollection(field, new CollectionValue(
            length,
            items,
            items.Length >= length ? CollectionTruncation.None : CollectionTruncation.Unreadable));
    }

    private static CollectionValue LengthOnly(int length, CollectionTruncation reason) =>
        new(length, Array.Empty<object>(), reason);

    /// <summary>
    /// Reads a collection whose elements are structs or unions.
    ///
    /// RTI exposes such a collection as a DynamicData whose members are its elements, numbered
    /// from 1, so each element is loaned in turn and walked with the element plan. That is one
    /// loan per element, which is why this runs only after the count has been checked against
    /// the cap.
    /// </summary>
    private object[] ReadAggregateElements(DynamicData data, ReadNode node, int length)
    {
        var take = Math.Min(length, maxCollectionElements);
        var items = new object[take];
        var members = new List<CollectionMember>();

        using var loaned = data.LoanValue(node.Name);
        var collection = loaned.Data;

        for (var i = 0; i < take; i++)
        {
            members.Clear();

            using (var element = collection.LoanValueByIndex((uint)(i + 1)))
            {
                ReadElementMembers(element.Data, node.ElementMembers, members, null);
            }

            items[i] = new CollectionElement(members.ToArray());
        }

        return items;
    }

    private static void ReadElementMembers(
        DynamicData data,
        ElementNode[] plan,
        List<CollectionMember> into,
        string prefix)
    {
        for (var i = 0; i < plan.Length; i++)
        {
            var member = plan[i];
            var path = prefix == null ? member.Name : prefix + "." + member.Name;

            if (member.IsAggregate)
            {
                using var loaned = data.LoanValue(member.Name);
                ReadElementMembers(loaned.Data, member.Children, into, path);
                continue;
            }

            into.Add(new CollectionMember(path, ReadElementLeaf(data, member)));
        }
    }

    /// <summary>
    /// Reads one leaf of an element. Values are boxed here, unlike the flat-slot path for
    /// top-level fields: an element has no slot of its own, and there are few of them.
    /// </summary>
    private static object ReadElementLeaf(DynamicData data, ElementNode member)
    {
        if (member.IsCharArray)
        {
            return AsText(data.GetAnyValue(member.Name));
        }

        switch (member.LeafKind)
        {
            case RtiTypeKind.Boolean:
                return data.GetValue<bool>(member.Name);

            case RtiTypeKind.Int16:
                return data.GetValue<short>(member.Name);

            case RtiTypeKind.Int32:
                return data.GetValue<int>(member.Name);

            case RtiTypeKind.Int64:
                return data.GetValue<long>(member.Name);

            case RtiTypeKind.Uint8:
                return data.GetValue<byte>(member.Name);

            case RtiTypeKind.Uint16:
                return data.GetValue<ushort>(member.Name);

            case RtiTypeKind.UInt32:
                return data.GetValue<uint>(member.Name);

            case RtiTypeKind.UInt64:
                return data.GetValue<ulong>(member.Name);

            case RtiTypeKind.Float32:
                return data.GetValue<float>(member.Name);

            case RtiTypeKind.Float64:
                return data.GetValue<double>(member.Name);

            case RtiTypeKind.Char8:
            case RtiTypeKind.Char16:
                return data.GetValue<char>(member.Name);

            case RtiTypeKind.String:
            case RtiTypeKind.WideString:
                return data.GetValue<string>(member.Name);

            case RtiTypeKind.Enumeration:
            {
                // Resolved here rather than at display time, matching PayloadSnapshot.FormatValue:
                // an element carries no schema of its own for the view to consult.
                var raw = data.GetValue<int>(member.Name);
                return member.EnumLabels != null && member.EnumLabels.TryGetValue(raw, out var label)
                    ? label + " (" + raw.ToString(CultureInfo.InvariantCulture) + ")"
                    : raw;
            }

            // Int8 and Octet are not switch labels because the 7.3.0 baseline does not define
            // them, the same reason ReadLeaf compares them by value.
            default:
                if (member.LeafKind == RtiKindCompat.Int8)
                {
                    return data.GetValue<sbyte>(member.Name);
                }

                return member.LeafKind == RtiKindCompat.Octet
                    ? data.GetValue<byte>(member.Name)
                    : data.GetAnyValue(member.Name);
        }
    }

    private object[] Materialize(object raw, int length)
    {
        if (raw is string text)
        {
            return new object[] { text };
        }

        if (raw is not IEnumerable enumerable)
        {
            // Not a shape we can enumerate. Recording the length alone is honest; presenting
            // the whole collection as its single element is not.
            return Array.Empty<object>();
        }

        var take = Math.Min(length, maxCollectionElements);
        var items = new List<object>(take);
        foreach (var element in enumerable)
        {
            if (items.Count == take)
            {
                break;
            }

            items.Add(element);
        }

        return items.ToArray();
    }
}
