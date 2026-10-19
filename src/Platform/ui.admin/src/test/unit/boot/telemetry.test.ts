import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { routes } from 'vue-router/auto-routes'
import { StorytellerError } from '@42for.net/2splatform.sdk'
import type { Breadcrumb, ErrorEvent, init } from '@sentry/vue'
import type { CaptureResult, PostHogConfig } from 'posthog-js'
import type { AppConfig } from '@/boot/app-config'
import type { AuthService } from '@/boot/auth'

// The SDK modules are mocked; each test loads boot/telemetry afresh (it keeps the started SDKs in module state).
const sdk = vi.hoisted(() => ({
  sentry: {
    init: vi.fn(),
    setUser: vi.fn(),
    captureException: vi.fn(),
    captureMessage: vi.fn(),
  },
  posthog: {
    init: vi.fn(),
    identify: vi.fn(),
    reset: vi.fn(),
    capture: vi.fn(),
    get_session_id: vi.fn(() => 'session_1'),
    get_distinct_id: vi.fn(() => 'user_1'),
  },
}))

vi.mock('@sentry/vue', () => sdk.sentry)
vi.mock('posthog-js/no-external', () => ({ default: sdk.posthog }))

type SentryOptions = NonNullable<Parameters<typeof init>[0]>

const dsn = 'https://key@o1.ingest.de.sentry.io/2'
const token = 'phc_test'

const baseConfig: AppConfig = {
  apiBaseUrl: 'https://api.test/api',
  environmentName: 'staging',
  posthogHost: 'https://eu.i.posthog.com',
}

function fakeAuth(): AuthService {
  return {
    kind: 'mock',
    getUser: () => ({
      id: 'user_1',
      email: 'ada@example.com',
      name: 'Ada Lovelace',
      pictureUrl: 'https://img.test/ada',
    }),
    getAccessToken: async () => 'token',
    signIn: vi.fn(async () => {}),
    signOut: vi.fn(async () => {}),
    consumeReturnTo: () => undefined,
  }
}

/** A fresh telemetry module (and session store), as main.ts has it after the boot. */
async function load(mode = 'production') {
  vi.stubEnv('MODE', mode)
  vi.resetModules()
  const { createPinia, setActivePinia } = await import('pinia')
  setActivePinia(createPinia())
  const { setBootState } = await import('@/boot/state')
  setBootState({ status: 'ready', auth: fakeAuth() })
  return {
    telemetry: await import('@/boot/telemetry'),
    session: (await import('@/stores/session')).useSessionStore(),
  }
}

const router = createRouter({ history: createMemoryHistory(), routes })
const app = createApp({ render: () => null })

async function started(config: Partial<AppConfig>, mode?: string) {
  const loaded = await load(mode)
  await loaded.telemetry.startTelemetry(app, router, { ...baseConfig, ...config })
  return loaded
}

const sentryOptions = () => sdk.sentry.init.mock.lastCall![0] as SentryOptions
const posthogOptions = () => sdk.posthog.init.mock.lastCall![1] as Partial<PostHogConfig>

beforeEach(() => {
  vi.resetAllMocks()
})

afterEach(() => {
  vi.doUnmock('@/boot/sentry')
  vi.unstubAllEnvs()
  history.replaceState(null, '', '/')
})

describe('starting telemetry', () => {
  it('starts nothing without keys, and the functions do nothing', async () => {
    const { telemetry } = await started({})

    telemetry.identifyUser('user_1')
    telemetry.resetTelemetry()
    telemetry.captureApiError(new StorytellerError({ status: 503, message: 'down' }))
    telemetry.capturePageview()

    expect(sdk.sentry.init).not.toHaveBeenCalled()
    expect(sdk.posthog.init).not.toHaveBeenCalled()
    expect(Object.values(sdk.sentry).every((fn) => fn.mock.calls.length === 0)).toBe(true)
    expect(Object.values(sdk.posthog).every((fn) => fn.mock.calls.length === 0)).toBe(true)
  })

  it('starts nothing in the e2e mode and in unit tests, whatever the keys', async () => {
    await started({ sentryDsn: dsn, posthogProjectToken: token }, 'e2e')
    await started({ sentryDsn: dsn, posthogProjectToken: token }, 'test')

    expect(sdk.sentry.init).not.toHaveBeenCalled()
    expect(sdk.posthog.init).not.toHaveBeenCalled()
  })

  it('starts nothing without a configuration (a failed boot)', async () => {
    const { telemetry } = await load()
    await telemetry.startTelemetry(app, router, undefined)

    expect(sdk.sentry.init).not.toHaveBeenCalled()
    expect(sdk.posthog.init).not.toHaveBeenCalled()
  })

  it('starts Sentry alone with its DSN: errors only, without props, sessions or console breadcrumbs', async () => {
    await started({ sentryDsn: dsn, release: '65c570ed' })

    expect(sdk.posthog.init).not.toHaveBeenCalled()
    const options = sentryOptions()
    expect(options).toMatchObject({
      app,
      dsn,
      environment: 'staging',
      release: '65c570ed',
      attachProps: false,
      enhanceFetchErrorMessages: 'report-only',
      dataCollection: {
        userInfo: false,
        cookies: false,
        httpHeaders: { request: { allow: ['user-agent'] }, response: false },
        httpBodies: [],
        urlQueryParams: false,
      },
    })
    // No tracing: no trace headers reach the API.
    expect(options).not.toHaveProperty('tracesSampleRate')

    const defaults = ['EventFilters', 'Breadcrumbs', 'Console', 'GlobalHandlers', 'BrowserSession', 'Vue'].map(
      (name) => ({ name }),
    )
    const integrations = options.integrations as (integrations: { name: string }[]) => { name: string }[]
    expect(integrations(defaults).map((integration) => integration.name)).toEqual([
      'EventFilters',
      'Breadcrumbs',
      'GlobalHandlers',
      'Vue',
    ])
  })

  it('starts PostHog alone with its token, with the privacy settings', async () => {
    await started({ posthogProjectToken: token })

    expect(sdk.sentry.init).not.toHaveBeenCalled()
    expect(sdk.posthog.init).toHaveBeenCalledOnce()
    expect(sdk.posthog.init.mock.lastCall![0]).toBe(token)
    expect(posthogOptions()).toMatchObject({
      api_host: 'https://eu.i.posthog.com',
      defaults: '2026-05-30',
      persistence: 'localStorage',
      capture_pageview: false,
      capture_pageleave: false,
      autocapture: { dom_event_allowlist: ['click'], element_allowlist: ['a', 'button'] },
      mask_all_element_attributes: true,
      save_referrer: false,
      save_campaign_params: false,
      disable_session_recording: true,
      capture_exceptions: false,
      capture_performance: false,
      capture_dead_clicks: false,
      capture_heatmaps: false,
      disable_surveys: true,
      disable_product_tours: true,
      disable_conversations: true,
      disable_external_dependency_loading: true,
      advanced_disable_flags: true,
      respect_dnt: true,
    })
  })

  it('templates the URLs of PostHog events before they are sent', async () => {
    await started({ posthogProjectToken: token })
    const beforeSend = posthogOptions().before_send as (event: CaptureResult) => CaptureResult | null

    const event = beforeSend({
      uuid: 'event_1',
      event: '$pageview',
      properties: {
        token,
        $current_url: `${location.origin}/acme/billing/prod/key-values/db.password?q=secret#value`,
        $pathname: '/acme/billing/prod/key-values/db.password',
      },
    })

    expect(event?.properties).toEqual({
      token,
      $current_url: `${location.origin}/:org/:project/:view/key-values/:key`,
      $pathname: '/:org/:project/:view/key-values/:key',
    })
  })

  it('uses another PostHog host, or http on localhost, and stays off for any other host', async () => {
    await started({ posthogProjectToken: token, posthogHost: 'https://posthog.example.com/' })
    expect(posthogOptions().api_host).toBe('https://posthog.example.com')

    await started({ posthogProjectToken: token, posthogHost: 'http://localhost:8010' })
    expect(posthogOptions().api_host).toBe('http://localhost:8010')

    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    sdk.posthog.init.mockClear()
    await started({ posthogProjectToken: token, posthogHost: 'http://posthog.example.com' })
    await started({ posthogProjectToken: token, posthogHost: 'eu.i.posthog.com' })

    expect(sdk.posthog.init).not.toHaveBeenCalled()
    expect(warn).toHaveBeenCalledTimes(2)
  })

  it('leaves an SDK that cannot be loaded off, without failing', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    // The lazy chunk does not arrive (offline, blocked, a deployment replaced it).
    vi.doMock('@/boot/sentry', () => {
      throw new TypeError('Failed to fetch dynamically imported module.')
    })

    const { telemetry } = await started({ sentryDsn: dsn, posthogProjectToken: token })
    telemetry.captureApiError(new StorytellerError({ status: 503, message: 'down' }))

    expect(warn).toHaveBeenCalledOnce()
    expect(sdk.sentry.captureMessage).not.toHaveBeenCalled()
    expect(sdk.posthog.init).toHaveBeenCalledOnce()
  })

  it('leaves an SDK that fails to start off, without failing', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    sdk.sentry.init.mockImplementation(() => {
      throw new Error('Invalid DSN.')
    })
    sdk.posthog.init.mockImplementation(() => {
      throw new Error('Storage is full.')
    })

    const { telemetry } = await started({ sentryDsn: dsn, posthogProjectToken: token })
    telemetry.identifyUser('user_2')
    telemetry.resetTelemetry()
    telemetry.captureApiError(new TypeError('boom'))
    telemetry.capturePageview()

    expect(warn).toHaveBeenCalledTimes(2)
    expect(sdk.sentry.setUser).not.toHaveBeenCalled()
    expect(sdk.sentry.captureException).not.toHaveBeenCalled()
    expect(sdk.posthog.identify).not.toHaveBeenCalled()
    expect(sdk.posthog.reset).not.toHaveBeenCalled()
    expect(sdk.posthog.capture).not.toHaveBeenCalled()
  })

  it('does nothing until the SDKs have started', async () => {
    const { telemetry } = await load()

    const starting = telemetry.startTelemetry(app, router, {
      ...baseConfig,
      sentryDsn: dsn,
      posthogProjectToken: token,
    })
    telemetry.captureApiError(new TypeError('boom'))
    telemetry.capturePageview()
    await starting

    expect(sdk.sentry.captureException).not.toHaveBeenCalled()
    expect(sdk.posthog.capture).not.toHaveBeenCalled()
  })
})

describe('identity', () => {
  it('identifies the signed-in user by id only, and again when the user changes', async () => {
    const { session } = await started({ sentryDsn: dsn, posthogProjectToken: token })

    expect(sdk.posthog.identify.mock.calls).toEqual([['user_1']])
    expect(sdk.sentry.setUser.mock.calls).toEqual([[{ id: 'user_1' }]])

    session.user = { id: 'user_2', email: 'grace@example.com', name: 'Grace Hopper' }
    await vi.waitFor(() => expect(sdk.posthog.identify).toHaveBeenLastCalledWith('user_2'))
    expect(sdk.sentry.setUser).toHaveBeenLastCalledWith({ id: 'user_2' })
    expect(JSON.stringify([sdk.posthog.identify.mock.calls, sdk.sentry.setUser.mock.calls])).not.toMatch(
      /example\.com|Ada|Grace|img\.test/,
    )
  })

  it('forgets the user on reset', async () => {
    const { telemetry } = await started({ sentryDsn: dsn, posthogProjectToken: token })

    telemetry.resetTelemetry()

    expect(sdk.posthog.reset).toHaveBeenCalledOnce()
    expect(sdk.sentry.setUser).toHaveBeenLastCalledWith(null)
  })

  it('tags every Sentry event with the current PostHog session and person', async () => {
    await started({ sentryDsn: dsn, posthogProjectToken: token })
    const beforeSend = sentryOptions().beforeSend as (event: ErrorEvent) => ErrorEvent
    const event = (): ErrorEvent => ({
      type: undefined,
      request: { url: `${location.origin}/acme/billing/settings?x=1` },
    })

    expect(beforeSend(event())).toMatchObject({
      request: { url: `${location.origin}/:org/:project/settings` },
      tags: { posthog_session_id: 'session_1', posthog_distinct_id: 'user_1' },
    })

    // The session rotates after 30 minutes of inactivity.
    sdk.posthog.get_session_id.mockReturnValue('session_2')
    expect(beforeSend(event()).tags).toMatchObject({ posthog_session_id: 'session_2' })
  })

  it("leaves clicks and PostHog's requests out of Sentry's breadcrumbs", async () => {
    await started({ sentryDsn: dsn, posthogProjectToken: token, posthogHost: 'https://posthog.example.com/' })
    const beforeBreadcrumb = sentryOptions().beforeBreadcrumb as (breadcrumb: Breadcrumb) => Breadcrumb | null
    const request = (url: string): Breadcrumb => ({ category: 'fetch', type: 'http', data: { method: 'POST', url } })

    expect(
      beforeBreadcrumb({ category: 'ui.click', message: 'button[aria-label="User menu for Ada Lovelace"]' }),
    ).toBeNull()
    expect(beforeBreadcrumb(request('https://posthog.example.com/i/v0/e/?ip=0'))).toBeNull()
    expect(beforeBreadcrumb(request('https://api.test/api/v1/acme/billing/prod/annotations?q=1'))?.data?.url).toBe(
      'https://api.test/api/v1/acme/billing/prod/annotations',
    )
  })

  it('tags no PostHog session when PostHog is off', async () => {
    await started({ sentryDsn: dsn })
    const beforeSend = sentryOptions().beforeSend as (event: ErrorEvent) => ErrorEvent

    expect(beforeSend({ type: undefined }).tags).toEqual({})
  })
})

describe('pageviews and API errors', () => {
  it('captures a $pageview', async () => {
    const { telemetry } = await started({ posthogProjectToken: token })

    telemetry.capturePageview()

    expect(sdk.posthog.capture).toHaveBeenCalledExactlyOnceWith('$pageview')
  })

  it('reports a server error by its status and error code only', async () => {
    history.replaceState(null, '', '/acme/billing/prod/key-values/db.password?q=secret')
    const { telemetry } = await started({ sentryDsn: dsn })

    telemetry.captureApiError(
      new StorytellerError({
        status: 503,
        message: 'Cosmos DB is down for acme/billing.',
        hint: 'Retry db.password later.',
        errorCode: 'StorageUnavailable',
        body: { Message: 'Cosmos DB is down for acme/billing.', Secret: 'SECRET' },
        cause: new Error('SECRET'),
      }),
    )

    expect(sdk.sentry.captureMessage).toHaveBeenCalledExactlyOnceWith('Storyteller API answered 503', {
      level: 'error',
      tags: { api_status: 503, api_error_code: 'StorageUnavailable', page: '/:org/:project/:view/key-values/:key' },
      fingerprint: ['storyteller-api', '503', 'StorageUnavailable'],
    })
    expect(JSON.stringify(sdk.sentry.captureMessage.mock.calls)).not.toMatch(/Cosmos|acme|db\.password|SECRET|Retry/)
  })

  it('reports an unreachable API as a warning, without the request path', async () => {
    const { telemetry } = await started({ sentryDsn: dsn })

    telemetry.captureApiError(
      new StorytellerError({
        status: 0,
        message: 'The Storyteller API is not reachable (/api/v1/acme/billing/prod/key-values/db.password).',
        cause: new TypeError('Failed to fetch'),
      }),
    )

    expect(sdk.sentry.captureMessage).toHaveBeenCalledExactlyOnceWith('Storyteller API is not reachable', {
      level: 'warning',
      tags: { api_status: 0, page: '/' },
      fingerprint: ['storyteller-api', '0'],
    })
    expect(JSON.stringify(sdk.sentry.captureMessage.mock.calls)).not.toMatch(/acme|db\.password|Failed to fetch/)
  })

  it('reports any other error as an exception', async () => {
    const { telemetry } = await started({ sentryDsn: dsn })
    const error = new TypeError('Cannot read properties of undefined')

    telemetry.captureApiError(error)

    expect(sdk.sentry.captureException).toHaveBeenCalledExactlyOnceWith(error)
    expect(sdk.sentry.captureMessage).not.toHaveBeenCalled()
  })
})
