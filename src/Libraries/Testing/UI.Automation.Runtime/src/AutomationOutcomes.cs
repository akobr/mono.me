using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Testing.UI.Automation.Runtime;

// How an engine's verdict maps to a TestOutcome where the runtime knows more than the engine.
public static class AutomationOutcomes
{
    // The test could not run for a reason outside the system under test: no user, no browser.
    public static bool IsBlocking(Exception? exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
        {
            exception = aggregate.InnerExceptions[0];
        }

        return exception is UserLeaseException or BrowserUnavailableException;
    }

    // A failure caused by a blocking exception is Blocked; anything else stays.
    public static TestOutcome Refine(TestOutcome outcome, Exception? exception)
    {
        return outcome == TestOutcome.Failed && IsBlocking(exception) ? TestOutcome.Blocked : outcome;
    }

    // Whether the failure artifacts (screenshot, trace, video) of an OnFailure policy are kept.
    public static bool KeepsFailureArtifacts(TestOutcome outcome)
    {
        return outcome is not (TestOutcome.Passed or TestOutcome.Skipped);
    }
}
