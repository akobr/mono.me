# Sentry and PostHog in the Admin UI

Linear: [424-52](https://linear.app/424fun/issue/424-52/connect-posthog-to-client-applicaitons).

## Overview

The single phase of [Sentry and PostHog in the Admin UI](../2026-10-19%20Sentry%20and%20PostHog%20in%20the%20Admin%20UI.md), sections 1-12. The admin UI reports errors to Sentry and pages and clicks to PostHog, each only when the deployment sets its key in `app-config.json`, and neither in the e2e and test modes. Both SDKs are lazy chunks, so a deployment without keys downloads neither. URLs go out as route templates, identity is the AuthKit user id only, and the screens with secrets, documents, personal data or typed text are under `ph-no-capture`. The deploy step that writes `app-config.json` and the CSP origins is a tested script now. The `sform` half of 424-52 is reviewed in [PostHog Analytics for sform](../../../Cli/specs/reviews/2026-10-19%20PostHog%20Analytics%20for%20sform.md).

Commits:
- `df89039d` test(platform): expect the Modeling path of a new descendant-type schema (a stale expectation that failed the unit tests of `main`, see Notes)
- `1d54e73d` feat(platform): report admin UI errors to Sentry and pages and clicks to PostHog
- `7c80575a` ci(platform): write the telemetry settings and CSP origins of the admin UI on deploy
- `82be177d` docs(platform): document the error reporting and analytics of the admin UI

Deviations from the plan:
- `scrubBreadcrumb` drops every `ui.*` breadcrumb (clicks and key presses) and the `fetch`/`xhr` breadcrumbs of PostHog's own requests, and returns `Breadcrumb | null`; `startSentry` takes the PostHog host for that (code review).
- `scrubPostHogEvent` also removes the campaign parameters and the search keyword that posthog-js reads from the entry URL and the referrer even with `save_campaign_params: false` (code review).
- `ph-no-capture` covers more than the list of section 9 (code review): `SharedCertificatesTable` and its issue dialog, `pages/invitations/index.vue`, `SchemaForm` (the configuration form, whose array titles are values), the active terms of `AnnotationFilterSheet` (their labels are the filter text), the narrow-screen list of `key-values.vue` (value previews), `AuditActorLink` and the stamps of `AnnotationDetail` (people's names). `UserMenu` gets it on the trigger and on its teleported content (`:ui.content`).
- `loadAppConfig` fills `posthogHost` with the EU host when it is missing (`defaultPostHogHost`, exported from `app-config.ts`).
- `telemetry-privacy.ts` has a fifth helper, `pathTemplate`, for path-style values (`$pathname`, navigation breadcrumbs, the `page` tag).
- The deploy script treats an unset or empty variable as missing, as the inline script did; it does not trim whitespace.

## What Was Done

### 1. Packages

As planned: `@sentry/vue` `^11.8.0` and `posthog-js` `^1.438.12`, installed with npm 11 (the CI version), so the lockfile only gains the new packages.

### 2. Runtime configuration

As planned: `sentryDsn`, `posthogProjectToken`, `posthogHost` (default EU) and `release` in `AppConfig`, the three `VITE_*` fallbacks in `env.d.ts`, commented-out lines in `.env.example`. `public/app-config.json` and `.env.development` set nothing.

### 3. When telemetry runs

As planned. `telemetryAllowed` (`MODE` is neither `e2e` nor `test`) is static, and `npm run build:e2e` has no Sentry or PostHog chunk. A PostHog host that is not `https` (or `http` on localhost) leaves PostHog off with a `console.warn`.

### 4. Modules

As planned: `boot/telemetry.ts` (the facade), `boot/sentry.ts` (named imports only), `boot/posthog.ts` (`posthog-js/no-external`), `utils/telemetry-privacy.ts`, and `await startTelemetry(app, router, state.config)` in `main.ts` after the boot state and before the router and the mount.
- Every facade call into an SDK is wrapped, so a failing SDK never breaks a navigation or a toast.
- `startTelemetry` accepts an undefined config (a failed boot) and installs the identity watch only when an SDK started.
- Production build: `sentry-*.js` 114.7 kB (39.2 kB gzip), `posthog-*.js` 352.7 kB (107.7 kB gzip), lazy; the index chunk holds the facade only. Neither chunk contains `replayIntegration`, `lazyLoadIntegration`, `browser.sentry-cdn.com`, `/config.js`, `new Function` or `eval(`.

### 5. URLs

As planned. `pageTemplate` and `pathTemplate` resolve the path through the router and drop query and hash; a value of another origin becomes the origin; only paths and http(s) URLs are templated, so values such as `$direct` stay.

### 6. PostHog

As planned, with the options of the spec (all checked by `tsc` against the installed types) and `before_send` templating every URL- and path-bearing key of `properties`, `$set` and `$set_once`, never `properties.token`. Plus the campaign parameters above. The pageview guard (`router/guards/telemetry.ts`, after page-memory) counts the first navigation, also to `/`, and every later successful navigation that changes the path.

### 7. Sentry

As planned: errors only, `attachProps: false`, `enhanceFetchErrorMessages: 'report-only'`, the restrictive `dataCollection`, without `BrowserSession` and `Console`.
- `scrubSentryEvent` templates `request.url`, deletes `request.query_string`, removes `Referer` (any case) and `extra.arguments`, reduces `user` to `{ id }` (removes a user without id), and adds `posthog_session_id` and `posthog_distinct_id` from the live PostHog instance.
- `captureApiError` (from `reportApiError`, after `shouldReport`): a `StorytellerError` becomes `captureMessage` with `api_status`, `api_error_code` (only when there is one) and `page` (a path template), level `error` for `5xx` and `warning` for a network failure, and the fingerprint `['storyteller-api', status, errorCode?]`; anything else goes to `captureException`.

### 8. Identity

As planned: a watch on `session.user?.id` calls `identifyUser(id)` (`posthog.identify(id)` and `setUser({ id })`), and `onSignOut(() => resetTelemetry())` in `main.ts`.

### 9. Sensitive areas

As planned, plus the components listed under the deviations.

### 10. CSP and deployment

As planned.
- `connect-src 'self' __API_ORIGIN__ __SENTRY_ORIGIN__ __POSTHOG_ORIGIN__ https://api.workos.com https://__AUTH_HOSTNAME__`.
- `build/environment-config.ts` (pure: variables in, `app-config.json` and the replacements out) and `build/write-environment-config.ts` (the entry), run by the deploy step as `node build/write-environment-config.ts` with the Node 24 of `.nvmrc`. The old variables give the same output and errors as the inline script. A host set without a token is still written and validated, but its origin stays out of the CSP. An origin that is not configured leaves two spaces in `connect-src`, which the CSP grammar allows.
- The deploy step passes `vars.UI_ADMIN_SENTRY_DSN`, `vars.UI_ADMIN_POSTHOG_PROJECT_TOKEN`, `vars.UI_ADMIN_POSTHOG_HOST` and `github.sha`; all are optional.

### 11. Tests

Unit tests:
- `boot.test.ts`: the new config fields, and `reportApiError` → `captureApiError` only for what `shouldReport` keeps.
- `telemetry-privacy.test.ts`: templates of the callback, invitation, key-value and machine URLs, hashes, other origins and unknown paths; a `$pageview` and an `$identify` with `$set_once`; campaign parameters; Sentry events; breadcrumbs (navigation, fetch, dropped clicks and key presses, PostHog's own requests).
- `boot/telemetry.test.ts`: off without keys and in the e2e and test modes, each SDK alone, failed loads and inits, the PostHog options, the Sentry integrations, identity, reset, API errors.
- `router/telemetry.test.ts`: the pageview rules.
- `build/environment-config.test.ts`, `build/csp.test.ts`, and `build/write-environment-config.test.ts`, which runs the script with `node` the way the deploy step does (code review: nothing else runs it before a deployment).
- Component tests that `AuditActorLink`, an array item of `SchemaForm` and a filter term of `AnnotationFilterSheet` are under `ph-no-capture`.

Results:
- `npm run lint`: clean.
- `npm test`: 67 files, 897 tests passed.
- `npm run build`: passes.
- `npm run test:e2e` (desktop and mobile): 296 passed, 6 skipped; the e2e build has no Sentry or PostHog chunk.
- Visual: not run; the changes add a class without style.

### 12. Documentation

As planned: `src/Platform/ui.admin/README.md` (the configuration and deployment rows, the placeholders, a "Telemetry" subsection with the off switches, the URL rules, identity, what Sentry and PostHog get and the `ph-no-capture` rule, the CSP note on `posthog-js/no-external`, the architecture tree) and a paragraph in "Running it" of `docs/42for.net/platform/admin-ui.md`. Both say that names still leave the browser as the text of clicked links and buttons and in the paths of the API calls in Sentry breadcrumbs.

### Review

An adversarial review (correctness, privacy, deployment and documentation) with a skeptic per finding.
- Confirmed and fixed: Sentry click breadcrumbs carried the `aria-label`, `title` and `alt` of the clicked element (the user's name or e-mail in the user menu, members' names); clicks on people's names, configuration values in the form view, filter terms and value previews on narrow screens reached PostHog as link and button text; campaign parameters of the entry URL; the docs claimed the names in the path never leave the browser; the deploy script ran under Node only in the deployment.
- Refuted: the boot waits for the two chunks without a time limit (they come from the app's own origin, right after the index chunk; a failed load is caught and leaves the SDK off).

### Notes

- The `ui-admin` GitHub environment needs `UI_ADMIN_POSTHOG_PROJECT_TOKEN` (the token of the issue) for PostHog and `UI_ADMIN_SENTRY_DSN` (a Sentry project for the admin UI, which does not exist yet) for Sentry. Without them the deployment works as before and sends nothing.
- `df89039d`: 424-59 added a test expecting `/schemas/annotation/…` after 424-38 had moved the schema pages under `modeling/`, so the UI unit tests of `main` failed. The expectation now has the `modeling/` path.
- Autocapture still sends the text of links and buttons outside the marked areas, which can be an organization, project, view, annotation or key name.
