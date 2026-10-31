using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

/// <summary>
/// The JSONPath query of <c>@config</c> and <c>@annotation</c> can be computed (a stored string read by a nested statement),
/// so the limits of the <c>@</c> string do not cover it, and Newtonsoft's parser recurses once per nested filter.
/// </summary>
public class JsonQueryLimitsTests
{
    private static readonly JObject Items = JObject.Parse("""{ "items": [ { "name": "a" } ] }""");

    [Fact]
    public void Resolve_QueryLongerThanTheCap_ThrowsExpressionLength()
    {
        var query = "$." + new string('a', BindingExpressionLimits.MaxLength - 1);

        var act = () => JsonQuery.Resolve(Items, query);

        var thrown = act.Should().Throw<EvaluationLimitExceededException>().Which;
        thrown.Kind.Should().Be(EvaluationLimitKind.ExpressionLength);
        thrown.Message.Should().Be("The JSONPath query is longer than 8192 characters.");
    }

    [Theory]
    [InlineData("$.items", 63, "[0]")]
    [InlineData("$", 64, ".a")]
    public void Resolve_SixtyFourSegments_AreRead(string start, int count, string unit)
    {
        var query = start + string.Concat(Enumerable.Repeat(unit, count));

        var act = () => JsonQuery.Resolve(Items, query);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("$.items", 64, "[0]")]
    [InlineData("$.items", 32, "[?(")]
    [InlineData("$", 65, ".a")]
    [InlineData("$", 4095, ".a")]
    [InlineData("$", 65, ".*")]
    public void Resolve_MoreThanSixtyFourSegmentsBracketsAndParentheses_ThrowsDepth(string start, int count, string unit)
    {
        // Newtonsoft's parser recurses per nested '[' and '(', its evaluation per segment; 4,095 '.a' overflowed a 1 MB stack.
        var query = start + string.Concat(Enumerable.Repeat(unit, count));

        var act = () => JsonQuery.Resolve(Items, query);

        var thrown = act.Should().Throw<EvaluationLimitExceededException>().Which;
        thrown.Kind.Should().Be(EvaluationLimitKind.Depth);
        thrown.Message.Should().Be("The JSONPath query has more than 64 path segments, brackets and parentheses.");
    }

    [Theory]
    [InlineData("$..a..a", "A JSONPath query can use at most one recursive descent ('..').")]
    [InlineData("$[?($[?($.zz)])]", "A JSONPath query can use '$' at most once after its start (a root reference inside a filter, or a regular expression's end).")]
    public void Resolve_QueryWhoseCostIsExponential_IsRefused(string query, string message)
    {
        var act = () => JsonQuery.Resolve(Items, query);

        act.Should().Throw<BindingEvaluationException>().WithMessage(message);
    }

    [Theory]
    [InlineData("$..name")]
    [InlineData("$.items[?(@.name == $.items[0].name)]")]
    [InlineData("$.items[?(@.name =~ /^a$/)]")]
    public void Resolve_OneDescentOrOneRootReference_IsRead(string query)
    {
        JsonQuery.Resolve(Items, query).Should().NotBeNull();
    }

    [Theory]
    [InlineData("$.items[?(@.since > 5)]")]
    [InlineData("$.items[?(@.since > true)]")]
    [InlineData("$.items[?(@.big > true)]")]
    public void Resolve_FilterComparingIncompatibleTypes_IsNotValid(string query)
    {
        // JObject.Parse reads an ISO date as a date and a long integer as a BigInteger, as the reads of the API do.
        var document = JObject.Parse("""{ "items": [ { "since": "2026-01-01T00:00:00Z", "big": 123456789012345678901234567890 } ] }""");

        var act = () => JsonQuery.Resolve(document, query);

        act.Should().Throw<BindingEvaluationException>().WithMessage("The JSONPath query is not valid.");
    }

    [Fact]
    public async Task Config_LiteralChainOfSegmentsAtTheCap_ThrowsDepthWithoutCrashing()
    {
        var raw = "@config(\"$" + string.Concat(Enumerable.Repeat(".a", 4090)) + "\")";
        var (executor, property) = Config(new JObject { ["settings"] = new JObject { ["value"] = raw } });

        raw.Length.Should().Be(BindingExpressionLimits.MaxLength);
        var act = () => executor.TryBinding(property, includeSecrets: true, Scope(property)).AsTask();

        (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which.Kind.Should().Be(EvaluationLimitKind.Depth);
    }

    [Fact]
    public async Task Config_LiteralDeepFilterWithinTheCap_ThrowsDepthWithoutCrashing()
    {
        var raw = "@config(\"$[?(@" + string.Concat(Enumerable.Repeat("[?(@", 2040)) + "\")";
        var (executor, property) = Config(new JObject { ["settings"] = new JObject { ["value"] = raw } });

        raw.Length.Should().BeLessThanOrEqualTo(BindingExpressionLimits.MaxLength);
        var act = () => executor.TryBinding(property, includeSecrets: true, Scope(property)).AsTask();

        var thrown = (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which;
        thrown.Kind.Should().Be(EvaluationLimitKind.Depth);
        thrown.Path.Should().Be("settings.value");
    }

    [Fact]
    public async Task Config_ComputedQueryLongerThanTheCap_ThrowsExpressionLengthWithoutCrashing()
    {
        // The binding is 22 characters; the query is a 30 KB plain string that the binding reads first.
        var query = "$" + string.Concat(Enumerable.Repeat("[?(@", 5000)) + ".a" + string.Concat(Enumerable.Repeat(")]", 5000));
        var document = new JObject { ["q"] = query, ["settings"] = new JObject { ["value"] = "@config(@config(\"/q\"))" } };
        var (executor, property) = Config(document);

        var act = () => executor.TryBinding(property, includeSecrets: true, Scope(property)).AsTask();

        var thrown = (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which;
        thrown.Kind.Should().Be(EvaluationLimitKind.ExpressionLength);
        thrown.Path.Should().Be("settings.value");
    }

    [Fact]
    public async Task Config_ComputedDeepFilterWithinTheCap_ThrowsDepthWithoutCrashing()
    {
        var query = "$" + string.Concat(Enumerable.Repeat("[?(@", 1000)) + ".a" + string.Concat(Enumerable.Repeat(")]", 1000));
        var document = new JObject { ["q"] = query, ["settings"] = new JObject { ["value"] = "@config(@config(\"/q\"))" } };
        var (executor, property) = Config(document);

        query.Length.Should().BeLessThanOrEqualTo(BindingExpressionLimits.MaxLength);
        var act = () => executor.TryBinding(property, includeSecrets: true, Scope(property)).AsTask();

        (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which.Kind.Should().Be(EvaluationLimitKind.Depth);
    }

    [Theory]
    [InlineData("$[ ")]
    [InlineData("$.a[ ")]
    [InlineData("$..[ ")]
    [InlineData("$?( ")]
    [InlineData("$[?(-")]
    [InlineData("$[?(@.[ ")]
    public void Resolve_QueryEndingInsideAnIndexerOrFilter_IsNotValid(string query)
    {
        var act = () => JsonQuery.Resolve(Items, query);

        act.Should().Throw<BindingEvaluationException>().WithMessage("The JSONPath query is not valid*");
    }

    [Fact]
    public void Resolve_CatastrophicRegexFilter_TimesOut()
    {
        var document = JObject.Parse("""{ "items": [ { "name": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!" } ] }""");
        var watch = Stopwatch.StartNew();

        var act = () => JsonQuery.Resolve(document, "$.items[?(@.name =~ /^(a+)+$/)]");

        act.Should().Throw<BindingEvaluationException>().WithMessage("The JSONPath query took too long to evaluate.");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Math_OperandPastLong_IsConverted()
    {
        var result = await EvaluateWithValue(new JValue(new BigInteger(ulong.MaxValue)));

        result!.Value<decimal>().Should().Be(18446744073709551616m);
    }

    [Fact]
    public async Task Math_OperandPastDecimal_IsAnOverflow()
    {
        var act = () => EvaluateWithValue(new JValue(BigInteger.Pow(10, 30)));

        await act.Should().ThrowAsync<BindingEvaluationException>().WithMessage("Math expression resulted in a numeric overflow.");
    }

    private static (BindingExecutor Executor, JProperty Property) Config(JObject document)
    {
        var executor = new BindingExecutor();
        executor.RegisterFunction("config", new ConfigBindingFunction());
        return (executor, ((JObject)document["settings"]!).Property("value")!);
    }

    private static BindingScope Scope(JProperty property)
    {
        return new BindingScope { Document = property.Root.DeepClone() };
    }

    private static Task<JToken?> EvaluateWithValue(JToken value)
    {
        var ast = new Parser(new Tokenizer("@{@value + 1}").Tokenize()).Parse();
        var evaluator = new BindingEvaluator(
            new Dictionary<string, IBindingSource> { [BindingEvaluator.DefaultSourceKey] = new ValueSource(value) },
            new Dictionary<string, IBindingFunction>(),
            includeSecrets: true);
        return evaluator.EvaluateAsync(ast).AsTask();
    }

    private sealed class ValueSource(JToken value) : IBindingSource
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
        {
            return new ValueTask<BindingValue?>(new BindingValue(value));
        }
    }
}
