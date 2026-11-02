using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The version history of a configuration, a template or a schema (components/versions/VersionsView.vue): the Versions
// tab of a configuration (…/configurations/<key>/versions) and of a schema or a template (?tab=versions). The
// addressing is in the query: ?v=<n> shows one version, ?v=<n>&from=<m> compares two; both replace the URL.
// - A toolbar: the selects "Compare" (placeholder "From") and "with" ("To"), the button "Compare" (enabled for two
//   different versions) and, while a version is shown, "Back to history".
// - The timeline, newest first: a row per version with "v<n>", the badges "current" and "expired", the author, the
//   time, "View" and "Compare with previous" (not on the oldest); "No versions." when empty.
// - One version: "Version v<n>" and a read-only editor named "Version <n>"; an expired one "This version is not
//   available".
// - A comparison: "v<m> → v<n>", the summary "+<a> −<d> · <k> change(s)", "Previous change", "Next change", the switch
//   "Collapse unchanged" and the tabs "Auto", "Split", "Unified" of "Diff layout"; when a document is gone the alert "A
//   version is no longer available" with the server's hunks.
// The key values have a simpler table of their own (KeyValuesPage).
public sealed partial class VersionsView
{
    // From this width (lg) a DiffEditor (a comparison of versions, the conflict dialog, Compare views) is side by side
    // with its labels on Auto; below it is unified, without them.
    public const int SplitDiffWidth = 1024;

    private readonly App app;

    // scope: the detail that shows the history (its region), the page by default.
    public VersionsView(App app, ILocator? scope = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        this.app = app;
        Scope = scope ?? app.Page.Locator(":root");
    }

    public ILocator Scope { get; }

    // The timeline (an ordered list of the versions).
    public ILocator Timeline => Scope.GetByRole(AriaRole.List).Filter(new() { Has = app.Page.GetByRole(AriaRole.Button, new() { Name = "View", Exact = true }) });

    public ILocator Rows => Timeline.GetByRole(AriaRole.Listitem);

    public ILocator NoVersions => Scope.GetByText("No versions.", new() { Exact = true });

    // The selects of the toolbar; the open select's options are named "v<n>".
    public ILocator FromSelect => Scope.GetByRole(AriaRole.Combobox, new() { Name = "Compare", Exact = true });

    public ILocator ToSelect => Scope.GetByRole(AriaRole.Combobox, new() { Name = "with", Exact = true });

    public ILocator CompareButton => Scope.GetByRole(AriaRole.Button, new() { Name = "Compare", Exact = true });

    public ILocator BackToHistoryButton => Scope.GetByRole(AriaRole.Button, new() { Name = "Back to history", Exact = true });

    // The summary of a comparison, e.g. "+1 −1 · 1 change".
    public ILocator DiffSummary => Scope.GetByText(DiffSummaryText());

    // The row of a version in the timeline, by its "v<n>" ("v1" is not "v10").
    public ILocator Row(int version)
    {
        return Rows.Filter(new() { Has = app.Page.GetByText(Label(version), new() { Exact = true }) });
    }

    // The badge "current" of a version's row.
    public ILocator CurrentBadge(int version)
    {
        return Row(version).GetByText("current", new() { Exact = true });
    }

    // The read-only editor of one shown version.
    public CodeEditor Version(int version)
    {
        return new CodeEditor(app, string.Create(CultureInfo.InvariantCulture, $"Version {version}"), Scope);
    }

    // The title of a comparison, "v<from> → v<to>".
    public ILocator CompareHeader(int from, int to)
    {
        return Scope.GetByText($"{Label(from)} → {Label(to)}", new() { Exact = true });
    }

    // Waits until the timeline lists the versions.
    public async Task WaitForTimelineAsync()
    {
        await Expect(Timeline.Or(NoVersions).First).ToBeVisibleAsync();
    }

    // "View" of the version's row: ?v=<n>, then "Version v<n>" with the version's read-only editor.
    public async Task<CodeEditor> ViewAsync(int version)
    {
        await Row(version).GetByRole(AriaRole.Button, new() { Name = "View", Exact = true }).ClickAsync();
        await app.ExpectQueryAsync("v", Number(version));
        await app.ExpectQueryAsync("from", null);
        var editor = Version(version);
        await Expect(editor.Content).ToBeVisibleAsync();
        return editor;
    }

    // "Compare with previous" of the version's row: ?v=<n>&from=<the version before it>, then the comparison's title.
    public async Task CompareWithPreviousAsync(int version)
    {
        await Row(version).GetByRole(AriaRole.Button, new() { Name = "Compare with previous", Exact = true }).ClickAsync();
        await app.ExpectQueryAsync("v", Number(version));
        await Expect(Scope.GetByText(CompareHeaderText())).ToBeVisibleAsync();
    }

    // "Back to history": the shown version or comparison closes, ?v and ?from are gone.
    public async Task BackToHistoryAsync()
    {
        await BackToHistoryButton.ClickAsync();
        await app.ExpectQueryAsync("v", null);
        await app.ExpectQueryAsync("from", null);
    }

    private static string Label(int version)
    {
        return string.Create(CultureInfo.InvariantCulture, $"v{version}");
    }

    private static string Number(int version)
    {
        return version.ToString(CultureInfo.InvariantCulture);
    }

    private async Task PickAsync(ILocator select, int version)
    {
        await select.ClickAsync();
        await app.Page.GetByRole(AriaRole.Option, new() { Name = Label(version), Exact = true }).ClickAsync();
        await Expect(select).ToContainTextAsync(Label(version));
    }

    // GetByText's regex sees an element's text with the spaces of the template around it.
    [GeneratedRegex(@"^\s*\+\d+ −\d+ · \d+ changes?\s*$")]
    private static partial Regex DiffSummaryText();

    [GeneratedRegex(@"^\s*v\d+ → v\d+\s*$")]
    private static partial Regex CompareHeaderText();
}
