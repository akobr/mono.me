using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

// Catalog: the tree with every path, the bulk bar, the empty view, the catalog's reload and the page memory of the
// annotations.
[Area(Area.Annotations)]
public sealed partial class AnnotationCatalogTests : PlatformTest
{
    // ANN-CAT-01: Tree shows every path by subject and by responsibility.
    // A unit of execution with its six ancestors in the tree: by subject an execution sits under its context and its
    // usage, and the unit of execution under both copies of it; by responsibility the unit of execution sits under its
    // unit and its execution. A filter expands every matching path and dims the root it keeps without a match, and
    // checking one copy of a key checks every copy. The grid lists each key once with the same filter.
    [Test]
    [TestCase(113)]
    [UserRole(Role.Editor)]
    [DesktopOnly]
    public async Task AnnotationTree_Perspectives_ShowEveryPath()
    {
        var unitOfExecution = PlatformData.AnnotationKey(PlatformData.UnitOfExecution);
        var subject = PlatformData.AncestorKey(unitOfExecution, PlatformData.Subject);
        var context = PlatformData.AncestorKey(unitOfExecution, PlatformData.Context);
        var usage = PlatformData.AncestorKey(unitOfExecution, PlatformData.Usage);
        var execution = PlatformData.AncestorKey(unitOfExecution, PlatformData.Execution);
        var responsibility = PlatformData.AncestorKey(unitOfExecution, PlatformData.Responsibility);
        var unit = PlatformData.AncestorKey(unitOfExecution, PlatformData.Unit);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(unitOfExecution);
        await annotations.WaitForCatalogAsync();

        await annotations.ListModeTab("Tree").ClickAsync();
        await App.ExpectQueryAsync("mode", "tree");
        await Expect(annotations.TreePerspective).ToHaveTextAsync("By subject");
        await Expect(annotations.ExpandAllButton).ToBeVisibleAsync();
        await Expect(annotations.CollapseAllButton).ToBeVisibleAsync();

        await annotations.FilterAsync($"ancestor:{subject}");
        await ExpectTreeAsync(annotations, dimmed: subject, (context, 1), (usage, 1), (execution, 2), (unitOfExecution, 2), (responsibility, 0), (unit, 0));

        await annotations.RowCheckbox(unitOfExecution).First.ClickAsync();
        await Expect(annotations.RowCheckbox(unitOfExecution).Nth(0)).ToBeCheckedAsync();
        await Expect(annotations.RowCheckbox(unitOfExecution).Nth(1)).ToBeCheckedAsync();
        await Expect(annotations.BulkBar.SelectedCount).ToHaveTextAsync("1 selected");

        await annotations.SelectTreePerspectiveAsync("By responsibility");
        await App.ExpectQueryAsync("by", "responsibility");
        await annotations.FilterAsync($"ancestor:{responsibility}");
        await ExpectTreeAsync(annotations, dimmed: responsibility, (unit, 1), (usage, 1), (execution, 1), (unitOfExecution, 2), (subject, 0), (context, 0));

        await annotations.ListModeTab("Grid").ClickAsync();
        await App.ExpectQueryAsync("mode", null);
        await Expect(annotations.Filter).ToHaveValueAsync($"ancestor:{responsibility}");
        await Expect(annotations.AnnotationRows).ToHaveCountAsync(4);
        foreach (var key in new[] { unit, usage, execution, unitOfExecution })
        {
            await Expect(annotations.Item(key)).ToHaveCountAsync(1);
        }
    }

    // ANN-CAT-02: Bulk bar disables, enables, adds and removes a label, and deletes selected rows with Undo.
    // On three own subjects the bulk bar acts on the checked rows only: Disable and Enable change their status, Label
    // adds a new label to every row and removes it from one, and Delete asks for the number of rows, deletes them with
    // a toast whose Undo brings them back. Each action clears the selection.
    [Test]
    [TestCase(114)]
    [UserRole(Role.Editor)]
    [DesktopOnly]
    public async Task AnnotationBulkBar_SelectedRows_ChangeDeleteAndUndo()
    {
        var prefix = PlatformData.AnnotationKeySetPrefix(PlatformData.Subject);
        var keys = PlatformData.AnnotationKeySet(prefix, 3);
        var label = PlatformData.LabelName();
        var annotations = new AnnotationsPage(App, Routes);
        var bar = annotations.BulkBar;
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync([.. keys]);
        await annotations.WaitForCatalogAsync();
        await annotations.FilterAsync($"key:{prefix}");
        await Expect(annotations.AnnotationRows).ToHaveCountAsync(3);

        await CheckRowsAsync(annotations, keys[0], keys[1]);
        await Expect(bar.SelectedCount).ToHaveTextAsync("2 selected");
        await bar.DisableButton.ClickAsync();
        await Expect(App.Toast("Annotations disabled")).ToBeVisibleAsync();
        await ExpectCellsAsync(annotations, "Status", "Disabled", keys[0], keys[1]);
        await Expect(bar.Root).ToBeHiddenAsync();

        await CheckRowsAsync(annotations, keys[0], keys[1]);
        await bar.EnableButton.ClickAsync();
        await Expect(App.Toast("Annotations enabled")).ToBeVisibleAsync();
        await ExpectCellsAsync(annotations, "Status", "Active", keys[0], keys[1]);

        await annotations.SelectAllRowsCheckbox.ClickAsync();
        await Expect(bar.SelectedCount).ToHaveTextAsync("3 selected");
        await bar.ChooseLabelAsync(label, existing: false);
        await bar.AddLabelButton.ClickAsync();
        await Expect(App.Toast($"Label {label} added")).ToBeVisibleAsync();
        await ExpectCellsAsync(annotations, "Labels", label, [.. keys]);

        await CheckRowsAsync(annotations, keys[0]);
        await Expect(bar.SelectedCount).ToHaveTextAsync("1 selected");
        await bar.ChooseLabelAsync(label, existing: true);
        await bar.RemoveLabelButton.ClickAsync();
        await Expect(App.Toast($"Label {label} removed")).ToBeVisibleAsync();
        await Expect(await annotations.GridCellAsync(keys[0], "Labels")).Not.ToContainTextAsync(label);
        await ExpectCellsAsync(annotations, "Labels", label, keys[1], keys[2]);

        await CheckRowsAsync(annotations, keys[0], keys[1]);
        await bar.DeleteButton.ClickAsync();
        var confirm = bar.DeleteDialog(2);
        await confirm.ExpectOpenAsync();
        await Expect(confirm.Description).ToHaveTextAsync(
            "The 2 annotations and their descendants move to Deleted, with their configuration. You can restore them from Deleted for 90 days.");
        await Expect(confirm.Button("Delete 2")).ToBeVisibleAsync();

        // Admin UI bug (424-152 review): AnnotationBulkBar.vue deleteSelected() reads props.keys after removeMany, so the
        // toast says "1 annotations deleted" instead of "2 annotations deleted"; both are accepted. The bar re-rendered
        // with one key fewer after each removal before it unmounted, so it counts the rows still selected, not the
        // deleted ones. The outcome is checked strictly: both rows are gone, and Undo restores both ("2 annotations
        // restored").
        await confirm.ConfirmAsync("Delete 2");
        var deletedToast = App.Toast(BulkDeletedToast());
        await Expect(deletedToast).ToBeVisibleAsync();
        var deletedTitle = (await deletedToast.InnerTextAsync()).Trim();
        await Expect(App.ToastAction(deletedTitle, "Undo")).ToBeVisibleAsync();
        await Expect(annotations.AnnotationRows).ToHaveCountAsync(1);
        await Expect(annotations.Item(keys[2])).ToBeVisibleAsync();

        await App.ToastAction(deletedTitle, "Undo").ClickAsync();
        await Expect(App.Toast("2 annotations restored")).ToBeVisibleAsync();
        await Expect(annotations.AnnotationRows).ToHaveCountAsync(3);
        foreach (var key in keys)
        {
            await Expect(annotations.Item(key)).ToBeVisibleAsync();
        }
    }

    // ANN-CAT-03: An empty view shows the empty state with create actions.
    // A view nobody wrote to (the API registers a view on its first write) opens with the empty state and its create
    // actions for a Contributor: Bulk create in the view and New annotation open and cancel without writing, so the
    // view stays empty.
    [Test]
    [TestCase(145)]
    [UserRole(Role.Editor)]
    public async Task AnnotationCatalog_EmptyView_OffersCreateActions()
    {
        var view = PlatformData.ViewName();
        var routes = Routes with { View = view };
        var annotations = new AnnotationsPage(App, routes);
        await annotations.OpenAsync();

        await Expect(annotations.EmptyState).ToBeVisibleAsync();
        await Expect(annotations.EmptyStateBlock.GetByText("Create the first annotations, or import them with the sform CLI.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(annotations.EmptyStateButton("New annotation")).ToBeVisibleAsync();
        await Expect(annotations.EmptyStateButton("Bulk create")).ToBeVisibleAsync();
        await Expect(annotations.FilterCount).ToHaveTextAsync("0");

        await annotations.EmptyStateButton("Bulk create").ClickAsync();
        var bulkCreate = new BulkCreateDialog(Page);
        await Expect(bulkCreate.Root).ToBeVisibleAsync();
        await Expect(bulkCreate.Root).ToHaveAccessibleDescriptionAsync($"In {routes.Organization}/{routes.Project}, view {view}.");
        await bulkCreate.CancelAsync();
        await Expect(annotations.EmptyState).ToBeVisibleAsync();

        await annotations.EmptyStateButton("New annotation").ClickAsync();
        var dialog = new NewAnnotationDialog(App);
        await Expect(dialog.Root).ToBeVisibleAsync();
        await Expect(dialog.FillInTheNames).ToBeVisibleAsync();
        await dialog.CancelAsync();
        await Expect(annotations.EmptyState).ToBeVisibleAsync();
    }

    // ANN-CAT-04: Reload the catalog shows an annotation created elsewhere.
    // The catalog loads once and never refreshes by itself: a subject that another writer creates meanwhile (on the
    // mock the API from the page, on a deployed target Bulk create in a second page) is not found until "Reload the
    // catalog" loads it again.
    [Test]
    [TestCase(146)]
    [UserRole(Role.Editor)]
    public async Task AnnotationCatalog_Reload_ShowsAnnotationsCreatedElsewhere()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        if (Target.IsMock)
        {
            var response = await MockApi.FetchAsync("POST", MockApi.ViewPath("annotations/simple"), new[] { key });
            var created = response.Json()?.AsArray().Select(annotation => annotation?["AnnotationKey"]?.GetValue<string>()).ToArray() ?? [];
            if (response.Status != 200 || created is not [var only] || only != key)
            {
                Assert.Fail($"The mock API should create exactly {key} with 200; it answered {response.Status}: {response.Body}");
            }
        }
        else
        {
            var other = await OpenSecondPageAsync();
            var otherAnnotations = new AnnotationsPage(other, Routes);
            await otherAnnotations.OpenAsync();
            var bulkCreate = await otherAnnotations.OpenBulkCreateAsync();
            await bulkCreate.EnterKeysAsync(key);
            await bulkCreate.CreateAsync(1);
            await Expect(bulkCreate.Result).ToHaveTextAsync("Created 1 annotation.");
            await other.Page.CloseAsync();
        }

        await annotations.FilterAsync($"key:{key}");
        await Expect(annotations.NoMatchingAnnotations).ToBeVisibleAsync();
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(0));

        await annotations.ReloadCatalogButton.ClickAsync();
        await annotations.WaitForCatalogAsync();
        await Expect(annotations.Item(key)).ToBeVisibleAsync();
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(1));
    }

    // ANN-CAT-05: Returning from another section restores the filter, detail, tab, selection and sorting.
    // The annotations page remembers where it was left when the user comes back from another section through the
    // navigation: the filter always, and on a wide screen also the open detail on its History tab, the checked row and
    // the descending sort by key. Following the navigation's Annotations from within the section opens the plain list
    // instead. On a phone the detail covers the list and its navigation, so only the filter is checked there.
    [Test]
    [TestCase(148)]
    [UserRole(Role.Editor)]
    public async Task AnnotationCatalog_ReturnFromSection_RestoresPageMemory()
    {
        var prefix = PlatformData.AnnotationKeySetPrefix(PlatformData.Subject);
        var keys = PlatformData.AnnotationKeySet(prefix, 2);
        var filter = $"key:{prefix}";
        var wide = Page.ViewportSize is not { Width: < AnnotationsPage.GridWidth };
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync([.. keys]);
        await annotations.WaitForCatalogAsync();
        await annotations.FilterAsync(filter);

        if (wide)
        {
            await annotations.SortButton("Key").ClickAsync();
            await ExpectFirstRowAsync(annotations, keys[0]);
            await annotations.SortButton("Key").ClickAsync();
            await ExpectFirstRowAsync(annotations, keys[1]);
            await annotations.RowCheckbox(keys[0]).ClickAsync();
            await Expect(annotations.BulkBar.SelectedCount).ToHaveTextAsync("1 selected");

            await annotations.OpenAnnotationFromListAsync(keys[1]);
            await detail.OpenHistoryTabAsync(keys[1]);
            await App.ExpectQueryAsync("q", filter);
        }

        await App.NavigateAsync("Configurations");
        await App.ExpectAtAsync(Routes.Configurations);

        await App.NavigateAsync("Annotations");
        await Expect(annotations.Filter).ToHaveValueAsync(filter);
        if (!wide)
        {
            return;
        }

        await App.ExpectAtAsync(Routes.AnnotationHistory(keys[1]));
        await App.ExpectQueryAsync("q", filter);
        await Expect(detail.History).ToBeVisibleAsync();
        await Expect(annotations.BulkBar.SelectedCount).ToHaveTextAsync("1 selected");
        await ExpectFirstRowAsync(annotations, keys[1]);

        await App.NavigateAsync("Annotations");
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", null);
        await Expect(annotations.Filter).ToHaveValueAsync(string.Empty);
    }

    // The tree rows of the filter: the dimmed root once (kept for its matching descendants), and each key the number
    // of times it is listed (0: not at all), every one undimmed.
    private static async Task ExpectTreeAsync(AnnotationsPage annotations, string dimmed, params (string Key, int Copies)[] rows)
    {
        await Expect(annotations.AnnotationRows).ToHaveCountAsync(1 + rows.Sum(row => row.Copies));
        await Expect(annotations.Item(dimmed)).ToHaveCountAsync(1);
        await Expect(annotations.Item(dimmed)).ToHaveClassAsync(Dimmed());
        foreach (var (key, copies) in rows)
        {
            await Expect(annotations.Item(key)).ToHaveCountAsync(copies);
            for (var copy = 0; copy < copies; copy++)
            {
                await Expect(annotations.Item(key).Nth(copy)).Not.ToHaveClassAsync(Dimmed());
            }
        }
    }

    // Checks the rows' checkboxes (the bulk bar then counts them).
    private static async Task CheckRowsAsync(AnnotationsPage annotations, params string[] keys)
    {
        foreach (var key in keys)
        {
            await annotations.RowCheckbox(key).ClickAsync();
            await Expect(annotations.RowCheckbox(key)).ToBeCheckedAsync();
        }
    }

    // The cell of the column holds the text in the row of every key.
    private static async Task ExpectCellsAsync(AnnotationsPage annotations, string column, string text, params string[] keys)
    {
        foreach (var key in keys)
        {
            await Expect(await annotations.GridCellAsync(key, column)).ToContainTextAsync(text);
        }
    }

    // The first row of the grid is the key's.
    private static async Task ExpectFirstRowAsync(AnnotationsPage annotations, string key)
    {
        await Expect(annotations.AnnotationRows.First.GetByRole(AriaRole.Checkbox)).ToHaveAccessibleNameAsync($"Select {key}");
    }

    // The toast of the bulk delete of two rows: "2 annotations deleted", or "1 annotations deleted" of the Admin UI bug
    // (424-152 review) that counts the rows still selected (no singular form either way).
    [GeneratedRegex("^[12] annotations deleted$")]
    private static partial Regex BulkDeletedToast();

    // An unmatched ancestor that the tree keeps for its matching descendants is dimmed.
    [GeneratedRegex(@"(^|\s)opacity-50(\s|$)")]
    private static partial Regex Dimmed();
}
