# Name the Default View in the Navigation Menu

Linear: [424-21](https://linear.app/424fun/issue/424-21/show-default-view-when-selected-in-navigation-menu)

## Problem

The sidebar of the Administration UI (`src/Platform/ui.admin`) groups the screens of the current view (Annotations, Configurations, Graph, …) under a section label. For any view except `default`, the label names the view: **View · staging**. For the `default` view, the label is only **View**, so nothing in the navigation menu says which view is selected.

The issue asks the navigation menu to show the `default` view the same way as every other view, so it is explicit that `default` is selected.

## Current State

- **Sections:** `buildNavigation` in `app/utils/navigation.ts` builds the sidebar sections from the context (organization, project, view) and the caller's roles. The view section (line 47) special-cases the default view:

  ```ts
  label: view === 'default' ? 'View' : `View · ${view}`,
  ```

  This is the only place in the app that treats the name `default` differently for display (the mock API in `app/mock/handlers.ts` also checks it, but to set `IsDefault`).
- **Consumers of the section label:**
  - `app/components/shell/AppSidebar.vue` (line 28) renders each `section.label` as a `type: 'label'` item of `UNavigationMenu`, both in the docked sidebar and in the slideover. The icon rail (collapsed sidebar) hides labels.
  - `app/components/shell/CommandPalette.vue` (line 72) uses `section.label` as the suffix of the page items, so "Annotations" shows **View** in the default view and **View · staging** in the other ones.
- **Context switcher:** `app/components/shell/ContextSwitcher.vue` already shows the view by name, including `default`, but only from `sm` up (the view button is `hidden sm:inline-flex`). On a phone, the navigation slideover is the only place that could name the view.
- **Tests:** `test/unit/utils/shell.test.ts` (the "navigation" block) checks the section ids and the first item of the view section, but not the labels. The visual baselines in `test/e2e/__screenshots__/` for the desktop screens of the default view (annotations, annotation-detail, annotation-history, deleted, template, schemas; light and dark) show the docked sidebar with the **View** label.
- **Docs:** `docs/42for.net/platform/admin-ui.md` describes the context in the address bar and the switcher (lines 11–18), but not the sidebar label.

## Proposed Changes

### 1. Always name the view in the section label (`app/utils/navigation.ts`)

Drop the special case:

```ts
label: `View · ${view}`,
```

- The sidebar shows **View · default** in the default view, the same as **View · staging** in another one.
- The command palette shows **View · default** as the suffix of the view pages, for the same reason.
- No new styling, badge or icon: the issue asks for the default view to look the same as the others.

### 2. Unit test (`test/unit/utils/shell.test.ts`)

In the "navigation" block:

- Extend the existing test to expect the labels of the sections: `['View · default', 'Project', 'Organization', undefined]`.
- Add a test with `view: 'staging'` that expects the view section label `View · staging` and the entries under `/acme/billing/staging/…`.

### 3. Visual baselines

Regenerate the desktop baselines that show the docked sidebar in the default view (annotations, annotation-detail, annotation-history, deleted, template, schemas; light and dark) with `npm run test:visual:docker -- --update`. Mobile baselines do not show the sidebar and must not change; members and machines do not have a view section on `main` and must not change either.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`, after the paragraph about the switcher: say that the sidebar lists the screens of the view under **View · {view}**, so it always names the selected view, `default` included.
