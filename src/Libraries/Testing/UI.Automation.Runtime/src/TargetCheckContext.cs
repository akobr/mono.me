using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime;

// What IUiAutomationSetup.CheckTargetAsync works with: the session and its settings, a leased pool user and a page of a
// browser context signed in as that user (through the session's cached sign-ins, so the user's first test signs in no
// more). The check runs in a test scope of its own, Test: its screenshot and trace follow Execution:Screenshot and
// Execution:Trace like a test's and land in <run directory>/TargetCheck/CheckTarget/; it is not reported. A setup whose
// TargetCheckUsesBrowser is false gets the user only: Page and Context throw, and the check opens a browser context when
// it needs one (OpenBrowserAsync).
public sealed class TargetCheckContext
{
    internal TargetCheckContext(TestScope scope)
    {
        Scope = scope;
    }

    // The identity of the check's scope: its artifacts folder, its BrowserStack session name (TargetCheck.CheckTarget).
    public static TestIdentity Test { get; } = new(
        "ui-automation/TargetCheck/CheckTarget",
        "CheckTarget",
        "TargetCheck",
        "CheckTarget",
        "ui-automation",
        [],
        []);

    // The check's scope, for more than one page: Scope.NewContextAsync, Scope.OpenBrowserAsync,
    // Scope.LeaseAnotherUserAsync, Scope.AttachTextAsync.
    public TestScope Scope { get; }

    public AutomationSession Session => Scope.Session;

    public UiAutomationSettings Settings => Session.Settings;

    // The leased pool user (any role); returned when the check ends.
    public TestUser User => Scope.User;

    // Whether the check has a page up front (IUiAutomationSetup.TargetCheckUsesBrowser).
    public bool HasPage => Scope.UsesBrowser;

    public IBrowserContext Context => HasPage ? Scope.Context : throw NoPage("context");

    // A blank page; its context's BaseURL is Target:BaseUrl.
    public IPage Page => HasPage ? Scope.Page : throw NoPage("page");

    // A new browser context signed in as User (Scope.OpenBrowserAsync): the browser of a check without a page, acquired
    // on demand, or another context of the check's browser. Closed when the check ends.
    public Task<IBrowserContext> OpenBrowserAsync(CancellationToken cancellationToken = default)
    {
        return Scope.OpenBrowserAsync(User, cancellationToken);
    }

    private static InvalidOperationException NoPage(string what)
    {
        return new InvalidOperationException(
            $"The target check has no {what}: the setup's TargetCheckUsesBrowser is false. Open a browser context when the check needs one (OpenBrowserAsync, or Scope.OpenBrowserAsync for another user).");
    }
}
