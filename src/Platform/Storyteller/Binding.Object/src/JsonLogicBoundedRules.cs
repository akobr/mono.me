using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Replaces the model-less JSON Logic handlers that can grow a result without a library limit.
/// Size is checked while the result is built. The original handler is not called first.
/// </summary>
internal static class JsonLogicBoundedRules
{
    public static void Register()
    {
        RuleRegistry.AddRule("cat", new Cat());
        RuleRegistry.AddRule("merge", new Merge());
        RuleRegistry.AddRule("reduce", new Reduce());
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
            }

            return accumulator;
        }
    }
}
