#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Conditional annotation writes (If-Match, If-None-Match) against interleaved writers: the service under test runs over
// an InterceptedRepositoryProvider, the competing writes and the checks go through the shared service. Every test has
// its own project.
public class CosmosAnnotationConditionalWriteTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string View = Constants.DefaultViewName;
    private const string Key = "sbt.northwind";

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task IfMatch_OfTheCurrentVersion_Writes_AndAnswersTheNewMarker()
    {
        const string project = "cw-match";
        var read = await CreateAsync(project);

        var written = await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "ours" }, Actor, AnnotationWriteCondition.Matching(read.Marker!));

        written.Annotation.Title.Should().Be("ours");
        written.Marker.Should().NotBeNullOrEmpty().And.NotBe(read.Marker);
        written.Marker.Should().Be(await ItemETagAsync(project));
    }

    [Fact]
    public async Task IfMatch_OfAnEarlierVersion_FailsThePrecondition_AndWritesNothing()
    {
        const string project = "cw-stale";
        var read = await CreateAsync(project);
        await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "theirs" }, Actor);

        var failure = await FluentActions.Awaiting(() => Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "ours" }, Actor, AnnotationWriteCondition.Matching(read.Marker!)))
            .Should().ThrowAsync<PreconditionFailedException>();

        failure.Which.ErrorCode.Should().Be(ErrorCodes.AnnotationPreconditionFailed);
        (await Annotations.GetAnnotationAsync(FullKeyOf(project)))!.Title.Should().Be("theirs");
    }

    [Fact]
    public async Task IfMatch_ACompetingChangeBeforeTheBatch_FailsThePrecondition_AndIsKept()
    {
        const string project = "cw-race";
        var read = await CreateAsync(project);
        var (service, storage) = CreateService();
        var competed = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (!competed && batch.Contains(BatchOperationKind.Replace, AnnotationId))
            {
                competed = true;
                await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "theirs" }, Actor);
            }

            return null;
        };

        var failure = await FluentActions.Awaiting(() => service.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "ours" }, Actor, AnnotationWriteCondition.Matching(read.Marker!)))
            .Should().ThrowAsync<PreconditionFailedException>();

        failure.Which.ErrorCode.Should().Be(ErrorCodes.AnnotationPreconditionFailed);
        competed.Should().BeTrue();
        (await Annotations.GetAnnotationAsync(FullKeyOf(project)))!.Title.Should().Be("theirs");
    }

    // A configuration write appends to the audit item, which shares the batch: the batch fails, and the retry, where
    // the annotation is still the version of the condition, writes.
    [Fact]
    public async Task IfMatch_AnAuditOnlyChangeBeforeTheBatch_StillWrites()
    {
        const string project = "cw-audit";
        var read = await CreateAsync(project);
        var (service, storage) = CreateService();
        var batches = 0;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, AnnotationId) && batches++ == 0)
            {
                await TouchAsync(project, $"{View}.adt.{Key}");
            }

            return null;
        };

        var written = await service.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "ours" }, Actor, AnnotationWriteCondition.Matching(read.Marker!));

        batches.Should().Be(2, "the first batch lost to the audit change, the retry wrote");
        written.Annotation.Title.Should().Be("ours");
        written.Marker.Should().Be(await ItemETagAsync(project));
    }

    [Fact]
    public async Task IfMatch_ADeleteBetweenReadAndWrite_FailsThePrecondition_AndReCreatesNothing()
    {
        const string project = "cw-delete";
        var read = await CreateAsync(project);
        var (service, storage) = CreateService();
        var deleted = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (!deleted && batch.Contains(BatchOperationKind.Replace, AnnotationId))
            {
                deleted = true;
                await Annotations.DeleteAnnotationAsync(FullKeyOf(project), Actor);
            }

            return null;
        };

        await FluentActions.Awaiting(() => service.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "ours" }, Actor, AnnotationWriteCondition.Matching(read.Marker!)))
            .Should().ThrowAsync<PreconditionFailedException>();

        (await Annotations.GetAnnotationAsync(FullKeyOf(project))).Should().BeNull();
        (await Annotations.GetDeletedAnnotationAsync(FullKeyOf(project))).Should().NotBeNull();
    }

    [Fact]
    public async Task IfMatch_OfADeletedOrMissingKey_FailsThePrecondition_AndCreatesNothing()
    {
        const string project = "cw-match-missing";
        var read = await CreateAsync(project);
        await Annotations.DeleteAnnotationAsync(FullKeyOf(project), Actor);

        await FluentActions.Awaiting(() => Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project), Actor, AnnotationWriteCondition.Matching(read.Marker!)))
            .Should().ThrowAsync<PreconditionFailedException>();
        await FluentActions.Awaiting(() => Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project, "sbt.never"), Actor, new AnnotationWriteCondition { IfMatch = AnnotationVersionMatch.Anything }))
            .Should().ThrowAsync<PreconditionFailedException>();

        (await Annotations.GetAnnotationAsync(FullKeyOf(project))).Should().BeNull();
        (await Annotations.GetAnnotationAsync(FullKeyOf(project, "sbt.never"))).Should().BeNull();
    }

    [Fact]
    public async Task NoOp_OfTheCurrentVersion_AnswersTheReadMarker_AndOfAnEarlierOne_FailsThePrecondition()
    {
        const string project = "cw-noop";
        var first = await CreateAsync(project);
        await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "second" }, Actor);
        var second = await Annotations.GetAnnotationWithMarkerAsync(FullKeyOf(project));

        var same = await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "second" }, Actor, AnnotationWriteCondition.Matching(second.Marker!));

        same.Marker.Should().Be(second.Marker);
        await FluentActions.Awaiting(() => Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "second" }, Actor, AnnotationWriteCondition.Matching(first.Marker!)))
            .Should().ThrowAsync<PreconditionFailedException>();
    }

    [Fact]
    public async Task IfNoneMatchAny_CreatesAMissingKey_AndReCreatesADeletedOne_ButNotAnExistingOne()
    {
        const string project = "cw-create-only";

        var created = await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "new" }, Actor, AnnotationWriteCondition.CreateOnly);

        created.Annotation.Title.Should().Be("new");
        created.Marker.Should().Be(await ItemETagAsync(project));

        var failure = await FluentActions.Awaiting(() => Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "again" }, Actor, AnnotationWriteCondition.CreateOnly))
            .Should().ThrowAsync<PreconditionFailedException>();
        failure.Which.Message.Should().Contain("already exists");

        await Annotations.DeleteAnnotationAsync(FullKeyOf(project), Actor);
        var recreated = await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "recreated" }, Actor, AnnotationWriteCondition.CreateOnly);

        recreated.Annotation.Title.Should().Be("recreated");
        recreated.Marker.Should().NotBe(created.Marker).And.Be(await ItemETagAsync(project));
    }

    [Fact]
    public async Task IfNoneMatchAny_AgainstAConcurrentCreate_FailsThePrecondition_AndKeepsTheOtherCreate()
    {
        const string project = "cw-create-race";
        var (service, storage) = CreateService();
        var competed = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (!competed && batch.Operations.Any(operation => operation.Kind == BatchOperationKind.Create))
            {
                competed = true;
                await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "theirs" }, Actor);
            }

            return null;
        };

        await FluentActions.Awaiting(() => service.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "ours" }, Actor, AnnotationWriteCondition.CreateOnly))
            .Should().ThrowAsync<PreconditionFailedException>();

        competed.Should().BeTrue();
        (await Annotations.GetAnnotationAsync(FullKeyOf(project)))!.Title.Should().Be("theirs");
    }

    // Without a condition, a create that loses to a concurrent create becomes an update on the retry.
    [Fact]
    public async Task Unconditional_ACreateLosingToAConcurrentCreate_UpdatesIt()
    {
        const string project = "cw-create-update";
        var (service, storage) = CreateService();
        var competed = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (!competed && batch.Operations.Any(operation => operation.Kind == BatchOperationKind.Create))
            {
                competed = true;
                await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "theirs" }, Actor);
            }

            return null;
        };

        var written = await service.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "ours" }, Actor);

        competed.Should().BeTrue();
        written.Title.Should().Be("ours");
        (await Annotations.GetAnnotationAsync(FullKeyOf(project)))!.Title.Should().Be("ours");
    }

    // The marker is the version a later condition names: a child changes it (its relation arrays), so does a re-create.
    [Fact]
    public async Task Marker_IsTheETagOfTheItem_AndAChildChangesIt()
    {
        const string project = "cw-marker";
        var created = await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project, "rst.billing"), Actor, AnnotationWriteCondition.None);
        created.Marker.Should().Be(await ItemETagAsync(project, "rst.billing"));

        await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project, "unt.billing.invoice"), Actor);

        var read = await Annotations.GetAnnotationWithMarkerAsync(FullKeyOf(project, "rst.billing"));
        read.Marker.Should().NotBe(created.Marker).And.Be(await ItemETagAsync(project, "rst.billing"));
    }

    private static AuditActor Actor => AnnotationServiceTestExtensions.TestActor;

    private static string AnnotationId => $"{View}.{Key}";

    private static FullKey FullKeyOf(string project, string key = Key)
    {
        return FullKey.Create(AnnotationKey.Parse(key), TestConstants.Organization, project, View);
    }

    private static Annotation Model(string project, string key = Key)
    {
        return AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), project, View);
    }

    private async Task<Configuring.MarkedRead<Annotation>> CreateAsync(string project)
    {
        await Annotations.SetAnnotationAsync(TestConstants.Organization, Model(project) with { Title = "first" }, Actor);
        var read = await Annotations.GetAnnotationWithMarkerAsync(FullKeyOf(project));
        read.Value.Should().NotBeNull();
        read.Marker.Should().NotBeNullOrEmpty();
        return read;
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

    private async Task<string> ItemETagAsync(string project, string key = Key)
    {
        var fullKey = FullKeyOf(project, key);
        using var response = await GetContainer().ReadItemStreamAsync($"{View}.{key}", new PartitionKey(fullKey.GetPartitionKey()));
        response.IsSuccessStatusCode.Should().BeTrue();
        return response.Headers.ETag;
    }

    // A change of an item that leaves its content as it is.
    private async Task TouchAsync(string project, string id)
    {
        using var response = await GetContainer().PatchItemStreamAsync(
            id,
            new PartitionKey(FullKeyOf(project).GetPartitionKey()),
            [PatchOperation.Increment("/TouchedByTest", 1)]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
