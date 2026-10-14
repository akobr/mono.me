# Bulk Creation of Annotations from a List of Keys

Linear: [424-33](https://linear.app/424fun/issue/424-33/allow-bulk-creation-of-annotations-in-admin-ui)

## Overview

This review covers the whole spec [2026-10-14 Bulk Creation of Annotations from a List of Keys](../2026-10-14%20Bulk%20Creation%20of%20Annotations%20from%20a%20List%20of%20Keys.md), delivered in one phase. The annotations page has a **Bulk create** button, and the command palette a **Bulk create annotations** action. The dialog explains how the lines are read, takes keys or compact lines typed or pasted one per line, and lists what each line becomes before anything is sent: **New**, **Exists**, **Duplicate** or **Invalid**, with the number of missing ancestors. **Create** sends the canonical keys to `POST …/annotations/simple`, 100 per request; the API creates the missing ancestors and skips the existing keys. The result says what was created, from the list and as ancestors, and what was skipped. The API, the OpenAPI document and the SDK did not change.

## What Was Done

### 1. Reading the lines (`app/utils/bulk-keys.ts`, new)

- `readBulkLines`, `readBulkLine` and `describeBulkProblem`, with the `BulkLine` and `BulkLineProblem` types of the spec (`code` included).
- The rules follow the spec: parts are trimmed, an empty part is `empty-part`, a type code with the wrong number of parts is `wrong-parts`, more than four parts without a code is `too-many-parts`, and a control character (the tab of a spreadsheet paste first) or one of `/ \ ? #` in a name is `invalid-name`. The code is written in lower case.
- A type code is recognized only when the first part has three characters, because `getAnnotationTypeInfo` also matches type names (`Subject.x` is the usage `usg.Subject.x`, as in the API).
- The messages name the parts of a key with the type names of the project settings, for example "An Execution key has 4 parts: exe.subject.responsibility.context". The spec gives only that one and the tab message; the others are new wording.
- **Addition:** `withArticle(label)` picks "a" or "an" for a type name ("an Execution", "a Usage", "a Unit"), so the explanation and the messages still read well when the project renames a type.

### 2. Batched create (`useAnnotationActions().createMany`)

- `BulkCreateOutcome` and `simpleBatchSize = 100` as in the spec. The keys are deduplicated, sorted by merge order and sent one batch after another; `progress` counts the keys sent and goes back to `undefined` at the end.
- After the run, also after a failure: `invalidateDeleted`, `invalidateAnnotation` for every created key and for every key of a failed batch with its ancestors, then a forced catalog reload and `refreshConfigurations()` in the background.
- **Addition (review finding):** a run of ten requests takes a while, and the user may switch views meanwhile. `createMany` upserts and reloads only while the catalog still holds the run's view (`sameView`, now exported from `app/utils/context-paths.ts` and reused by the page-memory guard), so the catalog of the other view is left alone.

### 3. The dialog (`app/components/annotations/AnnotationBulkCreateDialog.vue`, new)

- Explanation, input, review, buttons, create, result and failure as in the spec. The type names in the explanation come from `typeMeta`.
- The review table lists the lines in order, at most 200, and every invalid line beyond them, then "and *N* more lines". Statuses are `UBadge`s (New success, Exists neutral, Duplicate warning, Invalid error), so the colour is not the only signal. The typed text follows the key when it differs (`EXE.…`, a compact line).
- A legacy key with an upper-case code shows as **Exists** when the typed text is in the catalog.
- `create()` reads the text as it is when **Create** or `Ctrl`/`⌘` + `Enter` is used, not the debounced review, so a paste followed at once by `Ctrl` + `Enter` creates what is typed. An invalid line still blocks it.
- The snapshot of the review is shown while the run goes, so the rows and the **Create *N*** label do not change as the catalog takes the new keys. The modal cannot be dismissed while it creates.
- Restore: `DeletedKeysDialog` has the new `restoreLabel` prop, and the bulk dialog passes "Restore". Restored keys are taken out of the keys sent; ancestors that the restore had to create count as created missing ancestors. A failed restore shows the failed keys and sends nothing. When the user moves to another view while the deleted-key question waits, the run stops with "You moved to another view, so the keys were not created in …".
- **Deviations:**
  - The result counts the **Duplicate** lines on their own: "2 repeated lines were left out." The spec folded them into "already existed and was skipped", which is not true of a repeated line.
  - Other wording the spec left open: "Created *N* annotations." when no ancestor was created, "No annotations were created." when nothing was, and no "At least 0 annotations were created" sentence when the first batch fails.
  - The result table has a second column, **Created as** (From the list / Missing ancestor).
  - Above 1,000 lines the field shows "At most 1,000 keys at once; the list has *N*." in place of the help text.
  - The catalog note is a warning `UAlert`, shown with the review.
  - The failure alert sits above the input, not below the review, so it is in view on a phone; it has `role="alert"` and is scrolled into view.
- **Accessibility (found by the axe test on a phone):** the review and result tables scroll inside `max-h-72` containers, which axe reported as `scrollable-region-focusable`. Both containers are named regions (`role="region"`, "Review" and "Created annotations") with `tabindex="0"` and a focus ring. `KeyValueImportDialog` has the same unfocusable container; it is not changed here.
- **Focus:** the textarea has `autofocus`; after a result **Done** takes the focus and is described by the result; after a failure, a **Cancel** in the deleted-key question or **Remove invalid lines** the focus goes back to the textarea; **Create more** focuses it too. The summary is a polite live region that is mounted before the first paste.

### 4. Opening the dialog

- `useAnnotationBulkCreate` (module-level `ref`), the command palette action **Bulk create annotations** after **New annotation**, the empty-state button with the new description, and the dialog mounted next to `AnnotationCreateDialog`, all only with `canEdit`.
- **Deviation:** the navbar button is hidden below `sm`, as specified, and shows its label only from `lg`; between `sm` and `lg` it is an icon button with the tooltip "Bulk create annotations". With the label from `sm`, the right group of the navbar ran over the breadcrumb at 640–1023 px, where the sidebar is collapsed. The accessible name stays "Bulk create".

### 5. Mock API (`app/mock/simple-lines.ts`, `app/mock/annotation-handlers.ts`)

- `keyOfSimpleLine` reads an entry exactly as `CreateAnnotationsFromStringAsync`, kept in the mock so production builds do not contain it.
- The handler `POST …/annotations/simple` is registered before `…/annotations/:key`. It checks the Contributor role, answers `500` when the body is not an array (with the .NET null-reference message) and `400` above 100 entries, skips entries it cannot read, sorts the keys by type, and skips keys already handled or active. For each key it re-reads `annotationsOf`, calls `store()` with the key's own members, and answers the missing ancestors (ordered by depth, type and key, so roots first) and then the key. A shared ancestor is answered once.

### 6. Tests

- **Unit** (`test/unit/utils/bulk-keys.test.ts`, 11 tests): the reading rules, the problems, the messages with renamed types, and `withArticle`.
- **Unit** (`test/unit/annotations/bulk-create.test.ts`, 26 tests):
  - `keyOfSimpleLine` against the API's reading;
  - the mock endpoint through `setAnnotationsSimple` (type order, roots first, a shared ancestor once, existing keys skipped, a deleted key re-created, `400`, `500`, `403` for a Reader);
  - `createMany` through a harness (152 keys as 100 + 52 in merge order, `skipped`, a failed batch, a view change);
  - the dialog: the explanation and the review, invalid lines and **Remove invalid lines**, the result with **Create more**, a key of the list that is an ancestor of another, existing and repeated lines in the result, a legacy upper-case key, `Ctrl` + `Enter` without waiting for the review, `Ctrl` + `Enter` while a line is invalid, the label kept during the run, **Restore** (only the other keys are sent), **Cancel**, a view change while the deleted-key question waits, a partial failure with **Try again**, the 1,000-line limit and the 200-row cap with an invalid line beyond it.
  - The fixes made after the code review were checked with temporary mutations of the dialog and `createMany`; every mutation failed a test, except one: the failure count after a restore that had to create an ancestor.
- **End to end** (`test/e2e/annotations.spec.ts`): the flow of the spec with the expected texts, on desktop through the navbar and on a phone through the command palette (`openBulkCreate` in `test/e2e/support.ts`), and a Reader without **Bulk create** in the navbar or the palette.
- **Accessibility** (`test/e2e/accessibility.spec.ts`): axe on the open dialog with New, Exists, Duplicate and Invalid rows.
- **Visual:** the six desktop baselines `annotations-desktop-*`, `annotation-detail-desktop-*` and `annotation-history-desktop-*` were generated again in the Playwright container. The phone baselines did not change, since the button is not shown there. The new navbar button stays within the 0.2 % tolerance of the old baselines, so `--update` alone rewrote nothing; the six files were regenerated with `--update-snapshots=all` and only they were kept.

### 7. Documentation

- `docs/42for.net/platform/admin-ui.md`: a **Bulk create** paragraph after **New** (the lines, the compact lines linked to *Writing the catalog*, the review, the invalid lines, ancestors and existing keys, no title and no time zone, 1,000 keys, the palette on phones), and **Restore** in the deleted-key paragraph.
- `src/Platform/ui.admin/README.md`: the simple endpoint of the mock API, and the bulk create in the end-to-end flows and the axe checks.

### Results

- `npm run typecheck`, `oxlint` and `eslint` report no problems. Prettier reports no problems in the changed files.
- `npm test`: 362 passed (38 files), 37 of them new.
- `npm run test:e2e`: 119 passed and 3 skipped (the existing desktop-only and mobile-only skips), desktop and mobile, with the bundled Chromium.
- `npm run test:visual:docker`: 48 passed against the new baselines. A first run next to the end-to-end suite had one flaky shot (`annotation-detail-desktop-light`, "failed to take two consecutive stable screenshots" under load), which passed on its retry; the run on its own passed without a retry.
- Before the change, `main` failed `npm run typecheck` and 10 unit tests because the installed SDK copy was older than the generated SDK on `main` (the key-value endpoints). `npm ci` and `npm run build` in the SDK, then `npm ci` in the app, fixed that; nothing in the repository changed for it.

### Notes

- The branch starts from `main`. `git merge-tree` merges it without a conflict in its files with `feat/424-10`, `feat/424-14`, `feat/424-15`, `fix/424-16` and `feat/424-30` (`feat/424-14`, `feat/424-15` and `fix/424-16` conflict with `main` already, in the SDK and the backend). `fix/424-32` regenerates the same six desktop baselines for its logo, so whichever branch merges second has to generate them again with `npm run test:visual:docker -- --update`.
- The research found that the review `2026-10-11 Review of the PR 65 findings.md` still says the simple endpoint writes to `main/default`. It writes to the route's project and view since 2026-10-12; that historical review is left as it is.
- The server does not check the names of the lines it creates. The dialog rejects control characters and `/ \ ? #`, but a client that posts to the endpoint directly can still store them. Rejecting them on the server would be a separate Storyteller change.
