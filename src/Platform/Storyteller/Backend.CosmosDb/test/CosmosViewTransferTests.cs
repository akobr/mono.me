using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class CosmosViewTransferTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "transfer-tests";
    private const string View = Constants.DefaultViewName;
    private static readonly AuditActor Author = AuditActor.Account("importer", "Importer");

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configurations => Context.Services.GetRequiredService<IConfigurationService>();

    [Fact]
    public async Task Histories_HoldTheOwnDocumentsAndTheirVersions()
    {
        const string project = "transfer-histories";
        await CreateAsync(project, View, "rst.hist-one", "unt.hist-one.unit");
        var key = Key("rst.hist-one", project);

        await Configurations.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{"a":1}"""), AuditActor.Account("first", "First"));
        await Configurations.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{"a":2}"""), AuditActor.Account("second", "Second"));

        // the unit has no document of its own; reading it caches the inherited one in an empty item
        (await Configurations.GetRawConfigurationAsync(Key("unt.hist-one.unit", project)))!.Content["a"]!.Value<int>().Should().Be(2);

        var histories = await Configurations.GetConfigurationHistoriesAsync(TestConstants.Organization, project, View);

        histories.Select(history => history.AnnotationKey).Should().Equal("rst.hist-one");
        var history = histories[0];
        history.Versions.Select(version => version.Version).Should().Equal(1UL, 2UL);
        history.Versions[0].Content["a"]!.Value<int>().Should().Be(1);
        history.Versions[0].Author.Should().Be("First");
        history.Current.Should().BeSameAs(history.Versions[1]);
        history.Current!.Content["a"]!.Value<int>().Should().Be(2);
        history.Current.Author.Should().Be("Second");
        history.Current.CreatedAt.Should().BeOnOrAfter(history.Versions[0].CreatedAt);
    }

    [Fact]
    public async Task Histories_KeepClearedConfigurationsWithoutACurrentDocument()
    {
        const string project = "transfer-cleared";
        await CreateAsync(project, View, "rst.cleared");
        var key = Key("rst.cleared", project);

        await Configurations.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{"a":1}"""), Author);
        await Configurations.ClearConfigurationAsync(key);

        var history = (await Configurations.GetConfigurationHistoriesAsync(TestConstants.Organization, project, View)).Single();

        history.Current.Should().BeNull();
        history.Versions.Select(version => version.Version).Should().Equal(1UL);
    }

    [Fact]
    public async Task Histories_LeaveOutOtherViewsAndDeletedAnnotations()
    {
        const string project = "transfer-scope";
        await CreateAsync(project, View, "rst.kept", "rst.deleted");
        await CreateAsync(project, "other", "rst.other-view");
        await CreateAsync("transfer-scope-2", View, "rst.other-project");

        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.kept", project), JObject.Parse("""{"a":1}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.deleted", project), JObject.Parse("""{"a":1}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.deleted", project), JObject.Parse("""{"a":2}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.other-view", project, "other"), JObject.Parse("""{"a":1}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(Key("rst.other-project", "transfer-scope-2"), JObject.Parse("""{"a":1}"""), Author);
        await Annotations.DeleteAnnotationAsync(Key("rst.deleted", project));

        var histories = await Configurations.GetConfigurationHistoriesAsync(TestConstants.Organization, project, View);

        histories.Select(history => history.AnnotationKey).Should().Equal("rst.kept");
    }

    [Fact]
    public async Task Import_CreatesThenReplacesThenLeavesAnEqualDocument()
    {
        await CreateAsync(Project, View, "rst.import-replace");
        var key = Key("rst.import-replace");

        var created = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"a":1,"list":[1,2]}"""), Author);
        created.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));

        // a merge would keep "a" and unite the lists; an import replaces the document
        var updated = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"list":[3],"b":2}"""), Author);
        updated.Should().Be(new ViewImportResult(ViewImportOutcome.Updated, 2));

        var stored = await Configurations.GetConfigurationVersionContentAsync(key, 2);
        JToken.DeepEquals(stored!.Content, JObject.Parse("""{"list":[3],"b":2}""")).Should().BeTrue();
        stored.Modified.By.Should().Be(Author);

        var unchanged = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"b":2,"list":[3]}"""), Author);
        unchanged.Should().Be(new ViewImportResult(ViewImportOutcome.Unchanged, 2));

        (await Configurations.GetConfigurationVersionsAsync(key)).Select(version => version.Version).Should().Equal(1u, 2u);
    }

    [Fact]
    public async Task Import_AppliesTheDirectivesToTheDocumentAlone()
    {
        await CreateAsync(Project, View, "rst.import-directives");
        var key = Key("rst.import-directives");

        var result = await Configurations.ImportConfigurationAsync(
            key,
            JObject.Parse("""{"a":1,"b":2,"$remove":["$.b"],"$patch":[{"op":"add","path":"/c","value":3}]}"""),
            Author);

        result.Outcome.Should().Be(ViewImportOutcome.Created);
        var stored = await Configurations.GetConfigurationVersionContentAsync(key, 1);
        JToken.DeepEquals(stored!.Content, JObject.Parse("""{"a":1,"c":3}""")).Should().BeTrue();

        var broken = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"$patch":[{"op":"remove","path":"/missing"}]}"""), Author);
        broken.Outcome.Should().Be(ViewImportOutcome.Failed);
        broken.ErrorCode.Should().Be(ErrorCodes.PatchInvalid);
    }

    [Fact]
    public async Task Import_SkipsMissingDeletedAndEmpty()
    {
        await CreateAsync(Project, View, "rst.import-deleted", "rst.import-empty");
        await Annotations.DeleteAnnotationAsync(Key("rst.import-deleted"));

        var missing = await Configurations.ImportConfigurationAsync(Key("rst.import-missing"), JObject.Parse("""{"a":1}"""), Author);
        missing.Outcome.Should().Be(ViewImportOutcome.Skipped);
        missing.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);

        var deleted = await Configurations.ImportConfigurationAsync(Key("rst.import-deleted"), JObject.Parse("""{"a":1}"""), Author);
        deleted.Outcome.Should().Be(ViewImportOutcome.Skipped);
        deleted.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);

        var empty = await Configurations.ImportConfigurationAsync(Key("rst.import-empty"), new JObject(), Author);
        empty.Outcome.Should().Be(ViewImportOutcome.Skipped);
        empty.ErrorCode.Should().Be(ErrorCodes.EmptyDocument);

        (await Configurations.HasConfigurationContentAsync(Key("rst.import-empty"))).Should().BeFalse();
    }

    [Fact]
    public async Task Import_DryRunWritesNothing()
    {
        await CreateAsync(Project, View, "rst.import-dry");
        var key = Key("rst.import-dry");

        var result = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"a":1}"""), Author, dryRun: true);

        result.Should().Be(new ViewImportResult(ViewImportOutcome.Created));
        (await Configurations.HasConfigurationContentAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_IsValidatedUnlessForced()
    {
        const string project = "transfer-schema";
        await CreateAsync(project, View, "rst.import-schema");
        var key = Key("rst.import-schema", project);
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        await schemas.SetSchemaAsync(
            TestConstants.Organization,
            project,
            View,
            AnnotationTypeCodes.Responsibility,
            JObject.Parse("""{"type":"object","properties":{"a":{"type":"number"}}}"""),
            AuditActor.Account("test"),
            false);

        var rejected = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"a":"text"}"""), Author);
        rejected.Outcome.Should().Be(ViewImportOutcome.Failed);
        rejected.ErrorCode.Should().Be(ErrorCodes.SchemaViolation);
        rejected.Message.Should().StartWith("The document violates its schema: ");

        var dryRun = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"a":"text"}"""), Author, dryRun: true);
        dryRun.Outcome.Should().Be(ViewImportOutcome.Failed);

        var forced = await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"a":"text"}"""), Author, force: true);
        forced.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 1));
    }

    [Fact]
    public async Task Import_IsAuditedForTheActor()
    {
        await CreateAsync(Project, View, "rst.import-audit");
        var key = Key("rst.import-audit");
        var actor = new AuditActor { Kind = AuditActorKind.Account, Id = "user_importer", Name = "Importer" };

        await Configurations.ImportConfigurationAsync(key, JObject.Parse("""{"a":1}"""), actor);

        var history = await Annotations.GetAnnotationHistoryAsync(key);
        var entry = history!.Entries.First();
        entry.Action.Should().Be(AnnotationAuditAction.ConfigurationChanged);
        entry.Actor.Should().Be(actor);
        entry.ConfigurationVersion.Should().Be(1UL);
    }

    [Fact]
    public async Task Import_AfterAClearContinuesTheVersions()
    {
        const string project = "transfer-clear-versions";
        await CreateAsync(project, View, "unt.clear-parent.unit");
        var parent = Key("rst.clear-parent", project);
        var unit = Key("unt.clear-parent.unit", project);

        await Configurations.CreateOrUpdateConfigurationAsync(parent, JObject.Parse("""{"inherited":true}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(unit, JObject.Parse("""{"own":1}"""), Author);
        await Configurations.CreateOrUpdateConfigurationAsync(unit, JObject.Parse("""{"own":2}"""), Author);
        await Configurations.ClearConfigurationAsync(unit);

        // the read caches the inherited document in a new, empty item of version 0
        (await Configurations.GetRawConfigurationAsync(unit))!.Content["inherited"]!.Value<bool>().Should().BeTrue();

        var first = await Configurations.ImportConfigurationAsync(unit, JObject.Parse("""{"own":3}"""), Author);
        first.Should().Be(new ViewImportResult(ViewImportOutcome.Created, 3));

        var second = await Configurations.ImportConfigurationAsync(unit, JObject.Parse("""{"own":4}"""), Author);
        second.Should().Be(new ViewImportResult(ViewImportOutcome.Updated, 4));

        (await Configurations.GetConfigurationVersionsAsync(unit)).Select(version => version.Version).Should().Equal(1u, 2u, 3u, 4u);
    }

    [Fact]
    public async Task Store_ReadsAJobOnlyThroughItsViewAndGuardsWrites()
    {
        var store = Context.Services.GetRequiredService<IViewTransferStore>();
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var record = new ViewTransferRecord
        {
            Organization = TestConstants.Organization,
            Project = "transfer-store",
            View = View,
            Kind = ViewTransferKind.Export,
            Id = id,
            Export = new ViewExport
            {
                Id = id,
                Status = ViewTransferStatus.Queued,
                Format = ViewArchiveFormat.Git,
                FileName = "configurations.zip",
                CreatedAt = now,
                CreatedBy = AnnotationServiceTestExtensions.TestActor,
                ExpiresAt = now.AddHours(24),
            },
        };

        var created = await store.CreateAsync(record);
        created.ETag.Should().NotBeNullOrEmpty();

        var read = await store.GetAsync(TestConstants.Organization, "transfer-store", View, ViewTransferKind.Export, id);
        read!.Export.Should().BeEquivalentTo(record.Export);
        (await store.GetAsync(TestConstants.Organization, "transfer-store", "other", ViewTransferKind.Export, id)).Should().BeNull();
        (await store.GetAsync(TestConstants.Organization, "transfer-store", View, ViewTransferKind.Import, id)).Should().BeNull();

        (await store.CountActiveAsync(TestConstants.Organization, "transfer-store", now.AddMinutes(-5))).Should().Be(1);
        (await store.CountActiveAsync(TestConstants.Organization, "transfer-store", now.AddMinutes(5))).Should().Be(0);

        var running = await store.TryReplaceAsync(read with { Export = read.Export! with { Status = ViewTransferStatus.Running } });
        running.Should().NotBeNull();

        // the first read's ETag is stale now
        (await store.TryReplaceAsync(read with { Export = read.Export! with { Status = ViewTransferStatus.Failed } })).Should().BeNull();

        var succeeded = await store.TryReplaceAsync(running! with { Export = running.Export! with { Status = ViewTransferStatus.Succeeded } });
        succeeded.Should().NotBeNull();
        (await store.CountActiveAsync(TestConstants.Organization, "transfer-store", now.AddMinutes(-5))).Should().Be(0);
    }

    private static FullKey Key(string annotationKey, string project = Project, string view = View)
    {
        return FullKey.Create(AnnotationKey.Parse(annotationKey), TestConstants.Organization, project, view);
    }

    private Task CreateAsync(string project, string view, params string[] keys)
    {
        return Annotations.CreateAnnotationsFromStringAsync(TestConstants.Organization, project, view, keys, AnnotationServiceTestExtensions.TestActor);
    }
}
