#nullable enable
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The merge write of an annotation: applied to the stored state on every attempt, so a concurrent change of another
// member is kept. The service under test runs over an InterceptedRepositoryProvider where a test interleaves a
// writer; the competing writes and the checks go through the shared service. Every test has its own project.
public class CosmosAnnotationPatchTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string View = Constants.DefaultViewName;
    private const string Key = "exe.northwind.billing.prod";

    private static readonly AuditActor Actor = AnnotationServiceTestExtensions.TestActor;

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task Patch_ChangesItsMembersOnly_AndAnswersTheNewMarker()
    {
        const string project = "pt-members";
        await CreateAsync(project);

        var written = await Annotations.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"Title":"patched","Values":{"plan":"pro","old":null}}"""), Actor, AnnotationWriteCondition.None);

        written.Annotation.Title.Should().Be("patched");
        written.Annotation.Description.Should().Be("kept");
        written.Annotation.Labels.Should().Equal("eu");
        ValuesOf(written.Annotation).Should().Be("""{"limits":{"cpu":2},"plan":"pro"}""");
        var read = await Annotations.GetAnnotationWithMarkerAsync(FullKeyOf(project));
        written.Marker.Should().Be(read.Marker);
        read.Value!.Title.Should().Be("patched");
    }

    [Fact]
    public async Task Patch_AConcurrentChangeOfAnotherMember_IsKept()
    {
        const string project = "pt-race";
        await CreateAsync(project);
        var (service, storage) = CreateService();
        var batches = 0;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, $"{View}.{Key}") && batches++ == 0)
            {
                await Annotations.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"Description":"theirs","Values":{"limits":{"memory":4}}}"""), Actor, AnnotationWriteCondition.None);
            }

            return null;
        };

        var written = await service.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"Title":"ours","Values":{"plan":"pro"}}"""), Actor, AnnotationWriteCondition.None);

        batches.Should().Be(2, "the first batch lost to the concurrent change, the second applied the patch to it");
        var stored = await Annotations.GetAnnotationAsync(FullKeyOf(project));
        stored!.Title.Should().Be("ours");
        stored.Description.Should().Be("theirs");
        ValuesOf(stored).Should().Be("""{"limits":{"cpu":2,"memory":4},"old":1,"plan":"pro"}""");
        written.Annotation.Description.Should().Be("theirs");
    }

    [Fact]
    public async Task Patch_WithIfMatch_WritesOnlyTheVersionItNames()
    {
        const string project = "pt-if-match";
        var created = await CreateAsync(project);
        await Annotations.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"Title":"theirs"}"""), Actor, AnnotationWriteCondition.None);
        var current = await Annotations.GetAnnotationWithMarkerAsync(FullKeyOf(project));

        var failure = await FluentActions.Awaiting(() => Annotations.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"Title":"ours"}"""), Actor, AnnotationWriteCondition.Matching(created.Marker!)))
            .Should().ThrowAsync<PreconditionFailedException>();
        failure.Which.ErrorCode.Should().Be(ErrorCodes.AnnotationPreconditionFailed);

        var written = await Annotations.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"Title":"ours"}"""), Actor, AnnotationWriteCondition.Matching(current.Marker!));
        written.Annotation.Title.Should().Be("ours");
    }

    [Fact]
    public async Task Patch_OfAMissingOrDeletedKey_IsNotFound_AndCreatesNothing()
    {
        const string project = "pt-missing";
        await CreateAsync(project);
        await Annotations.DeleteAnnotationAsync(FullKeyOf(project), Actor);

        var deleted = await FluentActions.Awaiting(() => Annotations.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"Title":"x"}"""), Actor, AnnotationWriteCondition.None))
            .Should().ThrowAsync<NotFoundException>();
        deleted.Which.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);

        var missing = await FluentActions.Awaiting(() => Annotations.PatchAnnotationAsync(FullKeyOf(project, "sbt.never"), Merge("""{"Title":"x"}"""), Actor, AnnotationWriteCondition.None))
            .Should().ThrowAsync<NotFoundException>();
        missing.Which.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);

        (await Annotations.GetAnnotationAsync(FullKeyOf(project))).Should().BeNull();
        (await Annotations.GetAnnotationAsync(FullKeyOf(project, "sbt.never"))).Should().BeNull();
    }

    [Fact]
    public async Task Patch_RecordsOnlyTheChangedMembers()
    {
        const string project = "pt-history";
        await CreateAsync(project);

        await Annotations.PatchAnnotationAsync(FullKeyOf(project), Json("""[{"op":"replace","path":"/Title","value":"patched"},{"op":"add","path":"/Labels/-","value":"us"}]"""), Actor, AnnotationWriteCondition.None);

        var history = await Annotations.GetAnnotationHistoryAsync(FullKeyOf(project));
        var entry = history!.Entries[0];
        entry.Action.Should().Be(AnnotationAuditAction.Updated);
        entry.Fields!.Select(field => field.Field).Should().Equal(nameof(Annotation.Title));
        entry.Labels!.Added.Should().Equal("us");
        entry.Values.Should().BeNull();
    }

    [Fact]
    public async Task Patch_ThatChangesNothing_WritesNothing_AndAnswersTheReadMarker()
    {
        const string project = "pt-noop";
        var created = await CreateAsync(project);

        var written = await Annotations.PatchAnnotationAsync(FullKeyOf(project), Json("""[{"op":"add","path":"/Labels/-","value":"eu"},{"op":"test","path":"/Title","value":"first"}]"""), Actor, AnnotationWriteCondition.None);

        written.Marker.Should().Be(created.Marker);
        (await Annotations.GetAnnotationHistoryAsync(FullKeyOf(project)))!.Entries.Should().HaveCount(1, "only the create");
    }

    [Fact]
    public async Task Patch_OfADisabledAnnotation_ReEnablesIt()
    {
        const string project = "pt-inactive";
        await CreateAsync(project);
        await Annotations.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"IsDisabled":true}"""), Actor, AnnotationWriteCondition.None);

        var written = await Annotations.PatchAnnotationAsync(FullKeyOf(project), Merge("""{"IsDisabled":null}"""), Actor, AnnotationWriteCondition.None);

        written.Annotation.IsDisabled.Should().BeNull();
    }

    [Fact]
    public async Task Patch_AFailedTest_FailsThePatch_AndWritesNothing()
    {
        const string project = "pt-test";
        var created = await CreateAsync(project);

        var failure = await FluentActions.Awaiting(() => Annotations.PatchAnnotationAsync(FullKeyOf(project), Json("""[{"op":"test","path":"/Title","value":"other"},{"op":"replace","path":"/Title","value":"x"}]"""), Actor, AnnotationWriteCondition.None))
            .Should().ThrowAsync<JsonPatchException>();

        failure.Which.Kind.Should().Be(JsonPatchFailureKind.TestFailed);
        (await Annotations.GetAnnotationWithMarkerAsync(FullKeyOf(project))).Marker.Should().Be(created.Marker);
    }

    private static FullKey FullKeyOf(string project, string key = Key)
    {
        return FullKey.Create(AnnotationKey.Parse(key), TestConstants.Organization, project, View);
    }

    private static AnnotationPatch Merge(string json)
    {
        return AnnotationPatch.Merge(JsonNode.Parse(json)!.AsObject());
    }

    private static AnnotationPatch Json(string json)
    {
        return AnnotationPatch.Json(JsonNode.Parse(json)!.AsArray());
    }

    // The values as compact JSON with sorted names.
    private static string ValuesOf(Annotation annotation)
    {
        var values = new JsonObject();

        foreach (var (name, value) in annotation.Values!.OrderBy(pair => pair.Key, System.StringComparer.Ordinal))
        {
            values[name] = JsonSerializer.SerializeToNode(value);
        }

        return values.ToJsonString();
    }

    private async Task<MarkedRead<Annotation>> CreateAsync(string project)
    {
        var values = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>("""{"limits":{"cpu":2},"old":1}""")!;
        var model = AnnotationFactory.CreateDefault(AnnotationKey.Parse(Key), project, View) with
        {
            Title = "first",
            Description = "kept",
            Labels = ["eu"],
            Values = values,
        };

        await Annotations.SetAnnotationAsync(TestConstants.Organization, model, Actor);
        return await Annotations.GetAnnotationWithMarkerAsync(FullKeyOf(project));
    }

    private (CosmosAnnotationService Service, InterceptedRepositoryProvider Storage) CreateService()
    {
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var service = ActivatorUtilities.CreateInstance<CosmosAnnotationService>(Context.Services, storage);
        return (service, storage);
    }
}
