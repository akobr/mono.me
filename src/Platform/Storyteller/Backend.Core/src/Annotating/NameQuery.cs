using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Annotating;

// A name query of the typed annotation lists (nameQuery, responsibilityNameQuery, …): a regular expression when it
// starts with ^ or ends with $, a contained text when it starts or ends with %, an exact name otherwise. A query is
// parsed for one request. The expressions run on the non-backtracking engine, which has no catastrophic backtracking,
// but it is linear in the name only while its lazy automaton fits: a large pattern falls back to a slower mode that
// does not always check the match timeout. So three bounds hold together: the length of a pattern bounds its
// construction, the timeout bounds one match where the engine checks it, and the budget bounds the matching time of
// the whole request (a page maps up to 1000 annotations).
public sealed class NameQuery
{
    public const int PatternMaxLength = 256;

    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    public static readonly TimeSpan MatchBudget = TimeSpan.FromMilliseconds(250);

    private static readonly MethodInfo IsMatchMethod = typeof(NameQuery).GetMethod(nameof(IsMatch))!;

    private readonly Regex? _pattern;
    private readonly long _budgetTicks;
    private long _spentTicks;

    private NameQuery(string parameter, NameQueryKind kind, string value, Regex? pattern = null, TimeSpan budget = default)
    {
        Parameter = parameter;
        Kind = kind;
        Value = value;
        _pattern = pattern;
        _budgetTicks = (long)(budget.TotalSeconds * Stopwatch.Frequency);
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
        return Parse(text, parameter, MatchTimeout, MatchBudget);
    }

    // A pattern that runs into the timeout or the budget is an InvalidInputException (400 InvalidQuery).
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
                var started = Stopwatch.GetTimestamp();
                bool matches;

                try
                {
                    matches = _pattern!.IsMatch(name);
                }
                catch (RegexMatchTimeoutException)
                {
                    throw TooSlow();
                }

                // the predicate runs on one page at a time, but nothing promises a single thread
                if (Interlocked.Add(ref _spentTicks, Stopwatch.GetTimestamp() - started) > _budgetTicks)
                {
                    throw TooSlow();
                }

                return matches;
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

    // With another match timeout and budget, for the tests.
    internal static NameQuery? Parse(string? text, string parameter, TimeSpan matchTimeout, TimeSpan matchBudget)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var query = text.Trim();

        if (query.StartsWith('^') || query.EndsWith('$'))
        {
            return new NameQuery(parameter, NameQueryKind.Pattern, query, CreatePattern(query, parameter, matchTimeout), matchBudget);
        }

        if (query.StartsWith('%') || query.EndsWith('%'))
        {
            return new NameQuery(parameter, NameQueryKind.Contains, query.Trim('%'));
        }

        return new NameQuery(parameter, NameQueryKind.Exact, query);
    }

    private static Regex CreatePattern(string query, string parameter, TimeSpan matchTimeout)
    {
        if (query.Length > PatternMaxLength)
        {
            throw new InvalidInputException($"The {parameter} pattern has {query.Length} characters, at most {PatternMaxLength} are allowed.", ErrorCodes.InvalidQuery);
        }

        try
        {
            return new Regex(query, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, matchTimeout);
        }
        catch (RegexParseException exception)
        {
            throw new InvalidInputException($"The {parameter} is not a valid regular expression: {exception.Message}", ErrorCodes.InvalidQuery);
        }
        catch (NotSupportedException exception)
        {
            // the engine refuses backreferences, lookarounds, atomic groups and conditionals, and a pattern whose
            // automaton would be too large (long counted repetitions); its message says which
            throw new InvalidInputException($"The {parameter} '{query}' is not supported by name queries: {exception.Message}", ErrorCodes.InvalidQuery);
        }
    }

    private InvalidInputException TooSlow()
    {
        return new InvalidInputException($"The {Parameter} took too long to match the names; simplify the pattern.", ErrorCodes.InvalidQuery);
    }
}
