import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { routes } from 'vue-router/auto-routes'
import type { Breadcrumb, ErrorEvent } from '@sentry/vue'
import type { CaptureResult } from 'posthog-js'
import {
  pageTemplate,
  pathTemplate,
  scrubBreadcrumb,
  scrubPostHogEvent,
  scrubSentryEvent,
} from '@/utils/telemetry-privacy'

const router = createRouter({ history: createMemoryHistory(), routes })
const origin = location.origin

// Identifiers, user text and secrets of the sample URLs below; none of them may leave the browser.
const raw = /acme|billing|prod|db\.password|m-123|code=|SECRET|state=|tok_|ada|q=|#|\?/

describe('page templates', () => {
  it('drops the AuthKit code and state and the invitation token', () => {
    expect(pageTemplate(router, `${origin}/callback?code=SECRET&state=%7B%22returnTo%22%3A%22%2Facme%22%7D`)).toBe(
      `${origin}/callback`,
    )
    expect(pageTemplate(router, `${origin}/invitations/accept?invitation_token=tok_123&id=inv_1`)).toBe(
      `${origin}/invitations/accept`,
    )
  })

  it('replaces the identifiers of the path with the route parameters', () => {
    expect(pageTemplate(router, `${origin}/acme/billing/prod/key-values/db.password?q=secret`)).toBe(
      `${origin}/:org/:project/:view/key-values/:key`,
    )
    expect(pageTemplate(router, '/acme/billing/machines/m-123')).toBe(`${origin}/:org/:project/machines/:id`)
    expect(pathTemplate(router, '/acme/billing/machines/m-123')).toBe('/:org/:project/machines/:id')
  })

  it('drops the hash', () => {
    expect(pageTemplate(router, `${origin}/acme/billing/settings#secrets`)).toBe(`${origin}/:org/:project/settings`)
  })

  it('keeps only the origin of another site', () => {
    expect(pageTemplate(router, 'https://auth.example.com/authorize?login_hint=ada%40example.com')).toBe(
      'https://auth.example.com',
    )
    expect(pathTemplate(router, 'https://auth.example.com/callback?code=SECRET')).toBe('https://auth.example.com')
  })

  it('gives the not-found template for a path that matches no route', () => {
    expect(pageTemplate(router, `${origin}/a/b/c/d/e/f/g`)).toBe(`${origin}/:path(.*)`)
    expect(pathTemplate(router, '/a/b/c/d/e/f/g')).toBe('/:path(.*)')
  })
})

describe('PostHog events', () => {
  const scrub = scrubPostHogEvent(router)

  it('templates every URL and path of a pageview and keeps the token', () => {
    const event: CaptureResult = {
      uuid: 'event_1',
      event: '$pageview',
      properties: {
        token: 'phc_test',
        $current_url: `${origin}/acme/billing/prod/key-values/db.password?q=secret#value`,
        $pathname: '/acme/billing/prod/key-values/db.password',
        $referrer: '$direct',
        $referring_domain: '$direct',
        $session_entry_url: `${origin}/callback?code=SECRET&state=xyz`,
        $session_entry_pathname: '/callback',
        $session_entry_referrer: 'https://auth.example.com/authorize?login_hint=ada%40example.com',
        $prev_pageview_pathname: '/acme/billing/machines/m-123',
        $external_click_url: 'https://docs.example.com/acme?q=secret',
        title: '2S Admin',
      },
    }

    const result = scrub(event)

    expect(result?.properties).toEqual({
      token: 'phc_test',
      $current_url: `${origin}/:org/:project/:view/key-values/:key`,
      $pathname: '/:org/:project/:view/key-values/:key',
      $referrer: '$direct',
      $referring_domain: '$direct',
      $session_entry_url: `${origin}/callback`,
      $session_entry_pathname: '/callback',
      $session_entry_referrer: 'https://auth.example.com',
      $prev_pageview_pathname: '/:org/:project/machines/:id',
      $external_click_url: 'https://docs.example.com',
      title: '2S Admin',
    })
    expect(JSON.stringify(result)).not.toMatch(raw)
  })

  it('templates the URLs and paths of $set and $set_once', () => {
    const event: CaptureResult = {
      uuid: 'event_2',
      event: '$identify',
      properties: {
        token: 'phc_test',
        distinct_id: 'user_1',
        $current_url: `${origin}/invitations/accept?invitation_token=tok_123`,
        $initial_current_url: `${origin}/callback?code=SECRET&state=xyz`,
        $initial_pathname: '/callback',
        $initial_referrer: 'https://auth.example.com/callback?code=SECRET',
      },
      $set: { $current_url: `${origin}/acme/billing/prod/annotations?q=secret`, $browser: 'Chrome' },
      $set_once: {
        $initial_current_url: `${origin}/acme/billing/prod/key-values/db.password?q=secret`,
        $initial_pathname: '/acme/billing/prod/key-values/db.password',
        $initial_referrer: 'https://auth.example.com/callback?code=SECRET',
        $initial_referring_domain: 'auth.example.com',
        $current_url: `${origin}/acme/billing/machines/m-123#credentials`,
        $pathname: '/acme/billing/machines/m-123',
        $referrer: '$direct',
      },
    }

    const result = scrub(event)

    expect(result?.properties.token).toBe('phc_test')
    expect(result?.$set).toEqual({ $current_url: `${origin}/:org/:project/:view/annotations`, $browser: 'Chrome' })
    expect(result?.$set_once).toEqual({
      $initial_current_url: `${origin}/:org/:project/:view/key-values/:key`,
      $initial_pathname: '/:org/:project/:view/key-values/:key',
      $initial_referrer: 'https://auth.example.com',
      $initial_referring_domain: 'auth.example.com',
      $current_url: `${origin}/:org/:project/machines/:id`,
      $pathname: '/:org/:project/machines/:id',
      $referrer: '$direct',
    })
    expect(JSON.stringify(result)).not.toMatch(raw)
  })

  it('drops the campaign parameters and the search keyword PostHog takes from the entry query', () => {
    const event: CaptureResult = {
      uuid: 'event_3',
      event: '$identify',
      properties: {
        token: 'phc_test',
        $session_entry_url: `${origin}/invitations/accept?invitation_token=tok_123&utm_source=ada&gclid=G42`,
        $session_entry_utm_source: 'ada',
        $session_entry_gclid: 'G42',
        $session_entry_ph_keyword: 'ada',
        $session_entry_search_engine: 'google',
      },
      $set_once: {
        $current_url: `${origin}/invitations/accept?utm_source=ada`,
        $initial_utm_campaign: 'ada',
        $initial_msclkid: 'G42',
        utm_source: 'ada',
        gclid: 'G42',
        fbclid: null,
      },
    }

    const result = scrub(event)

    expect(result?.properties).toEqual({
      token: 'phc_test',
      $session_entry_url: `${origin}/invitations/accept`,
      $session_entry_search_engine: 'google',
    })
    expect(result?.$set_once).toEqual({ $current_url: `${origin}/invitations/accept` })
    expect(JSON.stringify(result)).not.toMatch(/ada|G42|utm_|gclid|fbclid|msclkid|keyword/)
  })
})

describe('Sentry events', () => {
  const event = (): ErrorEvent => ({
    type: undefined,
    request: {
      url: `${origin}/acme/billing/prod/key-values/db.password?q=secret#value`,
      query_string: 'q=secret',
      headers: { 'User-Agent': 'Mozilla/5.0', Referer: `${origin}/acme/billing/machines/m-123` },
    },
    user: { id: 'user_1', email: 'ada@example.com', username: 'Ada', ip_address: '192.0.2.1' },
    extra: { arguments: ['db.password', 'SECRET'], component: 'KeyValueDetail' },
    tags: { area: 'key-values' },
  })

  it('sends the page template, no Referer, the user id and the PostHog session', () => {
    const result = scrubSentryEvent(event(), router, { posthog_session_id: 'session_1', posthog_distinct_id: 'user_1' })

    expect(result.request).toEqual({
      url: `${origin}/:org/:project/:view/key-values/:key`,
      headers: { 'User-Agent': 'Mozilla/5.0' },
    })
    expect(result.user).toEqual({ id: 'user_1' })
    expect(result.extra).toEqual({ component: 'KeyValueDetail' })
    expect(result.tags).toEqual({ area: 'key-values', posthog_session_id: 'session_1', posthog_distinct_id: 'user_1' })
    expect(JSON.stringify(result)).not.toMatch(raw)
  })

  it('drops a user without an id and adds no tags without PostHog', () => {
    const result = scrubSentryEvent({ ...event(), user: { email: 'ada@example.com' }, tags: undefined }, router, {})

    expect(result.user).toBeUndefined()
    expect(result.tags).toEqual({})
  })
})

describe('Sentry breadcrumbs', () => {
  it('templates navigations', () => {
    const breadcrumb: Breadcrumb = {
      category: 'navigation',
      data: { from: '/acme/billing/prod/annotations?q=secret', to: '/acme/billing/prod/key-values/db.password#value' },
    }

    expect(scrubBreadcrumb(breadcrumb, router)?.data).toEqual({
      from: '/:org/:project/:view/annotations',
      to: '/:org/:project/:view/key-values/:key',
    })
  })

  it('drops the query and the hash of API calls and keeps their path', () => {
    const url = 'https://api.test/api/v1/acme/billing/prod/key-values/db.password?reveal=true&q=secret#value'
    const fetch: Breadcrumb = { category: 'fetch', type: 'http', data: { method: 'GET', url, status_code: 503 } }
    const xhr: Breadcrumb = { category: 'xhr', type: 'http', data: { method: 'PUT', url } }

    expect(scrubBreadcrumb(fetch, router)?.data).toEqual({
      method: 'GET',
      url: 'https://api.test/api/v1/acme/billing/prod/key-values/db.password',
      status_code: 503,
    })
    expect(scrubBreadcrumb(xhr, router)?.data?.url).toBe(
      'https://api.test/api/v1/acme/billing/prod/key-values/db.password',
    )
  })

  it('drops clicks and key presses, which carry aria-label, title and alt texts', () => {
    const click: Breadcrumb = {
      category: 'ui.click',
      message:
        'button.rounded[aria-label="User menu for Jane Doe"][type="button"] > img.rounded-full[alt="jane@example.com"]',
    }
    const input: Breadcrumb = { category: 'ui.input', message: 'input[aria-label="Remove label:confidential"]' }

    expect(scrubBreadcrumb(click, router)).toBeNull()
    expect(scrubBreadcrumb(input, router)).toBeNull()
  })

  it("drops PostHog's own requests and keeps the API calls", () => {
    const posthog = (url: string): Breadcrumb => ({ category: 'fetch', type: 'http', data: { method: 'POST', url } })
    const xhr: Breadcrumb = {
      category: 'xhr',
      type: 'http',
      data: { method: 'POST', url: 'https://eu.i.posthog.com/e/' },
    }
    const api = posthog('https://api.test/api/v1/acme/billing/prod/annotations?q=secret')

    expect(
      scrubBreadcrumb(posthog('https://eu.i.posthog.com/i/v0/e/?ip=0&ver=1'), router, 'https://eu.i.posthog.com'),
    ).toBeNull()
    expect(scrubBreadcrumb(xhr, router, 'https://eu.i.posthog.com')).toBeNull()
    // A host behind a reverse proxy: its path, not the whole origin.
    expect(scrubBreadcrumb(posthog('https://api.test/ingest/e/'), router, 'https://api.test/ingest')).toBeNull()
    expect(scrubBreadcrumb(api, router, 'https://api.test/ingest')?.data?.url).toBe(
      'https://api.test/api/v1/acme/billing/prod/annotations',
    )
    // Without PostHog nothing is dropped.
    expect(scrubBreadcrumb(posthog('https://eu.i.posthog.com/e/'), router)?.data?.url).toBe(
      'https://eu.i.posthog.com/e/',
    )
  })

  it('leaves other breadcrumbs alone', () => {
    const event: Breadcrumb = { category: 'sentry.event', message: 'Storyteller API answered 503' }

    expect(scrubBreadcrumb(event, router)).toEqual({
      category: 'sentry.event',
      message: 'Storyteller API answered 503',
    })
  })
})
