using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The detail beside a list (components/shell/DetailPanel.vue): one section, a region named by the detail's title, with
// data-detail-mode "overlay", "pinned" or "full". Below md (768 px) it is always full screen with a back button, and
// the annotations, configurations and modeling hide the list behind it; from md it is an overlay below the navbar (90 %
// wide up to lg, then the remembered width), with "Open in full screen"; from lg (1024 px) it can be pinned beside the
// list ("Pin the detail") and resized by its left border ("Resize the detail", 25 to 70 %). Its header closes it:
// "Close the detail" (from md) or "Back to the list" (below) for annotations, configurations and key values, "Close" and
// "Back" for schemas and templates. Escape on the page body or in the panel, not in a control, closes it too. The mode
// and the width are preferences of the browser (localStorage), so they apply to every detail opened after.
// The ids: annotations-detail, graph-detail, configuration-detail, key-value-detail, schema-detail, template-detail
// and machine-detail. The key-value detail has no pin and full-screen buttons.
public sealed partial class DetailPanel(App app, string id)
{
    // Below this width (md) a detail is always full screen.
    public const int OverlayWidth = 768;

    // From this width (lg) a detail can be pinned and resized.
    public const int PinnedWidth = 1024;

    public const string OverlayMode = "overlay";
    public const string PinnedMode = "pinned";
    public const string FullMode = "full";

    public ILocator Root => app.Page.Locator($"section#{id}");

    public ILocator PinButton => Root.GetByRole(AriaRole.Button, new() { Name = "Pin the detail", Exact = true });

    public ILocator UnpinButton => Root.GetByRole(AriaRole.Button, new() { Name = "Unpin the detail", Exact = true });

    public ILocator FullScreenButton => Root.GetByRole(AriaRole.Button, new() { Name = "Open in full screen", Exact = true });

    public ILocator ExitFullScreenButton => Root.GetByRole(AriaRole.Button, new() { Name = "Exit full screen", Exact = true });

    // aria-valuenow is the width in percent of the list's group.
    public ILocator ResizeSeparator => Root.GetByRole(AriaRole.Separator, new() { Name = "Resize the detail", Exact = true });

    // From md: "Close the detail", or "Close" in a schema or a template.
    public ILocator CloseButton => Root.GetByRole(AriaRole.Button, new() { NameRegex = CloseName() });

    // Below md: "Back to the list", or "Back" in a schema or a template.
    public ILocator BackButton => Root.GetByRole(AriaRole.Button, new() { NameRegex = BackName() });

    // The button that leaves the detail on this viewport: Back below md, Close from md.
    public ILocator LeaveButton => app.Page.ViewportSize is { Width: < OverlayWidth } ? BackButton : CloseButton;

    public async Task ExpectModeAsync(string mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        await Expect(Root).ToHaveAttributeAsync("data-detail-mode", mode);
    }

    // Leaves the detail with the button of the viewport (LeaveButton) and waits until it is gone; the page goes back to
    // its list, with the list's query. A dirty editor asks "Discard changes?" first (DiscardChangesDialog): click
    // LeaveButton for that instead.
    public async Task CloseAsync()
    {
        await LeaveButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    [GeneratedRegex("^(Close the detail|Close)$")]
    private static partial Regex CloseName();

    [GeneratedRegex("^(Back to the list|Back)$")]
    private static partial Regex BackName();
}
