using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.DbCreator.Commands;
using _42.Platform.Storyteller.DbCreator.DataSets;
using Moq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.Commands;

// 2sInitDb seed over fakes: the order of the checks and what each outcome exits with.
public class SeedCommandTests
{
    private const string Manifest = """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""";

    private readonly SeedFixture _fixture = new();

    [Fact]
    public async Task Seed_EnsuresTheContainersThenSeedsAndExitsWithSuccess()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"));
        _fixture.AddSet("top", """{ "DependsOn": [ "base" ] }""", ("annotations.jsonl", """{"$count":3,"AnnotationKey":"sbt.g-{n}"}"""));

        var exitCode = await Command(sets: ["top"], organization: "fabrikam", project: "invoicing-suite").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Containers.Verify(factory => factory.EnsureOrganizationsContainerAsync(), Times.Once);
        _fixture.Records.Records.Keys.ShouldBe(["fabrikam|invoicing-suite.meta|seed.default.base", "fabrikam|invoicing-suite.meta|seed.default.top"], ignoreOrder: true);
        _fixture.BulkWriter.Calls.ShouldHaveSingleItem().Options.Progress.ShouldBeSameAs(DataSetOutput.AnnotationProgress);

        // seeded already is a success too
        (await Command(sets: ["top"], organization: "fabrikam", project: "invoicing-suite").OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        _fixture.Records.Saves.ShouldBe(2);
    }

    [Fact]
    public async Task Seed_WrongInput_ExitsWithTwoBeforeTheDatabaseIsTouched()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"));
        _fixture.AddSet("cycle", """{ "DependsOn": [ "cycle-two" ] }""");
        _fixture.AddSet("cycle-two", """{ "DependsOn": [ "cycle" ] }""");

        (await Command(sets: ["unknown"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["cycle"], organization: "fabrikam", project: "invoicing-suite").OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["base"], project: "Bad Name").OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["base"], owner: "not:an-entra-id").OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: [], paths: [SeedFixture.Tool + "-missing"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _fixture.Containers.Verify(factory => factory.FindLayoutProblemsAsync(), Times.Never);
        _fixture.Access.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Seed_AnOrganizationOfAnotherOwner_ExitsWithTwoAndWritesNothing()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"));
        _fixture.Access.Setup(access => access.GetAccessPointAsync("fabrikam")).ReturnsAsync(new AccessPoint
        {
            Key = "fabrikam",
            AccessMap = new Dictionary<string, AccountRole> { ["someone-else"] = AccountRole.Owner },
        });

        (await Command(sets: ["base"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _fixture.Access.Verify(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()), Times.Never);
        _fixture.Importer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Seed_ADatabaseOfTheOldLayout_ExitsWithThreeBeforeAnythingIsSeeded()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"));
        _fixture.LayoutProblems.Add("The container 'org.fabrikam' is of the layout before the shared container.");

        (await Command(sets: ["base"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_DATABASE_LAYOUT);

        _fixture.Containers.Verify(factory => factory.EnsureOrganizationsContainerAsync(), Times.Never);
        _fixture.Access.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Seed_AFailedDocument_ExitsWithFourAndLeavesTheSetUnrecorded()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"), ("annotations/sbt.bad.json", "{}"));
        _fixture.Importer.FailingKeys.Add("sbt.bad");

        (await Command(sets: ["base"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_SEED_FAILED);
        (await Command(sets: ["base"], dryRun: true).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_SEED_FAILED);

        _fixture.Records.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Seed_ADryRun_WritesNothing()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"));

        (await Command(sets: ["base"], dryRun: true).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        _fixture.Importer.Calls.ShouldHaveSingleItem().Options.DryRun.ShouldBeTrue();
        _fixture.Access.Verify(access => access.CreateAccountAsync(It.IsAny<AccountCreate>()), Times.Never);
        _fixture.Access.Verify(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()), Times.Never);
        _fixture.Records.Saves.ShouldBe(0);
    }

    [Fact]
    public void CreateRequest_MapsTheArgumentsAndOptions()
    {
        var command = Command(
            sets: ["large", "core"],
            paths: ["one", "two"],
            organization: "acme",
            project: "billing",
            view: "preview",
            owner: "user_01owner",
            dryRun: true);
        command.IsForced = true;
        command.IsAgain = true;

        var request = command.CreateRequest();

        request.Sets.ShouldBe(["large", "core"]);
        request.Paths.ShouldBe(["one", "two"]);
        (request.Organization, request.Project, request.View, request.Owner).ShouldBe(("acme", "billing", "preview", "user_01owner"));
        (request.Options.DryRun, request.Options.Force, request.Options.Again).ShouldBe((true, true, true));
        request.Options.AnnotationProgress.ShouldBeNull();

        // nothing given: the sets of the folders, the targets of the manifests, the base account
        var empty = new SeedCommand(_fixture.Structure(), _fixture.Seeder()).CreateRequest();
        empty.ShouldBeEquivalentTo(new DataSetSeedRequest());
    }

    [Fact]
    public void ExitCodes_OfARun_FailOnFailuresOnly()
    {
        var set = new DataSet(new DataSetManifest { Name = "a" }, "a", DataSetSource.BuiltIn);
        var target = new DataSetTarget("fabrikam", "invoicing-suite", "default");
        var owner = new DataSetOwner("id", "id", "id");
        var provisioning = new DataSetProvisioning(false, false, false, false);
        DataSetSeedItem Item(ViewImportOutcome outcome) => new() { Path = "annotations/sbt.a.json", Kind = ViewDocumentKind.Annotation, Outcome = outcome };
        DataSetRunResult Run(params DataSetSeedResult[] sets) => new(target, owner, provisioning, sets, false);

        ExitCodes.Of(Run()).ShouldBe(ExitCodes.SUCCESS);
        ExitCodes.Of(Run(new DataSetSeedResult { Set = set, IsAlreadySeeded = true })).ShouldBe(ExitCodes.SUCCESS);
        ExitCodes.Of(Run(new DataSetSeedResult { Set = set, Items = [Item(ViewImportOutcome.Skipped), Item(ViewImportOutcome.Updated)] })).ShouldBe(ExitCodes.SUCCESS);
        ExitCodes.Of(Run(new DataSetSeedResult { Set = set, Items = [Item(ViewImportOutcome.Failed)] })).ShouldBe(ExitCodes.ERROR_SEED_FAILED);
        ExitCodes.Of(Run(new DataSetSeedResult { Set = set, FailedDependency = "b" })).ShouldBe(ExitCodes.ERROR_SEED_FAILED);
    }

    private SeedCommand Command(
        string[]? sets = null,
        string[]? paths = null,
        string? organization = null,
        string? project = null,
        string? view = null,
        string? owner = null,
        bool dryRun = false)
    {
        _fixture.Importer.Calls.Clear();
        _fixture.BulkWriter.Calls.Clear();

        return new SeedCommand(_fixture.Structure(), _fixture.Seeder())
        {
            Sets = sets,
            Paths = paths,
            Organization = organization,
            Project = project,
            View = view,
            Owner = owner,
            IsDryRun = dryRun,
        };
    }
}
