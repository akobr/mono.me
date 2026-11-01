using System.Collections.Concurrent;
using _42.Testing.UI.Automation.Runtime.Artifacts;
using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.Access;

// The signed-in storage state (cookies and local storage) of each pool user, captured after the user's first sign-in
// of the session; kept in memory only, never written to disk. A user signs in in a throw-away, untraced context of the
// test's browser, once, even when two scopes ask at the same time. Invalidate drops a state the application no longer
// accepts, so the next request signs in again; Refresh takes the rotated tokens of a test that ended signed in. A failed
// sign-in throws a SignInException that shows neither the user's e-mail nor the password.
public sealed class StorageStateCache
{
    private readonly ConcurrentDictionary<string, string> states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger logger;
    private int signIns;

    public StorageStateCache(ISignInFlow flow, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(flow);

        Flow = flow;
        this.logger = logger ?? NullLogger.Instance;
    }

    public ISignInFlow Flow { get; }

    // How many sign-ins the cache ran.
    public int SignInCount => Volatile.Read(ref signIns);

    public bool TryGet(TestUser user, out string? storageState)
    {
        ArgumentNullException.ThrowIfNull(user);
        return states.TryGetValue(user.Email, out storageState);
    }

    // contextOptions: the session's options (without storage state); the throw-away context drops video and state.
    // artifacts: where a screenshot of a failed sign-in goes (sign-in-failure.png).
    public async Task<string> GetAsync(
        TestUser user,
        IBrowser browser,
        BrowserNewContextOptions contextOptions,
        ArtifactCollector? artifacts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(contextOptions);

        if (states.TryGetValue(user.Email, out var cached))
        {
            return cached;
        }

        var gate = gates.GetOrAdd(user.Email, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (states.TryGetValue(user.Email, out cached))
            {
                return cached;
            }

            var state = await SignInAsync(user, browser, contextOptions, artifacts, cancellationToken);
            states[user.Email] = state;
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (states.TryRemove(user.Email, out _))
        {
            logger.LogInformation("The signed-in state of {User} was dropped; the next test of the user signs in again.", user);
        }
    }

    // After a test that ended signed in as the user (its context started from the cached state): the context's cookies
    // and sign-in items (ISignInFlow.IsSignInStorageItem) replace those of the cached state. Identity providers that
    // rotate refresh tokens (AuthKit retires the token a page load used, after a short grace period) would otherwise
    // hand the next test of the user a dead token. The other local storage stays as captured after the sign-in, so
    // preferences a test changed never reach the next one. A context that lost every sign-in item it started with is
    // signed out: the state is dropped. A state dropped meanwhile stays dropped.
    public async Task RefreshAsync(TestUser user, IBrowserContext context)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(context);

        if (!states.TryGetValue(user.Email, out var cached))
        {
            return;
        }

        var merged = StorageStateMerge.Merge(cached, await context.StorageStateAsync(), Flow.IsSignInStorageItem);
        if (merged is null)
        {
            if (states.TryRemove(new KeyValuePair<string, string>(user.Email, cached)))
            {
                logger.LogInformation("{User} lost the sign-in in a test; the next test of the user signs in again.", user);
            }

            return;
        }

        states.TryUpdate(user.Email, merged, cached);
    }

    private async Task<string> SignInAsync(
        TestUser user,
        IBrowser browser,
        BrowserNewContextOptions contextOptions,
        ArtifactCollector? artifacts,
        CancellationToken cancellationToken)
    {
        var options = new BrowserNewContextOptions(contextOptions)
        {
            StorageState = null,
            StorageStatePath = null,
            RecordVideoDir = null,
            RecordVideoSize = null,
        };

        var context = await browser.NewContextAsync(options);
        try
        {
            var page = await context.NewPageAsync();
            try
            {
                logger.LogInformation("Signing {User} in with {Flow}.", user, Flow.Name);
                await Flow.SignInAsync(page, user, cancellationToken);
                Interlocked.Increment(ref signIns);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await TryScreenshotAsync(page, artifacts);
                throw Failed(user, exception);
            }

            return await context.StorageStateAsync();
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    // Playwright's call log of a fill or a type that failed after its element resolved keeps the typed value, and the
    // .NET client puts the call log into the exception's message: the failure of a sign-in may hold the user's e-mail
    // and password. Its message goes out with both as ***, and the original exception is not kept (as the inner
    // exception it would reach every report's stack trace).
    private SignInException Failed(TestUser user, Exception exception)
    {
        var redactor = new SecretRedactor(user.Password, user.Email);
        return new SignInException($"The sign-in flow {Flow.Name} failed for {user}: {exception.GetType().Name}: {redactor.Redact(exception.Message)}");
    }

    private async Task TryScreenshotAsync(IPage page, ArtifactCollector? artifacts)
    {
        if (artifacts is null)
        {
            return;
        }

        try
        {
            var path = artifacts.GetPath("sign-in-failure.png");

            // The inputs are masked: the form may still show the typed e-mail (or a password field shown as text).
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true, Timeout = 10_000, Mask = [page.Locator("input, textarea")] });
            artifacts.Add(path, ArtifactKind.Screenshot);
        }
        catch (Exception exception) when (exception is PlaywrightException or IOException or TimeoutException)
        {
            logger.LogWarning(exception, "No screenshot of the failed sign-in.");
        }
    }
}
