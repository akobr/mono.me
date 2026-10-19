import type { Breadcrumb, ErrorEvent } from '@sentry/vue'
import type { BeforeSendFn, Properties } from 'posthog-js'
import type { Router } from 'vue-router'

/** The PostHog session and person of a Sentry event, read from the live PostHog instance. */
export type TelemetryCorrelation = Partial<Record<'posthog_session_id' | 'posthog_distinct_id', string>>

/** The template of the not-found page (pages/[...path].vue), for a path that matches no route. */
const notFoundTemplate = '/:path(.*)'

/** PostHog event properties that carry a URL or a path; "pathname" ones get the template without the origin. */
const urlProperties = [
  '$current_url',
  '$referrer',
  '$pathname',
  '$session_entry_url',
  '$session_entry_referrer',
  '$session_entry_pathname',
  '$initial_current_url',
  '$initial_referrer',
  '$initial_pathname',
  '$prev_pageview_pathname',
  '$external_click_url',
]

/** Person properties ($set, $set_once) that carry a URL or a path. */
const urlPersonProperty = /url|path|referr/

/**
 * Campaign parameters and the search keyword, which PostHog reads from the query of the session-entry URL and of the
 * referrer and sends as $session_entry_<name>, $initial_<name> and in $set_once, even with save_campaign_params off.
 */
const queryValueProperty =
  /^(?:\$session_entry_|\$initial_)?(?:utm_\w+|gclid|gclsrc|dclid|gbraid|wbraid|fbclid|msclkid|twclid|li_fat_id|igshid|ttclid|rdt_cid|epik|qclid|sccid|oppref|bfcid|irclid|_kx|gad_source|mc_cid|ph_keyword)$/

// A path or an http(s) URL; other values of these keys ('$direct', a referring domain) carry no path.
const isUrlOrPath = (value: string) => /^(?:https?:)?\//i.test(value)

function routeTemplate(router: Router, url: string, withOrigin: boolean): string {
  let parsed: URL
  try {
    parsed = new URL(url, location.origin)
  } catch {
    return withOrigin ? `${location.origin}${notFoundTemplate}` : notFoundTemplate
  }

  if (parsed.origin !== location.origin) {
    return parsed.origin === 'null' ? parsed.protocol : parsed.origin
  }

  const template = router.resolve(parsed.pathname).matched.at(-1)?.path ?? notFoundTemplate
  return withOrigin ? `${parsed.origin}${template}` : template
}

/**
 * The route template of a URL of the app (or of a path), for example
 * https://admin.example.com/:org/:project/:view/key-values/:key: no page URL names an organization, project, view,
 * key or machine, and the query (AuthKit code and state, invitation tokens, filter text) and the hash are always
 * dropped. Another origin gives the origin only.
 */
export function pageTemplate(router: Router, url: string): string {
  return routeTemplate(router, url, true)
}

/** {@link pageTemplate} without the origin, for path-style values ($pathname, navigation breadcrumbs, tags). */
export function pathTemplate(router: Router, url: string): string {
  return routeTemplate(router, url, false)
}

function scrubValue(router: Router, key: string, value: unknown): unknown {
  if (typeof value !== 'string' || !isUrlOrPath(value)) {
    return value
  }

  return key.includes('pathname') ? pathTemplate(router, value) : pageTemplate(router, value)
}

function scrubPersonProperties(router: Router, properties: Properties | undefined): void {
  if (!properties) {
    return
  }

  for (const key of Object.keys(properties)) {
    if (urlPersonProperty.test(key)) {
      properties[key] = scrubValue(router, key, properties[key])
    }
  }
}

function dropQueryValues(properties: Properties | undefined): void {
  if (!properties) {
    return
  }

  for (const key of Object.keys(properties)) {
    if (queryValueProperty.test(key)) {
      delete properties[key]
    }
  }
}

/**
 * PostHog's before_send: every URL and path of the event, of $set and of $set_once becomes its template, and the
 * campaign parameters and search keyword PostHog takes from the entry query are dropped. PostHog records the raw path,
 * query and hash in all of them, even with save_referrer and save_campaign_params off. `token` stays: without it
 * PostHog drops the event.
 */
export function scrubPostHogEvent(router: Router): BeforeSendFn {
  return (event) => {
    for (const key of urlProperties) {
      if (key in event.properties) {
        event.properties[key] = scrubValue(router, key, event.properties[key])
      }
    }
    scrubPersonProperties(router, event.$set)
    scrubPersonProperties(router, event.$set_once)
    dropQueryValues(event.properties)
    dropQueryValues(event.$set)
    dropQueryValues(event.$set_once)
    return event
  }
}

/**
 * Sentry's beforeSend: the page as its template, no Referer, no arguments of wrapped callbacks, the user as an id
 * only, and the current PostHog session and person as tags (the session id rotates, so it is read for every event).
 */
export function scrubSentryEvent(event: ErrorEvent, router: Router, correlation: TelemetryCorrelation): ErrorEvent {
  const request = event.request
  if (request) {
    if (request.url) {
      request.url = pageTemplate(router, request.url)
    }
    delete request.query_string

    const headers = request.headers ?? {}
    for (const name of Object.keys(headers)) {
      if (name.toLowerCase() === 'referer') {
        delete headers[name]
      }
    }
  }

  delete event.extra?.arguments

  if (event.user) {
    if (event.user.id === undefined) {
      delete event.user
    } else {
      event.user = { id: event.user.id }
    }
  }

  event.tags = { ...event.tags, ...correlation }
  return event
}

/** Whether a request goes to the PostHog host: its origin, and its path for a host behind a reverse proxy. */
function isPostHogRequest(url: string, posthogHost: string): boolean {
  try {
    const request = new URL(url, location.origin)
    const host = new URL(posthogHost)
    const path = host.pathname.replace(/\/+$/, '')
    return request.origin === host.origin && (request.pathname === path || request.pathname.startsWith(`${path}/`))
  } catch {
    return false
  }
}

/**
 * Sentry's beforeBreadcrumb: no clicks or key presses (Sentry writes the aria-label, title, alt and name of the element
 * and its ancestors into them, unescaped: user and member names, e-mails, filter text), no requests to PostHog (one
 * per batch of events, which would crowd out the API calls), navigations as templates, and API calls without query and
 * hash. API paths keep their organization, project, view and key names: they locate the failing call and are names,
 * not values.
 */
export function scrubBreadcrumb(breadcrumb: Breadcrumb, router: Router, posthogHost?: string): Breadcrumb | null {
  if (breadcrumb.category?.startsWith('ui.')) {
    return null
  }

  const data = breadcrumb.data
  if (!data) {
    return breadcrumb
  }

  if (breadcrumb.category === 'navigation') {
    for (const key of ['from', 'to']) {
      if (typeof data[key] === 'string') {
        data[key] = pathTemplate(router, data[key])
      }
    }
  } else if ((breadcrumb.category === 'fetch' || breadcrumb.category === 'xhr') && typeof data.url === 'string') {
    if (posthogHost && isPostHogRequest(data.url, posthogHost)) {
      return null
    }
    data.url = data.url.replace(/[?#].*$/s, '')
  }

  return breadcrumb
}
