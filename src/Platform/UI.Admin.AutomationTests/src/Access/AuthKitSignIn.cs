using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Access;

// Target:SignIn AuthKit, the real sign-in of a deployed admin UI; the runtime runs it once per pool user per session
// and gives the user's later tests the captured storage state. /login hands over to authkit-js, which redirects to the
// AuthKit hosted page; the e-mail and the password go there, and AuthKit returns to /callback, where the app exchanges
// the code and opens the shell. The hosted page is WorkOS' own UI, not the admin UI's: its steps use WorkOS' labels
// (Email, Continue, Password, Sign in), so a change on WorkOS' side fails SignIn_ShowsOrganizations first. The AuthKit
// environment needs password authentication, no MFA for the pool users, verified e-mails and the admin host's
// /callback as a redirect URI (README.md, "AuthKit checklist").
//
// authkit-js refreshes the session on every page load and AuthKit rotates the refresh token, so the runtime carries
// the cookies (the session on the auth domain) and the workos:* items (the tokens in local storage on localhost) of a
// test that ended signed in over to the user's next test; the admin UI's own 2s-admin:* preferences stay as captured
// right after the sign-in.
//
// Target:SignIn None (the mock target) uses no flow: the mock build has Ada Lovelace signed in in every new context.
public sealed class AuthKitSignIn : ISignInFlow
{
    public const string FlowName = "AuthKit";

    // The keys authkit-js keeps in local storage (devMode, on localhost): workos:refresh-token:<client id> and others.
    private const string StoragePrefix = "workos:";

    public string Name => FlowName;

    public async Task SignInAsync(IPage page, TestUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(user);

        var password = user.Password;
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"{user} has no password; set Users:Pool:{user.Index}:Password in the user secrets.");
        }

        var login = new LoginPage(page);
        await login.OpenAsync();
        var app = new Uri(page.Url);
        await login.ContinueWithAuthKit.ClickAsync();

        // The AuthKit hosted page, on the AuthKit domain: the e-mail first, then the password.
        await page.WaitForURLAsync(url => !IsOnApp(url, app));
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Email" }).FillAsync(user.Email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();

        // Back on the app, past /callback, which replaces itself with the return path.
        await page.WaitForURLAsync(url => IsOnApp(url, app) && !IsPage(url, "callback") && !IsPage(url, "login"));
        await Expect(App.UserMenuOf(page)).ToBeVisibleAsync();
    }

    // Signed out: on /login, or sent to another host (the AuthKit hosted page) by a protected route.
    public ValueTask<bool> IsSignedOutAsync(IPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return ValueTask.FromResult(false);
        }

        var signedOut = IsPage(url.ToString(), "login")
            || (Uri.TryCreate(UiAutomationSession.LoadedSettings?.Target.BaseUrl, UriKind.Absolute, out var app) && !IsOnApp(url.ToString(), app));
        return ValueTask.FromResult(signedOut);
    }

    public bool IsSignInStorageItem(string origin, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(StoragePrefix, StringComparison.Ordinal);
    }

    private static bool IsOnApp(string url, Uri app)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var current)
            && string.Equals(current.GetLeftPart(UriPartial.Authority), app.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPage(string url, string page)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var current)
            && current.AbsolutePath.TrimEnd('/').EndsWith($"/{page}", StringComparison.OrdinalIgnoreCase);
    }
}
