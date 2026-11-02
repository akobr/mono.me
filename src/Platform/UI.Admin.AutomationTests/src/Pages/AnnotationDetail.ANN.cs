using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Annotations section's members of the annotation detail: the header's status badge, the tab links Annotation and
// History with the History region, and the form's Time zone and save bar (Contributors and up: "Unsaved changes",
// Revert, and Save, whose name ends in its shortcut).
public sealed partial class AnnotationDetail
{
    // The status of the header, beside the Name: "Active", "Disabled", "Scheduled" or "Expired" (the form's switch
    // "Disabled" follows it, so the first match is the badge).
    public ILocator StatusBadge => Panel.Root.GetByText(StatusName()).First;

    // The form's time zone: the zone, or "Browser time zone (<zone>)" for an annotation without one (a key that Bulk
    // create or the API created as an ancestor).
    public ILocator TimeZoneButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Time zone", Exact = true });

    // The tab links of the detail, which keep the list's query.
    public ILocator AnnotationTabLink => Panel.Root.GetByRole(AriaRole.Link, new() { Name = "Annotation", Exact = true });

    public ILocator HistoryTabLink => Panel.Root.GetByRole(AriaRole.Link, new() { Name = "History", Exact = true });

    // The History tab's timeline, newest first: an entry per change ("Created", "Re-created" below the separator "New
    // lifetime", "Updated", "Deleted", "Restored", …) with "by <actor>". A deleted key's detail shows it too.
    public ILocator History => app.Page.GetByRole(AriaRole.Region, new() { Name = "History", Exact = true });

    // "Save" with its shortcut (⌘/Ctrl S) in the name.
    public ILocator SaveButton => Panel.Root.GetByRole(AriaRole.Button, new() { NameRegex = SaveName() });

    // Opens the History tab with its link and waits for the timeline.
    public async Task OpenHistoryTabAsync(string key)
    {
        await HistoryTabLink.ClickAsync();
        await app.ExpectAtAsync(routes.AnnotationHistory(key));
        await Expect(History).ToBeVisibleAsync();
    }

    // Saves the form's changes with Ctrl/Cmd+S once Save is enabled (a toast can cover the save bar), and returns once
    // the toast "Annotation saved" showed.
    public async Task SaveFormAsync()
    {
        await Expect(SaveButton).ToBeEnabledAsync();
        await Title.PressAsync("ControlOrMeta+s");
        await Expect(app.Toast("Annotation saved")).ToBeVisibleAsync();
        await Expect(SaveButton).ToBeDisabledAsync();
    }

    [GeneratedRegex("^(Active|Disabled|Scheduled|Expired)$")]
    private static partial Regex StatusName();

    [GeneratedRegex("^Save")]
    private static partial Regex SaveName();
}
