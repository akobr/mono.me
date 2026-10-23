using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

// How the evaluator and the executor pass BindingScope.Dependencies on, and when they make them volatile.
public class BindingRecordingTests
{
    [Fact]
    public async Task Evaluate_RecordingSource_GetsTheCollectorAndStaysNonVolatile()
    {
        var scope = NewScope();
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new RecordingSource(request =>
            {
                request.Dependencies!.Record(new BindingDependency("kv", "default", request.PathString, "\"1\""));
                return BindingValue.FromString("db-1");
            }),
        };

        var result = await Evaluate("@db.host", sources: sources, scope: scope);

        result!.Value<string>().Should().Be("db-1");
        scope.Dependencies!.IsVolatile.Should().BeFalse();
        scope.Dependencies.ToList().Should().ContainSingle().Which.Key.Should().Be("db.host");
    }

    [Fact]
    public async Task Evaluate_RecordingFunction_GetsTheCollectorAndItsSourceArgumentsRecordToo()
    {
        var scope = NewScope();
        BindingFunctionRequest? captured = null;
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new RecordingSource(request =>
            {
                request.Dependencies!.Record(new BindingDependency("kv", "default", request.PathString, "\"1\""));
                return BindingValue.FromString("/a");
            }),
        };
        var functions = new Dictionary<string, IBindingFunction>
        {
            ["lookup"] = new RecordingFunction(request =>
            {
                captured = request;
                return BindingValue.FromString("found");
            }),
        };

        await Evaluate("@lookup(path.to.expression)", sources: sources, functions: functions, scope: scope);

        captured!.Dependencies.Should().BeSameAs(scope.Dependencies);
        scope.Dependencies!.IsVolatile.Should().BeFalse();
        scope.Dependencies.ToList().Should().ContainSingle().Which.Key.Should().Be("path.to.expression");
    }

    [Fact]
    public async Task Evaluate_SourceThatDoesNotRecord_MakesItVolatile()
    {
        var scope = NewScope();
        var sources = new Dictionary<string, IBindingSource>
        {
            ["vault"] = new PlainSource(_ => BindingValue.FromString("s3cret")),
        };

        await Evaluate("@(db.password, vault)", sources: sources, scope: scope);

        scope.Dependencies!.IsVolatile.Should().BeTrue();
        scope.Dependencies.VolatileReason.Should().Contain("'vault'");
    }

    [Fact]
    public async Task Evaluate_SourceCallOfASourceThatDoesNotRecord_MakesItVolatile()
    {
        var scope = NewScope();
        var sources = new Dictionary<string, IBindingSource>
        {
            ["vault"] = new PlainSource(_ => BindingValue.FromString("s3cret")),
        };

        await Evaluate("@vault(db.password)", sources: sources, scope: scope);

        scope.Dependencies!.IsVolatile.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluate_FunctionThatDoesNotRecord_MakesItVolatile()
    {
        var scope = NewScope();
        var functions = new Dictionary<string, IBindingFunction>
        {
            ["random"] = new PlainFunction(_ => BindingValue.FromString("4")),
        };

        await Evaluate("@random(\"dice\")", functions: functions, scope: scope);

        scope.Dependencies!.IsVolatile.Should().BeTrue();
        scope.Dependencies.VolatileReason.Should().Contain("'random'");
    }

    [Theory]
    [InlineData("@(db.host, unknown)")]
    [InlineData("@unknown(\"x\")")]
    [InlineData("@unknown(db.host)")]
    public async Task Evaluate_UnregisteredName_ReadsAndRecordsNothing(string source)
    {
        var scope = NewScope();

        var result = await Evaluate(source, scope: scope);

        result.Should().BeNull();
        scope.Dependencies!.IsVolatile.Should().BeFalse();
        scope.Dependencies.ToList().Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_WithoutACollector_CallsANonRecordingSourceAsBefore()
    {
        BindingRequest? captured = null;
        var sources = new Dictionary<string, IBindingSource>
        {
            [BindingEvaluator.DefaultSourceKey] = new PlainSource(request =>
            {
                captured = request;
                return BindingValue.FromString("db-1");
            }),
        };

        var result = await Evaluate("@db.host", sources: sources, scope: new BindingScope());

        result!.Value<string>().Should().Be("db-1");
        captured!.Dependencies.Should().BeNull();
    }

    [Fact]
    public async Task Evaluate_ConfigFunction_RecordsNothingAndStaysNonVolatile()
    {
        var scope = new BindingScope { Document = new JObject { ["a"] = 1 }, Dependencies = new BindingDependencies() };
        var functions = new Dictionary<string, IBindingFunction> { ["config"] = new ConfigBindingFunction() };

        var result = await Evaluate("@config(\"/a\")", functions: functions, scope: scope);

        result!.Value<int>().Should().Be(1);
        scope.Dependencies!.IsVolatile.Should().BeFalse();
        scope.Dependencies.ToList().Should().BeEmpty();
    }

    [Fact]
    public async Task TryBinding_WithAScope_PassesTheCollectorToTheSource()
    {
        var executor = new BindingExecutor();
        BindingRequest? captured = null;
        executor.RegisterSource(BindingExecutor.DefaultSourceKey, new RecordingSource(request =>
        {
            captured = request;
            return BindingValue.FromString("db-1");
        }));
        var scope = NewScope();
        var content = new JObject { ["host"] = "@db.host" };

        await executor.TryBinding((JValue)content["host"]!, includeSecrets: true, scope);

        executor.Should().BeAssignableTo<IRecordingBinding>();
        captured!.Dependencies.Should().BeSameAs(scope.Dependencies);
        content["host"]!.Value<string>().Should().Be("db-1");
    }

    private static BindingScope NewScope()
    {
        return new BindingScope { Dependencies = new BindingDependencies() };
    }

    private static Task<JToken?> Evaluate(
        string source,
        IReadOnlyDictionary<string, IBindingSource>? sources = null,
        IReadOnlyDictionary<string, IBindingFunction>? functions = null,
        BindingScope? scope = null)
    {
        var ast = new Parser(new Tokenizer(source).Tokenize()).Parse();
        var evaluator = new BindingEvaluator(
            sources ?? new Dictionary<string, IBindingSource>(),
            functions ?? new Dictionary<string, IBindingFunction>(),
            includeSecrets: true,
            scope);
        return evaluator.EvaluateAsync(ast).AsTask();
    }

    private sealed class RecordingSource(Func<BindingRequest, BindingValue?> resolve) : IBindingSource, IRecordingBinding
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request) => new(resolve(request));
    }

    private sealed class PlainSource(Func<BindingRequest, BindingValue?> resolve) : IBindingSource
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request) => new(resolve(request));
    }

    private sealed class RecordingFunction(Func<BindingFunctionRequest, BindingValue?> invoke) : IBindingFunction, IRecordingBinding
    {
        public ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request) => new(invoke(request));
    }

    private sealed class PlainFunction(Func<BindingFunctionRequest, BindingValue?> invoke) : IBindingFunction
    {
        public ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request) => new(invoke(request));
    }
}
