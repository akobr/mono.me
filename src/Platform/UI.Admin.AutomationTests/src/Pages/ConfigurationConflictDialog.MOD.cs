using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's members of ConfigurationConflictDialog, which the template editor (TemplateDetail.vue) shows
// too, with the same texts, when the JSON Patch of a template's save fails its test: the tab "Split" of "Diff layout",
// which shows the diff side by side with its labels also below lg, where "Auto" is unified.
public sealed partial class ConfigurationConflictDialog
{
    public ILocator SplitLayoutTab => Root.GetByRole(AriaRole.Tab, new() { Name = "Split", Exact = true });

    // Shows the diff side by side with its labels: "Split" below lg; nothing from lg.
    public async Task ShowSplitDiffAsync()
    {
        if (app.Page.ViewportSize is { Width: < VersionsView.SplitDiffWidth })
        {
            await SplitLayoutTab.ClickAsync();
            await Expect(SplitLayoutTab).ToHaveAttributeAsync("aria-selected", "true");
        }

        await Expect(TheirsLabel).ToBeVisibleAsync();
    }
}
