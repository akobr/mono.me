# Type Names in the Configurations Grid

Linear: [424-34](https://linear.app/424fun/issue/424-34/fix-configurations-page-to-show-configured-annotation-type-names-in)

## Problem

Since [424-13](2026-10-13%20Annotation%20Type%20Names,%20Colors%20and%20Icons%20as%20Project%20Settings.md), a project's **Settings** and a user's Preferences can rename the seven annotation types and change their colors and icons. The **Type** column of the annotations grid shows the full name, with the icon and the color. The **Type** column of the **Configurations** grid (`src/Platform/ui.admin`) still shows the code (`exe`). It follows the custom color and icon, but a renamed type ("Job") is still shown as `exe`, and the name is only in the tooltip.

The issue asks for the custom color, icon and name in that column, the same as on the annotations page. It also states the rule: the **Schemas** page is the only place where the real type code may stay visible, and that page must not change.

## Current State

- **`AnnotationTypeBadge.vue`** (`app/components/annotations/`) has three variants:
  - `chip` (the default): the icon and the **code** (`meta.code`), with the name only in `title`;
  - `label`: the icon and the **name** (`meta.label`);
  - `icon`: only the icon, with the name in `title` and `aria-label` (`role="img"`).

  `meta` comes from `metaOf`, which reads the reactive `typeMeta`. `applyTypeAppearance` writes the custom names and icons into `typeMeta`; the colors follow the `--st-*` tokens. Every variant follows the custom color and icon, but only `label` and `icon` follow the custom name.
- **Annotations grid** (the reference), `app/components/annotations/grid/annotation-columns.ts`, lines 83–97: the Type column renders `h(AnnotationTypeBadge, { type, variant: 'label' })`. It is 160 px wide (`width(160)`, which sets `width` and `maxWidth`), and 260 px in Tree mode. The phone list (`AnnotationList.vue`, line 47) uses `variant="icon"`.
- **Configurations grid**, `app/pages/[org]/[project]/[view]/configurations.vue`:
  - lines 77–82, the Type column: `h(AnnotationTypeBadge, { type: row.original.key.slice(0, 3) })`, without a variant, so it is a `chip` with the code. The column is 96 px wide (`th` and `td`), enough for a code but not for "Responsibility" or "Unit of execution". Custom names can have up to 40 characters.
  - line 159, the phone list: `variant="icon"`, the same as the annotations phone list.
  - The detail header (`configurations/[key].vue`, line 45) already uses `variant="label"`.
- **Other places that show the code as the type's name** (found by an audit of every `AnnotationTypeBadge` and `typeMeta`/`metaOf` use in `app/`), all with the default `chip`:
  - `app/pages/[org]/[project]/[view]/deleted.vue`, line 308: the Key column of the **Deleted** screen, before the key, on desktop and on phones (one table).
  - `app/components/annotations/DeletedKeysDialog.vue`, line 24: the **Deleted annotations** dialog that a create asks when a key or an ancestor is deleted. The 424-13 review kept the code here on purpose; this issue's rule replaces that decision.
  - `app/components/graph/GraphTooltip.vue`, line 39: the hover card of a node on the **Graph**.
- **Schemas** (excluded): `app/pages/[org]/[project]/[view]/schemas.vue`, line 94, a `chip` with the code on every schema row.
- **Other uses that keep the code or the API type name on purpose:**
  - the preview of `TypeAppearanceEditor.vue` (Settings and account), which shows the code next to the name as the type's stable identifier;
  - keys (`exe.northwind…`), the filter syntax (`type:exe`), the JSON/YAML documents and the binding language (`@annotation("…", "Subject")`).
- **Tests:**
  - No unit or e2e test asserts the text of a type chip.
  - `test/e2e/settings.spec.ts` renames Execution to "Job" on Settings and checks the Templates page.
  - The visual suite (`test/e2e/visual.spec.ts`) has a `deleted` screen (its baselines show the `sbt` chip) but no Configurations or Graph screen.
- **Docs:**
  - `docs/42for.net/platform/admin-ui.md`, line 49, says that the annotations grid's Type column shows the full name.
  - Line 169 says that chips, lists, filters and dialogs all use the custom names, which is not true yet for the places above.

## Proposed Changes

### 1. The name in the Type column of the Configurations grid (`configurations.vue`)

- The Type column renders `h(AnnotationTypeBadge, { type: row.original.key.slice(0, 3), variant: 'label' })`: the custom icon, color and name, the same as the annotations grid.
- The column becomes 160 px wide, the same as the annotations grid. The `td` style also sets `max-width: 160px`, so a long custom name cannot widen the column.
- The column stays unsortable: the grid has no sorting; rows are ordered by key.
- The phone list keeps `variant="icon"` (the name is its `title` and accessible name), the same as the annotations phone list.

### 2. The name instead of the code everywhere else except Schemas

Following the issue's rule, the remaining chips outside the Schemas page get `variant="label"`:

- `deleted.vue`, the Key column of the Deleted screen. The table has the same layout on all widths and already scrolls sideways on phones, so the name is shown there too.
- `DeletedKeysDialog.vue`, each deleted key in the dialog.
- `GraphTooltip.vue`, the hover card of a graph node.

`schemas.vue` and its child routes do not change. `AnnotationTypeBadge` keeps `chip` as its default, so that the Schemas page does not need to change. After this change, the Schemas page is its only user.

**Not changed**, because they are identifiers or syntax, not the name of a type:

- the code in the preview of the type appearance editor;
- the "Type `exe` to confirm" field of a template's delete dialog, which asks the user to type an identifier, like **Delete permanently** asks for the key;
- keys, the `type:` filter, documents and the binding language.

### 3. Tests

- **E2E** (`test/e2e/settings.spec.ts`), desktop and mobile: a new test renames a type on **Settings** and opens **Configurations**, filtered to that type.
  - On desktop, the Type cell of the rows shows the new name, and no row shows the code.
  - On a phone, the list's type icon is named after the new name.
  - The desktop check fails before the change.
- **Unit** (new `test/unit/annotations/type-names.test.ts`): with a custom name applied through `applyTypeAppearance`, the Deleted annotations dialog and the graph hover card show the name and not the code. `afterEach` puts the built-in values back (`applyTypeAppearance({})`), as in `test/unit/stores/type-appearance.test.ts`.
- **Visual:**
  - a new `configurations` screen in `visual.spec.ts` (`/acme/billing/default/configurations`), with four new baselines;
  - the `deleted` baselines are regenerated, because the chip becomes the name;
  - `schemas` must not change.

  Run with `npm run test:visual:docker` (`-- --update` for the new and changed screens).
- `npm test`, `npm run test:e2e`, `npm run typecheck` and `npm run lint` must pass.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`:

- the paragraph about the Type column (line 49) also names the Configurations grid;
- the annotation types paragraph (line 173) adds that the Schemas page keeps showing the codes, next to the keys, the API, the documents and the filter.
