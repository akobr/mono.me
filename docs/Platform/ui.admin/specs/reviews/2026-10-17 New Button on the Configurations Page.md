# New Button on the Configurations Page

Linear: [424-42](https://linear.app/424fun/issue/424-42/new-action-button-on-configurations-page)

## Overview

This review covers the whole spec [2026-10-17 New Button on the Configurations Page](../2026-10-17%20New%20Button%20on%20the%20Configurations%20Page.md), delivered in one phase.

The Configurations page has a main **New** button at the end of its top bar, for Contributors and higher roles. It opens **New configuration**, a dialog with a searchable drop-down of the annotations of the view without their own configuration. **Open editor** (or Enter after the pick) opens the configuration of the picked key and keeps the list filter. There, **Create empty** and **Start from template** create it, as from the configuration link of an annotation. The empty state of the page offers the same dialog.

## What Was Done

### 1. The keys without a configuration (`utils/configuration-origin.ts`)

`keysWithoutConfiguration(annotations, configurations)` is next to `configurationOrigin()`, as planned.

- It returns the keys of annotations without a summary, sorted with `localeCompare`, or `null` while the summaries are unknown.
- A summary of an empty document (`HasContent: false`) counts as a configuration, so a key is either a row of the grid or offered by **New**, never both.
- Deleted annotations (`Deletion` set) are left out. Inactive ones stay in.

### 2. The dialog (`components/configuration/ConfigurationCreateDialog.vue`, new)

As planned:

- `UModal` **New configuration** with the scope description and `:ui="{ footer: 'justify-end' }"`.
- One **Annotation** field in a form, **Cancel** and **Open editor** in the footer.
- `picked` is emitted with the key; the page opens it with its own `open()`, which keeps `q`.
- `USelectMenu` with `virtualize`, `value-key="value"`, the search placeholder "Search keys…", the type icon in the type color, and `KeyText` in `#item-label`.
- The list waits for the catalog: `status === 'ready'`, not a snapshot, and the summaries known. Until then the drop-down is disabled and loading ("Loading the catalog…").
- A neutral alert replaces the drop-down when nothing can be offered: "Every annotation of the view has its own configuration." or "The view has no annotations yet."

Deviations and additions:

- **Enter.** The spec said Enter in the form does the same as **Open editor**, but a drop-down cannot submit a form. After a pick, the focus now moves to **Open editor**, so the next Enter opens it. `SelectMenu` keeps a focus placed outside its content when it closes. The drop-down has `autofocus`, and it takes the focus again when the catalog becomes ready while the dialog is open. The whole keyboard flow is Enter, type, Enter, Enter.
- **Errors (review finding).** A failed catalog load (`status === 'error'` or `catalog.error` set) shows an error alert with **Retry** (`catalog.load(scope, { force: true })`), like `ErrorState`. Without it, a snapshot followed by a failed load left the drop-down loading forever. A failed request for the summaries alone still looks like loading, the same as the page behind the dialog.
- **Another view (review finding).** The pick is cleared when the scope changes, for example after Back while the dialog is open. **Open editor** is enabled only for a key of the current list (`pickable`).
- **Texts (review finding).**
  - The help reads "Annotations of the view without their own configuration. The one you pick opens in the editor, to start it empty or from the type template."
  - Most of the offered annotations inherit values, and their configuration link reads **Inherited configuration**. So "without a configuration" would mislead.
- **Monospace only for the value** (`:ui="{ value: 'font-mono' }"`), not for the placeholder.

### 3. The button and the empty state (`configurations.vue`)

- **New** is last in `#right`, solid primary, without a tooltip, labelled from 768 px like its neighbours, with the accessible name **New configuration** on every viewport. It shows only with `canEdit`, as planned.
- The dialog is mounted with `v-if="canEdit"` next to the Export and Import dialogs. The comment above `canEdit` names New (424-42).
- **Empty state:**
  - The out-of-date description is replaced. For Contributors it reads "Create one with New, or from the configuration link in the header of an annotation." Other roles get no description.
  - The **New configuration** button is in a conditional default slot (`<template v-if="canEdit" #default>`), so a Reader gets no empty button row.
- **Deviation (review finding): Import leaves the top bar below sm.**
  - On a 390 px phone, three icon buttons pushed the right group over the project switcher. Its chevron was drawn under the Export icon, and a tap there opened Export.
  - `annotations.vue` has the same rule for its third button.
  - A phone now has **Export** and **New**. Import stays from 640 px.
  - Letting the switcher buttons shrink (`min-w-0`) was tried as well. It cut "acme / billing" down to "a… / bil…", so it was not kept.

### 4. Tests

- **Unit** (`test/unit/configuration/configuration-create.test.ts`, new, 10 tests):
  - `keysWithoutConfiguration`:
    - the keys without a summary, sorted;
    - a `HasContent: false` summary counts;
    - deleted annotations are left out and inactive ones are kept;
    - `null` without summaries.
  - The dialog against the mock API:
    - it offers exactly the seed keys without own content, read from the mock database: `exe.northwind.invoicing.prod` in, `usg.northwind.invoicing` out;
    - a pick enables **Open editor** and moves the focus to it, and the submit emits `picked` and closes;
    - the alert when every annotation has a configuration;
    - the pick is cleared when the view changes;
    - the error with **Retry** loads the catalog again;
    - the drop-down is disabled while the summaries never answer.
  - The drop-down is read through the `SelectMenu` component (`items`, `disabled`, `placeholder`). The virtualized options do not render in happy-dom.
- **End to end** (`test/e2e/configuration.spec.ts`), desktop (1440 × 900) and mobile (390 × 844):
  - "New opens the configuration of an annotation without one":
    - from `?q=northwind`, searching `northwind.invoicing` shows `exe.northwind.invoicing.prod` and not `usg.northwind.invoicing`;
    - after the pick the focus is on **Open editor**, and Enter opens `/acme/billing/default/configurations/exe.northwind.invoicing.prod?q=northwind` with **No configuration yet**;
    - **Create empty** shows "Configuration created";
    - after a reload, the key is a row and is no longer offered, while `exe.northwind.invoicing.staging` still is.
  - "the empty list of a filter offers New configuration" (addition): `?q=no-such-annotation` shows the empty state, and its **New configuration** opens the dialog.
  - "a reader has no New button on Configurations": it checks the top bar through **Search…**, not **Export**, because Export and Import are planned to leave the page (424-40).
- **Accessibility:**
  - The loop over the dialogs of the page has `['new configuration', 'New configuration']`.
  - The Import entry skips phones, which have no Import button now.
  - **Deviation:** the planned second check, with the drop-down open, was dropped. axe reports `aria-required-children` (a `div[tabindex]` inside the listbox) and `scrollable-region-focusable` for every open virtualized `USelectMenu` of the app. The time zone in New annotation and the key picker of Compare views report the same two. They come from the Nuxt UI / Reka markup, not from this change.
- **Visual:** the four `configurations-{desktop,mobile}-{light,dark}.png` baselines were regenerated in the Playwright Linux image.
  - `--update` alone rewrote only the mobile pair. The desktop pair stayed within the 0.2 % tolerance with the new button (about 2,400 px of 1,296,000).
  - So all four were rewritten with `--update-snapshots=all --grep ' configurations$'` in the same image.
  - The mobile pair was regenerated again after Import left the phone top bar. It also catches up with the earlier top bar changes: the Export icon, no theme button.
  - No other baseline changes.

### 5. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - The Configuration section starts with a paragraph on **New** (Contributor or a higher role). It lists the annotations of the view without their own configuration, so without a row on the page, and **Open editor** opens the configuration of the one you pick. There, **Create empty** or **Start from template** creates it, as from the configuration link in the header of the annotation.
  - The paragraph stands apart from "Export and import", which is planned to leave the page.
  - The Import bullet says a phone has no Import in the top bar.
- `src/Platform/ui.admin/README.md`: the end-to-end list names a new configuration from **New**, which offers only the annotations without one, and its absence for a Reader. The axe line names the dialogs of Configurations.

### Results

- `npm run typecheck`, `oxlint` and `eslint` report no problems. Prettier reports no problems in the changed lines; its only remark in `configurations.vue` is an older line (the `updated` column), left as it is.
- `npm test`: 455 passed (50 files), including the 10 new tests.
- `npm run test:e2e` with `PLAYWRIGHT_CHANNEL=chrome`, desktop and mobile:
  - Before the review fixes, the whole suite gave 208 passed, 5 skipped and 3 failed. The three failures were mobile timeouts in `annotations.spec.ts` (bottom sheet), `configuration.spec.ts` (Lineage) and `deleted.spec.ts` (restore). They ran while the review agents loaded the machine, and all three passed in a rerun.
  - After the fixes, `configuration`, `accessibility` and `transfers` gave 116 passed and 4 skipped.
  - The final run of the whole suite gave 210 passed, 7 skipped and 1 failed. The failure was the new "New opens…" test on desktop: a strict-mode violation, because two **New configuration** buttons existed for a moment (see Notes). The helper now clicks the first one, the one of the top bar.
  - With that fix, `configuration.spec.ts` passed three times in a row on both viewports (`--repeat-each=3`: 66 passed).
- `npm run test:visual:docker` (compare only, after the last change): 60 passed.

### Notes

- **Code review.** Three agents reviewed the change: behaviour, UX and accessibility, and tests, spec and docs. Two skeptics then tried to refute every finding.
  - 15 findings came in. 10 survived, which make 6 distinct issues: the endless loading on a failed load, the pick kept across views, Enter, the phone top bar, the focus after a late load, and the wording. All six are fixed above.
  - Refuted:
    - the missing baselines, written while the review ran;
    - the missing review;
    - an untested empty state, which got a test anyway;
    - two others: the screen reader name of the trigger, and an earlier wording point.
- **Other branches.** The branch starts from `origin/main` (6bc7e0ae).
  - `git merge-tree` merges it with `feat/424-41` (#100) without a conflict.
  - With `feat/424-40` (#99, Export and Import of a view) it conflicts in `configurations.vue`, `accessibility.spec.ts`, `README.md` and the desktop configurations baselines. That branch removes Export, Import and both dialogs from this page.
  - Whichever merges second keeps **New** as the only button of `#right`. It then drops the comment about Import below sm, which leaves the phone room for the full label. It keeps the `new configuration` entry of the axe loop and regenerates the four configurations baselines.
- **Empty state while loading.** With a filter, the page shows "No configuration matches the filter" until the catalog has loaded. The rows need the annotations to match the filter. With this change the empty state also has a **New configuration** button, so for a moment there are two. That is what tripped the end-to-end test. The loading behaviour itself is older and is left as it is.
- **Unchanged on purpose.**
  - The command palette has no "New configuration" action, as planned. The button is visible on every viewport.
  - On Values store, the phone top bar still runs over the project switcher with its three outline buttons. The switcher buttons have a `truncate` label but cannot shrink. This change does not touch that page.
