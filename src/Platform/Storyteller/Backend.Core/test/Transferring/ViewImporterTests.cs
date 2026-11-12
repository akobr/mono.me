using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewImporterTests
{
    private static readonly ViewTransferOptions ReaderOptions = new();

    private readonly ImportLog _imports = new();
    private readonly FakeAnnotationService _annotations;
    private readonly FakeSchemaService _schemas;
    private readonly FakeTemplateService _templates;
    private readonly FakeConfigurationService _configurations;
    private readonly ViewImporter _importer;

    public ViewImporterTests()
    {
        _annotations = new FakeAnnotationService(imports: _imports);
        _schemas = new FakeSchemaService(imports: _imports);
        _templates = new FakeTemplateService(imports: _imports);
        _configurations = new FakeConfigurationService(imports: _imports);
        _importer = new ViewImporter(_annotations, _schemas, _templates, _configurations, NullLogger<ViewImporter>.Instance);
    }

    [Fact]
    public async Task Import_TakesTheFilesInAnyOrderInThePassesOfAnImport()
    {
        var read = Files(
            ("configurations/rst.r.conf.json", """{"a":1}"""),
            ("exe.template.json", """{"t":1}"""),
            ("rst.r.schema.json", """{"type":"object"}"""),
            ("exe.schema.json", """{"type":"object"}"""),
            ("annotations/exe.s.r.c.json", "{}"),
            ("unt.r.u.json", "{}"),
            ("sbt.s.json", "{}"),
            ("rst.r.json", "{}"));

        // a file made in memory, as a generator makes it, takes its place among the read ones
        var made = new ViewArchiveFile
        {
            Path = "generated/sbt.made",
            Kind = ViewDocumentKind.Annotation,
            AnnotationKey = AnnotationKey.Parse("sbt.made"),
            Content = new JObject(),
        };

        var forward = await ImportAsync([.. read, made]);
        var documents = _imports.Documents.ToList();
        _imports.Calls.Clear();
        var backward = await ImportAsync([made, .. Enumerable.Reverse(read)]);

        documents.ShouldBe(
        [
            "annotation rst.r",
            "annotation sbt.made",
            "annotation sbt.s",
            "annotation unt.r.u",
            "annotation exe.s.r.c",
            "type schema exe",
            "schema of rst.r",
            "template exe",
            "configuration of rst.r",
        ]);
        _imports.Documents.ShouldBe(documents);

        // the outcomes come in the same order, each with its file
        forward.Select(result => result.File.Path).ShouldBe(
        [
            "rst.r.json",
            "generated/sbt.made",
            "sbt.s.json",
            "unt.r.u.json",
            "annotations/exe.s.r.c.json",
            "exe.schema.json",
            "rst.r.schema.json",
            "exe.template.json",
            "configurations/rst.r.conf.json",
        ]);
        backward.ShouldBe(forward);
        forward.Select(result => result.Result.Outcome).ShouldAllBe(outcome => outcome == ViewImportOutcome.Created);
    }

    [Fact]
    public async Task Import_ReadsEachKindOnceBeforeTheAnnotationPassAndNothingBeforeTheEnumeration()
    {
        _annotations.Active = new HashSet<string>(StringComparer.Ordinal) { "rst.a" };
        _schemas.Stored = [SchemaKey.ForType("exe")];
        _configurations.PageSize = 1;
        _configurations.Listed = [("rst.a", true), ("rst.b", true)];
        var readsBeforeAnnotations = new List<(int Annotations, int Schemas, int Pages)>();
        _annotations.Import = (key, _, overlay) =>
        {
            readsBeforeAnnotations.Add((_annotations.ActiveReads.Count, _schemas.KeyReads, _configurations.ListReads.Count));
            overlay.AddActiveAnnotation(key.Annotation);
            return new ViewImportResult(ViewImportOutcome.Created);
        };

        var files = Files(
            ("rst.b.json", "{}"),
            ("rst.c.json", "{}"),
            ("exe.schema.json", """{"type":"object"}"""),
            ("rst.a.schema.json", """{"type":"object"}"""),
            ("rst.a.conf.json", """{"a":1}"""),
            ("rst.b.conf.json", """{"a":1}"""),
            ("rst.c.conf.json", """{"a":1}"""));

        var results = _importer.ImportAsync("acme", "main", "default", files, Options(ViewImportExisting.Skip), cancellationToken: TestContext.Current.CancellationToken);

        _annotations.ActiveReads.ShouldBeEmpty();
        _imports.Calls.ShouldBeEmpty();

        await foreach (var _ in results)
        {
        }

        _annotations.ActiveReads.ShouldHaveSingleItem().ShouldBe(["rst.a", "rst.b", "rst.c"], ignoreOrder: true);
        _schemas.KeyReads.ShouldBe(1);
        _configurations.ListReads.ShouldBe([null, "1"]);
        readsBeforeAnnotations.ShouldBe([(1, 1, 2), (1, 1, 2)]);

        // a stored document stays out of the overlay with Skip
        var seen = _imports.Single("type schema exe").Seen!;
        seen.Schemas.ShouldBe(["schema of rst.a"]);
        seen.Configurations.ShouldBe(["rst.c"]);
    }

    [Fact]
    public async Task Import_FailsTheFilesWithAnErrorWithoutACallAndTracksEachOutcomeBeforeItComes()
    {
        var read = Files(
            ("rst.bad.json", "[]"),
            ("rst.dup.json", "{}"),
            ("x/rst.dup.json", "{}"),
            ("rst.bad.conf.json", """{"a":1}"""),
            ("rst.made.conf.json", """{"a":1}"""));
        var made = new ViewArchiveFile
        {
            Path = "generated/rst.made",
            Kind = ViewDocumentKind.Annotation,
            AnnotationKey = AnnotationKey.Parse("rst.made"),
            ErrorCode = ErrorCodes.InvalidAnnotation,
            Message = "The title is too long.",
        };
        var overlay = new ViewImportOverlay();
        var results = new List<(ViewImportFileResult Result, string Failed)>();

        // the failed annotations as the overlay holds them when each outcome comes
        await foreach (var result in _importer.ImportAsync("acme", "main", "default", [.. read, made], Options(), overlay, TestContext.Current.CancellationToken))
        {
            results.Add((result, string.Join(' ', overlay.FailedAnnotations.Order(StringComparer.Ordinal))));
        }

        results.Select(entry => (entry.Result.File.Path, entry.Result.Result.Outcome, entry.Result.Result.ErrorCode, entry.Failed)).ShouldBe(
        [
            ("rst.bad.json", ViewImportOutcome.Failed, ErrorCodes.InvalidDocument, "rst.bad"),
            ("rst.dup.json", ViewImportOutcome.Created, null, "rst.bad"),
            ("x/rst.dup.json", ViewImportOutcome.Failed, ErrorCodes.DuplicateFile, "rst.bad"),
            ("generated/rst.made", ViewImportOutcome.Failed, ErrorCodes.InvalidAnnotation, "rst.bad rst.made"),
            ("rst.bad.conf.json", ViewImportOutcome.Created, null, "rst.bad rst.made"),
            ("rst.made.conf.json", ViewImportOutcome.Created, null, "rst.bad rst.made"),
        ]);
        results[3].Result.Result.Message.ShouldBe("The title is too long.");
        results[0].Result.Result.Message!.ShouldStartWith("The file is not a JSON object: ");

        // the failed files reach no service; the configurations of failed annotations do, and the overlay tells them
        _imports.Documents.ShouldBe(["annotation rst.dup", "configuration of rst.bad", "configuration of rst.made"]);
        _imports.Single("configuration of rst.bad").Seen!.FailedAnnotations.ShouldBe(["rst.bad", "rst.made"]);
        _imports.Single("configuration of rst.bad").Seen!.Configurations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_SharesAGivenOverlayOverSequentialCalls()
    {
        _annotations.Import = (key, _, overlay) =>
        {
            if (key.Annotation.ToString() == "rst.fail")
            {
                return new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.InvalidAnnotation, Message: "rule");
            }

            // a dry run writes nothing: the key is active in the overlay only
            overlay.AddActiveAnnotation(key.Annotation);
            return new ViewImportResult(ViewImportOutcome.Created);
        };
        var overlay = new ViewImportOverlay();

        await ImportAsync(Files(("rst.new.json", "{}"), ("rst.fail.json", "{}")), overlay: overlay, dryRun: true);

        overlay.ActiveAnnotations.ShouldBe(["rst.new"]);
        overlay.FailedAnnotations.ShouldBe(["rst.fail"]);

        var documents = Files(
            ("rst.new.schema.json", """{"type":"object"}"""),
            ("rst.new.conf.json", """{"a":1}"""),
            ("rst.fail.conf.json", """{"a":1}"""));

        await ImportAsync(documents, overlay: overlay, dryRun: true);

        // the second call knows what the first one made active or failed, though the view has none of it stored
        var seen = _imports.Single("schema of rst.new").Seen!;
        seen.ActiveAnnotations.ShouldBe(["rst.new"]);
        seen.FailedAnnotations.ShouldBe(["rst.fail"]);
        seen.Schemas.ShouldBe(["schema of rst.new"]);
        seen.Configurations.ShouldBe(["rst.new"]);
        _imports.Calls.Where(call => call.Overlay is not null).ShouldAllBe(call => call.Overlay == overlay);
        overlay.Configurations.Keys.ShouldBe(["rst.new"]);

        // without an overlay, a call starts from its own empty one
        _imports.Calls.Clear();
        await ImportAsync(Files(("rst.new.conf.json", """{"a":1}""")), dryRun: true);

        var own = _imports.Single("configuration of rst.new");
        own.Overlay.ShouldNotBeSameAs(overlay);
        own.Seen!.ActiveAnnotations.ShouldBeEmpty();
        own.Seen.Configurations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_StopsWithTheEnumeration()
    {
        var files = Files(("rst.a.json", "{}"), ("rst.b.json", "{}"), ("rst.c.json", "{}"), ("rst.a.conf.json", """{"a":1}"""));

        await foreach (var result in _importer.ImportAsync("acme", "main", "default", files, Options(), cancellationToken: TestContext.Current.CancellationToken))
        {
            result.File.Path.ShouldBe("rst.a.json");
            break;
        }

        _imports.Documents.ShouldBe(["annotation rst.a"]);

        // a cancelled token stops it before the next file
        _imports.Calls.Clear();
        using var cancellation = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _importer.ImportAsync("acme", "main", "default", files, Options(), cancellationToken: cancellation.Token))
            {
                await cancellation.CancelAsync();
            }
        });

        _imports.Documents.ShouldBe(["annotation rst.a"]);
    }

    [Fact]
    public async Task Import_AWriteConflictFailsItsFileOnly()
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
        _templates.Import = (_, _) => throw new TemplateConcurrencyException("main", "default", "exe", 3);

        var files = Files(
            ("rst.a.json", "{}"),
            ("rst.busy.json", "{}"),
            ("exe.template.json", """{"t":1}"""),
            ("rst.busy.conf.json", """{"b":1}"""));

        var results = await ImportAsync(files);

        results.Select(result => (result.File.Path, result.Result.Outcome, result.Result.ErrorCode)).ShouldBe(
        [
            ("rst.a.json", ViewImportOutcome.Created, null),
            ("rst.busy.json", ViewImportOutcome.Failed, ErrorCodes.Conflict),
            ("exe.template.json", ViewImportOutcome.Failed, ErrorCodes.Conflict),
            ("rst.busy.conf.json", ViewImportOutcome.Created, null),
        ]);
        results[1].Result.Message!.ShouldStartWith("The document was changed concurrently while it was imported; import the file again. ");
        _imports.Single("configuration of rst.busy").Seen!.FailedAnnotations.ShouldBe(["rst.busy"]);
    }

    [Fact]
    public async Task Import_AnyOtherExceptionEndsTheEnumeration()
    {
        _templates.Import = (_, _) => throw new ConflictException("The view always exists.", ErrorCodes.ViewExists);
        var files = Files(("rst.a.json", "{}"), ("exe.template.json", """{"t":1}"""), ("rst.a.conf.json", """{"a":1}"""));
        var results = new List<ViewImportFileResult>();

        var exception = await Should.ThrowAsync<ConflictException>(async () =>
        {
            await foreach (var result in _importer.ImportAsync("acme", "main", "default", files, Options(), cancellationToken: TestContext.Current.CancellationToken))
            {
                results.Add(result);
            }
        });

        exception.ErrorCode.ShouldBe(ErrorCodes.ViewExists);
        results.Select(result => result.File.Path).ShouldBe(["rst.a.json"]);
        _imports.Documents.ShouldBe(["annotation rst.a", "template exe"]);
    }

    [Fact]
    public async Task Import_PassesTheOptionsToEveryKind()
    {
        var files = Files(
            ("rst.a.json", "{}"),
            ("rst.a.schema.json", """{"type":"object"}"""),
            ("exe.template.json", """{"t":1}"""),
            ("rst.a.conf.json", """{"a":1}"""));
        var options = new ViewImportOptions { Actor = AuditActor.System, Existing = ViewImportExisting.Skip, Force = true, DryRun = true };

        await foreach (var _ in _importer.ImportAsync("acme", "main", "default", files, options, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        _imports.Calls.Select(call => call.Options).Distinct().ShouldHaveSingleItem().ShouldBe(options);
        _imports.Calls.Where(call => call.Overlay is not null).Select(call => call.Overlay).Distinct().ShouldHaveSingleItem();
        _imports.Single("template exe").Overlay.ShouldBeNull();
        _configurations.Imports.Single().Key.ToString().ShouldBe(FullKey.Create(AnnotationKey.Parse("rst.a"), "acme", "main", "default").ToString());
    }

    [Fact]
    public void AddViewImporter_RegistersOneImporterOverTheServicesOfTheFourKinds()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IAnnotationService>(_annotations)
            .AddSingleton<IConfigurationSchemaService>(_schemas)
            .AddSingleton<IConfigurationTemplateService>(_templates)
            .AddSingleton<IConfigurationService>(_configurations);

        services.AddViewImporter().AddViewImporter();

        services.Count(service => service.ServiceType == typeof(IViewImporter)).ShouldBe(1);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetRequiredService<IViewImporter>().ShouldBeOfType<ViewImporter>();
    }

    [Fact]
    public void AddViewTransfers_AddsTheImporter()
    {
        var services = new ServiceCollection().AddViewTransfers(new ConfigurationBuilder().Build());

        services.Single(service => service.ServiceType == typeof(IViewImporter)).ImplementationType.ShouldBe(typeof(ViewImporter));
    }

    private static ViewImportOptions Options(ViewImportExisting existing = ViewImportExisting.Overwrite, bool dryRun = false)
    {
        return new ViewImportOptions { Actor = Actor, Existing = existing, DryRun = dryRun };
    }

    // The files of loose entries, as the reader gives them.
    private static List<ViewArchiveFile> Files(params (string Path, string Content)[] files)
    {
        return ViewArchiveReader.Read(Entries(files), ReaderOptions).Files.ToList();
    }

    // Imports the files into "acme.main.default" and returns every outcome.
    private async Task<List<ViewImportFileResult>> ImportAsync(
        IReadOnlyList<ViewArchiveFile> files,
        ViewImportExisting existing = ViewImportExisting.Overwrite,
        bool dryRun = false,
        ViewImportOverlay? overlay = null)
    {
        var results = new List<ViewImportFileResult>();

        await foreach (var result in _importer.ImportAsync("acme", "main", "default", files, Options(existing, dryRun), overlay, TestContext.Current.CancellationToken))
        {
            results.Add(result);
        }

        return results;
    }
}
