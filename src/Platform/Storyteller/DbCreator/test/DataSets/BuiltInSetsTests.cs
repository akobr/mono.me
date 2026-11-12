using System.IO.Abstractions;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;

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
}
