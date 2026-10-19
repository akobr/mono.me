import { describe, expect, it, vi } from 'vitest'
import { StorytellerError } from '@42for.net/2splatform.sdk'
import { AppConfigError, isLocalhost, loadAppConfig } from '@/boot/app-config'
import { onApiError, reportApiError, shouldReport } from '@/boot/api-errors'
import { shouldRetry } from '@/boot/query'
import { captureApiError } from '@/boot/telemetry'

vi.mock('@/boot/telemetry', () => ({ captureApiError: vi.fn() }))

const json = (body: unknown) =>
  vi.fn(
    async () => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } }),
  ) as unknown as typeof fetch

describe('loadAppConfig', () => {
  it('prefers /app-config.json and falls back to VITE_* variables', async () => {
    const config = await loadAppConfig(json({ apiBaseUrl: 'https://api.test/api/', environmentName: 'staging' }), {
      VITE_AUTH_API_HOSTNAME: 'auth.test',
    } as ImportMetaEnv)

    expect(config).toEqual({
      apiBaseUrl: 'https://api.test/api',
      authApiHostname: 'auth.test',
      environmentName: 'staging',
      posthogHost: 'https://eu.i.posthog.com',
    })
  })

  it('reads the telemetry keys and the release, with the PostHog EU host by default', async () => {
    const config = await loadAppConfig(
      json({
        apiBaseUrl: 'https://api.test/api',
        sentryDsn: 'https://key@o1.ingest.de.sentry.io/2',
        posthogProjectToken: ' phc_file ',
        release: '65c570ed',
      }),
      {
        VITE_SENTRY_DSN: 'https://other@o1.ingest.de.sentry.io/3',
        VITE_POSTHOG_PROJECT_TOKEN: 'phc_env',
      } as ImportMetaEnv,
    )

    expect(config).toMatchObject({
      sentryDsn: 'https://key@o1.ingest.de.sentry.io/2',
      posthogProjectToken: 'phc_file',
      posthogHost: 'https://eu.i.posthog.com',
      release: '65c570ed',
    })
  })

  it('falls back to the telemetry variables and leaves the keys out when neither is set', async () => {
    const fromVariables = await loadAppConfig(json({ apiBaseUrl: 'https://api.test/api', posthogHost: ' ' }), {
      VITE_POSTHOG_PROJECT_TOKEN: 'phc_env',
      VITE_POSTHOG_HOST: 'https://posthog.example.com',
    } as ImportMetaEnv)
    const none = await loadAppConfig(json({ apiBaseUrl: 'https://api.test/api', sentryDsn: '' }), {} as ImportMetaEnv)

    expect(fromVariables).toMatchObject({
      posthogProjectToken: 'phc_env',
      posthogHost: 'https://posthog.example.com',
    })
    expect(fromVariables.sentryDsn).toBeUndefined()
    expect(none.sentryDsn).toBeUndefined()
    expect(none.posthogProjectToken).toBeUndefined()
    expect(none.release).toBeUndefined()
  })

  it('resolves a relative API address against the origin', async () => {
    const config = await loadAppConfig(json({}), { VITE_API_BASE_URL: '/api' } as ImportMetaEnv)

    expect(config.apiBaseUrl).toBe(`${location.origin}/api`)
  })

  it('fails without an API address', async () => {
    const failing = vi.fn(async () => {
      throw new TypeError('offline')
    }) as unknown as typeof fetch

    await expect(loadAppConfig(failing, {} as ImportMetaEnv)).rejects.toBeInstanceOf(AppConfigError)
  })

  it('knows the hosts where AuthKit devMode is allowed', () => {
    expect(isLocalhost('localhost')).toBe(true)
    expect(isLocalhost('127.0.0.1')).toBe(true)
    expect(isLocalhost('admin.42for.net')).toBe(false)
  })
})

describe('global API errors', () => {
  it('reports server errors and unreachable APIs, not 4xx or aborts', () => {
    expect(shouldReport(new StorytellerError({ status: 503, message: 'down' }))).toBe(true)
    expect(shouldReport(new StorytellerError({ status: 0, message: 'offline' }))).toBe(true)
    expect(shouldReport(new StorytellerError({ status: 0, message: 'aborted', aborted: true }))).toBe(false)
    expect(shouldReport(new StorytellerError({ status: 409, message: 'conflict' }))).toBe(false)
  })

  it('notifies listeners until they unsubscribe', () => {
    const listener = vi.fn()
    const off = onApiError(listener)

    reportApiError(new StorytellerError({ status: 500, message: 'Boom', hint: 'Try later.' }))
    off()
    reportApiError(new StorytellerError({ status: 500, message: 'Again' }))

    expect(listener).toHaveBeenCalledExactlyOnceWith({ title: 'The API failed', description: 'Try later.' })
  })

  it('sends Sentry exactly what it reports', () => {
    vi.mocked(captureApiError).mockClear()
    const reported = [
      new StorytellerError({ status: 503, message: 'down' }),
      new StorytellerError({ status: 0, message: 'offline' }),
      new TypeError('boom'),
    ]
    const ignored = [
      new StorytellerError({ status: 404, message: 'missing' }),
      new StorytellerError({ status: 0, message: 'aborted', aborted: true }),
      new DOMException('aborted', 'AbortError'),
    ]

    for (const error of [...reported, ...ignored]) {
      reportApiError(error)
    }

    expect(vi.mocked(captureApiError).mock.calls).toEqual(reported.map((error) => [error]))
  })

  it('retries a failed read once, but never a 4xx', () => {
    expect(shouldRetry(0, new StorytellerError({ status: 502, message: 'bad gateway' }))).toBe(true)
    expect(shouldRetry(1, new StorytellerError({ status: 502, message: 'bad gateway' }))).toBe(false)
    expect(shouldRetry(0, new StorytellerError({ status: 404, message: 'missing' }))).toBe(false)
  })
})
