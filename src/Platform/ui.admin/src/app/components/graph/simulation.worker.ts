/// <reference lib="webworker" />
import {
  forceCollide,
  forceLink,
  forceManyBody,
  forceRadial,
  forceSimulation,
  forceX,
  forceY,
  type Simulation,
  type SimulationNodeDatum,
} from 'd3-force'
import type { GraphLayout } from './graph-model'
import { seedLayout } from './layout-seed'
import type { SimulationCommand, SimulationFrame, SimulationInit } from './simulation-protocol'

// The force simulation runs here, off the main thread, because a full view has 5,000 to 20,000 nodes
// (spec section 12). Positions go back as a transferable Float32Array once per frame.

interface Node extends SimulationNodeDatum {
  tier: number
  radius: number
}

const scope = self as unknown as DedicatedWorkerGlobalScope
/** Above this many nodes the forces are cheaper and the simulation cools faster. */
const largeGraph = 5_000
/** The pre-warm stops after this long even if it has ticks left; the rest is streamed. */
const prewarmBudgetMs = 1_500

let simulation: Simulation<Node, undefined> | undefined
let nodes: Node[] = []
let tierY: number[] = []
let tierRadius: number[] = []
let large = false
let running = false
let reducedMotion = false
let generation = 0

function applyLayout(layout: GraphLayout) {
  if (!simulation) {
    return
  }
  if (layout === 'layered') {
    simulation.force('x', forceX<Node>(0).strength(0.02))
    // Large graphs keep their tiers as bands: a strong pull would squeeze thousands of nodes onto one line.
    simulation.force('y', forceY<Node>((node) => tierY[node.tier] ?? 0).strength(large ? 0.2 : 0.3))
    simulation.force('radial', null)
  } else if (layout === 'radial') {
    simulation.force('x', null)
    simulation.force('y', null)
    simulation.force('radial', forceRadial<Node>((node) => tierRadius[node.tier] ?? 0).strength(large ? 0.1 : 0.4))
  } else {
    simulation.force('x', forceX<Node>(0).strength(0.03))
    simulation.force('y', forceY<Node>(0).strength(0.03))
    simulation.force('radial', null)
  }
}

function post() {
  const positions = new Float32Array(nodes.length * 2)
  nodes.forEach((node, index) => {
    positions[index * 2] = node.x ?? 0
    positions[index * 2 + 1] = node.y ?? 0
  })
  const frame: SimulationFrame = { type: 'positions', generation, positions, alpha: simulation?.alpha() ?? 0 }
  scope.postMessage(frame, [positions.buffer])
}

/**
 * Ticks while the simulation is hot, one tick per frame (requestAnimationFrame exists in dedicated workers).
 * With reduced motion the layout is not animated: it settles in 12 ms slices and is posted once, except while
 * a node is dragged.
 */
let loopId = 0

function loop(id: number) {
  if (!simulation || !running || id !== loopId) {
    return
  }
  const quiet = reducedMotion && simulation.alphaTarget() === 0
  if (quiet) {
    const until = performance.now() + 12
    while (simulation.alpha() >= simulation.alphaMin() && performance.now() < until) {
      simulation.tick()
    }
  } else {
    simulation.tick()
  }
  const settled = simulation.alpha() < simulation.alphaMin()
  if (!quiet || settled) {
    post()
  }
  if (settled) {
    running = false
    return
  }
  const next = () => loop(id)
  const schedule = (scope as unknown as { requestAnimationFrame?: (callback: () => void) => number })
    .requestAnimationFrame
  if (schedule) {
    schedule.call(scope, next)
  } else {
    setTimeout(next, 16)
  }
}

function start() {
  if (!running) {
    running = true
    loop(++loopId)
  }
}

/**
 * Positions to start from: the previous place of known nodes (a changed focus keeps its shape), next to a known
 * neighbour for new nodes, and the tier bands of the seed layout otherwise.
 */
function startPositions(message: SimulationInit, seed: Float32Array): { positions: Float32Array; known: number } {
  const count = message.tiers.length
  const positions = Float32Array.from(seed)
  const previous = message.seed
  if (!previous) {
    return { positions, known: 0 }
  }

  const has = (index: number) => Number.isFinite(previous[index * 2] ?? Number.NaN)
  let known = 0
  for (let index = 0; index < count; index++) {
    if (has(index)) {
      positions[index * 2] = previous[index * 2]!
      positions[index * 2 + 1] = previous[index * 2 + 1]!
      known++
    }
  }
  if (known === 0 || known === count) {
    return { positions, known }
  }

  const neighbours: number[][] = Array.from({ length: count }, () => [])
  for (let index = 0; index < message.links.length; index += 2) {
    neighbours[message.links[index]!]!.push(message.links[index + 1]!)
    neighbours[message.links[index + 1]!]!.push(message.links[index]!)
  }
  for (let index = 0; index < count; index++) {
    const anchor = has(index) ? undefined : neighbours[index]!.find(has)
    if (anchor !== undefined) {
      positions[index * 2] = previous[anchor * 2]! + (Math.random() - 0.5) * 40
      positions[index * 2 + 1] = previous[anchor * 2 + 1]! + (Math.random() - 0.5) * 40
    }
  }
  return { positions, known }
}

/** Handles one command of the graph component (exported for the tests). */
export function handleCommand(message: SimulationCommand): void {
  switch (message.type) {
    case 'init': {
      simulation?.stop()
      generation = message.generation
      reducedMotion = message.reducedMotion
      large = message.tiers.length > largeGraph

      const seed = seedLayout(message.tiers, message.links, message.layout)
      tierY = seed.tierY
      tierRadius = seed.tierRadius
      const { positions, known } = startPositions(message, seed.positions)
      nodes = Array.from(message.tiers, (tier, index) => ({
        tier,
        radius: message.radii[index]!,
        x: positions[index * 2]!,
        y: positions[index * 2 + 1]!,
      }))

      const links = []
      for (let index = 0; index < message.links.length; index += 2) {
        links.push({ source: message.links[index]!, target: message.links[index + 1]! })
      }
      simulation = forceSimulation<Node>(nodes)
        .stop()
        .alphaDecay(large ? 0.05 : 0.0228)
        .force('link', forceLink<Node, { source: number; target: number }>(links).distance(30).strength(0.4))
        .force(
          'charge',
          forceManyBody<Node>()
            .strength(-40)
            .distanceMax(400)
            .theta(large ? 1.2 : 0.9),
        )
        .force('collide', forceCollide<Node>((node) => node.radius + 2).strength(large ? 0.6 : 1))
      applyLayout(message.layout)
      // The seed is already close to the final layout; a graph that mostly keeps its nodes only settles gently.
      simulation.alpha(known > nodes.length / 2 ? 0.3 : 0.6)

      // A remembered layout continues where it was left, with its dragged nodes pinned.
      const resume = message.resume && known === nodes.length ? message.resume : undefined
      if (resume) {
        simulation.alpha(resume.alpha)
        for (const index of resume.pinned) {
          const node = nodes[index]
          if (node) {
            node.fx = node.x
            node.fy = node.y
          }
        }
      }

      const deadline = performance.now() + prewarmBudgetMs
      for (let tick = 0; tick < message.prewarm && performance.now() < deadline; tick++) {
        simulation.tick()
      }
      post()
      running = false
      // A remembered layout that had settled stays exactly as it was until the user drags or reheats it.
      if (!resume || simulation.alpha() >= simulation.alphaMin()) {
        start()
      }
      break
    }
    case 'layout': {
      applyLayout(message.layout)
      simulation?.alpha(0.8)
      start()
      break
    }
    case 'pin': {
      const node = nodes[message.index]
      if (node) {
        node.fx = node.x = message.x
        node.fy = node.y = message.y
        simulation?.alphaTarget(0.1)
        start()
      }
      break
    }
    case 'dragEnd':
      // The dragged node stays pinned (fx/fy) until "release all"; the simulation cools down again.
      simulation?.alphaTarget(0)
      break
    case 'unpinAll':
      nodes.forEach((node) => {
        node.fx = null
        node.fy = null
      })
      simulation?.alpha(0.5)
      start()
      break
    case 'reheat':
      simulation?.alpha(1)
      start()
      break
    case 'stop':
      running = false
      simulation?.stop()
      break
  }
}

scope.addEventListener('message', (event: MessageEvent<SimulationCommand>) => handleCommand(event.data))
