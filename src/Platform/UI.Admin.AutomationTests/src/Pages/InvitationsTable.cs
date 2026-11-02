using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The invitations of an organization or a project (InvitationsTable.vue) in a section of a Members page: a plain table
// with the columns Email, Role (a role badge), Status (Pending, Accepted, Declined, Revoked, Expired), Invited by,
// Expires (relative) and the actions, which only a Pending (or Expired) row has: "Copy link" (Pending only), "Resend"
// and "Revoke". The UI does not sort it: the mock lists in insertion order, the API newest first, so a test finds its
// rows by e-mail, never by index. Invitations cannot be deleted: a revoked one stays listed as Revoked. Below
// Administrator the section shows "No access" with "Retry".
public sealed partial class InvitationsTable(App app, ILocator section)
{
    // The columns, in their order.
    private static readonly string[] Columns = ["Email", "Role", "Status", "Invited by", "Expires"];

    public ILocator Section => section;

    public ILocator Table => section.GetByRole(AriaRole.Table);

    // The invitations' rows, without the header and the empty row.
    public ILocator Rows => Table.Locator("tbody").GetByRole(AriaRole.Row).Filter(new() { HasNot = app.Page.GetByText("No invitations.", new() { Exact = true }) });

    public ILocator NoInvitations => section.GetByText("No invitations.", new() { Exact = true });

    public ILocator NoAccess => section.GetByRole(AriaRole.Heading, new() { Name = "No access", Exact = true });

    public ILocator RetryButton => section.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });

    // Every row of the e-mail (exact, as the API stores it: lower case), e.g. a declined and an accepted one.
    public ILocator Row(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return Rows.Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell, new() { Name = email, Exact = true }) });
    }

    // The rows of the e-mail with the status.
    public ILocator Row(string email, string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        return Row(email).Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell, new() { Name = status, Exact = true }) });
    }

    // A cell of a row by its column: Email, Role, Status, Invited by or Expires.
    public ILocator Cell(ILocator row, string column)
    {
        ArgumentNullException.ThrowIfNull(row);

        var index = Array.IndexOf(Columns, column);
        return index >= 0
            ? row.GetByRole(AriaRole.Cell).Nth(index)
            : throw new ArgumentException($"'{column}' is not a column of the invitations: {string.Join(", ", Columns)}.", nameof(column));
    }

    // An action of a row: "Copy link", "Resend" or "Revoke".
    public ILocator Action(ILocator row, string action)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return row.GetByRole(AriaRole.Button, new() { Name = action, Exact = true });
    }

    // The confirmation of Revoke: "Revoke the invitation of <email>?", "The invitation link stops working.", Cancel and
    // Revoke.
    public ConfirmDialog RevokeDialog(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return new ConfirmDialog(app, $"Revoke the invitation of {email}?");
    }

    // Revokes every Pending invitation of the e-mail and waits until the last one shows Revoked; nothing when it has
    // none. For a cleanup: it waits for the table first, but not for the toasts.
    public async Task RevokePendingAsync(string email)
    {
        await Expect(Table.Or(NoAccess)).ToBeVisibleAsync();
        var pending = Row(email, "Pending");
        for (var count = await pending.CountAsync(); count > 0; count--)
        {
            await Action(pending.First, "Revoke").ClickAsync();
            await RevokeDialog(email).ConfirmAsync("Revoke");
            await Expect(pending).ToHaveCountAsync(count - 1);
        }
    }
}
