using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// A confirmation of the admin UI: a modal named by its title, with its description as the accessible description. It
// comes in two variants of the same shape:
//   - ConfirmDialog.vue, for destructive actions ("Delete <key>?", "Revoke <label>?", "Delete permanently?"): with a
//     typed confirmation, a textbox labelled "Type <text> to confirm" whose placeholder is the text to type, and the
//     confirm button (by default "Delete") disabled until the trimmed input equals it; Enter in the field confirms
//     then. The page closes the dialog once the action succeeded and leaves it open (with an error toast) when it
//     failed.
//   - ConfirmModal.vue, opened by useConfirm() ("Discard changes?", "Save anyway", "Rotate", "Remove", …): two buttons,
//     by default "Cancel" and "Confirm" (focused); any choice closes it, and Escape or a click outside count as Cancel.
// Both have Nuxt UI's "Close" button in the header. While a modal is open the rest of the page is aria-hidden, so the
// page's own locators find nothing until it closes; the toaster is found with IncludeHidden (App.Toast).
public sealed partial class ConfirmDialog
{
    public ConfirmDialog(App app, string title)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Root = app.Page.GetByRole(AriaRole.Dialog, new() { Name = title, Exact = true });
    }

    // title: for a title with a part the test does not know, e.g. the number of descendants.
    public ConfirmDialog(App app, Regex title)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(title);

        Root = app.Page.GetByRole(AriaRole.Dialog, new() { NameRegex = title });
    }

    public ILocator Root { get; }

    // The title, the modal's heading.
    public ILocator Heading => Root.GetByRole(AriaRole.Heading).First;

    // The description under the title (Nuxt UI's description slot); Expect(Root).ToHaveAccessibleDescriptionAsync
    // checks the same text.
    public ILocator Description => Root.Locator("[data-slot=\"description\"]");

    // The textbox of a typed confirmation ("Type <text> to confirm"), the only textbox of the dialog.
    public ILocator ConfirmationInput => Root.GetByRole(AriaRole.Textbox);

    public ILocator CancelButton => Button("Cancel");

    // Nuxt UI's close button in the header (the X).
    public ILocator CloseButton => Button("Close");

    // A button of the dialog by its exact label: the confirm button ("Delete", "Discard", "Turn off the mTLS rule"),
    // "Cancel" or another cancel label ("Keep editing"). Scoped to the dialog, so a page's button of the same name
    // ("Delete" of a save bar, "Save anyway" of an alert) does not count.
    public ILocator Button(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Root.GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    public async Task ExpectOpenAsync()
    {
        await Expect(Root).ToBeVisibleAsync();
    }

    // The text a typed confirmation asks for: the placeholder of its textbox (a key, a machine id, the start of a
    // thumbprint, a view's name, "delete").
    public async Task<string> ConfirmationTextAsync()
    {
        await Expect(ConfirmationInput).ToBeVisibleAsync();
        var text = await ConfirmationInput.GetAttributeAsync("placeholder");
        return string.IsNullOrEmpty(text)
            ? throw new InvalidOperationException("The typed confirmation of the dialog has no placeholder to read the text to type from.")
            : text;
    }

    // Types the text into the typed confirmation: by default the text it asks for (its placeholder).
    public async Task TypeConfirmationAsync(string? text = null)
    {
        await ConfirmationInput.FillAsync(text ?? await ConfirmationTextAsync());
    }

    // Clicks the confirm button (its exact label) once it is enabled and waits until the dialog closed: a ConfirmModal
    // closes at once, a ConfirmDialog once the page's action succeeded. A failed action leaves the dialog open, so this
    // then fails with the dialog still visible; the page's error toast tells why.
    public async Task ConfirmAsync(string label)
    {
        var button = Button(label);
        await Expect(button).ToBeEnabledAsync();
        await button.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    // Types the text the typed confirmation asks for, then confirms with the button (its exact label).
    public async Task TypeAndConfirmAsync(string label)
    {
        await TypeConfirmationAsync();
        await ConfirmAsync(label);
    }

    // Cancels with the cancel button (by default "Cancel"; "Keep editing" for Discard changes?) and waits until the
    // dialog closed; nothing is done.
    public async Task CancelAsync(string label = "Cancel")
    {
        await Button(label).ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
