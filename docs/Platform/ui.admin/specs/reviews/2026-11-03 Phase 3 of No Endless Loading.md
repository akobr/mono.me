# Phase 3 of No Endless Loading: The Automation Tests Wait for the API, and Old Runs Can Be Purged

Linear: [424-166](https://linear.app/424fun/issue/424-166/admin-ui-stalls-on-slow-api-calls-catalog-history-bulk-create).

## Overview

Phase 3 covers sections 13–16 of [No Endless Loading in the Admin UI on a Slow API](../2026-11-03%20No%20Endless%20Loading%20in%20the%20Admin%20UI%20on%20a%20Slow%20API.md), the part in the UI automation tests (`src/Platform/UI.Admin.AutomationTests`) and their docs. A wait for an answer of the API now waits as long as the admin UI's timeouts of phase 2 can take, and also ends on the UI's failure (its error state with Retry, a "not saved" toast, Bulk create's alert), which it throws with its text. A test that met a stalled request used to end in a bare "expected to be visible". The maintenance test `PurgeAutomationData` deletes the automation data of earlier runs from the target's view, but only when a run asks for it. A review of the first implementation found that the waits of writes ended before the admin UI's write timeout. The fixes of that review are part of this phase, so the deviations below include them. Phase 1 is reviewed in [`docs/Platform/Storyteller/specs/reviews/2026-11-03 Phase 1 of No Endless Loading.md`](../../../Storyteller/specs/reviews/2026-11-03%20Phase%201%20of%20No%20Endless%20Loading.md). Phase 2 is reviewed in this folder.

## What Was Done

Paths are relative to `src/Platform/UI.Admin.AutomationTests/src`.

### 13. `Target:ApiTimeoutMs`, and `Target:ApiWriteTimeoutMs`

- `PlatformTarget.ApiTimeoutMs` defaults to 65000 (`testsettings.json`) and to 15000 in the mock profile (`testsettings.mock.json`). It is the wait for the answer to a read: two reads in a row at the admin UI's 30 s read timeout, plus a margin.
- **Deviation: a second setting for writes.** `PlatformTarget.ApiWriteTimeoutMs` defaults to 185000 (mock profile: 15000). The spec gave the write waits `ApiTimeoutMs`, but that is too short for them:
  - The admin UI gives a write 120 s (`apiWriteTimeoutMs`, `ui.admin/src/app/boot/api.ts`).
  - When a configuration PATCH times out, the editor reads its content again (two reads, up to 60 s) before it shows its toast (`settleTimedOutSave`).
  - With 65 s, a stalled write still ended in a bare test timeout before the UI's alert or toast, which is the symptom of TR-4. The "Configuration saved" toast without a version and the two "The API did not answer in time; …" titles could never show in time.

  185 s is 120 + 2 × 30 + a margin. Bulk create waits twice as long: the lookup of deleted keys (`POST …/annotations/deleted/lookup`) and the create of a test's list (up to 100 keys) are two writes in a row (the final review).
- The defaults are constants of `PlatformTarget` (`DefaultApiTimeoutMs`, `DefaultApiWriteTimeoutMs`), and `App` reads them for its own defaults. The first implementation did it the other way round, which made the settings depend on a page object.
- `App(IPage page, string? mockScenario = null, int apiTimeoutMs = …, int apiWriteTimeoutMs = …)` has the properties `ApiTimeoutMs` and `ApiWriteTimeoutMs`.
  - Three construction sites pass both settings: `PlatformTest.cs`, `PlatformTargetCheck.cs` and `PlatformTest.Helpers.cs` (`OpenSecondPageAsync`).
  - `AuthKitSignIn`, `SignInTests` and `MyInvitationTests` keep the defaults.
- `PlatformAutomationSetup.Validate` requires both settings to be positive.

### 14. Waits that report the UI's error

- **`Pages/ErrorStates.cs` (new)** has three helpers:
  - `RetryButton(scope)`: the Retry buttons in the scope.
  - `Around(retry)`: the error state around a Retry button, which is its nearest ancestor with an `h2` or an alert title.
  - `TextAsync(element)`: the text on one line, without "Retry". A rendered element gives its `innerText`. A hidden one gives its text nodes, one per line. Below md, a full-screen detail hides the list, and there `innerText` glued the title, the message and "Retry" together (a review finding). `PlatformTargetCheck.ErrorTextAsync` uses it too.
- **`AnnotationsPage.WaitForCatalogAsync`**, as specified:
  - `CatalogLoaded` and `CatalogError` match with `IncludeHidden`. `CatalogRefreshError` is the compact alert "The catalog could not be refreshed", found by its text.
  - The wait is `ToBeAttachedAsync` up to `ApiTimeoutMs`. Then either error state throws a `PlaywrightException` with the error's text.
- **`GraphPage.WaitForCatalogAsync`** goes beyond the spec, which asked only for the timeout there. It calls `AnnotationsPage.WaitForCatalogAsync`: the graph shows the same catalog store, with the same "Reload the catalog" button and the same error titles (`AnnotationGraph.vue`). A graph test therefore stops at the catalog's error, not later at an unrelated assertion. `ReloadCatalogAsync` waits through it.
- **`PlatformTargetCheck`** keeps its own first wait, which tells "no access" apart, and uses `ApiTimeoutMs` for it.
- **`AnnotationHistory(App app, string section)`**:
  - `WaitForLoadAsync(path)` (new) waits up to `ApiTimeoutMs` for one of three states: the region "History", "No history", or the detail panel's Retry (`.First`). Retry throws with the error state's text.
  - `WaitForEntryAsync` loads, then waits 1 s for the action's badge, then loads the page again. The 30 s budget for the audit to catch up starts at the first loaded timeline.
  - **Deviation:** the badge wait of 1 s is the pause before a reload. There is no separate pause.
  - The 5 s `ReadTimeoutMs` is gone. It made the test reload in the middle of a slow read.
- **`AnnotationDetail.OpenHistoryTabAsync`** uses `WaitForLoadAsync`, beyond the spec, which asked only for the region with `ApiTimeoutMs`. "No history" now passes, since `WaitForEntryAsync` reloads it, and an error state throws at once. Before, either one waited 65 s for a region that would not come.
- **`BulkCreateDialog(App app)`**:
  - **Deviation:** the spec listed two construction sites, but there are four: `AnnotationsPage`, `AnnotationCatalogTests`, `GraphCreateTests` and `NavigationCommandPaletteTests`. All four changed.
  - `FailureAlert` is the dialog's `role="alert"`. The result's alert has no role, because Nuxt UI's `UAlert` sets none.
  - `CreateAsync` waits up to twice `ApiWriteTimeoutMs` for the result or the alert. The alert throws with its text.
- **`ConfigurationDetail.SaveAsync`** waits up to `ApiWriteTimeoutMs` for one of two outcomes:
  - Success: the toast "Configuration saved (v<n>)", or "Configuration saved" without a version. The UI shows the second for a save that timed out but was stored.
  - Failure: `NotSavedToast`, which throws with its text.
  - **Deviation:** the spec named one "not saved" toast, but the admin UI has two, and both are matched: "The configuration was not saved" and "The API did not answer in time; the change may have been saved. Reload before saving again." (the first implementation also matched a third, "…; the configuration was not saved.", which the final review of phase 2 removed from the UI).
- **`ConfigurationDetail`'s other waits** (the final review): `OpenAsync` and `OpenFilteredAsync` wait up to `ApiTimeoutMs` for the empty state, the editing mode or the detail's Retry (scoped to `section#configuration-detail`); `SetDocumentAsync` waits for the own content's editor or that Retry; `CreateEmptyAsync` and `StartFromTemplateAsync` wait up to `ApiWriteTimeoutMs` for the editing mode, the toast "The configuration was not created" or Retry. Each failure throws with its text. Before, they waited Expect's 10 s, passed with the JSON radio while the own content showed its error, and never looked at the "not created" toast (cases 221 and 192 create their configurations this way).
- **`ModelingTemplatePage`**:
  - `OpenAsync` waits up to `ApiTimeoutMs` for the empty state, the editor or the panel's Retry. Retry throws.
  - The saves (`SubmitAsync`) wait up to `ApiWriteTimeoutMs` for "Template saved (v<n>)" or "The template was not saved". The latter throws.
- **`DeletedAnnotationBanner(App app, string section)`**: `WaitForAsync()` waits up to `ApiTimeoutMs` for the banner, "Annotation not found" or the panel's Retry. Anything but the banner throws. `AnnotationDeleteTests` calls it at its four waits.

### 15. `PurgeAutomationData`

- **New files:**
  - `MaintenanceSettings.cs`: the `Maintenance` section with `Purge` (false), `OlderThanHours` (72) and `BudgetSeconds` (120).
  - `Attributes/RequiresPurgeAttribute.cs`: a `SkipAttribute` decided at registration from the loaded settings, like `[MockOnly]`, so a skipped test runs no hook and leases no user. It skips unless `Maintenance:Purge` is true, and always on the mock.
  - `Support/StorytellerApi.cs` and `Support/StorytellerApiResponse.cs`.
  - `Tests/Maintenance/AutomationDataTests.cs`.
- **The test's attributes:** `[Category(Maintenance)]`, `[Serial]`, `[RequiresPurge]`, `RequiredUser => AdminUser` (an Owner or Administrator of `Target:Project`), `AppRoutes.For(Target)`, and no `[TestCase]`.
- **`StorytellerApi`, how it connects and calls the API:**
  - `ConnectAsync` opens `/orgs` and takes the API base and the `Authorization` header from the admin UI's first request that has one. It skips `OPTIONS` and `/v1/auth/configuration`, and reads the header with `HeaderValueAsync`.
  - It sends the requests through the browser context's `APIRequest`.
  - On a 401 it captures the header once more, under a lock, so parallel requests renew it once. A second 401, a 403 or a 5xx throws an `HttpRequestException` with the API's message. No message includes the header: Playwright's own message of a timeout or a network error ends with the call log, which lists the request's headers, so `FetchAsync` throws a new `TimeoutException` or `PlaywrightException` with the method, the URL and the message up to the call log, without the original (the final review: the failure text goes to the console, the TRX, `results.jsonl` and CI artifacts).
- **`StorytellerApi`, decisions made after the review:**
  - The timeout depends on the method, like in the admin UI: `ApiTimeoutMs` for a GET, `ApiWriteTimeoutMs` for anything else.
  - Only a request without an answer throws a `TimeoutException`. A failed capture, at the start or in a renewal, throws an `InvalidOperationException`. Before, the purge's "left for the next run" catch took a failed renewal for a slow delete, and the test could pass.
  - Each `IAPIResponse` is disposed once its body is read, so the driver does not keep the bodies of hundreds of deletes.
- **What the test does:**
  1. Steps 1–2 as specified. It lists `subjects` and `responsibilities` with `nameQuery=^at-` and `includeInactive=true`, following the continuation tokens. It keeps a root when it matches all three conditions:
     - its name matches `^at-\d{8}-\d{6}-[a-z0-9]{4}-`;
     - its name does not start with the current run's `RunSlug`;
     - its `Created.At` is older than the cutoff.
  2. Step 3 deletes those roots four at a time (`Parallel.ForEachAsync`), the subjects before the responsibilities. A 404 counts as done. Any other answer the API does not refuse is recorded as skipped.
  3. Step 4 lists the deleted roots with `roots=true` and purges the matching ones: deleted before the cutoff, or by step 3. A 404 counts as purged, and a 409 is skipped.
  4. Step 5: after `BudgetSeconds` no request starts. The summary goes to the test's output in a `finally`: the counts, and the keys deleted, purged, skipped and left.
- **When the test fails:**
  - the API refuses a request (a second 401, a 403, a 5xx);
  - a list answers with anything other than 2xx;
  - a list gets no answer within `ApiTimeoutMs`;
  - the capture of the header fails;
  - a network error.
  A delete or purge without an answer within `ApiWriteTimeoutMs` is left for the next run, not a failure.
- **Deviation:** `PlatformAutomationSetup.Validate` requires `Maintenance:OlderThanHours` to be at least 1 (a review finding). With 0, the purge would delete the roots of another run that works in the view at that moment. It also requires `BudgetSeconds` to be positive.
- **Reporting:** `UiAutomationSession` passes `context.AllTests` as the planned tests, so the library's unmapped check also counts the skipped maintenance test. With `Reporting:UnmappedTests` set to `Warn`, every run lists it. With `Fail`, the session fails. The library was not changed. The docs tell such a run to add `--treenode-filter "/**[Category!=Maintenance]"`. The committed default, `Ignore`, is not affected.

### 16. Docs

- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md` changed in these places:
  - **The list of specs:** this spec.
  - **Run:** the purge command.
  - **Configure:** `Target:ApiTimeoutMs` and `Target:ApiWriteTimeoutMs`, the `Maintenance` table, and the validations.
  - **The mock profile row:** the two timeouts of the mock.
  - **Data rules:** nothing is deleted unless a run purges. The section covers what `PurgeAutomationData` does, when it fails, what it does not cover, and how to run it.
  - **The catalogue:** `Tests/Maintenance` is not a case.
  - **Writing tests:** which waits use which timeout and report the UI's failure.
  - **Testiny:** the maintenance test is not linked. A `Warn` or `Fail` run filters it out.
  - **Troubleshooting:** a row for "a page stays loading on the dev profile".
- The comment of `Data/PlatformData.cs` changed: the tests delete nothing, and only a purge run does.

## Final review

A review of the whole branch (three independent lenses, each finding verified against the code) found these in phase 3; all are fixed and described above:

- `BulkCreateDialog.CreateAsync` waited one write timeout for two writes in a row; it waits twice `ApiWriteTimeoutMs`.
- `ConfigurationDetail.OpenAsync`, `OpenFilteredAsync`, `SetDocumentAsync`, `CreateEmptyAsync` and `StartFromTemplateAsync` ignored the detail's error states and the "not created" toast.
- `StorytellerApi.FetchAsync` let Playwright's call log, with the user's `Authorization` header, into the message of a timeout or a network error.

After the fixes `dotnet build src/Platform/UI.Admin.AutomationTests/src --no-incremental` has the same 4 SA1143 warnings as before, none in a changed file, and `dotnet test --project src/Platform/UI.Admin.AutomationTests/src` skips all 234 tests without a target (exit 0). The desktop run against the mock was repeated with the final code of both phases (below).

## Verification

- `dotnet build src/Platform/UI.Admin.AutomationTests/src --no-incremental`: 0 errors. The 4 SA1143 warnings were already there (`TestinyResultComment.cs:85`, `BrowserStackCapabilities.cs:119`, `PlatformTest.Helpers.cs:85` and `:136`), and none is in a changed line.
- `dotnet test --project src/Platform/UI.Admin.AutomationTests/src --no-build`: 234 tests, all skipped (no target), exit 0.
- **Against the mock, with this branch's admin UI.** The e2e build (`vite build --mode e2e` into a temporary folder) was served with `vite preview` on port 5191. The runs used `UITESTS_PROFILE=mock UITESTS_Target__BaseUrl=http://127.0.0.1:5191 dotnet run --no-build --project src/Platform/UI.Admin.AutomationTests/src`:
  - Desktop: 234 tests, 230 passed, 4 skipped, 0 failed, 7 min. The skipped ones are three phone-only tests and `PurgeAutomationData`, which is always skipped on the mock. Repeated after the final review with the final code of phases 2 and 3 (the mock build served by `vite preview` on 5191): 230 passed, 4 skipped, 0 failed, 8 min.
  - Phone (`mock,mobile`): 234 tests, 215 passed, 19 skipped (desktop-only tests and the purge), 0 failed, 7 min.
  - A passing run does not exercise the failure branches, so a temporary class, deleted after the run, checked them on synthetic markup with `Page.SetContentAsync`. Its 4 tests passed:
    - `ErrorStates.TextAsync` gives the same one-line text for a rendered and for a `display:none` error state, and for a compact alert found through `Around(Retry)`.
    - `AnnotationsPage.WaitForCatalogAsync` throws with that text on a hidden "The catalog could not be loaded", and passes on a hidden, enabled "Reload the catalog".
    - `GraphPage.WaitForCatalogAsync` throws on "The catalog could not be refreshed".
- **The ten cases (110, 111, 177, 178, 67, 220, 222, 221, 192, 74) on the `dev` profile: all passed** (issue step 4). The owner ran them on 2026-11-03 against this branch's local stack, run `20261103-215826-hdjz`: `results.jsonl` has 10 results, all `Passed`, so none has to be split out as a functional problem. An earlier attempt by the implementation could not run them: another checkout's stack held the ports, and its sign-in stopped at AuthKit's "Continue with email" button, which main fixed in 424-165.
- **Not done: `PurgeAutomationData` against a real API.** It has only been skipped so far. The earlier read-only try (`--test-parameter Maintenance:OlderThanHours=1000000`) stopped in the sign-in before it sent a request. To list what it would do without deleting anything:

  ```bash
  UITESTS_PROFILE=dev dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Maintenance]" --test-parameter Maintenance:Purge=true --test-parameter Maintenance:OlderThanHours=1000000 --output Detailed
  ```
