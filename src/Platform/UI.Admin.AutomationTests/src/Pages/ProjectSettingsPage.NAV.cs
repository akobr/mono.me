using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The members of the project's Settings that the Navigation section uses: the heading of the section that the old
// …/secrets redirects to (settings#secrets).
public sealed partial class ProjectSettingsPage
{
    // The h2 of section#secrets; upper case only by CSS, so its accessible name keeps this case.
    public ILocator SecretsHeading => SecretsSection.GetByRole(AriaRole.Heading, new() { Name = "Secrets", Exact = true, Level = 2 });
}
