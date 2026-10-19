import { START_LOCATION, type NavigationFailure, type RouteLocationNormalized, type Router } from 'vue-router'
import { capturePageview } from '@/boot/telemetry'

/**
 * A pageview for the first navigation and for every later one that reaches another path. A query-only change (typing
 * a filter, switching a tab) is no new page, and a failed or cancelled navigation shows none.
 */
export function trackPageview(
  to: RouteLocationNormalized,
  from: RouteLocationNormalized,
  failure?: NavigationFailure | void,
): void {
  if (!failure && (from === START_LOCATION || to.path !== from.path)) {
    capturePageview()
  }
}

export function installTelemetryGuard(router: Router): void {
  router.afterEach(trackPageview)
}
