import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { RouterView, type RouteRecordRaw } from 'vue-router'
import { useToast } from '@nuxt/ui/composables'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import type { AuthService } from '@/boot/auth'
import { setBootState } from '@/boot/state'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, mockUser, resetDb, saveDb, type MockDb } from '@/mock/db'
import { authGuard } from '@/router/guards/auth'
import { useSessionStore } from '@/stores/session'
import { mountScreen, settle } from '../../helpers'

// The onboarding wizard (424-267, spec section 3): its steps and their address, the choice of the exhibition, the
// focus, and the end of the wizard with each of its failures. The page is mounted through its route on the mock API,
// for a signed-in user without an account and with the auth guard, which sends a user with an account from
// /onboarding to "/".

const server = setupServer(...mockHandlers)
/** Every request of a test, as "METHOD /path" below /api/v1. */
const requests: string[] = []
const mounted: { host: { unmount: () => void } }[] = []
let auth: AuthService

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
  server.events.on('request:start', ({ request }) => {
    requests.push(`${request.method} ${new URL(request.url).pathname.replace(/^\/api\/v1/, '')}`)
  })
})
beforeEach(() => {
  requests.length = 0
  auth = {
    kind: 'mock',
    getUser: () => mockUser,
    getAccessToken: async () => 'token',
    signIn: vi.fn(async () => {}),
    signOut: vi.fn(async () => {}),
    consumeReturnTo: () => undefined,
  }
  setBootState({ status: 'ready', auth })
  resetDb('new-user')
})
afterEach(() => {
  vi.useRealTimers()
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  document.body.innerHTML = ''
  // The toasts are global state of Nuxt UI: each test reads the toasts of its own end.
  createApp({}).runWithContext(() => useToast().clear())
})
afterAll(() => server.close())

// The route of the page as the app has it (its meta decides what the guard does); every other address is a stand-in.
const wizardRoutes: RouteRecordRaw[] = [
  routes.find((route) => route.path === '/onboarding')!,
  { path: '/:pathMatch(.*)*', component: { render: () => null } },
]

const exhibitionUrl = '*/api/v1/access/exhibition'
const joinUrl = '*/api/v1/access/exhibition/join'
const accountUrl = '*/api/v1/access/account'
const exhibitionPath = '/exhibition/exhibition/default/annotations'
const ownPath = '/globex/portal/default/annotations'
const conflict = () =>
  HttpResponse.json({ Message: 'The access point changed meanwhile.', ErrorCode: 'Conflict' }, { status: 409 })
const bareAccount = { Id: mockUser.id, UserName: mockUser.email, Name: mockUser.name, AccessMap: {} }

/** Starts the scenario of a user without an account with changed data. */
function start(change: (db: MockDb) => void) {
  const db = resetDb('new-user')
  change(db)
  saveDb(db)
}

/** Holds a request until the test lets it go; then the mock answers it. */
function hold(method: 'get' | 'post', url: string) {
  let release = () => {}
  const gate = new Promise<void>((resolve) => (release = resolve))
  server.use(
    http[method](url, async () => {
      await gate
    }),
  )
  return release
}

const eventually = (assertion: () => void) => vi.waitFor(assertion, { timeout: 5_000 })

/** Waits by turns of the event loop: under fake timers vi.waitFor would move the clock. */
async function turnsUntil(condition: () => boolean) {
  for (let turn = 0; turn < 100 && !condition(); turn++) {
    await settle(1)
  }
}

async function mountWizard(path = '/onboarding', loaded = true) {
  const view = await mountScreen(RouterView, {}, { path, routes: wizardRoutes, account: null, guards: [authGuard] })
  mounted.push(view)
  if (loaded) {
    await eventually(() => expect(stepList()).toBeTruthy())
  }
  const route = () => view.router.currentRoute.value
  return { ...view, route }
}

const text = (element: Element) => (element.textContent ?? '').replaceAll(/\s+/g, ' ').trim()
const body = () => text(document.body)
// The organization step stays mounted while another one shows.
const isShown = (element: Element | null): boolean =>
  !!element &&
  (element === document.body || ((element as HTMLElement).style.display !== 'none' && isShown(element.parentElement)))
const all = <T extends HTMLElement = HTMLElement>(selector: string) =>
  [...document.body.querySelectorAll<T>(selector)].filter((element) => isShown(element))

const stepList = () => document.body.querySelector('nav[aria-label="Onboarding steps"]')
const stepButtons = () => all<HTMLButtonElement>('nav[aria-label="Onboarding steps"] > ol > li > button')
const stepLabels = () => stepButtons().map((step) => step.getAttribute('aria-label'))
const step = (name: string) => stepButtons().find((candidate) => candidate.getAttribute('aria-label') === name)
const currentSteps = () =>
  stepButtons()
    .filter((candidate) => candidate.getAttribute('aria-current') === 'step')
    .map((candidate) => candidate.getAttribute('aria-label'))
/** The headings of the steps that show: one. */
const stepHeadings = () => all('section[data-step] h2').map(text)
const button = (name: string) => all<HTMLButtonElement>('button').find((candidate) => text(candidate) === name)
const link = (name: string) => all<HTMLAnchorElement>('a').find((candidate) => text(candidate) === name)
const checkbox = () => all<HTMLButtonElement>('button[role="checkbox"]')[0]
const isChecked = () => checkbox()?.getAttribute('aria-checked') === 'true'
/** The texts of the elements that hold text only. */
const leaves = (element: Element) =>
  [...element.querySelectorAll('*')]
    .filter((candidate) => candidate.children.length === 0)
    .map(text)
    .filter(Boolean)
/** The alerts of the page, each as its title and description; a toast is an alert too, outside the page. */
const alerts = () =>
  [...document.body.querySelector('h1')!.parentElement!.querySelectorAll('[role="alert"]')]
    .filter((element) => isShown(element))
    .map((element) => leaves(element).join(' '))
const form = () => document.body.querySelector<HTMLFormElement>('section[data-step="organization"] form')!
const fields = () => [...form().querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input, textarea')]
const posts = () => requests.filter((request) => request.startsWith('POST'))
const count = (request: string) => requests.filter((candidate) => candidate === request).length

async function click(element: HTMLElement | undefined) {
  expect(element).toBeTruthy()
  element!.click()
  await settle()
}

async function fill(organization: string, project: string, description = '') {
  const values = [organization, project, description]
  fields().forEach((field, index) => {
    field.value = values[index]!
    field.dispatchEvent(new Event('input', { bubbles: true }))
  })
  await settle()
}

async function submit() {
  form().dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await settle()
}

/** What the session held at every navigation, and how often it got an account. */
function watchSession(view: Awaited<ReturnType<typeof mountWizard>>) {
  const session = useSessionStore()
  const setAccount = vi.spyOn(session, 'setAccount')
  const navigations: string[] = []
  view.router.beforeEach((to) => {
    navigations.push(`${to.path} ${session.accountStatus}`)
  })
  return { session, setAccount, navigations }
}

describe('the steps of the onboarding wizard', () => {
  it('shows Loading… until the exhibition answers, then the four steps with the first one current', async () => {
    const release = hold('get', exhibitionUrl)
    const view = await mountWizard('/onboarding', false)
    await settle()

    expect(document.body.querySelector('[role="status"]')?.textContent).toContain('Loading…')
    expect(stepList()).toBeNull()
    expect(stepHeadings()).toEqual([])
    // The welcome line and Sign out do not wait.
    expect(document.body.querySelector('h1')?.textContent?.trim()).toBe('Welcome, Ada Lovelace')
    expect(button('Sign out')).toBeTruthy()

    release()
    await eventually(() => expect(stepList()).toBeTruthy())
    expect(document.body.querySelector('[role="status"]')).toBeNull()
    expect(stepLabels()).toEqual(['Step 1: Welcome', 'Step 2: Security', 'Step 3: Exhibition', 'Step 4: Organization'])
    expect(currentSteps()).toEqual(['Step 1: Welcome'])
    expect(stepHeadings()).toEqual(['Customer data stays in the EU'])
    expect(button('Back')).toBeUndefined()
    expect(button('Next')).toBeTruthy()
    expect(view.route().fullPath).toBe('/onboarding')
    // Only onboarding widens the column of the bare layout.
    expect(view.route().meta).toMatchObject({ layout: 'bare', allowWithoutAccount: true, wide: true })
    expect(routes.filter((route) => route.meta?.wide).map((route) => route.path)).toEqual(['/onboarding'])
  })

  it('moves with Next, Back and the step list, with the step by name in the query, and asks for the exhibition once', async () => {
    const view = await mountWizard()

    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Security first']))
    expect(view.route().fullPath).toBe('/onboarding?step=security')
    expect(currentSteps()).toEqual(['Step 2: Security'])
    expect(button('Back')).toBeTruthy()

    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Join the exhibition']))
    expect(view.route().fullPath).toBe('/onboarding?step=exhibition')
    expect(currentSteps()).toEqual(['Step 3: Exhibition'])

    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Create your organization']))
    expect(view.route().fullPath).toBe('/onboarding?step=organization')
    expect(currentSteps()).toEqual(['Step 4: Organization'])
    // The last step ends with the form: its submit and Back, no Next.
    expect(button('Next')).toBeUndefined()
    expect(button('Create and continue')).toBeTruthy()
    expect(body()).toContain(
      'Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.',
    )

    await click(button('Back'))
    await eventually(() => expect(stepHeadings()).toEqual(['Join the exhibition']))
    expect(view.route().fullPath).toBe('/onboarding?step=exhibition')

    await click(step('Step 1: Welcome'))
    await eventually(() => expect(stepHeadings()).toEqual(['Customer data stays in the EU']))
    // The first step is the address without a step.
    expect(view.route().fullPath).toBe('/onboarding')
    expect(button('Back')).toBeUndefined()

    await click(step('Step 4: Organization'))
    await eventually(() => expect(stepHeadings()).toEqual(['Create your organization']))
    expect(view.route().fullPath).toBe('/onboarding?step=organization')

    // Every step is an entry of the history.
    view.router.back()
    await eventually(() => expect(stepHeadings()).toEqual(['Customer data stays in the EU']))
    view.router.back()
    await eventually(() => expect(stepHeadings()).toEqual(['Join the exhibition']))

    expect(count('GET /access/exhibition')).toBe(1)
    expect(posts()).toEqual([])
  })

  it('keeps the rest of the query when the step changes', async () => {
    const view = await mountWizard('/onboarding?mock-scenario=new-user&join=1')

    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Security first']))
    expect(view.route().query).toEqual({ 'mock-scenario': 'new-user', join: '1', step: 'security' })

    await click(button('Back'))
    await eventually(() => expect(stepHeadings()).toEqual(['Customer data stays in the EU']))
    expect(view.route().query).toEqual({ 'mock-scenario': 'new-user', join: '1' })
  })

  it.each([
    ['an unknown step', '/onboarding?step=billing', 'Customer data stays in the EU'],
    ['the name of the first step', '/onboarding?step=welcome', 'Customer data stays in the EU'],
    ['an empty step', '/onboarding?step=', 'Customer data stays in the EU'],
    ['a repeated step, of which the first counts', '/onboarding?step=security&step=organization', 'Security first'],
  ])('shows the step of %s', async (_what, path, heading) => {
    await mountWizard(path)

    expect(stepHeadings()).toEqual([heading])
    expect(currentSteps()).toHaveLength(1)
  })

  it.each([
    ['/onboarding?step=security', 'Security first', 'Step 2: Security'],
    ['/onboarding?step=exhibition', 'Join the exhibition', 'Step 3: Exhibition'],
    ['/onboarding?step=organization', 'Create your organization', 'Step 4: Organization'],
  ])('opens %s on its step, as after a reload', async (path, heading, current) => {
    await mountWizard(path)

    expect(stepHeadings()).toEqual([heading])
    expect(currentSteps()).toEqual([current])
  })

  it('has three steps in a deployment without an exhibition, where the exhibition step is the first step', async () => {
    start((db) => {
      db.exhibition = null
    })
    const view = await mountWizard('/onboarding?step=exhibition&join=1')

    expect(stepLabels()).toEqual(['Step 1: Welcome', 'Step 2: Security', 'Step 3: Organization'])
    expect(stepHeadings()).toEqual(['Customer data stays in the EU'])
    expect(currentSteps()).toEqual(['Step 1: Welcome'])

    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Security first']))
    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Create your organization']))
    expect(view.route().query.step).toBe('organization')
    expect(currentSteps()).toEqual(['Step 3: Organization'])
    // The choice in the address counts for nothing without an exhibition.
    expect(body()).not.toContain('You are joining the exhibition.')
    expect(button('Continue with the exhibition only')).toBeUndefined()

    await click(button('Back'))
    await eventually(() => expect(stepHeadings()).toEqual(['Security first']))
    expect(count('GET /access/exhibition')).toBe(1)
  })

  it('keeps four steps when the exhibition fails to load, with Retry in step 3 and nothing to check', async () => {
    server.use(http.get(exhibitionUrl, () => HttpResponse.json({ Message: 'The database is down.' }, { status: 500 })))
    await mountWizard('/onboarding?step=organization&join=1')

    expect(stepLabels()).toEqual(['Step 1: Welcome', 'Step 2: Security', 'Step 3: Exhibition', 'Step 4: Organization'])
    // Nothing can be joined until the exhibition loads, whatever the address says.
    expect(body()).not.toContain('You are joining the exhibition.')
    expect(button('Continue with the exhibition only')).toBeUndefined()

    await click(button('Back'))
    await eventually(() => expect(stepHeadings()).toEqual(['Join the exhibition']))
    expect(body()).toContain('The API failed')
    expect(body()).toContain('The database is down.')
    expect(checkbox()).toBeUndefined()
    expect(body()).not.toContain('This step is optional.')
    // The other steps are reachable as ever.
    expect(button('Back')).toBeTruthy()
    expect(button('Next')).toBeTruthy()

    // Still failing: the step stays.
    await click(button('Retry'))
    await eventually(() => expect(count('GET /access/exhibition')).toBe(2))
    await eventually(() => expect(button('Retry')).toBeTruthy())
    expect(checkbox()).toBeUndefined()

    server.resetHandlers()
    await click(button('Retry'))
    await eventually(() => expect(checkbox()).toBeTruthy())
    expect(button('Retry')).toBeUndefined()
    expect(body()).toContain('Adds you to exhibition/exhibition as a Reader.')
    // The choice of the address counts now.
    expect(isChecked()).toBe(true)
    await click(button('Next'))
    await eventually(() => expect(body()).toContain('You are joining the exhibition.'))
    expect(count('GET /access/exhibition')).toBe(3)
  })

  it('waits 5 seconds for the exhibition, then goes on with Retry in step 3', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    hold('get', exhibitionUrl)
    await mountWizard('/onboarding?step=exhibition', false)
    await turnsUntil(() => count('GET /access/exhibition') === 1)
    expect(count('GET /access/exhibition')).toBe(1)

    await vi.advanceTimersByTimeAsync(4_999)
    await settle(8)
    expect(stepList()).toBeNull()
    expect(document.body.querySelector('[role="status"]')?.textContent).toContain('Loading…')

    await vi.advanceTimersByTimeAsync(1)
    await turnsUntil(() => stepList() !== null)
    expect(stepLabels()).toHaveLength(4)
    expect(stepHeadings()).toEqual(['Join the exhibition'])
    expect(body()).toContain('The API did not answer in time')
    expect(button('Retry')).toBeTruthy()
    expect(checkbox()).toBeUndefined()
  })

  it('greets the user by name on every step, in a heading that analytics do not capture', async () => {
    await mountWizard()
    const heading = () => document.body.querySelector('h1')!

    for (const name of ['Step 1: Welcome', 'Step 2: Security', 'Step 3: Exhibition', 'Step 4: Organization']) {
      await click(step(name))
      await eventually(() => expect(currentSteps()).toEqual([name]))
      expect(document.body.querySelectorAll('h1')).toHaveLength(1)
      expect(heading().textContent?.trim()).toBe('Welcome, Ada Lovelace')
      expect(heading().classList.contains('ph-no-capture')).toBe(true)
      expect(button('Sign out')).toBeTruthy()
    }
  })

  it('signs out with the link below the card', async () => {
    await mountWizard('/onboarding?step=security')

    await click(button('Sign out'))

    await eventually(() => expect(auth.signOut).toHaveBeenCalledWith(`${location.origin}/login`))
  })

  it('shows the hosting statement without the safeguards that are not live', async () => {
    await mountWizard()

    expect(body()).toContain(
      'All customer data, configuration and secrets are stored and processed only in the EU (Sweden).',
    )
    expect(body()).toContain('Every authorization decision is made by 2S in the EU.')
    expect(body()).not.toContain('recorded')
    expect(body()).not.toContain('Impersonation')
    const list = document.body.querySelector('ul[aria-label="Sub-processors"]')!
    expect([...list.querySelectorAll('li')].map(leaves)).toEqual([
      ['Microsoft Azure', 'EU only', 'Hosting, database, storage, secrets'],
      ['Sentry', 'EU only', 'Error reporting'],
      ['PostHog', 'EU only', 'Product analytics'],
      [
        'WorkOS',
        'USA',
        'Sign-in of the administrators of the 2S Admin UI and their invitation emails (name, email, IP address, session data)',
      ],
    ])
    const details = link('Details of the EU hosting')!
    expect(details.getAttribute('href')).toBe('https://claude.ai/share/c9fbe93d-61c4-466c-8490-2ba028fb1999')
    expect(details.getAttribute('target')).toBe('_blank')
  })

  it('explains the sign-in of machines and people, with links that open in a new tab', async () => {
    await mountWizard('/onboarding?step=security')

    expect(body()).toContain('Security is the main focus of the 2S Platform.')
    expect(all('section[data-step] h3').map(text)).toEqual(['Machines and pipelines', 'People'])
    expect(all('section[data-step] ul li').map(text)).toEqual([
      'Entra ID in your own tenant',
      'Keycloak, self-hosted, for example in the EU',
    ])
    expect(body()).toContain('WorkOS is not involved')
    const docs = 'https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md'
    expect(
      all<HTMLAnchorElement>('section[data-step] a').map((anchor) => [
        text(anchor),
        anchor.getAttribute('href'),
        anchor.getAttribute('target'),
      ]),
    ).toEqual([
      ['Authentication: Entra ID and Keycloak', `${docs}#selecting-the-provider`, '_blank'],
      ['Federated auth of machines', `${docs}#machine-access-with-workload-identity-federation`, '_blank'],
      ['sform', 'https://42for.net/cli/sform', '_blank'],
      ['GitHub Action', 'https://42for.net/cli/github-action', '_blank'],
    ])
  })
})

describe('the choice of the exhibition', () => {
  it('offers the exhibition unchecked, with its project and description, and sends nothing', async () => {
    await mountWizard('/onboarding?step=exhibition')

    expect(isChecked()).toBe(false)
    expect(body()).toContain('You join as a Reader: you can browse its annotations')
    expect(all('section[data-step="exhibition"] > div > p').map(text)[1]).toBe(
      loadDb().accessPoints['exhibition.exhibition']!.Description,
    )
    expect(body()).toContain(
      'Adds you to exhibition/exhibition as a Reader. Its administrators see your name and email in the list of members; other visitors do not.',
    )
    expect(body()).toContain(
      'This step is optional. To join only the exhibition and create your own organization later, check the box and choose Continue with the exhibition only in the last step.',
    )

    await click(checkbox())
    await eventually(() => expect(isChecked()).toBe(true))
    await click(checkbox())
    await eventually(() => expect(isChecked()).toBe(false))
    expect(posts()).toEqual([])
  })

  it('names the project of the deployment, without a description when it has none', async () => {
    start((db) => {
      db.exhibition = { Organization: 'northwind', Project: 'portal' }
      delete db.accessPoints['northwind.portal']!.Description
    })
    await mountWizard('/onboarding?step=exhibition')

    expect(body()).toContain('Adds you to northwind/portal as a Reader.')
    // The text of the step and the note; the exhibition of the seed has a third paragraph, its description.
    expect(all('section[data-step="exhibition"] > div > p')).toHaveLength(2)
  })

  it('keeps the choice in the query, written over the entry of the step', async () => {
    const view = await mountWizard('/onboarding?step=security')
    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Join the exhibition']))

    await click(checkbox())
    await eventually(() => expect(view.route().query).toEqual({ step: 'exhibition', join: '1' }))
    expect(isChecked()).toBe(true)

    // No entry of its own: back leaves the step, forward returns to the choice.
    view.router.back()
    await eventually(() => expect(view.route().query).toEqual({ step: 'security' }))
    view.router.forward()
    await eventually(() => expect(view.route().query).toEqual({ step: 'exhibition', join: '1' }))
    await eventually(() => expect(isChecked()).toBe(true))

    // Next and Back carry it along.
    await click(button('Next'))
    await eventually(() => expect(view.route().query).toEqual({ step: 'organization', join: '1' }))
    expect(body()).toContain('You are joining the exhibition.')
    expect(button('Continue with the exhibition only')).toBeTruthy()
    await click(button('Back'))
    await eventually(() => expect(view.route().query).toEqual({ step: 'exhibition', join: '1' }))

    await click(checkbox())
    await eventually(() => expect(view.route().query).toEqual({ step: 'exhibition' }))
    expect(isChecked()).toBe(false)
    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Create your organization']))
    expect(body()).not.toContain('You are joining the exhibition.')
    expect(button('Continue with the exhibition only')).toBeUndefined()
  })

  it.each([
    ['/onboarding?step=exhibition&join=1', true],
    ['/onboarding?step=exhibition&join=1&join=0', true],
    ['/onboarding?step=exhibition&join=0&join=1', false],
    ['/onboarding?step=exhibition&join=true', false],
    ['/onboarding?step=exhibition&join', false],
  ])('reads the choice of %s, as after a reload', async (path, checked) => {
    await mountWizard(path)

    expect(isChecked()).toBe(checked)
  })
})

describe('the organization form of the wizard', () => {
  it('keeps its values while other steps show', async () => {
    await mountWizard('/onboarding?step=organization')
    await fill('globex', 'portal', 'The customer portal.')

    await click(button('Back'))
    await eventually(() => expect(stepHeadings()).toEqual(['Join the exhibition']))
    await click(step('Step 1: Welcome'))
    await eventually(() => expect(stepHeadings()).toEqual(['Customer data stays in the EU']))
    await click(step('Step 4: Organization'))
    await eventually(() => expect(stepHeadings()).toEqual(['Create your organization']))

    expect(fields().map((field) => field.value)).toEqual(['globex', 'portal', 'The customer portal.'])
  })

  it('validates the names as the form does', async () => {
    await mountWizard('/onboarding?step=organization')

    await submit()
    await eventually(() => expect(body()).toContain('Enter a organization name.'))
    expect(body()).toContain('Enter a project name.')

    await fill('invitations', 'portal')
    await submit()
    await eventually(() => expect(body()).toContain('This organization name is reserved.'))
    expect(posts()).toEqual([])
  })
})

describe('the focus of the wizard', () => {
  beforeEach(() => (document.activeElement as HTMLElement | null)?.blur())

  it.each([
    '/onboarding',
    '/onboarding?step=security',
    '/onboarding?step=exhibition',
    '/onboarding?step=organization',
    '/onboarding?step=billing',
  ])('stays where it is when %s loads', async (path) => {
    await mountWizard(path)
    // The autofocus of an input waits for a timer.
    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(document.activeElement).toBe(document.body)
  })

  it('stays where it is when the step of the address is not one of the deployment', async () => {
    start((db) => {
      db.exhibition = null
    })
    await mountWizard('/onboarding?step=exhibition')
    await settle()

    expect(stepHeadings()).toEqual(['Customer data stays in the EU'])
    expect(document.activeElement).toBe(document.body)
  })

  it('goes to the heading of the new step, and to the first field of the form on the last one', async () => {
    const view = await mountWizard()
    const focused = () => document.activeElement as HTMLElement

    for (const heading of ['Security first', 'Join the exhibition']) {
      await click(button('Next'))
      await eventually(() => expect(text(focused())).toBe(heading))
      expect(focused().tagName).toBe('H2')
      expect(focused().getAttribute('tabindex')).toBe('-1')
    }

    await click(button('Next'))
    await eventually(() => expect(focused()).toBe(fields()[0]))

    await click(button('Back'))
    await eventually(() => expect(text(focused())).toBe('Join the exhibition'))

    await click(step('Step 1: Welcome'))
    await eventually(() => expect(text(focused())).toBe('Customer data stays in the EU'))

    // The browser's back and forward are changes of step too.
    view.router.back()
    await eventually(() => expect(text(focused())).toBe('Join the exhibition'))
  })
})

describe('the end of the wizard', () => {
  it('creates the account alone when the exhibition is not checked, and opens the new project', async () => {
    const view = await mountWizard('/onboarding?step=organization')
    const { session, setAccount, navigations } = watchSession(view)
    expect(body()).not.toContain('You are joining the exhibition.')
    expect(button('Continue with the exhibition only')).toBeUndefined()

    await fill('globex', 'portal', '  The customer portal of Globex.  ')
    await submit()

    await eventually(() => expect(view.route().fullPath).toBe(ownPath))
    expect(posts()).toEqual(['POST /access/account'])
    expect(loadDb().accessPoints['globex.portal']?.Description).toBe('The customer portal of Globex.')
    expect(session.account?.AccessMap).toEqual({ globex: 'Owner', 'globex.portal': 'Owner' })
    // The session gets the account once, with the navigation that leaves the wizard.
    expect(setAccount).toHaveBeenCalledTimes(1)
    expect(navigations).toEqual([`${ownPath} ready`])
    expect(body()).not.toContain('You joined the exhibition')
  })

  it('creates the account, then joins the exhibition, and opens the new project with a way to the exhibition', async () => {
    const view = await mountWizard('/onboarding?step=organization&join=1')
    const { session, setAccount, navigations } = watchSession(view)
    expect(body()).toContain('You are joining the exhibition.')

    await fill('globex', 'portal')
    await submit()

    await eventually(() => expect(view.route().fullPath).toBe(ownPath))
    // The join would create a bare account, and createAccount refuses an existing one.
    expect(posts()).toEqual(['POST /access/account', 'POST /access/exhibition/join'])
    expect(session.account?.AccessMap).toEqual({
      globex: 'Owner',
      'globex.portal': 'Owner',
      'exhibition.exhibition': 'Reader',
    })
    expect(setAccount).toHaveBeenCalledTimes(1)
    expect(navigations).toEqual([`${ownPath} ready`])
    await eventually(() => expect(body()).toContain('You joined the exhibition'))
    expect(link('Open the exhibition')?.getAttribute('href')).toBe(exhibitionPath)
  })

  it('joins only the exhibition, which creates the account, and opens the exhibition', async () => {
    const view = await mountWizard('/onboarding?step=organization&join=1')
    const { session, setAccount, navigations } = watchSession(view)

    await click(button('Continue with the exhibition only'))

    await eventually(() => expect(view.route().fullPath).toBe(exhibitionPath))
    expect(posts()).toEqual(['POST /access/exhibition/join'])
    expect(session.account?.AccessMap).toEqual({ 'exhibition.exhibition': 'Reader' })
    expect(setAccount).toHaveBeenCalledTimes(1)
    expect(navigations).toEqual([`${exhibitionPath} ready`])
    await eventually(() => expect(body()).toContain('You joined the exhibition'))
    // The user is there already.
    expect(link('Open the exhibition')).toBeUndefined()
  })

  it('joins the project the deployment names', async () => {
    start((db) => {
      db.exhibition = { Organization: 'northwind', Project: 'portal' }
    })
    const view = await mountWizard('/onboarding?step=organization&join=1')

    await click(button('Continue with the exhibition only'))

    await eventually(() => expect(view.route().fullPath).toBe('/northwind/portal/default/annotations'))
  })

  it('does not let the two ways to end run at once while the account is created', async () => {
    const release = hold('post', accountUrl)
    const view = await mountWizard('/onboarding?step=organization&join=1')
    await fill('globex', 'portal')

    await submit()
    await eventually(() => expect(posts()).toEqual(['POST /access/account']))
    await settle()

    // working: the step list, Back, the submit and the other way to end are disabled.
    expect(stepButtons().every((candidate) => candidate.disabled)).toBe(true)
    expect(button('Back')?.disabled).toBe(true)
    expect(button('Create and continue')?.disabled).toBe(true)
    expect(button('Continue with the exhibition only')?.disabled).toBe(true)
    // Whatever still reaches them does nothing.
    await click(button('Continue with the exhibition only'))
    await submit()
    await click(step('Step 1: Welcome'))
    expect(posts()).toEqual(['POST /access/account'])
    expect(stepHeadings()).toEqual(['Create your organization'])

    release()
    await eventually(() => expect(view.route().fullPath).toBe(ownPath))
    expect(posts()).toEqual(['POST /access/account', 'POST /access/exhibition/join'])
  })

  it('does not let the two ways to end run at once while the exhibition is joined', async () => {
    const release = hold('post', joinUrl)
    const view = await mountWizard('/onboarding?step=organization&join=1')
    await fill('globex', 'portal')

    await click(button('Continue with the exhibition only'))
    await eventually(() => expect(posts()).toEqual(['POST /access/exhibition/join']))
    await settle()

    expect(stepButtons().every((candidate) => candidate.disabled)).toBe(true)
    expect(button('Back')?.disabled).toBe(true)
    expect(button('Create and continue')?.disabled).toBe(true)
    expect(button('Continue with the exhibition only')?.disabled).toBe(true)
    await submit()
    await click(button('Continue with the exhibition only'))
    expect(posts()).toEqual(['POST /access/exhibition/join'])

    release()
    await eventually(() => expect(view.route().fullPath).toBe(exhibitionPath))
    expect(posts()).toEqual(['POST /access/exhibition/join'])
    expect(useSessionStore().account?.AccessMap).toEqual({ 'exhibition.exhibition': 'Reader' })
  })
})

describe('the failures of the end of the wizard', () => {
  it('shows a failed createAccount in the form, joins nothing and stays a wizard', async () => {
    const view = await mountWizard('/onboarding?step=organization&join=1')
    const { session, setAccount } = watchSession(view)

    // acme is somebody else's organization.
    await fill('acme', 'portal')
    await submit()

    await eventually(() => expect(body()).toContain('This organization already exists, or you are not its owner.'))
    expect(posts()).toEqual(['POST /access/account'])
    expect(alerts()).toEqual([])
    expect(setAccount).not.toHaveBeenCalled()
    expect(session.accountStatus).toBe('missing')
    // idle again: everything is enabled, and a change of step is not sent to "/".
    expect(button('Create and continue')?.disabled).toBe(false)
    expect(button('Continue with the exhibition only')?.disabled).toBe(false)
    await click(step('Step 2: Security'))
    await eventually(() => expect(stepHeadings()).toEqual(['Security first']))
    expect(view.route().fullPath).toBe('/onboarding?join=1&step=security')

    // The form kept its values; another name works.
    await click(step('Step 4: Organization'))
    await eventually(() => expect(stepHeadings()).toEqual(['Create your organization']))
    await fill('globex', 'portal')
    await submit()
    await eventually(() => expect(view.route().fullPath).toBe(ownPath))
    expect(posts()).toEqual(['POST /access/account', 'POST /access/account', 'POST /access/exhibition/join'])
  })

  it('shows any other failure of createAccount in the alert of the form', async () => {
    server.use(http.post(accountUrl, () => HttpResponse.json({ Message: 'The database is down.' }, { status: 500 })))
    const view = await mountWizard('/onboarding?step=organization')

    await fill('globex', 'portal')
    await submit()

    await eventually(() => expect(body()).toContain('The database is down.'))
    expect(view.route().path).toBe('/onboarding')
    expect(stepHeadings()).toEqual(['Create your organization'])
    expect(button('Create and continue')?.disabled).toBe(false)
  })

  it('keeps a failed join after the account was created out of the form, and tries it again', async () => {
    server.use(http.post(joinUrl, conflict))
    const view = await mountWizard('/onboarding?step=organization&join=1')
    const { session, setAccount, navigations } = watchSession(view)

    await fill('globex', 'portal')
    await submit()

    // joinFailed: the alert in place of the step list and the steps.
    await eventually(() =>
      expect(alerts()).toEqual([
        'Joining the exhibition failed Your account is ready. The access point changed meanwhile.',
      ]),
    )
    expect(posts()).toEqual(['POST /access/account', 'POST /access/exhibition/join'])
    // The 409 of the join is not "This organization already exists…".
    expect(body()).not.toContain('This organization already exists')
    expect(stepList()).toBeNull()
    expect(stepHeadings()).toEqual([])
    expect(document.body.querySelector('form')).toBeNull()
    expect(button('Sign out')).toBeTruthy()
    // The form took the focus with it: Try again has it now.
    await eventually(() => expect(document.activeElement).toBe(button('Try again')))

    // The account exists, but not in the session: the guard would send the next navigation of the wizard to "/".
    expect(loadDb().account?.AccessMap).toEqual({ globex: 'Owner', 'globex.portal': 'Owner' })
    expect(setAccount).not.toHaveBeenCalled()
    expect(session.accountStatus).toBe('missing')
    await view.router.push('/onboarding?step=security')
    expect(view.route().fullPath).toBe('/onboarding?step=security')
    expect(alerts()).toHaveLength(1)
    expect(stepList()).toBeNull()

    // Still failing: the alert stays, and both ways out work again.
    await click(button('Try again'))
    await eventually(() => expect(posts()).toHaveLength(3))
    await eventually(() => expect(button('Try again')?.disabled).toBe(false))
    expect(button('Continue without the exhibition')?.disabled).toBe(false)
    expect(alerts()).toHaveLength(1)
    expect(view.route().path).toBe('/onboarding')

    server.resetHandlers()
    await click(button('Try again'))

    // The end of the table: the new project, with the toast and its action.
    await eventually(() => expect(view.route().fullPath).toBe(ownPath))
    expect(posts()).toEqual([
      'POST /access/account',
      'POST /access/exhibition/join',
      'POST /access/exhibition/join',
      'POST /access/exhibition/join',
    ])
    expect(session.account?.AccessMap).toEqual({
      globex: 'Owner',
      'globex.portal': 'Owner',
      'exhibition.exhibition': 'Reader',
    })
    expect(setAccount).toHaveBeenCalledTimes(1)
    expect(navigations).toEqual(['/onboarding missing', `${ownPath} ready`])
    await eventually(() => expect(body()).toContain('You joined the exhibition'))
    expect(link('Open the exhibition')?.getAttribute('href')).toBe(exhibitionPath)
  })

  it('continues to the new project without the exhibition after a failed join', async () => {
    server.use(http.post(joinUrl, conflict))
    const view = await mountWizard('/onboarding?step=organization&join=1')
    const { session, setAccount } = watchSession(view)
    await fill('globex', 'portal')
    await submit()
    await eventually(() => expect(alerts()).toHaveLength(1))

    await click(button('Continue without the exhibition'))

    await eventually(() => expect(view.route().fullPath).toBe(ownPath))
    expect(posts()).toEqual(['POST /access/account', 'POST /access/exhibition/join'])
    expect(session.account?.AccessMap).toEqual({ globex: 'Owner', 'globex.portal': 'Owner' })
    expect(setAccount).toHaveBeenCalledTimes(1)
    expect(body()).not.toContain('You joined the exhibition')
  })

  it('disables the ways out of a failed join while it is tried again', async () => {
    server.use(http.post(joinUrl, conflict))
    const view = await mountWizard('/onboarding?step=organization&join=1')
    await fill('globex', 'portal')
    await submit()
    await eventually(() => expect(alerts()).toHaveLength(1))

    server.resetHandlers()
    const release = hold('post', joinUrl)
    await click(button('Try again'))
    await eventually(() => expect(posts()).toHaveLength(3))
    await settle()

    expect(button('Try again')?.disabled).toBe(true)
    expect(button('Continue without the exhibition')?.disabled).toBe(true)
    await click(button('Continue without the exhibition'))
    await click(button('Try again'))
    expect(view.route().path).toBe('/onboarding')
    expect(posts()).toHaveLength(3)

    release()
    await eventually(() => expect(view.route().fullPath).toBe(ownPath))
    expect(posts()).toHaveLength(3)
  })

  it('finds the account that a failed join of the exhibition only left, and goes on from it', async () => {
    // The API creates the account, then fails to join it.
    server.use(
      http.post(joinUrl, () => {
        const db = loadDb()
        db.account = { ...bareAccount }
        saveDb(db)
        return conflict()
      }),
    )
    const view = await mountWizard('/onboarding?step=organization&join=1')
    const { session, setAccount } = watchSession(view)

    await click(button('Continue with the exhibition only'))

    await eventually(() =>
      expect(alerts()).toEqual([
        'Joining the exhibition failed Your account is ready. The access point changed meanwhile.',
      ]),
    )
    // The guard asked for the account once; the page asks again after the failure.
    expect(requests).toEqual([
      'GET /access/account',
      'GET /access/exhibition',
      'POST /access/exhibition/join',
      'GET /access/account',
    ])
    expect(stepList()).toBeNull()
    expect(setAccount).not.toHaveBeenCalled()
    await view.router.push('/onboarding?step=exhibition&join=1')
    expect(view.route().path).toBe('/onboarding')
    expect(alerts()).toHaveLength(1)

    await click(button('Continue without the exhibition'))

    // No project of their own: the organizations, where New organization is.
    await eventually(() => expect(view.route().fullPath).toBe('/orgs'))
    expect(session.account).toEqual(bareAccount)
    expect(setAccount).toHaveBeenCalledTimes(1)
    expect(body()).not.toContain('You joined the exhibition')
  })

  it('tries the join of the exhibition only again from the account it left, and opens the exhibition', async () => {
    server.use(
      http.post(joinUrl, () => {
        const db = loadDb()
        db.account = { ...bareAccount }
        saveDb(db)
        return conflict()
      }),
    )
    const view = await mountWizard('/onboarding?step=organization&join=1')
    await click(button('Continue with the exhibition only'))
    await eventually(() => expect(alerts()).toHaveLength(1))

    server.resetHandlers()
    await click(button('Try again'))

    await eventually(() => expect(view.route().fullPath).toBe(exhibitionPath))
    expect(posts()).toEqual(['POST /access/exhibition/join', 'POST /access/exhibition/join'])
    expect(useSessionStore().account?.AccessMap).toEqual({ 'exhibition.exhibition': 'Reader' })
    await eventually(() => expect(body()).toContain('You joined the exhibition'))
    expect(link('Open the exhibition')).toBeUndefined()
  })

  it('shows a failed join of the exhibition only under its button when it left no account, and tries again', async () => {
    server.use(http.post(joinUrl, conflict))
    const view = await mountWizard('/onboarding?step=organization&join=1')
    const { session, setAccount } = watchSession(view)
    await fill('globex', 'portal')

    await click(button('Continue with the exhibition only'))

    await eventually(() => expect(alerts()).toEqual(['The access point changed meanwhile.']))
    expect(requests.slice(2)).toEqual(['POST /access/exhibition/join', 'GET /access/account'])
    // idle: the wizard as it was, with the form and its values.
    expect(body()).not.toContain('Joining the exhibition failed')
    expect(body()).not.toContain('This organization already exists')
    expect(stepHeadings()).toEqual(['Create your organization'])
    expect(fields().map((field) => field.value)).toEqual(['globex', 'portal', ''])
    expect(button('Create and continue')?.disabled).toBe(false)
    expect(button('Continue with the exhibition only')?.disabled).toBe(false)
    expect(setAccount).not.toHaveBeenCalled()
    expect(session.accountStatus).toBe('missing')
    await click(button('Back'))
    await eventually(() => expect(stepHeadings()).toEqual(['Join the exhibition']))
    expect(view.route().path).toBe('/onboarding')
    await click(button('Next'))
    await eventually(() => expect(stepHeadings()).toEqual(['Create your organization']))

    server.resetHandlers()
    await click(button('Continue with the exhibition only'))

    await eventually(() => expect(view.route().fullPath).toBe(exhibitionPath))
    expect(posts()).toEqual(['POST /access/exhibition/join', 'POST /access/exhibition/join'])
    expect(setAccount).toHaveBeenCalledTimes(1)
  })

  it('takes an account it cannot read for none', async () => {
    server.use(http.post(joinUrl, conflict))
    const view = await mountWizard('/onboarding?step=organization&join=1')
    server.use(http.get(accountUrl, () => HttpResponse.json({ Message: 'The database is down.' }, { status: 500 })))

    await click(button('Continue with the exhibition only'))

    await eventually(() => expect(alerts()).toEqual(['The access point changed meanwhile.']))
    expect(stepHeadings()).toEqual(['Create your organization'])
    expect(view.route().path).toBe('/onboarding')
  })

  it('clears the failure of the exhibition only when the form is submitted', async () => {
    server.use(http.post(joinUrl, conflict))
    await mountWizard('/onboarding?step=organization&join=1')
    await click(button('Continue with the exhibition only'))
    await eventually(() => expect(alerts()).toHaveLength(1))

    await fill('acme', 'portal')
    await submit()

    await eventually(() => expect(body()).toContain('This organization already exists, or you are not its owner.'))
    expect(alerts()).toEqual([])
  })
})
