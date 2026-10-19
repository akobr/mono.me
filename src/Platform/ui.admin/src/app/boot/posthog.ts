import type { Router } from 'vue-router'
// The build without the script loader: nothing is injected (CSP script-src 'self'), no eval and no worker.
import posthog from 'posthog-js/no-external'
import { scrubPostHogEvent, type TelemetryCorrelation } from '@/utils/telemetry-privacy'

export interface PostHogTelemetry {
  identify(id: string): void
  reset(): void
  capturePageview(): void
  correlation(): TelemetryCorrelation
}

/**
 * Product analytics (spec section 6). Every option the PostHog project settings could otherwise switch on is set
 * here, so the project cannot turn on replay, exception capture, web vitals, dead clicks, heatmaps or surveys.
 * Loaded on demand by boot/telemetry.ts.
 */
export function startPostHog(router: Router, token: string, host: string): PostHogTelemetry {
  posthog.init(token, {
    api_host: host,
    defaults: '2026-05-30',
    // No cookie: the app has no consent banner.
    persistence: 'localStorage',
    // Pageviews come from the router (router/guards/telemetry.ts): a query-only change is no new page.
    capture_pageview: false,
    capture_pageleave: false,
    // Clicks on links and buttons with their labels (a label can be a name), but no attributes: no href with
    // identifiers. Secrets, documents and personal data sit under ph-no-capture.
    autocapture: { dom_event_allowlist: ['click'], element_allowlist: ['a', 'button'] },
    mask_all_element_attributes: true,
    save_referrer: false,
    save_campaign_params: false,
    disable_session_recording: true,
    // Exceptions go to Sentry only.
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
    // Do Not Track and Global Privacy Control.
    respect_dnt: true,
    before_send: scrubPostHogEvent(router),
  })

  return {
    // The AuthKit user id only: no e-mail, name or picture.
    identify: (id) => posthog.identify(id),
    reset: () => posthog.reset(),
    capturePageview: () => void posthog.capture('$pageview'),
    correlation: () => {
      const correlation: TelemetryCorrelation = {}
      const sessionId = posthog.get_session_id()
      const distinctId = posthog.get_distinct_id()
      if (sessionId) {
        correlation.posthog_session_id = sessionId
      }
      if (distinctId) {
        correlation.posthog_distinct_id = distinctId
      }
      return correlation
    },
  }
}
