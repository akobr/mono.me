using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The card "Recipients (HYOK)" of the Secrets section (settings#secrets; components/secrets/RecipientsCard.vue): the
// RSA public keys client-side values are encrypted for. "No recipients yet." or a table (Name, Id, Key "RSA <bits>
// bits", Added, State "enabled"/"disabled", and for Administrators "Disable"/"Enable" and "Remove"); the table scrolls
// sideways on a phone. "Add recipient" opens a form (Name*, Id (optional), Public key*, Cancel, Add; Add disabled until
// Name and Public key are filled) that shows a refused key as an error alert and keeps the input. Adding shows the
// toast "Recipient <id> added", removing asks "Remove the recipient <name>?" and shows "Recipient <id> removed";
// Disable and Enable show no toast, only a refusal ("The recipient was not changed"). Every change refetches the
// policy, which resets the switches of Allowed levels that were not saved.
public sealed partial class RecipientsCard(App app, AppRoutes routes)
{
    // The description of the toast "Recipient <id> added".
    public const string AddedDescription = "Existing HYOK values are not encrypted for it; re-encrypt them with the CLI (sform story kv reencrypt) using a current recipient's private key.";

    public const string RemoveDialogDescription = "Values already encrypted for it stay readable with its private key until they are encrypted again.";

    private readonly ProjectSettingsPage settings = new(app, routes);

    public ILocator Root => settings.RecipientsCard;

    public ILocator NoRecipients => Root.GetByText("No recipients yet.", new() { Exact = true });

    // The rows of the recipients (the rows with cells, not the header row).
    public ILocator Rows => Root.GetByRole(AriaRole.Row).Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell) });

    public ILocator AddRecipientButton => settings.AddRecipientButton;

    // The fields of the open add form: "Name*", "Id (optional)" (both with the placeholder "invoicing-api") and
    // "Public key*".
    public ILocator Name => settings.RecipientName;

    public ILocator Id => settings.RecipientId;

    public ILocator PublicKey => settings.RecipientPublicKey;

    public ILocator AddButton => settings.AddButton;

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // The error alert of the add form: the API's refusal of the input (and its hint).
    public ILocator FormError => Root.Locator("form [data-slot='description']");

    // The row of a recipient, by its exact id.
    public ILocator Row(string id)
    {
        return settings.Recipient(id);
    }

    // A cell of a recipient's row by its column: "Name", "Id", "Key", "Added" or "State".
    public ILocator Cell(string id, string column)
    {
        var index = column switch
        {
            "Name" => 0,
            "Id" => 1,
            "Key" => 2,
            "Added" => 3,
            "State" => 4,
            _ => throw new ArgumentException($"'{column}' is not a column of the recipients; use Name, Id, Key, Added or State.", nameof(column)),
        };
        return Row(id).GetByRole(AriaRole.Cell).Nth(index);
    }

    // A button of a recipient's row: "Disable", "Enable" or "Remove" (Administrators only).
    public ILocator RowButton(string id, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Row(id).GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    // The confirmation of Remove, titled by the recipient's name (its id when it has none); its confirm button is
    // "Remove" too, so it is clicked inside the dialog.
    public ConfirmDialog RemoveDialog(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ConfirmDialog(app, $"Remove the recipient {name}?");
    }

    // Opens the add form and waits for its fields.
    public async Task OpenFormAsync()
    {
        await AddRecipientButton.ClickAsync();
        await Expect(Name).ToBeVisibleAsync();
    }

    // Disables (or enables) a recipient with the button of its row and waits until its state shows it; no toast.
    public async Task SetEnabledAsync(string id, bool enabled)
    {
        await RowButton(id, enabled ? "Enable" : "Disable").ClickAsync();
        await Expect(Cell(id, "State")).ToHaveTextAsync(enabled ? "enabled" : "disabled");
        await Expect(RowButton(id, enabled ? "Disable" : "Enable")).ToBeVisibleAsync();
    }
}
