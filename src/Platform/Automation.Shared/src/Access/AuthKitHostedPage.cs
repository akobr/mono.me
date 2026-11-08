using System.Text.RegularExpressions;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Platform.Automation.Shared.Access;

// The AuthKit hosted sign-in page of WorkOS, on the AuthKit domain, where a pool user signs in with the e-mail and the
// password: the steps every Platform project takes there, whatever led the browser to it (the admin UI's /login, a
// device code page of sform). It is WorkOS' own UI: its steps use WorkOS' labels (Email, Continue or Continue with
// email, Password, Sign in), so a change on WorkOS' side fails the first sign-in of a run. The AuthKit environment needs
// password authentication, no MFA for the pool users and verified e-mails
// (docs/Platform/UI.Admin.AutomationTests/automation-tests.md, "AuthKit checklist").
public static class AuthKitHostedPage
{
    // The e-mail step's button: "Continue", or "Continue with email" with the social sign-ins; once e-mail was used in
    // the browser, its "Last used" badge joins the accessible name. Without options: Playwright hands it to the page as a
    // JavaScript regular expression.
    public static Regex ContinueButton { get; } = new(@"^Continue( with email)?(\s*Last used)?$");

    // The pool user's password, which the hosted page needs; throws when Users:Pool has none for the user.
    public static string PasswordOf(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var password = user.Password;
        return string.IsNullOrEmpty(password)
            ? throw new InvalidOperationException($"{user} has no password; set Users:Pool:{user.Index}:Password in the user secrets.")
            : password;
    }

    // On the hosted page's e-mail step: types the e-mail, continues, types the password and clicks Sign in; returns after
    // the click, before AuthKit redirects. Playwright's call log keeps the typed values: show them as *** in a message
    // (the runtime's StorageStateCache does so for a failed sign-in flow).
    public static async Task EnterCredentialsAsync(IPage page, string email, string password)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrEmpty(email);
        ArgumentException.ThrowIfNullOrEmpty(password);

        await TypeAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Email" }), email);
        await page.GetByRole(AriaRole.Button, new() { NameRegex = ContinueButton }).ClickAsync();
        await TypeAsync(page.GetByLabel("Password", new() { Exact = true }), password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
    }

    // Typed, not filled: BrowserStack masks a typed value in its session logs (browserstack.maskCommands sendType, the
    // provider's default) but cannot mask a fill. The clear (an empty fill) waits until the field is visible, enabled
    // and editable, which typing alone does not.
    public static async Task TypeAsync(ILocator field, string value)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        await field.ClearAsync();
        await field.PressSequentiallyAsync(value);
    }

    // Whether url is on the scheme, host and port of site, e.g. back on the application after the hosted page.
    public static bool IsOnHost(string url, Uri site)
    {
        ArgumentNullException.ThrowIfNull(site);

        return Uri.TryCreate(url, UriKind.Absolute, out var current)
            && string.Equals(current.GetLeftPart(UriPartial.Authority), site.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }
}
