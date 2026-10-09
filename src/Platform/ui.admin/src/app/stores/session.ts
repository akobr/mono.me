import { computed, ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'
import { getAccount, isNotFound, type Account, type AccountAccessMap } from '@42for.net/2splatform.sdk'
import { getAuthService } from '@/boot/state'
import type { AuthUser, SignInOptions } from '@/boot/auth'

export type AccountStatus = 'unknown' | 'loading' | 'ready' | 'missing' | 'error'

type SignOutHook = () => void | Promise<void>

const signOutHooks = new Set<SignOutHook>()

/** Registers cleanup that must run on sign-out (query cache, catalog, snapshots); returns the unregister function. */
export function onSignOut(hook: SignOutHook): () => void {
  signOutHooks.add(hook)
  return () => signOutHooks.delete(hook)
}

export const useSessionStore = defineStore('session', () => {
  const user = shallowRef<AuthUser | null>(getAuthService()?.getUser() ?? null)
  const account = shallowRef<Account | null>(null)
  const accountStatus = ref<AccountStatus>('unknown')
  const accountError = shallowRef<unknown>()
  let pending: Promise<void> | undefined

  const status = computed(() => (user.value ? 'signedIn' : 'signedOut'))
  const accessMap = computed<AccountAccessMap>(() => account.value?.AccessMap ?? {})

  function refreshUser(): void {
    user.value = getAuthService()?.getUser() ?? null
  }

  function setAccount(value: Account): void {
    account.value = value
    accountStatus.value = 'ready'
    accountError.value = undefined
  }

  /** Loads GET v1/access/account once per session; 404 means the user has no account yet (onboarding). */
  async function loadAccount(force = false): Promise<void> {
    if (!force && (accountStatus.value === 'ready' || accountStatus.value === 'missing')) {
      return
    }

    pending ??= (async () => {
      accountStatus.value = 'loading'

      try {
        setAccount(await getAccount())
      } catch (error) {
        account.value = null
        accountError.value = error
        accountStatus.value = isNotFound(error) ? 'missing' : 'error'
      } finally {
        pending = undefined
      }
    })()

    return pending
  }

  async function signIn(returnTo?: string, options: Omit<SignInOptions, 'returnTo'> = {}): Promise<void> {
    await getAuthService()?.signIn({ ...options, returnTo })
  }

  async function signOut(): Promise<void> {
    for (const hook of signOutHooks) {
      await hook()
    }

    account.value = null
    accountStatus.value = 'unknown'
    user.value = null
    await getAuthService()?.signOut(`${location.origin}/login`)
  }

  return {
    user,
    account,
    accountStatus,
    accountError,
    status,
    accessMap,
    refreshUser,
    setAccount,
    loadAccount,
    signIn,
    signOut,
  }
})
