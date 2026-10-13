import { computed } from 'vue'
import { getRole } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'
import { navigationScope, type NavigationContext } from '@/utils/navigation'

/**
 * The context the navigation menu shows (the sidebar and the pages of the command palette), with the caller's roles
 * in it: the route's context completed by the remembered one, see `navigationScope`.
 */
export function useNavigationContext() {
  const context = useContextStore()
  const preferences = usePreferencesStore()
  const session = useSessionStore()

  return computed<NavigationContext>(() => {
    const { org, project, view } = navigationScope(
      { org: context.org, project: context.project, view: context.view },
      preferences.state.lastContext,
      session.accessMap,
    )

    return {
      org,
      project,
      view,
      projectRole: org && project ? getRole(session.accessMap, `${org}.${project}`) : 'None',
      orgRole: org ? getRole(session.accessMap, org) : 'None',
    }
  })
}
