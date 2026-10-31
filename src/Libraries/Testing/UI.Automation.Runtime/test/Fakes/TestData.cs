namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

internal static class TestData
{
    public static readonly DateTimeOffset Start = new(2026, 10, 31, 14, 25, 0, TimeSpan.Zero);

    public static TestIdentity Identity(string method, params TestCaseLink[] cases)
    {
        return new TestIdentity(
            $"42.Tests/Sample.Tests.SampleTests/{method}",
            method,
            "Sample.Tests.SampleTests",
            method,
            "42.Tests",
            ["Elementary"],
            cases);
    }

    public static TestResultReport Report(string method, TestOutcome outcome = TestOutcome.Passed, params TestCaseLink[] cases)
    {
        return Report(Identity(method, cases), outcome);
    }

    public static TestResultReport Report(TestIdentity test, TestOutcome outcome = TestOutcome.Passed)
    {
        return new TestResultReport(
            test,
            outcome,
            Start,
            TimeSpan.FromSeconds(3),
            1,
            outcome == TestOutcome.Failed ? new TestFailure("Boom", "System.InvalidOperationException", "at Sample.Tests", "SampleTests.cs", 42) : null,
            "output",
            [],
            new Dictionary<string, string> { ["engine"] = "chromium" });
    }

    public static TestRunDescriptor Run(string runId = "20261031-142500-abcd")
    {
        return new TestRunDescriptor(runId, "UI automation", "description", Start, new Dictionary<string, string> { ["profile"] = "mock" });
    }
}
