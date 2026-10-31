using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Annotating;

// A name query of the typed annotation lists (nameQuery, responsibilityNameQuery, …): a regular expression when it
// starts with ^ or ends with $, a contained text when it starts or ends with %, an exact name otherwise. The
// expressions run on the non-backtracking engine, in time linear in the name: a page maps up to 1000 annotations,
// and a match timeout alone would bound one match, not the page. The timeout stays as a second bound.
public sealed class NameQuery
{
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly MethodInfo IsMatchMethod = typeof(NameQuery).GetMethod(nameof(IsMatch))!;

    private readonly Regex? _pattern;

    private NameQuery(string parameter, NameQueryKind kind, string value, Regex? pattern = null)
    {
        Parameter = parameter;
        Kind = kind;
        Value = value;
        _pattern = pattern;
    }

    // The query parameter, for the messages.
    public string Parameter { get; }

    public NameQueryKind Kind { get; }

    // The exact name, the contained text without its % markers, or the pattern.
    public string Value { get; }

    public bool IsExact => Kind == NameQueryKind.Exact;

    // No query for null, empty or white space text; an invalid pattern is an InvalidInputException (400 InvalidQuery).
    public static NameQuery? Parse(string? text, string parameter)
    {
        return Parse(text, parameter, MatchTimeout);
    }

    public bool IsMatch(string? name)
    {
        if (name is null)
        {
            return false;
        }

        switch (Kind)
        {
            case NameQueryKind.Exact:
                return string.Equals(name, Value, StringComparison.Ordinal);

            case NameQueryKind.Contains:
                return name.Contains(Value, StringComparison.Ordinal);

            default:
                try
                {
                    return _pattern!.IsMatch(name);
                }
                catch (RegexMatchTimeoutException)
                {
                    throw new InvalidInputException($"The {Parameter} took too long to match a name; simplify the pattern.", ErrorCodes.InvalidQuery);
                }
        }
    }

    // The condition "the name matches" over one name of the model, for example u => u.ResponsibilityName; it reads
    // u => query.IsMatch(u.ResponsibilityName).
    public AnnotationsRequest.Condition<TAnnotation> ToCondition<TAnnotation>(Expression<Func<TAnnotation, string?>> name)
        where TAnnotation : Annotation
    {
        var match = Expression.Call(Expression.Constant(this), IsMatchMethod, name.Body);

        return new AnnotationsRequest.Condition<TAnnotation>
        {
            Predicate = Expression.Lambda<Func<TAnnotation, bool>>(match, name.Parameters),
        };
    }

    // With another match timeout, for the tests.
    internal static NameQuery? Parse(string? text, string parameter, TimeSpan matchTimeout)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var query = text.Trim();

        if (query.StartsWith('^') || query.EndsWith('$'))
        {
            return new NameQuery(parameter, NameQueryKind.Pattern, query, CreatePattern(query, parameter, matchTimeout));
        }

        if (query.StartsWith('%') || query.EndsWith('%'))
        {
            return new NameQuery(parameter, NameQueryKind.Contains, query.Trim('%'));
        }

        return new NameQuery(parameter, NameQueryKind.Exact, query);
    }

    private static Regex CreatePattern(string query, string parameter, TimeSpan matchTimeout)
    {
        try
        {
            return new Regex(query, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, matchTimeout);
        }
        catch (RegexParseException exception)
        {
            throw new InvalidInputException($"The {parameter} is not a valid regular expression: {exception.Message}", ErrorCodes.InvalidQuery);
        }
        catch (NotSupportedException)
        {
            throw new InvalidInputException(
                $"The {parameter} '{query}' uses a construct name queries don't support (for example backreferences, lookarounds, atomic groups or conditionals).",
                ErrorCodes.InvalidQuery);
        }
    }
}
