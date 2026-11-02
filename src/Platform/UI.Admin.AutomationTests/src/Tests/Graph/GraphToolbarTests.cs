using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using TUnit.Assertions.Enums;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Graph;

// Toolbar: Layout (Layered, Force, Radial), Fit, Reheat, Export PNG, and Open as list with the drawn annotations.
[Area(Area.Graph)]
public sealed class GraphToolbarTests : PlatformTest
{
    // The first 8 bytes of every PNG file.
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // GRA-TBR-01: Layout, Fit and Reheat redraw the graph without changing its annotations, and Export PNG downloads
    // it.
    // The layouts only move the nodes: the URL follows the choice (layout=radial or force, none for Layered) and the
    // focused tree keeps its five annotations; Fit and Reheat change only canvas pixels, so the URL and the summary stay
    // and no toast shows. Export PNG downloads graph-<org>-<project>-<view>.png, a real PNG.
    [Test]
    [TestCase(91)]
    [UserRole(Role.Editor)]
    public async Task GraphToolbar_LayoutAndExport_KeepNodesAndDownloadPng()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        await graph.OpenAsync(("focus", tree.Subject), ("layout", "radial"));
        await Expect(graph.LayoutSelect).ToHaveTextAsync("Radial");
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);

        await graph.SelectLayoutAsync("Force");
        await App.ExpectQueryAsync("layout", "force");
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);

        await graph.SelectLayoutAsync("Layered");
        await App.ExpectQueryAsync("layout", null);
        await Expect(graph.LayoutSelect).ToHaveTextAsync("Layered");
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);

        await graph.FitButton.ClickAsync();
        await graph.ReheatButton.ClickAsync();
        await App.ExpectQueryAsync("focus", tree.Subject);
        await App.ExpectQueryAsync("layout", null);
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await Expect(App.Notifications.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(0);

        await graph.WaitForLayoutAsync();
        var png = await DownloadAsync(() => graph.ExportPngButton.ClickAsync(), 30_000);
        await Expect(graph.ExportFailedToast).ToHaveCountAsync(0);
        await Assert.That(png.SuggestedFilename).IsEqualTo($"graph-{Routes.Organization}-{Routes.Project}-{Routes.View}.png");
        await Assert.That(png.Content.Length).IsGreaterThan(PngSignature.Length);
        await Assert.That(png.Content.Take(PngSignature.Length).ToArray()).IsEquivalentTo(PngSignature, CollectionOrdering.Matching);
    }

    // GRA-TBR-02: Open as list shows the drawn annotations in the grid.
    // Open as list opens the annotations filtered by key: with the drawn keys, so a hidden type's annotations are not
    // there: the tree without its execution gives four rows (items on a phone) and the count "4 of T".
    [Test]
    [TestCase(94)]
    [UserRole(Role.Editor)]
    public async Task GraphToolbar_OpenAsList_ShowsDrawnAnnotationsInGrid()
    {
        var tree = await CreateGraphTreeAsync();

        var graph = new GraphPage(App, Routes);
        await graph.OpenAsync(("focus", tree.Subject));
        await graph.ExpectFocusSummaryAsync(5, tree.Subject);
        await Expect(graph.OpenAsListButton).ToBeEnabledAsync();

        await graph.Legend.ToggleAsync(PlatformData.Execution);
        await graph.ExpectFocusSummaryAsync(4, tree.Subject);

        await graph.OpenAsListButton.ClickAsync();
        await App.ExpectAtAsync(Routes.Annotations);
        await Page.WaitForFunctionAsync("() => (new URLSearchParams(location.search).get('q') ?? '').startsWith('key:')", null, new() { PollingInterval = 100 });
        var query = await Page.EvaluateAsync<string>("() => new URLSearchParams(location.search).get('q')");

        // The drawn keys are the execution's ancestors, in the order of the graph's nodes.
        await Assert.That(query["key:".Length..].Split(',')).IsEquivalentTo(tree.Ancestors, CollectionOrdering.Any);

        var annotations = new AnnotationsPage(App, Routes);
        await annotations.WaitForCatalogAsync();
        await Expect(annotations.Filter).ToHaveValueAsync(query);
        await Expect(annotations.FilterCount).ToHaveTextAsync(GraphPage.FilterCountText(4));
        foreach (var key in tree.Ancestors)
        {
            await Expect(annotations.Item(key)).ToBeVisibleAsync();
        }

        await Expect(annotations.Item(tree.Execution)).ToHaveCountAsync(0);
    }
}
