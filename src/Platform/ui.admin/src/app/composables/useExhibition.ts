import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { isNotFound, joinExhibition, roleRank } from '@42for.net/2splatform.sdk'
import { useGetExhibition } from '@42for.net/2splatform.sdk/vue-query'
import { useSessionStore } from '@/stores/session'
import { errorMessage } from '@/utils/errors'
import { exhibitionTexts } from '@/utils/exhibition'
import { projectPath } from '@/utils/memberships'
import { onboardingTexts } from '@/utils/onboarding'

/** As the wizard: the exhibition is optional, and a slow answer must not hold anything up. */
const exhibitionTimeoutMs = 5_000

/**
 * The deployment's exhibition as the API offers it (424-271), and whether the caller is a member of it. The API answers
 * the same to members and to others, so membership is the account's: a role on the exhibition project. An answer is
 * kept for five minutes and a 404 (none) until the cache drops it, both until sign-out clears the cache; another
 * failure is asked again by the next page that needs it.
 */
export function useExhibition() {
  const session = useSessionStore()
  const query = useGetExhibition({
    query: {
      meta: { silent: true },
      staleTime: 5 * 60_000,
      // A 404 (none) is an answer, so no mount asks again after it; each request of a deployment whose exhibition is
      // not offered logs a Warning. Without data vue-query asks a query in error again on mount (retryOnMount); with
      // the data of an earlier success, it asks once that data is stale (refetchOnMount), also after a 404 since.
      retryOnMount: (cached) => !isNotFound(cached.state.error),
      refetchOnMount: (cached) => !isNotFound(cached.state.error),
    },
    request: { timeoutMs: exhibitionTimeoutMs },
  })

  // Only a successful answer counts: a refetch that fails after a success keeps vue-query's previous data, which would
  // keep offering an exhibition that has gone. Any failure hides the offer, a 404 (none) like any other: the exhibition
  // is optional, and nothing shows an error for it.
  const exhibition = computed(() => (query.isSuccess.value ? query.data.value : undefined))
  const projectKey = computed(() =>
    exhibition.value ? `${exhibition.value.Organization}.${exhibition.value.Project}` : undefined,
  )
  const member = computed(() => !!projectKey.value && roleRank(session.accessMap[projectKey.value]) > 0)
  const offered = computed(() => !!exhibition.value && !member.value)

  return {
    exhibition,
    projectKey,
    member,
    offered,
    isExhibition: (key: string | undefined) => !!key && key === projectKey.value,
    refetch: () => query.refetch(),
  }
}

/**
 * "Join the exhibition" of a user who has an account (424-271): the join, the account of its answer and the project.
 * The answer is the caller's account with the membership, so it is the reload of the account without a second request.
 */
export function useJoinExhibition(offer: ReturnType<typeof useExhibition>) {
  const router = useRouter()
  const session = useSessionStore()
  const toast = useToast()
  const joining = ref(false)

  async function join(): Promise<void> {
    const target = offer.exhibition.value
    if (!target || !offer.offered.value || joining.value) {
      return
    }

    joining.value = true
    try {
      session.setAccount(await joinExhibition())
    } catch (error) {
      if (isNotFound(error)) {
        // The deployment stopped offering it since the page asked: the answer of the refetch hides the offer.
        toast.add({ title: exhibitionTexts.notOffered, color: 'warning', icon: 'i-lucide-circle-alert' })
        await offer.refetch()
      } else {
        toast.add({ title: exhibitionTexts.joinFailed, description: errorMessage(error), color: 'error' })
      }
      return
    } finally {
      joining.value = false
    }

    toast.add({ title: onboardingTexts.end.joined, color: 'success', icon: 'i-lucide-party-popper' })
    await router.push(projectPath(target.Organization, target.Project))
  }

  return { joining, join }
}
