using System.Linq;
using System.Text.Json.Nodes;
using _42.Platform.Cli.Json;
using Json.Patch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests;

public class JsonPatchBuilderTests
{
    [Fact]
    public void EqualDocuments_ProduceNoOperations()
    {
        var original = JObject.Parse("""{ "a": 1, "b": { "c": [1, 2] } }""");
        var target = JObject.Parse("""{ "b": { "c": [1, 2] }, "a": 1 }""");

        JsonPatchBuilder.Create(original, target).ShouldBeEmpty();
    }

    [Fact]
    public void RemovedProperties_AreRemoved()
    {
        var original = JObject.Parse("""{ "keep": 1, "drop": 2, "nested": { "keep": true, "drop": "x" } }""");
        var target = JObject.Parse("""{ "keep": 1, "nested": { "keep": true } }""");

        var patch = JsonPatchBuilder.Create(original, target);

        patch.Select(op => ((string)op["op"]!, (string)op["path"]!))
            .ShouldBe([("remove", "/drop"), ("remove", "/nested/drop")], ignoreOrder: true);
        ApplyWithServerLibrary(original, patch).ShouldBe(target);
    }

    [Fact]
    public void ChangedArray_IsReplacedAsWhole()
    {
        // a merge would union the arrays and keep "b"
        var original = JObject.Parse("""{ "features": ["a", "b", "c"] }""");
        var target = JObject.Parse("""{ "features": ["a", "c"] }""");

        var patch = JsonPatchBuilder.Create(original, target);

        patch.Count.ShouldBe(1);
        ((string)patch[0]["op"]!).ShouldBe("replace");
        ((string)patch[0]["path"]!).ShouldBe("/features");
        ApplyWithServerLibrary(original, patch).ShouldBe(target);
    }

    [Fact]
    public void AddedReplacedAndNullValues_AreApplied()
    {
        // a merge would ignore the null
        var original = JObject.Parse("""{ "retries": 3, "owner": "platform", "limits": { "cpu": 1 } }""");
        var target = JObject.Parse("""{ "retries": 5, "owner": null, "limits": { "cpu": 1, "memory": "1G" }, "tags": { "tier": "gold" } }""");

        var patch = JsonPatchBuilder.Create(original, target);

        ApplyWithServerLibrary(original, patch).ShouldBe(target);
    }

    [Fact]
    public void ChangedValueType_IsReplaced()
    {
        var original = JObject.Parse("""{ "value": { "nested": 1 }, "other": [1] }""");
        var target = JObject.Parse("""{ "value": "flat", "other": { "x": 1 } }""");

        var patch = JsonPatchBuilder.Create(original, target);

        patch.Select(op => (string)op["op"]!).ShouldAllBe(op => op == "replace");
        ApplyWithServerLibrary(original, patch).ShouldBe(target);
    }

    [Fact]
    public void SpecialCharactersInChangedNames_ReplaceWholeDocument()
    {
        // the server doesn't decode escaped pointers (~0, ~1), a single replace of the document avoids them
        var original = JObject.Parse("""{ "a/b": 1, "c~d": 2, "plain": { "x": 1 } }""");
        var target = JObject.Parse("""{ "a/b": 3, "plain": { "x": 1 } }""");

        var patch = JsonPatchBuilder.Create(original, target);

        patch.Count.ShouldBe(1);
        ((string)patch[0]["op"]!).ShouldBe("replace");
        ((string)patch[0]["path"]!).ShouldBe(string.Empty);
        ApplyWithServerLibrary(original, patch).ShouldBe(target);
    }

    [Fact]
    public void SpecialCharactersInUnchangedNames_KeepFineGrainedPatch()
    {
        var original = JObject.Parse("""{ "a/b": 1, "retries": 3 }""");
        var target = JObject.Parse("""{ "a/b": 1, "retries": 5 }""");

        var patch = JsonPatchBuilder.Create(original, target);

        patch.Select(op => (string)op["path"]!).ShouldBe(["/retries"]);
        ApplyWithServerLibrary(original, patch).ShouldBe(target);
    }

    [Fact]
    public void EmptyTarget_RemovesEverything()
    {
        var original = JObject.Parse("""{ "a": 1, "b": [1], "c": { "d": 1 } }""");
        var target = new JObject();

        var patch = JsonPatchBuilder.Create(original, target);

        patch.Count.ShouldBe(3);
        ApplyWithServerLibrary(original, patch).ShouldBe(target);
    }

    // the same conversion and library as JsonExtensions.ApplyPatch in Backend.CosmosDb
    private static JObject ApplyWithServerLibrary(JObject original, JArray patchOperations)
    {
        var patch = System.Text.Json.JsonSerializer.Deserialize<JsonPatch>(patchOperations.ToString(Formatting.None))!;
        var result = patch.Apply(JsonNode.Parse(original.ToString(Formatting.None)));

        result.IsSuccess.ShouldBeTrue(result.Error);
        return JObject.Parse(result.Result!.ToJsonString());
    }
}
