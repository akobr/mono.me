using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class StorytellerTransferCommandTests
{
    private const string Org = AnnotationCommandFixture.Organization;
    private const string Project = AnnotationCommandFixture.Project;
    private const string View = AnnotationCommandFixture.View;
    private const string FileName = "export-org1-proj1-default-20261017-013012Z.zip";

    // The message of the API to a user below Administrator.
    private const string Refused = "No Administrator access to 'org1.proj1'.";

    private static readonly DateTimeOffset Now = new(2026, 10, 17, 1, 30, 12, TimeSpan.Zero);

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<ITransfersApiClient> _api = new(MockBehavior.Strict);
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));

    [Fact]
    public void Commands_AreSubcommandsOfStory_AndNoLongerOfConfig()
    {
        var story = typeof(StorytellerListCommand).GetCustomAttribute<SubcommandAttribute>()!.Types;
        var config = typeof(ConfigGetCommand).GetCustomAttribute<SubcommandAttribute>()!.Types;

        story.ShouldContain(typeof(StorytellerExportCommand));
        story.ShouldContain(typeof(StorytellerImportCommand));
        config.Select(type => type.GetCustomAttribute<CommandAttribute>()!.Name).ShouldNotContain(CommandNames.EXPORT);
        config.Select(type => type.GetCustomAttribute<CommandAttribute>()!.Name).ShouldNotContain(CommandNames.IMPORT);
        typeof(StorytellerExportCommand).GetCustomAttribute<CommandAttribute>()!.Name.ShouldBe("export");
        typeof(StorytellerImportCommand).GetCustomAttribute<CommandAttribute>()!.Name.ShouldBe("import");
    }

    [Theory]
    [InlineData(typeof(StorytellerExportCommand), " A machine needs the read scopes of the included kinds and Configuration.Transfer instead of a role.")]
    [InlineData(typeof(StorytellerImportCommand), " A machine needs the write scopes of the included kinds, with Annotation.Manage for annotations, Schema.Write for schemas and Template.Write for templates, and Configuration.Transfer instead of a role.")]
    public void Commands_NameTheAdministratorRole_AndWhatAMachineNeeds(Type command, string machine)
    {
        var description = command.GetCustomAttribute<CommandAttribute>()!.Description;

        description.ShouldNotBeNull();
        description.ShouldContain("(Administrator)", Case.Sensitive);
        description.ShouldEndWith(machine);
    }

    [Fact]
    public void Export_ParsesItsOptions()
    {
        var app = Application<StorytellerExportCommand>();

        app.Parse("-f", "git", "-i", "annotations,Templates", "--include", "schemas", "-o", "out", "-x", "--timeout", "5");

        app.Model.Format.ShouldBe("git");
        app.Model.Include.ShouldBe(["annotations,Templates", "schemas"]);
        app.Model.Output.ShouldBe("out");
        app.Model.IsExtractRequested.ShouldBeTrue();
        app.Model.TimeoutMinutes.ShouldBe(5);
    }

    [Fact]
    public void Export_HasDefaults()
    {
        var app = Application<StorytellerExportCommand>();

        app.Parse();

        app.Model.Format.ShouldBe("folder");
        app.Model.Include.ShouldBeNull();
        app.Model.Output.ShouldBeNull();
        app.Model.IsExtractRequested.ShouldBeFalse();
        app.Model.TimeoutMinutes.ShouldBe(10);
    }

    [Fact]
    public void Import_ParsesItsOptions()
    {
        var app = Application<StorytellerImportCommand>();

        app.Parse("export.zip", "-i", "configurations", "-e", "skip", "--dry-run", "--force", "--timeout", "3");

        app.Model.Path.ShouldBe("export.zip");
        app.Model.Include.ShouldBe(["configurations"]);
        app.Model.Existing.ShouldBe("skip");
        app.Model.IsDryRun.ShouldBeTrue();
        app.Model.IsForced.ShouldBeTrue();
        app.Model.TimeoutMinutes.ShouldBe(3);
    }

    [Fact]
    public void Import_HasDefaults()
    {
        var app = Application<StorytellerImportCommand>();

        app.Parse("folder");

        app.Model.Path.ShouldBe("folder");
        app.Model.Include.ShouldBeNull();
        app.Model.Existing.ShouldBeNull();
        app.Model.IsDryRun.ShouldBeFalse();
        app.Model.IsForced.ShouldBeFalse();
        app.Model.TimeoutMinutes.ShouldBe(10);
    }

    [Fact]
    public async Task Export_ByDefault_SendsABodyWithoutInclude_AndSavesTheArchiveUnderTheServersName()
    {
        var bodies = StartsExport(Export(ViewExportStatus.Queued), Export(ViewExportStatus.Running), Export(ViewExportStatus.Succeeded));
        ServesArchive(Zip(("annotations/rst.a.json", "{}")));

        var exitCode = await ExportAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        bodies.ShouldHaveSingleItem();
        bodies[0].Format.ShouldBe(ViewExportCreateFormat.Folder);
        bodies[0].Include.ShouldBeNull();
        _fileSystem.File.Exists(P($@"C:\work\{FileName}")).ShouldBeTrue();
        _fixture.Line("Exporting").ShouldBe("Exporting the annotations, schemas, templates and configurations of org1.proj1.default (folder)…");
        _fixture.Line("! Exported").ShouldBe(
            $"! Exported 4 annotations, 3 schemas, 2 templates and 5 configurations (9 versions, 2 KB) to {FileName}.");
        _api.Verify(api => api.GetViewExportAsync(Org, Project, View, "job-1"), Times.Exactly(3));
    }

    [Fact]
    public async Task Export_WithInclude_SendsTheKindsInImportOrder_AndCountsOnlyThem()
    {
        var bodies = StartsExport(Export(ViewExportStatus.Succeeded, include: [ViewArchiveContent.Annotations, ViewArchiveContent.Templates]));
        ServesArchive(Zip(("annotations/rst.a.json", "{}")));

        var exitCode = await ExportAsync(format: "GIT", include: ["Templates", "annotations,templates"]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        bodies[0].Format.ShouldBe(ViewExportCreateFormat.Git);
        bodies[0].Include.ShouldBe([ViewArchiveContent.Annotations, ViewArchiveContent.Templates]);
        _fixture.Line("Exporting").ShouldBe("Exporting the annotations and templates of org1.proj1.default (git)…");
        _fixture.Line("! Exported").ShouldBe($"! Exported 4 annotations and 2 templates (9 versions, 2 KB) to {FileName}.");
    }

    [Fact]
    public async Task Export_IntoAFolderUsesTheServersName_ElseTheGivenFile()
    {
        StartsExport(Export(ViewExportStatus.Succeeded));
        ServesArchive(Zip(("annotations/rst.a.json", "{}")));
        _fileSystem.AddDirectory(P(@"C:\exports"));

        (await ExportAsync(output: P(@"C:\exports"))).ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.Exists(P($@"C:\exports\{FileName}")).ShouldBeTrue();

        ServesArchive(Zip(("annotations/rst.a.json", "{}")));
        (await ExportAsync(output: P(@"C:\other\backup.zip"))).ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.Exists(P(@"C:\other\backup.zip")).ShouldBeTrue();
    }

    [Fact]
    public async Task Export_WithExtract_UnpacksIntoANewFolder()
    {
        StartsExport(Export(ViewExportStatus.Succeeded));
        ServesArchive(Zip((".git/", string.Empty), (".git/HEAD", "ref: refs/heads/main\n"), ("configurations/rst.a.conf.json", "{\"a\":1}\n")));

        var exitCode = await ExportAsync(format: "git", extract: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var folder = P(@"C:\work\export-org1-proj1-default-20261017-013012Z");
        _fileSystem.File.ReadAllText(_fileSystem.Path.Combine(folder, "configurations", "rst.a.conf.json")).ShouldBe("{\"a\":1}\n");
        _fileSystem.File.ReadAllText(_fileSystem.Path.Combine(folder, ".git", "HEAD")).ShouldBe("ref: refs/heads/main\n");
        _fixture.Line("! Exported").ShouldEndWith(" into export-org1-proj1-default-20261017-013012Z.");
    }

    [Fact]
    public async Task Export_WithExtract_RefusesAFolderThatIsNotEmpty()
    {
        StartsExport(Export(ViewExportStatus.Succeeded));
        ServesArchive(Zip(("annotations/rst.a.json", "{}")));
        _fileSystem.AddFile(P(@"C:\target\keep.txt"), new MockFileData("keep"));

        var exitCode = await ExportAsync(output: P(@"C:\target"), extract: true);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("The folder").ShouldBe($"The folder '{P(@"C:\target")}' is not empty.");
    }

    [Fact]
    public async Task Export_ThatFails_PrintsTheError()
    {
        StartsExport(Export(ViewExportStatus.Failed, error: "The export failed: boom"));

        var exitCode = await ExportAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _fixture.Line("The export failed").ShouldBe("The export failed: boom");
    }

    [Fact]
    public async Task Export_StopsWaitingAfterTheTimeout()
    {
        StartsExport(Export(ViewExportStatus.Running));

        var exitCode = await ExportAsync(timeout: 0);

        exitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        _fixture.Line("The export job-1").ShouldBe("The export job-1 is still running; it continues on the server.");
    }

    [Theory]
    [InlineData("tar", null, "Unknown format 'tar'; use 'folder' or 'git'.")]
    [InlineData("folder", "annotations,secrets", "Unknown kind 'secrets' in --include; use annotations, schemas, templates or configurations, comma-separated.")]
    [InlineData("folder", "1", "Unknown kind '1' in --include; use annotations, schemas, templates or configurations, comma-separated.")]
    [InlineData("folder", " , ", "The option --include names no kind; use annotations, schemas, templates or configurations, comma-separated, or leave it out for all four.")]
    public async Task Export_RejectsUnknownOptionsLocally(string format, string? include, string message)
    {
        var exitCode = await ExportAsync(format: format, include: include is null ? null : [include]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldBe([message]);
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(400, "InvalidQuery", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, "TransferLimit", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(403, "AccessDenied", ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    public async Task Export_RejectedByTheServer_PrintsItsMessage(int status, string code, int expected)
    {
        _api
            .Setup(api => api.StartViewExportAsync(Org, Project, View, It.IsAny<ViewExportCreate>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(status, code, $"Rejected with {code}."));

        var exitCode = await ExportAsync();

        exitCode.ShouldBe(expected);
        _fixture.Console.Lines.ShouldBe([$"Rejected with {code}."]);
    }

    [Fact]
    public async Task Export_UnauthorizedAtTheStart_IsLeftToProgramMain()
    {
        _api
            .Setup(api => api.StartViewExportAsync(Org, Project, View, It.IsAny<ViewExportCreate>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => ExportAsync());

        exception.StatusCode.ShouldBe(401);
        _fixture.Console.Lines.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Export_RefusedAtAReadOfTheJob_PrintsTheServersMessage(int runningReads)
    {
        StartsExport([.. Enumerable.Repeat(Export(ViewExportStatus.Running), runningReads)], Refusal());

        var exitCode = await ExportAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldBe(["Exporting the annotations, schemas, templates and configurations of org1.proj1.default (folder)…", Refused]);
        _api.Verify(api => api.GetViewExportAsync(Org, Project, View, "job-1"), Times.Exactly(runningReads + 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_WhoseArchiveIsRefused_PrintsTheServersMessage_AndWritesNothing(bool extract)
    {
        StartsExport(Export(ViewExportStatus.Succeeded));
        _api.Setup(api => api.GetViewExportArchiveAsync(Org, Project, View, "job-1")).ThrowsAsync(Refusal());

        var exitCode = await ExportAsync(extract: extract);

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldBe(["Exporting the annotations, schemas, templates and configurations of org1.proj1.default (folder)…", Refused]);
        _fileSystem.AllFiles.ShouldBeEmpty();
        _fileSystem.Directory.Exists(P(@"C:\work\export-org1-proj1-default-20261017-013012Z")).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_UnauthorizedWhileWaiting_IsLeftToProgramMain()
    {
        StartsExport([Export(ViewExportStatus.Running)], AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => ExportAsync());

        exception.StatusCode.ShouldBe(401);
        _fixture.Console.Lines.ShouldBe(["Exporting the annotations, schemas, templates and configurations of org1.proj1.default (folder)…"]);
    }

    [Fact]
    public async Task Export_UnauthorizedAtTheDownload_IsLeftToProgramMain()
    {
        StartsExport(Export(ViewExportStatus.Succeeded));
        _api.Setup(api => api.GetViewExportArchiveAsync(Org, Project, View, "job-1")).ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => ExportAsync());

        exception.StatusCode.ShouldBe(401);
        _fixture.Console.Lines.ShouldBe(["Exporting the annotations, schemas, templates and configurations of org1.proj1.default (folder)…"]);
        _fileSystem.AllFiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_OfAFolder_PacksItsJsonFiles_WithoutHiddenPaths_AndPassesTheDefaults()
    {
        _fileSystem.AddFile(P(@"C:\work\view\annotations\rst.a.json"), new MockFileData("{}"));
        _fileSystem.AddFile(P(@"C:\work\view\configurations\rst.a.conf.json"), new MockFileData("{\"a\":1}"));
        _fileSystem.AddFile(P(@"C:\work\view\exe.template.json"), new MockFileData("{\"b\":2}"));
        _fileSystem.AddFile(P(@"C:\work\view\notes.txt"), new MockFileData("notes"));
        _fileSystem.AddFile(P(@"C:\work\view\.git\rst.inside.json"), new MockFileData("{}"));
        _fileSystem.AddFile(P(@"C:\work\view\.vscode\settings.json"), new MockFileData("{}"));
        _fileSystem.AddFile(P(@"C:\work\view\schemas\._exe.schema.json"), new MockFileData("{}"));
        _fileSystem.AddFile(P(@"C:\work\view\__MACOSX\annotations\rst.b.json"), new MockFileData("{}"));
        var uploads = StartsImport(Import(ViewImportStatus.Succeeded, summary: new ViewImportSummary { Created = 3 }));

        var exitCode = await ImportAsync("view");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var upload = uploads.ShouldHaveSingleItem();
        upload.Entries.ShouldBe(["annotations/rst.a.json", "configurations/rst.a.conf.json", "exe.template.json"]);
        upload.Include.ShouldBeNull();
        upload.Existing.ShouldBeNull();
        upload.Force.ShouldBeNull();
        upload.DryRun.ShouldBeNull();
        upload.FileName.ShouldBe("view.zip");
        _fixture.Line("Importing").ShouldBe("Importing view.zip into org1.proj1.default…");
    }

    [Fact]
    public async Task Import_OfAFolderWithOnlyHiddenFiles_IsWrongInput()
    {
        _fileSystem.AddFile(P(@"C:\work\repo\.git\config.json"), new MockFileData("{}"));

        var exitCode = await ImportAsync("repo");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldBe(["The folder 'repo' has no *.json files outside hidden folders."]);
    }

    [Fact]
    public async Task Import_PassesTheIncludeExistingForceAndDryRun()
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("annotations/rst.a.json", "{}"))));
        var uploads = StartsImport(Import(ViewImportStatus.Succeeded, dryRun: true));

        var exitCode = await ImportAsync("export.zip", include: ["configurations,ANNOTATIONS", "annotations"], existing: "Skip", dryRun: true, force: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var upload = uploads.ShouldHaveSingleItem();
        upload.Include.ShouldBe([ViewArchiveContent.Annotations, ViewArchiveContent.Configurations]);
        upload.Existing.ShouldBe(ViewImportExisting.Skip);
        upload.Force.ShouldBe(true);
        upload.DryRun.ShouldBe(true);
        upload.FileName.ShouldBe("export.zip");
        _fixture.Line("Importing").ShouldBe("Importing export.zip into org1.proj1.default (annotations and configurations, skip existing, forced, dry run)…");
        _fixture.Line("Nothing").ShouldBe("Nothing was written (dry run).");
    }

    [Theory]
    [InlineData(null, "replace", "Unknown value 'replace' of --existing; use 'overwrite' or 'skip'.")]
    [InlineData(null, "", "Unknown value '' of --existing; use 'overwrite' or 'skip'.")]
    [InlineData("history", null, "Unknown kind 'history' in --include; use annotations, schemas, templates or configurations, comma-separated.")]
    public async Task Import_RejectsUnknownOptionsLocally_BeforeReadingThePath(string? include, string? existing, string message)
    {
        var exitCode = await ImportAsync("missing.zip", include: include is null ? null : [include], existing: existing);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldBe([message]);
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Import_ListsTheItemsWithKindsAndWarnings_AndTheSummaryPerKind()
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("annotations/rst.a.json", "{}"))));
        var items = new List<ViewImportItem>
        {
            new()
            {
                Path = "annotations/usg.n.p.json", Kind = ViewImportItemKind.Annotation, AnnotationKey = "usg.n.p", Outcome = ViewImportItemOutcome.Created,
                Warnings = [new ViewImportWarning { Code = "AncestorCreated", Message = "The missing ancestors sbt.n are created with defaults." }],
            },
            new() { Path = "templates/exe.template.json", Kind = ViewImportItemKind.Template, AnnotationType = "exe", Outcome = ViewImportItemOutcome.Updated, Version = 4 },
        };
        StartsImport(
            Import(ViewImportStatus.Running),
            Import(
                ViewImportStatus.Succeeded,
                summary: new ViewImportSummary { Created = 1, Updated = 1, Unchanged = 2, Warnings = 1, ExcludedFiles = 2, IgnoredFiles = 1 },
                summaries:
                [
                    new ViewImportSummary { Content = ViewImportSummaryContent.Annotations, Created = 1, Unchanged = 2, Warnings = 1 },
                    new ViewImportSummary { Content = ViewImportSummaryContent.Templates, Updated = 1 },
                ],
                items: items));

        var exitCode = await ImportAsync("export.zip");

        // a warning does not change the exit code
        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Line("# Import").ShouldBe("# Import of export.zip");
        var tables = _fixture.Console.Tables;
        tables.Count.ShouldBe(2);
        tables[0].Headers.ShouldBe(["Kind", "Document", "Outcome", "Code", "Message"]);
        tables[0].Rows.ShouldBe(
        [
            ["Annotation", "usg.n.p", "Created", string.Empty, string.Empty],
            [string.Empty, string.Empty, "!", "AncestorCreated", "The missing ancestors sbt.n are created with defaults."],
            ["Template", "exe", "Updated", string.Empty, string.Empty],
        ]);
        tables[1].Headers.ShouldBe(["Kind", "Created", "Updated", "Unchanged", "Skipped", "Failed", "Warnings"]);
        tables[1].Rows.ShouldBe(
        [
            ["Annotations", "1", "0", "2", "0", "0", "1"],
            ["Templates", "0", "1", "0", "0", "0", "0"],
            ["Total", "1", "1", "2", "0", "0", "1"],
        ]);
        _fixture.Line("2 files excluded").ShouldBe("2 files excluded, 1 other file ignored.");
    }

    [Fact]
    public async Task Import_WithAFailedFile_ExitsWithTwo()
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("configurations/rst.b.conf.json", "{}"))));
        var items = new List<ViewImportItem>
        {
            new()
            {
                Path = "configurations/rst.b.conf.json", Kind = ViewImportItemKind.Configuration, AnnotationKey = "rst.b", Outcome = ViewImportItemOutcome.Failed,
                ErrorCode = "SchemaViolation", Message = "The document violates its schema: …",
            },
        };
        StartsImport(Import(
            ViewImportStatus.Succeeded,
            summary: new ViewImportSummary { Failed = 1 },
            summaries: [new ViewImportSummary { Content = ViewImportSummaryContent.Configurations, Failed = 1 }],
            items: items));

        var exitCode = await ImportAsync("export.zip");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Tables[0].Rows.ShouldBe([["Configuration", "rst.b", "Failed", "SchemaViolation", "The document violates its schema: …"]]);
    }

    [Theory]
    [InlineData(400, "InvalidArchive", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, "TransferLimit", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(413, "ArchiveTooLarge", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(403, "AccessDenied", ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    public async Task Import_RejectedByTheServer_PrintsItsMessage(int status, string code, int expected)
    {
        _fileSystem.AddFile(P(@"C:\work\bad.zip"), new MockFileData("not a zip"));
        _api
            .Setup(api => api.StartViewImportAsync(Org, Project, View, null, null, null, null, "bad.zip", It.IsAny<Stream>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(status, code, $"Rejected with {code}."));

        var exitCode = await ImportAsync("bad.zip");

        exitCode.ShouldBe(expected);
        _fixture.Console.Lines.ShouldBe([$"Rejected with {code}."]);
    }

    [Fact]
    public async Task Import_UnauthorizedAtTheStart_IsLeftToProgramMain()
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("annotations/rst.a.json", "{}"))));
        _api
            .Setup(api => api.StartViewImportAsync(Org, Project, View, null, null, null, null, "export.zip", It.IsAny<Stream>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => ImportAsync("export.zip"));

        exception.StatusCode.ShouldBe(401);
        _fixture.Console.Lines.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Import_RefusedAtAReadOfTheJob_PrintsTheServersMessage(int runningReads)
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("annotations/rst.a.json", "{}"))));
        StartsImport([.. Enumerable.Repeat(Import(ViewImportStatus.Running), runningReads)], Refusal());

        var exitCode = await ImportAsync("export.zip");

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldBe(["Importing export.zip into org1.proj1.default…", Refused]);
        _api.Verify(api => api.GetViewImportAsync(Org, Project, View, "job-2"), Times.Exactly(runningReads + 1));
    }

    [Fact]
    public async Task Import_UnauthorizedWhileWaiting_IsLeftToProgramMain()
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("annotations/rst.a.json", "{}"))));
        StartsImport([Import(ViewImportStatus.Running)], AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => ImportAsync("export.zip"));

        exception.StatusCode.ShouldBe(401);
        _fixture.Console.Lines.ShouldBe(["Importing export.zip into org1.proj1.default…"]);
    }

    [Fact]
    public async Task Import_ThatFails_PrintsTheError()
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("annotations/rst.a.json", "{}"))));
        StartsImport(Import(ViewImportStatus.Failed, error: "The import failed: boom"));

        var exitCode = await ImportAsync("export.zip");

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _fixture.Line("The import failed").ShouldBe("The import failed: boom");
    }

    [Fact]
    public async Task Import_StopsWaitingAfterTheTimeout()
    {
        _fileSystem.AddFile(P(@"C:\work\export.zip"), new MockFileData(Zip(("annotations/rst.a.json", "{}"))));
        var running = Import(ViewImportStatus.Running);
        running.ProcessedCount = 3;
        running.TotalCount = 8;
        StartsImport(running);

        var exitCode = await ImportAsync("export.zip", timeout: 0);

        exitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        _fixture.Line("The import job-2").ShouldBe("The import job-2 is still running (3 of 8 files); it continues on the server.");
    }

    [Fact]
    public async Task Import_OfAMissingPath_IsWrongInput()
    {
        var exitCode = await ImportAsync("missing.zip");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("The path").ShouldBe("The path 'missing.zip' does not exist.");
    }

    // The mock file system takes Windows paths; on Unix they become /work, /exports, …
    private static string P(string path) => MockUnixSupport.Path(path);

    private static ViewExport Export(ViewExportStatus status, string? error = null, List<ViewArchiveContent>? include = null)
    {
        var all = include is null;

        return new ViewExport
        {
            Id = "job-1",
            Status = status,
            FileName = FileName,
            Include = include ?? [ViewArchiveContent.Annotations, ViewArchiveContent.Schemas, ViewArchiveContent.Templates, ViewArchiveContent.Configurations],
            CreatedAt = Now,
            ExpiresAt = Now.AddHours(24),
            AnnotationCount = 4,
            SchemaCount = all ? 3 : null,
            TemplateCount = 2,
            ConfigurationCount = all ? 5 : null,
            VersionCount = 9,
            Size = 2048,
            Error = error,
        };
    }

    private static ViewImport Import(
        ViewImportStatus status,
        bool dryRun = false,
        ViewImportSummary? summary = null,
        List<ViewImportSummary>? summaries = null,
        List<ViewImportItem>? items = null,
        string? error = null)
    {
        return new ViewImport
        {
            Id = "job-2",
            Status = status,
            IsDryRun = dryRun,
            CreatedAt = Now,
            ExpiresAt = Now.AddHours(24),
            Summary = summary ?? new ViewImportSummary(),
            Summaries = summaries ?? [],
            Items = items ?? [],
            Error = error,
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

    private static ApiException Refusal()
    {
        return AnnotationCommandFixture.Error(403, "AccessDenied", Refused);
    }

    private static List<string> Entries(Stream body)
    {
        using var archive = new ZipArchive(body, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.Select(entry => entry.FullName).ToList();
    }

    private CommandLineApplication<TCommand> Application<TCommand>()
        where TCommand : class
    {
        var services = new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_api.Object)
            .AddSingleton<IFileSystem>(_fileSystem)
            .AddSingleton<TransferPolling>(new ImmediatePolling())
            .BuildServiceProvider();
        var app = new CommandLineApplication<TCommand>();
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);
        return app;
    }

    private List<ViewExportCreate> StartsExport(params ViewExport[] states)
    {
        return StartsExport(states, null);
    }

    // The reads of the job return the states, then the next read throws the error, when there is one.
    private List<ViewExportCreate> StartsExport(ViewExport[] states, ApiException? error)
    {
        var bodies = new List<ViewExportCreate>();
        _api
            .Setup(api => api.StartViewExportAsync(Org, Project, View, It.IsAny<ViewExportCreate>()))
            .Callback<string, string, string, ViewExportCreate>((_, _, _, body) => bodies.Add(body))
            .ReturnsAsync(Export(ViewExportStatus.Queued));

        if (states.Length == 1 && error is null)
        {
            _api.Setup(api => api.GetViewExportAsync(Org, Project, View, "job-1")).ReturnsAsync(states[0]);
            return bodies;
        }

        var reads = _api.SetupSequence(api => api.GetViewExportAsync(Org, Project, View, "job-1"));

        foreach (var state in states)
        {
            reads = reads.ReturnsAsync(state);
        }

        if (error is not null)
        {
            reads.ThrowsAsync(error);
        }

        return bodies;
    }

    private void ServesArchive(byte[] zip)
    {
        _api
            .Setup(api => api.GetViewExportArchiveAsync(Org, Project, View, "job-1"))
            .ReturnsAsync(() => new FileResponse(200, new Dictionary<string, IEnumerable<string>>(), new MemoryStream(zip), null, null));
    }

    private List<Upload> StartsImport(params ViewImport[] states)
    {
        return StartsImport(states, null);
    }

    // The reads of the job return the states, then the last state again, or the error when there is one.
    private List<Upload> StartsImport(ViewImport[] states, ApiException? error)
    {
        var uploads = new List<Upload>();
        _api
            .Setup(api => api.StartViewImportAsync(
                Org,
                Project,
                View,
                It.IsAny<IEnumerable<ViewArchiveContent>>(),
                It.IsAny<ViewImportExisting?>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<string>(),
                It.IsAny<Stream>()))
            .Callback<string, string, string, IEnumerable<ViewArchiveContent>?, ViewImportExisting?, bool?, bool?, string, Stream>(
                (_, _, _, include, existing, force, dryRun, fileName, body) =>
                    uploads.Add(new Upload(include?.ToList(), existing, force, dryRun, fileName, Entries(body))))
            .ReturnsAsync(Import(ViewImportStatus.Queued));

        var reads = _api.SetupSequence(api => api.GetViewImportAsync(Org, Project, View, "job-2"));

        foreach (var state in states)
        {
            reads = reads.ReturnsAsync(state);
        }

        if (error is not null)
        {
            reads.ThrowsAsync(error);
            return uploads;
        }

        // the last state stays, as a job that is still running when the timeout runs out
        reads.ReturnsAsync(states[^1]);
        return uploads;
    }

    private Task<int> ExportAsync(string format = "folder", string[]? include = null, string? output = null, bool extract = false, int timeout = 10)
    {
        var command = new StorytellerExportCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object, _fileSystem, new ImmediatePolling())
        {
            Format = format,
            Include = include,
            Output = output,
            IsExtractRequested = extract,
            TimeoutMinutes = timeout,
        };

        return command.OnExecuteAsync();
    }

    private Task<int> ImportAsync(string path, string[]? include = null, string? existing = null, bool dryRun = false, bool force = false, int timeout = 10)
    {
        var command = new StorytellerImportCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object, _fileSystem, new ImmediatePolling())
        {
            Path = path,
            Include = include,
            Existing = existing,
            IsDryRun = dryRun,
            IsForced = force,
            TimeoutMinutes = timeout,
        };

        return command.OnExecuteAsync();
    }

    private sealed record class Upload(
        List<ViewArchiveContent>? Include,
        ViewImportExisting? Existing,
        bool? Force,
        bool? DryRun,
        string FileName,
        List<string> Entries);

    private sealed class ImmediatePolling : TransferPolling
    {
        protected override Task DelayAsync(TimeSpan delay) => Task.CompletedTask;
    }
}
