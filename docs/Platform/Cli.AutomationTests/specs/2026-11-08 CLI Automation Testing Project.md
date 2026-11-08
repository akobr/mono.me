# CLI Automation Testing Project

Linear: [424-215](https://linear.app/424fun/issue/424-215/create-cli-automation-testing-project-sform-against-a-real-backend)

Line numbers and file paths refer to `main` at `a6974e62`. This document is the plan; no code is changed by it.

## Problem

The `sform` CLI (`src/Platform/Cli`) is tested only by `Cli.UnitTests` (xUnit, in process, with Moq, the mock file system and `InternalsVisibleTo`). Nothing runs the real `sform` executable against a real Storyteller deployment: the real sign-in (AuthKit device code, machine credentials), the real API, the files the CLI writes and the exit codes a script depends on. The admin UI got this kind of coverage with `UI.Admin.AutomationTests` (spec `docs/Platform/UI.Admin.AutomationTests/specs/2026-10-31 UI Automation Testing Project.md`); the CLI needs the same.

### Decisions taken with the owner (2026-11-08)

| Topic | Decision |
|---|---|
| Where the tests live | A **new project**, `src/Platform/Cli.AutomationTests/src` (`42.Platform.Cli.AutomationTests`), on the `42.Testing.UI.Automation.*` libraries. Not in `Cli.UnitTests` (in process, hermetic, no secrets, part of every solution run) and not in `UI.Admin.AutomationTests` (another product, release cycle, Testiny project, browser matrix and CI image). |
| What runs | The **source build** of `src/Platform/Cli/src` (its build output, started out of process). An installed tool version can be pointed at later through the same setting, but is not a goal. |
| Sign-in | **Both**: pool users through the AuthKit **device code** (the code page finished by Playwright), used by the role tests; and **machine credentials** (`STORYTELLER_*` variables), non-interactive. |
| Test cases | A **new Testiny project** for the CLI, key **`2SC`**, not a section of `2SP`. |
| Pool users | A **separate CLI pool** (`cli-test-<role>-<n>`), so the admin and the CLI runs can run at the same time. |
| Device code as JSON | Only when **`SFORM_DEVICE_CODE_OUTPUT=json`** is set; the themed text stays the default, redirected or not. |
| Scope | The work includes the test cases (written, then stored in Testiny `2SC`) and their automation; **every existing command, option and argument is covered** (§4). |
| System under test | A deployed environment or the local Aspire stack, chosen by `Target:BaseUrl` like the admin tests; without a target every test skips and the run exits 0. |

## Current State

### Where `sform` keeps its state

Every file the CLI reads or writes is either next to its assembly or in one directory per OS user:

- Next to the assembly (`Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)`): `access.default.json` (`Commands/Account/AccountSetCommand.cs:92`), `editor.config.json` (`Services/EditorService.cs:362`), `auth.discovery.json` (`Authentication/AuthenticationConfigurationResolver.cs:46`), the log file (`Configuration/LoggingOptions.cs:17`). `app.config.json` is loaded by `Startup.ConfigureApplication` (`Startup.cs:79`), user secrets only in Development (`Startup.cs:84`).
- In `MsalCacheHelper.UserRootDirectory/.42for.net` (`%LOCALAPPDATA%` on Windows, `$HOME` elsewhere): the AuthKit session (`Authentication/ProtectedTokenStore.cs:28`, DPAPI / Keychain / libsecret), the Entra ID MSAL cache (`Authentication/EntraIdAuthenticationService.cs:135`) and the telemetry installation id (`Telemetry/InstallationIdStore.cs:21`).

Consequences for tests: two `sform` processes of the same build share the default project and the session, so two pool users cannot be signed in at the same time. On Windows `UserRootDirectory` comes from the known folder API, so overriding `LOCALAPPDATA` for the child process does not move it. On Linux the libsecret entry is keyed by its schema attributes (`ProtectedTokenStore.cs:90`), not by the directory, and a CI container usually has no keyring at all.

### Sign-in

- `sform account` (`Commands/Account/AccountCommand.cs:58`): with machine credentials configured (`IMachineSignIn.IsConfigured`, from `MachineSignInSettings.FromEnvironment`) it signs the machine in. Otherwise it reuses the stored session or calls `LoginWithDeviceCodeAsync`, which prints the verification URI, the user code and the complete URI (`AccountCommand.cs:238-258`), opening a browser only with `-b`.
- A user in several organizations gets an interactive `Console.Select` (`AccountCommand.cs:263`). `sform account set` without `-p` also selects interactively (`AccountSetCommand.cs:115`).
- Interactive prompts exist in 15 commands (access points, account, register, assist, config patch, key-values, storyteller delete/set); most have `-y|--yes`.

### The automation libraries

- `TestScope.BeginAsync` (`src/Libraries/Testing/UI.Automation.Runtime/src/TestScope.cs:128`) always acquires a browser lease, a context and a page; `TestScopeOptions` has no way to skip them.
- `UiTest` (`UI.Automation.Runtime.TUnit/src/UiTest.cs:22`) exposes `Page` and `Context` and builds the scope; user leasing (`LeasesUser`, `RequiredUser`), seeded data, artifacts, retries and reporting do not depend on the browser.
- `ISignInFlow` (`Runtime/src/Access/ISignInFlow.cs`) is page based; `StorageStateCache` keeps one signed-in storage state per pool user, which includes the AuthKit session cookie on the auth domain.
- Settings layering, `Target:BaseUrl` skip, `IUiAutomationSetup` with the target check, the user pool, `[TestCase]`, `[Elementary]`, `[Area]`, `[Serial]`, `[ConfiguredRetry]`, `results.jsonl` and the Testiny reporter are browser independent.

### Platform pieces in the admin project

`PlatformTarget` (62 lines), `PlatformTargetCheck` (185, page based), `PlatformAutomationSetup` (122), `Support/StorytellerApi` (189, on Playwright's `IAPIRequestContext`) and `Attributes/Role`, `UserRoleAttribute` live in `UI.Admin.AutomationTests`. They are small and partly UI specific.

## Proposed Changes

### 1. CLI changes that make it testable (and better for users)

1. **State directory override.** A variable `SFORM_HOME` (absolute path). When set, every file listed above moves under it: `access.default.json`, `editor.config.json`, `auth.discovery.json`, `logs/`, and the `.42for.net` content (AuthKit session, MSAL cache, `telemetry.json`). A single `CliPaths` service (`IFileSystem`-based) resolves the directories and replaces the scattered `Assembly.Location` / `UserRootDirectory` calls. Unset, the paths stay as they are. A global tool install also benefits: the default project no longer has to live in the shared tool directory (out of scope here, noted in the review).
2. **File token store.** `SFORM_TOKEN_STORE=file` stores the AuthKit session and the MSAL cache unencrypted in the state directory. It is honoured **only together with `SFORM_HOME`**, and `sform account` prints a warning when it is in use. This is what makes per-user isolation work on Linux CI without a keyring.
3. **Non-interactive organization.** `sform account -o|--organization <id>` picks the organization of a device-code sign-in; with input redirected and no option, the command fails with `ERROR_WRONG_INPUT` and lists the organizations instead of hanging on `Console.Select`. The same rule for `sform account set`: `-p` is required when input is redirected.
4. **No hangs on redirected input.** Every remaining `Console.Select` / confirmation checks `IsInputRedirected` (like `StorytellerSetCommand.cs:260` already does) and fails fast with the option to pass, unless `--yes` was given. A test of `Cli.UnitTests` covers each command.
5. **Machine-readable device code.** With `SFORM_DEVICE_CODE_OUTPUT=json` (and only then), the prompt is also written as one JSON line on stderr (`{"userCode":…,"verificationUri":…,"verificationUriComplete":…,"expiresIn":…}`), so the tests parse a contract rather than themed text.
6. **Browser command.** `sform account -b` opens the sign-in page with the command in `SFORM_BROWSER` (or, when that is unset, the conventional `BROWSER`), called with the URL as its only argument, instead of the shell's default browser. Unset, the behaviour stays as it is. When the command cannot be started, the CLI prints that the browser could not be opened and the sign-in goes on. This makes `-b` testable on every OS: on Windows a shell-executed URL follows the protected default-browser association, which `PATH` does not affect.

Docs: `docs/Platform/Cli/` gets the variables and the options.

### 2. Runtime: tests without a browser

1. `TestScopeOptions.Browser` (`bool`, default `true`). With `false`, `BeginAsync` leases the user, creates data and artifacts, but acquires no browser lease, context or page; `Page`/`Context`/`Browser` throw a clear `InvalidOperationException`. Trace/video/screenshot policies do not apply; artifacts are what the test attaches.
2. `TestScope.OpenBrowserAsync(TestUser? asUser)` acquires a lease on demand and returns a context (signed in through `StorageStateCache` when a user is given). The CLI tests use it only to approve a device code.
3. `UI.Automation.Runtime.TUnit`: a new base class **`AutomationTest`** with everything of `UiTest` that is not the page (user leasing, `RequiredUser`, `Scope`, data, `CleanUpAsync`, skip reason, reporting). `UiTest : AutomationTest` adds `Page`/`Context` and sets `Browser = true`, so the admin project does not change.
4. The settings validator and the session start do not start a browser provider before one is asked for (the local provider is lazy; BrowserStack is not used by the CLI profiles).
5. Library docs: `docs/Libraries/Testing/UI.Automation/readme.md` gets a "Tests without a browser" section.

The names `UI.Automation.*` stay: a command line is a user interface too.

### 3. The project `src/Platform/Cli.AutomationTests/src`

`42.Platform.Cli.AutomationTests`, namespace `_42.Platform.Cli.AutomationTests`, TUnit, `OutputType Exe`, `IsTestProject`, own `UserSecretsId`, references `UI.Automation.Runtime.TUnit` and `UI.Automation.Reporting.Testiny`, plus `src/Platform/Cli/src/Cli.csproj` with `ReferenceOutputAssembly="false"` so that building the tests builds the CLI. Added to `42.mono.slnx`. Follows the exception in CLAUDE.md for automation projects (TUnit, folder `src/`).

Layout:

```
Cli.AutomationTests/src/
  CliAutomationSetup.cs        IUiAutomationSetup: validation, sign-in flow registration, target check
  CliTarget.cs                 Target:* binding (BaseUrl = the API base incl. /api, Organization, Project, View, RoleProjects)
  CliTargetCheck.cs            signs a pool user in and checks org/project/view through sform
  Running/
    SformRunner.cs             starts the process, returns SformResult
    SformResult.cs             ExitCode, StdOut, StdErr, Duration, Json<T>()
    SformHome.cs               the per-user / per-test SFORM_HOME directories
  Access/
    DeviceCodeSignIn.cs        user sign-in: runs `sform account -o`, approves the code with Playwright
    MachineCredentials.cs      the STORYTELLER_* set of a machine
  Attributes/                  Role, [UserRole], [MachineOnly], [Area], categories
  CliTest.cs                   base class (AutomationTest): Sform(...) helpers, as user / as machine
  Tests/<Area>/                one folder per Testiny section, one class per sub-folder
  testsettings*.json           committed defaults, dev, ci, reporting-testiny
```

#### 3.1 Running `sform`

- `Cli:Path`: the path of `42.Platform.Cli.dll` (run as `dotnet <dll>`) or of an executable. Default: the build output of the referenced project, found next to the test assembly's configuration (`src/Platform/Cli/src/bin/<Configuration>/net10.0/`) and checked at session start.
- `SformRunner.RunAsync(args, SformRunOptions)`: arguments as a list (no shell quoting), working directory per test (under the artifacts directory), environment built from scratch plus a whitelist (`PATH`, `DOTNET_*`, `SystemRoot` …), always `SFORM_HOME`, `SFORM_TOKEN_STORE=file`, `general:baseUrl` from `Target:BaseUrl`, telemetry and Sentry off, `NO_COLOR`/plain output, `DOTNET_ENVIRONMENT=Production` (no user secrets of the CLI).
- Terminal mode (`SformRunOptions.Terminal`): the process runs in a pseudo terminal (ConPTY on Windows, `openpty` on Linux and macOS), and the test answers the prompts by expecting a text and sending keys. This is needed for the interactive cases (mode T in `test-cases.md`): confirmations, selections, hidden input and `account register`.
- Stdin: optional text; otherwise redirected and closed, so a prompt fails instead of hanging (§1.4). A timeout (`Cli:TimeoutMs`, default 120 s) kills the process tree and fails the test with the output so far.
- Every run is appended to the test's artifacts (`sform-<n>.txt`: command line, environment names, exit code, stdout, stderr, duration) through `Session.Redact`, so passwords, API keys and tokens never reach Testiny.
- Assertions helpers: `ShouldSucceed()`, `ShouldExitWith(ExitCodes.X)` (the constants of `src/Platform/Cli/src/ExitCodes.cs`, duplicated as a test-side enum to keep the CLI's internals internal), `Json<T>()` for commands with JSON output.

#### 3.2 Users: device code with Playwright

- Each pool user gets a **state directory per session** (`<run>/sform-home/<user fingerprint>`). The first test that leases the user signs in: `sform account -o <org>` is started, the JSON prompt (§1.5) read from stderr, then `Scope.OpenBrowserAsync(user)` opens a context with the user's cached AuthKit storage state (signed in through the admin project's AuthKit page flow the first time, which moves to a shared place, §3.5), navigates to `verificationUriComplete` and confirms; the process then ends with success.
- Later tests of the same user reuse the directory (the refresh token rotates inside it, so it is never copied). A lease gives exclusive use, so parallel tests never share a directory. Before each test the runner deletes `access.default.json` and `editor.config.json`, so a test starts with no default project unless it sets one.
- A test of the sign-in itself (`[FreshSignIn]`) gets an empty directory instead.
- Phase 3 verifies that the AuthKit device page accepts the cached session cookie (only a confirm button) and records what it shows otherwise.

#### 3.3 Machines

- `Machines:<name>`: pre-provisioned machine credentials of the target (API key in the user secrets, `Machines:<name>:ApiKey`; certificate path/password later), with the project and scopes they hold. A test asks for one with `AsMachine("<name>")`; the runner sets the `STORYTELLER_*` variables and no user state.
- Tests of `sform machine create/renew/reset/delete` create their own machine as an Owner/Administrator user with a generated name, use its credential in the same test and delete it in `CleanUpAsync`.

#### 3.4 Target check

Before the first test: one pool user signs in (§3.2) and `sform` reads the configured organization, project and view; a failure fails every test at once with its message, as in the admin project. Each configured machine is checked with `sform account` as that machine.

#### 3.5 Shared Platform code

The AuthKit page flow (`AuthKitSignIn`), `Role` and the `Target` shape are needed by both projects. They move to a small library **`src/Platform/Automation.Shared/src`** (`42.Platform.Automation.Shared`, not packable), referenced by both. `PlatformTargetCheck` and `StorytellerApi` stay in the admin project (page based); the CLI checks through `sform` itself. Anything else is copied only when a second user appears.

#### 3.6 Settings

`testsettings.json` (committed, no secrets, `Target:BaseUrl` empty), `testsettings.dev.json` (`http://localhost:7071/api`, Aspire / `func start`), `testsettings.reporting-testiny.json`; layering, `UITESTS_PROFILE` and `--test-parameter` as in the admin project. Sections: `Target`, `Cli` (`Path`, `TimeoutMs`), `Users` (pool), `Machines`, `Execution`, `Reporting`; `Browser` only for the device page (local headless Chromium). The pool: **its own users** (`cli-test-<role>-<n>@42for.software`, provisioned in the same AuthKit environment with the same roles as the admin pool), so the admin and the CLI runs can run at the same time without fighting over sessions.

### 4. Test cases, Testiny and full coverage

The scope includes writing the test cases, storing them in Testiny and automating every one of them. **Every existing command, with every option and argument, is covered.**

#### 4.1 What "every" means

At `a6974e62` the command tree under `src/Platform/Cli/src/Commands` has **99 commands** (`[Command]`), **180 options** (`[Option]`) and **77 arguments** (`[Argument]`), plus the `-p|--projectKey` and `-v|--view` options that `BaseContextCommand` gives to 55 commands (36 direct, 19 through `SchemaApiCommand`). Groups:

| Group | Commands |
|---|---|
| Root | `sform`, `supervisor`, `scheduler` (both placeholders today), `assist`, `assist key` |
| Account | `account` (aliases `access`, `login`), `register`, `set`, `logout`, `points` (`create`, `get`, `grant`, `revoke`), `ca` |
| Machines | `machine` (`agent`): `create`/`set`, `reset`, `delete`, `renew`, `machine-auth`, `rules`, `issuer` (`list`, `set`, `delete`) |
| Shared certificates | `shared-cert` (`certificate`): `issue`, `revoke` |
| Annotations | `storyteller` (`story`, `annotations`): list (12 options), `get`, `set`, `delete`, `restore`, `deleted`, `history`, `export`, `import` |
| Configurations | `config`: get (10 options), `set`, `patch`, `edit`, `delete`, `diff`, `versions`, `composition` |
| Templates | `template`: get, `set`, `edit`, `delete`, `diff`, `versions` |
| Schemas | `schema`: `type`, `annotation`, `descendant` (each `get`, `set`, `edit`, `delete`, `diff`, `versions`), `definition` |
| Key-values | `kv`: list, `get`, `set`, `delete`, `restore`, `purge`, `protect`, `versions`, `diff`, `export`, `import`, `reencrypt`, `policy` (`set`, `byok-setup`, `byok-verify`, `rotate`), `recipients` (`add`, `enable`, `disable`, `remove`), `audit`, `usage` |

Coverage means: each command has at least one positive case and, where it can fail, one negative case (wrong input, not found, refused by the role); each option and argument, and each alias, appears in at least one automated case that checks its effect, not only that the command accepts it. Option values with a fixed set (`--format`, `--protection`, `--mode`, `--existing`, `--mask`, `-k|--credential-kind`, …) are covered value by value.

#### 4.2 Enforced by a coverage test

- `[Covers("kv set", "--secret", "--protection")]` on a test (repeatable; the command path as typed, aliases resolved) records what it exercises.
- A meta test, `CommandCoverageTests` (not in the Elementary set), reads the command tree of `42.Platform.Cli.dll` with `System.Reflection.MetadataLoadContext` (no CLI code runs) and fails with the list of every command, option, alias and argument no test covers. It needs no target, so it also runs in `dotnet test --solution 42.mono.slnx` and a new option in the CLI without a test fails the build of the PR.
- A command that cannot be run against a target (e.g. a placeholder, or BYOK without an Azure Key Vault on the target) is listed in `coverage-exceptions.json` with a reason; its test exists and skips with that reason when the target lacks the feature (`Target:Features`).

#### 4.3 Commands with an editor or a file

- `edit` commands open the configured editor. The tests set `editor.config.json` in `SFORM_HOME` to `EditorType: Custom` with a small helper, `src/Platform/Cli.AutomationTests/editor` (console app, built with the tests), that applies a change from a file named by a variable, or leaves the file untouched (the "no change" path).
- Files the CLI reads or writes (`-i|--import`, `-o|--output`, `-e|--export`, `--rules-file`, `--jwks-file`, export zips) live in the test's working directory and are attached to the test on failure.

#### 4.4 The catalogue and Testiny

- The catalogue is written first in `docs/Platform/Cli.AutomationTests/test-cases.md`: one table per Testiny section, with id, title, role, preconditions, steps and expected result; the option and argument each case covers appear in a column, so the review can compare it with §4.1.
- The Testiny project **`2SC`** is created by hand in the Testiny app (the Testiny tools can create folders and cases, not projects). Sections (folders) are the groups of §4.1, plus "Sign-in" and "Output and exit codes" (`--github-env`, `--env-prefix`, `--mask`, formats, exit codes on errors, redirected input). The cases are created from the catalogue through the Testiny API, and their ids are written back to the catalogue and to `[TestCase("<id>")]`.
- Tests sit in `Tests/<Section>/`, one class per command group; every case is automated, none is left manual.
- **Elementary** set (the later PR gate): sign in as a user, sign in as a machine, `account set`, an annotation round trip (`set`, `get`, `delete`), a configuration `set`/`get --resolved`, a role refusal (Reader `set` exits with the unauthorized code).
- Roles: each command is run by the lowest role that may run it, and a refusal by the role below (exact roles in the projects of `Target:RoleProjects`, as in the admin tests).
- Data follows the admin rules: generated names under the configured project, not deleted (intended load); a `[DisposableProject]` test creates its own project.

### 5. CI

`.github/workflows/cli-automation.yml`: `workflow_dispatch` and `workflow_call`, inputs `profile`, `filter` (default Elementary), `target_url`, `report_testiny`; `ubuntu-latest` with the .NET SDK of `global.json` and Playwright's Chromium only (`playwright.ps1 install --with-deps chromium`), not the Playwright container. Secrets from a `cli-automation` environment, given only to the run step. Artifacts: `results.jsonl` and the `sform-*.txt` runs (already redacted). A matrix over Windows can come later; the token store work of §1.2 is what makes Linux possible.

### 6. Documentation

- `docs/Platform/Cli.AutomationTests/automation-tests.md` (run, configure, users, machines, Testiny, CI) and `test-cases.md`.
- `docs/Platform/Cli/`: the new variables and options of §1.
- `docs/Libraries/Testing/UI.Automation/readme.md`: §2.
- `CLAUDE.md`: the project row in the Platform table and the run commands.

### 7. Phases

| Phase | Content | Review |
|---|---|---|
| 1 | CLI changes (§1.1–§1.6) with unit tests and docs | `docs/Platform/Cli/specs/reviews/` |
| 2 | Runtime without a browser (§2), `Automation.Shared` extraction (§3.5), admin project still green | `docs/Libraries/Testing/UI.Automation/…/reviews/` |
| 3 | Project skeleton, runner, device-code and machine sign-in, target check, settings, the Elementary tests, local run against Aspire | `docs/Platform/Cli.AutomationTests/specs/reviews/` |
| 4 | The catalogue in `test-cases.md`, the cases in Testiny `2SC`, the coverage test, the editor helper | same |
| 5 | The automated tests of every case, group by group (one review per group or a few groups), until the coverage test passes with only the listed exceptions; CI workflow | same |

### Open questions

1. Should a Windows CI job (DPAPI token store without `SFORM_TOKEN_STORE=file`) be part of phase 5 or later?
