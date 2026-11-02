using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests;

// The Settings section's helpers: a page load that shows a change of the secrets policy (a deployed API caches the
// policy per instance for up to a minute; the instance that saved it knows the change, but a reload served by another
// instance can show the policy before it), and the message of a refusal that the mock words differently.
public abstract partial class PlatformTest
{
    // How long a deployed API may serve a policy from an instance's cache, with a margin.
    private static readonly TimeSpan PolicyCacheTime = TimeSpan.FromSeconds(75);

    // Opens the page (open) and checks it (expectation, with the usual Expect timeouts). On the mock that is it; on a
    // deployed target a failed check opens the page again until PolicyCacheTime is over, and the last failure is the
    // test's.
    protected async Task OpenUntilPolicyShowsAsync(Func<Task> open, Func<Task> expectation)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(expectation);

        var clock = Stopwatch.StartNew();
        while (true)
        {
            await open();
            try
            {
                await expectation();
                return;
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException && !Target.IsMock && clock.Elapsed < PolicyCacheTime)
            {
                // An instance with the old policy answered: load the page again.
            }
        }
    }

    // The exact message of a refusal on this target: the mock's, or a deployed API's, which the page may follow with
    // the API's hint.
    protected Regex TargetMessage(string mock, string live)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mock);
        ArgumentException.ThrowIfNullOrWhiteSpace(live);

        return Target.IsMock
            ? new Regex($"^{Regex.Escape(mock)}$")
            : new Regex($"^{Regex.Escape(live)}(?: .+)?$");
    }
}
