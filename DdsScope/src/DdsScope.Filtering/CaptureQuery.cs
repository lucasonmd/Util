using System.Globalization;
using DdsScope.Core.Capture;
using DdsScope.Core.Payload;

namespace DdsScope.Filtering;

/// <summary>
/// The topic/writer chosen in the tree.
///
/// Kept separate from the display filter on purpose: clicking a topic must never rewrite
/// the filter expression the user typed.
/// </summary>
public sealed class SelectionFilter
{
    public static readonly SelectionFilter All = new(null, null);

    public SelectionFilter(string topicName, string writerId)
    {
        TopicName = topicName;
        WriterId = writerId;
    }

    /// <summary>Null means every topic.</summary>
    public string TopicName { get; }

    /// <summary>Null means every writer of <see cref="TopicName"/>.</summary>
    public string WriterId { get; }

    public bool IsAll => TopicName == null && WriterId == null;

    public bool Matches(CaptureRecord record)
    {
        if (TopicName != null &&
            !string.Equals(record.TopicName, TopicName, StringComparison.Ordinal))
        {
            return false;
        }

        if (WriterId != null &&
            !string.Equals(record.Writer?.Id, WriterId, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    public override string ToString() =>
        IsAll ? "All topics" : WriterId == null ? TopicName : $"{TopicName} / {WriterId}";
}

/// <summary>
/// Everything that decides whether a captured record is displayed:
/// selection AND display filter AND quick search.
///
/// Instances are immutable and evaluated on the single projection task, so the schema
/// caches inside do not need synchronisation.
/// </summary>
public sealed class CaptureQuery
{
    private readonly Dictionary<int, bool> fieldNameHitBySchema = new();
    private readonly string searchTerm;
    private readonly bool searchIsNumeric;
    private readonly double searchNumber;

    public CaptureQuery(SelectionFilter selection, DisplayFilter display, string quickSearch)
    {
        Selection = selection ?? SelectionFilter.All;
        Display = display ?? DisplayFilter.PassAll;
        QuickSearch = quickSearch;

        searchTerm = string.IsNullOrWhiteSpace(quickSearch) ? null : quickSearch.Trim();
        searchIsNumeric = searchTerm != null &&
                          double.TryParse(searchTerm, NumberStyles.Float, CultureInfo.InvariantCulture, out searchNumber);
    }

    public static readonly CaptureQuery MatchAll = new(SelectionFilter.All, DisplayFilter.PassAll, null);

    public SelectionFilter Selection { get; }

    public DisplayFilter Display { get; }

    public string QuickSearch { get; }

    public bool IsMatchAll => Selection.IsAll && Display.IsEmpty && searchTerm == null;

    public bool Matches(CaptureRecord record)
    {
        if (!Selection.Matches(record))
        {
            return false;
        }

        if (!Display.Matches(record))
        {
            return false;
        }

        return searchTerm == null || QuickSearchMatches(record);
    }

    /// <summary>
    /// Quick search deliberately does not format every numeric field of every record.
    /// It matches topic / writer / key / payload field names always, string and enum values
    /// always, and numeric values only when the search term itself parses as a number.
    /// That keeps a keystroke in the search box from costing a full re-render of the store.
    /// </summary>
    private bool QuickSearchMatches(CaptureRecord record)
    {
        if (Contains(record.TopicName) ||
            Contains(record.Writer?.DisplayName) ||
            Contains(record.Writer?.TypeName))
        {
            return true;
        }

        var payload = record.Payload;
        var schema = payload?.Schema;
        if (schema == null)
        {
            return false;
        }

        if (MatchesAnyFieldName(schema))
        {
            return true;
        }

        for (var i = 0; i < schema.Fields.Count; i++)
        {
            var field = schema.Fields[i];
            if (!payload.IsPresent(i))
            {
                continue;
            }

            switch (field.Kind)
            {
                case PayloadValueKind.String:
                    if (payload.TryGetString(i, out var text) && Contains(text))
                    {
                        return true;
                    }

                    break;

                case PayloadValueKind.Enum:
                    if (payload.TryGetInt64(i, out var raw))
                    {
                        if (field.EnumLabels != null &&
                            field.EnumLabels.TryGetValue(raw, out var label) &&
                            Contains(label))
                        {
                            return true;
                        }

                        if (searchIsNumeric && Math.Abs(raw - searchNumber) < double.Epsilon)
                        {
                            return true;
                        }
                    }

                    break;

                default:
                    if (searchIsNumeric &&
                        payload.TryGetDouble(i, out var value) &&
                        Math.Abs(value - searchNumber) < 1e-9)
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private bool MatchesAnyFieldName(PayloadSchema schema)
    {
        if (fieldNameHitBySchema.TryGetValue(schema.Id, out var cached))
        {
            return cached;
        }

        var hit = false;
        foreach (var field in schema.Fields)
        {
            if (Contains(field.Path))
            {
                hit = true;
                break;
            }
        }

        fieldNameHitBySchema[schema.Id] = hit;
        return hit;
    }

    private bool Contains(string value) =>
        value != null && value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
}
