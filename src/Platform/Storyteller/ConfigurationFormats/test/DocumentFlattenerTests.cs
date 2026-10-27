using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

public class DocumentFlattenerTests
{
    [Fact]
    public void TheKeys_CompareCaseInsensitively_AsIConfigurationDoes()
    {
        var keys = DocumentFlattener.Flatten(JObject.Parse("""{ "Db": { "Host": "db.local" } }"""));

        keys["db:host"].ShouldBe("db.local");
    }

    [Fact]
    public void ADocumentReadByTheSdk_KeepsDateLikeStrings()
    {
        // As the SDK reads a body: date-like strings stay strings.
        using var reader = new JsonTextReader(new StringReader("""{ "db": { "port": 5432, "renewal": "2026-10-20T12:00:00.1230000+02:00" } }"""))
        {
            DateParseHandling = DateParseHandling.None,
        };

        var keys = DocumentFlattener.Flatten(JToken.ReadFrom(reader));

        keys["db:renewal"].ShouldBe("2026-10-20T12:00:00.1230000+02:00");
        keys["db:port"].ShouldBe("5432");
    }

    [Fact]
    public void ANullContent_HasNoKeys()
    {
        DocumentFlattener.Flatten(null).ShouldBeEmpty();
        DocumentFlattener.Flatten(JValue.CreateNull()).ShouldBeEmpty();
    }

    [Fact]
    public void ADateTokenBuiltInCode_KeepsItsValue()
    {
        var content = new JObject { ["at"] = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(2)) };

        DocumentFlattener.Flatten(content)["at"].ShouldBe("2026-10-20T12:00:00.0000000+02:00");
    }

    [Fact]
    public void AnError_NamesThePathButNotTheValue()
    {
        var exception = Should.Throw<InvalidDocumentException>(
            () => DocumentFlattener.Flatten(JObject.Parse("""{ "Db": { "Password": "s3cret" }, "db": { "password": "0th3r" } }""")));

        exception.Message.ShouldContain("$.Db.Password");
        exception.Message.ShouldContain("$.db.password");
        exception.Message.ShouldNotContain("s3cret");
        exception.Message.ShouldNotContain("0th3r");
    }

    [Fact]
    public void Read_ReturnsTheLeavesInDocumentOrder_WithTheirStepsKindsAndPaths()
    {
        var leaves = DocumentFlattener.Read(JObject.Parse("""{ "db": { "port": 5432, "hosts": [ "a", null, "c" ] }, "on": true, "a.b": 0.5 }"""));

        leaves.Select(leaf => leaf.Path).ShouldBe(["$.db.port", "$.db.hosts[0]", "$.db.hosts[2]", "$.on", "$['a.b']"]);
        leaves.Select(leaf => leaf.Value).ShouldBe(["5432", "a", "c", "true", "0.5"]);
        leaves.Select(leaf => leaf.Kind).ShouldBe(
            [DocumentValueKind.Number, DocumentValueKind.String, DocumentValueKind.String, DocumentValueKind.Boolean, DocumentValueKind.Number]);
        leaves[2].Segments.ShouldBe([DocumentSegment.Property("db"), DocumentSegment.Property("hosts"), DocumentSegment.Item(2)]);
        leaves[2].Segments[2].IsIndex.ShouldBeTrue();
    }

    [Fact]
    public void Flatten_IsTheColonProjectionOfRead()
    {
        var content = JObject.Parse("""{ "list": [ "x", null, { "name": "w" }, [ "nested" ] ], "Db": { "Host": "a" } }""");

        DocumentFlattener.Flatten(content).ShouldBe(
            new Dictionary<string, string>
            {
                ["list:0"] = "x",
                ["list:2:name"] = "w",
                ["list:3:0"] = "nested",
                ["Db:Host"] = "a",
            },
            ignoreOrder: true);
        DocumentFlattener.Read(content).Count.ShouldBe(4);
    }

    [Fact]
    public void ReadWithoutValidation_AppliesNoRule()
    {
        var leaves = DocumentFlattener.Read(JObject.Parse("""{ "a:b": "x", "": "y", "C": "1", "c": "2" }"""), validate: false);

        leaves.Select(leaf => leaf.Path).ShouldBe(["$['a:b']", "$['']", "$.C", "$.c"]);
        leaves[0].Segments.ShouldBe([DocumentSegment.Property("a:b")]);
    }

    [Fact]
    public void ReadWithoutValidation_HasNoLeaves_ForAContentThatIsNotAnObject()
    {
        DocumentFlattener.Read(JArray.Parse("""[ "a" ]"""), validate: false).ShouldBeEmpty();
        Should.Throw<InvalidDocumentException>(() => DocumentFlattener.Read(JArray.Parse("""[ "a" ]""")))
            .Rule.ShouldBe(DocumentRules.NotAnObject);
    }

    [Fact]
    public void ALeaf_LeavesItsValueOutOfToString()
    {
        var leaf = DocumentFlattener.Read(JObject.Parse("""{ "password": "s3cret" }"""))[0];

        leaf.ToString().ShouldNotContain("s3cret");
        leaf.ToString().ShouldContain("$.password");
    }
}
