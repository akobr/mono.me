# GitHub Workflows

The workflows in `.github/workflows`: what each one does and when it runs. All push and pull request triggers use path
filters, so a workflow runs only when files in its area change. The naming of workflows, their files and the GitHub
environments is described in [naming-convention.md](naming-convention.md).

## Automatic checks (on pushes and pull requests)

| Workflow | What it does | When it runs |
|---|---|---|
| **Docs.42for.net** (`Docs.42for.net.yml`) | Builds the VitePress docs site and deploys it to Azure Static Web Apps | On a push or PR to `main` that touches `docs/42for.net/**` |
| **Platform.UI.Admin** (`Platform.UI.Admin.yml`) | `verify`: lints, type-checks, tests and builds the Vue admin UI and the TypeScript SDK, and runs the Playwright suites against the mocked API. `deploy`: deploys `main` to Azure Static Web Apps | On a push or PR to `main` that touches `src/Platform/ui.admin/**`, `src/Platform/Storyteller/sdk.typescript/**` or `src/Platform/Storyteller/DbCreator/src/data-sets/showcase/**` (DbCreator's set `showcase`, a snapshot of the showcase mock that `npm test` checks). Only a push to `main` deploys |
| **Platform.Storyteller-conformance** (`Platform.Storyteller-conformance.yml`) | Runs the conformance suite: the Configuration and ConfigurationFormats tests on Linux and Windows, plus the TypeScript SDK tests | On a push or PR to `main` that touches the conformance vectors, the Configuration or ConfigurationFormats libraries, either SDK, KeyValues.ClientEncryption, or the root build files (`global.json`, `nuget.config`, `version.json`, …). Also by hand, and called by the SDK release as its gate |
| **Platform.Storyteller.CosmosDb** (`Platform.Storyteller.CosmosDb.yml`) | Runs the backend's Cosmos DB suites on Testcontainers emulators. A vNext job runs the tests without the `Emulator=Classic` trait. A classic-emulator job runs only that trait on a PR, and the full suites otherwise | On PRs that touch `src/Platform/Storyteller/**` or the root build files (no push trigger). Also by hand, or called by another workflow |
| **Platform.Cli.UnitTests** (`Platform.Cli.UnitTests.yml`) | Runs the `sform` unit tests and the CLI automation self tests and coverage checks. Needs no target and no secrets | On a PR, or a push to `main`, that touches the CLI, its automation tests, `Automation.Shared`, the UI.Automation libraries, the CLI Toolkit, `Sdk.NSwag`, the CLI test-case catalogue or `Directory.Packages.props` |
| **Platform.Deployment** (`Platform.Deployment.yml`) | Runs `bicep build` / `build-params` on the Azure templates and PSScriptAnalyzer on the scripts. Does not sign in to Azure | On a PR, or a push to `main`, that touches `src/Platform/Deployment/**` |

## Automation test runs (by hand or called)

| Workflow | What it does | When it runs |
|---|---|---|
| **Platform.UI.Admin.AutomationTests** (`Platform.UI.Admin.AutomationTests.yml`) | Runs the TUnit and Playwright tests of the admin UI (locally or on BrowserStack) against a deployed target, or against the mock build served inside the job (`use_mock_ui`). Can report results to Testiny | Only `workflow_dispatch` or `workflow_call`. Runs against deployed targets queue one at a time because they share the test users |
| **Platform.Cli.AutomationTests** (`Platform.Cli.AutomationTests.yml`) | Runs the real `sform` against a deployed API, with device-code sign-ins approved through Chromium. Can report results to Testiny | Only `workflow_dispatch` or `workflow_call`. Runs queue one at a time because they share the CLI test users |

## Releases (by hand)

| Workflow | What it does | When it runs |
|---|---|---|
| **Platform.Storyteller.Sdk-release** (`Platform.Storyteller.Sdk-release.yml`) | Releases the Storyteller NuGet and npm packages after the conformance workflow passes. A dry run unless `publish` is set | `workflow_dispatch` only (inputs `packages`, `publish`) |
| **Platform.GitHubAction-release** (`Platform.GitHubAction-release.yml`) | Releases `src/Platform/GitHubAction` to its own Marketplace repository: one commit, the tag `vX.Y.Z`, the major tag `vX` moved to it, and a draft release that the owner publishes from the release form | `workflow_dispatch` only (input `version`) |
