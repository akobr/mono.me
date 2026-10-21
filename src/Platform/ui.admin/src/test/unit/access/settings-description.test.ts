import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { delay, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { AccessPointUpdate } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, saveDb } from '@/mock/db'
import SettingsPage from '@/pages/[org]/[project]/settings.vue'
import { useContextStore } from '@/stores/context'
import { mountScreen, settle } from '../../helpers'

// The description of a project in its Settings (424-78, spec section 7): every member reads it, an Administrator
// changes it, against the mock API of the e2e mode.

// the navbar needs the dashboard shell; the page is tested without it
vi.mock('@/components/shell/AppNavbar.vue', () => ({ default: { name: 'AppNavbar', render: () => null } }))

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
})
afterAll(() => server.close())

const seed = createSeed('default')
// Ada owns acme/billing and reads northwind/portal.
const accessMap = seed.account!.AccessMap as Record<string, string>
const seeded = (key: string) => seed.accessPoints[key]!.Description!

async function mountSettings(org = 'acme', project = 'billing', hash = '', map = accessMap) {
  const view = await mountScreen(
    SettingsPage,
    {},
    { path: `/${org}/${project}/settings${hash}`, scope: { org, project, view: 'default' }, accessMap: map },
  )
  mounted.push(view)
  // a project page has no view (the context guard sets it from the route)
  useContextStore().set({ org, project })
  await settle(6)
  return view
}

/** The Project card: the section of the page under its heading. */
const projectCard = () =>
  [...document.body.querySelectorAll('h2')]
    .find((heading) => heading.textContent?.trim() === 'Project')!
    .closest('[data-slot="root"]') as HTMLElement

const descriptionField = () => projectCard().querySelector('textarea')
const saveButton = () =>
  [...projectCard().querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Save description')

async function type(text: string) {
  const field = descriptionField()!
  field.value = text
  field.dispatchEvent(new Event('input'))
  await settle()
}

/** The bodies of the description updates from now on. */
function recordUpdates() {
  const updates: AccessPointUpdate[] = []
  server.events.on('request:start', async ({ request }) => {
    if (request.method === 'PUT' && new URL(request.url).pathname.endsWith('/access/points/acme.billing')) {
      const text = await request.clone().text()
      updates.push(text ? (JSON.parse(text) as AccessPointUpdate) : {})
    }
  })
  return updates
}

describe('the description in Settings', () => {
  it('lets an Administrator change it and clear it', async () => {
    const updates = recordUpdates()
    await mountSettings()

    expect(descriptionField()!.value).toBe(seeded('acme.billing'))
    expect(saveButton()!.disabled).toBe(true)

    // The same text with spaces around it is no change.
    await type(`  ${seeded('acme.billing')}  `)
    expect(saveButton()!.disabled).toBe(true)

    await type('  Invoices and payments of Acme.  ')
    expect(saveButton()!.disabled).toBe(false)
    saveButton()!.click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('Description saved'))
    await settle()

    expect(updates).toEqual([{ Description: 'Invoices and payments of Acme.' }])
    expect(loadDb().accessPoints['acme.billing']?.Description).toBe('Invoices and payments of Acme.')
    // The text shows the stored description, and the saved text is no change.
    expect(descriptionField()!.value).toBe('Invoices and payments of Acme.')
    expect(saveButton()!.disabled).toBe(true)

    await type('   ')
    saveButton()!.click()
    await vi.waitFor(() => expect(updates).toHaveLength(2))
    await settle()

    expect(updates[1]).toEqual({})
    expect(loadDb().accessPoints['acme.billing']).not.toHaveProperty('Description')
    expect(descriptionField()!.value).toBe('')
    expect(saveButton()!.disabled).toBe(true)
  })

  it('keeps the text typed during the save', async () => {
    let answer = () => {}
    server.use(
      http.put('*/api/v1/access/points/:key', async () => {
        await new Promise<void>((resolve) => (answer = resolve))
      }),
    )
    await mountSettings()

    await type('  Invoices of Acme.  ')
    saveButton()!.click()
    await settle()
    await type('Invoices of Acme, and more.')
    answer()
    await vi.waitFor(() => expect(document.body.textContent).toContain('Description saved'))
    await settle()

    expect(descriptionField()!.value).toBe('Invoices of Acme, and more.')
  })

  // The context switcher keeps the section (utils/context-paths.ts), so the same page shows the other project.
  it('fills the text of the other project when the project changes', async () => {
    const db = loadDb()
    db.accessPoints['acme.returns'] = {
      Key: 'acme.returns',
      AccessMap: { [db.account!.Id!]: 'Owner' },
      Description: 'Returns of Acme.',
    }
    db.account!.AccessMap = { ...db.account!.AccessMap, 'acme.returns': 'Owner' }
    saveDb(db)
    const view = await mountSettings('acme', 'billing', '', { ...accessMap, 'acme.returns': 'Owner' })

    await type('A draft for billing.')
    expect(saveButton()!.disabled).toBe(false)

    await view.router.push('/acme/returns/settings')
    useContextStore().set({ org: 'acme', project: 'returns' })
    await settle(6)

    expect(descriptionField()!.value).toBe('Returns of Acme.')
    expect(saveButton()!.disabled).toBe(true)
  })

  it('keeps the text when the save fails', async () => {
    server.use(
      http.put('*/api/v1/access/points/:key', () =>
        HttpResponse.json({ Message: 'The project changed meanwhile.' }, { status: 409 }),
      ),
    )
    await mountSettings()

    await type('Invoices of Acme.')
    saveButton()!.click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('The description was not saved'))

    expect(document.body.textContent).toContain('The project changed meanwhile.')
    expect(descriptionField()!.value).toBe('Invoices of Acme.')
    expect(loadDb().accessPoints['acme.billing']?.Description).toBe(seeded('acme.billing'))
  })

  it('shows the error of a text over 500 characters and does not save it', async () => {
    const updates = recordUpdates()
    await mountSettings()

    await type('x'.repeat(500))
    expect(projectCard().textContent).not.toContain('At most 500 characters.')
    expect(saveButton()!.disabled).toBe(false)

    await type('x'.repeat(501))
    expect(projectCard().textContent).toContain('At most 500 characters.')
    expect(saveButton()!.disabled).toBe(true)

    const form = projectCard().querySelector('form')!
    form.dispatchEvent(new Event('submit', { cancelable: true }))
    await settle()
    expect(updates).toEqual([])
    expect(loadDb().accessPoints['acme.billing']?.Description).toBe(seeded('acme.billing'))
  })

  it('shows it to a Reader without the form', async () => {
    await mountSettings('northwind', 'portal')

    expect(projectCard().textContent).toContain(seeded('northwind.portal'))
    expect(projectCard().textContent).not.toContain('No description')
    expect(descriptionField()).toBeNull()
    expect(saveButton()).toBeUndefined()
  })

  it('tells a Reader that the project has no description', async () => {
    const db = loadDb()
    delete db.accessPoints['northwind.portal']!.Description
    saveDb(db)
    await mountSettings('northwind', 'portal')

    expect(projectCard().textContent).toContain('No description')
    expect(descriptionField()).toBeNull()
  })

  it('shows the loading of the list, not "No description"', async () => {
    server.use(http.get('*/api/v1/access/projects', () => delay('infinite')))
    await mountSettings('northwind', 'portal')

    expect(projectCard().querySelector('[role="status"]')?.textContent).toContain('Loading…')
    expect(projectCard().textContent).not.toContain('No description')

    unmountAll()
    await mountSettings()
    expect(projectCard().querySelector('[role="status"]')?.textContent).toContain('Loading…')
    expect(descriptionField()).toBeNull()
  })

  it('shows the error of the list with a retry, not "No description"', async () => {
    server.use(
      http.get('*/api/v1/access/projects', () => HttpResponse.json({ Message: 'Storage failed.' }, { status: 500 }), {
        once: true,
      }),
    )
    await mountSettings('northwind', 'portal')

    expect(projectCard().textContent).toContain('Storage failed.')
    expect(projectCard().textContent).not.toContain('No description')

    const retry = [...projectCard().querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Retry')
    retry!.click()
    await vi.waitFor(() => expect(projectCard().textContent).toContain(seeded('northwind.portal')))
  })

  it('scrolls to #secrets only once the list has loaded', async () => {
    const scrolls: string[] = []
    const original = HTMLElement.prototype.scrollIntoView
    HTMLElement.prototype.scrollIntoView = function (this: HTMLElement) {
      scrolls.push(this.id)
    }
    let answer = () => {}
    server.use(
      http.get('*/api/v1/access/projects', async () => {
        await new Promise<void>((resolve) => (answer = resolve))
        return HttpResponse.json([
          { Key: 'acme.billing', Role: 'Owner', Description: 'Invoices.' },
          { Key: 'acme.shipping', Role: 'Contributor' },
          { Key: 'northwind.portal', Role: 'Reader' },
        ])
      }),
    )

    try {
      await mountSettings('acme', 'billing', '#secrets')
      expect(scrolls).toEqual([])

      answer()
      await vi.waitFor(() => expect(scrolls).toEqual(['secrets']))
      expect(descriptionField()!.value).toBe('Invoices.')
    } finally {
      HTMLElement.prototype.scrollIntoView = original
    }
  })
})
