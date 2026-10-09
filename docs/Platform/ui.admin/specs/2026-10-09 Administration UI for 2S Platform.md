# Administration UI for 2S Platform

## Problem

The `sform` CLI is the only tool for working with the 2S Platform. It is good for scripts, but it does not let people browse a catalog of thousands of annotations, see how configurations inherit, compare versions side by side, or manage who has access. Several tasks people need to do every day have no visual tool:

- Signing in with the deployment's identity provider (WorkOS AuthKit) and seeing every organization, project and view you can access.
- Creating and managing organizations, projects (access points) and views.
- Inviting people, changing their roles, and managing machine access and shared certificates.
- Browsing, filtering and searching annotations as a grid, a tree, or a graph, then editing one in a side panel without losing your place in the list.
- Editing configurations as JSON or YAML, or through a form generated from the JSON Schema that applies to the annotation.
- Managing configuration templates and schemas, and browsing their version history with proper file diffs.

This spec plans a single-page administration application (`src/Platform/ui.admin`) and the TypeScript SDK it uses (`src/Platform/Storyteller/sdk.typescript`). Gaps in the REST API that the UI needs are planned in the companion spec [TypeScript SDK and API extensions for the Administration UI](../../Storyteller/specs/2026-10-09%20TypeScript%20SDK%20and%20API%20extensions%20for%20the%20Administration%20UI.md).

## Current State

### REST API (`src/Platform/Storyteller/Api.Functions/src`)

The OpenAPI document `open.api.v0.8.89.json` (version `0.8.89.5446`, server `http://localhost:7071/api`) has 72 operations under five tags:

| Tag | Operations the UI uses | Notes |
| --- | --- | --- |
| Access | `GetAccount`, `CreateAccount`, `GetAccessPoints`, `GetAccessPoint`, `CreateAccessPoint`, `GrantUserAccess`, `RevokeUserAccess`, `Get/Create/Reset/DeleteMachineAccess(es)`, `SetMachineAuthentication`, `GetCertificateAuthority`, `Get/Issue/RevokeSharedCertificate(s)`, `GetAuthConfiguration` | `RenewMachineCertificate` is mTLS-only and not for the UI. |
| Annotations | `GetAnnotations`, `GetAnnotation`, `SetAnnotation`, `SetAnnotations`, `DeleteAnnotation`, `GetDescendants`, per-type lists (`GetSubjects`, `GetResponsibilities`, …) | Pages of up to 1000 items (`CosmosConstants.MaxItemCountPerPage`) with `continuationToken`. Per-type lists accept `nameQuery`: exact, `%contains%`, or `^regex$` (`AnnotationsHttp.cs:605-630`). |
| Configurations | `Get/Set/Patch/DeleteConfiguration`, `GetResolvedConfiguration`, `GetConfigurationVersions`, `GetConfigurationVersion`, `GetConfigurationVersionDiff(Custom)`, `GetConfigurationViewDiff` | `POST` deep-merges into the stored document (`$remove` supported). `PATCH` is RFC 6902 including `test`. `force=true` skips schema compliance. |
| Schemas | Type, annotation and descendant-type schemas: get, put, delete, versions, version diff, plus `GetCombinedConfigurationSchema` (`/definition`) | `409 SchemaValidationErrorResponse` lists non-compliant configurations. |
| Templates | `Get/Set/Patch/DeleteTemplate`, versions, version diff | One template per annotation type and view. |

Behaviour that shapes the UI:

- **Configuration documents.** `GetConfiguration` returns the *effective* document (ancestors, then the type template, then the annotation's own content, merged; see [inheritance.md](../../Storyteller/inheritance.md) and [templating.md](../../Storyteller/templating.md)). The annotation's *own* stored content is only available as `GetConfigurationVersion(key, currentVersion)`. `GetResolvedConfiguration` also evaluates `@` bindings and JSON Logic or JSON-e envelopes ([binding.md](../../Storyteller/binding.md)). Secrets come back only for `ContributorWithSecrets` and above (`ConfigurationHttp.cs:106-115`).
- **Diffs.** Every diff endpoint returns `DiffResult { Stats, Hunks[] }`. Hunks carry 3 lines of context, and hunks separated by 6 unchanged lines or fewer are merged. `format=unified` returns `text/plain` ([JSON hunk-based diff model](../../Storyteller/specs/2026-09-21%20JSON%20hunk-based%20diff%20model.md)).
- **Access.** `GetAccount` returns `Account.AccessMap` (access point key → role) for *every* membership. `GetAccessPoints` returns only the points where the caller is `Administrator` or `Owner` (`CosmosAccessService.GetAccessPointsAsync`). Access point keys are `{org}` for an organization and `{org}.{project}` for a project. Project access is checked against the exact project key. An organization role does not grant access to its projects (`HttpRequestDataExtensions.CheckAccessToProjectAsync`).
- **Onboarding.** `CreateAccount` requires an organization and a project and makes the caller `Owner` of both. `CreateAccessPoint` creates a project. It creates the organization too when the organization does not exist yet, and otherwise requires `Owner` on it.
- **Roles.** `None < Reader < Contributor < ContributorWithSecrets < Administrator < Owner`. Machines need `Contributor`. Access points, shared certificates and the machine authentication policy need `Administrator`.
- **Grants** can only raise a role (`CosmosAccessService.cs:240-244`). `RevokePermissionAsync` checks the caller's membership with an inverted condition, so a legitimate administrator always gets an error. Members are known only by account ID: no names and no email lookup. There are no invitations.
- **Authorization errors** are returned as `401` (`SecurityTokenException` in `CheckAccessToAsync`, mapped by `ExceptionHandlingMiddleware`). A browser client cannot tell "your token expired" from "you have no role here".
- **Views** are implicit. Every annotation, configuration, template and schema id starts with `{view}.`, and `default` is the default view (`Constants.DefaultViewName`). There is no endpoint to list or create views.
- **Listings that do not exist:** configurations of a view, schemas of a view, templates of a view, labels, and members of an access point.

### Authentication

AuthKit support is implemented ([WorkOS AuthKit Authentication for Storyteller](../../Storyteller/specs/2026-09-29%20WorkOS%20AuthKit%20Authentication%20for%20Storyteller.md), reviews Phase A-D). The anonymous `GET v1/auth/configuration` returns `{ Provider, ClientId, TenantId?, Scopes?, AuthKitDomain? }` (`AuthConfigurationHttp.cs:25-65`). The API validates AuthKit access tokens against JWKS. The required JWT template adds `aud`, `email` and `name`. `DefaultUserScopes` grants signed-in users the coarse scopes. Real authorization is the account role.

### Repository

- `ff0b479` removed the old `src/Platform/ui.admin` (Vue 3 + Vite template) and `src/Platform/sdk.typescript` (openapi-generator output). Nothing is reused.
- `src/Platform/Storyteller/sdk.typescript/` contains `open.api.v0.8.89.json` and an empty `src/`.
- The C# SDK `Sdk.NSwag` is generated by NSwag (`Sdk.NSwag/README.md`). `nswag` 14.6.3 (NJsonSchema 11.5.2) is installed globally.
- `src/Platform/Directory.Build.proj` traverses `**\*.*?proj`, so any `.esproj` takes part in `dotnet build src`.
- `Aspire.Host` 13.1.2 orchestrates the Cosmos DB emulator and `api-functions`. `local.settings.json` sets `CORS: *`.
- The development machine runs Node 23.11 (not an LTS line), npm 10.9 and pnpm 10.17. The VitePress docs are deployed to Azure Static Web Apps by `.github/workflows/azure-static-web-apps-victorious-cliff-019dda503.yml`.

## Technology Choices and Alternatives

### Framework: Vue 3.5 + Vite + Vue Router 5 (no Nuxt)

The request originally named Nuxt 3. **Nuxt 3 reached end of life on 31 July 2026** ([Nuxt roadmap](https://nuxt.com/docs/4.x/community/roadmap)), so Nuxt 4 was considered first. The application is a pure SPA in front of an external REST API, and SSR is not needed. For an SPA, Nuxt's extras each have a lighter replacement:

| Nuxt feature | Replacement in plain Vue + Vite |
| --- | --- |
| File-based routing, typed routes | **Vue Router 5**, which has file-based routing (the former `unplugin-vue-router`) and typed routes in core ([migration guide](https://router.vuejs.org/guide/migration/v4-to-v5)) |
| Layouts | `route.meta.layout` (set with `definePage`) and a layout switch in `App.vue` |
| Route middleware | `router.beforeEach` guards |
| `runtimeConfig` | `/app-config.json` loaded before the app mounts (planned anyway) |
| Plugins | An explicit bootstrap sequence in `main.ts` |
| Color mode, auto-imported components | Nuxt UI's Vite plugin (`@nuxt/ui/vite`) |
| `@nuxt/test-utils` | Plain Vitest, which starts faster and has fewer moving parts |

What we gain:
- No Nitro build step for a static site.
- No forced upgrade when Nuxt 5 (with Nitro v3) ships, which is estimated for Q4 2026.
- Fewer conventions to learn, and faster tests.

*Decided (2026-10-09, second round): **Vue 3.5 + TypeScript, Vite, Vue Router 5**.*

**Versions:** Vue 3.5.x, because 3.6 was still a release candidate on npm in September 2026. Upgrade when 3.6 is `latest`; Vapor mode is not needed. For Vite, use the current stable major at implementation time and pin it.

**State:**
- **Pinia** holds client state: the session, the current context, the catalog and preferences.
- **TanStack Query for Vue** (`@tanstack/vue-query` v5) holds server state. It handles caching, deduplication, invalidation after mutations, retries and cancellation. Its composables are **generated by Orval** in the SDK package (companion spec, section 1).
- Pinia Colada (stable since 1.0 in March 2026) was considered. It was rejected because its only code generator, Hey API, documents its Colada plugin for Colada v0, is still pre-1.0 with breaking releases, and does not generate MSW mocks.

**Forms:** VeeValidate with Zod 4 for the app's own forms (the annotation form and every dialog). Use VeeValidate v5 if it is stable when phase B starts, because v5 accepts Zod 4 directly through Standard Schema. Otherwise use v4.15 with `@vee-validate/zod`, after checking Zod 4 compatibility. Base schemas come from the Orval-generated Zod output. The schema-driven configuration form stays on JSON Forms (section 10), because VeeValidate does not render forms from JSON Schema.

### UI kit: Nuxt UI v4

| Option | Licence and cost | Fit |
| --- | --- | --- |
| **Nuxt UI v4** (chosen) | MIT, free. Nuxt UI Pro was merged into it in v4 ([announcement](https://nuxt.com/blog/nuxt-ui-v4)). | Built on Reka UI and Tailwind CSS v4. It has the **dashboard shell** we need: `UDashboardGroup`, `UDashboardSidebar` (`resizable`, `collapsible`, mobile `slideover`/`drawer`), `UDashboardPanel` (resizable), `UDashboardNavbar`, `UDashboardToolbar`, `UDashboardSearch` (command palette). It also has Table (TanStack Table), Tree, InputMenu (multi-select with create, for labels), Breadcrumb, Form, InputDate and InputTime, Toast, Modal and Slideover, and light/dark color mode built in. The look is close to GitHub and Vercel. |
| PrimeVue 5 + Tailwind (styled with Aura, or unstyled with Volt) | MIT, free. | Richest widget set: DataTable, DatePicker, AutoComplete, TreeSelect, Splitter, ConfirmDialog. But TreeTable has no virtual scrolling we could confirm, so the 20,000-row tree would still need TanStack Table. There is no dashboard shell or command palette, so we would build them. Reaching a GitHub-like look takes theming work. v5 only shipped in August 2026. |
| shadcn-vue | MIT, copy-in components. | Gives the most control over the look, but takes the most assembly work. There are no dashboard or command-palette primitives as complete as Nuxt UI's. |
| Vuetify 3 / Naive UI / Element Plus | MIT. | Their visual language (Material, Ant-like) does not match the Grafana, Sentry or GitHub target. |

*Decided: Nuxt UI v4, used in a plain Vue + Vite project.* Nuxt UI officially supports projects without Nuxt. Its Vite plugin (`@nuxt/ui/vite`) registers components and composables, color mode and the theme configuration, and the Vue plugin (`@nuxt/ui/vue-plugin`) is installed in `main.ts` with `<UApp>` at the root. Check the exact plugin options against the official Vue installation page at implementation time.

### Data grid

The main views are read-mostly. Rows are selected and edited in the detail panel, not inline in cells. A spreadsheet-style grid is therefore more than we need. We need virtualization, sorting, column visibility, resizing and pinning, row selection, keyboard navigation, and **tree rows**.

| Option | Tree rows | Cost | Notes |
| --- | --- | --- | --- |
| **TanStack Table v8 via Nuxt UI `UTable`, plus TanStack Virtual** (chosen) | Yes (`getSubRows`, expanded state) | Free, MIT | Headless, so it matches the Nuxt UI theme exactly. Column sizing, pinning, visibility, sorting, row selection and global or column filters are all built in. It has the same model for the mobile list renderer, and it is small. |
| RevoGrid (`rv-grid.com`, Vue 3 wrapper) | **Pro only** ("expandable Tree View") | MIT core. Pro Light costs **$199 per developer per year** ([pricing](https://rv-grid.com/pricing), [licensing](https://www.rv-grid.com/guide/licensing)) | Excellent virtual scrolling and spreadsheet editing that we do not need. Web components with their own styling to theme. |
| AG Grid Community / Enterprise | **Enterprise only** (Tree Data, Row Grouping) | Enterprise is a commercial per-developer licence | The strongest grid overall, and heavy. Its look needs theming to match. |
| vxe-table | Yes | Free, MIT | Feature-rich (tree, virtual, editing), with a separate design system and a mostly Chinese-language ecosystem. |
| PrimeVue DataTable / TreeTable | Yes | Free, MIT | A good choice only if PrimeVue is the UI kit. |
| Handsontable | Yes | Commercial unless used non-commercially | Spreadsheet, so it is the wrong tool. |

*Decided: TanStack Table via Nuxt UI `UTable`, with TanStack Virtual.*

### Code editor (JSON and YAML, with diff)

| | Monaco (`monaco-editor`) | **CodeMirror 6** | vanilla-jsoneditor (svelte-jsoneditor) |
| --- | --- | --- | --- |
| Mobile | **Not supported**. The Monaco README FAQ answers "Is the editor supported in mobile browsers?" with "No", and touch selection and scrolling are unreliable. | Good. It is built on `contenteditable` and works with mobile keyboards. | Good |
| Bundle (approximate, minified) | Several MB plus web workers, even when limited to JSON and YAML | Around 0.3-0.5 MB for core, JSON, YAML, lint, autocomplete and merge | Around 1 MB (includes CodeMirror) |
| JSON Schema validation and completion | Built in for JSON. YAML via `monaco-yaml`. | `codemirror-json-schema` (JSON, JSON5, YAML: lint, completion, hover). Fallback: Ajv with `@codemirror/lint`. | JSON only, with a validator hook |
| Diff | `DiffEditor`, side by side or inline, with `hideUnchangedRegions` | `@codemirror/merge`: `MergeView` side by side and `unifiedMergeView`, with collapse of unchanged regions | none |
| YAML | basic language, plus `monaco-yaml` | `@codemirror/lang-yaml` | no |
| Feel | VS Code: best for desktop power users | Lighter, still IDE-like | Tree, text and table modes for raw JSON |
| Vue wrapper | `monaco-editor-vue3`, `@guolao/vue-monaco-editor`, or our own thin wrapper | `vue-codemirror` or our own thin wrapper | `json-editor-vue` |

*Decided: **CodeMirror 6** everywhere.* Responsive mobile support is a stated requirement, and Monaco does not support mobile browsers. CodeMirror covers JSON, YAML, schema validation and merge-view diffs at a fraction of the size. The trade-offs: the diff UX is slightly less polished than Monaco's, and `codemirror-json-schema` has a small maintainer base. Section 9.3 describes an Ajv-based fallback for linting. The editor sits behind our own `CodeEditor`, `DocumentEditor` and `DiffEditor` components, so replacing the engine later does not touch the screens.

### Schema-driven forms

| Option | Notes |
| --- | --- |
| **JSON Forms** (`@jsonforms/core` + `@jsonforms/vue`) with **our own Nuxt UI renderer set** (chosen) | Mature (EclipseSource) and MIT. It handles data paths, a default UI schema generated from the JSON Schema, arrays, `oneOf` and `anyOf`, rules, and Ajv validation. The bundled `vue-vanilla` renderers look plain, and the `vue-vuetify` renderers are still in preview. We write about 15 renderers on Nuxt UI components (section 10). |
| Own renderer | Full control, but we would re-implement path handling, validation mapping and array management. |
| FormKit schema, `vue-json-schema-form` (`@lljj/*`) | FormKit's schema is its own format, not JSON Schema. `@lljj` is tied to Element, Ant or Naive UI. |

### Graph

D3 (`d3-force`, `d3-zoom`, `d3-drag`, `d3-quadtree`) stays, as requested. It renders to **Canvas 2D** rather than SVG, and the simulation runs in a Web Worker so the graph stays fluid above roughly 1,500 nodes. The annotation graph is a layered DAG with 7 types. Besides a free force layout, we offer a **layered force** layout that pins each type to a tier, which makes the inheritance direction readable.

| Alternative | When it would be better |
| --- | --- |
| Sigma.js + graphology (WebGL) | More than about 10k visible nodes |
| Cytoscape.js | Ready-made layouts (dagre, ELK) and graph algorithms. It is heavier, and styling is its own format. |
| Vue Flow + elkjs | Node-editor UX with DOM nodes. Comfortable below about 500 nodes. |
| Apache ECharts graph series | A quick start, with less control over interaction. |

*Decided: D3 with a static directed graph. The time slider is deferred. Views are expected to hold 5,000 to 20,000 annotations, so the graph opens in focus mode by default (section 12).*

### Authentication: `@workos-inc/authkit-js`

WorkOS has no Vue SDK. The framework-agnostic `@workos-inc/authkit-js` (`createClient(clientId, options)`, `signIn`, `signUp`, `signOut`, `getUser`, `getAccessToken`) is wrapped in a bootstrap module (`boot/auth.ts`) and a Pinia store. Without a **custom authentication domain**, the refresh token must live in `localStorage` (`devMode`, which is turned on automatically for `localhost`). With a custom domain (`apiHostname`), it lives in an HttpOnly cookie ([client-only AuthKit](https://workos.com/docs/authkit/client-only)). *Decided: every deployed environment uses a custom AuthKit domain (for example `auth.42for.net`). `devMode` is only for `localhost`.*

### Other libraries

| Purpose | Library |
| --- | --- |
| JSON ⇄ YAML | `yaml` (eemeli), YAML 1.2 core schema, `prettyErrors` |
| JSON Patch for saves | `fast-json-patch` (`compare`, `applyPatch`) |
| Forms and validation | `vee-validate` with `zod` 4 for app forms. `ajv` and `ajv-formats` for JSON Schema: draft-07 by default, 2019-09 and 2020-12 when `$schema` says so. |
| Server state | `@tanstack/vue-query` v5, with `@tanstack/vue-query-devtools` in development builds |
| Routing | `vue-router` 5 with its Vite plugin (`vue-router/vite`) and the Volar plugins for typed routes |
| Utilities | `@vueuse/core` (breakpoints, storage, clipboard, keyboard, color mode), `idb-keyval` (catalog snapshot) |
| Icons | Iconify through Nuxt UI (`lucide` set) |
| Lint and format | **oxlint** first, for speed. Then **ESLint** flat config with `eslint-plugin-vue` (oxlint cannot lint `<template>` yet), `typescript-eslint` and `@vue/eslint-config-typescript`, plus `eslint-plugin-oxlint` (`buildFromOxlintConfigFile`) last, to turn off the rules oxlint already covers. Prettier for formatting. |
| Type checking | `vue-tsc --build` |
| Tests | Vitest (happy-dom), `@vue/test-utils`, MSW with the **Orval-generated handlers**, Playwright |
| Dev tooling | `vite-plugin-vue-devtools` |

The package manager is **npm**, the same as the removed projects and the build commands in `CLAUDE.md`. The Node version is pinned to the **Node 24 LTS** line through `.nvmrc` and `engines`.

## Proposed Changes

### 1. Projects and solution integration

```
src/Platform/
├── ui.admin/
│   ├── version.json                       # NBGV, "0.1"
│   └── src/                               # npm package root (private)
│       ├── ui.admin.esproj
│       ├── package.json                   # name "@42for.net/2splatform.ui.admin", private, "type": "module"
│       ├── index.html, vite.config.ts, tsconfig.json, tsconfig.app.json, tsconfig.node.json
│       ├── .oxlintrc.json, eslint.config.ts, .prettierrc.json
│       ├── vitest.config.ts, playwright.config.ts, .nvmrc, .env.example, .gitignore
│       ├── public/ (favicon.svg, app-config.json, staticwebapp.config.json)
│       ├── app/ …                         # application source, section 2 (named app/ to avoid src/src)
│       └── test/ (unit/, e2e/, setup.ts)
└── Storyteller/sdk.typescript/            # companion spec
    └── src/sdk.typescript.esproj
```

- Tests live in `src/Platform/ui.admin/src/test/`, inside the npm package, because they share `package.json` and `node_modules`. This deviates from the repository's `<project>/test/` layout, and the deviation is deliberate.
- **`ui.admin.esproj`**, pinned to the current `Microsoft.VisualStudio.JavaScript.Sdk` 1.0.x version (latest seen: `1.0.6578810`):

  ```xml
  <Project Sdk="Microsoft.VisualStudio.JavaScript.Sdk/1.0.6578810">
    <PropertyGroup>
      <StartupCommand>npm run dev</StartupCommand>
      <JavaScriptTestRoot>test\</JavaScriptTestRoot>
      <JavaScriptTestFramework>Vitest</JavaScriptTestFramework>
      <!-- dotnet build src must not require Node; opt in with /p:BuildJavaScript=true -->
      <ShouldRunNpmInstall Condition="'$(BuildJavaScript)' != 'true'">false</ShouldRunNpmInstall>
      <ShouldRunBuildScript Condition="'$(BuildJavaScript)' != 'true'">false</ShouldRunBuildScript>
      <BuildCommand>npm run build</BuildCommand>
      <PublishAssetsDirectory>$(MSBuildProjectDirectory)\dist</PublishAssetsDirectory>
    </PropertyGroup>
  </Project>
  ```

  Check `JavaScriptTestFramework` and `BuildCommand` against the SDK reference at implementation time. If `Vitest` is not a recognized value, drop the property.
- `42.mono.slnx`: add `src/Platform/ui.admin/src/ui.admin.esproj` to the `/Platform/` folder, and `sdk.typescript.esproj` to `/Platform/Storyteller/`.
- `mrepo.json`: add an item `src/Platform/ui.admin` ("2S admin UI") with scripts `install`, `build` (`npm run build`), `run` (`npm run dev`), `test`, and `lint`, in the same way as the `docs/42for.net` item.
- `package.json` scripts:

  | Script | Command |
  | --- | --- |
  | `dev` | `vite --port 5173 --strictPort` |
  | `build` | `vue-tsc --build && vite build` |
  | `preview` | `vite preview` |
  | `typecheck` | `vue-tsc --build` |
  | `lint` | `oxlint && eslint .` |
  | `lint:fix` | `oxlint --fix && eslint . --fix` |
  | `format` | `prettier --write .` |
  | `test` | `vitest run` |
  | `test:e2e` | `playwright test` |
- SDK dependency: `"@42for.net/2splatform.sdk": "file:../../Storyteller/sdk.typescript/src"`. The SDK ships compiled `dist/` (ESM and `.d.ts`) with the entry points `.`, `./vue-query`, `./zod` and `./msw` (companion spec, section 1). Its `prepare` script builds it, so `npm install` in `ui.admin` produces a working link.
- `vite.config.ts` essentials:

  ```ts
  import { fileURLToPath, URL } from 'node:url'
  import { defineConfig } from 'vite'
  import vue from '@vitejs/plugin-vue'
  import VueRouter from 'vue-router/vite'            // Vue Router 5 file-based routing (must come before vue())
  import ui from '@nuxt/ui/vite'
  import vueDevTools from 'vite-plugin-vue-devtools'

  export default defineConfig({
    plugins: [
      VueRouter({ routesFolder: 'app/pages', dts: 'app/typed-router.d.ts' }),
      vue(),
      ui({
        ui: { colors: { primary: 'indigo', neutral: 'zinc' } },  // theme (section 4)
        autoImport: { dirs: ['app/composables', 'app/stores'] },
        components: { dirs: ['app/components'] },
      }),
      vueDevTools(),
    ],
    resolve: { alias: { '@': fileURLToPath(new URL('./app', import.meta.url)) } },
    worker: { format: 'es' },                       // graph simulation worker
    build: { target: 'es2022', sourcemap: true },
  })
  ```

  `tsconfig.app.json` sets `strict: true`, `noUncheckedIndexedAccess: true`, the `@/*` path alias, and the Volar plugins `vue-router/volar/sfc-typed-router` and `vue-router/volar/sfc-route-blocks` under `vueCompilerOptions.plugins`.

### 2. Application structure (`app/`)

```
app/
├── main.ts                        # bootstrap sequence (section 3)
├── App.vue                        # <UApp> + layout switch on route.meta.layout + <RouterView>
├── typed-router.d.ts              # generated by the Vue Router 5 Vite plugin
├── assets/css/main.css            # @import "tailwindcss"; @import "@nuxt/ui"; type color tokens
├── boot/
│   ├── app-config.ts              # loads /app-config.json
│   ├── auth.ts                    # AuthKit client
│   ├── api.ts                     # configures the SDK fetcher (base URL, token)
│   └── query.ts                   # QueryClient, defaults, global error handler
├── router/
│   ├── index.ts                   # createRouter({ history: createWebHistory(), routes }) from 'vue-router/auto-routes'
│   ├── guards/auth.ts             # section 3
│   └── guards/context.ts          # validates :org/:project/:view against the account, sets context store
├── layouts/DefaultLayout.vue      # dashboard shell (section 4)
├── layouts/BareLayout.vue         # centered card: login, onboarding, invitation, errors
├── pages/                         # file-based routes, section 5
├── components/
│   ├── shell/        AppSidebar, AppNavbar, ContextSwitcher, CommandPalette, ThemeToggle, UserMenu, DetailPanel
│   ├── annotations/  AnnotationGrid, AnnotationList (mobile), AnnotationFilterBar, AnnotationTypeBadge,
│   │                 AnnotationStatusBadge, AnnotationDetail, AnnotationForm, AnnotationBreadcrumb,
│   │                 AnnotationLineage, AnnotationCreateDialog, LabelsInput, ValuesEditor, TimeZoneSelect
│   ├── configuration/ ConfigurationDetail, ConfigurationEditor, ConfigurationLayers, SchemaForm,
│   │                 renderers/*, BindingExpressionInput, SchemaErrorsPanel, ConflictDialog
│   ├── versions/     VersionTimeline, VersionCompare
│   ├── editor/       CodeEditor, DiffEditor, DocumentEditor, FormatToggle, setup/*
│   ├── graph/        AnnotationGraph, GraphCanvas, GraphLegend, GraphTooltip, GraphToolbar, simulation.worker.ts
│   ├── templates/    TemplateList, TemplateDetail
│   ├── schemas/      SchemaList, SchemaDetail, SchemaFormPreview, CombinedSchemaView
│   ├── access/       MembersTable, RoleSelect, InviteDialog, InvitationsTable, MachinesTable,
│   │                 MachineCreateDialog, MachinePolicyForm, CertificatesTable, CertificateIssueDialog,
│   │                 OneTimeSecretDialog
│   └── common/       ConfirmDialog (type-to-confirm), CopyButton, KeyText, RelativeTime, EmptyState,
│                     ErrorState, LoadingState, RoleGate
├── composables/      useAuth, useAppContext, useRole, useAnnotationFilter, useUnsavedChanges,
│                     useDocumentFormat, useJsonPatch, useDownload, useShortcutsHelp
├── stores/           session.ts, context.ts, catalog.ts, preferences.ts
├── queries/          scope.ts (wrappers that bind org/project/view to generated composables),
│                     invalidation.ts (path-prefix invalidation helpers), catalog.ts (catalog loaders)
├── forms/            annotation.schema.ts, organization.schema.ts, invite.schema.ts, … (Zod, used by VeeValidate)
└── utils/            annotation-key.ts (re-export from SDK), annotation-status.ts, filter-parser.ts,
                      tree-projection.ts, json-yaml.ts, errors.ts, names.ts
```

Components and composables in `app/components`, `app/composables` and `app/stores` are auto-imported by the Nuxt UI Vite plugin, the same as Nuxt UI's own components. Everything else, including SDK imports, is imported explicitly.

### 3. Authentication and session

**Boot sequence** (`main.ts`, top-level `await`, before `app.mount`):

```ts
const config = await loadAppConfig()                 // boot/app-config.ts
const auth = await createAuth(config)                // boot/auth.ts (may render the unsupported/config-error page and stop)
configureStoryteller({                               // boot/api.ts → SDK fetcher (companion spec 1.4)
  baseUrl: config.apiBaseUrl,
  getAccessToken: () => auth.getAccessToken(),
  onUnauthorized: () => auth.signIn(router.currentRoute.value.fullPath),
})
const app = createApp(App)
app.use(createPinia())
app.use(VueQueryPlugin, { queryClient: createQueryClient() })  // boot/query.ts
app.use(router)                                       // router/index.ts with guards
app.use(ui)                                           // @nuxt/ui/vue-plugin
app.provide(authKey, auth)
app.mount('#app')
```

1. `loadAppConfig` fetches `/app-config.json` (`cache: 'no-store'`). Values from `import.meta.env.VITE_*` are the fallback for local development, via `.env.local`. This lets one build serve any environment.

   ```json
   { "apiBaseUrl": "https://api.2s.42for.net/api", "authApiHostname": "auth.42for.net", "environmentName": "production" }
   ```
2. `createAuth` calls `GET {apiBaseUrl}/v1/auth/configuration` (anonymous; cached in `sessionStorage` for the tab's lifetime).
   - If `Provider !== 'AuthKit'`, the app shows a "This deployment uses {Provider}; the admin UI supports AuthKit" page. Entra ID support through MSAL is out of scope.
   - Otherwise it creates the client:

     ```ts
     const authkit = await createClient(cfg.ClientId, {
       apiHostname: isLocalhost ? undefined : appConfig.authApiHostname,  // custom auth domain → HttpOnly cookie refresh
       devMode: isLocalhost,                                              // localhost only: refresh token in localStorage
       redirectUri: `${location.origin}/callback`,
       onRedirectCallback: ({ state }) => router.replace(state?.returnTo ?? '/'),
       onRefreshFailure: () => session.signIn(router.currentRoute.value.fullPath),
     })
     ```

     If the app is not on `localhost` and `authApiHostname` is empty, it stops with a configuration error page instead of falling back to `devMode`. That way a deployment can never keep refresh tokens in `localStorage` by accident.
3. `useSessionStore` exposes `user` (from `getUser()`), `status` (`'loading' | 'signedOut' | 'signedIn'`), `signIn(returnTo)`, `signOut()`, and `getAccessToken()`.

**Route guard** (`router/guards/auth.ts`, `router.beforeEach`):

- Public routes declare `definePage({ meta: { public: true, layout: 'bare' } })`: `/login`, `/callback`, `/invitations/accept`, `/unsupported`.
- On every other route, a signed-out user triggers `authkit.signIn({ state: { returnTo: to.fullPath } })`.
- After sign-in, the guard loads the account once (`GET v1/access/account`, cached in the session store).
  - `404`, no account yet: if `GET v1/access/invitations/mine` (companion spec) returns pending invitations, go to `/invitations`. Otherwise go to `/onboarding`.
  - `200`: continue. `Account.AccessMap` is the source of the organization, project and role lists.

**API calls:** the SDK fetcher calls `getAccessToken` for every request (section 6). A `401` triggers one silent `getAccessToken()` retry, then `onUnauthorized`, which signs in again. A `403` (companion spec E1) shows a "no access" state and never re-authenticates.

**Sign-out** calls `authkit.signOut({ returnTo: origin + '/login' })`, then clears the query cache, the catalog and the session stores.

**WorkOS dashboard checklist** (documented in `ui.admin/README.md`):
- Redirect URIs: `http://localhost:5173/callback` and `https://<admin-host>/callback`.
- Initiate login URI: `/login`.
- Sign-out redirect: `/login`.
- CORS allowed origins: both origins.
- The existing JWT template (`aud`, `email`, `name`).
- Invitation accept URL: `https://<admin-host>/invitations/accept` (section 13).

### 4. Shell, layout and theming

```
Desktop (≥ 1280 px)
┌─────────────┬───────────────────────────────────────────────┬──────────────────────────────┐
│ ☰  2S Admin │ acme ▾ / billing ▾ / default ▾       ⌘K  ◐  (A) │ ● Execution · prod        ✕  │
│ 📌          ├───────────────────────────────────────────────┤ Subject northwind › Context  │
│ Annotations │ 🔍 type:exe label:eu is:active   [Grid|Tree]   ⚙ │ prod › Execution prod        │
│ Configs     │ ┌──────┬────────┬──────────────────────┬──────┐ │ [Annotation][Configuration]  │
│ Graph       │ │ Type │ Name   │ Key                  │Labels│ │ [Versions][Schema]           │
│ Templates   │ │ ● exe│ prod   │ exe.northwind.inv.prod│ eu  │ │ Form | JSON | YAML           │
│ Schemas     │ │ …    │        │                      │      │ │ Title  [                   ] │
│ Views       │ └──────┴────────┴──────────────────────┴──────┘ │ …                            │
│ ─ Project ─ │                                               │        [Revert]  [Save ⌘S]   │
│ Members     │                                             ⇔ │                              │
│ Machines    │                                               │                              │
│ Certificates│                                               │                              │
└─────────────┴───────────────────────────────────────────────┴──────────────────────────────┘
               ⇔ = drag handles; widths persisted per user

Mobile (< 768 px)
┌──────────────────────────┐    ┌──────────────────────────┐
│ ☰  billing / default  🔍 │    │ ←  prod            ⋯     │
│ [type:exe] [label:eu] ✕  │    │ Execution · Active       │
│ ● prod                   │ →  │ northwind › prod         │
│   exe.northwind.inv.prod │    │ [Annot.][Config][Ver.]   │
│ ● staging                │    │ Form fields…             │
│   …                      │    │          [Save]          │
└──────────────────────────┘    └──────────────────────────┘
```

- **Shell:** `layouts/DefaultLayout.vue` = `UDashboardGroup` (`storage: 'local'`, `storage-key: '2s-admin'`) containing `AppSidebar` (`UDashboardSidebar`), the page's main `UDashboardPanel`, and an optional `DetailPanel` (`UDashboardPanel`, `resizable`, min 360 px, default 40 %, max 70 %).
- **Hamburger and pin.** A ☰ button in the navbar opens the navigation. A 📌 toggle in the sidebar header switches between **pinned** (a docked `UDashboardSidebar` that is `resizable`, `collapsible` to an icon rail, and can be dragged to collapse) and **unpinned** (sidebar hidden, ☰ opens the same `UNavigationMenu` in a left `USlideover` that closes after navigation). Below `lg` the sidebar is always a slideover (Nuxt UI `mode: 'slideover'`). The `preferences` store persists `sidebarPinned`, `sidebarCollapsed` and the panel sizes.
- **Resizable split (desktop only).** Panel resizing is enabled at `lg` and above. Below `lg` the detail panel becomes a right `USlideover` (`md`) or a full-screen page with a back button (`< md`). Resizing is disabled on touch layouts.
- **Navbar:** `ContextSwitcher` (organization / project / view breadcrumb dropdowns, like Vercel), `UDashboardSearchButton` (⌘K / Ctrl+K), `ThemeToggle` (light, dark, system), and `UserMenu` (profile, account page, keyboard shortcuts, sign out).
- **Sidebar items:**
  - View scope: Annotations, Configurations, Graph, Templates, Schemas.
  - Project scope: Views, Members, Invitations, Machines, Certificates, Settings.
  - Organization scope: Overview, Members.

  Items the current role cannot use are hidden. Read-only items show a lock icon.
- **Theme.** The Nuxt UI Vite plugin options set `ui.colors.primary = 'indigo'` and `ui.colors.neutral = 'zinc'` (section 1). Dense typography uses Inter (`@fontsource-variable/inter`), and keys use a monospace font (`JetBrains Mono`). Color mode comes from Nuxt UI's Vite plugin, which uses VueUse `useColorMode` and stores the choice in `localStorage`. An inline script in `index.html` applies the `dark` class before the first paint, so there is no flash. Editors and the graph read CSS variables, so they switch themes without reloading.
- **Annotation type tokens.** `main.css` defines light and dark values for each type. All pairs meet WCAG AA contrast against the panel background, and every chip also shows a letter or icon, so color is never the only signal.

  | Type | Code | Token | Hue | Icon |
  | --- | --- | --- | --- | --- |
  | Responsibility | `rst` | `--st-rst` | violet | `i-lucide-target` |
  | Unit | `unt` | `--st-unt` | fuchsia | `i-lucide-box` |
  | Subject | `sbt` | `--st-sbt` | blue | `i-lucide-sun` |
  | Usage | `usg` | `--st-usg` | amber | `i-lucide-link` |
  | Context | `cnt` | `--st-cnt` | cyan | `i-lucide-layers` |
  | Execution | `exe` | `--st-exe` | emerald | `i-lucide-play` |
  | Unit of execution | `uxe` | `--st-uxe` | orange | `i-lucide-cpu` |
- **Keyboard shortcuts** (`defineShortcuts`):
  - `⌘K` palette, `/` focus search, `g a` / `g c` / `g g` / `g t` / `g s` go to section.
  - `j` / `k` and `↑` / `↓` move the row, `Enter` opens the detail, `Esc` closes the detail.
  - `⌘S` saves, `⌘⇧F` cycles Form / JSON / YAML, `[` toggles the sidebar, `?` shows the shortcut help.

### 5. Routes

Organization, project and view names are path segments, like GitHub. The top-level names `login`, `callback`, `onboarding`, `invitations`, `account`, `orgs` and `unsupported` are reserved. The companion spec validates names against them. Annotation keys contain only dots and name characters, so they are safe as path segments.

| Route (`app/pages/…`) | Screen |
| --- | --- |
| `index.vue` → `/` | Redirects to the last context (`preferences.lastContext`) or `/orgs` |
| `login.vue` | Sign-in landing. `?error=` shows AuthKit errors. |
| `callback.vue` | Spinner while authkit-js completes the code exchange |
| `onboarding.vue` | First organization and project form → `CreateAccount` |
| `invitations/index.vue`, `invitations/accept.vue` | Pending invitations and the accept landing (section 13) |
| `account.vue` | Profile, memberships (from `AccessMap`), preferences (theme, editor format, density) |
| `orgs/index.vue` | Organization cards with role, and each organization's projects. "New organization". |
| `[org]/index.vue` | Organization overview: projects, members (Admin), "New project" (Owner) |
| `[org]/members.vue` | Organization members and invitations |
| `[org]/[project]/index.vue` | Redirects to `/{org}/{project}/default/annotations` |
| `[org]/[project]/views.vue` | Views list and create |
| `[org]/[project]/members.vue`, `invitations.vue` | Project members and invitations |
| `[org]/[project]/machines.vue`, `machines/[id].vue` | Machine access list and detail |
| `[org]/[project]/certificates.vue` | Shared certificates |
| `[org]/[project]/settings.vue` | Machine authentication policy, CA certificate download, project info |
| `[org]/[project]/[view]/annotations.vue` | Annotation grid or tree. A child `<RouterView>` renders the detail panel. |
| `…/annotations/[key].vue` (+ children `index`, `configuration`, `versions`, `schema`) | Annotation detail tabs |
| `[org]/[project]/[view]/configurations.vue` + `configurations/[key].vue` (+ children `index`, `effective`, `resolved`, `versions`, `schema`) | Configuration grid and detail |
| `[org]/[project]/[view]/graph.vue` + `graph/[key].vue` | Graph and selected node detail |
| `[org]/[project]/[view]/templates.vue` + `templates/[type].vue` (+ `versions`) | Templates |
| `[org]/[project]/[view]/schemas.vue` + `schemas/type/[type].vue`, `schemas/annotation/[key].vue`, `schemas/annotation/[key]/type/[type].vue` (+ `versions`) | Schemas |
| `[org]/[project]/[view]/compare.vue` | Same key across two views (`?key=&to=`) |

- Because the grid page renders the detail as a nested route, the grid keeps its scroll position, selection and filters while the detail changes.
- Query state in the URL: `q` (filter), `mode` (`grid|tree`), `by` (`subject|responsibility`, tree perspective), `sort` (`name:asc`), `format` (`form|json|yaml`, only when it differs from the preference), and for diffs `v` and `from`. Every screen state worth sharing can be addressed.
- The `context` guard checks that `{org}.{project}` is in `AccessMap`, and shows a 403 state if it is not. It also sets the `context` store and saves `preferences.lastContext`.
- **Vue Router 5 conventions:**
  - `[param]` files and folders give dynamic segments.
  - `index.vue` is the default child.
  - A `name.vue` file next to a `name/` folder is the parent with a `<RouterView>`.
  - Redirects and meta (`layout`, `public`, `minRole`) are declared with `definePage`.
  - Typed `useRoute('/[org]/[project]/[view]/annotations/[key]')` and typed `RouterLink` targets come from `typed-router.d.ts`.
  - The experimental data loaders are **not** used. Data comes from TanStack Query.
- A catch-all `[...path].vue` renders the 404 page.

### 6. Data layer

**SDK.** The Orval-generated SDK (companion spec, section 1) has four entry points:

| Import | Content | Used for |
| --- | --- | --- |
| `@42for.net/2splatform.sdk` | Types, plain request functions (`getAnnotation(org, project, view, key, options)`), `configureStoryteller`, key and role helpers, `StorytellerError` | The catalog loader, imperative calls, guards |
| `@42for.net/2splatform.sdk/vue-query` | Generated composables (`useGetAnnotation`, `useSetAnnotation`, `usePatchConfiguration`, …), query-key functions (`getGetAnnotationQueryKey(...)`), and `invalidateByPath` | All screens |
| `@42for.net/2splatform.sdk/zod` | Generated Zod 4 schemas for parameters, bodies and responses (`setAnnotationBody`, …) | VeeValidate forms. JSON Schema for the editor through `z.toJSONSchema()`. |
| `@42for.net/2splatform.sdk/msw` | Generated MSW handlers and Faker data factories | Vitest and Playwright |

Generated composables accept `MaybeRef` parameters, so queries refetch when the route changes. `queries/scope.ts` adds thin wrappers that read `org`, `project` and `view` from the context store, so screens only pass the annotation key:

```ts
export function useAnnotation(key: MaybeRefOrGetter<string>) {
  const scope = useAppContext()                     // { org, project, view } refs
  return useGetAnnotation(scope.org, scope.project, scope.view, key, {
    query: { enabled: () => !!toValue(key), staleTime: 30_000 },
  })
}
```

**Errors.** The SDK fetcher throws `StorytellerError` for every non-2xx response and every network failure:

```ts
interface StorytellerError {
  status: number
  message: string
  hint?: string
  errorCode?: string
  schemaErrors?: { annotationKey: string; viewName: string; errors: string[] }[]
}
```

The global `QueryClient` `onError` shows a toast with `message` and `hint` for `5xx` and network errors. Screens handle `400`, `403`, `404`, `409` and `422` themselves. Exception details (`ErrorResponse.Error`, which includes stack traces) are never shown.

**Query keys.** Orval generates one key function per operation. The keys start with the request path, for example `['/v1/acme/billing/default/configuration/exe.a.b.c']`, followed by query parameters when there are any. Invalidation after a mutation uses the SDK helper `invalidateByPath(queryClient, prefix)`, which matches every key whose path starts with the prefix:

| After | Invalidate prefix |
| --- | --- |
| Save or delete an annotation | `/v1/{org}/{project}/{view}/annotations/{key}`, and update the catalog entry |
| Save or delete a configuration | `/v1/{org}/{project}/{view}/configuration/{key}` (covers own, resolved, versions, diffs) and `/v1/{org}/{project}/{view}/configurations` |
| Save a schema or template | `/v1/{org}/{project}/{view}/configuration-schema` or `/template/{type}`, plus `/configuration-schemas` or `/templates` |
| Member, invitation, machine or certificate change | `/v1/access/points/{key}`, `/v1/{org}/{project}/access`, and the account (`/v1/access/account`) when the caller's own role changed |

**Defaults** (`boot/query.ts`):
- `staleTime` is 30 s and `gcTime` is 10 min. `refetchOnWindowFocus` applies to detail queries only.
- One retry for idempotent `GET`s, never for `4xx`, and none for mutations.
- `useGetResolvedConfiguration` is always called with `gcTime: 0`, and query persistence is never turned on, because resolved configurations can contain secrets.
- Mutations that return one-time secrets (machine create and reset, certificate issue) pass their result straight to `OneTimeSecretDialog`. They never write it into the cache.

**Catalog store** (`stores/catalog.ts`). The grid, tree, graph, search, label suggestions and breadcrumbs all read the full annotation list of the current view, so the store loads it once per scope:

```ts
interface CatalogState {
  scope: Scope | null                       // { org, project, view }
  status: 'idle' | 'loading' | 'ready' | 'error'
  loadedCount: number
  byKey: ShallowRef<Map<string, Annotation>> // markRaw; replaced, not deep-mutated
  childrenByKey: Map<string, string[]>       // derived from key structure (section 7)
  labelCounts: Map<string, number>
  configured: Set<string> | null            // keys with a configuration (companion spec E6), null = unknown
  loadedAt?: number
}
```

- **Expected size:** 5,000 to 20,000 annotations per view, which is about 5-20 pages of 1000 and roughly 5-15 MB of JSON at the top end. The whole catalog is kept in the browser. Server-side search (companion spec E10) is not needed.
- **Parallel loading.** The seven per-type list endpoints (`GetResponsibilities`, `GetSubjects`, `GetUsages`, `GetContexts`, `GetExecutions`, `GetUnits`, `GetUnitsOfExecution`) are paged concurrently, each following its own `ContinuationToken`. This cuts wall time compared with one sequential `GetAnnotations` stream, and the per-type responses also carry the relation fields (`SubjectKey`, `ContextNames`, …). `GetAnnotations` remains the fallback when a per-type call fails.
- **Progress.** A progress bar ("Loaded 8 000 annotations…") is shown, and the grid renders as soon as the first page of any type arrives.
- **Snapshot cache.** After a full load, the catalog is written to IndexedDB (`idb-keyval`), keyed by user ID and scope. The next visit shows the snapshot at once with a "Refreshing…" badge and replaces it when the fresh load finishes. Editing always works on fresh detail queries, never on the snapshot. The cache holds annotations only, never configurations, and is cleared on sign-out.
- **Memory.** The `Map` and its derived indexes are `markRaw` and replaced as a whole, so Vue does not make 20,000 objects deeply reactive.
- Mutations update the map locally (`upsert`, `remove`) and invalidate the matching detail queries, so there is no full reload. The "Refresh" button reloads everything.
- **Performance budget**, on the 20,000-annotation fixture on a mid-range laptop: first rows within 1.5 s, full catalog within 6 s on a cold load, filter re-evaluation under 50 ms, grid scrolling at 60 fps.

### 7. Annotation keys, status and tree projection

`utils/annotation-key.ts` re-exports the SDK helpers (companion spec, section 1.5). They mirror `AnnotationKey` and [inheritance.md](../../Storyteller/inheritance.md):

| Type | Key | Direct ancestors (merge order) |
| --- | --- | --- |
| Responsibility | `rst.{r}` | none |
| Subject | `sbt.{s}` | none |
| Unit | `unt.{r}.{u}` | `rst.{r}` |
| Context | `cnt.{s}.{c}` | `sbt.{s}` |
| Usage | `usg.{s}.{r}` | `rst.{r}`, `sbt.{s}` |
| Execution | `exe.{s}.{r}.{c}` | `usg.{s}.{r}`, `cnt.{s}.{c}` |
| Unit of execution | `uxe.{s}.{r}.{c}.{u}` | `unt.{r}.{u}`, `exe.{s}.{r}.{c}` |

**Status** (`annotation-status.ts`) is computed with `now` in UTC:
- `disabled` if `IsDisabled`.
- Else `scheduled` if `ValidFrom > now`.
- Else `expired` if `ExpiresAt <= now`.
- Else `active`.

Dates display in the annotation's `TimeZone` (falling back to the user's zone), with the user's local time in a tooltip.

**Tree projection** (`tree-projection.ts`). A DAG cannot be shown as one tree, so the tree mode offers two **perspectives**. In each one, every annotation appears exactly once:

| Perspective | Roots | Parent rule |
| --- | --- | --- |
| By subject (default) | `sbt.*`, then `rst.*` | `cnt` → `sbt`; `usg` → `sbt`; `exe` → `cnt`; `uxe` → `exe`; `unt` → `rst` |
| By responsibility | `rst.*`, then `sbt.*` | `unt` → `rst`; `usg` → `rst`; `exe` → `usg`; `uxe` → `exe`; `cnt` → `sbt` |

- When the parent is missing from the catalog, the node attaches to a synthetic "(missing) {key}" placeholder row, so data problems stay visible.
- When filtering, matching rows keep their ancestor chain. Non-matching ancestors are shown dimmed, like a file tree in an IDE.

### 8. Annotations grid, tree, filtering and search

**Columns** (TanStack column defs, persisted per user in `preferences.grid.annotations`):

| Column | Default | Notes |
| --- | --- | --- |
| Select | ✓ | Checkbox for bulk actions |
| Type | ✓ | `AnnotationTypeBadge` (color, icon, code); sortable by merge order |
| Name | ✓ | Sortable |
| Key | ✓ | `KeyText` in monospace with segment highlighting and a copy button |
| Title | ✓ | Truncated; full text in a tooltip |
| Labels | ✓ | Chips, at most 3 plus "+n" |
| Status | ✓ | `AnnotationStatusBadge` |
| Configuration | ✓ | Version badge `v12`, or "–" (needs E6) |
| Valid from / Expires | hidden | Annotation time zone |
| Description, Documentation | hidden | |

**Features:**
- Virtualized rows with 36 px compact / 44 px comfortable density.
- Column resize, reorder, hide and pin (Type and Name are pinned by default). Multi-sort with Shift-click.
- `Grid | Tree` toggle, with a perspective selector in tree mode, plus expand-all and collapse-all.
- **Bulk actions** for Contributors and above: enable, disable, add or remove label, delete. All except delete go through `SetAnnotations` in batches of 100. Delete calls `DeleteAnnotation` per key with a concurrency of 4, and asks for confirmation with the count.
- Below `md`, `AnnotationList` renders the same row model as two-line cards: badge and name, then the key and status.
- **Filter language** (`filter-parser.ts`, GitHub style). The search box shows chips for the parsed qualifiers. The text is kept in `?q=`, so filters can be shared.

  ```
  query      := term (WS term)*
  term       := ['-'] (qualifier ':' value | text)
  qualifier  := type | label | is | has | key | subject | responsibility | context | unit | ancestor
  value      := word | '"' chars '"' | value ',' value          (comma = OR)
  type       := rst|responsibility | unt|unit | sbt|subject | usg|usage | cnt|context | exe|execution | uxe|unit-of-execution
  is         := active | disabled | scheduled | expired
  has        := config | labels | values | docs | description
  text       := matches name, key, title, description (case-insensitive substring)
  ```

  Examples: `type:exe label:eu,us -is:disabled`, `subject:northwind has:config`, `ancestor:rst.invoicing "late fee"`.

  Parsing yields `{ include: Predicate[]; exclude: Predicate[] }`, which is applied to the catalog. A filter over 20k annotations takes a few milliseconds and needs no worker.
- **Command palette** (`UDashboardSearch`) searches the catalog with Fuse.js, which the component already includes, over `Name`, `AnnotationKey` and `Title`. It has groups for Annotations (top 20), Pages, Actions ("New annotation", "Switch project", "Toggle theme") and Recent (last 10 opened keys from `preferences`).
- **New annotation** (`AnnotationCreateDialog`):
  1. Pick a type.
  2. Fill in the parent pickers that type needs. For example, an execution needs a subject, a responsibility and a context. Each picker autocompletes from the catalog and also accepts a new name.
  3. Enter a name and see a live key preview, with validation from the SDK key helpers.

  When an ancestor is missing, the dialog lists it and creates it in the same `SetAnnotations` batch. During implementation, confirm whether the backend auto-creates ancestors and simplify if it does.

### 9. Editors: `CodeEditor`, `DiffEditor`, `DocumentEditor`

#### 9.1 Contracts

```ts
// CodeEditor.vue: text level
props: {
  modelValue: string
  language: 'json' | 'yaml'
  schema?: JSONSchema        // validation + completion + hover
  readonly?: boolean
  height?: 'auto' | number   // auto grows to content up to the panel height
  documentUri?: string       // stable id, e.g. st://acme/billing/default/configuration/exe.a.b.c
  decorations?: 'bindings'   // highlight @-expressions and $jsonlogic/$jsone envelopes
}
emits: { 'update:modelValue': string, diagnostics: Diagnostic[], save: void /* ⌘S */ }

// DocumentEditor.vue: value level; owns JSON ⇄ YAML
props: { modelValue: unknown; format: 'json' | 'yaml'; schema?: JSONSchema; readonly?: boolean }
emits: { 'update:modelValue': unknown, 'update:format': 'json' | 'yaml', 'update:valid': boolean }

// DiffEditor.vue: JSON only
props: { original: string; modified: string; stats?: DiffStats; hunks?: DiffHunk[];
         layout?: 'auto' | 'split' | 'unified'; collapseUnchanged?: boolean }
```

#### 9.2 JSON ⇄ YAML (`useDocumentFormat`, `json-yaml.ts`)

- `DocumentEditor` keeps a text buffer per format. When the buffer parses, it emits the parsed value. When it does not, it emits `update:valid=false` and keeps the last valid value.
- Switching format serializes the **last valid value**. When the current buffer is invalid, the switch is blocked with "Fix the syntax error at line N first". Comments in YAML are lost when switching to JSON or saving (documented in a tooltip), because the API stores JSON.
- Serialization: JSON with 2-space indentation and key order preserved. YAML with `yaml.stringify(value, { indent: 2, lineWidth: 0, defaultStringType: 'PLAIN', defaultKeyType: 'PLAIN' })`, parsed with `{ schema: 'core', uniqueKeys: true, prettyErrors: true }`. Integers beyond 2^53 lose precision. This is a known limitation, shown as a warning when detected.
- The `preferences.editorFormat` default (`json`) applies everywhere. `FormatToggle` (`JSON | YAML`) sits in every editor header, and `⌘⇧F` cycles the format.

#### 9.3 CodeMirror 6 implementation

- Packages: `@codemirror/state`, `@codemirror/view`, `@codemirror/commands`, `@codemirror/language`, `@codemirror/lang-json`, `@codemirror/lang-yaml`, `@codemirror/lint`, `@codemirror/autocomplete`, `@codemirror/search`, `@codemirror/merge`, `codemirror-json-schema`. We write our own wrapper, so `vue-codemirror` is not needed.
- Extensions:
  - Line numbers, folding, bracket matching, search, `⌘S` keymap, history, indent on input.
  - `EditorView.theme` built from CSS variables, so light and dark follow color mode.
  - `jsonSchema(schema)` or `yamlSchema(schema)` from `codemirror-json-schema`. When the schema changes, it is swapped with a `Compartment`.
- **Fallback linter.** If `codemirror-json-schema` misbehaves (for example the known YAML completion issue), a `linter()` validates with Ajv. It maps `instancePath` to positions with `jsonc-parser` (JSON) or the `yaml` CST (YAML), and completion is turned off. Build this fallback in phase D if the issue still exists.
- **Bindings decoration.** A `ViewPlugin` marks strings that start with `@` and objects that contain `$jsonlogic`, `$jsone` or `$definition` keys. They get a subtle background and an "fx" gutter marker.
- **Diff:** `MergeView` (split, read-only both sides) at `lg` and above, and `unifiedMergeView` below `lg`, with `collapseUnchanged: { margin: 3, minSize: 6 }`. These are the same numbers as the server hunk rules.
- The editor bundle loads lazily: `CodeEditor` and `DiffEditor` are `defineAsyncComponent(() => import(...))`, so Vite splits them into their own chunk, and the grid route does not ship CodeMirror. The same applies to the graph (`AnnotationGraph`) and JSON Forms (`SchemaForm`).

#### 9.4 Mobile

All editors work on touch with CodeMirror. Forms are the default editing mode on phones anyway. The diff is unified below `lg`.

### 10. Schema-driven configuration form (`SchemaForm`)

**Input:** the merged schema from `GetCombinedConfigurationSchema(key)` (`MergedContent`). When no schema applies, the Form mode is hidden and the editor opens in the preferred text format.

**Engine:** `@jsonforms/vue` `JsonForms` with `uischema` generated by `Generate.uiSchema(schema)`, a custom Ajv instance, and the renderer set `components/configuration/renderers/`. Renderers use the `useJsonFormsControl` and `useJsonFormsLayout` composables and are ranked with `rankWith`.

| Renderer | Tester | Component |
| --- | --- | --- |
| `StringControl` | `isStringControl` | `UInput` (`maxLength`, `pattern` hint, `examples` as placeholder) |
| `MultilineControl` | string with `maxLength > 200`, `format: 'textarea'`, or `x-multiline` | `UTextarea` autoresize |
| `SecretControl` | `format: 'password'` or `x-secret: true` | `UInput type=password` with reveal |
| `UrlControl` | `format: 'uri'` / `'url'` | `UInput` with "open link" trailing button |
| `NumberControl` / `IntegerControl` | `isNumberControl` / `isIntegerControl` | `UInputNumber` (`minimum`, `maximum`, `multipleOf` → step) |
| `BooleanControl` | `isBooleanControl` | `USwitch` |
| `EnumControl` | `isEnumControl` | `USelect`, or `USelectMenu` with search when there are more than 8 options |
| `OneOfEnumControl` | `isOneOfEnumControl` (`oneOf: [{const,title}]`) | `USelectMenu` showing `title`, storing `const` |
| `DateControl` / `TimeControl` / `DateTimeControl` | `format` date / time / date-time | `UInputDate` / `UInputTime` |
| `PrimitiveArrayControl` | array of string, number or enum | Tag input (`UInputTags`, or `UInputMenu multiple` for enums) |
| `ArrayLayout` | array of objects | Collapsible cards with add, remove and move up/down; titles from `title` or the first string property |
| `ObjectRenderer` | `isObjectControl` | `UCard`/fieldset with title, description, collapse; nested sections get a sticky mini-TOC on desktop |
| `MapControl` | `additionalProperties` schema without `properties` | Key/value rows; values rendered with the `additionalProperties` schema |
| `OneOfRenderer` / `AnyOfRenderer` | `oneOf` / `anyOf` of objects | Segmented control to pick a branch, then that branch's form |
| `BindingControl` | **rank 100**, when the data at the path is a binding (string starting with `@`, or an object with `$jsonlogic`, `$jsone` or `$definition`) | `BindingExpressionInput` (monospace, "fx" badge, "Convert to value" action) |
| `FallbackJsonControl` | rank 1, anything unsupported (`not`, `if/then/else`, `patternProperties`, tuples) | Inline `DocumentEditor` for that subtree |

**Labels and help:**
- Labels come from `title`, falling back to the humanized property name.
- `description` renders as help text (inline markdown: code, links, emphasis).
- `default` shows as a placeholder, `readOnly` makes the field read-only, and `deprecated` adds a badge.
- Required fields get an asterisk.

**Literal ↔ expression.** Every control has a small "fx" toggle that turns the value into a binding expression (`@…`) and back. Ajv errors whose `instancePath` points at a binding value are dropped, because the server evaluates bindings later. The server still validates on save.

**Inheritance awareness.** `ConfigurationEditor` passes `effective` (from `GetConfiguration`) and `own` (from the current version's content):
- A field with no own value shows the effective value as a placeholder, with an "inherited" chip and an **Override** button that copies the value into own content.
- A field with its own value that differs from what it would inherit gets a **Reset to inherited** button, which removes the path from own content.
- v1 says "inherited" without naming the ancestor. Naming it needs the ancestors' own documents, which is a later enhancement.

**Server errors.** `409 SchemaValidationErrorResponse` messages have the form `#/path: Kind`. They are parsed and attached to controls by JSON pointer. The rest are listed in `SchemaErrorsPanel`.

### 11. Detail panels

#### 11.1 Annotation detail (`AnnotationDetail`)

- **Header:**
  - Type badge, `Name`, status badge, and `KeyText` with copy.
  - Actions: **Save** (⌘S), **Revert**, **Open configuration**, **Show in graph**, **Copy link**, and ⋯ (Delete, which requires typing the key; View JSON).
- **Breadcrumb** (`AnnotationBreadcrumb`) follows the current tree perspective. It is built from key segments, and each segment links to its annotation. For example `uxe.northwind.invoicing.prod.pdf`, by subject: *Subject northwind › Context prod › Execution prod › Unit of execution pdf*.

  Ancestors missing from the catalog are shown struck through. A **Lineage** popover (`AnnotationLineage`) shows every ancestor as a small DAG in merge order, which is also the order configurations inherit in.
- **Tabs:** Annotation · Configuration (`vN`) · Versions · Schema. Tabs are child routes, so they can be linked. The Configuration and Schema tabs are the configuration screens (section 11.2) embedded in the panel.
- **Annotation tab, `Form | JSON | YAML`** (Form is the default; the choice is remembered per user):

  | Field | Control | Validation |
  | --- | --- | --- |
  | Name, Key, Type | read-only (part of the key) | |
  | Title | `UInput` | ≤ 200 chars |
  | Description | `UTextarea` (autoresize) with Write/Preview tabs (markdown) | |
  | DocumentationLink | `UInput type=url` with an open-link button | absolute `http(s)` URL |
  | IsDisabled | `USwitch` ("Disabled") | |
  | ValidFrom, ExpiresAt | `UInputDate` + `UInputTime`, interpreted in `TimeZone`, with a "Clear" button | `ValidFrom < ExpiresAt` |
  | TimeZone | `TimeZoneSelect`: searchable `USelectMenu` over `Intl.supportedValuesOf('timeZone')` + `UTC`, showing the current offset | valid IANA ID |
  | Labels | `LabelsInput` (below) | trimmed, unique, non-empty |
  | Values | `ValuesEditor` (below) | unique, non-empty keys |
  | Type-specific references (`SubjectKey`, `ResponsibilityKey`, `ContextKey`, `UsageKey`, `UnitKey`, `ExecutionKey`, `*Names`) | read-only links and chips | |
  | `Unit.UnitType`, `Unit.UnitDefinition` | `UInput` / `UTextarea` | |

  - **`LabelsInput`**, Jira-like: `UInputMenu multiple` with `create-item`. Suggestions are all labels in the catalog, sorted by frequency, with counts ("eu · 214"). Typing filters them, and "Create label 'xyz'" appears when there is no exact match. Chips get a stable color from a hash of the label.
  - **`ValuesEditor`**: one row per key with key, type (`string | number | boolean | null | object | array`) and value. Primitives are edited inline. Objects and arrays open a popover `DocumentEditor`. Rows can be added, removed and renamed, and an "Edit all as JSON/YAML" toggle edits the whole map.
  - **JSON / YAML mode** edits the whole `Annotation` object. The editor's JSON Schema is `z.toJSONSchema(setAnnotationBody)`, built from the generated Zod schema and computed once. Read-only fields (`AnnotationKey`, `Name`, `AnnotationType`, `ProjectName`, `ViewName`) must be unchanged, or saving is blocked with a message.
  - **Validation** uses one Zod schema, `forms/annotation.schema.ts`, shared by Form and text modes. It extends the generated `setAnnotationBody` with UI rules: `ValidFrom < ExpiresAt`, an absolute `http(s)` URL, a valid IANA time zone, and unique trimmed labels. Form mode binds it with VeeValidate (`useForm({ validationSchema })`, with `defineField` per control). Text mode runs `safeParse` on the parsed document and shows the errors in the editor.
  - **Save** calls `useSetAnnotation` (`SetAnnotation(key, annotation)`), updates the catalog entry, and invalidates the annotation's path prefix and the configuration list.
- **Unsaved changes.** `useUnsavedChanges` tracks dirty state. A dot appears in the tab title. Route leave and tab close (`beforeunload`) ask for confirmation, and switching the selected row while dirty asks "Discard changes?".

#### 11.2 Configuration detail (`ConfigurationDetail`)

**Tabs:**

| Tab | Content | Source |
| --- | --- | --- |
| **Edit** (default) | The own stored content: `Form` (when a schema applies) `| JSON | YAML` | `GetConfiguration` → `Version`, then `GetConfigurationVersion(key, Version)` → own `Content` |
| **Effective** | Read-only merged document: ancestors, template, own | `GetConfiguration` (`Content`, `Hash`) |
| **Resolved** | Read-only, bindings evaluated | `GetResolvedConfiguration`. `422` shows the binding error (`Message`, `Hint`). A "may contain secrets" banner is shown, the content is blurred until **Reveal**, and the response is never cached. |
| **Versions** | Section 11.3 | |
| **Schema** | Combined schema, read-only, with "Applied schemas" (`AppliedSchemas[]`, in order, linking to each layer's schema page) | `GetCombinedConfigurationSchema` |

- **No configuration yet** (`404`): an empty state with **Create configuration**. It starts from `{}`, or from the type template's content when the user picks "Start from template".
- **Saving** (`useJsonPatch`):
  1. `ops = compare(ownOriginal, ownEdited)`. If `ops` is empty, nothing happens.
  2. Prepend a `test` op with the original value for every `replace` and `remove` path. This gives optimistic concurrency without ETags.
  3. Call `PatchConfiguration(key, ops)` (`application/json-patch+json`).
  4. **`409` schema error**: show `SchemaErrorsPanel` with field mapping. Contributors also get **Save anyway** (`force=true`) after a confirmation that names the failing rules.
  5. **Failed `test`** (`412`, `ErrorCode: PatchTestFailed`, companion spec E13; today the API returns `500`): open `ConflictDialog`. It shows a diff of *theirs* (fresh own content) against *yours*, with the choices **Reload, discard mine**, **Overwrite** (re-patch from the fresh base) and **Copy mine to clipboard**.
  6. On success, invalidate `configuration*`, `configurationVersions` and `configurations`, and update `catalog.configured`.
- **Delete configuration** requires typing the key. It deletes the own content only. Inherited values remain.
- **Navigation.** The header shows "Annotation ›" linking to the annotation route. The annotation detail's Configuration tab is the same component, so moving between the two keeps the selection.

#### 11.3 Versions and diffs (`VersionTimeline`, `VersionCompare`)

- **Timeline:** `GetConfigurationVersions` gives `Version`, `Author`, `CreationTime` and `ExpirationTime`. History expires, and expired versions show as "expired". Each entry has the actions **View**, **Compare with previous**, and **Compare with…** (pick two: A = `from`, B = `v`). URL: `…/versions?v=7&from=5`.
- **Compare (JSON only, no YAML toggle):**
  - Fetch `GetConfigurationVersionDiffCustom(v, from)` for `Stats` and `Hunks`.
  - Fetch `GetConfigurationVersion(from)` and `GetConfigurationVersion(v)` for the full documents. Both are serialized identically (2 spaces, original key order), so the editor's line diff and the server hunks agree.
  - Render `DiffEditor` with a header "+12 −3 · 2 changes", **Previous/Next change** buttons that jump to the server hunks (`NewStart`), a **Collapse unchanged** toggle (on by default), and **Split | Unified** (auto by breakpoint).
  - When a document is missing (an expired or deleted version), show the server hunks as a read-only unified view and explain why the full documents are not available. This uses the same `DiffResult` with `OldLineNumber` and `NewLineNumber`.
- **View compare** (`compare.vue`): pick a key and a target view. Uses `GetConfigurationViewDiff(key, viewTo)` and the same viewer.
- Templates and all three schema kinds reuse `VersionTimeline` and `VersionCompare` with their own version and diff endpoints.

### 12. Graph (`AnnotationGraph`)

**Data:** nodes are catalog annotations. Links are direct ancestor edges, **ancestor → descendant**, which is the direction configuration flows.

**Rendering** (`GraphCanvas`):
- Canvas 2D at `devicePixelRatio`.
- Node radius is `4 + 2·log2(1 + descendants)`, filled with the type color.
- Links are 1 px lines with arrowheads, at 35 % opacity.
- Names are drawn when the zoom is ≥ 1.2, for hovered or selected nodes, and for the top 50 by degree.
- Hit-testing uses a `d3-quadtree` rebuilt every N ticks.

**Simulation** (`simulation.worker.ts`, always used, because a full view has 5,000 to 20,000 nodes):
- `forceLink` (distance 30), `forceManyBody` (strength −40, `distanceMax` 400), `forceCollide` (radius + 2), and `forceX` / `forceY`.
- Positions are posted as a transferable `Float32Array` on every animation frame.

**Layouts:**
- **Force:** free layout.
- **Layered** (default): `forceY` pulls each type to a tier. Tier 0 is `rst`, `sbt`; tier 1 is `unt`, `usg`, `cnt`; tier 2 is `exe`; tier 3 is `uxe`. A weak `forceX` is added.
- **Radial:** `forceRadial` by tier.

**Interaction:**
- **Hover:** a tooltip (HTML overlay) with the full `AnnotationKey`, `Title` and status, and the neighbours highlighted.
- **Click:** select the node and open the detail panel (route `…/graph/{key}`).
- **Double-click:** focus mode, which shows the k-hop neighbourhood (k = 2, adjustable).
- **Drag** pins a node. Double-click on the background releases all pins.
- **Wheel or pinch** zooms (`d3-zoom`). **Long-press** on touch shows the tooltip.

**Toolbar** (`GraphToolbar`):
- The same filter box as the grid, which dims non-matching nodes. Type toggles are in the legend.
- Layout select, focus depth, **Fit**, **Reheat**, and **Export PNG**.

**Scale** (views hold 5,000 to 20,000 annotations):
- **Focus mode by default** above 5,000 nodes. The graph opens on the node selected in the grid, or on the most-connected subject or responsibility, with depth 2. The filter box and the legend can widen the visible set, for example `type:sbt,cnt,exe subject:northwind`.
- **Show everything** is available up to 20,000 nodes, after a notice about load time. It uses level-of-detail rendering:
  - links are drawn as one batched path, without arrowheads, below zoom 0.6;
  - names are drawn only for hovered and selected nodes and the top 50 by degree;
  - the layered layout is pre-warmed in the worker (300 ticks) before the first frame, then streamed.
- **Risk:** if phase G measures below 30 fps for 20,000 nodes on the reference laptop, the renderer is swapped for WebGL (PixiJS or regl) behind `GraphCanvas`, keeping `d3-force` and every interaction. The review records that decision.

**Accessibility:** the arrow keys move the selection to neighbours, `Enter` opens the detail, and an "Open as list" button shows the current neighbourhood in the grid.

**Time dimension:** deferred. A later slider over `[min ValidFrom, max ExpiresAt]` would show only the annotations active at time *t*, with nodes entering and leaving as in the temporal force-directed example. Status is already computed per node (section 7), so the slider only has to change `now`.

### 13. Organizations, projects, views and access

**Organizations and projects:**
- `/orgs` groups `AccessMap` keys: keys without a dot are organizations, keys with a dot are projects. Each shows the caller's role.
- **New organization** asks for the organization name and its first project, because the API creates an organization through its first project. It calls `CreateAccessPoint`, or `CreateAccount` during onboarding.
- **New project** is available to organization Owners.
- Names must match `^[a-z0-9][a-z0-9-]{1,62}$`, must not contain dots, and must not be a reserved word. Validation runs on the client and the server (companion spec E11).
- Renaming and deleting are out of scope.

**Views** (`views.vue`, companion spec E5):
- The list shows name, description and created-at. `default` is always present.
- Creating a view takes a name (same rules) and a description.
- **Compare** opens `compare.vue`.
- The `ContextSwitcher` view dropdown reads the same list. Administrators also see views that were discovered in the data but are not registered (`?discover=true`), with a **Register** action.

**Members** (organization and project, companion spec E2):
- The table shows name, username or email, role (`RoleSelect`) and a remove action.
- These rules are enforced in the UI and again by the API:
  - Only Owners can grant or revoke `Owner`.
  - The last Owner cannot be demoted or removed.
  - Administrators cannot change their own role.
- Role changes call `PUT …/members/{accountId}`. Removing calls `DELETE`, after a confirmation.
- A role legend explains what each role can do.

**Invitations** (companion spec E3):
- **Invite** opens a dialog with one or more emails, a role and an expiry (7 days by default).
- `InvitationsTable` shows email, role, status, invited by and expiry, with the actions **Resend**, **Revoke** and **Copy link**.
- The accept landing `/invitations/accept?invitation_token=…&id=…` signs in with `authkit.signIn({ invitationToken, state: { returnTo: '/invitations' } })`. This allows sign-up when AuthKit sign-up is disabled. Check that the option exists in authkit-js. If it does not, pass the token through `loginHint` and `state`.
- `/invitations` lists `GET v1/access/invitations/mine` with **Accept** and **Decline** actions. After acceptance, the user lands in the project.

**Machines** (`machines.vue`):
- The table shows `Id`, `Scope`, `AnnotationKey` restriction, `CredentialKind`, `CertificateThumbprint` and `LastRenewalAt`, with an expiry badge.
- **Create** (`MachineCreateDialog`) takes:
  - `Scope`, a select with an explanation per scope.
  - An optional `AnnotationKey`, autocompleted from the catalog.
  - `CertificateLifetimeDays`, shown when the project policy issues certificates.
- **Reset** asks for confirmation that names the effect ("existing credentials stop working"). **Delete** requires typing the id.
- Create and Reset responses go to **`OneTimeSecretDialog`**, which shows the API key with copy, `Certificate` as **Download .pfx** (base64 → `Blob`, `application/x-pkcs12`), `CertificatePassword` with copy, and `TokenEndpoint` / `TokenScope` for ClientCredentials. It also shows ready-to-paste snippets (`curl`, `.env`, `sform` config). The dialog cannot be dismissed until the user ticks "I have stored these credentials". Secrets are never written to the query cache, the store or the logs.

**Settings** (`settings.vue`):
- Machine authentication policy (`AccessPoint.MachineAuthentication`): `CredentialKind` and `CertificateLifetimeDays`, saved with `SetMachineAuthentication` (Administrator).
- **Download CA certificate** (`GetCertificateAuthority`, PEM). The SDK fetcher returns non-JSON responses as text, so the generated `getCertificateAuthority()` yields the PEM string.

**Shared certificates** (`certificates.vue`, Administrator):
- The table shows `Label`, `Thumbprint`, `NotBefore`, `NotAfter` and `IsRevoked`. A warning badge appears when a certificate expires within 30 days.
- **Issue** takes `Label` and `LifetimeDays`, and the result goes to `OneTimeSecretDialog`.
- **Revoke** requires typing the thumbprint's first 8 characters.

**Role-aware UI** (`useRole`, `RoleGate`): the effective role for the current project comes from `AccessMap[`${org}.${project}`]`, and for an organization from `AccessMap[org]`. Disabled actions show a tooltip naming the required role.

| Capability | Minimum role |
| --- | --- |
| Read annotations, configurations, templates, schemas | Reader |
| Edit annotations, configurations, templates, schemas; machines | Contributor |
| Resolved configuration with secrets | ContributorWithSecrets |
| Members, invitations, shared certificates, machine policy, views (create) | Administrator |
| Grant Owner, create projects in the organization | Owner |

### 14. Templates and schemas sections

**Templates** (`templates.vue`):
- Lists the 7 annotation types with presence, version and author, from the companion spec's E8.
- `templates/[type].vue` has the tabs Edit (`DocumentEditor`, JSON or YAML; save with `PatchTemplate` and JSON Patch with `test` ops, the same as configurations), Versions, and Delete (confirm).
- It also shows "Applies to N configurations" from the catalog count of that type.

**Schemas** (`schemas.vue`):
- Three sections: **Type schemas** (7 rows), **Annotation schemas**, and **Descendant-type schemas**, all listed by companion spec E7. Each row links to the annotation it belongs to.
- The schema editor (`SchemaDetail`):
  - A split view with `DocumentEditor` on the left, validated against the JSON Schema meta-schema (draft-07, or 2020-12 when `$schema` says so).
  - **Form preview** on the right (`SchemaFormPreview`), which renders `SchemaForm` live from the schema being edited, with sample data.
  - On phones the two panes become tabs.
- Saving uses `Set*Schema` (PUT, whole document).
  - `409` lists the non-compliant configurations (`AnnotationKey`, `ViewName`, `Errors`) in a table that links to each configuration.
  - **Save anyway** (`force=true`) asks for confirmation first.
- **Combined schema** for an annotation (`/definition`) is shown read-only, with the applied layers listed in order.
- Versions and diffs reuse section 11.3.

### 15. Responsiveness and accessibility

**Breakpoints** (Tailwind):

| Breakpoint | Layout |
| --- | --- |
| `xl` (≥ 1280 px) | Sidebar, main and detail side by side, all resizable |
| `lg` (≥ 1024 px) | Sidebar collapsed to the icon rail by default |
| `md` (≥ 768 px) | Detail as a right slideover (90 % width) |
| `< md` | Single column, list cards, full-screen detail with a back button, bottom-sheet filters |

**Touch targets** are at least 40 px in comfortable density. Resize handles are hidden on touch.

**Accessibility:**
- Reka UI primitives give focus traps and ARIA roles.
- The grid follows the `grid` role pattern with roving focus.
- Focus returns to the originating row when the detail closes.
- Color is never the only signal: badges have an icon and a code, and status has text.
- `prefers-reduced-motion` turns off graph animation and panel transitions.

**Internationalization:** v1 is English only. Dates and numbers use `Intl` with the browser locale.

### 16. Security

- Access tokens stay in memory, inside authkit-js. Deployed environments use the custom AuthKit domain, so the refresh token is in an HttpOnly cookie on that domain. Only `localhost` uses `devMode` with `localStorage` (section 3).
- No secrets are persisted:
  - One-time credentials and resolved configurations bypass the cache.
  - `preferences` stores only UI state.
  - Clipboard copies of secrets show a "clipboard contains a secret" toast.
- **CSP** (`staticwebapp.config.json` `globalHeaders`):

  ```
  default-src 'self'
  script-src 'self'
  style-src 'self' 'unsafe-inline'
  img-src 'self' data: https:
  font-src 'self'
  connect-src 'self' <apiBaseUrl origin> https://api.workos.com https://<authApiHostname>
  worker-src 'self'
  frame-ancestors 'none'
  base-uri 'self'
  form-action 'self' https://<authkit domain>
  ```

  It also sets `Referrer-Policy: strict-origin-when-cross-origin` and `X-Content-Type-Options: nosniff`.
- Destructive actions use type-to-confirm. Errors never render server stack traces.
- The admin origin must be added to the Function App's CORS (companion spec E12). The UI sends bearer tokens without cookies, so `Access-Control-Allow-Credentials` is not needed.

### 17. Hosting, local development and CI

- **Static hosting:** Azure Static Web Apps, the same as the docs site (decided).
  - `npm run build` → `dist/`.
  - A custom domain for the app (for example `admin.42for.net`) and a **custom AuthKit domain** (for example `auth.42for.net`, a DNS CNAME configured in the WorkOS dashboard). The latter goes into `app-config.json` as `authApiHostname` for every non-local environment.
  - `staticwebapp.config.json` sets `navigationFallback: { rewrite: '/index.html', exclude: ['/assets/*', '/*.{js,css,svg,png,ico,json,woff2,txt}'] }` and the headers from section 16. Hashed files under `/assets/` get `Cache-Control: public, max-age=31536000, immutable`. `index.html` and `app-config.json` get `no-cache`.
  - The deploy step writes `app-config.json` for each environment.
- **GitHub Actions** `.github/workflows/ui-admin.yml`:
  - Triggers on pushes and PRs touching `src/Platform/ui.admin/**` or `src/Platform/Storyteller/sdk.typescript/**`.
  - Jobs: `npm ci` (SDK, then UI), a check that the committed generated SDK matches the spec (`npm run generate` then `git diff --exit-code`), `oxlint` and `eslint`, `vue-tsc`, unit tests, `vite build`, Playwright with mocked API (Chromium, desktop and mobile viewports), then deploy from `main`.
- **Local:**
  - `dotnet run --project src/Platform/Aspire.Host/src` starts Cosmos and `api-functions`, and now also `ui-admin` through `Aspire.Hosting.JavaScript` with `AddViteApp`. Check the exact Aspire 13 API at implementation time. It passes `VITE_API_BASE_URL` from the `api-functions` endpoint and runs `npm run dev` on port 5173.
  - Without Aspire: `npm run dev` with `.env.local` (`VITE_API_BASE_URL=http://localhost:7071/api`).
  - A WorkOS staging environment with `http://localhost:5173` redirects is required. The API's Debug `DEV_AUTH` only decodes tokens, but the UI still signs in for real.
- **E2E mock auth:** only in the Vite `e2e` mode (`vite build --mode e2e`), `createAuth` returns a fake user and token, and MSW (`setupWorker` with the Orval-generated handlers plus scenario overrides) serves the API. The code is behind `import.meta.env.MODE === 'e2e'` checks and dynamic imports, so production builds drop it.

### 18. Testing

| Layer | Tool | Scope |
| --- | --- | --- |
| Utils | Vitest | `filter-parser` (grammar, negation, OR, quoting), `tree-projection` (both perspectives, missing parents, filtered ancestor chains), `annotation-status` (time zones, edges), `json-yaml` (round-trip, key order, invalid input, big-int warning), `useJsonPatch` (`test` ops, arrays, no-op) |
| Stores and composables | Vitest (happy-dom) + MSW (`setupServer` with the generated handlers) | catalog parallel paging and local upserts, IndexedDB snapshot (`fake-indexeddb`), session (401 retry → sign-in), role gating, unsaved-changes guard, `invalidateByPath` usage after mutations |
| Router | Vitest | guards: public routes, account 404 → onboarding or invitations, context 403, `lastContext` |
| Components | `@vue/test-utils` with the Nuxt UI Vue plugin installed in the test setup | `AnnotationForm` (VeeValidate + Zod validation, read-only fields), `LabelsInput` (suggest, create), `ValuesEditor`, `SchemaForm` renderers (each tester; binding detection; inherited placeholders), `OneTimeSecretDialog` (cannot close before ack), `DocumentEditor` format switching |
| E2E | Playwright + MSW, desktop 1440×900 and mobile 390×844, light and dark | Sign-in redirect → onboarding → project; grid filter → detail edit → save; configuration form save, then 409 → force; conflict dialog; version compare; invite member; create machine → download `.pfx`; graph click → detail |
| Visual | Playwright screenshots | Key pages in both themes and both viewports (tolerance 0.2 %) |

### 19. Delivery phases

Each phase ends with a review in `docs/Platform/ui.admin/specs/reviews/` (or `docs/Platform/Storyteller/specs/reviews/` for SDK and API phases).

| Phase | Content | Depends on |
| --- | --- | --- |
| **0** (companion) | API extensions E1-E8 and E11-E13, OpenAPI `v0.9.x`, C# SDK regeneration | – |
| **A** (companion) | `sdk.typescript`: Orval generation (fetch client, Vue Query composables, Zod, MSW), fetcher, helpers, tests, esproj, slnx | 0 for the final regeneration. Can start on `v0.8.89`. |
| **B** | Scaffold: Vite, Vue Router 5 file routes and guards, Nuxt UI Vite plugin, oxlint and ESLint, Vitest, esproj, slnx, mrepo, app-config, AuthKit, shell (sidebar pin/collapse, resizable panels, theme), context switcher, onboarding (VeeValidate + Zod), orgs and projects, account page | A |
| **C** | Annotations: catalog, grid and tree, filter language, command palette, detail panel (form, JSON, YAML, labels, values, breadcrumb, lineage), create, delete and bulk | B |
| **D** | Editors (CodeMirror 6), configuration detail (edit, effective, resolved), `SchemaForm` and renderers, JSON Patch save, conflicts, 409 handling, versions and diffs, view compare | C |
| **E** | Templates and schemas sections, schema form preview | D |
| **F** | Access: members, invitations, machines, policy, CA, shared certificates | B, 0 |
| **G** | Graph (D3, worker, layouts, focus mode) | C |
| **H** | Hardening: mobile pass, accessibility, E2E and visual suites, Aspire integration, SWA workflow, product docs page `docs/42for.net/platform/admin-ui.md` | all |

## Files to create / change

| Path | Change |
| --- | --- |
| `src/Platform/ui.admin/version.json` | new |
| `src/Platform/ui.admin/src/**` | new Vue 3 + Vite application (sections 1-18) |
| `src/Platform/ui.admin/src/ui.admin.esproj` | new |
| `src/Platform/ui.admin/README.md` | new: setup, WorkOS checklist, scripts, architecture |
| `src/Platform/Storyteller/sdk.typescript/**` | companion spec |
| `42.mono.slnx` | add both `.esproj` files |
| `mrepo.json` | add the `src/Platform/ui.admin` item |
| `src/Platform/Aspire.Host/src/AppHost.cs`, `Aspire.Host.Platform.csproj`, `Directory.Packages.props` | add `ui-admin` resource (`Aspire.Hosting.JavaScript`) |
| `.github/workflows/ui-admin.yml` | new |
| `docs/42for.net/platform/admin-ui.md` | new product page (phase H) |

## Decisions

1. SPA with Vue 3.5 + TypeScript, Vite and Vue Router 5 (file-based, typed routes). No Nuxt and no SSR. Static hosting on Azure Static Web Apps, with runtime `app-config.json`.
2. Nuxt UI v4 through its Vite and Vue plugins, as the only component library. Tailwind v4. Inter and JetBrains Mono.
3. Grid and tree use TanStack Table via `UTable`, with TanStack Virtual.
4. One editor abstraction (`CodeEditor`, `DocumentEditor`, `DiffEditor`), implemented with CodeMirror 6 everywhere.
5. Version diffs load both full documents for a side-by-side view. The API `DiffResult` supplies the change counts and jump-to-change positions.
6. JSON Forms core with our own Nuxt UI renderers for schema forms. Ajv for client-side validation. The server stays authoritative. App forms use VeeValidate with Zod 4 schemas built on the generated ones.
7. Pinia for client state. TanStack Query v5 for server state, with composables, query keys, Zod schemas and MSW mocks generated by Orval in the SDK. The catalog is a Pinia store with a raw `Map`, loaded through 7 parallel per-type streams and cached in IndexedDB. It is sized for 5,000 to 20,000 annotations per view.
8. Configuration saves are RFC 6902 patches with `test` guards. A full replace is never sent.
9. D3 force on Canvas with a worker. Layered layout. Focus mode by default above 5,000 nodes. No time slider in v1.
10. All API extensions in the companion spec (E1-E8, E11-E13) are delivered before the UI work. E9 and E10 are not needed at this scale.
11. Invitations are Storyteller documents. WorkOS sends the email.
12. Deployed environments use a custom AuthKit domain. `devMode` is for `localhost` only.
13. npm, Node 24 LTS, English only.
14. Tests sit inside the npm package (`src/test`).
15. Linting runs oxlint first, then ESLint with `eslint-plugin-vue` for templates. `eslint-plugin-oxlint` turns off the rules oxlint already covers.

## Resolved questions (2026-10-09)

| # | Question | Answer |
| --- | --- | --- |
| 1 | Nuxt version | Nuxt 4, superseded by question 11 |
| 2 | UI library | Nuxt UI v4 |
| 3 | Data grid | TanStack Table through Nuxt UI, with virtual scrolling |
| 4 | Editor | CodeMirror 6 everywhere |
| 5 | Diff data | Both full versions side by side, with API stats and jump-to-change |
| 6 | Annotations per view | 5,000 to 20,000 |
| 7 | Graph time dimension | Static graph now, time slider later |
| 8 | API changes | All of them, before the UI work |
| 9 | Invitations | Storyteller invitations, with WorkOS sending the email |
| 10 | Hosting and AuthKit domain | Azure Static Web Apps with a custom AuthKit domain |
| 11 | Framework (second round) | Plain Vue 3.5 + Vite + Vue Router 5, no Nuxt |
| 12 | UI library on plain Vite | Nuxt UI v4 (PrimeVue 5 + Tailwind considered) |
| 13 | Data fetching and SDK generator | TanStack Query + Orval (Pinia Colada + Hey API considered) |

Things to verify during implementation:
- whether authkit-js `signIn` accepts `invitationToken` (section 13);
- the exact Aspire 13 JavaScript hosting API (section 17);
- whether the backend creates missing ancestors automatically (section 8);
- the Nuxt UI Vite plugin options in Vue-only projects (sections 1 and 4);
- whether VeeValidate v5 is stable, or v4 works with Zod 4 (Technology Choices).

## Out of scope / known limitations

- Entra ID or Keycloak sign-in in the UI. The UI refuses non-AuthKit deployments with a clear message.
- Renaming or deleting organizations, projects and views. Copying a view.
- Billing (see the separate billing spec). A "Billing" sidebar slot is reserved but hidden.
- Naming the ancestor that provides an inherited configuration value.
- Real-time collaboration and push updates (there is no change feed in the API).
- Supervisor events and Scheduler UI.
- YAML comments are not preserved, because storage is JSON.
- The graph time slider (deferred, see section 12).
- Server-side annotation search and a labels endpoint (companion spec E9 and E10). Not needed while views stay under about 20,000 annotations.
