# Phase 3 of Conditional and Merge Writes of Annotations

## Overview

Phase 3 of [Conditional and Merge Writes of Annotations](../2026-11-08%20Conditional%20and%20Merge%20Writes%20of%20Annotations.md) (Linear 424-210): the clients. The .NET and TypeScript SDKs read and write annotations with their `ETag` and send either patch format; `sform story set` merges with one `PATCH` instead of its read, merge and write; the MCP server's `set_annotation` patches the given fields; and the documentation describes the new contract. The spec's sections 6 to 10, with their tests.

## What Was Done

### 6. The .NET SDK

- **`ApiSdk.Annotations.cs`** (`Sdk.NSwag/src/`), on `IAnnotationsApiClient` and `AnnotationsApiClient`: `GetAnnotationVersionedAsync` (only `includeInactive`: a deleted read has no tag), `SetAnnotationVersionedAsync` (`POST`, `ifMatch` and `ifNoneMatch` sent as given) and `PatchAnnotationVersionedAsync` (a `JArray` as `application/json-patch+json`, a `JObject` as `application/merge-patch+json` with its explicit nulls, anything else an `ArgumentException` before a request). They build the route and query of the generated methods, run the client's hooks (authorization), and throw the exceptions of the generated methods with the same messages, per operation.
- **`VersionedResponse<T>`**: `StatusCode`, `Result`, the raw `ETag` and the headers with case-insensitive names. A new type rather than `ConditionalResponse<T>`, which models a read that can answer `304`.
- **`AnnotationJsonPatch`**: `Set`, `Clear`, `SetValue`, `AddLabel`, `ToJson` (a copy), `IsEmpty`, `Count`, `Escape`; every operation is an `add`, which works whatever the member held, since the editable document always has every member. The spec's `SetValues` and `SetLabels` became `Set(nameof(Values), …)` and `Set(nameof(Labels), …)`.
- **Tests** (`AnnotationsVersionedTests`, 39): the tag, the route (escaped as the generated read), the conditions sent unchanged, both patch formats and their bodies, the authorization, the JSON Patch builder (escaping, dates, tokens, nulls), and every documented failure compared with the generated method's exception.

### 7. The TypeScript SDK

- **`lib/annotations.ts`**, exported from the package root: `getAnnotationVersioned`, `setAnnotationVersioned` and `patchAnnotationVersioned` return `{ value, etag }` through `runRequest`, like `lib/conditional.ts`. An explicit `ifMatch` or `ifNoneMatch` wins over a header of the same name in `options.headers`, and an empty string removes it. The patch helper picks the media type from the patch (array or object) and sets `Content-Type` itself; any other value is a `TypeError` before a request. An empty `ETag` header is `etag: undefined`.
- **README:** a section "Conditional writes of annotations": the helpers, the rules of the conditions and the patches, the generated `setAnnotation` (its body is now required) and the merge-only `patchAnnotation`, and what a cross-origin browser needs (`If-Match` and `If-None-Match` in the preflight, `ETag` exposed, which the platform's CORS cannot do; without a readable tag, `ifMatch: etag` sends no condition).
- **Tests** (`test/annotations.test.ts`, 20). The admin UI builds and its 1530 tests pass against the regenerated SDK without a change.

### 8. `sform story set`

- **Merge (default):** `ToJsonPatch()` (`Cli/src/Model/AnnotationExtensions.cs`) turns the input into a JSON Patch: each member the input has, a unit's type and definition when the file names them, each value by name, each label appended. `MergeAsync` sends it with `PatchAnnotationVersionedAsync`, without a read. A `404 AnnotationNotFound` creates the key with the input alone (`SetAnnotationAsync(…, if_None_Match: "*")`); a `412` on that create sends the patch again; at most three rounds (`MaxWriteRounds`), then "changed concurrently" and exit `1`. A `404 AnnotationDeleted` without **Create new** says the key was deleted meanwhile and exits `1`, and re-creates nothing.
- **Create new** starts with the create; a key deleted again before the patch is re-created, as the user chose. **Restore and update** restores, then merges.
- **`--replace`:** `ReplaceAsync` reads the key with its tag (`includeInactive: true`) and writes the input alone with `If-Match`, or with `If-None-Match: *` after a `404`; a read without a tag sends no condition. A concurrent change answers `412`, printed with the server's message and code (exit `2`).
- **Labels** are the `-l` labels in their order, then the file's new ones; the unstable `HashSet` order of PR #169 is gone, and a `null` label of a file is dropped.
- `ToSdkAnnotation` lost its `stored` parameter, `ReadStoredAsync` is gone, and an unhandled `404` or `412` prints the server's message and code.
- **Tests:** `StorytellerSetBodyTests` assert the requests on the wire through the real client (the JSON Patch of each input, its media type, no read and no `If-Match`; the create with `If-None-Match: *` and the full body; the rounds; Restore and update; Create new; `--replace` with `If-Match` and `If-None-Match`; the client's refusal of a body on both writes). `ScriptedHttpHandler` answers response headers and records the request's `Content-Type`. `StorytellerSetCommandTests` check the order of the calls.

### 9. MCP `set_annotation`

- The tool builds an `AnnotationJsonPatch` of the given fields (`labels` and `values` replace their whole sets; each `clearFields` entry sets its member to null, once) and sends one `PatchAnnotationVersionedAsync`. Only a `404` with `AnnotationNotFound` or `AnnotationDeleted` takes the create path (`EnsureCreatableAsync`: the unit members, the name rule, the deleted lookup unless `recreateDeleted`), written with `If-None-Match: *`; a `412` of that create patches again, at most three rounds. A `404` without one of those codes, and a `412` of the patch itself, are errors: an older API without the `PATCH` route must not be answered with a full write.
- The parameters, the hints and the meaning stay; the description adds that an update changes only the given fields, merged on the server. `TryGetAsync` and the old `Clear` are gone; `AnnotationBodies.CopyEditable` is now used by its own tests only.
- **Tests:** `AnnotationToolsTests` (strict mock) assert the exact patch, the create path, the deleted keys, the rounds and the errors; `ManagementToolsHostTests` check the `PATCH`, its `Content-Type` and body, and a create with `If-None-Match: *`, through the real SDK. `RecordedRequest` keeps the request's headers and media type.

### 10. Documentation

- **`annotation-lifecycle.md`:** the sections "Versions and conditional writes" and "Merge writes (PATCH)", the `PATCH` row and the conditions of the `PUT`/`POST` row, "Creating in clients" (Restore and update is a restore and a `PATCH`, Create new a `POST` with `If-None-Match: *`; the admin UI still writes whole bodies), the `sform story set` row, and the new error codes.
- **`mcp.md`** (`set_annotation`), **`authentication.md`** (the CORS needs of conditional writes, and `PATCH` in the scope `Annotation.Write`), **`access.md`**, **`42for.net/platform/annotations.md`** and **`using-the-platform.md`**, **`Sdk.NSwag/README.md`** (the annotation versions, and that `ApiSdk.Annotations.cs` must be compared after a regeneration) and its NuGet readme.

### Review of the branch

A review of the whole branch (five reviewers, each finding checked by a skeptic) confirmed these, now fixed:

- **A create that loses its parent's relation wave** (`CosmosAnnotationService.SetAnnotationAsync`): the key's batch commits before the relation arrays of its parent (wave 10). When the parent changed in between, the retry found the key this write had created and answered `412` to `If-None-Match: *`. The version an earlier attempt committed (`CreateCoreAsync`'s new `targetCommitted` callback) is not checked again; the retry is a no-op update that answers `200` and the marker. Test: `IfNoneMatchAny_AParentChangedBeforeItsRelationArrays_StillAnswersTheCreate`. A later review of the PR added that the retry also completes the relation arrays the failed wave did not write (`CompleteParentRelationsAsync`), and the test checks the parent lists the child.
- **The `412` message** named `If-Match` when both headers were given and `If-None-Match` failed; it now names the part that failed. `AnnotationVersionMatch` moved to its own file.
- **The growth of a JSON Patch:** a `copy` doubles a value with a few bytes of patch, so a 1 KB request could build gigabytes. `AnnotationPatch` weighs every value an operation adds (`MaxDocumentWeight`, 16 Mi characters) and the depth it ends at (`MaxDocumentDepth`, 64), and fails the operation with `400 PatchInvalid`. A patch value nested deeper than a body is read is an invalid patch, and `Values` that a patch nests too deep are `400 InvalidAnnotation`, both instead of `500`.
- **Legacy nulls:** a full write lets a `null` label, or a `null` `UnitType` or `UnitDefinition`, in. Such a record no longer fails every patch: the null label is dropped, and a null unit member stays null until a patch sets it.
- **The OpenAPI document:** the `If-Match` of `PatchAnnotation` has its own description (a missing or deleted key answers `404` first), and `UnitType` and `UnitDefinition` of `AnnotationMergePatch` are not nullable. The SDKs are regenerated from `open.api.v0.10.30.json`, which replaces the `0.10.28` snapshot of this branch.
- **Tests and docs:** the double of the deleted read tells a marker, as Cosmos DB does, so the test checks that no `ETag` goes out; a test of the tag of an inactive read with `includeInactive`; the leading-zero index tested where the number would address an item; the keys of the TypeScript README examples (`sbt`, not `sbj`), its list of generator corrections, and the wording about merges in `export-import.md`.

### Review of the pull request

The review of PR #174 confirmed these, now fixed:

- **`sform story set --replace`** no longer re-creates a key deleted after the lookup: after the read's `404` it looks up the key and its ancestors again and stops (exit `1`) unless **Create new** was chosen. A read answered without an `ETag` writes nothing (exit `3`) instead of writing without a condition.
- **The size of a patched annotation:** `MaxDocumentWeight` is 4 Mi characters, the limit of a body; a batch that Cosmos DB refuses as too large (`413`, an item above 2 MB) is `400 BodyTooLarge` instead of a `500`.
- **`BatchPlan`** checks that a batch answered one result per operation before it hands on the ETags.
- **Docs:** a busy parent changes its tag with every child, so `annotation-lifecycle.md` advises a `PATCH` without `If-Match` to change its members; the SDK README and its NuGet readme name the breaking change of the generated `SetAnnotationAsync`, and the description of `PatchAnnotation` (the remarks of the generated method) says that a model that leaves nulls out cannot clear a member. The SDKs are regenerated from `open.api.v0.10.31.json`.
- Small: `GetSegments` is called once per source, and `CreateCoreAsync` uses `BatchOperation.KeepingETag` instead of a second helper.

- **Every create completes the relation arrays it lost:** `CreateCoreAsync` itself, when a later wave loses to a change of a parent after the keys committed, completes the arrays under fresh ETags (`CompleteParentRelationsWithRetriesAsync`, three tries) instead of handing the failure to the retry of the whole create. So a bulk create reports the key, the single create no longer answers `409` for its own key, and an import calls it `Created`. Test: `Create_AParentChangedBeforeItsRelationArrays_CompletesThem` for the three paths. The root flags of children of a re-created key, also written in that wave, are not completed this way.

Not changed: the tag helper keeps `BindingDependency.Absent` as the marker of no item, which is what the service answers; a `304` on the annotation read stays out of scope; the two copies of the OpenAPI document are the convention of the two SDK folders.

### Not done here

- **The admin UI** still writes whole bodies (`useAnnotationActions.save`, `saveMany`, the restore dialog). It can move to `PATCH`, which needs no readable `ETag`; its mock API needs a `PATCH` handler first.
- **Configuration `PATCH`** keeps the JsonPatch.Net escaping bug described in the phase 2 review.
- **The version of the .NET SDK:** the break of the generated `SetAnnotationAsync` is documented; whether it takes a breaking-change version bump is the owner's call.
