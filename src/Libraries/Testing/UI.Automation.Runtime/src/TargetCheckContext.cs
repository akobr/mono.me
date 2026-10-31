using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime;

// What IUiAutomationSetup.CheckTargetAsync works with: the session and its settings, a leased pool user and a page of a
// browser context signed in as that user (through the session's cached sign-ins, so the user's first test signs in no
// more). The check runs in a test scope of its own, Test: its screenshot and trace follow Execution:Screenshot and
// Execution:Trace like a test's and land in <run directory>/TargetCheck/CheckTarget/; it is not reported.
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

    // The check's scope, for more than one page: Scope.NewContextAsync, Scope.LeaseAnotherUserAsync.
    public TestScope Scope { get; }

    public AutomationSession Session => Scope.Session;

    public UiAutomationSettings Settings => Session.Settings;

    // The leased pool user (any role); returned when the check ends.
    public TestUser User => Scope.User;

    public IBrowserContext Context => Scope.Context;

    // A blank page; its context's BaseURL is Target:BaseUrl.
    public IPage Page => Scope.Page;
}
