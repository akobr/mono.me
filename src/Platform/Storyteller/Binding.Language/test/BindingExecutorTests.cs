using System;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class BindingExecutorTests
{
    [Fact]
    public async Task TryBinding_NonBindingString_ReturnsFalseAndLeavesValue()
    {
        var executor = new BindingExecutor();
        var property = new JProperty("name", "plain value");

        var result = await executor.TryBinding(property, includeSecrets: true);

        result.Should().BeFalse();
        property.Value.Value<string>().Should().Be("plain value");
    }

    [Fact]
    public async Task TryBinding_NonStringProperty_ReturnsFalse()
    {
        var executor = new BindingExecutor();
        var property = new JProperty("count", 42);

        var result = await executor.TryBinding(property, includeSecrets: true);

        result.Should().BeFalse();
        property.Value.Value<int>().Should().Be(42);
    }

    [Fact]
    public async Task TryBinding_PathStatement_AssignsResolvedValue()
    {
        var executor = new BindingExecutor();
        executor.RegisterSource(
            BindingExecutor.DefaultSourceKey,
            new DelegateSource(_ => BindingValue.FromString("Acme")));
        var property = new JProperty("company", "@store.name");

        var result = await executor.TryBinding(property, includeSecrets: true);

        result.Should().BeTrue();
        property.Value.Value<string>().Should().Be("Acme");
    }

    [Fact]
    public async Task TryBinding_MathExpression_AssignsNumber()
    {
        var executor = new BindingExecutor();
        var property = new JProperty("total", "@{2 * 21}");

        var result = await executor.TryBinding(property, includeSecrets: true);

        result.Should().BeTrue();
        property.Value.Value<decimal>().Should().Be(42m);
    }

    [Fact]
    public async Task TryBinding_Interpolation_AssignsString()
    {
        var executor = new BindingExecutor();
        executor.RegisterSource(
            BindingExecutor.DefaultSourceKey,
            new DelegateSource(_ => BindingValue.FromString("Sam")));
        var property = new JProperty("greeting", "@[Hello @name!]");

        var result = await executor.TryBinding(property, includeSecrets: true);

        result.Should().BeTrue();
        property.Value.Value<string>().Should().Be("Hello Sam!");
    }

    [Fact]
    public async Task TryBinding_UnresolvedTopLevelStatement_ReturnsFalseAndLeavesValue()
    {
        var executor = new BindingExecutor();
        var property = new JProperty("secret", "@vault.key");

        var result = await executor.TryBinding(property, includeSecrets: true);

        result.Should().BeFalse();
        property.Value.Value<string>().Should().Be("@vault.key");
    }

    [Fact]
    public async Task TryBinding_IncludeSecretsFalse_PropagatesToSource()
    {
        var executor = new BindingExecutor();
        executor.RegisterSource(
            BindingExecutor.DefaultSourceKey,
            new DelegateSource(request => request.IncludeSecrets ? BindingValue.FromString("x") : null));
        var property = new JProperty("secret", "@vault.key");

        var result = await executor.TryBinding(property, includeSecrets: false);

        result.Should().BeFalse();
        property.Value.Value<string>().Should().Be("@vault.key");
    }

    [Fact]
    public async Task TryBinding_MalformedBinding_ThrowsWithPropertyPath()
    {
        var executor = new BindingExecutor();
        var root = new JObject { ["settings"] = new JObject { ["value"] = "@a." } };
        var property = ((JObject)root["settings"]!).Property("value")!;

        var act = () => executor.TryBinding(property, includeSecrets: true).AsTask();

        var assertion = await act.Should().ThrowAsync<BindingException>();
        assertion.Which.Message.Should().Contain("settings.value");
    }

    [Fact]
    public async Task TryBinding_JValueArrayItem_ReplacesItemInPlace()
    {
        var executor = new BindingExecutor();
        executor.RegisterSource(
            BindingExecutor.DefaultSourceKey,
            new DelegateSource(_ => BindingValue.FromString("resolved")));
        var array = new JArray { "@item.value", "literal" };
        var item = (JValue)array[0];

        var result = await executor.TryBinding(item, includeSecrets: true);

        result.Should().BeTrue();
        array[0].Value<string>().Should().Be("resolved");
        array[1].Value<string>().Should().Be("literal");
    }

    [Fact]
    public async Task TryBinding_WithScope_PropagatesDocumentAndContextToFunction()
    {
        var executor = new BindingExecutor();
        BindingFunctionRequest? captured = null;
        executor.RegisterFunction("config", new DelegateFunction(request =>
        {
            captured = request;
            return BindingValue.FromString("resolved");
        }));
        var document = new JObject { ["a"] = 1 };
        var context = new object();
        var scope = new BindingScope { Document = document, Context = context };
        var property = new JProperty("value", "@config(\"/a\")");

        var result = await executor.TryBinding(property, includeSecrets: true, scope);

        result.Should().BeTrue();
        captured!.Document.Should().BeSameAs(document);
        captured.Context.Should().BeSameAs(context);
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
