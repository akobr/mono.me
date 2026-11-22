import type { JsonPatchOperation } from '@42for.net/2splatform.sdk'

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

/** Labels are the same label whatever their case and surrounding spaces, as the UI keeps them unique. */
export const sameLabel = (first: string, second: string) => first.trim().toLowerCase() === second.trim().toLowerCase()

/** The labels as a PATCH sees them (the API's editable document): its strings, in order; a stored null is dropped. */
export const patchableLabels = (labels: readonly unknown[] | null | undefined): string[] =>
  (labels ?? []).filter((label): label is string => typeof label === 'string')

/**
 * The JSON Patches (RFC 6902) of the label actions start with a test of the labels the UI read: when they changed
 * meanwhile, the patch fails with 412 PatchTestFailed and writes nothing, so a label is never added twice in another
 * spelling (the API dedupes ordinally only) or removed at a moved index. `seen` are the patchable labels the UI read.
 */
const guard = (seen: readonly string[]): JsonPatchOperation => ({ op: 'test', path: '/Labels', value: [...seen] })

/** The patch that appends `label`, or undefined when the labels have it in any spelling. */
export function labelAddition(seen: readonly string[], label: string): JsonPatchOperation[] | undefined {
  return seen.some((existing) => sameLabel(existing, label))
    ? undefined
    : [guard(seen), { op: 'add', path: '/Labels/-', value: label.trim() }]
}

/**
 * The patch that removes every spelling of `label`, highest index first so the lower indexes stay valid, or undefined
 * when the labels do not have it.
 */
export function labelRemoval(seen: readonly string[], label: string): JsonPatchOperation[] | undefined {
  const removals = seen
    .map((existing, index) => ({ existing, index }))
    .filter(({ existing }) => sameLabel(existing, label))
    .toReversed()
    .map(({ index }): JsonPatchOperation => ({ op: 'remove', path: `/Labels/${index}` }))
  return removals.length ? [guard(seen), ...removals] : undefined
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
