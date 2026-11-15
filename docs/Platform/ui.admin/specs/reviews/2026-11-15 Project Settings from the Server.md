# Project Settings from the Server

Linear: [424-266 Project settings on the server: type names, colors, icons, time zone and date format for every client](https://linear.app/424fun/issue/424-266/project-settings-on-the-server-type-names-colors-icons-time-zone-and)

## Overview

This phase delivers the admin UI half of [Project Settings from the Server](../2026-11-15%20Project%20Settings%20from%20the%20Server.md), phase 2 of the server spec. The UI reads the names, colors and icons of the annotation types, the default time zone and the date format of a project from `GET v1/{organization}/{project}/settings` for every member. Administrators change them with one draft and one `PUT` under `If-Match`. Contributors and Readers see them read-only. A banner offers a one-time move of the settings an older UI kept in the browser. The mock API serves and stores the settings as the API does, the showcase seeds them, and the deployed-UI automation tests save them only in disposable projects (spec sections 1 to 8).

| Commit | Subject |
| --- | --- |
| `239308c6b` | feat(platform): read the project settings from the API in the admin UI (424-266) |
| `15a746005` | feat(platform): save the project settings for everyone from the Settings page of the admin UI (424-266) |
| `e328aa9d1` | feat(platform): offer to save the browser's old project settings for the project (424-266) |
| `bb357b312` | test(platform): run the settings automation tests of the admin UI in disposable projects (424-266) |
| `217ba0512` | docs(platform): describe the project settings of the admin UI (424-266) |
| `187fcebb4` | fix(platform): keep the old browser settings of a project opened from Settings of another one (424-266) |
| `2b16199f4` | fix(platform): save the project settings under the version their draft started from (424-266) |
| `262877066` | fix(platform): keep a project's time zone that the browser does not know when saving its settings (424-266) |
| `b1ccacd54` | fix(platform): describe the project settings file of the showcase data set (424-266) |

## What Was Done

Paths are relative to `src/Platform/ui.admin/src/` unless they start with `docs/`, `src/` or `.github/`.

### 1. API shape and mapping

As planned, with these details:

- `app/utils/project-settings.ts`: `ProjectSettingsDraft` (`types`, `defaultTimeZone`, `dateFormat`), `ProjectSettingsState` (adds `version`, `modified`, `storedTimeZone`, `storedDateFormat`), the frozen `defaultProjectSettings` (built-in types, `UTC`, `yyyy-mm-dd`, version 0), `fromApi`, `toApi(draft, base?)`, `isProjectTimeZone` and `usableTimeZone`.
- `toApi` writes the sanitized types in the order of the type codes. It leaves out `DefaultTimeZone` and `DateFormat` when the draft holds the default and the server does not store it, instead of sending `null`. When the server stores the default explicitly (`storedTimeZone`, `storedDateFormat`), `toApi` sends it back, so a `PUT` of an untouched draft changes nothing.
- `fromApi` keeps every zone of the catalog (`isCatalogTimeZone`), also one that this browser's `Intl` does not know (review fix `262877066`). `usableTimeZone` falls back to `UTC` only where the UI computes times, which is the store's `defaultTimeZone` for the New annotation dialog. `isProjectTimeZone` (in the catalog and known to `Intl`) is used only by the old-settings reader. `Intl` alone also accepts offsets such as `+01:00`, which the API refuses.
- Both writes build `If-Match` with the SDK's `projectSettingsEntityTag(version)`.

### 2. Query and store

As planned, with these details:

- `app/queries/project-settings.ts`:
  - `useProjectSettingsQuery()` is the query of the spec (`enabled`, `staleTime` 60 s, `meta.silent`). Its `initialData` is the project's `Settings` from the cached `GET v1/access/projects`, with that list's `dataUpdatedAt`. `App.vue` and the Settings page share it.
  - `useCurrentProjectSettings()` is mounted once in `App.vue`. It feeds the store from a `QueryCache` subscription (`added`, and `updated` on success) for every `['v1', org, project, 'settings']` key, not from a watch on `data`, so the `setQueryData` of a save reaches the store too. For an answer at `Version` > 0 it also removes the browser's old entry of that project, on every page.
- `invalidateProjectSettings(client, org, project)` in `app/queries/invalidation.ts` invalidates the settings and the projects list. It has no caller: the page writes the answer with `setQueryData` and calls `invalidateProject`, which covers the list and the access point.
- `app/stores/project-settings.ts` is rewritten:
  - The state is a `shallowRef<Record<string, ProjectSettingsState>>` with `receive` (skips an answer equal to the last one), `settingsOf`, `loaded`, `version`, `clear`, `current`, `defaultTimeZone` (through `usableTimeZone`) and `dateFormat`.
  - There is no `localStorage`, and `update` and `reset` are gone. `onSignOut` clears the state.
- The layers are unchanged. `date-format.ts` and `type-appearance.ts` read the store as before.
- `AnnotationCreateDialog.vue` follows `projectSettings.defaultTimeZone` while it is open, until the zone is picked (`timeZoneTouched` per opening, `pickTimeZone`).

### 3. The Settings page

As planned, with these details:

- `app/composables/useRole.ts`: the capability `manageProject: 'Administrator'`. The fields are editable only with it and once the settings have loaded (`settingsEditable`).
- `app/pages/[org]/[project]/settings.vue`, the draft and Save:
  - The three settings form one draft that follows the saved settings until it is changed, and starts again on a project switch.
  - **Save project settings** sits below the Annotation types card, with an "Unsaved changes" note, and is enabled when the draft differs. It sends `PUT` with `toApi(draft, draftBase)` and `If-Match: projectSettingsEntityTag(draftBase.version)`, writes the answer with `setQueryData`, runs `invalidateProject` and shows the toast "Project settings saved".
  - Edits made while a save runs stay in the draft.
- `draftBase` (review fix `2b16199f4`) holds the saved settings the draft started from. A refetch under a changed draft moves neither the draft nor the base, so Save gets a `412` instead of storing over another person's change. The base moves when the draft starts again, after a successful save, and after a `412`, so a second Save stores the draft over the newer settings.
- `settingsAnswer` (review fix `187fcebb4`): the query's data counts only when it is the data cached under the current project's key. vue-query moves to the new key in a watcher of its own, which can run after the page's watchers. `savedSettings`, `settingsLoaded` and the banner read it.
- Failures:
  - A `412` (`SettingsPreconditionFailed`, or any precondition failure) refetches the settings. It shows an inline warning, "Someone changed the project settings meanwhile; your changes were not saved", with the action **Load the new settings**.
  - A `400 InvalidSettings` lists the `Details` (path and message) in an inline alert.
  - Any other failure is a toast with the title `writeFailureTitle(error, 'The project settings were not saved')` and the text of `errorMessage(error)`.
  - The draft is kept every time.
- For Contributors and Readers, each of the three cards shows the "Administrator" lock badge (`i-lucide-lock`) and "Only Administrators change the project settings.". The fields are disabled, and there is no Save, no reset and no "Use the default".
- Two **Use the default** buttons, named "Use the default: UTC" and "Use the default: yyyy-mm-dd" for screen readers, are disabled while the draft holds the default. **Reset to built-in** sets the draft's types to `{}`.
- The help texts now say "for everyone in this project". The alerts about personal Preferences stay.
- `useUnsavedChanges(settingsDirty, { message })` guards navigation, and `useHashScroll` waits for the settings query.

### 4. Moving old browser settings

As planned, with these details:

- `app/utils/legacy-project-settings.ts` is the only code that knows the old key (`legacyProjectSettingsKey`). It has `readLegacyProjectSettings`, `removeLegacyProjectSettings` (it removes the key once no project is left) and `legacyProjectSettingsDraft`, and it only reads and removes. An entry counts only for what is not a default: sanitized types, a project time zone other than `UTC`, and a known date format other than `yyyy-mm-dd`.
- The banner "Project settings of this browser" on the Settings page appears while the browser holds an entry and the server's settings are at `Version` 0. It summarizes what the entry changes, and it uses the spec's texts.
  - Administrators get **Save for the project** (a `PUT` with `If-Match: "0"`, which removes the entry on success; a `412` refetches, so the answer removes it) and **Discard**.
  - Everyone else is asked to have an Administrator save them.
- `useCurrentProjectSettings` removes the entry (section 2). The page only reads it again with each answer of the current project (review fix `187fcebb4`).

### 5. Catalog checks

As planned, with these details:

- `test/unit/utils/project-settings-catalog.test.ts` reads `src/Platform/Storyteller/conformance/project-settings.json`:
  - The type codes, colors, icons and date formats equal the UI's, in their order, and so do the 40-character limit, the built-in names, colors and icons, and the defaults.
  - Every zone this Node's `Intl` lists is in the catalog.
  - Every vector goes through `fromApi`/`toApi` and through the UI's sanitizer, and `validateProjectSettings` finds no problem in either result. Every vector with `expected` survives `fromApi` and `toApi` unchanged.
- `app/components/annotations/TimeZoneSelect.vue` offers `UTC` and the `Intl` zones that `isCatalogTimeZone` accepts. While the value is a catalog zone that the browser does not list, the select adds it without an offset (review fix `262877066`).
- `.github/workflows/Platform.UI.Admin.yml` has the catalog in the path filters of both triggers, and `docs/github/workflows.md` follows.

### 6. Mock API and showcase

As planned, with these details:

- `app/mock/project-settings.ts` holds the settings logic of the mock:
  - `projectSettingsOf` and `apiShape` read the settings from `db.accessPoints[key].Settings`.
  - `storeProjectSettings` validates with `validateProjectSettings`. A no-op keeps the version. A change raises it, stamps `Modified` and appends a `ProjectSettingsChange` event with the API's `Detail`, built by `changeDetail`.
  - `patchProjectSettings` applies a merge patch after checking the patch's names. `invalidSettingsMessage` gives the message of a `400`.
- `app/mock/project-settings-handlers.ts` is in `all-handlers.ts` before the Faker handlers:
  - `GET` (Reader) sends an `ETag` and answers `304` for `If-None-Match`.
  - `PUT` and `PATCH` (Administrator) take `If-Match` as the API does (`*` or entity tags; `412 SettingsPreconditionFailed` with `Details.Version`) and answer `400 InvalidPrecondition` for `If-None-Match` and `400 InvalidSettings`. A `PATCH` with another content type than merge patch or JSON answers `415`.
- `app/mock/merge.ts`: `applyMergePatch` (RFC 7396, member names matched case-insensitively).
- `app/mock/handlers.ts` puts `Settings` on `ProjectSummary` and on the `AccessPoint` of `GET`, `PUT` and `POST`.
- `app/mock/showcase/seed.ts` writes each showcase project's settings into its access point at `Version` 1, with a `Modified` stamp of the project's first owner. The other datasets start at `Version` 0. `app/mock/showcase/settings.ts`, its call in `browser.ts` and its test are gone.
- `window.storytellerMock.changeProjectSettings(key, patch)` (`app/mock/browser.ts`, typed in `test/e2e/support.ts`) simulates another person's change.
- `AuditTrail.vue` lists `ProjectSettingsChange` in its operation filter.
- `test/unit/mock/showcase-set.test.ts` writes `DbCreator/src/data-sets/showcase/project-settings.json` (`Format: "2s.project-settings.v1"`, PascalCase, from `fabrikam/invoicing-suite`: types, `Europe/Oslo`, `dd.mm.yyyy`). The seed does not read the file yet, so `DbCreator/test` and `DbCreator/testint` `BuiltInSetsTests` count it as an ignored file, with a comment.
- `app/auto-imports.d.ts` is regenerated from scratch, because the append mode kept stale entries.

### 7. Tests

As planned, with these details:

- **Unit**:
  - New: `utils/project-settings`, `utils/project-settings-catalog`, `utils/legacy-project-settings`, `access/project-settings-query`, `access/settings-project-settings` (the page), `access/settings-legacy-settings` (the banner, mounted with `useCurrentProjectSettings()` first, as in `App.vue`), `annotations/create-dialog-time-zone`, `annotations/time-zone-select` and `mock/project-settings-handlers` (16).
  - `stores/project-settings` is rewritten. `date-format` and `type-appearance` seed with `receive`.
  - `showcase.test` drops the `writeShowcaseSettings` test and checks the seeded and served settings with `validateProjectSettings`. `project-description.test` expects `Settings` on the summaries.
  - Each review fix comes with a test that failed on the old code:
    - a switch from `billing` (`Version` 1) to `shipping` (`Version` 0) keeps the new project's old entry and sends the new project's `If-Match`;
    - an edit, a save by someone else and a refetch, then Save: a `412` keeps the draft, and a second Save goes under `"1"`;
    - a zone that `Intl` does not know survives a save of a type change (`isValidTimeZone` mocked), in the mapping, the store, the select and the page.
- **e2e**:
  - `test/e2e/settings.spec.ts`: an Administrator saves and the settings stay after a reload; a Contributor (`/acme/shipping/settings`) and a Reader (`/northwind/portal/settings`) see them read-only; a `412` through `changeProjectSettings`; the old-settings banner is saved for the project.
  - `test/e2e/annotations.spec.ts`: the existing time zone flow now saves, and new annotations start in the zone from the server.
  - `showcase.spec.ts` needed no change, because its type names now come from the mock DB.
- **Deployed-UI automation** (`src/Platform/UI.Admin.AutomationTests/src`):
  - `Tests/Settings/SettingsSharedTests.cs` (renamed from `SettingsBrowserTests.cs`): SET-BRW-01..04 run as `[UserRole(Role.Owner)] [DisposableProject]`, save with **Save project settings** (toast) and check after a reload. SET-BRW-03 and 04 check that the type colors of the app change only after Save. 04 resets one type and then all, and saves again.
  - `SettingsPreferencesTests.cs`: SET-PRF-02 and 03 work the same way.
  - `SettingsProjectTests.cs`:
    - SET-PRJ-04 is "A Contributor sees the project settings read-only" (`Settings_OpenAsContributor_ShowsProjectSettingsReadOnly`).
    - SET-PRJ-03 shares its check: the badge and text on each card, disabled fields, no Save, no resets, no "Use the default".
    - SET-PRJ-01 checks that an Administrator gets a disabled Save and no badge.
  - The page objects lose the storage key and `ExpectStoredAsync`. They gain the new help texts, `SaveSettingsAsync`, the "Unsaved changes" note, both "Use the default" buttons, the badge and text of each card, `WaitForEditableSettingsAsync`, and the banner with its two buttons.
  - The comments of `ProjectSettingsPage.cs`, `App.SET.cs`, `TypeAppearanceEditor.cs`, `TimeZoneSelect.cs`, `DateFormatSelect.cs` and `AnnotationCreateTests.cs` describe the new behaviour.
  - Testiny `2SP`:
    - Cases 61, 65, 78, 88, 108, 124, 137, 17 and 41 are edited (titles, preconditions, steps), and so is case 69 (ANN-NEW-01), whose precondition owed `UTC` to a fresh browser.
    - Folder 44 is renamed "Shared Settings", and the descriptions of folders 8, 18, 53 and 58 follow.

### 8. Documentation

As planned, with these details:

- `docs/42for.net/platform/admin-ui.md`:
  - The New annotation section: the starting zone is the project's.
  - The roles table: the Settings row (the project settings belong to Administrators) and the Monitor row (the Audit trail lists their changes).
  - The Settings section is rewritten: stored by the API for everyone (a link to `access.md#project-settings`), the one draft and Save, the resets, the `412` and the `400`, the read-only cards, and the move of old browser settings.
  - The time zone list (`isCatalogTimeZone`), with no personal time zone, and a zone the browser does not know (review fix).
- `src/Platform/ui.admin/README.md`:
  - `project-settings` is described as server state (the query, `fromApi`, `If-Match` from `projectSettingsEntityTag`, `utils/legacy-project-settings.ts`), and the layers of `type-appearance`.
  - Three stale passages are fixed too: mock mode (the seeded settings), the `changeProjectSettings` hook and a bullet on the mock settings handlers, and the new e2e flows.
- `docs/Platform/UI.Admin.AutomationTests/test-cases.md`: the heading "Shared Settings (`SettingsSharedTests`)", the six rows as Owner and `B,P`, and the titles of SET-PRJ-04, SET-BRW-01 and SET-BRW-03.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`:
  - The class name, and the counts recounted from the code: Editor 94, Owner 54, B 164, B,P 41, P 48 per desktop run and 41 per phone run.
  - The data rule "never save settings in the target project".
- `docs/Platform/Storyteller/data-sets.md` (review fix `b1ccacd54`): the showcase set's `project-settings.json` and its status as an ignored file. The item "the project settings of the browser" is removed from what the mock has and the set does not.

### Test results

| Command | Result |
| --- | --- |
| `npm run lint` | clean |
| `npm run build` (vue-tsc, vite) | passes |
| `npm test` (`NODE_OPTIONS=--no-experimental-webstorage`) | 116 files, 2209 passed (1962 before the phase) |
| `npm run test:e2e` (desktop and mobile, after the page and the banner) | 410 passed, 14 skipped |
| `npx playwright test test/e2e/settings.spec.ts test/e2e/annotations.spec.ts` (after the review fixes) | 43 passed, 3 skipped |
| `npm run test:visual:docker` | 68 passed, no baseline changed (the settings screenshot is of `#secrets`) |
| `npx vitest run test/unit/mock/showcase-set.test.ts -u` | wrote `data-sets/showcase/project-settings.json` |
| `DbCreator/test` | 135 passed |
| `DbCreator/testint` `Showcase_SeedsWithoutFailures` (container, vNext) | 1 passed |
| `dotnet build src/Platform/UI.Admin.AutomationTests/src` | 0 errors, no warnings in the changed files |
| `UI.Admin.AutomationTests` without a target | 234 skipped, exit 0 |
| `UI.Admin.AutomationTests` against `npm run dev:mock`: `SettingsSharedTests`, `SettingsProjectTests`, `SettingsPreferencesTests` | 4, 4 and 3 passed with `mock` and with `mock,mobile` |

During one e2e run, another server was using port 4174, and Playwright reused it, so the failures of that run do not count. The suite was run again once the port was free. Before each review fix commit, the two page test files, ESLint and vue-tsc ran on that commit's state.

### Reviews

The review confirmed five findings. Two of them were the same defect, and each defect is fixed in its own commit:

1. **Switching projects on Settings deleted the new project's old browser settings before they could be moved** (high; fixed in `187fcebb4`). For a moment the page saw the previous project's answer at `Version` > 0 under the new project's key, and it removed the new project's entry. `settingsAnswer` now accepts only the data cached under the current key, and only `useCurrentProjectSettings` removes entries, per query key.
2. **A save reset a stored time zone that this browser's `Intl` does not know to `UTC` for everyone** (medium; reported twice; fixed in `262877066`). `fromApi` keeps every catalog zone, `usableTimeZone` falls back only for the New annotation dialog, and `TimeZoneSelect` shows the zone while it is the value.
3. **A background refetch moved the `If-Match` version under a changed draft, so Save stored it over someone else's change** (medium; fixed in `2b16199f4`) through `draftBase` (section 3).
4. **`data-sets.md` said that the showcase set has no project settings** (low; fixed in `b1ccacd54`).

Refused: "the mock's `PUT` and `PATCH` accept duplicate member names that the API refuses". The two do differ, but nothing in the UI sends such a body, so this is a gap in how closely the mock follows the API, outside this phase.

### Deviations from the spec

1. `toApi` leaves out a default time zone and date format instead of sending `null`. The SDK's `PUT` type is not nullable, and for a `PUT` the two mean the same. A default that the server stores explicitly is sent back.
2. The store is fed from a `QueryCache` subscription, not from a watch on the query's data, so `setQueryData` after a save reaches it too. The old entry is removed on every page, not only on Settings.
3. Time zones. `Intl` accepts offsets such as `+01:00`, which the API refuses, so the select and the old-settings reader keep only catalog zones that `Intl` knows. After the review, `fromApi` keeps every catalog zone and `usableTimeZone` gives `UTC` where times are computed.
4. The button is labelled **Save project settings**, not "Save", to match the page's other buttons ("Save description", "Save machine policy").
5. The "Use the default" buttons are named "Use the default: UTC" and "Use the default: yyyy-mm-dd" for screen readers, because "Use the default time zone" also matched the locators of "Default time zone". They are disabled while the draft holds the default, so a stored explicit `UTC` stays explicit.
6. The problems of a `400` and the `412` message are inline alerts on the page; only other failures are toasts.
7. A second Save after a `412` stores the draft over the other change. Before that, Save always uses the version the draft started from.
8. An old-settings entry that holds only defaults shows no banner, because saving it would change nothing.
9. The showcase seeds its settings at `Version` 1 with a `Modified` stamp, not at `Version` 0.
10. The mock answers `Settings` on the `AccessPoint` of `PUT` and `POST` as well as `GET`, as the API does.
11. DbCreator's `BuiltInSetsTests` (unit and integration) changed, because the showcase set gained a file that the seed ignores until phase 4.
12. The automation class is renamed `SettingsSharedTests`, and Testiny folder 44 is renamed "Shared Settings", because "Browser Settings" is now wrong. The references stay SET-BRW-01..04. Testiny case 69 and four folder descriptions were edited too.
13. The banner has page-object members but no automation test, because a test needs a new Testiny case and would change the count of 233.
14. Beyond the lines the spec names, the documentation also changed the Monitor row of `admin-ui.md`, three passages of the README, and `data-sets.md`.
15. No visual baseline was regenerated, because none changed.

### Left for later

- DbCreator applies the `project-settings.json` of a data set in phase 4 of the server spec. Until then, the showcase file counts as ignored.
- The target check of the automation tests does not verify that the target project still has default settings (`Version` 0), which the other tests assume.
- An automation test of the old-settings banner, with its own Testiny case.
- The mock's acceptance of duplicate member names (the refused finding).
- When the deployed UI is older than the API's catalog, a save drops the colors, icons or date formats that the UI does not know. This needs a UI and an API of different versions, and the catalog unit test keeps the lists of one build equal.
- `invalidateProjectSettings` has no caller yet.
