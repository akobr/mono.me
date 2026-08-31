using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class ConfigBindingFunctionTests
{
    private static readonly JObject Document = JObject.Parse(
        """
        {
            "maxPrice": 10,
            "nested": { "value": "hello" }
        }
        """);

    [Fact]
    public async Task InvokeAsync_JsonPointerExpression_ReturnsResolvedValue()
    {
        var function = new ConfigBindingFunction();

        var result = await function.InvokeAsync(CreateRequest(Document, "/nested/value"));

        result!.Token.Value<string>().Should().Be("hello");
    }

    [Fact]
    public async Task InvokeAsync_JsonPathExpression_ReturnsResolvedValue()
    {
        var function = new ConfigBindingFunction();

        var result = await function.InvokeAsync(CreateRequest(Document, "$.maxPrice"));

        result!.Token.Value<int>().Should().Be(10);
    }

    [Fact]
    public async Task InvokeAsync_UnresolvedExpression_ReturnsNull()
    {
        var function = new ConfigBindingFunction();

        var result = await function.InvokeAsync(CreateRequest(Document, "/missing"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_MissingDocument_Throws()
    {
        var function = new ConfigBindingFunction();
        var request = new BindingFunctionRequest
        {
            Name = "config",
            Arguments = new List<BindingValue> { BindingValue.FromString("/a") },
            IncludeSecrets = true,
            Document = null,
        };

        var act = () => function.InvokeAsync(request).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task InvokeAsync_WrongArgumentCount_Throws()
    {
        var function = new ConfigBindingFunction();
        var request = new BindingFunctionRequest
        {
            Name = "config",
            Arguments = new List<BindingValue>(),
            IncludeSecrets = true,
            Document = Document,
        };

        var act = () => function.InvokeAsync(request).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    [Fact]
    public async Task InvokeAsync_NonStringArgument_Throws()
    {
        var function = new ConfigBindingFunction();
        var request = new BindingFunctionRequest
        {
            Name = "config",
            Arguments = new List<BindingValue> { new(new JValue(42)) },
            IncludeSecrets = true,
            Document = Document,
        };

        var act = () => function.InvokeAsync(request).AsTask();

        await act.Should().ThrowAsync<BindingEvaluationException>();
    }

    private static BindingFunctionRequest CreateRequest(JObject document, string expression)
    {
        return new BindingFunctionRequest
        {
            Name = "config",
            Arguments = new List<BindingValue> { BindingValue.FromString(expression) },
            IncludeSecrets = true,
            Document = document,
        };
    }
}
