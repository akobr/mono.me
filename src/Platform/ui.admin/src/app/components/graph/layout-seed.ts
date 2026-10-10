import type { GraphLayout } from './graph-model'

// Initial positions for the simulation worker. d3-force needs about 100 ms per tick at 16,000 nodes, so a large
// graph cannot be untangled from random positions before the first frame. Instead every tier is laid out as a
// band, ordered by the position of the node's ancestors (the barycenter heuristic of layered graph drawing); the
// simulation then only relaxes it.

export interface LayoutSeed {
  /** Interleaved x/y per node. */
  positions: Float32Array
  /** Target y of each tier (layered layout). */
  tierY: number[]
  /** Target radius of each tier (radial layout). */
  tierRadius: number[]
}

const tierCount = 4
const spacing = 14
const bandGap = 120

/**
 * Lays the nodes out in tier bands. `links` holds interleaved ancestor/descendant indexes; ancestors always sit in
 * a lower tier. Within a tier the nodes are ordered by the mean rank of their ancestors and spread over the width,
 * in as many rows as needed.
 */
export function seedLayout(tiers: ArrayLike<number>, links: ArrayLike<number>, layout: GraphLayout): LayoutSeed {
  const count = tiers.length
  const positions = new Float32Array(count * 2)
  const ancestors: number[][] = Array.from({ length: count }, () => [])
  for (let index = 0; index < links.length; index += 2) {
    ancestors[links[index + 1]!]!.push(links[index]!)
  }

  const members: number[][] = Array.from({ length: tierCount }, () => [])
  for (let index = 0; index < count; index++) {
    members[Math.min(Math.max(tiers[index]!, 0), tierCount - 1)]!.push(index)
  }

  const width = Math.max(800, Math.sqrt(count) * 60)
  const perRow = Math.max(1, Math.floor(width / spacing))
  // Rank of each placed node within its tier, normalised to 0..1.
  const rank = new Float64Array(count).fill(Number.NaN)
  const bands = members.map((list) => Math.ceil(list.length / perRow) * spacing)

  const tierY: number[] = []
  let y = 0
  for (let tier = 0; tier < tierCount; tier++) {
    const band = bands[tier]!
    y += tier === 0 ? band / 2 : bands[tier - 1]! / 2 + bandGap + band / 2
    tierY.push(y)
  }
  const middle = (tierY[0]! + tierY[tierCount - 1]!) / 2
  for (let tier = 0; tier < tierCount; tier++) {
    tierY[tier] = tierY[tier]! - middle
  }

  const ringGap = Math.max(140, Math.max(...bands) + bandGap)
  const tierRadius = Array.from({ length: tierCount }, (_, tier) => 60 + tier * ringGap)

  members.forEach((list, tier) => {
    const keyed = list.map((index, order) => {
      const placed = ancestors[index]!.map((ancestor) => rank[ancestor]!).filter((value) => !Number.isNaN(value))
      const key = placed.length ? placed.reduce((sum, value) => sum + value, 0) / placed.length : order / list.length
      return { index, key }
    })
    keyed.sort((a, b) => a.key - b.key)

    const rows = Math.max(1, Math.ceil(list.length / perRow))
    keyed.forEach(({ index }, position) => {
      const share = list.length > 1 ? position / (list.length - 1) : 0.5
      rank[index] = share
      // Consecutive nodes alternate between the rows, so the order follows x across the whole band.
      const offset = ((position % rows) - (rows - 1) / 2) * spacing
      if (layout === 'radial') {
        const angle = share * Math.PI * 2
        const radius = tierRadius[tier]! + offset
        positions[index * 2] = Math.cos(angle) * radius
        positions[index * 2 + 1] = Math.sin(angle) * radius
      } else {
        positions[index * 2] = (share - 0.5) * width * Math.min(1, (list.length * spacing) / rows / width + 0.2)
        positions[index * 2 + 1] = tierY[tier]! + offset
      }
    })
  })

  return { positions, tierY, tierRadius }
}
