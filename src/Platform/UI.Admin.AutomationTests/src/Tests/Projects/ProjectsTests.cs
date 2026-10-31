using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Projects;

[Area(Area.Projects)]
public sealed class ProjectsTests : PlatformTest
{
    // Only an Owner of the organization may create a project.
    protected override UserRequirement RequiredUser => UserRequirement.WithRole("Owner");

    // A new project with a description, in the target's organization, opens its annotations; its Settings, reached
    // through the navigation, show the description.
    [Test]
    [Elementary]
    public async Task Project_Create_OpensSettings()
    {
        var project = PlatformData.ProjectKey();
        var description = PlatformData.Description(200);
        var created = Routes with { Project = project, View = AppRoutes.DefaultView };

        var overview = new OrganizationOverview(App, Routes);
        await overview.OpenAsync();
        await overview.CreateProjectAsync(project, description);
        await Expect(App.Toast($"Project {project} created")).ToBeVisibleAsync();
        await App.ExpectAtAsync(created.Annotations);

        await App.NavigateAsync("Settings");
        await App.ExpectAtAsync(created.Settings);
        var settings = new ProjectSettingsPage(App, created);
        await Expect(settings.Description).ToHaveValueAsync(description);
        await Expect(settings.SaveDescriptionButton).ToBeDisabledAsync();
    }
}
