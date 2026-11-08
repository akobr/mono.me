using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Platform.Cli.AutomationTests.Access;

// A pool user's sign-in through sform's device code, in a home (SFORM_HOME):
//   1. sform account [-o Target:AuthKitOrganizationId] starts with SFORM_DEVICE_CODE_OUTPUT=json, and its first line on
//      the standard error is the code ({userCode, verificationUri, verificationUriComplete, expiresIn});
//   2. a browser context of the test's scope (TestScope.OpenBrowserAsync, anonymous: Target:SignIn is None; or one
//      started from the user's AuthKit session of an earlier approval in this run) opens verificationUriComplete, and
//      AuthKitDevicePage enters the credentials when the hosted page asks, and approves (or declines) the device;
//   3. sform ends: signed in is exit 0, or 43 when no default project is set ("No default project is set, please call
//      sform account set"), and the home is marked signed in; the context's AuthKit session is kept for the next approval.
// The password is typed only in a context of OpenBrowserAsync or one of this class, never traced (Execution:Trace does
// not apply to them, and the CLI's settings keep it Off anyway). A device page that does not lead to the end within the
// time fails with "the device page showed: …", a screenshot (inputs masked) and the page's text as attachments.
public sealed class DeviceCodeSignIn
{
    private readonly SformRunner runner;
    private readonly TestScope scope;
    private readonly CliTarget target;
    private int attempts;

    public DeviceCodeSignIn(SformRunner runner, TestScope scope, CliTarget target)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(target);

        this.runner = runner;
        this.scope = scope;
        this.target = target;
    }

    // Signs the user in to the home; throws an SformAssertionException when sform did not end signed in, unless the
    // options decline or do not expect a sign-in (DeviceCodeOptions.ExpectSignedIn): then the result says what sform did.
    public async Task<DeviceCodeSignInResult> SignInAsync(TestUser user, SformHome home, DeviceCodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(home);
        options ??= new DeviceCodeOptions();

        var arguments = new List<string>(options.Command);
        var organization = options.Organization ?? target.AuthKitOrganizationId;
        if (!string.IsNullOrEmpty(organization))
        {
            arguments.Add(options.OrganizationOption);
            arguments.Add(organization);
        }

        arguments.AddRange(options.Arguments);

        var environment = new Dictionary<string, string?>(options.Environment, StringComparer.Ordinal);
        if (options.JsonPrompt)
        {
            environment[SformProcessEnvironment.DeviceCodeOutput] = "json";
        }

        var timeout = runner.Options.Timeout;
        await using var process = await runner.StartAsync(
            arguments,
            new SformRunOptions { Home = home, Environment = environment, Label = $"device code sign-in of {user}" });
        process.CloseInput();

        var prompt = await ReadPromptAsync(process, options.JsonPrompt, timeout);
        if (prompt is null)
        {
            // Signed in already ("You are already logged in as …"), or it failed before a code.
            var early = await process.WaitForExitAsync();
            home.IsSignedIn = early.ExitCode is 0 or 43;
            return new DeviceCodeSignInResult(early, null, []);
        }

        var number = Interlocked.Increment(ref attempts);
        IReadOnlyList<string> steps = [];
        IPage? page = null;
        IBrowserContext? own = null;
        try
        {
            var anonymous = await scope.OpenBrowserAsync(null, cancellationToken);
            var cached = options.UseCachedBrowserSession ? AuthKitSessionCache.Get(scope.Session.RunId, user.Fingerprint) : null;
            var context = anonymous;
            if (cached is not null)
            {
                // Started from the user's AuthKit session; closed here, as the scope does not know it.
                own = await scope.Browser.Browser.NewContextAsync(scope.Session.ContextOptions.Create(cached));
                context = own;
            }

            page = await context.NewPageAsync();
            steps = await AuthKitDevicePage.CompleteAsync(
                page,
                prompt,
                user,
                options.Decline,
                () => process.HasExited,
                options.ApprovalTimeout ?? ApprovalTimeoutOf(timeout),
                cancellationToken);

            if (!process.HasExited)
            {
                // The last click may still be on its way to the token endpoint, which sform polls every few seconds.
                await Task.WhenAny(process.Completion, Task.Delay(TimeSpan.FromSeconds(15), cancellationToken));
            }

            if (!process.HasExited)
            {
                var shown = await RecordPageAsync(page, number);
                var killed = await process.KillAsync();
                throw killed.Fail($"did not end after the device code was {(options.Decline ? "declined" : "approved")} ({Describe(steps)}); the device page showed: {Cut(shown)}");
            }

            var result = await process.WaitForExitAsync();
            var signedIn = result.ExitCode is 0 or 43;
            if (signedIn && !options.Decline)
            {
                home.IsSignedIn = true;
                AuthKitSessionCache.Set(scope.Session.RunId, user.Fingerprint, await context.StorageStateAsync());
            }
            else if (!options.Decline && options.ExpectSignedIn)
            {
                var shown = await RecordPageAsync(page, number);
                throw result.Fail($"did not sign in after the device page ({Describe(steps)}); the device page showed: {Cut(shown)}");
            }

            return new DeviceCodeSignInResult(result, prompt, steps);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            var shown = page is null ? string.Empty : await RecordPageAsync(page, number);
            var killed = await process.KillAsync();
            throw killed.Fail($"could not be approved on the device page ({Describe(steps)}): {scope.Redact(exception.Message)}; the device page showed: {Cut(shown)}");
        }
        finally
        {
            if (own is not null)
            {
                await own.CloseAsync();
            }
        }
    }

    private static async Task<DeviceCodePrompt?> ReadPromptAsync(SformProcess process, bool json, TimeSpan timeout)
    {
        if (json)
        {
            var line = await process.WaitForLineOrExitAsync(SformStream.StdErr, DeviceCodePrompt.IsPrompt, timeout);
            return line is null ? null : DeviceCodePrompt.Parse(line);
        }

        var expires = await process.WaitForLineOrExitAsync(SformStream.StdOut, text => text.Contains("The code expires in", StringComparison.Ordinal), timeout);
        return expires is not null && DeviceCodePrompt.TryParseText(AnsiText.Plain(process.StdOut), out var prompt) ? prompt : null;
    }

    // The address without its query and fragment, which carry the user code or the state of a sign-in.
    private static string WithoutQuery(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : url;
    }

    // Three quarters of the run's timeout (at least 30 s), so the page's failure is recorded before the run's timeout
    // kills sform.
    private static TimeSpan ApprovalTimeoutOf(TimeSpan runTimeout)
    {
        return TimeSpan.FromTicks(Math.Max(runTimeout.Ticks * 3 / 4, TimeSpan.FromSeconds(30).Ticks));
    }

    private static string Describe(IReadOnlyList<string> steps)
    {
        return steps.Count == 0 ? "no step was taken" : string.Join(", ", steps);
    }

    private static string Cut(string text)
    {
        return text.Length <= 1500 ? text : text[..1500] + " …";
    }

    // Keeps a screenshot (inputs masked) and the page's text; returns the text, redacted.
    private async Task<string> RecordPageAsync(IPage page, int number)
    {
        var text = scope.Redact(await AuthKitDevicePage.TextOfAsync(page));
        try
        {
            var path = scope.Artifacts.GetPath($"device-page-{number}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true, Mask = [page.Locator("input")] });
            scope.Artifacts.Add(path, ArtifactKind.Screenshot);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            // the page is gone; the text says what there is
        }

        var address = WithoutQuery(page.Url);
        await scope.AttachTextAsync($"device-page-{number}.txt", $"{address}\n\n{text}");
        return $"{address}: {text}";
    }
}
