using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Modeling's Deleted tab (…/modeling/deleted, pages/…/modeling/deleted.vue): the deleted annotations of the view, the
// roots of deletions by default, every deleted annotation with the switch "Show cascaded" (?cascaded=1). A table row per
// annotation with the checkbox "Select <key>", the button "Show <key>" (the read-only details) and the menu "Actions
// for <key>": "Show details", "Restore" and "Restore without descendants" (Contributor and up), "Delete permanently"
// (Administrator and up). A selection shows the toolbar "Bulk actions" with "<n> selected", "Restore", "Delete
// permanently" (Administrator and up) and "Clear selection". Nothing deleted: "Nothing is deleted". Below md the table
// hides its Title and Descendants columns; the checkbox and the menu stay.
public sealed partial class ModelingDeletedPage(App app, AppRoutes routes)
{
    public ILocator ShowCascaded => app.Page.GetByRole(AriaRole.Switch, new() { Name = "Show cascaded", Exact = true });

    public ILocator NothingDeleted => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Nothing is deleted", Exact = true });

    // The toolbar of a selection.
    public ILocator BulkActions => app.Page.GetByRole(AriaRole.Toolbar, new() { Name = "Bulk actions", Exact = true });

    public ILocator BulkRestoreButton => BulkActions.GetByRole(AriaRole.Button, new() { Name = "Restore", Exact = true });

    public ILocator BulkDeletePermanentlyButton => BulkActions.GetByRole(AriaRole.Button, new() { Name = "Delete permanently", Exact = true });

    // The items of the open row menu, in their order.
    public ILocator MenuItems => app.Page.GetByRole(AriaRole.Menuitem);

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

    // An item of the open row menu by its exact name.
    public ILocator MenuItem(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { Name = name, Exact = true });
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
}
