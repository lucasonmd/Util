using System.Globalization;
using System.Text;

namespace DdsScope.Filtering;

public enum FilterTokenKind
{
    End,
    Identifier,
    Number,
    String,
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    And,
    Or,
    Not,
    LeftParen,
    RightParen,
    Contains,
    StartsWith,
    EndsWith
}

public readonly struct FilterToken
{
    public FilterToken(FilterTokenKind kind, string text, int position, double number = 0)
    {
        Kind = kind;
        Text = text;
        Position = position;
        Number = number;
    }

    public FilterTokenKind Kind { get; }

    public string Text { get; }

    public int Position { get; }

    public double Number { get; }

    public override string ToString() => $"{Kind}('{Text}')@{Position}";
}

/// <summary>Raised for any malformed filter. The UI shows the message and keeps the last good filter.</summary>
public sealed class FilterParseException : Exception
{
    public FilterParseException(string message, int position)
        : base(position >= 0 ? $"{message} (position {position + 1})" : message)
    {
        Position = position;
    }

    public int Position { get; }
}

/// <summary>
/// Turns filter text into tokens.
///
/// Word operators (contains/startsWith/endsWith/and/or/not) are recognised here so the
/// parser only ever deals with token kinds.
/// </summary>
public static class FilterLexer
{
    public static List<FilterToken> Tokenize(string text)
    {
        var tokens = new List<FilterToken>();
        if (string.IsNullOrWhiteSpace(text))
        {
            tokens.Add(new FilterToken(FilterTokenKind.End, string.Empty, 0));
            return tokens;
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;

            switch (c)
            {
                case '(':
                    tokens.Add(new FilterToken(FilterTokenKind.LeftParen, "(", start));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new FilterToken(FilterTokenKind.RightParen, ")", start));
                    i++;
                    continue;
                case '&' when i + 1 < text.Length && text[i + 1] == '&':
                    tokens.Add(new FilterToken(FilterTokenKind.And, "&&", start));
                    i += 2;
                    continue;
                case '|' when i + 1 < text.Length && text[i + 1] == '|':
                    tokens.Add(new FilterToken(FilterTokenKind.Or, "||", start));
                    i += 2;
                    continue;
                case '=' when i + 1 < text.Length && text[i + 1] == '=':
                    tokens.Add(new FilterToken(FilterTokenKind.Equal, "==", start));
                    i += 2;
                    continue;
                case '!' when i + 1 < text.Length && text[i + 1] == '=':
                    tokens.Add(new FilterToken(FilterTokenKind.NotEqual, "!=", start));
                    i += 2;
                    continue;
                case '!':
                    tokens.Add(new FilterToken(FilterTokenKind.Not, "!", start));
                    i++;
                    continue;
                case '>' when i + 1 < text.Length && text[i + 1] == '=':
                    tokens.Add(new FilterToken(FilterTokenKind.GreaterOrEqual, ">=", start));
                    i += 2;
                    continue;
                case '>':
                    tokens.Add(new FilterToken(FilterTokenKind.Greater, ">", start));
                    i++;
                    continue;
                case '<' when i + 1 < text.Length && text[i + 1] == '=':
                    tokens.Add(new FilterToken(FilterTokenKind.LessOrEqual, "<=", start));
                    i += 2;
                    continue;
                case '<':
                    tokens.Add(new FilterToken(FilterTokenKind.Less, "<", start));
                    i++;
                    continue;
                case '"':
                case '\'':
                    tokens.Add(ReadString(text, ref i));
                    continue;
            }

            if (char.IsDigit(c) || (c == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                tokens.Add(ReadNumber(text, ref i));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                tokens.Add(ReadIdentifier(text, ref i));
                continue;
            }

            throw new FilterParseException($"Unexpected character '{c}'", start);
        }

        tokens.Add(new FilterToken(FilterTokenKind.End, string.Empty, text.Length));
        return tokens;
    }

    private static FilterToken ReadString(string text, ref int i)
    {
        var quote = text[i];
        var start = i;
        i++;

        var builder = new StringBuilder();
        while (i < text.Length && text[i] != quote)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
            }

            builder.Append(text[i]);
            i++;
        }

        if (i >= text.Length)
        {
            throw new FilterParseException("Unterminated string literal", start);
        }

        i++;
        return new FilterToken(FilterTokenKind.String, builder.ToString(), start);
    }

    private static FilterToken ReadNumber(string text, ref int i)
    {
        var start = i;
        if (text[i] == '-')
        {
            i++;
        }

        var isHex = false;
        if (i + 1 < text.Length && text[i] == '0' && (text[i + 1] == 'x' || text[i + 1] == 'X'))
        {
            isHex = true;
            i += 2;
            while (i < text.Length && Uri.IsHexDigit(text[i]))
            {
                i++;
            }
        }
        else
        {
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
            {
                i++;
            }
        }

        var raw = text[start..i];
        double value;

        if (isHex)
        {
            var negative = raw[0] == '-';
            var digits = negative ? raw[3..] : raw[2..];
            if (digits.Length == 0 ||
                !long.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                throw new FilterParseException($"Invalid hexadecimal number '{raw}'", start);
            }

            value = negative ? -hex : hex;
        }
        else if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            throw new FilterParseException($"Invalid number '{raw}'", start);
        }

        return new FilterToken(FilterTokenKind.Number, raw, start, value);
    }

    private static FilterToken ReadIdentifier(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
        {
            i++;
        }

        var raw = text[start..i];

        return raw.ToLowerInvariant() switch
        {
            "and" => new FilterToken(FilterTokenKind.And, raw, start),
            "or" => new FilterToken(FilterTokenKind.Or, raw, start),
            "not" => new FilterToken(FilterTokenKind.Not, raw, start),
            "contains" => new FilterToken(FilterTokenKind.Contains, raw, start),
            "startswith" => new FilterToken(FilterTokenKind.StartsWith, raw, start),
            "endswith" => new FilterToken(FilterTokenKind.EndsWith, raw, start),
            _ => new FilterToken(FilterTokenKind.Identifier, raw, start)
        };
    }
}
