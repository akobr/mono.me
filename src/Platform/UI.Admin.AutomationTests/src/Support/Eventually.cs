using System.Diagnostics;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// Bounded polling for a read that a deployed target serves eventually consistent: the audit trail, written by another
// instance and cached by the page for 15 s. An attempt reads again (a page load, the filters once more) and asserts
// with AttemptTimeoutMs; a failed assertion or wait starts the next attempt until Limit is over, and the last attempt's
// failure is the test's. The mock answers at once, so there it is one attempt with the usual Expect timeouts (Once):
// a failure on the mock is a defect, never a delay to wait out. Pick it with On(Target.IsMock).
public sealed record Eventually(TimeSpan Limit, float? AttemptTimeoutMs)
{
    // The mock: the first attempt is the only one, with Expect's own timeouts.
    public static Eventually Once { get; } = new(TimeSpan.Zero, null);

    // A deployed target: the audit settles within a minute; short attempts, so that the next one reads again.
    public static Eventually Live { get; } = new(TimeSpan.FromSeconds(60), 5_000);

    public static Eventually On(bool isMock)
    {
        return isMock ? Once : Live;
    }

    // Runs attempt (with its number, 1 for the first) until it passes or Limit is over; the attempt that starts after
    // the deadline is the last, so with a zero Limit the first failure is rethrown.
    public async Task UntilAsync(Func<int, Task> attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var clock = Stopwatch.StartNew();
        for (var number = 1; ; number++)
        {
            try
            {
                await attempt(number);
                return;
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException && clock.Elapsed < Limit)
            {
                // Not there yet: the next attempt reads again.
            }
        }
    }
}
