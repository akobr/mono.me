# Sentry and PostHog in the Admin UI

Linear: [424-52](https://linear.app/424fun/issue/424-52/connect-posthog-to-client-applicaitons).

Line numbers refer to the tree this plan was written against (`1614279b`). No application code is changed by this document.

424-52 covers both client applications of the platform. This spec is the admin UI half; the `sform` half is [PostHog Analytics for sform](../../Cli/specs/2026-10-19%20PostHog%20Analytics%20for%20sform.md). Both use the same PostHog project in the EU cloud. The plan takes the CodeRabbit coding plan attached to the issue as input; where this spec decides differently, the section says so.

## Problem

The admin UI has no error reporting and no product analytics. A failure in a component or an unhandled rejection is visible only in the user's console, and nothing tells which pages are used. The issue asks for both Sentry and PostHog in the admin UI, linked so that a Sentry error leads to the PostHog session and user. It quotes the PostHog Vue guide (`posthog.init(token, { api_host: 'https://eu.i.posthog.com', defaults: '2026-05-30' })` in a composable called from the router) and the Sentry link (`Sentry.getCurrentScope().setTag('posthog_session_id', posthog.get_session_id())`).

The quoted setup does not fit the app as is:

- The production CSP is `script-src 'self'`. The default `posthog-js` build injects `https://eu-assets.i.posthog.com/array/<token>/config.js` and, depending on the PostHog project settings, the recorder, surveys, web vitals and other lazy scripts on every load.
- PostHog records the full URL, referrer and session entry URL by default. The app's URLs carry the AuthKit `code` and `state` (`/callback`), `invitation_token` (`/invitations/accept`), filter text (`?q=`) and organization, project, view, key and machine identifiers in the path. Autocapture records the text of clicked elements.
- The issue sets the Sentry tag once; the PostHog session id rotates after 30 minutes of inactivity.
- One build serves every environment (`app-config.json` per deployment), so keys cannot be baked into the source.

## Current State

### Boot

- `app/main.ts`: `isMock = import.meta.env.MODE === 'e2e'` (line 19) starts MSW (lines 37-40). Then `createApp`, `createQueryClient`, `createAppRouter` (lines 42-47), Pinia (line 49), `setBootState(await boot(router))` (line 50), `onSignOut(() => queryClient.clear())` (line 51), Vue Query, `app.use(router)` (line 54, the first navigation), Nuxt UI and `app.mount('#app')` (line 56).
- `boot()` (lines 21-35) loads the config, configures the SDK fetcher and creates the auth. `createAuth` awaits `createClient` of authkit-js, which exchanges `?code` and removes `code` and `state` from the URL (`history.replaceState`) before it resolves.
- `app/boot/app-config.ts`: `AppConfig` is `{ apiBaseUrl, authApiHostname?, environmentName }`. `loadAppConfig` (lines 21-46) reads `/app-config.json`, each field falling back to its `VITE_*` variable through `nonEmpty`. `app/env.d.ts` declares the three variables. `public/app-config.json` is `{}`.
- Vite loads `.env.local` in every mode, including `e2e` (`dev:mock`, `build:e2e`, every Playwright run) and `test` (Vitest).

### Errors

- No `app.config.errorHandler`, `window.onerror` or `unhandledrejection` listener exists; neither Nuxt UI, TanStack Query nor Pinia sets one.
- `app/boot/query.ts`: the query cache reports errors unless `meta.silent` (lines 17-21); the mutation cache reports every error (line 24).
- `app/boot/api-errors.ts`: `shouldReport` (line 22) keeps network failures and `5xx` (`StorytellerError.status === 0 || >= 500`, not aborted) and any non-SDK error except `AbortError`. `reportApiError` (line 30) notifies the listeners; `ApiErrorToaster.vue` is the only one.
- `StorytellerError` (SDK `lib/errors.ts`) has `status`, `hint`, `errorCode`, `schemaErrors`, `body` and `cause`. For status 0 its message contains the request path.

### Router and identity

- Routes are file-based (Vue Router 5). Guards: auth, context, page-memory (`app/router/index.ts`, lines 11-13); page-memory adds `router.afterEach((to, _from, failure) => …)`.
- `route.matched.at(-1).path` is the full template, for example `/:org/:project/:view/key-values/:key`; `router.resolve(path)` gives it for any URL path.
- `app/stores/session.ts`: `user` (`AuthUser` = `{ id, email, name, pictureUrl? }`) is set when the store is first used and by `refreshUser()` (auth guard, login, invitation accept). `signOut()` awaits the `onSignOut` hooks, clears the user and navigates to AuthKit.

### Sensitive surfaces

- Secrets: `components/access/OneTimeSecretDialog.vue` (API key, client secret, certificate password, snippets), `components/access/MachineCreateDialog.vue`, `components/key-values/KeyValueDetail.vue` (revealed value), `KeyValueVersions.vue`, `KeyValueCreateDialog.vue`, `KeyValueImportDialog.vue`.
- Documents: `components/editor/CodeEditor.vue`, `DiffEditor.vue`, `DocumentEditor.vue`, `components/versions/HunkList.vue`.
- Personal data: `components/access/InviteDialog.vue`, `MembersTable.vue`, `InvitationsTable.vue`, `components/shell/UserMenu.vue`, `pages/account.vue`.
- `UModal` teleports its content, so a class on a page ancestor does not reach a dialog; it has to be on the `UModal` (its `class` lands on the dialog content).

### CSP and deployment

- `public/staticwebapp.config.json`, line 15: `connect-src 'self' __API_ORIGIN__ https://api.workos.com https://__AUTH_HOSTNAME__`, `script-src 'self'`, `worker-src 'self'`.
- `.github/workflows/ui-admin.yml`, step "Environment configuration" (lines 146-162): an inline `node -e` script writes `dist/app-config.json` from `vars.UI_ADMIN_*` and replaces `__API_ORIGIN__` and `__AUTH_HOSTNAME__`. Nothing tests it, and Playwright serves `vite preview` without the CSP, so a wrong CSP shows only after deployment.

### SDKs

Verified against the installed packages:

- **posthog-js 1.438.12.** `defaults` accepts `'2026-05-30'` (it turns `capture_pageview` into `'history_change'`). `posthog-js/no-external` is the same core without the script loader: no `<script>` injection, no `eval`, no `Worker`. With `advanced_disable_flags: true` the only request is `POST https://eu.i.posthog.com/e/`. Every event carries `$current_url`, `$pathname`, `$session_entry_url`, `$session_entry_pathname` and `$session_entry_referrer`, and `$identify`'s `$set_once` carries `$initial_*` and session-entry URLs, all with the raw path, query and hash, even with `save_referrer: false`. `before_send(cr) => cr | null` sees `properties`, `$set` and `$set_once`; removing `properties.token` drops the event. `ph-no-capture` on an element or an ancestor drops its autocapture events; `mask_all_element_attributes` drops attributes and classes. `onSessionId(cb)` fires at once and on every rotation. The default persistence `localStorage+cookie` sets a `ph_<token>_posthog` cookie; `localStorage` sets none.
- **@sentry/vue 11.8.0.** `sendDefaultPii` is gone; `dataCollection` replaces it with permissive defaults (IP inference, query strings, headers). `attachProps` defaults to `true` (component props in events). Default integrations include `BrowserSession` (release-health envelopes) and `Console` (raw console arguments as breadcrumbs); no tracing or replay, so no `sentry-trace`/`baggage` headers reach the API. `tracesSampleRate: 0` counts as enabled. `resolveRoute(url)` gives the route template through the router. A DSN `https://<key>@o1.ingest.de.sentry.io/2` posts to `https://o1.ingest.de.sentry.io/api/2/envelope/`. A dynamic `import('@sentry/vue')` (namespace) bundles replay, feedback and a CDN loader (429 KB); a local module with named imports is 114 KB.

## Proposed Changes

### 1. Packages

`package.json` dependencies: `@sentry/vue` `^11.8.0` and `posthog-js` `^1.438.12`, with the lockfile.

### 2. Runtime configuration

`AppConfig` gains four optional fields, read like the others (`nonEmpty(file.x) ?? nonEmpty(env.VITE_X)`):

| `app-config.json` | Variable | Meaning |
| --- | --- | --- |
| `sentryDsn` | `VITE_SENTRY_DSN` | Sentry DSN of the admin UI project; no Sentry without it |
| `posthogProjectToken` | `VITE_POSTHOG_PROJECT_TOKEN` | PostHog project token (`phc_…`); no PostHog without it |
| `posthogHost` | `VITE_POSTHOG_HOST` | PostHog API host; `https://eu.i.posthog.com` when missing |
| `release` | — | written by the deployment (the commit SHA); the Sentry release |

`env.d.ts` declares the three variables. `public/app-config.json` stays `{}` and `.env.development` sets none, so local development, unit tests and Playwright send nothing. `.env.example` lists them commented out.

The CodeRabbit plan does the same; the CLI commits its token, but the UI keeps the one-build-per-environment rule.

### 3. When telemetry runs

- `MODE === 'e2e'` or `MODE === 'test'`: neither SDK is imported or started, whatever the variables (a developer's `.env.local` is loaded in those modes too). The check is static, like `isMock`, so the e2e build drops the modules.
- Otherwise each SDK starts only when its key is set and, for PostHog, when the host is an absolute `https` URL (or `http` on localhost).
- A failure to load or start an SDK is caught and logged with `console.warn`; it never changes the boot state.

### 4. Modules

- `app/boot/telemetry.ts`, the only module the app calls: `startTelemetry(app, router, config)`, `identifyUser(id)`, `resetTelemetry()`, `captureApiError(error)`, `capturePageview()`. Before `startTelemetry` resolves, and when telemetry is off, they do nothing. It loads the two SDK modules below with dynamic `import()`, in parallel, so a deployment without keys downloads neither.
- `app/boot/sentry.ts`: named imports from `@sentry/vue` only (never the namespace, which would pull replay and the CDN loader into the chunk).
- `app/boot/posthog.ts`: `import posthog from 'posthog-js/no-external'`.
- `app/utils/telemetry-privacy.ts`: pure functions, tested alone: `pageTemplate(router, url)`, `scrubPostHogEvent(router)`, `scrubSentryEvent`, `scrubBreadcrumb`.
- `app/main.ts` calls `await startTelemetry(app, router, state.config)` right after `setBootState(await boot(router))` (line 50): the config is known and AuthKit has already removed `code` and `state` from the URL, and it is still before `app.use(router)` and `app.mount`, so Sentry's Vue error handler catches errors of the first render. Sentry starts before PostHog.

### 5. URLs (`telemetry-privacy.ts`)

- `pageTemplate(router, url)`: for a URL of the app's origin (or a path), `origin + router.resolve(pathname).matched.at(-1).path`, for example `https://admin.example.com/:org/:project/:view/key-values/:key`; a path that matches no route gives `/:path(.*)`, the not-found template. Query and hash are always dropped. Another origin gives the origin only.
- Path-style keys (`$pathname`) get the template without the origin.

### 6. PostHog (`posthog.ts`)

``` ts
posthog.init(token, {
  api_host: host,
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
  before_send: scrubPostHogEvent(router),
})
```

- `scrubPostHogEvent` replaces, in `properties`, `$current_url`, `$referrer`, `$pathname`, `$session_entry_url`, `$session_entry_referrer`, `$session_entry_pathname`, `$initial_current_url`, `$initial_referrer`, `$initial_pathname`, `$prev_pageview_pathname` and `$external_click_url` with their template (section 5), and in `$set` and `$set_once` every key matching `/url|path|referr/`. It never touches `properties.token`.
- Every option the PostHog project settings could otherwise switch on is set explicitly, so the project's settings cannot turn on replay, exception capture, web vitals, dead clicks, heatmaps or surveys. Exceptions stay in Sentry only.
- Autocapture keeps clicks on links and buttons with their text (the labels show which action was used) but no attributes, so no `href` with identifiers. Section 9 removes the sensitive areas entirely.
- `persistence: 'localStorage'` sets no cookie. The CodeRabbit plan keeps the default cookie; the app has no consent banner, so it is avoided.
- `respect_dnt` honours Do Not Track and Global Privacy Control.
- Pageviews: `capture_pageview: false`, and a `router.afterEach` guard (`app/router/guards/telemetry.ts`, installed after page-memory) calls `capturePageview()` for the first navigation and for each later successful navigation whose `path` changed (a query-only change, such as typing a filter, is no new page). The event is `$pageview`; `before_send` templates its URL like every other event's.

### 7. Sentry (`sentry.ts`)

``` ts
init({
  app,
  dsn,
  environment: config.environmentName,
  release: config.release,
  attachProps: false,
  enhanceFetchErrorMessages: 'report-only',
  dataCollection: { userInfo: false, cookies: false, httpHeaders: { request: { allow: ['user-agent'] }, response: false }, httpBodies: [], urlQueryParams: false },
  integrations: (defaults) => defaults.filter((integration) => integration.name !== 'BrowserSession' && integration.name !== 'Console'),
  beforeSend: (event) => scrubSentryEvent(event, router, correlation()),
  beforeBreadcrumb: (breadcrumb) => scrubBreadcrumb(breadcrumb, router),
})
```

- Errors only: no `browserTracingIntegration`, no replay, no `tracesSampleRate` (so no trace headers and no change to the API's CORS). `BrowserSession` (release health) and `Console` are removed.
- `scrubSentryEvent`: `request.url` becomes its page template; the `Referer` header and `extra.arguments` are removed; `user` is reduced to `{ id }`; the tags `posthog_session_id` and `posthog_distinct_id` are set from the live PostHog instance on every event. That replaces the issue's one-off tag, which goes stale when the session rotates. The CodeRabbit plan does the same; `posthog.sentryIntegration()` is not used, to keep the two SDKs independent.
- `scrubBreadcrumb`: navigation `from`/`to` become templates; `fetch` and `xhr` `data.url` lose query and hash. API paths keep their organization, project, view and key names: they help to find the failing call and are names, not values.
- API errors: `reportApiError` calls `captureApiError(error)` after `shouldReport`, so the reported set equals the toasts (network and `5xx`; `meta.silent` queries are not reported). A `StorytellerError` becomes `captureMessage('Storyteller API answered <status>' | 'Storyteller API is not reachable', { level: 'error' | 'warning', tags: { api_status, api_error_code, page }, fingerprint: ['storyteller-api', status, errorCode] })`, without message, hint, body or cause; any other error goes to `captureException`. `onApiError` is not used, so nothing is reported twice. The CodeRabbit plan hooks the query cache instead; `reportApiError` is the single place both caches already call.

### 8. Identity

- `startTelemetry` watches `useSessionStore().user?.id` (immediately): a new id calls `identifyUser(id)`, which is `posthog.identify(id)` without person properties and `setUser({ id })`. E-mail, name and picture are never sent.
- `main.ts` registers `onSignOut(() => resetTelemetry())` next to the query cache hook: `posthog.reset()` and `setUser(null)`.
- The id is the AuthKit user id. CLI events are personless with an installation id; the two are not linked.

### 9. Sensitive areas (`ph-no-capture`)

The class goes on the root of: `OneTimeSecretDialog`, `MachineCreateDialog`, `KeyValueCreateDialog` and `KeyValueImportDialog` (on the `UModal`), `KeyValueDetail` and `KeyValueVersions`, `CodeEditor`, `DiffEditor`, `DocumentEditor` and `HunkList`, `InviteDialog` (on the `UModal`), `MembersTable`, `InvitationsTable`, `UserMenu` and `pages/account.vue`. Clicks inside them send no PostHog event. The class changes no style, so the visual baselines stay.

### 10. CSP and deployment

- `public/staticwebapp.config.json`: `connect-src 'self' __API_ORIGIN__ __SENTRY_ORIGIN__ __POSTHOG_ORIGIN__ https://api.workos.com https://__AUTH_HOSTNAME__`. `script-src` and `worker-src` stay: both SDKs are bundled, nothing is injected, and no worker is created.
- The inline script of the deploy step moves to `build/environment-config.ts` (a pure function: variables in, `app-config.json` content and the CSP replacements out) and `build/write-environment-config.ts` (reads `process.env`, writes `dist/app-config.json` and `dist/staticwebapp.config.json`), run by the step with `node build/write-environment-config.ts` (Node 24 runs TypeScript). It keeps today's behaviour and adds:
  - `sentryDsn` from `vars.UI_ADMIN_SENTRY_DSN`, `posthogProjectToken` from `vars.UI_ADMIN_POSTHOG_PROJECT_TOKEN`, `posthogHost` from `vars.UI_ADMIN_POSTHOG_HOST`, `release` from `github.sha`; empty values are left out;
  - `__SENTRY_ORIGIN__` = the origin of the DSN (the key is not part of it), `__POSTHOG_ORIGIN__` = the origin of the host or of `https://eu.i.posthog.com` when only the token is set; an empty string when the SDK is not configured;
  - an error for a DSN or host that is not an absolute `https` URL.
- All three variables are optional: a deployment without them deploys as today.

### 11. Tests

Unit (Vitest, the injection style of `boot.test.ts`; `vi.mock('@sentry/vue')` and `vi.mock('posthog-js/no-external')` for the SDK modules):

- `test/unit/boot/boot.test.ts`: the new `loadAppConfig` fields (file over variable, the PostHog host default).
- `test/unit/utils/telemetry-privacy.test.ts`: `pageTemplate` for `/callback?code=…&state=…`, `/invitations/accept?invitation_token=…`, a key-value path, a machine path, a hash, another origin and an unknown path; `scrubPostHogEvent` on a captured `$pageview` and an `$identify` with `$set_once` (no raw path, query or hash left; `token` kept); `scrubSentryEvent` (URL, `Referer`, `user`, tags); `scrubBreadcrumb` (navigation, fetch).
- `test/unit/boot/telemetry.test.ts`: nothing starts without keys; each SDK starts alone with its key; an SDK that throws on load or init leaves the facade inert and does not throw; the PostHog options include the privacy settings of section 6; Sentry has no `BrowserSession` or `Console`; `identifyUser` sends only the id; `resetTelemetry`; `captureApiError` for 5xx, network and other errors, never with the message or body.
- `test/unit/router/telemetry.test.ts`: a pageview for the first navigation (also to `/`) and per path change, none for a query-only change or a failed navigation.
- `test/unit/boot/api-errors.test.ts` or the `global API errors` block: `reportApiError` calls `captureApiError` only for what `shouldReport` keeps.
- `test/unit/build/environment-config.test.ts`: today's output; the new fields; empty variables; the origins; an invalid DSN or host fails.
- `test/unit/build/csp.test.ts`: `connect-src` has the four placeholders; `script-src` is `'self'`.

E2E and visual: no change. Telemetry is off in the e2e mode, and `ph-no-capture` has no style.

Must pass: `npm run lint`, `npm test`, `npm run build`, `npm run test:e2e`.

### 12. Documentation

- `src/Platform/ui.admin/README.md`: the four rows of section 2 in "Configuration"; the three variables in the "Deployment" table and the two placeholders after it; a "Telemetry" subsection (what is sent, the URL templates, the off switches, the `ph-no-capture` rule for new components that show secrets, documents or personal data); `boot/` in the architecture tree.
- `docs/42for.net/platform/admin-ui.md`, "Running it": one paragraph on error reporting and analytics and what they never contain.

Do not edit this spec to match later deviations; record those in the review.

### Phases

One phase, in two commits: the app (sections 1-9 and their tests), then the CSP, the deployment and the documentation (sections 10 and 12).

### Out of scope

- Uploading source maps to Sentry (`@sentry/vite-plugin`, a `SENTRY_AUTH_TOKEN` secret).
- Creating the Sentry project of the admin UI and setting the `ui-admin` environment variables.
- Session replay, feature flags, surveys and a consent banner.
- A Playwright run under the production CSP.
