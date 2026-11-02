using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Graph;

// Create: New annotation and Bulk create on Graph (the navbar and the empty state), the empty graph of a new project,
// a Reader without create actions, and the dialog state across browser Back.
[Area(Area.Graph)]
public sealed class GraphCreateTests : PlatformTest
{
    // GRA-CRE-01: New in the Graph top bar creates an annotation and opens it in the graph.
    // The navbar's New ("New annotation" on a phone) opens the shared dialog on Graph; a new subject opens in the
    // graph's detail with the graph's query (layout=force), and Focus afterwards starts from it, the key the detail
    // recorded.
    [Test]
    [TestCase(128)]
    [UserRole(Role.Editor)]
    public async Task GraphCreate_NewInTopBar_OpensAnnotationInGraph()
    {
        var subject = PlatformData.AnnotationKey(PlatformData.Subject);

        var graph = new GraphPage(App, Routes);
        await graph.OpenAsync(("layout", "force"));
        await Expect(graph.ReloadCatalogButton).ToBeEnabledAsync();
        if (App.IsPhone)
        {
            await Expect(graph.HeaderNewAnnotationButton).ToBeVisibleAsync();
            await Expect(graph.HeaderNewButton).ToHaveCountAsync(0);
            await Expect(graph.HeaderBulkCreateButton).ToHaveCountAsync(0);
        }
        else
        {
            await Expect(graph.HeaderNewButton).ToBeVisibleAsync();
            await Expect(graph.HeaderBulkCreateButton).ToBeVisibleAsync();
        }

        var dialog = new NewAnnotationDialog(App);
        await dialog.OpenAsync();
        await Expect(dialog.TypeSelect).ToContainTextAsync("Execution");

        await dialog.SelectTypeAsync("Subject");
        await dialog.FillPartAsync("Name", subject["sbt.".Length..]);
        await Expect(dialog.KeyPreview).ToHaveAttributeAsync("title", subject);
        await Expect(dialog.CreateButton).ToBeEnabledAsync();

        await dialog.CreateButton.ClickAsync();
        await Expect(App.Toast($"{subject} created")).ToBeVisibleAsync();
        await Expect(dialog.Root).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.GraphNode(subject));
        await App.ExpectQueryAsync("layout", "force");
        var detail = new GraphNodeDetail(App, Routes);
        await Expect(detail.Identity).ToHaveTextAsync(subject);

        await detail.CloseAsync();
        await graph.FocusAsync();
        await App.ExpectQueryAsync("focus", subject);
        await graph.ExpectFocusSummaryAsync(1, subject);
        await Expect(graph.FocusBadge(subject)).ToHaveTextAsync(subject);
    }

    // GRA-CRE-02: The empty graph of a new project offers New annotation and Bulk create, and Bulk create draws the
    // tree.
    // A new project's graph shows the empty state with both create actions inside the graph and no legend; Bulk create
    // from there draws the new tree once the catalog reloads (the project holds only it, so the totals are exact), and
    // Focus without a selection or a recent key starts from the most connected subject or responsibility, the subject.
    [Test]
    [DisposableProject]
    [TestCase(130)]
    [UserRole(Role.Owner)]
    public async Task GraphCreate_BulkCreateInEmptyProject_DrawsTree()
    {
        var project = await CreateDisposableProjectAsync(PlatformData.Description(200));

        var graph = new GraphPage(App, project);
        await graph.OpenAsync();
        await Expect(graph.EmptyState).ToBeVisibleAsync();
        await Expect(graph.EmptyStateDescription).ToBeVisibleAsync();
        await Expect(graph.Legend.Group).ToHaveCountAsync(0);
        await Expect(graph.Legend.TypesButton).ToHaveCountAsync(0);

        await Expect(graph.EmptyNewAnnotationButton).ToBeVisibleAsync();
        await Expect(graph.EmptyBulkCreateButton).ToBeVisibleAsync();
        await Expect(graph.OpenAsListButton).ToBeDisabledAsync();

        await graph.EmptyBulkCreateButton.ClickAsync();
        var bulkCreate = new BulkCreateDialog(Page);
        await Expect(bulkCreate.Root).ToBeVisibleAsync();

        var tree = PlatformData.GraphTree();
        await bulkCreate.EnterKeysAsync(tree.Execution);
        await bulkCreate.CreateAsync(1);
        await Expect(bulkCreate.Result).ToHaveTextAsync("Created 5 annotations: 1 from the list and 4 missing ancestors.");

        await bulkCreate.CloseAsync();
        await App.ExpectAtAsync(project.Graph);
        await graph.WaitForCatalogAsync();
        await graph.WaitForLayoutAsync();
        await Expect(graph.EmptyState).ToHaveCountAsync(0);
        await graph.ExpectWholeViewSummaryAsync(5);
        await graph.Legend.OpenAsync();
        await graph.Legend.ExpectCountsAsync(1, 0, 1, 1, 1, 1, 0);
        await graph.Legend.CloseAsync();

        await graph.FocusAsync();
        await App.ExpectQueryAsync("focus", tree.Subject);
        await graph.ExpectFocusSummaryAsync(5, tree.Subject, total: 5);
    }

    // GRA-CRE-03: A Reader has no create actions on Graph.
    // A Reader sees the graph (its summary, which an empty view shows too, as "0 annotations" beside the empty state)
    // without New, New annotation or Bulk create in the navbar, and the empty graph of a view that was never used has
    // neither buttons nor a legend. Nothing is written.
    [Test]
    [TestCase(133)]
    [UserRole(Role.Reader)]
    public async Task GraphCreate_Reader_HasNoCreateActions()
    {
        var graph = new GraphPage(App, Routes);
        await graph.OpenAsync();
        await Expect(graph.ReloadCatalogButton).ToBeEnabledAsync();
        await Expect(graph.SummaryText).ToBeVisibleAsync();
        if (await graph.TotalAsync() == 0)
        {
            await Expect(graph.EmptyState).ToBeVisibleAsync();
        }

        await Expect(graph.HeaderNewButton).ToHaveCountAsync(0);
        await Expect(graph.HeaderNewAnnotationButton).ToHaveCountAsync(0);
        await Expect(graph.HeaderBulkCreateButton).ToHaveCountAsync(0);

        var unused = new GraphPage(App, Routes with { View = PlatformData.ViewName() });
        await unused.OpenAsync();
        await Expect(unused.EmptyState).ToBeVisibleAsync();
        await Expect(unused.Application.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);
        await Expect(unused.Legend.Group).ToHaveCountAsync(0);
        await Expect(unused.Legend.TypesButton).ToHaveCountAsync(0);
    }

    // GRA-CRE-04: A create dialog left open on Graph does not reopen on Annotations after Back.
    // Annotations and Graph share the open state of their dialogs, and Graph closes them when the route leaves (the
    // 424-130 regression): New left open on Graph is gone after Back on Annotations, whose New still opens and cancels
    // its own. Nothing is created.
    [Test]
    [TestCase(136)]
    [UserRole(Role.Editor)]
    public async Task GraphCreate_DialogLeftOpen_DoesNotReopenOnAnnotations()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await App.NavigateAsync("Graph");
        await App.ExpectAtAsync(Routes.Graph);

        var dialog = new NewAnnotationDialog(App);
        await dialog.OpenAsync();

        await Page.GoBackAsync();
        await App.ExpectAtAsync(Routes.Annotations);
        await annotations.WaitForCatalogAsync();
        await Expect(dialog.Root).ToHaveCountAsync(0);

        await dialog.OpenAsync();
        await dialog.CancelAsync();
    }
}
