using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands.KeyValues;

public class KeyValueImportExportCommandTests
{
    private readonly RecordingConsole _console = new();
    private readonly Mock<IKeyValuesApiClient> _api = new();
    private readonly List<(string Mode, bool? DryRun, string? Prefix, KeyValueDocument Document)> _imports = [];

    public KeyValueImportExportCommandTests()
    {
        _api.Setup(api => api.GetKeyValuePolicyAsync("org1", "proj1"))
            .ReturnsAsync(new KeyValuePolicy { DefaultSecretProtection = KeyValuePolicyDefaultSecretProtection.Hsm });
        _api.Setup(api => api.ImportKeyValuesAsync("org1", "proj1", "default", It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<string>(), It.IsAny<KeyValueDocument>()))
            .Callback<string, string, string, string, bool?, string, KeyValueDocument>((_, _, _, mode, dryRun, prefix, document) => _imports.Add((mode, dryRun, prefix, document)))
            .ReturnsAsync((string _, string _, string _, string _, bool? dryRun, string _, KeyValueDocument document) =>
                KeyValueTestContext.Import(dryRun == true, true, document.Items.Select(item => (item.Key, item.Key.StartsWith("db.") ? KeyValueImportItemResultAction.Create : KeyValueImportItemResultAction.Unchanged)).ToArray()));
    }

    [Fact]
    public async Task Import_EnvFile_PlansFirst_SecretsByGlobGetTheDefaultSecretLevel()
    {
        var files = KeyValueTestContext.Files(("prod.env", "DB__HOST=sql\nDB__PASSWORD=s3cret\nAPP__NAME=billing"));
        _console.Confirm(true);

        var exitCode = await new KeyValueImportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { File = "prod.env", SecretGlobs = ["*password*"] }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _imports.Count.ShouldBe(2);
        _imports[0].DryRun.ShouldBe(true);
        _imports[1].DryRun.ShouldBeNull();
        _imports[0].Mode.ShouldBe("Merge");
        var items = _imports[0].Document.Items.ToList();
        items.Select(item => (item.Key, item.Protection)).ShouldBe([("db.host", KeyValueDocumentItemProtection.Public), ("db.password", KeyValueDocumentItemProtection.Hsm), ("app.name", KeyValueDocumentItemProtection.Public)]);
        _console.Lines.ShouldContain(line => line.Contains("Plan: "));
        _console.Lines.ShouldContain(line => line.Contains("Imported: "));
    }

    [Fact]
    public async Task Import_DryRun_WritesNothing_AndADeclinedPlanAborts()
    {
        var files = KeyValueTestContext.Files(("kv.json", """{ "Format": "2s.key-values.v1", "Items": [ { "Key": "db.a", "Protection": "Public", "Value": "1" } ] }"""));

        var dry = await new KeyValueImportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { File = "kv.json", DryRun = true }.OnExecuteAsync();
        _console.Confirm(false);
        var declined = await new KeyValueImportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { File = "kv.json" }.OnExecuteAsync();

        dry.ShouldBe(ExitCodes.SUCCESS);
        declined.ShouldBe(ExitCodes.WARNING_ABORTED);
        _imports.ShouldAllBe(import => import.DryRun == true);
    }

    [Fact]
    public async Task Import_ReplaceWithDeletes_NeedsTheViewNameTyped()
    {
        var files = KeyValueTestContext.Files(("flat.json", """{ "db.a": "1" }"""));
        _api.Setup(api => api.ImportKeyValuesAsync("org1", "proj1", "default", "Replace", true, "db.", It.IsAny<KeyValueDocument>()))
            .ReturnsAsync(KeyValueTestContext.Import(true, true, ("db.a", KeyValueImportItemResultAction.Create), ("db.old", KeyValueImportItemResultAction.Delete)));

        _console.Input("nope");
        var aborted = await new KeyValueImportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { File = "flat.json", Mode = "replace", Prefix = "db." }.OnExecuteAsync();
        _console.Input("default");
        var applied = await new KeyValueImportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { File = "flat.json", Mode = "replace", Prefix = "db." }.OnExecuteAsync();

        aborted.ShouldBe(ExitCodes.WARNING_ABORTED);
        applied.ShouldBe(ExitCodes.SUCCESS);
        _api.Verify(api => api.ImportKeyValuesAsync("org1", "proj1", "default", "Replace", null, "db.", It.IsAny<KeyValueDocument>()), Times.Once);
    }

    [Fact]
    public async Task Import_UnknownModeOrMissingFile_IsWrongInput()
    {
        var files = KeyValueTestContext.Files(("x.env", "A=1"));

        var mode = await new KeyValueImportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { File = "x.env", Mode = "upsert" }.OnExecuteAsync();
        var missing = await new KeyValueImportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { File = "none.env" }.OnExecuteAsync();

        mode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        missing.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _imports.ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_EnvLeavesSecretsWithoutAValueOut_JsonKeepsThem()
    {
        var document = new KeyValueDocument
        {
            Format = "2s.key-values.v1",
            Items =
            [
                new KeyValueDocumentItem { Key = "db.host", Protection = KeyValueDocumentItemProtection.Public, Value = "sql" },
                new KeyValueDocumentItem { Key = "db.password", Protection = KeyValueDocumentItemProtection.Standard },
            ],
        };
        KeyValueExportRequest? sent = null;
        _api.Setup(api => api.ExportKeyValuesAsync("org1", "proj1", "default", It.IsAny<KeyValueExportRequest>()))
            .Callback<string, string, string, KeyValueExportRequest>((_, _, _, request) => sent = request)
            .ReturnsAsync(document);

        var env = await new KeyValueExportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Format = "env", Prefix = "db.", Protections = ["public", "standard"] }.OnExecuteAsync();

        env.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("DB__HOST=sql");
        _console.Lines.ShouldContain(line => line.Contains("left out") && line.Contains("db.password"));
        sent!.KeyPrefix.ShouldBe("db.");
        sent.Protections.ShouldBe([Protections.Public, Protections.Standard]);
        sent.IncludeSecrets.ShouldBeFalse();

        var json = await new KeyValueExportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { IncludeSecrets = true }.OnExecuteAsync();
        json.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain(line => line.Contains("\"Format\": \"2s.key-values.v1\""));
        sent!.IncludeSecrets.ShouldBeTrue();
    }

    [Fact]
    public async Task Export_ToAFile_GuessesTheFormatFromTheName()
    {
        var file = new Mock<IFile>();
        string? written = null;
        file.Setup(f => f.WriteAllTextAsync("out.env", It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
            .Callback<string, string?, System.Threading.CancellationToken>((_, text, _) => written = text)
            .Returns(Task.CompletedTask);
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(fs => fs.File).Returns(file.Object);
        _api.Setup(api => api.ExportKeyValuesAsync("org1", "proj1", "default", It.IsAny<KeyValueExportRequest>()))
            .ReturnsAsync(new KeyValueDocument { Items = [new KeyValueDocumentItem { Key = "a", Protection = KeyValueDocumentItemProtection.Public, Value = "1" }] });

        var exitCode = await new KeyValueExportCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, fileSystem.Object) { OutputPath = "out.env" }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        written.ShouldBe("A=1");
        _console.Lines.ShouldContain(line => line.Contains("written to out.env"));
    }
}
