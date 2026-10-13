# Remove the Description and Documentation Columns from the Annotation Grid

Linear: [424-28](https://linear.app/424fun/issue/424-28/remove-description-and-documentation-columns-in-annotations-view)

## Problem

The annotation grid of the **Annotations** screen (`src/Platform/ui.admin`) offers a **Description** and a **Documentation** column. Both are hidden by default, but anyone can show them from the **Columns** menu. A description can be up to 10,000 characters and a documentation link up to 2048, so in a grid cell both are a truncated fragment that says little. They belong to the annotation detail, where the full text and a working link are shown.

The issue asks to remove the two columns from the grid and from the list of columns a user can show.

## Current State

- **Column definitions:** `app/components/annotations/grid/annotation-columns.ts`
  - `columnLabels` (lines 25–37) maps every column id to its label; `description: 'Description'` and `documentation: 'Documentation'` are lines 35 and 36.
  - `defaultHiddenColumns` (line 40) is `['validFrom', 'expiresAt', 'description', 'documentation']`.
  - `annotationColumns()` returns the column defs; the last two (lines 180–207) are `description` (a truncated `Description`) and `documentation` (a link to `DocumentationLink`).
- **Columns menu:** `app/pages/[org]/[project]/[view]/annotations.vue`, lines 150–164. `columnItems` lists the grid's hideable columns (`AnnotationGrid.hideableColumns()`) that have a label in `columnLabels`, so the menu follows the column defs.
- **Visibility state:** `app/components/annotations/AnnotationGrid.vue`, lines 32–38. The visibility comes from `preferences.grid('annotations').columnVisibility` in local storage, or from `defaultHiddenColumns` when nothing is stored, and every change is written back.
- **Other uses of the two fields stay as they are:** the annotation form and detail (`AnnotationForm.vue`), the `has:docs` and `has:description` filters and the free-text search on the description (`app/utils/filter-parser.ts`), the history, and the mock data.
- **Tests:** no unit or e2e test covers the grid columns. The e2e and visual suites never open the Columns menu, and both columns are hidden by default, so no screenshot shows them.
- **Docs:** `docs/42for.net/platform/admin-ui.md` says only "sortable, resizable columns" and does not list them. Section 8 of `docs/Platform/ui.admin/specs/2026-10-09 Administration UI for 2S Platform.md` lists them, but it is a historical spec and stays unchanged.

## Proposed Changes

### 1. Remove the columns (`annotation-columns.ts`)

- Remove the `description` and `documentation` column defs from `annotationColumns()`.
- Remove their entries from `columnLabels`.
- `defaultHiddenColumns` becomes `['validFrom', 'expiresAt']`.

The Columns menu is built from the column defs and `columnLabels`, so it no longer offers them; `annotations.vue` and `AnnotationGrid.vue` need no change.

### 2. Stored preferences

A user who has shown or hidden one of the columns has a `description` or `documentation` key in `columnVisibility` in local storage. TanStack Table looks the visibility up by column id, so a key without a column is ignored, and the grid renders without the columns. No migration is needed, and `AnnotationGrid.vue` stays unchanged, which also avoids conflicts with the open `feat/424-7` and `fix/424-19` branches.

### 3. Tests

- **Unit** (new `test/unit/annotations/annotation-columns.test.ts`):
  - `annotationColumns()` (grid and tree mode) has no `description` or `documentation` column;
  - `columnLabels` has no label for them, and `defaultHiddenColumns` is `['validFrom', 'expiresAt']`;
  - every hideable column has a label in `columnLabels`, and every default-hidden column exists, so the Columns menu and the defaults stay in step with the defs.
- `npm test`, `npm run typecheck` and `npm run lint` must pass; `npm run test:e2e` must still pass.

### 4. Documentation

No user-facing documentation lists the grid columns, so only this spec and its review are added.
