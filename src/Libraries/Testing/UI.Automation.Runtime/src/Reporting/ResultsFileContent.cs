namespace _42.Testing.UI.Automation.Runtime.Reporting;

// A results.jsonl as read back. Run is null when the header line is missing; Warnings name the lines that were skipped
// (a run that crashed may end with a cut line).
public sealed record ResultsFileContent(
    TestRunDescriptor? Run,
    IReadOnlyList<TestIdentity> PlannedTests,
    IReadOnlyList<TestResultReport> Results,
    IReadOnlyList<string> Warnings);
