using System.IO.Compression;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ConfigurationTransferServiceTests
{
    private readonly ManualTimeProvider _time = new(Start);
    private readonly InMemoryTransferStore _store = new();
    private readonly RecordingTransferQueue _queue = new();
    private readonly FakeConfigurationService _configurations = new();
    private readonly InMemoryArchiveStorage _storage;
    private readonly ConfigurationTransferOptions _options = new();
    private readonly ConfigurationTransferService _service;

    public ConfigurationTransferServiceTests()
    {
        _storage = new InMemoryArchiveStorage(_time);
        _service = new ConfigurationTransferService(
            _store,
            _storage,
            _queue,
            _configurations,
            Options.Create(_options),
            _time,
            NullLogger<ConfigurationTransferService>.Instance);
    }

    [Fact]
    public async Task StartExport_StoresAndQueuesAJob()
    {
        var export = await _service.StartExportAsync("acme", "main", "default", ConfigurationArchiveFormat.Git, Actor);

        export.Status.ShouldBe(ConfigurationTransferStatus.Queued);
        export.Format.ShouldBe(ConfigurationArchiveFormat.Git);
        export.FileName.ShouldBe("configurations-acme-main-default-git-20261001-080000Z.zip");
        export.CreatedAt.ShouldBe(Start);
        export.CreatedBy.ShouldBe(Actor);
        export.ExpiresAt.ShouldBe(Start.AddHours(24));

        _queue.Messages.ShouldBe([new ConfigurationTransferMessage("acme", "main", "default", ConfigurationTransferKind.Export, export.Id)]);
        (await _service.GetExportAsync("acme", "main", "default", export.Id)).ShouldBe(export);
        (await _service.GetExportAsync("acme", "main", "other", export.Id)).ShouldBeNull();
        (await _service.GetImportAsync("acme", "main", "default", export.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Start_RespectsTheLimitOfActiveJobs()
    {
        _store.ActiveCount = _options.MaxActiveTransfers;

        var exception = await Should.ThrowAsync<ConflictException>(
            () => _service.StartExportAsync("acme", "main", "default", ConfigurationArchiveFormat.Folder, Actor));

        exception.ErrorCode.ShouldBe(ErrorCodes.TransferLimit);
        _queue.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_FailsTheJobWhenItCannotBeQueued()
    {
        _queue.Failure = new InvalidOperationException("queue down");

        await Should.ThrowAsync<InvalidOperationException>(
            () => _service.StartExportAsync("acme", "main", "default", ConfigurationArchiveFormat.Folder, Actor));

        var saved = _store.Saves[^1].Export!;
        saved.Status.ShouldBe(ConfigurationTransferStatus.Failed);
        saved.Error.ShouldBe("The job could not be queued; try again.");
    }

    [Fact]
    public async Task ProcessExport_StoresTheArchive()
    {
        _configurations.Histories =
        [
            History("rst.a", true, (1, "a", 0, """{"a":1}"""), (2, "a", 1, """{"a":2}""")),
            History("rst.b", false, (1, "a", 0, """{"b":1}""")),
        ];

        var started = await _service.StartExportAsync("acme", "main", "default", ConfigurationArchiveFormat.Folder, Actor);
        _time.Now = Start.AddMinutes(1);
        await _service.ProcessAsync(_queue.Messages.Single());

        var export = (await _service.GetExportAsync("acme", "main", "default", started.Id))!;
        export.Status.ShouldBe(ConfigurationTransferStatus.Succeeded);
        export.StartedAt.ShouldBe(Start.AddMinutes(1));
        export.CompletedAt.ShouldBe(Start.AddMinutes(1));
        export.ExpiresAt.ShouldBe(Start.AddMinutes(1).AddHours(24));
        export.ConfigurationCount.ShouldBe(1);
        export.VersionCount.ShouldBe(3);

        var (opened, archive) = await _service.OpenExportArchiveAsync("acme", "main", "default", started.Id);
        opened.ShouldBe(export);

        using var buffer = new MemoryStream();
        await archive.CopyToAsync(buffer);
        export.Size.ShouldBe(buffer.Length);
        ReadTextEntries(buffer.ToArray()).Keys.ShouldBe(["rst.a.1.json", "rst.a.json", "rst.b.1.json", "export.json"], ignoreOrder: true);
    }

    [Fact]
    public async Task OpenExportArchive_TellsMissingFromNotReady()
    {
        var export = await _service.StartExportAsync("acme", "main", "default", ConfigurationArchiveFormat.Folder, Actor);

        var notReady = await Should.ThrowAsync<ConflictException>(() => _service.OpenExportArchiveAsync("acme", "main", "default", export.Id));
        notReady.ErrorCode.ShouldBe(ErrorCodes.TransferNotReady);

        await Should.ThrowAsync<NotFoundException>(() => _service.OpenExportArchiveAsync("acme", "main", "default", "unknown"));
    }

    [Fact]
    public async Task ProcessExport_ExceptionFailsTheJob()
    {
        _configurations.Histories = null!;
        var export = await _service.StartExportAsync("acme", "main", "default", ConfigurationArchiveFormat.Folder, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        var failed = (await _service.GetExportAsync("acme", "main", "default", export.Id))!;
        failed.Status.ShouldBe(ConfigurationTransferStatus.Failed);
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
            "rst.created" => new ConfigurationImportResult(ConfigurationImportOutcome.Created, 1),
            "rst.updated" => new ConfigurationImportResult(ConfigurationImportOutcome.Updated, 5),
            "rst.unchanged" => new ConfigurationImportResult(ConfigurationImportOutcome.Unchanged, 3),
            _ => new ConfigurationImportResult(ConfigurationImportOutcome.Skipped, ErrorCode: ErrorCodes.AnnotationNotFound, Message: "missing"),
        };

        using var zip = Zip(
            ("rst.created.json", """{"a":1}"""),
            ("rst.updated.json", """{"a":2}"""),
            ("rst.unchanged.json", """{"a":3}"""),
            ("rst.missing.json", """{"a":4}"""),
            ("rst.broken.json", "[]"),
            ("rst.updated.4.json", "{}"),
            ("export.json", "{}"));

        var started = await _service.StartImportAsync("acme", "main", "default", zip, @"C:\exports\configs.zip", true, false, Actor);
        started.FileName.ShouldBe("configs.zip");
        started.IsForced.ShouldBeTrue();
        started.IsDryRun.ShouldBeFalse();

        await _service.ProcessAsync(_queue.Messages.Single());

        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Status.ShouldBe(ConfigurationTransferStatus.Succeeded);
        import.TotalCount.ShouldBe(5);
        import.ProcessedCount.ShouldBe(5);
        import.Summary.ShouldBe(new ConfigurationImportSummary { Created = 1, Updated = 1, Unchanged = 1, Skipped = 1, Failed = 1, IgnoredFiles = 2 });
        import.Items!.Select(item => (item.AnnotationKey, item.Outcome, item.Version, item.ErrorCode)).ShouldBe(
        [
            ("rst.broken", ConfigurationImportOutcome.Failed, null, ErrorCodes.InvalidDocument),
            ("rst.created", ConfigurationImportOutcome.Created, 1UL, null),
            ("rst.missing", ConfigurationImportOutcome.Skipped, null, ErrorCodes.AnnotationNotFound),
            ("rst.updated", ConfigurationImportOutcome.Updated, 5UL, null),
        ]);
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
        using var zip = Zip(("rst.a.json", """{"a":1}"""));
        await _service.StartImportAsync("acme", "main", "default", zip, null, false, true, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        _configurations.Imports.Single().DryRun.ShouldBeTrue();
        _configurations.Imports.Single().Force.ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessImport_KeepsAtMostTheReportedItems()
    {
        _options.MaxReportedItems = 2;
        using var zip = Zip(("rst.a.json", "1"), ("rst.b.json", "2"), ("rst.c.json", "3"));
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
        using var zip = Zip(("rst.a.json", """{"a":1}"""));
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Status.ShouldBe(ConfigurationTransferStatus.Failed);
        import.Error.ShouldBe("The import failed: The request timed out.");
    }

    [Fact]
    public async Task ProcessImport_SavesProgress()
    {
        var files = Enumerable.Range(0, 120).Select(index => ($"rst.k{index:D3}.json", "{\"a\":1}")).ToArray();
        using var zip = Zip(files);
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        _store.Saves
            .Where(save => save.Id == started.Id)
            .Select(save => (save.Import!.Status, save.Import.ProcessedCount))
            .ShouldBe(
            [
                (ConfigurationTransferStatus.Queued, null),
                (ConfigurationTransferStatus.Running, null),
                (ConfigurationTransferStatus.Running, 0),
                (ConfigurationTransferStatus.Running, 50),
                (ConfigurationTransferStatus.Running, 100),
                (ConfigurationTransferStatus.Succeeded, 120),
            ]);
    }

    [Fact]
    public async Task Process_LeavesAFinishedJobAlone()
    {
        using var zip = Zip(("rst.a.json", """{"a":1}"""));
        await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);
        var message = _queue.Messages.Single();

        await _service.ProcessAsync(message);
        await _service.ProcessAsync(message);

        _configurations.Imports.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Process_IgnoresAnExpiredJob()
    {
        await _service.ProcessAsync(new ConfigurationTransferMessage("acme", "main", "default", ConfigurationTransferKind.Import, "gone"));

        _store.Saves.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessImport_FailsWithoutTheUpload()
    {
        using var zip = Zip(("rst.a.json", """{"a":1}"""));
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, false, false, Actor);
        _storage.Archives.Clear();

        await _service.ProcessAsync(_queue.Messages.Single());

        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Status.ShouldBe(ConfigurationTransferStatus.Failed);
        import.Error.ShouldBe("The import failed: The uploaded archive no longer exists.");
    }

    [Fact]
    public async Task Purge_DeletesArchivesAfterTheRetention()
    {
        using var zip = Zip(("rst.a.json", """{"a":1}"""));
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
        var message = new ConfigurationTransferMessage("acme", "main", "default", ConfigurationTransferKind.Import, "0123");

        message.ToJson().ShouldContain("\"Kind\":\"Import\"");
        ConfigurationTransferMessage.TryParse(message.ToJson(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(message);

        ConfigurationTransferMessage.TryParse("not json", out _).ShouldBeFalse();
        ConfigurationTransferMessage.TryParse("""{"Organization":"acme"}""", out _).ShouldBeFalse();
        ConfigurationTransferMessage.TryParse(null, out _).ShouldBeFalse();
    }

    [Fact]
    public void Zip_HelperWritesAReadableArchive()
    {
        using var zip = Zip(("rst.a.json", "{}"));
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        archive.Entries.Count.ShouldBe(1);
    }
}
