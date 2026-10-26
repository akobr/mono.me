using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The order of the cache invalidation and the guard of the cache writes: the service under test runs over an
// InterceptedRepositoryProvider, the setup and the concurrent writes go through the shared services. Every test has
// its own project.
public class CosmosConfigurationInvalidationOrderTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string View = Constants.DefaultViewName;

    // Cosmos DB limit of operations in one transactional batch (ConfigurationCacheInvalidator.MaxBatchOperations)
    private const int MaxBatchOperations = 100;

    private static readonly Dictionary<string, int> Levels = new()
    {
        { "rst", 0 },
        { "sbt", 0 },
        { "unt", 1 },
        { "usg", 1 },
        { "cnt", 1 },
        { "exe", 2 },
        { "uxe", 3 },
    };

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configurations => Context.Services.GetRequiredService<IConfigurationService>();

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    [Fact]
    public async Task ResponsibilityWrite_InvalidatesLevelByLevel()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("order-write");
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var configs = ActivatorUtilities.CreateInstance<CosmosConfigurationService>(Context.Services, storage);
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);

        // the read caches the unit, the usage, the execution and the unit of execution
        await Configurations.GetRawConfigurationAsync(unitOfExecution);

        // more executions of the responsibility than one batch holds
        var executions = await InsertCachedExecutionsAsync(responsibility, MaxBatchOperations + 5);

        await configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        var invalidations = storage.Interceptor.Batches.Where(batch => batch.IsInvalidation).ToList();
        invalidations.Select(LevelOf).Distinct().Order().Should().Equal(1, 2, 3);
        invalidations.Count(batch => LevelOf(batch) == 2).Should().BeGreaterThan(1, "the executions do not fit one batch");
        ShouldBeOrderedByLevel(invalidations);
        invalidations.Should().NotContain(batch => batch.Contains(BatchOperationKind.Patch, RowId(responsibility)), "the write cleared its own cache");

        (await HasCachedCalculationAsync(responsibility, executions[0])).Should().BeFalse();
        (await HasCachedCalculationAsync(responsibility, executions[^1])).Should().BeFalse();
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["a"]!.Value<int>().Should().Be(2);
    }

    [Fact]
    public async Task ItemCreatedDuringTheInvalidation_IsReachedByALaterLevel()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("order-late");
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var configs = ActivatorUtilities.CreateInstance<CosmosConfigurationService>(Context.Services, storage);
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.GetRawConfigurationAsync(unitOfExecution);
        string lateId = null;

        // a cached execution appears while the level above it is patched; its level is queried only afterwards
        storage.Interceptor.AfterBatch = async batch =>
        {
            if (lateId is null && batch.IsInvalidation && LevelOf(batch) == 1)
            {
                lateId = (await InsertCachedExecutionsAsync(responsibility, 1, "late"))[0];
            }
        };

        await configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);

        lateId.Should().NotBeNull();
        (await HasCachedCalculationAsync(responsibility, lateId)).Should().BeFalse();
    }

    [Fact]
    public async Task ResponsibilityTemplateWrite_InvalidatesLevelByLevel()
    {
        var project = "order-template";
        var (responsibility, unitOfExecution) = await CreateTreeAsync(project);
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var templates = ActivatorUtilities.CreateInstance<CosmosConfigurationTemplateService>(Context.Services, storage);
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Configurations.GetRawConfigurationAsync(unitOfExecution);
        await InsertCachedExecutionsAsync(responsibility, MaxBatchOperations + 5);

        await templates.CreateOrUpdateTemplateAsync(TestConstants.Organization, project, View, "rst", JObject.Parse("""{ "t": 1 }"""), AuditActor.System);

        var invalidations = storage.Interceptor.Batches.Where(batch => batch.IsInvalidation).ToList();
        invalidations.Select(LevelOf).Distinct().Order().Should().Equal(0, 1, 2, 3);
        ShouldBeOrderedByLevel(invalidations);
        invalidations.Where(batch => LevelOf(batch) == 0).Should().Contain(batch => batch.Contains(BatchOperationKind.Patch, RowId(responsibility)));
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["t"]!.Value<int>().Should().Be(1);
    }

    [Fact]
    public async Task AncestorWritten_DuringTheCreateOfARow_IsNotCachedOld()
    {
        var (responsibility, unitOfExecution) = await CreateTreeAsync("order-create");
        var unit = FullKey.Create(unitOfExecution.Annotation.GetUnitKey(), unitOfExecution);
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var configs = ActivatorUtilities.CreateInstance<CosmosConfigurationService>(Context.Services, storage);
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        (await HasItemAsync(unit, RowId(unit))).Should().BeFalse("the unit's row is created by the read below");
        var written = false;

        // the calculation of the unit reads the responsibility, then the responsibility is written; the unit's row
        // does not exist yet, so that write's invalidation cannot reach it
        storage.Interceptor.AfterRead = async id =>
        {
            if (id == RowId(responsibility) && !written)
            {
                written = true;
                await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 2 }"""), AuditActor.System);
            }
        };

        var read = await configs.GetRawConfigurationAsync(unit);

        written.Should().BeTrue();
        read!.Content["a"]!.Value<int>().Should().Be(2);
        var row = await ReadItemAsync(unit, RowId(unit));
        row["CalculatedContent"]?["a"]?.Value<int>().Should().NotBe(1);
        (await Configurations.GetRawConfigurationAsync(unit))!.Content["a"]!.Value<int>().Should().Be(2);
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["a"]!.Value<int>().Should().Be(2);
    }

    [Fact]
    public async Task TemplateWritten_BetweenTwoVisitsOfARow_IsNotCachedOld()
    {
        var project = "order-template-revisit";
        var (responsibility, unitOfExecution) = await CreateTreeAsync(project);
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var configs = ActivatorUtilities.CreateInstance<CosmosConfigurationService>(Context.Services, storage);
        await Templates.CreateOrUpdateTemplateAsync(TestConstants.Organization, project, View, "rst", JObject.Parse("""{ "t": 1 }"""), AuditActor.System);
        await Configurations.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        var templateId = $"{View}.gen.rst";
        var written = false;

        // a unit of execution reaches the responsibility twice, through its unit and through its execution; the
        // template is written after the first visit read it
        storage.Interceptor.AfterRead = async id =>
        {
            if (id == templateId && !written)
            {
                written = true;
                await Templates.CreateOrUpdateTemplateAsync(TestConstants.Organization, project, View, "rst", JObject.Parse("""{ "t": 2 }"""), AuditActor.System);
            }
        };

        await configs.GetRawConfigurationAsync(unitOfExecution);

        written.Should().BeTrue();
        (await ReadItemAsync(responsibility, RowId(responsibility)))["CalculatedContent"]?["t"]?.Value<int>().Should().NotBe(1);
        (await Configurations.GetRawConfigurationAsync(responsibility))!.Content["t"]!.Value<int>().Should().Be(2);
        (await Configurations.GetRawConfigurationAsync(unitOfExecution))!.Content["t"]!.Value<int>().Should().Be(2);
    }

    private static string RowId(FullKey key)
    {
        return $"{key.ViewName}.cnf.{key.Annotation}";
    }

    // {view}.cnf.{code}.…
    private static int LevelOf(RecordedBatch batch)
    {
        var levels = batch.Operations.Select(operation => Levels[operation.Id.Split('.')[2]]).Distinct().ToList();
        levels.Should().ContainSingle("a batch holds the items of one level");
        return levels[0];
    }

    private static void ShouldBeOrderedByLevel(IReadOnlyList<RecordedBatch> invalidations)
    {
        foreach (var earlier in invalidations)
        {
            foreach (var later in invalidations.Where(batch => LevelOf(batch) > LevelOf(earlier)))
            {
                earlier.End.Should().BeLessThan(later.Start, "every batch of a level ends before the next level starts");
            }
        }
    }

    private async Task<(FullKey Responsibility, FullKey UnitOfExecution)> CreateTreeAsync(string project)
    {
        var annotationKey = AnnotationKey.CreateUnitOfExecution("s", "r", "c", "u");
        await Annotations.CreateAnnotationsFromStringAsync(TestConstants.Organization, project, View, [annotationKey.ToString()], AnnotationServiceTestExtensions.TestActor);
        var unitOfExecution = FullKey.Create(annotationKey, TestConstants.Organization, project, View);
        return (FullKey.Create(annotationKey.GetResponsibilityKey(), unitOfExecution), unitOfExecution);
    }

    // Cached rows of executions without annotations: the invalidation needs only their id, project and view. They are
    // written in transactional batches, a burst of single writes times out on a small emulator.
    private async Task<List<string>> InsertCachedExecutionsAsync(FullKey responsibility, int count, string name = "raw")
    {
        var partitionKey = new PartitionKey(responsibility.GetPartitionKey());
        var ids = Enumerable.Range(0, count)
            .Select(index => $"{View}.cnf.exe.{name}{index}.{responsibility.Annotation.Name}.c")
            .ToList();

        foreach (var chunk in ids.Chunk(MaxBatchOperations))
        {
            var batch = GetContainer().CreateTransactionalBatch(partitionKey);

            foreach (var id in chunk)
            {
                batch.UpsertItem(new JObject
                {
                    ["id"] = id,
                    ["PartitionKey"] = responsibility.GetPartitionKey(),
                    ["AnnotationKey"] = id[$"{View}.cnf.".Length..],
                    ["Name"] = "c",
                    ["ProjectName"] = responsibility.ProjectName,
                    ["ViewName"] = View,
                    ["Version"] = 0,
                    ["AffectedCounter"] = 0,
                    ["Content"] = new JObject(),
                    ["CalculatedContent"] = JObject.Parse("""{ "a": 1 }"""),
                    ["CalculatedContentHash"] = "00000000",
                    ["Modified"] = JObject.FromObject(AuditStamp.Of(AuditActor.System, System.DateTimeOffset.UtcNow)),
                });
            }

            using var response = await batch.ExecuteAsync();
            response.IsSuccessStatusCode.Should().BeTrue();
        }

        return ids;
    }

    private Container GetContainer()
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(TestConstants.Organization)
            .Container;
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

    private async Task<bool> HasCachedCalculationAsync(FullKey key, string id)
    {
        return (await ReadItemAsync(key, id))["CalculatedContent"]?.Type == JTokenType.Object;
    }
}
