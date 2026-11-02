using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Key-Values section's members of the values store: the header's actions, the rest of the New key dialog (its
// description, the content type, the field errors and Cancel), the toolbar (the search, the level filter, Active and
// Deleted, the counter), the empty states and, on the table (768 px and wider), the row menu and the selection of rows
// with the "Bulk actions" toolbar and its confirmations. The dialog validates the key and the size of the value in the
// browser (Zod) and maps the API's refusals to its fields; an error replaces the field's help under it, marks the
// control aria-invalid and is its accessible description.
public sealed partial class KeyValuesPage
{
    // The help of the Key field while it has no error.
    public const string NewKeyHelp = "Dot-separated, for example db.password.";

    // The columns of the list's table, in their order: the selection, Key, Level, Value, Version, Author, Updated (or
    // Expires in the Deleted state) and the row menu.
    private static readonly string[] ListColumns = ["select", "Key", "Level", "Value", "Version", "Author", "Updated", "actions"];

    // In the navbar; icon-only below 640 px, with the same names.
    public ILocator ExportButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Export", Exact = true });

    public ILocator ImportButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = true });

    // The select of the dialog's content type: "Text" (text/plain), "JSON" (application/json) or "Secret reference".
    public ILocator NewKeyContentType => NewKeyDialog.GetByRole(AriaRole.Combobox, new() { Name = "Content type", Exact = true });

    public ILocator NewKeyCancelButton => NewKeyDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // The "Bulk actions" toolbar above the table while rows are selected: "<n> selected", then "Delete" and "Change
    // protection" (Active) or "Restore" and "Purge" (Deleted), and "Clear selection".
    public ILocator BulkActions => app.Page.GetByRole(AriaRole.Toolbar, new() { Name = "Bulk actions", Exact = true });

    // The level filter of the toolbar: "All levels", "Public", "Standard", "HSM", "BYOK" and "HYOK"; the server lists
    // only the keys of the level, and the URL keeps its API name (?level=Standard, Hsm, Byok, Hyok).
    public ILocator LevelFilter => app.Page.GetByRole(AriaRole.Combobox, new() { Name = "Level filter", Exact = true });

    // The state buttons of the toolbar: Active (no ?state) and Deleted (?state=Deleted). A deleted key's detail has a
    // badge "Deleted" too, which is no button.
    public ILocator ActiveButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Active", Exact = true });

    public ILocator DeletedButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Deleted", Exact = true });

    // The counter of the toolbar, "<shown> of <loaded>": the keys the search leaves of the keys the server listed.
    public ILocator ListCounter => app.Page.GetByText(ListCounterText());

    // The checkbox in the table's header that selects every listed key.
    public ILocator SelectAll => app.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Select all", Exact = true });

    // The open dropdown menu (a row's menu or Export) and its items in their order.
    public ILocator OpenMenu => app.Page.GetByRole(AriaRole.Menu);

    public ILocator OpenMenuItems => OpenMenu.GetByRole(AriaRole.Menuitem);

    // The confirmation of Delete permanently (ConfirmDialog: type the key; its button "Delete permanently").
    public ConfirmDialog DeletePermanentlyDialog => new(app, "Delete permanently?");

    // The confirmation of the bulk bar's Purge (ConfirmDialog: type the view's name; its button "Purge").
    public ConfirmDialog PurgeDialog => new(app, "Purge the selected keys?");

    // Every option of the open select (the New key dialog's or the Import's levels).
    public ILocator SelectOptions => app.Page.GetByRole(AriaRole.Option);

    // An option of the open select or a level's item of an open menu, by its exact name.
    public ILocator SelectOption(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true });
    }

    // A text of the New key dialog, e.g. the description of the level under the Level select.
    public ILocator NewKeyText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return NewKeyDialog.GetByText(text, new() { Exact = true });
    }

    // The checkbox "Select <key>" of a row of the table (no selection below 768 px).
    public ILocator SelectKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {key.ToLowerInvariant()}", Exact = true });
    }

    // A button of the Bulk actions toolbar by its exact label: "Delete", "Change protection", "Restore", "Purge" or
    // "Clear selection".
    public ILocator BulkActionButton(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return BulkActions.GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    // A cell of the table's row of the key by its column (ListColumns): "Level" holds the level's badge, "Version"
    // v<n>.
    public ILocator ItemCell(string key, string column)
    {
        var index = Array.IndexOf(ListColumns, column);
        if (index < 0)
        {
            throw new ArgumentException($"'{column}' is not a column of the list; use one of {string.Join(", ", ListColumns)}.", nameof(column));
        }

        return Item(key).GetByRole(AriaRole.Cell).Nth(index);
    }

    // Opens New key and waits for the dialog, which starts empty: Public, Text.
    public async Task OpenNewKeyAsync()
    {
        await NewKeyButton.ClickAsync();
        await Expect(NewKeyDialog).ToBeVisibleAsync();
    }

    // Picks the option of a select of the page (its exact label) and waits until the select shows it.
    public async Task PickAsync(ILocator select, string option)
    {
        ArgumentNullException.ThrowIfNull(select);

        await select.ClickAsync();
        await SelectOption(option).ClickAsync();
        await Expect(select).ToHaveTextAsync(option);
    }

    // The field of the open New key dialog shows the error: the control is aria-invalid, the message is its
    // accessible description, and it is shown in the dialog.
    public async Task ExpectNewKeyErrorAsync(ILocator field, string message)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        await Expect(field).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(field).ToHaveAccessibleDescriptionAsync(message);
        await Expect(NewKeyText(message)).ToBeVisibleAsync();
    }

    // The same for a message the test knows only a part of (an API's message).
    public async Task ExpectNewKeyErrorAsync(ILocator field, Regex message)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(message);

        await Expect(field).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(field).ToHaveAccessibleDescriptionAsync(message);
    }

    // The field of the New key dialog has no error.
    public async Task ExpectNoNewKeyErrorAsync(ILocator field)
    {
        ArgumentNullException.ThrowIfNull(field);
        await Expect(field).ToHaveAttributeAsync("aria-invalid", "false");
    }

    // Creates a public key value of a content type ("Text", "JSON" or "Secret reference"); the dialog closes and the
    // key's detail opens.
    public async Task CreateWithContentTypeAsync(string key, string value, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        await OpenNewKeyAsync();
        await NewKeyName.FillAsync(key);
        await PickAsync(NewKeyContentType, contentType);
        await NewKeyValue.FillAsync(value);
        await CreateButton.ClickAsync();
        await Expect(NewKeyDialog).ToBeHiddenAsync();
        await app.ExpectAtAsync(routes.KeyValue(key));
    }

    // The heading (h2) of the list's empty state: "No key matches the search", "No deleted keys" or "No key values in
    // this view".
    public ILocator EmptyState(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return app.Page.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true, Level = 2 });
    }

    // The empty state around its heading: the icon, the heading, the description and the action ("New key" in an empty
    // Active list without a search); the header's "New key" is outside it.
    public ILocator EmptyStateBlock(string title)
    {
        return EmptyState(title).Locator("xpath=..");
    }

    // The menu button of a row of the table, "Actions for <key>" (no row menu below 768 px).
    public ILocator RowActionsButton(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return app.Page.GetByRole(AriaRole.Button, new() { Name = $"Actions for {key.ToLowerInvariant()}", Exact = true });
    }

    // An item of the open row menu by its exact label: "Open", "Copy key", "Copy binding", "Reveal" (only opens the
    // detail), "Delete" or "Delete permanently".
    public ILocator RowMenuItem(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { Name = label, Exact = true });
    }

    // The confirmation of a row's Delete (useConfirm), "Delete '<key>'?", whose button "Delete" is named like the menu
    // item.
    public ConfirmDialog DeleteDialog(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new ConfirmDialog(app, $"Delete '{key.ToLowerInvariant()}'?");
    }

    // The confirmation of the bulk bar's Delete (useConfirm), "Delete <n> key(s)?".
    public ConfirmDialog BulkDeleteDialog(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return new ConfirmDialog(app, $"Delete {count} key(s)?");
    }

    // The count of the Bulk actions toolbar, "<n> selected".
    public ILocator BulkSelected(int count)
    {
        return BulkActions.GetByText($"{count} selected", new() { Exact = true });
    }

    // Types into the search (keys and public values, filtered in the browser) and waits until the URL keeps it as its
    // exact ?q (an empty text drops q).
    public async Task SearchAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        await Search.FillAsync(text);
        await app.ExpectQueryAsync("q", text.Length == 0 ? null : text);
    }

    // Picks a level of the level filter by its label ("All levels", "Public", "Standard", "HSM", "BYOK", "HYOK") and
    // waits until the URL has its API name as ?level (none for All levels).
    public async Task FilterLevelAsync(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        await PickAsync(LevelFilter, label);
        await app.ExpectQueryAsync("level", label == "All levels" ? null : ApiLevelOf(label));
    }

    // Opens the detail of a listed key with a click on its Key cell (a click on the row could hit the Author's link) or
    // its button in the list below 768 px; the detail keeps the list's query.
    public async Task OpenItemAsync(string key)
    {
        if (HasTable)
        {
            await ItemCell(key, "Key").ClickAsync();
        }
        else
        {
            await Item(key).GetByRole(AriaRole.Button).ClickAsync();
        }

        await app.ExpectAtAsync(routes.KeyValue(key));
    }

    // Opens the row menu of a key and returns its items.
    public async Task<ILocator> OpenRowMenuAsync(string key)
    {
        await RowActionsButton(key).ClickAsync();
        await Expect(OpenMenu).ToBeVisibleAsync();
        return OpenMenuItems;
    }

    // Opens the row menu of a key and picks the item.
    public async Task RowActionAsync(string key, string label)
    {
        await OpenRowMenuAsync(key);
        await RowMenuItem(label).ClickAsync();
    }

    // Deletes a key through its row menu and the confirmation; returns after the toast "'<key>' deleted".
    public async Task DeleteFromRowMenuAsync(string key)
    {
        await RowActionAsync(key, "Delete");
        var dialog = DeleteDialog(key);
        await dialog.ExpectOpenAsync();
        await dialog.ConfirmAsync("Delete");
        await Expect(app.Toast($"'{key.ToLowerInvariant()}' deleted")).ToBeVisibleAsync();
    }

    // The API name of a level's label, as the URL and the API spell it.
    private static string ApiLevelOf(string label)
    {
        return label switch
        {
            "HSM" => "Hsm",
            "BYOK" => "Byok",
            "HYOK" => "Hyok",
            _ => label,
        };
    }

    [GeneratedRegex(@"^\s*\d+ of \d+\s*$")]
    private static partial Regex ListCounterText();
}
