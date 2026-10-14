# Type Names in the Configurations Grid

Linear: [424-34](https://linear.app/424fun/issue/424-34/fix-configurations-page-to-show-configured-annotation-type-names-in)

## Overview

This review covers the whole spec [2026-10-14 Type Names in the Configurations Grid](../2026-10-14%20Type%20Names%20in%20the%20Configurations%20Grid.md), delivered in one phase with three code commits. The **Type** column of the **Configurations** grid now shows the icon, color and name of each type, like the annotations grid, so a type renamed on **Settings** or in the Preferences no longer appears as its code (`exe`). Following the rule of the issue, the Deleted screen, the Deleted annotations dialog and the hover card of a graph node show the name too. The chips of the **Schemas** screen are now the only place that shows the codes; that screen did not change.

## What Was Done

### 1. The name in the Type column of the Configurations grid (`configurations.vue`)

- **Type column:** it renders `h(AnnotationTypeBadge, { type, variant: 'label' })`. The column is 160 px wide, the same as the annotations grid, and the cell has `max-width: 160px`. The column stays unsortable.
- **Phone list:** unchanged. It keeps `variant="icon"`, with the name as the title and accessible name, the same as the annotations phone list.

Deviation from the spec: **long names end with an ellipsis** (`AnnotationTypeBadge.vue`).

- A custom name can have up to 40 characters. A 39-character name ran out of the 160 px column, over the Name and Key cells. The annotations grid had the same problem since 424-13.
- The chip now has `max-w-full min-w-0`, and its text has `truncate`. A name that does not fit ends with "…", and the chip's `title` holds the whole name.
- Where there is room, nothing changes. The Deleted screen and the dialogs still show the whole name. This was checked on screenshots of both grids and the Deleted screen with long names for Execution and Subject.
- The visual baselines, which use the built-in names, did not change.

### 2. The name instead of the code everywhere else except Schemas

As planned, these chips use `variant="label"`:

- `app/pages/[org]/[project]/[view]/deleted.vue`: the Key column of the Deleted screen, on all widths;
- `app/components/annotations/DeletedKeysDialog.vue`: the Deleted annotations dialog. This replaces the 424-13 decision to keep the code there.
- `app/components/graph/GraphTooltip.vue`: the hover card of a graph node.

`schemas.vue` is not touched. `AnnotationTypeBadge` keeps `chip` as its default, and its doc comment now says that the code is shown only on Schemas.

As planned, these are not changed:

- the code in the preview of the type appearance editor;
- the "Type `exe` to confirm" field of a template's delete dialog;
- keys, the `type:` filter, documents and the binding language.

Before the spec was written, an audit read every `AnnotationTypeBadge`, `typeMeta` and `metaOf` use in `app/`. Besides the code chips, it found texts that name a type with its **built-in** name instead of the custom one. They are not codes, so they are left for a follow-up:

- the **Relations** box of the annotation form (`AnnotationForm.vue`, lines 265 and 271). Its labels come from the API member names ("Subject", "Responsibility"), and the plural rows add an "s", which gives "Responsibilitys".
- the **Tree perspective** select ("By subject", "By responsibility") in `AnnotationFilterBar.vue`;
- the prose of the filter syntax help ("executions labelled eu or us…").

### 3. Tests

- **E2E** (`test/e2e/settings.spec.ts`), a new test, "the configurations grid shows the type names of the settings". It renames Execution to "Job" on **Settings** and opens **Configurations** with `type:exe`.
  - On desktop, the Type cell of an `exe.` row reads "Job", and no cell reads `exe`. This fails before the change, which received "exe".
  - On a phone, the type icon is named "Job".
  - The test finds the row by its key (`getByRole('row', { name: /exe\./ })`), because the header has a second, empty row (a follow-up commit).
- **Unit** (new `test/unit/annotations/type-names.test.ts`), 4 tests:
  - the Deleted annotations dialog shows "Customer" for a renamed Subject;
  - the graph hover card shows "Job" for a renamed Execution;
  - a 39-character name is in the chip's title and in the truncated text, and the chip has `max-w-full`;
  - "shows the codes only on Schemas": it scans `app/**/*.{vue,ts}` for `AnnotationTypeBadge` tags and `h(AnnotationTypeBadge, { … })` calls without a `variant`, and expects only `schemas.vue`. A new chip that shows the code fails it.

  `afterEach` calls `applyTypeAppearance({})`. Without the code changes, the dialog, hover card and scan tests fail: they receive "sbt" and "exe", and the scan lists the three files.
- **Visual** (`test/e2e/visual.spec.ts`):
  - **New screen:** `configurations` (`/acme/billing/default/configurations`), with four new baselines.
  - **Deleted, mobile:** the baselines were rewritten by `--update`.
  - **Deleted, desktop:** the change was under the 0.2 % tolerance. Playwright rewrites only failing screens, so these baselines were deleted and written again, and now show "Subject" instead of `sbt`.
  - **Schemas:** unchanged.
  - One update run happened while the local e2e suite was also running. Eight unrelated baselines came back blank or shifted, `schemas-mobile-dark` among them. They were restored from `main` and not committed.
- **Results:**
  - `npm test`: 363 passed (40 files), 4 of them new.
  - `npm run typecheck`, `oxlint` and `eslint .` report no problems. Prettier reports no problems in the changed files.
  - `npm run test:visual:docker`: 52 passed, in two compare runs (before and after the ellipsis change) with no other load.
  - `npm run test:e2e` (desktop and mobile, installed Chrome), with all the commits: 119 passed and 3 skipped.
    - The run before the ellipsis change had one failure, the mobile "filters the audit trail and shows the usage" of `key-values.spec.ts`. It passed 3 of 3 times when run again alone.
  - **Port:** another session ran its own e2e suite on the same port (4174) at the same time. With `reuseExistingServer`, the suites shared, and then lost, a preview server. The runs above therefore used a temporary config (not committed) with port 4374.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`:

- the Type column paragraph says that it applies "here and on **Configurations**";
- the annotation types list adds that the type chips on **Schemas** keep showing the codes.

### Notes

- The branch starts from `main` (`ed50bf16`).
- `git merge-tree` merges it with the open branches `feat/424-33`, `feat/424-30`, `fix/424-16`, `feat/424-15` and `feat/424-14` without a conflict in the files of this branch. Their other conflicts are with `main`.
- `fix/424-32` also changes `deleted-desktop-{light,dark}.png`, for the 42 logo in the sidebar. Binary baselines cannot be merged, so whichever branch is merged second must run `npm run test:visual:docker -- --update` for the Deleted screen.
