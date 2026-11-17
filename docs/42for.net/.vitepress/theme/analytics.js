import { nextTick } from 'vue'

// Usage analytics of 42for.net: page views in PostHog's EU cloud, without cookies and without anything in the browser's
// storage. The spec: docs/specs/2026-11-17 Cookieless Analytics on 42for.net.md.

// The project of sform and the admin UI's local runs. A project token is public by design: it can only send events.
const projectToken = 'phc_uGDiouxjcaRUArCSCi3knvAbub5eoCorCg8c9Wam7yke'
const apiHost = 'https://eu.i.posthog.com'

// Only the published site sends: local runs (vitepress dev, vitepress preview), pull-request previews and forks don't.
const productionHosts = ['42for.net', 'www.42for.net']

// The values posthog-js treats as "on" (isYesLike), strings in any case.
const yesLike = [true, 1, '1', 'true', 'yes']

let posthog
let lastPath

/** Do Not Track or Global Privacy Control, read like posthog-js's respect_dnt. */
function privacySignal() {
  return [navigator.doNotTrack, navigator.msDoNotTrack, window.doNotTrack, navigator.globalPrivacyControl].some(
    (value) => yesLike.includes(typeof value === 'string' ? value.toLowerCase() : value),
  )
}

/** One $pageview per page: a click on a heading, or back and forward between headings, keeps the pathname. */
function capturePageview() {
  const path = window.location.pathname
  if (!posthog || path === lastPath) {
    return
  }

  lastPath = path
  try {
    posthog.capture('$pageview')
  } catch (error) {
    console.warn('Analytics failed.', error)
  }
}

function startPostHog(instance) {
  instance.init(projectToken, {
    api_host: apiHost,
    defaults: '2026-05-30',
    // The id lives in memory: no cookie, nothing in localStorage or sessionStorage. A reload or a new tab starts anew.
    persistence: 'memory',
    person_profiles: 'never',
    // Page views come from the router (startAnalytics), with the title of the new page.
    capture_pageview: false,
    capture_pageleave: false,
    disable_capture_url_hashes: true,
    autocapture: false,
    // Campaign parameters would read document.cookie on every event.
    save_campaign_params: false,
    // fetch without credentials: a beacon would carry the visitor's cookies of posthog.com.
    disable_beacon: true,
    disableDeviceModel: true,
    // ?__posthog_debug=true would otherwise write ph_debug to localStorage.
    debug: false,
    disable_session_recording: true,
    capture_exceptions: false,
    capture_performance: false,
    capture_dead_clicks: false,
    capture_heatmaps: false,
    disable_surveys: true,
    disable_product_tours: true,
    disable_conversations: true,
    disable_external_dependency_loading: true,
    // No feature flags and no remote config: the only request is POST /e/.
    advanced_disable_flags: true,
    // Do Not Track and Global Privacy Control, again: a signal turned on after the page loaded.
    respect_dnt: true,
  })

  // Every event of the project carries its environment, like those of the admin UI and sform.
  instance.register({ environment: 'production' })
  posthog = instance
}

/**
 * Starts the analytics in the browser of a visitor of the published site, unless Do Not Track or Global Privacy Control
 * is on. The PostHog chunk is loaded only then, after the first render, and a failure leaves the analytics off.
 */
export function startAnalytics(router) {
  // A constant in the SSR build: the server bundle drops the rest, and the build never runs posthog-js.
  if (import.meta.env.SSR) {
    return
  }

  if (!productionHosts.includes(window.location.hostname) || privacySignal()) {
    return
  }

  // Called on the first page and after every navigation (popstate too), but not for a click on a heading.
  const previous = router.onAfterRouteChanged
  router.onAfterRouteChanged = async (to) => {
    await previous?.(to)
    // VitePress sets the new title and corrects the URL in the next tick.
    await nextTick()
    capturePageview()
  }

  import('posthog-js/no-external')
    .then(({ default: instance }) => {
      startPostHog(instance)
      // The first route change has usually passed while the chunk loaded.
      capturePageview()
    })
    .catch((error) => console.warn('Analytics stay off.', error))
}
