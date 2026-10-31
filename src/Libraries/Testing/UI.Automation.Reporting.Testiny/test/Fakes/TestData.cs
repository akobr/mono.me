namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests.Fakes;

internal static class TestData
{
    public static readonly DateTimeOffset Start = new(2026, 10, 31, 14, 25, 0, TimeSpan.Zero);

    public static TestRunDescriptor Run(string name = "UI automation 2026-10-31 15:25 mock")
    {
        return new TestRunDescriptor(
            "20261031-142500-abcd",
            name,
            "| Setting | Value |\n|---|---|\n| Target | http://localhost:5174/ |\n",
            Start,
            new Dictionary<string, string>
            {
                [TestRunProperties.RunId] = "20261031-142500-abcd",
                [TestRunProperties.Provider] = "Local",
                [TestRunProperties.Engine] = "chromium",
                [TestRunProperties.BrowserVersion] = "142.0",
                [TestRunProperties.BuildUrl] = "https://github.com/akobr/mono.me/actions/runs/1",
            });
    }

    public static TestIdentity Test(string method, params TestCaseLink[] cases)
    {
        return new TestIdentity(
            $"42.Suite/_42.Suite.Tests.SampleTests/{method}",
            method,
            "_42.Suite.Tests.SampleTests",
            method,
            "42.Suite",
            [],
            cases);
    }

    public static TestCaseLink Case(string id, string? system = null)
    {
        return new TestCaseLink(id, system);
    }

    public static TestResultReport Result(
        TestIdentity test,
        TestOutcome outcome,
        int attempt = 1,
        TestFailure? failure = null,
        IReadOnlyList<TestArtifact>? artifacts = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        return new TestResultReport(
            test,
            outcome,
            Start.AddMinutes(1),
            TimeSpan.FromMilliseconds(1500),
            attempt,
            failure,
            null,
            artifacts ?? [],
            properties ?? new Dictionary<string, string>
            {
                [TestRunProperties.Engine] = "chromium",
                [TestRunProperties.Provider] = "Local",
                [TestRunProperties.Device] = "Pixel 7",
                [TestRunProperties.Seed] = "1234",
                [TestRunProperties.RemoteSession] = "SampleTests.Fails",
                [TestRunProperties.Attempt] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
    }

    public static TestFailure Failure()
    {
        return new TestFailure(
            "Timeout 15000ms exceeded.\nCall log: waiting for GetByRole(Heading)",
            "Microsoft.Playwright.PlaywrightException",
            "Microsoft.Playwright.PlaywrightException: Timeout 15000ms exceeded.\n   at _42.Suite.Tests.SampleTests.Fails() in /src/Tests/SampleTests.cs:line 21\n   at ```odd frame```",
            "/src/Tests/SampleTests.cs",
            21);
    }
}
