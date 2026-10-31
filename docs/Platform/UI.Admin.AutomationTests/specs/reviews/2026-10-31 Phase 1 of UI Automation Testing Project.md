# Phase 1 of UI Automation Testing Project

Linear: [424-133](https://linear.app/424fun/issue/424-133/create-ui-automation-testing-project).

## Overview

Phase 1 covers the first row of section 11 of [UI Automation Testing Project](../2026-10-31%20UI%20Automation%20Testing%20Project.md), "Libraries skeleton + sign-in". It adds three of the four libraries of section 1 (`UI.Automation.Abstractions`, `UI.Automation.Runtime` and `UI.Automation.Runtime.TUnit`; `UI.Automation.Reporting.Testiny` comes in phase 3), with unit test projects for the two runtimes. It also adds the Platform project `UI.Admin.AutomationTests` with the AuthKit sign-in, `PlatformData`, the first page objects and test 1, `SignIn_ShowsOrganizations`. A probe against the pinned TUnit 1.76.45 answered the spec's open questions: whether hooks are discovered in a library, how serial tests are scheduled, and whether `dotnet test` works. Several of its answers changed the design (D1 to D6 below). There is no deployed environment yet, so the suite was checked against the admin UI's mock build (`npm run dev:mock`) through the `mock` profile: Chromium, Firefox and WebKit, desktop and Pixel 7, all headless. The phase delivers the libraries and the Platform project together, so this one review covers both, and `docs/Libraries/Testing/UI.Automation/specs/reviews/` has no review for phase 1.

| Commit | Content |
|---|---|
| `f70c16f9` | The three libraries, the two unit test projects, `Directory.Packages.props`, `42.mono.slnx`, the `.gitignore` negations |
| `3f819e7e` | The Platform project, the traversal exclusions, `CLAUDE.md` |

A review of the implementation confirmed 18 findings (some of them duplicates) and rejected 10. All 18 were fixed before these commits; the fixes are marked "review fix" below. The rejected ones that leave work for later are listed under "Not verified and deferred".

| Project | Passed (final pass) |
|---|---|
| `src/Libraries/Testing/UI.Automation.Runtime/test` | 130 |
| `src/Libraries/Testing/UI.Automation.Runtime.TUnit/test` | 25 |
| `src/Platform/UI.Admin.AutomationTests/src` (`mock`, desktop Chromium and Pixel 7) | 1 |

## What Was Done

### 1. Overview and dependency graph

- **Projects.**
  - `src/Libraries/Testing/UI.Automation.Abstractions/src/UI.Automation.Abstractions.csproj`: assembly `42.Testing.UI.Automation.Abstractions`, namespace `_42.Testing.UI.Automation`.
  - `src/Libraries/Testing/UI.Automation.Runtime/src/UI.Automation.Runtime.csproj`: `42.Testing.UI.Automation.Runtime`, with the namespaces `.Settings`, `.Browsers`, `.Users`, `.Access`, `.Data`, `.Artifacts` and `.Reporting`.
  - `src/Libraries/Testing/UI.Automation.Runtime.TUnit/src/UI.Automation.Runtime.TUnit.csproj`: `42.Testing.UI.Automation.Runtime.TUnit`.
  - `src/Platform/UI.Admin.AutomationTests/src/UI.Admin.AutomationTests.csproj`: `42.Platform.UI.Admin.AutomationTests`.
  - Unit tests: `UI.Automation.Runtime/test/UI.Automation.Runtime.UnitTests.csproj` (xUnit, Shouldly, Moq, TestableIO TestingHelpers, `Microsoft.Extensions.TimeProvider.Testing`) and `UI.Automation.Runtime.TUnit/test/UI.Automation.Runtime.TUnit.UnitTests.csproj` (xUnit, Shouldly). The Abstractions project has no test project.
- **Graph.** Abstractions ← Runtime ← Runtime.TUnit ← `UI.Admin.AutomationTests` (plus `TUnit` and `Bogus`), as planned, without Reporting.Testiny.
- **Deviation: Abstractions depends on `Microsoft.Extensions.Configuration.Abstractions`.** The spec's table says "nothing", but the planned `ITestRunReporterProvider.Create` takes an `IConfiguration`.
- **Runtime packages.** `Bogus`, `Microsoft.Extensions.Configuration`, `.Binder`, `.Json` and `.UserSecrets`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Playwright` and `TestableIO.System.IO.Abstractions` (with `.Wrappers`). There is no `Options` and no `Configuration.EnvironmentVariables` package:
  - the loader reads the `UITESTS_*` variables itself, and a test can pass them in through `UiAutomationSettingsOptions.EnvironmentVariables`;
  - validation is code (`Settings/SettingsValidator.cs`), not data annotations and `IValidateOptions`.
- **Packaging.** The libraries are packable, with `PackageTags` `$(PackageTags);testing;ui;automation;playwright` (plus `tunit` on Runtime.TUnit). They have no `version.json` of their own.
- **`Directory.Packages.props`** gains `Microsoft.Extensions.Configuration.UserSecrets` 10.0.12, `Microsoft.Playwright` 1.64.1, `TUnit` 1.76.45 and `TUnit.Core` 1.76.45, in alphabetical order.
- **`42.mono.slnx`.** `/Libraries/Testing/` gets the three libraries and the two test projects; `/Platform/` gets the automation project.
- **Review fix: `.gitignore` negations.** The root `.gitignore` ignores every `artifacts/` folder, which also hid `UI.Automation.Runtime/src/Artifacts/` (`ArtifactCollector.cs`, `MimeTypes.cs`) and `UI.Automation.Runtime/test/Artifacts/` (`ArtifactCollectorTests.cs`). Two negations after that line keep them. After the fix, `git status --ignored -uall` shows nothing ignored except `bin/` and `obj/`.

### 2. `42.Testing.UI.Automation.Abstractions`

- **As specified.** `TestCaseAttribute`, `TestOutcome`, `ArtifactKind`, the records `TestCaseLink`, `TestIdentity`, `TestFailure`, `TestArtifact`, `TestRunDescriptor`, `TestResultReport` and `TestRunSummary`, and the contract `ITestRunReporter` / `ITestRunReporterProvider`, one file each.
- **`AutomationCategories`** has `Elementary`, `Serial`, `Maintenance`, `AreaPrefix = "Area-"` and `Area(string)`. As planned, `[Elementary]` itself lives in the engine adapter.
- **Additions.**
  - `TestCaseAttribute.ToLink()` and `TestCaseLink.AppliesTo(system)`, which is case-insensitive; a null `System` applies to every reporter.
  - `TestFailure.FromException`: it unwraps a single-inner `AggregateException` and takes the file and line from the first stack frame outside the `/_/…` sources of packages such as Playwright, which is the test's own line (that second part was a fix in part B).
  - `TestRunSummary.Total`.
  - `TestRunProperties`: the key constants of the run and result properties (`profile`, `engine`, `device`, `seed`, `sha`, `playwrightVersion`, `user`, `attempt` and others).
  - `TransientReporterException` with `RetryAfter`: an engine-neutral signal for the dispatcher's back-off. Section 6.2 had planned a Testiny-specific transient exception.
- **`TestIdentity.Id`** is documented as unique per test instance (see 4.1).
- **Comments** are `//`, as in `src/Platform/Storyteller/Configuration/src`, not XML documentation.

### 3. `42.Testing.UI.Automation.Runtime`

#### 3.1 Settings model and loader

- **Files.** `Settings/UiAutomationSettingsLoader.cs`, `UiAutomationSettingsOptions`, `UiAutomationSettings`, the sections `TargetSettings`, `BrowserSettings` (with `ViewportSettings`), `ExecutionSettings`, `UsersSettings` (with `PoolUserSettings`), `BrowserStackSettings` and `ReportingSettings`, the enums `BrowserProviderKind`, `BrowserEngine`, `ArtifactPolicy` and `UnmappedTestsPolicy`, plus `SettingsValidator`, `SettingsRedaction` and `UiAutomationSettingsException`.
- **Layers.** The six layers of the spec, in its order. JSON comments and trailing commas are allowed.
- **Profiles.** Several can be given, comma-separated, and apply in order (`UITESTS_PROFILE=mock,mobile`). The options value wins, then the `Profile` parameter, then `UITESTS_PROFILE`.
- **A named profile must exist, also in skip mode.** A missing profile file, or a file that is not JSON, fails at once (`Load_FailsForAMissingProfileFile_EvenWithoutATarget`). `testsettings.mock.json` supplies the target itself, so with a lenient loader a typo such as `mok` would fall into skip mode and exit 0.
- **D5, sign-in setting: `Target:SignIn` is in the runtime.**
  - It holds the name of a registered flow, `None` (`TargetSettings.NoSignIn`), or nothing, which means the first registered flow (or none when there is none).
  - Pool passwords are required only when the resolved flow has `ISignInFlow.RequiresPassword`.
  - `Target:MockScenario` belongs to the admin UI's mock, so it is not in the runtime; the Platform binds it (section 5).
- **`UiAutomationSettings`** exposes:
  - `Configuration` and `Bind<T>(section)` (cached per type and section) for a project's own keys;
  - `ResolvePath`;
  - `Describe()`, the redacted dump logged at session start;
  - `Warnings`, `IsConfigured`, `IsReplay` and `EffectiveEngine`.
- **Pool relations.**
  - A `MaxParallelTests` above the pool size is lowered to the pool size, with a warning.
  - `MaxParallelTests × 2 > pool` gets a second warning, so the `mock` profile (4 users, 4 parallel tests) always shows it.
- **Redaction.** A key is secret when any of its segments contains `Password`, `AccessKey`, `ApiKey`, `Secret` or `Token`. That masks more than needed, on purpose. An empty secret is shown empty, so a missing password is visible.
- **Skip mode.** For a replay, only `Reporting` is validated, and `Reporting:ReplayFile` needs `Reporting:Enabled`.
- **D6: files through `IFileSystem`.** The JSON files and the user secrets (path from `PathHelper`) are read through `System.IO.Abstractions`, per the repository's rule. So are the run directory, the artifacts and `results.jsonl`. The loader tests run on `MockFileSystem`.
- **Library defaults.**
  - `Browser:TimeZone` is `UTC`; the Platform's `testsettings.json` sets `Europe/Prague`.
  - `BrowserStack:PlaywrightVersion` is null, meaning the client's version. Section 7 had `"1.64.0"`; null matches section 3.2, which reads `client.playwrightVersion` from the assembly.

#### 3.2 Browser providers

- **Types.**
  - `Browsers/IBrowserProvider`, which also has `Kind`, `MaxParallelSessions`, `Devices` and `Properties`.
  - `BrowserLease` and `LocalBrowserProvider`.
  - `BrowserProviderFactory`, which throws `NotSupportedException` for BrowserStack until phase 4.
  - `ContextOptionsFactory`, `DeviceCatalog` and `BrowserUnavailableException`.
- **The browser launches at session start**, so a missing browser fails before any test runs. Review fix: when the launch fails, the message names the install command, with the channel (`install chrome`) when `Browser:Channel` is set.
- **Firefox and a phone.** Playwright refuses `isMobile` in Firefox. `ContextOptionsFactory` drops it with a warning and keeps the device's viewport, user agent and touch.
- **`IgnoreHTTPSErrors`** is set for `localhost` only.
- **BrowserStack** has only its settings model and validation (`BrowserStackSettings.Browsers`, `GetEngine()`).

#### 3.3 Session, artifacts and timeouts

- **`AutomationSession`.**
  - The signature is `StartAsync(settings, AutomationSessionOptions?, ct)`, instead of `(settings, entryAssembly, reporters…)`. `AutomationSessionOptions.FromSetup(IUiAutomationSetup)` fills the options from the consumer's setup (section 4.3).
  - There is no static `Current`; the engine adapter keeps the session.
  - `ReplayAsync` returns a `ReplayResult`.
  - The run id is `yyyyMMdd-HHmmss-xxxx`: the dash is new.
  - User info in the target URL is stripped from the run properties.
  - `Assertions.SetDefaultExpectTimeout` is set for the whole process.
- **Review fix: start order and clean-up.**
  - `StartAsync` builds the `UserPool` before the browser launches.
  - A failure after the reporters started completes and disposes them (`AbandonReportingAsync`).
  - `ReporterSelection.CreateAsync` (formerly synchronous) disposes the reporters it already created when a later provider throws.
- **`TestScope`.**
  - `TestScope.Create(session, test, TestScopeOptions?)` does no IO, and `BeginAsync(ct)` does the work, instead of `BeginAsync(session, identity, options)`.
  - `EndAsync(outcome, exception?, output?, report = true)` runs once, never throws, and works after a `BeginAsync` that threw. The `report` flag (part B) lets the adapter end an attempt that TUnit will run again without reporting it.
  - Also: `LeaseAnotherUserAsync` and `NewContextAsync`.
  - `TestScopeOptions` has `User`, `LeaseUser`, `SignedIn`, `Attempt` and `StartedAt`.
- **Outcomes and failures.**
  - `AutomationOutcomes` refines Failed to Blocked for `UserLeaseException` and `BrowserUnavailableException`.
  - `AutomationSession.CreateReport` keeps only the reason of a skip: no exception type, no stack trace.
  - The new `DataConflictException` is the exception `[ConfiguredRetry]` never retries.
- **Review fix: `SignInAgainAsync` removed.** It signed in again on the test's traced page, so `trace.zip` kept the typed password, and nothing called it. A test that signs in itself (`SignedIn => false`) still records what it types. That is documented on `TestScope`, on `UiTest.SignedIn` and in the README: turn `Execution:Trace` off for such a run.
- **Review fix: the artifact folder layout** (`Artifacts/ArtifactCollector.cs`).
  - The path is `<ArtifactsPath>/<RunId>/<class below the assembly namespace>/<method>[/attempt-n]/`, e.g. `Tests.Access.SignInTests/SignIn_ShowsOrganizations`. A class segment that had to be sanitized or shortened gets a hash of the full name.
  - Data rows and repeats use `<display name>-<hash of the id>`.
  - Why: the spec's `<Class>` was the short class name, so same-named classes in two namespaces shared a folder, and one test's `.video` cleanup could delete the other's recording. Now every instance and attempt has a folder of its own.
  - The folder is created on first use. The raw `.video` folder is deleted after the video is saved. `console.log` is kept and attached for passing tests too.

#### 3.4 User pool and storage state

- **Users.** `Users/UserPool`, `UserRequirement` and `UserLeaseException`, as specified. `TestUser` has a `Fingerprint` (`u-xxxxxxxx`), and its `ToString()` shows only the pool index and the fingerprint.
- **`Access/ISignInFlow`** has:
  - `Name`, `RequiresPassword` (default true) and `SignInAsync`;
  - `IsSignedOutAsync` (default false);
  - review fix: `IsSignInStorageItem(origin, name)` (default false).
- **`Access/StorageStateCache`** keeps the states in memory only. It signs each user in once per session, in a throw-away, untraced context, also when two scopes ask at the same time. Members: `TryGet`, `Invalidate`, `RefreshAsync` and `SignInCount`.
- **Review fix: `RefreshAsync` instead of `CaptureAsync`, with a storage-state merge.**
  - Why: authkit-js refreshes the session on every page load, and AuthKit rotates the refresh token. A state captured once would go stale for the user's later tests.
  - The merge (`Access/StorageStateMerge.cs`, internal) takes the cookies from the context, and the sign-in's own local-storage items (those `IsSignInStorageItem` accepts) from the context too. Every other item stays as captured right after the sign-in, so the admin UI's `2s-admin:*` preferences never pass from one test to the next.
  - A context that lost every sign-in item it started with drops the cached state.
  - `TestScope.EndCoreAsync` runs `KeepSignInsAsync` on each context signed in from the cache, after the trace stopped, whether the test passed or failed. Signed out drops the state, still signed in refreshes it, and a refresh that fails drops it.
  - This path runs only with AuthKit. Unit tests cover it (`StorageStateMergeTests`, `StorageStateCacheTests`); it has not run against AuthKit.

#### 3.5 Seeded data

- **Types.** `Data/SeededData` (`Seed`, `Random` as a Bogus `Randomizer`, `Faker`, `NewSessionSeed()`, `Word()`, `Slug(prefix, words)`, `Sentence(max)`, `Hex(n)`) and `Data/StableHash`. The Runtime references Bogus directly, as planned.
- **Deviation: `StableHash` instead of `HashCode.Combine`.** `HashCode` is randomized per process, which would break the reproducibility from the logged seed. The seed is FNV-1a over (session seed, test id, attempt) plus the MurmurHash3 finalizer. A test pins it, and it was checked against an independent implementation.
- **The attempt is part of the seed**, so a retry gets fresh names and does not collide with what the failed attempt created.

#### 3.6 Reporting pipeline

- **Types.** `Reporting/ReportingDispatcher`, `ReportingDispatcherOptions`, `ResultsFile` (with `ResultsFileContent`, `ResultsFileLine` and `ResultsFileJsonContext`), `RunNameFormatter` with `RunNameValues`, `RunEnvironment`, `RunDescriptions`, `TransientFailures` and `ReporterSelection`.
- **The dispatcher.**
  - It has one channel and one consumer per reporter.
  - Each reporter sees only the case links of its own system.
  - A reporter whose start fails gets nothing more.
  - `Reporting:CompleteTimeoutSeconds` bounds the start, the drain and the completion separately, and disposing a reporter is capped at 10 s.
  - Unmapped tests are computed at completion, from the planned and the reported tests.
  - The summary counts the last report of each test id.
- **Deviation: no `NullReporter`.** Zero reporters is valid and still writes `results.jsonl`.
- **`results.jsonl`.**
  - Its first line holds the run descriptor and the planned tests, so a replay can recreate the run; the result lines follow.
  - It is written with a source-generated JSON context.
  - A cut last line is skipped with a warning.
- **A report sent before `StartAsync`** would be overwritten by the run line. The session prevents this, and the TUnit adapter holds early results until the session starts (4.3).

### 4. `42.Testing.UI.Automation.Runtime.TUnit`

- **Project file.** It references `TUnit.Core` only and sets `TUnitImplicitUsings` to `false`. TUnit's global `using static TUnit.Core.HookType` makes `Assembly` and `Test` ambiguous (CS0229), so the hooks are written `[Before(HookType.Test)]`.
- **Files.**
  - `UiTest`, `UiAutomationSession`, `UiAutomationSetupAttribute` with `UiAutomationSetupAttribute<TSetup>`.
  - `TestIdentityFactory`, `TestOutcomeMapper`, `RetryPolicy`, `TestParameters` and `ConsoleSessionLogger`.
  - The attributes `ElementaryAttribute`, `AreaAttribute`, `SerialAttribute`, `RequiresProviderAttribute`, `RequiresEngineAttribute`, `ConfiguredRetryAttribute`, `ReportResultsAttribute` and `BrowserStackSessionLimitAttribute`.

#### 4.1 `UiTest` base class

- **Members.**
  - `Page`, `Context`, `User`, `Data` and `Scope`.
  - `Settings`, which comes from `UiAutomationSession.Settings`, because `AutomationSession` has no static `Current`.
  - `Session`, which is new.
  - `RequiredUser`, `SignedIn` and the new `LeasesUser` (false for a test of a public page).
  - `LeaseAnotherUserAsync`.
- **`BeginUiTest(TestContext, CancellationToken)`.** The token parameter fixes TUnit warning TUnit0075.
- **`EndUiTest(TestContext)`** maps the outcome, attaches the artifacts to TUnit's result (with "(attempt n)" from the second attempt on) and disposes the scope.
- **Review fix: `CleanUpAsync`.**
  - The problem: a derived class's own `[After(Test)]` hook runs before `EndUiTest`, while the result still says Passed. TUnit fails the test for that hook's exception only after every hook has run. The report said Passed, and with retries the test was reported twice as final.
  - `protected virtual Task CleanUpAsync()` runs inside `EndUiTest`, on the still open page. Its exception turns a passing attempt into Failed, decides the retry (`RetryPolicy.WillRetryAsync(context, failure)`) and is thrown again, so TUnit fails the test too.
  - `UiTest` now claims every attempt it ends, also one TUnit will run again. Otherwise the end receiver reported the first attempt as Passed.
  - The class comment and the README say not to write an `[After(Test)]` hook of your own.
- **`TestIdentityFactory`.**
  - The id is `<Assembly>/<full class name>/<Method>`, plus the display name of a data row.
  - Review fix: a test that runs as several instances (method or class arguments, `[Repeat]`) also gets `#<FNV-1a of TestDetails.TestId>`. Display names alone collided for class data sources, for `[Repeat]` and for rows whose arguments format the same. Reports were dropped silently, and seeds and artifact folders were shared. Ordinary tests keep the plain id. `UiTest` logs a warning when its claim on a report is refused.
  - Deviation: `[TestCase]` is read by reflection, from the method (with its overloads) and its class, and cached. TUnit's source generator keeps only attributes related to TUnit types, so `TestDetails.GetAttributes<TestCaseAttribute>()` was always empty in the probe.
- **`TestOutcomeMapper`** maps TUnit's states, unwraps TUnit's wrapper exceptions, and maps `UserLeaseException` and `BrowserUnavailableException` to Blocked.

#### 4.2 Attributes

- **`[Elementary]`** is `CategoryAttribute(AutomationCategories.Elementary)`, as planned. **`[Area(string)]`** is `CategoryAttribute("Area-<area>")` and is not sealed, so a project can wrap an enum.
- **D3: `[Serial]` is a parallel group, a keyed constraint and a category, and it runs last.**
  - The probe showed that a keyed `[NotInParallel("serial")]` runs interleaved with the unconstrained tests.
  - So `SerialAttribute` is a plain `Attribute` and an `ITestDiscoveryEventReceiver`. It adds the category `Serial`, `ParallelGroupConstraint(Key, 0)` and `NotInParallelConstraint([Key]) { Order = Order }`.
  - TUnit runs such a group after every unconstrained test, one test at a time and in `Order`.
  - The key is `SerialAttribute.Key`, `ui-automation-serial`, not `serial`.
  - Neither fallback of section 5.4 is needed (`ExecutionPriority`, two invocations).
  - The category is added at discovery, so `--treenode-filter` sees it.
- **`[RequiresProvider(BrowserProviderKind)]` and `[RequiresEngine(params BrowserEngine[])]`** are `SkipAttribute` subclasses.
  - They decide at registration, from the settings loaded before discovery.
  - Deviation: they take enums instead of strings (`[RequiresEngine("chromium")]` in the spec).
  - Without a target, or in a replay, they skip with the same reason as every UI test.
- **D4: `[ConfiguredRetry]` is a discovery receiver, not a `RetryAttribute` subclass.**
  - `RetryAttribute.Times` is a constructor constant, so it cannot come from the settings.
  - `ConfiguredRetryAttribute` implements `ITestDiscoveryEventReceiver` and `IScopedAttribute`, with `ScopeType = typeof(RetryAttribute)` and `Order = -1`, so it replaces any `[Retry]`.
  - It calls `DiscoveredTestContext.SetRetryLimit(Execution:Retries, predicate)`, with 0 in skip mode and in a replay.
  - `RetryPolicy.ShouldRetry` refuses a `SkipTestException` and a `DataConflictException`. The skip matters because a `Skip.Test` from `[Before(Test)]` reaches the retry predicate.
- **`[BrowserStackSessionLimit]`** is an `ITestRegisteredEventReceiver`. It sets a programmatic `IParallelLimit` when `Browser:Provider` is BrowserStack, so it does nothing in phase 1.

#### 4.3 Session hooks and the event receiver

- **D2: no `Session.cs`; the hooks live in the library.**
  - The probe showed that static `[Before(TestDiscovery)]` and `[Before/After(TestSession)]` hooks declared in a referenced library run. The executable's generated initializer touches a public type of each referenced TUnit assembly, which runs the library's module initializer.
  - `UiAutomationSession.ConfigureDiscovery`, `StartAsync` and `StopAsync` are those hooks.
  - The consumer declares `[assembly: UiAutomationSetup<PlatformAutomationSetup>]` (or `[assembly: UiAutomationSetup(typeof(…))]`). The hooks look for it on the entry assembly, then on the other loaded assemblies, and create the setup with its parameterless constructor.
  - `IUiAutomationSetup` (in the Runtime) takes the place of the spec's `PlatformSessionOptions`. It has `ReadmeHint`, `SignInFlows`, `ReporterProviders`, `Services` and `Validate(settings, errors)`.
- **The settings load in `[Before(TestDiscovery)]`.**
  - Registration-time code (`ShouldSkip`, the registered receivers) runs before `Before(TestSession)`, so the settings must exist earlier.
  - The setup is passed to the loader, so the `Target:SignIn` name and the passwords are checked at load.
  - The hook always hands the (possibly lowered) `Execution:MaxParallelTests` to `context.Settings.Parallelism.MaximumParallelTests`. The command line and `TUNIT_MAX_PARALLEL_TESTS` still win inside TUnit, so the hook does not need to check them.
  - Every discovery starts from scratch (`Reset()`), because an IDE runs several sessions in one process.
- **Load and start failures do not throw from the hooks.** The error is kept in `UiAutomationSession.Failure`, and each UI test fails in `Before(Test)` with its message. TUnit's discovery is not affected. Plain tests still run but are not reported.
- **`--test-parameter`.** `TestParameters` parses the raw command line, because TUnit cuts a value at its second `=`. TUnit's parsed values are the fallback.
- **`[ReportResults]` uses only the end receiver.**
  - The engine never calls `ITestRetryEventReceiver`, and `OnTestEnd` fires for every attempt, before TUnit decides on a retry.
  - So `UiAutomationSession.ReportFromEngineAsync` repeats TUnit's rule (`RetryPolicy.WillRetryAsync`: the retry limit plus the predicate).
  - It drops duplicates by test id and attempt (`TryClaim`).
  - It holds results that arrive before the session starts: attribute skips are decided at registration.
  - It reports nothing in skip mode or after a failed start.
  - Tests skipped by an attribute run no hooks, so this receiver is the only thing that reports them.
- **The session log goes to standard output.** `ConsoleSessionLogger` writes lines prefixed `ui-automation:` straight to the process's stdout, because TUnit shows the console output of hooks only with `--output Detailed`.
  - Under `--server` (an IDE) it writes nothing of its own.
  - Review fix: under `--list-tests` it writes to stderr, so `--list-tests json` stays a JSON document.
- **`StopAsync`** logs the totals, attaches `results.jsonl` as a session artifact, and throws when `Reporting:UnmappedTests` is `Fail` and tests have no `[TestCase]`.

### 5. `42.Platform.UI.Admin.AutomationTests`

- **Project file.** As in the spec, except:
  - there is no `UI.Automation.Reporting.Testiny` reference until phase 3;
  - review fix: the direct `Microsoft.Extensions.Configuration.UserSecrets` reference was redundant and is removed. The `UserSecretsId` (`3d07d28d-5b62-432e-9cfc-b53b6b883740`) still produces the assembly's `UserSecretsIdAttribute`.
- **Files.**
  - `README.md` and `src/.gitignore` (`testsettings.local.json`, `TestResults/`).
  - `testsettings.json` and the profiles `testsettings.mobile.json`, `testsettings.firefox.json`, `testsettings.webkit.json` and `testsettings.mock.json`.
  - `PlatformAutomationSetup.cs` (in place of `Session.cs`), `PlatformTarget.cs` and `PlatformTest.cs`.
  - `Access/AuthKitSignIn.cs`, `Data/PlatformData.cs`, `Attributes/Area.cs` and `Attributes/AreaAttribute.cs`.
  - `Pages/App.cs`, `Pages/LoginPage.cs`, `Pages/OrganizationsPage.cs` and `Tests/Access/SignInTests.cs`.
- **D1: the traversals build the project but leave it out of `dotnet test`.**
  - `src/Directory.Build.proj` and `src/Platform/Directory.Build.proj` add `<ProjectReference Update="**\UI.Admin.AutomationTests.csproj" Test="false" />`, with a comment.
  - Why: on the .NET 10 SDK, the MSBuild targets of Microsoft.Testing.Platform 2.5.2 fail a VSTest-mode `dotnet test` of an MTP project ("Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later"), so `dotnet test src` would fail.
  - The MTP mode of `global.json` is no way out: it rejects the xUnit 2.9 projects and the Traversal projects.
  - Overriding the private `_MTPBeforeVSTest` target worked in the probe. It was not adopted, because an MTP update could break it.
  - So the suite runs with `dotnet run --project … -- <options>`. The spec's done-when "`dotnet test src` passes (skips)" becomes: `dotnet test src` leaves the project out, and `dotnet run` without configuration skips every test and exits 0.
- **`PlatformAutomationSetup`.**
  - It holds the four assembly attributes: `UiAutomationSetup<PlatformAutomationSetup>`, `ReportResults`, `ConfiguredRetry` and `BrowserStackSessionLimit`.
  - `ReadmeHint` is the README path, `SignInFlows` is `[AuthKitSignIn]`, and there are no reporters yet.
  - `Validate` checks:
    - `Target:Organization`, `Project` and `View` against the pattern of `NameRules`;
    - the `Target:MockScenario` name;
    - `Target:EmailDomain`;
    - a pool that is not empty.

    Review fix: the empty-pool message names `testsettings.local.json`, the user secrets and the README's "Configure" section.
- **`PlatformTarget : TargetSettings`** binds the `Target` section, with `Organization`, `Project`, `View` and `MockScenario`. Deviation: it also has **`EmailDomain`**, which section 7 does not list. Section 5.2's `Email()` assumes "a configured test domain".
- **D5, mock target: `Target:SignIn None` and `Target:MockScenario`.**
  - `testsettings.mock.json` targets `http://localhost:5174`, with `acme` / `billing` / `default`, `SignIn` `None`, `MockScenario` `default` and four placeholder pool users, which only bound the parallelism.
  - `App.GotoAsync` adds `?mock-scenario=<name>` to the first navigation of each test.
  - The mock keeps its data in the tab's `sessionStorage`, so every test starts from the scenario's seed.
- **Deviation: a `PlatformTest` base class.** It derives from `UiTest` and adds `Target`, `App` (with the mock scenario) and `PlatformData`, created per attempt.
- **Deviation: `Area` shadowing.** The `Area` enum and `AreaAttribute(Area)` are in the root namespace `_42.Platform.UI.Admin.AutomationTests`. A test file imports `_42.Testing.UI.Automation.Runtime.TUnit`, and `[Area(Area.Access)]` still resolves to the project's attribute, not to the library's `[Area(string)]`.
- **`AuthKitSignIn`.**
  - The flow: `/login`, "Continue with AuthKit", then the hosted page (Email, Continue, Password, Sign in), then back on the app past `/callback`, until the user menu is visible. It throws when the user has no password.
  - `IsSignedOutAsync` is true on `/login` or on another host. It reads the target URL from `UiAutomationSession.LoadedSettings`.
  - `IsSignInStorageItem` accepts the `workos:*` items.
- **`PlatformData`** builds on `SeededData`:
  - `Slug`, `OrganizationKey`, `ProjectKey`, `ViewName` and `MachineName`;
  - `AnnotationKey(code, parts)`, with the codes `rst`, `sbt`, `unt`, `cnt`, `usg`, `exe` and `uxe`;
  - `KeyValueKey`, which starts with `at_<runId>` because key-value keys allow no `-`;
  - `Description`, `ConfigurationDocument()` (without the planned schema parameter), `TemplateDocument` and `Email` (`ui-tests+<slug>@<EmailDomain>`).
- **Page objects.**
  - `App`: the shell, the user menu, and the navigation slideover below 1024 px.
  - `LoginPage` and `OrganizationsPage`.
  - The locator regexes have no options, because Playwright rejects `RegexOptions.CultureInvariant`.
  - No `data-testid` was added to the UI.
- **Test 1.** `Tests/Access/SignInTests.SignIn_ShowsOrganizations`, marked `[Elementary]` and `[Area(Area.Access)]`, checks the heading, the user menu, and the links of the target's organization and project. It has no `[TestCase]` yet, because the cases do not exist in Testiny.
- **Profiles.**
  - `testsettings.json` is the section 7 shape with deviations:
    - the committed `Users:Pool` is empty; the pre-provisioned users go to `testsettings.local.json` or `UITESTS_*` variables;
    - `BrowserStack:PlaywrightVersion` is null;
    - `Target:EmailDomain` is added.
  - Review fix: `testsettings.mobile.json` now says what it does. It uses the Pixel 7 descriptor as is (412x839, device scale factor 2.625), while the TypeScript suite narrows it to 390x844. Both are below the `sm` breakpoint (640 px), so the layout is the same. No viewport override setting was added.

### 10. Documentation and `CLAUDE.md`

- **`CLAUDE.md`.**
  - The Tech Stack line names TUnit on Microsoft.Testing.Platform, Microsoft.Playwright and the `42.Testing.UI.Automation.*` libraries. Testiny and BrowserStack are left out until they exist.
  - The Platform table gets the `UI.Admin.AutomationTests/` row, and the Libraries list gets `Testing/UI.Automation.*/`.
  - The Build Commands get the `dotnet run` lines (all tests, the Elementary filter, the `mock` profile) and a note that `dotnet test src` leaves the project out.
  - The Testing section describes the project as the exception to the framework and folder rules. It covers MTP, `OutputType Exe` and no `Microsoft.NET.Test.Sdk`; D1, with the advice to add `Test="false"` to any new traversal that includes a TUnit project; `--treenode-filter` and `--test-parameter`; the skip without `Target:BaseUrl`; `[TestCase]`; and that the libraries reference `TUnit.Core` only.
  - Review fix: the exception is narrowed to "the framework and folder rules above (Bogus, Moq and the `IFileSystem` rule still apply)".
- **Project `README.md`.** Sections: "Run", "Configure" (the layers, the keys, the secrets, a sample `testsettings.local.json`, the profiles), "The mock target", "AuthKit checklist" and "Write a test".
  - Review fix: the commands run from the repository root, and the dev server is a `( … )` subshell started in a second terminal.
  - Its "keep traces private" note is covered under "Notable".
- **Not written in this phase.** `docs/Libraries/Testing/UI.Automation/readme.md`, a `package-readme.md` per library folder, and `docs/Platform/UI.Admin.AutomationTests/automation-tests.md` with its link from `docs/Platform/development.md`.
  - Section 11 lists the docs pages under phase 5, and the library surface is still growing.
  - `Directory.Build.props` packs a `package-readme.md` only when one exists, so `dotnet pack` works without them.
  - The stub spec `docs/Libraries/Testing/UI.Automation/specs/2026-10-31 UI Automation Libraries.md` came with the spec.

### Notable

- **TUnit facts that the spec had wrong or left open**, all verified in the probe against 1.76.45 / MTP 2.5.2:
  - A VSTest-mode `dotnet test` does not run an MTP project on the .NET 10 SDK. The spec expected `TestingPlatformDotnetTestSupport` to make it work (D1).
  - Static session and discovery hooks, base-class instance hooks and assembly-level receivers declared in a referenced library all run (D2). The spec listed this as a risk.
  - A keyed `NotInParallel` runs interleaved with the parallel tests, not after them. `Order` is honoured for keyed tests in this build (the fix of TUnit#7088 is in 1.76.45). `ExecutionPriority` only orders tests within a scheduling phase, so the spec's first fallback would not have worked (D3).
  - `RetryAttribute.Times` is a constructor constant (D4). A `Skip.Test` from `[Before(Test)]` reaches the retry predicate.
  - Registration-time code (`ShouldSkip`, the registered receivers) runs before `[Before(TestSession)]`.
  - `ITestRetryEventReceiver` is never called. Start and end receivers fire for every attempt, and `OnTestEnd` fires before the retry decision.
  - Tests skipped by an attribute run no `[Before(Test)]` or `[After(Test)]` hooks.
  - The source generator keeps only attributes related to TUnit types, so the engine-neutral `[TestCase]` must be read by reflection.
  - `--test-parameter` values are cut at their second `=`.
  - `TUnit.Core` adds its global usings to libraries too (CS0229 with `Assembly`).
  - Categories added in `OnTestDiscovered` are seen by `--treenode-filter`; those added in `OnTestRegistered` are not.
  - The console output of hooks is shown only with `--output Detailed`.
  - A run where every test is skipped exits 0. A filter that matches nothing exits 8 (`--ignore-exit-code 8` maps it to 0).
- **An existing project breaks `dotnet build src/Platform` and `dotnet build src`.** `src/Platform/Supervisor/Abstraction/test/Abstraction.Tests.csproj` targets `net6.0`, which `Microsoft.NET.Test.Sdk` 18.3.0 refuses. The build exits 1 before any project builds. The file is untouched (last changed in 2024). The Platform build evidence below used `-p:SuppressTfmSupportBuildErrors=true`, and the project needs a fix of its own.
- **Traces contain the signed-in storage state.** A quick Playwright check showed that every `trace.zip` holds the context's initial storage state (cookies and local storage, in `trace.trace`) and the session cookies (in `trace.network`). So a trace holds the pool user's tokens, though not the password, because the sign-in runs untraced. This comes with tracing a signed-in app. The README says to keep traces where test results are private; nothing in the code changed.
- **Noise on every run.**
  - The `mock` profile always shows the "MaxParallelTests × 2 > pool" warning.
  - The settings dump at session start is about 70 lines.
  - `console.log` is attached for passing tests too.

## Verification

Done-when of phase 1 (section 11), against what was run:

| Criterion | State |
|---|---|
| `dotnet test src` passes (skips) without configuration | Changed by D1. `dotnet test src` leaves the project out; it was not run end to end (the whole suite is too heavy). The evidence is the item metadata, the MTP error and the traversal probe. `dotnet run` without configuration: total 1, skipped 1 ("UITESTS_Target__BaseUrl is not configured; see src/Platform/UI.Admin.AutomationTests/README.md."), exit 0 |
| `SignIn_ShowsOrganizations` passes against the configured environment in Chromium, Firefox and WebKit, headless and headed, desktop and `mobile` | Against the mock target only, headless only. All six engine and device combinations passed before the review fixes; desktop Chromium and Pixel 7 Chromium passed again after them |
| `results.jsonl` is written | Yes: a run line (descriptor, planned tests with the categories `Area-Access` and `Elementary`), then one result line with its properties and artifacts |

Commands and checks:

| Check | Result |
|---|---|
| `dotnet build src/Libraries` | 0 errors, no warning from `UI.Automation.*`. There were 303 warnings with `--no-incremental` and 4 in the final incremental pass, all from existing projects |
| `dotnet build src/Platform` | Exit 1 before any project builds, from the Supervisor test project. With `-p:SuppressTfmSupportBuildErrors=true`: 0 errors, no warnings from the new projects |
| `-getItem:ProjectReference` on the traversals | `src/Platform/Directory.Build.proj` has 65 items and `src/Directory.Build.proj` 121; only the automation project has `Test=false` |
| `dotnet test` on the project itself | The MTP "Testing with VSTest target is no longer supported" error, exit 1 (the reason for D1) |
| Runtime unit tests | 130 / 130 (119 after part A, 120 after part B) |
| Runtime.TUnit unit tests | 25 / 25 (20 after part B) |
| `UITESTS_PROFILE=mock`, desktop | Chromium, Firefox and WebKit each 1 passed, exit 0. The Firefox run used `Execution:Trace=Always`, and its trace shows `http://localhost:5174/orgs?mock-scenario=default` |
| `UITESTS_PROFILE=mock,mobile` (Pixel 7) | Chromium, Firefox (with the expected `isMobile` warning) and WebKit each 1 passed, exit 0 |
| `--test-parameter Profile=mock --report-trx` | Passed; the TRX file shows the test as Passed with 1 attached file (`console.log`) |
| `--treenode-filter` | `/**[Category=Elementary]` and `/**[Category=Area-Access]` select the test. `/**[Category=Serial]` matches nothing and exits 8 |
| Provoked failure (`UITESTS_Target__Organization=does-not-exist`) | Exit 2. `failure.png` (34,238 B), `trace.zip` (392,618 B) and `console.log` were written to the test's folder and attached to TUnit's result. `results.jsonl` records Failed with the `PlaywrightException` |
| The same with `Execution:Retries=1` | 2 attempts; the artifacts of both are kept, the second under `attempt-2/`. `results.jsonl` has one result, attempt 2, pointing at the test's line in `SignInTests.cs` |
| Settings and start failures (unknown profile, empty pool, AuthKit without a password, a channel that is not installed) | Each exits 2 with a readable message, on the terminal and in the test result |
| Temporary probe of 9 tests (removed again) | All 9 results reached `results.jsonl`, including two attribute skips reported before the session started and a skip from inside a test body. `[Serial]` ran last and alone. Data-driven ids were stable, and `[TestCase]` links came from the method and from the class |
| Review-fix probes (removed again) | `[Repeat(1)]` gave two ids (`…/SignIn_ShowsOrganizations#c00600d2` and `#c1060265`), two results and two folders. A failing `CleanUpAsync` with `Retries=1` reported only attempt 2 (Failed). Without retries it reported attempt 1 (Failed) once. A failing test body with `Retries=1` reported only attempt 2 |
| `--list-tests json` | Standard output parses as JSON (`schemaVersion` 1); the skip message is on standard error |
| Final mock run after the fixes | Artifacts in `…/artifacts/<run>/Tests.Access.SignInTests/SignIn_ShowsOrganizations/`. The id is `42.Platform.UI.Admin.AutomationTests/_42.Platform.UI.Admin.AutomationTests.Tests.Access.SignInTests/SignIn_ShowsOrganizations` |

## Not verified and deferred

- **The AuthKit hosted page.** There is no deployed environment, so `AuthKitSignIn` has not run. Its steps (Email, Continue, Password, Sign in) use WorkOS' labels and are unverified. The refresh path (`RefreshAsync`, `IsSignInStorageItem`, `KeepSignInsAsync`) is covered by unit tests only.
- **Not run at all:** headed runs, Linux and the Playwright container image, and IDE server mode (`--server`, where the session logger stays silent).
- **Waits for a reporter (phase 3):**
  - the replay of `Reporting:ReplayFile`;
  - failing the session for `Reporting:UnmappedTests=Fail`.

  A replay needs a run that selects at least one test, because TUnit runs no session hooks when no test is selected.
- **A run where every test is skipped by an attribute starts no session.** TUnit runs `Before(TestSession)` only when a test executes, so no `results.jsonl` is written.
- **Skip mode skips the `UiTest` tests and the `Requires*` tests only.** A plain `[Test]` in the same assembly that needs the target must skip itself, through `Settings.IsConfigured`.
- **Session-start check of the default organization, project and membership (section 5.2): deferred to phase 2.** Today the only UI test is that check. The plan is a default interface member on `IUiAutomationSetup` (e.g. `OnSessionStartedAsync`), called from `UiAutomationSession.StartAsync`; its failure would be kept, so that every UI test fails with the message.
- **Hardening that was considered and deferred:**
  - `LeaseAnotherUserAsync` and `NewContextAsync` still work after `EndAsync`. That needs per-test timeouts or multi-user tests to matter. The fix is an ended flag in `TestScope`.
  - `[ConfiguredRetry]` also retries tests that cannot run because the session failed to start. Each extra attempt fails in microseconds, before any browser or lease, and changes no visible result.
  - A `Target:BaseUrl` with user info is stripped from the run properties but printed as is in the settings dump. Credentials in the URL are not a supported setup.
  - `LoginPage` navigates without `App.GotoAsync`, so it carries no mock scenario. Only `AuthKitSignIn` uses it, and only against deployed targets.
- **Later phases:** the BrowserStack provider and profiles (phase 4), `UI.Automation.Reporting.Testiny` (phase 3), the docs pages of section 10 (phase 5), and the `Maintenance` purge and the first `[Serial]` tests (phase 6).
