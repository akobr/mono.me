# Keep Every Section in the Navigation Menu

Linear: [424-17](https://linear.app/424fun/issue/424-17/stop-removing-items-from-navigation-menu-when-a-less-specific-context)

## Overview

This review covers the whole spec [Keep Every Section in the Navigation Menu](../2026-10-13%20Keep%20Every%20Section%20in%20the%20Navigation%20Menu.md), delivered in one phase.

The sidebar of the Administration UI no longer drops the View and Project sections on a page of a less specific context. The context guard now remembers every allowed context in `preferences.lastContext` (in `localStorage`), merged with the one remembered before. The sidebar and the **Pages** of the command palette show the route's context completed by that remembered context. Opening a page of another view, project or organization updates both.

## What Was Done

### 1. The remembered context: `preferences.lastContext`

- `app/stores/preferences.ts`:
  - `lastContext` is now `Partial<Scope>`, with a doc comment.
  - `rememberContext` takes a `Partial<Scope>`.
  - Contexts stored by the previous version are full contexts, so they load as before.
- `app/utils/navigation.ts`, two new pure functions:
  - `mergeContext(current, remembered)` implements the table of the spec. Without a current organization it returns a copy of the remembered context.
  - `accessibleContext(remembered, accessMap)` drops a remembered organization without any membership (`hasOrganization`). It also drops a project without a role (`roleRank(...) === 0`), together with its view. It returns a copy, never the stored object itself.

### 2. The context guard remembers every allowed context

`app/router/guards/context.ts` calls `rememberContext(mergeContext({ org, project, view }, accessibleContext(lastContext, accessMap)))` for every allowed route. It no longer requires all three parts. The guard's doc comment says so.

- A forbidden route still leaves `lastContext` alone.
- A route without an organization returns before this code, as before.
- Because the remembered context is filtered by access before the merge, a project the user lost access to is cleaned out of the storage on their next navigation.

### 3. The navigation context

- `navigationScope(current, remembered, accessMap)` in `app/utils/navigation.ts` merges the route with the remembered context, after the access filter, and adds `view: 'default'` to a project without a view.
- New composable `app/composables/useNavigationContext.ts`, auto-imported, so `app/auto-imports.d.ts` was regenerated:
  - It returns a `computed<NavigationContext>` built from the `context` store, `preferences.state.lastContext` and `session.accessMap`.
  - Its roles come from `getRole` on the navigation context, not on the route.
- `buildNavigation` did not change.

### 4. Sidebar and command palette

- `AppSidebar.vue` builds its items from `buildNavigation(navigation.value)`. It no longer uses the `context` store or `useRole()`.
- `CommandPalette.vue`:
  - The **Pages** group uses the same navigation context. `useRole()` is no longer needed there.
  - A new `navigationBase` (the view path of the navigation context) drives the `g a` / `g c` / `g g` / `g t` / `g s` shortcuts.
  - `viewBase`, the route's view, is still used by the annotation items, **Recent** and **New annotation**.
  - The header comment now says "the pages of the navigation menu".
- The context switcher and the section labels did not change.

### 5. `/` redirect

`app/pages/index.vue` reads `accessibleContext(lastContext, accessMap)`:

- With a project, it goes to `/{org}/{project}/{view ?? 'default'}/annotations`.
- With an organization only, it goes to `/{org}`.
- Otherwise it goes to `/orgs`.

The `roleRank` check moved into `accessibleContext`.

### 6. Tests

- **Unit**, 14 new tests:
  - `test/unit/utils/shell.test.ts`, a new "navigation context" block with 11 tests:
    - the six rows of the `mergeContext` table, and the case with nothing remembered;
    - `accessibleContext` with a removed project, a `None` project role, a removed organization, and nothing remembered;
    - `navigationScope` on less specific pages, with the default view, and keeping a forbidden route's own parts.
  - `test/unit/router/guards.test.ts`, three tests:
    - the view is kept on project, organization and global pages;
    - another project or organization replaces the remembered parts;
    - a forbidden route is not remembered.
- **E2E**, new `test/e2e/navigation.spec.ts`, desktop and mobile. On mobile, a `menu()` helper opens the slideover first.
  - "keeps the sections of the view …": from `/acme/billing/staging/configurations`, the test opens Settings, Overview and Organizations from the menu. Each page still links Annotations to `/acme/billing/staging/annotations`, and that link leads back.
  - "updates the sections when another view is opened": after `/acme/billing/default/graph`, the Views page links Annotations to the `default` view.
  - Without the sidebar change, both desktop tests fail (checked by temporarily restoring the old `AppSidebar.vue`).
- **Visual:** `members-desktop-{light,dark}` and `machines-desktop-{light,dark}` were regenerated in the Playwright Linux container (`node test/e2e/run-in-container.mjs --update`). They now show the View section of the default view. The other 36 screenshots matched the old baselines.
- **Results:**
  - `npm run typecheck` and `npm run lint` pass.
  - `npm test`: 185 passed, 14 of them new.
  - `npm run test:e2e` with `PLAYWRIGHT_CHANNEL=chrome`: 63 passed and 1 skipped, desktop and mobile, including the 4 new tests.
  - Visual suite in the container: 40 passed after the update.

### 7. Documentation

`docs/42for.net/platform/admin-ui.md`, "Signing in and the first project", two new paragraphs:

- The sidebar always lists the pages of the last view, project and organization, also on project pages, the organization overview, Organizations and Account.
- Opening another context updates it, and a project without a known view shows `default`.
- The browser remembers the context, and `/` opens it again.

The historical spec `2026-10-09 Administration UI for 2S Platform.md` was not changed.

### Notes

- **Related issues:**
  - 424-7 (PR #70) restores the remembered location of a section when the user comes back to it. With this change, the sidebar's Annotations, Configurations and Graph links on a project page are one more way back, and that guard handles them too.
  - 424-13 (PR #73) also edits `preferences.ts`, in lines next to `rememberContext`. Merging both may need a trivial conflict resolution.
- **`viewSections` in `context-paths.ts`** does not list `deleted`, so switching the view from **Deleted** opens Annotations. This was already the case, and it is not part of this issue.
