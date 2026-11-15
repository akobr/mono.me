using _42.Platform.Storyteller.DbCreator.Commands;
using _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.Commands;

// 2sInitDb sets: the files only, exit 2 for a catalog that cannot be read.
public class SetsCommandTests
{
    private readonly SeedFixture _fixture = new();

    [Fact]
    public async Task Sets_ListsTheBuiltInAndTheGivenSets()
    {
        _fixture.AddSet("base", """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""", ("annotations/sbt.base.json", "{}"));
        var own = Path.Combine(SeedFixture.Tool + "-own", "own");
        SetFiles.Add(_fixture.FileSystem, own, ("set.json", """{ "DependsOn": [ "base", "missing" ] }"""), ("annotations.jsonl", """{"$count":2,"AnnotationKey":"sbt.g-{n}"}"""));

        (await Command(own).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        _fixture.Containers.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sets_ListsTheLevelsOfThePairs_AndRefusesPairsASeedCannotWrite()
    {
        _fixture.AddSet(
            "base",
            """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""",
            ("key-values.json", """{ "Items": [ { "Key": "a", "Protection": "Public", "Value": "1" }, { "Key": "b", "Protection": "Standard", "Value": "2" } ] }"""));

        // the listing reads the files only: Standard pairs are listed without a vault
        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        _fixture.AddSet("bad", "{}", ("key-values.json", """{ "Items": [ { "Key": "a", "Protection": "Byok", "Value": "1" } ] }"""));

        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
    }

    [Fact]
    public async Task Sets_ShowsTheProjectSettingsAndRefusesThoseThatBreakARule()
    {
        _fixture.AddSet(
            "styled",
            """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""",
            ("project-settings.json", """{ "Format": "2s.project-settings.v1", "Types": { "sbt": { "Label": "Customer" } }, "DefaultTimeZone": "Europe/Oslo" }"""));

        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        _fixture.AddSet("broken", "{}", ("project-settings.json", """{ "DefaultTimeZone": "Central Europe Standard Time" }"""));

        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
    }

    [Fact]
    public async Task Sets_WithoutAnySet_Succeeds()
    {
        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
    }

    [Fact]
    public async Task Sets_ACatalogThatCannotBeRead_ExitsWithTwo()
    {
        _fixture.AddSet("base", """{ "Unknown": 1 }""");

        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
    }

    [Fact]
    public async Task Sets_AFolderWithoutSets_ExitsWithTwo()
    {
        _fixture.FileSystem.AddDirectory(SeedFixture.Tool + "-empty");

        (await Command(SeedFixture.Tool + "-empty").OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
    }

    private SetsCommand Command(params string[] paths)
    {
        return new SetsCommand(_fixture.FileSystem, _fixture.Catalog) { Paths = paths };
    }
}
