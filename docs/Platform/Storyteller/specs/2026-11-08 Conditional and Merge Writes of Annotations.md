# Conditional and Merge Writes of Annotations

Linear 424-210. Follows [sform story set Merges by Default](../../Cli/specs/2026-11-07%20sform%20story%20set%20Merges%20by%20Default.md) (PR #169), whose merge is a client-side read, merge and write.

## Problem

`POST` and `PUT …/annotations/{key}` (`AnnotationsHttp.SetAnnotation`) replace the editable content of an existing key. Whatever the body does not hold is cleared: title, description, documentation link, validity window, time zone, disabled flag, values and labels; a unit falls back to `event` and `unknown`. Two things are missing:

- **A precondition.** A read answers no `ETag`, and a write honours no `If-Match`. The only `409` is the internal "changed concurrently three times in a row".
- **A partial write.** To change one member, a client reads the whole record, changes it and writes it back.

So every client that edits one member (the admin UI, `sform`, the MCP server, the SDKs) runs a read-modify-write without a precondition, and two editors lose each other's changes silently. PR #169 made `sform story set` merge by default, but its merge is exactly that read-modify-write: a write between its read and its write is lost for the members the input does not hold. The MCP server's `set_annotation` does the same.

The owner wants annotations to get what configurations have, so the two writes behave alike:

1. a precondition on annotation writes: an `ETag` on reads and `If-Match` on writes, answering `412` on a mismatch;
2. a merge `PATCH`, so a client changes one member in one request, merged on the server, without a read first;
3. `sform story set` on the new API, in this issue: its default merge one `PATCH` with no read first, `--replace` a write with the precondition, and **Restore and update** a restore, then the same `PATCH`. For users, the merge rules and `--replace` stay as PR #169 made them.

## Current State

### Server

- **`IAnnotationService`** (`Backend.Core/src/Annotating/IAnnotationService.cs`): `SetAnnotationAsync(organization, annotation, actor)` (line 57) creates, updates or re-creates. `GetAnnotationWithMarkerAsync` (17-20) returns `MarkedRead<Annotation>` with the storage marker; its default has no marker.
- **`CosmosAnnotationService.SetAnnotationAsync`** (`Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs:150-169`): `AnnotationRules.Validate`, then `RetryAsync` (280-296, three attempts on `AnnotationConcurrencyException`, then `ConflictException`). Each attempt loads the annotation item; an active one goes to `UpdateCoreAsync` (450-494), a missing or deleted one to `CreateCoreAsync` (348-447).
  - `UpdateCoreAsync` normalizes the input (`AnnotationFactory.Normalize`), returns the stored model when `AnnotationAuditDiff.Compute` finds no change, and otherwise replaces the annotation item with `IfMatchEtag = node.ETag` in one transactional batch with its audit item.
  - `CreateCoreAsync` creates or replaces the target and its non-active ancestors, and replaces the relation arrays of active parents in wave 10, each guarded by its ETag.
  - `GetAnnotationWithMarkerAsync` (76-81) returns the Cosmos `_etag` of the annotation item in any state, `BindingDependency.Absent` without an item.
- **`BatchPlan`** (`BatchPlan.cs`) runs the operations as opaque `Action<TransactionalBatch>`s and drops the batch responses, so the ETag a write committed is not known. A batch status of `412`, `409` or `404` becomes `AnnotationConcurrencyException`.
- **`AnnotationsHttp`** (`Api.Functions/src/V1/AnnotationsHttp.cs`):
  - `GetAnnotation` (104-163) calls `GetAnnotationAsync` and sets no header.
  - `SetAnnotation` (295-390), `POST` and `PUT`, reads the body into the runtime type of its `AnnotationType` and calls `SetAnnotationAsync`. Only `POST` is in the OpenAPI document (the first method of a multi-method trigger), with `200, 400, 401, 403, 409, 500`.
  - Nothing in the API reads `If-Match`. `ConditionalReads/ConditionalRequestExtensions.cs` parses `If-None-Match` for the conditional configuration reads (weak tags).
- **Configurations** (`ConfigurationHttp.PatchConfiguration`, 350-441) take a JSON Patch (RFC 6902) and answer `412 PatchTestFailed` for a failed `test` operation; there is no `If-Match` either. `JsonExtensions.ApplyPatch` (Backend.CosmosDb) runs JsonPatch.Net on a `JsonObject`.
- **Errors:** `PreconditionFailedException(message, errorCode)` maps to `412` (`ErrorResponseMapping.cs:24`), `JsonPatchException { Kind: TestFailed }` to `412`, any other `JsonPatchException` to `400`. `ErrorCodes` has `AnnotationNotFound`, `AnnotationDeleted`, `InvalidAnnotation`, `PatchInvalid` and `PatchTestFailed`, and nothing for an annotation precondition.

### Clients

- **`sform story set`** (`Cli/src/Commands/Storyteller/StorytellerSetCommand.cs`): after `HandleDeletedKeysAsync` (lookup, the **Restore and update** / **Create new** / **Cancel** choice, the restore), `ReadStoredAsync` reads the key with `includeInactive: true` unless `--replace` is given, and `SetAnnotationAsync` posts `annotation.ToSdkAnnotation(stored)` (`Cli/src/Model/AnnotationExtensions.cs`). The merge rules: a member of the input wins when it is not null; the labels are the stored ones, then the new ones of the input; each value of the input replaces the stored value of the same name whole; a unit's type and definition come from the input, else the stored unit, else `event` and `unknown`. `-c` wins over the file. A `412` or `404` lands in "Error occurred" with exit `1`.
- **MCP `set_annotation`** (`Mcp/src/Tools/AnnotationTools.cs:225-322`): reads with `includeInactive: true` (a deleted key counts as missing), copies the editable members into a fresh body, applies the given fields (`labels` and `values` replace the stored sets), clears `clearFields` by leaving them out of the body, and posts the whole body. A missing key goes through the create rules first: a unit needs `unitType` and `unitDefinition`, the name rule (`EnsureNewNamesAsync`), and the deleted lookup unless `recreateDeleted`.
- **.NET SDK** (`Sdk.NSwag`): generated methods return the body without the response headers; `ApiSdk.Conditional.cs` is the hand-written twin of the configuration reads that keeps the headers (`ConditionalResponse<T>`). The SDK `Annotation` model cannot send a JSON `null` (`NullValueHandling.Ignore`).
- **TypeScript SDK** (`sdk.typescript`): generated functions drop the headers and take no header arguments (`output.headers` is off); `lib/conditional.ts` reads headers through `runRequest`.
- **Admin UI** writes whole bodies (`useAnnotationActions.save`, `saveMany`; `AnnotationCreateDialog.vue` merges the dialog's title over a restored record). Its cross-origin requests cannot read an `ETag` (the platform's CORS cannot expose response headers).

## Proposed Changes

### 1. The version of an annotation and its entity tag

- **The version is the Cosmos `_etag` of the annotation item.** It changes with every replacement of the item: an update, a delete, a restore, a re-create, and also a relation change, when a child is created or deleted under it (the relation arrays are part of the representation, for example `UnitNames`). A no-op write keeps it.
- **The entity tag is strong:** `"a1.<base64url of the _etag>"`, built and parsed by `AnnotationEntityTags` in `Api.Functions/src/ConditionalWrites/`. `a1` names the representation; a change of the annotation's JSON bumps it, so no strong tag of the old representation matches. A tag that is weak, has another prefix or does not decode never matches.
- **Reads:** `GetAnnotation` calls `GetAnnotationWithMarkerAsync` and sends `ETag` with a `200` of an active, visible annotation. A deleted annotation read with `includeDeleted`, and a `404` (missing, deleted, or inactive without `includeInactive`), get none. `Absent` and a null marker are never sent.

### 2. Conditions on the service (Backend.Core)

- **`AnnotationWriteCondition`** (`Backend.Core/src/Annotating/AnnotationWriteCondition.cs`), a record with `IfMatch` and `IfNoneMatch`, each an optional `AnnotationVersionMatch(bool Any, IReadOnlyList<string> Markers)`; `None` and `IsEmpty`. `Check(FullKey key, bool exists, string? marker)` throws `PreconditionFailedException` with the new `ErrorCodes.AnnotationPreconditionFailed` when it does not hold:
  - `IfMatch` holds when the annotation exists (is not deleted) and is `Any` or its marker is listed;
  - `IfNoneMatch` holds when the annotation does not exist, or is not `Any` and its marker is not listed;
  - both are checked when both are given (RFC 9110 §13.2.2), so `If-Match` with `If-None-Match: *` never holds.
  - The messages say what failed: the annotation changed since it was read, it does not exist (or is deleted), or it already exists; each with the hint to read it again.
- **`AnnotationWriteResult(Annotation Annotation, string? Marker)`**: a write's stored annotation and the marker it left.
- **`IAnnotationService`** gets two members with default bodies, so the test doubles keep compiling:
  - `SetAnnotationAsync(organization, annotation, actor, AnnotationWriteCondition condition)`: the default delegates to the unconditional write for an empty condition and throws `NotSupportedException` for any other, so a service that cannot check a condition never writes unconditionally.
  - `PatchAnnotationAsync(FullKey key, AnnotationPatch patch, AuditActor actor, AnnotationWriteCondition condition)` (section 4): the default throws `NotSupportedException`.

### 3. Conditional writes in Cosmos DB

- **`BatchPlan`** keeps an optional callback per operation, run with the operation's `TransactionalBatchOperationResult` after its batch succeeds; `Add` takes `BatchOperation`s next to the plain actions.
- **`CosmosAnnotationService.SetAnnotationAsync(…, condition)`**, inside `RetryAsync`, on every attempt: load the annotation item, check the **original** condition against it (never a newer ETag), then update (active) or create (missing or deleted).
  - A failed condition throws `PreconditionFailedException`, which `RetryAsync` does not catch, so it answers `412` whichever attempt finds it.
  - A conditional write reaches `CreateCoreAsync` only when the condition lets a missing key through (`If-None-Match`, or none), never with `If-Match`.
  - The guarded batch uses the ETag the condition was checked against. When another writer replaced the annotation in between, the batch fails, the retry reloads, and the condition fails with `412` instead of overwriting. When only the audit item changed (a configuration write appends to it), the retry passes and writes.
  - `UpdateCoreAsync` returns the marker its replacement committed, or the read marker for a no-op; `CreateCoreAsync` returns the marker of the target's create or replace.
- The unconditional `SetAnnotationAsync` is the conditional one with `None`; its behaviour does not change.

### 4. The merge `PATCH`

#### 4.1 The patch document

A patch applies to the **editable document** of an annotation, a JSON object with exactly these members, all present (null when unset):

| Member | Type |
| --- | --- |
| `IsDisabled` | boolean or null |
| `ValidFrom`, `ExpiresAt` | ISO 8601 date and time with an offset, or null |
| `TimeZone`, `Title`, `Description`, `DocumentationLink` | string or null |
| `Labels` | array of strings (`[]` when there are none) |
| `Values` | object (`{}` when there are none) |
| `UnitType`, `UnitDefinition` | string (units only) |

Two formats, both RFC-conformant, chosen by `Content-Type`:

- **`application/merge-patch+json`** (RFC 7396): a member left out keeps its value; `null` clears it; `Values` merges recursively, so `{"Values":{"plan":"pro"}}` changes one value and `{"Values":{"old":null}}` removes one; an array (`Labels`, an array value) is replaced whole. Member names match case-insensitively, as the API reads bodies. The identity, type-specific, relation and server-owned members of an annotation (`AnnotationKey`, `AnnotationType`, `Name`, `ProjectName`, `ViewName`, the ancestor keys and names, the relation arrays, `Created`, `Modified`, `Deletion`) are ignored, as `PUT` ignores them, so a read body can be sent back. Any other member is a `400`. Limitation: Merge Patch cannot set a value to JSON `null` (it removes it); a JSON Patch or a full write can.
- **`application/json-patch+json`** (RFC 6902, as configurations take): operations on the editable document; paths are JSON Pointers, case-sensitive. `add /Labels/-` appends a label, `add /Values/plan` sets one value (the document always has `Labels` and `Values`), `replace /Title` works on an unset title (it is present as null), and `test` is a precondition: a failed one answers `412 PatchTestFailed`, as for configurations.
- **`application/json`**, or no `Content-Type`: an object is a merge patch, an array a JSON Patch.
- Any other media type answers `415` with `Accept-Patch: application/merge-patch+json, application/json-patch+json` (RFC 5789 §2.2).

After the patch:

- The document must still have only the editable members, with the types above; a member of another type, an unknown member, or a label that is not a string is a `400 InvalidAnnotation` naming the member.
- A removed or null member is unset; `Labels: null` means no labels, `Values: null` no values.
- `UnitType` and `UnitDefinition` cannot be removed or set to null: a unit always has them (`400 InvalidAnnotation`).
- **Labels keep their first occurrence** (ordinal), so appending a label the annotation has changes nothing.
- The result is normalized (`AnnotationFactory.Normalize`) and validated (`AnnotationRules.Validate`) as a full write is.

**`AnnotationPatch`** (`Backend.Core/src/Annotating/AnnotationPatch.cs`) holds the format and the parsed document; `AnnotationPatch.Merge(JsonObject)` and `AnnotationPatch.Json(JsonArray)` parse it up front, so a malformed JSON Patch is a `400 PatchInvalid` before any read. `Apply(Annotation current)` returns the patched annotation; it is pure and runs again on every attempt. `AnnotationPatchDocument` turns an annotation into its editable document and back. Backend.Core references JsonPatch.Net for this.

#### 4.2 `PatchAnnotationAsync` in Cosmos DB

Inside `RetryAsync`, on every attempt:

1. Load the annotation item. Missing: `NotFoundException(AnnotationNotFound)`. Deleted: `NotFoundException(AnnotationDeleted)`. A `PATCH` never creates or re-creates a key. Per RFC 9110 §13.2.1 the conditions do not apply to a request that would fail without them, so these come before the condition.
2. Check the original condition (`If-Match`).
3. Apply the patch to the stored annotation, validate the result, and go on through `UpdateCoreAsync`: the audit diff, the no-op, and the guarded batch.

The patch is recomputed against fresh state on every attempt, so a concurrent change of another member is kept, not overwritten: the merge is atomic. A disabled, scheduled or expired annotation can be patched (for example re-enabled). A no-op returns the stored annotation and its marker, without a batch or an audit entry.

#### 4.3 `PATCH v1/{organization}/{project}/{view}/annotations/{key}`

A new function `PatchAnnotation` on the annotation route:

- The same access as `SetAnnotation`: Contributor or the machine scope `Annotation.Write`; the body limit `RequestBodyLimits.Annotation`.
- The body is parsed as JSON (System.Text.Json, a BOM is skipped); the format per 4.1.
- `If-Match` per section 5; `If-None-Match` is a `400` on a `PATCH` (it never creates).
- `200` with the stored annotation and `ETag`; `400` for invalid JSON, an invalid patch or an invalid result; `404` for a missing (`AnnotationNotFound`) or deleted (`AnnotationDeleted`) key, with the hint to `POST` it or restore it; `409` after three concurrent changes; `412` for a failed `If-Match` (`AnnotationPreconditionFailed`) or `test` (`PatchTestFailed`); `415` for another media type.

### 5. Preconditions on `POST` and `PUT`, and the headers

- **`ConditionalWrites/WriteConditions`** parses `If-Match` and `If-None-Match` into an `AnnotationWriteCondition`, over every field line:
  - `*`, or a list of entity tags (`EntityTagHeaderValue.TryParseStrictList`); a malformed header is a `400` (`InvalidPrecondition`): ignoring it would turn an intended conditional write into an unconditional one.
  - `If-Match` compares strongly: a weak tag never matches. `If-None-Match` compares weakly. Tags that do not decode stay listed as no marker, so they match nothing.
- **`SetAnnotation`** passes the condition to the conditional service method. `If-Match: <tag>` updates only that version, and never re-creates a deleted key; `If-None-Match: *` creates only (a missing or deleted key).
- **`ETag` on writes:** the `200` of `POST` and of `PATCH` carries the tag of the stored annotation; the `200` of `PUT` does not, as RFC 9110 §9.3.4 forbids a validator after a transformed `PUT` (the server normalizes the body). The SDKs use `POST`.
- **OpenAPI:**
  - `Definitions.Parameters.IfMatch = "If-Match"`; header parameters `If-Match` and `If-None-Match` on `SetAnnotation`, `If-Match` on `PatchAnnotation`;
  - `AnnotationResponseHeaders` (`ETag`) on the `200` of `GetAnnotation`, `SetAnnotation` and `PatchAnnotation`;
  - `412` on `SetAnnotation` and `PatchAnnotation`, `404` and `415` on `PatchAnnotation`;
  - the body of `SetAnnotation` becomes `required` (a write without one is a `400` anyway), so the generated .NET method keeps `body` before the new optional header parameters;
  - the `PatchAnnotation` body is `application/merge-patch+json` with an `AnnotationPatch` schema (the editable members, optional and nullable); `AnnotationDocumentFilter` adds `application/json-patch+json` (an array of operations).
  - The export tests (`OpenApiExportTests`: the machine operations, the paths, the bodies; `ForbiddenResponseDocumentFilterTests`; `RouteDispatchTests`; `MachineRestrictionHttpTests.KeyedHandlers`) list the new operation.
- **The SDKs are regenerated** from the exported document (`Sdk.NSwag/README.md`): `ApiSdk.g.cs` with NSwag 14.6.3 and `sdk.typescript/src/generated` with `npm run generate`.

### 6. The .NET SDK

`Sdk.NSwag/src/ApiSdk.Annotations.cs`, hand-written next to `ApiSdk.Conditional.cs`, on `IAnnotationsApiClient`:

- `GetAnnotationVersionedAsync(organization, project, view, key, includeInactive, cancellationToken)`;
- `SetAnnotationVersionedAsync(organization, project, view, key, body, ifMatch, ifNoneMatch, cancellationToken)` (`POST`);
- `PatchAnnotationVersionedAsync(organization, project, view, key, JToken patch, ifMatch, cancellationToken)`: a `JArray` goes as `application/json-patch+json`, a `JObject` as `application/merge-patch+json`, with its explicit `null`s.

Each returns `VersionedResponse<Annotation>` (`StatusCode`, `Result`, the raw `ETag`, the `Headers`), routes through the client's hooks (authorization) and throws what the generated methods throw, `ApiException<ErrorResponse>` for a `412`. **`AnnotationJsonPatch`** builds a JSON Patch of the editable document (`Set(member, value)`, `Clear(member)`, `SetValue(name, value)`, `SetValues(object)`, `AddLabel(label)`, `SetLabels(labels)`), escaping names per RFC 6901; `sform` and the MCP server share it.

### 7. The TypeScript SDK

`lib/annotations.ts`, exported from the package root: `getAnnotationVersioned`, `setAnnotationVersioned` and `patchAnnotationVersioned` return `{ value, etag }` through `runRequest`, take `ifMatch` / `ifNoneMatch`, and throw `StorytellerError` as the generated functions do. The README documents them and the CORS requirement: a cross-origin browser needs `If-Match` and `If-None-Match` allowed in the preflight and `ETag` in `Access-Control-Expose-Headers`, which the platform's CORS cannot configure (a same-origin proxy or API Management can).

### 8. `sform story set`

The command keeps its options, prompts and merge rules; only the requests change.

- **Merge (default):** the input becomes a JSON Patch (`AnnotationJsonPatch`), and the command sends one `PATCH`, without a read:
  - each member the input has (not null): `add /<Member>`; for a unit also `UnitType` and `UnitDefinition`;
  - each value of the input: `add /Values/<name>`, so it replaces the stored value of that name whole; `-c` still wins over the file;
  - each label of the input: `add /Labels/-`; the server keeps the first occurrence, so the labels are the stored ones, then the new ones.
  - A missing key (`404 AnnotationNotFound`) is created with the input alone, by `POST` with `If-None-Match: *`; a key created meanwhile (`412`) is patched instead. At most three rounds, then "changed concurrently" and exit `1`.
  - A key deleted after the lookup (`404 AnnotationDeleted`) is not re-created: the command says so and exits `1`, so a run again asks what to do.
- **Restore and update:** the restore, then the same `PATCH`, so the restored record keeps its content.
- **Create new:** `POST` of the input alone with `If-None-Match: *`, which re-creates the deleted keys; a key restored or created meanwhile (`412`) is patched instead.
- **`--replace`:** a versioned read (`includeInactive: true`), then the full body by `POST` with `If-Match: <its tag>`, or with `If-None-Match: *` when there is none. A concurrent change answers `412`, which the command prints with the server's message and code, exit `2`.
- Errors: a `404` or `412` it does not handle prints the server's message (`WriteApiError`); the others as now.
- `ToSdkAnnotation` loses its `stored` parameter (the body of a create or a replace is the input alone), and `ReadStoredAsync` goes.

### 9. MCP `set_annotation`

The same pattern, so the tool no longer overwrites concurrent edits:

1. The input checks as now, before any request.
2. `PATCH` with a JSON Patch of the given fields: each given field `add`, `labels` and `values` replace their whole set (`add /Labels`, `add /Values`), `clearFields` set to null.
3. A `404` takes the create path as now (unit members, the name rule, the deleted lookup unless `recreateDeleted`) and writes the full body with `If-None-Match: *`; a `412` (created meanwhile) goes back to step 2. At most three rounds.

The tool's description and semantics stay; its hints stay (`Destructive`, `Idempotent`).

### 10. Documentation

- `docs/Platform/Storyteller/annotation-lifecycle.md`: a section on the version, `ETag`, `If-Match`, `If-None-Match`, `412` versus `409`, the `PATCH` formats and their rules, that a child changes its parent's tag, and that a `PATCH` never re-creates; the "Creating in clients" text and the `sform story set` row.
- `docs/42for.net/platform/annotations.md` (Restore and update, the merge), `docs/Platform/Storyteller/mcp.md` (`set_annotation`), `docs/Platform/Storyteller/authentication.md` (CORS), the SDK READMEs (`Sdk.NSwag/README.md`, `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md`, `sdk.typescript/README.md`).

### 11. Tests

- **Backend.Core:** `AnnotationWriteCondition` (every combination), `AnnotationPatch` (both formats: omission, null, nested `Values`, arrays, case-insensitive members, ignored and unknown members, wrong types, unit members, label duplicates, `test`, rules).
- **Backend.CosmosDb** (`STORYTELLER_TESTS_USE_CONTAINER=true`), with `InterceptedRepositoryProvider` and a competing write through the shared service:
  - `If-Match`: a competing change before the batch gives `412` and is kept; an audit-only change before the batch still writes; a delete between read and write gives `412` and re-creates nothing; a matching no-op returns the read marker; a stale no-op gives `412`;
  - `If-None-Match: *` against a concurrent create gives `412`;
  - the returned marker equals the item's `_etag`; a re-create changes it;
  - `PATCH`: a concurrent change of another member between read and batch is kept; a missing and a deleted key give `404`; a stale `If-Match` gives `412`; the audit holds only the changed members.
- **Api.Functions:** `ETag` on `GET` (not on a deleted or inactive read), on `POST` and `PATCH`, not on `PUT`; the header parsing (lists, `*`, weak tags, unknown prefixes, malformed); the `PATCH` media types, statuses and access; routes and OpenAPI.
- **SDKs:** the .NET twins against a stub handler (headers, media types, explicit nulls, `ETag`, errors like the generated methods); the TypeScript helpers with a fake fetch.
- **`sform`:** `StorytellerSetBodyTests` through the real client: the JSON Patch of each input, the create after a `404`, the patch after a `412`, Restore and update, Create new, `--replace` with `If-Match` and `If-None-Match`; `StorytellerSetCommandTests` for the order of calls.
- **MCP:** `set_annotation` patches an existing key, creates a missing one with `If-None-Match: *`, retries after a `412`, and keeps `clearFields` and `values` as they are.

### 12. Out of scope

- **The admin UI** keeps its whole-body writes. It can move to `PATCH` (which needs no `ETag`), or to `If-Match` once the deployment exposes `ETag` cross-origin; both need its mock API too. A follow-up.
- `If-None-Match` → `304` on the annotation read, and conditions on the bulk writes and `Api.Web`.
