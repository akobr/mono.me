# Phase 1 of Conditional and Merge Writes of Annotations

## Overview

Phase 1 of [Conditional and Merge Writes of Annotations](../2026-11-08%20Conditional%20and%20Merge%20Writes%20of%20Annotations.md) (Linear 424-210): the version of an annotation and the conditions of its writes on the server. A read answers the strong `ETag` of the version, `POST` and `PUT` honour `If-Match` and `If-None-Match` against the item each attempt replaces, and `POST` answers the tag of the version it wrote. The spec's sections 1, 2, 3 and 5 (without the `PATCH`), with their tests.

## What Was Done

### 1. The version of an annotation and its entity tag

- **`AnnotationEntityTags`** (`Api.Functions/src/ConditionalWrites/AnnotationEntityTags.cs`): `"a1.<base64url of the marker>"`, strong. `GetMarker` decodes a tag; a weak tag (unless the caller allows it, for `If-None-Match`), `*`, another prefix or a value that does not decode give `null`. `ToHeader` never sends `BindingDependency.Absent` or an empty marker.
- The marker is base64url-encoded whole, quotes included, rather than the Cosmos `_etag` with its quotes stripped: any marker round-trips, and the tag stays a valid `etagc` whatever Cosmos DB answers.
- **`GetAnnotation`** calls `GetAnnotationWithMarkerAsync` and sets the tag only for the body of that read: not for a deleted annotation read with `includeDeleted`, not for a `404` (missing, or inactive without `includeInactive`).

### 2. Conditions on the service

- **`AnnotationWriteCondition`** and **`AnnotationVersionMatch`** (`Backend.Core/src/Annotating/AnnotationWriteCondition.cs`): `IfMatch` and `IfNoneMatch`, each `Any` or a list of markers; `Holds(exists, marker)` and `Check(key, exists, marker)`, which throws `PreconditionFailedException` with the new `ErrorCodes.AnnotationPreconditionFailed`. Both conditions are checked when both are given (RFC 9110 §13.2.2). The messages say what failed: changed since it was read, does not exist or is deleted, already exists.
- `If-None-Match` takes a list of tags too (RFC 9110 allows it on a write; a listed version is refused), instead of the `400` the CodeRabbit plan proposed.
- **`AnnotationWriteResult(Annotation, Marker)`** and the conditional **`IAnnotationService.SetAnnotationAsync(…, condition)`**, whose default writes only an empty condition and throws `NotSupportedException` for any other.
- **`ErrorCodes`**: `AnnotationPreconditionFailed` (412) and `InvalidPrecondition` (400).

### 3. Conditional writes in Cosmos DB

- **`BatchOperation`** (`Backend.CosmosDb/src/Annotating/BatchOperation.cs`): an operation of a batch with an optional callback that gets its result after the batch committed. `BatchPlan` runs the callbacks in the order of the operations; the old `Add` overloads wrap plain actions.
- **`CosmosAnnotationService.SetAnnotationAsync(…, condition)`**: on every attempt it loads the annotation item and checks the original condition against it before choosing between the update and the create. The unconditional overload is the conditional one with `None`.
- **`UpdateCoreAsync`** returns the marker its replacement committed (kept through `BatchOperation.KeepingETag`), or the read marker for a no-op.
- **`CreateCoreAsync`** returns a `CreateResult` with the marker of the key's item, and takes an optional **`loadedTarget`**: the item the caller loaded and checked, so the create replaces (or creates) exactly that version. A change in between fails the batch (`409` on the create, `412` on the replace), and the retry reloads and checks the condition again. Not in the spec: without a condition, a create that loses to a concurrent create now becomes an update on the retry, where before it answered `409` "already exists".
- A failed condition is a `PreconditionFailedException`, which `RetryAsync` does not retry, so it answers `412` on whichever attempt finds it.

### 5. Preconditions on `POST` and `PUT`, and the headers

- **`WriteConditions.TryRead`** (`Api.Functions/src/ConditionalWrites/WriteConditions.cs`) reads every field line of both headers with `EntityTagHeaderValue.TryParseStrictList`. A header that is not `*` or a strict list, `*` mixed with tags, or an empty header is a `400 InvalidPrecondition`. `If-Match` keeps only strong annotation tags as markers, `If-None-Match` also weak ones; a tag that names no marker matches nothing, so `If-Match` of only such tags fails with `412`.
- **`SetAnnotation`** passes the condition to the service, and sets the `ETag` of the written version on a `POST`, not on a `PUT` (RFC 9110 §9.3.4).
- **OpenAPI:** `Definitions.Parameters.IfMatch`; the header parameters `If-Match` and `If-None-Match` of `SetAnnotation`; `AnnotationResponseHeaders` (`ETag`) on the `200` of `GetAnnotation` and `SetAnnotation`; the `412` of `SetAnnotation`; its body is `required`, so the generated .NET method keeps `body` before the new optional parameters. The descriptions are `AnnotationEntityTag`, `IfMatchAnnotation`, `IfNoneMatchAnnotation` and `ResponseAnnotationPreconditionFailed`.

### 11. Tests

- **Backend.Core:** `AnnotationWriteConditionTests` (every combination of the two conditions, the messages, ordinal markers).
- **Backend.CosmosDb** (`CosmosAnnotationConditionalWriteTests`, with `STORYTELLER_TESTS_USE_CONTAINER=true`): `If-Match` of the current version writes and answers the item's `_etag`; an earlier version fails and writes nothing; a competing change before the batch fails and is kept; an audit-only change before the batch still writes on the retry; a delete between read and write fails and re-creates nothing; `If-Match` of a deleted or missing key creates nothing; a no-op answers the read marker, a stale one fails; `If-None-Match: *` creates and re-creates but refuses an existing key and a concurrent create; an unconditional create that loses to a concurrent create updates it; a child changes its parent's marker.
- **Api.Functions** (`AnnotationsHttpTests.Conditional.cs`): the tag of a read and its absence for a deleted, inactive or unmarked read; the tag of a `POST` and none for a `PUT`; the header parsing (lists, several lines, `*`, weak and foreign tags, malformed values); the `412`; the tags' round trip; the contract in the OpenAPI document.
- **Doubles:** `RecordingAnnotations` tells a marker and records the condition; `MachineRestrictionHttpTests` reads `GetAnnotationWithMarkerAsync`, returns a write result for the conditional write, and its `RecordingProxy` answers `null` to an overload whose return type the configured result does not fit.
- `ForbiddenResponseDocumentFilterTests` lists `SetAnnotation` among the operations that document a `412`.
