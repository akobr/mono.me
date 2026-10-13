# Preselect the Only Other View in Compare Views

Linear: [424-20](https://linear.app/424fun/issue/424-20/compare-views-should-preselected-view-if-there-is-only-one-option)

## Overview

This review covers the whole spec [2026-10-13 Preselect the Only Other View in Compare Views](../2026-10-13%20Preselect%20the%20Only%20Other%20View%20in%20Compare%20Views.md), delivered in one phase. When the project has only one view other than the current one, **Compare views** now selects it and writes it to `?to=`. With a key in the URL, the comparison loads at once. A `?to=` that is already set is kept, and with no other view or two or more, the select starts empty as before.

## What Was Done

### 1. Preselect the only other view (`compare.vue`)

`app/pages/[org]/[project]/[view]/compare.vue` has a new watcher on `viewItems` (`immediate: true`), as planned. When `?to=` is empty and `viewItems` has exactly one name, it sets `target` to that name. The `target` setter calls `router.replace`, so the choice is in the URL and adds no history entry.

- Until the views are loaded, `viewItems` is empty, so the watcher does nothing.
- The page component is reused when the view changes (the root `<RouterView>` has no key). In that case the watcher runs again for the new view.
- A `?to=` from the Views page (it can name a discovered view, which this page does not list) is never replaced.
- The key is not preselected.

### 2. End-to-end test (`test/e2e/configuration.spec.ts`)

New test "compare views preselects the only other view", desktop and mobile. It opens `/acme/billing/default/compare?key=exe.northwind.invoicing.prod`; the mock project has the views `default` and `staging`. Then it checks that:

- the URL gets `to=staging`;
- the "Compare default with" combobox shows `staging`;
- the empty state is hidden and the diff editor (`.cm-editor`) is visible.

Without the fix, the test fails at the URL check (no `to=`).

Results:

- `npm test`: 171 passed (19 files).
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`): 61 passed, 1 skipped.
- `npm run typecheck`, `oxlint`, `eslint` and Prettier report no problems.
- No visual baseline shows the compare page, so no baseline changed.

### 3. Documentation

In `docs/42for.net/platform/admin-ui.md` (Configurations section), the sentence about **Compare views** now says that the only other view is selected for you.

### Notes

- In a project with only one view, the select has no options, and the page still shows the generic empty state ("Pick a key and another view…"). A message that the project has nothing to compare with would be clearer. It is outside this issue.
