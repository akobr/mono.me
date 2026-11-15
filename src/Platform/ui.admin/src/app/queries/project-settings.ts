import { onScopeDispose } from 'vue'
import { useQueryClient, type QueryClient } from '@tanstack/vue-query'
import type { ProjectSettings, ProjectSummary } from '@42for.net/2splatform.sdk'
import { getGetProjectsQueryKey, useGetProjectSettings } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'
import { useProjectSettingsStore } from '@/stores/project-settings'
import { useSessionStore } from '@/stores/session'
import { removeLegacyProjectSettings } from '@/utils/legacy-project-settings'

/** The project key of a settings query (`['v1', org, project, 'settings']`), or undefined for any other query. */
function projectKeyOf(queryKey: readonly unknown[]): string | undefined {
  const [version, org, project, resource] = queryKey
  return queryKey.length === 4 &&
    version === 'v1' &&
    resource === 'settings' &&
    typeof org === 'string' &&
    typeof project === 'string' &&
    org !== 'access'
    ? `${org}.${project}`
    : undefined
}

/** The settings of a project in the cached list of the caller's projects (GET v1/access/projects), when it is loaded. */
function listedSettings(client: QueryClient, projectKey: string): ProjectSettings | undefined {
  return client.getQueryData<ProjectSummary[]>(getGetProjectsQueryKey())?.find((summary) => summary.Key === projectKey)
    ?.Settings
}

/**
 * The query of the settings of the current project (GET v1/{organization}/{project}/settings, 424-266), for App.vue
 * (useCurrentProjectSettings) and the Settings page, which share it. The first answer comes from the cached list of the
 * caller's projects when it is loaded, which carries every project's settings, so a project opened from the overview
 * shows its types at once; the query reads them again once that list is a minute old.
 */
export function useProjectSettingsQuery() {
  const session = useSessionStore()
  const context = useContextStore()
  const queryClient = useQueryClient()

  return useGetProjectSettings(
    () => context.org ?? '',
    () => context.project ?? '',
    {
      query: {
        enabled: () => !!session.account && !!context.projectKey && context.access === 'ok',
        staleTime: 60_000,
        meta: { silent: true },
        initialData: () => (context.projectKey ? listedSettings(queryClient, context.projectKey) : undefined),
        initialDataUpdatedAt: () => queryClient.getQueryState(getGetProjectsQueryKey())?.dataUpdatedAt,
      },
    },
  )
}

/**
 * The settings of the current project, mounted once in App.vue: the query of useProjectSettingsQuery, and every answer
 * of a settings query, also one a save writes into the cache, goes to the project settings store, which the type
 * appearance, the date format and the New annotation dialog read; a project with settings on the server (Version > 0)
 * loses the settings this browser kept for it before.
 */
export function useCurrentProjectSettings() {
  const store = useProjectSettingsStore()
  const cache = useQueryClient().getQueryCache()

  const receive = (queryKey: readonly unknown[], data: unknown) => {
    const projectKey = projectKeyOf(queryKey)
    if (projectKey && data) {
      store.receive(projectKey, data as ProjectSettings)
      // The settings this browser kept before the API stored them go once the project has its own (424-266).
      if (((data as ProjectSettings).Version ?? 0) > 0) {
        removeLegacyProjectSettings(projectKey)
      }
    }
  }

  for (const query of cache.getAll()) {
    receive(query.queryKey, query.state.data)
  }
  const unsubscribe = cache.subscribe((event) => {
    if (event.type === 'added' || (event.type === 'updated' && event.action.type === 'success')) {
      receive(event.query.queryKey, event.query.state.data)
    }
  })
  onScopeDispose(unsubscribe)

  return useProjectSettingsQuery()
}
