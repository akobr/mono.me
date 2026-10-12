import {
  START_LOCATION,
  type NavigationFailure,
  type RouteLocationNormalized,
  type RouteLocationRaw,
  type Router,
} from 'vue-router'
import type { Scope } from '@/stores/context'
import { isRememberedSection, usePageMemoryStore } from '@/stores/page-memory'
import { viewLocation } from '@/utils/context-paths'

const sameView = (a: Scope, b: Scope) => a.org === b.org && a.project === b.project && a.view === b.view

/**
 * Brings the user back to where they left the annotations, configurations or graph section of a view (filter,
 * selected detail and tab) when they return to the section's plain list from elsewhere: another page, another
 * section or another view. A plain list opened from within the same section (closing the detail, selecting the open
 * section in the sidebar) and the first navigation of the tab (a typed, bookmarked or reloaded URL) stay as they are.
 */
export function pageMemoryGuard(to: RouteLocationNormalized, from: RouteLocationNormalized): true | RouteLocationRaw {
  const target = viewLocation(to.path)
  if (!target?.root || !isRememberedSection(target.section) || Object.keys(to.query).length > 0 || to.hash) {
    return true
  }
  if (from === START_LOCATION) {
    return true
  }

  const origin = viewLocation(from.path)
  if (origin && origin.section === target.section && sameView(origin.scope, target.scope)) {
    return true
  }

  const remembered = usePageMemoryStore().location(target.scope, target.section)
  return remembered && remembered !== to.fullPath ? remembered : true
}

/** Records the location of every completed navigation inside a remembered section. */
export function rememberPageLocation(to: RouteLocationNormalized, failure?: NavigationFailure | void): void {
  const target = failure ? undefined : viewLocation(to.path)
  if (target && isRememberedSection(target.section)) {
    usePageMemoryStore().rememberLocation(target.scope, target.section, to.fullPath)
  }
}

export function installPageMemoryGuard(router: Router): void {
  router.beforeEach(pageMemoryGuard)
  router.afterEach((to, _from, failure) => rememberPageLocation(to, failure))
}
