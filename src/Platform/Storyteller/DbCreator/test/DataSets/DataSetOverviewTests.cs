using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// What the listing of the sets shows of one set, from its folder alone.
public class DataSetOverviewTests
{
    [Fact]
    public void Read_CountsTheDocumentsPerKindAndTheAnnotationsOfTheLines()
    {
        const string annotationLines =
            """
            // literal, generator, unreadable and a key a file holds
            {"AnnotationKey":"cnt.a.x"}
            {"$count":3,"AnnotationKey":"sbt.g-{n}"}
            {"AnnotationKey":"nope"}
            {"AnnotationKey":"sbt.a"}
            """;
        var folder = SetFiles.Folder("showcase");
        var fileSystem = SetFiles.Create(
            folder,
            ("set.json", """{ "Organization": "fabrikam", "Project": "invoicing-suite" }"""),
            ("README.md", "# Showcase"),
            ("annotations/sbt.a.json", "{}"),
            ("annotations/sbt.b.json", "not json"),
            ("configurations/sbt.a.conf.json", """{ "a": 1 }"""),
            ("configurations/sbt.a.conf.1.json", """{ "a": 1 }"""),
            ("templates/sbt.template.json", "{}"),
            ("annotations.jsonl", annotationLines));

        var overview = DataSetOverview.Read(fileSystem, new DataSet(DataSetManifest.Read(fileSystem, folder), folder, DataSetSource.Path));

        overview.Documents.ShouldBe(
        [
            (ViewArchiveContent.Annotations, 2),
            (ViewArchiveContent.Schemas, 0),
            (ViewArchiveContent.Templates, 1),
            (ViewArchiveContent.Configurations, 1),
        ]);
        overview.FailedDocuments.ShouldBe(1);
        overview.IgnoredFiles.ShouldBe(2);

        var lines = overview.Lines.ShouldNotBeNull();
        (lines.Lines, lines.GeneratorLines).ShouldBe((4, 1));
        overview.Annotations.ShouldBe(6);
        overview.FailedAnnotations.ShouldBe(2);
    }

    [Fact]
    public void Read_ASetWithoutTargetOrLines()
    {
        var folder = SetFiles.Folder("bare");
        var fileSystem = SetFiles.Create(folder, ("set.json", "{}"), ("annotations/rst.r.json", "{}"));

        var overview = DataSetOverview.Read(fileSystem, new DataSet(DataSetManifest.Read(fileSystem, folder), folder, DataSetSource.BuiltIn));

        overview.Documents.Sum(pair => pair.Count).ShouldBe(1);
        overview.Lines.ShouldBeNull();
        (overview.Annotations, overview.FailedAnnotations).ShouldBe((0, 0));
        overview.KeyValues.ShouldBeNull();
    }

    [Fact]
    public void Read_CountsThePairsPerLevel_AndRefusesPairsASeedCannotWrite()
    {
        var folder = SetFiles.Folder("pairs");
        var fileSystem = SetFiles.Create(
            folder,
            ("set.json", "{}"),
            ("key-values.json", """
                {
                  "Format": "2s.key-values.v1",
                  "Items": [
                    { "Key": "smtp.host", "Protection": "Public", "Value": "smtp.example" },
                    { "Key": "smtp.password", "Protection": "Standard", "Value": "demo" },
                    { "Key": "smtp.port", "Protection": "Public", "Value": "587" }
                  ]
                }
                """));

        var overview = DataSetOverview.Read(fileSystem, new DataSet(DataSetManifest.Read(fileSystem, folder), folder, DataSetSource.Path));

        // the file is not a document, so neither counted nor ignored
        overview.IgnoredFiles.ShouldBe(0);
        overview.KeyValues.ShouldNotBeNull().Levels.ShouldBe([(KeyValueProtection.Public, 2), (KeyValueProtection.Standard, 1)]);

        // a set a seed refuses is refused by the listing too
        fileSystem.AddFile(Path.Combine(folder, "key-values.json"), """{ "Items": [ { "Key": "api.token", "Protection": "Hsm", "Value": "x" } ] }""");
        Should.Throw<DataSetException>(() => DataSetOverview.Read(fileSystem, new DataSet(DataSetManifest.Read(fileSystem, folder), folder, DataSetSource.Path)))
            .Message.ShouldContain("the level Hsm cannot be seeded");
    }
}
