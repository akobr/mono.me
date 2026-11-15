using System.Diagnostics;
using System.IO.Abstractions;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// The sets of the repository seed without failures: core by the build of the database, large, showcase, exhibit and
// the playground's folder each into an organization of its own (the given organization wins over their manifests);
// the sets with Standard pairs with the vault (DbCreatorFixture.GetVaultServicesAsync).
public class BuiltInSetsTests(DbCreatorFixture fixture) : SeedTestsBase(fixture)
{
    private IProjectSettingsService ProjectSettings => Services.GetRequiredService<IProjectSettingsService>();

    [Fact]
    public async Task Core_IsSeededByTheBuildOfTheDatabase()
    {
        var run = Fixture.CoreRun;

        run.HasFailures.ShouldBeFalse();
        run.Target.ShouldBe(new DataSetTarget(Constants.ORGANIZATION, Storyteller.Constants.DefaultProjectName, Storyteller.Constants.DefaultViewName));
        var core = run.Sets.ShouldHaveSingleItem();
        (core.Set.Name, core.IsSeeded, core.IsRecorded).ShouldBe((CoreDbStructureBuilder.CORE_SET, true, true));
        core.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = 12 });

        (await Records.GetAsync(run.Target, CoreDbStructureBuilder.CORE_SET, Cancellation)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Large_SeedsWithoutFailures()
    {
        var result = await SeedAsync(new DataSetSeedRequest { Sets = ["large"], Organization = NewOrganization() });

        result.Lines!.Summary.ShouldBe(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 16_017 });
        result.Lines.AncestorsCreated.ShouldBeEmpty();
    }

    [Fact]
    public async Task Showcase_SeedsWithoutFailures()
    {
        var organization = NewOrganization();
        var services = await Fixture.GetVaultServicesAsync(Cancellation);

        var result = await SeedAsync(new DataSetSeedRequest { Sets = ["showcase"], Organization = organization }, services);

        // every file is created, in the summary of its folder's kind; the counts come from the folder, which the admin
        // UI's test/unit/mock/showcase-set.test.ts writes, so a change of the mock needs none here
        var files = DataSetFolder.GetEntries(new FileSystem(), result.Set.Folder);
        result.Lines.ShouldBeNull();
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = files.Count });
        result.Documents.Summaries.Single(summary => summary.Content == ViewArchiveContent.Annotations).Created.ShouldBeGreaterThan(0);
        result.Documents.Summaries
            .Where(summary => summary.Created > 0)
            .ToDictionary(summary => ViewArchiveNames.GetFolder(summary.Content!.Value), summary => summary.Created)
            .ShouldBe(files.GroupBy(file => file.Path.Split('/')[0]).ToDictionary(group => group.Key, group => group.Count()), ignoreOrder: true);
        (await Access.GetAccessPointAsync($"{organization}.invoicing-suite"))!.Description.ShouldStartWith("The B2B SaaS of the documentation");

        // the pairs of its key-values.json, the Standard ones too, every one created
        var pairs = DataSetKeyValues.Read(new FileSystem(), result.Set.Folder).ShouldNotBeNull();
        pairs.Count(KeyValueProtection.Standard).ShouldBeGreaterThan(0);
        result.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = pairs.Items.Count });

        // the modules bind them: a resolved read gives the values, the Standard ones too
        var configurations = services.GetRequiredService<IConfigurationService>();
        var target = new DataSetTarget(organization, "invoicing-suite", "default");
        var invoicing = (await configurations.GetResolvedConfigurationWithSecretsAsync(Key(target, "rst.invoicing"))).ShouldNotBeNull();
        var payments = (await configurations.GetResolvedConfigurationWithSecretsAsync(Key(target, "rst.payments"))).ShouldNotBeNull();
        var dunning = (await configurations.GetResolvedConfigurationWithSecretsAsync(Key(target, "rst.dunning"))).ShouldNotBeNull();
        var values = pairs.Items.ToDictionary(item => item.Key, item => item.Value!);
        invoicing.Content.SelectToken("email.host")!.ToString().ShouldBe(values["smtp.host"]);
        invoicing.Content.SelectToken("email.sender")!.ToString().ShouldBe(values["smtp.sender"]);
        invoicing.Content.SelectToken("email.password")!.ToString().ShouldBe(values["smtp.password"]);
        invoicing.Content.SelectToken("pdfRenderer")!.ToString().ShouldBe(values["pdf.rendererUrl"]);
        payments.Content.SelectToken("adyen.merchantAccount")!.ToString().ShouldBe(values["payments.adyen.merchantAccount"]);
        payments.Content.SelectToken("adyen.hmacKey")!.ToString().ShouldBe(values["payments.adyen.hmacKey"]);
        dunning.Content.SelectToken("maxReminders")!.ToString().ShouldBe(values["dunning.maxReminders"]);

        // and the project gets the settings of the mock's project
        result.ProjectSettings.ShouldNotBeNull().Outcome.ShouldBe(ViewImportOutcome.Created);
        var settings = await ProjectSettings.GetAsync(organization, "invoicing-suite", Cancellation);
        (settings.Version, settings.DefaultTimeZone, settings.DateFormat).ShouldBe((1L, "Europe/Oslo", "dd.mm.yyyy"));
        settings.Types["sbt"].Label.ShouldBe("Customer");
    }

    [Fact]
    public async Task Exhibit_SeedsWithoutFailures_AndItsConfigurationsBindItsPairs()
    {
        var organization = NewOrganization();
        var services = await Fixture.GetVaultServicesAsync(Cancellation);

        var result = await SeedAsync(new DataSetSeedRequest { Sets = ["exhibit"], Organization = organization }, services);

        // README.md is an ignored file; the 20 pairs are created, the Standard one with the vault
        var files = DataSetFolder.GetEntries(new FileSystem(), result.Set.Folder);
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = files.Count - 1, IgnoredFiles = 1 });
        result.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = 20 });

        // a resolved read binds the public pairs, their interpolations and the Standard secret
        var configurations = services.GetRequiredService<IConfigurationService>();
        var target = new DataSetTarget(organization, "toons-and-tyrants", "default");
        var crm = (await configurations.GetResolvedConfigurationWithSecretsAsync(Key(target, "usg.globex.crm"))).ShouldNotBeNull();
        crm.Content.SelectToken("application.url")!.ToString().ShouldBe("https://lairforce.globex.example");
        crm.Content.SelectToken("crm.ransom.escrowPin")!.ToString().ShouldBe("0000-NICE-TRY");
        var globex = (await configurations.GetResolvedConfigurationWithSecretsAsync(Key(target, "sbt.globex"))).ShouldNotBeNull();
        globex.Content.SelectToken("company.portal")!.ToString().ShouldBe("https://lair.globex.example");
        globex.Content.SelectToken("perks.hammockVendors")!.ToString().ShouldBe("https://hammocks.cypresscreek.example/vendors");

        // the type names, colors and icons of Fourth Wall Software's project, its time zone and its date format
        result.ProjectSettings.ShouldNotBeNull().Outcome.ShouldBe(ViewImportOutcome.Created);
        var settings = await ProjectSettings.GetAsync(organization, "toons-and-tyrants", Cancellation);
        settings.Version.ShouldBe(1);
        settings.Types.Select(type => type.Value.Label).ShouldBe(["Solution", "Job", "Company", "Application", "Department", "AppUsage", "JobInstance"]);
        settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" });
        (settings.DefaultTimeZone, settings.DateFormat).ShouldBe(("America/Phoenix", "yyyy-mm-dd"));
    }

    [Fact]
    public async Task Playground_SeedsFromItsFolder()
    {
        var folder = new FileSystem().Path.Combine(RepositoryRoot(), "src", "Platform", "Storyteller", "Playground", "data");

        var result = await SeedAsync(new DataSetSeedRequest { Paths = [folder], Organization = NewOrganization() });

        result.Set.Name.ShouldBe("playground");
        result.Set.Source.ShouldBe(DataSetSource.Path);

        // the files name no responsibility, subject, context or execution: the import creates them with a warning
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = 10, Warnings = 5 });
    }

    private async Task<DataSetSeedResult> SeedAsync(DataSetSeedRequest request, IServiceProvider? services = null)
    {
        var started = Stopwatch.GetTimestamp();
        var seeder = (services ?? Services).GetRequiredService<DataSetSeeder>();
        var run = await seeder.SeedAsync(request with { Owner = NewOwner() }, Cancellation);
        var result = run.Sets.ShouldHaveSingleItem();
        Log($"The set {result.Set.Name} seeded in {Stopwatch.GetElapsedTime(started).TotalSeconds:F1} s on the {Fixture.Emulator.Kind} emulator.");

        result.Failures.ShouldBeEmpty();
        run.HasFailures.ShouldBeFalse();
        result.IsRecorded.ShouldBeTrue();
        result.Items.ShouldBeEmpty();
        return result;
    }

    // The folder of 42.mono.slnx above the output of the tests.
    private static string RepositoryRoot()
    {
        var fileSystem = new FileSystem();
        var directory = fileSystem.DirectoryInfo.New(AppContext.BaseDirectory);

        while (directory is not null && !fileSystem.File.Exists(fileSystem.Path.Combine(directory.FullName, "42.mono.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No 42.mono.slnx above '{AppContext.BaseDirectory}'.");
    }
}
