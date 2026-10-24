# Annotation detail descendant relations

Linear: [424-89](https://linear.app/424fun/issue/424-89/the-annotations-has-references-to-descendants)

## Overview

This review covers the whole spec [2026-10-24 Annotation detail descendant relations](../2026-10-24%20Annotation%20detail%20descendant%20relations.md), delivered in two commits after the spec. The annotation detail Relations block now lists ancestors and descendants. Descendants come from the loaded catalog with the same ancestry test as the `ancestor:` filter, grouped by type, with a short preview and links into the Annotations list.

## What Was Done

### 1. Grouped descendants

`app/utils/annotation-key.ts` adds `descendantPreviewLimit` (5), `descendantFilterQuery`, and `groupedDescendants`. Membership is a case-insensitive `ancestorsOf` hit, keys are unique, and groups follow `descendantTypeCodes` (`typeCodes` order: for a subject, usage, then context, then execution, then unit of execution). The attached plan's prose listed context before usage; the spec already keeps `typeCodes` order, and that is what shipped.

`descendantFilterQuery` quotes a value that contains a space or a comma with the same rule as `setQualifier`. It does not import `filter-parser.ts`, because that module already imports `annotation-key.ts`.

`test/unit/utils/annotations.test.ts` covers quoting, group order for every root, both diamond cases, a disabled key, a prefix neighbor that is not a descendant, mixed-case sort order (`localeCompare` with `'en'` puts the lowercase key first), a preview of 5 with a larger total, an empty unit of execution, and filter parity through `parseFilter` / `compileFilter`.

### 2. Exact counts

`reset()` in `app/stores/catalog.ts` now also clears `error`, `loadedAt`, and `fromSnapshot`. The form treats counts as exact only when the catalog scope is the open view, status is `ready`, there is no error, and the load is not a snapshot. The detail does not call `catalog.load()` or `getDescendants`. The annotations page already loads the view.

`test/unit/annotations/catalog.test.ts` asserts the three fields are cleared.

### 3. Relations in both directions

`AnnotationForm.vue` keeps the existing Relations block. The title is an `h2`. Ancestor keys and unit metadata sit under an `h3` of Ancestors. Name-collection badges are gone. Descendants is an `h3` with one wrapping row per group: the type icon, the `metaOf` label, a total badge, preview links that keep `route.query`, and "Show all N" when the group is longer than the preview. "All descendants (N)" appears when there is more than one group. Both filter links go to the annotations list with `{ q }` only.

While the scope does not match, or the catalog is idle or loading, the block says "Counting descendants…" and shows no counts. A ready catalog with an error, or any other non-authoritative settled load, keeps the groups, badges them "at least N", and notes that the catalog is incomplete. An authoritative catalog with no groups omits Descendants. Inactive keys are included. JSON and YAML still edit the raw document, so the name collections stay there. `withRelations`, `AnnotationLineage`, and `AnnotationBreadcrumb` are unchanged.

`test/unit/annotations/annotation-relations.test.ts` mounts the form and covers the ancestor link, the preview, "Show all", "All descendants", the counting state, the partial counts, and a unit of execution with no Descendants heading.

### 4. Tests and checks

Targeted annotation tests passed (the helper, catalog reset, the form, and the new relations file). `npm run lint` and `npm run typecheck` passed in `src/Platform/ui.admin/src`. A full `npm test` then failed in `compare.test.ts` and `page-memory.test.ts` under load. Those two files pass on their own (30 tests). The optional end-to-end test from the attached plan was not added.

In the mock admin UI, `sbt.northwind` shows Usage 5, Instance 3, AppRun 15 with "Show all 15", JobRun 19 with "Show all 19", and "All descendants (42)". Those labels are the project type names from `metaOf`, not the built-in words. "Show all 15" opens `/acme/billing/default/annotations?q=ancestor:sbt.northwind+type:exe` and the grid says 15 of 367, all executions. `exe.northwind.invoicing.prod` shows ancestor links and two job runs. `uxe.northwind.invoicing.prod.email` shows ancestors only.

### Deviations

- The branch `feat/424-89` is based on local `main` at `981684a3`, which is behind `origin/main`. The three-dot diff against `origin/main` is these commits.
- No end-to-end spec was added. The relations behavior is covered by the component test and a pass through the mock UI.
