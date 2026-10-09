using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Logic;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

public class JsonLogicMeteringTests
{
    [Fact]
    public void Install_WrapsEveryBuiltInRule()
    {
        JsonLogicMetering.Install();

        JsonLogicMetering.Operators.Should().HaveCount(35, "JsonLogic 6.1.0 declares 35 operators; review the wrapper when this changes");
        foreach (var name in JsonLogicMetering.Operators)
        {
            RuleRegistry.GetHandler(name).Should().BeOfType<JsonLogicMetering.MeteredRule>(name);
        }
    }

    [Fact]
    public void Install_IsIdempotent()
    {
        JsonLogicMetering.Install();
        JsonLogicMetering.Install();

        RuleRegistry.GetHandler("+").Should().BeOfType<JsonLogicMetering.MeteredRule>();
    }

    [Fact]
    public void MeteredRule_OutsideABindingRead_PassesThrough()
    {
        JsonLogicMetering.Install();

        var result = JsonLogic.Apply(JsonNode.Parse("""{ "+": [1, { "var": "a" }] }"""), JsonNode.Parse("""{ "a": 2 }"""));

        result!.GetValue<decimal>().Should().Be(3);
        EvaluationMeter.Current.Should().BeNull();
    }

    [Fact]
    public void MeteredRule_InsideABindingRead_TicksPerRule()
    {
        JsonLogicMetering.Install();
        var meter = new EvaluationMeter(ObjectBindingLimits.Default, System.TimeProvider.System);

        using (meter.Enter())
        {
            JsonLogic.Apply(JsonNode.Parse("""{ "map": [[1, 2, 3], { "+": [{ "var": "" }, 1] }] }"""), new JsonObject());
        }

        // map, then per item: + and var.
        meter.Steps.Should().Be(1 + (3 * 2));
    }

    [Fact]
    public void BindingObject_ReferencesVendoredJsonE_NotThePackage()
    {
        var references = typeof(ConfigurationBindingResolver).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToList();

        references.Should().Contain("42.Platform.Storyteller.Binding.JsonE");
        references.Should().NotContain("JsonE.Net");
    }
}
