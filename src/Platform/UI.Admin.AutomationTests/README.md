# Admin UI automation tests

Browser tests of the integrated 2S Platform: the deployed admin UI (`src/Platform/ui.admin`), the real Storyteller API and the AuthKit sign-in, driven by **Playwright** under **TUnit**, on the reusable libraries `42.Testing.UI.Automation.*` (`src/Libraries/Testing/UI.Automation.*`).

**Documentation:** [Admin UI Automation Tests](../../../docs/Platform/UI.Admin.AutomationTests/automation-tests.md) (running, settings and profiles, users, the AuthKit checklist, the target check, the mock target, the Elementary set, the catalogue and its modes, Testiny, BrowserStack, the CI workflow, troubleshooting) and, for every setting and attribute of the framework, [UI Automation](../../../docs/Libraries/Testing/UI.Automation/readme.md). Specs: [UI Automation Testing Project](../../../docs/Platform/UI.Admin.AutomationTests/specs/2026-10-31%20UI%20Automation%20Testing%20Project.md) and [Initial Test Cases of the Admin UI and Their Automation](../../../docs/Platform/UI.Admin.AutomationTests/specs/2026-11-02%20Initial%20Test%20Cases%20of%20the%20Admin%20UI%20and%20Their%20Automation.md). Every case and its test: [test-cases.md](../../../docs/Platform/UI.Admin.AutomationTests/test-cases.md).

```bash
dotnet build src/Platform/UI.Admin.AutomationTests/src
pwsh src/Platform/UI.Admin.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install chromium firefox webkit   # once: Playwright 1.63's browsers

dotnet run --project src/Platform/UI.Admin.AutomationTests/src                                                   # against the local dev server (port 5173); UITESTS_Target__BaseUrl= skips
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"   # the PR gate set
UITESTS_PROFILE=mock dotnet run --project src/Platform/UI.Admin.AutomationTests/src                              # against npm run dev:mock (:5174)
```

Run it with `dotnet run` (options after `--`) or `dotnet test --project src/Platform/UI.Admin.AutomationTests/src` (options without `--`); `dotnet test --solution 42.mono.slnx` runs it with the rest of the repository's tests on Microsoft.Testing.Platform, skipped without `Target:BaseUrl`.

| Path | Contents |
|---|---|
| `src/PlatformAutomationSetup.cs` | The setup (`[assembly: UiAutomationSetup<…>]` and the other assembly attributes): AuthKit sign-in, the Testiny reporter, the settings' checks |
| `src/PlatformTargetCheck.cs` | The check of the target before the first test |
| `src/PlatformTarget.cs`, `src/testsettings*.json` | The Platform's `Target` keys, the committed defaults and the profiles |
| `src/PlatformTest*.cs` | The tests' base class: its target and routes, the roles and `Target:RoleProjects` with the mode skips (`PlatformTest.Roles.cs`), the helpers (`PlatformTest.Helpers.cs`), and the sections' partials |
| `src/Attributes/` | `[Area]`, `Role` and `[UserRole]`, `[DesktopOnly]`, `[PhoneOnly]`, `[MockOnly]`, `[DisposableProject]` |
| `src/Access/`, `src/Data/`, `src/Pages/`, `src/Support/` | The AuthKit sign-in flow, the generated data, the page objects (one partial file per section that extends a shared one), and the test support (`MockApi`, the clipboard and request recorders, downloads, `Eventually`) |
| `src/Tests/<Area>/` | The tests: one folder per Testiny section, one class per sub-folder |
