import type { RouteLocationNormalized, RouteLocationRaw, Router } from 'vue-router'
import { useBootState } from '@/boot/state'
import { useSessionStore } from '@/stores/session'

/**
 * Spec section 3:
 * - without a working auth configuration every route shows /unsupported;
 * - public routes pass; a signed-out user is sent to AuthKit with the target as returnTo;
 * - the account is loaded once; a user without one goes to onboarding.
 */
export async function authGuard(to: RouteLocationNormalized): Promise<boolean | RouteLocationRaw> {
  const boot = useBootState().value

  if (boot.status !== 'ready') {
    return to.path === '/unsupported' ? true : { path: '/unsupported', replace: true }
  }

  if (to.meta.public) {
    return true
  }

  const session = useSessionStore()
  session.refreshUser()

  if (!session.user) {
    await session.signIn(to.fullPath)
    return false
  }

  await session.loadAccount()

  if (session.accountStatus === 'missing') {
    return to.meta.allowWithoutAccount ? true : { path: '/onboarding', replace: true }
  }

  if (session.accountStatus === 'ready' && to.path === '/onboarding') {
    return { path: '/', replace: true }
  }

  return true
}

export function installAuthGuard(router: Router): void {
  router.beforeEach(authGuard)
}
