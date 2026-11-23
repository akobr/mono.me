/* eslint-disable vue/one-component-per-file -- a page that asks for the exhibition, and a host that shows it again */
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, ref } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { getGetExhibitionQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import { useExhibition } from '@/composables/useExhibition'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, mockUser, resetDb, saveDb, type MockScenario } from '@/mock/db'
import OrganizationsPage from '@/pages/orgs/index.vue'
import { useSessionStore } from '@/stores/session'
import { mountScreen, settle } from '../../helpers'

// The offer of the exhibition on Organizations (424-271, spec section 2), against the mock API of the e2e mode: Ada
// has an account of the default dataset and is no member of exhibition/exhibition.

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

const exhibitionPath = '/exhibition/exhibition/default/annotations'
const eventually = (assertion: () => void) => vi.waitFor(assertion, { timeout: 5_000 })
const text = (element: Element | null | undefined) => (element?.textContent ?? '').replaceAll(/\s+/g, ' ').trim()
const body = () => text(document.body)
const offer = () => document.body.querySelector<HTMLElement>('[data-exhibition-offer]')
const joinButton = () =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => text(button) === 'Join the exhibition',
  )
const posts = () => requests.filter((request) => request.startsWith('POST'))

/** Organizations with the account of the dataset, after `change` of its data. */
async function mountOrganizations(scenario: MockScenario = 'default', change?: Parameters<typeof start>[1]) {
  start(scenario, change)
  const view = await mountScreen(OrganizationsPage, {}, { path: '/orgs', account: loadDb().account })
  mounted.push(view)
  await settle(6)
  return { ...view, route: () => view.router.currentRoute.value }
}

function start(scenario: MockScenario, change?: (db: ReturnType<typeof loadDb>) => void) {
  const db = resetDb(scenario)
  change?.(db)
  saveDb(db)
}

describe('the exhibition on Organizations', () => {
  it('is offered to a user with an account who is no member of it, after the organizations', async () => {
    await mountOrganizations()

    await eventually(() => expect(offer()).toBeTruthy())
    expect(text(offer()?.querySelector('h2'))).toBe('The exhibition')
    expect(text(offer())).toContain('Step into Fourth Wall Software')
    expect(text(offer())).toContain(
      'Adds the demo project to your account as a Reader. Nothing you do there can break it: you join read-only.',
    )
    expect(joinButton()?.disabled).toBe(false)
    // the last item of the grid, and no link: nothing lists the exhibition as a project
    expect(offer()?.parentElement?.lastElementChild).toBe(offer())
    expect(offer()?.querySelector('a')).toBeNull()
    expect(requests).toContain('GET /access/exhibition')
  })

  it('is not offered to a member of it', async () => {
    await mountOrganizations('default', (db) => {
      db.accessPoints['exhibition.exhibition']!.AccessMap![mockUser.id] = 'Reader'
      db.account!.AccessMap!['exhibition.exhibition'] = 'Reader'
    })

    await eventually(() => expect(requests).toContain('GET /access/exhibition'))
    await settle()
    expect(offer()).toBeNull()
    expect(body()).toContain('exhibition')
  })

  it('is not offered where the deployment has none', async () => {
    await mountOrganizations('showcase')

    await eventually(() => expect(requests).toContain('GET /access/exhibition'))
    await settle()
    expect(offer()).toBeNull()
  })

  it('is not offered after any other failure, and shows no error', async () => {
    server.use(http.get('*/api/v1/access/exhibition', () => HttpResponse.json({ Message: 'Down' }, { status: 503 })))
    await mountOrganizations()

    await eventually(() => expect(requests).toContain('GET /access/exhibition'))
    await settle()
    expect(offer()).toBeNull()
    expect(body()).not.toContain('Down')
  })

  it('is offered below the empty state to a user without organizations', async () => {
    await mountOrganizations('default', (db) => {
      for (const point of Object.values(db.accessPoints)) {
        delete point.AccessMap![mockUser.id]
      }
      db.account!.AccessMap = {}
    })

    await eventually(() => expect(offer()).toBeTruthy())
    expect(body()).toContain('No organizations yet')
  })
})

describe('the answer about the exhibition', () => {
  /** A page that asks for the exhibition, shown and gone again with `shown`. */
  async function mountAsker(scenario: MockScenario) {
    start(scenario)
    const shown = ref(true)
    const Asker = defineComponent({
      setup: () => {
        const { offered } = useExhibition()
        return () => h('p', offered.value ? 'offered' : 'not offered')
      },
    })
    const Host = defineComponent({ setup: () => () => (shown.value ? h(Asker) : null) })
    const view = await mountScreen(Host, {}, { path: '/orgs', account: loadDb().account })
    mounted.push(view)
    await settle(6)
    return { shown, view }
  }

  const asked = () => requests.filter((request) => request === 'GET /access/exhibition').length

  it('keeps a 404 (none), so the next page does not ask again', async () => {
    const { shown } = await mountAsker('showcase')
    await eventually(() => expect(asked()).toBe(1))

    shown.value = false
    await settle()
    shown.value = true
    await settle(6)

    expect(asked()).toBe(1)
    expect(body()).toContain('not offered')
  })

  it('asks again after another failure', async () => {
    server.use(http.get('*/api/v1/access/exhibition', () => HttpResponse.json({ Message: 'Down' }, { status: 503 })))
    const { shown } = await mountAsker('default')
    await eventually(() => expect(asked()).toBe(1))
    server.resetHandlers()

    shown.value = false
    await settle()
    shown.value = true

    await eventually(() => expect(asked()).toBe(2))
    await eventually(() => expect(body()).toContain('offered'))
    expect(body()).not.toContain('not offered')
  })
})

describe('the answer about the exhibition, after a 404 that followed an offer', () => {
  afterEach(() => vi.useRealTimers())

  it('does not ask again on a later page, also once the offer is stale', async () => {
    const asked = () => requests.filter((request) => request === 'GET /access/exhibition').length
    vi.useFakeTimers({ toFake: ['Date'] })
    start('default')
    const shown = ref(true)
    const Asker = defineComponent({
      setup: () => {
        const { offered } = useExhibition()
        return () => h('p', offered.value ? 'offered' : 'not offered')
      },
    })
    const Host = defineComponent({ setup: () => () => (shown.value ? h(Asker) : null) })
    const view = await mountScreen(Host, {}, { path: '/orgs', account: loadDb().account })
    mounted.push(view)
    await eventually(() => expect(body()).toBe('offered'))

    // the deployment stopped offering it, and a join's 404 asked again (the data of the offer stays in the cache)
    const db = loadDb()
    db.exhibition = null
    saveDb(db)
    await view.queryClient.refetchQueries({ queryKey: getGetExhibitionQueryKey() })
    await eventually(() => expect(body()).toBe('not offered'))
    expect(asked()).toBe(2)

    // much later, past the staleTime of the offer: another page mounts
    vi.setSystemTime(Date.now() + 10 * 60_000)
    shown.value = false
    await settle()
    shown.value = true
    await settle(6)

    expect(asked()).toBe(2)
    expect(body()).toBe('not offered')
  })
})

describe('Join the exhibition on Organizations', () => {
  it('joins once, keeps the account of the answer and opens the exhibition with the toast', async () => {
    const view = await mountOrganizations()
    await eventually(() => expect(joinButton()).toBeTruthy())
    const setAccount = vi.spyOn(useSessionStore(), 'setAccount')

    joinButton()!.click()
    await eventually(() => expect(view.route().path).toBe(exhibitionPath))

    expect(posts()).toEqual(['POST /access/exhibition/join'])
    // the answer is the account: no second read of it
    expect(requests.filter((request) => request === 'GET /access/account')).toEqual([])
    expect(setAccount).toHaveBeenCalledTimes(1)
    expect(useSessionStore().accessMap).toMatchObject({ 'exhibition.exhibition': 'Reader', 'acme.billing': 'Owner' })
    expect(body()).toContain('You joined the exhibition')
    expect(offer()).toBeNull()
  })

  it('says that the exhibition is no longer offered after a 404, and stops offering it', async () => {
    const view = await mountOrganizations()
    await eventually(() => expect(joinButton()).toBeTruthy())
    // the deployment turned it off since the page asked
    const db = loadDb()
    db.exhibition = null
    saveDb(db)

    joinButton()!.click()

    await eventually(() => expect(body()).toContain('The exhibition is no longer offered.'))
    await eventually(() => expect(offer()).toBeNull())
    expect(view.route().path).toBe('/orgs')
    expect(useSessionStore().accessMap).not.toHaveProperty(['exhibition.exhibition'])
  })

  it('says why a join failed and keeps the offer to try again', async () => {
    server.use(
      http.post('*/api/v1/access/exhibition/join', () =>
        HttpResponse.json({ Message: 'Try again later.', ErrorCode: 'Conflict' }, { status: 409 }),
      ),
    )
    const view = await mountOrganizations()
    await eventually(() => expect(joinButton()).toBeTruthy())

    joinButton()!.click()

    await eventually(() => expect(body()).toContain('Joining the exhibition failed'))
    expect(body()).toContain('Try again later.')
    expect(view.route().path).toBe('/orgs')
    await eventually(() => expect(joinButton()?.disabled).toBe(false))
  })
})
