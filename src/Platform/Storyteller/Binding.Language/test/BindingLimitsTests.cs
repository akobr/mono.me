using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class BindingLimitsTests
{
    private const string Secret = "SECRET_SENTINEL";

    [Fact]
    public void Limits_MatchTheMcpReader()
    {
        BindingExpressionLimits.MaxLength.Should().Be(8192);
        BindingExpressionLimits.MaxDepth.Should().Be(64);
    }

    [Fact]
    public void Parse_SixtyFourNestedStatements_Parses()
    {
        var node = Parse(NestedCalls(64));

        node.Should().BeOfType<FunctionStatement>();
    }

    [Fact]
    public void Parse_SixtyFiveNestedStatements_ThrowsDepth()
    {
        var act = () => Parse(NestedCalls(65));

        var thrown = act.Should().Throw<EvaluationLimitExceededException>().Which;
        thrown.Kind.Should().Be(EvaluationLimitKind.Depth);
        thrown.Limit.Should().Be(64);
        thrown.Message.Should().Be("The expression nests deeper than 64 levels or than the stack allows.");
    }

    [Fact]
    public void Parse_SixtyFourParentheses_Parses()
    {
        var node = Parse(NestedParentheses(64));

        node.Should().BeOfType<MathExpression>().Which.Expression.Should().BeOfType<NumberNode>();
    }

    [Fact]
    public void Parse_SixtyFiveParentheses_ThrowsDepth()
    {
        var act = () => Parse(NestedParentheses(65));

        act.Should().Throw<EvaluationLimitExceededException>().Which.Kind.Should().Be(EvaluationLimitKind.Depth);
    }

    [Theory]
    [InlineData(32, 32, false)]
    [InlineData(33, 32, true)]
    [InlineData(32, 33, true)]
    public void Parse_ParenthesesAndStatements_ShareOneCounter(int parentheses, int statements, bool refused)
    {
        var source = "@{" + new string('(', parentheses) + NestedCalls(statements) + new string(')', parentheses) + "}";

        var act = () => Parse(source);

        if (refused)
        {
            act.Should().Throw<EvaluationLimitExceededException>().Which.Kind.Should().Be(EvaluationLimitKind.Depth);
        }
        else
        {
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void Parse_Wrappers_AreNotLevels()
    {
        Parse("@[" + NestedCalls(64) + "]").Should().BeOfType<InterpolationExpression>();
        Parse("@{" + NestedCalls(64) + "}").Should().BeOfType<MathExpression>();
    }

    [Fact]
    public void Parse_SiblingStatements_DoNotAddUp()
    {
        var interpolation = "@[" + string.Concat(Enumerable.Repeat("@a.b ", 200)) + "]";
        var call = "@f(" + string.Join(", ", Enumerable.Repeat("@a.b", 200)) + ")";
        var math = "@{" + string.Join(" + ", Enumerable.Repeat("(@a.b)", 200)) + "}";

        Parse(interpolation).Should().BeOfType<InterpolationExpression>();
        Parse(call).Should().BeOfType<FunctionStatement>().Which.Arguments.Should().HaveCount(200);
        Parse(math).Should().BeOfType<MathExpression>();
    }

    [Fact]
    public void Parse_TheIssuesTenThousandNestedCalls_ThrowsDepthWithoutCrashing()
    {
        // About 40 KB: the executor refuses it for its length first; the parser alone must still stop.
        var act = () => Parse(NestedCalls(10_000));

        act.Should().Throw<EvaluationLimitExceededException>().Which.Kind.Should().Be(EvaluationLimitKind.Depth);
    }

    [Fact]
    public void Parse_TenThousandParentheses_ThrowsDepthWithoutCrashing()
    {
        var act = () => Parse(NestedParentheses(10_000));

        act.Should().Throw<EvaluationLimitExceededException>().Which.Kind.Should().Be(EvaluationLimitKind.Depth);
    }

    [Fact]
    public void Parse_NumberLargerThanDecimal_ThrowsSyntaxError()
    {
        var act = () => Parse("@{1 + 79228162514264337593543950336}");

        act.Should().Throw<BindingSyntaxException>().Which.Position.Should().Be(6);
    }

    [Fact]
    public void Parse_LargestDecimal_Parses()
    {
        var node = Parse("@{79228162514264337593543950335}");

        node.Should().BeOfType<MathExpression>()
            .Which.Expression.Should().BeOfType<NumberNode>()
            .Which.Value.Should().Be(decimal.MaxValue);
    }

    [Fact]
    public async Task TryBinding_ExactlyTheMaximumLength_IsBound()
    {
        var raw = "@" + new string('a', BindingExpressionLimits.MaxLength - 1);
        var executor = Executor(_ => BindingValue.FromString("resolved"));
        var (_, property) = Settings(raw);

        raw.Length.Should().Be(8192);
        (await executor.TryBinding(property, includeSecrets: true)).Should().BeTrue();
        property.Value.Value<string>().Should().Be("resolved");
    }

    [Fact]
    public async Task TryBinding_OneCharacterOverTheMaximumLength_ThrowsExpressionLength()
    {
        var raw = "@" + new string('a', BindingExpressionLimits.MaxLength);
        var executor = Executor(_ => BindingValue.FromString("resolved"));
        var (_, property) = Settings(raw);

        var act = () => executor.TryBinding(property, includeSecrets: true).AsTask();

        var thrown = (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which;
        thrown.Kind.Should().Be(EvaluationLimitKind.ExpressionLength);
        thrown.Limit.Should().Be(8192);
        thrown.Path.Should().Be("settings.value");
        thrown.Message.Should().Be("Failed to process the data binding for 'settings.value': the expression is longer than 8192 characters.");
        property.Value.Value<string>().Should().Be(raw);
    }

    [Fact]
    public async Task TryBinding_TheIssuesFortyKilobyteValue_ThrowsExpressionLength()
    {
        var raw = NestedCalls(10_000);
        var executor = Executor(_ => BindingValue.FromString("resolved"));
        var (_, property) = Settings(raw);

        var act = () => executor.TryBinding(property, includeSecrets: true).AsTask();

        var thrown = (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which;
        thrown.Kind.Should().Be(EvaluationLimitKind.ExpressionLength);
        thrown.Message.Should().NotContain("f(@");
    }

    [Fact]
    public async Task TryBinding_SixtyFiveLevelsWithinTheLength_ThrowsDepthWithPath()
    {
        var raw = NestedCalls(65);
        var executor = Executor(_ => BindingValue.FromString("resolved"));
        var (_, property) = Settings(raw);

        var act = () => executor.TryBinding(property, includeSecrets: true).AsTask();

        var thrown = (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which;
        thrown.Kind.Should().Be(EvaluationLimitKind.Depth);
        thrown.Limit.Should().Be(64);
        thrown.Path.Should().Be("settings.value");
        thrown.Message.Should().StartWith("Failed to process the data binding for 'settings.value': ").And.NotContain("f(@");
        thrown.InnerException.Should().BeOfType<EvaluationLimitExceededException>();
    }

    [Theory]
    [InlineData("@a.")]
    [InlineData("@{1 + }")]
    [InlineData("@{1 + 79228162514264337593543950336}")]
    public async Task TryBinding_SyntaxError_IsAnEvaluationErrorWithoutTheExpression(string raw)
    {
        var executor = Executor(_ => BindingValue.FromString("resolved"));
        var (_, property) = Settings(raw);

        var act = () => executor.TryBinding(property, includeSecrets: true).AsTask();

        var thrown = (await act.Should().ThrowAsync<BindingEvaluationException>()).Which;
        thrown.Should().NotBeOfType<EvaluationLimitExceededException>();
        thrown.Path.Should().Be("settings.value");
        thrown.Message.Should().StartWith("Failed to process the data binding for 'settings.value': ").And.NotContain(raw);
        thrown.InnerException.Should().BeOfType<BindingSyntaxException>();
    }

    [Fact]
    public async Task TryBinding_SecretOperandInMath_NamesTheKindOnly()
    {
        var includedSecrets = false;
        var executor = Executor(request =>
        {
            includedSecrets |= request.IncludeSecrets;
            return request.IncludeSecrets ? BindingValue.FromString(Secret) : null;
        });
        var (_, property) = Settings("@{@value + 1}");

        var act = () => executor.TryBinding(property, includeSecrets: true).AsTask();

        var thrown = (await act.Should().ThrowAsync<BindingEvaluationException>()).Which;
        includedSecrets.Should().BeTrue();
        thrown.Path.Should().Be("settings.value");
        thrown.Message.Should().Be("Failed to process the data binding for 'settings.value': A math operand of type 'string' is not a number.");
        Chain(thrown).Should().NotContain(exception => exception.Message.Contains(Secret));
    }

    [Fact]
    public async Task TryBinding_FlatChainNearTheMaximumLength_SumsWithoutRecursion()
    {
        var raw = "@{1" + string.Concat(Enumerable.Repeat("+1", 4094)) + "}";
        var executor = new BindingExecutor();
        var (_, property) = Settings(raw);

        raw.Length.Should().Be(8192);
        (await executor.TryBinding(property, includeSecrets: true)).Should().BeTrue();
        property.Value.Value<decimal>().Should().Be(4095m);
    }

    [Fact]
    public async Task Evaluate_ParenthesesWithinTheLimit_AreNotCountedTwice()
    {
        // 63 parentheses, each with an operator chain inside: the numeric recursion is deeper than the parser's count.
        var source = "@{" + string.Concat(Enumerable.Repeat("1 + 2 * (", 63)) + "1" + new string(')', 63) + "}";

        var result = await Evaluate(Parse(source));

        result!.Value<decimal>().Should().Be(18446744073709551615m);
    }

    [Fact]
    public async Task Evaluate_SixtyFourParsedStatements_AreEvaluated()
    {
        var functions = new Dictionary<string, IBindingFunction> { ["f"] = new FirstArgument() };
        var sources = Sources(_ => BindingValue.FromString("x"));

        var result = await Evaluate(Parse(NestedCalls(64)), sources, functions);

        result!.Value<string>().Should().Be("x");
    }

    [Fact]
    public async Task Evaluate_ChainOrder_IsUnchanged()
    {
        var reads = new List<string>();
        var sources = Sources(request =>
        {
            reads.Add(request.Path[0]);
            return new BindingValue(new JValue(int.Parse(request.Path[0][1..])));
        });

        var result = await Evaluate(Parse("@{@v10 - @v3 * @v2 - @v1 + (@v6 - @v4) % @v5}"), sources);

        result!.Value<decimal>().Should().Be(10m - (3m * 2m) - 1m + ((6m - 4m) % 5m));
        reads.Should().Equal("v10", "v3", "v2", "v1", "v6", "v4", "v5");
    }

    [Fact]
    public async Task Evaluate_HandBuiltLeftDeepChain_SumsWithoutRecursion()
    {
        BindingNode chain = new NumberNode(0m);
        for (var i = 0; i < 100_000; i++)
        {
            chain = new BinaryNode('+', chain, new NumberNode(1m));
        }

        var result = await Evaluate(new MathExpression(chain));

        result!.Value<decimal>().Should().Be(100_000m);
    }

    [Fact]
    public async Task Evaluate_HandBuiltRightDeepChain_ThrowsDepthWithoutCrashing()
    {
        BindingNode chain = new NumberNode(0m);
        for (var i = 0; i < 100_000; i++)
        {
            chain = new BinaryNode('+', new NumberNode(1m), chain);
        }

        var act = () => Evaluate(new MathExpression(chain));

        (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which.Kind.Should().Be(EvaluationLimitKind.Depth);
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public async Task Evaluate_HandBuiltNestedStatements_AreBounded(int statements, bool refused)
    {
        Statement statement = new PathStatement(new PathNode(["x"]));
        for (var i = 1; i < statements; i++)
        {
            statement = new FunctionStatement("f", [statement]);
        }

        var functions = new Dictionary<string, IBindingFunction> { ["f"] = new FirstArgument() };
        var sources = Sources(_ => BindingValue.FromString("x"));
        var act = () => Evaluate(statement, sources, functions);

        if (refused)
        {
            var thrown = (await act.Should().ThrowAsync<EvaluationLimitExceededException>()).Which;
            thrown.Kind.Should().Be(EvaluationLimitKind.Depth);
            thrown.Limit.Should().Be(64);
        }
        else
        {
            (await act()).Should().NotBeNull();
        }
    }

    private static string NestedCalls(int statements)
    {
        return "@" + string.Concat(Enumerable.Repeat("f(@", statements - 1)) + "x" + new string(')', statements - 1);
    }

    private static string NestedParentheses(int parentheses)
    {
        return "@{" + new string('(', parentheses) + "1" + new string(')', parentheses) + "}";
    }

    private static BindingNode Parse(string source)
    {
        return new Parser(new Tokenizer(source).Tokenize()).Parse();
    }

    private static Task<JToken?> Evaluate(
        BindingNode ast,
        IReadOnlyDictionary<string, IBindingSource>? sources = null,
        IReadOnlyDictionary<string, IBindingFunction>? functions = null)
    {
        var evaluator = new BindingEvaluator(
            sources ?? new Dictionary<string, IBindingSource>(),
            functions ?? new Dictionary<string, IBindingFunction>(),
            includeSecrets: true);
        return evaluator.EvaluateAsync(ast).AsTask();
    }

    private static Dictionary<string, IBindingSource> Sources(Func<BindingRequest, BindingValue?> resolve)
    {
        return new Dictionary<string, IBindingSource> { [BindingEvaluator.DefaultSourceKey] = new DelegateSource(resolve) };
    }

    private static BindingExecutor Executor(Func<BindingRequest, BindingValue?> resolve)
    {
        var executor = new BindingExecutor();
        executor.RegisterSource(BindingExecutor.DefaultSourceKey, new DelegateSource(resolve));
        return executor;
    }

    private static (JObject Root, JProperty Property) Settings(string raw)
    {
        var root = new JObject { ["settings"] = new JObject { ["value"] = raw } };
        return (root, ((JObject)root["settings"]!).Property("value")!);
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private sealed class DelegateSource(Func<BindingRequest, BindingValue?> resolve) : IBindingSource
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
        {
            return new ValueTask<BindingValue?>(resolve(request));
        }
    }

    private sealed class FirstArgument : IBindingFunction
    {
        public ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request)
        {
            return new ValueTask<BindingValue?>(request.Arguments[0]);
        }
    }
}
