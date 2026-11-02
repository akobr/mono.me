using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Modeling's Deleted tab (…/modeling/deleted, pages/…/modeling/deleted.vue): the deleted annotations of the view, the
// roots of deletions by default, every deleted annotation with the switch "Show cascaded" (?cascaded=1), which adds the
// column "Root" (the badge "root" on a root, else the root's key). A table row per annotation with the checkbox
// "Select <key>", the button "Show <key>" (the read-only details) and the menu "Actions for <key>": "Show details",
// "Restore" and "Restore without descendants" (Contributor and up), "Delete permanently" (Administrator and up). A
// selection shows the toolbar "Bulk actions" with "<n> selected", "Restore", "Delete permanently" (Administrator and
// up) and "Clear selection". Nothing deleted: "Nothing is deleted". Below md the table hides its Title and Descendants
// columns; the checkbox and the menu stay.
// The details are a slideover named by the key ("The deleted annotation, read-only, and its history."): the pre
// "Annotation document" and the history with the banner "<key> is deleted". Delete permanently asks "Delete <key>
// permanently?" (typed: the key) or "Delete <n> annotations permanently?" (typed: the count).
public sealed partial class ModelingDeletedPage(App app, AppRoutes routes)
{
    // The description of the details.
    public const string DetailsDescriptionText = "The deleted annotation, read-only, and its history.";

    // The confirm button of Delete permanently (also the name of the menu item and of the toolbar's button).
    public const string DeletePermanentlyLabel = "Delete permanently";

    public ILocator ShowCascaded => app.Page.GetByRole(AriaRole.Switch, new() { Name = "Show cascaded", Exact = true });

    // The column of the roots, there with Show cascaded.
    public ILocator RootColumn => app.Page.GetByRole(AriaRole.Columnheader, new() { Name = "Root", Exact = true });

    // The toolbar of a selection.
    public ILocator BulkActions => app.Page.GetByRole(AriaRole.Toolbar, new() { Name = "Bulk actions", Exact = true });

    public ILocator BulkRestoreButton => BulkActions.GetByRole(AriaRole.Button, new() { Name = "Restore", Exact = true });

    public ILocator BulkDeletePermanentlyButton => BulkActions.GetByRole(AriaRole.Button, new() { Name = DeletePermanentlyLabel, Exact = true });

    public ILocator ClearSelectionButton => BulkActions.GetByRole(AriaRole.Button, new() { Name = "Clear selection", Exact = true });

    // The items of the open row menu, in their order.
    public ILocator MenuItems => app.Page.GetByRole(AriaRole.Menuitem);

    // "<n> selected" of the toolbar.
    public ILocator SelectedCount(int count)
    {
        return BulkActions.GetByText(string.Create(CultureInfo.InvariantCulture, $"{count} selected"), new() { Exact = true });
    }

    // The row of a deleted annotation, by the exact key of its "Show <key>" button.
    public ILocator Row(string key)
    {
        return app.Page.GetByRole(AriaRole.Row).Filter(new() { Has = ShowButton(key) });
    }

    public ILocator ShowButton(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Button, new() { Name = $"Show {key}", Exact = true });
    }

    public ILocator ActionsButton(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Button, new() { Name = $"Actions for {key}", Exact = true });
    }

    public ILocator SelectCheckbox(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {key}", Exact = true });
    }

    // The badge "root" in the Root column of a root of a deletion (Show cascaded).
    public ILocator RootBadge(string key)
    {
        return Row(key).GetByText("root", new() { Exact = true });
    }

    // An item of the open row menu by its exact name ("Restore" is a prefix of "Restore without descendants").
    public ILocator MenuItem(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { Name = name, Exact = true });
    }

    // The details of a deleted annotation: the slideover named by its key.
    public ILocator Details(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Dialog, new() { Name = key, Exact = true });
    }

    // The annotation as JSON in the details.
    public ILocator AnnotationDocument(string key)
    {
        return Details(key).GetByLabel("Annotation document", new() { Exact = true });
    }

    // The banner of a deleted annotation (DeletedAnnotationBanner.vue), "<key> is deleted": in the details' history, and
    // in the annotation's detail while it is deleted.
    public ILocator DeletedAnnotationBanner(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByText($"{key} is deleted", new() { Exact = true });
    }

    // The confirmation of Delete permanently of one annotation, "Delete <key> permanently?" (typed: the key).
    public ConfirmDialog PurgeDialog(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new ConfirmDialog(app, $"Delete {key} permanently?");
    }

    // The confirmation of Delete permanently of a selection, "Delete <n> annotations permanently?" (typed: the count).
    public ConfirmDialog PurgeDialog(int count)
    {
        return new ConfirmDialog(app, string.Create(CultureInfo.InvariantCulture, $"Delete {count} annotations permanently?"));
    }

    // Opens the tab by its URL and waits for its switch.
    public async Task OpenAsync(bool cascaded = false)
    {
        await app.GotoAsync(routes.Deleted(cascaded));
        await app.WaitForReadyAsync();
        await Expect(ShowCascaded).ToBeVisibleAsync();
    }

    // Opens the menu "Actions for <key>" and waits for its first item.
    public async Task OpenActionsAsync(string key)
    {
        await ActionsButton(key).ClickAsync();
        await Expect(MenuItem("Show details")).ToBeVisibleAsync();
    }

    // Picks an item of the menu "Actions for <key>" ("Restore", "Restore without descendants", "Delete permanently").
    public async Task RowActionAsync(string key, string item)
    {
        await OpenActionsAsync(key);
        await MenuItem(item).ClickAsync();
    }

    // Turns "Show cascaded" on or off and waits for the URL (?cascaded=1 or none) and for the list of that state: the
    // column Root shows only with the cascaded list, which replaces the table once it loaded.
    public async Task SetShowCascadedAsync(bool cascaded)
    {
        await ShowCascaded.SetCheckedAsync(cascaded);
        await app.ExpectQueryAsync("cascaded", cascaded ? "1" : null);
        if (cascaded)
        {
            await Expect(RootColumn).ToBeVisibleAsync();
        }
        else
        {
            await Expect(RootColumn).ToHaveCountAsync(0);
        }
    }

    // The cell of an annotation's row in the column of that header ("Root", "Descendants", "By", …), by the header's
    // place among the visible ones: Title and Descendants are hidden below md, Root shows only with Show cascaded.
    public async Task<ILocator> CellAsync(string key, string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        var header = app.Page.GetByRole(AriaRole.Columnheader, new() { Name = column, Exact = true });
        await Expect(header).ToBeVisibleAsync();
        var headers = await app.Page.GetByRole(AriaRole.Columnheader).AllInnerTextsAsync();
        var index = headers.Select(text => text.Trim()).ToList().IndexOf(column);
        return index < 0
            ? throw new InvalidOperationException($"The deleted annotations have no column '{column}': {string.Join(", ", headers)}.")
            : Row(key).GetByRole(AriaRole.Cell).Nth(index);
    }
}
