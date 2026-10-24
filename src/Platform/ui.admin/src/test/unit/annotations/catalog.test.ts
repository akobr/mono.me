import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { useCatalogStore } from '@/stores/catalog'

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
