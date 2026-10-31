# Phase 4 of UI Automation Testing Project

Linear: [424-133](https://linear.app/424fun/issue/424-133/create-ui-automation-testing-project).

## Overview

Phase 4 covers the fourth row of section 11 of [UI Automation Testing Project](../../../../../Platform/UI.Admin.AutomationTests/specs/2026-10-31%20UI%20Automation%20Testing%20Project.md), "BrowserStack": the `BrowserStackBrowserProvider` of section 3.2 with its session naming, pass/fail status and session limit, the BrowserStack settings of section 7, and the `browserstack-win-chrome` and `browserstack-mac-safari` profiles. The provider lives in `UI.Automation.Runtime`, so this review sits next to the stub spec [UI Automation Libraries](../2026-10-31%20UI%20Automation%20Libraries.md). It also covers the Platform project's part (the two profiles, `testsettings.json` and the README), so `docs/Platform/UI.Admin.AutomationTests/specs/reviews/` has no Phase 4 review. The phase changes one decision of section 1: `Microsoft.Playwright` is pinned to 1.63.0 instead of 1.64.1, because BrowserStack does not run Playwright 1.64 yet. There is no BrowserStack account, so the provider was checked against a local `npx playwright run-server`. That server speaks the same protocol and ignores the capabilities, and it ran with the admin UI's mock build (`npm run dev:mock`) as the target. The row's done-when needs BrowserStack itself and is not met yet (see "Verification").

| Commit | Content |
|---|---|
| `3666bd8a` | The provider and its helpers in `UI.Automation.Runtime/src/Browsers/`, the `IBrowserProvider`, `AutomationSession` and settings changes, three new test classes and three new fakes, the Playwright pin in `Directory.Packages.props`, the two profiles, `testsettings.json` and the README (29 files) |
| `27fd74ea` (the Phase 3 commit) | The Phase 4 parts in files that Phase 3 also changed: `TestRunProperties.Os`, the `os` of the Browser line in `TestinyResultComment` with `TestinyResultCommentTests`, and the BrowserStack wording in `CLAUDE.md` |

Phases 3 and 4 were reviewed together. The review confirmed 16 findings and rejected 11. Eight of the confirmed findings concern this phase; two are duplicates, so they needed 7 fixes. The other eight are Phase 3's, in the Testiny reporter. All were fixed before the commits. This phase's fixes are marked "review fix" below and listed under "Review findings".

| Project | Passed (final pass) |
|---|---|
| `src/Libraries/Testing/UI.Automation.Runtime/test` | 189 (131 after Phase 3) |
| `src/Libraries/Testing/UI.Automation.Runtime.TUnit/test` | 25 |
| `src/Libraries/Testing/UI.Automation.Reporting.Testiny/test` | 93 (one from this phase) |
| `src/Platform/UI.Admin.AutomationTests/src` (`mock`, the BrowserStack provider against a local Playwright 1.63 server) | 6 |

## What Was Done

### 1. Overview and dependency graph

- **Review fix (high): `Microsoft.Playwright` is pinned to 1.63.0** in `Directory.Packages.props`. Section 1 and Phase 1 had 1.64.1.
  - Why: Playwright's server refuses a client with another major.minor version (HTTP 428, "Playwright version mismatch"). [BrowserStack's browsers and OS table](https://www.browserstack.com/docs/automate/playwright/browsers-and-os), checked on 2026-10-31, lists Playwright up to 1.63, so a 1.64 client could not start any session.
  - `BrowserStack:PlaywrightVersion` stays unset by default. BrowserStack then matches `client.playwrightVersion`, which `AutomationSession.PlaywrightVersion` reads from the `Microsoft.Playwright` assembly (1.63.0).
  - A connection refused for its version fails with a message that names the client's version and links the table (`VersionHint` in the provider).
  - The version examples in comments and tests now say 1.63.0. The README explains the pin and names the CI image `mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`; section 9 has `v1.64.x-noble`, so Phase 5 needs the new tag.
  - The Platform project references `TUnit`, not `TUnit.Playwright` (the package section 1 says depends on Playwright 1.64.0), so nothing else asks for 1.64.
  - Local runs need the 1.63 browsers (`playwright.ps1 install`).
- **No new project or package.** The provider is part of `UI.Automation.Runtime`, and `42.mono.slnx` is unchanged.
- **How the commits are split.** Phases 3 and 4 were built in one working tree and committed as two commits, file by file.
  - Some Phase 4 changes are in `27fd74ea`: the files are listed in the table above.
  - Some Phase 3 changes are in `3666bd8a`:
    - the logger that `AutomationSession` passes to `ReporterSelection.CreateAsync`;
    - the test `StartAsync_HandsTheProvidersTheRunsConfiguration_AndTheConsumersServices`;
    - the README's "Report to Testiny" section.
  - So `27fd74ea` alone does not compile the Runtime: its `ReporterSelection.CreateAsync` requires a logger that its caller passes only in `3666bd8a`. The two commits build together. This was found by reading the commits, not by building `27fd74ea`.

### 3. `42.Testing.UI.Automation.Runtime`

#### 3.2 Browser providers

- **New types** in `src/Libraries/Testing/UI.Automation.Runtime/src/Browsers/`:
  - `BrowserStackBrowserProvider` (public, sealed): `StartAsync(BrowserProviderContext, ct)`, `Build`, `IdleCloseAfter` and the `IBrowserProvider` members.
  - `BrowserStackCapabilities` (static): the capabilities, the endpoint URL, the build and session names, and the idle timeout.
  - `BrowserStackSessionStatus` (record): the `setSessionStatus` payload and its reason.
  - `BrowserProviderContext` (record): what a provider starts with.
  - `IRemoteBrowserConnector` (internal) and `PlaywrightBrowserConnector` (the real one): the seam that the unit tests fill with `FakeRemoteBrowserConnector`.
  - `SecretRedactor` (internal).
- **Changed types.**
  - `IBrowserProvider` gains a default member, `void TestCompleted(TestIdentity test)`, which does nothing (review fix; see "Session per class").
  - `BrowserProviderFactory` starts the BrowserStack provider instead of throwing `NotSupportedException`. The new `CreateAsync(BrowserProviderContext, ct)` is the main overload. `CreateAsync(settings, logger, ct)` stays and builds its context with `BrowserProviderContext.ForSettings`.
  - API change: `AutomationSessionOptions.BrowserProviderFactory` is now `Func<BrowserProviderContext, CancellationToken, Task<IBrowserProvider>>`; it took the settings and a logger before. The context carries:
    - the settings;
    - the run's `RunNameValues`, which `BrowserStack:Build` uses;
    - the planned tests, which a session per class needs;
    - the session's logger;
    - review fix: the session's `TimeProvider`, which the idle close uses.
  - `AutomationSession`:
    - It builds the run's `RunNameValues` with the new `RunNameValues.For(settings, runId, localStartedAt, environment)` before it starts the provider, and `CreateRun` uses the same values.
    - `Report(report)` hands the report to the dispatcher, then calls `BrowserProvider.TestCompleted(report.Test)`. When the provider throws, it logs a warning.
- **Connection.**
  - Nothing connects at start: `StartAsync` starts only the Playwright driver.
  - `AcquireAsync` connects through the `BrowserType.ConnectAsync` of the engine that `BrowserStack:Browser` maps to (`BrowserStackSettings.Browsers`):
    - `chrome`, `edge` and `playwright-chromium` run on Chromium;
    - `playwright-firefox` runs on Firefox;
    - `playwright-webkit` runs on WebKit.
  - The connection goes to `BrowserStack:Endpoint` plus `caps=<the URL-encoded JSON>`. Its timeout is `ConnectTimeoutSeconds`, and it applies `Browser:SlowMoMs`. `Browser:Engine` and `Browser:Channel` are not used: `UiAutomationSettings.EffectiveEngine` follows `BrowserStack:Browser`.
  - When a connection fails, the test gets a `BrowserUnavailableException` and is Blocked.
    - The message has the session name, the endpoint without its query, the exception type and the redacted message.
    - The Playwright exception is not attached as the inner exception, because it may hold the caps URL.
  - Playwright's `ConnectAsync` takes no token. When a test stops waiting, `PlaywrightBrowserConnector` closes the connection once it opens, so no session is left behind.
- **Capabilities (`BrowserStackCapabilities.Create`).**
  - The order: `browser`, `browser_version`, `os`, `os_version`, `resolution`, `project`, `build`, `buildTag`, `name`, `browserstack.username`, `browserstack.accessKey`, `browserstack.local`, `browserstack.localIdentifier`, `browserstack.playwrightVersion`, `client.playwrightVersion`, `browserstack.console`, `browserstack.networkLogs`, `browserstack.debug` and `browserstack.video`.
  - Then come the entries of `BrowserStack:AdditionalCapabilities`. Each one replaces the capability of the same name, but never the credentials.
  - Every value is a string, `"true"` and `"false"` included, as in BrowserStack's C# sample. Values that are not set are left out.
  - `browser_version` is sent for `chrome` and `edge` only. `browserstack.playwrightVersion` is sent only when it is set. `client.playwrightVersion` is always sent.
  - `ToJson` writes the object with `Utf8JsonWriter`, and `CreateEndpoint` URL-encodes it with `Uri.EscapeDataString`. `project`, `build` and `name` are cut at 255 characters (`MaxNameLength`).
- **Names.**
  - A session per test is named `<Class>.<DisplayName>`, with the class name stripped of its namespace, e.g. `SignInTests.SignIn_ShowsOrganizations`. The display name keeps the rows of a data-driven test apart. A session per class is named `<Class>`. Both come from `BrowserStackCapabilities.SessionName`.
  - The name becomes the lease's `RemoteSessionName`. `TestScope` already puts that into the report as `remoteSession` (Phase 1), and the Testiny comment shows it.
  - The build is `BrowserStack:Build`, a pattern that takes the placeholders of `Reporting:RunName`. When it is unset (`ResolveBuild`):
    - `{Branch} #{BuildNumber}` in a GitHub Actions job (`CiBuild`);
    - `local {Machine} {Date}` elsewhere (`LocalBuild`).
- **Pass/fail mark (`BrowserStackSessionStatus`).**
  - With a session per test, `ReleaseAsync` marks the session and closes its browser. `ForTest` decides the mark:
    - passed for a passed test;
    - failed for a failed test, with its message as the `reason`;
    - failed for Timeout, Blocked and Cancelled, with `<Outcome>: <message>`;
    - no mark for a skipped test or a missing result.
  - The reason is redacted, put on one line and cut to 255 characters with an ellipsis (`MaxReasonLength`). BrowserStack documents no limit. The JSON writer escapes it.
  - The payload is `browserstack_executor: {"action":"setSessionStatus","arguments":{"status":…,"reason":…}}`. It is sent with `page.EvaluateAsync("_ => {}", payload)`, with a 30 s timeout. The test's context is already closed when the browser is released, so the mark opens a new context and page when no page is left.
  - When a mark or a close fails, the provider logs a warning. `ReleaseAsync` never throws.
- **Session per class (`SessionPerTest` `false`).**
  - The tests of a class share one session, each test in its own context, and tests that start together connect only once. The connection takes no token, so one test's cancellation does not stop the others.
  - The mark comes from the last attempt of each test (`ForSession`):
    - failed when one test neither passed nor was skipped; the reason is `<n> of <m> test(s) failed; <first>: <message>`;
    - otherwise passed when one test passed;
    - otherwise no mark.
  - Review fix: the session closes once no test holds it and every planned test of its class is done. A test is done when it released a browser or when its final report arrives (`TestCompleted`). So a test skipped by an attribute, a test blocked before its browser and a failed connection all count. The planned tests are `AutomationSessionOptions.PlannedTests`, which the TUnit adapter already filled.
  - Review fix: sometimes no test holds the session while tests of its class are still to come, e.g. a `[Serial]` test, a retry, or a test that never starts. The session then closes, marked, after `IdleCloseAfter`, which is two thirds of BrowserStack's idle timeout (`BrowserStackCapabilities.IdleTimeout`):
    - 60 s by default (`DefaultIdleTimeoutSeconds` 90);
    - 2/3 of `browserstack.idleTimeout` when `AdditionalCapabilities` sets it.
    A test that joins in time cancels the timer, and a later test opens a new session.
  - A session that BrowserStack ended (it is disconnected when a test joins) is replaced, with a warning. Review fix: the replaced session closes when its last test leaves it; before, it stayed open until dispose.
  - A failed connection is dropped, so the next test of the class connects again. A class missing from the planned tests keeps its session until dispose.
  - Not done, by the fixer's choice: tests skipped by an attribute stay in the planned list. `TestCompleted` and the idle close already cover them, and it is unverified whether TUnit sets the skip reason by `Before(TestSession)`.
- **`DisposeAsync`**:
  - marks and closes every class session from the results it has;
  - closes the sessions of unreleased tests without a mark;
  - waits for every close, then stops the driver.
- **Properties.**
  - The run gets `provider` (`BrowserStack`), `engine`, `channel` (the value of `BrowserStack:Browser`), the new `os` (`<Os> <OsVersion>`, e.g. "Windows 11"), and `browserVersion`. For `chrome` and `edge` that is the version asked for, e.g. `latest`.
  - Each lease has the real `browserVersion` of its browser.
  - `RunDescriptions` has a new "OS" row, which appears in the run description in Testiny.
- **Redaction (`SecretRedactor`).**
  - The access key travels in the caps URL, so every message and exception of the provider shows it as `***`.
  - The redaction covers the raw, URL-encoded (`%20` and `+`) and JSON-escaped forms, and their combinations.
  - Playwright 1.63 and 1.64 already leave the query out of their call log. The redaction is for an error of the grid or of another Playwright version that echoes the URL. Playwright's own `DEBUG=pw:*` output may still print it, which the README notes.
- **Start log.**
  - One line: the build, the browser and its version, the OS, BrowserStack Local, the session scope with the idle close, the session limit, the client's and BrowserStack's Playwright versions, and the endpoint without its query.
  - Section 12 wants three versions logged at start; the third, the CI image tag, comes with Phase 5.
- **Review fix: the chrome/edge rule is written once.** `BrowserStackSettings.GetBrowserName()`, `UsesBrowserVersion()` and `GetRequestedBrowserVersion()` serve the capabilities, the properties and the start log. The start log no longer prints an empty version.

### 4. `42.Testing.UI.Automation.Runtime.TUnit`

#### 4.2 Attributes

- **`[BrowserStackSessionLimit]`** is unchanged since Phase 1, and `PlatformAutomationSetup` applies it.
  - With `Browser:Provider` `BrowserStack`, it limits the tests that run at once to `BrowserStack:MaxParallelSessions`. That is the same value as the provider's `MaxParallelSessions`.
  - It was not exercised: every run had 4 parallel tests and a limit of 5, and the attribute has no unit test.

### 7. Configuration shape

- **`BrowserStackSettings`** (`Settings/BrowserStackSettings.cs`) has the keys of section 7, plus:
  - `Endpoint` (`DefaultEndpoint`, `wss://cdp.browserstack.com/playwright`): any `ws://` or `wss://` Playwright server. The local run-server check uses it.
  - `BuildTag` (`buildTag`).
  - `ConnectTimeoutSeconds` (300): how long a connection may take, waiting in BrowserStack's queue included.
  - `AdditionalCapabilities`: more capabilities as strings, e.g. `browserstack.idleTimeout`, `browserstack.maskCommands` or `browserstack.geoLocation`.
  - `ConsoleLevels`: the values `Console` accepts.
- **Changed keys.**
  - `Build` takes the placeholders of `Reporting:RunName`, with the defaults above. Phase 1's comment said "the run name when unset".
  - `BrowserVersion` is nullable and applies to `chrome` and `edge` only.
  - `PlaywrightVersion` is still null by default (the Phase 1 deviation from section 7's `"1.64.0"`), and it is not sent while it is null.
- **Validation (`SettingsValidator`).** With the BrowserStack provider, these checks join Phase 1's (username, access key, a known browser, `MaxParallelSessions` ≥ 1):
  - `Endpoint` is an absolute `ws` or `wss` URL without a `caps` parameter. The message shows the endpoint without its query (review fix: through `BrowserStackCapabilities.Describe`, `Uri.UriSchemeWs` and `Uri.UriSchemeWss`).
  - `Os` and `OsVersion` are required.
  - `Console` is one of the levels.
  - `Build` is a valid pattern (`RunNameFormatter.Validate`).
  - `AdditionalCapabilities` has no empty key and does not set the credentials.
  - `ConnectTimeoutSeconds` ≥ 1.
- **`testsettings.json`** gets `Endpoint`, `BuildTag` (null), `ConnectTimeoutSeconds` 300 and `AdditionalCapabilities` `{}`.
- **Profiles** (in `src/Platform/UI.Admin.AutomationTests/src/`). Both set `Browser:Provider` `BrowserStack` and `Resolution` `1920x1080`.
  - `testsettings.browserstack-win-chrome.json`: `Windows` `11`, `chrome` `latest`.
  - `testsettings.browserstack-mac-safari.json`: `OS X` `Tahoe`, `playwright-webkit`, `BrowserVersion` null.
    - Deviation: BrowserStack has no branded Safari for Playwright, so the spec's "macOS Safari" is Playwright's WebKit build.
    - Tahoe (macOS 26) is the newest macOS that BrowserStack lists for Playwright 1.59 to 1.63. "Golden Gate" is listed for Playwright 1.57 and older only.
  - Neither profile has a target or credentials. They combine with a target from `testsettings.local.json`, and with `mobile` or `reporting-testiny`.
- **Secrets.**
  - `BrowserStack:Username` and `BrowserStack:AccessKey` go in the user secrets or the `UITESTS_BrowserStack__*` variables.
  - The settings dump shows `BrowserStack:AccessKey = ***` (the Phase 1 redaction).

### 10. Documentation and `CLAUDE.md`

- **The project `README.md` gets a new section, "Run on BrowserStack":**
  - the two profiles;
  - the credentials, and why `DEBUG=pw:*` should stay off with real credentials;
  - the commands, including `mobile` emulation and `edge`;
  - BrowserStack Local, which the provider does not start;
  - the key table;
  - the Playwright pin and the CI image;
  - what the Automate dashboard shows (project, build, sessions, marks, the remote session in the Testiny comment);
  - the session limit and the session-per-class lifecycle;
  - a check without an account against `npx -y playwright@1.63.0 run-server`.
- **Other README changes.**
  - The Configure table gets a `BrowserStack:*` row, and the `Browser:Provider` row now names `BrowserStack`.
  - The secrets list and the list of profiles are updated.
  - Review fix: the README's list of the rows in the Testiny run description now has channel, OS and run id.
- **`CLAUDE.md`** (in `27fd74ea`):
  - the Tech Stack line now says "with Testiny test run reporting and BrowserStack browsers";
  - the Libraries entry now names the "browser providers (local Playwright, BrowserStack over its Playwright endpoint)".
- **Not written in this phase:** the docs pages of section 10, which belong to Phase 5.

### Deviations from the spec

- **Playwright 1.63.0, not 1.64.1** (section 1), and so the CI image `v1.63.0-noble`, not `v1.64.x-noble` (section 9).
- **`browserstack.playwrightVersion`** is not sent while it is unset; `client.playwrightVersion` always is. BrowserStack then follows the client's version, so the two cannot drift apart. (The implementer chose this while the client was still 1.64.1, when sending that version would have asked for one BrowserStack does not have.)
- **Capabilities.**
  - All values are strings, and `browser_version` is sent for `chrome` and `edge` only.
  - `browserstack.maskCommands`, which the spec's BrowserStack facts (Current State) list, has no key of its own: it goes through `AdditionalCapabilities`.
- **New settings:** `Endpoint`, `BuildTag`, `ConnectTimeoutSeconds` and `AdditionalCapabilities`. `Build` takes placeholders and has defaults, and `BrowserVersion` is nullable.
- **New API surface** that the spec does not describe: `BrowserProviderContext`, with its `TimeProvider`; the factory delegate of `AutomationSessionOptions`; `IBrowserProvider.TestCompleted`; `RunNameValues.For`.
- **Session names.** The spec's `<Class>.<Method>` is the class without its namespace plus the display name, cut at 255 characters.
- **Properties.**
  - `channel` holds `BrowserStack:Browser`, and the new `os` property holds the OS.
  - The run's `browserVersion` is the version asked for; each test's report has the real one.
- **The mark.**
  - Skipped tests are not marked.
  - Timeout, Blocked and Cancelled are marked failed, with the outcome in the reason.
  - The reason is redacted, on one line and at most 255 characters.
  - The mark goes through a new context and page.
- **Session per class.** The spec says only "one session per class". The closing rules are this phase's design: when the class's tests are done, the idle close, replacement, and the last attempt deciding the mark.
- **Nothing connects at start**, so wrong credentials show up on the first test, as a Blocked test. With the Local provider, a browser that does not launch fails the session start instead.
- **The mac profile** is `playwright-webkit` on OS X Tahoe.

### Review findings

Confirmed and fixed for this phase (8 findings, 7 fixes; the lenses were correctness, BrowserStack and spec, and conventions):

| Finding | Severity | Fix |
|---|---|---|
| The 1.64.1 Playwright client cannot connect to BrowserStack, whose newest server is 1.63 | high | Pinned to 1.63.0; a version message on connect; README, comments and test examples (section 1) |
| With a session per class, the session waits for planned tests that never take a browser or run much later (two findings) | medium, medium | `IBrowserProvider.TestCompleted` from `AutomationSession.Report`; the idle close after `IdleCloseAfter` (3.2) |
| A replaced class session may never close before dispose | low | It closes when its last test leaves it (3.2) |
| The rule that `BrowserVersion` applies to chrome and edge only is written in three places | low | `GetBrowserName()`, `UsesBrowserVersion()`, `GetRequestedBrowserVersion()` |
| The validator strips the endpoint's query by hand | low | `BrowserStackCapabilities.Describe`, `Uri.UriSchemeWs` and `Uri.UriSchemeWss` |
| The README's list of the run description leaves out the rows this phase added | low | Channel, OS and run id |
| The connector's header comment says `ConnectAsync` takes no token, but the method takes one | low | It now names Playwright's `BrowserType.ConnectAsync`; `SecretRedactor`'s comment now names 1.63 |

Rejected for this phase (5):

- **Marking and closing a remote session have no overall timeout, so `ReleaseAsync` and `DisposeAsync` could hang.** The finding was wrong about `CloseAsync`. The mark's `EvaluateAsync` is bounded by 30 s, and the `NewContextAsync`/`NewPageAsync` before it is unbounded. That gap is real on paper, but the proposed fix would not stop the hang it described.
- **With a session per class, the open sessions are not limited to `MaxParallelSessions`.** The trigger the finding described does not hold for TUnit. The mode is opt-in, and the README documents the behaviour: BrowserStack queues a session over the limit.
- **A passing BrowserStack run leaves Testiny with no BrowserStack build or session.** The code follows the spec: each report has `remoteSession`, and the Testiny comment names it. Under the default `Comments` `Failures`, only failed results get that comment. The suggested fix would not have closed the gap.
- **The `<Class>.<DisplayName>` short name exists three times**: `ReportingDispatcher.Name`, `TestinyResultComment.ShortName` and `BrowserStackCapabilities.SessionName`. The copies exist; a shared helper was not judged worth it now.
- **Three truncation helpers end a cut text differently**, among them `BrowserStackSessionStatus.Cut`. They do different jobs on purpose.

The other six rejected findings, and the eight confirmed Testiny findings, belong to Phase 3.

## Verification

Done-when of phase 4 (section 11), against what was run:

| Criterion | State |
|---|---|
| Elementary set green on BrowserStack Windows 11 Chrome latest and macOS Safari | Not run: there is no BrowserStack account. The Elementary set is green through the provider against a local Playwright server: 6/6 with a session per test and 6/6 with a session per class. The `browserstack-mac-safari` profile ran in WebKit |
| Sessions visible with pass/fail | Only seen in the protocol: a logging WebSocket proxy recorded one `evaluate("_ => {}", "browserstack_executor: …")` per session, with `passed` or `failed` and the reason. BrowserStack's dashboard was not seen |
| The Testiny run carries the BrowserStack session names | Each test's report has `remoteSession` (seen in `results.jsonl`), and `TestinyResultComment` shows it (`TestinyResultCommentTests`). Under the default `Comments` `Failures`, only failed results get that comment. No run went to Testiny with BrowserStack |

Before the review fixes (implementer; client 1.64.1, `npx playwright run-server --port 3999` 1.64.0, behind the logging proxy):

| Check | Result |
|---|---|
| `dotnet build src/Libraries --no-incremental` | 0 errors, 303 warnings, as many as in Phase 3, none from `UI.Automation.*`. The Platform project: 0 errors, 0 warnings |
| Unit tests | Runtime 180, Runtime.TUnit 25, Reporting.Testiny 85 |
| A session per test | 6/6, exit 0: 6 connections named after the tests (e.g. `SignInTests.SignIn_ShowsOrganizations`), 6 `passed` marks on a new context, 6 disconnects |
| A session per class | 6/6, exit 0: 6 sessions named after their classes. Each was marked passed and closed when the last test of its class released it, not at the end of the run |
| Throwaway class of 3 tests, one failing on purpose (deleted afterwards) | Per class: one shared session, marked `failed` with `1 of 3 test(s) failed; BrowserStackProbeTests.Fails_OnPurpose: Forced failure: "quoted" text, a second line…`. Per test: 3 sessions, 2 passed and 1 failed. `failure.png` and `trace.zip` came back from the remote browser, and `remoteSession` is in `results.jsonl` |
| Endpoint on a closed port | Blocked with `The BrowserStack session '…' did not start at ws://localhost:3997/: PlaywrightException: …ECONNREFUSED`, and no inner exception |
| `playwright-webkit` and `playwright-firefox` | 6/6 each, on the server's WebKit and Firefox builds |
| Local provider (regression) | 6/6 |
| Secrets | The dummy access key appears in no output or error log, no `results.jsonl`, no TUnit HTML or JSON report, no `console.log` and no unzipped `trace.zip`. The settings dump shows `BrowserStack:AccessKey = ***` |

After the review fixes (fixer; client 1.63.0, `npx -y playwright@1.63.0 run-server` behind the logging proxy):

| Check | Result |
|---|---|
| `dotnet build src/Libraries` | 0 errors. The only warnings beyond the earlier Phase 4 build are `NU1701` restore warnings for two packages of the unrelated `Roslyn/Compose` project. Clean rebuilds of the three `UI.Automation` test projects and of the Platform project: 0 warnings, 0 errors |
| Unit tests | Runtime 189, Runtime.TUnit 25, Reporting.Testiny 93. The new Runtime tests cover the class-session lifecycle, the idle close, closing the replaced session, `TestCompleted` and the version message |
| `UITESTS_PROFILE=mock`, local provider, Chromium headless | 6/6, exit 0, with the usual single pool warning |
| Scratch probe class: a pass, a forced failure, and a test skipped by `[RequiresEngine]` (deleted afterwards) | It joined the 6 Elementary tests, 9 tests in all, for the next three runs |
| A session per test (run `20261031-214107-p1ws`) | 7 passed, 1 failed (the probe's forced failure), 1 skipped: 8 sessions, all marked |
| A session per class (run `20261031-214131-8jtb`) | The same 9 tests in 7 sessions. The probe class's session was marked "1 of 2 test(s) failed" and closed at 28.5 s, while another class ran on until 31.7 s; before the fix it would have closed only at dispose |
| `browserstack-mac-safari` with a session per class, WebKit (run `20261031-214338-91gm`) | 8 passed, 1 failed (the probe), every class's session marked |
| Against a 1.64.0 server (run `20261031-214256-eg0o`) | `SignIn_ShowsOrganizations` Blocked: `… did not start at ws://localhost:3998/: the server does not run the Playwright version of this client, 1.63.0 (both sides need the same major.minor); pin Microsoft.Playwright to a version BrowserStack lists (…) … PlaywrightException: WebSocket error: ws://localhost:3998/ 428 Precondition Required` |
| Secrets | The access key appears 0 times in the logs |
| Clean-up | The run-servers and the proxy were stopped, and the mock dev server on 5174 still answers |

The fixer's runs against the fake Testiny server after the review fixes check Phase 3's fixes and are Phase 3's evidence.

## Not verified and deferred

- **Nothing ran on BrowserStack.** Without an account, these are unconfirmed:
  - that BrowserStack serves the 1.63.0 client: its table lists 1.63, but no session was opened;
  - that it accepts the capabilities as written: the string values, `os_version` `Tahoe`, `resolution`, `buildTag`, `project`, `build` and `name`, and which characters it refuses in names;
  - that `setSessionStatus` sent through a new context's page marks the session in the dashboard, and how long a reason may be;
  - the session limits:
    - queueing above the plan's parallel limit;
    - BrowserStack's idle timeout ending a held class session, and the 90 s default that `IdleCloseAfter` assumes;
    - the 300 s connect timeout;
  - BrowserStack Local, Edge, and device emulation (`mobile`) on remote browsers;
  - traces, videos and screenshots coming back from BrowserStack;
  - the format of BrowserStack's error for wrong credentials, and whether the redaction catches it;
  - the done-when of the row: the Elementary set green on Windows 11 Chrome and macOS Safari, the sessions with their marks in the dashboard, and the Testiny run carrying the session names.
- **`[BrowserStackSessionLimit]`** has neither run below the parallelism nor any unit test.
- **Open after the rejected findings:**
  - opening the mark's context and page has no timeout of its own;
  - with a session per class, the open sessions can outnumber `MaxParallelSessions`;
  - passing results carry the session name in Testiny only with `Reporting:Testiny:Comments` `All`.
- **Side effect on the machine:** installing the Playwright 1.63 browsers (Chromium and WebKit) removed the 1.64 builds from the shared browser cache. The admin UI's TypeScript e2e suite needs `npx playwright install` before its next local run.
- **Later phases:**
  - Phase 5: the CI workflow on `mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`, with the BrowserStack secrets and BrowserStack Local (`browserstack/github-actions`), plus the docs pages of section 10;
  - whenever BrowserStack's table lists 1.64 or later: raising the Playwright pin.
