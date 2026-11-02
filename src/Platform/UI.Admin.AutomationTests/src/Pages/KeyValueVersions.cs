using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Versions tab of a key value's detail (…/key-values/<key>/versions, components/key-values/KeyValueVersions.vue),
// loaded only while it is open: a table with the columns Version ("v<n>", "v<n>(current)" for the current one),
// Level, Author, Created and Expires ("—" for the current version, the date of the expiry for older ones), newest
// first, and per row "View" (public) or "Reveal" (secret, ContributorWithSecrets and up) and "Compare" (with the
// version before, version 0 being the empty text). View opens the card "Version <n>" (close "Close the version"),
// Compare the card "Version <n-1> → <n>" (close "Close the comparison") with "No differences." or the changed lines,
// "+" added and "−" (U+2212) removed. A HYOK key has no buttons and a notice instead. Scope: the detail's region (the
// values store's list beside it has a table too).
public sealed partial class KeyValueVersions(App app, ILocator scope)
{
    // The columns of the table, in their order; the last one holds the buttons.
    private static readonly string[] Columns = ["Version", "Level", "Author", "Created", "Expires", "actions"];

    // The table of the versions, by its column "Created" (the list's table has none).
    public ILocator Table => scope.GetByRole(AriaRole.Table).Filter(new() { Has = app.Page.GetByRole(AriaRole.Columnheader, new() { Name = "Created", Exact = true }) });

    public ILocator Headers => Table.GetByRole(AriaRole.Columnheader);

    // The rows of the versions (the header's row has no cell).
    public ILocator Rows => Table.GetByRole(AriaRole.Row).Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell) });

    // The first cell of every row, newest first: "v<n>(current)", "v<n-1>", ….
    public ILocator VersionCells => Table.GetByRole(AriaRole.Cell, new() { NameRegex = VersionCellName() });

    public ILocator ClientEncryptedNotice => scope.GetByText("The values are encrypted on the client; the platform cannot show or compare them.", new() { Exact = true });

    // The buttons of every row, for a check that a HYOK key has none.
    public ILocator RowButtons => Rows.GetByRole(AriaRole.Button);

    public ILocator CloseVersionButton => scope.GetByRole(AriaRole.Button, new() { Name = "Close the version", Exact = true });

    public ILocator CloseComparisonButton => scope.GetByRole(AriaRole.Button, new() { Name = "Close the comparison", Exact = true });

    // The row of a version by its first cell, "v<n>" or "v<n>(current)".
    public ILocator Row(int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        return Rows.Filter(new() { Has = app.Page.GetByRole(AriaRole.Cell, new() { NameRegex = new Regex($@"^v{version}\s*(\(current\))?$") }) });
    }

    // A cell of a version's row: "Version", "Level" (the level's badge), "Author", "Created" or "Expires".
    public ILocator Cell(int version, string column)
    {
        var index = Array.IndexOf(Columns, column);
        if (index < 0)
        {
            throw new ArgumentException($"'{column}' is not a column of the versions; use one of {string.Join(", ", Columns)}.", nameof(column));
        }

        return Row(version).GetByRole(AriaRole.Cell).Nth(index);
    }

    // "View" of a public version, "Reveal" of a secret one.
    public ILocator ViewButton(int version)
    {
        return RowButton(version, "View");
    }

    public ILocator RevealButton(int version)
    {
        return RowButton(version, "Reveal");
    }

    public ILocator CompareButton(int version)
    {
        return RowButton(version, "Compare");
    }

    // The card of a viewed (or revealed) version; its value is in a pre.
    public ILocator VersionCard(int version)
    {
        return Card($"Version {version}");
    }

    public ILocator VersionValue(int version)
    {
        return VersionCard(version).Locator("pre");
    }

    // The card of a comparison, "Version <from> → <to>".
    public ILocator CompareCard(int from, int to)
    {
        return Card($"Version {from} → {to}");
    }

    // A line of a comparison's changes: its sign ('+' added, '−' removed, ' ' unchanged) and its content, after the old
    // and the new line number; the text of the line is the numbers, the sign and the content.
    public ILocator DiffLine(int from, int to, char sign, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return CompareCard(from, to).Locator("div").Filter(new()
        {
            HasTextRegex = new Regex($@"^\s*\d*\s*\d*\s*{Regex.Escape(sign.ToString())}\s*{Regex.Escape(content)}\s*$"),
        });
    }

    // Nuxt UI's card around a heading (h3) of the tab: the heading's nearest data-slot="root".
    private ILocator Card(string heading)
    {
        return scope
            .GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true, Level = 3 })
            .Locator("xpath=ancestor::*[@data-slot='root'][1]");
    }

    private ILocator RowButton(int version, string label)
    {
        return Row(version).GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    [GeneratedRegex(@"^v\d+\s*(\(current\))?$")]
    private static partial Regex VersionCellName();
}
