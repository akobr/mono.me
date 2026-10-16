# Purple Accent Matching the 42 Logo

Linear: [424-39](https://linear.app/424fun/issue/424-39/change-ascent-color)

## Problem

The accent color of the admin UI (`src/Platform/ui.admin`) is indigo. Since [2026-10-14 42 Logo in the Navigation Bar](./2026-10-14%2042%20Logo%20in%20the%20Navigation%20Bar.md) and [2026-10-14 42 Logo as the Favicon](./2026-10-14%2042%20Logo%20as%20the%20Favicon.md), the app shows the 42 logo, whose signature color is a bright purple ring. The indigo of buttons, links, the active navigation item, tabs, switches and focus rings echoes only the logo's dark inner ring and reads as off-brand next to it. The issue asks for "something purple which will better accompany the 42 logo", which "will work nicely with dark and light mode".

## Current State

- **Palette choice:** `vite.config.ts`, line 16: `ui({ ui: { colors: { primary: 'indigo', neutral: 'zinc' } } })`. This is the only place the accent palette is chosen. At runtime, Nuxt UI 4.11.3 (`runtime/plugins/colors.js`) injects `<style id="nuxt-ui-colors">` in `@layer theme` with `--ui-color-primary-50..950: var(--color-<palette>-<shade>, <tailwindcss/colors fallback>)`, and by default `--ui-primary` = shade 500 in light and 400 in dark.
- **Shade overrides:** `app/assets/css/main.css`, lines 62–89 (the WCAG block of spec section 15 of [2026-10-09 Administration UI for 2S Platform](./2026-10-09%20Administration%20UI%20for%202S%20Platform.md)): unlayered `:root { --ui-primary: var(--ui-color-primary-600) }` and `.dark { --ui-primary: var(--ui-color-primary-400) }`. Unlayered rules win over Nuxt UI's `@layer theme`, and they point at the alias shades, not at a palette name, so they keep working with any palette.
- **Everything else follows `--ui-primary`.** No Nuxt UI component theme and no app class uses a numbered primary shade (`primary-500`) or an `indigo-*` utility. Nuxt UI components paint the accent through `text-primary`, `bg-primary`, `bg-primary/10`, `ring-primary`, `outline-primary/25` (solid, soft, subtle, outline and link buttons and badges, the active `UNavigationMenu` item, `UTabs`, `USwitch`, `UCheckbox`, `URadioGroup`, `UProgress`, input focus rings, toasts, the command palette). The app's own uses read the same variable: the selected-row tint `color-mix(in oklab, var(--ui-primary) 12%, var(--ui-bg))` (`AnnotationGrid.vue:63`, `configurations.vue:112`, `key-values.vue:206`), `bg-primary/5` and `/10` row highlights, `text-primary` links and icons, the usage chart bars (`usage.vue:124,142`), resize handles, Markdown links (`MarkdownText.vue:127`), and the CodeMirror caret, cursor and selection (`editor/setup/theme.ts:18-22`).
- **One hard-coded indigo:** `app/components/graph/GraphCanvas.vue`, line 123: `const primary = cssVar(style, '--ui-primary', '#4f46e5')`. The live value comes from `--ui-primary` (the selection ring and the neighbour links of the graph); `#4f46e5` (Tailwind v3 indigo-600) is only the fallback.
- **Indigo that is not the accent:**
  - `main.css` lines 17 and 35: the CodeMirror syntax token `--cm-property` (`#4338ca` light, `#a5b4fc` dark) for property names in the JSON and YAML editors.
  - `main.css` lines 25–26 and 43–44 and `app/utils/type-appearance.ts`: the annotation type tokens, among them `--st-rst` violet (`#6d28d9` / `#a78bfa`) and `--st-unt` fuchsia (`#a21caf` / `#e879f9`), and the palette users pick type colors from (indigo, violet, purple, fuchsia, …).
- **The 42 logo** (`app/assets/images/42logo-128.png`, the favicons): a navy center (oklch(21% 0.066 256)) with a white "42", an indigo ring (oklch(47% 0.13 277–281)), the dominant **bright purple ring `#b060ef` = oklch(64.5% 0.211 307.3)**, and a lavender outer ring `#d0c5fb` = oklch(85% 0.075 294).
- **Tailwind 4.3.3 palettes** by hue: indigo ≈ 277, violet ≈ 293, purple ≈ 302–308, fuchsia ≈ 322. purple-400 is oklch(71.4% 0.203 305.5), purple-500 oklch(62.7% 0.265 303.9).
- **Tests:**
  - No unit or e2e test asserts the accent; every "primary" in `test/` is the binding source `primaryVault`.
  - `test/e2e/accessibility.spec.ts` runs axe (WCAG 2.1 AA, serious and critical) on 16 screens and 5 dialogs or bare pages, desktop and mobile, in the **light** scheme only (`playwright.config.ts` sets no `colorScheme`). Run in the dark scheme against the current indigo build, it fails once: on the key value detail (desktop), the subtle primary "Standard" badge in the versions table has too little contrast (`td:nth-child(3) > .bg-primary\/10.ring-primary\/25.text-primary > .truncate`).
  - `test/e2e/visual.spec.ts`: 13 screens × desktop/mobile × light/dark = 52 baselines in `test/e2e/__screenshots__`, compared with `maxDiffPixelRatio: 0.002` and Playwright's per-pixel threshold 0.2 in the Playwright 1.64 Linux image (`npm run test:visual:docker`). A pure hue change of the accent stays under that threshold, so the stale baselines would still pass. `--update` passes a bare `--update-snapshots`, which in Playwright 1.64 rewrites only failing screenshots. 10 desktop baselines (annotations, annotation detail, annotation history, configurations and deleted, light and dark) still show the old indigo "2S" logo for the same reason.
- **Docs:** `src/Platform/ui.admin/README.md`, "Architecture", describes `css/main.css` as "(Tailwind, Nuxt UI, fonts, type tokens)". `docs/42for.net/platform/admin-ui.md`, "Phones, themes and accessibility", names no accent and says the screens are tested against WCAG 2.1 AA (axe).

### Contrast of the candidates

WCAG 2 ratios on the zinc surfaces of Nuxt UI (light: `--ui-bg` white, `--ui-bg-elevated` zinc-100 `#f4f4f5`; dark: `--ui-bg` zinc-900 `#18181b`, `--ui-bg-elevated` zinc-800 `#27272a`, `--ui-text-inverted` zinc-900). oklch is converted to 8-bit sRGB and clipped as Chrome renders it; tints are composited over the surface. Text needs 4.5:1.

| Light shade | text on bg | text on elevated (active nav) | soft/subtle badge on elevated | solid hover (`/75`) |
|---|---|---|---|---|
| indigo-600 `#4f39f6` (today) | 6.46 | 5.88 | 5.04 | 3.96 ✗ |
| violet-600 `#7f22fe` | 5.89 | 5.36 | 4.58 | 3.94 ✗ |
| purple-600 `#9810fa` | 5.54 | 5.04 | 4.28 ✗ | 3.95 ✗ |
| **purple-700 `#8200db`** | **7.07** | **6.43** | **5.38** | **4.81** |

| Dark shade | text on bg | text on elevated | soft/subtle badge on elevated | solid hover (`/75`) |
|---|---|---|---|---|
| indigo-400 `#7c86ff` (today) | 5.66 | 4.76 | 4.10 ✗ | 3.75 ✗ |
| violet-400 `#a684ff` | 6.21 | 5.22 | 4.49 ✗ | 4.05 ✗ |
| **purple-400 `#c27aff`** | **6.35** | **5.33** | **4.58** | 4.13 ✗ |

Purple has a higher luminance than indigo at the same shade number, so purple-600 in light mode falls below today's indigo; purple-700 is better than indigo on every pair. The 4.10 of the dark indigo badge is the axe failure above.

## Proposed Changes

### 1. Purple as the primary palette (`vite.config.ts`)

Set `ui: { colors: { primary: 'purple', neutral: 'zinc' } }`.

- **Tailwind `purple`** is the stock palette closest to the logo's dominant ring (hue 302–308 against the ring's 307). purple-400, the dark-mode accent, renders on sRGB screens at hue 307.1 with almost the ring's chroma: the same purple.
- **Not `violet`.** It echoes the lavender outer ring (hue 294), not the bright purple one, and it is the built-in color of the Responsibility type (`--st-rst`): dark violet-400 `#a684ff` is practically `#a78bfa`, so links and buttons would look like rst chips.
- **Not a custom palette.** A hue-307 ramp would need all 11 shades in `@theme static` in `main.css` (the runtime builds the variable names, so Tailwind's scanner never emits a plain `@theme`), and to pass the hover contrast its light shade has to be essentially purple-700. It is not worth maintaining.
- Secondary, success, info, warning and error keep the Nuxt UI defaults (secondary and info stay blue). Purple separates from them better than indigo did, for example the Owner badge (primary) from the Administrator badge.

### 2. Shades for light and dark (`app/assets/css/main.css`)

- **Light:** `--ui-primary: var(--ui-color-primary-700)` instead of `600`. purple-700 `#8200db` is a deeper shade of the logo's purple and passes every pair of the table, including the hover states that indigo fails today.
- **Dark:** unchanged, `var(--ui-color-primary-400)` (`#c27aff`). It passes every resting pair (lowest 4.58, a soft badge on an elevated surface) and fixes the axe failure of the key value detail.
- Update the comment of the block: the light accent is shade 700 because purple's 600 is below 4.5:1 on the tinted backgrounds of soft and subtle badges.

The remaining gaps are Nuxt UI defaults, not this palette: in dark mode the transient `/75` and `/15` hover states (4.13–4.31; indigo 3.75–3.93), and the `outline-primary/25` focus outline (about 1.5:1 for every candidate). They stay out of scope.

### 3. The graph's fallback color (`GraphCanvas.vue`)

Replace the fallback `'#4f46e5'` with `'#8200db'` (purple-700), so no indigo literal is left for the accent.

### 4. Unchanged on purpose

- **CodeMirror property names** (`--cm-property`, indigo `#4338ca` / `#a5b4fc`) are a syntax token, not the accent. Keeping them indigo matches the logo's indigo ring, and property names no longer share the hue of links and the caret.
- **Annotation type colors** (`--st-*`, `builtInTypeColors`, the type palette). Purple sits between the violet rst and the fuchsia unt (9–22° of hue apart). Type chips always show an icon and the type code, and type text on a primary-tinted selected row still passes (lowest 4.53). The palette's purple option becomes close to the accent, as its indigo option was to the old one.
- **The logo, the favicons and the product name.**
- **The docs site** (`docs/42for.net/.vitepress`) keeps its own theme.

### 5. Tests

- **Accessibility in both themes** (`test/e2e/accessibility.spec.ts`): run the whole file in the light and the dark scheme (`test.describe` per scheme with `test.use({ colorScheme })`), so the dark accent is checked by axe too. With this change the dark key value detail must pass.
- **The accent itself** (`test/e2e/theme.spec.ts`, new): in each scheme, open the annotations screen, read `--ui-primary` from the document, render it through a 2D canvas to 8-bit sRGB, and expect purple-700 `#8200db` in light and purple-400 `#c27aff` in dark (±2 per channel). Then check that the accent really reaches a component: in light mode, the computed color of a primary element equals the same value.
- **Visual baselines:** regenerate them in the Playwright 1.64 container with `--update-snapshots=all` (a bare `--update-snapshots` would keep them, see Current State), from a one-off copy of `run-in-container.mjs` outside the repository, as the 2026-10-14 logo review did. Commit the screenshots that show the accent. A screenshot whose only differences are unrelated to the accent keeps its committed file. A second compare run must pass all 52.
- `npm test`, `npm run typecheck`, `npm run lint`, `npm run build` and `npm run test:e2e` must pass.

### 6. Documentation

- `src/Platform/ui.admin/README.md`, "Architecture": `css/main.css (Tailwind, Nuxt UI theme with the purple accent, fonts, type tokens)`.
- `docs/42for.net/platform/admin-ui.md`, "Phones, themes and accessibility": the accent is the purple of the 42 logo in both themes, and the screens are tested against WCAG 2.1 AA in the light and the dark theme.
- The historical specs and reviews that name indigo stay as they are.
