using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Json.JsonE;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

internal static class JsonELimits
{
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
            if (JsonEExpressionRewriter.TryRewriteInterpolation(property.Name, out var rewritten))
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

            obj[name] = BoundWrap(value);
        }
    }

    private static bool ShouldBound(string name, JToken value, bool isMapping)
    {
        // $default makes the cases object look like an operator. The case values are wrapped on their own.
        if (name == "$switch")
        {
            return false;
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
                    ["$eval"] = JsonEExpressionRewriter.BoundFunction + "(v[0])",
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
        text = string.Empty;
        if (node is not JsonValue value)
        {
            return false;
        }

        var json = value.ToJsonString();
        if (json.Length == 0 || json[0] != '"')
        {
            return false;
        }

        return value.TryGetValue(out text!);
    }
}
