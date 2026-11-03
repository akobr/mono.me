import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { delay, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  configureStoryteller,
  getStorytellerConfig,
  isTimedOut,
  type Annotation,
  type StorytellerConfig,
} from '@42for.net/2splatform.sdk'
import { catalogProgressText, useCatalogStore } from '@/stores/catalog'

const server = setupServer()
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }
const annotation = (key: string, labels?: string[]): Annotation => ({
  ProjectName: 'billing',
  ViewName: 'default',
  AnnotationKey: key,
  Name: key.split('.').at(-1)!,
  AnnotationType: 'Execution',
  Labels: labels,
})

const types: Record<string, Annotation[]> = {
  responsibilities: [annotation('rst.invoicing', ['eu'])],
  subjects: [annotation('sbt.northwind', ['eu', 'pci']), annotation('sbt.contoso')],
  usages: [],
  contexts: [annotation('cnt.northwind.prod')],
  executions: [annotation('exe.northwind.invoicing.prod', ['eu'])],
  units: [],
  'units-of-execution': [],
}

/** Every type endpoint, paged one item per page to exercise the continuation tokens. */
function typeHandlers(overrides: Record<string, () => Response> = {}) {
  return Object.entries(types).map(([segment, items]) =>
    http.get(`*/api/v1/acme/billing/default/${segment}`, ({ request }) => {
      if (overrides[segment]) {
        return overrides[segment]()
      }
      const offset = Number(new URL(request.url).searchParams.get('continuationToken') ?? 0)
      return HttpResponse.json({
        Annotations: items.slice(offset, offset + 1),
        ContinuationToken: offset + 1 < items.length ? String(offset + 1) : undefined,
      })
    }),
  )
}

const configurations = http.get('*/api/v1/acme/billing/default/configurations', () =>
  HttpResponse.json({
    Configurations: [{ AnnotationKey: 'exe.northwind.invoicing.prod', Version: 3, HasContent: true }],
  }),
)

describe('catalog store', () => {
  beforeEach(async () => {
    await useCatalogStore().forgetSnapshot(scope)
  })

  it('loads the seven type streams in parallel and follows continuation tokens', async () => {
    server.use(...typeHandlers(), configurations)
    const catalog = useCatalogStore()

    await catalog.load(scope)

    expect(catalog.status).toBe('ready')
    expect([...catalog.byKey.keys()].toSorted()).toEqual([
      'cnt.northwind.prod',
      'exe.northwind.invoicing.prod',
      'rst.invoicing',
      'sbt.contoso',
      'sbt.northwind',
    ])
    expect([...catalog.labelCounts]).toEqual([
      ['eu', 3],
      ['pci', 1],
    ])
    expect(catalog.childrenByKey.get('sbt.northwind')).toEqual(['cnt.northwind.prod'])
    await expect.poll(() => catalog.configured?.has('exe.northwind.invoicing.prod')).toBe(true)
  })

  it('asks every list for the inactive annotations too, and not for the deleted ones', async () => {
    const asked: URL[] = []
    server.use(
      ...Object.keys(types).map((segment) =>
        http.get(`*/api/v1/acme/billing/default/${segment}`, ({ request }) => {
          asked.push(new URL(request.url))
          return HttpResponse.json({ Annotations: [] })
        }),
      ),
      configurations,
    )

    await useCatalogStore().load(scope)

    expect(asked).toHaveLength(7)
    for (const url of asked) {
      expect(url.searchParams.get('includeInactive')).toBe('true')
      expect(url.searchParams.has('includeDeleted')).toBe(false)
    }
  })

  it('falls back to GetAnnotations when a type endpoint fails', async () => {
    server.use(
      ...typeHandlers({ executions: () => HttpResponse.json({ Message: 'Down' }, { status: 503 }) }),
      configurations,
      http.get('*/api/v1/acme/billing/default/annotations', () =>
        HttpResponse.json({
          Annotations: [annotation('exe.northwind.invoicing.prod'), annotation('exe.contoso.payments.eu')],
        }),
      ),
    )
    const catalog = useCatalogStore()

    await catalog.load(scope)

    expect(catalog.byKey.has('exe.contoso.payments.eu')).toBe(true)
    expect(catalog.byKey.size).toBe(6)
  })

  it('applies local upserts and removals without reloading', async () => {
    server.use(...typeHandlers(), configurations)
    const catalog = useCatalogStore()
    await catalog.load(scope)

    catalog.upsert(annotation('sbt.globex', ['new']))
    catalog.remove('sbt.contoso')

    expect(catalog.byKey.has('sbt.globex')).toBe(true)
    expect(catalog.byKey.has('sbt.contoso')).toBe(false)
    expect(catalog.labelCounts.get('new')).toBe(1)
    expect(catalog.loadedCount).toBe(5)
  })

  it('shows the IndexedDB snapshot first, then replaces it with the fresh load', async () => {
    server.use(...typeHandlers(), configurations)
    await useCatalogStore().load(scope)

    // A new session: the snapshot is shown at once while the API answers slowly.
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    server.use(
      ...Object.keys(types).map((segment) =>
        http.get(`*/api/v1/acme/billing/default/${segment}`, async () => {
          await gate
          return HttpResponse.json({ Annotations: segment === 'subjects' ? [annotation('sbt.fresh')] : [] })
        }),
      ),
    )
    const catalog = useCatalogStore()
    catalog.reset()
    const loading = catalog.load(scope)

    await expect.poll(() => catalog.fromSnapshot).toBe(true)
    expect(catalog.byKey.has('sbt.northwind')).toBe(true)

    release()
    await loading

    expect(catalog.fromSnapshot).toBe(false)
    expect([...catalog.byKey.keys()]).toEqual(['sbt.fresh'])
  })

  it('keeps the local changes made while a fresh load replaces the snapshot', async () => {
    server.use(...typeHandlers(), configurations)
    await useCatalogStore().load(scope)

    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    server.use(
      ...Object.keys(types).map((segment) =>
        http.get(`*/api/v1/acme/billing/default/${segment}`, async () => {
          await gate
          return HttpResponse.json({
            Annotations: segment === 'subjects' ? [annotation('sbt.fresh'), annotation('sbt.stale')] : [],
          })
        }),
      ),
    )
    const catalog = useCatalogStore()
    catalog.reset()
    const loading = catalog.load(scope)
    await expect.poll(() => catalog.fromSnapshot).toBe(true)

    // Deleted and created before the pages arrived: the pages must not bring them back or drop them.
    catalog.remove('sbt.stale')
    catalog.upsert(annotation('sbt.edited', ['new']))

    release()
    await loading

    expect([...catalog.byKey.keys()].toSorted()).toEqual(['sbt.edited', 'sbt.fresh'])
  })

  it('reports an error when nothing could be loaded', async () => {
    server.use(
      ...typeHandlers(
        Object.fromEntries(
          Object.keys(types).map((segment) => [segment, () => HttpResponse.json({}, { status: 500 })]),
        ),
      ),
      configurations,
      http.get('*/api/v1/acme/billing/default/annotations', () =>
        HttpResponse.json({ Message: 'Down' }, { status: 500 }),
      ),
    )
    const catalog = useCatalogStore()

    await catalog.load(scope)

    expect(catalog.status).toBe('error')
  })

  it('clears the load metadata on reset', async () => {
    server.use(...typeHandlers(), configurations)
    const catalog = useCatalogStore()
    await catalog.load(scope)
    catalog.error = new Error('stale')
    catalog.fromSnapshot = true

    catalog.reset()

    expect(catalog.status).toBe('idle')
    expect(catalog.scope).toBeNull()
    expect(catalog.error).toBeUndefined()
    expect(catalog.loadedAt).toBeUndefined()
    expect(catalog.fromSnapshot).toBe(false)
    expect(catalog.byKey.size).toBe(0)
  })
})

// 424-166: a catalog load always ends, leaves browser connections to the page, and tells how far it is.
describe('catalog store on a slow API', () => {
  let shared: StorytellerConfig | undefined

  /** The SDK of the unit tests has no timeout (test/setup.ts); these tests give it a short one. */
  function timeoutAfter(timeoutMs: number) {
    shared = getStorytellerConfig()
    configureStoryteller({ ...shared, timeoutMs })
  }

  beforeEach(async () => {
    await useCatalogStore().forgetSnapshot(scope)
  })

  afterEach(() => {
    if (shared) {
      configureStoryteller(shared)
      shared = undefined
    }
  })

  const stalled = (segment: string) =>
    http.get(`*/api/v1/acme/billing/default/${segment}`, async () => {
      await delay('infinite')
      return HttpResponse.json({ Annotations: [] })
    })

  it('ends a stalled type list with its timeout, without the GetAnnotations fallback', async () => {
    timeoutAfter(100)
    let fallback = 0
    server.use(
      stalled('executions'),
      ...Object.keys(types).map((segment) =>
        http.get(`*/api/v1/acme/billing/default/${segment}`, () => HttpResponse.json({ Annotations: [] })),
      ),
      configurations,
      http.get('*/api/v1/acme/billing/default/annotations', () => {
        fallback++
        return HttpResponse.json({ Annotations: [] })
      }),
    )
    const catalog = useCatalogStore()

    await catalog.load(scope)

    expect(catalog.status).toBe('error')
    expect(isTimedOut(catalog.error)).toBe(true)
    expect(fallback).toBe(0)
    expect(catalog.progress.fallback).toBe(false)
  })

  it('drops what a first load collected before a type list timed out', async () => {
    timeoutAfter(100)
    let fallback = 0
    server.use(
      stalled('executions'),
      ...typeHandlers(),
      configurations,
      http.get('*/api/v1/acme/billing/default/annotations', () => {
        fallback++
        return HttpResponse.json({ Annotations: [] })
      }),
    )
    const catalog = useCatalogStore()

    await catalog.load(scope)

    // Without a snapshot the lists before the stalled one paged into the shown map; a partial catalog is no catalog.
    expect(catalog.status).toBe('error')
    expect(isTimedOut(catalog.error)).toBe(true)
    expect(catalog.byKey.size).toBe(0)
    expect(catalog.loadedCount).toBe(0)
    expect(fallback).toBe(0)
  })

  it('pages at most four type lists at once', async () => {
    let inFlight = 0
    let most = 0
    server.use(
      ...Object.keys(types).map((segment) =>
        http.get(`*/api/v1/acme/billing/default/${segment}`, async () => {
          most = Math.max(most, ++inFlight)
          await delay(20)
          inFlight--
          return HttpResponse.json({ Annotations: [] })
        }),
      ),
      configurations,
    )
    const catalog = useCatalogStore()

    await catalog.load(scope)

    expect(catalog.status).toBe('ready')
    expect(most).toBe(4)
    expect(catalog.progress).toMatchObject({ lists: 7, total: 7 })
  })

  it('counts the annotations and the types of a reload apart from the catalog it replaces', async () => {
    server.use(...typeHandlers(), configurations)
    const catalog = useCatalogStore()
    await catalog.load(scope)
    expect(catalog.loadedCount).toBe(5)

    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    server.use(
      http.get('*/api/v1/acme/billing/default/executions', async () => {
        await gate
        return HttpResponse.json({ Annotations: [annotation('exe.northwind.invoicing.prod')] })
      }),
    )
    const loading = catalog.load(scope, { force: true })

    await expect.poll(() => catalog.progress.lists).toBe(6)
    expect(catalog.status).toBe('loading')
    expect(catalog.progress).toEqual({ lists: 6, total: 7, annotations: 4, fallback: false, replacing: true })
    // the shown catalog keeps its count until the reload replaces it
    expect(catalog.loadedCount).toBe(5)
    expect(catalogProgressText(catalog.progress)).toBe('Refreshing: 4 annotations, 6 of 7 types…')

    release()
    await loading

    expect(catalog.status).toBe('ready')
    expect(catalog.progress).toMatchObject({ lists: 7, annotations: 5 })
    expect(catalog.loadedCount).toBe(5)
  })

  it('words the progress of a first load and of the fallback', () => {
    const progress = { lists: 3, total: 7, annotations: 12, fallback: false, replacing: false }

    expect(catalogProgressText(progress)).toBe('Loaded 12 annotations, 3 of 7 types…')
    expect(catalogProgressText({ ...progress, lists: 7, fallback: true, replacing: true })).toBe(
      'Refreshing: 12 annotations, all types…',
    )
  })

  it('keeps the shown catalog with the error when a refresh fails, and stops refreshing', async () => {
    server.use(...typeHandlers(), configurations)
    await useCatalogStore().load(scope)

    server.use(
      ...Object.keys(types).map((segment) =>
        http.get(`*/api/v1/acme/billing/default/${segment}`, () =>
          HttpResponse.json({ Message: 'Down' }, { status: 503 }),
        ),
      ),
      http.get('*/api/v1/acme/billing/default/annotations', () =>
        HttpResponse.json({ Message: 'Down' }, { status: 503 }),
      ),
    )
    // a new session: the snapshot is shown while the API fails
    const catalog = useCatalogStore()
    catalog.reset()

    await catalog.load(scope)

    expect(catalog.status).toBe('ready')
    expect(catalog.error).toMatchObject({ status: 503 })
    expect(catalog.fromSnapshot).toBe(false)
    expect(catalog.byKey.size).toBe(5)
  })

  it('ends a refresh of a shown catalog that times out the same way', async () => {
    server.use(...typeHandlers(), configurations)
    await useCatalogStore().load(scope)

    timeoutAfter(100)
    server.use(stalled('subjects'))
    const catalog = useCatalogStore()
    catalog.reset()

    await catalog.load(scope)

    expect(catalog.status).toBe('ready')
    expect(isTimedOut(catalog.error)).toBe(true)
    expect(catalog.fromSnapshot).toBe(false)
  })

  it('keeps the known configuration summaries when their list fails, with the error', async () => {
    server.use(...typeHandlers(), configurations)
    const catalog = useCatalogStore()
    await catalog.load(scope)
    await expect.poll(() => catalog.configurations?.size).toBe(1)

    server.use(
      http.get('*/api/v1/acme/billing/default/configurations', () =>
        HttpResponse.json({ Message: 'Down' }, { status: 503 }),
      ),
    )
    await catalog.refreshConfigurations()

    expect(catalog.configurations?.has('exe.northwind.invoicing.prod')).toBe(true)
    expect(catalog.configurationsError).toMatchObject({ status: 503 })

    // the next refresh clears the error
    server.use(configurations)
    await catalog.refreshConfigurations()
    expect(catalog.configurationsError).toBeUndefined()

    // none known: they stay unknown, with the error, instead of loading forever
    server.use(
      http.get('*/api/v1/acme/billing/default/configurations', () =>
        HttpResponse.json({ Message: 'Down' }, { status: 503 }),
      ),
    )
    catalog.reset()
    await catalog.load(scope)
    await expect.poll(() => catalog.configurationsError).toMatchObject({ status: 503 })
    expect(catalog.configurations).toBeNull()
  })

  it('drops a refresh of the configuration summaries that ends after a load of another view', async () => {
    const staging = { ...scope, view: 'staging' }
    await useCatalogStore().forgetSnapshot(staging)
    server.use(...typeHandlers(), configurations)
    const catalog = useCatalogStore()
    await catalog.load(scope)
    await expect.poll(() => catalog.configurations?.size).toBe(1)

    let asked = false
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    server.use(
      http.get('*/api/v1/acme/billing/default/configurations', async () => {
        asked = true
        await gate
        return HttpResponse.json({ Configurations: [{ AnnotationKey: 'exe.late', Version: 1, HasContent: true }] })
      }),
      http.get('*/api/v1/acme/billing/staging/configurations', () =>
        HttpResponse.json({ Configurations: [{ AnnotationKey: 'sbt.staging', Version: 1, HasContent: true }] }),
      ),
      http.get('*/api/v1/acme/billing/staging/:segment', () => HttpResponse.json({ Annotations: [] })),
    )

    const refresh = catalog.refreshConfigurations()
    await vi.waitFor(() => expect(asked).toBe(true))
    await catalog.load(staging)
    await expect.poll(() => catalog.configurations?.has('sbt.staging')).toBe(true)
    release()
    await refresh

    expect([...catalog.configurations!.keys()]).toEqual(['sbt.staging'])
    expect(catalog.configurationsError).toBeUndefined()
  })

  it('drops a load that another view replaced while it read its snapshot', async () => {
    const staging = { ...scope, view: 'staging' }
    await useCatalogStore().forgetSnapshot(staging)
    const asked: string[] = []
    server.use(
      http.get('*/api/v1/acme/billing/default/:segment', ({ request }) => {
        asked.push(new URL(request.url).pathname)
        return HttpResponse.json({ Annotations: [annotation('sbt.from-default')] })
      }),
      http.get('*/api/v1/acme/billing/staging/configurations', () => HttpResponse.json({ Configurations: [] })),
      http.get('*/api/v1/acme/billing/staging/:segment', ({ params }) =>
        HttpResponse.json({ Annotations: params.segment === 'subjects' ? [annotation('sbt.staging')] : [] }),
      ),
    )
    const catalog = useCatalogStore()

    // a quick switch of the view: the first load is still reading its snapshot when the second starts
    const first = catalog.load(scope)
    const second = catalog.load(staging)
    await Promise.all([first, second])

    expect(asked).toEqual([])
    expect(catalog.scope).toEqual(staging)
    expect(catalog.status).toBe('ready')
    expect([...catalog.byKey.keys()]).toEqual(['sbt.staging'])
    expect(catalog.progress).toMatchObject({ lists: 7, annotations: 1, replacing: false })
  })
})
