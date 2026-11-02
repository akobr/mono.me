using System.Collections.Concurrent;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests;

// The helpers of the Access section: the sign-in again on the AuthKit hosted page of a deployed target, after a test
// signed out, and a record of the main frame's navigations, which shows that a sign-in went through /callback.
public abstract partial class PlatformTest
{
    // The path where AuthKit (and the mock's sign-in) returns to the app.
    private const string CallbackPath = "/callback";

    // Records the URL of every navigation of the page's main frame from now on, oldest first: page loads (location
    // assign, a redirect of AuthKit) and the router's same-document navigations. Read it after the action, e.g. to show
    // that a sign-in passed /callback (SignedInThroughCallback).
    protected IReadOnlyCollection<string> RecordMainFrameNavigations()
    {
        var urls = new ConcurrentQueue<string>();
        Page.FrameNavigated += (_, frame) =>
        {
            if (frame == Page.MainFrame)
            {
                urls.Enqueue(frame.Url);
            }
        };

        return urls;
    }

    // Whether one of the recorded navigations (RecordMainFrameNavigations) opened /callback, the end of a sign-in.
    protected static bool SignedInThroughCallback(IReadOnlyCollection<string> navigations)
    {
        ArgumentNullException.ThrowIfNull(navigations);
        return navigations.Any(url => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && string.Equals(parsed.AbsolutePath.TrimEnd('/'), CallbackPath, StringComparison.OrdinalIgnoreCase));
    }

    // On a deployed target, once the app handed a sign-in over to AuthKit (Continue with AuthKit, or a protected page
    // opened signed out): waits until the browser left the app's host, signs in on the hosted page as the leased user,
    // with WorkOS' labels like AuthKitSignIn (Email, Continue, Password, Sign in), and returns once the browser is back
    // on the app past /callback. The trace leaves the hosted page out: the chunk recorded so far is dropped and a new one
    // starts after the sign-in, so no trace.zip keeps the password (a failure still has its screenshot and console.log).
    protected async Task SignInOnHostedPageAsync()
    {
        var password = User.Password;
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"{User} has no password for the AuthKit hosted page; set Users:Pool:{User.Index}:Password in the user secrets.");
        }

        var app = new Uri(Target.BaseUrl ?? throw new InvalidOperationException("Target:BaseUrl is not set."));
        await Page.WaitForURLAsync(url => !IsOnHost(url, app));

        var traced = Settings.Execution.Trace != ArtifactPolicy.Off;
        if (traced)
        {
            await Context.Tracing.StopChunkAsync();
        }

        try
        {
            await TypeOnHostedPageAsync(Page.GetByRole(AriaRole.Textbox, new() { Name = "Email" }), User.Email);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await TypeOnHostedPageAsync(Page.GetByLabel("Password", new() { Exact = true }), password);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
            await Page.WaitForURLAsync(url => IsOnHost(url, app));
        }
        finally
        {
            if (traced)
            {
                await Context.Tracing.StartChunkAsync(new() { Title = $"{TestContext.Current?.Metadata.TestDetails.MethodName} after the AuthKit sign-in" });
            }
        }

        await Expect(App.UserMenu).ToBeVisibleAsync();
    }

    // Typed, not filled, as AuthKitSignIn does: BrowserStack masks a typed value in its session logs.
    private static async Task TypeOnHostedPageAsync(ILocator field, string value)
    {
        await field.ClearAsync();
        await field.PressSequentiallyAsync(value);
    }

    private static bool IsOnHost(string url, Uri app)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var current)
            && string.Equals(current.GetLeftPart(UriPartial.Authority), app.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }
}
