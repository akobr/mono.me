using System.IO.Compression;
using _42.Platform.Storyteller.Configuring;
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
    private readonly ImportLog _imports = new();
    private readonly FakeAnnotationService _annotations;
    private readonly FakeSchemaService _schemas;
    private readonly FakeTemplateService _templates;
    private readonly FakeConfigurationService _configurations;
    private readonly InMemoryArchiveStorage _storage;
    private readonly ViewTransferOptions _options = new();
    private readonly ViewTransferService _service;

    public ViewTransferServiceTests()
    {
        _annotations = new FakeAnnotationService(_reads, _imports);
        _schemas = new FakeSchemaService(_reads, _imports);
        _templates = new FakeTemplateService(_reads, _imports);
        _configurations = new FakeConfigurationService(_reads, _imports);
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
        var export = await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Git, null, Actor);

        export.Status.ShouldBe(ViewTransferStatus.Queued);
        export.Format.ShouldBe(ViewArchiveFormat.Git);
        export.FileName.ShouldBe("export-acme-main-default-git-20261001-080000Z.zip");
        export.CreatedAt.ShouldBe(Start);
        export.CreatedBy.ShouldBe(Actor);
        export.ExpiresAt.ShouldBe(Start.AddHours(24));
        export.Include.ShouldBe(ViewArchiveInclude.All);

        _queue.Messages.ShouldBe([new ViewTransferMessage("acme", "main", "default", ViewTransferKind.Export, export.Id)]);
        (await _service.GetExportAsync("acme", "main", "default", export.Id)).ShouldBe(export);
        (await _service.GetExportAsync("acme", "main", "other", export.Id)).ShouldBeNull();
        (await _service.GetImportAsync("acme", "main", "default", export.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Start_StoresTheIncludeResolvedAndTheExistingOption()
    {
        var export = await _service.StartExportAsync(
            "acme",
            "main",
            "default",
            ViewArchiveFormat.Folder,
            [ViewArchiveContent.Configurations, ViewArchiveContent.Annotations, ViewArchiveContent.Configurations],
            Actor);

        export.Include.ShouldBe([ViewArchiveContent.Annotations, ViewArchiveContent.Configurations]);

        using var zip = Zip(("rst.a.conf.json", """{"a":1}"""));
        var import = await _service.StartImportAsync("acme", "main", "default", zip, null, [], ViewImportExisting.Skip, false, true, Actor);

        import.Include.ShouldBe(ViewArchiveInclude.All);
        import.Existing.ShouldBe(ViewImportExisting.Skip);
        (await _service.GetImportAsync("acme", "main", "default", import.Id)).ShouldBe(import);
    }

    [Fact]
    public async Task Start_RespectsTheLimitOfActiveJobs()
    {
        _store.ActiveCount = _options.MaxActiveTransfers;

        var exception = await Should.ThrowAsync<ConflictException>(
            () => _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, null, Actor));

        exception.ErrorCode.ShouldBe(ErrorCodes.TransferLimit);
        _queue.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_FailsTheJobWhenItCannotBeQueued()
    {
        _queue.Failure = new InvalidOperationException("queue down");

        await Should.ThrowAsync<InvalidOperationException>(
            () => _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, null, Actor));

        var saved = _store.Saves[^1].Export!;
        saved.Status.ShouldBe(ViewTransferStatus.Failed);
        saved.Error.ShouldBe("The job could not be queued; try again.");
    }

    [Fact]
    public async Task ProcessExport_StoresTheArchive()
    {
        SetEveryKind();

        var started = await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, null, Actor);
        _time.Now = Start.AddMinutes(1);
        await _service.ProcessAsync(_queue.Messages.Single());

        var export = (await _service.GetExportAsync("acme", "main", "default", started.Id))!;
        export.Status.ShouldBe(ViewTransferStatus.Succeeded);
        export.StartedAt.ShouldBe(Start.AddMinutes(1));
        export.CompletedAt.ShouldBe(Start.AddMinutes(1));
        export.ExpiresAt.ShouldBe(Start.AddMinutes(1).AddHours(24));
        export.Include.ShouldBe(ViewArchiveInclude.All);
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
        await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Git, null, Actor);
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
        var export = await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, null, Actor);

        var notReady = await Should.ThrowAsync<ConflictException>(() => _service.OpenExportArchiveAsync("acme", "main", "default", export.Id));
        notReady.ErrorCode.ShouldBe(ErrorCodes.TransferNotReady);

        await Should.ThrowAsync<NotFoundException>(() => _service.OpenExportArchiveAsync("acme", "main", "default", "unknown"));
    }

    [Fact]
    public async Task ProcessExport_ExceptionFailsTheJob()
    {
        _configurations.Histories = null!;
        var export = await _service.StartExportAsync("acme", "main", "default", ViewArchiveFormat.Folder, null, Actor);

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
            () => _service.StartImportAsync("acme", "main", "default", body, "configs.zip", null, ViewImportExisting.Overwrite, false, false, Actor));

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

        var started = await _service.StartImportAsync("acme", "main", "default", zip, @"C:\exports\configs.zip", null, ViewImportExisting.Overwrite, true, false, Actor);
        started.FileName.ShouldBe("configs.zip");
        started.IsForced.ShouldBeTrue();
        started.IsDryRun.ShouldBeFalse();

        await _service.ProcessAsync(_queue.Messages.Single());

        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Status.ShouldBe(ViewTransferStatus.Succeeded);
        import.TotalCount.ShouldBe(5);
        import.ProcessedCount.ShouldBe(5);
        import.Summary.ShouldBe(new ViewImportSummary { Created = 1, Updated = 1, Unchanged = 1, Skipped = 1, Failed = 1, Warnings = 1, IgnoredFiles = 2 });
        import.Summaries.ShouldBe(
        [
            new ViewImportSummary { Content = ViewArchiveContent.Annotations },
            new ViewImportSummary { Content = ViewArchiveContent.Schemas },
            new ViewImportSummary { Content = ViewArchiveContent.Templates },
            new ViewImportSummary { Content = ViewArchiveContent.Configurations, Created = 1, Updated = 1, Unchanged = 1, Skipped = 1, Failed = 1, Warnings = 1 },
        ]);
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
        _configurations.Imports.ShouldAllBe(call => call.Options.Force && !call.Options.DryRun && call.Options.Actor == Actor);
        _configurations.Imports.ShouldAllBe(call => call.Options.Existing == ViewImportExisting.Overwrite);
        _configurations.Imports.Select(call => call.Overlay).Distinct().ShouldHaveSingleItem();
        _configurations.Imports[0].Key.ViewName.ShouldBe("default");

        // the upload is removed once the job is done
        _storage.Archives.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessImport_PassesTheDryRun()
    {
        using var zip = Zip(("rst.a.conf.json", """{"a":1}"""));
        await _service.StartImportAsync("acme", "main", "default", zip, null, null, ViewImportExisting.Overwrite, false, true, Actor);

        await _service.ProcessAsync(_queue.Messages.Single());

        _configurations.Imports.Single().Options.DryRun.ShouldBeTrue();
        _configurations.Imports.Single().Options.Force.ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessImport_ImportsThePassesInDependencyOrder()
    {
        // folders don't matter, so the files are spread over the export's folders, foreign ones and the root
        using var zip = Zip(
            ("configurations/rst.r.conf.json", """{"a":1}"""),
            ("exe.s.r.c.conf.json", """{"a":1}"""),
            ("templates/unt.template.json", """{"t":1}"""),
            ("exe.template.json", """{"t":1}"""),
            ("rst.r.schema.json", """{"type":"object"}"""),
            ("x/sbt.s.exe.schema.json", """{"type":"object"}"""),
            ("rst.r.unt.schema.json", """{"type":"object"}"""),
            ("schemas/rst.schema.json", """{"type":"object"}"""),
            ("exe.schema.json", """{"type":"object"}"""),
            ("annotations/exe.s.r.c.json", "{}"),
            ("unt.r.u.json", "{}"),
            ("usg.s.r.json", "{}"),
            ("cnt.s.c.json", "{}"),
            ("sbt.s.json", "{}"),
            ("rst.r.json", "{}"),
            ("x/y/rst.a.json", "{}"));

        var import = await ImportAsync(zip);

        // annotations by depth, type (unit, usage, context) and key, so ancestors come first; type, descendant-type and
        // annotation schemas; templates; configurations
        _imports.Documents.ShouldBe(
        [
            "annotation rst.a",
            "annotation rst.r",
            "annotation sbt.s",
            "annotation unt.r.u",
            "annotation usg.s.r",
            "annotation cnt.s.c",
            "annotation exe.s.r.c",
            "type schema exe",
            "type schema rst",
            "exe schema below sbt.s",
            "unt schema below rst.r",
            "schema of rst.r",
            "template exe",
            "template unt",
            "configuration of exe.s.r.c",
            "configuration of rst.r",
        ]);

        // the items follow the passes, with their kinds
        import.Items!.Select(item => (item.Kind, item.AnnotationType, item.AnnotationKey)).ShouldBe(
        [
            (ViewDocumentKind.Annotation, null, "rst.a"),
            (ViewDocumentKind.Annotation, null, "rst.r"),
            (ViewDocumentKind.Annotation, null, "sbt.s"),
            (ViewDocumentKind.Annotation, null, "unt.r.u"),
            (ViewDocumentKind.Annotation, null, "usg.s.r"),
            (ViewDocumentKind.Annotation, null, "cnt.s.c"),
            (ViewDocumentKind.Annotation, null, "exe.s.r.c"),
            (ViewDocumentKind.TypeSchema, "exe", null),
            (ViewDocumentKind.TypeSchema, "rst", null),
            (ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.s"),
            (ViewDocumentKind.DescendantTypeSchema, "unt", "rst.r"),
            (ViewDocumentKind.AnnotationSchema, null, "rst.r"),
            (ViewDocumentKind.Template, "exe", null),
            (ViewDocumentKind.Template, "unt", null),
            (ViewDocumentKind.Configuration, null, "exe.s.r.c"),
            (ViewDocumentKind.Configuration, null, "rst.r"),
        ]);
        import.Items!.Select(item => item.Path).ShouldContain("x/sbt.s.exe.schema.json");
        import.TotalCount.ShouldBe(16);
        import.ProcessedCount.ShouldBe(16);
    }

    [Fact]
    public async Task ProcessImport_ReportsTheItemsAndASummaryPerKind()
    {
        _annotations.Import = (key, _, _) => key.Annotation.ToString() == "rst.b"
            ? new ViewImportResult(ViewImportOutcome.Unchanged)
            : new ViewImportResult(ViewImportOutcome.Created, Warnings: [new ViewImportWarning { Code = ErrorCodes.AncestorCreated, Message = "ancestors" }]);
        _schemas.Import = (target, _, _) => target.Kind == ViewDocumentKind.TypeSchema
            ? new ViewImportResult(ViewImportOutcome.Updated, 2)
            : new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: ErrorCodes.DocumentExists, Message: "kept");
        _templates.Import = (_, _) => new ViewImportResult(ViewImportOutcome.Unchanged, 4);
        _configurations.Import = (key, _) => key.Annotation.ToString() == "rst.a"
            ? new ViewImportResult(ViewImportOutcome.Created, 1, Warnings: [Warning])
            : new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.SchemaViolation, Message: "violates");

        using var zip = Zip(
            ("annotations/rst.a.json", "{}"),
            ("annotations/rst.b.json", "{}"),
            ("annotations/sbt.bad.json", "[]"),
            ("schemas/exe.schema.json", """{"type":"object"}"""),
            ("schemas/rst.a.schema.json", """{"type":"object"}"""),
            ("templates/unt.template.json", """{"t":1}"""),
            ("configurations/rst.a.conf.json", """{"a":1}"""),
            ("configurations/rst.b.conf.json", """{"a":"text"}"""),
            ("configurations/rst.a.conf.3.json", """{"a":0}"""),
            ("readme.json", "{}"),
            ("export.json", "{}"),
            (".git/config", "[core]"));

        var import = await ImportAsync(zip);

        import.Status.ShouldBe(ViewTransferStatus.Succeeded);
        import.TotalCount.ShouldBe(8);
        import.ProcessedCount.ShouldBe(8);
        import.Items!.Select(item => (item.Path, item.Kind, item.Outcome, item.Version, item.ErrorCode)).ShouldBe(
        [
            ("annotations/rst.a.json", ViewDocumentKind.Annotation, ViewImportOutcome.Created, null, null),
            ("annotations/sbt.bad.json", ViewDocumentKind.Annotation, ViewImportOutcome.Failed, null, ErrorCodes.InvalidDocument),
            ("schemas/exe.schema.json", ViewDocumentKind.TypeSchema, ViewImportOutcome.Updated, 2UL, null),
            ("schemas/rst.a.schema.json", ViewDocumentKind.AnnotationSchema, ViewImportOutcome.Skipped, null, ErrorCodes.DocumentExists),
            ("configurations/rst.a.conf.json", ViewDocumentKind.Configuration, ViewImportOutcome.Created, 1UL, null),
            ("configurations/rst.b.conf.json", ViewDocumentKind.Configuration, ViewImportOutcome.Failed, null, ErrorCodes.SchemaViolation),
        ]);
        import.Items![0].Warnings!.Single().Code.ShouldBe(ErrorCodes.AncestorCreated);
        import.Items![4].Warnings.ShouldBe([Warning]);

        import.Summaries.ShouldBe(
        [
            new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 1, Unchanged = 1, Failed = 1, Warnings = 1 },
            new ViewImportSummary { Content = ViewArchiveContent.Schemas, Updated = 1, Skipped = 1 },
            new ViewImportSummary { Content = ViewArchiveContent.Templates, Unchanged = 1 },
            new ViewImportSummary { Content = ViewArchiveContent.Configurations, Created = 1, Failed = 1, Warnings = 1 },
        ]);

        // the hidden .git folder is not counted; the version file, export.json and readme.json are ignored
        import.Summary.ShouldBe(new ViewImportSummary { Created = 2, Updated = 1, Unchanged = 2, Skipped = 1, Failed = 2, Warnings = 2, IgnoredFiles = 3 });
    }

    [Fact]
    public async Task ProcessImport_ImportsOnlyTheIncludedKinds()
    {
        using var zip = Zip(
            ("annotations/rst.a.json", "{}"),
            ("schemas/exe.schema.json", """{"type":"object"}"""),
            ("schemas/rst.a.schema.json", """{"type":"object"}"""),
            ("templates/unt.template.json", """{"t":1}"""),
            ("templates/unt.template.2.json", """{"t":0}"""),
            ("configurations/rst.a.conf.json", """{"a":1}"""));

        var import = await ImportAsync(zip, [ViewArchiveContent.Configurations, ViewArchiveContent.Templates]);

        import.Include.ShouldBe([ViewArchiveContent.Templates, ViewArchiveContent.Configurations]);
        _imports.Documents.ShouldBe(["template unt", "configuration of rst.a"]);
        import.TotalCount.ShouldBe(2);
        import.Summaries!.Select(summary => (summary.Content, summary.Created)).ShouldBe(
        [
            (ViewArchiveContent.Templates, 1),
            (ViewArchiveContent.Configurations, 1),
        ]);
        import.Summary.ShouldBe(new ViewImportSummary { Created = 2, ExcludedFiles = 3, IgnoredFiles = 1 });
    }

    [Fact]
    public async Task ProcessImport_FillsTheOverlayAfterTheAnnotationPass()
    {
        _annotations.Active = new HashSet<string>(StringComparer.Ordinal) { "rst.stored", "sbt.stored" };
        var readsBeforeAnnotations = new List<int>();
        _annotations.Import = (key, _, overlay) =>
        {
            readsBeforeAnnotations.Add(_annotations.ActiveReads.Count);

            if (key.Annotation.ToString() == "rst.failing")
            {
                return new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.InvalidAnnotation, Message: "rule");
            }

            overlay.AddActiveAnnotation(key.Annotation);
            return new ViewImportResult(ViewImportOutcome.Created);
        };

        using var zip = Zip(
            ("rst.new.json", "{}"),
            ("rst.failing.json", "{}"),
            ("exe.schema.json", """{"type":"object"}"""),
            ("unt.schema.json", "{}"),
            ("usg.schema.json", "[]"),
            ("rst.stored.schema.json", """{"type":"object"}"""),
            ("rst.new.schema.json", """{"type":"object"}"""),
            ("rst.missing.schema.json", """{"type":"object"}"""),
            ("rst.failing.schema.json", """{"type":"object"}"""),
            ("sbt.missing.exe.schema.json", """{"type":"object"}"""),
            ("rst.stored.conf.json", """{"a":1}"""),
            ("rst.new.conf.json", """{"a":1}"""),
            ("rst.missing.conf.json", """{"a":1}"""),
            ("sbt.stored.conf.json", "{}"));

        await ImportAsync(zip);

        // the annotations of the schema and configuration files are read once, before the annotation pass
        _annotations.ActiveReads.ShouldHaveSingleItem().ShouldBe(["rst.failing", "rst.missing", "rst.new", "rst.stored", "sbt.missing"], ignoreOrder: true);
        readsBeforeAnnotations.ShouldBe([1, 1]);

        // Overwrite needs no existence of schemas and configurations
        _schemas.KeyReads.ShouldBe(0);
        _configurations.ListReads.ShouldBeEmpty();

        // not empty, read without an error, and of an annotation that is stored or created by the import
        var seen = _imports.Single("type schema exe").Seen!;
        seen.ActiveAnnotations.ShouldBe(["rst.new"]);
        seen.FailedAnnotations.ShouldBe(["rst.failing"]);
        seen.Schemas.ShouldBe(["schema of rst.new", "schema of rst.stored", "type schema exe"]);
        seen.Configurations.ShouldBe(["rst.new", "rst.stored"]);

        _imports.Calls.Where(call => call.Overlay is not null).Select(call => call.Overlay).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ProcessImport_SkipLeavesTheDocumentsOfTheViewOutOfTheOverlay()
    {
        _annotations.Active = new HashSet<string>(StringComparer.Ordinal) { "rst.a", "rst.b" };
        _annotations.Import = (key, _, overlay) =>
        {
            overlay.AddActiveAnnotation(key.Annotation, isRecreated: true);
            return new ViewImportResult(ViewImportOutcome.Created);
        };
        _schemas.Stored = [SchemaKey.ForType("exe"), SchemaKey.ForAnnotation("rst.a")];
        _configurations.PageSize = 2;
        _configurations.Listed = [("rst.a", true), ("rst.b", false), ("rst.gone", true), ("rst.other", true), ("sbt.x", false)];

        using var zip = Zip(
            ("rst.gone.json", "{}"),
            ("exe.schema.json", """{"type":"object"}"""),
            ("rst.schema.json", """{"type":"object"}"""),
            ("rst.a.schema.json", """{"type":"object"}"""),
            ("rst.b.schema.json", """{"type":"object"}"""),
            ("rst.a.conf.json", """{"a":1}"""),
            ("rst.b.conf.json", """{"a":1}"""),
            ("rst.gone.conf.json", """{"a":1}"""));

        await ImportAsync(zip, existing: ViewImportExisting.Skip);

        // one read per kind; the configuration listing is paged
        _schemas.KeyReads.ShouldBe(1);
        _configurations.ListReads.ShouldBe([null, "2", "4"]);

        // a stored document stays, except the configuration of a re-created annotation, which the re-create empties
        var seen = _imports.Single("type schema exe").Seen!;
        seen.RecreatedAnnotations.ShouldBe(["rst.gone"]);
        seen.Schemas.ShouldBe(["schema of rst.b", "type schema rst"]);
        seen.Configurations.ShouldBe(["rst.b", "rst.gone"]);
    }

    [Fact]
    public async Task ProcessImport_DocumentsThatFailOrAreSkippedLeaveTheOverlay()
    {
        _annotations.Active = new HashSet<string>(StringComparer.Ordinal) { "rst.a", "rst.b" };
        _schemas.Import = (target, _, _) => target.AnnotationType switch
        {
            "exe" => new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.InvalidSchema, Message: "invalid"),
            "rst" => new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: ErrorCodes.DocumentExists, Message: "kept"),
            _ => new ViewImportResult(ViewImportOutcome.Created, 1),
        };
        _configurations.Import = (key, _) => key.Annotation.ToString() == "rst.a"
            ? new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.SchemaViolation, Message: "violates")
            : new ViewImportResult(ViewImportOutcome.Created, 1);

        using var zip = Zip(
            ("exe.schema.json", """{"type":"object"}"""),
            ("rst.schema.json", """{"type":"object"}"""),
            ("unt.schema.json", """{"type":"object"}"""),
            ("rst.a.conf.json", """{"a":1}"""),
            ("rst.b.conf.json", """{"a":1}"""));

        await ImportAsync(zip);

        _imports.Single("type schema exe").Seen!.Schemas.ShouldBe(["type schema exe", "type schema rst", "type schema unt"]);
        _imports.Single("type schema unt").Seen!.Schemas.ShouldBe(["type schema unt"]);
        _imports.Single("configuration of rst.a").Seen!.Configurations.ShouldBe(["rst.a", "rst.b"]);

        var overlay = _imports.Calls[^1].Overlay!;
        overlay.Schemas.Keys.ShouldBe([SchemaKey.ForType("unt")]);
        overlay.Configurations.Keys.ShouldBe(["rst.b"]);
    }

    [Fact]
    public async Task ProcessImport_AFailedAnnotationFailsWhatHangsOnIt()
    {
        _annotations.Import = (key, _, overlay) =>
        {
            switch (key.Annotation.ToString())
            {
                case "rst.fail":
                    return new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.InvalidAnnotation, Message: "rule");
                case "sbt.dep":
                    return new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: ErrorCodes.DependencyFailed, Message: "ancestor");
                case "sbt.kept":
                    return new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: ErrorCodes.DocumentExists, Message: "kept");
                default:
                    overlay.AddActiveAnnotation(key.Annotation);
                    return new ViewImportResult(ViewImportOutcome.Created);
            }
        };

        using var zip = Zip(
            ("rst.bad.json", "[]"),
            ("rst.fail.json", "{}"),
            ("rst.dup.json", "{}"),
            ("x/rst.dup.json", "{}"),
            ("sbt.dep.json", "{}"),
            ("sbt.kept.json", "{}"),
            ("usg.dep.fail.json", "{}"),
            ("rst.bad.conf.json", """{"a":1}"""));

        var import = await ImportAsync(zip);

        // a file that failed while reading, a failed import and a skip for a failed ancestor fail the key; a duplicate
        // and a kept document don't
        _imports.Single("annotation usg.dep.fail").Seen!.FailedAnnotations.ShouldBe(["rst.bad", "rst.fail", "sbt.dep"]);
        _imports.Single("configuration of rst.bad").Seen!.FailedAnnotations.ShouldBe(["rst.bad", "rst.fail", "sbt.dep"]);
        _imports.Single("configuration of rst.bad").Seen!.Configurations.ShouldBeEmpty();

        // the files that failed while reading are reported in the annotation pass, in its order
        import.Items!.Where(item => item.Kind == ViewDocumentKind.Annotation).Select(item => (item.Path, item.Outcome, item.ErrorCode)).ShouldBe(
        [
            ("rst.bad.json", ViewImportOutcome.Failed, ErrorCodes.InvalidDocument),
            ("rst.dup.json", ViewImportOutcome.Created, null),
            ("x/rst.dup.json", ViewImportOutcome.Failed, ErrorCodes.DuplicateFile),
            ("rst.fail.json", ViewImportOutcome.Failed, ErrorCodes.InvalidAnnotation),
            ("sbt.dep.json", ViewImportOutcome.Skipped, ErrorCodes.DependencyFailed),
            ("sbt.kept.json", ViewImportOutcome.Skipped, ErrorCodes.DocumentExists),
            ("usg.dep.fail.json", ViewImportOutcome.Created, null),
        ]);
    }

    [Fact]
    public async Task ProcessImport_WriteConflictsFailTheirFileNotTheJob()
    {
        _annotations.Import = (key, _, overlay) =>
        {
            if (key.Annotation.ToString() == "rst.busy")
            {
                throw new ConflictException("The annotations changed concurrently; try again.");
            }

            overlay.AddActiveAnnotation(key.Annotation);
            return new ViewImportResult(ViewImportOutcome.Created);
        };
        _schemas.Import = (_, _, _) => throw new SchemaConcurrencyException("main", "default", "default.cfs.t.exe", 3);
        _templates.Import = (_, _) => throw new TemplateConcurrencyException("main", "default", "exe", 3);
        _configurations.Import = (key, _) => key.Annotation.ToString() == "rst.a"
            ? throw new ConflictException("The configuration 'rst.a' was changed concurrently, try again.")
            : new ViewImportResult(ViewImportOutcome.Created, 1);

        using var zip = Zip(
            ("rst.busy.json", "{}"),
            ("rst.a.json", "{}"),
            ("exe.schema.json", """{"type":"object"}"""),
            ("exe.template.json", """{"t":1}"""),
            ("rst.a.conf.json", """{"a":1}"""),
            ("rst.busy.conf.json", """{"b":1}"""));

        var import = await ImportAsync(zip);

        import.Status.ShouldBe(ViewTransferStatus.Succeeded);
        import.Items!.Select(item => (item.Path, item.Outcome, item.ErrorCode)).ShouldBe(
        [
            ("rst.a.json", ViewImportOutcome.Created, null),
            ("rst.busy.json", ViewImportOutcome.Failed, ErrorCodes.Conflict),
            ("exe.schema.json", ViewImportOutcome.Failed, ErrorCodes.Conflict),
            ("exe.template.json", ViewImportOutcome.Failed, ErrorCodes.Conflict),
            ("rst.a.conf.json", ViewImportOutcome.Failed, ErrorCodes.Conflict),
            ("rst.busy.conf.json", ViewImportOutcome.Created, null),
        ]);

        // the annotation that failed on the conflict counts as failed for the documents that hang on it
        _imports.Single("configuration of rst.busy").Seen!.FailedAnnotations.ShouldBe(["rst.busy"]);
        import.Summary!.Failed.ShouldBe(4);
    }

    [Fact]
    public async Task ProcessImport_OtherConflictsStillFailTheJob()
    {
        _templates.Import = (_, _) => throw new ConflictException("The view always exists.", ErrorCodes.ViewExists);

        using var zip = Zip(("exe.template.json", """{"t":1}"""));

        var import = await ImportAsync(zip);

        import.Status.ShouldBe(ViewTransferStatus.Failed);
    }

    [Fact]
    public async Task ProcessImport_PassesTheOptionsAndOneOverlayToEveryKind()
    {
        using var zip = Zip(
            ("rst.a.json", "{}"),
            ("rst.a.schema.json", """{"type":"object"}"""),
            ("exe.template.json", """{"t":1}"""),
            ("rst.a.conf.json", """{"a":1}"""));

        await ImportAsync(zip, existing: ViewImportExisting.Skip, force: true, dryRun: true);

        _imports.Documents.ShouldBe(["annotation rst.a", "schema of rst.a", "template exe", "configuration of rst.a"]);
        _imports.Calls.Select(call => call.Options).Distinct().ShouldHaveSingleItem().ShouldBe(
            new ViewImportOptions { Actor = Actor, Existing = ViewImportExisting.Skip, Force = true, DryRun = true });
        _imports.Calls.Where(call => call.Overlay is not null).Select(call => call.Overlay).Distinct().ShouldHaveSingleItem();
        _imports.Single("template exe").Overlay.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessImport_RunsAnInterruptedJobAgainFromTheStart()
    {
        using var zip = Zip(("rst.a.json", "{}"), ("rst.a.conf.json", """{"a":1}"""));
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, null, ViewImportExisting.Overwrite, false, false, Actor);
        var message = _queue.Messages.Single();

        // a first invocation was cut off after the annotation: the job stays running with its progress
        var record = (await _store.GetAsync("acme", "main", "default", ViewTransferKind.Import, started.Id))!;
        await _store.TryReplaceAsync(record with { Import = record.Import! with { Status = ViewTransferStatus.Running, StartedAt = Start, ProcessedCount = 1 } });
        _annotations.Active.Add("rst.a");
        _configurations.Import = (_, _) => new ViewImportResult(ViewImportOutcome.Unchanged, 1);

        await _service.ProcessAsync(message);

        // the documents of the first run are unchanged now
        var import = (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
        import.Status.ShouldBe(ViewTransferStatus.Succeeded);
        import.ProcessedCount.ShouldBe(2);
        import.Summary.ShouldBe(new ViewImportSummary { Unchanged = 2 });
        import.Items.ShouldBeEmpty();
        _storage.Archives.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessImport_KeepsAtMostTheReportedItems()
    {
        _options.MaxReportedItems = 2;
        using var zip = Zip(("rst.a.conf.json", "1"), ("rst.b.conf.json", "2"), ("rst.c.conf.json", "3"));
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, null, ViewImportExisting.Overwrite, false, false, Actor);

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
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, null, ViewImportExisting.Overwrite, false, false, Actor);

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
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, null, ViewImportExisting.Overwrite, false, false, Actor);

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
        await _service.StartImportAsync("acme", "main", "default", zip, null, null, ViewImportExisting.Overwrite, false, false, Actor);
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
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, null, ViewImportExisting.Overwrite, false, false, Actor);
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
        await _service.StartImportAsync("acme", "main", "default", zip, null, null, ViewImportExisting.Overwrite, false, false, Actor);

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

    // Imports the zip into "acme.main.default" and returns the finished job.
    private async Task<ViewImport> ImportAsync(
        MemoryStream zip,
        IReadOnlyList<ViewArchiveContent>? include = null,
        ViewImportExisting existing = ViewImportExisting.Overwrite,
        bool force = false,
        bool dryRun = false)
    {
        var started = await _service.StartImportAsync("acme", "main", "default", zip, null, include, existing, force, dryRun, Actor);
        await _service.ProcessAsync(_queue.Messages.Single());
        return (await _service.GetImportAsync("acme", "main", "default", started.Id))!;
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
