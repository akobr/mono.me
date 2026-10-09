import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { AuthService } from '@/boot/auth'
import { setBootState } from '@/boot/state'
import { onSignOut, useSessionStore } from '@/stores/session'

const server = setupServer()
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function auth(): AuthService {
  return {
    kind: 'mock',
    getUser: () => ({ id: 'user_1', email: 'ada@example.com', name: 'Ada' }),
    getAccessToken: async () => 'token',
    signIn: vi.fn(async () => {}),
    signOut: vi.fn(async () => {}),
    consumeReturnTo: () => undefined,
  }
}

describe('session store', () => {
  it('reports API failures of the account as an error, not as a missing account', async () => {
    setBootState({ status: 'ready', auth: auth() })
    server.use(http.get('*/api/v1/access/account', () => HttpResponse.json({ Message: 'Down' }, { status: 503 })))

    const session = useSessionStore()
    await session.loadAccount()

    expect(session.accountStatus).toBe('error')
    expect(session.account).toBeNull()
  })

  it('shares one request between concurrent loads', async () => {
    setBootState({ status: 'ready', auth: auth() })
    let calls = 0
    server.use(http.get('*/api/v1/access/account', () => (calls++, HttpResponse.json({ Id: 'user_1', AccessMap: {} }))))

    const session = useSessionStore()
    await Promise.all([session.loadAccount(), session.loadAccount()])

    expect(calls).toBe(1)
    expect(session.accountStatus).toBe('ready')
  })

  it('runs the sign-out hooks, clears the account and signs out to /login', async () => {
    const service = auth()
    setBootState({ status: 'ready', auth: service })
    const hook = vi.fn()
    const off = onSignOut(hook)

    const session = useSessionStore()
    session.setAccount({ Id: 'user_1', AccessMap: { acme: 'Owner' } })
    await session.signOut()
    off()

    expect(hook).toHaveBeenCalledOnce()
    expect(session.account).toBeNull()
    expect(service.signOut).toHaveBeenCalledWith(`${location.origin}/login`)
  })
})
