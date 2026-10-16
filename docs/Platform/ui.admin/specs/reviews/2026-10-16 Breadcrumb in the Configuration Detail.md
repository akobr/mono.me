# Breadcrumb in the Configuration Detail

Linear: [424-37](https://linear.app/424fun/issue/424-37/add-breadcrumbs-to-configuration-details)

## Overview

This review covers the whole spec [2026-10-16 Breadcrumb in the Configuration Detail](../2026-10-16%20Breadcrumb%20in%20the%20Configuration%20Detail.md), delivered in one phase.

The configuration detail now has the same row as the annotation detail, between the title and the tabs. The breadcrumb takes the free width, for example "Subject northwind › Context prod › Execution prod", and **Lineage** sits at the right end of the row, on desktop and on a phone. Each parent in the breadcrumb opens its **configuration**, as the ancestors in **Lineage** do, and keeps the list filter (`q`). The annotation and graph details do not change.

## What Was Done

### 1. Breadcrumb links to configurations (`AnnotationBreadcrumb.vue`)

- `section` is now `'annotations' | 'graph' | 'configurations'`, the same union as `AnnotationLineage`. Its doc comment says that the configuration detail links to the ancestors' configurations. The links already used `section` as the path segment, so nothing else in the component changes.
- `query` is now `LocationQueryRaw` instead of `LocationQuery`. The configuration page passes `{ q: route.query.q }`, whose `q` can be `undefined`. The annotation detail still passes `route.query`, which fits the new type, so its links resolve to the same URLs as before.
- The runtime code is the same: the chain, the current item (`aria-current="page"`) and the struck-through missing ancestors do not change.

### 2. Breadcrumb row in the configuration header (`configurations/[key].vue`)

The `<div class="flex">` that held only **Lineage** is now the row of the annotation detail, with the same classes:

- `<div class="flex flex-wrap items-center gap-2">`;
- `AnnotationBreadcrumb` with `perspective="subject"`, `section="configurations"`, `:query="query"` and `class="min-w-0 flex-1"`;
- the existing `AnnotationLineage`, unchanged.

A one-line template comment says the row is the one of the annotation detail and that the screen has no perspective switch, which is why the perspective is fixed to `subject`.

- **Layout:** the breadcrumb grows, so **Lineage** ends the row. At 390 px the breadcrumb wraps inside itself ("Execution prod" on a second line) and **Lineage** stays at the right, as in the annotation detail. Screenshots of both headers at 1440 px and 390 px were compared side by side: the rows match.
- **Annotation › link:** kept, as planned. It is the way back to the annotation; the breadcrumb leads to the parents' configurations.
- **Perspective:** always `subject`, as planned. The detail's `query` keeps only `q`, so `?by=` never reaches it from the list, the tabs or the configuration link of the annotation detail.

### 3. Tests

- **Unit** (`test/unit/annotations/annotation-breadcrumb.test.ts`, new). The component had no unit test before. The tests mount `AnnotationBreadcrumb` with `mountScreen`, a catalog of `sbt.northwind` and `usg.northwind.invoicing`, and the catalog status set to `ready`, so missing ancestors are struck through. Three cases:
  - `section: 'configurations'` and `query: { q: 'type:exe' }`: **Subject northwind** links to `/acme/billing/default/configurations/sbt.northwind?q=type:exe`, **Context prod** is struck through without a link, and **Execution prod** is the current item without a link;
  - default section and no query: the parent links to `/acme/billing/default/annotations/sbt.northwind`;
  - `perspective: 'responsibility'` with `query: { q: undefined }`: the chain is **Responsibility invoicing** (missing), **Usage invoicing** and **Execution prod**, and the usage links to its configuration without a query string.
- **End to end** (`test/e2e/configuration.spec.ts`): "the breadcrumb opens the configuration of a parent and keeps Lineage at the right". It sits right after the Lineage test of the same file. It opens `exe.northwind.invoicing.prod` with `?q=northwind` and expects:
  - the **Annotation ancestors** navigation with the links **Subject northwind** and **Context prod**, and the current **Execution prod**;
  - **Lineage** at the right end of the row: its right edge is within 1 px of the row's right edge, it starts after the breadcrumb, and the two overlap vertically;
  - a click on **Context prod** opens `/acme/billing/default/configurations/cnt.northwind.prod?q=northwind`, with the **Effective** tab, and the current item becomes **Context prod**.

  On `main` the test would fail at its first check, because the configuration detail has no such navigation. It passes on desktop (1440 × 900) and mobile (390 × 844).
- **Accessibility:** the existing axe check of the configuration detail passes with the new navigation, on desktop and mobile.
- **Visual:** no baseline changes, as planned.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`, Configuration section: the **Lineage** paragraph now says that the header has the breadcrumb of the annotation detail, always by subject (the parents of the annotation up to its subject). It says **Lineage**, at its right, lists every ancestor in merge order, and that each ancestor, in the breadcrumb or in **Lineage**, opens its own configuration.

The sentence about the annotation detail ("The breadcrumb and **Lineage** show its ancestors in merge order") is left as it is. It is older than this change, and `feat/424-33` edits the lines right after it.

### Results

- `npm run typecheck`, `oxlint` and `eslint` report no problems. Prettier reports no problems in the changed files.
- `npm test`: 433 passed (48 files), including the 3 new breadcrumb tests.
- `npm run test:e2e` with `PLAYWRIGHT_CHANNEL=chrome`: 143 passed and 5 skipped, desktop and mobile.
- `npm run test:visual:docker` (compare only): 48 passed and 4 failed. The four failures are `configurations` and `key-values`, desktop light and dark. `origin/main` fails the same four in the same container: their baselines predate the Export and Import buttons in the top bar. This change does not touch those screens, so the baselines are not regenerated here. A first run, made while the end-to-end suite was running, also missed `annotation-detail` and `annotation-history` on desktop light. These were unstable screenshots under load, and they passed in the rerun.

### Notes

- **Code review.** Three agents reviewed the change: correctness and behaviour parity, layout and accessibility, and tests and docs. Two skeptics then checked every finding.
  - The first two found nothing.
  - The third raised two low findings, and both skeptics refuted each. The new unit tests pass on `main` too, because the component's runtime does not change; reverting its prop types is still caught by `vue-tsc` on the configuration page and by the end-to-end test. The second finding was the docs wording. It was refuted, but the paragraph now says what the breadcrumb shows, as the spec planned.
- **Other branches.** The branch starts from `main`. `git merge-tree` merges it with `fix/424-35` and `feat/424-33` without a conflict.
- **Unchanged on a phone:** the five configuration tabs do not fit in 390 px and their labels are cut ("E", "Ef", "Re"…). This was already so before the change, and the issue did not ask about it.
