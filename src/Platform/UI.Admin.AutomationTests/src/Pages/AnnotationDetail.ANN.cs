using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Annotations section's members of the annotation detail: the header's status badge, key, copy button,
// configuration link and stamps, the breadcrumb with Lineage, the tab links Annotation and History with the History
// region, the Relations block of the form (ancestors and the descendant groups), and the form's Time zone and save bar
// (Contributors and up: "Unsaved changes", Revert, and Save, whose name ends in its shortcut).
public sealed partial class AnnotationDetail
{
    // The status of the header, beside the Name: "Active", "Disabled", "Scheduled" or "Expired" (the form's switch
    // "Disabled" follows it, so the first match is the badge).
    public ILocator StatusBadge => Panel.Root.GetByText(StatusName()).First;

    // The header's badge of a deleted key, beside the Name (instead of the status).
    public ILocator DeletedBadge => Panel.Root.GetByText("Deleted", new() { Exact = true }).First;

    // The header's "Copy to clipboard" of the key under the Name; the form's identity line has no copy button.
    public ILocator HeaderKeyCopyButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Copy to clipboard", Exact = true });

    // The link to the annotation's configuration (…/configurations/<key>), drawn by where the configuration comes from:
    // data-origin "own" ("Configuration · v<n>"), "inherited" ("Inherited configuration"), "none" ("No configuration"),
    // or "unknown" ("Configuration") while the summaries load. Not on a deleted key.
    public ILocator ConfigurationOriginLink => Panel.Root.Locator("a[data-origin]");

    // The stamps under the key, a link to the History tab: "Created <relative time>" and, once the annotation changed,
    // "· Updated <relative time> by <actor>". Not on a deleted key.
    public ILocator StampsLink => Panel.Root.GetByTestId("annotation-stamps");

    // The chain of parents in the tree's perspective (?by=responsibility, else by subject), "<Type label> <last
    // segment>" each: the ancestors as links (which keep the query), the annotation itself the current item.
    public ILocator AncestorsBreadcrumb => Panel.Root.GetByRole(AriaRole.Navigation, new() { Name = "Annotation ancestors", Exact = true });

    // The breadcrumb's last item, the annotation itself (aria-current="page", not a link).
    public ILocator AncestorsBreadcrumbCurrent => AncestorsBreadcrumb.Locator("[aria-current=\"page\"]");

    // "Lineage" beside the breadcrumb, disabled for a key without ancestors (rst, sbt); it opens AncestorLineage.
    public ILocator AncestorLineageButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Lineage", Exact = true });

    // The popover of "Lineage": "Ancestors, applied top to bottom" and a numbered level per line, the farthest
    // ancestors first (each level in merge order, the keys as links), the annotation itself last.
    public ILocator AncestorLineage => app.Page.GetByRole(AriaRole.Dialog).Filter(new() { Has = app.Page.GetByText("Ancestors, applied top to bottom", new() { Exact = true }) });

    public ILocator AncestorLineageLevels => AncestorLineage.GetByRole(AriaRole.Listitem);

    // The form's Relations: "Ancestors" (a row per ancestor of the record: its field and a link to its key) and
    // "Descendants" (a group per descendant type, "Counting descendants…" while the catalog loads).
    public ILocator RelationsBlock => Panel.Root.GetByTestId("relations");

    // The descendant groups of Relations, in the type order of the key (e.g. Usage, Context, Execution, Unit of
    // execution for a subject): the type, its count, up to 5 key links and "Show all <n>" beyond.
    public ILocator DescendantGroups => RelationsBlock.GetByTestId("descendant-group");

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

    // The row of an ancestor in Relations, by its key: its field ("Responsibility", "Subject", "Unit", "Usage",
    // "Context", "Execution") and the link.
    public ILocator RelationsAncestorRow(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Has is queried inside each div, so the link's locator starts at the page.
        return RelationsBlock.Locator("div").Filter(new() { Has = app.Page.GetByRole(AriaRole.Link, new() { Name = key, Exact = true }) });
    }

    // The link of a key in Relations (an ancestor, or a descendant's preview).
    public ILocator RelationsLink(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return RelationsBlock.GetByRole(AriaRole.Link, new() { Name = key, Exact = true });
    }

    // The descendant group of a type by its exact label ("Execution", not "Unit of execution").
    public ILocator DescendantGroup(string typeLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeLabel);
        return DescendantGroups.Filter(new() { Has = app.Page.GetByText(typeLabel, new() { Exact = true }) });
    }

    // Opens the History tab with its link and waits for the timeline.
    public async Task OpenHistoryTabAsync(string key)
    {
        await HistoryTabLink.ClickAsync();
        await app.ExpectAtAsync(routes.AnnotationHistory(key));
        await Expect(History).ToBeVisibleAsync();
    }

    // Opens the detail by its URL and waits until it shows the annotation's form (its identity line): a fresh page
    // load, after which the catalog loads again.
    public async Task OpenFormAsync(string key)
    {
        await OpenAsync(key);
        await Expect(Identity).ToHaveTextAsync(key);
    }

    // Opens Lineage and returns its popover once it shows.
    public async Task<ILocator> OpenAncestorLineageAsync()
    {
        await AncestorLineageButton.ClickAsync();
        await Expect(AncestorLineage).ToBeVisibleAsync();
        return AncestorLineage;
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
