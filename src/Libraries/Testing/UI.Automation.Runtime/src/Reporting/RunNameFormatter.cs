using System.Globalization;
using System.Text;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

// Resolves the placeholders of a run name: {RunId}, {Date}, {Date:format}, {Time}, {Time:format}, {Profile},
// {Provider}, {Engine}, {Device}, {Branch}, {Sha7}, {BuildNumber} and {Machine}; names are case-insensitive, {{ and }}
// are literal braces. {Date} is yyyy-MM-dd and {Time} HH:mm unless a .NET date format follows the colon. An unknown
// value is empty and, in a name, the spaces it leaves collapse, so "UI automation {Date} {Profile}" without a profile
// has no trailing space. An unknown placeholder stays as written (Validate reports it).
public static class RunNameFormatter
{
    private const string DefaultDateFormat = "yyyy-MM-dd";
    private const string DefaultTimeFormat = "HH:mm";

    public static IReadOnlyList<string> Placeholders { get; } =
        ["RunId", "Date", "Time", "Profile", "Provider", "Engine", "Device", "Branch", "Sha7", "BuildNumber", "Machine"];

    // collapseSpaces: false for multi-line text such as a markdown description.
    public static string Format(string pattern, RunNameValues values, bool collapseSpaces = true)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(values);

        var builder = new StringBuilder(pattern.Length + 32);
        foreach (var token in Parse(pattern, errors: null))
        {
            builder.Append(token.IsPlaceholder ? Resolve(token, values) : token.Text);
        }

        return collapseSpaces ? CollapseSpaces(builder.ToString()) : builder.ToString();
    }

    // The problems of a pattern, empty when it is fine.
    public static IReadOnlyList<string> Validate(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var errors = new List<string>();
        foreach (var token in Parse(pattern, errors).Where(token => token.IsPlaceholder))
        {
            var name = Placeholders.FirstOrDefault(known => string.Equals(known, token.Name, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                errors.Add($"the placeholder {{{token.Name}}} is unknown; use one of {string.Join(", ", Placeholders.Select(known => $"{{{known}}}"))}.");
            }
            else if (token.Format is not null && name is not ("Date" or "Time"))
            {
                errors.Add($"the placeholder {{{name}}} takes no format.");
            }
            else if (token.Format is not null)
            {
                try
                {
                    _ = DateTimeOffset.UnixEpoch.ToString(token.Format, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    errors.Add($"'{token.Format}' of {{{name}:{token.Format}}} is not a date or time format.");
                }
            }
        }

        return errors;
    }

    private static string Resolve(Token token, RunNameValues values)
    {
        switch (token.Name.ToUpperInvariant())
        {
            case "RUNID":
                return values.RunId;
            case "DATE":
                return FormatDate(values.StartedAt, token.Format ?? DefaultDateFormat);
            case "TIME":
                return FormatDate(values.StartedAt, token.Format ?? DefaultTimeFormat);
            case "PROFILE":
                return values.Profile ?? string.Empty;
            case "PROVIDER":
                return values.Provider ?? string.Empty;
            case "ENGINE":
                return values.Engine ?? string.Empty;
            case "DEVICE":
                return values.Device ?? string.Empty;
            case "BRANCH":
                return values.Branch ?? string.Empty;
            case "SHA7":
                return values.Sha is null ? string.Empty : values.Sha[..Math.Min(7, values.Sha.Length)];
            case "BUILDNUMBER":
                return values.BuildNumber ?? string.Empty;
            case "MACHINE":
                return values.Machine ?? string.Empty;
            default:
                return token.Text;
        }
    }

    private static string FormatDate(DateTimeOffset value, string format)
    {
        try
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString(DefaultDateFormat, CultureInfo.InvariantCulture);
        }
    }

    private static List<Token> Parse(string pattern, List<string>? errors)
    {
        var tokens = new List<Token>();
        var literal = new StringBuilder();

        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            var next = index + 1 < pattern.Length ? pattern[index + 1] : '\0';

            if (character == '{' && next == '{')
            {
                literal.Append('{');
                index++;
                continue;
            }

            if (character == '}' && next == '}')
            {
                literal.Append('}');
                index++;
                continue;
            }

            if (character != '{')
            {
                literal.Append(character);
                continue;
            }

            var close = pattern.IndexOf('}', index + 1);
            if (close < 0)
            {
                errors?.Add($"the '{{' at position {index + 1} is not closed; write {{{{ for a literal brace.");
                literal.Append(pattern, index, pattern.Length - index);
                break;
            }

            if (literal.Length > 0)
            {
                tokens.Add(Token.Literal(literal.ToString()));
                literal.Clear();
            }

            var content = pattern[(index + 1)..close];
            var colon = content.IndexOf(':');
            tokens.Add(colon < 0
                ? Token.Placeholder(content.Trim(), null, pattern[index..(close + 1)])
                : Token.Placeholder(content[..colon].Trim(), content[(colon + 1)..], pattern[index..(close + 1)]));
            index = close;
        }

        if (literal.Length > 0)
        {
            tokens.Add(Token.Literal(literal.ToString()));
        }

        return tokens;
    }

    private static string CollapseSpaces(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character == ' ' && (builder.Length == 0 || builder[^1] == ' '))
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().TrimEnd();
    }

    // Text: the literal, or the placeholder as written (kept when the name is unknown).
    private sealed record Token(bool IsPlaceholder, string Name, string? Format, string Text)
    {
        public static Token Literal(string text) => new(false, string.Empty, null, text);

        public static Token Placeholder(string name, string? format, string text) => new(true, name, format, text);
    }
}
