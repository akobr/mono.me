namespace _42.Testing.UI.Automation.Runtime.Reporting;

// One line of results.jsonl: Kind "run" with the run and the planned tests (the first line), or "result" with one report.
internal sealed record ResultsFileLine(
    string Kind,
    TestRunDescriptor? Run = null,
    IReadOnlyList<TestIdentity>? PlannedTests = null,
    TestResultReport? Result = null);
