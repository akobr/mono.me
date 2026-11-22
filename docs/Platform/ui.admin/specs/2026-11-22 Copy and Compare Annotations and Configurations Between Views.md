# Copy and Compare Annotations and Configurations Between Views

Linear: [424-309 Improve usability when working with multiple views](https://linear.app/424fun/issue/424-309/improve-usability-when-working-with-multiple-views)

## Problem

A project of the 2S Platform has views: named variants of its annotations and their configurations (`default`, the production view, and for example `staging`). A developer or a configuration operator changes a view, and then has to bring the change to another view. The Administration UI (`src/Platform/ui.admin`) gives little help with that:

- **Compare views**, a tab of **Modeling**, compares the effective configuration of one key that is picked there. It cannot compare annotations, and it cannot compare several keys at once.
- Nothing copies an annotation or a configuration to another view. The only transfer is **Export & Import** of a whole view, which needs the Administrator role and a zip file.
- The multi-selection of **Annotations** offers Enable, Disable, Label and Delete only. **Configurations** has no multi-selection. The configuration detail has no **More actions** (…) menu.

The issue asks for:

1. copy and compare to a different view from the multi-selection of annotations;
2. a multi-selection of configurations, with the same batch copy and compare;
3. a batch compare that shows several JSON documents on one page, like the "Files changed" view of a GitHub pull request;
4. copy and compare of one annotation or configuration from the **More actions** (…) menu of its detail panel.

## Current State

All paths are relative to `src/Platform/ui.admin/src`.

- **Annotations page:** `app/pages/[org]/[project]/[view]/annotations.vue`.
  - The selection is a TanStack `RowSelectionState` keyed by annotation key, kept per view in the page memory (`AnnotationsMemory.selection`, `app/stores/page-memory.ts`, lines 17–26). `AnnotationGrid.vue` (lines 25–32) projects it onto the row ids of the shown rows.
  - `selectedKeys` (lines 114–116) are the selected keys that are still in the catalog. `<AnnotationBulkBar v-if="selectedKeys.length && canEdit" :keys="selectedKeys" @clear="rowSelection = {}" />` (line 291) shows the bar to Contributors and above.
  - Below 768 px the page renders `AnnotationList.vue`, which has no checkboxes.
- **Bulk bar:** `app/components/annotations/AnnotationBulkBar.vue`, a `role="toolbar"` named "Bulk actions" with **Enable**, **Disable**, **Label**, **Delete**, a progress bar and **Clear selection**. Its actions come from `useAnnotationActions()` (`app/composables/useAnnotationActions.ts`), and every one of them writes to `context.scope`, the view of the route.
- **Configurations page:** `app/pages/[org]/[project]/[view]/configurations.vue`. A virtualized `UTable` over the configuration summaries of the catalog (lines 40–60, 176–188) with `:get-row-id="(row) => row.key"`, `class="h-full"` and `:on-select` that opens the detail. It has no selection, and `ConfigurationsMemory` holds only `scrollTop`. The rows include summaries without an own document (`v0`). Below 768 px it is a list of buttons.
- **Annotation detail:** `app/components/annotations/AnnotationDetail.vue`, used by the annotations page and by Graph. Its **More actions** menu (lines 78–115, rendered at 160–162) has **Show in graph** or **Show in list**, then **Copy link** and **Copy key**, then **Delete** for Contributors (`useCan('edit')`).
- **Configuration detail:** the header is inline in `app/pages/[org]/[project]/[view]/configurations/[key].vue` (lines 33–87). It has no **More actions** menu.
- **Compare views:** `app/pages/[org]/[project]/[view]/modeling/compare.vue`, addressed by `?key=&to=`.
  - It shows `getConfigurationViewDiff` (the counts and hunks of the server) next to the effective documents of both views (`getConfiguration`). The left side is the view of the route.
  - The views come from `useGetViews(org, project)` without `discover`, the same query as in `ModelingHeader.vue` and `ContextSwitcher.vue`. One other view is preselected. `landed` (lines 43–46) holds every `router.replace` and every request back until the route and the context store agree (424-64). A `?to=` that is not in the list is kept, since the Views page can link to a discovered view.
  - All of Modeling is locked below Contributor (`modeling.vue`), and **Compare** on the Views page has the same rule.
- **Diff editor:** `app/components/editor/DiffEditor.vue`: a CodeMirror `MergeView` (split) or `unifiedMergeView`, read-only, JSON. Its counts and its previous and next buttons exist only with a `DiffResult` of the server. Each instance has its own toolbar and a height limit of `70vh` (lines 174–177). `layout` and `collapseUnchanged` are read once (lines 41–42). Its editors (`role="textbox"`) have no accessible name.
- **API and SDK** (`@42for.net/2splatform.sdk`). Every function takes the view, so another view than the one of the route can be read and written. Roles are per project, never per view. A view is implicit: a write to any view name succeeds and creates data there.
  - `getAnnotation(org, project, view, key, { includeInactive: true })` is the point read; with `includeDeleted: true` a deleted annotation is answered with its `Deletion`. The lists (and so the catalog) return numbers and dates of `Values` in another form, so they are no source for a copy or a comparison. `getAnnotationVersioned` also returns the ETag.
  - `setAnnotation` creates the annotation with its missing ancestors, or replaces its editable content. The route sets `ProjectName`, `ViewName` and `AnnotationKey`, and the server-owned members are ignored, so an annotation read in one view can be sent to another. An ancestor that the API creates gets only its key and name, plus `ValidFrom`, `ExpiresAt`, `TimeZone`, `IsDisabled` and `Labels` of the written annotation. A write to a key that is deleted in that view, or below a deleted ancestor, re-creates it and empties its configuration. `setAnnotationVersioned` takes `ifMatch` (only that version, never a re-creation) and `ifNoneMatch: '*'` (only a creation).
  - The API decides that an annotation did not change by its own rules (`AnnotationAuditDiff`): labels as a set, `ValidFrom` and `ExpiresAt` as instants, a null and an empty top-level member as the same.
  - `findDeletedAnnotations(org, project, view, { Keys })` finds the deleted ones among at most 100 keys.
  - `getConfiguration` returns the effective document (ancestors, the type template, the own document) with the `Version` of the own document. It answers 404 when the annotation is missing or deleted (then with the error code `AnnotationDeleted`). `getConfigurationVersion(…, key, version)` returns the own document; `useConfiguration` asks for it also with version 0 and takes a 404 as "nothing stored".
  - An own document without members counts as none in the API (`HasContent`, the view import skips it), and the editor's **Create empty** stores one.
  - `setConfiguration` (POST) merges its body into a stored own document with members. It replaces a missing or empty one, after it ran root members named `$remove` and `$patch` as directives.
  - `patchConfiguration` needs a stored version (404 otherwise). The API does not decode the `~0` and `~1` of a JSON Pointer, so the patch that `buildPatch` (`app/utils/json-patch.ts`) builds writes a wrong member for a name with `/` or `~`. `sform` and the MCP server replace the whole document for that reason: `[{ op: 'test', path: '', value: stored }, { op: 'replace', path: '', value: desired }]` (`Storyteller/Mcp/src/Json/JsonPatchBuilder.cs`, lines 39–46). A failed `test` answers 412, and a write is validated against the schemas of its own view (409 with `schemaErrors`).
  - The API accepts a configuration write for a key without an annotation and stores a configuration without an annotation. The mock answers 404.
  - The SDK parses JSON with `JSON.parse`, so an integer beyond 2^53 is rounded in the browser. The server keeps it exactly.
  - Nothing compares annotations, compares several keys, or copies between views on the server.
- **Mock data:** in the `default` dataset of the unit and e2e tests, `acme/billing` (Owner) has the views `default` and `staging` with different annotations and configurations. `acme/shipping` (Contributor) and `northwind/portal` (Reader) have one view. The mock keeps data per view, its handlers accept any view name, and its PATCH applies a patch of the root.
- **Tests:** `test/unit/components/annotation-bulk-bar.test.ts` (delete only, mounted without a router and a query client), `test/unit/modeling/compare.test.ts`, `test/e2e/annotations.spec.ts`, `configuration.spec.ts`, `settings.spec.ts` (line 135 reads the first cell of a Configurations row), `accessibility.spec.ts`. The visual baselines hold `configurations` in four variants. No unit test mounts a virtualized table: happy-dom has no layout, so it renders no rows, and its `IntersectionObserver` never calls back.
- **Deployed-UI automation:** `src/Platform/UI.Admin.AutomationTests/src/Pages/ConfigurationsPage.CFG.cs` reads the cells of a Configurations row by position (`TypeColumn = 0` to `UpdatedColumn = 5`, lines 19–24).
- **Docs:** `docs/42for.net/platform/admin-ui.md` (the page memory at lines 33–40, the bulk actions at line 80, the annotation detail at 110–125, Configuration at 151–186, Modeling at 206–212) and `src/Platform/ui.admin/README.md` (the e2e list, the client state, `ph-no-capture`).

## Proposed Changes

The change is in the admin UI only. It uses the API calls above, so no API, SDK or mock handler changes.

Two words are used below. The **source** is the view of the route, where the rows are selected. The **target** is the other view.

### 1. The documents that are compared and copied (`app/utils/view-documents.ts`, new)

```ts
export type ViewItemKind = 'annotations' | 'configurations'
export type ConfigurationContent = 'own' | 'effective'

/** The editable members of an annotation as the API compares them. */
export function annotationDocument(annotation: Annotation): Record<string, unknown>
/** JSON with two spaces whose object members are in the order of their names at every level; arrays keep their order. */
export function canonicalJson(value: unknown): string
export function annotationText(annotation: Annotation): string
/** Whether the value holds an integer that the browser cannot hold exactly (beyond 2^53). */
export function hasInexactNumber(value: unknown): boolean
/** The own document of a configuration, or undefined for none: an object without members counts as none. */
export function ownDocument(content: unknown): Record<string, unknown> | undefined

export interface AnnotationSide {
  annotation?: Annotation
  /** The key is deleted in the view; only a read with `includeDeleted` tells. */
  deleted: boolean
}
export interface ConfigurationSide {
  /** The effective document could be read: the annotation is in the view and is not deleted. */
  readable: boolean
  /** The annotation is deleted in the view (404 with AnnotationDeleted). */
  deleted: boolean
  version: number
  effective?: unknown
  /** The own document; undefined when nothing, or a document without members, is stored. */
  own?: Record<string, unknown>
}
export function readAnnotation(scope: Scope, key: string, options?: StorytellerRequestInit & { includeDeleted?: boolean }): Promise<AnnotationSide>
export function readConfiguration(scope: Scope, key: string, options?: StorytellerRequestInit): Promise<ConfigurationSide>
```

- **Annotation:** `annotationDocument` holds `Title`, `Description`, `DocumentationLink`, `Labels`, `Values`, `IsDisabled`, `ValidFrom`, `ExpiresAt`, `TimeZone`, and for a unit `UnitType` and `UnitDefinition`, in this order, which is the order of an annotation file of the view export (`docs/Platform/Storyteller/export-import.md`). It is normalised as the API compares annotations, so the page and the API agree on "no change":
  - a top-level member that is null, an empty string, an empty array, an empty object or `IsDisabled: false` is left out. Inside `Values` nothing is left out: a name with null or an empty string is a value;
  - `Labels` are distinct and sorted;
  - `ValidFrom` and `ExpiresAt` are UTC instants (`new Date(value).toISOString()`).

  The key, the names, `ViewName`, the stamps and the relation arrays are not part of it: they are the same by definition, or owned by the server. `annotationText` is the JSON of it with two spaces, with the members of `Values` in the order of their names.
- **Configuration:** the own document (the default) or the effective one, as `canonicalJson`. The order of the members is no difference: two views can hold the same document with its members in another order.
- **`canonicalJson`** writes the text itself, member by member in the order of `Object.keys(value).sort()`. It never copies members into a new object, which would lose a member named `__proto__` and reorder names that look like numbers.
- **Numbers.** A document with an integer beyond 2^53 is **inexact**: the browser has rounded it, so it can neither be compared nor copied truthfully.
- **Reads:**
  - `readAnnotation` is `getAnnotation` with `includeInactive: true` (and `includeDeleted` when asked). A 404 is `{ deleted: false }` without an annotation; an answer with `Deletion` is `{ deleted: true }` without one.
  - `readConfiguration` is `getConfiguration`, then `getConfigurationVersion` of its `Version`, also for version 0, as `useConfiguration` does. A 404 of the first is `{ readable: false, version: 0 }`, `deleted` by its error code. A 404 of the second leaves `own` undefined.
  - Any other error is thrown.

### 2. Counting changed lines (`app/utils/line-diff.ts`, new)

`lineStats(before: string, after: string): { additions: number; deletions: number }` counts the lines only in `after` and the lines only in `before`, as the `+` and `−` of a file on GitHub. It trims the common start and end, then runs the greedy algorithm of Myers for the length of the shortest edit script: time O((n + m) · d), memory O(n + m). An empty text has no lines. Above 8,000 changed lines the search costs too much, and the lines that each side has more often than the other are counted without their order. The counts of the server (`DiffResult.Stats`) exist only for the effective configuration of one key, so the page counts its own lines for every kind.

### 3. Addressing a comparison (`app/utils/view-compare.ts` and `app/stores/view-transfer.ts`, new)

The comparison of a selection is a page of its own, below the **Compare views** tab:

```
/{org}/{project}/{view}/modeling/compare/selection?kind=annotations&to=staging&key=sbt.northwind&key=usg.northwind.invoicing
```

- **File:** `app/pages/[org]/[project]/[view]/modeling/compare.selection.vue`. A dot in a file name is a slash of the path without a nested route (`dotNesting`, the default of the router plugin), so the page is a sibling of `compare.vue`, a child of `modeling.vue`. It is locked below Contributor like every Modeling page, `modelingTab()` returns `compare` for it, so the **Compare views** tab is selected, and Modeling remembers it as its last page. A click on that tab opens **Compare views** itself. The view switcher keeps only the tab, so it opens **Compare views** of the other view; **Swap views** (section 5) is what turns a comparison around.
- **Query:**
  - `kind`: `annotations`, anything else is `configurations`.
  - `to`: the target view. As on **Compare views**, a `to` that names the view of the route is no target.
  - `key`, repeated: the keys. Duplicates are dropped, and the keys are shown sorted.
  - `content=effective`: the effective configurations. Absent: the own ones.
  - `set`: instead of `key`, the id of a list of keys that the browser keeps (below).
- **Limit:** `viewTransferLimit = 100` keys for one comparison and for one copy. The bulk bars disable both actions above it, and the page compares the first 100 keys of a longer address and says so.
- **Long selections:** an address stays below about 2,000 characters. When the keys need more (`key` parameters longer than `maxKeyQueryLength = 1500` characters, about 40 keys), the address carries `set=<id>`, and `useViewTransferStore` keeps the keys:

```ts
export const useViewTransferStore = defineStore('view-transfer', () => {
  /** Keeps the keys and returns their id, a hash of the sorted keys; the oldest of more than eight lists is dropped. */
  function remember(keys: readonly string[]): string
  function keysOf(id: string): readonly string[] | undefined
  /** The target last compared with or copied to in a project, by `org.project`. */
  function lastTarget(projectKey: string): string | undefined
  function rememberTarget(projectKey: string, view: string): void
  function clear(): void
})
```

  - The lists are in `sessionStorage` (`2s-admin:view-transfer`), so a reload of the tab finds them. They are cleared on sign-out. The last targets are in memory only.
  - The id is a hash of the keys, so the same selection has the same address, and opening it again does not push another list out.
  - A list is stored when the comparison is opened, never while an address is computed. So the bulk bars open the comparison with a button (`openCompare()`, section 8), not with a link.
  - In another tab, another browser or after sign-out, a `set` address shows "The selection is gone". A comparison of a few keys, and every comparison of one key from a detail, is a plain address that can be reloaded and shared.
- **Helpers** of `view-compare.ts`:

```ts
export interface CompareRequest { kind: ViewItemKind; keys: string[]; to?: string; content: ConfigurationContent; lost: boolean; truncated: boolean }
/** The address of a comparison. `set` is given for keys that do not fit in the address. */
export function compareLocation(scope: Scope, request: { kind: ViewItemKind; keys: readonly string[]; to?: string; content?: ConfigurationContent; set?: string }): RouteLocationRaw
export function fitsInAddress(keys: readonly string[]): boolean
export function parseCompareQuery(query: LocationQuery, view: string, keysOf: (id: string) => readonly string[] | undefined): CompareRequest
export type CompareStatus = 'different' | 'identical' | 'onlySource' | 'onlyTarget' | 'absent'
export interface CompareResult { status: CompareStatus; additions: number; deletions: number; source: string; target: string }
/** `undefined` is a side without a document. */
export function compareTexts(source: string | undefined, target: string | undefined): CompareResult
```

### 4. Loading both sides (`app/composables/useViewCompare.ts` and `app/utils/limiter.ts`, new)

`useViewCompare({ source, target, kind, keys, content })` returns one entry per key:

```ts
export interface CompareFile {
  key: string
  loading: boolean
  error?: unknown
  /** The result of the shown documents: the annotations, or the own or the effective configurations. */
  shown?: CompareResult
  /** What a copy would move: the annotations, or the own configurations whatever is shown. */
  own?: CompareResult
  /** Own configurations are shown, and the effective ones differ although the own ones do not. */
  effectiveDiffers: boolean
  /** The key is deleted in the target. */
  deleted: boolean
  /** A side holds a number beyond 2^53: the file is never identical. */
  inexact: boolean
  /** What a side lacks, for example "The annotation is not in staging". */
  notes: string[]
  /** What the target held when it was read: the stamp of the annotation or the version of the configuration. */
  expected: string | number | null
  retry: () => void
}
```

- One query per key and view (`useQueries` with `shallow: true`), with the query key `['v1', org, project, view, 'annotations' | 'configuration', key, 'view-compare']`. It starts with the path segments of the read, so `invalidateAnnotation`, `invalidateConfiguration` and `invalidateAllConfigurations` of a view refresh it after a save or a copy. No generated key collides with it.
- The query function returns the texts of its side (`annotationText`, or `canonicalJson` of the own and of the effective document), so a text is built once per read. `compareTexts` is cached per key by its two texts.
- There are no queries until the route has `landed` and the target is another view than the one of the route.
- The queries are `meta: { silent: true }`: a failed document shows its error with **Retry** in its own section, not a toast per document.
- `createLimiter(4)` runs at most four reads at once and starts the others as slots free up (`app/utils/limiter.ts`: `createLimiter(limit): <T>(task: () => Promise<T>, signal?: AbortSignal) => Promise<T>`). The timer of a read starts when the read is called, so a hundred keys must not be sent at once. Four is the number of lists the catalog loads at once.
- The target annotation is read with `includeDeleted`, so a key that is deleted there is told from a key that was never there.
- A side has a document:
  - **annotations:** when the annotation exists and is not deleted;
  - **own configurations:** when an own document with members is stored;
  - **effective configurations:** when the effective document can be read.
- For a configuration both results are computed from the one read per view: `own` always, and the effective one for `shown` or for `effectiveDiffers`.

### 5. The comparison page (`compare.selection.vue`, `app/components/views/ViewCompareFile.vue`, new)

The page is the "Files changed" of a selection. It reads like a pull request from the source into the target: **the target is the old side (left, red), the source is the new side (right, green)**, so green lines are what the source adds. This is the opposite of **Compare views**, where the view of the route is on the left. It is chosen because the page is where a copy starts, and a copy goes from the source to the target.

- **Header:** `ModelingHeader`, as on every Modeling page.
- **Title:** an `h2`, "3 configurations: staging → default", with a link back to **Annotations** or **Configurations** of the source view.
- **Sides**, a row in the order of the diff that also works where the diff has one column:
  - "−" (red) and a `USelect` of the other views, named "Target view", with the `prod` badge when it is `default`, and the caption "now";
  - **Swap views** (`i-lucide-arrow-left-right`): the same comparison from the target, an address `/{org}/{project}/{target}/modeling/compare/selection?…&to={source}`;
  - "+" (green), the name of the source view and the caption "after a copy".

  One other view is preselected; with several, the target last used in the project, if it is still listed. The choice is written to `?to=` with `router.replace`, guarded by `landed`.
- **Legend**, one line: "Only the selected keys are compared. A copy moves what is green and never deletes: a document that only {target} has stays." With **Effective**: "What {target} and {source} read. A copy moves only own configurations."
- **Controls:**
  - **Own** | **Effective** (`UTabs`, configurations only, named "Configuration content");
  - **Auto** | **Split** | **Unified** (named "Diff layout"), **Expand all** and **Collapse all**;
  - **Copy {n} to {target}** (section 7): the documents that the active filter shows and that a copy would change, which is an own result of `different` or `onlySource`, never a deleted key. With none it is disabled: "Nothing to copy". It is also disabled for a target that is not a listed view: "{to} is not a view of this project".
- **Summary:** chips with counts that are also filters, a `role="group"` named "Filter by result". A chip with a count of zero is not shown, and each chip has `aria-pressed`. When the count of the active chip falls to zero, the filter returns to **All**. A polite live region says "Compared 12 of 40…" while documents load.

  | Chip | Annotations, effective configurations | Own configurations |
  | --- | --- | --- |
  | **All** | | |
  | **Different** | both sides, different text | the same |
  | **Only in {source}** | only the source has it | **Own configuration only in {source}** |
  | **Only in {target}** | only the target has it | **Own configuration only in {target}** |
  | **Identical** | the same text | the same |
  | **Not in either view** | neither side has it | **No own configuration** |
  | **Deleted in {target}** | the key is deleted in the target | the same |
  | **Effective differs** | | the own result is identical or none, and the effective documents differ |
  | **Failed** | a read failed | the same |
- **One section per key** (`ViewCompareFile`), a `<section>` in key order:
  - Its header is an `h3` with a button (`aria-expanded`) that holds the type badge, the key, the state badge and `+3 −1` (labelled "3 lines added, 1 removed"). Beside it: **Open in {source}** and **Open in {target}**, each only when that side has the document (the annotation or the configuration detail of that view), and **Copy to {target}** for one key, only when a copy would change it.
  - The body is the `DiffEditor` in its embedded form (section 6), with the target as `original` and the source as `modified`. A side without a document is an empty text, so a document that exists only in the source is all green, as a new file on GitHub.
  - Notes under the header: "The annotation is not in {target}", "The own configurations are the same. The difference is inherited (ancestors, the template, key values), and a copy does not change it" (effective documents differ, own ones do not), "It holds a number beyond 2^53, which the browser cannot compare exactly".
  - **Deleted in {target}** shows a link to **Deleted** of the target view, and has no copy button.
  - Sections that are different or exist on one side start open, the others closed. After a copy a section keeps its place and its open state. A closed section mounts no editor. An open one mounts its editor when it first comes near the visible part: `useIntersectionObserver` with the scrolling element of the page as its `root` (the panel body, found from the page's own root element) and a margin of 400 px. So a hundred documents do not build two hundred editors.
  - A section that failed shows `ErrorState` with **Retry**. A loading one shows a skeleton line.
- **Empty states:**
  - no keys: "Nothing to compare", and how to start: select rows on **Annotations** or **Configurations** and choose **Compare with view**. With a `set` that is gone: "The selection is gone. A comparison of many rows opens only in the browser tab that started it.";
  - no other view: the text of **Compare views** ("No other view to compare with");
  - no target picked yet: "Pick the view to compare with".
- The documents carry `ph-no-capture` (README, "Telemetry").

### 6. The diff editor inside a list (`app/components/editor/DiffEditor.vue`)

- Every editor gets an accessible name (`EditorView.contentAttributes`): `originalLabel` and `modifiedLabel` in the split layout, "{modifiedLabel}, compared with {originalLabel}" in the unified one. The page passes "{key} in {target}" and "{key} in {source}". This also names the editors of the existing callers ("Before", "After").
- A new prop, `embedded?: boolean` (default `false`):
  - no toolbar and no row of side labels: the page has one set of controls and one row of sides for all documents;
  - no height limit: the document is as high as its lines, and the page scrolls, as on GitHub. Unchanged regions stay collapsed (`collapseUnchanged`);
  - the layout and `collapseUnchanged` follow their props. Today they are read once; a `watch` copies a changed prop into the local state, which changes nothing for the existing callers, which pass neither.

Without `embedded` the component looks and behaves as it does today.

### 7. Copy to another view (`app/composables/useViewCopy.ts`, `app/components/views/ViewCopyDialog.vue`, new)

**The dialog.** `<ViewCopyDialog v-model:open :kind :keys :target? :expected? :compare? />`, a `UModal`.

- Title: "Copy 3 annotations to another view", or "Copy {key} to another view" for one key. Description: "From {org}/{project}, view {source}."
- **Copy to:** a `USelect` of the other views, with the `prod` badge on `default`. Preselected: the only other view; else the target last used in the project, unless that is `default`. The production view is never preselected by memory. Opened from the comparison page, the target is the compared one and is shown as text. A `target` that is not a listed view is ignored.
- With `default` as the target, a warning: "default is the production view. The copy changes what production reads."
- **When {target} already has it** (`URadioGroup`): **Overwrite** (the default; "Replace its content with the content of {source}") or **Skip** ("Leave it as it is").
- Configurations only: a checkbox **Copy missing annotations too**, checked. "A configuration needs its annotation. When {target} does not have it, the annotation is copied first. Otherwise the configuration is skipped."
- What is copied, in one line:
  - annotations: "The title, description, documentation link, labels, values, the enabled or disabled state and the validity. Ancestors that {target} lacks are copied too. Configurations are not copied.";
  - configurations: "The own configuration, as it is stored. Inherited values, templates and schemas stay those of {target}."
- **Check** runs the copy without writing and shows the plan: "To create 2 · To update 3 · Unchanged 5 · To skip 1" (the dry-run words of `outcomes` in `app/utils/transfers.ts`) and the table below. A change of the target or of an option discards the plan.
- **Copy** runs the copy. After a check it reads "Copy 5 to {target}", the documents to create and to update; with none it is disabled. With `default` as the target, **Copy** is enabled only after a check, and its hint says so: "Check first: default is the production view".
- While a run or a check is going, the dialog cannot be closed, a status line (`role="status"`) says "Copied 3 of 12…", and **Stop** ends it after the request that is under way. The keys not reached are rows "Skipped: Not copied, the copy was stopped".
- **Result:** an alert "Copied to {target}" (success; warning when something failed or was skipped) with the counts ("2 created, 1 updated, 5 unchanged, 1 skipped, 1 failed"), and a table of every document: kind, key (a link to the document in the target view when it was created or updated), outcome badge (the colors of `outcomes`) and the reason. The alert takes the focus. Then **Compare with {target}** (the comparison of the same keys; not on the comparison page itself) and **Done**.
- A copy that fails as a whole (the views or the deleted keys cannot be read, the target is no view of the project, the session ended) shows the error above the rows written so far, and **Try again**. Copying again is safe: what was copied comes back **Unchanged**.
- When the dialog goes while a run is going (the page was left), the run stops after the request under way. The browser asks before the tab is closed or reloaded during a run.
- The dialog carries `ph-no-capture` on its `UModal`.

**The run.** `useViewCopy()` returns `{ progress, run }`:

```ts
export type ViewCopyOutcome = 'Created' | 'Updated' | 'Unchanged' | 'Skipped' | 'Failed'
export interface ViewCopyItem { kind: 'Annotation' | 'Configuration'; key: string; outcome: ViewCopyOutcome; message?: string }
export interface ViewCopyRequest {
  source: Scope
  target: string
  kind: ViewItemKind
  keys: readonly string[]
  existing: 'overwrite' | 'skip'
  /** Configurations: copy an annotation that the target lacks. */
  missingAnnotations: boolean
  /** What the comparison page saw in the target, by key (`CompareFile.expected`). */
  expected?: Readonly<Record<string, string | number | null>>
  /** Decide and report, write nothing. */
  dryRun?: boolean
}
export interface ViewCopyResult { target: string; dryRun: boolean; items: ViewCopyItem[]; stopped: boolean; error?: unknown }
function run(request: ViewCopyRequest, signal?: AbortSignal): Promise<ViewCopyResult>
```

1. **The target** must be a view that `getViews` lists, other than the source. Otherwise the run fails as a whole, so a mistyped `?to=` never creates a view.
2. The keys are put in merge order (ancestors before descendants), without duplicates.
3. **Deleted keys.** One `findDeletedAnnotations` in the target per 100 keys, over the keys and all their ancestors. A key that is deleted in the target, or below a deleted ancestor, is **Skipped**: "{key} is deleted in {target}. Restore it there first." So the run does not re-create what the lookup found deleted, which would empty its configuration.
4. The keys are copied **one after another**, and the signal is checked before each. Ancestors have to be written before their descendants, and the mock loses updates when two writes overlap. One key is two to four reads and one write.
5. **An annotation:**
   - read in the source (`readAnnotation`). Gone: **Skipped**, "It is no longer in {source}". Inexact: **Failed**, "It holds a number beyond 2^53. Copy it with Export & Import or sform.";
   - read in the target with its ETag (`getAnnotationVersioned`). With an `expected` stamp that is not the one read: **Skipped**, "It changed in {target} since it was compared";
   - in the target and equal `annotationText`: **Unchanged**, nothing is written. Different: **Updated** with `setAnnotationVersioned(…, source, { ifMatch: etag })` (unconditional without an ETag), or **Skipped** ("It is already in {target}") with **Skip**;
   - not in the target: its **ancestors** that the target lacks are copied from the source first, in merge order, each as its own **Created** row ("Missing ancestor of {key}"). So the target gets the real ancestors, not the skeletons that the API would create with the dates, the time zone, the disabled flag and the labels of their descendant. An ancestor that the source lacks too is left to the API. Then **Created** with `ifNoneMatch: '*'`, which never overwrites a key that appeared meanwhile;
   - the run remembers which keys the target has (read once, then updated by its own writes) and which keys were not copied. A key below an ancestor that was not copied (it failed, or it is deleted) is **Skipped** without a request: "Its ancestor {ancestor} was not copied". So the API never creates a skeleton for a failed ancestor.
6. **A configuration:**
   - read in the source (`readConfiguration`). No own document (nothing stored, or a document without members): **Skipped**, "{source} has no own configuration for it". Inexact: **Failed** as above. A root member named `$remove` or `$patch`: **Failed**, "It has a member named $remove or $patch, which a first write would run as a directive";
   - the annotation in the target. Not there: with **Copy missing annotations too** it is copied as in step 5 (its row is **Created**), otherwise **Skipped**, "Its annotation is not in {target}". When its annotation was not copied: **Skipped**, "Its annotation was not copied". A configuration is never written for a key without an annotation;
   - read in the target. With an `expected` version that is not the one read: **Skipped** as above;
   - no own document in the target: **Created** with `setConfiguration`, which replaces a missing or empty document;
   - equal `canonicalJson`: **Unchanged**;
   - different: **Updated** with `patchConfiguration(…, [{ op: 'test', path: '', value: targetOwn }, { op: 'replace', path: '', value: sourceOwn }])`, the patch of `sform` and the MCP server: an exact replacement that fails with 412 when the target changed since it was read, and that has no path to escape. Or **Skipped** ("{target} has its own configuration") with **Skip**.
7. **Failures** of one key do not stop the run: a 409 with `schemaErrors` is "It does not satisfy the schema of {target}: …" with the first error; a 412 is "It changed in {target} meanwhile. Copy it again."; anything else is `errorMessage(error)`. A 401 or a 403 ends the run as a whole, with the rows so far.
8. **A dry run** takes the same decisions and sends no write. What it would create counts as there for the keys after it.
9. **Caches.** After each write, the queries of that key in the target are invalidated (`invalidateAnnotation`, `invalidateConfiguration`). At the end of a run that wrote anything, every configuration query of the target is invalidated once (`invalidateAllConfigurations`): the effective document of a key depends on the configurations of its ancestors, and a created annotation makes its configuration readable. A key that was skipped for `expected` is invalidated too, so its section shows what the target holds now. The catalog holds the source view and is not touched; when it holds the target at the end of the run, it is reloaded.

The copy takes what is stored. Unsaved changes of an open form or editor are not part of it.

### 8. Where the actions are

A new composable, `useViewTransfer(kind, keys)` (`app/composables/useViewTransfer.ts`), is the one place that knows the rules of the two actions:

```ts
export function useViewTransfer(kind: ViewItemKind, keys: MaybeRefOrGetter<readonly string[]>): {
  /** Why the actions are off: no other view, or more keys than the limit. Undefined when they are on. */
  blocked: ComputedRef<string | undefined>
  /** The plain address of the comparison, when the keys fit in an address. */
  compareTo: ComputedRef<RouteLocationRaw | undefined>
  /** Opens the comparison; keeps a long list of keys first. */
  openCompare: () => Promise<void>
  /** The open state of ViewCopyDialog. */
  copying: Ref<boolean>
  /** "Compare with view" and "Copy to view" as the items of a menu group. */
  items: ComputedRef<DropdownMenuItem[]>
}
```

The other views come from `useOtherViews()` (`app/composables/useOtherViews.ts`, new): the `useGetViews` query of the header and the switcher, without the view of the route. `blocked` is "The project has no other view" once the views are known and there is none, and "Select up to 100 to compare or copy them at once" above the limit. Both actions are for Contributors and the roles above, as **Compare views** and the bulk bars are today.

The labels are the same everywhere: **Compare with view** (`i-lucide-git-compare`) and **Copy to view** (`i-lucide-copy-plus`).

- **Annotations bulk bar** (`AnnotationBulkBar.vue`): the two buttons after **Label**, before **Delete**. Blocked buttons are disabled, with the reason as a tooltip and as text beside them, which a keyboard reaches. They keep the selection, since the rows did not change.
- **Configurations** (`configurations.vue`, `app/components/configuration/ConfigurationBulkBar.vue`, `app/utils/row-selection.ts`, new):
  - a first column with `RowSelect` ("Select all rows", "Select {key}"), as in the annotation grid, through `v-model:row-selection` of the `UTable`. A click on a checkbox does not open the detail;
  - the selection is keyed by annotation key and kept per view in the page memory (`ConfigurationsMemory.selection`). The table gets only the selected keys of the shown rows, so the header checkbox is right under a filter, and keys hidden by the filter stay selected. Both directions are pure functions: `shownSelection(selection, shown)` and `mergeShownSelection(selection, shown, next)`;
  - `selectedKeys` are the selected keys that are still among the configurations of the catalog;
  - `ConfigurationBulkBar` (`role="toolbar"`, "Bulk actions") shows "{n} selected", the two buttons and **Clear selection**, for Contributors and above, as on Annotations. The table becomes `min-h-0 flex-1`, as the annotation grid, so the bar does not push its last row out;
  - the list below 768 px has no checkboxes, as on Annotations.
- **Annotation detail** (`AnnotationDetail.vue`, also on Graph): a group with the two items after **Copy link** and **Copy key**, for Contributors, not for a deleted annotation. **Compare with view** is a link, so a form with unsaved changes asks before it is left.
- **Configuration detail** (`configurations/[key].vue`): a **More actions** menu before the pin button, with **Copy link** and **Copy key** for everyone, and the two items for Contributors.
- In a menu, a blocked item stays, disabled, with the reason as its description.
- Each of these four places mounts one `ViewCopyDialog`.

### 9. Tests

- **Unit** (Vitest, the mock API in `msw/node`, new files in `test/unit/views/` unless named):
  - `view-documents.test.ts`: the members and their order, what is left out at the top level and what stays inside `Values`, sorted distinct labels, dates with another offset, a unit, a member named `__proto__`, names that look like numbers, an inexact number, an own document without members, both reads with their 404s and the deleted cases.
  - `line-diff.test.ts`: equal texts, one side empty, a changed line, a moved block, counts equal to those of a naive LCS on random lines below the cap, and one case above it.
  - `view-compare.test.ts`: the address of a few keys and of a long selection, parsing (kind, duplicates, `to` equal to the view, the limit, a lost `set`), the store (the same id for the same keys, eight lists, `sessionStorage`, sign-out), the states of `compareTexts`.
  - `row-selection.test.ts`: the two functions of the Configurations selection.
  - `view-copy.test.ts`:
    - annotations: created (the request has `If-None-Match: *`), updated, unchanged without a request (also for labels in another order and for a date with another offset), **Skip**, a missing ancestor copied from the source and written first, a failed ancestor whose descendant is skipped without a request, deleted in the target, gone in the source, an inexact number;
    - configurations: created by POST (the test first deletes the own configuration of the target), updated with the root `test` and `replace` (the test asserts the request body, with a member named `a/b`), unchanged, no own document and a document without members in the source, a missing annotation with and without the checkbox, `$remove`, a schema failure, a 412;
    - the run: a target that is no view, `expected` that does not match, a dry run that sends no write, **Stop** by the signal, a 403 on the third key, and the invalidation of the target.
  - `view-copy-dialog.test.ts`: the preselected target and that `default` is never preselected by memory, the production warning and its check before **Copy**, the options per kind, **Check** and its plan, the status line, **Stop**, the result with its counts and links, **Try again**.
  - `compare-selection.test.ts`: the page with real routes and a fake `IntersectionObserver` that reports every element as visible (one test keeps the silent one of happy-dom: an unseen section mounts no editor): the sections and their states, the chips as filters, **Own** and **Effective** with **Effective differs**, the preselected target and `landed`, **Swap views**, the empty states, the limit, a target that is no view, and that a copy from the page turns a section into **Identical** (own), and the section of a descendant too (effective, after the configuration of its ancestor was copied).
  - `test/unit/components/annotation-bulk-bar.test.ts`: the existing tests mock `@/composables/useViewTransfer` by its module path; the two buttons, blocked and not, are tested with `mountScreen`.
  - `test/unit/configuration/configuration-bulk-bar.test.ts`: the bar mounted alone, and the page memory of the selection.
  - `test/unit/components/view-transfer-menu.test.ts`: the menus of both details per role, and blocked without another view.
- **End to end** (Playwright, `test/e2e/views.spec.ts`, new; `acme/billing` with `default` as the source and `staging` as the target):
  - Configurations (desktop): the row checkboxes, the header checkbox under a filter, a click on a checkbox does not open the detail, the bar for the Owner and none for the Reader of `northwind/portal`;
  - annotations `sbt.northwind` (**Different**) and `sbt.initech` (**Only in default**): select both, **Compare with view**: two sections with their states, the diff is drawn, **Unified** changes it (desktop);
  - configurations `sbt.contoso` (different), `usg.northwind.invoicing` (identical) and `exe.northwind.reporting.test` (its annotation is not in `staging`): select them, **Copy to view**, **Check**, **Copy**: the result, then **Compare with staging** shows them **Identical** (desktop);
  - from the detail of the annotation `sbt.northwind` and of the configuration `sbt.contoso`: **More actions**, **Copy to view** and **Compare with view** (desktop and mobile);
  - `settings.spec.ts`, line 135: the Type cell is the second cell of a row;
  - `accessibility.spec.ts`: the comparison page once a diff is drawn, and the copy dialog.
- **Visual:** the two desktop `configurations` baselines get the checkbox column and are regenerated in Docker. The comparison page gets no baseline: its documents load one by one.
- **Deployed-UI automation** (`src/Platform/UI.Admin.AutomationTests`): `ConfigurationsPage.CFG.cs` gets `SelectColumn = 0` and its six columns move to 1 to 6; the header comments of `AnnotationBulkBar.cs` and `AnnotationDetail.cs` name the new buttons and menu items. The project is built. New Testiny cases for the copy and the comparison are not part of this change (see Out of scope).

### 10. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - the remembered selection of Configurations, and that Modeling returns to the comparison of a selection;
  - the bulk actions of both pages, and **More actions** of both details;
  - a new part "Working with several views" in Configuration: the comparison page (the sides, own and effective, the states, what the view switcher and **Swap views** do, long selections), the copy (what is copied, ancestors, deleted keys, **Overwrite** and **Skip**, **Check**, the production view, **Stop**, numbers beyond 2^53, the limit of 100), and that both need the Contributor role.
- `src/Platform/ui.admin/README.md`: the e2e and a11y lists, the `view-transfer` store and the selection of Configurations in the client state, the comparison page and the copy dialog in the `ph-no-capture` list, and the architecture note of Modeling.
- The generated `app/auto-imports.d.ts`, `app/components.d.ts` and `app/typed-router.d.ts` are regenerated by a Vite start and committed with the code.
- `CLAUDE.md` needs no change.

### 11. Commits

Every commit builds and passes on its own.

| Commit | Content | Checks |
| --- | --- | --- |
| `docs(platform): plan copy and compare between views in the admin UI (424-309)` | this spec | |
| `feat(platform): compare a selection of annotations or configurations between views in the admin UI (424-309)` | sections 1 to 6; of section 8 the selection of Configurations, `ConfigurationBulkBar` and **Compare with view** in both bars and both menus; their unit tests, the e2e and a11y cases of the comparison, `settings.spec.ts`, the two baselines, the page object | `npm run lint`, `npm test`, `npm run build`, `npm run test:e2e`, `npm run test:visual:docker`, `dotnet build src/Platform/UI.Admin.AutomationTests/src` |
| `feat(platform): copy annotations and configurations to another view in the admin UI (424-309)` | section 7, **Copy to view** in the four places and on the comparison page; their unit tests, the e2e and a11y cases of the copy | the same without the visual run |
| `docs(platform): describe copy and compare between views in the admin UI (424-309)` | section 10 | |
| `docs(platform): review of copy and compare between views in the admin UI (424-309)` | the review | |

### Differences from the issue

- The issue says "copy/compare". Both are built for both kinds, from both multi-selections and from both details.
- The comparison shows the own configuration by default, not the effective one of **Compare views**, because the own document is what a copy moves. **Effective** is one click away, and a section says when only the effective documents differ.
- Both actions need the Contributor role, as **Compare views** does today, although a comparison only reads.
- A copy has a **Check** and, for the production view, needs it. The issue asks for a simple transfer; a transfer into production that cannot be seen first is not one to trust.

### Out of scope

- An API for it: a diff of annotations, a diff or a copy of several keys on the server. A copy of 100 keys is a few hundred requests from the browser.
- Copying schemas, templates and key values, and copying annotations together with their configurations in one run.
- Deleting in the target what the source does not have (also emptying an own configuration), and restoring keys that are deleted in the target.
- **Save anyway** for a configuration that fails the schema of the target; the editor of the target view has it.
- Copying documents with integers beyond 2^53; **Export & Import** and `sform` copy them exactly.
- A multi-selection on phones.
- Changes to **Compare views** itself (one key, effective, the counts of the server), discovered views in the lists of views, and keeping a comparison when the view switcher changes the view.
- New Testiny cases of project `2SP` and their automation tests. Candidates: a batch comparison of annotations, a batch copy of configurations with its check, copy and compare from each detail, and the two actions without another view.
