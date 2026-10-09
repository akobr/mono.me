# Phase B of Administration UI

## Overview

Phase B is the scaffold of the admin SPA from [Administration UI for 2S Platform](../2026-10-09%20Administration%20UI%20for%202S%20Platform.md) (section 19, phase B). It builds on the TypeScript SDK of phase A ([review](../../../Storyteller/specs/reviews/2026-10-10%20Phase%20A%20of%20TypeScript%20SDK.md)).

- **New package:** `src/Platform/ui.admin/src`, built with Vite 8, Vue 3.5, Vue Router 5 file-based routes, Nuxt UI 4 through its Vite plugin, Pinia, TanStack Query and VeeValidate with Zod 4.
- **Boot sequence:** the runtime `app-config.json`, then the SDK fetcher, then AuthKit.
- **Routing:** route guards for sign-in, the account and the organization/project/view context.
- **Dashboard shell:** sidebar with pin, collapse and resize, navbar, context switcher, theme toggle, user menu, command palette and shortcut help.
- **Pages:** onboarding; organizations and projects; the account page.
- **Mock mode:** `npm run dev:mock` runs the whole app against an MSW mock API.

## What Was Done

### 1. Projects and solution integration

- **Package:** `src/Platform/ui.admin/src/package.json`, named `@42for.net/2splatform.ui.admin`, private, `type: module`, Node `>=22.18`, `.nvmrc` 24.
- **Scripts:** the spec's scripts, plus:
  - `dev:mock`: `vite --mode e2e`, port 5174;
  - `build:e2e`.

  `test:e2e` comes with the Playwright suite in phase H.
- **Versions:**

  | Area | Packages |
  | --- | --- |
  | Framework | Vue 3.5.43, Vite 8.3, Vue Router 5.4 |
  | UI | Nuxt UI 4.11.3, Tailwind 4.3 |
  | State and data | Pinia 4.0, TanStack Vue Query 5.104 |
  | Forms | VeeValidate 4.15, Zod 4.6 |
  | Language and types | TypeScript 6.0, vue-tsc 3.3 |
  | Tests | Vitest 4.1, happy-dom 20, @vue/test-utils 2.5, MSW 2.15, Playwright 1.64 |
  | Lint | oxlint 1.87, ESLint 10, eslint-plugin-vue 10, @vue/eslint-config-typescript 14, eslint-plugin-oxlint 1.87 |

  TypeScript stays on 6.0, because typescript-eslint 8.71 requires `<6.1`.
- **SDK dependency:** `file:../../Storyteller/sdk.typescript/src`. **`.npmrc` sets `install-links=true`**, so npm installs a packed copy instead of a symlink. With a symlink, the SDK's `dist` would resolve `vue` and `@tanstack/vue-query` from the SDK's own `node_modules`. That gives two Vue Query instances at runtime (the injection key mismatches) and two `QueryClient` declarations for TypeScript. After an SDK change, `rm -rf node_modules/@42for.net && npm install` refreshes the copy (README).
- **Lockfile:** `package-lock.json` was created with npm 11 (`npx npm@11 install`), which avoids the npm 10.9.2 resolution crash noted in phase A.
- **Config files:**
  - `vite.config.ts` follows section 1. `components` and `autoImport` write their declarations to `app/components.d.ts` and `app/auto-imports.d.ts`; both are committed together with `app/typed-router.d.ts`, so `vue-tsc` works on a clean checkout.
  - `icon.clientBundle.scan: true` bundles the lucide icons used in the source (`@iconify-json/lucide`), so no request goes to the Iconify API, as the CSP `connect-src` requires.
  - Vue DevTools runs in the `development` mode only.
  - `tsconfig.app.json` sets `strict`, `noUncheckedIndexedAccess`, `noUnusedLocals`, `rootDir` (needed by the typed-router Volar plugin) and the two Vue Router Volar plugins. `tsconfig.vitest.json` adds the tests, and `tsconfig.node.json` covers the config files.
  - `eslint.config.ts` uses `defineConfigWithVueTs` with `flat/recommended`, a few formatting rules turned off for Prettier, and `buildFromOxlintConfigFile('.oxlintrc.json')` last.
  - `.oxlintrc.json` uses the typescript, unicorn, oxc and vue plugins. `unicorn/consistent-function-scoping` is off, because Vue setup closures trip it.
  - `.prettierrc.json`: no semicolons, single quotes, width 120, the same style as the SDK.
- **`ui.admin.esproj`:** pinned to `Microsoft.VisualStudio.JavaScript.Sdk/1.0.6887863`, the same version and the same `TargetPlatformIdentifier`/`TargetPlatformMoniker` fix (NU1012) as the SDK esproj. `JavaScriptTestFramework` is `Vitest`. `dotnet build` of the esproj succeeds without Node.
- **Solution, mrepo and versioning:**
  - `42.mono.slnx`: the esproj is added under `/Platform/`.
  - `mrepo.json`: item `src/Platform/ui.admin`, "2S admin UI", with the scripts `install`, `build`, `run`, `test` and `lint` (`npm --prefix src …`).
  - `src/Platform/ui.admin/version.json`: `0.1-alpha`, inherit.
- **Environment files:**
  - `.env.development` (API `http://localhost:7071/api`), `.env.e2e` (API `/api`) and `.env.example` are committed.
  - `.env.e2e` needs a `!.env.e2e` in the package's `.gitignore`, because the root `.gitignore` ignores `*.e2e`.

### 2. Application structure

`app/` follows section 2:
- `boot/`, `router/` (with `guards/`), `layouts/`, `pages/`, `components/shell|common|orgs`, `composables/`, `stores/`, `forms/`, `utils/` and `mock/`.
- `queries/` arrives with the catalog in phase C.

Components, composables and stores are auto-imported by the Nuxt UI Vite plugin. The SDK and everything else is imported explicitly.

### 3. Authentication and session

- **`boot/app-config.ts`:** `loadAppConfig` reads `/app-config.json` (`no-store`). It falls back to `VITE_API_BASE_URL`, `VITE_AUTH_API_HOSTNAME` and `VITE_ENVIRONMENT_NAME`, and fails with `AppConfigError` when there is no API address. A relative `apiBaseUrl` (`/api`) is resolved against the origin. That is used by the mock mode and also allows a reverse proxy.
- **`boot/auth.ts`:**
  - `createAuth` loads `GET v1/auth/configuration` (cached in `sessionStorage`). It returns `unsupported` for providers other than AuthKit, and `error` when a non-localhost host has no `authApiHostname`.
  - It creates the authkit-js client with `apiHostname`, `devMode` (localhost only) and `redirectUri: /callback`.
  - The redirect's `state.returnTo` goes through `safeReturnTo` (same-origin paths only), as the authkit-js documentation asks.
  - `AuthService.getAccessToken` returns `undefined` on `LoginRequiredError`.
  - `signIn` passes `invitationToken` and `loginHint`. authkit-js 0.20 has `invitationToken` in its `RedirectOptions`, which settles the open question of section 13.
- **Boot state:** `boot/state.ts` holds the result (`ready | unsupported | error`).
  - `main.ts` runs the sequence of section 3: config, then `configureApi` (the SDK fetcher with `onUnauthorized` → sign-in), then auth, then Pinia, Vue Query, the router and Nuxt UI.
  - Failures do not stop the mount: the auth guard routes every page to `/unsupported`, which shows the reason and a retry button.
- **SDK changes (phase A follow-up):**
  - `StorytellerConfig.getAccessToken` now receives `{ forceRefresh }`, which is `true` for the one retry after a `401`. The UI passes it to `authkit.getAccessToken({ forceRefresh })`, so the retry really uses a new token.
  - `StorytellerError` has `aborted` and an `isAborted()` guard, so cancelled queries are never reported as failures.
  - The SDK tests and README were updated.
- **`stores/session.ts`:**
  - `user`, `account`, `accountStatus` (`unknown | loading | ready | missing | error`) and `accessMap`.
  - `loadAccount(force)` shares one request between concurrent calls and maps `404` to `missing`.
  - `signIn(returnTo)`.
  - `signOut()` runs the registered `onSignOut` hooks (the query cache is cleared by `main.ts`), then signs out to `/login`.
- **Route guards:**
  - **`router/guards/auth.ts`:**
    - Public routes (`meta.public`) always pass.
    - A signed-out user is sent to AuthKit with `returnTo = to.fullPath`.
    - The account is loaded once. `missing` goes to `/onboarding`, unless the route has `meta.allowWithoutAccount`.
    - A user with an account is redirected away from `/onboarding`.
    - The "pending invitations → `/invitations`" branch is added in phase F together with the invitation pages.
  - **`router/guards/context.ts`:**
    - It reads `:org`, `:project` and `:view`.
    - A project needs a role above `None` on the exact `{org}.{project}` key. An organization page needs any membership in the organization, because a member of one project may open the organization overview and see their projects.
    - It sets the `context` store (`access: ok | forbidden`) and stores `preferences.lastContext`.
    - `DefaultLayout` renders the 403 state instead of the page.
- **Pages:**
  - `login.vue` (with `?error=` / `error_description`), `callback.vue` (sends a failed exchange back to `/login` instead of looping), `unsupported.vue`, `onboarding.vue` (`CreateAccount`) and `index.vue` (last context, or `/orgs`).
  - Route meta (`public`, `layout`, `allowWithoutAccount`) is declared with `definePage` and typed in `router/meta.d.ts`.

### 4. Shell, layout and theming

- **Layouts:**
  - `App.vue` holds `<UApp>`, the layout switch on `route.meta.layout`, and the toaster for global API errors (`ApiErrorToaster`).
  - It renders nothing before the first navigation resolves, so the shell does not flash on `/login`.
  - The Vue Query devtools are loaded asynchronously in the development mode only.
- **`DefaultLayout`:** `UDashboardGroup` (`storage: local`, `storage-key: 2s-admin`, unit `%`) with `AppSidebar`, the page's `UDashboardPanel` and the command palette. It shows an account error state with retry, or the 403 state.
- **Pin, collapse and hamburger:**
  - **Pinned** (the default): `AppSidebar` is a `UDashboardSidebar` that is `collapsible` and `resizable` (12–24 %), with `mode="slideover"` below `lg`.
  - **Unpinned:** the sidebar is replaced by a left `USlideover` with the same navigation.
  - The ☰ button in `AppNavbar` opens the slideover. It is always shown when the sidebar is unpinned, and only below `lg` when it is pinned.
  - The 📌 button sits in `SidebarBrand`. Open and collapsed state is shared through `useSidebar()`.
  - The slideover closes after navigation.
  - `[` collapses the docked sidebar on desktop and toggles the slideover elsewhere.
- **Navigation** (`utils/navigation.ts`): view, project, organization and global sections. Entries are hidden below `minRole` and get a lock badge below `editRole`, for example Annotations for a Reader. Phase B has Annotations, the organization Overview (exact match), Organizations and Account; each later phase adds its entries.
- **`ContextSwitcher`:**
  - Dropdowns for organization, project and view, like Vercel. Views come from `useGetViews`.
  - Switching keeps the current section (`utils/context-paths.ts`). A project switch goes to the same section in the `default` view; a view switch drops the selected key.
- **Navbar items:**
  - **`ThemeToggle`:** light, dark or system through VueUse `useColorMode`. The storage key `vueuse-color-scheme` is shared with Nuxt UI's `useDark`.
  - **`UserMenu`:** account, organizations, keyboard shortcuts and sign-out.
  - **`CommandPalette`:** `UDashboardSearch` with groups for the pages of the current context, every project, and actions (pin, shortcuts, sign out, and the color-mode group). It also registers `?`, `[`, `g o` and `g a`.
  - **`ShortcutsHelp`:** a modal listing the shortcuts.
- **Theme:**
  - `main.css` imports Tailwind, Nuxt UI, Inter and JetBrains Mono (`@fontsource-variable`).
  - The colors are `primary: indigo` and `neutral: zinc`.
  - It defines the annotation type tokens `--st-*` for light (Tailwind 700) and dark (400), exposed as `text-st-exe` and similar through `@theme inline`.
  - It honors `prefers-reduced-motion`.
- **No flash of the wrong theme:** the spec planned an inline script in `index.html`, which the CSP `script-src 'self'` would block. It is the external `public/theme-init.js` instead, loaded synchronously in `<head>`.
- **Common components:**
  - `EmptyState`, `ErrorState` (message and hint only, never `ErrorResponse.Error`), `LoadingState` and `RoleBadge`.
  - `RoleGate` (hide, or disable with a tooltip naming the required role).
  - `CopyButton` (with a warning toast for secrets), `RelativeTime`, and `ConfirmDialog` (type-to-confirm).

### 5. Routes

Phase B routes:
- `/`, `/login`, `/callback`, `/unsupported`, `/onboarding`, `/account`, `/orgs`;
- `/[org]` (overview, "New project" for organization Owners);
- `/[org]/[project]`, which redirects to `default/annotations`;
- `/[org]/[project]/[view]/annotations`, a placeholder that phase C replaces;
- the catch-all `[...path]` 404.

### 6. Data layer (scaffold)

- **`boot/query.ts`:**
  - `staleTime` 30 s, `gcTime` 10 min, `refetchOnWindowFocus` off by default.
  - One retry, never for `4xx` (`shouldRetry`); none for mutations.
  - `QueryCache` and `MutationCache` report errors to `boot/api-errors.ts`. A query can opt out with `meta.silent`.
- **`api-errors.ts`:** only `5xx` and network failures (status 0, not aborted) reach the toast. Everything else is left to the screen.
- **`useRole()` / `useCan()`:** read the project and organization roles from `AccessMap`, with the capability table of section 13. `useAppContext()` and `useScope()` expose the context as refs and getters for the generated composables.

### 13. Organizations and projects (part)

- **`/orgs`:** organization cards grouped from `AccessMap` (`utils/memberships.ts`) with the caller's role and their projects. An organization reached only through a project shows "Project member".
- **New organization:** `OrganizationCreateDialog` asks for the organization and its first project (`CreateAccessPoint`), reloads the account and opens the project.
- **`/[org]`:** the projects and the caller's role. **New project** (`ProjectCreateDialog`) is offered to organization Owners and disabled with a tooltip for everyone else.
- **Forms:** `OrganizationForm` and `ProjectCreateDialog` use VeeValidate with the Zod schemas of `forms/names.schema.ts`, which mirror `NameRules` (pattern and reserved words). API errors map to fields: `409` and `InvalidName`.
- **VeeValidate:**
  - v5 is still in beta (`5.0.0-beta.0`), and `@vee-validate/zod` 4.15 requires Zod 3.
  - `forms/typed-schema.ts` is a 40-line `toTypedSchema` for Zod 4: `safeParseAsync`, with issues grouped by VeeValidate field path (`values[0].name`).
- **`/account`:** profile, memberships with roles, and preferences (theme, row density, default document format, default annotation editing mode, reset).

### 17. Mock mode (moved forward from phase H)

The spec planned the e2e mock auth for phase H. It was built in phase B, because it is the only way to run the app without a WorkOS staging tenant, and every later phase uses it for checks.

- **`npm run dev:mock`** (`vite --mode e2e`):
  - `main.ts` dynamically imports `mock/browser.ts`, which starts MSW's `setupWorker` with the hand-written handlers of `mock/handlers.ts` first and `getStorytellerMock()` (the generated Faker handlers) as the fallback.
  - It also imports `mock/auth.ts`, a fake AuthService. Its sign-in goes through `/callback` like the real one.
- **Mock data:**
  - `mock/db.ts` keeps a coherent data set in `sessionStorage`: account, access points and views.
  - Scenarios: `?mock-scenario=default`, and `new-user` for onboarding.
  - `window.storytellerMock.reset()` resets the data.
  - The handlers enforce roles and name rules like the API.
- **Worker script:** `build/mock-service-worker.ts` is a Vite plugin that serves msw's `mockServiceWorker.js` in the e2e mode and emits it for e2e builds. The worker file is not committed, and it is not part of production builds.
- **Production build check:** a production build contains no mock module, no Faker and no worker. A grep of `dist/assets` confirmed it.

### Deviations from the spec

1. **SDK as a packed copy:** `install-links=true`, explained in section 1.
2. **VeeValidate:** v4.15 with an in-app Zod 4 adapter instead of v5 or `@vee-validate/zod`.
3. **Theme script:** an external `theme-init.js` instead of an inline script, because of the CSP.
4. **Mock mode** was built in phase B instead of H.
5. **SDK fetcher:** gained `forceRefresh` and `aborted`.
6. **Navigation entries** are added phase by phase, so no link points at a page that does not exist yet.
7. **Invitations redirect** for users without an account moves to phase F, together with the invitation pages.
8. **Playwright** is installed already. It was used for screenshot checks of this phase; the E2E suite comes in phase H.

## Tests

`npm test` runs 53 tests in 6 files, and all pass:

| File | Covers |
| --- | --- |
| `test/unit/utils/shell.test.ts` | `safeReturnTo` (same-origin paths; rejects absolute, protocol-relative, backslash and non-string values), `groupMemberships` and `hasOrganization`, context paths (section detection, project and view switch targets), navigation sections and locks, `describeError` (no details) |
| `test/unit/utils/forms.test.ts` | name rules (valid, invalid, reserved per kind, trimming), `toFieldPath`, `toTypedSchema` values and grouped errors |
| `test/unit/boot/boot.test.ts` | `loadAppConfig` (file vs variables, relative URL, missing API), `isLocalhost`, `shouldReport`, listener subscription, `shouldRetry` |
| `test/unit/router/guards.test.ts` | auth guard (unsupported deployment, public routes, sign-in with returnTo, 404 → onboarding, account loaded once, onboarding redirect for existing accounts) and context guard (scope and `lastContext`, exact project key, `None` role, organization reached through a project, cleared context), with MSW |
| `test/unit/stores/session.test.ts` | `503` → `error`, not `missing`; concurrent loads share one request; sign-out hooks and redirect |
| `test/unit/components/organization-form.test.ts` | VeeValidate + Zod validation messages, trimmed submit, `409` mapped to the organization field, with the Nuxt UI plugin |

Writing the tests found a bug: `useStorage` mutated the shared `defaultPreferences` object when nothing was stored yet. The store now passes a copy.

These also pass:
- `npm run typecheck` (`vue-tsc --build`);
- `npm run lint` (oxlint and ESLint, no findings);
- `npm run build` and `npm run build:e2e`;
- `dotnet build` of the esproj.

Desktop (1440×900, light and dark) and mobile (390×844) screenshots of `/orgs`, `/acme` and the annotations placeholder were checked in mock mode, with no console errors.

## Documentation

- **`src/Platform/ui.admin/README.md` (new):**
  - stack and getting started (the SDK first, then `dev:mock` or `dev`);
  - scripts and mock mode;
  - configuration (`app-config.json` and the `VITE_*` variables);
  - the WorkOS dashboard checklist;
  - architecture.
- **`CLAUDE.md`:** the admin UI tech stack line and commands.
- **SDK `README.md`:** `forceRefresh` and `isAborted`.

## Follow-ups

- Phase F adds the pending-invitations branch of the auth guard, together with the `/invitations` pages and the views page link in the context switcher.
- Check `npm ci` with the npm version of the CI image once the workflow exists (phase H).
