using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Modeling's Compare views tab (…/modeling/compare, pages/…/modeling/compare.vue): the effective configuration of a key
// in this view against another view of the project. The pickers "Annotation key" (?key=, the keys with a
// configuration when the page loaded; placeholder "Pick a key", a virtualized list with its search box "Search…") and
// "Compare <view> with" (?to=, the project's other views; the only other one is preselected, and a ?to= of the view
// itself is replaced). The tab is disabled in a project with one view, whose page says "No other view to compare with";
// without a key or a target it says "Compare a configuration across views". Ready, the DiffEditor shows the summary
// "+<a> −<d> · <k> change(s)" and the two documents: side by side from lg (VersionsView.SplitDiffWidth) under the
// labels <view> and <other view>, unified (one editor) below.
public sealed partial class ModelingComparePage(App app, AppRoutes routes)
{
    public const string NoOtherViewDescriptionText = "The project has only this view. An Administrator can create another one on the Views page.";

    // The trigger of the key's select menu: a button named by its label, whose text is the key or "Pick a key".
    public ILocator KeyPicker => app.Page.GetByRole(AriaRole.Button, new() { Name = "Annotation key", Exact = true });

    // The search box of the open key picker.
    public ILocator KeySearch => app.Page.GetByPlaceholder("Search…", new() { Exact = true });

    // The empty state of a project with one view.
    public ILocator NoOtherView => app.Page.GetByRole(AriaRole.Heading, new() { Name = "No other view to compare with", Exact = true });

    public ILocator NoOtherViewDescription => app.Page.GetByText(NoOtherViewDescriptionText, new() { Exact = true });

    // The empty state without a key or another view.
    public ILocator PickPrompt => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Compare a configuration across views", Exact = true });

    // "+<additions> −<deletions> · <n> change(s)" of the diff.
    public ILocator DiffSummary => app.Page.GetByText(DiffSummaryText());

    // The read-only editors of the diff: the view's and the other view's side by side, or one unified editor.
    public ILocator DiffEditors => app.Page.GetByRole(AriaRole.Textbox);

    // The diff: the innermost element around its summary and its editors, which holds the views' labels too.
    public ILocator DiffArea => app.Page.Locator("div").Filter(new() { Has = DiffSummary }).Filter(new() { Has = DiffEditors }).Last;

    // The picker of the other view, "Compare <view> with"; its text is the picked view.
    public ILocator ViewPicker => app.Page.GetByRole(AriaRole.Combobox, new() { Name = $"Compare {routes.View} with", Exact = true });

    // An option of the open key picker.
    public ILocator KeyOption(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = key, Exact = true });
    }

    // The labels above the split diff, this view's and the other view's name: the element of the diff with the other
    // view's name that holds no editor (no document holds that name; the view's own name can be a token of a value).
    public ILocator SideLabels(string otherView)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(otherView);
        return DiffArea.Locator("div").Filter(new() { Has = app.Page.GetByText(otherView, new() { Exact = true }), HasNot = DiffEditors });
    }

    // Opens the tab by its URL, with ?to= when given, and waits for the app.
    public async Task OpenAsync(string? to = null)
    {
        await app.GotoAsync(routes.Compare(to));
        await app.WaitForReadyAsync();
    }

    // Opens the tab by its URL with ?to= and ?key= (null leaves a parameter out) and waits for the app.
    public async Task OpenAsync(string? to, string? key)
    {
        await app.GotoAsync(AppRoutes.WithQuery(routes.Compare(to), ("key", key)));
        await app.WaitForReadyAsync();
    }

    // Picks a key in "Annotation key": opens it, searches the key (the list is virtualized), clicks its option and
    // waits for ?key=.
    public async Task PickKeyAsync(string key)
    {
        await KeyPicker.ClickAsync();
        await KeySearch.FillAsync(key);
        await KeyOption(key).ClickAsync();
        await app.ExpectQueryAsync("key", key);
    }

    [GeneratedRegex(@"^\s*\+\d+ −\d+ · \d+ changes?\s*$")]
    private static partial Regex DiffSummaryText();
}
