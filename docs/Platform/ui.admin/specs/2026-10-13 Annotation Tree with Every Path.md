# Annotation Tree with Every Path

Linear: [424-19](https://linear.app/424fun/issue/424-19/annotation-tree-view-should-show-only-subjects-or-only)

## Problem

The tree mode of **Annotations** in the Administration UI (`src/Platform/ui.admin`) has two perspectives, **By subject** and **By responsibility**. Each one shows every annotation exactly once, so it mixes both kinds of roots and cuts the inheritance graph:

- **By subject** lists the subjects, then the responsibilities (with their units) at the root level.
- **By responsibility** lists the responsibilities, then the subjects (with their contexts).
- An execution has two parents, a usage and a context, but it appears under only one of them. A unit of execution has two parents, a unit and an execution, and also appears under only one.

So in the subject perspective, a usage looks like a leaf, and the executions of that usage are not visible below it. In the responsibility perspective, a unit has no units of execution below it.

The issue asks for:

- Only subjects, or only responsibilities, at the root, depending on the selected perspective.
- The full view of all descendants, through all paths. For example, by subject:
  - `sbt → cnt → exe → uxe`
  - `sbt → usg → exe → uxe`

## Current State

- **Inheritance graph** (SDK, `src/Platform/Storyteller/sdk.typescript/src/lib/keys.ts`, `getDirectAncestors`, lines 140–164):

  | Type | Direct ancestors |
  | --- | --- |
  | `rst`, `sbt` | none |
  | `unt.{r}.{u}` | `rst.{r}` |
  | `cnt.{s}.{c}` | `sbt.{s}` |
  | `usg.{s}.{r}` | `rst.{r}`, `sbt.{s}` |
  | `exe.{s}.{r}.{c}` | `usg.{s}.{r}`, `cnt.{s}.{c}` |
  | `uxe.{s}.{r}.{c}.{u}` | `unt.{r}.{u}`, `exe.{s}.{r}.{c}` |

- **Tree projection:** `app/utils/tree-projection.ts`.
  - `parentKey(parsed, perspective)` (lines 22–46) picks **one** parent per perspective. By subject: `usg → sbt`, `exe → cnt`, `uxe → exe`, `unt → rst`. By responsibility: `usg → rst`, `exe → usg`, `uxe → exe`, `cnt → sbt`.
  - `projectTree(annotations, perspective, matches?)` (lines 55–136) links every annotation to that parent and adds a synthetic "(missing)" node for a parent that is not in the catalog. Roots are sorted by `rootOrder` (lines 17–20): `sbt` then `rst` by subject, `rst` then `sbt` by responsibility. With a filter, it keeps the matching nodes and their ancestor chain; kept ancestors that do not match have `matched: false` and are dimmed.
  - `TreeNode` (lines 6–15) has no row identity other than `key`, which is unique only because every annotation appears once.
  - `expandableKeys(roots)` (lines 138–149) returns the keys of all nodes with children, for "Expand all".
- **Annotations page:** `app/pages/[org]/[project]/[view]/annotations.vue`.
  - `tree` (lines 48–56) projects the catalog, `rows` (lines 61–82) flattens the expanded part. Grid rows are built as `TreeNode`s with `key` only.
  - `expanded` (line 28) is a set of annotation keys; `isExpanded`, `toggle` (lines 59, 86–94) and "Expand all" (line 219) use keys.
  - The keyboard cursor `activeKey` (line 29) is an annotation key. `move()` (lines 103–110) finds the row by key; focus return (lines 142–148) scrolls to a key.
- **Grid:** `app/components/annotations/AnnotationGrid.vue`.
  - `get-row-id` is `row.key` (line 106), so TanStack and the virtual rows key the rows by annotation key. The row selection (`v-model:row-selection`) is therefore keyed by annotation key, and `annotations.vue` passes those keys to the bulk bar (line 84).
  - `rowClass` (lines 53–59) highlights the selected and the active row by key; `scrollToKey` (lines 62–76) scrolls to the first row with a key.
  - The tree column (`grid/annotation-columns.ts`, lines 88–99) calls `isExpanded` and `toggle` with `row.original.key`.
- **Mobile list:** `AnnotationList.vue` renders grid rows only (the tree mode is desktop only) and keys its items by `key`.
- **Breadcrumb:** `AnnotationBreadcrumb.vue` (lines 28–43) builds the chain of parents of the open annotation with `parentKey`. It shows one chain, which stays useful as a "primary" path.
- **Tests:** `test/unit/utils/annotations.test.ts` (lines 38–108) tests the current shape of both perspectives, including the mixed roots.
- **Docs:** the original spec (`specs/2026-10-09 Administration UI for 2S Platform.md`, section 7) describes the single-parent projection. The user guide (`docs/42for.net/platform/admin-ui.md`, line 25) says "grouped by subject or by responsibility".

## Proposed Changes

### 1. Tree projection: every path from the perspective's roots

`projectTree` unfolds the inheritance graph from one kind of root, following every child edge:

| Perspective | Roots | Below the roots (all paths) | Not shown |
| --- | --- | --- | --- |
| By subject (default) | `sbt.*` | `sbt → cnt → exe → uxe`, `sbt → usg → exe → uxe` | `rst`, `unt` |
| By responsibility | `rst.*` | `rst → unt → uxe`, `rst → usg → exe → uxe` | `sbt`, `cnt` |

The hidden types are exactly the ones that are not descendants of the perspective's roots. Every usage, execution and unit of execution has both a subject and a responsibility ancestor, so it is always shown.

An annotation with several parents appears once **per path**: by subject, an execution appears under its context and under its usage, and its units of execution appear under both copies. By responsibility, a unit of execution appears under its unit and under its execution. So a row count is at most twice the catalog (20k annotations → at most 40k nodes), and the projection stays linear.

**Algorithm** (`app/utils/tree-projection.ts`):

1. Index the annotations with a valid key, with `matched = !matches || matches(annotation)`.
2. Build the child edges from `getDirectAncestors` (SDK, via `utils/annotation-key.ts`), walking up from every annotation. An ancestor that is not in the catalog becomes a synthetic "(missing)" node, with its own ancestors linked the same way. So data problems stay visible on every path.
3. The roots are the graph nodes of the perspective's root type (`sbt` or `rst`), real or missing.
4. `kept(key)` is memoized per graph node: the node matches, or one of its children is kept. Without a filter, every node is kept (a missing node always has a real descendant).
5. Unfold from each root: one new `TreeNode` per path, with `depth` and the kept children only, sorted by key (so contexts come before usages under a subject, and units before usages under a responsibility). Roots are sorted by key.

**`TreeNode`** gets a unique row identity:

```ts
export interface TreeNode {
  /** Unique per row: the keys of the path from the root, joined with '/'. The annotation key in grid mode. */
  id: string
  key: string
  annotation?: Annotation
  missing: boolean
  matched: boolean
  depth: number
  children: TreeNode[]
}
```

The id is never parsed; the row always carries its `key`.

**Other exports:**

- `rootOrder` is replaced by `rootCode: Record<TreePerspective, AnnotationTypeCode>` (`sbt`, `rst`).
- `parentKey` stays for the breadcrumb, which shows one chain. Its comment says it is the primary chain of a perspective, not the tree rule anymore.
- `expandableKeys(roots)` becomes `expandableIds(roots)` and returns row ids.

### 2. Annotations page (`annotations.vue`)

- Grid rows get `id = key`.
- `expanded` holds row ids. `isExpanded(id)`, `toggle(id)` and "Expand all" (`expandableIds(tree)`) use row ids, so expanding an execution under its context does not expand its copy under the usage.
- The keyboard cursor becomes `activeId` (a row id):
  - `move()` steps from the active row by id, or from the first row of the open annotation when there is no active row.
  - Enter opens the key of the active row; the live region announces it.
  - `open(key, id?)` sets the cursor to the clicked row. When the detail opens another annotation (a link, the create dialog), the cursor moves to the first row of that key unless the active row already shows it.
  - When the detail closes, the active row is scrolled into view.

### 3. Grid (`AnnotationGrid.vue`, `grid/annotation-columns.ts`)

- `get-row-id` is `row.id`, so TanStack and the virtual rows have unique ids.
- The prop `activeKey` becomes `activeId`, and `scrollToKey` becomes `scrollToId`. The open annotation stays highlighted by key, so every copy of it is highlighted; the outline of the keyboard cursor is on one row.
- `open` emits the key and the row id.
- **Row selection** stays keyed by annotation key in the `rowSelection` model, so the bulk bar is unchanged. The grid maps it for UTable through a writable computed and two pure functions in `tree-projection.ts`:

  ```ts
  /** The selection by row id: every row whose key is selected, so all copies of a selected annotation. */
  export function rowSelectionOf(rows: TreeNode[], byKey: RowSelectionState): RowSelectionState

  /** The selection by key after a table change: rows turned on select their key, rows turned off clear it. */
  export function keySelectionOf(
    rows: TreeNode[],
    byKey: RowSelectionState,
    previous: RowSelectionState,
    next: RowSelectionState,
  ): RowSelectionState
  ```

  Keys of rows that are not in the data (collapsed or filtered out) are kept, like TanStack does for unknown ids. `RowSelectionState` is the plain `Record<string, boolean>` type of `@tanstack/vue-table`.
- The tree column passes `row.original.id` to `isExpanded` and `toggle`.

### 4. Mobile list (`AnnotationList.vue`)

Keys its virtual items by `row.id` (the same value as the key for grid rows), so it stays correct if it ever renders tree rows.

### 5. Tests

`test/unit/utils/annotations.test.ts`:

- By subject: only subjects at the root (including a missing one); an execution under its context and its usage, with its units of execution under both; no responsibilities or units.
- By responsibility: only responsibilities at the root; units with their units of execution; usages with executions and units of execution; no subjects or contexts.
- Missing ancestors are placeholders on every path.
- Row ids are unique and are the path of keys.
- With a filter, every path to a match is kept and the ancestors that do not match are `matched: false`; `expandableIds` lists the row ids with children.

The selection mapping (`rowSelectionOf`, `keySelectionOf`): selecting one copy selects the annotation key and shows every copy selected; clearing one copy clears the key; keys of rows that are not shown are kept. (A component test of `AnnotationGrid` would need a layout for the virtual rows, which happy-dom does not have.)

`test/e2e/annotations.spec.ts` (desktop): switch to Tree, filter one execution, and see it under its context and its usage.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`: the Tree mode shows only subjects or only responsibilities at the root, with every path to the descendants.
- The original spec stays as it is (historical); the review of this change records what was done.
