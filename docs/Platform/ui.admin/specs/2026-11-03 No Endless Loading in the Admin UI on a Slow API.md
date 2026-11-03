# No Endless Loading in the Admin UI on a Slow API

Linear: [424-166](https://linear.app/424fun/issue/424-166/admin-ui-stalls-on-slow-api-calls-catalog-history-bulk-create) — Admin UI stalls on slow API calls: catalog, history, bulk create, configuration save and template stay loading.

## Problem

Ten UI automation tests (Testiny TR-4, run `20261102-230831-hviz`, the `dev` profile against the local stack with the Windows Cosmos DB emulator) failed because a page never left its loading state within the test's 5–10 s waits:

- the catalog (cases 110, 111): "Reload the catalog" stayed disabled while the catalog paged;
- the History tab (178, 67, 220, 222): "Loading the history…", and the deleted banner came late;
- Bulk create, the setup of case 177: the Create button kept spinning, `POST …/annotations/simple` was pending;
- the configuration save (221, 192): the Save button kept spinning, no "Configuration saved (v2)" toast;
- the template detail (74): "Loading…", `GET …/default/template/rst` never answered.

The traces show the local API answering in 1–6 s per GET, even in an empty disposable project, and requests still pending when the tests ended. Three faults add up to that.

1. **Every new connection from the API to the local emulator costs 2 s.** On Windows, `localhost` resolves to `::1` first; the Windows Cosmos DB emulator listens on `127.0.0.1:8081` only, and Windows needs about 2 s to give up on `[::1]:8081` before it tries IPv4. Measured on the development machine (2026-11-03): a TCP connect to `localhost:8081` takes 2041 ms, to `127.0.0.1:8081` 4 ms. The API's connection string in the `local` Cosmos mode (the `*-local-cosmos` launch profiles of the Aspire host, `func start` with `local.settings.json`, DbCreator on its own) is `https://localhost:8081/`; the TR-4 run used it (its leaked organization container `org.at-20261102-230831-hviz-…` is in the Windows emulator). The Aspire `container` mode is not affected: Aspire builds the emulator's connection string with the IPv4 host. The Cosmos work itself is cheap: the seven type lists of the 423-annotation automation view cost 5–129 ms and 3.4–7.9 RU each, one page, no continuation; a warm API answers them in 30–70 ms. The penalty is paid by every request that needs a new pooled connection: the first requests of a worker, every burst of parallel requests larger than the pool (four parallel tests, each loading a catalog of eight lists, are 32 concurrent requests), and the first request after the pool dropped idle connections (measured with `func start` on this branch's base: eight parallel lists 1.9–2.1 s each after 75 s without requests, 20–40 ms right after). No code path of the API hangs: no lock is held across I/O, every retry is bounded. The `Dictionary` of `ContainerRepositoryProvider` is written without a lock by parallel requests of a cold worker, which can throw (a sporadic 500), not hang.
2. **The admin UI has no request timeout and holds its spinners on unrelated work.** The SDK's fetcher has no deadline, so a request that never answers keeps its spinner forever (history, template detail, Bulk create, Save). Saves and bulk actions also await the refetch of every active query below the touched paths before they show their toast or end their spinner. The catalog fires its seven type lists and the configurations list at once; with the API on cross-origin `http://localhost:7071` the browser has six HTTP/1.1 connections per origin and sends a CORS preflight per URL, so the catalog occupies every connection and the page's own requests queue behind it. A failed catalog refresh that already had data shows nothing, and "Refreshing…" can spin forever; a failed configurations list leaves "Loading configurations…" forever. During a reload of the same view the progress text repeats the old count.
3. **The tests fight the slow API.** `AnnotationHistory.WaitForEntryAsync` reloads the page every 5 s, which cancels a history request slower than 5 s and restarts the catalog, so it can never succeed. `WaitForCatalogAsync` passes on the catalog's error state. Nothing removes the data of earlier runs (the documented rule is "delete nothing"): the shared view `ui-tests/automation` held 423 annotations from runs of the same evening. The data size is not what makes the API slow (see 1), but the view grows with every run.

## Current State

Paths of the admin UI are relative to `src/Platform/ui.admin/src`, of the SDK to `src/Platform/Storyteller/sdk.typescript/src`, of the tests to `src/Platform/UI.Admin.AutomationTests/src`.

### The local API

- `src/Platform/Storyteller/Backend.CosmosDb/src/CosmosClientProvider.cs:50–58` — with `ShouldAcceptAnyCertificate` (set automatically for a `localhost` or `127.0.0.1` connection, `EntryPoint.cs:51–55`) the client uses Gateway mode, `LimitToEndpoint`, and `HttpClientFactory = () => new HttpClient(new HttpClientHandler { … DangerousAcceptAnyServerCertificateValidator })`. The connection goes to the host of the connection string: `localhost` in `Api.Functions/src/local.settings.json:13`, `Aspire.Host/src/Properties/launchSettings.json:43,59` and `DbCreator/src/app.config.json:3`; user secrets and `ConnectionStrings:cosmos` override these files.
- `src/Platform/Storyteller/KeyValues.Azure.KeyVault/src/KeyVaultClients.cs:24–28` and `CustomerKeyProvider.cs:216–221` — the Key Vault emulator transport is `new HttpClientTransport(new HttpClientHandler { … })` to `https://localhost:<port>` (Lowkey Vault, published on `127.0.0.1` only); `CustomerKeyProvider.GetKeyClient` builds a new handler, so a new connection pool, on every call. Only secret operations of key values use it.
- `src/Platform/Storyteller/Backend.CosmosDb/src/ContainerRepositoryProvider.cs:8,27–35` — a singleton caching repositories in a plain `Dictionary` written without a lock.
- Other per-request traffic is not affected: the Functions host talks to the worker over `127.0.0.1`, Azurite is reached through `UseDevelopmentStorage=true` (`127.0.0.1`), AuthKit is remote.
- The Functions host listens on IPv4 only (a request to `[::1]:7072` is refused after 2 s): browsers and .NET clients that call `http://localhost:7071` pay the IPv6 attempt per new connection too (Chromium falls back to IPv4 after about 300 ms).

### The SDK

- `lib/fetcher.ts:3–17` — `StorytellerConfig` has `baseUrl`, `getAccessToken`, `onUnauthorized`, `fetch`, `headers`; no timeout.
- `lib/fetcher.ts:49–58` — `storytellerFetch` reads the body after `storytellerRequest`, outside any error mapping: an abort during the body read rejects with a raw `DOMException`.
- `lib/fetcher.ts:85–113` — `send` awaits `getAccessToken` unbounded, then `fetch`; a failure becomes `StorytellerError` status 0, `aborted` only for a `DOMException` named `AbortError`. A caller's `AbortSignal.timeout` therefore reads "The Storyteller API is not reachable (…)" with `aborted: false`, which `README.md:243,252` documents for pollers (the error stays transient).
- `lib/errors.ts:4–46,76,97–113` — `StorytellerError` has `aborted`, no timeout flag; `isTransientError` is true for status 0 when not aborted.
- `lib/conditional.ts:21,95,110,113` — the conditional reads use `storytellerRequest` and read the body themselves; `storytellerRequest` has no other caller.
- The generated functions type their options as `Parameters<typeof storytellerFetch>[1]` (138 of them, and their copies in `generated/vue-query`), and the composables' `request` as `SecondParameter<typeof storytellerFetch>`; Orval reads only the mutator's number of parameters, so widening the type of the second parameter needs no regeneration. Every generated `queryFn` passes TanStack's `signal`; mutations get none.
- The admin UI installs the SDK as a packed copy of its `dist` (`"file:../../Storyteller/sdk.typescript/src"`, `install-links=true`), so an SDK change reaches it in the same commit.

### The admin UI

- `app/boot/api.ts:9–15` — `configureStoryteller({ baseUrl, getAccessToken, onUnauthorized })`, no timeout.
- `app/boot/query.ts:5–12` — `shouldRetry` retries any non-4xx read once (status 0 included).
- `app/boot/api-errors.ts:23–49`, `app/utils/errors.ts:14–31`, `app/boot/sentry.ts:66–77` — status 0 is "The API is not reachable" (toast, Sentry fingerprint `['storyteller-api', '0']`); `components/common/ErrorState.vue:9–15` shows `i-lucide-wifi-off` for it.
- `app/stores/catalog.ts`:
  - 43–51, 277 — seven type lists paged at once (`Promise.allSettled`), plus `loadConfigurations` (269);
  - 283–285 — a failed list starts the full `getAnnotations` fallback after all seven settled;
  - 191–194 — `loadedCount` moves only on a first load without a snapshot;
  - 198–224 — a failed configurations list sets `configurations = null`, and nothing else records the failure;
  - 304–309 — a failure with data ends as `ready` with `error`, and `fromSnapshot` stays true;
  - 333–337 — `refreshConfigurations` uses a controller nobody aborts, so a refresh of one view can finish after a load of another and write its summaries there.
- `app/pages/[org]/[project]/[view]/annotations.vue:224,236–245,269–283` and `graph.vue:56–65` — "Reload the catalog" is `:loading` (Nuxt UI disables a loading button) while `status === 'loading'`; the strip shows `Refreshing…` or `Loaded N annotations…`; only `status === 'error'` shows an error. `components/graph/AnnotationGraph.vue:646–683` is the same for the graph.
- `app/pages/[org]/[project]/[view]/configurations.vue:148–153` — "Loading configurations…" while `configurations` is null and the status is not `error`.
- `app/components/configuration/ConfigurationCreateDialog.vue:24–28` — `failed` looks at `catalog.error` only.
- `app/composables/useAnnotationActions.ts` — `restoreMany` (199–202) and `createMany` (252–255) await the invalidation of the deleted lists and of every touched key before they return; `createMany` then starts `catalog.load(force)` and a second, redundant `refreshConfigurations` (259–260).
- `app/components/configuration/ConfigurationEditor.vue`:
  - 112–152 — `save` awaits `invalidateConfiguration` (the effective and own documents, the list and the history) before the toast; any failure other than 409 and 412 is "The configuration was not saved";
  - 187–239 — `create` and `remove` await the same, which is what turns the empty state into the editor and back;
  - 311 — the own content shows a spinner while it loads and has no error branch: after an error the editor offers `{}` with `original` undefined, and `buildPatch(undefined, …)` would send `add` operations without `test` guards.
- `app/components/templates/TemplateDetail.vue:115–119,132–133,163–164` — save and delete await `invalidateTemplate` and `invalidateAllConfigurations` (every active configuration query of the view) before the toast.
- `app/components/annotations/AnnotationDetailPanel.vue:24–27,48–57` — `GET annotation`, on its 404 `GET annotations/deleted/{key}` (`queries/scope.ts:48–57`: silent, no retry), then the history: a failed deleted lookup shows "Annotation not found".
- `app/components/annotations/AnnotationHistory.vue:45–54` — the deleted banner shows when its lookup has data (an error shows nothing); the history spins, then `ErrorState` with Retry on an error. `TemplateDetail.vue:204–218` and `ConfigurationEditor.vue:248,270` are the same: a request that never answers never reaches the error.
- The calls that can legitimately take long: `app/stores/transfers.ts:160,183` (view import upload, export download), and the writes whose server work grows with the input (Bulk create's 100 keys per request, restore, delete and purge of subtrees, the schema writes with their compliance check, the bulk key-value actions, the key-value import and export).

### The automation tests

- `testsettings.json:33–35` — Action and Expect 10 s, Navigation 30 s; `testsettings.dev.json` sets only the base URL.
- `Pages/AnnotationsPage.cs:55–58` — `WaitForCatalogAsync` waits for "Reload the catalog" to be enabled (`ToBeEnabledAsync`, which does not require visibility), which the error state also satisfies. `PlatformTargetCheck.cs:44` waits for the loaded catalog, its error or "no access" with the Expect timeout. `Pages/GraphPage.cs:131–160` waits for the graph's catalog with the Expect timeout.
- `Pages/AnnotationHistory.cs:16,41–58` — reads for 5 s (`ReadTimeoutMs`), then reloads the page, up to 30 s; `AnnotationDetail.ANN.cs:113–117` (`OpenHistoryTabAsync`) waits for the region with the Expect timeout.
- `Pages/BulkCreateDialog.cs:13,60` (built from an `IPage` in `AnnotationsPage.cs:84` and `AnnotationCatalogTests.cs:167`), `Pages/ConfigurationDetail.cs:73–75`, `Pages/ModelingTemplatePage.cs:69,173–178`, `Tests/Annotations/AnnotationDeleteTests.cs:103,116,121,126` — Expect's 10 s; none of them ends on the UI's failure (the dialog's alert, the "not saved" toasts, an `ErrorState`).
- `App` is built in `PlatformTest.cs:25`, `PlatformTargetCheck.cs:32`, `PlatformTest.Helpers.cs:175`, `Access/AuthKitSignIn.cs:45`, `Tests/…/SignInTests.cs:194` and `Tests/…/MyInvitationTests.cs:113`.
- `Data/PlatformData.cs:10–11` and `docs/Platform/UI.Admin.AutomationTests/automation-tests.md:155` — "delete nothing: the growing data set is intended load" (the owner's decision of 2026-10-31). The spec `2026-10-31 UI Automation Testing Project.md:346` planned a `Maintenance` test `PurgeAutomationData`, skipped unless `Maintenance:Purge=true`, deleting `at-*` items older than `Maintenance:OlderThanDays`; `AutomationCategories.Maintenance` exists, the test does not. A test without `[TestCase]` is not reported (with `Reporting:UnmappedTests=Warn` it is a warning).
- Generated annotation names are `at-<runId>-<hex4>-<word>-<word>` with `runId = yyyyMMdd-HHmmss-xxxx`; every generated key has an `sbt.at-…` or `rst.at-…` root, because Bulk create adds the missing ancestors. The API lists roots by name (`nameQuery=^at-` is a regular expression), lists deleted roots (`GET …/annotations/deleted`, with `Created.At` and `Deletion.DeletedAt`), soft-deletes a tree (`DELETE …/annotations/{key}`, Contributor) and purges a deleted one (`DELETE …/annotations/deleted/{key}`, Administrator; 404 when gone, 409 when restored meanwhile).

## Proposed Changes

The fix has three phases, each with its own commits and review. Phase 1 changes Storyteller, so its review lives in `docs/Platform/Storyteller/specs/reviews/` and links this spec; the reviews of phases 2 and 3 live in `docs/Platform/ui.admin/specs/reviews/`.

### Phase 1 — The local API connects to the emulators over IPv4

#### 1. `EmulatorHttpHandler` (`Backend.Core`)

A new public static class `_42.Platform.Storyteller.EmulatorHttpHandler` in `src/Platform/Storyteller/Backend.Core/src/EmulatorHttpHandler.cs` (BCL only), for local development only:

```csharp
public static class EmulatorHttpHandler
{
    // A handler for a local emulator: any server certificate is accepted (the emulators are self-signed), no proxy,
    // a 15 s connect timeout, and "localhost" connects to 127.0.0.1 first and to ::1 only when IPv4 fails.
    public static SocketsHttpHandler Create();

    // The addresses a connection tries, in order: 127.0.0.1 then ::1 for "localhost" (any case, a trailing dot
    // allowed); the host itself otherwise.
    internal static IReadOnlyList<EndPoint> Candidates(DnsEndPoint endPoint);

    // The connect step of Create: the candidates in order, the first that connects wins; a failed socket is disposed,
    // and the last failure is rethrown. A host other than localhost connects as SocketsHttpHandler would.
    internal static ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, CancellationToken cancellationToken);
}
```

The URI, the SNI and the `Host` header stay `localhost`: Lowkey Vault's key ids carry the request host, and `CustomerKeyProvider.cs:147` compares the host of a wrapping key with the configured one, so the connection strings are not rewritten. IPv4 first is right for every emulator of the repository (the Windows Cosmos DB emulator, Docker port publishing on `127.0.0.1`); a service bound to `::1` only still works, through the fallback.

#### 2. Cosmos DB client (`CosmosClientProvider.cs`)

In the `ShouldAcceptAnyCertificate` branch, one handler per provider: `_emulatorHandler = EmulatorHttpHandler.Create()` and `HttpClientFactory = () => new HttpClient(_emulatorHandler, disposeHandler: false)`. A new `HttpClient` per factory call, because the SDK sets its timeout and default headers on the client it gets and disposes it with the `CosmosClient`; the handler is disposed by `Dispose` after the client (null-checked: it is created in the lazy `BuildClient`). Gateway mode and `LimitToEndpoint` stay; `GatewayModeMaxConnectionLimit` is ignored with a factory and stays unset. The SDK's own `ServerCertificateCustomValidationCallback` would keep its handler, which has no connect hook, so the factory stays. The configuration files keep `localhost`.

#### 3. Key Vault emulator (`KeyVaultClients.cs`, `CustomerKeyProvider.cs`)

The emulator transports become `new HttpClientTransport(new HttpClient(KeyVaultClients.EmulatorHandler, disposeHandler: false))` over one process-wide handler (`internal static` lazily created `EmulatorHttpHandler.Create()` in `KeyVaultClients`, used by both); `CustomerKeyProvider` stops creating a handler per call.

#### 4. Repository cache (`ContainerRepositoryProvider.cs`)

`ConcurrentDictionary<string, IContainerRepository>` with `GetOrAdd`.

#### 5. Tests and docs

- `Backend.Core/test/EmulatorHttpHandlerTests.cs`: the candidates of `localhost`, `LOCALHOST`, `localhost.` and of another host (deterministic); `ConnectAsync` to `localhost` reaches a `TcpListener` bound to `127.0.0.1` only (on Windows also well below the 2 s of the IPv6 attempt); a listener on `::1` only is reached through the fallback (skipped where IPv6 loopback is unavailable, and on a port where nothing listens on IPv4); an HTTP request through `Create()` reaches a minimal HTTP responder on `127.0.0.1` by the name `localhost`.
- `Backend.CosmosDb/test/ContainerRepositoryProviderTests.cs`: a plain class (not `BaseTestsClass`, not the Cosmos collection, so no emulator starts or is wiped) with a mocked `CosmosClient`: parallel `GetOrganizationContainer` calls return one repository per name (a smoke test; the race cannot be forced).
- An A/B against the local emulator, read-only: the API started with `func start` before and after the change, the seven type lists and the configurations list of `ui-tests/automation/default` in parallel bursts (cold, warm, after the pool dropped idle connections).
- Docs: `docs/Platform/development.md` (Troubleshooting), `docs/Platform/Aspire.Host/local-development.md` (the `local` Cosmos mode) and `docs/Platform/Storyteller/key-values.md:226` (the emulator transport): the API connects to the local emulators over IPv4; a .NET client of an IPv4-only local service (the MCP server and `sform` against `localhost:7071`) still pays the IPv6 attempt.

### Phase 2 — Requests end, and spinners do not wait for unrelated work

#### 6. SDK request timeout (`lib/fetcher.ts`, `lib/errors.ts`, `lib/conditional.ts`, `index.ts`)

- `StorytellerConfig.timeoutMs?: number | ((request: { method: string; url: string }) => number | undefined)` — the longest a request may take, from the call until its body is read, across the token lookup and the retry after a 401. A function picks the limit per request (e.g. by method). Undefined or 0: no timeout (today's behaviour; other SDK users are unaffected).
- `export type StorytellerRequestInit = RequestInit & { timeoutMs?: number }` — `storytellerFetch(url, init?: StorytellerRequestInit)` keeps its two parameters (a third would change Orval's output), so every generated function and composable accepts a per-call `timeoutMs` (0 disables) through its existing `options` parameter; `ConditionalReadOptions` becomes `StorytellerRequestInit & { ifNoneMatch?: string }`. `timeoutMs` never reaches `fetch`.
- An internal `runRequest(url, init, read: (response) => Promise<T>)` owns the deadline: an `AbortController` with `setTimeout` (cleared in `finally` after `read`, `unref`'d where the runtime has it), combined with the caller's signal by a listener that is removed in `finally` (no dependency on `AbortSignal.any` or `AbortSignal.timeout`). `storytellerFetch` and the conditional reads use it. Without a deadline the caller's signal is passed as it is (`conditional.test.ts:192` checks its identity).
- The token lookup, `fetch` (a `config.fetch` override may ignore the signal) and the body read are each raced against the combined signal.
- Failure mapping, one helper for every step:
  - the deadline fired → `timedOut: true`, `aborted: false`, status 0, `The Storyteller API did not answer within <s> s (<path>).`;
  - the caller's signal aborted with a `TimeoutError` reason (`AbortSignal.timeout`) → also `timedOut: true`, `aborted: false` (it stays transient, as the README documents for pollers);
  - the caller's signal aborted otherwise, or an `AbortError` → `aborted: true`, "The request was aborted." (as today);
  - anything else → "not reachable" (as today).
- `StorytellerError.timedOut: boolean` (init `timedOut?`), `export const isTimedOut`. `isTransientError` stays true for a timeout.
- Tests (`test/fetcher.test.ts`, `test/conditional.test.ts`, `test/configuration-formats.test.ts`): the config timeout, the function form, a per-call override, 0 disables, a caller abort wins and stays `aborted`, a caller `AbortSignal.timeout` gives `timedOut`, a timeout during the body read, a `fetch` override that ignores the signal, one deadline across the 401 retry, a hanging `getAccessToken`, no timer and no listener left after a request, `isTransientError` of a timeout.
- `README.md`: `timeoutMs` in Usage, the per-call option, `timedOut` / `isTimedOut` in the error bullets, the conditional-read paragraph (a caller's `AbortSignal.timeout` now reads "did not answer"), and that the limit counts the time a browser queues the request.
- No conformance change: the `errors` vectors are HTTP answers only, and the TypeScript SDK does not run `refresh`.

#### 7. Admin UI timeouts and their errors (`app/boot`, `app/utils/errors.ts`, `ErrorState.vue`)

- `app/boot/api.ts`: reads (`GET`, `HEAD`) 30 s (`apiReadTimeoutMs`; the .NET configuration provider's read timeout is 30 s as well), every other method 120 s (`apiWriteTimeoutMs`: Bulk create, restore, delete, purge, the schema writes, the bulk key-value actions and the key-value import grow with their input). The view import's upload and the export's download pass `{ timeoutMs: 10 * 60_000 }`.
- `app/boot/query.ts`: `shouldRetry` does not retry a timeout (a second wait before the error is worse than the error's Retry). On a deployed API a cold start that takes longer than 30 s shows the error on the first page.
- `app/utils/errors.ts`: a timeout's title is "The API did not answer in time"; `ErrorDescription` gets `timedOut: true` only for a timeout. `app/boot/api-errors.ts`: the same toast title; a timeout is still reported like any status-0 failure. `app/boot/sentry.ts`: the message "Storyteller API did not answer in time" and the fingerprint `['storyteller-api', 'timeout']`, so timeouts do not merge with network failures.
- `ErrorState.vue`: the literal icon `i-lucide-hourglass` for a timeout.

With these, the History tab, the template detail, the configuration editor and every other `useQuery` reach their existing `ErrorState` with Retry, and the writes reach their existing failure handling (Bulk create's alert with "Try again", the restore and delete toasts).

#### 8. Screens that would show a wrong state instead of the error

- `AnnotationDetailPanel.vue` — a failed deleted lookup other than 404 shows `ErrorState` with Retry, not "Annotation not found".
- `AnnotationHistory.vue` — a failed lookup of the deleted banner shows a compact `ErrorState` with Retry.
- `ConfigurationEditor.vue` — the own content's error shows `ErrorState` with Retry (`own.refetch()`) instead of an editable `{}`, and `save()` does nothing while the own content is unknown.

#### 9. Writes do not wait for unrelated refreshes

- `ConfigurationEditor.vue` `save` — the toast follows the patch; `invalidateConfiguration` runs in the background (the watcher of the own content does not replace a dirty draft). When the patch timed out, the editor reads its own content again (bounded by the timeout): equal to the draft → the save counts as done ("Configuration saved"); different → "The API did not answer in time; the configuration was not saved." and the draft stays dirty; unreadable → "The API did not answer in time; the change may have been saved. Reload before saving again." (a patch of `add` operations has no `test` guards, so blindly sending it again could add an array element twice). `create` and `remove` keep awaiting the refresh: the switch between the empty state and the editor depends on it, and it is bounded now.
- `TemplateDetail.vue` save and delete — the toast after the write; `invalidateTemplate` is still awaited before `saving` ends (whether the template exists decides between create and patch on the next save), `invalidateAllConfigurations` runs in the background.
- `useAnnotationActions.ts` — `createMany` returns after its writes, its invalidations run in the background, and it drops the second `refreshConfigurations`; `restoreMany` still awaits `invalidateDeleted` (the Deleted page's own list), the per-key invalidations run in the background. `removeMany`, `save` and `saveMany` stay as they are (their awaits are the screen's own data, and bounded).

#### 10. The catalog (`app/stores/catalog.ts`)

- **At most four type lists at once** (`typeListConcurrency = 4`, lanes over the seven fetchers), so the catalog leaves browser connections to the page's own requests; the configurations list still starts with them.
- **No fallback after a timeout:** when a type list failed with `isTimedOut`, the load does not start `getAnnotations` (it would wait again); it ends with the error.
- **Progress of every load:** `progress = { lists, total: 7, annotations, fallback }`, reset when a load starts; `annotations` is the size of the map this load fills (visible or not), `lists` the settled type lists, `fallback` true while `getAnnotations` runs. `loadedCount` keeps its meaning (the annotations shown, the filter bar's total).
- **A failed load always ends:** `catch` also sets `fromSnapshot = false`, so "Refreshing…" cannot spin after an error.
- **Configurations:** a failed list keeps the summaries known so far (null only when there were none) and sets `configurationsError` (cleared by the next load or refresh); an abort between pages (`signal.aborted`) is not a failure. `refreshConfigurations` has its own controller, aborted by the next refresh, by `load()` and by `reset()`, and writes only while the catalog still holds its scope.

#### 11. The catalog's pages

- `annotations.vue` — the strip reads `Loaded <n> annotations, <k> of 7 types…` on a first load, `Refreshing: <n> annotations, <k> of 7 types…` while a shown catalog is replaced, and `… all types…` while the fallback runs, with a determinate `UProgress` (`k` of 7). A failed refresh with data shows a compact `ErrorState` "The catalog could not be refreshed" with Retry above the grid. "Reload the catalog" keeps its disabled-while-loading contract (the automation tests, the e2e specs and `GraphPage.ReloadCatalogAsync`'s observer rely on it); with the timeouts a load always ends.
- `AnnotationGraph.vue` — the same text in the loading overlay, and the compact error for a failed refresh.
- `configurations.vue` — `ErrorState` with Retry (`refreshConfigurations`) when `configurationsError` is set and no summaries are known, instead of "Loading configurations…" forever.
- `ConfigurationCreateDialog.vue` — `failed` includes `configurationsError`.

#### 12. Tests and docs

- Vitest (`test/unit`): the catalog (a stalled list ends in an error without the fallback; never more than four type lists in flight; progress during a same-view reload; a failed refresh with data gives `ready` + `error` and `fromSnapshot` false; a failed configurations list keeps the summaries and sets `configurationsError`; a refresh of another view is dropped; `createMany` adds no configurations request of its own), `boot.test.ts` (no retry of a timeout, the toast title), `telemetry.test.ts` (the fingerprint), `shell.test.ts` (`describeError`), the configuration editor (the toast while the refetch hangs; a timed-out save that was stored; one that was not; the own content's error), the detail panel and the history (a failed deleted lookup), Bulk create (the result while an invalidated query hangs), the template detail. The SDK tests run in Node; the UI's tests configure the SDK without a timeout (`test/setup.ts`), so only the new tests set one.
- `src/Platform/ui.admin/README.md` (Architecture: errors and timeouts, query defaults, the catalog's loading), `docs/42for.net/platform/admin-ui.md` (The catalog: progress and a failed refresh; a request that does not answer in time shows an error with Retry; a save that timed out), `sdk.typescript/README.md` (section 6).

### Phase 3 — The automation tests wait for the API, and old runs can be purged

#### 13. `Target:ApiTimeoutMs`

A new `PlatformTarget` setting, default 65000 (`testsettings.json`; the mock profile 15000): how long a wait for an answer of the API may take — two reads in a row (an annotation, then its history or its deletion) at the admin UI's 30 s read timeout, plus a margin — so a stalled request ends in the UI's error state, which the wait reports, rather than in a bare test timeout. Waits that pass are not slower. `App` carries it (`App(IPage page, string? mockScenario = null, int apiTimeoutMs = 65000)`), set at every construction site (`PlatformTest.cs`, `PlatformTargetCheck.cs`, `PlatformTest.Helpers.cs`; `AuthKitSignIn.cs`, `SignInTests.cs` and `MyInvitationTests.cs` keep the default). Expect's 10 s stay for everything else.

#### 14. Waits that report the UI's error

- `AnnotationsPage.WaitForCatalogAsync` — locators with `IncludeHidden` (a full-screen detail hides the list below md), `Expect(CatalogLoaded.Or(CatalogError).Or(CatalogRefreshError).First).ToBeAttachedAsync(ApiTimeoutMs)`, then an explicit check: the error state or "The catalog could not be refreshed" (`GetByText`, the compact `ErrorState` is an alert, not a heading) throws a `PlaywrightException` with the error's text. `PlatformTargetCheck` keeps its own wait (it tells "no access" apart and wraps failures in `TargetCheckException`) with `ApiTimeoutMs`. `GraphPage.WaitForCatalogAsync` and `ReloadCatalogAsync` wait with `ApiTimeoutMs`.
- `AnnotationHistory.WaitForEntryAsync` — each round first waits up to `ApiTimeoutMs` for the timeline (the region "History"), "No history" or the detail panel's Retry (scoped to the panel, `.First`); an error state throws, outside the `try` that reloads; a loaded timeline (or "No history", which a lagging read of a deployed API can answer) without the entry reloads the page after a 1 s pause. The 30 s budget for the eventually consistent audit starts at the first loaded timeline. `AnnotationDetail.OpenHistoryTabAsync` waits for the region with `ApiTimeoutMs`.
- `BulkCreateDialog` — built from `App` (its two construction sites change); `CreateAsync` waits up to `ApiTimeoutMs` for the result or the dialog's alert (`.First`); the alert throws with its text.
- `ConfigurationDetail.SaveAsync` and `ModelingTemplatePage.SubmitAsync` — wait for the "saved" toast or the "not saved" toast within `ApiTimeoutMs`; the latter throws with its description.
- `ModelingTemplatePage.OpenAsync` — the editor, the empty state or the panel's Retry within `ApiTimeoutMs`; the error throws with its text.
- `DeletedAnnotationBanner.WaitForAsync()` — the banner, "Annotation not found" or the panel's Retry within `ApiTimeoutMs`; anything but the banner throws. Used at the four waits of `AnnotationDeleteTests`.

#### 15. `PurgeAutomationData` (the `Maintenance` test of the 2026-10-31 plan)

The owner's data rule stays the default: nothing is deleted unless a run asks for it. The issue asks for a way to keep the shared view from growing; the purge planned on 2026-10-31 is that way, now built.

- Settings (`Maintenance` section, `MaintenanceSettings`): `Purge` (default false everywhere), `OlderThanHours` (default 72), `BudgetSeconds` (default 120). A run purges with `--test-parameter Maintenance:Purge=true` (or `UITESTS_Maintenance__Purge=true`), e.g. `dotnet run --project … -- --treenode-filter "/**[Category=Maintenance]" --test-parameter Maintenance:Purge=true`.
- `Tests/Maintenance/AutomationDataTests.PurgeAutomationData`: `[Category(AutomationCategories.Maintenance)]`, `[Serial]` (alone, after the other tests, so it neither competes with them for the API nor holds an Administrator while they run), acts as an Owner or Administrator of `Target:Project` (`RequiredUser => AdminUser`, `AppRoutes.For(Target)`, independent of `Target:RoleProjects`), no `[TestCase]` (not a functional case, not reported). Skipped with a reason unless `Maintenance:Purge` is true; always skipped on the mock.
- It works through the API with the signed-in user's own token, not through the UI (hundreds of trees through the grid would be slow and brittle). It opens `/orgs` and takes the `Authorization` header and the API base from the app's first request that has one: not `OPTIONS`, a URL with `/v1/` other than `/v1/auth/configuration`, read with `HeaderValueAsync("authorization")`. It calls the API through the browser context's `APIRequest`. On a 401 it opens `/orgs` again and takes the header once more; a second 401 fails the test.
  1. lists the roots `subjects` and `responsibilities` of the target view with `nameQuery=^at-` and `includeInactive=true`, following continuation tokens;
  2. keeps the roots whose name is automation data (`^at-\d{8}-\d{6}-[a-z0-9]{4}-`), not of the current run, and created (`Created.At`, the server's UTC) more than `OlderThanHours` ago;
  3. deletes them (`DELETE …/annotations/{key}`, which takes the descendants and their configurations), the `sbt` roots before the `rst` roots (their descendants overlap), four at a time; 404 counts as done;
  4. purges the deleted automation roots of the view deleted more than `OlderThanHours` ago or just now by step 3 (`GET …/annotations/deleted`, `Deletion.DeletedAt`; `DELETE …/annotations/deleted/{key}`); 404 is done, 409 is skipped (restored meanwhile);
  5. stops at `BudgetSeconds` (the next run continues) and writes what it deleted, purged and left to the test output.
- It fails only when the API refused it (a second 401, a 403, a 5xx), with the response; keys left for the next run are not a failure.
- Not covered: disposable projects and organizations (the API cannot delete projects), key values `at_<runId>…`, the views of `Target:RoleProjects`.

#### 16. Docs

`docs/Platform/UI.Admin.AutomationTests/automation-tests.md`: the spec in the list of specs; `Target:ApiTimeoutMs` and the `Maintenance` keys under Configure; Data rules (nothing is deleted unless a run purges; what `PurgeAutomationData` deletes, how to run it); the sentence that every test is linked to a case (the maintenance test is not); a Troubleshooting row "a page stays loading on the dev profile" (the local Cosmos mode before this fix, the IPv4-only Functions host). `Data/PlatformData.cs:10–11` comment.

### Verification

- `dotnet build` of the changed .NET projects; `dotnet test --project` of `Backend.Core/test` and of `Backend.CosmosDb/test` filtered to the new class (never the Cosmos suites against the local emulator); one Cosmos suite with `STORYTELLER_TESTS_USE_CONTAINER=true` when Docker has room, as the end-to-end check of the new handler.
- The API A/B of section 5.
- The SDK: `npm run lint`, its type check, `npm test`, `npm run build`; the admin UI: `npm run prepare:local`, `npm run lint`, `npm run build`, `npm test`.
- The automation project builds. With the local stack (the API of this branch, the admin UI's dev server on 5173) and the pool's secrets on the machine, the ten cases (110, 111, 177, 178, 67, 220, 222, 221, 192, 74) are run again with the `dev` profile (issue step 4).

### Out of scope

- A single catalog endpoint or server-side paging: the type lists cost milliseconds on the server.
- Calling the IPv4-only Functions host by `127.0.0.1` from the admin UI's dev server, the MCP server and `sform` (`AppHost.cs:83,98`, `.env.development`): about 300 ms per new browser connection, 2 s per new .NET connection; documented, left for a follow-up.
- A warm-up of the API at start, a per-request memo of the caller's account (Release builds read it per request), a `ReadMany` pre-check in Bulk create.
- "Reload the catalog" usable during a load (it would change the contract of the tests and e2e specs); progress is shown instead, as the issue allows.
- A cancellable Bulk create dialog, a "still loading" hint in `LoadingState`, a parallel lookup of the deleted annotation in the detail.
- Moving the ten tests to `[DisposableProject]`: a disposable project needs one of the two Owners of the pool and can never be deleted, and case 74 already ran in one.
- Purging by default: it would reverse the owner's "delete nothing" rule; a profile can switch it on with one setting.
