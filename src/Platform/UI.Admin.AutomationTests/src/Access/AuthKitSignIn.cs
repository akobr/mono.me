using _42.Platform.Automation.Shared.Access;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Access;

// Target:SignIn AuthKit, the real sign-in of a deployed admin UI (the shared AuthKitSignInFlow of
// 42.Platform.Automation.Shared); the runtime runs it once per pool user per session and gives the user's later tests the
// captured storage state. /login hands over to authkit-js, which redirects to the AuthKit hosted page; the e-mail and the
// password go there (AuthKitHostedPage, with WorkOS' labels, so a change on WorkOS' side fails SignIn_ShowsOrganizations
// first), and AuthKit returns to /callback, where the app exchanges the code and opens the shell. The AuthKit
// environment needs password authentication, no MFA for the pool users, verified e-mails and the admin host's /callback
// as a redirect URI (docs/Platform/UI.Admin.AutomationTests/automation-tests.md, "AuthKit checklist").
//
// authkit-js refreshes the session on every page load and AuthKit rotates the refresh token, so the runtime carries
// the cookies (the session on the auth domain) and the workos:* items (the tokens in local storage on localhost) of a
// test that ended signed in over to the user's next test; the admin UI's own 2s-admin:* preferences stay as captured
// right after the sign-in.
//
// Target:SignIn None (the mock target) uses no flow: the mock build has Ada Lovelace signed in in every new context.
public sealed class AuthKitSignIn : AuthKitSignInFlow
{
    public override async Task SignInAsync(IPage page, TestUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(user);

        var password = AuthKitHostedPage.PasswordOf(user);

        // A deployed target has no mock scenario, so a plain App opens the page.
        var login = new LoginPage(new App(page));
        await login.OpenAsync();
        var app = new Uri(page.Url);
        await login.ContinueWithAuthKit.ClickAsync();

        // The AuthKit hosted page, on the AuthKit domain: the e-mail first, then the password.
        await page.WaitForURLAsync(url => !AuthKitHostedPage.IsOnHost(url, app));
        await AuthKitHostedPage.EnterCredentialsAsync(page, user.Email, password);

        // Back on the app, past /callback, which replaces itself with the return path.
        await page.WaitForURLAsync(url => AuthKitHostedPage.IsOnHost(url, app) && !IsPage(url, "callback") && !IsPage(url, "login"));
        await Expect(App.UserMenuOf(page)).ToBeVisibleAsync();
    }

    // Signed out: on /login, or sent to another host (the AuthKit hosted page) by a protected route.
    public override ValueTask<bool> IsSignedOutAsync(IPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return ValueTask.FromResult(false);
        }

        var signedOut = IsPage(url.ToString(), "login")
            || (Uri.TryCreate(UiAutomationSession.LoadedSettings?.Target.BaseUrl, UriKind.Absolute, out var app) && !AuthKitHostedPage.IsOnHost(url.ToString(), app));
        return ValueTask.FromResult(signedOut);
    }

    private static bool IsPage(string url, string page)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var current)
            && current.AbsolutePath.TrimEnd('/').EndsWith($"/{page}", StringComparison.OrdinalIgnoreCase);
    }
}
