using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// Resolves a structured query expression against a <see cref="JToken"/>, automatically detecting whether the
/// expression is a JSON Pointer (RFC 6901, e.g. <c>/objects/and/3/arrays</c>) or a JSONPath expression (Newtonsoft's
/// dialect, e.g. <c>$.store.book[0].price</c>) from its leading character.
/// </summary>
/// <remarks>
/// A bare <c>@...</c>-prefixed expression is only meaningful inside a JSONPath <c>[?(...)]</c> filter, not as a
/// whole top-level expression, so it is intentionally not auto-detected here; only a leading <c>$</c> is treated as
/// JSONPath and only a leading <c>/</c> (or the empty string, meaning the whole document) is treated as a JSON
/// Pointer. Anything else is a validation error naming both accepted forms.
/// The expression can be the value of a nested statement, even a secret, so no message quotes it.
/// </remarks>
public static class JsonQuery
{
    /// <summary>
    /// The time one <c>=~</c> filter of a JSONPath query may take to match one value. Without settings, Newtonsoft
    /// matches with no timeout at all.
    /// </summary>
    public static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSelectSettings SelectSettings = new() { RegexMatchTimeout = RegexMatchTimeout };

    public static JToken? Resolve(JToken root, string expression)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        if (expression.Length == 0 || expression[0] == '/')
        {
            return ResolveJsonPointer(root, expression);
        }

        if (expression[0] == '$')
        {
            return ResolveJsonPath(root, expression);
        }

        throw new BindingEvaluationException(
            "The query is neither a JSON Pointer (must start with '/') nor a JSONPath expression (must start with '$').");
    }

    private static JToken? ResolveJsonPath(JToken root, string expression)
    {
        // The query can be computed (a stored string read by a nested statement), so neither the length cap nor the
        // depth limit of the @ string covers it. Newtonsoft guards none of its recursion: its parser recurses once per nested
        // '[' or '(', and its evaluation chains one lazy filter per '.', '[' and '(' that the first match runs through.
        if (expression.Length > BindingExpressionLimits.MaxLength)
        {
            throw new EvaluationLimitExceededException(
                EvaluationLimitKind.ExpressionLength,
                BindingExpressionLimits.MaxLength,
                $"The JSONPath query is longer than {BindingExpressionLimits.MaxLength} characters.");
        }

        // Counting every one of them bounds both, even when quotes would hide a closing one.
        if (expression.Count(c => c is '.' or '[' or '(') > BindingExpressionLimits.MaxDepth)
        {
            throw new EvaluationLimitExceededException(
                EvaluationLimitKind.Depth,
                BindingExpressionLimits.MaxDepth,
                $"The JSONPath query has more than {BindingExpressionLimits.MaxDepth} path segments, brackets and parentheses.");
        }

        // A recursive descent scans below every match of the previous step, and a root reference inside a filter runs a
        // whole query for every candidate. Chained, either costs time exponential in a short query over a small document.
        if (expression.Split("..").Length > 2)
        {
            throw new BindingEvaluationException("A JSONPath query can use at most one recursive descent ('..').");
        }

        if (expression.Count(c => c == '$') > 2)
        {
            throw new BindingEvaluationException(
                "A JSONPath query can use '$' at most once after its start (a root reference inside a filter, or a regular expression's end).");
        }

        try
        {
            return root.SelectToken(expression, SelectSettings);
        }
        catch (JsonException exception)
        {
            // Newtonsoft's messages quote a character of the query, which can be computed.
            throw new BindingEvaluationException("The JSONPath query is not valid.", exception);
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw new BindingEvaluationException("The JSONPath query took too long to evaluate.", exception);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IndexOutOfRangeException or InvalidCastException)
        {
            // An index Newtonsoft cannot convert, an invalid =~ pattern (its message quotes the pattern), a query that ends
            // inside an indexer or a filter, which Newtonsoft reads past its end, or a filter that compares values of
            // incompatible types, such as a date or a BigInteger with a number or a boolean.
            throw new BindingEvaluationException("The JSONPath query is not valid.", exception);
        }
    }

    private static JToken? ResolveJsonPointer(JToken root, string pointer)
    {
        if (pointer.Length == 0)
        {
            return root;
        }

        var current = root;
        var segments = pointer.Split('/');
        var offset = 0;

        // pointer starts with '/', so the first split segment is always empty; skip it.
        for (var i = 1; i < segments.Length; i++)
        {
            offset += segments[i - 1].Length + 1;
            var segment = Unescape(segments[i], offset);

            switch (current)
            {
                case JObject obj:
                    if (!obj.TryGetValue(segment, out var next))
                    {
                        return null;
                    }

                    current = next;
                    break;

                case JArray array:
                    if (segment == "-"
                        || segment.Length == 0
                        || (segment.Length > 1 && segment[0] == '0')
                        || !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        || index < 0
                        || index >= array.Count)
                    {
                        return null;
                    }

                    current = array[index];
                    break;

                default:
                    return null;
            }
        }

        return current;
    }

    // offset is the position of the segment in the whole pointer
    private static string Unescape(string segment, int offset)
    {
        if (!segment.Contains('~'))
        {
            return segment;
        }

        for (var i = 0; i < segment.Length; i++)
        {
            if (segment[i] == '~' && (i + 1 >= segment.Length || (segment[i + 1] != '0' && segment[i + 1] != '1')))
            {
                throw new BindingEvaluationException(
                    $"Invalid JSON Pointer escape at offset {offset + i} of the query: '~' must be followed by '0' or '1'.");
            }
        }

        return segment.Replace("~1", "/").Replace("~0", "~");
    }
}
