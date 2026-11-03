# Phase 2 of No Endless Loading: Requests End, and Spinners Do Not Wait for Unrelated Work

Linear: [424-166](https://linear.app/424fun/issue/424-166/admin-ui-stalls-on-slow-api-calls-catalog-history-bulk-create).

## Overview

Phase 2 covers sections 6–12 of [No Endless Loading in the Admin UI on a Slow API](../2026-11-03%20No%20Endless%20Loading%20in%20the%20Admin%20UI%20on%20a%20Slow%20API.md): the TypeScript SDK (`src/Platform/Storyteller/sdk.typescript`) and the admin UI (`src/Platform/ui.admin`). The SDK's fetcher gets an optional deadline per request. The admin UI gives every read 30 s and every other request 120 s, shows a timeout as its own error with Retry, and stops holding its toasts and spinners on refreshes of unrelated queries. The catalog pages at most four type lists at once, shows how far a load is, and always ends a failed load. No generated SDK code changed, and the conformance suite did not change. A review of the first implementation found a race in the catalog store, where a load that was replaced while it read its snapshot still paged its view into the next load's map. The fix is part of this phase, so the deviations below include it. Phase 1 is reviewed in [`docs/Platform/Storyteller/specs/reviews/2026-11-03 Phase 1 of No Endless Loading.md`](../../../Storyteller/specs/reviews/2026-11-03%20Phase%201%20of%20No%20Endless%20Loading.md), and phase 3 in this folder.

## What Was Done

Paths of the SDK are relative to `src/Platform/Storyteller/sdk.typescript/src`, of the admin UI to `src/Platform/ui.admin/src`.

### 6. SDK request timeout

- **`lib/fetcher.ts`:**
  - `StorytellerConfig.timeoutMs` is a number or a function of `{ method, url }`. The method is in upper case, and the URL is the one `fetch` gets. Undefined, 0 and anything that is not a positive finite number mean no limit. A limit above 2^31 − 1 ms is capped there, because `setTimeout` would fire at once.
  - `StorytellerRequestInit = RequestInit & { timeoutMs?: number }` is exported from `index.ts`. `storytellerFetch(url, init?: StorytellerRequestInit)` keeps its two parameters, so every generated function and composable takes a per-call `timeoutMs` without regeneration. A per-call value wins over the configured one, and 0 disables the limit. `timeoutMs` is taken out of the init before `fetch`.
  - `runRequest(url, init, read)` owns the deadline. It is an `AbortController` with a `setTimeout`, `unref`'d where the runtime has it. The caller's signal is forwarded by a listener, or at once when it is already aborted. The timer is cleared and the listener removed in `finally`, after `read`. There is no `AbortSignal.any` or `AbortSignal.timeout`. Without a limit the caller's signal reaches `fetch` unchanged.
  - The token lookup, `fetch`, the body cancel before the retry after a 401, and `read` are each raced against the combined signal (`race`). A response of a `fetch` override that ignores the signal and answers after the race is cancelled (`discard`). A token provider that fails on its own keeps its error. Only an abort or the deadline during the lookup becomes the request's error.
  - `failure(error, attempt)` maps every step. A `StorytellerError` passes as it is. The deadline gives `timedOut: true`, status 0 and "The Storyteller API did not answer within <s> s (<path>)." A caller's signal aborted with a `TimeoutError` reason gives `timedOut: true` and "did not answer in time (<path>)". A caller's abort or an `AbortError` gives `aborted: true` and "The request was aborted." Anything else is "not reachable". Errors and reasons are told apart by `name`, not `instanceof`, because a `DOMException` of another realm (jsdom, a polyfilled fetch) fails `instanceof`.
- **`lib/errors.ts`:** `StorytellerError.timedOut` (init `timedOut?`), `isTimedOut`, and the doc of `isTransientError`, which stays true for a timeout.
- **`lib/conditional.ts`:** `ConditionalReadOptions = StorytellerRequestInit & { ifNoneMatch?: string }`. The conditional reads run through `runRequest`, so the limit covers their body read as well.
- **Deviation: `storytellerRequest` was removed.** It returned the response with an unread body, so the body read was outside both the deadline and the error mapping (spec, Current State). `runRequest` takes the body read as a callback instead. `storytellerRequest` had no caller besides the conditional reads and was not exported from the package.
- **Deviation: body-read failures are mapped.** Before, an abort during the body read of `storytellerFetch` rejected with a raw `DOMException`. Now it is a `StorytellerError` like any other step.
- **Tests:**
  - `test/fetcher.test.ts`, a new `storytellerFetch timeouts` block with ten tests: the configured limit, the function form, a per-call override and 0, a caller abort that stays aborted, a caller `AbortSignal.timeout` that gives `timedOut`, a body that does not arrive, a transport that ignores the signal with its late answer discarded, one deadline across the 401 retry, a hanging and a failing `getAccessToken`, and no timer and no listener left behind.
  - `test/conditional.test.ts`: a read ended by its `timeoutMs` with the body read included, and a poller's `AbortSignal.timeout` as a transient timeout.
  - `test/configuration-formats.test.ts`: `isTransientError` of a timeout.
- **`README.md`:**
  - Usage gets `timeoutMs`.
  - The fetcher bullets cover the timeout, `timedOut` and `isTimedOut`, and the messages.
  - A paragraph covers the per-call option with two examples, the browser's queueing, and the caller's signal.
  - The conditional-read paragraph says that a caller's `AbortSignal.timeout` now reads "did not answer in time" with `timedOut`.
  - The `isTransientError` row of the table.

### 7. Admin UI timeouts and their errors

- **`app/boot/api.ts`:**
  - `apiReadTimeoutMs` is 30 s (`GET`, `HEAD`) and `apiWriteTimeoutMs` 120 s (every other method). `apiTimeoutMs({ method })` picks one of them and is passed to `configureStoryteller`.
  - `apiTransferTimeoutMs` is 10 minutes. `stores/transfers.ts` passes it per call to `startViewImport` and to `getViewExportArchive`.
- **`app/boot/query.ts`:** `shouldRetry` returns false for a timeout.
- **`app/utils/errors.ts`:** `describeError` titles a timeout "The API did not answer in time" and sets `timedOut: true` only for a timeout.
- **`app/boot/api-errors.ts`:** the toast has the same title. `shouldReport` is unchanged, so a timeout is reported like any status-0 failure.
- **`app/boot/sentry.ts`:** the message is "Storyteller API did not answer in time", with the fingerprint `['storyteller-api', 'timeout']`.
- **`ErrorState.vue`:** shows the literal `i-lucide-hourglass` for a timeout. A comment says why it is literal: the icon bundle holds only the names its scan finds. The build has the icon in its bundle.

### 8. Screens that would show a wrong state instead of the error

- **`AnnotationDetailPanel.vue`:** `deletedFailed` is a failed deleted lookup other than a 404. It shows `ErrorState` with Retry (`deleted.refetch()`), placed before the "Annotation not found" `EmptyState`.
- **`AnnotationHistory.vue`:** the same check. It shows a compact `ErrorState` titled "The deleted annotation could not be loaded", with Retry, where the banner would be.
- **`ConfigurationEditor.vue`:**
  - The own content's error shows `ErrorState` with Retry (`own.refetch()`), instead of an editable `{}`.
  - `save()` returns while `original` is undefined.

### 9. Writes do not wait for unrelated refreshes

- **`ConfigurationEditor.vue` `save`:**
  - The toast follows the patch, and `invalidateConfiguration` runs in the background.
  - The success path is in `stored(saved, sent)`: the base becomes the document as sent, and the catalog's summary is set.
  - A timed-out patch goes to `settleTimedOutSave`, which reads the content again through `refreshOwn`, each read within the read timeout:
    - equal: "Configuration saved", with "The API answered late, but the content is stored.";
    - different or unreadable: "The API did not answer in time; the change may have been saved. Reload before saving again.", and the draft stays dirty. The first implementation said "the configuration was not saved" for a different content; the final review changed it (see Final review).
  - `create` and `remove` still await `invalidateConfiguration`, as specified.
  - `sameJson` (`utils/json-patch.ts`) compares two documents regardless of key order, and treats an absent one as `{}`.
- **Deviation: a timed-out save is compared with the document as sent, not with the draft.** The editor stays editable while a save runs. An edit made during a slow save would otherwise make a stored save read as "not saved". For the same reason the base after a save is the sent document, so such an edit stays a change.
- **Deviation: `refreshOwn` returns `{ content, read }`.** `read` is false when the effective read or the own read failed. vue-query's `refetch` resolves with the last data even on an error, so the content alone cannot tell "different" from "unreadable". The conflict dialog takes `content`.
- **`TemplateDetail.vue`:** the save and delete show their toast after the write. `afterSave(scope)` starts `invalidateTemplate` and `invalidateAllConfigurations` together, and awaits only the template before `saving` ends. The scope is captured before the write.
- **`useAnnotationActions.ts`:**
  - `createMany` returns after its writes. It invalidates the deleted list and the touched keys in the background, and no longer calls the second `refreshConfigurations`, since the forced catalog load lists the configurations.
  - `restoreMany` awaits only `invalidateDeleted`, and invalidates the restored keys in the background.
  - `removeMany`, `save` and `saveMany` are unchanged.

### 10. The catalog

`app/stores/catalog.ts`:

- `typeListConcurrency = 4`. `pageTypes` runs four lanes over the seven fetchers. The configurations list starts with them.
- After a type list timed out, the load throws that timeout and does not start the `getAnnotations` fallback.
- **Deviation: a timeout also stops the other type lists.** `pageTypes` has its own controller, aborted by the load's signal and by a timeout. The answers of the other lists would not be used, and they would keep their browser connections. Lists stopped that way count as aborted, not as failures.
- `progress`: `CatalogProgress { lists, total, annotations, fallback, replacing }` and `initialProgress()`, reset by every load and by `reset`. `annotations` is the size of the map this load fills, updated only while the load is current. `lists` counts the settled type lists. `loadedCount` keeps its meaning.
- **Deviation: `progress.replacing`.** The spec words the strip as "Refreshing: …" while a shown catalog is replaced, but did not say where the page learns that. `replacing` is true when the load fills its own map while a snapshot or the previous load stays on screen. `catalogProgressText(progress)` builds the text for both pages and is auto-imported.
- The `catch` of a load sets `fromSnapshot = false` and `progress.fallback = false`. A failure ends as `ready` with `error` when something is shown, and as `error` otherwise.
- **Configurations:**
  - `loadConfigurations` collects the pages into a local map and writes it only at the end, while the catalog holds the scope.
  - A failure keeps the summaries known so far, or null when there were none, and sets `configurationsError`. An abort, also between pages, is not a failure.
  - `configurationsError` is cleared by a load and by `refreshConfigurations`.
  - `refreshConfigurations` has its own controller, aborted by the next refresh, by `load()` and by `reset()`.
- **Fix of the review (a regression of this phase):**
  - **The race:** a load replaced while it awaited `readSnapshot` used to continue. Its `fresh` map was the next load's new `byKey`, so it paged its own view's seven lists into the other view's catalog (and into its snapshot). It also overwrote the next load's `pendingChanges` and `progress`. Before, `paginate`'s `throwIfAborted` stopped this. Here `pageTypes` passed the signal only through an `abort` listener, which never fires on a signal that is already aborted.
  - **The fix:**
    - `load()` returns right after the snapshot block when its signal is aborted, before it starts the configurations, picks `fresh`, or touches `pendingChanges` and `progress`.
    - `pageTypes` aborts its own controller at once when the load's signal is already aborted.
  - **The test:** `catalog.test.ts` "drops a load that another view replaced while it read its snapshot". Before the fix it failed with the seven type lists of the first view requested.

### 11. The catalog's pages

- **`annotations.vue`:**
  - The strip shows while a load runs or a snapshot is shown. It has a determinate `UProgress` (`lists` of `total`; indeterminate while the fallback runs) and `catalogProgressText`.
  - A failed refresh with data shows a compact `ErrorState`, "The catalog could not be refreshed", with Retry above the grid.
  - "Reload the catalog" keeps `:loading`. The handler moved to `reload()`.
- **`AnnotationGraph.vue`:**
  - The loading overlay uses `catalogProgressText` while the catalog loads.
  - A failed refresh shows the same compact error below the legend. The error state and the compact error share `reload()`.
- **`configurations.vue`:**
  - "Loading configurations…" stops when `configurationsError` is set.
  - With no summaries known, the page shows `ErrorState`, "The configurations could not be loaded", with Retry (`refreshConfigurations`).
- **`ConfigurationCreateDialog.vue`:**
  - `failed` includes `configurationsError`, with its own text: "The configurations could not be listed, …".
  - Beyond the spec, its Retry lists only the summaries again when the annotations loaded, rather than reloading the whole catalog.

### 12. Tests and docs

- **Vitest, every test the spec lists:**
  - `catalog.test.ts`, a new `catalog store on a slow API` block with nine tests:
    - a stalled list ends with its timeout and without the fallback;
    - never more than four lists in flight;
    - the progress of a same-view reload;
    - the wording;
    - a failed refresh with data, and a timed-out one;
    - the summaries kept with `configurationsError`;
    - a refresh of another view dropped;
    - the race above.
  - `bulk-create.test.ts`: `createMany` asks for no summaries of its own, and the dialog shows its result while an invalidated query hangs.
  - `boot.test.ts`: the limits, the SDK configured with them, no retry of a timeout, and the toast title.
  - `telemetry.test.ts`: the message and the fingerprint.
  - `shell.test.ts`: `describeError`.
  - `configuration-editor.test.ts`: the toast while the documents refresh, a timed-out save that was stored, one that was not, one that cannot be read again, and the own content's error.
  - `soft-delete.test.ts`: the history's banner lookup, and the detail panel's deleted lookup.
  - `configuration-create.test.ts`: the dialog with a failed list of the configurations.
  - `templates-schemas.test.ts`: the template's toast while the configurations refresh.
  - Only the new tests set a timeout. `test/setup.ts` still configures none.
- **Deviation: a racy test was fixed.** In `transfers.test.ts`, "keeps a saved import that could not be read for another reason than a 404" passed its `waitFor` before the 503 was answered, because the import was undefined from the start either way. Its `resetHandlers()` could then put the mock back first, and the mock answers 404 for the unknown job. The test now waits until the handler answered.
- **Docs:**
  - `src/Platform/ui.admin/README.md`: Telemetry (timeouts in Sentry), and three new Architecture bullets (Timeouts, Query defaults, Catalog).
  - `docs/42for.net/platform/admin-ui.md`: the catalog's progress and failed refresh, the timeout error with Retry and the limits, and a save that timed out.
  - `sdk.typescript/README.md`: see section 6.
- **Formatting:** after the review, three lines were rewrapped with Prettier (`ConfigurationEditor.vue`, `boot.test.ts`, `templates-schemas.test.ts`). The user docs now quote the real toast title of a timed-out save that was not stored.

## Final review

A review of the whole branch (three independent lenses, each finding verified against the code) found these in phase 2; all are fixed:

- **A first load that timed out ended `ready` with a partial catalog.** A first load without a snapshot pages straight into the shown map, and without the fallback after a timeout it kept the types it had: `ready` told the breadcrumbs (`AnnotationBreadcrumb.vue`) and the composition (`notInCatalog`) that the ancestors of the missing types did not exist, and the guard of `load()` kept that state when the user came back. The `catch` of `load()` now drops what a first load collected (`fresh === byKey.value`), so it ends in `error` with Retry; a snapshot or a previous load still stays as `ready` with the compact error. Test: `catalog.test.ts` "drops what a first load collected before a type list timed out".
- **The conflict dialog of a 412 spun forever when the stored content could not be read.** `loadTheirs()` reads it (`refreshOwn`) and, when that fails, leaves the error in `theirsError`; `ConflictDialog.vue` gets the props `error` and the event `retry`, shows the compact `ErrorState` with Retry, and disables Overwrite and "Reload, discard mine" while it loads or failed (neither has a base then). Test: `configuration-editor.test.ts` "shows the error of the stored content in the conflict dialog with Retry, not a spinner".
- **A timed-out save whose content read again differs was called "not saved".** The API may still be applying the patch it no longer answers, so a read right after the timeout proves nothing, and a resent patch of unguarded `add` operations could add an array element twice. Only an equal content is a verdict now; anything else says the change may have been saved and asks for a reload. The test of that case expects the cautious toast.
- **Docs:** `admin-ui.md` promised the title "The API did not answer in time" with Retry on any page, which screens that name what failed (the catalog, Configurations) and the writes do not show; it now says what each shows. The README's rule for a timed-out save compared with the draft; it now names the document as sent. Both describe the dropped partial first load and the conflict dialog's error.

## Verification

- **SDK** (`src/Platform/Storyteller/sdk.typescript/src`):
  - `npm run lint` and `npm run typecheck`: exit 0.
  - `npm test`: 334 passed, 1 skipped.
  - `npm run build`: exit 0.
  - `npm run prepare:local`: up to date.
- **Admin UI** (`src/Platform/ui.admin/src`), after the fixes of the final review:
  - `npm run prepare:local`: up to date. The installed SDK copy has the timeout code.
  - `npm run lint`: exit 0.
  - `npm run build` (`vue-tsc --build` and `vite build`): exit 0, and `i-lucide-hourglass` is in the bundle.
  - `npm test`: 1506 passed in 101 files. One full run before it hit a 10 s hook timeout in `test/unit/router/page-memory.test.ts` (the hook builds the router with every page), which passed alone (19 of 19) and in the next full run; the implementation saw the same once..
  - `npm run test:e2e` (Playwright, mocked API, desktop and mobile): 389 passed, 9 skipped in the full run before the final review. After it, a full run had two desktop specs fail under the load of six workers: `configuration.spec.ts:320` evaluated the mock's test hook before the page had it, and `deleted.spec.ts:78` ran over its 30 s limit. Both passed 3 of 3 alone, and the next run of the desktop project passed 198 of 198. `deleted.spec.ts:78` takes 16 s alone and 26–28 s in a full run, close to its limit..
- The automation tests' full suite against this branch's mock build passed as well (phase 3 review).
