using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Invite people" (InviteDialog.vue), opened by the top bar's "Invite" of a Members page (Administrator and up; icon-only
// below 640 px with the same name): "To <key>. Everyone gets the same role and expiry.", the Emails tags input (its
// placeholder ada@example.com), Role (Reader by default; the raw role names, Owner only for an Owner of the access
// point) and "Expires after (days)" (7). A typed space, comma or semicolon, or Enter, commits the typed text as a tag
// (in lower case, without duplicates); a fill alone does not, but Send commits what is left in the input. A pasted list
// becomes tags at once. Send posts one invitation per address and toasts "<n> invitation(s) sent"; it closes the dialog
// when all succeeded, else it keeps the failed addresses as tags and lists them in an alert. Opening it again clears
// the tags, the error and the failures.
public sealed partial class InviteDialog(App app)
{
    public ILocator OpenButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Invite", Exact = true });

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "Invite people", Exact = true });

    public ILocator EmailsInput => Root.GetByPlaceholder("ada@example.com");

    // The tags of the Emails field.
    public ILocator Tags => Root.Locator("[data-slot='item']");

    // The error of the Emails field ("Not an email: …", "Enter at least one email address.").
    public ILocator EmailsError => Root.Locator("[data-slot='error']");

    public ILocator RoleSelect => Root.GetByRole(AriaRole.Combobox, new() { Name = "Role", Exact = true });

    // The options of the open Role select.
    public ILocator RoleOptions => app.Page.GetByRole(AriaRole.Option);

    public ILocator ExpiresField => Root.GetByLabel("Expires after (days)", new() { Exact = true });

    // The addresses that were not invited, each "<email>: <reason>".
    public ILocator Failures => Root.GetByRole(AriaRole.Alert);

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    public ILocator SendButton => Root.GetByRole(AriaRole.Button, new() { Name = "Send invitations", Exact = true });

    // The tag of an address, by its exact text.
    public ILocator Tag(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return Tags.Filter(new() { Has = app.Page.GetByText(email, new() { Exact = true }) });
    }

    // The text of a tag, which is struck through for an address that is not valid.
    public ILocator TagText(string email)
    {
        return Tag(email).Locator("[data-slot='itemText'] > span");
    }

    public async Task OpenAsync()
    {
        await OpenButton.ClickAsync();
        await Expect(Root).ToBeVisibleAsync();
    }

    // Types the address and commits it with Enter; returns once its tag (lower case) shows.
    public async Task AddEmailAsync(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        await EmailsInput.FillAsync(email);
        await EmailsInput.PressAsync("Enter");
        await Expect(Tag(email.ToLowerInvariant())).ToBeVisibleAsync();
    }

    // Pastes the text into the Emails field as a browser does: a paste event with the text as text/plain (a real paste
    // would need the clipboard's permissions).
    public async Task PasteAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        await EmailsInput.FocusAsync();
        await EmailsInput.EvaluateAsync(
            """
            (input, text) => {
                const data = new DataTransfer();
                data.setData('text/plain', text);
                input.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
            }
            """,
            text);
    }

    // Picks a role by its exact (raw) name and waits until the select shows it.
    public async Task SelectRoleAsync(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        await RoleSelect.ClickAsync();
        await app.Page.GetByRole(AriaRole.Option, new() { Name = role, Exact = true }).ClickAsync();
        await Expect(RoleSelect).ToHaveTextAsync(role);
    }

    // Clicks "Send invitations" near its top edge: a tag that Send commits can wrap the field and move the button down
    // under the pointer.
    public async Task SendAsync()
    {
        await Expect(SendButton).ToBeEnabledAsync();
        await SendButton.ClickAsync(new() { Position = new() { X = 8, Y = 3 } });
    }

    // Invites the addresses (Enter after each) with the dialog's role and expiry: opens the dialog, sends, and returns
    // once the toast "<n> invitation(s) sent" showed and the dialog closed.
    public async Task InviteAsync(params string[] emails)
    {
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentOutOfRangeException.ThrowIfZero(emails.Length);

        await OpenAsync();
        foreach (var email in emails)
        {
            await AddEmailAsync(email);
        }

        await SendAsync();
        await Expect(app.Toast(SentToast(emails.Length))).ToBeVisibleAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    // The toast of a send that invited count addresses.
    public static string SentToast(int count)
    {
        return count == 1 ? "1 invitation sent" : $"{count} invitations sent";
    }
}
