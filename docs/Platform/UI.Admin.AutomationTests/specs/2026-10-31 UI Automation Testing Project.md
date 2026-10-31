# UI Automation Testing Project

Linear: [424-133](https://linear.app/424fun/issue/424-133/create-ui-automation-testing-project)

Line numbers and file paths refer to `main` at `a6ce3aa3`. No application code is changed by this document. This revision (2026-10-31, second version) adds the test case management integration and the split into reusable libraries requested in the issue's second comment; it replaces the first version of the same day.

## Problem

The administration UI (`src/Platform/ui.admin`) has end-to-end coverage only through its TypeScript Playwright suite (`src/Platform/ui.admin/src/test/e2e`), which runs against the e2e build with the API served by MSW in the browser and a fake signed-in user. Nothing exercises the real system: the deployed UI, the real Storyteller API, Cosmos DB and the WorkOS AuthKit sign-in. There is no way to run a browser check against a deployed environment, no way to run on a cloud browser grid, no cheap "is the environment alive and usable" check for a pull request, and no link between automated tests and the test cases a test case management system tracks.

The issue asks for:

1. A C# project, `UI.Admin.AutomationTests`, that drives the real admin UI with **Playwright** under the **TUnit** runtime, is configured from a JSON file in the IDE (secrets in .NET user secrets), can run on **BrowserStack**, runs tests **in parallel** with an opt-out for tests that must run alone, marks a subset as **Elementary** for a later PR gate, creates everything it needs through the UI with **Bogus**-generated data, and keeps the tests idempotent.
2. A **generic test case management integration**: `[TestCase("<id>")]` on a test links it to a case of the external system; when configuration enables it, the runtime creates a **test run** there (name and API access configured) and reports each test's result, duration, exception stack, screenshots and other artifacts. The first implementation targets **Testiny** (https://www.testiny.io, API https://www.testiny.io/docs/api-quickstart/); TestRail, Qase, Kiwi or another system must be pluggable later.
3. The framework split into **reusable libraries** under `src/Libraries/Testing/` so other projects of the mono-repository can use it: `UI.Automation.Abstractions`, `UI.Automation.Runtime`, `UI.Automation.Runtime.TUnit`, `UI.Automation.Reporting.<system>`.

### Decisions taken with the issue owner (2026-10-31)

| Topic | Decision |
|---|---|
| System under test | A **deployed environment** (staging/dev URL). The base URL is configuration; a local Aspire stack is the same configuration with another URL. |
| Sign-in | Real **AuthKit** sign-in with dedicated test users; e-mail in configuration, **password in user secrets** (locally) or environment variables (CI). No sign-in bypass in the API or the UI. |
| Test data | A pre-provisioned **default organization, project and view** are configured. Tests create what they need inside them with generated names and **do not delete it** (the growing data set is intended load). A test that needs its own project or organization creates one with a random key. |
| Users | A **pool of pre-provisioned users**; a test leases one (or more) for its duration and returns it. The pool is large enough for the configured parallelism. |
| Random order | No explicit shuffle (TUnit has none). Parallel execution already removes any order guarantee; Bogus runs on a **logged seed** so a run's data is reproducible. |
| `dotnet test src` | The project always **builds**; the suite **skips with a clear reason** when no target base URL is configured. |
| BrowserStack scope | **Desktop browsers** (Chrome, Edge, Firefox, WebKit on Windows/macOS) on BrowserStack; **mobile is emulated** (Playwright device descriptors) locally and on BrowserStack. Real mobile devices are out of scope for the first version. |
| Test case management | Generic reporter contract in the abstractions; **Testiny first**, as a classic Testiny **test run** whose test cases are the ids given in `[TestCase]`. Enabled, named and authenticated by configuration. |
| Libraries | Four projects under `src/Libraries/Testing/`, consumed by `src/Platform/UI.Admin.AutomationTests`. |

## Current State

### The admin UI and its existing tests

- `src/Platform/ui.admin/src/playwright.config.ts`: three projects, `desktop` (Desktop Chrome, 1440×900), `mobile` (Pixel 7, 390×844) and `visual`; `locale: 'en-US'`, `timezoneId: 'Europe/Prague'`, `trace: 'retain-on-failure'`; the web server is `npm run build:e2e && vite preview --port 4174` with `VITE_MOCK_DATASET=default`.
- `src/Platform/ui.admin/src/test/e2e/*.spec.ts` and `support.ts`: the flows listed in `src/Platform/ui.admin/README.md` ("End-to-end and visual tests"). Locators are `getByRole`/`getByLabel`/`getByText`; only 4 components carry a `data-testid`. These suites stay as they are: they are fast, hermetic regression tests of the UI alone; the new project covers the integrated system.
- Sign-in: `app/stores/session.ts:78` `signIn()` calls `@workos-inc/authkit-js`, which redirects to the AuthKit hosted page and back to `/callback` (`app/pages/callback.vue`). The provider and client id come from `GET v1/auth/configuration`. On any host other than `localhost` the deployment must set `authApiHostname` (custom auth domain), otherwise the app refuses to start (`README.md`, "Configuration").
- Routes (`app/pages/`): `/login`, `/onboarding`, `/orgs`, `/[org]`, `/[org]/members`, `/[org]/[project]` (settings, members, invitations, machines, certificates, secrets, audit, usage, monitor, views) and `/[org]/[project]/[view]/…` (annotations, configurations, graph, key-values, modeling with templates/schemas/compare/deleted/export-import).
- CI: `.github/workflows/ui-admin.yml` runs the TypeScript suites in `mcr.microsoft.com/playwright:v1.64.0-noble` and deploys `main` to Azure Static Web Apps (`ui-admin` environment).

### Repository conventions that apply

- `Directory.Build.props`: StyleCop on by default (`EnableStyleCop`), `42.Monorepo.GitVersioning` referenced, `ManagePackageVersionsCentrally`, packages go to `.artifacts/`, `PackageReadmeFile` picked up from `docs/<ProjectStructurePath>/package-readme.md` when it exists. `Directory.Packages.props` holds every version, alphabetically.
- `src/Libraries/` is the home of reusable NuGet packages (suppressed as an mrepo item, `mrepo.json`); `src/Libraries/Testing/System.IO.Abstractions.Trace/src/System.IO.Abstractions.Trace.csproj` is the one testing library today: `AssemblyName` `42.Testing.System.IO.Abstractions.Trace`, `RootNamespace` `_42.Testing.System.IO.Abstractions`, `net10.0`, packable by default, no own `version.json`. Its docs folder (`docs/Libraries/Testing/…`) does not exist yet; `docs/Libraries/CLI/Toolkit/` has `readme.md`, `package-readme.md` and `release-notes/`.
- `src/Platform/Directory.Build.proj` (Traversal) includes `**\*.*?proj`, so `dotnet build src` and `dotnet test src` pick up every project under `src/Platform`; `src/Libraries` has the same traversal.
- Every test project today is xUnit 2.9 with `Microsoft.NET.Test.Sdk` (VSTest). `global.json` pins SDK `10.0.201` and has no `test.runner` entry, so `dotnet test` runs in VSTest mode.
- Test projects: `IsPackable=false`, `IsTestProject=true`, assembly `42.<Area>.<Project>`, namespace `_42.<Area>.<Project>`.
- User secrets are used by `Aspire.Host`, `Cli`, `Api.Functions`, `Api.Web` and `Mcp` (`<UserSecretsId>` in the csproj; `docs/Platform/development.md`, "API settings").
- `Bogus` 35.6.5 and `Microsoft.Extensions.*` 10.0.12 (Configuration, Binder, Json, EnvironmentVariables, Options, Logging.Abstractions, Http) are already in `Directory.Packages.props`.

### TUnit and Playwright facts the design relies on

Checked against the TUnit documentation and NuGet on 2026-10-31.

- TUnit (latest stable **1.76.45**, 2026-10-28) runs only on Microsoft.Testing.Platform. The test project needs `<OutputType>Exe</OutputType>`, must **not** reference `Microsoft.NET.Test.Sdk` or `coverlet.*`, and is run with `dotnet run`, `dotnet test` or `dotnet exec`. Arguments go after `--` with `dotnet test`. `TUnit` is the meta package (engine + entry point); `TUnit.Core` holds the attributes, `TestContext` and the extension interfaces and is what a **library** references.
- Mixing with the VSTest projects: `dotnet test` in VSTest mode runs an MTP project when the project sets `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>` (the TUnit package sets it). Switching `global.json` to `"test": { "runner": "Microsoft.Testing.Platform" }` would stop the xUnit 2.9 projects from running, so the repository stays in VSTest mode.
- Parallelism: every test runs in parallel by default; `--maximum-parallel-tests N` / `TUNIT_MAX_PARALLEL_TESTS` caps it globally. `[NotInParallel]` (no key) runs a test alone; `[NotInParallel("key", Order = n)]` serializes tests sharing a key in `Order`; `[ParallelLimiter<T>]` with `IParallelLimit` caps a group; `[ParallelGroup("key")]` batches classes. Issue thomhurst/TUnit#7088 (open, 2026-10) reports that **keyed** `NotInParallel` ignores `Order`; un-keyed ordering works. The documentation does not state whether constrained tests run after the unconstrained ones; Phase 1 verifies it (see §5.4).
- Filtering: `--treenode-filter "/<Assembly>/<Namespace>/<Class>/<Test>[Category=X]"`, with `*` and `/**`, e.g. `--treenode-filter "/**[Category=Elementary]"`; `[Category("…")]` and `[Property("k","v")]` attributes; one `[...]` group per path segment.
- `[Retry(n)]` (and a `RetryAttribute` subclass with `ShouldRetry`), `[Skip("…")]`, a `SkipAttribute` subclass with `ShouldSkip`, and `Skip.Test("…")` at run time, also from `[Before(Test)]` hooks. Hooks: `[Before/After(Test|Class|Assembly|TestSession|TestDiscovery)]`. Instance hooks in a **base class** apply to every derived test class; static session/assembly hooks live in the test assembly (Phase 1 checks whether TUnit also discovers them in a referenced library — §4.3). `--test-parameter key=value` reaches `TestContext.Parameters`.
- Extension points (`TUnit.Core.Interfaces`): `ITestStartEventReceiver`, `ITestEndEventReceiver`, `ITestRetryEventReceiver`, `ITestRegisteredEventReceiver` (can set a parallel limiter), `ITestDiscoveryEventReceiver`, `IParallelLimit`; they are attributes applied at method, class or assembly level. `TestContext` exposes the test details (class, method, attributes, display name), the result (status, exception, start/end, duration), output and an artifacts API.
- Reporting: `--report-trx`, HTML report on by default, a GitHub step summary when `GITHUB_ACTIONS` is set (`--github-reporter-style collapsible|full`), `--fail-fast`, `--timeout`.
- `TUnit.Playwright` gives `PageTest`/`BrowserTest`/`ContextTest` base classes; its browser is created with `LaunchAsync`; nothing documented lets it `ConnectAsync` to a remote endpoint, which BrowserStack requires. TUnit 1.76 depends on `Microsoft.Playwright` 1.64.0 (TUnit PR #7237).
- Microsoft.Playwright latest is **1.64.1**; the .NET container image is `mcr.microsoft.com/playwright/dotnet:v1.64.x-noble`. Device descriptors: `playwright.Devices["Pixel 7"]` returns a `BrowserNewContextOptions`.
- BrowserStack: `await playwright.Chromium.ConnectAsync($"wss://cdp.browserstack.com/playwright?caps={UrlEncode(json)}")` with capabilities `browserstack.username`, `browserstack.accessKey`, `browser` (`chrome`, `edge`, `playwright-chromium`, `playwright-firefox`, `playwright-webkit`), `browser_version`, `os`, `os_version`, `resolution`, `project`, `build`, `name`, `browserstack.local`, `browserstack.localIdentifier`, `browserstack.playwrightVersion` (`1.latest` or a version), `client.playwrightVersion`, `browserstack.console`, `browserstack.networkLogs`, `browserstack.debug`, `browserstack.video`, `browserstack.maskCommands`. The pass/fail status is set with `page.EvaluateAsync("_ => {}", "browserstack_executor: {\"action\":\"setSessionStatus\",\"arguments\":{\"status\":\"passed\"}}")`. `localhost` needs BrowserStack Local. BrowserStack's `BrowserStack.Playwright` NuGet replaces `Microsoft.Playwright` and is not used (it conflicts with the official package and with TUnit's dependency).

### Testiny API facts the design relies on

Checked against https://www.testiny.io/docs/api-quickstart/, https://www.testiny.io/docs/api-rate-limiting/ and the OpenAPI document `https://app.testiny.io/api/v1/swagger.json` (version 1.42.2) on 2026-10-31.

- Base URL `https://app.testiny.io/api/v1/`, header `X-Api-Key: <key>`, JSON bodies; reads return `_etag`, which updates must send back (`Update conflict` otherwise).
- Generic entity routes: `GET /<entity>/:id`, `POST /<entity>/find` (filter, pagination, `map`), `POST /<entity>` and `/bulk`, `PUT /<entity>/:id` and `/bulk`, `DELETE`, `POST /<entity>/mapping/bulk/<a>:<b>?op=add|update|add_or_update|delete`.
- **Classic test runs** (the model this plan uses first): `POST /testrun` `{ "title", "project_id", "description" }` → `TestRun { id, title, project_id, testplan_id, is_closed, closed_at, description, _etag }`. Test cases are put into the run and get their result through `POST /testrun/mapping/bulk/testcase:testrun?op=add_or_update` with `[{ "ids": { "testcase_id", "testrun_id" }, "mapped": { "result_status": "NOTRUN|PASSED|FAILED|BLOCKED|SKIPPED" } }]` (`TestRunTestCaseValues` also has `assigned_to`, `assigned_user_id`, `result_per_step`, `result_at`; there is **no duration field** on a result). A file is attached to a result with `POST /testrun/upload-attachment/{trId}/{tcId}?title=…&filename=…&mime_type=…` and the raw bytes as the body (`application/octet-stream`), answered with a `Blob`. A comment is created with `POST /comment/bulk` `[{ "project_id", "type": "TEXT", "text" }]` (markdown with the `<!-- MARKDOWN -->` header line) and mapped to a result with `POST /comment/mapping/bulk/comment:testcase:testrun?op=add`. A run is closed with `PUT /testrun/:id` `{ "id", "is_closed": true, "_etag" }`.
- **Automation API** (an alternative model): `POST /automation` (`AutomationResults { projectId, source, fieldNames, runGroupBy, runTitlePattern, results[] }`, each `AutomationResultItem { key|title, path[], status: passed|passed_warn|failed|blocked|skipped, duration (s), error (≤2000), detailMessage (≤100000), errorLocation { file, line, column, step }, attachments[] { refId, file, type, title }, fields[] }`) creates **automation sources, automation test cases and automation test runs** (`atm-*` entities, keyed by `atm_key`/title); attachments are prepared in the response (`preparedAttachments[].blobId`) and uploaded afterwards; `POST /atm-testrun/complete` completes runs. According to Testiny's forum (2025) automation test cases are **not linked to manual test cases**; that linkage is on their roadmap. This model has native duration/error fields but does not satisfy "link the C# test to the case with id X", so it is a second, optional mode (§7.6), not the first version.
- Rate limits (per account): writes 270 / 6 min, bulk writes 960 / 12 min, reads 1100 / min, blob uploads 3200 / 30 min; `429` on excess, no documented headers; recommended: wait and resend at a lower rate.

## Proposed Changes

### 1. Overview and dependency graph

```
src/Libraries/Testing/
├── UI.Automation.Abstractions/src/UI.Automation.Abstractions.csproj        42.Testing.UI.Automation.Abstractions
├── UI.Automation.Runtime/src/UI.Automation.Runtime.csproj                  42.Testing.UI.Automation.Runtime
├── UI.Automation.Runtime.TUnit/src/UI.Automation.Runtime.TUnit.csproj      42.Testing.UI.Automation.Runtime.TUnit
└── UI.Automation.Reporting.Testiny/src/UI.Automation.Reporting.Testiny.csproj  42.Testing.UI.Automation.Reporting.Testiny
src/Platform/
└── UI.Admin.AutomationTests/src/UI.Admin.AutomationTests.csproj            42.Platform.UI.Admin.AutomationTests

Abstractions  ←  Runtime  ←  Runtime.TUnit
Abstractions  ←  Reporting.Testiny
Runtime.TUnit + Reporting.Testiny  ←  UI.Admin.AutomationTests (+ Bogus, project-specific pages, data, tests)
```

| Project | Namespace | Depends on | Contains |
|---|---|---|---|
| `Abstractions` | `_42.Testing.UI.Automation` | nothing | `[TestCase]`, engine-neutral marker attributes, the result/artifact/run records, `ITestRunReporter`, `ITestRunReporterProvider` |
| `Runtime` | `_42.Testing.UI.Automation.Runtime` | Abstractions, `Microsoft.Playwright`, `Microsoft.Extensions.Configuration.*`, `Options`, `Logging.Abstractions` | settings model and loader, browser providers (Local, BrowserStack), context options, user pool, artifacts collector, seeded data, the reporting pipeline (dispatcher, aggregation, retry) |
| `Runtime.TUnit` | `_42.Testing.UI.Automation.Runtime.TUnit` | Runtime, `TUnit.Core` | `UiTest` base class with the test hooks, TUnit attributes (`Elementary`, `Area`, `Serial`, `RequiresProvider`, `ConfiguredRetry`), the event receiver that feeds the reporting pipeline, session start/stop helpers |
| `Reporting.Testiny` | `_42.Testing.UI.Automation.Reporting.Testiny` | Abstractions, `Microsoft.Extensions.Http`, `System.Text.Json` (in-box) | `TestinyOptions`, `TestinyClient`, `TestinyTestRunReporter`, its provider |
| `UI.Admin.AutomationTests` | `_42.Platform.UI.Admin.AutomationTests` | Runtime.TUnit, Reporting.Testiny, `TUnit`, `Bogus` | Platform target settings, AuthKit sign-in, page objects, data generators, the tests |

- All four libraries: `net10.0`, `LangVersion latest`, `Nullable enable`, packable (repo default), StyleCop on, `PackageTags` `testing;ui;automation;playwright` (+ `tunit` / `testiny`). No own `version.json` (they inherit the root `0.1.0`, like the other libraries); versions can be split later when a library is released on its own.
- Docs: `docs/Libraries/Testing/UI.Automation/readme.md` (one page for the family, mirroring §3–§7) and a `package-readme.md` per library folder so `Directory.Build.props` packs it. A stub spec `docs/Libraries/Testing/UI.Automation/specs/2026-10-31 UI Automation Libraries.md` points to this document; reviews of the library phases go to `docs/Libraries/Testing/UI.Automation/specs/reviews/`, reviews of the Platform project to `docs/Platform/UI.Admin.AutomationTests/specs/reviews/`.
- `Directory.Packages.props` (alphabetical, no grouping): add `Microsoft.Extensions.Configuration.UserSecrets` (10.0.12), `Microsoft.Playwright` (1.64.1), `TUnit` (1.76.45), `TUnit.Core` (1.76.45). Everything else is already there.
- `42.mono.slnx`: `/Libraries/Testing/` gets the four projects (and their unit test projects, §8), `/Platform/` gets `UI.Admin.AutomationTests`.

### 2. `42.Testing.UI.Automation.Abstractions`

No dependencies; usable from any test engine or reporter.

```csharp
namespace _42.Testing.UI.Automation;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class TestCaseAttribute(string id) : Attribute
{
    public string Id { get; } = id;              // the id in the external system, e.g. "1234" or "TC-1234"
    public string? System { get; init; }         // optional: restricts the id to one reporter ("Testiny", "TestRail") when several run at once
}
```

- Engine-neutral markers used by every runtime: `[Elementary]` is **not** here (it must derive from the engine's category attribute); instead the abstractions define the **names** the runtimes map to: `AutomationCategories.Elementary = "Elementary"`, `AutomationCategories.Serial = "Serial"`, `AutomationCategories.Maintenance = "Maintenance"`, and `AreaAttribute`'s category prefix.
- Records (immutable, serializable with `System.Text.Json` for the dispatcher's spill file):

```csharp
public enum TestOutcome { Passed, Failed, Skipped, Blocked, Timeout, Cancelled }

public sealed record TestRunDescriptor(
    string RunId, string Name, string? Description, DateTimeOffset StartedAt,
    IReadOnlyDictionary<string, string> Properties);          // profile, provider, engine, device, target, seed, branch, sha, build url

public sealed record TestIdentity(string Id, string DisplayName, string ClassName, string MethodName, string Assembly,
    IReadOnlyList<string> Categories, IReadOnlyList<TestCaseLink> Cases);

public sealed record TestCaseLink(string Id, string? System);

public sealed record TestFailure(string Message, string? ExceptionType, string? StackTrace, string? SourceFile, int? SourceLine);

public enum ArtifactKind { Screenshot, Trace, Video, ConsoleLog, NetworkLog, Output, Other }

public sealed record TestArtifact(string Name, string Path, string MimeType, ArtifactKind Kind, long SizeBytes);

public sealed record TestResultReport(
    TestIdentity Test, TestOutcome Outcome, DateTimeOffset StartedAt, TimeSpan Duration, int Attempt,
    TestFailure? Failure, string? Output, IReadOnlyList<TestArtifact> Artifacts,
    IReadOnlyDictionary<string, string> Properties);          // browser, engine, provider, device, user e-mail (redacted to its hash), seed

public sealed record TestRunSummary(DateTimeOffset FinishedAt, int Passed, int Failed, int Skipped, int Blocked, IReadOnlyList<string> Warnings);
```

- The reporter contract (one instance per run; every method must be safe to call concurrently from parallel tests and must **never throw into a test** — the dispatcher in the runtime guarantees the latter, the contract documents it):

```csharp
public interface ITestRunReporter : IAsyncDisposable
{
    string System { get; }                                                             // "Testiny"
    Task StartAsync(TestRunDescriptor run, IReadOnlyList<TestIdentity> plannedTests, CancellationToken ct);
    Task ReportAsync(IReadOnlyList<TestResultReport> results, CancellationToken ct);   // batched by the dispatcher
    Task CompleteAsync(TestRunSummary summary, CancellationToken ct);
    Uri? RunUrl { get; }                                                               // for the console and the CI summary, once known
}

public interface ITestRunReporterProvider
{
    string System { get; }                                                             // matches Reporting:<System> in configuration
    ITestRunReporter Create(IServiceProvider services, IConfiguration section, TestRunDescriptor run);
}
```

- `IArtifactSink` is not an abstraction: artifacts are files on disk described by `TestArtifact`; a reporter uploads what it wants.

### 3. `42.Testing.UI.Automation.Runtime`

Engine-neutral. Everything a Playwright-based UI suite needs except the hooks.

#### 3.1 Settings model and loader

`UiAutomationSettings` with the sections `Target` (base), `Browser`, `Execution`, `Users`, `BrowserStack`, `Reporting` — shape in §7. The `Target` section of the library has only `BaseUrl`; a consuming project binds its own extension type to the same section (`settings.Bind<PlatformTarget>("Target")`) for extra keys such as the Platform's `Organization`/`Project`/`View`.

`UiAutomationSettingsLoader.Load(UiAutomationSettingsOptions)`:

1. `testsettings.json` (base directory of the test assembly) — committed defaults.
2. `testsettings.{Profile}.json` when `UITESTS_PROFILE` (or `--test-parameter Profile=…`, passed in by the engine adapter) names one.
3. `testsettings.local.json` — git-ignored overrides for the IDE.
4. User secrets of the **test assembly** (`UserSecretsIdAttribute` of the entry assembly; the loader takes the assembly as a parameter so a library never guesses).
5. Environment variables with the prefix `UITESTS_`, `__` as the section separator.
6. Command-line parameters handed in by the engine adapter (`IDictionary<string,string>`), e.g. TUnit's `--test-parameter Browser:Headless=false`.

The loader returns `UiAutomationSettings` + the `IConfigurationRoot` (for project extensions) and validates (data annotations + `IValidateOptions`): absolute `http(s)` `BaseUrl` when present; known `Engine`; `Device` present in `playwright.Devices`; BrowserStack credentials when the provider is BrowserStack; pool/parallelism relations (§3.4); `Reporting` sections only when `Enabled`. The **redacted** effective settings are logged once (passwords, access keys and API keys replaced by `***`).

**Skip mode**: `BaseUrl` empty → `settings.IsConfigured == false`; nothing else is validated, no browser starts; the engine adapter skips every test with `UITESTS_Target__BaseUrl is not configured; see <README path given by the consumer>`.

#### 3.2 Browser providers

```csharp
public interface IBrowserProvider : IAsyncDisposable
{
    ValueTask<BrowserLease> AcquireAsync(TestIdentity test, CancellationToken ct);
    ValueTask ReleaseAsync(BrowserLease lease, TestResultReport? result, CancellationToken ct);   // result known at release time (status for BrowserStack)
}
public sealed record BrowserLease(IBrowser Browser, string? RemoteSessionName, IReadOnlyDictionary<string, string> Properties);
```

- `LocalBrowserProvider`: one `IPlaywright` per session; one `IBrowser` per engine, `BrowserTypeLaunchOptions { Headless, SlowMo, Channel }`; contexts per test.
- `BrowserStackBrowserProvider`: `SessionPerTest=true` (default) → one `ConnectAsync` per test, named `<Class>.<Method>`; `ReleaseAsync` sends `setSessionStatus` passed/failed with the failure message as `reason`, closes the browser. `SessionPerTest=false` → one session per class. Engine mapping: `chrome`/`edge`/`playwright-chromium` → `Chromium`, `playwright-firefox` → `Firefox`, `playwright-webkit` → `Webkit`. `Local=true` expects a running BrowserStack Local with the configured identifier (the provider does not start it). `client.playwrightVersion` is read from the `Microsoft.Playwright` assembly version. The provider exposes `MaxParallelSessions` so the engine adapter can install a parallel limiter.
- `ContextOptionsFactory`: `BrowserNewContextOptions` from `Browser` — `playwright.Devices[Device]` or `ViewportSize`, `Locale`, `TimezoneId`, `ColorScheme`, `BaseURL`, `RecordVideoDir` (when video is on), `IgnoreHTTPSErrors` for `localhost` only, plus the storage state the caller passes.

#### 3.3 Session, artifacts and timeouts

- `AutomationSession`: created once per run by the engine adapter (`AutomationSession.StartAsync(settings, entryAssembly, reporters…)`), holds `Settings`, `RunId` (`yyyyMMdd-HHmmss` + 4 random chars), `Seed`, `BrowserProvider`, `UserPool`, `StorageStateCache`, `ReportingDispatcher`, `RunDescriptor`; `StopAsync` flushes the dispatcher, completes the reporters and disposes the provider. It is engine-neutral; TUnit calls it from session hooks (§4.3).
- `TestScope`: what one test gets — `Context`, `Page`, the leased `TestUser`, an `ArtifactCollector` and its `TestIdentity`. `TestScope.BeginAsync(session, identity, options)` and `EndAsync(outcome, exception)` do the work the engine hooks only delegate to: lease user, acquire browser, create the context (with cached storage state), open the page, start tracing, subscribe to console/requests; at the end stop tracing (keep per policy), screenshot on failure, write `console.log`, close, release, return the user, and hand the `TestResultReport` to the dispatcher.
- `ArtifactCollector`: `Execution.ArtifactsPath/<RunId>/<Class>/<Method>[/attempt-n]/` with `trace.zip`, `failure.png`, `console.log`, `video.webm`; produces the `TestArtifact` list. The engine adapter also attaches them to the engine's own result (TUnit artifacts → HTML/TRX report).
- Page defaults from `Execution.*TimeoutMs`.

#### 3.4 User pool and storage state

- `UserPool` from `Users.Pool`; `LeaseAsync(UserRequirement, ct)` with a role filter, waits `LeaseTimeoutSeconds`, fails with `No free user with role X; pool has N, M leased`. `Return(user)`. A user is never held by two running tests. `MaxParallelTests` above the pool size is lowered with a warning; a second lease that could deadlock (`MaxParallelTests × 2 > pool`) is warned at start.
- `StorageStateCache`: per user, the storage state captured after the first sign-in of the session; `ISignInFlow` is an interface here (`Task SignInAsync(IPage page, TestUser user, CancellationToken ct)`), implemented by the consuming project (AuthKit for the Platform). The cache signs in once per user in a throw-away context, refreshes on a detected sign-out.

#### 3.5 Seeded data

`SeededData`: the session seed (`Execution.Seed` or random, logged) and a per-test `Random`/`Faker` seeded with `HashCode.Combine(seed, test id)`, so a test's data is deterministic for a seed regardless of order. Generic helpers only (`Slug(prefix)`, `Word()`, `Sentence(max)`), with a `Bogus` **optional** adapter (`SeededData.Faker()` in a separate `Runtime.Bogus`? — no: Bogus is small and already used in the repo; `Runtime` references `Bogus` directly to avoid a fifth package). Domain generators (annotation keys etc.) stay in the consuming project.

#### 3.6 Reporting pipeline

- `ReportingDispatcher`: a `Channel<TestResultReport>` with one background consumer per configured reporter. Results are batched (`Reporting:BatchSize`, default 20, or `Reporting:FlushIntervalSeconds`, default 5) and passed to `ITestRunReporter.ReportAsync`. Every reporter call is wrapped: exceptions are logged as run **warnings** (collected into `TestRunSummary.Warnings`), retried with exponential back-off on transient failures (`429`, `5xx`, timeouts; `Reporting:MaxAttempts`, default 5), and never propagate into a test. `StopAsync` drains the channel with a deadline (`Reporting:CompleteTimeoutSeconds`, default 120) so a stuck reporter cannot hang the process.
- **Aggregation**: several tests may point to one case id (`[TestCase("55")]` on two tests) and one test may point to several. The dispatcher keeps the raw per-test reports; aggregation per case is the reporter's job (§6 does worst-status-wins).
- **Planned tests**: at start the adapter hands the list of discovered tests with their `[TestCase]` links, so a reporter can create the run with every case as `NOTRUN` and the UI shows what is still pending.
- **Unmapped tests** (`[TestCase]` missing): `Reporting:UnmappedTests` = `Ignore` (default) | `Warn` | `Fail` (the run completes with a warning list, and the adapter fails the session when `Fail`).
- **Spill file**: every report is also appended to `Execution.ArtifactsPath/<RunId>/results.jsonl` so a crashed or offline run can be re-sent later (`Reporting:ReplayFile` setting → the adapter replays the file through the reporters instead of running tests; a small `Maintenance` test does it).
- Reporter selection: `Reporting:Enabled` and `Reporting:Systems` (list, default `["Testiny"]` when the Testiny section is present) resolve `ITestRunReporterProvider`s registered by the consuming project (`AutomationSession.StartAsync(..., providers: [TestinyReporterProvider.Instance])`) — no reflection scanning, so AOT/trimming stays possible.
- Run naming: `Reporting:RunName` is a pattern with placeholders `{RunId}`, `{Date:yyyy-MM-dd}`, `{Time:HH:mm}`, `{Profile}`, `{Provider}`, `{Engine}`, `{Device}`, `{Branch}`, `{Sha7}`, `{BuildNumber}`, `{Machine}`; the git/CI values come from `GITHUB_*` variables when present. Default: `UI automation {Date:yyyy-MM-dd} {Time:HH:mm} {Profile}`.

### 4. `42.Testing.UI.Automation.Runtime.TUnit`

References `TUnit.Core` (not the `TUnit` meta package: the engine and the entry point belong to the test project).

#### 4.1 `UiTest` base class

```csharp
public abstract class UiTest
{
    protected IPage Page => Scope.Page;
    protected IBrowserContext Context => Scope.Context;
    protected TestUser User => Scope.User;
    protected UiAutomationSettings Settings => AutomationSession.Current.Settings;
    protected SeededData Data => Scope.Data;
    protected TestScope Scope { get; private set; }

    protected virtual UserRequirement RequiredUser => UserRequirement.Any;
    protected virtual bool SignedIn => true;

    [Before(Test)]  public async Task BeginUiTest(TestContext context)  // skip when !IsConfigured; TestScope.BeginAsync(identity from context)
    [After(Test)]   public async Task EndUiTest(TestContext context)    // TestScope.EndAsync(outcome, exception); attach artifacts to context; report
    protected Task<TestUser> LeaseAnotherUserAsync(UserRequirement requirement);
}
```

`TestIdentityFactory` maps `TestContext` → `TestIdentity` (categories from `[Category]`, cases from `[TestCase]` on the method and the class, id = `<Assembly>/<Class>/<Method>` + the display name for data-driven tests). `TestOutcomeMapper` maps TUnit's result (passed/failed/skipped/timeout/cancelled; a `UserLeaseException` or `BrowserUnavailableException` → `Blocked`).

#### 4.2 Attributes

- `[Elementary]` : `CategoryAttribute(AutomationCategories.Elementary)`.
- `[Area("Annotations")]` : `CategoryAttribute` (the consumer may define an enum wrapper).
- `[Serial]` : `NotInParallelAttribute("serial")` plus `CategoryAttribute("Serial")` (the category exists for the two-invocation fallback, §5.4).
- `[RequiresProvider(BrowserProviderKind.Local)]`, `[RequiresEngine("chromium")]` : `SkipAttribute` subclasses reading the session settings.
- `[ConfiguredRetry]` : `RetryAttribute` whose count is `Execution.Retries` and whose `ShouldRetry` refuses to retry assertion failures that are data conflicts (so idempotency bugs surface). Applied by the consumer as `[assembly: ConfiguredRetry]`.
- `[BrowserStackSessionLimit]` : `ParallelLimiter<T>` installed programmatically through an `ITestRegisteredEventReceiver` when the provider is BrowserStack.

#### 4.3 Session hooks and the event receiver

- `UiAutomationSession` (static): `StartAsync(TestSessionContext, UiAutomationSessionOptions)` and `StopAsync(TestSessionContext)`. The **consuming test project** declares the global hooks and delegates (two lines), because TUnit's static session hooks are source-generated per test assembly:

```csharp
public static class Session
{
    [Before(TestSession)] public static Task Start(TestSessionContext c) => UiAutomationSession.StartAsync(c, PlatformSessionOptions.Create());
    [After(TestSession)]  public static Task Stop(TestSessionContext c)  => UiAutomationSession.StopAsync(c);
    [Before(TestDiscovery)] public static void Discovery(BeforeTestDiscoveryContext c) => UiAutomationSession.ConfigureDiscovery(c);  // MaxParallelTests
}
```

  Phase 1 checks whether TUnit discovers these hooks when they live in the library; if yes, the consumer's `Session` class disappears.
- `[assembly: ReportResults]` : an attribute implementing `ITestStartEventReceiver`/`ITestEndEventReceiver`/`ITestRetryEventReceiver` that builds the `TestResultReport` from the `TestContext` for tests that **do not** derive from `UiTest` too (plain API checks in the same assembly), and hands it to the dispatcher; `UiTest` tests report through `TestScope.EndAsync` (which knows the artifacts) and the receiver only de-duplicates by test id + attempt.
- `UiAutomationSession.ConfigureDiscovery` applies `Execution.MaxParallelTests` (`context.Settings.Parallelism.MaximumParallelTests`) unless the command line set it.
- `--test-parameter` values are read from `TestSessionContext`/`TestContext.Parameters` and passed to the loader as the highest source.

### 5. `42.Platform.UI.Admin.AutomationTests` — what stays in the Platform project

`src/Platform/UI.Admin.AutomationTests/src/UI.Admin.AutomationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>42.Platform.UI.Admin.AutomationTests</AssemblyName>
    <RootNamespace>_42.Platform.UI.Admin.AutomationTests</RootNamespace>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>              <!-- TUnit supplies the entry point -->
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <UserSecretsId>…new guid…</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Bogus" />
    <PackageReference Include="TUnit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\Libraries\Testing\UI.Automation.Runtime.TUnit\src\UI.Automation.Runtime.TUnit.csproj" />
    <ProjectReference Include="..\..\..\Libraries\Testing\UI.Automation.Reporting.Testiny\src\UI.Automation.Reporting.Testiny.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="testsettings*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

No `Microsoft.NET.Test.Sdk`, no `xunit`, no `coverlet.collector`. `.gitignore`: `testsettings.local.json`, `TestResults/`.

```
src/Platform/UI.Admin.AutomationTests/
├── README.md
└── src/
    ├── UI.Admin.AutomationTests.csproj
    ├── testsettings.json / testsettings.<profile>.json / testsettings.local.json (ignored)
    ├── Session.cs                     # the three delegating hooks (§4.3), [assembly: ReportResults], [assembly: ConfiguredRetry]
    ├── PlatformTarget.cs              # Target extension: Organization, Project, View; PlatformSessionOptions (sign-in flow, reporter providers)
    ├── Access/AuthKitSignIn.cs        # ISignInFlow for the AuthKit hosted page
    ├── Data/PlatformData.cs           # Bogus generators for keys, documents, e-mails (§5.2)
    ├── Pages/                         # page objects
    ├── Attributes/Area.cs             # enum + [Area(Area.Annotations)] wrapper
    └── Tests/<Area>/…Tests.cs
```

#### 5.1 Sign-in (`AuthKitSignIn : ISignInFlow`)

`Target:SignIn` selects the flow: `AuthKit` (default, the deployed environments) or `None` for a target that is already signed in — the admin UI's mock build (`npm run dev:mock`, `vite preview` of `build:e2e`), where MSW serves the API and Ada Lovelace is signed in. With `None` no password is required and the pool users only bound the parallelism; `Target:MockScenario` (e.g. `default`) appends `?mock-scenario=<name>` to the first navigation of each test so the mock data starts from a known seed. This is not a bypass in the API or the UI (the mock build already exists); it lets the suite and its page objects be run and verified without a deployed environment.


Open `{BaseUrl}/login` → AuthKit hosted page → e-mail → **Continue** → password → **Sign in** → `/callback` → the app shell (user menu visible). Role/label selectors; one class; covered by the first Elementary test. Pre-conditions on the AuthKit environment (manual): password authentication enabled, no MFA for the pool users, verified e-mails, membership of the default project with the roles listed in `Users.Pool`, the target host's redirect URIs registered. The README lists this checklist.

#### 5.2 Data (`PlatformData`, on top of `SeededData`)

`Slug(prefix)` → `at-<runId>-<two words>` in the key charset (lower-case letters, digits, `-`); `AnnotationKey(type, ancestors…)` for `rst.*`, `unt.*`, `sbt.*`, `usg.*`, `cnt.*`, `exe.*`, `uxe.*`; `ProjectKey()`, `OrganizationKey()`, `ViewName()`, `Description()` (≤500), `ConfigurationDocument(schema?)`, `TemplateDocument()`, `Email()` (sub-addressed on a configured test domain), `MachineName()`. Nothing is deleted; `at-<runId>` makes leftovers attributable. A `Maintenance` category holds `PurgeAutomationData` (`[Skip]` unless `--test-parameter Maintenance:Purge=true`) deleting `at-*` items older than `Maintenance:OlderThanDays` through the UI. Idempotency: a test creates what it acts on; the only assumptions (default project exists, the user is a member) are checked once at session start.

#### 5.3 Page objects and the first tests

`App` (shell), `LoginPage`, `OrganizationsPage`, `OrganizationOverview`, `ProjectSettingsPage`, `MembersPage` (+ invite dialog), `MachinesPage` (+ new machine dialog, detail), `ViewsPage`, `AnnotationsPage` (grid, filter, detail panel, bulk create), `AnnotationDetail`, `ConfigurationsPage` + `ConfigurationDetail`, `ModelingPage`, `KeyValuesPage`. A `data-testid` is added to the UI only where no accessible locator exists, documented in the review, kept in production builds.

**Elementary set** (each creates what it needs in the default view; every test carries its `[TestCase]` once the cases exist in Testiny):

1. `SignIn_ShowsOrganizations`
2. `Annotations_CreateResponsibility_AppearsInGrid` — through the **Bulk create** dialog (the toolbar button on desktop, the command palette on mobile), then the detail URL opened directly and its identity checked, so the test never depends on a row of the virtualized grid
3. `Configurations_SetDocument_EffectiveShowsIt`
4. `KeyValues_CreateAndRead`
5. `Project_Create_OpensSettings`
6. `Navigation_MobileMenu_OpensPages`

**Full set candidates** (Phase 6): bulk create with missing ancestors [Annotations]; delete, Undo, restore [Annotations]; compare versions and views [Configurations]; machine with API key, reset, one-time secret [Access]; invitation seen by a second leased user [Access]; template edit and "Discard changes?" [Modeling]; export/import check [Modeling]; type names as a project setting [Settings, `[Serial]`, restores]; theme and command palette [Navigation]. Axe checks stay in the TypeScript suite.

#### 5.4 Parallelism and order

- Default: parallel up to `MaxParallelTests`, bounded by the pool and the BrowserStack session limit.
- `[Serial]` tests share one key and run one after another, restoring what they change. Phase 1 probes (timestamps of 20 parallel + 3 serial tests) whether TUnit schedules keyed `NotInParallel` tests after the unconstrained ones; fallbacks: the lowest `ExecutionPriority` if honoured, else two CI invocations (`[Category!=Serial]` then `[Category=Serial]`).
- No shuffle; data is reproducible from the logged seed (`--test-parameter Execution:Seed=…`).

### 6. `42.Testing.UI.Automation.Reporting.Testiny`

#### 6.1 Options (`Reporting:Testiny`)

```jsonc
"Reporting": {
  "Enabled": false,                          // master switch; false → no reporter is created, [TestCase] is inert
  "Systems": ["Testiny"],
  "RunName": "UI automation {Date:yyyy-MM-dd} {Time:HH:mm} {Profile}",
  "RunDescription": null,                    // markdown; default: the redacted environment table (target, provider, engine, device, seed, branch, sha, build url)
  "UnmappedTests": "Ignore",                 // Ignore | Warn | Fail
  "BatchSize": 20, "FlushIntervalSeconds": 5, "MaxAttempts": 5, "CompleteTimeoutSeconds": 120,
  "ReplayFile": null,
  "Testiny": {
    "BaseUrl": "https://app.testiny.io/api/v1/",
    "ApiKey": "",                            // secret: user secrets "Reporting:Testiny:ApiKey" / UITESTS_Reporting__Testiny__ApiKey
    "ProjectId": 0,                          // required
    "TestPlanId": null,                      // optional: the run is added to the plan
    "Mode": "TestRun",                       // TestRun (first version) | Automation (§6.6, later)
    "PlanCasesAsNotRun": true,               // add every linked case with NOTRUN at start
    "CloseRunOnComplete": true,
    "Attachments": "Failures",               // None | Failures | All
    "MaxAttachmentBytes": 26214400,          // 25 MB per file; larger files are listed in the comment instead
    "Comments": "Failures",                  // None | Failures | All — the duration/environment/stack comment
    "ResultForSkipped": "SKIPPED",           // allows BLOCKED for projects that use it that way
    "ResultForBlocked": "BLOCKED",
    "RequestTimeoutSeconds": 30
  }
}
```

#### 6.2 `TestinyClient`

A thin typed client over `HttpClient` (`IHttpClientFactory` from `Microsoft.Extensions.Http`, `X-Api-Key` header, `System.Text.Json` source-generated context for AOT): `CreateTestRunAsync`, `GetTestRunAsync` (for `_etag`), `UpdateTestRunAsync` (`testplan_id`, `is_closed`), `MapTestCasesAsync(runId, items, op)`, `UploadResultAttachmentAsync(runId, caseId, stream, title, filename, mimeType)`, `CreateCommentAsync` + `MapCommentToResultAsync`, `FindTestCasesAsync(ids)` (to validate the configured ids once, when `UnmappedTests != Ignore`). `429` and `5xx` surface as `TestinyTransientException` so the dispatcher's back-off applies; `4xx` as `TestinyApiException` with the API's `ApiError` body.

#### 6.3 `TestinyTestRunReporter` (`Mode = TestRun`)

- `StartAsync`: `POST /testrun` with the resolved `RunName`, `project_id`, description; store `id` and `_etag`; `RunUrl` = `https://app.testiny.io/…/testruns/<id>` (verified in Phase 3 against the UI's URL scheme). If `TestPlanId` is set, `PUT /testrun/:id` with `testplan_id`. When `PlanCasesAsNotRun`, one `mapping/bulk … ?op=add_or_update` with every distinct case id of the planned tests as `NOTRUN` (chunks of 200).
- `ReportAsync(batch)`: for each case id in the batch, the **aggregated** status (worst wins: `FAILED` > `BLOCKED` > `SKIPPED` > `PASSED`; a retried test counts its last attempt) → one `mapping/bulk … ?op=add_or_update` per batch. Then, per test (not per case) and per policy: the attachments (`Attachments` = Failures/All; `trace.zip`, `failure.png`, `video.webm`, `console.log`, respecting `MaxAttachmentBytes`) uploaded to **each** linked case; and one markdown comment mapped to each linked case containing the test name, outcome, **duration**, attempt, browser/engine/provider/device, the seed, the artifact names, the exception type, message and stack trace (fenced), and the CI build URL. Duration lives in the comment because `TestRunTestCaseValues` has no duration field.
- `CompleteAsync`: a final comment on the run is not possible (comments map to results), so the run **description** is updated with the totals and the warning list (`PUT /testrun/:id` with `_etag`), then `is_closed = true` when `CloseRunOnComplete`.
- Rate limits: writes 270 / 6 min. A 200-test run with failures everywhere costs ~1 (run) + N/20 (batches) + failures × (uploads + 2 comment calls); uploads have their own 3200 / 30 min budget. The dispatcher's batch size and back-off keep a normal run far below the limits; `429` is retried.
- Case ids: `[TestCase("1234")]` is the numeric Testiny test case id; a `TC-1234` style value is accepted and its digits taken. Ids that do not exist in the project are reported as run warnings (`FindTestCasesAsync` once at start when `UnmappedTests != Ignore`), never as test failures.

#### 6.4 `TestinyReporterProvider`

`System = "Testiny"`, binds `Reporting:Testiny` to `TestinyOptions`, validates (`ApiKey`, `ProjectId`), creates the client and the reporter. Registered by the consumer in its session options.

#### 6.5 Tests of the library

`src/Libraries/Testing/UI.Automation.Reporting.Testiny/test/UI.Automation.Reporting.Testiny.UnitTests.csproj` (xUnit, like the rest of the repository — the library is engine-neutral): the request shapes against a fake `HttpMessageHandler` (run creation, NOTRUN planning in chunks, aggregation worst-wins, attachment policy and size cap, comment content, `_etag` round-trip, `429` → transient), options validation. No live Testiny calls in unit tests; a hand-run `testint` against a scratch Testiny project is a Phase 3 checklist item, not a repository test.

#### 6.6 `Mode = Automation` (later, optional)

Same reporter interface; `POST /automation` with `source = Reporting:Testiny:Source`, `fieldNames` (`profile`, `browser`, `provider`, `device`, `branch`), `runGroupBy` (`branch`, `profile`), `runTitlePattern`, one `AutomationResultItem` per test with `key` = the test id, `path` = `[assembly, class]`, `duration`, `error`, `detailMessage`, `errorLocation` (from the stack trace), `attachments` (prepared → uploaded by `blobId`), then `POST /atm-testrun/complete`. Native durations and failure fields, dashboards per source, but automation test cases are **separate** from manual ones (Testiny forum, 2025). Documented in the options; implemented only when asked.

### 7. Configuration shape (consumer view, `testsettings.json`)

```jsonc
{
  "Target": { "BaseUrl": "", "Organization": "ui-tests", "Project": "automation", "View": "default", "SignIn": "AuthKit", "MockScenario": null },
  "Browser": {
    "Provider": "Local", "Engine": "chromium", "Channel": null, "Headless": true, "SlowMoMs": 0,
    "Device": null, "Viewport": { "Width": 1440, "Height": 900 },
    "Locale": "en-US", "TimeZone": "Europe/Prague", "ColorScheme": "light"
  },
  "Execution": {
    "MaxParallelTests": 4, "Seed": null,
    "ActionTimeoutMs": 10000, "NavigationTimeoutMs": 30000, "ExpectTimeoutMs": 10000,
    "Retries": 0, "Trace": "OnFailure", "Screenshot": "OnFailure", "Video": "Off",
    "ArtifactsPath": "TestResults/artifacts"
  },
  "Users": {
    "LeaseTimeoutSeconds": 120,
    "Pool": [
      { "Email": "ui-test-01@…", "Password": "", "Roles": ["Owner"] },
      { "Email": "ui-test-02@…", "Password": "", "Roles": ["Contributor"] }
    ]
  },
  "BrowserStack": {
    "Username": "", "AccessKey": "", "Project": "2S Platform – admin UI", "Build": null,
    "Os": "Windows", "OsVersion": "11", "Browser": "chrome", "BrowserVersion": "latest", "Resolution": "1920x1080",
    "Local": false, "LocalIdentifier": null, "PlaywrightVersion": "1.64.0", "SessionPerTest": true,
    "Console": "errors", "NetworkLogs": false, "Debug": false, "Video": true, "MaxParallelSessions": 5
  },
  "Reporting": { /* §6.1 */ }
}
```

Secrets (never in committed files): `Users:Pool:<n>:Password`, `BrowserStack:Username`, `BrowserStack:AccessKey`, `Reporting:Testiny:ApiKey` — user secrets locally, `UITESTS_…__…` variables in CI. Profiles: `mobile`, `firefox`, `webkit`, `browserstack-win-chrome`, `browserstack-mac-safari`, `reporting-testiny` (turns reporting on; combinable through `UITESTS_PROFILE=browserstack-win-chrome` plus `UITESTS_Reporting__Enabled=true`).

### 8. Running

```bash
# once: browsers for the local provider
dotnet build src/Platform/UI.Admin.AutomationTests/src
pwsh src/Platform/UI.Admin.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium

cd src/Platform/UI.Admin.AutomationTests/src
#   testsettings.local.json → Target.BaseUrl, Users.Pool e-mails, Browser.Headless=false …
dotnet user-secrets set "Users:Pool:0:Password" "…"
dotnet user-secrets set "BrowserStack:Username" "…" ; dotnet user-secrets set "BrowserStack:AccessKey" "…"
dotnet user-secrets set "Reporting:Testiny:ApiKey" "…"

dotnet run                                                        # everything, local Chromium, no reporting
dotnet run -- --treenode-filter "/**[Category=Elementary]"        # the PR gate set
UITESTS_PROFILE=mobile dotnet run                                 # Pixel 7 emulation
UITESTS_PROFILE=browserstack-win-chrome dotnet run -- --report-trx
UITESTS_Reporting__Enabled=true dotnet run -- --test-parameter "Reporting:RunName=Nightly {Date:yyyy-MM-dd} {Engine}"
dotnet run -- --maximum-parallel-tests 1 --test-parameter Browser:Headless=false --test-parameter Browser:SlowMoMs=200
dotnet run -- --test-parameter Reporting:ReplayFile=TestResults/artifacts/<runId>/results.jsonl   # re-send a run
dotnet test src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"
dotnet test src                                                   # builds; the suite skips without Target.BaseUrl
```

The console prints the run URL of every reporter at the end (and the GitHub step summary gets it). IDE: Visual Studio "Use testing platform server mode", Rider "Enable Testing Platform support", VS Code C# Dev Kit "Use Testing Platform Protocol".

### 9. CI (prepared, wired in a later issue)

`.github/workflows/ui-admin-automation.yml`: `workflow_dispatch` (inputs `profile`, `filter`, `target_url`, `report` boolean) and `workflow_call`; runs in `mcr.microsoft.com/playwright/dotnet:v1.64.x-noble` with `UITESTS_*` from the `ui-admin-automation` GitHub environment (variables: target, organization/project/view, user e-mails, Testiny project id; secrets: passwords, BrowserStack keys, Testiny API key); `--report-trx`; uploads `TestResults/` on failure; the TUnit GitHub reporter and the Testiny run URL in the step summary. A BrowserStack variant uses `browserstack/github-actions`. Which PR events gate on Elementary and how a PR gets a staging deployment are follow-up issues.

### 10. Documentation and `CLAUDE.md`

- `docs/Libraries/Testing/UI.Automation/readme.md`: the family (dependency graph, settings reference, providers, attributes, reporting contract, how to add a reporter, how to adopt the framework in another project — the four-step recipe: reference `Runtime.TUnit` + a reporter, add `Session.cs`, implement `ISignInFlow`, bind the `Target` extension); `package-readme.md` per library.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`: the Platform specifics (target, users, AuthKit checklist, data rules, Elementary set, Testiny project and case ids, running); linked from `docs/Platform/development.md`.
- `CLAUDE.md`: Tech Stack adds "**UI automation:** TUnit + Microsoft.Playwright, reusable `42.Testing.UI.Automation.*` libraries, Testiny reporting, BrowserStack-capable"; Libraries list gets `Testing/UI.Automation.*/`; the Platform table gets `UI.Admin.AutomationTests/`; Build Commands get the `dotnet run … --treenode-filter` lines; Testing notes that this project is TUnit on Microsoft.Testing.Platform (no `Microsoft.NET.Test.Sdk`, `--treenode-filter`, arguments after `--`), that it skips without `Target.BaseUrl`, that its folder is `src/` because the project is the tests, and that `[TestCase("<id>")]` links a test to the test case management system.

### 11. Phases

| Phase | Content | Done when |
|---|---|---|
| **1 Libraries skeleton + sign-in** | Abstractions (attributes, records, reporter contract); Runtime (settings loader/validation, skip mode, `LocalBrowserProvider`, `ContextOptionsFactory`, `AutomationSession`, `TestScope`, artifacts, `UserPool`, `StorageStateCache`, `SeededData`, the dispatcher with a `NullReporter`); Runtime.TUnit (`UiTest`, attributes, session helpers, `ReportResults` receiver); the Platform project with `Session.cs`, `AuthKitSignIn`, `PlatformData`, test 1; the hook-discovery and serial-scheduling probes; packages, solution, README, CLAUDE.md. | `dotnet test src` passes (skips) without configuration; `SignIn_ShowsOrganizations` passes against the configured environment in Chromium, Firefox and WebKit, headless and headed, desktop and `mobile`; `results.jsonl` is written. |
| **2 Elementary set** | Page objects, tests 2–6, `[TestCase]` ids once the cases exist in Testiny. | Elementary set green in 3 consecutive runs with `MaxParallelTests=4` and 4 pool users. |
| **3 Testiny reporting** | `Reporting.Testiny` (`Mode = TestRun`): client, reporter, provider, unit tests; dispatcher batching/back-off/replay verified; run URL in the console. | A run against a scratch Testiny project shows the run, every case with its status, duration/stack comments and screenshots/traces on failures; a forced `429` is retried; an offline run replays from `results.jsonl`. |
| **4 BrowserStack** | `BrowserStackBrowserProvider`, session naming/status, session limiter, `browserstack-*` profiles. | Elementary set green on BrowserStack Windows 11 Chrome latest and macOS Safari; sessions visible with pass/fail; the Testiny run carries the BrowserStack session names. |
| **5 CI** | `ui-admin-automation.yml`, environment variables/secrets, artifact upload, docs pages. | A hand-run against staging is green and shows the TUnit summary and the Testiny run URL. |
| **6 Coverage** | Full-set candidates, `Maintenance` purge, `[Serial]` tests, optionally `Mode = Automation`. | Decided per test in later issues. |

Each phase ends with a review (library phases under `docs/Libraries/Testing/UI.Automation/specs/reviews/`, Platform phases under `docs/Platform/UI.Admin.AutomationTests/specs/reviews/`).

### 12. Risks and mitigations

- **TUnit static hooks in a referenced library** may not be discovered → the consumer keeps the three-line `Session.cs` (the design already assumes it).
- **AuthKit hosted UI changes** → one class, one Elementary test that fails first; storage-state cache limits sign-ins; back-off on `429`.
- **Keyed `NotInParallel` ordering bug (TUnit#7088)** → serial tests restore their changes and never depend on order; the two-invocation fallback exists.
- **Reporter failures or slowness** → the dispatcher isolates every reporter call: results never fail a test, the drain has a deadline, the spill file allows a replay.
- **Testiny rate limits** → batched mappings, per-run budget estimate logged at start, exponential back-off, attachments only for failures by default.
- **Case ids drifting** (a case deleted or moved in Testiny) → validated once at start as run warnings, never as failures.
- **Playwright version skew** (package vs. BrowserStack `browserstack.playwrightVersion` vs. the CI image tag) → one value in `Directory.Packages.props`; all three logged at session start.
- **Growing data set** → intended; `at-<runId>` prefixes and the purge; Elementary tests filter by their own slug.
- **Flaky selectors** → role/label locators, `Expect` with configured timeouts, no `Task.Delay`; `Retries` stays `0` locally.

### 13. Second opinion: the CodeRabbit coding plan

The issue carries a CodeRabbit coding plan (`2026-10-31 Create UI automation testing project.md`, attached to its comment), written before the reporting and library requirements. It was read as a source of ideas, not of decisions:

- **Adopted:** a mock target with an optional sign-in (`Target:SignIn = None`, `Target:MockScenario`, §5.1); the first annotation test through bulk create and the detail page (`annotation-identity`) instead of the virtualized grid, reusing the locators of the TypeScript suite (`Reload the catalog`, the toolbar's **Bulk create**, the command palette's **Bulk create annotations** on mobile); data generated only inside test bodies and hooks, never in TUnit data sources, so test ids stay stable; one `Faker`/`Randomizer` per test and never the global `Randomizer.Seed`; verify every TUnit API against the pinned version, including whether `CategoryAttribute` and `NotInParallelAttribute` can be derived from.
- **Not adopted:** the PowerShell launcher that runs one process per test in a seeded random order (the issue owner chose no shuffle, §Decisions; it stays the documented fallback for serial-last scheduling next to the two-invocation one, §5.4); excluding the project from the traversal `Test` target (the owner chose a project that builds and skips without a target); `appsettings.json` with an `Automation__` prefix (this plan uses `testsettings*.json` and `UITESTS_`, so test settings never mix with an app's); no CI workflow (Phase 5 adds one that runs only by hand or when called).

### 14. Open items for the environment and tooling owner

- The staging/dev base URL and the AuthKit environment of the test users; the organization/project/view keys (`ui-tests` / `automation` / `default` are placeholders); the pool users and roles (suggested 6: 2 Owner, 1 Administrator, 2 Contributor, 1 Reader); the mailbox domain for generated invitation addresses.
- The BrowserStack plan's parallel session limit.
- Testiny: the project id, an API key restricted to that project, whether the Elementary test cases already exist (ids for `[TestCase]`) or are to be created by hand before Phase 2, and whether runs should go into a test plan.
