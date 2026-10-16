# Purple Accent Matching the 42 Logo

Linear: [424-39](https://linear.app/424fun/issue/424-39/change-ascent-color)

## Overview

This review covers the whole spec [2026-10-16 Purple Accent Matching the 42 Logo](../2026-10-16%20Purple%20Accent%20Matching%20the%2042%20Logo.md), delivered in one phase. The accent of the admin UI (`src/Platform/ui.admin`) is Tailwind purple instead of indigo: purple-700 `#8200db` in the light theme and purple-400 `#c27aff`, the hue of the 42 logo's purple ring, in the dark theme. Buttons, links, the active navigation item, tabs, switches, focus rings and the graph's selection ring follow it. One deviation from the spec: the accent tints of selected and highlighted rows are a fifth lighter, because purple-700 is darker than indigo-600 and the old tints pushed one type chip below WCAG AA (section 2). The accessibility suite now runs in both themes, a new test checks the accent itself, and the visual baselines are regenerated.

## What Was Done

### 1. Purple as the primary palette (`vite.config.ts`)

As planned: `ui: { colors: { primary: 'purple', neutral: 'zinc' } }`. Nuxt UI's runtime colors plugin maps `--ui-color-primary-50..950` to `--color-purple-*`, with the oklch values of `tailwindcss/colors` as the fallback. Secondary, success, info, warning and error keep the Nuxt UI defaults.

### 2. Shades for light and dark (`app/assets/css/main.css`)

- **As planned:** the light `--ui-primary` is `var(--ui-color-primary-700)`. The dark one stays `var(--ui-color-primary-400)`. The comment of the WCAG block says why light mode uses 700.
- **Not in the spec, lighter tints of selected rows.** The code review found that the selected grid row, `color-mix(in oklab, var(--ui-primary) 12%, var(--ui-bg))`, is darker with purple-700 than with indigo-600. In light mode, axe on a selected Unit row of the annotations grid reports the Unit chip at 4.49:1, where indigo gave 4.53. So the accent change itself added an AA failure. A contrast matrix rendered in Chrome covered every type and palette color, the label badge colors and the neutral text, as plain text and inside a chip's 10 % tint, on each tint, in both themes. It showed the same effect for the alpha tints: `bg-primary/10` regressed cyan text and the fuchsia, rose and red badge chips, and in dark mode the palette's indigo chip. Every tint is now a fifth lighter, and every pair has at least the contrast it had with indigo, in both themes:
  - selected grid rows (`AnnotationGrid.vue`, `configurations.vue`, `key-values.vue`): 12 % → 10 %;
  - the selected item of the annotation list (phones) and the highlighted member row (`AnnotationList.vue`, `MembersTable.vue`): `bg-primary/10` → `bg-primary/8`;
  - the current or selected row of version, schema and template lists (`KeyValueVersions.vue`, `VersionTimeline.vue`, `schemas.vue`, `templates.vue`): `bg-primary/5` → `bg-primary/4`.

  With the lighter tints, axe on a selected row of each built-in type in the annotations grid (desktop) reports no contrast failure in dark mode. In light mode it reports only Context (4.03) and Execution (4.13), which also failed with indigo (3.93 and 4.03). Tailwind 4 emits `bg-primary/8` and `bg-primary/4` as `color-mix(in oklab, var(--ui-primary) 8%, transparent)` and `4%`. The soft and subtle badges, buttons and Nuxt UI's own tints keep their `/10`, because their text is the accent itself (5.38 on elevated surfaces in light mode, 4.58 in dark mode).

### 3. The graph's fallback color (`GraphCanvas.vue`)

As planned: `cssVar(style, '--ui-primary', '#8200db')`. No indigo literal is left for the accent.

### 4. Unchanged on purpose

- **As planned:** the CodeMirror `--cm-property` indigo, the `--st-*` type tokens, the type palette, the logo, the favicons, the product name and the docs site.
- **Correction to the spec:** it says type text on a primary-tinted selected row "still passes (lowest 4.53)". That is wrong. Even with indigo, several colors were already below 4.5:1 on a selected grid row in light mode: the Context chip 3.93, the Execution chip 4.03, the palette's teal, sky, pink and rose chips, the red label badge 4.46, and dimmed text 4.04. 4.53 was the Unit chip, the lowest one that passed. After section 2 all of them are better than with indigo, but Context and Execution chips on a selected row in light mode stay below AA. Fixing them needs darker light tokens for `cnt` and `exe` (for example cyan-800 and emerald-800 in `main.css` and `builtInTypeColors`), or a selection that does not tint the row. That is a change of the type colors, outside this issue; it is left for a follow-up.

### 5. Tests

- **Accessibility in both themes** (`test/e2e/accessibility.spec.ts`): as planned, every test runs in a `light` and a `dark` `test.describe` with `test.use({ colorScheme })`, so the file has 42 tests per project instead of 21. With indigo, the dark run fails on the key value detail (desktop): the subtle primary "Standard" badge in the versions table has too little contrast. With purple it passes. Test titles now start with the scheme ("light › …", "dark › …"); nothing refers to the old titles. The axe check of the Deleted screen in `deleted.spec.ts` still runs in the light theme only. A dark run of it passes, and the screen shows no accent when axe runs.
- **The accent itself** (`test/e2e/theme.spec.ts`, new): in both schemes, it reads `--ui-primary` from the document and the computed background of the solid **New** button ("New annotation" on phones). It paints both on a 2D canvas and expects `#8200db` in light mode and `#c27aff` in dark mode, ±2 per channel. With `primary: 'indigo'` the test fails ("renders as rgb(67, 45, 215)" and "rgb(124, 134, 255)").
- **Not added:** an axe test on selected rows. It would fail on the Context and Execution chips, which this change does not touch (section 4).
- **Visual baselines:** regenerated twice with `--update-snapshots=all` in the Playwright 1.64 container, from a one-off copy of `run-in-container.mjs` outside the repository: after the accent change and again after the lighter tints. A script compared each new screenshot with the committed one pixel by pixel and sorted the changed pixels into accent hues and others.
  - **Committed (46):** every screenshot with accent pixels. That is all 26 desktop screenshots, because the active navigation item is accent on every app screen. On phones it is `annotation-detail`, `annotation-history`, `annotations`, `key-values`, `machines`, `onboarding`, `organizations`, `schemas`, `secrets` and `template`, light and dark.
  - **Also in those files:** differences that are not accent. Most desktop screenshots differ in the content (for example 12,000 to 27,000 pixels on `configurations`, `key-values` and `template`). The 10 stale desktop baselines also get the 42 logo instead of the indigo "2S" square. The committed files are what `main` plus this change renders in the container.
  - **Kept (2):** `configurations-mobile-{light,dark}` differ in 258 and 261 pixels of the navbar and in no accent pixel.
  - **Unchanged (4):** `deleted-mobile-*` and `members-mobile-*` came out identical.
- **Not changed:** `run-in-container.mjs --update` still passes a bare `--update-snapshots`, which rewrites only failing screenshots. A hue change stays under its per-pixel threshold, so this is the third change that needed the one-off copy.

### 6. Documentation

As planned:

- `src/Platform/ui.admin/README.md`, "Architecture", describes `css/main.css` as "Tailwind, Nuxt UI theme with the purple accent, fonts, type tokens".
- `docs/42for.net/platform/admin-ui.md`, "Phones, themes and accessibility", says that buttons, links, the selected item and the focus use the purple of the 42 logo, a deeper shade in the light theme and a lighter one in the dark theme. It also says that the screens are tested against WCAG 2.1 AA in the light and the dark theme.

### Results

- `npm test`: 430 passed (47 files).
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`): 187 passed, 5 skipped. Under 12 parallel workers, two other runs each had one timeout-type failure that did not reproduce: "light › templates" once (40 of 40 passed on repeat), and a click timeout on "dark › the export dialog" (mobile) in a three-times repeat of the accessibility file (251 of 252).
- `npm run test:visual:docker`: 52 passed with the committed baselines.
- `npm run typecheck`, `npm run lint` (oxlint, ESLint) and `npm run build` report no problems. Prettier reports no problems in the changed source and test files.

### Notes

- **Merges:** the branch starts from `main` (`908ff981`). It changes 46 baseline files, and the open `fix/424-35` rewrites 48. Whichever merges second has to regenerate the baselines in the container with `--update-snapshots=all`. `fix/424-37` changes no baseline.
- **Code review:** three reviewers (correctness and coverage, tests, docs and spec), and two skeptics checked every finding. Two findings held up, and both are handled above: the darker selected-row tint (section 2) and the wrong "lowest 4.53" in the spec (section 4). Three were refuted. The README's list of e2e checks is an incomplete summary and was already before this change. The Deleted axe check shows no accent. The README's `--update` instructions are older than this change.
- **Remaining gaps, as in the spec:** in dark mode, Nuxt UI's `/75` hover of solid buttons and links and its `/15` hover of soft buttons are at 4.13 to 4.31 (indigo: 3.75 to 3.93). The `outline-primary/25` focus outline is about 1.5:1 for any accent.
