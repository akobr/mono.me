# Cookieless Analytics on 42for.net

Linear: [424-280](https://linear.app/424fun/issue/424-280/switch-42fornet-from-google-analytics-to-cookieless-analytics)

## Problem

The docs site 42for.net (`docs/42for.net`, VitePress) loads Google Analytics (`G-HDDVP9E098`) on every page. Google Analytics:

- sets cookies (`_ga`, `_ga_<id>`) without asking. The Swedish Electronic Communications Act (chapter 9, section 28) requires consent for storage on the visitor's device that isn't strictly necessary, and the site has no consent banner;
- sends visitor data to the USA, while everything else we run keeps its data in the EU;
- leaves a `[TBD]` in the draft privacy policy (424-273): the website row of section 2, section 4, and the Google rows of sections 5 and 6.

The issue asks for analytics that set no cookies, store nothing in the browser and keep the data in the EU. They must also respect Do Not Track and Global Privacy Control, like the admin UI. The owner chose PostHog EU (`eu.i.posthog.com`) without persistence (`persistence: 'memory'`). PostHog is already a sub-processor, so this needs no new one.

## Current State

- `docs/42for.net/.vitepress/config.mjs`, `head` (lines 12-23): the `gtag.js` loader from `www.googletagmanager.com` and an inline `gtag('config', 'G-HDDVP9E098')` snippet. Nothing else on the site loads analytics.
- `docs/42for.net/.vitepress/theme/index.js`: extends the default theme with `_42Layout.vue` and the tabs plugin; `enhanceApp({ app })` only.
- `docs/42for.net/package.json`: dev dependencies only (VitePress, Mermaid, the tabs and Mermaid plugins); no runtime dependency.
- **Two lockfiles.** `package-lock.json` pins VitePress `1.0.0-rc.35` and Vite 5.0.11. `pnpm-lock.yaml` (bumped by the dependency PRs) pins VitePress 1.6.4. The deployment (`.github/workflows/Docs.42for.net.yml`, `Azure/static-web-apps-deploy@v1`) builds with Oryx, and its log shows `npm install` and `npm run build` on Node 22. The deployed site is therefore built from `package-lock.json`, with VitePress rc.35.
- The same workflow deploys pull-request preview environments (`*.azurestaticapps.net`), and `npm run dev` / `npm run preview` serve on `localhost`.
- The admin UI's PostHog setup (`src/Platform/ui.admin/src/app/boot/posthog.ts`) uses `posthog-js/no-external` with `persistence: 'localStorage'`, `respect_dnt: true`, `advanced_disable_flags: true` and every optional feature off. Its project token (`phc_uGDiou…`) for local runs is committed in `.env.development`, and `sform` commits the same token in `src/Platform/Cli/src/app.config.json`.
- The privacy policy, sub-processors and the other legal drafts live in `docs/42for.software/legal/`. They are **untracked** in the main checkout, not committed to the repository.

### What posthog-js 1.450.0 does with `persistence: 'memory'`

Read from the installed sources (`node_modules/posthog-js/lib/src`, `dist/module.no-external.js`):

- **State.** PostHog's state (distinct id, session id, `$initial_*` and session properties) lives in a module-level JavaScript object (`storage.js`, `memoryStore`). VitePress is a single-page app, so the id lasts while the visitor navigates in the tab. A reload or a new tab starts a new anonymous id. With `'memory'` no cookie is written, the window id skips `sessionStorage` (`sessionid.js`), and nothing uses IndexedDB.
- **Remaining device access.** Even so, every start-up touches the browser's storage briefly. Nothing stays behind afterwards, and no option turns these off:
  - a `localStorage` support probe (`__mplssupport__`: written, read, removed);
  - a read and a remove of `ph_debug`;
  - the toolbar extension's probe (`test`) and a read of `_postHogToolbarParams`;
  - reads of the consent key `__ph_opt_in_out_<token>` (in `localStorage`, and once in `document.cookie`);
  - a `sessionStorage` read and remove of two `ph_<token>_*` keys.
- **Durable writes from a crafted URL.** Two writes persist:
  - `?__posthog_debug=true` writes `ph_debug=true` unless `debug: false` is set;
  - a crafted `#__posthog=…` hash with the public token writes `_postHogToolbarParams`. Only a bundle without the toolbar avoids that. The slim bundle has no package entry, and the switch `__extensionClasses` is internal.
- **Campaign parameters.** `save_campaign_params` (on by default) reads `document.cookie` on every event for `_fbc`, `_fbp` and `li_fat_id`.
- **DNT and GPC.** `respect_dnt: true` checks `navigator.doNotTrack`, `navigator.msDoNotTrack`, `window.doNotTrack` and `navigator.globalPrivacyControl`. It treats `true`, `'true'`, `1`, `'1'` and `'yes'` as on (`consent.js`, `isYesLike`). It blocks every capture, but init still runs with all the storage touches above.
- **Requests.** With `advanced_disable_flags: true`, PostHog never requests `/flags` or the remote config. Events go only to `POST /e/`, gzip, `text/plain` (no CORS preflight). `sendBeacon` is used on `pagehide` unless `disable_beacon` is set, and a beacon carries the visitor's cookies of `posthog.com`.
- **Importing the package.** It cannot be imported statically in a VitePress theme. `posthog-js/no-external` is a folder with its own `package.json`, and posthog-js has no `exports` field. Vite resolves it, but Node's ESM loader rejects it in the SSR render step (`ERR_UNSUPPORTED_DIR_IMPORT`).
- **`capture_pageview: 'history_change'`.** It fires one microtask after `pushState`. VitePress pushes the URL before it loads the page and sets `document.title`, so the event would very likely carry the previous page's title. VitePress rc.35 calls `router.onAfterRouteChanged(to)`:
  - on the first load and after every in-site navigation;
  - after `popstate`, including back and forward between anchors of one page;
  - but not for a click on a heading anchor.

## Proposed Changes

### 1. Remove Google Analytics

Delete both `gtag` entries from `head` in `docs/42for.net/.vitepress/config.mjs`. Keep the favicons and `/tooltips.js`.

### 2. Add posthog-js

- Add `posthog-js` (`^1.450.0`, the admin UI's major line) to `dependencies` of `docs/42for.net/package.json`. It is the only dependency that ships to the browser.
- Update **both** lockfiles. `package-lock.json` is the one the deployment installs (`npm install`). Keep `pnpm-lock.yaml` in sync so a pnpm install (and the dependency PRs) still work. Use `npm install posthog-js` and `pnpm install --lockfile-only` with a current pnpm, so the existing pins stay as they are.

### 3. `docs/42for.net/.vitepress/theme/analytics.js`

A new module with one export, `startAnalytics(router)`, called from `enhanceApp`.

**Gates.** Each gate runs before anything is downloaded, in this order:

1. `import.meta.env.SSR`: Vite replaces it with a constant, so the SSR bundle drops the whole module, and the build never evaluates posthog-js.
2. **Production host only.** `location.hostname` is `42for.net` or `www.42for.net`. Local `vitepress dev` / `preview`, the pull-request previews and forks send nothing. This needs no build variable and no workflow change.
3. **Do Not Track or Global Privacy Control.** The module reads `navigator.doNotTrack`, `navigator.msDoNotTrack`, `window.doNotTrack` and `navigator.globalPrivacyControl`, with posthog's own set of "on" values (`true`, `'true'`, `1`, `'1'`, `'yes'`), compared case-insensitively for strings. For such a visitor the PostHog chunk is never requested, and nothing runs or touches storage. `respect_dnt: true` stays as a second guard for a signal switched on later in the session.

**Loading.** A dynamic `import('posthog-js/no-external')` (the admin UI's entry, which never injects scripts), not awaited, so it never delays the first render. A load or init failure is logged with `console.warn` and leaves analytics off.

**Configuration**

| Option | Value | Why |
| --- | --- | --- |
| `api_host` | `https://eu.i.posthog.com` | EU cloud (Frankfurt) |
| `defaults` | `'2026-05-30'` | The admin UI's preset; the options below are explicit anyway |
| `persistence` | `'memory'` | No cookie, nothing in `localStorage` or `sessionStorage` |
| `person_profiles` | `'never'` | Anonymous page views only, no person profiles |
| `capture_pageview` | `false` | Page views come from the router (section 4) |
| `capture_pageleave` | `false` | Like the admin UI |
| `disable_capture_url_hashes` | `true` | `#heading` is not part of the page URL |
| `autocapture` | `false` | Page views only, no clicks |
| `save_campaign_params` | `false` | Would read `document.cookie` (`_fbc`, `_fbp`, `li_fat_id`) on every event |
| `disable_beacon` | `true` | `fetch` without credentials; a beacon sends the visitor's `posthog.com` cookies |
| `disableDeviceModel` | `true` | No high-entropy client hint request |
| `debug` | `false` | `?__posthog_debug=true` would write `ph_debug` to `localStorage` |
| `respect_dnt` | `true` | Second guard after the gate |
| `advanced_disable_flags` | `true` | No `/flags` and no remote config: the only request is `POST /e/` |
| `disable_session_recording`, `capture_exceptions`, `capture_performance`, `capture_dead_clicks`, `capture_heatmaps` | off | Nothing but page views |
| `disable_surveys`, `disable_product_tours`, `disable_conversations`, `disable_external_dependency_loading` | `true` | As in the admin UI |

`save_referrer` keeps its default (on). The referrer is the main signal of which documentation helps, and browsers send only the origin of another site (`strict-origin-when-cross-origin`). The site never calls `identify`, `reset`, `opt_in_capturing`, `opt_out_capturing`, `set_config` or `loadToolbar`, which write or remove storage keys.

**Super property.** `posthog.register({ environment: 'production' })`, like the admin UI and `sform`. The site shares their project, and dashboards filter on `environment`.

**Project.** The token is a constant in the module. It is public by design (write-only) and already committed for `sform`. It is the project of `sform` and the admin UI's local runs; moving the site to another project means changing the constant.

### 4. Page views from the VitePress router

- `startAnalytics` wraps `router.onAfterRouteChanged`, chaining an existing handler.
- It waits for Vue's `nextTick()`, so VitePress has set the new `document.title` and finished its `replaceState` correction of the URL.
- It then sends `$pageview` when `location.pathname` differs from the last one sent. A click on a heading, or back and forward between headings, sends nothing.
- When the chunk loads after the first route change (the usual case), the loader sends the first page view itself. The same pathname check prevents a duplicate when the order is reversed.
- `enhanceApp` in `theme/index.js` gets `router` and calls `startAnalytics(router)` after `enhanceAppWithTabs(app)`.

### 5. Privacy policy (untracked drafts)

The drafts in `docs/42for.software/legal/` are not in the repository, so this branch cannot change them. The intended text, to apply when the drafts are committed or edited:

- **Section 2, website row:** "**Website visits** to 42for.net: the pages visited, the site that linked to them, browser and device details, and the IP address | To see which documentation helps | Legitimate interest | 30 days".
- **Section 4, last bullet:** "The 42for.net website sets no cookies and keeps nothing in your browser's storage. Its analytics use a random id that lasts only until you reload or close the page, and send nothing when your browser has Do Not Track or Global Privacy Control turned on."
- **Section 5:** remove the Google row; the PostHog row becomes "Usage analytics, and website analytics of 42for.net".
- **Section 6:** "WorkOS is in the USA. It is certified under the EU-U.S. Data Privacy Framework, which the EU recognizes as giving adequate protection, and its data processing agreement also includes the EU Standard Contractual Clauses."
- **README.md:**
  - check the item "Google Analytics on 42for.net";
  - in "For the lawyer", replace "42for.net sets Google Analytics cookies" with a note on PostHog's start-up probes;
  - add a row for website analytics (30 days) to "The numbers".
- `sub-processors.md` needs no change. Website analytics are our own data (controller), not customer data.

### 6. Delete the Google Analytics property

A manual step for the owner, once the new analytics show page views: delete the property of `G-HDDVP9E098` in Google Analytics (Admin → Property → Move to trash).

### 7. Verification

There is no test project for the site. Verify the built site in Chromium with Playwright, against `vitepress preview`:

- **Production host.** Map `42for.net` to `127.0.0.1` with `--host-resolver-rules`. Intercept `https://eu.i.posthog.com/**` so nothing reaches PostHog. Instrument `Storage.prototype` and `document.cookie`. Check:
  - one `$pageview` on load, with the right title and `environment`;
  - one per in-site navigation, with the new page's title;
  - none for a heading click, and one for back to another page;
  - no cookies;
  - no PostHog key left in `localStorage` or `sessionStorage`;
  - no request to `googletagmanager.com` or `google-analytics.com`.
- **Do Not Track and Global Privacy Control.** The PostHog chunk is never requested.
- **localhost.** The PostHog chunk is never requested.
- **Build.** `npm run build` passes. The SSR bundle has no `posthog-js` import, and the HTML has no `modulepreload` of the PostHog chunk.

## Alternatives considered

- **Plausible** (cookieless by design, EU). A new sub-processor and a new contract, against the owner's choice of PostHog.
- **PostHog `cookieless_mode: 'always'`.** The server derives a daily-rotating id from a hash of IP and user agent, which counts visitors across reloads. It needs a project setting, and in this version it bypasses `respect_dnt` (`is_capturing()` returns early). Not chosen; a possible later step with our own DNT/GPC gate.
- **`capture_pageview: 'history_change'`.** Simpler, but the title of a page view would likely be the previous page's (section "Current State").
- **A token from a GitHub variable through the workflow's `env`.** It works, but needs a variable to be set before analytics run. The token is public, and the host gate already keeps previews out.

## Out of scope (found while preparing this)

- VitePress itself writes `localStorage['vitepress-theme-appearance']` on the first visit (the dark-mode preference). Its local search writes `localStorage` and `sessionStorage` when used. These are preferences, not analytics.
- `cli/mrepo.md` embeds five YouTube iframes (`www.youtube.com`), which set third-party cookies on page load.
- The lockfile drift: the dependency PRs bump `pnpm-lock.yaml`, but the deployment installs from `package-lock.json` (VitePress rc.35).
