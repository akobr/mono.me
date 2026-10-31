using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/<view>/configurations: the annotations with an own configuration, filtered like the
// annotations (the same "Filter annotations" box) and virtualized too. "New configuration" (Contributor and up) picks
// an annotation without one; ConfigurationDetail opens one by its key instead.
public sealed class ConfigurationsPage(App app, AppRoutes routes)
{
    public ILocator Filter => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Filter annotations", Exact = true });

    // The navbar's button (labelled "New" from md) and, in an empty list, the empty state's.
    public ILocator NewConfigurationButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New configuration", Exact = true }).First;

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Configurations);
        await app.WaitForReadyAsync();
        await Expect(Filter).ToBeVisibleAsync();
    }
}
