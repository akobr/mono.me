# Admin UI Writes Annotation Changes with PATCH

Linear: [424-225 Admin UI: write annotation changes with PATCH instead of whole bodies](https://linear.app/424fun/issue/424-225/admin-ui-write-annotation-changes-with-patch-instead-of-whole-bodies)

## Overview

This review covers the whole of [Admin UI Writes Annotation Changes with PATCH](../2026-11-22%20Admin%20UI%20Writes%20Annotation%20Changes%20with%20PATCH.md), delivered in two phases on `feat/424-225`. Phase 1 gives the mock API the annotation `PATCH` of the API (both formats, the API's pipeline and errors). Phase 2 moves every change of an existing annotation in the admin UI to a `PATCH` of only that change. That covers the edit form (merge patch, JSON Patch for a `null` value), Enable and Disable, labels added and removed with a guarded JSON Patch, and Restore and update. A change someone else made meanwhile is kept, and a key deleted meanwhile is not re-created. The code is reviewed afterwards; its findings are fixed in four commits.

| Commit | Subject |
| --- | --- |
| `b794a413d` | docs(platform): plan the admin UI's annotation writes as PATCH of only the change (424-225) |
| `baeaeeb36` | feat(platform): answer the PATCH of an annotation in the admin UI's mock API as the API does (424-225) |
| `7393f1fa5` | feat(platform): write annotation changes from the admin UI as a PATCH of only the change (424-225) |
| `66a1f3933` | test(platform): check that the admin UI's bulk bar writes its edits as patches in the e2e flows (424-225) |
| `30e99a5d2` | docs(platform): describe the admin UI's annotation writes as patches of only the change (424-225) |
| `19df34119` | fix(platform): keep the admin UI's bulk edits in the view they started in, and check a stale label copy on the server (424-225) |
| `20ac10bea` | fix(platform): diff the admin UI form's own values, and drop a member deleted in its JSON mode (424-225) |
| `48b6f26b3` | test(platform): check the bodies of the bulk bar's patches and keep a concurrent title in the e2e flow (424-225) |
| `311b9d71e` | docs(platform): correct how the admin UI's annotation patches fall back, retry and use If-Match (424-225) |

## What Was Done

Paths are relative to `src/Platform/ui.admin/src/` unless they start with `docs/` or `src/`.

### 1. Mock API: `PATCH …/annotations/{key}`

As planned:

- **`app/mock/annotation-patch.ts`** is new and pure. It exports:
  - `patchFormatOf` and `isPatchMediaType` (the media type, case-insensitive, parameters ignored; `application/json` or none decides by the body);
  - `jsonPatchProblem` (the six RFC 6902 operations, then `fast-json-patch`'s `validate` without a document for the shapes);
  - `editableDocumentOf` (a copy: `Values` cloned, `Labels` its strings);
  - `mergeValues` (ordinal RFC 7396, names set as own properties);
  - `applyAnnotationPatch`.
- **Merge patch.** Top-level names match case-insensitively. A name given twice in two cases answers `400 InvalidAnnotation`. The members of the stored type that the API ignores are listed per type code (the table of the spec) and skipped. Any other member is unknown (`400 InvalidAnnotation`), and so are `UnitType`/`UnitDefinition` on a type that is not a unit.
- **JSON Patch.** The operations run one at a time (`applyOperation`, validated against the document as it is then). So the message names the failing operation (`JSON Patch operation 2 failed: …`).
  - A failing `test`, also one whose path does not resolve, answers `412 PatchTestFailed`.
  - Any other failure answers `400 PatchInvalid`, a thrown `TypeError` included.
  - A `move` into its own child also answers `400 PatchInvalid`.
- **Checks of the result.** The type checks of `AnnotationPatchDocument` name the member. A date must be ISO 8601: a pattern for a date, or a date and time with an optional offset, before `Date.parse`. A unit's trigger cannot become `null` unless it was stored `null`. Labels keep their first occurrence, ordinally.
- **The handler** in `annotation-handlers.ts` follows the API's order:
  1. the role;
  2. the key (`400 InvalidKey`);
  3. any `If-None-Match` (`400 InvalidPrecondition`);
  4. the media type (`415 InvalidContentType` with `Accept-Patch`);
  5. the body (`400 PatchInvalid`);
  6. deleted, then missing (`404 AnnotationDeleted`, `404 AnnotationNotFound`, with the API's hints);
  7. the patch;
  8. `annotationProblems`;
  9. the write.
- **The write.** The existing-key branch of `storeAnnotation` became `updateAnnotation` (relations, `Created` kept, `Modified` stamped, `diffAnnotations`, no write without a change). `storeAnnotation` calls it, so `POST` behaves as before.
- **The link rule.** `annotationProblems` gained the API's rule ("DocumentationLink must be an absolute http or https URL."), for `POST` and the view import too.
- **Concurrency.** The handler reads its body before `withDb`. `withDb` no longer awaits a synchronous handler's answer (`app/mock/handlers.ts`). Before, four concurrent writes loaded the same stored state and the last save won. The first run of the bulk tests showed this: a disable of two keys kept one.
- **Not modelled** (the README says so): `If-Match`, an exact duplicate member (`JSON.parse` keeps the last), an index with a leading zero.

### 2. The PATCH body of the edit form

As planned, in `app/forms/annotation-patch.ts` (`editableOf`, `sameValue`, `toAnnotationPatch`):

- **Normalization.** Both sides are normalized alike: trimmed strings or `null`, `IsDisabled` `true` or `null`, dates compared as instants, labels as their trimmed strings, and the unit trigger for units only.
- **The merge patch** holds the changed members, a cleared one as `null`, `Labels` whole (`null` when emptied), and `Values` as a recursive diff:
  - an object (not an array, not `null`) over an object becomes its own nested diff;
  - an array, or a value that changes type, is replaced whole;
  - a name the base does not own is new;
  - names are set with `Object.defineProperty`, so a value named `__proto__` works.
- **The JSON Patch fallback** is used when the change sets a JSON `null`: `replace` per member, `replace /Labels`, `add /Values/<name>` per changed name, and `add` `null` then `remove` per removed name. Names are escaped per RFC 6901.
- `toAnnotationBody` is gone. Its tests in `annotation-form.test.ts` became patch tests.

### 3. `useAnnotationActions`

- **`patch(key, body, target)`** replaces `save`. It calls `patchAnnotationVersioned` and ignores the `ETag`.
- **`patchMany(annotations, write)`** replaces `saveMany`.
- **`setDisabled`** sends `{ IsDisabled: true | null }`.
- **The label actions** go through `patchLabels`. The helpers in `app/utils/labels.ts` are `patchableLabels`, `labelGuard`, `labelAddition`, `labelRemoval` and `sameLabel`. Every patch starts with `test /Labels` (the labels read). On `412 PatchTestFailed` the key is read again (`getAnnotation`, `includeInactive`) and the patch is rebuilt, three attempts at most.
- **Deviation, the review's high finding (`19df34119`).** The view of a run is taken once, by `patchMany`, and passed to every write (`patch(…, target)`, `patchLabels(…, target)`). Before the fix, each write read the current view: a user who opened another view during a long run sent the rest of the patches there. A regression test switches the view on the first `PATCH`. The catalog takes results, and loses gone keys, only while it holds that view (`catalogHolds`, as `createMany` does).
- **Deviation from the plan, which skipped them.** A key whose catalog copy needs no label change (it has the label, for an add; it lacks it, for a remove) now gets the guard alone. The catalog copy may be stale. A guard that holds writes nothing. A guard that fails leads to the re-read, and the fresh labels decide. So an add where someone removed the label meanwhile still adds it.
- **A key gone meanwhile.** A key deleted or purged meanwhile counts as not saved and leaves the catalog (`catalog.remove`, `forgetAnnotation`, `invalidateDeleted`). This is decided by `isNotFound`, not by the two error codes, because the re-read of a key deleted after a `412` answers the API's bare `404`.

### 4. The edit form

As planned, with what the plan's review added:

- **The base.** `base` is a `shallowRef` of the annotation. `reset(annotation)` sets it and calls `resetForm(…, { force: true })`. A test fails without `force`: a removed value name came back with the next save.
- **The JSON and YAML modes** call `setDocument`. It sets each member whole (`setFieldValue`). A member of the annotation that the text leaves out takes its unset value. Any other member that the text no longer has is set to `undefined`, which VeeValidate's dirty check and the editor's JSON leave out (review fix `20ac10bea`).
- **Deviation (`20ac10bea`): the save diffs the form's own values.** It uses `JSON.parse(JSON.stringify(values))`, not the schema's output. Zod's `z.record` leaves out a key named `__proto__`, so diffing its output against the loaded copy removed such a value on every save. The submit still validates first, and both sides go through `editableOf`, so the schema's trims make no difference.
- **"Nothing to save"** resets to the latest copy (`props.annotation`).

### 5. Restore and update

As planned:

- `updateRestored` builds a merge patch of the title, when it is given and differs, and of the time zone, when the restored record has none. An empty patch writes nothing.
- A failure after the restore shows the warning "{key} restored, but not updated" (through `writeFailureTitle`), then runs the same `finish` as a success.
- Create, Create new, and a restore of ancestors only stay a `POST`.

### 6. Tests

New:

- `test/unit/mock/annotation-patch-handlers.test.ts` (29 tests);
- `test/unit/annotations/annotation-patch.test.ts` (19 tests: the patch builder and the label helpers);
- `annotation-actions.test.ts` (12 tests, the bulk edits: concurrent changes kept, guarded labels, stale and moved labels, deleted keys, a view switch mid-run);
- `annotation-form-save.test.ts` (11 tests: the form against the mock, including the base after a save, a refetch and Revert, a `null` value, the JSON mode, `__proto__`);
- `create-dialog-restore.test.ts` (2 tests: Restore and update patches only the title; Create new stays a `POST`).

Changed:

- `annotation-form.test.ts`.
- The e2e flow at the end of `test/e2e/annotations.spec.ts` is desktop-only, because the phone list has no selection. It covers Disable, Enable, adding a new label through create-item, and removing it. It checks the method, the media type and the body of every write, and that a title written elsewhere after the list loaded survives all four edits.

Three tests were checked against the code they protect by reverting the fix: the forced reset, the view of a run, and `__proto__`. Each fails without its fix.

### 7. Documentation

As planned:

- `docs/Platform/Storyteller/annotation-lifecycle.md`: the end of "Creating in clients", and a paragraph "In the admin UI" under "Merge writes (PATCH)".
- `docs/Platform/Storyteller/authentication.md`: the admin UI's annotation writes need only `PATCH` and the two media types. Its project settings saves still send `If-Match`, which a review finding added.
- `src/Platform/ui.admin/README.md`: the mock's `PATCH`.
- `docs/42for.net/platform/admin-ui.md`: the form, the bulk actions and Restore and update.
- The comment of `IsWriteOf` in `src/Platform/UI.Admin.AutomationTests/src/PlatformTest.ANN.cs`.

The review corrected three statements (`311b9d71e`): the fallback, the attempts, and `If-Match`.

### Test results

- `npm run typecheck` passes. `npm run lint` reports one warning, the existing `no-shadow` in `app/mock/key-value-handlers.ts`.
- `npm test`: 131 files, 2522 tests passed.
- `npm run test:e2e`, before the review fixes: 461 passed, 15 skipped (one form factor by design). After the fixes, `annotations.spec.ts` and `deleted.spec.ts`: 36 passed, 4 skipped.
- `dotnet build src/Platform/UI.Admin.AutomationTests/src` passes, with no new warning. The deployed-UI automation tests were not run, because they need a target. ANN-FRM-01..07, ANN-CAT-02, ANN-NEW-04, ANN-DET-05 and ANN-ROL-02 do not check the method (`IsWriteOf` matches any write) and should pass unchanged.

### Reviews

- **The plan** was reviewed before implementation by two independent critics, one on API fidelity and one on the UI and the tests. Their findings are part of the spec (sections 1-6).
- **The code** was reviewed on four dimensions, each finding then checked by a skeptic. Fixed:
  - (high) a bulk run wrote to the view open at each write;
  - (low) a stale catalog copy skipped a label action without a check;
  - (low) a key deleted between a `412` and the re-read stayed in the list;
  - (low) a value named `__proto__` was removed by every form save;
  - (low) a member typed in the JSON mode stayed after its line was deleted;
  - (low) the e2e flow checked only the method;
  - (low) three doc statements.
- **Not completed.** The reviewer of the mock dimension, and the skeptics of three form findings, did not run: the review hit a usage limit. The mock was then checked by hand: `storeAnnotation` stamps for new, existing and re-created keys; `withDb` with async handlers; the link rule against the seeds, the showcase links and the import.

### Deviations from the spec

- Section 3: a key whose catalog copy needs no label change is checked with the guard alone, not skipped. A gone key is told by `isNotFound`. A run passes its view to every write.
- Section 4: the save diffs the form's values instead of the schema's output, and `setDocument` also drops members the annotation does not have.

### Left for later

- In the form, `Labels` is replaced whole. When two people change the labels of one annotation at the same time, the later save decides them. The bulk bar's label actions do not have this limit.
- The JSON Patch fallback sets each changed value name whole. A concurrent change inside that same value is not kept.
- In the JSON mode, a line that is cut comes back with its unset value (the editor re-serializes the form). A pasted copy of it above that one then has a duplicate key, and `JSON.parse` keeps the last. The YAML mode refuses duplicate keys. The JSON parser could do the same.
- `If-Match` on full-form saves needs an exposed `ETag` (API Management or a same-origin proxy). `If-None-Match: *` on the dialog's create needs the header in the CORS allow-list.
