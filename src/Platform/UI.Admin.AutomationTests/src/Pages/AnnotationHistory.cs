using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The History tab of an annotation's detail (components/annotations/AnnotationHistory.vue), …/annotations/<key>/history,
// and the body of a deleted key's detail: the region "History" with a timeline, newest first. Each entry has its
// relative time, the action's badge ("Created", "Re-created" below the separator "New lifetime", "Updated", "Deleted",
// "Restored", "Configuration saved", "Configuration deleted"), "by <actor>" (the account's name, a machine or
// "System"), the cause ("for <key>" when it was created or restored as an ancestor of that key, "with <key>" when it
// went with that key's deletion), "<n> descendant(s)" and the change lines ("Title: – → "<title>"", "Description
// changed", "+label", "<key> added: <value>", …). Relative times change, so a test reads text fragments.
public sealed partial class AnnotationHistory(App app)
{
    public ILocator Region => app.Page.GetByRole(AriaRole.Region, new() { Name = "History", Exact = true });

    // The entries of the timeline, newest first (Nuxt UI's timeline items have no role, so their data-slot).
    public ILocator Entries => Region.Locator("[data-slot=\"item\"]");

    public ILocator NewestEntry => Entries.First;

    // The first entry of the annotation's life (its "Created", or the "Re-created" of its latest lifetime's start
    // below older entries).
    public ILocator OldestEntry => Entries.Last;

    // The action's badge of an entry: exactly "Created", "Updated", "Deleted", "Restored", …
    public static ILocator ActionBadge(ILocator entry, string action)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return entry.GetByText(action, new() { Exact = true });
    }
}
