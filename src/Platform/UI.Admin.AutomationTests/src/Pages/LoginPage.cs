using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /login, the public page that starts the sign-in. Signed out it offers "Continue with AuthKit" (a redirect to the
// AuthKit hosted page); signed in it says so and links back with "Continue".
public sealed class LoginPage(IPage page)
{
    public ILocator Heading => page.GetByRole(AriaRole.Heading, new() { Name = "Sign in to 2S Admin" });

    public ILocator ContinueWithAuthKit => page.GetByRole(AriaRole.Button, new() { Name = "Continue with AuthKit" });

    // Shown instead of ContinueWithAuthKit when the browser is signed in already.
    public ILocator Continue => page.GetByRole(AriaRole.Link, new() { Name = "Continue", Exact = true });

    public async Task OpenAsync()
    {
        await page.GotoAsync("login");
        await Expect(Heading).ToBeVisibleAsync();
    }
}
