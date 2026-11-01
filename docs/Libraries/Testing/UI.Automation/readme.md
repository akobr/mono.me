# UI Automation

A small framework for browser tests of a deployed web application on **Playwright** (`Microsoft.Playwright`), with the **TUnit** test engine, test case management reporting (**Testiny** first) and browsers on your machine or on **BrowserStack**. It is four packages under `src/Libraries/Testing/UI.Automation.*`; the first consumer is the Platform's admin UI suite, [`src/Platform/UI.Admin.AutomationTests`](../../../Platform/UI.Admin.AutomationTests/automation-tests.md).

What the framework owns: layered settings (`testsettings*.json`, user secrets, `UITESTS_*` variables, `--test-parameter`), browsers and their contexts, a pool of pre-provisioned users with cached sign-ins, seeded test data, artifacts (screenshots, traces, videos, console logs), a check of the target before the first test, retries, and a reporting pipeline with a replayable results file. What a consuming project owns: how its users sign in, the extra settings of its target, its page objects, data generators and tests.

Spec and reviews: [2026-10-31 UI Automation Testing Project](../../../Platform/UI.Admin.AutomationTests/specs/2026-10-31%20UI%20Automation%20Testing%20Project.md) (sections 1–4 and 6 are the libraries); the reviews of Phases 1 and 2 are next to it, in [its `reviews/`](../../../Platform/UI.Admin.AutomationTests/specs/reviews/), the reviews of the libraries' Phases 3 to 5 (Testiny, BrowserStack, the target check and the security fixes) in [`specs/reviews/`](specs/reviews/) here, and the Platform's Phase 5 (the CI workflow) again next to the spec.

## Packages

| Package | Namespace | Depends on | Contains |
|---|---|---|---|
| `42.Testing.UI.Automation.Abstractions` | `_42.Testing.UI.Automation` | `Microsoft.Extensions.Configuration.Abstractions` | `[TestCase]`, the category names, the run, test, result and artifact records, the reporter contract (`ITestRunReporter`, `ITestRunReporterProvider`, `TransientReporterException`) |
| `42.Testing.UI.Automation.Runtime` | `_42.Testing.UI.Automation.Runtime` | Abstractions, `Microsoft.Playwright` 1.63.0, `Microsoft.Extensions.Configuration` (Json, Binder, UserSecrets), `Microsoft.Extensions.Logging.Abstractions`, `Bogus`, `TestableIO.System.IO.Abstractions` | engine-neutral: settings loader and validation, `IUiAutomationSetup`, `AutomationSession`, `TestScope`, the target check, browser providers (Local, BrowserStack), user pool, `ISignInFlow` and the storage-state cache, `SeededData`, artifacts, the reporting dispatcher and `results.jsonl` |
| `42.Testing.UI.Automation.Runtime.TUnit` | `_42.Testing.UI.Automation.Runtime.TUnit` | Runtime, `TUnit.Core` 1.76.45 | the TUnit adapter: `UiTest`, the session hooks (`UiAutomationSession`), `[UiAutomationSetup<T>]`, `[Elementary]`, `[Area]`, `[Serial]`, `[RequiresProvider]`, `[RequiresEngine]`, `[ConfiguredRetry]`, `[ReportResults]`, `[BrowserStackSessionLimit]` |
| `42.Testing.UI.Automation.Reporting.Testiny` | `_42.Testing.UI.Automation.Reporting.Testiny` | Abstractions, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging.Abstractions`, `TestableIO.System.IO.Abstractions` | `TestinyReporterProvider`, `TestinyTestRunReporter`, `TestinyOptions`, `TestinyClient` (API v1) |

```
Abstractions  ←  Runtime  ←  Runtime.TUnit
Abstractions  ←  Reporting.Testiny
Runtime.TUnit + a reporter (+ TUnit, Bogus)  ←  the test project (sign-in flow, target settings, pages, data, tests)
```

All four are `net10.0`, packable, StyleCop-clean, and versioned with the repository (no own `version.json`). The libraries reference `TUnit.Core` only; the `TUnit` package (engine and entry point) belongs to the test project. Each has a short `package-readme.md` in `docs/Libraries/Testing/<project>/` that its NuGet package carries.

## How a run works

1. **Before discovery** (`[Before(TestDiscovery)]` of `UiAutomationSession`, in the library; TUnit runs the static hooks of a referenced library): the setup named by `[assembly: UiAutomationSetup<T>]` is created, the settings load (with the raw `--test-parameter` values and the setup's `Validate`), and `Execution:MaxParallelTests` goes to TUnit, whose `--maximum-parallel-tests` and `TUNIT_MAX_PARALLEL_TESTS` still win. Settings that do not load are logged once and kept: every UI test fails with the message instead of TUnit's discovery failing.
2. **Registration**: `[RequiresProvider]`/`[RequiresEngine]` skip, `[ConfiguredRetry]` sets the retry limit, `[BrowserStackSessionLimit]` installs its limiter, all from the settings loaded in step 1.
3. **Before the first test** (`[Before(TestSession)]`): in a replay (`Reporting:ReplayFile`) the results file goes to the reporters; without `Target:BaseUrl` nothing starts. Otherwise `AutomationSession.StartAsync` runs, with every selected test as planned: it resolves the sign-in flow, builds the user pool, picks the run id (`yyyyMMdd-HHmmss-xxxx`) and the data seed, creates `<ArtifactsPath>/<RunId>/`, starts the browser provider (the Local provider launches its browser now, so missing browsers fail at once), applies the device, sets Playwright's `Expect` timeout, describes the run, creates the reporters (their settings are validated here), logs the effective settings with secrets as `***`, runs the setup's **target check**, and only then starts the reporters (a Testiny run is created) and writes the first line of `results.jsonl`. Any failure stops the start: the browsers close, reporters that started complete, and every UI test fails at once with the message.
4. **Each UI test** (`UiTest`'s `[Before(Test)]`/`[After(Test)]`, delegating to `TestScope`): lease a pool user (`RequiredUser`), acquire a browser, create a context signed in from the cached storage state (the user signs in once per session, in a throw-away untraced context), start tracing, capture the console, page errors and failed requests, open the page; after the body: `CleanUpAsync`, the screenshot, trace and video per the `Execution` policies, the refreshed sign-in back to the cache, `console.log`, close the contexts, release the browser (BrowserStack marks the session), return the users, report the final attempt and attach the artifacts to TUnit's result (HTML and TRX reports).
5. **Tests that do not report themselves** (not derived from `UiTest`, or skipped by an attribute, which runs no hooks) are reported by `[assembly: ReportResults]`.
6. **After the last test** (`[After(TestSession)]`): the dispatcher drains and completes every reporter, the browsers close, the totals and every reporter's run URL go to the terminal (`ui-automation: Testiny run: https://…`), and `results.jsonl` is attached to the session's result. With `Reporting:UnmappedTests` `Fail` and a test without a case, the session fails.

The terminal shows the session's lines with the prefix `ui-automation:` (standard output; standard error under `--list-tests`; nothing under `--server`, where an IDE drives the process).

## Settings

### Layers and profiles

`UiAutomationSettingsLoader.Load` reads, each layer over the one before:

1. `testsettings.json` next to the test assembly (the committed defaults);
2. `testsettings.<profile>.json` for each profile, comma-separated and in order; the profile list comes from `UiAutomationSettingsOptions.Profile`, else `--test-parameter Profile=…`, else `UITESTS_PROFILE`. A named profile whose file is missing fails, also without a target;
3. `testsettings.local.json` (git-ignored overrides for the IDE);
4. the user secrets of the test assembly (its `UserSecretsId`);
5. `UITESTS_*` environment variables, `__` between sections (`UITESTS_Target__BaseUrl`, `UITESTS_Users__Pool__0__Password`);
6. `--test-parameter Section:Key=value` (TUnit cuts a value at its second `=`, so the adapter reads the raw command line: `Target:BaseUrl=https://x/?a=b` arrives whole).

JSON files may have comments and trailing commas; a file that is not a JSON object fails. The test project copies the files to its output (`<None Update="testsettings*.json" CopyToOutputDirectory="PreserveNewest" />`).

**Skip mode**: without `Target:BaseUrl` nothing else is validated, no browser starts and every UI test is skipped with `UITESTS_Target__BaseUrl is not configured; see <IUiAutomationSetup.ReadmeHint>.`; an all-skipped run exits `0`.

**Validation** (with a target): every error is collected and thrown together, naming the key and, for a secret, where to set it. After it the runtime lowers `Execution:MaxParallelTests` to the pool size (a warning) and warns when `MaxParallelTests × 2` exceeds the pool (a test that leases a second user may wait).

**Secrets**: a key is a secret when a segment contains `Password`, `AccessKey`, `ApiKey`, `Secret` or `Token` (case-insensitive); the logged settings, run descriptions and reports show it as `***` (an empty one stays empty, so a missing password is visible). Secrets never go into a committed file: user secrets locally, `UITESTS_*` variables in CI. The values of the secrets are redacted too, as is, trimmed, URL-encoded and JSON-escaped (`SettingsRedaction.CreateRedactor`): Playwright's call log of a fill or a type keeps the typed value, so the failure message, stack trace and output of every report (`AutomationSession.CreateReport`, so `results.jsonl`, every reporter and BrowserStack's session reason), the message of a failed target check and every line of the TUnit adapter's `ui-automation:` log show them as `***`; the reports also show the pool users' e-mails as `***`. TUnit's own TRX and HTML reports show the exception of a test body as thrown.

A consuming project reads its own keys with `settings.Bind<T>("Section")` (cached per type and section), e.g. a `Target` extension that derives from `TargetSettings`.

### `Target`

| Key | Default | Meaning |
|---|---|---|
| `BaseUrl` | empty | The application, an absolute `http`/`https` URL and the `BaseURL` of every context (relative paths stay below it); empty is skip mode |
| `SignIn` | unset | The `Name` of a registered `ISignInFlow`, `None` (a target that is signed in already, or a public site), or unset for the first registered flow (`None` without one). Passwords are required only when the flow says so |

### `Browser`

| Key | Default | Meaning |
|---|---|---|
| `Provider` | `Local` | `Local` (the installed Playwright's browsers) or `BrowserStack` |
| `Engine` | `chromium` | `chromium`, `firefox` or `webkit`, for the Local provider; BrowserStack derives the engine from `BrowserStack:Browser` |
| `Channel` | `null` | A branded Chromium of the Local provider (`chrome`, `msedge`, `chrome-beta`, …); Chromium only |
| `Headless` | `true` | |
| `SlowMoMs` | `0` | Playwright's slow motion per action |
| `Device` | `null` | A Playwright device descriptor (`Pixel 7`, `iPhone 15`, …), mobile emulation; wins over `Viewport`. Firefox does not emulate `isMobile` (a warning; viewport, user agent and touch stay) |
| `Viewport` | `1440` × `900` | `Width` and `Height` without a device |
| `Locale` | `en-US` | `null` for the browser's own |
| `TimeZone` | `UTC` | An IANA time zone; `null` for the machine's |
| `ColorScheme` | `light` | Playwright's `ColorScheme`: `light`, `dark` or `noPreference` |

HTTPS errors are ignored for a `localhost` target only.

### `Execution`

| Key | Default | Meaning |
|---|---|---|
| `MaxParallelTests` | `4` | Handed to TUnit before discovery; lowered to the pool size; TUnit's `--maximum-parallel-tests` and `TUNIT_MAX_PARALLEL_TESTS` win |
| `Seed` | `null` | The session seed of the generated data; random and logged when unset, so `--test-parameter Execution:Seed=<n>` repeats a run's data |
| `ActionTimeoutMs` | `10000` | Default timeout of a Playwright action in the test's contexts |
| `NavigationTimeoutMs` | `30000` | Default navigation timeout |
| `ExpectTimeoutMs` | `10000` | Playwright's `Expect` timeout, set once per process |
| `Retries` | `0` | How often a failed test runs again (0–10); see "Retries" |
| `Trace` / `Screenshot` / `Video` | `OnFailure` / `OnFailure` / `Off` | `Off`, `OnFailure` (kept for a test that did not pass or skip) or `Always` |
| `ArtifactsPath` | `TestResults/artifacts` | Relative to the test assembly's directory (or absolute); the run writes to `<ArtifactsPath>/<RunId>/` |

### `Users`

| Key | Default | Meaning |
|---|---|---|
| `LeaseTimeoutSeconds` | `120` | How long a lease waits for a free user before the test is **Blocked** (`No free user with role X …`) |
| `Pool` | `[]` | The pre-provisioned users: `Email` (required, unique), `Password` (a secret; required when the sign-in flow needs one), `DisplayName`, `Roles` (a list, matched by a test's `UserRequirement`) |

A user is never held by two running tests; `TestUser.ToString()` and every report show only `Users:Pool:<n> (u-<hash of the e-mail>)`, never the e-mail or the password (both are `***` in a failure's message and stack trace too).

### `BrowserStack`

Used when `Browser:Provider` is `BrowserStack`; see "Browser providers".

| Key | Default | Meaning |
|---|---|---|
| `Username` / `AccessKey` | empty | The account (secrets); required with the provider |
| `Endpoint` | `wss://cdp.browserstack.com/playwright` | The Playwright endpoint; the capabilities go into its `caps` parameter. Any other `ws://`/`wss://` Playwright server works too and ignores them |
| `Project` | `null` | The dashboard's project |
| `Build` | `null` | The dashboard's build, a pattern with the placeholders of `Reporting:RunName`; unset: `{Branch} #{BuildNumber}` in a GitHub Actions job, `local {Machine} {Date}` elsewhere |
| `BuildTag` | `null` | A tag of the build, e.g. `nightly` |
| `Os` / `OsVersion` | `Windows` / `11` | `Windows` with `10` or `11`; `OS X` with a macOS name (`Tahoe`, `Sequoia`, …) |
| `Browser` | `chrome` | `chrome`, `edge` (branded), `playwright-chromium`, `playwright-firefox`, `playwright-webkit` (Playwright's builds); the engine follows from it |
| `BrowserVersion` | `latest` | `chrome` and `edge` only: `latest`, `latest-1`, `latest-beta` or a version |
| `Resolution` | `1920x1080` | The remote screen; the page's viewport is still `Browser:Viewport` or the device |
| `Local` / `LocalIdentifier` | `false` / `null` | Route through a running BrowserStack Local (with that identifier); the provider does not start it |
| `PlaywrightVersion` | `null` | `browserstack.playwrightVersion` (`1.latest` or a version); unset it is not sent and BrowserStack matches `client.playwrightVersion`, which is always sent |
| `SessionPerTest` | `true` | A session per test, named `<Class>.<Method>`; `false`: one per test class, named `<Class>` |
| `Console` | `errors` | `disable`, `errors`, `warnings`, `info` or `verbose` |
| `NetworkLogs` / `Debug` / `Video` | `false` / `false` / `true` | BrowserStack's network log, a screenshot per command, the session's video |
| `MaxParallelSessions` | `5` | The plan's parallel sessions; `[BrowserStackSessionLimit]` limits the running tests to it |
| `ConnectTimeoutSeconds` | `300` | How long a connection may take, queueing at BrowserStack included (the TUnit adapter raises its hook timeout so this one ends first; see "Tests: `UiTest`") |
| `MaskCommands` | `sendType, sendPress, setHTTPCredentials, setStorageState` | `browserstack.maskCommands`: the Playwright commands whose values BrowserStack's text and raw logs show as `REDACTED VALUE` (also `setGeolocation`, `setWebAuthnCredentials`); empty masks none. BrowserStack cannot mask a `fill`, so a sign-in flow types the password (`PressSequentiallyAsync`) |
| `PlaywrightLogs` | `false` | `browserstack.playwrightLogs`: BrowserStack's Playwright logs, which it never masks |
| `AdditionalCapabilities` | `{}` | More capabilities as strings, e.g. `browserstack.idleTimeout`, `browserstack.geoLocation`; one of the same name replaces a capability of the keys above (e.g. `browserstack.maskCommands`); not the credentials |

### `Reporting`

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | The master switch: `false` creates no reporter and leaves `[TestCase]` inert; `results.jsonl` is written anyway |
| `Systems` | `[]` | The systems to report to; empty means every registered provider whose `Reporting:<System>` section exists. Selecting nothing, or naming an unregistered system, fails the start |
| `RunName` | `UI automation {Date:yyyy-MM-dd} {Time:HH:mm} {Profile}` | A pattern with `{RunId}`, `{Date}` / `{Date:<format>}`, `{Time}` / `{Time:<format>}`, `{Profile}`, `{Provider}`, `{Engine}`, `{Device}`, `{Branch}`, `{Sha7}`, `{BuildNumber}`, `{Machine}`; `{{` and `}}` are braces; an unknown value is empty and its spaces collapse |
| `RunDescription` | `null` | Markdown with the same placeholders; unset: a table of the run (target, profile, provider, engine, channel, browser version, OS, device, seed, branch, commit, build, machine, versions, run id) |
| `UnmappedTests` | `Ignore` | Tests without a `[TestCase]` of a reporter: `Ignore`, `Warn` (a run warning; the Testiny reporter also checks the ids), `Fail` (a warning and a failed session) |
| `BatchSize` / `FlushIntervalSeconds` | `20` / `5` | Results go to a reporter in batches of this size, or of what arrived within the interval |
| `MaxAttempts` | `5` | Attempts of a call that failed transiently; the back-off doubles from 1 s up to 60 s, at least the `Retry-After` the system sent |
| `CompleteTimeoutSeconds` | `120` | Bounds a reporter's start, the drain at the end and its completion, each on its own |
| `ReplayFile` | `null` | A `results.jsonl` of an earlier run: it goes through the reporters instead of running tests (needs `Enabled`) |
| `<System>` | | The section a reporter's provider binds, e.g. `Reporting:Testiny` |

### `Reporting:Testiny`

| Key | Default | Meaning |
|---|---|---|
| `BaseUrl` | `https://app.testiny.io/api/v1/` | The API root |
| `ApiKey` | empty | Sent as `X-Api-Key` (a secret, required); best restricted to the project |
| `ProjectId` | `0` | The numeric Testiny project id (required) |
| `TestPlanId` | `null` | A test plan the run is added to; Testiny refusing it is a warning and the run goes on outside the plan |
| `Mode` | `TestRun` | `Automation` (Testiny's automation API, spec §6.6) is not implemented and refused |
| `PlanCasesAsNotRun` | `true` | Every linked case of the planned tests goes into the run as `NOTRUN` at the start |
| `CloseRunOnComplete` | `true` | |
| `Attachments` / `Comments` | `Failures` / `Failures` | `None`, `Failures` (every outcome but passed and skipped) or `All` |
| `MaxAttachmentBytes` | `26214400` (25 MB) | Larger files are listed in the comment instead |
| `ResultForSkipped` / `ResultForBlocked` | `SKIPPED` / `BLOCKED` | The result status of a skipped and a blocked test (a custom status works too) |
| `RequestTimeoutSeconds` | `30` | Per request; a timeout is retried like a `429` or a `5xx` |
| `MinUploadBytesPerSecond` | `262144` (256 KiB/s) | The slowest upload expected: an upload may take its size at this rate when that is longer than `RequestTimeoutSeconds` |
| `RunUrlTemplate` | `{AppUrl}/{ProjectId}/testruns/{RunId}` | The browser link of the run printed at the end; `{AppUrl}` is `BaseUrl` without its `/api/…` path. **Not verified** against Testiny's app: set the form its "Copy link to test run" shows |

## The setup of a project: `IUiAutomationSetup`

One class in the test assembly, named by `[assembly: UiAutomationSetup<T>]` (or `UiAutomationSetup(typeof(T))`) and created with its parameterless constructor:

| Member | Default | Meaning |
|---|---|---|
| `ReadmeHint` | (required) | Where the project documents its settings; ends the skip reason |
| `SignInFlows` | `[]` | The flows `Target:SignIn` may name; the first is the default |
| `ReporterProviders` | `[]` | The reporters `Reporting:Systems` may select (no reflection scanning) |
| `Services` | `null` | Handed to the reporter providers (e.g. an `IHttpClientFactory`); the runtime adds the run's `IConfiguration` and, unless given, an `ILoggerFactory` |
| `Validate(settings, errors)` | nothing | Checks the project's own settings once they load with a target; each message fails the load |
| `CheckTargetAsync(context, ct)` | none | The target check (below) |

### The target check

`CheckTargetAsync(TargetCheckContext context, CancellationToken)` checks once per run, before the first test, that the target can run the tests: say the pre-provisioned data exists and the users may open it. `TargetCheckContext` gives the `Session`, the `Settings`, a leased pool `User` (any role) and a `Page` of a `Context` signed in as that user through the cached sign-ins (so the user's first test signs in no more), and the `Scope` for more (`Scope.NewContextAsync`, `Scope.LeaseAnotherUserAsync`).

- It runs at the end of the session's start, after the reporters are created and **before** any of them creates a run, in a scope of its own (`TargetCheckContext.Test`): its screenshot and trace follow `Execution:Screenshot` and `Execution:Trace` and land in `<run directory>/TargetCheck/CheckTarget/`; it is not reported; with BrowserStack it is one more session (`TargetCheck.CheckTarget`).
- Throw to refuse the target, best a `TargetCheckException` whose message says what is wrong and what to fix. Any exception becomes the start failure `The target check failed: <message> The check's failure.png, trace.zip, console.log are in <directory>.` (another exception type is named in front of its message): logged once, no reporter run is created, and every UI test fails at once with `UI automation is not available: The target check failed: …` (a `UiAutomationUnavailableException`, which `[ConfiguredRetry]` never retries).
- A setup that does not implement it gets no check and no extra browser context (the runtime looks at the setup type's interface map; an explicit implementation counts).

## Browser providers

**Local** (`Browser:Provider` `Local`): one `IPlaywright` and one browser per session, launched at start (so missing browsers fail before any test, with the install command), shared by every test; each test gets its own context. Install the browsers of the pinned Playwright with the `playwright.ps1` the build puts next to the test assembly: `pwsh <bin>/playwright.ps1 install chromium firefox webkit` (`install --with-deps` on Linux).

**BrowserStack** (`Browser:Provider` `BrowserStack`): the installed Playwright client connects to `BrowserStack:Endpoint` with the session's capabilities (credentials, platform, `project`, `build`, `buildTag`, `name`, logs, local testing, `client.playwrightVersion`); BrowserStack's own SDK is not used.

- A session per test (`SessionPerTest` `true`): connected when the test acquires its browser, marked **passed** or **failed** when it releases it (through a page of the session, `browserstack_executor` `setSessionStatus`, with the failure's message as the reason: one line, at most 255 characters, the access key redacted), then closed. A skipped test or a release without a result leaves it unmarked; Timeout, Blocked and Cancelled are failed.
- A session per class (`false`): shared by the tests of a class; failed when one of its tests failed in its last attempt. It closes once no test holds it and every planned test of the class is done (its final report is known: it ran, or ended without a browser); an attempt that runs again is not done, so its retry finds the session open. One that no test holds while tests of its class are still to come (a retry, a `[Serial]` test) closes after two thirds of BrowserStack's idle timeout (`browserstack.idleTimeout`, 90 s by default) and the next test opens another.
- Nothing connects at start, so wrong credentials show on the first test (blocked, with the message; BrowserStack answers `Invalid username or password`). The access key is `***` in the settings and in every message of the provider, also when it was set with surrounding whitespace (the capabilities send it trimmed); Playwright's own `DEBUG=pw:*` log may print the `caps` URL, so keep it off with real credentials.
- The sign-in and every signed-in context run in BrowserStack's browser: by default the sessions mask typed values and the storage state a context starts from in BrowserStack's logs (`MaskCommands`) and send no Playwright logs (`PlaywrightLogs`); check a first session's text logs.
- `[assembly: BrowserStackSessionLimit]` limits the tests that run at once to `MaxParallelSessions`, on top of `Execution:MaxParallelTests` and the pool.
- **The Playwright version**: a Playwright server refuses a client of another major and minor version, and BrowserStack runs only the versions of [its table](https://www.browserstack.com/docs/automate/playwright/browsers-and-os). `Microsoft.Playwright` is therefore pinned in `Directory.Packages.props` to **1.63.0**, the newest BrowserStack lists; raise it only when the table does, together with the CI image (`mcr.microsoft.com/playwright/dotnet:v<version>-noble`) and the installed browsers. A connection refused for its version fails with a message that names the client version and the table.
- Mobile is **emulated** (`Browser:Device`) in BrowserStack's desktop browsers exactly as locally; real devices are out of scope. A `localhost` target needs BrowserStack Local.
- Without an account, check the provider against a local Playwright server of the same version, which speaks the protocol and ignores the capabilities: `npx -y playwright@1.63.0 run-server --port 3999`, then `Browser:Provider=BrowserStack`, `BrowserStack:Endpoint=ws://localhost:3999/` and any `Username`/`AccessKey`.

A project can plug in another provider through `AutomationSessionOptions.BrowserProviderFactory` (an `IBrowserProvider`: `AcquireAsync`, `ReleaseAsync` with the test's result, `TestCompleted`, `MaxParallelSessions`, `Devices`, `Properties`).

## Tests: `UiTest`

Derive the tests (or a project base class) from `UiTest`:

| Member | Meaning |
|---|---|
| `Page`, `Context` | The test's page and its context (BaseURL `Target:BaseUrl`, the `Execution` timeouts) |
| `User` | The leased pool user |
| `Settings`, `Session`, `Scope` | The run's settings, the `AutomationSession`, the test's `TestScope` (`Properties` go into the report, `NewContextAsync`, `ExtraUsers`, `Artifacts`) |
| `Data` | `SeededData` of this attempt: `Slug(prefix, words)`, `Word()`, `Sentence(max)`, `Hex(n)`, Bogus' `Faker` and `Random`, seeded from the session seed, the test id and the attempt |
| `RequiredUser` (virtual) | `UserRequirement.Any` by default; `UserRequirement.WithRole("Owner")`, `WithAnyRole(…)` |
| `SignedIn` (virtual) | `true`: the context starts signed in; `false` for a test that signs in itself on its page (that page is traced, and a trace keeps what was typed: turn `Execution:Trace` off when it types a real password) |
| `LeasesUser` (virtual) | `false` for a test that needs no user at all (a public page; not signed in either) |
| `CleanUpAsync()` (virtual) | The test's cleanup on the still open page (e.g. restoring a setting); its failure fails a passing attempt, is reported and decides a retry. Use it instead of an own `[After(Test)]` hook, which TUnit runs with the result still Passed |
| `LeaseAnotherUserAsync(requirement)` | A second user for the test (e.g. one who accepts an invitation), returned at the end; `Scope.NewContextAsync(user)` signs a context in as that user |

A lease that times out and a browser that is not available make the test **Blocked**, not Failed. The start of a test (the lease, the browser, the user's first sign-in) runs in the `Before(Test)` hook, and the session's start (the target check) in `Before(TestSession)`; TUnit ends a hook after its hook timeout (5 minutes), so the adapter raises that timeout to the settings' `StartBudget` when it is longer: `Users:LeaseTimeoutSeconds`, plus `BrowserStack:ConnectTimeoutSeconds` with BrowserStack, plus 5 × `Execution:NavigationTimeoutMs`, plus a minute (10.5 minutes with the defaults and BrowserStack). A start that a hook timeout still ends (`TestScopeOptions.TestCancellation` tells it from a stopped run) is Blocked by the step that did not finish, not cancelled: `UserLeaseException` or `BrowserUnavailableException` with `… before the engine's time for the start of the test ran out`. TUnit creates an instance per attempt, so everything on the instance belongs to one attempt. Test ids are `<Assembly>/<Class>/<Method>`, plus the display name for a data-driven case and `#<hash>` for a test that runs as several instances (class or method arguments, `[Repeat]`); they key the report claims, the data seeds and the artifact folders.

### Sign-in

`ISignInFlow` is the project's sign-in on the application's real page: `Name` (what `Target:SignIn` names), `RequiresPassword` (default `true`), `SignInAsync(page, user, ct)` (starts on a blank page of a context whose BaseURL is the target; returns once the app shows the user signed in), `IsSignedOutAsync(page, ct)` (asked at the end of every signed-in test; `true` drops the user's cached state) and `IsSignInStorageItem(origin, name)` (local storage items of the sign-in, e.g. a rotating refresh token, carried over to the user's next test with every cookie; the app's other items stay as captured right after the sign-in). The `StorageStateCache` signs each user in once per session (also when two tests ask at once), in a throw-away untraced context without video or storage state (so no trace holds a password), keeps the state in memory only, and refreshes it from every test that ended signed in (one that ends signed out, or whose refresh fails, drops it, and the user's next test signs in again). A failed sign-in throws a `SignInException` with the type and the message of the flow's exception, the user's e-mail and password as `***`, and no inner exception (Playwright's call log keeps what a fill or a type entered), and keeps `sign-in-failure.png`. A trace still holds the context's cookies and tokens: keep traces where the test results are private.

### Artifacts

`<ArtifactsPath>/<RunId>/<class below the assembly's namespace>/<method>[/attempt-n]/`: `failure.png` (or `final.png` with `Screenshot` `Always`), `trace.zip` (open with `playwright.ps1 show-trace`), `video.webm`, `console.log` (console messages, page errors, failed requests); `sign-in-failure.png` when a sign-in fails. A data-driven case or a repetition uses its display name and a hash of its id. The run directory also holds `results.jsonl`. The files are attached to TUnit's result, so they appear in its HTML and TRX reports.

## Attributes and filters

| Attribute | Where | Effect |
|---|---|---|
| `[assembly: UiAutomationSetup<T>]` | assembly | Names the setup (required for sign-in flows, reporters, validation, the target check) |
| `[assembly: ReportResults]` | assembly | Reports the tests that do not report themselves (plain tests, attribute skips) |
| `[assembly: ConfiguredRetry]` | assembly | Retries per `Execution:Retries`; replaces any `[Retry(n)]` |
| `[assembly: BrowserStackSessionLimit]` | assembly | Limits running tests to `BrowserStack:MaxParallelSessions` with the BrowserStack provider |
| `[Elementary]` | test, class | Category `Elementary`: the small, fast set that gates a pull request |
| `[Area("Annotations")]` | test, class | Category `Area-Annotations`; not sealed, so a project wraps an enum of its areas |
| `[Serial]` (`Order = n`) | test, class | Runs alone, after every parallel test, in `Order`; adds the category `Serial` |
| `[RequiresProvider(BrowserProviderKind.BrowserStack)]` | test, class | Skipped unless the run uses that provider |
| `[RequiresEngine(BrowserEngine.Chromium, …)]` | test, class | Skipped unless the browsers run one of the engines |
| `[TestCase(1234)]` (`System = "Testiny"`) | test, class | Links the test to a case of the test case management system; several per test, several tests per case |

Select with TUnit's `--treenode-filter` (not `--filter`), one `[...]` per path segment:

```bash
dotnet run --project <test project> -- --treenode-filter "/**[Category=Elementary]"
dotnet run --project <test project> -- --treenode-filter "/**[Category=Area-Annotations]"
dotnet run --project <test project> -- --treenode-filter "/**[Category!=Serial]"      # leave the serial tests out
dotnet run --project <test project> -- --treenode-filter "/*/*/SignInTests/*"         # a class
```

Exit codes (Microsoft.Testing.Platform): `0` passed (also when every test was skipped), `2` a test failed, `5` an invalid command line, `8` no test matched the filter (`--ignore-exit-code 8` where that is fine).

### Serial

`[Serial]` puts a test into one parallel group with a keyed not-in-parallel constraint (`ui-automation-serial`): TUnit runs that group after every unconstrained test, one test at a time, sorted by `Order`. A keyed `[NotInParallel]` alone would run interleaved with the parallel tests. A serial test restores what it changes (`CleanUpAsync`) and never depends on another test's order; the `Serial` category also allows two invocations (`[Category!=Serial]`, then `[Category=Serial]`).

### Retries

`[assembly: ConfiguredRetry]` runs a failed test again up to `Execution:Retries` times, never for a skip, a `DataConflictException` (the runtime's signal that a test met data it did not expect, so idempotency bugs surface) or a run whose session is not available (`UiAutomationUnavailableException`: the settings did not load, the start or the target check failed). Each attempt is a new instance with its own scope, user lease, data seed and artifact folder (`attempt-2/`, …); only the last attempt is reported, and the reports count it (`Attempt` in the report).

## Reporting

### The contract

A reporter (`ITestRunReporter`, one instance per run) is created by its `ITestRunReporterProvider` (`System`, `Create(services, section, run)`, which validates its `Reporting:<System>` section and throws with the setting at fault). The dispatcher calls it from a background consumer, never from a test: `StartAsync(run, plannedTests)` once, `ReportAsync(batch)` with batches, `CompleteAsync(summary)` once, then `DisposeAsync`; `RunUrl` once known. Every exception is caught, logged as a run warning and never reaches a test; a `TransientReporterException` (throttling, `5xx`, a timeout; `RetryAfter` optional) is retried with back-off, and so are an `HttpRequestException` with `429`/`5xx`/no status, a `TimeoutException`, an unrequested cancellation and an `IOException`. `TestIdentity.Cases` holds only the links that apply to the reporter's `System` (a link without `System` applies to every reporter).

The records: `TestRunDescriptor` (run id, name, description, start, properties: profile, target, provider, engine, channel, browser version, OS, device, headless, seed, branch, sha, build number and URL, machine, test engine, Playwright version), `TestIdentity` (id, display name, class, method, assembly, categories, case links), `TestResultReport` (outcome `Passed`/`Failed`/`Skipped`/`Blocked`/`Timeout`/`Cancelled`, start, duration, attempt, `TestFailure` with message, exception type, stack trace and source line, output, `TestArtifact`s, properties such as the user's fingerprint and the remote session), `TestRunSummary` (totals, warnings).

### The dispatcher and `results.jsonl`

- One channel and consumer per reporter; results in batches (`BatchSize`, `FlushIntervalSeconds`); a reporter whose start failed gets nothing more, and the run's other reporters go on.
- Every report is also appended to `<run directory>/results.jsonl`: the first line is the run with the planned tests, each further line a result. A cut last line is skipped with a warning.
- The end drains the queues within `CompleteTimeoutSeconds`; what was not sent is a warning that names the replay command. A start that fails before the reporters started creates no run anywhere.
- **Replay**: `--test-parameter Reporting:ReplayFile=<path of results.jsonl>` (relative to the test assembly's directory, like `ArtifactsPath`) with reporting on sends the file through the reporters as a new run instead of running tests (every UI test is skipped with the reason; the run must select at least one test, because TUnit runs no session hook for zero tests). Artifact paths in the file are absolute, so a replay on another machine lists them as gone.

### Testiny

`42.Testing.UI.Automation.Reporting.Testiny` reports a run as a classic Testiny **test run** whose test cases are the `[TestCase]` ids (`"1234"`, or `"TC-1234"` whose digits are taken):

- **Start**: the run (title `Reporting:RunName`, the description under Testiny's `<!--markdown-->` header), added to `TestPlanId`; with `Reporting:UnmappedTests` `Warn` or `Fail` the ids are checked once (`POST /testcase/find`; unknown ids are warnings and left out); every linked case planned as `NOTRUN`, 200 per request.
- **Results**: each linked case gets the worst result over the tests that link it (`FAILED` > `BLOCKED` > `SKIPPED` > `PASSED`; a retried test counts its last attempt); per `Attachments` the artifacts are uploaded to each linked case (`MaxAttachmentBytes`; an upload that fails transiently twice is listed in the comment instead); per `Comments` one markdown comment per test and linked case: test, outcome, **duration** (Testiny has no duration field), attempt, browser, OS, seed, remote session, run, build, artifacts, exception and stack trace.
- **Complete**: the totals, the cases by status and the run's warnings go into the description, then the run is closed. A run whose reporting stopped early is still closed, with why in its description.
- Every step remembers what Testiny accepted, so a retried call repeats only what is missing; Testiny refusing one case, upload, comment, the plan or the id check is a warning and the rest goes on. Rate limits (per account: writes 270 / 6 min, bulk writes 960 / 12 min, uploads 3200 / 30 min) are met by batching and back-off; a long throttle can outlast `MaxAttempts`, and the batch can then be replayed.
- With an `IHttpClientFactory` in the setup's `Services` the provider uses its client named `Testiny`; otherwise it creates its own.

### Add a reporter

1. A library `42.Testing.UI.Automation.Reporting.<System>` that references the Abstractions only.
2. An options class bound from `Reporting:<System>` with a `Validate` that names the wrong key; secrets only through user secrets or `UITESTS_Reporting__<System>__<Key>`.
3. `ITestRunReporter`: create the external run in `StartAsync` (set `RunUrl`), map each batch in `ReportAsync`, aggregate per case yourself when several tests share one, finish in `CompleteAsync`, and close a run you created but never completed in `DisposeAsync`. Throw `TransientReporterException` (with `RetryAfter` when the system sends one) for what may go away; anything else becomes a warning. Make a retried call idempotent: remember what the system accepted.
4. `ITestRunReporterProvider` with `System = "<System>"`, binding and validating the section in `Create`.
5. The consumer registers it in `IUiAutomationSetup.ReporterProviders`; `Reporting:Enabled` and `Reporting:Systems` select it, and `[TestCase("…", System = "<System>")]` restricts a link to it.

Test it against a fake `HttpMessageHandler`, as `UI.Automation.Reporting.Testiny/test` does, never against the live system.

## Adopt the framework in another project

1. **The project**: a TUnit executable that references `UI.Automation.Runtime.TUnit` and a reporter, plus `TUnit` (engine and entry point) and, for data, `Bogus`:

   ```xml
   <PropertyGroup>
     <OutputType>Exe</OutputType>
     <IsPackable>false</IsPackable>
     <IsTestProject>true</IsTestProject>
     <UserSecretsId>…a new guid…</UserSecretsId>
   </PropertyGroup>
   <ItemGroup>
     <PackageReference Include="TUnit" />
     <PackageReference Include="Bogus" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="…\Libraries\Testing\UI.Automation.Runtime.TUnit\src\UI.Automation.Runtime.TUnit.csproj" />
     <ProjectReference Include="…\Libraries\Testing\UI.Automation.Reporting.Testiny\src\UI.Automation.Reporting.Testiny.csproj" />
   </ItemGroup>
   <ItemGroup>
     <None Update="testsettings*.json" CopyToOutputDirectory="PreserveNewest" />
   </ItemGroup>
   ```

   No `Microsoft.NET.Test.Sdk`, `xunit` or `coverlet`. Git-ignore `testsettings.local.json` and `TestResults/`. In this repository, add `<ProjectReference Update="**\<Project>.csproj" Test="false" />` to every traversal (`Directory.Build.proj`) whose glob includes it: TUnit on Microsoft.Testing.Platform refuses the VSTest-mode `dotnet test` of the .NET 10 SDK, so the traversal builds the project and `dotnet run` runs it.
2. **The setup and the assembly attributes** (one file, no session class: the library's hooks run):

   ```csharp
   [assembly: UiAutomationSetup<ShopAutomationSetup>]
   [assembly: ReportResults]
   [assembly: ConfiguredRetry]
   [assembly: BrowserStackSessionLimit]

   public sealed class ShopAutomationSetup : IUiAutomationSetup
   {
       public string ReadmeHint => "src/Shop/UI.AutomationTests/README.md";
       public IReadOnlyList<ISignInFlow> SignInFlows { get; } = [new ShopSignIn()];
       public IReadOnlyList<ITestRunReporterProvider> ReporterProviders { get; } = [TestinyReporterProvider.Instance];

       public void Validate(UiAutomationSettings settings, ICollection<string> errors)
       {
           if (string.IsNullOrWhiteSpace(settings.Bind<ShopTarget>("Target").Store))
           {
               errors.Add("Target:Store is required.");
           }
       }

       // Optional: once per run, before the first test.
       public async Task CheckTargetAsync(TargetCheckContext context, CancellationToken cancellationToken)
       {
           var store = context.Settings.Bind<ShopTarget>("Target").Store;
           await context.Page.GotoAsync($"stores/{store}");
           if (await context.Page.GetByRole(AriaRole.Heading, new() { Name = store }).CountAsync() == 0)
           {
               throw new TargetCheckException($"Target:Store '{store}' was not found for {context.User}.");
           }
       }
   }
   ```
3. **The sign-in**: an `ISignInFlow` that drives the application's real sign-in page (no bypass in the application), or none with `Target:SignIn` `None`.
4. **The target extension**: `public sealed class ShopTarget : TargetSettings { public string Store { get; set; } = ""; }`, read with `Settings.Bind<ShopTarget>("Target")`, its defaults in `testsettings.json`.
5. **The tests**: a base class over `UiTest` with the project's page objects; tests with `[Test]`, `[Elementary]`, `[Area(…)]`, `[TestCase(…)]`; data from `Data` (or a project generator on top of it), nothing shared between tests.
6. **Run** it with `dotnet run --project <path>` (options after `--`): `testsettings.json` without a `Target:BaseUrl` skips every test; a `testsettings.local.json`, user secrets (`dotnet user-secrets set "Users:Pool:0:Password" "…"`) and `UITESTS_*` variables configure a real run.

## Running

```bash
dotnet run --project <test project>                                                   # every test
dotnet run --project <test project> -- --treenode-filter "/**[Category=Elementary]"   # a category
UITESTS_PROFILE=mobile,webkit dotnet run --project <test project>                     # profiles, in order
dotnet run --project <test project> -- --test-parameter Browser:Headless=false --test-parameter Browser:SlowMoMs=200 --maximum-parallel-tests 1
dotnet run --project <test project> -- --report-trx --results-directory <dir>         # TRX besides TUnit's HTML report
dotnet run --project <test project> -- --list-tests
```

- In GitHub Actions TUnit writes its summary into the job's step summary (`GITHUB_STEP_SUMMARY`); the run URLs are on the terminal as `ui-automation: <System> run: <url>`.
- IDE: Visual Studio "Use testing platform server mode", Rider "Enable Testing Platform support", VS Code C# Dev Kit "Use Testing Platform Protocol".
- `dotnet test` does not run a TUnit project in this repository (VSTest mode for the xUnit projects; see step 1 of the recipe).

## Limits

- A run in which every selected test is skipped by an attribute starts no session (TUnit runs `Before(TestSession)` only for a test that executes), so no `results.jsonl` is written.
- Tests that do not derive from `UiTest` are not skipped in skip mode; check `UiAutomationSession.LoadedSettings` yourself where that matters.
- The target check leases one user: a missing membership of another pool user shows in that user's tests.
- Testiny's automation API (`Mode` `Automation`) and real mobile devices on BrowserStack are not implemented.
