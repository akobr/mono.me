using System.Globalization;
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
/// </remarks>
public static class JsonQuery
{
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
            $"'{expression}' is neither a JSON Pointer (must start with '/') nor a JSONPath expression (must start with '$').");
    }

    private static JToken? ResolveJsonPath(JToken root, string expression)
    {
        try
        {
            return root.SelectToken(expression);
        }
        catch (JsonException exception)
        {
            throw new BindingEvaluationException(
                $"Invalid JSONPath expression '{expression}': {exception.Message}", exception);
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

        // pointer starts with '/', so the first split segment is always empty; skip it.
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = Unescape(segments[i]);

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

    private static string Unescape(string segment)
    {
        return segment.Contains('~')
            ? segment.Replace("~1", "/").Replace("~0", "~")
            : segment;
    }
}
