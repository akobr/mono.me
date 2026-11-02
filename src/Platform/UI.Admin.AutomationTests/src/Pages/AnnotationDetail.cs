using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// …/annotations/<key>: the detail of an annotation over the list (an overlay by default on a wide screen, full screen
// on a phone, where it hides the navbar until "Back to the list"); the graph shows the same detail at …/graph/<key>
// (section GraphSection). Its identity line (test id annotation-identity) shows the type's icon and the key, in the
// Form mode only; an unknown key shows "Annotation not found". The header has the type, the Name as its heading, the key
// with "Copy to clipboard", "More actions" (Show in graph or Show in list, Copy link, Copy key and, for Contributors
// and up, Delete), the panel's pin and full-screen buttons and "Close the detail" (from md). Deleting asks "Delete
// <key>?" (no typed confirmation), then the toast "<key> deleted" with "Undo" (10 seconds) and the list.
public sealed partial class AnnotationDetail(App app, AppRoutes routes, string section = AnnotationDetail.AnnotationsSection)
{
    public const string AnnotationsSection = "annotations";

    public const string GraphSection = "graph";

    // The panel: section#annotations-detail or #graph-detail, a region named by the annotation's Name (or its key while
    // it loads).
    public DetailPanel Panel { get; } = new(app, $"{section}-detail");

    // Its text is the key.
    public ILocator Identity => app.Page.GetByTestId("annotation-identity");

    public ILocator Title => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Title", Exact = true });

    // The header's heading: the annotation's Name (the last part of its key).
    public ILocator Heading => Panel.Root.GetByRole(AriaRole.Heading, new() { Level = 2 }).First;

    // The empty state of a key that does not exist (and is not deleted) in the view.
    public ILocator NotFound => Panel.Root.GetByRole(AriaRole.Heading, new() { Name = "Annotation not found", Exact = true });

    public ILocator MoreActionsButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "More actions", Exact = true });

    // From md (768 px); below it BackToTheListButton.
    public ILocator CloseButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Close the detail", Exact = true });

    public ILocator BackToTheListButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Back to the list", Exact = true });

    // The list of the section, where the detail returns to.
    private string ListPath => section == GraphSection ? routes.Graph : routes.Annotations;

    // An item of the open "More actions" menu: "Show in graph", "Show in list", "Copy link", "Copy key", "Delete".
    public ILocator MenuItem(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { Name = name, Exact = true });
    }

    // The confirmation of Delete, "Delete <key>?", with the description of what moves to Deleted.
    public ConfirmDialog DeleteDialog(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new ConfirmDialog(app, $"Delete {key}?");
    }

    // Opens the detail by its URL, without the list's grid; on a phone the navbar stays hidden, so this waits for
    // nothing.
    public async Task OpenAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await app.GotoAsync(section == GraphSection ? routes.GraphNode(key) : routes.Annotation(key));
    }

    // Opens "More actions" and picks the item.
    public async Task MoreActionsAsync(string item)
    {
        await MoreActionsButton.ClickAsync();
        await MenuItem(item).ClickAsync();
    }

    // Deletes the annotation of the open detail (with its descendants): More actions, Delete, then Delete in "Delete
    // <key>?". Returns once the toast "<key> deleted" showed (its "Undo": App.ToastAction) and the list is back.
    public async Task DeleteAsync(string key)
    {
        await MoreActionsAsync("Delete");
        await DeleteDialog(key).ConfirmAsync("Delete");
        await Expect(app.Toast($"{key} deleted")).ToBeVisibleAsync();
        await app.ExpectAtAsync(ListPath);
    }

    // Leaves the detail for the list: "Close the detail" from md, "Back to the list" below.
    public async Task CloseAsync()
    {
        await Panel.CloseAsync();
        await app.ExpectAtAsync(ListPath);
    }
}
