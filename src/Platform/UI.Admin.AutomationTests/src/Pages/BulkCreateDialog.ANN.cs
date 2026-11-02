using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Annotations section's members of "Bulk create annotations": the field error of Keys, the footer before the run
// (Cancel, "Remove invalid lines") and after it ("Create more"), the review by line number, the result's description and
// its created rows by the exact key, and the nested "Deleted annotations" of keys that were deleted.
public sealed partial class BulkCreateDialog
{
    // The error of the field Keys: "At most 1,000 keys at once; the list has 1,001.".
    public ILocator KeysError => Root.GetByText(KeysErrorText());

    public ILocator CancelButton => Root.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });

    // Only while the text has invalid lines; it removes them and focuses Keys again.
    public ILocator RemoveInvalidLinesButton => Root.GetByRole(AriaRole.Button, new() { Name = "Remove invalid lines", Exact = true });

    // After a run, beside Done: clears Keys and focuses it for the next list.
    public ILocator CreateMoreButton => Root.GetByRole(AriaRole.Button, new() { Name = "Create more", Exact = true });

    // The sentences under the result's title ("1 key already existed and was skipped. 1 repeated line was left out."),
    // in the alert of the result: the description of Nuxt UI's alert, beside its title.
    public ILocator ResultDescription => Result.Locator("xpath=..").Locator("[data-slot=\"description\"]");

    // The warning above the review while the catalog loads or failed: the review's statuses may be stale then.
    public ILocator CatalogNote => Root.GetByText(CatalogNoteText());

    // The table of the lines (Line, Key, Status): at most 200 rows, and the invalid ones beyond them.
    public ILocator Review => Root.GetByRole(AriaRole.Region, new() { Name = "Review", Exact = true });

    // The table of the created keys (Key, "Created as"); its header row is a row too.
    public ILocator CreatedAnnotations => Root.GetByRole(AriaRole.Region, new() { Name = "Created annotations", Exact = true });

    // The nested dialog of a list with deleted keys or ancestors, with Cancel, "Create new" and "Restore".
    public DeletedKeysDialog DeletedKeys => new(page);

    // The review row of a line by its number (the Line cell), whatever its text: lines can share a prefix, and an
    // invalid line's text (a tab, two dots) is hard to match. Its Key cell holds the key (and the typed text of a
    // compact line) or the invalid text, its Status cell the badge (New, Exists, Duplicate, Invalid) and an invalid
    // line's problem.
    public ILocator ReviewLine(int line)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);

        var number = line.ToString(CultureInfo.InvariantCulture);
        return Review.GetByRole(AriaRole.Row).Filter(new() { Has = page.GetByRole(AriaRole.Cell, new() { Name = number, Exact = true }) });
    }

    // The row of a created key by its exact key ("From the list" or "Missing ancestor"), not a row of a longer key.
    public ILocator CreatedKeyRow(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return CreatedAnnotations.GetByRole(AriaRole.Row).Filter(new() { Has = page.GetByText(key, new() { Exact = true }) });
    }

    // Clicks "Create N annotation(s)" for a list with deleted keys or ancestors and returns the nested "Deleted
    // annotations" once it shows, before anything is written.
    public async Task<DeletedKeysDialog> CreateOverDeletedAsync(int count)
    {
        await CreateButton(count).ClickAsync();
        var deleted = DeletedKeys;
        await Expect(deleted.Root).ToBeVisibleAsync();
        return deleted;
    }

    // Closes the dialog with Cancel before a run; nothing is written.
    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    [GeneratedRegex(@"^At most [\d,]+ keys at once; the list has [\d,]+\.$")]
    private static partial Regex KeysErrorText();

    [GeneratedRegex("^The catalog (is still loading|could not be loaded)")]
    private static partial Regex CatalogNoteText();
}
