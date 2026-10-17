using System.IO.Compression;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewArchiveWriterTests
{
    [Fact]
    public void Folder_WritesOneFolderPerKind()
    {
        var entries = ReadTextEntries(Write(View()));

        entries.Keys.ShouldBe(
            [
                "annotations/rst.payments.json",
                "annotations/sbt.northwind.json",
                "schemas/exe.schema.1.json",
                "schemas/exe.schema.json",
                "schemas/sbt.northwind.exe.schema.json",
                "schemas/rst.payments.schema.1.json",
                "schemas/rst.payments.schema.json",
                "templates/exe.template.2.json",
                "templates/exe.template.json",
                "configurations/exe.northwind.payments.prod.conf.json",
                "configurations/rst.payments.conf.1.json",
                "configurations/rst.payments.conf.json",
                "export.json",
            ],
            ignoreOrder: true);

        entries["schemas/exe.schema.json"].ShouldBe("{\n  \"type\": \"object\",\n  \"version\": 2\n}\n");
        entries["schemas/exe.schema.1.json"].ShouldBe("{\n  \"type\": \"object\",\n  \"version\": 1\n}\n");
        entries["templates/exe.template.json"].ShouldBe("{\n  \"retries\": 3\n}\n");
        entries["configurations/rst.payments.conf.json"].ShouldBe("{\n  \"retries\": 2\n}\n");
    }

    [Fact]
    public void Folder_WritesNoFolderForAKindWithoutDocuments()
    {
        var source = Source(History("rst.payments", true, (1, "a", 0, """{"a":1}"""))) with
        {
            Annotations = [ActiveAnnotation("rst.payments")],
            Templates = [new TemplateHistory { AnnotationType = "exe", Versions = [] }],
        };

        ReadTextEntries(Write(source)).Keys.ShouldBe(
            ["annotations/rst.payments.json", "configurations/rst.payments.conf.json", "export.json"],
            ignoreOrder: true);
    }

    [Fact]
    public void Folder_WritesOnlyTheIncludedKinds()
    {
        var source = View() with { Include = [ViewArchiveContent.Templates, ViewArchiveContent.Annotations, ViewArchiveContent.Templates] };
        using var stream = new MemoryStream();

        var statistics = ViewArchiveWriter.Write(stream, source, ViewArchiveFormat.Folder);

        var entries = ReadTextEntries(stream.ToArray());
        entries.Keys.ShouldBe(
            ["annotations/rst.payments.json", "annotations/sbt.northwind.json", "templates/exe.template.2.json", "templates/exe.template.json", "export.json"],
            ignoreOrder: true);

        var manifest = ParseManifest(entries["export.json"]);
        manifest["Include"]!.Values<string>().ShouldBe(["Annotations", "Templates"]);
        ((JArray)manifest["Annotations"]!).Count.ShouldBe(2);
        ((JArray)manifest["Schemas"]!).ShouldBeEmpty();
        ((JArray)manifest["Templates"]!).Count.ShouldBe(1);
        ((JArray)manifest["Configurations"]!).ShouldBeEmpty();

        statistics.ShouldBe(new ViewArchiveStatistics(AnnotationCount: 2, SchemaCount: 0, TemplateCount: 1, ConfigurationCount: 0, VersionCount: 2));
    }

    [Fact]
    public void Folder_EmptyIncludeWritesEveryKind()
    {
        var entries = ReadTextEntries(Write(View() with { Include = [] }));

        entries.Keys.Select(path => path.Split('/')[0]).Distinct().ShouldBe(
            ["annotations", "schemas", "templates", "configurations", "export.json"],
            ignoreOrder: true);
        ParseManifest(entries["export.json"])["Include"]!.Values<string>().ShouldBe(["Annotations", "Schemas", "Templates", "Configurations"]);
    }

    [Fact]
    public void Folder_WritesTheEditableMembersOfAnnotations()
    {
        var unit = ActiveAnnotation("unt.payments.retry") with { Title = "Retry" };
        var source = Source() with
        {
            Annotations =
            [
                ActiveAnnotation("rst.payments") with { Title = "Payments", Labels = ["eu"], TimeZone = "Europe/Prague" },
                ActiveAnnotation("sbt.empty"),
                unit,
            ],
        };

        var entries = ReadTextEntries(Write(source));

        entries["annotations/rst.payments.json"].ShouldBe(
            "{\n  \"Title\": \"Payments\",\n  \"Labels\": [\n    \"eu\"\n  ],\n  \"TimeZone\": \"Europe/Prague\"\n}\n");
        entries["annotations/sbt.empty.json"].ShouldBe("{}\n");
        entries["annotations/unt.payments.retry.json"].ShouldBe(
            "{\n  \"Title\": \"Retry\",\n  \"UnitType\": \"event\",\n  \"UnitDefinition\": \"unknown\"\n}\n");
    }

    [Fact]
    public void Folder_EncodesTheKeysOfEveryKind()
    {
        var source = Source(History("rst.a:b", true, (1, "a", 0, """{"a":1}"""))) with
        {
            Annotations = [ActiveAnnotation("rst.schema"), ActiveAnnotation("rst.a:b")],
            Schemas = [Schema(ViewDocumentKind.AnnotationSchema, null, "rst.template", true, (1, "a", 0, "{}"))],
        };

        ReadTextEntries(Write(source)).Keys.ShouldBe(
            [
                "annotations/rst.%73chema.json",
                "annotations/rst.a%3Ab.json",
                "schemas/rst.%74emplate.schema.json",
                "configurations/rst.a%3Ab.conf.json",
                "export.json",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Folder_EntryTimesAreTheVersionTimesAndTheModificationOfAnnotations()
    {
        var zip = Write(View());

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);

        // zip times have a resolution of two seconds and no time zone
        archive.GetEntry("annotations/rst.payments.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddMinutes(20).UtcDateTime);
        archive.GetEntry("schemas/exe.schema.1.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddMinutes(1).UtcDateTime);
        archive.GetEntry("schemas/exe.schema.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddMinutes(90).UtcDateTime);
        archive.GetEntry("templates/exe.template.2.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddMinutes(4).UtcDateTime);
        archive.GetEntry("templates/exe.template.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddMinutes(6).UtcDateTime);
        archive.GetEntry("configurations/rst.payments.conf.1.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddMinutes(10).UtcDateTime);
        archive.GetEntry("configurations/rst.payments.conf.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddMinutes(12).UtcDateTime);
        archive.GetEntry("export.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddDays(1).UtcDateTime);
    }

    [Fact]
    public void Folder_ManifestHasTheMembersOfVersion2()
    {
        var manifest = ParseManifest(ReadTextEntries(Write(View()))["export.json"]);

        manifest.Properties().Select(property => property.Name).ShouldBe(
        [
            "ManifestVersion", "Organization", "Project", "View", "ExportedAt", "ExportedBy", "Include",
            "Annotations", "Schemas", "Templates", "Configurations",
        ]);
        manifest["ManifestVersion"]!.Value<int>().ShouldBe(2);
        manifest["Organization"]!.Value<string>().ShouldBe("acme");
        manifest["Project"]!.Value<string>().ShouldBe("main");
        manifest["View"]!.Value<string>().ShouldBe("default");
        manifest["ExportedAt"]!.Value<string>().ShouldBe("2026-10-02T08:00:00+00:00");
        manifest["ExportedBy"]!["Kind"]!.Value<string>().ShouldBe("Account");
        manifest["ExportedBy"]!["Name"]!.Value<string>().ShouldBe("Ada");
        manifest["Include"]!.Values<string>().ShouldBe(["Annotations", "Schemas", "Templates", "Configurations"]);

        var annotation = manifest["Annotations"]![0]!;
        ((JObject)annotation).Properties().Select(property => property.Name).ShouldBe(["Key", "File", "Created", "Modified"]);
        annotation["Key"]!.Value<string>().ShouldBe("rst.payments");
        annotation["File"]!.Value<string>().ShouldBe("annotations/rst.payments.json");
        annotation["Created"]!["At"]!.Value<string>().ShouldBe("2026-10-01T08:00:00+00:00");
        annotation["Created"]!["By"]!["Id"]!.Value<string>().ShouldBe("user_01");
        annotation["Modified"]!["At"]!.Value<string>().ShouldBe("2026-10-01T08:20:00+00:00");
        annotation["Modified"]!["By"]!["Kind"]!.Value<string>().ShouldBe("Machine");
        manifest["Annotations"]![1]!["Key"]!.Value<string>().ShouldBe("sbt.northwind");

        // type schemas, descendant-type schemas, annotation schemas
        var schemas = (JArray)manifest["Schemas"]!;
        schemas.Select(schema => schema["File"]!.Value<string>()).ShouldBe(
            ["schemas/exe.schema.json", "schemas/sbt.northwind.exe.schema.json", "schemas/rst.payments.schema.json"]);
        ((JObject)schemas[0]).Properties().Select(property => property.Name).ShouldBe(
            ["Kind", "AnnotationType", "AnnotationKey", "File", "Version", "Author", "UpdatedAt", "Versions"]);
        schemas.Select(schema => schema["Kind"]!.Value<string>()).ShouldBe(["TypeSchema", "DescendantTypeSchema", "AnnotationSchema"]);
        schemas.Select(schema => schema["AnnotationType"]!.Value<string>()).ShouldBe(["exe", "exe", null]);
        schemas.Select(schema => schema["AnnotationKey"]!.Value<string>()).ShouldBe([null, "sbt.northwind", "rst.payments"]);
        schemas[0]["Version"]!.Value<int>().ShouldBe(2);
        schemas[0]["Author"]!.Value<string>().ShouldBe("account: user_02");
        schemas[0]["UpdatedAt"]!.Value<string>().ShouldBe("2026-10-01T09:30:00+00:00");
        schemas[0]["Versions"]!.Single()["File"]!.Value<string>().ShouldBe("schemas/exe.schema.1.json");
        schemas[0]["Versions"]!.Single()["Author"]!.Value<string>().ShouldBe("account: user_01");
        schemas[1]["Versions"]!.ShouldBeEmpty();

        var template = manifest["Templates"]!.Single();
        ((JObject)template).Properties().Select(property => property.Name).ShouldBe(
            ["AnnotationType", "File", "Version", "Author", "UpdatedAt", "Versions"]);
        template["AnnotationType"]!.Value<string>().ShouldBe("exe");
        template["File"]!.Value<string>().ShouldBe("templates/exe.template.json");
        template["Version"]!.Value<int>().ShouldBe(3);
        template["Versions"]!.Single()["Version"]!.Value<int>().ShouldBe(2);
        template["Versions"]!.Single()["File"]!.Value<string>().ShouldBe("templates/exe.template.2.json");

        var configurations = (JArray)manifest["Configurations"]!;
        ((JObject)configurations[0]).Properties().Select(property => property.Name).ShouldBe(
            ["AnnotationKey", "File", "Version", "Author", "UpdatedAt", "Versions"]);
        configurations.Select(configuration => configuration["File"]!.Value<string>()).ShouldBe(
            ["configurations/exe.northwind.payments.prod.conf.json", "configurations/rst.payments.conf.json"]);
        configurations[1]["Versions"]!.Single()["File"]!.Value<string>().ShouldBe("configurations/rst.payments.conf.1.json");
        ((JObject)configurations[1]["Versions"]![0]!).Properties().Select(property => property.Name).ShouldBe(
            ["Version", "File", "Author", "CreatedAt"]);
    }

    [Fact]
    public void Folder_DeletedDocumentsHaveOnlyVersionFiles()
    {
        var source = Source(History("rst.cleared", false, (1, "a", 0, """{"a":1}"""), (2, "a", 1, """{"a":2}"""))) with
        {
            Schemas = [Schema(ViewDocumentKind.TypeSchema, "rst", null, false, (4, "a", 2, "{}"))],
            Templates = [Template("rst", false, (1, "a", 3, """{"t":1}"""))],
        };

        var entries = ReadTextEntries(Write(source));

        entries.Keys.ShouldBe(
            [
                "schemas/rst.schema.4.json",
                "templates/rst.template.1.json",
                "configurations/rst.cleared.conf.1.json",
                "configurations/rst.cleared.conf.2.json",
                "export.json",
            ],
            ignoreOrder: true);

        var manifest = ParseManifest(entries["export.json"]);

        foreach (var document in new[] { manifest["Schemas"]![0]!, manifest["Templates"]![0]!, manifest["Configurations"]![0]! })
        {
            document["File"]!.Type.ShouldBe(JTokenType.Null);
            document["Version"]!.Type.ShouldBe(JTokenType.Null);
            document["Author"]!.Type.ShouldBe(JTokenType.Null);
            document["UpdatedAt"]!.Type.ShouldBe(JTokenType.Null);
            document["Versions"]!.ShouldNotBeEmpty();
        }

        manifest["Configurations"]![0]!["Versions"]!.Select(version => version["File"]!.Value<string>()).ShouldBe(
            ["configurations/rst.cleared.conf.1.json", "configurations/rst.cleared.conf.2.json"]);
    }

    [Theory]
    [InlineData(ViewArchiveFormat.Folder)]
    [InlineData(ViewArchiveFormat.Git)]
    public void Write_CountsDocumentsAndVersions(ViewArchiveFormat format)
    {
        using var stream = new MemoryStream();
        var source = View() with
        {
            Schemas = [.. View().Schemas, Schema(ViewDocumentKind.TypeSchema, "rst", null, false, (1, "a", 0, "{}"))],
        };

        var statistics = ViewArchiveWriter.Write(stream, source, format);

        // versions: schemas 2 + 1 + 2 + 1, templates 2, configurations 2 + 1
        statistics.ShouldBe(new ViewArchiveStatistics(AnnotationCount: 2, SchemaCount: 3, TemplateCount: 1, ConfigurationCount: 2, VersionCount: 11));
    }

    [Fact]
    public void Documents_KeepDatesAndUnicode()
    {
        var history = History("rst.a", true, (1, "a", 0, """{"at":"2026-10-14T01:30:12Z","name":"Ünïcödé ✓"}"""));

        var text = ReadTextEntries(Write(ViewArchiveFormat.Folder, history))["configurations/rst.a.conf.json"];

        text.ShouldBe("{\n  \"at\": \"2026-10-14T01:30:12Z\",\n  \"name\": \"Ünïcödé ✓\"\n}\n");
    }

    [Fact]
    public void ExportedDocuments_ReadBackEqual()
    {
        var source = View() with
        {
            Annotations = [ActiveAnnotation("rst.payments") with { Values = new Dictionary<string, object> { ["at"] = "2026-10-14T01:30:12Z" } }],
            Configurations =
            [
                History("rst.payments", true, (1, "a", 0, """{"at":"2026-10-14T01:30:12Z","n":1.5,"list":[1,"x",null],"nested":{"b":true}}""")),
            ],
        };

        var content = ViewArchiveReader.Read(new MemoryStream(Write(source)), new ViewTransferOptions());

        content.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.Files.Select(file => file.Path).ShouldBe(
        [
            "annotations/rst.payments.json",
            "configurations/rst.payments.conf.json",
            "schemas/exe.schema.json",
            "schemas/rst.payments.schema.json",
            "schemas/sbt.northwind.exe.schema.json",
            "templates/exe.template.json",
        ]);

        // export.json and the version files of schemas and templates
        content.IgnoredFiles.ShouldBe(4);
        content.ExcludedFiles.ShouldBe(0);

        var files = content.Files.ToDictionary(file => file.Path);
        JToken.DeepEquals(files["annotations/rst.payments.json"].Content, ViewAnnotationDocument.Write(source.Annotations[0])).ShouldBeTrue();
        files["annotations/rst.payments.json"].Content!["Values"]!["at"]!.Type.ShouldBe(JTokenType.String);
        JToken.DeepEquals(files["configurations/rst.payments.conf.json"].Content, source.Configurations[0].Current!.Content).ShouldBeTrue();
        JToken.DeepEquals(files["templates/exe.template.json"].Content, source.Templates[0].Current!.Content).ShouldBeTrue();
        JToken.DeepEquals(files["schemas/exe.schema.json"].Content, source.Schemas.Single(schema => schema.Kind == ViewDocumentKind.TypeSchema).Current!.Content).ShouldBeTrue();
    }

    // Every kind, with current documents and earlier versions; the arrays are not in the written order.
    private static ViewArchiveSource View()
    {
        return Source(
                History("rst.payments", true, (1, "account: user_01", 10, """{"retries":1}"""), (2, "account: user_02", 12, """{"retries":2}""")),
                History("exe.northwind.payments.prod", true, (1, "account: user_01", 14, """{"timeout":30}""")))
            with
            {
                Annotations = [ActiveAnnotation("sbt.northwind", 0, 30, "Northwind"), ActiveAnnotation("rst.payments", 0, 20, "Payments")],
                Schemas =
                [
                    Schema(ViewDocumentKind.AnnotationSchema, null, "rst.payments", true, (1, "a", 2, """{"type":"object"}"""), (2, "a", 3, """{"type":"object","required":["retries"]}""")),
                    Schema(ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.northwind", true, (1, "a", 5, """{"type":"object"}""")),
                    Schema(
                        ViewDocumentKind.TypeSchema,
                        "exe",
                        null,
                        true,
                        (1, "account: user_01", 1, """{"type":"object","version":1}"""),
                        (2, "account: user_02", 90, """{"type":"object","version":2}""")),
                ],
                Templates = [Template("exe", true, (2, "a", 4, """{"retries":2}"""), (3, "a", 6, """{"retries":3}"""))],
            };
    }

    private static JObject ParseManifest(string text)
    {
        return JsonConvert.DeserializeObject<JObject>(text, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!;
    }
}
