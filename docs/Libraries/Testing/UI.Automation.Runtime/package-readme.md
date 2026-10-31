# 42.Testing.UI.Automation.Runtime

The engine-neutral runtime of the 42 UI automation framework on **Playwright** (`Microsoft.Playwright` 1.63.0):

- **Settings** in layers: `testsettings.json`, `testsettings.<profile>.json` (`UITESTS_PROFILE`), `testsettings.local.json`, the test assembly's user secrets, `UITESTS_*` variables and the engine's parameters; validated, logged with secrets as `***`; without `Target:BaseUrl` every UI test is skipped. The secrets' values (and the pool users' e-mails) are `***` in every report's failure, stack trace and output, also where a Playwright call log kept what a fill typed.
- **Browsers**: the local Playwright browsers, or **BrowserStack** over its Playwright endpoint (session per test or per class, named and marked passed or failed, limited to the plan's sessions; typed values and storage states masked in BrowserStack's logs); mobile by device emulation.
- **Users**: a pool of pre-provisioned users leased per test by role, signed in once per session through the project's `ISignInFlow`, with the storage state cached in memory.
- **The target check**: `IUiAutomationSetup.CheckTargetAsync` checks once, on a signed-in page, that the target can run the tests; a failure fails every UI test at once with its message.
- **Seeded data** (`SeededData`, Bogus), **artifacts** per test (screenshot, trace, video, console log) and a **reporting dispatcher** with batching, back-off, run warnings and a replayable `results.jsonl`.

A project describes itself in one `IUiAutomationSetup` (its sign-in flows, reporter providers, validation and target check). An engine adapter drives `AutomationSession` and `TestScope`; for TUnit that is `42.Testing.UI.Automation.Runtime.TUnit`.

```jsonc
// testsettings.json
{
  "Target": { "BaseUrl": "", "SignIn": "MySignIn" },
  "Browser": { "Provider": "Local", "Engine": "chromium", "Headless": true },
  "Execution": { "MaxParallelTests": 4, "Trace": "OnFailure" },
  "Users": { "Pool": [ { "Email": "ui-test-01@example.com", "Roles": [ "Owner" ] } ] }
}
```

Documentation: [UI Automation](https://github.com/akobr/mono.me/blob/main/docs/Libraries/Testing/UI.Automation/readme.md), with the reference of every setting.
