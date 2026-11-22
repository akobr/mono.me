import type { RowSelectionState } from '@tanstack/vue-table'

// A selection that is kept by key while the table shows only some of the keys (a filter): the table gets the selected
// keys of its rows, and its changes are merged back (spec "Copy and Compare Annotations and Configurations Between
// Views", section 8). The annotation grid does the same through its row ids (rowSelectionOf, keySelectionOf).

/**
 * The selection the table gets: only the selected keys among the shown ones. The table counts every key of its
 * selection for the header checkbox, so a key that the filter hides, or one that is false, must not be in it.
 */
export function shownSelection(selection: RowSelectionState, shown: readonly string[]): RowSelectionState {
  const result: RowSelectionState = {}
  for (const key of shown) {
    if (selection[key]) {
      result[key] = true
    }
  }
  return result
}

/**
 * The selection after the table changed the one of its rows to `next`: the shown keys take their new state, and the
 * keys that the filter hides keep theirs.
 */
export function mergeShownSelection(
  selection: RowSelectionState,
  shown: readonly string[],
  next: RowSelectionState,
): RowSelectionState {
  const visible = new Set(shown)
  const result: RowSelectionState = {}
  for (const key of Object.keys(selection)) {
    if (selection[key] && !visible.has(key)) {
      result[key] = true
    }
  }
  for (const key of shown) {
    if (next[key]) {
      result[key] = true
    }
  }
  return result
}

/** The selected keys, in the order of the selection. */
export const selectedKeysOf = (selection: RowSelectionState): string[] =>
  Object.keys(selection).filter((key) => selection[key])
