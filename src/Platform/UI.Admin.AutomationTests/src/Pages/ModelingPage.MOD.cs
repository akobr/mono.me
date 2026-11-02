using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's members of ModelingPage: the header's tabs in their order, the lock text of a Reader, and the
// tab that has the focus after the arrow keys (manual activation: the arrows move the focus, Enter or Space opens).
public sealed partial class ModelingPage
{
    // The description under "Contributors only".
    public const string ContributorsOnlyText = "The schemas, the templates, the deleted annotations and the comparison of views are available to the project's Contributors, the export and the import to its Administrators.";

    // The tabs of the header, in their order: the first tablist of the page (the header comes before the detail, whose
    // Edit and Versions are a tablist of their own).
    public ILocator HeaderTabs => app.Page.GetByRole(AriaRole.Tablist).First.GetByRole(AriaRole.Tab);

    // Every tab of the page, the header's and a detail's: none under a lock.
    public ILocator AllTabs => app.Page.GetByRole(AriaRole.Tab);

    // Every selected tab of the page: one of the header's, plus the detail's Edit or Versions when a detail is open.
    public ILocator SelectedTabs => app.Page.GetByRole(AriaRole.Tab, new() { Selected = true });

    public ILocator ContributorsOnlyDescription => app.Page.GetByText(ContributorsOnlyText, new() { Exact = true });

    // Opens a tab of the header with a click (the tab bar scrolls sideways on a phone; Playwright scrolls the tab into
    // view) and waits until its page shows it selected.
    public async Task SelectTabAsync(string name)
    {
        await Tab(name).ClickAsync();
        await ExpectSelectedAsync(name);
    }
}
