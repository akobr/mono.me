# Compare on the Views Page Only with Another View

Linear: [424-64](https://linear.app/424fun/issue/424-64/compare-button-should-be-disabled)

## Overview

This review covers the whole spec [2026-10-19 Compare on the Views Page Only with Another View](../2026-10-19%20Compare%20on%20the%20Views%20Page%20Only%20with%20Another%20View.md), delivered in one phase. **Compare** on the Views page is now disabled on the row of the default view, which is the only row of a project without another view, and a tooltip says why. Compare views no longer takes a `?to=` that names its own view, and it says so when the project has no other view. There is one deviation from the plan. A review of the first version found that comparing `?to=` with the context store cancelled a view switch made from Compare views. The page now reads its view from the route and waits for a navigation to land (section 2).

## What Was Done

### 1. Compare only another view on the Views page (`views.vue`)

As planned:

- The link starts from `defaultViewName` instead of `context.view ?? 'default'`.
- `:disabled="isDefaultView(view.Name)"` is set on the button.
- A `UTooltip` wraps the button and is disabled on every other row.
- `compareHint` holds the tooltip text. It is "The other views are compared with this one" when the project has another view, and "The project has no other view to compare with" when it does not. It is computed with `hasOtherView` over the page's list, so discovered views count.
- Discovered views keep an enabled **Compare**.

### 2. No comparison of a view with itself (`compare.vue`)

**Deviation.** The plan compared `?to=` with `context.view`. The context guard is a `beforeEach`, so it sets the store before the route commits. Take a switch from `default?to=staging` to `staging`:

1. While the navigation was pending, the target became empty.
2. The 424-20 preselection then replaced `?to=` on the old route.
3. That replace cancelled the switch, and the user was left on `default` with `?to=default`.

The page now works as follows:

- `page` holds the route's params.
- The target getter drops a `?to=` that equals the route's view.
- `landed` is true when the route's org, project and view equal the store's.
- The preselection watches `[viewItems, target, landed]` and acts only once a navigation has landed.
- `ready` requires `landed`.

As a result:

- The view switcher lands on the other view, with its only other view preselected. Before, it landed without `?to=`.
- A project switch to a project whose views are cached is no longer cancelled by the preselection. This was a 424-20 issue.
- No diff of a view with itself is requested while a navigation is pending.

### 3. Say that there is no other view (`compare.vue`)

As planned: "No other view to compare with" replaces the generic empty state. `alone` also requires `landed`.

### 4. Unit tests (`test/unit/modeling/compare.test.ts`)

The new file has 11 tests: the four Views page cases of the spec, and seven for Compare views.

The Compare views tests:

- a self `?to=` with another view;
- a self `?to=` with no other view, which makes no requests;
- a discovered `?to=`;
- loading;
- error;
- a view switch, which lands with `to=default` and makes no self-diff;
- a project switch with cached views.

These tests mount through the real routes and the real `contextGuard`. For that, `mountScreen` got a `routes` option, and its default is unchanged.

Checks against broken code:

- Without the source change, 6 of the first 9 tests fail.
- On the first version, both switch tests fail with "Navigation cancelled".
- Removing `landed` from `ready`, or from the watcher, fails one test each.

### 5. End-to-end tests

- **`navigation.spec.ts`:**
  - "Compare on the Views page is enabled only for another view". On desktop, the tooltip is checked as the link's accessible description. reka-ui's `role="tooltip"` node is `aria-hidden`, so `getByRole('tooltip')` finds nothing. The planned click on the disabled link was left out, because a URL check right after a click cannot catch an asynchronous router navigation. The unit test covers the click.
  - New: "the view switcher opens Compare views of the view it compared with". It runs on desktop only, because the switcher is hidden on a phone.
- **`configuration.spec.ts`:** "compare views never compares a view with itself". It also polls for the `/diff/staging` request, which proves that the page's requests are visible to the test.
- **`accessibility.spec.ts`:** the Views page of `acme/shipping` was added.

Results:

- `npm test`: 842 tests, 841 passed. The one failure, "selects the first type that can descend when the key changes" in `templates-schemas.test.ts`, also fails on `main`. It expects the path from before Modeling, and it is fixed on `fix/424-66` and `feat/424-52`.
- `navigation`, `configuration` and `accessibility` e2e on desktop and mobile with `PLAYWRIGHT_CHANNEL=chrome`: 204 passed, 2 skipped.
- `npm run typecheck`, `oxlint`, `eslint` and Prettier on the changed code report no problems.
- No visual baseline shows these pages.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`, Configurations section: covers **Compare** on the Views page, the disabled default row with its tooltip, the message shown when there is no other view, and that a view is never compared with itself.
- `src/Platform/ui.admin/README.md`: covers the e2e flows, the axe list, and the Modeling note on the route-based view and the `landed` wait.

### Notes

- Two review rounds with adversarial verification took place. They found the navigation race, the e2e tooltip locator, the vacuous click check, the switcher test on mobile, the untested `ready` gate and a README overclaim. All of them were fixed.
- Keyboard and touch users cannot open the tooltip of the disabled **Compare**. The other disabled controls of the app with tooltips have the same limit.
- Still out of scope:
  - the command palette, which offers Compare views with one view;
  - an API that accepts a diff of a view with itself;
  - discovered views in the Modeling list.
