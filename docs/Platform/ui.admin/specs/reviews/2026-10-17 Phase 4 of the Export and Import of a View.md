# Phase 4 of the Export and Import of a View

## Overview

Phase 4 covers the Administration UI (sections 4.1-4.6) and the public documentation of [Export and Import of a View](../../../Storyteller/specs/2026-10-15%20Export%20and%20Import%20of%20a%20View.md), Linear 424-40. A view now has an **Export & Import** page: a sidebar entry after **Compare views**, a command palette page and `g e`. The Export section picks the kinds and the format and downloads the zip when the job is ready. The Import section takes a zip, the kinds, Overwrite or Skip and force, and offers Check and Import. Below them, the result of the view's latest import shows the counts per kind, filter chips and the documents with their warnings and links, and **Import now** after a check. The Export and Import buttons of the Configurations page are gone, and so are its three dialog components. The transfer store passes `include`, `existing`, `force` and `dryRun` to the view transfer operations of the SDK (Phase 2), and it keeps the latest import of each view across a reload. `TransferToaster` moves to `components/transfer/`, links a finished import to the page, and refreshes the queries of the kinds an import wrote. The mock API exports and imports all four kinds as the API does, with a TypeScript port of `ViewArchiveNames`. The work is in `ef206566` (store and mock API), `43480e93` (page, panels, toaster, invalidation), `7b23ff56` (visual baselines), `989a798b` (documentation, shared with Phase 3) and `82a092f5` (review fixes). The CLI (Phase 3) is reviewed separately.

## What Was Done

### 4.1 The Export & Import page (`pages/[org]/[project]/[view]/export-import.vue`)

- **Navigation:**
  - `utils/navigation.ts` adds **Export & Import** after **Compare views** in the view section, with the icon `i-lucide-arrow-down-up` and `editRole: 'Contributor'`. A Reader sees it locked and can still export.
  - The command palette lists it without a change of its own, because its pages come from `buildNavigation`.
  - `g e` opens it (`components/shell/CommandPalette.vue`), and the shortcut help lists **G E** (`composables/useShortcutsHelp.ts`). No other shortcut used these keys, and a unit test checks that the help has no duplicate keys.
  - **Addition beyond the spec:** `export-import` is one of the view sections of `utils/context-paths.ts` (`viewSections`), so switching the view stays on the page.
- **Layout:**
  - The **Export** and **Import** sections sit in a two-column grid from the `lg` breakpoint and are stacked below it.
  - Below them, full width, the card **Latest import** (`ImportResult`) shows the view's latest import once it has succeeded (`latestImport` in the phase `done`). While a newer import or check runs, and after one fails, the card is hidden, and the Import section shows the progress or the error.
  - Both panels are keyed by the scope, so switching the view starts them with fresh options.
- While the page shows an import, it marks it with `showInPage(id)`, so the toaster gives it no toast. The watcher runs with `flush: 'sync'` (see [Review](#review), item 2).
- **The Configurations page** (`configurations.vue`) lost its two header buttons, `canEdit`, the open flags and both dialogs; its header is the plain `AppNavbar`.
  - `components/configuration/ConfigurationExportDialog.vue`, `ConfigurationImportDialog.vue` and `ConfigurationImportResult.vue` are deleted. Their reusable parts (the format cards, the progress, the alerts and the outcome badges) moved into the panels and `utils/transfers.ts`.
  - In `ef206566` the dialogs still served the new store API, with all four kinds and new texts. `43480e93` replaced them.
- The build regenerated `components.d.ts` and `typed-router.d.ts` with the new components and route.

### 4.2 Export section (`components/transfer/ExportPanel.vue`)

- **Kinds:** a checkbox group of the four kinds, all checked, each with a line on what it holds (`contentItems` in `utils/transfers.ts`). With none checked, the field says "Pick at least one kind." and **Export** is disabled.
- **Format:** radio cards **Folder of JSON files** and **Git repository**, each with a description of its layout.
- The line of `neverExported`: "Never included: secrets, effective documents, the audit, the version history of annotations, and deleted annotations."
- **Export** sends the checked kinds in import order (`archiveContents`).
  - The progress shows in place: "Waiting for the server…", "Building the archive…", then "Downloading the archive…".
  - The store downloads the zip when it is ready, also after the user has left the page. The section then says "`<file name>` was downloaded."
  - A failure shows "The export failed" with the error.
- **Refinement:** the section reads the view's latest export through `computed(() => transfers.latestExport(scope))`. So it shows an export started before the user left the page, and one that the store follows again after a reload (see [Review](#review), item 1).

### 4.3 Import section (`components/transfer/ImportPanel.vue`)

- **Who:** without `useCan('edit')`, that is below `Contributor`, an alert replaces the form. Its title is "Importing needs the Contributor role", and its description says "You can export the view; ask a Contributor of the project to import an archive."
- **The file:** `UFileUpload` for one `.zip`, with the hint of the spec as its description.
- **Kinds:** the same four checkboxes, described as "What the import may write." At least one is required.
- **Existing documents:** radio cards **Overwrite** (the default) and **Skip**, with the texts of the spec.
- **Force:** the checkbox with the label and the description of the spec.
- **Check** starts a dry run and **Import** a real one, with the checked kinds in import order, the mode and force. Both are disabled without a file or a kind, and while a run starts or runs.
- **Progress:** a determinate bar with "N of M files checked…" or "… imported…" once the job reports `TotalCount`, an indeterminate one before it. It adds "You can leave this page; the import continues." Upload errors (`400 InvalidArchive`, `409 TransferLimit`, `413 ArchiveTooLarge`) and failed jobs show as "The import failed".
- **Deviation:** **Import now** is an action of the result's banner, not a third button of the Import section.
  - The section exposes `importNow()` and `starting` (`defineExpose`), and the page calls it. So the progress and the start errors still show in the Import section.
  - `importNow()` uploads the job's stored file again with the job's `Include`, `Existing` and `IsForced`, not with the current values of the form.
  - Reason: the action sits next to the result the user has just reviewed, and the form stays free for another file.
- An import restored after a reload has no `File`, so the page offers **Import now** only once the zip is picked again.

### 4.4 Result (`components/transfer/ImportResult.vue`)

- **Banner:** "Checked: nothing was written" (info) or "Imported" (success), in the warning color when a file failed. It carries the file name and the counts (`importCounts`).
- **Summary:**
  - On desktop, a table with one row per entry of `Summaries` and a **Total** row. The columns are Created, Updated, Unchanged, Skipped, Failed and Warnings, plus Excluded files and Ignored files, which only the total fills.
  - A dry run says "To create", "To update", "To skip" and "Fails" in the headers and in the badges.
  - Phones get a list of lines instead ("2 to create, 5 unchanged, 1 warning").
- **Filters:** chips by kind and by outcome (**Failed**, **Skipped**, **Warnings**), each with its count. A chip shows only when it matches documents. Chips of one group combine with *or*, the two groups with *and*, and **Clear the filter** removes them. The filters reset when another import is shown.
- **Documents:** the items of the job.
  - Desktop has a `UTable`: the kind badge (label and icon from `documentKinds`), the document, the outcome, the version, the code and the message. The warnings, with their code, are listed under the message.
  - Phones get a list with the same content.
- **Refinements:**
  - The document cell shows its name (`documentName`: the key, the type code, or `<type> below <key>` for a descendant-type schema) and the file path below it.
  - The first 200 documents are rendered, and **Show all N** renders the rest; the API caps the items at 1,000. The truncation note of PR #88 stays.
  - Without items, a line says that every document equals the stored one, or that the archive holds no document of the included kinds.
- **Links** (`documentPath`) follow the table of 4.4 and were checked against the real routes.
  - **Refinement:** `documentExists` decides when a document is linked: `Created` in a real run, `Updated` (in a dry run too, since the document exists), and `Skipped` with `DocumentExists`. Failed documents, other skips and the creates of a dry run get no link.
- **Deviation:** "after a reload" is a real browser reload, not only a return to the page, which is what the dialog of PR #88 survived. See 4.5.

### 4.5 Store, toaster and caches

**`stores/transfers.ts`:**
- `startExport(scope, { format, include })` and `startImport(scope, file, { include, existing, force, dryRun })` take the new types `ExportOptions` and `ImportOptions`; every import option is optional. Options that are left out are not sent, so the API's defaults apply, and an empty include is sent as none, which means all four kinds.
- The polling, the 15-minute limit, the download and the resume of pending exports are unchanged.
- **API changes:** `latestExport(scope)` is new, `showInDialog` is renamed `showInPage`, and `ImportJob.file` is optional.
- **Deviation:** the latest import of each view survives a reload. It is saved in `sessionStorage` (`2s-admin:latest-imports`: the id, the scope, the start and whether it had finished), and `resume()` reads it again from the server:
  - an import that had finished before the reload is shown without a toast;
  - one that was still running is followed, and its end is reported unless the page shows it;
  - one that has expired (24 hours, or a `404`), cannot be read, or was replaced by a newer import of the view is forgotten.

**`components/transfer/TransferToaster.vue`** (moved from `components/configuration/`, still in `DefaultLayout`):
- A finished import that no page shows gets a toast: "Imported `<file>`" or "Checked `<file>`" with the counts, or "The import failed". It has **Show the result**, a link to the page of the import's view (`exportImportPath`).
- **Refinement:** the export toast names the counts of the included kinds (`exportCounts`, for example "Exported 12 annotations and 2 templates") instead of "Exported N configurations". The API leaves out the counts of the kinds that were not included.

**Caches** (`queries/invalidation.ts`): the new `invalidateImport(client, scope, state)` reads the written kinds from the summaries per kind and from the items (`writtenContents`), refreshes their queries and returns the kinds. A dry run refreshes nothing. `invalidateAnnotations` and `invalidateTemplates` are new helpers.

| Written | Refreshed |
| --- | --- |
| Annotations | Every annotation query of the view. The toaster reloads the catalog when it shows that view. |
| Schemas | The schema queries (`invalidateSchemas`) and every configuration query |
| Templates | The template queries (`invalidateTemplates`) and every configuration query, which holds the effective documents |
| Configurations | The written keys (`invalidateConfiguration`). Every configuration query when the items are truncated or annotations were re-created (`AnnotationRecreated`). The catalog refreshes its configurations when it shows that view. |

- **Refinements of what the spec left open:**
  - every configuration query is also refreshed after a truncated list or a re-create, which empties a configuration;
  - the catalog is reloaded only when it shows the imported view;
  - `catalog.refreshConfigurations()` runs only when configurations were written; before, any written document triggered it.
- This resolves the note of the Phase 1 and Phase 2 reviews: template and type-schema items no longer reach `invalidateConfiguration` with an undefined key, and annotation items are no longer treated as configurations.

### 4.6 Mock API, tests

**Mock API** (`ef206566`), which resolves the mock notes of the Phase 2 review:
- `mock/archive-names.ts` (new) is a TypeScript port of `ViewArchiveNames`: the builders per kind and version, the reserved second segment, `parseArchiveName`, `canDescend`, `identityOf`, `isSkippedPath` and `getArchiveFileName`. It replaces the mock's `fileNameOf` and `parseFileName`.
- `mock/zip.ts`: an entry can carry its own time (`ZipFile.modified`), so a version file has the time of its version.
- `mock/transfer-handlers.ts` is rewritten over the mock database (annotations, the `template:` and `schema:` documents, configurations):
  - **folder export:** one folder per included kind, version files next to their current file, and `export.json` manifest version 2;
  - **git export:** the same folders with the current documents and `.git/HEAD`, without `export.json`, as the spec keeps it;
  - **export state:** the counts of the included kinds and `VersionCount`. The counts of the other kinds are left out, as the API's JSON leaves out nulls, so the existing unit test now expects them absent instead of `null`;
  - **reading:** names in any folder; hidden paths and `__MACOSX` skipped without being counted; version files, `export.json` and other names ignored; excluded kinds counted; duplicates across folders and spellings fail with `DuplicateFile`; comments and trailing commas allowed;
  - **passes:** the API's order (annotations with ancestors first, then schemas, templates and configurations), with the overlay, `Existing`, force and the dry run;
  - **outcomes:** the API's codes, messages and warnings: `DocumentExists`, `DependencyFailed`, `AnnotationDeleted`, `AnnotationNotFound`, `EmptyDocument`, `InvalidSchema`, `InvalidKey`, `SchemaViolation` (failed, or a warning with force), `InvalidDocument`, `InvalidAnnotation`, `AncestorCreated` and `AnnotationRecreated`. The job has the summaries per kind with `Content` and `Warnings`, and the total with `ExcludedFiles` and `IgnoredFiles`. Items carry `Kind`, `AnnotationType` and `AnnotationKey`.
  - **Addition** (`43480e93`): the last 10 finished imports are kept in `sessionStorage` (`2s-admin:mock-finished-imports`), so the page shows a result again after a reload in mock mode too. Before, the mock's jobs lived only in memory.
- `mock/annotation-handlers.ts` exports `storeAnnotation` (the former private `store`), `byDepthTypeKey`, and the new `annotationProblems` (the length checks), which the import reuses.
- **Fix beyond the spec** (`mock/documents.ts`): only seeded annotations (`Created.By.Kind === 'System'`) get a generated configuration. Before, an annotation that an import created, and whose key hashed into the generated third, got a configuration with versions. So a real run reported other outcomes than its dry run. The annotation, configuration, deleted and accessibility e2e specs still pass.
- **Simplifications of the mock**, documented in `export-import.md` (Mock API):
  - `$remove` and `$patch` are not applied to imported templates and configurations, and duplicate JSON properties are not detected;
  - the document and archive limits are not enforced, and the items are never truncated;
  - `deleteDocument` drops the versions. So an export never lists a deleted schema, template or configuration, the git export has no remove step, and the configuration of a re-created annotation starts again at version 1;
  - the `InvalidSchema` message comes from the client-side meta-schema check, and the message for an annotation member of a wrong type is the mock's own.

**Vitest** (`test/unit/transfers/`, which replaces `test/unit/configuration/transfers.test.ts`):
- `archive-names.test.ts` (new, 153 tests): every case of `ViewArchiveNamesTests.cs`.
- `mock-transfers.test.ts` (new, 14):
  - the export layout, include and the manifest, and the git export;
  - a round trip, flat and hidden paths, and a copy into another view whose dry run matches the real run;
  - `AncestorCreated`, Skip, the excluded counts, the overlay of a schema and its configurations with force, `DependencyFailed`, re-create versus Skip, duplicates and invalid files, and the annotation rules.
- `transfers.test.ts` (moved, 20): the zip helpers, each store option reaching the request with the defaults left out, `latestExport`, the restore after a reload (finished, running during the reload, expired or replaced), and the mock's options and its kept finished imports.
- `transfer-utils.test.ts` (new, 12): names and links per kind, when a document is linked, the export counts, the written kinds, re-creates, and `invalidateImport` per kind without an undefined key.
- `transfer-page.test.ts` (new, 11):
  - ExportPanel: the include, the at-least-one rule, and an export resumed after a reload;
  - ImportPanel: the reader notice, and a check followed by Import now with the same options;
  - ImportResult: the banner, the summary rows, the documents, the links and the chips;
  - TransferToaster: the link, the invalidation per kind, and no toast for an import the page shows;
  - the page itself, with an import that ended during a reload.
- `test/unit/utils/navigation.test.ts` (3 new): the position, icon and role of the entry and its lock for a Reader; the view section kept on a view switch; and **G E** in the help without clashes.

**Playwright** (`test/e2e/transfers.spec.ts`, rewritten, 8 tests per project):
- the page from the sidebar (on phones from the menu) and with `g e`, and a Configurations page without transfer buttons;
- an export of only annotations and templates: the zip name, the folders, `Include` of `export.json` and the toast;
- a git export;
- the at-least-one-kind rule;
- a check of an edited annotation, a new execution below missing ancestors, an edited template and schema, a version file and `export.json`: the summary per kind, `AncestorCreated`, the Warnings chip, **Import now**, the links, and the edited title on the annotation page;
- an import with Skip that reports `DocumentExists`, the Skipped chip, and the same result after `page.reload()`;
- a Reader exports and sees the import notice;
- a file that is not a zip is refused.

The check-then-Import-now flow and the non-zip refusal are skipped on phones, as the import flows were before this phase; the Skip test covers the phone list. In `accessibility.spec.ts`, the two dialog tests are replaced by axe on the page, on the page of a Reader (`/northwind/portal/default/export-import`) and on a dry-run result, in both themes and both projects.

**Visual baselines** (`7b23ff56`):
- `visual.spec.ts` captures `/acme/billing/default/export-import` as it opens, after `schemas`. That adds four baselines: desktop and mobile, light and dark.
- 22 desktop baselines of pages with the view sidebar were regenerated. On 20 of them only the sidebar changed: the new entry, and the items below it 32 px lower. The two desktop Configurations baselines also lose the header buttons.
- **Deviation:** the spec says the Configurations baseline changes; only the desktop ones did. The mobile ones were captured in `7797b22d` without the two icon-only buttons and were never rewritten after the buttons came: `a438abbc` regenerated only the desktop ones, and the icons stayed within the tolerance on phones. So they already match the page without the buttons. Phones also hide the sidebar, so every other mobile baseline is unchanged too.
- The import result has no visual baseline. The e2e flows and axe cover it.

### Documentation

`989a798b` and `82a092f5`:
- `docs/Platform/Storyteller/export-import.md`:
  - The intro lists the code of the CLI and the admin UI. The "State of the delivery" paragraph is removed, since every phase is done.
  - **Administration UI** is rewritten: the navigation, the layout, the Export and Import sections, the result with its link table, the store and its `sessionStorage` restore, the toaster, the invalidation table, the mock API with its simplifications, and the tests. The review fixes added the export resumed after a reload, the synchronous mark, and when **Latest import** shows.
  - The CLI section belongs to Phase 3.
- `annotation-lifecycle.md`: its CLI section points to `sform story export` and `import`. Its import rules and `templating.md` (an import replaces a template) date from Phase 1.
- Public site (`docs/42for.net/platform/`):
  - `admin-ui.md`: the configuration-only "Export and import" part under Configuration is replaced by its own **Export and import** section after Graph. The keyboard table gains `G` then `E`.
  - `configuration.md`: the four kinds, the file names by kind, the folder and git layouts, and how an import behaves (the order, the overlay, Overwrite or Skip, the dry run, no deletes, no history). It also says what never travels and has `sform story export`/`import` examples.
  - `annotations.md`: a new **Export and import** section: what an annotation file holds, the unit type and definition included (review fix), CLI examples, and how an import treats annotations.
  - `using-the-platform.md`: a new **Exporting and importing a view** section with the routes, the options, the roles, the scopes and CLI examples.
  - **Fix beyond the spec:** `using-the-platform.md` said schemas are per project and listed their routes without `{view}`. They are per view since `d20c3688`, and the new section would have contradicted it.
- **Addition:** `src/Platform/ui.admin/README.md` lists the page in its e2e coverage and in the architecture notes.

## Tests

All checks ran in `src/Platform/ui.admin/src`, against the TypeScript SDK of Phase 2 (`open.api.v0.9.21.json`), built and installed. No backend, API or SDK code changed in this phase. The results are those after the review fixes (`82a092f5`).

| Check | Result |
| --- | --- |
| `npm run typecheck` (vue-tsc) | pass |
| `npm run lint` (oxlint, then ESLint) | pass, no findings; Prettier is clean on the changed files |
| `npm test` (Vitest) | 635 pass in 52 files (436 in 48 after Phase 2). The transfer suites: `archive-names.test.ts` 153, `transfers.test.ts` 20, `mock-transfers.test.ts` 14, `transfer-utils.test.ts` 12, `transfer-page.test.ts` 11; `navigation.test.ts` has 3 new. |
| `npm run build` | pass |
| Playwright, desktop and mobile, full suite | 199 pass, 5 skipped: the 2 transfer tests that run on desktop only, and 3 older annotation tests that run in one viewport only |
| Playwright `transfers.spec.ts`, `--repeat-each=4` | 56 pass, 8 skipped, 0 failed; before the fix of the flaky toast check, the export test failed 2 of 8 runs |
| Visual, in Docker (`mcr.microsoft.com/playwright:v1.64.0-noble`) | At `7b23ff56`, the first compare failed 26 of 56, all expected (the 22 desktop pages with the sidebar and the 4 missing baselines). `--update` rewrote those 22 and added the 4. The second compare and the compare after the review fixes passed 56 of 56. |

Port 4174 was free before each Playwright run. The runs left no generated noise: `auto-imports.d.ts` and `typed-router.d.ts` were rewritten with other line endings only, which git does not report, and `package-lock.json` is unchanged.

## Review

Fixed (`82a092f5`):

1. **After a reload, the Export section did not show an export that was still running, and offered Export again** (reported twice).
   - The cause: `ExportPanel` took the job id once, when it was set up, but `TransferToaster` resumes the pending exports only after the page is mounted.
   - The fix: `job = computed(() => transfers.latestExport(props.scope))`. `startExport` makes the new job the latest one, so `start()` assigns nothing.
   - Test: "shows an export that the store follows again after a reload" mounts the panel first, then calls `resume()` with a saved pending export. It expects the progress and a disabled **Export**, then "was downloaded.", one `saveBlob` call and **Export** enabled again. It fails on the old panel.
2. **An import that ended during a reload got a toast, although the page showed it** (reported twice).
   - The cause: `restoreImport` tracks the job and finishes it in one step. The page's `showInPage` watcher ran with the default `pre` flush, after `finish()` had called the listeners, so the toaster did not find the job in `watched`.
   - The fix: the watcher runs with `flush: 'sync'`. It keeps the id-based `showInPage` API and is deterministic, which an `await nextTick()` in the store would not be.
   - Test: "the Export & Import page > shows an import that ended during a reload, and the toaster does not report it". It saves a running import, mounts the real page and the toaster, and expects the mark, the banner "Checked: nothing was written" and no toast. It fails with the `pre` flush.
3. **The e2e test "exports only the annotations and templates" was flaky.** The export toast shows for 5 seconds and could expire before the test checked it at the end. `exportView` takes a `toast` pattern and checks it right after "was downloaded.", before it reads the file. The toast duration of the product is unchanged.
4. **The public annotations page left the unit type and definition out of what an annotation file holds.** `annotations.md` now names them. It also says that an import replaces the content, so a field the file leaves out is cleared, and that a unit without its type or definition gets the defaults of a new unit (`event`, `unknown`), as `ViewAnnotationDocument` does.
5. **`export-import.md` described "Latest import" as the latest finished import,** but the page hides the result while a newer import runs and after one fails. The doc changed, not the code, which follows spec 4.1: the Layout bullet now says when the section shows.

Not fixed:

- **No review documents for Phases 3 and 4:** skipped by the fix step, which owns no file under `specs/`. This file is the Phase 4 review; the Phase 3 review is written in its own step.

## Open Points

- **Mock reset:** `?mock-scenario=` and `window.storytellerMock.reset()` reset the data, but not `2s-admin:mock-finished-imports` or `2s-admin:latest-imports`. So the page can show a result from before the reset. The e2e tests are not affected, because each test gets a fresh browser context.
- **A restored running import** is followed from its original start. When more than 15 minutes have passed, it ends at once with "did not end within 15 minutes", as pending exports already do.
- **Mock speed:** in Node, importing the whole default view into another view through the mock takes about 3 seconds, because `annotationsOf` (`mock/db.ts`) serializes the overrides on every call. A browser import of a large archive into another view is slow in the mock. The unit test copies one subject.
- **Sidebar height:** at 1440×900 the sidebar now overflows by two links, **Organizations** and **Account**, where before only **Account** was below the fold. The sidebar scrolls.
- **Baseline churn**, older than this phase: in the desktop annotations grid (annotations, annotation detail and history), a 1-px row border renders faint or full from run to run. It stays below Playwright's per-pixel threshold, so comparisons pass, but the PNGs churn when they are rewritten.
- **`package-lock.json`** still records the `file:` SDK as `0.8.91`. It was not touched, because no dependency changed.
- **Public docs:** the VitePress build of `docs/42for.net` was not run, so dead links and rendering are unverified; the internal link targets were checked by hand. `docs/42for.net/cli/sform.md` is still a stub.
- **Still open from Phase 1**, outside the UI: concurrency exhaustion fails the whole job instead of one file; descendant-type schemas that fail `CanDescend` can be written and exported and then fail on import with `InvalidKey`; `ViewArchiveReader.Validate` counts every kind at upload.
