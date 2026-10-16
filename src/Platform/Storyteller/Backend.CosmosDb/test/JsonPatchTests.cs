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
    public async Task ApplyPatchRequested_FailedTest_ThrowsTestFailed()
    {
        var document = JObject.Parse("""{ "retries": 3, "$patch": [{ "op": "test", "path": "/retries", "value": 1 }] }""");

        var act = () => document.ApplyPatchRequested();

        (await act.Should().ThrowAsync<JsonPatchException>()).Which.Kind.Should().Be(JsonPatchFailureKind.TestFailed);
    }
}
