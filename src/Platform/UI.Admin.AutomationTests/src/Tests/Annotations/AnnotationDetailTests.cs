using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

// Detail: the annotation detail over the list, its keyboard, its header's actions and links, the breadcrumb, Lineage
// and Relations, the History tab, and the old tab URLs that now open the configuration's tabs.
[Area(Area.Annotations)]
public sealed partial class AnnotationDetailTests : PlatformTest
{
    // ANN-DET-01: Open an annotation from the list and close it back to the filtered list.
    // A row of the filtered list opens the detail with the list's query kept: an overlay on a wide screen, full screen
    // on a phone, named by the annotation's Name. The header shows a new subject as active, with its key, More actions,
    // the tabs (Annotation current) and only its creation; the form shows the identity instead of a Name field. Closing
    // returns to the same filtered list.
    [Test]
    [TestCase(152)]
    [UserRole(Role.Editor)]
    public async Task AnnotationDetail_OpenFromList_ClosesBackToTheFilter()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        var name = key.Split('.')[1];
        var filter = $"key:{key}";
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);
        await annotations.WaitForCatalogAsync();
        await annotations.FilterAsync(filter);

        await annotations.OpenAnnotationFromListAsync(key);
        await App.ExpectQueryAsync("q", filter);
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        await detail.Panel.ExpectModeAsync(Page.ViewportSize is { Width: < DetailPanel.OverlayWidth } ? DetailPanel.FullMode : DetailPanel.OverlayMode);

        await Expect(detail.Heading).ToHaveTextAsync(name);
        await Expect(detail.StatusBadge).ToHaveTextAsync("Active");
        await Expect(detail.Panel.Root.GetByTitle(key, new() { Exact = true }).First).ToHaveTextAsync(key);
        await Expect(detail.MoreActionsButton).ToBeVisibleAsync();
        await Expect(detail.AnnotationTabLink).ToHaveAttributeAsync("aria-current", "page");
        await Expect(detail.HistoryTabLink).ToBeVisibleAsync();
        await Expect(detail.HistoryTabLink).Not.ToHaveAttributeAsync("aria-current", "page");
        await Expect(detail.StampsLink).ToHaveTextAsync(CreatedOnly());

        await Expect(detail.Identity).ToHaveTextAsync(key);
        await Expect(detail.Identity.GetByRole(AriaRole.Img, new() { Name = "Subject", Exact = true })).ToBeVisibleAsync();
        await Expect(detail.Panel.Root.GetByRole(AriaRole.Textbox, new() { Name = "Name", Exact = true })).ToHaveCountAsync(0);
        await Expect(detail.Title).ToHaveValueAsync(string.Empty);

        await detail.CloseAsync();
        await App.ExpectQueryAsync("q", filter);
        await Expect(detail.Panel.Root).ToHaveCountAsync(0);
        await Expect(annotations.Filter).ToHaveValueAsync(filter);
        await Expect(annotations.Item(key)).ToBeVisibleAsync();
    }

    // ANN-DET-02: Keyboard: J and K move, Enter opens, Escape closes, / focuses the filter.
    // On the grid sorted by key, J and K move the keyboard cursor over the rows, which a live region announces as
    // "<Name>, <key>"; Enter opens the cursor's row with the list's query, Escape on the page closes it again, and /
    // puts the focus into the filter. Keys typed into a control do not move the cursor, so the focus leaves it first.
    [Test]
    [TestCase(154)]
    [UserRole(Role.Editor)]
    [DesktopOnly]
    public async Task AnnotationDetail_Keyboard_MovesOpensAndCloses()
    {
        var prefix = PlatformData.AnnotationKeySetPrefix(PlatformData.Subject);
        var keys = PlatformData.AnnotationKeySet(prefix, 3);
        var filter = $"key:{prefix}";
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync([.. keys]);
        await annotations.WaitForCatalogAsync();
        await annotations.FilterAsync(filter);

        await annotations.SortButton("Key").ClickAsync();
        await Expect(annotations.AnnotationRows).ToHaveCountAsync(keys.Count);
        for (var index = 0; index < keys.Count; index++)
        {
            await Expect(annotations.AnnotationRows.Nth(index).GetByRole(AriaRole.Checkbox)).ToHaveAccessibleNameAsync($"Select {keys[index]}");
        }

        await BlurAsync();
        await Page.Keyboard.PressAsync("j");
        await Expect(annotations.ActiveRowAnnouncement).ToHaveTextAsync(Announcement(keys[0]));
        await Page.Keyboard.PressAsync("j");
        await Expect(annotations.ActiveRowAnnouncement).ToHaveTextAsync(Announcement(keys[1]));
        await Page.Keyboard.PressAsync("k");
        await Expect(annotations.ActiveRowAnnouncement).ToHaveTextAsync(Announcement(keys[0]));

        await Page.Keyboard.PressAsync("Enter");
        await App.ExpectAtAsync(Routes.Annotation(keys[0]));
        await App.ExpectQueryAsync("q", filter);
        await Expect(detail.Identity).ToHaveTextAsync(keys[0]);

        await BlurAsync();
        await Page.Keyboard.PressAsync("Escape");
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", filter);
        await Expect(detail.Panel.Root).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("/");
        await Expect(annotations.Filter).ToBeFocusedAsync();
    }

    // ANN-DET-03: More actions copy the link and the key, and the header links lead to the graph and the configuration.
    // More actions offers Show in graph, Copy link, Copy key and Delete. Copy link copies the detail's absolute URL with a
    // toast, Copy key and the key's copy button copy the key without one. A new subject has no configuration of its own,
    // so the configuration link reads "No configuration" (or "Inherited configuration" where type templates apply) and
    // opens the configuration; Show in graph opens the graph focused on the key.
    [Test]
    [TestCase(176)]
    [UserRole(Role.Editor)]
    public async Task AnnotationDetail_MoreActions_CopyAndShowInGraph()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        await InstallClipboardRecorderAsync();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);
        await detail.OpenFormAsync(key);

        await detail.MoreActionsButton.ClickAsync();
        foreach (var item in new[] { "Show in graph", "Copy link", "Copy key", "Delete" })
        {
            await Expect(detail.MenuItem(item)).ToBeVisibleAsync();
        }

        var origin = await Page.EvaluateAsync<string>("() => location.origin");
        await ClearCopiedAsync();
        await detail.MenuItem("Copy link").ClickAsync();
        await Expect(App.Toast("Link copied")).ToBeVisibleAsync();
        await WaitForCopiedTextAsync($"{origin}/{Routes.Annotation(key)}");

        await ClearCopiedAsync();
        await detail.MoreActionsAsync("Copy key");
        await WaitForCopiedTextAsync(key);
        await Expect(App.Notifications.Locator("li").Filter(new() { HasNot = Page.GetByText("Link copied", new() { Exact = true }) })).ToHaveCountAsync(0);

        await ClearCopiedAsync();
        await detail.HeaderKeyCopyButton.ClickAsync();
        await WaitForCopiedTextAsync(key);

        await Expect(detail.ConfigurationOriginLink).ToHaveAttributeAsync("data-origin", NoneOrInherited());
        var configurationOrigin = await detail.ConfigurationOriginLink.GetAttributeAsync("data-origin");
        await Expect(detail.ConfigurationOriginLink).ToHaveTextAsync(configurationOrigin == "none" ? "No configuration" : "Inherited configuration");

        await detail.ConfigurationOriginLink.ClickAsync();
        await App.ExpectAtAsync(Routes.Configuration(key));

        await Page.GoBackAsync();
        await App.ExpectAtAsync(Routes.Annotation(key));
        await Expect(detail.Identity).ToHaveTextAsync(key);
        await detail.MoreActionsAsync("Show in graph");
        await App.ExpectAtAsync(Routes.GraphNode(key));
        await App.ExpectQueryAsync("focus", key);
    }

    // ANN-DET-04: Breadcrumb, Lineage and Relations lead through ancestors and descendants.
    // A unit of execution's breadcrumb follows the tree's perspective (by subject, or by responsibility), Lineage lists
    // its six ancestors by level in merge order, and Relations links every ancestor of the record. Its subject, reached
    // through Relations, has no Lineage and counts its descendants by type (the Execution group previews 5 of its 6), and
    // "Show all 6" lists exactly them.
    [Test]
    [TestCase(177)]
    [UserRole(Role.Editor)]
    public async Task AnnotationDetail_Lineage_LinksAncestorsAndDescendants()
    {
        var unitOfExecution = PlatformData.AnnotationKey(PlatformData.UnitOfExecution);
        var parts = unitOfExecution.Split('.');
        var (s, r, c1, u) = (parts[1], parts[2], parts[3], parts[4]);
        var subject = PlatformData.AncestorKey(unitOfExecution, PlatformData.Subject);
        var responsibility = PlatformData.AncestorKey(unitOfExecution, PlatformData.Responsibility);
        var unit = PlatformData.AncestorKey(unitOfExecution, PlatformData.Unit);
        var usage = PlatformData.AncestorKey(unitOfExecution, PlatformData.Usage);
        var context = PlatformData.AncestorKey(unitOfExecution, PlatformData.Context);
        var execution = PlatformData.AncestorKey(unitOfExecution, PlatformData.Execution);
        var executions = Enumerable.Range(2, 5).Select(_ => PlatformData.AnnotationKey(PlatformData.Execution, s, r)).ToArray();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync([unitOfExecution, .. executions]);
        await annotations.WaitForCatalogAsync();

        await detail.OpenFormAsync(unitOfExecution);
        await Expect(detail.AncestorsBreadcrumb.GetByRole(AriaRole.Link)).ToHaveTextAsync([$"Subject {s}", $"Context {c1}", $"Execution {c1}"]);
        await Expect(detail.AncestorsBreadcrumbCurrent).ToHaveTextAsync($"Unit of execution {u}");

        await App.GotoAsync(AppRoutes.WithQuery(Routes.Annotation(unitOfExecution), ("by", "responsibility")));
        await Expect(detail.Identity).ToHaveTextAsync(unitOfExecution);
        await Expect(detail.AncestorsBreadcrumb.GetByRole(AriaRole.Link)).ToHaveTextAsync([$"Responsibility {r}", $"Usage {r}", $"Execution {c1}"]);
        await Expect(detail.AncestorsBreadcrumbCurrent).ToHaveTextAsync($"Unit of execution {u}");

        await detail.OpenAncestorLineageAsync();
        string[][] levels = [[subject], [responsibility, usage, context], [unit, execution]];
        await Expect(detail.AncestorLineageLevels).ToHaveCountAsync(levels.Length + 1);
        for (var level = 0; level < levels.Length; level++)
        {
            var line = detail.AncestorLineageLevels.Nth(level);
            await Expect(line).ToHaveTextAsync(new Regex($"^{level + 1}"));
            await Expect(line.GetByRole(AriaRole.Link)).ToHaveTextAsync(levels[level]);
        }

        var own = detail.AncestorLineageLevels.Nth(levels.Length);
        await Expect(own).ToHaveTextAsync(new Regex($@"^{levels.Length + 1}\s*{Regex.Escape(unitOfExecution)}$"));
        await Expect(own.GetByRole(AriaRole.Link)).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(detail.AncestorLineage).ToBeHiddenAsync();

        (string Field, string Key)[] ancestors =
        [
            ("Responsibility", responsibility), ("Subject", subject), ("Unit", unit), ("Usage", usage), ("Context", context), ("Execution", execution),
        ];
        await Expect(detail.RelationsBlock.GetByRole(AriaRole.Link)).ToHaveTextAsync([.. ancestors.Select(ancestor => ancestor.Key)]);
        foreach (var (field, key) in ancestors)
        {
            await Expect(detail.RelationsAncestorRow(key)).ToHaveTextAsync(new Regex($@"^{field}\s*{Regex.Escape(key)}$"));
        }

        await Expect(detail.RelationsBlock.GetByRole(AriaRole.Heading, new() { Name = "Descendants", Exact = true })).ToHaveCountAsync(0);

        await detail.RelationsLink(subject).ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(subject));
        await App.ExpectQueryAsync("by", "responsibility");
        await Expect(detail.Identity).ToHaveTextAsync(subject);
        await Expect(detail.AncestorLineageButton).ToBeDisabledAsync();

        (string Label, int Count)[] groups = [("Usage", 1), ("Context", 6), ("Execution", 6), ("Unit of execution", 1)];
        await Expect(detail.DescendantGroups).ToHaveCountAsync(groups.Length);
        for (var index = 0; index < groups.Length; index++)
        {
            var (label, count) = groups[index];
            await Expect(detail.DescendantGroups.Nth(index)).ToHaveTextAsync(new Regex($@"^{label}\s*{count}(?!\d)"));
        }

        await Expect(detail.RelationsBlock.GetByRole(AriaRole.Link, new() { Name = "All descendants (14)", Exact = true })).ToBeVisibleAsync();
        var executionGroup = detail.DescendantGroup("Execution");
        await Expect(executionGroup.GetByRole(AriaRole.Link, new() { NameRegex = ExecutionKey() })).ToHaveCountAsync(5);
        var showAll = executionGroup.GetByRole(AriaRole.Link, new() { Name = "Show all 6", Exact = true });
        await Expect(showAll).ToBeVisibleAsync();

        await showAll.ClickAsync();
        var query = $"ancestor:{subject} type:exe";
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", query);
        await Expect(annotations.Filter).ToHaveValueAsync(query);
        await Expect(annotations.FilterCount).ToHaveTextAsync(new Regex(@"^6 of [\d,]+$"));
    }

    // ANN-DET-05: History lists the creation, the update and the ancestor cause.
    // Bulk create of an execution creates its responsibility for it, which its history names as the cause. Saving the
    // execution's title adds "Updated" to its stamps (by the signed-in user), which lead to the History tab: the update
    // with the title's change is the newest entry, the plain creation the oldest. The Annotation tab shows the form
    // again.
    [Test]
    [TestCase(178)]
    [UserRole(Role.Editor)]
    public async Task AnnotationHistory_CreateAndUpdate_ListsEntries()
    {
        var execution = PlatformData.AnnotationKey(PlatformData.Execution);
        var responsibility = PlatformData.AncestorKey(execution, PlatformData.Responsibility);
        var title = PlatformData.Title();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var history = new AnnotationHistory(App);
        await annotations.OpenAsync();
        var user = await App.SignedInNameAsync();
        await annotations.BulkCreateAsync(execution);

        await App.GotoAsync(Routes.AnnotationHistory(responsibility));
        await history.WaitForEntryAsync(Routes.AnnotationHistory(responsibility), "Created");
        await Expect(history.Region).ToContainTextAsync($"by {user}");
        await Expect(history.Region).ToContainTextAsync($"for {execution}");

        await detail.OpenFormAsync(execution);
        await detail.Title.FillAsync(title);
        await detail.SaveButton.ClickAsync();
        await Expect(App.Toast("Annotation saved")).ToBeVisibleAsync();
        await Expect(detail.StampsLink).ToHaveTextAsync(new Regex($@"^Created .+·\s*Updated .+ by {Regex.Escape(user)}$"));

        await detail.StampsLink.ClickAsync();
        await App.ExpectAtAsync(Routes.AnnotationHistory(execution));
        await Expect(detail.HistoryTabLink).ToHaveAttributeAsync("aria-current", "page");
        await history.WaitForEntryAsync(Routes.AnnotationHistory(execution), "Updated");
        await Expect(history.Entries).ToHaveCountAsync(2);

        await Expect(AnnotationHistory.ActionBadge(history.NewestEntry, "Updated")).ToBeVisibleAsync();
        await Expect(history.NewestEntry).ToContainTextAsync($"by {user}");
        await Expect(history.NewestEntry).ToContainTextAsync("Title");
        await Expect(history.NewestEntry).ToContainTextAsync(title);

        await Expect(AnnotationHistory.ActionBadge(history.OldestEntry, "Created")).ToBeVisibleAsync();
        await Expect(history.OldestEntry).ToContainTextAsync($"by {user}");
        await Expect(history.OldestEntry).Not.ToContainTextAsync(CauseText());

        await detail.AnnotationTabLink.ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(execution));
        await Expect(detail.Title).ToHaveValueAsync(title);
    }

    // ANN-DET-06: Old annotation tab URLs open the configuration tabs.
    // The annotation detail's old tabs (configuration with cfg=effective or resolved, schema, versions) redirect to the
    // same tab of the configuration detail, keeping only the list's filter and the version (v); the key need not exist,
    // and nothing is written.
    [Test]
    [TestCase(180)]
    public async Task AnnotationDetail_OldTabUrls_Redirect()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        var configuration = Routes.OldAnnotationTab(key, "configuration");

        await ExpectRedirectAsync(configuration, Routes.Configuration(key), "Edit", null, null);
        await ExpectRedirectAsync(
            AppRoutes.WithQuery(configuration, ("cfg", "effective"), ("q", "type:exe")),
            Routes.ConfigurationTab(key, "effective"),
            "Effective",
            "type:exe",
            null);
        await ExpectRedirectAsync(AppRoutes.WithQuery(configuration, ("cfg", "resolved")), Routes.ConfigurationTab(key, "resolved"), "Resolved", null, null);
        await ExpectRedirectAsync(Routes.OldAnnotationTab(key, "schema"), Routes.ConfigurationTab(key, "schema"), "Schema", null, null);
        await ExpectRedirectAsync(
            AppRoutes.WithQuery(Routes.OldAnnotationTab(key, "versions"), ("v", "2")),
            Routes.ConfigurationTab(key, "versions"),
            "Versions",
            null,
            "2");
    }

    // "<Name>, <key>" of the keyboard cursor's row; a key set's Name is the part after the code.
    private static string Announcement(string key)
    {
        return $"{key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..]}, {key}";
    }

    // Opens an old URL (the first navigation carries the mock scenario) and checks where it lands: the path, exactly
    // the q and v of the query (null: absent), and the configuration detail's current tab.
    private async Task ExpectRedirectAsync(string oldPath, string path, string tab, string? q, string? v)
    {
        await App.GotoAsync(oldPath);
        await App.ExpectAtAsync(path);
        await App.ExpectQueryAsync("q", q);
        await App.ExpectQueryAsync("v", v);
        if (q is null && v is null)
        {
            await Expect(Page).ToHaveURLAsync(new Regex($"/{Regex.Escape(path)}$"));
        }

        var panel = new DetailPanel(App, "configuration-detail");
        await Expect(panel.Root.GetByRole(AriaRole.Link, new() { Name = tab, Exact = true })).ToHaveAttributeAsync("aria-current", "page");
    }

    // Moves the focus out of any control, so the page's keys apply (J and K are ignored while typing, Enter and Escape
    // inside a control).
    private async Task BlurAsync()
    {
        await Page.EvaluateAsync("() => document.activeElement?.blur()");
    }

    // Empties the clipboard recorder, so the next copy is told apart from an earlier one of the same text.
    private async Task ClearCopiedAsync()
    {
        await Page.EvaluateAsync("() => { if (window.__copied) { window.__copied.length = 0; } }");
    }

    // A new annotation's stamps: its creation, without "Updated".
    [GeneratedRegex("^Created (?!.*Updated)")]
    private static partial Regex CreatedOnly();

    [GeneratedRegex("^(none|inherited)$")]
    private static partial Regex NoneOrInherited();

    [GeneratedRegex(@"^exe\.")]
    private static partial Regex ExecutionKey();

    // The cause of an entry: "for <key>" (created as an ancestor) or "with <key>" (deleted with it).
    [GeneratedRegex(@"\b(for|with) [a-z]{3}\.")]
    private static partial Regex CauseText();
}
