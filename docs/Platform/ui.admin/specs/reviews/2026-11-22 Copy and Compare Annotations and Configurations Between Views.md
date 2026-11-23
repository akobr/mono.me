# Copy and Compare Annotations and Configurations Between Views

Linear: [424-309 Improve usability when working with multiple views](https://linear.app/424fun/issue/424-309/improve-usability-when-working-with-multiple-views)

## Overview

This review covers the whole spec [Copy and Compare Annotations and Configurations Between Views](../2026-11-22%20Copy%20and%20Compare%20Annotations%20and%20Configurations%20Between%20Views.md), delivered in two feature commits: the comparison, then the copy. The admin UI now compares a selection of annotations or configurations with another view of the project on one page, one diff per key, and copies a selection to another view with a check before it writes. Both start from the bulk bar of Annotations, from the new bulk bar of Configurations, from **More actions** of both details, and the copy also from the comparison page. Nothing changed in the API, the SDK or the mock handlers. The deviations are listed at the end; the larger ones are the run of the copy living in `app/utils/view-copy.ts`, a dialog that closes when the view changes under it, and three tests added to the API's JSON Patch.

| Commit | Subject |
| --- | --- |
| `5c0a71dd7` | docs(platform): plan copy and compare between views in the admin UI (424-309) |
| `4a8408abe` | feat(platform): compare a selection of annotations or configurations between views in the admin UI (424-309) |
| `4b79dfbad` | feat(platform): copy annotations and configurations to another view in the admin UI (424-309) |
| `dc1fad935` | docs(platform): describe copy and compare between views in the admin UI (424-309) |

## What Was Done

Paths are relative to `src/Platform/ui.admin/src/` unless they start with `docs/` or `src/`.

### 1. The documents that are compared and copied

As planned, in `app/utils/view-documents.ts`:

- `annotationDocument` and `annotationText`: the editable members in the order of an annotation file of the view export, normalised as the API compares annotations (a top-level member that is not set left out, the labels a sorted set, the dates as UTC instants, nothing left out inside `Values`).
- `canonicalJson` writes the text itself, member by member in the order of the names, so a member named `__proto__` and names that look like numbers survive.
- `hasInexactNumber`, `ownDocument` (an object without members counts as none), `readAnnotation` (with `includeDeleted` on request) and `readConfiguration` (the effective document, then the own one of its version, also of version 0).
- An addition: `countOf(kind, count)` for "1 annotation", "3 configurations".

### 2. Counting changed lines

As planned, in `app/utils/line-diff.ts`: `lineStats` with the greedy algorithm of Myers and the cap of 8,000 changed lines. Above the cap a pure reordering counts as `+0 −0`; the section then shows **Different** without counts.

### 3. Addressing a comparison

As planned:

- `app/pages/[org]/[project]/[view]/modeling/compare.selection.vue` is the route `/:org/:project/:view/modeling/compare/selection`, a sibling of `compare.vue` below `modeling.vue`.
- `app/utils/view-compare.ts`: `compareLocation`, `fitsInAddress`, `keySetId` (two FNV-1a hashes of the sorted keys), `parseCompareQuery`, `compareTexts`, `copyChanges`, `compareLabel` and the filters of the summary. With both `set` and `key` in an address, the list wins.
- `app/stores/view-transfer.ts`: the lists of keys in `sessionStorage` (`2s-admin:view-transfer`, eight lists), the last target of a project in memory, cleared on sign-out.
- **Addition (test finding):** `app/main.ts` imports the store for its sign-out hook. The store is otherwise loaded only by lazy page chunks, so a sign-out after a reload on another page would have left the lists in the tab.

### 4. Loading both sides

As planned, in `app/composables/useViewCompare.ts` and `app/utils/limiter.ts`, with these details:

- The query of a view returns the texts and flags of its side, not the documents. `compareTexts` is cached per key by its two texts, and a file whose content did not change keeps its identity, so a notification renders only the sections that changed.
- `filtersOf(file)` and `stateOf(file)` give the chips a file counts for and the one state of its badge: a failed file only **Failed**, a key deleted in the target only **Deleted in {target}**.
- The inexact flag is kept per document, so a number beyond 2^53 that is only inherited does not turn equal own documents into **Different**.
- **Deviation.** The read of the target annotation has the query key `[…, key, 'view-compare', { includeDeleted: true }]`. With the literal key of the spec, a view's read as the source and as the target shared one cache entry, and after **Swap views** a key deleted in the new target showed **Not in either view**.
- **Addition (review finding):** the queries have `staleTime: 0`, so a comparison reads both views whenever it is opened.

### 5. The comparison page

As planned, with `app/components/views/ViewCompareFile.vue` as one section. Details that the spec left open:

- The title without a target is "{n} {kind}: {source}". While the views load and no target is set, the page shows the loading state, so "Pick the view to compare with" does not flash before the preselection.
- A `?to=` that is not a listed view is added to the items of **Target view**, so the select shows what is compared.
- A section has no landmark of its own (a hundred regions would bury those of the page); its `h3` holds a button only when there is a document to show. The two groups of tabs are named through a wrapping `role="group"`, because `UTabs` puts an `aria-label` on an element without a role.
- The open state of a section is set when its documents first arrive and changed only by the user afterwards. A click on the chosen chip returns to **All**.
- The link of a key that is deleted in the target is a button **Deleted**, to the **Deleted** tab of the target view.
- **Addition:** in the embedded diff the changed lines of the old side are red, as the "−" of the page says; in every other `DiffEditor` both sides keep the green tint they had.
- **Addition:** in the unified layout a document that only one side has is drawn whole as added or as removed. The merge view would draw the one empty line of the missing side as a removed line above it.
- **Addition (review finding):** the copy dialog of the page closes, and its keys and what it saw of the target are dropped, when the comparison changes (**Swap views**, the browser's **Back**, another target, other keys).
- **Addition (review and test findings):** after a copy the focus has a home. The dialog returns it to the button that opened it; when that button is gone or off, also when that happens only after the page read the target again, the focus goes to the heading of the copied section, or to the title of the page.

### 6. The diff editor inside a list

As planned, in `app/components/editor/DiffEditor.vue`: the `embedded` prop, the accessible names of the editors, and `layout` and `collapseUnchanged` following their props. It also rebuilds when a label changes. Without `embedded` the markup and the classes are unchanged.

### 7. Copy to another view

As planned, with these details:

- **Deviation.** The run is `copyToView(request, { client, onProgress, signal })` in `app/utils/view-copy.ts`, with `copyCounts`, `copyWrites`, `copySummary` and `annotationStamp`. `app/composables/useViewCopy.ts` binds it to the query client and the catalog and keeps the progress. The run is then tested without a component.
- **Deviation.** An annotation whose ETag the browser cannot read is updated under `If-Match: *`, not unconditionally: any existing version, never a re-creation. Across origins the Function App does not expose `ETag`, so on a deployed admin UI every update goes this way, and a change of the target between the run's read and its write is overwritten. Configurations are guarded by the `test` of their patch.
- **Deviation.** `expected` guards a write only. A key whose target changed since the comparison showed it, and holds the content of the source by now, is **Unchanged**; one that moved to other content is **Skipped**. Either way the queries of the key are invalidated, so its section reads the target again (review finding: with the guard before the comparison of the documents, the section stayed **Different**).
- **Addition.** A different own configuration of the target that holds an integer beyond 2^53 fails: the `test` of the patch would hold the rounded number and fail with 412 on every try.
- **Addition (test finding):** a copy of configurations never changes an annotation of the target, also not one that appears between two reads of the run.
- **Addition (test finding):** the run does not wait for the refetch of the queries it invalidated; a query that does not answer would have held the dialog.
- **The dialog** (`app/components/views/ViewCopyDialog.vue`):
  - **Deviation.** With a fixed target the title names it: "Copy sbt.contoso to staging".
  - **Deviation.** **Stop** ends a run after the key that is under way, not after one request; a write is never cut.
  - **Deviation.** After a copy that was stopped or that ended as a whole, the footer has **Try again** instead of **Compare with {target}**.
  - A check also counts "Fails {n}", its status line is "Checked 3 of 12…", and a check that was stopped is discarded.
  - **Additions (test findings):** a check holds only for what it checked (a counter drops a check that ran across a change of the keys, the target, an option, the kind or the source); **Stop** stands at the other end of the footer, since a double click on **Copy** landed on it; the second click of a double click on **Check** does not copy; **Compare with {target}** compares the keys that were copied; the group of **Overwrite** and **Skip** has an accessible name; the plan takes the focus after a check; the table of documents scrolls sideways on a phone instead of breaking a key into letters.
  - **Addition (review finding):** the dialog closes when the view of the route changes under it. A detail keeps its instance when only the view changes, so after the browser's **Back** a plan of another source stood with an enabled **Copy** to the production view.
  - **Addition:** the dialog emits `closed` after it left the screen, and both details then focus **More actions**: the menu item that opened the dialog is gone by then. The link of a written row closes nothing itself, so a link opened in a new tab leaves the result where it is.
- **The comparison page** takes `expected` as a snapshot when the dialog opens, and takes the keys that a copy wrote out of it, so **Try again** finds them unchanged.

### 8. Where the actions are

As planned: `app/composables/useViewTransfer.ts` and `useOtherViews.ts`, both bulk bars, both details, `app/utils/row-selection.ts` (`shownSelection`, `mergeShownSelection`, `selectedKeysOf`), `ConfigurationsMemory.selection` and `app/components/configuration/ConfigurationBulkBar.vue`.

- A selection whose keys do not fit in an address has no link: its menu item opens the comparison itself.
- A blocked button also points to the reason with `aria-describedby`. The annotations bulk bar disables both buttons while a bulk action runs.
- **Addition (test findings), `configurations.vue`:** the cells of the checkbox and of the type stay on the page background, as the pinned cells of the annotation grid: the tint of a selected row took the contrast of the **Context** badge below 4.5:1. The panel body of Configurations and of Annotations gets `sm:gap-0`: a strip of 24 px stood between the bulk bar and the table.

### 9. Tests

- **Unit:** eight new files in `test/unit/views/` (372 tests: `view-documents`, `line-diff`, `view-compare`, `limiter`, `row-selection`, `compare-selection`, `view-copy`, `view-copy-dialog`), `test/unit/configuration/configuration-bulk-bar.test.ts` (19), `test/unit/components/view-transfer-menu.test.ts` (18) and ten new tests in `annotation-bulk-bar.test.ts`.
  - **Deviation.** The Configurations page is mounted in `configuration-bulk-bar.test.ts` after all: with `offsetHeight` and `offsetWidth` stubbed, the virtualized table renders its rows in happy-dom.
  - The page tests stub an `IntersectionObserver` that reports every element as visible; one keeps the silent one of happy-dom.
  - The tests of the run and of the dialog were checked against mutations of the code (66 of the run, about 20 of the review fixes): each made a test fail.
- **End to end:** `test/e2e/views.spec.ts`, 22 tests per project (12 skipped on mobile): the selection of Configurations, both comparisons, own and effective, a long selection across a reload, the one-sided document in the unified layout, the copies from both bars, both details and the comparison page, the production view, **Stop**, the focus after the dialog, and the dialog closing with the view. `settings.spec.ts` reads the Type cell as the second cell.
- **Accessibility:** the comparison with its diffs drawn and the copy dialog (options, plan, result, the production target) in `accessibility.spec.ts`, light and dark, desktop and mobile. Two limits, both older than this change, are under Left for later.
- **Visual:** the two desktop `configurations` baselines are regenerated in Docker; no other screen changed.
- **Deployed-UI automation:** `ConfigurationsPage.CFG.cs` has `SelectColumn = 0` and its six columns at 1 to 6; the comments of `AnnotationBulkBar.cs` and `AnnotationDetail.cs` name the new buttons and menu items. No new case, no new test.
- **Addition, `src/Platform/Storyteller/Backend.CosmosDb/test/JsonPatchTests.cs`:** three tests of the API's `ApplyPatch` for the pair the copy sends: a `test` and a `replace` of the root replace the whole document (with members named `a/b` and `m~n`), a changed document fails as `PatchTestFailed`, and the same document with its members in another order and `2.0` written as `2` passes. The spec asked for one run against a real API for this; no API runs in this workspace, so the function of the API is tested instead.

### 10. Documentation

As planned: `docs/42for.net/platform/admin-ui.md` gets "Working with several views" (the comparison, the copy, their rules) as a part of its own after Configuration, since it covers annotations too, and the passages on the page memory, the bulk bars, both **More actions** menus and Modeling. `src/Platform/ui.admin/README.md` gets the e2e and a11y lists, the client state, the `ph-no-capture` list and two architecture notes.

### 11. Commits

As planned: the plan, the comparison, the copy, the documentation, this review. The fixes of the branch review are in the commit of the copy, which was not committed when the review ran. Three of them touch code of the comparison (`staleTime`, the stamp of an annotation, the reset of the page's copy).

### Test results

| Check | Result |
| --- | --- |
| `npm run lint` | passes (one warning, in `app/mock/key-value-handlers.ts`, older than this change) |
| `npm test` | 2,868 passed in 136 files, 419 of them new |
| `npm run build` | passes |
| `npm run test:e2e` | 512 passed, 26 skipped, 0 failed (538 tests). In my last run one unrelated test failed under load (`key-values.spec.ts:212`) and passed alone. |
| `npm run test:visual:docker` | 88 passed, with the two regenerated baselines |
| `dotnet build src/Platform/UI.Admin.AutomationTests/src` | 0 errors |
| `dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*JsonPatchTests"` | 17 passed |

Not run: the deployed-UI automation suite, and anything against a real API. The comparison and the copy ran only against the mock API, in the unit tests, in Playwright and by hand in the mock dev server (light, dark, phone).

### Reviews

- **The spec**, before any code, by four reviewers (feasibility, product, data safety, tests and scope): 41 findings, one blocker. The spec was rewritten with them before it was committed. The blocker: `buildPatch` writes a wrong member for a name with `/` or `~`, because the API takes the `~0` and `~1` of a JSON Pointer literally; the copy replaces the whole document instead, as `sform` and the MCP server do. Also from that review: the check and the production view, a document without members as none, the normalised annotation, integers beyond 2^53, a failed ancestor, **Stop**, `If-Match` and `If-None-Match`, `expected`, the target that must be a listed view, the deleted state, and commits that pass on their own.
- **The branch**, by four reviewers with a skeptic per finding: five findings confirmed and fixed, one refuted (a missing test, added all the same), three not verified and fixed.
  - Important: a check outlived a change of the source view, so **Copy** to the production view was enabled for a source that was never checked (section 7).
  - Important: a copy that found the target already equal left its section **Different** (section 7).
  - Minor: the copy dialog of the comparison page stayed open across a change of the comparison; reads stayed fresh for 30 seconds; the focus fell to the page after a copy from the comparison (sections 4 and 5).
- **The tests** found the rest named above as test findings, among them two that needed a real browser: the double clicks on **Copy** and on **Check**.

### Deviations from the spec

1. The run of the copy is `copyToView` in `app/utils/view-copy.ts`; `useViewCopy` is a thin composable over it.
2. The read of the target annotation has a query key of its own, and the reads of a comparison are never fresh.
3. An annotation without a readable ETag is updated under `If-Match: *`.
4. `expected` guards a write only, and a moved key is invalidated whatever the run does with it.
5. A target configuration with an integer beyond 2^53 fails; the spec refused only such a source.
6. **Stop** ends a run after a key, the title names a fixed target, and **Try again** replaces **Compare with {target}** after a copy that did not finish.
7. The dialog closes when the view of the route changes, and emits `closed`.
8. The Configurations page is mounted in a unit test, with a stubbed size.
9. Three tests of the API's JSON Patch stand in for the run against a real API.
10. The embedded diff tints its old side red and draws a one-sided document whole in the unified layout; Configurations and Annotations lose the strip below their bulk bar.

### Merge with read-only Modeling for Readers

Before the pull request was merged, `main` opened Modeling to Readers, read-only ([424-272](https://linear.app/424fun/issue/424-272/admin-ui-read-only-modeling-schemas-templates-deleted-for-readers), PR #216). The comparison of a selection is a page of Modeling and had no role check of its own, so it would have shown its copy buttons to a Reader. The branch merges `main` and follows the new rule: a comparison only reads, so every member compares; a copy writes, so it stays with Contributors.

- `useViewTransfer` has `canCopy` (`useCan('edit')`): its menu items hold **Copy to view** only from Contributor up. Both **More actions** menus show **Compare with view** to every role, also disabled with its reason in a project with one view.
- `compare.selection.vue` shows the copy of the page and of each section, and mounts its `ViewCopyDialog`, only from Contributor up. For a Reader its legend says nothing of a copy, the caption of the source side is "this view" instead of "after a copy", and the empty state points to **More actions** of a detail.
- The bulk bars stay with Contributors, as every bulk action and as case ANN-ROL-03 asserts.
- Tests: two Reader tests of the menus are rewritten and one added (`view-transfer-menu.test.ts`), three Reader tests of the page (`compare-selection.test.ts`), and the end-to-end case "a Reader compares, and gets no copy" (`views.spec.ts`, desktop and mobile).
- Docs: the role rules in `admin-ui.md` and the README.

After the merge: `npm test` 2,935 passed in 140 files, `npm run test:e2e` 542 passed and 26 skipped, `npm run test:visual:docker` 88 passed, lint, type check and build pass, and the automation project builds.

### Left for later

- **A run against a real API.** The mock differs from the API where the spec names it (a configuration write without an annotation, the members of an ancestor that the API creates, `If-Match` and `ETag`). The copy avoids each of these paths, but none was exercised against the API.
- **Contrast of the tinted diff lines in the light theme.** Syntax colors on changed lines have less than 4.5:1 (a string 3.5:1, deleted text of the unified layout 2.7:1), in every `DiffEditor`. No a11y case drew a diff before; the new cases leave the tinted lines out in the light theme. A fix needs other tints or other syntax colors.
- **axe does not check contrast inside a modal.** While a modal is open the body has no height, and axe takes its content for hidden. The cases of the copy dialog give the body a height; `expectAccessible` could do it for every dialog.
- **Accessible names of two radio groups** in `ExportPanel.vue` and `ImportPanel.vue`: `URadioGroup` does not take the label of its form field.
- **`AnnotationGrid.vue`** passes `enable-row-selection`, `enable-sorting`, `enable-column-resizing` and `column-resize-mode` to `UTable`, which declares none of them; they probably have no effect. Read from the sources, not run.
- **The bulk bars below 768 px.** A selection made in a wide window keeps the bar above the list of a narrow one, on Annotations as before and now on Configurations.
- **New Testiny cases** of project `2SP` and their automation tests: a batch comparison of annotations, a batch copy of configurations with its check, copy and compare from each detail, and the two actions without another view.
- Everything under Out of scope of the spec.
