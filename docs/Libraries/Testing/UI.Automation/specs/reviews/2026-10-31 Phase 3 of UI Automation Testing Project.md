# Phase 3 of UI Automation Testing Project

Linear: [424-133](https://linear.app/424fun/issue/424-133/create-ui-automation-testing-project).

## Overview

Phase 3 covers the third row of section 11 of [UI Automation Testing Project](../../../../../Platform/UI.Admin.AutomationTests/specs/2026-10-31%20UI%20Automation%20Testing%20Project.md), "Testiny reporting". It adds the fourth library of section 1, `42.Testing.UI.Automation.Reporting.Testiny` (`Mode = TestRun`, section 6), with its client, reporter, provider and unit test project. It also runs the runtime's dispatcher (batching, back-off, replay) against a real HTTP reporter for the first time, and prints the run URL on the terminal. The library has no spec of its own: the [stub spec](../2026-10-31%20UI%20Automation%20Libraries.md) points to the Platform document, and this library-phase review sits next to the stub. The phase also needed two small runtime changes and wires the reporter into the Platform project (`PlatformAutomationSetup`, the `reporting-testiny` profile, the README). This review covers those changes too, so `docs/Platform/UI.Admin.AutomationTests/specs/reviews/` has no review for phase 3. The code is commit `27fd74ea`. There is no Testiny account yet, so every end-to-end check ran the admin UI's mock suite (`npm run dev:mock`, the `mock` profile) against a fake Testiny server on `127.0.0.1:5399`. The done-when of section 11 holds on that fake, not yet on a scratch Testiny project. The committed tests still have no `[TestCase]` ids, so a run against a real project stays empty until their cases exist.

| Commit | Content |
|---|---|
| `27fd74ea` | The library (33 files) and its unit test project (11 files); `ReporterServices` and `ReporterSelection` in the Runtime, `FakeReporterProvider`; `TestRunProperties.Os`; the Platform project reference, `PlatformAutomationSetup` and `testsettings.reporting-testiny.json`; `42.mono.slnx`, `CLAUDE.md` (53 files) |
| `3666bd8a` (phase 4) | Also holds this phase's `AutomationSession.cs` call sites, its `AutomationSessionTests` test and the README's "Report to Testiny" section (see "Commit split") |

One review covered phases 3 and 4 together. It confirmed 16 findings and rejected 11. Eight of the confirmed findings belong to this phase; two of them overlap, so they took 7 fixes. All were fixed before the commit, together with the phase 4 findings. The fixes are marked "review fix" below and listed under "Review findings".

| Project | Passed, Phase 3 implementation | Passed, final pass (after the review fixes of phases 3 and 4) |
|---|---|---|
| `src/Libraries/Testing/UI.Automation.Reporting.Testiny/test` | 84 | 93 |
| `src/Libraries/Testing/UI.Automation.Runtime/test` | 131 | 189 |
| `src/Libraries/Testing/UI.Automation.Runtime.TUnit/test` | 25 | 25 |

The 93 Testiny tests are the 84 from the implementation, the 8 that the review fixer added and 1 from phase 4 (`TestinyResultCommentTests`). All Runtime tests after the 131st come from phase 4.

## What Was Done

### 3. `42.Testing.UI.Automation.Runtime`

#### 3.6 Reporting pipeline

- **The dispatcher is unchanged.** Batching, back-off with `Retry-After`, giving up after `Reporting:MaxAttempts`, `results.jsonl` and the replay are phase 1's code. This phase ran them end to end against the Testiny reporter (see "Verification").
- **New: `Reporting/ReporterServices`** (internal), the `IServiceProvider` a reporter provider receives:
  - `IConfiguration` returns the run's own configuration, so a reporter can read the shared `Reporting` keys next to its section. The Testiny provider reads `Reporting:UnmappedTests`.
  - `ILoggerFactory`, when the consumer's services have none, returns a factory whose loggers all write to the session's logger, so the reporter's start, close and warnings reach the terminal.
  - Every other service comes from `AutomationSessionOptions.Services`, as before.
- **`ReporterSelection.CreateAsync`** has a new `ILogger logger` parameter and passes `ReporterServices` to each provider. `AutomationSession` passes the session logger at both call sites, the run and `ReplayAsync`.
- **Tests.** `FakeReporterProvider` records the services it got. `AutomationSessionTests.StartAsync_HandsTheProvidersTheRunsConfiguration_AndTheConsumersServices` is new, which brings the Runtime to 131 tests.
- **Abstractions.** `TestRunProperties.Os` (`"os"`) is phase 4's property. It is committed here because `TestinyResultComment` shows it on its Browser line.

### 5. `42.Platform.UI.Admin.AutomationTests`

- `UI.Admin.AutomationTests.csproj` references `UI.Automation.Reporting.Testiny`.
- `PlatformAutomationSetup.ReporterProviders` is now `[TestinyReporterProvider.Instance]`, a get-only property (it was `=> []`). Its header comment names the reporters that `Reporting:Systems` may select.
- No test got a `[TestCase]` id, because the cases do not exist in Testiny yet (see phase 2).
- The `reporting-testiny` profile is described in section 7 below.

### 6. `42.Testing.UI.Automation.Reporting.Testiny`

- **Project.** `src/Libraries/Testing/UI.Automation.Reporting.Testiny/src/UI.Automation.Reporting.Testiny.csproj`, assembly `42.Testing.UI.Automation.Reporting.Testiny`, namespaces `_42.Testing.UI.Automation.Reporting.Testiny` and `.Api`.
  - It is packable, with the tags `testing;ui;automation;testiny` added to the global ones. `InternalsVisibleTo` gives the unit tests access.
  - Its only reference into the framework is the Abstractions, as section 1 plans.
  - Packages: `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging.Abstractions`, `TestableIO.System.IO.Abstractions` and `TestableIO.System.IO.Abstractions.Wrappers`. Artifacts are read through `IFileSystem`. `Microsoft.Extensions.Options` was not needed, and `Directory.Packages.props` is unchanged.
  - `42.mono.slnx` lists the library and its tests under `/Libraries/Testing/`.
- **Files.**
  - Options and enums: `TestinyOptions`, `TestinyMode`, `TestinyContentPolicy`, `TestinyResultStatus`.
  - Reporter and provider: `TestinyTestRunReporter`, `TestinyReporterProvider`.
  - Helpers: `TestinyMarkdown` and `TestinyCaseIds` (public); `TestinyResultComment`, `TestinyRunDescription`, `TestinyArtifactNote` and `TestinyArtifactState` (internal).
  - `Api/`:
    - the client `TestinyClient`, with `TestinyTransientException`, `TestinyApiException` and `TestinyJsonContext`;
    - the public bodies `TestinyTestRun`, `TestinyTestCaseRunMapping` (with `TestinyTestCaseRunIds` and `TestinyResultValues`), `TestinyComment`, `TestinyCommentRunMapping` (with `TestinyCommentRunIds`), `TestinyBlob`, `TestinyApiError` and `TestinyMappingOperation`;
    - the internal find types `TestinyFindRequest`, `TestinyFindResult`, `TestinyFindMeta`, `TestinyEntityId`, `TestinyPagination` and `TestinyProjectFilter`.

#### 6.1 Options (`Reporting:Testiny`)

- **`TestinyOptions`** has every key of section 6.1, with the spec's defaults (test `Defaults_AreTheSpecs`): `BaseUrl`, `ApiKey`, `ProjectId`, `TestPlanId`, `Mode`, `PlanCasesAsNotRun`, `CloseRunOnComplete`, `Attachments`, `MaxAttachmentBytes`, `Comments`, `ResultForSkipped`, `ResultForBlocked` and `RequestTimeoutSeconds`.
- **Two keys beyond the spec:**
  - `RunUrlTemplate`, default `{AppUrl}/{ProjectId}/testruns/{RunId}` (`DefaultRunUrlTemplate`). It replaces the run URL that section 6.3 wanted "verified in Phase 3 against the UI's URL scheme". There was no account to check that scheme, so the default is marked unverified in the code, the profile and the README (see "Not verified").
  - Review fix: `MinUploadBytesPerSecond`, default 262144 (256 KiB/s, `DefaultMinUploadBytesPerSecond`); see 6.2.
- **Members:**
  - review fix: `SectionName` (`"Reporting:" + TestinyTestRunReporter.SystemName`);
  - `DefaultBaseUrl`, `DefaultMaxAttachmentBytes`;
  - `AppUrl`, the scheme and host of `BaseUrl` without its `/api/...` path;
  - `GetBaseUri()`, the API root with a trailing slash;
  - `FormatRunUrl(long)`, which is null without a template.
- **`Validate(section)`** returns every problem at once and never prints a secret value. It checks:
  - `BaseUrl` is an absolute http or https URL;
  - `ApiKey` is not empty (the message names the user secret `Reporting:Testiny:ApiKey` and `UITESTS_Reporting__Testiny__ApiKey`);
  - `ProjectId` is at least 1, and `TestPlanId` is positive or null;
  - `Mode` is `TestRun`;
  - `MaxAttachmentBytes`, `RequestTimeoutSeconds` and `MinUploadBytesPerSecond` are positive;
  - `ResultForSkipped` and `ResultForBlocked` look like statuses (`^[A-Z][A-Z0-9_]*$`, so a custom status works);
  - `RunUrlTemplate` has only the placeholders `{AppUrl}`, `{ProjectId}` and `{RunId}`.

  Each message starts with the key below the section, because the runtime puts `Reporting:Testiny: ` in front of it.
- **Enums and constants.** `TestinyMode { TestRun, Automation }`. `TestinyContentPolicy { None, Failures, All }`, where `Failures` means every outcome except Passed and Skipped, so Failed, Timeout, Cancelled and Blocked. `TestinyResultStatus` holds the built-in statuses `NOTRUN`, `PASSED`, `FAILED`, `BLOCKED` and `SKIPPED`.
- **The shared `Reporting` keys** (`Enabled`, `Systems`, `RunName`, `RunDescription`, `UnmappedTests`, `BatchSize`, `FlushIntervalSeconds`, `MaxAttempts`, `CompleteTimeoutSeconds`, `ReplayFile`) are still the Runtime's `ReportingSettings` from phase 1.

#### 6.2 `TestinyClient`

- **`TestinyClient(HttpClient, TestinyOptions, TimeProvider? = null)`** is stateless and safe to use from several threads.
  - Every request carries `X-Api-Key` (`ApiKeyHeader`) and `Accept: application/json`, and is bounded by `RequestTimeoutSeconds` through a linked token on the `TimeProvider`.
  - Bodies go through the source-generated `TestinyJsonContext` (snake_case names, nulls not written), so an update sends only what it sets.
- **Routes:**
  - `CreateTestRunAsync` (`POST /testrun`), `GetTestRunAsync` (`GET /testrun/{id}`, for the `_etag`) and `UpdateTestRunAsync` (`PUT /testrun/{id}`).
  - `MapTestCasesAsync(items, operation)` (`POST /testrun/mapping/bulk/testcase:testrun?op=…`). The answer has no body in the OpenAPI document, so it is not read.
  - `UploadResultAttachmentAsync(testRunId, testCaseId, content, title, fileName, mimeType)`: the raw bytes as `application/octet-stream`, with `title`, `filename` and `mime_type` in the query.
  - `CreateCommentsAsync` (`POST /comment/bulk`) and `MapCommentsToResultsAsync` (`POST /comment/mapping/bulk/comment:testcase:testrun?op=add`), bulk forms of the spec's `CreateCommentAsync` and `MapCommentToResultAsync`.
  - `FindTestCasesAsync(projectId, ids)` (`POST /testcase/find`), `MaxFindIds` (200) ids per request, with `filter.project_id`, `idOnly`, a pagination of the chunk's size and, as a review fix, `omitForbidden: true`.
- **Review fix: `UploadTimeout(bytes)`** (public) is the longer of `RequestTimeoutSeconds` and the file's size at `MinUploadBytesPerSecond`. A 25 MB file gets 100 s instead of 30 s.
- **Errors:**
  - `TestinyTransientException : TransientReporterException` (the phase 1 abstraction), with `StatusCode`. It covers 429, 408, 5xx, a timeout and a network failure (`HttpRequestException`). It carries `Retry-After` as a delta or a date, capped at 5 minutes.
  - `TestinyApiException` covers any other 4xx, with `StatusCode`, `Error` (the `ApiError` body), `IsUpdateConflict` (a 409, or an error whose message or code mentions a conflict) and `IsDataError` (400, 404, 409, 422).
  - Messages read like "Testiny POST /testrun answered 503 Service Unavailable: …", without the query and never with the key.
  - When the caller cancels, the `OperationCanceledException` passes through unchanged.

#### 6.3 `TestinyTestRunReporter` (`Mode = TestRun`)

- **Public surface.**
  - The constructor is `(TestinyClient, TestinyOptions, bool validateCaseIds = false, IFileSystem? = null, ILogger? = null)`.
  - Members: `SystemName` (`"Testiny"`), `ChunkSize` (200), `MaxUploadAttempts` (2, review fix), `RunUrl`, `RunId` and `Warnings`.
  - A `SemaphoreSlim` serializes the calls, so concurrent batches keep the state consistent (test `ReportAsync_FromSeveralThreads_KeepsTheStateConsistent`).
- **`StartAsync`:**
  - It creates the run once: the run's name, cut to 200 characters, as the title, `project_id`, and the description from `TestinyRunDescription.Started`. It keeps the `id` and the `_etag`, sets `RunUrl` from the template and logs "Testiny run {id} '{title}' created in project {projectId}: {url}".
  - With `TestPlanId` set, it sends the spec's `PUT /testrun/{id}` with `testplan_id` and the creation's `_etag`.
  - When `validateCaseIds` is set, it checks the linked ids once with `FindTestCasesAsync`. Ids that are unknown or deleted are left out of the run with one warning, which lists up to 20 of them with their tests.
  - With `PlanCasesAsNotRun`, it adds every distinct linked case as `NOTRUN`, 200 per request.
  - Review fix: a plan or a check that Testiny refuses (`TestinyApiException`) becomes a warning. The run goes on outside the plan, or with the ids unchecked. Before the fix, either refusal failed the start and left an open, empty run.
- **`ReportAsync(batch)`:**
  - **Status.** Only the last attempt of each test counts (`LatestAttempts`), and the worst result over every test of a case wins: `FAILED` > `BLOCKED` > `SKIPPED` > `PASSED`. Timeout and Cancelled count as `FAILED`, and `ResultForSkipped` and `ResultForBlocked` replace the middle two. A case's status is recomputed over all results so far, so a later passing test does not hide an earlier failure. Only changed statuses are sent, in one `add_or_update` mapping per batch (chunks of 200).
  - **Attachments.** Per `Attachments`, each artifact (`failure.png`, `trace.zip`, `console.log`, `video.webm`) goes to each linked case.
    - A file over `MaxAttachmentBytes`, a file that is gone and a file the policy excludes are only listed in the comment.
    - The title is `<Class>.<Test>: <artifact>`, with ` (attempt n)` after the first attempt.
    - The file name keeps letters, digits, `.`, `-` and `_`. Review fix: a long name is cut before its extension (up to 16 characters), trailing `.` and `-` are trimmed, and the name stays within 120 characters. Before the fix the cut dropped the extension.
  - **Comments.** Per `Comments`, `TestinyResultComment.Build` writes one markdown comment per test and linked case. It has:
    - the bold status and the short name;
    - the Test, Outcome, Duration (Testiny has no duration field), Started, Attempt, Browser (engine and version, channel, provider, os, device), Seed, Remote session, Run (name and run id), Build (the CI URL) and Source (`file:line`) lines;
    - the artifacts and what became of each;
    - the exception's type, the first line of its message (at most 1,000 characters) and its stack trace in a fence longer than any backtick run in it, cut at `MaxStackTraceLength` (20,000). A failure without an exception type gets a **Reason:** line.

    The comments of a batch go out in one `POST /comment/bulk` per 200, and their mappings in one more request. The reporter checks that the answer has as many comments with ids as it sent, and maps them by position.
- **Retries repeat only what is missing.** The reporter remembers what Testiny accepted: the run, the plan, the check, each case's sent status, each upload (by result, case and file) and each comment and mapping. A batch that the dispatcher retries after a transient failure sends only the rest, never a second run, upload or comment.
- **Refusals cost only their own item.**
  - A bulk mapping that Testiny refuses for its data (`IsDataError`) is sent again case by case. A refused case becomes a warning and is left out for the rest of the run.
  - A refused upload (a data error, 413, an I/O or access error) becomes a warning, and the comment says "the upload failed".
  - Review fix: an upload that fails transiently `MaxUploadAttempts` times is given up the same way, so the comments of its batch still go out.
  - Review fix: a refused `POST /comment/bulk` chunk is sent again comment by comment. A refused comment becomes a warning and is neither retried nor mapped. Before the fix one refusal dropped every comment of the batch.
  - A refused comment mapping becomes a warning.
- **Case ids.**
  - `TestinyCaseIds.TryParse` accepts `1234`, `TC-1234` and `TC1234` (an optional letter prefix with an optional `-` or `_`, then up to 18 digits, greater than 0). Any other value is a warning, once, and the link is ignored.
  - Links of another system (`TestCaseLink.AppliesTo`) are ignored.
  - Unknown ids are never test failures.
- **`CompleteAsync`:**
  - It fetches a fresh `_etag`, then sends `PUT /testrun/{id}` with the description from `TestinyRunDescription.Completed` and `is_closed` (left out without `CloseRunOnComplete`). After an update conflict it fetches the `_etag` once more and tries again; a second conflict surfaces.
  - The description holds the runtime's environment table (or `Reporting:RunDescription`, else the run's name) under the markdown header. Below it come a "Results" table (Passed, Failed, Skipped, Blocked, Total), the start, end and duration, "Test cases in the run: N (… by status)", the attachment and comment counts, and "Warnings": the dispatcher's and the reporter's, at most `MaxWarnings` (50), each cut to 1,000 characters.
  - It logs "Testiny run {id} closed: N test case(s), N attachment(s), N comment(s)". The runtime prints "Testiny run: {RunUrl}" at the end of the run and of a replay (phase 1's `LogRunUrls`).
- **Review fix: `DisposeAsync`** closes a run that was created but never completed: its start failed after the creation, so the dispatcher sends it nothing more, or its completion failed.
  - It works within `AbandonTimeout` (8 s, under the 10 s the dispatcher gives a dispose).
  - The description comes from `TestinyRunDescription.Stopped`: why the reporting stopped, the cases by status, the warnings, and a pointer to `results.jsonl` and `Reporting:ReplayFile`.
  - A failure is logged and never thrown. It then disposes an `HttpClient` the reporter owns.
- **`TestinyMarkdown`** builds the markdown:
  - `Header` (`<!--markdown-->`), `Document`;
  - `Text` (one line, markdown characters escaped), `Code`, `Fence`;
  - `Size`, `Duration`, `Time`, `Truncate`.
- **Rate limits.** The cost is the one section 6.3 estimates, and the README gives the formula. The per-run budget estimate at the start (section 12) is not logged.

#### 6.4 `TestinyReporterProvider`

- `TestinyReporterProvider.Instance`. `System` is `TestinyTestRunReporter.SystemName` (`"Testiny"`), and `HttpClientName` is `"Testiny"`.
- `BindOptions(IConfiguration section)` binds the section, validates it with its path (or `TestinyOptions.SectionName`) and throws an `InvalidOperationException` with every error joined. Without a key and a project the message names both (test `Create_RefusesASectionWithoutApiKeyAndProjectId_NamingBoth`).
- `Create(services, section, run)` takes from the services, when they have them:
  - `IHttpClientFactory`: the named client `"Testiny"`. Without one, the provider creates its own `HttpClient` (a `SocketsHttpHandler` with a 5-minute `PooledConnectionLifetime` and an infinite client timeout, because the client bounds each request), which the reporter disposes.
  - `IFileSystem` for the artifacts.
  - `ILoggerFactory`, which creates the `TestinyTestRunReporter` logger.
  - The run's `IConfiguration`: `Reporting:UnmappedTests` (the parent of the section) set to `Warn` or `Fail` turns on the check of the linked ids. `Ignore`, the default, saves the request.

#### 6.5 Tests of the library

- **Project.** `src/Libraries/Testing/UI.Automation.Reporting.Testiny/test/UI.Automation.Reporting.Testiny.UnitTests.csproj`: xUnit with Shouldly (as the repository's rule for new test projects allows), `Microsoft.Extensions.TimeProvider.Testing` and `TestableIO.System.IO.Abstractions.TestingHelpers`. No test calls Testiny.
- **Fakes.**
  - `Fakes/FakeTestiny` is an `HttpMessageHandler` that keeps the routes in memory. Every update checks and renews the run's `_etag` and answers a stale one with 409 `API_UPDATE_CONFLICT`. `POST /testcase/find` answers over `KnownCases`, `Fail(...)` scripts answers (a status, a body, a `Retry-After`) for the next matching requests, `TouchRun` stands for another writer, and `Intercept` delays or replaces answers.
  - `Fakes/RecordedRequest` and `Fakes/TestData` support them.
- **93 tests:**
  - `TestinyTestRunReporterTests` (34) cover run creation and NOTRUN planning in chunks of 200, the test plan, the id check, worst-wins over tests and batches, the configured statuses, the last attempt, the attachment policy and size cap, missing files, the comment's content and policies, retries without duplicates, refused cases, uploads and comments, long names, concurrency, the `_etag` round trip and conflicts, `CloseRunOnComplete` and `DisposeAsync`.
  - `TestinyClientTests` (21) cover the request shapes and the API key, 429 with `Retry-After` (seconds and a date, capped), 5xx and 408, `ApiError`, update conflicts, a 401 without the key in the message, timeouts, the upload timeout, network failures, cancellation and a base URL without a trailing slash.
  - `TestinyReporterProviderTests` (17) cover binding, validation of every key, `Mode Automation`, the id check from the run's configuration, `IHttpClientFactory`, the defaults, `AppUrl` and `FormatRunUrl`.
  - `TestinyCaseIdsTests` (13), `TestinyMarkdownTests` (7) and `TestinyResultCommentTests` (1, phase 4).
- **Not done:** the hand-run `testint` against a scratch Testiny project of the phase 3 checklist (section 6.5). No account exists; the fake-server runs under "Verification" stand in for it.

#### 6.6 `Mode = Automation`

Not implemented, as planned. `TestinyMode.Automation` exists, and `Validate` refuses it with "Mode Automation (Testiny's automation API) is not implemented yet; use TestRun." (test `Create_RefusesModeAutomation_AsNotImplementedYet`).

### 7. Configuration shape

- **New profile `src/Platform/UI.Admin.AutomationTests/src/testsettings.reporting-testiny.json`.**
  - It sets `Reporting:Enabled` to `true` and `Reporting:Systems` to `["Testiny"]`. The `Testiny` section lists every key of 6.1 at its default, plus `MinUploadBytesPerSecond` and `RunUrlTemplate`, the latter with an "unverified" comment.
  - `ProjectId` stays 0 and `ApiKey` stays empty. The project id goes in `testsettings.local.json` or `UITESTS_Reporting__Testiny__ProjectId`, the key in the user secrets or `UITESTS_Reporting__Testiny__ApiKey`.
  - The profile combines with a target, e.g. `UITESTS_PROFILE=mock,reporting-testiny`.
- Without a project id and an API key, the profile makes the session start fail with both validation messages, so every UI test fails with them (see "Verification").

### 10. Documentation and `CLAUDE.md`

- **Project `README.md`** (committed with `3666bd8a`):
  - A new section, "Report to Testiny", covers:
    - configuring and the secrets, and the key table of `Reporting:Testiny:`;
    - the `RunName` placeholders;
    - what lands in Testiny (the run and its description, the statuses, the attachments and comments of failed tests, the case id forms, which problems become warnings);
    - retries and the `Reporting:ReplayFile` command;
    - the rate-limit cost of a run.
  - The Configure table's `Reporting:*` row, the `Reporting:Testiny:ApiKey` user-secrets line and the `reporting-testiny` profile in the profile list.
  - Review fix: the section says that the committed tests have no `[TestCase]` ids yet, so a reported run is empty. It shows `[TestCase("1234")]` on `SignInTests.SignIn_ShowsOrganizations` and suggests `Reporting:UnmappedTests=Warn`, which lists the tests without an id.
  - A phase 4 review fix completed the list of the description's environment rows (channel, OS, run id).
- **`CLAUDE.md`.** The Tech Stack line adds "with Testiny test run reporting and BrowserStack browsers". The Libraries entry for `Testing/UI.Automation.*/` adds `Reporting.Testiny` and the BrowserStack provider. The BrowserStack wording belongs to phase 4 but is in this commit.
- **Not written in this phase:** the docs pages of section 10 (`docs/Libraries/Testing/UI.Automation/readme.md`, the Platform page) and a `package-readme.md` for the library (phase 5).

### Commit split

The working tree held phases 3 and 4 together. Files that both phases changed went into one of the two commits:

- In `27fd74ea`, from phase 4: `TestRunProperties.Os`, the `os` part of the comment's Browser line, `TestinyResultCommentTests`, and the BrowserStack wording in `CLAUDE.md`. The library is in its final state, with all the review fixes.
- In `3666bd8a`, from phase 3: the `logger` argument at the two `ReporterSelection.CreateAsync` call sites in `AutomationSession.cs`, `AutomationSessionTests.StartAsync_HandsTheProvidersTheRunsConfiguration_AndTheConsumersServices`, and the README's "Report to Testiny" section and rows.
- Read from the code, not built: at `27fd74ea` alone, `AutomationSession.cs` calls `ReporterSelection.CreateAsync` without the new required `logger` parameter, so the Runtime does not compile at that commit. The two commits build together, and that state is what was verified.

### Deviations from the spec

- **Markdown header `<!--markdown-->`.** That is the spelling of the OpenAPI document and the API quickstart's text. The spec, and the quickstart's example, write `<!-- MARKDOWN -->`. Which one Testiny reads is unverified.
- **Runtime change.** Providers get `ReporterServices` (the run's `IConfiguration`, and the session logger as `ILoggerFactory`), and `ReporterSelection.CreateAsync` has a `logger` parameter (3.6). Section 6 assumed no runtime change.
- **Transient exception.** `TestinyTransientException` derives from the abstractions' `TransientReporterException`, so the spec's name and phase 1's engine-neutral signal both hold. Beyond the spec's 429 and 5xx, 408, timeouts and network failures are transient too.
- **Comments in bulk.** The spec plans two calls per failed test. The reporter sends one `/comment/bulk` request and one mapping request per batch (200 items each), so the client has `CreateCommentsAsync` and `MapCommentsToResultsAsync`. This assumes that Testiny answers a bulk create in request order; the reporter checks the count.
- **`FindTestCasesAsync(projectId, ids)`** takes the project, filters by it and sends `idOnly` and `omitForbidden`.
- **`IHttpClientFactory` is optional.** Without one the provider creates its own `HttpClient`, and the reporter disposes it.
- **Run URL.** It is not verified, as section 6.3 wanted, but configurable through `RunUrlTemplate`, with an unverified default.
- **Additions beyond the spec:**
  - case-by-case fallbacks for refused mappings and comments;
  - retries that repeat only what is missing;
  - `Failures` covering every outcome except Passed and Skipped;
  - the Source line in the comment;
  - the 20,000-character stack trace and the 50-warning description;
  - the second try after an update conflict.
- **Review-fix additions** (new API surface the spec does not describe): `MinUploadBytesPerSecond` and `TestinyClient.UploadTimeout`; `MaxUploadAttempts`; closing an abandoned run in `DisposeAsync`; the refused plan and refused check becoming warnings. The test plan is still attached by the spec's `PUT`, not sent with the creation.
- **Validation messages** start with the key relative to the section, because the runtime adds the section in front.
- **Not done from the spec:** the per-run rate-limit budget logged at the start (section 12); the run URL check (6.3); the `testint` checklist run (6.5).

### Review findings

Confirmed and fixed for this phase (8; the review's lenses were correctness, Testiny API, BrowserStack spec, and conventions):

| Finding | Severity | Fix |
|---|---|---|
| A start that fails after `POST /testrun` leaves an open, empty Testiny run | medium | A refused plan or check is a warning; `DisposeAsync` closes the run with `TestinyRunDescription.Stopped` (6.3) |
| A 4xx from the optional case check or the test-plan `PUT` kills all reporting and leaves an open, empty run | medium | The same fix; the find request also sends `omitForbidden: true` (6.2) |
| Testiny refusing `POST /comment/bulk` drops every comment of the batch | medium | A refused chunk is sent comment by comment; a refused comment is a warning and is not mapped |
| A 25 MB upload has the same 30 s timeout as any request, so a slow link loses the batch's comments | low | `MinUploadBytesPerSecond` and `UploadTimeout`; an upload is given up after `MaxUploadAttempts` transient failures |
| Cutting a long artifact name drops the extension and leaves trailing dots | low | `FileNameOf` cuts the stem, keeps the extension, trims `.` and `-` |
| The README describes the reporting as if the tests carry `[TestCase]` ids | medium | The README says they have none yet and how to add one |
| The section name `Reporting:Testiny` is defined twice | low | `TestinyOptions.SectionName`, used by `Validate`, `BindOptions`, the `UnmappedTests` fallback and the tests |
| Comments name settings `Testiny:<Key>`, which is not a key path | low | `Reporting:Testiny:<Key>` |

Each fix has a unit test: `StartAsync_ATestPlanTestinyRefuses_IsAWarning_AndTheRunGoesOnOutsideIt`, `StartAsync_ACaseCheckTestinyRefuses_IsAWarning_AndTheIdsAreUsedUnchecked`, `DisposeAsync_ClosesARunWhoseStartFailed_WithWhyInItsDescription`, `DisposeAsync_ClosesNothing_AfterTheRunCompleted_OrWithoutARun`, `ReportAsync_ACommentTestinyRefuses_CostsOnlyItself`, `AnUpload_HasTheTimeOfItsSize_AtTheSlowestExpectedRate`, `ReportAsync_AnUploadThatFailsTransientlyTwice_IsGivenUp_AndTheCommentStillGoesOut` and `ReportAsync_ALongArtifactName_IsCutBeforeItsExtension`.

Rejected (8 that concern this phase):

- **A refused single mapping blocks the case for the rest of the run, even when Testiny refused its status.** The code path is real, but it shows only after a misconfiguration, depends on Testiny behaviour that nobody has verified, and the warning already says what happened.
- **The fallback after a refused bulk mapping sends up to 200 single writes back to back.** True, but it stays within Testiny's write limits.
- **A 429 without `Retry-After` falls back to the generic 1-2-4-8 s back-off, and the request rate is never lowered.** The facts are right (Testiny documents no rate-limit headers), but this is not a defect to fix now; see "Not verified".
- **Comment ids are matched to cases by position only.** This does not hold up as a fix: the reporter checks that the answer has one comment with an id per comment sent.
- **The comment mapping uses `op=add`, which fails on a retry.** The harm depends on server behaviour that nobody has confirmed, and the fix would move away from Testiny's documented request.
- **The provider parses `Reporting:UnmappedTests` itself, as a string.** Correct, but not worth changing now. The typed `UnmappedTestsPolicy` is in the Runtime, which this library does not reference.
- **The `<Class>.<DisplayName>` short name exists three times** (the dispatcher's warning list, `TestinyResultComment.ShortName`, the BrowserStack session name). The copies exist; the finding was rejected without a change.
- **Three truncation helpers end a cut text differently.** They do different jobs on purpose.

The rest of the review (the BrowserStack findings, the Playwright 1.63.0 pin, and "a passing BrowserStack run leaves Testiny with no BrowserStack session") belongs to the phase 4 review.

## Verification

Done-when of phase 3 (section 11), against what was run:

| Criterion | State |
|---|---|
| A run against a scratch Testiny project shows the run, every case with its status, duration/stack comments and screenshots/traces on failures | Against the fake Testiny server only (run 1 below): the run, every linked case with its worst status, two comments with duration and stack trace, and the `failure.png`, `trace.zip` and `console.log` of the failure, each on both of its cases. Not against Testiny |
| A forced `429` is retried | Yes, on the fake. Run 2: `POST /testrun` got 503 three times and then a 429, and succeeded on attempt 5; `POST /comment/bulk` got a 429 and was retried. The replay of run 3 waited the 6 s `Retry-After` (6.0 s between the attempts, where the back-off alone was 1 s) |
| An offline run replays from `results.jsonl` | Yes, on the fake. Run 3: with every request answered 503, the reporter was dropped and the tests ran normally; the replay of its `results.jsonl` created, filled and closed a run |

End-to-end runs. The fake was a scratch HTTP server on `127.0.0.1:5399` with the routes of 6.2, a scripted fault injector and a request log. The suite was the mock target's 6 Elementary tests plus a throwaway probe class (deleted afterwards) linked to the cases 1001 to 1003, the unknown id 9999 and a `System = "TestRail"` link:

| Run | Result |
|---|---|
| 1, `mock,reporting-testiny`, `UnmappedTests=Warn` (`20261031-200608-90sp`) | 9 tests: 8 passed and 1 failed on purpose, exit 2 from that failure. 15 requests: the run; `testcase/find` with 1001, 1002, 1003 and 9999 (9999 became a warning and stayed out); the planning of 1001 to 1003 as NOTRUN; the results; 6 uploads (`failure.png` 34,238 B, `trace.zip` 394,153 B, `console.log` 169 B, to cases 1001 and 1002), failures only; one `comment/bulk` with 2 comments and one mapping; `GET` and `PUT` of the run with `_etag` and `is_closed`. Case 1001 went NOTRUN → PASSED → FAILED, because two tests share it and the worse result won. The TestRail link was left out. The description has the totals, "Test cases in the run: 3 (2 FAILED, 1 PASSED); 6 attachment(s), 2 comment(s)" and three warnings. The terminal printed `Testiny run: http://127.0.0.1:5399/42/testruns/1` |
| Replay of run 1's `results.jsonl` | A second run (id 2) with the same statuses, 6 uploads and 2 comments, closed; the 9 tests were skipped, exit 0 |
| 2, outage (`20261031-200742-f2zs`) | Run creation: 503 three times, then 429 with `Retry-After` 4, then success. A batch: one upload 503, then `comment/bulk` 503 twice and a 429. Exactly one run, each file uploaded once, each comment written once, the status mapping not repeated, and the test outcomes unchanged |
| 3, full outage (`20261031-200903-dedt`) | Every request answered 503: after 5 attempts (back-off 1, 2, 4, 8 s) the reporter was dropped with "nothing is reported to it in this run; the results stay in …results.jsonl". The tests ran normally, and the session start waited about 15 s |
| Replay of run 3 against the recovered fake | One 429 with `Retry-After` 6 s, honored. `UnmappedTests` was at its default, so the ids were not checked: the bulk planning with 9999 was refused (400), the reporter retried case by case, and only 9999 was dropped as a warning |
| A later run, after `ReporterServices` passed the session logger to the reporter (`20261031-201049-qv3m`) | The reporter's own lines reached the terminal: "Testiny run 1 … created in project 42", the refused case 9999, and "Testiny run 1 closed: 3 test case(s), 6 attachment(s), 2 comment(s)" |
| `reporting-testiny` without `ProjectId` and `ApiKey` | The session did not start: "Reporting:Testiny: ApiKey is empty; set Reporting:Testiny:ApiKey in the user secrets … ProjectId is required …". Every UI test failed with that message |
| After the review fixes, A: the plan answers 404, an upload answers 503 twice (`20261031-213927-f206`) | A warning "Testiny did not add run 1 to test plan 77 …", and the run continued outside the plan. `failure.png` was given up after 2 tries with a warning. The comment still went out and lists it as "the upload failed". The run was closed: 3 cases, 2 attachments, 1 comment |
| B: the planning answers 403 (`20261031-213951-ns9h`) | The start failed (403 is not a data error) and the dispatcher dropped the reporter. `DisposeAsync` closed run 1 with the stopped description: "Testiny run 1 was closed without the rest of its results: its start failed: …" |
| C: `comment/bulk` answers 422 (`20261031-214007-ubrg`) | One refused request and a warning "Testiny refused the comment of ReportingProbeTests.Probe_Fails …". No mapping was sent, and the run was completed and closed |

The probe of the fixer's runs A to C (a pass, a failure and an attribute skip, cases 1001 to 1003) was also deleted afterwards. Runs 1 to 3 used Playwright 1.64.1 and runs A to C used 1.63.0 (the phase 4 pin).

Commands and checks:

| Check | Result |
|---|---|
| `dotnet build src/Libraries --no-incremental` | 0 errors and 303 warnings, as in the phase 1 baseline, none from `UI.Automation.*`. After the review fixes: 0 errors; the only new warnings were two NuGet restore warnings of the unrelated `Roslyn/Compose` project |
| The Platform project with `--no-incremental` | 0 errors, 0 warnings, before and after the fixes. Clean rebuilds of the three UI.Automation test projects: 0 warnings |
| Unit tests | Implementation: Reporting.Testiny 84, Runtime 131, Runtime.TUnit 25. Final: 93, 189, 25 (see "Overview") |
| Mock suite after the fixes (`mock`, Chromium, headless, Playwright 1.63.0) | 6/6, exit 0, with the usual pool warning |
| The API key | Found in no terminal log, `results.jsonl` or request log. The settings dump shows `Reporting:Testiny:ApiKey = ***` |

The run ids are those of `results.jsonl` under the project's `bin/Debug/net10.0/TestResults/artifacts/`.

## Not verified and deferred

- **Nothing ran against a real Testiny.** Still to confirm on a scratch project (the section 6.5 checklist):
  - that Testiny reads `<!--markdown-->` as the markdown header;
  - the default `RunUrlTemplate`, `{AppUrl}/{ProjectId}/testruns/{RunId}`;
  - the status of an update conflict (assumed 409, or an error that mentions "conflict");
  - that `/comment/bulk` answers in request order;
  - that `testcase/find` accepts `ids`, `filter`, `idOnly` and `omitForbidden` together;
  - how Testiny answers a mapping with an unknown case id (the fake answers 400);
  - that uploads with `title`, `filename` and `mime_type` show up on the result as expected.
- **No `[TestCase]` ids yet.** Until the Elementary cases exist in Testiny, a reported run of the committed tests is an empty run with the totals in its description.
- **Retries against real rate limits.** With the defaults (5 attempts, back-off from 1 s), a call gives up after about 15 s. Testiny's windows are minutes, and its 429 documents no `Retry-After`, so a real throttle can outlast the retries. The batch is then dropped as a warning and can be replayed. Raising `Reporting:MaxAttempts` or the back-off may pay off for large runs (see the rejected 429 finding).
- **Reporter warnings are not counted in the dispatcher's summary.** They show on the terminal and in the run's description, but run A's summary says "1 warning(s)" (the pool), without the upload warning.
- **The start waits for Testiny.** While Testiny is down, the session start, and so every test, waits for the reporter's retries, up to `Reporting:CompleteTimeoutSeconds` (about 15 s in run 3).
- **Replay details.** A replay's description gives the replay time as "finished". Artifact paths in `results.jsonl` are absolute, so a replay on another machine lists the files as gone.
- **Traces carry the pool user's session cookies** (noted in phase 1). With `Attachments: Failures`, the traces of failed tests go to Testiny.
- **`Reporting:UnmappedTests=Fail`**, deferred from phase 1, was not run end to end. It is covered by `ReportingDispatcherTests.UnmappedTests_AreWarnedPerSystem_AndFailTheSessionForFail` and the check in `UiAutomationSession`; runs 1 and 2 used `Warn`.
- **Not built:** the rate-limit budget estimate at the start (section 12) and `Mode = Automation` (6.6, phase 6 or later).
- **Later phases:** the Testiny run carrying the BrowserStack session names (phase 4's done-when, for failures under the default `Comments: Failures`); the CI workflow with the Testiny run URL in the step summary, and the docs pages and `package-readme.md` (phase 5); the `[TestCase]` ids once the cases exist.
