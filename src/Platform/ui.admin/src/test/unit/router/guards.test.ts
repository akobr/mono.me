import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { RouteLocationNormalized } from 'vue-router'
import type { AuthService } from '@/boot/auth'
import { setBootState } from '@/boot/state'
import { authGuard } from '@/router/guards/auth'
import { contextGuard } from '@/router/guards/context'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'

const server = setupServer()
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const route = (path: string, extra: Partial<RouteLocationNormalized> = {}) =>
  ({ path, fullPath: path, params: {}, query: {}, meta: {}, ...extra }) as RouteLocationNormalized

function fakeAuth(signedIn = true): AuthService {
  return {
    kind: 'mock',
    getUser: () => (signedIn ? { id: 'user_1', email: 'ada@example.com', name: 'Ada' } : null),
    getAccessToken: async () => (signedIn ? 'token' : undefined),
    signIn: vi.fn(async () => {}),
    signOut: vi.fn(async () => {}),
    consumeReturnTo: () => undefined,
  }
}

const account = (accessMap: Record<string, string>) =>
  http.get('*/api/v1/access/account', () => HttpResponse.json({ Id: 'user_1', AccessMap: accessMap }))

describe('auth guard', () => {
  beforeEach(() => setBootState({ status: 'ready', auth: fakeAuth() }))

  it('sends every route to /unsupported when the deployment cannot sign in', async () => {
    setBootState({ status: 'unsupported', provider: 'EntraId' })

    await expect(authGuard(route('/orgs'))).resolves.toEqual({ path: '/unsupported', replace: true })
    await expect(authGuard(route('/unsupported'))).resolves.toBe(true)
  })

  it('lets public routes pass without a user', async () => {
    setBootState({ status: 'ready', auth: fakeAuth(false) })

    await expect(authGuard(route('/login', { meta: { public: true } }))).resolves.toBe(true)
  })

  it('signs a signed-out user in with the target as returnTo', async () => {
    const auth = fakeAuth(false)
    setBootState({ status: 'ready', auth })

    await expect(authGuard(route('/acme/billing/default/annotations?q=x'))).resolves.toBe(false)
    expect(auth.signIn).toHaveBeenCalledWith({ returnTo: '/acme/billing/default/annotations?q=x' })
  })

  it('sends a user without an account to onboarding', async () => {
    server.use(
      http.get('*/api/v1/access/account', () => HttpResponse.json({ Message: 'No account.' }, { status: 404 })),
    )

    await expect(authGuard(route('/orgs'))).resolves.toEqual({ path: '/onboarding', replace: true })
    await expect(authGuard(route('/onboarding', { meta: { allowWithoutAccount: true } }))).resolves.toBe(true)
    expect(useSessionStore().accountStatus).toBe('missing')
  })

  it('loads the account once and keeps users with an account out of onboarding', async () => {
    let calls = 0
    server.use(
      http.get(
        '*/api/v1/access/account',
        () => (calls++, HttpResponse.json({ Id: 'user_1', AccessMap: { acme: 'Owner' } })),
      ),
    )

    await expect(authGuard(route('/orgs'))).resolves.toBe(true)
    await expect(authGuard(route('/onboarding', { meta: { allowWithoutAccount: true } }))).resolves.toEqual({
      path: '/',
      replace: true,
    })
    expect(calls).toBe(1)
    expect(useSessionStore().accessMap).toEqual({ acme: 'Owner' })
  })
})

describe('context guard', () => {
  beforeEach(async () => {
    setBootState({ status: 'ready', auth: fakeAuth() })
    server.use(account({ 'acme.billing': 'Reader', 'northwind.portal': 'None' }))
    await useSessionStore().loadAccount()
  })

  it('sets the context and remembers it for "/"', () => {
    contextGuard(
      route('/acme/billing/staging/annotations', { params: { org: 'acme', project: 'billing', view: 'staging' } }),
    )

    expect(useContextStore().scope).toEqual({ org: 'acme', project: 'billing', view: 'staging' })
    expect(useContextStore().access).toBe('ok')
    expect(usePreferencesStore().state.lastContext).toEqual({ org: 'acme', project: 'billing', view: 'staging' })
  })

  it('checks the exact project key: an organization or a None role is not enough', () => {
    contextGuard(
      route('/acme/shipping/default/annotations', { params: { org: 'acme', project: 'shipping', view: 'default' } }),
    )
    expect(useContextStore().access).toBe('forbidden')

    contextGuard(route('/northwind/portal', { params: { org: 'northwind', project: 'portal' } }))
    expect(useContextStore().access).toBe('forbidden')
    expect(usePreferencesStore().state.lastContext).toBeUndefined()
  })

  it('allows an organization page to members of one of its projects', () => {
    contextGuard(route('/acme', { params: { org: 'acme' } }))
    expect(useContextStore().access).toBe('ok')

    contextGuard(route('/globex', { params: { org: 'globex' } }))
    expect(useContextStore().access).toBe('forbidden')
  })

  it('clears the context outside organizations', () => {
    contextGuard(route('/acme', { params: { org: 'acme' } }))
    contextGuard(route('/orgs'))

    expect(useContextStore().org).toBeUndefined()
  })
})
