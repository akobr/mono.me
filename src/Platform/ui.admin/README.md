# 2S Admin UI

Single-page administration application of the 2S Platform: organizations, projects and views, annotations, configurations, templates and schemas, members, invitations, machines and certificates.

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

npm run dev:mock   # http://localhost:5174, API served by MSW in the browser, fake signed-in user
npm run dev        # http://localhost:5173, real API (VITE_API_BASE_URL) and AuthKit
```

After a change of the SDK, rebuild it and reinstall it here: `rm -rf node_modules/@42for.net && npm install`. The SDK is a file: dependency, so a rebuilt SDK changes neither the lockfile nor the version, and the dev server would keep pre-bundling the old copy (two instances of `@tanstack/vue-query`: `client.defaultQueryOptions is not a function`). `build/sdk-freshness.ts` stamps the installed `dist`: a changed stamp forces the re-optimization at the start, and a running dev server re-optimizes and restarts when the reinstall lands.

The dev server pre-bundles every package imported in `app/` when it starts (`optimizeDeps.entries` in `vite.config.ts`), so moving to another page never re-optimizes the dependencies and reloads the app. A package that only a virtual module imports, like `vue-router/experimental` from the generated routes, goes into `optimizeDeps.include`. `test/unit/build/dependency-prebundling.test.ts` checks both.

| Script | Command |
| --- | --- |
| `dev` | Vite dev server on port 5173 against the API in `.env.development` / `.env.local` |
| `dev:mock` | Vite in the `e2e` mode on port 5174: MSW mock API (`app/mock`) and a fake user |
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

`npm run dev:mock` starts the app with `vite --mode e2e`. `app/main.ts` then starts MSW in the browser with the hand-written handlers of `app/mock/handlers.ts` (a coherent in-memory data set, kept in `sessionStorage`) in front of the Orval-generated Faker handlers, and signs in a fake user. The mock modules are loaded through static `import.meta.env.MODE === 'e2e'` checks and dynamic imports, so production builds do not contain them.

- `?mock-scenario=new-user` starts without an account (onboarding); `?mock-scenario=invited` starts without an account but with a pending invitation; `?mock-scenario=large` puts 16,317 annotations into `acme/billing/default` (performance of the grid and the graph); `?mock-scenario=default` returns to the seeded data.
- `window.storytellerMock.reset()` resets the data of the current scenario.
- The mock API keeps deleted annotations restorable and records a change history per key, like the API (`app/mock/annotation-handlers.ts`, `app/mock/audit.ts`). A re-created key starts without its configuration.
- `POST …/annotations/simple` (bulk create) reads its lines as the API does (`app/mock/simple-lines.ts`): it creates the keys with their missing ancestors, skips existing keys, and answers `400` above 100 lines.

### End-to-end and visual tests

`npm run test:e2e` builds the app in the `e2e` mode, serves it with `vite preview` on port 4174 and runs `test/e2e/*.spec.ts`:

- sign-in and onboarding;
- grid filter, detail edit and save;
- a schema violation saved anyway;
- the conflict dialog;
- a version comparison;
- an invitation;
- a machine with a `.pfx` download;
- a graph node opened by mouse and by keyboard;
- delete and Undo, restore from Deleted (with and without descendants), create over a deleted key, permanent delete for Administrators only, the history and the configuration link;
- bulk create from a list of keys, with the missing ancestors, and its absence for a Reader;
- axe (WCAG 2.1 AA) on the main screens, on Deleted and on the bulk create dialog.

Each test gets a fresh context, so the mock data starts from the scenario's seed. Set `PLAYWRIGHT_CHANNEL=chrome` to use an installed Chrome instead of `npx playwright install chromium`.

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
| `AZURE_STATIC_WEB_APPS_API_TOKEN_UI_ADMIN` | secret | deployment token of the Static Web App |

The deploy step writes `dist/app-config.json` and replaces `__API_ORIGIN__` and `__AUTH_HOSTNAME__` in `dist/staticwebapp.config.json`. That file (from `public/`) sets the following:

- the SPA navigation fallback;
- the headers: CSP, `Referrer-Policy`, `X-Content-Type-Options`;
- caching: `no-cache` by default, a year and `immutable` for the hashed `/assets/*`.

The CSP allows no `eval`. Keep these in mind when adding code:

- **Icons:** they are bundled at build time (the lucide icons used in `app/**/*.{vue,ts}`). An icon name built at runtime from parts is not found, and it would be fetched from the Iconify API, which the CSP blocks.
- **Fonts:** they are never inlined as `data:` URLs.
- **Validation:** JSON Schema validation uses `@cfworker/json-schema`, which interprets schemas, instead of Ajv, which generates code with `new Function`.
- **Zod:** it runs `jitless`.

## Architecture

```
src/
├── index.html, vite.config.ts, vitest.config.ts, eslint.config.ts, .oxlintrc.json, tsconfig*.json
├── build/mock-service-worker.ts   # serves msw's worker script in the e2e mode only
├── public/                        # app-config.json, theme-init.js (color mode before first paint), 16 and 32 px favicons
├── app/
│   ├── main.ts                    # boot: app-config → SDK fetcher → AuthKit → Pinia, Vue Query, router, Nuxt UI
│   ├── App.vue                    # <UApp>, layout switch on route.meta.layout
│   ├── assets/                    # css/main.css (Tailwind, Nuxt UI, fonts, type tokens), images/ (the 42 logo)
│   ├── boot/                      # app-config, auth (AuthKit), api (SDK fetcher), query (QueryClient), api-errors
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

- **Server state** lives in TanStack Query through the SDK's generated composables. **Client state** lives in Pinia: `session` (user, account, AccessMap), `context` (org, project and view of the route), `preferences` (UI state only, in `localStorage`, including the personal date format and personal names, colors and icons of the annotation types), `project-settings` (per project defaults such as the time zone of new annotations, the date format and the names, colors and icons of the types, in `localStorage` until the API stores project settings), `type-appearance` (resolves the type overrides, personal over project over built-in, and applies them to `typeMeta` and the `--st-*` color tokens), `date-format` (resolves the date format, personal over project over `yyyy-mm-dd`, and applies it to the formatters of `utils/dates.ts`), `page-memory` (in memory only: per view, the last location and the URL-less state of the annotations, configurations and graph pages, restored by the `page-memory` router guard when the user returns to the plain list of one of these sections).
- **Errors:** the SDK throws `StorytellerError`. `5xx` and network failures show a global toast; screens handle `4xx` themselves. Server stack traces are never shown.
- **Roles:** `useRole()` and `<RoleGate>` read the caller's role from `Account.AccessMap` (an organization role does not grant project access).
- **Forms:** VeeValidate 4 with Zod 4 through `forms/typed-schema.ts` (`@vee-validate/zod` supports Zod 3 only).
- **Key values** (`pages/[org]/[project]/[view]/key-values*`, `components/key-values/`, `components/secrets/`): the pairs of a view with their levels, the detail with Reveal (masked again after 60 seconds, never cached) and Set new value, versions loaded only on request, import in three steps and export through a Blob URL, the project's Secrets settings (allowed levels, keys and rotation, the BYOK wizard, recipients), the Audit and Usage pages. HYOK values are encrypted in the browser with `@42for.net/2splatform.sdk/hyok` before they leave; `app/mock/key-value-handlers.ts` is the stateful mock behind them. See [key-values.md](../../../docs/Platform/Storyteller/key-values.md#admin-ui).
- **Graph** (`components/graph/`): the d3-force simulation runs in a Web Worker (`simulation.worker.ts`), seeded with a layered layout (`layout-seed.ts`) and streamed as positions. Nodes, links and arrowheads are drawn with WebGL 2 (`webgl-renderer.ts`); names, highlights and rings are drawn on a Canvas 2D overlay. Without WebGL 2, or with `localStorage['2s-admin:graph-renderer'] = 'canvas'`, Canvas 2D draws everything (fine up to a few thousand nodes).
