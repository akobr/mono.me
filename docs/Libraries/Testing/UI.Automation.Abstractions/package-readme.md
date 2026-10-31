# 42.Testing.UI.Automation.Abstractions

The engine-neutral contracts of the 42 UI automation framework (Playwright, TUnit, Testiny, BrowserStack):

- `[TestCase("1234")]` links a test (or a test class) to a case of a test case management system; `System = "Testiny"` restricts the link to one reporter.
- `AutomationCategories`: the category names every engine adapter uses (`Elementary`, `Serial`, `Maintenance`, `Area-<area>`).
- The records of a run: `TestRunDescriptor`, `TestIdentity`, `TestResultReport` with `TestOutcome`, `TestFailure` and `TestArtifact`, `TestRunSummary`, and the property names in `TestRunProperties`.
- The reporter contract: `ITestRunReporter` (`StartAsync`, `ReportAsync` with batches, `CompleteAsync`, `RunUrl`), `ITestRunReporterProvider` and `TransientReporterException` for failures worth another attempt.

```csharp
[Test]
[TestCase("1234")]
public async Task SignIn_ShowsOrganizations() { … }
```

A reporter references this package only; the runtime (`42.Testing.UI.Automation.Runtime`) calls it from a background dispatcher that batches, retries transient failures with back-off and never lets a reporter fail a test.

Documentation: [UI Automation](https://github.com/akobr/mono.me/blob/main/docs/Libraries/Testing/UI.Automation/readme.md), "Reporting" and "Add a reporter".
