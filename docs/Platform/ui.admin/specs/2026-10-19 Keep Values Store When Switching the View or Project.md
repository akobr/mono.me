# Keep Values Store When Switching the View or Project

Linear: [424-68](https://linear.app/424fun/issue/424-68/keep-values-store-when-switching-the-view-or-project)

## Problem

The switcher in the top bar of the Administration UI (`src/Platform/ui.admin`) changes the project or the view and keeps the section the user is on. The public docs say so: "The switcher at the top changes the organization, project or view and keeps the screen you are on". **Values store** (`/{org}/{project}/{view}/key-values`) is the exception. Switching the view or the project there opens **Annotations** of the new view instead of its Values store. The same happens from the detail of a key (`…/key-values/{key}`) and from its versions (`…/key-values/{key}/versions`).

The issue asks for Values store to be kept, without the selected key, and for the page-memory guard to be checked.

## Current State

All paths are relative to `src/Platform/ui.admin/src`.

- **Context paths:** `app/utils/context-paths.ts`.
  - `viewSections` (line 8) is `annotations`, `configurations`, `graph`, `modeling`. `ViewSection` is derived from it.
  - `currentSection()` (line 37) returns `{ kind: 'view', name }` only when the fourth segment is one of `viewSections`. For `/acme/billing/staging/key-values` it returns `{ kind: 'org', name: undefined }`.
  - `projectTarget()` (line 79) and `viewTarget()` (line 93) keep a view section through `viewSectionPath()` (line 73), which adds the tab on Modeling only. Any other path falls back to `default/annotations` (line 88) and `annotations` (line 95). Both return a path without the query or the hash.
  - `viewLocation()` (line 63) is `undefined` for a path that is not a view section, so it is `undefined` for every Values store path today.
- **Switcher:** `app/components/shell/ContextSwitcher.vue`. The project items link to `projectTarget(route.path, …)` (line 41) and the view items to `viewTarget(route.path, …)` (line 55).
- **Sidebar:** `app/utils/navigation.ts`, line 106, lists Values store at `${base}/key-values` between Configurations and Graph. It does not use `viewSections`.
- **Reserved names:** `app/forms/names.schema.ts`, line 26: `key-values` is a reserved project and view name. A view can be named `monitor` or `modeling`, but not `key-values`.
- **Values store pages:** `app/pages/[org]/[project]/[view]/key-values.vue` is the list. `key-values/[key].vue` with `index.vue` and `versions.vue` is the detail, a child route.
  - The queries go through `useScope()` (`app/queries/scope.ts`, lines 77–98), and every action reads `context.scope` when it runs. Both follow a change of the context.
  - The create and import dialogs reset when they open (`watch(open, …)`), and they are modal, so the switcher cannot be used while one is open.
  - `search`, the level filter and the Active or Deleted state follow `?q=`, `?level=` and `?state=`.
  - The selection of the bulk actions is a local `ref`. Line 60 clears it when the state or the view changes: `watch([state, () => context.view], …)`.
- **Component reuse:** `App.vue` renders a plain `<RouterView />`. Vue Router keeps the component of a route record when only the params change. Annotations, Configurations and Graph already rely on this when the view changes. Values store has never been opened this way, because the switcher always left it.
- **Page memory:** `app/stores/page-memory.ts`, line 10: `rememberedSections` is `annotations`, `configurations`, `graph`. The spec [2026-10-13 Remember the State of the Annotations, Configurations and Graph Pages](./2026-10-13%20Remember%20the%20State%20of%20the%20Annotations,%20Configurations%20and%20Graph%20Pages.md) names only these three.
  - The guard (`app/router/guards/page-memory.ts`) restores in `pageMemoryGuard` (line 17) only when the target is the plain list of a remembered section (line 19).
  - It compares the target with `viewLocation(from.path)` (line 26), so that a list opened from within its own section stays as it is.
  - It records in `rememberPageLocation` (line 36) only for a remembered section (line 38).
  - The open PR of 424-67 (`feat/424-67`) adds `modeling` to `rememberedSections`. It keeps the `isRememberedSection()` check first, and its test already uses Values store as an example of a page that is not remembered.
- **Tests:**
  - `test/unit/utils/shell.test.ts`, "context paths" (lines 57–156), covers Configurations, Graph, Modeling and the views named `monitor` and `modeling`. It has no Values store case.
  - `test/unit/router/page-memory.test.ts` has no Values store case.
  - No unit test mounts the Values store list.
  - `test/e2e/navigation.spec.ts` has `viewButton()` (line 69) and the switcher test of 424-64 (line 368). `test/e2e/key-values.spec.ts` has the Values store flows. Neither switches the view or the project from Values store.
- **Mock data:** `app/mock/key-values.ts`, lines 119–136. `acme/billing/default` has seeded pairs, `db.host` among them. `acme/billing/staging` has `db.host` (`sql.staging`) and `db.password`. `acme/shipping/default` has none.
- **Docs:**
  - `docs/42for.net/platform/admin-ui.md`, line 18, says that the switcher keeps the screen. It needs no change.
  - The Admin UI section of `docs/Platform/Storyteller/key-values.md` and the Values store entry of `src/Platform/ui.admin/README.md` (line 172) do not mention the switcher.

## Proposed Changes

### 1. Values store as a view section (`context-paths.ts`)

Add `'key-values'` to `viewSections`, in the order of the sidebar:

```ts
export const viewSections = ['annotations', 'configurations', 'key-values', 'graph', 'modeling'] as const
```

Update the comment above it, so it names the sections. The helpers then give:

| Current path | `viewTarget(…, 'acme', 'billing', 'default')` | `projectTarget(…, 'acme', 'shipping')` |
|---|---|---|
| `/acme/billing/staging/key-values` | `/acme/billing/default/key-values` | `/acme/shipping/default/key-values` |
| `/acme/billing/staging/key-values/db.host` | `/acme/billing/default/key-values` | `/acme/shipping/default/key-values` |
| `/acme/billing/staging/key-values/db.host/versions` | `/acme/billing/default/key-values` | `/acme/shipping/default/key-values` |
| `/acme/billing/staging/key-values?level=Standard&state=Deleted` | `/acme/billing/default/key-values` | `/acme/shipping/default/key-values` |

- `currentSection()` returns `{ kind: 'view', name: 'key-values' }` for all four paths.
- `viewLocation()` returns `root: true` for the list and `root: false` for the detail and the versions.
- `viewSectionPath()` returns `key-values` alone, because only Modeling has tabs. The selected key and the query are dropped, as for the other sections.
- `/acme/billing/monitor/key-values` is the Values store of a view named `monitor`, not the Monitor page, which has no pages below it (424-35).
- `ViewSection` gains `'key-values'`. Nothing has an exhaustive `switch` or a `Record` over `ViewSection`. The only consumers are `currentSection()`, `viewLocation()` and the page-memory guard, which narrows with `isRememberedSection()`. `ContextSwitcher.vue` and `navigation.ts` stay as they are.

### 2. Page memory stays as it is

Values store does not join `rememberedSections`. The page-memory spec names three sections, and Values store keeps nothing outside its URL that would need restoring. Once `viewLocation()` knows Values store:

- `pageMemoryGuard` returns `true` for every Values store target at line 19, because `isRememberedSection('key-values')` is false. No location is read, and no navigation is redirected.
- `rememberPageLocation` records nothing for Values store at line 38.
- A navigation from Values store to a remembered section now has an `origin`, Values store of the same or another view. Its section differs from the target's, so the guard restores the remembered location as before, when `origin` was `undefined`.

No code changes in the guard or the store. The tests of section 4 prove the boundary. The same reasoning holds after 424-67, whose guard checks `isRememberedSection()` before it reads `to.redirectedFrom`.

### 3. Clear the selection of Values store when the project changes (`key-values.vue`)

A switch that keeps Values store keeps the same route record, so Vue Router keeps the page's component and its local state. The selection is cleared when the view changes, but not when the project changes. A project switch from `default` lands on `default` again. For example, select `db.host` on `/acme/billing/default/key-values`, then pick `shipping` in the switcher. Today, the bulk bar "1 selected" stays on `/acme/shipping/default/key-values`, with **Delete** and **Change protection** for the keys of `billing`. They would run against `shipping`, on any key of the same name.

Line 60 watches the project too:

```ts
watch([state, () => context.projectKey, () => context.view], () => (selected.value = new Set()))
```

`projectKey` (`{org}.{project}`) also covers the organization. The rest of the page already follows the context:

- the queries are keyed by the scope;
- the actions read `context.scope` when they run;
- the dialogs reset when they open;
- the filter, the search and the state come from the query, which the switcher drops;
- the detail is a child route, which unmounts because the switcher drops the key.

### 4. Unit tests

- **`test/unit/utils/shell.test.ts`**, "context paths": a new case "keeps Values store, without the key, when switching the project or the view". It checks `currentSection()`, `viewTarget()` and `projectTarget()` for the four paths of the table in section 1, and `viewLocation()` with `root: true` for the list and `root: false` for a key and its versions. A second assertion block covers `/acme/billing/monitor/key-values` (a view named `monitor`).
- **`test/unit/router/page-memory.test.ts`:**
  - "view locations": `viewLocation()` of the Values store list and of a key.
  - "page memory guard", a new case "leaves Values store out of the memory". Each check below must fail if Values store were remembered, or if the origin check stopped the restore:
    1. Visit `${base}/key-values/db.host?q=db`, leave for `/acme/billing/settings`, and come back to `${base}/key-values`. The URL stays the plain list (nothing recorded, nothing restored).
    2. Remember `/acme/billing/other/annotations?q=b`, then go from `${base}/key-values` to `/acme/billing/other/key-values`, as the switcher does. The URL stays the plain list. The remembered location of another section is not used.
    3. Remember `${base}/annotations?q=a`, open `${base}/key-values`, then `${base}/annotations`. The URL becomes `${base}/annotations?q=a`, because Values store is another section of the same view.

    The case goes after "keeps each section and each view apart", so it does not touch the lines that 424-67 changes.
- **`test/unit/key-values/key-values-page.test.ts`** (new). It mounts the Values store list with `mountScreen`, the generated routes, `contextGuard` and the mock API (MSW in node, as in `test/unit/modeling/compare.test.ts`), with `AppNavbar` replaced by a stand-in. The tests:
  - Select `db.host` on `/acme/billing/default/key-values`; the bulk bar shows "1 selected". Push `/acme/shipping/default/key-values`: the bulk bar is gone. This test fails without section 3.
  - The same with `/acme/billing/staging/key-values`, the view switch, which already works today.
  - After the project switch, the list shows the pairs of the new scope: the empty state "No key values in this view" of `acme/shipping`.

### 5. End-to-end test (`test/e2e/key-values.spec.ts`)

"the switcher keeps Values store, without the key and the selection" runs on desktop only, because the view button of the switcher is hidden on a phone:

1. Open `/acme/billing/default/key-values/db.host`, so the detail is open.
2. Pick `staging` in the view switcher. The URL is `/acme/billing/staging/key-values`, the detail is closed, and the list shows `sql.staging`.
3. Select `db.host`. The "Bulk actions" toolbar shows "1 selected".
4. Pick `shipping` in the project switcher. The URL is `/acme/shipping/default/key-values`, the toolbar is gone, and the empty state "No key values in this view" shows.

### 6. Documentation

- `docs/Platform/Storyteller/key-values.md`, Admin UI, the Values store item: the switcher keeps Values store when it changes the view or the project, and closes the open key and the selection.
- `src/Platform/ui.admin/README.md`:
  - The Values store entry (line 172): `utils/context-paths.ts` lists `key-values` as a view section, outside page memory, and the list clears its selection when the project or the view changes.
  - The e2e list gets the new flow.
- `docs/42for.net/platform/admin-ui.md` already says that the switcher keeps the screen. It needs no change.

### Out of scope

- Page memory for Values store: coming back to its last key, filter and scroll position.
- The organization switch, which opens the organization's overview.
- Keeping the filter (`?level=`, `?state=`, `?q=`) through the switcher. No section keeps its query today.

## The CodeRabbit Plan

The issue has a plan from CodeRabbit, taken as a second opinion:

- **Taken:**
  - Add `key-values` to `viewSections` only, and keep it out of `rememberedSections`.
  - Change neither `ContextSwitcher.vue` nor `navigation.ts`.
  - Its test cases for `currentSection()`, `projectTarget()`, `viewTarget()` and `viewLocation()`: the list, a key, a query, and the view named `monitor`.
  - The two page-memory regressions: nothing restored on the root, nothing recorded from a key.
  - The focused Vitest, typecheck and lint runs.
- **Changed:**
  - The plan treats the change as trivial and skips the spec. The documentation workflow of this repository asks for a spec and a review, so both are written.
  - The plan searched the docs for a list of the kept sections. There is none, but the Values store parts of `key-values.md` and of the README get a sentence each.
- **Added:**
  - The plan does not look at the Values store page itself. Section 3 clears the selection when the project changes, because the page now stays mounted through a project switch, and section 4 tests it.
  - A third page-memory check: from Values store back to Annotations of the same view, the remembered location still comes back. It covers the new `origin`.
  - The versions path of a key in the tests.
  - The end-to-end test of section 5.
