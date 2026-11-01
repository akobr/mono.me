# Graph Empty State Below the Legend with Create Actions

Linear: [424-130](https://linear.app/424fun/issue/424-130/admin-ui-graph-empty-state-overlaps-the-type-legend-and-has-no-create) — Admin UI Graph: empty state overlaps the type legend and has no create actions.

## Overview

This review covers the whole spec [2026-11-01 Graph Empty State Below the Legend with Create Actions](../2026-11-01%20Graph%20Empty%20State%20Below%20the%20Legend%20with%20Create%20Actions.md), delivered in one phase of several commits.

The empty states of **Graph** no longer cover the type legend. An empty view hides the legend, and "Every type is hidden" and the error state sit below it, so every chip, **All types** and the collapsed **Types** button can be clicked. The empty view offers **New annotation** and **Bulk create**, and the Graph top bar has **Bulk create** and **New**, as on Annotations, for Contributors and higher roles. Both pages share one component for these buttons. A new annotation created from Graph opens its detail in the graph. Two review rounds added fixes around the new buttons: `Enter` on a control inside the graph, the layout through a reload, the canvas of an empty graph, and a dialog left open by **Back**.

## What Was Done

### 1. Hide the legend in an empty view

`AnnotationGraph.vue` has a computed `empty` (`catalog.status === 'ready' && catalog.loadedCount === 0`). The legend row renders only with `!empty`, and "No annotations in this view" uses the same flag. Two more computed values name the old inline conditions: `loading` (the loading overlay) and `allHidden` ("Every type is hidden").

### 2. Lay out the states below the legend

The legend row and the states share one `pointer-events-none absolute inset-0 flex flex-col` column, after the summary and the live region:

- the legend row, `flex flex-wrap items-start justify-between gap-2 p-3`, with `GraphLegend` as `pointer-events-auto`;
- `<template v-if="!loading">` with `ErrorState`, "No annotations in this view" and "Every type is hidden", each `pointer-events-auto` and in the normal flow, so it starts below the legend row.

The loading overlay stays `absolute inset-0` after the column, so it still covers everything while the catalog loads or the layout runs. Nothing is measured.

The legend of a populated graph stays where it was. The four new screenshots and the unchanged ones confirm it (Results). An end-to-end run on the old component confirmed the root cause: "Every type is hidden" took the clicks of the **Contexts** chip on a desktop and of the **Types** button on a phone ("… intercepts pointer events").

### 3. A slot for the actions of the empty view

`defineSlots<{ 'empty-actions'?: () => unknown }>()`. The slot reaches the default slot of "No annotations in this view" through `<template v-if="$slots['empty-actions']" #default>`, so without it the actions row of `EmptyState` is not rendered. "Every type is hidden" has no actions.

### 4. Enter on a control inside the graph

`onKeydown` opens the active node on `Enter` only when `event.target === event.currentTarget`. On a legend chip or a button of the empty state, `Enter` presses the control. The arrow keys and `Escape` are unchanged.

### 5. Keep the layout through a reload

`onFrame` still takes the positions of a graph without nodes, but returns before it remembers the frame and before it fits the canvas. After a reload of the same scope, `layoutSeed` seeds from the frame before it: with the same nodes the layout resumes with its alpha, its dragged nodes and its zoom.

**Deviation:** the spec said `onFrame` ignores such frames and keeps `positions` unset. The review of the implementation found that `GraphCanvas.draw()` returns early without positions, so the old picture stayed on the canvas behind "Every type is hidden", and behind the empty state of a view reached with the view switcher. The positions of the empty frame are now set, which clears the canvas (commit `fix(platform): clear the graph canvas when the graph has no nodes`). `layoutSeed` still takes the remembered frame, because the empty positions differ from it.

### 6. Shared create actions

`components/annotations/AnnotationCreateActions.vue` (new, auto-imported, registered in `components.d.ts`) has a `variant` prop:

- `header`: **Bulk create** in its tooltip (`hidden sm:inline-flex`, label from `lg`), **New** from `sm`, and the icon button **New annotation** below `sm`, with the comment that explains the breakpoints;
- `empty`: **New annotation** and **Bulk create**.

It calls `show()` of `useAnnotationCreate()` and `useAnnotationBulkCreate()`, renders nothing without `useCan('edit')`, and mounts no dialog.

### 7. The Annotations page uses it

`annotations.vue` replaces its inline buttons with the two variants. The dialogs, their `canEdit` gate and `@created="open"` stay. The visual suite compared the four `annotations-*` screenshots with no change.

### 8. The Graph page offers them

`graph.vue` puts the `header` variant after **Reload the catalog**, gives `#empty-actions` the `empty` variant behind `v-if="canEdit"`, and mounts `AnnotationCreateDialog` (`@created="open"`) and `AnnotationBulkCreateDialog` behind `canEdit`. The detail opens at `graph/<key>` with the query of the graph. The graph draws the new node when it shows it; the docs say when it does not (focus, hidden type).

### 9. A dialog left open does not come back

Both pages set `creating` and `bulkCreating` to `false` in `onBeforeRouteLeave`. The child routes and the view switcher do not run it, and the command palette's `show()` runs after it.

### 10. Tests

- **Unit, `test/unit/graph/graph.test.ts`:** `mountGraph` takes an optional component and props, and a `WithActions` wrapper gives the slot. Five new cases: the empty view with and without the slot; "Every type is hidden" after the legend row in the same column, without actions, and a chip that brings the graph back; `Enter` on a chip (not prevented, no `open`) and on the viewport (prevented, `open`); and a reload with the fake worker, where the canvas positions become empty, the remembered frame stays, and the next `init` seeds every node and resumes. The first three failed on the old component, and the reload case failed without the guard.
- **Unit, `test/unit/annotations/create-actions.test.ts` (new):** the names of both variants for a Contributor, the dialog state after each click, and nothing for a Reader.
- **End to end, `test/e2e/graph.spec.ts` (new):** an empty view (the view `empty` of `acme/billing`, registered through the mock API) without legend, with **Bulk create** and the first annotation from **New annotation** at `/acme/billing/empty/graph/sbt.cyberdyne`; "Every type is hidden" below the legend, with the chip click on a desktop and the **Types** click on a phone; `Enter` on a chip with `sbt.contoso` selected (desktop only, the phone's legend is in a popover outside the graph); **New** in the top bar; a dialog left open on Graph and **Back** to Annotations; and a Reader, also on `northwind/portal/empty`. The empty-state checks are scoped to `getByRole('application')`, because the header has buttons with the same names. The hidden-types, `Enter` and **Back** cases failed on the old code.
- **End to end, `test/e2e/onboarding.spec.ts`:** the Graph of the new `globex/portal/default` view has no legend group and no **Types** button, and offers both actions.
- **Visual:** the `graph-empty` screen registers the view `empty` in its `prepare` and opens `/acme/billing/empty/graph`. Four new baselines, `graph-empty-{desktop,mobile}-{light,dark}.png`, from the Playwright Linux image.

`test/e2e/graph.spec.ts` uses `getByText('367 annotations', { exact: true })`: without `exact`, the label "Laying out 367 annotations…" of the loading overlay matched too.

### 11. Documentation

- `docs/42for.net/platform/admin-ui.md`: **New** and **Bulk create** are on Graph too, and Bulk create opens from the empty view of either page on a phone. The Graph section has four new bullets: **Legend**, **Creating**, **Empty view** and **Reloads**. After the review, the reload bullet says what is kept: **Reload the catalog** keeps the layout, the zoom and the dragged nodes, and after **Bulk create** the drawn annotations keep their places and the dragged nodes are released.
- `src/Platform/ui.admin/README.md`: the end-to-end list names the empty Graph after onboarding and the new Graph flows.

### Reviews

- **Of the spec**, before the code: three independent reviews of layout, behaviour and tests. They corrected the root cause (the chips covered on a desktop are the middle ones, and on a phone the **Types** button), the gap below the legend (60 px to the icon, not 48 px), and the planned tests. They also found the reload that resets the layout and the dialog left open by **Back**. Sections 5 and 9 of the spec come from them.
- **Of the implementation:** three reviews, each finding checked by a second agent that tried to refute it. Two were confirmed and fixed: the canvas of an empty graph (section 5) and the reload claim of the docs. Two doc wording points were not confirmed but were taken anyway: the role of the empty-view actions, and the sentence about Graph moved after the time zone of **New**.

### Results

- Unit: `npm test` 1460 of 1460 after the second commit. The first full run had 2 failures in `test/unit/router/page-memory.test.ts` (a 5 s timeout and the case after it). That file passed alone, 19 of 19, and the second full run passed. It does not mount the changed pages. The graph and create-actions files pass after the canvas fix, 26 of 26.
- `npm run typecheck` and `npm run lint` pass.
- End to end: `npm run test:e2e`, desktop and mobile, 381 passed and 9 skipped (the skips are device-specific cases, among them the `Enter` case on a phone).
- Visual, in the Playwright Linux image: the existing 64 screenshots matched; the first run wrote the four `graph-empty` baselines. The second run, with them committed, passed 68 of 68, and no baseline changed.
- One desktop run of the empty-view create waited 10 s for the navigation to the new detail while the parallel workers were busy, and timed out. It passed 3 of 3 times when repeated with the other Graph tests.

### Left for later

- **The command palette** sets the open state of a dialog even when its navigation to Annotations is refused (for example **Keep editing** on a dirty configuration). The dialog then opens on the next visit of Annotations, or now of Graph. This was so before. `show()` should run only when the navigation succeeded.
- **A new annotation the graph does not draw** (outside the focus, or of a hidden type) could move the focus or show the type.
- **The focus after the first create** goes to the page, as on the empty Annotations page, because the button that opened the dialog is gone.
