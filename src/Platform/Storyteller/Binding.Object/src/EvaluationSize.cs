using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Serialized length used by the per-evaluation size budget.
/// The walk adds key and string lengths plus structural characters and stops once the
/// total passes <see cref="ConfigurationBindingResolver.MaxConcatLength"/>, so an oversized
/// value is never serialized. A stopped walk reports one past that limit.
/// Null is the four characters of the JSON text <c>null</c>.
/// </summary>
internal static class EvaluationSize
{
    public static int SerializedLength(JsonNode? node)
    {
        var used = 0;
        if (!TryAdd(node, ref used))
        {
            return ConfigurationBindingResolver.MaxConcatLength + 1;
        }

        return used;
    }

    private static bool TryAdd(JsonNode? node, ref int used)
    {
        switch (node)
        {
            case null:
                return TryAddRaw(4, ref used);
            case JsonObject obj:
                return TryAddObject(obj, ref used);
            case JsonArray array:
                return TryAddArray(array, ref used);
            case JsonValue value:
                return TryAddValue(value, ref used);
            default:
                return TryAddRaw(node.ToJsonString().Length, ref used);
        }
    }

    private static bool TryAddObject(JsonObject obj, ref int used)
    {
        if (!TryAddRaw(1, ref used))
        {
            return false;
        }

        var first = true;
        foreach (var property in obj)
        {
            if (!first && !TryAddRaw(1, ref used))
            {
                return false;
            }

            first = false;
            if (!TryAddString(property.Key, ref used) ||
                !TryAddRaw(1, ref used) ||
                !TryAdd(property.Value, ref used))
            {
                return false;
            }
        }

        return TryAddRaw(1, ref used);
    }

    private static bool TryAddArray(JsonArray array, ref int used)
    {
        if (!TryAddRaw(1, ref used))
        {
            return false;
        }

        var first = true;
        foreach (var item in array)
        {
            if (!first && !TryAddRaw(1, ref used))
            {
                return false;
            }

            first = false;
            if (!TryAdd(item, ref used))
            {
                return false;
            }
        }

        return TryAddRaw(1, ref used);
    }

    private static bool TryAddValue(JsonValue value, ref int used)
    {
        if (value.TryGetValue<string>(out var text))
        {
            return TryAddString(text ?? string.Empty, ref used);
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            return TryAddRaw(flag ? 4 : 5, ref used);
        }

        return TryAddRaw(value.ToJsonString().Length, ref used);
    }

    private static bool TryAddString(string text, ref int used)
    {
        if (!TryAddRaw(1, ref used))
        {
            return false;
        }

        foreach (var current in text)
        {
            if (!TryAddRaw(EscapedWidth(current), ref used))
            {
                return false;
            }
        }

        return TryAddRaw(1, ref used);
    }

    private static int EscapedWidth(char current)
    {
        switch (current)
        {
            case '\b' or '\t' or '\n' or '\f' or '\r' or '\\':
                return 2;
            case '"' or '&' or '\'' or '+' or '<' or '>' or '`' or '\u007F':
                return 6;
            default:
                return current is < ' ' or > '\u007F' ? 6 : 1;
        }
    }

    private static bool TryAddRaw(int width, ref int used)
    {
        if ((long)used + width > ConfigurationBindingResolver.MaxConcatLength)
        {
            return false;
        }

        used += width;
        return true;
    }
}
