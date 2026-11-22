# Admin UI Writes Annotation Changes with PATCH

Linear: [424-225 Admin UI: write annotation changes with PATCH instead of whole bodies](https://linear.app/424fun/issue/424-225/admin-ui-write-annotation-changes-with-patch-instead-of-whole-bodies)

Follow-up of 424-210 ([Conditional and Merge Writes of Annotations](../../Storyteller/specs/2026-11-08%20Conditional%20and%20Merge%20Writes%20of%20Annotations.md), PR #174), which added `PATCH …/annotations/{key}` (JSON Merge Patch and JSON Patch, merged on the server against the stored state).

## Problem

The admin UI writes every annotation change as a whole body (`setAnnotation`, `POST`, no condition). The body is the copy the UI holds: the form's draft over the loaded annotation, or the catalog entry of a bulk action. When someone else changes the annotation between that load and the save, the UI writes its old copy back and their change is lost, on every member, not only on the one the user touched. A key deleted meanwhile is even re-created by the `POST`.

- The edit form saves through `useAnnotationActions().save`.
- Enable, disable, add label and remove label of the bulk bar go through `saveMany`, which sends back the catalog entry with one member changed.
- **Restore and update** in `AnnotationCreateDialog.vue` restores the record, then sends `{ ...restored, Title, TimeZone }`.

The mock API (`app/mock/annotation-handlers.ts`) has no `PATCH` handler: in `dev:mock` and e2e an annotation `PATCH` falls through to the SDK's generated Faker handler (a random annotation), and in unit tests it is unhandled. So the mock has to learn `PATCH` first.

## Current State

Paths are relative to `src/Platform/ui.admin/src/` unless they start with `docs/` or `src/`.

### Write paths

- `app/composables/useAnnotationActions.ts`
  - `save(annotation)` (l.98-104): `setAnnotation(org, project, view, key, annotation)`, then `catalog.upsert(saved)` and `invalidateAnnotation`.
  - `saveMany(annotations)` (l.110-140): `setAnnotation` per key, four at a time (`runLimited`, `writeConcurrency`), `progress` for the bulk bar, a failed key collected, then one error `"N of M annotations were not saved: …"`.
  - `setDisabled` (l.308-309): `{ ...catalogEntry, IsDisabled: disabled ? true : null }`.
  - `addLabel` (l.310-316): `{ ...catalogEntry, Labels: normalizeLabels([...Labels, label]) }` (`normalizeLabels`: trimmed, case-insensitive first spelling wins).
  - `removeLabel` (l.317-323): `{ ...catalogEntry, Labels: Labels.filter(case-insensitive ≠ label) }`.
  - `selected(keys)` (l.296-297): the base is the catalog entry (list load, IndexedDB snapshot, or the last upsert), never a fresh read.
- `app/components/annotations/AnnotationForm.vue`
  - `useForm` with `initialValues: toAnnotationDraft(props.annotation)` (l.45-49). The watch on `props.annotation` (l.86-93) resets the form only when it is not dirty or the key changed, so while the user edits, a refetch does not move the form's base. Revert (l.118) and a save (l.100) reset it as well.
  - `save` (l.97-111): `actions.save(toAnnotationBody(draft, props.annotation))`; `400 InvalidAnnotation` messages go to their fields (`invalidAnnotationFields`).
  - The JSON/YAML modes edit the same draft (`setValues`), guarded by `readOnlyIssues` for the key fields.
- `app/forms/annotation.schema.ts`
  - `toAnnotationDraft` (l.90-114): the editable document of the form: key fields, `IsDisabled ?? false`, `ValidFrom`/`ExpiresAt ?? null`, the four strings `?? ''`, `Labels ?? []`, `Values ?? {}`, and for a unit `UnitType`/`UnitDefinition ?? ''`.
  - `toAnnotationBody` (l.122-148): `{ ...original, …every editable member }`, blank strings and empty collections as `undefined` (left out = cleared by a full write). Used only by the form.
- `app/components/annotations/AnnotationCreateDialog.vue` `create()` (l.131-196): after the deleted-key check, **Restore and update** restores the deleted keys with `Descendants = false` (`actions.restoreMany`), then one `setAnnotation(withRelations({ ...restored, …, Title: title.trim() || restored.Title, TimeZone: restored.TimeZone || timeZone }))` (l.160-176). The same `POST` serves a plain create and **Create new**, and the case where only an ancestor was deleted (`restored` undefined: the key itself is created).
- `app/components/annotations/AnnotationBulkBar.vue`: `run(title, action)` shows the success toast or `writeFailureTitle(error, 'The bulk action failed')`; Enable (l.84), Disable (l.93), Remove label (l.113), Add label (l.119); the label comes from a `UInputMenu` with `create-item` and is not trimmed.
- No other code writes an annotation body: no Vue Query mutation, no inline editor; `createMany` (`SetAnnotationsSimple`) only creates.

### The API's PATCH (`docs/Platform/Storyteller/annotation-lifecycle.md`, "Merge writes (PATCH)"; `Backend.Core/src/Annotating/AnnotationPatch.cs`, `AnnotationPatchDocument.cs`; `Api.Functions/src/V1/AnnotationsHttp.cs` l.443-531)

- Order: access (Contributor) → key (`400 InvalidKey`) → `If-Match`/`If-None-Match` syntax (`400 InvalidPrecondition`; any `If-None-Match` is `400 InvalidPrecondition`) → `Content-Type` (`415 InvalidContentType` with `Accept-Patch: application/merge-patch+json, application/json-patch+json`) → size → parse (`400 PatchInvalid`: not JSON, an exact duplicate member, the wrong shape for the format, a malformed operation) → missing key `404 AnnotationNotFound` / deleted key `404 AnnotationDeleted` (with hints) → `If-Match` (`412 AnnotationPreconditionFailed`) → apply → checks → write.
- The editable document: `IsDisabled, ValidFrom, ExpiresAt, TimeZone, Title, Description, DocumentationLink` (null when unset), `Labels` (`[]`), `Values` (`{}`), and for units `UnitType, UnitDefinition`.
- Merge patch: top-level names case-insensitive; a name twice in different cases → `400 InvalidAnnotation`; the non-editable members of the stored runtime type (keys, names, relation arrays, stamps) ignored, any other member `400 InvalidAnnotation` ("unknown member"); `null` clears; `Values` merged recursively per RFC 7396 with ordinal names (`null` removes a name, so a merge patch cannot set a value to JSON `null`); every other member, `Labels` included, replaced whole.
- JSON Patch: ordinal paths over the editable document; a failing `test` (also a missing path) → `412 PatchTestFailed`; any other failing operation → `400 PatchInvalid`; a member outside the document afterwards → `400 InvalidAnnotation`.
- After either: type checks per member (`400 InvalidAnnotation`, naming it); a unit's `UnitType`/`UnitDefinition` cannot become null or be removed; labels keep their first occurrence, compared **ordinally** (`"eu"` and `"EU"` both stay); the length and link rules of a full write. A patch that changes nothing writes nothing (no audit entry, same `ETag`) and answers `200` with the stored annotation. A disabled, scheduled or expired annotation can be patched. `IsDisabled` `false` and `null` are the same.
- The SDK: generated `patchAnnotation` (merge patch only, typed `AnnotationMergePatch`) and `patchAnnotationVersioned` (`lib/annotations.ts`: an array as `application/json-patch+json`, an object as `application/merge-patch+json` with its explicit nulls; through the configured fetcher). `hasErrorCode(error, 'PatchTestFailed')` works on its errors.
- CORS: a `PATCH` without `If-Match` needs only the preflight to allow `PATCH` and its `Content-Type`, which the deployment does (`docs/Platform/Storyteller/authentication.md`, "CORS for browser clients"). The UI cannot read `ETag` cross-origin, so no `If-Match`.

### Mock API

- `app/mock/annotation-handlers.ts`: GET (l.732-755), POST (l.757-773, `invalidAnnotation` then `storeAnnotation`) and DELETE (l.775-780) of `…/annotations/:key`; no `PATCH`, no `ETag`. `storeAnnotation` (l.175-231) creates missing keys and ancestors, re-creates deleted ones, and for an existing key builds `withRelations({...body, ProjectName, ViewName, Created, Modified: stamp})`, compares with `diffAnnotations` (`audit.ts` l.109: `''`/`false`/`undefined` as null, labels as sets) and writes nothing without a change, else appends an `Updated` audit entry and `put()`s it. `annotationProblems` (l.271) checks the lengths.
- `app/mock/merge.ts` `applyMergePatch`: RFC 7396 with case-insensitive names at every level (made for project settings; the API's `Values` names are ordinal). `fast-json-patch` (`applyPatch`) is used by the configuration and template PATCH handlers (`TEST_OPERATION_FAILED` → `412 PatchTestFailed`, else `400 PatchInvalid`).
- `app/mock/project-settings-handlers.ts` l.96-99, 187-206: the `Content-Type` check, `415` with `Accept-Patch`, `400 PatchInvalid` on a body that is not JSON.
- `app/mock/browser.ts` registers the hand-written handlers before the SDK's Faker handlers, so a hand-written `PATCH` wins.

### Tests

- Unit: `test/unit/annotations/annotation-form.test.ts` (l.85-174, `toAnnotationBody`; l.188-207 mounts the form), `soft-delete.test.ts` (`setAnnotation` for setup and the history test l.278-302, the deleted-key dialog l.245-275), `active-annotations.test.ts` (setup only), `bulk-create.test.ts` (the `useAnnotationActions` harness l.162-186, `recordBatches` l.40-48), `components/annotation-bulk-bar.test.ts` (Delete only), `mock/project-settings-handlers.test.ts` (the raw-`fetch` template for a mock PATCH: `415`, `PatchInvalid`). No test asserts the method or the body of an annotation write, and none covers `save`, `saveMany`, the bulk edits, the form's submit or Restore and update.
- e2e (mocked): `annotations.spec.ts` l.4-38 (title and description saved, kept after a reload), l.326-351 (a unit's type and definition); `deleted.spec.ts` l.78-102 (Restore and update: toast, history "Restored"), l.127-142 (a title save: history "Updated"). No flow filters annotation requests by method; no flow covers the bulk edits.
- Deployed-UI automation (`src/Platform/UI.Admin.AutomationTests`): `PlatformTest.ANN.cs` l.63-71 `IsWriteOf(key)` matches any non-GET (its comment says "SetAnnotation is its POST"); ANN-FRM-01..07, ANN-CAT-02 (bulk disable, enable, labels), ANN-NEW-04 (Restore and update), ANN-DET-05 and ANN-ROL-02 save annotations and must keep passing; none checks the method.

### Documentation

- `docs/Platform/Storyteller/annotation-lifecycle.md` l.203, end of "Creating in clients": "The admin UI still writes the whole body after the restore, merged over the restored record, without a condition."
- `docs/Platform/Storyteller/authentication.md` l.2056: "The admin UI uses neither yet: it writes whole bodies without a condition."
- `src/Platform/ui.admin/README.md` l.95 (the mock keeps deletions and history), the list of mock behaviours.
- `docs/42for.net/platform/admin-ui.md` (the form l.112, the bulk bar l.139, Restore and update l.147) says nothing about how changes are written.

## Proposed Changes

### 1. Mock API: `PATCH …/annotations/{key}` (phase 1)

A pure module `app/mock/annotation-patch.ts` mirrors `AnnotationPatch`/`AnnotationPatchDocument`, and a handler in `annotation-handlers.ts` mirrors the pipeline of `AnnotationsHttp.cs`.

**`annotation-patch.ts`**

```ts
export type AnnotationPatchFormat = 'merge' | 'json-patch'
export const acceptPatch = 'application/merge-patch+json, application/json-patch+json'
/** The format by Content-Type and body (object/array), or 'unsupported' (→ 415) / a PatchInvalid message (→ 400). */
export function patchFormatOf(contentType: string | null, body: unknown): AnnotationPatchFormat | { unsupported: true } | { invalid: string }
/** Up-front checks of a JSON Patch: every operation an object with a known op, a path (a JSON Pointer), value for add/replace/test, from for move/copy. */
export function jsonPatchProblem(operations: unknown[]): string | undefined
/** The editable document of the stored annotation, every member present (Labels [], Values {}; UnitType and UnitDefinition for units). */
export function editableDocumentOf(annotation: Annotation): Record<string, unknown>
/** Applies the patch: the annotation as it would be stored, or the error the API answers (status, code, message). */
export function applyAnnotationPatch(current: Annotation, format: AnnotationPatchFormat, patch: unknown):
  | { annotation: Annotation }
  | { status: 400 | 412; code: 'InvalidAnnotation' | 'PatchInvalid' | 'PatchTestFailed'; message: string }
```

- **The document is a copy** (`Values` cloned, `Labels` a new array of its strings; a stored `null` label is dropped as the API's `OfType<string>` does), so neither format can change the stored annotation, and `diffAnnotations` sees every change.
- **Merge patch**: walk the patch's top-level names; a name that matches another one case-insensitively → `400 InvalidAnnotation` ("The annotation patch has the member '{name}' more than once."); a name that matches an editable member case-insensitively applies to it (`Values`: an ordinal RFC 7396 merge, new function `mergeValues` in the module, a patch object over a non-object starting from `{}`, names set as own properties so a value named `__proto__` works; any other member: replaced, `null` clears); a name of the type's ignored members is skipped; anything else → `400 InvalidAnnotation` ("The annotation patch has an unknown member '{name}'."). The API finds the ignored members by reflection over the stored runtime type; the mock lists them (`Abstractions.Annotations/src/Model/*.cs`), matched case-insensitively:

  | Type | Ignored members |
  | --- | --- |
  | every type | `ProjectName, ViewName, AnnotationKey, Name, AnnotationType, Created, Modified, Deletion` |
  | Responsibility | `UnitNames` |
  | Subject | `ContextNames, ResponsibilityNames` |
  | Unit | `ResponsibilityKey, ResponsibilityName` |
  | Usage | `SubjectKey, ResponsibilityKey, SubjectName, ResponsibilityName, ContextNames` |
  | Context | `SubjectKey, SubjectName, ResponsibilityNames` |
  | Execution | `ResponsibilityKey, SubjectKey, ContextKey, UsageKey, SubjectName, ResponsibilityName, ContextName, UnitNames` |
  | UnitOfExecution | `ResponsibilityKey, UnitKey, SubjectKey, ContextKey, UsageKey, ExecutionKey, ResponsibilityName, UnitName, SubjectName, ContextName` |

  `UnitType`/`UnitDefinition` are editable only on units, so on other types they are unknown.
- **JSON Patch**: the operations one at a time (`applyOperation` of `fast-json-patch`, validating against the document as it is then), so a failure names its own index (the library's validator always reports index 0); a failing `test` operation (a value that differs, or a path that does not resolve) → `412 PatchTestFailed` ("JSON Patch operation {i} failed: …"); any other failure, also a thrown `TypeError` (a `__proto__` segment) → `400 PatchInvalid`; a `move` into its own child → `400 PatchInvalid` (the library would apply it); a result that is not an object → `400 PatchInvalid`; a member outside the document (ordinal) → `400 InvalidAnnotation` ("The patched annotation has an unknown member '{name}'.").
- **Checks of the result** (`400 InvalidAnnotation`, naming the member): `IsDisabled` boolean or null; `ValidFrom`/`ExpiresAt` an ISO 8601 date or date and time (an optional offset) or null; the four strings string or null; `Labels` an array of strings (an item that is not a string: "…the item {i} is not a string.") or null; `Values` an object or null; a unit's `UnitType`/`UnitDefinition` a string, not null or removed unless stored null.
- **Normalization**: labels keep their first occurrence, ordinal; the members of the document go back onto the stored annotation, a `null` or removed member left out; `Labels`/`Values` stay `[]`/`{}` when the document has them (as the API's real write).
- **Known differences** (the UI never sends them): an exact duplicate member (`{"Title":"a","Title":"b"}`, the API's `400 PatchInvalid`; `JSON.parse` keeps the last), an array index with a leading zero (`/Labels/01`), and `If-Match` (the mock has no ETags).

**The handler** (after the single-key `POST`):

1. `requireRole(…, 'Contributor')` (`403 AccessDenied`).
2. `isValidAnnotationKey(params.key)`, else `400 InvalidKey`.
3. Any `If-None-Match` → `400 InvalidPrecondition` ("A patch never creates an annotation, …"). `If-Match` is not modelled (the mock has no ETags, and the UI sends none).
4. `Content-Type` → `415 InvalidContentType` with the `Accept-Patch` header for any type other than `application/merge-patch+json`, `application/json-patch+json`, `application/json` or none (compared case-insensitively, parameters ignored).
5. The body as text, `JSON.parse` → `400 PatchInvalid` ("The body is not a patch of an annotation: …") when it is not JSON, empty, or does not fit the format, or (JSON Patch) `jsonPatchProblem` finds a malformed operation.
6. A deleted key (`trash`) → `404 AnnotationDeleted`, a missing one → `404 AnnotationNotFound`, with the API's messages and hints. Inactive annotations are patched like active ones.
7. `applyAnnotationPatch`; its error → `errorResponse(status, message, code)`.
8. `annotationProblems` of the result → `400 InvalidAnnotation` with the message of a full write. It gains the API's link rule ("DocumentationLink must be an absolute http or https URL.", `AnnotationRules.GetProblems`), for `POST` too.
9. The write: the existing-key branch of `storeAnnotation` becomes `updateAnnotation(db, params, previous, next)` (relations derived, `Created` kept, `Modified` stamped, `diffAnnotations`, no change → the stored annotation unchanged and nothing written, else an `Updated` audit entry and `put`); `storeAnnotation` calls it for an existing key, so `POST` behaves as before. A `PATCH` never creates ancestors or re-creates keys.
10. `200` with the stored annotation.

**Concurrent writes.** The bulk actions send four patches at once. `withDb` loads the stored database, runs the handler and saves it; a handler that awaits (its body) in between lets another request load the same old state, and the later save overwrites the earlier write. So the handler reads its body before `withDb`, and `withDb` no longer awaits a synchronous handler's answer: the database part of a `PATCH` runs without a yield.

### 2. The PATCH body of the edit form (`app/forms/annotation-patch.ts`, phase 2)

A new module next to the schema, pure and unit-tested:

```ts
/** The editable members of a draft, normalized as the API stores them: trimmed strings or null, IsDisabled true or null, … */
export function editableOf(draft: AnnotationForm): EditableAnnotation
/** The patch of the members the user changed from `base` to `draft`, or undefined when nothing changed. */
export function toAnnotationPatch(draft: AnnotationForm, base: AnnotationForm): AnnotationPatchBody | undefined
```

- Both sides go through the same normalization as `toAnnotationBody` today: `IsDisabled` → `true` or `null`; `ValidFrom`/`ExpiresAt` → the string or `null`, compared as instants (`Date.parse`), so a re-formatted equal instant is no change; `TimeZone`, `Title`, `Description`, `DocumentationLink` → trimmed, `''` → `null`; `Labels` → its strings, trimmed on both sides (the schema trims the draft's, and the API never trims, so a stored `" eu"` would otherwise count as a change on every save), compared in order; `Values` → the record; a unit's `UnitType`/`UnitDefinition` → trimmed strings (only for units, so other types never send them).
- **A merge patch** (the default): every changed member with its new value, a cleared one as `null`; `Labels` whole (`null` when emptied); `Values` as a recursive diff: a changed or added name with its value, a removed name as `null`, an object (non-null, not an array) over an object as its own nested diff (so a concurrent change of another name, or of another member of the same nested object, is kept); arrays and values that change type are replaced whole; names set as own properties (`__proto__`), and a name the base does not own is new.
- **A JSON Patch when a merge patch cannot say it**: setting a JSON `null` (a value of type null in the values editor, or a null member inside an object value that the merge would drop). Then the same changes as operations over the editable document: `replace /<Member>` for each changed member (every member exists, `null` unsets), `replace /Labels` with the list, and per changed top-level value name `add /Values/<name>` with the whole new value, per removed name `add /Values/<name>` `null` then `remove /Values/<name>` (so a name removed meanwhile does not fail the patch); names escaped per RFC 6901 (`~` → `~0`, `/` → `~1`). This fallback works per top-level value name: a concurrent change inside the same nested value is overwritten (see Out of scope).
- Members outside the editable document, which the JSON/YAML modes can add to the draft, are not sent (as today: `toAnnotationBody` copied only named members).
- `toAnnotationBody` is removed; its tests move to the patch builder.

### 3. `useAnnotationActions` (phase 2)

- `patch(key, body)` replaces `save(annotation)`: `patchAnnotationVersioned(org, project, view, key, body)` (the `ETag` is ignored), then `catalog.upsert(value)` and `invalidateAnnotation`; returns the stored annotation.
- `patchMany(annotations, write)` replaces `saveMany`: the same four lanes, `progress`, the collected failures and the error `"N of M annotations were not saved: …"`, but each key runs `write(annotation)`, which returns the stored annotation (upserted and invalidated) or `undefined` when there was nothing to write. A key deleted or purged meanwhile (`404 AnnotationDeleted`/`AnnotationNotFound`) is not re-created: it counts among the failures and leaves the catalog (`catalog.remove`, `forgetAnnotation`, then `invalidateDeleted`), as a delete would have taken it, so the row does not stay and fail again.
- `setDisabled(keys, disabled)`: a merge patch `{ IsDisabled: true }` or `{ IsDisabled: null }` per selected key (the API treats `null` and `false` alike, and the form writes `null`).
- **The label actions** are JSON Patches that start with `{ op: 'test', path: '/Labels', value: <the labels read> }` (their strings, as the API's document has them). The catalog entry can be stale (a snapshot, a list load), and the API dedupes ordinally only: without the guard, adding `eu` where someone added `EU` meanwhile would store both spellings, and the form would then refuse to save that annotation ("Labels must be unique."); a `remove` at an index that moved would take another label. A guard that fails answers `412 PatchTestFailed`: the key is read again (`getAnnotation`, `includeInactive`), the patch rebuilt from the fresh labels and sent again, up to three attempts; a fresh copy that needs no change is upserted and counts as done. Helpers in `app/utils/labels.ts`: `patchableLabels`, `labelAddition`, `labelRemoval`, `sameLabel`.
- `addLabel(keys, label)`: the label trimmed (an empty one writes nothing); a key that has the label in any spelling is skipped (the UI keeps labels unique case-insensitively); the others get `[test /Labels, { op: 'add', path: '/Labels/-', value: label }]`.
- `removeLabel(keys, label)`: for a key that has the label in any spelling, `[test /Labels, remove /Labels/<i> …]`, one remove per occurrence, highest index first; keys without it are skipped.

### 4. The edit form (`AnnotationForm.vue`, phase 2)

- A `base` (a `shallowRef` of the annotation) holds what the form was last reset to (mount, the watch's reset, Revert, a save): `reset(annotation)` sets both, replacing the three `resetForm` calls.
- `reset` calls `resetForm({ values }, { force: true })`. Without `force`, VeeValidate 4 merges the new values into the old ones (`merge` recurses into objects): a `Values` name the new copy no longer has (removed by this save, or by someone else before a refetch) would stay in the form while `base` lacks it, and the next save would write it back.
- The JSON/YAML modes set every member whole (`setFieldValue` per member, a member the text leaves out taking its unset value from `toAnnotationDraft`), instead of `setValues`, which merges the same way: a value name or a line deleted in the text would otherwise stay in the form and never reach the patch. Anything but an object changes nothing (the editor reports it).
- `save`: `toAnnotationPatch(draft, toAnnotationDraft(base))`; `undefined` (nothing changed after normalization, e.g. only whitespace) resets the form to the latest copy (`props.annotation`) and toasts "Nothing to save"; otherwise `actions.patch(key, patch)`, then `reset(saved)` (the merged result: the user's changes and anyone else's) and "Annotation saved". The error handling stays (`InvalidAnnotation` messages to their fields); a `404 AnnotationDeleted`/`AnnotationNotFound` shows its message in the error toast.
- The component comment says that saving sends only the changed members.

### 5. Restore and update (`AnnotationCreateDialog.vue`, phase 2)

- When the key itself was restored: a merge patch of what the dialog changes, `Title` when the trimmed title is set and differs from the restored one, `TimeZone` when the restored annotation has none and the dialog has one; an empty patch writes nothing and the restored annotation is the result. `actions.patch` upserts and invalidates the key.
- When the patch fails after the restore: the key stays restored (and `restoreMany` already put it into the catalog), so the dialog runs the same cleanup as a success (closes, clears its name and title, navigates to the key with `emit('created')`, which the parents only route) and shows a warning toast "{key} restored, but not updated" with the error (`writeFailureTitle` for a timeout, when the change may have been saved), instead of an alert in a dialog whose key now exists.
- A plain create, **Create new**, and a restore where only ancestors were deleted keep the `POST` (`setAnnotation`), the only way to create or re-create a key.

### 6. Tests (phases 1 and 2)

- `test/unit/mock/annotation-patch-handlers.test.ts` (raw `fetch`, `onUnhandledRequest: 'error'`): the content types (`415` with `Accept-Patch`, `application/json` object/array), `400 PatchInvalid` (not JSON, wrong shape, malformed operation), `400 InvalidPrecondition` for `If-None-Match`, `403` for a Reader, `404 AnnotationNotFound`/`AnnotationDeleted`, merge semantics (case-insensitive names, `null` clears, recursive ordinal `Values`, ignored members, unknown and duplicate members, units), JSON Patch semantics (`add /Labels/-` and ordinal dedupe, `test` → `412` also on a missing path, `remove` of a missing name → `400`, the index of the failing operation, a move into itself, a member outside the document), the checks of the result (an ISO date, the link rule), the stored annotation unchanged by `applyAnnotationPatch`, no write and no audit entry without a change, an `Updated` entry with only the changed members (also for a merge of `Values` alone), an inactive annotation patched, a deleted one not re-created, concurrent patches all kept.
- `test/unit/annotations/annotation-patch.test.ts`: `toAnnotationPatch` (cleared members as `null`, unchanged members left out, dates as instants, trimming of both sides' labels, labels, the recursive `Values` diff, an array that shrinks, an object replaced by an array and back, a value named `__proto__`, the JSON Patch fallback for nulls with escaping and the guarded removal, units only for units, `undefined` without a change), and the label helpers. `annotation-form.test.ts` turns its `toAnnotationBody` tests into patch tests.
- `test/unit/annotations/annotation-actions.test.ts` (MSW, recorded requests): `setDisabled`, `addLabel` and `removeLabel` send their patches and keep a concurrent change of another member and of another label made in the mock after the catalog loaded; `addLabel` skips a case variant and adds no second spelling added meanwhile; `removeLabel` recovers from a moved index (`412` → re-read → retry); a deleted key fails, stays deleted and leaves the catalog.
- `test/unit/annotations/annotation-form-save.test.ts` (the form against the mock, recorded requests): a title save sends `{ Title }` only and keeps a value written meanwhile; an emptied member is `null`; a whitespace-only edit sends nothing; a removed value name does not come back with the next save; a fresh copy while clean becomes the base; a fresh copy while dirty does not; Revert after a refetch; a `null` value as a JSON Patch; a value name and a member removed in the JSON mode.
- `test/unit/annotations/create-dialog-restore.test.ts`: Restore and update restores the key with its content and patches only the title (history `Updated`, `Restored`, `Deleted`); Create new stays a `POST`.
- e2e: the existing flows (`annotations.spec.ts`, `deleted.spec.ts`) run on the mock's `PATCH`; a new desktop-only flow in `annotations.spec.ts` for the bulk bar (`AnnotationList` below 768 px has no selection): filter to two subjects (the grid is virtualized), select them (`Select <key>`), disable and enable, add a new label made with create-item (the mock's labels are random) and remove it, and check that no `POST` to `…/annotations/<key>` was sent (`page.on('request')`).

### 7. Documentation (phase 2)

- `annotation-lifecycle.md` "Creating in clients": the admin UI restores, then patches only what the dialog sets; and a short note under "Merge writes (PATCH)" or "Creating in clients" that the admin UI writes every change of an existing annotation as a `PATCH` (form: merge patch of the changed members, JSON Patch when a value is set to `null`; bulk bar: `IsDisabled`, `add /Labels/-`, guarded `remove`).
- `authentication.md` l.2056: the admin UI now sends merge and JSON patches without a condition, so it needs only the preflight's `PATCH` and `Content-Type`.
- `src/Platform/ui.admin/README.md`: the mock's annotation `PATCH`.
- `docs/42for.net/platform/admin-ui.md`: saving the form or a bulk edit sends only the change, so concurrent edits of other members are kept.
- `UI.Admin.AutomationTests/src/PlatformTest.ANN.cs` l.63: the comment of `IsWriteOf` names `POST` and `PATCH`.
- Code comments in `useAnnotationActions.ts`, `AnnotationForm.vue` and `AnnotationCreateDialog.vue`.

### 8. Verification

`npm run lint`, `npm run typecheck`, `npm test` and `npm run test:e2e` (or at least `annotations.spec.ts` and `deleted.spec.ts`) in `src/Platform/ui.admin/src`; `dotnet build` of `UI.Admin.AutomationTests` (comment only).

### Differences from the issue

- **Enable and remove label** are not in the issue's list. Enable is the merge patch `{ IsDisabled: null }`; remove label is a JSON Patch guarded by `test`, re-read and retried on `412`.
- **Add label** is guarded by a `test` of the labels and skips a key whose label exists in another case, because the API dedupes ordinally and would keep both spellings.
- **The mock's `withDb`** changes too: concurrent writes overwrote each other (a pre-existing race that the bulk actions' four lanes hit).
- The plan was reviewed by two independent critics (API fidelity; UI and tests) before implementation; their findings are part of sections 1-6 above (the forced reset, the text modes, the label guard, the mock's copy and per-operation indexes, the tests).
- **The form** falls back to a JSON Patch when a merge patch cannot express the change (a `null` value).

### Out of scope

- `If-Match` on full-form saves (the browser cannot read `ETag` across origins; needs API Management or a same-origin proxy).
- `Labels` in the form is replaced whole (a merge patch replaces arrays): when two people change the labels of one annotation at the same time, the later save decides the labels. The bulk bar's label actions do not have this limit.
- The form's JSON Patch fallback (a `null` value) replaces a changed top-level value whole, so a concurrent change inside that same nested value is overwritten; nested operations would answer `400 PatchInvalid` when the parent stopped being an object meanwhile.
- `If-None-Match: *` on the dialog's create (`POST`): a separate change (it needs `If-None-Match` in the CORS allow-list).
