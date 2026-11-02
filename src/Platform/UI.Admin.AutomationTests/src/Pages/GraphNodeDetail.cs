using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// …/<view>/graph/<key>: the annotation detail of a node beside the graph (AnnotationDetail in its graph section,
// section#graph-detail): a 40 % overlay from lg, 90 % from md, full screen below md, where the graph is hidden. What the
// graph adds: its links stay on the graph and keep the graph's query (focus, layout, …). The tabs "Annotation" and
// "History" (…/graph/<key>/history) are links; the breadcrumb "Annotation ancestors" links each ancestor of the
// subject perspective ("Subject <s>", "Context <c>") and ends in the current one ("Execution <c>"); "Lineage" opens a
// popover "Ancestors, applied top to bottom" whose links read the full keys. "More actions" has "Show in list" (to
// …/annotations/<key>, no query) instead of the list's "Show in graph".
public sealed partial class GraphNodeDetail(App app, AppRoutes routes)
{
    public AnnotationDetail Detail { get; } = new(app, routes, AnnotationDetail.GraphSection);

    public DetailPanel Panel => Detail.Panel;

    public ILocator Identity => Detail.Identity;

    public ILocator AnnotationTab => Detail.AnnotationTabLink;

    public ILocator HistoryTab => Detail.HistoryTabLink;

    // The content of the History tab.
    public ILocator HistoryRegion => Panel.Root.GetByRole(AriaRole.Region, new() { Name = "History", Exact = true });

    public ILocator Breadcrumb => Detail.AncestorsBreadcrumb;

    // The breadcrumb's last item, the annotation itself ("Execution <c>").
    public ILocator BreadcrumbCurrent => Detail.AncestorsBreadcrumbCurrent;

    public ILocator LineageButton => Detail.AncestorLineageButton;

    // The open popover of Lineage.
    public ILocator Lineage => Detail.AncestorLineage;

    public ILocator LineageHeading => Lineage.GetByText(AnnotationDetail.LineageHeadingText, new() { Exact = true });

    // "<key> does not exist in this view. It may have been deleted permanently."
    public ILocator NotFoundDescription(string key)
    {
        return Detail.NotFoundDescription(key);
    }

    // A link of the breadcrumb by its text, the type label and the last key part: "Subject <s>", "Context <c>".
    public ILocator BreadcrumbLink(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Breadcrumb.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
    }

    // A link of the open Lineage popover by the ancestor's key.
    public ILocator LineageLink(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Lineage.GetByRole(AriaRole.Link, new() { Name = key, Exact = true });
    }

    // Opens the node by its URL (a page load) with the graph's query, e.g. ("focus", key); on a phone the navbar stays
    // hidden behind the detail, so this waits for nothing.
    public async Task OpenAsync(string key, params (string Name, string? Value)[] query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await app.GotoAsync(AppRoutes.WithQuery(routes.GraphNode(key), query));
    }

    // Opens the Lineage popover.
    public async Task OpenLineageAsync()
    {
        await LineageButton.ClickAsync();
        await Expect(LineageHeading).ToBeVisibleAsync();
    }

    // Leaves the detail for the graph ("Close the detail" from md, "Back to the list" below), which keeps the query.
    public async Task CloseAsync()
    {
        await Detail.CloseAsync();
    }
}
