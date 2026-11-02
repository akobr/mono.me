using System.Diagnostics;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// Bounded polling for a read that a deployed target serves eventually consistent: the audit trail (written by another
// instance, cached by the page for 15 s) or a policy the API caches per instance for up to a minute. An attempt reads
// again (a page load, the filters once more) and asserts with a short timeout; a failed assertion or wait starts the
// next attempt until the time is up, and the last attempt's failure is the test's. On the mock the first attempt
// passes, so it costs nothing there.
public static class Eventually
{
    // The timeout of the assertions of one attempt (Expect's Timeout), short so that the next attempt reads again.
    public const float AttemptTimeoutMs = 5_000;

    // The time a deployed target may take: the audit and the policy settle within a minute.
    public static readonly TimeSpan LiveTimeout = TimeSpan.FromSeconds(60);

    // Runs attempt (with its number, 1 for the first) until it passes or timeout (LiveTimeout by default) is over; the
    // attempt that starts after the deadline is the last.
    public static async Task UntilAsync(Func<int, Task> attempt, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var limit = timeout ?? LiveTimeout;
        var clock = Stopwatch.StartNew();
        for (var number = 1; ; number++)
        {
            try
            {
                await attempt(number);
                return;
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException && clock.Elapsed < limit)
            {
                // Not there yet: the next attempt reads again.
            }
        }
    }
}
