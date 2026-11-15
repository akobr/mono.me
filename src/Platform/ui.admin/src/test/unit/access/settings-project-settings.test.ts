import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { NormalizedProjectSettings } from '@42for.net/2splatform.sdk'
import { getGetProjectSettingsQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import type { VueWrapper } from '@vue/test-utils'
import TimeZoneSelect from '@/components/annotations/TimeZoneSelect.vue'
import TypeAppearanceEditor from '@/components/annotations/TypeAppearanceEditor.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, saveDb } from '@/mock/db'
import { storeProjectSettings } from '@/mock/project-settings'
import SettingsPage from '@/pages/[org]/[project]/settings.vue'
import { useContextStore } from '@/stores/context'
import { mountScreen, settle } from '../../helpers'

// The project settings in Settings (424-266, spec section 3): every member reads them from the API, an Administrator
// changes them in one draft and saves it with one PUT under If-Match of the version; Contributors and Readers see them
// read-only. Against the mock API of the e2e mode.

// the navbar needs the dashboard shell; the page is tested without it
vi.mock('@/components/shell/AppNavbar.vue', () => ({ default: { name: 'AppNavbar', render: () => null } }))

// A zone of the catalog newer than the browser's Intl (an older Safari or Chrome): isValidTimeZone refuses the zones in
// this set.
const unknownZones = vi.hoisted(() => new Set<string>())
vi.mock('@/forms/annotation.schema', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/forms/annotation.schema')>()
  return { ...original, isValidTimeZone: (zone: string) => !unknownZones.has(zone) && original.isValidTimeZone(zone) }
})

const server = setupServer(...mockHandlers)
// unmounted before the body is cleared, so a closing toast does not patch removed nodes
const mounted: { host: { unmount: () => void } }[] = []
function unmountAll() {
  mounted.splice(0).forEach((view) => view.host.unmount())
  document.body.innerHTML = ''
}
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  unmountAll()
  server.events.removeAllListeners()
  server.resetHandlers()
  unknownZones.clear()
})
afterAll(() => server.close())

// Ada owns acme/billing, contributes to acme/shipping and reads northwind/portal.
const accessMap = createSeed('default').account!.AccessMap as Record<string, string>

async function mountSettings(org = 'acme', project = 'billing', hash = '') {
  const view = await mountScreen(
    SettingsPage,
    {},
    { path: `/${org}/${project}/settings${hash}`, scope: { org, project, view: 'default' }, accessMap },
  )
  mounted.push(view)
  // a project page has no view (the context guard sets it from the route)
  useContextStore().set({ org, project })
  await settle(6)
  return view
}

/** Stores settings of a project in the mock database, as someone else would have saved them. */
function storeSettings(projectKey: string, settings: NormalizedProjectSettings) {
  const db = loadDb()
  storeProjectSettings(db, projectKey, settings)
  saveDb(db)
}

/** A card of the page: the section under its heading. */
const card = (title: string) =>
  [...document.body.querySelectorAll('h2')]
    .find((heading) => heading.textContent?.trim() === title)!
    .closest('[data-slot="root"]') as HTMLElement

const buttonNamed = (name: string, within: ParentNode = document.body) =>
  [...within.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === name || button.getAttribute('aria-label') === name,
  )
const saveButton = () => buttonNamed('Save project settings')

const timeZone = (view: { host: VueWrapper }) => view.host.findComponent(TimeZoneSelect)
const typeEditor = (view: { host: VueWrapper }) => view.host.findComponent(TypeAppearanceEditor)
const dateFormat = (view: { host: VueWrapper }) =>
  view.host.findAllComponents({ name: 'Select' }).find((select) => card('Dates').contains(select.element))!

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

const settingsOf = (projectKey: string) => loadDb().accessPoints[projectKey]?.Settings

describe('the project settings in Settings', () => {
  it('lets an Administrator change the three settings and save them with one PUT under If-Match', async () => {
    const writes = recordWrites()
    const view = await mountSettings()

    for (const title of ['Annotations', 'Dates', 'Annotation types']) {
      expect(card(title).textContent).not.toContain('Only Administrators change the project settings.')
    }
    expect(card('Annotations').textContent).toContain('for everyone in this project')
    expect(card('Annotations').textContent).not.toContain('in this browser')
    expect(timeZone(view).props('disabled')).toBe(false)
    expect(typeEditor(view).props('disabled')).toBe(false)
    expect(saveButton()!.disabled).toBe(true)

    timeZone(view).vm.$emit('update:modelValue', 'Asia/Tokyo')
    dateFormat(view).vm.$emit('update:modelValue', 'dd.mm.yyyy')
    typeEditor(view).vm.$emit('update:modelValue', { exe: { label: 'Job', color: 'red' } })
    await settle()

    // The draft is not saved yet: the store and the database keep the saved settings.
    expect(settingsOf('acme.billing')).toBeUndefined()
    expect(document.body.textContent).toContain('Unsaved changes')
    expect(saveButton()!.disabled).toBe(false)

    saveButton()!.click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('Project settings saved'))
    await settle()

    expect(writes).toEqual([
      {
        method: 'PUT',
        ifMatch: '"0"',
        body: {
          Types: { exe: { Label: 'Job', Color: 'red' } },
          DefaultTimeZone: 'Asia/Tokyo',
          DateFormat: 'dd.mm.yyyy',
        },
      },
    ])
    expect(settingsOf('acme.billing')).toMatchObject({
      Types: { exe: { Label: 'Job', Color: 'red' } },
      DefaultTimeZone: 'Asia/Tokyo',
      DateFormat: 'dd.mm.yyyy',
      Version: 1,
    })
    expect(view.queryClient.getQueryData(getGetProjectSettingsQueryKey('acme', 'billing'))).toMatchObject({
      Version: 1,
    })
    expect(timeZone(view).props('modelValue')).toBe('Asia/Tokyo')
    expect(saveButton()!.disabled).toBe(true)
    expect(document.body.textContent).not.toContain('Unsaved changes')

    // The next save is under the new version.
    timeZone(view).vm.$emit('update:modelValue', 'Europe/Oslo')
    await settle()
    saveButton()!.click()
    await vi.waitFor(() => expect(writes).toHaveLength(2))
    await settle()
    expect(writes[1]!.ifMatch).toBe('"1"')
    expect(settingsOf('acme.billing')).toMatchObject({ DefaultTimeZone: 'Europe/Oslo', Version: 2 })
  })

  it('resets the types, the zone and the format in the draft, and saves them as unset', async () => {
    storeSettings('acme.billing', {
      Types: { exe: { Label: 'Job' } },
      DefaultTimeZone: 'Asia/Tokyo',
      DateFormat: 'dd.mm.yyyy',
    })
    const writes = recordWrites()
    const view = await mountSettings()
    expect(timeZone(view).props('modelValue')).toBe('Asia/Tokyo')
    expect(typeEditor(view).props('modelValue')).toEqual({ exe: { label: 'Job' } })

    buttonNamed('Reset to built-in')!.click()
    buttonNamed('Use the default: UTC')!.click()
    buttonNamed('Use the default: yyyy-mm-dd')!.click()
    await settle()

    // Only the draft changed.
    expect(writes).toEqual([])
    expect(typeEditor(view).props('modelValue')).toEqual({})
    expect(timeZone(view).props('modelValue')).toBe('UTC')
    expect(dateFormat(view).props('modelValue')).toBe('yyyy-mm-dd')
    expect(buttonNamed('Reset to built-in')!.disabled).toBe(true)
    expect(buttonNamed('Use the default: UTC')!.disabled).toBe(true)
    expect(buttonNamed('Use the default: yyyy-mm-dd')!.disabled).toBe(true)

    saveButton()!.click()
    await vi.waitFor(() => expect(writes).toHaveLength(1))
    await settle()
    expect(writes[0]).toEqual({ method: 'PUT', ifMatch: '"1"', body: { Types: {} } })
    expect(settingsOf('acme.billing')).toEqual(expect.objectContaining({ Types: {}, Version: 2 }))
    expect(settingsOf('acme.billing')).not.toHaveProperty('DefaultTimeZone')
    expect(settingsOf('acme.billing')).not.toHaveProperty('DateFormat')
  })

  it('keeps the draft on a 412, reads the settings again and loads them on request', async () => {
    const view = await mountSettings()
    timeZone(view).vm.$emit('update:modelValue', 'Asia/Tokyo')
    await settle()

    // Someone else saves meanwhile.
    storeSettings('acme.billing', { Types: { sbt: { Label: 'Company' } }, DateFormat: 'dd.mm.yyyy' })
    saveButton()!.click()
    await vi.waitFor(() =>
      expect(document.body.textContent).toContain(
        'Someone changed the project settings meanwhile; your changes were not saved',
      ),
    )
    await settle()

    expect(timeZone(view).props('modelValue')).toBe('Asia/Tokyo')
    expect(settingsOf('acme.billing')).not.toHaveProperty('DefaultTimeZone')
    expect(view.queryClient.getQueryData(getGetProjectSettingsQueryKey('acme', 'billing'))).toMatchObject({
      Version: 1,
    })
    expect(saveButton()!.disabled).toBe(false)

    buttonNamed('Load the new settings')!.click()
    await settle()
    expect(timeZone(view).props('modelValue')).toBe('UTC')
    expect(dateFormat(view).props('modelValue')).toBe('dd.mm.yyyy')
    expect(typeEditor(view).props('modelValue')).toEqual({ sbt: { label: 'Company' } })
    expect(saveButton()!.disabled).toBe(true)
    expect(document.body.textContent).not.toContain('Someone changed the project settings meanwhile')
  })

  it('saves over the settings of someone else after a 412 with their version', async () => {
    const writes = recordWrites()
    const view = await mountSettings()
    timeZone(view).vm.$emit('update:modelValue', 'Asia/Tokyo')
    await settle()
    storeSettings('acme.billing', { Types: {}, DateFormat: 'dd.mm.yyyy' })

    saveButton()!.click()
    await vi.waitFor(() =>
      expect(document.body.textContent).toContain('Someone changed the project settings meanwhile'),
    )
    await settle()
    saveButton()!.click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('Project settings saved'))
    await vi.waitFor(() => expect(writes).toHaveLength(2))

    expect(writes.map((write) => write.ifMatch)).toEqual(['"0"', '"1"'])
    expect(settingsOf('acme.billing')).toMatchObject({ DefaultTimeZone: 'Asia/Tokyo', Version: 2 })
    expect(document.body.textContent).not.toContain('Someone changed the project settings meanwhile')
  })

  it('saves under the version the draft started from, so a change read again meanwhile gets a 412', async () => {
    const writes = recordWrites()
    const view = await mountSettings()
    typeEditor(view).vm.$emit('update:modelValue', { exe: { label: 'Job' } })
    await settle()

    // Someone else saves a date format, and the query reads the settings again (a reconnect refetches a stale query).
    storeSettings('acme.billing', { Types: {}, DateFormat: 'dd.mm.yyyy' })
    await view.queryClient.refetchQueries({ queryKey: getGetProjectSettingsQueryKey('acme', 'billing') })
    await settle()
    expect(view.queryClient.getQueryData(getGetProjectSettingsQueryKey('acme', 'billing'))).toMatchObject({
      Version: 1,
    })
    // The draft stays as it was, with the date format it started from.
    expect(typeEditor(view).props('modelValue')).toEqual({ exe: { label: 'Job' } })
    expect(dateFormat(view).props('modelValue')).toBe('yyyy-mm-dd')
    expect(document.body.textContent).not.toContain('Someone changed the project settings meanwhile')

    saveButton()!.click()
    await vi.waitFor(() =>
      expect(document.body.textContent).toContain(
        'Someone changed the project settings meanwhile; your changes were not saved',
      ),
    )
    await settle()
    expect(writes.map((write) => write.ifMatch)).toEqual(['"0"'])
    expect(settingsOf('acme.billing')).toMatchObject({ Types: {}, DateFormat: 'dd.mm.yyyy', Version: 1 })
    expect(typeEditor(view).props('modelValue')).toEqual({ exe: { label: 'Job' } })

    // Told of the change, a second Save stores the draft over it.
    saveButton()!.click()
    await vi.waitFor(() => expect(writes).toHaveLength(2))
    await settle()
    expect(writes[1]!.ifMatch).toBe('"1"')
    expect(settingsOf('acme.billing')).toMatchObject({ Types: { exe: { Label: 'Job' } }, Version: 2 })
  })

  it('keeps a stored zone this browser does not know when it saves another change', async () => {
    unknownZones.add('America/Coyhaique')
    storeSettings('acme.billing', { Types: {}, DefaultTimeZone: 'America/Coyhaique' })
    const writes = recordWrites()
    const view = await mountSettings()

    expect(timeZone(view).props('modelValue')).toBe('America/Coyhaique')
    expect(buttonNamed('Use the default: UTC')!.disabled).toBe(false)

    typeEditor(view).vm.$emit('update:modelValue', { exe: { label: 'Job' } })
    await settle()
    saveButton()!.click()
    await vi.waitFor(() => expect(writes).toHaveLength(1))
    await settle()

    expect(writes[0]!.body).toEqual({ Types: { exe: { Label: 'Job' } }, DefaultTimeZone: 'America/Coyhaique' })
    expect(settingsOf('acme.billing')).toMatchObject({ DefaultTimeZone: 'America/Coyhaique', Version: 2 })
  })

  it('lists the problems of a 400 InvalidSettings and keeps the draft', async () => {
    server.use(
      http.put('*/api/v1/:org/:project/settings', () =>
        HttpResponse.json(
          {
            Message: 'The project settings have 2 problems; the first is $.Types.exe.Color: …',
            ErrorCode: 'InvalidSettings',
            Details: [
              { Path: '$.Types.exe.Color', Rule: 'color', Message: "'red' is not a color of the catalog." },
              { Path: '$.DefaultTimeZone', Rule: 'time-zone', Message: "'Asia/Tokyo' is not a time zone." },
            ],
          },
          { status: 400 },
        ),
      ),
    )
    const view = await mountSettings()
    timeZone(view).vm.$emit('update:modelValue', 'Asia/Tokyo')
    await settle()

    saveButton()!.click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('The project settings were not saved'))
    await settle()

    const text = document.body.textContent!
    expect(text).toContain("$.Types.exe.Color 'red' is not a color of the catalog.")
    expect(text).toContain("$.DefaultTimeZone 'Asia/Tokyo' is not a time zone.")
    expect(timeZone(view).props('modelValue')).toBe('Asia/Tokyo')
    expect(saveButton()!.disabled).toBe(false)
    expect(settingsOf('acme.billing')).toBeUndefined()
  })

  it('tells of any other failure and keeps the draft', async () => {
    server.use(
      http.put('*/api/v1/:org/:project/settings', () =>
        HttpResponse.json({ Message: 'Storage failed.' }, { status: 500 }),
      ),
    )
    const view = await mountSettings()
    dateFormat(view).vm.$emit('update:modelValue', 'mm/dd/yyyy')
    await settle()

    saveButton()!.click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('The project settings were not saved'))
    expect(document.body.textContent).toContain('Storage failed.')
    expect(dateFormat(view).props('modelValue')).toBe('mm/dd/yyyy')
    expect(saveButton()!.disabled).toBe(false)
  })

  it('starts the draft again from the saved settings of another project', async () => {
    storeSettings('acme.shipping', { Types: {}, DefaultTimeZone: 'Europe/Oslo' })
    const db = loadDb()
    db.account!.AccessMap = { ...db.account!.AccessMap, 'acme.shipping': 'Owner' }
    saveDb(db)
    const view = await mountScreen(
      SettingsPage,
      {},
      {
        path: '/acme/billing/settings',
        scope: { org: 'acme', project: 'billing', view: 'default' },
        accessMap: { ...accessMap, 'acme.shipping': 'Owner' },
      },
    )
    mounted.push(view)
    useContextStore().set({ org: 'acme', project: 'billing' })
    await settle(6)

    timeZone(view).vm.$emit('update:modelValue', 'Asia/Tokyo')
    await settle()
    const writes = recordWrites()
    useContextStore().set({ org: 'acme', project: 'shipping' })
    await settle(6)

    expect(timeZone(view).props('modelValue')).toBe('Europe/Oslo')
    expect(saveButton()!.disabled).toBe(true)

    // A save there is under that project's version.
    dateFormat(view).vm.$emit('update:modelValue', 'dd.mm.yyyy')
    await settle()
    saveButton()!.click()
    await vi.waitFor(() => expect(writes).toHaveLength(1))
    await settle()
    expect(writes[0]).toMatchObject({
      ifMatch: '"1"',
      body: { DefaultTimeZone: 'Europe/Oslo', DateFormat: 'dd.mm.yyyy' },
    })
    expect(settingsOf('acme.billing')).toBeUndefined()
  })

  it.each([
    ['a Contributor', 'acme', 'shipping'],
    ['a Reader', 'northwind', 'portal'],
  ])('shows %s the settings read-only with the Administrator badge, and sends nothing', async (_, org, project) => {
    storeSettings(`${org}.${project}`, {
      Types: { exe: { Label: 'Job' } },
      DefaultTimeZone: 'Asia/Tokyo',
      DateFormat: 'dd.mm.yyyy',
    })
    const writes = recordWrites()
    const view = await mountSettings(org, project)

    for (const title of ['Annotations', 'Dates', 'Annotation types']) {
      const section = card(title)
      expect(section.querySelector('[data-slot="header"]')?.textContent, title).toContain('Administrator')
      expect(section.textContent, title).toContain('Only Administrators change the project settings.')
    }
    expect(timeZone(view).props('modelValue')).toBe('Asia/Tokyo')
    expect(timeZone(view).props('disabled')).toBe(true)
    expect(dateFormat(view).props('modelValue')).toBe('dd.mm.yyyy')
    expect(dateFormat(view).props('disabled')).toBe(true)
    expect(typeEditor(view).props('modelValue')).toEqual({ exe: { label: 'Job' } })
    expect(typeEditor(view).props('disabled')).toBe(true)
    expect(saveButton()).toBeUndefined()
    expect(buttonNamed('Reset to built-in')).toBeUndefined()
    expect(buttonNamed('Use the default: UTC')).toBeUndefined()
    expect(buttonNamed('Use the default: yyyy-mm-dd')).toBeUndefined()

    await settle()
    expect(writes).toEqual([])
  })

  it('shows the defaults read-only until the settings are loaded, and their error with a retry', async () => {
    server.use(
      http.get(
        '*/api/v1/:org/:project/settings',
        () => HttpResponse.json({ Message: 'Storage failed.' }, { status: 500 }),
        { once: true },
      ),
    )
    storeSettings('acme.billing', { Types: {}, DefaultTimeZone: 'Asia/Tokyo' })
    const view = await mountSettings()

    expect(document.body.textContent).toContain('The project settings could not be loaded')
    expect(timeZone(view).props('modelValue')).toBe('UTC')
    expect(timeZone(view).props('disabled')).toBe(true)
    expect(saveButton()!.disabled).toBe(true)

    buttonNamed('Retry', document.body)!.click()
    await vi.waitFor(() => expect(timeZone(view).props('modelValue')).toBe('Asia/Tokyo'))
    expect(timeZone(view).props('disabled')).toBe(false)
    expect(document.body.textContent).not.toContain('The project settings could not be loaded')
  })

  it('scrolls to #secrets only once the settings have loaded', async () => {
    const scrolls: string[] = []
    const original = HTMLElement.prototype.scrollIntoView
    HTMLElement.prototype.scrollIntoView = function (this: HTMLElement) {
      scrolls.push(this.id)
    }
    let answer = () => {}
    server.use(
      // The list carries no settings, so the page reads them.
      http.get('*/api/v1/access/projects', () =>
        HttpResponse.json([
          { Key: 'acme.billing', Role: 'Owner' },
          { Key: 'acme.shipping', Role: 'Contributor' },
          { Key: 'northwind.portal', Role: 'Reader' },
        ]),
      ),
      http.get('*/api/v1/:org/:project/settings', async () => {
        await new Promise<void>((resolve) => (answer = resolve))
        return HttpResponse.json({ Types: {}, Version: 0 })
      }),
    )

    try {
      await mountSettings('acme', 'billing', '#secrets')
      expect(scrolls).toEqual([])

      answer()
      await vi.waitFor(() => expect(scrolls).toEqual(['secrets']))
    } finally {
      HTMLElement.prototype.scrollIntoView = original
    }
  })
})
