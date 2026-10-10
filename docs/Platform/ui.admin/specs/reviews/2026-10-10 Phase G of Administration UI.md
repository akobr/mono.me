# Phase G of Administration UI

## Overview

Phase G adds the annotation graph of [Administration UI for 2S Platform](../2026-10-09%20Administration%20UI%20for%202S%20Platform.md), section 12, with these parts:

- a force simulation in a Web Worker;
- three layouts: layered, force and radial;
- focus mode, which is the default above 5,000 annotations;
- hover, selection, drag and pin;
- filter dimming, a type legend, keyboard navigation and PNG export;
- the selected node's detail as a nested route.

The phase measured the graph against the spec's risk rule. With Canvas 2D, a whole view of 16,317 annotations drew at 3–6 fps, below the 30 fps limit. As the spec decides for that case, the bulk of the graph is now drawn with WebGL 2 behind `GraphCanvas`, and it runs at 60 fps.

## What Was Done

### 1. Data (`components/graph/graph-model.ts`)

- **Nodes and links:**
  - `buildGraph(keys, descendants)` builds the nodes, the links, a key index and the undirected neighbours.
  - Links are direct ancestor edges between visible nodes, ancestor → descendant.
  - The node radius is `4 + 2·log2(1 + descendants)`. `countDescendants` counts all transitive descendants in the catalog, using the catalog's cached `ancestorsOf`.
- **Tiers:** `tierOf` is rst, sbt → 0; unt, usg, cnt → 1; exe → 2; uxe → 3.
- **Helpers:**
  - `neighbourhood(start, depth, childrenByKey, exists)`: the k-hop set over ancestors and descendants of the whole catalog.
  - `mostConnectedRoot`: the subject or responsibility with the most children.
  - `topByDegree`: the 50 nodes whose names are always drawn.
  - `sameKeys`: skips a rebuild when the visible set did not change.
  - `neighbourInDirection`: keyboard navigation.

### 2. Simulation (`simulation.worker.ts`, `simulation-protocol.ts`, `layout-seed.ts`)

- **Messages:**
  - `init` carries `generation`, tiers, radii and links as typed arrays (transferred), an optional `seed` of previous positions, the layout, a pre-warm tick count and reduced motion.
  - Commands: `layout`, `pin`, `dragEnd`, `unpinAll`, `reheat` and `stop`.
  - The worker answers with `positions` frames: an interleaved `Float32Array` (transferred), the alpha and the generation. Frames of an earlier graph are ignored.
- **Forces:**
  - The spec's forces: `forceLink` (distance 30), `forceManyBody` (−40, `distanceMax` 400), `forceCollide` (radius + 2), plus `forceX`/`forceY`.
  - Layered: `forceY` to the tier band. Radial: `forceRadial` to the tier ring. Force: a weak pull to the centre.
- **Ticks:** the worker ticks once per animation frame (`requestAnimationFrame` in the worker, or a 16 ms timeout) until alpha drops below `alphaMin`. With reduced motion it runs 5 ticks per frame.
- **Seed layout** (`seedLayout`, a deviation, see below):
  - Each tier becomes a band of rows. A node is ordered by the mean rank of its ancestors, the barycenter heuristic of layered graph drawing.
  - Tier positions and ring radii come from the band heights.
  - When the focus moves, nodes that stay keep their previous position. New nodes start next to a known neighbour, and the simulation only settles gently (alpha 0.3).
- **Large graphs** (above 5,000 nodes):
  - Faster cooling: `alphaDecay` is 0.05.
  - Cheaper forces: Barnes–Hut `theta` 1.2, collision strength 0.6, and the tier pull at 0.2 so the tiers stay bands.
- **Pre-warm:** up to 300 ticks, or 1.5 s, whichever comes first. The rest of the layout is streamed.

### 3. Rendering (`GraphCanvas.vue`, `webgl-renderer.ts`)

- **WebGL 2 (`GraphGlRenderer`, no dependency):**
  - Links are `gl.LINES` over the position buffer with an element index buffer, at 35 % opacity.
  - Arrowheads are instanced triangles, drawn from zoom 0.6.
  - Nodes are instanced quads with an antialiased circle in the fragment shader. They use the type colors from `--st-*`, resolved through a 1×1 canvas, so `oklch()` values work too.
  - Dimmed nodes are drawn first, at 15 %. Nodes are at least 1.5 px wide when zoomed out.
- **Canvas 2D overlay** (on top, and it receives the pointer events):
  - Highlighted links of the hovered or selected node, and the rings of the highlighted nodes.
  - Names: those of the hovered or selected node and its neighbours, the 50 most connected nodes, and above zoom 1.2 the largest nodes in the viewport. A coarse occupancy grid skips names that would overlap; at most 400 are drawn.
- **Canvas 2D fallback:** without WebGL 2, or with `localStorage['2s-admin:graph-renderer'] = 'canvas'`, the overlay draws everything. Links are one batched path, arrowheads appear from zoom 0.6, and there is one path per type.
- **Hit-testing:** a `d3-quadtree` over the latest positions, rebuilt at most every 150 ms.
- **Zoom:** `d3-zoom` with a filter. A press on a node drags the node, a press on the background pans, and pinch always zooms. Double-click zoom is off.
- **Export PNG:** both canvases on the theme background, drawn in the same task (the WebGL buffer is only readable until it is presented). The file is `graph-{org}-{project}-{view}.png`.
- **Detail panel:** when the detail opens and the canvas narrows, the selected node is kept in view.

### 4. Screen (`AnnotationGraph.vue`, `GraphToolbar.vue`, `GraphLegend.vue`, `GraphTooltip.vue`)

- **URL state:** `q` (filter), `layout`, `focus`, `depth` (1–6, 2 by default) and `all`.
- **Focus mode:**
  - Above 5,000 annotations, the graph opens on a start node and writes it to the URL, so the screen can be shared. The start node is the selected one, else the most recently opened one, else `mostConnectedRoot`.
  - In focus mode, the filter adds its matches to the neighbourhood, for example `type:sbt,cnt,exe subject:northwind`.
  - In the whole view, the filter dims the nodes that do not match.
- **Show everything:**
  - Above 5,000 annotations it asks for confirmation, with a notice about load time.
  - It is disabled above 20,000 annotations.
- **Toolbar:**
  - The grid's filter bar (compact), the layout select, and the focus depth with the focus key.
  - Fit, Reheat, Release pinned nodes, Export PNG and Open as list.
  - Show everything, or Focus.
- **Legend:** type toggles with node counts, and "All types". It overlays the top of the graph.
- **Tooltip:** type, name, status, the full key, the title and the number of connections. A long press shows it on touch.
- **Interaction:**
  - Click opens `…/graph/{key}`.
  - Double-click focuses on the node.
  - Drag pins the node. Double-click on the background, or the toolbar button, releases all pins.
  - Arrow keys move to the neighbour in that direction, Enter opens it, Escape closes the detail.
- **Accessibility:**
  - The graph container is focusable (`role="application"`) and its label explains the keys.
  - A polite live region announces the node under the keyboard cursor.
- **States:** catalog loading and layout in progress, error, empty view, and "every type is hidden".
- **Open as list:** shows up to 300 visible nodes in the grid as `key:a,b,…`.

### 5. Routes, detail and navigation

- **Pages:**
  - `[view]/graph.vue` loads the catalog and renders `AnnotationGraph` with a nested `<RouterView>`.
  - `graph/[key].vue` with the children `index`, `configuration`, `versions` and `schema` reuses the annotation tabs.
- **`AnnotationDetailPanel`:** the panel of `annotations/[key].vue`, moved into a component with a `section` prop so both screens use it.
- **`section` prop:** `AnnotationDetail`, `AnnotationBreadcrumb` and `AnnotationLineage` take it, so tabs, breadcrumb and lineage links stay on the graph. The menu has **Show in graph** (`graph/{key}?focus={key}`), and **Show in list** on the graph.
- **Navigation and shortcuts:** a sidebar entry "Graph" between Configurations and Templates, and `G G` in the command palette shortcuts.

### 6. Mock API

The `large` scenario took more than 30 s to load in phase G. Since phase D, `GetConfigurations` rebuilt the 16,317-annotation map and hashed every key once for each configuration document, which is O(n²) per page.

- `annotationsOf` now returns the same map while the overrides of a scope are unchanged.
- `configurationKeys` is cached per map in a `WeakMap`.

The annotation grid of the large scenario is back to 1.6 s for the first rows and 4.2 s for the full catalog.

### Measurements

Measured on a Windows 11 development machine (RTX 2060) with Chrome through Playwright, against the Vite dev server (unminified) and the mock API. FPS counts `requestAnimationFrame` callbacks on the main thread.

| Case | Canvas 2D | WebGL 2 + overlay |
| --- | --- | --- |
| Default view, 367 nodes | 60 fps idle, pan and wheel | 60 fps idle, pan and wheel |
| Large view, focus on `rst.invoicing` (2,919 nodes) | 60 idle / 44 pan / 51 wheel | 60 / 60 / 60 |
| Large view, everything (16,317 nodes, about 30,000 links): first frame after Show everything | 34 s (300 ticks at about 110 ms each) | 1.5 s (seeded bands + time-boxed pre-warm) |
| … while the layout runs | 3–6 fps | 60 fps |
| … settled: pan / wheel | 32 / 3 fps | 60 / 60 fps |

A d3-force tick at 16,000 nodes costs about 70 ms for `forceManyBody` and about 120 ms for `forceCollide` from random positions. This is why the seed layout replaced the fixed 300-tick pre-warm.

### Deviations from the spec

1. **Renderer:**
   - The renderer is raw WebGL 2, not PixiJS or regl, because it needs only three small programs.
   - Names and highlights stay on a Canvas 2D overlay.
   - Canvas 2D remains as the fallback.
2. **Pre-warm:** the seeded layered layout replaces the fixed 300-tick pre-warm. The pre-warm is capped at 300 ticks or 1.5 s, and the rest is streamed.
3. **Filter:** in focus mode the filter widens the visible set; in the whole view it dims the nodes that do not match. The spec describes both, but not when each applies.
4. **Node size:** counts all descendants, not only the visible ones.
5. **Keyboard navigation:** it lives on the focusable graph element, not in global shortcuts, so open menus and editors keep their arrow keys.
6. **Open as list:** uses the existing `key:` qualifier. It is a prefix match, so a listed name can also bring in a sibling whose name extends it.
7. **Pinned nodes:** a dragged node stays pinned until it is released. There is a toolbar button for this besides the double-click on the background.

## Tests

`npm test` runs 145 tests in 18 files, and all pass. New in phase G is `test/unit/graph/graph.test.ts`:

| Area | Covers |
| --- | --- |
| Graph model | links and radius; hidden nodes; k-hop neighbourhood; focus start; degree ranking; `sameKeys`; arrow-key neighbour |
| Layout seed | tier bands from top to bottom; barycentric order; radial rings |
| Simulation worker | `init` frames with the generation; a pinned node; a new graph keeps the seed positions |
| Graph screen (MSW) | the whole view with legend counts; focus from the URL; depth; the filter widening the focus; a 5,003-annotation view opening in focus mode on the most connected subject |
| Navigation | the Graph entry |

These also pass:
- `vue-tsc --build`;
- oxlint and ESLint, with no findings;
- `npm run build` (the worker is its own 18 kB chunk, the graph route 86 kB).

The graph was also checked in mock mode, in both themes and at 390 px:
- hover, click to detail, arrow keys, Escape;
- Show in graph, the radial layout, type toggles and PNG export;
- the large scenario in focus and whole-view modes.

There were no console errors.

## Follow-ups

- The time slider (spec section 12) stays deferred.
- Phase H mobile pass: collapse the legend on small screens.
- Phase H: Playwright flows for the graph.
- An exact-key filter (`key:=…`) would make "Open as list" precise.
