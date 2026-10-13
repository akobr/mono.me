# Remember the State of the Annotations, Configurations and Graph Pages

## Overview

This review covers the whole of [Remember the State of the Annotations, Configurations and Graph Pages](../2026-10-13%20Remember%20the%20State%20of%20the%20Annotations,%20Configurations%20and%20Graph%20Pages.md), the plan for Linear issue [424-7](https://linear.app/424fun/issue/424-7/remember-state-of-pages-annotations-configurations-graph). It was delivered in three code commits and one documentation commit.

The Annotations, Configurations and Graph pages of the Administration UI now come back as the user left them. This covers:

- the filter, the open detail and its tab;
- the tree expansion, the bulk selection, the grid sorting, the keyboard cursor and the scroll position;
- the graph's layout, zoom and pan, dragged nodes and hidden types.

The state is restored when the user returns through the sidebar, the context switcher, the command palette or a redirect. It is kept per view, in memory only.

**Checks run:**

- `npm run typecheck`
- `npm run lint`
- `npm test`: 181 unit tests, 10 of them new.
- `npm run test:e2e` in the installed Chrome (`PLAYWRIGHT_CHANNEL=chrome`): 61 passed and 1 skipped, desktop and mobile. This includes one new flow.

**Graph check.** The graph was also checked with a temporary Playwright script, which was not committed. With the Canvas 2D renderer, it compared the graph's canvases before leaving and after returning: zoomed, panned, one node dragged, and with and without a hidden type.

- With the change, the canvases are pixel-identical.
- Without it (the earlier `AnnotationGraph.vue`), the run fails.

**Not run:** the visual suite (it needs the Playwright Linux image) and the real API.

## What Was Done

### 1. Page memory store

`app/stores/page-memory.ts` is the Pinia store `usePageMemoryStore`.

- **API:** `location`, `rememberLocation`, `page` and `clear`, with `rememberedSections = ['annotations', 'configurations', 'graph']` and `isRememberedSection`.
- **Entries:** the section memories (`AnnotationsMemory`, `ConfigurationsMemory`, `GraphMemory`) are `reactive` objects, created on first use, in plain (non-reactive) maps keyed `{org}/{project}/{view}/{section}`. `page()` is called inside computeds, so creating an entry never triggers the computed that reads it.
- **Sign-out:** the store is cleared through `onSignOut`, like the catalog snapshots.

`app/composables/usePageMemory.ts` returns the memory of `context.scope`.

- When the route briefly has no view before the page unmounts, it keeps the previous value through the computed's `previous` argument.

**Deviation from the spec:** `GraphLayoutMemory` became `GraphFrameMemory`, in `GraphMemory.frame`. It also stores the frame's `layout` (layered, force or radial). Without it, a frame remembered in the radial layout would "resume" at rest under the layered forces when the graph is opened from a link without `?layout=`. Resuming now also requires the same layout.

### 2. Location memory and the restoring guard

**`viewLocation(path)`** (`app/utils/context-paths.ts`) returns `{ scope, section, root }`. It is built on `currentSection`, so the reserved top-level names stay excluded.

**`app/router/guards/page-memory.ts`** is installed after the context guard in `app/router/index.ts`. It has two parts:

- `pageMemoryGuard` (`beforeEach`)
- `rememberPageLocation` (`afterEach`)

The rules are those of the spec:

- **Redirect** only for the plain list of a remembered section (no query, no hash), and only when the user arrives from outside that section of the same view.
- **Never redirect from `START_LOCATION`.** A typed, bookmarked or reloaded URL is shown as it is. This also keeps the e2e tests, which navigate with `page.goto`, unaffected.
- **Record nothing for a cancelled navigation**, for example one refused by `useUnsavedChanges`.

The redirect keeps the push of the original navigation, so Back returns to the page the user came from. A unit test covers this.

**Found during the e2e work:** the project pages (Settings, Members, Machines, ...) have no view in the route. The sidebar there shows no view section, which is Linear issue 424-17. From those pages the user returns through the context switcher or Views, and the guard handles both. The new e2e flow therefore moves between Annotations and Configurations instead of Settings.

### 3. Annotations page

`app/pages/[org]/[project]/[view]/annotations.vue`:

- `rowSelection`, `expanded`, `sorting`, `activeKey` and the scroll offset are writable computeds over `usePageMemory('annotations')`.
- The immediate `context.scope` watcher only loads the catalog now. It no longer clears the selection on every mount.
- `selectedKeys` keeps only keys still in the catalog.

`AnnotationGrid.vue`:

- `sorting` is now `defineModel('sorting')`.
- The grid exposes `scrollElement()`.

`AnnotationList.vue` exposes `scrollElement()`.

`app/composables/useScrollMemory.ts`:

- It restores the offset after the DOM update, once the element exists and there are rows.
- If the virtualized content was not sized yet, it retries once on the next animation frame.
- It records the offset on `scroll` only after the restore.
- The page passes the grid's element on desktop and the list's on phones.

### 4. Configurations page

`configurations.vue` gives the `UTable` (desktop) and the list (phone) template refs and uses `useScrollMemory` with `usePageMemory('configurations').scrollTop`. The filter, the open configuration and its tab come back through the location memory.

### 5. Graph

`AnnotationGraph.vue`:

- `hidden` is a writable computed over `usePageMemory('graph')`.
- Every frame stores a `markRaw` `GraphFrameMemory`: the layout, the node keys (a computed per graph), the transferred positions, the alpha and the keys of the pinned nodes.
- `startSimulation(next, from)` takes a `LayoutSeed`. `layoutSeed(current)` returns the current positions while there are any, otherwise the remembered frame. It is called both in the `visibleKeys` watcher and in `onMounted`, which used to start without a seed.
- **Resuming.** When every node is known and the layout is the same, the init message carries `resume: { alpha, pinned }` and the page restores its `pinned` set.
- **Zoom.** When a remembered frame keeps the shape, the remembered zoom is put back through `GraphCanvas.restore`. Without a remembered zoom, the graph is fitted on the first frame as before.

`GraphCanvas.vue`:

- It exposes `restore(transform)`, which goes through `behavior.transform`, so d3-zoom's internal state matches.
- It emits `transform` on the zoom behaviour's `end` event. This covers gestures, `fit`, `reveal` and `restore`.

`simulation-protocol.ts` has the optional `SimulationInit.resume`.

`simulation.worker.ts`, in `init`:

- With `resume` and every node known, it uses the remembered alpha and pins the listed nodes (`fx`, `fy`).
- If that alpha is below `alphaMin`, it posts the frame once and does not start the loop, so a settled graph does not move at all.
- Without `resume`, the behaviour is unchanged.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`: a paragraph on what the three screens remember, how to start over (select the open screen again in the sidebar), and that a reload, a sign-out or a typed URL starts from the URL alone.
- `src/Platform/ui.admin/README.md`: the `page-memory` store in the client state list, and the guard in the `router/` line.

### 7. Tests

- **`test/unit/router/page-memory.test.ts`** (new): `viewLocation`, plus the guard on a real memory router. It covers:
  - restoring the filter, the detail and the tab, with Back afterwards;
  - keeping sections and views apart;
  - the context switcher;
  - no redirect from the same section, for links with a query, for unremembered sections and on the first navigation;
  - cancelled navigations, `clear()` and the store entries.
- **`test/unit/components/scroll-memory.test.ts`** (new): no write before the restore, the restore once ready, and recording afterwards.
- **`test/unit/graph/graph.test.ts`**:
  - a `resume` init at alpha 0 posts the seed exactly and stays at rest;
  - a `resume` at alpha 0.5 keeps the pinned node fixed while the rest moves;
  - the hidden types survive an unmount and a mount of `AnnotationGraph`.
- **`test/e2e/annotations.spec.ts`**:
  - Desktop: filter, open `sbt.northwind`, open History, go to Configurations through the sidebar, come back through the sidebar, and find the same URL, filter and History region. Selecting Annotations again then gives the plain list.
  - Phone: the filter part of the same flow, through the navigation slideover.

### 8. After the rebase on main

`main` brought the detail panel (overlay, pinned or full screen) and the tree with a row per path. Two changes followed:

- **Row ids.** In the tree, an annotation with several parents has a row per path, and `main` keys the expansion and the keyboard cursor by row id (the keys of the path, joined with `/`). `AnnotationsMemory.activeKey` became `activeId`. `expanded` holds row ids, and `selection` stays by annotation key, which the grid maps to every copy of a row. Both perspectives (by subject, by responsibility) share one `expanded` set; their ids never collide.
- **Scroll behind a covering detail.** A full-screen detail, and every detail on a phone, hides the list with `display: none`. The browser drops the offset of a hidden element and reports the reset as a scroll once the list shows again, so `useScrollMemory` recorded 0 and the list stayed at the top, also after coming back. `useScrollMemory` now:
  - ignores scrolls while the element is hidden (`checkVisibility()`, with `getClientRects()` as the fallback);
  - watches the element with `useResizeObserver`; hiding and showing change its size, and when it shows again the remembered offset is put back;
  - waits for the list to show when it was mounted hidden (a detail URL opened in full screen).
- **Tests:**
  - `test/unit/components/scroll-memory.test.ts`: the offset survives hiding, the reset scroll on showing and comes back; a list mounted hidden gets its offset once shown. `ResizeObserver` is a stub that the test triggers.
  - `test/e2e/annotations.spec.ts`: scrolls the list to 1500 px, opens a row (full screen on desktop), closes it, and finds the same offset; again after Configurations and back. On desktop it failed before the change (0 instead of 1500). On a phone it passed before too, and stays as a guard.
- **Results:** `npm test` 305 passed, `npm run lint` and `vue-tsc` clean, `npm run test:e2e` 79 passed and 3 skipped, the visual suite 40 of 40.

### Not done

- **Unsaved editor drafts** are still guarded by `useUnsavedChanges`, not remembered.
- **Templates, Schemas, Compare and Deleted** are not remembered. Adding them needs only an entry in `rememberedSections`, plus a memory type if they have state outside the URL.
- **The keyboard cursor of the graph** (`activeKey`) is not remembered. It follows the selected key as before.
- **The remembered scroll offset is not re-applied** when the view switcher changes the view while the page stays mounted. Switching to the view's memory happens, but the list then starts at the top.
