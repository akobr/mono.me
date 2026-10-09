using System;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.JsonE;
using Xunit;

namespace _42.Platform.Storyteller.Binding.JsonE.UnitTests;

public class MeteringHookTests
{
    [Fact]
    public void Evaluate_WithMeter_ReportsTicksAndBalancedFrames()
    {
        var meter = new CountingMeter();
        var template = JsonNode.Parse("""{ "$map": [1, 2, 3], "each(x)": { "$eval": "x + 1" } }""");

        var result = Json.JsonE.JsonE.Evaluate(template, new JsonObject(), meter);

        result!.ToJsonString().Should().Be("[2,3,4]");
        meter.Ticks.Should().BeGreaterThan(6);
        meter.Frames.Should().BeGreaterThan(3);
        meter.MaxObservedDepth.Should().BeGreaterThan(1);
        meter.Depth.Should().Be(0);
    }

    [Fact]
    public void Evaluate_MeterThrows_StopsEvaluationAndRestoresCurrent()
    {
        var meter = new CountingMeter(maxTicks: 100);
        var template = JsonNode.Parse("""{ "$eval": "len(range(0, 1000000))" }""");

        var act = () => Json.JsonE.JsonE.Evaluate(template, new JsonObject(), meter);

        act.Should().Throw<MeterStopException>().Which.Reason.Should().Be("ticks");
        Metering.Current.Should().BeNull();
    }

    [Fact]
    public void Evaluate_RestoresPreviousMeter()
    {
        var outer = new CountingMeter();
        var inner = new CountingMeter();
        Metering.Current = outer;
        try
        {
            Json.JsonE.JsonE.Evaluate(JsonNode.Parse("""{ "$eval": "1 + 1" }"""), new JsonObject(), inner);

            Metering.Current.Should().BeSameAs(outer);
            inner.Ticks.Should().BeGreaterThan(0);
            outer.Ticks.Should().Be(0);
        }
        finally
        {
            Metering.Current = null;
        }
    }

    [Fact]
    public void Evaluate_WithoutMeter_IsNotMetered()
    {
        var result = Json.JsonE.JsonE.Evaluate(JsonNode.Parse("""{ "$eval": "len(range(0, 5000))" }"""), new JsonObject(), meter: null);

        result!.ToJsonString().Should().Be("5000");
        Metering.Current.Should().BeNull();
    }

    [Fact]
    public void Range_TicksPerProducedItem()
    {
        var meter = new CountingMeter();

        Json.JsonE.JsonE.Evaluate(JsonNode.Parse("""{ "$eval": "len(range(0, 10000))" }"""), new JsonObject(), meter);

        meter.Ticks.Should().BeGreaterThanOrEqualTo(10_000);
    }

    [Fact]
    public void Interpolation_TicksPerHole()
    {
        var few = new CountingMeter();
        var many = new CountingMeter();
        var context = new JsonObject { ["s"] = "x" };

        Json.JsonE.JsonE.Evaluate(JsonValue.Create("${s}"), context.DeepClone(), few);
        Json.JsonE.JsonE.Evaluate(JsonValue.Create(string.Concat(Enumerable.Repeat("${s}", 100))), context.DeepClone(), many);

        (many.Ticks - few.Ticks).Should().BeGreaterThanOrEqualTo(99);
    }

    [Theory]
    [InlineData("""{ "$eval": "len(join(xs, ''))" }""")]
    [InlineData("""{ "$json": { "$eval": "xs" } }""")]
    [InlineData("""{ "k": "${s}${s}${s}${s}${s}${s}${s}${s}${s}${s}" }""")]
    public void SharedStringPrimitives_ReserveBeforeBuilding(string template)
    {
        // Ten references to one 100,000-character string: 1,000,000 characters (2 MB) once flattened.
        var s = new string('x', 100_000);
        var context = new JsonObject { ["s"] = s, ["xs"] = new JsonArray(Enumerable.Range(0, 10).Select(_ => (JsonNode?)JsonValue.Create(s)).ToArray()) };
        var meter = new CountingMeter(maxReservedBytes: 1_000_000);

        var act = () => Json.JsonE.JsonE.Evaluate(JsonNode.Parse(template), context, meter);

        act.Should().Throw<MeterStopException>().Which.Reason.Should().Be("reserve");
    }

    [Fact]
    public void SharedStringPrimitives_ReserveTheirResultSize()
    {
        var meter = new CountingMeter();
        var context = new JsonObject { ["s"] = "abc", ["xs"] = new JsonArray("ab", "cd") };

        var joined = Json.JsonE.JsonE.Evaluate(JsonNode.Parse("""{ "$eval": "join(xs, '-')" }"""), context.DeepClone(), meter);
        var interpolated = Json.JsonE.JsonE.Evaluate(JsonValue.Create("${s}/${s}"), context.DeepClone(), meter);

        joined!.ToJsonString().Should().Be("\"ab-cd\"");
        interpolated!.ToJsonString().Should().Be("\"abc/abc\"");
        meter.MaxReservedBytes.Should().BeInRange(10, 1_000);
    }

    [Theory]
    [InlineData("!", 15_000, "true")]
    [InlineData("-", 50_000, "1")]
    public void Parse_DeepUnaryChain_StopsOnFramesInsteadOfStackOverflow(string op, int count, string operand)
    {
        var meter = new CountingMeter(maxDepth: 256);
        var template = new JsonObject { ["$eval"] = string.Concat(Enumerable.Repeat(op, count)) + operand };

        var act = () => Json.JsonE.JsonE.Evaluate(template, new JsonObject(), meter);

        act.Should().Throw<MeterStopException>().Which.Reason.Should().Be("depth");
    }

    [Fact]
    public void Parse_NestedArrayLiteral_StopsOnFrames()
    {
        var meter = new CountingMeter(maxDepth: 256);
        var template = new JsonObject { ["$eval"] = new string('[', 5_000) + "1" + new string(']', 5_000) };

        var act = () => Json.JsonE.JsonE.Evaluate(template, new JsonObject(), meter);

        act.Should().Throw<MeterStopException>().Which.Reason.Should().Be("depth");
    }

    [Fact]
    public void Evaluate_LeftDeepBinaryChain_StopsOnFrames()
    {
        // The parser builds the chain iteratively, but evaluation recurses once per term.
        var meter = new CountingMeter(maxDepth: 256);
        var template = new JsonObject { ["$eval"] = string.Join("+", Enumerable.Repeat("1", 50_000)) };

        var act = () => Json.JsonE.JsonE.Evaluate(template, new JsonObject(), meter);

        act.Should().Throw<MeterStopException>().Which.Reason.Should().Be("depth");
    }

    [Fact]
    public void Evaluate_ShallowLeftDeepChain_Succeeds()
    {
        var meter = new CountingMeter(maxDepth: 256);
        var template = new JsonObject { ["$eval"] = string.Join("+", Enumerable.Repeat("1", 200)) };

        var result = Json.JsonE.JsonE.Evaluate(template, new JsonObject(), meter);

        result!.ToJsonString().Should().Be("200");
        meter.Depth.Should().Be(0);
    }
}
