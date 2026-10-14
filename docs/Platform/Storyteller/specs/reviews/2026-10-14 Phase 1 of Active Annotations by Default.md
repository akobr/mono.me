# Phase 1 of Active Annotations by Default

## Overview

Phase 1 covers sections 1–3 of [Active Annotations by Default](../2026-10-14%20Active%20Annotations%20by%20Default.md), Linear 424-16: the status rule, the filter of the list reads, the two query flags of the API, the OpenAPI export and both regenerated SDKs, and the Storyteller documentation. The CLI (Phase 2) and the admin UI (Phase 3) were changed only as far as the regenerated SDKs required: they pass the new arguments empty, so until their phases they get the API default.

## What Was Done

### 1. The status of an annotation

- `Abstractions.Annotations/src/AnnotationStatus.cs`: `Active`, `Disabled`, `Scheduled`, `Expired`.
- `Abstractions.Annotations/src/AnnotationStatusExtensions.cs`: `GetStatus(bool? isDisabled, DateTimeOffset? validFrom, DateTimeOffset? expiresAt, DateTimeOffset now)` and `GetStatus(this IAnnotation, now)`. The rule is the admin UI's: disabled wins, then `ValidFrom > now`, then `ExpiresAt <= now`. The lifted comparisons of `DateTimeOffset?` compare instants, so the offset doesn't matter.
- The existing `AnnotationState` (`Active`, `Deleted`, used by the history) is unchanged; the new enum is named after the admin UI's `AnnotationStatus`.

### 2. Filtering the list reads

- `AnnotationsRequest.IncludeInactive` (`Backend.Core`).
- `RequestProcessModel.Now` (`Backend.CosmosDb`), set once per request from the service's `TimeProvider`.
- `CosmosAnnotationService.GetAnnotationsAsync<TAnnotation, TEntity>`:
  - without `IncludeInactive`, the Cosmos query gets `!r.IsDisabled.IsDefined() || r.IsDisabled == null || r.IsDisabled == false`;
  - each page is filtered in memory with `GetStatus(..., model.Now) == Active`, after the descendants filter.
- `IAnnotationService` comments say which reads look at the status. Nothing else in the service changed: the single reads, the deleted reads, the writes, the cascade, the binding and the configurations are as before.

### 3. API

- `Definitions.Parameters.IncludeInactive` / `IncludeDeleted` and their descriptions; `ErrorCodes.AnnotationInactive`.
- `AnnotationsHttp`: `GetAnnotations`, `GetResponsibilities`, `GetSubjects`, `GetUsages`, `GetContexts`, `GetExecutions`, `GetUnits`, `GetUnitsOfExecution` and `GetDescendants` take `[FromQuery] bool includeInactive = false, bool includeDeleted = false` after `continuationToken`, publish them as `OpenApiParameter`s, and map them to `IncludeInactive` and `Deleted` (`DeletedFilter(includeDeleted)`: `Include` or `Exclude`).
- `GetAnnotation`:
  1. reads the key with `GetAnnotationAsync`, and with `includeDeleted` falls back to `GetDeletedAnnotationAsync`;
  2. nothing found: plain `404`, as before;
  3. not active and no `includeInactive`: `404` with `ErrorResponse { ErrorCode = AnnotationInactive, Message = "The annotation 'sbt.x' is disabled; read it with includeInactive=true." }`.
  The OpenAPI `404` of the operation now has an `ErrorResponse` body.
- `AnnotationsHttp` gets the `TimeProvider` in its constructor; `AddCosmosDbAnnotations` already registers `TimeProvider.System`.
- `Api.Web` builds unchanged.

### OpenAPI and SDKs

- **Export.** The chocolatey `func` (4.0, `in-proc8`) starts the isolated worker with the x86 `dotnet`, which has no .NET 10 runtime. The document was exported with `%LOCALAPPDATA%\AzureFunctionsTools\Releases\4.109.0\cli_x64\func.exe start --port 7072` from the worktree's `Api.Functions/src`, saved from `/api/openapi/v3.json`, and the host was stopped. `servers` was set back to `http://localhost:7071/api`.
- **Version.** The build is `0.9.2.62924`, so the files are `Sdk.NSwag/open.api.v0.9.2.json` and `sdk.typescript/open.api.v0.9.2.json`. Against `v0.9.1` the only changes are the two parameters on ten operations and the `404` body of `GetAnnotation`. The branch of 424-14 also exports a `v0.9.2`; whichever merges second has to export again.
- **`Sdk.NSwag`.** `src/ApiSdk.g.cs` regenerated with the README command (README names `v0.9.2`). NSwag makes the flags `bool?` parameters **without defaults**, so every C# caller passes them.
- **TypeScript SDK.** `npm run generate` set the version to `0.9.2`. Orval puts them in the params objects (`GetAnnotationsParams`, `GetAnnotationParams`, …). `getAnnotation` and `useGetAnnotation` gained a `params` argument before `options`. Typecheck, lint, 59 tests (1 skipped) and the build pass.
- **Fallout:**
  - CLI: the calls pass `null` for both flags; the mocks of `StorytellerDeleteCommandTests` and `StorytellerReadCommandsTests` match the new arity.
  - admin UI: `useAnnotation` passes `undefined` params.

### Documentation

- `docs/Platform/Storyteller/annotation-lifecycle.md`: new section **Active annotations** (status table, flags, the list and single-key behaviour, what is not filtered). I2 now says "not deleted", the API table lists the reads with the flags, and `AnnotationInactive` is in the error codes.
- The public `annotations.md` is updated with the CLI in Phase 2, because it names the CLI option.

### Tests

| Project | Result |
| --- | --- |
| `Backend.Core/test` | 42 pass (34 before). `AnnotationStatusTests`: no flag or window, disabled wins, scheduled, `ValidFrom == now` active, `ExpiresAt == now` expired, other offsets, an inverted window, the `IAnnotation` overload. |
| `Backend.CosmosDb/test` | 269 pass (263 before), against the local emulator. `CosmosAnnotationStatusTests`: the default list hides disabled, scheduled and expired annotations and keeps one with `IsDisabled = false` and one with a window around now written with a `+02:00` offset; `IsDisabled: null` (patched in Cosmos) is active; `Deleted = Include` adds deleted annotations that are active, with `Deletion` and `PurgeAt`, and needs `IncludeInactive` for a deleted disabled one; the descendants read applies the filter; `GetAnnotationAsync` still returns a disabled annotation. |
| `Api.Functions/test` | 202 pass (178 before). The defaults and the three flag combinations reach `AnnotationsRequest` on all nine list reads; `GetAnnotation` returns an active annotation, answers `404 AnnotationInactive` for each inactive status without the flag and `200` with it, returns a deleted one only with `includeDeleted`, needs both flags for a deleted disabled one, and answers a plain `404` for an unknown key. `OpenApiDocumentTests.AnnotationReads_PublishTheIncludeFlags` checks the parameters of the ten operations. |
| `Cli/test` | 110 pass. |
| `ui.admin` | typecheck and 308 unit tests pass. |

**Environment.** `Backend.CosmosDb/test` builds a Testcontainers `CosmosDbBuilder` even though it runs against the local emulator (`UseContainer = false`), and the build needs a Docker engine; Docker Desktop was started for the run.
