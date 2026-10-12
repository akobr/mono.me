using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

// @name(path) is @(path, name) when name is a registered source and not a function.
public class SourceCallFormTests
{
    [Fact]
    public async Task SourceCall_ResolvesThePathAgainstTheNamedSource()
    {
        BindingRequest? seen = null;
        var sources = new Dictionary<string, IBindingSource>
        {
            ["public"] = new DelegateSource(request =>
            {
                seen = request;
                return BindingValue.FromString("sql.internal");
            }),
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(_ => throw new InvalidOperationException("the argument must not be pre-resolved")),
        };

        var result = await Evaluate("@public(db.host)", sources: sources);

        result.Should().BeOfType<JValue>().Which.Value<string>().Should().Be("sql.internal");
        seen!.PathString.Should().Be("db.host");
    }

    [Fact]
    public async Task SourceCall_EqualsTheSourcedStatement()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            ["vault"] = new DelegateSource(request => BindingValue.FromString($"from:{request.PathString}")),
        };

        var call = await Evaluate("@vault(api.key)", sources: sources);
        var sourced = await Evaluate("@(api.key, vault)", sources: sources);

        call!.Value<string>().Should().Be(sourced!.Value<string>());
    }

    [Fact]
    public async Task SourceCall_AFunctionWithTheSameNameWins()
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            ["both"] = new DelegateSource(_ => BindingValue.FromString("source")),
        };
        var functions = new Dictionary<string, IBindingFunction>
        {
            ["both"] = new DelegateFunction(_ => BindingValue.FromString("function")),
        };

        var result = await Evaluate("@both(\"x\")", sources: sources, functions: functions);

        result!.Value<string>().Should().Be("function");
    }

    [Theory]
    [InlineData("@public()")]
    [InlineData("@public(a.b, c.d)")]
    [InlineData("@public(\"literal\")")]
    public async Task SourceCall_OtherShapes_AreNotSourceCalls(string expression)
    {
        var sources = new Dictionary<string, IBindingSource>
        {
            ["public"] = new DelegateSource(_ => BindingValue.FromString("never")),
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(_ => null),
        };

        var result = await Evaluate(expression, sources: sources);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SourceCall_UnknownName_IsNull()
    {
        var result = await Evaluate("@nothing(a.b)", sources: new Dictionary<string, IBindingSource>());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Sources_ReceiveTheScopeContext()
    {
        var context = new object();
        object? seen = null;
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new DelegateSource(request =>
            {
                seen = request.Context;
                return BindingValue.FromString("x");
            }),
        };

        await Evaluate("@a.b", sources: sources, scope: new BindingScope { Context = context });

        seen.Should().BeSameAs(context);
    }

    private static Task<JToken?> Evaluate(
        string source,
        bool includeSecrets = true,
        IReadOnlyDictionary<string, IBindingSource>? sources = null,
        IReadOnlyDictionary<string, IBindingFunction>? functions = null,
        BindingScope? scope = null)
    {
        var ast = new Parser(new Tokenizer(source).Tokenize()).Parse();
        var evaluator = new BindingEvaluator(
            sources ?? new Dictionary<string, IBindingSource>(),
            functions ?? new Dictionary<string, IBindingFunction>(),
            includeSecrets,
            scope);
        return evaluator.EvaluateAsync(ast).AsTask();
    }

    private sealed class DelegateSource(Func<BindingRequest, BindingValue?> resolve) : IBindingSource
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request) => new(resolve(request));
    }

    private sealed class DelegateFunction(Func<BindingFunctionRequest, BindingValue?> invoke) : IBindingFunction
    {
        public ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request) => new(invoke(request));
    }
}
