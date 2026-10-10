import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { Annotation } from '@42for.net/2splatform.sdk'
import AnnotationGraph from '@/components/graph/AnnotationGraph.vue'
import {
  buildGraph,
  countDescendants,
  mostConnectedRoot,
  neighbourhood,
  neighbourInDirection,
  sameKeys,
  topByDegree,
} from '@/components/graph/graph-model'
import { seedLayout } from '@/components/graph/layout-seed'
import type { SimulationCommand, SimulationFrame } from '@/components/graph/simulation-protocol'
import { useCatalogStore } from '@/stores/catalog'
import { getAllAncestors } from '@/utils/annotation-key'
import { buildNavigation } from '@/utils/navigation'
import { mountScreen, settle } from '../../helpers'

const keys = [
  'rst.invoicing',
  'sbt.northwind',
  'sbt.contoso',
  'usg.northwind.invoicing',
  'cnt.northwind.prod',
  'exe.northwind.invoicing.prod',
]

describe('graph model', () => {
  it('links visible ancestors to descendants and sizes nodes by their descendants', () => {
    const descendants = countDescendants(keys, getAllAncestors)
    const graph = buildGraph(keys, descendants)

    expect(graph.nodes).toHaveLength(6)
    const pairs = graph.links.map(([source, target]) => `${keys[source]} → ${keys[target]}`)
    expect(pairs).toEqual(
      expect.arrayContaining([
        'rst.invoicing → usg.northwind.invoicing',
        'sbt.northwind → usg.northwind.invoicing',
        'sbt.northwind → cnt.northwind.prod',
        'usg.northwind.invoicing → exe.northwind.invoicing.prod',
        'cnt.northwind.prod → exe.northwind.invoicing.prod',
      ]),
    )
    // sbt.northwind has three descendants (usage, context, execution): 4 + 2·log2(4) = 8.
    expect(graph.nodes[graph.index.get('sbt.northwind')!]!.radius).toBe(8)
    expect(graph.nodes[graph.index.get('sbt.contoso')!]!.radius).toBe(4)
    expect(graph.neighbours[graph.index.get('exe.northwind.invoicing.prod')!]).toHaveLength(2)
  })

  it('leaves out links to hidden nodes', () => {
    const graph = buildGraph(['sbt.northwind', 'exe.northwind.invoicing.prod'], new Map())
    expect(graph.links).toEqual([])
  })

  it('collects the k-hop neighbourhood over ancestors and descendants', () => {
    const children = new Map<string, string[]>([
      ['rst.invoicing', ['usg.northwind.invoicing']],
      ['sbt.northwind', ['usg.northwind.invoicing', 'cnt.northwind.prod']],
      ['usg.northwind.invoicing', ['exe.northwind.invoicing.prod']],
      ['cnt.northwind.prod', ['exe.northwind.invoicing.prod']],
    ])
    const exists = (key: string) => keys.includes(key)

    expect([...neighbourhood('sbt.northwind', 1, children, exists)].toSorted()).toEqual([
      'cnt.northwind.prod',
      'sbt.northwind',
      'usg.northwind.invoicing',
    ])
    expect(neighbourhood('sbt.northwind', 2, children, exists)).toEqual(
      new Set([
        'sbt.northwind',
        'usg.northwind.invoicing',
        'cnt.northwind.prod',
        'rst.invoicing',
        'exe.northwind.invoicing.prod',
      ]),
    )
    expect(neighbourhood('sbt.contoso', 3, children, exists)).toEqual(new Set(['sbt.contoso']))
  })

  it('starts focus mode on the subject or responsibility with the most children', () => {
    const children = new Map([
      ['rst.invoicing', ['usg.northwind.invoicing']],
      ['sbt.northwind', ['usg.northwind.invoicing', 'cnt.northwind.prod']],
      ['cnt.northwind.prod', ['a', 'b', 'c']],
    ])
    const annotations = keys.map((key) => ({ AnnotationKey: key }) as Annotation)
    expect(mostConnectedRoot(annotations, children)).toBe('sbt.northwind')
    expect(mostConnectedRoot([], children)).toBeUndefined()
  })

  it('ranks nodes by degree and compares key lists', () => {
    const graph = buildGraph(keys, new Map())
    const top = topByDegree(graph.nodes, 2)
    expect([...top].map((index) => keys[index]).toSorted()).toEqual(['sbt.northwind', 'usg.northwind.invoicing'])
    expect(sameKeys(['a', 'b'], ['a', 'b'])).toBe(true)
    expect(sameKeys(['a', 'b'], ['b', 'a'])).toBe(false)
    expect(sameKeys(['a'], ['a', 'b'])).toBe(false)
  })

  it('moves the keyboard selection to the neighbour in the arrow direction', () => {
    const graph = buildGraph(['sbt.northwind', 'usg.northwind.invoicing', 'cnt.northwind.prod'], new Map())
    const at = (key: string) => graph.index.get(key)!
    const positions = new Float32Array(6)
    positions.set([0, 0], at('sbt.northwind') * 2)
    positions.set([-50, 100], at('usg.northwind.invoicing') * 2)
    positions.set([80, 90], at('cnt.northwind.prod') * 2)

    const from = at('sbt.northwind')
    expect(neighbourInDirection(graph, positions, from, { x: 1, y: 0 })).toBe(at('cnt.northwind.prod'))
    expect(neighbourInDirection(graph, positions, from, { x: 0, y: 1 })).toBe(at('usg.northwind.invoicing'))
    expect(neighbourInDirection(graph, positions, from, { x: 0, y: -1 })).toBeUndefined()
  })
})

describe('layout seed', () => {
  // Two subjects (tier 0) and their contexts (tier 1): left subject → contexts 2 and 3, right subject → 4 and 5.
  const tiers = [0, 0, 1, 1, 1, 1]
  const links = [0, 4, 1, 2, 0, 5, 1, 3]

  it('puts the tiers in bands from top to bottom', () => {
    const seed = seedLayout(tiers, links, 'layered')
    expect(seed.tierY[0]).toBeLessThan(seed.tierY[1]!)
    expect(seed.tierY[1]).toBeLessThan(seed.tierY[3]!)
    for (const [index, tier] of tiers.entries()) {
      expect(Math.abs(seed.positions[index * 2 + 1]! - seed.tierY[tier]!)).toBeLessThan(20)
    }
  })

  it('orders each tier by the position of the ancestors', () => {
    const { positions } = seedLayout(tiers, links, 'layered')
    const x = (index: number) => positions[index * 2]!
    expect(x(0)).toBeLessThan(x(1))
    // The children of the left subject end up left of the children of the right one.
    expect(Math.max(x(4), x(5))).toBeLessThan(Math.min(x(2), x(3)))
  })

  it('places the radial layout on rings by tier', () => {
    const seed = seedLayout(tiers, links, 'radial')
    for (const [index, tier] of tiers.entries()) {
      const radius = Math.hypot(seed.positions[index * 2]!, seed.positions[index * 2 + 1]!)
      expect(Math.abs(radius - seed.tierRadius[tier]!)).toBeLessThan(20)
    }
  })
})

describe('simulation worker', () => {
  const frames: SimulationFrame[] = []

  beforeEach(() => {
    frames.length = 0
    vi.spyOn(window, 'postMessage').mockImplementation((message: unknown) => {
      frames.push(message as SimulationFrame)
    })
  })

  afterEach(() => {
    send({ type: 'stop' })
    vi.restoreAllMocks()
  })

  let send: (command: SimulationCommand) => void = () => {}

  it('posts positions for every node, pins dragged nodes and keeps known positions of a new graph', async () => {
    send = (await import('@/components/graph/simulation.worker')).handleCommand

    send({
      type: 'init',
      generation: 7,
      tiers: new Float32Array([0, 1, 1]),
      radii: new Float32Array([6, 4, 4]),
      links: new Uint32Array([0, 1, 0, 2]),
      layout: 'layered',
      prewarm: 20,
      reducedMotion: false,
    })
    expect(frames.length).toBeGreaterThan(0)
    expect(frames[0]).toMatchObject({ type: 'positions', generation: 7 })
    expect(frames[0]!.positions).toHaveLength(6)
    expect([...frames[0]!.positions].every(Number.isFinite)).toBe(true)

    send({ type: 'pin', index: 1, x: 500, y: -300 })
    await vi.waitFor(() => {
      const pinned = frames.at(-1)!.positions
      expect([pinned[2], pinned[3]]).toEqual([500, -300])
    })

    // A new graph keeps known positions from the seed it is given.
    send({
      type: 'init',
      generation: 8,
      tiers: new Float32Array([0, 1]),
      radii: new Float32Array([6, 4]),
      links: new Uint32Array([0, 1]),
      seed: new Float32Array([10, 20, 30, 40]),
      layout: 'force',
      prewarm: 0,
      reducedMotion: true,
    })
    const first = frames.find((frame) => frame.generation === 8)!
    expect(Math.abs(first.positions[0]! - 10)).toBeLessThan(15)
    expect(Math.abs(first.positions[3]! - 40)).toBeLessThan(15)
  })
})

describe('graph screen', () => {
  const server = setupServer()
  beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
  afterEach(() => server.resetHandlers())
  afterAll(() => server.close())

  const annotation = (key: string): Annotation => ({
    ProjectName: 'billing',
    ViewName: 'default',
    AnnotationKey: key,
    Name: key.split('.').at(-1)!,
    AnnotationType: 'Execution',
  })

  function serve(all: string[]) {
    const segments = ['responsibilities', 'subjects', 'usages', 'contexts', 'executions', 'units', 'units-of-execution']
    const prefixes = ['rst', 'sbt', 'usg', 'cnt', 'exe', 'unt', 'uxe']
    server.use(
      ...segments.map((segment, index) =>
        http.get(`*/api/v1/acme/billing/default/${segment}`, () =>
          HttpResponse.json({
            Annotations: all.filter((key) => key.startsWith(`${prefixes[index]}.`)).map(annotation),
          }),
        ),
      ),
      http.get('*/api/v1/acme/billing/default/configurations', () => HttpResponse.json({ Configurations: [] })),
    )
  }

  beforeEach(async () => {
    await useCatalogStore().forgetSnapshot({ org: 'acme', project: 'billing', view: 'default' })
  })

  async function mountGraph(all: string[], path = '/acme/billing/default/graph') {
    serve(all)
    const catalog = useCatalogStore()
    await catalog.load({ org: 'acme', project: 'billing', view: 'default' })
    const screen = await mountScreen(AnnotationGraph, {}, { path })
    await settle()
    return screen
  }

  it('shows the whole view with type counts below 5,000 annotations', async () => {
    const { host } = await mountGraph(keys)
    expect(host.text()).toContain('6 annotations')
    expect(host.text()).toMatch(/Subjects\s*2/)
    expect(host.text()).toMatch(/Executions\s*1/)
    host.unmount()
  })

  it('focuses on the neighbourhood from the URL and widens it with the filter', async () => {
    const { host, router } = await mountGraph(keys, '/acme/billing/default/graph?focus=sbt.contoso')
    expect(host.text()).toContain('1 of 6 annotations around sbt.contoso (depth 2)')

    await router.replace({ query: { focus: 'sbt.northwind', depth: '1' } })
    await settle()
    expect(host.text()).toContain('3 of 6 annotations around sbt.northwind (depth 1)')

    await router.replace({ query: { focus: 'sbt.northwind', depth: '1', q: 'type:rst' } })
    await settle()
    expect(host.text()).toContain('4 of 6 annotations around sbt.northwind (depth 1 + filter matches)')
    host.unmount()
  })

  it('opens large views in focus mode on the most connected subject', async () => {
    const many = ['sbt.big', 'sbt.small', 'cnt.small.prod']
    for (let index = 0; index < 5_000; index++) {
      many.push(`cnt.big.c${index}`)
    }
    const { host, router } = await mountGraph(many)
    await vi.waitFor(() => expect(router.currentRoute.value.query.focus).toBe('sbt.big'))
    await settle()
    expect(host.text()).toContain('around sbt.big (depth 2)')
    host.unmount()
  })
})

describe('navigation', () => {
  it('lists the graph in the view section', () => {
    const view = buildNavigation({
      org: 'acme',
      project: 'billing',
      view: 'default',
      projectRole: 'Reader',
      orgRole: 'None',
    })[0]!
    expect(view.items.map((item) => item.label)).toContain('Graph')
    expect(view.items.find((item) => item.label === 'Graph')).toMatchObject({
      to: '/acme/billing/default/graph',
      locked: false,
    })
  })
})
