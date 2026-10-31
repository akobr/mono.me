using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// What part of an engine's message may leave <see cref="ObjectBindingEngine"/>. The message reaches the API's response,
/// so it must not quote a value: a value of <c>$context</c> can be a secret.
/// The vendored JSON-e builds four messages with an evaluated value; its parity tests with upstream keep their text,
/// so they are replaced here. An envelope emitted by an earlier result has a computed definition, so none of its JSON-e
/// text is forwarded. The messages of JSON Logic are not all verified, so none is forwarded.
/// Any other exception is named by its type only.
/// </summary>
internal static class EngineMessages
{
    public const string JsonLogicFailed = "JSON Logic evaluation failed.";

    public const string JsonEFailed = "JSON-e evaluation failed.";

    private const string UnknownContextValue = "unknown context value";

    // An authored JSON-e name is an identifier; anything longer is not worth scanning the definition for.
    private const int MaxAuthoredNameLength = 256;

    public static string ForJsonE(string message, JToken definition, bool emitted)
    {
        if (emitted)
        {
            return JsonEFailed;
        }

        // FunctionExpressionNode: "{value} is not callable", the value of the callee
        if (message.EndsWith(" is not callable", StringComparison.Ordinal))
        {
            return "A called value is not a function.";
        }

        // DefinedFunction: "'{argument}' is not a valid context accessor", the evaluated argument
        if (message.EndsWith(" is not a valid context accessor", StringComparison.Ordinal))
        {
            return "The argument of defined() is not a valid context accessor.";
        }

        // ContextAccessor: "{name} is not a valid accessor", a callee whose value is a string
        if (message.EndsWith(" is not a valid accessor", StringComparison.Ordinal))
        {
            return "A called name is not a valid context accessor.";
        }

        // EvaluationContext, FunctionExpressionNode and ValueAccessor: "unknown context value {name}". The name is authored,
        // except when a callee's value is a string: then it is that value. A whole word of the definition is authored.
        if (message.StartsWith(UnknownContextValue + " ", StringComparison.Ordinal))
        {
            var name = message[(UnknownContextValue.Length + 1)..].Trim('"');
            return IsWordOf(name, definition.ToString(Formatting.None)) ? message : UnknownContextValue;
        }

        return message;
    }

    public static string ForUnexpected(Exception exception)
    {
        return $"Object binding evaluation failed ({exception.GetType().Name}).";
    }

    // One pass over the text: each maximal run of word characters is compared once, its length first.
    private static bool IsWordOf(string name, string text)
    {
        if (name.Length == 0 || name.Length > MaxAuthoredNameLength || !name.All(IsWordCharacter))
        {
            return false;
        }

        var index = 0;
        while (index < text.Length)
        {
            if (!IsWordCharacter(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && IsWordCharacter(text[index]))
            {
                index++;
            }

            if (index - start == name.Length && string.CompareOrdinal(text, start, name, 0, name.Length) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordCharacter(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c == '_';
    }
}
