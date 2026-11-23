import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { RouterView, type RouteRecordRaw } from 'vue-router'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import TimeZoneSelect from '@/components/annotations/TimeZoneSelect.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, mockUser, resetDb, saveDb, type MockDb } from '@/mock/db'
import { setStaleSignIn } from '@/mock/step-up'
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
  setStaleSignIn(false)
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

// Settings as a route, so its guard of unsaved changes runs; every other address is a stand-in.
const routes: RouteRecordRaw[] = [
  { path: '/:org/:project/settings', component: SettingsPage },
  { path: '/:pathMatch(.*)*', component: { render: () => null } },
]

/** Settings of the project with the account of the default dataset, after `change` of its data. */
async function mountSettings(
  org: string,
  project: string,
  change?: (db: MockDb) => void,
  extraRoutes: RouteRecordRaw[] = [],
) {
  const db = resetDb('default')
  change?.(db)
  saveDb(db)
  const view = await mountScreen(
    RouterView,
    {},
    {
      path: `/${org}/${project}/settings`,
      scope: { org, project, view: 'default' },
      account: loadDb().account,
      routes: [...extraRoutes, ...routes],
    },
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

  it('needs no recent sign-in', async () => {
    const view = await mountSettings('acme', 'shipping')
    setStaleSignIn(true)

    await confirmLeave()

    await eventually(() => expect(view.route().path).toBe('/orgs'))
    expect(body()).not.toContain("Confirm it's you")
  })

  it('keeps the confirmation, as it was opened, until the page is gone', async () => {
    // Organizations loads slowly: the account has lost the project before the page goes.
    let release!: () => void
    const held = new Promise<void>((resolve) => (release = resolve))
    const slowOrganizations: RouteRecordRaw = {
      path: '/orgs',
      component: async () => {
        await held
        return { render: () => null }
      },
    }
    const view = await mountSettings('acme', 'shipping', undefined, [slowOrganizations])

    await confirmLeave()
    await eventually(() => expect(useSessionStore().accessMap).not.toHaveProperty(['acme.shipping']))
    await settle()

    expect(view.route().path).toBe('/acme/shipping/settings')
    expect(text(dialog())).toContain('Leave acme.shipping?')
    expect(text(dialog())).toContain('You lose your Contributor role on acme.shipping.')
    // the confirm button keeps its loading state, so a second click does nothing
    expect(button('Leave project', dialog()!)?.disabled).toBe(true)
    expect(deletes()).toHaveLength(1)
    release()
    await eventually(() => expect(view.route().path).toBe('/orgs'))
  })

  it('ends a draft of the project settings without asking, and the confirmation says it is lost', async () => {
    const view = await mountSettings('acme', 'shipping', (db) => {
      db.accessPoints['acme.shipping']!.AccessMap![mockUser.id] = 'Administrator'
      db.account!.AccessMap!['acme.shipping'] = 'Administrator'
    })
    await eventually(() => expect(view.host.findComponent(TimeZoneSelect).props('disabled')).toBe(false))
    view.host.findComponent(TimeZoneSelect).vm.$emit('update:modelValue', 'Asia/Tokyo')
    await settle()

    leaveButton()!.click()
    await eventually(() => expect(dialog()).toBeTruthy())
    expect(text(dialog())).toContain(
      'You lose your Administrator role on acme.shipping. To come back, you need a new invitation. Your unsaved changes on this page are lost.',
    )
    button('Leave project', dialog()!)!.click()

    await eventually(() => expect(view.route().path).toBe('/orgs'))
    expect(body()).not.toContain('Discard changes?')
  })

  it('says nothing about coming back while the exhibition is not known', async () => {
    server.use(http.get('*/api/v1/access/exhibition', () => HttpResponse.json({ Message: 'Down' }, { status: 503 })))
    await mountSettings('acme', 'shipping')
    await eventually(() => expect(requests).toContain('GET /access/exhibition'))
    await settle()

    leaveButton()!.click()
    await eventually(() => expect(dialog()).toBeTruthy())

    expect(text(dialog())).toContain('You lose your Contributor role on acme.shipping.')
    expect(text(dialog())).not.toContain('To come back')
    expect(text(dialog())).not.toContain('join the exhibition again')
  })

  it('forgets the project once its page is gone, without reading it again as a non-member', async () => {
    const view = await mountSettings('acme', 'shipping')
    const cachedOf = () => view.queryClient.getQueryCache().findAll({ queryKey: ['v1', 'acme', 'shipping'] })
    await eventually(() => expect(cachedOf().length).toBeGreaterThan(0))

    await confirmLeave()
    await eventually(() => expect(view.route().path).toBe('/orgs'))
    await settle()

    const removal = requests.indexOf(deletes()[0]!)
    expect(requests.slice(removal).filter((request) => request.includes('/acme/shipping'))).toEqual([])
    expect(cachedOf()).toEqual([])
  })

  it('keeps a save of the project settings that ends after the leave out of the cache', async () => {
    let release!: () => void
    const held = new Promise<void>((resolve) => (release = resolve))
    let saved = false
    // the API took the save before the leave, and its answer arrives after it
    server.use(
      http.put('*/api/v1/acme/shipping/settings', async () => {
        await held
        saved = true
        return HttpResponse.json({ DefaultTimeZone: 'Asia/Tokyo', Version: 1 })
      }),
    )
    const view = await mountSettings('acme', 'shipping', (db) => {
      db.accessPoints['acme.shipping']!.AccessMap![mockUser.id] = 'Administrator'
      db.account!.AccessMap!['acme.shipping'] = 'Administrator'
    })
    const cachedOf = () => view.queryClient.getQueryCache().findAll({ queryKey: ['v1', 'acme', 'shipping'] })
    await eventually(() => expect(view.host.findComponent(TimeZoneSelect).props('disabled')).toBe(false))
    view.host.findComponent(TimeZoneSelect).vm.$emit('update:modelValue', 'Asia/Tokyo')
    await settle()
    button('Save project settings')!.click()
    await eventually(() => expect(requests).toContain('PUT /acme/shipping/settings'))

    await confirmLeave()
    await eventually(() => expect(view.route().path).toBe('/orgs'))
    await eventually(() => expect(cachedOf()).toEqual([]))
    release()
    await eventually(() => expect(saved).toBe(true))
    await settle()

    expect(cachedOf()).toEqual([])
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
