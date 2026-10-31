# Phase 5 of UI Automation Testing Project

Linear: [424-133](https://linear.app/424fun/issue/424-133/create-ui-automation-testing-project).

## Overview

Phase 5 is the fifth row of section 11 of [UI Automation Testing Project](../../../../../Platform/UI.Admin.AutomationTests/specs/2026-10-31%20UI%20Automation%20Testing%20Project.md), "CI". This review covers its library side:

- the session-start check of section 5.2, built as an optional check of the project's setup that every engine adapter runs (commit `aaaca9c7`);
- the package readmes and the library readme of section 10 (commit `baf92244`);
- the library fixes of the final review of the whole branch, which came after `baf92244`.

The Platform side (`PlatformTargetCheck`, the workflow, the docs pages, the workflow's review fixes and the verification of the whole phase) is in the [Platform review of Phase 5](../../../../../Platform/UI.Admin.AutomationTests/specs/reviews/2026-10-31%20Phase%205%20of%20UI%20Automation%20Testing%20Project.md). The stub spec of the libraries is [UI Automation Libraries](../2026-10-31%20UI%20Automation%20Libraries.md).

| Project | Passed (final pass) |
|---|---|
| `src/Libraries/Testing/UI.Automation.Runtime/test` | 220 (189 after Phase 4; 9 in `TargetCheckTests` with the check, and 22 with the review fixes) |
| `src/Libraries/Testing/UI.Automation.Runtime.TUnit/test` | 28 (25 after Phase 4) |
| `src/Libraries/Testing/UI.Automation.Reporting.Testiny/test` | 93 |

## What Was Done

### 3. `42.Testing.UI.Automation.Runtime`

#### 3.1 The target check

- **`IUiAutomationSetup.CheckTargetAsync(TargetCheckContext, CancellationToken)`**, a default interface method that does nothing.
  - `AutomationSessionOptions.FromSetup` takes it only when the setup's type (or a base class, or an explicit implementation) implements it. It looks at the type's interface map, so a setup with the default gets no check and no extra browser context.
- **`AutomationSessionOptions.TargetCheck`**, a `Func<TargetCheckContext, CancellationToken, Task>`.
  - `AutomationSession.StartAsync` runs it last: after the reporters are created and their settings validated, before any reporter starts. So a failed check creates no Testiny run.
  - It runs in a `TestScope` of its own, `TargetCheckContext.Test` (`TargetCheck.CheckTarget`). It is one more planned test of the browser provider (so a BrowserStack session per class closes once it is done) and is not reported.
  - Its screenshot, trace and console log follow the `Execution` policies.
- **`TargetCheckContext`**: the session, the settings, a leased user (any role), the signed-in context and page, and the scope for more.
- **`TargetCheckException`**: the check's own refusal.
  - Any exception of the check becomes `The target check failed: <message> The check's <artifacts> are in <directory>.` Another exception type is named in front of its message.
  - A cancellation of the start stays a cancellation.
- **Deviation:** section 5.2 says the assumptions "are checked once at session start". The engine-neutral check is the setup's to define; the Platform's checks one pool user (Platform review).

#### 3.2 Browser providers (review fixes)

- **Review fix (medium): BrowserStack sessions mask what is typed and the starting storage state, and send no Playwright logs.**
  - `BrowserStackSettings.MaskCommands` (default `sendType, sendPress, setHTTPCredentials, setStorageState`) becomes `browserstack.maskCommands`, normalized to `a, b`; an empty value sends none.
  - `BrowserStackSettings.PlaywrightLogs` (default `false`) becomes `browserstack.playwrightLogs`.
  - `AdditionalCapabilities` of the same name still replace them.
  - Why: the sign-in runs in BrowserStack's browser and every signed-in context starts there from the user's storage state (cookies, `workos:*` refresh tokens). BrowserStack's text and raw logs record command values unless masked, and its Playwright logs are never masked. Its [hide sensitive data](https://www.browserstack.com/docs/automate/playwright/hide-sensitive-data) page lists `sendType`, `sendPress`, `setHTTPCredentials`, `setStorageState`, `setGeolocation` and `setWebAuthnCredentials`, not `fill`, so the Platform's AuthKit flow now types (Platform review).
  - The comment of `AdditionalCapabilities` suggested `"browserstack.maskCommands": "setValues, setCookies"`, BrowserStack's Selenium names, which mask nothing in a Playwright session; the example is gone.
  - The capability name `browserstack.playwrightLogs` follows the `browserstack.*` form of the other log capabilities (BrowserStack's page names it `playwrightLogs`). **Not verified** on a real session.
- **Review fix (low): the access key is redacted also when it was set with surrounding whitespace.**
  - `BrowserStackCapabilities.Add` trims every value, so the caps URL carries the trimmed key, while the redactor was built from the raw value. A key pasted with a trailing newline never matched.
  - `SecretRedactor` now adds the trimmed form of every secret, in each of its encodings.
  - Test: `AcquireAsync_HidesAnAccessKeySetWithSurroundingWhitespace_ThatTheCapabilitiesTrim` (`\n`, `\r\n`, a space).
- **Review fix (low): with a session per class, a test is done only when its final report arrives** (`TestCompleted`), not at a release.
  - Before, `ReleaseAsync` marked the test done for every release, also of an attempt TUnit runs again. When that test was the last open one of its class, the class session closed, marked failed from attempt 1, and the retry opened a second session. A class that passed after a retry left a failed session in the dashboard and used an extra session.
  - Now a release only drops `Active`, stores the result and starts the idle timer. `AutomationSession.Report` (for every final report, after the release) and the target check call `TestCompleted`, which closes a complete session in the background; `DisposeAsync` waits for it.
  - The Phase 4 review's "a test is done when it released a browser", and its list of the idle close's cases ("a retry"), held only when other tests of the class were still to come; both hold now.
  - Tests: `SessionPerClass_KeepsTheSession_ForTheRetryOfTheLastOpenTestOfTheClass` and `SessionPerClass_KeepsTheSessionOfAOneTestClass_ForItsRetry`, which fail on the old rule. The other per-class tests end their final attempts as the session does (release, then `TestCompleted`). An internal `ClosingAsync` lets a test wait for the closes a `TestCompleted` started.
  - The finding that `SessionPerClass_MarksATestByItsLastAttempt` could not catch this (it kept another test's lease open) is answered by the two new tests.

#### 3.3 Settings, the session and the scope (review fixes)

- **Review fix (high): no secret leaves through a failed sign-in.**
  - Why: Playwright 1.63 logs `fill("<value>")` (and `elementHandle.type("<value>")`) in the action's call log once the locator resolves, and the .NET client appends the call log to the exception's message. A fill of the AuthKit password that timed out on a hidden, disabled or re-rendered field, or whose page closed, therefore held the clear-text password. `StorageStateCache` copied the message into its `SignInException` and kept the raw exception as the inner one. From there it reached `results.jsonl`, the Testiny comment, the BrowserStack session reason, TUnit's TRX and HTML reports and `run.log`. Through a failed target check it became every test's failure message.
  - `StorageStateCache` now throws `SignInException($"The sign-in flow {flow} failed for {user}: {type}: {message}")`, with the user's password and e-mail redacted from the message (`SecretRedactor`: as is, trimmed, URL-encoded, JSON-escaped) and **no inner exception**.
  - **Safety net, in the session:**
    - `SettingsRedaction.SecretValues(settings)` collects the value of every configuration key that `IsSecret` finds (the pool passwords, `BrowserStack:AccessKey`, `Reporting:Testiny:ApiKey`, a consumer's own) and the bound pool passwords and access key. `SettingsRedaction.CreateRedactor(settings, includeEmails)` replaces them, and with `includeEmails` the pool users' e-mails, by `***`.
    - `AutomationSession.Redact` (secrets and e-mails) is applied by `CreateReport` to the failure's message and stack trace (the `ToString` form, inner exceptions included), to a skip's reason and to the output. So `results.jsonl`, every reporter and BrowserStack's session reason get redacted text.
    - The target check's failure message is redacted, and an exception whose text holds a secret is not kept as the `TargetCheckException`'s inner exception.
    - The TUnit adapter's `UiAutomationUnavailableException` does the same, and its terminal log redacts the secrets (section 4).
  - TUnit's own TRX and HTML reports show the exception of a test body as thrown. A body does not type a pool password: a test that signs in itself (`SignedIn` false) has that exposure in its trace already, as documented.
  - Tests:
    - `GetAsync_ThrowsASignInException_WithoutTheEmailOrThePassword_AndKeepsAScreenshot`: a fake flow throws a Playwright-like call log with the e-mail and a password with `"`, a space, `&` and `+`, raw, JSON-escaped and URL-encoded;
    - `CreateReport_ShowsTheSecretsOfTheSettingsAndThePoolEmails_AsStars`;
    - `StartAsync_ShowsASecretInTheChecksFailure_AsStars_AndDropsTheExceptionThatHeldIt`;
    - `CreateRedactor_MasksEverySecretValue_AndThePoolEmailsOnlyWhenAsked`.
  - The library readme's promise (every report shows "never the e-mail or the password") now holds for failure texts too.
- **Review fix (medium): a start that the engine's hook timeout ends is Blocked, with its own message.**
  - `UiAutomationSettings.StartBudget` is `Users:LeaseTimeoutSeconds`, plus `BrowserStack:ConnectTimeoutSeconds` with the BrowserStack provider, plus 5 × `Execution:NavigationTimeoutMs`, plus a minute. That is 5.5 minutes locally and 10.5 minutes on BrowserStack with the defaults. The TUnit adapter raises its hook timeout to it (section 4).
  - `TestScopeOptions.TestCancellation`, the engine's cancellation of the test itself: when `BeginAsync`'s token is cancelled while this one is not, a budget of the engine ran out. The start then fails with the exception of the step that did not finish:
    - the lease: `UserLeaseException`, Blocked;
    - the browser: `BrowserUnavailableException`, Blocked;
    - the sign-in or the page: a `TimeoutException`.
    - Each message says `… before the engine's time for the start of the test ran out (e.g. its hook timeout)`. Unset, a cancelled start stays a cancelled test.
  - Tests: `BeginAsync_BlocksALeaseThatTheEnginesBudgetEnded`, `BeginAsync_BlocksABrowserThatTheEnginesBudgetEnded`, `BeginAsync_StaysCancelled_WhenTheTestItselfIsCancelled_OrNoTestCancellationIsGiven`, and `StartBudget_CoversTheLease_ABrowserStackConnection_AndTheFirstSignIn`.
- **Review fix (medium): `TestScope`'s sign-in hand-off has unit tests.**
  - The Phase 1 review says "The refresh path (`RefreshAsync`, `IsSignInStorageItem`, `KeepSignInsAsync`) is covered by unit tests only". That was not true for `KeepSignInsAsync`, nor for the Platform's `IsSignInStorageItem`: only `StorageStateCache.RefreshAsync` had tests. The only tests that ran a `TestScope` used `SignIn` `None`, where the cache is null.
  - New `TestScopeTests`, on a session with a fake sign-in flow and a mock browser:
    - a scope that ends signed in hands its rotated token to the user's next test, which starts from it;
    - one that ends signed out drops the state, and the next test signs in again;
    - a refresh that throws drops the state (a warning, no failure);
    - an extra context of a second leased user hands back its sign-in too, and both users are returned;
    - a browser that fails after the lease still returns the user;
    - plus the three budget tests above.
  - New `StorageStateCacheTests`: two scopes that ask at once sign in once, and the throw-away context drops video and storage state but keeps the other options.
  - `FakeSignInFlow` can now fail, hold and say signed out; `FakeBrowserProvider` can hold an acquisition.
  - The Platform's `AuthKitSignIn.IsSignedOutAsync` and `IsSignInStorageItem` still have no unit test: the Platform project has no unit test project (by design, section 5).

### 4. `42.Testing.UI.Automation.Runtime.TUnit`

- **`UiAutomationUnavailableException`** (`aaaca9c7`): a UI test cannot begin because the settings did not load or the session did not start (a failed target check included). `RetryPolicy.ShouldRetry`, the predicate of `[ConfiguredRetry]`, never retries it, so a failed check fails every test once, in 0 ms.
- **Review fix (medium): TUnit's hook timeout covers the start.**
  - `UiTest.BeginUiTest` is an instance `Before(Test)` hook. Without a `[Timeout]`, TUnit 1.76 gives it a token that is cancelled after `TimeoutSettings.DefaultHookTimeout`, 5 minutes. That is shorter than the lease, the BrowserStack connection (300 s alone) and the first sign-in together.
  - A start ended that way was Failed with "A task was canceled", not Blocked with the pool's or BrowserStack's message, and `BrowserStack:ConnectTimeoutSeconds` of 300 s or more could never apply.
  - `UiAutomationSession.ConfigureDiscovery` now raises `context.Settings.Timeouts.DefaultHookTimeout` to the settings' `StartBudget` when that is longer. TUnit reads it at execution time, so this also covers the session hook with the target check.
  - `BeginUiTest` passes `TestContext.Execution.CancellationToken` as `TestScopeOptions.TestCancellation`, so a start the hook timeout still ends is Blocked by its step.
  - A probe on TUnit 1.76.45 confirmed both parts (Platform review, "Verification"): the discovery hook's value applies to an instance hook, and the hook's token is cancelled while the test's is not.
- **Review fix (high, the safety net): the terminal log redacts the secrets.**
  - `ConsoleSessionLogger.Redact` is set to `SettingsRedaction.CreateRedactor(settings)` once the settings load (and reset with them), and every line `Write` prints goes through it. An error line prints the whole exception, so this covers a call log in any exception the session logs.
  - The pool e-mails stay visible in the log, as in the settings dump.
  - Tests: `ConsoleSessionLoggerTests` (the format of a warning and an error; `Lines` with and without a redactor).
- `UiAutomationSession.Require` redacts the message of its `UiAutomationUnavailableException` and keeps the inner exception only when its text holds no secret.

### 10. Documentation and `CLAUDE.md`

- `baf92244` wrote `docs/Libraries/Testing/UI.Automation/readme.md` and a `package-readme.md` for each library (each `.nupkg` packs its readme; `dotnet pack` checked in the scratch folder).
- Review fixes of the readme:
  - The readme names both review folders (Phases 1 and 2 next to the spec, the libraries' Phases 3 to 5 here); it said only "the reviews next to it".
  - The secrets paragraph describes the redaction of secret values; the `Users` section says the e-mail and the password are `***` in failure texts too.
  - The BrowserStack table has `MaskCommands` and `PlaywrightLogs`. The `AdditionalCapabilities` row no longer suggests the Selenium mask names. `ConnectTimeoutSeconds` points to the hook timeout.
  - The provider section gives the per-class rule (done = final report, a retry finds the session open), the trimmed key, the masking defaults, and BrowserStack's answer to wrong credentials (`Invalid username or password`).
  - The `UiTest` section explains the hook timeout and the start budget. The sign-in section covers the single sign-in under concurrency, the drop on a signed-out or failed refresh, and the redacted `SignInException`.
- The package readmes of `Runtime` and `Runtime.TUnit` mention the redaction, the masking and the hook timeout.

### Deviations from the spec

1. The session-start check is the setup's optional `CheckTargetAsync`, run by the runtime for every engine, not a fixed check of the default project and membership (5.2).
2. A run whose session is not available fails each UI test with `UiAutomationUnavailableException`, never retried (4).
3. Not in the spec, from the final review:
   - the redaction of secret values in reports, messages and the terminal log;
   - `BrowserStack:MaskCommands` and `BrowserStack:PlaywrightLogs`;
   - `UiAutomationSettings.StartBudget` with the raised hook timeout, and `TestScopeOptions.TestCancellation`.

### Review findings

The final review of the branch confirmed these findings in the libraries (the others are in the Platform review):

| Severity | Finding | Fix |
|---|---|---|
| High | A failed AuthKit sign-in could put the pool user's password in plain text into `run.log`, TRX, `results.jsonl`, Testiny and BrowserStack | Redacted `SignInException` without inner exception; the session-wide redaction of reports, check failures and the terminal log |
| Medium | BrowserStack session logs kept pool passwords and the signed-in storage state unmasked | `MaskCommands` and `PlaywrightLogs` defaults; the Platform types its sign-in |
| Medium | `BeginUiTest` ran under TUnit's 5-minute hook timeout, shorter than its lease, connect and sign-in budget | `StartBudget`, the raised hook timeout, `TestCancellation` |
| Medium | `SessionPerClass_MarksATestByItsLastAttempt` could not fail, and hid the retry bug below | Two retry tests |
| Medium | `TestScope`'s sign-in hand-off had no test, though the Phase 1 review said it had | `TestScopeTests`, more `StorageStateCacheTests` |
| Low | With a session per class, an attempt that will be retried counted as done, so the class session closed and was marked from the failed attempt | Done only at `TestCompleted` |
| Low | The redactor used the untrimmed access key while the caps URL carried the trimmed one | Trimmed forms of every secret |

## Verification

The builds, the unit tests and the mock runs of the whole phase are in the [Platform review](../../../../../Platform/UI.Admin.AutomationTests/specs/reviews/2026-10-31%20Phase%205%20of%20UI%20Automation%20Testing%20Project.md#verification). For the libraries:

- `dotnet build src/Libraries --no-incremental`: 0 errors, no warning from `UI.Automation.*`.
- Unit tests: Runtime 220, Runtime.TUnit 28, Reporting.Testiny 93, all passed.
- A mutation pass put back each of the old behaviours, and the new tests caught each one:
  - `MarkDone` in `ReleaseAsync`;
  - the unredacted `SignInException` with its inner exception;
  - the redactor without trimmed forms.
  - Seven tests failed, among them both retry tests, the sign-in test, the report test and the three trimmed-key cases.
- The mock suite on Chromium, Pixel 7, Firefox and WebKit passed with the redaction, the masking keys in the settings dump and the raised hook timeout.

## Not verified and deferred

- **BrowserStack:** `browserstack.maskCommands` and `browserstack.playwrightLogs` on a real session (the text logs showing `REDACTED VALUE`, no Playwright logs). The first session should be checked.
- **A hook timeout in a real run:** only the TUnit probe and the unit tests show the mapping; no run had a start that slow.
- **AuthKit:** the redacted `SignInException` against a real AuthKit failure.
- **Open, as in Phase 4:**
  - with a session per class, the open sessions can outnumber `MaxParallelSessions`;
  - a retry that comes after `IdleCloseAfter` opens a new session, which the class's mark then splits over two sessions.
