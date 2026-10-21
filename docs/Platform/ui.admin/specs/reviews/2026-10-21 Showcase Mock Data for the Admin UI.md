# Showcase Mock Data for the Admin UI

Linear: [424-77](https://linear.app/424fun/issue/424-77/new-mock-data-for-admin-ui).

## Overview

This is the single phase of [Showcase Mock Data for the Admin UI](../2026-10-21%20Showcase%20Mock%20Data%20for%20the%20Admin%20UI.md), sections 1-12.

`npm run dev:mock` now starts the mock API on a new dataset, `showcase`. Fourteen projects in twelve organizations show how the seven annotation types model real setups:
- the six documentation examples;
- the five other examples of the Miro board;
- three new ones.

Each project has:
- its type names, colors and icons;
- the five environment views;
- every lifecycle status, and deletions to restore;
- histories of one, a few and many versions;
- templates, schemas and bindings.

The previous datasets are unchanged:
- `--legacy` selects the standard one (`default`), on which every test still runs.
- `--large` selects the large one.
- `--scenario=<name>` selects any dataset.

The default views hold 3,590 annotations, and all views together hold 7,748. Nothing of the showcase reaches a production build.

Commits:
- `1aa010d1` docs(platform): plan the showcase mock data of the admin UI
- `697c39c3` feat(platform): start the admin UI mock on a showcase dataset, the previous ones behind --legacy and --large
- `97d83648` feat(platform): resolve @config and @annotation bindings in the admin UI mock
- `08e531a2` chore(platform): list the new store exports in the admin UI auto-imports
- `7e3cd00c` feat(platform): model the fourteen showcase projects of the admin UI mock
- `3fc6bd2b` test(platform): cover the showcase dataset in the admin UI end-to-end tests
- `6d3361c5` docs(platform): document the mock datasets and the showcase projects of the admin UI
- `9ceb4adc` fix(platform): keep the dates, history and settings of the showcase consistent
- `554b77da` fix(platform): answer 422 for a binding pointer the admin UI mock cannot decode
- `a8f68e81` fix(platform): keep the showcase B2B SaaS on the documented plans and the views at their planned age
- `919e1807` test(platform): check that an unknown mock scenario keeps the session's dataset and edits

The last four commits come from a review of the whole branch: four reviewers by dimension, with two skeptics per finding.

Deviations from the plan:
- **Dates of documents.** The builder dates a document's versions when the scope is built, not when `configure()` runs. The first version is never before the annotation's creation, and the newest is before its deletion: the spacing shrinks, or the newest version moves back. A deleted annotation's `Modified` is set before its deletion. The plan did not cover this; the review found configurations of seeded deletions that had been saved while they were deleted.
- **Current version.** The current version of a showcase document never expires (`ExpirationTime` is `9999-12-31T23:59:59.999Z`, as `DateTimeOffset.MaxValue` in the API). Older versions keep the 30-day rule. The legacy histories are unchanged.
- **History.** The history of a seeded annotation names the person who created it, while nothing else was recorded for it. The synthetic `Created` entry of `GET …/history` used to say System. This was not in the plan.
- **Writing the showcase settings.** `writeShowcaseSettings` sends a `storage` event, so an open page's project settings store follows a `storytellerMock.reset('showcase')`.
- **`stores/project-settings.ts`.** It exports its storage key as `projectSettingsKey`, so the showcase settings use the same key. Its behavior does not change.
- **Default titles.** They are sentence case ("Production eu") like the generated legacy titles, not title case as section 4.2 says.
- **Extra customers in `invoicing-suite`.** It has 20 customers besides Northwind and Contoso, not 14: six more reach the size. All of them stay on the documentation's two plans.
- **`fitness-cloud` and `conferences` calendar.** These two projects write the calendar dates of their configurations (start dates, edition days, prices from 1 January) for 21 October 2026. The lifecycle and audit dates follow the clock, so statuses stay stable on any day. The fitness test compares the two on one calendar.
- **Launcher exits.** The runner sets `process.exitCode` instead of calling `process.exit`. On Windows, exiting while stderr still flushes to a pipe aborts Node (libuv `UV_HANDLE_CLOSING`).
- **Single test files per project.** Each project has its own test of the facts its model must contain, besides the shared integrity test.

## What Was Done

### 1. Datasets and launcher options

As planned:
- **`build/mock-dataset.ts`:** `mockDatasets`, `defaultMockDataset` and `parseDevMockArguments`.
- **`build/dev-mock.ts`:** runs Vite's `bin/vite.js` through `process.execPath` with `--mode e2e --port 5174 --strictPort` and `VITE_MOCK_DATASET`. It forwards `SIGINT` and `SIGTERM`, and on a wrong option it prints the usage and stops with code 2.
- **`package.json`:** `dev:mock`, `dev:mock:legacy`, `dev:mock:large`.
- **Also:** `VITE_MOCK_DATASET` in `app/env.d.ts`, `showcase` in both unions, and `scenarios` exported from `app/mock/db.ts`.

### 2. Start-up precedence, fresh datasets and stale snapshots

As planned:
- `applyStartScenario(launch)` and `launchScenario(value)` in `app/mock/db.ts`, with the launch marker `2s-admin:mock-launch`.
- `startMockApi` stores the seed of a fresh dataset at once (`resetDb()`). It then runs `startDataset`, which clears the catalog snapshots (`clearCatalogSnapshots`, now exported by `stores/catalog.ts` and reused by its sign-out hook) and writes the showcase settings.
- `storytellerMock.reset(scenario)` does the same.
- `currentScenario()` keeps its `default` fallback.
- Checked in the same tab of the built-in browser: a restart with `--legacy` reloaded the tab into acme and northwind, and `--large` showed 16,317 annotations in `acme/billing/default`.

### 3. Tests stay on the legacy data

As planned: `webServer.env: { VITE_MOCK_DATASET: 'default' }` in `playwright.config.ts`. No existing spec, screenshot or unit test changed.

### 4. The showcase framework

As planned, in `app/mock/showcase/`:
- **`people.ts`:** twelve people, merged into `mockDirectory`.
- **`history.ts`:**
  - `versionHistory` and `evolve`;
  - `personActor`.
- **`builder.ts`:**
  - `ScopeBuilder` with the seven types, the five document kinds, `trash` and a seeded `rng`;
  - `seededRng`, exported for content that must be equal in every view.
- **`types.ts` and `views.ts`:** `environmentViews(refined, extra)`.
- **`index.ts`:** the organizations, the projects, `buildShowcaseScope` and the memoized `showcaseScope`.
- **`seed.ts`:** `createShowcaseSeed`.
- **`settings.ts`:** `writeShowcaseSettings`.

Beyond the plan:
- `deepMerge` moved to `app/mock/merge.ts`; `documents.ts` re-exports it. This way the showcase does not import `documents.ts`, which imports `db.ts`.
- The seed's deletion records copy the shapes of `softDelete`: a `del_seed_<org>_<project>_<n>` id, `Created` and `Deleted` audit entries, and `PurgeAt` 90 days after the deletion. Each project's default view starts with at least one tree with descendants and one deleted leaf, all inside the 90-day retention.
- Building the 14 default scopes for the seed takes about 150 ms once per page. The stored database is about 214 KB.

### 5. Routing the showcase through the mock

As planned:
- `annotationsOf` takes the showcase catalog.
- `baseDocument` takes the showcase documents.
- `documentIds` lists them and keeps the configurations of active annotations only. A restore lists a configuration again.
- The legacy generated ids moved into `generatedDocumentIds`, unchanged.

### 6. Bindings, personas, access and settings

- **6.1 Bindings.** As planned. `resolveBindings(value, db, scope, key)` understands:
  - `@config("…")` and `@annotation("…"[, "<Type>"])`, with a JSON Pointer or a dotted JSONPath;
  - `@<key>/<path>`.
  Sourced values, interpolation and math stay as written. A pointer that cannot be decoded (a stray `%`) answers `422 BindingFailed`, not 500 (review). As a side effect, the seeded `@config("/baseUrl")` of the `default` dataset now resolves.
- **6.2 Personas.** As planned.
- **6.3 Organizations and Ada's roles.** As planned, table for table:
  - Ada is Reader only in `meridian-health/clinical-integrations`.
  - She is Owner in 3 projects, Administrator in 3, ContributorWithSecrets in 3 and Contributor in 4.
  - She is only a project member in `bluefin`, `harbor-industrial`, `meridian-health` and `seabreeze-hotels`.
- **6.4 Views.** As planned:
  - Five registered views per project, plus `playback-14` (streaming) and `campaign-march` (plant fleet).
  - The views of the Miro board follow their frames.
  - `feature-delivery1` was created 21 days ago, not 12 (review).
- **6.6 Settings.** As planned, with the storage event above.

### 7. Content rules

Every default view has:
- active, scheduled, expired, disabled and deleted annotations;
- histories of 1, 2-5 and 15 or more versions;
- at least one template and one schema;
- labels and values;
- own configurations on 60-85 % of the annotations.

Every own configuration satisfies its combined schema in every view. The one exception is `streaming-platform`'s `exe.us.ads.canary`, which is listed in `nonCompliant`: it was saved before the `trafficShare` limit existed.

Bindings appear in every project: `@config`, `@annotation` with a type, and secrets from `primaryVault`.

### 8. Type appearance

As planned. The names follow section 8 and the board, and the colors and icons come from the palette and `typeIcons`. Plurals are given where the English rule fails: People, Firmware, Event series.

### 9. The projects

Counts in the `default` view: active annotations, deleted annotations, own configurations, templates and schemas. The other views follow section 6.4.

| Project | Active | Deleted | Configurations | Templates | Schemas | Other views (active) |
| --- | --- | --- | --- | --- | --- | --- |
| `fabrikam/finance-cloud` | 196 | 12 | 156 | 1 | 2 | development 15, staging 93, release-candidate 196, feature-delivery1 25 |
| `fabrikam/fitness-cloud` | 1,045 | 4 | 710 | 2 | 12 | development 23, staging 16, release-candidate 464, feature-delivery1 20 |
| `fabrikam/invoicing-suite` | 242 | 4 | 155 | 1 | 2 | development 18, staging 39, release-candidate 249, feature-delivery1 14 |
| `bluefin/money-suite` | 339 | 13 | 227 | 2 | 5 | development 35, staging 84, release-candidate 39, feature-delivery1 24 |
| `streamwave/streaming-platform` | 327 | 14 | 209 | 1 | 1 | development 49, staging 93, release-candidate 56, feature-delivery1 18, playback-14 327 |
| `ledgerline/erp` | 192 | 7 | 121 | 1 | 3 | development 31, staging 8, release-candidate 198, feature-delivery1 12 |
| `aurora-motors/vehicle-platform` | 214 | 5 | 152 | 1 | 2 | development 31, staging 68, release-candidate 218, feature-delivery1 10 |
| `harbor-industrial/plant-fleet` | 217 | 14 | 159 | 1 | 3 | development 10, staging 53, release-candidate 224, feature-delivery1 21, campaign-march 217 |
| `meridian-health/clinical-integrations` | 105 | 5 | 73 | 1 | 2 | development 18, staging 10, release-candidate 110, feature-delivery1 13 |
| `appforge/white-label-app` | 122 | 14 | 94 | 1 | 1 | development 8, staging 73, release-candidate 129, feature-delivery1 11 |
| `gatherly/conferences` | 102 | 6 | 66 | 1 | 1 | development 16, staging 62, release-candidate 106, feature-delivery1 10 |
| `pixel-forge/live-ops` | 130 | 5 | 92 | 1 | 2 | development 37, staging 50, release-candidate 136, feature-delivery1 23 |
| `velvet-retail/storefronts` | 127 | 7 | 95 | 2 | 2 | development 27, staging 74, release-candidate 131, feature-delivery1 21 |
| `seabreeze-hotels/guest-platform` | 109 | 13 | 90 | 1 | 1 | development 21, staging 61, release-candidate 116, feature-delivery1 20 |

`invoicing-suite` is the reference project, written first. The other thirteen were each written, reviewed independently against the spec, the documentation and the board, and fixed. Notable points:

#### 9.1 `fabrikam/finance-cloud`

- **Contoso** follows the board exactly: `demo` runs invoicing only, and the only JobRun is `uxe.contoso.reporting.production.nightly`.
- **Lifecycle:**
  - the expired `cnt.litware.trial`;
  - the scheduled `cnt.woodgrove-bank.production`;
  - the deleted `sbt.margies-travel` tree;
  - a disabled bank-sync AppRun in UAT.
- **Development** adds the reporting job on `dev`, the spec's "dev job of reporting", on top of the board frame.
- **Host names** are on `finance-cloud.fabrikam.example`, not `.com`, after the content rule on example domains.
- **Tax filing** goes to countries with a VAT or BAS return, each with a quarterly CRON.

#### 9.2 `fabrikam/fitness-cloud`

- **Members.** There are 534, generated per tenant from Czech, Slovak, German and English name pools. On a clash a username gets a letter, never a number: `jnovakb`. The roster is the same in every view.
- **Usages.** The spec's "about 40 applications enabled per tenant" is read as about 40 usages in all (43).
- **Schemas.** There are twelve: price and currency on usages, plan on assignments, and one plan enum per application.

#### 9.3 `fabrikam/invoicing-suite`

The documentation's catalog, values and both schemas are kept exactly:
- Northwind's export runs at 02:00 in Oslo, and the sandbox export is disabled.
- Contoso keeps the module's schedule through the `uxe` template.

The release candidate brings PEPPOL into invoicing and a new e-archive module. `feature-delivery1` is a PEPPOL gateway.

#### 9.4 `bluefin/money-suite`

- **From the documentation:**
  - Alex's executions follow the documentation's table. There is no `exe.alex.cards.harbor-studio`, investments exist only for `self`, and `usg.alex.cards` exists.
  - Bright Forge's payments use stores exactly `{ limit: 250000, currency: "NOK", vatInvoice: true, approval: "two-eyes-above-50000" }`.
  - Alex's private payments use has no own document, so it keeps the product default.
- **Extension:** 40 people, so the default view (352 including Deleted) is above the "about 250" of the spec but inside its range. The account numbers pass their national check digits.
- **The release candidate** follows the board: cards for Harbor Studio and the new lending product.

#### 9.5 `streamwave/streaming-platform`

- **From the documentation:**
  - `eu` has no billing license.
  - The canary model refresh runs at 02:00, earlier than the unit's 04:00.
  - The canary-only flag sits on `exe.eu.playback.canary`.
  - Services carry `edge`, `ml` or `money` labels and runbook links.
- **Extension:** 8 markets and 20 services.
- **The release candidate** is the board's canary-only slice for every market. `playback-14` is a copy of production with the new codec ladder.

#### 9.6 `ledgerline/erp`

- **From the documentation:**
  - The values (`schemaVersion` 14 and 18, `creditNote`, Contoso's maintenance window and `legacyExport`) are kept exactly.
  - `v2025` is long-term support with its end of support.
  - Contoso's billing rollout is disabled.
  - `cnt.v2024.northwind` (a lifted pin) is in Deleted with its rollouts.
- **Staging** is exactly the board's backport frame.
- **Review fix:** SSO providers became an object of flags rather than an array, so that a channel can turn one off. The mock replaces arrays where the API's merge would union them; see Notes.

#### 9.7 `aurora-motors/vehicle-platform`

- **From the documentation:**
  - The Aurora has exactly the board's builds, fitments, fitted parts and firmware pins.
  - `vin-4F2` is a sibling of the trims with its own battery record.
  - The battery schema restricts the chemistry.
  - The firmware unit is a `file` unit.
- **Extension:** the Borealis and the Zephyr, with recall cases as VIN contexts.
- **`feature-delivery1`** follows the board: ADAS on the Touring trim.

#### 9.8 `harbor-industrial/plant-fleet`

- **From the documentation:**
  - `exe.harbor-plant.control-loop.compressor-1` stores exactly `{ samplePeriodMs: 500, pressureMaxBar: 8.4, alertChannel: "@annotation(\"$.alertChannel\", \"Subject\")" }`, and the binding resolves to the site's value.
  - The control-loop schema caps the pressure.
  - Compressor 2's control loop is disabled for service.
- **Device facts** (model, serial, install date, firmware family) are values, as section 9.8 says. The documentation's table puts them in the device's document instead.
- **`campaign-march`** has the faster sample rate.

#### 9.9 `meridian-health/clinical-integrations`

- **Setting:** a US network of three hospitals.
- **Schema:** integrations require `endpoint` and `certificateRef`, the latter bound to the vault.
- **Ada** reads only, in line with the regulated domain.
- **Staging** is exactly the board's ten keys. That is smaller than the "moderate size" of section 6.4, because the board frame reads as a whole view.

#### 9.10 to 9.14

- **`appforge/white-label-app`:** FreshMart is exactly the board's slice. Development is `android-beta` with Scan & Go.
- **`gatherly/conferences`:** past editions are expired and announced ones scheduled. `feature-delivery1` is exactly the board's matchmaking frame. Prague prices are in CZK.
- **`pixel-forge/live-ops`:** seasonal events are runs with past (expired) and coming (scheduled) windows. Time units run in UTC.
- **`velvet-retail/storefronts`:** VAT rates, carriers and payment methods are real per market. The `it-it` launch is scheduled. Test views never use production endpoints or keys.
- **`seabreeze-hotels/guest-platform`:** the summer-season rate plan is scheduled and a flash sale has expired. OTA and GDS channels carry commission, payment terms and a vault key per hotel.

### 10. Tests

- **`test/unit/mock/showcase.test.ts`**:
  - The dataset rules: roles, Owners, map agreement, the seeded deletions, determinism, and a project through the mock API (views, lists, schemas, Deleted, a restore, the history's creator).
  - The settings written into an open page.
  - Per project: views, sizes, page limits, canonical keys and ancestors, statuses and deletions, history lengths, templates and schemas, current versions, dates within each annotation's life, combined schemas and appearance.
- **Per project:** `test/unit/mock/showcase/<project>.test.ts` checks the documented or board facts of each project. `test/unit/mock/showcase/support.ts` reads a project without the seed, so `vitest -t <project>` builds that project only.
- **Start-up:** `test/unit/mock/start-scenario.test.ts`.
- **Launcher:** `test/unit/build/mock-dataset.test.ts`, which also runs the launcher with Node.
- **Bindings:** `test/unit/configuration/binding-resolution.test.ts`.
- **End-to-end:** `test/e2e/showcase.spec.ts`, desktop and mobile. Type names are read from the grid cells on a desktop and from the icon labels of the list on a phone, behind a `type:` filter, because the grid is virtualized.

Results on the last commit, from `src/Platform/ui.admin/src`:
- `npm run typecheck` and `npm run lint` pass.
- `npm test`: 91 files and 1,384 tests pass. Two runs in a row passed.
- The showcase unit tests also pass with the clock moved 1, 45, 200 and 400 days ahead.
- `npm run test:e2e`: 348 tests pass and 8 are skipped (those were already skipped).
- `npm run test:visual:docker`: 64 tests pass, with no screenshot change.
- A production `vite build` contains no showcase code.

### 11. Documentation

As planned:
- the Mock mode section of `src/Platform/ui.admin/README.md`: options, precedence, datasets, the project table with type names and Ada's role, and how to add a project;
- the scripts table, the e2e list and the Resolved read in the README;
- the `dev:mock` lines in the root `CLAUDE.md`.

## Notes

- **The mock's merge semantics.** The mock's `deepMerge` replaces arrays; the API's configuration merge unions them along the hierarchy (configuration.md). A project that narrows an inherited array looks right in the mock and would differ on the server, so the showcase avoids that pattern. A mock merge closer to the API's would remove the trap.
- **The order of combined schemas.** The mock's `combinedSchema` merges descendant-type schemas in `getAllAncestors` order, not in merge order. A site's schema in `plant-fleet` therefore overrides the capability's.
- **Even version spacing.** Histories are evenly spaced (`every`). A view therefore cannot add one newer version while it keeps production's older version dates exactly. The release candidates keep only production's latest date.
- **A slow unit test.** `test/unit/router/page-memory.test.ts` once timed out in its `beforeEach` (10 s) during a full run, before any showcase test existed. It passed on its own and in every later full run.
