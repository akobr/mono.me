export interface LineStats {
  /** Lines only in the text after. */
  additions: number
  /** Lines only in the text before. */
  deletions: number
}

/**
 * Above this many changed lines the exact count costs too much (the search is quadratic in the changes), and the
 * lines are counted without their order. Two documents that far apart are rewritten, not edited.
 */
const maxEdits = 8_000

/** The lines of a text; an empty text has none. */
const linesOf = (text: string): string[] => (text === '' ? [] : text.split('\n'))

/**
 * The lines a line diff of two texts adds and removes, as the `+` and `−` of a file on GitHub. The comparison of a
 * selection across views counts them for every document, since the server counts only the effective configuration of
 * one key (spec "Copy and Compare Annotations and Configurations Between Views", section 2).
 */
export function lineStats(before: string, after: string): LineStats {
  const a = linesOf(before)
  const b = linesOf(after)

  let start = 0
  while (start < a.length && start < b.length && a[start] === b[start]) {
    start++
  }
  let endA = a.length
  let endB = b.length
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) {
    endA--
    endB--
  }

  const deleted = endA - start
  const added = endB - start
  if (deleted === 0 || added === 0) {
    return { additions: added, deletions: deleted }
  }

  const edits = shortestEdit(a, b, start, deleted, added)
  if (edits === undefined) {
    return unorderedStats(a.slice(start, endA), b.slice(start, endB))
  }
  // edits = additions + deletions, and the lengths differ by additions − deletions
  return { additions: (edits + added - deleted) / 2, deletions: (edits - added + deleted) / 2 }
}

/**
 * The length of the shortest edit script of a[offset, offset + n) into b[offset, offset + m): the greedy algorithm of
 * Myers ("An O(ND) Difference Algorithm and Its Variations"), which needs O(n + m) memory. Undefined above maxEdits.
 */
function shortestEdit(a: string[], b: string[], offset: number, n: number, m: number): number | undefined {
  const max = Math.min(n + m, maxEdits)
  // furthest[k + max + 1] is the furthest x reached on the diagonal k = x − y
  const furthest = new Int32Array(2 * max + 3)
  const at = (k: number) => k + max + 1

  for (let edits = 0; edits <= max; edits++) {
    for (let k = -edits; k <= edits; k += 2) {
      const down = k === -edits || (k !== edits && furthest[at(k - 1)]! < furthest[at(k + 1)]!)
      let x = down ? furthest[at(k + 1)]! : furthest[at(k - 1)]! + 1
      let y = x - k
      while (x < n && y < m && a[offset + x] === b[offset + y]) {
        x++
        y++
      }
      furthest[at(k)] = x
      if (x >= n && y >= m) {
        return edits
      }
    }
  }
  return undefined
}

/** The lines of each side that the other side does not have as often, whatever their order. */
function unorderedStats(a: string[], b: string[]): LineStats {
  const counts = new Map<string, number>()
  for (const line of a) {
    counts.set(line, (counts.get(line) ?? 0) + 1)
  }
  let additions = 0
  for (const line of b) {
    const left = counts.get(line) ?? 0
    if (left > 0) {
      counts.set(line, left - 1)
    } else {
      additions++
    }
  }
  let deletions = 0
  for (const left of counts.values()) {
    deletions += left
  }
  return { additions, deletions }
}
