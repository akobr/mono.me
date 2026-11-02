using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Key-Values section's members of the values store: the header's actions, the rest of the New key dialog (its
// description, the content type, the field errors and Cancel) and, on the table (768 px and wider), the selection of
// rows with the "Bulk actions" toolbar. The dialog validates the key and the size of the value in the browser (Zod)
// and maps the API's refusals to its fields; an error replaces the field's help under it, marks the control
// aria-invalid and is its accessible description.
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
}
