# Name, Type and Key as Text in the Annotation Form

Linear: [424-24](https://linear.app/424fun/issue/424-24/render-name-type-key-more-simply-than-text-boxes-in-annotation-details)

## Overview

This review covers the whole spec [2026-10-13 Name, Type and Key as Text in the Annotation Form](../2026-10-13%20Name,%20Type%20and%20Key%20as%20Text%20in%20the%20Annotation%20Form.md), delivered in one phase. The **Annotation** tab of the annotation detail no longer starts with three disabled text boxes (Name, Type, Key). It starts with one line of text: the type icon in the type color, then the whole key in monospace with the name, its last segment, in bold. On a desktop the block is about one text box shorter. On a phone it was three stacked boxes and is now one line. Long keys are no longer cut off.

## What Was Done

### 1. One line of text instead of the three boxes (`AnnotationForm.vue`)

The `grid sm:grid-cols-3` with three `UFormField` + disabled `UInput` is replaced by a `<p data-testid="annotation-identity">` with:

- `AnnotationTypeBadge variant="icon"` for `annotation.AnnotationType`: the type icon in `text-st-{code}`, with `role="img"` and the type name as `aria-label` and tooltip. Names, icons and colors set in project settings or Preferences (branch `feat/424-13`) apply here without further change, because the badge reads the shared `typeMeta` and the `--st-*` tokens.
- `KeyText size="sm" :truncate="false"` for `annotation.AnnotationKey`: the code and the other segments dimmed, the name in `font-semibold text-highlighted`, and `break-all` so a long key wraps.

As planned, there are no labels and no copy button (the header has one). The line stays the first block of the form. JSON and YAML modes are unchanged and still block changes of `Name`, `AnnotationType` and `AnnotationKey`. A short template comment explains why the fields are text.

I checked that `Annotation.Name` is always the last key segment (`AnnotationKey.Name => _segments[^1]`, set by `AnnotationFactory`), so showing the key with its last segment in bold always shows the name.

### 2. A `size` for `KeyText` (`app/components/common/KeyText.vue`)

New optional prop `size?: 'xs' | 'sm'`, `'xs'` by default, mapped to `text-xs` / `text-sm`. The other uses (detail header, create dialog, configuration page, Deleted, deleted-keys dialog) do not pass it and look the same as before.

### 3. Tests

- **Unit** (`test/unit/annotations/annotation-form.test.ts`): new `AnnotationForm` block. It mounts the form with `mountScreen` and the existing `Execution` fixture and checks that:
  - no input holds the name, type or key;
  - the identity line has the icon named `Execution` in `text-st-exe`, the key `exe.northwind.invoicing.prod` in `text-sm` and `break-all`, and `prod` as the bold segment.
- **End to end** (`test/e2e/annotations.spec.ts`): the "filters the grid, edits an annotation…" test now checks, after opening `sbt.northwind`, that the identity line reads `sbt.northwind`, holds the `Subject` icon, and that there is no **Name** text box.
- **Visual:** `npm run test:visual:docker -- --update` regenerated exactly the four `annotation-detail-{desktop,mobile}-{light,dark}.png` baselines; the other 36 screens did not change.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`: the **Annotation** tab bullet now says that the form starts with the type icon and the key, with the name in bold, and that they cannot be edited because they are part of the key.

### Results

- `npm test`: 172 passed (19 files), including the new form test.
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`): 59 passed, 1 skipped. The axe checks of the annotation detail found no serious violations.
- `npm run test:visual:docker -- --update`: 40 passed, four baselines updated (above).
- `npm run typecheck` and `npm run lint` (oxlint, ESLint) report no problems. Prettier reports no problems in the changed lines.

### Notes

- The branch starts from `main`, not from `feat/424-13`. `AnnotationForm.vue`, `AnnotationTypeBadge.vue` and `KeyText.vue` are the same on both. `git merge-tree` merges this branch with `feat/424-13`, `fix/424-17`, `fix/424-19`, `fix/424-20`, `fix/424-21` and `fix/424-23` without a conflict.
- `fix/424-22` (detail as overlay) also updates the `annotation-detail-desktop-*` baselines. Git cannot merge the two PNG files. Whichever branch merges second has to regenerate them with `npm run test:visual:docker -- --update`. The code and the e2e spec merge cleanly.
