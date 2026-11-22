using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Access section's part of /orgs: what shows that the user left a project (ProjectSettingsPage.ConfirmLeaveAsync
// opens this page), i.e. no link of the page leads into it any more.
public sealed partial class OrganizationsPage
{
    // Every link of the page into the project (/<organization>/<project>/…): its link in the organization's card
    // (Project) and any other, such as the navigation's; none once the user is no member of the project.
    public ILocator LinksInto(string organization, string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        return app.Page.Locator($"a[href*=\"/{organization}/{project}/\"]");
    }
}
