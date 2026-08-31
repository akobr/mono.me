using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Language;

/// <summary>
/// Shared validation helpers for <see cref="IBindingFunction"/> implementations that expect their arguments to be
/// literal strings (as opposed to values resolved from a source). Function arguments must be written as quoted
/// string literals in the binding expression (e.g. <c>@config("$.a.b")</c>); an unquoted identifier is instead
/// pre-resolved as a configuration path lookup by <see cref="BindingEvaluator"/> before the function ever runs, so
/// it will never arrive here as the identifier's own literal text.
/// </summary>
public static class BindingFunctionArguments
{
    public static string RequireString(BindingValue value, string functionName, int position)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.Token is JValue { Type: JTokenType.String, Value: string text })
        {
            return text;
        }

        throw new BindingEvaluationException(
            $"Argument {position} of function '{functionName}' must be a quoted string literal.");
    }
}
