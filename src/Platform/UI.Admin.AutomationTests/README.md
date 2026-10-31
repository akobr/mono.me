# Admin UI automation tests

Browser tests of the integrated 2S Platform: the deployed admin UI (`src/Platform/ui.admin`), the real Storyteller API and the AuthKit sign-in, driven by **Playwright** under **TUnit**. The project is built on the reusable libraries `42.Testing.UI.Automation.*` (`src/Libraries/Testing/UI.Automation.*`): settings, browsers, the user pool, seeded data, artifacts and the reporting pipeline live there, the admin UI's sign-in, page objects, data generators and tests live here.

- **Spec:** [UI Automation Testing Project](../../../docs/Platform/UI.Admin.AutomationTests/specs/2026-10-31%20UI%20Automation%20Testing%20Project.md).
- **Not `dotnet test`:** TUnit runs on Microsoft.Testing.Platform 2.5, which refuses the VSTest mode of `dotnet test` on the .NET 10 SDK, and the repository stays in VSTest mode for its xUnit projects. `dotnet build src` builds this project; `dotnet test src` leaves it out (`Test="false"` in `src/Directory.Build.proj` and `src/Platform/Directory.Build.proj`). Run it with `dotnet run`, options after `--`.
- **Without a target** (`Target:BaseUrl` empty, the committed default) every test is skipped with a reason and the run exits `0`.

## Run

```bash
# once: build, then install the browsers of the Playwright version the project uses
dotnet build src/Platform/UI.Admin.AutomationTests/src
pwsh src/Platform/UI.Admin.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install chromium firefox webkit   # Linux: install --with-deps

dotnet run --project src/Platform/UI.Admin.AutomationTests/src                                              # everything, headless Chromium
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"   # the PR gate set
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Area-Access]"
UITESTS_PROFILE=mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src                       # Pixel 7 emulation
UITESTS_PROFILE=webkit,mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src                # profiles combine, in order
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --test-parameter Execution:Seed=1383911092   # repeat a run's data
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --maximum-parallel-tests 1 --test-parameter Browser:Headless=false --test-parameter Browser:SlowMoMs=200
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --report-trx                             # also a TRX file
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --list-tests
```

- A filter that matches no test exits `8` (Microsoft.Testing.Platform's "zero tests ran"); add `--ignore-exit-code 8` where that is fine.
- The terminal shows lines prefixed `ui-automation:`: the run id, the data seed, the effective settings (secrets as `***`), warnings, and at the end the totals and the path of `results.jsonl`.
- Everything goes to `src/Platform/UI.Admin.AutomationTests/src/bin/<configuration>/net10.0/TestResults/` (git-ignored): TUnit's HTML report (and the TRX with `--report-trx`), and `artifacts/<runId>/` with `results.jsonl` (every reported result, replayable through the reporters later) and a folder per test (`<class below the project namespace>/<method>`, e.g. `Tests.Access.SignInTests/SignIn_ShowsOrganizations`) with `failure.png`, `trace.zip` (open with `pwsh src/Platform/UI.Admin.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 show-trace <file>`) and `console.log`. The files are also attached to the test's result in TUnit's reports.
- A `trace.zip` holds the test's network log and the context's options, so the session cookies and tokens of the pool user (never the passwords: the sign-in runs untraced) are in it: keep traces where the test results are private.
- IDE: Visual Studio "Use testing platform server mode", Rider "Enable Testing Platform support", VS Code C# Dev Kit "Use Testing Platform Protocol".

## Configure

The settings are read in layers, each over the one before:

1. `testsettings.json` — the committed defaults (no secrets);
2. `testsettings.<profile>.json` for each profile of `UITESTS_PROFILE` or `--test-parameter Profile=…` (comma-separated);
3. `testsettings.local.json` — your overrides, git-ignored, e.g. the target and the pool e-mails;
4. the user secrets of the project (`UserSecretsId` in the csproj);
5. `UITESTS_*` environment variables, `__` between sections (`UITESTS_Target__BaseUrl`, `UITESTS_Users__Pool__0__Password`);
6. `--test-parameter Section:Key=value` (a value may contain `=`).

| Key | Meaning |
|---|---|
| `Target:BaseUrl` | The admin UI, e.g. `https://admin.staging.example.com`; empty skips every test |
| `Target:Organization` / `Project` / `View` | The pre-provisioned organization, project and view the tests work in |
| `Target:SignIn` | `AuthKit` (deployed environments) or `None` (a target that is signed in already, the mock) |
| `Target:MockScenario` | Mock target only: `?mock-scenario=<name>` on the first navigation of each test |
| `Target:EmailDomain` | Generated e-mail addresses are `ui-tests+<slug>@<domain>` |
| `Browser:*` | `Provider` (`Local`, or `BrowserStack`: see "Run on BrowserStack"), `Engine` (`chromium`, `firefox`, `webkit`; of the Local provider), `Channel`, `Headless`, `SlowMoMs`, `Device` (a Playwright device such as `Pixel 7`), `Viewport`, `Locale`, `TimeZone`, `ColorScheme` |
| `BrowserStack:*` | The platform, the dashboard's names and the logs of the BrowserStack provider; `Username` and `AccessKey` only in the user secrets; see "Run on BrowserStack" |
| `Execution:*` | `MaxParallelTests` (lowered to the pool size), `Seed`, the `*TimeoutMs`, `Retries` (skips and data conflicts never run again), `Trace` / `Screenshot` / `Video` (`Off`, `OnFailure`, `Always`), `ArtifactsPath` |
| `Users:Pool` | The pre-provisioned users: `Email`, `DisplayName`, `Roles` (the user's role in the target's organization and project, which a test's `RequiredUser` matches); `Password` only in the user secrets. A test leases one for its duration |
| `Reporting:*` | The test case management integration, off by default: `Enabled`, `Systems`, `RunName`, `RunDescription`, `UnmappedTests`, the batching and retry knobs, `ReplayFile`, and the `Testiny` section; see "Report to Testiny" |

Secrets, from `src/Platform/UI.Admin.AutomationTests/src`:

```bash
dotnet user-secrets set "Users:Pool:0:Password" "…"
dotnet user-secrets set "Users:Pool:1:Password" "…"
dotnet user-secrets set "BrowserStack:Username" "…" ; dotnet user-secrets set "BrowserStack:AccessKey" "…"   # for the browserstack-* profiles
dotnet user-secrets set "Reporting:Testiny:ApiKey" "…"                                                       # for the reporting-testiny profile
```

A `testsettings.local.json` for a deployed environment:

```json
{
  "Target": { "BaseUrl": "https://admin.staging.example.com", "Organization": "ui-tests", "Project": "automation" },
  "Users": {
    "Pool": [
      { "Email": "ui-test-01@example.com", "DisplayName": "UI Test 01", "Roles": [ "Owner" ] },
      { "Email": "ui-test-02@example.com", "DisplayName": "UI Test 02", "Roles": [ "Contributor" ] }
    ]
  }
}
```

Profiles: `mobile` (Pixel 7), `firefox`, `webkit`, `mock`, `browserstack-win-chrome` and `browserstack-mac-safari` (the browsers on BrowserStack; see "Run on BrowserStack"), `reporting-testiny` (turns the Testiny reporting on; combine it with a target profile).

## The mock target

The admin UI's mock build serves the API with MSW in the browser and has Ada Lovelace signed in, so the suite and its page objects can run without a deployed environment. It is not a sign-in bypass of the API or the UI; the mock build exists for the TypeScript suite already.

```bash
(cd src/Platform/ui.admin/src && npm ci && npm run dev:mock)    # in a second terminal: http://localhost:5174
UITESTS_PROFILE=mock dotnet run --project src/Platform/UI.Admin.AutomationTests/src
UITESTS_PROFILE=mock UITESTS_Browser__Engine=webkit UITESTS_Browser__Device="Pixel 7" dotnet run --project src/Platform/UI.Admin.AutomationTests/src
```

`testsettings.mock.json` targets `http://localhost:5174` with `acme` / `billing` / `default`, `SignIn` `None`, `MockScenario` `default` (`npm run dev:mock` starts the `showcase` dataset otherwise) and four placeholder pool users with the role `Owner` (Ada Lovelace owns `acme` and `acme/billing`), which only bound the parallelism. The mock's data lives in the tab's `sessionStorage`: every test starts from the scenario's seed, and what it creates is gone with its page. The dev server serves the app unbundled, so the profile raises `Execution:ExpectTimeoutMs` to 15 s for slow page loads (Firefox with 4 parallel tests).

## The Elementary set

`--treenode-filter "/**[Category=Elementary]"` runs the PR gate set, one test per area; each creates what it acts on (in the target's view, or a new project in its organization), and the tests that create something lease a user with an editing role:

| Test | Area | What it does |
|---|---|---|
| `SignIn_ShowsOrganizations` | Access | `/orgs` lists the target's organization and project, signed in |
| `Annotations_CreateResponsibility_AppearsInGrid` | Annotations | Bulk create (the toolbar; the command palette on a phone) creates `rst.at-<runId>-…`; the grid (the list on a phone) filtered by `key:` shows it, and its detail, opened by its URL, shows the key in its identity line |
| `Configurations_SetDocument_EffectiveShowsIt` | Configurations | Bulk create creates a responsibility (no ancestors, and usually no type schema or template); Create empty, a generated JSON document in the editor, save (`v2`); the Effective tab shows every value |
| `KeyValues_CreateAndRead` | KeyValues | New key `at_<runId>.<word>_<hex4>` with a value; its detail shows the value and the Versions tab `v1(current)` |
| `Project_Create_OpensSettings` | Projects | A user with the role `Owner` creates a project with a description in the organization; its Settings, opened through the navigation, show the description |
| `Navigation_MobileMenu_OpensPages` | Navigation | The sidebar (the "Open navigation" slideover below 1024 px) opens Configurations, Values store, Settings, Organizations and Annotations |

```bash
UITESTS_PROFILE=mock dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"
UITESTS_PROFILE=mock,mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"
```

The page objects in `Pages/` follow the admin UI's layouts: `App` (the shell: ready state, navigation by link name in the sidebar or the slideover, the command palette, toasts), `AppRoutes` (the routes of the target's organization, project and view), `LoginPage` (the public `/login` page of the AuthKit sign-in), `OrganizationsPage`, `OrganizationOverview`, `ProjectSettingsPage`, `AnnotationsPage` with `BulkCreateDialog`, `AnnotationDetail`, `ConfigurationsPage`, `ConfigurationDetail` and `KeyValuesPage`. They branch on the viewport's width, as the app does: the phone layout below 640 px (sm), the list instead of the grid below 768 px (md), the slideover instead of the docked sidebar below 1024 px (lg). An action of a page object returns once its effect shows (the page it opens, its dialog closed, the editor in place of the empty state, or its toast when nothing else shows it); the test asserts the exact text it cares about.

## Run on BrowserStack

`Browser:Provider` `BrowserStack` runs the browsers on [BrowserStack Automate](https://www.browserstack.com/docs/automate/playwright): the runtime connects the installed Playwright client (`Microsoft.Playwright`, the same package the local runs use) to `wss://cdp.browserstack.com/playwright`, with the session's capabilities in the `caps` parameter. BrowserStack's SDK is not used. Two profiles are committed:

| Profile | Platform |
|---|---|
| `browserstack-win-chrome` | Chrome `latest` on Windows 11, 1920×1080 |
| `browserstack-mac-safari` | Safari's engine on OS X Tahoe (macOS 26), 1920×1080: `playwright-webkit`, Playwright's WebKit build, since BrowserStack runs no branded Safari for Playwright; Tahoe is the newest macOS BrowserStack lists for its recent Playwright versions |

Credentials (once): the username and the access key of the BrowserStack account (Automate, "Access key"), in the user secrets or, in CI, `UITESTS_BrowserStack__Username` and `UITESTS_BrowserStack__AccessKey`. The access key is `***` in the settings on the terminal and in every message of the provider: it travels in the `caps` URL, which a connection error may echo. Playwright's own debug log (`DEBUG=pw:*`) may print that URL, so keep it off with real credentials.

```bash
(cd src/Platform/UI.Admin.AutomationTests/src && dotnet user-secrets set "BrowserStack:Username" "…" && dotnet user-secrets set "BrowserStack:AccessKey" "…")
UITESTS_PROFILE=browserstack-win-chrome dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"
UITESTS_PROFILE=browserstack-mac-safari,reporting-testiny dotnet run --project src/Platform/UI.Admin.AutomationTests/src
UITESTS_PROFILE=browserstack-win-chrome,mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src      # Pixel 7, emulated in Chrome
UITESTS_PROFILE=browserstack-win-chrome UITESTS_BrowserStack__Browser=edge dotnet run --project src/Platform/UI.Admin.AutomationTests/src
```

The target comes from your settings as usual (`testsettings.local.json`), and BrowserStack's browsers must reach it. A deployed environment is reachable as it is. A target on your machine or in a private network (`localhost`, the mock on `:5174`) needs **BrowserStack Local**: start the `BrowserStackLocal` binary yourself (`BrowserStackLocal --key <access key> --local-identifier <id>`; in GitHub Actions `browserstack/github-actions/setup-local`), then set `BrowserStack:Local` `true` and `BrowserStack:LocalIdentifier` `<id>`. The provider does not start it; without it a `localhost` target does not load.

| Key (`BrowserStack:`) | Default | Meaning |
|---|---|---|
| `Username` / `AccessKey` | (secret) | The account; required with the provider |
| `Endpoint` | `wss://cdp.browserstack.com/playwright` | The Playwright endpoint; any other `ws://` or `wss://` Playwright server works too and ignores the capabilities |
| `Os` / `OsVersion` | `Windows` / `11` | `Windows` with `10` or `11`; `OS X` with a macOS name (`Tahoe`, `Sequoia`, …) |
| `Browser` | `chrome` | `chrome` and `edge` (branded), `playwright-chromium`, `playwright-firefox` and `playwright-webkit` (Playwright's builds); the engine follows from it, `Browser:Engine` and `Browser:Channel` are not used |
| `BrowserVersion` | `latest` | `chrome` and `edge` only: `latest`, `latest-1`, `latest-beta` or a version |
| `Resolution` | `1920x1080` | The remote machine's screen; the page's viewport is still `Browser:Viewport` (or the `Browser:Device`) |
| `Project` | `2S Platform - admin UI` | The dashboard's project |
| `Build` | (see below) | The dashboard's build, a pattern with the placeholders of `Reporting:RunName` |
| `BuildTag` | `null` | A tag of the build, e.g. `nightly` |
| `Local` / `LocalIdentifier` | `false` / `null` | Route the browser through a running BrowserStack Local (with that identifier) |
| `PlaywrightVersion` | `null` | `browserstack.playwrightVersion`, `1.latest` or a version; unset, it is not sent and BrowserStack matches the client's version (`client.playwrightVersion`, always sent). Either way the client's major and minor version must be one BrowserStack runs (see below) |
| `SessionPerTest` | `true` | A session per test; `false`: a session per test class |
| `Console` | `errors` | `browserstack.console`: `disable`, `errors`, `warnings`, `info` or `verbose` |
| `NetworkLogs` / `Debug` / `Video` | `false` / `false` / `true` | BrowserStack's network log, a screenshot per command, the session's video |
| `MaxParallelSessions` | `5` | The parallel sessions of the plan; the tests that run at once are limited to it |
| `ConnectTimeoutSeconds` | `300` | How long a connection may take, queueing at BrowserStack included |
| `AdditionalCapabilities` | `{}` | More capabilities as strings, e.g. `{ "browserstack.idleTimeout": "300", "browserstack.maskCommands": "setValues, setCookies" }`; not the credentials |

The Playwright version: Playwright's server refuses a client of another major and minor version, and BrowserStack runs only the versions of [its table](https://www.browserstack.com/docs/automate/playwright/browsers-and-os). `Microsoft.Playwright` is therefore pinned in `Directory.Packages.props` to the newest version BrowserStack lists, **1.63.0** (BrowserStack does not run 1.64 yet); raise it only once the table lists the new version, and use the matching CI image (`mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`) and browsers (`playwright.ps1 install`). A session refused for its version fails with a message that names the client's version and that table.

What shows in BrowserStack's Automate dashboard:

- the **project** `BrowserStack:Project` and the **build** `BrowserStack:Build`: by default `<branch> #<run number>` in a GitHub Actions job and `local <machine> <yyyy-MM-dd>` elsewhere, so a machine's local runs of a day share one build; set it (e.g. `Nightly {Date:yyyy-MM-dd} {Sha7}`) to tell runs apart;
- a **session** per test named `<Class>.<Method>` (`SignInTests.SignIn_ShowsOrganizations`), or with `SessionPerTest` `false` one per test class named `<Class>`, with its video, console log and (when on) network log;
- each session marked **passed** or **failed**, with the failure's message as the reason (one line, at most 255 characters). A session per class is failed when one of its tests failed in its last attempt. A skipped test leaves its session unmarked. The mark is set when the test ends, through a page of the session (`browserstack_executor` `setSessionStatus`), so the sessions of a run that is killed stay unmarked;
- the session's name is also the test's **remote session** in its report, so the Testiny comment of a failed test names the session whose video to watch.

Parallel sessions: the plan limits how many sessions run at once. `[assembly: BrowserStackSessionLimit]` (in `PlatformAutomationSetup.cs`) limits the tests that run at once to `BrowserStack:MaxParallelSessions`, on top of `Execution:MaxParallelTests` and the pool size; set it to the plan's parallel limit, or to a share of it when others use the plan too. With `SessionPerTest` `false` a session stays open while tests of its class are still to come, so the open sessions can outnumber the running tests, and BrowserStack queues a session over the limit. It closes, marked, once every planned test of the class is done: it ran, or it ended without a browser (skipped by an attribute such as `[RequiresEngine]`, blocked before its browser). A session that no test of its class holds closes after two thirds of BrowserStack's idle timeout (`browserstack.idleTimeout`, 90 s by default, so after 60 s), before BrowserStack would end it unmarked; a later test of the class (a `[Serial]` one, which runs after the parallel ones, or a retry) opens another session. One that BrowserStack ended anyway is replaced.

Mobile is **emulated**: `Browser:Device` (the `mobile` profile, Pixel 7) gives the desktop browser on BrowserStack the device's viewport, user agent and touch, exactly as locally. Real mobile devices are out of scope.

Without an account, check the provider against a local Playwright server, which speaks the same protocol and ignores the capabilities (its major and minor version must be the client's, 1.63; the admin UI's own `@playwright/test` is another version):

```bash
npx -y playwright@1.63.0 run-server --port 3999    # in another terminal, besides npm run dev:mock
UITESTS_PROFILE=mock UITESTS_Browser__Provider=BrowserStack UITESTS_BrowserStack__Endpoint=ws://localhost:3999/ UITESTS_BrowserStack__Username=local UITESTS_BrowserStack__AccessKey=local-key-0000 dotnet run --project src/Platform/UI.Admin.AutomationTests/src
```

## Report to Testiny

`[TestCase("<id>")]` on a test (or its class) links it to a test case of [Testiny](https://www.testiny.io); `UITESTS_PROFILE=<target>,reporting-testiny` (or `Reporting:Enabled` `true` with `Reporting:Systems` `["Testiny"]`) reports the run as a classic Testiny **test run** whose test cases are those ids. The reporter is `42.Testing.UI.Automation.Reporting.Testiny`; `PlatformAutomationSetup` registers it.

The committed tests carry **no `[TestCase]` ids yet**: their cases are still to be created in Testiny. Until they are, a reported run is an empty Testiny run (no test cases, the totals in its description only). Add the id of a test's case to the test, e.g. `[TestCase("1234")]` on `SignInTests.SignIn_ShowsOrganizations`, and set `Reporting:UnmappedTests` `Warn` (`--test-parameter Reporting:UnmappedTests=Warn`) to list the tests that still have none, on the terminal and in the run's description.

Configure (once): the project id in `testsettings.local.json` (or `UITESTS_Reporting__Testiny__ProjectId`), the API key in the user secrets (or `UITESTS_Reporting__Testiny__ApiKey` in CI); best an API key restricted to the project.

```json
{ "Reporting": { "Testiny": { "ProjectId": 12, "TestPlanId": null } } }
```

```bash
(cd src/Platform/UI.Admin.AutomationTests/src && dotnet user-secrets set "Reporting:Testiny:ApiKey" "…")
UITESTS_PROFILE=mock,reporting-testiny dotnet run --project src/Platform/UI.Admin.AutomationTests/src
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --test-parameter Profile=reporting-testiny --test-parameter "Reporting:RunName=Nightly {Date:yyyy-MM-dd} {Engine} {Branch}"
```

| Key (`Reporting:Testiny:`) | Default | Meaning |
|---|---|---|
| `BaseUrl` | `https://app.testiny.io/api/v1/` | The API root |
| `ApiKey` | (secret) | Sent as `X-Api-Key`; never in a committed file, shown as `***` in the log |
| `ProjectId` | (required) | The numeric id of the Testiny project |
| `TestPlanId` | `null` | A test plan the run is added to |
| `Mode` | `TestRun` | `Automation` (Testiny's automation API) is not implemented yet and refused |
| `PlanCasesAsNotRun` | `true` | At the start every linked case goes into the run as `NOTRUN` |
| `CloseRunOnComplete` | `true` | The run is closed at the end |
| `Attachments` / `Comments` | `Failures` | `None`, `Failures` (every outcome but passed and skipped) or `All` |
| `MaxAttachmentBytes` | `26214400` (25 MB) | Larger files are listed in the comment instead of uploaded |
| `ResultForSkipped` / `ResultForBlocked` | `SKIPPED` / `BLOCKED` | The result status of a skipped and a blocked test (a custom status works too) |
| `RequestTimeoutSeconds` | `30` | Per request; a timeout is retried like a `429` or `5xx` |
| `MinUploadBytesPerSecond` | `262144` (256 KiB/s) | The slowest upload expected: an attachment may take its size at this rate when that is longer than `RequestTimeoutSeconds` (25 MB: 100 s) |
| `RunUrlTemplate` | `{AppUrl}/{ProjectId}/testruns/{RunId}` | The browser link of the run printed at the end. **Not verified**: Testiny does not document its app's URLs; set the form "Copy link to test run" shows |

The run's name is `Reporting:RunName`, a pattern with `{RunId}`, `{Date:yyyy-MM-dd}`, `{Time:HH:mm}`, `{Profile}`, `{Provider}`, `{Engine}`, `{Device}`, `{Branch}`, `{Sha7}`, `{BuildNumber}` and `{Machine}` (default `UI automation {Date:yyyy-MM-dd} {Time:HH:mm} {Profile}`). What lands in Testiny:

- **the run** (in `TestPlanId` when set), its description the environment table (target, profile, provider, engine, channel, browser version, OS, device, seed, branch, commit, build, machine, versions, run id) and, at the end, the totals, the test cases by status and the run's warnings; then it is closed. When the reporting stops before the end (e.g. Testiny refused the planning), the run is still closed, with why in its description;
- **every linked case** with its result: the worst over the tests that link it (`FAILED` > `BLOCKED` > `SKIPPED` > `PASSED`; a retried test counts with its last attempt), `NOTRUN` until a result arrives;
- per failed test and linked case: its **artifacts** (`failure.png`, `trace.zip`, `console.log`, `video.webm`) as attachments of the result, and a **markdown comment** with the test, outcome, duration (Testiny has no duration field), attempt, browser, seed, remote session, run, build, the artifacts and the exception with its stack trace;
- `[TestCase("1234")]` is the numeric id; `TC-1234` works too. `[TestCase("55", System = "TestRail")]` is left out of Testiny. With `Reporting:UnmappedTests` `Warn` or `Fail` the ids are checked once at the start; an unknown id (or one Testiny refuses) is a warning in the run's description and on the terminal, never a test failure. So is a `TestPlanId` Testiny refuses (the run goes on outside the plan), a check Testiny refuses (the ids are used unchecked), a comment it refuses, and an upload that failed twice (the comment lists it instead).

Testiny failing (`429`, `5xx`, timeouts) is retried with back-off (`Reporting:MaxAttempts`, at least the `Retry-After` Testiny sends) and never fails a test; what could not be sent stays in `results.jsonl`. Send a run again, e.g. one made while Testiny was down, as a new Testiny run (no tests run; the path is the one the end of the run prints, or relative to the build output like `Execution:ArtifactsPath`):

```bash
UITESTS_PROFILE=mock,reporting-testiny dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --test-parameter "Reporting:ReplayFile=TestResults/artifacts/<runId>/results.jsonl"
```

Testiny's limits are per account (writes 270 per 6 minutes, bulk writes 960 per 12 minutes, uploads 3200 per 30 minutes): a run costs one run creation, the planning (200 cases per request), one mapping per batch of results (`Reporting:BatchSize`), and per failed test one upload per artifact and linked case plus its share of one comment request and one comment mapping per batch.

## AuthKit checklist

`Target:SignIn` `AuthKit` signs each pool user in once per run, through `/login`, "Continue with AuthKit" and the AuthKit hosted page (e-mail, Continue, password, Sign in), and gives the user's later tests the captured storage state. The hosted page is WorkOS' UI; its labels are in `Access/AuthKitSignIn.cs`. The AuthKit environment of the target needs, by hand:

- password authentication enabled;
- no MFA (and no other second step) for the pool users;
- verified e-mail addresses of the pool users;
- each pool user a member of the default organization and project with the roles listed in `Users:Pool`;
- the target's redirect URI `https://<admin host>/callback` (and the sign-out redirect `/login`) registered;
- a custom auth domain (`authApiHostname` in the deployment's `app-config.json`) on any host other than `localhost`.

## Write a test

- Derive from `PlatformTest` (`Page`, `App`, `Target`, `Routes`, `PlatformData`, `User`, `Data`); override `RequiredUser` for a test that needs a role (`UserRequirement.WithRole("Owner")`, matched against the pool users' `Roles`): a test that creates or edits something asks for `EditingUser` (Contributor and up), since a Reader gets no create, edit or save action and the pool may hold one; mark it `[Test]`, add `[Elementary]` for the PR gate set and `[Area(Area.Annotations)]`; `[Serial]` for a test that must run alone (after the parallel ones, restoring what it changes); `[TestCase("<id>")]` links it to its test case in the test case management system.
- Restore what a test changed in an override of `CleanUpAsync` (it runs on the still open page; its failure fails the test), not in an `[After(Test)]` hook of your own: TUnit runs those with the result still `Passed`, so the report would miss their failure.
- A test that signs in itself (`SignedIn => false`) types the password on its traced page, and the trace keeps it: set `Execution:Trace` `Off` for such a run.
- Navigate with `App.GotoAsync(Routes.<page>)` or a page object's `OpenAsync`, relative to `Target:BaseUrl`, and follow links with `App.NavigateAsync("<link>")`; locate by role, label and text (`Pages/`).
- The grids and lists of annotations and configurations are virtualized: look for a row only in a filtered result (`key:<key>`), or open the detail's URL.
- Assert a toast (`App.Toast("<title>")`) right after its action: it closes after about 5 seconds, and while it is open it covers the bottom right of the page.
- Create what the test acts on, with names from `PlatformData` (`at-<runId>-…`, in every generated part of an annotation key, so the missing ancestors it creates carry it too; key values `at_<runId>.…`), and delete nothing: the growing data set is intended, and the prefix makes leftovers attributable.
- No `Task.Delay`: Playwright's `Expect` waits up to `Execution:ExpectTimeoutMs`.
