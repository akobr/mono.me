using System.Reflection;
using System.Text.Json.Nodes;
using Json.Logic;

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
                if (handler is not MeteredRule)
                {
                    RuleRegistry.AddRule(name, new MeteredRule(handler));
                }
            }

            _installed = true;
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
