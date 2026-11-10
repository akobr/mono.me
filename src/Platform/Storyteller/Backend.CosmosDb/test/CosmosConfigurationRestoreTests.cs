using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The restore of a configuration version: a new version with the old content, the no-ops, the states of the annotation,
// the schema, the audit and the cache of the descendants. Every test has its own keys in the project.
public class CosmosConfigurationRestoreTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "restore-cfg";
    private const string View = Constants.DefaultViewName;

    private static readonly AuditActor Editor = new() { Kind = AuditActorKind.Account, Id = "user_editor", Name = "Editor" };

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configurations => Context.Services.GetRequiredService<IConfigurationService>();

    [Fact]
    public async Task Restore_OfAnOldVersion_StoresItWholeAsANewVersion()
    {
        var key = await CreateAsync("rst.old");
        await WriteAsync(key, """{ "a": 1, "b": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");
        await WriteAsync(key, """{ "c": 3 }""");

        var result = await Configurations.RestoreConfigurationVersionAsync(key, 1, Editor);

        result.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        result.FromVersion.Should().Be(1);
        result.Version.Should().Be(4);
        result.Modified.By.Id.Should().Be(Editor.Id);

        var stored = await Configurations.GetRawConfigurationAsync(key);
        stored!.Version.Should().Be(4);
        JToken.DeepEquals(stored.Content, JObject.Parse("""{ "a": 1, "b": 1 }""")).Should().BeTrue("a restore replaces, it does not merge: {0}", stored.Content);

        var versions = await Configurations.GetConfigurationVersionsAsync(key);
        versions.Select(version => version.Version).Should().Equal(1u, 2u, 3u, 4u);
        versions.Select(version => version.RestoredFrom).Should().Equal(null, null, null, 1u);
        versions.Last().Modified.By.Id.Should().Be(Editor.Id);

        // an ordinary write after the restore is no restore, and the archive of the restored version keeps its mark
        await WriteAsync(key, """{ "d": 5 }""");
        versions = await Configurations.GetConfigurationVersionsAsync(key);
        versions.Select(version => version.RestoredFrom).Should().Equal(null, null, null, 1u, null);
    }

    [Fact]
    public async Task Restore_IsInTheAnnotationsHistory()
    {
        var key = await CreateAsync("rst.audit");
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");

        await Configurations.RestoreConfigurationVersionAsync(key, 1, Editor);

        var history = await Annotations.GetAnnotationHistoryAsync(key);
        history!.Entries[0].Action.Should().Be(AnnotationAuditAction.ConfigurationRestored);
        history.Entries[0].ConfigurationVersion.Should().Be(3);
        history.Entries[0].FromVersion.Should().Be(1);
        history.Entries[0].Actor.Id.Should().Be(Editor.Id);
        history.Entries[1].Action.Should().Be(AnnotationAuditAction.ConfigurationChanged);
        history.Entries[1].FromVersion.Should().BeNull();
    }

    [Fact]
    public async Task Restore_OfTheCurrentVersion_ChangesNothing()
    {
        var key = await CreateAsync("rst.current");
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");
        var entries = (await Annotations.GetAnnotationHistoryAsync(key))!.Entries.Count;

        var result = await Configurations.RestoreConfigurationVersionAsync(key, 2, Editor);

        result.Outcome.Should().Be(VersionRestoreOutcome.Unchanged);
        result.FromVersion.Should().Be(2);
        result.Version.Should().Be(2);
        result.Modified.By.Id.Should().Be(AuditActor.System.Id);
        (await Configurations.GetConfigurationVersionsAsync(key)).Select(version => version.Version).Should().Equal(1u, 2u);
        (await Annotations.GetAnnotationHistoryAsync(key))!.Entries.Should().HaveCount(entries, "nothing changed, nothing is audited");
    }

    [Fact]
    public async Task Restore_OfAVersionThatTheCurrentOneEquals_ChangesNothing()
    {
        var key = await CreateAsync("rst.equal");
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");
        await WriteAsync(key, """{ "a": 1 }""");

        var result = await Configurations.RestoreConfigurationVersionAsync(key, 1, Editor);

        result.Outcome.Should().Be(VersionRestoreOutcome.Unchanged);
        result.FromVersion.Should().Be(1);
        result.Version.Should().Be(3);
        (await Configurations.GetConfigurationVersionsAsync(key)).Select(version => version.Version).Should().Equal(1u, 2u, 3u);
    }

    [Fact]
    public async Task Restore_AfterADelete_BringsTheConfigurationBack_AndClearsTheCachesOfTheDescendants()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("rst-tree");
        await WriteAsync(responsibility, """{ "a": 1 }""");
        await WriteAsync(responsibility, """{ "a": 2 }""");
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["a"]!.Value<int>().Should().Be(2);
        (await HasCachedCalculationAsync(unitOfExecution)).Should().BeTrue();

        (await Configurations.ClearConfigurationAsync(responsibility, Editor)).Should().BeTrue();
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content.ContainsKey("a").Should().BeFalse();

        var result = await Configurations.RestoreConfigurationVersionAsync(responsibility, 1, Editor);

        result.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        result.Version.Should().Be(3, "the numbering continues after the archived versions");
        (await Configurations.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version).Should().Equal(1u, 2u, 3u);
        (await Configurations.GetRawConfigurationAsync(responsibility))!.Content["a"]!.Value<int>().Should().Be(1);
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["a"]!.Value<int>().Should().Be(1, "the restore cleared the cached calculation");
    }

    [Fact]
    public async Task Restore_OfTheVersionThatADeleteArchived_WritesItAgain()
    {
        var key = await CreateAsync("rst.deleted-last");
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");
        await Configurations.DeleteAsync(key, Editor);

        var result = await Configurations.RestoreConfigurationVersionAsync(key, 2, Editor);

        result.Outcome.Should().Be(VersionRestoreOutcome.Restored, "a deleted configuration has no current version");
        result.Version.Should().Be(3);
        (await Configurations.GetRawConfigurationAsync(key))!.Content["a"]!.Value<int>().Should().Be(2);
    }

    [Fact]
    public async Task Restore_OfAnUnknownExpiredOrHiddenVersion_IsVersionNotFound()
    {
        var key = await CreateAsync("rst.unknown");
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");
        await WriteAsync(key, """{ "a": 3 }""");

        await ShouldBeVersionNotFoundAsync(key, 7);

        // an expired archive is gone from the container
        await GetContainer().DeleteItemAsync<JObject>(HistoryId(key, 1), key.GetCosmosPartitionKey());
        await ShouldBeVersionNotFoundAsync(key, 1);

        // an archive above the current version is a leftover the version list does not show
        var row = (await GetContainer().ReadItemAsync<JObject>(RowId(key), key.GetCosmosPartitionKey())).Resource;
        await GetContainer().UpsertItemAsync(
            new JObject
            {
                ["id"] = HistoryId(key, 5),
                ["PartitionKey"] = row["PartitionKey"],
                ["Organization"] = row["Organization"],
                ["AnnotationKey"] = row["AnnotationKey"],
                ["Name"] = row["Name"],
                ["ProjectName"] = row["ProjectName"],
                ["ViewName"] = row["ViewName"],
                ["Version"] = 5,
                ["Content"] = new JObject { ["leftover"] = true },
                ["Modified"] = row["Modified"],
            },
            key.GetCosmosPartitionKey());
        await ShouldBeVersionNotFoundAsync(key, 5);

        (await Configurations.GetRawConfigurationAsync(key))!.Version.Should().Be(3);
    }

    [Fact]
    public async Task Restore_OfASoftDeletedAnnotation_IsAnnotationDeleted()
    {
        var key = await CreateAsync("rst.soft-deleted");
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");
        await Annotations.DeleteAnnotationAsync(key);

        var act = () => Configurations.RestoreConfigurationVersionAsync(key, 1, Editor);

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);
    }

    [Fact]
    public async Task Restore_OfAPurgedOrUnknownAnnotation_IsAnnotationNotFound()
    {
        var key = await CreateAsync("rst.purged");
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");
        await Configurations.RestoreConfigurationVersionAsync(key, 1, Editor);
        await Annotations.DeleteAnnotationAsync(key);
        await Annotations.PurgeAnnotationAsync(key, AnnotationServiceTestExtensions.TestActor);

        var versions = await Configurations.GetConfigurationVersionsAsync(key);
        versions.Select(version => version.Version).Should().Equal(1u, 2u, 3u);
        versions.Last().RestoredFrom.Should().Be(1, "the purge keeps the last content with its mark");

        var purged = () => Configurations.RestoreConfigurationVersionAsync(key, 1, Editor);
        (await purged.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);

        var unknown = () => Configurations.RestoreConfigurationVersionAsync(Key("rst.never"), 1, Editor);
        (await unknown.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);
    }

    [Fact]
    public async Task Restore_IsCheckedAgainstTheSchema_UnlessForced()
    {
        const string project = "restore-cfg-schema";
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var key = await CreateAsync("rst.schema", project);
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 1, "b": 1 }""");
        await schemas.SetSchemaAsync(
            TestConstants.Organization,
            project,
            View,
            AnnotationTypeCodes.Responsibility,
            JObject.Parse("""{ "type": "object", "required": [ "b" ] }"""),
            Editor,
            false);

        var act = () => Configurations.RestoreConfigurationVersionAsync(key, 1, Editor);

        await act.Should().ThrowAsync<SchemaValidationException>();
        (await Configurations.GetConfigurationVersionsAsync(key)).Select(version => version.Version).Should().Equal(new[] { 1u, 2u }, "a rejected restore leaves no version");

        var forced = await Configurations.RestoreConfigurationVersionAsync(key, 1, Editor, force: true);
        forced.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        forced.Version.Should().Be(3);
    }

    [Fact]
    public async Task Restore_CopiesTheStoredContentAsItIs()
    {
        var key = await CreateAsync("rst.verbatim");
        await WriteAsync(key, """{ "a": 1 }""");

        // a JSON Patch stores a literal "$remove", which a write would read as a directive
        await Configurations.PatchConfigurationAsync(key, JArray.Parse("""[{ "op": "add", "path": "/$remove", "value": [ "$.a" ] }]"""), AuditActor.System);
        await Configurations.PatchConfigurationAsync(key, JArray.Parse("""[{ "op": "replace", "path": "/a", "value": 2 }]"""), AuditActor.System);

        var result = await Configurations.RestoreConfigurationVersionAsync(key, 2, Editor);

        result.Version.Should().Be(4);
        var stored = (await Configurations.GetRawConfigurationAsync(key))!.Content;
        JToken.DeepEquals(stored, JObject.Parse("""{ "a": 1, "$remove": [ "$.a" ] }""")).Should().BeTrue("the stored version is copied as it is: {0}", stored);
    }

    [Fact]
    public async Task Restore_RetriesAConcurrentChange_AndGivesUpAfterThreeMore()
    {
        var key = await CreateAsync("rst.concurrent");
        await WriteAsync(key, """{ "a": 1 }""");
        await WriteAsync(key, """{ "a": 2 }""");
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var configs = ActivatorUtilities.CreateInstance<CosmosConfigurationService>(Context.Services, storage);
        var rowId = RowId(key);
        var changes = 0;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, rowId) && changes++ == 0)
            {
                await TouchRowAsync(key);
            }

            return null;
        };

        var restored = await configs.RestoreConfigurationVersionAsync(key, 1, Editor);

        changes.Should().Be(2, "the first attempt lost to the concurrent change, the second wrote");
        restored.Version.Should().Be(3);

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, rowId))
            {
                await TouchRowAsync(key);
            }

            return null;
        };

        var failure = await FluentActions.Awaiting(() => configs.RestoreConfigurationVersionAsync(key, 2, Editor))
            .Should().ThrowAsync<ConflictException>();
        failure.Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        (await Configurations.GetRawConfigurationAsync(key))!.Version.Should().Be(3);
    }

    private static FullKey Key(string key, string project = Project)
    {
        return FullKey.Create(AnnotationKey.Parse(key), TestConstants.Organization, project, View);
    }

    private static string RowId(FullKey key)
    {
        return $"{key.ViewName}.cnf.{key.Annotation}";
    }

    private static string HistoryId(FullKey key, ulong version)
    {
        return $"{key.ViewName}.cfv.{key.Annotation}.{version}";
    }

    private async Task<FullKey> CreateAsync(string key, string project = Project)
    {
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), project, View));
        return Key(key, project);
    }

    private async Task<(FullKey Responsibility, FullKey UnitOfExecution)> CreateTreeAsync(string project)
    {
        var annotationKey = AnnotationKey.CreateUnitOfExecution("s", "r", "c", "u");
        await Annotations.CreateAnnotationsFromStringAsync(TestConstants.Organization, project, View, [annotationKey.ToString()], AnnotationServiceTestExtensions.TestActor);
        var unitOfExecution = FullKey.Create(annotationKey, TestConstants.Organization, project, View);
        return (FullKey.Create(annotationKey.GetResponsibilityKey(), unitOfExecution), unitOfExecution);
    }

    private Task WriteAsync(FullKey key, string json)
    {
        return Configurations.CreateOrUpdateConfigurationAsync(key, JObject.Parse(json), AuditActor.System);
    }

    private async Task ShouldBeVersionNotFoundAsync(FullKey key, uint version)
    {
        var act = () => Configurations.RestoreConfigurationVersionAsync(key, version, Editor);
        (await act.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.VersionNotFound);
    }

    private Container GetContainer()
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(TestConstants.Organization)
            .Container;
    }

    private async Task<bool> HasCachedCalculationAsync(FullKey key)
    {
        var row = (await GetContainer().ReadItemAsync<JObject>(RowId(key), key.GetCosmosPartitionKey())).Resource;
        return row["CalculatedContent"]?.Type == JTokenType.Object;
    }

    // A change of the row that leaves its documents as they are, like a cache write or an invalidation.
    private async Task TouchRowAsync(FullKey key)
    {
        using var response = await GetContainer().PatchItemStreamAsync(
            RowId(key),
            key.GetCosmosPartitionKey(),
            [PatchOperation.Increment("/AffectedCounter", 1)]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
