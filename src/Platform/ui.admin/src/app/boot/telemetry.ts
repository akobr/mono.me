import { watch, type App } from 'vue'
import type { Router } from 'vue-router'
import { useSessionStore } from '@/stores/session'
import { defaultPostHogHost, isLocalhost, type AppConfig } from './app-config'
import type { PostHogTelemetry } from './posthog'
import type { SentryTelemetry } from './sentry'

// Static, like isMock in main.ts: the e2e mode (dev:mock, every Playwright run) and Vitest load neither SDK, even when
// a developer's .env.local sets the keys, and the e2e build drops both modules.
const telemetryAllowed = import.meta.env.MODE !== 'e2e' && import.meta.env.MODE !== 'test'

let sentry: SentryTelemetry | undefined
let posthog: PostHogTelemetry | undefined

/**
 * Starts Sentry (error reporting) and PostHog (product analytics), each only when its key is configured. Both are
 * separate chunks loaded in parallel, so a deployment without keys downloads neither. A failure to load or to start an
 * SDK is logged and leaves it off; it never changes the boot. Until then, and when telemetry is off, the functions
 * below do nothing.
 */
export async function startTelemetry(app: App, router: Router, config: AppConfig | undefined): Promise<void> {
  if (!telemetryAllowed || !config) {
    return
  }

  const token = config.posthogProjectToken
  const host = token ? postHogHost(config.posthogHost ?? defaultPostHogHost) : undefined
  const [sentryModule, posthogModule] = await Promise.all([
    config.sentryDsn ? load('Sentry', () => import('./sentry')) : undefined,
    token && host ? load('PostHog', () => import('./posthog')) : undefined,
  ])

  // Sentry first: its handlers are in place when PostHog starts. Its events read the PostHog session as they are sent,
  // and its breadcrumbs leave out PostHog's requests.
  if (sentryModule) {
    sentry = start('Sentry', () =>
      sentryModule.startSentry(app, router, config, () => posthog?.correlation() ?? {}, host),
    )
  }
  if (posthogModule && token && host) {
    posthog = start('PostHog', () => posthogModule.startPostHog(router, token, host))
  }

  if (sentry || posthog) {
    const session = useSessionStore()
    watch(
      () => session.user?.id,
      (id) => {
        if (id) {
          identifyUser(id)
        }
      },
      { immediate: true },
    )
  }
}

/** Links the following events to the AuthKit user id; the e-mail, name and picture are never sent. */
export function identifyUser(id: string): void {
  safely(() => sentry?.identify(id))
  safely(() => posthog?.identify(id))
}

/** On sign-out: a new anonymous PostHog person and no Sentry user. */
export function resetTelemetry(): void {
  safely(() => posthog?.reset())
  safely(() => sentry?.reset())
}

/** Reports an API failure that reportApiError shows as a toast (network and 5xx). */
export function captureApiError(error: unknown): void {
  safely(() => sentry?.captureApiError(error))
}

/** A $pageview; PostHog's before_send reduces its URL to the route template. */
export function capturePageview(): void {
  safely(() => posthog?.capturePageview())
}

/** An absolute https URL, or http on localhost (a local PostHog); anything else leaves PostHog off. */
function postHogHost(value: string): string | undefined {
  try {
    const url = new URL(value)
    if (url.protocol === 'https:' || (url.protocol === 'http:' && isLocalhost(url.hostname))) {
      return value.replace(/\/+$/, '')
    }
  } catch {
    // not a URL
  }

  console.warn(`PostHog stays off: the host "${value}" is not an https URL.`)
  return undefined
}

async function load<T>(name: string, loader: () => Promise<T>): Promise<T | undefined> {
  try {
    return await loader()
  } catch (error) {
    console.warn(`${name} could not be loaded and stays off.`, error)
    return undefined
  }
}

function start<T>(name: string, starter: () => T): T | undefined {
  try {
    return starter()
  } catch (error) {
    console.warn(`${name} could not start and stays off.`, error)
    return undefined
  }
}

// An SDK never breaks the app: a failing call is logged and dropped.
function safely(action: () => void): void {
  try {
    action()
  } catch (error) {
    console.warn('Telemetry failed.', error)
  }
}
