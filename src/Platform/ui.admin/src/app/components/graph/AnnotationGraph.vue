<script setup lang="ts">
import { computed, markRaw, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router'
import { useElementSize, useMutationObserver, usePreferredReducedMotion } from '@vueuse/core'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import type { GraphFrameMemory, GraphTransform } from '@/stores/page-memory'
import { usePreferencesStore } from '@/stores/preferences'
import { useTypeAppearanceStore } from '@/stores/type-appearance'
import { typeCodes, typeMeta, type AnnotationTypeCode } from '@/utils/annotation-key'
import {
  buildGraph,
  countDescendants,
  mostConnectedRoot,
  neighbourhood,
  neighbourInDirection,
  sameKeys,
  topByDegree,
  type GraphData,
  type GraphLayout,
} from './graph-model'
import type { SimulationCommand, SimulationFrame } from './simulation-protocol'

/**
 * The annotation graph (spec section 12). The visible set is the whole view, or in focus mode the k-hop
 * neighbourhood of one annotation widened by the filter matches. Above 5,000 annotations the graph opens in focus
 * mode. The layout runs in a worker; GraphCanvas draws the positions it streams.
 * URL state: `q` (filter), `layout`, `focus`, `depth`, `all`. The page memory of the view keeps the hidden types,
 * the last frame of the layout and the zoom, so the graph comes back exactly as it was left.
 */
const props = defineProps<{ selectedKey?: string }>()
const emit = defineEmits<{ open: [key: string]; close: [] }>()

const focusThreshold = 5_000
const showAllLimit = 20_000
const listLimit = 300

const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()
const preferences = usePreferencesStore()
const typeAppearance = useTypeAppearanceStore()
const memory = usePageMemory('graph')
const filter = useAnnotationFilter()
const confirm = useConfirm()
const toast = useToast()
const reducedMotion = usePreferredReducedMotion()

const container = useTemplateRef<HTMLDivElement>('container')
const canvas = useTemplateRef<{
  fit: (indexes?: readonly number[]) => void
  restore: (value: GraphTransform) => void
  reveal: (index: number) => void
  toBlob: () => Promise<Blob | null>
  frames: () => number
}>('canvas')
const bounds = useElementSize(container)

const first = (value: unknown) => (Array.isArray(value) ? value[0] : value)
const queryText = (name: string) => {
  const value = first(route.query[name])
  return typeof value === 'string' ? value : ''
}

function updateQuery(changes: Record<string, string | undefined>, push = false) {
  const query: LocationQueryRaw = { ...route.query }
  for (const [key, value] of Object.entries(changes)) {
    if (value === undefined || value === '') {
      delete query[key]
    } else {
      query[key] = value
    }
  }
  void (push ? router.push({ query }) : router.replace({ query }))
}

const layout = computed<GraphLayout>({
  get: () => (['force', 'radial'].includes(queryText('layout')) ? (queryText('layout') as GraphLayout) : 'layered'),
  set: (value) => updateQuery({ layout: value === 'layered' ? undefined : value }),
})
const depth = computed({
  get: () => Math.min(Math.max(Number.parseInt(queryText('depth')) || 2, 1), 6),
  set: (value: number) => updateQuery({ depth: value === 2 ? undefined : String(value) }),
})
const showAll = computed(() => queryText('all') === '1')
const hidden = computed<Set<AnnotationTypeCode>>({
  get: () => memory.value.hidden,
  set: (value) => (memory.value.hidden = value),
})

const hasData = computed(() => catalog.status === 'ready' || catalog.fromSnapshot)
const large = computed(() => catalog.loadedCount > focusThreshold)

const focusKey = computed(() => {
  const requested = queryText('focus')
  if (requested && catalog.byKey.has(requested)) {
    return requested
  }
  return undefined
})

/** Where focus mode starts: the selected node, a recently opened one, or the most connected subject or responsibility. */
function defaultFocus(): string | undefined {
  if (props.selectedKey && catalog.byKey.has(props.selectedKey)) {
    return props.selectedKey
  }
  return (
    preferences.state.recentKeys.find((key) => catalog.byKey.has(key)) ??
    mostConnectedRoot(catalog.annotations, catalog.childrenByKey)
  )
}

// Large views open in focus mode; the start key goes into the URL so the screen can be shared.
watch(
  [hasData, large, focusKey, showAll],
  () => {
    if (hasData.value && large.value && !focusKey.value && !showAll.value && catalog.status === 'ready') {
      const start = defaultFocus()
      if (start) {
        updateQuery({ focus: start })
      }
    }
  },
  { immediate: true },
)

const descendants = computed(() => countDescendants(catalog.byKey.keys(), catalog.ancestorsOf))
const matchKeys = computed(() =>
  filter.active.value ? new Set(filter.filtered.value.map((annotation) => annotation.AnnotationKey)) : undefined,
)

/** The nodes before the type toggles: whole view, or the neighbourhood plus the filter matches. */
const candidates = computed<string[]>(() => {
  if (!hasData.value) {
    return []
  }
  if (!focusKey.value) {
    return large.value && !showAll.value ? [] : [...catalog.byKey.keys()]
  }
  const keys = neighbourhood(focusKey.value, depth.value, catalog.childrenByKey, (key) => catalog.byKey.has(key))
  for (const key of matchKeys.value ?? []) {
    keys.add(key)
  }
  return [...keys]
})

const typeCounts = computed(() => {
  const counts = new Map<AnnotationTypeCode, number>()
  for (const key of candidates.value) {
    const code = key.slice(0, 3) as AnnotationTypeCode
    counts.set(code, (counts.get(code) ?? 0) + 1)
  }
  return counts
})

const visibleKeys = computed(() =>
  hidden.value.size
    ? candidates.value.filter((key) => !hidden.value.has(key.slice(0, 3) as AnnotationTypeCode))
    : candidates.value,
)

const graph = shallowRef<GraphData>(buildGraph([], new Map()))
const positions = shallowRef<Float32Array>()
const layingOut = ref(false)
const pinned = ref(new Set<number>())

// Outside focus mode the filter dims the nodes that do not match (in focus mode it adds them instead).
const dimmed = computed<ReadonlySet<number>>(() => {
  const matches = matchKeys.value
  if (focusKey.value || !matches) {
    return new Set()
  }
  const result = new Set<number>()
  graph.value.nodes.forEach((node, index) => {
    if (!matches.has(node.key)) {
      result.add(index)
    }
  })
  return result
})
const labelled = computed(() => topByDegree(graph.value.nodes, 50))

// Type colors come from the --st-* tokens and change with the theme.
const colors = shallowRef<Record<AnnotationTypeCode, string>>(readColors())

function readColors(): Record<AnnotationTypeCode, string> {
  const style = getComputedStyle(document.documentElement)
  return Object.fromEntries(
    typeCodes.map((code) => [code, style.getPropertyValue(`--st-${code}`).trim() || '#888']),
  ) as Record<AnnotationTypeCode, string>
}

useMutationObserver(
  typeof document === 'undefined' ? undefined : document.documentElement,
  () => (colors.value = readColors()),
  { attributes: true, attributeFilter: ['class'] },
)

// ... and with the type colors of the project settings and Preferences (424-13), once their style is written.
watch(
  () => typeAppearance.css,
  async () => {
    await nextTick()
    colors.value = readColors()
  },
  { flush: 'post' },
)

// The simulation worker.
let worker: Worker | undefined
let generation = 0
let fitted = false
let settledFit = false
let interacted = false

function onInteract() {
  interacted = true
}

function post(command: SimulationCommand, transfer: Transferable[] = []) {
  worker?.postMessage(command, transfer)
}

/** Where a new layout starts: positions by node key, and the remembered frame when they come from the memory. */
interface LayoutSeed {
  index: ReadonlyMap<string, number>
  positions: Float32Array
  remembered?: GraphFrameMemory
}

/** The current layout while there is one, otherwise the last frame of the view's graph from an earlier visit. */
function layoutSeed(current: GraphData): LayoutSeed | undefined {
  if (positions.value) {
    return { index: current.index, positions: positions.value }
  }
  const frame = memory.value.frame
  return (
    frame && {
      index: new Map(frame.keys.map((key, index) => [key, index])),
      positions: frame.positions,
      remembered: frame,
    }
  )
}

function startSimulation(next: GraphData, from: LayoutSeed | undefined) {
  const count = next.nodes.length
  const seed = new Float32Array(count * 2).fill(Number.NaN)
  let seeded = 0
  if (from) {
    next.nodes.forEach((node, index) => {
      const old = from.index.get(node.key)
      if (old !== undefined) {
        seed[index * 2] = from.positions[old * 2]!
        seed[index * 2 + 1] = from.positions[old * 2 + 1]!
        seeded++
      }
    })
  }
  const links = new Uint32Array(next.links.length * 2)
  next.links.forEach(([source, target], index) => {
    links[index * 2] = source
    links[index * 2 + 1] = target
  })
  const tiers = Float32Array.from(next.nodes, (node) => node.tier)
  const radii = Float32Array.from(next.nodes, (node) => node.radius)
  const keepsShape = seeded > count / 2
  // The same nodes in the same layout as the remembered frame: it resumes with its alpha and its dragged nodes.
  const resumed =
    from?.remembered && seeded === count && from.remembered.layout === layout.value ? from.remembered : undefined
  const pins =
    resumed?.pinned.flatMap((key) => {
      const index = next.index.get(key)
      return index === undefined ? [] : [index]
    }) ?? []

  generation++
  fitted = keepsShape
  settledFit = keepsShape
  layingOut.value = !!worker && count > 0
  pinned.value = new Set(pins)
  post(
    {
      type: 'init',
      generation,
      tiers,
      radii,
      links,
      seed,
      layout: layout.value,
      // The large layered layout is pre-warmed before the first frame (spec section 12), then streamed.
      prewarm: keepsShape ? 0 : count > focusThreshold ? 300 : count > 1_500 ? 100 : 0,
      reducedMotion: reducedMotion.value === 'reduce',
      resume: resumed ? { alpha: resumed.alpha, pinned: Uint32Array.from(pins) } : undefined,
    },
    [tiers.buffer, radii.buffer, links.buffer, seed.buffer],
  )

  // A remembered layout that keeps its shape gets its zoom back; without a remembered zoom it is fitted.
  if (from?.remembered && keepsShape) {
    const view = memory.value.transform
    if (view && canvas.value) {
      canvas.value.restore(view)
    } else {
      fitted = false
      settledFit = false
    }
  }
}

watch(
  visibleKeys,
  (keys) => {
    const current = graph.value
    if (
      sameKeys(
        keys,
        current.nodes.map((node) => node.key),
      )
    ) {
      return
    }
    const next = buildGraph(keys, descendants.value)
    const seed = layoutSeed(current)
    graph.value = next
    positions.value = undefined
    startSimulation(next, seed)
  },
  { immediate: true },
)

const nodeKeys = computed(() => graph.value.nodes.map((node) => node.key))

function onFrame(event: MessageEvent<SimulationFrame>) {
  if (event.data.generation !== generation) {
    return
  }
  positions.value = event.data.positions
  // Every frame is remembered by reference: the worker transfers a new buffer each time and never reuses it.
  memory.value.frame = markRaw({
    layout: layout.value,
    keys: nodeKeys.value,
    positions: event.data.positions,
    alpha: event.data.alpha,
    pinned: [...pinned.value].map((index) => graph.value.nodes[index]!.key),
  })
  layingOut.value = false
  if (!fitted) {
    fitted = true
    requestAnimationFrame(() => canvas.value?.fit())
  } else if (!settledFit && event.data.alpha < 0.05) {
    settledFit = true
    if (!interacted) {
      canvas.value?.fit()
    }
  }
}

onMounted(() => {
  if (typeof Worker === 'undefined') {
    return
  }
  worker = new Worker(new URL('./simulation.worker.ts', import.meta.url), { type: 'module' })
  worker.addEventListener('message', onFrame)
  if (graph.value.nodes.length) {
    startSimulation(graph.value, layoutSeed(graph.value))
  }
})

onBeforeUnmount(() => {
  post({ type: 'stop' })
  worker?.terminate()
})

watch(layout, (value) => post({ type: 'layout', layout: value }))

// Selection: the route key, and a keyboard cursor that starts there.
const activeKey = ref<string>()
watch(
  () => props.selectedKey,
  (key) => (activeKey.value = key),
  { immediate: true },
)
const activeIndex = computed(() => {
  const key = activeKey.value ?? props.selectedKey
  return key === undefined ? undefined : graph.value.index.get(key)
})

const hover = ref<{ index: number; x: number; y: number }>()

function onHover(index: number | undefined, clientX: number, clientY: number) {
  const rect = container.value?.getBoundingClientRect()
  hover.value = index === undefined || !rect ? undefined : { index, x: clientX - rect.left, y: clientY - rect.top }
}

const hoverKey = computed(() => (hover.value ? graph.value.nodes[hover.value.index]?.key : undefined))

function keyAt(index: number) {
  return graph.value.nodes[index]?.key
}

function onSelect(index: number) {
  const key = keyAt(index)
  if (key) {
    activeKey.value = key
    emit('open', key)
  }
}

function focusOn(key: string | undefined) {
  if (key) {
    updateQuery({ focus: key, all: undefined }, true)
  }
}

function onDrag(index: number, x: number, y: number) {
  if (!pinned.value.has(index)) {
    pinned.value = new Set(pinned.value).add(index)
  }
  post({ type: 'pin', index, x, y })
}

function releaseAll() {
  pinned.value = new Set()
  post({ type: 'unpinAll' })
}

function toggleType(code: AnnotationTypeCode) {
  const next = new Set(hidden.value)
  if (next.has(code)) {
    next.delete(code)
  } else {
    next.add(code)
  }
  hidden.value = next
}

async function showEverything() {
  if (catalog.loadedCount > showAllLimit) {
    return
  }
  if (large.value) {
    const count = new Intl.NumberFormat().format(catalog.loadedCount)
    const ok = await confirm({
      title: `Show all ${count} annotations?`,
      description:
        'The layout of the whole view is computed before the first frame, which takes several seconds. Names are only drawn for the most connected, hovered and selected nodes.',
      confirmLabel: 'Show everything',
      color: 'primary',
    })
    if (!ok) {
      return
    }
  }
  updateQuery({ all: large.value ? '1' : undefined, focus: undefined }, true)
}

async function exportPng() {
  const blob = await canvas.value?.toBlob()
  if (!blob) {
    toast.add({ title: 'The graph could not be exported', color: 'error' })
    return
  }
  const link = document.createElement('a')
  link.href = URL.createObjectURL(blob)
  link.download = `graph-${context.org}-${context.project}-${context.view}.png`
  link.click()
  setTimeout(() => URL.revokeObjectURL(link.href), 1_000)
}

const canList = computed(() => graph.value.nodes.length > 0 && graph.value.nodes.length <= listLimit)

function openAsList() {
  if (!canList.value) {
    return
  }
  const keys = graph.value.nodes.map((node) => node.key)
  void router.push({
    path: `/${context.org}/${context.project}/${context.view}/annotations`,
    query: { q: `key:${keys.join(',')}` },
  })
}

// Keyboard: the arrow keys move to the neighbour in that direction, Enter opens it, Escape closes the detail.
const directions: Record<string, { x: number; y: number }> = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 },
}

function onKeydown(event: KeyboardEvent) {
  const direction = directions[event.key]
  if (direction) {
    event.preventDefault()
    if (!positions.value || graph.value.nodes.length === 0) {
      return
    }
    const from = activeIndex.value
    const next =
      from === undefined
        ? (graph.value.index.get(focusKey.value ?? '') ?? [...labelled.value][0])
        : neighbourInDirection(graph.value, positions.value, from, direction)
    if (next !== undefined) {
      activeKey.value = keyAt(next)
      canvas.value?.reveal(next)
    }
  } else if (event.key === 'Enter' && activeKey.value) {
    event.preventDefault()
    emit('open', activeKey.value)
  } else if (event.key === 'Escape' && props.selectedKey) {
    emit('close')
  }
}

const activeNode = computed(() => {
  const index = activeIndex.value
  const node = index === undefined ? undefined : graph.value.nodes[index]
  if (!node || index === undefined) {
    return undefined
  }
  const name = catalog.byKey.get(node.key)?.Name ?? node.key
  const count = graph.value.neighbours[index]?.length ?? 0
  return `${typeMeta[node.code].label} ${name}, ${count} ${count === 1 ? 'connection' : 'connections'}`
})

const formatter = new Intl.NumberFormat()
const summary = computed(() => {
  const shown = formatter.format(graph.value.nodes.length)
  const total = formatter.format(catalog.loadedCount)
  return focusKey.value
    ? `${shown} of ${total} annotations around ${focusKey.value} (depth ${depth.value}${matchKeys.value ? ' + filter matches' : ''})`
    : `${shown} annotations`
})

const waitingForFocus = computed(() => hasData.value && large.value && !focusKey.value && !showAll.value)
</script>

<template>
  <div class="flex h-full min-h-0 flex-col">
    <div class="border-b border-default px-4 py-2">
      <GraphToolbar
        :query="filter.query.value"
        :parsed="filter.parsed.value"
        :shown="filter.filtered.value.length"
        :total="catalog.loadedCount"
        :layout="layout"
        :depth="depth"
        :focus-key="focusKey"
        :can-show-all="catalog.loadedCount <= showAllLimit"
        :can-list="canList"
        :pinned="pinned.size > 0"
        :compact="bounds.width.value < 720"
        @update:query="(value) => (filter.query.value = value)"
        @update:layout="(value) => (layout = value)"
        @update:depth="(value) => (depth = value)"
        @fit="canvas?.fit()"
        @reheat="post({ type: 'reheat' })"
        @export="exportPng"
        @show-all="showEverything"
        @focus="focusOn(activeKey ?? defaultFocus())"
        @open-list="openAsList"
        @release-all="releaseAll"
      />
    </div>

    <div
      ref="container"
      class="relative min-h-0 flex-1 overflow-hidden focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none focus-visible:ring-inset"
      tabindex="0"
      role="application"
      aria-roledescription="graph"
      :aria-label="`Annotation graph, ${summary}. Use the arrow keys to move between connected annotations and Enter to open one.`"
      @keydown="onKeydown"
    >
      <GraphCanvas
        ref="canvas"
        :graph="graph"
        :positions="positions"
        :colors="colors"
        :dimmed="dimmed"
        :selected="activeIndex"
        :labelled="labelled"
        @hover="onHover"
        @select="onSelect"
        @focus="(index) => focusOn(keyAt(index))"
        @drag="onDrag"
        @drag-end="post({ type: 'dragEnd' })"
        @release-all="releaseAll"
        @interact="onInteract"
        @transform="(value) => (memory.transform = value)"
      />

      <GraphTooltip
        v-if="hover && hoverKey"
        :annotation-key="hoverKey"
        :annotation="catalog.byKey.get(hoverKey)"
        :x="hover.x"
        :y="hover.y"
        :bounds="{ width: bounds.width.value, height: bounds.height.value }"
        :neighbours="graph.neighbours[hover.index]?.length ?? 0"
      />

      <div class="pointer-events-none absolute inset-x-3 top-3 flex flex-wrap items-start justify-between gap-2">
        <GraphLegend
          class="pointer-events-auto"
          :counts="typeCounts"
          :hidden="hidden"
          :collapsed="bounds.width.value > 0 && bounds.width.value < 640"
          @toggle="toggleType"
          @show-all="hidden = new Set()"
        />
      </div>
      <p class="pointer-events-none absolute start-3 bottom-3 rounded bg-default/80 px-2 py-1 text-xs text-muted">
        {{ summary }}
      </p>
      <p class="sr-only" aria-live="polite">{{ activeNode }}</p>

      <div
        v-if="(!hasData && catalog.status !== 'error') || layingOut || waitingForFocus"
        class="absolute inset-0 flex items-center justify-center bg-default/60"
      >
        <LoadingState
          :label="
            layingOut
              ? `Laying out ${formatter.format(graph.nodes.length)} annotations…`
              : `Loaded ${formatter.format(catalog.loadedCount)} annotations…`
          "
        />
      </div>
      <ErrorState
        v-else-if="catalog.status === 'error'"
        class="absolute inset-0"
        :error="catalog.error"
        title="The catalog could not be loaded"
        @retry="context.scope && catalog.load(context.scope, { force: true })"
      />
      <EmptyState
        v-else-if="catalog.status === 'ready' && catalog.loadedCount === 0"
        class="absolute inset-0"
        icon="i-lucide-waypoints"
        title="No annotations in this view"
        description="The graph shows annotations and their ancestors once the view has some."
      />
      <EmptyState
        v-else-if="hasData && visibleKeys.length === 0 && candidates.length > 0"
        class="absolute inset-0"
        icon="i-lucide-eye-off"
        title="Every type is hidden"
        description="Turn types back on in the legend."
      />
    </div>
  </div>
</template>
