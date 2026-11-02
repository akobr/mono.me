using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Graph;

// Focus and Filter: focus mode (?focus, ?depth), the key the Focus button starts from, the Focus depth spinbutton,
// Show everything and browser Back; the filter, which adds its matches in focus mode and only dims nodes in the whole
// view.
[Area(Area.Graph)]
public sealed partial class GraphFocusTests : PlatformTest
{
    // GRA-FOC-01: Focus starts from the last opened annotation, the depth changes the neighbourhood and Show
    // everything leaves it.
    // The detail records every opened key; once it is closed, Focus starts from the last one (the context of a new
    // tree in a fresh browser) and draws its neighbourhood: 3 annotations at depth 1, 4 at depth 2 and 5 at depth 3.
    // The depth replaces the URL, Focus and Show everything push it, so Back returns to the focus with its depth.
    [Test]
    [TestCase(60)]
    [UserRole(Role.Editor)]
    public async Task GraphFocus_FocusAndDepth_ChangeNeighbourhood()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        var detail = new GraphNodeDetail(App, Routes);
        await detail.OpenAsync(tree.Context);
        await Expect(detail.Identity).ToHaveTextAsync(tree.Context);

        await detail.CloseAsync();
        await graph.WaitForCatalogAsync();

        // A view above 5,000 annotations (a grown deployed target) focused itself on the selected key already.
        if (!await graph.IsLargeViewAsync())
        {
            await App.ExpectQueryAsync("focus", null);
            await Expect(graph.FocusButton).ToBeVisibleAsync();
            await Expect(graph.DepthInput).ToHaveCountAsync(0);
            await graph.FocusButton.ClickAsync();
        }

        await App.ExpectQueryAsync("focus", tree.Context);
        await graph.ExpectFocusSummaryAsync(4, tree.Context);
        await Expect(graph.FocusBadge(tree.Context)).ToHaveTextAsync(tree.Context);
        await Expect(graph.DepthInput).ToHaveValueAsync("2");
        await Expect(graph.ShowEverythingButton).ToBeVisibleAsync();
        await Expect(graph.FocusButton).ToHaveCountAsync(0);

        await graph.DecrementDepthButton.ClickAsync();
        await App.ExpectQueryAsync("depth", "1");
        await graph.ExpectFocusSummaryAsync(3, tree.Context, depth: 1);

        await graph.IncrementDepthButton.ClickAsync();
        await App.ExpectQueryAsync("depth", null);
        await graph.IncrementDepthButton.ClickAsync();
        await App.ExpectQueryAsync("depth", "3");
        await graph.ExpectFocusSummaryAsync(5, tree.Context, depth: 3);

        await graph.ShowEverythingAsync();
        await App.ExpectQueryAsync("focus", null);
        await Expect(graph.FocusButton).ToBeVisibleAsync();
        await Expect(graph.DepthInput).ToHaveCountAsync(0);
        await Expect(graph.FocusBadge(tree.Context)).ToHaveCountAsync(0);
        await graph.ExpectWholeViewSummaryAsync(await graph.TotalAsync());

        await Page.GoBackAsync();
        await App.ExpectQueryAsync("focus", tree.Context);
        await App.ExpectQueryAsync("depth", "3");
        await graph.ExpectFocusSummaryAsync(5, tree.Context, depth: 3);
    }

    // GRA-FOC-02: The filter adds its matches to the focus and keeps every node of the whole view.
    // In focus mode the filter's matches join the neighbourhood (an unconnected responsibility makes it 6, "+ filter
    // matches", and the legend counts it); in the whole view the filter only dims the nodes that do not match, so the
    // summary still counts every annotation of the view, the total of the filter's count.
    [Test]
    [TestCase(62)]
    [UserRole(Role.Editor)]
    public async Task GraphFocus_Filter_AddsMatchesAndKeepsWholeView()
    {
        var unconnected = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var tree = await CreateGraphTreeAsync("Created 6 annotations: 2 from the list and 4 missing ancestors.", unconnected);

        var graph = new GraphPage(App, Routes);
        await graph.OpenAsync(("focus", tree.Subject));
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await Expect(graph.FilterCount).ToHaveTextAsync(TotalCount());

        // "/" focuses the filter from the page body.
        var query = $"key:{unconnected}";
        await Page.Keyboard.PressAsync("/");
        await Expect(graph.Filter).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync(query);
        await App.ExpectQueryAsync("q", query);
        await Expect(graph.FilterCount).ToHaveTextAsync(OneMatchCount());
        await graph.ExpectFocusSummaryAsync(6, tree.Subject, withFilter: true);
        await graph.Legend.OpenAsync();
        await Expect(graph.Legend.Count(PlatformData.Responsibility)).ToHaveTextAsync("2");
        await graph.Legend.CloseAsync();

        await graph.ShowEverythingAsync();
        await App.ExpectQueryAsync("q", query);
        await App.ExpectQueryAsync("focus", null);
        await Expect(graph.FilterCount).ToHaveTextAsync(OneMatchCount());
        await graph.ExpectWholeViewSummaryAsync(await graph.TotalAsync());

        await graph.ClearFilterButton.ClickAsync();
        await App.ExpectQueryAsync("q", null);
        await Expect(graph.FilterCount).ToHaveTextAsync(TotalCount());
        await graph.ExpectWholeViewSummaryAsync(await graph.TotalAsync());
    }

    // The filter's count without a filter: the view's total T.
    [GeneratedRegex(@"^\s*[\d,]+\s*$")]
    private static partial Regex TotalCount();

    // The filter's count with one match: "1 of T".
    [GeneratedRegex(@"^\s*1 of [\d,]+\s*$")]
    private static partial Regex OneMatchCount();
}
