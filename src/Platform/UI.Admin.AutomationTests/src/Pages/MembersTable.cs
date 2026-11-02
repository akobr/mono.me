using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The members of an organization or a project (MembersTable.vue) in a section of a Members page: a plain table at
// every width (the page scrolls it sideways), with the columns Name, User and Role, sorted by name (or account id).
// A row's id is "member-<account id>"; its Name cell has the badge "you" for the signed-in account, the User cell the
// user name and the account id; the Role cell a select named "Role of <name or account id>" with the raw role names,
// disabled where the member rules (the caller's role, the last Owner, the own role) forbid a change; then the button
// "Remove <name or account id>". ?highlight=<account id> marks a row with aria-current="true". Below Administrator
// the API refuses the list and the section shows "No access" with "Retry" instead.
public sealed partial class MembersTable(App app, ILocator section)
{
    private const string RowIdPrefix = "member-";

    public ILocator Section => section;

    public ILocator Table => section.GetByRole(AriaRole.Table);

    // The members' rows, without the header.
    public ILocator Rows => Table.Locator("tbody").GetByRole(AriaRole.Row).Filter(new() { Has = app.Page.GetByRole(AriaRole.Combobox) });

    // The signed-in account's row, with the badge "you".
    public ILocator OwnRow => Rows.Filter(new() { Has = app.Page.GetByText("you", new() { Exact = true }) });

    // The role select of the own row; its name is "Role of <name>", or of the account id when the account has no name.
    public ILocator OwnRoleSelect => OwnRow.GetByRole(AriaRole.Combobox);

    public ILocator OwnRemoveButton => OwnRow.GetByRole(AriaRole.Button, new() { NameRegex = RemoveName() });

    // The Name cell of the own row: the avatar's initials, the name (or '–') and "you".
    public ILocator OwnNameCell => OwnRow.GetByRole(AriaRole.Cell).First;

    // The rows marked by ?highlight=.
    public ILocator HighlightedRows => Rows.And(app.Page.Locator("tr[aria-current='true']"));

    // The options of the open role select.
    public ILocator RoleOptions => app.Page.GetByRole(AriaRole.Option);

    public ILocator NoAccess => section.GetByRole(AriaRole.Heading, new() { Name = "No access", Exact = true });

    public ILocator RetryButton => section.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });

    public ILocator ColumnHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Table.GetByRole(AriaRole.Columnheader, new() { Name = name, Exact = true });
    }

    // The row of a member by the exact name the Name cell shows.
    public ILocator Row(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Rows.Filter(new() { Has = app.Page.GetByText(name, new() { Exact = true }) });
    }

    // "Role of <name>": the select shows the role and lists the raw role names it may change to.
    public ILocator RoleSelect(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Table.GetByRole(AriaRole.Combobox, new() { Name = $"Role of {name}", Exact = true });
    }

    public ILocator RemoveButton(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Table.GetByRole(AriaRole.Button, new() { Name = $"Remove {name}", Exact = true });
    }

    // The confirmation of Remove: "Remove <name>?", "They lose their <role> role on <key>.", Cancel and Remove.
    public ConfirmDialog RemoveDialog(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ConfirmDialog(app, $"Remove {name}?");
    }

    // An option of the open role select by its exact (raw) role name.
    public ILocator RoleOption(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = role, Exact = true });
    }

    // The account id of the own row, from the row's id ("member-<account id>").
    public async Task<string> OwnAccountIdAsync()
    {
        var id = await OwnRow.GetAttributeAsync("id") ?? string.Empty;
        return id.StartsWith(RowIdPrefix, StringComparison.Ordinal)
            ? id[RowIdPrefix.Length..]
            : throw new InvalidOperationException($"The own member row has the id '{id}', not '{RowIdPrefix}<account id>'.");
    }

    [GeneratedRegex("^Remove ")]
    private static partial Regex RemoveName();
}
