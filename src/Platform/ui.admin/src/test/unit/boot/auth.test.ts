import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppConfig } from '@/boot/app-config'
import { createAuth, type AuthService } from '@/boot/auth'

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
    getUser: vi.fn(() => null),
    getAccessToken: vi.fn(async () => 'token'),
    signIn: vi.fn(async () => {}),
    signOut: vi.fn(),
  }

  return { NoSessionError, LoginRequiredError, client, createClient: vi.fn(async () => client) }
})

vi.mock('@workos-inc/authkit-js', () => ({
  createClient: authkit.createClient,
  NoSessionError: authkit.NoSessionError,
  LoginRequiredError: authkit.LoginRequiredError,
}))

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
