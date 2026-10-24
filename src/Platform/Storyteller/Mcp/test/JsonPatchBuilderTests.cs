using _42.Platform.Storyteller.Mcp.Json;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class JsonPatchBuilderTests
{
    [Fact]
    public void EqualDocuments_GiveNoOperations()
    {
        var patch = JsonPatchBuilder.Create(JObject.Parse("""{"a":1,"b":{"c":[1,2]}}"""), JObject.Parse("""{"b":{"c":[1,2]},"a":1}"""));

        patch.ShouldBeEmpty();
    }

    [Fact]
    public void TopLevelProperties_AreTestedThenReplacedOrRemoved_AndNewOnesAdded()
    {
        var stored = JObject.Parse("""{"retries":3,"owner":"team-a","nested":{"x":1,"y":2},"same":true}""");
        var desired = JObject.Parse("""{"retries":5,"nested":{"x":1},"same":true,"added":[1]}""");

        var patch = JsonPatchBuilder.Create(stored, desired);

        patch.ToString(Formatting.None).ShouldBe(
            """[{"op":"test","path":"/owner","value":"team-a"},{"op":"remove","path":"/owner"},"""
            + """{"op":"test","path":"/retries","value":3},{"op":"replace","path":"/retries","value":5},"""
            + """{"op":"test","path":"/nested","value":{"x":1,"y":2}},{"op":"replace","path":"/nested","value":{"x":1}},"""
            + """{"op":"add","path":"/added","value":[1]}]""");
    }

    [Fact]
    public void ApplyingThePatch_GivesTheDesiredDocument()
    {
        var stored = JObject.Parse("""{"a":1,"list":[1,2,3],"gone":null,"": "empty name"}""");
        var desired = JObject.Parse("""{"a":2,"list":[3],"new":{"deep":true}}""");

        var result = Apply(stored, JsonPatchBuilder.Create(stored, desired));

        JToken.DeepEquals(result, desired).ShouldBeTrue(result.ToString(Formatting.None));
    }

    [Fact]
    public void ChangedNameWithPointerEscapes_ReplacesTheWholeDocument()
    {
        var stored = JObject.Parse("""{"a/b":1,"keep":2}""");
        var desired = JObject.Parse("""{"a/b":3,"keep":2}""");

        var patch = JsonPatchBuilder.Create(stored, desired);

        patch.ToString(Formatting.None).ShouldBe(
            """[{"op":"test","path":"","value":{"a/b":1,"keep":2}},{"op":"replace","path":"","value":{"a/b":3,"keep":2}}]""");
    }

    [Fact]
    public void UnchangedNameWithPointerEscapes_KeepsTopLevelOperations()
    {
        var patch = JsonPatchBuilder.Create(JObject.Parse("""{"m~n":1,"a":1}"""), JObject.Parse("""{"m~n":1,"a":2}"""));

        patch.Count.ShouldBe(2);
        patch[1]!["path"]!.Value<string>().ShouldBe("/a");
    }

    [Fact]
    public void ParsedDates_AreComparedAsParsed_AndNeverTested()
    {
        // the SDK's template client parses date-like strings, so the stored text is unknown
        var stored = ReadWithDates("""{"at":"2026-10-24T10:00:00.000Z","list":["2026-01-01T00:00:00Z"],"n":1}""");
        var desired = JObject.Parse("""{"at":"2026-10-24T10:00:00.000Z","list":["2026-02-01T00:00:00Z"],"n":2}""");

        var patch = JsonPatchBuilder.Create(stored, desired);

        patch.ToString(Formatting.None).ShouldBe(
            """[{"op":"replace","path":"/list","value":["2026-02-01T00:00:00Z"]},"""
            + """{"op":"test","path":"/n","value":1},{"op":"replace","path":"/n","value":2}]""");
    }

    private static JObject ReadWithDates(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.DateTime };
        return JObject.Load(reader);
    }

    // A minimal RFC 6902 application of the operations the builder emits, with test checks.
    private static JObject Apply(JObject document, JArray patch)
    {
        JToken current = document.DeepClone();

        foreach (var operation in patch.Cast<JObject>())
        {
            var path = operation.Value<string>("path")!;
            var name = path.Length == 0 ? null : path[1..].Replace("~1", "/").Replace("~0", "~");
            var target = (JObject)current;

            switch (operation.Value<string>("op"))
            {
                case "test":
                    JToken.DeepEquals(name is null ? current : target[name], operation["value"]).ShouldBeTrue(path);
                    break;
                case "remove":
                    target.Remove(name!).ShouldBeTrue(path);
                    break;
                case "add":
                case "replace":
                    if (name is null)
                    {
                        current = operation["value"]!.DeepClone();
                    }
                    else
                    {
                        target[name] = operation["value"]!.DeepClone();
                    }

                    break;
            }
        }

        return (JObject)current;
    }
}
