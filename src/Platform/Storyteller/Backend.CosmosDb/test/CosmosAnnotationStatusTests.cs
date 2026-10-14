using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The list reads return only active annotations unless they are asked for the disabled, scheduled, expired or
// deleted ones.
public class CosmosAnnotationStatusTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "status";

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private Container Container => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(TestConstants.Organization)
        .Container;

    [Fact]
    public async Task List_ByDefault_ReturnsOnlyActiveAnnotations()
    {
        var now = DateTimeOffset.UtcNow;
        var prague = TimeSpan.FromHours(2);
        await CreateAsync("sbt.ls-plain");
        await CreateAsync("sbt.ls-enabled", a => a with { IsDisabled = false });
        await CreateAsync("sbt.ls-disabled", a => a with { IsDisabled = true });
        await CreateAsync("sbt.ls-scheduled", a => a with { ValidFrom = now.AddDays(1) });
        await CreateAsync("sbt.ls-expired", a => a with { ExpiresAt = now.AddDays(-1) });
        await CreateAsync("sbt.ls-window", a => a with { ValidFrom = now.AddHours(-1).ToOffset(prague), ExpiresAt = now.AddHours(1).ToOffset(prague) });

        var active = await ListAsync("sbt.ls-");
        var all = await ListAsync("sbt.ls-", includeInactive: true);

        active.Should().BeEquivalentTo("sbt.ls-plain", "sbt.ls-enabled", "sbt.ls-window");
        all.Should().BeEquivalentTo("sbt.ls-plain", "sbt.ls-enabled", "sbt.ls-disabled", "sbt.ls-scheduled", "sbt.ls-expired", "sbt.ls-window");
    }

    [Fact]
    public async Task List_AnnotationWithNullIsDisabled_IsActive()
    {
        await CreateAsync("sbt.ln-null");
        await Container.PatchItemAsync<object>(
            $"{Constants.DefaultViewName}.sbt.ln-null",
            PartitionKeys.GetCosmosSubject(Project, "ln-null"),
            [PatchOperation.Set<object>("/IsDisabled", null)]);

        var active = await ListAsync("sbt.ln-");

        active.Should().Equal("sbt.ln-null");
    }

    [Fact]
    public async Task List_WithDeleted_AddsDeletedAnnotationsThatAreActive()
    {
        await CreateAsync("sbt.ld-live");
        await CreateAsync("sbt.ld-gone");
        await CreateAsync("sbt.ld-gone-disabled", a => a with { IsDisabled = true });
        await Annotations.DeleteAnnotationAsync(Key("sbt.ld-gone"));
        await Annotations.DeleteAnnotationAsync(Key("sbt.ld-gone-disabled"));

        var active = await ListAsync("sbt.ld-");
        var withDeleted = await ListAsync("sbt.ld-", deleted: AnnotationDeletedFilter.Include);
        var everything = await ListAsync("sbt.ld-", includeInactive: true, deleted: AnnotationDeletedFilter.Include);

        active.Should().Equal("sbt.ld-live");
        withDeleted.Should().BeEquivalentTo("sbt.ld-live", "sbt.ld-gone");
        everything.Should().BeEquivalentTo("sbt.ld-live", "sbt.ld-gone", "sbt.ld-gone-disabled");
    }

    [Fact]
    public async Task List_WithDeleted_ReturnsTheDeletion()
    {
        await CreateAsync("sbt.lw-gone");
        await Annotations.DeleteAnnotationAsync(Key("sbt.lw-gone"));

        var response = await Annotations.GetAnnotationsAsync(Request(deleted: AnnotationDeletedFilter.Include));

        var deleted = response.Annotations.Single(a => a.AnnotationKey == "sbt.lw-gone");
        deleted.Deletion.Should().NotBeNull();
        deleted.Deletion!.PurgeAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Descendants_ByDefault_ReturnOnlyActiveAnnotations()
    {
        await CreateAsync("sbt.dd-s");
        await CreateAsync("cnt.dd-s.dd-live");
        await CreateAsync("cnt.dd-s.dd-off", a => a with { IsDisabled = true });
        await CreateAsync("usg.dd-s.dd-r", a => a with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        var active = await DescendantsAsync("sbt.dd-s");
        var all = await DescendantsAsync("sbt.dd-s", includeInactive: true);

        active.Should().Equal("cnt.dd-s.dd-live");
        all.Should().BeEquivalentTo("cnt.dd-s.dd-live", "cnt.dd-s.dd-off", "usg.dd-s.dd-r");
    }

    [Fact]
    public async Task Get_InactiveAnnotation_IsStillReadByKey()
    {
        await CreateAsync("sbt.gk-off", a => a with { IsDisabled = true });

        var annotation = await Annotations.GetAnnotationAsync(Key("sbt.gk-off"));

        annotation.Should().NotBeNull("the single read leaves the status to its caller");
        annotation!.IsDisabled.Should().BeTrue();
    }

    private FullKey Key(string key)
    {
        return FullKey.Create(AnnotationKey.Parse(key), TestConstants.Organization, Project, Constants.DefaultViewName);
    }

    private Task CreateAsync(string key, Func<Annotation, Annotation> change = null)
    {
        var model = AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), Project, Constants.DefaultViewName);
        return Annotations.CreateAnnotationAsync(TestConstants.Organization, change?.Invoke(model) ?? model);
    }

    private static AnnotationsRequest Request(bool includeInactive = false, AnnotationDeletedFilter deleted = AnnotationDeletedFilter.Exclude)
    {
        return new AnnotationsRequest
        {
            Organization = TestConstants.Organization,
            Project = Project,
            View = Constants.DefaultViewName,
            Types = Enum.GetValues<AnnotationType>(),
            IncludeInactive = includeInactive,
            Deleted = deleted,
        };
    }

    // The keys of every page with the prefix: the tests of this class share the project.
    private async Task<List<string>> ListAsync(string prefix, bool includeInactive = false, AnnotationDeletedFilter deleted = AnnotationDeletedFilter.Exclude)
    {
        var keys = new List<string>();
        var request = Request(includeInactive, deleted);

        do
        {
            var page = await Annotations.GetAnnotationsAsync(request);
            keys.AddRange(page.Annotations.Select(a => a.AnnotationKey).Where(key => key.StartsWith(prefix, StringComparison.Ordinal)));
            request.ContinuationToken = page.ContinuationToken;
        }
        while (request.ContinuationToken is not null);

        return keys;
    }

    private async Task<List<string>> DescendantsAsync(string key, bool includeInactive = false)
    {
        var request = Request(includeInactive);
        request.DescendantsOf = AnnotationKey.Parse(key);
        var response = await Annotations.GetAnnotationsAsync(request);
        return response.Annotations.Select(a => a.AnnotationKey).ToList();
    }
}
