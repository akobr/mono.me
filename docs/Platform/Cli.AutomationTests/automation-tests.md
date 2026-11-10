# CLI Automation Tests

Tests of the real `sform` executable against a real Storyteller deployment: the source build of `src/Platform/Cli/src`, started out of process the way a script or a CI job starts it, signed in as pool users through the AuthKit **device code** (the code approved by Playwright on the AuthKit page) or as **machines** (`STORYTELLER_*` variables), checking exit codes, output, JSON and files. The project is `src/Platform/Cli.AutomationTests/src` (`42.Platform.Cli.AutomationTests`, TUnit), built on the [UI automation libraries](../../Libraries/Testing/UI.Automation/readme.md) (`42.Testing.UI.Automation.*`) as [tests without a browser](../../Libraries/Testing/UI.Automation/readme.md#tests-without-a-browser-automationtest), with the Platform pieces of [`42.Platform.Automation.Shared`](../../../src/Platform/Automation.Shared/README.md). The libraries own the settings layering, the user pool, the seeded data, the artifacts, the retries and the reporting and document every setting; this page is what the CLI suite adds.

`Cli.UnitTests` (`src/Platform/Cli/test`) stays the hermetic, in-process suite of the CLI; this suite covers the integrated system.

Spec: [2026-11-08 CLI Automation Testing Project](specs/2026-11-08%20CLI%20Automation%20Testing%20Project.md) (Linear [424-215](https://linear.app/424fun/issue/424-215/create-cli-automation-testing-project-sform-against-a-real-backend)); the reviews of its phases: [Phase 1](../Cli/specs/reviews/2026-11-08%20Phase%201%20of%20CLI%20Automation%20Testing%20Project.md) (the CLI changes: `SFORM_HOME`, `SFORM_TOKEN_STORE`, `-o`, no hangs, the device code as JSON), [Phase 2](../../Libraries/Testing/UI.Automation/specs/reviews/2026-11-08%20Phase%202%20of%20CLI%20Automation%20Testing%20Project.md) (tests without a browser, the shared library), [Phase 3](specs/reviews/2026-11-08%20Phase%203%20of%20CLI%20Automation%20Testing%20Project.md) (this project) and [Phase 4](specs/reviews/2026-11-08%20Phase%204%20of%20CLI%20Automation%20Testing%20Project.md) (the catalogue, the cases in Testiny, the coverage and catalogue checks, the fake editor); phase 5 automated every case and added the CI workflow. The test cases: [test-cases.md](test-cases.md) (Testiny project `2SC`), **258 cases, each automated** by one test of `Tests/` (the defects of sform they found: [sform Defects Found by the CLI Test Cases](../Cli/specs/reviews/2026-11-08%20sform%20Defects%20Found%20by%20the%20CLI%20Test%20Cases.md)).

## Run

```bash
dotnet build src/Platform/Cli.AutomationTests/src          # builds sform (src/Platform/Cli/src) too
pwsh src/Platform/Cli.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install chromium   # once: the device page's browser (Linux: install --with-deps chromium)

dotnet test --project src/Platform/Cli.AutomationTests/src                                                   # without a target: the self tests and coverage checks run, every CliTest is skipped
UITESTS_PROFILE=dev dotnet run --project src/Platform/Cli.AutomationTests/src                                # against the local API (Aspire or func start, :7071)
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"    # the PR gate set
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=Area-KeyValues]"
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=SelfTest]"      # the suite's own machinery, no target needed
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=Coverage]"      # the command coverage and the catalogue check, no target needed
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category!=DisposableProject]"
dotnet run --project src/Platform/Cli.AutomationTests/src -- --test-parameter Target:BaseUrl=https://<host>/api --test-parameter Execution:Seed=1383911092
dotnet run --project src/Platform/Cli.AutomationTests/src -- --maximum-parallel-tests 1 --test-parameter Browser:Headless=false
dotnet test --solution 42.mono.slnx                                                                          # with every other test of the repository
```

- **`dotnet test`** runs on Microsoft.Testing.Platform (`global.json`): `--project src/Platform/Cli.AutomationTests/src <options>` takes the options of `dotnet run` without the `--`; TUnit's options (`--treenode-filter`, `--test-parameter`, `--maximum-parallel-tests`) go only with `--project`. `dotnet test` prints the suite's output only when it fails; `dotnet run` shows the `ui-automation:` lines (the run id, the seed, the effective settings with secrets as `***`, the target check, the totals and the Testiny run).
- **Without a target** (`Target:BaseUrl` empty, the committed default) every `CliTest` is skipped with `UITESTS_Target__BaseUrl is not configured; see docs/Platform/Cli.AutomationTests/automation-tests.md.`, the **self tests** (`SelfTests/`, category `SelfTest`) still run against the built `sform` (no network: their API is a closed port or a stub on the loopback), and so do the **coverage checks** (`Coverage/`, category `Coverage`: [the command coverage](#command-coverage) and [the catalogue check](#the-catalogue-check)), also in `dotnet test --solution 42.mono.slnx`. The self tests of the device page skip without Playwright's Chromium, those of the terminal where no pseudo terminal is supported. Every case has its test and every item of sform is covered, so such a run passes the self tests and the coverage checks and skips the 258 case tests (exit 0); `EveryCommandOptionArgumentAndValue_IsCovered` and `EveryCase_IsAutomated` fail only when sform gains a command, option or value, or the catalogue a row, without its test ([Automating a case](#automating-a-case)).
- **The `dev` profile** targets `http://localhost:7071/api`: start the API first (`dotnet run --project src/Platform/Aspire.Host/src`, or `func start` in `src/Platform/Storyteller/Api.Functions/src`). A Debug build of the API skips the role checks of users (`DEV_AUTH`, [access.md](../Storyteller/access.md#roles)), so the Reader and Contributor refusals show only against a Release API. sform signs in against AuthKit staging (its `app.config.json`), so the committed pool users and their passwords are needed locally too.
- Results go to `src/Platform/Cli.AutomationTests/src/bin/<configuration>/net10.0/TestResults/` (git-ignored) under `dotnet run`, or `TestResults/` of the current directory under `dotnet test`: TUnit's HTML report, and `artifacts/<runId>/` with `results.jsonl` and a folder per test (`Tests.Annotations.AnnotationEditTests/Story_SetGet_RoundTrip/`) with its `sform-<n>.txt` runs (and `work-files.txt` when its working directory was kept). The homes of sform and the working directories of the tests are **not** there but under `<temp>/sform-automation/<runId>/` ([below](#homes-and-working-directories)), as they hold plain sessions, private keys and resolved secrets.

## Configure

Settings come in layers, each over the one before ([the library's settings](../../Libraries/Testing/UI.Automation/readme.md#settings)): `testsettings.json` (committed, no secrets), `testsettings.<profile>.json` for each profile of `UITESTS_PROFILE` (comma-separated), `testsettings.local.json` (git-ignored), the user secrets of the project (`UserSecretsId` `be44b33c-c6f8-469a-aa25-4963a533f962`), `UITESTS_*` variables (`UITESTS_Target__BaseUrl`) and `--test-parameter Section:Key=value`.

```bash
cd src/Platform/Cli.AutomationTests/src
dotnet user-secrets set "Users:Pool:0:Password" "…"          # one per pool user, by position (0…7)
dotnet user-secrets set "Machines:apikey:ApiKey" "2s.…"       # the API key machine of the machine sign-in cases
dotnet user-secrets set "Reporting:Testiny:ApiKey" "…"        # with the reporting-testiny profile
dotnet user-secrets set "KeyValues:ByokKeyUri" "https://<vault>.vault.azure.net/keys/<name>"   # with Target:Features Byok
dotnet user-secrets set "KeyValues:ByokTenantId" "<tenant GUID>"
```

| Profile | What |
|---|---|
| `dev` | `Target:BaseUrl` `http://localhost:7071/api`, the local API, and `Target:Features` `ByokApplication` (the Aspire host runs the API with the Key Vault emulator, which stands in for the platform's BYOK application) |
| `ci` | One retry, `Users:LeaseTimeoutSeconds` 300; the target and the secrets come from the job's `UITESTS_*` variables ([CI](#ci)) |
| `reporting-testiny` | Reporting to Testiny project `2SC` (`ProjectId` 2); combine it: `UITESTS_PROFILE=dev,reporting-testiny` |

### `Target`

`CliTarget` (`Settings.Bind<CliTarget>("Target")`) over the shared `PlatformTargetSettings`:

| Key | Default | Meaning |
|---|---|---|
| `BaseUrl` | `""` | The **API's** base URL with `/api` (`https://<host>/api`); sform gets it as `SFORM_general__baseUrl`. Empty is skip mode |
| `Organization`, `Project`, `View` | `cli-tests`, `automation`, `default` | The 2S organization, project and view the tests work in (`-p cli-tests.automation -v default`); checked before the first test |
| `SignIn` | `None` | The runtime signs nobody in: a pool user signs in through sform's device code. Any other value is refused |
| `AuthKitOrganizationId` | `null` | The **WorkOS** organization id (`org_…`) passed as `sform account -o`, which takes that id and not the 2S organization of `-p`; `null` signs in without `-o`, which works for a pool user in one organization. Set it to cover `-o` on a target whose users are in several organizations |
| `RoleProjects` | `{}` | `{ "<Role>": "<org>/<project>[/<view>]" }`: a project where every pool user holds the role, for the exact-role tests (`Reader`, `Contributor`); a role without an entry leases a pool user who holds it in `Target:Project` |
| `Features` | `[]` | What the target offers beyond the basics (`CliFeatures`): `Byok` (a customer key in an Azure Key Vault: [`KeyValues`](#keyvalues) is then required), `ByokApplication` (the platform's BYOK application on the API, `KeyValues:Protection:Byok` of the deployment, without a customer key: `kv policy byok-setup` and `byok-verify` of KV-POL-04 and KV-CTX-02, which a deployment without it answers with 501 `ProtectionNotConfigured`; `Byok` implies it), `Hsm` (HSM-backed keys), `Registration` and `MultiOrganization` (each with its [feature user](#feature-users) in `Users:Pool`); a `[RequiresFeature("<Feature>")]` test skips unless listed. The settings check refuses a name that is none of these |

### `Cli`

| Key | Default | Meaning |
|---|---|---|
| `Path` | `null` | `42.Platform.Cli.dll` (run as `dotnet <dll>`) or an `sform` executable (an installed tool); `null` runs the build of `src/Platform/Cli/src` of the tests' configuration, whose path MSBuild records in the test assembly (`[assembly: AssemblyMetadata("SformPath", …)]`, target `RecordSformPath` of the project file). Relative to the test assembly's folder |
| `TimeoutMs` | `120000` | How long one run of sform may take; then its process tree is killed and the test fails with the output so far. The device code sign-in waits within it |
| `KeepWorkingDirectories` | `false` | A test's working directory is removed when the test passes and kept (with `work-files.txt`, the list of its files, attached) when it does not; `true` keeps every one |

### `KeyValues`

The target's customer key of the BYOK cases (KV-POL-05, KV-POL-07), `KeyValueSettings` (`Settings.Bind<KeyValueSettings>("KeyValues")`). Both are required when `Target:Features` lists `Byok`: without them the settings check fails every test at once with what to set (`CliAutomationSetup.Validate`); a value that is set is checked even without the feature.

| Key | Default | Meaning |
|---|---|---|
| `ByokKeyUri` | `null` | The versionless URI of a key in an Azure Key Vault that the platform's BYOK application may wrap and unwrap with, `https://<vault>.vault.azure.net/keys/<name>` (what `kv policy set --byok-key` takes) |
| `ByokTenantId` | `null` | The Entra tenant id (a GUID) of the vault (`--byok-tenant`) |

Set them in a profile, `testsettings.local.json`, the user secrets or `UITESTS_KeyValues__ByokKeyUri` and `UITESTS_KeyValues__ByokTenantId`; the CI workflow passes the variables `UITESTS_KEYVALUES_BYOK_KEY_URI` and `UITESTS_KEYVALUES_BYOK_TENANT_ID` of its environment.

### `Users` and roles

The CLI has **its own pool** (`cli-test-<role>-<n>@42for.software`), provisioned in the same AuthKit environment as the admin UI's `ui-test-*` pool and with the same roles in `cli-tests/automation`, so the two suites run at the same time without sharing sessions: 2 Owners, 2 Administrators, 2 Contributors, 2 Readers (`testsettings.json`). The passwords go to the user secrets (`Users:Pool:<n>:Password`) or `UITESTS_Users__Pool__<n>__Password`; every pool user needs one (the device page asks for it). A test acts as `[UserRole(Role.X)]` (the shared `Role`): `Any`, `Editor` (Contributor and up), `Admin`, `Secrets` (ContributorWithSecrets and up), `Owner`, or exactly `Contributor` or `Reader`, which act in the project `Target:RoleProjects` maps them to. `ProjectKey`, `View` and `InProject(...)` follow the role (or the project of `UseProject`). An exact `Administrator` or `ContributorWithSecrets`, which the pool does not hold, is the "second user" of a P case: the leased Owner creates a disposable project and grants another pool user that role there (`AsGrantedUserAsync`); an organization role grants nothing in a project ([access.md](../Storyteller/access.md)), so the user then holds exactly that role. The AuthKit environment needs password sign-in, no MFA for the pool users and verified e-mails, as for the admin UI ([its checklist](../UI.Admin.AutomationTests/automation-tests.md#authkit-checklist)), and the **device code grant** enabled for sform's client (`authentication.clientId` of `src/Platform/Cli/src/app.config.json`).

#### Feature users

Two features are users rather than capabilities of the deployment (`CliFeatures.OfUsers`): `Registration`, a pool user without an account that `account register` may create (SGN-OUT-04), and `MultiOrganization`, a pool user in two WorkOS organizations (SGN-DEV-06). The target marks such a user with the feature's name among its `Roles` (`{ "Email": "…", "Roles": [ "Reader", "Registration" ] }`) and lists the feature in `Target:Features`. A `[RequiresFeature]` test of the feature leases that user, whatever its `[UserRole]` (`CliTest.FeatureUser`; a pool without such a user fails the lease and names the requirement), and **no other test ever leases it**: every lease of the suite (`RequiredUser`, `AsAnotherUserAsync`, `ArrangeAsAsync`, `AsRoleUserAsync`, `AsGrantedUserAsync`) goes through `WithoutFeatureUsers`, which adds `UserRequirement.Except(<the features>)` of the runtime library unless the requirement asks for the feature itself. The [target check](#the-target-check) signs in no feature user either: the library leases the check the first free pool user, whatever its roles, and when that one stands for a feature the check leases another pool user who stands for none (`CliTargetCheck.ReplacementOf`) and signs in and reads as that one.

Adding a feature user means **restating the whole pool in one layer**. The library takes `Users:Pool` from the highest layer that sets any `Users:Pool:<n>:Email` and does not merge the entries by index (only the passwords apply from every layer, [the library's settings](../../Libraries/Testing/UI.Automation/readme.md#settings)): a lone `Users:Pool:8:*` in the user secrets or a lone `UITESTS_Users__Pool__8__*` makes a pool of that one user, and every other test fails its lease (`No user of Users:Pool has role Owner and none of Registration, MultiOrganization`). List the role users of `testsettings.json` and then the feature users in the same layer: a profile's or `testsettings.local.json`'s `Users:Pool` array, or every `UITESTS_Users__Pool__<n>__*` entry. With the feature users last the role users keep their positions, and so their passwords (`Users:Pool:<n>:Password`); the feature user's password goes to its own position. In CI the workflow maps 8 slots (`UITESTS_USER_0_*` to `UITESTS_USER_7_*`) and the committed pool already has 8 role users, so a feature user there takes the slot of a role user ([CI](#ci)).

### `Machines`

Pre-provisioned machines of the target by name, `Machines:<name>` (`MachineCatalog`, case-insensitive), for the cases that sign in as a machine they did not create:

| Key | Variable | Meaning |
|---|---|---|
| `ApiKey` | `STORYTELLER_API_KEY` | `2s.<machine segment>.<secret>` (a secret: user secrets or `UITESTS_Machines__<name>__ApiKey`) |
| `ClientId`, `ClientSecret` | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET` | Client credentials (the secret in the user secrets) |
| `ClientCredentialsUrl`, `ClientCredentialsScope` | `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE` | When the provider needs them |
| `Certificate`, `CertificatePassword` | `STORYTELLER_CLIENT_CERTIFICATE`, `…_PASSWORD` | A `.pfx` (path relative to the test assembly) and its password (a secret) |
| `Project`, `View`, `Key` | `STORYTELLER_PROJECT`, `STORYTELLER_VIEW`, `STORYTELLER_KEY` | The target of the machine (`<org>.<project>`); an API key names its project already |
| `Endpoint` | `STORYTELLER_ENDPOINT` | `null`: `Target:BaseUrl` |
| `Id`, `Scopes` | – | Informative: the machine id (read from the API key when unset) and the scopes it holds |

`apikey` (`MachineCatalog.ApiKeyMachine`) is the API key machine of `Target:Project`, read scopes at least, that the machine sign-in cases use (`[RequiresMachine("apikey")]`: skipped with the setting to add when it is missing). The target check reads with each configured machine, so each needs `Annotation.Read`. The secrets of `Machines` are masked by their names like every secret of the settings. Tests that need a machine of a kind or with scopes create their own (`CreateMachineAsync`) and delete it at the end.

### `Execution`, `Browser`, `Reporting`

As in the library. The CLI defaults: `Execution:Trace` `Off` (the pool passwords are typed on the device page; the contexts that type them are never traced anyway), `MaxParallelTests` 4 (lowered to the pool size), `Browser` the local headless Chromium (only the device page uses it; no BrowserStack), `Reporting:RunName` `CLI automation {Date:yyyy-MM-dd} {Time:HH:mm} {Profile}`.

## Running sform

`SformRunner` (`Running/`) starts sform for a test:

- **The program**: `dotnet <42.Platform.Cli.dll>` (the host of `DOTNET_HOST_PATH`, the current process, `DOTNET_ROOT` or the `PATH`), or the executable of `Cli:Path`; the arguments as a list (`ProcessStartInfo.ArgumentList`), never through a shell.
- **The environment is built from scratch** (`SformProcessEnvironment`): only the variables a process needs from the machine (`PATH`, `SystemRoot`, `windir`, `ComSpec`, `PATHEXT`, `TEMP`, `TMP`, `USERPROFILE`, `HOME`, `LOCALAPPDATA`, `APPDATA`, `ProgramData`, `ProgramFiles*`, `DOTNET_ROOT*`, `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `LANG`, `LC_*`, `TZ`, the proxies and certificate bundles of a CI agent), then for every run `DOTNET_ENVIRONMENT=Production` (no user secrets of the CLI's build), `SFORM_TELEMETRY_OPTOUT=1`, `DO_NOT_TRACK=1`, `NO_COLOR=1`, `COLUMNS=1000`, `DOTNET_NOLOGO=1`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `SFORM_HOME` and `SFORM_TOKEN_STORE=file`; a user's run gets `SFORM_general__baseUrl=Target:BaseUrl`, a machine's run its `STORYTELLER_*` variables; last the run's own (`SformRunOptions.Environment`, `null` removes one, but not `SFORM_HOME`), and `CLI_AUTOMATION_PROCESS_TAG` (below). The outer `STORYTELLER_*`, `SFORM_*`, `BROWSER`, `DOTNET_ENVIRONMENT`, `GITHUB_ENV` and the token variables of a GitHub Actions job never reach sform.
- **Without `SFORM_HOME`** (`SformRunOptions.WithoutHome`, GEN-CLI-06 only): sform keeps its state next to its build and its session in the user's folder. On Linux and macOS the run's `HOME` and `XDG_*` folders point at an empty folder of the test, so the real profile is untouched; on Windows sform finds the user's folder through the known-folder API, which no variable redirects, so such a run reads the real profile: start it (`StartSform`) and kill it after the line the case checks. GEN-CLI-06 runs `sform account` that way with `SFORM_TOKEN_STORE=file` and kills it after its first line (the notice that the file store is ignored); that run writes `logs/` into the folder of the CLI's build (`src/Platform/Cli/src/bin/<configuration>/net10.0/logs/`) and, on Windows, reads the real profile's protected token store, and a second run (`sform assist`) ends at its prompt with 43. Its other steps check that the runs with `SFORM_HOME` change nothing in the build folder or in the user's `.42for.net` folder (`%LOCALAPPDATA%` on Windows, `$HOME` elsewhere).
- **Standard input**: `SformRunOptions.Input` is written, then the input is closed (at once without it), so a prompt fails fast (2 or 43) instead of waiting.
- **Timeout**: `Cli:TimeoutMs` (or `SformRunOptions.Timeout`) kills the whole process tree, and everything it started; the run fails with an `SformTimeoutException` holding the output so far.
- **What sform leaves running**: a process sform started that outlives it and inherited its streams (a browser opener, an editor gone to the background) would keep the run open. Every run is in a `ProcessJob`: on Windows a job object with kill-on-close, elsewhere the variable `CLI_AUTOMATION_PROCESS_TAG=<tag>`, which every descendant inherits and the runner finds in `/proc`. Three seconds after sform ended with its streams still open, the job's processes are killed; if the streams stay open even then, the run ends with the note `[the tests stopped reading: …]` on its standard error. The job also ends with the run.
- **Output**: both streams in full (`StdOut`, `StdErr`), as text (`PlainStdOut`, `PlainStdErr`, `Lines`: without escape sequences, and with [an older sform's WriteJson folds joined](#json)), and their lines in the order they arrived (`Timeline`, `Output`). On Windows a .NET program with redirected streams writes and reads them in the OEM code page of its hidden console (437 on an en-US system), and sform switches to UTF-8 only around a formatted payload (`config --format`/`--content-only`), so the runner reads valid UTF-8 as UTF-8 and every other byte in the OEM code page, and writes the input in the OEM code page: a character the code page lacks reaches sform as `?` and comes back from it as its best fit (a CLI that set its console encodings to UTF-8 when redirected would lose nothing). Generated data is ASCII. On Linux and macOS everything is UTF-8.
- **Exit codes**: `ExitCode` is sform's code. On Linux and macOS an exit status is a byte, so sform's -1 (`InputParsing`) arrives as 255 and is read back as -1 (`RawExitCode` keeps 255; sform defines no 255).
- **Attachments**: every run is attached to the test as `sform-<n>.txt`: the command line, the label, the working directory, `SFORM_HOME`, the **names** of the variables (never their values), whether it ran in a terminal, the exit code and the duration, and both streams (the standard output as `PlainStdOut` has it), through the scope's redaction. Before that, the credentials sform printed once are registered as secrets of the test (`SecretScanner`: a created machine's `AccessKey`, `Client secret:`, `Certificate password:`, any `2s.<…>.<…>` API key, the values of `::add-mask::` lines with their escaping undone), and so is what `SformRunOptions.SecretsOf` returns for a value the test learns only from that run (a Key Vault value of `config -r`, `kv get --reveal` of a pair it did not set), so they are `***` in the attachment, the report and every later assertion message. An attachment is written when the run ends and never rewritten, so a secret registered later does not reach it.

### Homes and working directories

With `SFORM_HOME` sform keeps every file there (the default project and the editor in `sform.settings.json`, `auth.discovery.json`, `logs/`, and with `SFORM_TOKEN_STORE=file` the session `authkit.cache.plaintext` and the MSAL cache) and never touches the profile or its build folder. The homes and the tests' working directories are under `<temp>/sform-automation/<runId>/` (`SformHomes`), outside `Execution:ArtifactsPath`, so a job that uploads the artifacts, or a zip of `TestResults` shared for a failure, never carries a session, a `.pfx` of `machine create -o`, a `GITHUB_ENV` file with resolved secrets or a kv export:

- `homes/<user fingerprint>/`: a pool user's home for the whole session. The first run that needs it signs the user in (the target check signs its user in there, so that user's first test does not); later tests reuse the session (the refresh token rotates inside, so the folder is never copied). Before each test `sform.settings.json` (and the legacy `access.default.json` and `editor.config.json`, which sform still reads while the settings file has no section of its own) is removed, so a test starts without a default project or an editor unless it sets them. A lease is exclusive, so two tests never share a home.
- `homes/tests/<id>/`: an empty home of one test (`[FreshSignIn]`, `FreshHome()`, every machine run, a run without `SFORM_HOME`), removed when the test ends.
- `work/<class>.<method>-<id>/`: the working directory of one test (`WorkingDirectory`), removed when the test passes; kept when it does not (or with `Cli:KeepWorkingDirectories`), and then `work-files.txt`, the list of its files (not their contents), is attached. A test attaches a file a failure needs with `AttachFileAsync`, which redacts it.

The session files (`*.plaintext`) of every home are deleted when the test process exits; the logs stay.

## Sign-in

`DeviceCodeSignIn` (`Access/`), what the user's first run and `SignInAsync` do:

1. `sform account [-o <Target:AuthKitOrganizationId>]` starts in the home with `SFORM_DEVICE_CODE_OUTPUT=json`; the first line on its standard error is the code, `{"userCode":…,"verificationUri":…,"verificationUriComplete":…,"expiresIn":…}` (`DeviceCodePrompt`). Without that line (sform ended: "You are already logged in", or a failure) the result says so.
2. A browser context of the test's scope opens `verificationUriComplete`: anonymous (`TestScope.OpenBrowserAsync`, `Target:SignIn None`), or started from the user's AuthKit session of an earlier approval in the same run (`AuthKitSessionCache`, memory only), so a fresh-home test may not need the credentials again.
3. `AuthKitDevicePage` works through whatever AuthKit shows, in any order, until sform ends: the hosted sign-in (`AuthKitHostedPage.EnterCredentialsAsync`: e-mail, Continue, password, Sign in, typed), a field for the code, a button that approves (`Confirm`, `Continue`, `Approve`, `Allow`, `Authorize`, `Yes`, `Accept`) or, for a decline, one that declines (`Deny`, `Decline`, `Cancel`, `Reject`, `No`).
4. sform ends: **signed in** is exit `0`, or `43` when no default project is set ("No default project is set, please call sform account set command."). The home is marked signed in, and the context's AuthKit session is kept for the user's next approval.

A device page that does not lead there in time fails with `… the device page showed: <url>: <text of the page>`, `device-page-<n>.png` (inputs masked) and `device-page-<n>.txt`. A case that expects sform to fail after the approval (SGN-DEV-06 and SGN-DEV-07: exit 2) sets `DeviceCodeOptions.ExpectSignedIn` to `false` and checks `DeviceCodeSignInResult.Result`. The password is typed only in contexts that are never traced. A run that ends with 44 and the CLI's general refusal ("You are not authorized to execute the command or you don't have access to requested resources.") runs `sform account` once in the same home: a session sform can no longer use starts a device code, which is approved, and the run goes once more; otherwise the refusal is the result (`SformRunOptions.RenewExpiredSession`).

**Not verified yet** (no AuthKit target in phase 3): the labels of AuthKit's device pages, whether the device page takes a cached AuthKit session, and the user name sform prints for an AuthKit user (the tests expect the e-mail). The first run against a target records them; see the [review of phase 3](specs/reviews/2026-11-08%20Phase%203%20of%20CLI%20Automation%20Testing%20Project.md#not-verified-without-a-target).

## Machines

- `AsMachine("<name>")` (options of a run as `Machines:<name>`), `AsMachine(credentials)`, `SformAsMachine("<name>", args…)`, `MachineOf("<name>")`: a machine's run gets its `STORYTELLER_*` variables and an empty home of its own, and its secrets are masked.
- `CreateMachineAsync(options…)`: `sform account machine create` in the acting project as the leased user, with the options of the case (`-r`, `-w`, `--scope`, `-m`, `-k`, `-a`, `-o`, `-l`, `-s`, `--issuer`, …). `CreateMachineAsync(MachineCreation, options…)` creates it as another pool user (`As`: the creator of a `[MachineOnly]` case, which leases nobody, e.g. `AsAnotherUserAsync(AdminUser)` for `-m`), in another project (`ProjectKey`, `View`), or without the cleanup (`DeleteAtCleanup = false`). The machine's id is read first and its deletion registered before anything else is checked; the cleanup runs as the creator and counts a machine that is gone (the case deleted it) as deleted.
- `CreatedMachine`: `Id`, `AccessKey` (the API key, or the client secret), `CredentialKind`, `Scopes`, `ProjectKey`, `View`, `CertificatePath` (the full path of "Certificate written to …"), `CertificatePassword`, `ClientId`, `ClientSecret`, `TokenUrl`, `TokenScope`, `PrintedVariables` (the "To sign in sform in a pipeline, set:" lines), `Credentials` (the variables to sign in with for ApiKey, Certificate, CertificateAndApiKey and ClientCredentials; a shared-certificate machine gets its key only, a Federated one none), `ApiKeyCredentials`, `SignInCredentials`, `Result`. Every printed secret is a secret of the test.
- `RunCreatingMachineAsync(projectKey, args…)` (and `(projectKey, actor, options, args…)` as another pool user): a command that creates a machine the way the case writes it (the alias `set`, the context options, a refusal that must create nothing), run as it is and returned unchecked; whatever machine it printed is deleted at the end by the leased user (`DeleteMachineAtCleanup(id, projectKey, view?, creator?)`, which `CreateMachineAsync` uses too). `MachineIdOf(result)` and `TryMachineIdOf(result)` read the `"Id"` it printed.
- `MachineCredentials.ForApiKey`, `ForClientCredentials`, `ForCertificate`, `ForFederated(clientId, tokenFile, endpoint, projectKey)`, `FromVariables(name, variables, secrets)` and `With(changes)` build the variables of other machines (a wrong key, another `STORYTELLER_PROJECT`, a token file).

## The target check

Before the first test, `CliAutomationSetup.CheckTargetAsync` (`CliTargetCheck`, no page: `TargetCheckUsesBrowser` is `false`), as a pool user who stands for no [feature](#feature-users) (the library leases the check the first free pool user; when that one is a feature user, the check leases another one):

1. `sform --version` starts (the build of `src/Platform/Cli/src`, or `Cli:Path`);
2. the user signs in through the device code into the user's home of the session (the AuthKit device page, the password, `-o`);
3. `sform story -p <Organization>.<Project> -v <View>` reads the target's project, and the same for each `Target:RoleProjects` entry: the organization and the project exist and the user may read them. A view exists by its data (a view without data lists "No annotation exists yet." with exit 0, GEN-CTX-03), so a wrong `Target:View` is not caught;
4. each configured machine reads with `sform story -p <its project> -v <its view>`, a call to the API (`sform account` of an API key or certificate machine calls nothing), so a wrong or revoked credential, a wrong project or an unreachable endpoint fails here.

A failure fails every CLI test at once with a message that says what to fix (`The target check failed: Users:Pool:0 (u-…) cannot read the project cli-tests.automation (…): sform story … ended with 44 …`); its runs are in `TargetCheck/CheckTarget/`. The settings are checked before that (`CliAutomationSetup.Validate`): the `Target` names and `RoleProjects`, `Target:BaseUrl` an http(s) URL, `Target:SignIn` `None`, the shape of `Target:AuthKitOrganizationId`, the names of `Target:Features`, `KeyValues` (required with `Byok`), `Cli:TimeoutMs`, that sform can be found, each machine's credential kind and project, a non-empty pool and a password for every pool user.

## Write a test

Derive from `CliTest` (`src/Platform/Cli.AutomationTests/src/CliTest*.cs`), or from the section's base class (`MachineAccessTest` in Machines and Shared Certificates, [below](#section-helpers)), put it in `Tests/<Section>/<Class>.cs` (one folder per section of the catalogue, one class per sub-folder), start the comment above it with the case's reference, and mark it with its Testiny case, its role, its modes and what it covers; [the catalogue check](#the-catalogue-check) compares all of it with the case's row:

```csharp
[Area(Area.Annotations)]
public sealed class AnnotationEditTests : CliTest
{
    // ANN-SET-01: story set creates an annotation, and story get returns it.
    [Test]
    [TestCase(275)]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("story set", "arg0")]
    [Covers("story get", "arg0")]
    public async Task Story_SetGet_RoundTrip()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();

        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(annotation["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
    }
}
```

### `CliTest`

| Member | Meaning |
|---|---|
| `Sform(params string[] args)`, `Sform(SformRunOptions, params string[] args)` | Runs sform to its end as the leased user (signed in first when its home is not yet) in `Home` and `WorkingDirectory`; returns an `SformResult` |
| `StartSform(…)`, `StartSformInTerminal(…)` | Starts sform and returns a running `SformProcess` (killed at the end of the test at the latest) |
| `InProject(params string[] args)` | The arguments with `-p <ProjectKey> -v <View>` of the acting project (a test starts without a default project) |
| `Target` (`CliTarget`), `Cli` (`CliSettings`), `Machines` (`MachineCatalog`) | The settings; `Target.ProjectKey` is always the target project |
| `ActingRole`, `RoleProject`, `Organization`, `Project`, `View`, `ProjectKey`, `ActsInOwnProject` | Whom the test acts as and where (`[UserRole]`, `Target:RoleProjects`, `UseProject`) |
| `UseProject(projectKey, view = "default")` | From now on the test acts in that project: `ProjectKey`, `View`, `InProject` and `CreateMachineAsync` name it |
| `User`, `Home`, `FreshHome()` | The leased pool user, the home of the test's user runs, a new empty home |
| `SignInAsync(home?, DeviceCodeOptions?)`, `SignInAsync(user, home, options?)`, `DeviceCode` | The device code sign-in (a `[FreshSignIn]` test signs in itself); `DeviceCodeOptions`: `Command` (`account`, `login`, `access`), `Organization` (`null`: `Target:AuthKitOrganizationId`; `""`: no `-o`), `OrganizationOption` (`-o`/`--organization`), `Arguments` (`-b`), `Environment`, `Decline`, `ExpectSignedIn` (false: any exit after the approval is returned, not thrown), `UseCachedBrowserSession`, `JsonPrompt` (false: the text prompt), `ApprovalTimeout`; returns `DeviceCodeSignInResult` (`Result`, `Prompt`, `Steps`, `IsSignedIn`, `WasSignedInAlready`) |
| `AsAnotherUserAsync(requirement)` | Another pool user with a home of its own (`SformActor`: `Sform(…)`, `AccountIdAsync()`), returned at the end; never a [feature user](#feature-users) unless the requirement is `FeatureUser(…)` |
| `ArrangeAsAsync(requirement)`, `ArrangeAsEditorAsync()` | The user who arranges what the test's role reads or is refused, in the acting project: the leased user when it satisfies the requirement (`EditingUser`, `SecretsUser`, `AdminUser`), else another pool user; e.g. the Editor who creates what a Reader's case reads, or the ContributorWithSecrets who sets the secret a Contributor is refused. In `Target:Project` only (a role project has no such user: it throws) |
| `AsRoleUserAsync(role)`, `RoleProjectOf(role)` | "`<contributor project>`" or "`<reader project>`" of a step of another user: `(actor, projectKey, view)`, any other pool user in the project `Target:RoleProjects` maps the role to, else a pool user who holds the role in `Target:Project`; `RoleProjectOf` is that mapping as `(ProjectKey, View)` for `-p` and `-v`, or `null` |
| `FeatureUser(feature)`, `WithoutFeatureUsers(requirement)`, `UserFeature` | The [feature users](#feature-users): the requirement of the user a feature stands for, a requirement that excludes them, the user feature of the running test's `[RequiresFeature]` |
| `EventuallyAsync(passes, within, args…)`, `EventuallyAsync(read, passes, within, interval?)` | A bounded re-read: runs sform (or any read, e.g. another actor's run or the rows of several pages) until `passes` says so, at most for `within`, every 5 s by default, and returns the last result, which the test checks as usual; a read that passes at once returns at once. For what the API serves from a cache of its instance ([Troubleshooting](#troubleshooting)) |
| `AccountIdAsync()` | The leased user's account id ("Account ID: #<id>") |
| `AsMachine`, `SformAsMachine`, `MachineOf`, `CreateMachineAsync(options…)`, `CreateMachineAsync(MachineCreation, options…)`, `RunCreatingMachineAsync`, `DeleteMachineAtCleanup`, `MachineIdOf` | Machines (above) |
| `CreateDisposableProjectAsync(actInIt = true)` | `<Target:Organization>.<generated>` through `sform account points create` in a pseudo terminal, as the leased Owner (`[UserRole(Role.Owner)]`, `[DisposableProject]`: every P case of the catalogue); with `actInIt` the test acts in it (`UseProject`); the project stays. The default project `points create` sets is removed again (`SformHome.ResetDefaultProject`), the home's editor stays, so a test of modes E and P may call `UseEditorAsync` before or after |
| `GrantAsync(accountId, role, projectKey?)`, `AsGrantedUserAsync(role, requirement?, projectKey?)` | `account points grant <id> -p <project> -r <role>` as the leased user, only in a project of the test's own; another pool user granted the role there (the "second user" of a P case) |
| `CliData` | Seeded names: `ResponsibilityKey()` (`rst.cli-<runId>-<hex>-<words>`, `<rst>` of the catalogue), `UnitKeyOf(rst)`, `AnnotationKey(code, parts…)`, `KeyValueKey()` (`cli_<runId>.<word>_<hex>`, also a binding path), `Value()`, `LabelName()`, `ProjectName()`, `ViewName()`, `MachineName()`, `SecretValue()` (random), `EnvironmentPrefix()` |
| `UseEditorAsync(home?)`, `UseEditorAsync(customCommand, home?)` | Mode E: the [fake editor](#the-fake-editor-mode-e) (a `FakeEditor`) or another program becomes the editor of the home: the `editor` section of its `sform.settings.json` |
| `WorkingDirectory`, `WriteFileAsync(path, text)`, `ReadFileAsync(path)`, `AttachFileAsync(path)`, `FileSystem` | The test's working directory (outside the artifacts, [above](#homes-and-working-directories)) and its files; `AttachFileAsync` attaches a text file redacted |
| `AddSecret(value)` | A secret the test came by (a value it set): masked in the attachments, the report and the assertion messages |
| `OnCleanUp(Func<Task>)`, `CleanUpAsync()` | Cleanups at the end, in reverse order; an override calls the base |
| `Scope`, `Session`, `Settings`, `Data`, `LeaseAnotherUserAsync`, `GetSkipReason` | The library's `AutomationTest` (its `LeaseAnotherUserAsync` does not exclude the feature users: use `AsAnotherUserAsync`) |

### `SformRunOptions`

`Environment` (variables to add, `null` removes), `Input` (written, then the input closes), `Timeout`, `WorkingDirectory`, `Home`, `Machine` (`MachineCredentials`), `Terminal`, `TerminalColumns`/`TerminalRows` (250 × 50), `BaseUrl`, `Secrets` (values to mask), `SecretsOf` (a function of the result that returns the secrets the test learns from this run, registered before the run is attached), `WithoutHome` (no `SFORM_HOME`, above), `RenewExpiredSession` (true), `Label` (a note in the attachment).

### `SformResult`

`ExitCode`, `RawExitCode`, `Exit` (`CliExit?`), `Succeeded`, `StdOut`, `StdErr`, `PlainStdOut`, `PlainStdErr`, `Lines`, `ErrorLines`, `Timeline`, `Output`, `Duration`, `IsTerminal`, `WorkingDirectory`, `Home`, `Text(stream)`; `JsonNode()`, `Json<T>()`, `TryJsonNode()`; the assertions, which throw an `SformAssertionException` with the command line, the exit code, what they looked for and the output, all redacted, and return the result for chaining: `ShouldSucceed()`, `ShouldExitWith(CliExit)`, `ShouldExitWith(int)`, `ShouldExitWithAnyOf(…)`, `ShouldFail()` (non-zero, not a crash), `ShouldContain(text, stream, comparison)`, `ShouldNotContain`, `ShouldMatch(regex, stream)`, `ShouldNotMatch(regex, stream)`, `ShouldHaveLine(text, stream)`, and `Fail(what)` for a check of the test's own. `VersionRows(headerStart)` reads the table of `config versions`, `template versions` and `schema … versions` (`WriteVersions`: Version, Author, Created, Expires, the newest first): the version of each row after the header line that contains `headerStart` (`"Versions of the configuration for"`, `"Versions of the template for"`, `"Versions of the schema for"`), empty for "… has no versions.", a failure without the header. `await Sform(…).ShouldSucceed()`, `ShouldExitWith` and `ShouldFail` work on the task too; anything else on the awaited result: `(await Sform(…).ShouldSucceed()).JsonNode()`. `CliExit` mirrors `src/Platform/Cli/src/ExitCodes.cs`: `InputParsing` -1, `Success` 0, `Crash` 1, `WrongInput` 2, `ServiceFailure` 3, `Aborted` 40, `NoWorkNeeded` 42, `InteractionNeeded` 43, `Unauthorized` 44, `PartialResult` 45.

#### JSON

sform prints a whole document only with `--content-only`, `--value-only`, `--format json`/`--json` (history) and into files (`-e`, `-o`, the kv export). The read commands (`story get`, `story set`, `story config`, `config set`, `kv get`, `machine create`, …) print their model through `WriteJson`, with a line-number gutter (` 1 │ {`) and colours even when redirected; a `--format` other than json prints its text through `WriteNumberedText`, with the gutter too (and a `#` written as `\u0023`), unless `--content-only` or a file. `JsonNode()` takes the whole plain standard output when it is a document, else the first block of numbered lines (the gutter removed), else the first balanced `{…}` or `[…]`. sform writes PascalCase, so `Json<T>()` matches names without case.

**WriteJson renders into a capture console 1,000,000 columns wide** (`ExtendedConsoleExtensions.WriteJson`, since `80732b66`), so a long token (an API key, a unit key of about 85 characters, a long value) stays on its line and every JSON line of the model is one numbered line. Before that the capture console had Spectre's default width of 80, and a token that did not fit was folded into lines of 80 inside its string; `PlainStdOut` still joins such folds (`WrappedJson.Join`: a numbered block that is no JSON as printed but is JSON with its folds joined is replaced by the joined lines), a safety net for an older sform (`Cli:Path`) that changes nothing in the output of the current one. `AnsiText.Plain(result.StdOut)` is the text exactly as printed.

### `SformProcess` and the terminal

`WaitForLineAsync(stream, predicate, timeout?)`, `WaitForLineOrExitAsync(…)` (null when sform ended without the line), `ExpectAsync(text | regex, timeout?)` (on the screen after the previous expectation; white space in a text matches any white space or none, as a terminal's renderer moves the cursor instead of writing spaces), `SendAsync(text)`, `SendLineAsync(text)`, `SendKeyAsync(TerminalKey, times)` (`Enter`, `Tab`, `Space`, `Escape`, `Backspace`, `Delete`, `Up`, `Down`, `Left`, `Right`, `Home`, `End`, `PageUp`, `PageDown`, `CtrlC`, `CtrlD`), `WriteAsync`, `CloseInput()`, `WaitForExitAsync(timeout?)`, `KillAsync()`, `Screen`, `StdOut`, `StdErr`, `LinesOf(stream)`, `HasExited`. Each wait has 30 s by default and fails with what sform wrote so far.

**Terminal mode** (mode T: `[Terminal]`, `StartSformInTerminal`): sform runs in a pseudo terminal, so Sharprompt's prompts (selections, confirmations, inputs, hidden input) run as for a person. Windows: a ConPTY pseudo console (Windows 10 1809+, through P/Invoke, no package); Linux: util-linux `script -qefc '<command>' /dev/null` (each argument single-quoted, the size set with `stty`); macOS is not supported (the test skips with the reason). In a terminal stdout and stderr are one screen (`StdOut`; `StdErr` is empty), and the screen is a stream of what was drawn, not an emulated screen: a redrawn prompt shows each time.

```csharp
await using var terminal = await StartSformInTerminal("assist", "key");
await terminal.ExpectAsync("What type of annotation you need");
await terminal.SendKeyAsync(TerminalKey.Down);
await terminal.SendKeyAsync(TerminalKey.Enter);
await terminal.ExpectAsync("Name of the responsibility");
await terminal.SendLineAsync("my-app");
await terminal.ExpectAsync("Name of the job");
await terminal.SendLineAsync("nightly");
(await terminal.WaitForExitAsync()).ShouldSucceed().ShouldContain("unt.my-app.nightly");
```

### Attributes

| Attribute | Effect |
|---|---|
| `[Test]`, `[Elementary]`, `[TestCase(<id>)]` | TUnit's test; the PR gate set (priority Elementary); the Testiny case of the row, its id from the catalogue's Testiny column (`[TestCase(275)]`) |
| `[UserRole(Role.X)]` | The role the test acts as (shared library) |
| `[Area(Area.X)]` | Category `Area-<section>`: `SignIn`, `Account`, `Machines`, `SharedCertificates`, `Annotations`, `Configurations`, `Templates`, `Schemas`, `KeyValues`, `General` |
| `[MachineOnly]` | Mode M: leases no pool user |
| `[FreshSignIn]` | Mode F: the test's user runs use an empty home of its own; the test signs in itself |
| `[Terminal]` | Mode T: skipped where no pseudo terminal is supported |
| `[DisposableProject]` | Mode P: creates its own project (`CreateDisposableProjectAsync`, needs a pseudo terminal). The project stays: neither sform nor the API can delete a project yet, so each run leaves its P projects in `Target:Organization`, named `cli-<runId>-prj-…` (the run id starts with its date); remove those of past runs by hand from time to time, or skip mode P with `--treenode-filter "/**[Category!=DisposableProject]"` |
| `[CertificateKinds]` | Needs the credential kind `Certificate` or `CertificateAndApiKey` (a machine of that kind, its renewal, a shared certificate, or a policy that enables them): 11 cases of Machines, Shared Certificates and Key/Values. A deployment that turns these kinds off (`MachineAuth:DisabledCredentialKinds`, e.g. one without mTLS) refuses them, so leave them out there with `--treenode-filter "/**[Category!=CertificateKinds]"` |
| `[RequiresFeature("<Feature>")]` | Mode X: skipped unless `Target:Features` lists it |
| `[RequiresMachine("<name>")]` | Skipped unless `Machines:<name>` is configured with a credential |
| `[Covers("<command path>", "<item>", …)]` | What the test exercises: the command path as typed after sform, and the options (each form), fixed values (`--protection=public`) and arguments (`arg0`) of that command whose effect it checks; repeat it per command, or put it on the class for all its tests. The rules of [the command coverage](#command-coverage) |

Categories (`CliCategories`): `MachineOnly`, `FreshSignIn`, `Terminal`, `DisposableProject`, `CertificateKinds`, `Requires-<Feature>`, `Machine-<name>`, `SelfTest`, `Coverage`, plus the library's `Elementary`, `Serial`, `Area-<section>`.

### Data and secrets

Generated data follows the catalogue's rules: keys and names under the target project, `cli-<runId>` in every name, nothing deleted that a case does not delete. A secret value a test sets (`CliData.SecretValue()`) is registered with `AddSecret` (or `SformRunOptions.Secrets`) **before** the run that carries it, so the attachment and every message mask it; the raw `StdOut`/`StdErr` stay unmasked for the checks (`ShouldNotContain(secret)`, whose own failure message masks it too). A secret the test learns from a run's output is returned by that run's `SformRunOptions.SecretsOf`.

### Section helpers

What one section's cases share stays in its folder; what several sections need is in `CliTest` and `SformResult` (`EventuallyAsync`, `ArrangeAsAsync`, `AsRoleUserAsync`, `RunCreatingMachineAsync`, `ShouldNotMatch`, `VersionRows`, …), so move a helper there once a second section needs it.

- **`Tests/Machines/MachineAccessTest`**, the abstract base class of the Machines and Shared Certificates classes (abstract, so [the catalogue check](#the-catalogue-check) takes no test from it): `IssueSharedCertificateAsync` and `RunIssuingSharedCertificateAsync` (every certificate the test issued is revoked at the end, unless `MarkRevoked`), `WithSharedCertificate`, `EnableCredentialKindsAsync` (the machine authentication policy of a disposable project), `TokenEndpointOf` (the audience of a federation rule), `InView`, `CreateAnnotationAsync`, `CreateSignerAsync`, `SetIssuerAsync` and `FederatedAsync` (`PlatformTokenSigner`: an RSA key, its JWKS and RS256 platform tokens of the test's issuer), `OpenPfx`, `FileExists`, `ShouldExpireInDaysAsync`, `ScopesOf` and `ShouldHaveScopesAsync`, `CertificatePasswordOf`, the readers of sform's tables and policies (`RowOf`, `TryRowOf`, `CellOf`, `LineWith`, `ValueOf`, `EnabledKindsOf`, `ShouldPrintPolicyAsync`), and the windows of `EventuallyAsync` for the API's caches: `IssuerCacheWindow` (75 s) and `SharedCertificateCacheWindow` (315 s).
- Static helpers of a section's output: `Tests/SignIn/SignInOutput` (the account header, the organizations of a user of several, made-up and tampered API keys), `Tests/Account/AccessPointOutput`, `Tests/General/GeneralChecks` (snapshots of a folder's files), `Tests/Annotations/AnnotationOutput` and `ViewArchive` (export zips), `Tests/Configurations/ConfigOutput` (also for Templates: numbered lines, diffs, saved versions, the authors of a versions table, `GITHUB_ENV` files), `Tests/Schemas/SchemaChecks`, `Tests/KeyValues/KeyValuesSupportA` (tables, pages, recipients, `PolicyCacheWait` of 90 s for the key/value policy) and `KeyValuesSupportB` (export and import documents, audit rows).

### Examples

```csharp
// A user command in the acting project (-p and -v of the role's project), and a role refusal.
[Test]
[UserRole(Role.Editor)]
[Covers("story set", "arg0")]
public async Task Story_Set_Creates()
{
    var key = CliData.ResponsibilityKey();
    var set = (await Sform(InProject("story", "set", key))).ShouldSucceed();
    await Assert.That(set.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
}

[Test]
[UserRole(Role.Reader)]
public async Task Story_SetAsReader_Unauthorized()
{
    (await Sform(InProject("story", "set", CliData.ResponsibilityKey())))
        .ShouldExitWith(CliExit.Unauthorized)
        .ShouldContain(GeneralRefusal);
}

// A machine run: a configured machine, and one the test creates (deleted at the end).
[Test]
[MachineOnly]
[RequiresMachine(MachineCatalog.ApiKeyMachine)]
public async Task Machine_Reads()
{
    (await SformAsMachine(MachineCatalog.ApiKeyMachine, "story")).ShouldSucceed();
    var creator = await ArrangeAsEditorAsync();                   // a [MachineOnly] test leases nobody: another user creates
    var key = CliData.ResponsibilityKey();
    (await creator.Sform(InProject("story", "set", key))).ShouldSucceed();
    var writer = await CreateMachineAsync(new MachineCreation { As = creator }, "-w");
    (await Sform(AsMachine(writer.SignInCredentials), "story", "config", "set", key, "-x", "a=1")).ShouldSucceed();
}

// A fresh sign-in, and one that fails after the approval.
[Test]
[FreshSignIn]
public async Task DeviceCode_SignsIn()
{
    var signIn = await SignInAsync();
    signIn.Result.ShouldExitWith(CliExit.InteractionNeeded).ShouldContain($"You have been logged in as {User.Email}");
    var wrong = await SignInAsync(FreshHome(), new DeviceCodeOptions { Organization = "org_01NOTAMEMBER0000000000", ExpectSignedIn = false });
    wrong.Result.ShouldExitWith(CliExit.WrongInput);
}

// A prompt in a pseudo terminal.
[Test]
[Terminal]
public async Task Assist_Key_AsksForTheParts()
{
    await using var terminal = await StartSformInTerminal("assist", "key");
    await terminal.ExpectAsync("What type of annotation you need");
    await terminal.SendKeyAsync(TerminalKey.Down);
    await terminal.SendKeyAsync(TerminalKey.Enter);
    await terminal.ExpectAsync("Name of the responsibility");
    await terminal.SendLineAsync("my-app");
    await terminal.ExpectAsync("Name of the job");
    await terminal.SendLineAsync("nightly");
    (await terminal.WaitForExitAsync()).ShouldSucceed().ShouldContain("unt.my-app.nightly");
}

// A file the command writes, read back from the working directory.
[Test]
[UserRole(Role.Editor)]
public async Task Story_Get_Exports()
{
    var key = CliData.ResponsibilityKey();
    (await Sform(InProject("story", "set", key))).ShouldSucceed();
    (await Sform(InProject("story", "get", key, "-e", "out.json"))).ShouldSucceed();
    var exported = System.Text.Json.Nodes.JsonNode.Parse(await ReadFileAsync("out.json"))!;
    await Assert.That(exported["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
}

// A disposable project (mode P) with a second user granted a role there.
[Test]
[UserRole(Role.Owner)]
[DisposableProject]
public async Task Machine_ManageAsContributorWithSecrets_Refused()
{
    await CreateDisposableProjectAsync();                       // the test acts in it from now on
    var second = await AsGrantedUserAsync(PlatformRoles.ContributorWithSecrets);
    (await second.Sform(InProject("account", "machine", "create", "-m", "SchemaWrite")))
        .ShouldExitWith(CliExit.Unauthorized)
        .ShouldContain($"No Administrator access to '{ProjectKey}'.");
}
```

```csharp
// An edit through the fake editor (mode E): the change applied, the file sform gave the editor.
[Test]
[UserRole(Role.Editor)]
public async Task Config_Edit_StoresChange()
{
    var key = CliData.ResponsibilityKey();
    (await Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed();
    var editor = await UseEditorAsync();
    var options = editor.With(EditorChange.Merge("""{ "a": 2 }"""));    // one With per sform run
    (await Sform(options, InProject("story", "config", "edit", key, "-y")))
        .ShouldSucceed();                                          // -y saves without "Do you want to save these changes"
    await Assert.That(editor.ReadLog(options).Single().OriginalJson()!["a"]!.GetValue<int>()).IsEqualTo(1);
    var saved = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
    await Assert.That(saved["a"]!.GetValue<int>()).IsEqualTo(2);
}
```

### The fake editor (mode E)

The `edit` commands (`config edit`, `template edit`, `schema type|annotation|descendant edit`) open the editor of the `editor` section of `sform.settings.json` in `SFORM_HOME` (the file of `sform account settings`). The tests make it the **fake editor**, `src/Platform/Cli.AutomationTests/editor` (`42.Platform.Cli.AutomationTests.Editor`, a console app built with the tests through a project reference with `ReferenceOutputAssembly="false"`; MSBuild records the path of its dll as `[assembly: AssemblyMetadata("EditorPath", …)]` next to `SformPath`).

How sform runs a custom editor (`EditorService`): the `CustomCommand` is split at its **first space** into the program and its arguments (`ParseCustomCommand`), the quoted path of a temporary file (`<temp>/<prefix>-<random>.json`, e.g. `config-<key>-1a2b3c4d.json`, holding the document indented) is appended, the program starts without a shell or a window with sform's standard streams, and sform **waits** for it: exit 0 makes sform read the file back, any other code ends with "Editor exited with code N." and 1. A document equal to the one it wrote is "No changes detected." and 42; one that is not JSON is "Invalid JSON: …" and "Nothing was saved." with 2 on redirected input (in a terminal sform offers to open the editor again); a changed one is shown as a diff and saved with `-y` (else after "Do you want to save these changes", which redirected input refuses with 43). The temporary file is deleted after the run.

So the custom command is the fake editor's **apphost** (`42.Platform.Cli.AutomationTests.Editor.exe` on Windows, without the extension elsewhere), never `dotnet <dll>`; a path with a space cannot be a custom command, so when the build's path has one (a checkout under `D:\My Projects`, where a data volume usually has no 8.3 names, or anywhere on Linux) `FakeEditor` copies the editor's build output once per build to `<temp>/sform-automation/fake-editor/<hash>/` and uses that apphost (on Windows through the short 8.3 path of a temporary folder with a space); only when the temporary folder has a space too does it throw with what to change. sform's environment is built from scratch but passes the machine's `DOTNET_ROOT` (and `DOTNET_ROOT_<ARCH>`) on; when the machine has none, `FakeEditor.With` sets `DOTNET_ROOT` to the installation of the dotnet the tests use, with links followed (`/usr/bin/dotnet` is often a link to `/usr/lib/dotnet/dotnet`) and only when it has `host/fxr`; otherwise it leaves it out and the apphost finds the global installation itself (a wrong `DOTNET_ROOT` would stop it with 131). What the editor does comes from variables of the run, which it inherits from sform:

| Variable | Meaning |
|---|---|
| `CLI_EDITOR_MODE` | The step of each invocation, comma-separated; the n-th time sform opens the editor in a run it does the n-th step, and the last step repeats: `replace` (the file becomes the step's file), `merge` (the step's file is an RFC 7396 JSON merge patch applied to the document: members merge, `null` removes one, anything else replaces the document), `none` (the file stays as it is), `invalid` (the file becomes text that is not JSON), `exit[:<code>]` (the file stays, the editor exits with the code, 1 by default) |
| `CLI_EDITOR_FILE_<n>`, `CLI_EDITOR_FILE` | The file of the n-th step (`replace`, `merge`), else of every step |
| `CLI_EDITOR_LOG` | A file that gets one JSON line per invocation: `invocation`, `run`, `time`, `file` (the temporary file sform gave), `arguments`, `step`, `changeFile`, `exitCode`, `original` (the document sform wrote), `written` (what the editor wrote, `null` when it left the file), `error`. Its lines count the invocations |
| `CLI_EDITOR_RUN` | A name of the run, written into each line; invocations count per run, so one log serves several runs of a test |

The fake editor writes nothing to its standard output, which it shares with sform; a problem goes to the standard error and the log, with its own exit codes (sform then says "Editor exited with code N."): 64 no file argument, 65 `CLI_EDITOR_MODE` missing or unknown (a test that forgot the change), 66 the file to edit or the step's file missing, 67 `merge` on a document that is not JSON.

The tests use it through `Editing/`:

- `await UseEditorAsync(home?)` writes the `editor` section of the home's `sform.settings.json` with `editorType` `Custom` and the apphost as `customCommand`, as `sform account settings editor custom --command` would (`Home` by default; `ResetDefaults` removes it before the next test of a pool user's home) and returns a `FakeEditor` with a folder of its own in the test's working directory (`fake-editor/<id>/`, outside the artifacts) for its log (`editor.log`) and change files, so two editors of one test (one per home) never mix. `UseEditorAsync(customCommand, home?)` sets another program.
- `editor.With(change, options?)` returns the options of **one run** of sform: the variables of the change (its files, `<run>-step-<n>.json`; `CLI_EDITOR_RUN` `run-<n>`, a new run per call, so the steps count from the first again; `DOTNET_ROOT` when needed), over the given options' variables. Call it once per sform run: reusing its result for a second run continues the same run (a `Sequence` goes on where it stopped, and the log puts both runs under one name).
- `EditorChange`: `Replace(content)`, `Merge(mergePatch)` (text or a `JsonNode`), `None`, `Invalid`, `Exit(code)`, `Sequence(changes…)` (one per invocation, e.g. `Sequence(Invalid, Merge(…))` for a terminal case that opens the editor again after invalid JSON).
- `editor.ReadLog(options)` returns the `EditorLog` of the run of those options (`FakeEditor.RunOf(options)` is its name), `editor.ReadLog()` that of every run: `Invocations`, `Count`, `Run`, `Single()` (throws with the log unless sform opened the editor once in it), `Last()`, `OfRun("run-1")` or `OfRun(options)`; an `EditorInvocation` has `File`, `FileName`, `Arguments`, `Step`, `ExitCode`, `Original`/`OriginalJson()`, `Written`/`WrittenJson()`, `Error`, `Run`, `Invocation`.

The catalogue's E cases run `edit` two to six times each with one editor, so each run gets its own `With` and its log is read with `ReadLog(options)`. A run that must save needs a change against the document stored at that moment: merging the same patch again is "No changes detected." and 42.

| Case | The `edit` runs of its steps, and the change of each |
|---|---|
| CFG-HIS-05 | `config edit <rst> -y` with `Merge({"a":5})`, `config edit <rst> --yes` with `Merge({"a":6})`: two new versions |
| CFG-HIS-06 | `config edit <rst> -y` with `None`: 42, no version; then a schema-breaking `Merge`: `-y` refused with 2, `-y -f` stored, and `--yes --force` with another breaking `Merge` stored |
| TPL-TPL-06 | `template edit unt -y` with `Merge` adding `x`, `template edit unt --yes` with `Merge` adding `y` |
| SCH-TYP-04, SCH-ANN-04, SCH-DSC-04 | `schema type edit`, `schema annotation edit`, `schema descendant edit` (`-y`) with a `Merge` adding a property; a breaking `Merge` with `-y -f`, another with `--yes --force`: a version each |
| CFG-CTX-01, TPL-TPL-09 | `config edit <rst>`/`<rst2>` (`template edit unt`/`sbt`) `-y` with `--projectKey <B>` and `--view <view>`, then `-v <view>`, each with a `Merge` adding a property: `ReadLog(options).Single().OriginalJson()` is the document of `<view>` in `<B>` |
| SCH-CTX-01 | the six `schema … edit -y` of its steps (two each of type, annotation and descendant), each with a `Merge` adding a property, half with `--view`, half with `-v` |
| ANN-EDT-01 to ANN-EDT-08, ANN-CTX-02 | `story edit <rst>` in a pseudo terminal (`StartSformInTerminal(editor.With(…), …)`), answering "Do you want to save these changes" (and the questions of a deleted key and of an invalid document): a `Merge` of `Title`, `Labels` and `Values` (`null` removes a value), `None` (42), `Sequence(Invalid, Replace(…), Merge(…))` (the editor opened three times); a run that keeps a failed save's edit sets `TEMP`, `TMP` and `TMPDIR` to a folder of the working directory, where sform writes the file |

`story edit` has no `-y`: it always asks before it saves, and with a redirected input or output it ends with 43 before any request (ANN-EDT-04). So its cases are modes T and E together: sform runs in the pseudo terminal and starts the fake editor there as its child (a console program on the same pseudo terminal, which writes nothing to it), waits for it, shows the changes and asks. `EditorThroughSformSelfTests` runs both without a target.

```csharp
// CFG-HIS-05: the fake editor sets a to 5, then to 6.
var editor = await UseEditorAsync();
var five = editor.With(EditorChange.Merge("""{ "a": 5 }"""));
(await Sform(five, InProject("story", "config", "edit", rst, "-y"))).ShouldSucceed().ShouldContain($"Configuration for '{rst}' has been saved (version");
var six = editor.With(EditorChange.Merge("""{ "a": 6 }"""));
(await Sform(six, InProject("story", "config", "edit", rst, "--yes"))).ShouldSucceed();
await Assert.That(editor.ReadLog(six).Single().OriginalJson()!["a"]!.GetValue<int>()).IsEqualTo(5);   // the editor got the stored document

// CFG-HIS-06: no change.
var unchanged = editor.With(EditorChange.None);
(await Sform(unchanged, InProject("story", "config", "edit", rst, "-y"))).ShouldExitWith(CliExit.NoWorkNeeded).ShouldContain("No changes detected.");
```

Mode E is not an attribute, so [the catalogue check](#the-catalogue-check) does not check it.

## The Elementary set

The PR gate set, `--treenode-filter "/**[Category=Elementary]"`, 12 cases: SGN-DEV-01 (device code sign-in), SGN-MSI-01 (machine sign-in), ACC-SET-01 (`account set`), MCH-LIF-03 (a created API key machine reads), ANN-SET-01 and ANN-SET-08 (annotation round trip, Reader refused), CFG-GET-01, CFG-GET-02 and CFG-SET-01 (configuration model, resolved, set), CFG-GHA-01 (`config export --github-env`), KV-PAR-01 and KV-SEC-01 (a public pair, a secret). They need the pool passwords, `Machines:apikey`, and a Reader who reads `Target:Project` (the Reader cases arrange their data as an Editor there).

## Self tests

`SelfTests/` (category `SelfTest`): plain TUnit tests of the suite's own machinery that need no target and run in every run: the built sform starts (`--version`, `--help`), an unknown command ends with -1, a prompt on the redirected input ends at once with 43, the API of a run is the one given (a closed port is 3), the input is written and closed, the outer `STORYTELLER_*` variables do not leak, the environment is built from scratch, a timeout kills the process tree, a child left running does not hold the run and is killed, the input and output of a .NET child on Windows in the OEM code page (and the decoder of mixed UTF-8 and OEM bytes), the escape sequences are removed, the JSON is found behind the gutter, and the 80-column folds of an older sform's WriteJson are joined (`WrappedJson`, the safety net: the lines Spectre rendered at 80 columns for an API key, a unit key, wrapped text; output without folds stays as it is), 255 is read as -1 off Windows, the ::add-mask:: values are found, an assertion message masks what it looked for, `ShouldNotMatch` fails only for a match, the rows of a versions table are read after its header (`VersionRows`), also with the column Restored from (`ConfigOutput.VersionRestoredFrom`), the `KeyValues` settings are required with `Byok` and have their shape, `Byok` stands for `ByokApplication` too, and the feature users are leased only for their feature and never by the target check (`SettingsSelfTests`), the environment without SFORM_HOME, the device code is parsed (JSON and text), the printed credentials are found, the command lines are quoted for Windows and the shell, the device page's steps on a stand-in of AuthKit (skipped without Chromium), `assist key` answered in a pseudo terminal (skipped where none is supported), the [fake editor](#the-fake-editor-mode-e) started as sform starts a custom editor (`FakeEditorSelfTests`: built and recorded without a space in its path, each change, the steps of a sequence counted per run, the log of one run (`ReadLog(options)`), two editors of one test kept apart, a build path with a space copied to a folder without one, `DOTNET_ROOT` only for a real installation, the failure without a mode, a quoted path with spaces), the rules of [the command coverage](#command-coverage) and of [the catalogue check](#the-catalogue-check) on a small command tree built by hand (`CoverageRulesSelfTests`: aliases in the middle and at the end of a path, help-only groups, the implicit `-p`, values in any form and case, the problems of a `[Covers]`, listed, unlisted and stale exceptions, the drift checks of `coverage-values.json` (also for the values of an argument), the strict loading of both json files, and `CatalogueCovers` on a context row, a value listed once for several commands and a token listed twice), and mode E end to end (`EditorThroughSformSelfTests`: the built sform runs `story config edit -y` as a machine with an API key against a stub of the API on the loopback (`StubApi`), with the fake editor in the `editor` section of `sform.settings.json`: a merge is saved with the merged content, `none` is 42, `invalid` is 2, a failing editor is 1; and `story edit` in a pseudo terminal, saved after Yes as a replace of the edited document, and with redirected streams, 43 before any request).

## Command coverage

Spec §4.2: every command, alias, option form, positional argument and fixed option value of sform is covered by a `[Covers]` of a test. `CommandCoverageTests` (`Coverage/`, category `Coverage`, not Elementary, no target) checks it in every run, also in `dotnet test --solution 42.mono.slnx`, so a new option of the CLI without a test fails the run.

**The command tree** comes from the build of `src/Platform/Cli/src` that the tests' build made (the `SformPath` metadata; `Cli:Path` is not used, the coverage is that of the source): `CommandTreeReader` opens `42.Platform.Cli.dll` with `System.Reflection.MetadataLoadContext` (metadata only, no code of the CLI or of McMaster runs) and follows what McMaster.Extensions.CommandLineUtils 5.1 builds by reflection (sform's build has no generated command metadata):

- the commands from the root `_42.Platform.Cli.Commands.PlatformCommand` along the `[Subcommand(typeof(…), …)]` attributes, named by `[Command(name, aliases…)]` (or `Name =`; without a name, the class name without `Command` in kebab case);
- the options of `[Option]` and `[VersionOption]` on the properties (and fields) of the class and its base classes, so `-p|--projectKey` and `-v|--view` of `BaseContextCommand` belong to every context command (also through `SchemaApiCommand` or `KeyValueCommandBase`). The forms come from the template (`-p|--projectKey <KEY>`, `--name=<V>`, `-n|--name:<V>`); without a template from `ShortName`/`LongName`, else from the member name in kebab case and its first letter (`FooBar` is `-f|--foo-bar`, as McMaster's reflection derives it; its source-generated metadata, which sform does not use, would derive `--foobar`). `[HelpOption]` and the `-?|-h|--help` of McMaster's conventions are no items;
- the positional arguments of `[Argument(order, name)]`, sorted by order: `arg0`, `arg1`, …;
- a group that only prints its help (its `OnExecute`/`OnExecuteAsync` calls `ShowHelp`, read from the IL): `sform`, `storyteller schema`, `schema type`, `schema annotation` and `schema descendant`.

At the current CLI that is 110 commands, 35 aliases, 651 option forms, 93 arguments and 147 fixed values: 1036 items, every one covered (`CommandTree_IsReadFromTheCli` prints the counts).

**The rules** (the catalogue's, the Covers column of [test-cases.md](test-cases.md)):

| `[Covers]` | Covers |
|---|---|
| The path, as typed after sform (`"story kv set"`, `"sform"` for the root) | Its command when the path ends with the command's canonical name (`story config` covers `config`, `story configuration` only the alias); every alias it types, anywhere (`story kv set` covers the alias `story`); every help-only group it names (`story schema type set` covers `schema` and `schema type`) |
| (implicit) | The context option `-p` of the path's command when it is a context command: every case passes `-p` (the catalogue's rule), so any case of the command covers it; `--projectKey`, `-v` and `--view` need a case of their own |
| An option form, `"-r"`, `"--resolved"` | That form of an option of the path's command (each form is an item) |
| `"--option=value"` | That value of an option with a fixed set ([coverage-values.json](#coverage-valuesjson)); any form of the option, the value in any case |
| `"argN=value"` | That value of a positional argument with a fixed set ([coverage-values.json](#coverage-valuesjson), `"argument"`: the type of `account settings editor`, the key of `account settings set` and `unset`); the value in any case |
| `"arg0"`, `"arg1"`, … | The positional argument at that position |

Every `[Covers]` of the assembly counts, on a method or a class, whether its test runs or skips. The tests:

| Test | Checks |
|---|---|
| `CommandTree_IsReadFromTheCli` | The reader on stable commands: the root and its `--version`, the alias `story`, the inherited context options, the own `-p` of `account set`, the help-only groups, the arguments of `config diff` |
| `Covers_NameOnlyWhatSformHas` | Every `[Covers]` names a path, options, values and arguments sform has, and sits on a `[Test]`; the message says what the command has instead |
| `CoverageValues_MatchTheCli` | [coverage-values.json](#coverage-valuesjson) against the CLI |
| `CoverageExceptions_ListTheFeatureGatedItems` | [coverage-exceptions.json](#coverage-exceptionsjson) against the tests |
| `EveryCommandOptionArgumentAndValue_IsCovered` | The gap: every item no `[Covers]` covers, grouped by command in the tree's order, one line per kind |

The message of the gap lists what is left to cover, e.g. at phase 4, before the cases were automated:

```text
907 of 946 items of sform's command tree are covered by no [Covers] of the tests (15 [Covers] read): commands 90 of 99, aliases 33 of 35, option forms 580 of 600, arguments 74 of 81, values 130 of 131.
…
account points grant [AccessPointGrantCommand]
  command   grant
  option    -p, --point, -r, --role
  argument  arg0 (AccountId)
  value     --role=Reader, --role=Contributor, --role=ContributorWithSecrets, --role=Administrator, --role=Owner
```

### `coverage-values.json`

`src/Platform/Cli.AutomationTests/src/coverage-values.json` (copied to the output) lists the options whose values come from a fixed set (spec §4.1); each value is an item, covered as `--option=value`. An entry with `"argument": "arg0"` instead of `"option"` is a positional argument with such a set, each value covered as `arg0=value`:

```json
{
  "command": "storyteller kv protect",
  "option": "--protection",
  "values": [ "standard", "hsm", "byok" ],
  "enum": "_42.Platform.Storyteller.Sdk.KeyValueProtection",
  "except": [ "Public", "Hyok" ],
  "source": "KeyValueProtectCommand (KeyValueBulkCommands.cs): KeyValueLevels.Parse, then public and hyok are refused …"
}
```

- `command`: the canonical path without sform (an alias in it fails the test: the entry would add no value to cover); `option`: its long form, or `argument`: its token (`arg0`, `arg1`, …), exactly one of the two; `values`: as the catalogue writes them, compared without case; `source`: where the set lives in the CLI's code, and who refuses the members left out (required).
- **`values` are every value the CLI accepts and acts on**, however odd: each is an item a case has to cover, so the case shows what it does.
- sform's options are strings validated in code, so for most of them the file is the source of the set. When the code parses the value into an enum, `enum` names it (in `42.Platform.Cli.dll` or a `42.*.dll` next to it, such as the SDK's), and the test checks that `values` are exactly its members, but those in `except` and `refused`, plus those in `extra` (`config -f` takes `json` besides `ConfigurationFormat`): a member added to the enum in the SDK or the CLI fails the test until the file (and a case) has it.
  - `except`: members **the CLI itself refuses** with exit 2 before any call (`kv protect --protection` public and hyok, `kv policy rotate --protection` public, byok and hyok, the runtime scopes of `machine create -m`).
  - `refused`: members the CLI **sends and the platform refuses** (`points grant` and `revoke -r None`, `kv policy set --default public`); `source` says where the API refuses them. They are no items: nothing of the CLI acts on them.
  - A member in both, or a name the enum does not have, fails the test; so do `except`, `refused` or `extra` on an entry without an enum.
- An option whose own type is an enum (none at phase 1) must be listed, with exactly the enum's names (but `except` and `refused`); the test names any that is not.
- The test also fails for an entry that names no command or option, has no values or no source, or lists an option or a value twice. A field the file does not know (a misspelling) fails the load with the file's name; `"$comment"` is allowed on the file and on each entry.

It has the 31 options and 3 arguments of the catalogue's fixed sets (the type of `account settings editor` and the keys of `account settings set|unset`; `points grant|revoke -r`, `machine create --scope|-m|-k`, `machine-auth -k|-d`, `story -b`, `export -f|-i`, `import -i|-e`, `config -f`, `config export -f|--mask`, `kv --protection`, `kv set|protect|export|import --protection`, `kv export|import --format`, `kv import --mode`, `kv policy set --allow|--default|--require-mtls`, `kv policy rotate --protection`, `kv audit --operation`, and `--format` of the three schema diffs), 25 of them checked against an enum. `kv import --protection` takes `standard`, `hsm` and `byok`; `public` and `hyok` end with exit 2 (the keys `--secret` matches are secrets), so they are in `except`.

It also has the 3 positional arguments with a fixed set: the type of `account settings editor` (`vscode`, `neovim`, `vim`, `custom`, checked against the CLI's `EditorType`) and the key of `account settings set` and `account settings unset` (`editor.type`, `editor.command`, `access.project`, `access.view`: `UserSettingDefinitions.All`, no enum), covered as `arg0=<value>` by the cases of [Settings](test-cases.md#settings-accountsettingstests).

### `coverage-exceptions.json`

`src/Platform/Cli.AutomationTests/src/coverage-exceptions.json` (copied to the output) lists the items that only tests a target may lack the feature for (`[RequiresFeature]`, mode X) or that never run (`[Skip]`) cover, each with the reason; their tests exist and skip where `Target:Features` lacks the feature:

```json
{ "command": "storyteller kv set", "item": "--protection=byok", "feature": "Byok", "reason": "KV-POL-07: a value at the byok level needs the customer key of KV-POL-05." }
```

`item` is an option form, a `--option=value`, an `argN` or an alias, and missing for the command itself; `feature` is one of `CliFeatures`. `CoverageExceptions_ListTheFeatureGatedItems` fails for an item covered only by such tests that the file does not list, and for a stale entry: one that names nothing sform has, has no reason, names a feature that is none of `CliFeatures`, is listed twice (also through an alias path), is covered by a test that runs against every target, or names another feature than its tests require. A field the file does not know fails the load, as in `coverage-values.json`. An entry whose item no test covers yet is no error (the item is in the gap until its test comes). The file has the 13 items that only the BYOK (KV-POL-05, KV-POL-07) and HSM (KV-POL-08) cases cover: `--byok-key`, `--byok-tenant`, `--allow=byok|hsm` and `--default=byok|hsm` of `kv policy set`, `--protection=byok|hsm` of `kv set`, `kv protect` and `kv import`, and `--protection=hsm` of `kv policy rotate`. The other X cases (SGN-DEV-06, SGN-OUT-04) cover nothing that an ungated case does not cover too.

## The catalogue check

`CatalogueTests` (`Coverage/`, category `Coverage`, no target) compares [test-cases.md](test-cases.md) with the tests of `Tests/`. It finds the repository from the test assembly (the first folder above it with `42.mono.slnx`) and skips with the reason when the assembly runs outside it (a copy of the output).

| Test | Checks |
|---|---|
| `Catalogue_IsWellFormed` | Every row parses (8 cells, under a section of the "Cases per section" table and a sub-folder heading `### <Title> (<Class> in backticks)`); unique references, Testiny ids (numbers) and planned tests; known priorities, roles, modes and features; the counts of the section table; every P case an Owner case, every Machine case mode M; no empty Covers |
| `EveryCase_IsAutomated` | The gap: every row whose planned method does not exist yet, grouped by class, with the attributes it needs and its Covers |
| `EveryAutomatedCase_MatchesItsRow` | Every row whose test exists, by the rules below |
| `EveryTest_HasACase` | Every `[Test]` of `Tests/` is planned by a row, its folder is a section, and its `[TestCase]` ids are ids of rows |

A row's test matches it when:

- **Place**: the method of the Test column is in the class of the row's sub-folder heading, `_42.Platform.Cli.AutomationTests.Tests.<Folder>.<Class>` in `Tests/<Folder>/<Class>.cs` (the folder from the section table), and a source file of the folder holds the comment `// <Reference>:` (the comment above the test starts with the reference).
- **`[TestCase(<id>)]`**: exactly the row's Testiny id (method and class).
- **Role** (`[UserRole]` of the method or the class, `Role.Any` without one): Any, Reader, Contributor, Editor, Secrets, Admin and Owner are `Role.<the same>`; Machine is `[MachineOnly]` (and no `[UserRole]`); only a Machine case has `[MachineOnly]`.
- **Modes**: F is `[FreshSignIn]`, T `[Terminal]`, P `[DisposableProject]`, `X:<f>` `[RequiresFeature(CliFeatures.<f>)]`, each in both directions. M is `[MachineOnly]` for the role Machine (above) and a machine run (`AsMachine`) otherwise, E a run with the fake editor, U the default: no attribute shows them, so they are not checked.
- **Priority**: Elementary is `[Elementary]`, in both directions.
- **Covers** (`CatalogueCovers`): the `[Covers]` of the method and the class, resolved as [the command coverage](#command-coverage) does.
  - Every command or alias token of the row is covered by a `[Covers]`: a path that ends with the command's name, types the alias anywhere, or names the help-only group.
  - A `[Covers]` without items covers a command token of the row by its last word.
  - Every option, value and argument token of the row is an item of **at least as many commands** of the test as the row lists it. A token the row lists twice (`arg0` of `set` and of `get`) needs two commands that cover it; a token the row lists once may be covered on any number of commands. That is how a context case works: it lists `--projectKey`, `-v` and `--view` once and covers them on every command of its steps; and KV-POL-07 lists `--protection=byok` once for `kv set`, `kv protect` and `kv import`. Two paths to one command (`story config` and `story configuration`) are one command.
  - Every item token of a `[Covers]` is listed by the row.
  - Option forms compare exactly (`-r` is not `--role`); a value compares as the catalogue writes it, by the option's long form and the value of `coverage-values.json` (`-r=reader` is `--role=Reader`).
  - A `[Covers]` that names nothing of sform is a mismatch too.

Each of the 258 rows has its test, so all four pass; a row added to the catalogue fails `EveryCase_IsAutomated` until its test exists, and a changed row fails `EveryAutomatedCase_MatchesItsRow` until its test follows.

## Automating a case

For a new row of the catalogue, or a new command, option or value of sform (every case of the catalogue is automated):

1. Run the coverage checks: `dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=Coverage]"` (or `dotnet test --project … --treenode-filter …`).
2. Take a class of `EveryCase_IsAutomated`: open or create `Tests/<Folder>/<Class>.cs` (`[Area(Area.<section>)]`, `: CliTest`) and, for each of its rows, the method with the comment `// <Reference>: <title>` and the attributes the message names (`[Test] [TestCase(<id>)] [UserRole(Role.X)]` or `[MachineOnly]`, `[FreshSignIn]`, `[Terminal]`, `[DisposableProject]`, `[RequiresFeature(CliFeatures.X)]`, `[Elementary]`).
3. Write the `[Covers]` from the Covers column: one per command of the steps, its path as the steps type it (with `story` for the area commands the catalogue writes without it: `kv get` is `"story kv get"`), and as items the row's tokens that this command's steps use: option forms as written (`-r` and `--resolved` are two items), a fixed value as `--option=value` (any form of the option, the value in any case: `-r=reader` of `account points grant` is `--role=Reader`, also for the row comparison), positional arguments as `arg0`, `arg1`, …. A command token of the row is the path's last word, or an alias in the path (`[Covers("story configuration", "arg0")]` covers the alias `configuration`, not `config`).
   - A row like `set`, `arg0`, `get`, `arg0` is `[Covers("story set", "arg0")]` and `[Covers("story get", "arg0")]`.
   - A token the row lists once that the steps use on several commands goes on each of them: the context cases put `--projectKey`, `-v` and `--view` on every command their steps run with them (`[Covers("account machine create", "--projectKey", "-v", "--view")]`, …), which is what covers those forms of every context command; KV-POL-07 puts `--protection` and `--protection=byok` on `kv set`, `kv protect` and `kv import`.
   - A command the case only uses for its setup gets no `[Covers]` (a `[Covers]` without items claims the command and must be in the row).
   - `-p` of a context command counts with any `[Covers]` of the command; GEN-CTX-01 and GEN-CTX-03, which list `-p`, write it as an item.
4. Run the checks again: `EveryAutomatedCase_MatchesItsRow` names every difference from the row, `Covers_NameOnlyWhatSformHas` a `[Covers]` that names something sform does not have, and the gap of `EveryCommandOptionArgumentAndValue_IsCovered` shrinks.
5. A case gated by a feature (X) whose items no other case covers: `CoverageExceptions_ListTheFeatureGatedItems` lists the items to add to `coverage-exceptions.json` (with the feature, and the case in the reason); a stale entry is to be removed.
6. A new fixed set (an option whose values the code restricts): add it to `coverage-values.json` with its source (and its enum), and a case per value to the catalogue.
7. When a row changes in the catalogue, the same checks show what its test has to follow; the catalogue stays the source of truth, also for Testiny.

## Testiny

Project **`2SC`** (id 2), created by hand in the Testiny app. Its cases were created from [test-cases.md](test-cases.md) through the Testiny API: 229 in phase 4 (10 top-level folders, one per section, with one sub-folder per test class), and 22 more after the rebase onto the sform changes of #172 to #176 (`story edit`, `story set --replace`, `story config export`, `account settings`), and 7 more for the restore of a version (Linear 424-221: CFG-HIS-07 to CFG-HIS-09, TPL-TPL-10, SCH-TYP-06, SCH-ANN-06 and SCH-DSC-06), 258 in all; their ids are in the catalogue's Testiny column, and `[TestCase(<id>)]` links each test to its case (checked by [the catalogue check](#the-catalogue-check)). `UITESTS_PROFILE=<target>,reporting-testiny` with `Reporting:Testiny:ApiKey` in the user secrets reports a run as a classic Testiny test run (the [library's Testiny reporter](../../Libraries/Testing/UI.Automation/readme.md#testiny)); the attachments are the redacted `sform-*.txt` runs and the device page's screenshot and text of a failure.

## CI

`.github/workflows/Platform.Cli.AutomationTests.yml` runs the suite on `ubuntu-latest` with the SDK of `global.json`, by hand (`workflow_dispatch`) or from another workflow (`workflow_call`); it has no push or pull request trigger yet (gating pull requests on the Elementary set is a follow-up). What needs no target runs on every pull request that touches the CLI or its tests: `.github/workflows/Platform.Cli.UnitTests.yml` runs the unit tests of the CLI and the self tests and coverage checks of this project (`--treenode-filter "/**[(Category=SelfTest)|(Category=Coverage)]"`), without secrets. The runs share the CLI pool, so they share one concurrency group: one at a time, the others queued, a running one never cancelled.

| Input | Default | Meaning |
|---|---|---|
| `profile` | `""` | Settings profiles, comma-separated, after `ci` (always applied) |
| `filter` | `/**[Category=Elementary]` | TUnit's `--treenode-filter`; empty runs every test |
| `target_url` | `""` | The API to test, `/api` included; empty takes the environment's `UITESTS_TARGET_BASE_URL` (with neither, the job fails at once) |
| `report_testiny` | `false` | Adds the `reporting-testiny` profile and the Testiny key; the run's URL goes into the job summary |

The steps: check the inputs, check out the whole history (the versioning needs it), set up the SDK, build Release (sform and the fake editor too), install Playwright's **Chromium only** (`playwright.ps1 install --with-deps chromium`, for the device page; the terminal cases use util-linux `script` of the image), and run `dotnet run -c Release --no-build -- --treenode-filter <filter> --report-trx --results-directory TestResults/cli-automation` with its log in `run.log`.

The target and its secrets come from the **`cli-automation` environment** (restrict it to `main`: its secrets are the pool's passwords, the machine key and the Testiny key). A variable that is empty or unset keeps the committed value:

| Environment | Setting |
|---|---|
| `UITESTS_TARGET_BASE_URL`, `UITESTS_TARGET_ORGANIZATION`, `UITESTS_TARGET_PROJECT`, `UITESTS_TARGET_VIEW`, `UITESTS_TARGET_AUTHKIT_ORGANIZATION_ID` | `Target:BaseUrl` (unless `target_url`), `Organization`, `Project`, `View`, `AuthKitOrganizationId` |
| `UITESTS_TARGET_FEATURES` | `Target:Features`, comma-separated |
| `UITESTS_KEYVALUES_BYOK_KEY_URI`, `UITESTS_KEYVALUES_BYOK_TENANT_ID` | [`KeyValues`](#keyvalues)`:ByokKeyUri`, `ByokTenantId` (required when the features list `Byok`) |
| `UITESTS_USER_<n>_EMAIL`, `UITESTS_USER_<n>_ROLES` (comma-separated), secret `UITESTS_USER_<n>_PASSWORD`, n = 0…7 | `Users:Pool:<n>`: a password applies to the pool by position; any e-mail makes the variables' entries replace the committed pool entirely (a [feature user](#feature-users) is one with the feature among its roles; the 8 slots hold at most 8 users, so a feature user takes the slot of one of the committed pool's 8 role users) |
| `UITESTS_MACHINE_APIKEY_PROJECT`, `UITESTS_MACHINE_APIKEY_VIEW`, secret `UITESTS_MACHINE_APIKEY_API_KEY` | `Machines:apikey` (without the key the machine sign-in cases skip) |
| `UITESTS_TESTINY_PROJECT_ID`, `UITESTS_TESTINY_TEST_PLAN_ID`, secret `UITESTS_TESTINY_API_KEY` | `Reporting:Testiny:*`, with `report_testiny` only |

Only the `Run` step gets the secrets (the Testiny key only with `report_testiny`); a caller of `workflow_call` may pass secrets of the same names, which the environment's win over. On a failure or a cancellation the artifact `cli-automation-<profile or base>-<attempt>` (7 days) holds `TestResults/cli-automation`: the TRX report, `run.log`, and `artifacts/<runId>/` with `results.jsonl` and each test's redacted `sform-<n>.txt` (and the device page's screenshot and text of a failed sign-in); any `.zip` (a trace, off in every committed profile) is deleted first. GitHub masks the secrets in its console log only, so the `Run` step replaces every secret it was given (the pool passwords, the machine key, the Testiny key) with `***` in every text file of the results, `run.log` included, before the upload. The homes and the working directories are in the runner's temporary folder and never uploaded.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Every test fails at once with `The UI automation settings are not valid: … Users:Pool:<n>:Password is required` | The pool passwords are not in the user secrets (or `UITESTS_Users__Pool__<n>__Password`) |
| `sform is not built at '…'` | Build the tests (which builds the CLI), or set `Cli:Path` |
| `The target check failed: … could not sign in through sform's device code … the device page showed: …` | The device page did not approve the code: see `TargetCheck/CheckTarget/device-page-1.png` and `.txt`; a wrong password, a user without verified e-mail or with MFA, the device code grant off, or labels the step machine does not know (`AuthKitDevicePage`) |
| `… ended with 2 … You are not a member of the organization 'org_…'` | `Target:AuthKitOrganizationId` is not the user's WorkOS organization (not the 2S organization name) |
| `… cannot read the project cli-tests.automation …` | The organization or the project is missing, or the user is no member |
| `Machines:<name> cannot read …` | The machine's credential is wrong or revoked, it lacks `Annotation.Read`, or its project or endpoint is wrong |
| `[the tests stopped reading: a process sform started kept its standard streams open after sform ended]` | Something sform started (`SFORM_BROWSER`, an editor) outlived it and could not be killed |
| `Requires the machine apikey: Machines:apikey is not configured …` (skipped) | Set `Machines:apikey:ApiKey` in the user secrets |
| `Terminal: A pseudo terminal on Linux needs util-linux script …` (skipped) | Install util-linux (`bsdutils`) |
| `Playwright's Chromium is not installed` (a self test skipped), or a sign-in Blocked with `BrowserUnavailableException` | `pwsh bin/<configuration>/net10.0/playwright.ps1 install chromium` |
| `SformTimeoutException: sform … did not end within 120 s` | A run waited (e.g. a device code nobody approved); the attachment shows how far it came |
| `EveryCommandOptionArgumentAndValue_IsCovered` or `EveryCase_IsAutomated` fails | sform has a command, option or value, or the catalogue a row, that no test covers yet; see [Automating a case](#automating-a-case) |
| A list or a write right after a change misses it on a scaled-out API, then passes on a rerun | The API caches per instance and evicts a copy only on the instance that made the change: trusted issuers (`CosmosFederatedIssuerStore`, 60 s), shared certificates (`CosmosSharedCertificateStore`, 5 min), the machine authentication policy (`CosmosMachineAuthenticationPolicyStore`, 60 s) and the key/value policy (`CosmosKeyValuePolicyStore`, 60 s, which writes and reveals check). The tests re-read within that window (`EventuallyAsync` with `IssuerCacheWindow`, `SharedCertificateCacheWindow`, `KeyValuesSupportA.PolicyCacheWait`); a new read of such a cache needs the same |
| `The UI automation settings are not valid: Target:Features lists Byok, so the tests need the target's customer key …` | Set `KeyValues:ByokKeyUri` and `KeyValues:ByokTenantId` ([`KeyValues`](#keyvalues)), or leave `Byok` out of `Target:Features` |
| `No user of Users:Pool has role Registration` (or `MultiOrganization`) | `Target:Features` lists the feature, but no pool user has its name among its `Roles` ([feature users](#feature-users)) |
| `No user of Users:Pool has role Reader and none of Registration, MultiOrganization` (or another role) | Every pool user of the role stands for a feature: add a user of the role without the feature. When it names every role, a layer above `testsettings.json` (the user secrets, `UITESTS_Users__Pool__<n>__*`) sets only the feature users and so replaces the whole pool: restate the role users in that layer before them ([feature users](#feature-users)) |
| `The target check failed: The target check signs in a pool user who stands for no feature …` | Every pool user stands for a feature, usually a layer that sets only the feature users and so replaces the whole pool: restate the role users in that layer before them ([feature users](#feature-users)) |
| `Editor exited with code 65.` in an E case | The test ran `edit` without `editor.With(EditorChange.…)`: the fake editor has no `CLI_EDITOR_MODE` |
| `The fake editor's path '…' contains a space, and so does the temporary folder` | sform splits the custom command at its first space; the editor is copied to `<temp>/sform-automation/fake-editor/` when the build's path has a space, which needs a temporary folder without one (or with an 8.3 name on Windows): point `TEMP`/`TMPDIR` to one, or build the repository in a folder without spaces |
| `Editor exited with code 131.` in an E case | The editor's apphost found no .NET runtime: a `DOTNET_ROOT` of the machine that is not an installation, or a dotnet the tests use outside the global location without `host/fxr` next to it; set `DOTNET_ROOT` to the installation |
| `… coverage-values.json does not read as …` or `… coverage-exceptions.json does not read as …` | A misspelled or unknown field (or invalid JSON) in the file; the message names it |
| `CatalogueTests` skipped: `The test assembly runs outside the repository …` | The assembly runs from a copy of the output; run it from the repository's build |
