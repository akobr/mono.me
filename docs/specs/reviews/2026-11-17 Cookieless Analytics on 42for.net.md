# Cookieless Analytics on 42for.net

## Overview

This review covers the implementation of [Cookieless Analytics on 42for.net](../2026-11-17%20Cookieless%20Analytics%20on%2042for.net.md) (Linear [424-280](https://linear.app/424fun/issue/424-280/switch-42fornet-from-google-analytics-to-cookieless-analytics)).

- **What changed.** Google Analytics is gone from the docs site. It now counts page views in PostHog's EU cloud with `persistence: 'memory'`. It sets no cookie and leaves no key in `localStorage` or `sessionStorage`.
- **Who is counted.** Only visitors of the published site, and never when Do Not Track or Global Privacy Control is on.
- **Commits:** `94c5fddac` (the spec), `0c2afc37b` (the implementation), `399c76f59` (fixes from the review).
- **Not on this branch.** The privacy policy text (section 5) and the deletion of the Google Analytics property (section 6). The drafts are untracked, and the property waits for page views to arrive.

## What Was Done

### 1. Remove Google Analytics

- **`config.mjs`.** `docs/42for.net/.vitepress/config.mjs` no longer has the `gtag.js` loader or the inline `gtag('config', 'G-HDDVP9E098')` snippet. A comment in `head` points to the theme's analytics. The favicons and `/tooltips.js` stay.
- **The build.** The built site contains no `googletagmanager`, `google-analytics` or `G-HDDVP9E098`.

### 2. Add posthog-js

- **The dependency.** `posthog-js` `^1.450.0` (locked at 1.450.0) is in `dependencies` of `docs/42for.net/package.json`. Its new transitive packages:
  - `@posthog/core` 1.68.0
  - `@posthog/browser-common` 0.12.0
  - `@posthog/types` 1.425.1
  - `core-js`
  - `fflate`
  - `web-vitals`
  - `query-selector-shadow-dom`
- **`package-lock.json`.** This is the lockfile the deployment installs: Oryx runs `npm install` on Node 22, as the deploy log shows. The spec expected every existing pin to stay, but two shared packages moved, because posthog-js needs `dompurify ^3.4.13` and `preact ^10.29.3`:
  - `dompurify` 3.0.8 → 3.4.18, which Mermaid also uses (`^3.0.5`);
  - `preact` 10.18.1 → 10.29.8, which DocSearch also uses (`^10.0.0`).

  Both stay within those ranges, and both are no longer dev-only.
- **`pnpm-lock.yaml`.** Regenerated with `pnpm install --lockfile-only` and pnpm 12.21, which only adds the new entries. pnpm 10.17 would also have dropped the `libc:` fields, so it wasn't used. The pnpm lockfile keeps its own pins (`dompurify` 3.4.16, `preact` 10.29.8).

### 3. `docs/42for.net/.vitepress/theme/analytics.js`

Implemented as specified. `startAnalytics(router)` runs these gates in order:

1. `import.meta.env.SSR`;
2. the host is `42for.net` or `www.42for.net`;
3. Do Not Track or Global Privacy Control, with posthog's "on" values, strings compared case-insensitively.

It then requests `posthog-js/no-external` with a dynamic import that it never awaits. Notes on the result:

- **The build.** The PostHog chunk is a separate file, `module.no-external.*.js`: 431 KB, or 133 KB gzip. No page preloads it. `npm run build` passes, and the SSR render never loads posthog-js.
- **The configuration.** It is the spec's table. One deviation came from the review (section 8): a `before_send` (`scrubEvent`), which the spec didn't have. PostHog takes the query of the entry URL and still sends it, even with `save_campaign_params: false`:
  - as `$session_entry_url` and `$current_url`;
  - with its campaign parameters and ad click ids as `$session_entry_<name>` and `$initial_<name>`, for example `$session_entry_fbclid`.

  `scrubEvent` does three things, and treats `properties`, `$set` and `$set_once` alike:
  - every URL of the site becomes its origin and page, without query or hash;
  - another site's URL becomes its origin;
  - it deletes the admin UI's list of campaign, click-id and keyword properties (`utm_*` included).
- **The token and the environment.** The token is the public project token of `sform` and of the admin UI's local runs. Every event carries `environment: 'production'`.

### 4. Page views from the VitePress router

- **The hook.** `router.onAfterRouteChanged` is wrapped, and an existing handler is chained. After Vue's `nextTick()` the wrapper sends `$pageview`. The loader also sends one for the page the visitor is on once the chunk has loaded, so the first page counts too.
- **Deviation: what counts as the same page.** The spec compared `location.pathname`. The review found that one page has two addresses: a visitor who lands on `/cli/sform#telemetry` (the link `sform` prints) and clicks the page's own sidebar entry (`/cli/sform.html`) sent a second page view, and Back sent a third. Pages are now compared as VitePress resolves them (`pageOf`, which mirrors `normalizeHref` with `cleanUrls` off). `scrubEvent` writes every path and URL as that page, so both addresses count as one page in PostHog.
- **Known limits** (judged not material in the review):
  - When two in-site links are clicked within the load time of one page chunk, the page the visitor ends on is recorded once, but possibly with the previous page's title.
  - A navigation before the PostHog chunk has loaded isn't counted. The page the visitor is on when it loads is.

### 5. Privacy policy (untracked drafts)

**Not applied.** The drafts in `docs/42for.software/legal/` are untracked in the main checkout, so this branch can't change them.

**Corrected wording.** The review found that the spec's intended text for section 4 claimed more than the change delivers: "The 42for.net website sets no cookies and keeps nothing in your browser's storage." Two things make it false:

- VitePress writes `localStorage['vitepress-theme-appearance']` (`auto`) on the first visit, and its local search keeps `vitepress:local-search-filter` (sessionStorage) and `vitepress:local-search-detailed-list` (localStorage) once it is used.
- `cli/mrepo.md` embeds five `www.youtube.com` iframes, which load with the page and set YouTube's cookies and storage. They also send the visitor's IP address to Google in the USA.

The spec must not change after implementation began, so the corrected text is recorded here:

- **Section 2, website row:** "**Website visits** to 42for.net: the pages visited (without their query), the website that linked to them, browser and device details, and the IP address with the approximate location derived from it | To see which documentation helps | Legitimate interest | 30 days".
- **Section 4, last bullet:** "The analytics of the 42for.net website set no cookies and keep nothing in your browser's storage. They use a random id held in memory, which lasts only until you reload or close the page, and send nothing when your browser has Do Not Track or Global Privacy Control turned on. The site itself remembers your light or dark theme, and while you use the search, your last query and view, in your browser's storage."
- **YouTube, while the embeds stay:**
  - Section 4: "The page about `mrepo` embeds videos from YouTube (Google, USA). When it loads, YouTube receives your IP address and may set its own cookies."
  - Section 6: mention Google (YouTube) next to WorkOS.
  - The "In short" EU bullet must not claim that only sign-in leaves the EU.
  - Google is not a processor for this, so it goes in section 5's "Also" list, not its table.
  - If the embeds become click-to-load or plain links (the suggested follow-up), these YouTube sentences shrink to one about starting a video.
- **Sections 5 and 6, otherwise as in the spec:**
  - Section 5: remove the Google Analytics row; PostHog's row becomes "Usage analytics, and website analytics of 42for.net".
  - Section 6: WorkOS is the only one in the USA, apart from YouTube.
- **README.md:**
  - Check "Google Analytics on 42for.net".
  - Add website analytics (30 days) to "The numbers".
  - Keep the "For the lawyer" note on storage, but point it at three things:
    - PostHog's start-up probes (below);
    - the theme key, written as `auto` before the visitor chooses, and whether it is strictly necessary;
    - the YouTube embeds.

### 6. Delete the Google Analytics property

Not done; this is the owner's step. Delete the property of `G-HDDVP9E098` once page views show up in PostHog (Admin → Property → Move to trash).

### 7. Verification

The check used Playwright 1.64 with Chromium against `vitepress preview` of the built site. `42for.net` was mapped to `127.0.0.1` with `--host-resolver-rules`, and `eu.i.posthog.com` was answered locally, so nothing reached PostHog. `Storage.prototype` and `document.cookie` were instrumented.

**Bot filter.** PostHog drops the events of automated browsers (`navigator.webdriver`, `HeadlessChrome`), so the run posed as a regular Chrome. Real visitors are unaffected.

| Scenario | Result |
| --- | --- |
| Home → Introduction (nav) → heading click → Motivation (sidebar) → Back → Back | 4 `$pageview` (home, introduction, motivation, introduction), each with its page's title. None for the heading click or the hash-only Back. One distinct id and session; `environment: production`; `$process_person_profile: false`. Only `POST /e/` |
| Landing on `/introduction?fbclid=…&utm_source=…&mc_eid=…#why` with a full cross-site referrer, then Motivation | URLs without query or hash; no campaign or click-id property; no query in any value; `$referrer` reduced to `https://example.com` |
| Landing on `/cli/sform#telemetry` → its own sidebar link → Back | 1 `$pageview`, as `/cli/sform.html` |
| Reload | A new distinct id and session |
| Do Not Track `'1'`, or Global Privacy Control `true` | The PostHog chunk is never requested; nothing sent |
| Do Not Track switched on after the first page view | Later page views are dropped (`respect_dnt`) |
| `localhost` | The PostHog chunk is never requested |
| Every scenario | No cookies (`document.cookie` empty, none in the browser context). `localStorage` ends with only VitePress's `vitepress-theme-appearance`; `sessionStorage` empty |

**PostHog's transient start-up accesses.** They are as the spec predicted, and nothing stays behind:

- `localStorage`: `__mplssupport__` and `test` are written and removed; `ph_debug` is read and removed; `__ph_opt_in_out_<token>` is read on every consent check;
- `document.cookie`: read once;
- `sessionStorage`: a read and a remove of two `ph_<token>_*` keys.

These go in the lawyer note (section 5). A crafted `#__posthog=` link (the toolbar's authorization, with the public token) can still write `_postHogToolbarParams`. The spec records this; no option prevents it in the `no-external` entry.

### 8. Review

A review in four lenses ran over the branch, with one skeptic per finding trying to refute it:

- runtime correctness;
- privacy and compliance;
- build and deployment;
- documentation and scope.

The build and deployment lens found nothing. These findings stood:

- **The same page counted twice under both its addresses** (low): fixed in `399c76f59` (section 4).
- **Query, campaign parameters and click ids sent** (medium): fixed in `399c76f59` (section 3).
- **The intended privacy text claimed no storage and no cookies for the whole site** (medium): corrected in section 5.
- **This review was missing**: this file.

Refuted as not material:

- the stale title in a double-click race, and the uncounted navigation before the chunk loads (both in section 4);
- the `#__posthog=` toolbar write (known, section 7);
- the wording "after the first render": it is now "in parallel and never awaited";
- the location and retention details of the website row: added to section 5 anyway.

### Out of scope, for follow-ups

- **The YouTube embeds in `cli/mrepo.md`.** Replace them with click-to-load facades or links. Then the whole site sets no cookies.
- **VitePress's theme key.** Set `appearance` so that nothing is written before the visitor picks a theme, if the lawyer asks for it.
- **The lockfile drift.** The dependency PRs bump `pnpm-lock.yaml`, but the deployment installs from `package-lock.json`, which still pins VitePress rc.35.
