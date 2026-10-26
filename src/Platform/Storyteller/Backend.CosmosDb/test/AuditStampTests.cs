#nullable enable

using System;
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

// The audit stamps of configurations and views; every test works in its own project.
public class AuditStampTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IViewService Views => Context.Services.GetRequiredService<IViewService>();

    private Container Container => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(Organization)
        .Container;

    [Fact]
    public async Task ConfigurationVersions_KeepTheStampOfTheirWrite()
    {
        var project = NewProject();
        var key = await CreateAnnotationAsync(project, AnnotationKey.CreateResponsibility("versions"));

        var first = await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "a": 1 }"""), AuditActor.Machine("machine-1"));
        var second = await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "a": 2 }"""), AuditActor.Account("user-1", "User One"));

        var versions = (await Configs.GetConfigurationVersionsAsync(key)).ToList();
        versions.Select(version => version.Version).Should().Equal(1u, 2u);
        versions[0].Modified.Should().Be(first.Modified, "the archived version keeps the stamp of its write");
        versions[1].Modified.Should().Be(second.Modified);
        (await Configs.ListConfigurationsAsync(Organization, project, View)).Configurations.Single().Modified.Should().Be(second.Modified);
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
    public async Task ViewUpdate_KeepsWhoCreatedTheView()
    {
        var project = NewProject();
        var created = await Views.CreateViewAsync(Organization, project, new ViewCreate { Name = "rollout" }, AuditActor.Account("creator"));

        var updated = await Views.UpdateViewAsync(Organization, project, "rollout", new ViewUpdate { Description = "Kept" }, AuditActor.Account("editor"));

        created.Created!.By.Should().Be(AuditActor.Account("creator"));
        updated.Created.Should().Be(created.Created);
    }

    // A change made through an actor keeps it in its stamp ("Ada via mcp"); a direct change has no Via.
    [Fact]
    public async Task ConfigurationVersions_KeepTheActorThatActedForTheAccount()
    {
        var project = NewProject();
        var key = await CreateAnnotationAsync(project, AnnotationKey.CreateResponsibility("via"));
        var throughMcp = AuditActor.Account("user-1", "Ada") with { Via = "mcp" };

        await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "a": 1 }"""), throughMcp);
        await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "a": 2 }"""), AuditActor.Account("user-1", "Ada"));

        var versions = (await Configs.GetConfigurationVersionsAsync(key)).ToList();
        versions[0].Modified.By.Should().Be(throughMcp);
        versions[1].Modified.By.Should().Be(AuditActor.Account("user-1", "Ada"));
        versions[1].Modified.By.Via.Should().BeNull();
    }

    [Fact]
    public async Task View_KeepsTheActorThatActedForItsCreator()
    {
        var project = NewProject();
        var throughMcp = AuditActor.Account("creator", "Ada") with { Via = "mcp" };

        await Views.CreateViewAsync(Organization, project, new ViewCreate { Name = "rollout" }, throughMcp);

        var view = (await Views.GetViewsAsync(Organization, project, discover: false)).Single(candidate => candidate.Name == "rollout");
        view.Created!.By.Should().Be(throughMcp);
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
