using TUnit.Core;
using TUnit.Core.Exceptions;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// Which failures run again, and whether the attempt that just ended is the last one. TUnit raises no retry event and
// calls the end receivers before it decides, so the adapter repeats the engine's decision to report final attempts only.
public static class RetryPolicy
{
    // The predicate of [ConfiguredRetry]: a skip, a data conflict and a run whose session is not available (settings
    // that did not load, a start or a target check that failed) are final; anything else may run again.
    public static bool ShouldRetry(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var cause = TestOutcomeMapper.Unwrap(exception);
        return exception is not SkipTestException && cause is not (SkipTestException or DataConflictException or UiAutomationUnavailableException);
    }

    // The engine's rule: a failed attempt below the retry limit runs again when the test's retry predicate (of
    // [ConfiguredRetry] or [Retry]) agrees, or when it has none. A copy of TUnit's internal retry decision as of TUnit
    // 1.76.x (TUnit.Core 1.76.45, pinned in Directory.Packages.props): check it against the engine's source on every
    // upgrade of TUnit, as a drift reports a retried attempt as final, or a final one not at all. failure: an exception an After(Test) hook is about to
    // throw, which fails (and may retry) an attempt whose result still says Passed.
    public static async Task<bool> WillRetryAsync(TestContext context, Exception? failure = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = context.Execution.Result;
        var exception = failure;
        if (exception is null)
        {
            if (result is null || result.State is not (TestState.Failed or TestState.Timeout) || result.Exception is null)
            {
                return false;
            }

            exception = result.Exception;
        }

        var attempt = context.Execution.CurrentRetryAttempt;
        if (attempt >= context.Metadata.TestDetails.RetryLimit)
        {
            return false;
        }

        return context.Execution.RetryFunc is not { } shouldRetry || await shouldRetry(context, exception, attempt + 1);
    }
}
