namespace _42.Testing.UI.Automation;

// The totals of a run, counted from the last report of each test; Failed includes Timeout and Cancelled. Warnings are
// what went wrong around the tests: settings adjustments, reporter failures, unmapped tests.
public sealed record TestRunSummary(DateTimeOffset FinishedAt, int Passed, int Failed, int Skipped, int Blocked, IReadOnlyList<string> Warnings)
{
    public int Total => Passed + Failed + Skipped + Blocked;
}
