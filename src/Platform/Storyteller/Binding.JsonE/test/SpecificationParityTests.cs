extern alias upstream;

using System;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.JsonE;
using Xunit;
using Xunit.Abstractions;
using UpstreamJsonE = upstream::Json.JsonE.JsonE;

namespace _42.Platform.Storyteller.Binding.JsonE.UnitTests;

/// <summary>
/// The vendored fork must behave exactly like the unpatched JsonE.Net 3.0.1 binary on the JSON-e specification suite,
/// with and without a meter.
/// </summary>
public class SpecificationParityTests
{
    private readonly ITestOutputHelper _output;

    public SpecificationParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [MemberData(nameof(SpecificationSuite.CaseIndexes), MemberType = typeof(SpecificationSuite))]
    public void Fork_WithoutMeter_MatchesUpstream(int index)
    {
        var test = SpecificationSuite.Cases[index];

        var expected = RunUpstream(test);
        var actual = Outcome.Run(() => Json.JsonE.JsonE.Evaluate(test.Template?.DeepClone(), test.CreateContext(), meter: null));

        actual.Text.Should().Be(expected.Text, test.Name);
    }

    [Theory]
    [MemberData(nameof(SpecificationSuite.CaseIndexes), MemberType = typeof(SpecificationSuite))]
    public void Fork_WithUnlimitedMeter_MatchesUpstreamAndBalancesFrames(int index)
    {
        var test = SpecificationSuite.Cases[index];
        var meter = new CountingMeter();

        var expected = RunUpstream(test);
        var actual = Outcome.Run(() => Json.JsonE.JsonE.Evaluate(test.Template?.DeepClone(), test.CreateContext(), meter));

        actual.Text.Should().Be(expected.Text, test.Name);
        meter.Depth.Should().Be(0, "every frame the interpreter enters is left again");
        if (actual.Succeeded)
        {
            meter.Ticks.Should().BeGreaterThan(0);
        }
    }

    /// <summary>
    /// Templates from the PR #57 review that the specification suite does not cover. The first case pins a JsonE.Net 3.0.1
    /// quirk: the same hole written escaped and live renders as "2 and {x}" instead of "${x} and 2".
    /// </summary>
    [Theory]
    [InlineData("""{ "k": "$${x} and ${x}" }""")]
    [InlineData("""{ "k": "$${y} and ${x}" }""")]
    [InlineData("""{ "k": "$${a ${s}}" }""")]
    [InlineData("""{ "pre$${x} and ${x}": 1, "$${x}": 2, "$$a${s}": 3 }""")]
    [InlineData("""{ "greeting": "it's ${s}", "path": "C:\\data\\${s}" }""")]
    [InlineData("""{ "$map": [1, 2, 3], "each(v)": { "$if": "v > 1", "then": { "$eval": "v" } } }""")]
    [InlineData("""{ "$switch": { "x == 3": "three", "$default": "other" } }""")]
    [InlineData("""{ "$eval": "[s[0:1] + 'x', s[:1], s[1:], {a: x + 1}, split('a0b', 0), 'b' in s]" }""")]
    [InlineData("""{ "$sort": [{ "a": 2 }, { "a": 1 }], "by(e)": "e.a + 10" }""")]
    [InlineData("""{ "$eval": "2 ** 100000" }""")]
    [InlineData("""{ "$eval": "number('0001')" }""")]
    public void Fork_MatchesUpstream_OnReviewedTemplates(string template)
    {
        var context = new JsonObject { ["x"] = 2, ["s"] = "ab" };

        var expected = Outcome.Run(() => UpstreamJsonE.Evaluate(JsonNode.Parse(template), context.DeepClone()));
        var actual = Outcome.Run(() => Json.JsonE.JsonE.Evaluate(JsonNode.Parse(template), context.DeepClone(), new CountingMeter()));

        _output.WriteLine(actual.Text);
        actual.Text.Should().Be(expected.Text);
    }

    [Fact]
    public void Fork_ConformsToSpecification_ExactlyLikeUpstream()
    {
        var cases = SpecificationSuite.Cases;
        var upstreamPasses = cases.Where(test => Conforms(test, RunUpstream(test))).Select(test => test.Name).ToList();
        var forkPasses = cases
            .Where(test => Conforms(test, Outcome.Run(() => Json.JsonE.JsonE.Evaluate(test.Template?.DeepClone(), test.CreateContext(), new CountingMeter()))))
            .Select(test => test.Name)
            .ToList();

        _output.WriteLine($"Cases: {cases.Count}. Conforming: upstream {upstreamPasses.Count}, fork {forkPasses.Count}.");
        forkPasses.Should().Equal(upstreamPasses);
        cases.Count.Should().BeGreaterThan(1000);
    }

    private static Outcome RunUpstream(SpecificationCase test)
    {
        return Outcome.Run(() => UpstreamJsonE.Evaluate(test.Template?.DeepClone(), test.CreateContext()));
    }

    private static bool Conforms(SpecificationCase test, Outcome outcome)
    {
        if (test.ExpectsError)
        {
            return !outcome.Succeeded;
        }

        return test.HasExpected && outcome.Succeeded && JsonEquivalence.AreEquivalent(test.Expected, outcome.Result);
    }

    private sealed record Outcome(bool Succeeded, JsonNode? Result, string Text)
    {
        public static Outcome Run(Func<JsonNode?> evaluate)
        {
            try
            {
                var result = evaluate();
                return new Outcome(true, result, result?.ToJsonString() ?? "null");
            }
            catch (Exception exception)
            {
                return new Outcome(false, null, $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
