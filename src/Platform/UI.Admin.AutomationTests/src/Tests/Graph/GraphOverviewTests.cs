using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Graph;

// Overview: the graph of a created tree in focus mode, its summary, the legend counts and the keyboard cursor (arrow
// keys, Enter, Escape), and the phone layout: the Types popover, the icon-only toolbar and the full-screen detail.
[Area(Area.Graph)]
public sealed class GraphOverviewTests : PlatformTest
{
    // GRA-OVR-01: Graph focused on a created tree shows its neighbourhood and opens a node by keyboard.
    // The graph focused on the subject of a new tree draws its five annotations (the summary in the graph's name, the
    // badge, depth 2, Show everything) with a count of one per type of the tree in the legend; the first ArrowDown
    // puts the keyboard cursor on the focus key, and Enter opens its detail beside the graph with the graph's query.
    [Test]
    [Elementary]
    [TestCase(16)]
    [UserRole(Role.Editor)]
    public async Task Graph_FocusOnCreatedTree_OpensNodeByKeyboard()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        await graph.OpenAsync(("focus", tree.Subject));
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await Expect(graph.FocusBadge(tree.Subject)).ToHaveTextAsync(tree.Subject);
        await Expect(graph.DepthInput).ToHaveValueAsync("2");
        await Expect(graph.ShowEverythingButton).ToBeVisibleAsync();

        await graph.Legend.OpenAsync();
        await Expect(graph.Legend.PressedChips).ToHaveCountAsync(7);
        await graph.Legend.ExpectCountsAsync(1, 0, 1, 1, 1, 1, 0);
        await graph.Legend.CloseAsync();

        await graph.FocusGraphAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(graph.CursorAnnouncement).ToHaveTextAsync(Announcement(tree.SubjectName, 2));

        await Page.Keyboard.PressAsync("Enter");
        await App.ExpectAtAsync(Routes.GraphNode(tree.Subject));
        await App.ExpectQueryAsync("focus", tree.Subject);
        await Expect(new GraphNodeDetail(App, Routes).Identity).ToHaveTextAsync(tree.Subject);
    }

    // GRA-OVR-02: Arrow keys move along the links, Enter opens the annotation and Escape closes it.
    // With reduced motion the layered layout settles before its first frame, so its tiers stay put: ArrowDown moves
    // from the focused subject to a connected node one tier lower (its context or its usage) and then to the
    // execution, Enter opens the execution's detail with the graph's query, and Escape closes it again (the graph's
    // own key handler on a desktop, the detail's window listener on a phone, where the detail covered the graph).
    [Test]
    [TestCase(56)]
    [UserRole(Role.Editor)]
    public async Task Graph_ArrowKeys_MoveAlongLinksAndOpenNode()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        await graph.OpenAsync(("focus", tree.Subject));
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await graph.FocusGraphAsync();

        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(graph.CursorAnnouncement).ToHaveTextAsync(Announcement(tree.SubjectName, 2));

        // One tier lower: the context (2 connections) or the usage (3), whichever lies nearer the arrow's direction.
        await Page.Keyboard.PressAsync("ArrowDown");
        var contextName = Regex.Escape(tree.ContextName);
        var usageName = Regex.Escape(tree.ResponsibilityName);
        await Expect(graph.CursorAnnouncement).ToHaveTextAsync(new Regex($"^.+ (?:{contextName}, 2|{usageName}, 3) connections$"));

        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(graph.CursorAnnouncement).ToHaveTextAsync(Announcement(tree.ContextName, 2));

        await Page.Keyboard.PressAsync("Enter");
        var detail = new GraphNodeDetail(App, Routes);
        await App.ExpectAtAsync(Routes.GraphNode(tree.Execution));
        await App.ExpectQueryAsync("focus", tree.Subject);
        await Expect(detail.Identity).ToHaveTextAsync(tree.Execution);

        await Page.Keyboard.PressAsync("Escape");
        await App.ExpectAtAsync(Routes.Graph);
        await App.ExpectQueryAsync("focus", tree.Subject);
        await Expect(detail.Identity).ToHaveCountAsync(0);
        await Expect(graph.Application).ToBeVisibleAsync();
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
    }

    // GRA-OVR-03: On a phone the legend is a Types popover, the toolbar is icon-only and a node's detail covers the
    // graph.
    // Below 640 px of graph width the legend is the button "Types", whose label counts the shown types while some are
    // hidden; below 720 px Fit, Reheat, Export PNG and Open as list show only their icons; the navbar has only the icon
    // button "New annotation"; and a node opened with the keyboard fills the screen with "Back to the list", which
    // brings the graph back with its focus.
    [Test]
    [PhoneOnly]
    [TestCase(57)]
    [UserRole(Role.Editor)]
    public async Task Graph_PhoneLayout_CollapsesLegendAndCoversGraph()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        var legend = graph.Legend;
        await graph.OpenAsync(("focus", tree.Subject));
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await legend.ExpectTypesButtonAsync(7);
        await Expect(legend.Group).ToHaveCountAsync(0);
        await Expect(graph.HeaderNewAnnotationButton).ToBeVisibleAsync();
        await Expect(graph.HeaderNewButton).ToHaveCountAsync(0);
        await Expect(graph.HeaderBulkCreateButton).ToHaveCountAsync(0);

        foreach (var button in new[] { graph.FitButton, graph.ReheatButton, graph.ExportPngButton, graph.OpenAsListButton })
        {
            await Expect(button).ToBeVisibleAsync();
            await Expect(button).ToHaveTextAsync(string.Empty);
        }

        await Expect(graph.LayoutSelect).ToBeVisibleAsync();
        await Expect(graph.DepthInput).ToBeVisibleAsync();
        await Expect(graph.ShowEverythingButton).ToBeVisibleAsync();

        await legend.TypesButton.ClickAsync();
        await Expect(legend.Group).ToBeVisibleAsync();
        await Expect(legend.PressedChips).ToHaveCountAsync(7);

        // Escape with the chip focused closes only the popover.
        await legend.ChipTitled("Hide subjects").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(legend.Group).ToBeHiddenAsync();
        await legend.ExpectTypesButtonAsync(6);
        await graph.ExpectFocusSummaryAsync(4, tree.Subject);

        await legend.ShowAllTypesAsync();
        await legend.ExpectTypesButtonAsync(7);
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);

        await graph.FocusGraphAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(graph.CursorAnnouncement).ToHaveTextAsync(Announcement(tree.SubjectName, 2));
        await Page.Keyboard.PressAsync("Enter");
        var detail = new GraphNodeDetail(App, Routes);
        await App.ExpectAtAsync(Routes.GraphNode(tree.Subject));
        await App.ExpectQueryAsync("focus", tree.Subject);
        await detail.Panel.ExpectModeAsync(DetailPanel.FullMode);
        await Expect(detail.Detail.BackToTheListButton).ToBeVisibleAsync();
        await Expect(detail.Detail.CloseButton).ToBeHiddenAsync();
        await Expect(graph.Application).ToBeHiddenAsync();

        await detail.Detail.BackToTheListButton.ClickAsync();
        await App.ExpectAtAsync(Routes.Graph);
        await App.ExpectQueryAsync("focus", tree.Subject);
        await Expect(graph.Application).ToBeVisibleAsync();
        await legend.ExpectTypesButtonAsync(7);
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
    }

    // The live region of the keyboard cursor, "<Type label> <name>, <n> connections": any type label, so renamed type
    // names of a project do not matter.
    private static Regex Announcement(string name, int connections)
    {
        return new Regex($"^.+ {Regex.Escape(name)}, {connections} connections?$");
    }
}
