# Breadcrumb in the Configuration Detail

Linear: [424-37](https://linear.app/424fun/issue/424-37/add-breadcrumbs-to-configuration-details)

## Problem

The annotation detail of the admin UI (`src/Platform/ui.admin`) has a row above its tabs with the breadcrumb of the annotation, for example "Subject northwind › Context prod › Execution prod". The **Lineage** button sits at the right end of the same row.

The configuration detail has no breadcrumb. Its **Lineage** button sits alone in that row, at the left. You cannot see where the configuration sits in the tree, and you cannot move to the configuration of a parent in one click.

The issue asks for the same breadcrumb in the configuration detail, with **Lineage** at the right of the row, as in the annotation detail.

## Current State

- **Annotation detail:** `app/components/annotations/AnnotationDetail.vue`.
  - `perspective` (lines 57–59) is `responsibility` when the URL has `?by=responsibility`, and `subject` otherwise.
  - Lines 191–200 are the row:

    ```vue
    <div class="flex flex-wrap items-center gap-2">
      <AnnotationBreadcrumb :annotation-key :perspective :query="route.query" :section class="min-w-0 flex-1" />
      <AnnotationLineage :annotation-key :section />
    </div>
    ```

    The breadcrumb takes the free width (`flex-1`), so **Lineage** ends up at the right. `min-w-0` lets the breadcrumb wrap inside itself on a phone instead of pushing the button to a new line.
- **Breadcrumb:** `app/components/annotations/AnnotationBreadcrumb.vue`.
  - Props (lines 13–22): `annotationKey`, `perspective`, `query?: LocationQuery` and `section?: 'annotations' | 'graph'`, `'annotations'` by default.
  - `chain` (lines 27–43) walks `parentKey` (`utils/tree-projection.ts`) from the key up to the root of the perspective. Each item links to `/${org}/${project}/${view}/${section}/${key}` with `query`.
  - The last item is the current one: a `span` with `aria-current="page"`. Ancestors missing from a loaded catalog are struck through, without a link.
  - No unit test covers the component.
- **Lineage:** `app/components/annotations/AnnotationLineage.vue` already takes `section: 'configurations'` and `query?: LocationQueryRaw` (424-25). In the configuration detail, its ancestors open their configuration and keep the list filter.
- **Configuration detail:** `app/pages/[org]/[project]/[view]/configurations/[key].vue`.
  - `query` (line 16) is `{ q: route.query.q }`. The tabs keep only the list filter, as the list does when it opens a row (`configurations.vue`, line 80).
  - The header (lines 33–74) has the type badge, an **Annotation ›** link to the annotation, the name, the key, the panel actions, the close button and the tabs.
  - Lines 69–71 are the row with only the lineage: `<div class="flex"><AnnotationLineage :annotation-key="key" section="configurations" :query="query" /></div>`.
  - The configuration detail opens for any key in the catalog, also without own content (the Edit tab then offers to create it). So every ancestor in the catalog has a configuration page to open.
- **Configurations screen:** `configurations.vue` has no tree and no perspective switch. Its filter bar gets a fixed `perspective="subject"` (line 151). The detail never keeps `by` in its URL.
- **Tests:** `test/e2e/configuration.spec.ts` (line 30) opens **Lineage** in the configuration detail. `test/e2e/accessibility.spec.ts` runs axe on the configuration detail of `exe.northwind.invoicing.prod`. The visual suite has no configuration detail screen.
- **Docs:** `docs/42for.net/platform/admin-ui.md`. Line 94 (annotation detail) says that the breadcrumb and **Lineage** show the ancestors. Line 137 (Configuration) mentions only **Lineage**.

## Proposed Changes

### 1. Breadcrumb links to configurations (`AnnotationBreadcrumb.vue`)

- Extend `section` to `'annotations' | 'graph' | 'configurations'`, like `AnnotationLineage`. The links already use `section` as the path segment, so `'configurations'` opens `/{org}/{project}/{view}/configurations/{ancestor}`.
- Type `query` as `LocationQueryRaw`, the type `RouterLink` takes for `to.query`, as `AnnotationLineage` does. The configuration page passes `{ q: route.query.q }`, whose `q` can be `undefined`, which `LocationQuery` rejects. `route.query` from the annotation detail still fits.
- Update the doc comments: the section is the screen the links stay on, and the configuration detail links to the ancestors' configurations.
- Nothing else changes: the chain, the current item and the missing ancestors stay as they are. The annotation and graph details render the same links.

### 2. Breadcrumb row in the configuration header (`configurations/[key].vue`)

Replace the lineage-only row (lines 69–71) with the same row as in the annotation detail:

```vue
<div class="flex flex-wrap items-center gap-2">
  <AnnotationBreadcrumb
    :annotation-key="key"
    perspective="subject"
    section="configurations"
    :query="query"
    class="min-w-0 flex-1"
  />
  <AnnotationLineage :annotation-key="key" section="configurations" :query="query" />
</div>
```

- **Same layout:** the breadcrumb takes the free width and **Lineage** sits at the right of the row, on a phone too.
- **Links:** each parent opens its **configuration** (the Edit tab), not its annotation, as **Lineage** does here. They keep the list filter (`q`), like the tabs and the lineage.
- **Perspective:** always `subject`. The Configurations screen has no perspective switch, its filter bar is fixed to `subject`, and the detail does not keep `by`. Reading `?by=` would make the breadcrumb change when you switch tabs, because the tabs drop it.
- **The Annotation › link stays.** It is the way back to the annotation; the breadcrumb now leads to the parents' configurations.
- A subject or a responsibility shows only itself in the breadcrumb, and **Lineage** stays disabled, as in the annotation detail.

### 3. Tests

- **Unit** (`test/unit/annotations/annotation-breadcrumb.test.ts`, new): mount `AnnotationBreadcrumb` with `mountScreen`, a catalog of `sbt.northwind` and `usg.northwind.invoicing` (no `cnt.northwind.prod`) and the catalog status `ready`. Expect:
  - for `exe.northwind.invoicing.prod`, `perspective: 'subject'`, `section: 'configurations'` and `query: { q: 'type:exe' }`: `sbt.northwind` links to `/acme/billing/default/configurations/sbt.northwind?q=type:exe`, `cnt.northwind.prod` is struck through without a link, and the current item is `aria-current="page"` without a link;
  - with the default section and no query: the parent links to `/acme/billing/default/annotations/sbt.northwind`;
  - with `perspective: 'responsibility'`: the chain goes through `usg.northwind.invoicing`, and a `q` set to `undefined` adds nothing to the link.
- **End to end** (`test/e2e/configuration.spec.ts`): open the configuration of `exe.northwind.invoicing.prod` with `?q=northwind`. Expect:
  - the **Annotation ancestors** navigation with **Subject northwind**, **Context prod** and the current **Execution prod**;
  - **Lineage** at the right end of the same row: its right edge is the right edge of the row, and it is level with the breadcrumb;
  - a click on **Context prod** opens `/acme/billing/default/configurations/cnt.northwind.prod?q=northwind` with the configuration tabs (**Effective**).

  The suite runs on desktop and mobile, so both layouts are checked.
- **Accessibility:** the existing axe check of the configuration detail covers the new navigation.
- **Visual:** no baseline changes. The suite has no configuration detail screen, and the annotation detail does not change.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`, Configuration section: the paragraph about **Lineage** (line 137) also names the breadcrumb. Both are in the header; the breadcrumb shows the parents of the annotation, **Lineage** every ancestor in merge order, and each one opens the configuration of that ancestor.
