# Modeling Page in the View Section

Linear: [424-38](https://linear.app/424fun/issue/424-38/add-modeling-page-to-view-section)

## Problem

The admin UI (`src/Platform/ui.admin`) lists nine entries in the **View** section of the sidebar. Five of them are pages for modeling the view: **Templates**, **Schemas**, **Compare views**, **Export & Import** and **Deleted**. They are separate pages with separate entries, so the section is long and the modeling pages are not grouped together.

The issue asks for these changes:

- Add a **Modeling** page to the View section, with the `puzzle` icon.
- Show **Schemas**, **Templates**, **Export & Import**, **Deleted** and **Compare views** on that page as tabs, in this order, in the same way as the **Monitor** page of a project.
- Disable the **Compare views** tab when the project has no other view.
- Lock the whole Modeling page for users who only have the Reader role.

CodeRabbit made a coding plan for the issue ("2026-10-18 Add Modeling page to View section.md", attached to its comment on 424-38). This spec follows that plan. The places where it differs are listed in section 12.

## Current State

- **Navigation:** `app/utils/navigation.ts`, `buildNavigation` (lines 86–164). The sidebar (`AppSidebar.vue`) and the **Pages** group of the command palette (`CommandPalette.vue`, lines 70–74) both read from it. The view section is at lines 93–114:

  | Entry | Icon | Path | Role |
  | --- | --- | --- | --- |
  | Annotations | `tags` | `annotations` | everyone, locked below Contributor |
  | Configurations | `settings-2` | `configurations` | everyone, locked below Contributor |
  | Values store | `vault` | `key-values` | everyone, locked below Contributor |
  | Graph | `waypoints` | `graph` | everyone |
  | Templates | `layout-template` | `templates` | everyone, locked below Contributor |
  | Schemas | `braces` | `schemas` | everyone, locked below Contributor |
  | Compare views | `git-compare` | `compare` | everyone |
  | Export & Import | `arrow-down-up` | `export-import` | everyone, locked below Contributor |
  | Deleted | `archive-restore` | `deleted` | everyone, locked below Contributor |

  A "locked" entry has `editRole`. `AppSidebar.vue` (lines 34–41) shows it with a lock badge and the tooltip "Read-only for your role". An entry with `minRole` is hidden instead.
- **The five pages** are leaf pages in `app/pages/[org]/[project]/[view]/`. Each one has its own `UDashboardPanel` with `<AppNavbar title="…">` in `#header`:

  | Page | Panel | Header | Below it |
  | --- | --- | --- | --- |
  | `schemas.vue` | `schemas`, hidden while `detail.covers` | **New schema** in `#right` (Contributor) | `schemas/type/[type].vue`, `schemas/annotation/[key]/index.vue`, `schemas/annotation/[key]/type/[type].vue` (detail panels), sibling `<RouterView />` |
  | `templates.vue` | `templates`, hidden while `detail.covers` | — | `templates/[type].vue` (detail panel), sibling `<RouterView />` |
  | `export-import.vue` | `export-import` | — | — |
  | `deleted.vue` | `deleted` | its own `UDashboardToolbar` (**Show cascaded**) and a bulk-actions bar | — |
  | `compare.vue` | `compare` | — | — |

  - Page state lives in the query: Deleted uses `?cascaded=1` (lines 25–28) and Compare uses `?key=&to=` (lines 34–41). Both use `router.replace({ query })`.
  - `export-import.vue` (lines 25–36) holds `transfers.showInPage(id)` while it is mounted and releases it in `onBeforeUnmount`, so the toaster does not report the import shown on the page.
- **The Monitor precedent (424-35):** `app/pages/[org]/[project]/monitor.vue`.
  - One `UDashboardPanel` with `<AppNavbar title="Monitor" />` and a `UDashboardToolbar` with `<UTabs :content="false" variant="link" size="sm">` (lines 25–31).
  - The tab is kept in `?tab=audit` and switched with `router.replace` (lines 15–19). Only the open tab is mounted.
  - Below Administrator the page shows one lock `EmptyState` ("Administrators only") instead of the tabs (lines 33–38), and the menu entry is hidden (`minRole`).
- **Query tabs of the detail editors:** `SchemaDetail.vue` (lines 75–81) and `TemplateDetail.vue` (lines 92–98) keep their own **Edit** and **Versions** tabs in `?tab=versions`, and `VersionsView.vue` keeps `?v=` and `?from=`.
  - `useUnsavedChanges.ts` (lines 25–40) asks "Discard changes?" on `onBeforeRouteLeave` and `onBeforeRouteUpdate`. It does not ask when only the query changes (`to.path === from.path`).
- **Layout:** `layouts/DefaultLayout.vue` renders the page inside `UDashboardGroup`, a horizontal flex container. A list page renders a fragment: its `UDashboardPanel`, then `<RouterView />` for the detail. `DetailPanel.vue` is the next flex sibling of the list panel. It resizes against `panel.parentElement` (lines 51–67), and in overlay mode it starts below the navbar (`top-(--ui-header-height)`).
- **Section parsing:** `app/utils/context-paths.ts`.
  - `viewSections` (lines 4–12) is `annotations`, `configurations`, `graph`, `templates`, `schemas`, `compare`, `export-import`. There is no `deleted`.
  - `currentSection` (lines 41–58) reads the fourth segment. `viewTarget` (lines 91–94) and `projectTarget` (lines 77–89) keep only that section when the context switcher changes the view or the project. From Deleted they go to Annotations.
  - The `page-memory` guard (`router/guards/page-memory.ts`) remembers only `annotations`, `configurations` and `graph`.
- **Links to the five pages:**
  - `schemaRoute` (`app/utils/schema-targets.ts`, lines 144–154), used by `schemas.vue` (also for the highlighted row, `current === row.to`) and `SchemaCreateDialog.vue` (line 47);
  - `components/configuration/CombinedSchemaView.vue`, line 16, which builds its own `…/schemas` links for the schema layers on the **Schema** tab of a configuration;
  - the close targets of `SchemaDetailPanel.vue` (lines 15–17) and `templates/[type].vue` (lines 15–17), and the row links of `templates.vue` (line 54);
  - `documentPath` and `exportImportPath` (`app/utils/transfers.ts`, lines 116–126 and 190), used by `ImportResult.vue` and `TransferToaster.vue`;
  - the **Compare** button of each row of the Views page (`pages/[org]/[project]/views.vue`, lines 110–116), shown to every role, which links to `…/compare?to={view}`;
  - the shortcuts `g t`, `g s` and `g e` (`CommandPalette.vue`, lines 147–149), listed in `composables/useShortcutsHelp.ts` (lines 28–30).
- **What a Reader can do today:** browse Schemas and Templates and open their details read-only; use Compare views; export the view (`export-import.vue`, line 9: "Every role exports"; Import shows "Importing needs the Contributor role"); list Deleted and open the details of a deleted annotation, without Restore.
- **Views:** `useGetViews` returns the registered views and `default`. `compare.vue` (lines 26–31) and `ContextSwitcher.vue` (lines 20–25) call it with the same arguments, so they share the query key `['v1', org, project, 'views']`. In the mock API, `acme/billing` (Owner) has `default` and `staging`, while `acme/shipping` (Contributor) and `northwind/portal` (Reader) have only `default`.
- **Router:** file-based routes of vue-router 5.4 (`vite.config.ts`, line 13). Redirect stubs use `definePage({ redirect })` with a hidden template (`pages/[org]/[project]/audit.vue`). A redirect that returns a path string keeps the query and the hash of the old URL. `app/typed-router.d.ts` is generated and committed.
- **Tests:** `test/unit/utils/navigation.test.ts` (lines 7–40) tests the Export & Import entry, including "is shown to a reader, locked, because a reader can still export". `transfer-utils.test.ts`, `transfer-page.test.ts`, `templates-schemas.test.ts` and `type-names.test.ts` contain the old paths. The e2e specs `transfers`, `deleted`, `configuration`, `settings`, `accessibility` and `visual` open the old URLs. `transfers.spec.ts` (lines 217–223) and `accessibility.spec.ts` (line 16) cover the Export & Import page of a Reader. No test covers the "Discard changes?" prompt.
- **Docs:** `docs/42for.net/platform/admin-ui.md` (lines 40, 110, 144, 146, 162–172 and 225), `src/Platform/ui.admin/README.md` (lines 65–69, 163 and 168) and `docs/Platform/Storyteller/export-import.md` (lines 683–735 and 806–808).

## Proposed Changes

### 1. Routes: `/{org}/{project}/{view}/modeling/{tab}`

- Each tab is a page with its own path:

  | Tab | Path below the view | Icon |
  | --- | --- | --- |
  | Schemas | `modeling/schemas`, and `…/type/{type}`, `…/annotation/{key}`, `…/annotation/{key}/type/{type}` | `braces` |
  | Templates | `modeling/templates`, and `…/{type}` | `layout-template` |
  | Export & Import | `modeling/export-import` | `arrow-down-up` |
  | Deleted | `modeling/deleted` | `archive-restore` |
  | Compare views | `modeling/compare` | `git-compare` |

  The icons are the icons of the old menu entries.
- **Path, not query.** Monitor keeps its tab in `?tab=`. Modeling cannot do that, for two reasons:
  - The schema and template details already use `?tab=versions` for their own tabs.
  - `useUnsavedChanges` does not ask when only the query changes, so switching a query tab would drop a dirty draft without a prompt.

  A path also keeps the nested detail routes as they are.
- **New parent page `pages/[org]/[project]/[view]/modeling.vue`**: the Reader lock (section 3) and the outlet of the tabs.
- **New `pages/[org]/[project]/[view]/modeling/index.vue`**: a redirect stub from `/…/modeling` to `/…/modeling/schemas`, so the first tab opens by default, as Usage does on Monitor. The query and the hash are kept.
- An unknown tab, such as `/…/modeling/foo`, shows "Page not found", as any other unknown path does.
- **Reserved names:** `modeling` is not added to the reserved view names. It is the fourth segment, so a view named `modeling` still works (`/{org}/{project}/modeling/modeling/schemas`).

### 2. The tabs in one place (`app/utils/modeling.ts`)

A new module that the header, the menu, the section parsing, the link builders and the redirect stubs share:

```ts
export const modelingTabs = [
  { label: 'Schemas', value: 'schemas', icon: 'i-lucide-braces' },
  { label: 'Templates', value: 'templates', icon: 'i-lucide-layout-template' },
  { label: 'Export & Import', value: 'export-import', icon: 'i-lucide-arrow-down-up' },
  { label: 'Deleted', value: 'deleted', icon: 'i-lucide-archive-restore' },
  { label: 'Compare views', value: 'compare', icon: 'i-lucide-git-compare' },
] as const

export type ModelingTab = (typeof modelingTabs)[number]['value']

/** /{org}/{project}/{view}/modeling, or one of its tabs. */
export function modelingPath(scope: Scope, tab?: ModelingTab): string

/** The tab of a path under /{org}/{project}/{view}/modeling/{tab}/…, or undefined. */
export function modelingTab(path: string): ModelingTab | undefined

/** An old path of a view page (/{org}/{project}/{view}/schemas/…) moved under modeling/ (424-38). */
export function movedToModeling(path: string): string

/** Whether the project has a view other than the current one. */
export function hasOtherView(views: View[] | undefined, current: string | undefined): boolean
```

`hasOtherView` uses the filter of the view list of `compare.vue` (line 48): a view with a `Name` that is not the current view.

### 3. The Reader lock (`modeling.vue`)

- `const canEdit = useCan('edit')`. "Only the Reader role" means "cannot edit": `None` never reaches a project page, because the context guard stops it.
- **Below Contributor**, the page renders one `UDashboardPanel id="modeling"`:
  - `<AppNavbar title="Modeling" />` in `#header`, without tabs;
  - in `#body`, `<EmptyState icon="i-lucide-lock" title="Contributors only" description="…" />`. The description says that the schemas, the templates, the export and import, the deleted annotations and the comparison of views are available to the project's Contributors.
- **From Contributor up**, the page renders a bare `<RouterView />`, without a wrapper element.
  - Each tab page keeps its own `UDashboardPanel` and its sibling detail outlet, as direct children of `UDashboardGroup`.
  - A wrapper would nest the panels, and `DetailPanel` would resize against the wrong parent.
- The tab pages are not mounted for a Reader, so none of their queries run.
- **This is a deliberate change of policy.** A Reader can no longer browse schemas and templates, compare views, export the view or list deleted annotations in the admin UI. The API and `sform` do not change. A Reader can still export with `sform story export` or the API. The Views page hides its **Compare** buttons from Readers, and the schema layers of a configuration are no longer links for them (section 6).
- The per-action checks of the pages stay as they are: **New schema**, the editors and the template and schema save bars need `edit`, Restore needs `edit`, Delete permanently needs `manageAccess`, and Import needs `edit`. The lock is an addition, not a replacement.

### 4. The shared header with the tabs (`app/components/modeling/ModelingHeader.vue`)

The header is a component, not a page, because every file in `pages/` becomes a route.

- **Template:** in the `#header` of each tab page:

  ```vue
  <AppNavbar title="Modeling">
    <template #right><slot name="right" /></template>
  </AppNavbar>
  <UDashboardToolbar>
    <UTabs v-model="tab" :items="items" :content="false" variant="link" size="sm" activation-mode="manual" />
  </UDashboardToolbar>
  ```

  A page with its own toolbar (Deleted) keeps it after `<ModelingHeader />`, in the same `#header`.
- **The tab model** is a computed with a getter and a setter:
  - The getter is `modelingTab(route.path)`.
  - The setter calls `router.push(modelingPath(context.scope, value))`. This drops the query and the hash of the previous tab, so `?cascaded=1` or `?key=&to=` does not leak into another tab.
  - `UTabs` items have no `to` (Nuxt UI 4.11.3), so the setter navigates.
  - Each tab is a page, so a tab change is pushed to the history. **Back** returns to the previous tab, as it does after a click in the sidebar.
- **Manual activation:** every activation is a navigation: one page unmounts, the next one mounts and loads its data, and a dirty editor asks first. With manual activation, the arrow keys only move the focus, and Enter or Space opens the tab.
- **Unsaved changes:** a tab change changes the path, so a dirty schema or template detail asks "Discard changes?". **Keep editing** aborts the navigation. The route does not change, and the getter keeps the current tab selected.
- **Compare views:**
  - The header calls `useGetViews(() => context.org ?? '', () => context.project ?? '', undefined, { query: { enabled: () => !!context.project } })`. These are the arguments of `compare.vue` and `ContextSwitcher.vue`, so the header shares their cache entry and adds no request.
  - The tab is disabled only when the query has succeeded and `hasOtherView` is false. While the views load, or when the query fails, the tab stays enabled. That way the tab is disabled only when the project is known to have no other view.
  - An open Compare tab is not redirected away. A link from the Views page can name a discovered view with `?to=`, which the list of registered views does not have. The page keeps its own empty state.

### 5. The five pages move under `modeling/`

- `git mv` keeps their history:

  | From `[view]/` | To `[view]/modeling/` |
  | --- | --- |
  | `schemas.vue`, `schemas/type/[type].vue`, `schemas/annotation/[key]/index.vue`, `schemas/annotation/[key]/type/[type].vue` | the same names |
  | `templates.vue`, `templates/[type].vue` | the same names |
  | `export-import.vue`, `deleted.vue`, `compare.vue` | the same names |

- In each page, `<AppNavbar title="…">` becomes `<ModelingHeader>`. Schemas passes **New schema** through `#right`.
- Everything else stays: the panel ids, `:class="{ hidden: detail.covers.value }"`, the sibling `<RouterView />`, the `showInPage` lifecycle of Export & Import, `?cascaded=1`, `?key=&to=`, and the queries. Only the open tab is mounted, as on Monitor. Nothing uses `KeepAlive` or `v-show`, so leaving Export & Import still releases `showInPage`.
- None of the pages fills its panel, so their body padding stays.
- The comment of `export-import.vue` no longer says that every role exports, because Readers do not reach the page.

### 6. Links use the new paths

- `schemaRoute` builds `…/modeling/schemas/…`, so the highlighted row of Schemas still matches `route.path`.
- `CombinedSchemaView.vue` uses `schemaRoute` instead of its own base. A Reader cannot open Modeling, so for a Reader the schema layers are plain text, not links.
- The close target of `SchemaDetailPanel.vue` is `…/modeling/schemas`. The row links of `templates.vue` and the close target of `templates/[type].vue` use `…/modeling/templates`.
- `documentPath` links templates and schemas to `…/modeling/templates/{type}` and `…/modeling/schemas/…`, and `exportImportPath` is `…/modeling/export-import`.
- The **Compare** button of the Views page links to `…/modeling/compare?to={view}`. It is shown only to users who can edit.
- The shortcuts `g t`, `g s` and `g e` open `…/modeling/templates`, `…/modeling/schemas` and `…/modeling/export-import`. Their help labels (**Templates**, **Schemas**, **Export & Import**) stay, because these are the tab names.

### 7. Old URLs redirect

- Redirect stubs replace the old pages:
  - `[view]/schemas/[[...path]].vue` and `[view]/templates/[[...path]].vue`. The optional catch-all covers the list and every detail below it in one file. Nothing is nested under these stubs, so they need no parent `schemas.vue` or `templates.vue`.
  - `[view]/export-import.vue`, `[view]/deleted.vue` and `[view]/compare.vue`.
- Each stub has a one-line comment and the same redirect:

  ```ts
  import { movedToModeling } from '@/utils/modeling'

  // Schemas moved to the Schemas tab of Modeling (424-38); saved links keep working.
  definePage({ redirect: (to) => movedToModeling(to.path) })
  ```

- `movedToModeling` inserts `modeling` after the view segment of the path. It works on the encoded `to.path`, so keys survive as they were. The result is a path string, so Vue Router keeps the query and the hash.

  | Old URL | New URL |
  | --- | --- |
  | `/acme/billing/default/schemas` | `/acme/billing/default/modeling/schemas` |
  | `/acme/billing/default/schemas/annotation/sbt.a/type/exe?tab=versions` | `/acme/billing/default/modeling/schemas/annotation/sbt.a/type/exe?tab=versions` |
  | `/acme/billing/default/templates/exe` | `/acme/billing/default/modeling/templates/exe` |
  | `/acme/billing/default/export-import` | `/acme/billing/default/modeling/export-import` |
  | `/acme/billing/default/deleted?cascaded=1` | `/acme/billing/default/modeling/deleted?cascaded=1` |
  | `/acme/billing/default/compare?to=staging` | `/acme/billing/default/modeling/compare?to=staging` |

### 8. The menu and the command palette (`app/utils/navigation.ts`, `CommandPalette.vue`)

- The View section becomes:

  | Entry | Icon | Path | Role |
  | --- | --- | --- | --- |
  | Annotations | `tags` | `annotations` | everyone, locked below Contributor |
  | Configurations | `settings-2` | `configurations` | everyone, locked below Contributor |
  | Values store | `vault` | `key-values` | everyone, locked below Contributor |
  | Graph | `waypoints` | `graph` | everyone |
  | Modeling | `puzzle` | `modeling` | everyone, locked below Contributor |

- The entry is `{ label: 'Modeling', icon: 'i-lucide-puzzle', to: `${base}/modeling`, editRole: 'Contributor', lockedHint: 'Contributors only', tabs }`:
  - **No `minRole`:** a Reader sees the entry with the lock, as the issue asks, and the page explains the lock.
  - **`lockedHint`** is a new optional field of `NavigationEntry`. It is the tooltip of a locked entry, and it defaults to "Read-only for your role". Modeling is not read-only for a Reader. It is closed, so its tooltip says "Contributors only".
  - **Active state:** the sidebar link goes to the `index.vue` child, which has an empty path. `RouterLink` counts such a link as active on every route of the parent, so Modeling is active on every tab and every detail. The entry is not `exact`.
- **`tabs`** is a new optional field of `NavigationEntry`: `{ label, icon, to }[]`. The Modeling entry lists its five tabs. The sidebar ignores the field.
  - The **Pages** group of the command palette lists the tabs after their entry, with the suffix "Modeling", so typing "Schemas" or "Deleted" still finds the page.
  - The tabs are listed only when the entry is not locked.

### 9. Section parsing and the context switcher (`app/utils/context-paths.ts`)

- `viewSections` becomes `annotations`, `configurations`, `graph`, `modeling`. The old names `templates`, `schemas`, `compare` and `export-import` are removed: their URLs now only redirect and are never the current path. 424-35 did the same for the redirect stubs of the project pages.
- `currentSection('/acme/billing/default/modeling/templates')` is `{ kind: 'view', name: 'modeling' }`.
- **`viewTarget` and `projectTarget` keep the tab.** On a Modeling tab they go to `/…/{view}/modeling/{tab}` and drop the detail. Switching the view on Templates stays on Templates, as it does today, and Deleted now stays on Deleted instead of going to Annotations. Without a known tab, they go to `/…/modeling`, which opens Schemas.
- The page memory does not change. `modeling` is not a remembered section, so the guard ignores it.

### 10. Tests

- **Unit**
  - `test/unit/utils/navigation.test.ts` replaces the Export & Import tests:
    - the View section is **Annotations**, **Configurations**, **Values store**, **Graph**, **Modeling** for an Owner, a Contributor and a Reader;
    - **Modeling** has `i-lucide-puzzle`, the path `…/modeling` and its five tabs in order. It is unlocked for a Contributor, and locked with the hint "Contributors only" for a Reader;
    - no entry links to `templates`, `schemas`, `compare`, `export-import` or `deleted` directly below the view;
    - the shortcut keys stay unique, and `G E` is still listed.
  - `test/unit/utils/shell.test.ts`: `currentSection`, `viewTarget` and `projectTarget` on `…/modeling/templates`, on `…/modeling/schemas/type/exe` (the detail is dropped), on `…/modeling` and on a view named `modeling`.
  - A new `test/unit/modeling/modeling.test.ts`:
    - `modelingPath`, `modelingTab`, `movedToModeling` and `hasOtherView`;
    - `ModelingHeader`: the five tabs in order, the selected tab from the path, and a tab change that pushes the new path without the query;
    - **Compare views** is disabled when the views query succeeds with no other view (`acme/shipping`). It is enabled with another view (`acme/billing`), while the query is pending, and after it failed;
    - `modeling.vue` shows the lock and no tabs to a Reader, and no lock to a Contributor;
    - with the real file routes (`vue-router/auto-routes`, as in `soft-delete.test.ts`), the old URLs of section 7 land on their new path with the query and the hash, and `/…/modeling` lands on `/…/modeling/schemas`.
  - The old paths change in `templates-schemas.test.ts`, `transfer-utils.test.ts`, `transfer-page.test.ts` (the import of the page and the expected links), `type-names.test.ts` (the path of the schemas page) and `page-memory.test.ts`.
- **E2E** (desktop and mobile)
  - `navigation.spec.ts`:
    - the View section per role. The Owner of `acme/billing`, the Contributor of `acme/shipping` and the Reader of `northwind/portal` all see **Modeling**, and only the Reader sees the lock;
    - the tabs: clicking a tab changes the path and selects it, a direct link opens its tab, and a detail link opens its tab and its detail;
    - **Compare views** is enabled on `acme/billing` and disabled on `acme/shipping`;
    - the old URLs of section 7 land on their new path with the query kept;
    - the Reader of `northwind/portal` sees "Contributors only" and no tabs on every Modeling URL;
    - a dirty template editor asks "Discard changes?" when a tab is clicked. **Keep editing** keeps the URL and the draft, and **Discard** opens the tab.
  - `transfers.spec.ts`: the page opens from the sidebar (**Modeling**, then the **Export & Import** tab) and with `g e`. The test "a reader can export but sees the import notice" becomes "a reader sees the Modeling lock".
  - `deleted.spec.ts`, `configuration.spec.ts` (Compare), `settings.spec.ts` (Templates), `accessibility.spec.ts` and `visual.spec.ts` use the new paths. In `accessibility.spec.ts`, "export and import of a reader" becomes the locked Modeling page of a Reader, and **Compare views** is added.
- **Visual:** the `deleted`, `template`, `schemas` and `export-import` screens keep their names and get the new header. The View section of every desktop screen changes. Regenerate the baselines with `npm run test:visual:docker -- --update`, then check that `npm run test:visual:docker` passes.
- `npm test`, `npm run test:e2e`, `npm run typecheck`, `npm run lint` and `npm run build` must pass.

### 11. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - a short **Modeling** paragraph: the five tabs, the disabled **Compare views**, and the lock for Readers;
  - lines 40 and 110: **Deleted** is a tab of **Modeling**;
  - line 144: **Compare views** is a tab of **Modeling**;
  - line 146: **Templates** and **Schemas** are tabs of **Modeling**;
  - lines 162–172: **Export & Import** is a tab of **Modeling**, and it is not available to a Reader;
  - line 225: the shortcuts open the tabs.
- `src/Platform/ui.admin/README.md`: the e2e list (lines 65–69), the merged pages (line 163) and the file paths of Export & Import (line 168).
- `docs/Platform/Storyteller/export-import.md`: the page path (line 683), the navigation (line 685), the Reader (line 703), the links table (lines 732–735) and the e2e and axe coverage of a Reader (lines 806–808).

### 12. Differences from the CodeRabbit coding plan

- **Redirect stubs:** one optional catch-all per old list, `schemas/[[...path]].vue` and `templates/[[...path]].vue`, instead of the `index.vue` and `[...path].vue` pair. The behaviour is the same, with fewer files.
- **`viewSections`** drops the old names instead of keeping them. A redirected URL is never the current path.
- **The context switcher keeps the Modeling tab** instead of opening Schemas each time.
- **The command palette** lists the five tabs, so the pages stay findable by name.
- **Readers:** the Views page hides **Compare**, the schema layers of a configuration are plain text, and the locked entry says "Contributors only" instead of "Read-only for your role".
- **The tabs** use manual activation.

### Out of scope

- Permissions of the API and `sform`. The lock is in the admin UI only.
- Remembering the last Modeling tab or detail (the page memory of 424-29).
- Adding `key-values` to `viewSections`.
- Hiding the **Compare** button of the base view's own row on the Views page.
