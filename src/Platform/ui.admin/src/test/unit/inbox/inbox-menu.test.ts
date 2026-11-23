import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, type SetupContext } from 'vue'
import { RouterView } from 'vue-router'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import { dismissViewInboxItem, type AccountAccessMap } from '@42for.net/2splatform.sdk'
import AppSidebar from '@/components/shell/AppSidebar.vue'
import { useSidebar } from '@/composables/useSidebar'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, saveDb } from '@/mock/db'
import { pushInboxItem, systemActor, type InboxPush } from '@/mock/inbox'
import { inboxPollMs, inboxStaleMs, invalidateInbox } from '@/queries/inbox'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'
import { mountScreen, settle, type AppMountOptions } from '../../helpers'

// The mark of the Inbox entry in the menu (424-31): a dot on its icon while the view has open items for the caller,
// from the query the Inbox page shares. No mark while the answer is not there or failed, and none below Contributor,
// where nothing is asked. Ada owns acme.billing and reads northwind.portal; their default views start with open items.

// the navbar needs the dashboard shell; the stand-in shows the buttons of the page
vi.mock('@/components/shell/AppNavbar.vue', async () => {
  const vue = await import('vue')
  return {
    default: {
      name: 'AppNavbar',
      props: ['title'],
      setup:
        (props: { title?: string }, { slots }: SetupContext) =>
        () =>
          vue.h('header', { 'data-navbar': props.title }, slots.right?.()),
    },
  }
})

const server = setupServer(...mockHandlers)
const mounted: { host: { unmount: () => void } }[] = []
/** The inbox lists a test asked for, in their order. */
const requests: URL[] = []
server.events.on('request:start', ({ request }) => {
  const url = new URL(request.url)
  if (request.method === 'GET' && url.pathname.endsWith('/inbox')) {
    requests.push(url)
  }
})
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  vi.useRealTimers()
  requests.length = 0
  sidebar.open.value = false
  sidebar.collapsed.value = false
  document.body.innerHTML = ''
})
afterAll(() => server.close())

type Scope = { org: string; project: string; view: string }

const sidebar = useSidebar()
const accessMap = createSeed('default').account!.AccessMap as Record<string, string>

const billing: Scope = { org: 'acme', project: 'billing', view: 'default' }
const shipping: Scope = { org: 'acme', project: 'shipping', view: 'default' }
const portal: Scope = { org: 'northwind', project: 'portal', view: 'default' }
/** A view of acme.billing that the mock does not seed: its inbox starts empty. */
const empty: Scope = { org: 'acme', project: 'billing', view: 'empty' }

const message = (title: string): InboxPush => ({
  Kind: 'Message',
  Topic: 'Test',
  Title: title,
  MinimalRole: 'Contributor',
  CreatedBy: systemActor,
})

/** Pushes an item into the stored inbox of a view, as a producer of the back end would. */
function push(scope: Scope, item: InboxPush) {
  const db = loadDb()
  const stored = pushInboxItem(db, scope, item)
  saveDb(db)
  return stored
}

/** The menu as the slideover shows it, with its labels: happy-dom is 1024 px wide, where the docked one is the rail. */
async function mountMenu(scope: Partial<Scope>, options: { rail?: boolean } = {}) {
  sidebar.collapsed.value = !!options.rail
  const view = await mountScreen(AppSidebar, {}, { scope: scope as AppMountOptions['scope'], accessMap })
  mounted.push(view)
  if (!options.rail) {
    usePreferencesStore().sidebarPinned = false
    sidebar.open.value = true
  }
  await settle(6)
  return view
}

const entry = (path = '/inbox') => document.body.querySelector<HTMLAnchorElement>(`a[href$="${path}"]`)
const inbox = () => entry()!
const dot = (link: Element | null = inbox()) => link?.querySelector('[data-slot="linkLeadingChip"] [data-slot="base"]')
const lock = (link: Element | null = inbox()) => link?.querySelector('[data-slot="linkTrailingBadge"]')
/** What a screen reader says after the name of the link. */
const description = (link: Element = inbox()) => {
  const id = link.getAttribute('aria-describedby')
  return id ? document.getElementById(id)?.textContent : undefined
}

describe('the mark of the Inbox entry', () => {
  it('marks the entry of a view with open items with a dot, and says Open items to a screen reader', async () => {
    await mountMenu(billing)

    await vi.waitFor(() => expect(dot()).toBeTruthy())
    // a dot, not a number: the link still reads Inbox, and it has no trailing badge, which is the lock
    expect(dot()!.textContent?.trim()).toBe('')
    expect(inbox().textContent?.trim()).toBe('Inbox')
    expect(lock()).toBeNull()
    expect(description()).toBe('Open items')
    // one element describes the link, wherever the menu is rendered
    expect(document.querySelectorAll(`#${inbox().getAttribute('aria-describedby')}`)).toHaveLength(1)
    expect(requests.map((url) => [url.pathname, url.search])).toEqual([
      ['/api/v1/acme/billing/default/inbox', '?state=Open'],
    ])
  })

  it('marks no other entry', async () => {
    await mountMenu(billing)
    await vi.waitFor(() => expect(dot()).toBeTruthy())

    const marked = [...document.body.querySelectorAll('a')].filter(
      (link) => link.querySelector('[data-slot="linkLeadingChip"]') || link.hasAttribute('aria-describedby'),
    )
    expect(marked.map((link) => link.textContent?.trim())).toEqual(['Inbox'])
  })

  it('names the mark in the tooltip of the entry in the icon rail', async () => {
    const view = await mountMenu(billing, { rail: true })
    await vi.waitFor(() => expect(dot()).toBeTruthy())

    const tooltips = view.host.findAllComponents({ name: 'Tooltip' }).map((tooltip) => tooltip.props('text'))
    expect(tooltips).toContain('Open items')
    // the other entries keep the tooltip of their label
    expect(tooltips).toContain('Modeling')
    expect(tooltips).not.toContain('Inbox')
  })

  it('has no mark for a view without open items', async () => {
    await mountMenu(empty)

    await vi.waitFor(() => expect(requests).toHaveLength(1))
    await settle()
    expect(inbox().getAttribute('href')).toBe('/acme/billing/empty/inbox')
    expect(dot()).toBeFalsy()
    expect(inbox().hasAttribute('aria-describedby')).toBe(false)
    expect(document.body.textContent).not.toContain('Open items')
  })

  it('takes the mark away when the last open item is closed, and puts it back with a new one', async () => {
    const view = await mountMenu(empty)
    await vi.waitFor(() => expect(requests).toHaveLength(1))
    expect(dot()).toBeFalsy()

    const item = push(empty, message('The only one'))
    await invalidateInbox(view.queryClient, empty)
    await vi.waitFor(() => expect(dot()).toBeTruthy())
    expect(description()).toBe('Open items')

    await dismissViewInboxItem(empty.org, empty.project, empty.view, item.Id)
    await invalidateInbox(view.queryClient, empty)
    await vi.waitFor(() => expect(dot()).toBeFalsy())
  })

  it.each([
    ['fails', 500, 'InternalError'],
    ['has no inbox routes yet', 404, 'NotFound'],
    ['refuses the caller', 403, 'AccessDenied'],
  ] as const)('has no mark when the API %s, and keeps the failure silent', async (_, status, errorCode) => {
    server.use(
      http.get('*/api/v1/:org/:project/:view/inbox', () =>
        HttpResponse.json({ Message: 'The inbox could not be read.', ErrorCode: errorCode }, { status }),
      ),
    )
    const view = await mountMenu(billing)

    await vi.waitFor(() => expect(requests).toHaveLength(1))
    await settle()
    expect(dot()).toBeFalsy()
    expect(inbox().hasAttribute('aria-describedby')).toBe(false)
    // the reporter of the app skips a query that says it is silent (boot/query.ts)
    const [query] = view.queryClient.getQueryCache().findAll({ queryKey: ['v1', 'acme', 'billing', 'default', 'inbox'] })
    expect(query!.state.status).toBe('error')
    expect(query!.meta).toEqual({ silent: true })
  })

  it('drops the mark when a later read fails, although the earlier answer had open items', async () => {
    const view = await mountMenu(billing)
    await vi.waitFor(() => expect(dot()).toBeTruthy())
    server.use(
      http.get('*/api/v1/:org/:project/:view/inbox', () =>
        HttpResponse.json({ Message: 'The inbox could not be read.', ErrorCode: 'InternalError' }, { status: 500 }),
      ),
    )

    await invalidateInbox(view.queryClient, billing)

    await vi.waitFor(() => expect(dot()).toBeFalsy())
    expect(inbox().hasAttribute('aria-describedby')).toBe(false)
  })

  it('marks the entry for a Contributor too, who has open items of Contributors', async () => {
    await mountMenu(shipping)

    await vi.waitFor(() => expect(dot()).toBeTruthy())
    expect(inbox().getAttribute('href')).toBe('/acme/shipping/default/inbox')
    expect(lock()).toBeNull()
    expect(requests.map((url) => url.pathname)).toEqual(['/api/v1/acme/shipping/default/inbox'])
  })

  it('has no Inbox entry and asks for nothing for a Reader', async () => {
    await mountMenu(portal)

    expect(entry()).toBeNull()
    expect(requests).toEqual([])
  })

  it('takes the mark away when the session falls below Contributor, although the answer with open items is cached', async () => {
    await mountMenu(billing)
    await vi.waitFor(() => expect(dot()).toBeTruthy())

    // the account is read again, as after a new project or an accepted invitation: somebody lowered the role meanwhile
    useSessionStore().setAccount({
      Id: 'user_1',
      AccessMap: { ...accessMap, 'acme.billing': 'Reader' } as AccountAccessMap,
    })
    await settle()

    // the entry leaves the menu with its mark
    expect(entry()).toBeNull()
    expect(document.getElementById('navigation-inbox-mark')).toBeNull()
    expect(requests).toHaveLength(1)
  })

  it('reads the inbox of the view the menu shows outside a view: the default view of the project', async () => {
    await mountMenu({ org: 'acme', project: 'billing' })

    await vi.waitFor(() => expect(dot()).toBeTruthy())
    expect(inbox().getAttribute('href')).toBe('/acme/billing/default/inbox')
    expect(requests.map((url) => url.pathname)).toEqual(['/api/v1/acme/billing/default/inbox'])
  })

  it('has no Inbox entry and asks for nothing outside a project', async () => {
    await mountMenu({ org: 'acme' })

    expect(entry()).toBeNull()
    expect(requests).toEqual([])
  })

  it('never reads the inbox again by itself', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    await mountMenu(billing)
    await vi.waitFor(() => expect(dot()).toBeTruthy())

    vi.advanceTimersByTime(5 * inboxPollMs)
    await settle()

    expect(requests).toHaveLength(1)
  })
})

describe('the menu next to the Inbox page', () => {
  const Shell = defineComponent({ setup: () => () => [h(AppSidebar), h(RouterView)] })

  async function mountShell(scope: Scope) {
    const view = await mountScreen(
      Shell,
      {},
      { path: `/${scope.org}/${scope.project}/${scope.view}/inbox`, scope, accessMap, routes },
    )
    mounted.push(view)
    await settle(6)
    return view
  }

  const rows = () => [...document.body.querySelectorAll<HTMLLIElement>('ul[aria-label$=" items"] > li')]
  const tabCount = () =>
    document.body.querySelector('button[role="tab"] [data-slot="trailingBadge"]')?.textContent?.trim()

  it('shares one request of the open items with the page', async () => {
    await mountShell(billing)

    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    await vi.waitFor(() => expect(dot()).toBeTruthy())
    expect(requests.map((url) => url.search)).toEqual(['?state=Open'])
    expect(tabCount()).toBe('3')
  })

  /** The menu on a page that is not the Inbox, as after the start of the app. */
  async function mountElsewhere() {
    const view = await mountScreen(
      Shell,
      {},
      { path: '/start', scope: billing, accessMap, routes: [{ path: '/start', component: { render: () => null } }, ...routes] },
    )
    mounted.push(view)
    await vi.waitFor(() => expect(dot()).toBeTruthy())
    expect(requests).toHaveLength(1)
    return view
  }

  it('opens the page with the answer of the menu while that is fresh', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    const view = await mountElsewhere()

    vi.setSystemTime(Date.now() + inboxStaleMs - 1000)
    await view.router.push('/acme/billing/default/inbox')

    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    await settle()
    expect(requests).toHaveLength(1)
  })

  it('opens the page with a fresh answer when the menu read the open items a while ago', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    const view = await mountElsewhere()
    push(billing, message('Arrived meanwhile'))

    vi.setSystemTime(Date.now() + inboxStaleMs + 1000)
    await view.router.push('/acme/billing/default/inbox')

    await vi.waitFor(() => expect(rows()).toHaveLength(4))
    expect(requests.map((url) => url.search)).toEqual(['?state=Open', '?state=Open'])
    expect(tabCount()).toBe('4')
  })

  it('follows the page: the mark goes with the last open item that the page dismisses', async () => {
    push(empty, message('The only one'))
    await mountShell(empty)
    await vi.waitFor(() => expect(rows()).toHaveLength(1))
    await vi.waitFor(() => expect(dot()).toBeTruthy())

    rows()[0]!.querySelectorAll('button')[0]!.click()

    await vi.waitFor(() => expect(document.body.textContent).toContain('Nothing to do'))
    expect(dot()).toBeFalsy()
    expect(tabCount()).toBeUndefined()
  })

  it('gets the reads of the page: an item that arrives while the page is open lights the mark', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    await mountShell(empty)
    await vi.waitFor(() => expect(document.body.textContent).toContain('Nothing to do'))
    expect(dot()).toBeFalsy()

    push(empty, message('Arrived meanwhile'))
    vi.advanceTimersByTime(inboxPollMs)

    await vi.waitFor(() => expect(dot()).toBeTruthy())
    expect(tabCount()).toBe('1')
    expect(requests.map((url) => url.search)).toEqual(['?state=Open', '?state=Open'])
  })
})
