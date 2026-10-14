# 42 Logo in the Navigation Bar

Linear: [424-32](https://linear.app/424fun/issue/424-32/change-the-2s-logo-to-42-logo)

## Overview

This review covers the whole spec [2026-10-14 42 Logo in the Navigation Bar](../2026-10-14%2042%20Logo%20in%20the%20Navigation%20Bar.md), delivered in one phase. The navigation bar at the top left of the admin UI shows the 42 logo from `.asset/42logo-128.png` instead of the "2S" placeholder (`public/favicon.svg`). The sign-in and the other bare pages show it too. The favicon of the browser tab and the product name "2S Admin" are unchanged.

## What Was Done

### 1. The 42 logo as an app asset

`.asset/42logo-128.png` is copied byte for byte to `src/Platform/ui.admin/src/app/assets/images/42logo-128.png` and imported from the templates, not served from `public/`. `npm run build` emits it as `dist/assets/42logo-128-B1wBfpcj.png` (19.92 kB, not inlined), referenced from the `index-*.js` chunk. After the change, `/favicon.svg` is referenced only by `index.html`.

### 2. The 42 logo in the navigation bar (`SidebarBrand.vue`)

`app/components/shell/SidebarBrand.vue` uses `src="@/assets/images/42logo-128.png"`, as planned. `size-6`, `alt=""`, the `aria-label="2S Admin home"` of the link and the "2S Admin" text are unchanged.

Correction to the spec's Current State: the spec says the `SidebarBrand` at `AppSidebar.vue` line 74 is "the menu on phones and when the sidebar is unpinned". That slideover is the `v-else` of `preferences.sidebarPinned`, so it exists only when the sidebar is unpinned, at any width. With the default pinned sidebar, the phone menu is the slideover of `UDashboardSidebar` itself (`mode="slideover"`). It renders the same `#header` slot, the `SidebarBrand` at line 56. The conclusion does not change: every variant (docked, icon rail, phone menu, unpinned slideover) renders `SidebarBrand` and shows the new logo.

### 3. The same logo on the bare pages (`BareLayout.vue`)

`app/layouts/BareLayout.vue` uses the same import, with `size-7` and `alt=""` unchanged. The sign-in, callback, invitation, onboarding, unsupported-browser and start pages show the 42 logo above their card.

### 4. Out of scope

- **Favicon:** `public/favicon.svg` and `index.html` are unchanged, so the browser tab still shows the indigo "2S" square. It is now the only place with the old logo. A follow-up can point `index.html` at a PNG of the 42 logo.
- **Product name:** "2S Admin" stays everywhere.
- **Accessible name on the icon rail:** with the sidebar collapsed to the icon rail (the default between `lg` and `xl`), the home link shows only the logo. Its visible text is now "42", but its accessible name is still "2S Admin home". The old logo's "2S" was part of that name; "42" is not. A speech-input user who says "click 42" does not reach the link. Logos are a gray area of WCAG 2.5.3 (Label in Name), and axe does not flag it. The name stays, because it names the product and the destination, and the issue asks for the logo only. If it matters, a tooltip or an `aria-label` that includes "42" on the collapsed link can follow.

### 5. Tests

- **Unit** (`test/unit/components/brand.test.ts`, new file, 3 tests): the expanded `SidebarBrand` shows the logo, `alt=""`, the name "2S Admin home" and the text; the collapsed one keeps the logo and drops the text; `BareLayout` shows the logo above its slot. Each compares the `src` with the URL of the imported `@/assets/images/42logo-128.png`. Before the change the file cannot even load: in Vitest the template compiler turns `src="/favicon.svg"` into an import, which fails with "Received 'file:///favicon.svg'". The file also had a fourth test that checked only the imported file name; Vitest's dev asset pipeline never hashes a PNG, so it could not fail, and it was removed.
- **E2e** (not in the spec): happy-dom never loads images, and the visual tolerance is larger than a missing logo. With the logo blanked to the background, the screenshots differ by 232 to 342 pixels, under the 2592 (desktop) and 658 (mobile) pixel limits. Two checks now wait until the browser has decoded the 128 px image (`naturalWidth` 128):
  - `navigation.spec.ts`, "shows the 42 logo at the top of the navigation menu": the docked sidebar on desktop, the navigation slideover on mobile;
  - `onboarding.spec.ts`, "the sign-in page offers AuthKit after signing out": the bare layout of `/login`.
- **Visual:** with the old baselines, `npm run test:visual:docker` passed all 48 screens, including `onboarding-mobile-*`. The spec expected those to be over the tolerance. The new logo differs from the old one in 764 raw pixels there, but Playwright's per-pixel color threshold ignores part of them. `npm run test:visual:docker -- --update` passes a bare `--update-snapshots`, which in Playwright 1.64 rewrites only failing screenshots, so it would have left every baseline as it was. The baselines were regenerated instead in the same Playwright 1.64 container with `--update-snapshots=all`, from a one-off copy of `run-in-container.mjs` outside the repository. Of the 33 screenshots that came out different, 26 are committed: the 24 desktop ones (the sidebar logo at 16,20–39,43; onboarding at 660,204–687,231) and `onboarding-mobile-{light,dark}` (135,174–162,201). Six of the desktop ones also differ outside the logo, and the regenerated files include those differences: `annotations-desktop-*` and `annotation-history-desktop-*` by 380 to 760 pixels in the content, and `deleted-desktop-dark` and `machines-desktop-dark` by 7 pixels. The other 7 mobile screenshots (`annotation-history`, `template`, `schemas` and `deleted-mobile-light`) differed only outside the logo. The sidebar is closed on phones, so their differences are not related to this change, and they keep their committed files. A second compare run passed all 48 screens with the new baselines.
- **Correction to the spec:** the CSP directive is `img-src 'self' data: https:`, not `img-src 'self' data:`. Either way, a hashed `/assets/` PNG is `'self'`.

### 6. Documentation

`src/Platform/ui.admin/README.md`, "Architecture", lists `app/assets/`: `css/main.css` and `images/`, which holds the 42 logo. No user-facing documentation shows the logo.

### Results

- `npm test`: 328 passed (37 files).
- `npm run test:e2e` (desktop and mobile, in the installed Chrome with `PLAYWRIGHT_CHANNEL=chrome`): 115 passed, 3 skipped, including both new logo checks on both projects.
- `npm run test:visual:docker`: 48 passed with the committed baselines.
- `npm run typecheck`, `npm run lint` (oxlint, ESLint) and `npm run build` report no problems. Prettier reports no problems in the changed source and test files. `README.md` fails its check on `main` too (unaligned tables), and this change does not touch those lines.

### Notes

- The branch starts from `main` (`c297d5fe`). For every other local `feat/424-*` and `fix/424-*` branch, `git merge-tree` gives the same set of conflicts against this branch as against `main`, so the branch adds no conflict. It merges cleanly with the open `feat/424-30`.
- The visual baselines are binary. A branch that changes any of the 26 updated screenshots and is merged after this one must regenerate them in the container.
- An independent review covered asset resolution in every environment (dev, e2e build, Vitest, the Docker runner, Static Web Apps with its CSP and `/assets/*` cache rule, the MSW worker), accessibility, tests, docs and merge safety. Its confirmed findings are handled above: the e2e checks, the removed unit test, the two spec corrections and the note on the icon rail's accessible name.
