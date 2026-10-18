# Modeling Page in the View Section

Linear: [424-38](https://linear.app/424fun/issue/424-38/add-modeling-page-to-view-section)

## Overview

This review covers the whole spec [2026-10-18 Modeling Page in the View Section](../2026-10-18%20Modeling%20Page%20in%20the%20View%20Section.md), delivered in one phase. The code came in three commits, one per part of the plan: the shell, the pages, and the menu. The tests, the docs and two fixes from the code review followed.

The **View** section of the sidebar went from nine entries to five: **Annotations**, **Configurations**, **Values store**, **Graph** and **Modeling** (`puzzle` icon). Modeling shows the five pages it replaced as tabs, in the style of Monitor:

| Tab | Path below the view | Old path |
| --- | --- | --- |
| Schemas | `modeling/schemas`, with its three detail routes | `schemas` |
| Templates | `modeling/templates`, with `…/{type}` | `templates` |
| Export & Import | `modeling/export-import` | `export-import` |
| Deleted | `modeling/deleted` | `deleted` |
| Compare views | `modeling/compare` | `compare` |

**Compare views** is disabled once the project is known to have no other view. Below Contributor, the whole page is one lock ("Contributors only"), and the menu entry shows the lock. The old URLs redirect, and they keep the rest of the path, the query and the hash. The API, the SDK and the mock API did not change.

## What Was Done

### 1. Routes: `/{org}/{project}/{view}/modeling/{tab}`

As planned:

- `pages/[org]/[project]/[view]/modeling.vue` is the parent route, and `modeling/index.vue` redirects `/…/modeling` to `/…/modeling/schemas`. It returns `${to.path}/schemas` as a string, so the query and the hash stay.
- An unknown tab, such as `/…/modeling/foo`, falls to the root `[...path].vue` ("Page not found").
- `modeling` is not a reserved view name. `/acme/billing/modeling/modeling` opens the Schemas tab of a view named `modeling`, and a unit test covers it.

### 2. The tabs in one place (`app/utils/modeling.ts`)

As planned: `modelingTabs`, `ModelingTab`, `modelingPath`, `modelingTab`, `movedToModeling` and `hasOtherView`, with the signatures of the spec. `modelingTab` ignores a query or a hash. `movedToModeling` splits the encoded path and inserts `modeling` after the view segment, so an encoded key and a trailing slash stay as they are.

### 3. The Reader lock (`modeling.vue`)

As planned. Below `edit`, the page is one `UDashboardPanel id="modeling"` with `<AppNavbar title="Modeling" />` and the lock `EmptyState`. From Contributor up, it is a bare `<RouterView />`. A unit test checks that a Reader gets no `RouterView`, so no tab page and none of its queries are mounted. Another unit test checks that a Contributor gets the tab page as the root element, with no wrapper.

### 4. The shared header with the tabs (`app/components/modeling/ModelingHeader.vue`)

As planned: the navbar with the `#right` slot, the `UDashboardToolbar` with `UTabs` (`variant="link"`, `size="sm"`, `:content="false"`, `activation-mode="manual"`), a computed tab with a getter from `route.path` and a setter that calls `router.push(modelingPath(scope, tab))`, and **Compare views** disabled only when `useGetViews` has succeeded and `hasOtherView` is false. The views query uses the arguments of `compare.vue` and `ContextSwitcher.vue`, so all three share the cache entry `['v1', org, project, 'views']`.

**Additions (code review):** each tab is a page with its own header. Two things got lost when one header replaced another, and the header now handles both:

- **Focus** (`27d8dfdb`, then `42620259`). Opening a tab from the keyboard removed the focused tab together with the previous page, and the focus fell to `<body>`. Now the setter records the path of the tab it opens, but only when the focus is in the tabs. When the header of that path mounts, it focuses its selected tab. The record is cleared one tick after the navigation settles. The first version kept a boolean until the next mount. That leaked in two cases:
  - the selected tab was activated from a detail, which keeps the page and its header, so a later visit took the focus;
  - a second activation came while the first page was still loading, and the cancelled first navigation cleared the mark of the second.
- **The open tab in view.** On a 390 px phone the five tabs are wider than the toolbar, so the toolbar scrolls sideways. A new header used to start at Schemas, with the open Deleted or Compare views tab out of view. Now the header scrolls its selected tab into view (`inline: 'nearest'`) when it mounts. It scrolls again when `document.fonts.ready` resolves, because on the first load the labels widen once the web font arrives. The first regenerated baseline of the mobile Deleted screen showed that tab cut off.

### 5. The five pages move under `modeling/`

As planned:

- The pages and their four detail routes moved with their content unchanged in `bb0b6474`, so git follows them as renames. `bc310a29` put each of them under `<ModelingHeader>`.
- Schemas passes **New schema** through `#right`. Deleted keeps its own toolbar and bulk-actions bar after the header.
- The panel ids, `detail.covers`, the sibling outlets, `showInPage`, `?cascaded=1`, `?key=&to=` and the queries did not change.
- The comment of `export-import.vue` now says that Readers do not reach the page.

### 6. Links use the new paths

As planned: `schemaRoute`, `documentPath`, `exportImportPath`, the close targets of `SchemaDetailPanel.vue` and `templates/[type].vue`, the row links of `templates.vue`, the **Compare** button of the Views page (now only for `edit`), and the shortcuts `g t`, `g s` and `g e`.

**Deviation:** `CombinedSchemaView.vue` does not call `schemaRoute`. It builds its base with `modelingPath(scope, 'schemas')`. `schemaRoute` takes a known type code, but the schema layers fall back to the raw type name when `metaOf` does not know the type, and that fallback stays. For a Reader the base is `undefined`, and the layer names render as plain text.

### 7. Old URLs redirect

As planned: `[view]/schemas/[[...path]].vue`, `[view]/templates/[[...path]].vue`, `[view]/export-import.vue`, `[view]/deleted.vue` and `[view]/compare.vue`. Each one is a stub with one comment and `definePage({ redirect: (to) => movedToModeling(to.path) })`.

- The plugin of vue-router 5.4 extracts `definePage` together with its static import of `@/utils/modeling`.
- The optional catch-all matches `/schemas`, `/schemas/` and every detail below it.
- Before the spec was written, a throwaway page and test checked that these stubs keep the query, the hash and an encoded key.
- Unit tests now run every URL of the spec's table against the real file routes, and they check the route name each one lands on. The e2e `movedPages` loop runs the same six URLs.

### 8. The menu and the command palette

As planned: one **Modeling** entry after **Graph**, with `editRole: 'Contributor'`, `lockedHint: 'Contributors only'` and `tabs`. `AppSidebar.vue` uses `item.lockedHint ?? 'Read-only for your role'` as the tooltip of a locked entry. The **Pages** group of the command palette lists the five tabs after the entry, with the suffix "Modeling", unless the entry is locked. The sidebar marks **Modeling** as active on every tab and every detail; the regenerated template baseline shows it next to a template detail. The e2e test of the View section checks the entry per role.

The tooltip of a locked entry shows only in the collapsed sidebar, because Nuxt UI 4.11.3 renders item tooltips there only. There it replaces the label, so a Reader hovering the icon reads "Contributors only". This was already true of "Read-only for your role", and the docs describe it.

### 9. Section parsing and the context switcher

As planned. `viewSections` is `annotations`, `configurations`, `graph`, `modeling`. A private `viewSectionPath` adds the tab when the section is `modeling`, so `viewTarget` and `projectTarget` keep it and drop the detail. Deleted now stays on Deleted when the view or the project changes. Before, it went to Annotations, because `deleted` was not a view section.

### 10. Tests

- **Unit** (51 new tests in place of 3, 7 files changed, 1 new). `npm test` went from 664 tests in 55 files to 712 in 56.
  - `test/unit/modeling/modeling.test.ts` (new, 39 tests):
    - the path helpers;
    - the header: its tabs and their order, the selected tab per path, a tab change without the old query and Back to it, a cancelled change, and the `#right` slot;
    - the focus: a change from the tabs, a cancelled change, two changes in a row, the selected tab closing its detail, and a change from elsewhere;
    - **Compare views** when the views are known with and without another view, while they load, and after a 500 or a 403;
    - the lock of a Reader and the bare outlet of a Contributor;
    - the old URLs and `/modeling` with the real file routes, and Page not found for an unknown tab.
  - `navigation.test.ts`: "Modeling entry" replaces "Export & Import entry". It checks the View section of an Owner, a Contributor and a Reader, the full entry with its five tabs, the Reader lock, no entry for a page that became a tab, and the shortcuts.
  - `shell.test.ts`: the tab is kept by the view and project switch, the detail is dropped, `/modeling` without a tab, and a view named `modeling`.
  - `type-names.test.ts`, `templates-schemas.test.ts`, `transfer-utils.test.ts`, `transfer-page.test.ts` and `page-memory.test.ts` use the new paths and the moved page.
  - **With the code of `main` and these tests:**
    - 14 tests fail;
    - 2 files cannot load (`modeling.test.ts`, and `transfer-page.test.ts`, which imports the moved page): 50 tests.
    - Run in a throwaway worktree at `fac25b53`.
  - The two focus tests "moves to the tab opened last when two are opened in a row" and "leaves the focus alone later when the open tab only closed its detail" fail with the first focus fix (`27d8dfdb`). "moves to the tab of the opened page after a change from the tabs" fails without any focus fix.
- **E2E** (desktop and mobile)
  - `navigation.spec.ts`, as planned:
    - the six old URLs in `movedPages`;
    - "lists Modeling in the View section of every role, locked for a reader";
    - "opens the tabs of Modeling with a click, and Back returns to the previous tab";
    - "a link opens its tab of Modeling, and a detail link opens the tab and the detail";
    - "Compare views is enabled only when the project has another view";
    - "a reader gets the lock of Modeling instead of its tabs";
    - "a dirty template asks before another tab of Modeling opens", which covers **Keep editing** (URL and draft kept) and **Discard**.
  - **Additions (code review):**
    - "opens the tabs of Modeling from the keyboard, and the opened tab keeps the focus in view";
    - "a link to the last tab of Modeling shows the tab without taking the focus". On mobile it fails without the scroll into view.
  - **Deviation:** the dirty-template test skips itself on mobile. There the detail is full screen and hides the list panel with its tabs (`detail.covers`), so no tab can be clicked while the editor is open.
  - `transfers.spec.ts`: the page opens through **Modeling** and the **Export & Import** tab, and with `g e` from `/modeling/templates`. "a reader can export but sees the import notice" is now "a reader sees the Modeling lock".
  - `accessibility.spec.ts`: the new paths, a **compare views** screen, and "the locked Modeling page of a reader has no serious accessibility violations" in place of the Reader's Export & Import page.
  - `deleted.spec.ts`, `configuration.spec.ts`, `settings.spec.ts` and `visual.spec.ts` use the new paths.
- **Visual:** the screens keep their names. 34 of the 64 baselines were regenerated in the Playwright 1.64 container (`npm run test:visual:docker -- --update`):
  - the desktop light and dark screenshots of annotations, annotation-detail, annotation-history, configurations, deleted, export-import, key-values, members, machines, monitor, monitor-audit, schemas, settings and template, because the View section is shorter, and the five pages have the Modeling header;
  - the mobile deleted, export-import and schemas screenshots, because of the header and the tabs.

  The mobile template screenshot shows the full-screen detail, so it did not change, and neither did the organizations and onboarding screenshots.

### 11. Documentation

As planned:

- `docs/42for.net/platform/admin-ui.md`:
  - a new **Modeling** section between **Graph** and **Export and import**, covering the tabs, the switcher, the palette and the shortcuts, the old addresses, and what a Reader loses;
  - **Deleted**, **Compare views**, **Templates**, **Schemas** and **Export & Import** are described as tabs;
  - the role lines of Export and Import, and the shortcut row;
  - the heading `## Export and import` is unchanged, so the anchor used by `configuration.md` still works.
- `src/Platform/ui.admin/README.md`:
  - the e2e flows;
  - the merged pages;
  - a new **Modeling** bullet with the files, the path-based tabs, the lock, the focus and the scroll of the header, the views query and the menu entry;
  - the path of Export & Import.
- `docs/Platform/Storyteller/export-import.md`: the page path, the navigation, the Reader, the links table and the e2e and axe coverage.

**Change (code review):** `admin-ui.md` says that the palette lists the tabs for Contributors and the roles above. A Reader gets the entry without its tabs.

### 12. Differences from the CodeRabbit coding plan

As in the spec: one optional catch-all per old list, `viewSections` without the old names, the switcher keeping the tab, the tabs in the palette, the Reader details (**Compare** button, schema layers, "Contributors only"), and manual activation.

### Results

- `npm test`: 712 passed (56 files).
- `npm run test:e2e` (desktop and mobile, Playwright's Chromium, `CI=1`): 250 passed and 6 skipped, with no retry. The skipped tests are the dirty-template test on mobile (section 10) and the five that already skip themselves for one device.
- `npm run test:visual:docker`: 64 passed, compared with the baselines committed with this review.
- `npm run typecheck`, `npm run lint` (oxlint and ESLint) and `npm run build`: no problems.
- Prettier reports no problems in the changed files. The one finding left in `modeling/templates.vue` (line 63) was already there on `main`, and it is not on a line this branch changes.

### Notes

- **Branch base:** `main` at `fac25b53`. The TypeScript SDK was rebuilt and reinstalled first, as the ui.admin README says. Without that step, the installed copy predated OpenAPI 0.9.29, and the type check of `main` failed on the transfer operations.
- **Code review:** two workflow rounds. Two skeptics tried to refute each finding.
  - **Round 1:** five reviewers over the code (routing, layout and unsaved changes, permissions, navigation, the tabs). Five findings; two survived and were fixed in `27d8dfdb`:
    - the focus loss after a tab change from the keyboard;
    - the open tab out of view on phones.

    Refuted:
    - activating the selected tab navigates to the tab's list (twice). The old menu links did the same, and it closes an open detail;
    - the views query refetching after its 30 s `staleTime`.
  - **Round 2:** four reviewers over the fix, the tests, the docs and the spec. Eight findings; five survived and were fixed in `42620259`:
    - the focus mark leaking when the selected tab closed its detail (reported twice);
    - a cancelled first activation clearing the mark of a second one;
    - an e2e check that passed without the scroll into view;
    - the palette sentence of `admin-ui.md`.

    Refuted:
    - the same scroll check (a duplicate);
    - a race in the ArrowRight check, which the manual-activation unit test covers;
    - "baselines not regenerated" (they were, and are committed with this review).
- **Merges:** these open PRs touch the same files.

  | PR | Shared files |
  | --- | --- |
  | #100 (424-41) | `admin-ui.md`, `accessibility.spec.ts`, the members baselines |
  | #101 (424-42) | `admin-ui.md`, the README, `configurations.vue`, `accessibility.spec.ts`, the configurations baselines |
  | #105 (424-46) | the README |

  `git merge-tree` finds text conflicts in the docs, the README and `accessibility.spec.ts`. Whichever branch merges second resolves them and runs `npm run test:visual:docker -- --update`.
- **Out of scope**, as in the spec:
  - the API and `sform` (a Reader still exports with `sform story export`);
  - remembering the last Modeling tab;
  - `key-values` in `viewSections`;
  - the **Compare** button on the base view's own row.
