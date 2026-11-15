using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Protection;
using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// The pairs of key-values.json in a seed, over the fakes of the seeder tests and an in-memory key/value service: where
// they go among the steps, what a seed writes of them (only the missing ones), and what is refused before any write.
public class DataSetSeederKeyValuesTests
{
    private const string Owner = "user_01owner";
    private const string Organization = "fabrikam";
    private const string Project = "invoicing-suite";
    private const string Manifest = """{ "Organization": "fabrikam", "Project": "invoicing-suite" }""";

    private static readonly string Tool = MockUnixSupport.Path(@"C:\tool");

    private readonly MockFileSystem _fileSystem = new();
    private readonly Mock<IAccessService> _access = new();
    private readonly Mock<IViewService> _views = new();
    private readonly FakeViewImporter _importer = new();
    private readonly FakeBulkWriter _bulkWriter = new();
    private readonly FakeKeyValueService _keyValues = new();
    private readonly FakeRecordStore _records = new();
    private readonly List<IKeyEncryptionProvider> _providers = [];

    public DataSetSeederKeyValuesTests()
    {
        _access.Setup(access => access.CreateAccountAsync(It.IsAny<AccountCreate>()))
            .ReturnsAsync((AccountCreate model) => new Account { Id = model.IdentityId, UserName = model.UserName, Name = model.Name, AccessMap = new Dictionary<string, AccountRole>() });
        _access.Setup(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()))
            .ReturnsAsync((AccessPointCreate model) => new AccessPoint { Key = $"{model.Organization}.{model.Project}", AccessMap = new Dictionary<string, AccountRole>() });
        _views.Setup(views => views.GetViewsAsync(Organization, Project, false))
            .ReturnsAsync([new View { Name = "default", IsDefault = true }]);
    }

    private static DataSetSeedRequest Request => new() { Sets = ["pairs"], Owner = Owner };

    [Fact]
    public async Task Seed_WritesThePairsAfterTheAnnotationsAndBeforeTheConfigurations()
    {
        AddSet(
            Pairs(("smtp.host", "Public", "smtp.fabrikam.example"), ("smtp.port", "Public", "587")),
            ("annotations/rst.invoicing.json", "{}"),
            ("configurations/rst.invoicing.conf.json", """{ "smtp": "@smtp.host" }"""));
        var (importsBefore, completionsBefore) = (-1, -1);
        _keyValues.OnImport = () => (importsBefore, completionsBefore) = (_importer.Calls.Count, _bulkWriter.Completions.Count);

        var run = await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken);

        // the annotation file and the completion of its relation arrays before the pairs, the configuration after them
        (importsBefore, completionsBefore).ShouldBe((1, 1));
        _importer.Calls.Count.ShouldBe(2);
        _importer.Calls[1].Files.Single().Path.ShouldBe("configurations/rst.invoicing.conf.json");

        // a plan, then the write, both a merge into the target as the System caller
        _keyValues.Imports.Select(call => (call.Options.DryRun, string.Join(",", call.Keys))).ShouldBe([(true, "smtp.host,smtp.port"), (false, "smtp.host,smtp.port")]);
        _keyValues.Imports.ShouldAllBe(call => call.Organization == Organization && call.Project == Project && call.View == "default"
            && call.Options.Mode == KeyValueImportMode.Merge && call.Caller.Actor == AuditActor.System && call.Caller.Channel == KeyValueChannel.System
            && call.Caller.CanWriteSecrets);
        _keyValues.Pairs.Keys.ShouldBe(["smtp.host", "smtp.port"], ignoreOrder: true);

        var set = run.Sets.Single();
        var pairs = set.KeyValues.ShouldNotBeNull();
        pairs.Pairs.ShouldBe(2);
        pairs.Levels.ShouldBe([(KeyValueProtection.Public, 2)]);
        pairs.Summary.ShouldBe(new ViewImportSummary { Created = 2 });
        set.Items.ShouldBeEmpty();
        set.IsRecorded.ShouldBeTrue();

        // the record counts the pairs with the documents
        var record = _records.Records.Values.Single();
        (record.Documents, record.Annotations, record.KeyValues, record.Created).ShouldBe((2, 0, 2, 4));
    }

    [Fact]
    public async Task Seed_WritesOnlyTheMissingPairs_AndNeverChangesOrRestoresAnother()
    {
        AddSet(Pairs(
            ("same", "Public", "1"),
            ("changed", "Public", "new"),
            ("level", "Standard", "secret"),
            ("deleted.one", "Public", "1"),
            ("deleted.two", "Public", "2"),
            ("deleted.three", "Public", "3"),
            ("missing", "Public", "4")));
        _providers.Add(StandardProvider());
        _keyValues.Add("same", KeyValueProtection.Public, "1");
        _keyValues.Add("CHANGED", KeyValueProtection.Public, "old");
        _keyValues.Add("level", KeyValueProtection.Public, "secret");
        _keyValues.Add("deleted.one", KeyValueProtection.Public, "1", isDeleted: true);
        _keyValues.Add("deleted.two", KeyValueProtection.Public, "other", isDeleted: true);
        _keyValues.Add("deleted.three", KeyValueProtection.Public, "3", isDeleted: true);

        var run = await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken);

        // the write takes the missing pair alone; the deleted ones were read in pages
        _keyValues.Imports.Select(call => (call.Options.DryRun, call.Keys.Count)).ShouldBe([(true, 7), (false, 1)]);
        _keyValues.Imports[1].Keys.ShouldBe(["missing"]);
        _keyValues.Lists.ShouldBe(2);

        var set = run.Sets.Single();
        set.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = 1, Unchanged = 1, Skipped = 5 });
        set.Items.Select(item => (item.Path, item.Kind, item.KeyValueKey, item.Protection, item.Outcome, item.ErrorCode)).ShouldBe(
        [
            ("key-values.json", null, "changed", KeyValueProtection.Public, ViewImportOutcome.Skipped, DataSetKeyValueSeeder.KeyValueExists),
            ("key-values.json", null, "level", KeyValueProtection.Standard, ViewImportOutcome.Skipped, DataSetKeyValueSeeder.KeyValueExists),
            ("key-values.json", null, "deleted.one", KeyValueProtection.Public, ViewImportOutcome.Skipped, DataSetKeyValueSeeder.KeyValueDeleted),
            ("key-values.json", null, "deleted.two", KeyValueProtection.Public, ViewImportOutcome.Skipped, DataSetKeyValueSeeder.KeyValueDeleted),
            ("key-values.json", null, "deleted.three", KeyValueProtection.Public, ViewImportOutcome.Skipped, DataSetKeyValueSeeder.KeyValueDeleted),
        ]);
        set.Items[0].Message.ShouldBe("The pair exists with another value, level or content type; a seed never changes it.");
        set.Items[2].Message.ShouldBe("The pair is deleted in the view; a seed never restores it.");

        // what is stored stays as it was, and skipped pairs are no failure
        _keyValues.Pairs["changed"].Item.Value.ShouldBe("old");
        _keyValues.Pairs["level"].Item.Protection.ShouldBe(KeyValueProtection.Public);
        _keyValues.Pairs["deleted.one"].IsDeleted.ShouldBeTrue();
        set.HasFailures.ShouldBeFalse();
        set.IsRecorded.ShouldBeTrue();
        _records.Records.Values.Single().Skipped.ShouldBe(5);
    }

    [Fact]
    public async Task Seed_DryRunPlansAndWritesNothing_AndASecondRunWritesNothing()
    {
        AddSet(Pairs(("a", "Public", "1"), ("b", "Public", "2")));
        _keyValues.Add("b", KeyValueProtection.Public, "2");

        var dryRun = await Seeder().SeedAsync(Request with { Options = new DataSetSeedOptions { DryRun = true } }, TestContext.Current.CancellationToken);

        _keyValues.Imports.ShouldHaveSingleItem().Options.DryRun.ShouldBeTrue();
        _keyValues.Pairs.Keys.ShouldBe(["b"]);
        dryRun.Sets.Single().KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = 1, Unchanged = 1 });
        _records.Saves.ShouldBe(0);

        await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken);
        _keyValues.Imports.Clear();
        var lists = _keyValues.Lists;

        // seeded already: not read; seeded again: planned, and nothing to write
        (await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken)).Sets.Single().IsAlreadySeeded.ShouldBeTrue();
        _keyValues.Imports.ShouldBeEmpty();
        var again = await Seeder().SeedAsync(Request with { Options = new DataSetSeedOptions { Again = true } }, TestContext.Current.CancellationToken);
        _keyValues.Imports.ShouldHaveSingleItem().Options.DryRun.ShouldBeTrue();
        again.Sets.Single().KeyValues!.Summary.ShouldBe(new ViewImportSummary { Unchanged = 2 });

        // nothing to create, so the deleted pairs are not listed
        _keyValues.Lists.ShouldBe(lists);
    }

    [Fact]
    public async Task Seed_CutsThePairsIntoTheChunksOfTheImport()
    {
        AddSet(Pairs(Enumerable.Range(1, 2_500).Select(index => ($"key{index:0000}", "Public", $"{index}")).ToArray()));

        var run = await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken);

        _keyValues.Imports.Select(call => (call.Options.DryRun, call.Keys.Count)).ShouldBe(
        [
            (true, 1_000), (false, 1_000),
            (true, 1_000), (false, 1_000),
            (true, 500), (false, 500),
        ]);
        _keyValues.Imports[2].Keys[0].ShouldBe("key1001");
        run.Sets.Single().KeyValues!.Summary.Created.ShouldBe(2_500);
    }

    [Fact]
    public async Task Seed_APairTheImportRefusesFails_AndTheOthersAreWritten()
    {
        AddSet(Pairs(("a", "Public", "1"), ("b", "Standard", "2"), ("c", "Public", "3")));
        _providers.Add(StandardProvider());
        _keyValues.RefusedKeys.Add("b");

        var run = await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken);

        // planned again without the refused pair, then written
        _keyValues.Imports.Select(call => (call.Options.DryRun, string.Join(",", call.Keys))).ShouldBe([(true, "a,b,c"), (true, "a,c"), (false, "a,c")]);
        var set = run.Sets.Single();
        var failure = set.Failures.ShouldHaveSingleItem();
        (failure.KeyValueKey, failure.ErrorCode, failure.Message).ShouldBe(("b", ErrorCodes.ProtectionNotAllowed, "The level Standard is not allowed."));
        set.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = 2, Failed = 1 });

        // a failure: no record, and the exit code of a failed seed
        set.IsRecorded.ShouldBeFalse();
        _records.Saves.ShouldBe(0);
        ExitCodes.Of(run).ShouldBe(ExitCodes.ERROR_SEED_FAILED);
    }

    [Fact]
    public async Task Seed_AFailedWriteFailsItsPairs_AndTheSeedGoesOn()
    {
        AddSet(Pairs(("a", "Public", "1"), ("b", "Public", "2")), ("configurations/rst.x.conf.json", """{ "a": 1 }"""));
        _keyValues.FailingWriteKeys.Add("b");

        var set = (await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken)).Sets.Single();

        set.Failures.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            item => item.KeyValueKey.ShouldBe("b"),
            item => item.ErrorCode.ShouldBe(ErrorCodes.KeyValueBusy));
        set.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = 1, Failed = 1 });
        _importer.Calls.ShouldHaveSingleItem().Files.Single().Path.ShouldBe("configurations/rst.x.conf.json");
        set.IsRecorded.ShouldBeFalse();
    }

    [Fact]
    public async Task Seed_AWriteThatThrowsFailsEveryPairOfIt()
    {
        AddSet(Pairs(("a", "Public", "1"), ("b", "Standard", "2")));
        _providers.Add(StandardProvider());
        _keyValues.WriteException = new RequestFailedException(403, "Forbidden by the vault.");

        var set = (await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken)).Sets.Single();

        set.Failures.Select(item => (item.KeyValueKey, item.ErrorCode, item.Message)).ShouldBe(
        [
            ("a", ErrorCodes.KeyEncryptionUnavailable, "The key vault failed: Forbidden by the vault."),
            ("b", ErrorCodes.KeyEncryptionUnavailable, "The key vault failed: Forbidden by the vault."),
        ]);

        // an error of the platform keeps its code
        _keyValues.WriteException = KeyValueErrors.ProtectionNotConfigured(KeyValueProtection.Standard);
        var again = (await Seeder().SeedAsync(Request with { Options = new DataSetSeedOptions { Again = true } }, TestContext.Current.CancellationToken)).Sets.Single();
        again.Failures.Select(item => item.ErrorCode).ShouldBe([ErrorCodes.ProtectionNotConfigured, ErrorCodes.ProtectionNotConfigured]);

        // anything else is no failure of a pair: it ends the seed
        _keyValues.WriteException = new InvalidOperationException("The storage broke.");
        await Should.ThrowAsync<InvalidOperationException>(
            () => Seeder().SeedAsync(Request with { Options = new DataSetSeedOptions { Again = true } }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Seed_ASetWithoutPairsCallsNoImport()
    {
        AddSet(null, ("annotations/rst.x.json", "{}"));

        var set = (await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken)).Sets.Single();

        set.KeyValues.ShouldBeNull();
        _keyValues.Imports.ShouldBeEmpty();
        _records.Records.Values.Single().KeyValues.ShouldBe(0);

        // an empty file is planned with no call either
        AddSet(Pairs());
        (await Seeder().SeedAsync(Request, TestContext.Current.CancellationToken)).Sets.Single().KeyValues!.Pairs.ShouldBe(0);
        _keyValues.Imports.ShouldBeEmpty();
    }

    [Fact]
    public async Task Seed_StandardPairsWithoutAVault_AreRefusedBeforeAnythingIsWritten()
    {
        AddSet(Pairs(("smtp.host", "Public", "x"), ("smtp.password", "Standard", "demo")), ("annotations/rst.x.json", "{}"));

        var exception = await Should.ThrowAsync<DataSetException>(() => Seeder().SeedAsync(Request, TestContext.Current.CancellationToken));

        exception.Message.ShouldStartWith("The set 'pairs' has 1 Standard pair in key-values.json, and DbCreator has no Standard vault to encrypt them; nothing was written.");
        exception.Message.ShouldContain("Set AzureKeyVaults__default to the URI of the vault (locally Lowkey Vault, with KeyValues__Protection__Emulator=true)");
        _access.Invocations.ShouldBeEmpty();
        _importer.Calls.ShouldBeEmpty();
        _keyValues.Imports.ShouldBeEmpty();

        // with a Standard provider the plan passes
        _providers.Add(StandardProvider());
        Seeder().Plan(Request).Sets.Single().Name.ShouldBe("pairs");
    }

    [Fact]
    public void Plan_RefusesPairsASeedCannotWrite_AlsoInADependency()
    {
        AddSet(Pairs(("a", "Public", "1")));
        SetFiles.Add(
            _fileSystem,
            Path.Combine(Tool, DataSetCatalog.BuiltInFolderName, "base"),
            ("set.json", Manifest),
            ("key-values.json", """{ "Items": [ { "Key": "api.token", "Protection": "Hsm", "Value": "x" } ] }"""));
        SetFiles.Add(_fileSystem, Path.Combine(Tool, DataSetCatalog.BuiltInFolderName, "top"), ("set.json", """{ "Organization": "fabrikam", "Project": "invoicing-suite", "DependsOn": [ "base" ] }"""));

        Should.Throw<DataSetException>(() => Seeder().Plan(new DataSetSeedRequest { Sets = ["top"], Owner = Owner }))
            .Message.ShouldContain("'api.token': the level Hsm cannot be seeded");
        _access.Invocations.ShouldBeEmpty();
    }

    private static IKeyEncryptionProvider StandardProvider()
    {
        var provider = new Mock<IKeyEncryptionProvider>();
        provider.SetupGet(candidate => candidate.Protection).Returns(KeyValueProtection.Standard);
        return provider.Object;
    }

    private static string Pairs(params (string Key, string Protection, string Value)[] items)
    {
        return $$"""{ "Format": "2s.key-values.v1", "Items": [ {{string.Join(", ", items.Select(item => $$"""{ "Key": "{{item.Key}}", "Protection": "{{item.Protection}}", "Value": "{{item.Value}}" }"""))}} ] }""";
    }

    private void AddSet(string? keyValues, params (string Path, string Content)[] files)
    {
        var folder = Path.Combine(Tool, DataSetCatalog.BuiltInFolderName, "pairs");

        if (_fileSystem.Directory.Exists(folder))
        {
            _fileSystem.Directory.Delete(folder, recursive: true);
        }

        SetFiles.Add(_fileSystem, folder, [("set.json", Manifest), .. files, .. keyValues is null ? Array.Empty<(string, string)>() : [("key-values.json", keyValues)]]);
    }

    private DataSetSeeder Seeder()
    {
        _importer.Calls.Clear();
        _bulkWriter.Calls.Clear();
        _bulkWriter.Completions.Clear();

        return new DataSetSeeder(
            _fileSystem,
            new DataSetCatalog(_fileSystem, Tool),
            new DataSetProvisioner(_access.Object, _views.Object, NullLogger<DataSetProvisioner>.Instance),
            _importer,
            _bulkWriter,
            new DataSetKeyValueSeeder(_keyValues, _providers, Options.Create(new KeyValueOptions()), NullLogger<DataSetKeyValueSeeder>.Instance),
            _records,
            Options.Create(new BaseAccountOptions()),
            TimeProvider.System,
            NullLogger<DataSetSeeder>.Instance);
    }
}
