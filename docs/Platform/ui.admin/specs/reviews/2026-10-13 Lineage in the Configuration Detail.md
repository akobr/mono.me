# Lineage in the Configuration Detail

Linear: [424-25](https://linear.app/424fun/issue/424-25/make-lineage-graph-view-available-in-configuration-view)

## Overview

This review covers the whole spec [2026-10-13 Lineage in the Configuration Detail](../2026-10-13%20Lineage%20in%20the%20Configuration%20Detail.md), delivered in one phase. The configuration detail now has the same **Lineage** button as the annotation detail, in a row between the title and the tabs. It opens the same layered graph of the ancestors in merge order. Each ancestor opens its **configuration**, not its annotation, and the list filter (`q`) is kept, as the tabs keep it. The annotation and graph details do not change.

## What Was Done

### 1. Lineage links to configurations (`AnnotationLineage.vue`)

- `section` is now `'annotations' | 'graph' | 'configurations'`, with a short doc comment. The links already used `section` as the path segment, so `'configurations'` opens `/{org}/{project}/{view}/configurations/{ancestor}` with no other change.
- New optional prop `query`, `undefined` by default. `link(key)` returns `{ path, query }`. The annotation and graph details do not pass it, so their links resolve to the same URLs as before.
- **Deviation:** the spec typed the prop as `LocationQuery`, like `AnnotationBreadcrumb`. `vue-tsc` rejected the configuration page's `{ q: route.query.q }`, whose `q` can be `undefined`. The prop is `LocationQueryRaw` instead, the type `RouterLink` takes for `to.query`. A `route.query` still fits it.
- **Small fix on the way:** missing ancestors are drawn as a struck-through `span`, and `:to` was also bound on that span. With a string it only left a stray `to="/…"` attribute in the DOM; with an object it would have rendered `to="[object Object]"`. `:to` is now set only when the ancestor is in the catalog.

### 2. Lineage in the configuration header (`configurations/[key].vue`)

A `<div class="flex">` with `<AnnotationLineage :annotation-key="key" section="configurations" :query="query" />` sits between the title row and the tabs, at the same place as the breadcrumb row of the annotation detail. The spec had `flex flex-wrap items-center gap-2`. The row holds only the button, so `flex` is enough to keep the button from stretching across the column. `query` is the existing `{ q: route.query.q }` used by the tabs. The links open the ancestor's Edit tab. The button is disabled for subjects and responsibilities.

### 3. Tests

- **Unit** (`test/unit/annotations/annotation-lineage.test.ts`, new): mounts `AnnotationLineage` with `mountScreen` and a catalog of `rst.invoicing`, `sbt.northwind` and `usg.northwind.invoicing`. `cnt.northwind.prod` is left out on purpose. Three cases:
  - `exe.northwind.invoicing.prod`, `section: 'configurations'`, `query: { q: 'type:exe' }`: the ancestors come in merge order (`rst.invoicing`, `sbt.northwind`, `usg.northwind.invoicing`, `cnt.northwind.prod`). The three present ones link to `/acme/billing/default/configurations/{key}?q=type:exe`. The missing one is struck through and has no `href`.
  - Default props: the links still go to `/acme/billing/default/annotations/{key}` without a query.
  - `sbt.northwind`: the button is disabled and nothing opens.
- **End to end** (`test/e2e/configuration.spec.ts`): "Lineage opens the configuration of an ancestor and keeps the filter". It opens `exe.northwind.invoicing.prod` with `?q=northwind`, opens **Lineage** and clicks `sbt.northwind`. It then expects `/acme/billing/default/configurations/sbt.northwind?q=northwind`, the **Effective** tab, and **Lineage** disabled. The test is placed before the other tests of the file, not at the end, because `fix/424-20` appends a test at the end of the same file.
- **Accessibility:** the existing axe check of the configuration detail passes with the new button.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`, Configuration section: a new paragraph says that **Lineage** in the header lists the ancestors in merge order, the order in which their configuration is inherited, and that each ancestor opens its own configuration.

### Results

- `npm run typecheck`, `oxlint` and `eslint` report no problems. Prettier reports no problems in the changed files.
- `npm test`: 174 passed (20 files), including the 3 new lineage tests.
- `npm run test:e2e` with `PLAYWRIGHT_CHANNEL=chrome`: 61 passed and 1 skipped, desktop and mobile.
- `npm run test:visual:docker` (compare only): 40 passed. No baseline changed, because the visual suite has no configuration detail screen and the annotation detail looks the same.

### Notes

- The branch starts from `main`. `git merge-tree` merges it with every other open 424 branch without a conflict: `feat/424-7`, `feat/424-9`, `feat/424-12`, `feat/424-13`, `fix/424-17`, `fix/424-19`, `fix/424-20`, `fix/424-21`, `fix/424-22`, `fix/424-23` and `fix/424-24`.
- `fix/424-22` adds `<DetailPanelActions />` to the same configuration header, in the title row. The lineage row is below it, so the two changes do not touch the same lines.
- The lineage of the annotation detail still does not keep the query. Its breadcrumb does. The issue did not ask to change that, so it is left as it is.
