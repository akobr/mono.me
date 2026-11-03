using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// …/<view>/graph: the view's annotations as a network of ancestor-to-descendant links on a WebGL canvas, which the tests
// drive only through the DOM around it. The navbar has "Reload the catalog" (disabled while the catalog loads) and, for
// Contributors and up, "Bulk create" (from sm, icon-only below lg) and "New" (from sm) or the icon button "New
// annotation" (below sm). The toolbar above the graph: the compact filter bar ("Filter annotations", "Clear the
// filter", its count "T" or "N of T"), "Layout" (Layered, Force, Radial), in focus mode "Focus depth" (1 to 6, with
// "Increment" and "Decrement") and the focus key's badge, then "Fit", "Reheat", "Export PNG", "Open as list" (only for
// 1 to 300 drawn nodes) and "Show everything" (focus mode) or "Focus"; below 720 px of graph width Fit, Reheat, Export
// PNG and Open as list are icon-only. The graph is the application "Annotation graph, <summary>. Use the arrow keys …"
// (tabindex 0): its summary is "<shown> annotations" for the whole view or "<shown> of <total> annotations around <key>
// (depth <n>)" (" + filter matches" with a filter) in focus mode; it holds the legend (GraphLegend), the keyboard
// cursor's live region ("<Type> <name>, <n> connections"), the states (the empty view, "Every type is hidden") and,
// while the catalog loads or the worker lays out the first frame, a status. The URL keeps q, layout, focus and depth;
// Focus and Show everything push, the rest replaces. Above 5,000 annotations the graph focuses by itself and Show
// everything asks "Show all <n> annotations?" first; above 20,000 Show everything is disabled ("Available up to 20,000
// annotations"), so a check of the whole view is skipped there.
public sealed partial class GraphPage(App app, AppRoutes routes)
{
    // Above this many annotations the graph opens in focus mode, and Show everything asks first.
    public const int LargeViewSize = 5_000;

    // Above this many annotations Show everything is disabled: the graph cannot leave focus mode.
    public const int ShowAllLimit = 20_000;

    public ILocator Application => app.Page.GetByRole(AriaRole.Application);

    // The visible summary at the bottom left: "<shown> annotations", or in focus mode "<shown> of <T> annotations
    // around <key> (depth <n>)" (also the start of the graph's name); not the loading label "Laying out … annotations…".
    public ILocator SummaryText => Application.GetByText(SummaryTextRegex());

    // The live region of the keyboard cursor: "<Type label> <name>, <n> connection(s)".
    public ILocator CursorAnnouncement => Application.Locator("p[aria-live=polite]");

    // "Loaded <n> annotations…" or "Laying out <n> annotations…": until the first frame the arrow keys do nothing.
    public ILocator LoadingStatus => Application.GetByRole(AriaRole.Status);

    public ILocator ReloadCatalogButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Reload the catalog", Exact = true });

    // The navbar's create actions (Contributor and up): "New" from sm, the icon button "New annotation" below it, and
    // "Bulk create" from sm. The empty state's own buttons are EmptyNewAnnotationButton and EmptyBulkCreateButton.
    public ILocator HeaderNewButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New", Exact = true });

    public ILocator HeaderNewAnnotationButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New annotation", Exact = true });

    public ILocator HeaderBulkCreateButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Bulk create", Exact = true });

    public ILocator Filter => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Filter annotations", Exact = true });

    public ILocator LayoutSelect => app.Page.GetByRole(AriaRole.Combobox, new() { Name = "Layout", Exact = true });

    // The toolbar above the graph: the innermost element that holds both the filter and the layout.
    public ILocator Toolbar => app.Page.Locator("div").Filter(new() { Has = Filter }).Filter(new() { Has = LayoutSelect }).Last;

    public ILocator ClearFilterButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Clear the filter", Exact = true });

    // The filter bar's count: the view's total "T" without a filter, "<matches> of T" with one (over the whole view).
    public ILocator FilterCount => Toolbar.Locator("span[aria-live=polite]");

    // Focus mode only.
    public ILocator DepthInput => Toolbar.GetByRole(AriaRole.Spinbutton, new() { Name = "Focus depth", Exact = true });

    public ILocator IncrementDepthButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Increment", Exact = true });

    public ILocator DecrementDepthButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Decrement", Exact = true });

    public ILocator FitButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Fit", Exact = true });

    public ILocator ReheatButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Reheat", Exact = true });

    public ILocator ExportPngButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Export PNG", Exact = true });

    public ILocator OpenAsListButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Open as list", Exact = true });

    public ILocator ShowEverythingButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Show everything", Exact = true });

    public ILocator FocusButton => Toolbar.GetByRole(AriaRole.Button, new() { Name = "Focus", Exact = true });

    // The empty state of a view without annotations; Contributors and up get its two buttons.
    public ILocator EmptyState => Application.GetByRole(AriaRole.Heading, new() { Name = "No annotations in this view", Exact = true });

    public ILocator EmptyStateDescription => Application.GetByText("The graph shows annotations and their ancestors once the view has some.", new() { Exact = true });

    public ILocator EmptyNewAnnotationButton => Application.GetByRole(AriaRole.Button, new() { Name = "New annotation", Exact = true });

    public ILocator EmptyBulkCreateButton => Application.GetByRole(AriaRole.Button, new() { Name = "Bulk create", Exact = true });

    // Shown while every type of the drawn set is hidden, below the legend.
    public ILocator AllHiddenState => Application.GetByRole(AriaRole.Heading, new() { Name = "Every type is hidden", Exact = true });

    public ILocator AllHiddenDescription => Application.GetByText("Turn types back on in the legend.", new() { Exact = true });

    // The error toast of Export PNG without a canvas.
    public ILocator ExportFailedToast => app.Toast("The graph could not be exported");

    public GraphLegend Legend { get; } = new(app);

    // The focus key's badge, titled with the key.
    public ILocator FocusBadge(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Toolbar.GetByTitle(key, new() { Exact = true });
    }

    // An option of the open Layout select: "Layered", "Force" or "Radial".
    public ILocator LayoutOption(string layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layout);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = layout, Exact = true });
    }

    // Opens the graph by its URL (a page load) with the query, e.g. ("focus", key), and waits until the catalog loaded
    // (it may show its IndexedDB snapshot first, without the test's new annotations); WaitForLayoutAsync waits for the
    // first frame.
    public async Task OpenAsync(params (string Name, string? Value)[] query)
    {
        await app.GotoAsync(AppRoutes.WithQuery(routes.Graph, query));
        await app.WaitForReadyAsync();
        await WaitForCatalogAsync();
    }

    // The catalog loaded: "Reload the catalog" is enabled again, also while a full-screen detail hides the graph's
    // navbar (below md); up to Target:ApiTimeoutMs, the admin UI ends a type list that does not answer with an error.
    // The graph shows the catalog's store with the same button and the same error titles as the annotations page, so a
    // load that ended in "The catalog could not be loaded" or "… refreshed" throws with its text there too.
    public async Task WaitForCatalogAsync()
    {
        await new AnnotationsPage(app, routes).WaitForCatalogAsync();
    }

    // The graph has its first layout frame: no status in the graph, so the arrow keys and Export PNG work.
    public async Task WaitForLayoutAsync()
    {
        await Expect(LoadingStatus).ToHaveCountAsync(0);
    }

    // Reloads the catalog with "Reload the catalog" and waits until it was disabled (loading) and is enabled again (up
    // to Target:ApiTimeoutMs, WaitForCatalogAsync), and the graph is laid out. The mock answers within milliseconds, so
    // the button records its own disabled state: an assertion could miss it.
    public async Task ReloadCatalogAsync()
    {
        await WaitForCatalogAsync();
        await ReloadCatalogButton.EvaluateAsync(
            """
            button => {
              window.__graphCatalogReloading = false;
              new MutationObserver((records, observer) => {
                if (records.some((record) => record.oldValue === null)) {
                  window.__graphCatalogReloading = true;
                  observer.disconnect();
                }
              }).observe(button, { attributes: true, attributeFilter: ['disabled'], attributeOldValue: true });
            }
            """);
        await ReloadCatalogButton.ClickAsync();
        await app.Page.WaitForFunctionAsync("() => window.__graphCatalogReloading === true", null, new() { PollingInterval = 50 });
        await WaitForCatalogAsync();
        await WaitForLayoutAsync();
    }

    // The summary in focus mode: "<shown> of <T> annotations around <key> (depth <depth>[ + filter matches])", T any
    // number unless total is given (a deployed target's view keeps growing).
    public async Task ExpectFocusSummaryAsync(int shown, string key, int depth = 2, bool withFilter = false, int? total = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filter = withFilter ? @" \+ filter matches" : string.Empty;
        var of = total is { } exact ? string.Create(CultureInfo.InvariantCulture, $"{exact:N0}") : @"[\d,]+";
        var summary = string.Create(CultureInfo.InvariantCulture, $@"{shown:N0} of {of} annotations around {Regex.Escape(key)} \(depth {depth}{filter}\)");
        await Expect(Application).ToHaveAccessibleNameAsync(new Regex($"^Annotation graph, {summary}\\. "));
    }

    // The summary of the whole view: "<shown> annotations"; any number for null.
    public async Task ExpectWholeViewSummaryAsync(int? shown = null)
    {
        var count = shown is { } exact ? string.Create(CultureInfo.InvariantCulture, $"{exact:N0}") : @"[\d,]+";
        await Expect(Application).ToHaveAccessibleNameAsync(new Regex($"^Annotation graph, {count} annotations\\. "));
    }

    // The filter's count: the view's total T without a filter (matches null), "<matches> of T" with one, T any number.
    public async Task ExpectFilterCountAsync(int? matches = null)
    {
        await Expect(FilterCount).ToHaveTextAsync(FilterCountText(matches));
    }

    // The text of a filter bar's count (also the annotations page's): "T", or "<matches> of T", T any number.
    public static Regex FilterCountText(int? matches = null)
    {
        return matches is { } count
            ? new Regex(string.Create(CultureInfo.InvariantCulture, $@"^\s*{count:N0} of [\d,]+\s*$"))
            : TotalCountText();
    }

    // The view's total from the filter count: "T" without a filter, the T of "N of T" with one.
    public async Task<int> TotalAsync()
    {
        await Expect(FilterCount).ToHaveTextAsync(CountText());
        var text = (await FilterCount.TextContentAsync() ?? string.Empty).Trim();
        var of = text.LastIndexOf(" of ", StringComparison.Ordinal);
        return ParseCount(of < 0 ? text : text[(of + 4)..]);
    }

    // Whether the view is above LargeViewSize (a grown deployed target), where the graph focuses by itself and Show
    // everything asks first.
    public async Task<bool> IsLargeViewAsync()
    {
        return await TotalAsync() > LargeViewSize;
    }

    // Picks a layout ("Layered", "Force" or "Radial") and waits for the URL: layout=force or radial, none for Layered.
    public async Task SelectLayoutAsync(string layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layout);

        await LayoutSelect.ClickAsync();
        await LayoutOption(layout).ClickAsync();
        await Expect(LayoutSelect).ToHaveTextAsync(layout);
        await app.ExpectQueryAsync("layout", string.Equals(layout, "Layered", StringComparison.Ordinal) ? null : layout.ToLowerInvariant());
    }

    // Focus: the keyboard cursor, else the selected node, else the last opened annotation, else the most connected
    // subject or responsibility. In focus mode already (a large view focuses by itself) the whole view is shown first,
    // so above ShowAllLimit the test is skipped (ShowEverythingAsync). Returns once the URL has a focus.
    public async Task FocusAsync()
    {
        if (await ShowEverythingButton.IsVisibleAsync())
        {
            await ShowEverythingAsync();
        }

        await FocusButton.ClickAsync();
        await Expect(ShowEverythingButton).ToBeVisibleAsync();
        await app.Page.WaitForFunctionAsync("() => !!new URLSearchParams(location.search).get('focus')", null, new() { PollingInterval = 100 });
    }

    // Show everything: leaves focus mode (a push, so Back returns to it); a view above LargeViewSize asks "Show all <n>
    // annotations?" first, which is confirmed (the URL then has all=1). Returns once Focus is back. A view above
    // ShowAllLimit (a deployed target's view only grows) has the button disabled: the test is skipped then.
    public async Task ShowEverythingAsync()
    {
        var total = await TotalAsync();
        Skip.When(
            total > ShowAllLimit,
            string.Create(CultureInfo.InvariantCulture, $"The view has {total:N0} annotations, and Show everything is disabled above {ShowAllLimit:N0}: the whole view cannot be shown."));

        var large = total > LargeViewSize;
        await ShowEverythingButton.ClickAsync();
        if (large)
        {
            await new ConfirmDialog(app, ShowAllTitle()).ConfirmAsync("Show everything");
        }

        await app.ExpectQueryAsync("focus", null);
        await Expect(FocusButton).ToBeVisibleAsync();
    }

    // Moves the keyboard focus to the graph once it has its first frame (before it the arrow keys do nothing).
    public async Task FocusGraphAsync()
    {
        await WaitForLayoutAsync();
        await Application.FocusAsync();
        await Expect(Application).ToBeFocusedAsync();
    }

    private static int ParseCount(string text)
    {
        return int.Parse(text, NumberStyles.AllowThousands, CultureInfo.GetCultureInfo("en-US"));
    }

    [GeneratedRegex(@"^\s*[\d,]+ (?:annotations|of [\d,]+ annotations around .+)\s*$")]
    private static partial Regex SummaryTextRegex();

    [GeneratedRegex(@"^\s*(?:[\d,]+ of )?[\d,]+\s*$")]
    private static partial Regex CountText();

    [GeneratedRegex(@"^\s*[\d,]+\s*$")]
    private static partial Regex TotalCountText();

    [GeneratedRegex(@"^Show all [\d,]+ annotations\?$")]
    private static partial Regex ShowAllTitle();
}
