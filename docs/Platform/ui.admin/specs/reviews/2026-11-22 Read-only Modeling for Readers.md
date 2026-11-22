# Read-only Modeling for Readers

## Overview

The admin UI opened **Modeling** to Readers, read-only. A Reader now browses Schemas, Templates, Deleted and Compare views with their versions and details, and gets no create, edit, delete, restore, import or export action; Export & Import keeps its own Administrator lock, which a Reader now sees under the four other tabs like a Contributor. Contributors and the roles above see Modeling as before. The links that only followed the old lock (Compare on the Views page, the schema layers and template steps of a configuration) are open to every role, the exhibition step of the onboarding promises the schemas and templates, and the unit, e2e, visual and automation tests, the Testiny cases and the docs follow. Spec: [2026-11-22 Read-only Modeling for Readers](../2026-11-22%20Read-only%20Modeling%20for%20Readers.md). Linear: [424-272](https://linear.app/424fun/issue/424-272/admin-ui-read-only-modeling-schemas-templates-deleted-for-readers).

## What Was Done

### 1. Modeling opens to every member

`app/pages/[org]/[project]/[view]/modeling.vue` is the bare `<RouterView />` of every role; the `EmptyState` "Contributors only" and `useCan('edit')` are gone. The file stays as the parent route of the tab panels and their detail outlets, and its comment says that the tabs are read-only below Contributor and that Export & Import has its own lock.

### 2. The sidebar entry and the command palette

- `utils/navigation.ts`: the Modeling entry keeps `editRole: 'Contributor'`, so a Reader sees the read-only lock of Annotations, Configurations and Values store, and drops `lockedHint`. The field had no other user and is removed from `NavigationEntry`; `AppSidebar.vue` shows "Read-only for your role" for every locked entry.
- `CommandPalette.vue`: the tabs of an entry are listed whether it is locked or not, so a Reader's palette has Schemas, Templates, Deleted and Compare views, not Export & Import (`modelingTabsFor`).
- `utils/modeling.ts`, `ModelingHeader.vue`: comments only.

### 3. The tab pages, read-only below Contributor

1. Schemas (`components/schemas/SchemaDetail.vue`): the editor shows the **Read-only** badge through `DocumentEditor`'s `#actions` slot, only below Contributor (the slot is conditional, so a Contributor's toolbar is unchanged). A schema that does not exist shows a Reader the `EmptyState` "No schema" ("This schema does not exist in this view, so it validates nothing.") between the error state and the editor, so neither the starter document, its preview nor the alert to create it appear; the **Versions** tab stays. The alert "This schema does not exist yet" stays for Contributors.
2. Templates (`components/templates/TemplateDetail.vue`): the **Read-only** badge after the version badge.
3. Deleted (`modeling/deleted.vue`): the selection column (its header cell and its cells) and the **Bulk actions** toolbar render only with `canEdit`; the bulk **Restore** lost its own `v-if`, which the toolbar's condition now covers. The row menu keeps **Show details** for every role.
4. Compare views: unchanged.
5. Export & Import: unchanged behaviour, comment updated.

### 4. Links into Modeling for Readers

`views.vue` shows **Compare** to every role (`canCompare` is removed), `CombinedSchemaView.vue` links the schema layers and `ConfigurationComposition.vue` the template steps for every role (both lose their `canEdit`).

### 5. The onboarding promise

`utils/onboarding.ts`: "Explore 30 annotations, configurations that resolve live, the graph that ties them together, and the schemas and templates behind them." Deleted annotations and Compare views are not promised, as planned.

### 6. Admin UI tests

- Unit (Vitest, 127 files, 2467 tests pass): `navigation.test.ts` (the entry without `lockedHint`, a Reader's read-only lock with the four tabs, a Reader row of the tabs per role), `modeling.test.ts` (the outlet of a Reader and of a Contributor, the Reader's header tabs, no selected tab on Export & Import for both), `compare.test.ts` (a Reader gets **Compare** on the Views page and compares default with staging, sending only reads), `configuration-composition.test.ts` (the Reader's template link, and a new `CombinedSchemaView` test of the layer links for a Reader and a Contributor), `templates-schemas.test.ts` (a Reader's schema and template: badge, read-only editor, no write button, `⌘S` sends nothing even with a changed draft; the Reader's "No schema" and the Contributor's starter document; a Reader's missing template without **Create template**; positive controls in which a Contributor's `⌘S` sends the `PUT`/`PATCH`), the new `deleted.test.ts` (a Reader's rows without selection or toolbar and with **Show details** only; a Contributor's selection, toolbar and restore), `transfer-page.test.ts` (a Reader's "Administrators only"), and the onboarding text tests.
- E2E (Playwright, desktop and mobile): `navigation.spec.ts` (the Reader's read-only badge and its tooltip in the rail; "a reader browses the tabs of Modeling read-only"; the Reader's palette), `transfers.spec.ts` and `accessibility.spec.ts` (the Export & Import lock of a Contributor and of a Reader; axe on the Reader's read-only template), `showcase.spec.ts` (Ada's read-only Modeling of `clinical-integrations`, its two deletions without checkboxes and with **Show details** only, **Compare** from the Views page), `modeling.spec.ts` (a Reader's read-only schema; "No schema" for a missing one), `configuration.spec.ts` (a Reader follows the composition's template and a layer of the Schema tab into Modeling, on `exe.northwind.notifications.prod`, which has a configuration), `onboarding.spec.ts` (the promise).
- Visual: the four `onboarding-exhibition-*` baselines were regenerated in the container. Two unrelated baselines (`template-desktop-light`, `annotation-detail-desktop-dark`) came out blank in that run and were restored; the visual suite then passed (88).
- Runs: the whole e2e suite, 471 passed, 14 skipped, 1 failed: `theme.spec.ts` › dark › the accent, which read `rgb(0, 0, 0)` under the load of the full run (also once during the implementation, on mobile); alone it passed 12 of 12 (`--repeat-each=3`, both projects). It does not touch Modeling. Port 4174 was taken by the preview of another worktree, so the runs used a copy of the config on port 4184.
- Deviations found in the review of the branch:
  - the first Reader shortcut test pressed `⌘S` on an existing schema, where the shortcut cannot save for any role (nothing is dirty), so it passed without the role gate. The press moved to the missing schema (`rst`), where a Contributor's `⌘S` creates it from the starter document; with the `canEdit` of the shortcut removed, the test fails;
  - the Reader's palette test checked that Export & Import is absent before the 100 ms filter applied; it now waits for the previous option to go first, as NAV-MNU-05 does.

### 7. Automation tests and Testiny

- MOD-TAB-03 (Testiny 42): `ModelingTabsTests.Modeling_Reader_BrowsesReadOnly`, the five steps of the spec. The template step accepts "No execution template" without **Create template** or the read-only document; the Deleted step waits for the list (`ModelingDeletedPage.WaitForListAsync`) and checks the row menu only on a target with deletions, which the mock's Reader project has none of, so there the rows are covered by the admin UI's own tests (said in the test's comment); Compare views opens selected, with its key picker. `ExpectContributorsOnlyAsync`, `ModelingPage.ContributorsOnly`, `ContributorsOnlyText`, `ContributorsOnlyDescription` and `AllTabs` (whose only user was the helper) are removed; `ModelingDeletedPage` gets `NothingDeleted`, `ActionsButtons` and `SelectCheckboxes`, `ModelingTemplatePage` gets `ReadOnlyBadge`.
- NAV-MNU-05 (Testiny 30): the description "Read-only for your role", and the palette lists **Schemas** (suffix Modeling) and no **Export & Import**.
- ORG-VEW-04 (Testiny 37): `ProjectViewTests.Views_Reader_ListsWithoutManagingActions`, "A Reader lists the views and compares them, without managing actions": **Compare** disabled on the default view's row, one per listed view, enabled with its `?to=` link otherwise.
- ACC-SGN-10 (Testiny 495): `OnboardingPage.ExhibitionPromiseText`.
- Comments: `NavigationContextSwitcherTests`, `NavigationShortcutTests`, `ModelingExportImportPage`, `ModelingSchemaPage`, `ModelingTemplatePage`, `ModelingDeletedPage`, `ViewsPage.ORG`, `CombinedSchemaView`.
- Testiny project `2SP`: cases 42 and 37 got their new titles, preconditions (the test names) and steps, case 30 its steps 3 and 5, case 495 the promise of step 1.
- `test-cases.md`: the rows of MOD-TAB-03 and ORG-VEW-04, and a pointer to section 7 of the spec for the steps of the rewritten cases; still 240 cases. `automation-tests.md`: `WaitForListAsync` is listed with the waits that use the read timeout.
- Runs: the build has no new warning; without a target the 241 tests skip and the run exits 0; against this worktree's mock build (port 5184, as 5174 served another worktree) the three Reader tests passed on desktop and phone with the default dataset and on desktop with the showcase dataset.

### 8. Documentation

- `docs/42for.net/platform/admin-ui.md`: the exhibition step, Deleted (**Show details**, **Restore** for Contributors, what a Reader sees), the composition's template link and **Compare** on the Views page for every role, a paragraph on a Reader's read-only templates and schemas with "No schema", Modeling (the four tabs of Readers and Contributors, the palette of every role, the rewritten paragraph on the Reader's read-only Modeling), Export & Import (a Reader or a Contributor sees "Administrators only").
- `docs/Platform/Storyteller/access.md`: **Roles** and the exhibition's "The admin UI shows the same" (schemas and templates with their versions; Export & Import stays locked).
- `docs/Platform/Storyteller/export-import.md`: **Navigation**, **Who** and the test lists.
- `src/Platform/ui.admin/README.md`: the e2e and axe coverage and the architecture of Modeling.
- The review of the branch ran its correctness checks (the Reader's write paths, the regressions of Contributors) and its documentation check only in part, as the agents ran out of usage; they were done by hand instead: every action reached by a Reader was already gated by `canEdit`, `canPurge` or `canTransfer` (as the mapping before the plan found), and a search of the docs and code outside `specs/` finds "Contributors only" and `lockedHint` only in tests that assert their absence.
