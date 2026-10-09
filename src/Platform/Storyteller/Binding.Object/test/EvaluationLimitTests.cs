using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;
using static _42.Platform.Storyteller.Binding.Object.UnitTests.Fixtures;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// One test per <see cref="EvaluationLimitKind"/>, with small limits passed to the resolver.
/// </summary>
public class EvaluationLimitTests
{
    private readonly FakeStringBinding _strings = new();
    private readonly BindingScope _scope = new() { Context = "test-configuration" };

    [Fact]
    public async Task Steps_JsonE_ThrowsWithKindLimitAndPath()
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxSteps = 1_000 });
        var content = Envelope("jsone", new JObject
        {
            ["$map"] = new JObject { ["$eval"] = "range(0, 2000)" },
            ["each(x)"] = new JObject { ["$eval"] = "x" },
        });

        var thrown = await ResolveThrows(resolver, content);

        thrown.Kind.Should().Be(EvaluationLimitKind.Steps);
        thrown.Limit.Should().Be(1_000);
        thrown.Path.Should().Be("value");
        thrown.Message.Should().StartWith("Failed to process the object binding for 'value': ").And.Contain("1000 steps");
    }

    [Fact]
    public async Task Steps_JsonLogic_Throws()
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxSteps = 1_000 });
        var content = Envelope("jlogic", Parse("""{ "map": [{ "var": "xs" }, { "+": [{ "var": "" }, 1] }] }"""), new JObject { ["xs"] = Sequence(2_000) });

        var thrown = await ResolveThrows(resolver, content);

        thrown.Kind.Should().Be(EvaluationLimitKind.Steps);
    }

    [Fact]
    public async Task Steps_AreSharedBySiblingEnvelopesOfOneRead()
    {
        JObject Sibling() => EnvelopeToken("jsone", new JObject
        {
            ["$map"] = Sequence(50),
            ["each(x)"] = new JObject { ["$eval"] = "x + 1" },
        });

        // Measure one envelope's steps through the read's debug summary, then allow exactly that many.
        var logger = new CapturingLogger();
        await Resolver(ObjectBindingLimits.Default, logger: logger).ResolveAsync(new JObject { ["a"] = Sibling() }, includeSecrets: true, _scope);

        // JSON Logic `log` output of parallel tests can reach this logger through the process-wide sink; pick the read summary.
        var single = Convert.ToInt64(logger.Entries.Single(entry => entry.State.ContainsKey("Steps")).State["Steps"]);
        single.Should().BeGreaterThan(50);

        var limits = ObjectBindingLimits.Default with { MaxSteps = single };
        await Resolver(limits).ResolveAsync(new JObject { ["a"] = Sibling() }, includeSecrets: true, _scope);

        var thrown = await ResolveThrows(Resolver(limits), new JObject { ["a"] = Sibling(), ["b"] = Sibling() });
        thrown.Kind.Should().Be(EvaluationLimitKind.Steps);
        thrown.Path.Should().Be("b");
    }

    [Fact]
    public async Task Memory_Throws()
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxAllocatedBytes = 1L << 20 });
        var content = Envelope("jsone", new JObject { ["$eval"] = "len(range(0, 200000))" });

        var thrown = await ResolveThrows(resolver, content);

        thrown.Kind.Should().Be(EvaluationLimitKind.Memory);
        thrown.Limit.Should().Be(1L << 20);
    }

    [Fact]
    public async Task Time_Throws()
    {
        // Every timestamp read advances the clock by 1 ms, so the work itself takes no real time.
        // The meter reads the clock every 16 steps: 2000 steps read it about 125 times.
        var resolver = Resolver(
            ObjectBindingLimits.Default with { MaxEvaluationTime = TimeSpan.FromMilliseconds(50) },
            new SteppingTimeProvider(TimeSpan.FromMilliseconds(1)));
        var content = Envelope("jlogic", Parse("""{ "map": [{ "var": "xs" }, { "var": "" }] }"""), new JObject { ["xs"] = Sequence(2_000) });

        var thrown = await ResolveThrows(resolver, content);

        thrown.Kind.Should().Be(EvaluationLimitKind.Time);
        thrown.Limit.Should().Be(50);
    }

    [Fact]
    public async Task Time_CountsOnlyEngineCalls()
    {
        // 10 envelopes of a few ticks each: far below 50 timestamp reads in total.
        var resolver = Resolver(
            ObjectBindingLimits.Default with { MaxEvaluationTime = TimeSpan.FromMilliseconds(200) },
            new SteppingTimeProvider(TimeSpan.FromMilliseconds(1)));
        var content = new JObject();
        for (var index = 0; index < 10; index++)
        {
            content["v" + index] = EnvelopeToken("jlogic", Parse("""{ "+": [1, 2] }"""));
        }

        await resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["v9"]!.Value<decimal>().Should().Be(3);
    }

    [Theory]
    [InlineData("jsone")]
    [InlineData("jlogic")]
    public async Task Depth_Throws(string kind)
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxDepth = 16 });
        JToken definition = kind == "jsone"
            ? new JObject { ["$eval"] = new string('!', 40) + "true" }
            : NestedNot(40);

        var thrown = await ResolveThrows(resolver, Envelope(kind, definition));

        thrown.Kind.Should().Be(EvaluationLimitKind.Depth);
        thrown.Limit.Should().Be(16);
    }

    [Fact]
    public async Task Depth_BelowLimit_Succeeds()
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxDepth = 64 });
        var content = Envelope("jsone", new JObject { ["$eval"] = new string('!', 10) + "true" });

        await resolver.ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ResultLength_Throws()
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxResultLength = 100 });
        var content = Envelope("jsone", new JObject { ["$eval"] = "[s,s]" }, new JObject { ["s"] = new string('x', 60) });

        var thrown = await ResolveThrows(resolver, content);

        thrown.Kind.Should().Be(EvaluationLimitKind.Result);
        thrown.Message.Should().Contain("100 characters");
    }

    [Fact]
    public async Task ResultDepth_Throws()
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxResultDepth = 4 });

        // Five nested objects: o, a, b, c, d.
        var content = Envelope("jlogic", Parse("""{ "var": "o" }"""), Parse("""{ "o": { "a": { "b": { "c": { "d": { "e": 1 } } } } } }"""));

        var thrown = await ResolveThrows(resolver, content);

        thrown.Kind.Should().Be(EvaluationLimitKind.Result);
        thrown.Message.Should().Contain("deeper than 4");
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public async Task ResultDepth_DefaultMatchesTokenConversion(int depth, bool succeeds)
    {
        // A result is converted back with JsonTextReader (MaxDepth 64); the check must fail first and cleanly.
        var resolver = Resolver(ObjectBindingLimits.Default);
        var content = Envelope("jsone", new JObject
        {
            ["$reduce"] = new JObject { ["$eval"] = $"range(0, {depth - 1})" },
            ["initial"] = new JArray(),
            ["each(acc,i)"] = new JObject { ["$eval"] = "[acc]" },
        });

        if (succeeds)
        {
            await resolver.ResolveAsync(content, includeSecrets: true, _scope);
            content["value"].Should().BeOfType<JArray>();
        }
        else
        {
            var thrown = await ResolveThrows(resolver, content);
            thrown.Kind.Should().Be(EvaluationLimitKind.Result);
        }
    }

    [Fact]
    public async Task EnvelopeDepth_Throws()
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxEnvelopeDepth = 3 });

        var thrown = await ResolveThrows(resolver, new JObject { ["value"] = Chain(4) });

        thrown.Kind.Should().Be(EvaluationLimitKind.EnvelopeDepth);
        thrown.Limit.Should().Be(3);
    }

    [Fact]
    public async Task EnvelopeCount_Throws()
    {
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxEnvelopeEvaluations = 6 });

        var thrown = await ResolveThrows(resolver, new JObject { ["value"] = FanOut(3) });

        thrown.Kind.Should().Be(EvaluationLimitKind.EnvelopeCount);
        thrown.Limit.Should().Be(6);
    }

    [Fact]
    public async Task LimitExceeded_LogsWarningWithMeterTotals()
    {
        var logger = new CapturingLogger(LogLevel.Information);
        var resolver = Resolver(ObjectBindingLimits.Default with { MaxSteps = 100 }, logger: logger);
        var content = Envelope("jsone", new JObject { ["$eval"] = "len(range(0, 1000))" });

        await ResolveThrows(resolver, content);

        var warning = logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning).Subject;
        warning.State["Kind"].Should().Be(EvaluationLimitKind.Steps);
        warning.State["Path"].Should().Be("value");
        warning.State["Configuration"].Should().Be("test-configuration");
        Convert.ToInt64(warning.State["Steps"]).Should().BeGreaterThan(100);
    }

    [Fact]
    public void Limits_MustBePositive()
    {
        var act = () => Resolver(ObjectBindingLimits.Default with { MaxSteps = 0 });

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*MaxSteps*");
    }

    [Fact]
    public void Meter_LatchesTheFirstExceededLimit()
    {
        var meter = new EvaluationMeter(ObjectBindingLimits.Default with { MaxSteps = 2 }, TimeProvider.System);
        using (meter.Enter())
        {
            meter.Tick();
            meter.Tick();
            var first = () => meter.Tick();
            first.Should().Throw<EvaluationLimitExceededException>().Which.Kind.Should().Be(EvaluationLimitKind.Steps);

            // A library that swallowed the exception cannot continue: every later report throws again.
            var again = () => meter.Tick(0);
            again.Should().Throw<EvaluationLimitExceededException>().Which.Kind.Should().Be(EvaluationLimitKind.Steps);
            var frame = () => meter.EnterFrame();
            frame.Should().Throw<EvaluationLimitExceededException>();
        }

        var after = () => meter.ThrowIfExceeded();
        after.Should().Throw<EvaluationLimitExceededException>();
        EvaluationMeter.Current.Should().BeNull();
    }

    [Fact]
    public void Meter_AccumulatesAcrossScopes()
    {
        var meter = new EvaluationMeter(ObjectBindingLimits.Default, TimeProvider.System);
        using (meter.Enter())
        {
            meter.Tick(10);
            EvaluationMeter.Current.Should().BeSameAs(meter);
        }

        EvaluationMeter.Current.Should().BeNull();
        using (meter.Enter())
        {
            meter.Tick(5);
        }

        meter.Steps.Should().Be(15);
        meter.AllocatedBytes.Should().BeGreaterThanOrEqualTo(0);
        meter.Exceeded.Should().BeNull();
    }

    [Fact]
    public void Meter_RejectsNestedScopes()
    {
        var meter = new EvaluationMeter(ObjectBindingLimits.Default, TimeProvider.System);
        using (meter.Enter())
        {
            var nested = () => meter.Enter();
            nested.Should().Throw<InvalidOperationException>();
        }
    }

    private static JToken NestedNot(int depth)
    {
        JToken rule = true;
        for (var level = 0; level < depth; level++)
        {
            rule = new JObject { ["!"] = new JArray { rule } };
        }

        return rule;
    }

    private ConfigurationBindingResolver Resolver(
        ObjectBindingLimits limits,
        TimeProvider? timeProvider = null,
        CapturingLogger? logger = null)
    {
        return new ConfigurationBindingResolver(_strings, logger, limits, timeProvider);
    }

    private async Task<EvaluationLimitExceededException> ResolveThrows(ConfigurationBindingResolver resolver, JObject content)
    {
        var act = () => resolver.ResolveAsync(content, includeSecrets: true, _scope).AsTask();
        var thrown = await act.Should().ThrowAsync<EvaluationLimitExceededException>();
        return thrown.Which;
    }
}
