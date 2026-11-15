import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppConfig } from '@/boot/app-config'
import { authTimeOf, createAuth, signInState, stepUpReturnOf, withMaxAge, type AuthService } from '@/boot/auth'

const host = vi.hoisted(() => ({ local: true }))

vi.mock('@/boot/app-config', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/boot/app-config')>()),
  isLocalhost: () => host.local,
}))

// authkit-js is mocked: createClient resolves a fake client, and its error classes stay classes, so the service's
// instanceof checks work.
const authkit = vi.hoisted(() => {
  class NoSessionError extends Error {}
  class LoginRequiredError extends Error {}

  const client = {
    getUser: vi.fn((): unknown => null),
    getAccessToken: vi.fn(async () => 'token'),
    getSignInUrl: vi.fn(async (_options?: unknown) => 'https://auth.test/user_management/authorize?client_id=client_test&state=x'),
    signIn: vi.fn(async (_options?: unknown) => {}),
    signOut: vi.fn(),
  }

  // The options of the last createClient, for its redirect callback.
  const created: { onRedirectCallback?: (params: { state?: unknown }) => void } = {}

  return {
    NoSessionError,
    LoginRequiredError,
    client,
    created,
    createClient: vi.fn(async (_clientId: string, options: typeof created) => {
      created.onRedirectCallback = options.onRedirectCallback
      return client
    }),
  }
})

// getClaims stays the real decoder of the access token.
vi.mock('@workos-inc/authkit-js', async (importOriginal) => ({
  getClaims: (await importOriginal<typeof import('@workos-inc/authkit-js')>()).getClaims,
  createClient: authkit.createClient,
  NoSessionError: authkit.NoSessionError,
  LoginRequiredError: authkit.LoginRequiredError,
}))

/** An unsigned JWT with these claims: getClaims only decodes the payload. */
const tokenWith = (claims: Record<string, unknown>) =>
  ['{"alg":"none"}', JSON.stringify({ sub: 'user_1', ...claims })]
    .map((part) => btoa(part).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, ''))
    .concat('')
    .join('.')

const config: AppConfig = { apiBaseUrl: 'http://api.test/api', environmentName: 'test' }

/** The AuthKit service, with GET v1/auth/configuration answered from its sessionStorage cache. */
async function authKitService(appConfig: AppConfig = config): Promise<AuthService> {
  sessionStorage.setItem(
    `2s-admin:auth-configuration:${appConfig.apiBaseUrl}`,
    JSON.stringify({ Provider: 'AuthKit', ClientId: 'client_test' }),
  )

  const boot = await createAuth(appConfig)
  if (boot.status !== 'ready') {
    throw new Error(`The AuthKit service did not start: ${boot.status}`)
  }

  return boot.auth
}

describe('AuthKit service', () => {
  beforeEach(() => {
    authkit.createClient.mockClear()
    authkit.client.signOut.mockReset()
    host.local = true
  })

  describe('on a deployed host', () => {
    beforeEach(() => {
      host.local = false
    })

    it('uses the custom AuthKit domain and no devMode', async () => {
      await authKitService({ ...config, authApiHostname: 'auth.test', authLocalStorageSession: true })

      expect(authkit.createClient).toHaveBeenCalledWith(
        'client_test',
        expect.objectContaining({ devMode: false, apiHostname: 'auth.test' }),
      )
    })

    it('uses devMode without a custom domain only when the configuration opts in', async () => {
      await authKitService({ ...config, authLocalStorageSession: true })

      expect(authkit.createClient).toHaveBeenCalledWith(
        'client_test',
        expect.objectContaining({ devMode: true, apiHostname: undefined }),
      )
    })

    it('refuses to start without a custom domain or the opt-in', async () => {
      sessionStorage.setItem(
        `2s-admin:auth-configuration:${config.apiBaseUrl}`,
        JSON.stringify({ Provider: 'AuthKit', ClientId: 'client_test' }),
      )

      const boot = await createAuth(config)

      expect(boot).toMatchObject({ status: 'error' })
      expect(authkit.createClient).not.toHaveBeenCalled()
    })
  })

  it('creates the client in devMode on localhost, returning to /callback', async () => {
    await authKitService()

    expect(authkit.createClient).toHaveBeenCalledWith(
      'client_test',
      expect.objectContaining({ devMode: true, apiHostname: undefined, redirectUri: `${location.origin}/callback` }),
    )
  })

  // WorkOS returns to the target only when it is a Sign-out URI of the environment (or its default one); the logout
  // must still go through WorkOS, which ends the session, rather than a background request the app cannot check.
  it('signs out through the WorkOS logout with the target as returnTo', async () => {
    const assign = vi.spyOn(location, 'assign').mockImplementation(() => {})
    const auth = await authKitService()

    await auth.signOut(`${location.origin}/login`)

    expect(authkit.client.signOut).toHaveBeenCalledExactlyOnceWith({ returnTo: `${location.origin}/login` })
    expect(assign).not.toHaveBeenCalled()
  })

  it('goes to the target itself when AuthKit has no session', async () => {
    const assign = vi.spyOn(location, 'assign').mockImplementation(() => {})
    authkit.client.signOut.mockImplementation(() => {
      throw new authkit.NoSessionError('no session')
    })
    const auth = await authKitService()

    await auth.signOut(`${location.origin}/login`)

    expect(assign).toHaveBeenCalledExactlyOnceWith(`${location.origin}/login`)
  })

  it('keeps the discovery in the boot result, for the step-up of the screens', async () => {
    sessionStorage.setItem(
      `2s-admin:auth-configuration:${config.apiBaseUrl}`,
      JSON.stringify({ Provider: 'AuthKit', ClientId: 'client_test', StepUp: { Enabled: true, MaxAge: 600 } }),
    )

    const boot = await createAuth(config)

    expect(boot).toMatchObject({ status: 'ready', authConfiguration: { StepUp: { Enabled: true, MaxAge: 600 } } })
  })

  it('signs in as before without maxAge', async () => {
    const assign = vi.spyOn(location, 'assign').mockImplementation(() => {})
    authkit.client.signIn.mockClear()
    authkit.client.getSignInUrl.mockClear()
    const auth = await authKitService()

    await auth.signIn({ returnTo: '/acme/billing', loginHint: 'ada@example.com' })

    expect(authkit.client.signIn).toHaveBeenCalledExactlyOnceWith({
      state: { returnTo: '/acme/billing' },
      invitationToken: undefined,
      loginHint: 'ada@example.com',
    })
    expect(authkit.client.getSignInUrl).not.toHaveBeenCalled()
    expect(assign).not.toHaveBeenCalled()
  })

  // authkit-js has no max_age option: the URL of getSignInUrl (same state, login hint and PKCE verifier) gets it.
  it('signs in with max_age on the URL of getSignInUrl and the step-up marker in the state', async () => {
    const assign = vi.spyOn(location, 'assign').mockImplementation(() => {})
    authkit.client.signIn.mockClear()
    authkit.client.getSignInUrl.mockClear()
    authkit.client.getUser.mockReturnValue({ id: 'user_1' })
    authkit.client.getAccessToken.mockResolvedValue(tokenWith({ auth_time: 1_700_000_000 }))
    vi.spyOn(Date, 'now').mockReturnValue(1_700_000_900_000)
    const auth = await authKitService()

    await auth.signIn({ returnTo: '/acme/billing/default/key-values?key=db.password', loginHint: 'ada@example.com', maxAge: 0 })

    expect(authkit.client.signIn).not.toHaveBeenCalled()
    expect(authkit.client.getSignInUrl).toHaveBeenCalledExactlyOnceWith({
      state: {
        returnTo: '/acme/billing/default/key-values?key=db.password',
        stepUp: 1_700_000_900_000,
        stepUpAuthTime: 1_700_000_000_000,
      },
      invitationToken: undefined,
      loginHint: 'ada@example.com',
    })
    const target = new URL(assign.mock.calls[0]![0] as string)
    expect(target.origin).toBe('https://auth.test')
    expect(target.searchParams.get('max_age')).toBe('0')
    expect(target.searchParams.get('state')).toBe('x')
    authkit.client.getUser.mockReturnValue(null)
  })

  it('records the step-up only when the callback brings its marker back', async () => {
    const auth = await authKitService()
    expect(auth.getStepUpReturn()).toBeUndefined()

    authkit.created.onRedirectCallback?.({ state: { returnTo: '/orgs' } })
    expect(auth.getStepUpReturn()).toBeUndefined()
    expect(auth.consumeReturnTo()).toBe('/orgs')

    vi.spyOn(Date, 'now').mockReturnValue(5_000)
    authkit.created.onRedirectCallback?.({ state: { returnTo: '/acme', stepUp: 1_000, stepUpAuthTime: 500 } })
    expect(auth.getStepUpReturn()).toEqual({ startedAt: 1_000, previousAuthTime: 500, returnedAt: 5_000 })
    expect(auth.consumeReturnTo()).toBe('/acme')
  })

  it('reads auth_time of the current access token in milliseconds', async () => {
    authkit.client.getUser.mockReturnValue({ id: 'user_1' })
    authkit.client.getAccessToken.mockResolvedValue(tokenWith({ auth_time: 1_700_000_000 }))
    const auth = await authKitService()

    await expect(auth.getAuthTime()).resolves.toBe(1_700_000_000_000)

    authkit.client.getAccessToken.mockResolvedValue(tokenWith({}))
    await expect(auth.getAuthTime()).resolves.toBeUndefined()

    authkit.client.getUser.mockReturnValue(null)
    await expect(auth.getAuthTime()).resolves.toBeUndefined()
  })

  it('passes on any other error of the sign-out', async () => {
    const assign = vi.spyOn(location, 'assign').mockImplementation(() => {})
    authkit.client.signOut.mockImplementation(() => {
      throw new Error('storage unavailable')
    })
    const auth = await authKitService()

    await expect(auth.signOut(`${location.origin}/login`)).rejects.toThrow('storage unavailable')
    expect(assign).not.toHaveBeenCalled()
  })
})

describe('step-up sign-in helpers', () => {
  it('puts the start and the sign-in before it into the state only for a step-up', () => {
    expect(signInState({ returnTo: '//evil.test' })).toEqual({ returnTo: '/' })
    expect(signInState({ returnTo: '/a', maxAge: 0 }, 1_000, 2_000)).toEqual({ returnTo: '/a', stepUp: 2_000, stepUpAuthTime: 1_000 })
  })

  it('reads a returned step-up as untrusted input', () => {
    expect(stepUpReturnOf(undefined)).toBeUndefined()
    expect(stepUpReturnOf({ returnTo: '/' })).toBeUndefined()
    expect(stepUpReturnOf({ stepUp: '1000' })).toBeUndefined()
    expect(stepUpReturnOf({ stepUp: 1_000, stepUpAuthTime: 'x' }, 3_000)).toEqual({
      startedAt: 1_000,
      previousAuthTime: undefined,
      returnedAt: 3_000,
    })
  })

  it('appends max_age in whole seconds and keeps the other parameters', () => {
    const url = withMaxAge('https://auth.test/authorize?client_id=c&state=%7B%7D', 0)

    expect(new URL(url).searchParams.get('max_age')).toBe('0')
    expect(new URL(url).searchParams.get('state')).toBe('{}')
    expect(new URL(withMaxAge(url, 59.9)).searchParams.get('max_age')).toBe('59')
    expect(new URL(withMaxAge(url, -5)).searchParams.get('max_age')).toBe('0')
  })

  it('reads auth_time from a token, and nothing from a token that is not a JWT', () => {
    expect(authTimeOf(tokenWith({ auth_time: 1_700_000_000 }))).toBe(1_700_000_000_000)
    expect(authTimeOf(tokenWith({ auth_time: '1700000000' }))).toBeUndefined()
    expect(authTimeOf('mock-access-token')).toBeUndefined()
    expect(authTimeOf(undefined)).toBeUndefined()
  })
})
