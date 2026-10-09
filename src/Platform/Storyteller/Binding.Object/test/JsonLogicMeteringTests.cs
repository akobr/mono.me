using System;
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

    [Theory]
    [InlineData("""["foo", "foobar"]""")]
    [InlineData("""["baz", "foobar"]""")]
    [InlineData("""["", "foobar"]""")]
    [InlineData("""[1, "a1b"]""")]
    [InlineData("""[true, "xtruex"]""")]
    [InlineData("""[null, "abc"]""")]
    [InlineData("""[["a", "b"], "xa,by"]""")]
    [InlineData("""[{ "var": "o" }, "abc"]""")]
    [InlineData("""["a", ["b", "a"]]""")]
    [InlineData("""[1, [1.0, 2]]""")]
    [InlineData("""[{ "var": "o" }, [{ "a": 1 }]]""")]
    [InlineData("""["a", 5]""")]
    [InlineData("""["a", null]""")]
    [InlineData("""["a"]""")]
    [InlineData("\"a\"")]
    public void LinearInRule_MatchesTheLibraryRule(string args)
    {
        var library = (IRule)Activator.CreateInstance(typeof(Json.Logic.Rules.InRule), nonPublic: true)!;
        var linear = new JsonLogicMetering.LinearInRule();

        var expected = Outcome(() => library.Apply(JsonNode.Parse(args), Context()));
        var actual = Outcome(() => linear.Apply(JsonNode.Parse(args), Context()));

        actual.Should().Be(expected);
    }

    [Fact]
    public void BindingObject_ReferencesVendoredJsonE_NotThePackage()
    {
        var references = typeof(ConfigurationBindingResolver).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToList();

        references.Should().Contain("42.Platform.Storyteller.Binding.JsonE");
        references.Should().NotContain("JsonE.Net");
    }

    private static EvaluationContext Context()
    {
        // The constructor is internal; JsonLogic.Apply builds the same context from the data.
        return (EvaluationContext)Activator.CreateInstance(
            typeof(EvaluationContext),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            binder: null,
            args: new object?[] { JsonNode.Parse("""{ "o": { "a": 1 } }""") },
            culture: null)!;
    }

    private static string Outcome(Func<JsonNode?> apply)
    {
        try
        {
            return apply()?.ToJsonString() ?? "null";
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }
}
