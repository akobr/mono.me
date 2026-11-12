using System.Diagnostics;
using System.IO.Abstractions;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// The sets of the repository seed without failures: core by the build of the database, large, showcase and the
// playground's folder each into an organization of its own (the given organization wins over their manifests).
public class BuiltInSetsTests(DbCreatorFixture fixture) : SeedTestsBase(fixture)
{
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

        var result = await SeedAsync(new DataSetSeedRequest { Sets = ["showcase"], Organization = organization });

        result.Lines.ShouldBeNull();
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = 398 });
        result.Documents.Summaries.Single(summary => summary.Content == ViewArchiveContent.Annotations).Created.ShouldBe(242);
        (await Access.GetAccessPointAsync($"{organization}.invoicing-suite"))!.Description.ShouldStartWith("The B2B SaaS of the documentation");
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

    private async Task<DataSetSeedResult> SeedAsync(DataSetSeedRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        var run = await Seeder.SeedAsync(request with { Owner = NewOwner() }, Cancellation);
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
