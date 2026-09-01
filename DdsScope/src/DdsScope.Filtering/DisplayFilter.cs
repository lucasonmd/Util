using DdsScope.Core.Capture;

namespace DdsScope.Filtering;

/// <summary>
/// A parsed, ready-to-run display filter.
///
/// Compilation happens once when the user edits the filter box; evaluation then costs one
/// virtual call per node with no string parsing and no field-name lookups.
/// </summary>
public sealed class DisplayFilter
{
    private readonly FilterNode root;

    private DisplayFilter(string text, FilterNode root)
    {
        Text = text;
        this.root = root;
    }

    /// <summary>Matches every record. Used when the filter box is empty.</summary>
    public static readonly DisplayFilter PassAll = new(string.Empty, AlwaysTrueNode.Instance);

    public string Text { get; }

    public bool IsEmpty => ReferenceEquals(root, AlwaysTrueNode.Instance);

    public bool Matches(CaptureRecord record) => root.Evaluate(record);

    /// <summary>Compiles filter text. Throws <see cref="FilterParseException"/> on bad input.</summary>
    public static DisplayFilter Compile(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return PassAll;
        }

        var tokens = FilterLexer.Tokenize(text);
        var parser = new FilterParser(tokens);
        var root = parser.ParseAll();
        return new DisplayFilter(text, root);
    }

    /// <summary>
    /// Compiles filter text, returning false plus a message instead of throwing. Used by the
    /// UI, where a half-typed filter must not disturb capture.
    /// </summary>
    public static bool TryCompile(string text, out DisplayFilter filter, out string error)
    {
        try
        {
            filter = Compile(text);
            error = null;
            return true;
        }
        catch (FilterParseException ex)
        {
            filter = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Combines an existing filter expression with another using AND, parenthesising the
    /// existing one when needed. Used by "filter on this value" in the sample detail tree.
    /// </summary>
    public static string AndWith(string existing, string addition)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return addition;
        }

        if (string.IsNullOrWhiteSpace(addition))
        {
            return existing;
        }

        var trimmed = existing.Trim();
        var needsParentheses = trimmed.Contains("||", StringComparison.Ordinal) ||
                               trimmed.Contains(" or ", StringComparison.OrdinalIgnoreCase);

        return needsParentheses ? $"({trimmed}) && {addition}" : $"{trimmed} && {addition}";
    }
}

/// <summary>Recursive-descent parser for the display-filter grammar.</summary>
internal sealed class FilterParser
{
    private readonly List<FilterToken> tokens;
    private int position;

    public FilterParser(List<FilterToken> tokens)
    {
        this.tokens = tokens;
    }

    private FilterToken Current => tokens[position];

    public FilterNode ParseAll()
    {
        var node = ParseOr();
        if (Current.Kind != FilterTokenKind.End)
        {
            throw new FilterParseException($"Unexpected '{Current.Text}'", Current.Position);
        }

        return node;
    }

    private FilterNode ParseOr()
    {
        var left = ParseAnd();
        while (Current.Kind == FilterTokenKind.Or)
        {
            position++;
            left = new OrNode(left, ParseAnd());
        }

        return left;
    }

    private FilterNode ParseAnd()
    {
        var left = ParseUnary();
        while (Current.Kind == FilterTokenKind.And)
        {
            position++;
            left = new AndNode(left, ParseUnary());
        }

        return left;
    }

    private FilterNode ParseUnary()
    {
        if (Current.Kind == FilterTokenKind.Not)
        {
            position++;
            return new NotNode(ParseUnary());
        }

        return ParsePrimary();
    }

    private FilterNode ParsePrimary()
    {
        if (Current.Kind == FilterTokenKind.LeftParen)
        {
            position++;
            var inner = ParseOr();
            if (Current.Kind != FilterTokenKind.RightParen)
            {
                throw new FilterParseException("Expected ')'", Current.Position);
            }

            position++;
            return inner;
        }

        if (Current.Kind != FilterTokenKind.Identifier)
        {
            throw new FilterParseException(
                Current.Kind == FilterTokenKind.End ? "Unexpected end of filter" : $"Expected a field name but found '{Current.Text}'",
                Current.Position);
        }

        var fieldToken = Current;
        position++;

        var accessor = FilterFields.Resolve(fieldToken.Text, fieldToken.Position);

        if (!IsComparisonOperator(Current.Kind))
        {
            // A bare field reference is a presence test.
            return new PresenceNode(accessor);
        }

        var op = Current.Kind;
        position++;

        var literal = ParseLiteral();
        return new ComparisonNode(accessor, op, literal);
    }

    private FilterLiteral ParseLiteral()
    {
        var token = Current;
        switch (token.Kind)
        {
            case FilterTokenKind.Number:
                position++;
                return FilterLiteral.FromNumber(token.Number, token.Text);

            case FilterTokenKind.String:
                position++;
                return FilterLiteral.FromText(token.Text);

            case FilterTokenKind.Identifier:
                position++;
                // Unquoted words are literals too, so `data.Status == Enabled` works.
                return token.Text.Equals("true", StringComparison.OrdinalIgnoreCase)
                    ? FilterLiteral.FromNumber(1, "true")
                    : token.Text.Equals("false", StringComparison.OrdinalIgnoreCase)
                        ? FilterLiteral.FromNumber(0, "false")
                        : FilterLiteral.FromText(token.Text);

            default:
                throw new FilterParseException(
                    token.Kind == FilterTokenKind.End ? "Expected a value" : $"Expected a value but found '{token.Text}'",
                    token.Position);
        }
    }

    private static bool IsComparisonOperator(FilterTokenKind kind) => kind switch
    {
        FilterTokenKind.Equal => true,
        FilterTokenKind.NotEqual => true,
        FilterTokenKind.Greater => true,
        FilterTokenKind.GreaterOrEqual => true,
        FilterTokenKind.Less => true,
        FilterTokenKind.LessOrEqual => true,
        FilterTokenKind.Contains => true,
        FilterTokenKind.StartsWith => true,
        FilterTokenKind.EndsWith => true,
        _ => false
    };
}

/// <summary>Maps filter field names onto accessors.</summary>
public static class FilterFields
{
    public const string PayloadPrefix = "data.";

    /// <summary>Names offered by filter auto-completion, besides discovered payload fields.</summary>
    public static IReadOnlyList<string> BuiltinNames { get; } = new[]
    {
        "topic", "writer", "writerid", "type", "key", "seq"
    };

    internal static IFieldAccessor Resolve(string name, int position)
    {
        if (name.StartsWith(PayloadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var path = name[PayloadPrefix.Length..];
            if (path.Length == 0)
            {
                throw new FilterParseException("Expected a payload field after 'data.'", position);
            }

            return new PayloadFieldAccessor(path);
        }

        return name.ToLowerInvariant() switch
        {
            "topic" => new TopicAccessor(),
            "writer" => new WriterAccessor(),
            "writerid" => new WriterIdAccessor(),
            "type" => new TypeNameAccessor(),
            "key" => new InstanceKeyAccessor(),
            "seq" => new SequenceAccessor(),
            _ => throw new FilterParseException(
                $"Unknown field '{name}'. Use topic, writer, writerid, type, key, seq or data.<field>",
                position)
        };
    }
}
