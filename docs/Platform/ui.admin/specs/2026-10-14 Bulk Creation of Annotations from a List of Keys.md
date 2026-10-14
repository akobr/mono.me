# Bulk Creation of Annotations from a List of Keys

Linear: [424-33](https://linear.app/424fun/issue/424-33/allow-bulk-creation-of-annotations-in-admin-ui)

## Problem

The admin UI (`src/Platform/ui.admin`) creates annotations one at a time. **New** opens a dialog with a type, the parent names and a name, and creates one key with its missing ancestors.

Building a new project this way is slow. A catalog of a few subjects, responsibilities and their executions has dozens of keys, and every key needs its own dialog. Users who already have the keys in a spreadsheet, a ticket or a script have to go through them one by one, or switch to the `sform` CLI.

The Storyteller API already has a bulk endpoint for this: `POST /v1/{org}/{project}/{view}/annotations/simple`. It takes a list of keys or compact lines, creates them with every missing ancestor, and skips the keys that already exist. The issue asks for a simple screen in the admin UI where a list of keys can be typed or pasted, and which calls that endpoint. The dialog has to explain how the bulk operation works.

## Current State

### The endpoint

- **Handler:** `Api.Functions/src/V1/AnnotationsHttp.cs`, lines 378–409 (`SetAnnotationsSimple`). The body is a `string[]`. It needs the Contributor role on the project and one of the scopes `Annotation.ReadWrite` or `Default.ReadWrite`.
- **Limit:** more than `ApiConstants.MaxInputItems` (100) entries answer `400` with the message "The number of annotations to create is too large, maximum allowed is 100." and no error code. The count is taken on the raw list, blank lines included.
- **Line parser:** `Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs`, lines 184–226 (`CreateAnnotationsFromStringAsync`).
  - A blank line is skipped. A line is split on `.`, and empty segments are dropped. Segments are not trimmed.
  - When the first segment is a type code (any case) and the line parses as a key, the line is that key.
  - Otherwise the line is read by its number of segments:

    | Segments | Becomes |
    | --- | --- |
    | 1 | Subject `sbt.{0}` |
    | 2 | Usage `usg.{subject}.{responsibility}` |
    | 3 | Execution `exe.{subject}.{responsibility}.{context}` |
    | 4 | Unit of execution `uxe.{subject}.{responsibility}.{context}.{unit}` |
    | 5 or more | skipped |

  - A compact line never makes a responsibility, a context or a unit; those need their code.
  - Invalid lines never fail the request. They are skipped, or read with another meaning: `exe.a.b` (a code with the wrong number of segments) becomes the Execution `exe.exe.a.b`, and `"  northwind"` keeps its spaces in the name.
  - The code of the line's own key keeps the case it was typed in: `SBT.northwind` is stored as a different key than `sbt.northwind`. Ancestors always get lower-case codes.
  - Names are not checked at all (`AnnotationRules` checks only the title, the description and the documentation link). A tab or a `/` in a name is stored as sent, and a `/`, `\`, `?` or `#` cannot be part of a Cosmos item id (`AnnotationItemIds.cs`, line 7: `{view}.{key}`).
- **Creation:** `CreateAnnotationsAsync`, lines 137–182.
  - The lines are sorted by type (responsibility, unit, subject, usage, context, execution, unit of execution; a stable sort) and written one after another.
  - A key that already exists (active) is skipped, and so is a key repeated in the request or created earlier in it as an ancestor.
  - Each annotation gets only what its key gives: the name (its last segment) and, for units, the unit type `event` and the definition `unknown`. It has no title and no time zone.
  - Missing ancestors are created with it. A deleted key or a deleted ancestor is re-created empty, without asking (`docs/Platform/Storyteller/annotation-lifecycle.md`, line 63).
- **Response:** `200` with the annotations this call wrote, in the sorted order of the lines: for each line, the ancestors created for it (by depth, then type, then key, so roots first), then the key. A key that already existed is not in it, so a list where every key exists answers `[]`.
- **Not atomic:** the lines are written one by one. When one fails (for example `409` after three concurrent changes, or `500`), the lines before it stay created, and so may some ancestors of the failing line. The error does not say what was written. Sending the same list again is safe, because existing keys are skipped.
- **Project and view:** both come from the route since the follow-up of 2026-10-12 (`docs/Platform/Storyteller/specs/reviews/2026-10-12 Phase 2 of Soft-delete and audit of annotations.md`). The admin UI review `2026-10-11 Review of the PR 65 findings.md`, line 67, still says it wrote to `main/default`; that is no longer true.
- **Public docs:** `docs/42for.net/platform/using-the-platform.md`, section *Writing the catalog* (lines 63–101), describes the compact lines with the same table.

### The SDK

`@42for.net/2splatform.sdk` already has `setAnnotationsSimple(organization, project, view, body?: string[], options?): Promise<Annotation[]>` (`generated/client/annotations/annotations.ts`, lines 278–326). The installed copy in the admin UI contains it, so the SDK does not change.

### The admin UI

- **Annotations page:** `app/pages/[org]/[project]/[view]/annotations.vue`.
  - The navbar (lines 226–253) has **Reload the catalog** and **New**. **New** is two buttons: `label="New"` from `sm` up, and an icon button with `aria-label="New annotation"` below `sm`. Both are shown only with `useCan('edit')` (Contributor).
  - The empty state (lines 289–296) has a **New annotation** button.
  - `AnnotationCreateDialog` is mounted at line 315, with its open state from `useAnnotationCreate()`, a module-level `ref`.
- **Command palette:** `app/components/shell/CommandPalette.vue`, lines 86–98. The action **New annotation** goes to the annotations page and calls `useAnnotationCreate().show()`.
- **Single create:** `app/components/annotations/AnnotationCreateDialog.vue`.
  - It checks deleted keys first: `useDeletedKeyCheck().check(key)` looks up the key and its ancestors, and `DeletedKeysDialog` offers **Restore and update**, **Create new** or **Cancel**. `check` already takes a list of keys.
  - After the write it calls `catalog.upsert(created)`, and `catalog.load(scope, { force: true })` in the background when ancestors were created.
  - Errors are shown in the dialog with `errorMessage(error)` in an error `UAlert`.
- **Writes:** the app calls the plain SDK functions (no TanStack mutations). `useAnnotationActions().restoreMany` (`app/composables/useAnnotationActions.ts`, lines 168–191) is the model for a batched write: `chunk(keys, 100)`, a request per batch, `catalog.upsert`, then `invalidateDeleted` and `invalidateAnnotation` per key, and `catalog.refreshConfigurations()`.
- **Paste dialog:** `app/components/key-values/KeyValueImportDialog.vue` is the closest pattern: a `UModal` with `:description` naming the target, a mono `UTextarea`, a review list where invalid rows are red, a result `UAlert`, and the buttons in `#footer`.
- **Keys:** `app/utils/annotation-key.ts` re-exports the SDK key helpers (`parseAnnotationKey`, `isValidName`, `getAllAncestors`, `getAnnotationTypeInfo`) and adds `typeMeta`, whose labels and icons follow the project settings (424-13). `AnnotationTypeBadge` and `KeyText` show a key with its type. Nothing turns a compact line into a key yet.
- **Mock API:** `app/mock/annotation-handlers.ts` has no handler for `…/annotations/simple`. The handler `http.post(`${api}/annotations/:key`)` (line 628) catches it with the key `simple` and answers `400 InvalidKey`. `store(db, params, annotation)` (lines 135–191) already creates the missing ancestors, re-creates deleted keys and writes the audit entries.
- **Catalog:** `app/stores/catalog.ts` holds the active annotations of the view, disabled and expired ones included, and no deleted ones, the same notion of "exists" as the server. It loads page by page (`status === 'loading'`) and can first show an IndexedDB snapshot (`fromSnapshot`), so it is not always complete when a dialog opens.
- **Navbar on phones:** below `sm` the annotations navbar has **Reload** and the **New** icon next to the search button, the theme toggle and the user menu, and the breadcrumb `acme / billing` just fits. The key-values page has three page buttons there, and its breadcrumb is cut (`key-values-mobile-light.png`).
- **Tests:** no test covers the simple endpoint in the UI. `test/e2e/annotations.spec.ts`, lines 148–177, opens **New** with `getByRole('button', { name: isMobile ? 'New annotation' : 'New', exact: true })`. `test/e2e/visual.spec.ts` takes the annotations page in four shots (`annotations-{desktop,mobile}-{light,dark}.png`); on desktop the annotation detail and history open over the list, so `annotation-detail-desktop-*` and `annotation-history-desktop-*` show its navbar too.
- **Docs:** `docs/42for.net/platform/admin-ui.md`, line 94, describes **New**; line 106 describes the deleted-key question; line 108 the command palette.

## Proposed Changes

### 1. Reading the lines (`app/utils/bulk-keys.ts`, new)

Pure functions for the dialog.

```ts
export type BulkLineProblem =
  | 'too-many-parts' // more than 4 parts without a code
  | 'wrong-parts' // starts with a type code, but the number of parts does not fit it
  | 'empty-part' // "a..b", ".a", "a."
  | 'invalid-name' // a name with a tab or another control character, or with / \ ? #

export interface BulkLine {
  /** 1-based line number in the text. */
  line: number
  /** The line as typed, trimmed. */
  text: string
  /** The type code (lower case) when the first part is one and there are more parts. */
  code?: AnnotationTypeCode
  /** The key the line stands for, with a lower-case code; undefined when the line has a problem. */
  key?: string
  problem?: BulkLineProblem
}

/** Splits the text on line breaks, trims the lines and drops blank ones; each line is read with readBulkLine. */
export function readBulkLines(text: string): BulkLine[]

/** Reads one line into a key. */
export function readBulkLine(text: string, line: number): BulkLine

/** The message of a line's problem for the review, e.g. "An Execution key has 4 parts: exe.subject.responsibility.context". */
export function describeBulkProblem(line: BulkLine): string
```

`readBulkLine` is stricter than the API, so the user sees what will be created before anything is sent, and the UI sends canonical keys only:

1. Split on `.` and trim each part. An empty part is `empty-part` (the API drops it silently).
2. When the first part is a type code (any case) and there are more parts, the line is a key: its number of parts must fit the code (`rst` and `sbt` 2, `usg`, `cnt` and `unt` 3, `exe` 4, `uxe` 5), otherwise `wrong-parts`. The API would read it as a compact line with another meaning (`exe.a.b` as `exe.exe.a.b`). The code is written in lower case.
3. Otherwise the line is compact and is read by its number of parts, 1 to 4, as in the table above; more is `too-many-parts`. A one-part line is always a subject, even when it is a type code (`exe` is the Subject `sbt.exe`), as in the API.
4. A name with a control character (`\u0000`–`\u001f`, `\u007f`) or one of `/ \ ? #` is `invalid-name`. The tab is the common case: two cells copied from a spreadsheet. Its message says "Contains a tab: cells copied from a spreadsheet? Join them with dots."

The text is split on line breaks only (`\r\n` or `\n`), because a name may contain spaces or commas. For every canonical key, the server reads the same key (`AnnotationKey.TryParse`), so the key in the review is the key that is created.

### 2. Batched create (`useAnnotationActions().createMany`)

```ts
export interface BulkCreateOutcome {
  /** Everything the API created, in the order it answered: ancestors and the keys of the list. */
  created: Annotation[]
  /** Sent keys the API skipped, because they existed already. */
  skipped: string[]
  /** Set when a batch failed; the batches before it stay created, and the failed one may have written some keys. */
  failure?: unknown
}

async function createMany(keys: string[]): Promise<BulkCreateOutcome>
```

- The keys are deduplicated, sorted by type (merge order, stable), and sent with `setAnnotationsSimple` in batches of 100 (`simpleBatchSize`, like `restoreBatchSize`), one batch after another. The sort keeps a key of the list from coming back as an ancestor of an earlier batch.
- `progress`, the per-instance `{ done, total }` ref of `useAnnotationActions`, counts the keys sent and goes back to `undefined` at the end, like `saveMany`. The bulk bar has its own instance and is not affected.
- Every returned annotation is upserted into the catalog. `skipped` is the sent keys that are in no response.
- A failed batch stops the loop. The outcome keeps what was created and the error, so the dialog can show both and offer to try again.
- At the end, also after a failure:
  - `invalidateDeleted` (re-created keys leave **Deleted**);
  - `invalidateAnnotation` for every created key, and after a failure also for every key of the failed batch and its ancestors (the detail and history of a re-created key);
  - `catalog.load(scope, { force: true })` in the background, because the relation arrays in the response are snapshots that miss the children written later in the same request, and a failed batch is not in any response;
  - `catalog.refreshConfigurations()`.

### 3. The dialog (`app/components/annotations/AnnotationBulkCreateDialog.vue`, new)

`UModal` with `title="Bulk create annotations"`, `:description="`In ${org}/${project}, view ${view}.`"` and `:ui="{ content: 'max-w-3xl' }"`, opened with `v-model:open` like `AnnotationCreateDialog`. The state resets each time it opens. While it creates, the modal cannot be dismissed (`:dismissible="!creating"`, `:close="!creating"`), so a run never writes into a reset dialog.

**Explanation**, at the top: `UAlert color="neutral" variant="subtle" icon="i-lucide-info" title="How bulk create works"`, with a list in the `#description` slot:

- One key per line. A line that starts with a type code is that key, for example `rst.invoicing`.
- A line without a code is read by its number of parts: `northwind` is a Subject, `northwind.invoicing` a Usage, `northwind.invoicing.production` an Execution, and four parts a Unit of execution. Responsibilities, contexts and units need their code.
- Missing ancestors are created with them. Keys that already exist are skipped, so the same list can be sent again.
- The new annotations get only their key and name; add titles, labels and configurations later.

The type names in the text come from `typeMeta`, so they follow the project settings.

**Input:** `UFormField label="Keys"` with a mono `UTextarea` (`autofocus`, 8 rows, `autoresize` up to 16) and a placeholder of example lines. The help text says "One key per line, up to 1,000." `Ctrl`/`⌘` + `Enter` in the textarea runs **Create** when it is enabled. The text is read with a short debounce (`refDebounced`, 150 ms), so a long paste is not parsed on every keystroke.

**Review**, below the input, computed from `readBulkLines(text)` and the catalog:

- A summary line, leaving out zero counts: "2 new · 1 already exists · 1 duplicate · 1 invalid · 4 missing ancestors".
- A table (Line, Key, Status) in `max-h-72 overflow-auto rounded-md border border-default`, `tbody divide-y divide-default`, like the review of `KeyValueImportDialog`. Each row has a status as text (a `UBadge`), so colour is not the only signal:
  - **New**: the key is not in the catalog;
  - **Exists**: the key is in the catalog (or the typed text is, for a legacy key with an upper-case code), and is skipped;
  - **Duplicate**: the same key is on an earlier line, and is skipped;
  - **Invalid**: the row gets `bg-error/5`, the key cell shows the typed text in `font-mono text-xs text-error`, and the status cell the message of its problem.
- The key cell shows `AnnotationTypeBadge` and `KeyText` of the key; the typed text follows in muted text when it differs from the key.
- The table shows the lines in order, at most 200 of them, and every invalid line; then "and *N* more lines".
- The missing ancestors are the ancestors of the new keys that are neither in the catalog nor in the list. The summary counts them; the table does not list them.
- While `catalog.status !== 'ready'` or `catalog.fromSnapshot`, a note says "The catalog is still loading: a key shown as new may exist already; the API skips it." After a load error it says the keys could not be checked. **Create** stays allowed; the result counts come from the response.

**Buttons** (`#footer`, `flex w-full flex-wrap justify-end gap-2`, like `KeyValueImportDialog`):

- **Cancel** closes the dialog.
- **Remove invalid lines** appears when there are invalid lines and deletes them from the text.
- **Create *N* annotations** (*N* = new keys) is disabled while there is no new key, while a line is invalid, or above 1,000 non-blank lines. An invalid line blocks the create, so nothing is sent with a meaning the user did not see.

**Create:**

1. Take a snapshot of the new keys; the review shows the snapshot until the run ends, and the textarea is read-only.
2. `useDeletedKeyCheck().check(snapshot)` looks up the keys and their ancestors.
   - **Cancel** stops here.
   - **Restore** restores the deleted keys and ancestors with `restoreMany(keys, { descendants: false })`; the restored keys are removed from the snapshot. When `restoreMany` throws or reports failed keys, the error `UAlert` shows them, as in `AnnotationCreateDialog`, and nothing is sent.
   - **Create new** goes on, and the API re-creates them empty.
   - `DeletedKeysDialog` gets an optional `restoreLabel` prop (default "Restore and update"); the bulk dialog passes "Restore", because nothing is updated.
3. `createMany(snapshot)`. Above 100 keys, a `UProgress` shows the keys sent.
4. **Result**, in place of the input and the review:
   - a success `UAlert` (`variant="subtle"`): "Created 6 annotations: 2 from the list and 4 missing ancestors." "From the list" counts the created keys that are in the snapshot; the others are ancestors.
   - When keys were skipped: "1 key already existed and was skipped." Skipped are the **Exists** and **Duplicate** lines of the review, plus `outcome.skipped`.
   - When keys were restored: "3 deleted annotations were restored." Restored keys are not counted as skipped.
   - the list of the created keys, with type badge, in the same table style;
   - **Create more** (back to an empty input, focused) and **Done** (closes, and gets the focus).
5. **Failure:** the error `UAlert` shows `errorMessage(error)` and "At least *N* annotations were created before the error; creating the same list again is safe, because existing keys are skipped." The input stays, and the button reads **Try again**.

No toast: the result step says it, as in `KeyValueImportDialog`.

### 4. Opening the dialog

- **Composable:** `app/composables/useAnnotationBulkCreate.ts`, a module-level `ref(false)` with `show()`, like `useAnnotationCreate`.
- **Navbar** of the annotations page: a **Bulk create** button before **New**, `icon="i-lucide-list-plus"`, `color="neutral"`, `variant="outline"`, `class="hidden sm:inline-flex"`, only with `canEdit`. It is not shown below `sm`, where the navbar has no room for a third page button; phones open the dialog from the command palette or the empty state.
- **Empty state:** a second button **Bulk create** (`color="neutral"`, `variant="outline"`) next to **New annotation**, because an empty view is where a list of keys helps most. The description becomes "Create the first annotations, or import them with the sform CLI."
- **Command palette:** the action **Bulk create annotations** (`i-lucide-list-plus`) right after **New annotation**, under the same condition (`catalogInScope && canEdit`). It goes to the annotations page and calls `show()`.
- The dialog is mounted on the annotations page next to `AnnotationCreateDialog`, with `v-if="canEdit"`.

No keyboard shortcut is added; **New** has none either.

### 5. Mock API (`app/mock/annotation-handlers.ts`, `app/mock/simple-lines.ts`)

- `keyOfSimpleLine(input: string): string | undefined` in `app/mock/simple-lines.ts` reads a line exactly as `CreateAnnotationsFromStringAsync` does: blank lines skipped, no trimming, empty segments dropped, a code with the wrong number of segments read as a compact line, more than four segments skipped, the typed case of the code kept. It lives in the mock, which production builds do not contain.
- A handler `http.post(`${api}/annotations/simple`)`, registered before `${api}/annotations/:key` (the literal segment wins, as for `deleted`):
  - `requireRole(…, 'Contributor')`.
  - A body that is not an array answers `500`, like the server's null list. More than 100 entries: `400` with the server's message and no error code.
  - Entries that are not strings, or that `keyOfSimpleLine` cannot read, are skipped. The keys are sorted by type in the order of the server's enum (`rst`, `unt`, `sbt`, `usg`, `cnt`, `exe`, `uxe`, the SDK's `mergeOrder`), keeping the input order within a type.
  - For each key: a key already handled in the request, or active in the view, is skipped. Otherwise the handler re-reads `annotationsOf` (it is rebuilt after every write), takes `missing = getAllAncestors(key)` that are not active, and calls `store()` with `withRelations({ ProjectName, ViewName, AnnotationKey, Name, AnnotationType })`, as `defaultAncestor` builds a body. `store()` already writes the missing ancestors, re-creates deleted ones and writes the audit entries.
  - The response gets the `missing` ancestors read back from `annotationsOf`, ordered by `depthOf`, then type, then an ordinal comparison of the key, and then the key. The key and its ancestors are added to the handled set.
- The existing bulk `POST …/annotations` handler is not changed.

### 6. Tests

- **Unit** (`test/unit/utils/bulk-keys.test.ts`): `readBulkLines` with blank lines, `\r\n`, trimming of lines and parts; keys with a code, upper-case codes written in lower case; compact lines of 1 to 4 parts; `too-many-parts`, `wrong-parts` (`exe.a.b`), `empty-part` (`a..b`), `invalid-name` (a tab, `a/b`); a one-part type code read as a subject; `describeBulkProblem` messages.
- **Unit** (`test/unit/annotations/bulk-create.test.ts`), against the stateful mock (`setupServer(...mockHandlers)`):
  - `keyOfSimpleLine`: the API's reading, including `exe.a.b` → `exe.exe.a.b`, `a..b` → `usg.a.b`, five segments → `undefined`.
  - The mock endpoint through `setAnnotationsSimple`: creates the keys with their ancestors and returns them roots first, an ancestor shared by two lines once; skips existing and repeated keys; `400` above 100 entries; `403` for a Reader (`northwind.portal`).
  - `createMany` through a small harness component mounted with `mountScreen` (it needs `useQueryClient`), as in `soft-delete.test.ts`: 150 keys make two requests and are all created; an existing key lands in `skipped`.
  - The dialog, mounted with `mountScreen` and `{ open: true }` after `await useCatalogStore().load(scope)`. The modal is teleported, so the test types with `new DOMWrapper(document.body.querySelector('textarea')!).setValue(text)` and clicks buttons found in `document.body`, then `settle()`. Cases: the summary and the statuses; an invalid line disables **Create** and **Remove invalid lines** removes it; **Create** shows the result and the keys are in the mock database; a failing request (overridden with `server.use`) shows the error and keeps the input.
- **End to end** (`test/e2e/annotations.spec.ts`), desktop and mobile:
  - Open **Bulk create** (desktop from the navbar, mobile from the command palette), wait for the catalog, and fill the textarea with `northwind.billing`, `exe.cyberdyne.invoicing.prod` and `rst.invoicing`.
  - Expect the summary "2 new · 1 already exists · 4 missing ancestors" (the ancestors are `rst.billing`, `sbt.cyberdyne`, `usg.cyberdyne.invoicing` and `cnt.cyberdyne.prod`), and the button **Create 2 annotations**.
  - Create; expect "Created 6 annotations: 2 from the list and 4 missing ancestors." and "1 key already existed and was skipped."; close with **Done**.
  - Open `exe.cyberdyne.invoicing.prod` with the filter `key:exe.cyberdyne.invoicing.prod` (the list is virtualized).
  - A Reader (`/northwind/portal/default/annotations`) does not see **Bulk create**.
- **Accessibility** (`test/e2e/accessibility.spec.ts`): axe on the open dialog with a few lines in the review, one of them invalid.
- **Visual:** the new navbar button changes the four `annotations-*` baselines and the desktop `annotation-detail-*` and `annotation-history-*` baselines (the list's navbar shows behind the detail). They are generated again with `npm run test:visual:docker -- --update` and committed separately.

### 7. Documentation

- `docs/42for.net/platform/admin-ui.md`: after the **New** paragraph, a paragraph on **Bulk create**: one key per line, the reading of compact lines (linking `using-the-platform#writing-the-catalog`), the review before creating, ancestors created and existing keys skipped, no title and no time zone on the new annotations, at most 1,000 keys, and that phones open it from the command palette. The deleted-key paragraph says that **Bulk create** offers **Restore** instead of **Restore and update**.
- `src/Platform/ui.admin/README.md`: the mock mode and the end-to-end flows list name the bulk create.
- The review in `docs/Platform/ui.admin/specs/reviews/`.

### Out of scope

- Titles, labels or a time zone for the new annotations. The simple endpoint takes keys only; the typed bulk endpoint (`POST …/annotations`) could carry them later.
- Reading a file, or other separators than line breaks (CSV, JSON arrays).
- Changes to the API, the OpenAPI document or the SDK. The server stays lenient; the dialog sends only keys it has checked. Rejecting the unsafe name characters on the server would be a separate Storyteller change.
