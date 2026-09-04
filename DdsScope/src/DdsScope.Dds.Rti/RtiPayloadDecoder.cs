using System.Collections;
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
            builder.SetCollection(field, new CollectionValue(length, Array.Empty<object>(), true));
            return;
        }

        object[] items;
        try
        {
            items = Materialize(data.GetAnyValue(node.Name), length);
        }
        catch (Exception)
        {
            // A collection we cannot read must not cost us the rest of the sample.
            builder.SetCollection(field, new CollectionValue(length, Array.Empty<object>(), true));
            return;
        }

        builder.SetCollection(field, new CollectionValue(length, items, items.Length < length));
    }

    private object[] Materialize(object raw, int length)
    {
        if (raw is not IEnumerable enumerable || raw is string)
        {
            return raw == null ? Array.Empty<object>() : new[] { raw };
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
