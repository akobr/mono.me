# 42.Testing.UI.Automation.Reporting.Testiny

The **Testiny** reporter of the 42 UI automation framework: a run becomes a classic Testiny test run whose test cases are the `[TestCase("<id>")]` ids of the tests.

- At the start: the run (named by `Reporting:RunName`, its environment in the description, optionally in a test plan) with every linked case as `NOTRUN`; with `Reporting:UnmappedTests` `Warn` or `Fail` the ids are checked once.
- Per batch of results: each case's result, worst wins over the tests that link it (`FAILED` > `BLOCKED` > `SKIPPED` > `PASSED`); for failed tests the screenshots, traces and logs as attachments and a markdown comment with the duration, environment and stack trace.
- At the end: the totals and warnings in the description, and the run closed. Transient failures (`429`, `5xx`, timeouts) are retried by the runtime's dispatcher; what Testiny refuses becomes a run warning, never a test failure.

Register it in the project's setup and turn it on in the settings (the API key only in user secrets or `UITESTS_Reporting__Testiny__ApiKey`):

```csharp
public IReadOnlyList<ITestRunReporterProvider> ReporterProviders { get; } = [TestinyReporterProvider.Instance];
```

```json
{ "Reporting": { "Enabled": true, "Systems": [ "Testiny" ], "Testiny": { "ProjectId": 12 } } }
```

Documentation: [UI Automation](https://github.com/akobr/mono.me/blob/main/docs/Libraries/Testing/UI.Automation/readme.md), sections "Reporting:Testiny" and "Testiny".
