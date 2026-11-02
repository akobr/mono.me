using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /login, the public page that starts the sign-in. Signed out it offers "Continue with AuthKit" (a redirect to the
// AuthKit hosted page); signed in it says so and links back with "Continue". Opened through App.GotoAsync, so as the
// first navigation of a test it carries the mock scenario like every other page.
public sealed partial class LoginPage(App app)
{
    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in to 2S Admin" });

    public ILocator ContinueWithAuthKit => app.Page.GetByRole(AriaRole.Button, new() { Name = "Continue with AuthKit" });

    // Shown instead of ContinueWithAuthKit when the browser is signed in already.
    public ILocator Continue => app.Page.GetByRole(AriaRole.Link, new() { Name = "Continue", Exact = true });

    // path: the login route, by default plain /login; AppRoutes.Login(returnTo, error, …) adds its query.
    public async Task OpenAsync(string path = "login")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await app.GotoAsync(path);
        await Expect(Heading).ToBeVisibleAsync();
    }
}
