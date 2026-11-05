# Docked Sidebar Starts as the Icon Rail Between lg and xl

## Overview

Between 1024 and 1279 px the docked sidebar opened expanded once any wider window had stored it expanded, because `UDashboardSidebar` shows its stored state and not its model. A stored "expanded" no longer decides the start, so these windows start as the icon rail, the first `[` works, and a start stores nothing. Spec: [2026-11-03 Docked Sidebar Starts as the Icon Rail Between lg and xl](../2026-11-03%20Docked%20Sidebar%20Starts%20as%20the%20Icon%20Rail%20Between%20lg%20and%20xl.md). Linear: [424-168](https://linear.app/424fun/issue/424-168/docked-sidebar-does-not-start-as-the-icon-rail-between-1024-and-1279).

## What Was Done

### 1. A stored "expanded" is no choice

`app/layouts/DefaultLayout.vue`: the group gets `:storage-options="sidebarStorage"`. The serializer is as planned: `read` drops a stored `collapsed: false`, so `isCollapsed` falls back to the start, and `write` puts it back, so a start over it writes nothing.

An independent review found two regressions in the planned change, and both are fixed here. They are deviations from the spec:

- **`listenToStorageChanges: false` (new).** `serializer.read` also runs on storage events, which the plan did not consider. A window that started below 1280 px, was expanded or widened, and then received another window's write fell back to its start: it showed the rail, even at 1440 px, while the shared `collapsed` ref stayed `false`, and its next `[` did nothing visible. An open window now reads the stored state only at its start. This also removes the dead `[` that two wide windows could already cause.
- **The start moved to `useSidebar.ts` (new).** The plan kept the immediate watcher in `AppSidebar.vue`. The logo goes through the bare route `/`, which mounts the layout again, so that watcher reset the ref and an expansion below 1280 px was lost, unlike with any other link. The media query, the start value and a watcher that is not immediate are now module state in `useSidebar.ts`, so the start applies once per page load and crossing 1280 px still switches the sidebar, also while a bare route is showing.

A review of the PR found a third case, also a deviation from the spec: when the layout mounted again (the logo link), the sidebar read the shared stored value again, so a rail stored by another window collapsed a window that was expanded. `sidebarStorage` moved to `useSidebar.ts` and now brings back a stored rail only on the first read of a page. Later reads (the layout mounting again, a new pin) keep the window's `collapsed`, and write back the stored value they did not change. Two rare cases remain: with nothing stored when a window starts, its first read comes later, so one remount can still take another window's rail; and in a page that starts unpinned, the first read happens at the pin, so a stored rail can apply then. The new unit test for this case was not run against the code before this change. After it, the whole Vitest suite (1495 tests), `vue-tsc --build`, `npm run lint` and the five docked sidebar e2e tests on desktop pass; the whole Playwright suite was not repeated.

### 2. Comments

- `stores/preferences.ts`: `UDashboardGroup` persists the sidebar's size and whether it is collapsed.
- `AppSidebar.vue`: the planned comment went away with the watcher. The explanation is in `useSidebar.ts` and `DefaultLayout.vue`.
- The review's point that "a stored rail is a choice" reads as a claim about who stored it was not a defect, but the comment now says "a stored rail stays".

### 3. Tests

- `test/unit/components/docked-sidebar.test.ts` (new, five tests at happy-dom's 1024 px): the start as the rail with the stored text unchanged; the stored 20 % after **Expand sidebar**; the expansion kept when the layout mounts again; the rail and the expanded sidebar each kept when a storage event arrives. Without the storage options all five fail. Without `listenToStorageChanges: false` only the expanded case fails, and resetting the ref when the sidebar mounts fails only the remount test.
- `test/e2e/navigation.spec.ts`, desktop only (five tests):
  - a new tab at 1100 px after a tab at 1440 px starts as the rail, the wider tab is expanded after a reload, and the first `[` expands the rail;
  - an open window crossing 1280 px in both directions (added);
  - the expansion kept after the logo link (added);
  - unpin at 1440 px, shrink to 1100 px, pin: the rail;
  - `[` at 1440 px and a reload keep the rail.

  The first and the pin test failed on the unfixed code. The planned check that the wider tab stays as it is right after the narrow start was dropped: with no storage events it cannot fail. The reload checks the storage instead.
- A review finding about a race in the pin test (clicking a palette option during its 100 ms filter delay) was measured and rejected, so the test is as written.

Runs:

- The whole Vitest suite (104 files, 1494 tests), `vue-tsc --build` and `npm run lint` pass. After the last test edit, lint, types and the two sidebar test files ran again and pass.
- The whole Playwright suite on desktop and mobile: 395 passed, 14 skipped, 1 failed. The failure was the new logo test: its URL check did not allow the query that the page restored, and starting on the page the logo leads to could not prove that the navigation had happened. It now starts on Configurations and waits for the path of Annotations; the five docked sidebar tests then passed three times each on desktop. The suite as a whole was not repeated after that test edit, which changed nothing else.
- An earlier full run, with a `dotnet` build and other work running at the same time, had three failures in other files (`accessibility`, `configuration`, `deleted`, one of them `net::ERR_NO_BUFFER_SPACE`). All three passed twice when run again, and the later full run did not repeat them.
- `NavigationMenuTests` (7 tests, with case 35, `Sidebar_IconRail_ExpandsCollapsesAndToggles`) passes with the `mock` profile against the e2e build on port 4174. It was not run with the `dev` profile against the local stack where the issue was reported, which needs a running API and the pool passwords, and it was not run on the unfixed code with the `mock` profile.

Known limits, for the maintainer:

- An expansion between 1024 and 1279 px is not kept over a reload: the stored text is the same as after a wide window.
- The spec left a rail stored below 1280 px opening as the rail at the next start from 1280 px up. A later PR review changed that: `serializer.write` no longer stores a rail below 1280 px as a choice, so a first start there, a shrink or a collapse leaves the stored value as it was. Startup is also marked done when the layout mounts (`markSidebarStarted`), so it no longer depends on a stored value. This replaces the two rare cases noted in section 1; as a result, a page that starts unpinned does not bring back a stored rail when it is pinned.
- Open windows no longer follow each other's collapse or width until their next start, and a window's next write carries its own size over a width dragged in another one.
- The fix relies on the `?? defaultStorageValue.collapsed` fallback of `useResizable.js:34` (`@nuxt/ui` 4.11.3). `storageOptions` is typed `Record<string, any>`, so an upgrade would not fail at compile time; the unit tests would catch it.

### 4. Documentation

- `docs/42for.net/platform/admin-ui.md`, the desktop line: the start between 1024 and 1279 px, widening, and a collapse kept through a reload in a wider window.
- `src/Platform/ui.admin/README.md`: the e2e list names the docked sidebar flows.
