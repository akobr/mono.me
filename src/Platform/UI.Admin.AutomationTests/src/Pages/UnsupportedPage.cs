using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /unsupported (public, bare layout): what the app says about its configuration. A working deployment (and the mock)
// shows "Everything is fine", "The application is configured." and the link "Open the app" to '/'; a deployment with
// another sign-in provider or a broken configuration shows what is wrong and "Try again" instead.
public sealed partial class UnsupportedPage(App app, AppRoutes routes)
{
    public const string ConfiguredTitle = "Everything is fine";

    public const string ConfiguredText = "The application is configured.";

    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { Level = 1 });

    public ILocator Description => app.Page.GetByText(ConfiguredText, new() { Exact = true });

    public ILocator OpenTheAppLink => app.Page.GetByRole(AriaRole.Link, new() { Name = "Open the app", Exact = true });

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Unsupported);
        await Expect(Heading).ToBeVisibleAsync();
    }
}
