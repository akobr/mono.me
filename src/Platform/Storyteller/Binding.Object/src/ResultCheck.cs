using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Checks one envelope result before it is converted back to a token.
/// The walk adds key and string lengths plus structural characters and stops once the total passes
/// <see cref="ObjectBindingLimits.MaxResultLength"/>, so an oversized value is never serialized.
/// It also stops once arrays and objects nest deeper than <see cref="ObjectBindingLimits.MaxResultDepth"/>,
/// so the walk itself cannot exhaust the stack. A structurally shared result (<c>[s,s,…]</c>) is cheap to build
/// but large to serialize, and only this check catches it.
/// </summary>
internal static class ResultCheck
{
    public static void Ensure(JsonNode? result, ObjectBindingLimits limits)
    {
        var walk = new Walk(limits);
        if (walk.TryAdd(result, depth: 0))
        {
            return;
        }

        if (walk.TooDeep)
        {
            throw new EvaluationLimitExceededException(
                EvaluationLimitKind.Result,
                limits.MaxResultDepth,
                $"Object binding result nests deeper than {limits.MaxResultDepth} levels.");
        }

        throw new EvaluationLimitExceededException(
            EvaluationLimitKind.Result,
            limits.MaxResultLength,
            $"Object binding result exceeds {limits.MaxResultLength} characters.");
    }

    /// <summary>
    /// Returns the serialized length the walk counts, or one past the limit when it stops early.
    /// </summary>
    internal static long SerializedLength(JsonNode? node, ObjectBindingLimits limits)
    {
        var walk = new Walk(limits);
        return walk.TryAdd(node, depth: 0) ? walk.Used : limits.MaxResultLength + 1L;
    }

    private sealed class Walk
    {
        private readonly ObjectBindingLimits _limits;

        public Walk(ObjectBindingLimits limits)
        {
            _limits = limits;
        }

        public long Used { get; private set; }

        public bool TooDeep { get; private set; }

        public bool TryAdd(JsonNode? node, int depth)
        {
            switch (node)
            {
                case null:
                    return TryAddRaw(4);
                case JsonObject obj:
                    return TryEnter(depth) && TryAddObject(obj, depth + 1);
                case JsonArray array:
                    return TryEnter(depth) && TryAddArray(array, depth + 1);
                case JsonValue value:
                    return TryAddValue(value);
                default:
                    return TryAddRaw(node.ToJsonString().Length);
            }
        }

        private bool TryEnter(int depth)
        {
            if (depth >= _limits.MaxResultDepth)
            {
                TooDeep = true;
                return false;
            }

            return true;
        }

        private bool TryAddObject(JsonObject obj, int depth)
        {
            if (!TryAddRaw(1))
            {
                return false;
            }

            var first = true;
            foreach (var property in obj)
            {
                if (!first && !TryAddRaw(1))
                {
                    return false;
                }

                first = false;
                if (!TryAddString(property.Key) ||
                    !TryAddRaw(1) ||
                    !TryAdd(property.Value, depth))
                {
                    return false;
                }
            }

            return TryAddRaw(1);
        }

        private bool TryAddArray(JsonArray array, int depth)
        {
            if (!TryAddRaw(1))
            {
                return false;
            }

            var first = true;
            foreach (var item in array)
            {
                if (!first && !TryAddRaw(1))
                {
                    return false;
                }

                first = false;
                if (!TryAdd(item, depth))
                {
                    return false;
                }
            }

            return TryAddRaw(1);
        }

        private bool TryAddValue(JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return TryAddString(text ?? string.Empty);
            }

            if (value.TryGetValue<bool>(out var flag))
            {
                return TryAddRaw(flag ? 4 : 5);
            }

            return TryAddRaw(value.ToJsonString().Length);
        }

        private bool TryAddString(string text)
        {
            if (!TryAddRaw(1))
            {
                return false;
            }

            foreach (var current in text)
            {
                if (!TryAddRaw(EscapedWidth(current)))
                {
                    return false;
                }
            }

            return TryAddRaw(1);
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

        private bool TryAddRaw(int width)
        {
            if (Used + width > _limits.MaxResultLength)
            {
                return false;
            }

            Used += width;
            return true;
        }
    }
}
