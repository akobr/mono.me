import {
  START_LOCATION,
  type NavigationFailure,
  type RouteLocationNormalized,
  type RouteLocationRaw,
  type Router,
} from 'vue-router'
import { isRememberedSection, usePageMemoryStore } from '@/stores/page-memory'
import { sameView, viewLocation, type ViewLocation } from '@/utils/context-paths'

// The navigations this guard sent to a remembered location, by the location first asked for: `to.redirectedFrom`
// behind Modeling's redirect, `to` itself otherwise. Their next pass comes back with it as its redirectedFrom, and goes
// through however the remembered location is spelled: one recorded from the address bar keeps the browser's spelling
// (%27), which the router writes again ('), so a comparison of the strings could loop (424-67).
const restored = new WeakSet<object>()

/**
 * Brings the user back to where they left the annotations, configurations or graph section of a view (filter,
 * selected detail and tab) when they return to the section's plain list from elsewhere: another page, another
 * section or another view. A plain list opened from within the same section (closing the detail, selecting the open
 * section in the sidebar) and the first navigation of the tab (a typed, bookmarked or reloaded URL) stay as they are.
 * Modeling comes back to its last tab, detail and query the same way; its plain URL redirects to its first tab before
 * any guard runs, so it is read from `to.redirectedFrom` (424-67).
 */
export function pageMemoryGuard(to: RouteLocationNormalized, from: RouteLocationNormalized): true | RouteLocationRaw {
  if (to.redirectedFrom && restored.has(to.redirectedFrom)) {
    return true
  }

  const target = viewLocation(to.path)
  if (!target || !isRememberedSection(target.section)) {
    return true
  }
  const requested = target.root ? to : redirectedList(to, target)
  if (!requested || Object.keys(requested.query).length > 0 || requested.hash) {
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
  if (!remembered || remembered === to.fullPath) {
    return true
  }
  restored.add(to.redirectedFrom ?? to)
  return remembered
}

/**
 * The plain list of the section that `to` was redirected from: `modeling` opens its first tab, `modeling/schemas`
 * (424-67). A redirect from anywhere else, such as the old `schemas` or `deleted` page of a view, is no plain list.
 */
function redirectedList(to: RouteLocationNormalized, target: ViewLocation) {
  const requested = to.redirectedFrom && viewLocation(to.redirectedFrom.path)
  return requested?.root && requested.section === target.section && sameView(requested.scope, target.scope)
    ? to.redirectedFrom
    : undefined
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
