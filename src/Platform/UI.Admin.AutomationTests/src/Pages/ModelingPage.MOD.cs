using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's members of ModelingPage: the header's tabs in their order, the selected tabs of the page, and
// SelectTabAsync, which opens a tab with a click.
public sealed partial class ModelingPage
{
    // The tabs of the header, in their order: the first tablist of the page (the header comes before the detail, whose
    // Edit and Versions are a tablist of their own).
    public ILocator HeaderTabs => app.Page.GetByRole(AriaRole.Tablist).First.GetByRole(AriaRole.Tab);

    // Every selected tab of the page: one of the header's, plus the detail's Edit or Versions when a detail is open.
    public ILocator SelectedTabs => app.Page.GetByRole(AriaRole.Tab, new() { Selected = true });

    // Opens a tab of the header with a click (the tab bar scrolls sideways on a phone; Playwright scrolls the tab into
    // view) and waits until its page shows it selected.
    public async Task SelectTabAsync(string name)
    {
        await Tab(name).ClickAsync();
        await ExpectSelectedAsync(name);
    }
}
