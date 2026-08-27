using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class BindingEvaluatorTests
{
    [Fact]
    public async Task Evaluate_Path_ResolvesViaDefaultSource()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(request =>
            {
                request.PathString.Should().Be("store.name");
                return BindingValue.FromString("Acme");
            }),
        };

        var result = await Evaluate("@store.name", sources: sources);

        result.Should().BeOfType<JValue>().Which.Value<string>().Should().Be("Acme");
    }

    [Fact]
    public async Task Evaluate_Sourced_ResolvesViaNamedSource()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            ["vault"] = new DelegateSource(request =>
            {
                request.PathString.Should().Be("api.key");
                return BindingValue.FromString("secret-value");
            }),
        };

        var result = await Evaluate("@(api.key, vault)", sources: sources);

        result.Should().BeOfType<JValue>().Which.Value<string>().Should().Be("secret-value");
    }

    [Fact]
    public async Task Evaluate_Path_ResolvingToObject_ReturnsObjectToken()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(_ =>
                new BindingValue(new JObject { ["a"] = 1 })),
        };

        var result = await Evaluate("@settings", sources: sources);

        result.Should().BeOfType<JObject>();
        result!.Value<int>("a").Should().Be(1);
    }

    [Fact]
    public async Task Evaluate_UnresolvedPath_ReturnsNull()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(_ => null),
        };

        var result = await Evaluate("@missing", sources: sources);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Evaluate_UnknownSource_ReturnsNull()
    {
        var result = await Evaluate("@anything");

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(true, "secret")]
    [InlineData(false, null)]
    public async Task Evaluate_RespectsIncludeSecrets(bool includeSecrets, string? expected)
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(request =>
                request.IncludeSecrets ? BindingValue.FromString("secret") : null),
        };

        var result = await Evaluate("@token", includeSecrets: includeSecrets, sources: sources);

        result?.Value<string>().Should().Be(expected);
    }

    [Fact]
    public async Task Evaluate_Function_InvokesRegisteredFunctionWithArguments()
    {
        BindingFunctionRequest? captured = null;
        var functions = new Dictionary<string, IBindingFunction>
        {
            ["upper"] = new DelegateFunction(request =>
            {
                captured = request;
                var input = request.Arguments[0].Token.Value<string>() ?? string.Empty;
                return BindingValue.FromString(input.ToUpperInvariant());
            }),
        };

        var result = await Evaluate("@upper(\"hello\")", functions: functions);

        result.Should().BeOfType<JValue>().Which.Value<string>().Should().Be("HELLO");
        captured!.Name.Should().Be("upper");
        captured.Arguments.Should().ContainSingle();
    }

    [Fact]
    public async Task Evaluate_UnknownFunction_ReturnsNull()
    {
        var result = await Evaluate("@unknown(\"x\")");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Evaluate_FunctionWithUnresolvedArgument_Throws()
    {
        var functions = new Dictionary<string, IBindingFunction>
        {
            ["echo"] = new DelegateFunction(request => request.Arguments[0]),
        };

        var act = () => Evaluate("@echo(missing.path)", functions: functions);

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task Evaluate_Interpolation_ConcatenatesTextAndValues()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(request => request.PathString switch
            {
                "name" => BindingValue.FromString("Sam"),
                "count" => new BindingValue(new JValue(3)),
                _ => null,
            }),
        };

        var result = await Evaluate("@[Hello @name, you have @count messages]", sources: sources);

        result.Should().BeOfType<JValue>().Which.Value<string>().Should().Be("Hello Sam, you have 3 messages");
    }

    [Fact]
    public async Task Evaluate_InterpolationWithObjectValue_UsesCompactJson()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(_ =>
                new BindingValue(new JObject { ["a"] = 1 })),
        };

        var result = await Evaluate("@[value=@obj]", sources: sources);

        result!.Value<string>().Should().Be("value={\"a\":1}");
    }

    [Fact]
    public async Task Evaluate_InterpolationWithUnresolvedStatement_Throws()
    {
        var act = () => Evaluate("@[Hello @name]");

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task Evaluate_Math_AppliesPrecedence()
    {
        var result = await Evaluate("@{1 + 2 * 3}");

        result.Should().BeOfType<JValue>().Which.Value<decimal>().Should().Be(7m);
    }

    [Fact]
    public async Task Evaluate_MathDecimals_UsesDecimalArithmetic()
    {
        var result = await Evaluate("@{0.1 + 0.2}");

        result!.Value<decimal>().Should().Be(0.3m);
    }

    [Fact]
    public async Task Evaluate_MathWithNumericStatement_ResolvesOperand()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(_ => new BindingValue(new JValue(10))),
        };

        var result = await Evaluate("@{@base + 5}", sources: sources);

        result!.Value<decimal>().Should().Be(15m);
    }

    [Fact]
    public async Task Evaluate_MathWithNonNumericOperand_Throws()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(_ => BindingValue.FromString("abc")),
        };

        var act = () => Evaluate("@{@value + 1}", sources: sources);

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Theory]
    [InlineData("@{1 / 0}")]
    [InlineData("@{1 % 0}")]
    public async Task Evaluate_MathDivideOrModuloByZero_Throws(string source)
    {
        var act = () => Evaluate(source);

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    private static Task<JToken?> Evaluate(
        string source,
        bool includeSecrets = true,
        IReadOnlyDictionary<string, IBindingSource>? sources = null,
        IReadOnlyDictionary<string, IBindingFunction>? functions = null)
    {
        var ast = new Parser(new Tokenizer(source).Tokenize()).Parse();
        var evaluator = new BindingEvaluator(
            sources ?? new Dictionary<string, IBindingSource>(),
            functions ?? new Dictionary<string, IBindingFunction>(),
            includeSecrets);
        return evaluator.EvaluateAsync(ast).AsTask();
    }

    private sealed class DelegateSource : IBindingSource
    {
        private readonly Func<BindingRequest, BindingValue?> _resolve;

        public DelegateSource(Func<BindingRequest, BindingValue?> resolve)
        {
            _resolve = resolve;
        }

        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
        {
            return new ValueTask<BindingValue?>(_resolve(request));
        }
    }

    private sealed class DelegateFunction : IBindingFunction
    {
        private readonly Func<BindingFunctionRequest, BindingValue?> _invoke;

        public DelegateFunction(Func<BindingFunctionRequest, BindingValue?> invoke)
        {
            _invoke = invoke;
        }

        public ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request)
        {
            return new ValueTask<BindingValue?>(_invoke(request));
        }
    }
}
