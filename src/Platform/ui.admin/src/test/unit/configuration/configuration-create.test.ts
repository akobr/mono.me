import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import type { VueWrapper } from '@vue/test-utils'
import { http } from 'msw'
import { setupServer } from 'msw/node'
import type { Annotation, ConfigurationSummary } from '@42for.net/2splatform.sdk'
import ConfigurationCreateDialog from '@/components/configuration/ConfigurationCreateDialog.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { ownContent } from '@/mock/configuration-handlers'
import { annotationsOf, loadDb } from '@/mock/db'
import { useCatalogStore } from '@/stores/catalog'
import { keysWithoutConfiguration } from '@/utils/configuration-origin'
import { mountScreen, settle } from '../../helpers'

// New configuration (424-42) against the stateful mock API of the e2e mode, served by msw/node.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }

const annotation = (key: string, extra: Partial<Annotation> = {}) => ({ AnnotationKey: key, ...extra }) as Annotation
const summaries = (entries: [string, Partial<ConfigurationSummary>][]) =>
  new Map(entries.map(([key, summary]) => [key, { AnnotationKey: key, ...summary } as ConfigurationSummary]))

describe('keysWithoutConfiguration', () => {
  it('lists the annotations without a summary, sorted', () => {
    const annotations = [annotation('usg.northwind.invoicing'), annotation('sbt.northwind'), annotation('rst.payments')]

    expect(keysWithoutConfiguration(annotations, summaries([['usg.northwind.invoicing', { Version: 7 }]]))).toEqual([
      'rst.payments',
      'sbt.northwind',
    ])
  })

  it('counts a summary of an empty document as a configuration', () => {
    const annotations = [annotation('sbt.northwind'), annotation('sbt.contoso')]

    expect(keysWithoutConfiguration(annotations, summaries([['sbt.contoso', { HasContent: false }]]))).toEqual([
      'sbt.northwind',
    ])
  })

  it('leaves out deleted annotations and keeps the inactive ones', () => {
    const annotations = [
      annotation('sbt.northwind', { Deletion: { DeletedAt: '2026-10-16T08:00:00Z' } }),
      annotation('sbt.contoso', { IsDisabled: true, ExpiresAt: '2026-01-01T00:00:00Z' }),
    ]

    expect(keysWithoutConfiguration(annotations, new Map())).toEqual(['sbt.contoso'])
  })

  it('knows nothing while the summaries are unknown', () => {
    expect(keysWithoutConfiguration([annotation('sbt.northwind')], null)).toBeNull()
  })
})

describe('ConfigurationCreateDialog', () => {
  // A failed test must not leave its teleported dialog in the body for the next one.
  let unmount: (() => void) | undefined
  beforeEach(async () => {
    await useCatalogStore().forgetSnapshot(scope)
  })
  afterEach(() => {
    unmount?.()
    unmount = undefined
  })

  async function openDialog(options: { load?: boolean } = {}) {
    if (options.load !== false) {
      await useCatalogStore().load(scope)
    }
    const closed: boolean[] = []
    const picked: string[] = []
    const screen = await mountScreen(ConfigurationCreateDialog, {
      open: true,
      'onUpdate:open': (value: boolean) => closed.push(!value),
      onPicked: (key: string) => picked.push(key),
    })
    unmount = () => screen.host.unmount()
    await settle(6)
    const select = () => screen.inner().findComponent({ name: 'SelectMenu' }) as VueWrapper
    return { closed, picked, select }
  }

  // The modal is teleported to the body.
  const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')!
  const text = () => dialog().textContent!.replace(/\s+/g, ' ')
  const button = (label: string) =>
    [...document.body.querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)
  const offered = (select: VueWrapper) => (select.props('items') as { value: string }[]).map((item) => item.value)

  /** The keys of the seed without own content, read from the mock database rather than from the catalog. */
  function unconfiguredKeys(): string[] {
    const db = loadDb()
    return [...annotationsOf(db, scope.org, scope.project, scope.view).keys()]
      .filter((key) => ownContent(db, scope, key) === undefined)
      .toSorted((a, b) => a.localeCompare(b))
  }

  it('offers every annotation of the view without a configuration', async () => {
    const { select } = await openDialog()

    expect(text()).toContain('New configuration')
    expect(text()).toContain('In acme/billing, view default.')
    const keys = offered(select())
    expect(keys).toEqual(unconfiguredKeys())
    expect(keys).toContain('exe.northwind.invoicing.prod')
    expect(keys).not.toContain('usg.northwind.invoicing')
    expect(select().props('disabled')).toBe(false)
  })

  it('hands the picked key to the page and closes', async () => {
    const { closed, picked, select } = await openDialog()
    expect(button('Open editor')!.disabled).toBe(true)

    select().vm.$emit('update:modelValue', 'exe.northwind.invoicing.prod')
    await settle()
    expect(button('Open editor')!.disabled).toBe(false)

    const form = dialog().querySelector('form')!
    form.dispatchEvent(new Event('submit', { cancelable: true }))
    await settle()

    expect(picked).toEqual(['exe.northwind.invoicing.prod'])
    expect(closed).toEqual([true])
  })

  it('says so when every annotation has a configuration', async () => {
    const catalog = useCatalogStore()
    await catalog.load(scope)
    for (const key of catalog.byKey.keys()) {
      catalog.setConfiguration(key, { AnnotationKey: key, Version: 1, HasContent: true })
    }

    const { select } = await openDialog({ load: false })

    expect(text()).toContain('Every annotation of the view has a configuration.')
    expect(select().exists()).toBe(false)
    expect(button('Open editor')!.disabled).toBe(true)
  })

  it('waits for the catalog before it lists anything', async () => {
    // The configuration summaries never answer, so the catalog stays incomplete.
    server.use(http.get('*/api/v1/:org/:project/:view/configurations', () => new Promise<Response>(() => {})))
    const catalog = useCatalogStore()
    void catalog.load(scope)

    const { select } = await openDialog({ load: false })

    expect(catalog.configurations).toBeNull()
    expect(select().props('disabled')).toBe(true)
    expect(select().props('placeholder')).toBe('Loading the catalog…')
    expect(offered(select())).toEqual([])
  })
})
