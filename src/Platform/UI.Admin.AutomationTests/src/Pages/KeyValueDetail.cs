using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The detail of a key value (…/key-values/<key>, components/key-values/KeyValueDetail.vue in [key].vue): a region
// named by the key (section#key-value-detail) with the header (the key as h2, its "Copy to clipboard", the level's
// badge, "Deleted" for a deleted key, "Close the detail" from 768 px or "Back to the list" below) and the tab links
// "Value" and "Versions" (KeyValueVersions). The values store's list stays rendered beside it, also behind a full
// screen detail on a phone, so every locator is scoped to the region.
// The Value tab: the metadata (Level with "Change protection" for a server-side secret, Version "v<n>", Author,
// Updated, Created "<author>, <date>", Content type, and Binding with its "Binding source" select, the binding and its
// copy button), then the value:
//   - public: the textbox "Value" (a CodeMirror editor "Code editor" for application/json), edited in place; Save
//     (and Ctrl/Cmd+S in the textbox or the editor) sends the loaded version, so a concurrent change gets the alert
//     "Someone else changed this key" with Reload and Overwrite;
//   - a server-side secret (Standard, HSM, BYOK): "••••••••" until Reveal (60 seconds, then masked again), and
//     "Set new value" with "New level" and "New value";
//   - HYOK: the alert "Encrypted on the client" with a badge per recipient, no Reveal.
// The save row: "Unsaved changes" while dirty, "Cancel" of Set new value, "Save"; a save shows the toast "Saved as
// version <n>". Leaving the Value tab for Versions with unsaved changes asks "Discard changes?".
public sealed partial class KeyValueDetail
{
    public const string ConflictTitle = "Someone else changed this key";

    private readonly App app;
    private readonly AppRoutes routes;
    private readonly KeyValuesPage keyValues;

    public KeyValueDetail(App app, AppRoutes routes, string key)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        this.app = app;
        this.routes = routes;
        Key = key.ToLowerInvariant();
        keyValues = new KeyValuesPage(app, routes);
        Panel = new DetailPanel(app, "key-value-detail");
        Versions = new KeyValueVersions(app, Region);
    }

    // The key as the store and the route keep it, in lower case.
    public string Key { get; }

    public DetailPanel Panel { get; }

    // The Versions tab's content.
    public KeyValueVersions Versions { get; }

    public ILocator Region => keyValues.Detail(Key);

    public ILocator Heading => Region.GetByRole(AriaRole.Heading, new() { Name = Key, Exact = true, Level = 2 });

    // The first "Copy to clipboard" of the region, beside the heading: it copies the key.
    public ILocator KeyCopyButton => Region.GetByRole(AriaRole.Button, new() { Name = "Copy to clipboard", Exact = true }).First;

    public ILocator ValueTab => Region.GetByRole(AriaRole.Link, new() { Name = "Value", Exact = true });

    public ILocator VersionsTab => Region.GetByRole(AriaRole.Link, new() { Name = "Versions", Exact = true });

    // The level's badge of the metadata: "Public", "Standard", "HSM", "BYOK", "HYOK" or "Reference".
    public ILocator LevelBadge => keyValues.LevelBadge(Key);

    public ILocator Version => Term("Version");

    public ILocator Author => Term("Author");

    public ILocator Created => Term("Created");

    public ILocator ContentType => Term("Content type");

    public ILocator BindingSource => Region.GetByRole(AriaRole.Combobox, new() { Name = "Binding source", Exact = true });

    // The binding to copy: "@<key>", "@public(<key>)" or "@secret(<key>)" by the binding source.
    public ILocator Binding => Term("Binding").Locator("code");

    public ILocator BindingCopyButton => Term("Binding").GetByRole(AriaRole.Button, new() { Name = "Copy to clipboard", Exact = true });

    // The public text value; disabled for a deleted key and without the right to edit.
    public ILocator Value => keyValues.Value(Key);

    // The public value of application/json.
    public CodeEditor JsonEditor => new(app, "Code editor", Region);

    // The server-side secret's value: "••••••••" until Reveal.
    public ILocator SecretValue => keyValues.SecretValue(Key);

    public ILocator RevealButton => keyValues.RevealButton(Key);

    public ILocator HideButton => keyValues.HideButton(Key);

    public ILocator MaskedAgainHint => keyValues.MaskedAgainHint(Key);

    // The last "Copy to clipboard" of the region while a secret is revealed: it copies the value (and toasts "The
    // clipboard contains a secret").
    public ILocator SecretCopyButton => Region.GetByRole(AriaRole.Button, new() { Name = "Copy to clipboard", Exact = true }).Last;

    public ILocator SetNewValueButton => Region.GetByRole(AriaRole.Button, new() { Name = "Set new value", Exact = true });

    // The level of the new value: the levels the project allows, Public among them.
    public ILocator NewLevel => Region.GetByRole(AriaRole.Combobox, new() { Name = "New protection level", Exact = true });

    public ILocator NewValue => Region.GetByRole(AriaRole.Textbox, new() { Name = "New value", Exact = true });

    // A menu beside a server-side secret's badge with "Move to <level>" for the project's other server-side levels.
    public ILocator ChangeProtectionButton => Region.GetByRole(AriaRole.Button, new() { Name = "Change protection", Exact = true });

    public ILocator UnsavedChanges => Region.GetByText("Unsaved changes", new() { Exact = true });

    // "Cancel" of the save row, while Set new value is open.
    public ILocator CancelButton => Region.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    public ILocator SaveButton => Region.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });

    // The alert of a save that met a newer version (Nuxt UI's alert, the title's nearest data-slot="root"), with the
    // actions Reload and Overwrite.
    public ILocator ConflictAlert => Alert(ConflictTitle);

    public ILocator ConflictDescription => ConflictAlert.Locator("[data-slot='description']");

    public ILocator ReloadButton => ConflictAlert.GetByRole(AriaRole.Button, new() { Name = "Reload", Exact = true });

    public ILocator OverwriteButton => ConflictAlert.GetByRole(AriaRole.Button, new() { Name = "Overwrite", Exact = true });

    // The alert of a HYOK value instead of the value.
    public ILocator ClientEncryptedAlert => Alert("Encrypted on the client");

    public ILocator ClientEncryptedDescription => ClientEncryptedAlert.Locator("[data-slot='description']");

    // The header's badge of a deleted key (the list's toolbar has a button "Deleted" outside the region).
    public ILocator DeletedBadge => Region.GetByText("Deleted", new() { Exact = true });

    // The alert of a deleted key, "This key is deleted" ("Removed for good in <n> day(s), with every version. Restore
    // it to bring back the latest version."), with Restore; the key is read-only while deleted.
    public ILocator DeletedAlert => Alert("This key is deleted");

    public ILocator DeletedDescription => DeletedAlert.Locator("[data-slot='description']");

    public ILocator RestoreButton => DeletedAlert.GetByRole(AriaRole.Button, new() { Name = "Restore", Exact = true });

    // The compact error of a key the view does not have ("Not found", "The key '<key>' does not exist in view
    // '<view>'."), with Retry.
    public ILocator NotFoundAlert => Alert("Not found");

    public ILocator NotFoundDescription => NotFoundAlert.Locator("[data-slot='description']");

    public ILocator RetryButton => NotFoundAlert.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });

    // A term of the metadata ("Level", "Version", "Author", "Updated", "Created", "Content type", "Binding").
    public ILocator Term(string term)
    {
        return keyValues.DetailValue(Key, term);
    }

    // A text of the region (exact), e.g. a help under a field or a recipient's badge.
    public ILocator Text(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Region.GetByText(text, new() { Exact = true });
    }

    // Everything of the region that contains the text: a value the test checks is not shown.
    public ILocator Containing(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Region.GetByText(text);
    }

    // An item of the open Change protection menu: "Move to Standard", "Move to HSM" or "Move to BYOK".
    public ILocator MoveTo(string level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { Name = $"Move to {level}", Exact = true });
    }

    // The confirmation of a protection change, "Move '<key>' to <level>?", whose confirm button is named like the menu's
    // trigger, "Change protection".
    public ConfirmDialog MoveDialog(string level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        return new ConfirmDialog(app, $"Move '{Key}' to {level}?");
    }

    // Opens the detail by its URL (a page load) and waits for the heading.
    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.KeyValue(Key));
        await app.WaitForReadyAsync();
        await Expect(Heading).ToBeVisibleAsync();
    }

    public async Task OpenVersionsAsync()
    {
        await VersionsTab.ClickAsync();
        await app.ExpectAtAsync(routes.KeyValueVersions(Key));
    }

    // Back to the Value tab.
    public async Task OpenValueAsync()
    {
        await ValueTab.ClickAsync();
        await app.ExpectAtAsync(routes.KeyValue(Key));
    }

    // Leaves the detail with "Close the detail" (from 768 px) or "Back to the list" (below) and waits for the list.
    public async Task CloseAsync()
    {
        await Panel.CloseAsync();
        await app.ExpectAtAsync(routes.KeyValues);
    }

    // Picks the binding source ("default source", "public only" or "secret only").
    public async Task PickBindingSourceAsync(string source)
    {
        await keyValues.PickAsync(BindingSource, source);
    }

    // Clicks Save and waits for the toast "Saved as version <n>".
    public async Task SaveAsync(int expectedVersion)
    {
        await SaveButton.ClickAsync();
        await Expect(app.Toast($"Saved as version {expectedVersion}")).ToBeVisibleAsync();
    }

    // Ctrl/Cmd+S in the field (the public Value, New value or the editor's content) and waits for the toast "Saved as
    // version <n>".
    public async Task SaveWithKeyboardAsync(ILocator field, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(field);

        await field.PressAsync("ControlOrMeta+S");
        await Expect(app.Toast($"Saved as version {expectedVersion}")).ToBeVisibleAsync();
    }

    // Set new value of a secret: opens the fields, picks the level when given (its label), types the value and saves;
    // returns after the toast "Saved as version <n>".
    public async Task SetNewValueAsync(string value, int expectedVersion, string? level = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        await SetNewValueButton.ClickAsync();
        if (level is not null)
        {
            await keyValues.PickAsync(NewLevel, level);
        }

        await NewValue.FillAsync(value);
        await SaveAsync(expectedVersion);
    }

    // Reveals the secret value and returns its locator once it shows something else than the mask.
    public async Task<ILocator> RevealAsync()
    {
        return await keyValues.RevealAsync(Key);
    }

    // A Nuxt UI alert of the region by its title: the title's nearest data-slot="root".
    private ILocator Alert(string title)
    {
        return Region.GetByText(title, new() { Exact = true }).Locator("xpath=ancestor::*[@data-slot='root'][1]");
    }
}
