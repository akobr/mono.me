# Compare on the Views Page Only with Another View

Linear: [424-64](https://linear.app/424fun/issue/424-64/compare-button-should-be-disabled)

## Problem

The **Views** page of a project in the Administration UI (`src/Platform/ui.admin`) lists the views of the project, and each row has a **Compare** button for Contributors and the roles above. In a project with only the default view, **Compare** is still enabled. A click opens the **Compare views** tab of **Modeling**, a tab that the header shows as disabled because there is no other view to compare with.

The issue asks for **Compare** to be disabled when there is nothing to compare.

The cause is wider than a project with one view. **Compare** always compares the default view with the view of the row, so the row of the default view compares the default view with itself, in every project. In a project with one view, that row is the only row.

## Current State

All paths are relative to `src/Platform/ui.admin/src`.

- **Views page:** `app/pages/[org]/[project]/views.vue`.
  - `canCompare = useCan('edit')` (lines 20–21) shows **Compare** to Contributors and above (Modeling is locked below Contributor, 424-38).
  - The views come from `useGetViews(org, project, () => ({ discover: canAdmin.value }))` (lines 23–28). For an Administrator, the list has discovered views too (`IsRegistered: false`). The API always lists `default`, first.
  - **Compare** (lines 112–119) is a `UButton` with `:to="/{org}/{project}/{context.view ?? 'default'}/modeling/compare?to={view.Name}"`, and nothing ever disables it.
  - The route `/[org]/[project]/views` has no `:view` parameter, and the context guard (`app/router/guards/context.ts`, lines 18–43) sets `context.view` from the route parameters only. So `context.view` is always `undefined` on this page, and every link starts from `default`. The row of `default` links to `/{org}/{project}/default/modeling/compare?to=default`.
- **Compare views:** `app/pages/[org]/[project]/[view]/modeling/compare.vue`.
  - `key` and `target` (lines 34–41) are writable computeds over `?key=` and `?to=`.
  - `viewItems` (lines 47–49) are the names of the views without the current one, from `useGetViews` without `discover`.
  - The watcher at lines 53–61 preselects the only other view when `?to=` is empty (424-20). It never replaces a `?to=` that is set, because the Views page can name a discovered view, which `viewItems` does not have.
  - `ready` (line 63) needs a key, a target and a scope. It does not check that the target is another view. With `?to=default` on the `default` view and a key, the page asks the API for the diff of `default` with `default` (`GET …/default/configuration/{key}/diff/default`) and loads the same configuration twice. Then it shows a diff with no changes, both sides labelled `default`. The API does not reject a diff of a view with itself.
  - With no other view and no `?to=`, the page shows the generic empty state "Compare a configuration across views", which asks for another view, while the select of views has no options.
- **Modeling header:** `app/components/modeling/ModelingHeader.vue`, lines 28–35. The **Compare views** tab is disabled once the views are known (`views.isSuccess`) and `hasOtherView(views, context.view)` (`app/utils/modeling.ts`, lines 39–42) is false. Nothing stops navigation to the disabled tab by its address. The Modeling spec ([2026-10-18 Modeling Page in the View Section](./2026-10-18%20Modeling%20Page%20in%20the%20View%20Section.md)) keeps an open Compare tab where it is, because of the discovered views. It left "Hiding the **Compare** button of the base view's own row on the Views page" out of scope.
- **Default view:** `app/utils/views.ts` has `defaultViewName = 'default'` and `isDefaultView`. The API sets `View.IsDefault` from that name, so it holds for every project.
- **Nuxt UI 4.11.3:** a `UButton` with `to` and `disabled` renders `<a role="link" aria-disabled="true" tabindex="-1">` without `href` (`LinkBase.vue`). A click is prevented, and the element is out of the tab order, so neither the mouse nor the keyboard can follow it. The app explains a disabled control with `<UTooltip :text="reason" :disabled="allowed">` around it (`RoleGate` in `pages/[org]/index.vue`, `MembersTable.vue`, `GraphToolbar.vue`).
- **Mock data:** `acme/billing` has `default` and `staging` (the mock user is Owner). `acme/shipping` has only `default` (Contributor). `northwind/portal` has only `default` (Reader).
- **Tests:**
  - No unit test mounts the Views page or the compare page.
  - `test/e2e/navigation.spec.ts`: "marks the default view as production on the Views page" (lines 105–115), and "Compare views is enabled only when the project has another view" (lines 329–340), which checks the tab only.
  - `test/e2e/configuration.spec.ts`: "compare views preselects the only other view" (lines 201–208).
  - Neither the Views page nor the compare page is in the visual baselines.
- **Docs:** `docs/42for.net/platform/admin-ui.md` says, in the Configurations section (line 146), that **Compare views** is disabled when the project has no other view. In the Modeling section (line 168) it says that the **Views** page shows **Compare** only to Contributors and above. `src/Platform/ui.admin/README.md` lists the e2e flows (line 71).

## Proposed Changes

### 1. Compare only another view on the Views page (`views.vue`)

**Compare** keeps its link and gets `disabled` on the row of the default view. That row is the view every comparison of this page starts from, so it has nothing to compare with. In a project with only the default view, it is the only row, which is the case of the issue.

- The base of the link becomes `defaultViewName` (`@/utils/views`) instead of `context.view ?? 'default'`. `context.view` is never set on this project page, so the link is the same, and the code says what it does.
- `:disabled="isDefaultView(view.Name)"`. The link of a disabled `UButton` has no `href`, so there is no link to follow, by mouse, by keyboard or with a middle click.
- A tooltip says why, as elsewhere in the app: `<UTooltip :text="compareHint" :disabled="!isDefaultView(view.Name)">` around the button. `UTooltip` renders its trigger as the child itself, so the row keeps its layout. The text depends on the list of the page:
  - with another view: "The other views are compared with this one";
  - without: "The project has no other view to compare with" (`hasOtherView(views.data.value, defaultViewName)` is false).
- Every other row keeps an enabled **Compare** with the same link as today, discovered views included. Compare views keeps a `?to=` that names a discovered view (424-20), so an Administrator can still compare the default view with a view that is not registered.
- The rows exist only once the views are loaded, so there is no loading state to handle.

```vue
<UTooltip v-if="canCompare" :text="compareHint" :disabled="!isDefaultView(view.Name)">
  <UButton
    :to="`/${context.org}/${context.project}/${defaultViewName}/modeling/compare?to=${view.Name}`"
    :disabled="isDefaultView(view.Name)"
    label="Compare"
    size="xs"
    color="neutral"
    variant="ghost"
  />
</UTooltip>
```

### 2. No comparison of a view with itself (`compare.vue`)

The fix of the Views page does not change links that were already saved or shared (`…/default/modeling/compare?to=default`, or the old `…/default/compare?to=default`, which redirects there). An address typed by hand can do the same. So the compare page treats a `?to=` that names the current view as no target:

```ts
get: () => {
  const to = text(route.query.to)
  return to === context.view ? undefined : to
},
```

- `ready` is false while there is no other view, so neither the diff nor the two documents are requested.
- The select shows its placeholder instead of the current view.
- The watcher of 424-20 sees no target, so in a project with exactly one other view it selects that view, and `router.replace` writes it over `?to=`. An old link of the Views page then opens the comparison that it meant to show.
- A `?to=` that names any other view, discovered or not, is kept as before.

### 3. Say that there is no other view (`compare.vue`)

The tab is disabled without another view, but the page can still be opened by its address, by an old link, or by the command palette. In that case, the generic empty state, which asks to pick another view, is replaced:

```ts
const alone = computed(() => !target.value && views.isSuccess.value && !hasOtherView(views.data.value, context.view))
```

```vue
<EmptyState
  v-if="alone"
  icon="i-lucide-git-compare"
  title="No other view to compare with"
  description="The project has only this view. An Administrator can create another one on the Views page."
/>
<EmptyState v-else-if="!ready" … />
```

- It uses the same test as the disabled tab of the header (the views query has succeeded, and `hasOtherView` is false), so the page and the tab agree. While the views load, or after an error, the generic empty state stays.
- A `?to=` that names a discovered view is a target, so it never shows this state.
- The page is not redirected away, as the Modeling spec decided.

### 4. Unit tests (`test/unit/modeling/compare.test.ts`)

A new file mounts both pages with `mountScreen` and the mock API (MSW in node, as `modeling.test.ts`), with `AppNavbar` replaced by a stand-in:

- **Views page:**
  - `acme/shipping` (Contributor, only `default`): **Compare** of `default` has `aria-disabled="true"` and no `href`, and a click leaves the route at `/acme/shipping/views`.
  - `acme/billing` (Owner, `default` and `staging`): **Compare** of `staging` links to `/acme/billing/default/modeling/compare?to=staging`. **Compare** of `default` is disabled.
  - A discovered view (the views response overridden with `default` and an unregistered `legacy`): **Compare** of `legacy` links to `?to=legacy`.
  - A Reader (`northwind/portal`) gets no **Compare**.
- **Compare views:**
  - `acme/billing`, `?to=default&key=…`: the URL gets `to=staging`, and no diff request ends with `/diff/default`.
  - `acme/shipping`, `?to=default&key=…`: no diff or configuration request is made, and the page shows "No other view to compare with".
  - `acme/shipping`, `?to=legacy&key=…` (a discovered view): the target is kept, and the diff of `default` with `legacy` is requested.
  - While the views of `acme/shipping` load, the generic empty state is shown, not the new one.

### 5. End-to-end tests

- `test/e2e/navigation.spec.ts`, after the production badge test, desktop and mobile. On `/acme/billing/views`, **Compare** of `default` is disabled, and **Compare** of `staging` opens `/acme/billing/default/modeling/compare?to=staging`. On `/acme/shipping/views`, **Compare** of `default` is disabled and has no `href`. On desktop, hovering it shows the tooltip "The project has no other view to compare with".
- `test/e2e/configuration.spec.ts`, after "compare views preselects the only other view":
  - `/acme/billing/default/modeling/compare?to=default&key=…` gets `to=staging` and shows the diff editor, and no request ends with `/diff/default`.
  - `/acme/shipping/default/modeling/compare?to=default&key=…` shows "No other view to compare with", and no diff request is made.
- `test/e2e/accessibility.spec.ts`: add the Views page of `acme/shipping`, whose only row has the disabled **Compare**.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - Configurations section (line 146): **Compare** on the **Views** page compares the default view with the view of its row, and is disabled on the row of the default view. A view is never compared with itself.
  - Modeling section (line 164): when **Compare views** is opened without another view, it says so.
- `src/Platform/ui.admin/README.md`: the e2e list (line 71) and the a11y list (line 74) name the new checks.

### Out of scope

- The command palette lists **Compare views** among the Modeling tabs even when the project has no other view, and switching the project keeps the tab. These paths now end on the new empty state, which says what is missing.
- Starting **Compare** of the Views page from the view the user came from (the remembered context of the menu) instead of the default view.
- Discovered views in the views list of Modeling, which the header does not count.
- Rejecting a diff of a view with itself in the API.

## The CodeRabbit Plan

The issue has a plan from CodeRabbit, taken as a second opinion:

- **Taken:** disable **Compare** where there is no distinct pair, never link a view to itself, explain why, require `target !== context.view` before the diff and the documents are requested, show a clear state when there is no other view, keep the links to discovered views, and keep the preselection of 424-20. Its test ideas (unit cases for one view, the view itself and discovered views; no navigation by mouse or keyboard; e2e for the Views page and for a direct self-comparison URL with no comparison request) are in sections 4 and 5.
- **Changed:**
  - The plan validates `?to=` and stops there. This spec also lets the page drop a self-comparison `?to=`, so the only other view is preselected and an old link still opens a useful comparison.
  - The plan wants the destination omitted while the button is disabled. Nuxt UI already renders a disabled link without `href`, so the link stays as it is.
- **Outdated:** the plan was written against an older `main` without the **Compare views** tab of Modeling. The tab and its disabled state (424-38) are in the current source, and this spec is written against them.
