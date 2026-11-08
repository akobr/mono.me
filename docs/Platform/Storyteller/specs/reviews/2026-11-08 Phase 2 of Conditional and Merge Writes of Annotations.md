# Phase 2 of Conditional and Merge Writes of Annotations

## Overview

Phase 2 of [Conditional and Merge Writes of Annotations](../2026-11-08%20Conditional%20and%20Merge%20Writes%20of%20Annotations.md) (Linear 424-210): the merge write. `PATCH …/annotations/{key}` takes a JSON Merge Patch or a JSON Patch of the editable document, the service applies it to the stored annotation on every attempt, and the SDKs are regenerated from the new OpenAPI document. The spec's section 4, the `PATCH` parts of section 5, and its tests.

## What Was Done

### 4.1 The patch document

- **`AnnotationPatch`**, **`AnnotationPatchFormat`** and **`AnnotationPatchDocument`** (`Backend.Core/src/Annotating/`). `AnnotationPatch.Merge(JsonObject)` deep-copies the document; `AnnotationPatch.Json(JsonArray)` parses it with JsonPatch.Net up front, so an invalid JSON Patch is a `JsonPatchException` (`Invalid`, `400 PatchInvalid`) before any read. `Apply(current)` is pure: it builds the editable document, applies the patch, checks the shape, then normalizes (`AnnotationFactory.Normalize`) and validates (`AnnotationRules.Validate`).
- **Merge patch:** member names match case-insensitively; a member that is a property of the annotation's runtime type but not editable is ignored (found by reflection, so a read body can be sent back); any other member, or one member twice in different case, is a `400 InvalidAnnotation`. `Values` merge recursively per RFC 7396, every other member is replaced.
- **JSON Patch:** JsonPatch.Net parses and validates the document, but **the operations are applied by `AnnotationPatch` itself**. JsonPatch.Net 5.0.3 does not unescape the last segment of a path when it writes: `add /Values/a~1b` creates the name `a~1b`, not `a/b`, and `remove` of such a path removes nothing. The applier unescapes every segment once (`~1`, then `~0`), keeps paths case-sensitive, and fails an operation with the kinds and messages of `JsonExtensions.ApplyPatch` (`TestFailed` for `test`, else `OperationFailed`). A `null` operation is refused up front, as JsonPatch.Net throws a `NullReferenceException` for it.
- **After the patch:** only the editable members with their types; `UnitType` and `UnitDefinition` cannot be removed or null; labels keep their first occurrence (ordinal); `Values` entries become what System.Text.Json reads for a body (`JsonElement`, or `null` for a JSON null). An empty `Labels` or `Values` comes back empty, not `null`; the audit diff treats both the same.
- Backend.Core references **JsonPatch.Net** for the parsing.

### 4.2 `PatchAnnotationAsync` in Cosmos DB

- **`IAnnotationService.PatchAnnotationAsync(key, patch, actor, condition)`**, whose default throws `NotSupportedException`.
- **`CosmosAnnotationService.PatchAnnotationAsync`**, inside `RetryAsync`: a missing key is `NotFoundException(AnnotationNotFound)`, a deleted one `NotFoundException(AnnotationDeleted)`, both before the condition (RFC 9110 §13.2.1); then the original condition, the patch applied to the loaded annotation, and `UpdateCoreAsync` with its audit diff, no-op and guarded batch. A concurrent change fails the batch, and the next attempt applies the patch to the changed annotation.

### 4.3 `PATCH v1/{organization}/{project}/{view}/annotations/{key}`

- **`AnnotationsHttp.PatchAnnotation`**: the access of `SetAnnotation`, the key, the conditions (`If-None-Match` is a `400 InvalidPrecondition`), the media type, the body (`RequestBodyLimits.Annotation`, a BOM skipped), then the service.
- **Media types:** `application/merge-patch+json` and `application/json-patch+json` by name (parameters such as `charset` are allowed); `application/json` or none by the shape of the body; anything else is a `415 InvalidContentType` with `Accept-Patch: application/merge-patch+json, application/json-patch+json`.
- **The body** is parsed with `AllowDuplicateProperties = false`, so a member twice is a `400` at the door, also inside `Values`. A body that is not JSON, or not a patch of its format, is a `400 PatchInvalid` before the service is called.
- **Answers:** `200` with the stored annotation and its `ETag`; `404` with the error code and a hint (restore or `POST` for a deleted key, `POST` for a missing one); `412`, `400` and `409` through `ErrorResponseMapping` (a failed `test` is `412 PatchTestFailed`).

### 5. The OpenAPI document and the SDKs

- **`AnnotationMergePatch`** (`Api.Functions/src/Models/`), the schema of the merge patch; `AnnotationDocumentFilter` makes every member optional and nullable and adds `application/json-patch+json` (an array of operations) to the body of `PatchAnnotation`. The operation documents `If-Match`, the `ETag` of its `200`, and `400, 404, 409, 412, 415`. `Definitions` gains `RouteIds.Annotations.PatchAnnotation`, `ContentTypes.MergePatch` and `JsonPatch`, and `ResponseAnnotationPatchPreconditionFailed`.
- **Tests that list operations:** `OpenApiExportTests` (machine operations, paths), `RouteDispatchTests`, `RouteTableTests`, `MachineRestrictionHttpTests.KeyedHandlers`, `ForbiddenResponseDocumentFilterTests`.
- **Regeneration** from `open.api.v0.10.28.json` (NSwag 14.6.3, Orval 8.41). The generated `SetAnnotationAsync` takes `Annotation body` (now required), then `if_Match` and `if_None_Match`; `PatchAnnotationAsync` takes an `AnnotationMergePatch`, whose members ignore nulls, so it sets members but cannot clear one. The MCP call that passed its cancellation token by position, the strict mock of its tests and the CLI test override were adjusted in the same commit, so every commit builds.
- **The TypeScript generator** (`sdk.typescript/src/scripts/openapi-transformer.mjs`) keeps only the merge patch of `PatchAnnotation`. With two request media types Orval sends no `Content-Type`, fetch then sends `text/plain`, and the API answers `415`. The generated `patchAnnotation` sends a merge patch; the hand-written helper of phase 3 sends either format.

### 11. Tests

- **Backend.Core** (`AnnotationPatchTests`, 87 cases): both formats, omission, null, nested `Values`, arrays, case, ignored and unknown members, duplicates, wrong types, unit members, labels, escaping (also `~01`, which must be the name `~1`), `test`, failed operations, purity, normalization, rules.
- **Backend.CosmosDb** (`CosmosAnnotationPatchTests`): only the patched members change and the marker is the item's; a concurrent change of another member (also inside `Values`) is kept; `If-Match` writes only its version; a missing or deleted key is `404` and creates nothing; the audit records only the changed members; a no-op writes nothing and keeps the marker; a disabled annotation is re-enabled; a failed `test` writes nothing.
- **Api.Functions** (`AnnotationsHttpTests.Patch.cs`): the formats by media type and by shape, the `415` with `Accept-Patch`, bodies that are not patches, a BOM, the conditions, the `404` hints, the failures and their statuses, an invalid key, and the contract in the OpenAPI document.

### Found on the way

JsonPatch.Net's escaping bug affects the configurations too: `PatchConfiguration` and `$patch` go through `JsonExtensions.ApplyPatch`, so a JSON Patch of a configuration member whose name has `/` or `~` writes or removes the wrong member (checked against JsonPatch.Net directly), and a `null` operation fails inside JsonPatch.Net with a `NullReferenceException` instead of a `400`. This change leaves the configurations as they are; it needs an issue of its own.
