import type { App } from 'vue'
import type { Router } from 'vue-router'
import { isStorytellerError } from '@42for.net/2splatform.sdk'
// Named imports only: the namespace would pull replay, feedback and the CDN loader into the chunk.
import { captureException, captureMessage, init, setUser } from '@sentry/vue'
import { pathTemplate, scrubBreadcrumb, scrubSentryEvent, type TelemetryCorrelation } from '@/utils/telemetry-privacy'
import type { AppConfig } from './app-config'

export interface SentryTelemetry {
  identify(id: string): void
  reset(): void
  captureApiError(error: unknown): void
}

/**
 * Error reporting (spec section 7), errors only: no tracing (so no trace headers reach the API), no replay, no
 * release-health sessions, and no console, click, key-press or PostHog request breadcrumbs. `posthogHost` is the host
 * PostHog sends to, when it runs. Loaded on demand by boot/telemetry.ts.
 */
export function startSentry(
  app: App,
  router: Router,
  config: AppConfig,
  correlation: () => TelemetryCorrelation,
  posthogHost?: string,
): SentryTelemetry {
  init({
    app,
    dsn: config.sentryDsn,
    environment: config.environmentName,
    release: config.release,
    // Props can hold values (secrets, documents).
    attachProps: false,
    // The app keeps the original message of a failed fetch.
    enhanceFetchErrorMessages: 'report-only',
    dataCollection: {
      userInfo: false,
      cookies: false,
      httpHeaders: { request: { allow: ['user-agent'] }, response: false },
      httpBodies: [],
      urlQueryParams: false,
    },
    integrations: (defaults) =>
      defaults.filter((integration) => integration.name !== 'BrowserSession' && integration.name !== 'Console'),
    beforeSend: (event) => scrubSentryEvent(event, router, correlation()),
    beforeBreadcrumb: (breadcrumb) => scrubBreadcrumb(breadcrumb, router, posthogHost),
  })

  return {
    identify: (id) => setUser({ id }),
    reset: () => setUser(null),
    captureApiError: (error) => captureApiError(error, router),
  }
}

/**
 * A Storyteller failure is reported by its status and error code only: the message (with the request path for an
 * unreachable API), the hint, the body and the cause stay in the browser. Any other error is reported as it is.
 */
function captureApiError(error: unknown, router: Router): void {
  if (!isStorytellerError(error)) {
    captureException(error)
    return
  }

  const reachable = error.status !== 0
  const tags: Record<string, string | number> = { api_status: error.status, page: pathTemplate(router, location.href) }
  if (error.errorCode) {
    tags.api_error_code = error.errorCode
  }

  captureMessage(reachable ? `Storyteller API answered ${error.status}` : 'Storyteller API is not reachable', {
    level: reachable ? 'error' : 'warning',
    tags,
    fingerprint: ['storyteller-api', String(error.status), ...(error.errorCode ? [error.errorCode] : [])],
  })
}
