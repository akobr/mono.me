using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Projects;

// Projects: New project on the organization overview, its validation, and who may create one.
[Area(Area.Projects)]
public sealed partial class ProjectTests : PlatformTest
{
    private const string PatternMessage = "Use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit.";

    // ORG-PRJ-01: New project opens its annotations and its Settings show the description.
    // Only an Owner of the organization may create a project. A new project with a description, in the target's
    // organization, opens its annotations; its Settings, reached through the navigation, show the description.
    [Test]
    [Elementary]
    [TestCase(5)]
    [UserRole(Role.Owner)]
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
        await App.ExpectAtAsync(created.Settings());
        var settings = new ProjectSettingsPage(App, created);
        await Expect(settings.Description).ToHaveValueAsync(description);
        await Expect(settings.SaveDescriptionButton).ToBeDisabledAsync();
    }

    // ORG-PRJ-02: New project validates the name and rejects an existing project.
    // The client shows the first error of each field under it, after each submit: the empty name, a name against the
    // pattern, a reserved project name and a description over 500 characters. The target's own project passes the
    // client, and the API refuses it as a conflict, shown under Project without a toast. Cancel stays on the overview
    // and creates nothing.
    [Test]
    [TestCase(26)]
    [UserRole(Role.Owner)]
    public async Task Project_CreateInvalid_ShowsFieldErrors()
    {
        var project = PlatformData.ProjectKey();

        var overview = new OrganizationOverview(App, Routes);
        await overview.OpenAsync();
        await overview.OpenNewProjectAsync();
        await Expect(overview.NewProjectMessage($"The project is created in {Routes.Organization}; you become its owner.")).ToBeVisibleAsync();

        await overview.CreateProjectButton.ClickAsync();
        await Expect(overview.ProjectName).ToHaveAccessibleDescriptionAsync("Enter a project name.");
        await Expect(overview.NewProjectDialog).ToBeVisibleAsync();

        await overview.ProjectName.FillAsync("Bad_Name");
        await overview.CreateProjectButton.ClickAsync();
        await Expect(overview.ProjectName).ToHaveAccessibleDescriptionAsync(PatternMessage);

        await overview.ProjectName.FillAsync("settings");
        await overview.CreateProjectButton.ClickAsync();
        await Expect(overview.ProjectName).ToHaveAccessibleDescriptionAsync("This project name is reserved.");

        await overview.ProjectName.FillAsync(project);
        await overview.ProjectDescription.FillAsync(PlatformData.Text(501));
        await overview.CreateProjectButton.ClickAsync();
        await Expect(overview.ProjectDescription).ToHaveAccessibleDescriptionAsync("At most 500 characters.");
        await Expect(overview.NewProjectMessage("This project name is reserved.")).ToHaveCountAsync(0);

        // The target's project exists: the API answers 409, which the form shows under Project; nothing is created, so
        // no toast follows and the page stays on the overview.
        await overview.ProjectName.FillAsync(Routes.Project);
        await overview.ProjectDescription.FillAsync(string.Empty);
        await overview.CreateProjectButton.ClickAsync();
        await Expect(overview.ProjectName).ToHaveAccessibleDescriptionAsync("A project with this name already exists.");
        await Expect(App.Notifications.GetByText(ProjectCreatedToast())).ToHaveCountAsync(0);
        await Expect(overview.NewProjectDialog).ToBeVisibleAsync();
        await App.ExpectAtAsync(Routes.OrganizationOverview);

        await overview.NewProjectCancelButton.ClickAsync();
        await Expect(overview.NewProjectDialog).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.OrganizationOverview);
        await Expect(overview.ProjectLink(project)).ToHaveCountAsync(0);
    }

    // ORG-PRJ-03: A project member without an organization role cannot create projects.
    // On the mock, Ada is the Reader of northwind/portal and holds no role on northwind: /orgs marks the organization
    // "Project member" and lists the project with Reader, and the overview says "Member of some projects", shows the
    // project's card, explains that project roles do not grant the organization's, and disables New project. Mock only:
    // on a deployed target this needs an organization that another identity owns.
    [Test]
    [TestCase(28)]
    [UserRole(Role.Reader)]
    [MockOnly("a project role without an organization role, which the default dataset gives Ada in northwind/portal")]
    public async Task Project_CreateWithoutOrganizationRole_IsDisabled()
    {
        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();
        await Expect(organizations.Organization(Routes.Organization)).ToBeVisibleAsync();
        await Expect(organizations.OrganizationRoleBadge(Routes.Organization, "Project member")).ToBeVisibleAsync();
        await Expect(organizations.Project(Routes.Organization, Routes.Project)).ToHaveAccessibleNameAsync(new Regex($@"^{Regex.Escape(Routes.Project)}\s*Reader$"));

        await organizations.Organization(Routes.Organization).ClickAsync();
        await App.ExpectAtAsync(Routes.OrganizationOverview);
        var overview = new OrganizationOverview(App, Routes);
        await Expect(overview.Heading).ToBeVisibleAsync();
        await Expect(overview.HeadingBadge("Member of some projects")).ToBeVisibleAsync();

        await Expect(overview.ProjectLink(Routes.Project)).ToBeVisibleAsync();
        await Expect(overview.ProjectCardRoleBadge(Routes.Project, "Reader")).ToBeVisibleAsync();
        await Expect(overview.ProjectCardKey(Routes.Project)).ToBeVisibleAsync();

        await Expect(overview.NoOrganizationRoleNote).ToBeVisibleAsync();

        // Not hovered: a disabled button does not dispatch pointer events in every engine, so its tooltip "Requires the
        // Owner role" is left out.
        await Expect(overview.NewProjectButton).ToBeDisabledAsync();
    }

    [GeneratedRegex("^Project .+ created$")]
    private static partial Regex ProjectCreatedToast();
}
