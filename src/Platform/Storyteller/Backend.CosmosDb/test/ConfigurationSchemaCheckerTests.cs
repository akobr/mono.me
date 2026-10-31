using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
    // 30 'a' and a '!': about 2^30 steps of backtracking for ^(a+)+$
    private static readonly string Evil = $"{new string('a', 30)}!";

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

    // a real catastrophic match: the checker's own timeout bounds it, not the process default of the test host
    [Theory]
    [InlineData("""{ "type": "object", "properties": { "p": { "type": "string", "pattern": "^(a+)+$" } } }""", """{ "p": "{evil}" }""")]
    [InlineData("""{ "properties": { "list": { "type": "array", "items": { "type": "string", "pattern": "^(a+)+$" } } } }""", """{ "list": ["aaa", "{evil}"] }""")]
    [InlineData("""{ "oneOf": [ { "type": "object", "properties": { "p": { "pattern": "^(a+)+$" } } }, { "type": "string" } ] }""", """{ "p": "{evil}" }""")]
    [InlineData("""{ "type": "object", "patternProperties": { "^(a+)+$": { "type": "integer" } } }""", """{ "{evil}": 1 }""")]
    public async Task Validate_OfAPatternThatRunsOutOfItsTimeout_IsASchemaValidationTimeout(string schemaContent, string content)
    {
        var checker = Checker(new ConfigurationSchemaOptions { PatternMatchTimeout = TimeSpan.FromMilliseconds(20) });
        var schema = await checker.ParseAsync(JObject.Parse(schemaContent));

        var validate = () => checker.Validate(schema, JObject.Parse(content.Replace("{evil}", Evil)));

        var exception = validate.Should().Throw<InvalidInputException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.SchemaValidationTimeout);
        exception.Message.Should().Be("The pattern '^(a+)+$' of the schema took longer than 20 ms to match a value; simplify the pattern.");
    }

    [Fact]
    public async Task Validate_OfALongPatternThatRunsOutOfItsTimeout_QuotesItShortened()
    {
        var pattern = $"^(a+)+$|{new string('x', 300)}";
        var checker = Checker(new ConfigurationSchemaOptions { PatternMatchTimeout = TimeSpan.FromMilliseconds(20) });
        var schema = await checker.ParseAsync(new JObject { ["properties"] = new JObject { ["p"] = new JObject { ["pattern"] = pattern } } });

        var validate = () => checker.Validate(schema, new JObject { ["p"] = Evil });

        var exception = validate.Should().Throw<InvalidInputException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.SchemaValidationTimeout);
        exception.Message.Should().Contain("xxx…'").And.NotContain(new string('x', 101));
    }

    // every step of the validator reads the clock, which advances a second per reading here
    [Fact]
    public async Task Validate_OverItsBudget_IsASchemaValidationTimeout()
    {
        var checker = Checker(new ConfigurationSchemaOptions(), new SteppingTimeProvider());
        var schema = await checker.ParseAsync(JObject.Parse("""{ "properties": { "list": { "type": "array", "items": { "type": "integer" } } } }"""));

        var validate = () => checker.Validate(schema, new JObject { ["list"] = new JArray(Enumerable.Range(0, 50)) });

        var exception = validate.Should().Throw<InvalidInputException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.SchemaValidationTimeout);
        exception.Message.Should().Be("Checking the document against its schema takes longer than 10 s; simplify the schema or the document.");
    }

    [Fact]
    public async Task Parse_OverItsBudget_IsASchemaValidationTimeout()
    {
        var exception = await ParseFailureAsync(WithReference("#/definitions/a"), Checker(new ConfigurationSchemaOptions { CheckBudget = TimeSpan.Zero }));

        exception.ErrorCode.Should().Be(ErrorCodes.SchemaValidationTimeout);
        exception.Message.Should().Be("Reading the schema takes longer than 0 ms; simplify its references.");
    }

    // NJsonSchema serializes an object once per entry within one step, which the clock would not see
    [Fact]
    public async Task Validate_OfAnObjectAgainstALargeEnum_IsASchemaValidationTimeoutWithoutTheClock()
    {
        var entries = new JArray(Enumerable.Range(0, 2_000).Select(index => new JObject { ["k"] = index }));
        var checker = Checker(new ConfigurationSchemaOptions(), new SteppingTimeProvider(step: TimeSpan.Zero));
        var onTheObject = await checker.ParseAsync(new JObject { ["enum"] = entries });
        var onAScalar = await checker.ParseAsync(new JObject { ["properties"] = new JObject { ["v"] = new JObject { ["enum"] = entries } } });

        var large = () => checker.Validate(onTheObject, new JObject { ["data"] = new string('x', 30_000) });

        large.Should().Throw<InvalidInputException>().Which.ErrorCode.Should().Be(ErrorCodes.SchemaValidationTimeout);
        checker.Validate(onAScalar, new JObject { ["v"] = "x" }).Should().Equal("#/v: NotInEnumeration");
    }

    // a step depends only on its value and its schema: meeting the pair again below itself never ends
    [Theory]
    [InlineData("""{ "not": { "$ref": "#" } }""", "{}")]
    [InlineData("""{ "oneOf": [ { "$ref": "#" }, { "type": "string" } ] }""", "{}")]
    [InlineData("""{ "type": "object", "anyOf": [ { "not": { "not": { "$ref": "#" } } } ] }""", """{ "a": 1 }""")]
    [InlineData("""{ "properties": { "x": { "not": { "$ref": "#/properties/x" } } } }""", """{ "x": 1 }""")]
    public async Task Validate_OfASchemaThatAppliesItselfToTheSameValue_IsAnInvalidSchema(string schemaContent, string content)
    {
        var checker = Checker();
        var schema = await checker.ParseAsync(JObject.Parse(schemaContent));

        var validate = () => checker.Validate(schema, JObject.Parse(content));

        var exception = validate.Should().Throw<InvalidInputException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidSchema);
        exception.Message.Should().Be(
            "Invalid JSON Schema: the schema applies itself to the same value again (for example \"not\": { \"$ref\": \"#\" }), so its check would never end.");
    }

    // the same schema on child values is no cycle
    [Fact]
    public async Task Validate_OfARecursiveSchemaOverNestedValues_Passes()
    {
        var checker = Checker();
        var schema = await checker.ParseAsync(JObject.Parse("""
            { "type": "object", "properties": { "child": { "$ref": "#" }, "name": { "type": "string" } } }
            """));

        checker.Validate(schema, JObject.Parse("""{ "child": { "child": { "child": { "name": 5 } } } }"""))
            .Should().Equal("#/child.child.child.name: StringExpected");
        checker.Validate(schema, JObject.Parse("""{ "x": 1 }""")).Should().BeEmpty();
    }

    // NJsonSchema asks whether a declared property's schema is nullable when its value is null, before the property's
    // own step: a cycle through oneOf would overflow the stack there
    [Theory]
    [InlineData("""{ "properties": { "x": { "oneOf": [ { "$ref": "#/properties/x" }, { "type": "string" } ] } } }""")]
    [InlineData("""{ "definitions": { "a": { "oneOf": [ { "$ref": "#/definitions/b" } , { "type": "integer" } ] }, "b": { "oneOf": [ { "$ref": "#/definitions/a" }, { "type": "string" } ] } }, "properties": { "x": { "$ref": "#/definitions/a" } } }""")]
    public async Task Validate_OfANullValueWhoseSchemaIsNullableThroughACycle_IsAnInvalidSchema(string schemaContent)
    {
        var checker = Checker();
        var schema = await checker.ParseAsync(JObject.Parse(schemaContent));

        var validate = () => checker.Validate(schema, JObject.Parse("""{ "x": null }"""));

        var exception = validate.Should().Throw<InvalidInputException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidSchema);
        exception.Message.Should().Contain("applies itself to the same value again");
    }

    // NJsonSchema's IsNullable uses more stack per level than the guard's walk, so the walk is limited by a fixed depth:
    // on a 1.5 MiB stack, like a thread-pool thread, no length of the chain may pass the guard and then overflow
    [Theory]
    [InlineData(100, null)]
    [InlineData(300, ErrorCodes.InvalidSchema)]
    [InlineData(1_524, ErrorCodes.InvalidSchema)]
    [InlineData(1_550, ErrorCodes.InvalidSchema)]
    [InlineData(1_600, ErrorCodes.InvalidSchema)]
    public async Task Validate_OfANullValueWhoseSchemaIsALongChainOfOneOf_NeverOverflowsTheStack(int levels, string expectedCode)
    {
        var definitions = new JObject { [$"d{levels}"] = new JObject { ["type"] = "null" } };

        for (var level = 0; level < levels; level++)
        {
            definitions[$"d{level}"] = new JObject
            {
                ["type"] = "object",
                ["nullable"] = true,
                ["oneOf"] = new JArray(new JObject { ["$ref"] = $"#/definitions/d{level + 1}" }),
            };
        }

        var schemaContent = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["x"] = new JObject { ["$ref"] = "#/definitions/d0" } },
            ["definitions"] = definitions,
        };
        Exception failure = null;

        // a long chain is refused when the schema is read (MaxReferenceChain), or by the walk's depth
        var thread = new Thread(
            () =>
            {
                try
                {
                    var checker = Checker();
                    var schema = checker.ParseAsync(schemaContent).GetAwaiter().GetResult();
                    checker.Validate(schema, JObject.Parse("""{ "x": null }"""));
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            },
            maxStackSize: 1536 * 1024);
        thread.Start();
        thread.Join();
        await Task.CompletedTask;

        if (expectedCode is null)
        {
            failure.Should().BeNull();
        }
        else
        {
            failure.Should().BeOfType<InvalidInputException>().Which.ErrorCode.Should().Be(expectedCode);
        }
    }

    // NJsonSchema follows a chain of references recursively within one step, before the guard runs: on a 1.5 MiB stack,
    // a long chain is refused when the schema is read, and a short one still checks
    [Theory]
    [InlineData("allOf", 100, null)]
    [InlineData("oneOf", 200, null)]
    [InlineData("$ref", 200, null)]
    [InlineData("allOf", 6_000, ErrorCodes.InvalidSchema)]
    [InlineData("oneOf", 12_000, ErrorCodes.InvalidSchema)]
    [InlineData("anyOf", 6_000, ErrorCodes.InvalidSchema)]
    [InlineData("$ref", 6_000, ErrorCodes.InvalidSchema)]
    public async Task Check_OfALongChainOfReferences_NeverOverflowsTheStack(string wrapper, int levels, string expectedCode)
    {
        var definitions = new JObject { [$"d{levels}"] = new JObject { ["type"] = "string" } };

        for (var level = 0; level < levels; level++)
        {
            var reference = new JObject { ["$ref"] = $"#/definitions/d{level + 1}" };
            definitions[$"d{level}"] = wrapper == "$ref" ? reference : new JObject { [wrapper] = new JArray(reference) };
        }

        var schemaContent = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["x"] = new JObject { ["$ref"] = "#/definitions/d0" } },
            ["definitions"] = definitions,
        };
        Exception failure = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    var checker = Checker();
                    var schema = checker.ParseAsync(schemaContent).GetAwaiter().GetResult();
                    checker.Validate(schema, new JObject());
                    checker.Validate(schema, JObject.Parse("""{ "x": "a" }"""));
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            },
            maxStackSize: 1536 * 1024);
        thread.Start();
        thread.Join();
        await Task.CompletedTask;

        if (expectedCode is null)
        {
            failure.Should().BeNull();
        }
        else
        {
            var exception = failure.Should().BeOfType<InvalidInputException>().Which;
            exception.ErrorCode.Should().Be(expectedCode);
            exception.Message.Should().Contain("chains more than 512 schemas through references");
        }
    }

    // without a cycle, IsNullable still takes exponential time over a graph of oneOf references, within one step
    [Fact]
    public async Task Validate_OfANullValueWhoseSchemaIsAGraphOfOneOf_IsASchemaValidationTimeoutWithoutTheClock()
    {
        var definitions = new JObject { ["a20"] = new JObject { ["type"] = "string" } };

        for (var level = 0; level < 20; level++)
        {
            var next = new JObject { ["$ref"] = $"#/definitions/a{level + 1}" };
            definitions[$"a{level}"] = new JObject { ["oneOf"] = new JArray(next, next.DeepClone()) };
        }

        var checker = Checker(new ConfigurationSchemaOptions(), new SteppingTimeProvider(step: TimeSpan.Zero));
        var schema = await checker.ParseAsync(new JObject
        {
            ["definitions"] = definitions,
            ["properties"] = new JObject { ["x"] = new JObject { ["$ref"] = "#/definitions/a0" } },
        });

        var validate = () => checker.Validate(schema, JObject.Parse("""{ "x": null }"""));

        validate.Should().Throw<InvalidInputException>().Which.ErrorCode.Should().Be(ErrorCodes.SchemaValidationTimeout);
    }

    // NJsonSchema compares each name of 'required' that the object lacks with every property of the object, within one
    // step
    [Fact]
    public async Task Validate_OfAnObjectThatLacksManyRequiredNames_IsASchemaValidationTimeoutWithoutTheClock()
    {
        var checker = Checker(new ConfigurationSchemaOptions(), new SteppingTimeProvider(step: TimeSpan.Zero));
        var required = await checker.ParseAsync(new JObject { ["required"] = new JArray(Enumerable.Range(0, 10_000).Select(index => $"r{index}")) });
        var declared = await checker.ParseAsync(new JObject
        {
            ["properties"] = new JObject(Enumerable.Range(0, 10_000).Select(index => new JProperty($"p{index}", new JObject { ["type"] = "integer" }))),
        });
        var others = new JObject(Enumerable.Range(0, 10_000).Select(index => new JProperty($"o{index}", index)));
        var present = new JObject(Enumerable.Range(0, 10_000).Select(index => new JProperty($"p{index}", index)));

        var lacking = () => checker.Validate(required, others);

        lacking.Should().Throw<InvalidInputException>().Which.ErrorCode.Should().Be(ErrorCodes.SchemaValidationTimeout);
        checker.Validate(declared, present).Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "allOf": [ { "$ref": "#" } ] }""", "{}")]
    [InlineData("""{ "properties": { "p": { "pattern": "(" } } }""", """{ "p": "x" }""")]
    [InlineData("""{ "properties": { "n": { "multipleOf": 0 } } }""", """{ "n": 3 }""")]
    [InlineData("""{ "properties": { "n": { "multipleOf": 1e-30 } } }""", """{ "n": 3.5 }""")]
    public async Task Validate_OfASchemaWhoseCheckCannotRun_IsAnInvalidSchema(string schemaContent, string content)
    {
        var checker = Checker();
        var schema = await checker.ParseAsync(JObject.Parse(schemaContent));

        var validate = () => checker.Validate(schema, JObject.Parse(content));

        var exception = validate.Should().Throw<InvalidInputException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidSchema);
        exception.Message.Should().StartWith("Invalid JSON Schema: ");
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Dispose();
    }

    private static ConfigurationSchemaChecker Checker(ConfigurationSchemaOptions options = null, TimeProvider timeProvider = null)
    {
        return new ConfigurationSchemaChecker(options ?? new ConfigurationSchemaOptions(), timeProvider ?? TimeProvider.System);
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

    private static async Task<InvalidInputException> ParseFailureAsync(JObject schema, ConfigurationSchemaChecker checker = null)
    {
        var parse = () => (checker ?? Checker()).ParseAsync(schema);
        return (await parse.Should().ThrowAsync<InvalidInputException>()).Which;
    }

    // a clock that advances by its step on every reading
    private sealed class SteppingTimeProvider(TimeSpan? step = null) : TimeProvider
    {
        private readonly long _step = (step ?? TimeSpan.FromSeconds(1)).Ticks;
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return Interlocked.Add(ref _now, _step);
        }
    }
}
