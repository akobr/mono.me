using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The configuration writes when the storage fails or other writers interleave: the service under test runs over an
// InterceptedRepositoryProvider, the setup and the checks go through the shared services. Every test has its own
// project.
public class CosmosConfigurationRecoveryTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string View = Constants.DefaultViewName;

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configurations => Context.Services.GetRequiredService<IConfigurationService>();

    [Theory]
    [InlineData("update")]
    [InlineData("patch")]
    [InlineData("create")]
    [InlineData("import")]
    public async Task FailedInvalidation_CommitsTheWrite_AndARepeatedRequestCompletesIt(string operation)
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync($"rec-fail-{operation}");
        var (configs, storage) = CreateService();

        if (operation == "create")
        {
            // the descendant caches its own document, the first write of the responsibility has to clear it
            await Configurations.CreateOrUpdateConfigurationAsync(unitOfExecution, JObject.Parse("""{ "own": 1 }"""), AuditActor.System);
        }
        else
        {
            await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        }

        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content.Value<int?>("a").Should().Be(operation == "create" ? null : 1);
        (await HasCachedCalculationAsync(unitOfExecution)).Should().BeTrue();
        var expectedVersion = operation == "create" ? 1UL : 2UL;

        Task Act() => operation switch
        {
            "patch" => configs.PatchConfigurationAsync(responsibility, JArray.Parse("""[{ "op": "add", "path": "/a", "value": 2 }]"""), AuditActor.System),
            "import" => configs.ImportConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), new ViewImportOptions { Actor = AuditActor.System }, new ViewImportOverlay()),
            _ => configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System),
        };

        storage.Interceptor.BeforeBatch = FailInvalidation;
        await FluentActions.Awaiting(Act).Should().ThrowAsync<ConfigurationStorageException>();

        var committed = await ReadRowAsync(responsibility);
        committed["Version"]!.Value<ulong>().Should().Be(expectedVersion);
        committed["Content"]!["a"]!.Value<int>().Should().Be(2);
        committed.Value<string>("PendingInvalidationId").Should().NotBeNullOrEmpty();
        (await HasCachedCalculationAsync(unitOfExecution)).Should().BeTrue("the invalidation failed");

        storage.Interceptor.Reset();
        await Act();

        var completed = await ReadRowAsync(responsibility);
        completed["Version"]!.Value<ulong>().Should().Be(expectedVersion, "the repeated request changes nothing");
        completed.ContainsKey("PendingInvalidationId").Should().BeFalse();
        (await HasCachedCalculationAsync(unitOfExecution)).Should().BeFalse();
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["a"]!.Value<int>().Should().Be(2);

        var next = await configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 3 }"""), AuditActor.System);
        next.Version.Should().Be(expectedVersion + 1);
    }

    [Fact]
    public async Task FailedInvalidation_IsCompletedByARequestThatFails()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("rec-fail-test-op");
        var (configs, storage) = CreateService();
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.GetRawConfigurationAsync(unitOfExecution);
        var patch = JArray.Parse("""[{ "op": "test", "path": "/a", "value": 1 }, { "op": "replace", "path": "/a", "value": 2 }]""");

        storage.Interceptor.BeforeBatch = FailInvalidation;
        await FluentActions.Awaiting(() => configs.PatchConfigurationAsync(responsibility, patch, AuditActor.System))
            .Should().ThrowAsync<ConfigurationStorageException>();
        (await HasCachedCalculationAsync(unitOfExecution)).Should().BeTrue();

        // the repeated patch fails its test on the committed version, and still finishes the invalidation
        storage.Interceptor.Reset();
        var failure = await FluentActions.Awaiting(() => configs.PatchConfigurationAsync(responsibility, patch, AuditActor.System))
            .Should().ThrowAsync<JsonPatchException>();
        failure.Which.Kind.Should().Be(JsonPatchFailureKind.TestFailed);

        (await HasCachedCalculationAsync(unitOfExecution)).Should().BeFalse();
        (await ReadRowAsync(responsibility)).ContainsKey("PendingInvalidationId").Should().BeFalse();
    }

    [Fact]
    public async Task FailedInvalidation_IsCompletedByADifferentWrite_InOneAttempt()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("rec-fail-then-write");
        var (configs, storage) = CreateService();
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.GetRawConfigurationAsync(unitOfExecution);
        var rowId = RowId(responsibility);

        storage.Interceptor.BeforeBatch = FailInvalidation;
        await FluentActions.Awaiting(() => configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System))
            .Should().ThrowAsync<ConfigurationStorageException>();

        // finishing the pending invalidation changes the row, the write reads it again instead of losing an attempt
        storage.Interceptor.Reset();
        var written = await configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 3 }"""), AuditActor.System);

        written.Version.Should().Be(3);
        storage.Interceptor.Batches.Count(batch => batch.Contains(BatchOperationKind.Replace, rowId)).Should().Be(1);
        (await ReadRowAsync(responsibility)).ContainsKey("PendingInvalidationId").Should().BeFalse();
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["a"]!.Value<int>().Should().Be(3);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("patch")]
    [InlineData("import")]
    [InlineData("clear")]
    [InlineData("delete")]
    public async Task OrphanHistory_OfTheCurrentVersion_CountsAsArchived(string operation)
    {
        var responsibility = await CreateResponsibilityAsync($"rec-orphan-{operation}");
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        // what a failed write of the old code left behind: the archive of the current version next to it
        var orphan = await InsertHistoryAsync(responsibility, 2, await ReadRowAsync(responsibility));
        (await Configurations.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version).Should().Equal(1U, 2U);

        switch (operation)
        {
            case "write":
                (await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 3 }"""), AuditActor.System)).Version.Should().Be(3);
                break;

            case "patch":
                (await Configurations.PatchConfigurationAsync(responsibility, JArray.Parse("""[{ "op": "replace", "path": "/a", "value": 3 }]"""), AuditActor.System)).Version.Should().Be(3);
                break;

            case "import":
                var result = await Configurations.ImportConfigurationAsync(
                    responsibility,
                    JObject.Parse("""{ "a": 3 }"""),
                    new ViewImportOptions { Actor = AuditActor.System },
                    new ViewImportOverlay());
                result.Outcome.Should().Be(ViewImportOutcome.Updated);
                result.Version.Should().Be(3);
                break;

            case "clear":
                (await Configurations.ClearConfigurationAsync(responsibility)).Should().BeTrue();
                break;

            default:
                await Configurations.DeleteAsync(responsibility);
                break;
        }

        var versions = (await Configurations.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version).ToList();
        versions.Should().OnlyHaveUniqueItems();
        versions.Should().Equal(operation is "clear" or "delete" ? new uint[] { 1, 2 } : new uint[] { 1, 2, 3 });

        var stored = await ReadItemAsync(responsibility, orphan);
        stored["Content"]!["a"]!.Value<int>().Should().Be(2);
    }

    [Fact]
    public async Task CollidingHistory_IsNeverOverwritten()
    {
        var responsibility = await CreateResponsibilityAsync("rec-collision");
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);

        // an archive of an earlier lifetime holds the current version with other content
        var row = await ReadRowAsync(responsibility);
        row["Content"] = JObject.Parse("""{ "earlier": 1 }""");
        var colliding = await InsertHistoryAsync(responsibility, 1, row);

        var written = await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        written.Version.Should().Be(3);
        (await ReadItemAsync(responsibility, colliding))["Content"]!["earlier"]!.Value<int>().Should().Be(1);
        (await ReadItemAsync(responsibility, HistoryId(responsibility, 2)))["Content"]!["a"]!.Value<int>().Should().Be(1);
        (await Configurations.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version).Should().Equal(1U, 2U, 3U);
        (await Configurations.GetConfigurationVersionContentAsync(responsibility, 3))!.Content["a"]!.Value<int>().Should().Be(2);
    }

    [Fact]
    public async Task CollidingHistory_ArchivedByAnEarlierAttempt_IsNotArchivedAgain()
    {
        var responsibility = await CreateResponsibilityAsync("rec-collision-again");
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        var row = await ReadRowAsync(responsibility);
        var earlier = (JObject)row.DeepClone();
        earlier["Content"] = JObject.Parse("""{ "earlier": 1 }""");
        await InsertHistoryAsync(responsibility, 1, earlier);

        // what an attempt that archived the current content after the collision and then lost its write left behind
        await InsertHistoryAsync(responsibility, 2, row);

        var written = await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        written.Version.Should().Be(3);
        (await HasItemAsync(responsibility, HistoryId(responsibility, 3))).Should().BeFalse("the current content is archived once, as version 2");
        (await Configurations.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version).Should().Equal(1U, 2U, 3U);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("delete")]
    public async Task FailedClearOrDelete_Throws_AndKeepsTheDocument(string operation)
    {
        var responsibility = await CreateResponsibilityAsync($"rec-failed-{operation}");
        var (configs, storage) = CreateService();
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        var rowId = RowId(responsibility);

        storage.Interceptor.BeforeBatch = batch => Task.FromResult<HttpStatusCode?>(
            batch.Contains(BatchOperationKind.Delete, rowId) ? HttpStatusCode.ServiceUnavailable : null);

        Task Act() => operation == "clear"
            ? configs.ClearConfigurationAsync(responsibility, AnnotationServiceTestExtensions.TestActor)
            : configs.DeleteAsync(responsibility, AnnotationServiceTestExtensions.TestActor);

        await FluentActions.Awaiting(Act).Should().ThrowAsync<ConfigurationStorageException>();

        var row = await ReadRowAsync(responsibility);
        row["Version"]!.Value<ulong>().Should().Be(1);
        row["Content"]!["a"]!.Value<int>().Should().Be(1);
        var history = await Annotations.GetAnnotationHistoryAsync(responsibility);
        history!.Entries.Should().NotContain(entry => entry.Action == AnnotationAuditAction.ConfigurationDeleted);
    }

    [Fact]
    public async Task FailedClearInvalidation_IsCompletedByARepeatedClear()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("rec-clear");
        var (configs, storage) = CreateService();
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.GetRawConfigurationAsync(unitOfExecution);

        storage.Interceptor.BeforeBatch = FailInvalidation;
        await FluentActions.Awaiting(() => configs.ClearConfigurationAsync(responsibility)).Should().ThrowAsync<ConfigurationStorageException>();

        (await Configurations.HasConfigurationContentAsync(responsibility)).Should().BeFalse("the clear committed");
        (await ReadItemAsync(responsibility, HistoryId(responsibility, 1))).Value<string>("PendingInvalidationId").Should().NotBeNullOrEmpty();
        (await HasCachedCalculationAsync(unitOfExecution)).Should().BeTrue("the invalidation failed");

        storage.Interceptor.Reset();
        (await configs.ClearConfigurationAsync(responsibility)).Should().BeFalse();

        (await HasCachedCalculationAsync(unitOfExecution)).Should().BeFalse();
        (await ReadItemAsync(responsibility, HistoryId(responsibility, 1))).ContainsKey("PendingInvalidationId").Should().BeFalse();
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content.Should().NotContainKey("a");

        // nothing is pending any more, so a clear that finds nothing writes nothing
        storage.Interceptor.Reset();
        (await configs.ClearConfigurationAsync(responsibility)).Should().BeFalse();
        storage.Interceptor.Batches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("update")]
    [InlineData("patch")]
    [InlineData("clear")]
    [InlineData("delete")]
    public async Task Writes_CommitBeforeTheyInvalidate(string operation)
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync($"rec-order-{operation}");
        var (configs, storage) = CreateService();
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.GetRawConfigurationAsync(unitOfExecution);
        var rowId = RowId(responsibility);

        switch (operation)
        {
            case "update":
                await configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);
                break;

            case "patch":
                await configs.PatchConfigurationAsync(responsibility, JArray.Parse("""[{ "op": "replace", "path": "/a", "value": 2 }]"""), AuditActor.System);
                break;

            case "clear":
                await configs.ClearConfigurationAsync(responsibility);
                break;

            default:
                await configs.DeleteAsync(responsibility);
                break;
        }

        var batches = storage.Interceptor.Batches;
        var write = batches.Single(batch => !batch.IsInvalidation && (batch.Contains(BatchOperationKind.Replace, rowId) || batch.Contains(BatchOperationKind.Delete, rowId)));
        var invalidations = batches.Where(batch => batch.IsInvalidation).ToList();

        invalidations.Should().NotBeEmpty();
        write.End.Should().BeLessThan(invalidations.Min(batch => batch.Start));
    }

    [Fact]
    public async Task ReadDuringTheInvalidation_SeesTheWrittenAncestor()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("rec-read-during");
        var (configs, storage) = CreateService();
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.GetRawConfigurationAsync(unitOfExecution);
        var descendantId = RowId(unitOfExecution);
        JObject readDuring = null;

        // right after the descendant's patch, while the rest of the invalidation may still run
        storage.Interceptor.AfterBatch = async batch =>
        {
            if (batch.IsInvalidation && batch.Contains(BatchOperationKind.Patch, descendantId) && readDuring is null)
            {
                readDuring = (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content;
            }
        };

        await configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        readDuring.Should().NotBeNull();
        readDuring!["a"]!.Value<int>().Should().Be(2);
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["a"]!.Value<int>().Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentChange_IsRetried()
    {
        var responsibility = await CreateResponsibilityAsync("rec-retry");
        var (configs, storage) = CreateService();
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        var rowId = RowId(responsibility);
        var changes = 0;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, rowId) && changes++ == 0)
            {
                await TouchRowAsync(responsibility);
            }

            return null;
        };

        var written = await configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        changes.Should().Be(2, "the first attempt lost to the concurrent change, the second wrote");
        written.Version.Should().Be(2);
        (await Configurations.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version).Should().Equal(1U, 2U);

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, rowId))
            {
                await TouchRowAsync(responsibility);
            }

            return null;
        };

        var failure = await FluentActions.Awaiting(() => configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 3 }"""), AuditActor.System))
            .Should().ThrowAsync<ConflictException>();
        failure.Which.ErrorCode.Should().Be(ErrorCodes.Conflict);
        (await ReadRowAsync(responsibility))["Version"]!.Value<ulong>().Should().Be(2);
    }

    [Fact]
    public async Task SoftDelete_BetweenReadAndWrite_IsNotOverwritten()
    {
        var responsibility = await CreateResponsibilityAsync("rec-soft-delete");
        var (configs, storage) = CreateService();
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        var rowId = RowId(responsibility);
        var deleted = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, rowId) && !deleted)
            {
                deleted = true;
                await Annotations.DeleteAnnotationAsync(responsibility);
            }

            return null;
        };

        var failure = await FluentActions.Awaiting(() => configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System))
            .Should().ThrowAsync<ConflictException>();

        failure.Which.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);
        var row = await ReadRowAsync(responsibility);
        row.Value<string>("AnnotationDeletionId").Should().NotBeNullOrEmpty();
        row["Content"]!["a"]!.Value<int>().Should().Be(1);
    }

    [Fact]
    public async Task Patch_OfACacheRow_ContinuesAfterTheArchivedVersions()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("rec-cache-row");
        var unit = FullKey.Create(unitOfExecution.Annotation.GetUnitKey(), unitOfExecution);
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.CreateOrUpdateConfigurationAsync(unit, JObject.Parse("""{ "u": 1 }"""), AuditActor.System);
        await Configurations.CreateOrUpdateConfigurationAsync(unit, JObject.Parse("""{ "u": 2 }"""), AuditActor.System);
        await Configurations.ClearConfigurationAsync(unit);

        // the read caches the inherited document in a row of version 0
        await Configurations.GetRawConfigurationAsync(unit);
        (await ReadRowAsync(unit))["Version"]!.Value<ulong>().Should().Be(0);

        var patched = await Configurations.PatchConfigurationAsync(unit, JArray.Parse("""[{ "op": "add", "path": "/u", "value": 3 }]"""), AuditActor.System);

        patched.Version.Should().Be(3);
        (await HasItemAsync(unit, HistoryId(unit, 0))).Should().BeFalse();
        (await Configurations.GetConfigurationVersionsAsync(unit)).Select(version => version.Version).Should().Equal(1U, 2U, 3U);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("import")]
    public async Task EmptiedVersion_IsArchived(string operation)
    {
        var responsibility = await CreateResponsibilityAsync($"rec-emptied-{operation}");
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        var emptied = await Configurations.PatchConfigurationAsync(responsibility, JArray.Parse("""[{ "op": "remove", "path": "/a" }]"""), AuditActor.System);
        emptied.Version.Should().Be(2);

        // an empty document over the emptied version changes nothing
        (await Configurations.CreateOrUpdateConfigurationAsync(responsibility, new JObject(), AuditActor.System)).Version.Should().Be(2);

        if (operation == "write")
        {
            (await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "b": 1 }"""), AuditActor.System)).Version.Should().Be(3);
        }
        else
        {
            var result = await Configurations.ImportConfigurationAsync(
                responsibility,
                JObject.Parse("""{ "b": 1 }"""),
                new ViewImportOptions { Actor = AuditActor.System },
                new ViewImportOverlay());
            result.Version.Should().Be(3);
        }

        (await HasItemAsync(responsibility, HistoryId(responsibility, 2))).Should().BeTrue();
        (await Configurations.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version).Should().Equal(1U, 2U, 3U);
        var changes = await Configurations.GetConfigurationVersionChangesAsync(responsibility, 3);
        changes.Should().NotBeNull();
    }

    [Fact]
    public async Task VersionZero_WithContent_IsArchived()
    {
        var responsibility = await CreateResponsibilityAsync("rec-version-zero");
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);

        // a document written before versions were counted
        var row = await ReadRowAsync(responsibility);
        row["Version"] = 0;
        await GetContainer().ReplaceItemAsync(row, RowId(responsibility), new PartitionKey(responsibility.GetPartitionKey()));

        var written = await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        written.Version.Should().Be(1);
        (await ReadItemAsync(responsibility, HistoryId(responsibility, 0)))["Content"]!["a"]!.Value<int>().Should().Be(1);
        (await Configurations.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version).Should().Equal(0U, 1U);

        // a cache row, version 0 without content, does not hide the archived version 0
        await Configurations.ClearConfigurationAsync(responsibility);
        var cacheRow = (JObject)row.DeepClone();
        cacheRow["Content"] = new JObject();
        await GetContainer().UpsertItemAsync(cacheRow, new PartitionKey(responsibility.GetPartitionKey()));
        (await Configurations.GetConfigurationVersionContentAsync(responsibility, 0))!.Content["a"]!.Value<int>().Should().Be(1);
    }

    [Fact]
    public async Task UnitOfExecution_WritesAndClearsStoreNoToken()
    {
        var (_, unitOfExecution) = await CreateTreeAsync("rec-uxe-token");
        var (configs, storage) = CreateService();

        await configs.CreateOrUpdateConfigurationAsync(unitOfExecution, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        (await ReadRowAsync(unitOfExecution)).ContainsKey("PendingInvalidationId").Should().BeFalse();

        storage.Interceptor.Reset();
        (await configs.ClearConfigurationAsync(unitOfExecution)).Should().BeTrue();

        (await ReadItemAsync(unitOfExecution, HistoryId(unitOfExecution, 1))).ContainsKey("PendingInvalidationId").Should().BeFalse();
        storage.Interceptor.Batches.Should().ContainSingle("the clear writes its archive and deletes the row, and has nothing to invalidate");
    }

    private static Task<HttpStatusCode?> FailInvalidation(RecordedBatch batch)
    {
        return Task.FromResult<HttpStatusCode?>(batch.IsInvalidation ? HttpStatusCode.ServiceUnavailable : null);
    }

    private static string RowId(FullKey key)
    {
        return $"{key.ViewName}.cnf.{key.Annotation}";
    }

    private static string HistoryId(FullKey key, ulong version)
    {
        return $"{key.ViewName}.cfv.{key.Annotation}.{version}";
    }

    private (CosmosConfigurationService Configs, InterceptedRepositoryProvider Storage) CreateService()
    {
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var configs = ActivatorUtilities.CreateInstance<CosmosConfigurationService>(Context.Services, storage);
        return (configs, storage);
    }

    private async Task<FullKey> CreateResponsibilityAsync(string project)
    {
        var annotationKey = AnnotationKey.CreateResponsibility("r");
        await Annotations.CreateAnnotationsFromStringAsync(TestConstants.Organization, project, View, [annotationKey.ToString()], AnnotationServiceTestExtensions.TestActor);
        return FullKey.Create(annotationKey, TestConstants.Organization, project, View);
    }

    private async Task<(FullKey Responsibility, FullKey UnitOfExecution)> CreateTreeAsync(string project)
    {
        var annotationKey = AnnotationKey.CreateUnitOfExecution("s", "r", "c", "u");
        await Annotations.CreateAnnotationsFromStringAsync(TestConstants.Organization, project, View, [annotationKey.ToString()], AnnotationServiceTestExtensions.TestActor);
        var unitOfExecution = FullKey.Create(annotationKey, TestConstants.Organization, project, View);
        return (FullKey.Create(annotationKey.GetResponsibilityKey(), unitOfExecution), unitOfExecution);
    }

    private Container GetContainer()
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(TestConstants.Organization)
            .Container;
    }

    private Task<JObject> ReadRowAsync(FullKey key)
    {
        return ReadItemAsync(key, RowId(key));
    }

    private async Task<JObject> ReadItemAsync(FullKey key, string id)
    {
        var response = await GetContainer().ReadItemAsync<JObject>(id, new PartitionKey(key.GetPartitionKey()));
        return response.Resource;
    }

    private async Task<bool> HasItemAsync(FullKey key, string id)
    {
        using var response = await GetContainer().ReadItemStreamAsync(id, new PartitionKey(key.GetPartitionKey()));
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> HasCachedCalculationAsync(FullKey key)
    {
        return (await ReadRowAsync(key))["CalculatedContent"]?.Type == JTokenType.Object;
    }

    // An archive of the given version, made of the fields of a row as the history items hold them.
    private async Task<string> InsertHistoryAsync(FullKey key, ulong version, JObject row)
    {
        var id = HistoryId(key, version);
        var history = new JObject
        {
            ["id"] = id,
            ["PartitionKey"] = row["PartitionKey"],
            ["AnnotationKey"] = row["AnnotationKey"],
            ["Name"] = row["Name"],
            ["ProjectName"] = row["ProjectName"],
            ["ViewName"] = row["ViewName"],
            ["Version"] = version,
            ["Content"] = row["Content"],
            ["Modified"] = row["Modified"],
        };

        await GetContainer().UpsertItemAsync(history, new PartitionKey(key.GetPartitionKey()));
        return id;
    }

    // A change of the row that leaves its documents as they are, like a cache write or an invalidation.
    private async Task TouchRowAsync(FullKey key)
    {
        using var response = await GetContainer().PatchItemStreamAsync(
            RowId(key),
            new PartitionKey(key.GetPartitionKey()),
            [PatchOperation.Increment("/AffectedCounter", 1)]);

        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
