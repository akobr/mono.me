using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/<view>/annotations: the catalog of the view as a grid (from md, 768 px) or a list on a
// phone, the filter above it and the navbar's actions. "Reload the catalog" is disabled while the catalog loads; Bulk
// create's review and the palette's annotation actions need the loaded catalog. Both the grid and the list are
// virtualized: rows out of view are not in the page, so find a row only in a filtered result (e.g. key:<key>).
public sealed partial class AnnotationsPage(App app, AppRoutes routes)
{
    // The grid replaces the list from this width (the md breakpoint).
    public const int GridWidth = 768;

    public ILocator ReloadCatalogButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Reload the catalog", Exact = true });

    // The catalog loaded: "Reload the catalog" is enabled again.
    public ILocator CatalogLoaded => app.Page.GetByRole(AriaRole.Button, new() { Name = "Reload the catalog", Exact = true, Disabled = false });

    // The heading of the empty state of a view without annotations (a new project's or view's), with "New annotation"
    // and "Bulk create" for Contributors and up.
    public ILocator EmptyState => app.Page.GetByRole(AriaRole.Heading, new() { Name = "No annotations in this view", Exact = true });

    // The heading of the error state of a catalog the API refused; the API's message follows it.
    public ILocator CatalogError => app.Page.GetByRole(AriaRole.Heading, new() { Name = "The catalog could not be loaded", Exact = true });

    // In the navbar from sm (640 px); a phone opens Bulk create from the command palette. An empty view's empty state
    // has a second one, after the navbar's, hence the first.
    public ILocator BulkCreateButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Bulk create", Exact = true }).First;

    // Debounced; the URL gets ?q=<query> once it applies. Grammar: type:<code>, key:<prefix>, label:, is:, has:, …
    public ILocator Filter => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Filter annotations", Exact = true });

    private bool HasGrid => app.Page.ViewportSize is not { Width: < GridWidth };

    // The row (grid) or the item (list) of an annotation, by the exact key in its Key cell (the grid) or its key line
    // (the list): not a row of a longer key that starts with it. Only rows in view exist, so filter first.
    public ILocator Item(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var keyText = app.Page.GetByText(key, new() { Exact = true });
        return HasGrid
            ? app.Page.GetByRole(AriaRole.Grid, new() { Name = "Annotations", Exact = true }).GetByRole(AriaRole.Row).Filter(new() { Has = keyText })
            : app.Page.GetByRole(AriaRole.List, new() { Name = "Annotations", Exact = true }).GetByRole(AriaRole.Listitem).Filter(new() { Has = keyText });
    }

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Annotations);
        await app.WaitForReadyAsync();
        await WaitForCatalogAsync();
    }

    public async Task WaitForCatalogAsync()
    {
        await Expect(ReloadCatalogButton).ToBeEnabledAsync();
    }

    // Types the query into the filter and waits until it applied: the debounced URL has exactly ?q=<query>, or no q for
    // an empty query, so a second filter or a page opened with ?q= does not return early.
    public async Task FilterAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        await Filter.FillAsync(query);
        await app.ExpectQueryAsync("q", query.Length == 0 ? null : query);
    }

    // The toolbar button on a wide screen, the command palette's "Bulk create annotations" on a phone; both wait for
    // the catalog.
    public async Task<BulkCreateDialog> OpenBulkCreateAsync()
    {
        await WaitForCatalogAsync();
        if (app.IsPhone)
        {
            await app.RunCommandAsync("Bulk create annotations");
        }
        else
        {
            await BulkCreateButton.ClickAsync();
        }

        var dialog = new BulkCreateDialog(app.Page);
        await Expect(dialog.Root).ToBeVisibleAsync();
        return dialog;
    }

    // Opens "New annotation" from the navbar ("New", or "New annotation" on a phone) once the catalog loaded, which the
    // dialog's suggestions and its exists check read.
    public async Task<NewAnnotationDialog> OpenNewAnnotationAsync()
    {
        await WaitForCatalogAsync();
        var dialog = new NewAnnotationDialog(app);
        await dialog.OpenAsync();
        return dialog;
    }

    // Creates annotations that do not exist yet, with their missing ancestors, and closes the dialog: the setup of a
    // test that acts on them. Every key must be new.
    public async Task BulkCreateAsync(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var dialog = await OpenBulkCreateAsync();
        await dialog.EnterKeysAsync(keys);
        await dialog.CreateAsync(keys.Length);
        foreach (var key in keys)
        {
            await Expect(dialog.CreatedRow(key)).ToContainTextAsync("From the list");
        }

        await dialog.CloseAsync();
    }
}
