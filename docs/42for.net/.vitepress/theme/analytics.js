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

// Event properties with a URL, and with a path, of the page or of the one before (the admin UI's list).
const urlProperties = /^\$(?:current_url|referrer|session_entry_url|session_entry_referrer|initial_current_url|initial_referrer)$/
const pathProperties = /^\$(?:pathname|session_entry_pathname|initial_pathname|prev_pageview_pathname)$/

// Campaign parameters, ad click ids and the search keyword, which PostHog takes from the query of the entry URL and of
// the referrer and sends as $session_entry_<name> and $initial_<name>, even with save_campaign_params off.
const queryValueProperty =
  /^(?:\$session_entry_|\$initial_)?(?:utm_\w+|gclid|gclsrc|dclid|gbraid|wbraid|fbclid|msclkid|twclid|li_fat_id|igshid|ttclid|rdt_cid|epik|qclid|sccid|oppref|bfcid|irclid|_kx|gad_source|mc_cid|ph_keyword)$/

let posthog
let lastPage

/** Do Not Track or Global Privacy Control, read like posthog-js's respect_dnt. */
function privacySignal() {
  return [navigator.doNotTrack, navigator.msDoNotTrack, window.doNotTrack, navigator.globalPrivacyControl].some(
    (value) => yesLike.includes(typeof value === 'string' ? value.toLowerCase() : value),
  )
}

/** The page of a path, as VitePress resolves it (normalizeHref, cleanUrls off): /x, /x.html and /x/index.html are one. */
function pageOf(pathname) {
  const path = pathname.replace(/(^|\/)index(\.html)?$/, '$1')
  return path.endsWith('/') || path.endsWith('.html') ? path : `${path}.html`
}

/** A URL of the site as its origin and page, without query and hash; another site's URL as its origin only. */
function scrubUrl(value) {
  if (typeof value !== 'string' || !/^https?:\/\//i.test(value)) {
    return value // '$direct', or no URL at all
  }

  try {
    const url = new URL(value)
    return url.origin === window.location.origin ? `${url.origin}${pageOf(url.pathname)}` : url.origin
  } catch {
    return undefined
  }
}

function scrubProperties(properties) {
  if (!properties) {
    return
  }

  for (const key of Object.keys(properties)) {
    const value = properties[key]
    if (queryValueProperty.test(key)) {
      delete properties[key]
    } else if (urlProperties.test(key)) {
      properties[key] = scrubUrl(value)
    } else if (pathProperties.test(key) && typeof value === 'string' && value.startsWith('/')) {
      properties[key] = pageOf(value)
    }
  }
}

/**
 * PostHog's before_send: a docs page needs no query, and a query can carry an ad click id or a newsletter's subscriber
 * id. Every URL becomes its page (or another site's origin), every path its page, and the campaign parameters go.
 */
function scrubEvent(event) {
  scrubProperties(event.properties)
  scrubProperties(event.$set)
  scrubProperties(event.$set_once)
  return event
}

/** One $pageview per page: a click on a heading, back and forward between headings, or /x after /x.html send none. */
function capturePageview() {
  const page = pageOf(window.location.pathname)
  if (!posthog || page === lastPage) {
    return
  }

  lastPage = page
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
    // Campaign parameters would read document.cookie on every event; scrubEvent drops those PostHog still sends.
    save_campaign_params: false,
    before_send: scrubEvent,
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
 * is on. Only then is the PostHog chunk requested, in parallel and never awaited, so it never delays the first render;
 * a failure leaves the analytics off.
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
