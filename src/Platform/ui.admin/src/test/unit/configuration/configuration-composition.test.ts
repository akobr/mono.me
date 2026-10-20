import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  getConfiguration,
  getConfigurationComposition,
  type Annotation,
  type ConfigurationComposition as Composition,
} from '@42for.net/2splatform.sdk'
import ConfigurationComposition from '@/components/configuration/ConfigurationComposition.vue'
import ConfigurationEffective from '@/components/configuration/ConfigurationEffective.vue'
import { invalidateByPath, pathToQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import { compositionOf, effectiveContent } from '@/mock/configuration-handlers'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'
import { putVersion } from '@/mock/documents'
import { useCatalogStore } from '@/stores/catalog'
import { typeMeta, type AnnotationTypeCode } from '@/utils/annotation-key'
import { compositionSteps } from '@/utils/composition-lines'
import { toJson } from '@/utils/json-yaml'
import { mountScreen, settle } from '../../helpers'

// The Composition view of the Effective tab (424-36), against the mock API and against a composition shaped like the
// server's for an execution, where the subject is applied through the usage and through the context.
const server = setupServer(...mockHandlers)
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.events.removeAllListeners()
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }
const key = 'exe.northwind.invoicing.prod'
const path = `/acme/billing/default/configurations/${key}/effective`

const annotation = (annotationKey: string): Annotation => ({
  ProjectName: 'billing',
  ViewName: 'default',
  AnnotationKey: annotationKey,
  Name: annotationKey.split('.').at(-1)!,
  AnnotationType: typeMeta[annotationKey.slice(0, 3) as AnnotationTypeCode].type,
})

/** The usage gets a value the execution template lacks and a mode the template replaces; the rest is the seed. */
function changeUsage(content: Record<string, unknown> = { mode: 'stream', queue: 'invoices' }) {
  const db = loadDb()
  putVersion(db, scope, 'configuration:usg.northwind.invoicing', content)
  saveDb(db)
}

/** The server's composition of the execution of inheritance.md, with the execution template (spec section 1). */
const execution: Composition = {
  AnnotationKey: key,
  Content: {
    currency: 'NOK',
    residency: 'norway',
    plan: 'enterprise',
    endpoint: 'https://northwind.example/invoices',
    retries: 3,
  },
  Hash: 'c0ffee12',
  Sources: [
    {
      Id: 'configuration:rst.invoicing',
      Kind: 'Configuration',
      AnnotationType: 'Responsibility',
      AnnotationKey: 'rst.invoicing',
      Presence: 'Missing',
      Version: 0,
    },
    {
      Id: 'configuration:sbt.northwind',
      Kind: 'Configuration',
      AnnotationType: 'Subject',
      AnnotationKey: 'sbt.northwind',
      Presence: 'Present',
      Version: 1,
      Content: { currency: 'NOK', residency: 'norway' },
    },
    {
      Id: 'configuration:usg.northwind.invoicing',
      Kind: 'Configuration',
      AnnotationType: 'Usage',
      AnnotationKey: 'usg.northwind.invoicing',
      Presence: 'Present',
      Version: 2,
      Content: { currency: 'USD', plan: 'enterprise' },
    },
    {
      Id: 'configuration:cnt.northwind.prod',
      Kind: 'Configuration',
      AnnotationType: 'Context',
      AnnotationKey: 'cnt.northwind.prod',
      Presence: 'Present',
      Version: 1,
      Content: { endpoint: 'https://northwind.example/invoices' },
    },
    {
      Id: 'template:exe',
      Kind: 'Template',
      AnnotationType: 'Execution',
      Presence: 'Present',
      Version: 3,
      Content: { retries: 3 },
    },
    {
      Id: `configuration:${key}`,
      Kind: 'Configuration',
      AnnotationType: 'Execution',
      AnnotationKey: key,
      Presence: 'Missing',
      Version: 0,
    },
  ],
  Nodes: [
    { Index: 0, AnnotationKey: 'rst.invoicing', MergedInto: 2, Depth: 2 },
    { Index: 1, AnnotationKey: 'sbt.northwind', MergedInto: 2, Depth: 2 },
    { Index: 2, AnnotationKey: 'usg.northwind.invoicing', MergedInto: 5, Depth: 1 },
    { Index: 3, AnnotationKey: 'sbt.northwind', MergedInto: 4, Depth: 2 },
    { Index: 4, AnnotationKey: 'cnt.northwind.prod', MergedInto: 5, Depth: 1 },
    { Index: 5, AnnotationKey: key, MergedInto: null, Depth: 0 },
  ],
  Steps: [
    { Order: 1, SourceId: 'configuration:rst.invoicing', Node: 0 },
    { Order: 2, SourceId: 'configuration:sbt.northwind', Node: 1 },
    { Order: 3, SourceId: 'configuration:usg.northwind.invoicing', Node: 2 },
    { Order: 4, SourceId: 'configuration:sbt.northwind', Node: 3 },
    { Order: 5, SourceId: 'configuration:cnt.northwind.prod', Node: 4 },
    { Order: 6, SourceId: 'template:exe', Node: 5 },
    { Order: 7, SourceId: `configuration:${key}`, Node: 5 },
  ],
  // the currency comes back from the subject through the context; the residency keeps its first application
  Origins: [
    { Path: '/currency', Step: 4 },
    { Path: '/residency', Step: 2 },
    { Path: '/plan', Step: 3 },
    { Path: '/endpoint', Step: 5 },
    { Path: '/retries', Step: 6 },
  ],
}

const serveComposition = (composition: Composition) =>
  server.use(
    http.get('*/api/v1/:org/:project/:view/configuration/:key/composition', () => HttpResponse.json(composition)),
  )

/** The number of Effective and composition requests from now on. */
function countRequests() {
  const counts = { effective: 0, composition: 0 }
  server.events.on('request:start', ({ request }) => {
    const pathname = new URL(request.url).pathname
    if (pathname.endsWith(`/configuration/${key}`) && request.method === 'GET') {
      counts.effective++
    } else if (pathname.endsWith(`/configuration/${key}/composition`)) {
      counts.composition++
    }
  })
  return counts
}

async function mountComposition(
  props: Record<string, unknown> = {},
  options: { query?: string; accessMap?: Record<string, string>; catalog?: string[]; catalogLoading?: boolean } = {},
) {
  const view = await mountScreen(
    ConfigurationComposition,
    { annotationKey: key, ...props },
    { path: `${path}?show=composition${options.query ?? ''}`, accessMap: options.accessMap },
  )
  mounted.push(view)
  const catalog = useCatalogStore()
  for (const entry of options.catalog ?? ['rst.invoicing', 'sbt.northwind', 'usg.northwind.invoicing']) {
    catalog.upsert(annotation(entry))
  }
  catalog.status = options.catalogLoading ? 'loading' : 'ready'
  await settle(10)
  return view
}

async function mountEffective(query = '') {
  const view = await mountScreen(ConfigurationEffective, { annotationKey: key }, { path: `${path}${query}` })
  mounted.push(view)
  await settle(12)
  return view
}

const radio = (name: string) =>
  [...document.body.querySelectorAll<HTMLElement>('[role="radio"]')].find((item) => item.textContent?.trim() === name)!
const rows = () => [...document.body.querySelectorAll<HTMLElement>('li[data-step]')]
const row = (order: number) => rows().find((item) => item.dataset.step === String(order))!
const toggle = (order: number) => row(order).querySelector<HTMLButtonElement>('button[aria-pressed]')!
const status = () => document.body.querySelector('[aria-live="polite"]:not([role="status"])')?.textContent?.trim()
const editorText = () => document.body.querySelector<HTMLTextAreaElement>('textarea')?.value

describe('compositionOf (mock)', () => {
  it('composes the mock Effective read flat, with its content and hash', async () => {
    changeUsage()
    const db = loadDb()
    const composition = compositionOf(db, scope, key)

    expect(composition.Content).toEqual(effectiveContent(db, scope, key))
    const [effective, served] = await Promise.all([
      getConfiguration('acme', 'billing', 'default', key),
      getConfigurationComposition('acme', 'billing', 'default', key),
    ])
    // the seeded history is dated from now, so the stamps of the served sources are left out
    const { Sources: sources, ...rest } = composition
    expect(served).toMatchObject(rest)
    expect(served.Sources?.map((source) => source.Id)).toEqual(sources?.map((source) => source.Id))
    expect(served.Hash).toBe(effective.Hash)

    // every ancestor is a node at depth 1 merged into the execution, which applies the template and its own content
    expect(composition.Nodes?.map((node) => [node.AnnotationKey, node.MergedInto, node.Depth])).toEqual([
      ['rst.invoicing', 4, 1],
      ['sbt.northwind', 4, 1],
      ['usg.northwind.invoicing', 4, 1],
      ['cnt.northwind.prod', 4, 1],
      [key, null, 0],
    ])
    expect(composition.Steps?.map((step) => [step.Order, step.SourceId, step.Node])).toEqual([
      [1, 'configuration:rst.invoicing', 0],
      [2, 'configuration:sbt.northwind', 1],
      [3, 'configuration:usg.northwind.invoicing', 2],
      [4, 'configuration:cnt.northwind.prod', 3],
      [5, 'template:exe', 4],
      [6, `configuration:${key}`, 4],
    ])
    expect(composition.Sources?.find((source) => source.Id === 'template:exe')).toMatchObject({
      Kind: 'Template',
      AnnotationType: 'Execution',
      Presence: 'Present',
      Version: 3,
    })
    expect(composition.Sources?.find((source) => source.Id === `configuration:${key}`)).toEqual({
      Id: `configuration:${key}`,
      Kind: 'Configuration',
      AnnotationType: 'Execution',
      AnnotationKey: key,
      Presence: 'Missing',
      Version: 0,
    })
  })

  it('attributes the values as deepMerge merges them: an equal scalar keeps the earlier step', () => {
    // the template has enabled: true, mode: batch and channels: [email]
    changeUsage({ mode: 'stream', queue: 'invoices', enabled: true, channels: ['email'] })
    const origins = Object.fromEntries(
      (compositionOf(loadDb(), scope, key).Origins ?? []).map((origin) => [origin.Path, origin.Step]),
    )

    expect(origins).toEqual({
      '/mode': 5,
      '/queue': 3,
      '/enabled': 3,
      // arrays are replaced, even by an equal one
      '/channels': 5,
      '/channels/0': 5,
      '/retries': 5,
      '/timeout': 5,
    })
  })

  it('marks an empty own document Empty and a replaced subtree with the new step', () => {
    const db = loadDb()
    putVersion(db, scope, 'configuration:rst.invoicing', { tiers: { gold: { limit: 1 } } })
    putVersion(db, scope, `configuration:${key}`, {})
    putVersion(db, scope, 'configuration:cnt.northwind.prod', { tiers: ['flat'] })
    const composition = compositionOf(db, scope, key)

    expect(composition.Sources?.find((source) => source.Id === `configuration:${key}`)?.Presence).toBe('Empty')
    expect(composition.Origins?.filter((origin) => origin.Path?.startsWith('/tiers'))).toEqual([
      { Path: '/tiers', Step: 4 },
      { Path: '/tiers/0', Step: 4 },
    ])
  })
})

describe('compositionSteps', () => {
  it('gives each step its node, the node it goes through, a repeated source and the paths it set', () => {
    const steps = compositionSteps(execution)

    expect(
      steps.map((step) => ({
        order: step.order,
        node: step.node,
        depth: step.depth,
        via: step.via,
        own: step.own,
        again: step.again,
        paths: step.paths,
      })),
    ).toEqual([
      {
        order: 1,
        node: 'rst.invoicing',
        depth: 2,
        via: 'usg.northwind.invoicing',
        own: false,
        again: false,
        paths: [],
      },
      {
        order: 2,
        node: 'sbt.northwind',
        depth: 2,
        via: 'usg.northwind.invoicing',
        own: false,
        again: false,
        paths: ['/residency'],
      },
      {
        order: 3,
        node: 'usg.northwind.invoicing',
        depth: 1,
        via: undefined,
        own: false,
        again: false,
        paths: ['/plan'],
      },
      {
        order: 4,
        node: 'sbt.northwind',
        depth: 2,
        via: 'cnt.northwind.prod',
        own: false,
        again: true,
        paths: ['/currency'],
      },
      {
        order: 5,
        node: 'cnt.northwind.prod',
        depth: 1,
        via: undefined,
        own: false,
        again: false,
        paths: ['/endpoint'],
      },
      { order: 6, node: key, depth: 0, via: undefined, own: false, again: false, paths: ['/retries'] },
      { order: 7, node: key, depth: 0, via: undefined, own: true, again: false, paths: [] },
    ])
    expect(steps[5]).toMatchObject({ kind: 'Template', code: 'exe', version: 3, annotationKey: undefined })
    expect(steps[3]).toMatchObject({ mergedInto: 'cnt.northwind.prod', presence: 'Present', version: 1 })
  })
})

describe('ConfigurationComposition', () => {
  it('lists the steps of the mock with links to the configurations in the catalog and to the template', async () => {
    changeUsage()
    await mountComposition({}, { query: '&q=northwind' })

    expect(rows()).toHaveLength(6)
    expect([...document.body.querySelectorAll('li[data-step] a')].map((link) => link.getAttribute('href'))).toEqual([
      '/acme/billing/default/configurations/rst.invoicing?q=northwind',
      '/acme/billing/default/configurations/sbt.northwind?q=northwind',
      '/acme/billing/default/configurations/usg.northwind.invoicing?q=northwind',
      '/acme/billing/default/modeling/templates/exe',
    ])
    // not in the catalog (deleted): struck through, without a link; the annotation's own step has none either
    expect(row(4).querySelector('.line-through')?.textContent?.trim()).toBe('cnt.northwind.prod')
    expect(row(6).querySelector('a')).toBeNull()
    expect(row(6).textContent).toContain(key)
    expect(row(6).textContent).toContain('No configuration')
    expect(row(5).textContent).toContain('Template · Execution')
    expect(row(5).textContent).toContain('v3')

    // the usage sets the queue; the template replaces its mode
    expect(row(3).querySelector('summary')?.textContent?.trim()).toBe('1 value')
    expect([...row(3).querySelectorAll('details li')].map((item) => item.textContent)).toEqual(['/queue'])
    expect(row(5).querySelector('summary')?.textContent?.trim()).toBe('6 values')
    expect(row(1).textContent).toContain('No values')

    expect(editorText()).toBe(toJson(effectiveContent(loadDb(), scope, key)))
    expect(document.body.textContent).not.toContain('Read at different times')
  })

  it('shows a template without a link below Contributor', async () => {
    await mountComposition({}, { accessMap: { 'acme.billing': 'Reader' } })

    expect(row(5).querySelector('a')).toBeNull()
    expect(row(5).textContent).toContain('Template · Execution')
    expect(row(3).querySelector('a')?.getAttribute('href')).toBe(
      '/acme/billing/default/configurations/usg.northwind.invoicing',
    )
  })

  it('answers an unknown annotation with the empty state of the Effective tab', async () => {
    await mountScreen(
      ConfigurationComposition,
      { annotationKey: 'exe.nobody.nothing.none' },
      { path: '/acme/billing/default/configurations/exe.nobody.nothing.none/effective?show=composition' },
    ).then((view) => mounted.push(view))
    await settle(10)

    expect(document.body.textContent).toContain('No configuration')
    expect(document.body.textContent).toContain('Nothing is configured or inherited for this annotation yet.')
    expect(rows()).toHaveLength(0)
  })

  it('shows the branches of the server: via, depth, a source applied again and a step that sets nothing', async () => {
    serveComposition(execution)
    await mountComposition({ effectiveHash: execution.Hash }, { catalog: [] })

    expect(rows().map((item) => item.style.paddingInlineStart)).toEqual([
      '2.5rem',
      '2.5rem',
      '1.25rem',
      '2.5rem',
      '1.25rem',
      '0rem',
      '0rem',
    ])
    expect(row(1).textContent).toContain('via usg.northwind.invoicing')
    expect(row(2).textContent).toContain('via usg.northwind.invoicing')
    expect(row(4).textContent).toContain('via cnt.northwind.prod')
    expect(row(3).textContent).not.toContain('via')
    expect(
      rows()
        .filter((item) => item.textContent?.includes('Applied again'))
        .map((item) => item.dataset.step),
    ).toEqual(['4'])
    expect(row(1).textContent).toContain('No configuration')
    expect(row(1).textContent).toContain('No values')
    expect([...row(4).querySelectorAll('details li')].map((item) => item.textContent)).toEqual(['/currency'])
    expect(row(2).textContent).toContain('v1')
    expect(editorText()).toBe(toJson(execution.Content))
    expect(document.body.textContent).not.toContain('Read at different times')
  })

  it('selects a step with a toggle and names it in the status line', async () => {
    serveComposition(execution)
    await mountComposition({ effectiveHash: execution.Hash })

    expect(toggle(6).getAttribute('aria-label')).toBe('Show the lines of step 6')
    expect(toggle(6).getAttribute('aria-pressed')).toBe('false')

    toggle(6).click()
    await settle()
    expect(toggle(6).getAttribute('aria-pressed')).toBe('true')
    expect(status()).toBe('Line 6: step 6 · Template · Execution')

    toggle(1).click()
    await settle()
    expect(toggle(6).getAttribute('aria-pressed')).toBe('false')
    expect(status()).toBe('Step 1 · rst.invoicing: sets no value')

    toggle(1).click()
    await settle()
    expect(toggle(1).getAttribute('aria-pressed')).toBe('false')
    expect(status()).toBe('')
  })

  it('names a step that sets no value when the effective document is empty', async () => {
    serveComposition({
      AnnotationKey: key,
      Content: {},
      Hash: 'e0e0e0e0',
      Sources: [
        {
          Id: `configuration:${key}`,
          Kind: 'Configuration',
          AnnotationType: 'Execution',
          AnnotationKey: key,
          Presence: 'Missing',
          Version: 0,
        },
      ],
      Nodes: [{ Index: 0, AnnotationKey: key, MergedInto: null, Depth: 0 }],
      Steps: [{ Order: 1, SourceId: `configuration:${key}`, Node: 0 }],
      Origins: [],
    })
    await mountComposition({ effectiveHash: 'e0e0e0e0' })

    expect(document.body.textContent).toContain('The effective document is empty')
    expect(editorText()).toBeUndefined()
    toggle(1).click()
    await settle()
    expect(toggle(1).getAttribute('aria-pressed')).toBe('true')
    expect(status()).toBe(`Step 1 · ${key}: sets no value`)
  })

  it('marks an ancestor that is not in the catalog as deleted', async () => {
    serveComposition(execution)
    await mountComposition({ effectiveHash: execution.Hash }, { catalog: ['usg.northwind.invoicing'] })

    // the responsibility, the subject (twice) and the context are not in the catalog; the template and the own step
    // are not ancestors
    expect(
      rows()
        .filter((item) => item.textContent?.includes('Deleted'))
        .map((item) => item.dataset.step),
    ).toEqual(['1', '2', '4', '5'])
  })

  it('marks no ancestor as deleted while the catalog is loading', async () => {
    serveComposition(execution)
    await mountComposition({ effectiveHash: execution.Hash }, { catalog: [], catalogLoading: true })

    expect(rows().filter((item) => item.textContent?.includes('Deleted'))).toEqual([])
    expect(document.querySelectorAll('.line-through')).toHaveLength(0)
  })
})

describe('ConfigurationEffective', () => {
  it('shows the document by default and fetches the composition only once its view is shown', async () => {
    const counts = countRequests()
    const view = await mountEffective('?q=northwind')

    expect(document.body.querySelector('[role="radiogroup"]')?.getAttribute('aria-label')).toBe('Effective view')
    expect(radio('Document').getAttribute('aria-checked')).toBe('true')
    expect(document.body.textContent).toContain('Bindings (@…) are not evaluated here')
    expect(counts.composition).toBe(0)

    radio('Composition').click()
    await settle(12)
    expect(view.router.currentRoute.value.query).toEqual({ q: 'northwind', show: 'composition' })
    expect(radio('Composition').getAttribute('aria-checked')).toBe('true')
    expect(counts.composition).toBe(1)
    expect(rows().length).toBeGreaterThan(0)

    radio('Document').click()
    await settle(12)
    expect(view.router.currentRoute.value.query).toEqual({ q: 'northwind' })
    expect(rows()).toHaveLength(0)
  })

  it('reads the Effective document again once, then warns while the hashes differ, and Refresh reads both', async () => {
    serveComposition(execution)
    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key', () =>
        HttpResponse.json({ AnnotationKey: key, Version: 0, Content: execution.Content, Hash: 'beefcafe' }),
      ),
    )
    const counts = countRequests()
    await mountEffective('?show=composition')

    expect(counts).toEqual({ effective: 2, composition: 1 })
    expect(document.body.textContent).toContain('Read at different times')

    const refresh = [...document.body.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Refresh',
    )!
    refresh.click()
    // the warning and its button are hidden while both are read again: the focus goes on to the steps
    expect(document.activeElement?.textContent?.trim()).toBe('Steps, in the order applied')
    await settle(12)
    expect(counts).toEqual({ effective: 3, composition: 2 })
    expect(document.body.textContent).toContain('Read at different times')
  })

  it('shows no warning when the Effective document read again matches the composition', async () => {
    serveComposition(execution)
    let reads = 0
    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key', () =>
        HttpResponse.json({
          AnnotationKey: key,
          Version: 0,
          Content: execution.Content,
          Hash: reads++ === 0 ? 'beefcafe' : execution.Hash,
        }),
      ),
    )
    const counts = countRequests()
    await mountEffective('?show=composition')

    expect(counts).toEqual({ effective: 2, composition: 1 })
    expect(document.body.textContent).not.toContain('Read at different times')
  })

  it('announces the warning in a status that is there before it', async () => {
    serveComposition(execution)
    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key', () =>
        HttpResponse.json({ AnnotationKey: key, Version: 0, Content: execution.Content, Hash: 'beefcafe' }),
      ),
    )
    await mountEffective('?show=composition')

    const announced = [...document.body.querySelectorAll('p[role="status"]')].map((item) => item.textContent?.trim())
    expect(announced).toContain('The Effective document and the composition were read at different times.')
  })

  /**
   * Serves the composition and the Effective document with the hash `hashes.current`, the composition behind a gate;
   * the first Effective read can have an older hash.
   */
  function serveHashes(firstEffective?: string) {
    const hashes = { current: 'aaaa1111', gate: undefined as Promise<void> | undefined }
    let reads = 0
    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key/composition', async () => {
        await hashes.gate
        return HttpResponse.json({ ...execution, Hash: hashes.current })
      }),
      http.get('*/api/v1/:org/:project/:view/configuration/:key', () =>
        HttpResponse.json({
          AnnotationKey: key,
          Version: 0,
          Content: execution.Content,
          Hash: reads++ === 0 && firstEffective ? firstEffective : hashes.current,
        }),
      ),
    )
    return hashes
  }

  const configurationPath = `/v1/acme/billing/default/configuration/${key}`

  it('compares a cached composition only once it is read again', async () => {
    const hashes = serveHashes()
    const counts = countRequests()
    const view = await mountEffective('?show=composition')
    expect(counts).toEqual({ effective: 1, composition: 1 })

    // a save elsewhere: the Effective read is active and read again, the composition is only marked invalid
    radio('Document').click()
    await settle(12)
    hashes.current = 'bbbb2222'
    let release = () => {}
    hashes.gate = new Promise<void>((resolve) => (release = resolve))
    await invalidateByPath(view.queryClient, configurationPath)
    await settle(12)
    expect(counts).toEqual({ effective: 2, composition: 1 })

    // the cached composition still has the older hash while it is read again
    radio('Composition').click()
    await settle(12)
    expect(counts).toEqual({ effective: 2, composition: 2 })
    expect(rows().length).toBeGreaterThan(0)
    expect(document.body.textContent).not.toContain('Read at different times')

    release()
    await settle(12)
    expect(counts).toEqual({ effective: 2, composition: 2 })
    expect(document.body.textContent).not.toContain('Read at different times')
  })

  it('reads the Effective document again for a hash that comes back after the hashes agreed', async () => {
    // the first Effective read is older than the composition
    const hashes = serveHashes('old00000')
    const counts = countRequests()
    const view = await mountEffective('?show=composition')
    expect(counts).toEqual({ effective: 2, composition: 1 })
    expect(document.body.textContent).not.toContain('Read at different times')

    // a value is changed, both are read again and agree
    hashes.current = 'bbbb2222'
    await invalidateByPath(view.queryClient, configurationPath)
    await settle(12)
    expect(counts).toEqual({ effective: 3, composition: 2 })

    // and changed back: only the composition is read again (focus), the Effective read is still fresh
    hashes.current = 'aaaa1111'
    await view.queryClient.refetchQueries({ queryKey: pathToQueryKey(`${configurationPath}/composition`), exact: true })
    await settle(12)
    expect(counts).toEqual({ effective: 4, composition: 3 })
    expect(document.body.textContent).not.toContain('Read at different times')
  })
})
