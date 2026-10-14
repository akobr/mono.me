# 42 Logo as the Favicon

Linear: [424-32](https://linear.app/424fun/issue/424-32/change-the-2s-logo-to-42-logo)

## Problem

[2026-10-14 42 Logo in the Navigation Bar](./2026-10-14%2042%20Logo%20in%20the%20Navigation%20Bar.md) replaced the "2S" placeholder in the navigation bar and on the bare pages of the admin UI (`src/Platform/ui.admin`) with the 42 logo. It left the favicon out of scope, so the browser tab, bookmarks and history still show the indigo "2S" square while the app shows the 42 logo. The favicon should be the 42 logo too.

## Current State

- `src/index.html`, line 5: `<link rel="icon" type="image/svg+xml" href="/favicon.svg" />`.
- `public/favicon.svg`: a 32 × 32 SVG, an indigo `#4f46e5` rounded square with the text "2S". After the navigation bar change, `index.html` is its only reference.
- **Docs site:** `docs/42for.net/public/` already has the 42 logo as favicons drawn for their size, `favicon-16x16.png` (16 × 16, 3.6 KB) and `favicon-32x32.png` (32 × 32, 5.3 KB). `docs/42for.net/.vitepress/config.mjs`, lines 10–11, links them:

  ```js
  ['link', { rel: 'icon', type: 'image/png', sizes: '32x32', href: '/favicon-32x32.png'}],
  ['link', { rel: 'icon', type: 'image/png', sizes: '16x16', href: '/favicon-16x16.png'}],
  ```

- **Serving:** Vite copies `public/` to `dist/` unchanged and leaves absolute links to public files in `index.html` as they are. On Azure Static Web Apps, `navigationFallback.exclude` in `public/staticwebapp.config.json` already has `/*.{js,css,svg,png,ico,json,woff2,txt}`, so a missing PNG answers `404` instead of `index.html`. The CSP allows `img-src 'self'`.
- **Tests:** nothing tests the favicon. The visual suite takes page screenshots, which do not include the tab. The comment of `test/unit/components/brand.test.ts`, line 22, names `public/favicon.svg`.
- **Docs:** `README.md`, "Architecture", lists `public/` as "app-config.json, theme-init.js (color mode before first paint), favicon".

## Proposed Changes

### 1. The favicons of the docs site

Copy `docs/42for.net/public/favicon-16x16.png` and `favicon-32x32.png` unchanged to `src/Platform/ui.admin/src/public/`. A favicon keeps a fixed name and is never imported from the app, so `public/` is the right place, as for the old one. Drawn sizes stay sharper in a tab than the 128 px logo scaled down by the browser, and the admin UI and the docs site then show the same icon.

### 2. `index.html`

Replace the SVG link with the two PNG links of the docs site, the 32 px first:

```html
<link rel="icon" type="image/png" sizes="32x32" href="/favicon-32x32.png" />
<link rel="icon" type="image/png" sizes="16x16" href="/favicon-16x16.png" />
```

The browser picks the size it needs; a high-density 16 px tab uses the 32 px file.

### 3. Remove `public/favicon.svg`

Nothing references it any more. A request for it answers `404`, as for any missing file.

### 4. Tests

- **Unit** (`test/unit/build/favicon.test.ts`, new file, node environment): every `<link rel="icon">` of `index.html` points at a file in `public/` whose PNG header has the width and height of its `sizes` attribute, the links cover 16 × 16 and 32 × 32, and `index.html` no longer references `favicon.svg`.
- **Build:** `dist/` has both PNGs and `dist/index.html` links them unchanged.
- `npm test`, `npm run typecheck`, `npm run lint`, `npm run build` and `npm run test:e2e` must pass. The visual baselines do not change, since screenshots do not include the tab.

### 5. Documentation

- `README.md`, "Architecture": `public/` lists the 16 and 32 px favicons of the 42 logo.
- The comment of `brand.test.ts` no longer names `public/favicon.svg`.
