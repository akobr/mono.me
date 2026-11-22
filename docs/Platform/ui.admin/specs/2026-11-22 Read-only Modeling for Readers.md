# Read-only Modeling for Readers

Linear: [424-272](https://linear.app/424fun/issue/424-272/admin-ui-read-only-modeling-schemas-templates-deleted-for-readers) — Admin UI: read-only Modeling (schemas, templates, deleted) for Readers.

## Problem

The admin UI closes **Modeling** below Contributor: a Reader who opens any of its tabs sees the notice "Contributors only" and no tab. The API reads the schemas, the templates, their versions, the deleted annotations and the comparison of views with `Reader` (`docs/Platform/Storyteller/access.md`, **Roles**), so the lock is a choice of the admin UI alone. It hides exactly what the exhibition (424-267) is built to show: a visitor joins the demo project as a Reader and cannot see its schemas and templates.

Modeling should open to Readers read-only: the schemas, the templates and their versions, the deleted annotations and the comparison of views, without any create, edit, delete, restore, import or export action. Contributors and the roles above keep Modeling exactly as today. Export & Import stays with Administrators.

## Current State

### The lock

- `src/Platform/ui.admin/src/app/pages/[org]/[project]/[view]/modeling.vue`: the parent route of every tab. With `canEdit = useCan('edit')` false it renders a `UDashboardPanel` with the `EmptyState` "Contributors only" ("The schemas, the templates, the deleted annotations and the comparison of views are available to the project's Contributors, the export and the import to its Administrators.") and no `RouterView`, so no tab page, no `ModelingHeader` and no detail mounts. From Contributor up it is a bare `<RouterView />`.
- `app/utils/navigation.ts:108-122`: the Modeling entry of the View section has `editRole: 'Contributor'` (the lock badge, "read-only for the caller") and `lockedHint: 'Contributors only'` (the tooltip of a page that is "not read-only but closed"). `lockedHint` has no other user; `AppSidebar.vue:39` shows `item.lockedHint ?? 'Read-only for your role'`.
- `app/components/shell/CommandPalette.vue:70-83`: `pageItems` lists the tabs of an entry only when it is not locked (`item.locked ? [] : item.tabs`), so a Reader's palette has **Modeling** but none of its tabs. The shortcuts `g t`, `g s` and `g e` are not gated and land on the lock.
- `app/utils/modeling.ts`: `modelingTabsFor(role)` already gives every role below Administrator the four tabs Schemas, Templates, Deleted and Compare views (Export & Import has `minRole: 'Administrator'`).

### Gates that only follow the lock

- `app/pages/[org]/[project]/views.vue:21-23`: `canCompare = useCan('edit')` hides **Compare** on the Views page from a Reader, "Compare views is a tab of Modeling, which is locked below Contributor".
- `app/components/configuration/CombinedSchemaView.vue:17-20`: the layers of the **Schema** tab of a configuration link to the Schemas tab only for `canEdit`; a Reader gets plain names.
- `app/components/configuration/ConfigurationComposition.vue:34,120-122`: a template step of the composition links to the Templates tab only for `canEdit`.

### The tab pages

The tab pages were written role-aware (for Contributor against Administrator), so nearly every write is already gated:

- Schemas (`modeling/schemas.vue`, `components/schemas/SchemaDetail.vue`): **New schema** (`v-if="canEdit"`), the footer with **Delete**, **Revert** and **Save**/**Create**, **Save anyway**, `⌘S` and the editor's save all check `canEdit`; the editor gets `:readonly="!canEdit"`. Not gated: a schema that does not exist shows the starter document with the alert "This schema does not exist yet — Edit the starter document and save to create it." (`SchemaDetail.vue:273-280`), which a Reader reaches through every type row (each links, also with **No schema**).
- Templates (`modeling/templates.vue`, `components/templates/TemplateDetail.vue`): **Create template**, the footer, `⌘S` and the editor's save check `canEdit`, the editor is read-only below it. A missing template shows "No {type} template" with a neutral description.
- Versions (`components/versions/VersionsView.vue:69`): `restorable = !!source.restore && canEdit` gates **Restore**, **Restore this version** and **Restore anyway**, and the deleted alert already words itself for a Reader ("Its versions stay readable until they expire.").
- Deleted (`modeling/deleted.vue`): **Restore** and **Restore without descendants** (row menu, bulk) need `canEdit`, **Delete permanently** needs `canPurge` (`manageAccess`), the banner of the detail (`DeletedAnnotationBanner.vue:69`) shows its **Restore** for `canEdit` only. Not gated: the selection column (select all, a checkbox per row) and the **Bulk actions** toolbar, which a Reader would get with nothing but **Clear selection**.
- Compare views (`modeling/compare.vue`): only pickers that write the query and the read-only `DiffEditor` (no merge controls).
- Export & Import (`modeling/export-import.vue`): its own lock `useCan('transfer')` (Administrator) shows "Administrators only" under the other four tabs and mounts no section and sends no request; a Contributor gets it today.
- Read-only screens elsewhere (`ConfigurationEditor.vue:356`, `AnnotationForm.vue`) show a **Read-only** badge (`i-lucide-lock`); the schema and template editors have none.

### API and mock

- Every read of the four tabs needs `Reader` in `Api.Functions` (`ConfigurationSchemaHttp*`, `TemplateHttp`, `AnnotationsHttp.Deleted`, `ConfigurationHttp` with the view diff and the composition, `ViewsHttp` without `discover`, `BindingsHttp`); every write needs `Contributor`, the purge, the export and the import `Administrator`. Nothing changes in the API.
- The MSW mock checks the same roles (`requireRole` in `template-schema-handlers.ts`, `annotation-handlers.ts`, `configuration-handlers.ts`, `transfer-handlers.ts`). Reader contexts: `northwind/portal` in the default dataset (one view, templates and schemas, no deletions) and `meridian-health/clinical-integrations` in the showcase dataset (five views, two deletions, templates and schemas).
- A Debug API (`DEV_AUTH`) skips the role checks of users, so the UI must hide every write itself, as it does.

### Onboarding

- `app/utils/onboarding.ts:130`: the promise of the Exhibition step: "Explore 30 annotations, configurations that resolve live, and the graph that ties them together." It is asserted by unit, e2e and automation tests, Testiny case 495 (ACC-SGN-10) and the `onboarding-exhibition` visual baselines. The exhibition data set (`exhibit`) has 11 schemas, 3 templates, one view and no deletions.

### Tests and documents that assert the lock

- Unit: `test/unit/utils/navigation.test.ts` (the exact Contributor entry with `lockedHint`, "is shown to a reader, locked"), `test/unit/modeling/modeling.test.ts` ("shows a Reader one lock and no tabs"), `test/unit/modeling/compare.test.ts` ("is not offered to a Reader"), `test/unit/configuration/configuration-composition.test.ts` ("shows a template without a link below Contributor").
- E2E: `test/e2e/navigation.spec.ts` ("a reader gets the lock of Modeling instead of its tabs", the badge test), `transfers.spec.ts` ("a reader sees the Modeling lock"), `accessibility.spec.ts` ("the locked Modeling page of a reader"), `showcase.spec.ts` ("lets Ada only read the clinical integrations").
- Automation (`src/Platform/UI.Admin.AutomationTests`): MOD-TAB-03 (Testiny 42, `ModelingTabsTests.Modeling_Reader_GetsContributorsOnlyLock`), NAV-MNU-05 (Testiny 30, the tooltip "Contributors only" and "a locked Modeling lists no tabs"), ORG-VEW-04 (Testiny 37, no **Compare** for a Reader), the page objects `ModelingPage.ContributorsOnly*`, comments in `NavigationContextSwitcherTests`, `NavigationShortcutTests`, `ModelingExportImportPage`, `ViewsPage.ORG`; the catalogue `docs/Platform/UI.Admin.AutomationTests/test-cases.md`.
- Docs: `docs/42for.net/platform/admin-ui.md` (Modeling, Deleted, Templates and Schemas, Composition, Export & Import, the onboarding), `docs/Platform/Storyteller/access.md` (**Roles**, the exhibition's "The admin UI shows less"), `docs/Platform/Storyteller/export-import.md` (**Administration UI**: navigation, who, tests), `src/Platform/ui.admin/README.md` (tests, architecture of Modeling).

## Proposed Changes

### 1. Modeling opens to every member (`modeling.vue`)

`modeling.vue` becomes the bare `<RouterView />` for every role (the file stays: it is the parent route of the tab panels and their detail outlets). The tab pages and their details mount for a Reader and hide their writes themselves. Its comment says so: Readers browse every tab read-only, Export & Import keeps its own Administrator lock (424-66).

### 2. The sidebar entry and the command palette

- `utils/navigation.ts`: the Modeling entry drops `lockedHint` and keeps `editRole: 'Contributor'`, so a Reader sees the read-only lock of Annotations, Configurations and Values store, with the tooltip "Read-only for your role". `lockedHint` loses its only user and is removed from `NavigationEntry`; `AppSidebar.vue` shows "Read-only for your role" for every locked entry.
- `CommandPalette.vue`: a locked entry is read-only, not closed, so its tabs are always listed: `...(item.tabs ?? [])`. The tabs are the role's (`modelingTabsFor`), so a Reader gets Schemas, Templates, Deleted and Compare views, not Export & Import.
- `utils/modeling.ts` and `ModelingHeader.vue`: no change of behaviour (a Reader gets the four tabs; on Export & Import none is selected); their comments name Readers and Contributors.

### 3. The tab pages, read-only below Contributor

1. **Schemas** (`SchemaDetail.vue`):
   - below Contributor, the editor shows the **Read-only** badge (`UBadge` "Read-only", `i-lucide-lock`, as in `ConfigurationEditor.vue`) in the `#actions` slot of `DocumentEditor`;
   - a schema that does not exist shows a Reader the `EmptyState` "No schema" ("This schema does not exist in this view, so it validates nothing.", icon `i-lucide-braces`) instead of the starter document, its form preview and the alert to create it; the alert "This schema does not exist yet" stays for Contributors. The **Versions** tab stays, so the history of a deleted schema remains readable.
   - **New schema**, the footer, **Save anyway** and the shortcuts are already gated (unchanged).
2. **Templates** (`TemplateDetail.vue`): the same **Read-only** badge next to the version badge. The empty state of a missing template is already neutral and offers **Create template** to Contributors only (unchanged).
3. **Deleted** (`deleted.vue`): the selection column (select all and a checkbox per row) and the **Bulk actions** toolbar render only with `canEdit` (an Administrator always has it): a Reader has nothing to act on. The row menu keeps **Show details** for every role; **Restore** and **Delete permanently** keep their gates, as does the banner of the detail. The comment names Readers.
4. **Compare views** (`compare.vue`): no change.
5. **Export & Import** (`export-import.vue`): no change of behaviour: a Reader who opens it (a link, the old address, `g e`) gets "Administrators only" under the four other tabs, like a Contributor, and no request. Its comment no longer says that Readers do not reach it.

### 4. Links into Modeling for Readers

- `views.vue`: **Compare** is shown to every role (`canCompare` is removed); it is disabled on the default view's row as before.
- `CombinedSchemaView.vue`: the schema layers link to their schemas for every role (`canEdit` is removed).
- `ConfigurationComposition.vue`: a template step links to its template for every role (`canEdit` is removed).

### 5. The onboarding promise (`utils/onboarding.ts`)

The Exhibition step promises the schemas and templates that a visitor can now read: "Explore 30 annotations, configurations that resolve live, the graph that ties them together, and the schemas and templates behind them." Deleted annotations and Compare views are not promised (the exhibition has none and one view).

### 6. Admin UI tests

- Unit:
  - `navigation.test.ts`: the Contributor entry without `lockedHint`; a Reader's Modeling is locked (read-only) without `lockedHint` and lists the four tabs; a Reader row in the tabs per role.
  - `modeling.test.ts`: `modeling.vue` is only the outlet for a Reader too; a Reader row in the header's tabs per role; a Reader on Export & Import has the four tabs and none selected.
  - `compare.test.ts`: a Reader gets **Compare** on the Views page (enabled for another view, disabled on the default view); a Reader compares two views on Compare views.
  - `configuration-composition.test.ts`: a Reader's template step links to `…/modeling/templates/exe`; a new test of `CombinedSchemaView` (or the configuration's Schema tab) shows a Reader the layer links.
  - `templates-schemas.test.ts`: a Reader's schema and template detail: the **Read-only** badge, a read-only editor, no **Delete**, **Revert**, **Save** or **Create**, `⌘S` sends nothing; a Reader on a missing schema gets "No schema" and no starter document; a Contributor still gets the starter document and its alert.
  - a new `test/unit/modeling/deleted.test.ts`: a Reader sees the deleted rows without checkboxes or bulk toolbar, the row menu has only **Show details**; a Contributor still selects rows and restores.
  - `transfer-page.test.ts`: a Reader gets "Administrators only" under the four tabs, no section.
  - the onboarding text tests take the new promise.
- E2E (Playwright, desktop and mobile):
  - `navigation.spec.ts`: the badge test keeps the lock of a Reader (renamed: read-only); "a reader browses Modeling read-only": the four tabs on Schemas, Templates, Deleted and Compare views (disabled in `northwind/portal`), no **New schema**, a read-only template detail without **Save** or **Delete**, "Administrators only" on Export & Import, no "Contributors only"; the palette of a Reader lists the four tabs and not Export & Import.
  - `transfers.spec.ts`: a Reader gets "Administrators only" under the four tabs, like a Contributor.
  - `accessibility.spec.ts`: axe on a Reader's read-only template detail and on a Reader's Export & Import lock.
  - `showcase.spec.ts`: Ada reads Modeling of `clinical-integrations`: the tabs, no **New schema**; Deleted lists the seeded deletions without checkboxes and with **Show details** only.
  - `deleted.spec.ts` or `modeling.spec.ts`: a Reader's schema detail ignores `⌘S` and shows no write action.
  - `onboarding.spec.ts`: the new promise; the `onboarding-exhibition` visual baselines are regenerated (`npm run test:visual:docker`).

### 7. Automation tests and Testiny

- MOD-TAB-03 (Testiny 42) becomes "A Reader browses Modeling read-only, with the Administrators only lock on Export & Import", `ModelingTabsTests.Modeling_Reader_BrowsesReadOnly`, mode B, role Reader:
  1. Open `…/{view}/modeling`: URL `…/modeling/schemas`; the tabs Schemas, Templates, Deleted, Compare views; no "Contributors only"; no **New schema**.
  2. Open `…/modeling/templates/exe`: the template detail; either "No execution template" without **Create template**, or the document read-only without **Save**, **Revert** or **Delete**.
  3. Open `…/modeling/export-import`: "Administrators only" with its description, the four tabs, none selected, no region "Export" or "Import".
  4. Open `…/modeling/deleted`: "Show cascaded"; no **Restore** and no **Delete permanently**.
  5. Open `…/modeling/compare`: no lock; Compare views is the selected tab.
- NAV-MNU-05 (Testiny 30): the Modeling entry's description is "Read-only for your role"; typing "Schemas" in the palette lists **Schemas** (suffix Modeling) and no **Export & Import**.
- ORG-VEW-04 (Testiny 37) becomes "A Reader lists the views and compares them, without managing actions", `ProjectViewTests.Views_Reader_ListsWithoutManagingActions`: a **Compare** per row (disabled on the default view), no **New view**, **Register** or **Edit**.
- ACC-SGN-10 (Testiny 495) and `Pages/OnboardingPage.cs`: the new promise.
- The page objects drop `ContributorsOnly`, `ContributorsOnlyText` and `ContributorsOnlyDescription`; `ModelingDeletedPage` gets a locator of "Nothing is deleted" if a test needs it; the comments of `NavigationContextSwitcherTests`, `NavigationShortcutTests`, `ModelingExportImportPage`, `ModelingSchemaPage`, `ModelingTemplatePage` and `ViewsPage.ORG` follow.
- `test-cases.md`: the titles and test names of the three cases, and a pointer to this spec for their new steps. The number of cases does not change (240).

### 8. Documentation

- `docs/42for.net/platform/admin-ui.md`: Modeling (a Reader browses its four tabs read-only, the entry shows the read-only lock, the palette lists the tabs of every role, Export & Import stays with Administrators), Deleted (Restore needs Contributor; a Reader sees the list and the details), Templates and Schemas (a Reader's read-only documents and "No schema"), Composition (the template link for every role), Versions/Compare (the Views page offers Compare to everyone), Export & Import (a Reader or a Contributor sees "Administrators only"), the onboarding's Exhibition step.
- `docs/Platform/Storyteller/access.md`: **Roles** (the admin UI shows these reads read-only to a Reader) and the exhibition's "What a visitor reads".
- `docs/Platform/Storyteller/export-import.md`: **Administration UI** (navigation, who, the tests).
- `src/Platform/ui.admin/README.md`: the e2e and axe coverage and the architecture of Modeling.
- `docs/Platform/UI.Admin.AutomationTests/test-cases.md` and, if a count changes, `automation-tests.md`.
