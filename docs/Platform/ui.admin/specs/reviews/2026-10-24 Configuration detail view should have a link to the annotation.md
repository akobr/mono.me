# Configuration detail view should have a link to the annotation

Linear: [424-88](https://linear.app/424fun/issue/424-88/configuration-detail-view-should-have-a-link-to-the-annotation)

## Overview

This review covers the whole spec [2026-10-24 Configuration detail view should have a link to the annotation](../2026-10-24%20Configuration%20detail%20view%20should%20have%20a%20link%20to%20the%20annotation.md), delivered in one phase.

The muted **Annotation ›** text above a configuration's name is now a labelled **Annotation** button. It opens that annotation without the list filter, for every role, and a dirty editor still asks before leaving. The button stays in the header, so overlay, pinned and full screen all show it.

## What Was Done

### 1. A labelled Annotation button in the configuration header

`configurations/[key].vue` replaces the text `RouterLink` with a `UButton` that has `:to`. It stays above the name, inside the title column.

- **Destination:** `/${context.org}/${context.project}/${context.view}/annotations/${key}`. No `q`, and no parameter for the configuration tab the user left.
- **Look:** `size="xs"`, `variant="outline"`, `color="neutral"`, `icon="i-lucide-tags"`, `label="Annotation"`, `class="w-fit"`. The icon name is a static literal. The button is only as wide as its label.
- **Name:** **Annotation** is the accessible name. A `UTooltip` says "Open the annotation". The tooltip is not the name.
- **Who sees it:** everyone who can open the configuration. The header does not consult `canEdit` or `catalog.byKey`.
- **How it navigates:** declarative `:to` only. A path change still runs `useUnsavedChanges`.

No new component. `ConfigurationLink.vue`, `AnnotationDetail.vue`, `DetailPanel.vue`, `useDetailPanel.ts` and `useUnsavedChanges.ts` are unchanged.

### 2. User guide

`docs/42for.net/platform/admin-ui.md`, Configuration section: the **Lineage** sentence now ends with "**Annotation**, above the name, opens the annotation of this configuration. It does not keep the list filter."

### 3. Tests

`test/e2e/configuration.spec.ts` adds three cases. The control is selected with `getByRole('link', { name: 'Annotation', exact: true })`.

- **Destination.** `/acme/billing/default/configurations/exe.northwind.invoicing.prod?q=northwind` opens `/acme/billing/default/annotations/exe.northwind.invoicing.prod` with no `q`. The annotation detail shows **Inherited configuration**.
- **Reader.** The same key under `/northwind/portal/default` shows the **Annotation** link.
- **Dirty editor.** `openFromTemplate` and `editJson` set `retries` to 9. **Annotation** opens **Discard changes?** and the URL stays. **Keep editing** leaves `"retries": 9` in the editor. **Annotation** again, then **Discard**, opens the annotation URL with no `q`. CodeMirror accepted the edit in Playwright, so no unit fallback was needed.

The spec said the destination assertion may match `/configuration/i`, because the seed might show **Inherited configuration** or **No configuration**. The first run failed: that pattern also matches the sidebar link **Configurations**. The assertion is the exact name **Inherited configuration**. The seed for `exe.northwind.invoicing.prod` has no own configuration, and `data-origin="inherited"` is what `ConfigurationLink` shows. The spec file is left as written.

### Results

- Configuration end-to-end, desktop and mobile: 32 passed. The first run was 31 passed and 1 failed, on the loose configuration-name pattern above.
- Accessibility, `accessibility.spec.ts` filtered to "configuration has no serious", desktop and mobile: 8 passed.
- In the browser, on the mock app with the default dataset, **Annotation** on `http://localhost:5176/acme/billing/default/configurations/exe.northwind.invoicing.prod?q=northwind` opened `http://localhost:5176/acme/billing/default/annotations/exe.northwind.invoicing.prod` with no query. The page showed **Inherited configuration**. Back on the configuration, **Open in full screen** left the list, kept `?q=northwind`, and left **Annotation** in the header (about 99 px wide, at the left of the title). The action buttons stayed at the right. The control did not leave the viewport.
- `npm test` is not one green run. The first full run after the SDK in this worktree was built passed 1422 tests and timed out 5, in page-memory, soft-delete redirect and streaming-platform. Those three files, run alone, passed 54 of 54. A second full run passed 1404 and failed 23, almost all 5000 ms timeouts in `test/unit/router/page-memory.test.ts`, plus teardown errors importing `AnnotationDetailPanel.vue` and `AnnotationForm.vue`. That file had passed alone (19 tests) before the second full run. The header change does not mount those screens. The suite was not run a third time.

### Notes

- The three generated declaration files (`auto-imports.d.ts`, `components.d.ts`, `typed-router.d.ts`) show as modified with an empty content diff. Git reports that LF will be replaced by CRLF. They are not part of this change.
