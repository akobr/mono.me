import { signInState, stepUpReturnOf, type AuthService } from '@/boot/auth'
import { safeReturnTo } from '@/utils/return-to'
import { mockUser } from './db'
import { isStaleSignIn, setStaleSignIn } from './step-up'

const sessionKey = '2s-admin:mock-session'
const returnToKey = '2s-admin:mock-return-to'
const stepUpKey = '2s-admin:mock-step-up'

/** How old the sign-in of a stale mock session is: well past the mock API's window of 10 minutes. */
const staleAgeMs = 60 * 60_000

/**
 * Fake sign-in for the e2e mode. The user is signed in unless the tab signed out (or a test set the session
 * key to "signed-out"); signing in goes through /callback like the real redirect. The sign-in is always recent, unless
 * window.storytellerMock.staleSignIn() made it stale; a sign-in with maxAge (the step-up) makes it recent again and
 * brings its marker back like the AuthKit state.
 */
export function createMockAuth(): AuthService {
  const signedIn = () => sessionStorage.getItem(sessionKey) !== 'signed-out'
  const getAuthTime = async () => (signedIn() ? Date.now() - (isStaleSignIn() ? staleAgeMs : 0) : undefined)

  // The state of the redirect that brought the page here, read once per page like the AuthKit callback.
  const stored = sessionStorage.getItem(stepUpKey)
  sessionStorage.removeItem(stepUpKey)
  const stepUpReturn = stepUpReturnOf(parse(stored))

  return {
    kind: 'mock',
    getUser: () => (signedIn() ? { id: mockUser.id, email: mockUser.email, name: mockUser.name } : null),
    getAccessToken: async () => (signedIn() ? 'mock-access-token' : undefined),
    getAuthTime,
    signIn: async (options = {}) => {
      const state = signInState(options, options.maxAge === undefined ? undefined : await getAuthTime())
      if (options.maxAge !== undefined) {
        sessionStorage.setItem(stepUpKey, JSON.stringify(state))
        setStaleSignIn(false)
      }
      sessionStorage.setItem(sessionKey, 'signed-in')
      sessionStorage.setItem(returnToKey, state.returnTo)
      location.assign('/callback')
    },
    signOut: async (target) => {
      sessionStorage.setItem(sessionKey, 'signed-out')
      location.assign(target)
    },
    consumeReturnTo: () => {
      const value = sessionStorage.getItem(returnToKey)
      sessionStorage.removeItem(returnToKey)
      return safeReturnTo(value)
    },
    getStepUpReturn: () => stepUpReturn,
  }
}

function parse(text: string | null): unknown {
  try {
    return text ? (JSON.parse(text) as unknown) : undefined
  } catch {
    return undefined
  }
}
