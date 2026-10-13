# Machine Icon in the Navigation Menu

Linear: [424-27](https://linear.app/424fun/issue/424-27/change-icon-of-machines-section-in-navigation-menu)

## Overview

This review covers the whole spec [2026-10-13 Machine Icon in the Navigation Menu](../2026-10-13%20Machine%20Icon%20in%20the%20Navigation%20Menu.md), delivered in one phase. The **Machines** entry of the navigation menu now uses the lucide `computer` icon instead of `cpu`, which stays the icon of the **Unit of execution** annotation type. The other machine screens use `computer` too, so a machine looks the same everywhere and never like an annotation type.

## What Was Done

### 1. `computer` for the Machines entry (`app/utils/navigation.ts`)

The **Machines** entry of the **Project** section uses `i-lucide-computer`. `typeMeta.uxe.icon` in `app/utils/annotation-key.ts` is unchanged (`i-lucide-cpu`).

### 2. The same icon on the other machine screens

As planned, `i-lucide-cpu` is replaced by `i-lucide-computer` in:

- the Machines empty state (`app/pages/[org]/[project]/machines.vue`);
- the machine detail panel header (`app/pages/[org]/[project]/machines/[id].vue`);
- the **Create** button of `app/components/access/MachineCreateDialog.vue`.

`i-lucide-cpu` is now used only by `typeMeta.uxe`. The icon is a literal name, so the build bundles it with the other lucide icons (checked in the built `dist/assets/index-*.js`); no Iconify API call is needed.

### 3. Tests

- **Unit** (`test/unit/utils/navigation.test.ts`, new file): the test "does not use the icon of an annotation type for an entry" builds the navigation of a project view for an Owner (all entries visible), expects the **Machines** entry to have `i-lucide-computer`, and expects no entry to use any `typeMeta[*].icon`. It fails with the old `i-lucide-cpu` and passes with the change.

  Deviation from the spec: the spec put the test in `test/unit/utils/shell.test.ts`, next to the other `buildNavigation` tests. The open branches `fix/424-17` and `fix/424-22` change the imports and the end of that file, so the test conflicted with both. It lives in its own file instead.
- **Visual:** `npm run test:visual:docker` passes all 40 screens with the committed baselines. The icon is 16 × 16 px in the desktop sidebar, below the 0.2 % tolerance, and the machines screenshot shows the table, not the empty state. Playwright's `--update-snapshots` only rewrites failing screens, so no baseline changed.

### 4. Documentation

No user-facing documentation describes the icons; only this review and the spec were added.

### Results

- `npm test`: 172 passed (20 files), including the new navigation test.
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`): 59 passed, 1 skipped, including the axe check of the Machines screen.
- `npm run test:visual:docker`: 40 passed.
- `npm run typecheck` and `npm run lint` (oxlint, ESLint) report no problems. Prettier reports no problems in the changed files apart from line endings, which differ the same way on `main`.

### Notes

- The branch starts from `main`. `git merge-tree` merges it without a conflict with `feat/424-7`, `feat/424-9`, `feat/424-12`, `feat/424-13`, `fix/424-17`, `fix/424-19`, `fix/424-20`, `fix/424-21`, `fix/424-22`, `fix/424-23`, `fix/424-24` and `fix/424-25`.
- `feat/424-13` lets project settings and Preferences override the type icons, and its icon picker offers `i-lucide-cpu`. The new test reads the built-in `typeMeta` (no override is applied in unit tests), so it still holds after that branch is merged. `computer` is not among the icons a user can pick for a type there, so a machine cannot be made to look like a type through the settings either.
