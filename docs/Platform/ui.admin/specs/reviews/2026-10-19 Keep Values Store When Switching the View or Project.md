# Keep Values Store When Switching the View or Project

Linear: [424-68](https://linear.app/424fun/issue/424-68/keep-values-store-when-switching-the-view-or-project)

## Overview

This review covers the whole spec [2026-10-19 Keep Values Store When Switching the View or Project](../2026-10-19%20Keep%20Values%20Store%20When%20Switching%20the%20View%20or%20Project.md), delivered in one phase. The switcher in the top bar now keeps **Values store** when it changes the view or the project, and drops the open key, its versions and the query. Page memory still leaves Values store out. The Values store page now stays mounted through such a switch, so it clears its selection when the project changes too.

There is one addition and one deviation from the plan:

- **Addition:** a review round found that **Include secret values** in the Export menu stayed checked across a switch. It is now cleared too (section 3).
- **Deviation:** the end-to-end test runs its steps in a different order. In the planned order, the step meant to test section 3 also changed the view, so it would have passed without the fix (section 5).

## What Was Done

### 1. Values store as a view section (`context-paths.ts`)

As planned:

- `viewSections` is `annotations`, `configurations`, `key-values`, `graph`, `modeling`, in the order of the menu.
- The comment above it says that Values store is `key-values`.
- No other code changed. `ViewSection` has no exhaustive `switch` or `Record`. `vue-tsc --build` passes.

`test/unit/utils/shell.test.ts` has these new cases:

- An `it.each` over the four paths of the spec's table. Each one checks `currentSection()`, `viewTarget()` and `projectTarget()`.
- "tells the list of Values store from a key". `viewLocation()` returns `root: true` for the list, including with `?q=`, and `root: false` for a key and for its versions.
- Two more assertions in "tells the Monitor page from a view named monitor", for `/acme/billing/monitor/key-values`.

With `key-values` taken out of `viewSections`, all six of these tests fail.

### 2. Page memory stays as it is

As planned, the guard and the store did not change. In `test/unit/router/page-memory.test.ts`:

- **"view locations":** reads the Values store list and the versions of a key. The spec named a plain key, and the versions path also covers a deeper path.
- **"leaves Values store out of the memory":** a new case after "keeps each section and each view apart". It has three checks:
  1. A key with `?q=` is left, and the plain list is opened again. The list stays the plain list.
  2. The switcher goes to Values store of another view. In that view, a key and Annotations had been left open, and the list still stays the plain list. The first check of the spec did not open a key, so it would also pass if Values store were remembered. The key makes the check fail in that case.
  3. From Values store to Annotations of the same view, the remembered Annotations location still comes back.

Checks against broken code:

- With `key-values` in `rememberedSections`, the new case fails.
- With the section comparison removed from the origin check, it fails, and so does "keeps each section and each view apart".

The new case is placed away from the lines that 424-67 changes, and that file merges cleanly with `feat/424-67`.

### 3. Clear the state of Values store when the project changes

**The selection (`key-values.vue`):** done as planned. The watch is now `watch([state, () => context.projectKey, () => context.view], …)`, with a comment that says why. The long lines of the file were not reformatted. The file was not Prettier-formatted on `main` either.

**Addition: the secret values of Export (`KeyValueExportMenu.vue`).** The review round found a second piece of state that the page now keeps. The menu lives in the navbar of the page, so it stays mounted through a switch, and so does its `includeSecrets` ref. Take an Administrator who ticks **Include secret values** in `staging`, downloads, and then switches to `default`, the production view, or to another project. Their next `.env` or JSON export there would have included the secret values without being asked for again. Before this change, the switcher always went to Annotations, which unmounted the menu.

The menu now watches `context.projectKey` and `context.view` and clears `includeSecrets` when either changes.

The rest of the page follows the context, as the spec says:

- the queries;
- the actions, which read `context.scope` when they run;
- the dialogs, which reset when they open;
- the filter, the search and the state, which come from the query;
- the detail, a child route.

### 4. Unit tests

`test/unit/key-values/key-values-page.test.ts` is a new file. It mounts a `RouterView` with the generated routes, not the page itself. The page's own `<RouterView />` then renders the detail as in the app, and a switch reuses the page's component the same way. `contextGuard` follows the route, MSW serves the mock API, and `AppNavbar` is replaced by a stand-in that renders the right slot, so Export is there.

The file has three tests:

- **"clears the selection when the project changes to a view of the same name":** select `db.host`, push `/acme/shipping/default/key-values`. The bulk bar is gone, and the empty state of `acme/shipping` shows. The planned check that the list shows the pairs of the new scope is this empty state.
- **"clears the selection when the view changes":** the same with `/acme/billing/staging/key-values`. It shows `sql.staging` with `db.host` unchecked.
- **"asks again for the secret values of an export in another project or view":** tick **Include secret values**, switch the project, then the view. The item is unchecked after each switch.

Without `context.projectKey` in the watch, the first test fails. Without the watch in the menu, the third fails. Three runs in a row passed.

### 5. End-to-end test (`test/e2e/key-values.spec.ts`)

**Deviation.** The test is "the switcher keeps Values store, without the key and the selection", on desktop only. Its steps run in this order:

1. Open `/acme/billing/staging/key-values/db.host/versions`. The detail (`#key-value-detail`) is visible.
2. Pick `default` in the view switcher. The URL is `/acme/billing/default/key-values`, the detail is gone, and the row of `db.host` shows `sql.internal`.
3. Select `db.host`. The "Bulk actions" toolbar shows "1 selected".
4. Pick `shipping` in the project switcher. The URL is `/acme/shipping/default/key-values`, "No key values in this view" shows, and "1 selected" is gone.

The spec started on `default` and switched to `staging` first. Its project switch then went from `staging` to `default` of `shipping`, which changes the view as well. The old watch already cleared the selection on that step, so the test could not fail without section 3. In this order, the project switch keeps the view `default`.

The checks that something is gone do not use role queries. While a menu of the switcher closes, reka-ui hides the rest of the page from the accessibility tree (`aria-hidden`). The first version checked `getByRole('toolbar', { name: 'Bulk actions' })` with `toHaveCount(0)`, and it passed without the fix, during that moment. The test now checks the detail by its id and the selection by the text "1 selected". It fails without section 3, and it passed three runs in a row with it.

### 6. Documentation

- `docs/Platform/Storyteller/key-values.md`, Admin UI, Values store: the switcher keeps Values store when it changes the view or the project. The open key closes, the selection is cleared, and an export asks again for the secret values.
- `src/Platform/ui.admin/README.md`:
  - The Values store entry: `key-values` is a view section in `utils/context-paths.ts`, outside page memory. The page stays mounted through a switch, so the list clears its selection, and Export its request for the secret values.
  - The e2e list: the new flow.
- `docs/42for.net/platform/admin-ui.md` already said that the switcher keeps the screen, and it did not change.

### Results

- `npm test`: 62 files, 868 tests passed.
- `npm run typecheck` and `npm run lint` (oxlint and ESLint) report no problems. Prettier passes on the new and changed tests.
- `key-values` and `navigation` e2e on desktop and mobile, with `PLAYWRIGHT_CHANNEL=chrome`:
  - before the Export fix: 83 passed, 3 skipped;
  - `key-values` after it: 25 passed, 1 skipped.

  The skipped tests are those of the view switcher on a phone, which has no view switcher.

### Notes

- **Review round.** Four reviewers looked at the paths, the state that survives a switch, the page memory, and the tests and docs, and each finding was checked by a skeptic. Their findings:
  - the Export opt-in, fixed in section 3;
  - the order of the e2e steps and the missing review, both in this document;
  - a merge conflict with `feat/424-67`, described below.
- **Rejected as out of scope.** Two findings were refuted because they exist on `main` already. A confirmation that is open (the bulk Delete or Change protection, the row Delete, and Delete permanently) stays open through browser Back or Forward. When the user confirms, the action uses the scope of the page now shown. On `main`, the same can be reached through Forward and through Modeling > Deleted. A follow-up could take the scope when a confirmation opens, or close the overlays when the scope changes, for every page.
- **Merge with `feat/424-67`.** That branch rewrites the Modeling entry of the README, the line right after the Values store entry, so the second branch to merge gets a conflict there. Keep both rewritten entries. The code and the tests merge cleanly. `test/unit/router/page-memory.test.ts`, `shell.test.ts`, `key-values-page.test.ts` and `unsaved-changes.test.ts` passed on the merged tree. `feat/424-52` merges without conflicts.
