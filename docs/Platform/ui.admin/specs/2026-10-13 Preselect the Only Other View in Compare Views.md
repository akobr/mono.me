# Preselect the Only Other View in Compare Views

Linear: [424-20](https://linear.app/424fun/issue/424-20/compare-views-should-preselected-view-if-there-is-only-one-option)

## Problem

**Compare views** in the Administration UI (`src/Platform/ui.admin`) shows the effective configuration of one key in the current view and in another view of the project. The user picks the key and the other view. When the project has only two views, there is only one view to pick, but the select still starts empty with the "Another view" placeholder. The user has to open it and pick the only option before anything can be compared.

The issue asks the page to preselect the view when there is only one option.

## Current State

- **Page:** `app/pages/[org]/[project]/[view]/compare.vue`.
  - The selection is in the URL: `?key=` (annotation key) and `?to=` (the other view). `key` and `target` (lines 34–41) are writable computeds over `route.query`; the setters call `router.replace` with the rest of the query.
  - `views` (lines 26–31) is `useGetViews(org, project)`, without `discover`, so it lists the registered views (and `default`).
  - `viewItems` (lines 47–49) is the names of those views without the current one (`context.view`). It is empty until the views are loaded.
  - `ready` (line 51) needs a key, a target and a scope. Until then the page shows the "Compare a configuration across views" empty state (lines 108–113).
  - The view select (lines 103–105) is a `USelect` bound to `target`, labelled "Compare {view} with", with the "Another view" placeholder.
- **Entry points:**
  - The sidebar link "Compare views" (`app/utils/navigation.ts`, line 54) opens `…/{view}/compare` without a query.
  - **Compare** on the Views page (`app/pages/[org]/[project]/views.vue`, line 111) opens `…/{view}/compare?to={name}`. For Administrators, that page also lists discovered views (`discover=true`), which the compare page does not list.
- **Mock data** (`app/mock/db.ts`, lines 148–167): `acme/billing` has two views, `default` and `staging`, so each of them has exactly one other view.
- **Tests:** no unit or e2e test covers the compare page. The configuration flows are in `test/e2e/configuration.spec.ts`.
- **Docs:** `docs/42for.net/platform/admin-ui.md`, line 93, says that "**Compare views** shows the same key in two views."

## Proposed Changes

### 1. Preselect the only other view (`compare.vue`)

Add a watcher on `viewItems`, with `immediate: true`, after the `viewItems` computed:

```ts
// With one other view there is nothing to pick: preselect it, unless ?to= already names a view.
watch(
  viewItems,
  (items) => {
    if (!target.value && items.length === 1) {
      target.value = items[0]
    }
  },
  { immediate: true },
)
```

- The preselection goes through the `target` setter, so it is written to `?to=` with `router.replace`. The URL stays the single source of the selection, and no history entry is added.
- It runs when the views are loaded (before that `viewItems` is empty), and again when the current view changes (the component is reused across views).
- It never replaces a `?to=` that is already set, even when that view is not in `viewItems`. The Views page can link to a discovered view, which `useGetViews` without `discover` does not return.
- With no other view (a project with one view) or with two or more, nothing changes: the select starts empty, as today.
- The key is not preselected. The issue is about the view only, and a project has many keys.

With a key in the URL, the comparison now loads at once. Without a key, the empty state stays until a key is picked.

### 2. End-to-end test (`test/e2e/configuration.spec.ts`)

A new test, desktop and mobile:

1. Open `/acme/billing/default/compare?key=exe.northwind.invoicing.prod` (the mock project has the views `default` and `staging`).
2. Expect the URL to get `to=staging`, and the "Compare default with" select to show `staging`.
3. Expect the empty state to be gone and the diff editor (`.cm-editor`) to be visible.

### 3. Documentation

`docs/42for.net/platform/admin-ui.md`, the Configurations section: say that **Compare views** preselects the other view when the project has only one other view.
