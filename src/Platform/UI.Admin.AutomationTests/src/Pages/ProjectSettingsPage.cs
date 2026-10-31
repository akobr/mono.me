using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/settings. The "Project" card shows the access point and, for an Administrator or Owner,
// the description with "Save description" (disabled until it changes; the toast "Description saved"); a Reader sees
// the text only. The navbar's title is hidden below 2xl, so the card's heading is the sign the page is there.
public sealed class ProjectSettingsPage(App app, AppRoutes routes)
{
    public ILocator ProjectHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Project", Exact = true, Level = 2 });

    public ILocator Description => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true });

    public ILocator SaveDescriptionButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Save description", Exact = true });

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Settings);
        await app.WaitForReadyAsync();
        await Expect(ProjectHeading).ToBeVisibleAsync();
    }

    public async Task SaveDescriptionAsync(string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        await Description.FillAsync(description);
        await SaveDescriptionButton.ClickAsync();
        await Expect(app.Toast("Description saved")).ToBeVisibleAsync();
    }
}
