# Stop Full Page Reloads When Vite Optimizes Dependencies

Linear: [424-29](https://linear.app/424fun/issue/424-29/resolve-reloading-of-entire-application-when-dependencies-are)

## Overview

This review covers the whole spec [2026-10-13 Stop Full Page Reloads When Vite Optimizes Dependencies](../2026-10-13%20Stop%20Full%20Page%20Reloads%20When%20Vite%20Optimizes%20Dependencies.md), delivered in one phase. The admin UI dev server now finds every dependency of the app when it starts and pre-bundles all of them at once. Moving to another page no longer re-optimizes the dependencies, so Vite no longer reloads the whole app. Production builds, the e2e build and `vite preview` are unchanged.

## What Was Done

### 1. Crawl the whole app (`vite.config.ts`)

`src/vite.config.ts` has the planned `optimizeDeps` block, placed between `resolve` and `worker`:

- `entries: ['index.html', 'app/**/*.{vue,ts}', '!app/**/*.d.ts']`: every source file is an entry for the scan, so it covers the pages behind the virtual `vue-router/auto-routes` module and the auto-imported components, composables and stores;
- `include: ['vue-router/experimental']`: only the generated routes import it.

The comment above the block explains why the default crawl from `index.html` is not enough. No other file needed a change; the Nuxt UI exclusion of `@nuxt/ui` is kept.

### 2. Tests

- **Unit** (`test/unit/build/dependency-prebundling.test.ts`, new, Vitest `node` environment), two tests. `beforeAll` creates a Vite dev server from `vite.config.ts` in middleware mode (mode `development`, so the config of `npm run dev`), with a temporary `cacheDir`, `optimizeDeps.force` and no HMR or WebSocket server. It then calls `depsOptimizer.init()`, waits for `depsOptimizer.scanProcessing`, and takes the dependencies from `metadata.optimized` and `metadata.discovered`. A dependency is in one of the two, depending on whether its bundle has already been written.
  - *finds every package the app imports*: the bare imports of `app/**/*.{vue,ts}` (without `.d.ts`; `import type` skipped) are read with a regular expression. Each must be among the dependencies, except `vue-router/auto-routes`, `@nuxt/ui/*` and JSON files. A failure lists each missing import with the first file that imports it.
  - *finds vue-router/experimental, which only the generated routes import*.

  Both tests fail with the old config: the first lists 25 missing imports (CodeMirror, d3, JSON Forms, VeeValidate, the SDK's `vue-query` and `zod` entries, …), and `vue-router/experimental` is the 26th dependency the spec counted. Both pass with the change. The file lists the app with `fs/promises` `glob`, which is stable from Node 22.17; on Node 23, Node prints an experimental warning.
- `test/setup.ts`: the `afterEach` hook calls `globalThis.localStorage?.clear()` and `globalThis.sessionStorage?.clear()`, with a comment, because the `node` environment has no Web Storage.
- **Manual**, with an empty cache, `vite --mode e2e` and a Playwright script in the installed Chrome that moves through 30 routes with `router.push`:
  - before: Vite optimized dependencies nine more times after the first bundle and logged `optimized dependencies changed. reloading` each time. The page loaded twice for the first route and reloaded at least six more times;
  - after: one page load in total, no reload, and no optimizer message in the Vite log. `node_modules/.vite/deps` held all 40 dependencies after the start.
- **Visual:** not run. `optimizeDeps` does not affect `vite build`, so the screenshots cannot change.

### 3. Documentation

`src/Platform/ui.admin/README.md`, Getting started, has a new paragraph after the SDK reinstall note. It says that the dev server pre-bundles every package imported in `app/`, that a package imported only by a virtual module goes into `optimizeDeps.include`, and that the new unit test checks both. The user docs (`docs/42for.net/platform/admin-ui.md`) describe only how to start the dev server and did not need a change.

### Results

- `npm test`: 173 passed (20 files), including the 2 new tests.
- `npm run test:e2e` (desktop and mobile, `PLAYWRIGHT_CHANNEL=chrome`): 59 passed, 1 skipped.
- `npm run typecheck`, `oxlint` and `eslint` report no problems. Prettier reports no problems in the changed source files. `README.md` did not pass the Prettier check before this change either.

### Notes

- **Unrelated crash found during the manual check:** after a client-side move from a schema type page (`/acme/billing/default/schemas/type/sbt`) to another page (`/templates` or `/account`), the tab hangs until Chrome kills it (Playwright reports "Target crashed" after 3–4 minutes). Opening the target page directly works. With the old config and a warm dependency cache, the move from `/schemas/type/Subject` to `/templates` crashes the same way, so the crash was there before and only the frequent reloads hid it. It is not part of this fix; the manual check visits the schema type page last.
- The branch starts from `main`. `git merge-tree` merges it without a conflict with `feat/424-7`, `feat/424-9`, `feat/424-12`, `feat/424-13`, `fix/424-17`, `fix/424-19`, `fix/424-20`, `fix/424-21`, `fix/424-22`, `fix/424-23`, `fix/424-24`, `fix/424-25`, `fix/424-27` and `fix/424-28`. `feat/424-7`, `feat/424-12`, `feat/424-13` and `fix/424-23` also edit the README, but in other lines.
