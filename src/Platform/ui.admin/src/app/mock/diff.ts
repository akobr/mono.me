import type { DiffHunk, DiffLine, DiffResult } from '@42for.net/2splatform.sdk'

// A small line diff for the mock API with the server's hunk rules: 3 lines of context, and hunks separated by
// 6 unchanged lines or fewer are merged (JSON hunk-based diff model).

type Edit = { type: DiffLine['Type']; text: string; old?: number; new?: number }

function lineDiff(before: string[], after: string[]): Edit[] {
  const rows = before.length
  const columns = after.length
  const lcs = Array.from({ length: rows + 1 }, () => Array.from<number>({ length: columns + 1 }).fill(0))

  for (let i = rows - 1; i >= 0; i--) {
    for (let j = columns - 1; j >= 0; j--) {
      lcs[i]![j] = before[i] === after[j] ? lcs[i + 1]![j + 1]! + 1 : Math.max(lcs[i + 1]![j]!, lcs[i]![j + 1]!)
    }
  }

  const edits: Edit[] = []
  let i = 0
  let j = 0
  while (i < rows || j < columns) {
    if (i < rows && j < columns && before[i] === after[j]) {
      edits.push({ type: 'Unchanged', text: before[i]!, old: i + 1, new: j + 1 })
      i++
      j++
    } else if (j < columns && (i >= rows || lcs[i]![j + 1]! >= lcs[i + 1]![j]!)) {
      edits.push({ type: 'Addition', text: after[j]!, new: j + 1 })
      j++
    } else {
      edits.push({ type: 'Deletion', text: before[i]!, old: i + 1 })
      i++
    }
  }
  return edits
}

export function diffDocuments(before: unknown, after: unknown): DiffResult {
  const edits = lineDiff(
    JSON.stringify(before ?? {}, null, 2).split('\n'),
    JSON.stringify(after ?? {}, null, 2).split('\n'),
  )
  const changed = edits.map((edit, index) => (edit.type === 'Unchanged' ? -1 : index)).filter((index) => index >= 0)

  const ranges: [number, number][] = []
  for (const index of changed) {
    const start = Math.max(index - 3, 0)
    const end = Math.min(index + 3, edits.length - 1)
    const last = ranges.at(-1)
    if (last && start - last[1] <= 7) {
      last[1] = Math.max(last[1], end)
    } else {
      ranges.push([start, end])
    }
  }

  const hunks: DiffHunk[] = ranges.map(([start, end]) => {
    const lines = edits.slice(start, end + 1)
    const oldLines = lines.filter((line) => line.type !== 'Addition')
    const newLines = lines.filter((line) => line.type !== 'Deletion')
    return {
      OldStart: oldLines[0]?.old ?? 0,
      OldCount: oldLines.length,
      NewStart: newLines[0]?.new ?? 0,
      NewCount: newLines.length,
      Lines: lines.map((line) => ({
        Type: line.type,
        Content: line.text,
        OldLineNumber: line.old ?? null,
        NewLineNumber: line.new ?? null,
      })),
    }
  })

  return {
    Stats: {
      Additions: edits.filter((edit) => edit.type === 'Addition').length,
      Deletions: edits.filter((edit) => edit.type === 'Deletion').length,
      Unchanged: edits.filter((edit) => edit.type === 'Unchanged').length,
    },
    Hunks: hunks,
  }
}
