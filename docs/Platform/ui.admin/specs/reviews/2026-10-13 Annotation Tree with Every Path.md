# Annotation Tree with Every Path

Linear: [424-19](https://linear.app/424fun/issue/424-19/annotation-tree-view-should-show-only-subjects-or-only)

## Overview

This review covers the whole spec [2026-10-13 Annotation Tree with Every Path](../2026-10-13%20Annotation%20Tree%20with%20Every%20Path.md), delivered in one phase. The tree mode of **Annotations** now shows only subjects (by subject) or only responsibilities (by responsibility) at the root. Below them it follows every path of the inheritance graph, so an execution appears under its context and under its usage, and a unit of execution under its unit and under its execution. Each tree row has its own id (the path of keys), used for the row identity, the expansion and the keyboard cursor. The row selection stays keyed by annotation key, so every copy of a selected annotation shows selected.

## What Was Done

### 1. Tree projection: every path from the perspective's roots

`app/utils/tree-projection.ts`:

- `TreeNode` has a new `id`: the keys of the path from the root, joined with `/`. Grid rows use the key as the id. The id is never parsed.
- `projectTree` was rewritten as planned:
  1. It indexes the annotations with a valid key and the set of matching keys.
  2. `link(key)` walks `getDirectAncestors` up from every annotation and records the child edges (`Map<string, Set<string>>`). Ancestors that are not in the catalog get edges too, so they become "(missing)" nodes on every path.
  3. The roots are the linked keys of `rootCode[perspective]` (`sbt` or `rst`).
  4. `kept(key)` is memoized per key: the key matches, or a child is kept. All children are visited, so the memo is complete.
  5. `build(key, parentId, depth)` creates a new node per path with the kept children sorted by key. Roots are sorted by key too.
- `rootOrder` was replaced by the exported `rootCode`.
- `parentKey` stays for the breadcrumb. Its comment now says it is the primary chain of a perspective, and that the tree shows every path.
- `expandableKeys` became `expandableIds` and returns row ids.
- The selection mapping is in the same file, as planned: `rowSelectionOf(rows, byKey)` and `keySelectionOf(rows, byKey, previous, next)`, typed with `RowSelectionState` of `@tanstack/vue-table`.

The rest of the projection rules did not change. Without a filter, every node is kept (a missing node always has a real descendant). With a filter, the ancestors that do not match are `matched: false` and dimmed.

### 2. Annotations page (`annotations.vue`)

- Grid rows get `id = key`.
- `expanded`, `isExpanded(id)`, `toggle(id)` and "Expand all" (`expandableIds(tree)`) use row ids.
- `activeKey` was replaced by `activeId` and a computed `activeRow`: the row with the active id or, when there is none, the first row of the open annotation. `move()`, Enter, the live region and the `active-id` prop of the grid all use `activeRow`.
- `open(key, id?)` sets the cursor to the clicked row. The `selectedKey` watcher moves the cursor to the first row of the open annotation unless the active row already shows it, so links and the create dialog (which emit only a key) still move the cursor. When the detail closes, the active row is scrolled into view.

### 3. Grid (`AnnotationGrid.vue`, `grid/annotation-columns.ts`)

- `get-row-id` is `row.id`.
- `activeKey` became `activeId`, and `scrollToKey` became `scrollToId`. The open annotation is still highlighted by key, so all its copies are highlighted. The keyboard outline is on one row.
- `open` emits `(key, id)`; `toggle` emits a row id.
- A writable computed `tableSelection` is bound to UTable's `v-model:row-selection`. Its getter is `rowSelectionOf(rows, rowSelection)`. Its setter is `keySelectionOf(rows, rowSelection, tableSelection, next)`, with the cached previous state of the table. The `rowSelection` model and the bulk bar did not change.
- `ColumnOptions.isExpanded` / `toggle` take row ids, and the tree column passes `row.original.id`.

### 4. Mobile list (`AnnotationList.vue`)

The virtual items are keyed by `row.id`.

### 5. Tests

- `test/unit/utils/annotations.test.ts`, `projectTree`:
  - By subject: only subjects at the root (including the missing `sbt.contoso`). Executions are under the context and the usage, with their units of execution, and missing ancestors are placeholders on both paths.
  - By responsibility: only `rst.invoicing` at the root, with the unit (both units of execution), the missing usage of contoso, and the usage of northwind with its execution.
  - Row ids are unique and are the path of keys, with depths.
  - A filter keeps both paths to a unit of execution, and `expandableIds` lists the five ids with children.
  - Filters that match only `rst`/`unt` (by subject) or `sbt`/`cnt` (by responsibility) give an empty tree.
  - Selection: one copy selects the key, every copy shows selected, and clearing one copy clears the key while a key that is not shown stays selected.
- `test/e2e/annotations.spec.ts` (desktop): a new flow opens the tree filtered to the orphaned unit of execution of the mock data (`uxe.northwind.invoicing.legacy.pdf`):
  - By subject: it is shown twice, under `sbt.northwind` and the missing `cnt.northwind.legacy`, and no responsibility is shown.
  - Checking one copy checks both copies, and the bulk bar shows "1 selected". This checks the selection mapping through UTable, which the unit tests cannot: a component test of `AnnotationGrid` would need a layout for the virtual rows.
  - By responsibility (`?by=responsibility`): it is shown twice, under `rst.invoicing` and `unt.invoicing.pdf`, and no subject is shown.

  The spec planned to filter one execution. The orphaned unit of execution was used instead, because the mock data guarantees it, and it covers the missing ancestors too.

Results: `npm test` 174 passed (19 files). `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`) 60 passed, 2 skipped. `npm run typecheck`, `oxlint` and `eslint` are clean. No visual baseline shows the tree mode, so none changed.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`: the Tree mode section lists the paths of both perspectives, and says that an annotation with two parents appears under both, how selection works, and that missing parents and filtered ancestors are shown.
- The original spec (`2026-10-09 Administration UI for 2S Platform.md`, section 7) was not changed. It is historical; this spec and review supersede its tree projection table.

### Notes

- A filter that matches only types that are not in the perspective (for example `type:rst` by subject) shows an empty tree. The count in the filter bar still counts the matching annotations, as it did before for the grid.
- The tree can have at most twice as many nodes as the catalog (an execution or a unit of execution has two paths). The projection stays linear in the catalog size.
