using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Json.More;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Replaces the model-less JSON Logic handlers that can grow a result without a library limit.
/// Size is checked while the result is built. The original handler is not called first.
/// </summary>
internal static class JsonLogicBoundedRules
{
    // The handlers are process-wide singletons, so the iteration count lives on the calling thread.
    [ThreadStatic]
    private static int _iterations;

    public static void ResetIterations()
    {
        _iterations = 0;
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
    }

    private static void CountIteration()
    {
        if (++_iterations > ConfigurationBindingResolver.MaxEvaluationSteps)
        {
            throw new BindingEvaluationException(
                $"JSON Logic evaluation exceeds {ConfigurationBindingResolver.MaxEvaluationSteps} iterations.");
        }
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
}
