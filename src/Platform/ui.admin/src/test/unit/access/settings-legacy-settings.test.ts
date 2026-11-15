import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { setupServer } from 'msw/node'
import { getGetProjectSettingsQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, saveDb } from '@/mock/db'
import { storeProjectSettings } from '@/mock/project-settings'
import SettingsPage from '@/pages/[org]/[project]/settings.vue'
import { useContextStore } from '@/stores/context'
import { legacyProjectSettingsKey } from '@/utils/legacy-project-settings'
import { mountScreen, settle } from '../../helpers'

// The project settings this browser kept before the API stored them (424-266, spec section 4): while the project has
// none on the server, an Administrator saves them for the project or discards them; anyone else is asked to have an
// Administrator save them. Against the mock API of the e2e mode.

// the navbar needs the dashboard shell; the page is tested without it
vi.mock('@/components/shell/AppNavbar.vue', () => ({ default: { name: 'AppNavbar', render: () => null } }))

const server = setupServer(...mockHandlers)
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  document.body.innerHTML = ''
  server.events.removeAllListeners()
  server.resetHandlers()
})
afterAll(() => server.close())

// Ada owns acme/billing, contributes to acme/shipping and reads northwind/portal.
const accessMap = createSeed('default').account!.AccessMap as Record<string, string>

const oldSettings = {
  'acme.billing': {
    types: { exe: { label: 'Job', color: 'red' } },
    defaultTimeZone: 'Asia/Tokyo',
    dateFormat: 'dd.mm.yyyy',
  },
  'acme.shipping': { defaultTimeZone: 'Europe/Oslo' },
}
const storeOldSettings = () => localStorage.setItem(legacyProjectSettingsKey, JSON.stringify(oldSettings))
const storedOldSettings = () =>
  JSON.parse(localStorage.getItem(legacyProjectSettingsKey) ?? '{}') as Record<string, unknown>

async function mountSettings(org = 'acme', project = 'billing') {
  const view = await mountScreen(
    SettingsPage,
    {},
    { path: `/${org}/${project}/settings`, scope: { org, project, view: 'default' }, accessMap },
  )
  mounted.push(view)
  useContextStore().set({ org, project })
  await settle(6)
  return view
}

const intro = 'These project settings were set in this browser before they were shared.'
const banner = () =>
  [...document.body.querySelectorAll('[role="alert"], [data-slot="root"]')].find(
    (element) => element.textContent?.includes(intro) && !element.querySelector('h2'),
  ) as HTMLElement | undefined
const buttonNamed = (name: string) =>
  [...document.body.querySelectorAll('button')].find((button) => button.textContent?.trim() === name)

/** The settings writes from now on: method, If-Match and body. */
function recordWrites() {
  const writes: { method: string; ifMatch: string | null; body: unknown }[] = []
  server.events.on('request:start', async ({ request }) => {
    if (request.method !== 'GET' && new URL(request.url).pathname.endsWith('/settings')) {
      const text = await request.clone().text()
      writes.push({
        method: request.method,
        ifMatch: request.headers.get('If-Match'),
        body: text ? JSON.parse(text) : undefined,
      })
    }
  })
  return writes
}

describe('the project settings of this browser in Settings', () => {
  it('lets an Administrator save them for the project under If-Match "0"', async () => {
    storeOldSettings()
    const writes = recordWrites()
    const view = await mountSettings()

    expect(banner()?.textContent).toContain('Save them for the whole project?')
    expect(banner()?.textContent).toContain(
      'Time zone Asia/Tokyo, date format dd.mm.yyyy, names, colors or icons of 1 annotation type.',
    )

    buttonNamed('Save for the project')!.click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('Project settings saved'))
    await vi.waitFor(() => expect(writes).toHaveLength(1))
    await settle()

    expect(writes[0]).toEqual({
      method: 'PUT',
      ifMatch: '"0"',
      body: { Types: { exe: { Label: 'Job', Color: 'red' } }, DefaultTimeZone: 'Asia/Tokyo', DateFormat: 'dd.mm.yyyy' },
    })
    expect(loadDb().accessPoints['acme.billing']?.Settings).toMatchObject({
      Types: { exe: { Label: 'Job', Color: 'red' } },
      DefaultTimeZone: 'Asia/Tokyo',
      DateFormat: 'dd.mm.yyyy',
      Version: 1,
    })
    expect(view.queryClient.getQueryData(getGetProjectSettingsQueryKey('acme', 'billing'))).toMatchObject({
      Version: 1,
    })
    // The entry of this project goes, the others stay.
    expect(storedOldSettings()).toEqual({ 'acme.shipping': oldSettings['acme.shipping'] })
    expect(banner()).toBeUndefined()
    // The page shows what is stored.
    expect(view.host.findComponent({ name: 'TimeZoneSelect' }).props('modelValue')).toBe('Asia/Tokyo')
  })

  it('lets an Administrator discard them without a request', async () => {
    storeOldSettings()
    const writes = recordWrites()
    await mountSettings()

    buttonNamed('Discard')!.click()
    await settle()

    expect(banner()).toBeUndefined()
    expect(storedOldSettings()).toEqual({ 'acme.shipping': oldSettings['acme.shipping'] })
    expect(writes).toEqual([])
    expect(loadDb().accessPoints['acme.billing']?.Settings).toBeUndefined()
  })

  it('asks a Contributor to have an Administrator save them, with no action', async () => {
    storeOldSettings()
    const writes = recordWrites()
    await mountSettings('acme', 'shipping')

    expect(banner()?.textContent).toContain('Ask an Administrator of the project to save them for everyone.')
    expect(banner()?.textContent).toContain('Time zone Europe/Oslo.')
    expect(banner()?.textContent).not.toContain('Save them for the whole project?')
    expect(buttonNamed('Save for the project')).toBeUndefined()
    expect(buttonNamed('Discard')).toBeUndefined()

    await settle()
    expect(writes).toEqual([])
    expect(storedOldSettings()).toEqual(oldSettings)
  })

  it('removes them without an offer once the project has settings on the server', async () => {
    storeOldSettings()
    const db = loadDb()
    storeProjectSettings(db, 'acme.billing', { Types: {}, DateFormat: 'mm/dd/yyyy' })
    saveDb(db)
    await mountSettings()

    expect(banner()).toBeUndefined()
    expect(storedOldSettings()).toEqual({ 'acme.shipping': oldSettings['acme.shipping'] })
  })

  it('shows no offer for an entry that holds only defaults', async () => {
    localStorage.setItem(
      legacyProjectSettingsKey,
      JSON.stringify({ 'acme.billing': { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' } }),
    )
    await mountSettings()

    expect(banner()).toBeUndefined()
  })

  it('tells of a save refused because someone saved settings meanwhile, and removes them', async () => {
    storeOldSettings()
    await mountSettings()
    expect(banner()).toBeDefined()

    // Someone else saves first: the save under If-Match "0" gets a 412, the read again has settings.
    const db = loadDb()
    storeProjectSettings(db, 'acme.billing', { Types: {}, DateFormat: 'mm/dd/yyyy' })
    saveDb(db)
    buttonNamed('Save for the project')!.click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('The project settings were not saved'))
    await vi.waitFor(() => expect(banner()).toBeUndefined())

    expect(document.body.textContent).toContain('they are at version 1')
    expect(loadDb().accessPoints['acme.billing']?.Settings).toMatchObject({ DateFormat: 'mm/dd/yyyy', Version: 1 })
    expect(storedOldSettings()).toEqual({ 'acme.shipping': oldSettings['acme.shipping'] })
  })
})
