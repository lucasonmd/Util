using System.Globalization;
using DdsScope.Core.Capture;

namespace DdsScope.Filtering;

/// <summary>A node of the compiled display-filter tree.</summary>
public abstract class FilterNode
{
    public abstract bool Evaluate(CaptureRecord record);
}

public sealed class AlwaysTrueNode : FilterNode
{
    public static readonly AlwaysTrueNode Instance = new();

    public override bool Evaluate(CaptureRecord record) => true;
}

public sealed class AndNode : FilterNode
{
    public AndNode(FilterNode left, FilterNode right)
    {
        Left = left;
        Right = right;
    }

    public FilterNode Left { get; }

    public FilterNode Right { get; }

    public override bool Evaluate(CaptureRecord record) =>
        Left.Evaluate(record) && Right.Evaluate(record);
}

public sealed class OrNode : FilterNode
{
    public OrNode(FilterNode left, FilterNode right)
    {
        Left = left;
        Right = right;
    }

    public FilterNode Left { get; }

    public FilterNode Right { get; }

    public override bool Evaluate(CaptureRecord record) =>
        Left.Evaluate(record) || Right.Evaluate(record);
}

public sealed class NotNode : FilterNode
{
    public NotNode(FilterNode operand)
    {
        Operand = operand;
    }

    public FilterNode Operand { get; }

    public override bool Evaluate(CaptureRecord record) => !Operand.Evaluate(record);
}

/// <summary>A bare field reference: true when the record actually carries that field.</summary>
public sealed class PresenceNode : FilterNode
{
    private readonly IFieldAccessor accessor;

    public PresenceNode(IFieldAccessor accessor)
    {
        this.accessor = accessor;
    }

    public override bool Evaluate(CaptureRecord record) => accessor.IsPresent(record);
}

/// <summary>A literal on the right-hand side of a comparison.</summary>
public readonly struct FilterLiteral
{
    private FilterLiteral(bool hasNumber, double number, string text)
    {
        HasNumber = hasNumber;
        Number = number;
        Text = text;
    }

    public bool HasNumber { get; }

    public double Number { get; }

    public string Text { get; }

    public static FilterLiteral FromNumber(double value, string text) =>
        new(true, value, text ?? value.ToString(CultureInfo.InvariantCulture));

    public static FilterLiteral FromText(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? new FilterLiteral(true, parsed, text)
            : new FilterLiteral(false, 0, text);
}

/// <summary>
/// A comparison between a field and a literal.
///
/// A field that is missing from the record - an inactive union branch, or a field this
/// topic simply does not have - makes the comparison false rather than an error, so a
/// filter written for one topic never breaks the all-topics view.
/// </summary>
public sealed class ComparisonNode : FilterNode
{
    private readonly IFieldAccessor accessor;
    private readonly FilterTokenKind op;
    private readonly FilterLiteral literal;

    public ComparisonNode(IFieldAccessor accessor, FilterTokenKind op, FilterLiteral literal)
    {
        this.accessor = accessor;
        this.op = op;
        this.literal = literal;
    }

    public override bool Evaluate(CaptureRecord record)
    {
        switch (op)
        {
            case FilterTokenKind.Contains:
                return TextOperation(record, (a, b) => a.Contains(b, StringComparison.OrdinalIgnoreCase));
            case FilterTokenKind.StartsWith:
                return TextOperation(record, (a, b) => a.StartsWith(b, StringComparison.OrdinalIgnoreCase));
            case FilterTokenKind.EndsWith:
                return TextOperation(record, (a, b) => a.EndsWith(b, StringComparison.OrdinalIgnoreCase));
        }

        if (literal.HasNumber && accessor.TryGetNumber(record, out var number))
        {
            return Compare(number.CompareTo(literal.Number));
        }

        if (!accessor.TryGetText(record, out var text) || text == null)
        {
            return false;
        }

        return Compare(string.Compare(text, literal.Text, StringComparison.OrdinalIgnoreCase));
    }

    private bool TextOperation(CaptureRecord record, Func<string, string, bool> operation)
    {
        if (!accessor.TryGetText(record, out var text) || text == null)
        {
            return false;
        }

        return operation(text, literal.Text ?? string.Empty);
    }

    private bool Compare(int comparison) => op switch
    {
        FilterTokenKind.Equal => comparison == 0,
        FilterTokenKind.NotEqual => comparison != 0,
        FilterTokenKind.Greater => comparison > 0,
        FilterTokenKind.GreaterOrEqual => comparison >= 0,
        FilterTokenKind.Less => comparison < 0,
        FilterTokenKind.LessOrEqual => comparison <= 0,
        _ => false
    };
}
