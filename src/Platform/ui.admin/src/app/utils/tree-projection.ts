import type { Annotation } from '@42for.net/2splatform.sdk'
import { parseAnnotationKey, type AnnotationTypeCode, type ParsedAnnotationKey } from './annotation-key'

export type TreePerspective = 'subject' | 'responsibility'

export interface TreeNode {
  key: string
  /** Undefined for a synthetic "(missing)" node of a parent that is not in the catalog. */
  annotation?: Annotation
  missing: boolean
  /** False for an ancestor kept only because a descendant matches the filter (rendered dimmed). */
  matched: boolean
  depth: number
  children: TreeNode[]
}

const rootOrder: Record<TreePerspective, AnnotationTypeCode[]> = {
  subject: ['sbt', 'rst'],
  responsibility: ['rst', 'sbt'],
}

/**
 * The parent of a key in a perspective (spec section 7). In each perspective every annotation has at most one
 * parent, so the DAG becomes a tree:
 * - by subject:        cnt → sbt, usg → sbt, exe → cnt, uxe → exe, unt → rst
 * - by responsibility: unt → rst, usg → rst, exe → usg, uxe → exe, cnt → sbt
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

const compareNodes = (a: TreeNode, b: TreeNode) => a.key.localeCompare(b.key)

/**
 * Projects the annotations into a tree. A parent missing from the catalog becomes a synthetic "(missing)" node
 * (attached to its own parent the same way), so data problems stay visible. With `matches`, only matching
 * annotations and their ancestor chains are kept; kept ancestors that do not match are marked `matched: false`.
 */
export function projectTree(
  annotations: Iterable<Annotation>,
  perspective: TreePerspective,
  matches?: (annotation: Annotation) => boolean,
): TreeNode[] {
  const nodes = new Map<string, TreeNode>()
  const parsedByKey = new Map<string, ParsedAnnotationKey>()
  const visible = new Set<string>()

  const node = (key: string, annotation?: Annotation): TreeNode => {
    let entry = nodes.get(key)
    if (!entry) {
      entry = { key, annotation, missing: !annotation, matched: false, depth: 0, children: [] }
      nodes.set(key, entry)
    } else if (annotation) {
      entry.annotation = annotation
      entry.missing = false
    }
    return entry
  }

  for (const annotation of annotations) {
    const parsed = parseAnnotationKey(annotation.AnnotationKey)
    if (parsed) {
      parsedByKey.set(annotation.AnnotationKey, parsed)
      node(annotation.AnnotationKey, annotation).matched = !matches || matches(annotation)
    }
  }

  // Link every node (and every synthetic parent) to its parent.
  const linked = new Set<string>()
  const roots: TreeNode[] = []
  const link = (entry: TreeNode) => {
    if (linked.has(entry.key)) {
      return
    }
    linked.add(entry.key)

    const parsed = parsedByKey.get(entry.key) ?? parseAnnotationKey(entry.key)
    const parent = parsed ? parentKey(parsed, perspective) : undefined

    if (!parent) {
      roots.push(entry)
      return
    }

    const parentNode = node(parent)
    parentNode.children.push(entry)
    link(parentNode)
  }

  // Map iteration also visits the synthetic parents added while linking.
  for (const entry of nodes.values()) {
    link(entry)
  }

  // Keep matching nodes and their ancestor chains.
  const keep = (entry: TreeNode): boolean => {
    const childrenKept = entry.children.filter(keep)
    entry.children = childrenKept.toSorted(compareNodes)
    const kept = entry.matched || childrenKept.length > 0 || (!matches && entry.missing)
    if (kept) {
      visible.add(entry.key)
    }
    return kept
  }

  const setDepth = (entry: TreeNode, depth: number) => {
    entry.depth = depth
    entry.children.forEach((child) => setDepth(child, depth + 1))
  }

  const order = rootOrder[perspective]
  return roots
    .filter(keep)
    .toSorted((a, b) => {
      const byType =
        order.indexOf(a.key.slice(0, 3) as AnnotationTypeCode) - order.indexOf(b.key.slice(0, 3) as AnnotationTypeCode)
      return byType !== 0 ? byType : compareNodes(a, b)
    })
    .map((root) => (setDepth(root, 0), root))
}

/** Keys of every node that has children (for "expand all"). */
export function expandableKeys(roots: TreeNode[]): string[] {
  const keys: string[] = []
  const walk = (entry: TreeNode) => {
    if (entry.children.length > 0) {
      keys.push(entry.key)
      entry.children.forEach(walk)
    }
  }
  roots.forEach(walk)
  return keys
}
