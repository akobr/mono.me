using TUnit.Core;
using TUnit.Core.Exceptions;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// TUnit's verdict as a TestOutcome. A failure caused by a missing user or browser is Blocked; a skip from a hook or the
// test body (SkipTestException) is Skipped whatever the state says.
public static class TestOutcomeMapper
{
    public static TestOutcome Map(TestResult? result)
    {
        return result is null ? TestOutcome.Failed : Map(result.State, result.Exception);
    }

    public static TestOutcome Map(TestState state, Exception? exception)
    {
        var cause = Unwrap(exception);
        if (cause is SkipTestException)
        {
            return TestOutcome.Skipped;
        }

        var outcome = state switch
        {
            TestState.Passed => TestOutcome.Passed,
            TestState.Skipped => TestOutcome.Skipped,
            TestState.Timeout => TestOutcome.Timeout,
            TestState.Cancelled => TestOutcome.Cancelled,

            // Failed, and a test that ended without reaching a final state.
            _ => TestOutcome.Failed,
        };

        return AutomationOutcomes.Refine(outcome, cause);
    }

    // The exception that failed the test, without the wrappers TUnit puts around hook failures: BeforeTestException and
    // AfterTestException carry it as the inner exception, TestExecutionException as TestException (or its only hook
    // exception).
    public static Exception? Unwrap(Exception? exception)
    {
        for (var depth = 0; depth < 8 && exception is not null; depth++)
        {
            var inner = exception switch
            {
                BeforeTestException or AfterTestException => exception.InnerException,
                TestExecutionException { TestException: { } test } => test,
                TestExecutionException { HookExceptions.Count: 1 } execution => execution.HookExceptions[0],
                AggregateException { InnerExceptions.Count: 1 } aggregate => aggregate.InnerExceptions[0],
                _ => null,
            };

            if (inner is null)
            {
                return exception;
            }

            exception = inner;
        }

        return exception;
    }
}
