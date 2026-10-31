using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /orgs: one card per organization of the user, with the organization's link and the links of its projects, whose
// accessible names end in the user's role ("billing Owner").
public sealed class OrganizationsPage(App app)
{
    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Organizations", Exact = true, Level = 1 });

    // Labelled on wide screens, icon-only with the same accessible name on a phone.
    public ILocator NewOrganization => app.Page.GetByRole(AriaRole.Button, new() { Name = "New organization", Exact = true });

    public ILocator Organization(string organization)
    {
        return app.Page.GetByRole(AriaRole.Link, new() { Name = organization, Exact = true });
    }

    // The project's link in its organization's card: named "<project> <role>", pointing to /<organization>/<project>/….
    public ILocator Project(string organization, string project)
    {
        return app.Page
            .GetByRole(AriaRole.Link, new() { NameRegex = new Regex($@"^{Regex.Escape(project)}\b") })
            .And(app.Page.Locator($"a[href*=\"/{organization}/{project}/\"]"));
    }

    public async Task OpenAsync()
    {
        await app.GotoAsync("orgs");
        await app.WaitForReadyAsync();
    }
}
