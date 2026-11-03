using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The History tab of an annotation's detail (components/annotations/AnnotationHistory.vue), …/annotations/<key>/history,
// and the body of a deleted key's detail: the region "History" with a timeline, newest first. Each entry has its
// relative time, the action's badge ("Created", "Re-created" below the separator "New lifetime", "Updated", "Deleted",
// "Restored", "Configuration saved", "Configuration deleted"), "by <actor>" (the account's name, a machine or
// "System"), the cause ("for <key>" when it was created or restored as an ancestor of that key, "with <key>" when it
// went with that key's deletion), "<n> descendant(s)" and the change lines ("Title: – → "<title>"", "Description
// changed", "+label", "<key> added: <value>", …). Relative times change, so a test reads text fragments. A key without
// an audit trail shows "No history"; a read the API refused or did not answer in time shows an error state with Retry
// in the detail panel (section, AnnotationDetail.AnnotationsSection or GraphSection).
public sealed partial class AnnotationHistory(App app, string section = AnnotationDetail.AnnotationsSection)
{
    // How long a loaded timeline without the entry stays before its page is loaded again (WaitForEntryAsync): the tab
    // does not read the audit again by itself, so this is the pause between two reads.
    private const int ReloadPauseMs = 1_000;

    public DetailPanel Panel { get; } = new(app, $"{section}-detail");

    public ILocator Region => app.Page.GetByRole(AriaRole.Region, new() { Name = "History", Exact = true });

    // The empty state of a key without an audit trail (a lagging read of a deployed API can answer it for a new key).
    public ILocator NoHistory => Panel.Root.GetByRole(AriaRole.Heading, new() { Name = "No history", Exact = true });

    // The entries of the timeline, newest first (Nuxt UI's timeline items have no role, so their data-slot).
    public ILocator Entries => Region.Locator("[data-slot=\"item\"]");

    public ILocator NewestEntry => Entries.First;

    // The first entry of the annotation's life (its "Created", or the "Re-created" of its latest lifetime's start
    // below older entries).
    public ILocator OldestEntry => Entries.Last;

    // Retry of an error state in the detail: the annotation's, the deleted lookup's, the history's or the banner's.
    private ILocator Retry => ErrorStates.RetryButton(Panel.Root).First;

    // The action's badge of an entry: exactly "Created", "Updated", "Deleted", "Restored", …
    public static ILocator ActionBadge(ILocator entry, string action)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return entry.GetByText(action, new() { Exact = true });
    }

    // Waits until the history of the open tab (path, e.g. AppRoutes.AnnotationHistory(key)) loaded, up to
    // Target:ApiTimeoutMs (the tab reads the annotation, then its audit trail): the timeline or "No history". An error
    // state in the detail throws with its text.
    public async Task WaitForLoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await Expect(Region.Or(NoHistory).Or(Retry).First).ToBeVisibleAsync(new() { Timeout = app.ApiTimeoutMs });
        if (await Retry.IsVisibleAsync())
        {
            throw new PlaywrightException($"The history at {path} did not load: {await ErrorStates.TextAsync(ErrorStates.Around(Retry))}");
        }
    }

    // Waits until the timeline shows an entry of the action. The tab reads the audit trail once when it opens, and a
    // deployed API may record a change a moment after it answered, so the history's page (path, e.g.
    // AppRoutes.AnnotationHistory(key)) is loaded again, a second after a loaded timeline (or "No history") without the
    // entry, until the entry shows; the mock records at once. Each load is waited for (WaitForLoadAsync), never cut
    // short by a reload (that would cancel a slow read and start the catalog again), and timeoutMs, the time the audit
    // may lag, starts at the first loaded timeline. An error state in the detail throws with its text.
    public async Task WaitForEntryAsync(string path, string action, int timeoutMs = 30_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);

        var badge = ActionBadge(Region, action).First;
        DateTimeOffset? deadline = null;
        while (true)
        {
            await WaitForLoadAsync(path);
            deadline ??= DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
            try
            {
                await Expect(badge).ToBeVisibleAsync(new() { Timeout = ReloadPauseMs });
                return;
            }
            catch (PlaywrightException) when (DateTimeOffset.UtcNow < deadline)
            {
                await app.GotoAsync(path);
            }
        }
    }
}
