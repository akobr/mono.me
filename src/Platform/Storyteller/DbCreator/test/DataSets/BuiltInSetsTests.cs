using System.IO.Abstractions;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// The sets of the repository, as copied next to the tool (and to this project's output).
public class BuiltInSetsTests
{
    private static readonly IFileSystem FileSystem = new FileSystem();

    [Fact]
    public void Core_IsTheAnnotationsAndTheConfigurationOfThePlatformItself()
    {
        var core = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == CoreDbStructureBuilder.CORE_SET);

        core.Source.ShouldBe(DataSetSource.BuiltIn);
        core.Manifest.DependsOn.ShouldBeEmpty();
        DataSetTarget.Resolve([core]).ShouldBe(new DataSetTarget("42dotnet", "main", "default"));

        var content = DataSetContent.Read(FileSystem, core.Folder, DataSetTarget.Resolve([core]));

        content.Lines.ShouldBeNull();
        content.Documents.IgnoredFiles.ShouldBe(0);
        content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.Documents.Files
            .Where(file => file.Kind == ViewDocumentKind.Annotation)
            .Select(file => file.AnnotationKey!.ToString())
            .Order(StringComparer.Ordinal)
            .ShouldBe(
            [
                "cnt.42.system",
                "exe.42.scheduler.system",
                "exe.42.storyteller.system",
                "exe.42.supervisor.system",
                "rst.scheduler",
                "rst.storyteller",
                "rst.supervisor",
                "sbt.42",
                "usg.42.scheduler",
                "usg.42.storyteller",
                "usg.42.supervisor",
            ]);

        var configuration = content.Documents.Files.Single(file => file.Kind == ViewDocumentKind.Configuration);
        configuration.AnnotationKey!.ToString().ShouldBe("cnt.42.system");
        configuration.Content!.ToString(Newtonsoft.Json.Formatting.None).ShouldBe("""{"IsSystem":true,"Number":42,"Text":"system"}""");
    }

    [Fact]
    public void Large_IsTheShapeOfTheMocksLargeDatasetInGeneratorLines()
    {
        var large = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == "large");
        var target = DataSetTarget.Resolve([large]);

        target.ShouldBe(new DataSetTarget("acme", "billing", "default"));
        large.Manifest.DependsOn.ShouldBeEmpty();

        var content = DataSetContent.Read(FileSystem, large.Folder, target);

        content.Documents.Files.ShouldBeEmpty();
        content.Documents.IgnoredFiles.ShouldBe(0);
        var lines = content.Lines.ShouldNotBeNull();
        (lines.Lines, lines.GeneratorLines, lines.Annotations.Count).ShouldBe((49, 32, 16_017));
        lines.Annotations.ShouldAllBe(line => line.ErrorCode == null);
        lines.Annotations.Select(line => line.Key!.ToString()).Distinct().Count().ShouldBe(16_017);

        // per type: 6 responsibilities and their 11 units, then per subject 2 contexts, 6 usages, 12 executions and 11
        // units of execution
        lines.Annotations
            .GroupBy(line => line.Key!.Type)
            .ToDictionary(group => group.Key, group => group.Count())
            .ShouldBe(new Dictionary<AnnotationType, int>
            {
                [AnnotationType.Responsibility] = 6,
                [AnnotationType.Unit] = 11,
                [AnnotationType.Subject] = 500,
                [AnnotationType.Context] = 1_000,
                [AnnotationType.Usage] = 3_000,
                [AnnotationType.Execution] = 6_000,
                [AnnotationType.UnitOfExecution] = 5_500,
            });

        var subjects = lines.Annotations.Where(line => line.Key!.Type == AnnotationType.Subject).Select(line => line.Key!.ToString()).ToList();
        (subjects[0], subjects[^1]).ShouldBe(("sbt.customer-001", "sbt.customer-500"));

        // every key below a subject names one of the set's responsibilities, contexts and units, so no ancestor is
        // created on the way and no parent lists more than a handful of names
        var keys = lines.Annotations.Select(line => line.Key!.ToString()).ToHashSet();
        lines.Annotations.ShouldAllBe(line => line.Key!.GetAllAncestorKeys().All(ancestor => keys.Contains(ancestor.ToString())));

        var unit = lines.Annotations.Single(line => line.Key!.ToString() == "unt.payments.sepa").Annotation.ShouldBeOfType<Unit>();
        (unit.UnitType, unit.UnitDefinition).ShouldBe(("time", "0 6 * * 1-5"));
        lines.Annotations.Single(line => line.Key!.ToString() == "sbt.customer-042").Annotation!.Values!["customerNumber"].ToString().ShouldBe("C-00042");
    }

    [Fact]
    public void Showcase_IsTheInvoicingSuiteOfTheMocksShowcase()
    {
        var showcase = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == "showcase");
        var target = DataSetTarget.Resolve([showcase]);

        target.ShouldBe(new DataSetTarget("fabrikam", "invoicing-suite", "default"));
        showcase.Manifest.DependsOn.ShouldBeEmpty();
        showcase.Manifest.ProjectDescription.ShouldNotBeNullOrWhiteSpace();

        var content = DataSetContent.Read(FileSystem, showcase.Folder, target);

        // the files a Git export of the scope gives (ui.admin's test/unit/mock/showcase-set.test.ts writes them): every
        // file of the folder is a document in the folder of its kind. The counts come from the folder, so a change of
        // the mock needs no change here.
        content.Lines.ShouldBeNull();
        content.Documents.IgnoredFiles.ShouldBe(0);
        content.Documents.Files.Count.ShouldBe(DataSetFolder.GetEntries(FileSystem, showcase.Folder).Count);
        content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.Documents.Files.ShouldAllBe(file => file.Path.StartsWith(ViewArchiveNames.GetFolder(ViewArchiveNames.GetContent(file.Kind)) + "/"));
        content.Documents.Files.ShouldContain(file => file.Kind == ViewDocumentKind.Annotation);
        content.Documents.Files.ShouldContain(file => file.Kind == ViewDocumentKind.Configuration);

        // every document hangs on an annotation of the set, so the import creates no ancestor with defaults
        var keys = content.Documents.Files
            .Where(file => file.Kind == ViewDocumentKind.Annotation)
            .Select(file => file.AnnotationKey!.ToString())
            .ToHashSet();
        content.Documents.Files
            .Where(file => file.AnnotationKey is not null)
            .ShouldAllBe(file => keys.Contains(file.AnnotationKey!.ToString())
                && file.AnnotationKey!.GetAllAncestorKeys().All(ancestor => keys.Contains(ancestor.ToString())));
    }

    [Fact]
    public void Playground_IsASetOfItsFolder()
    {
        var folder = FileSystem.Path.Combine(RepositoryRoot(), "src", "Platform", "Storyteller", "Playground", "data");

        var playground = new DataSetCatalog(FileSystem).GetSets([folder]).Single(set => set.Source == DataSetSource.Path);

        playground.Name.ShouldBe("playground");
        playground.Manifest.DependsOn.ShouldBeEmpty();
        DataSetTarget.Resolve([playground]).ShouldBe(new DataSetTarget("42dotnet", "main", "default"));

        var content = DataSetContent.Read(FileSystem, playground.Folder, DataSetTarget.Resolve([playground]));

        content.Lines.ShouldBeNull();
        content.Documents.IgnoredFiles.ShouldBe(0);
        content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.Documents.Files.Count(file => file.Kind == ViewDocumentKind.Annotation).ShouldBe(5);
        content.Documents.Files.Count(file => file.Kind == ViewDocumentKind.Configuration).ShouldBe(5);
    }

    [Fact]
    public void EveryBuiltInSet_ReadsWithoutFailures()
    {
        var catalog = new DataSetCatalog(FileSystem);
        var sets = catalog.GetSets();

        sets.ShouldNotBeEmpty();

        foreach (var set in sets)
        {
            DataSetCatalog.Resolve(sets, [set.Name]).Last().ShouldBe(set);

            var content = DataSetContent.Read(FileSystem, set.Folder, DataSetTarget.Resolve([set], "probe", "probe"));
            content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null, set.Name);
            (content.Lines?.Annotations ?? []).ShouldAllBe(line => line.ErrorCode == null, set.Name);
        }
    }

    // The folder of 42.mono.slnx above the output of the tests.
    private static string RepositoryRoot()
    {
        var directory = FileSystem.DirectoryInfo.New(AppContext.BaseDirectory);

        while (directory is not null && !FileSystem.File.Exists(FileSystem.Path.Combine(directory.FullName, "42.mono.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No 42.mono.slnx above '{AppContext.BaseDirectory}'.");
    }
}
