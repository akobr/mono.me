using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Pure JSON tests, no Cosmos DB needed.
public class JsonPatchTests
{
    private static JObject Document => JObject.Parse("""{ "retries": 3, "owner": "team-a" }""");

    [Fact]
    public async Task ApplyPatch_ValidOperations_ReturnsThePatchedDocument()
    {
        var patch = JArray.Parse("""
            [
              { "op": "test", "path": "/retries", "value": 3 },
              { "op": "replace", "path": "/retries", "value": 5 },
              { "op": "remove", "path": "/owner" }
            ]
            """);

        var result = await Document.ApplyPatch(patch);

        result.Should().BeEquivalentTo(JObject.Parse("""{ "retries": 5 }"""));
    }

    [Fact]
    public async Task ApplyPatch_FailedTest_ThrowsTestFailedWithTheOperationIndex()
    {
        var patch = JArray.Parse("""
            [
              { "op": "replace", "path": "/owner", "value": "team-b" },
              { "op": "test", "path": "/retries", "value": 4 }
            ]
            """);

        var act = () => Document.ApplyPatch(patch);

        var exception = (await act.Should().ThrowAsync<JsonPatchException>()).Which;
        exception.Kind.Should().Be(JsonPatchFailureKind.TestFailed);
        exception.OperationIndex.Should().Be(1);
        exception.ErrorCode.Should().Be(ErrorCodes.PatchTestFailed);
    }

    // The copy to another view of the admin UI (424-309) replaces a whole own document with a test and a replace of
    // the root, as sform and the MCP server do for a member whose name needs a pointer escape.
    [Fact]
    public async Task ApplyPatch_RootTestAndReplace_ReplacesTheWholeDocument()
    {
        var replacement = JObject.Parse("""{ "a/b": 1, "m~n": { "deep": [1, 2] }, "retries": 5 }""");
        var patch = new JArray(
            new JObject { ["op"] = "test", ["path"] = string.Empty, ["value"] = Document },
            new JObject { ["op"] = "replace", ["path"] = string.Empty, ["value"] = replacement });

        var result = await Document.ApplyPatch(patch);

        result.Should().BeEquivalentTo(replacement);
        result.Properties().Select(property => property.Name).Should().Equal("a/b", "m~n", "retries");
    }

    [Fact]
    public async Task ApplyPatch_RootTestOfAChangedDocument_ThrowsTestFailed()
    {
        var read = JObject.Parse("""{ "retries": 3, "owner": "team-b" }""");
        var patch = new JArray(
            new JObject { ["op"] = "test", ["path"] = string.Empty, ["value"] = read },
            new JObject { ["op"] = "replace", ["path"] = string.Empty, ["value"] = new JObject { ["retries"] = 5 } });

        var act = () => Document.ApplyPatch(patch);

        var exception = (await act.Should().ThrowAsync<JsonPatchException>()).Which;
        exception.Kind.Should().Be(JsonPatchFailureKind.TestFailed);
        exception.OperationIndex.Should().Be(0);
        exception.ErrorCode.Should().Be(ErrorCodes.PatchTestFailed);
    }

    // A browser sends the document it read back as it parsed it: the members of an object in any order, and a number
    // such as 2.0 as 2. Neither is a change of the document.
    [Fact]
    public async Task ApplyPatch_RootTestOfTheSameDocumentWrittenAnotherWay_Passes()
    {
        var stored = JObject.Parse("""{ "ratio": 2.0, "limits": { "low": 1, "high": 1e2 }, "tags": ["a", "b"] }""");
        var read = JObject.Parse("""{ "tags": ["a", "b"], "limits": { "high": 100, "low": 1 }, "ratio": 2 }""");
        var patch = new JArray(
            new JObject { ["op"] = "test", ["path"] = string.Empty, ["value"] = read },
            new JObject { ["op"] = "replace", ["path"] = string.Empty, ["value"] = new JObject { ["ratio"] = 3 } });

        var result = await stored.ApplyPatch(patch);

        result.Should().BeEquivalentTo(JObject.Parse("""{ "ratio": 3 }"""));
    }

    // RFC 6901: ~1 is "/" and ~0 is "~" in a member name. The admin UI's editor (buildPatch) and the MCP server write
    // such pointers for a member whose name holds either character.
    [Fact]
    public async Task ApplyPatch_EscapedPointers_ChangeTheMembersTheyName()
    {
        var document = JObject.Parse("""{ "a/b": 1, "m~n": 2, "~/": 3, "a": { "b": 4 } }""");
        var patch = JArray.Parse("""
            [
              { "op": "test", "path": "/a~1b", "value": 1 },
              { "op": "replace", "path": "/a~1b", "value": 10 },
              { "op": "remove", "path": "/m~0n" },
              { "op": "add", "path": "/~0~1x", "value": 5 },
              { "op": "test", "path": "/~0~1", "value": 3 }
            ]
            """);

        var result = await document.ApplyPatch(patch);

        result.Should().BeEquivalentTo(JObject.Parse("""{ "a/b": 10, "~/": 3, "~/x": 5, "a": { "b": 4 } }"""));
    }

    [Fact]
    public async Task ApplyPatch_MissingPath_ThrowsOperationFailed()
    {
        var patch = JArray.Parse("""[{ "op": "replace", "path": "/missing/deep", "value": 1 }]""");

        var act = () => Document.ApplyPatch(patch);

        var exception = (await act.Should().ThrowAsync<JsonPatchException>()).Which;
        exception.Kind.Should().Be(JsonPatchFailureKind.OperationFailed);
        exception.ErrorCode.Should().Be(ErrorCodes.PatchInvalid);
    }

    [Theory]
    [InlineData("""[{ "op": "explode", "path": "/retries" }]""")]
    [InlineData("""[{ "op": "replace", "value": 1 }]""")]
    [InlineData("""[{ "op": "replace", "path": "retries-without-slash", "value": 1 }]""")]
    public async Task ApplyPatch_MalformedDocument_ThrowsInvalid(string patchJson)
    {
        var act = () => Document.ApplyPatch(JArray.Parse(patchJson));

        (await act.Should().ThrowAsync<JsonPatchException>()).Which.Kind.Should().Be(JsonPatchFailureKind.Invalid);
    }

    [Fact]
    public async Task ApplyPatchRequested_PatchNotAnArray_ThrowsInvalid()
    {
        var document = JObject.Parse("""{ "retries": 3, "$patch": { "op": "remove" } }""");

        var act = () => document.ApplyPatchRequested();

        (await act.Should().ThrowAsync<JsonPatchException>()).Which.Kind.Should().Be(JsonPatchFailureKind.Invalid);
    }

    [Theory]
    [InlineData("$[")]
    [InlineData("$..[?(@.x")]
    [InlineData("$.list[?(@.name =~ /[/)]")]
    [InlineData("$")]
    public void RemoveRequested_BrokenPath_ThrowsInvalid(string path)
    {
        var document = JObject.Parse("""{ "retries": 3, "list": [{ "name": "a" }] }""");
        document["$remove"] = new JArray(path);

        var act = () => document.RemoveRequested();

        var exception = act.Should().Throw<JsonPatchException>().Which;
        exception.Kind.Should().Be(JsonPatchFailureKind.Invalid);
        exception.ErrorCode.Should().Be(ErrorCodes.PatchInvalid);
        exception.Message.Should().Contain(path);
    }

    [Fact]
    public void RemoveRequested_TokenMatchedTwice_IsRemovedOnce()
    {
        var document = JObject.Parse("""{ "a": 1, "b": 2, "list": [1, 2], "$remove": ["$['a','a']", "$.list[0,0]"] }""");

        document.RemoveRequested().Should().BeEquivalentTo(JObject.Parse("""{ "b": 2, "list": [2] }"""));
    }

    [Fact]
    public void RemoveRequested_PathMatchingTheDirective_RemovesItWithoutBreakingTheLoop()
    {
        var document = JObject.Parse("""{ "a": 1, "$remove": ["$['$remove'][0]", "$.a"] }""");

        document.RemoveRequested().Should().BeEquivalentTo(new JObject());
    }

    [Fact]
    public async Task ApplyPatchRequested_FailedTest_ThrowsTestFailed()
    {
        var document = JObject.Parse("""{ "retries": 3, "$patch": [{ "op": "test", "path": "/retries", "value": 1 }] }""");

        var act = () => document.ApplyPatchRequested();

        (await act.Should().ThrowAsync<JsonPatchException>()).Which.Kind.Should().Be(JsonPatchFailureKind.TestFailed);
    }
}
