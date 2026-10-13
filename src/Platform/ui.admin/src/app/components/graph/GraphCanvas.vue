<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useResizeObserver } from '@vueuse/core'
import { select } from 'd3-selection'
import { zoom, zoomIdentity, type D3ZoomEvent, type ZoomBehavior, type ZoomTransform } from 'd3-zoom'
import { quadtree, type Quadtree } from 'd3-quadtree'
import type { AnnotationTypeCode } from '@/utils/annotation-key'
import type { GraphData } from './graph-model'
import { GraphGlRenderer } from './webgl-renderer'

/**
 * Renderer of the graph (spec section 12): nodes filled with the type color, links as 1 px lines at 35 % opacity
 * with arrowheads, names at zoom ≥ 1.2 and for hovered, selected and the most connected nodes. Level of detail:
 * below zoom 0.6 the links have no arrowheads. Hit-testing uses a quadtree.
 * Links and nodes are drawn with WebGL 2 (webgl-renderer.ts); a Canvas 2D overlay draws the names, the highlighted
 * links and the rings. Without WebGL 2 the overlay draws everything.
 */
const props = defineProps<{
  graph: GraphData
  positions?: Float32Array
  colors: Record<AnnotationTypeCode, string>
  dimmed: ReadonlySet<number>
  selected?: number
  labelled: ReadonlySet<number>
}>()

const emit = defineEmits<{
  hover: [index: number | undefined, clientX: number, clientY: number]
  select: [index: number]
  focus: [index: number]
  drag: [index: number, x: number, y: number]
  dragEnd: [index: number]
  releaseAll: []
  interact: []
}>()

const canvas = ref<HTMLCanvasElement>()
const glCanvas = ref<HTMLCanvasElement>()
let renderer: GraphGlRenderer | undefined
// What the WebGL buffers currently hold.
let glGraph: GraphData | undefined
let glDimmed: ReadonlySet<number> | undefined
let glPositions: Float32Array | undefined
let glStyle = ''
const transform = shallowRef<ZoomTransform>(zoomIdentity)
const hovered = ref<number>()
let behavior: ZoomBehavior<HTMLCanvasElement, unknown> | undefined
let width = 0
let height = 0
/** A fit asked for while the canvas had no size (hidden); it runs once the canvas is shown. */
let pendingFit: readonly number[] | undefined
let scheduled = false
let frames = 0

// Plain copies of the hot props: the draw loop reads positions several hundred thousand times per frame, and
// every read through the reactive props proxy costs.
let pos: Float32Array = new Float32Array(0)
const x = (index: number) => pos[index * 2] ?? 0
const y = (index: number) => pos[index * 2 + 1] ?? 0

// The quadtree follows the positions at most every 150 ms, which is plenty for hover and clicks.
let tree: Quadtree<number> | undefined
let treePositions: Float32Array | undefined
let treeBuiltAt = 0

function currentTree(): Quadtree<number> | undefined {
  if (props.positions && (!tree || (treePositions !== props.positions && performance.now() - treeBuiltAt > 150))) {
    tree = quadtree<number>()
      .x(x)
      .y(y)
      .addAll(props.graph.nodes.map((_, index) => index))
    treePositions = props.positions
    treeBuiltAt = performance.now()
  }
  return tree
}

/** The node under a client point, if any. */
function nodeAt(clientX: number, clientY: number): number | undefined {
  const found = canvas.value ? currentTree() : undefined
  if (!found) {
    return undefined
  }
  const rect = canvas.value!.getBoundingClientRect()
  const [px, py] = transform.value.invert([clientX - rect.left, clientY - rect.top])
  const index = found.find(px, py, 40)
  if (index === undefined) {
    return undefined
  }
  const radius = props.graph.nodes[index]?.radius ?? 0
  return Math.hypot(x(index) - px, y(index) - py) <= radius + 4 / transform.value.k ? index : undefined
}

function schedule() {
  if (!scheduled) {
    scheduled = true
    requestAnimationFrame(draw)
  }
}

function cssVar(style: CSSStyleDeclaration, name: string, fallback: string) {
  return style.getPropertyValue(name).trim() || fallback
}

function draw() {
  scheduled = false
  const element = canvas.value
  const context = element?.getContext('2d')
  if (!element || !context || !props.positions) {
    return
  }
  frames++

  const ratio = window.devicePixelRatio || 1
  const { k, x: tx, y: ty } = transform.value
  const { nodes, neighbours } = props.graph
  const { dimmed, labelled, selected, colors } = props
  const style = getComputedStyle(element)
  const text = cssVar(style, '--ui-text-highlighted', '#111')
  const muted = cssVar(style, '--ui-text-dimmed', '#999')
  const primary = cssVar(style, '--ui-primary', '#4f46e5')
  const background = cssVar(style, '--ui-bg', '#fff')

  if (renderer) {
    if (glGraph !== props.graph || glDimmed !== dimmed) {
      renderer.setGraph(props.graph, dimmed)
      glGraph = props.graph
      glDimmed = dimmed
      glPositions = undefined
    }
    const styleKey = `${muted}|${Object.values(colors).join()}`
    if (styleKey !== glStyle) {
      renderer.setStyle({ colors, link: muted })
      glStyle = styleKey
    }
    if (glPositions !== pos) {
      renderer.setPositions(pos)
      glPositions = pos
    }
    renderer.render({ transform: { k, x: tx, y: ty }, width, height, ratio, arrows: k >= 0.6 })
  }

  context.setTransform(ratio, 0, 0, ratio, 0, 0)
  context.clearRect(0, 0, width, height)
  context.setTransform(ratio * k, 0, 0, ratio * k, ratio * tx, ratio * ty)

  const focusIndex = hovered.value ?? props.selected
  const highlight = new Set<number>()
  if (focusIndex !== undefined) {
    highlight.add(focusIndex)
    neighbours[focusIndex]?.forEach((neighbour) => highlight.add(neighbour))
  }

  // Without WebGL: links as one batched path, arrowheads only when zoomed in enough to see them, then the nodes.
  if (!renderer) {
    drawBulk(context, k, muted)
  }

  if (focusIndex !== undefined) {
    context.globalAlpha = 0.9
    context.strokeStyle = primary
    context.lineWidth = 1.5 / k
    context.beginPath()
    for (const neighbour of neighbours[focusIndex] ?? []) {
      context.moveTo(x(focusIndex), y(focusIndex))
      context.lineTo(x(neighbour), y(neighbour))
    }
    context.stroke()
  }
  context.globalAlpha = 1
  for (const index of highlight) {
    const isSelected = index === selected
    context.strokeStyle = isSelected ? primary : text
    context.lineWidth = (isSelected ? 3 : 1.5) / k
    context.beginPath()
    context.arc(x(index), y(index), (nodes[index]?.radius ?? 0) + 2 / k, 0, Math.PI * 2)
    context.stroke()
  }

  // Names: highlighted and the most connected nodes first, then (zoomed in) the larger nodes in the viewport.
  // A name that would overlap one already drawn is skipped, so dense areas stay readable.
  context.font = `${11 / k}px ${style.fontFamily || 'sans-serif'}`
  context.textAlign = 'center'
  context.textBaseline = 'top'
  context.lineJoin = 'round'
  context.lineWidth = 3 / k
  context.strokeStyle = background
  context.fillStyle = text
  const taken = new Map<number, [number, number, number, number][]>()
  const cell = 64
  const fits = (box: [number, number, number, number]) => {
    const cells: number[] = []
    for (let cx = Math.floor(box[0] / cell); cx <= Math.floor(box[2] / cell); cx++) {
      for (let cy = Math.floor(box[1] / cell); cy <= Math.floor(box[3] / cell); cy++) {
        const id = cx * 100_000 + cy
        const boxes = taken.get(id)
        if (boxes?.some((other) => box[0] < other[2] && box[2] > other[0] && box[1] < other[3] && box[3] > other[1])) {
          return false
        }
        cells.push(id)
      }
    }
    for (const id of cells) {
      const boxes = taken.get(id)
      if (boxes) {
        boxes.push(box)
      } else {
        taken.set(id, [box])
      }
    }
    return true
  }
  let drawn = 0
  const label = (index: number, force: boolean) => {
    const node = nodes[index]
    if (!node || drawn >= 400) {
      return
    }
    const sx = x(index) * k + tx
    const sy = (y(index) + node.radius) * k + ty + 2
    if (sx < -50 || sx > width + 50 || sy < -20 || sy > height + 20) {
      return
    }
    const name = node.key.slice(node.key.lastIndexOf('.') + 1)
    const half = (context.measureText(name).width * k) / 2 + 2
    if (!fits([sx - half, sy, sx + half, sy + 13]) && !force) {
      return
    }
    context.strokeText(name, x(index), y(index) + node.radius + 2 / k)
    context.fillText(name, x(index), y(index) + node.radius + 2 / k)
    drawn++
  }
  if (focusIndex !== undefined) {
    label(focusIndex, true)
  }
  highlight.forEach((index) => label(index, false))
  labelled.forEach((index) => !dimmed.has(index) && label(index, false))
  if (k >= 1.2) {
    for (const index of bySize()) {
      if (drawn >= 400) {
        break
      }
      if (!dimmed.has(index)) {
        label(index, false)
      }
    }
  }
}

// Node indexes per type, split into dimmed and normal; rebuilt when the graph or the dimmed set changes.
let groupCache:
  { graph: GraphData; dimmed: ReadonlySet<number>; groups: [boolean, Map<AnnotationTypeCode, number[]>][] } | undefined
function nodeGroups(): [boolean, Map<AnnotationTypeCode, number[]>][] {
  const { graph, dimmed } = props
  if (groupCache?.graph !== graph || groupCache.dimmed !== dimmed) {
    const faint = new Map<AnnotationTypeCode, number[]>()
    const normal = new Map<AnnotationTypeCode, number[]>()
    graph.nodes.forEach((node, index) => {
      const target = dimmed.has(index) ? faint : normal
      const list = target.get(node.code)
      if (list) {
        list.push(index)
      } else {
        target.set(node.code, [index])
      }
    })
    groupCache = {
      graph,
      dimmed,
      groups: [
        [true, faint],
        [false, normal],
      ],
    }
  }
  return groupCache.groups
}

// Node indexes by radius, largest first: the order in which names claim space.
let sizeOrder: { graph: GraphData; order: number[] } | undefined
function bySize(): number[] {
  if (sizeOrder?.graph !== props.graph) {
    const order = props.graph.nodes.map((_, index) => index)
    order.sort((a, b) => props.graph.nodes[b]!.radius - props.graph.nodes[a]!.radius)
    sizeOrder = { graph: props.graph, order }
  }
  return sizeOrder.order
}

/** Links, arrowheads and nodes in Canvas 2D: the fallback when WebGL 2 is not available. */
function drawBulk(context: CanvasRenderingContext2D, k: number, muted: string) {
  const { nodes, links } = props.graph
  const { colors } = props
  context.globalAlpha = 0.35

  context.lineWidth = 1 / k
  context.strokeStyle = muted
  context.beginPath()
  for (const [source, target] of links) {
    context.moveTo(x(source), y(source))
    context.lineTo(x(target), y(target))
  }
  context.stroke()

  if (k >= 0.6) {
    const size = 5 / Math.sqrt(k)
    context.fillStyle = muted
    context.beginPath()
    for (const [source, target] of links) {
      const dx = x(target) - x(source)
      const dy = y(target) - y(source)
      const length = Math.hypot(dx, dy) || 1
      const ux = dx / length
      const uy = dy / length
      const offset = (nodes[target]?.radius ?? 0) + 1 / k
      const tipX = x(target) - ux * offset
      const tipY = y(target) - uy * offset
      const baseX = tipX - ux * size
      const baseY = tipY - uy * size
      context.moveTo(tipX, tipY)
      context.lineTo(baseX - uy * size * 0.45, baseY + ux * size * 0.45)
      context.lineTo(baseX + uy * size * 0.45, baseY - ux * size * 0.45)
      context.closePath()
    }
    context.fill()
  }

  // Nodes: one path per type and pass; dimmed nodes first, faint.
  for (const [dimPass, groups] of nodeGroups()) {
    context.globalAlpha = dimPass ? 0.15 : 1
    for (const [code, indexes] of groups) {
      context.fillStyle = colors[code]
      context.beginPath()
      for (const index of indexes) {
        const radius = nodes[index]!.radius
        context.moveTo(x(index) + radius, y(index))
        context.arc(x(index), y(index), radius, 0, Math.PI * 2)
      }
      context.fill()
    }
  }
}

function apply(next: ZoomTransform) {
  if (canvas.value && behavior) {
    select(canvas.value).call(behavior.transform, next)
  }
}

/** Fits the given nodes (all by default) into the canvas. */
function fit(indexes?: readonly number[]) {
  if (!props.positions || props.graph.nodes.length === 0) {
    return
  }
  if (!canvas.value?.clientWidth || !canvas.value.clientHeight) {
    pendingFit = indexes ?? []
    return
  }
  const list = indexes?.length ? indexes : props.graph.nodes.map((_, index) => index)
  let minX = Infinity
  let minY = Infinity
  let maxX = -Infinity
  let maxY = -Infinity
  for (const index of list) {
    minX = Math.min(minX, x(index))
    minY = Math.min(minY, y(index))
    maxX = Math.max(maxX, x(index))
    maxY = Math.max(maxY, y(index))
  }
  // The legend covers the top of the canvas.
  const top = 56
  const scale = Math.min(2, 0.92 / Math.max((maxX - minX + 60) / width, (maxY - minY + 60) / (height - top)))
  apply(
    zoomIdentity
      .translate(width / 2, top + (height - top) / 2)
      .scale(scale)
      .translate(-(minX + maxX) / 2, -(minY + maxY) / 2),
  )
}

/** Pans so the node is well inside the canvas, keeping the zoom. */
function reveal(index: number) {
  const [sx, sy] = transform.value.apply([x(index), y(index)])
  if (sx < 40 || sy < 40 || sx > width - 40 || sy > height - 40) {
    const { k } = transform.value
    apply(zoomIdentity.translate(width / 2 - x(index) * k, height / 2 - y(index) * k).scale(k))
  }
}

/** The graph as a PNG on the theme background. */
async function toBlob(): Promise<Blob | null> {
  const element = canvas.value
  if (!element) {
    return null
  }
  const copy = document.createElement('canvas')
  copy.width = element.width
  copy.height = element.height
  const context = copy.getContext('2d')!
  context.fillStyle = cssVar(getComputedStyle(element), '--ui-bg', '#fff')
  context.fillRect(0, 0, copy.width, copy.height)
  // Drawn now, in the same task: the WebGL drawing buffer is only readable until the frame is presented.
  draw()
  if (renderer && glCanvas.value) {
    context.drawImage(glCanvas.value, 0, 0)
  }
  context.drawImage(element, 0, 0)
  return new Promise((resolve) => copy.toBlob(resolve, 'image/png'))
}

function resize() {
  const element = canvas.value
  // A hidden graph (a full-screen detail in its place) keeps its size and view until it is shown again.
  if (!element || !element.clientWidth || !element.clientHeight) {
    return
  }
  const ratio = window.devicePixelRatio || 1
  width = element.clientWidth
  height = element.clientHeight
  for (const target of [element, glCanvas.value]) {
    if (target) {
      target.width = Math.max(1, Math.round(width * ratio))
      target.height = Math.max(1, Math.round(height * ratio))
    }
  }
  if (pendingFit) {
    const indexes = pendingFit
    pendingFit = undefined
    fit(indexes)
  } else if (props.selected !== undefined && props.positions) {
    // The detail panel opening beside the graph must not hide the selected node.
    reveal(props.selected)
  }
  schedule()
}

// Pointer interaction: dragging a node pins it, a click selects, a double-click focuses, and a long press on
// touch shows the tooltip.
let dragging: number | undefined
let downAt: { x: number; y: number } | undefined
let moved = false
let longPressed = false
let longPress: ReturnType<typeof setTimeout> | undefined

function onPointerDown(event: PointerEvent) {
  if (event.button !== 0) {
    return
  }
  const index = nodeAt(event.clientX, event.clientY)
  downAt = { x: event.clientX, y: event.clientY }
  moved = false
  longPressed = false
  if (index !== undefined) {
    dragging = index
    canvas.value?.setPointerCapture(event.pointerId)
    if (event.pointerType === 'touch') {
      longPress = setTimeout(() => {
        longPressed = true
        hovered.value = index
        emit('hover', index, event.clientX, event.clientY)
        schedule()
      }, 500)
    }
  }
}

function onPointerMove(event: PointerEvent) {
  if (dragging !== undefined && downAt) {
    if (moved || Math.hypot(event.clientX - downAt.x, event.clientY - downAt.y) > 4) {
      moved = true
      clearTimeout(longPress)
      const rect = canvas.value!.getBoundingClientRect()
      const [px, py] = transform.value.invert([event.clientX - rect.left, event.clientY - rect.top])
      emit('drag', dragging, px, py)
    }
    return
  }
  if (event.pointerType === 'touch') {
    return
  }
  const index = nodeAt(event.clientX, event.clientY)
  if (index !== hovered.value) {
    hovered.value = index
    schedule()
  }
  emit('hover', index, event.clientX, event.clientY)
}

function onPointerUp(event: PointerEvent) {
  clearTimeout(longPress)
  if (dragging !== undefined) {
    canvas.value?.releasePointerCapture(event.pointerId)
    if (moved) {
      emit('dragEnd', dragging)
    } else if (!longPressed) {
      emit('select', dragging)
    }
  } else if (event.pointerType === 'touch' && hovered.value !== undefined) {
    // A tap elsewhere hides a long-press tooltip.
    hovered.value = undefined
    emit('hover', undefined, 0, 0)
    schedule()
  }
  dragging = undefined
  downAt = undefined
}

function onDoubleClick(event: MouseEvent) {
  const index = nodeAt(event.clientX, event.clientY)
  if (index === undefined) {
    emit('releaseAll')
  } else {
    emit('focus', index)
  }
}

function onLeave() {
  if (hovered.value !== undefined && dragging === undefined) {
    hovered.value = undefined
    emit('hover', undefined, 0, 0)
    schedule()
  }
}

/** A press on a node drags the node instead of panning; pinch always zooms. */
function zoomFilter(event: MouseEvent | TouchEvent | WheelEvent): boolean {
  if (event.type === 'mousedown') {
    const mouse = event as MouseEvent
    return mouse.button === 0 && !mouse.ctrlKey && nodeAt(mouse.clientX, mouse.clientY) === undefined
  }
  if (event.type === 'touchstart') {
    const touches = (event as TouchEvent).touches
    return touches.length > 1 || !touches[0] || nodeAt(touches[0].clientX, touches[0].clientY) === undefined
  }
  return !event.ctrlKey || event.type === 'wheel'
}

/** `localStorage['2s-admin:graph-renderer'] = 'canvas'` forces the Canvas 2D renderer (comparisons, old GPUs). */
const forceCanvas = () => {
  try {
    return localStorage.getItem('2s-admin:graph-renderer') === 'canvas'
  } catch {
    return false
  }
}

onMounted(() => {
  renderer = glCanvas.value && !forceCanvas() ? GraphGlRenderer.create(glCanvas.value) : undefined
  resize()
  behavior = zoom<HTMLCanvasElement, unknown>()
    .scaleExtent([0.02, 8])
    .filter(zoomFilter)
    .on('start', (event: D3ZoomEvent<HTMLCanvasElement, unknown>) => {
      if (event.sourceEvent) {
        emit('interact')
      }
    })
    .on('zoom', (event: D3ZoomEvent<HTMLCanvasElement, unknown>) => {
      transform.value = event.transform
      schedule()
    })
  select(canvas.value!).call(behavior).on('dblclick.zoom', null)
})

onBeforeUnmount(() => {
  clearTimeout(longPress)
  renderer?.dispose()
})
useResizeObserver(canvas, resize)

watch(
  () => props.positions,
  (value) => {
    pos = value ?? new Float32Array(0)
    schedule()
  },
  { immediate: true },
)
watch(() => [props.dimmed, props.selected, props.colors, props.labelled], schedule)
watch(
  () => props.selected,
  (index) => index !== undefined && props.positions && reveal(index),
)
watch(
  () => props.graph,
  () => {
    tree = undefined
    hovered.value = undefined
    schedule()
  },
)

defineExpose({
  fit,
  reveal,
  toBlob,
  redraw: schedule,
  /** Frames drawn so far (the FPS probe of the review). */
  frames: () => frames,
})
</script>

<template>
  <div class="relative h-full w-full">
    <canvas ref="glCanvas" class="absolute inset-0 block h-full w-full" aria-hidden="true" />
    <canvas
      ref="canvas"
      class="absolute inset-0 block h-full w-full touch-none outline-none select-none"
      :class="hovered !== undefined ? 'cursor-pointer' : 'cursor-grab'"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @pointercancel="onPointerUp"
      @pointerleave="onLeave"
      @dblclick="onDoubleClick"
    />
  </div>
</template>
