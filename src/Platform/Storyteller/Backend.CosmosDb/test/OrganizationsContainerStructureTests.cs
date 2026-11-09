#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Logic;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The layout of the database: core and organizations whatever the number of organizations, the hierarchical key of the
// shared container, and DbCreator's refusal of a database with containers of the layout before it (org.{organization}).
// The expected names and paths are written out, so a changed constant fails here instead of moving the layout.
public class OrganizationsContainerStructureTests(Startup startup)
    : BaseTestsClass(startup)
{
    // an owner without an account: creating an access point needs only its id
    private const string OwnerId = "structure-tests-owner";

    private IContainerFactory Factory => Context.Services.GetRequiredService<IContainerFactory>();

    private CoreDbStructureBuilder Builder => Context.Services.GetRequiredService<CoreDbStructureBuilder>();

    private IAccessService Access => Context.Services.GetRequiredService<IAccessService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private Database PlatformDatabase => Context.Services.GetRequiredService<ICosmosClientProvider>().Client.GetDatabase(CosmosConstants.DatabaseName);

    [Fact]
    public async Task Setup_CreatesExactlyCoreAndOrganizations()
    {
        await ShouldHaveContainersAsync("core", "organizations");
    }

    [Fact]
    public async Task Organizations_IsPartitionedByOrganizationThenPartitionKey()
    {
        var organizations = (await PlatformDatabase.GetContainer("organizations").ReadContainerAsync()).Resource;
        var core = (await PlatformDatabase.GetContainer("core").ReadContainerAsync()).Resource;

        organizations.PartitionKeyPaths.Should().Equal("/Organization", "/PartitionKey");
        core.PartitionKeyPath.Should().Be("/PartitionKey", "core keeps its single-level key");
    }

    [Fact]
    public async Task CreateAccessPoint_NewOrganizations_CreatesNoContainer()
    {
        var organizations = Enumerable.Range(0, 3).Select(_ => NewOrganizationName()).ToArray();

        foreach (var organization in organizations)
        {
            await Access.CreateAccessPointAsync(new AccessPointCreate
            {
                Organization = organization,
                Project = Constants.DefaultProjectName,
                OwnerId = OwnerId,
            });
        }

        // a new organization is usable at once: its data goes to the shared container, nothing waits for a container
        await Annotations.CreateAnnotationsFromStringAsync(organizations[0], ["payments"]);
        var subjectKey = FullKey.Create(AnnotationKey.CreateSubject("payments"), organizations[0], Constants.DefaultProjectName, Constants.DefaultViewName);

        await ShouldHaveContainersAsync("core", "organizations");
        (await Annotations.GetAnnotationAsync(subjectKey)).Should().NotBeNull();

        foreach (var organization in organizations)
        {
            (await Access.GetAccessPointAsync(organization)).Should().NotBeNull("an organization is its access point in core");
        }
    }

    [Fact]
    public async Task BuildAsync_Again_KeepsTheLayoutAndTheSeed()
    {
        // the fixture ran it once already
        var build = () => Builder.BuildAsync();

        await build.Should().NotThrowAsync();

        await ShouldHaveContainersAsync("core", "organizations");
        (await PlatformDatabase.GetContainer("organizations").ReadContainerAsync()).Resource
            .PartitionKeyPaths.Should().Equal("/Organization", "/PartitionKey");
        (await Factory.FindLayoutProblemsAsync()).Should().BeEmpty();

        var seeded = FullKey.Create(AnnotationKey.CreateResponsibility("storyteller"), TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        (await Annotations.GetAnnotationAsync(seeded)).Should().NotBeNull();
    }

    [Fact]
    public async Task LegacyOrganizationContainer_IsReportedAndRefusedByDbCreator()
    {
        (await Factory.FindLayoutProblemsAsync()).Should().BeEmpty("the fixture's database has the shared layout");

        var legacyName = $"org.legacy-{Guid.NewGuid():N}";
        var legacy = (await PlatformDatabase.CreateContainerAsync(new ContainerProperties(legacyName, "/PartitionKey"))).Container;

        // removed whatever happens, so the tests after this one see the healthy database
        try
        {
            var problems = await Factory.FindLayoutProblemsAsync();

            problems.Should().ContainSingle().Which.Should().Contain($"'{legacyName}'");

            var build = () => Builder.BuildAsync();
            var refusal = (await build.Should().ThrowAsync<DatabaseLayoutException>()).Which;

            refusal.Problems.Should().ContainSingle().Which.Should().Contain(legacyName);
            refusal.Message.Should().Contain(legacyName);

            // refused before anything is written: the database holds what it held
            await ShouldHaveContainersAsync("core", "organizations", legacyName);
        }
        finally
        {
            await legacy.DeleteContainerAsync();
        }

        (await Factory.FindLayoutProblemsAsync()).Should().BeEmpty();
    }

    // valid by the name rules: 2 to 63 lower-case letters, digits or hyphens
    private static string NewOrganizationName() => $"s-{Guid.NewGuid():N}"[..14];

    private async Task ShouldHaveContainersAsync(params string[] names)
    {
        (await Factory.GetContainerNamesAsync()).Should().BeEquivalentTo(names);
    }
}
