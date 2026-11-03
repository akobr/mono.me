# Docked Sidebar Starts as the Icon Rail Between lg and xl

Linear: [424-168](https://linear.app/424fun/issue/424-168/docked-sidebar-does-not-start-as-the-icon-rail-between-1024-and-1279) — Docked sidebar does not start as the icon rail between 1024 and 1279 px.

## Problem

Between 1024 and 1279 px the docked sidebar should start as the icon rail: links without names and **Expand sidebar** (spec section 15 of the [administration UI](2026-10-09%20Administration%20UI%20for%202S%20Platform.md), test case NAV-MNU-07). Test case 35, `NavigationMenuTests.Sidebar_IconRail_ExpandsCollapsesAndToggles`, failed in run `20261102-230831-hviz`: at 1100 × 900 the sidebar was expanded, with link names and **Unpin sidebar**.

The browser only has to have stored an expanded sidebar before. Any start from 1280 px up stores it, and the storage is shared by every tab and later visit. These two paths fail in Chromium on the mock API:

- A tab at 1440 px opens the app, and a new tab at 1100 px opens it too: the new tab is expanded.
- At 1440 px, unpin the sidebar, shrink the window to 1100 px and pin it again: it is expanded.

A second symptom follows from the first. In that state the first press of `[` does nothing visible.

A browser that has never opened the app gets the rail at 1100 px, which is why the fault depends on the earlier visits.

## Current State

- `src/Platform/ui.admin/src/app/components/shell/AppSidebar.vue:48-50`: an immediate watcher sets the shared `collapsed` ref of `useSidebar` to `!wide` during the setup of `AppSidebar`, before `UDashboardSidebar` is created. The value is right at this point.
- `src/Platform/ui.admin/src/app/layouts/DefaultLayout.vue:11`: `UDashboardGroup storage="local" storage-key="2s-admin"`. The only group and the only user of `AppSidebar`. The stored key is `2s-admin-sidebar-sidebar`, with the text `{"size":16,"collapsed":false}`.
- `node_modules/@nuxt/ui/dist/runtime/composables/useResizable.js` (`@nuxt/ui` 4.11.3):
  - lines 21-25: the model only feeds the defaults of `useStorage`, which the stored object then replaces;
  - line 34: `isCollapsed` reads `storageData.value?.collapsed ?? defaultStorageValue.collapsed`, so a stored `false` beats a model of `true`. `isCollapsed` drives what the sidebar shows;
  - lines 161-163: only a stored `true` is copied to the model;
  - lines 164-173: the watcher of the model is not immediate and is created after the model holds `true`, so it never fires. Model and stored value stay apart. When `[` then flips the model to `false`, line 169 finds no difference, which is the second symptom.
- Shrinking an open page works, because there the model changes after that watcher exists. Pinning again is a new `UDashboardSidebar` (the `v-if` at `AppSidebar.vue:55`), so it takes the same path as a start.
- `useStorage` (`@vueuse/core` 14.4.0, `index.js:1907`): the watcher that writes is `flush: 'pre'`, and `update()` pauses and resumes it synchronously. The job therefore runs after the resume, so a first read that differs from the stored text is written back, with a storage event for the other tabs (`index.js:1923`, `2002-2010`, `1962-1974`). Equal text writes nothing.
- `UDashboardPanel` without `resizable` never calls `useStorage` (`useResizable.js:25`), and no panel sets it (the detail panel keeps its width in the `preferences` store). So the sidebar is the only user of the group's storage options.
- A collapse from 1280 px up survives a reload today (line 161). Spec line 399 says the collapsed state is persisted, and it stays so.

Two hypotheses of the issue do not hold:

- `useMediaQuery` is correct during setup. `useSupported` returns `Boolean(callback())` whatever `isMounted` holds (`index.js:327-332`), and the first run of its `watchEffect` is synchronous.
- The module state does not carry over. The immediate watcher overwrites it on every setup of `AppSidebar`.

## Proposed Changes

### 1. A stored "expanded" is no choice (`DefaultLayout.vue`)

The group gets `:storage-options` with a serializer:

```ts
const sidebarStorage = {
  serializer: {
    read: (raw: string): StoredSidebar => {
      const stored: StoredSidebar = JSON.parse(raw)
      return stored.collapsed ? stored : { size: stored.size }
    },
    write: (state: StoredSidebar) => JSON.stringify({ ...state, collapsed: state.collapsed ?? false }),
  },
}
```

- **The rule:** a stored rail is a choice and stays. A stored "expanded" is read as no choice, so `isCollapsed` falls back to the start `AppSidebar` set for the width of the window. No second copy of the 1280 px breakpoint is needed.
- **A start stores nothing:** `{ size }` serializes back to the stored text, so `useStorage` calls no `setItem` and sends no storage event. Other open windows and a later start at another width see no change.
- The stored size stays, and the stored format does not change.

What a user sees after the change:

| Flow | Today | After |
| --- | --- | --- |
| Start at 1024-1279 px, an expanded sidebar stored | Expanded, the first `[` does nothing | Icon rail, the first `[` expands it, nothing stored |
| Unpin at 1280 px or more, shrink, pin | Expanded | Icon rail |
| Start from 1280 px up, an expanded sidebar stored | Expanded | Expanded |
| `[` to the rail from 1280 px up, reload | Rail | Rail |
| Rail stored, any start | Rail | Rail |
| Widen an open page to 1280 px or more, or shrink it below | Expands, or collapses | Same |
| Expand between 1024 and 1279 px, reload | Expanded | Icon rail: the stored text is the same as after a wide window, so the two cannot be told apart |

Not changed, and not part of this issue: a rail stored below 1280 px (a first start there, a shrink of an open page, or a collapse) opens as the rail at the next start from 1280 px up, until one press of `[`.

Options not taken:

- `mergeDefaults` forcing `collapsed: true` when the default is collapsed: one line, but the merged value is written back. Each narrow start stores the rail, the next start from 1280 px up opens as the rail, and open wide windows flip to it.
- `mergeDefaults` letting the width decide at every start: a collapse from 1280 px up no longer survives a reload, against spec line 399.
- `flush: 'sync'` to keep the merged value from being written: a write sends a storage event to its own tab, whose handler pauses the watcher until the next tick, so a second change in that tick (`collapse()` makes two) is lost.
- A serializer that stores `collapsed: false` below 1280 px: repeats the breakpoint, and a narrow start erases a rail chosen on a wide window.

### 2. Comments

- `AppSidebar.vue`: the comment of the watcher points to the storage options of `DefaultLayout`.
- `stores/preferences.ts`: `UDashboardGroup` persists the sidebar's size and whether it is collapsed, not only its size.

### 3. Tests

- New `test/unit/components/docked-sidebar.test.ts` (Vitest, `DefaultLayout`, happy-dom's 1024 px): with `{"size":20,"collapsed":false}` stored, the sidebar starts as the rail with **Expand sidebar**, no **Unpin sidebar**, and the stored text unchanged; **Expand sidebar** then takes the stored 20 %.
- `test/e2e/navigation.spec.ts`, desktop only:
  - a new tab at 1100 px after a tab at 1440 px starts as the rail, the wider tab stays expanded and is expanded after a reload, and the first `[` expands the rail;
  - unpin at 1440 px, shrink to 1100 px, pin: the rail;
  - `[` at 1440 px and a reload keep the rail.

### 4. Documentation

- `docs/42for.net/platform/admin-ui.md`, the desktop line: from 1024 to 1279 px the sidebar starts collapsed to its icons, and widening the window expands it.
- `src/Platform/ui.admin/README.md`: the e2e list names the docked sidebar flows, if the list covers navigation.
