# CLI automation tests

Tests of the real `sform` CLI (the build of `src/Platform/Cli/src`, started out of process) against a real Storyteller deployment: pool users signed in through the AuthKit device code (approved by Playwright on the AuthKit page) and machines signed in from the `STORYTELLER_*` variables, under **TUnit**, on the reusable libraries `42.Testing.UI.Automation.*` (`src/Libraries/Testing/UI.Automation.*`) as tests without a browser, and the shared Platform pieces of [`42.Platform.Automation.Shared`](../Automation.Shared/README.md).

**Documentation:** [CLI Automation Tests](../../../docs/Platform/Cli.AutomationTests/automation-tests.md) (running, settings and profiles, users and machines, the sign-in, running sform, the target check, writing a test, the self tests, Testiny, troubleshooting) and, for every setting of the framework, [UI Automation](../../../docs/Libraries/Testing/UI.Automation/readme.md). Spec: [CLI Automation Testing Project](../../../docs/Platform/Cli.AutomationTests/specs/2026-11-08%20CLI%20Automation%20Testing%20Project.md). Every case and its test: [test-cases.md](../../../docs/Platform/Cli.AutomationTests/test-cases.md).

```bash
dotnet build src/Platform/Cli.AutomationTests/src                                                           # builds sform too
pwsh src/Platform/Cli.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install chromium                 # once: the device page's browser

dotnet test --project src/Platform/Cli.AutomationTests/src                                                  # without Target:BaseUrl: self tests run, CLI tests skipped
UITESTS_PROFILE=dev dotnet run --project src/Platform/Cli.AutomationTests/src                               # against the local API (:7071)
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"   # the PR gate set
```

Run it with `dotnet run` (options after `--`) or `dotnet test --project src/Platform/Cli.AutomationTests/src` (options without `--`); `dotnet test --solution 42.mono.slnx` runs it with the rest of the repository's tests on Microsoft.Testing.Platform: its self tests run, every CLI test is skipped without `Target:BaseUrl` (the committed default).

| Path | Contents |
|---|---|
| `src/CliAutomationSetup.cs` | The setup (`[assembly: UiAutomationSetup<…>]` and the other assembly attributes): no runtime sign-in flow (`Target:SignIn None`), the Testiny reporter, the settings' checks, the target check without a page |
| `src/CliTargetCheck.cs` | The check of the target before the first test, through sform: it starts, a pool user signs in, the project is readable, each configured machine reads |
| `src/CliTarget.cs`, `src/CliSettings.cs`, `src/CliFeatures.cs`, `src/testsettings*.json` | The CLI's `Target` keys (over the shared `PlatformTargetSettings`), the `Cli` section, the feature names, the committed defaults and the profiles (`dev`, `ci`, `reporting-testiny`) |
| `src/CliTest*.cs` | The tests' base class: running sform (`Sform`, `StartSform`, `StartSformInTerminal`, `InProject`), the roles and the skips (`CliTest.Roles.cs`), the sign-in and other users (`CliTest.SignIn.cs`), machines (`CliTest.Machines.cs`), a disposable project (`CliTest.Projects.cs`) |
| `src/Running/` | `SformRunner`, `SformRunOptions`, `SformResult`, `SformProcess`, `CliExit`, the environment built from scratch, the locator of the built sform, escape sequences, JSON (`WrappedJson`: the 80-column folds of WriteJson joined), the secrets sform prints; `Processes/` (redirected with the console encodings and the process job, Windows ConPTY, Linux `script`), `Terminal/` (the keys) |
| `src/Access/` | The homes (`SFORM_HOME`) and working directories (outside the artifacts), the device code sign-in and the AuthKit device page, the machines (`Machines` settings, `MachineCredentials`, `MachineCreation`, `CreatedMachine`), another pool user (`SformActor`, `AccountOutput`) |
| `src/Attributes/` | `[Area]`, `[MachineOnly]`, `[FreshSignIn]`, `[Terminal]`, `[DisposableProject]`, `[RequiresFeature]`, `[RequiresMachine]`, `[Covers]`; `Role` and `[UserRole]` come from the shared library (imported globally) |
| `src/Data/` | `CliData`, the seeded names (`rst.cli-<runId>-…`, `cli_<runId>.…`) |
| `src/SelfTests/` | Plain TUnit tests of the machinery that need no target (category `SelfTest`) |
| `src/Tests/<Section>/` | The tests: one folder per section of the catalogue, one class per sub-folder |
