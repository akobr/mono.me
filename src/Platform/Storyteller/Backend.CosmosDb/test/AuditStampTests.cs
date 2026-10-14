#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities.Annotations;
using _42.Platform.Storyteller.Entities.Configurations;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The audit stamps of configurations and views, also for items stored before the stamps; every test works in its own project.
public class AuditStampTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private static readonly DateTimeOffset LegacyTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IViewService Views => Context.Services.GetRequiredService<IViewService>();

    private Container Container => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(Organization)
        .Container;

    [Fact]
    public async Task LegacyConfiguration_IsReadWithItsAuthorAndConvertedOnTheNextWrite()
    {
        var project = NewProject();
        var annotationKey = AnnotationKey.CreateResponsibility("legacy");
        var key = await CreateAnnotationAsync(project, annotationKey);
        var partitionKey = key.GetPartitionKey();
        var current = Create.Configuration(annotationKey, JObject.Parse("""{ "a": 2 }"""), viewName: View, projectName: project) with
        {
            Version = 2,
            Modified = null,
            Author = "account: user-legacy",
        };
        var history = new ConfigurationHistoryEntity
        {
            PartitionKey = partitionKey,
            Id = $"{View}.cfv.{annotationKey}.1",
            AnnotationKey = annotationKey,
            Name = annotationKey.Name,
            ProjectName = project,
            ViewName = View,
            Version = 1,
            Content = JObject.Parse("""{ "a": 1 }"""),
            Author = "application: machine-legacy",
            CreationTime = LegacyTime,
        };
        await Container.UpsertItemAsync(current, new PartitionKey(partitionKey));
        await Container.UpsertItemAsync(history, new PartitionKey(partitionKey));

        var versions = (await Configs.GetConfigurationVersionsAsync(key)).ToList();
        var summary = (await Configs.ListConfigurationsAsync(Organization, project, View)).Configurations.Single();

        versions.Select(version => version.Version).Should().Equal(1u, 2u);
        versions[0].Modified.Should().Be(AuditStamp.Of(AuditActor.Machine("machine-legacy"), LegacyTime));
        versions[1].Modified.By.Should().Be(AuditActor.Account("user-legacy"));
        versions[1].Modified.At.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), "a legacy item without a time has its last write");
        summary.Modified.Should().Be(versions[1].Modified);

        var written = await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "a": 3 }"""), AuditActor.Account("user-new", "New User"));

        written.Modified.By.Should().Be(AuditActor.Account("user-new", "New User"));
        var stored = await ReadRawAsync($"{View}.cnf.{annotationKey}", partitionKey);
        stored.ContainsKey("Author").Should().BeFalse("the legacy author is not written again");
        stored["Modified"]!["By"]!["Id"]!.Value<string>().Should().Be("user-new");
        var archived = await ReadRawAsync($"{View}.cfv.{annotationKey}.2", partitionKey);
        archived.ContainsKey("Author").Should().BeFalse();
        archived["Modified"]!["By"]!["Id"]!.Value<string>().Should().Be("user-legacy");
        archived["Modified"]!["By"]!["Kind"]!.Value<string>().Should().Be("Account");
    }

    [Fact]
    public async Task ConfigurationVersionTime_DoesNotMoveWithTheCalculationCache()
    {
        var project = NewProject();
        var annotationKey = AnnotationKey.CreateResponsibility("cached");
        var key = await CreateAnnotationAsync(project, annotationKey);
        var written = await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "a": 1 }"""), AuditActor.Account("author-1"));
        var before = await ReadRawAsync($"{View}.cnf.{annotationKey}", key.GetPartitionKey());

        // _ts has a resolution of one second
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var calculated = await Configs.GetRawConfigurationAsync(key);
        var after = await ReadRawAsync($"{View}.cnf.{annotationKey}", key.GetPartitionKey());

        after["_ts"]!.Value<long>().Should().BeGreaterThan(before["_ts"]!.Value<long>(), "the calculation rewrote the item");
        calculated!.Modified.Should().Be(written.Modified);
        (await Configs.GetConfigurationVersionsAsync(key)).Single().Modified.Should().Be(written.Modified);
        (await Configs.ListConfigurationsAsync(Organization, project, View)).Configurations.Single().Modified.Should().Be(written.Modified);
    }

    [Fact]
    public async Task UnwrittenConfiguration_IsTheCalculationBySystem()
    {
        var project = NewProject();
        var key = await CreateAnnotationAsync(project, AnnotationKey.CreateResponsibility("unwritten"));

        var hierarchy = await Configs.GetConfigurationHierarchyViewAsync(key);

        hierarchy!.Version.Should().Be(0);
        hierarchy.Modified.By.Should().Be(AuditActor.System);
    }

    [Fact]
    public async Task LegacyView_IsReadWithItsAuthorAndConvertedOnUpdate()
    {
        var project = NewProject();
        var partitionKey = ViewEntity.GetPartitionKey(project);
        await Container.UpsertItemAsync(
            new ViewEntity { PartitionKey = partitionKey, Name = "legacy", CreatedAt = LegacyTime, CreatedBy = "system" },
            new PartitionKey(partitionKey));

        var listed = (await Views.GetViewsAsync(Organization, project, discover: false)).Single(view => view.Name == "legacy");
        var updated = await Views.UpdateViewAsync(Organization, project, "legacy", new ViewUpdate { Description = "Kept" }, AuditActor.Account("user-1"));

        listed.Created.Should().Be(AuditStamp.Of(AuditActor.System, LegacyTime));
        updated.Created.Should().Be(listed.Created, "an update does not change who created the view");
        var stored = await ReadRawAsync($"{ViewEntity.IdPrefix}legacy", partitionKey);
        stored.ContainsKey("CreatedBy").Should().BeFalse();
        stored.ContainsKey("CreatedAt").Should().BeFalse();
        stored["Created"]!["By"]!["Kind"]!.Value<string>().Should().Be("System");
    }

    [Fact]
    public async Task OrganizationContainer_KeepsTheAuditStampsOutOfTheIndex()
    {
        var properties = (await Container.ReadContainerAsync()).Resource;

        properties.IndexingPolicy.ExcludedPaths.Select(path => path.Path).Should().Contain(ContainerFactory.AuditStampPaths);
    }

    private static string NewProject() => $"stm-{Guid.NewGuid():N}"[..16];

    private async Task<FullKey> CreateAnnotationAsync(string project, AnnotationKey annotationKey)
    {
        await Annotations.CreateAnnotationAsync(Organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = annotationKey.Name,
            ProjectName = project,
            ViewName = View,
        });

        return FullKey.Create(annotationKey, Organization, project, View);
    }

    private async Task<JObject> ReadRawAsync(string id, string partitionKey)
    {
        return (await Container.ReadItemAsync<JObject>(id, new PartitionKey(partitionKey))).Resource;
    }
}
