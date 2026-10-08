using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Json.More;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Replaces the model-less JSON Logic handlers that can grow a result or scan a value without a library limit.
/// Growth rules check the size while the result is built and do not call the original handler.
/// Comparison and arithmetic rules charge the scan, then use the library comparison.
/// </summary>
internal static class JsonLogicBoundedRules
{
    // The handlers are process-wide singletons, so each call points this thread at the read's budget.
    [ThreadStatic]
    private static ConfigurationBindingResolver.EvaluationBudget? _read;

    public static void UseBudget(ConfigurationBindingResolver.EvaluationBudget read)
    {
        _read = read;
    }

    public static void Register()
    {
        RuleRegistry.AddRule("cat", new Cat());
        RuleRegistry.AddRule("merge", new Merge());
        RuleRegistry.AddRule("reduce", new Reduce());
        RuleRegistry.AddRule("map", new Map());
        RuleRegistry.AddRule("all", new All());
        RuleRegistry.AddRule("some", new Some());
        RuleRegistry.AddRule("none", new None());
        RuleRegistry.AddRule("filter", new Filter());
        RuleRegistry.AddRule("in", new In());
        RuleRegistry.AddRule("==", new LooseEquals());
        RuleRegistry.AddRule("!=", new LooseNotEquals());
        AddLibraryRule("===", strict: true, invert: false);
        AddLibraryRule("!==", strict: true, invert: true);
        AddLibraryRule("<", strict: false, invert: false);
        AddLibraryRule("<=", strict: false, invert: false);
        AddLibraryRule(">", strict: false, invert: false);
        AddLibraryRule(">=", strict: false, invert: false);
        AddLibraryRule("+", strict: false, invert: false);
        AddLibraryRule("-", strict: false, invert: false);
        AddLibraryRule("*", strict: false, invert: false);
        AddLibraryRule("/", strict: false, invert: false);
        AddLibraryRule("%", strict: false, invert: false);
        AddLibraryRule("min", strict: false, invert: false);
        AddLibraryRule("max", strict: false, invert: false);
    }

    private static void AddLibraryRule(string name, bool strict, bool invert)
    {
        var inner = RuleRegistry.GetHandler(name)
            ?? throw new InvalidOperationException($"JSON Logic has no '{name}' rule.");
        RuleRegistry.AddRule(name, new Metered(inner, strict, invert));
    }

    private static void CountIteration()
    {
        (_read ?? throw new BindingEvaluationException("JSON Logic evaluation has no step budget.")).AddStep();
    }

    private static void Charge(int count)
    {
        (_read ?? throw new BindingEvaluationException("JSON Logic evaluation has no step budget.")).AddSteps(count);
    }

    private static void ChargeStructure(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                Charge(array.Count);
                foreach (var item in array)
                {
                    ChargeStructure(item);
                }

                break;
            case JsonObject obj:
                Charge(obj.Count);
                foreach (var property in obj)
                {
                    ChargeStructure(property.Value);
                }

                break;
            case JsonValue value when value.TryGetValue(out string? text):
                Charge(text?.Length ?? 0);
                break;
        }
    }

    private static string Describe(JsonNode? node)
    {
        return node switch
        {
            null => "null",
            JsonObject => "object",
            JsonArray => "array",
            JsonValue value when value.TryGetValue(out string? _) => "string",
            JsonValue value when value.TryGetValue(out bool _) => "boolean",
            _ => "number",
        };
    }

    private static bool ApplyElement(JsonNode? rule, JsonNode? element, EvaluationContext context)
    {
        CountIteration();
        context.Push(element);
        try
        {
            return JsonLogic.Apply(rule, context).IsTruthy();
        }
        finally
        {
            context.Pop();
        }
    }

    private sealed class Cat : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray array)
            {
                return args;
            }

            var result = new StringBuilder();
            foreach (var item in array)
            {
                var value = JsonLogic.Apply(item, context);
                var str = value.Stringify() ?? throw new JsonLogicException("Cannot concatenate object.");
                if ((long)result.Length + str.Length > ConfigurationBindingResolver.MaxConcatLength)
                {
                    throw new BindingEvaluationException(
                        $"JSON Logic cat exceeds {ConfigurationBindingResolver.MaxConcatLength} characters.");
                }

                result.Append(str);
            }

            return result.ToString();
        }
    }

    private sealed class Merge : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray array)
            {
                return JsonLogic.Apply(new JsonArray(args?.DeepClone()), context);
            }

            var result = new JsonArray();
            foreach (var item in array)
            {
                var applied = JsonLogic.Apply(item, context);
                foreach (var flat in applied.Flatten())
                {
                    if (result.Count >= ConfigurationBindingResolver.MaxMergeItems)
                    {
                        throw new BindingEvaluationException(
                            $"JSON Logic merge exceeds {ConfigurationBindingResolver.MaxMergeItems} items.");
                    }

                    result.Add(flat?.DeepClone());
                }
            }

            return result;
        }
    }

    private sealed class Reduce : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 3 } array)
            {
                throw new JsonException("The 'reduce' rule needs an array with 3 parameters.");
            }

            var input = JsonLogic.Apply(array[0], context);
            var rule = array[1];
            var accumulator = JsonLogic.Apply(array[2], context);
            if (input is not JsonArray items)
            {
                return accumulator;
            }

            if (items.Count > ConfigurationBindingResolver.MaxReduceItems)
            {
                throw new BindingEvaluationException(
                    $"JSON Logic reduce exceeds {ConfigurationBindingResolver.MaxReduceItems} items.");
            }

            foreach (var element in items)
            {
                CountIteration();
                var intermediary = new JsonObject
                {
                    ["current"] = element?.DeepClone(),
                    ["accumulator"] = accumulator?.DeepClone(),
                };

                context.Push(intermediary);
                try
                {
                    accumulator = JsonLogic.Apply(rule, context);
                }
                finally
                {
                    context.Pop();
                }

                if (accumulator is null)
                {
                    break;
                }

                if (EvaluationSize.SerializedLength(accumulator) > ConfigurationBindingResolver.MaxConcatLength)
                {
                    throw new BindingEvaluationException(
                        $"JSON Logic reduce value exceeds {ConfigurationBindingResolver.MaxConcatLength} characters.");
                }
            }

            return accumulator;
        }
    }

    private sealed class Map : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 2 } array)
            {
                throw new JsonLogicException("The 'map' rule requires an array with two arguments");
            }

            var input = JsonLogic.Apply(array[0], context);
            var rule = array[1];
            if (input is not JsonArray items)
            {
                return new JsonArray();
            }

            var result = new JsonArray();
            var used = 0;
            foreach (var element in items)
            {
                CountIteration();
                context.Push(element);
                JsonNode? mapped;
                try
                {
                    mapped = JsonLogic.Apply(rule, context);
                }
                finally
                {
                    context.Pop();
                }

                var length = EvaluationSize.SerializedLength(mapped);
                if ((long)used + length > ConfigurationBindingResolver.MaxConcatLength)
                {
                    throw new BindingEvaluationException(
                        $"JSON Logic map exceeds {ConfigurationBindingResolver.MaxConcatLength} characters.");
                }

                used += length;
                result.Add(mapped?.DeepClone());
            }

            return result;
        }
    }

    private sealed class All : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 2 } array)
            {
                throw new JsonLogicException("The 'all' rule requires an array with two arguments");
            }

            var input = JsonLogic.Apply(array[0], context);
            var rule = array[1];
            if (input is not JsonArray { Count: > 0 } items)
            {
                return false;
            }

            foreach (var element in items)
            {
                if (!ApplyElement(rule, element, context))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class Some : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 2 } array)
            {
                throw new JsonLogicException("The 'some' rule requires an array with two arguments");
            }

            var input = JsonLogic.Apply(array[0], context);
            var rule = array[1];
            if (input is not JsonArray items)
            {
                return false;
            }

            foreach (var element in items)
            {
                if (ApplyElement(rule, element, context))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class None : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 2 } array)
            {
                throw new JsonLogicException("The 'none' rule requires an array with two arguments");
            }

            var input = JsonLogic.Apply(array[0], context);
            var rule = array[1];
            if (input is not JsonArray items)
            {
                return true;
            }

            foreach (var element in items)
            {
                if (ApplyElement(rule, element, context))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class Filter : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 2 } array)
            {
                throw new JsonLogicException("The 'filter' rule requires an array with two arguments");
            }

            var input = JsonLogic.Apply(array[0], context);
            var rule = array[1];
            if (input is not JsonArray items)
            {
                return false;
            }

            var result = new JsonArray();
            foreach (var element in items)
            {
                if (ApplyElement(rule, element, context))
                {
                    result.Add(element?.DeepClone());
                }
            }

            return result;
        }
    }

    private sealed class In : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 2 } array)
            {
                throw new JsonLogicException("The 'in' rule requires an array with 2 parameters");
            }

            var test = JsonLogic.Apply(array[0], context);
            var source = JsonLogic.Apply(array[1], context);
            if (source is JsonValue value && value.TryGetValue(out string? stringSource))
            {
                var stringTest = test.Stringify();
                if (stringTest == null)
                {
                    throw new JsonLogicException($"Cannot check string for {Describe(test)}.");
                }

                if (string.IsNullOrEmpty(stringTest))
                {
                    return false;
                }

                var haystack = stringSource ?? string.Empty;
                Charge(haystack.Length);
                return haystack.Contains(stringTest);
            }

            if (source is JsonArray arr)
            {
                ChargeStructure(arr);
                foreach (var item in arr)
                {
                    if (item is null || test is null)
                    {
                        if (item is null && test is null)
                        {
                            return true;
                        }

                        continue;
                    }

                    if (item.IsEquivalentTo(test))
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }
    }

    private sealed class LooseEquals : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 2 } array)
            {
                throw new JsonLogicException("The '==' rule needs an array with 2 parameters");
            }

            var left = JsonLogic.Apply(array[0], context);
            var right = JsonLogic.Apply(array[1], context);
            if (left is JsonArray || right is JsonArray)
            {
                ChargeStructure(left);
                ChargeStructure(right);
            }

            return left.LooseEquals(right);
        }
    }

    private sealed class LooseNotEquals : IRule
    {
        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (args is not JsonArray { Count: 2 } array)
            {
                throw new JsonLogicException("The '!=' rule needs an array with 2 parameters");
            }

            var left = JsonLogic.Apply(array[0], context);
            var right = JsonLogic.Apply(array[1], context);
            if (left is JsonArray || right is JsonArray)
            {
                ChargeStructure(left);
                ChargeStructure(right);
            }

            return !left.LooseEquals(right);
        }
    }

    private sealed class Metered : IRule
    {
        private readonly IRule _inner;
        private readonly bool _strict;
        private readonly bool _invert;

        public Metered(IRule inner, bool strict, bool invert)
        {
            _inner = inner;
            _strict = strict;
            _invert = invert;
        }

        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            if (_strict)
            {
                if (args is not JsonArray { Count: 2 } pair)
                {
                    return _inner.Apply(args, context);
                }

                var left = JsonLogic.Apply(pair[0], context);
                var right = JsonLogic.Apply(pair[1], context);
                ChargeStrict(left, right);
                var same = left.IsEquivalentTo(right);
                return _invert ? !same : same;
            }

            if (args is not JsonArray array)
            {
                var single = JsonLogic.Apply(args, context);
                ChargeOperand(single);
                return _inner.Apply(CopyValue(single), context);
            }

            var evaluated = new JsonNode?[array.Count];
            for (var index = 0; index < array.Count; index++)
            {
                evaluated[index] = JsonLogic.Apply(array[index], context);
                ChargeOperand(evaluated[index]);
            }

            var copy = new JsonArray();
            foreach (var value in evaluated)
            {
                copy.Add(CopyValue(value));
            }

            return _inner.Apply(copy, context);
        }

        private static void ChargeStrict(JsonNode? left, JsonNode? right)
        {
            if (left is JsonArray or JsonObject || right is JsonArray or JsonObject)
            {
                ChargeStructure(left);
                ChargeStructure(right);
                return;
            }

            if (left is JsonValue leftValue && leftValue.TryGetValue(out string? leftText) &&
                right is JsonValue rightValue && rightValue.TryGetValue(out string? rightText))
            {
                Charge(leftText?.Length ?? 0);
                Charge(rightText?.Length ?? 0);
            }
        }

        private static void ChargeOperand(JsonNode? node)
        {
            if (node is JsonArray or JsonObject)
            {
                ChargeStructure(node);
                return;
            }

            if (node is JsonValue value && value.TryGetValue(out string? text))
            {
                Charge(text?.Length ?? 0);
            }
        }

        private static JsonNode? CopyValue(JsonNode? node)
        {
            if (node is null)
            {
                return null;
            }

            if (node is JsonValue value && value.TryGetValue(out string? text))
            {
                return text;
            }

            return node.DeepClone();
        }
    }
}
