using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Graph;

// Remembered State: the graph's page memory per view: the last URL and the hidden types come back through the
// navigation, selecting Graph again starts over, and a page load forgets them.
[Area(Area.Graph)]
public sealed class GraphMemoryTests : PlatformTest
{
    // GRA-MEM-01: Coming back to Graph through the navigation restores its URL state and hidden types until a reload.
    // The plain Graph link from Annotations returns to the remembered URL (focus, layout) with the hidden type; the
    // same link on Graph itself starts over (no focus, Layered) but keeps the hidden type; a page load forgets the
    // memory. Between leaving and starting over the test moves only inside the app (App.NavigateAsync), because a page
    // load (App.GotoAsync) clears the memory.
    [Test]
    [TestCase(138)]
    [UserRole(Role.Editor)]
    public async Task GraphMemory_SidebarReturn_RestoresStateUntilReload()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        var legend = graph.Legend;
        await graph.OpenAsync(("focus", tree.Subject));
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);

        await graph.SelectLayoutAsync("Force");
        await legend.ToggleAsync(PlatformData.Context);
        await App.ExpectQueryAsync("layout", "force");
        await graph.ExpectFocusSummaryAsync(4, tree.Subject);

        await App.NavigateAsync("Annotations");
        await App.ExpectAtAsync(Routes.Annotations);
        await Expect(new AnnotationsPage(App, Routes).Filter).ToHaveValueAsync(string.Empty);

        await App.NavigateAsync("Graph");
        await App.ExpectAtAsync(Routes.Graph);
        await App.ExpectQueryAsync("focus", tree.Subject);
        await App.ExpectQueryAsync("layout", "force");
        await Expect(graph.LayoutSelect).ToHaveTextAsync("Force");
        await ExpectContextsHiddenAsync(legend);
        await graph.ExpectFocusSummaryAsync(4, tree.Subject);

        // From Graph itself the plain link starts over. A view above 5,000 annotations (a grown deployed target) then
        // focuses by itself on its most connected root, so only the layout and the hidden type are checked there.
        await App.NavigateAsync("Graph");
        await App.ExpectAtAsync(Routes.Graph);
        await App.ExpectQueryAsync("layout", null);
        await Expect(graph.LayoutSelect).ToHaveTextAsync("Layered");
        if (!await graph.IsLargeViewAsync())
        {
            await App.ExpectQueryAsync("focus", null);
            await Expect(graph.FocusButton).ToBeVisibleAsync();
            await Expect(graph.ShowEverythingButton).ToHaveCountAsync(0);
        }

        await ExpectContextsHiddenAsync(legend);

        await graph.OpenAsync(("focus", tree.Subject));
        await legend.OpenAsync();
        await Expect(legend.PressedChips).ToHaveCountAsync(7);
        await legend.CloseAsync();
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
    }

    // The chip of the contexts is not pressed ("Show contexts"); on a phone "Types" counts 6 of 7 types too.
    private static async Task ExpectContextsHiddenAsync(GraphLegend legend)
    {
        if (legend.IsCollapsed)
        {
            await legend.ExpectTypesButtonAsync(6);
        }

        await legend.OpenAsync();
        await legend.ExpectShownAsync(PlatformData.Context, "Contexts", shown: false);
        await legend.CloseAsync();
    }
}
