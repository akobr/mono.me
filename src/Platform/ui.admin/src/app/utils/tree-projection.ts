import type { Annotation } from '@42for.net/2splatform.sdk'
import type { RowSelectionState } from '@tanstack/vue-table'
import {
  codeOfKey,
  getDirectAncestors,
  parseAnnotationKey,
  type AnnotationTypeCode,
  type ParsedAnnotationKey,
} from './annotation-key'

export type TreePerspective = 'subject' | 'responsibility'

export interface TreeNode {
  /** Unique per row: the keys of the path from the root, joined with '/'. The annotation key in grid mode. */
  id: string
  key: string
  /** Undefined for a synthetic "(missing)" node of a parent that is not in the catalog. */
  annotation?: Annotation
  missing: boolean
  /** False for an ancestor kept only because a descendant matches the filter (rendered dimmed). */
  matched: boolean
  depth: number
  children: TreeNode[]
}

/** The only type at the root of a perspective; the types that do not descend from it are not shown. */
export const rootCode: Record<TreePerspective, AnnotationTypeCode> = {
  subject: 'sbt',
  responsibility: 'rst',
}

/**
 * The primary parent of a key in a perspective, for the breadcrumb, which shows one chain (spec section 11.1):
 * - by subject:        cnt → sbt, usg → sbt, exe → cnt, uxe → exe, unt → rst
 * - by responsibility: unt → rst, usg → rst, exe → usg, uxe → exe, cnt → sbt
 * The tree itself shows every path (projectTree).
 */
export function parentKey(parsed: ParsedAnnotationKey, perspective: TreePerspective): string | undefined {
  const { subject, responsibility, context } = parsed

  switch (parsed.code) {
    case 'rst':
    case 'sbt':
      return undefined
    case 'unt':
      return `rst.${responsibility}`
    case 'cnt':
      return `sbt.${subject}`
    case 'usg':
      return perspective === 'subject' ? `sbt.${subject}` : `rst.${responsibility}`
    case 'exe':
      return perspective === 'subject' ? `cnt.${subject}.${context}` : `usg.${subject}.${responsibility}`
    case 'uxe':
      return `exe.${subject}.${responsibility}.${context}`
  }
}

const compareKeys = (a: string, b: string) => a.localeCompare(b)

/**
 * Projects the inheritance graph into a tree with only subjects or only responsibilities at the root, following
 * every path down (spec 2026-10-13): by subject, an execution appears under its context and its usage; by
 * responsibility, a unit of execution appears under its unit and its execution. A parent missing from the catalog
 * becomes a synthetic "(missing)" node on every path, so data problems stay visible. With `matches`, only matching
 * annotations and their ancestor paths are kept; kept ancestors that do not match are marked `matched: false`.
 */
export function projectTree(
  annotations: Iterable<Annotation>,
  perspective: TreePerspective,
  matches?: (annotation: Annotation) => boolean,
): TreeNode[] {
  const byKey = new Map<string, Annotation>()
  const matched = new Set<string>()
  const children = new Map<string, Set<string>>()

  for (const annotation of annotations) {
    const key = annotation.AnnotationKey
    if (parseAnnotationKey(key)) {
      byKey.set(key, annotation)
      if (!matches || matches(annotation)) {
        matched.add(key)
      }
    }
  }

  // Link every annotation, and every missing ancestor met on the way, to all of its direct ancestors.
  const linked = new Set<string>()
  const link = (key: string) => {
    if (linked.has(key)) {
      return
    }
    linked.add(key)

    for (const parent of getDirectAncestors(key)) {
      let entry = children.get(parent)
      if (!entry) {
        entry = new Set()
        children.set(parent, entry)
      }
      entry.add(key)
      link(parent)
    }
  }
  byKey.forEach((_, key) => link(key))

  // A node is kept when it matches or a child is kept. Memoized per key: a node can be reached on several paths.
  const keptByKey = new Map<string, boolean>()
  const kept = (key: string): boolean => {
    let result = keptByKey.get(key)
    if (result === undefined) {
      result = matched.has(key)
      for (const child of children.get(key) ?? []) {
        result = kept(child) || result
      }
      keptByKey.set(key, result)
    }
    return result
  }

  const keptChildren = (key: string) => [...(children.get(key) ?? [])].filter(kept).toSorted(compareKeys)

  const build = (key: string, parentId: string | undefined, depth: number): TreeNode => {
    const annotation = byKey.get(key)
    const id = parentId === undefined ? key : `${parentId}/${key}`
    return {
      id,
      key,
      annotation,
      missing: !annotation,
      matched: matched.has(key),
      depth,
      children: keptChildren(key).map((child) => build(child, id, depth + 1)),
    }
  }

  const root = rootCode[perspective]
  return [...linked]
    .filter((key) => codeOfKey(key) === root && kept(key))
    .toSorted(compareKeys)
    .map((key) => build(key, undefined, 0))
}

/** Row ids of every node that has children (for "expand all"). */
export function expandableIds(roots: TreeNode[]): string[] {
  const ids: string[] = []
  const walk = (entry: TreeNode) => {
    if (entry.children.length > 0) {
      ids.push(entry.id)
      entry.children.forEach(walk)
    }
  }
  roots.forEach(walk)
  return ids
}

/** The selection by row id: every row whose key is selected, so all copies of a selected annotation. */
export function rowSelectionOf(rows: TreeNode[], byKey: RowSelectionState): RowSelectionState {
  const result: RowSelectionState = {}
  for (const row of rows) {
    if (byKey[row.key]) {
      result[row.id] = true
    }
  }
  return result
}

/**
 * The selection by key after a table change: rows turned on select their key, rows turned off clear it. Keys of
 * rows that are not in `rows` (collapsed or filtered out) are kept, like TanStack keeps unknown row ids.
 */
export function keySelectionOf(
  rows: TreeNode[],
  byKey: RowSelectionState,
  previous: RowSelectionState,
  next: RowSelectionState,
): RowSelectionState {
  const result = { ...byKey }
  for (const row of rows) {
    const selected = !!next[row.id]
    if (selected === !!previous[row.id]) {
      continue
    }
    if (selected) {
      result[row.key] = true
    } else {
      delete result[row.key]
    }
  }
  return result
}
