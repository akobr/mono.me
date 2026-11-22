import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, mockUser, resetDb, saveDb, type MockDb } from '@/mock/db'
import SettingsPage from '@/pages/[org]/[project]/settings.vue'
import { useContextStore } from '@/stores/context'
import { useSessionStore } from '@/stores/session'
import { mountScreen, settle } from '../../helpers'

// Leave project in the Project card of Settings (424-271, spec section 3), against the mock API of the e2e mode: in the
// default dataset Ada is a Contributor of acme/shipping, the only Owner of acme/billing and a Reader of northwind/portal.

// the navbar needs the dashboard shell; the page is tested without it
vi.mock('@/components/shell/AppNavbar.vue', () => ({ default: { name: 'AppNavbar', render: () => null } }))

const server = setupServer(...mockHandlers)
const mounted: { host: { unmount: () => void } }[] = []
let requests: string[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => {
  requests = []
  server.events.on('request:start', ({ request }) => {
    requests.push(`${request.method} ${new URL(request.url).pathname.replace(/^\/api\/v1/, '')}`)
  })
})
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  document.body.innerHTML = ''
  server.events.removeAllListeners()
  server.resetHandlers()
  // The toasts are global state of Nuxt UI: each test reads its own.
  createApp({}).runWithContext(() => useToast().clear())
})
afterAll(() => server.close())

const eventually = (assertion: () => void) => vi.waitFor(assertion, { timeout: 5_000 })
const text = (element: Element | null | undefined) => (element?.textContent ?? '').replaceAll(/\s+/g, ' ').trim()
const body = () => text(document.body)
const buttons = (root: ParentNode = document.body) => [...root.querySelectorAll<HTMLButtonElement>('button')]
const button = (name: string, root?: ParentNode) => buttons(root).find((candidate) => text(candidate) === name)
const leaveButton = () => button('Leave project', projectCard())
const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')
const deletes = () => requests.filter((request) => request.startsWith('DELETE'))

/** The Project card: the section of the page under its heading. */
const projectCard = () =>
  [...document.body.querySelectorAll('h2')]
    .find((heading) => text(heading) === 'Project')
    ?.closest<HTMLElement>('[data-slot="root"]') ?? undefined

/** Settings of the project with the account of the default dataset, after `change` of its data. */
async function mountSettings(org: string, project: string, change?: (db: MockDb) => void) {
  const db = resetDb('default')
  change?.(db)
  saveDb(db)
  const view = await mountScreen(
    SettingsPage,
    {},
    { path: `/${org}/${project}/settings`, scope: { org, project, view: 'default' }, account: loadDb().account },
  )
  mounted.push(view)
  // a project page has no view (the context guard sets it from the route)
  useContextStore().set({ org, project })
  await settle(6)
  await eventually(() => expect(leaveButton()).toBeTruthy())
  return { ...view, route: () => view.router.currentRoute.value }
}

async function confirmLeave() {
  leaveButton()!.click()
  await eventually(() => expect(dialog()).toBeTruthy())
  button('Leave project', dialog()!)!.click()
}

describe('Leave project in Settings', () => {
  it('asks first, and Cancel sends nothing', async () => {
    await mountSettings('acme', 'shipping')

    expect(text(projectCard())).toContain('Leave the project to remove it from your organizations.')
    expect(leaveButton()!.disabled).toBe(false)
    leaveButton()!.click()
    await eventually(() => expect(dialog()).toBeTruthy())

    expect(text(dialog())).toContain('Leave acme.shipping?')
    expect(text(dialog())).toContain(
      'You lose your Contributor role on acme.shipping. To come back, you need a new invitation.',
    )
    button('Cancel', dialog()!)!.click()
    await eventually(() => expect(dialog()).toBeNull())
    expect(deletes()).toEqual([])
    expect(useSessionStore().accessMap).toHaveProperty(['acme.shipping'], 'Contributor')
  })

  it('removes the own membership, reads the account again and ends on Organizations', async () => {
    const view = await mountSettings('acme', 'shipping')

    await confirmLeave()

    await eventually(() => expect(view.route().path).toBe('/orgs'))
    expect(deletes()).toEqual([`DELETE /access/points/acme.shipping/members/${mockUser.id}`])
    // no recent sign-in is asked for, and the account is read after the removal
    const removal = requests.indexOf(deletes()[0]!)
    expect(requests.slice(removal)).toContain('GET /access/account')
    expect(useSessionStore().accessMap).not.toHaveProperty(['acme.shipping'])
    expect(useSessionStore().accessMap).toMatchObject({ acme: 'Owner', 'acme.billing': 'Owner' })
    expect(body()).toContain('You left acme.shipping')
  })

  it('counts a membership that is gone already as left', async () => {
    const view = await mountSettings('acme', 'shipping')
    // left in another tab since the page loaded
    const db = loadDb()
    delete db.accessPoints['acme.shipping']!.AccessMap![mockUser.id]
    delete db.account!.AccessMap!['acme.shipping']
    saveDb(db)

    await confirmLeave()

    await eventually(() => expect(view.route().path).toBe('/orgs'))
    expect(useSessionStore().accessMap).not.toHaveProperty(['acme.shipping'])
    expect(body()).toContain('You left acme.shipping')
  })

  it('drops the membership from the account it has when the new read fails', async () => {
    const view = await mountSettings('acme', 'shipping')
    server.use(http.get('*/api/v1/access/account', () => HttpResponse.json({ Message: 'Down' }, { status: 503 })))

    await confirmLeave()

    await eventually(() => expect(view.route().path).toBe('/orgs'))
    expect(useSessionStore().accessMap).not.toHaveProperty(['acme.shipping'])
    expect(useSessionStore().accessMap).toMatchObject({ acme: 'Owner', 'northwind.portal': 'Reader' })
  })

  it('keeps the membership and says why when the removal fails', async () => {
    const view = await mountSettings('northwind', 'portal')
    server.use(
      http.delete('*/api/v1/access/points/:key/members/:accountId', () =>
        HttpResponse.json({ Message: 'Try again later.' }, { status: 500 }),
      ),
    )

    await confirmLeave()

    await eventually(() => expect(body()).toContain('You did not leave the project'))
    expect(body()).toContain('Try again later.')
    await eventually(() => expect(dialog()).toBeNull())
    expect(view.route().path).toBe('/northwind/portal/settings')
    expect(useSessionStore().accessMap).toHaveProperty(['northwind.portal'], 'Reader')
  })

  it('disables the button of the only Owner and says why', async () => {
    await mountSettings('acme', 'billing')

    await eventually(() => expect(leaveButton()!.disabled).toBe(true))
    expect(text(projectCard())).toContain(
      'You are the only Owner of this project. Make another member an Owner before you leave.',
    )
  })

  it('lets an Owner leave while another Owner stays', async () => {
    const view = await mountSettings('acme', 'billing', (db) => {
      db.accessPoints['acme.billing']!.AccessMap!['user_01MOCKGRACE'] = 'Owner'
    })
    await eventually(() => expect(text(projectCard())).toContain('Leave the project to remove it'))

    await confirmLeave()

    await eventually(() => expect(view.route().path).toBe('/orgs'))
    expect(useSessionStore().accessMap).not.toHaveProperty(['acme.billing'])
  })

  it('shows the last Owner the answer of the API while the members are not known', async () => {
    server.use(http.get('*/api/v1/access/points/:key', () => HttpResponse.json({ Message: 'Down' }, { status: 503 })))
    const view = await mountSettings('acme', 'billing')
    expect(leaveButton()!.disabled).toBe(false)

    await confirmLeave()

    await eventually(() => expect(body()).toContain('You did not leave the project'))
    expect(body()).toContain('The last Owner cannot leave. Make another member an Owner first.')
    expect(view.route().path).toBe('/acme/billing/settings')
    expect(useSessionStore().accessMap).toHaveProperty(['acme.billing'], 'Owner')
  })

  it('tells a visitor of the exhibition that it can be joined again', async () => {
    await mountSettings('exhibition', 'exhibition', (db) => {
      db.accessPoints['exhibition.exhibition']!.AccessMap![mockUser.id] = 'Reader'
      db.account!.AccessMap!['exhibition.exhibition'] = 'Reader'
    })
    await eventually(() => expect(requests).toContain('GET /access/exhibition'))
    await settle()

    leaveButton()!.click()
    await eventually(() => expect(dialog()).toBeTruthy())

    expect(text(dialog())).toContain(
      'You lose your Reader role on exhibition.exhibition. You can join the exhibition again from Organizations.',
    )
  })

  it('names the long role as the badge does', async () => {
    await mountSettings('acme', 'shipping', (db) => {
      db.accessPoints['acme.shipping']!.AccessMap![mockUser.id] = 'ContributorWithSecrets'
      db.account!.AccessMap!['acme.shipping'] = 'ContributorWithSecrets'
    })

    leaveButton()!.click()
    await eventually(() => expect(dialog()).toBeTruthy())

    expect(text(dialog())).toContain('You lose your Contributor + secrets role on acme.shipping.')
  })
})
