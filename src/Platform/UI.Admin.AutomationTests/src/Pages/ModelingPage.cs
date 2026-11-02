using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// …/<view>/modeling: the view's schemas, templates, export and import, deleted annotations and the comparison of views,
// one page per tab under the header's tabs "Schemas", "Templates", "Export & Import" (Administrator and up), "Deleted"
// and "Compare views" (role tab; the selected one has aria-selected="true"; every activation is a navigation, so the
// arrow keys only move the focus). The navbar's title is hidden below 2xl, so the tabs are the sign the page is there.
// Below Contributor the whole page is the lock "Contributors only". The plain …/modeling opens Schemas; the sidebar's
// Modeling returns to the last tab and detail of the view (page memory, lost on a reload). Below md a detail is full
// screen and hides the tabs. ModelingSchemaPage and ModelingTemplatePage drive the editors.
public sealed partial class ModelingPage(App app, AppRoutes routes)
{
    // The lock of a Reader (no tabs).
    public ILocator ContributorsOnly => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Contributors only", Exact = true });

    // A tab of the header by its exact name: "Schemas", "Templates", "Export & Import", "Deleted", "Compare views".
    public ILocator Tab(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
    }

    // Opens a tab by its URL (schemas, templates, export-import, deleted, compare; AppRoutes.ModelingTab) and waits for
    // the header's tabs.
    public async Task OpenAsync(string tab = "schemas")
    {
        await app.GotoAsync(routes.ModelingTab(tab));
        await app.WaitForReadyAsync();
        await Expect(Tab("Schemas")).ToBeVisibleAsync();
    }

    // Waits until the tab is the selected one.
    public async Task ExpectSelectedAsync(string name)
    {
        await Expect(Tab(name)).ToHaveAttributeAsync("aria-selected", "true");
    }
}
