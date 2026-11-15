using _42.Platform.Storyteller.Accessing;
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
    public async Task Seed_Stopped_ExitsWithAbortedAndRecordsNothing()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"));
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        (await Command(sets: ["base"], organization: "fabrikam", project: "invoicing-suite").OnExecuteAsync(stopped.Token)).ShouldBe(ExitCodes.WARNING_ABORTED);
        _fixture.Records.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Seed_WrongInput_ExitsWithTwoBeforeTheDatabaseIsTouched()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"));
        _fixture.AddSet("cycle", """{ "DependsOn": [ "cycle-two" ] }""");
        _fixture.AddSet("cycle-two", """{ "DependsOn": [ "cycle" ] }""");
        _fixture.AddSet("styled", Manifest, ("project-settings.json", """{ "Types": { "sbt": { "Color": "pinkish" } } }"""));
        _fixture.AddSet("other-styled", Manifest, ("project-settings.json", """{ "DateFormat": "dd.mm.yyyy" }"""));
        _fixture.AddSet("restyled", Manifest, ("project-settings.json", """{ "DateFormat": "mm/dd/yyyy" }"""));

        (await Command(sets: ["unknown"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["cycle"], organization: "fabrikam", project: "invoicing-suite").OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["base"], project: "Bad Name").OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["base"], owner: "not:an-entra-id").OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: [], paths: [SeedFixture.Tool + "-missing"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["styled"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["other-styled", "restyled"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _fixture.Containers.Verify(factory => factory.FindLayoutProblemsAsync(), Times.Never);
        _fixture.Access.Invocations.ShouldBeEmpty();
        _fixture.ProjectSettings.Writes.ShouldBeEmpty();
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

        _fixture.Access.Verify(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>(), It.IsAny<AccessCaller>()), Times.Never);
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

    [Theory]
    [InlineData("""{ "Key": "smtp.password", "Protection": "Standard", "Value": "demo" }""")]
    [InlineData("""{ "Key": "api.token", "Protection": "Hsm", "Value": "x" }""")]
    [InlineData("""{ "Key": "db.password", "Protection": "Standard" }""")]
    [InlineData("""{ "Key": "bad-key", "Protection": "Public", "Value": "x" }""")]
    public async Task Seed_PairsASeedCannotWrite_ExitWithTwoBeforeTheDatabaseIsTouched(string item)
    {
        // no key-encryption provider, so Standard is refused as Hsm is
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"), ("key-values.json", $$"""{ "Items": [ {{item}} ] }"""));

        (await Command(sets: ["base"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await Command(sets: ["base"], dryRun: true).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _fixture.Containers.Verify(factory => factory.FindLayoutProblemsAsync(), Times.Never);
        _fixture.Access.Invocations.ShouldBeEmpty();
        _fixture.KeyValues.Imports.ShouldBeEmpty();
    }

    [Fact]
    public async Task Seed_PublicPairsOnly_SeedsASetWithStandardPairsWithoutAVault()
    {
        _fixture.AddSet("base", Manifest, ("key-values.json", """{ "Items": [ { "Key": "smtp.host", "Protection": "Public", "Value": "x" }, { "Key": "smtp.password", "Protection": "Standard", "Value": "demo" } ] }"""));

        (await Command(sets: ["base"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        var command = Command(sets: ["base"]);
        command.IsPublicPairsOnly = true;
        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        _fixture.KeyValues.Pairs.Keys.ShouldBe(["smtp.host"]);
        _fixture.Records.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Seed_PublicPairs_SeedWithoutAVault_AndAFailedPairExitsWithFour()
    {
        _fixture.AddSet("base", Manifest, ("key-values.json", """{ "Items": [ { "Key": "smtp.host", "Protection": "Public", "Value": "smtp.example" } ] }"""));

        (await Command(sets: ["base"]).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        _fixture.KeyValues.Pairs.Keys.ShouldBe(["smtp.host"]);

        _fixture.AddSet("other", Manifest, ("key-values.json", """{ "Items": [ { "Key": "smtp.port", "Protection": "Public", "Value": "25" } ] }"""));
        _fixture.KeyValues.FailingWriteKeys.Add("smtp.port");

        (await Command(sets: ["other"]).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_SEED_FAILED);
        _fixture.Records.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Seed_ProjectSettings_AreWrittenOnceThenKeptAndAFailedWriteExitsWithFour()
    {
        _fixture.AddSet(
            "styled",
            Manifest,
            ("project-settings.json", """{ "Format": "2s.project-settings.v1", "Types": { "sbt": { "Label": "Customer" } }, "DefaultTimeZone": "Europe/Oslo" }"""),
            ("annotations/sbt.base.json", "{}"));

        (await Command(sets: ["styled"]).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        _fixture.ProjectSettings.Writes.ShouldHaveSingleItem();

        // other settings are kept, which is no failure, also in a dry run
        _fixture.ProjectSettings.Store("fabrikam", "invoicing-suite", new ProjectSettings { DateFormat = "dd.mm.yyyy" });
        (await Again(Command(sets: ["styled"])).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        (await Again(Command(sets: ["styled"], dryRun: true)).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        _fixture.ProjectSettings.Writes.ShouldHaveSingleItem();

        _fixture.ProjectSettings.Stored.Clear();
        _fixture.ProjectSettings.SetFailure = new ConflictException("The project changed concurrently twice; try again.");
        (await Again(Command(sets: ["styled"])).OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_SEED_FAILED);
        _fixture.Records.Saves.ShouldBe(2);
    }

    [Fact]
    public async Task Seed_ADryRun_WritesNothing()
    {
        _fixture.AddSet("base", Manifest, ("annotations/sbt.base.json", "{}"));

        (await Command(sets: ["base"], dryRun: true).OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        _fixture.Importer.Calls.ShouldHaveSingleItem().Options.DryRun.ShouldBeTrue();
        _fixture.Access.Verify(access => access.CreateAccountAsync(It.IsAny<AccountCreate>(), It.IsAny<AccessCaller>()), Times.Never);
        _fixture.Access.Verify(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>(), It.IsAny<AccessCaller>()), Times.Never);
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
        command.IsPublicPairsOnly = true;

        var request = command.CreateRequest();

        request.Sets.ShouldBe(["large", "core"]);
        request.Paths.ShouldBe(["one", "two"]);
        (request.Organization, request.Project, request.View, request.Owner).ShouldBe(("acme", "billing", "preview", "user_01owner"));
        (request.Options.DryRun, request.Options.Force, request.Options.Again, request.Options.PublicPairsOnly).ShouldBe((true, true, true, true));
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

        // a pair as any other item
        DataSetSeedItem Pair(ViewImportOutcome outcome) => new() { Path = "key-values.json", KeyValueKey = "smtp.host", Protection = KeyValueProtection.Public, Outcome = outcome };
        ExitCodes.Of(Run(new DataSetSeedResult { Set = set, Items = [Pair(ViewImportOutcome.Skipped)] })).ShouldBe(ExitCodes.SUCCESS);
        ExitCodes.Of(Run(new DataSetSeedResult { Set = set, Items = [Pair(ViewImportOutcome.Failed)] })).ShouldBe(ExitCodes.ERROR_SEED_FAILED);
    }

    private static SeedCommand Again(SeedCommand command)
    {
        command.IsAgain = true;
        return command;
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
