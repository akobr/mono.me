# Phase H of Administration UI

## Overview

Phase H is the hardening phase of [Administration UI for 2S Platform](../2026-10-09%20Administration%20UI%20for%202S%20Platform.md), sections 15 to 18 and the phase table of section 19. It delivers:

- the mobile pass;
- the accessibility pass;
- the Playwright end-to-end and visual suites;
- the Aspire integration;
- the Static Web Apps configuration with the CSP;
- the GitHub Actions workflow;
- the product page `docs/42for.net/platform/admin-ui.md`.

Checking the production CSP against a real build found three things it blocked:

1. icons that were not bundled;
2. inlined fonts;
3. `eval` in Ajv's schema compilation.

The first two are configuration fixes. The third replaces the validator behind the client-side schema validation.

## What Was Done

### 1. Mobile pass (spec section 15)

- **Bottom-sheet filters:**
  - Below md, `AnnotationFilterBar` shows the box and a **Filters** button. The button opens `AnnotationFilterSheet`, a `UDrawer`.
  - The sheet has type, status and "has a configuration" toggles, which write the same filter language through `setQualifier`. It also shows the active terms (removable), the examples, **Clear all** and **Done**.
  - The sheet parses the box's text itself, so quick toggles never work on the debounced filter.
  - Targets are at least 40 px.
- **Graph legend:** below 640 px of graph width it collapses into a **Types** popover. Hidden types are shown as struck-through muted text instead of 50 % opacity, which failed contrast.
- **Sidebar:** between lg and xl the docked sidebar starts as the icon rail; `[` still toggles it.
- **Touch:** panel resize handles are hidden on coarse pointers (`[role='separator'].cursor-ew-resize`).
- **List cards:** each card is a `listitem` that contains a `button` (before, the button itself had the `listitem` role and lost its button semantics). The selected card has `aria-current`.

### 2. Accessibility (spec section 15)

- **axe (WCAG 2.1 A/AA)** runs on these screens and finds no serious or critical violations, in both themes:
  - organizations, annotations, annotation detail, configuration, template, schemas, graph;
  - members, machines, settings, account;
  - onboarding and sign-in.
- **Contrast fixes** (`main.css`):
  - **Light text:** muted is now neutral-600 and dimmed neutral-500.
  - **Light semantic colors:** primary 600, secondary 700, info and error 700, success and warning 800. The 500 shades of Nuxt UI's defaults fell below 4.5:1 on the tinted backgrounds of soft badges and tabs.
  - **Type colors:** usage and unit of execution are now Tailwind 800 in light mode.
  - **Dark mode:** Nuxt UI's dark values are restated under `.dark`, because the `:root` rule would otherwise override them.
  - `KeyText` uses muted instead of dimmed for the key separators.
- **Grid semantics:**
  - `role="grid"` was on UTable's scroll container (`aria-required-children`). It now sits on the `<table>`, together with `aria-label`, `aria-rowcount` (the rows are virtualized) and `aria-multiselectable`.
  - The row under the J/K cursor is announced in a polite live region.
- **Reduced motion:**
  - With reduced motion, the graph worker lets the layout settle in 12 ms slices and posts it once, instead of animating it. While a node is dragged it still posts every frame.
  - The panel and overlay transitions were already shortened by `main.css`.

### 3. End-to-end and visual suites (spec section 18)

- **`playwright.config.ts`:**
  - Projects: `desktop` (1440×900), `mobile` (Pixel 7 at 390×844, touch) and `visual`.
  - `webServer` runs `npm run build:e2e && vite preview` on port 4174.
  - Locale `en-US`, time zone `Europe/Prague`.
  - `PLAYWRIGHT_CHANNEL=chrome` uses an installed Chrome.
  - Scripts: `test:e2e`, `test:visual` and `test:visual:docker`. Type-checked by the new `tsconfig.e2e.json`.
- **Flows** (`test/e2e/*.spec.ts`, desktop and mobile):

| Spec | Flow |
| --- | --- |
| `onboarding` | A signed-out visit goes to the sign-in redirect, back through `/callback`, then to onboarding; organization and project → project. Sign-out → `/login` → Continue with AuthKit. |
| `annotations` | Grid (desktop) or list (mobile) filter → detail → edit the title → save → still saved after a reload. Bottom-sheet filters (mobile). Graph node → detail: by pointer on desktop (the tooltip finds a node), by keyboard on mobile. |
| `configuration` | Start from template → JSON edit with an invalid value → 409 panel → **Save anyway** → confirm. A concurrent change (`storytellerMock.changeConfiguration`) → conflict dialog → **Overwrite**. A save, then **Compare with previous** (split or unified diff). |
| `access` | Invite an email → it appears in the list. Set the policy to Certificate → new machine → **Download .pfx** (non-empty) → **Done** stays disabled until the acknowledgement. |
| `accessibility` | axe on the 13 screens above |

- **Visual** (`visual.spec.ts`): 8 screens × 2 viewports × 2 themes = 32 screenshots, with a 0.2 % tolerance.
  - The screens: organizations, annotations, annotation detail, template, schemas, members, machines, onboarding.
  - The clock is fixed (`2026-10-10T08:00Z`) because the mock data is relative to "now".
  - Motion is reduced and the device scale factor is 1.
- **Baselines:** they are made in `mcr.microsoft.com/playwright:v1.64.0-noble` by `test/e2e/run-in-container.mjs`, which copies the SDK and the app without `node_modules` into the container, installs, runs and copies the screenshots back. The baselines (2.1 MB) are in `test/e2e/__screenshots__`; the path template has no platform segment.
- **Mock API:**
  - `?mock-scenario=` is applied once at start-up (`applyScenarioFromUrl`). It used to be applied per request: while the parameter stayed in the URL, every request reset the data, and a redirect before the first request (to the sign-in) lost it.
  - `window.storytellerMock.changeConfiguration(key, changes)` saves a version as another user.

### 4. Aspire (spec section 17)

- **Package:** `Aspire.Hosting.JavaScript` 13.1.2, the version of the other Aspire packages, added to `Directory.Packages.props` in alphabetical order and referenced by `Aspire.Host.Platform.csproj`.
- **`AppHost.cs`:**
  - `AddViteApp("ui-admin", "../../ui.admin/src", "dev:aspire")` with `WithNpm(install: false)`.
  - The `http` endpoint is fixed to 5173 and not proxied, because that is the WorkOS redirect URI.
  - `VITE_API_BASE_URL` is the `api-functions` http endpoint plus `/api`, and there is a reference to `api-functions`.
- **`dev:aspire`:** `vite --strictPort`. Aspire passes `--port`, which would clash with the fixed port of `dev`.
- **Verification:** `dotnet build` of the host succeeds. The host was not started during the phase, because it would start a second Functions host and a Cosmos emulator next to the ones running on the machine. The MessagePack advisories that restore reports come from the existing `Aspire.Hosting` dependency.

### 5. Hosting and security (spec sections 16 and 17)

- **`public/staticwebapp.config.json`:**
  - The navigation fallback to `/index.html`, excluding `/assets/*` and static files.
  - `Cache-Control: no-cache` by default, and a year plus `immutable` for `/assets/*`.
  - The CSP of section 16, with `Referrer-Policy` and `X-Content-Type-Options`. The deploy step fills the `__API_ORIGIN__` and `__AUTH_HOSTNAME__` placeholders.
- **CSP verification:** a local server served `dist/` with these headers, and every main screen of the e2e build was loaded, including the configuration form, the graph worker with WebGL, and CodeMirror. Fixes:
  - **Icons:** the Nuxt UI icon scan covered only `.vue` files. Icons named in `.ts` files (types, statuses, navigation) were fetched from `api.iconify.design` at runtime, which `connect-src` blocks. It also leaked usage to a third party. The scan now covers `app/**/*.{vue,ts}`.
  - **Fonts:** Vite inlined small font subsets as `data:` URLs, which `font-src 'self'` blocks. `assetsInlineLimit` now keeps fonts as files.
  - **Ajv:** it compiles schemas with `new Function`, which `script-src 'self'` blocks. In production, client validation would silently not run. `utils/schema-validation.ts` now uses `@cfworker/json-schema`, which interprets the schema:
    - The meta-schemas are the JSON files shipped with Ajv (draft-07, 2019-09, 2020-12).
    - A schema that fails its meta-schema validates nothing.
    - The output is reduced to one issue per problem: wrapper keywords and the alternatives of `anyOf`/`oneOf` are dropped, and `required` points at the property. Declared properties that the validator also reports under `additionalProperties` are skipped.
    - The messages keep Ajv's wording, so the existing tests and the UI text are unchanged.
    - `ajv-formats` was removed. `ajv` stays for the meta-schema files and the `ErrorObject` type used by JSON Forms.
  - **Zod:** it is set to `jitless`, because its `new Function` probe raised a CSP report.
  - **Result:** no CSP violations on any screen of the e2e build, and none at start-up of the production build.

### 6. CI (spec section 17)

`.github/workflows/ui-admin.yml` runs on pushes and pull requests touching `src/Platform/ui.admin/**`, `src/Platform/Storyteller/sdk.typescript/**` or the workflow itself:

- **`verify`:** Node from `.nvmrc` and npm 11; the SDK `npm ci`, then `npm run generate` with `git diff --exit-code -- generated`, then the SDK lint, typecheck and test; the UI `npm ci`, lint, unit tests and `npm run build` (vue-tsc and Vite).
- **`e2e`:** runs in the Playwright image of the installed version: `test:e2e` and `test:visual`. The report and the results are uploaded on failure.
- **`deploy`** (pushes to `main`, environment `ui-admin`):
  - It builds, writes `dist/app-config.json` from the `UI_ADMIN_*` variables, and fills the CSP placeholders.
  - It deploys with `Azure/static-web-apps-deploy@v1` (`skip_app_build`) and the secret `AZURE_STATIC_WEB_APPS_API_TOKEN_UI_ADMIN`.

### 7. Documentation

- **`docs/42for.net/platform/admin-ui.md`:** sign-in and onboarding, the context in the URL, the catalog and the filter language, configuration (form, bindings, Save anyway, conflicts, versions), templates and schemas, the graph, people and machines, keyboard, phones and accessibility, running it. It is linked in the 2S Platform sidebar, and the VitePress build succeeds.
- **README:** the new scripts, the end-to-end and visual tests, Aspire, and deployment (variables, secret, configuration rewrite, CSP rules for contributors).
- **`CLAUDE.md`:** the E2E commands.

### Deviations from the spec

1. **Validator:** schema validation in the browser uses `@cfworker/json-schema` instead of Ajv, to keep `script-src 'self'` without `'unsafe-eval'`. JSON Forms still creates its own Ajv instance, but the form runs with `NoValidation`, and our combinator renderer uses our validator, so Ajv never compiles.
2. **Sign-in flow:** the guard sends a signed-out user straight to the sign-in redirect, so the E2E flow checks the redirect and the return, not the `/login` page. A second test covers `/login` after signing out.
3. **Visual tests:** they run only in the Playwright Linux image (locally through Docker), because font rendering differs per platform. The functional suite runs anywhere.
4. **The `configuration` flows start from the template:** the seeded execution has no own configuration.
5. **Aspire installs nothing** (`WithNpm(install: false)`): the SDK must be built, and the UI installed with npm 11, before the first start.

## Tests

- **Unit:** `npm test` runs 146 tests in 18 files, and all pass. New: one-issue-per-problem output of the new validator (forbidden property, `oneOf`, nested items, `undefined` data). The existing Ajv-worded expectations pass unchanged.
- **E2E:** `npm run test:e2e` runs 46 tests on desktop and mobile: 45 pass, and 1 is skipped (the bottom sheet test on desktop).
- **Visual:** `npm run test:visual:docker` passes all 32 screenshots. The comparison was run three times; it is stable.
- **Static checks:**
  - `vue-tsc --build`;
  - oxlint and ESLint, with no findings;
  - `npm run build` (no mock service worker, fonts as files, `staticwebapp.config.json` emitted);
  - `dotnet build` of the Aspire host;
  - the VitePress build.

## Follow-ups

- Start the Aspire host once against a clean machine: check the `ui-admin` resource, the API base URL and the Functions CORS for `http://localhost:5173`.
- Create the `ui-admin` GitHub environment (variables and token) and the Static Web App with its custom domain, then run the first deployment from `main`.
- A WebKit project for the E2E flows (Safari on iPad and iPhone).
- The time slider of the graph (spec section 12) stays deferred.
