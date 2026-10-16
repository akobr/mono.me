# Phase 2 of the Export and Import of a View

## Overview

Phase 2 covers the API, the OpenAPI document and both SDKs (sections 2.1-2.3) of [Export and Import of a View](../2026-10-15%20Export%20and%20Import%20of%20a%20View.md), Linear 424-40. The five transfer routes of PR #88 move from `…/configurations/` to `v1/{organization}/{project}/{view}/exports` and `…/imports`. They have the tag `Transfers` and the view-level operations `StartViewExport`, `GetViewExport`, `GetViewExportArchive`, `StartViewImport` and `GetViewImport`. The export body takes `Include`, and the import query takes `include` and `existing`; the routes pass them to `IViewTransferService`, whose signature Phase 1 had already extended. Each included kind needs its scopes, also when a job is read, and a missing scope answers `403 AccessDenied` with a message that names `include`. The OpenAPI document was exported again as `open.api.v0.9.21.json`, and the C# and TypeScript SDKs were regenerated from it. `sform story config export`/`import` and the admin UI's transfer store, mock API and Configurations dialogs were adapted to the new SDK names and routes only: they pass neither include nor existing, so they behave as before (all four kinds, `Overwrite`). The new commands (Phase 3) and the Export & Import page (Phase 4) are not part of this phase. The work is in `21a24c52` (routes and scopes), `0e96f76f` (shared OpenAPI schemas), `f7414f37` (document, SDKs and adaptations), `9ace5d52` (documentation) and `775437d1` (review fixes).

## What Was Done

### 2.1 Routes (`Api.Functions/src/V1/ViewTransferHttp.cs`, `V1/ViewTransferRequests.cs`)

- `ConfigurationTransferHttp.cs` is replaced by `ViewTransferHttp.cs`. Each function is named after its operation id:

  | Method | Route below `v1/{organization}/{project}/{view}` | Function and operation |
  | --- | --- | --- |
  | `POST` | `exports` | `StartViewExport` |
  | `GET` | `exports/{id}` | `GetViewExport` |
  | `GET` | `exports/{id}/archive` | `GetViewExportArchive` |
  | `POST` | `imports` | `StartViewImport` |
  | `GET` | `imports/{id}` | `GetViewImport` |

- `Definitions` has the routes (`Routes.Transfers.V1`: `Exports`, `Export`, `ExportArchive`, `Imports`, `Import`) and the operation ids (`RouteIds.Transfers`). It also has the tag `Tags.Transfers` and the parameters `include` and `existing`. New descriptions cover the `401` (`ResponseUnauthenticated`), the two `403`s (`ResponseTransferForbiddenExport`, `ResponseTransferForbiddenImport`), `TransferInclude` and `TransferExisting`. The routes, route ids and classes of PR #88 are removed, without a redirect; they were never released.
- The other status codes are the ones of PR #88: `202` with `Location`, `404 NotFound` for a job read through another view, `409 TransferLimit` and `TransferNotReady`, `413 ArchiveTooLarge` and `400 InvalidArchive`.
- **Deviation:** the route reads and parses the export body itself (`ViewTransferRequests.TryParseExport`), instead of binding it with `[FromBody]`. With the binder, an unknown enum string could fail outside the handler; here every mistake answers a clear `400`.
  - Member names, formats and kinds match in any case. Other members are ignored.
  - These export a folder of all four kinds: no body, `{}`, null members, and an empty `Include`, which becomes null.
  - These answer `400` without an error code, with a message that names the bad value:
    - an unknown format or kind, or a number in its place;
    - an `Include` that is not a list;
    - a body that is not a JSON object, or invalid JSON.
- **Deviation:** `include` and `existing` are read from `request.Query`, not bound as method parameters. `force`, `dryRun` and `fileName` stay bound as in PR #88.
  - `HttpRequestData.Query` joins repeated keys with commas, so `include=a&include=b` works as `include=a,b`. Spaces are trimmed and empty entries dropped. An empty `include` means all four kinds. A kind named twice counts once, because the service resolves the include.
  - Only the defined names are accepted, in any case (`TryParseName`). `Enum.TryParse` would also take numbers and comma-joined names.
  - An unknown kind, or an `existing` that is neither `Overwrite` nor `Skip`, answers `400 InvalidQuery` (`InvalidInputException`). An empty `existing` means `Overwrite`.
- `IViewTransferService` is unchanged. Since Phase 1, `StartExportAsync` takes `include`, `StartImportAsync` takes `include` and `existing`, and the jobs store the resolved include. **Not done:** bundling the options into a record, which the Phase 1 review suggested as optional.
- **Background functions:** `Transfers/ConfigurationTransferFunctions.cs` is now `ViewTransferFunctions.cs`, with `ProcessViewTransfer` and `PurgeViewTransfers`, and its log messages say "view transfer" and "transfer archives".
  - The storage names stay: the queue `configuration-transfers`, the container, the partition `{project}.transfer` and the options section `ConfigurationTransfers`.
  - The new timer name changes the Functions host's own timer status blob (`azure-webjobs-hosts/timers/…/PurgeViewTransfers`). That blob is runtime bookkeeping, not one of the storage names that decision 13 keeps.
- `exports` and `imports` are literal segments after the view, and `views` is a reserved view name, so they meet no other route. `RouteTableTests` checks that the routes are registered, and its uniqueness test (`HttpRoutes_AreUniquePerMethodAndShape`) covers them.

### 2.2 Access (`Api.Functions/src/Security/ViewTransferScopes.cs`)

- The roles stay as in PR #88: any role to export, to read an export and to download its archive; `Contributor` to import and to read an import.
- `ViewTransferScopes.GetScopes(content, kind)` returns the spec's table: Annotations need the `Annotation` or `Default` scopes; Schemas, Templates and Configurations need the `Configuration` or `Default` ones. An export, and reading one, need a read or write scope. An import, and reading one, need a write scope.
- `Ensure(granted, include, kind, isJob)` resolves the include with `ViewArchiveInclude.Resolve` (null or empty: all four). It groups the kinds without a scope by their rule and throws `AccessDeniedException`, which answers `403 AccessDenied`. The message has one sentence per failing rule. It names the kinds, the scopes that would allow them, and `include`:
  - a request without `include` also gets `Without include, all four kinds are included; name the kinds to import in include.`;
  - a request with `include` gets `… in include: exporting them needs …`;
  - reading a job gets `… in the include of this export: reading it needs …`.
- **Reading a job** checks the scopes of the job's stored `Include`, not of the request. A job without one counts as all four kinds.
  - The order is the role, then the job (`404`), then the scopes (`403`).
  - **Refinement:** the archive download reads the job once more before `OpenExportArchiveAsync`. That costs one point read, but a caller without the scopes learns nothing about the job's state, and the archive is never opened for it.
- **Starting a job** checks the options (`400`), the credentials (`401`, added in the review), the scopes (`403`) and then the role.
- **Deviation:** a missing scope is `403 AccessDenied`, where PR #88 answered `401` (`CheckScope` throws `SecurityTokenException`). Spec 2.1 keeps PR #88's status codes, but 2.2 asks for a `403` that names the missing scope and `include`; 2.2 wins. `401` stays for missing or invalid credentials, and each operation documents both.
- **DEV_AUTH:** the handler has an internal init property, `ViewTransferHttp.ChecksScopes`, which defaults to `!IsDevelopmentAuthentication`. Debug builds therefore skip the scope checks, like every other route. The property is a test seam: `ViewTransferHttpTests` turn it on, so the rules per kind and their wiring are tested in Debug too. The role checks are still skipped in Debug, as everywhere.
- `HttpRequestDataExtensions.GetScopes()` is extracted: the `scp`, `roles` and scope claims, without an `App.` prefix. It does not skip anything in Debug itself. `CheckScope` and `TryCheckScope` now use it.
- This resolves the access note of the Phase 1 review.

### 2.3 OpenAPI and SDKs

**Operations.**
- Each of the five operations documents its `401` (no body) and its `403` (`ErrorResponse`).
- **Deviation:** `include` is published as an array of `ViewArchiveContent` (`style: form`, `explode: false`), and `existing` as the enum `ViewImportExisting` with the default `Overwrite`. The existing code publishes enum query parameters as strings; the typed form gives both SDKs typed parameters.

**Shared schemas (`Api.Functions/src/OpenApi/Filters/ViewTransferSchemaDocumentFilter.cs`).** This filter is an **addition beyond the spec**, registered in `OpenApiConfigurationOptions` after `MachineCredentialKindDocumentFilter`, whose pattern it follows.
- Without it, the inline enums gave NSwag the enums `Include`, `Include2`, `Include3` and `Anonymous` for the same kinds, and `Existing` for the query's mode. Every include item also carried `default: Annotations`, which is misleading, since no include means all four kinds.
- The filter adds two component schemas:
  - `ViewArchiveContent`, without a default;
  - `ViewImportExisting`, with the default `Overwrite`.
- These point at them: the `Include` lists of `ViewExportCreate`, `ViewExport` and `ViewImport`; `ViewImport.Existing`; and the `include` and `existing` query parameters of `StartViewImport`.
- These stay inline:
  - the nullable `ViewImportSummary.Content` (`ViewImportSummaryContent`), as the precedent keeps nullable enums inline;
  - the format (`ViewExportCreateFormat` in the body, `ViewExportFormat` on the job), as in PR #88;
  - `ViewImportItem.Kind` (`ViewImportItemKind`), the outcome and the job states.
- `NullableEnumDefaultDocumentFilter` is unchanged.

**The document.** `OpenApiExportTests` rendered it (`STORYTELLER_OPENAPI_EXPORT`) at `0e96f76f`, as version `0.9.21.3734`, so the file is `open.api.v0.9.21.json`.
- **Deviation:** the version is not the `0.9.20` that a render at `21a24c52` gives. The filter was committed first and the document rendered at that commit, as the repository did before with `f9582f9a` and then `3e011ee7`. So the document carries the version of the API code it comes from. The two renders differ only in `info.version`.
- Against `0.9.15`, only the version, the five transfer paths and the transfer schemas (with the two shared ones) changed. The `servers` entry is unchanged.
- `Sdk.NSwag` keeps `open.api.v0.9.15.json` next to the new file. `sdk.typescript` replaces its `0.9.15`, as its README asks. Git shows that as a rename of the TypeScript `0.9.15` to the identical `Sdk.NSwag/open.api.v0.9.21.json`, which is only cosmetic.

**C# SDK (`Sdk.NSwag`).** `ApiSdk.g.cs` is regenerated with the README command, which now names `0.9.21`.
- New `ITransfersApiClient`/`TransfersApiClient`:
  - `StartViewExportAsync(…, ViewExportCreate body)`;
  - `GetViewExportAsync`;
  - `GetViewExportArchiveAsync` (a `FileResponse`);
  - `StartViewImportAsync(…, IEnumerable<ViewArchiveContent> include, ViewImportExisting? existing, bool? force, bool? dryRun, string fileName, Stream body)`, which sends `include` comma-separated;
  - `GetViewImportAsync`.
- The types are `ViewExportCreate`, `ViewExport`, `ViewImport`, `ViewImportSummary`, `ViewImportItem` and `ViewImportWarning`, with the enums `ViewArchiveContent`, `ViewImportExisting`, `ViewExportCreateFormat`, `ViewExportFormat`, `ViewExportStatus`, `ViewImportStatus`, `ViewImportItemKind`, `ViewImportItemOutcome` and `ViewImportSummaryContent`.
- The `Configuration*` transfer types are gone, and so are the five transfer methods of `ConfigurationsApiClient`.
- `ApiSdk.partial.cs` adds the `TransfersApiClient` partial (the DI constructor and the authorization hook), and `AddStorytellerSdk` registers the client. The partial adds one SA1402 warning, like the six existing clients.

**TypeScript SDK (`sdk.typescript`).** `npm run generate` sets the package version to `0.9.21` and adds the `transfers` tag.
- It has the operations `startViewExport`, `getViewExport`, `getViewExportArchive`, `startViewImport` and `getViewImport`, in `client/transfers`, `vue-query/transfers` (composables and `transfers.msw.ts`) and `zod/transfers`.
- The models are `view*.ts`, `startViewImportParams.ts`, `viewArchiveContent.ts` and `viewImportExisting.ts`. The `Configuration*` transfer models are removed, and every generated file gets the new version header.
- The zip upload (a `Blob` sent as `application/zip`) and the `Blob` download work as in PR #88. `openapi-transformer.mjs` is unchanged, and `lib/fetcher.ts` only changed a comment.
- `msw.ts` is hand-written, and generating does not touch it. The review added `getTransfersMock()` to `getStorytellerMock()`, and an `export *` of `transfers.msw` (see [Review](#review)).
- The README gained the section "Export and import of a view", and step 3 of "A new API version": a new tag has to be added to `msw.ts` by hand.

**Tests of 2.3.**
- **`ViewTransferHttpTests`** replaces `ConfigurationTransferHttpTests`. It has 38 test methods and 98 cases:
  - the options and their defaults: the export body (`""`, `{}`, null members, empty `Include`), the kinds in any case, the import query in any case, comma-separated and repeated;
  - `400` for an unknown format or kind, a numeric value, a non-object body and invalid JSON; `400 InvalidQuery` for an unknown kind or `existing`;
  - the scopes per kind (`GetScopes` for every kind and direction), and the scopes of both starts against `include`;
  - the scopes of the reads against the stored `Include`, and a job without `Include`;
  - the three message forms, and a machine with only `Configuration.ReadWrite`;
  - `ChecksScopes` against `DEV_AUTH`, and requests without credentials (review);
  - the removed routes: no `…/configurations/exports|imports` route, route id or `ConfigurationTransfer*` type is left;
  - the operation ids, functions and tags;
  - the PR #88 cases, carried over: `Location`, `404` through another view, the archive, non-zip bodies, `413`, and the queue and timer functions, with the queue name kept.
- **`OpenApiDocumentTests.ViewTransferSchemas_ArePublished`** (renamed from `ConfigurationTransferSchemas_ArePublished`) covers `Kind`, `Summaries`, `Include` on the body and both jobs, `Existing`, `Warnings`, `Content`, `ExcludedFiles`, `ViewImportWarning` and the five counts. It also checks that no `Configuration*Export|Import` schema is left.
- **`OpenApiExportTests.Document_HasTheViewTransferOperations_UnderTheTransfersTag`** renders the document and checks:
  - the five operations under `Transfers`, each with a `403`, and no `/configurations/exports` path;
  - the query parameters, with `include` as `form` and `explode: false`;
  - the `$ref`s to both shared schemas, and their enums and defaults.
- **`ViewTransferSchemaDocumentFilterTests`** (4): the lists, the job's `Existing`, the import's query, and a document without transfers.
- **`RouteTableTests.ViewTransferRoutes_AreRegistered`** (5 cases).

### Adaptations of the CLI and the admin UI

These changes were not in the spec's Phase 2. They keep the tree building and green until Phases 3 and 4.

- **CLI:** `ConfigExportCommand`, `ConfigImportCommand`, `TransferConsoleExtensions` and `ConfigurationTransferCommandTests` use `ITransfersApiClient` and the `View*` types. The export sends `new ViewExportCreate { Format = format }`, and the import passes `include` and `existing` as null. The behaviour is unchanged.
- **Admin UI:**
  - `stores/transfers.ts` calls the `startView*`/`getView*` operations with the `ViewExport` and `ViewImport` types.
  - `utils/transfers.ts`, `ConfigurationExportDialog.vue` (`ViewExportCreateFormat`) and `ConfigurationImportResult.vue` (`ViewImport`, `ViewImportItem`) use the new names. `ConfigurationImportDialog` and `TransferToaster` are unchanged.
- **Deviation:** the mock API (`mock/transfer-handlers.ts`) serves `…/{view}/exports|imports`, and does more than accept the options. The purpose is that the mock is not silently wrong when a caller passes the options. The store still passes neither.
  - It parses `Include`, `include` and `existing` like the API. Values match in any case, and `include` may be comma-separated or repeated. An unknown value answers `400`, with `InvalidQuery` for the query.
  - It stores the options resolved on the job.
  - It applies them to configurations, the only kind it knows. Without `Configurations`, an export writes none and reports `ConfigurationCount` null, and an import counts their files as `ExcludedFiles`. `existing=Skip` keeps a different stored configuration as `Skipped` with `DocumentExists` and the API's message.
  - Items carry `Kind: 'Configuration'`, and the import checks its options before the zip, as the API does.
- **Zip name:** the mock names its zip like the API, `export-{org}-{project}-{view}[-git]-{stamp}Z.zip` (renamed in Phase 1). The unit and e2e regexes and the store's fallback download name (`export-{view}.zip`) changed with it.
- Prettier, with ui.admin's config, ran on the touched mock, component and unit-test files. It also re-wrapped two long lines of `transfer-handlers.ts` that were already unformatted.
- **Not committed:**
  - `app/auto-imports.d.ts`, `app/components.d.ts` and `app/typed-router.d.ts` were restored, because only their line endings had changed.
  - `package-lock.json` was restored too. It still records the `file:` SDK as `0.8.91`, which was already stale before this phase, and `npm install` adds `libc` fields to it.

### Documentation

- `export-import.md`:
  - the code list names `Api.Functions` and both SDKs;
  - **State of the delivery** says phases 1 and 2 are done;
  - the API section is split into Routes, Request options, Status codes (with the order of the checks), Access (the scope table, the three message forms, the machine example, `DEV_AUTH` and `GetScopes()`), OpenAPI and SDKs, and Background functions.
  - The `## API` heading stays, because code comments point to "export-import.md, API". The options subsection is called "Request options", so that its anchor does not clash with `#options` of the Import section.
  - The CLI and Administration UI sections describe the transition: the new SDK operations without include or existing, and the mock API.
- **Addition:** `access.md` has one more row in its role table, for exporting, importing and reading those jobs, linked to `export-import.md#access`.
- The READMEs of `Sdk.NSwag` and `sdk.typescript` (2.3).
- The public pages (`docs/42for.net/platform/configuration.md`, `admin-ui.md`) are unchanged. They name no routes or SDK operations, and the spec gives them to Phase 4.

## Tests

| Project | Result |
| --- | --- |
| `Api.Functions/test` | 434 pass in Debug (348 after Phase 1). Four runs after the review fixes: two all green, and two with one failure each, in `MachineTokenFallbackTests.MachineKeyFailure_IsServiceUnavailable` and in `BearerAuthenticationMiddlewareTests.Invoke_DebugDecode_AuthKitToken_RunsTheAuthKitNormalizer`. Both passed when re-run. Both classes toggle the static `BearerValidationMode.DecodeWithoutValidation`, and neither is touched by this phase. `ViewTransferHttpTests`: 98 of 98 in Debug and in Release. |
| `Api.Functions/test`, Release | One full Release run, before the review fixes, had 418 pass and 6 fail: five in `MachineHttpTests` and `AnnotationsHttpTests.Purge_ByUser_CallsTheService`. Their test doubles expect `DEV_AUTH` to skip the Administrator role check. This phase changes neither those tests nor the role check, but the failures were not confirmed at the Phase 1 commit. |
| `Backend.Core/test` | 425 pass, unchanged. |
| `Backend.CosmosDb/test` | 398 pass against the local Cosmos DB emulator, run after `21a24c52`. No backend code changed after that. |
| `Cli/test` | 190 pass. |
| `sdk.typescript` | Vitest: 109 pass and 1 skipped (the smoke test that needs a running API); 103 before the review fix. `msw.test.ts` gained 6 tests (19 now). `fetcher.test.ts` uses the new operations: the import test checks `include=Annotations%2CTemplates&existing=Skip`, and a new test sends the export body. `typecheck`, `lint` (oxlint) and `build` (tsup) pass. |
| `ui.admin` | Run against the rebuilt and reinstalled SDK. `typecheck` (vue-tsc) and `lint` (oxlint, ESLint) pass. Vitest: 436 pass in 48 files; `transfers.test.ts` has 14, 3 of them new. Playwright `transfers.spec.ts` on desktop and mobile: 8 pass and 2 skipped (the spec skips them on mobile). |

Other builds succeed: `Api.Functions` and its tests, `Aspire.Host` (the other project that references `Api.Functions`), and `Sdk.NSwag` with the CLI, its only consumer. The touched files add no StyleCop warnings, except the SA1402 of the `TransfersApiClient` partial.

## Review

Fixed (`775437d1`):

1. **Requests without credentials to `StartViewExport` and `StartViewImport` got `403 AccessDenied` instead of `401`.**
   - The cause: no authentication middleware stops a request without credentials. The starts checked the scopes, against an empty set, before the role, so the answer was the `403` that asks to name the kinds in `include`.
   - The fix: when `ChecksScopes` is on and `request.GetClaims()` is empty, the private `CheckScopes` throws `SecurityTokenException("Missing credentials.")`. That answers `401`, as in PR #88. Every authenticated caller has claims (machines get synthesized `azp`, `sub` and `roles`, users their token's claims), so a missing scope is still `403` for them. The fix also covers the three reads, which already answered `401` in Release through the role check. Debug is unchanged, since `ChecksScopes` is off there.
   - Tests: `StartExport_WithoutCredentials_IsUnauthorized` (no body, and with `Include`), `StartImport_WithoutCredentials_IsUnauthorized` (without and with `include`) and `GetJobs_WithoutCredentials_AreUnauthorized` (the three reads). They expect `SecurityTokenException`, and that the service is never called, so the archive is never opened. The test helper `Request` gained `isAnonymous`. All pass in Debug and in Release.
   - Documentation: `export-import.md` has the credentials (`401`) before the scopes in the order of the checks, and its Access section says so.
   - The `401` was already documented, so the API surface did not change and nothing was regenerated.
2. **The TypeScript SDK's `./msw` entry and `getStorytellerMock()` left out the transfer handlers.**
   - The cause: `msw.ts` is hand-written, and generating the new tag did not add it.
   - The fix: `msw.ts` imports `getTransfersMock()`, spreads it into `getStorytellerMock()`, and re-exports `generated/vue-query/transfers/transfers.msw`.
   - Tests: `test/msw.test.ts` gained six tests, all of which failed before the fix:
     - `it.each` cases for `startViewExport`, `getViewExport`, `startViewImport` and `getViewImport` against their generated Zod response schemas;
     - a `Blob` check for `getViewExportArchive`;
     - a completeness test. It reads the tag folders under `generated/vue-query`, and checks that the entry exports each `get<Tag>Mock` and that `getStorytellerMock()` serves every handler of the tag, compared by `handler.info.header`.
   - Documentation: the SDK README (step 3 of "A new API version", and the MSW handlers of the transfer section) and `export-import.md`.
   - The SDK was rebuilt and reinstalled into ui.admin.

Not fixed:

- **The export route rejects a JSON `null` body, which the regenerated C# SDK sends for an omitted optional body**: dropped in verification as not a real defect. For the record:
  - `StartViewExportAsync` serializes its `body` argument, so a null argument is sent as `null`, which answers `400` (`The body must be a JSON object…`);
  - the CLI, the only caller of the C# SDK, always passes a `ViewExportCreate`;
  - the TypeScript SDK sends no body when it is omitted.

## Notes for Phases 3 and 4

- **CLI (Phase 3):**
  - `sform story config export` and `import` still exist, and pass no include or existing.
  - In the C# SDK, `ViewImportSummary.Content` is the inline enum `ViewImportSummaryContent`, while the include uses `ViewArchiveContent`, so the per-kind output has to map them by name. `ViewImportItem.Kind` is `ViewImportItemKind`.
  - The new commands should always pass a `ViewExportCreate` to `StartViewExportAsync` (see [Review](#review)).
  - `ConfigurationArchives.PackFolder` still skips only `.git`.
- **Admin UI (Phase 4):**
  - The mock API still uses PR #88's file names: `<key>.json` is a configuration there, while the server reads it as an annotation and `<key>.conf.json` as a configuration.
  - The mock sends no per-kind `Summaries` and no `Warnings`, and `mock/archive-names.ts` does not exist yet.
  - The dialogs, and the `TransferToaster.refresh` invalidation noted in the Phase 1 review, remain.
  - `package-lock.json` records a stale SDK version. After an SDK rebuild, reinstall it with `rm -rf node_modules/@42for.net && npm install`.
- **Documentation:**
  - The CLI and Administration UI sections of `export-import.md`, and its "State of the delivery" paragraph, have to be rewritten in Phases 3 and 4.
  - `docs/42for.net/platform/configuration.md` and `admin-ui.md` still say an archive holds no templates or schemas and that `<key>.json` files are configurations (Phase 4).
- **OpenAPI:** the other inline enums keep the generator's first-value defaults, an existing pattern: `ViewImportItem.Kind` `Annotation`, `Outcome` `Created`, the job `Status` `Queued`, and the `Protections` items of key-values `Public`. Only the transfer kinds and the existing mode are shared schemas.
- **Tests:**
  - The six Release-only failures in `MachineHttpTests` and `AnnotationsHttpTests` should be confirmed on `main`.
  - The two auth test classes that share `BearerValidationMode.DecodeWithoutValidation` make the full Debug run flaky.
- **Still open from Phase 1:**
  - concurrency exhaustion fails the whole job instead of one file;
  - descendant-type schemas that fail `CanDescend` can be written through the API and are exported, and then fail on import with `InvalidKey`;
  - `ViewArchiveReader.Validate` counts every kind at upload. It could take the request's include, but this phase left it unchanged.
