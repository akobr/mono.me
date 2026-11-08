using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The base class of a UI test: an AutomationTest whose scope also opens a context of the session's browser (signed in
// as the leased user from the cached storage state) and a page before each attempt. After it, the TestScope keeps the
// screenshot, trace and video per the Execution policies, writes console.log, closes everything, returns the users, and
// reports the result unless TUnit runs the test again. Everything else (the lease, RequiredUser, LeasesUser, the skip,
// CleanUpAsync, the retries, the reporting and the artifacts on TUnit's result) is AutomationTest's.
public abstract class UiTest : AutomationTest
{
    // The page of the test's context.
    protected IPage Page => Scope.Page;

    protected IBrowserContext Context => Scope.Context;

    // False for a test that signs in itself on its page. That page is traced, and a trace keeps what every action typed:
    // a test that types a pool user's password keeps it in its trace.zip, so turn its trace off (Execution:Trace Off).
    protected virtual bool SignedIn => true;

    // A UI test always opens its page before the test.
    protected sealed override bool UsesBrowser => true;

    private protected override bool StartsSignedIn => SignedIn && LeasesUser;
}
