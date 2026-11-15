using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// A set with project-settings.json end to end: the settings of a project that has none are written through the
// service of the API, and a seed never overwrites or merges settings that are set.
public class ProjectSettingsSeedTests(DbCreatorFixture fixture) : SeedTestsBase(fixture)
{
    private const string Project = "ledger";

    private const string SettingsFile = """
        {
          "Format": "2s.project-settings.v1",
          "Types": {
            "rst": { "Label": "Module", "Plural": "Modules", "Color": "violet", "Icon": "i-lucide-puzzle" },
            "sbt": { "Label": "Customer", "Color": "blue" }
          },
          "DefaultTimeZone": "Europe/Oslo",
          "DateFormat": "dd.mm.yyyy"
        }
        """;

    private IProjectSettingsService ProjectSettings => Services.GetRequiredService<IProjectSettingsService>();

    [Fact]
    public async Task Seed_ANewProject_GetsTheSettingsOfTheSet()
    {
        using var set = TestSetFolder.Accounting().Write(DataSetProjectSettings.FileName, SettingsFile);
        var (organization, owner) = (NewOrganization(), NewOwner());

        var run = await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        run.HasFailures.ShouldBeFalse();
        run.Provisioning.IsProjectCreated.ShouldBeTrue();
        var result = run.Sets.ShouldHaveSingleItem();
        result.ProjectSettings.ShouldNotBeNull().Outcome.ShouldBe(ViewImportOutcome.Created);
        result.Items.ShouldBeEmpty();

        // stored as the API stores them: version 1, by the System actor, with the event of the project's audit trail
        var stored = await ProjectSettings.GetAsync(organization, Project, Cancellation);
        stored.Version.ShouldBe(1);
        stored.Modified!.By.ShouldBe(AuditActor.System);
        ProjectSettingsRules.SameValues(stored, Settings()).ShouldBeTrue();
        stored.Types["rst"].ShouldBe(new ProjectTypeAppearance { Label = "Module", Plural = "Modules", Color = "violet", Icon = "i-lucide-puzzle" });
        (stored.DefaultTimeZone, stored.DateFormat).ShouldBe(("Europe/Oslo", "dd.mm.yyyy"));
        (await Access.GetAccessPointAsync($"{organization}.{Project}"))!.Settings!.Version.ShouldBe(1);

        var audit = await Services.GetRequiredService<IKeyValueAuditLog>().QueryAsync(
            organization,
            Project,
            new KeyValueAuditQuery { Operation = KeyValueAuditOperation.ProjectSettingsChange },
            Cancellation);
        var change = audit.Items.ShouldHaveSingleItem();
        (change.Actor, change.Channel).ShouldBe((AuditActor.System, KeyValueChannel.System));

        // the record counts the settings with the documents and the annotations
        var record = (await Records.GetAsync(run.Target, set.Name, Cancellation)).ShouldNotBeNull();
        (record.ProjectSettings, record.Documents, record.Annotations, record.Created).ShouldBe((ViewImportOutcome.Created, 13, 10, 24));
    }

    [Fact]
    public async Task Seed_AgainWithTheSameSettings_LeavesThemAndTheAccessPointUnchanged()
    {
        using var set = TestSetFolder.Accounting().Write(DataSetProjectSettings.FileName, SettingsFile);
        var (organization, owner) = (NewOrganization(), NewOwner());
        (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).HasFailures.ShouldBeFalse();
        var etag = await AccessPointEtagAsync(organization);

        // seeded already: nothing is read; and with --again the settings are read and found equal
        (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).Sets.Single().IsAlreadySeeded.ShouldBeTrue();
        var again = await Seeder.SeedAsync(Request(set, organization, owner) with { Options = new DataSetSeedOptions { Again = true } }, Cancellation);

        again.HasFailures.ShouldBeFalse();
        var result = again.Sets.ShouldHaveSingleItem();
        result.ProjectSettings.ShouldNotBeNull().Outcome.ShouldBe(ViewImportOutcome.Unchanged);
        result.Items.ShouldBeEmpty();
        (await Records.GetAsync(again.Target, set.Name, Cancellation))!.ProjectSettings.ShouldBe(ViewImportOutcome.Unchanged);
        (await ProjectSettings.GetAsync(organization, Project, Cancellation)).Version.ShouldBe(1);
        (await AccessPointEtagAsync(organization)).ShouldBe(etag);
    }

    [Fact]
    public async Task Seed_AProjectWithOtherSettings_KeepsThem()
    {
        using var set = TestSetFolder.Accounting().Write(DataSetProjectSettings.FileName, SettingsFile);
        var (organization, owner) = (NewOrganization(), NewOwner());
        var other = new ProjectSettings { DateFormat = "mm/dd/yyyy", Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = "Client" } } };
        await Access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = Project, OwnerId = owner }, AccessCaller.System);
        await ProjectSettings.SetAsync(organization, Project, other, ProjectSettingsCondition.Version(0), KeyValueCaller.WithoutSecrets(AuditActor.Account("someone")), Cancellation);
        var etag = await AccessPointEtagAsync(organization);

        var run = await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        // kept, which is no failure: the set is recorded
        run.HasFailures.ShouldBeFalse();
        var result = run.Sets.ShouldHaveSingleItem();
        var settings = result.ProjectSettings.ShouldNotBeNull();
        (settings.Outcome, settings.ErrorCode).ShouldBe((ViewImportOutcome.Skipped, ErrorCodes.DocumentExists));
        settings.Message.ShouldBe("The project has other settings (version 1); a seed never overwrites them.");
        result.Items.Select(item => (item.Path, item.Outcome, item.ErrorCode)).ShouldBe([(DataSetProjectSettings.FileName, ViewImportOutcome.Skipped, ErrorCodes.DocumentExists)]);
        result.IsRecorded.ShouldBeTrue();
        (await Records.GetAsync(run.Target, set.Name, Cancellation))!.ProjectSettings.ShouldBe(ViewImportOutcome.Skipped);

        var stored = await ProjectSettings.GetAsync(organization, Project, Cancellation);
        (stored.Version, stored.Modified!.By).ShouldBe((1L, AuditActor.Account("someone")));
        ProjectSettingsRules.SameValues(stored, other).ShouldBeTrue();
        (await AccessPointEtagAsync(organization)).ShouldBe(etag);
    }

    [Fact]
    public async Task Seed_DryRun_DecidesAndWritesNothing()
    {
        using var set = TestSetFolder.Accounting().Write(DataSetProjectSettings.FileName, SettingsFile);
        var (organization, owner) = (NewOrganization(), NewOwner());
        var dryRun = Request(set, organization, owner) with { Options = new DataSetSeedOptions { DryRun = true } };

        // a project the dry run would create
        var run = await Seeder.SeedAsync(dryRun, Cancellation);

        run.Sets.Single().ProjectSettings.ShouldNotBeNull().Outcome.ShouldBe(ViewImportOutcome.Created);
        (await StoredItems.ReadAsync(Services, organization, Project, owner)).ShouldBeEmpty();

        // a project without settings
        await Access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = Project, OwnerId = owner }, AccessCaller.System);
        var etag = await AccessPointEtagAsync(organization);

        run = await Seeder.SeedAsync(dryRun, Cancellation);

        run.Sets.Single().ProjectSettings.ShouldNotBeNull().Outcome.ShouldBe(ViewImportOutcome.Created);
        (await ProjectSettings.GetAsync(organization, Project, Cancellation)).Version.ShouldBe(0);
        (await AccessPointEtagAsync(organization)).ShouldBe(etag);
    }

    [Fact]
    public async Task Seed_SettingsThatBreakARule_AreWrongInputBeforeAnyWrite()
    {
        using var set = TestSetFolder.Accounting().Write(DataSetProjectSettings.FileName, """{ "DefaultTimeZone": "Central Europe Standard Time" }""");
        var (organization, owner) = (NewOrganization(), NewOwner());

        var command = Command();
        command.Paths = [set.Path];
        (command.Organization, command.Project, command.Owner) = (organization, Project, owner);

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await StoredItems.ReadAsync(Services, organization, Project, owner)).ShouldBeEmpty();
    }

    private static ProjectSettings Settings()
    {
        return ProjectSettingsJson.Read(JsonNode.Parse(SettingsFile)).Settings!;
    }

    private static DataSetSeedRequest Request(TestSetFolder set, string organization, string owner)
    {
        return new DataSetSeedRequest
        {
            Paths = [set.Path],
            Organization = organization,
            Project = Project,
            Owner = owner,
        };
    }

    private async Task<string> AccessPointEtagAsync(string organization)
    {
        var stored = await StoredItems.ReadAsync(Services, organization, Project, NewOwner());
        return stored[$"core/apt.{organization}.{Project}"];
    }
}
