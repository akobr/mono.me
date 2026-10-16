using System.IO.Compression;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewArchiveWriterTests
{
    [Fact]
    public void Folder_HasTheCurrentDocumentsAndTheEarlierVersions()
    {
        var zip = Write(
            ViewArchiveFormat.Folder,
            History("rst.payments", true, (1, "account: user_01", 0, """{"retries":1}"""), (2, "account: user_02", 5, """{"retries":2}""")),
            History("sbt.shop", true, (4, "application: machine-1", 3, """{"theme":"dark"}""")));

        var entries = ReadTextEntries(zip);

        entries.Keys.ShouldBe(["rst.payments.conf.1.json", "rst.payments.conf.json", "sbt.shop.conf.json", "export.json"], ignoreOrder: true);
        entries["rst.payments.conf.json"].ShouldBe("{\n  \"retries\": 2\n}\n");
        entries["rst.payments.conf.1.json"].ShouldBe("{\n  \"retries\": 1\n}\n");
        entries["sbt.shop.conf.json"].ShouldBe("{\n  \"theme\": \"dark\"\n}\n");
    }

    [Fact]
    public void Folder_EntryTimesAreTheVersionTimes()
    {
        var zip = Write(
            ViewArchiveFormat.Folder,
            History("rst.payments", true, (1, "a", 0, """{"a":1}"""), (2, "a", 90, """{"a":2}""")));

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);

        // zip times have a resolution of two seconds and no time zone
        archive.GetEntry("rst.payments.conf.1.json")!.LastWriteTime.DateTime.ShouldBe(Start.UtcDateTime);
        archive.GetEntry("rst.payments.conf.json")!.LastWriteTime.DateTime.ShouldBe(Start.AddMinutes(90).UtcDateTime);
    }

    [Fact]
    public void Folder_ManifestDescribesEveryFile()
    {
        var zip = Write(
            ViewArchiveFormat.Folder,
            History("rst.payments", true, (1, "account: user_01", 0, """{"a":1}"""), (3, "account: user_02", 5, """{"a":3}""")),
            History("rst.cleared", false, (2, "account: user_01", 1, """{"b":2}""")));

        var manifest = JObject.Parse(ReadTextEntries(zip)["export.json"]);

        manifest["Format"]!.Value<string>().ShouldBe("Folder");
        manifest["Organization"]!.Value<string>().ShouldBe("acme");
        manifest["View"]!.Value<string>().ShouldBe("default");
        manifest["ExportedBy"]!["Kind"]!.Value<string>().ShouldBe("Account");
        manifest["ExportedBy"]!["Name"]!.Value<string>().ShouldBe("Ada");

        var configurations = (JArray)manifest["Configurations"]!;
        configurations.Count.ShouldBe(2);

        var cleared = configurations[0];
        cleared["AnnotationKey"]!.Value<string>().ShouldBe("rst.cleared");
        cleared["File"]!.Type.ShouldBe(JTokenType.Null);
        cleared["Version"]!.Type.ShouldBe(JTokenType.Null);
        cleared["Versions"]![0]!["File"]!.Value<string>().ShouldBe("rst.cleared.conf.2.json");

        var payments = configurations[1];
        payments["File"]!.Value<string>().ShouldBe("rst.payments.conf.json");
        payments["Version"]!.Value<int>().ShouldBe(3);
        payments["Author"]!.Value<string>().ShouldBe("account: user_02");
        payments["Versions"]!.Count().ShouldBe(1);
        payments["Versions"]![0]!["Version"]!.Value<int>().ShouldBe(1);
    }

    [Fact]
    public void Folder_ClearedConfigurationHasOnlyVersionFiles()
    {
        var zip = Write(ViewArchiveFormat.Folder, History("rst.cleared", false, (1, "a", 0, """{"a":1}"""), (2, "a", 1, """{"a":2}""")));

        ReadTextEntries(zip).Keys.ShouldBe(["rst.cleared.conf.1.json", "rst.cleared.conf.2.json", "export.json"], ignoreOrder: true);
    }

    [Fact]
    public void Write_CountsConfigurationsAndVersions()
    {
        using var stream = new MemoryStream();
        var statistics = ViewArchiveWriter.Write(
            stream,
            Source(
                History("rst.a", true, (1, "a", 0, """{"a":1}"""), (2, "a", 1, """{"a":2}""")),
                History("rst.b", false, (1, "a", 0, """{"b":1}"""))),
            ViewArchiveFormat.Folder);

        statistics.ConfigurationCount.ShouldBe(1);
        statistics.VersionCount.ShouldBe(3);
    }

    [Fact]
    public void Documents_KeepDatesAndUnicode()
    {
        var history = History("rst.a", true, (1, "a", 0, """{"at":"2026-10-14T01:30:12Z","name":"Ünïcödé ✓"}"""));

        var text = ReadTextEntries(Write(ViewArchiveFormat.Folder, history))["rst.a.conf.json"];

        text.ShouldBe("{\n  \"at\": \"2026-10-14T01:30:12Z\",\n  \"name\": \"Ünïcödé ✓\"\n}\n");
    }

    [Fact]
    public void ExportedDocuments_ReadBackEqual()
    {
        var history = History("rst.a", true, (1, "a", 0, """{"at":"2026-10-14T01:30:12Z","n":1.5,"list":[1,"x",null],"nested":{"b":true}}"""));
        var zip = Write(ViewArchiveFormat.Folder, history);

        var content = ViewArchiveReader.Read(new MemoryStream(zip), new ViewTransferOptions());

        content.Files.Count.ShouldBe(1);
        JToken.DeepEquals(content.Files[0].Content, history.Current!.Content).ShouldBeTrue();
        content.IgnoredFiles.ShouldBe(1); // export.json
    }
}
