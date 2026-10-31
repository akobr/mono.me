using System;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class JsonQueryTests
{
    private static readonly JObject Document = JObject.Parse(
        """
        {
            "store": {
                "book": [
                    { "category": "reference", "price": 8.95 },
                    { "category": "fiction", "price": 22.99 }
                ]
            },
            "maxPrice": 10,
            "a/b": 1,
            "m~n": 2
        }
        """);

    [Fact]
    public void Resolve_EmptyPointer_ReturnsWholeDocument()
    {
        var result = JsonQuery.Resolve(Document, string.Empty);

        result.Should().BeSameAs(Document);
    }

    [Fact]
    public void Resolve_Pointer_NavigatesObjectsAndArrays()
    {
        var result = JsonQuery.Resolve(Document, "/store/book/1/category");

        result!.Value<string>().Should().Be("fiction");
    }

    [Theory]
    [InlineData("/a~1b", 1)]
    [InlineData("/m~0n", 2)]
    public void Resolve_Pointer_UnescapesTildeAndSlash(string pointer, int expected)
    {
        var result = JsonQuery.Resolve(Document, pointer);

        result!.Value<int>().Should().Be(expected);
    }

    [Fact]
    public void Resolve_Pointer_MissingProperty_ReturnsNull()
    {
        var result = JsonQuery.Resolve(Document, "/store/missing");

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_Pointer_OutOfRangeIndex_ReturnsNull()
    {
        var result = JsonQuery.Resolve(Document, "/store/book/5");

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_Pointer_DashSegment_ReturnsNull()
    {
        var result = JsonQuery.Resolve(Document, "/store/book/-");

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_Pointer_IndexingIntoScalar_ReturnsNull()
    {
        var result = JsonQuery.Resolve(Document, "/maxPrice/nested");

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_JsonPath_ReturnsMatchedToken()
    {
        var result = JsonQuery.Resolve(Document, "$.store.book[0].price");

        result!.Value<decimal>().Should().Be(8.95m);
    }

    [Fact]
    public void Resolve_JsonPath_FilterComparingCurrentAndRoot_ReturnsMatches()
    {
        var result = JsonQuery.Resolve(Document, "$.store.book[?(@.price < $.maxPrice)].category");

        result!.Value<string>().Should().Be("reference");
    }

    [Fact]
    public void Resolve_JsonPath_NoMatch_ReturnsNull()
    {
        var result = JsonQuery.Resolve(Document, "$.store.missing");

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_JsonPath_Malformed_ThrowsBindingEvaluationException()
    {
        var act = () => JsonQuery.Resolve(Document, "$.store[");

        act.Should().Throw<BindingEvaluationException>();
    }

    [Theory]
    [InlineData("store.book")]
    [InlineData("@.price")]
    [InlineData("#invalid")]
    public void Resolve_UnrecognizedPrefix_Throws(string expression)
    {
        var act = () => JsonQuery.Resolve(Document, expression);

        act.Should().Throw<BindingEvaluationException>();
    }

    [Theory]
    [InlineData("SECRET_SENTINEL", "The query is neither a JSON Pointer (must start with '/') nor a JSONPath expression (must start with '$').")]
    [InlineData("$.SECRET_SENTINEL[", "The JSONPath query is not valid.")]
    [InlineData("$.items[99999999999999999999]", "The JSONPath query is not valid*")]
    [InlineData("$.items[?(@.name =~ /SECRET_SENTINEL(/)]", "The JSONPath query is not valid.")]
    [InlineData("/SECRET~2", "Invalid JSON Pointer escape at offset 7 of the query: '~' must be followed by '0' or '1'.")]
    [InlineData("/a/SECRET~", "Invalid JSON Pointer escape at offset 9 of the query: '~' must be followed by '0' or '1'.")]
    public void Resolve_Invalid_MessageDoesNotQuoteTheQuery(string expression, string message)
    {
        // The query can be the value of a nested statement, such as @config(@secret(name)).
        var document = JObject.Parse("""{ "items": [ { "name": "a" } ], "a": { "SECRET": 1 } }""");

        var act = () => JsonQuery.Resolve(document, expression);

        var thrown = act.Should().Throw<BindingEvaluationException>().WithMessage(message).Which;
        thrown.Message.Should().NotContain("SECRET").And.NotContain("99999999999999999999");
    }

    [Fact]
    public void Resolve_NullRoot_Throws()
    {
        var act = () => JsonQuery.Resolve(null!, "/a");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_NullExpression_Throws()
    {
        var act = () => JsonQuery.Resolve(Document, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
