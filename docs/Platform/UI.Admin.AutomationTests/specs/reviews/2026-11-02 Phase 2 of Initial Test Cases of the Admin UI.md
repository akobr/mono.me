# Phase 2 of Initial Test Cases of the Admin UI: the Framework

## Overview

Phase 2 of [2026-11-02 Initial Test Cases of the Admin UI and Their Automation](../2026-11-02%20Initial%20Test%20Cases%20of%20the%20Admin%20UI%20and%20Their%20Automation.md) (Linear [424-152](https://linear.app/424fun/issue/424-152/create-initial-tests-cases-and-automate-them-for-admin-ui)) covers §3 and the move of the six existing tests (§1). It adds three things:

- A pre-lease skip hook in the library.
- To the Platform project: per-test roles and mode attributes, `Target:RoleProjects`, the helpers, every route of the catalogue, the fixes of the known weak spots and collision-proof generated names.
- The page objects that more than one section uses.

Three commits: `74e730a9` (library), `19aff4a8` (core of the Platform project), `e1c34dae` (shared page objects).

## What Was Done

### 3.1 The pre-lease skip in `UiTest`

- `UiTest.GetSkipReason(TestContext context, AutomationSession session)` is virtual and returns null by default.
- `BeginUiTest` calls it through the internal `SkipWhenAsked` right after `UiAutomationSession.Require()` and before `TestScope.Create`. A reason calls `Skip.Test`. The scope stays null, so no user is leased and no context or page is opened. `ReportResults` reports the test as Skipped, with the reason as `failure.message` in `results.jsonl`.
- Three xUnit tests in `src/Libraries/Testing/UI.Automation.Runtime.TUnit/test/UiTestTests.cs` cover it.
- The library tests pass:
  - Runtime.TUnit: 31.
  - Runtime: 227.
  - Reporting.Testiny: 111.
- `docs/Libraries/Testing/UI.Automation/readme.md` documents the hook: the `UiTest` table, a paragraph with an example, and the run steps.

### 3.2 Target settings

- `PlatformTarget.IsMock`: `MockScenario` is set, or `SignIn` is `None`.
- `PlatformTarget.RoleProjects`: a case-insensitive dictionary from a role to `<org>/<project>[/<view>]`.
  - **Deviation:** any `Role` except `Any` may have an entry, not only Contributor and Reader. A mapped role acts in that project with any pool user. The mock maps only Contributor (`acme/shipping`) and Reader (`northwind/portal`).
- `PlatformAutomationSetup.Validate` checks the role names and both name parts, and names the setting in its errors (`Target:RoleProjects:<role>`).
- `PlatformTargetCheck` reads `/orgs` once more. For each entry, the project's link must exist and must name the role. A wrong mapping, for example `Reader=acme/shipping`, fails the check: "… is not Reader there: its link on the organizations reads 'shipping Contributor'". A missing project fails it with the user's projects listed.
- `testsettings.json` documents an empty `RoleProjects`, and `testsettings.mock.json` sets the two entries.

### 3.3 Roles and modes

- In the root namespace, so a test needs no using:
  - `Area` gets `Graph` and `Monitoring`.
  - The new attributes and types: `Role`, `[UserRole]`, `[DesktopOnly]`, `[PhoneOnly]`, `[DisposableProject]`, `[MockOnly(why)]` and `PlatformCategories` (the category names).
  - **Deviation:** `[MockOnly]` takes a required reason, reported as "Mock only: <why>.".
- `PlatformTest.Roles.cs`:
  - `ActingRole` is the `[UserRole]` of the method, else of the class. It is read by reflection and cached, because TUnit's metadata keeps only attributes of TUnit types.
  - `RoleProject`; `Routes`, which is `AppRoutes.Parse(RoleProject)` for a mapped role.
  - `RequiredUser`: Editor maps to `EditingUser`, Admin to `AdminUser`, Secrets to `SecretsUser`, and Owner, Contributor and Reader to `WithRole`. A mapped role uses `Any`.
  - The `GetSkipReason` override:
    - `[DesktopOnly]` skips below 1024 px and `[PhoneOnly]` at 640 px and wider. Both read the session's viewport (`ContextOptions.Viewport`).
    - On the mock, a requirement that no pool user satisfies is skipped. On a live target such a test stays Blocked.
- Checked with temporary probes (deleted, not committed):
  - The skip reasons in `results.jsonl` on desktop and on `mobile` (`Desktop only: the viewport is 412 px wide (Pixel 7), below the desktop layout's 1024 px.`).
  - A Reader test in `northwind/portal` without Bulk create, and a Contributor test in `acme/shipping`.
  - An unsatisfiable Administrator requirement skipped on the mock.

### 3.4 Helpers

`PlatformTest.Helpers.cs` and `Support/`:

- `CreateDisposableProjectAsync(description?)`:
  - It requires an Owner and creates `PlatformData.ProjectKey()` through the organization overview.
  - It asserts the toast "Project <key> created" itself and waits for the project's catalog. A test does not assert the toast again.
  - It returns the routes of the project's `default` view, and adds the project to the report's properties.
- `InstallClipboardRecorderAsync`, `CopiedTextAsync`, `WaitForCopiedTextAsync`: an init script records `writeText`, `write` (text/plain) and `execCommand('copy')` into `window.__copied`. Checked with "Copy to clipboard" on `/account`.
- `InstallRequestRecorderAsync`, `RecordedRequestsAsync`: a `fetch` wrapper fills `window.__requests` with `RecordedRequest(Method, Url, Body)`. Checked with the account load and the POST of Bulk create.
- `DownloadAsync(action, timeoutMs = 60000)` returns `DownloadedFile(SuggestedFilename, Content)` with `.Text`, and never calls `PathAsync`. Checked with `storyteller-ca.pem` from Settings.
- `MockApi` (mock only; it throws on a deployed target):
  - `FetchAsync(method, path, body)` against `/api/` once the service worker controls the page, returning `MockApiResponse(Status, Body)`. **Deviation:** the API base `/api` is fixed, from `.env.e2e`, because the e2e build's `app-config.json` is empty.
  - `ChangeConfigurationAsync(key, changes)`, through `window.storytellerMock` (saved as Grace Hopper).
- `OpenSecondPageAsync()`: a new `App` on a second page of the context, with the mock scenario on its first navigation.

### 3.5 Routes, data, shell helpers and fixes

- **Partial classes**: `AppRoutes`, `App`, `PlatformData`, `PlatformTest` and every page object are `partial`, so a section adds members in `<Type>.<Section>.cs`.
- **`AppRoutes`**:
  - It has every route of the catalogue: the global, organization, project, view, key-value, graph and modeling routes, the configuration tabs, `Login(returnTo, error, errorDescription)`, `Callback`, `InvitationAccept`, the old paths of the merged pages, `WithQuery` and `Parse`.
  - **Deviation:** `ProjectRoot` is the name of `{org}/{project}`, because `Project` is the record's positional member.
  - **Deviation:** `Settings(section?)` and `Monitor(tab?)` are methods, because they take an optional hash or tab.
- **`App`**:
  - `Toast(…)` returns the last match, and `ToastAction(title, action)` clicks a button of the newest toast (Undo).
  - `MenuAsync`, `CloseMenuAsync`, `OpenUserMenuAsync`, `UserMenuItem`, `SignOutAsync`, `SignedInNameAsync`, `OpenPaletteAsync`, `PaletteOption` and `ExpectQueryAsync`.
  - `NavigationDialog` now matches the slideover's names only, not any dialog. `NavigateAsync` and `MenuAsync` use the slideover whenever "Open navigation" is visible, so an unpinned sidebar on desktop works too.
- **Fixes**:
  - `AnnotationsPage.FilterAsync` and the new `ConfigurationsPage.FilterAsync` wait for the exact `q` (an empty query waits until `q` is gone).
  - `Item(key)` matches the exact key.
  - `ConfigurationDetail.SaveAsync(expectedVersion)` waits for the exact toast.
  - `LoginPage` takes `App`, so its first navigation carries the mock scenario. `AuthKitSignIn` uses `new LoginPage(new App(page))`.
- **`PlatformData`**:
  - Every generated word part starts with `Data.Hex(4)` (for example `at-20261102-012345-k3x9-prj-6193-qui-ea`). **Deviation:** the key-value keys keep `<word>_<hex4>`, because a key-value segment must not start with a digit.
  - New generators: `ViewName`, `Slug(kind)`, `LabelName`, `Title`, `Text(length)` (exact length, e.g. 501), a run-tagged JSON document, `RecipientId`, `RsaPublicKeyPem` and `RsaPublicJwk`.
  - The "Data rules" of `automation-tests.md` show the new shapes.

The shared page objects (Phase 2b), each a `sealed partial` class written from the Vue component and the dossiers:

- **New**:
  - `ConfirmDialog`: one class for both `ConfirmDialog.vue` and the `useConfirm` modal, with typed confirmation from the placeholder.
  - `DiscardChangesDialog`.
  - `CodeEditor`: the CodeMirror helper; `ConfigurationDetail` is built on it.
  - `DetailPanel`: the modes, pin, full screen, resize, close and back.
  - `TimeZoneSelect`, `NewAnnotationDialog`, `AccountPage`, `ContextSwitcher`, `ModelingPage`, `ModelingSchemaPage`, `ModelingTemplatePage` and `VersionsView`.
- **Extended**:
  - `AnnotationsPage`: `EmptyState`, `OpenNewAnnotationAsync`.
  - `AnnotationDetail`: the panel, the menu, `DeleteAsync`, `CloseAsync`, and the graph's section.
  - `ViewsPage.CreateViewAsync`.
  - `OrganizationOverview`: the project card and its description.
  - `ProjectSettingsPage`:
    - The machine policy, the CA download and the default time zone.
    - The secrets section: allowed levels, default level, recipients.
  - `KeyValuesPage`: the exact item, the detail values, `CreateSecretAsync`, `RevealAsync`, the level badge.

Bugs found while probing them, fixed in the page objects:

- On an emulated phone, CodeMirror keeps a `\r` from `\r\n`, so the editor now inserts `\n`.
- Playwright does not escape `/` in a regex locator, so `TimeZoneSelect` escapes it (`Asia/Tokyo`).
- Vue leaves a leading space in some texts, so the anchored regexes allow it.
- The part combobox stays open after `Create "<name>"`, so `FillPartAsync` closes it by clicking the dialog title.

### 1. The six existing tests

They moved, with their method names, `[TestCase]` and `[Elementary]`, a comment with their reference, and `[UserRole]` instead of a class-level `RequiredUser`:

| Case | New class |
|---|---|
| 1 | `Tests/Access/SignInTests` (unchanged class) |
| 2 | `Tests/Annotations/AnnotationBulkCreateTests` |
| 3 | `Tests/Configurations/ConfigurationCreateTests` |
| 4 | `Tests/KeyValues/KeyValueCreateTests` |
| 5 | `Tests/Projects/ProjectTests` |
| 6 | `Tests/Navigation/NavigationMenuTests` |

The old files are deleted.

### Verification

- `dotnet build src/Platform/UI.Admin.AutomationTests/src --no-incremental`: 0 warnings, 0 errors.
- Against the mock e2e build on `:4174`:
  - `mock`: 6 passed (25 s).
  - `mock,mobile`: 6 passed (16 s).
  - `/**[Category=Elementary]`: 6 passed.
  - Without a target: every test skipped, exit 0.
- Temporary probes, deleted and not committed:
  - Phase 2a: 15 probes, 13 passed and 2 skipped by their mode on each profile.
  - Phase 2b: 7 probes, all passed on desktop, at 1100 px (icon rail), at 800 px and on `mobile`.
