import { createClient, LoginRequiredError, NoSessionError, type User } from '@workos-inc/authkit-js'
import { getAuthConfiguration, type AuthConfiguration } from '@42for.net/2splatform.sdk'
import { isLocalhost, type AppConfig } from './app-config'
import { safeReturnTo } from '@/utils/return-to'

export interface AuthUser {
  id: string
  email: string
  name: string
  pictureUrl?: string
}

export interface SignInOptions {
  /** Same-origin path to return to after the redirect. */
  returnTo?: string
  /** WorkOS invitation token; lets an invited user sign up even when sign-up is disabled. */
  invitationToken?: string
  loginHint?: string
}

export interface AuthService {
  readonly kind: 'authkit' | 'mock'
  getUser(): AuthUser | null
  getAccessToken(options?: { forceRefresh?: boolean }): Promise<string | undefined>
  signIn(options?: SignInOptions): Promise<void>
  signOut(returnTo: string): Promise<void>
  /** The returnTo of the completed redirect, read once by the /callback page. */
  consumeReturnTo(): string | undefined
}

export type AuthBoot =
  | { status: 'ready'; auth: AuthService }
  | { status: 'unsupported'; provider: string }
  | { status: 'error'; message: string }

const configurationCacheKey = (apiBaseUrl: string) => `2s-admin:auth-configuration:${apiBaseUrl}`

/** GET v1/auth/configuration (anonymous), cached in sessionStorage for the lifetime of the tab. */
export async function loadAuthConfiguration(config: AppConfig): Promise<AuthConfiguration> {
  const key = configurationCacheKey(config.apiBaseUrl)
  const cached = sessionStorage.getItem(key)

  if (cached) {
    return JSON.parse(cached) as AuthConfiguration
  }

  const configuration = await getAuthConfiguration()
  sessionStorage.setItem(key, JSON.stringify(configuration))
  return configuration
}

export const toAuthUser = (user: User): AuthUser => ({
  id: user.id,
  email: user.email,
  name: [user.firstName, user.lastName].filter(Boolean).join(' ') || user.email,
  pictureUrl: user.profilePictureUrl ?? undefined,
})

const currentPath = () => `${location.pathname}${location.search}${location.hash}`

/**
 * Creates the AuthKit client. Without a custom AuthKit domain the refresh token lives in localStorage
 * (devMode), so that is allowed on localhost only; any other host without authApiHostname is a configuration error.
 */
export async function createAuth(config: AppConfig): Promise<AuthBoot> {
  const configuration = await loadAuthConfiguration(config)

  if (configuration.Provider !== 'AuthKit') {
    return { status: 'unsupported', provider: configuration.Provider ?? 'unknown' }
  }

  if (!configuration.ClientId) {
    return { status: 'error', message: 'The API reports AuthKit without a client ID.' }
  }

  const local = isLocalhost()

  if (!local && !config.authApiHostname) {
    return {
      status: 'error',
      message:
        'authApiHostname is missing in /app-config.json. Deployed environments must use a custom AuthKit domain.',
    }
  }

  let returnTo: string | undefined

  const client = await createClient(configuration.ClientId, {
    apiHostname: local ? undefined : config.authApiHostname,
    devMode: local,
    redirectUri: `${location.origin}/callback`,
    onRedirectCallback: ({ state }) => {
      returnTo = safeReturnTo(state?.returnTo)
    },
    onRefreshFailure: ({ signIn }) => {
      void signIn({ state: { returnTo: currentPath() } })
    },
  })

  return {
    status: 'ready',
    auth: {
      kind: 'authkit',
      getUser: () => {
        const user = client.getUser()
        return user ? toAuthUser(user) : null
      },
      getAccessToken: async (options) => {
        if (!client.getUser()) {
          return undefined
        }

        try {
          return await client.getAccessToken({ forceRefresh: options?.forceRefresh })
        } catch (error) {
          if (error instanceof LoginRequiredError) {
            return undefined
          }
          throw error
        }
      },
      signIn: (options = {}) =>
        client.signIn({
          state: { returnTo: safeReturnTo(options.returnTo) ?? '/' },
          invitationToken: options.invitationToken,
          loginHint: options.loginHint,
        }),
      signOut: async (target) => {
        try {
          client.signOut({ returnTo: target })
        } catch (error) {
          if (!(error instanceof NoSessionError)) {
            throw error
          }
          location.assign(target)
        }
      },
      consumeReturnTo: () => {
        const value = returnTo
        returnTo = undefined
        return value
      },
    },
  }
}
