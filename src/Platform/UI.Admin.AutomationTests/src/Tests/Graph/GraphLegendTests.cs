using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Graph;

// Legend: the type legend "Annotation types": hiding and showing types, "All types", the "Every type is hidden" state,
// hidden types across a catalog reload, and Enter on a focused chip.
[Area(Area.Graph)]
public sealed class GraphLegendTests : PlatformTest
{
    // GRA-LEG-01: Hiding types removes their nodes, survives a catalog reload, and the legend brings them back.
    // A hidden type leaves the drawn set (the summary drops) but keeps its count in the legend, and stays hidden after
    // "Reload the catalog", because the page memory keeps it; with every type hidden the state "Every type is hidden"
    // shows below the legend without covering it, so a chip (the "Types" button on a phone) still takes the click that
    // shows a type again, and "All types" shows them all.
    [Test]
    [TestCase(58)]
    [UserRole(Role.Editor)]
    public async Task GraphLegend_HideTypes_RemovesNodesUntilShownAgain()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        var legend = graph.Legend;
        await graph.OpenAsync(("focus", tree.Subject));
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await legend.OpenAsync();
        await Expect(legend.PressedChips).ToHaveCountAsync(7);

        await legend.ChipTitled("Hide executions").ClickAsync();
        await legend.ExpectShownAsync(PlatformData.Execution, "Executions", shown: false);
        await Expect(legend.Count(PlatformData.Execution)).ToHaveTextAsync("1");
        await Expect(legend.AllTypesButton).ToBeVisibleAsync();
        await legend.CloseAsync();
        await graph.ExpectFocusSummaryAsync(4, tree.Subject);
        await ExpectTypesButtonOnPhoneAsync(legend, 6);

        await graph.ReloadCatalogAsync();
        await legend.OpenAsync();
        await legend.ExpectShownAsync(PlatformData.Execution, "Executions", shown: false);
        await legend.CloseAsync();
        await ExpectTypesButtonOnPhoneAsync(legend, 6);
        await graph.ExpectFocusSummaryAsync(4, tree.Subject);

        await legend.HideAllAsync();
        await legend.CloseAsync();
        await Expect(graph.AllHiddenState).ToBeVisibleAsync();
        await Expect(graph.AllHiddenDescription).ToBeVisibleAsync();
        await graph.ExpectFocusSummaryAsync(0, tree.Subject);
        await ExpectTypesButtonOnPhoneAsync(legend, 0);

        // The click would fail its actionability check if the state covered the legend (or the Types button).
        await legend.OpenAsync();
        await legend.ChipTitled("Show contexts").ClickAsync();
        await legend.ExpectShownAsync(PlatformData.Context, "Contexts", shown: true);
        await legend.CloseAsync();
        await Expect(graph.AllHiddenState).ToHaveCountAsync(0);
        await graph.ExpectFocusSummaryAsync(1, tree.Subject);

        await legend.OpenAsync();
        await legend.AllTypesButton.ClickAsync();
        await Expect(legend.PressedChips).ToHaveCountAsync(7);
        await Expect(legend.AllTypesButton).ToHaveCountAsync(0);
        await legend.CloseAsync();
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
    }

    // GRA-LEG-02: Enter on a focused legend chip toggles its type while a node is open.
    // With a node's detail open beside the graph, Enter on a focused chip presses the chip: it hides and shows its type
    // and the detail stays open. The graph opens its cursor's node on Enter only when the key reaches the graph itself
    // (the 424-130 regression).
    [Test]
    [DesktopOnly]
    [TestCase(59)]
    [UserRole(Role.Editor)]
    public async Task GraphLegend_EnterOnChip_TogglesTypeWithNodeOpen()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        var detail = new GraphNodeDetail(App, Routes);
        await detail.OpenAsync(tree.Subject, ("focus", tree.Subject));
        await Expect(detail.Identity).ToHaveTextAsync(tree.Subject);
        await graph.WaitForCatalogAsync();
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await graph.Legend.ExpectShownAsync(PlatformData.Subject, "Subjects", shown: true);

        // The chip stays the same element (keyed by its type), so it keeps the focus across the toggle.
        await graph.Legend.ChipTitled("Hide subjects").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await graph.Legend.ExpectShownAsync(PlatformData.Subject, "Subjects", shown: false);
        await graph.ExpectFocusSummaryAsync(4, tree.Subject);
        await App.ExpectAtAsync(Routes.GraphNode(tree.Subject));
        await App.ExpectQueryAsync("focus", tree.Subject);
        await Expect(detail.Identity).ToHaveTextAsync(tree.Subject);

        await Page.Keyboard.PressAsync("Enter");
        await graph.Legend.ExpectShownAsync(PlatformData.Subject, "Subjects", shown: true);
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await App.ExpectAtAsync(Routes.GraphNode(tree.Subject));
        await App.ExpectQueryAsync("focus", tree.Subject);
        await Expect(detail.Identity).ToHaveTextAsync(tree.Subject);
    }

    // On a phone, "Types" counts the shown types while some are hidden.
    private async Task ExpectTypesButtonOnPhoneAsync(GraphLegend legend, int shown)
    {
        if (legend.IsCollapsed)
        {
            await legend.ExpectTypesButtonAsync(shown);
        }
    }
}
