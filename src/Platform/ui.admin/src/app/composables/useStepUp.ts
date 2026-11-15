import { ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'
import { stepUpRequiredCode } from '@42for.net/2splatform.sdk'
import { getAuthService, getStepUpSettings } from '@/boot/state'
import type { StepUpReturn } from '@/boot/auth'
import { useSessionStore } from '@/stores/session'
import { hasUnsavedChanges, leaveWithoutPrompt } from './useUnsavedChanges'

/**
 * The step-up of the admin UI (424-264): some operations need a sign-in within the API's window (StepUp.MaxAge of
 * GET v1/auth/configuration, 10 minutes by default). A token refresh keeps the time of the sign-in, so the person signs
 * in again with max_age=0 (a forced re-authentication) and comes back to the same page. Writes are never replayed;
 * reads run again with the page, and a Reveal that failed is repeated once.
 */

/** The confirmation, or the error of a step-up that came back without a newer sign-in (the loop guard). */
export type StepUpMode = 'confirm' | 'failed'

/** A Reveal to repeat after the return: the key of a scope ("org/project/view"), and its version for a past one. */
export interface RevealTarget {
  scope: string
  key: string
  version?: number
}

const pendingRevealKey = '2s-admin:step-up-reveal'

/** True when the sign-in moved since the step-up started; with no sign-in time before it, since the start itself. */
export function signedInSince(returned: StepUpReturn, authTime: number | undefined): boolean {
  return authTime !== undefined && authTime > (returned.previousAuthTime ?? returned.startedAt)
}

/**
 * The client hint: whether the API will find the sign-in too old, by the token's auth_time against StepUp.MaxAge less a
 * minute (half the window when it is shorter than two). Only a hint (two clocks): the API decides. After a step-up whose
 * sign-in moved, the browser's time of the return counts too, so a browser clock ahead of the provider's does not ask
 * again at once. False while the API has no step-up (StepUp.Enabled).
 */
export async function isSignInStale(now = Date.now()): Promise<boolean> {
  const settings = getStepUpSettings()
  const auth = getAuthService()

  if (!settings.enabled || !auth) {
    return false
  }

  const authTime = await auth.getAuthTime()

  if (authTime === undefined) {
    return true
  }

  const returned = auth.getStepUpReturn()
  const signedInAt = returned && signedInSince(returned, authTime) ? Math.max(authTime, returned.returnedAt) : authTime
  const margin = Math.min(60, settings.maxAge / 2)
  return now - signedInAt > (settings.maxAge - margin) * 1000
}

/** True for the items of a bulk call (restore, purge) that the API refused for the step-up. */
export const hasStepUpItems = (items: readonly { ErrorCode?: string | null }[] | undefined): boolean =>
  (items ?? []).some((item) => item.ErrorCode === stepUpRequiredCode)

const sameTarget = (left: RevealTarget, right: RevealTarget) =>
  left.scope === right.scope && left.key === right.key && left.version === right.version

export const useStepUpStore = defineStore('step-up', () => {
  const open = ref(false)
  const mode = ref<StepUpMode>('confirm')
  /** Whether the page had unsaved changes when the confirmation opened: the redirect loses them. */
  const unsaved = ref(false)
  const redirecting = ref(false)
  const failure = shallowRef<unknown>()
  let pendingReveal: RevealTarget | undefined
  let opening: Promise<void> | undefined

  /**
   * Asks the person to confirm it is them, once however many requests failed (the SDK's onStepUpRequired, a bulk item
   * with StepUpRequired, the client hint). After a step-up that came back without a newer sign-in, it shows the error
   * of the loop guard instead of another redirect.
   */
  function ask(): Promise<void> {
    if (open.value || redirecting.value) {
      return Promise.resolve()
    }

    opening ??= (async () => {
      const auth = getAuthService()
      const returned = auth?.getStepUpReturn()
      const looped = returned !== undefined && !signedInSince(returned, await auth?.getAuthTime())

      mode.value = looped ? 'failed' : 'confirm'
      unsaved.value = hasUnsavedChanges()
      failure.value = undefined
      open.value = true
    })().finally(() => (opening = undefined))

    return opening
  }

  /**
   * Runs the confirmation first when the client hint finds the sign-in stale, before the person types what the
   * redirect would lose (a secret value, a new machine, an invitation, a role change). True when the action may go on.
   */
  async function ensureRecentSignIn(): Promise<boolean> {
    if (!(await isSignInStale())) {
      return true
    }

    await ask()
    return false
  }

  /** Signs in again with max_age=0 and the user's email as login hint, back to returnTo (the current route). */
  async function confirm(returnTo: string): Promise<void> {
    const auth = getAuthService()

    if (!auth) {
      return
    }

    redirecting.value = true
    failure.value = undefined

    if (pendingReveal) {
      sessionStorage.setItem(pendingRevealKey, JSON.stringify(pendingReveal))
    }

    // The person confirmed that the unsaved changes are lost.
    leaveWithoutPrompt()

    try {
      await auth.signIn({ returnTo, loginHint: useSessionStore().user?.email, maxAge: 0 })
    } catch (error) {
      leaveWithoutPrompt(false)
      sessionStorage.removeItem(pendingRevealKey)
      redirecting.value = false
      failure.value = error
    }
  }

  /** Closes the confirmation; the screen keeps its error. */
  function cancel(): void {
    open.value = false
    pendingReveal = undefined
  }

  /** The Reveal that failed for the step-up: repeated once after the return, if the person confirms. */
  function rememberReveal(target: RevealTarget): void {
    pendingReveal = target
  }

  /** True once for the Reveal that failed before the step-up the page came back from. */
  function takePendingReveal(target: RevealTarget): boolean {
    const stored = sessionStorage.getItem(pendingRevealKey)

    if (!stored) {
      return false
    }

    let pending: RevealTarget | undefined

    try {
      pending = JSON.parse(stored) as RevealTarget
    } catch {
      pending = undefined
    }

    // Only right after the return: a sign-in abandoned at the provider leaves no reveal for a later visit.
    if (!getAuthService()?.getStepUpReturn() || !pending) {
      sessionStorage.removeItem(pendingRevealKey)
      return false
    }

    if (!sameTarget(pending, target)) {
      return false
    }

    sessionStorage.removeItem(pendingRevealKey)
    return true
  }

  let returnAnnounced = false

  /** True once per page load after a step-up return, for the toast that says a change is not repeated. */
  function takeReturnNotice(): boolean {
    if (returnAnnounced || !getAuthService()?.getStepUpReturn()) {
      return false
    }

    returnAnnounced = true
    return true
  }

  return {
    open,
    mode,
    unsaved,
    redirecting,
    failure,
    ask,
    ensureRecentSignIn,
    confirm,
    cancel,
    rememberReveal,
    takePendingReveal,
    takeReturnNotice,
  }
})
