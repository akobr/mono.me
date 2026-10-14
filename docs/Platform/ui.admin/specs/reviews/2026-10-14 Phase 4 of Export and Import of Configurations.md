# Phase 4 of Export and Import of Configurations

## Overview

Phase 4 covers the Administration UI (sections 4.1 to 4.4) of [Export and Import of Configurations](../../../Storyteller/specs/2026-10-14%20Export%20and%20Import%20of%20Configurations.md), Linear 424-15. It builds on the endpoints of Phase 2 and the regenerated TypeScript SDK (`open.api.v0.9.1.json`, with zip bodies as `Blob`).

- **Configurations page:** **Export** for every role and **Import** for Contributors in the header.
- **Export:** a dialog picks the folder or the git format. The browser downloads the zip under the server's name, with the UTC time stamp, once the server has built it, even if the dialog was closed or the user left the page.
- **Import:** a dialog takes a zip, checks it first if asked, shows the progress and the outcome of every file, and imports the checked file with **Import now**.
- **Mock API:** both jobs run over the mock database, so `npm run dev:mock` and the end-to-end suites work offline.

## What Was Done

### 4.1 Entry points

- `pages/[org]/[project]/[view]/configurations.vue` adds two ghost buttons to the navbar's `right` slot, with tooltips and `aria-label`s (`Export configurations`, `Import configurations`). On phones they are icons only.
- Import shows only with `useCan('edit')`; the dialogs render only with a scope.

### 4.2 Export (`components/configuration/ConfigurationExportDialog.vue`)

- A card radio group (**Folder**, **Git repository**) with a description of each layout, and **Export**.
- **Deviation:** the dialog stays open while the job runs, with an indeterminate progress bar and the phase (*Waiting for the server…*, *Building the archive…*, *Downloading the archive…*). The spec closed it at once. Seeing the export start is clearer, and closing it early still works: the export goes on and the toast reports it.
- The dialog closes when the download starts; a failure stays in the dialog as an alert.
- **`stores/transfers.ts`** (new) holds the jobs of the tab:
  - It starts exports and imports and polls them (1, 1, 1, 2, 2, then 3 seconds) until they end or 15 minutes pass. A `404` ends a job as expired, and three failed reads in a row end it with the error.
  - A succeeded export is fetched with `getConfigurationExportArchive` and saved by `utils/download.ts` (an object URL and a temporary link). The API needs the bearer token, so a plain link could not download it.
  - Running exports are written to `sessionStorage` (`2s-admin:pending-exports`) and resumed by `resume()` after a reload.
  - `onFinished` listeners and the set of jobs a dialog shows (`showInDialog`) connect it to the toaster.
- **`components/configuration/TransferToaster.vue`** (new, in `DefaultLayout`) resumes pending exports and reports every finished export: *Exported N configurations* with the file name, or the error.

### 4.3 Import (`components/configuration/ConfigurationImportDialog.vue`)

- `UFileUpload` for one `.zip`, and the options *Only check, write nothing* and *Store documents that violate their schema*. The primary button is **Check** for a dry run and **Import** otherwise.
- While the job runs, a progress bar (determinate once the server reports `TotalCount`) and the files done.
- The result (`ConfigurationImportResult.vue`, new):
  - badges with the five counts and the ignored files;
  - a list of the files that were not unchanged, with an outcome badge (*To create*, *To update*, *Fails*, … for a dry run), the key, the message and the version;
  - a link to each written configuration;
  - the truncation note.
- After a dry run, **Import now** uploads the same `File` again without the dry run, keeping the force option. **New import** forgets the shown job.
- Opening the dialog again shows the view's latest import, running or finished. A dialog that shows an import registers it with the store, so the toaster gives that import no toast.
- Upload errors (`400 InvalidArchive`, `409 TransferLimit`, `413 ArchiveTooLarge`) and failed jobs are shown as an alert in the dialog.
- After an import that created or updated documents, the toaster invalidates the configuration queries of the written keys (or all configurations when the item list was truncated) and calls `catalog.refreshConfigurations()`.
- `utils/transfers.ts` formats the counts for the toast.

### 4.4 Mocks and tests

- **`mock/zip.ts`** (new): a zip writer (stored entries, UTF-8 names, CRC-32) and a reader (stored and deflated entries, through `DecompressionStream('deflate-raw')`).
- **`mock/transfer-handlers.ts`** (new), before the configuration handlers in `all-handlers.ts`:
  - A job is `Running` on its first read and ends on the second.
  - The export writes the folder layout from the stored documents, without deleted annotations. **Deviation:** the git format is the current documents plus `.git/HEAD`; the mock writes no repository.
  - The import parses the file names like the API (`fileNameOf`, `parseFileName`) and reports the same outcomes and error codes, schema validation included (`combinedSchema`, `schemaErrors`). Written documents get a version and a `ConfigurationChanged` audit entry.
  - Only the import requires `Contributor`.
- **Vitest** (`test/unit/configuration/transfers.test.ts`, 11 tests):
  - the zip helpers, including a deflated zip written by .NET;
  - the mock file names and the counts;
  - the store with fake timers and MSW: the export download and the pending list, a failed export, an expired job and the 15-minute limit, a resume after a reload, and an import with its query options and the latest import of a view.
- **Playwright** (`test/e2e/transfers.spec.ts`):
  - exports in both formats (the downloaded name, the files);
  - a dry run and then a real import of an edited export: an update, a key without an annotation, a broken file and an ignored file;
  - a reader's page without Import;
  - a non-zip upload.

  Two new cases in `accessibility.spec.ts` run axe on both dialogs.

| Check | Result |
| --- | --- |
| `npm test` | 319 pass (34 files) |
| `npm run test:e2e` (desktop and mobile) | 91 pass, 5 skipped (the import cases run on desktop only) |
| `npm run typecheck`, `npm run lint`, `npm run build` | pass |

The visual screenshots were not run (they need Docker); no screen they cover changed except the Configurations header, which none of them captures.

The dialogs were also tried in the dev server with the mock API (`--mode e2e`, port 5194): both open, and a git export downloaded and closed its dialog.

### Documentation

- `docs/42for.net/platform/admin-ui.md`: *Export and import* below Configuration.
- `docs/42for.net/platform/configuration.md`: an *Export and import* section.
- `docs/Platform/Storyteller/configuration-transfer.md`: an *Administration UI* section.
