namespace _42.Platform.Cli.AutomationTests.Running;

// The assertions of SformResult on the task of a run, so a test writes await Sform(…).ShouldSucceed().
public static class SformResultTaskExtensions
{
    public static async Task<SformResult> ShouldSucceed(this Task<SformResult> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return (await run).ShouldSucceed();
    }

    public static async Task<SformResult> ShouldExitWith(this Task<SformResult> run, CliExit expected)
    {
        ArgumentNullException.ThrowIfNull(run);
        return (await run).ShouldExitWith(expected);
    }

    public static async Task<SformResult> ShouldFail(this Task<SformResult> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return (await run).ShouldFail();
    }
}
