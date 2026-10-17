# Highlight the Default View in the Navigation Menu and the Switcher

Linear: [424-43](https://linear.app/424fun/issue/424-43/highlight-a-selection-of-default-view)

## Overview

This review covers the whole spec [2026-10-17 Highlight the Default View in the Navigation Menu and the Switcher](../2026-10-17%20Highlight%20the%20Default%20View%20in%20the%20Navigation%20Menu%20and%20the%20Switcher.md), delivered in one phase. The `default` view, the production view of a project, now carries a purple **prod** badge in three places: after **View · default** in the sidebar, in the view button of the switcher in the top bar, and on the `default` item of the switcher's list of views. Every other view looks as before. The label text and the command palette do not change. There is one change from the plan: the chevrons of the view button use a literal class instead of the slot's `ui.trailingIcon()` (section 4).

## What Was Done

### 1. Option 2: a `prod` badge

As planned. The badge is a `UBadge` with `color="primary" variant="subtle" size="sm"` and the lowercase text `prod`, the style of the **default** badge of the Views page. Before writing the spec, three designs went to a panel of three judges: bold text in the accent (option 1), the `prod` badge (option 2), and a badge that also shows in the icon rail. All three judges chose option 2. Bold adds little to a label that is already semibold, and a purple label right above the purple active entry reads as a second selection. The badge also carries text, so the mark does not rely on color alone.

### 2. One helper for the default view (`app/utils/views.ts`, new)

As planned:

- `defaultViewName` is `'default'`;
- `isDefaultView(view)` compares the name exactly and case-sensitively, like the API;
- `defaultViewBadge` is the props object of the badge (`as const`).

The sidebar, the view button and the view list all use the same object, so the three marks cannot drift apart. The two `'default'` literals in the touched files now use `defaultViewName`: `navigationScope` in `navigation.ts` and the fallback list in `ContextSwitcher.vue`.

### 3. The sidebar (`app/utils/navigation.ts`, `AppSidebar.vue`)

As planned:

- `NavigationSection` has an optional `defaultView`. `buildNavigation` sets it with `isDefaultView(view)` on the view section and returns `defaultView: boolean` on every section.
- The label stays `` `View · ${view}` ``. `CommandPalette.vue` reads only the label and the items, so it needed no change.
- `AppSidebar.vue` gives the label item `badge: section.defaultView ? defaultViewBadge : undefined` and `ui: { linkTrailing: 'ms-0' }`.
  - Nuxt UI binds the badge object after its own neutral/outline defaults, so the badge is primary and subtle.
  - `ms-0` replaces the theme's `ms-auto`, so the badge follows the name instead of sitting at the right edge with the lock badges.

The docked sidebar, both slideovers and the project pages (which show the remembered view, or `default`) show the badge. The icon rail shows no section label, as before.

### 4. The switcher (`app/components/shell/ContextSwitcher.vue`)

- **View button:**
  - When `isDefaultView(context.view)`, a conditional `#trailing` slot draws the badge (`shrink-0 py-0.5`) and then the chevrons again. Other views keep the stock button.
  - The button keeps 28 px, the height of the organization and project buttons (checked in the mock app).
  - This is a change from the plan: the chevrons get `class="size-4 shrink-0"` instead of `:class="ui.trailingIcon()"`. `ButtonSlots` types the slot's `ui` as `Button['ui']`, whose members are optional, so `ui.trailingIcon()` fails `vue-tsc` with TS2722 ("Cannot invoke an object which is possibly 'undefined'"). The literal is the class of the Nuxt UI 4.11.3 theme for a sm button (`shrink-0`, plus `size-4` for sm). If the button size or a theme override of `trailingIcon` changes, this class must change with it.
- **View list:** as planned. The `default` item gets `slot: 'default-view'`, and a `#default-view-trailing` template next to the trigger draws the same badge. The other items and **Manage views** keep their default content.
- **Accessible names:** the button and the menu item are named "default prod". No `aria-label` or hidden text was added.

### 5. Tests

- **Unit**
  - `test/unit/utils/views.test.ts` (new): `isDefaultView` is true only for `default`. It is false for `staging`, `Default`, `DEFAULT`, `prod`, `production`, `''`, `null` and `undefined`. The badge props are checked too.
  - `test/unit/utils/shell.test.ts`:
    - the first navigation test expects `defaultView` `[true, false, false, false]`;
    - the `staging` test is renamed "names the selected view and marks only the default one" and expects `defaultView: false`;
    - a new test checks that a project page without a remembered view gets **View · default** with `defaultView: true`.
  - `test/unit/components/default-view.test.ts` (new): no shell component was mounted in a unit test before.
    - It mounts `ContextSwitcher` with the MSW mock handlers. The `default` button shows the name, then the badge, then a single chevron icon; `staging` shows no badge; the open list reads `defaultprod`, `staging`, `Manage views`.
    - It mounts `AppSidebar` through the unpinned slideover, because happy-dom's 1024 px viewport collapses the docked menu into the rail. **View · default** holds the `prod` badge with `ms-0`; **View · staging** holds none.
    - It resets the module-level `open` and `collapsed` refs of `useSidebar` after each test.
  - With the change undone, 7 of the new or changed tests fail. The check that `staging` has no badge passes both ways, as it should.
- **End to end** (`test/e2e/navigation.spec.ts`):
  - "marks the default view as production in the menu and the switcher": the label **View · default** in the menu holds `prod`, on desktop and in the phone slideover. On desktop, the view button is named `default prod`.
  - "does not mark another view, and marks the default one in the list of views": **View · staging** holds no badge. On desktop, the `staging` button has no badge, the list offers `default prod`, and choosing it opens `/acme/billing/default/annotations` with the badge on the button and in the menu.
  - Every `prod` locator is scoped to the label row or the `Context` navigation.
- **Accessibility:** no new screen. The axe run of `accessibility.spec.ts` covers the badge on the default view, docked sidebar and switcher, light and dark, and it passed.
- **Visual:** as planned.
  - All 60 screenshots were rewritten with `--update-snapshots=all` in the Playwright 1.64 container, from a one-off copy of `run-in-container.mjs` outside the repository that wrote into a temp folder. A pixel diff against the committed baselines sorted them.
  - **Committed (26):** the desktop light and dark screenshots of annotations, annotation-detail, annotation-history, configurations, deleted, key-values, members, machines, monitor, monitor-audit, schemas, settings and template.
    - The view screens differ in the sidebar label row and the view crumb (1,272 px). The project screens differ only in the label row (676 px).
    - Every one of these diffs was under the 0.2 % tolerance, so a plain `--update` would have written nothing.
    - Three of them also differ in a one-pixel row border of the grid (380 px), and members-desktop-light in 3 px of anti-aliasing. The new files are kept as rendered.
  - **Kept (34):** the mobile, organizations and onboarding screenshots. 26 did not change.
    - configurations-mobile and deleted-mobile (light and dark) and organizations-desktop-dark differ only because their committed baselines are older than this branch. They still show the theme button that 424-35 removed and lack the export/import buttons of the configurations page. They pass within the tolerance, so this branch leaves them alone.
    - organizations-desktop-light and onboarding-desktop show no view.
  - A compare run then passed all 60.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`, line 18: as planned. After the sentence about **View · {view}**, it says that `default` is the main view of the project, the one that serves production, and that a purple **prod** badge follows its name in the sidebar, in the switcher and in the switcher's list of views.
- `src/Platform/ui.admin/README.md`: the list of e2e flows names the `prod` badge of the default view and its absence on another view.

### Results

- `npm test`: 461 passed (51 files).
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome` and `CI=1`): 206 passed, 1 flaky, 5 skipped.
  - The flaky test is "opens /acme/billing/invitations on the page that took it over" (desktop). It timed out waiting for the redirect, then passed on the retry. It passed in an earlier run of `navigation.spec.ts` alone, and this branch does not touch the redirect stubs.
  - The 5 skipped tests skip themselves for one device: the phone bottom sheet on desktop, and the detail panel, the tree roots and two import flows on mobile.
- `npm run test:visual:docker`: 60 passed after the update.
- `npm run typecheck`, `oxlint`, `eslint` and Prettier on the changed files report no problems.

### Notes

- **Code review:** a workflow sent four reviewers over the diff (correctness, accessibility and UX, tests, docs and spec). Two skeptics then tried to refute each of their 15 findings. Four findings survived:
  - the visual baselines were not committed yet: they were regenerated by then and are committed in `f4a0972d`;
  - the review document was missing: this file;
  - the chevron class differs from the spec: kept, with the reason in section 4;
  - the view button test did not check the order of the name, the badge and the chevrons: fixed in `8d651e61`, which also reads the list of views from the open menu only.

  The refuted findings included:
  - a clash between `prod` and the environment badge in the sidebar footer;
  - truncation at the narrowest sidebar width;
  - a request to show `prod` on the Views page too.
- **The Views page** (not in the spec, which left it out of scope; asked for after the PR was opened): the badge of the default view on the project's **Views** page (`app/pages/[org]/[project]/views.vue`) says `production` instead of `default`. Its style is unchanged (primary, subtle, sm). The page lists every view, so it has the room for the full word that the sidebar and the switcher shorten to `prod`.
  - A new e2e test, "marks the default view as production on the Views page", checks the badge on the `default` row and its absence on `staging`. `navigation.spec.ts` then passed 26 of 26 on desktop and mobile (Chrome, `CI=1`). The full e2e run in Results was before this test.
  - `admin-ui.md` says the Views page marks the default view **production**.
  - No visual or axe screen shows the Views page, so no baseline changed.
- **Gaps left as they are** (Out of scope in the spec):
  - the icon rail between lg and xl names no view;
  - phones have no view button in the top bar, so the slideover menu is their only mark;
  - the other `'default'` literals of the app and the mocks.
- **Merges:** branches that also change `navigation.ts`, `AppSidebar.vue`, `ContextSwitcher.vue`, `shell.test.ts` or the desktop baselines should be checked with `git merge-tree`. Whichever branch merges second regenerates the desktop baselines.
