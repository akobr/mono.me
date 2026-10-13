# Name the Default View in the Navigation Menu

Linear: [424-21](https://linear.app/424fun/issue/424-21/show-default-view-when-selected-in-navigation-menu)

## Overview

This review covers the whole spec [2026-10-13 Name the Default View in the Navigation Menu](../2026-10-13%20Name%20the%20Default%20View%20in%20the%20Navigation%20Menu.md), delivered in one phase. The view section of the sidebar is now labelled **View · {view}** for every view. Before, the `default` view showed only **View**. The command palette uses the same label as the suffix of the view pages, so it now shows **View · default** too.

## What Was Done

### 1. Always name the view in the section label (`app/utils/navigation.ts`)

`buildNavigation` no longer special-cases `default`. The view section label is `` `View · ${view}` ``, as planned. There was no other place in the app that hid the name `default`, so `AppSidebar.vue` (docked sidebar and slideover) and `CommandPalette.vue` needed no change. No badge, icon or style was added: the default view now looks the same as the other views.

I checked it in the mock app (`npm run dev:mock`): the sidebar on `/acme/billing/default/annotations` reads **View · default**.

### 2. Unit test (`test/unit/utils/shell.test.ts`)

- The existing "builds the sections of the current context…" test now also checks the section labels: `['View · default', 'Project', 'Organization', undefined]`.
- New test "names the selected view, the same for every view": with `view: 'staging'` and the Contributor role, the view section is labelled `View · staging`, and its first entry links to `/acme/billing/staging/annotations` without a lock.

### 3. Visual baselines

This is a change from the plan: no baseline needed an update. `npm run test:visual:docker -- --update` passed all 40 screens and wrote no new file. The extra text (" · default") changes fewer pixels than the 0.2 % tolerance of the visual suite, so the committed desktop baselines still match.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`: a new sentence says that the sidebar lists the screens of the view under **View · {view}**, so it always names the selected view, `default` included. This is a change from the plan: the sentence ends the paragraph about the switcher instead of starting a new paragraph after it. Branch `fix/424-17` adds its own paragraph right after the switcher paragraph, and a second new paragraph in the same place would conflict.

### Results

- `npm test`: 172 passed (19 files).
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`): 59 passed, 1 skipped.
- `npm run test:visual:docker -- --update`: 40 passed, no baseline changed.
- `npm run typecheck`, `oxlint`, `eslint` and Prettier report no problems.

### Notes

- Branch `fix/424-17` (keep every section in the navigation menu) also changes `app/utils/navigation.ts`, `test/unit/utils/shell.test.ts` and `admin-ui.md`, but not the view section label. `git merge-tree` merges this branch with it without a conflict. After both are merged, the members and machines screens also show the remembered view, for example **View · default**.
