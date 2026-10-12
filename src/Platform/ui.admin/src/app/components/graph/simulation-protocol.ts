import type { GraphLayout } from './graph-model'

// Messages between the graph canvas and the simulation worker.

export interface SimulationInit {
  type: 'init'
  /** Identifies the graph; frames of an earlier graph are ignored. */
  generation: number
  /** Per node: tier and radius. */
  tiers: Float32Array
  radii: Float32Array
  /** Interleaved source/target indexes. */
  links: Uint32Array
  /** Interleaved x/y per node from the previous layout (NaN where unknown), so a changed graph keeps its shape. */
  seed?: Float32Array
  layout: GraphLayout
  /** Ticks to run before the first frame (pre-warm for large graphs). */
  prewarm: number
  /**
   * Resumes a remembered layout of exactly these nodes (the seed knows every one): the alpha it had cooled to,
   * and the indexes of the nodes the user had dragged, which stay pinned where they are.
   */
  resume?: { alpha: number; pinned: Uint32Array }
  reducedMotion: boolean
}

export type SimulationCommand =
  | SimulationInit
  | { type: 'layout'; layout: GraphLayout }
  | { type: 'pin'; index: number; x: number; y: number }
  | { type: 'dragEnd' }
  | { type: 'unpinAll' }
  | { type: 'reheat' }
  | { type: 'stop' }

export interface SimulationFrame {
  type: 'positions'
  generation: number
  /** Interleaved x/y per node (transferred, not copied). */
  positions: Float32Array
  alpha: number
}
