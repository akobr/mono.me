# 42 Logo as the Favicon

Linear: [424-32](https://linear.app/424fun/issue/424-32/change-the-2s-logo-to-42-logo)

## Overview

This review covers the whole spec [2026-10-14 42 Logo as the Favicon](../2026-10-14%2042%20Logo%20as%20the%20Favicon.md), delivered in one phase as a follow-up to [2026-10-14 42 Logo in the Navigation Bar](./2026-10-14%2042%20Logo%20in%20the%20Navigation%20Bar.md), which left the favicon out of scope. The browser tab of the admin UI now shows the 42 logo, from the same 16 and 32 px PNGs as the docs site, and the "2S" SVG placeholder is gone. Nothing in the app shows the old logo any more.

## What Was Done

### 1. The favicons of the docs site

`docs/42for.net/public/favicon-16x16.png` (16 × 16, 3.6 KB) and `favicon-32x32.png` (32 × 32, 5.3 KB) are copied byte for byte to `src/Platform/ui.admin/src/public/`, as planned.

### 2. `index.html`

The SVG link is replaced by the two PNG links of the docs site, 32 px first:

```html
<link rel="icon" type="image/png" sizes="32x32" href="/favicon-32x32.png" />
<link rel="icon" type="image/png" sizes="16x16" href="/favicon-16x16.png" />
```

`npm run build` copies both files to `dist/` and keeps the links in `dist/index.html` unchanged. `vite preview` of that build serves both with `200 image/png`.

### 3. Remove `public/favicon.svg`

The file is deleted. In `src/Platform/ui.admin`, `favicon.svg` appears only in the test that checks it stays gone. `vite preview` answers a request for it with `index.html`, its SPA fallback. Azure Static Web Apps answers `404`, because `navigationFallback.exclude` covers `*.svg`.

### 4. Tests

- **Unit** (`test/unit/build/favicon.test.ts`, new file, node environment, 2 tests):
  - "links the 42 logo at 32 and 16 px" reads the `<link rel="icon">` tags of `index.html`. It expects them in the order `32x32`, `16x16`, each `image/png` and named `/favicon-<size>.png`, and checks that the PNG signature and the IHDR width × height of each file in `public/` match its `sizes`.
  - "no longer links the "2S" placeholder" checks that `index.html` has no `favicon.svg`.

  Both tests fail with the previous `index.html`.
- **Build:** checked as in section 2.
- **Visual:** not run again. Screenshots do not include the tab, and no file the suite renders changed.

### 5. Documentation

- `README.md`, "Architecture": `public/` lists "16 and 32 px favicons" instead of "favicon".
- `test/unit/components/brand.test.ts`: its comment says "the former "2S" placeholder" instead of naming the deleted `public/favicon.svg`.

### Results

- `npm test`: 330 passed (38 files), including the 2 favicon tests.
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`): 115 passed, 3 skipped.
- `npm run typecheck`, `npm run lint` (oxlint, ESLint) and `npm run build` report no problems, and Prettier reports none in the changed files.

### Notes

- The admin UI and the docs site now have two copies of the same favicon files. If the 42 logo changes, both `public/` folders need the new files.
- There is still no `apple-touch-icon` or web app manifest; neither existed before.
