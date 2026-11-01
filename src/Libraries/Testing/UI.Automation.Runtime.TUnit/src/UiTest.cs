using System.Runtime.ExceptionServices;
using _42.Testing.UI.Automation.Runtime.Data;
using _42.Testing.UI.Automation.Runtime.Settings;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TUnit.Core;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The base class of a UI test. Before each attempt it leases a pool user, opens a context of the session's browser
// (signed in as the user from the cached storage state) and a page; after it, the TestScope keeps the screenshot,
// trace and video per the Execution policies, writes console.log, closes everything, returns the users, and reports
// the result unless TUnit runs the test again. Its artifacts are attached to TUnit's result (HTML and TRX reports).
// Without Target:BaseUrl every UI test is skipped with the README hint of the setup. TUnit creates an instance per
// attempt, so the scope belongs to one attempt; the hooks run before a derived class's own Before(Test) hooks.
//
// Clean up in CleanUpAsync, not in an After(Test) hook of a derived class (or an AfterEvery(Test) hook): TUnit runs
// those before EndUiTest, with the result still Passed, and fails the test for their exception only after every hook
// ran, so the report would say Passed and a retry would be reported a second time.
public abstract class UiTest
{
    private TestScope? scope;

    // The page of the test's context.
    protected IPage Page => Scope.Page;

    protected IBrowserContext Context => Scope.Context;

    // The leased pool user; throws for a test whose LeasesUser is false.
    protected TestUser User => Scope.User;

    protected UiAutomationSettings Settings => UiAutomationSession.Settings;

    protected AutomationSession Session => Scope.Session;

    // The generated data of this attempt, seeded from the session seed and the test id.
    protected SeededData Data => Scope.Data;

    protected TestScope Scope => scope ?? throw new InvalidOperationException("The UI test has not begun; use the page from the test body or a Before(Test) hook of the derived class.");

    // Which pool user the test leases.
    protected virtual UserRequirement RequiredUser => UserRequirement.Any;

    // False for a test that signs in itself on its page. That page is traced, and a trace keeps what every action typed:
    // a test that types a pool user's password keeps it in its trace.zip, so turn its trace off (Execution:Trace Off).
    protected virtual bool SignedIn => true;

    // False for a test that needs no user at all (a public page); such a test is not signed in either.
    protected virtual bool LeasesUser => true;

    [Before(HookType.Test)]
    public async Task BeginUiTest(TestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var session = UiAutomationSession.Require();
        scope = TestScope.Create(session, TestIdentityFactory.Create(context), new TestScopeOptions
        {
            User = RequiredUser,
            LeaseUser = LeasesUser,
            SignedIn = SignedIn && LeasesUser,
            Attempt = context.Execution.CurrentRetryAttempt + 1,
            StartedAt = context.Execution.TestStart,

            // The hook's token also ends at TUnit's hook timeout (raised over the start's budget by UiAutomationSession);
            // a start it ends is Blocked by the step that did not finish, not a cancelled test.
            TestCancellation = context.Execution.CancellationToken,
        });

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

    // The derived class's cleanup of an attempt (e.g. a [Serial] test restoring a project setting through the UI), on
    // the still open page, before the result is decided. Its exception fails an attempt that passed, is reported as the
    // failure and decides a retry like a failure of the test body. Not called when the scope did not begin.
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
