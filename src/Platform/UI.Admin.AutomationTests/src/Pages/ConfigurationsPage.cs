using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/<view>/configurations: the annotations with an own configuration, filtered like the
// annotations (the same "Filter annotations" box, ?q=) and virtualized too: a grid (a table without a name) from md
// (768 px), a list of buttons below. "Loading configurations…" shows while the list loads. "New configuration"
// (Contributor and up) picks an annotation without one; ConfigurationDetail opens one by its key instead.
public sealed partial class ConfigurationsPage(App app, AppRoutes routes)
{
    // The grid replaces the list from this width (the md breakpoint).
    public const int GridWidth = 768;

    public ILocator Filter => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Filter annotations", Exact = true });

    // The navbar's button (labelled "New" from md) and, in an empty list, the empty state's.
    public ILocator NewConfigurationButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New configuration", Exact = true }).First;

    // The loading state of the list (role status); gone once the configurations loaded.
    public ILocator Loading => app.Page.GetByText("Loading configurations…", new() { Exact = true });

    private bool HasGrid => app.Page.ViewportSize is not { Width: < GridWidth };

    // The row (grid) or the item (list) of an annotation's configuration, by the exact key in its Key cell or its key
    // line: not a row of a longer key that starts with it. Only rows in view exist, so filter first.
    public ILocator Item(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var keyText = app.Page.GetByText(key, new() { Exact = true });
        return HasGrid
            ? app.Page.GetByRole(AriaRole.Row).Filter(new() { Has = keyText })
            : app.Page.GetByRole(AriaRole.Listitem).Filter(new() { Has = keyText });
    }

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Configurations);
        await app.WaitForReadyAsync();
        await Expect(Filter).ToBeVisibleAsync();
    }

    // Types the query into the filter and waits until it applied: the debounced URL has exactly ?q=<query>, or no q for
    // an empty query, so a second filter or a page opened with ?q= does not return early.
    public async Task FilterAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        await Filter.FillAsync(query);
        await app.ExpectQueryAsync("q", query.Length == 0 ? null : query);
    }
}
