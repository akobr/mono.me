#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The reads of the active parents outside of a deleted or restored tree, whose relation arrays the operation changes:
// all of them in one read of many items, and again in every attempt. The service under test runs over an
// InterceptedRepositoryProvider, the setup, the competing writes and the checks go through the shared service. Every
// test has its own project.
public class CosmosAnnotationParentReadTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string View = Constants.DefaultViewName;

    private static AuditActor Actor => AnnotationServiceTestExtensions.TestActor;

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    // A context's outside parents: its subject, and the usages of its executions (deleted with it).
    [Fact]
    public async Task Delete_ReadsItsOutsideParentsInOneReadOfManyItems()
    {
        const string project = "pr-delete";
        await CreateAsync(project, "exe.shop.billing.blue");
        await CreateAsync(project, "exe.shop.shipping.blue");
        (await GetAsync<Subject>(project, "sbt.shop")).ContextNames.Should().Contain("blue");
        (await GetAsync<Usage>(project, "usg.shop.billing")).ContextNames.Should().Contain("blue");
        (await GetAsync<Usage>(project, "usg.shop.shipping")).ContextNames.Should().Contain("blue");

        string[] parentKeys = ["sbt.shop", "usg.shop.billing", "usg.shop.shipping"];
        var parents = parentKeys.Select(Id).ToList();
        var etags = new Dictionary<string, string>();

        foreach (var parent in parentKeys)
        {
            etags[Id(parent)] = await ItemETagAsync(project, parent);
        }

        var (service, storage) = CreateService();

        var result = await service.DeleteAnnotationAsync(FullKeyOf(project, "cnt.shop.blue"), Actor);

        result.DeletedKeys.Should().Equal("exe.shop.billing.blue", "exe.shop.shipping.blue", "cnt.shop.blue");
        ReadsOfParents(storage, parents).Should().ContainSingle().Which.Should().BeEquivalentTo(parents);
        storage.Interceptor.PointReads.Should().NotIntersectWith(parents);

        // each parent is written with the ETag of the read
        var replaces = storage.Interceptor.Batches.SelectMany(batch => batch.Operations).Where(operation => operation.Kind == BatchOperationKind.Replace).ToList();

        foreach (var parent in parents)
        {
            replaces.Should().ContainSingle(operation => operation.Id == parent).Which.IfMatchEtag.Should().Be(etags[parent]);
        }

        (await GetAsync<Subject>(project, "sbt.shop")).ContextNames.Should().NotContain("blue");
        (await GetAsync<Usage>(project, "usg.shop.billing")).ContextNames.Should().NotContain("blue");
        (await GetAsync<Usage>(project, "usg.shop.shipping")).ContextNames.Should().NotContain("blue");
    }

    // A usage's restored executions have their contexts outside of the restored tree.
    [Fact]
    public async Task Restore_ReadsItsOutsideParentsInOneReadOfManyItems()
    {
        const string project = "pr-restore";
        await CreateAsync(project, "exe.shop.billing.blue");
        await CreateAsync(project, "exe.shop.billing.green");
        await Annotations.DeleteAnnotationAsync(FullKeyOf(project, "usg.shop.billing"), Actor);
        (await GetAsync<Context>(project, "cnt.shop.blue")).ResponsibilityNames.Should().NotContain("billing");
        (await GetAsync<Context>(project, "cnt.shop.green")).ResponsibilityNames.Should().NotContain("billing");

        string[] parents = [Id("cnt.shop.blue"), Id("cnt.shop.green")];
        var (service, storage) = CreateService();

        var result = await service.RestoreAnnotationsAsync(TestConstants.Organization, project, View, [AnnotationKey.Parse("usg.shop.billing")], Actor);

        result.Failed.Should().BeEmpty();
        result.Annotations.Select(annotation => annotation.AnnotationKey).Should().Equal("usg.shop.billing", "exe.shop.billing.blue", "exe.shop.billing.green");
        ReadsOfParents(storage, parents).Should().ContainSingle().Which.Should().BeEquivalentTo(parents);
        storage.Interceptor.PointReads.Should().NotIntersectWith(parents);
        (await GetAsync<Context>(project, "cnt.shop.blue")).ResponsibilityNames.Should().Contain("billing");
        (await GetAsync<Context>(project, "cnt.shop.green")).ResponsibilityNames.Should().Contain("billing");
    }

    // The first batch loses to a change of its item; the retry reads the parents again, under fresh ETags.
    [Fact]
    public async Task Delete_RetriedAfterAConcurrentChange_ReadsItsOutsideParentsAgain()
    {
        const string project = "pr-delete-retry";
        await CreateAsync(project, "exe.shop.billing.blue");
        var (service, storage) = CreateService();
        var root = Id("exe.shop.billing.blue");
        var touched = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (!touched && batch.Contains(BatchOperationKind.Replace, root))
            {
                touched = true;
                await TouchAsync(project, "exe.shop.billing.blue");
            }

            return null;
        };

        var result = await service.DeleteAnnotationAsync(FullKeyOf(project, "exe.shop.billing.blue"), Actor);

        result.DeletedKeys.Should().Equal("exe.shop.billing.blue");
        storage.Interceptor.Batches.Where(batch => batch.Contains(BatchOperationKind.Replace, root)).Select(batch => batch.StatusCode)
            .Should().HaveCount(2).And.StartWith(HttpStatusCode.PreconditionFailed);
        string[] parents = [Id("usg.shop.billing"), Id("cnt.shop.blue")];
        ReadsOfParents(storage, parents).Should().HaveCount(2, "every attempt reads the parents")
            .And.AllSatisfy(ids => ids.Should().BeEquivalentTo(parents));
        (await GetAsync<Usage>(project, "usg.shop.billing")).ContextNames.Should().NotContain("blue");
        (await GetAsync<Context>(project, "cnt.shop.blue")).ResponsibilityNames.Should().NotContain("billing");
    }

    [Fact]
    public async Task Restore_RetriedAfterAConcurrentChange_ReadsItsOutsideParentsAgain()
    {
        const string project = "pr-restore-retry";
        await CreateAsync(project, "exe.shop.billing.blue");
        await Annotations.DeleteAnnotationAsync(FullKeyOf(project, "usg.shop.billing"), Actor);
        var (service, storage) = CreateService();
        var usage = Id("usg.shop.billing");
        var touched = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (!touched && batch.Contains(BatchOperationKind.Replace, usage))
            {
                touched = true;
                await TouchAsync(project, "usg.shop.billing");
            }

            return null;
        };

        var result = await service.RestoreAnnotationsAsync(TestConstants.Organization, project, View, [AnnotationKey.Parse("usg.shop.billing")], Actor);

        result.Failed.Should().BeEmpty();
        result.Annotations.Select(annotation => annotation.AnnotationKey).Should().Equal("usg.shop.billing", "exe.shop.billing.blue");
        storage.Interceptor.Batches.Where(batch => batch.Contains(BatchOperationKind.Replace, usage)).Select(batch => batch.StatusCode)
            .Should().HaveCount(2).And.StartWith(HttpStatusCode.PreconditionFailed);
        string[] parents = [Id("cnt.shop.blue")];
        ReadsOfParents(storage, parents).Should().HaveCount(2, "every attempt reads the parents")
            .And.AllSatisfy(ids => ids.Should().BeEquivalentTo(parents));
        (await GetAsync<Context>(project, "cnt.shop.blue")).ResponsibilityNames.Should().Contain("billing");
    }

    private static string Id(string key)
    {
        return $"{View}.{key}";
    }

    // the reads of many items that ask for any of the parents; other reads of many items of the operation don't count
    private static List<IReadOnlyList<string>> ReadsOfParents(InterceptedRepositoryProvider storage, IReadOnlyCollection<string> parents)
    {
        return storage.Interceptor.ReadsOfMany.Where(ids => ids.Intersect(parents).Any()).ToList();
    }

    private static FullKey FullKeyOf(string project, string key)
    {
        return FullKey.Create(AnnotationKey.Parse(key), TestConstants.Organization, project, View);
    }

    private Task CreateAsync(string project, string key)
    {
        return Annotations.CreateAnnotationAsync(TestConstants.Organization, AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), project, View));
    }

    private async Task<TAnnotation> GetAsync<TAnnotation>(string project, string key)
        where TAnnotation : Annotation
    {
        var annotation = await Annotations.GetAnnotationAsync(FullKeyOf(project, key));
        annotation.Should().NotBeNull($"'{key}' should be active");
        return (TAnnotation)annotation!;
    }

    private (CosmosAnnotationService Service, InterceptedRepositoryProvider Storage) CreateService()
    {
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var service = ActivatorUtilities.CreateInstance<CosmosAnnotationService>(Context.Services, storage);
        return (service, storage);
    }

    private Container GetContainer()
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(TestConstants.Organization)
            .Container;
    }

    private async Task<string> ItemETagAsync(string project, string key)
    {
        using var response = await GetContainer().ReadItemStreamAsync(Id(key), FullKeyOf(project, key).GetCosmosPartitionKey());
        response.IsSuccessStatusCode.Should().BeTrue();
        return response.Headers.ETag;
    }

    // A change of the annotation item that leaves its content as it is.
    private async Task TouchAsync(string project, string key)
    {
        using var response = await GetContainer().PatchItemStreamAsync(
            Id(key),
            FullKeyOf(project, key).GetCosmosPartitionKey(),
            [PatchOperation.Increment("/TouchedByTest", 1)]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
