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

After a change of the SDK, rebuild it and reinstall it here: `rm -rf node_modules/@42for.net && npm install`.

| Script | Command |
| --- | --- |
| `dev` | Vite dev server on port 5173 against the API in `.env.development` / `.env.local` |
| `dev:mock` | Vite in the `e2e` mode on port 5174: MSW mock API (`app/mock`) and a fake user |
| `build` | `vue-tsc --build` and `vite build` → `dist/` |
| `build:e2e` | Build with the mock API, for Playwright |
| `typecheck` | `vue-tsc --build` |
| `lint` / `lint:fix` | oxlint, then ESLint (Vue templates; `eslint-plugin-oxlint` turns off the rules oxlint covers) |
| `format` | Prettier |
| `test` | Vitest (happy-dom) |

`dotnet build` builds `ui.admin.esproj` without Node. Pass `-p:BuildJavaScript=true` to run `npm install` and `npm run build` as well.

### Mock mode

`npm run dev:mock` starts the app with `vite --mode e2e`. `app/main.ts` then starts MSW in the browser with the hand-written handlers of `app/mock/handlers.ts` (a coherent in-memory data set, kept in `sessionStorage`) in front of the Orval-generated Faker handlers, and signs in a fake user. The mock modules are loaded through static `import.meta.env.MODE === 'e2e'` checks and dynamic imports, so production builds do not contain them.

- `?mock-scenario=new-user` starts without an account (onboarding); `?mock-scenario=invited` starts without an account but with a pending invitation; `?mock-scenario=large` puts 16,317 annotations into `acme/billing/default` (performance of the grid and the graph); `?mock-scenario=default` returns to the seeded data.
- `window.storytellerMock.reset()` resets the data of the current scenario.

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

## Architecture

```
src/
├── index.html, vite.config.ts, vitest.config.ts, eslint.config.ts, .oxlintrc.json, tsconfig*.json
├── build/mock-service-worker.ts   # serves msw's worker script in the e2e mode only
├── public/                        # app-config.json, theme-init.js (color mode before first paint), favicon
├── app/
│   ├── main.ts                    # boot: app-config → SDK fetcher → AuthKit → Pinia, Vue Query, router, Nuxt UI
│   ├── App.vue                    # <UApp>, layout switch on route.meta.layout
│   ├── boot/                      # app-config, auth (AuthKit), api (SDK fetcher), query (QueryClient), api-errors
│   ├── router/                    # router + guards (auth: sign-in, account, onboarding; context: org/project/view)
│   ├── layouts/                   # DefaultLayout (dashboard shell), BareLayout (centered card)
│   ├── pages/                     # file-based routes (Vue Router 5)
│   ├── components/                # auto-imported by the Nuxt UI Vite plugin (shell/, common/, orgs/, …)
│   ├── composables/, stores/      # auto-imported (useRole, useSidebar, session, context, preferences, …)
│   ├── forms/                     # Zod schemas and the VeeValidate adapter for Zod 4
│   ├── mock/                      # MSW handlers and fake auth for the e2e mode
│   └── utils/
└── test/unit/                     # Vitest
```

- **Server state** lives in TanStack Query through the SDK's generated composables. **Client state** lives in Pinia: `session` (user, account, AccessMap), `context` (org, project and view of the route), `preferences` (UI state only, in `localStorage`).
- **Errors:** the SDK throws `StorytellerError`. `5xx` and network failures show a global toast; screens handle `4xx` themselves. Server stack traces are never shown.
- **Roles:** `useRole()` and `<RoleGate>` read the caller's role from `Account.AccessMap` (an organization role does not grant project access).
- **Forms:** VeeValidate 4 with Zod 4 through `forms/typed-schema.ts` (`@vee-validate/zod` supports Zod 3 only).
- **Graph** (`components/graph/`): the d3-force simulation runs in a Web Worker (`simulation.worker.ts`), seeded with a layered layout (`layout-seed.ts`) and streamed as positions. Nodes, links and arrowheads are drawn with WebGL 2 (`webgl-renderer.ts`); names, highlights and rings are drawn on a Canvas 2D overlay. Without WebGL 2, or with `localStorage['2s-admin:graph-renderer'] = 'canvas'`, Canvas 2D draws everything (fine up to a few thousand nodes).
