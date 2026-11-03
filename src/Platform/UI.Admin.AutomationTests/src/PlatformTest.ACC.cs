using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.Access;
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
    // that a sign-in passed /callback (ExpectSignedInThroughCallbackAsync).
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

    // Asserts that one of the recorded navigations (RecordMainFrameNavigations) opened /callback: the sign-in returned
    // through it.
    protected static async Task ExpectSignedInThroughCallbackAsync(IReadOnlyCollection<string> navigations)
    {
        await Assert.That(SignedInThroughCallback(navigations))
            .IsTrue()
            .Because($"the sign-in returns through /callback; the page went to {string.Join(", ", navigations)}");
    }

    // On a deployed target, once the app handed a sign-in over to AuthKit (Continue with AuthKit, or a protected page
    // opened signed out): waits until the browser left the app's host, signs in on the hosted page as the leased user,
    // with WorkOS' labels like AuthKitSignIn (Email, Continue or Continue with email, Password, Sign in), and returns once the browser is back
    // on the app past /callback. The steps on the hosted page are not traced: the chunk recorded so far is dropped, and
    // a new one starts once the steps ended. A browser that is not back on the app by then (a step failed: a wrong
    // password, an MFA or Radar prompt, a renamed button) first has the value of every input and text area of its
    // frames emptied, or goes to about:blank when that fails, so the snapshots of the new chunk (the failure screenshot
    // takes one) hold no typed value; when neither works, the trace is not started again. A failed step throws a
    // SignInException with the type and the message of Playwright's error, the e-mail and the password as *** (its call
    // log keeps a typed value) and no inner exception, as the runtime's StorageStateCache does for AuthKitSignIn.
    protected async Task SignInOnHostedPageAsync()
    {
        var password = User.Password;
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"{User} has no password for the AuthKit hosted page; set Users:Pool:{User.Index}:Password in the user secrets.");
        }

        await WaitForHostedPageAsync();

        var app = AppUri();
        var traced = Settings.Execution.Trace != ArtifactPolicy.Off;
        if (traced)
        {
            await Context.Tracing.StopChunkAsync();
        }

        try
        {
            await TypeOnHostedPageAsync(Page.GetByRole(AriaRole.Textbox, new() { Name = "Email" }), User.Email);
            await Page.GetByRole(AriaRole.Button, new() { NameRegex = Access.AuthKitSignIn.AuthKitContinue }).ClickAsync();
            await TypeOnHostedPageAsync(Page.GetByLabel("Password", new() { Exact = true }), password);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
            await Page.WaitForURLAsync(url => IsOnHost(url, app));
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new SignInException($"The AuthKit sign-in on the hosted page failed for {User}: {exception.GetType().Name}: {Redact(exception.Message, password, User.Email)}");
        }
        finally
        {
            if (traced && (IsOnHost(Page.Url, app) || await TryEmptyHostedPageAsync()))
            {
                await Context.Tracing.StartChunkAsync(new() { Title = $"{TestContext.Current?.Metadata.TestDetails.MethodName} after the AuthKit sign-in" });
            }
        }

        await Expect(App.UserMenu).ToBeVisibleAsync();
    }

    // On a deployed target, once the app handed a sign-in over to AuthKit: waits until the browser left the app's host
    // for the AuthKit hosted page.
    protected async Task WaitForHostedPageAsync()
    {
        var app = AppUri();
        await Page.WaitForURLAsync(url => !IsOnHost(url, app));
    }

    // Typed, not filled, as AuthKitSignIn does: BrowserStack masks a typed value in its session logs.
    private static async Task TypeOnHostedPageAsync(ILocator field, string value)
    {
        await field.ClearAsync();
        await field.PressSequentiallyAsync(value);
    }

    // The text with the password and the e-mail as ***, each as is, JSON-escaped and URL-encoded (a URL in the message
    // may carry the e-mail), the longest form first; the e-mail in any case.
    private static string Redact(string text, string password, string email)
    {
        foreach (var (secret, comparison) in new[] { (password, StringComparison.Ordinal), (email, StringComparison.OrdinalIgnoreCase) })
        {
            if (string.IsNullOrEmpty(secret))
            {
                continue;
            }

            var forms = new[] { secret, JsonEncodedText.Encode(secret).ToString(), Uri.EscapeDataString(secret), WebUtility.UrlEncode(secret) }
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(form => form.Length);
            foreach (var form in forms)
            {
                text = text.Replace(form, SettingsRedaction.Mask, comparison);
            }
        }

        return text;
    }

    private static bool IsOnHost(string url, Uri app)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var current)
            && string.Equals(current.GetLeftPart(UriPartial.Authority), app.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }

    // Untraced, after a failed step on the hosted page: empties the value of every input and text area of the page's
    // frames (their shadow roots too), else leaves for about:blank. Returns whether one of them worked.
    private async Task<bool> TryEmptyHostedPageAsync()
    {
        try
        {
            foreach (var frame in Page.Frames)
            {
                await frame.EvaluateAsync(
                    """
                    () => {
                      const empty = (root) => root.querySelectorAll('*').forEach((element) => {
                        if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) element.value = '';
                        if (element.shadowRoot) empty(element.shadowRoot);
                      });
                      empty(document);
                    }
                    """);
            }

            return true;
        }
        catch (PlaywrightException)
        {
            // A frame navigated or went away meanwhile: leave the hosted page instead.
        }

        try
        {
            await Page.GotoAsync("about:blank");
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private Uri AppUri()
    {
        return new Uri(Target.BaseUrl ?? throw new InvalidOperationException("Target:BaseUrl is not set."));
    }
}
