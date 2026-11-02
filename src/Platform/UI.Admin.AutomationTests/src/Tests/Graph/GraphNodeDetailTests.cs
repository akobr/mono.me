using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Graph;

// Node Detail: the node detail at graph/<key>, beside the graph or full screen, with tabs, breadcrumb and lineage
// links that stay on the graph; Show in list and Show in graph, the graph after a delete and its Undo, "Annotation not
// found" and the redirects of the old graph/<key>/* tab links.
[Area(Area.Graph)]
public sealed class GraphNodeDetailTests : PlatformTest
{
    // Below md (768 px) the detail is full screen and hides the graph.
    private bool IsFullScreenDetail => Page.ViewportSize is { Width: < DetailPanel.OverlayWidth };

    // GRA-DET-01: A node's detail opens beside the graph, and its tabs, breadcrumb and close keep the graph's query.
    // A node opened by its URL shows its detail beside the graph (over it, full screen, below md); its History tab,
    // the breadcrumb's links of its ancestors and its close keep the graph's focus and layout, so the graph comes back
    // as it was.
    [Test]
    [TestCase(97)]
    [UserRole(Role.Editor)]
    public async Task GraphDetail_OpenByUrl_KeepsGraphQueryOnTabsAndClose()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        var detail = new GraphNodeDetail(App, Routes);
        await detail.OpenAsync(tree.Execution, ("focus", tree.Subject), ("layout", "force"));
        await Expect(detail.Identity).ToHaveTextAsync(tree.Execution);
        await Expect(detail.AnnotationTab).ToBeVisibleAsync();
        await Expect(detail.HistoryTab).ToBeVisibleAsync();
        if (IsFullScreenDetail)
        {
            await Expect(graph.Application).ToBeHiddenAsync();
            await Expect(detail.Detail.BackToTheListButton).ToBeVisibleAsync();
        }
        else
        {
            await Expect(graph.Application).ToBeVisibleAsync();
            await Expect(detail.Detail.CloseButton).ToBeVisibleAsync();
        }

        await Expect(detail.BreadcrumbLink($"Subject {tree.SubjectName}")).ToBeVisibleAsync();
        await Expect(detail.BreadcrumbLink($"Context {tree.ContextName}")).ToBeVisibleAsync();
        await Expect(detail.BreadcrumbCurrent).ToHaveTextAsync($"Execution {tree.ContextName}");

        await detail.HistoryTab.ClickAsync();
        await App.ExpectAtAsync(Routes.GraphNodeHistory(tree.Execution));
        await ExpectGraphQueryAsync(tree.Subject, "force");
        await Expect(detail.HistoryRegion).ToBeVisibleAsync();

        await detail.BreadcrumbLink($"Context {tree.ContextName}").ClickAsync();
        await App.ExpectAtAsync(Routes.GraphNode(tree.Context));
        await ExpectGraphQueryAsync(tree.Subject, "force");
        await Expect(detail.Identity).ToHaveTextAsync(tree.Context);

        await detail.CloseAsync();
        await ExpectGraphQueryAsync(tree.Subject, "force");
        await Expect(detail.Panel.Root).ToHaveCountAsync(0);
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await Expect(graph.LayoutSelect).ToHaveTextAsync("Force");
    }

    // GRA-DET-02: Lineage, Show in list and Show in graph move between the graph and the list.
    // The lineage's links of a node stay on the graph; "Show in list" of the graph's More actions opens the same
    // annotation on Annotations, whose "Show in graph" brings it back to the graph focused on it.
    [Test]
    [TestCase(98)]
    [UserRole(Role.Editor)]
    public async Task GraphDetail_LineageAndShowInList_SwitchListAndGraph()
    {
        var tree = await CreateGraphTreeAsync();

        var detail = new GraphNodeDetail(App, Routes);
        await detail.OpenAsync(tree.Execution);
        await Expect(detail.Identity).ToHaveTextAsync(tree.Execution);

        await detail.OpenLineageAsync();
        foreach (var ancestor in new[] { tree.Responsibility, tree.Subject, tree.Usage, tree.Context })
        {
            await Expect(detail.LineageLink(ancestor)).ToBeVisibleAsync();
        }

        await detail.LineageLink(tree.Subject).ClickAsync();
        await App.ExpectAtAsync(Routes.GraphNode(tree.Subject));
        await Expect(detail.Identity).ToHaveTextAsync(tree.Subject);

        await detail.Detail.MoreActionsButton.ClickAsync();
        foreach (var item in new[] { "Show in list", "Copy link", "Copy key", "Delete" })
        {
            await Expect(detail.Detail.MenuItem(item)).ToBeVisibleAsync();
        }

        await detail.Detail.MenuItem("Show in list").ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(tree.Subject));
        await Expect(detail.Identity).ToHaveTextAsync(tree.Subject);

        await new AnnotationDetail(App, Routes).MoreActionsAsync("Show in graph");
        await App.ExpectAtAsync(Routes.GraphNode(tree.Subject));
        await App.ExpectQueryAsync("focus", tree.Subject);
        await Expect(detail.Identity).ToHaveTextAsync(tree.Subject);

        await detail.CloseAsync();
        await App.ExpectQueryAsync("focus", tree.Subject);
        await new GraphPage(App, Routes).ExpectFocusSummaryAsync(5, tree.Subject);
    }

    // GRA-DET-03: Deleting a node from its detail removes it from the graph, and Undo brings it back.
    // A context without descendants deleted from its graph detail leaves the graph at once (its subject alone, the
    // legend's Contexts 0), and the deletion toast's Undo restores it and opens its detail again with the graph's
    // query. The Reka toast closes on any Escape, so the phone's Types popover is closed with its button.
    [Test]
    [TestCase(100)]
    [UserRole(Role.Editor)]
    public async Task GraphDetail_DeleteAndUndo_RemovesAndRestoresNode()
    {
        var context = PlatformData.AnnotationKey(PlatformData.Context);
        var subject = PlatformData.AncestorKey(context, PlatformData.Subject);
        await BulkCreateForGraphAsync("Created 2 annotations: 1 from the list and 1 missing ancestor.", context);

        var graph = new GraphPage(App, Routes);
        var legend = graph.Legend;
        var detail = new GraphNodeDetail(App, Routes);
        await detail.OpenAsync(context, ("focus", subject));
        await Expect(detail.Identity).ToHaveTextAsync(context);
        await graph.WaitForCatalogAsync();

        var deleteDialog = detail.Detail.DeleteDialog(context);
        await detail.Detail.MoreActionsAsync("Delete");
        await deleteDialog.ExpectOpenAsync();
        await Expect(deleteDialog.CancelButton).ToBeVisibleAsync();
        await Expect(deleteDialog.Button("Delete")).ToBeVisibleAsync();

        await deleteDialog.ConfirmAsync("Delete");
        await Expect(App.Toast($"{context} deleted")).ToBeVisibleAsync();
        await Expect(App.ToastAction($"{context} deleted", "Undo")).ToBeVisibleAsync();
        await App.ExpectAtAsync(Routes.Graph);
        await App.ExpectQueryAsync("focus", subject);
        await graph.ExpectFocusSummaryAsync(1, subject);

        await legend.OpenAsync();
        await Expect(legend.Count(PlatformData.Context)).ToHaveTextAsync("0");
        await Expect(legend.Count(PlatformData.Subject)).ToHaveTextAsync("1");
        await legend.CloseAsync(withEscape: false);

        await App.ToastAction($"{context} deleted", "Undo").ClickAsync();
        await App.ExpectAtAsync(Routes.GraphNode(context));
        await App.ExpectQueryAsync("focus", subject);
        await Expect(detail.Identity).ToHaveTextAsync(context);

        await detail.CloseAsync();
        await App.ExpectQueryAsync("focus", subject);
        await graph.ExpectFocusSummaryAsync(2, subject);
        await legend.OpenAsync();
        await Expect(legend.Count(PlatformData.Context)).ToHaveTextAsync("1");
        await legend.CloseAsync(withEscape: false);
    }

    // GRA-DET-04: An unknown node shows Annotation not found, and the old tab links of a node redirect to its
    // configuration with their query.
    // A key that never existed shows "Annotation not found" in the graph's detail; the old tab links of a node
    // (configuration with ?cfg=, schema, versions) land on the configuration's tabs and keep only q (versions also v and
    // from). Nothing is written, so any pool user will do.
    [Test]
    [TestCase(102)]
    public async Task GraphDetail_UnknownKeyAndOldTabs_ShowNotFoundAndRedirect()
    {
        var missing = PlatformData.AnnotationKey(PlatformData.Subject);

        var detail = new GraphNodeDetail(App, Routes);
        await detail.OpenAsync(missing);
        await Expect(detail.Detail.NotFound).ToBeVisibleAsync();
        await Expect(detail.NotFoundDescription(missing)).ToBeVisibleAsync();

        await App.GotoAsync(AppRoutes.WithQuery(Routes.OldGraphTab(missing, "configuration"), ("layout", "force")));
        await App.ExpectAtAsync(Routes.Configuration(missing));
        await App.ExpectQueryAsync("layout", null);

        await App.GotoAsync(AppRoutes.WithQuery(Routes.OldGraphTab(missing, "configuration"), ("cfg", "effective"), ("q", "type:sbt")));
        await App.ExpectAtAsync(Routes.ConfigurationTab(missing, "effective"));
        await App.ExpectQueryAsync("q", "type:sbt");
        await App.ExpectQueryAsync("cfg", null);

        await App.GotoAsync(AppRoutes.WithQuery(Routes.OldGraphTab(missing, "configuration"), ("cfg", "resolved")));
        await App.ExpectAtAsync(Routes.ConfigurationTab(missing, "resolved"));
        await App.ExpectQueryAsync("cfg", null);

        await App.GotoAsync(AppRoutes.WithQuery(Routes.OldGraphTab(missing, "schema"), ("q", "type:sbt")));
        await App.ExpectAtAsync(Routes.ConfigurationTab(missing, "schema"));
        await App.ExpectQueryAsync("q", "type:sbt");

        await App.GotoAsync(AppRoutes.WithQuery(Routes.OldGraphTab(missing, "versions"), ("v", "2"), ("from", "1")));
        await App.ExpectAtAsync(Routes.ConfigurationTab(missing, "versions"));
        await App.ExpectQueryAsync("v", "2");
        await App.ExpectQueryAsync("from", "1");
    }

    // The graph's query that the detail's links keep: the focus and the layout.
    private async Task ExpectGraphQueryAsync(string focus, string layout)
    {
        await App.ExpectQueryAsync("focus", focus);
        await App.ExpectQueryAsync("layout", layout);
    }
}
