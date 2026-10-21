# 2S Admin UI

Single-page administration application of the 2S Platform: organizations, projects and views, annotations, configurations, templates and schemas, the export and import of a view, the values store, members and invitations, machines, shared certificates and trusted issuers, settings and monitoring.

- **Stack:** Vue 3.5 + TypeScript, Vite, Vue Router 5 (file-based, typed routes), Nuxt UI 4 (through its Vite plugin, no Nuxt), Pinia, TanStack Query for Vue, VeeValidate + Zod 4.
- **API client:** the generated SDK [`@42for.net/2splatform.sdk`](../Storyteller/sdk.typescript/README.md) (fetch functions, Vue Query composables, Zod schemas, MSW mocks).
- **Sign-in:** WorkOS AuthKit (`@workos-inc/authkit-js`).
- **Specs:** [Administration UI for 2S Platform](../../../docs/Platform/ui.admin/specs/2026-10-09%20Administration%20UI%20for%202S%20Platform.md); the phase reviews are in `docs/Platform/ui.admin/specs/reviews/`.

## Getting started

The npm package lives in `src/`. Node 22.18 or newer is required; Node 24 LTS is pinned in `src/.nvmrc`.

```bash
# the SDK is a file: dependency and is installed as a packed copy (src/.npmrc install-links=true)
cd src/Platform/Storyteller/sdk.typescript/src && npm ci && npm run build
cd ../../../ui.admin/src && npm ci

npm run dev:mock   # http://localhost:5174, API served by MSW in the browser, fake signed-in user, showcase data
npm run dev        # http://localhost:5173, real API (VITE_API_BASE_URL) and AuthKit
```

After a change of the SDK, rebuild it and reinstall it here: `rm -rf node_modules/@42for.net && npm install`. The SDK is a file: dependency, so a rebuilt SDK changes neither the lockfile nor the version, and the dev server would keep pre-bundling the old copy (two instances of `@tanstack/vue-query`: `client.defaultQueryOptions is not a function`). `build/sdk-freshness.ts` stamps the installed `dist`: a changed stamp forces the re-optimization at the start, and a running dev server re-optimizes and restarts when the reinstall lands.

The dev server pre-bundles every package imported in `app/` when it starts (`optimizeDeps.entries` in `vite.config.ts`), so moving to another page never re-optimizes the dependencies and reloads the app. A package that only a virtual module imports, like `vue-router/experimental` from the generated routes, goes into `optimizeDeps.include`. `test/unit/build/dependency-prebundling.test.ts` checks both.

| Script | Command |
| --- | --- |
| `dev` | Vite dev server on port 5173 against the API in `.env.development` / `.env.local` |
| `dev:mock` | `build/dev-mock.ts`: Vite in the `e2e` mode on port 5174 with the MSW mock API (`app/mock`), a fake user and the showcase dataset; `-- --legacy`, `-- --large` or `-- --scenario=<name>` pick another (see Mock mode) |
| `dev:mock:legacy` / `dev:mock:large` | `dev:mock` with the previous standard dataset / with the large one |
| `dev:aspire` | Vite without a fixed port; used by the Aspire host, which passes `--port 5173` |
| `build` | `vue-tsc --build` and `vite build` → `dist/` |
| `build:e2e` | Build with the mock API, for Playwright |
| `typecheck` | `vue-tsc --build` |
| `lint` / `lint:fix` | oxlint, then ESLint (Vue templates; `eslint-plugin-oxlint` turns off the rules oxlint covers) |
| `format` | Prettier |
| `test` | Vitest (happy-dom) |
| `test:e2e` | Playwright flows and axe accessibility checks, desktop 1440×900 and mobile 390×844, against the e2e build |
| `test:visual` | Playwright screenshots of key screens (both themes, both viewports); compared only in the Linux container |
| `test:visual:docker` | Runs `test:visual` in the Playwright Docker image; `-- --update` writes new baselines |

`dotnet build` builds `ui.admin.esproj` without Node. Pass `-p:BuildJavaScript=true` to run `npm install` and `npm run build` as well.

### Mock mode

`npm run dev:mock` runs `build/dev-mock.ts`, which starts `vite --mode e2e` on port 5174 and passes the chosen dataset as `VITE_MOCK_DATASET`. `app/main.ts` then starts MSW in the browser with the hand-written handlers of `app/mock/handlers.ts` (a coherent in-memory data set, kept in `sessionStorage`) in front of the Orval-generated Faker handlers, and signs in a fake user, Ada Lovelace. The mock modules are loaded through static `import.meta.env.MODE === 'e2e'` checks and dynamic imports, so production builds do not contain them.

| Option (`npm run dev:mock -- <option>`) | Dataset | Content |
| --- | --- | --- |
| *(none)* | `showcase` | Fourteen projects that model the platform's examples (below) |
| `--legacy` | `default` | The previous standard dataset: `acme/billing`, `acme/shipping`, `northwind/portal`; the tests run on it |
| `--large` | `large` | 16,317 annotations in `acme/billing/default` (performance of the grid and the graph) |
| `--scenario=<name>` | any | `default`, `new-user` (no account, onboarding), `invited` (no account, a pending invitation), `large`, `showcase` |

Other arguments go to Vite (`npm run dev:mock -- --legacy --open`); two dataset options or an unknown name stop with the usage. A page load picks the dataset once (`applyStartScenario` in `app/mock/db.ts`): a valid `?mock-scenario=<name>` first, then the dataset of the launch when it differs from the one the tab last started with (a restart with another option takes effect at the next reload), else the tab's own dataset with its edits. A dataset that starts fresh is stored at once, clears the catalog snapshots in IndexedDB, and, for the showcase, writes the type names, colors, icons, time zone and date format of its projects into the project settings of this browser (`app/mock/showcase/settings.ts`). Changes in Settings stay until the dataset starts fresh again. Every project of every dataset has a description, which the organization overview and Settings show: the three legacy projects have theirs in `app/mock/db.ts`, shared by the `default`, `large`, `new-user` and `invited` scenarios, and each showcase project has its `description`. Organizations have none. Playwright builds with `VITE_MOCK_DATASET=default` (`playwright.config.ts`), and Vitest uses the `default` dataset too.

The showcase (`app/mock/showcase/`) follows the examples of the [documentation](../../../docs/42for.net/platform/examples/index.md) and of the Miro board of 424-77, plus three new ones; the plan is [Showcase Mock Data for the Admin UI](../../../docs/Platform/ui.admin/specs/2026-10-21%20Showcase%20Mock%20Data%20for%20the%20Admin%20UI.md). Every project registers the views `default` (production), `development`, `staging`, `release-candidate` and `feature-delivery1`, and its default view has active, scheduled, expired, disabled and deleted annotations, histories of one, a few and many versions, templates and schemas. Ada is Reader only in `meridian-health/clinical-integrations`:

| Project | Models | Type names (rst, unt, sbt, usg, cnt, exe, uxe) | Ada |
| --- | --- | --- | --- |
| `fabrikam/finance-cloud` | Single-tenant B2B SaaS, about 200 annotations | Application, Job, Customer, Usage, Instance, AppRun, JobRun | Owner |
| `fabrikam/fitness-cloud` | Multi-tenant B2B SaaS with ~550 end customers, about 1,000 | Application, —, Tenant, App enablement, Member, App assignment, — | Administrator |
| `fabrikam/invoicing-suite` | [B2B SaaS](../../../docs/42for.net/platform/examples/b2b-saas.md) | Module, Job, Customer, Contract, Environment, Deployment, JobRun | ContributorWithSecrets |
| `bluefin/money-suite` | [Consumer finance suite](../../../docs/42for.net/platform/examples/b2c-finance.md) | Product, Batch, Person, Entitlement, Legal entity, Product use, BatchRun | Contributor |
| `streamwave/streaming-platform` | [Distributed platform](../../../docs/42for.net/platform/examples/distributed-platform.md), with the view `playback-14` | Service, Pipeline, Market, License, Cell, Deployment, PipelineRun | Administrator |
| `ledgerline/erp` | [Versioned monolith](../../../docs/42for.net/platform/examples/versioned-monolith.md) | Module, Job, Version, Build, Channel, Rollout, JobRun | Owner |
| `aurora-motors/vehicle-platform` | [Physical product](../../../docs/42for.net/platform/examples/physical-product.md) | Subsystem, Firmware, Model, Fitment, Build, Fitted part, Firmware pin | Contributor |
| `harbor-industrial/plant-fleet` | [Device fleet](../../../docs/42for.net/platform/examples/device-fleet.md), with the view `campaign-march` | Capability, Job, Site, Enablement, Device, Assignment, JobRun | ContributorWithSecrets |
| `meridian-health/clinical-integrations` | Hospital network integrations | Clinical system, Interface, Hospital, License, Department, Integration, Feed | Reader |
| `appforge/white-label-app` | White-label mobile app | Feature, Campaign, Brand, Feature license, Platform, App feature, CampaignRun | Contributor |
| `gatherly/conferences` | A conference series and its editions | Event service, Job, Event series, Booking, Edition, Edition service, JobRun | Owner |
| `pixel-forge/live-ops` | Game live operations | Backend service, Live event, Game, Service plan, Platform region, Service deployment, Event run | Administrator |
| `velvet-retail/storefronts` | Multi-brand storefronts | Capability, Feed, Storefront, Subscription, Market, Market setup, Feed run | Contributor |
| `seabreeze-hotels/guest-platform` | Hotel distribution | Module, Sync job, Hotel, License, Channel, Channel setup, Sync run | ContributorWithSecrets |

To add a showcase project, write `app/mock/showcase/projects/<project>.ts` (a `ShowcaseProject`: its `description` of at most 500 characters, members, settings, views and a `build(builder)` that fills each view through the `ScopeBuilder` of `builder.ts`, like `invoicing-suite.ts`), add it to `showcaseProjects` in `app/mock/showcase/index.ts` (and its organization to `showcaseOrganizations`), and add `test/unit/mock/showcase/<project>.test.ts`. `test/unit/mock/showcase.test.ts` checks the rules every project follows: canonical keys with every ancestor, all statuses and deletions, the history lengths, configurations that satisfy their combined schema, the appearance, the views, the roles and the description.

- `window.storytellerMock.reset(scenario?)` resets the data of the current (or the given) dataset, as a fresh start; `window.storytellerMock.addMachine(projectKey, machine)` adds a machine as the Owner of the project would, for what a lower role may do with it.
- The mock API keeps deleted annotations restorable and records a change history per key, like the API (`app/mock/annotation-handlers.ts`, `app/mock/audit.ts`). A re-created key starts without its configuration.
- `POST …/annotations/simple` (bulk create) reads its lines as the API does (`app/mock/simple-lines.ts`): it creates the keys with their missing ancestors, skips existing keys, and answers `400` above 100 lines.
- `GET …/configuration/{key}/composition` (`compositionOf` in `app/mock/configuration-handlers.ts`) follows the mock Effective read, not the server: the ancestors' own documents at depth 1, the template of the key's type, then its own document, merged with `deepMerge` (arrays replaced, `null` overwrites). Its `Content` and hash are those of the mock Effective read, so the mock never warns of a stale view. It has no repeated source and no depth above 1; unit tests serve a fixture shaped like the server's composition of an execution for those.
- `GET …/configuration/{key}/resolved` evaluates `@config("…")` (the configuration being resolved), `@annotation("…"[, "<Type>"])` (the `Values` of the annotation or of its ancestor of that type), each with a JSON Pointer or a dotted JSONPath, and `@<key>/<path>`; sourced values (secrets), interpolation and math stay as written, and anything else unresolved answers `422 BindingFailed`.

### End-to-end and visual tests

`npm run test:e2e` builds the app in the `e2e` mode, serves it with `vite preview` on port 4174 and runs `test/e2e/*.spec.ts`:

- sign-in and onboarding, with the description of the first project on the organization overview;
- grid filter, detail edit and save;
- a schema violation saved anyway;
- the conflict dialog;
- a version comparison, and **Compare views** of two views: the only other view preselected, and never a view compared with itself, nor any request for it;
- invitations from the top bar of the project and organization Members pages, to addresses typed with spaces and commas or pasted as a list, and axe on the invite dialog with an invalid address;
- a machine with a `.pfx` download;
- a machine with a management scope, created by an Owner and shown after its scope in the list and on its detail, and the management scopes disabled for a Contributor, who also gets no Reset and no Edit rules on such a machine (added by its Owner through `window.storytellerMock.addMachine`), but deletes it;
- a trusted issuer from a template (what the templates fill in), and one deleted: its rules flagged on the machine detail, and New machine's link to the section, which keeps the focus;
- a federated machine created by a Contributor (its setup instead of a secret, no Reset) and its rules edited on the detail, where an audience other than the token endpoint is flagged and kept by new rules;
- a graph node opened by mouse and by keyboard;
- delete and Undo, restore from Deleted (with and without descendants), create over a deleted key, permanent delete for Administrators only, the history and the configuration link;
- bulk create from a list of keys, with the missing ancestors, and its absence for a Reader;
- a new configuration from **New** on Configurations, which offers only the annotations without one, and its absence for a Reader;
- the **Composition** view of the Effective tab: the step of each line in the real gutter (a usage value from the usage step, `mode` from the template step), a step selected from the list, which highlights its lines, scrolls to them and keeps the focus, a click on a marker, the status line, the Effective document read again before the hashes are compared, and **Document** dropping `?show=composition`;
- Values store kept by the view and project switchers, without the open key and the selection;
- the project pages of the menu per role, the redirects of the merged pages to their sections, and the theme in the command palette;
- Modeling: the entry of the View section per role, locked for a Reader; its tabs by click, by direct link and by a link to a detail; **Compare views** enabled with another view and disabled without, and the view switcher keeping it with the other view selected; **Compare** on the Views page disabled on the row of the default view, with its tooltip, and following the link of another view; the four tabs of a Contributor, without Export & Import, also in the command palette; the redirects of the old view pages to their tabs, with the query; "Contributors only" without tabs for a Reader on every Modeling URL; the "Discard changes?" prompt of a dirty template when a tab is clicked (**Keep editing** and **Discard**); and the return from the menu to the last tab, detail and query of Modeling, and to **Schemas** when Modeling is selected again;
- the Export & Import tab from the sidebar (**Modeling**, then **Export & Import**) and with `g e`: an export of some kinds, a git export, a check of an edited archive with its result per kind and **Import now**, an import with **Skip** shown again after a reload, the Modeling lock of a Reader, and "Administrators only" under the four other tabs for a Contributor;
- the `prod` badge of the default view in the menu, the switcher and its list of views, and its absence on another view;
- the description of a project on Settings: an Administrator saves it, sees it again after a reload and on the organization overview, and clears it; a Reader reads it without the form;
- the showcase dataset: every project on `/orgs` with Ada as a project member of four organizations, the descriptions on the overview of an organization, the type names of two projects in the grid, the five environment views with the default one as production, the seeded deletions in Deleted, and the read-only `clinical-integrations` (no New, the Modeling lock);
- axe (WCAG 2.1 AA) on the main screens, on the grid of a showcase project, Compare views, the Views page of a project with one view and the locked Modeling page of a Reader among them, on the locked Export & Import page of a Contributor, on Deleted, on the bulk create dialog, on the result of an import, on the new configuration dialog, on the Composition view of a configuration, on the new machine dialog of an Owner (a management scope picked) and of a Contributor (the management scopes disabled), and on the dialogs of trusted issuers and federated machines.

Each test gets a fresh context, so the mock data starts from the scenario's seed. The suites run on the `default` dataset: `openApp` sets `?mock-scenario=default` unless a test asks for another, and the e2e build gets `VITE_MOCK_DATASET=default`. Set `PLAYWRIGHT_CHANNEL=chrome` to use an installed Chrome instead of `npx playwright install chromium`.

Fonts render differently per platform, so the visual baselines in `test/e2e/__screenshots__` come from the Playwright Linux image. `npm run test:visual:docker` (Docker required) copies the SDK and the app into the container, installs, and compares. Add `-- --update` after an intended UI change, and review the new PNGs. CI runs both suites in the same image.

### Aspire

`dotnet run --project src/Platform/Aspire.Host/src` starts the `ui-admin` resource:

- It runs `npm run dev:aspire` on port 5173 (the WorkOS redirect URI).
- It sets `VITE_API_BASE_URL` to the `api-functions` endpoint.
- It does not install packages. Run `npm ci` here, and build the SDK, once before the first start.

## Configuration

One build serves every environment. At start-up the app reads `/app-config.json` (written by the deployment) and falls back to the `VITE_*` variables:

| `app-config.json` | Variable | Meaning |
| --- | --- | --- |
| `apiBaseUrl` | `VITE_API_BASE_URL` | API root including `/api`, for example `https://api.2s.42for.net/api`. A relative value means the app's origin. |
| `authApiHostname` | `VITE_AUTH_API_HOSTNAME` | Custom AuthKit domain, for example `auth.42for.net`. **Required on every host except localhost**: without it AuthKit keeps the refresh token in `localStorage` (devMode), so the app refuses to start. |
| `environmentName` | `VITE_ENVIRONMENT_NAME` | Shown in the sidebar footer. |
| `sentryDsn` | `VITE_SENTRY_DSN` | Sentry DSN of the admin UI project; no Sentry without it. |
| `posthogProjectToken` | `VITE_POSTHOG_PROJECT_TOKEN` | PostHog project token (`phc_…`); no PostHog without it. |
| `posthogHost` | `VITE_POSTHOG_HOST` | PostHog API host; `https://eu.i.posthog.com` when missing. |
| `release` | — | The commit SHA, written by the deployment; the Sentry release. |

The sign-in provider and the AuthKit client ID come from the API (`GET v1/auth/configuration`). A deployment that uses another provider shows an "unsupported" page.

### WorkOS dashboard checklist

- Redirect URIs: `http://localhost:5173/callback` and `https://<admin-host>/callback`.
- Initiate login URI: `https://<admin-host>/login`.
- Sign-out redirect: `https://<admin-host>/login` (and `http://localhost:5173/login`).
- CORS allowed origins: `http://localhost:5173` and `https://<admin-host>`.
- JWT template with `aud`, `email`, `name` and `email_verified` (see `docs/Platform/Storyteller/authentication.md`).
- Invitation accept URL: `https://<admin-host>/invitations/accept`.
- A custom authentication domain (`auth.42for.net`) for every deployed environment.

The admin origin must also be in the CORS settings of the Function App.

### Deployment

`.github/workflows/ui-admin.yml` covers SDK and UI changes. On pull requests and on `main`, it runs these steps:

- `npm ci`, and a check that the generated SDK matches the OpenAPI document;
- lint, unit tests and the build;
- the Playwright suites in the Playwright image.

On `main` it also deploys `dist/` to Azure Static Web Apps (`skip_app_build`). The `ui-admin` GitHub environment provides:

| Name | Kind | Value |
| --- | --- | --- |
| `UI_ADMIN_API_BASE_URL` | variable | `apiBaseUrl`; its origin goes into the CSP `connect-src` |
| `UI_ADMIN_AUTH_API_HOSTNAME` | variable | `authApiHostname`; goes into `connect-src` and `form-action` |
| `UI_ADMIN_ENVIRONMENT_NAME` | variable | optional `environmentName` |
| `UI_ADMIN_SENTRY_DSN` | variable | optional `sentryDsn`; its origin, without the key, goes into `connect-src` |
| `UI_ADMIN_POSTHOG_PROJECT_TOKEN` | variable | optional `posthogProjectToken`; puts the PostHog host into `connect-src` |
| `UI_ADMIN_POSTHOG_HOST` | variable | optional `posthogHost`; its origin goes into `connect-src` (`https://eu.i.posthog.com` when missing) |
| `AZURE_STATIC_WEB_APPS_API_TOKEN_UI_ADMIN` | secret | deployment token of the Static Web App |

The deploy step runs `node build/write-environment-config.ts` (Node 24 runs TypeScript). It writes `dist/app-config.json`, with `release` set to the commit SHA, and replaces `__API_ORIGIN__`, `__AUTH_HOSTNAME__`, `__SENTRY_ORIGIN__` and `__POSTHOG_ORIGIN__` in `dist/staticwebapp.config.json`. Without a DSN or a PostHog token, the Sentry or PostHog origin is empty, and a deployment without the three variables deploys as before. A DSN or host that is not an absolute `https` URL fails the step. Playwright serves `vite preview` without the CSP, so `test/unit/build/csp.test.ts` and `environment-config.test.ts` check the policy and the step, and `write-environment-config.test.ts` runs the script with Node, as the deploy step does: syntax that Node's type stripping rejects, such as an `enum`, fails `npm test` instead of the deployment. The config file (from `public/`) sets the following:

- the SPA navigation fallback;
- the headers: CSP, `Referrer-Policy`, `X-Content-Type-Options`;
- caching: `no-cache` by default, a year and `immutable` for the hashed `/assets/*`.

The CSP allows no `eval`. Keep these in mind when adding code:

- **Icons:** they are bundled at build time (the lucide icons used in `app/**/*.{vue,ts}`). An icon name built at runtime from parts is not found, and it would be fetched from the Iconify API, which the CSP blocks.
- **Fonts:** they are never inlined as `data:` URLs.
- **Validation:** JSON Schema validation uses `@cfworker/json-schema`, which interprets schemas, instead of Ajv, which generates code with `new Function`.
- **Zod:** it runs `jitless`.
- **Telemetry:** PostHog comes from `posthog-js/no-external`, which loads no script from PostHog. The default `posthog-js` injects its config, recorder and surveys from a CDN, which `script-src 'self'` blocks.

### Telemetry

Sentry reports errors and PostHog counts pages and clicks. Each starts only when the deployment sets its key (`sentryDsn`, `posthogProjectToken`). `boot/telemetry.ts`, the only module the app calls, loads the SDKs with dynamic imports, so a deployment without keys downloads neither. A failure to load or start an SDK is logged with `console.warn` and changes nothing else.

- **Off:** in the `e2e` and `test` modes (mock mode, Playwright, Vitest) neither SDK is imported, whatever the variables say, because Vite loads `.env.local` in these modes too. `public/app-config.json` and `.env.development` set no keys, so local development sends nothing.
- **URLs:** a page goes out as its route template, for example `https://<admin-host>/:org/:project/:view/key-values/:key`, without the query and the hash (`utils/telemetry-privacy.ts`). The AuthKit `code` and `state`, the `invitation_token` and the filter text stay in the browser, and no page URL names an organization, project, view or key. The names still leave the browser in two ways: a PostHog click keeps the text of its link or button, which is a name on the organizations page, in the context switcher, in the breadcrumb of an annotation and in the lists; and Sentry's request breadcrumbs keep the paths (without the query) of the API calls before the error, which name their organization, project, view and key.
- **Identity:** both get the AuthKit user id, never the e-mail, the name or the picture. Sign-out resets both.
- **Sentry** (`boot/sentry.ts`) gets errors only, with `environmentName` and `release`: Vue errors, unhandled errors and rejections, and the API errors of the global toast (network failures and `5xx`). An API error is sent with its status and error code, never with its message, hint or body. There is no tracing (no trace headers to the API), no replay, no release-health session and no component prop. There are no console, click or key-press breadcrumbs (Sentry writes the `aria-label`, `title` and `alt` of the clicked element into them, which can be a name or an e-mail) and no breadcrumbs of PostHog's own requests, and the `Referer` header is removed. Each event carries the `posthog_session_id` and `posthog_distinct_id` tags of the current PostHog session, so an error leads to the PostHog session and user.
- **PostHog** (`boot/posthog.ts`) gets a `$pageview` for the first page and for each change of the path (a change of the query only, such as typing a filter, is no new page), and the clicks on links and buttons, with their text (which can be an organization, project, view, annotation or key name) but no attributes. Recordings, exceptions, web vitals, heatmaps, dead clicks, surveys and feature flags stay off, whatever the project settings say. PostHog keeps its state in `localStorage` (no cookie), saves no referrer or campaign parameters (the `utm_*`, `gclid` and similar values and the search keyword it still reads from the entry URL are removed before sending), and honours Do Not Track and Global Privacy Control.
- **`ph-no-capture`:** clicks inside an element with this class send nothing to PostHog. It is on the root of every component that shows secrets (one-time secrets, new machines, shared certificates, key values and their versions, the key-value list on narrow screens, the create and import dialogs of key values), documents (the code, diff and document editors, the configuration form, the hunk list), personal data (the invite dialog, members, invitations, the invitations page, the user menu, the account page, the author and actor links of `AuditActorLink` and the stamps of an annotation) or typed text (the active terms of the filter sheet). A new component that shows secrets, documents or personal data gets it too. A dialog gets it on its `UModal`, which teleports its content out of the page.

## Architecture

```
src/
├── index.html, vite.config.ts, vitest.config.ts, eslint.config.ts, .oxlintrc.json, tsconfig*.json
├── build/                         # mock-service-worker (msw's worker script in the e2e mode only), sdk-freshness,
│                                  #   environment-config + write-environment-config (the deploy step)
├── public/                        # app-config.json, theme-init.js (color mode before first paint), 16 and 32 px favicons
├── app/
│   ├── main.ts                    # boot: app-config → SDK fetcher → AuthKit → telemetry → Pinia, Vue Query, router,
│   │                              #   Nuxt UI
│   ├── App.vue                    # <UApp>, layout switch on route.meta.layout
│   ├── assets/                    # css/main.css (Tailwind, Nuxt UI theme with the purple accent, fonts, type
│   │                              #   tokens), images/ (the 42 logo)
│   ├── boot/                      # app-config, auth (AuthKit), api (SDK fetcher), query (QueryClient), api-errors,
│   │                              #   telemetry, sentry and posthog (the SDKs, loaded only when configured)
│   ├── router/                    # router + guards (auth: sign-in, account, onboarding; context: org/project/view;
│   │                              #   page-memory: back to where a section was left)
│   ├── layouts/                   # DefaultLayout (dashboard shell), BareLayout (centered card)
│   ├── pages/                     # file-based routes (Vue Router 5)
│   ├── components/                # auto-imported by the Nuxt UI Vite plugin (shell/, common/, orgs/, …)
│   ├── composables/, stores/      # auto-imported (useRole, useSidebar, session, context, preferences, …)
│   ├── forms/                     # Zod schemas and the VeeValidate adapter for Zod 4
│   ├── mock/                      # MSW handlers and fake auth for the e2e mode
│   └── utils/
└── test/unit/                     # Vitest
```

- **Server state** lives in TanStack Query through the SDK's generated composables. **Client state** lives in Pinia: `session` (user, account, AccessMap), `context` (org, project and view of the route), `preferences` (UI state only, in `localStorage`, including the personal date format and personal names, colors and icons of the annotation types), `project-settings` (per project defaults such as the time zone of new annotations, the date format and the names, colors and icons of the types, in `localStorage` until the API stores project settings), `type-appearance` (resolves the type overrides, personal over project over built-in, and applies them to `typeMeta` and the `--st-*` color tokens), `date-format` (resolves the date format, personal over project over `yyyy-mm-dd`, and applies it to the formatters of `utils/dates.ts`), `page-memory` (in memory only: per view, the last location and the URL-less state of the annotations, configurations and graph pages, and the last tab, detail and query of Modeling, restored by the `page-memory` router guard when the user returns to the plain list of one of these sections).
- **Merged pages:** the old Invitations, Certificates, Secrets, Audit and Usage pages of a project are redirect stubs (`definePage({ redirect })`) to the section that took them over: `members#invitations`, `machines#shared-certificates`, `settings#secrets` and `monitor` (`?tab=audit`). The router does not scroll to anchors, and the panel body is the scroll container, so `useHashScroll(ready)` scrolls the section of the hash into view once the content above it has loaded. The old Schemas, Templates, Export & Import, Deleted and Compare views pages of a view redirect to their tab, `modeling/{tab}`: `movedToModeling` (`utils/modeling.ts`) inserts `modeling` after the view and keeps the rest of the path, and `schemas/[[...path]].vue` and `templates/[[...path]].vue` cover the details below them too. The redirects return a path, so the query and the hash stay.
- **Errors:** the SDK throws `StorytellerError`. `5xx` and network failures show a global toast, and `reportApiError` sends the same errors to Sentry when it is configured ([Telemetry](#telemetry)); screens handle `4xx` themselves. Server stack traces are never shown.
- **Roles:** `useRole()` and `<RoleGate>` read the caller's role from `Account.AccessMap` (an organization role does not grant project access).
- **Forms:** VeeValidate 4 with Zod 4 through `forms/typed-schema.ts` (`@vee-validate/zod` supports Zod 3 only).
- **Values store** (`pages/[org]/[project]/[view]/key-values*`, `components/key-values/`, `components/secrets/`, `components/monitor/`): the key values of a view with their levels, the detail with Reveal (masked again after 60 seconds, never cached) and Set new value, versions loaded only on request, import in three steps and export through a Blob URL, the Secrets section of the project's Settings (allowed levels, keys and rotation, the BYOK wizard, recipients), and the Usage and Audit tabs of the Monitor page (`pages/[org]/[project]/monitor.vue`, `?tab=audit`). HYOK values are encrypted in the browser with `@42for.net/2splatform.sdk/hyok` before they leave; `app/mock/key-value-handlers.ts` is the stateful mock behind them. `utils/context-paths.ts` lists `key-values` among the view sections, so switching the view or the project keeps Values store, without the open key (424-68). Page memory leaves it out. The page stays mounted through such a switch, so the list clears its selection, and Export its request for the secret values, when the project or the view changes. See [key-values.md](../../../docs/Platform/Storyteller/key-values.md#admin-ui).
- **Modeling** (`pages/[org]/[project]/[view]/modeling.vue`, `pages/[org]/[project]/[view]/modeling/`, `components/modeling/ModelingHeader.vue`, `utils/modeling.ts`): the Schemas, Templates, Export & Import, Deleted and Compare views pages of a view, a tab each, at `modeling/{tab}`; `modeling/index.vue` redirects to `modeling/schemas`. From outside the Modeling of the view (another section, a project page or another view), the `page-memory` guard then returns to its last Modeling location; it reads the plain `modeling` from `to.redirectedFrom`, because the redirect runs before any guard, and it lets the next pass of its own redirect through by the identity of the navigation, not by comparing URLs, which the browser and the router may spell differently. From within it, the plain `modeling` stays on Schemas, which is how the user starts over. The tab is in the path, not in `?tab=` as on Monitor: the schema and template details keep their own `?tab=versions`, and `useUnsavedChanges` asks only when the path changes, once per navigation, also when a guard redirects it (the leave guards run again on the redirected pass). Below Contributor, `modeling.vue` shows the lock ("Contributors only") and mounts no tab; from Contributor up it is a bare `<RouterView />`, so each tab page keeps its panel and its detail outlet as children of the dashboard group. `ModelingHeader` is the navbar and the tabs in the header of each tab page, with manual activation, because every activation is a navigation. The header of the opened page scrolls its tab into view (the tabs scroll sideways on phones) and takes the focus over when the tabs had it. It shares the views query of `ContextSwitcher` and disables **Compare views** once the views are known and none of them is another view. `compare.vue` compares `?to=` with the view of its route, not of the context store: a `?to=` that names the page's own view is no target, so the only other view is preselected over it. The context guard sets the store before a navigation lands, so until the store and the route agree again the page neither preselects (a `router.replace` would cancel the navigation) nor requests the diff and the two documents. A tab with a `minRole` in `utils/modeling.ts` (Export & Import, `Administrator`) is left out below that role by `modelingTabsFor`, in the header and in the command palette. `utils/modeling.ts` also feeds the Modeling entry of the menu (`editRole` and `lockedHint` for the lock, `tabs` for the command palette) and `utils/context-paths.ts`, where `modeling` is a view section and switching the view or the project keeps the tab.
- **Export & Import** (`pages/[org]/[project]/[view]/modeling/export-import.vue`, `components/transfer/`, `stores/transfers.ts`): the Export section (the kinds, a folder of JSON files or a git repository, the download when the job is ready), the Import section (a zip, the kinds, Overwrite or Skip, force, Check and Import), and the result of the view's latest import (the counts per kind, filter chips, a table or a list with links to the documents, **Import now** after a check). The export and the import need the Administrator role, as in the API (`useCan('transfer')`): below it the page shows "Administrators only" under the four other tabs of Modeling and mounts none of the sections, and `app/mock/transfer-handlers.ts` answers `403` (the seed has no Administrator project, so Owner stands in for it in e2e). The store follows the jobs outside the page: after a reload, pending exports are resumed and the latest import of each view is read again. `TransferToaster` reports the ends and refreshes the queries of the kinds an import wrote; `app/mock/transfer-handlers.ts` is the stateful mock. See [export-import.md](../../../docs/Platform/Storyteller/export-import.md).
- **Composition** (`components/configuration/ConfigurationComposition.vue`, `composables/useConfigurationComposition.ts`, `utils/composition-lines.ts`, `components/editor/setup/composition.ts`): the blame view of the Effective tab, behind its `Document | Composition` toggle (`?show=composition`; the configurations list reads `mode` already). The query runs only while the view is shown, with `staleTime: 0` and a refetch on focus; it is not part of the eager `useConfiguration`. `compositionLines` writes the JSON text itself, identical to `toJson`, and looks up the step of each line by pointer, because `JSON.parse` moves integer-like keys first. The CodeMirror extension draws the step of each line in a gutter (full-contrast number, the type color as a tint only, `T` on template steps) and the lines of the selected step; the gutter is `aria-hidden`, so a polite status line names the step of the cursor's line, and every step lists the paths it set. Selecting a step scrolls without moving the focus. When the composition's hash differs from the Effective one, the view reads the Effective document again once and warns only if they still differ; **Refresh** invalidates the configuration's path. See [inheritance.md](../../../docs/Platform/Storyteller/inheritance.md#composition-read).
- **Graph** (`components/graph/`): the d3-force simulation runs in a Web Worker (`simulation.worker.ts`), seeded with a layered layout (`layout-seed.ts`) and streamed as positions. Nodes, links and arrowheads are drawn with WebGL 2 (`webgl-renderer.ts`); names, highlights and rings are drawn on a Canvas 2D overlay. Without WebGL 2, or with `localStorage['2s-admin:graph-renderer'] = 'canvas'`, Canvas 2D draws everything (fine up to a few thousand nodes).
