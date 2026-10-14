# 42 Logo in the Navigation Bar

Linear: [424-32](https://linear.app/424fun/issue/424-32/change-the-2s-logo-to-42-logo)

## Problem

The logo at the top left of the navigation bar of the admin UI (`src/Platform/ui.admin`) is a placeholder: an indigo rounded square with the text "2S". The issue asks to replace it with the 42 logo, which the repository already has in `.asset/42logo-<size>.png`.

## Current State

- **The 2S logo** is `public/favicon.svg`, a 32 × 32 SVG: a `#4f46e5` rounded square with the text "2S" in `system-ui`. The app shows it in three places:
  - `app/components/shell/SidebarBrand.vue`, line 12: `<img src="/favicon.svg" alt="" class="size-6 shrink-0" />`, inside the home link (`aria-label="2S Admin home"`, line 11) and before the "2S Admin" text (line 13, hidden when the sidebar is collapsed to the icon rail). `AppSidebar.vue` renders `SidebarBrand` in the header of the docked sidebar (line 56) and at the top of the navigation slideover (line 74), which is the menu on phones and when the sidebar is unpinned. This is the logo the issue is about.
  - `app/layouts/BareLayout.vue`, line 4: `<img src="/favicon.svg" alt="" class="size-7" />` before "2S Admin", above the card of the pages without the dashboard shell (sign-in, callback, invitations, onboarding, unsupported browser, and the start page `/`).
  - `index.html`, line 5: the favicon of the browser tab.
- **The 42 logo** is in `.asset/` at the repository root: `42logo-128.png` (128 × 128, RGBA, 20 KB), `42logo-512.png` and `42logo-642.png`. It is round: a dark circle with "42" in white and a violet ring. Its corners are transparent, so it sits on the light and the dark theme alike.
- **`.asset/` is outside the app.** The app's root is `src/Platform/ui.admin/src`. The Vite dev server does not serve files outside its root by default (`server.fs.allow`), and `npm run test:visual:docker` copies only the admin UI and the SDK folders into the Playwright container. The app cannot use the file from `.asset/` where it is.
- **Assets:** `public/` holds the files that must keep their names (`favicon.svg`, `app-config.json`, `staticwebapp.config.json`, `theme-init.js`). `app/assets/` holds `css/` only. Images imported from the app are emitted to `dist/assets/` with a content hash, which `staticwebapp.config.json` serves with `Cache-Control: public, max-age=31536000, immutable`. `vite.config.ts` inlines no fonts; other files under 4 KB would be inlined as `data:` URLs (the CSP allows `img-src 'self' data:`), and the 20 KB logo is not.
- **Tests:** no unit test mounts `SidebarBrand` or `BareLayout`. No e2e test looks at the logo; `onboarding.spec.ts` checks the sign-in heading by its text. The visual suite (`test/e2e/visual.spec.ts`, 12 screens × desktop/mobile × light/dark, 0.2 % tolerance) shows the docked sidebar on all desktop screens except `onboarding`, and `BareLayout` on the `onboarding` screens. On phones the sidebar is a closed slideover, so the logo appears only on `onboarding-mobile-*`.
- **Docs:** `README.md`, "Architecture", lists `public/` as "app-config.json, theme-init.js (color mode before first paint), favicon". No user-facing documentation shows the logo.

## Proposed Changes

### 1. The 42 logo as an app asset

Copy `.asset/42logo-128.png` unchanged to `app/assets/images/42logo-128.png`. 128 px covers the 24 px sidebar logo up to a device pixel ratio of 5 and the 28 px logo of the bare pages up to 4.5.

The logo is imported, not put in `public/`:

- the build resolves the path, so a wrong path fails the build instead of showing a broken image;
- the file gets a content-hashed name under `/assets/`, cached as immutable, and a later change of the logo is never served from a stale cache.

### 2. The 42 logo in the navigation bar (`SidebarBrand.vue`)

Replace `src="/favicon.svg"` with `src="@/assets/images/42logo-128.png"`. The Vue compiler turns the `@/` path of an `<img>` into an import, so no script change is needed. The size (`size-6`), `alt=""` (the link already has the accessible name "2S Admin home") and the "2S Admin" text stay. The docked sidebar, its icon rail and the navigation slideover all render `SidebarBrand`, so all three show the new logo.

### 3. The same logo on the bare pages (`BareLayout.vue`)

The sign-in page is the first screen a user sees, with the same logo next to the same "2S Admin" name. Use the 42 logo there too (`src="@/assets/images/42logo-128.png"`, `size-7` and `alt=""` unchanged), so the app shows one logo everywhere.

### 4. Out of scope

- **The favicon** (`public/favicon.svg`, `index.html`) stays. It is the icon of the browser tab, not the logo of the navigation bar, and a PNG favicon needs its own sizes. It can follow in a separate change.
- **The product name** "2S Admin" (sidebar, bare pages, `<title>`, the sign-in heading) stays; the issue asks for the logo only.

### 5. Tests

- **Unit** (`test/unit/components/brand.test.ts`, new file): `SidebarBrand`, expanded and collapsed, and `BareLayout` show an image whose `src` is the URL of the imported `@/assets/images/42logo-128.png`, and neither shows `/favicon.svg`. The collapsed brand keeps the logo and drops the text.
- **Visual:** run `npm run test:visual:docker`. The logo is 24 × 24 px on the desktop screens, under the 0.2 % tolerance, but 28 × 28 px on `onboarding-mobile-*`, over it. Regenerate the baselines in the container so that every screen that shows the logo shows the new one, and commit only the screenshots that change.
- `npm test`, `npm run typecheck`, `npm run lint`, `npm run build` and `npm run test:e2e` must pass. Check in `dist/` that the logo is emitted as `assets/42logo-128-<hash>.png`.

### 6. Documentation

- `README.md`, "Architecture": add `app/assets/` (global CSS and the 42 logo) to the tree.
- No user-facing documentation shows the logo, so nothing else changes beyond this spec and its review.
