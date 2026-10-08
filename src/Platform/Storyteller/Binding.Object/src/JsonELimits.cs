using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.JsonE;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

internal static class JsonELimits
{
    private const string ReservedPrefix = "storyteller";

    public static int OperatorDepth(JToken token)
    {
        var max = 0;
        Walk(token, 0, ref max);
        return max;
    }

    public static JToken PrepareDefinition(JToken definition)
    {
        var clone = definition.DeepClone();
        if (clone.Type == JTokenType.String &&
            JsonEExpressionRewriter.TryRewriteInterpolation(clone.Value<string>() ?? string.Empty, out var rewritten))
        {
            return new JValue(rewritten);
        }

        Rewrite(clone);
        return clone;
    }

    public static JsonNode? Range(JsonNode?[] arguments, EvaluationContext context)
    {
        _ = context;
        if (arguments.Length is < 2 or > 3 ||
            !TryInteger(arguments[0], out var start) ||
            !TryInteger(arguments[1], out var end))
        {
            throw new InterpreterException("range expects two or three integer arguments.");
        }

        var step = 1m;
        if (arguments.Length == 3 && (!TryInteger(arguments[2], out step) || step == 0))
        {
            throw new InterpreterException("range expects two or three integer arguments.");
        }

        var direction = Math.Sign(step);
        var orientedStart = start * direction;
        var orientedEnd = end * direction;
        var orientedStep = step * direction;
        if (orientedStart >= orientedEnd)
        {
            return new JsonArray();
        }

        decimal span;
        try
        {
            span = orientedEnd - orientedStart;
        }
        catch (OverflowException)
        {
            throw RangeExceeded();
        }

        if (span / orientedStep > ConfigurationBindingResolver.MaxRangeItems)
        {
            throw RangeExceeded();
        }

        var count = (int)((span + orientedStep - 1) / orientedStep);
        if (count > ConfigurationBindingResolver.MaxRangeItems)
        {
            throw RangeExceeded();
        }

        var values = new JsonArray();
        for (var i = orientedStart; values.Count < count && i < orientedEnd; i += orientedStep)
        {
            values.Add(JsonValue.Create(i * direction));
        }

        return values;
    }

    public static JsonNode? Add(JsonNode?[] arguments, EvaluationContext context)
    {
        _ = context;
        if (arguments.Length != 2)
        {
            throw new InterpreterException("infix: + expects numbers/strings + numbers/strings");
        }

        if (IsJsonString(arguments[0], out var leftString) && IsJsonString(arguments[1], out var rightString))
        {
            if ((long)leftString.Length + rightString.Length > ConfigurationBindingResolver.MaxConcatLength)
            {
                throw ConcatExceeded();
            }

            return leftString + rightString;
        }

        if (!TryNumber(arguments[0], out var left) || !TryNumber(arguments[1], out var right))
        {
            throw new InterpreterException("infix: + expects numbers/strings + numbers/strings");
        }

        return left + right;
    }

    public static JsonNode? Concat(JsonNode?[] arguments, EvaluationContext context)
    {
        _ = context;
        var result = new StringBuilder();
        foreach (var argument in arguments)
        {
            var part = Stringify(argument);
            if ((long)result.Length + part.Length > ConfigurationBindingResolver.MaxConcatLength)
            {
                throw ConcatExceeded();
            }

            result.Append(part);
        }

        return result.ToString();
    }

    public static JsonNode? Bound(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        var value = arguments.Length == 0 ? null : arguments[0];
        budget.Add(EvaluationSize.SerializedLength(value));
        return value;
    }

    public static JsonNode? Let(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        var value = arguments.Length == 0 ? null : arguments[0];
        if (value is JsonObject obj)
        {
            foreach (var property in obj)
            {
                if (property.Key.StartsWith(ReservedPrefix, StringComparison.Ordinal))
                {
                    throw new BindingEvaluationException($"JSON-e name '{property.Key}' is reserved.");
                }
            }
        }

        return Bound(arguments, budget);
    }

    public static JsonNode? Step(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        budget.AddStep();
        var value = arguments.Length == 0 ? null : arguments[0];
        return value is JsonArray { Count: > 0 };
    }

    public static JsonNode? Join(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        if (arguments.Length != 2 ||
            arguments[0] is not JsonArray source ||
            !TryJoinSeparator(arguments[1], out var separator))
        {
            throw new BuiltInException("invalid arguments to builtin: join");
        }

        var result = new StringBuilder();
        var first = true;
        foreach (var item in source)
        {
            if (!TryJoinElement(item, out var part))
            {
                throw new BuiltInException("invalid arguments to builtin: join");
            }

            var extra = first ? part.Length : separator.Length + part.Length;
            budget.EnsureFits(result.Length + extra);
            if (!first)
            {
                result.Append(separator);
            }

            result.Append(part);
            first = false;
        }

        budget.Add(result.Length);
        return result.ToString();
    }

    public static JsonNode? In(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        if (arguments.Length != 2)
        {
            throw new InterpreterException("infix: in expects Array, string, or object on right side");
        }

        var left = arguments[0];
        var right = arguments[1];
        if (right is JsonObject obj)
        {
            if (!IsJsonString(left, out var key))
            {
                throw new InterpreterException("infix: in-object expects string on left side");
            }

            budget.AddSteps(obj.Count);
            return obj.ContainsKey(key);
        }

        if (IsJsonString(right, out var haystack))
        {
            if (!IsJsonString(left, out var needle))
            {
                throw new InterpreterException("infix: in-string expects string on left side");
            }

            if (needle.Length == 0)
            {
                return true;
            }

            budget.AddSteps(haystack.Length);
            return haystack.Contains(needle);
        }

        if (right is JsonArray array)
        {
            budget.AddSteps(array.Count);
            foreach (var item in array)
            {
                if (DeepEquals(left, item, budget))
                {
                    return true;
                }
            }

            return false;
        }

        throw new InterpreterException("infix: in expects Array, string, or object on right side");
    }

    public static JsonNode? Equals(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        if (arguments.Length != 2)
        {
            throw new InterpreterException("infix: == expects two values");
        }

        return DeepEquals(arguments[0], arguments[1], budget);
    }

    public static JsonNode? Split(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        if (arguments.Length != 2 || !IsJsonString(arguments[0], out var text) || !TrySplitDelimiter(arguments[1], out var delimiter))
        {
            throw new BuiltInException("invalid arguments to builtin: split");
        }

        budget.AddSteps(text.Length);
        if (delimiter.Length == 0)
        {
            var chars = new JsonArray();
            foreach (var character in text)
            {
                chars.Add(character.ToString());
            }

            return chars;
        }

        var parts = new JsonArray();
        foreach (var part in text.Split(delimiter))
        {
            parts.Add(part);
        }

        return parts;
    }

    public static JsonNode? Lowercase(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        var text = RequireString(arguments, "lowercase");
        budget.AddSteps(text.Length);
        return text.ToLowerInvariant();
    }

    public static JsonNode? Uppercase(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        var text = RequireString(arguments, "uppercase");
        budget.AddSteps(text.Length);
        return text.ToUpperInvariant();
    }

    public static JsonNode? Strip(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        var text = RequireString(arguments, "strip");
        budget.AddSteps(text.Length);
        return Trim(text, leading: true, trailing: true);
    }

    public static JsonNode? LStrip(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        var text = RequireString(arguments, "lstrip");
        budget.AddSteps(text.Length);
        return Trim(text, leading: true, trailing: false);
    }

    public static JsonNode? RStrip(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        var text = RequireString(arguments, "rstrip");
        budget.AddSteps(text.Length);
        return Trim(text, leading: false, trailing: true);
    }

    public static JsonNode? Len(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        if (arguments.Length == 1 && arguments[0] is JsonArray array)
        {
            return array.Count;
        }

        if (arguments.Length == 1 && IsJsonString(arguments[0], out var text))
        {
            budget.AddSteps(text.Length);
            return new StringInfo(text).LengthInTextElements;
        }

        throw new BuiltInException("invalid arguments to builtin: len");
    }

    public static JsonNode? Index(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        if (arguments.Length != 2)
        {
            throw new InterpreterException("infix: \"[..]\" expects object, array, or string");
        }

        var target = arguments[0];
        var index = arguments[1];
        if (target is JsonObject obj)
        {
            if (!IsJsonString(index, out var key))
            {
                throw new InterpreterException("object keys must be strings");
            }

            return obj.TryGetPropertyValue(key, out var value) ? CopyValue(value, budget) : null;
        }

        if (!TryInteger(index, out var raw))
        {
            throw new InterpreterException("should only use integers to access arrays or strings");
        }

        if (IsJsonString(target, out var text))
        {
            budget.AddSteps(text.Length);
            var info = new StringInfo(text);
            var position = ResolveIndex(raw, info.LengthInTextElements);
            return info.SubstringByTextElements(position, 1);
        }

        if (target is JsonArray array)
        {
            var position = ResolveIndex(raw, array.Count);
            return CopyValue(array[position], budget);
        }

        throw new InterpreterException("infix: \"[..]\" expects object, array, or string");
    }

    public static JsonNode? Slice(JsonNode?[] arguments, JsonESizeBudget budget)
    {
        if (arguments.Length is < 2 or > 3)
        {
            throw new InterpreterException("infix: \"[..]\" expects object, array, or string");
        }

        if (!TryInteger(arguments[1], out var start))
        {
            throw new InterpreterException("cannot perform interval access with non-integers");
        }

        int? end = null;
        if (arguments.Length == 3)
        {
            if (!TryInteger(arguments[2], out var endValue))
            {
                throw new InterpreterException("cannot perform interval access with non-integers");
            }

            end = DecimalToInt(endValue);
        }

        var target = arguments[0];
        if (IsJsonString(target, out var text))
        {
            budget.AddSteps(text.Length);
            var info = new StringInfo(text);
            var (from, to) = SliceRange(DecimalToInt(start), end, info.LengthInTextElements);
            return from >= to ? string.Empty : info.SubstringByTextElements(from, to - from);
        }

        if (target is JsonArray array)
        {
            var (from, to) = SliceRange(DecimalToInt(start), end, array.Count);
            budget.AddSteps(to - from);
            var slice = new JsonArray();
            for (var position = from; position < to; position++)
            {
                slice.Add(CopyValue(array[position], budget));
            }

            return slice;
        }

        throw new InterpreterException("infix: \"[..]\" expects object, array, or string");
    }

    private static void Rewrite(JToken token)
    {
        Rewrite(token, interpolateKeys: true);
    }

    private static void Rewrite(JToken token, bool interpolateKeys)
    {
        switch (token)
        {
            case JObject obj:
                if (interpolateKeys)
                {
                    RewriteInterpolatedKeys(obj);
                }

                var isSort = obj.ContainsKey("$sort");
                var isFind = obj.ContainsKey("$find");
                foreach (var property in obj.Properties())
                {
                    if (property.Name is "$switch" or "$match" && property.Value is JObject cases)
                    {
                        RewriteInterpolatedKeys(cases);
                        RewriteCaseKeys(cases);
                        Rewrite(cases, interpolateKeys: false);
                        continue;
                    }

                    RejectReservedBinding(property);

                    if (property.Value.Type == JTokenType.String)
                    {
                        var text = property.Value.Value<string>() ?? string.Empty;
                        if (IsExpressionSlot(property.Name, isSort, isFind))
                        {
                            property.Value = JsonEExpressionRewriter.RewritePlus(text);
                            continue;
                        }

                        if (JsonEExpressionRewriter.TryRewriteInterpolation(text, out var rewritten))
                        {
                            property.Value = rewritten;
                        }

                        continue;
                    }

                    Rewrite(property.Value);
                }

                WrapGrowth(obj);
                break;

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index].Type == JTokenType.String &&
                        JsonEExpressionRewriter.TryRewriteInterpolation(array[index].Value<string>() ?? string.Empty, out var rewritten))
                    {
                        array[index] = rewritten;
                        continue;
                    }

                    Rewrite(array[index]);
                }

                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is JObject child && HasOperator(child))
                    {
                        array[index] = BoundWrap(child);
                    }
                }

                break;
        }
    }

    private static bool IsExpressionSlot(string name, bool isSort, bool isFind)
    {
        if (name is "$eval" or "$if")
        {
            return true;
        }

        if (isSort && IsNamedClause(name, "by"))
        {
            return true;
        }

        return isFind && IsNamedClause(name, "each");
    }

    private static bool IsNamedClause(string name, string keyword)
    {
        return name.StartsWith(keyword + "(", StringComparison.Ordinal) && name.EndsWith(')');
    }

    private static void RewriteInterpolatedKeys(JObject obj)
    {
        var names = new List<string>();
        var changed = false;
        foreach (var property in obj.Properties())
        {
            if (JsonEExpressionRewriter.TryRewriteInterpolatedKey(property.Name, out var rewritten))
            {
                names.Add(rewritten);
                changed = true;
            }
            else
            {
                names.Add(property.Name);
            }
        }

        if (!changed)
        {
            return;
        }

        var rebuilt = new JObject();
        var index = 0;
        foreach (var property in obj.Properties())
        {
            var name = names[index];
            index++;
            if (rebuilt.Property(name) is not null)
            {
                throw new BindingEvaluationException("JSON-e expression could not be bounded.");
            }

            rebuilt.Add(name, property.Value.DeepClone());
        }

        obj.RemoveAll();
        foreach (var property in rebuilt.Properties().ToList())
        {
            property.Remove();
            obj.Add(property);
        }
    }

    private static void RewriteCaseKeys(JObject cases)
    {
        var rebuilt = new JObject();
        foreach (var property in cases.Properties().ToList())
        {
            var name = property.Name == "$default"
                ? property.Name
                : JsonEExpressionRewriter.RewritePlus(property.Name);
            if (rebuilt.Property(name) is not null)
            {
                throw new BindingEvaluationException("JSON-e expression could not be bounded.");
            }

            rebuilt.Add(name, property.Value.DeepClone());
        }

        cases.RemoveAll();
        foreach (var property in rebuilt.Properties().ToList())
        {
            property.Remove();
            cases.Add(property);
        }
    }

    private static void WrapGrowth(JObject obj)
    {
        var isMapping = obj.ContainsKey("$map") || obj.ContainsKey("$reduce");
        var names = new List<string>();
        foreach (var property in obj.Properties())
        {
            names.Add(property.Name);
        }

        foreach (var name in names)
        {
            if (obj[name] is not JToken value || !ShouldBound(name, value, isMapping))
            {
                continue;
            }

            obj[name] = name == "$let" ? LetWrap(value) : BoundWrap(value);
        }
    }

    private static void RejectReservedBinding(JProperty property)
    {
        if (property.Name == "$let" && property.Value is JObject bindings)
        {
            foreach (var binding in bindings.Properties())
            {
                RejectReservedName(binding.Name);
            }
        }

        if (!TryClauseParameters(property.Name, out var parameters))
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            RejectReservedName(parameter);
        }
    }

    private static void RejectReservedName(string name)
    {
        if (name.StartsWith(ReservedPrefix, StringComparison.Ordinal))
        {
            throw new BindingEvaluationException($"JSON-e name '{name}' is reserved.");
        }
    }

    private static bool TryClauseParameters(string name, out string[] parameters)
    {
        parameters = [];
        var open = name.IndexOf('(');
        if (open <= 0 || name[^1] != ')')
        {
            return false;
        }

        var keyword = name[..open];
        if (keyword is not ("each" or "by"))
        {
            return false;
        }

        parameters = name[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return true;
    }

    private static bool ShouldBound(string name, JToken value, bool isMapping)
    {
        // $default makes the cases object look like an operator. The case values are wrapped on their own.
        if (name == "$switch")
        {
            return false;
        }

        // Keys can be interpolated, so check them after rendering.
        if (name == "$let")
        {
            return true;
        }

        if (value is JObject child && HasOperator(child))
        {
            return true;
        }

        if (name == "$json")
        {
            return true;
        }

        return isMapping && name.StartsWith("each(", StringComparison.Ordinal) && name.EndsWith(')');
    }

    private static JObject BoundWrap(JToken body)
    {
        return ChargeWrap(body, JsonEExpressionRewriter.BoundFunction);
    }

    private static JObject LetWrap(JToken body)
    {
        return ChargeWrap(body, JsonEExpressionRewriter.LetFunction);
    }

    private static JObject ChargeWrap(JToken body, string function)
    {
        // An array drops a delete marker, so $if without else still removes the value.
        var held = new JArray();
        held.Add(body.DeepClone());
        return new JObject
        {
            ["$let"] = new JObject
            {
                ["v"] = held,
            },
            ["in"] = new JObject
            {
                ["$if"] = JsonEExpressionRewriter.StepFunction + "(v)",
                ["then"] = new JObject
                {
                    ["$eval"] = function + "(v[0])",
                },
            },
        };
    }

    private static void Walk(JToken token, int depth, ref int max)
    {
        var next = depth;
        if (token is JObject obj && HasOperator(obj))
        {
            next = depth + 1;
            if (next > max)
            {
                max = next;
            }
        }

        switch (token)
        {
            case JObject objectNode:
                foreach (var property in objectNode.Properties())
                {
                    Walk(property.Value, next, ref max);
                }

                break;

            case JArray array:
                foreach (var item in array)
                {
                    Walk(item, next, ref max);
                }

                break;
        }
    }

    private static bool HasOperator(JObject obj)
    {
        foreach (var property in obj.Properties())
        {
            var name = property.Name;
            if (name.Length > 1 && name[0] == '$' && name[1] != '$')
            {
                return true;
            }
        }

        return false;
    }

    private static BindingEvaluationException RangeExceeded()
    {
        return new BindingEvaluationException(
            $"JSON-e range exceeds {ConfigurationBindingResolver.MaxRangeItems} items.");
    }

    private static BindingEvaluationException ConcatExceeded()
    {
        return new BindingEvaluationException(
            $"JSON-e string concatenation exceeds {ConfigurationBindingResolver.MaxConcatLength} characters.");
    }

    private static string Stringify(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        if (IsJsonString(node, out var text))
        {
            return text;
        }

        if (TryNumber(node, out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        if (node is JsonValue value && value.TryGetValue(out bool boolean))
        {
            return boolean ? "true" : "false";
        }

        throw new InterpreterException("interpolation produced an array or object.");
    }

    private static bool TryJoinSeparator(JsonNode? node, out string text)
    {
        text = string.Empty;
        if (TryNumber(node, out var number))
        {
            text = number.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return IsJsonString(node, out text);
    }

    private static bool TryJoinElement(JsonNode? node, out string text)
    {
        if (TryJoinSeparator(node, out text))
        {
            return true;
        }

        if (node is JsonValue value && value.TryGetValue(out bool boolean))
        {
            text = boolean ? "true" : "false";
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static bool TryInteger(JsonNode? node, out decimal value)
    {
        if (!TryNumber(node, out value) || decimal.Truncate(value) != value)
        {
            value = 0;
            return false;
        }

        return true;
    }

    private static bool TryNumber(JsonNode? node, out decimal number)
    {
        number = 0;
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue(out decimal parsed))
        {
            number = parsed;
            return true;
        }

        if (value.TryGetValue(out double real) && double.IsFinite(real))
        {
            number = (decimal)real;
            return true;
        }

        if (value.TryGetValue(out int integer))
        {
            number = integer;
            return true;
        }

        if (value.TryGetValue(out long wide))
        {
            number = wide;
            return true;
        }

        return false;
    }

    private static bool IsJsonString(JsonNode? node, out string text)
    {
        if (node is JsonValue value &&
            value.GetValueKind() == JsonValueKind.String &&
            value.TryGetValue(out string? parsed) &&
            parsed is not null)
        {
            text = parsed;
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static int ResolveIndex(decimal raw, int length)
    {
        var position = (long)DecimalToInt(raw);
        if (position < 0)
        {
            position += length;
        }

        if (position < 0 || position >= length)
        {
            throw new InterpreterException("index out of bounds");
        }

        return (int)position;
    }

    private static (int From, int To) SliceRange(int start, int? end, int length)
    {
        var from = Clamp(start, length);
        var to = end is int value ? Clamp(value, length) : length;
        if (from > to)
        {
            to = from;
        }

        return (from, to);
    }

    private static int Clamp(int bound, int length)
    {
        var value = (long)bound;
        if (value < 0)
        {
            value += length;
        }

        if (value < 0)
        {
            return 0;
        }

        if (value > length)
        {
            return length;
        }

        return (int)value;
    }

    private static int DecimalToInt(decimal value)
    {
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }

    private static JsonNode? CopyValue(JsonNode? node, JsonESizeBudget budget)
    {
        if (node is null)
        {
            return null;
        }

        if (IsJsonString(node, out var text))
        {
            return text;
        }

        ChargeCopiedStructure(node, budget);
        return node.DeepClone();
    }

    private static void ChargeCopiedStructure(JsonNode? node, JsonESizeBudget budget)
    {
        switch (node)
        {
            case JsonArray array:
                budget.AddSteps(array.Count);
                foreach (var item in array)
                {
                    ChargeCopiedStructure(item, budget);
                }

                break;
            case JsonObject obj:
                budget.AddSteps(obj.Count);
                foreach (var property in obj)
                {
                    ChargeCopiedStructure(property.Value, budget);
                }

                break;
            default:
                if (IsJsonString(node, out var text))
                {
                    budget.AddSteps(text.Length);
                }

                break;
        }
    }

    private static bool DeepEquals(JsonNode? left, JsonNode? right, JsonESizeBudget budget)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is JsonArray leftArray && right is JsonArray rightArray)
        {
            budget.AddSteps(Math.Max(leftArray.Count, rightArray.Count));
            if (leftArray.Count != rightArray.Count)
            {
                return false;
            }

            for (var index = 0; index < leftArray.Count; index++)
            {
                if (!DeepEquals(leftArray[index], rightArray[index], budget))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            budget.AddSteps(Math.Max(leftObject.Count, rightObject.Count));
            if (leftObject.Count != rightObject.Count)
            {
                return false;
            }

            foreach (var property in leftObject)
            {
                if (!rightObject.TryGetPropertyValue(property.Key, out var other) ||
                    !DeepEquals(property.Value, other, budget))
                {
                    return false;
                }
            }

            return true;
        }

        if (IsJsonString(left, out var leftText) && IsJsonString(right, out var rightText))
        {
            var limit = Math.Min(leftText.Length, rightText.Length);
            var index = 0;
            while (index < limit && leftText[index] == rightText[index])
            {
                index++;
            }

            var scanned = index < limit ? index + 1 : index;
            budget.AddSteps(scanned);
            return index == leftText.Length && leftText.Length == rightText.Length;
        }

        if (TryNumber(left, out var leftNumber) && TryNumber(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        if (left is JsonValue leftValue &&
            right is JsonValue rightValue &&
            leftValue.TryGetValue(out bool leftBool) &&
            rightValue.TryGetValue(out bool rightBool))
        {
            return leftBool == rightBool;
        }

        return false;
    }

    private static string RequireString(JsonNode?[] arguments, string name)
    {
        if (arguments.Length == 1 && IsJsonString(arguments[0], out var text))
        {
            return text;
        }

        throw new BuiltInException($"invalid arguments to builtin: {name}");
    }

    private static bool TrySplitDelimiter(JsonNode? node, out string delimiter)
    {
        if (IsJsonString(node, out delimiter))
        {
            return true;
        }

        if (!TryNumber(node, out var number))
        {
            delimiter = string.Empty;
            return false;
        }

        delimiter = number.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static string Trim(string text, bool leading, bool trailing)
    {
        var start = 0;
        var end = text.Length - 1;
        if (leading)
        {
            while (start <= end && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }

        if (trailing)
        {
            while (end >= start && char.IsWhiteSpace(text[end]))
            {
                end--;
            }
        }

        return text[start..(end + 1)];
    }
}
