import type { Annotation } from '@42for.net/2splatform.sdk'
import { codeOfKey, getDirectAncestors, typeCodes, type AnnotationTypeCode } from '@/utils/annotation-key'

export type GraphLayout = 'layered' | 'force' | 'radial'

/** Tier of a type in the layered and radial layouts (spec section 12). */
export const tierOf: Record<AnnotationTypeCode, number> = { rst: 0, sbt: 0, unt: 1, usg: 1, cnt: 1, exe: 2, uxe: 3 }

export interface GraphNode {
  key: string
  code: AnnotationTypeCode
  tier: number
  radius: number
  degree: number
}

export interface GraphData {
  nodes: GraphNode[]
  /** Pairs of node indexes, ancestor → descendant (the direction configuration flows). */
  links: [number, number][]
  index: Map<string, number>
  /** Undirected neighbours per node index. */
  neighbours: number[][]
}

/** Number of (transitive) descendants per key in the catalog. */
export function countDescendants(
  keys: Iterable<string>,
  ancestorsOf: (key: string) => readonly string[],
): Map<string, number> {
  const counts = new Map<string, number>()
  for (const key of keys) {
    for (const ancestor of ancestorsOf(key)) {
      counts.set(ancestor, (counts.get(ancestor) ?? 0) + 1)
    }
  }
  return counts
}

/**
 * Builds the graph of the visible annotations: nodes sized by their descendant count (4 + 2·log2(1 + n)), links
 * from the direct ancestor edges between visible nodes.
 */
export function buildGraph(keys: Iterable<string>, descendants: ReadonlyMap<string, number>): GraphData {
  const nodes: GraphNode[] = []
  const index = new Map<string, number>()

  for (const key of keys) {
    const code = codeOfKey(key)
    if (!code || index.has(key)) {
      continue
    }
    index.set(key, nodes.length)
    const count = descendants.get(key) ?? 0
    nodes.push({ key, code, tier: tierOf[code], radius: 4 + 2 * Math.log2(1 + count), degree: 0 })
  }

  const links: [number, number][] = []
  const neighbours: number[][] = nodes.map(() => [])
  nodes.forEach((node, target) => {
    for (const ancestor of getDirectAncestors(node.key)) {
      const source = index.get(ancestor)
      if (source !== undefined) {
        links.push([source, target])
        neighbours[source]!.push(target)
        neighbours[target]!.push(source)
        nodes[source]!.degree++
        node.degree++
      }
    }
  })

  return { nodes, links, index, neighbours }
}

/**
 * The k-hop neighbourhood of a key over ancestor and descendant edges (focus mode, spec section 12), computed on
 * the full catalog structure so the visible set can be chosen before the graph is built.
 */
export function neighbourhood(
  start: string,
  depth: number,
  childrenByKey: ReadonlyMap<string, readonly string[]>,
  exists: (key: string) => boolean,
): Set<string> {
  const visited = new Set<string>([start])
  let frontier = [start]
  for (let level = 0; level < depth && frontier.length; level++) {
    const next: string[] = []
    for (const key of frontier) {
      for (const neighbour of [...getDirectAncestors(key), ...(childrenByKey.get(key) ?? [])]) {
        if (!visited.has(neighbour) && exists(neighbour)) {
          visited.add(neighbour)
          next.push(neighbour)
        }
      }
    }
    frontier = next
  }
  return visited
}

/** The most connected subject or responsibility: where focus mode starts without a selection. */
export function mostConnectedRoot(
  annotations: Iterable<Annotation>,
  childrenByKey: ReadonlyMap<string, readonly string[]>,
): string | undefined {
  let best: string | undefined
  let bestCount = -1
  for (const annotation of annotations) {
    const code = codeOfKey(annotation.AnnotationKey)
    if (code === 'sbt' || code === 'rst') {
      const count = childrenByKey.get(annotation.AnnotationKey)?.length ?? 0
      if (count > bestCount) {
        best = annotation.AnnotationKey
        bestCount = count
      }
    }
  }
  return best
}

/** Indexes of the `count` nodes with the highest degree (their names are always drawn). */
export function topByDegree(nodes: readonly GraphNode[], count: number): Set<number> {
  return new Set(
    nodes
      .map((node, index) => [index, node.degree] as const)
      .toSorted((a, b) => b[1] - a[1])
      .slice(0, count)
      .map(([index]) => index),
  )
}

/** True when both lists hold the same keys in the same order (a rebuild of the graph is not needed). */
export function sameKeys(a: readonly string[], b: readonly string[]): boolean {
  return a.length === b.length && a.every((key, index) => key === b[index])
}

/**
 * The neighbour of a node that lies best in the direction of an arrow key (within ±60°, the nearest wins), for
 * keyboard navigation of the graph.
 */
export function neighbourInDirection(
  graph: GraphData,
  positions: Float32Array,
  index: number,
  direction: { x: number; y: number },
): number | undefined {
  const fromX = positions[index * 2] ?? 0
  const fromY = positions[index * 2 + 1] ?? 0
  let best: number | undefined
  let bestScore = Infinity
  for (const neighbour of graph.neighbours[index] ?? []) {
    const dx = (positions[neighbour * 2] ?? 0) - fromX
    const dy = (positions[neighbour * 2 + 1] ?? 0) - fromY
    const distance = Math.hypot(dx, dy) || 1
    const cosine = (dx * direction.x + dy * direction.y) / distance
    if (cosine >= 0.5) {
      // Prefer nodes straight ahead; distance breaks ties.
      const score = distance * (2 - cosine)
      if (score < bestScore) {
        best = neighbour
        bestScore = score
      }
    }
  }
  return best
}

export const allTypeCodes = typeCodes
