#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Every test works in its own project of the test organization.
public class CosmosViewServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = TestConstants.Organization;

    private IViewService Views => Context.Services.GetRequiredService<IViewService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task GetViews_EmptyProject_ReturnsTheImplicitDefault()
    {
        var views = await Views.GetViewsAsync(Organization, NewProject(), discover: false);

        views.Should().ContainSingle();
        views[0].Name.Should().Be(Constants.DefaultViewName);
        views[0].IsDefault.Should().BeTrue();
        views[0].IsRegistered.Should().BeFalse();
    }

    [Fact]
    public async Task CreateView_RegistersItWithDescriptionAndAuthor()
    {
        var project = NewProject();

        var created = await Views.CreateViewAsync(Organization, project, new ViewCreate { Name = "rollout", Description = "  Canary rollout  " }, AuditActor.Account("user-1"));
        var views = await Views.GetViewsAsync(Organization, project, discover: false);

        created.IsRegistered.Should().BeTrue();
        created.Description.Should().Be("Canary rollout");
        views.Select(view => (view.Name, view.IsRegistered)).Should().Equal((Constants.DefaultViewName, false), ("rollout", true));
        views[1].Created!.By.Should().Be(AuditActor.Account("user-1"));
        views[1].Created.At.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task CreateView_Twice_ThrowsViewExists()
    {
        var project = NewProject();
        await Views.CreateViewAsync(Organization, project, new ViewCreate { Name = "rollout" }, AuditActor.Account("author"));

        var again = () => Views.CreateViewAsync(Organization, project, new ViewCreate { Name = "rollout" }, AuditActor.Account("author"));
        var defaultView = () => Views.CreateViewAsync(Organization, project, new ViewCreate { Name = Constants.DefaultViewName }, AuditActor.Account("author"));

        (await again.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ViewExists);
        (await defaultView.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ViewExists);
    }

    [Theory]
    [InlineData("Rollout")]
    [InlineData("roll.out")]
    [InlineData("members")]
    public async Task CreateView_InvalidOrReservedName_ThrowsInvalidName(string name)
    {
        var act = () => Views.CreateViewAsync(Organization, NewProject(), new ViewCreate { Name = name }, AuditActor.Account("author"));

        (await act.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidName);
    }

    [Fact]
    public async Task UpdateView_LegacyName_ThrowsInvalidNameInsteadOfRegisteringAnotherCase()
    {
        var project = NewProject();

        var act = () => Views.UpdateViewAsync(Organization, project, "Prod", new ViewUpdate(), AuditActor.Account("author"));

        (await act.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidName);
        (await Views.GetViewsAsync(Organization, project, discover: false)).Select(view => view.Name).Should().NotContain("prod");
    }

    [Fact]
    public async Task UpdateView_RegistersAndDescribesTheDefaultAndNewViews()
    {
        var project = NewProject();

        var defaultView = await Views.UpdateViewAsync(Organization, project, Constants.DefaultViewName, new ViewUpdate { Description = "Production" }, AuditActor.Account("author"));
        var beta = await Views.UpdateViewAsync(Organization, project, "beta", new ViewUpdate { Description = "Beta testers" }, AuditActor.Account("author"));
        var cleared = await Views.UpdateViewAsync(Organization, project, "beta", new ViewUpdate { Description = " " }, AuditActor.Account("other"));
        var views = await Views.GetViewsAsync(Organization, project, discover: false);

        defaultView.IsDefault.Should().BeTrue();
        defaultView.IsRegistered.Should().BeTrue();
        beta.Description.Should().Be("Beta testers");
        cleared.Description.Should().BeNull();
        cleared.Created!.By.Id.Should().Be("author");
        views.Select(view => (view.Name, view.Description)).Should().Equal((Constants.DefaultViewName, "Production"), ("beta", (string?)null));
    }

    [Fact]
    public async Task GetViews_Discover_AddsViewsThatOnlyExistInTheData()
    {
        var project = NewProject();
        await Views.CreateViewAsync(Organization, project, new ViewCreate { Name = "registered" }, AuditActor.Account("author"));
        await CreateResponsibilityAsync(project, "hidden");
        await CreateResponsibilityAsync(project, "registered");

        var plain = await Views.GetViewsAsync(Organization, project, discover: false);
        var discovered = await Views.GetViewsAsync(Organization, project, discover: true);

        plain.Select(view => view.Name).Should().Equal(Constants.DefaultViewName, "registered");
        discovered.Select(view => (view.Name, view.IsRegistered)).Should().Equal(
            (Constants.DefaultViewName, false),
            ("hidden", false),
            ("registered", true));
    }

    private static string NewProject() => $"vw-{Guid.NewGuid():N}"[..15];

    private Task CreateResponsibilityAsync(string project, string view)
    {
        var key = AnnotationKey.CreateResponsibility("billing");
        return Annotations.CreateAnnotationAsync(Organization, new Responsibility
        {
            AnnotationKey = key,
            AnnotationType = AnnotationType.Responsibility,
            Name = "billing",
            ProjectName = project,
            ViewName = view,
        });
    }
}
