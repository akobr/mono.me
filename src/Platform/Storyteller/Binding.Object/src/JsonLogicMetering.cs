using System.Reflection;
using System.Text.Json.Nodes;
using Json.Logic;
using Json.More;
using OrdinalSearch = Json.JsonE.OrdinalSearch;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Wraps every built-in JSON Logic rule so that each rule invocation reports to the read's <see cref="EvaluationMeter"/>.
/// Every rule object dispatches through <see cref="RuleRegistry"/>, so the wrapper sees all of them.
/// Outside an object-binding engine call the wrapper passes through.
/// </summary>
internal static class JsonLogicMetering
{
    private static readonly object Gate = new();
    private static bool _installed;

    /// <summary>
    /// Gets the operator names declared by the <c>JsonLogic</c> assembly.
    /// <see cref="RuleRegistry"/> cannot enumerate its handlers, so the names come from <see cref="OperatorAttribute"/>.
    /// </summary>
    public static IReadOnlyList<string> Operators { get; } = typeof(JsonLogic).Assembly
        .GetTypes()
        .Where(type => typeof(IRule).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
        .SelectMany(type => type.GetCustomAttributes<OperatorAttribute>())
        .Select(attribute => attribute.Name)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static void Install()
    {
        lock (Gate)
        {
            if (_installed)
            {
                return;
            }

            foreach (var name in Operators)
            {
                var handler = RuleRegistry.GetHandler(name)
                    ?? throw new InvalidOperationException($"JSON Logic has no handler for the '{name}' rule.");
                if (handler is MeteredRule)
                {
                    continue;
                }

                if (name == "in")
                {
                    handler = new LinearInRule();
                }

                RuleRegistry.AddRule(name, new MeteredRule(handler));
            }

            _installed = true;
        }
    }

    /// <summary>
    /// The library's <c>in</c> rule (JsonLogic 6.1.0), with <see cref="OrdinalSearch"/> instead of
    /// <see cref="string.Contains(string)"/> for the string case, so one call is linear in its operands.
    /// </summary>
    internal sealed class LinearInRule : IRule
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
                // Stringify returns null only for an object; the library reports its JSON type.
                var stringTest = test.Stringify();
                if (stringTest == null || stringSource == null)
                {
                    throw new JsonLogicException("Cannot check string for object.");
                }

                return !string.IsNullOrEmpty(stringTest) && OrdinalSearch.Contains(stringSource, stringTest);
            }

            if (source is JsonArray items)
            {
                return items.Any(item => item.IsEquivalentTo(test));
            }

            return false;
        }
    }

    internal sealed class MeteredRule : IRule
    {
        private readonly IRule _inner;

        public MeteredRule(IRule inner)
        {
            _inner = inner;
        }

        public JsonNode? Apply(JsonNode? args, EvaluationContext context)
        {
            var meter = EvaluationMeter.Current;
            if (meter is null)
            {
                return _inner.Apply(args, context);
            }

            meter.Tick();
            meter.EnterFrame();
            try
            {
                return _inner.Apply(args, context);
            }
            finally
            {
                meter.ExitFrame();
            }
        }
    }
}
