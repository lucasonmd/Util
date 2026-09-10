using DdsScope.Core.Payload;
using Rti.Types.Dynamic;
using RtiTypeKind = Omg.Types.Dynamic.TypeKind;

namespace DdsScope.Dds.Rti;

internal enum ReadNodeKind
{
    Struct,
    Union,
    Leaf,
    Collection,

    /// <summary><c>char[N]</c> and <c>sequence&lt;char&gt;</c>, captured as text.</summary>
    CharArray
}

/// <summary>
/// One member of a collection's element type.
///
/// A sequence of primitives is read as a block, but a <c>sequence&lt;SourceId&gt;</c> cannot be:
/// its elements are aggregates that have to be loaned and walked one at a time. The element
/// type therefore gets its own small plan, built once with the schema.
/// </summary>
internal sealed class ElementNode
{
    public string Name { get; init; }

    /// <summary>True when the member is itself a struct or union and must be loaned.</summary>
    public bool IsAggregate { get; init; }

    /// <summary>True when the member is <c>char[N]</c> or <c>sequence&lt;char&gt;</c>.</summary>
    public bool IsCharArray { get; init; }

    /// <summary>Resolved IDL kind of a leaf member.</summary>
    public RtiTypeKind LeafKind { get; init; }

    /// <summary>Label table for enum members; null otherwise.</summary>
    public IReadOnlyDictionary<long, string> EnumLabels { get; init; }

    public ElementNode[] Children { get; init; } = Array.Empty<ElementNode>();
}

/// <summary>
/// One step of the decode plan for a payload type.
///
/// The plan is built once per discovered type. Decoding a sample then walks a small tree of
/// these instead of interpreting the DynamicType again for every sample.
/// </summary>
internal sealed class ReadNode
{
    public string Name { get; init; }

    public ReadNodeKind Kind { get; init; }

    /// <summary>Target field for leaf and collection nodes.</summary>
    public PayloadField Field { get; init; }

    /// <summary>Resolved IDL kind of a leaf, so decoding is a switch and not a type test.</summary>
    public RtiTypeKind LeafKind { get; init; }

    public ReadNode[] Children { get; init; } = Array.Empty<ReadNode>();

    /// <summary>Populated for unions, where only the active member is decoded.</summary>
    public Dictionary<string, ReadNode> ChildrenByName { get; init; }

    /// <summary>
    /// Members of the element type, for a collection whose elements are structs or unions.
    /// Null for collections of primitives, which are read as a block.
    /// </summary>
    public ElementNode[] ElementMembers { get; init; }
}

/// <summary>A discovered type: the neutral schema plus the plan used to fill it.</summary>
public sealed class RtiTypeSchema
{
    internal RtiTypeSchema(PayloadSchema schema, ReadNode root)
    {
        Schema = schema;
        Root = root;
    }

    public PayloadSchema Schema { get; }

    internal ReadNode Root { get; }
}

/// <summary>
/// Flattens an RTI <see cref="DynamicType"/> into a <see cref="PayloadSchema"/>.
///
/// This is where the vendor type system stops: nothing above the adapter ever sees a
/// DynamicType again.
/// </summary>
public static class RtiSchemaBuilder
{
    /// <summary>Guards against pathological or recursive type graphs.</summary>
    private const int MaxDepth = 12;

    public static RtiTypeSchema Build(DynamicType type)
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        var state = new BuildState();
        var resolved = Resolve(type);

        ReadNode root;
        if (resolved is StructType structType)
        {
            root = BuildStructNode(null, structType, string.Empty, state, 0, false);
        }
        else if (resolved is UnionType unionType)
        {
            root = BuildUnionNode(null, unionType, string.Empty, state, 0, true);
        }
        else
        {
            // A non-aggregated top-level type still deserves a single "value" field.
            var field = state.AddField("value", "value", resolved, false, false);
            root = new ReadNode
            {
                Name = null,
                Kind = ReadNodeKind.Struct,
                Children = new[] { MakeLeafOrCollection("value", resolved, field) }
            };
        }

        var schema = new PayloadSchema(type.Name, state.Fields);
        return new RtiTypeSchema(schema, root);
    }

    private static DynamicType Resolve(DynamicType type)
    {
        var depth = 0;
        while (type is AliasType alias && depth++ < MaxDepth)
        {
            type = alias.RelatedType as DynamicType;
            if (type == null)
            {
                break;
            }
        }

        return type;
    }

    private static ReadNode BuildStructNode(
        string memberName,
        StructType type,
        string pathPrefix,
        BuildState state,
        int depth,
        bool optional)
    {
        var children = new List<ReadNode>();

        if (depth < MaxDepth)
        {
            foreach (var member in type.Members)
            {
                var child = BuildMemberNode(
                    member.Name,
                    member.Type,
                    pathPrefix,
                    state,
                    depth + 1,
                    member.IsKey,
                    optional || member.IsOptional);

                if (child != null)
                {
                    children.Add(child);
                }
            }
        }

        return new ReadNode
        {
            Name = memberName,
            Kind = ReadNodeKind.Struct,
            Children = children.ToArray()
        };
    }

    private static ReadNode BuildUnionNode(
        string memberName,
        UnionType type,
        string pathPrefix,
        BuildState state,
        int depth,
        bool optional)
    {
        var children = new List<ReadNode>();
        var byName = new Dictionary<string, ReadNode>(StringComparer.Ordinal);

        if (depth < MaxDepth)
        {
            foreach (var member in type.Members)
            {
                // Every branch gets a field, but only the active one is ever marked present.
                var child = BuildMemberNode(
                    member.Name,
                    member.Type,
                    pathPrefix,
                    state,
                    depth + 1,
                    false,
                    true);

                if (child != null)
                {
                    children.Add(child);
                    byName[member.Name] = child;
                }
            }
        }

        return new ReadNode
        {
            Name = memberName,
            Kind = ReadNodeKind.Union,
            Children = children.ToArray(),
            ChildrenByName = byName
        };
    }

    private static ReadNode BuildMemberNode(
        string name,
        DynamicType memberType,
        string pathPrefix,
        BuildState state,
        int depth,
        bool isKey,
        bool optional)
    {
        var resolved = Resolve(memberType);
        if (resolved == null)
        {
            return null;
        }

        var path = pathPrefix.Length == 0 ? name : pathPrefix + "." + name;

        switch (resolved.Kind)
        {
            case RtiTypeKind.Structure:
                return BuildStructNode(name, (StructType)resolved, path, state, depth, optional);

            case RtiTypeKind.Union:
                return BuildUnionNode(name, (UnionType)resolved, path, state, depth, optional);

            default:
            {
                var field = state.AddField(path, name, resolved, isKey, optional);
                return field == null ? null : MakeLeafOrCollection(name, resolved, field);
            }
        }
    }

    private static ReadNode MakeLeafOrCollection(string name, DynamicType type, PayloadField field)
    {
        if (IsCharacterArray(type))
        {
            return new ReadNode
            {
                Name = name,
                Kind = ReadNodeKind.CharArray,
                Field = field,
                LeafKind = type.Kind
            };
        }

        var isCollection = type.Kind is RtiTypeKind.Array or RtiTypeKind.Sequence;

        return new ReadNode
        {
            Name = name,
            Kind = isCollection ? ReadNodeKind.Collection : ReadNodeKind.Leaf,
            Field = field,
            LeafKind = type.Kind,
            ElementMembers = isCollection ? BuildElementMembers(ElementTypeOf(type), 0) : null
        };
    }

    /// <summary>
    /// True for <c>char[N]</c> and <c>sequence&lt;char&gt;</c>.
    ///
    /// IDL uses a char array where other languages use a string, so one is captured as text
    /// rather than as a list of letters: <c>abc</c>, not <c>[a, b, c]</c>. That also makes it
    /// filterable and exportable the way an IDL string already is.
    /// </summary>
    private static bool IsCharacterArray(DynamicType type)
    {
        if (type.Kind is not (RtiTypeKind.Array or RtiTypeKind.Sequence))
        {
            return false;
        }

        var element = ElementTypeOf(type);
        return element != null && element.Kind is RtiTypeKind.Char8 or RtiTypeKind.Char16;
    }

    /// <summary>The element type of an array or sequence, with aliases resolved.</summary>
    private static DynamicType ElementTypeOf(DynamicType type) => type switch
    {
        SequenceType sequence => Resolve(sequence.ContentType as DynamicType),
        ArrayType array => Resolve(array.ContentType as DynamicType),
        _ => null
    };

    /// <summary>
    /// The plan for one element of a collection of aggregates, or null when the elements are
    /// primitives - read as a block - or of a shape with no useful flat rendering. Null is what
    /// tells the decoder to take the cheap block path.
    /// </summary>
    private static ElementNode[] BuildElementMembers(DynamicType elementType, int depth)
    {
        if (depth >= MaxDepth)
        {
            return null;
        }

        var members = MembersOf(elementType);
        if (members == null)
        {
            return null;
        }

        var nodes = new List<ElementNode>(members.Count);
        foreach (var member in members)
        {
            var resolved = Resolve(member.Value);
            if (resolved == null)
            {
                continue;
            }

            if (resolved.Kind is RtiTypeKind.Structure or RtiTypeKind.Union)
            {
                var children = BuildElementMembers(resolved, depth + 1);
                if (children == null)
                {
                    continue;
                }

                nodes.Add(new ElementNode
                {
                    Name = member.Key,
                    IsAggregate = true,
                    Children = children
                });

                continue;
            }

            if (IsCharacterArray(resolved))
            {
                nodes.Add(new ElementNode { Name = member.Key, IsCharArray = true });
                continue;
            }

            // Any other collection nested inside an element is left out on purpose: the detail
            // pane shows one level of elements, and a sequence of sequences has no flat rendering.
            if (resolved.Kind is RtiTypeKind.Array or RtiTypeKind.Sequence ||
                MapKind(resolved.Kind) == PayloadValueKind.Unsupported)
            {
                continue;
            }

            nodes.Add(new ElementNode
            {
                Name = member.Key,
                LeafKind = resolved.Kind,
                EnumLabels = resolved.Kind == RtiTypeKind.Enumeration ? BuildEnumLabels(resolved) : null
            });
        }

        return nodes.Count == 0 ? null : nodes.ToArray();
    }

    /// <summary>
    /// Name/type pairs of a struct or union, or null for anything else. Struct and union
    /// members are unrelated types in the RTI API, so they are reduced to a common shape here
    /// rather than at every call site.
    /// </summary>
    private static List<KeyValuePair<string, DynamicType>> MembersOf(DynamicType type)
    {
        var members = new List<KeyValuePair<string, DynamicType>>();

        switch (type)
        {
            case StructType structType:
                foreach (var member in structType.Members)
                {
                    members.Add(new KeyValuePair<string, DynamicType>(member.Name, member.Type));
                }

                break;

            case UnionType unionType:
                foreach (var member in unionType.Members)
                {
                    members.Add(new KeyValuePair<string, DynamicType>(member.Name, member.Type));
                }

                break;

            default:
                return null;
        }

        return members;
    }

    private static IReadOnlyDictionary<long, string> BuildEnumLabels(DynamicType type)
    {
        if (type is not EnumType enumType)
        {
            return null;
        }

        var labels = new Dictionary<long, string>();
        foreach (var member in enumType.Members)
        {
            labels[member.Ordinal] = member.Name;
        }

        return labels;
    }

    internal static PayloadValueKind MapKind(RtiTypeKind kind)
    {
        // Int8 and Octet cannot be pattern labels: the 7.3.0 baseline does not define them.
        // An unmapped kind would drop the field from the schema entirely, so they are matched
        // by value first rather than left to the Unsupported arm.
        if (kind == RtiKindCompat.Int8)
        {
            return PayloadValueKind.Int;
        }

        if (kind == RtiKindCompat.Octet)
        {
            return PayloadValueKind.UInt;
        }

        return kind switch
        {
            RtiTypeKind.Boolean => PayloadValueKind.Bool,
            RtiTypeKind.Int16 or RtiTypeKind.Int32 or RtiTypeKind.Int64 => PayloadValueKind.Int,
            RtiTypeKind.Uint8 or RtiTypeKind.Uint16 or RtiTypeKind.UInt32 or RtiTypeKind.UInt64 => PayloadValueKind.UInt,
            RtiTypeKind.Float32 or RtiTypeKind.Float64 or RtiTypeKind.Float128 => PayloadValueKind.Float,
            RtiTypeKind.Char8 or RtiTypeKind.Char16 => PayloadValueKind.Char,
            RtiTypeKind.Enumeration => PayloadValueKind.Enum,
            RtiTypeKind.String or RtiTypeKind.WideString => PayloadValueKind.String,
            RtiTypeKind.Array or RtiTypeKind.Sequence => PayloadValueKind.Collection,
            _ => PayloadValueKind.Unsupported
        };
    }

    /// <summary>Accumulates fields and hands out slot indices while the plan is built.</summary>
    private sealed class BuildState
    {
        private int numericSlots;
        private int textSlots;
        private int collectionSlots;

        public List<PayloadField> Fields { get; } = new();

        public PayloadField AddField(string path, string name, DynamicType type, bool isKey, bool optional)
        {
            var kind = MapKind(type.Kind);
            if (kind == PayloadValueKind.Unsupported)
            {
                return null;
            }

            if (kind == PayloadValueKind.Collection && IsCharacterArray(type))
            {
                kind = PayloadValueKind.String;
            }

            var slot = kind switch
            {
                PayloadValueKind.String => PayloadSlot.Text,
                PayloadValueKind.Collection => PayloadSlot.Collection,
                _ => PayloadSlot.Numeric
            };

            var slotIndex = slot switch
            {
                PayloadSlot.Text => textSlots++,
                PayloadSlot.Collection => collectionSlots++,
                _ => numericSlots++
            };

            var field = new PayloadField
            {
                Index = Fields.Count,
                Path = path,
                Name = name,
                Kind = kind,
                Slot = slot,
                SlotIndex = slotIndex,
                IsKey = isKey,
                IsOptional = optional,
                TypeName = DescribeType(type),
                EnumLabels = kind == PayloadValueKind.Enum ? BuildEnumLabels(type) : null
            };

            Fields.Add(field);
            return field;
        }

        private static string DescribeType(DynamicType type)
        {
            switch (type)
            {
                case SequenceType sequence:
                    return $"sequence<{(sequence.ContentType as DynamicType)?.Name ?? "?"}>";
                case ArrayType array:
                    return $"{(array.ContentType as DynamicType)?.Name ?? "?"}[{array.TotalElementCount}]";
                default:
                    return type.Name;
            }
        }
    }
}
