using System.Collections.ObjectModel;
using System.Globalization;
using DdsScope.App.Infrastructure;
using DdsScope.Core.Capture;
using DdsScope.Core.Payload;

namespace DdsScope.App.ViewModels;

/// <summary>
/// A node of the payload tree in the detail pane.
///
/// Collection elements are not turned into nodes until the user expands them, so selecting a
/// sample that carries a large sequence costs nothing until it is actually opened.
/// </summary>
public sealed class SampleDetailNode : ObservableObject
{
    private bool isExpanded;
    private bool childrenRealised;
    private Func<IEnumerable<SampleDetailNode>> lazyChildren;

    private SampleDetailNode(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string Value { get; private set; }

    public string TypeName { get; private set; }

    /// <summary>Filter expression for this node, or null when it cannot be filtered on.</summary>
    public string FilterPath { get; private set; }

    public ObservableCollection<SampleDetailNode> Children { get; } = new();

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (!Set(ref isExpanded, value) || !value)
            {
                return;
            }

            Realise();
        }
    }

    private void Realise()
    {
        if (childrenRealised || lazyChildren == null)
        {
            return;
        }

        childrenRealised = true;
        Children.Clear();
        foreach (var child in lazyChildren())
        {
            Children.Add(child);
        }

        lazyChildren = null;
    }

    /// <summary>Builds the tree for one captured sample.</summary>
    public static IReadOnlyList<SampleDetailNode> Build(CaptureRecord record)
    {
        var roots = new List<SampleDetailNode>();
        if (record == null)
        {
            return roots;
        }

        if ((record.Flags & CaptureFlags.TypeUnavailable) != 0)
        {
            roots.Add(new SampleDetailNode("Payload") { Value = "type unavailable" });
            return roots;
        }

        var payload = record.Payload;
        var schema = payload?.Schema;
        if (schema == null || schema.Fields.Count == 0)
        {
            roots.Add(new SampleDetailNode("Payload") { Value = "(no fields)" });
            return roots;
        }

        var groups = new Dictionary<string, SampleDetailNode>(StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            if (!payload.IsPresent(field.Index))
            {
                continue;
            }

            var parent = EnsureGroup(field.Path, groups, roots);
            var node = new SampleDetailNode(field.Name)
            {
                TypeName = field.TypeName,
                FilterPath = "data." + field.Path
            };

            if (field.Kind == PayloadValueKind.Collection)
            {
                var collection = payload.GetCollection(field.Index);
                node.Value = collection?.ToString() ?? "[]";
                node.lazyChildren = () => BuildCollectionChildren(collection);
                if (collection != null && collection.Length > 0)
                {
                    // A placeholder gives the node an expander; Realise() replaces it.
                    node.Children.Add(new SampleDetailNode("...") { Value = "expand to load" });
                }
            }
            else
            {
                node.Value = payload.FormatValue(field.Index);
            }

            if (parent == null)
            {
                roots.Add(node);
            }
            else
            {
                parent.Children.Add(node);
            }
        }

        if ((record.Flags & CaptureFlags.DecodeFailed) != 0)
        {
            roots.Insert(0, new SampleDetailNode("Decode error") { Value = record.Error });
        }

        return roots;
    }

    private static IEnumerable<SampleDetailNode> BuildCollectionChildren(CollectionValue collection)
    {
        if (collection == null)
        {
            yield break;
        }

        for (var i = 0; i < collection.Items.Count; i++)
        {
            yield return new SampleDetailNode("[" + i.ToString(CultureInfo.InvariantCulture) + "]")
            {
                Value = Convert.ToString(collection.Items[i], CultureInfo.InvariantCulture)
            };
        }

        if (collection.Truncated)
        {
            yield return new SampleDetailNode("...")
            {
                Value = $"{collection.Length} elements on the wire; only the first {collection.Items.Count} were captured"
            };
        }
    }

    /// <summary>Creates the struct nodes a dotted path implies, reusing ones already made.</summary>
    private static SampleDetailNode EnsureGroup(
        string path,
        Dictionary<string, SampleDetailNode> groups,
        List<SampleDetailNode> roots)
    {
        var lastDot = path.LastIndexOf('.');
        if (lastDot < 0)
        {
            return null;
        }

        var groupPath = path[..lastDot];
        if (groups.TryGetValue(groupPath, out var existing))
        {
            return existing;
        }

        var name = groupPath;
        var parentDot = groupPath.LastIndexOf('.');
        SampleDetailNode parent = null;
        if (parentDot >= 0)
        {
            name = groupPath[(parentDot + 1)..];
            parent = EnsureGroup(groupPath, groups, roots);
        }

        var node = new SampleDetailNode(name) { Value = string.Empty, isExpanded = true, childrenRealised = true };
        groups[groupPath] = node;

        if (parent == null)
        {
            roots.Add(node);
        }
        else
        {
            parent.Children.Add(node);
        }

        return node;
    }
}
