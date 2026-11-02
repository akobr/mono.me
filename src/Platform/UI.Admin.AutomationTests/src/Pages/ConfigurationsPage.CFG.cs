using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Configurations section's part of the list: the empty state (an h2, "No configuration matches the filter" with a
// filter, "No configurations in this view" without; for Contributors and up its description and a second "New
// configuration" after the navbar's) and the New configuration dialog. With a filter the empty state also shows for a
// moment while the catalog loads.
public sealed partial class ConfigurationsPage
{
    public const string NoMatchTitle = "No configuration matches the filter";

    public const string EmptyStateDescriptionText = "Create one with New, or from the configuration link in the header of an annotation.";

    // The columns of the grid: the type's name, the annotation's Name (else the last part of its key), the key, "v<n>",
    // the author of the last version (a link for Administrators) and the time of it ("just now" under a minute).
    public const int TypeColumn = 0;
    public const int NameColumn = 1;
    public const int KeyColumn = 2;
    public const int VersionColumn = 3;
    public const int AuthorColumn = 4;
    public const int UpdatedColumn = 5;

    public ILocator EmptyStateHeading => app.Page.GetByRole(AriaRole.Heading, new() { NameRegex = EmptyStateTitle() });

    public ILocator NoMatchHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = NoMatchTitle, Exact = true });

    public ILocator EmptyStateDescription => app.Page.GetByText(EmptyStateDescriptionText, new() { Exact = true });

    // Every "New configuration" of the page: the navbar's first (icon only below 768 px), the empty state's last.
    public ILocator NewConfigurationButtons => app.Page.GetByRole(AriaRole.Button, new() { Name = "New configuration", Exact = true });

    public ILocator EmptyStateNewConfigurationButton => NewConfigurationButtons.Last;

    // True from 768 px, where the list is a grid; a list of buttons below.
    public bool HasGridLayout => HasGrid;

    // The count beside the filter while the filter has terms, "<shown> of <total>" (the browser's number format).
    public ILocator FilteredCount => app.Page.GetByText(FilteredCountText());

    // The first configuration of the list (sorted by key): the grid's first row of cells (not the header's), or the
    // list's first item.
    public ILocator FirstItem => HasGrid
        ? app.Page.GetByRole(AriaRole.Row).Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell) }).First
        : app.Page.GetByRole(AriaRole.Listitem).Filter(new() { Has = app.Page.GetByRole(AriaRole.Button) }).First;

    // A cell of the key's grid row by its column (TypeColumn … UpdatedColumn); the grid only, from 768 px.
    public ILocator ItemCell(string key, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, UpdatedColumn);
        return Item(key).GetByRole(AriaRole.Cell).Nth(column);
    }

    // Opens the list with a filter in its URL (?q=) and waits until the filter box shows it.
    public async Task OpenFilteredAsync(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        await app.GotoAsync(AppRoutes.WithQuery(routes.Configurations, ("q", query)));
        await app.WaitForReadyAsync();
        await Expect(Filter).ToHaveValueAsync(query);
    }

    // Opens New configuration with one of its buttons (NewConfigurationButton, EmptyStateNewConfigurationButton).
    public async Task<NewConfigurationDialog> OpenNewConfigurationAsync(ILocator button)
    {
        ArgumentNullException.ThrowIfNull(button);

        await button.ClickAsync();
        var dialog = new NewConfigurationDialog(app);
        await dialog.ExpectOpenAsync();
        return dialog;
    }

    // Opens the configuration of a row by its Name cell (the Author cell can be a link), or of a list item by its
    // button; the detail's URL keeps the list's q.
    public async Task OpenItemAsync(ILocator item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (HasGrid)
        {
            await item.GetByRole(AriaRole.Cell).Nth(NameColumn).ClickAsync();
        }
        else
        {
            await item.GetByRole(AriaRole.Button).First.ClickAsync();
        }

        await Expect(new DetailPanel(app, "configuration-detail").Root).ToBeVisibleAsync();
    }

    // GetByText's regex sees the text with the spaces of the template around it.
    [GeneratedRegex(@"^\s*\d[\d,.\s]* of \d[\d,.\s]*$")]
    private static partial Regex FilteredCountText();

    [GeneratedRegex("^(No configuration matches the filter|No configurations in this view)$")]
    private static partial Regex EmptyStateTitle();
}
