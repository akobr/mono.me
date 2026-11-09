using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Processes;
using _42.Platform.Cli.AutomationTests.Running.Terminal;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// The pseudo terminal (ConPTY on Windows, util-linux script on Linux) with the built sform and no target: assist key asks
// its questions with Sharprompt before any call, so the test answers them as a person would (ACC-TLS-02 without a
// target): Down and Enter pick Unit, two names follow, and the key is printed.
[Category(CliCategories.SelfTest)]
[Category(CliCategories.Terminal)]
public sealed class TerminalSelfTests
{
    [Test]
    public async Task AssistKey_AnswersInATerminal_PrintsTheKey()
    {
        SkipWithoutTerminal();
        using var sandbox = new SelfTestSandbox(timeout: TimeSpan.FromSeconds(90));

        await using var terminal = await sandbox.Runner.StartAsync(["assist", "key"], new SformRunOptions { Terminal = true });
        await terminal.ExpectAsync("What type of annotation you need");
        await terminal.SendKeyAsync(TerminalKey.Down);
        await terminal.SendKeyAsync(TerminalKey.Enter);
        await terminal.ExpectAsync("Name of the responsibility");
        await terminal.SendLineAsync("my-app");
        await terminal.ExpectAsync("Name of the job");
        await terminal.SendLineAsync("nightly");
        var result = await terminal.WaitForExitAsync();

        result.ShouldSucceed().ShouldContain("Your annotation key should be unt.my-app.nightly");
        await Assert.That(result.IsTerminal).IsTrue();
        await Assert.That(result.StdErr).IsEmpty();
    }

    // The same command in a terminal is no redirected input: it does not stop with 43, and Escape or Ctrl+C cancels it.
    [Test]
    public async Task AssistKey_CancelledInATerminal_Ends()
    {
        SkipWithoutTerminal();
        using var sandbox = new SelfTestSandbox(timeout: TimeSpan.FromSeconds(90));

        await using var terminal = await sandbox.Runner.StartAsync(["assist", "key"], new SformRunOptions { Terminal = true });
        await terminal.ExpectAsync("What type of annotation you need");
        await terminal.SendKeyAsync(TerminalKey.CtrlC);
        var result = await terminal.WaitForExitAsync(TimeSpan.FromSeconds(60));

        result.ShouldNotContain("the input is redirected", SformStream.Terminal);
        await Assert.That(result.ExitCode).IsNotEqualTo((int)CliExit.InteractionNeeded);
    }

    private static void SkipWithoutTerminal()
    {
        if (TerminalSupport.Reason() is { } reason)
        {
            Skip.Test($"Terminal: {reason}");
        }
    }
}
