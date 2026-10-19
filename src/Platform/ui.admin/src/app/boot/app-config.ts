export interface AppConfig {
  /** API root including "/api", for example https://api.2s.42for.net/api. */
  apiBaseUrl: string
  /** Custom AuthKit domain (refresh token in an HttpOnly cookie). Required on every host except localhost. */
  authApiHostname?: string
  environmentName: string
  /** Sentry DSN of the admin UI project; no error reporting without it. */
  sentryDsn?: string
  /** PostHog project token (phc_…); no product analytics without it. */
  posthogProjectToken?: string
  /** PostHog API host; loadAppConfig fills in the EU cloud when it is missing. */
  posthogHost?: string
  /** Written by the deployment (the commit SHA); the Sentry release. */
  release?: string
}

export const defaultPostHogHost = 'https://eu.i.posthog.com'

export class AppConfigError extends Error {}

type Fetch = typeof globalThis.fetch

const nonEmpty = (value: unknown): string | undefined =>
  typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined

/**
 * Loads /app-config.json, written per environment by the deployment, so one build serves every environment.
 * VITE_* variables (.env.development, .env.local) are the fallback for local development.
 */
export async function loadAppConfig(
  fetchImpl: Fetch = globalThis.fetch,
  env: ImportMetaEnv = import.meta.env,
): Promise<AppConfig> {
  let file: Record<string, unknown> = {}

  try {
    const response = await fetchImpl('/app-config.json', { cache: 'no-store' })
    if (response.ok && (response.headers.get('content-type') ?? '').includes('json')) {
      file = (await response.json()) as Record<string, unknown>
    }
  } catch {
    // no file: rely on the build-time variables
  }

  const apiBaseUrl = nonEmpty(file.apiBaseUrl) ?? nonEmpty(env.VITE_API_BASE_URL)
  if (!apiBaseUrl) {
    throw new AppConfigError('The API address is not configured (apiBaseUrl in /app-config.json or VITE_API_BASE_URL).')
  }

  return {
    // A relative value ("/api") means the same origin as the app, for example behind a reverse proxy.
    apiBaseUrl: new URL(apiBaseUrl, location.origin).href.replace(/\/+$/, ''),
    authApiHostname: nonEmpty(file.authApiHostname) ?? nonEmpty(env.VITE_AUTH_API_HOSTNAME),
    environmentName: nonEmpty(file.environmentName) ?? nonEmpty(env.VITE_ENVIRONMENT_NAME) ?? 'unknown',
    sentryDsn: nonEmpty(file.sentryDsn) ?? nonEmpty(env.VITE_SENTRY_DSN),
    posthogProjectToken: nonEmpty(file.posthogProjectToken) ?? nonEmpty(env.VITE_POSTHOG_PROJECT_TOKEN),
    posthogHost: nonEmpty(file.posthogHost) ?? nonEmpty(env.VITE_POSTHOG_HOST) ?? defaultPostHogHost,
    release: nonEmpty(file.release),
  }
}

export const isLocalhost = (hostname: string = location.hostname) =>
  hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '[::1]'
