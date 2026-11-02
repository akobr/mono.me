using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The filters of the annotations as a bottom sheet below 768 px (components/annotations/AnnotationFilterSheet.vue):
// the button "Filters" beside the filter box (its visible label is the number of terms, once there are any) opens the
// drawer "Filters" ("Narrow the annotations of this view."). Its toggle buttons (aria-pressed) write the filter's text:
// the type plurals ("Executions", …) to type:, the statuses ("Active", "Disabled", "Scheduled", "Expired") to is:, "Has
// a configuration" to has:config. "Active filter" lists the terms as buttons "Remove <raw term>" with "Clear all";
// "Done" closes the sheet. The URL's q follows the box after its debounce of 200 ms.
public sealed partial class AnnotationFilterSheet(App app)
{
    public ILocator OpenButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Filters", Exact = true });

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "Filters", Exact = true });

    // The term buttons of "Active filter", "Remove <raw term>".
    public ILocator ActiveFilterTerms => Root.GetByRole(AriaRole.Button, new() { NameRegex = RemoveTermName() });

    public ILocator ClearAllButton => Root.GetByRole(AriaRole.Button, new() { Name = "Clear all", Exact = true });

    public ILocator DoneButton => Root.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true });

    // A toggle of the sheet by its exact label ("Executions", "Active", "Has a configuration"); aria-pressed tells
    // whether its term is in the filter.
    public ILocator Toggle(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Root.GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    // The button of a term in "Active filter", "Remove <raw term>" (e.g. "Remove is:active").
    public ILocator ActiveFilterTerm(string rawTerm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTerm);
        return Root.GetByRole(AriaRole.Button, new() { Name = $"Remove {rawTerm}", Exact = true });
    }

    public async Task OpenAsync()
    {
        await OpenButton.ClickAsync();
        await Expect(Root).ToBeVisibleAsync();
    }

    public async Task DoneAsync()
    {
        await DoneButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    [GeneratedRegex("^Remove ")]
    private static partial Regex RemoveTermName();
}
