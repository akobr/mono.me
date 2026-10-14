# Active Annotations by Default

Linear: [424-16](https://linear.app/424fun/issue/424-16/properly-handle-disabled-expired-and-soft-deleted-annotations).

## Problem

An annotation can be out of effect in four ways: it is **disabled** (`IsDisabled`), **scheduled** (`ValidFrom` is in the future), **expired** (`ExpiresAt` has passed), or **soft-deleted**. The API hides only the soft-deleted ones. Every list endpoint and `GET …/annotations/{key}` return disabled, scheduled and expired annotations as if they were in effect, so every consumer has to repeat the rules, and most don't.

The issue asks for:

- the API returns only **active** annotations by default;
- flags that also return the disabled, expired, scheduled and deleted ones;
- the CLI and the admin UI query the annotations correctly.

## Current State

- **Model.** `IAnnotation` (`Abstractions.Annotations/src/Model/IAnnotation.cs`) has `bool? IsDisabled`, `DateTimeOffset? ValidFrom`, `DateTimeOffset? ExpiresAt` and `string? TimeZone`. The server stores them (`AnnotationEntity`) and never reads them, apart from copying the window and the flag to automatic ancestors (`AnnotationFactory.CreateDefault`) and recording changes in the audit (`AnnotationAuditDiff`).
- **Status rule.** Only the admin UI has one (`ui.admin/src/app/utils/annotation-status.ts`, spec section 7 of the Administration UI): `disabled` wins, then `scheduled` when `ValidFrom > now`, then `expired` when `ExpiresAt <= now`, otherwise `active`. `now` is UTC; the dates are instants, `TimeZone` only says how to show and enter them.
- **List reads.** `CosmosAnnotationService.GetAnnotationsAsync(AnnotationsRequest)` (`Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs:65`, per-type query at `:579`) filters by type, project, view, `Deleted` (`AnnotationDeletedFilter`, default `Exclude`), the deletion fields, the descendants scope and the endpoint conditions. Descendants are filtered once more in memory, so a page can already hold fewer items than the page size.
- **Endpoints** (`Api.Functions/src/V1/AnnotationsHttp.cs`): `GetAnnotations`, `GetResponsibilities`, `GetSubjects`, `GetUsages`, `GetContexts`, `GetExecutions`, `GetUnits`, `GetUnitsOfExecution`, `GetDescendants` build an `AnnotationsRequest` with the default `Deleted = Exclude`. `GetAnnotation` answers `404` for a deleted or missing key. None of them has a flag for the validity. The deleted annotations have their own routes (`AnnotationsHttp.Deleted.cs`: `…/deleted`, `…/deleted/{key}`, lookup, restore, purge).
- **Other reads** of annotations — `ExistAnnotationAsync`, the writes, the delete cascade and restore (`AnnotationItemStore`), the `@annotation` binding and every configuration read — look only at the deletion.
- **CLI** (`src/Platform/Cli/src/Commands/Storyteller/`):
  - `StorytellerListCommand` prints keys from the list endpoints, the descendants and two trees. `RenderAnnotationsAsync` passes `null` instead of `--continuation-token`.
  - `StorytellerGetCommand` prints one annotation; on `404` it explains a deleted key.
  - `StorytellerDeleteCommand --permanent` finds the key with `TryGetAnnotationAsync` and counts the descendants with `GetDescendants` for its confirmation.
- **Admin UI** (`src/Platform/ui.admin/src/`):
  - `stores/catalog.ts` loads the whole view through the seven per-type list endpoints (fallback `GetAnnotations`). The grid, tree, graph, filters (`is:active`, `is:disabled`, …) and status badges work on that catalog, so the admin UI expects every status.
  - `queries/scope.ts` `useAnnotation` reads one annotation for the detail.
  - The MSW mock (`app/mock/annotation-handlers.ts`) seeds disabled, scheduled and expired annotations (`app/mock/annotations.ts`) and returns all of them.
- **SDKs.** `Sdk.NSwag` (used by the CLI) and `sdk.typescript` (used by the admin UI) are generated from `open.api.v0.9.1.json`. `Api.Web`, `src/Platform/Sdk` and `Sdk.Kiota` are not maintained beyond compiling.

## Proposed Changes

### 1. The status of an annotation (`Abstractions.Annotations`)

The admin UI rule becomes the platform rule, in C#:

```csharp
// Whether an annotation is in effect now. Deletion is a separate state (AnnotationState).
public enum AnnotationStatus
{
    Active = 0,
    Disabled,   // IsDisabled
    Scheduled,  // ValidFrom after now
    Expired,    // ExpiresAt at or before now
}

public static class AnnotationStatusExtensions
{
    // Disabled wins, then a ValidFrom after now (scheduled), then an ExpiresAt at or before now (expired).
    public static AnnotationStatus GetStatus(bool? isDisabled, DateTimeOffset? validFrom, DateTimeOffset? expiresAt, DateTimeOffset now);

    public static AnnotationStatus GetStatus(this IAnnotation annotation, DateTimeOffset now);
}
```

- The dates are compared as instants, so the stored offset and `TimeZone` don't matter.
- The CLI uses the primitive overload for the generated SDK types.

### 2. Filtering the list reads (`Backend.Core`, `Backend.CosmosDb`)

`AnnotationsRequest` gets:

```csharp
// Also the disabled, scheduled and expired annotations. Without it, only the active ones (AnnotationStatus).
public bool IncludeInactive { get; set; }
```

A read returns an annotation when **both** hold:

1. it is not deleted, or `Deleted` is `Include` (or `Only`);
2. its status is `Active`, or `IncludeInactive` is set.

Each flag removes one condition, so `includeInactive` and `includeDeleted` together return everything.

`CosmosAnnotationService.GetAnnotationsAsync`:

- takes `now` once per request from its `TimeProvider`;
- without `IncludeInactive`, adds the disabled filter to the Cosmos query: `!r.IsDisabled.IsDefined() || r.IsDisabled == null || r.IsDisabled == false`. An undefined member fails every comparison in Cosmos, so `IsDisabled != true` alone would drop every annotation without the member;
- filters each page in memory with `GetStatus(now) == Active`, like the descendants filter. The window is not filtered in Cosmos: the dates are stored with the offset the client sent (`2026-10-14T09:00:00+02:00`), so a string comparison in Cosmos is wrong by up to 14 hours.
- `Count` stays the number of returned items. A page can hold fewer items than the page size, or none, and still have a continuation token; clients already follow the token until it is absent.

Unchanged: `GetDeletedAnnotationsAsync` (every deleted annotation, whatever its status), `GetAnnotationAsync`, `GetDeletedAnnotationAsync`, `ExistAnnotationAsync`, writes, delete, restore and purge (they act on inactive annotations as on active ones), the `@annotation` binding, and the configuration reads and listings. Hiding the configuration of an inactive annotation is a separate decision.

### 3. API (`Api.Functions`)

**Flags.** Two optional query parameters, `false` by default:

| Parameter | Adds |
| --- | --- |
| `includeInactive` | disabled, scheduled and expired annotations |
| `includeDeleted` | soft-deleted annotations, each with its `Deletion` (`PurgeAt` included) |

They are added to `GetAnnotations`, `GetResponsibilities`, `GetSubjects`, `GetUsages`, `GetContexts`, `GetExecutions`, `GetUnits`, `GetUnitsOfExecution`, `GetDescendants` and `GetAnnotation`. The list endpoints map them to `IncludeInactive` and `Deleted = includeDeleted ? Include : Exclude`.

**`GET …/annotations/{key}`:**

1. The active item is read with `GetAnnotationAsync`. When there is none and `includeDeleted` is set, the deleted one with `GetDeletedAnnotationAsync`.
2. Nothing found: `404`, as today.
3. Without `includeInactive`, a status other than `Active` answers `404` with an `ErrorResponse`: `ErrorCode` `AnnotationInactive` and the message "The annotation '{key}' is {disabled|scheduled|expired}; read it with includeInactive=true." Clients can tell an inactive annotation from a missing one, like `AnnotationDeleted` on the configuration reads.

`AnnotationsHttp` gets the `TimeProvider` (registered by `AddCosmosDbAnnotations`) for step 3.

**Definitions.** `Parameters.IncludeInactive`, `Parameters.IncludeDeleted`, and their descriptions. `ErrorCodes.AnnotationInactive` in `Backend.Core/src/ErrorCodes.cs`. The `404` of `GetAnnotation` documents the `ErrorResponse` body.

**OpenAPI and SDKs.** The document is exported from the Functions host as `open.api.v0.9.x.json` (the build version), into `Sdk.NSwag/` and `sdk.typescript/`. `Sdk.NSwag/src/ApiSdk.g.cs` is regenerated with the README command, the TypeScript SDK with `npm run generate`. The flags reach the generated clients as optional parameters (`bool? includeInactive`, `params.includeInactive`).

**Not changed:** `Api.Web`, `src/Platform/Sdk` and `Sdk.Kiota` (legacy; they only have to compile).

### 4. CLI (`src/Platform/Cli`)

**`sform story` (list):**

| Option | Effect |
| --- | --- |
| `-i\|--inactive` | Also disabled, scheduled and expired annotations (`includeInactive`). |
| `--deleted` | Also deleted annotations (`includeDeleted`). `-d` is taken by `--descendants`. |
| `-a\|--all` | Both. |

- Every query of the command passes the flags: the per-type lists, `GetAnnotations`, the descendants and both trees.
- Each printed annotation that is not active gets a lowlight marker: `[disabled]`, `[scheduled]`, `[expired]`, `[deleted]` (deleted wins). The trees mark their subject or responsibility nodes and the execution leaves.
- `RenderAnnotationsAsync` passes `--continuation-token` (today it passes `null`).

**`sform story get <key>`** asks with `includeInactive=true`, so an inactive annotation is printed with its `IsDisabled`, `ValidFrom` and `ExpiresAt`. The deleted-key explanation stays.

**`sform story delete <key> --permanent`** finds the key with `includeInactive=true` and counts the descendants with `includeInactive=true`, because the delete cascades to inactive descendants too. `TryGetAnnotationAsync` always asks with `includeInactive=true`.

### 5. Admin UI (`src/Platform/ui.admin`)

- **Catalog.** `stores/catalog.ts` asks the seven per-type streams and the `GetAnnotations` fallback with `includeInactive: true`. The admin UI keeps showing every status and filtering with `is:`; the deleted annotations stay on their own page.
- **Detail.** `useAnnotation` in `queries/scope.ts` asks with `includeInactive: true`.
- **Mock API.** `app/mock/annotation-handlers.ts` behaves like the API: the list endpoints return only active annotations unless `includeInactive=true`, and add the deleted ones (from the trash) with `includeDeleted=true`. `GET …/annotations/:key` answers `404 AnnotationInactive` for an inactive annotation without `includeInactive=true`, and returns a deleted one with `includeDeleted=true`. It uses `annotationStatus` from `utils/annotation-status.ts`.
- The query keys of Vue Query get the params object at the end; `invalidateByPath` and `removeByPath` match by prefix, so the invalidation is unchanged.

### 6. Documentation

- `docs/Platform/Storyteller/annotation-lifecycle.md`: a section **Active annotations** (the status rule, the two flags, what stays unfiltered), the flags in the API table, the CLI options, and `AnnotationInactive` in the error codes. Invariant I2 says that reads see only active annotations by default.
- `docs/42for.net/platform/annotations.md`: the API returns only active records unless asked; the CLI flags.
- `docs/42for.net/platform/admin-ui.md`: the admin UI shows every status.

### 7. Tests

- `Backend.Core/test` (or the `Abstractions` test location used for annotation models): the status rule — disabled wins, `ValidFrom == now` is active, `ExpiresAt == now` is expired, other offsets, an inverted window.
- `Backend.CosmosDb/test`: the lists hide disabled, scheduled and expired annotations by default, return them with `IncludeInactive`, return deleted ones with `Deleted = Include` and both with both; an annotation without `IsDisabled` and one with `IsDisabled = false` are listed; the descendants read applies the same filter.
- `Api.Functions/test`: the flags reach `AnnotationsRequest` on every list endpoint and the descendants; `GetAnnotation` answers `404 AnnotationInactive` without the flag, `200` with it, and returns a deleted annotation with `includeDeleted`; `ErrorResponseMappingTests` is unaffected; `OpenApiDocumentTests` checks the parameters.
- `Cli/test`: the list flags and markers, `get` and `delete --permanent` passing `includeInactive`, the continuation token of the plain list.
- `ui.admin`: the catalog sends `includeInactive=true`; the mock filters; existing unit and e2e tests keep passing.

### Phases

| Phase | Content |
| --- | --- |
| 1 | Sections 1–3: status rule, service filter, API flags, OpenAPI export, both SDKs, and the Storyteller docs. |
| 2 | Section 4: CLI. |
| 3 | Section 5: admin UI and its mock. |
