using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Modeling's Compare views tab (…/modeling/compare, pages/…/modeling/compare.vue): the effective configuration of a key
// in this view against another view of the project. The pickers "Annotation key" (?key=, the keys with a
// configuration) and "Compare <view> with" (?to=, the project's other views; the only other one is preselected, and a
// ?to= of the view itself is replaced). The tab is disabled in a project with one view, whose page says "No other view
// to compare with".
public sealed partial class ModelingComparePage(App app, AppRoutes routes)
{
    public ILocator KeyPicker => app.Page.GetByRole(AriaRole.Combobox, new() { Name = "Annotation key", Exact = true });

    // Opens the tab by its URL, with ?to= when given, and waits for the app.
    public async Task OpenAsync(string? to = null)
    {
        await app.GotoAsync(routes.Compare(to));
        await app.WaitForReadyAsync();
    }
}
