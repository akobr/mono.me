# Remember the State of the Annotations, Configurations and Graph Pages

Linear: [424-7](https://linear.app/424fun/issue/424-7/remember-state-of-pages-annotations-configurations-graph)

## Problem

In the Administration UI (`src/Platform/ui.admin`), the Annotations, Configurations and Graph pages lose their state when the user leaves them. A user might filter the annotations, switch to the tree, expand a few branches and open an annotation on the right. If they then visit Settings and come back through the sidebar, they get an unfiltered grid at the top with no detail open. The Configurations page and the Graph behave the same way: the graph lays itself out again from scratch, fits itself to the screen and forgets the hidden types and the dragged nodes.

The pages should come back exactly as the user left them.

## Current State

- **Where the state lives.**
  - **In the URL** (spec [Administration UI for 2S Platform](2026-10-09%20Administration%20UI%20for%202S%20Platform.md), section 5):
    - Annotations: `q`, `mode` and `by` (`composables/useAnnotationFilter.ts`).
    - Graph: `layout`, `focus`, `depth` and `all` (`components/graph/AnnotationGraph.vue`).
    - The open annotation or configuration, with its detail tab, is a nested route: `…/annotations/{key}/configuration`, `…/configurations/{key}/versions`, `…/graph/{key}`. The configuration sub-tabs use `?cfg=`.
  - **In local component state, lost on unmount:**
    - `pages/[org]/[project]/[view]/annotations.vue`: the tree expansion (`expanded`), the bulk selection (`rowSelection`, which is also cleared on every mount by the immediate `context.scope` watcher) and the keyboard cursor (`activeKey`).
    - `components/annotations/AnnotationGrid.vue`: the grid sorting (`sorting`). The column visibility and sizes are already persisted in `preferences.grids.annotations`.
    - Scroll position: the scroll containers of `AnnotationGrid` (the `UTable` root), `AnnotationList` (phone) and the Configurations `UTable` or list.
    - `components/graph/AnnotationGraph.vue`: the hidden types (`hidden`), the dragged nodes (`pinned`), and the node positions streamed by the simulation worker (`positions`).
    - `components/graph/GraphCanvas.vue`: the zoom and pan (`transform`).
- **How the user leaves and comes back.**
  - The sidebar (`utils/navigation.ts`) links every section to its plain list: `/{org}/{project}/{view}/annotations`, `…/configurations` and `…/graph`. The command palette's Pages group does the same.
  - The context switcher (`utils/context-paths.ts`, `viewTarget` and `projectTarget`) also goes to the plain list of the same section in the other view.
  - `[org]/[project]/index.vue` and `/` redirect to `…/annotations`.
  - Because these links carry no query and no key, the URL part of the state is dropped too.
- **Graph start-up.**
  - `AnnotationGraph` builds the graph and starts the worker on mount (`startSimulation`). The positions are seeded from the previous graph only while the component stays mounted.
  - The worker (`simulation.worker.ts`, `handleCommand('init')`) starts a seeded layout at alpha 0.3 (more than half the nodes known) or 0.6, so the nodes always move.
  - The first frame fits the graph to the screen, unless the seed kept the shape.
- **Router guards** (`router/index.ts`): `auth`, then `context`. The context guard already remembers the last scope in `preferences.lastContext`.

## Proposed Changes

### 1. Page memory store (`stores/page-memory.ts`)

A new Pinia setup store, `usePageMemoryStore`, holds what the three pages need to come back. It is kept per view (`{org}/{project}/{view}`) and per section.

```ts
export const rememberedSections = ['annotations', 'configurations', 'graph'] as const
export type RememberedSection = (typeof rememberedSections)[number]

export interface AnnotationsMemory {
  expanded: Set<string>
  selection: RowSelectionState
  sorting: SortingState
  activeKey?: string
  scrollTop: number
}

export interface ConfigurationsMemory {
  scrollTop: number
}

/** A graph layout as it was left: positions by key, the alpha it had cooled to, and the dragged nodes. */
export interface GraphLayoutMemory {
  keys: string[]
  positions: Float32Array
  alpha: number
  pinned: string[]
}

export interface GraphMemory {
  hidden: Set<AnnotationTypeCode>
  transform?: { k: number; x: number; y: number }
  layout?: GraphLayoutMemory // markRaw: positions of up to 20,000 nodes
}
```

API:

- `location(scope, section): string | undefined` and `rememberLocation(scope, section, fullPath)`: the last full path (path, query and hash) of a section.
- `page(scope, section)`: the reactive memory of a section, created on first use.
- `clear()`: also registered with `onSignOut`. Sign-out reloads the app anyway, but the next user must never see the previous user's state.

**In memory only, not in `localStorage` or `sessionStorage`.**

- A reload or a typed or bookmarked URL shows exactly that URL.
- A new tab starts clean.
- The e2e tests, which open every screen with `page.goto`, keep their current behaviour.
- The graph positions (up to 20,000 × 2 floats) never have to be serialized.

A composable `usePageMemory(section)` (`composables/usePageMemory.ts`) returns a computed memory for the current `context.scope`.

- When the scope changes (the view switcher), the page switches to that view's memory.
- Outside a view (for the moment between leaving a view and unmounting the page), it keeps the previous value.

### 2. Location memory and the restoring guard (`router/guards/page-memory.ts`)

A new helper `viewLocation(path)` in `utils/context-paths.ts` returns `{ scope, section, root }` for a path under `/{org}/{project}/{view}/{section}…`, or `undefined`. `root` means the path is the section's plain list.

The guard is installed after the context guard:

- **`afterEach(to, from, failure)`**: on a successful navigation into a remembered section, it records `to.fullPath`. Every filter keystroke (`router.replace`), opened annotation and detail tab updates it.
- **`beforeEach(to, from)`**: it redirects to the remembered location when all of these hold:
  - `to` is the plain list of a remembered section: `root`, no query and no hash.
  - `from` is not `START_LOCATION`. A typed, bookmarked or reloaded URL is shown as it is.
  - `from` is outside that section of the same view: another page, another section, or another view through the context switcher.
  - A remembered location exists and differs from `to.fullPath`.

  The redirect keeps the push or replace of the original navigation, so Back still returns to the page the user came from.

Navigations inside the section are never redirected. These are closing the detail (the close button or Escape), clearing the filter, and selecting the sidebar entry of the section already open. Selecting the open section's sidebar entry again therefore still gives the plain list, which is how the user starts over. Links that carry a key or a query, such as Open as list, a configuration's Annotation link or the deleted-key links, are never redirected.

### 3. Annotations page

`pages/[org]/[project]/[view]/annotations.vue`:

- `expanded`, `rowSelection` and `activeKey` become writable computeds over `usePageMemory('annotations')`.
- The immediate `context.scope` watcher no longer clears the selection: the selection belongs to the view's memory.
- `selectedKeys` keeps only the keys still in the catalog. An annotation deleted elsewhere in the meantime then never reaches a bulk action.

`AnnotationGrid.vue`:

- `sorting` becomes a `defineModel('sorting')`, bound by the page to the memory.
- The grid exposes `scrollElement()` (the `UTable` root).

`AnnotationList.vue` exposes `scrollElement()` too.

**Scroll memory**, in a new composable `useScrollMemory(element, offset, ready)` (`composables/useScrollMemory.ts`):

- Once the element exists and `ready()` is true (there are rows), it sets `scrollTop` to the remembered offset, after the DOM update.
- If virtualization has not sized the content yet, it tries again on the next animation frame.
- After that, scroll events write the offset back.
- Before the restore, nothing is written, so an empty list during loading cannot overwrite the memory.

The page uses it with the grid on desktop and the list on phones.

### 4. Configurations page

`pages/[org]/[project]/[view]/configurations.vue`:

- The filter, the open configuration and its tab are already in the location (section 2).
- The scroll position of the `UTable` (desktop) or the list (phone) uses `useScrollMemory` with `usePageMemory('configurations').scrollTop`.

### 5. Graph

`components/graph/AnnotationGraph.vue`:

- `hidden` becomes a writable computed over `usePageMemory('graph')`.
- **Layout memory.** Every frame stores a `markRaw` `GraphLayoutMemory`:
  - the node keys, cached per graph;
  - the streamed positions;
  - the frame's alpha;
  - the keys of the pinned nodes.

  Storing a frame is a reference assignment. The positions buffer is transferred to the main thread and never reused by the worker.
- **Seeding.** `startSimulation` takes a seed source `{ index: Map<key, index>, positions, resume? }` instead of the previous `GraphData`:
  - While the component is mounted, the source is the current graph, as today.
  - When there are no current positions (the first graph after mounting), the source is the remembered layout of the view.
  - When every node of the new graph is in the remembered layout, the init message carries `resume: { alpha, pinned }`, where `pinned` is a `Uint32Array` of the indexes in the new graph. The page also restores its `pinned` set.
- **View.** When the remembered layout keeps the shape and a remembered `transform` exists, the canvas is put back to that transform instead of fitting it. Otherwise the graph fits on the first frame, as today.

`components/graph/GraphCanvas.vue`:

- It exposes `restore(transform)`, which applies `zoomIdentity.translate(x, y).scale(k)` through the zoom behaviour, so d3's internal state stays in sync.
- It emits `transform` with `{ k, x, y }` on the zoom behaviour's `end` event, after gestures and after `fit` or `reveal`. `AnnotationGraph` stores it in the memory.

`components/graph/simulation-protocol.ts`: `SimulationInit` gets an optional field.

```ts
/** Resumes a remembered layout of exactly these nodes: the alpha it had cooled to and its dragged nodes. */
resume?: { alpha: number; pinned: Uint32Array }
```

`components/graph/simulation.worker.ts`, in `init`:

- With `resume` and every node known, the simulation starts at the remembered alpha instead of 0.3, and the pinned nodes get `fx` and `fy` at their positions.
- A remembered layout that had settled (alpha below `alphaMin`) is posted once and left at rest. The nodes therefore do not move at all until the user drags a node, reheats or changes the layout.
- Without `resume`, nothing changes.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`: one paragraph on what the Annotations, Configurations and Graph pages remember, and how to start over (select the open section again in the sidebar).
- `src/Platform/ui.admin/README.md`: add the `page-memory` store to the client state list.

### 7. Tests

- **Unit, page memory guard** (`test/unit/router/page-memory.test.ts`). It covers:
  - recording locations per view and section;
  - redirecting a plain list opened from another page or another view;
  - no redirect from the same section, on the first navigation, with a query, without a memory, or for sections that are not remembered;
  - `clear()`.
- **Unit, worker** (`test/unit/graph/graph.test.ts`): a `resume` init at alpha 0 posts exactly the seeded positions and stays at rest, and keeps the remembered pinned nodes fixed.
- **Unit, graph screen**: the hidden types survive an unmount and a mount of `AnnotationGraph`.
- **Unit, scroll memory**: the offset is restored once there is content and recorded on scroll.
- **e2e, annotations** (`test/e2e/annotations.spec.ts`): filter, open an annotation, go to another page through the sidebar, return through the sidebar, and expect the same filter, detail and tab.

## Out of Scope

- **Unsaved editor drafts** (the annotation form and the configuration editor). Leaving them is already guarded by `useUnsavedChanges`.
- **The Templates, Schemas, Compare and Deleted sections.** They can be added to `rememberedSections` later.
- **Remembering across reloads and tabs.** The URL already keeps the shareable part of the state across a reload.
