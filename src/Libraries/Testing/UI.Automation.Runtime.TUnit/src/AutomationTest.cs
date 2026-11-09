using System.Runtime.ExceptionServices;
using _42.Testing.UI.Automation.Runtime.Data;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TUnit.Core;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The base class of a test on the UI automation session that needs no browser up front, e.g. one that runs a command
// line against the target. Before each attempt it leases a pool user and creates the test's scope (its data and
// artifacts); after it, the TestScope closes what the test opened, returns the users, and reports the result unless
// TUnit runs the test again. Its artifacts (what the test attaches: Scope.AttachTextAsync, Scope.Artifacts) are attached
// to TUnit's result (HTML and TRX reports). A test that needs a browser later opens one with OpenBrowserAsync; a test
// that works on a page from the start derives from UiTest. Without Target:BaseUrl every test is skipped with the README
// hint of the setup, and a derived class skips a test that does not run in the session (GetSkipReason) before the lease.
// TUnit creates an instance per attempt, so the scope belongs to one attempt; the hooks run before a derived class's own
// Before(Test) hooks.
//
// Clean up in CleanUpAsync, not in an After(Test) hook of a derived class (or an AfterEvery(Test) hook): TUnit runs
// those before EndUiTest, with the result still Passed, and fails the test for their exception only after every hook
// ran, so the report would say Passed and a retry would be reported a second time.
public abstract class AutomationTest
{
    private TestScope? scope;

    // The leased pool user; throws for a test whose LeasesUser is false.
    protected TestUser User => Scope.User;

    protected UiAutomationSettings Settings => UiAutomationSession.Settings;

    protected AutomationSession Session => Scope.Session;

    // The generated data of this attempt, seeded from the session seed and the test id.
    protected SeededData Data => Scope.Data;

    protected TestScope Scope => scope ?? throw new InvalidOperationException("The test has not begun; use its scope from the test body or a Before(Test) hook of the derived class.");

    // Which pool user the test leases.
    protected virtual UserRequirement RequiredUser => UserRequirement.Any;

    // False for a test that needs no user at all (a public page, a command that needs no sign-in); such a test is not
    // signed in either.
    protected virtual bool LeasesUser => true;

    // Whether the test's scope opens a browser context and a page before the test (TestScopeOptions.Browser): false
    // here, true for a UiTest. A test without one opens a browser when it needs one (OpenBrowserAsync).
    protected virtual bool UsesBrowser => false;

    // Whether the context of a test with a browser starts signed in as the user; a UiTest asks its SignedIn.
    private protected virtual bool StartsSignedIn => UsesBrowser && LeasesUser;

    [Before(HookType.Test)]
    public async Task BeginUiTest(TestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var session = UiAutomationSession.Require();

        // A test that does not run in this session is skipped before it costs a lease, a browser context or a page; the
        // scope stays null, so the end receiver reports the skip with its reason.
        SkipWhenAsked(context, session);

        // The hook's token also ends at TUnit's hook timeout (raised over the start's budget by UiAutomationSession); a
        // start it ends is Blocked by the step that did not finish, not a cancelled test.
        scope = TestScope.Create(
            session,
            TestIdentityFactory.Create(context),
            CreateScopeOptions(context.Execution.CurrentRetryAttempt + 1, context.Execution.TestStart, context.Execution.CancellationToken));

        // On failure the scope keeps the exception; After(Test) still runs and reports it.
        await scope.BeginAsync(cancellationToken);
    }

    [After(HookType.Test)]
    public async Task EndUiTest(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (scope is not { } current)
        {
            // Skipped or failed before the scope existed; the end receiver reports what there is to report.
            return;
        }

        var result = context.Execution.Result;
        var exception = current.BeginException ?? TestOutcomeMapper.Unwrap(result?.Exception);
        var outcome = TestOutcomeMapper.Map(result?.State ?? TestState.Failed, exception);

        // The cleanup fails an attempt that passed; after a failure or a skip, the test's own outcome is the one reported.
        var cleanupFailure = current.IsStarted ? await CleanUpCoreAsync() : null;
        var failsAttempt = cleanupFailure is not null && outcome == TestOutcome.Passed;
        if (failsAttempt)
        {
            exception = cleanupFailure;
            outcome = AutomationOutcomes.Refine(TestOutcome.Failed, cleanupFailure);
        }
        else if (cleanupFailure is not null)
        {
            UiAutomationSession.Logger.LogWarning(cleanupFailure, "{Test}: the cleanup failed after the test had not passed.", current.Test.DisplayName);
        }

        if (outcome == TestOutcome.Skipped)
        {
            exception = UiAutomationSession.SkipReasonOf(context, exception);
        }

        // Claimed also when TUnit runs the attempt again, so the end receiver, which runs after this hook, does not report
        // the attempt a second time: this report, with the cleanup's failure, is the one that counts (since TUnit 1.78 the
        // receiver sees a failed cleanup as the attempt's failure too, before 1.78 it saw the attempt as passed).
        var final = !await RetryPolicy.WillRetryAsync(context, failsAttempt ? cleanupFailure : null);
        var claimed = UiAutomationSession.TryClaim(current.Test, current.Attempt);
        if (!claimed)
        {
            UiAutomationSession.Logger.LogWarning("{Test} (attempt {Attempt}) was reported already; this report is not sent.", current.Test.Id, current.Attempt);
        }

        var report = await current.EndAsync(outcome, exception, UiAutomationSession.CapturedOutput(context), report: final && claimed);

        foreach (var artifact in report.Artifacts)
        {
            var name = current.Attempt > 1 ? $"{artifact.Name} (attempt {current.Attempt})" : artifact.Name;
            context.Output.AttachArtifact(artifact.Path, name, $"{artifact.Kind} of {current.Test.DisplayName}");
        }

        await current.DisposeAsync();

        // TUnit fails the attempt for the cleanup's exception, as reported, and retries it per [ConfiguredRetry].
        if (failsAttempt)
        {
            ExceptionDispatchInfo.Throw(cleanupFailure!);
        }
    }

    // Skips the test (Skip.Test) with the reason of GetSkipReason, when it has one.
    internal void SkipWhenAsked(TestContext context, AutomationSession session)
    {
        if (GetSkipReason(context, session) is { } reason)
        {
            Skip.Test(reason);
        }
    }

    // The options of the attempt's scope: the user it leases, whether it opens a browser and starts signed in.
    internal TestScopeOptions CreateScopeOptions(int attempt, DateTimeOffset? startedAt, CancellationToken testCancellation)
    {
        var leasesUser = LeasesUser;
        var usesBrowser = UsesBrowser;
        return new TestScopeOptions
        {
            User = RequiredUser,
            LeaseUser = leasesUser,
            Browser = usesBrowser,
            SignedIn = usesBrowser && leasesUser && StartsSignedIn,
            Attempt = attempt,
            StartedAt = startedAt,
            TestCancellation = testCancellation,
        };
    }

    // The derived class's cleanup of an attempt (e.g. a [Serial] test restoring a project setting, through the UI or a
    // command), before the result is decided; a UiTest's page is still open. Its exception fails an attempt that passed,
    // is reported as the failure and decides a retry like a failure of the test body. Not called when the scope did not
    // begin.
    protected virtual Task CleanUpAsync()
    {
        return Task.CompletedTask;
    }

    // A second pool user for the test (e.g. one who accepts an invitation); returned at the end.
    protected Task<TestUser> LeaseAnotherUserAsync(UserRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return Scope.LeaseAnotherUserAsync(requirement, TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None);
    }

    // A browser context when the test needs one (TestScope.OpenBrowserAsync), signed in as asUser when given: the
    // session's browser, acquired on demand by a test without one, or another context of a UiTest's browser. Closed at
    // the end of the attempt.
    protected Task<IBrowserContext> OpenBrowserAsync(TestUser? asUser = null)
    {
        return Scope.OpenBrowserAsync(asUser, TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None);
    }

    // Why the test does not run in this session, e.g. a layout the session's viewport (session.ContextOptions.Viewport)
    // does not have, or a role that no user of the pool (session.Users) holds; null runs it. BeginUiTest asks before the
    // lease, the browser context and the page, so such a skip costs nothing, and the reason is the skip's reason in the
    // reports and results.jsonl. It runs before the scope reads RequiredUser (it may read it itself); the test's
    // categories and attributes are in context.Metadata.TestDetails.
    protected virtual string? GetSkipReason(TestContext context, AutomationSession session)
    {
        return null;
    }

    private async Task<Exception?> CleanUpCoreAsync()
    {
        try
        {
            await CleanUpAsync();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
