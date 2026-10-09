import type { AuthService } from '@/boot/auth'
import { safeReturnTo } from '@/utils/return-to'
import { mockUser } from './db'

const sessionKey = '2s-admin:mock-session'
const returnToKey = '2s-admin:mock-return-to'

/**
 * Fake sign-in for the e2e mode. The user is signed in unless the tab signed out (or a test set the session
 * key to "signed-out"); signing in goes through /callback like the real redirect.
 */
export function createMockAuth(): AuthService {
  const signedIn = () => sessionStorage.getItem(sessionKey) !== 'signed-out'

  return {
    kind: 'mock',
    getUser: () => (signedIn() ? { id: mockUser.id, email: mockUser.email, name: mockUser.name } : null),
    getAccessToken: async () => (signedIn() ? 'mock-access-token' : undefined),
    signIn: async (options = {}) => {
      sessionStorage.setItem(sessionKey, 'signed-in')
      sessionStorage.setItem(returnToKey, safeReturnTo(options.returnTo) ?? '/')
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
  }
}
