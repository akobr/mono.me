# Admin UI automation tests

Browser tests of the integrated 2S Platform: the deployed admin UI (`src/Platform/ui.admin`), the real Storyteller API and the AuthKit sign-in, driven by **Playwright** under **TUnit**, on the reusable libraries `42.Testing.UI.Automation.*` (`src/Libraries/Testing/UI.Automation.*`).

**Documentation:** [Admin UI Automation Tests](../../../docs/Platform/UI.Admin.AutomationTests/automation-tests.md) (running, settings and profiles, users, the AuthKit checklist, the target check, the mock target, the Elementary set, Testiny, BrowserStack, the CI workflow, troubleshooting) and, for every setting and attribute of the framework, [UI Automation](../../../docs/Libraries/Testing/UI.Automation/readme.md). Spec: [UI Automation Testing Project](../../../docs/Platform/UI.Admin.AutomationTests/specs/2026-10-31%20UI%20Automation%20Testing%20Project.md).

```bash
dotnet build src/Platform/UI.Admin.AutomationTests/src
pwsh src/Platform/UI.Admin.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install chromium firefox webkit   # once: Playwright 1.63's browsers

dotnet run --project src/Platform/UI.Admin.AutomationTests/src                                                   # skipped without Target:BaseUrl
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"   # the PR gate set
UITESTS_PROFILE=mock dotnet run --project src/Platform/UI.Admin.AutomationTests/src                              # against npm run dev:mock (:5174)
```

Run it with `dotnet run` (options after `--`), not `dotnet test`: TUnit on Microsoft.Testing.Platform refuses the VSTest mode the repository keeps for its xUnit projects, so the traversals build this project and leave it out of `dotnet test src`.

| Path | Contents |
|---|---|
| `src/PlatformAutomationSetup.cs` | The setup (`[assembly: UiAutomationSetup<…>]` and the other assembly attributes): AuthKit sign-in, the Testiny reporter, the settings' checks |
| `src/PlatformTargetCheck.cs` | The check of the target before the first test |
| `src/PlatformTarget.cs`, `src/testsettings*.json` | The Platform's `Target` keys, the committed defaults and the profiles |
| `src/Access/`, `src/Data/`, `src/Pages/` | The AuthKit sign-in flow, the generated data, the page objects |
| `src/Tests/<Area>/` | The tests |
