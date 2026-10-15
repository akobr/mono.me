using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using Alba.CsConsoleFormat;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class ConfigurationTransferCommandTests
{
    private const string Org = AnnotationCommandFixture.Organization;
    private const string Project = AnnotationCommandFixture.Project;
    private const string View = AnnotationCommandFixture.View;
    private const string FileName = "configurations-org1-proj1-default-20261014-013012Z.zip";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 1, 30, 12, TimeSpan.Zero);

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<IConfigurationsApiClient> _api = new(MockBehavior.Strict);
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));

    [Fact]
    public async Task Export_WaitsAndSavesTheArchiveUnderTheServersName()
    {
        StartsExport(ConfigurationExportCreateFormat.Git, Export(ConfigurationExportStatus.Queued), Export(ConfigurationExportStatus.Running), Export(ConfigurationExportStatus.Succeeded));
        ServesArchive(Zip(("rst.a.json", "{}")));

        var exitCode = await Export(format: "GIT");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.Exists(P($@"C:\work\{FileName}")).ShouldBeTrue();
        _fixture.Line("Exporting").ShouldBe("Exporting the configurations of org1.proj1.default (git)…");
        _api.Verify(api => api.GetConfigurationExportAsync(Org, Project, View, "job-1"), Times.Exactly(3));
    }

    [Fact]
    public async Task Export_IntoAFolderUsesTheServersName_ElseTheGivenFile()
    {
        StartsExport(ConfigurationExportCreateFormat.Folder, Export(ConfigurationExportStatus.Succeeded));
        ServesArchive(Zip(("rst.a.json", "{}")));
        _fileSystem.AddDirectory(P(@"C:\exports"));

        (await Export(output: P(@"C:\exports"))).ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.Exists(P($@"C:\exports\{FileName}")).ShouldBeTrue();

        ServesArchive(Zip(("rst.a.json", "{}")));
        (await Export(output: P(@"C:\other\backup.zip"))).ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.Exists(P(@"C:\other\backup.zip")).ShouldBeTrue();
    }

    [Fact]
    public async Task Export_WithExtract_UnpacksIntoANewFolder()
    {
        StartsExport(ConfigurationExportCreateFormat.Git, Export(ConfigurationExportStatus.Succeeded));
        ServesArchive(Zip((".git/", string.Empty), (".git/HEAD", "ref: refs/heads/main\n"), ("rst.a.json", "{\"a\":1}\n")));

        var exitCode = await Export(format: "git", extract: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var folder = P(@"C:\work\configurations-org1-proj1-default-20261014-013012Z");
        _fileSystem.File.ReadAllText(_fileSystem.Path.Combine(folder, "rst.a.json")).ShouldBe("{\"a\":1}\n");
        _fileSystem.File.ReadAllText(_fileSystem.Path.Combine(folder, ".git", "HEAD")).ShouldBe("ref: refs/heads/main\n");
    }

    [Fact]
    public async Task Export_WithExtract_RefusesAFolderThatIsNotEmpty()
    {
        StartsExport(ConfigurationExportCreateFormat.Folder, Export(ConfigurationExportStatus.Succeeded));
        ServesArchive(Zip(("rst.a.json", "{}")));
        _fileSystem.AddFile(P(@"C:\target\keep.txt"), new MockFileData("keep"));

        var exitCode = await Export(output: P(@"C:\target"), extract: true);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("The folder").ShouldBe($"The folder '{P(@"C:\target")}' is not empty.");
    }

    [Fact]
    public async Task Export_ThatFails_PrintsTheError()
    {
        StartsExport(ConfigurationExportCreateFormat.Folder, Export(ConfigurationExportStatus.Failed, error: "The export failed: boom"));

        var exitCode = await Export();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _fixture.Line("The export failed").ShouldBe("The export failed: boom");
    }

    [Fact]
    public async Task Export_StopsWaitingAfterTheTimeout()
    {
        StartsExport(ConfigurationExportCreateFormat.Folder, Export(ConfigurationExportStatus.Running));

        var exitCode = await Export(timeout: 0);

        exitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        _fixture.Line("The export job-1").ShouldBe("The export job-1 is still running; it continues on the server.");
    }

    [Fact]
    public async Task Export_RejectsAnUnknownFormat()
    {
        var exitCode = await Export(format: "tar");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("Unknown format").ShouldBe("Unknown format 'tar'; use 'folder' or 'git'.");
    }

    [Fact]
    public async Task Import_OfAFolder_PacksItsJsonFilesWithoutGit()
    {
        _fileSystem.AddFile(P(@"C:\work\configs\rst.a.json"), new MockFileData("{\"a\":1}"));
        _fileSystem.AddFile(P(@"C:\work\configs\subjects\sbt.b.json"), new MockFileData("{\"b\":2}"));
        _fileSystem.AddFile(P(@"C:\work\configs\notes.txt"), new MockFileData("notes"));
        _fileSystem.AddFile(P(@"C:\work\configs\.git\rst.inside.json"), new MockFileData("{}"));
        List<string>? uploaded = null;
        StartsImport(
            force: null,
            dryRun: true,
            fileName: "configs.zip",
            onUpload: body => uploaded = Entries(body),
            Import(ConfigurationImportStatus.Succeeded, dryRun: true, summary: new ConfigurationImportSummary { Created = 1, Unchanged = 1 }));

        var exitCode = await ImportAsync(@"configs", dryRun: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        uploaded.ShouldBe(["rst.a.json", "subjects/sbt.b.json"]);
        _fixture.Line("Would create").ShouldBe("Would create 1, would update 0, unchanged 1, skipped 0, failed 0; 0 other files ignored.");
        _fixture.Line("Nothing").ShouldBe("Nothing was written (dry run).");
    }

    [Fact]
    public async Task Import_OfAZip_ListsTheItemsAndFailsWithFailedFiles()
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("rst.a.json", "{}"))));
        var items = new List<ConfigurationImportItem>
        {
            new() { Path = "rst.a.json", AnnotationKey = "rst.a", Outcome = ConfigurationImportItemOutcome.Updated, Version = 4 },
            new() { Path = "rst.b.json", AnnotationKey = "rst.b", Outcome = ConfigurationImportItemOutcome.Failed, ErrorCode = "SchemaViolation", Message = "The document violates its schema: …" },
        };
        StartsImport(
            force: true,
            dryRun: null,
            fileName: "export.zip",
            onUpload: _ => { },
            Import(ConfigurationImportStatus.Running),
            Import(ConfigurationImportStatus.Succeeded, summary: new ConfigurationImportSummary { Updated = 1, Failed = 1, IgnoredFiles = 1 }, items: items));

        var exitCode = await ImportAsync("export.zip", force: true);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("Created").ShouldBe("Created 0, updated 1, unchanged 0, skipped 0, failed 1; 1 other file ignored.");
        _fixture.Console.Mock.Verify(console => console.WriteTable(
            It.Is<IEnumerable<ConfigurationImportItem>>(rows => rows.Count() == 2),
            It.IsAny<Func<ConfigurationImportItem, IEnumerable<Cell>>>(),
            It.IsAny<IEnumerable<_42.CLI.Toolkit.Output.IHeaderColumn>>()));
    }

    [Fact]
    public async Task Import_RejectedByTheServer_PrintsItsMessage()
    {
        _fileSystem.AddFile(P(@"C:\work\bad.zip"), new MockFileData("not a zip"));
        _api
            .Setup(api => api.StartConfigurationImportAsync(Org, Project, View, null, null, "bad.zip", It.IsAny<Stream>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(400, "InvalidArchive", "The body is not a zip archive."));

        var exitCode = await ImportAsync("bad.zip");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("The body").ShouldBe("The body is not a zip archive.");
    }

    [Fact]
    public async Task Import_OfAMissingPath_IsWrongInput()
    {
        var exitCode = await ImportAsync("missing.zip");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("The path").ShouldBe("The path 'missing.zip' does not exist.");
    }

    [Fact]
    public async Task Extract_RefusesEntriesOutsideTheFolder()
    {
        using var archive = new MemoryStream(Zip(("../evil.json", "{}")));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => ConfigurationArchives.ExtractAsync(_fileSystem, archive, P(@"C:\work\out")));

        exception.Message.ShouldBe("The archive entry '../evil.json' points outside the folder.");
        _fileSystem.File.Exists(P(@"C:\work\evil.json")).ShouldBeFalse();
    }

    // The mock file system takes Windows paths; on Unix they become /work, /exports, …
    private static string P(string path) => MockUnixSupport.Path(path);

    private static ConfigurationExport Export(ConfigurationExportStatus status, string? error = null)
    {
        return new ConfigurationExport
        {
            Id = "job-1",
            Status = status,
            FileName = FileName,
            CreatedAt = Now,
            ExpiresAt = Now.AddHours(24),
            ConfigurationCount = 2,
            VersionCount = 3,
            Size = 2048,
            Error = error,
        };
    }

    private static ConfigurationImport Import(
        ConfigurationImportStatus status,
        bool dryRun = false,
        ConfigurationImportSummary? summary = null,
        List<ConfigurationImportItem>? items = null)
    {
        return new ConfigurationImport
        {
            Id = "job-2",
            Status = status,
            IsDryRun = dryRun,
            CreatedAt = Now,
            ExpiresAt = Now.AddHours(24),
            Summary = summary,
            Items = items ?? [],
        };
    }

    private static byte[] Zip(params (string Path, string Content)[] files)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(path);

                if (!path.EndsWith('/'))
                {
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(content);
                }
            }
        }

        return stream.ToArray();
    }

    private static List<string> Entries(Stream body)
    {
        using var archive = new ZipArchive(body, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.Select(entry => entry.FullName).ToList();
    }

    private void StartsExport(ConfigurationExportCreateFormat format, params ConfigurationExport[] states)
    {
        _api
            .Setup(api => api.StartConfigurationExportAsync(Org, Project, View, It.Is<ConfigurationExportCreate>(body => body.Format == format)))
            .ReturnsAsync(Export(ConfigurationExportStatus.Queued));

        if (states.Length == 1)
        {
            _api.Setup(api => api.GetConfigurationExportAsync(Org, Project, View, "job-1")).ReturnsAsync(states[0]);
            return;
        }

        var reads = _api.SetupSequence(api => api.GetConfigurationExportAsync(Org, Project, View, "job-1"));

        foreach (var state in states)
        {
            reads = reads.ReturnsAsync(state);
        }
    }

    private void ServesArchive(byte[] zip)
    {
        _api
            .Setup(api => api.GetConfigurationExportArchiveAsync(Org, Project, View, "job-1"))
            .ReturnsAsync(() => new FileResponse(200, new Dictionary<string, IEnumerable<string>>(), new MemoryStream(zip), null, null));
    }

    private void StartsImport(bool? force, bool? dryRun, string fileName, Action<Stream> onUpload, params ConfigurationImport[] states)
    {
        _api
            .Setup(api => api.StartConfigurationImportAsync(Org, Project, View, force, dryRun, fileName, It.IsAny<Stream>()))
            .Callback<string, string, string, bool?, bool?, string, Stream>((_, _, _, _, _, _, body) => onUpload(body))
            .ReturnsAsync(Import(ConfigurationImportStatus.Queued));

        var reads = _api.SetupSequence(api => api.GetConfigurationImportAsync(Org, Project, View, "job-2"));

        foreach (var state in states)
        {
            reads = reads.ReturnsAsync(state);
        }
    }

    private Task<int> Export(string format = "folder", string? output = null, bool extract = false, int timeout = 10)
    {
        var command = new ConfigExportCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object, _fileSystem, new ImmediatePolling())
        {
            Format = format,
            Output = output,
            IsExtractRequested = extract,
            TimeoutMinutes = timeout,
        };

        return command.OnExecuteAsync();
    }

    private Task<int> ImportAsync(string path, bool dryRun = false, bool force = false)
    {
        var command = new ConfigImportCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object, _fileSystem, new ImmediatePolling())
        {
            Path = path,
            IsDryRun = dryRun,
            IsForced = force,
        };

        return command.OnExecuteAsync();
    }

    private sealed class ImmediatePolling : TransferPolling
    {
        protected override Task DelayAsync(TimeSpan delay) => Task.CompletedTask;
    }
}
