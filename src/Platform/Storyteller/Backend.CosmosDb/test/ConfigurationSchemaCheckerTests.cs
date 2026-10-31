using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The parse and the check of configuration schemas, without Cosmos: references stay inside the schema, and every
// failure is an InvalidInputException with a schema code.
public sealed class ConfigurationSchemaCheckerTests : IDisposable
{
    // the target of the URL references: NJsonSchema's default resolver would connect to it
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public ConfigurationSchemaCheckerTests()
    {
        _listener.Start();
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    [Theory]
    [InlineData("http://127.0.0.1:{port}/x.json", "http://127.0.0.1:{port}/x.json")]
    [InlineData("https://127.0.0.1:{port}/x.json#/definitions/a", "https://127.0.0.1:{port}/x.json")]
    [InlineData("HTTP://127.0.0.1:{port}/x.json", "HTTP://127.0.0.1:{port}/x.json")]
    [InlineData("//127.0.0.1:{port}/x.json", "//127.0.0.1:{port}/x.json")]
    [InlineData("file:///etc/passwd", "file:///etc/passwd")]
    [InlineData("other.json#/definitions/a", "other.json#/definitions/a")]
    [InlineData("/etc/passwd", "/etc/passwd")]
    [InlineData("C:/Windows/win.ini", "C:/Windows/win.ini")]
    [InlineData("urn:example:a", "urn:example:a")]
    public async Task Parse_OfAnExternalReference_RefusesItWithoutConnecting(string reference, string quoted)
    {
        var schema = WithReference(reference.Replace("{port}", Port.ToString()));

        var exception = await ParseFailureAsync(schema);

        exception.ErrorCode.Should().Be(ErrorCodes.ExternalSchemaReference);
        exception.Message.Should().Be(
            $"External $ref not allowed: '{quoted.Replace("{port}", Port.ToString())}'. Only '#' and JSON Pointers inside the schema ('#/…') are resolved.");
        _listener.Pending().Should().BeFalse();
    }

    // every reference of the document is resolved on parse, used or not
    [Fact]
    public async Task Parse_OfAnUnusedExternalReference_RefusesIt()
    {
        var schema = JObject.Parse($$"""
            {
                "type": "object",
                "definitions": { "unused": { "$ref": "http://127.0.0.1:{{Port}}/x.json" } }
            }
            """);

        var exception = await ParseFailureAsync(schema);

        exception.ErrorCode.Should().Be(ErrorCodes.ExternalSchemaReference);
        _listener.Pending().Should().BeFalse();
    }

    [Fact]
    public async Task Parse_OfAnExternalReference_QuotesNoPathOfTheHostAndShortensALongOne()
    {
        var relative = await ParseFailureAsync(WithReference("../secrets/other.json"));
        var longReference = await ParseFailureAsync(WithReference($"http://127.0.0.1:{Port}/{new string('x', 500)}.json"));

        relative.Message.Should().Contain("'../secrets/other.json'").And.NotContain(":\\").And.NotContain("configuration-schema");
        longReference.Message.Should().Contain("xxx…'").And.NotContain(new string('x', 101));
    }

    [Theory]
    [InlineData("#foo")]
    [InlineData("#definitions/a")]
    public async Task Parse_OfALocalReferenceThatIsNoPointer_IsAnInvalidSchema(string reference)
    {
        var exception = await ParseFailureAsync(WithReference(reference));

        exception.ErrorCode.Should().Be(ErrorCodes.InvalidSchema);
        exception.Message.Should().Be(
            $"Invalid JSON Schema: The $ref '{reference}' is not supported; a reference inside the schema is '#' or a JSON Pointer ('#/…').");
    }

    [Fact]
    public async Task Parse_OfAMissingLocalTarget_IsAnInvalidSchema()
    {
        var exception = await ParseFailureAsync(WithReference("#/definitions/missing"));

        exception.ErrorCode.Should().Be(ErrorCodes.InvalidSchema);
        exception.Message.Should().StartWith("Invalid JSON Schema: ");
    }

    [Fact]
    public async Task Parse_OfADocumentThatIsNoSchema_IsAnInvalidSchema()
    {
        var exception = await ParseFailureAsync(JObject.Parse("""{ "type": "object", "properties": [] }"""));

        exception.ErrorCode.Should().Be(ErrorCodes.InvalidSchema);
        exception.Message.Should().StartWith("Invalid JSON Schema: ");
    }

    [Fact]
    public async Task Validate_WithLocalReferences_AppliesTheReferencedDefinitions()
    {
        var checker = Checker();
        var schema = await checker.ParseAsync(JObject.Parse("""
            {
                "type": "object",
                "definitions": { "name": { "type": "string", "maxLength": 5 } },
                "properties": {
                    "name": { "$ref": "#/definitions/name" },
                    "child": { "$ref": "#" }
                }
            }
            """));

        checker.Validate(schema, JObject.Parse("""{ "name": "ok", "child": { "name": "fine" } }""")).Should().BeEmpty();
        checker.Validate(schema, JObject.Parse("""{ "name": "too long", "child": { "name": 5 } }"""))
            .Should().BeEquivalentTo("#/name: StringTooLong", "#/child.name: StringExpected");
    }

    // the messages of JsonSchema.Validate(string), format validators included
    [Fact]
    public async Task Validate_ReportsWhatJsonSchemaValidateReports()
    {
        var content = JObject.Parse("""
            {
                "email": "not-an-email",
                "when": "2026-10-31T10:00:00Z",
                "count": -1,
                "kind": "other",
                "tags": ["a", "a"]
            }
            """);
        var schemaContent = JObject.Parse("""
            {
                "type": "object",
                "required": ["name"],
                "properties": {
                    "email": { "type": "string", "format": "email" },
                    "when": { "type": "string", "pattern": "^\\d{4}-\\d{2}-\\d{2}$" },
                    "count": { "type": "integer", "minimum": 0 },
                    "kind": { "enum": ["a", "b"] },
                    "tags": { "type": "array", "uniqueItems": true }
                },
                "additionalProperties": false
            }
            """);
        var checker = Checker();

        var errors = checker.Validate(await checker.ParseAsync(schemaContent), content);

        var expected = (await JsonSchema.FromJsonAsync(schemaContent.ToString(Formatting.None)))
            .Validate(content.ToString(Formatting.None))
            .Select(error => $"{error.Path}: {error.Kind}");
        errors.Should().Equal(expected).And.Contain("#/email: EmailExpected").And.Contain("#/when: PatternMismatch");
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Dispose();
    }

    private static ConfigurationSchemaChecker Checker()
    {
        return new ConfigurationSchemaChecker();
    }

    private static JObject WithReference(string reference)
    {
        return new JObject
        {
            ["type"] = "object",
            ["definitions"] = new JObject { ["a"] = new JObject { ["type"] = "string" } },
            ["properties"] = new JObject { ["p"] = new JObject { ["$ref"] = reference } },
        };
    }

    private static async Task<InvalidInputException> ParseFailureAsync(JObject schema)
    {
        var parse = () => Checker().ParseAsync(schema);
        return (await parse.Should().ThrowAsync<InvalidInputException>()).Which;
    }
}
