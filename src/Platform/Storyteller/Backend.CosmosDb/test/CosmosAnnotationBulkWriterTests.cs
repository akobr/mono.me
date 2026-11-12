#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The bulk create of annotations (IAnnotationBulkWriter): the items of an import, the outcomes of an import with Skip,
// the relation arrays of active parents, dry runs, orphan audits, retries (after a concurrent change, also of what an
// earlier attempt committed, and the waits of throttles), cancellation, parallelism, the repair of a failed relations
// wave, and thousands of keys. Every test works in its own project.
public class CosmosAnnotationBulkWriterTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;
    private static readonly AuditActor Seeder = AuditActor.System;
    private static readonly AuditActor Creator = AnnotationServiceTestExtensions.TestActor;

    // the members Cosmos DB adds to a stored item
    private static readonly string[] SystemProperties = ["_rid", "_self", "_etag", "_attachments", "_ts"];

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IAnnotationBulkWriter Writer => Context.Services.GetRequiredService<IAnnotationBulkWriter>();

    private Container Container => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(Organization)
        .Container;

    [Fact]
    public async Task CreateMissing_WritesTheItemsAndReportsTheOutcomesOfAnImport()
    {
        const string project = "bulk-as-import";

        // two views of one length, so even the sizes of the audit items compare
        const string imported = "imported";
        const string bulk = "bulkload";

        foreach (var view in new[] { imported, bulk })
        {
            await Annotations.CreateAnnotationAsync(Organization, Model("rst.ledger", project, view) with { Title = "Ledger" }, Creator);
            await Annotations.CreateAnnotationsFromStringAsync(Organization, project, view, ["cnt.acme.prod", "sbt.gone"], Creator);
            await Annotations.DeleteAnnotationAsync(Key("sbt.gone", project, view), Creator);
        }

        var documents = new (string Key, string Json)[]
        {
            // the order of an import is not the order of the input
            ("uxe.globex.billing.eu.invoice", """{ "Title": "Invoice run", "Labels": ["eu", "billing"], "ValidFrom": "2026-01-01T00:00:00+01:00", "TimeZone": "Europe/Prague", "IsDisabled": false }"""),
            ("rst.ledger", """{ "Title": "Ledger" }"""),
            ("sbt.acme", """{ "Title": "Different" }"""),
            ("sbt.globex", """{ "Title": "Globex", "Values": { "limit": 10.50, "since": "2026-01-01T00:00:00Z", "nested": { "a": [1, 2] } } }"""),
            ("unt.ledger.posting", """{ "Title": "Posting", "UnitType": "service", "UnitDefinition": "posting.v1" }"""),
            ("exe.acme.ledger.prod", """{ "Description": "Production ledger of Acme" }"""),
            ("usg.acme.ledger", "{}"),
            ("cnt.gone.lab", "{}"),
            ("unt.billing.invoice", """{ "Title": "Invoice" }"""),
        };

        var files = documents
            .Select(document => new ViewArchiveFile
            {
                Path = $"annotations/{document.Key}.json",
                Kind = ViewDocumentKind.Annotation,
                AnnotationKey = AnnotationKey.Parse(document.Key),
                Content = Document(document.Json),
            })
            .ToList();
        var importer = new ViewImporter(
            Annotations,
            Context.Services.GetRequiredService<IConfigurationSchemaService>(),
            Context.Services.GetRequiredService<IConfigurationTemplateService>(),
            Context.Services.GetRequiredService<IConfigurationService>(),
            NullLogger<ViewImporter>.Instance);
        var importResults = new Dictionary<string, ViewImportResult>(StringComparer.Ordinal);

        await foreach (var outcome in importer.ImportAsync(Organization, project, imported, files, new ViewImportOptions { Actor = Seeder, Existing = ViewImportExisting.Skip }))
        {
            importResults[outcome.File.AnnotationKey!.ToString()] = outcome.Result;
        }

        var result = await Writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            bulk,
            documents.Select(document => Read(document.Key, document.Json, project, bulk)).ToList(),
            Seeder);

        result.Items.Select(item => item.Key.ToString()).Should().Equal(documents.Select(document => document.Key), "the items are in input order");

        foreach (var item in result.Items)
        {
            item.Result.Should().BeEquivalentTo(importResults[item.Key.ToString()], $"'{item.Key}' is decided as an import decides it");
        }

        result.Items.Select(item => item.Result.Outcome).Should().Contain(new[] { ViewImportOutcome.Created, ViewImportOutcome.Unchanged, ViewImportOutcome.Skipped });
        result.AncestorsCreated.Should().Equal("rst.billing", "usg.globex.billing", "cnt.globex.eu", "exe.globex.billing.eu");

        // rst.ledger (UnitNames), sbt.acme (ResponsibilityNames) and cnt.acme.prod (ResponsibilityNames)
        result.RelationUpdates.Should().Be(3);

        var importedKeys = (await Annotations.GetViewAnnotationsAsync(Organization, project, imported)).Select(annotation => annotation.AnnotationKey).Order(StringComparer.Ordinal).ToList();
        var bulkKeys = (await Annotations.GetViewAnnotationsAsync(Organization, project, bulk)).Select(annotation => annotation.AnnotationKey).Order(StringComparer.Ordinal).ToList();
        bulkKeys.Should().Equal(importedKeys);
        importedKeys.Should().HaveCount(13);

        foreach (var key in importedKeys)
        {
            var importedItem = await ReadItemAsync(project, imported, key, audit: false);
            var bulkItem = await ReadItemAsync(project, bulk, key, audit: false);
            JsonNode.DeepEquals(Normalize(bulkItem, audit: false), Normalize(importedItem, audit: false))
                .Should().BeTrue($"the annotation item of '{key}' should equal the imported one:\n{Normalize(bulkItem, audit: false)}\n{Normalize(importedItem, audit: false)}");

            var importedAudit = await ReadItemAsync(project, imported, key, audit: true);
            var bulkAudit = await ReadItemAsync(project, bulk, key, audit: true);
            JsonNode.DeepEquals(Normalize(bulkAudit, audit: true), Normalize(importedAudit, audit: true))
                .Should().BeTrue($"the audit item of '{key}' should equal the imported one:\n{Normalize(bulkAudit, audit: true)}\n{Normalize(importedAudit, audit: true)}");
        }

        // a few of the checks the comparison already makes, spelled out
        var ledger = (Responsibility)await GetAsync("rst.ledger", project, bulk);
        ledger.UnitNames.Should().Equal("posting");
        ledger.Modified!.By.Should().Be(Creator, "the relation arrays of an active parent change without an audit entry");
        var invoiceRun = await GetAsync("exe.globex.billing.eu", project, bulk);
        invoiceRun.TimeZone.Should().Be("Europe/Prague", "a missing ancestor copies the window, time zone, flag and labels of the key that needs it");
        invoiceRun.Labels.Should().Equal("eu", "billing");
        var history = await Annotations.GetAnnotationHistoryAsync(Key("exe.globex.billing.eu", project, bulk));
        history!.Entries.Should().ContainSingle().Which.Cause.Should().Be(new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = "uxe.globex.billing.eu.invoice" });
    }

    [Fact]
    public async Task CreateMissing_LeavesAnEqualAnnotationUnchangedAndSkipsADifferentOne()
    {
        const string project = "bulk-existing";
        await Annotations.CreateAnnotationAsync(Organization, Model("sbt.same", project) with { Title = "Same", Labels = ["eu"] }, Creator);
        await Annotations.CreateAnnotationAsync(Organization, Model("rst.other", project) with { Title = "Old" }, Creator);

        var result = await Writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            View,
            [Read("sbt.same", """{ "Title": "Same", "Labels": ["eu"] }""", project), Read("rst.other", """{ "Title": "New" }""", project)],
            Seeder);

        result.Items.Select(item => item.Result).Should().BeEquivalentTo(
            new[]
            {
                new ViewImportResult(ViewImportOutcome.Unchanged),
                Skipped(ErrorCodes.DocumentExists, "The view has a different annotation rst.other, and Skip keeps it."),
            },
            options => options.WithStrictOrdering());
        result.AncestorsCreated.Should().BeEmpty();
        result.RelationUpdates.Should().Be(0);

        (await GetAsync("rst.other", project)).Title.Should().Be("Old");

        foreach (var key in new[] { "sbt.same", "rst.other" })
        {
            (await Annotations.GetAnnotationHistoryAsync(Key(key, project)))!.Entries.Should().ContainSingle($"'{key}' is not written");
        }
    }

    [Fact]
    public async Task CreateMissing_AddsTheNewNamesToTheArraysOfActiveParents()
    {
        const string project = "bulk-arrays";
        await Annotations.CreateAnnotationsFromStringAsync(Organization, project, View, ["unt.pay.u1", "cnt.shop.c1"], Creator);
        var before = await GetAsync("rst.pay", project);

        var result = await Writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            View,
            [Read("unt.pay.u3", "{}", project), Read("unt.pay.u2", "{}", project), Read("exe.shop.pay.c2", "{}", project)],
            Seeder);

        result.Items.Should().OnlyContain(item => item.Result.Outcome == ViewImportOutcome.Created);
        result.AncestorsCreated.Should().Equal("usg.shop.pay", "cnt.shop.c2");
        result.RelationUpdates.Should().Be(2, "rst.pay and sbt.shop change, each in one replace");

        // in the order of an import
        var responsibility = (Responsibility)await GetAsync("rst.pay", project);
        responsibility.UnitNames.Should().Equal("u1", "u2", "u3");
        responsibility.Modified.Should().Be(before.Modified);
        var subject = (Subject)await GetAsync("sbt.shop", project);
        subject.ContextNames.Should().Equal("c1", "c2");
        subject.ResponsibilityNames.Should().Equal("pay");
        ((Usage)await GetAsync("usg.shop.pay", project)).ContextNames.Should().Equal("c2");
        ((Context)await GetAsync("cnt.shop.c2", project)).ResponsibilityNames.Should().Equal("pay");

        (await Annotations.GetAnnotationHistoryAsync(Key("rst.pay", project)))!.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateMissing_SkipsADeletedKeyAndWhatHangsOnIt()
    {
        const string project = "bulk-deleted";
        await Annotations.CreateAnnotationsFromStringAsync(Organization, project, View, ["cnt.gone.lab"], Creator);
        await Annotations.DeleteAnnotationAsync(Key("sbt.gone", project), Creator);

        var result = await Writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            View,
            [
                Read("exe.gone.run.lab", "{}", project),
                Read("cnt.gone.lab", "{}", project),
                Read("sbt.gone", """{ "Title": "Back" }""", project),
                Read("cnt.gone.office", "{}", project),
                Read("rst.fine", "{}", project),
            ],
            Seeder);

        result.Items.Select(item => item.Result).Should().BeEquivalentTo(
            new[]
            {
                Skipped(ErrorCodes.AnnotationDeleted, "The annotations sbt.gone, cnt.gone.lab are deleted, and Skip leaves them deleted."),
                Skipped(ErrorCodes.AnnotationDeleted, "The annotations sbt.gone, cnt.gone.lab are deleted, and Skip leaves them deleted."),
                Skipped(ErrorCodes.AnnotationDeleted, "The annotation sbt.gone is deleted, and Skip leaves it deleted."),
                Skipped(ErrorCodes.AnnotationDeleted, "The annotation sbt.gone is deleted, and Skip leaves it deleted."),
                new ViewImportResult(ViewImportOutcome.Created),
            },
            options => options.WithStrictOrdering());
        result.AncestorsCreated.Should().BeEmpty("the ancestors of a skipped key are not created");

        (await Annotations.GetDeletedAnnotationAsync(Key("sbt.gone", project))).Should().NotBeNull();
        (await Annotations.GetDeletedAnnotationAsync(Key("cnt.gone.lab", project))).Should().NotBeNull();

        foreach (var key in new[] { "rst.run", "usg.gone.run", "exe.gone.run.lab", "cnt.gone.office" })
        {
            (await Annotations.ExistAnnotationAsync(Key(key, project))).Should().BeFalse($"'{key}' is not written");
        }

        (await Annotations.ExistAnnotationAsync(Key("rst.fine", project))).Should().BeTrue();
    }

    [Fact]
    public async Task CreateMissing_FailsInvalidAndDuplicateKeysAndSkipsWhatHangsOnThem()
    {
        const string project = "bulk-invalid";
        var tooLong = new string('x', AnnotationLimits.TitleMaxLength + 1);

        var result = await Writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            View,
            [
                Read("rst.ok", """{ "Title": "First" }""", project),
                Read("exe.bad.ok.c", "{}", project),
                Model("sbt.bad", project) with { Title = tooLong },
                Read("rst.ok", """{ "Title": "Second" }""", project),
                Read("cnt.bad.c", "{}", project),
            ],
            Seeder);

        var items = result.Items.Select(item => item.Result).ToList();
        items[0].Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        items[1].Should().Be(Skipped(ErrorCodes.DependencyFailed, "The ancestors sbt.bad, cnt.bad.c failed in this import."));
        items[2].Outcome.Should().Be(ViewImportOutcome.Failed);
        items[2].ErrorCode.Should().Be(ErrorCodes.InvalidAnnotation);
        items[2].Message.Should().StartWith($"The annotation 'sbt.bad' is not valid: Title has {AnnotationLimits.TitleMaxLength + 1} characters");
        items[3].Should().Be(new ViewImportResult(
            ViewImportOutcome.Failed,
            ErrorCode: ErrorCodes.DuplicateFile,
            Message: "The input holds the annotation rst.ok more than once; only the first counts."));
        items[4].Should().Be(Skipped(ErrorCodes.DependencyFailed, "The ancestor sbt.bad failed in this import."));

        (await GetAsync("rst.ok", project)).Title.Should().Be("First");

        foreach (var key in new[] { "sbt.bad", "cnt.bad.c", "usg.bad.ok", "exe.bad.ok.c" })
        {
            (await Annotations.ExistAnnotationAsync(Key(key, project))).Should().BeFalse($"'{key}' is not written");
        }
    }

    [Fact]
    public async Task CreateMissing_DryRunDecidesEverythingAndWritesNothing()
    {
        const string project = "bulk-dry-run";
        await Annotations.CreateAnnotationsFromStringAsync(Organization, project, View, ["rst.dry"], Creator);
        IReadOnlyList<Annotation> annotations = [Read("unt.dry.u1", "{}", project), Read("exe.wet.dry.c", "{}", project)];
        var progress = new RecordingProgress();

        var dryRun = await Writer.CreateMissingAnnotationsAsync(Organization, project, View, annotations, Seeder, new AnnotationBulkOptions { DryRun = true, Progress = progress });

        dryRun.Items.Select(item => item.Result.Outcome).Should().Equal(ViewImportOutcome.Created, ViewImportOutcome.Created);
        dryRun.Items[1].Result.Warnings!.Single().Message.Should().Be("The missing ancestors sbt.wet, usg.wet.dry, cnt.wet.c are created with defaults.");
        dryRun.AncestorsCreated.Should().Equal("sbt.wet", "usg.wet.dry", "cnt.wet.c");
        dryRun.RelationUpdates.Should().Be(1);
        progress.Values.Should().BeEmpty();

        foreach (var key in new[] { "unt.dry.u1", "sbt.wet", "usg.wet.dry", "cnt.wet.c", "exe.wet.dry.c" })
        {
            (await Annotations.ExistAnnotationAsync(Key(key, project))).Should().BeFalse($"'{key}' is not written");
        }

        ((Responsibility)await GetAsync("rst.dry", project)).UnitNames.Should().BeEmpty();

        // the real run does what the dry run said
        var run = await Writer.CreateMissingAnnotationsAsync(Organization, project, View, annotations, Seeder);

        run.Should().BeEquivalentTo(dryRun);
        ((Responsibility)await GetAsync("rst.dry", project)).UnitNames.Should().Equal("u1");
    }

    [Fact]
    public async Task CreateMissing_ContinuesTheAuditOfAMissingKey()
    {
        const string project = "bulk-orphan-audit";
        await Annotations.CreateAnnotationsFromStringAsync(Organization, project, View, ["sbt.orphan"], Creator);
        var key = Key("sbt.orphan", project);

        // the annotation item goes, its audit item stays
        using (var response = await Container.DeleteItemStreamAsync($"{View}.sbt.orphan", key.GetCosmosPartitionKey()))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var result = await Writer.CreateMissingAnnotationsAsync(Organization, project, View, [Read("sbt.orphan", """{ "Title": "Again" }""", project)], Seeder);

        result.Items.Single().Result.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        (await GetAsync("sbt.orphan", project)).Title.Should().Be("Again");
        var history = await Annotations.GetAnnotationHistoryAsync(key);
        history!.Entries.Select(entry => (entry.Action, entry.Actor)).Should().Equal(
            (AnnotationAuditAction.Created, Seeder),
            (AnnotationAuditAction.Created, Creator));
    }

    [Fact]
    public async Task CreateMissing_RunsAgainAfterAConcurrentChangeAndReportsWhatItCommittedCreated()
    {
        const string project = "bulk-retry";
        await Annotations.CreateAnnotationsFromStringAsync(Organization, project, View, ["rst.pay"], Creator);
        var (writer, storage) = CreateWriter();
        var failures = 0;

        // the first relations wave loses to a concurrent change of rst.pay, after the keys committed
        storage.Interceptor.BeforeBatch = batch => Task.FromResult<HttpStatusCode?>(
            batch.Operations.Any(operation => operation.Kind == BatchOperationKind.Replace) && Interlocked.Increment(ref failures) == 1
                ? HttpStatusCode.PreconditionFailed
                : null);

        var result = await writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            View,
            [Read("usg.retry.pay", "{}", project), Read("unt.pay.u1", "{}", project)],
            Seeder);

        failures.Should().Be(2, "the relations wave ran twice");
        result.Items[0].Result.Outcome.Should().Be(ViewImportOutcome.Created);
        result.Items[0].Result.Warnings!.Single().Message.Should().Be("The missing ancestor sbt.retry is created with defaults.");
        result.Items[1].Result.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        result.AncestorsCreated.Should().Equal("sbt.retry");
        result.RelationUpdates.Should().Be(1);

        // every key was created once
        storage.Interceptor.Batches.Should().NotContain(batch => batch.StatusCode == HttpStatusCode.Conflict);
        ((Responsibility)await GetAsync("rst.pay", project)).UnitNames.Should().Equal("u1");
        ((Subject)await GetAsync("sbt.retry", project)).ResponsibilityNames.Should().Equal("pay");

        foreach (var key in new[] { "sbt.retry", "usg.retry.pay", "unt.pay.u1" })
        {
            (await Annotations.GetAnnotationHistoryAsync(Key(key, project)))!.Entries.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task CreateMissing_SkipsWhatHangsOnAnAncestorDeletedBetweenAttempts()
    {
        const string project = "bulk-retry-deleted";
        var (writer, storage) = CreateWriter();
        var batches = 0;

        // the missing subject commits in the first wave; then someone deletes it, and the context's wave loses
        storage.Interceptor.BeforeBatch = async _ =>
        {
            if (Interlocked.Increment(ref batches) != 2)
            {
                return null;
            }

            await Annotations.DeleteAnnotationAsync(Key("sbt.lost", project), Creator);
            return HttpStatusCode.PreconditionFailed;
        };

        var result = await writer.CreateMissingAnnotationsAsync(Organization, project, View, [Read("cnt.lost.c1", "{}", project)], Seeder);

        result.Items.Single().Result.Should().BeEquivalentTo(Skipped(ErrorCodes.AnnotationDeleted, "The annotation sbt.lost is deleted, and Skip leaves it deleted."));
        result.AncestorsCreated.Should().Equal(new[] { "sbt.lost" }, "the call created it before the delete");
        (await Annotations.ExistAnnotationAsync(Key("cnt.lost.c1", project))).Should().BeFalse("nothing is created under a deleted ancestor");
        (await Annotations.GetDeletedAnnotationAsync(Key("sbt.lost", project))).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateMissing_WritesAgainAnAncestorPurgedBetweenAttempts()
    {
        const string project = "bulk-retry-purged";
        var (writer, storage) = CreateWriter();
        var batches = 0;

        // the missing subject commits in the first wave; then someone deletes it permanently, and the context's wave loses
        storage.Interceptor.BeforeBatch = async _ =>
        {
            if (Interlocked.Increment(ref batches) != 2)
            {
                return null;
            }

            await Annotations.DeleteAnnotationAsync(Key("sbt.purged", project), Creator);
            await Annotations.PurgeAnnotationAsync(Key("sbt.purged", project), Creator);
            return HttpStatusCode.PreconditionFailed;
        };

        var result = await writer.CreateMissingAnnotationsAsync(Organization, project, View, [Read("cnt.purged.c1", "{}", project)], Seeder);

        result.Items.Single().Result.Outcome.Should().Be(ViewImportOutcome.Created);
        result.Items.Single().Result.Warnings!.Single().Message.Should().Be("The missing ancestor sbt.purged is created with defaults.");
        result.AncestorsCreated.Should().Equal("sbt.purged");
        ((Subject)await GetAsync("sbt.purged", project)).ContextNames.Should().Equal("c1");
        (await Annotations.ExistAnnotationAsync(Key("cnt.purged.c1", project))).Should().BeTrue();
        var history = await Annotations.GetAnnotationHistoryAsync(Key("sbt.purged", project));
        history!.Entries.Should().ContainSingle("the purge removed the audit of the first write")
            .Which.Cause.Should().Be(new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = "cnt.purged.c1" });
    }

    [Fact]
    public async Task CreateMissing_WaitsTheRetryAfterOfAThrottleAndRunsAgain()
    {
        const string project = "bulk-throttle";
        var clock = new RecordingTimeProvider();
        var (writer, storage) = CreateWriter(clock);
        var reads = 0;
        var throttles = 0;

        // a throttled read and a throttled batch that ask for a wait, and a throttled batch that does not
        storage.Interceptor.BeforeReadMany = _ => Task.FromResult(Interlocked.Increment(ref reads) == 1 ? ThrottledRead(TimeSpan.FromSeconds(2)) : null);
        storage.Interceptor.BeforeBatch = batch =>
        {
            switch (Interlocked.Increment(ref throttles))
            {
                case 1:
                    batch.RetryAfter = TimeSpan.FromSeconds(7);
                    return Task.FromResult<HttpStatusCode?>(HttpStatusCode.TooManyRequests);

                case 2:
                    return Task.FromResult<HttpStatusCode?>(HttpStatusCode.TooManyRequests);

                default:
                    return Task.FromResult<HttpStatusCode?>(null);
            }
        };

        var result = await writer.CreateMissingAnnotationsAsync(Organization, project, View, [Read("unt.slow.u1", "{}", project)], Seeder);

        result.Items.Single().Result.Outcome.Should().Be(ViewImportOutcome.Created);

        // the waits the service asked for, and without one as many seconds as the attempts so far
        clock.Waits.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(3));
        storage.Interceptor.Batches.Select(batch => batch.StatusCode).Should().StartWith(new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests });
        ((Responsibility)await GetAsync("rst.slow", project)).UnitNames.Should().Equal("u1");
    }

    [Fact]
    public async Task CreateMissing_RethrowsAThrottleThatOutlastsTheAttempts()
    {
        const string project = "bulk-throttled";
        var clock = new RecordingTimeProvider();
        var (writer, storage) = CreateWriter(clock);
        storage.Interceptor.BeforeBatch = batch =>
        {
            batch.RetryAfter = TimeSpan.FromSeconds(1);
            return Task.FromResult<HttpStatusCode?>(HttpStatusCode.TooManyRequests);
        };

        var call = () => writer.CreateMissingAnnotationsAsync(Organization, project, View, [Read("sbt.busy", "{}", project)], Seeder);

        (await call.Should().ThrowAsync<CosmosException>()).Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        storage.Interceptor.Batches.Should().HaveCount(5, "the call makes five attempts");
        clock.Waits.Should().Equal(Enumerable.Repeat(TimeSpan.FromSeconds(1), 4), "no wait follows the last attempt");
        (await Annotations.ExistAnnotationAsync(Key("sbt.busy", project))).Should().BeFalse();
    }

    [Fact]
    public async Task CreateMissing_EndsAConflictThatOutlastsTheAttemptsWithAConflictError()
    {
        const string project = "bulk-conflict";
        var (writer, storage) = CreateWriter();
        storage.Interceptor.BeforeBatch = _ => Task.FromResult<HttpStatusCode?>(HttpStatusCode.PreconditionFailed);

        var call = () => writer.CreateMissingAnnotationsAsync(Organization, project, View, [Read("sbt.busy", "{}", project)], Seeder);

        (await call.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        storage.Interceptor.Batches.Should().HaveCount(5, "the call makes five attempts")
            .And.OnlyContain(batch => batch.StatusCode == HttpStatusCode.PreconditionFailed);
        (await Annotations.ExistAnnotationAsync(Key("sbt.busy", project))).Should().BeFalse();
    }

    [Fact]
    public async Task CreateMissing_RefusesAKeyThatDoesNotParse()
    {
        const string project = "bulk-bad-key";

        var call = () => Writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            View,
            [Read("sbt.fine", "{}", project), Model("sbt.fine", project) with { AnnotationKey = "not a key" }],
            Seeder);

        (await call.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidKey);
        (await Annotations.ExistAnnotationAsync(Key("sbt.fine", project))).Should().BeFalse("the call refuses the input before it writes anything");
    }

    [Fact]
    public async Task CreateMissing_StopsBeforeTheNextWaveWhenCancelledAndARunAgainCompletesIt()
    {
        const string project = "bulk-cancel";
        var (writer, storage) = CreateWriter();
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<Annotation> annotations = [Read("unt.cancel.u1", "{}", project)];

        // cancelled once the wave of the responsibility committed
        storage.Interceptor.AfterBatch = _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        var cancelled = () => writer.CreateMissingAnnotationsAsync(Organization, project, View, annotations, Seeder, cancellationToken: cancellation.Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        storage.Interceptor.Batches.Should().ContainSingle("the wave of the unit does not start");
        (await Annotations.ExistAnnotationAsync(Key("rst.cancel", project))).Should().BeTrue();
        (await Annotations.ExistAnnotationAsync(Key("unt.cancel.u1", project))).Should().BeFalse();

        storage.Interceptor.Reset();
        var result = await writer.CreateMissingAnnotationsAsync(Organization, project, View, annotations, Seeder);

        result.Items.Single().Result.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        ((Responsibility)await GetAsync("rst.cancel", project)).UnitNames.Should().Equal("u1");
    }

    [Fact]
    public async Task CreateMissing_RunsAtMostMaxParallelBatchesAtOnce()
    {
        const string project = "bulk-parallel";
        var (writer, storage) = CreateWriter();
        var gate = new object();
        var running = 0;
        var peak = 0;

        // every batch holds its slot a while, so the batches that may run at once do
        storage.Interceptor.BeforeBatch = async _ =>
        {
            lock (gate)
            {
                running++;
                peak = Math.Max(peak, running);
            }

            await Task.Delay(100);

            lock (gate)
            {
                running--;
            }

            return null;
        };

        // twelve subjects, each in its own partition, so one wave of twelve batches
        var result = await writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            View,
            Enumerable.Range(0, 12).Select(index => Read($"sbt.p{index:00}", "{}", project)).ToList(),
            Seeder,
            new AnnotationBulkOptions { MaxParallelBatches = 3 });

        result.Items.Should().HaveCount(12).And.OnlyContain(item => item.Result.Outcome == ViewImportOutcome.Created);
        storage.Interceptor.Batches.Should().HaveCount(12);
        peak.Should().Be(3);
    }

    [Fact]
    public async Task CreateMissing_AgainRepairsTheArraysOfAFailedRelationsWave()
    {
        const string project = "bulk-repair";
        await Annotations.CreateAnnotationsFromStringAsync(Organization, project, View, ["unt.repair.u0", "cnt.repairs.c0"], Creator);
        var (writer, storage) = CreateWriter();
        IReadOnlyList<Annotation> annotations = [Read("unt.repair.u1", "{}", project), Read("unt.repair.u2", "{}", project), Read("cnt.repairs.c1", "{}", project)];

        // every relations batch fails with an error that is not retried: the keys stay, the arrays miss them
        storage.Interceptor.BeforeBatch = batch => Task.FromResult<HttpStatusCode?>(
            batch.Operations.Any(operation => operation.Kind == BatchOperationKind.Replace) ? HttpStatusCode.ServiceUnavailable : null);

        var failed = () => writer.CreateMissingAnnotationsAsync(Organization, project, View, annotations, Seeder);

        (await failed.Should().ThrowAsync<CosmosException>()).Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Annotations.ExistAnnotationAsync(Key("unt.repair.u2", project))).Should().BeTrue();
        ((Responsibility)await GetAsync("rst.repair", project)).UnitNames.Should().Equal("u0");

        storage.Interceptor.Reset();
        var result = await writer.CreateMissingAnnotationsAsync(Organization, project, View, annotations, Seeder);

        result.Items.Should().OnlyContain(item => item.Result.Outcome == ViewImportOutcome.Unchanged);
        result.RelationUpdates.Should().Be(2);
        ((Responsibility)await GetAsync("rst.repair", project)).UnitNames.Should().Equal("u0", "u1", "u2");
        ((Subject)await GetAsync("sbt.repairs", project)).ContextNames.Should().Equal("c0", "c1");
        (await Annotations.GetAnnotationHistoryAsync(Key("rst.repair", project)))!.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateMissing_WritesThousandsOfKeysOverManyPartitions()
    {
        const string project = "bulk-large";
        var annotations = new List<Annotation>();

        for (var responsibility = 0; responsibility < 30; responsibility++)
        {
            annotations.Add(Read($"rst.r{responsibility:00}", $$"""{ "Title": "Responsibility {{responsibility}}" }""", project));
            annotations.AddRange(Enumerable.Range(0, 30).Select(unit => Read($"unt.r{responsibility:00}.u{unit:00}", """{ "UnitType": "job" }""", project)));
        }

        for (var subject = 0; subject < 120; subject++)
        {
            annotations.Add(Read($"sbt.s{subject:000}", "{}", project));
            annotations.AddRange(Enumerable.Range(0, 8).Select(context => Read($"cnt.s{subject:000}.c{context}", """{ "Labels": ["generated"] }""", project)));
        }

        // executions whose usages are not in the input
        annotations.AddRange(Enumerable.Range(0, 20).Select(subject => Read($"exe.s{subject:000}.r{subject % 30:00}.c0", "{}", project)));
        var progress = new RecordingProgress();

        var result = await Writer.CreateMissingAnnotationsAsync(
            Organization,
            project,
            View,
            annotations,
            Seeder,
            new AnnotationBulkOptions { MaxUnitsPerPlan = 250, Progress = progress });

        result.Items.Should().HaveCount(2030).And.OnlyContain(item => item.Result.Outcome == ViewImportOutcome.Created);
        result.AncestorsCreated.Should().HaveCount(20).And.OnlyContain(key => key.StartsWith("usg.", StringComparison.Ordinal));
        result.RelationUpdates.Should().Be(0);
        progress.Values.Should().BeInAscendingOrder().And.HaveCountGreaterThan(8);
        progress.Values[^1].Should().Be(2050);

        var stored = await Annotations.GetViewAnnotationsAsync(Organization, project, View);
        stored.Should().HaveCount(2050);
        stored.OfType<Responsibility>().Should().HaveCount(30).And.OnlyContain(annotation => annotation.UnitNames.Count == 30);
        stored.OfType<Subject>().Should().HaveCount(120).And.OnlyContain(annotation => annotation.ContextNames.Count == 8);
        stored.OfType<Subject>().Count(annotation => annotation.ResponsibilityNames.Count == 1).Should().Be(20);
        ((Responsibility)stored.Single(annotation => annotation.AnnotationKey == "rst.r07")).UnitNames.Should().Equal(Enumerable.Range(0, 30).Select(unit => $"u{unit:00}"));

        var unitHistory = await Annotations.GetAnnotationHistoryAsync(Key("unt.r29.u29", project));
        unitHistory!.Entries.Should().ContainSingle().Which.Actor.Should().Be(Seeder);
        var usageHistory = await Annotations.GetAnnotationHistoryAsync(Key("usg.s019.r19", project));
        usageHistory!.Entries.Should().ContainSingle().Which.Cause.Should().Be(new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = "exe.s019.r19.c0" });

        // a second run finds every key
        var again = await Writer.CreateMissingAnnotationsAsync(Organization, project, View, annotations, Seeder);
        again.Items.Should().OnlyContain(item => item.Result.Outcome == ViewImportOutcome.Unchanged);
        again.RelationUpdates.Should().Be(0);
    }

    private static ViewImportResult Skipped(string errorCode, string message)
    {
        return new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: errorCode, Message: message);
    }

    // A file as the reader parses it.
    private static JObject Document(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ViewAnnotationDocument.Parse(stream);
    }

    // The annotation of a file, as the seeder reads it.
    private static Annotation Read(string annotationKey, string json, string project, string view = View)
    {
        return ViewAnnotationDocument.TryRead(Document(json), Key(annotationKey, project, view), out var annotation, out _, out var message)
            ? annotation
            : throw new InvalidOperationException(message);
    }

    private static FullKey Key(string annotationKey, string project, string view = View)
    {
        return FullKey.Create(AnnotationKey.Parse(annotationKey), Organization, project, view);
    }

    private static Annotation Model(string annotationKey, string project, string view = View)
    {
        return AnnotationFactory.CreateDefault(AnnotationKey.Parse(annotationKey), project, view);
    }

    // The item without what differs between two writes of the same content: the system properties, the id and view,
    // the times of the stamps and entries, the entry ids and the size of an audit.
    private static JsonNode Normalize(JsonObject item, bool audit)
    {
        var copy = item.DeepClone().AsObject();

        foreach (var name in SystemProperties.Append("id").Append(nameof(Annotation.ViewName)))
        {
            copy.Remove(name);
        }

        if (audit)
        {
            copy.Remove("SizeInBytes");

            foreach (var entry in copy["Entries"]!.AsArray())
            {
                entry!.AsObject().Remove("Id");
                entry.AsObject().Remove("At");
            }
        }
        else
        {
            copy["Created"]!.AsObject().Remove("At");
            copy["Modified"]!.AsObject().Remove("At");
        }

        return copy;
    }

    private async Task<JsonObject> ReadItemAsync(string project, string view, string annotationKey, bool audit)
    {
        var key = Key(annotationKey, project, view);
        var id = audit ? $"{view}.adt.{annotationKey}" : $"{view}.{annotationKey}";
        using var response = await Container.ReadItemStreamAsync(id, key.GetCosmosPartitionKey());
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"the item '{id}' should exist");
        return JsonNode.Parse(response.Content)!.AsObject();
    }

    private async Task<Annotation> GetAsync(string annotationKey, string project, string view = View)
    {
        var annotation = await Annotations.GetAnnotationAsync(Key(annotationKey, project, view));
        annotation.Should().NotBeNull($"'{annotationKey}' should be active");
        return annotation!;
    }

    // A read of many items that the service throttled after the retries of the client, with the wait it asks for.
    private static ResponseMessage ThrottledRead(TimeSpan retryAfter)
    {
        var response = new ResponseMessage(HttpStatusCode.TooManyRequests, null, "Throttled by the test.");
        response.Headers.Add("x-ms-retry-after-ms", retryAfter.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
        return response;
    }

    // A writer over storage whose batches the test records and fails, and with a clock of the test when given.
    private (IAnnotationBulkWriter Writer, InterceptedRepositoryProvider Storage) CreateWriter(TimeProvider? timeProvider = null)
    {
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var writer = timeProvider is null
            ? ActivatorUtilities.CreateInstance<CosmosAnnotationService>(Context.Services, storage)
            : ActivatorUtilities.CreateInstance<CosmosAnnotationService>(Context.Services, storage, timeProvider);
        return (writer, storage);
    }

    // Records the waits of the writer and ends each at once; the time is the system's.
    private sealed class RecordingTimeProvider : TimeProvider
    {
        private readonly ConcurrentQueue<TimeSpan> _waits = new();

        public IReadOnlyList<TimeSpan> Waits => _waits.ToArray();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _waits.Enqueue(dueTime);
            return TimeProvider.System.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }

    // Records the reports as they come, unlike Progress<T>, which posts them.
    private sealed class RecordingProgress : IProgress<int>
    {
        private readonly List<int> _values = [];

        public IReadOnlyList<int> Values => _values;

        public void Report(int value)
        {
            _values.Add(value);
        }
    }
}
