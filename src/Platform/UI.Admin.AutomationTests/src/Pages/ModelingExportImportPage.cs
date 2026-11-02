using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Modeling's Export & Import tab (…/modeling/export-import, pages/…/modeling/export-import.vue), Administrator and up:
// the cards "Export" and "Import" (regions named by their h2, side by side from lg) and, once the view's latest import
// succeeded, "Latest import". Both cards have the same four kind checkboxes, so scope them to their region. A
// Contributor gets the lock "Administrators only" instead, under the four other tabs with none selected; the tab
// itself is listed from Administrator up.
public sealed partial class ModelingExportImportPage(App app, AppRoutes routes)
{
    // The description under "Administrators only".
    public const string AdministratorsOnlyText = "The export and the import of the view are available to the project's Administrators.";

    public ILocator ExportRegion => app.Page.GetByRole(AriaRole.Region, new() { Name = "Export", Exact = true });

    public ILocator ImportRegion => app.Page.GetByRole(AriaRole.Region, new() { Name = "Import", Exact = true });

    // The lock of a Contributor.
    public ILocator AdministratorsOnly => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Administrators only", Exact = true });

    public ILocator AdministratorsOnlyDescription => app.Page.GetByText(AdministratorsOnlyText, new() { Exact = true });

    // Opens the tab by its URL and waits for the app; the page shows the cards or a lock.
    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.ModelingTab("export-import"));
        await app.WaitForReadyAsync();
    }
}
