using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// …/annotations/<key>: the detail of an annotation over the list (an overlay by default on a wide screen, full screen
// on a phone, where it hides the navbar until "Back to the list"). Its identity line (test id annotation-identity)
// shows the type's icon and the key, in the Form mode only; an unknown key shows "Annotation not found".
public sealed class AnnotationDetail(App app, AppRoutes routes)
{
    // Its text is the key.
    public ILocator Identity => app.Page.GetByTestId("annotation-identity");

    public ILocator Title => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Title", Exact = true });

    public ILocator BackToTheListButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Back to the list", Exact = true });

    // Opens the detail by its URL, without the list's grid; on a phone the navbar stays hidden, so this waits for
    // nothing.
    public async Task OpenAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await app.GotoAsync(routes.Annotation(key));
    }
}
