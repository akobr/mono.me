import { describe, expect, it } from 'vitest'
import { random } from '@/mock/annotations'
import { lineStats } from '@/utils/line-diff'

// The line counts of a document in the comparison of a selection across views (spec "Copy and Compare Annotations and
// Configurations Between Views", section 2).

const text = (...lines: (string | number)[]) => lines.join('\n')
const numbered = (count: number, prefix = '') => Array.from({ length: count }, (_, index) => `${prefix}${index}`)

/** The counts by the textbook table of the longest common subsequence: quadratic, and plainly right. */
function naiveStats(before: string, after: string) {
  const a = before === '' ? [] : before.split('\n')
  const b = after === '' ? [] : after.split('\n')
  const zeros = () => Array.from({ length: b.length + 1 }, () => 0)
  let previous = zeros()
  for (let i = 1; i <= a.length; i++) {
    const row = zeros()
    for (let j = 1; j <= b.length; j++) {
      row[j] = a[i - 1] === b[j - 1] ? previous[j - 1]! + 1 : Math.max(previous[j]!, row[j - 1]!)
    }
    previous = row
  }
  const common = previous[b.length]!
  return { additions: b.length - common, deletions: a.length - common }
}

// A search that runs to its cap takes about half a second here, and a test has five by default.
const long = { timeout: 30_000 }

describe('lineStats', () => {
  it('counts nothing for equal texts', () => {
    expect(lineStats('', '')).toEqual({ additions: 0, deletions: 0 })
    expect(lineStats('a', 'a')).toEqual({ additions: 0, deletions: 0 })
    expect(lineStats(text('{', '  "a": 1', '}'), text('{', '  "a": 1', '}'))).toEqual({ additions: 0, deletions: 0 })
  })

  it('counts every line of the other side when one side is empty: an empty text has no lines', () => {
    expect(lineStats('', text('a', 'b', 'c'))).toEqual({ additions: 3, deletions: 0 })
    expect(lineStats(text('a', 'b', 'c'), '')).toEqual({ additions: 0, deletions: 3 })
    expect(lineStats('', 'a')).toEqual({ additions: 1, deletions: 0 })
  })

  it('counts a changed line as one added and one removed', () => {
    const before = text('{', '  "retries": 2,', '  "timeout": 20', '}')
    const after = text('{', '  "retries": 3,', '  "timeout": 20', '}')

    expect(lineStats(before, after)).toEqual({ additions: 1, deletions: 1 })
  })

  it('counts added and removed lines at the start, in the middle and at the end', () => {
    expect(lineStats(text('b', 'c'), text('a', 'b', 'c'))).toEqual({ additions: 1, deletions: 0 })
    expect(lineStats(text('a', 'c'), text('a', 'b', 'c'))).toEqual({ additions: 1, deletions: 0 })
    expect(lineStats(text('a', 'b'), text('a', 'b', 'c'))).toEqual({ additions: 1, deletions: 0 })
    expect(lineStats(text('a', 'b', 'c'), text('b', 'c'))).toEqual({ additions: 0, deletions: 1 })
    expect(lineStats(text('a', 'b', 'c'), text('a', 'c'))).toEqual({ additions: 0, deletions: 1 })
    expect(lineStats(text('a', 'b', 'c'), text('a', 'b'))).toEqual({ additions: 0, deletions: 1 })
    expect(lineStats(text('a', 'b', 'c', 'd'), text('x', 'b', 'y', 'c', 'z'))).toEqual({ additions: 3, deletions: 2 })
  })

  it('counts a moved block once on each side', () => {
    // the first two lines move to the end
    expect(lineStats(text('a', 'b', 'c', 'd', 'e'), text('c', 'd', 'e', 'a', 'b'))).toEqual({
      additions: 2,
      deletions: 2,
    })
    // and the shorter part of two swapped blocks is the one that counts
    expect(lineStats(text('a', 'b', 'c', 'x', 'y'), text('x', 'y', 'a', 'b', 'c'))).toEqual({
      additions: 2,
      deletions: 2,
    })
  })

  it('tells repeated lines apart by their place', () => {
    expect(lineStats(text('a', 'a', 'a'), text('a', 'a'))).toEqual({ additions: 0, deletions: 1 })
    expect(lineStats(text('a', 'b', 'a'), text('b', 'a', 'b'))).toEqual({ additions: 1, deletions: 1 })
    expect(lineStats(text('}', '}', '}'), text('}', '{', '}', '}'))).toEqual({ additions: 1, deletions: 0 })
  })

  it('counts like the longest common subsequence on random texts', () => {
    const next = random(424309)
    const int = (below: number) => Math.floor(next() * below)
    // few different lines, so the texts share many of them in many orders
    const lines = (count: number, alphabet: number) => Array.from({ length: count }, () => `line ${int(alphabet)}`)

    /** A copy of the lines with some of them removed, replaced, added or moved, as an edit of a document does. */
    const edited = (source: string[], alphabet: number) => {
      const result = [...source]
      for (let edits = int(8); edits > 0; edits--) {
        const at = int(result.length + 1)
        switch (int(4)) {
          case 0:
            result.splice(at, int(4))
            break
          case 1:
            result.splice(at, 1, `line ${int(alphabet)}`)
            break
          case 2:
            result.splice(at, 0, ...lines(int(4), alphabet))
            break
          default:
            result.splice(int(result.length + 1), 0, ...result.splice(at, int(5)))
        }
      }
      return result
    }

    for (let round = 0; round < 400; round++) {
      const alphabet = 2 + int(12)
      const a = lines(int(60), alphabet)
      const b = round % 2 ? lines(int(60), alphabet) : edited(a, alphabet)
      const [before, after] = [a.join('\n'), b.join('\n')]
      const stats = lineStats(before, after)

      expect(stats, `${JSON.stringify(before)} into ${JSON.stringify(after)}`).toEqual(naiveStats(before, after))
      // the other way round, the additions are the deletions
      expect(lineStats(after, before)).toEqual({ additions: stats.deletions, deletions: stats.additions })
    }
  })

  it('counts exactly up to 2,000 changed lines, whatever their order', long, () => {
    // a reversed text keeps one line in place: 1,001 lines are 1,000 added and 1,000 removed
    const lines = numbered(1001)

    expect(lineStats(lines.join('\n'), lines.toReversed().join('\n'))).toEqual({ additions: 1000, deletions: 1000 })
  })

  it('counts the lines without their order above 2,000 changed lines', long, () => {
    // 1,002 reversed lines are 2,002 changes: only the lines a side has more often than the other are counted
    const lines = numbered(1002)
    expect(lineStats(lines.join('\n'), lines.toReversed().join('\n'))).toEqual({ additions: 0, deletions: 0 })

    // the shared lines are in another order, each side has 5,000 lines of its own, and one has "x" twice
    const before = [...numbered(5000, 'a'), ...numbered(3000, 'shared ')]
    const after = [...numbered(3000, 'shared ').toReversed(), ...numbered(5000, 'b'), 'x', 'x']
    expect(lineStats(before.join('\n'), after.join('\n'))).toEqual({ additions: 5002, deletions: 5000 })
  })

  it('answers two long texts that have nothing in common without searching for long', long, () => {
    const before = numbered(200_000, 'before ').join('\n')
    const after = numbered(150_000, 'after ').join('\n')

    // the search gives up at its budget; without it, it would take 350,000 rounds over as many diagonals
    expect(lineStats(before, after)).toEqual({ additions: 150_000, deletions: 200_000 })
  })

  it('counts without the order once a search would take more steps than its budget', () => {
    // a moved block: in its order, the line diff counts it on both sides; without its order, nothing changed
    const lines = numbered(200)
    const moved = [...lines.slice(100), ...lines.slice(0, 100)]
    const [before, after] = [lines.join('\n'), moved.join('\n')]

    expect(lineStats(before, after)).toEqual({ additions: 100, deletions: 100 })
    expect(lineStats(before, after, 1_000)).toEqual({ additions: 0, deletions: 0 })
  })

  it('skips the common start and end of long texts', () => {
    // one changed line in the middle of 100,000
    const lines = numbered(100_000)
    const changed = lines.with(50_000, 'changed')

    expect(lineStats(lines.join('\n'), changed.join('\n'))).toEqual({ additions: 1, deletions: 1 })
  })
})
