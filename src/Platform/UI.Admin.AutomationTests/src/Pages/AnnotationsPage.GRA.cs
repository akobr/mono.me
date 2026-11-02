using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// What the Graph section reads on the annotations page: the filter bar's count after "Open as list".
public sealed partial class AnnotationsPage
{
    // The count of the filter bar (aria-live): the view's total "T" without a filter, "<matches> of T" with one; the
    // live span of the innermost element that holds the filter.
    public ILocator FilterBarCount => app.Page.Locator("div")
        .Filter(new() { Has = Filter })
        .Filter(new() { Has = app.Page.Locator("span[aria-live=polite]") })
        .Last
        .Locator("span[aria-live=polite]");
}
