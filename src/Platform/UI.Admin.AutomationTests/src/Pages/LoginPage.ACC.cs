using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Access section's part of /login: the intro under the heading, the "You are signed in as <name>." of a signed-in
// browser (next to Continue, to ?returnTo or '/'), and the alert "Sign-in failed" with the ?error_description, else
// the ?error, of the query (also where /callback sends a failed sign-in). The bare layout has no user menu, so a test
// reads the signed-in name elsewhere first (App.SignedInNameAsync).
public sealed partial class LoginPage
{
    public const string IntroText = "Manage organizations, projects, annotations and configurations of the 2S Platform.";

    public ILocator Intro => app.Page.GetByText(IntroText, new() { Exact = true });

    // The paragraph "You are signed in as <name>.", only when the browser is signed in.
    public ILocator SignedInText => app.Page.GetByText("You are signed in as");

    // The title of the error alert; Nuxt UI's alert has no alert role, so it is found by its title.
    public ILocator SignInErrorTitle => app.Page.GetByText("Sign-in failed", new() { Exact = true });

    // The description of the error alert: the error of the query.
    public ILocator SignInError => SignInErrorTitle.Locator("xpath=following-sibling::*[@data-slot='description']");
}
