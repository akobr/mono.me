# Detail Panel as Overlay, Pinned Panel or Full Screen

Linear: [424-22](https://linear.app/424fun/issue/424-22/make-right-panel-resizable-and-open-able-full-screen)

## Overview

This review covers the whole spec [Detail Panel as Overlay, Pinned Panel or Full Screen](../2026-10-13%20Detail%20Panel%20as%20Overlay%2C%20Pinned%20Panel%20or%20Full%20Screen.md), delivered in one phase.

The detail of an annotation, a configuration, a template, a schema or a machine now opens as an overlay from the right, over the list. The header can pin it beside the list, where its left border resizes it, or open it in full screen in place of the list. The choice and the width are remembered in the preferences, so the next detail opens the same way. `DetailPanel` is now one element in every mode, so switching never remounts the detail or loses an edit. One fix was added beyond the spec: the graph keeps its view while a full-screen detail hides it (section 9).

## What Was Done

### 1. Preferences

`app/stores/preferences.ts` has `detailPinned` (`false`), `detailFullscreen` (`false`) and `detailWidth` (`40`) in `Preferences` and `defaultPreferences`, as planned. The store comment now says that the sidebar size is persisted by `UDashboardGroup` and the detail mode and width are kept here. The old per-panel `2s-admin-panel-*-detail` keys are no longer written.

### 2. Layout rules

`app/utils/detail-panel.ts` exports `DetailMode`, `detailWidth` (`min` 25, `max` 70, `default` 40, `step` 2), `detailMode()` and `clampDetailWidth()`, following the table of the spec. The input type is named `DetailScreen`.

### 3. Shared state

`app/composables/useDetailPanel.ts` (auto-imported, so `app/auto-imports.d.ts` was regenerated) returns `mode`, `covers`, `canPin`, `canFullscreen`, `togglePinned`, `toggleFullscreen` and `track`, as planned. It also returns the `medium` and `large` media queries, which `DetailPanel` needs for the overlay width, the resize handle and the full-screen navbar. A module-level counter of mounted panels drives `covers`.

### 4. `DetailPanel.vue`

`app/components/shell/DetailPanel.vue` no longer uses `UDashboardPanel` or `USlideover`. It renders one `<section :aria-label="title" :data-detail-mode="mode">`:

- **Overlay:** `absolute right-0 bottom-0 top-(--ui-header-height) z-30`, left border, `shadow-xl` and the `slide-in-from-right` animation of Nuxt UI (its keyframes already fade instead under reduced motion). The width is `detailWidth` % at `lg` and 90 % below.
- **Pinned:** `relative shrink-0`. Deviation from the spec: no left border of its own, because the list panel before it already draws one (`lg:not-last:border-e`), which would have doubled the line.
- **Full:** `relative flex-1`, with `<AppNavbar :title="title" />` above the header at `md` and above.
- **Resize handle** (overlay and pinned, `lg` and above): a 12 px `role="separator"` centred on the left border, with `aria-orientation`, `aria-label="Resize the detail"`, `aria-controls` and `aria-valuenow/min/max`. A 2 px line shows on hover, focus and drag.
  - Pointer events with pointer capture; `preventDefault()` on `pointerdown` keeps the drag from selecting text, which was checked in Chrome.
  - The width is rounded to 0.1 % before it is stored.
  - `←` / `→` change it by 2 %, and a double-click resets it to 40 %.
- **Esc** closes the detail when the event target is the page body or inside the panel, and not a field, button, link, menu or dialog. Events the annotation grid already handled (`defaultPrevented`) are ignored. Because the target must be the body or the panel, the graph canvas's own `Esc` is not doubled either.
- The `id` prop is now the `id` of the section (`annotations-detail`, `configuration-detail`, …) instead of the storage key of the panel size.

### 5. `DetailPanelActions.vue`

`app/components/shell/DetailPanelActions.vue` (registered in `app/components.d.ts`) shows **Pin the detail** / **Unpin the detail** (`i-lucide-pin` / `i-lucide-pin-off`) when `canPin`, and **Open in full screen** / **Exit full screen** (`i-lucide-maximize-2` / `i-lucide-minimize-2`) when `canFullscreen`, each with a tooltip. It is placed right before the close button in `AnnotationDetail.vue`, `configurations/[key].vue`, `SchemaDetailPanel.vue`, `templates/[type].vue` and `machines/[id].vue`.

### 6. The list makes room for a full-screen detail

`annotations.vue`, `configurations.vue`, `graph.vue`, `schemas.vue`, `templates.vue` and `machines.vue` call `useDetailPanel()` and bind `:class="{ hidden: detail.covers.value }"` on their `UDashboardPanel`. Nuxt UI merges the classes with tailwind-merge, so `hidden` replaces the panel's `flex`. In `annotations.vue`, the watcher that scrolls back to the closed row runs with `flush: 'post'`.

### 7. Tests

- `test/unit/utils/shell.test.ts`, new "detail panel" block: every row of the mode table and the width limits, including `NaN`.
- `test/unit/components/detail-panel.test.ts` (new, happy-dom at 1024 px, `AppNavbar` stubbed):
  - pin and unpin;
  - full screen with the navbar, no separator and no pin button, then back to the pinned mode;
  - the arrow keys within 25–70 % and the double-click reset;
  - `Esc` from a field does not close the detail, `Esc` from the body does.
- `test/e2e/annotations.spec.ts`, new desktop flow:
  1. open → overlay;
  2. pin → pinned;
  3. drag the separator 150 px → the detail is more than 100 px wider;
  4. full screen → the filter of the grid is hidden;
  5. reload → still full screen;
  6. exit → pinned;
  7. close → the grid is back.
- Visual baselines regenerated in the Playwright container with `npm run test:visual:docker -- --update`. Only the six desktop baselines with a detail changed (`annotation-detail`, `annotation-history`, `template`; light and dark). The mobile baselines did not change.
- Results on the final code: `vue-tsc --build`, oxlint and ESLint are clean; Vitest 185/185; Playwright desktop and mobile 60 passed, 2 skipped (the tests for one viewport only). The visual suite (40/40) ran before the graph fix of section 9; none of its screens shows the graph.

### 8. Documentation

`docs/42for.net/platform/admin-ui.md`:

- *The catalog* describes the overlay, **Pin** with the resizable border (pointer, arrow keys, double-click), **Full screen**, and that the choice and the width are remembered for every kind of detail.
- *Phones, themes and accessibility* now says that on a tablet the detail slides in or opens in full screen, and on a desktop it opens over the list, pinned and resizable, or in full screen.

### 9. Not in the spec: the graph behind a full-screen detail

Hiding the graph panel gives its canvas no size. The checks in the browser found two problems:

- `GraphCanvas.resize()` then ran `reveal()` on a 0 × 0 canvas, which moved the selected node to the top-left corner. After the detail closed, the graph stayed there.
- Opening `/graph/{key}` directly in full screen made the first `fit()` use the half width the canvas had for one frame, before the list was hidden.

`app/components/graph/GraphCanvas.vue` now ignores a resize to no size, so the view is kept while the graph is hidden. A `fit()` asked for while the canvas has no size is remembered in `pendingFit` and runs at the next real resize. Both cases were checked with screenshots in Chrome. The graph has no unit tests for the canvas (happy-dom has no layout), so there is no automated test for this.
