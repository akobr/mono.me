using System.IO.Compression;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewTransferServiceTests
{
    private static readonly ViewImportWarning Warning = new() { Code = ErrorCodes.SchemaViolation, Message = "/a: StringExpected" };

    private readonly ManualTimeProvider _time = new(Start);
    private readonly InMemoryTransferStore _store = new();
    private readonly RecordingTransferQueue _queue = new();
    private readonly ExportReads _reads = new();
    private readonly FakeAnnotationService _annotations;
    private readonly FakeSchemaService _schemas;
    private readonly FakeTemplateService _templates;
    private readonly FakeConfigurationService _configurations;
    private readonly InMemoryArchiveStorage _storage;
    private readonly ViewTransferOptions _options = new();
    private readonly ViewTransferService _service;

    public ViewTransferServiceTests()
    {
        _annotations = new FakeAnnotationService(_reads);
        _schemas = new FakeSchemaService(_reads);
        _templates = new FakeTemplateService(_reads);
        _configurations = new FakeConfigurationService(_reads);
        _storage = new InMemoryArchiveStorage(_time);
        _service = new ViewTransferService(
            _store,
            _storage,
            _queue,
            _annotations,
            _schemas,
            _templates,
            _configurations,
            Options.Create(_options),
            _time,
            NullLogger<ViewTransferService>.Instance);
    }

    [Fact]
    public async Task StartExport_StoresAndQueuesAJob()
    {
        var export = await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Git, Actor);

        export.Status.ShouldBe(ViewTransferStatus.Queued);
        export.Format.ShouldBe(ViewArchiveFormat.Git);
        export.FileName.ShouldBe("export-acme-main-default-git-20261001-080000Z.zip");
        export.CreatedAt.ShouldBe(Start);
        export.CreatedBy.ShouldBe(Actor);
        export.ExpiresAt.ShouldBe(Start.AddHours(24));

        _queue.Messages.ShouldBe([new ViewTransferMessage("acme", "main", "default", ViewTransferKind.Export, export.Id)]);
        (await _service.GetExportAsync("acme", "main", "default", export.Id)).ShouldBe(export);
        (await _service.GetExportAsync("acme", "main", "other", export.Id)).ShouldBeNull();
        (await _service.GetImportAsync("acme", "main", "default", export.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Start_RespectsTheLimitOfActiveJobs()
    {
        _store.ActiveCount = _options.MaxActiveTransfers;

        var exception = await Should.ThrowAsync<ConflictException>(
            () => _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, Actor));

        exception.ErrorCode.ShouldBe(ErrorCodes.TransferLimit);
        _queue.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_FailsTheJobWhenItCannotBeQueued()
    {
        _queue.Failure = new InvalidOperationException("queue down");

        await Should.ThrowAsync<InvalidOperationException>(
            () => _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, Actor));

        var saved = _store.Saves[^1].Export!;
        saved.Status.ShouldBe(ViewTransferStatus.Failed);
        saved.Error.ShouldBe("The job could not be queued; try again.");
    }

    [Fact]
    public async Task ProcessExport_StoresTheArchive()
    {
        SetEveryKind();

        var started = await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, Actor);
        _time.Now = Start.AddMinutes(1);
        await _service.ProcessAsync(_queue.Messages.Single());

        var export = (await _service.GetExportAsync("acme", "main", "default", started.Id))!;
        export.Status.ShouldBe(ViewTransferStatus.Succeeded);
        export.StartedAt.ShouldBe(Start.AddMinutes(1));
        export.CompletedAt.ShouldBe(Start.AddMinutes(1));
        export.ExpiresAt.ShouldBe(Start.AddMinutes(1).AddHours(24));
        export.Include.ShouldBeNull();
        export.AnnotationCount.ShouldBe(2);
        export.SchemaCount.ShouldBe(1);
        export.TemplateCount.ShouldBe(0);
        export.ConfigurationCount.ShouldBe(1);

        // schemas 2 + templates 1 + configurations 3; annotations have no versions
        export.VersionCount.ShouldBe(6);

        var (opened, archive) = await _service.OpenExportArchiveAsync("acme", "main", "default", started.Id);
        opened.ShouldBe(export);

        using var buffer = new MemoryStream();
        await archive.CopyToAsync(buffer);
        export.Size.ShouldBe(buffer.Length);
        ReadTextEntries(buffer.ToArray()).Keys.ShouldBe(
            [
                "annotations/rst.a.json",
                "annotations/rst.b.json",
                "schemas/exe.schema.1.json",
                "schemas/exe.schema.json",
                "templates/unt.template.1.json",
                "configurations/rst.a.conf.1.json",
                "configurations/rst.a.conf.json",
                "configurations/rst.b.conf.1.json",
                "export.json",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task ProcessExport_ReadsTheKindsInReverseDependencyOrder()
    {
        await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Git, Actor);
        await _service.ProcessAsync(_queue.Messages.Single());

        // a document read first still finds its dependency in a later read
        _reads.Reads.ShouldBe(
        [
            (ViewArchiveContent.Configurations, "acme", "main", "default"),
            (ViewArchiveContent.Templates, "acme", "main", "default"),
            (ViewArchiveContent.Schemas, "acme", "main", "default"),
            (ViewArchiveContent.Annotations, "acme", "main", "default"),
        ]);
    }

    [Fact]
    public async Task ProcessExport_ReadsAndCountsOnlyTheIncludedKinds()
    {
        SetEveryKind();
        var message = await QueueExportAsync([ViewArchiveContent.Templates, ViewArchiveContent.Annotations]);

        await _service.ProcessAsync(message);

        _reads.Reads.Select(read => read.Content).ShouldBe([ViewArchiveContent.Templates, ViewArchiveContent.Annotations]);

        var export = (await _service.GetExportAsync("acme", "main", "default", message.Id))!;
        export.Status.ShouldBe(ViewTransferStatus.Succeeded);
        export.Include.ShouldBe([ViewArchiveContent.Templates, ViewArchiveContent.Annotations]);
        export.AnnotationCount.ShouldBe(2);
        export.TemplateCount.ShouldBe(0);
        export.SchemaCount.ShouldBeNull();
        export.ConfigurationCount.ShouldBeNull();
        export.VersionCount.ShouldBe(1);

        var (_, archive) = await _service.OpenExportArchiveAsync("acme", "main", "default", message.Id);
        using var buffer = new MemoryStream();
        await archive.CopyToAsync(buffer);
        var entries = ReadTextEntries(buffer.ToArray());
        entries.Keys.ShouldBe(["annotations/rst.a.json", "annotations/rst.b.json", "templates/unt.template.1.json", "export.json"], ignoreOrder: true);
        entries["export.json"].ShouldContain("\"Include\": [\n    \"Annotations\",\n    \"Templates\"\n  ]");
    }

    [Fact]
    public async Task OpenExportArchive_TellsMissingFromNotReady()
    {
        var export = await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, Actor);

        var notReady = await Should.ThrowAsync<ConflictException>(() => _service.OpenExportArchiveAsync("acme", "main", "default", export.Id));
        notReady.ErrorCode.ShouldBe(ErrorCodes.TransferNotReady);

        await Should.ThrowAsync<NotFoundException>(() => _service.OpenExportArchiveAsync("acme", "main", "default", "unknown"));
    }

    [Fact]
    public async Task ProcessExport_ExceptionFailsTheJob()
    {
        _configurations.Histories = null!;
        var export = await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        var failed = (await _service.GetExportAsync("acme", "main", "default", export.Id))!;
        failed.Status.ShouldBe(ViewTransferStatus.Failed);
        failed.Error.ShouldStartWith("The export failed: ");
        failed.CompletedAt.ShouldBe(Start);
        _storage.Archives.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartImport_RejectsAnInvalidArchiveBeforeStoringIt()
    {
        using var body = new MemoryStream("not a zip"u8.ToArray());

        var exception = await Should.ThrowAsync<InvalidInputException>(
            () => _service.StartImportAsync("acme", "main", "default", body, "configs.zip", false, false, Actor));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
        _storage.Archives.ShouldBeEmpty();
        _store.Saves.ShouldBeEmpty();
        _queue.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessImport_ReportsEveryOutcome()
    {
        _configurations.Import = (key, _) => key.Annotation.ToString() switch
        {
            "rst.created" => new ViewImportResult(ViewImportOutcome.Created, 1),
            "rst.updated" => new ViewImportResult(ViewImportOutcome.Updated, 5, Warnings: [Warning]),
            "rst.unchanged" => new ViewImportResult(ViewImportOutcome.Unchanged, 3),
            _ => new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: ErrorCodes.AnnotationNotFound, Message: "missing"),
        };

        using var zip = Zip(
            ("rst.created.conf.json", """{"a":1}"""),
            ("rst.updated.conf.json", """{"a":2}"""),
            ("rst.unchanged.conf.json", """{"a":3}"""),
            ("rst.missing.conf.json", """{"a":4}"""),
            ("rst.broken.conf.json", "[]"),
            ("rst.updated.conf.4.json", "{}"),
            ("export.json", "{}"));

        var started = await _service.StartImportAsync("acme", "main", "default", zip, @"C:\exports\configs.zip", true, false, Actor);
        started.FileName.ShouldBe("configs.zip");
        started.IsForced.ShouldBeTrue();
        started.IsDryRun.ShouldBeFalse();

        await _service.ProcessAsync(_queue.Messages.Single());

        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Status.ShouldBe(ViewTransferStatus.Succeeded);
        import.TotalCount.ShouldBe(5);
        import.ProcessedCount.ShouldBe(5);
        import.Summary.ShouldBe(new ViewImportSummary { Created = 1, Updated = 1, Unchanged = 1, Skipped = 1, Failed = 1, Warnings = 1, IgnoredFiles = 2 });
        import.Items!.Select(item => (item.AnnotationKey, item.Outcome, item.Version, item.ErrorCode)).ShouldBe(
        [
            ("rst.broken", ViewImportOutcome.Failed, null, ErrorCodes.InvalidDocument),
            ("rst.created", ViewImportOutcome.Created, 1UL, null),
            ("rst.missing", ViewImportOutcome.Skipped, null, ErrorCodes.AnnotationNotFound),
            ("rst.updated", ViewImportOutcome.Updated, 5UL, null),
        ]);
        import.Items!.ShouldAllBe(item => item.Kind == ViewDocumentKind.Configuration && item.AnnotationType == null);
        import.Items!.Single(item => item.AnnotationKey == "rst.updated").Warnings.ShouldBe([Warning]);
        import.Items!.Single(item => item.AnnotationKey == "rst.created").Warnings.ShouldBeNull();
        import.AreItemsTruncated.ShouldBeFalse();

        // the invalid file never reaches the configurations
        _configurations.Imports.Select(call => call.Key.Annotation.ToString()).ShouldBe(["rst.created", "rst.missing", "rst.unchanged", "rst.updated"]);
        _configurations.Imports.ShouldAllBe(call => call.Force && !call.DryRun && call.Actor == Actor);
        _configurations.Imports[0].Key.ViewName.ShouldBe("default");

        // the upload is removed once the job is done
        _storage.Archives.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessImport_PassesTheDryRun()
    {
        using var zip = Zip(("rst.a.conf.json", """{"a":1}"""));
        await _service.StartImportAsync("acme", "main", "default", zip, null, false, true, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        _configurations.Imports.Single().DryRun.ShouldBeTrue();
        _configurations.Imports.Single().Force.ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessImport_KeepsAtMostTheReportedItems()
    {
        _options.MaxReportedItems = 2;
        using var zip = Zip(("rst.a.conf.json", "1"), ("rst.b.conf.json", "2"), ("rst.c.conf.json", "3"));
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Summary!.Failed.ShouldBe(3);
        import.Items!.Count.ShouldBe(2);
        import.AreItemsTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task ProcessImport_TimeoutOfACallFailsTheJob()
    {
        _configurations.Import = (_, _) => throw new TaskCanceledException("The request timed out.");
        using var zip = Zip(("rst.a.conf.json", """{"a":1}"""));
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Status.ShouldBe(ViewTransferStatus.Failed);
        import.Error.ShouldBe("The import failed: The request timed out.");
    }

    [Fact]
    public async Task ProcessImport_SavesProgress()
    {
        var files = Enumerable.Range(0, 120).Select(index => ($"rst.k{index:D3}.conf.json", "{\"a\":1}")).ToArray();
        using var zip = Zip(files);
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        _store.Saves
            .Where(save => save.Id == started.Id)
            .Select(save => (save.Import!.Status, save.Import.ProcessedCount))
            .ShouldBe(
            [
                (ViewTransferStatus.Queued, null),
                (ViewTransferStatus.Running, null),
                (ViewTransferStatus.Running, 0),
                (ViewTransferStatus.Running, 50),
                (ViewTransferStatus.Running, 100),
                (ViewTransferStatus.Succeeded, 120),
            ]);
    }

    [Fact]
    public async Task Process_LeavesAFinishedJobAlone()
    {
        using var zip = Zip(("rst.a.conf.json", """{"a":1}"""));
        await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);
        var message = _queue.Messages.Single();

        await _service.ProcessAsync(message);
        await _service.ProcessAsync(message);

        _configurations.Imports.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Process_IgnoresAnExpiredJob()
    {
        await _service.ProcessAsync(new ViewTransferMessage("acme", "main", "default", ViewTransferKind.Import, "gone"));

        _store.Saves.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessImport_FailsWithoutTheUpload()
    {
        using var zip = Zip(("rst.a.conf.json", """{"a":1}"""));
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);
        _storage.Archives.Clear();

        await _service.ProcessAsync(_queue.Messages.Single());

        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Status.ShouldBe(ViewTransferStatus.Failed);
        import.Error.ShouldBe("The import failed: The uploaded archive no longer exists.");
    }

    [Fact]
    public async Task Purge_DeletesArchivesAfterTheRetention()
    {
        using var zip = Zip(("rst.a.conf.json", """{"a":1}"""));
        await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);

        _time.Now = Start.AddHours(24);
        (await _service.PurgeExpiredAsync()).ShouldBe(0);

        _time.Now = Start.AddHours(25).AddSeconds(1);
        (await _service.PurgeExpiredAsync()).ShouldBe(1);
        _storage.Archives.ShouldBeEmpty();
    }

    [Fact]
    public void Message_RoundTripsThroughJson()
    {
        var message = new ViewTransferMessage("acme", "main", "default", ViewTransferKind.Import, "0123");

        message.ToJson().ShouldContain("\"Kind\":\"Import\"");
        ViewTransferMessage.TryParse(message.ToJson(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(message);

        ViewTransferMessage.TryParse("not json", out _).ShouldBeFalse();
        ViewTransferMessage.TryParse("""{"Organization":"acme"}""", out _).ShouldBeFalse();
        ViewTransferMessage.TryParse(null, out _).ShouldBeFalse();
    }

    [Fact]
    public void Zip_HelperWritesAReadableArchive()
    {
        using var zip = Zip(("rst.a.conf.json", "{}"));
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        archive.Entries.Count.ShouldBe(1);
    }

    // Two annotations, a type schema with two versions, a deleted template and two configurations, one cleared.
    private void SetEveryKind()
    {
        _annotations.Annotations = [ActiveAnnotation("rst.a"), ActiveAnnotation("rst.b")];
        _schemas.Histories = [Schema(ViewDocumentKind.TypeSchema, "exe", null, true, (1, "a", 0, """{"type":"object"}"""), (2, "a", 1, """{"type":"object","required":["a"]}"""))];
        _templates.Histories = [Template("unt", false, (1, "a", 0, """{"t":1}"""))];
        _configurations.Histories =
        [
            History("rst.a", true, (1, "a", 0, """{"a":1}"""), (2, "a", 1, """{"a":2}""")),
            History("rst.b", false, (1, "a", 0, """{"b":1}""")),
        ];
    }

    // An export job of the view "acme.main.default" with the include, queued without StartExportAsync.
    private async Task<ViewTransferMessage> QueueExportAsync(IReadOnlyList<ViewArchiveContent> include)
    {
        var id = Guid.NewGuid().ToString("N");
        var record = await _store.CreateAsync(new ViewTransferRecord
        {
            Organization = "acme",
            Project = "main",
            View = "default",
            Kind = ViewTransferKind.Export,
            Id = id,
            Export = new ViewExport
            {
                Id = id,
                Status = ViewTransferStatus.Queued,
                Format = ViewArchiveFormat.Folder,
                Include = include,
                FileName = "export.zip",
                CreatedAt = Start,
                CreatedBy = Actor,
                ExpiresAt = Start.AddHours(24),
            },
        });

        return record.ToMessage();
    }
}
