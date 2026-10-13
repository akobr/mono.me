# Stop Full Page Reloads When Vite Optimizes Dependencies

Linear: [424-29](https://linear.app/424fun/issue/424-29/resolve-reloading-of-entire-application-when-dependencies-are)

## Problem

When the admin UI (`src/Platform/ui.admin`) runs in the Vite dev server (`npm run dev`, `npm run dev:mock`, `dev:aspire`), the whole application reloads when the user moves to some pages. The terminal shows:

```
[vite] (client) dependencies optimized: d3-quadtree, d3-selection, d3-zoom, d3-force
[vite] (client) optimized dependencies changed. reloading
```

The reload drops the state of the page (filters, unsaved form input, open dialogs) and the user lands on the page again after a white flash. It happens whenever the dependency cache (`node_modules/.vite/deps`) is new: after `npm ci` or `npm install`, after a change of `package-lock.json` or `vite.config.ts`, or with `--force`. The reloads stop only after every page that imports a new package has been visited once.

Reproduced on 2026-10-13 with an empty cache: `vite --mode e2e`, then a Playwright script that moves through 30 routes with `router.push` (client-side, like clicking a link). Vite optimized dependencies nine more times after the first bundle and reloaded the page at least six times, starting with the first page:

| Batch | Dependencies found late |
| --- | --- |
| 1 | `vue-router/experimental` |
| 2 | `@42for.net/2splatform.sdk/vue-query`, `idb-keyval` |
| 3 | `@tanstack/vue-virtual` |
| 4 | `vee-validate` |
| 5 | `@42for.net/2splatform.sdk/zod`, `@internationalized/date`, `jsonc-parser`, `yaml` |
| 6 | `d3-quadtree`, `d3-selection`, `d3-zoom`, `d3-force` |
| 7–8 | `@codemirror/*` (11 packages), `@lezer/highlight` |
| 9 | `@jsonforms/core`, `@jsonforms/vue` |

## Current State

- **Vite config:** `src/vite.config.ts` (lines 9–33) has no `optimizeDeps` option. Vite 8.3.4 then finds the dependencies to pre-bundle by crawling the imports from `index.html` → `app/main.ts`.
- **Pages are behind a virtual module:** `app/router/index.ts` (line 2) imports `routes` from `vue-router/auto-routes`, a virtual module that the `VueRouter()` plugin (line 12 of the config) generates from `app/pages`. The dependency scanner resolves imports through the plugins, but a resolved id that is not a file is treated as external (`shouldExternalizeDep` in `vite/dist/node/chunks/node.js`). The scanner therefore never reaches the pages or anything they import. The generated module also imports helpers from `vue-router/experimental`, which no source file imports.
- **Components and composables are auto-imported:** the Nuxt UI plugin registers `app/components` (line 17) and `app/composables` and `app/stores` (line 16). A page uses `<CodeEditor>` or `useCatalog()` without an import statement, so even a scan of the pages would not reach the components that import CodeMirror, d3 or JSON Forms.
- **Result:** at start Vite pre-bundles 14 dependencies; 26 more are found only when the browser first requests a module that imports them. Each find re-bundles the dependencies. The re-bundled chunks have new hashes, so the modules already in the page are stale and Vite sends a full reload.
- **Not affected:**
  - Nuxt UI excludes `@nuxt/ui` from pre-bundling (`optimizeDeps.exclude` in its unplugin). Its own dependencies (`reka-ui`, …) are imported from files in `node_modules`, and Vite does not optimize imports whose importer is in `node_modules`, so they never trigger a reload.
  - JSON imports (`ajv/dist/refs/*.json` in `app/utils/schema-validation.ts`) are never pre-bundled.
  - `optimizeDeps` applies only to the dev server, so `vite build`, `build:e2e` (Playwright) and `vite preview` are unchanged. `vitest.config.ts` merges the Vite config, but Vitest does not run the dependency optimizer by default.

## Proposed Changes

### 1. Crawl the whole app (`vite.config.ts`)

Add to the config:

```ts
  // The dev server pre-bundles the dependencies it finds by crawling from index.html. The pages sit behind the
  // virtual vue-router/auto-routes module and the components are auto-imported, so the crawl would miss most of
  // the app, and every page that imports a new package would re-optimize the dependencies and reload the app.
  optimizeDeps: {
    entries: ['index.html', 'app/**/*.{vue,ts}', '!app/**/*.d.ts'],
    // Imported only by the generated routes.
    include: ['vue-router/experimental'],
  },
```

- Explicit `entries` replace the default HTML crawl, so `index.html` stays in the list.
- `app/**/*.{vue,ts}` makes every source file an entry, which covers the pages, the auto-imported components, composables and stores, the mock API and the graph worker. The generated `.d.ts` files only hold types and are skipped.
- `include` adds the one dependency that only a virtual module imports.

Rejected alternatives:

- **A list of every dependency in `optimizeDeps.include`:** it would have to be updated with every new package; the glob follows the code.
- **`optimizeDeps.holdUntilCrawlEnd`** (already `true` by default): it waits only for the static imports of the first page; lazily loaded routes still find new dependencies later.

With the change, Vite pre-bundles all 40 dependencies at start (about 1.6 s for the scan and the bundle on the development machine, compared with 1.4 s for the 14 found today).

### 2. Tests

- **Unit** (new `test/unit/build/dependency-prebundling.test.ts`, Vitest `node` environment): creates a Vite dev server from `vite.config.ts` in middleware mode with a temporary `cacheDir` and `optimizeDeps.force`, runs the dependency scan, and checks that
  - every bare import of `app/**/*.{vue,ts}` (without `.d.ts`) is among the discovered dependencies, except the ones Vite never pre-bundles: the virtual `vue-router/auto-routes`, `@nuxt/ui/*` (excluded by Nuxt UI) and JSON files;
  - `vue-router/experimental` is discovered.

  The first check fails with today's config (26 imports are missing).
- `test/setup.ts` clears `localStorage` and `sessionStorage` after every test; neither exists in the `node` environment, so the calls get optional chaining.
- `npm test`, `npm run typecheck` and `npm run lint` must pass; `npm run test:e2e` must still pass.
- **Manual:** the reproduction above with an empty cache must show no `optimized dependencies changed. reloading` message and no full page load after the first.

### 3. Documentation

Add a short note to `src/Platform/ui.admin/README.md` (Getting started): the dev server pre-bundles every package imported in `app/`, and a package that only a virtual module imports must be added to `optimizeDeps.include`.
