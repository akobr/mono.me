/* eslint-disable vue/one-component-per-file -- a host that shows the query on demand, and the query */
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { setupServer } from 'msw/node'
import type { ProjectSettings, ProjectSummary } from '@42for.net/2splatform.sdk'
import { getGetProjectSettingsQueryKey, getGetProjectsQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, resetDb, saveDb } from '@/mock/db'
import { useCurrentProjectSettings } from '@/queries/project-settings'
import { useContextStore } from '@/stores/context'
import { useProjectSettingsStore } from '@/stores/project-settings'
import { legacyProjectSettingsKey } from '@/utils/legacy-project-settings'
import { mountScreen, settle } from '../../helpers'

// The settings of the current project (424-266, spec section 2): read from the API into the store, the first answer
// from the cached list of the caller's projects.

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

const accessMap = createSeed('default').account!.AccessMap as Record<string, string>
const tokyo: ProjectSettings = {
  Types: { exe: { Label: 'Job', Color: 'red' } },
  DefaultTimeZone: 'Asia/Tokyo',
  Version: 2,
}

/** The paths of the settings requests from now on. */
function recordReads() {
  const reads: string[] = []
  server.events.on('request:start', ({ request }) => {
    const path = new URL(request.url).pathname
    if (path.endsWith('/settings')) {
      reads.push(path)
    }
  })
  return reads
}

/** Stores settings of acme.billing in the mock database. */
function storeSettings(settings: ProjectSettings) {
  resetDb('default')
  const db = loadDb()
  db.accessPoints['acme.billing'] = { ...db.accessPoints['acme.billing'], Settings: settings }
  saveDb(db)
}

/** The query in a host that mounts it on demand, so the cache can be filled first. */
async function mountQuery() {
  const shown = ref(false)
  const Query = defineComponent({
    setup() {
      useCurrentProjectSettings()
      return () => h('div')
    },
  })
  const Host = defineComponent({ setup: () => () => (shown.value ? h(Query) : null) })
  const view = await mountScreen(Host, {}, { accessMap })
  mounted.push(view)
  const show = async () => {
    shown.value = true
    await settle()
  }
  return { ...view, show }
}

describe('useCurrentProjectSettings', () => {
  it('reads the settings of the current project into the store', async () => {
    storeSettings(tokyo)
    const reads = recordReads()
    const { show } = await mountQuery()
    useContextStore().set({ org: 'acme', project: 'billing' })
    await show()

    const store = useProjectSettingsStore()
    expect(reads).toEqual(['/api/v1/acme/billing/settings'])
    expect(store.loaded('acme.billing')).toBe(true)
    expect(store.defaultTimeZone).toBe('Asia/Tokyo')
    expect(store.current.types).toEqual({ exe: { label: 'Job', color: 'red' } })
    expect(store.version('acme.billing')).toBe(2)
  })

  it('starts from the cached list of projects, without a read while the list is fresh', async () => {
    resetDb('default')
    const reads = recordReads()
    const { show, queryClient } = await mountQuery()
    const summaries: ProjectSummary[] = [{ Key: 'acme.billing', Role: 'Owner', Settings: tokyo }]
    queryClient.setQueryData(getGetProjectsQueryKey(), summaries)
    useContextStore().set({ org: 'acme', project: 'billing' })
    await show()

    expect(reads).toEqual([])
    expect(useProjectSettingsStore().defaultTimeZone).toBe('Asia/Tokyo')
    expect(queryClient.getQueryData(getGetProjectSettingsQueryKey('acme', 'billing'))).toEqual(tokyo)
  })

  it('reads again when the cached list is older than a minute', async () => {
    storeSettings({ ...tokyo, DefaultTimeZone: 'Europe/Oslo', Version: 3 })
    const reads = recordReads()
    const { show, queryClient } = await mountQuery()
    queryClient.setQueryData(getGetProjectsQueryKey(), [{ Key: 'acme.billing', Role: 'Owner', Settings: tokyo }], {
      updatedAt: Date.now() - 120_000,
    })
    useContextStore().set({ org: 'acme', project: 'billing' })
    await show()

    expect(reads).toEqual(['/api/v1/acme/billing/settings'])
    expect(useProjectSettingsStore().defaultTimeZone).toBe('Europe/Oslo')
  })

  it('follows the project of the context and takes the answers a save writes into the cache', async () => {
    resetDb('default')
    const { show, queryClient } = await mountQuery()
    const context = useContextStore()
    const store = useProjectSettingsStore()
    context.set({ org: 'acme', project: 'billing' })
    await show()
    expect(store.loaded('acme.billing')).toBe(true)
    expect(store.defaultTimeZone).toBe('UTC')

    queryClient.setQueryData(getGetProjectSettingsQueryKey('acme', 'billing'), tokyo)
    expect(store.defaultTimeZone).toBe('Asia/Tokyo')

    context.set({ org: 'acme', project: 'shipping' })
    await settle()
    expect(store.loaded('acme.shipping')).toBe(true)
    expect(store.defaultTimeZone).toBe('UTC')
    expect(store.settingsOf('acme.billing').defaultTimeZone).toBe('Asia/Tokyo')
  })

  it('removes the settings this browser kept for a project once it has settings on the server', async () => {
    localStorage.setItem(
      legacyProjectSettingsKey,
      JSON.stringify({ 'acme.billing': { dateFormat: 'dd.mm.yyyy' }, 'acme.shipping': { dateFormat: 'mm/dd/yyyy' } }),
    )
    resetDb('default')
    const { show, queryClient } = await mountQuery()
    useContextStore().set({ org: 'acme', project: 'billing' })
    await show()

    // Version 0: the offer to save them stays possible.
    expect(JSON.parse(localStorage.getItem(legacyProjectSettingsKey)!)).toHaveProperty(['acme.billing'])

    queryClient.setQueryData(getGetProjectSettingsQueryKey('acme', 'billing'), tokyo)
    expect(JSON.parse(localStorage.getItem(legacyProjectSettingsKey)!)).toEqual({
      'acme.shipping': { dateFormat: 'mm/dd/yyyy' },
    })
  })

  it('reads nothing outside a project or for a project without access', async () => {
    resetDb('default')
    const reads = recordReads()
    const { show } = await mountQuery()
    useContextStore().set({ org: 'acme' })
    await show()

    useContextStore().set({ org: 'acme', project: 'returns' }, 'forbidden')
    await settle()

    expect(reads).toEqual([])
    expect(useProjectSettingsStore().loaded('acme.returns')).toBe(false)
  })
})
