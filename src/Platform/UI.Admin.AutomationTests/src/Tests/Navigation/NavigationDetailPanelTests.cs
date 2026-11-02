using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Navigation;

// Detail Panel: the frame of a detail beside its list (components/shell/DetailPanel.vue), shown on an annotation the
// test creates. From 768 px it is an overlay that can go full screen, from 1024 px it can be pinned beside the list and
// resized by keyboard; below 768 px it always covers the list. The mode and the width are preferences of the browser,
// so they apply to every detail opened after, and every test starts from a fresh context: an overlay 40 % wide.
[Area(Area.Navigation)]
public sealed class NavigationDetailPanelTests : PlatformTest
{
    // NAV-DET-01: The detail pins, resizes by keyboard and keeps its mode for every detail.
    // A new responsibility's detail opens as an overlay 40 % wide; pinned, its separator widens it by keyboard and a
    // double-click resets it, Escape on the separator closes it with the list's filter kept, and the pinned mode comes
    // back for the annotation and for a template of Modeling after a reload, until it is unpinned there.
    [Test]
    [TestCase(142)]
    [UserRole(Role.Editor)]
    [DesktopOnly]
    public async Task DetailPanel_PinAndResize_KeptForEveryDetail()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = await OpenCreatedAnnotationAsync(key);
        var panel = new AnnotationDetail(App, Routes).Panel;
        await panel.ExpectModeAsync(DetailPanel.OverlayMode);
        await Expect(panel.ResizeSeparator).ToHaveAttributeAsync("aria-valuenow", "40");

        await panel.PinButton.ClickAsync();
        await panel.ExpectModeAsync(DetailPanel.PinnedMode);
        await Expect(panel.UnpinButton).ToBeVisibleAsync();

        // ArrowLeft widens the detail by 2 % of the list's group.
        await panel.ResizeSeparator.FocusAsync();
        for (var press = 0; press < 5; press++)
        {
            await Page.Keyboard.PressAsync("ArrowLeft");
        }

        await Expect(panel.ResizeSeparator).ToHaveAttributeAsync("aria-valuenow", "50");
        await panel.ResizeSeparator.DblClickAsync();
        await Expect(panel.ResizeSeparator).ToHaveAttributeAsync("aria-valuenow", "40");

        // The separator is inside the panel and no control, so Escape closes the detail there.
        await Expect(panel.ResizeSeparator).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(App.DetailPanels).ToHaveCountAsync(0);
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", $"key:{key}");

        await annotations.Item(key).ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(key));
        await panel.ExpectModeAsync(DetailPanel.PinnedMode);
        await Expect(panel.UnpinButton).ToBeVisibleAsync();

        // Opened by its address, so the mode comes from the browser's preferences, not from the page.
        var templates = new ModelingTemplatePage(App, Routes);
        await templates.OpenAsync(PlatformData.Responsibility);
        await templates.Panel.ExpectModeAsync(DetailPanel.PinnedMode);
        await Expect(templates.Panel.UnpinButton).ToBeVisibleAsync();
        await templates.Panel.UnpinButton.ClickAsync();
        await templates.Panel.ExpectModeAsync(DetailPanel.OverlayMode);
        await Expect(templates.Panel.PinButton).ToBeVisibleAsync();
    }

    // NAV-DET-02: The detail goes full screen and stays so after a reload.
    // A new responsibility's detail in full screen replaces the list (its filter hidden) and has neither the pin nor
    // the separator; reloaded by its address it is still full screen, and back as an overlay the list shows again until
    // "Close the detail" removes the detail.
    [Test]
    [TestCase(143)]
    [UserRole(Role.Editor)]
    [DesktopOnly]
    public async Task DetailPanel_FullScreen_SurvivesReload()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = await OpenCreatedAnnotationAsync(key);
        var detail = new AnnotationDetail(App, Routes);
        var panel = detail.Panel;
        await panel.ExpectModeAsync(DetailPanel.OverlayMode);

        await panel.FullScreenButton.ClickAsync();
        await panel.ExpectModeAsync(DetailPanel.FullMode);
        await Expect(annotations.Filter).ToBeHiddenAsync();
        await Expect(panel.PinButton).ToHaveCountAsync(0);
        await Expect(panel.ResizeSeparator).ToHaveCountAsync(0);
        await Expect(panel.ExitFullScreenButton).ToBeVisibleAsync();

        await App.GotoAsync(Routes.Annotation(key));
        await Expect(detail.Identity).ToHaveTextAsync(key);
        await panel.ExpectModeAsync(DetailPanel.FullMode);

        await panel.ExitFullScreenButton.ClickAsync();
        await panel.ExpectModeAsync(DetailPanel.OverlayMode);
        await Expect(annotations.Filter).ToBeVisibleAsync();

        // The reload opened the detail without the list's filter, so the list comes back without one.
        await detail.CloseButton.ClickAsync();
        await Expect(App.DetailPanels).ToHaveCountAsync(0);
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", null);
    }

    // NAV-DET-03: On a phone the detail covers the list until Back to the list.
    // Below 768 px a new responsibility's detail is always full screen, without the frame's wide-screen buttons and
    // with the list's navbar hidden behind it; "Back to the list" returns to the filtered list, and after a direct load
    // of the detail to the list without a filter.
    [Test]
    [TestCase(144)]
    [UserRole(Role.Editor)]
    [PhoneOnly]
    public async Task DetailPanel_Phone_CoversListUntilBack()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);
        await annotations.FilterAsync($"key:{key}");
        await Expect(annotations.Item(key)).ToBeVisibleAsync();

        var detail = new AnnotationDetail(App, Routes);
        var panel = detail.Panel;
        await annotations.Item(key).ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(key));
        await panel.ExpectModeAsync(DetailPanel.FullMode);
        await Expect(detail.BackToTheListButton).ToBeVisibleAsync();

        // "Close the detail" is in the header but hidden below md; the others are not rendered there.
        await Expect(panel.FullScreenButton).ToHaveCountAsync(0);
        await Expect(panel.PinButton).ToHaveCountAsync(0);
        await Expect(detail.CloseButton).ToBeHiddenAsync();
        await Expect(panel.ResizeSeparator).ToHaveCountAsync(0);
        await Expect(App.OpenNavigationButton).ToBeHiddenAsync();
        await Expect(annotations.Filter).ToBeHiddenAsync();

        await detail.BackToTheListButton.ClickAsync();
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", $"key:{key}");
        await Expect(annotations.Item(key)).ToBeVisibleAsync();
        await Expect(App.OpenNavigationButton).ToBeVisibleAsync();

        await detail.OpenAsync(key);
        await panel.ExpectModeAsync(DetailPanel.FullMode);
        await Expect(detail.BackToTheListButton).ToBeVisibleAsync();

        await detail.BackToTheListButton.ClickAsync();
        await App.ExpectAtAsync(Routes.Annotations);
        await App.ExpectQueryAsync("q", null);
        await Expect(annotations.Filter).ToHaveValueAsync(string.Empty);
    }

    // Opens the annotations, creates the responsibility with Bulk create, filters the list by its key and opens its
    // detail with a click on its row; returns the list's page.
    private async Task<AnnotationsPage> OpenCreatedAnnotationAsync(string key)
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);
        await annotations.FilterAsync($"key:{key}");
        await annotations.Item(key).ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(key));
        return annotations;
    }
}
