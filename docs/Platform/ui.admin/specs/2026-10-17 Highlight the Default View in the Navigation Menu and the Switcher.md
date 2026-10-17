# Highlight the Default View in the Navigation Menu and the Switcher

Linear: [424-43](https://linear.app/424fun/issue/424-43/highlight-a-selection-of-default-view)

## Problem

The admin UI (`src/Platform/ui.admin`) names the selected view in two places: the section label of the sidebar (**View · default**, **View · staging**) and the last crumb of the switcher in the top bar. Since [2026-10-13 Name the Default View in the Navigation Menu](./2026-10-13%20Name%20the%20Default%20View%20in%20the%20Navigation%20Menu.md), the `default` view looks the same as every other view in both places.

The `default` view is the main view of a project, the one that serves production. Another view is a parallel edition: a rollout, a rehearsal or a release candidate. An edit in `default` changes what is live, so the UI should make it clear at a glance when `default` is the selected view.

The issue asks to mark the `default` view in the **View** section of the navigation menu and in the last section of the breadcrumb in the top panel. It offers two options: bold text in the main color, or a suffix badge `prod` or `production` in the main color.

## Current State

- **Default view:** the backend names it `default` (`Constants.DefaultViewName`, `Abstractions.Annotations/src/Constants.cs`, line 7) and sets `View.IsDefault = Name == "default"` (`Backend.CosmosDb/src/Entities/Annotations/ViewEntity.cs`, line 31). The UI has no constant or helper for it. It compares with the literal `'default'` in several places, for example `app/utils/navigation.ts` (line 75) and `app/components/shell/ContextSwitcher.vue` (line 48). It reads `IsDefault` in one place only: the Views page (`app/pages/[org]/[project]/views.vue`, line 105) marks the default view with `<UBadge label="default" color="primary" variant="subtle" size="sm" />`.
- **Sidebar label:** `buildNavigation` in `app/utils/navigation.ts` builds the view section with `label: \`View · ${view}\`` (line 92). It returns `{ id, label?, items }` per section (lines 84 and 141–151). The view comes from `navigationScope` (lines 69–76): the view of the route, else the remembered view, else `default`. So the label also names a view on project pages (Members, Settings, Monitor, …), where the top bar shows no view.
- **Sidebar rendering:** `app/components/shell/AppSidebar.vue`, line 21, turns the label into `{ label: section.label, type: 'label' }`. The same items feed the docked `UNavigationMenu` (line 60, `:collapsed`) and the slideover menu (line 75).
- **How Nuxt UI 4.11.3 draws a label item** (`node_modules/@nuxt/ui/dist/runtime/components/NavigationMenu.vue`):
  - It renders only in vertical orientation and when the menu is not collapsed (line 184). The icon rail (pinned sidebar between lg and xl) shows no section label at all, for any view.
  - It uses the same inner template as a link. `item.badge` becomes a trailing `UBadge` with the fixed props `color="neutral" variant="outline"` followed by `v-bind="item.badge"`, so an object badge can set its own color and variant (lines 160–168).
  - The trailing span has the theme class `group ms-auto inline-flex gap-1.5 items-center`, so the badge sits at the right edge of the row, in the column of the lock badges of read-only entries. A per-item `ui.linkTrailing` overrides it.
  - The label row is `font-semibold text-xs/5 text-highlighted`. It is already semibold, so bold adds little.
- **Command palette:** `app/components/shell/CommandPalette.vue`, lines 70–74, uses `section.label` as the `suffix` of every page item, and Fuse searches it. Any text added to the label would change the palette too.
- **Switcher:** `app/components/shell/ContextSwitcher.vue` (`<nav aria-label="Context">`) has one dropdown per organization, project and view.
  - The view button (lines 92–101) is `color="neutral" variant="ghost" size="sm"`, with the `eye` icon, the `chevrons-up-down` trailing icon and `class="hidden max-w-40 sm:inline-flex"`. Below sm it is hidden, so on a phone the slideover is the only place that names the view.
  - The view list (lines 46–59) maps the views to their names (`IsDefault` is dropped) and marks the current one with `active`.
  - `UButton` has a `trailing` slot that replaces only the trailing icon and keeps the label span with its `truncate`. A sm `UBadge` is 20 px tall (`text-[10px]/3 py-1`), 4 px more than the 16 px content of a sm button.
  - `DropdownMenuItem` has no `badge` prop. An item with `slot: 'x'` gets the slots `#x-leading`, `#x-label` and `#x-trailing`; the ones that are not given keep their default content (`DropdownMenuContent.vue`, lines 101–134).
- **Accent:** purple, from [2026-10-16 Purple Accent Matching the 42 Logo](./2026-10-16%20Purple%20Accent%20Matching%20the%2042%20Logo.md). The active menu entry is purple text on a grey pill. A subtle primary badge has a contrast of 5.38 (light) and 4.58 (dark) on the elevated surface, and more on the plain background.
- **Mock data:** `acme/billing` has the views `default` ("Production catalog") and `staging` (`app/mock/db.ts`, lines 161–176).
- **Tests:**
  - `test/unit/utils/shell.test.ts`, "navigation": checks the section labels (`'View · default'`, line 107) and, in "names the selected view, the same for every view" (line 115), the label of `staging`.
  - No unit test mounts `AppSidebar` or `ContextSwitcher`.
  - `test/e2e/navigation.spec.ts` has no locator on the section label or on the switcher.
  - `test/e2e/accessibility.spec.ts` runs axe on 16 screens in light and dark, desktop and mobile. On desktop (1440 px) the sidebar is docked and expanded, so axe checks the label and the switcher of the default view.
  - `test/e2e/visual.spec.ts`: 15 screens × desktop/mobile × light/dark = 60 baselines. The desktop screens except organizations and onboarding show **View · default**; the 8 view screens also show the switcher's `default` button. The mobile screens show neither. The tolerance is `maxDiffPixelRatio: 0.002`, and `--update` rewrites only failing screenshots.
- **Docs:** `docs/42for.net/platform/admin-ui.md`, line 18, ends with "The sidebar lists the screens of the view under **View · {view}**, so it always names the selected view, `default` included." Line 219 lists what uses the purple accent.

## Proposed Changes

This reverses one decision of the 424-21 spec ("No new styling, badge or icon"): the `default` view gets a mark of its own. Its label text stays **View · default**.

### 1. Option 2: a `prod` badge

Mark the default view with a lowercase **prod** badge: `UBadge` `color="primary" variant="subtle" size="sm"`, the style of the **default** badge of the Views page.

- **Badge, not bold:** the label is already semibold, so bold and purple would rest on the color. A purple, non-interactive label right above the purple active entry would also read as a second selected item. A badge carries text, so the mark does not depend on color (WCAG 1.4.1), and it says what the view is.
- **`prod`, not `production`:** `production` is about twice as wide. In the view button (`max-w-40`) it would truncate the name `default`, and at the default sidebar width it would barely fit next to **View · default**. `prod` is short and lowercase, like the other badges of the shell (`default`, `discovered`, `current`, `local`).
- **Only the badge is purple:** the label text, the button colors, its hover and focus ring stay as they are.

### 2. One helper for the default view (`app/utils/views.ts`, new)

```ts
/** The view every project has (`Constants.DefaultViewName`); the API sets `View.IsDefault` from this name. */
export const defaultViewName = 'default'

/** Whether a view is the default one, the main (production) view of its project. Case-sensitive, like the API. */
export function isDefaultView(view: string | null | undefined): boolean {
  return view === defaultViewName
}

/** The badge after the name of the default view in the sidebar and the switcher (424-43). */
export const defaultViewBadge = { label: 'prod', color: 'primary', variant: 'subtle', size: 'sm' } as const
```

- It compares the name and does not load the views for `IsDefault`: the sidebar and the switcher have only the name, and the API derives `IsDefault` from it.
- The two literals in the files this change touches use `defaultViewName`: `navigation.ts` (line 75) and `ContextSwitcher.vue` (line 48). The other literals stay; replacing them is not part of this change.

### 3. The sidebar (`app/utils/navigation.ts`, `AppSidebar.vue`)

- `buildNavigation` returns a fact, not a style: each section gets `defaultView: boolean`, true for the view section of the default view and false otherwise. `NavigationSection` gets an optional `defaultView?: boolean`, and the return type gets `defaultView: boolean`.
- The label stays `` `View · ${view}` ``, so the command palette suffix and its search do not change.
- `AppSidebar.vue` maps the flag to the label item:

  ```ts
  {
    label: section.label,
    type: 'label' as const,
    badge: section.defaultView ? defaultViewBadge : undefined,
    // The badge follows the name; the lock badges of the entries keep the right edge.
    ui: { linkTrailing: 'ms-0' },
  }
  ```

- The row reads **View · default** `prod` in the docked sidebar, in the slideover of the pinned sidebar below lg, and in the slideover of the unpinned sidebar (phones included). It also shows on project pages whose sidebar links into the default view.
- The icon rail does not render section labels for any view, and nothing is added there.

### 4. The switcher (`app/components/shell/ContextSwitcher.vue`)

- **View button:** only when `isDefaultView(context.view)`, a `#trailing` slot puts the badge between the name and the chevrons:

  ```vue
  <template v-if="isDefaultView(context.view)" #trailing="{ ui }">
    <UBadge v-bind="defaultViewBadge" class="shrink-0 py-0.5" />
    <UIcon name="i-lucide-chevrons-up-down" data-slot="trailingIcon" :class="ui.trailingIcon()" />
  </template>
  ```

  - The slot replaces the trailing icon, so the chevrons are drawn again with the button's own class. The `trailing-icon` prop stays, so the button keeps its trailing layout.
  - `py-0.5` keeps the badge at 16 px, so the view crumb stays as tall as the organization and project crumbs.
  - `shrink-0` keeps `prod` whole, and a long name truncates first. Other views keep the stock button.
- **View list:** the `default` item gets `slot: 'default-view'`, and a `#default-view-trailing` slot shows the same badge, so the list says which view is production when another one is selected.
- **Accessible names:** the button and the menu item are named "default prod", which keeps the visible text (WCAG 2.5.3). No `aria-label` or hidden text is added.

### 5. Tests

- **Unit**
  - `test/unit/utils/views.test.ts` (new): `isDefaultView('default')` is true; `'staging'`, `'Default'`, `'DEFAULT'`, `'prod'`, `'production'`, `''`, `null` and `undefined` are false.
  - `test/unit/utils/shell.test.ts`:
    - the first test also expects `defaultView` `[true, false, false, false]`;
    - "names the selected view, the same for every view" becomes "names the selected view and marks only the default one", and expects `defaultView: false` for `staging`;
    - a project page without a remembered view (`navigationScope` for `acme/shipping`) gets a view section with `View · default` and `defaultView: true`.
  - `test/unit/components/default-view.test.ts` (new), with the MSW mock handlers:
    - `ContextSwitcher` in `acme/billing/default`: the view button holds a `prod` badge; in `staging` it does not; the open view list shows the badge on `default` only;
    - `AppSidebar` through the unpinned slideover (the happy-dom viewport is 1024 px, so the docked menu is collapsed): the **View · default** label holds the `prod` badge, the **View · staging** label does not.
- **End to end** (`test/e2e/navigation.spec.ts`, desktop and mobile)
  - "marks the default view as production in the menu and the switcher": on `/acme/billing/default/annotations` the menu label **View · default** holds `prod`; on desktop the view button of the `Context` navigation is named "default prod".
  - "does not mark another view": on `/acme/billing/staging/annotations` the label **View · staging** holds no badge. On desktop, the view button has no badge, the open list shows `prod` on `default`, and choosing it opens `/acme/billing/default/annotations` with the badge on the button.
  - Locators of `prod` stay inside the label row or the `Context` navigation, because the mock data has keys like `exe.northwind.invoicing.prod`.
- **Accessibility:** `accessibility.spec.ts` needs no new screen: it already checks the default view with the docked sidebar and the switcher in both themes.
- **Visual:** regenerate the desktop baselines that show the label or the button (26 PNGs) with `--update-snapshots=all`, from a one-off copy of `run-in-container.mjs` outside the repository, because a small change can stay under the tolerance. Commit only the screenshots that show the badge, check that the mobile ones do not change, and run `npm run test:visual:docker` to compare.
- `npm test`, `npm run typecheck`, `npm run lint` and `npm run test:e2e` must pass.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`, line 18: after the sentence about **View · {view}**, say that the `default` view is the main, production view of the project, and that a purple **prod** badge marks it in the sidebar and in the switcher.
- `src/Platform/ui.admin/README.md`: add the badge to the list of e2e flows.

### Out of scope

- The icon rail between lg and xl, which names no view today.
- A view button on phones (below sm).
- The other `'default'` literals in the app and the mocks.
- The command palette, the Views page and the Compare page.
