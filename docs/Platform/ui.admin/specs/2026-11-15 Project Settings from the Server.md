# Project Settings from the Server

Linear: [424-266 Project settings on the server: type names, colors, icons, time zone and date format for every client](https://linear.app/424fun/issue/424-266/project-settings-on-the-server-type-names-colors-icons-time-zone-and)

## Problem

The admin UI keeps three settings of a project in each browser's `localStorage` (`2s-admin:project-settings`): the names, colors and icons of the annotation types (424-13), the default time zone of new annotations (424-12) and the date format (424-23). Contributors and up change them for their own browser only; nobody else sees the change. The server half of 424-266 ([Project Settings on the Server](../../Storyteller/specs/2026-11-15%20Project%20Settings%20on%20the%20Server.md)) stores them on the project and serves them by `GET`, `PUT` and `PATCH v1/{organization}/{project}/settings`, writable by Administrators only. The admin UI has to read them from the server for every member, write them to the server only, keep personal Preferences as an override, offer a one-time move of old browser settings, and test all of it with its mock API and its automation tests.

The branch `feat/424-266` starts from `main` at `7944671e`; phase 1 of the server spec (the API and both SDKs) comes first.

## Current State

Paths are relative to `src/Platform/ui.admin/src/` unless they start with `docs/` or `src/`.

### Stores

- `app/stores/project-settings.ts`: `ProjectSettings { defaultTimeZone, dateFormat, types }` per `org.project` in `localStorage[projectSettingsKey]` (`useStorage`); `settingsOf(key)` sanitizes (invalid zone → `UTC`, unknown format → `yyyy-mm-dd`, `sanitizeTypeAppearances`); `current`, `defaultTimeZone`, `dateFormat`; `update(key, patch)` and `reset(key)`.
- Readers: `app/stores/date-format.ts` (personal `preferences.dateFormat` over the project's), `app/stores/type-appearance.ts` (`mergeTypeAppearances(personal, project)`, `applyTypeAppearance` into `typeMeta`, the `<style id="st-type-colors">` element), both created once in `App.vue` (l.10-13); `app/components/annotations/AnnotationCreateDialog.vue` (l.32, 42, 46-51, 156: the starting time zone); `app/mock/showcase/settings.ts` (writes the showcase projects' settings into the same key at dataset start, `app/mock/browser.ts` l.29-34).
- No store uses Vue Query; `useQueryClient` fails in the store unit tests (Pinia without an app).
- `app/stores/preferences.ts`: the personal layer (`2s-admin:preferences`: `types`, `dateFormat`), unchanged by this spec.

### Rules

- `app/utils/type-appearance.ts`: `TypeColor` (18), `typeColors` with light and dark shades, `builtInTypeColors`, `typeIcons` (66 `i-lucide-*` literals, kept literal for Vite's icon scan and the CSP), `maxTypeNameLength = 40`, `sanitizeTypeAppearances` (drops unknown codes, colors and icons, a plural without a label, empty types), `pluralize`, `mergeTypeAppearances`, `resolveTypeAppearances`.
- `app/utils/dates.ts`: `DateFormat` (5), `isDateFormat`, `dateFormatItems`. `app/forms/annotation.schema.ts`: `isValidTimeZone` (`Intl`).

### The Settings page

- `app/pages/[org]/[project]/settings.vue`: the **Annotations** card (Default time zone, `TimeZoneSelect`, help "Saved in this browser."), the **Dates** card (Date format, help "…for everyone who opens it in this browser…", an alert when personal Preferences override), the **Annotation types** card (`TypeAppearanceEditor`, an alert when `hasPersonalTypes`, footer "Reset to built-in"). Writable computeds apply each change at once, gated by `canEdit = useCan('edit')` (Contributor). `canAdmin = useCan('manageAccess')` and the "Administrator" lock badge already exist for the Machine authentication card (l.310-321). The description edit (l.93-128) is the server-write pattern (draft, `invalidateProject`, toast).
- `app/components/annotations/TypeAppearanceEditor.vue` emits the whole layer (`v-model`), with a preview per type.
- Version and conflict patterns: `SecretsPolicyCard.vue` (body `ExpectedVersion`, inline "changed meanwhile"), `KeyValueDetail.vue` and `TemplateDetail.vue` (`412` → refetch). No UI code sends `If-Match`; the deployed UI cannot read `ETag` cross-origin (`docs/Platform/Storyteller/authentication.md`, CORS).

### Queries

- `app/boot/query.ts` defaults (`staleTime` 30 s). `app/queries/projects.ts` `useProjectSummaries()` (`GET v1/access/projects`). `app/queries/scope.ts` holds project-scoped wrappers (`useKeyValuePolicy`, `staleTime` 60 s, `meta.silent`). `app/queries/invalidation.ts` (`invalidateByPath`, `invalidateProject`). `main.ts` clears the query cache on sign-out.

### Mock API

- `app/mock/all-handlers.ts` lists the hand-written handlers; the SDK's Faker handlers answer anything else in `dev:mock` and e2e. `app/mock/handlers.ts` has `errorResponse`, `roleOf`, `requireRole`, `withDb`; the mock DB (`app/mock/db.ts`, `sessionStorage`) has `accessPoints`; the showcase seed (`app/mock/showcase/seed.ts`) and its projects' `settings` (`app/mock/showcase/types.ts` `ShowcaseSettings`). No mock reads `If-Match` or sends `ETag`; `app/mock/merge.ts` `deepMerge` does not delete on `null`.
- `test/unit/mock/showcase-set.test.ts` writes DbCreator's `data-sets/showcase/` as file snapshots.

### Tests

- Unit: `test/unit/stores/project-settings.test.ts`, `date-format.test.ts`, `type-appearance.test.ts` (seed the project layer with `update`), `test/unit/access/settings-description.test.ts` (the page test pattern), `test/unit/utils/type-appearance.test.ts`, `test/unit/mock/showcase.test.ts` (l.204-214 `writeShowcaseSettings`).
- e2e (mocked, Ada = Owner of `acme.billing`, Contributor of `acme.shipping`, Reader of `northwind.portal`): `test/e2e/settings.spec.ts` (type appearance l.70-109, date format l.128-158), `annotations.spec.ts` l.150-179 (time zone), `showcase.spec.ts` l.54-60 (type names), `accessibility.spec.ts`, `visual.spec.ts` (`settings#secrets`).
- Deployed-UI automation (`src/Platform/UI.Admin.AutomationTests`): `Tests/Settings/SettingsBrowserTests.cs` (SET-BRW-01..04, Testiny 65, 78, 88, 108, as `Role.Editor`, checking `localStorage`), `SettingsProjectTests.cs` (SET-PRJ-04, Testiny 61 "A Contributor changes the browser settings but not the project-wide ones"; SET-PRJ-01 and 03), `SettingsPreferencesTests.cs` (SET-PRF-02, 03: the Editor sets the project's layer), page objects `Pages/ProjectSettingsPage*.cs` (storage key, help texts), `Pages/App.SET.cs` (`ExpectStored…` over `localStorage`), `TypeAppearanceEditor.cs`, `TimeZoneSelect.cs`, `DateFormatSelect.cs`; the catalogue `docs/Platform/UI.Admin.AutomationTests/test-cases.md` (Settings, l.522-573). Other tests assume the target project keeps the built-in names, `UTC` and `yyyy-mm-dd`.

### Documentation

- `docs/42for.net/platform/admin-ui.md` l.107 (time zone of new annotations), l.237 (Settings: Contributor), l.298-311 ("kept in your browser until the API stores project settings"); `README.md` l.249 (client state).

## Proposed Changes

### 1. API shape and mapping (`app/utils/project-settings.ts`)

- The API answers `ProjectSettings { Types: { [code]: { Label?, Plural?, Color?, Icon? } }, DefaultTimeZone?, DateFormat?, Version, Modified? }` (PascalCase, lower-case codes). `fromApi(settings)` maps it to the UI's `{ types: TypeAppearances, defaultTimeZone: string, dateFormat: DateFormat, version: number, modified? }` (defaults `UTC` and `yyyy-mm-dd`, then `sanitizeTypeAppearances` and the zone and format guards as defense in depth); `toApi(draft)` maps back, leaving out defaults that were not set (`null` time zone and date format when the draft has the default and the server had none, so a PUT of an untouched draft changes nothing).
- `If-Match` is built from the body's `Version` with the SDK's `projectSettingsEntityTag(version)` (`"<Version>"`), because the browser cannot read `ETag` across origins.

### 2. Query and store

1. **Query** `useCurrentProjectSettings()` in `app/queries/project-settings.ts`: `useGetProjectSettings(org, project, { query: { enabled: () => !!session.account && !!context.projectKey && context.access === 'ok', staleTime: 60_000, meta: { silent: true }, initialData, initialDataUpdatedAt } })`, where `initialData` is the project's `Settings` from the cached `GET v1/access/projects` (`getGetProjectsQueryKey()`) when that list is loaded. It runs once in `App.vue`, next to the two app-wide stores, and feeds the store with `receive(projectKey, settings)`. `invalidateProjectSettings(client, org, project)` joins `app/queries/invalidation.ts`.
2. **Store** `app/stores/project-settings.ts` keeps its read API (`settingsOf`, `current`, `defaultTimeZone`, `dateFormat`) so `date-format.ts`, `type-appearance.ts` and `AnnotationCreateDialog.vue` read the server's settings unchanged. State is a `shallowRef<Record<string, ProjectSettingsState>>` filled by `receive` (no `localStorage`); `version(key)` and `loaded(key)` are added; `update` and `reset` are removed (writes are server calls, section 3); `onSignOut` clears it. Until the first answer arrives a project shows the built-ins (no flash of wrong values, only of defaults).
3. **Layers** stay as they are: type appearance per field personal → project (server) → built-in; date format personal → project (server) → `yyyy-mm-dd`; default time zone project (server) → `UTC`, no personal override.
4. **Create dialog.** `AnnotationCreateDialog.vue` also follows `projectSettings.defaultTimeZone` while it is open and the time zone was not touched, so a dialog opened before the settings arrive moves to the project's zone.

### 3. The Settings page

1. **Gate.** A new capability `manageProject: 'Administrator'` in `app/composables/useRole.ts`. The time zone, date format and type fields are editable for Administrators and Owners only; for Contributors and Readers they are read-only, and each of the three card headers shows the lock badge ("Administrator", `i-lucide-lock`) as the Machine authentication card does, with "Only Administrators change the project settings." below.
2. **Draft and Save.** The three settings form one draft (`{ types, defaultTimeZone, dateFormat }`) that follows the loaded settings until edited and resets on a project switch (the description pattern). One "Save" button below the Annotation types card (Administrators only, enabled when the draft differs) sends the whole draft with `PUT …/settings` and `If-Match: "<Version>"`, then writes the answer into the query (`setQueryData`), invalidates the projects list, and shows the toast "Project settings saved". "Reset to built-in" sets the draft's types to `{}`, and the time zone and the date format each get a "Use the default" action that sets them to `UTC` and `yyyy-mm-dd` (sent as unset). `useUnsavedChanges(dirty)` guards navigation. The type editor's own previews show the draft; the rest of the app keeps the saved settings until Save.
3. **Failures.** A `412 SettingsPreconditionFailed` refetches the settings and shows "Someone changed the project settings meanwhile; your changes were not saved" with the draft kept and an action to load the new settings; a `400 InvalidSettings` lists the problems; any other failure shows "The project settings were not saved" with `errorMessage(error)`. The draft is kept on every failure.
4. **Copy.** "Saved in this browser" and "for everyone who opens it in this browser" become "for everyone in this project". The alerts about personal Preferences (`hasPersonalTypes`, the personal date format) stay.
5. `useHashScroll` waits for the settings query too.

### 4. Moving old browser settings

- **When.** The browser holds an entry for the project in `2s-admin:project-settings` (any of `types`, `defaultTimeZone`, `dateFormat` that sanitizes to something) and the server's settings are at `Version` 0.
- **Administrators** see a banner on the Settings page: "These project settings were set in this browser before they were shared. Save them for the whole project?" with **Save for the project** (a `PUT` with `If-Match: "0"` of the sanitized local values; on success the entry is removed) and **Discard** (removes the entry).
- **Other roles** see "These project settings were set in this browser before they were shared. Ask an Administrator of the project to save them for everyone." and no action.
- The entry is removed once the server has settings (`Version` > 0). `app/utils/legacy-project-settings.ts` reads and removes entries; nothing else reads the old key and nothing writes it.

### 5. Catalog checks

- The UI keeps its literal `typeColors`, `typeIcons` (Vite's icon scan) and `dateFormats`. A unit test reads `src/Platform/Storyteller/conformance/project-settings.json` and checks that the colors, the icons, the date formats, the 40-character limit, the built-in names, colors and icons, and the defaults equal the catalog, and runs the catalog's vectors through `validateProjectSettings` of the SDK against the UI's own sanitizer (whatever the UI keeps, the server accepts).
- The time zone choices of `TimeZoneSelect` are the zones `Intl` knows that the catalog lists (`isCatalogTimeZone`), so the UI never offers a zone the server refuses.
- `.github/workflows/Platform.UI.Admin.yml` triggers on `src/Platform/Storyteller/conformance/project-settings.json` too.

### 6. Mock API and showcase

- `app/mock/project-settings-handlers.ts` (in `all-handlers.ts` before the Faker handlers): `GET /:org/:project/settings` (Reader; `ETag`; `304` for `If-None-Match`), `PUT` and `PATCH` (Administrator; `If-Match` as the API: `*`, strong tags, `412 SettingsPreconditionFailed` with `Details.Version`, `400 InvalidPrecondition` for `If-None-Match`; `400 InvalidSettings` with the problems of the SDK's `validateProjectSettings`; `415` for a PATCH content type other than merge patch or JSON; a no-op keeps the version; a change raises it, stamps `Modified` and appends a `ProjectSettingsChange` event to the mock's audit trail). The settings live in the mock DB on the access point (`db.accessPoints[key].Settings`), and the projects handler returns them in `ProjectSummary.Settings`. An `applyMergePatch` helper (RFC 7396, case-insensitive members) joins `app/mock/merge.ts`.
- The showcase seed writes each project's `settings` into its access point; `app/mock/showcase/settings.ts`, its call in `browser.ts` and its test go. A `window.storytellerMock.changeProjectSettings(key, patch)` hook (as `changeConfiguration`) lets e2e simulate another person's change.
- `AuditTrail.vue` lists `ProjectSettingsChange` in its operation filter.
- `test/unit/mock/showcase-set.test.ts` writes `data-sets/showcase/project-settings.json` in the file shape (`Format: "2s.project-settings.v1"`, PascalCase) from `fabrikam/invoicing-suite` (types, `Europe/Oslo`, `dd.mm.yyyy`).

### 7. Tests

- **Unit**: the store (`receive`, per project, follows the context, sanitizes, clears on sign-out); the layering (types personal → server → built-in; date format personal → server → default; time zone server → `UTC`); the query seeding from the projects list; the page: an Administrator edits, saves (one `PUT` with `If-Match`), resets, gets a `412` (draft kept, message, reload), a `400`; a Contributor and a Reader see read-only fields with the badge and send nothing; the banner for an Administrator (save, discard) and for a Contributor; the mock handlers (roles, versions, `412`, `304`, validation, merge patch); the catalog checks; the create dialog following a late answer.
- **e2e** (`settings.spec.ts`, `annotations.spec.ts`): an Administrator saves, reloads and still sees the settings; a Contributor (`/acme/shipping/settings`) and a Reader (`/northwind/portal/settings`) see them read-only; the create dialog starts in the project's time zone; a `412` through the mock hook; the showcase type names come from the mock DB. Visual baselines are regenerated with `npm run test:visual:docker`.
- **Deployed-UI automation** (`UI.Admin.AutomationTests`): SET-BRW-01..04 and SET-PRF-02, 03 run as `[UserRole(Role.Owner)] [DisposableProject]`, save, and check after a reload instead of `localStorage`; SET-PRJ-04 becomes "A Contributor sees the project settings read-only" (Testiny 61 retitled); SET-PRJ-01 and 03 check the Save button and the badge; the page objects lose the storage key and get the new texts, the Save button and the banner; the Testiny `2SP` cases (61, 65, 78, 88, 108, 124, 137) and `test-cases.md` follow. No test writes settings of the shared target project.

### 8. Documentation

- `docs/42for.net/platform/admin-ui.md`: project settings are stored by the API for everyone, changed by Administrators, personal Preferences still override the type appearance and the date format; the move of old browser settings.
- `README.md` (client state), `docs/Platform/UI.Admin.AutomationTests/test-cases.md` and `automation-tests.md`.

### 9. Phase and commits

Phase 2 of the server spec. Commits (subjects end with `(424-266)`): `feat(platform): read the project settings from the API in the admin UI`; `feat(platform): save the project settings for everyone from the Settings page of the admin UI`; `feat(platform): offer to save the browser's old project settings for the project`; `test(platform): run the settings automation tests of the admin UI in disposable projects`; `docs(platform): describe the project settings of the admin UI`; the review `docs/Platform/ui.admin/specs/reviews/2026-11-15 Project Settings from the Server.md`.

### 10. Verification

```bash
cd src/Platform/Storyteller/sdk.typescript/src && npm ci && npm run prepare:local
cd src/Platform/ui.admin/src && npm ci && npm run prepare:local
npm run lint && npm test && npm run build && npm run test:e2e
npx vitest run test/unit/mock/showcase-set.test.ts
dotnet build src/Platform/UI.Admin.AutomationTests/src
```

### 11. Differences from the issue

1. **Resets change the draft**, and Save sends them, instead of each reset sending a write at once: every change on the page then goes through one Save with one condition.
2. **One Save for the three settings** (one `PUT`, one version) instead of a Save per card.
3. **A capability `manageProject`** (Administrator), one of the two options of the issue.

### Out of scope

- Personal Preferences on the server; an app-wide live preview of an unsaved draft; settings in view exports and imports.
