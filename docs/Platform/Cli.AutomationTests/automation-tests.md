# CLI Automation Tests

Tests of the real `sform` executable against a real Storyteller deployment: the source build of `src/Platform/Cli/src`, started out of process the way a script or a CI job starts it, signed in as pool users through the AuthKit **device code** (the code approved by Playwright on the AuthKit page) or as **machines** (`STORYTELLER_*` variables), checking exit codes, output, JSON and files. The project is `src/Platform/Cli.AutomationTests/src` (`42.Platform.Cli.AutomationTests`, TUnit), built on the [UI automation libraries](../../Libraries/Testing/UI.Automation/readme.md) (`42.Testing.UI.Automation.*`) as [tests without a browser](../../Libraries/Testing/UI.Automation/readme.md#tests-without-a-browser-automationtest), with the Platform pieces of [`42.Platform.Automation.Shared`](../../../src/Platform/Automation.Shared/README.md). The libraries own the settings layering, the user pool, the seeded data, the artifacts, the retries and the reporting and document every setting; this page is what the CLI suite adds.

`Cli.UnitTests` (`src/Platform/Cli/test`) stays the hermetic, in-process suite of the CLI; this suite covers the integrated system.

Spec: [2026-11-08 CLI Automation Testing Project](specs/2026-11-08%20CLI%20Automation%20Testing%20Project.md) (Linear [424-215](https://linear.app/424fun/issue/424-215/create-cli-automation-testing-project-sform-against-a-real-backend)); the reviews of its phases: [Phase 1](../Cli/specs/reviews/2026-11-08%20Phase%201%20of%20CLI%20Automation%20Testing%20Project.md) (the CLI changes: `SFORM_HOME`, `SFORM_TOKEN_STORE`, `-o`, no hangs, the device code as JSON), [Phase 2](../../Libraries/Testing/UI.Automation/specs/reviews/2026-11-08%20Phase%202%20of%20CLI%20Automation%20Testing%20Project.md) (tests without a browser, the shared library) and [Phase 3](specs/reviews/2026-11-08%20Phase%203%20of%20CLI%20Automation%20Testing%20Project.md) (this project). The test cases: [test-cases.md](test-cases.md) (Testiny project `2SC`).

## Run

```bash
dotnet build src/Platform/Cli.AutomationTests/src          # builds sform (src/Platform/Cli/src) too
pwsh src/Platform/Cli.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install chromium   # once: the device page's browser (Linux: install --with-deps chromium)

dotnet test --project src/Platform/Cli.AutomationTests/src                                                   # without a target: the self tests run, every CliTest is skipped
UITESTS_PROFILE=dev dotnet run --project src/Platform/Cli.AutomationTests/src                                # against the local API (Aspire or func start, :7071)
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"    # the PR gate set
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=Area-KeyValues]"
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category=SelfTest]"      # the suite's own machinery, no target needed
dotnet run --project src/Platform/Cli.AutomationTests/src -- --treenode-filter "/**[Category!=DisposableProject]"
dotnet run --project src/Platform/Cli.AutomationTests/src -- --test-parameter Target:BaseUrl=https://<host>/api --test-parameter Execution:Seed=1383911092
dotnet run --project src/Platform/Cli.AutomationTests/src -- --maximum-parallel-tests 1 --test-parameter Browser:Headless=false
dotnet test --solution 42.mono.slnx                                                                          # with every other test of the repository
```

- **`dotnet test`** runs on Microsoft.Testing.Platform (`global.json`): `--project src/Platform/Cli.AutomationTests/src <options>` takes the options of `dotnet run` without the `--`; TUnit's options (`--treenode-filter`, `--test-parameter`, `--maximum-parallel-tests`) go only with `--project`. `dotnet test` prints the suite's output only when it fails; `dotnet run` shows the `ui-automation:` lines (the run id, the seed, the effective settings with secrets as `***`, the target check, the totals and the Testiny run).
- **Without a target** (`Target:BaseUrl` empty, the committed default) every `CliTest` is skipped with `UITESTS_Target__BaseUrl is not configured; see docs/Platform/Cli.AutomationTests/automation-tests.md.`, the **self tests** (`SelfTests/`, category `SelfTest`) still run against the built `sform` (no network: their API is a closed port), and the run exits `0`, also in `dotnet test --solution 42.mono.slnx`. The self tests of the device page skip without Playwright's Chromium, those of the terminal where no pseudo terminal is supported.
- **The `dev` profile** targets `http://localhost:7071/api`: start the API first (`dotnet run --project src/Platform/Aspire.Host/src`, or `func start` in `src/Platform/Storyteller/Api.Functions/src`). A Debug build of the API skips the role checks of users (`DEV_AUTH`, [access.md](../Storyteller/access.md#roles)), so the Reader and Contributor refusals show only against a Release API. sform signs in against AuthKit staging (its `app.config.json`), so the committed pool users and their passwords are needed locally too.
- Results go to `src/Platform/Cli.AutomationTests/src/bin/<configuration>/net10.0/TestResults/` (git-ignored) under `dotnet run`, or `TestResults/` of the current directory under `dotnet test`: TUnit's HTML report, and `artifacts/<runId>/` with `results.jsonl` and a folder per test (`Tests.Annotations.AnnotationEditTests/Story_SetGet_RoundTrip/`) with its `sform-<n>.txt` runs (and `work-files.txt` when its working directory was kept). The homes of sform and the working directories of the tests are **not** there but under `<temp>/sform-automation/<runId>/` ([below](#homes-and-working-directories)), as they hold plain sessions, private keys and resolved secrets.

## Configure

Settings come in layers, each over the one before ([the library's settings](../../Libraries/Testing/UI.Automation/readme.md#settings)): `testsettings.json` (committed, no secrets), `testsettings.<profile>.json` for each profile of `UITESTS_PROFILE` (comma-separated), `testsettings.local.json` (git-ignored), the user secrets of the project (`UserSecretsId` `be44b33c-c6f8-469a-aa25-4963a533f962`), `UITESTS_*` variables (`UITESTS_Target__BaseUrl`) and `--test-parameter Section:Key=value`.

```bash
cd src/Platform/Cli.AutomationTests/src
dotnet user-secrets set "Users:Pool:0:Password" "…"          # one per pool user, by position (0…7)
dotnet user-secrets set "Machines:apikey:ApiKey" "2s.…"       # the API key machine of the machine sign-in cases
dotnet user-secrets set "Reporting:Testiny:ApiKey" "…"        # with the reporting-testiny profile
```

| Profile | What |
|---|---|
| `dev` | `Target:BaseUrl` `http://localhost:7071/api`, the local API |
| `ci` | One retry, `Users:LeaseTimeoutSeconds` 300; the target and the secrets come from the job's `UITESTS_*` variables (phase 5 adds the workflow) |
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
| `Features` | `[]` | What the target offers beyond the basics: `Byok`, `Hsm`, `Registration`, `MultiOrganization` (`CliFeatures`); a `[RequiresFeature("<Feature>")]` test skips unless listed |

### `Cli`

| Key | Default | Meaning |
|---|---|---|
| `Path` | `null` | `42.Platform.Cli.dll` (run as `dotnet <dll>`) or an `sform` executable (an installed tool); `null` runs the build of `src/Platform/Cli/src` of the tests' configuration, whose path MSBuild records in the test assembly (`[assembly: AssemblyMetadata("SformPath", …)]`, target `RecordSformPath` of the project file). Relative to the test assembly's folder |
| `TimeoutMs` | `120000` | How long one run of sform may take; then its process tree is killed and the test fails with the output so far. The device code sign-in waits within it |
| `KeepWorkingDirectories` | `false` | A test's working directory is removed when the test passes and kept (with `work-files.txt`, the list of its files, attached) when it does not; `true` keeps every one |

### `Users` and roles

The CLI has **its own pool** (`cli-test-<role>-<n>@42for.software`), provisioned in the same AuthKit environment as the admin UI's `ui-test-*` pool and with the same roles in `cli-tests/automation`, so the two suites run at the same time without sharing sessions: 2 Owners, 2 Administrators, 2 Contributors, 2 Readers (`testsettings.json`). The passwords go to the user secrets (`Users:Pool:<n>:Password`) or `UITESTS_Users__Pool__<n>__Password`; every pool user needs one (the device page asks for it). A test acts as `[UserRole(Role.X)]` (the shared `Role`): `Any`, `Editor` (Contributor and up), `Admin`, `Secrets` (ContributorWithSecrets and up), `Owner`, or exactly `Contributor` or `Reader`, which act in the project `Target:RoleProjects` maps them to. `ProjectKey`, `View` and `InProject(...)` follow the role (or the project of `UseProject`). An exact `Administrator` or `ContributorWithSecrets`, which the pool does not hold, is the "second user" of a P case: the leased Owner creates a disposable project and grants another pool user that role there (`AsGrantedUserAsync`); an organization role grants nothing in a project ([access.md](../Storyteller/access.md)), so the user then holds exactly that role. The AuthKit environment needs password sign-in, no MFA for the pool users and verified e-mails, as for the admin UI ([its checklist](../UI.Admin.AutomationTests/automation-tests.md#authkit-checklist)), and the **device code grant** enabled for sform's client (`authentication.clientId` of `src/Platform/Cli/src/app.config.json`).

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
- **Without `SFORM_HOME`** (`SformRunOptions.WithoutHome`, GEN-CLI-06 only): sform keeps its state next to its build and its session in the user's folder. On Linux and macOS the run's `HOME` and `XDG_*` folders point at an empty folder of the test, so the real profile is untouched; on Windows sform finds the user's folder through the known-folder API, which no variable redirects, so such a run reads the real profile: start it (`StartSform`) and kill it after the line the case checks.
- **Standard input**: `SformRunOptions.Input` is written, then the input is closed (at once without it), so a prompt fails fast (2 or 43) instead of waiting.
- **Timeout**: `Cli:TimeoutMs` (or `SformRunOptions.Timeout`) kills the whole process tree, and everything it started; the run fails with an `SformTimeoutException` holding the output so far.
- **What sform leaves running**: a process sform started that outlives it and inherited its streams (a browser opener, an editor gone to the background) would keep the run open. Every run is in a `ProcessJob`: on Windows a job object with kill-on-close, elsewhere the variable `CLI_AUTOMATION_PROCESS_TAG=<tag>`, which every descendant inherits and the runner finds in `/proc`. Three seconds after sform ended with its streams still open, the job's processes are killed; if the streams stay open even then, the run ends with the note `[the tests stopped reading: …]` on its standard error. The job also ends with the run.
- **Output**: both streams in full (`StdOut`, `StdErr`), as text (`PlainStdOut`, `PlainStdErr`, `Lines`: without escape sequences, and with [WriteJson's folds joined](#json)), and their lines in the order they arrived (`Timeline`, `Output`). On Windows a .NET program with redirected streams writes and reads them in the OEM code page of its hidden console (437 on an en-US system), and sform switches to UTF-8 only around a formatted payload (`config --format`/`--content-only`), so the runner reads valid UTF-8 as UTF-8 and every other byte in the OEM code page, and writes the input in the OEM code page: a character the code page lacks reaches sform as `?` and comes back from it as its best fit (a CLI that set its console encodings to UTF-8 when redirected would lose nothing). Generated data is ASCII. On Linux and macOS everything is UTF-8.
- **Exit codes**: `ExitCode` is sform's code. On Linux and macOS an exit status is a byte, so sform's -1 (`InputParsing`) arrives as 255 and is read back as -1 (`RawExitCode` keeps 255; sform defines no 255).
- **Attachments**: every run is attached to the test as `sform-<n>.txt`: the command line, the label, the working directory, `SFORM_HOME`, the **names** of the variables (never their values), whether it ran in a terminal, the exit code and the duration, and both streams (the standard output with WriteJson's folds joined), through the scope's redaction. Before that, the credentials sform printed once are registered as secrets of the test (`SecretScanner`: a created machine's `AccessKey`, `Client secret:`, `Certificate password:`, any `2s.<…>.<…>` API key, the values of `::add-mask::` lines with their escaping undone), and so is what `SformRunOptions.SecretsOf` returns for a value the test learns only from that run (a Key Vault value of `config -r`, `kv get --reveal` of a pair it did not set), so they are `***` in the attachment, the report and every later assertion message. An attachment is written when the run ends and never rewritten, so a secret registered later does not reach it.

### Homes and working directories

With `SFORM_HOME` sform keeps every file there (the default project `access.default.json`, `editor.config.json`, `auth.discovery.json`, `logs/`, and with `SFORM_TOKEN_STORE=file` the session `authkit.cache.plaintext` and the MSAL cache) and never touches the profile or its build folder. The homes and the tests' working directories are under `<temp>/sform-automation/<runId>/` (`SformHomes`), outside `Execution:ArtifactsPath`, so a job that uploads the artifacts, or a zip of `TestResults` shared for a failure, never carries a session, a `.pfx` of `machine create -o`, a `GITHUB_ENV` file with resolved secrets or a kv export:

- `homes/<user fingerprint>/`: a pool user's home for the whole session. The first run that needs it signs the user in (the target check signs its user in there, so that user's first test does not); later tests reuse the session (the refresh token rotates inside, so the folder is never copied). Before each test `access.default.json` and `editor.config.json` are removed, so a test starts without a default project unless it sets one. A lease is exclusive, so two tests never share a home.
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
- `MachineCredentials.ForApiKey`, `ForClientCredentials`, `ForCertificate`, `ForFederated(clientId, tokenFile, endpoint, projectKey)`, `FromVariables(name, variables, secrets)` and `With(changes)` build the variables of other machines (a wrong key, another `STORYTELLER_PROJECT`, a token file).

## The target check

Before the first test, `CliAutomationSetup.CheckTargetAsync` (`CliTargetCheck`, no page: `TargetCheckUsesBrowser` is `false`), as a pool user:

1. `sform --version` starts (the build of `src/Platform/Cli/src`, or `Cli:Path`);
2. the user signs in through the device code into the user's home of the session (the AuthKit device page, the password, `-o`);
3. `sform story -p <Organization>.<Project> -v <View>` reads the target's project, and the same for each `Target:RoleProjects` entry: the organization and the project exist and the user may read them. A view exists by its data (a view without data lists "No annotation exists yet." with exit 0, GEN-CTX-03), so a wrong `Target:View` is not caught;
4. each configured machine reads with `sform story -p <its project> -v <its view>`, a call to the API (`sform account` of an API key or certificate machine calls nothing), so a wrong or revoked credential, a wrong project or an unreachable endpoint fails here.

A failure fails every CLI test at once with a message that says what to fix (`The target check failed: Users:Pool:0 (u-…) cannot read the project cli-tests.automation (…): sform story … ended with 44 …`); its runs are in `TargetCheck/CheckTarget/`. The settings are checked before that (`CliAutomationSetup.Validate`): the `Target` names and `RoleProjects`, `Target:BaseUrl` an http(s) URL, `Target:SignIn` `None`, the shape of `Target:AuthKitOrganizationId`, the names of `Target:Features`, `Cli:TimeoutMs`, that sform can be found, each machine's credential kind and project, a non-empty pool and a password for every pool user.

## Write a test

Derive from `CliTest` (`src/Platform/Cli.AutomationTests/src/CliTest*.cs`), put it in `Tests/<Section>/<Class>.cs` (one folder per section of the catalogue, one class per sub-folder), start the comment above it with the case's reference, and mark it with its role, its modes and what it covers:

```csharp
[Area(Area.Annotations)]
public sealed class AnnotationEditTests : CliTest
{
    // ANN-SET-01: story set creates an annotation, and story get returns it.
    [Test]
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
| `AsAnotherUserAsync(requirement)`, `ArrangeAsEditorAsync()` | Another pool user with a home of its own (`SformActor`: `Sform(…)`, `AccountIdAsync()`), e.g. the Editor who creates what a Reader's case reads (in `Target:Project`; a role project has no writer) |
| `AccountIdAsync()` | The leased user's account id ("Account ID: #<id>") |
| `AsMachine`, `SformAsMachine`, `MachineOf`, `CreateMachineAsync(options…)`, `CreateMachineAsync(MachineCreation, options…)` | Machines (above) |
| `CreateDisposableProjectAsync(actInIt = true)` | `<Target:Organization>.<generated>` through `sform account points create` in a pseudo terminal, as the leased Owner (`[UserRole(Role.Owner)]`, `[DisposableProject]`: every P case of the catalogue); with `actInIt` the test acts in it (`UseProject`); the project stays |
| `GrantAsync(accountId, role, projectKey?)`, `AsGrantedUserAsync(role, requirement?, projectKey?)` | `account points grant <id> -p <project> -r <role>` as the leased user, only in a project of the test's own; another pool user granted the role there (the "second user" of a P case) |
| `CliData` | Seeded names: `ResponsibilityKey()` (`rst.cli-<runId>-<hex>-<words>`, `<rst>` of the catalogue), `UnitKeyOf(rst)`, `AnnotationKey(code, parts…)`, `KeyValueKey()` (`cli_<runId>.<word>_<hex>`, also a binding path), `Value()`, `LabelName()`, `ProjectName()`, `ViewName()`, `MachineName()`, `SecretValue()` (random), `EnvironmentPrefix()` |
| `WorkingDirectory`, `WriteFileAsync(path, text)`, `ReadFileAsync(path)`, `AttachFileAsync(path)`, `FileSystem` | The test's working directory (outside the artifacts, [above](#homes-and-working-directories)) and its files; `AttachFileAsync` attaches a text file redacted |
| `AddSecret(value)` | A secret the test came by (a value it set): masked in the attachments, the report and the assertion messages |
| `OnCleanUp(Func<Task>)`, `CleanUpAsync()` | Cleanups at the end, in reverse order; an override calls the base |
| `Scope`, `Session`, `Settings`, `Data`, `LeaseAnotherUserAsync`, `GetSkipReason` | The library's `AutomationTest` |

### `SformRunOptions`

`Environment` (variables to add, `null` removes), `Input` (written, then the input closes), `Timeout`, `WorkingDirectory`, `Home`, `Machine` (`MachineCredentials`), `Terminal`, `TerminalColumns`/`TerminalRows` (250 × 50), `BaseUrl`, `Secrets` (values to mask), `SecretsOf` (a function of the result that returns the secrets the test learns from this run, registered before the run is attached), `WithoutHome` (no `SFORM_HOME`, above), `RenewExpiredSession` (true), `Label` (a note in the attachment).

### `SformResult`

`ExitCode`, `RawExitCode`, `Exit` (`CliExit?`), `Succeeded`, `StdOut`, `StdErr`, `PlainStdOut`, `PlainStdErr`, `Lines`, `ErrorLines`, `Timeline`, `Output`, `Duration`, `IsTerminal`, `WorkingDirectory`, `Home`, `Text(stream)`; `JsonNode()`, `Json<T>()`, `TryJsonNode()`; the assertions, which throw an `SformAssertionException` with the command line, the exit code, what they looked for and the output, all redacted, and return the result for chaining: `ShouldSucceed()`, `ShouldExitWith(CliExit)`, `ShouldExitWith(int)`, `ShouldExitWithAnyOf(…)`, `ShouldFail()` (non-zero, not a crash), `ShouldContain(text, stream, comparison)`, `ShouldNotContain`, `ShouldMatch(regex, stream)`, `ShouldHaveLine(text, stream)`, and `Fail(what)` for a check of the test's own. `await Sform(…).ShouldSucceed()`, `ShouldExitWith` and `ShouldFail` work on the task too; anything else on the awaited result: `(await Sform(…).ShouldSucceed()).JsonNode()`. `CliExit` mirrors `src/Platform/Cli/src/ExitCodes.cs`: `InputParsing` -1, `Success` 0, `Crash` 1, `WrongInput` 2, `ServiceFailure` 3, `Aborted` 40, `NoWorkNeeded` 42, `InteractionNeeded` 43, `Unauthorized` 44, `PartialResult` 45.

#### JSON

sform prints a whole document only with `--content-only`, `--value-only`, `--format json`/`--json` (history) and into files (`-e`, `-o`, the kv export). The read commands (`story get`, `story set`, `story config`, `config set`, `kv get`, `machine create`, …) print their model through `WriteJson`, with a line-number gutter (` 1 │ {`) and colours even when redirected; a `--format` other than json prints its text through `WriteNumberedText`, with the gutter too (and a `#` written as `\u0023`), unless `--content-only` or a file. `JsonNode()` takes the whole plain standard output when it is a document, else the first block of numbered lines (the gutter removed), else the first balanced `{…}` or `[…]`. sform writes PascalCase, so `Json<T>()` matches names without case.

**WriteJson renders at 80 columns** whatever `COLUMNS` says (it renders into a Spectre console over a `StringWriter`, whose width is fixed at 80, and adds the gutter after): a token that does not fit the rest of a line moves to the next one, a string value wraps at its spaces (losing the space), and a token longer than 80 columns (an API key, a unit key of about 85 characters, a long value) is folded into lines of 80 inside its string. `PlainStdOut` joins them again (`WrappedJson`): a numbered block that is no JSON as printed but is JSON with its folds joined is replaced by the joined lines, so `JsonNode()`, `ShouldContain(key)` and the redaction of a printed key work; a run of spaces where a value wrapped comes back as one space. `AnsiText.Plain(result.StdOut)` is the text exactly as printed. For an exact long text value, read a file (`-e`) or `--content-only`. The fix belongs in the CLI (a capture console as wide as the text); then nothing needs joining.

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
| `[Test]`, `[Elementary]`, `[TestCase("<id>")]` | TUnit's test; the PR gate set; the Testiny case (added once the cases exist in `2SC`) |
| `[UserRole(Role.X)]` | The role the test acts as (shared library) |
| `[Area(Area.X)]` | Category `Area-<section>`: `SignIn`, `Account`, `Machines`, `SharedCertificates`, `Annotations`, `Configurations`, `Templates`, `Schemas`, `KeyValues`, `General` |
| `[MachineOnly]` | Mode M: leases no pool user |
| `[FreshSignIn]` | Mode F: the test's user runs use an empty home of its own; the test signs in itself |
| `[Terminal]` | Mode T: skipped where no pseudo terminal is supported |
| `[DisposableProject]` | Mode P: creates its own project (`CreateDisposableProjectAsync`, needs a pseudo terminal) |
| `[RequiresFeature("<Feature>")]` | Mode X: skipped unless `Target:Features` lists it |
| `[RequiresMachine("<name>")]` | Skipped unless `Machines:<name>` is configured with a credential |
| `[Covers("<command path>", "<item>", …)]` | What the test exercises: the command path as typed (an alias in the path covers that alias), and the options (each form), fixed values (`--protection=public`) and arguments (`arg0`) whose effect it checks; repeat it per command. Phase 4's `CommandCoverageTests` reads them |

Categories (`CliCategories`): `MachineOnly`, `FreshSignIn`, `Terminal`, `DisposableProject`, `Requires-<Feature>`, `Machine-<name>`, `SelfTest`, plus the library's `Elementary`, `Serial`, `Area-<section>`.

### Data and secrets

Generated data follows the catalogue's rules: keys and names under the target project, `cli-<runId>` in every name, nothing deleted that a case does not delete. A secret value a test sets (`CliData.SecretValue()`) is registered with `AddSecret` (or `SformRunOptions.Secrets`) **before** the run that carries it, so the attachment and every message mask it; the raw `StdOut`/`StdErr` stay unmasked for the checks (`ShouldNotContain(secret)`, whose own failure message masks it too). A secret the test learns from a run's output is returned by that run's `SformRunOptions.SecretsOf`.

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

Mode E: `UseEditorAsync(customCommand, home?)` writes `editor.config.json` (`{ "editor": { "EditorType": "Custom", "CustomCommand": "<program> [arguments]" } }`) into the home, and `ResetDefaults` removes it before the next test of that home. sform appends the quoted path of the file to edit and splits the command at its first space (`EditorService.ParseCustomCommand`), so the program's path must contain no space. The fake editor helper of spec §4.3 (which applies a change from a file named by a variable, or leaves the file untouched) comes with phase 4:

```csharp
[Test]
[UserRole(Role.Editor)]
public async Task Config_Edit_AppliesTheChange()
{
    var key = CliData.ResponsibilityKey();
    (await Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed();
    await UseEditorAsync("<fake editor of phase 4>");            // placeholder: the helper's command line
    var change = await WriteFileAsync("change.json", "{ \"a\": 2 }");
    (await Sform(new SformRunOptions { Environment = { ["<the helper's variable>"] = change } }, InProject("story", "config", "edit", key, "-y")))
        .ShouldSucceed();                                          // -y saves without "Do you want to save these changes"
    var saved = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
    await Assert.That(saved["a"]!.GetValue<int>()).IsEqualTo(2);
}
```

## The Elementary set

The PR gate set, `--treenode-filter "/**[Category=Elementary]"`, 12 cases: SGN-DEV-01 (device code sign-in), SGN-MSI-01 (machine sign-in), ACC-SET-01 (`account set`), MCH-LIF-03 (a created API key machine reads), ANN-SET-01 and ANN-SET-08 (annotation round trip, Reader refused), CFG-GET-01, CFG-GET-02 and CFG-SET-01 (configuration model, resolved, set), CFG-GHA-01 (`--github-env`), KV-PAR-01 and KV-SEC-01 (a public pair, a secret). They need the pool passwords, `Machines:apikey`, and a Reader who reads `Target:Project` (the Reader cases arrange their data as an Editor there).

## Self tests

`SelfTests/` (category `SelfTest`): plain TUnit tests of the suite's own machinery that need no target and run in every run: the built sform starts (`--version`, `--help`), an unknown command ends with -1, a prompt on the redirected input ends at once with 43, the API of a run is the one given (a closed port is 3), the input is written and closed, the outer `STORYTELLER_*` variables do not leak, the environment is built from scratch, a timeout kills the process tree, a child left running does not hold the run and is killed, the input and output of a .NET child on Windows in the OEM code page (and the decoder of mixed UTF-8 and OEM bytes), the escape sequences are removed, the JSON is found behind the gutter and WriteJson's 80-column folds are joined (the lines Spectre renders for an API key, a unit key, wrapped text), 255 is read as -1 off Windows, the ::add-mask:: values are found, an assertion message masks what it looked for, the environment without SFORM_HOME, the device code is parsed (JSON and text), the printed credentials are found, the command lines are quoted for Windows and the shell, the device page's steps on a stand-in of AuthKit (skipped without Chromium), and `assist key` answered in a pseudo terminal (skipped where none is supported).

## Testiny

Project **`2SC`** (id 2), created by hand in the Testiny app; its cases come from [test-cases.md](test-cases.md) (phase 4), and `[TestCase("<id>")]` links each test to its case. `UITESTS_PROFILE=<target>,reporting-testiny` with `Reporting:Testiny:ApiKey` in the user secrets reports a run as a classic Testiny test run (the [library's Testiny reporter](../../Libraries/Testing/UI.Automation/readme.md#testiny)); the attachments are the redacted `sform-*.txt` runs and the device page's screenshot and text of a failure.

## CI

Phase 5 adds `.github/workflows/cli-automation.yml` (`workflow_dispatch` and `workflow_call`, inputs `profile`, `filter` (default the Elementary set), `target_url`, `report_testiny`; `ubuntu-latest` with the SDK of `global.json` and Playwright's Chromium only; the secrets of a `cli-automation` environment given to the run step only; artifacts `results.jsonl` and the `sform-*.txt` runs; the homes and working directories are outside the artifacts anyway). Until then a CI job runs it like the commands above with `UITESTS_PROFILE=ci` and the `UITESTS_*` variables.

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
