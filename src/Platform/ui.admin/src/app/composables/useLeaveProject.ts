import { ref } from 'vue'
import { isNavigationFailure, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { getAccount, hasErrorCode, removeMember, type Account } from '@42for.net/2splatform.sdk'
import { leaveWithoutPrompt } from '@/composables/useUnsavedChanges'
import { forgetProject } from '@/queries/invalidation'
import { useSessionStore } from '@/stores/session'
import { errorMessage } from '@/utils/errors'
import { withoutMembership } from '@/utils/memberships'

export const leaveTexts = {
  failed: 'You did not leave the project',
  lastOwner: 'The last Owner cannot leave. Make another member an Owner first.',
  left: (key: string) => `You left ${key}`,
} as const

/**
 * "Leave project" (424-271): the caller removes their own membership of a project, which the API allows any member but
 * the last Owner, without a recent sign-in. The page of the project stays reachable only by a new invitation (or a
 * new join of the exhibition), so the leave ends on Organizations.
 */
export function useLeaveProject() {
  const router = useRouter()
  const queryClient = useQueryClient()
  const session = useSessionStore()
  const toast = useToast()
  const leaving = ref(false)

  /** True when the caller is no member of the project any more. */
  async function leave(projectKey: string): Promise<boolean> {
    const account = session.account
    if (!account?.Id || leaving.value) {
      return false
    }

    // In flight until the page is gone: the confirmation keeps its loading state, and a second click does nothing.
    leaving.value = true
    try {
      try {
        await removeMember(projectKey, account.Id)
      } catch (error) {
        // Not a member (any more): the membership is gone, as after a leave, and the page goes the same way.
        if (!hasErrorCode(error, 'MemberNotFound')) {
          toast.add({
            title: leaveTexts.failed,
            description: hasErrorCode(error, 'LastOwner') ? leaveTexts.lastOwner : errorMessage(error),
            color: 'error',
          })
          return false
        }
      }

      // The account as the API has it now; when that read fails, the old one without the project, which is what the
      // removal left. The page must not keep the membership either way.
      const fresh: Account = await getAccount().catch(() => withoutMembership(account, projectKey))
      session.setAccount(fresh)
      toast.add({ title: leaveTexts.left(projectKey), color: 'success', icon: 'i-lucide-log-out' })
      await toOrganizations()
      // Only once the project's page is gone: none of its queries reads the project again as a non-member.
      const dot = projectKey.indexOf('.')
      void forgetProject(queryClient, projectKey.slice(0, dot), projectKey.slice(dot + 1))
      return true
    } finally {
      leaving.value = false
    }
  }

  /** Ends the page of the project: a guard that stops the navigation, or a page that did not load, ends in a full load. */
  async function toOrganizations(): Promise<void> {
    let failed: boolean
    try {
      failed = isNavigationFailure(await router.replace('/orgs'))
    } catch {
      failed = true
    }
    if (failed) {
      leaveWithoutPrompt()
      window.location.replace(router.resolve('/orgs').href)
    }
  }

  return { leaving, leave }
}
