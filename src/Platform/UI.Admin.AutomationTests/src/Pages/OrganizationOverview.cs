using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>: the organization's name as the page's heading, a card per project of the user, and "New project"
// in the navbar, enabled only for an Owner of the organization. A new project is named by its key (2 to 63 of a-z,
// 0-9 and '-'); its creator becomes its Owner and lands on its default view's annotations. A project's card has its
// link (named by the project, to its default view's annotations), the user's role badge, the key <org>.<project> and
// the description, clamped to two lines (the whole text is its title).
public sealed partial class OrganizationOverview(App app, AppRoutes routes)
{
    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { Name = routes.Organization, Exact = true, Level = 1 });

    public ILocator NewProjectButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New project", Exact = true });

    public ILocator NewProjectDialog => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New project", Exact = true });

    // A required field's accessible name ends in '*' ("Project*").
    public ILocator ProjectName => NewProjectDialog.GetByRole(AriaRole.Textbox, new() { NameRegex = ProjectLabel() });

    public ILocator ProjectDescription => NewProjectDialog.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true });

    public ILocator CreateProjectButton => NewProjectDialog.GetByRole(AriaRole.Button, new() { Name = "Create project", Exact = true });

    // The link of a project's card, to its default view's annotations.
    public ILocator ProjectLink(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        return app.Page.GetByRole(AriaRole.Link, new() { Name = project, Exact = true })
            .And(app.Page.Locator($"a[href*=\"/{routes.Organization}/{project}/\"]"));
    }

    // The card of a project of the user (Nuxt UI's card, the link's nearest data-slot="root"): its role badge ("Owner",
    // "Contributor + secrets", …), the key and the description.
    public ILocator ProjectCard(string project)
    {
        return ProjectLink(project).Locator("xpath=ancestor::*[@data-slot='root'][1]");
    }

    // The description of a project's card; its title attribute holds the whole text.
    public ILocator ProjectCardDescription(string project)
    {
        return ProjectCard(project).Locator("p[title]");
    }

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.OrganizationOverview);
        await app.WaitForReadyAsync();
        await Expect(Heading).ToBeVisibleAsync();
    }

    // Opens "New project" and returns once its dialog shows; only an Owner of the organization has it enabled.
    public async Task OpenNewProjectAsync()
    {
        await NewProjectButton.ClickAsync();
        await Expect(NewProjectDialog).ToBeVisibleAsync();
    }

    // Fills and submits "New project"; the dialog closes once the project exists, and the app opens its annotations
    // (the toast "Project <name> created" says so).
    public async Task CreateProjectAsync(string project, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        await OpenNewProjectAsync();
        await ProjectName.FillAsync(project);
        if (description is not null)
        {
            await ProjectDescription.FillAsync(description);
        }

        await CreateProjectButton.ClickAsync();
        await Expect(NewProjectDialog).ToBeHiddenAsync();
    }

    [GeneratedRegex("^Project")]
    private static partial Regex ProjectLabel();
}
