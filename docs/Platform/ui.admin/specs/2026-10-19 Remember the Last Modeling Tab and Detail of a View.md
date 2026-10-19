# Remember the Last Modeling Tab and Detail of a View

Linear: [424-67](https://linear.app/424fun/issue/424-67/remember-the-last-modeling-tab-and-detail-of-a-view)

## Problem

In the Administration UI (`src/Platform/ui.admin`), the page memory (424-7) brings the user back to where they left **Annotations**, **Configurations** and **Graph** when they return to one of them from the sidebar. **Modeling** (424-38) is not part of it. Its sidebar entry always opens `/{org}/{project}/{view}/modeling`, which redirects to the **Schemas** tab. A user who was on **Deleted** with **Show cascaded** on, on **Compare views** of a key, or on a template or schema detail, gets the Schemas list instead.

The issue asks Modeling to remember, per view, its last tab, the detail below it and its query (`?cascaded=1` on Deleted, `?key=…&to=…` on Compare views, `?tab=versions…` on a template or schema detail). The plain `/…/modeling` link must restore that location, as the plain list URL of the other sections does.

## Current State

- **The page memory store** (`app/stores/page-memory.ts`):
  - `rememberedSections` (line 10) is `['annotations', 'configurations', 'graph']`.
  - `SectionMemories` (line 55) and the `empty` factories (line 61) are exhaustive over `RememberedSection`, so a new section needs a state shape and a factory.
  - `location(scope, section)` and `rememberLocation(scope, section, fullPath)` keep the last full path per `{org}/{project}/{view}/{section}`, in memory only. `clear()` runs on sign-out.
  - `page(scope, section)` holds the state the URL does not hold. Only the three current pages use it, through `usePageMemory(section)`.
- **The page memory guard** (`app/router/guards/page-memory.ts`):
  - `pageMemoryGuard` (`beforeEach`, line 17) redirects to the remembered location when all of these hold:
    - `to` is the plain list of a remembered section: `viewLocation(to.path).root`, no query and no hash;
    - `from` is not `START_LOCATION`;
    - `from` is not in the same section of the same view;
    - a remembered location exists and differs from `to.fullPath` (line 32).
  - The redirect passes through the guards again. When the remembered location is a detail or has a query, that second pass ends at the first check (line 19), because it is no plain list. The `to.fullPath` comparison only keeps a remembered plain list from being redirected to itself.
  - `rememberPageLocation` (`afterEach`, line 36) records `to.fullPath` after every successful navigation inside a remembered section, including the `router.replace` of a query change.
  - It runs after the auth and context guards (`app/router/index.ts`).
- **Modeling** (424-38):
  - The tabs are pages at `modeling/{tab}`: `schemas`, `templates`, `export-import`, `deleted` and `compare`. The details are below them: `schemas/type/{type}`, `schemas/annotation/{key}`, `schemas/annotation/{key}/type/{type}` and `templates/{type}`.
  - `pages/[org]/[project]/[view]/modeling/index.vue` (line 3) is a route-record redirect: ``definePage({ redirect: (to) => `${to.path.replace(/\/$/, '')}/schemas` })``.
  - The old pages of a view (`schemas/**`, `templates/**`, `export-import`, `deleted`, `compare`) are route-record redirects to their tab, through `movedToModeling` (`app/utils/modeling.ts`).
  - Deleted sets `?cascaded=1` with `router.replace` (`modeling/deleted.vue`, line 27). Compare views sets `?key=` and `?to=` the same way, and preselects the only other view. The template and schema details keep `?tab=versions`, `?v=` and `?from=`.
  - `utils/context-paths.ts` already lists `modeling` in `viewSections`, so `viewLocation('/acme/billing/default/modeling/deleted')` is `{ section: 'modeling', root: false }`, and `viewLocation('/acme/billing/default/modeling')` is `{ section: 'modeling', root: true }`.
- **How a redirect reaches the guards** (Vue Router 5.4.0, `pushWithRedirect` in `vue-router/dist/vue-router.js`, lines 1288–1352):
  - A route-record redirect is resolved before any guard runs. The guard of a plain `/…/modeling` therefore sees `to.path === '/…/modeling/schemas'`, which is not a root. Adding `modeling` to `rememberedSections` alone does nothing.
  - The original location is kept in `to.redirectedFrom`. The redirect target inherits the query and the hash, so `/…/modeling?x=1` lands on `/…/modeling/schemas?x=1`.
  - When a guard returns a location, the next pass gets the first pass's `redirectedFrom`, or the first pass's `to` itself when there was none (`redirectedFrom || toLocation`, the same object). It keeps `replace`, and `afterEach` runs once, for the last pass.
  - Every pass runs the leave guards of the page being left again (`navigate`, line 1366), with the same `from`.
  - The first navigation of the app still has `from === START_LOCATION`.
  - A location returned as a string is parsed and resolved as an object, so its query is written again by `stringifyQuery` (`%3A` becomes `:`, `%20` becomes `+`, `%27` becomes `'`). A location recorded from the address bar, a reload or Back keeps the browser's spelling. The two strings can differ for the same location.
  - The 30-redirect limit (line 1336) only exists in development builds.
- **Unsaved changes** (`app/composables/useUnsavedChanges.ts`): the annotation form, the configuration editor, the template and schema details and the values store detail register a leave and an update guard that asks "Discard changes?" while the editor is dirty. A page memory redirect runs these guards twice, so the user is asked twice when they leave a dirty editor for a remembered Annotations, Configurations or Graph location.
- **How the user enters Modeling:**
  - the sidebar entry and the command palette's Modeling page: the plain `modelingPath(scope)` (`utils/navigation.ts`, line 113);
  - the command palette's tab entries and the `G` then `T`, `S` or `E` shortcuts: an explicit tab (`modelingPath(scope, tab)`);
  - the tabs of `ModelingHeader`: an explicit tab, without the query of the previous one;
  - the context switcher (`viewTarget` and `projectTarget`): the same tab in the other view or project (424-38);
  - links from other pages (a schema layer of a configuration, the result of an import, **Compare** on the Views page) and the close buttons of the details: an explicit tab or detail;
  - the old URLs: a redirect from `schemas/**`, `templates/**`, `export-import`, `deleted` or `compare`.
- **Tests:**
  - `test/unit/router/page-memory.test.ts` runs the guard on a router with one catch-all route, so it never runs a real redirect. Its test "does not redirect links with a query, unremembered sections or the first navigation" (line 82) uses `modeling/templates` as the example of a section that is not remembered.
  - `test/unit/modeling/modeling.test.ts` runs the generated routes (`vue-router/auto-routes`) for the Modeling and the old URLs, without the page memory guard.
  - `test/e2e/annotations.spec.ts` (line 225) leaves Annotations through the sidebar and comes back through it.
  - No unit test covers `useUnsavedChanges`.
- **Documentation:**
  - `docs/42for.net/platform/admin-ui.md` (lines 24–31) lists what Annotations, Configurations and Graph remember, and says that selecting the open screen again starts over. Its Modeling section (line 164) says that `…/modeling` opens **Schemas**.
  - `src/Platform/ui.admin/README.md` (line 167) describes the `page-memory` store for the three pages, and the Modeling note (line 173) says that `modeling/index.vue` redirects to `modeling/schemas`.

## Proposed Changes

### 1. Modeling as a remembered section (`app/stores/page-memory.ts`)

```ts
/** The view sections that come back as the user left them; Modeling only to its last tab, detail and query (424-67). */
export const rememberedSections = ['annotations', 'configurations', 'graph', 'modeling'] as const

/** Modeling keeps nothing outside its URL. */
export type ModelingMemory = Record<string, never>

export interface SectionMemories {
  annotations: AnnotationsMemory
  configurations: ConfigurationsMemory
  graph: GraphMemory
  modeling: ModelingMemory
}

const empty = {
  // …
  modeling: () => ({}),
}
```

- `location`, `rememberLocation`, `page` and `clear` do not change. No page calls `usePageMemory('modeling')`.
- The doc comment of the store names Modeling, and says that it remembers only its location.
- `Record<string, never>` instead of an empty interface, which `@typescript-eslint/no-empty-object-type` rejects.

### 2. The plain Modeling URL behind its redirect (`app/router/guards/page-memory.ts`)

The guard takes the requested plain list either from `to` or, after a route-record redirect, from `to.redirectedFrom`. It lets its own redirect through by the identity of the navigation, not by comparing strings:

```ts
// The navigations this guard sent to a remembered location. Their next pass comes back with the same redirectedFrom,
// and goes through, however the remembered location is spelled.
const restored = new WeakSet<object>()

export function pageMemoryGuard(to: RouteLocationNormalized, from: RouteLocationNormalized): true | RouteLocationRaw {
  if (to.redirectedFrom && restored.has(to.redirectedFrom)) {
    return true
  }

  const target = viewLocation(to.path)
  if (!target || !isRememberedSection(target.section)) {
    return true
  }
  const requested = target.root ? to : redirectedList(to, target)
  if (!requested || Object.keys(requested.query).length > 0 || requested.hash) {
    return true
  }
  if (from === START_LOCATION) {
    return true
  }

  const origin = viewLocation(from.path)
  if (origin && origin.section === target.section && sameView(origin.scope, target.scope)) {
    return true
  }

  const remembered = usePageMemoryStore().location(target.scope, target.section)
  if (!remembered || remembered === to.fullPath) {
    return true
  }
  restored.add(to.redirectedFrom ?? to)
  return remembered
}

/**
 * The plain list of the section that `to` was redirected from: `modeling` opens its first tab, `modeling/schemas`
 * (424-67). A redirect from anywhere else, such as the old `schemas` or `deleted` page of a view, is no plain list.
 */
function redirectedList(to: RouteLocationNormalized, target: ViewLocation) {
  const requested = to.redirectedFrom && viewLocation(to.redirectedFrom.path)
  return requested?.root && requested.section === target.section && sameView(requested.scope, target.scope)
    ? to.redirectedFrom
    : undefined
}
```

**Why the identity.** With `redirectedList`, the second pass of every restore is redirected from a plain list, so it would reach the `to.fullPath` comparison. A remembered location in the browser's spelling (`?key=sbt.O%27Brien` after a reload) never equals the `to.fullPath` that Vue Router writes for it (`?key=sbt.O'Brien`). The guard would redirect again, forever in production. The `restored` set lets the second pass through for any spelling: Vue Router hands it the object the guard saw as `to.redirectedFrom`, or as `to` when there was none. A `WeakSet` keeps no navigation alive, and a later navigation is a new object, so it is never let through by mistake. The `to.fullPath` comparison stays only to skip a redirect to the location the navigation already has.

How the rules apply to Modeling:

| Navigation | `to` seen by the guard | Result |
| --- | --- | --- |
| Sidebar or palette `…/modeling`, from another section, page or view | `…/modeling/schemas`, redirected from `…/modeling` | The remembered location, for example `…/modeling/deleted?cascaded=1`, in one navigation |
| `…/modeling/` (trailing slash), from elsewhere | the same | The same |
| The second pass of that redirect | the remembered location, still redirected from `…/modeling` | Goes through: its `redirectedFrom` is in `restored` |
| `…/modeling` without a remembered location, or with `…/modeling/schemas` remembered | `…/modeling/schemas` | Schemas |
| `…/modeling` from within Modeling of the same view | `…/modeling/schemas` | Schemas: this is how the user starts over, as on the other sections |
| `…/modeling?x=1` or `…/modeling#x` | `…/modeling/schemas?x=1`, `…/modeling/schemas#x` | Kept as they are: the requested URL has a query or a hash |
| The first navigation of the app (typed, bookmarked, reloaded) | any | Kept as it is |
| An explicit tab or detail (`…/modeling/templates`, `…/modeling/templates/exe`), from anywhere | not a root, and not redirected | Kept as it is |
| The context switcher in Modeling | an explicit tab of the other view | Kept as it is (the switcher keeps the tab, 424-38) |
| An old URL (`…/schemas`, `…/deleted?cascaded=1`, …) | its tab, redirected from a path that is not a view section | Kept as it is |

For Annotations, Configurations and Graph, nothing changes for the user:

- Their plain lists are roots, so `requested` is `to`.
- `[org]/[project]/index.vue` redirects to the plain `…/default/annotations`, and `/` replaces itself with the plain `…/annotations` of the last context. Both targets are roots, so they keep working as before.
- The second pass of their restore now ends at the `restored` check instead of the root check.

`rememberPageLocation` does not change. With `modeling` in `rememberedSections`, it records every completed Modeling location: a tab, a detail, a query change through `router.replace`, and the landing of an old URL. It still skips a failed or cancelled navigation, such as **Keep editing** on a dirty template.

The doc comment of `pageMemoryGuard` names Modeling: it comes back to its last tab, detail and query, and its plain URL is read from `to.redirectedFrom`, behind the redirect to its first tab.

The Modeling index redirect, the old URL redirects, `utils/modeling.ts`, `utils/navigation.ts`, `utils/context-paths.ts` and `ModelingHeader.vue` do not change.

### 3. One Discard prompt per navigation (`app/composables/useUnsavedChanges.ts`)

Vue Router runs the leave and update guards again on the second pass of a redirect. Today a dirty editor left for a remembered Annotations, Configurations or Graph location asks "Discard changes?" twice. With section 2, the Modeling entry would ask twice too. When the user picks **Discard** and then **Keep editing**, the second answer cancels a navigation they had confirmed.

The guard remembers the navigations the user discarded for, by the same identity as in section 2:

```ts
// The navigations the user chose Discard for. A redirect, such as the page memory's, runs this guard again with the
// first pass as its redirectedFrom: the user is asked once per navigation (424-67).
const discarded = new WeakSet<object>()

const guard = async (to: RouteLocationNormalized, from: RouteLocationNormalized) => {
  if (!isDirty() || options.ignore?.(to, from) || to.path === from.path) {
    return true
  }
  if (to.redirectedFrom && discarded.has(to.redirectedFrom)) {
    return true
  }

  const discard = await confirm({ … })
  if (discard) {
    discarded.add(to.redirectedFrom ?? to)
  }
  return discard
}
```

- The set is per editor (inside `useUnsavedChanges`), so one editor's Discard never lets another editor's changes go.
- A navigation cancelled after Discard (by another guard) leaves its object in the set. The next click is a new navigation, a new object, and asks again.
- A redirect before the first prompt, such as Modeling's route-record redirect, is not affected: the prompt adds the `redirectedFrom` the next pass will carry.

### 4. Unit tests

`test/unit/router/page-memory.test.ts`:

- **The existing test** "does not redirect links with a query, unremembered sections or the first navigation" uses `modeling/templates` as a section that is not remembered. It changes to a project page (`/acme/billing/settings`), which keeps its purpose. Its query and first-navigation cases do not change.
- **A new `describe('the page memory of Modeling')`** runs the generated routes (`routes` from `vue-router/auto-routes`) with `createMemoryHistory()`, as `modeling.test.ts` does. Its router registers a counting `beforeEach` spy **before** `installPageMemoryGuard`, because Vue Router stops a pass at the first guard that returns a location. It covers:
  - **Restoring**, after leaving Modeling for Annotations and pushing the plain `…/modeling`:
    - Deleted with `?cascaded=1`;
    - a template detail, `…/modeling/templates/exe`;
    - Compare views with `?key=sbt.northwind&to=staging`;
    - a schema detail, `…/modeling/schemas/annotation/sbt.northwind/type/exe`.

    Each lands in one navigation: the spy sees two passes (the redirect and its second pass), an `afterEach` spy runs once, and Back returns to Annotations.
  - **The trailing slash:** `…/modeling/` restores the same location.
  - **The browser's spelling:** a first navigation to `…/modeling/compare?key=sbt.O%27Brien&to=staging` (as a reload or Back records it), then Annotations, then the plain `…/modeling`, lands on `…/modeling/compare?key=sbt.O'Brien&to=staging` in two passes, without "Infinite redirect in navigation guard".
  - **Kept as they are:**
    - an explicit `…/modeling/schemas` and `…/modeling/templates` from Annotations;
    - `…/modeling?x=1` and `…/modeling#x`, which land on `…/modeling/schemas?x=1` and `…/modeling/schemas#x`;
    - the first navigation to `…/modeling`;
    - the old `…/schemas` and `…/deleted?cascaded=1`, which land on their own tab;
    - the plain `…/modeling` from within Modeling, which lands on `…/modeling/schemas`;
    - the plain `…/modeling` without a remembered location.
  - **Views apart:** the `default` and `staging` views of `acme/billing` keep their own Modeling location, and the plain `…/modeling` of one view, pushed from the other view's Modeling, restores its own location.
  - **Recording:**
    - a query change through `router.replace` on Deleted is remembered;
    - a cancelled navigation into Modeling (a `beforeEach` that returns `false`) records nothing;
    - `clear()` forgets the Modeling location.
- **The other sections behind a redirect**, on the same generated routes:
  - the plain `/acme/billing` (the project index, redirected to `…/default/annotations`) restores the remembered Annotations location;
  - a first navigation to `…/annotations/sbt.northwind?q=type%3Asbt`, then `/acme/billing/settings`, then the plain `…/annotations`, lands on `…/annotations/sbt.northwind?q=type:sbt` in two passes.

`test/unit/components/unsaved-changes.test.ts` (new): a page with `useUnsavedChanges(() => true)`, mounted with `mountScreen` on a router with the page memory guard and a remembered location of another section:

- the plain list of that section asks "Discard changes?" once; **Discard** lands on the remembered location;
- the same through the Modeling redirect (`…/modeling` → `…/modeling/schemas` → the remembered location);
- **Keep editing** keeps the page;
- after a navigation that another guard cancelled after **Discard**, the next click asks again;
- an ordinary navigation, without a redirect, asks once, as before.

### 5. End-to-end test (`test/e2e/navigation.spec.ts`)

A new test after "a link opens its tab of Modeling, and a detail link opens the tab and the detail", for desktop and mobile. It opens the app once with `openApp` and then uses only clicks, so the in-memory store survives (a `goto` reloads the app). It follows the annotations test (`annotations.spec.ts`, line 225), and uses `menu()`, `modelingTab()` and `relativeUrl()` (`openApp`'s `mock-scenario` stays in the remembered query):

1. Open `…/modeling/deleted` and turn on **Show cascaded**: the URL has `?cascaded=1`.
2. Leave for **Annotations** and return through **Modeling** in the menu: the URL is `…/modeling/deleted?cascaded=1`, the Deleted tab is selected and **Show cascaded** is on.
3. On desktop only (on a phone the detail covers the screen, with the menu button, until it is closed, as in the annotations test): open the **Templates** tab and the Execution template (`getByRole('link', { name: /^Execution\b/ })`, since "Unit of execution" contains the word too), leave for **Configurations** and return through **Modeling**. The URL is `…/modeling/templates/exe`, the Templates tab is selected and the "Execution template" region is visible.
4. Select **Modeling** in the menu again: the URL is `…/modeling/schemas` and the Schemas tab is selected.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - the page memory paragraph (line 24): the sentence about the sidebar, the switcher and the command palette stays about Annotations, Configurations and Graph. A new sentence says that **Modeling** comes back to its last tab, the open template or schema, and the query of the tab (Show cascaded, the compared key and view) when you return through its sidebar entry or the command palette's **Modeling** entry, while the switcher keeps the tab you are on and the palette's tab entries and `G` then `T`, `S` or `E` open the tab they name;
  - the Modeling section (line 164): the sidebar entry returns to the last tab of the view; opening Modeling for the first time, or selecting it again while it is open, opens **Schemas**.
- `src/Platform/ui.admin/README.md`:
  - the client state list (line 167): the `page-memory` store also keeps the last Modeling location, restored behind the redirect of `modeling/index.vue` (`to.redirectedFrom`), and the guard lets its own redirect through by the navigation's identity;
  - the Modeling note (line 173): `modeling/index.vue` redirects to `modeling/schemas`, and the page memory guard then restores the last Modeling location of the view;
  - the e2e list: the Modeling flows name the return to the last tab, detail and query.

### Out of scope

- **The context switcher in Modeling** keeps the current tab in the other view (424-38, 424-64) instead of restoring that view's last Modeling location. Changing it would change the switcher's rule for every section.
- **The palette's tab entries and `G` then `T`, `S` or `E`** open the tab they name, not the remembered location.
- **The hash** of a Modeling location is remembered as the pages leave it. Deleted and Compare views drop it when they replace their query, and this does not change.
- **A Modeling URL that shows Page not found** (an unknown tab) is remembered like any other location of the section, as on the other sections.
- **Remembering across reloads and tabs**, as for the other sections.

## The CodeRabbit Plan

The issue has a coding plan from CodeRabbit, taken as a second opinion:

- **Taken:**
  - `modeling` in `rememberedSections` with an empty state (`Record<string, never>`), and no change to `rememberPageLocation`;
  - the requested plain list taken from `to.redirectedFrom` only when it is a root of the same section and view as `to`, with the query and hash checks on it, and the existing `START_LOCATION` and same-section rules;
  - the Modeling index redirect, the old URL redirects and the Modeling utilities kept as they are;
  - the unit tests on the generated routes, with its list of restoring, excluded, view and recording cases, and the change of the existing unremembered-section example;
  - an e2e test that uses only clicks after the first load.
- **Changed:**
  - The plan keeps the `to.fullPath` comparison as what stops the guard's own redirect. With `redirectedFrom` accepted, that comparison decides every restore, and a location recorded in the browser's spelling loops forever in production. The guard lets its own redirect through by the navigation's identity instead (section 2).
  - The plan does not see that a guard redirect asks a dirty editor twice. Section 3 makes it ask once, for the existing sections too.
  - The plan's e2e test opens the template detail on mobile too, with `openFromList`. On a phone, the detail covers the screen and its menu button, so the user cannot leave it through the menu; the detail part runs on desktop only, as in the annotations test. `openFromList` opens an annotation row, not a template.
  - The guard first checks that `to` is in a remembered section, and computes the requested list only then.
  - Unit tests for the project index redirect and for a location in the browser's spelling are added, to show that the change keeps the other sections working.
  - The documentation names the page memory of Modeling in `admin-ui.md` too, not only in the README.
