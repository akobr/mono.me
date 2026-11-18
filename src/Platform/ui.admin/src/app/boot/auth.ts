import { createClient, getClaims, LoginRequiredError, NoSessionError, type User } from '@workos-inc/authkit-js'
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
  /**
   * A step-up: the oldest sign-in in seconds that WorkOS still accepts without asking the person to authenticate again
   * (max_age of the authorization URL, RFC 9470); 0 always asks. The start of the step-up travels in the state, so the
   * return can be told from a plain sign-in (getStepUpReturn).
   */
  maxAge?: number
}

/** The step-up sign-in that the completed redirect came back from. */
export interface StepUpReturn {
  /** When the step-up started, in milliseconds since the epoch (the browser's clock). */
  startedAt: number
  /** The sign-in before the step-up (auth_time of the token then), in milliseconds since the epoch; undefined if unknown. */
  previousAuthTime?: number
  /** When the redirect came back, in milliseconds since the epoch (the browser's clock). */
  returnedAt: number
}

export interface AuthService {
  readonly kind: 'authkit' | 'mock'
  getUser(): AuthUser | null
  getAccessToken(options?: { forceRefresh?: boolean }): Promise<string | undefined>
  /**
   * The time of the person's last active sign-in (auth_time of the current access token), in milliseconds since the
   * epoch; undefined without a token or without the claim. A token refresh does not move it.
   */
  getAuthTime(): Promise<number | undefined>
  signIn(options?: SignInOptions): Promise<void>
  signOut(returnTo: string): Promise<void>
  /** The returnTo of the completed redirect, read once by the /callback page. */
  consumeReturnTo(): string | undefined
  /** The step-up the page came back from, for the lifetime of the page; undefined after a plain sign-in or none. */
  getStepUpReturn(): StepUpReturn | undefined
}

export type AuthBoot =
  | { status: 'ready'; auth: AuthService; authConfiguration?: AuthConfiguration }
  | { status: 'unsupported'; provider: string }
  | { status: 'error'; message: string }

/** The state of a sign-in: where to return, and for a step-up when it started and the sign-in before it. */
export interface SignInState {
  returnTo: string
  stepUp?: number
  stepUpAuthTime?: number
}

/** The state of a sign-in with these options; previousAuthTime is the auth_time of the token before a step-up. */
export function signInState(options: SignInOptions, previousAuthTime?: number, now = Date.now()): SignInState {
  return {
    returnTo: safeReturnTo(options.returnTo) ?? '/',
    ...(options.maxAge === undefined ? {} : { stepUp: now, stepUpAuthTime: previousAuthTime }),
  }
}

/** The step-up of a returned state; anything that is not a number is none (the state is untrusted input). */
export function stepUpReturnOf(state: unknown, now = Date.now()): StepUpReturn | undefined {
  const record = typeof state === 'object' && state !== null ? (state as Record<string, unknown>) : undefined
  const startedAt = record?.stepUp
  const previous = record?.stepUpAuthTime

  if (typeof startedAt !== 'number' || !Number.isFinite(startedAt)) {
    return undefined
  }

  return {
    startedAt,
    previousAuthTime: typeof previous === 'number' && Number.isFinite(previous) ? previous : undefined,
    returnedAt: now,
  }
}

/** The authorization URL of a step-up: max_age in whole seconds, 0 or more. */
export function withMaxAge(url: string, maxAge: number): string {
  const target = new URL(url)
  target.searchParams.set('max_age', String(Math.max(0, Math.floor(maxAge))))
  return target.toString()
}

/** auth_time of an access token in milliseconds since the epoch; undefined when the token has none or is no JWT. */
export function authTimeOf(accessToken: string | undefined): number | undefined {
  if (!accessToken) {
    return undefined
  }

  try {
    const authTime = getClaims<{ auth_time?: unknown }>(accessToken).auth_time
    return typeof authTime === 'number' && Number.isFinite(authTime) && authTime > 0 ? authTime * 1000 : undefined
  } catch {
    return undefined
  }
}

const configurationCacheKey = (apiBaseUrl: string) => `2s-admin:auth-configuration:${apiBaseUrl}`

/**
 * How long a tab keeps the discovery: an API deployment that turns its step-up on (or changes its window) reaches an
 * open tab at its first load after that.
 */
export const authConfigurationCacheMs = 5 * 60_000

/** The discovery in sessionStorage, with when it was read (milliseconds since the epoch). */
interface CachedAuthConfiguration {
  storedAt: number
  configuration: AuthConfiguration
}

/** The cached discovery while younger than authConfigurationCacheMs; anything else (an older format, a bad entry) is none. */
function cachedConfiguration(key: string, now: number): AuthConfiguration | undefined {
  try {
    const cached = JSON.parse(sessionStorage.getItem(key) ?? 'null') as Partial<CachedAuthConfiguration> | null
    const age = typeof cached?.storedAt === 'number' ? now - cached.storedAt : Number.NaN
    return age >= 0 && age < authConfigurationCacheMs ? cached?.configuration : undefined
  } catch {
    return undefined
  }
}

/** GET v1/auth/configuration (anonymous), cached in sessionStorage for authConfigurationCacheMs. */
export async function loadAuthConfiguration(config: AppConfig): Promise<AuthConfiguration> {
  const key = configurationCacheKey(config.apiBaseUrl)
  const cached = cachedConfiguration(key, Date.now())

  if (cached) {
    return cached
  }

  const configuration = await getAuthConfiguration()
  sessionStorage.setItem(key, JSON.stringify({ storedAt: Date.now(), configuration } satisfies CachedAuthConfiguration))
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
 * (devMode), so that is allowed on localhost, and on a deployed host only when its configuration opts in with
 * authLocalStorageSession (a stopgap until the custom domain exists); otherwise a host without authApiHostname is a
 * configuration error.
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

  const localStorageSession = !local && !config.authApiHostname && config.authLocalStorageSession === true

  if (!local && !config.authApiHostname && !localStorageSession) {
    return {
      status: 'error',
      message:
        'authApiHostname is missing in /app-config.json. Deployed environments must use a custom AuthKit domain, ' +
        'or opt in to a localStorage session with authLocalStorageSession.',
    }
  }

  let returnTo: string | undefined
  let stepUpReturn: StepUpReturn | undefined

  const client = await createClient(configuration.ClientId, {
    apiHostname: local ? undefined : config.authApiHostname,
    devMode: local || localStorageSession,
    redirectUri: `${location.origin}/callback`,
    onRedirectCallback: ({ state }) => {
      returnTo = safeReturnTo(state?.returnTo)
      // Recorded only when the callback brings the marker back: the loop guard of the step-up.
      stepUpReturn = stepUpReturnOf(state)
    },
    onRefreshFailure: ({ signIn }) => {
      void signIn({ state: { returnTo: currentPath() } })
    },
  })

  // auth_time of the last access token the page got. A refresh keeps it, so it stays the sign-in's time for the page.
  // The step-up's sign-in reads it, not a token: a token request could refresh, fail and start a plain sign-in
  // (onRefreshFailure) whose PKCE verifier would replace the step-up's.
  let lastAuthTime: number | undefined

  const getAccessToken = async (options?: { forceRefresh?: boolean }) => {
    if (!client.getUser()) {
      return undefined
    }

    try {
      const token = await client.getAccessToken({ forceRefresh: options?.forceRefresh })
      lastAuthTime = authTimeOf(token) ?? lastAuthTime
      return token
    } catch (error) {
      if (error instanceof LoginRequiredError) {
        return undefined
      }
      throw error
    }
  }

  const getAuthTime = async () => authTimeOf(await getAccessToken().catch(() => undefined))

  return {
    status: 'ready',
    authConfiguration: configuration,
    auth: {
      kind: 'authkit',
      getUser: () => {
        const user = client.getUser()
        return user ? toAuthUser(user) : null
      },
      getAccessToken,
      getAuthTime,
      signIn: async (options = {}) => {
        const redirect = {
          state: signInState(options, options.maxAge === undefined ? undefined : lastAuthTime),
          invitationToken: options.invitationToken,
          loginHint: options.loginHint,
        }

        if (options.maxAge === undefined) {
          return await client.signIn(redirect)
        }

        // authkit-js 0.20 has no max_age option. getSignInUrl stores the PKCE verifier exactly as signIn does, so the
        // parameter appended to its URL survives the code exchange.
        location.assign(withMaxAge(await client.getSignInUrl(redirect), options.maxAge))
      },
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
      getStepUpReturn: () => stepUpReturn,
    },
  }
}
