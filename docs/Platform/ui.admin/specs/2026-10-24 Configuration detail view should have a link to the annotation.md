# Configuration detail view should have a link to the annotation

Linear: [424-88](https://linear.app/424fun/issue/424-88/configuration-detail-view-should-have-a-link-to-the-annotation)

## Problem

The annotation detail has a button that opens the configuration of that annotation. An open configuration has only a small muted text link, **Annotation ›**, above the title. It goes to the annotation, but it does not look like the button on the other side, and it is easy to miss.

The issue asks for the same kind of direct navigation from a configuration back to its annotation.

## Current State

- **Configuration header:** `src/Platform/ui.admin/src/app/pages/[org]/[project]/[view]/configurations/[key].vue`.
  - The page is the shared `DetailPanel` (overlay, pinned, and full screen). The header slot is the same in every mode. Full screen is what a phone gets, below 768px, and the back button then shows (`md:hidden`, lines 36–44).
  - Lines 47–52 are a `RouterLink` to `/${org}/${project}/${view}/annotations/${key}`:

    ```vue
    <RouterLink
      :to="`/${context.org}/${context.project}/${context.view}/annotations/${key}`"
      class="flex items-center gap-1 text-xs text-muted hover:text-highlighted hover:underline"
    >
      Annotation <UIcon name="i-lucide-chevron-right" class="size-3" />
    </RouterLink>
    ```

    It sits above the name, inside the title column. It forwards no query. `query` (line 16) is `{ q: route.query.q }`, and the tabs, **Close**, the breadcrumb and **Lineage** keep that filter. This link does not.
  - Nothing hides the link. `catalog.byKey` only supplies the name. There is no `canEdit` check.
- **The other direction:** `app/components/annotations/ConfigurationLink.vue`.
  - A `UButton` with `:to`, `size="xs"` and a leading `i-lucide-settings-2`. The label, variant and color follow where the configuration comes from (`Configuration · vN` solid, **Inherited configuration** outline, **No configuration** ghost). A `UTooltip` adds the explanation. The button label is the accessible name.
  - `AnnotationDetail.vue` (lines 175–176) draws it on the row under the title, for every role. It also forwards no query.
- **Annotations icon:** the sidebar entry is `i-lucide-tags` (`app/utils/navigation.ts`, line 104). Icon names in `app/**/*.{vue,ts}` are bundled. A name built at runtime is fetched from Iconify, which the CSP blocks (`src/Platform/ui.admin/README.md`).
- **Unsaved edits:** `ConfigurationEditor.vue` calls `useUnsavedChanges(dirty)`. The guard (`app/composables/useUnsavedChanges.ts`) asks **Discard changes?** when the path changes, including a router link. **Keep editing** stays. **Discard** leaves. Same-path tab changes are ignored (`to.path === from.path`).
- **Tests:** `test/e2e/configuration.spec.ts` never clicks this link. The **New configuration** dialog has its own control named **Annotation** (`getByRole('button')` inside that dialog), which is a different control. `exe.northwind.invoicing.prod` under `/acme/billing/default` is the fixture, and the seed gives it no own configuration. `test/e2e/navigation.spec.ts` (lines 458–478) is the **Keep editing** / **Discard** pattern, on a Modeling tab. `test/e2e/accessibility.spec.ts` runs axe on this configuration detail. No unit test mounts the header.
- **Guide:** `docs/42for.net/platform/admin-ui.md`, section Configuration, describes the breadcrumb and **Lineage**. It does not mention a way back to the annotation. The annotation section documents the configuration button.

An attached coding plan suggested the same button, the same destination, no query, visibility for every role, and end-to-end coverage of the dirty editor. The code agrees. The plan is not the decision: the header already navigates, and the missing piece is that the control does not look like the button it answers.

## Proposed Changes

### 1. A labelled Annotation button in the configuration header

In `configurations/[key].vue`, replace the text `RouterLink` (lines 47–52) with a `UButton` that has `:to`. Keep it in that place, above the name, so overlay, pinned and full screen all show it.

- **Destination:** the same expression, `/${context.org}/${context.project}/${context.view}/annotations/${key}`. No `q`, and no parameter that records which configuration tab the user left. A list filter has no meaning on the annotation, and the configuration route does not record whether the user came from Annotations or Graph. This matches the current link and `ConfigurationLink`.
- **Look:** `size="xs"`, `variant="outline"`, `color="neutral"`, `icon="i-lucide-tags"`, `label="Annotation"`. A neutral outline reads as a related-resource action, in the same family as **Inherited configuration**, and it does not compete with **Save**. The icon name is a static literal. The button stays as wide as its label, so on a phone it does not stretch across the title column.
- **Name:** **Annotation** is the accessible name. A `UTooltip` with the text "Open the annotation" may describe it. The tooltip is not the name.
- **Who sees it:** everyone who can open the configuration, including a Reader. Related-resource navigation is not an edit. Do not consult `canEdit` or `catalog.byKey`.
- **How it navigates:** declarative `:to` only. No `@click` and no `router.push`. A path change still runs `useUnsavedChanges`, so a dirty editor asks **Discard changes?** before the annotation opens. Panel mode stays whatever the shell already remembers for a router navigation.
- Do not add a component. One call site does not need `AnnotationLink`. Do not change `ConfigurationLink.vue`, `AnnotationDetail.vue`, `DetailPanel.vue`, `useDetailPanel.ts` or `useUnsavedChanges.ts`.

### 2. User guide

In `docs/42for.net/platform/admin-ui.md`, in the Configuration section, say that **Annotation** above the name opens that annotation and does not keep the list filter.

### 3. Tests

Extend `test/e2e/configuration.spec.ts`. The control is a link (a `UButton` with `:to`), named **Annotation**. Select it with `getByRole('link', { name: 'Annotation', exact: true })` so it does not match the **New configuration** dialog's **Annotation** button, or a name that merely contains the word.

- **Destination.** Open `/acme/billing/default/configurations/exe.northwind.invoicing.prod?q=northwind`. Click the link. The URL is exactly `/acme/billing/default/annotations/exe.northwind.invoicing.prod`, with no `q`. The annotation detail is open: its configuration link is visible. That link's name depends on the seed (this execution has no own configuration, so it is **Inherited configuration** or **No configuration**, not the literal **Configuration**). Match `/configuration/i`.
- **Reader.** Open the same key under `/northwind/portal/default`, where the mock user is a Reader. The **Annotation** link is visible. The header does not depend on the catalog entry, so a missing annotation still shows the link.
- **Dirty editor.** Use `openFromTemplate` and `editJson` already in this file, which edit CodeMirror the same way as the other configuration tests. Click **Annotation**. The dialog **Discard changes?** is visible and the URL stays on the configuration. **Keep editing** leaves the editor dirty on that URL. Click **Annotation** again and **Discard**: the annotation URL from the first case opens, still with no `q`.
- Run the file in the desktop and mobile Playwright projects. The selector does not depend on where the button sits. From `src/Platform/ui.admin/src`, run `npm test` and `npm run test:e2e -- test/e2e/configuration.spec.ts`. The existing axe check of this detail stays in `accessibility.spec.ts`.
