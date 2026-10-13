# Keep Every Section in the Navigation Menu

Linear: [424-17](https://linear.app/424fun/issue/424-17/stop-removing-items-from-navigation-menu-when-a-less-specific-context)

## Problem

The sidebar of the Administration UI (`src/Platform/ui.admin`) has a section per context: **View** (Annotations, Configurations, Graph, …), **Project** (Views, Members, …) and **Organization** (Overview, Members), plus the global entries (Organizations, Account). Each section is built only from the organization, project and view in the **current route**.

So the sections disappear when the user opens a page of a less specific context:

- On a project page (`/acme/billing/settings`), the View section is gone.
- On an organization page (`/acme`), the View and Project sections are gone.
- On a global page (`/orgs`, `/account`), only the global entries are left.

To get back to the annotations from Settings, the user has to go through the context switcher or **Views**.

The issue asks for:

- All pages always available in the navigation menu.
- When the context changes (for example another view), the menu just updates.
- The context stored in `localStorage` and remembered.

## Current State

- **Sections:** `app/utils/navigation.ts`, `buildNavigation(context)` (lines 37–107). It adds the View section only when `org && project && view` (line 43), the Project section only when `org && project` (line 60), and the Organization section only when `org` (line 76). It hides entries below `minRole` and locks entries below `editRole`, using `projectRole` for the View and Project sections and `orgRole` for the Organization one.
- **Sidebar:** `app/components/shell/AppSidebar.vue` (lines 20–38) passes `context.org/project/view` of the `context` store and the roles of `useRole()` to `buildNavigation`. The same items are shown in the docked sidebar and in the slideover (phones, or an unpinned sidebar).
- **Command palette:** `app/components/shell/CommandPalette.vue`:
  - The **Pages** group (lines 64–74) is built by `buildNavigation` from the same route context.
  - The `g a` / `g c` / `g g` / `g t` / `g s` shortcuts (lines 136–140) use `viewBase` (lines 29–31), which is only defined on view pages.
- **Route context:** `app/stores/context.ts` holds the organization, project and view of the route. `app/router/guards/context.ts` sets it before each navigation and checks access (lines 16–38). `useRole()` reads the roles of that context.
- **Remembered context:** the guard already saves the context in `localStorage`, through `preferences.rememberContext` (`app/stores/preferences.ts`, lines 21 and 47–49). It does that only for an allowed route with all three parts (lines 33–35). The only reader is `app/pages/index.vue` (lines 9–17): `/` redirects to `/{org}/{project}/{view}/annotations` of `lastContext` when the user still has access to that project, otherwise to `/orgs`.
- **Default view:** every project has a `default` view. `[org]/[project]/index.vue` redirects to `/{org}/{project}/default/annotations`, and `projectTarget` (`app/utils/context-paths.ts`) switches projects into the default view.
- **Tests:** `test/unit/utils/shell.test.ts` and `test/unit/graph/graph.test.ts` test `buildNavigation`; `test/unit/router/guards.test.ts` tests that the guard remembers full contexts only. The visual baselines `members-*` and `machines-*` (`test/e2e/visual.spec.ts`) show a project page, so they show no View section.

## Proposed Changes

### 1. The remembered context: `preferences.lastContext`

`lastContext` becomes a partial context, so it can also hold a context without a view or without a project:

```ts
// app/stores/preferences.ts
lastContext?: Partial<Scope>

function rememberContext(scope: Partial<Scope>): void
```

Contexts stored by the previous version are full contexts, so they stay valid. Nothing else is migrated.

A new pure function in `app/utils/navigation.ts` merges the current context into the remembered one:

```ts
export function mergeContext(current: Partial<Scope>, remembered: Partial<Scope> | undefined): Partial<Scope>
```

- Without a current organization (`/orgs`, `/account`), the remembered context stays as it is.
- Otherwise the organization is the current one.
  - The project is the current one, or the remembered one if the organization is the same.
  - The view is the current one, or the remembered one if the organization and the project are the same.

| Remembered | Current route | Result |
| --- | --- | --- |
| `acme/billing/staging` | `/acme/billing/settings` | `acme/billing/staging` |
| `acme/billing/staging` | `/acme` | `acme/billing/staging` |
| `acme/billing/staging` | `/acme/shipping/members` | `acme/shipping` |
| `acme/billing/staging` | `/globex` | `globex` |
| `acme/billing/staging` | `/acme/billing/default/graph` | `acme/billing/default` |
| `acme/billing/staging` | `/orgs` | `acme/billing/staging` |

A second function drops what the caller has no access to any more. A membership can be removed, or another user can sign in on the same browser:

```ts
export function accessibleContext(
  remembered: Partial<Scope> | undefined,
  accessMap: Record<string, string> | undefined,
): Partial<Scope> | undefined
```

- No organization, or no membership in it (`hasOrganization`), gives `undefined`.
- A project without a role in the AccessMap (`roleRank(accessMap['org.project']) === 0`) is dropped with its view, and the organization stays.

### 2. The context guard remembers every allowed context

`app/router/guards/context.ts` remembers the context of every allowed route with an organization, not only full contexts:

```ts
if (allowed) {
  const preferences = usePreferencesStore()
  preferences.rememberContext(mergeContext({ org, project, view }, accessibleContext(preferences.state.lastContext, accessMap)))
}
```

- A forbidden route does not change it, as today.
- A route without an organization does not reach this code (the guard returns earlier), so it does not change it either.

### 3. The navigation context

A new pure function in `app/utils/navigation.ts` gives the context the navigation shows:

```ts
export function navigationScope(
  current: Partial<Scope>,
  remembered: Partial<Scope> | undefined,
  accessMap: Record<string, string> | undefined,
): Partial<Scope>
```

- It is `mergeContext(current, accessibleContext(remembered, accessMap))`, so the menu shows the same context the guard remembers.
- A project without a known view gets the `default` view. For example, `/acme/shipping/members` opened for the first time shows the View section of `acme/shipping/default`.
- The parts of the current route are never dropped. On a forbidden route the menu still shows that route's sections, with the `None` role (hidden and locked entries), as today.

A new composable `app/composables/useNavigationContext.ts` (auto-imported) wraps it for components:

```ts
/** The context the navigation menu shows, with the caller's roles in it. */
export function useNavigationContext(): ComputedRef<NavigationContext>
```

- It reads the `context` store (the route), `preferences.state.lastContext` and `session.accessMap`.
- It returns the existing `NavigationContext` (`org`, `project`, `view`, `projectRole`, `orgRole`). The roles are those of the navigation context (`getRole(accessMap, 'org.project')` and `getRole(accessMap, org)`), not of the route. On `/orgs`, entries such as Members and Machines are therefore shown or hidden by the role in the remembered project.

`buildNavigation` itself does not change: it still builds the sections it is given.

### 4. Sidebar and command palette

- **`AppSidebar.vue`:** builds its items from `buildNavigation(navigation.value)`, with `navigation = useNavigationContext()`, instead of the route context and `useRole()`.
- **`CommandPalette.vue`:**
  - The **Pages** group is built from the same navigation context, so the palette lists the same pages as the sidebar.
  - The `g a` / `g c` / `g g` / `g t` / `g s` shortcuts go to the sections of the navigation context's view, so they also work on project, organization and global pages.
  - The annotation items, **Recent** and **New annotation** keep using the route's view, because they need the catalog of the view on screen.
- The **context switcher** is not changed. It still shows the context of the route.
- The section labels do not change.

When the user opens a page of another view, project or organization, the guard updates `lastContext`, and the menu follows it.

### 5. `/` redirect

`app/pages/index.vue` uses `accessibleContext(lastContext, accessMap)`:

- With a project, `/` goes to `/{org}/{project}/{view ?? 'default'}/annotations`.
- With an organization only, `/` goes to `/{org}`.
- Otherwise it goes to `/orgs`.

### 6. Tests

- **Unit (`test/unit/utils/shell.test.ts`):** `mergeContext` (the table in section 1), `accessibleContext` (a removed project, a removed organization, another user), `navigationScope` (the default view, route parts kept on a forbidden route).
- **Unit (`test/unit/router/guards.test.ts`):** the guard remembers project and organization pages, keeps the view of the same project, drops it for another project, and still ignores forbidden routes.
- **E2E (`test/e2e/navigation.spec.ts`, new):** from a view, open the project Settings, the organization Overview and Organizations from the sidebar. Each page still lists the view's Annotations, which leads back to the same view.
- **Visual:** the `members-*` and `machines-*` baselines now show the View section of the default view. They are updated in the Playwright Linux container (`npm run test:visual:docker -- --update`).

### 7. Documentation

- `docs/42for.net/platform/admin-ui.md`: the sidebar keeps the sections of the view, project and organization you last worked in, and switching the context updates them.
- The historical spec `2026-10-09 Administration UI for 2S Platform.md` is not changed.

### Out of scope

- Remembering the last project per organization, or the last view per project. Only the last context is remembered.
- Showing the remembered view in the context switcher on project pages.
- The section labels, including the default view's label (424-21).
