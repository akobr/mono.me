# Detail Panel as Overlay, Pinned Panel or Full Screen

Linear: [424-22](https://linear.app/424fun/issue/424-22/make-right-panel-resizable-and-open-able-full-screen)

## Problem

The Administration UI (`src/Platform/ui.admin`) shows the detail of an annotation, a configuration, a template, a schema or a machine in a panel on the right of the list. The issue asks for the panel to work like the item peek of Plane:

- The detail opens as an **overlay** from the right side, over the list.
- The overlay can be **pinned**, so it becomes a real right panel beside the list, and that panel can be **resized**.
- A button opens the detail in **full screen**, as a page of its own.
- The application **remembers** the latest choice and opens the next detail the same way.

Today the panel cannot be resized in practice: its resize handle sits on the outer edge of the screen, not between the list and the detail (see Current State). There is no overlay, no pin and no full screen.

## Current State

- **`app/components/shell/DetailPanel.vue`** is the shared panel. It switches between two components by media query:
  - At `lg` and above (line 22): a `UDashboardPanel` with `resizable`, `min-size` 25, `default-size` 40 and `max-size` 70 (percent). Its size is stored by `UDashboardGroup` (`layouts/DefaultLayout.vue` line 8, `storage="local"`, `storage-key="2s-admin"`) under one key per panel id (`2s-admin-panel-annotations-detail`, `…-configuration-detail`, …).
  - Below `lg`: a modal `USlideover` from the right, 90 % wide at `md` and `fullscreen` below `md`. Closing it waits for `after:leave`, then emits `close`.
  - The header is a slot that receives `close`; the body is the default slot.
- **Why resizing does not work.** `UDashboardPanel` renders its resize handle *after* the panel element, and `useResizable` (Nuxt UI 4.11) computes the drag for a panel on the left (`side: 'left'`; the panel has no `side` prop). For the detail panel, the last child of the group, the handle is therefore the 1-pixel right edge of the browser window, and dragging it left makes the panel narrower. The border between the list and the detail cannot be dragged.
- **Users of `DetailPanel`** (each renders its own header with a close button):

  | Screen | File | Header close button |
  | --- | --- | --- |
  | Annotation (list and graph) | `components/annotations/AnnotationDetailPanel.vue` → header `components/annotations/AnnotationDetail.vue` | line 168, `Close the detail` |
  | Configuration | `pages/[org]/[project]/[view]/configurations/[key].vue` | line 63, `Close the detail` |
  | Schema | `components/schemas/SchemaDetailPanel.vue` | line 49, `Close` |
  | Template | `pages/[org]/[project]/[view]/templates/[type].vue` | line 40, `Close` |
  | Machine | `pages/[org]/[project]/machines/[id].vue` | line 32, `Close` |

  Each detail is a nested route rendered by `<RouterView />` after the list's `UDashboardPanel`, in the list pages `annotations.vue`, `configurations.vue`, `graph.vue`, `schemas.vue`, `templates.vue` (under `pages/[org]/[project]/[view]/`) and `machines.vue` (under `pages/[org]/[project]/`). The list keeps its scroll position, selection and filter while the detail changes.
- **Preferences.** `app/stores/preferences.ts` keeps UI state in localStorage (`2s-admin:preferences`, `useStorage` with `mergeDefaults`). It has `sidebarPinned`, the same pin idea for the navigation sidebar (`SidebarBrand.vue`), but nothing for the detail.
- **Keyboard.** `annotations.vue` (line 136) and the graph canvas (`AnnotationGraph.vue` line 441) close the detail with `Esc`. The other details rely on the `USlideover`, which closes on `Esc` below `lg` only.
- **Closing restores the row.** `annotations.vue` scrolls the grid back to the row of the closed detail (`watch(selectedKey, …)`).
- **Tests.** E2E flows open the detail in `test/e2e/annotations.spec.ts`; `test/e2e/accessibility.spec.ts` runs axe on the annotation detail; `test/e2e/visual.spec.ts` has baselines of `annotation-detail`, `annotation-history` and `template` on desktop and mobile, light and dark.
- **Docs.** `docs/42for.net/platform/admin-ui.md` says "Selecting a row opens the detail beside the list" and, under *Phones, themes and accessibility*, that the detail is full-screen on a phone, slides in on a tablet and is resizable on a desktop.

## Proposed Changes

### 1. Preferences (`app/stores/preferences.ts`)

Add three fields to `Preferences` and `defaultPreferences`:

```ts
detailPinned: boolean      // default false: the detail opens as an overlay
detailFullscreen: boolean  // default false
detailWidth: number        // default 40 (percent of the window beside the sidebar's group)
```

They are plain fields of the persisted state, so they are remembered across reloads and sessions (`mergeDefaults` fills them in for existing users). The comment "Panel sizes are persisted by UDashboardGroup itself" changes: the detail width is now kept here, one width for every detail. The old per-panel `2s-admin-panel-*-detail` keys are no longer written and are ignored.

### 2. Layout rules (`app/utils/detail-panel.ts`, new)

Pure functions, unit tested:

```ts
export type DetailMode = 'overlay' | 'pinned' | 'full'

export const detailWidth = { min: 25, max: 70, default: 40, step: 2 } as const

/** The mode the detail opens in, from the preferences and the screen size. */
export function detailMode(options: { pinned: boolean; fullscreen: boolean; medium: boolean; large: boolean }): DetailMode

/** A width in percent within the limits; a stored value that is not a number gives the default. */
export function clampDetailWidth(value: number): number
```

Rules of `detailMode`:

| Screen | `fullscreen` | `pinned` | Mode |
| --- | --- | --- | --- |
| below `md` (phone) | any | any | `full` |
| `md` to `lg` (tablet) | true | any | `full` |
| `md` to `lg` | false | any | `overlay` (no room for a pinned panel) |
| `lg` and above | true | any | `full` |
| `lg` and above | false | true | `pinned` |
| `lg` and above | false | false | `overlay` |

### 3. Shared state (`app/composables/useDetailPanel.ts`, new, auto-imported)

```ts
export function useDetailPanel(): {
  mode: ComputedRef<DetailMode>
  /** A detail is open in full screen: the list it belongs to is hidden. */
  covers: ComputedRef<boolean>
  /** Pin and unpin are offered at lg and above, outside full screen. */
  canPin: ComputedRef<boolean>
  /** Full screen is offered at md and above (below md the detail is always full screen). */
  canFullscreen: Ref<boolean>
  togglePinned(): void
  toggleFullscreen(): void
  /** Called by DetailPanel: counts the open panel while it is mounted. */
  track(): void
}
```

A module-level counter of mounted `DetailPanel`s makes `covers` true only while a detail is actually open and the mode is `full`.

### 4. `DetailPanel.vue`: one element, three modes

Replace the `UDashboardPanel` / `USlideover` switch with one `<section>` whose classes follow `mode`, so switching the mode (or resizing the window) never remounts the detail and never loses an unsaved edit:

| Mode | Placement | Width |
| --- | --- | --- |
| `overlay` | `absolute` in the `UDashboardGroup`, `right-0 bottom-0`, below the list's navbar (`top-(--ui-header-height)`), `z-30`, `shadow-xl`, a left border, slides in from the right (`slide-in-from-right`, reduced motion fades) | `detailWidth` % at `lg`, 90 % below `lg` |
| `pinned` | a flex item after the list, `shrink-0`, left border | `detailWidth` % |
| `full` | `flex-1`; the list is hidden (section 6) | the whole area beside the sidebar |

- The overlay is not modal: the visible part of the list stays usable, so clicking another row switches the detail. It starts below the navbar, so the navbar (context switcher, New, search, user menu) stays usable too.
- In `full` mode at `md` and above, the panel starts with `<AppNavbar :title="title" />`, so a full-screen detail is a page like any other (context switcher, search, theme, user menu, and the ☰ button when the sidebar is unpinned). Below `md` it keeps today's look: the detail header with its back button only.
- The panel is a labelled region: `<section :aria-label="title" :data-detail-mode="mode">`.
- **Resize handle** (overlay and pinned, `lg` and above): an element on the panel's **left** edge, `role="separator"`, `aria-orientation="vertical"`, `aria-label="Resize the detail"`, `aria-valuenow/min/max` (percent), `tabindex="0"`, `cursor-ew-resize`, highlighted on hover, focus and drag.
  - Pointer: `pointerdown` captures the pointer; while it moves, the width is `(groupRight − x) / groupWidth × 100`, clamped to 25–70 %. Text selection is prevented while dragging.
  - Keyboard: `←` widens and `→` narrows by 2 %.
  - Double-click resets to 40 %.
  - Every change is stored in `detailWidth`, so overlay and pinned share it and the next detail opens with it.
- **Esc** closes the detail when the focus is in the panel or on the page body, and not in a field, button, link, menu or dialog. It ignores events a page already handled (`defaultPrevented`), so it does not double the `Esc` of the annotation grid; it replaces the `Esc` the `USlideover` gave the other details on tablets.
- The header slot still receives `close`; the body keeps the `flex flex-col overflow-y-auto` container of today's `UDashboardPanel` body.
- `useDetailPanel().track()` is called in `setup`.

### 5. `DetailPanelActions.vue` (new, `components/shell/`)

Two ghost buttons (`size="sm"`, `color="neutral"`), each with a tooltip equal to its label:

- **Pin the detail** (`i-lucide-pin`) / **Unpin the detail** (`i-lucide-pin-off`), when `canPin`.
- **Open in full screen** (`i-lucide-maximize-2`) / **Exit full screen** (`i-lucide-minimize-2`), when `canFullscreen`.

Pinning or entering full screen only changes the preferences, so the choice applies at once and to every later detail. Exiting full screen returns to the overlay or the pinned panel, whichever was chosen before.

Every detail header places `<DetailPanelActions />` right before its close button: `AnnotationDetail.vue`, `configurations/[key].vue`, `SchemaDetailPanel.vue`, `templates/[type].vue` and `machines/[id].vue`.

### 6. The list makes room for a full-screen detail

The six list pages bind `:class="{ hidden: covers }"` on their `UDashboardPanel` (`covers` from `useDetailPanel()`). The list stays mounted, so its filter, selection and expansion survive. In `annotations.vue`, the watcher that scrolls the grid back to the closed row runs with `flush: 'post'`, after the list is visible again.

### 7. Tests

- **Unit** (`test/unit/utils/shell.test.ts`, new "detail panel" block): `detailMode` for every row of the table in section 2; `clampDetailWidth` for values below, inside and above the limits and for `NaN`.
- **Unit** (`test/unit/components/detail-panel.test.ts`, new): mount `DetailPanel` with a header that uses `DetailPanelActions` (happy-dom, 1024 px wide, so `lg`): it opens as an overlay; **Pin the detail** pins it and stores `detailPinned`; **Open in full screen** sets `full`; the arrow keys on the separator change `detailWidth` within the limits; `Esc` on the body emits `close`.
- **E2E** (`test/e2e/annotations.spec.ts`, desktop only): open an annotation → the detail is an overlay; pin it → it is pinned, and dragging the separator widens it; open it in full screen → the grid is hidden; reload → still full screen; exit full screen → pinned again; close → the grid is back.
- **Visual baselines**: the detail now opens as an overlay by default, so regenerate the baselines that show a detail (`annotation-detail`, `annotation-history`, `template`) with `npm run test:visual:docker -- --update`, and check that the other screens do not change.

### 8. Documentation

`docs/42for.net/platform/admin-ui.md`:

- *The catalog*: the detail opens as an overlay from the right; **Pin** docks it beside the list, where the border can be dragged to resize it; **Full screen** opens it as a page of its own; the UI remembers the choice and the width.
- *Phones, themes and accessibility*: phone — full screen; tablet — overlay or full screen; desktop — overlay, pinned and resizable, or full screen.
