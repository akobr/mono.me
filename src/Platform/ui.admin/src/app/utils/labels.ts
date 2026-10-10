const palette = ['primary', 'info', 'success', 'warning', 'error', 'secondary', 'neutral'] as const

export type LabelColor = (typeof palette)[number]

/** A stable chip color per label (hash of the text), so a label looks the same everywhere. */
export function labelColor(label: string): LabelColor {
  let hash = 0
  for (const char of label) {
    hash = (hash * 31 + char.charCodeAt(0)) >>> 0
  }
  return palette[hash % palette.length]!
}

/** Trims, drops empty values and duplicates (case-insensitive, first spelling wins). */
export function normalizeLabels(labels: readonly string[]): string[] {
  const seen = new Set<string>()
  const result: string[] = []
  for (const label of labels) {
    const trimmed = label.trim()
    if (trimmed && !seen.has(trimmed.toLowerCase())) {
      seen.add(trimmed.toLowerCase())
      result.push(trimmed)
    }
  }
  return result
}
