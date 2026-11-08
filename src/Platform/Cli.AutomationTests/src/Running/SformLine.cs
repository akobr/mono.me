namespace _42.Platform.Cli.AutomationTests.Running;

// One line of a run, as it arrived; Text is plain (no escape sequences), At is the time since the start.
public sealed record SformLine(SformStream Stream, string Text, TimeSpan At);
