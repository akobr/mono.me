# New Button on the Configurations Page

Linear: [424-42](https://linear.app/424fun/issue/424-42/new-action-button-on-configurations-page)

## Problem

The **Configurations** page of the admin UI (`src/Platform/ui.admin`) lists the configurations of the view. Its top bar has **Export** and **Import**, but no main action. To create a configuration you have to find its annotation first: open it on **Annotations**, then open the configuration link in its header. The configuration editor then offers **Create empty** and **Start from template**.

The other list pages have a main **New** button in the top bar: Annotations, Values store, Schemas, Views and Machines.

The issue asks for a main **New** button with the standard **+** icon on the Configurations page. It opens a dialog with a drop-down of every annotation key that has no configuration yet. It is just another way to start creating a configuration.

## Current State

- **Configurations page:** `app/pages/[org]/[project]/[view]/configurations.vue`.
  - Lines 71–74: `canEdit = useCan('edit')` (Contributor or a higher role) and the open state of the Export and Import dialogs.
  - Lines 79–81: `open(key)` opens a configuration as a nested route and keeps only the list filter: `router.push({ path: \`${base}/${key}\`, query: { q: route.query.q } })`.
  - Lines 123–144: the `#right` slot of `AppNavbar`. It holds **Export** (every role) and **Import** (`v-if="canEdit"`). Both are neutral ghost buttons in a tooltip, labelled only from 768 px (`:label="desktop ? 'Export' : undefined"`), each with one `aria-label` on every viewport (`Export configurations`, `Import configurations`).
  - Lines 167–172: the empty state, "No configurations in this view" (or "No configuration matches the filter"). Its description is out of date: "Open an annotation and create its configuration in the Configuration tab." The annotation detail has no Configuration tab any more. It has the configuration link in its header (`components/annotations/ConfigurationLink.vue`), and the old tab routes only redirect to `/configurations/<key>`.
  - Lines 201–202: the dialogs are mounted after the panel, the import one only with `canEdit`.
- **Rows:** `rows` (lines 40–60) has one row per entry of `catalog.configurations`, the configuration summaries of the view (`stores/catalog.ts`, line 78; `null` while unknown). The page does not look at `HasContent`, so a summary of an empty document is a row too.
- **The configuration detail opens for any key of the catalog** (`configurations/[key].vue`), also without a configuration. Its Edit tab (`components/configuration/ConfigurationEditor.vue`, lines 249–266) then shows **No configuration yet** with **Create empty** and **Start from template** for Contributors.
  - `create()` (lines 186–215) posts the document (`setConfiguration`), adds the summary to `catalog.configurations` (`catalog.setConfiguration`), invalidates the queries and shows "Configuration created".
  - The new row appears in the grid at once, highlighted as the open one.
  - A deleted annotation is refused (`409 AnnotationDeleted`). The real API would store a configuration for a key that is not an annotation, so only catalog keys may be offered.
- **The catalog** (`stores/catalog.ts`):
  - `byKey` holds the active and inactive annotations of the view, never the deleted ones. A snapshot from IndexedDB can hold a key deleted since then, until the fresh load replaces it (`fromSnapshot`).
  - `annotations` (line 90) is the list of `byKey`, recomputed on every change.
  - Every type can have a configuration. Configuration reads and writes do not depend on the status of the annotation (`docs/Platform/Storyteller/annotation-lifecycle.md`, line 51).
  - No helper lists the annotations without a configuration. `configurationOrigin()` (`utils/configuration-origin.ts`) answers per key.
- **Dialog patterns:**
  - `components/schemas/SchemaCreateDialog.vue`, **New schema**, is the closest. It picks a key from the catalog, closes, and opens the editor without creating anything (**Open editor**).
  - `components/annotations/TimeZoneSelect.vue` is a searchable, virtualized `USelectMenu` with object items and a slot per item.
  - `components/shell/CommandPalette.vue` (lines 46–57) gives an annotation its type icon in the type color (`metaOf(key.slice(0, 3))`, `ui.itemLeadingIcon`).
  - `components/common/KeyText.vue` writes a key in monospace, with the own name emphasized.
- **Main buttons elsewhere:**
  - `key-values.vue` (Export, Import, then **New key**) and `annotations.vue` (Reload, Bulk create, then **New**) put the primary solid button last, without a tooltip, behind `canEdit`.
  - The empty states of both pages repeat the button.
- **Size:** the `large` mock scenario has 16,317 annotations in `acme/billing/default`, 10,901 of them without a configuration. The list has to be virtualized.
- **Tests:**
  - No unit test mounts `configurations.vue`.
  - `test/e2e/transfers.spec.ts` (lines 93–98) checks that a Reader (`/northwind/portal/default/configurations`) has **Export** and no **Import**.
  - `test/e2e/accessibility.spec.ts` (lines 59–69) runs axe on the Export and Import dialogs.
  - `test/e2e/configuration.spec.ts` relies on `exe.northwind.invoicing.prod` having no own configuration in the seed.
  - `test/e2e/visual.spec.ts` (line 31) has the `configurations` screen. Its four baselines show the top bar. The mobile pair still predates the Export and Import buttons and the removal of the theme button, so all four get regenerated.
- **Docs:** `docs/42for.net/platform/admin-ui.md`, section Configuration (lines 120–153). It does not say how a configuration is created from the Configurations page. Line 90 says Contributors create one from the **No configuration** link of an annotation.
- **Planned elsewhere:** the spec [2026-10-15 Export and Import of a View](../../Storyteller/specs/2026-10-15%20Export%20and%20Import%20of%20a%20View.md) moves **Export** and **Import** off this page. **New** does not depend on them and stays.

## Proposed Changes

### 1. The keys without a configuration (`utils/configuration-origin.ts`)

A pure function next to `configurationOrigin()`:

```ts
/**
 * The keys a new configuration can be started for (424-42): the annotations of the catalog that are not deleted and
 * have no summary yet, so no row on Configurations. Sorted; null while the summaries are unknown.
 */
export function keysWithoutConfiguration(
  annotations: Iterable<Annotation>,
  configurations: ReadonlyMap<string, ConfigurationSummary> | null,
): string[] | null
```

- **"No configuration" means no summary.** A key is either a row of the grid or offered by **New**, never both. A summary with `HasContent: false`, an empty document, is already a row and opens from the grid. Using `HasContent` would also make a key come back to the list after **Create empty** once the summaries reload (the real API reports `{}` as `HasContent: false`).
- **Deleted annotations are left out** (`Deletion` set). This covers keys of a snapshot that were deleted since then.
- **Inactive annotations stay in.** They can have a configuration, and it still resolves.
- Sorted with `localeCompare`, like the grid.

### 2. The dialog (`components/configuration/ConfigurationCreateDialog.vue`, new)

Picks an annotation without a configuration, then opens its configuration. It does not create anything itself: the Edit tab already offers **Create empty** and **Start from template**, with the role checks, the errors and the catalog update. This is the pattern of **New schema**.

- **Model and event:** `defineModel<boolean>('open')` and `defineEmits<{ picked: [key: string] }>()`. The page opens the key with its `open()`, so the list filter is kept the same way as for a row.
- **Modal:** `UModal` titled **New configuration**, with the description `In {org}/{project}, view {view}.` like the other create dialogs, and `:ui="{ footer: 'justify-end' }"` like the Export and Import dialogs.
- **Body:** a `<form id="configuration-create" novalidate @submit.prevent="go">` with one `UFormField` labelled **Annotation**. Its help: "Annotations of the view without a configuration. Their configuration opens next, to start it empty or from the type template."
- **Drop-down:** a `USelectMenu`:
  - `v-model` the picked key, `value-key="value"`, `virtualize`, `class="w-full"`, `:ui="{ base: 'font-mono' }"`, the placeholder "Pick an annotation" and `:search-input="{ placeholder: 'Search keys…' }"`. The search matches the key, which also holds the name.
  - Its items are `{ label: key, value: key, icon, ui: { itemLeadingIcon } }`, with the icon and the color class of the type from `metaOf(key.slice(0, 3))`, as in the command palette.
  - The `#item-label` slot renders the key with `KeyText` (`size="sm"`, `:copy="false"`), so the own name stands out.
  - The items are built from `keysWithoutConfiguration(catalog.annotations, catalog.configurations)`. The computed value is only read while the dialog is open.
- **Readiness:** the list is complete only when `catalog.status === 'ready'`, the catalog is not a snapshot (`!catalog.fromSnapshot`) and the summaries are known. Until then the drop-down is disabled and loading, with the placeholder "Loading the catalog…".
- **Nothing to offer:** when the ready list is empty, the drop-down is replaced by a neutral `UAlert` (`i-lucide-info`): "Every annotation of the view has a configuration." When the view has no annotations at all: "The view has no annotations yet."
- **Footer:** **Cancel** (neutral ghost, closes) and **Open editor** (`type="submit" form="configuration-create"`, primary, disabled until a key is picked).
- **Open editor** closes the dialog and emits `picked` with the key. Enter in the form does the same.
- **Reset:** opening the dialog clears the picked key.

### 3. The button and the empty state (`configurations.vue`)

- **New** goes last in `#right`, after **Import**, as the main action of the page:

  ```vue
  <UButton
    v-if="canEdit"
    icon="i-lucide-plus"
    :label="desktop ? 'New' : undefined"
    aria-label="New configuration"
    @click="creating = true"
  />
  ```

  - Solid primary (the default), without a tooltip, like the **New** buttons of the other pages.
  - The label follows **Export** and **Import** on this page: shown from 768 px, an icon only below. A phone then has three icon buttons, as on Values store.
  - One accessible name on every viewport, **New configuration**, which starts with the visible label.
  - Only for Contributors and higher roles (`canEdit`), like **Import** and the Create buttons of the editor.
- **Dialog:** `<ConfigurationCreateDialog v-if="canEdit" v-model:open="creating" @picked="open" />`, next to the other two dialogs. `creating` is a new `ref(false)`. The comment above `canEdit` names the new button.
- **Empty state:**
  - The out-of-date description is replaced. For Contributors: "Create one with New, or from the configuration link in the header of an annotation." For the other roles, who cannot create one, there is no description.
  - For Contributors the empty state also has a **New configuration** button that opens the same dialog, as on Annotations and Values store. It shows with and without a filter: a filter that finds nothing is a good moment to create the missing one.

### 4. Tests

- **Unit** (`test/unit/configuration/configuration-create.test.ts`, new):
  - `keysWithoutConfiguration`:
    - leaves out the keys with a summary, also one with `HasContent: false`;
    - leaves out deleted annotations;
    - keeps inactive ones;
    - sorts the keys;
    - returns `null` without summaries.
  - `ConfigurationCreateDialog`, mounted with `mountScreen` after `catalog.load(scope)` on the mock API:
    - the drop-down offers exactly the seed keys without a configuration (for example `exe.northwind.invoicing.prod`, and not `usg.northwind.invoicing`);
    - **Open editor** is disabled until a key is picked;
    - picking a key and submitting closes the dialog and emits `picked` with that key;
    - with every annotation configured, the alert replaces the drop-down;
    - while the catalog loads, the drop-down is disabled.
- **End to end** (`test/e2e/configuration.spec.ts`), desktop and mobile:
  - "New opens the configuration of an annotation without one":
    - open `/acme/billing/default/configurations?q=northwind`, click **New configuration** and expect the **New configuration** dialog;
    - search `usg.northwind.invoicing` (configured) and expect no option;
    - search `exe.northwind.invoicing.prod` and pick it; **Open editor** opens `/acme/billing/default/configurations/exe.northwind.invoicing.prod?q=northwind` with **No configuration yet**;
    - **Create empty** shows "Configuration created";
    - **New** again no longer offers the key.
  - "a reader has no New button": `/northwind/portal/default/configurations` has no **New configuration**.
- **Accessibility** (`test/e2e/accessibility.spec.ts`): the loop over the dialogs of the page gets `['new', 'New configuration']`. A second check runs axe with the drop-down open.
- **Visual:** the four `configurations-{desktop,mobile}-{light,dark}.png` baselines are regenerated in the Playwright Linux image (`npm run test:visual:docker -- --update`). Only these four are kept.
- `npm test`, `npm run test:e2e`, `npm run typecheck` and `npm run lint` must pass, and Prettier must report nothing in the changed files.

### 5. Documentation

- `docs/42for.net/platform/admin-ui.md`, Configuration section: a paragraph on **New** (Contributor or a higher role). It lists the annotations of the view without a configuration and opens the configuration of the one you pick, where **Create empty** or **Start from template** creates it. The paragraph sits apart from "Export and import", which is planned to leave the page.
- `src/Platform/ui.admin/README.md`: the list of end-to-end flows names a new configuration from **New**, and its absence for a Reader. The axe line names the new configuration dialog.
- The review in `docs/Platform/ui.admin/specs/reviews/`.

### Out of scope

- **Creating in the dialog.** The dialog only starts the creation. Empty versus template is chosen in the editor, as before.
- **A command palette action.** The palette has actions only for the annotation dialogs. **New** is visible on every viewport, so it does not need one.
- **Export and Import.** Their buttons and dialogs do not change.
