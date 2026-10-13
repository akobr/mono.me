import type { RouteLocationNormalized, Router } from 'vue-router'
import { roleRank } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { useSessionStore } from '@/stores/session'
import { hasOrganization } from '@/utils/memberships'
import { accessibleContext, mergeContext } from '@/utils/navigation'

const param = (value: unknown): string | undefined =>
  typeof value === 'string' && value.length > 0 ? value : undefined

/**
 * Reads :org, :project and :view, checks them against Account.AccessMap and sets the context store.
 * Project access is checked against the exact "{org}.{project}" key (an organization role does not grant it);
 * a route outside the caller's memberships renders the 403 state instead of the page.
 * An allowed context is merged into `preferences.lastContext`, which the navigation menu and "/" use.
 */
export function contextGuard(to: RouteLocationNormalized): true {
  const params = to.params as Record<string, unknown>
  const org = param(params.org)
  const project = param(params.project)
  const view = param(params.view)
  const context = useContextStore()

  if (!org) {
    context.set({})
    return true
  }

  const accessMap = useSessionStore().accessMap
  const allowed = project ? roleRank(accessMap[`${org}.${project}`]) > 0 : hasOrganization(accessMap, org)

  context.set({ org, project, view }, allowed ? 'ok' : 'forbidden')

  if (allowed) {
    const preferences = usePreferencesStore()
    preferences.rememberContext(
      mergeContext({ org, project, view }, accessibleContext(preferences.state.lastContext, accessMap)),
    )
  }

  return true
}

export function installContextGuard(router: Router): void {
  router.beforeEach(contextGuard)
}
