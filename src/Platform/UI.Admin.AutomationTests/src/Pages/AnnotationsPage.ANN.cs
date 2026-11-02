using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Annotations section's members of the annotations page: the filter's count, chips, notice and syntax popover, the
// rows of the grid (from 768 px) or the list, the tree's controls (desktop grid only), the row checkboxes and sorting
// of the grid, the empty state's buttons, the bulk bar and the phone's filter sheet.
public sealed partial class AnnotationsPage
{
    // The count of the filter bar: the total ("367") without a filter term, "<shown> of <total>" ("7 of 367", with
    // thousands separators) with any. The only span that is a live region on the page.
    public ILocator FilterCount => app.Page.Locator("span[aria-live=\"polite\"]");

    public ILocator AnnotationGrid => app.Page.GetByRole(AriaRole.Grid, new() { Name = "Annotations", Exact = true });

    public ILocator AnnotationList => app.Page.GetByRole(AriaRole.List, new() { Name = "Annotations", Exact = true });

    // The rows of annotations in view: the grid's rows with a "Select <key>" checkbox (not its header, its separators
    // and the virtualizer's spacers), or the list's items on a phone. In the tree an annotation has a row per path.
    public ILocator AnnotationRows => HasGrid
        ? AnnotationGrid.GetByRole(AriaRole.Row).Filter(new() { Has = app.Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = RowCheckboxName() }) })
        : AnnotationList.GetByRole(AriaRole.Listitem);

    // The grid's or the list's text when no annotation matches the filter.
    public ILocator NoMatchingAnnotations => app.Page.GetByText("No annotations match the filter.", new() { Exact = true });

    // Only while the filter has text.
    public ILocator ClearFilterButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Clear the filter", Exact = true });

    public ILocator FilterSyntaxButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Filter syntax", Exact = true });

    // The popover of "Filter syntax": its title, the qualifiers and the example buttons.
    public ILocator FilterSyntaxPopover => app.Page.GetByRole(AriaRole.Dialog).Filter(new() { Has = app.Page.GetByText("Filter syntax", new() { Exact = true }) });

    // The popover's paragraph of the qualifiers ("Qualifiers: type, label, is, …").
    public ILocator FilterSyntaxQualifiers => FilterSyntaxPopover.GetByText(new Regex("^Qualifiers: "));

    // The tabs "Grid" and "Tree" of the grid (from 768 px); Tree adds ?mode=tree.
    public ILocator ListModeTabs => app.Page.GetByRole(AriaRole.Tablist, new() { Name = "List mode", Exact = true });

    // "By subject" or "By responsibility" (?by=responsibility), in tree mode only.
    public ILocator TreePerspective => app.Page.GetByRole(AriaRole.Combobox, new() { Name = "Tree perspective", Exact = true });

    public ILocator ExpandAllButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Expand all", Exact = true });

    public ILocator CollapseAllButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Collapse all", Exact = true });

    // The grid header's checkbox of every row in view.
    public ILocator SelectAllRowsCheckbox => app.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select all rows", Exact = true });

    // The bulk bar of the selected rows (the grid, Contributors and up).
    public AnnotationBulkBar BulkBar => new(app);

    // The filter's bottom sheet below 768 px.
    public AnnotationFilterSheet FilterSheet => new(app);

    // The empty state of a view without annotations: the heading's block with its text and the buttons "New
    // annotation" and "Bulk create" for Contributors and up (the navbar has buttons of the same names).
    public ILocator EmptyStateBlock => EmptyState.Locator("xpath=..");

    public ILocator EmptyStateButton(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return EmptyStateBlock.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    }

    // The chip of a filter term ("subject: <s>", "not type: exe or cnt"), found by its button "Remove <raw term>".
    public ILocator FilterChip(string rawTerm)
    {
        return RemoveFilterTermButton(rawTerm).Locator("xpath=..");
    }

    // The chip's remove button, "Remove <raw term>" (e.g. "Remove type:exe"); the phone sheet's terms have the same
    // names while the sheet is open.
    public ILocator RemoveFilterTermButton(string rawTerm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTerm);
        return app.Page.GetByRole(AriaRole.Button, new() { Name = $"Remove {rawTerm}", Exact = true });
    }

    // The first parse error of the filter, next to the chips ("Unknown type foo.", "type: needs a value.").
    public ILocator FilterNotice(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return app.Page.GetByText(message, new() { Exact = true });
    }

    // An example of the open "Filter syntax" popover ("key:exe.*.payments.*"); a click fills the filter with it.
    public ILocator FilterSyntaxExample(string example)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(example);
        return FilterSyntaxPopover.GetByRole(AriaRole.Button, new() { Name = example, Exact = true });
    }

    // The tab "Grid" or "Tree".
    public ILocator ListModeTab(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ListModeTabs.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
    }

    // The checkbox of a row, "Select <key>"; in the tree every copy of the key has one, and all follow one click.
    public ILocator RowCheckbox(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {key}", Exact = true });
    }

    // The header button that sorts by a column ("Sort by Key"): ascending, descending, then unsorted.
    public ILocator SortButton(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return app.Page.GetByRole(AriaRole.Button, new() { Name = $"Sort by {column}", Exact = true });
    }

    // The cell of a column ("Status", "Labels", "Title", …) in the row of a key in the grid, by the column's header,
    // which moves with the visible columns.
    public async Task<ILocator> GridCellAsync(string key, string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        var headers = await AnnotationGrid.GetByRole(AriaRole.Columnheader).AllInnerTextsAsync();
        var index = headers.Select(header => header.Trim()).ToList().IndexOf(column);
        if (index < 0)
        {
            throw new ArgumentException($"The grid has no column '{column}'; its columns are {string.Join(", ", headers.Select(header => $"'{header.Trim()}'"))}.", nameof(column));
        }

        return Item(key).GetByRole(AriaRole.Gridcell).Nth(index);
    }

    // Switches the tree's perspective ("By subject", "By responsibility").
    public async Task SelectTreePerspectiveAsync(string perspective)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(perspective);

        await TreePerspective.ClickAsync();
        await app.Page.GetByRole(AriaRole.Option, new() { Name = perspective, Exact = true }).ClickAsync();
        await Expect(TreePerspective).ToContainTextAsync(perspective);
    }

    // Opens the detail of a listed key (filter first) as a user does: the grid's Name cell, pinned left, because an
    // open overlay detail covers the right of the grid; the list item's button on a phone. Returns on its URL.
    public async Task OpenAnnotationFromListAsync(string key)
    {
        var item = Item(key).First;
        if (HasGrid)
        {
            // The pinned columns lead: select, Type, Name.
            await item.GetByRole(AriaRole.Gridcell).Nth(2).ClickAsync();
        }
        else
        {
            await item.GetByRole(AriaRole.Button).ClickAsync();
        }

        await app.ExpectAtAsync(routes.Annotation(key));
    }

    // "Select <key>" of a row, not "Select all rows".
    [GeneratedRegex(@"^Select [a-zA-Z]{3}\.")]
    private static partial Regex RowCheckboxName();
}
