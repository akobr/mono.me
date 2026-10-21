import { computed, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { roleRank, type ProjectSummary } from '@42for.net/2splatform.sdk'
import { invalidateByPath, useGetProjects } from '@42for.net/2splatform.sdk/vue-query'
import { useSessionStore } from '@/stores/session'

/** The project keys of an AccessMap ("{org}.{project}" with a role), sorted, as one string to compare. */
const projectKeysOf = (accessMap: Record<string, string>) =>
  Object.entries(accessMap)
    .filter(([key, role]) => key.includes('.') && roleRank(role) > 0)
    .map(([key]) => key)
    .toSorted()
    .join(' ')

const listedKeysOf = (projects: ProjectSummary[]) =>
  projects
    .map((project) => project.Key ?? '')
    .toSorted()
    .join(' ')

/**
 * The caller's projects with their descriptions, for every role (GET v1/access/projects, 424-78). The list is cached
 * like any read, so it is fetched again when the projects of the session's AccessMap differ from the listed ones:
 * onboarding, a new project or organization, an accepted invitation, a reloaded account, a project left. The check
 * also runs when a page mounts, because the account may have changed while no page showed the list. It waits for a
 * fetch under way (a stale list fetched on mount, a refetch after a save) and compares its answer, so it never
 * cancels that fetch for another one.
 */
export function useProjectSummaries() {
  const session = useSessionStore()
  const queryClient = useQueryClient()
  const projects = useGetProjects({ query: { enabled: () => !!session.account, meta: { silent: true } } })

  const byKey = computed(() => new Map(projects.data.value?.map((project) => [project.Key, project])))

  // A key without its access point is never listed, so each set of keys asks once.
  let requested: string | undefined
  watch(
    [() => projectKeysOf(session.accessMap), () => projects.data.value, () => projects.isFetching.value] as const,
    ([keys, listed, fetching]) => {
      if (listed && !fetching && keys !== requested && keys !== listedKeysOf(listed)) {
        requested = keys
        void invalidateByPath(queryClient, '/v1/access/projects')
      }
    },
    { immediate: true },
  )

  return {
    ...projects,
    /** The summary of a project key; undefined while the list loads or without the project. */
    summaryOf: (key: string | undefined): ProjectSummary | undefined => (key ? byKey.value.get(key) : undefined),
  }
}
