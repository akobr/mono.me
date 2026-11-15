import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { useToast } from '@nuxt/ui/composables'
import type { Exhibition } from '@42for.net/2splatform.sdk'
import { useOnboardingEnd } from '@/composables/useOnboardingEnd'
import { mockHandlers } from '@/mock/all-handlers'
import { resetDb } from '@/mock/db'
import { useSessionStore } from '@/stores/session'
import { mountScreen } from '../../helpers'

// The end of the onboarding wizard as the organization form sees it (424-267, spec 3.6). The form maps every 409 of its
// submit to "This organization already exists…", so only a failed createAccount may reach it: a failed join must not.

const server = setupServer(...mockHandlers)
const requests: string[] = []
const mounted: { host: { unmount: () => void } }[] = []

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
  server.events.on('request:start', ({ request }) => {
    requests.push(`${request.method} ${new URL(request.url).pathname.replace(/^\/api\/v1/, '')}`)
  })
})
beforeEach(() => {
  requests.length = 0
  resetDb('new-user')
})
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  document.body.innerHTML = ''
  createApp({}).runWithContext(() => useToast().clear())
})
afterAll(() => server.close())

const exhibition: Exhibition = { Organization: 'exhibition', Project: 'exhibition' }
const values = { organization: 'globex', project: 'portal', description: '' }
const conflict = () =>
  HttpResponse.json({ Message: 'The access point changed meanwhile.', ErrorCode: 'Conflict' }, { status: 409 })

async function mountEnd() {
  let end!: ReturnType<typeof useOnboardingEnd>
  const Host = {
    setup() {
      end = useOnboardingEnd(() => exhibition)
      return () => null
    },
  }
  const view = await mountScreen(Host, {}, { path: '/onboarding', account: null })
  mounted.push(view)
  return { end, router: view.router, session: useSessionStore() }
}

describe('useOnboardingEnd', () => {
  it('throws a failed createAccount for the form, joins nothing and is idle again', async () => {
    const { end, router, session } = await mountEnd()

    await expect(end.createOrganization({ ...values, organization: 'acme' }, true)).rejects.toMatchObject({
      status: 409,
      errorCode: 'AccessPointExists',
    })

    expect(end.state.value).toBe('idle')
    expect(end.joinError.value).toBeUndefined()
    expect(requests).toEqual(['POST /access/account'])
    expect(session.account).toBeNull()
    expect(router.currentRoute.value.path).toBe('/onboarding')
  })

  it('does not throw a failed join after the account was created, which the form would show as its own', async () => {
    server.use(http.post('*/api/v1/access/exhibition/join', conflict))
    const { end, router, session } = await mountEnd()

    await expect(end.createOrganization(values, true)).resolves.toBeUndefined()

    expect(end.state.value).toBe('joinFailed')
    expect(end.joinError.value).toMatchObject({ status: 409, message: 'The access point changed meanwhile.' })
    expect(end.exhibitionOnlyError.value).toBeUndefined()
    expect(requests).toEqual(['POST /access/account', 'POST /access/exhibition/join'])
    // The account stays out of the session until the wizard leaves.
    expect(session.account).toBeNull()
    expect(router.currentRoute.value.path).toBe('/onboarding')
  })

  // A navigation rejects when a guard throws or a page does not load. By then the session has the account, and the
  // guard would send every further step of the wizard to "/": a full load of the target ends the wizard instead.
  it('ends with a full load of the target when the last navigation fails', async () => {
    const replace = vi.spyOn(window.location, 'replace').mockImplementation(() => undefined)
    const { end, router, session } = await mountEnd()
    router.beforeEach((to) => {
      if (to.path !== '/onboarding') {
        throw new Error('The page did not load.')
      }
    })

    // Nothing is thrown for the form to show as its own.
    await expect(end.createOrganization(values, false)).resolves.toBeUndefined()

    expect(session.account?.AccessMap).toEqual({ globex: 'Owner', 'globex.portal': 'Owner' })
    expect(replace).toHaveBeenCalledExactlyOnceWith('/globex/portal/default/annotations')
    replace.mockRestore()
  })

  it('does nothing in a state that has no such way on', async () => {
    const { end, router } = await mountEnd()

    // idle has no failed join to try again or to leave.
    await end.retryJoin()
    await end.continueWithout()
    expect(requests).toEqual([])
    expect(router.currentRoute.value.path).toBe('/onboarding')

    // joinFailed is past the form and past "Continue with the exhibition only".
    server.use(http.post('*/api/v1/access/exhibition/join', conflict))
    await end.createOrganization(values, true)
    expect(end.state.value).toBe('joinFailed')
    requests.length = 0
    await end.createOrganization(values, true)
    await end.joinOnly()
    expect(requests).toEqual([])
    expect(end.state.value).toBe('joinFailed')
  })
})
