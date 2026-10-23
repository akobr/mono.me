# Annotation detail descendant relations

Linear: [424-89](https://linear.app/424fun/issue/424-89/the-annotations-has-references-to-descendants)

## Problem

The annotation detail form shows Relations in one direction. Ancestor keys such as `SubjectKey` and `UsageKey` are links. Descendant name collections (`UnitNames`, `ContextNames`, `ResponsibilityNames`) are badges of raw names, not links, and they are not a full descendant list.

A responsibility's `UnitNames` are direct unit names. A context's `ResponsibilityNames` identify executions, not responsibilities. A subject's two name lists are not a Cartesian product of usages and contexts. None of them include transitive descendants such as units of execution, and a large list has no total and no way to open those annotations in the Annotations view.

The detail should show both directions. Descendants are grouped by type, with a total, a short list of links, and a link to the Annotations view that filters to the whole group. A huge group must not render every key.

## Current State

The admin UI is `src/Platform/ui.admin`. The form is `src/app/components/annotations/AnnotationForm.vue` (paths below are relative to `src/Platform/ui.admin/src`).

- Relations is an inline block in Form mode, around lines 259–280. It renders when `references` has ancestor keys, name collections, or unit metadata (`references` is the computed at lines 118–137).
- Ancestor keys become `RouterLink`s to `/${org}/${project}/${view}/annotations/${key}` and keep `route.query`. Name collections become unlinked `UBadge`s. `UnitType` and `UnitDefinition` are plain text. The title is a styled paragraph, not a heading.
- JSON and YAML mode edit the whole annotation, so the raw name collections stay visible there. `withRelations` (`app/utils/annotation-relations.ts`) still fills those fields for the API. `AnnotationLineage.vue` and `AnnotationBreadcrumb.vue` are separate and stay as they are.
- The detail route `annotations/[key]` is nested in `annotations.vue`. The list page stays mounted and already calls `catalog.load(scope)` for the current view, including while a detail is open.
- `app/stores/catalog.ts` holds every non-deleted annotation in the view, including inactive ones (`includeInactive: true`). `ancestorsOf(key)` caches SDK `getAllAncestors`. `revision` publishes in-place map edits. `reset()` clears scope, status, the map, configurations, and `loadedCount`. It leaves `error`, `loadedAt`, and `fromSnapshot` as they were.
- `app/utils/filter-parser.ts` compiles `ancestor:<key>` as a case-insensitive test against `ancestorsOf`, and `type:<code>` as a type restriction. `setQualifier` quotes a value that contains a space or a comma. `app/router/guards/page-memory.ts` does not restore a remembered list when the target query is non-empty, so an explicit `q` opens as written.
- `app/utils/annotation-key.ts` already has `descendantTypeCodes`, in `typeCodes` order (`rst`, `unt`, `sbt`, `usg`, `cnt`, `exe`, `uxe`). For a subject that is usage, context, execution, then unit of execution. A unit of execution has none. `deletedWith` uses the same ancestry test. `getAllAncestors` keeps the case of the key's own segments.
- A key prefix is not a descendant test. `cnt.northwind.prod` is not a prefix of `exe.northwind.invoicing.prod`, and `unt.invoicing.worker` is not a prefix of `uxe.northwind.invoicing.prod.worker`. `key:exe.north` also matches `exe.northwind`.
- No test mounts the Relations block. `test/unit/utils/annotations.test.ts` covers `deletedWith` and `descendantTypeCodes`. `test/unit/annotations/catalog.test.ts` calls `reset()` but does not assert the leftover metadata.

The attached plan on 424-89 and the CodeRabbit summary agree on the catalog as the source and on `ancestor:` plus `type:`. The attached plan's prose lists a subject's descendants as context then usage. `descendantTypeCodes` already lists usage then context, and the service tests use that order. This spec keeps `typeCodes` order.

## Proposed Changes

### 1. Grouped descendants

Add a pure helper next to `deletedWith` in `app/utils/annotation-key.ts`.

`descendantPreviewLimit` is 5.

`groupedDescendants(rootKey, keys, ancestorsOf, limit = descendantPreviewLimit)` returns:

- `groups`: one entry per non-empty descendant type, in `descendantTypeCodes` order
- `total`: the sum of the group totals
- `query`: `ancestor:<rootKey>` for every descendant

Each group has:

- `code`: the type code from SDK `parseAnnotationKey`, not the `AnnotationType` field
- `total`: the full count
- `preview`: at most `limit` keys, sorted with `localeCompare`
- `query`: `ancestor:<rootKey> type:<code>`

Membership uses the same rule as the `ancestor:` filter: a catalog key is a member when `ancestorsOf(key)` contains `rootKey` case-insensitively. Iterate unique keys so a diamond (an execution under both its usage and its context, a unit of execution under both its unit and its execution) counts once. Skip keys that do not parse or whose type cannot descend from the root. An invalid root or a unit of execution yields no groups.

`descendantFilterQuery(rootKey, code?)` builds those queries. Quote a value that contains a space or a comma, the same rule as `setQualifier`. Do not import `filter-parser.ts` from `annotation-key.ts`: `filter-parser.ts` already imports `annotation-key.ts`.

The caller passes `catalog.byKey.keys()` and `catalog.ancestorsOf`. Do not read `UnitNames`, `ContextNames`, or `ResponsibilityNames`. Do not call `getDescendants`.

### 2. Exact counts

In the form, counts are exact only when all of these hold:

- `catalog.scope` is the context org, project, and view
- `catalog.status === 'ready'`
- `catalog.error` is empty
- `catalog.fromSnapshot === false`

`reset()` in `app/stores/catalog.ts` also clears `error`, `loadedAt`, and `fromSnapshot`. Leave the fallback merge, configuration-summary cancellation, and initial-load mutation journaling unchanged.

The annotations page already loads the current scope while the detail is open. Do not add another `catalog.load()` or another fetch.

### 3. Relations in both directions

Extend the existing Relations block in `AnnotationForm.vue`. Do not add a new section component.

- Change the title to an `h2`. Add an `h3` of "Ancestors" over the existing ancestor-key rows and the unit metadata rows.
- Remove the name-collection badge rows.
- Add an `h3` of "Descendants" when there are groups, or the catalog is still counting, or the catalog is incomplete.
- Show the block when any ancestor row, unit row, descendant group, counting state, or incomplete state exists.
- Read groups from `groupedDescendants`. Depend on `catalog.revision` and the annotation key.
- Each group is one wrapping row: a `w-24` label with the type icon and `metaOf` label, a total badge, preview `RouterLink`s to `/${org}/${project}/${view}/annotations/${key}` that keep `route.query`, and, when `total` is greater than the preview length, a "Show all N" link.
- "Show all N" and "All descendants (N)" go to `/${org}/${project}/${view}/annotations` with query `{ q }` only. "All descendants" appears when there is more than one group and uses the untyped `ancestor:` query. Do not keep `/[key]` or `mode=tree` on those links.
- Use `flex-wrap`, truncation, and monospace keys so the rows fit the narrow panel and a full-page phone width.
- When the scope does not match, or status is `idle` or `loading`, show "Counting descendants…" with `role="status"` and `aria-live="polite"`, and do not show counts.
- When the catalog is not authoritative and not counting (ready with an error, or a failed empty load), show the groups that exist, badge them "at least N", and add a short note that the catalog is incomplete. Keep the filter links.
- When the catalog is authoritative and there are no groups, omit the Descendants part.
- Include inactive annotations. The catalog already excludes deleted ones. Do not add a deleted-state check.

### 4. Tests

- Extend `test/unit/utils/annotations.test.ts`: group order for every root type, both diamond cases, a context descendant and a unit descendant that do not share the root prefix, a neighbor that shares a prefix and is not a descendant, inactive membership (the helper sees keys only, so a disabled annotation's key is still grouped), a sorted preview of 5 with an exact total, no groups for a unit of execution, and quoting. Parse each generated `q` with `filter-parser.ts`, apply it to the same fixture, and expect the same keys.
- Add Relations tests in `test/unit/annotations/annotation-relations.test.ts`, mounted with `mountScreen`. Cover ancestor rows, descendant counts and preview targets, "Show all N" only above the limit and aimed at the plain list, "All descendants", counting without counts, and "at least" when the catalog is ready with an error. Name-collection badges are gone.
- In `test/unit/annotations/catalog.test.ts`, assert that `reset()` clears `error`, `loadedAt`, and `fromSnapshot`.

Run `npm run test`, `npm run typecheck`, and `npm run lint` from `src/Platform/ui.admin/src`.
