# Graph Empty State Below the Legend with Create Actions

Linear: [424-130](https://linear.app/424fun/issue/424-130/admin-ui-graph-empty-state-overlaps-the-type-legend-and-has-no-create) — Admin UI Graph: empty state overlaps the type legend and has no create actions.

## Problem

The **Graph** page of the admin UI (`src/Platform/ui.admin`) has two problems in a view without annotations.

- **The empty state overlaps the type legend.** "No annotations in this view" is drawn over the legend chips (Responsibilities, Units, Subjects, …). Its icon sits on the bottom edge of the legend bar.
- **The empty state has no way forward.** The empty **Annotations** page offers **New annotation** and **Bulk create** to Contributors, and its top bar has **Bulk create** and **New**. The Graph page has neither. To start the view from the graph, you have to go to Annotations first.

The issue asks for:

1. An empty state that does not overlap the legend or the toolbar. It is laid out below the legend, or the legend is hidden while the view is empty.
2. The same two actions as the empty Annotations page, only when the user can edit, and the same buttons in the top bar as on Annotations.

The "Every type is hidden" empty state has the same layout. It is worse there: the legend is the only way back, and the empty state covers part of it.

## Current State

- **The graph viewport** (`app/components/graph/AnnotationGraph.vue`):
  - Lines 577–585: the viewport is a `relative` container with `tabindex="0"` and `role="application"`. Everything below is drawn inside it.
  - Lines 614–623: the legend is always rendered, in a `pointer-events-none absolute inset-x-3 top-3` wrapper. Only `GraphLegend` itself takes pointer events. Below 640 px it is collapsed into a **Types** button with a popover.
  - Lines 629–661: one `v-if` chain draws the state over the canvas:
    - the loading overlay, `absolute inset-0` with a translucent background (`(!hasData && catalog.status !== 'error') || layingOut || waitingForFocus`);
    - `ErrorState`, `class="absolute inset-0"`;
    - "No annotations in this view", `EmptyState` with `class="absolute inset-0"`, when `catalog.status === 'ready' && catalog.loadedCount === 0`;
    - "Every type is hidden", the same layout, when `hasData && visibleKeys.length === 0 && candidates.length > 0`.
  - Lines 502–524: `onKeydown` on the viewport. The arrow keys move the keyboard cursor, `Enter` opens the active node, `Escape` closes the detail. `Enter` is handled whenever a node is active, also when the key comes from a button inside the viewport. It then calls `preventDefault()`, so the button is not pressed.
  - The component has no slots and no permission checks.
- **`EmptyState`** (`app/components/common/EmptyState.vue`): the root is `mx-auto flex max-w-md flex-col items-center gap-3 px-4 py-12`. The content is not centered vertically, it starts 48 px below the top. The actions row (line 15) renders only when the default slot is given.
- **Why it overlaps:** with `absolute inset-0`, the empty state starts at the top of the viewport. The icon is drawn from 48 px to 96 px. The legend starts at 12 px and is about 42 px high, so it ends at about 54 px. Both are positioned and the empty state comes later in the DOM, so it is drawn on top.
- **Why the legend cannot be used under "Every type is hidden":** the `absolute inset-0` box of `EmptyState` is a column 448 px wide (`max-w-md`, `mx-auto`), centered, and as high as the viewport. It takes pointer events. The legend chips under that column cannot be clicked: at 1440 px these are the middle chips (Usages, Contexts, Executions, Units of execution). On a phone the viewport is narrower than 448 px, so the column covers the whole width, including the collapsed **Types** button: once every type is hidden, a phone cannot turn them back on. `ErrorState` renders `EmptyState` too, so it covers the legend the same way.
- **The Annotations page** (`app/pages/[org]/[project]/[view]/annotations.vue`):
  - Line 21: `canEdit = useCan('edit')` (Contributor or a higher role).
  - Lines 55–56: `creating` and `bulkCreating`, the `open` refs of `useAnnotationCreate()` and `useAnnotationBulkCreate()`.
  - Lines 239–267: the top bar. **Bulk create** (hidden below `sm`, icon only below `lg`, in a tooltip), **New** from `sm`, and an icon button named **New annotation** below `sm`. All behind `v-if="canEdit"`.
  - Lines 305–321: the empty state with **New annotation** and **Bulk create**, behind `<template v-if="canEdit">`.
  - Lines 340–341: the two dialogs, behind `v-if="canEdit"`. `AnnotationCreateDialog` opens the new annotation with `@created="open"`.
- **The dialog state** (`app/composables/useAnnotationCreate.ts`, `useAnnotationBulkCreate.ts`): one module-level `open` ref per dialog, with `show()`. Mounting a dialog twice on one page would open it twice. Nothing resets the ref when the page goes away: a dialog left open by the browser's **Back** opens again on the next visit of the page. The command palette (`components/shell/CommandPalette.vue`, lines 107–123) runs `await router.push(…/annotations)` and then `show()`, because only that page mounts the dialogs.
- **Creates reload the catalog:** `AnnotationCreateDialog` (lines 159–163) adds the new annotation with `catalog.upsert()`, and reloads the whole catalog (`catalog.load(scope, { force: true })`) when it also created missing ancestors. Bulk create always reloads it (`useAnnotationActions.ts`, `createMany`). **Reload the catalog** does the same.
- **What a reload does to the graph:** a reload of the same scope keeps the annotations but sets `status` to `loading` (`stores/catalog.ts`, line 247). `hasData` (`AnnotationGraph.vue`, line 92) is then false, so the graph is rebuilt with no nodes. The worker answers that `init` with a frame too (`simulation.worker.ts`, line 212), and `onFrame` (lines 344–367) remembers the empty frame and fits the empty canvas. When the catalog is ready again, `layoutSeed` (lines 237–249) seeds from that empty frame: the layout starts from scratch and the zoom is lost. Hiding every type has the same effect.
- **The Graph page** (`app/pages/[org]/[project]/[view]/graph.vue`):
  - Lines 27–29: `open(key)` opens the node's detail at `graph/<key>` and keeps the query.
  - Lines 42–57: the top bar has only **Reload the catalog**.
  - Line 60: `<AnnotationGraph>` with `@open` and `@close`.
- **Tests:**
  - `test/unit/graph/graph.test.ts` mounts `AnnotationGraph` against an MSW catalog (`mountGraph`). No test covers an empty catalog or the hidden-types empty state.
  - `test/e2e/onboarding.spec.ts` checks "No annotations in this view" on the Annotations page of the new `globex/portal/default` view.
  - `test/e2e/annotations.spec.ts` finds **New**, **New annotation** and **Bulk create** by their exact names, per device. `openBulkCreate` (`test/e2e/support.ts`) uses the command palette on a phone. "a Reader does not see Bulk create" checks `northwind/portal`, where the mock user is a Reader.
  - `test/e2e/visual.spec.ts` has no Graph screen. Every scope of the default mock dataset has annotations.
- **Docs:** `docs/42for.net/platform/admin-ui.md` describes **New** and **Bulk create** under Annotations, and the Graph section does not mention the empty view. `src/Platform/ui.admin/README.md` lists the end-to-end flows.

## Proposed Changes

### 1. Hide the legend in an empty view (`AnnotationGraph.vue`)

- A computed `empty` holds `catalog.status === 'ready' && catalog.loadedCount === 0`.
- The legend row renders only with `!empty`. In an empty view every count is 0 and no toggle changes anything.
- "No annotations in this view" uses the same `empty` flag, so the two conditions cannot drift apart.
- The legend keeps its behaviour while loading, on an error, with annotations and with a snapshot.

### 2. Lay out the states below the legend (`AnnotationGraph.vue`)

The legend and the error and empty states share one overlay column. The states follow the legend in the normal flow, so they start below it whatever its height. Nothing is measured.

```vue
<div class="pointer-events-none absolute inset-0 flex flex-col">
  <div v-if="!empty" class="flex flex-wrap items-start justify-between gap-2 p-3">
    <GraphLegend class="pointer-events-auto" … />
  </div>
  <template v-if="!loading">
    <ErrorState v-if="catalog.status === 'error'" class="pointer-events-auto" … />
    <EmptyState v-else-if="empty" class="pointer-events-auto" title="No annotations in this view" …>
      <template v-if="$slots['empty-actions']" #default>
        <slot name="empty-actions" />
      </template>
    </EmptyState>
    <EmptyState v-else-if="allHidden" class="pointer-events-auto" title="Every type is hidden" … />
  </template>
</div>
```

- **The legend row** has `p-3`, the same 12 px offset from the top and the sides as `inset-x-3 top-3`. Populated graphs look the same.
- **A wrapped legend** (several rows) and **the collapsed legend** below 640 px push the states down by their real height.
- **Pointer events:** the column and the legend row stay `pointer-events-none`, so the canvas still pans and zooms around them. Only the legend and the state take pointer events. The state no longer covers the legend, so every chip and **All types** can be clicked.
- **`loading`** is a computed of the condition of the loading overlay. The overlay stays `absolute inset-0` after the column, on top of everything, as now. The states render only when it is not shown, as in the current `v-if` chain.
- **`allHidden`** is a computed of the current condition (`hasData && visibleKeys.length === 0 && candidates.length > 0`).
- **`ErrorState`** moves into the column too. It is the same `EmptyState` and has the same overlap.
- The states stay top aligned. The box of a state starts right below the legend row, 12 px below the legend, and its icon 48 px lower. When the legend is hidden, the icon is 48 px below the top of the viewport, where the empty states of the other pages sit below their toolbar.
- The summary at the bottom left and the live region do not change.

### 3. A slot for the actions of the empty view (`AnnotationGraph.vue`)

- A named slot `empty-actions`, declared with `defineSlots<{ 'empty-actions'?: () => unknown }>()`.
- It goes into the default slot of "No annotations in this view" only, and only when the page gives it. Without it, the actions row of `EmptyState` is not rendered.
- "Every type is hidden" gets no actions. The legend is its way back.
- The graph component stays free of permissions and dialogs. The page that mounts the dialogs also gives the buttons that open them.

### 4. Enter on a control inside the graph (`AnnotationGraph.vue`)

The new buttons are inside the viewport, like the legend chips. `onKeydown` opens the active node on `Enter` even when the key comes from one of them, and its `preventDefault()` cancels the button. With a node selected, `Enter` on a legend chip opens the node instead of toggling the type.

- `Enter` opens the active node only when the viewport itself has the focus (`event.target === event.currentTarget`). On a button inside it, `Enter` presses the button.
- The arrow keys and `Escape` do not change.

### 5. Keep the layout through a reload (`AnnotationGraph.vue`)

Bulk create always reloads the catalog, and so does a create with missing ancestors (Current State). Offered on Graph, they would throw away the layout, the zoom and the dragged nodes every time.

- `onFrame` ignores the frames of a graph without nodes. It does not remember them, does not fit the empty canvas and keeps `positions` unset.
- After the reload, `layoutSeed` then seeds from the remembered frame of the graph before it. With the same nodes, the layout resumes with its dragged nodes and its zoom. With new nodes, the known ones keep their positions and the zoom stays, as when a create adds a node without a reload.
- The loading overlay still covers the graph while the catalog loads.
- The same applies when every type is hidden and then shown again, and to **Reload the catalog**.

### 6. Shared create actions (`components/annotations/AnnotationCreateActions.vue`, new)

One component owns the create buttons, so both pages show the same ones.

- **Prop:** `variant: 'header' | 'empty'`.
- **`header`:** the markup of `annotations.vue` lines 239–267, with its comment. **Bulk create** in a tooltip, hidden below `sm` and icon only below `lg`, then **New** from `sm`, then the icon button **New annotation** below `sm`.
- **`empty`:** **New annotation** (primary) and **Bulk create** (neutral outline), on every viewport.
- **Clicks:** `show()` of `useAnnotationCreate()` and of `useAnnotationBulkCreate()`.
- **Role:** the whole component renders nothing without `useCan('edit')`.
- **No dialogs.** The page mounts each dialog once and decides what happens after a create. Mounting them here would mount them twice on a page that shows both variants, and both would open.
- **Names:** the accessible names stay **Bulk create**, **New** and **New annotation**. The end-to-end locators use them.

### 7. The Annotations page uses it (`annotations.vue`)

- The top bar buttons become `<AnnotationCreateActions variant="header" />`, after **Reload the catalog**.
- The buttons of the empty state become `<AnnotationCreateActions variant="empty" />`.
- The dialogs, their `canEdit` gate and `@created="open"` stay. `creating` and `bulkCreating` stay for the dialog models. `canEdit` stays for the dialogs and the bulk bar.
- No visible change. The annotations screenshots must not change.

### 8. The Graph page offers them (`graph.vue`)

- `<AnnotationCreateActions variant="header" />` after **Reload the catalog** in the `#right` slot of `AppNavbar`.
- `<template v-if="canEdit" #empty-actions><AnnotationCreateActions variant="empty" /></template>` on `AnnotationGraph`. Without the right to edit the slot is not given, so the empty state has no actions row.
- The two dialogs after `AnnotationGraph`, each with `v-if="canEdit"` and bound to `useAnnotationCreate().open` and `useAnnotationBulkCreate().open`, as on Annotations.
- **After a create:** `AnnotationCreateDialog` calls `open(key)` with `@created`. The detail of the new annotation opens at `graph/<key>` with the query of the graph, so the user stays on Graph, as Annotations keeps the user on Annotations.
- **The new node:** the catalog already holds it (`catalog.upsert`). The graph draws and selects it when it shows it, as for any node opened by its URL. It does not when it is outside the neighbourhood of a focus mode (Focus then moves the focus to it), or when its type is hidden in the legend. A filter outside focus mode dims it when it does not match.
- **Bulk create** shows its result in the dialog, as on Annotations. It has no `@created`.

### 9. A dialog left open does not come back (`annotations.vue`, `graph.vue`)

With two pages that mount the dialogs, a dialog left open by the browser's **Back** would open again on the other page at once, and its `@created` would act there.

- Both pages close the two dialogs in `onBeforeRouteLeave`: `creating` and `bulkCreating` are set to `false`.
- The guard runs only when the page itself is left. Opening the detail of a row or a node (a child route) does not close anything.
- It runs before the navigation is confirmed. The command palette calls `show()` after its `router.push` has finished, so its dialog still opens on Annotations.
- Not in `onBeforeUnmount`: that runs after the palette's `show()` and would close the dialog it just opened.

### 10. Tests

- **Unit, `test/unit/graph/graph.test.ts` ("graph screen"):** `mountGraph` gets optional `component` and `props` parameters. A small wrapper component renders `AnnotationGraph` with an `#empty-actions` slot, as the page does.
  - An empty catalog with the slot: "No annotations in this view" and the slot content render, and the legend (`role="group"`, "Annotation types") does not.
  - The same without the slot: no actions row.
  - Every type hidden with the legend: the legend and "Every type is hidden" render, the empty state comes after the legend row in the overlay column, and it has no actions. A chip brings the graph back.
  - With a node selected, a cancelable `Enter` keydown on a legend chip is not prevented and emits no `open`. On the viewport it is prevented and emits `open`. Happy-dom does not turn `Enter` into a click, so the toggle itself is checked end to end.
  - With the fake worker: a forced reload of the same scope does not remember the frame of the empty graph, and the next `init` seeds every node and resumes the remembered frame.
- **Unit, `test/unit/annotations/create-actions.test.ts` (new):**
  - `header` for a Contributor: **Bulk create**, **New** and **New annotation**. `empty`: **New annotation** and **Bulk create**.
  - A click opens the matching dialog state (`useAnnotationCreate().open`, `useAnnotationBulkCreate().open`).
  - A Reader gets no buttons in either variant.
- **End to end:** the header and the empty state have buttons with the same names (**Bulk create** on desktop, **New annotation** on a phone). The checks of the empty state are scoped to the graph viewport, `getByRole('application')`. `openBulkCreate` is not used on an empty view.
- **End to end, `test/e2e/onboarding.spec.ts`:** after the Annotations check, the Graph page of the same new view shows "No annotations in this view", no legend (neither the group nor the **Types** button), and **New annotation** and **Bulk create** in the empty state.
- **End to end, `test/e2e/graph.spec.ts` (new), desktop and mobile:**
  - An empty view (the view `empty` of `acme/billing`, registered through the mock API): no legend, **Bulk create** opens its dialog, and **New annotation** creates `sbt.cyberdyne` and opens `/acme/billing/empty/graph/sbt.cyberdyne`.
  - Every type hidden: the box of "Every type is hidden" starts at or below the bottom of the legend (of the **Types** button on a phone). A chip under the old 448 px column (**Contexts**) is clicked on desktop, and the **Types** button with the popover closed on a phone. Both clicks are intercepted today. **All types** then brings the graph back.
  - With a node selected (`graph/sbt.contoso`), `Enter` on a focused legend chip hides its type and the URL stays (desktop; the collapsed legend of a phone is in a popover outside the viewport).
  - **New** in the Graph top bar (**New annotation** on a phone) creates `sbt.cyberdyne` and opens `/graph/sbt.cyberdyne` with the query of the graph.
  - A dialog left open on Graph does not come back on Annotations after **Back**, and **New** there still opens it.
  - A Reader (`northwind/portal`) has no **New**, **New annotation** or **Bulk create** on Graph, and an empty view of a Reader (`northwind/portal/empty`) shows its empty state without buttons.
- **Visual, `test/e2e/visual.spec.ts`:** a `graph-empty` screen. Its `prepare` creates the view `empty` in `acme/billing` through the mock API, as `deleted` does, and opens `/acme/billing/empty/graph`. Four new baselines, `graph-empty-{desktop,mobile}-{light,dark}.png`, made in the Playwright Linux image. The other baselines are compared and must not change.
- `npm test`, `npm run test:e2e`, `npm run typecheck` and `npm run lint` must pass.

### 11. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - Annotations: **New** and **Bulk create** are on Graph too.
  - Graph: an empty view hides the legend and offers **New annotation** and **Bulk create** to Contributors. **New** and **Bulk create** are in the top bar too. A new annotation created from Graph opens its detail next to the graph, and the graph draws it when it shows it (not outside the neighbourhood of a focus, nor when its type is hidden). "Every type is hidden" sits below the legend, which turns the types back on. A reload keeps the layout.
- `src/Platform/ui.admin/README.md`: the end-to-end list names the empty Graph of a new view, the hidden types, a new annotation from Graph and the Reader without create buttons on Graph.
- The review in `docs/Platform/ui.admin/specs/reviews/`.

### Out of scope

- **The command palette** still opens the Annotations page before it opens a create dialog. The Graph page mounts the dialogs now, but the palette does not know which page is open.
- **The graph toolbar** (filter, layouts, Fit, Reheat, PNG, Open as list, Focus) stays as it is in an empty view.
- **The arrow keys and `Escape`** on a control inside the graph keep their current behaviour.
- **Focus after the first create.** When the first annotation of a view is created from the empty state, the button that opened the dialog is gone, and the focus returns to the page. The empty Annotations page does the same today.
- **Moving the focus or showing a hidden type** for a new annotation the graph does not draw. The detail opens either way.

### Compared with the CodeRabbit coding plan

The coding plan in the issue was read as a second opinion.

- **The same:** the legend hidden in an empty view and kept for "Every type is hidden", a shared component with the buttons and the role check but without the dialogs, a named slot for the empty state filled by the page, and the Graph detail route after a create.
- **Different:** the plan measures the legend with `useElementSize` and offsets "Every type is hidden" by its height. Here the states follow the legend in one flex column, so nothing is measured, and a wrapped or collapsed legend needs no special case.
- **Added:** `ErrorState` below the legend too, `Enter` on a control inside the graph, the layout kept through a reload, a dialog left open on one page not coming back on the other, and an end-to-end check of the chips the old empty state covered.
