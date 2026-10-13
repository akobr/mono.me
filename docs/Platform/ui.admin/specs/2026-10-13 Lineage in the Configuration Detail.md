# Lineage in the Configuration Detail

Linear: [424-25](https://linear.app/424fun/issue/424-25/make-lineage-graph-view-available-in-configuration-view)

## Problem

The annotation detail of the admin UI (`src/Platform/ui.admin`) has a **Lineage** button. It opens a small layered graph of every ancestor of the annotation, in merge order, and each ancestor links to its annotation detail.

The configuration detail does not have this button, although the order matters most there: it is the order in which a configuration inherits from its ancestors. To see where inherited values come from, or to open the configuration of an ancestor, you have to go back to the annotation, open **Lineage** there, open the ancestor's annotation and then its configuration.

The issue asks to show the **Lineage** button and graph in the configuration detail too. There, the ancestors open their **configuration**, not their annotation.

## Current State

- **Lineage component:** `app/components/annotations/AnnotationLineage.vue`.
  - Props (lines 11–13): `annotationKey` and `section?: 'annotations' | 'graph'`, `'annotations'` by default.
  - `levels` (lines 18–33) walks `getDirectAncestors` level by level, the level closest to the annotation first. It then reverses the levels, so the popover lists them top to bottom in merge order.
  - `link(key)` (line 35) is `/${org}/${project}/${view}/${section}/${key}`, without a query.
  - Ancestors present in the catalog are `RouterLink`s; missing ones are struck-through `span`s. The button is disabled when there is no ancestor (subjects and responsibilities).
- **Annotation detail:** `app/components/annotations/AnnotationDetail.vue`, lines 190–199, shows the breadcrumb and `<AnnotationLineage :annotation-key :section>` in one row above the tabs. `section` is `'annotations'` on the Annotations screen and `'graph'` on the Graph screen, so the links stay on the screen that hosts the panel.
- **Configuration detail:** `app/pages/[org]/[project]/[view]/configurations/[key].vue`.
  - The header (lines 33–70) has the type badge, an **Annotation ›** link, the name, the key with a copy button, the close button and the tabs (Edit, Effective, Resolved, Versions, Schema).
  - It has no breadcrumb and no lineage.
  - The tabs keep only the list filter, `{ q: route.query.q }` (line 16). The list does the same when it opens a row (`configurations.vue`, line 61).
  - The configuration detail works for any annotation key, also without own content: the Edit tab then offers to create it. So every ancestor in the catalog has a configuration page to open.
- **Breadcrumb:** `AnnotationBreadcrumb.vue` already takes an optional `query?: LocationQuery` for its links.
- **Tests:** no unit or end-to-end test covers `AnnotationLineage`. `test/e2e/accessibility.spec.ts` runs axe on `/acme/billing/default/configurations/exe.northwind.invoicing.prod`. There is no visual baseline of the configuration detail.
- **Docs:** `docs/42for.net/platform/admin-ui.md`, line 63, says that the breadcrumb and **Lineage** show the ancestors of an annotation. The Configuration section (lines 79–86) does not mention lineage.

## Proposed Changes

### 1. Lineage links to configurations (`AnnotationLineage.vue`)

- Extend `section` to `'annotations' | 'graph' | 'configurations'`. The links already use `section` as the path segment, so `'configurations'` opens `/{org}/{project}/{view}/configurations/{ancestor}`.
- Add an optional `query?: LocationQuery` prop, `undefined` by default, like `AnnotationBreadcrumb`. Links become `{ path, query }`.
- The annotation and graph details do not pass `query`, so their links do not change.
- The popover content stays the same: the heading "Ancestors, applied top to bottom", the levels, missing ancestors struck through, and the current key last.

### 2. Lineage in the configuration header (`configurations/[key].vue`)

Add a row between the title row and the tabs, at the same place as in the annotation detail:

```vue
<div class="flex flex-wrap items-center gap-2">
  <AnnotationLineage :annotation-key="key" section="configurations" :query="query" />
</div>
```

- `query` is the existing `{ q: route.query.q }`, so moving to an ancestor keeps the list filter, like the tabs and the list do.
- The link opens the ancestor's Edit tab. The other tabs are reached from there, as when you open a row in the list.
- The button is disabled for subjects and responsibilities, as in the annotation detail.

### 3. Tests

- **Unit** (`test/unit/annotations/annotation-lineage.test.ts`): mount `AnnotationLineage` with `mountScreen`, with a catalog of `sbt.northwind`, `rst.invoicing`, `usg.northwind.invoicing` and `cnt.northwind.prod` (no `exe` key needed). Expect:
  - for `exe.northwind.invoicing.prod` and `section: 'configurations'` with `query: { q: 'type:exe' }`: the popover lists the ancestors in merge order, and each link points to `/acme/billing/default/configurations/{ancestor}?q=type%3Aexe`;
  - with the default section and no query: the links still point to `/acme/billing/default/annotations/{ancestor}` without a query;
  - for `sbt.northwind`: the button is disabled.
- **End to end** (`test/e2e/configuration.spec.ts`): open the configuration of `exe.northwind.invoicing.prod`, open **Lineage**, click `sbt.northwind`. Expect the URL `/acme/billing/default/configurations/sbt.northwind`, the configuration tabs still shown (**Effective**), and **Lineage** disabled for the subject.
- **Accessibility:** the existing axe check of the configuration detail covers the new button.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`, Configuration section: add that **Lineage** in the header lists the ancestors in merge order, the order in which their configuration is inherited, and that each one opens the configuration of that ancestor.
