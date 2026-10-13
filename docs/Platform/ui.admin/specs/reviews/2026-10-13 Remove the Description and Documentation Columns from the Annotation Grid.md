# Remove the Description and Documentation Columns from the Annotation Grid

Linear: [424-28](https://linear.app/424fun/issue/424-28/remove-description-and-documentation-columns-in-annotations-view)

## Overview

This review covers the whole spec [2026-10-13 Remove the Description and Documentation Columns from the Annotation Grid](../2026-10-13%20Remove%20the%20Description%20and%20Documentation%20Columns%20from%20the%20Annotation%20Grid.md), delivered in one phase. The annotation grid no longer has a **Description** or a **Documentation** column, and the **Columns** menu no longer offers them. Both fields are still shown and edited in the annotation detail, and the `has:docs` and `has:description` filters and the free-text search on the description work as before.

## What Was Done

### 1. Remove the columns (`annotation-columns.ts`)

In `app/components/annotations/grid/annotation-columns.ts`:

- the `description` and `documentation` column defs are removed from `annotationColumns()`;
- their labels are removed from `columnLabels`;
- `defaultHiddenColumns` is now `['validFrom', 'expiresAt']`.

As planned, `annotations.vue` (the Columns menu) and `AnnotationGrid.vue` are unchanged; the menu is built from the column defs and `columnLabels`, so it follows them.

### 2. Stored preferences

No migration, as planned. A `description` or `documentation` key left in `preferences.grid.annotations.columnVisibility` in local storage has no column to apply to, and TanStack Table reads the visibility by column id, so the key is ignored.

### 3. Tests

- **Unit** (`test/unit/annotations/annotation-columns.test.ts`, new file), four tests:
  - in grid and in tree mode, `annotationColumns()` has no `description` or `documentation` column;
  - `columnLabels` has no label for them, and `defaultHiddenColumns` is `['validFrom', 'expiresAt']`;
  - the hideable columns are exactly the ones in `columnLabels`, and every default-hidden column exists.

  The first three fail against the old `annotation-columns.ts` and pass with the change.
- **Visual:** the two columns were hidden by default and no visual test opens the Columns menu, so no baseline changed.

### 4. Documentation

No user-facing documentation lists the grid columns (`docs/42for.net/platform/admin-ui.md` only says "sortable, resizable columns"), so only the spec and this review were added. Section 8 of the original Administration UI spec still lists the columns; it is a historical document and stays unchanged.

### Results

- `npm test`: 175 passed (20 files), including the new file.
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`): 59 passed, 1 skipped.
- `npm run test:visual:docker`: 40 passed with the committed baselines.
- `npm run typecheck`, `oxlint` and `eslint` report no problems. Prettier reports no problems in the changed files.

### Notes

- The branch starts from `main`. `git merge-tree` merges it without a conflict with `feat/424-7`, `feat/424-9`, `feat/424-12`, `feat/424-13`, `fix/424-17`, `fix/424-19`, `fix/424-20`, `fix/424-21`, `fix/424-22`, `fix/424-23`, `fix/424-24`, `fix/424-25` and `fix/424-27`. `fix/424-19` also edits `annotation-columns.ts` (the tree cell is keyed by row id), but in other lines.
