using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// The seeder over fakes of the import, the bulk writer, the record store and the access model: the order of the steps,
// what they share, and what is written when.
public class DataSetSeederTests
{
    private const string Owner = "user_01owner";
    private const string Organization = "fabrikam";
    private const string Project = "invoicing-suite";

    private static readonly string Tool = MockUnixSupport.Path(@"C:\tool");

    private readonly MockFileSystem _fileSystem = new();
    private readonly Mock<IAccessService> _access = new();
    private readonly Mock<IViewService> _views = new();
    private readonly FakeViewImporter _importer = new();
    private readonly FakeBulkWriter _bulkWriter = new();
    private readonly FakeRecordStore _records = new();
    private readonly List<string> _writes = [];

    public DataSetSeederTests()
    {
        _access.Setup(access => access.CreateAccountAsync(It.IsAny<AccountCreate>()))
            .Callback<AccountCreate>(model => _writes.Add($"account {model.IdentityId} {model.UserName}"))
            .ReturnsAsync((AccountCreate model) => new Account { Id = model.IdentityId, UserName = model.UserName, Name = model.Name, AccessMap = new Dictionary<string, AccountRole>() });
        _access.Setup(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()))
            .Callback<AccessPointCreate>(model => _writes.Add($"project {model.Organization}.{model.Project} {model.OwnerId} {model.ProjectDescription}"))
            .ReturnsAsync((AccessPointCreate model) => new AccessPoint { Key = $"{model.Organization}.{model.Project}", AccessMap = new Dictionary<string, AccountRole>() });
        _views.Setup(views => views.GetViewsAsync(Organization, Project, false))
            .ReturnsAsync([new View { Name = "default", IsDefault = true }]);
        _views.Setup(views => views.CreateViewAsync(Organization, Project, It.IsAny<ViewCreate>(), AuditActor.System))
            .Callback<string, string, ViewCreate, AuditActor>((_, _, model, _) => _writes.Add($"view {model.Name} {model.Description}"))
            .ReturnsAsync((string _, string _, ViewCreate model, AuditActor _) => new View { Name = model.Name, IsRegistered = true });
    }

    [Fact]
    public async Task Seed_CreatesTheTargetThenRunsTheStepsWithOneOverlay()
    {
        AddShowcase();
        _importer.FailingKeys.Add("sbt.bad");

        var run = await Seeder().SeedAsync(new DataSetSeedRequest { Sets = ["showcase"], Owner = Owner }, TestContext.Current.CancellationToken);

        // the account first, so the project's membership is mirrored into it; the view after the project
        _writes.ShouldBe(
        [
            $"account {Owner} {Owner}",
            $"project {Organization}.{Project} {Owner} The invoices.",
            "view preview A preview.",
        ]);
        run.Target.ShouldBe(new DataSetTarget(Organization, Project, "preview"));
        run.Provisioning.ShouldBe(new DataSetProvisioning(true, true, true, true));

        // the annotation files, then annotations.jsonl in bulk, then the other documents with the same overlay
        _importer.Calls.Count.ShouldBe(2);
        _importer.Calls[0].Files.Select(file => file.Path).ShouldBe(["annotations/sbt.a.json", "annotations/sbt.bad.json"]);
        _importer.Calls[1].Files.Select(file => file.Path).ShouldBe(["configurations/cnt.bad.x.conf.json", "configurations/sbt.a.conf.json"]);
        _importer.Calls[1].Overlay.ShouldBeSameAs(_importer.Calls[0].Overlay);
        _importer.Calls.ShouldAllBe(call => call.Options.Actor == AuditActor.System && call.Options.Existing == ViewImportExisting.Skip && !call.Options.DryRun);
        _importer.Calls[0].View.ShouldBe("preview");

        // what hangs on the failed file is not sent, and fails what hangs on it in turn
        var sent = _bulkWriter.Calls.ShouldHaveSingleItem();
        sent.Annotations.Select(annotation => annotation.AnnotationKey).ShouldBe(["cnt.a.x", "sbt.g-1", "sbt.g-2"]);
        sent.Annotations.ShouldAllBe(annotation => annotation.ProjectName == Project && annotation.ViewName == "preview");
        sent.Options.DryRun.ShouldBeFalse();
        _importer.Calls[1].FailedAtStart.ShouldBe(["sbt.bad", "cnt.bad.x", "exe.bad.r.x"], ignoreOrder: true);

        var set = run.Sets.ShouldHaveSingleItem();
        set.Items.Select(item => (item.Path, item.Line, item.AnnotationKey, item.Outcome, item.ErrorCode)).ShouldBe(
        [
            ("annotations/sbt.bad.json", null, "sbt.bad", ViewImportOutcome.Failed, ErrorCodes.InvalidAnnotation),
            ("annotations.jsonl", 2, "cnt.bad.x", ViewImportOutcome.Skipped, ErrorCodes.DependencyFailed),
            ("annotations.jsonl", 3, "exe.bad.r.x", ViewImportOutcome.Skipped, ErrorCodes.DependencyFailed),
            ("annotations.jsonl", 5, "sbt.a", ViewImportOutcome.Failed, ErrorCodes.DuplicateFile),
        ]);
        set.Items[1].Message.ShouldBe("The ancestor sbt.bad failed in this import.");
        set.Items[2].Message.ShouldBe("The ancestors sbt.bad, cnt.bad.x failed in this import.");
        set.Documents!.Summary.Created.ShouldBe(3);
        set.Documents.Summary.Failed.ShouldBe(1);
        var lines = set.Lines.ShouldNotBeNull();
        (lines.Lines, lines.GeneratorLines, lines.Annotations, lines.RelationUpdates).ShouldBe((5, 1, 6, 2));
        lines.AncestorsCreated.ShouldBe(["rst.made-on-the-way"]);
        lines.Summary.ShouldBeEquivalentTo(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 3, Skipped = 2, Failed = 1 });

        // a failure leaves the set without its record, so the next run seeds it again
        set.HasFailures.ShouldBeTrue();
        set.IsRecorded.ShouldBeFalse();
        _records.Saves.ShouldBe(0);
        run.HasFailures.ShouldBeTrue();
    }

    [Fact]
    public async Task Seed_RecordsASetSeededWithoutFailuresAndThenSkipsIt()
    {
        AddShowcase(withFailures: false);
        var request = new DataSetSeedRequest { Sets = ["showcase"], Owner = Owner };

        var first = (await Seeder().SeedAsync(request, TestContext.Current.CancellationToken)).Sets.Single();

        first.IsRecorded.ShouldBeTrue();
        var record = _records.Records.Values.ShouldHaveSingleItem();
        record.Id.ShouldBe("seed.preview.showcase");
        record.PartitionKey.ShouldBe($"{Project}.meta");
        record.Organization.ShouldBe(Organization);
        record.Version.ShouldBe(2);
        record.Hash.ShouldBe(first.Hash);
        record.Seeded.By.ShouldBe(AuditActor.System);
        (record.Documents, record.Annotations, record.Created).ShouldBe((2, 3, 5));

        var second = (await Seeder().SeedAsync(request, TestContext.Current.CancellationToken)).Sets.Single();

        second.IsAlreadySeeded.ShouldBeTrue();
        second.Documents.ShouldBeNull();
        _importer.Calls.ShouldBeEmpty();
        _bulkWriter.Calls.ShouldBeEmpty();

        // a changed file, or Again, seeds it again
        _fileSystem.AddFile(Path.Combine(Folder("showcase"), "annotations", "sbt.c.json"), new MockFileData("{}"));
        (await Seeder().SeedAsync(request, TestContext.Current.CancellationToken)).Sets.Single().IsSeeded.ShouldBeTrue();
        (await Seeder().SeedAsync(request with { Options = new DataSetSeedOptions { Again = true } }, TestContext.Current.CancellationToken))
            .Sets.Single().IsRecorded.ShouldBeTrue();
        _records.Saves.ShouldBe(3);
    }

    [Fact]
    public async Task Seed_DryRunWritesNothingAndTellsTheLaterStepsWhatTheBulkWouldCreate()
    {
        AddShowcase(withFailures: false);

        var run = await Seeder().SeedAsync(
            new DataSetSeedRequest { Sets = ["showcase"], Owner = Owner, Options = new DataSetSeedOptions { DryRun = true, Force = true } },
            TestContext.Current.CancellationToken);

        run.IsDryRun.ShouldBeTrue();
        run.Provisioning.ShouldBe(new DataSetProvisioning(true, true, true, true));
        _writes.ShouldBeEmpty();
        _records.Saves.ShouldBe(0);
        run.Sets.Single().IsRecorded.ShouldBeFalse();

        _bulkWriter.Calls.Single().Options.DryRun.ShouldBeTrue();
        _importer.Calls.ShouldAllBe(call => call.Options.DryRun && call.Options.Force);
        _importer.Calls[1].ActiveAtStart.ShouldBe(["sbt.a", "cnt.a.x", "sbt.g-1", "sbt.g-2", "rst.made-on-the-way"], ignoreOrder: true);
    }

    [Fact]
    public async Task Seed_WritesTheLinesAboveAnAnnotationFileBeforeTheFile()
    {
        AddMixed();
        _bulkWriter.AncestorsCreated = [];
        var progress = new RecordingProgress();

        var run = await Seeder().SeedAsync(
            new DataSetSeedRequest { Sets = ["mixed"], Owner = Owner, Options = new DataSetSeedOptions { AnnotationProgress = progress } },
            TestContext.Current.CancellationToken);

        // round 0: the file without a line above it, then the lines it does not hang on; round 1: the file below the
        // line rst.payments, then the line below that file; then the other documents
        _importer.Calls.Select(call => call.Files.Select(file => file.Path).ToList()).ShouldBe(
        [
            ["annotations/sbt.other.json"],
            ["annotations/unt.payments.ledger.json"],
            ["configurations/rst.payments.conf.json"],
        ]);
        _bulkWriter.Calls.Select(call => call.Annotations.Select(annotation => annotation.AnnotationKey).ToList()).ShouldBe(
        [
            ["rst.payments", "sbt.acme"],
            ["uxe.acme.payments.prod.ledger"],
        ]);
        _importer.Calls.ShouldAllBe(call => call.Overlay == _importer.Calls[0].Overlay);

        // the progress counts on over the rounds
        progress.Values.ShouldBe([2, 3]);

        var set = run.Sets.Single();
        set.Items.ShouldBeEmpty();
        var lines = set.Lines.ShouldNotBeNull();
        lines.Summary.Created.ShouldBe(3);
        lines.RelationUpdates.ShouldBe(4);
        set.IsRecorded.ShouldBeTrue();
    }

    [Fact]
    public async Task Seed_DryRunGivesEachStepWhatTheStepsBeforeWouldMakeActive()
    {
        AddMixed();

        await Seeder().SeedAsync(
            new DataSetSeedRequest { Sets = ["mixed"], Owner = Owner, Options = new DataSetSeedOptions { DryRun = true } },
            TestContext.Current.CancellationToken);

        // the bulk writer gets the keys the files would create, and the files the keys the bulk writer would create
        _bulkWriter.Calls.Count.ShouldBe(2);
        _bulkWriter.Calls[0].DryRunActiveAtStart.ShouldNotBeNull().ShouldBe(["sbt.other"]);
        _bulkWriter.Calls[1].DryRunActiveAtStart.ShouldNotBeNull().ShouldBe(["sbt.other", "rst.payments", "sbt.acme", "rst.made-on-the-way", "unt.payments.ledger"], ignoreOrder: true);
        _importer.Calls[1].ActiveAtStart.ShouldBe(["sbt.other", "rst.payments", "sbt.acme", "rst.made-on-the-way"], ignoreOrder: true);
        _records.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Seed_ALineThatCannotBeReadFailsTheFilesBelowIt()
    {
        AddSet(
            "broken",
            """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""",
            ("annotations.jsonl", """{"AnnotationKey":"rst.payments","Colour":"red"}"""),
            ("annotations/unt.payments.ledger.json", "{}"));

        var run = await Seeder().SeedAsync(new DataSetSeedRequest { Sets = ["broken"], Owner = Owner }, TestContext.Current.CancellationToken);

        // the file is imported with the line's key failed, so the importer skips it instead of creating the line's
        // annotation with defaults
        _importer.Calls.ShouldHaveSingleItem().FailedAtStart.ShouldBe(["rst.payments"]);
        _bulkWriter.Calls.ShouldBeEmpty();
        run.Sets.Single().Failures.ShouldHaveSingleItem().Line.ShouldBe(1);
        _records.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Seed_DryRunCarriesWhatTheSetsBeforeWouldWriteButNotTheirFailures()
    {
        AddSet(
            "base",
            """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""",
            ("annotations/rst.x.json", "{}"),
            ("annotations/sbt.bad.json", "{}"),
            ("configurations/rst.x.conf.json", """{ "a": 1 }"""));
        AddSet(
            "side",
            """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""",
            ("annotations.jsonl", """{"AnnotationKey":"unt.x.u"}"""),
            ("annotations/sbt.y.json", "{}"));
        _importer.FailingKeys.Add("sbt.bad");
        var request = new DataSetSeedRequest { Sets = ["base", "side"], Owner = Owner, Options = new DataSetSeedOptions { DryRun = true } };

        var run = await Seeder().SeedAsync(request, TestContext.Current.CancellationToken);

        run.Sets.Select(set => set.IsSeeded).ShouldBe([true, true]);
        var side = _importer.Calls[2];
        side.Files.Single().Path.ShouldBe("annotations/sbt.y.json");
        side.Overlay.ShouldNotBeSameAs(_importer.Calls[0].Overlay);
        side.ActiveAtStart.ShouldBe(["rst.x"]);
        side.ConfigurationsAtStart.ShouldBe(["rst.x"]);
        side.FailedAtStart.ShouldBeEmpty();
        _bulkWriter.Calls.Single().DryRunActiveAtStart.ShouldNotBeNull().ShouldBe(["rst.x", "sbt.y"], ignoreOrder: true);

        // a real run reads what the sets before wrote
        await Seeder().SeedAsync(request with { Options = new DataSetSeedOptions() }, TestContext.Current.CancellationToken);

        _importer.Calls[2].ActiveAtStart.ShouldBeEmpty();
        _bulkWriter.Calls.Single().DryRunActiveAtStart.ShouldBeNull();
    }

    [Fact]
    public async Task Seed_RefusesASetBeyondTheLimitsBeforeAnythingIsWritten()
    {
        AddSet("first", """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""", ("annotations/sbt.first.json", "{}"));
        AddSet(
            "big",
            """{ "Organization": "fabrikam", "Project": "invoicing-suite", "DependsOn": [ "first" ] }""",
            ("annotations.jsonl", """{"$count":200001,"AnnotationKey":"sbt.g-{n}"}"""));

        var exception = await Should.ThrowAsync<DataSetException>(
            () => Seeder().SeedAsync(new DataSetSeedRequest { Sets = ["big"], Owner = Owner }, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("more than 200,000 annotations");
        _access.Invocations.ShouldBeEmpty();
        _importer.Calls.ShouldBeEmpty();
        _records.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Seed_UsesAnExistingProjectAsItIs()
    {
        AddShowcase(withFailures: false);
        _access.Setup(access => access.GetAccessPointAsync(Organization)).ReturnsAsync(Point(Organization, "someone-else"));
        _access.Setup(access => access.GetAccessPointAsync($"{Organization}.{Project}")).ReturnsAsync(Point($"{Organization}.{Project}", "someone-else"));
        _access.Setup(access => access.GetAccountAsync(Owner)).ReturnsAsync(new Account { Id = Owner, UserName = "u", Name = "n", AccessMap = new Dictionary<string, AccountRole>() });
        _views.Setup(views => views.GetViewsAsync(Organization, Project, false))
            .ReturnsAsync([new View { Name = "default", IsDefault = true }, new View { Name = "preview", IsRegistered = true }]);

        var run = await Seeder().SeedAsync(new DataSetSeedRequest { Sets = ["showcase"], Owner = Owner }, TestContext.Current.CancellationToken);

        run.Provisioning.ShouldBe(new DataSetProvisioning(false, false, false, false));
        _writes.ShouldBeEmpty();
        run.Sets.Single().IsRecorded.ShouldBeTrue();
    }

    [Fact]
    public async Task Seed_RefusesAProjectInAnOrganizationTheOwnerDoesNotOwn()
    {
        AddShowcase(withFailures: false);
        _access.Setup(access => access.GetAccessPointAsync(Organization)).ReturnsAsync(Point(Organization, "someone-else", AccountRole.Administrator, Owner));

        var exception = await Should.ThrowAsync<DataSetException>(
            () => Seeder().SeedAsync(new DataSetSeedRequest { Sets = ["showcase"], Owner = Owner }, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain($"the owner '{Owner}' is not its Owner");
        _writes.ShouldBeEmpty();
        _importer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Seed_RefusesABadNameBeforeAnythingIsWritten()
    {
        AddShowcase(withFailures: false);

        await Should.ThrowAsync<DataSetException>(
            () => Seeder().SeedAsync(new DataSetSeedRequest { Sets = ["showcase"], Owner = Owner, View = "views" }, TestContext.Current.CancellationToken));

        _writes.ShouldBeEmpty();
        _access.Verify(access => access.GetAccountAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Seed_SeedsTheDependenciesFirstAndNotWhatDependsOnAFailedSet()
    {
        AddSet("base", """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""", ("annotations/sbt.bad.json", "{}"));
        AddSet("top", """{ "Project": "invoicing-suite", "DependsOn": [ "base" ] }""", ("annotations/sbt.top.json", "{}"));
        _importer.FailingKeys.Add("sbt.bad");

        var run = await Seeder().SeedAsync(new DataSetSeedRequest { Sets = ["top"], Organization = "contoso" }, TestContext.Current.CancellationToken);

        run.Target.ShouldBe(new DataSetTarget("contoso", Project, "default"));
        run.Owner.Id.ShouldBe(Constants.LEGACY_BASE_ACCOUNT_ID);
        run.Sets.Select(set => (set.Set.Name, set.IsSeeded, set.FailedDependency)).ShouldBe([("base", true, null), ("top", false, "base")]);
        run.Sets[1].HasFailures.ShouldBeTrue();
        _importer.Calls.ShouldHaveSingleItem().Organization.ShouldBe("contoso");
    }

    [Fact]
    public async Task Seed_TakesTheSetsOfTheFoldersWithoutNames()
    {
        var folder = SetFiles.Folder("own");
        SetFiles.Add(_fileSystem, folder, ("set.json", """{ "Name": "own", "Organization": "fabrikam", "Project": "invoicing-suite" }"""), ("annotations/sbt.own.json", "{}"));
        AddSet("built-in", """{ "Organization": "other", "Project": "other" }""", ("annotations/sbt.b.json", "{}"));

        var run = await Seeder().SeedAsync(new DataSetSeedRequest { Paths = [folder] }, TestContext.Current.CancellationToken);

        run.Sets.ShouldHaveSingleItem().Set.Name.ShouldBe("own");
        await Should.ThrowAsync<DataSetException>(() => Seeder().SeedAsync(new DataSetSeedRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Plan_DecidesFromTheFilesAndTheOptionsAlone()
    {
        AddSet("base", """{ "Organization": "fabrikam", "Project": "invoicing-suite", "ProjectDescription": "The base.", "ViewDescription": "A base view." }""");
        AddShowcase(withFailures: false);
        AddSet("top", """{ "DependsOn": [ "base" ] }""");

        var plan = Seeder().Plan(new DataSetSeedRequest { Sets = ["top", "showcase"], Owner = Owner });

        plan.Sets.Select(set => set.Name).ShouldBe(["base", "top", "showcase"]);
        plan.Named.Select(set => set.Name).ShouldBe(["top", "showcase"]);
        plan.Target.ShouldBe(new DataSetTarget(Organization, Project, "preview"));
        plan.Owner.Id.ShouldBe(Owner);

        // a named set's descriptions before those of a dependency
        (plan.ProjectDescription, plan.ViewDescription).ShouldBe(("The invoices.", "A preview."));

        // the project, the view and a dotted organization are checked here; another organization once the provisioning
        // knows it is new
        Should.Throw<DataSetException>(() => Seeder().Plan(new DataSetSeedRequest { Sets = ["base"], Project = "Not Valid" }));
        Should.Throw<DataSetException>(() => Seeder().Plan(new DataSetSeedRequest { Sets = ["base"], View = "views" }));
        Should.Throw<DataSetException>(() => Seeder().Plan(new DataSetSeedRequest { Sets = ["base"], Organization = "fabrikam.invoicing-suite" }));
        Seeder().Plan(new DataSetSeedRequest { Sets = ["base"], Organization = "Existing Org" }).Target.Organization.ShouldBe("Existing Org");
        _access.Invocations.ShouldBeEmpty();
        _views.Invocations.ShouldBeEmpty();
    }

    private static string Folder(string name) => Path.Combine(Tool, "data-sets", name);

    private static AccessPoint Point(string key, string ownerId, AccountRole otherRole = AccountRole.None, string? other = null)
    {
        var map = new Dictionary<string, AccountRole> { [ownerId] = AccountRole.Owner };

        if (other is not null)
        {
            map[other] = otherRole;
        }

        return new AccessPoint { Key = key, AccessMap = map };
    }

    private DataSetSeeder Seeder()
    {
        _importer.Calls.Clear();
        _bulkWriter.Calls.Clear();

        return new DataSetSeeder(
            _fileSystem,
            new DataSetCatalog(_fileSystem, Tool),
            new DataSetProvisioner(_access.Object, _views.Object, NullLogger<DataSetProvisioner>.Instance),
            _importer,
            _bulkWriter,
            _records,
            Options.Create(new BaseAccountOptions()),
            TimeProvider.System,
            NullLogger<DataSetSeeder>.Instance);
    }

    private void AddSet(string name, string manifest, params (string Path, string Content)[] files)
    {
        SetFiles.Add(_fileSystem, Folder(name), [("set.json", manifest), .. files]);
    }

    // the lines rst.payments and sbt.acme above the file unt.payments.ledger, a line below that file, a file without a
    // line above it, and a configuration
    private void AddMixed()
    {
        AddSet(
            "mixed",
            """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""",
            ("annotations.jsonl", """
                                  {"AnnotationKey":"uxe.acme.payments.prod.ledger"}
                                  {"AnnotationKey":"rst.payments","Title":"Payments"}
                                  {"AnnotationKey":"sbt.acme"}
                                  """),
            ("annotations/unt.payments.ledger.json", "{}"),
            ("annotations/sbt.other.json", "{}"),
            ("configurations/rst.payments.conf.json", """{ "a": 1 }"""));
    }

    // sbt.bad fails in its file when the test says so; below it a line of annotations.jsonl and one below that
    private void AddShowcase(bool withFailures = true)
    {
        var lines = withFailures
            ? """
              {"AnnotationKey":"cnt.a.x"}
              {"AnnotationKey":"cnt.bad.x"}
              {"AnnotationKey":"exe.bad.r.x"}
              {"$count":2,"AnnotationKey":"sbt.g-{n}"}
              {"AnnotationKey":"sbt.a"}
              """
            : """
              {"AnnotationKey":"cnt.a.x"}
              {"$count":2,"AnnotationKey":"sbt.g-{n}"}
              """;

        AddSet(
            "showcase",
            """
            {
              "Version": 2,
              "Organization": "fabrikam",
              "Project": "invoicing-suite",
              "View": "preview",
              "ProjectDescription": "The invoices.",
              "ViewDescription": "A preview."
            }
            """,
            [
                ("annotations.jsonl", lines),
                ("annotations/sbt.a.json", "{}"),
                .. withFailures ? [("annotations/sbt.bad.json", "{}"), ("configurations/cnt.bad.x.conf.json", """{ "a": 1 }""")] : Array.Empty<(string, string)>(),
                ("configurations/sbt.a.conf.json", """{ "a": 1 }"""),
            ]);
    }

    private sealed class RecordingProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value)
        {
            Values.Add(value);
        }
    }
}
