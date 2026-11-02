# Initial Test Cases of the Admin UI and Their Automation

Linear: [424-152](https://linear.app/424fun/issue/424-152/create-initial-tests-cases-and-automate-them-for-admin-ui)

Line numbers and file paths refer to `main` at `87892691`. This document changes no code. It defines the catalogue of functional test cases for the admin UI (`src/Platform/ui.admin/src/ui.admin.esproj`), how the catalogue is created in Testiny, the changes the automation framework needs, and how each case is automated in `src/Platform/UI.Admin.AutomationTests/src/UI.Admin.AutomationTests.csproj`.

## Problem

The admin UI has 37 implemented specs (`docs/Platform/ui.admin/specs`, with 48 reviews), but the integrated test suite has only six tests: the Elementary set. Each is linked to one Testiny case of project `2SP` (cases 1–6, folder `Elementary`, type Sanity). Most of the UI's functionality has no test case and no automated check against a real target: annotations beyond bulk create, configurations beyond one save, modeling, the graph, key values beyond one create, access management, settings and monitoring.

The issue asks for:

1. A detailed set of test cases for all functionality of the admin UI. The cases are based on the code and on the specs and reviews of `docs/Platform/ui.admin/specs`.
2. The cases grouped in folders and sub-folders by functionality and application structure. They are written into this plan and created with the same structure in Testiny project `2SP` (id 1) through the Testiny MCP server.
3. Every case with type `Functional`, status `Ready for Test` and automation `Automated`, and a priority `Low`, `Medium` or `High`. The most important case of each section gets the priority `Elementary (Critical)`, and its automated test gets `[Elementary]`.
4. Every case implemented as an automated test in `UI.Admin.AutomationTests`, with a matching structure, and linked to its case with `[TestCase(id)]`. The six existing tests and cases may be moved and restructured.

## Current State

### The admin UI

- File-based routes under `src/Platform/ui.admin/src/app/pages`. The global pages are `/login`, `/callback`, `/onboarding`, `/unsupported`, `/account`, `/orgs` and `/invitations`. The organization pages are `/{org}` and `/{org}/members`. The project pages are `/{org}/{project}`: `settings`, `members`, `machines`, `machines/{id}`, `views` and `monitor`, plus the redirect stubs `secrets`, `certificates`, `invitations`, `usage` and `audit`. The view pages are `/{org}/{project}/{view}`: `annotations`, `configurations`, `graph` and `key-values` with their details, and `modeling`, whose tabs are schemas, templates, compare, deleted and export-import.
- The router guards run in this order: auth, context (membership, "You have no access here", the remembered last context), page memory, telemetry (`app/router`).
- Roles come from the account's access map (`app/composables/useRole.ts`): Reader reads; Contributor creates and edits and manages machines; ContributorWithSecrets also handles secrets; Administrator also manages access, transfers and monitoring; Owner also owns the project and organization operations. An organization role grants no project role.
- The mock build (`npm run build:e2e`, `VITE_MOCK_DATASET=default`, served on port 4174) serves the API with MSW in the browser (`app/mock/*.ts`) and has Ada Lovelace signed in. The `default` scenario makes Ada Owner of `acme` and `acme/billing` (views `default` and `staging`), Contributor of `acme/shipping`, and Reader of `northwind/portal` with no organization role (`app/mock/db.ts:173-178`). The data is stateful in the tab's `sessionStorage`. `?mock-scenario=` selects `default`, `new-user`, `invited`, `large` or `showcase`, and changing it resets the data. `window.storytellerMock` offers `reset`, `changeConfiguration(key, changes, scope)` (a concurrent edit by Grace Hopper) and `addMachine` (`app/mock/browser.ts`).
- The TypeScript Playwright suite (`src/Platform/ui.admin/src/test/e2e/*.spec.ts`) covers the UI's flows against the mock. It stays the fast, hermetic regression suite of the UI alone, together with the network-fault, visual and axe checks.

### The automation project and its libraries

- `src/Platform/UI.Admin.AutomationTests/src` is a TUnit executable on Microsoft.Testing.Platform (`OutputType Exe`), run with `dotnet run`. It builds on `src/Libraries/Testing/UI.Automation.*`. Its documentation is `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`, and its spec is `specs/2026-10-31 UI Automation Testing Project.md`.
- `PlatformTest : UiTest` (`PlatformTest.cs`) provides `Target` (`PlatformTarget`: `Organization`, `Project`, `View`, `MockScenario`, `EmailDomain`), `Routes` (`AppRoutes`), `App` and `PlatformData`, and the requirement `EditingUser` (Owner, Administrator, ContributorWithSecrets or Contributor). A test class overrides `RequiredUser` for the whole class.
- `UiTest` (`src/Libraries/Testing/UI.Automation.Runtime.TUnit/src/UiTest.cs:52-73`) leases the pool user, then opens the context and page in `BeginUiTest`. `UserRequirement` (`UI.Automation.Runtime/src/Users/UserRequirement.cs`) has `Any`, `WithRole` and `WithAnyRole`. When no pool user has the role, the test is Blocked at once (`UserPool.cs:57-61`). A skip from a derived class's `[Before(Test)]` hook comes after the lease and the page. A registration-time `SkipAttribute` (like `RequiresEngineAttribute`) or `Skip.Test` in a getter read before `TestScope.Create` skips earlier.
- Attributes: `[Elementary]`, `[Area(Area.X)]` (the category `Area-X`; the enum `Attributes/Area.cs` has Access, Annotations, Configurations, KeyValues, Modeling, Navigation, Projects and Settings), `[Serial]`, `[ConfiguredRetry]` (assembly), `[TestCase(id)]` (read by reflection from the method and the class, `TestIdentityFactory.cs:74-75`).
- Page objects (`Pages/`): `App`, `AppRoutes`, `LoginPage`, `OrganizationsPage`, `OrganizationOverview`, `ProjectSettingsPage`, `ViewsPage`, `AnnotationsPage`, `BulkCreateDialog`, `AnnotationDetail`, `ConfigurationsPage`, `ConfigurationDetail` and `KeyValuesPage`.
- `PlatformData` (`Data/PlatformData.cs`) generates run-id names: slugs `at-<runId>-<kind>-<word>-<word>`, annotation keys `rst.at-<runId>-<word>-<word>`, key values `at_<runId>.<word>_<hex4>` and e-mails. The words come from Bogus's Lorem list (181 words), and every generated part is cut to 40 characters (`PlatformData.cs:27,118`), so names can collide on a live target over a long run.
- Known weak spots: `AnnotationsPage.FilterAsync` returns when any `q=` is in the URL, not the requested one (`AnnotationsPage.cs:53-59,97`). `ConfigurationDetail.SaveAsync` accepts any "Configuration saved (v<n>)" (`ConfigurationDetail.cs:65-70`). `App.Toast` fails strict mode when two identical toasts are stacked (`App.cs:77-85`). `AnnotationsPage.Item` matches a row by a substring of its key.
- Six tests, all `[Elementary]`: `SignInTests.SignIn_ShowsOrganizations` (case 1), `AnnotationsTests.Annotations_CreateResponsibility_AppearsInGrid` (2), `ConfigurationsTests.Configurations_SetDocument_EffectiveShowsIt` (3), `KeyValuesTests.KeyValues_CreateAndRead` (4), `ProjectsTests.Project_Create_OpensSettings` (5) and `NavigationTests.Navigation_MobileMenu_OpensPages` (6). They pass against the mock build on desktop and on the `mobile` profile (checked on 2026-11-02: 6 passed in 25 s and in 10 s).
- The `mock` profile (`testsettings.mock.json`) targets `acme/billing/default` with four Owner placeholder users. Downloads are accepted (Playwright's default). Nothing grants clipboard permissions. Every tab of the mock has its own MSW database, so two pages cannot share mock data.
- The Testiny reporter accepts numeric case ids (`1234`, `TC-1234`). For each case, the worst result wins: FAILED, then BLOCKED, then SKIPPED, then PASSED (`TestinyTestRunReporter.cs:319-327`). So a case must not be shared by two variants of a test.

### Testiny project `2SP`

Checked through the Testiny MCP server on 2026-11-01:

- Project id 1, key `2SP`, "2S Platform". The case fields are `priority` (0 `Elementary (Critical)`, 1 `High`, 2 `Medium`, 3 `Low`), `status` (`READY` is "Ready for Test"), `testcase_type` (`FUNCTIONAL` is "Functional") and `automation` (`AUTOMATED` is "Automated"), all required.
- One folder, `Elementary` (id 1), with cases 1–6. Each case uses the STEPS template, has priority 0, status READY, type SANITY and automation AUTOMATED. Each precondition names its test ("Automated: `Class.Method`").
- The MCP server can create folders and cases, edit cases and folders, and move cases and folders. It cannot delete folders or cases.

### Second opinion: the CodeRabbit coding plan

The CodeRabbit plan attached to the issue (a reply to its comment on 2026-11-01) proposes about 140 cases in ten folders, the MCP server with a preflight check, keeping cases 1–6 and moving them, disposable projects for project-wide changes, skips for role, phone and live-only cases, and one test class per sub-folder. This plan takes the folders, the preflight, the reuse of cases 1–6, the disposable projects, the class-per-sub-folder layout and the exclusion list from it. It differs in these points:

- One Elementary case per section, as the issue asks. CodeRabbit kept `[Elementary]` on cases 1–6 only.
- Exact Contributor and Reader cases run on the mock through `Target:RoleProjects`; they are not skipped.
- Concurrent edits (412) run on the mock through its hooks and on a live target through a second page. CodeRabbit made them live-only.
- No live-only case: anything the mock cannot verify is listed as not automated.
- Role requirements per test method, not per class, so one class can hold the cases of a sub-folder whatever their role.
- A pre-lease skip hook in the library, so a desktop-only test costs no lease and no browser context on a phone profile.

## Proposed Changes

### 1. The catalogue

The catalogue (section "Test Case Catalogue" below) has 233 cases in ten sections, which follow the app's structure. Each section is a top folder in Testiny, and its sub-folders group the functionality.

| # | Section (Testiny folder) | Tests folder | `[Area]` | Cases | Elementary case |
|---|---|---|---|---|---|
| 1 | Access (ACC) | `Tests/Access` | `Access` | 33 | ACC-SGN-01 `SignInTests.SignIn_ShowsOrganizations` |
| 2 | Organizations and Projects (ORG) | `Tests/Projects` | `Projects` | 10 | ORG-PRJ-01 `ProjectTests.Project_Create_OpensSettings` |
| 3 | Navigation (NAV) | `Tests/Navigation` | `Navigation` | 23 | NAV-MNU-01 `NavigationMenuTests.Navigation_MobileMenu_OpensPages` |
| 4 | Annotations (ANN) | `Tests/Annotations` | `Annotations` | 34 | ANN-BLK-01 `AnnotationBulkCreateTests.Annotations_CreateResponsibility_AppearsInGrid` |
| 5 | Configurations (CFG) | `Tests/Configurations` | `Configurations` | 28 | CFG-CRE-01 `ConfigurationCreateTests.Configurations_SetDocument_EffectiveShowsIt` |
| 6 | Modeling (MOD) | `Tests/Modeling` | `Modeling` | 28 | MOD-SCH-01 `ModelingSchemaTests.Schemas_CreateAnnotationSchema_ListsVersionOne` |
| 7 | Graph (GRA) | `Tests/Graph` | `Graph` | 18 | GRA-OVR-01 `GraphOverviewTests.Graph_FocusOnCreatedTree_OpensNodeByKeyboard` |
| 8 | Key-Values (KV) | `Tests/KeyValues` | `KeyValues` | 31 | KV-CRE-01 `KeyValueCreateTests.KeyValues_CreateAndRead` |
| 9 | Settings (SET) | `Tests/Settings` | `Settings` | 21 | SET-PRJ-01 `SettingsProjectTests.Settings_OpenAsAdministrator_ShowsEverySection` |
| 10 | Monitoring (MON) | `Tests/Monitoring` | `Monitoring` | 7 | MON-PAG-01 `MonitorTests.Monitor_SwitchTabs_ShowsUsageAndAudit` |

Every case has these properties:

- **Type** `Functional`, **status** `Ready for Test`, **automation** `Automated`.
- **Priority** `Elementary (Critical)` for exactly one case per section: the section's PR-gate smoke test, whose test gets `[Elementary]`. The other cases are `High` (core flows and essential validation), `Medium` (secondary flows) or `Low` (edge cases).
- **Local reference** `<SECTION>-<SUB>-<NN>` (for example `ANN-BLK-01`): the section code (ACC, ORG, NAV, ANN, CFG, MOD, GRA, KV, SET, MON), a three-letter sub-folder code, and a number. The reference is written in the Testiny precondition and in the comment above the test, and links this document, the reviews, the living case list, Testiny and the code.
- **Role**:
  - Any: any pool user.
  - Editor: Contributor, ContributorWithSecrets, Administrator or Owner.
  - Admin: Administrator or Owner.
  - Secrets: ContributorWithSecrets, Administrator or Owner.
  - Owner.
  - Contributor: exactly Contributor.
  - Reader: exactly Reader.
- **Mode**:
  - B: runs on mock and live targets.
  - M: mock only, through another mock scenario, mock seed data or a mock hook; it is skipped with a reason on a live target.
  - D: desktop only, skipped below 1024 px.
  - Ph: phone only, skipped at 640 px and wider.
  - P: creates a disposable project in the target organization and changes only that project. It needs an Owner of the organization.
- **Test**: one test method `<Class>.<Method>` in `Tests/<Area>/<Class>.cs`. The method name has the form `<Feature>_<Action>_<Outcome>` and is unique in the project. One class matches one sub-folder.
- **Steps**: an action and its expected result each, with the exact UI strings. **Preconditions** say who, where and what the test creates first.

Data rules, as for the existing tests:

- A test creates what it acts on, with generated names that carry the run id.
- It changes or deletes only what it created.
- It does not rely on seed data of the mock, except in an M case.
- Project-wide settings (description, machine policy, secrets policy, rotation, recipients, BYOK, trusted issuers, views) change only inside a P project.
- Browser-local settings change freely: the default time zone, the date format and the type appearance are kept in `localStorage` (`app/stores/project-settings.ts`), and every test has a fresh browser context.

The catalogue has 170 B cases, 35 B,P, 11 B,D, 7 B,D,P, 3 B,Ph and 7 M. On a live target, a desktop run creates 42 disposable projects and a phone run 35, and they stay in the organization, as the existing data rules intend ("the growing data set is intended load"). A shared per-run project is a possible follow-up. It cannot work on the mock, where each tab has its own database.

The existing cases keep their ids and method names, and move into the classes of their sub-folders:

| Case | Method | New class |
|---|---|---|
| 1 (ACC-SGN-01) | `SignIn_ShowsOrganizations` | `SignInTests` |
| 2 (ANN-BLK-01) | `Annotations_CreateResponsibility_AppearsInGrid` | `AnnotationBulkCreateTests` |
| 3 (CFG-CRE-01) | `Configurations_SetDocument_EffectiveShowsIt` | `ConfigurationCreateTests` |
| 4 (KV-CRE-01) | `KeyValues_CreateAndRead` | `KeyValueCreateTests` |
| 5 (ORG-PRJ-01) | `Project_Create_OpensSettings` | `ProjectTests` |
| 6 (NAV-MNU-01) | `Navigation_MobileMenu_OpensPages` | `NavigationMenuTests` |

The old classes `AnnotationsTests`, `ConfigurationsTests`, `KeyValuesTests`, `NavigationTests` and `ProjectsTests` are removed. `SignInTests` stays.

What is not automated is listed at the end of each section, with the reason. In short:

- Fault injection (network errors, 429, 500, partial failures) and the error states with Retry. The TypeScript suite covers them against MSW.
- Flows that need a second controlled identity, such as accepting or declining someone else's invitation, or changing another member's role in the shared project.
- An exact Administrator or ContributorWithSecrets role, which the mock has in no project.
- Real Azure resources: BYOK verification, HSM where the deployment has none, federation token exchange, and mTLS use.
- WebGL pointer interaction with graph nodes, visual checks and accessibility scans.

### 2. Testiny provisioning

Through the Testiny MCP server, in project `2SP` (id 1):

1. Preflight, done on 2026-11-01: the project, its fields and their values, the folder `Elementary` and cases 1–6 exist, and the server can create, edit and move folders and cases. If a check fails, stop and report; never invent ids.
2. Create the ten top folders in the order of the table above, each with the section's paragraph as its description, and their sub-folders with the sub-folder descriptions.
3. Create each new case in its sub-folder with these fields:
   - The title.
   - The precondition: role, mode, setup, and "Automated: `<Class>.<Method>`".
   - The steps (STEPS template).
   - `priority`, `status` `READY`, `testcase_type` `FUNCTIONAL` and `automation` `AUTOMATED`.
4. Edit cases 1–6: set `testcase_type` to `FUNCTIONAL`, keep priority 0, set the new class in the precondition, and move each case into its sub-folder.
5. The folder `Elementary` is left empty, because the server cannot delete it. Its description says that it is superseded by the priority `Elementary (Critical)` and can be deleted.
6. Record the mapping from local reference to Testiny id in the review of this phase and in the living case list (§6).

### 3. Framework changes

#### 3.1 A pre-lease skip in `UiTest` (library)

`UiTest` gets `protected virtual string? GetSkipReason(TestContext context, AutomationSession session)`. `BeginUiTest` calls it after `UiAutomationSession.Require()` and before `TestScope.Create`. When it returns a reason, `BeginUiTest` calls `Skip.Test(reason)`, and the scope stays null. Such a test is skipped before any user lease or browser context, and the reporting receiver reports it as Skipped, with its reason in `results.jsonl`. The library readme documents the hook, and a test of `Runtime.TUnit` covers it.

#### 3.2 Target settings: mock and role projects

`PlatformTarget` gets these members:

- `IsMock`: true when `MockScenario` is set or `SignIn` is `None`.
- `RoleProjects`: a case-insensitive dictionary from a role name to `<org>/<project>[/<view>]`, a project where every pool user holds exactly that role. It is empty by default. `testsettings.mock.json` sets `Contributor` to `acme/shipping` and `Reader` to `northwind/portal`.

`PlatformAutomationSetup.Validate` checks the role names and the name parts. `PlatformTargetCheck` checks each entry on `/orgs`: the project link must exist and must name the role. Otherwise the check fails with `Target:RoleProjects:<role>`.

#### 3.3 Per-test role and mode attributes (Platform project, `Attributes/`)

- `[UserRole(Role.X)]` on a method (or a class; the method wins), with `Role` = `Any`, `Editor`, `Admin`, `Secrets`, `Owner`, `Contributor` and `Reader`. `PlatformTest.RequiredUser` reads it from the running test:
  - Editor is `EditingUser`.
  - Admin is Owner or Administrator.
  - Secrets is Owner, Administrator or ContributorWithSecrets.
  - Owner is `WithRole("Owner")`.
  - Contributor and Reader: `Any` when `Target:RoleProjects` maps the role (and `PlatformTest.Routes` then points at that project), otherwise `WithRole(role)`.
  
  A class may still override `RequiredUser`. Without the attribute or an override, the requirement is `Any`.
- `[DesktopOnly]` and `[PhoneOnly]` are categories (`DesktopOnly`, `PhoneOnly`) that `PlatformTest.GetSkipReason` turns into a skip from the context's viewport (`session.ContextOptions.Viewport`): below 1024 px, and at 640 px or wider. `GetSkipReason` also skips an exact-role test when the target is the mock and no pool user and no `RoleProjects` entry can satisfy it. On a live target that stays a Blocked test, because there it is a real gap in the environment.
- `[MockOnly]` is a registration-time `SkipAttribute` on `PlatformTarget.IsMock`, for the M cases.
- `[DisposableProject]` is a category (`DisposableProject`) for the P cases, so a run can leave them out (`--treenode-filter "/**[Category!=DisposableProject]"`).
- `Area` gets `Graph` and `Monitoring`.

#### 3.4 `PlatformTest` helpers

- `CreateDisposableProjectAsync()`: as an Owner of `Target:Organization`, it creates `PlatformData.ProjectKey()` through New project on the organization overview. It returns the project's `AppRoutes` with the view `default`.
- `InstallClipboardRecorderAsync()` and `CopiedTextAsync()`: an init script, added before the first navigation, records `navigator.clipboard.write`, `writeText` and `execCommand('copy')` into `window.__copied`. It works on every engine and on BrowserStack.
- `InstallRequestRecorderAsync()`: an init script that wraps `window.fetch` and records the method, URL and body into `window.__requests`, for the "a request was or was not sent" checks.
- `DownloadAsync(action, timeout)`: `Page.RunAndWaitForDownloadAsync` with an explicit timeout (60 s by default). It returns the suggested file name and the bytes (`CreateReadStreamAsync`, never `PathAsync`). A downloaded secret (PFX, a secret export) is never kept as an artifact.
- `MockApi`, used only when `Target.IsMock`:
  - `FetchAsync(method, path, body)`: an in-page `fetch` of `/api/v1/…`, sent once `navigator.serviceWorker.controller` is ready.
  - `ChangeConfigurationAsync(key, changes)` through `window.storytellerMock`.
  
  A section's "another writer" for a concurrent-edit case uses it on the mock. On a live target it uses `OpenSecondPageAsync()` instead: a second page of the same context, with its own `App`, that saves through the UI.

#### 3.5 Routes, data and shared page objects

- `AppRoutes`, `App`, `PlatformData`, `PlatformTest` and every page object become `partial`, so a section adds members in `<Type>.<Section>.cs` without touching a shared file. `AppRoutes` gets every route the catalogue needs:
  - Account, onboarding and invitations.
  - Organization members, project members, machines and machine details.
  - Monitor (with a tab), settings (with a hash), graph and graph nodes.
  - The modeling tabs, details and old paths.
  - Configuration tabs, annotation history.
  - Query and hash builders.
- `App`:
  - `Toast(…)` returns the last match, so stacked identical toasts do not fail strict mode.
  - `MenuAsync()` returns the docked sidebar, or the slideover below 1024 px.
  - User menu helpers, palette helpers (`OpenPaletteAsync`, `PaletteOption`).
  - `ExpectQueryAsync(name, value)`, a URL check that keeps the query.
- `AnnotationsPage.FilterAsync` and a new `ConfigurationsPage.FilterAsync` wait for the exact `q`. `Item(key)` matches the exact key. `ConfigurationDetail.SaveAsync(expectedVersion)` waits for the exact toast.
- `PlatformData`:
  - Every generated part starts with `Data.Hex(4)`, so names stay unique after the 40- and 63-character cuts.
  - New generators: view names, labels, titles, texts of an exact length, and run-tagged JSON documents.
- Page objects that more than one section uses are created first, with the members the catalogue needs:
  - `ConfirmDialog`: plain and typed confirmation.
  - `DiscardChangesDialog`.
  - `CodeEditor`: the CodeMirror helper that `ConfigurationDetail` uses today, made shared.
  - `NewAnnotationDialog`.
  - `AnnotationDetail.DeleteAsync`.
  - `AccountPage`.
  - `ContextSwitcher`.
  - `ViewsPage.CreateViewAsync`.
  - `ProjectSettingsPage`: enable a credential kind, allow a protection level, add a recipient.
  - `KeyValuesPage`: create a secret, reveal.
  - `ModelingSchemaPage` and `ModelingTemplatePage`: create an annotation schema or a type template.
  
  The section that owns a page object adds the rest.
- `testsettings.mock.json` sets the role projects.

### 4. Tests

Each section's tests are in `Tests/<Area>/`, one class per sub-folder, derived from `PlatformTest` and marked `[Area(Area.<Area>)]`. Each test method has these attributes:

- `[Test]`.
- `[TestCase(<Testiny id>)]` on the method, never on the class.
- `[UserRole(Role.<role>)]` unless the role is Any.
- `[DesktopOnly]`, `[PhoneOnly]`, `[MockOnly]` or `[DisposableProject]` per the mode.
- `[Elementary]` for the section's Elementary case.

The tests follow the rules of `automation-tests.md` ("Write a test"):

- Navigate through `App.GotoAsync` and the page objects, so the first navigation carries the mock scenario.
- Locate by role, label and text.
- Assert a toast right after its action.
- No `Task.Delay`.
- Clean up only what the test created, in `CleanUpAsync`.

A concurrent-edit case uses the mock hook on the mock and a second page on a live target. A live read that is eventually consistent (the audit trail) repeats its read with a bounded timeout.

### 5. Verification

- Build without warnings: `dotnet build src/Platform/UI.Admin.AutomationTests/src`, and the library tests (`dotnet test src/Libraries/Testing`).
- Run the whole suite against the mock e2e build (`VITE_MOCK_DATASET=default npm run build:e2e`, `vite preview --port 4174`):
  - `UITESTS_PROFILE=mock UITESTS_Target__BaseUrl=http://localhost:4174 dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**"`.
  - The same with `mock,mobile`.
  
  Every test passes, or is skipped only for its mode: D on the phone profile, Ph on desktop, M nowhere on the mock. Run it twice to catch flaky tests.
- The Elementary set (`/**[Category=Elementary]`) has ten tests, one per section, and passes on both profiles.
- No live target is available for this change, so the live paths (AuthKit sign-in again, the second-page writers, the polling of the audit trail) are reviewed but not run. A first run with `reporting-testiny` and `Reporting:UnmappedTests=Warn` against a deployed target confirms that every id exists.

### 6. Documentation

- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md` covers the new folders, areas and attributes (`[UserRole]`, `[DesktopOnly]`, `[PhoneOnly]`, `[MockOnly]`, `[DisposableProject]`), `Target:RoleProjects`, the helpers, the skip rules, the Elementary set of ten, and the full-suite commands.
- A new living list, `docs/Platform/UI.Admin.AutomationTests/test-cases.md`, lists every case: its reference, Testiny id, title, priority, role, mode and test.
- `docs/Libraries/Testing/UI.Automation/readme.md` documents `GetSkipReason`.
- `CLAUDE.md` mentions the role projects and the catalogue.
- Reviews in `docs/Platform/UI.Admin.AutomationTests/specs/reviews/`, one per phase.

### 7. Phases

1. **Catalogue in Testiny** (§2). Review: the folders and the mapping of the local references to Testiny ids.
2. **Framework** (§3), with the six existing tests moved and still passing. Review.
3. **Tests per section** (§4). One commit per section, developed in parallel on branches of `feat/424-152` and merged into it. Review.
4. **Verification and documentation** (§5, §6). Review.

## Test Case Catalogue

### 1. Access

Access covers who and what may reach a project in the Admin UI. It covers:
- signing in and out (AuthKit on a deployed target, the mock sign-in through /callback on the mock build);
- the login, callback and unsupported pages, the Account page's profile and memberships, and a new user's onboarding;
- the organization and project Members pages with their role rules;
- invitations: the Invite dialog, Resend, Copy link and Revoke;
- the signed-in user's own invitations: Accept, Decline and the public landing;
- machine access: API key machines with their one-time credentials, presets and scope rules per role, the machine detail, the annotation restriction, Certificate and CertificateAndApiKey machines, shared certificates, trusted issuers with their key sources, and Federated machines with their federation rules.

The section has seven sub-folders, one test class each, in `Tests/Access` with `[Area(Area.Access)]`: Sign In and Account (`SignInTests`), Members (`MemberTests`), Invitations (`InvitationTests`), My Invitations (`MyInvitationTests`), Machines (`MachineTests`), Machine Certificates (`MachineCertificateTests`) and Federation (`FederationTests`). The Elementary case is ACC-SGN-01 (`SignInTests.SignIn_ShowsOrganizations`, existing Testiny case 1).

Tests that change project-wide state work in a disposable project (mode P, as an Owner of Target:Organization): the machine policy that enables Certificate, CertificateAndApiKey or Federated, the trusted issuers (a project-wide list), and the annotation that ACC-MCH-07 needs in the default view. Everything else creates its own invitations and machines in the target project, and revokes or deletes them in `CleanUpAsync`. Exact Contributor and Reader cases run in the project of `Target:RoleProjects`: on the mock, acme/shipping and northwind/portal. Federated machine access is a deployment capability: when the API answers 'Federated machine access is not configured for this deployment.', ACC-FED-03 and ACC-FED-04 skip with that message; on the mock they run.

#### Sign In and Account (SignInTests)

##### ACC-SGN-01 A signed-in user sees the organizations and the target project
- **Priority** Elementary · **Role** Any · **Mode** B · **Test** `SignInTests.SignIn_ShowsOrganizations` · existing Testiny case 1
- **Preconditions** Any pool user who is a member of Target:Organization/Target:Project, signed in by the runtime (live: the cached AuthKit sign-in; mock: Ada Lovelace). Creates nothing.

1. Open /orgs → the heading 'Organizations' and the button 'User menu for <name>' are visible
2. Look at the target organization's card → a link '<organization>' and, in its card, the project link '<project> <role>' to /<organization>/<project>/default/annotations

##### ACC-SGN-02 Signing out shows the sign-in page, and Continue with AuthKit signs in again
- **Priority** High · **Role** Any · **Mode** B · **Test** `SignInTests.SignIn_SignOutAndContinue_ReturnsToLastProject`
- **Preconditions** Any signed-in pool user. On a live target the user's email and password (Users:Pool) are needed for the AuthKit hosted page. Creates nothing.

1. Open the target view's Annotations (/<org>/<project>/<view>/annotations) → the annotations and 'User menu for <name>' are visible
2. Open 'User menu for <name>' → menu items 'Account', 'Organizations', 'Keyboard shortcuts' and 'Sign out'
3. Click 'Sign out' → URL /login: heading 'Sign in to 2S Admin', text 'Manage organizations, projects, annotations and configurations of the 2S Platform.', button 'Continue with AuthKit'; no user menu and no 'You are signed in as'
4. Click 'Continue with AuthKit'; on a live target, sign in on the AuthKit hosted page with the pool user's email and password → mock: the app signs in at once through /callback. Live: the browser leaves the app host for AuthKit and returns through /callback
5. Wait for the app → the URL is the target view's annotations again ('/' opens the last context), and 'User menu for <name>' shows the same name as in step 1

##### ACC-SGN-03 A signed-out deep link signs in and returns to the requested page
- **Priority** High · **Role** Any · **Mode** B · **Test** `SignInTests.SignIn_SignedOutDeepLink_ReturnsToThePage`
- **Preconditions** Any signed-in pool user, who signs out first. On a live target the user's email and password are needed. Creates nothing.

1. Open /orgs, then 'User menu for <name>' and 'Sign out' → URL /login with 'Continue with AuthKit'
2. Open the target view's Configurations (/<org>/<project>/<view>/configurations) directly; on a live target, sign in on the AuthKit hosted page → the sign-in starts with this page as its return path: on the mock through /callback at once, on live through the AuthKit hosted page and /callback
3. Wait for the app → the URL ends in /<org>/<project>/<view>/configurations; the 'Filter annotations' box and 'User menu for <name>' are visible

##### ACC-SGN-04 The login, callback and unsupported pages handle the return path, sign-in errors and the configured state
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `SignInTests.SignIn_LoginPage_HandlesReturnPathAndErrors`
- **Preconditions** Any signed-in pool user. Creates nothing.

1. Open /orgs and read the name of 'User menu for <name>' → the organizations are listed
2. Open /login?returnTo=/<org> → text 'You are signed in as <name>.' and a link 'Continue' to /<org>; no 'Continue with AuthKit'
3. Click 'Continue' → URL /<org>; the organization overview's heading '<org>'
4. Open /login?returnTo=//evil.example/x → the link 'Continue' points to '/'
5. Open /login?error_description=Access%20denied%20by%20test → alert 'Sign-in failed' with 'Access denied by test'
6. Open /callback?error=access_denied → URL /login?error=access_denied; alert 'Sign-in failed' with 'access_denied'
7. Open /unsupported → heading 'Everything is fine', text 'The application is configured.', link 'Open the app'
8. Click 'Open the app' → the app opens with 'User menu for <name>'

##### ACC-SGN-05 The Account page shows the profile and the memberships, and a membership opens its project
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `SignInTests.Account_Memberships_OpenTheProject`
- **Preconditions** Any signed-in pool user who is a member of the target project. The clipboard recorder is installed before the first navigation. Creates nothing.

1. Open 'User menu for <name>' and pick 'Account' → URL /account; heading 'Account'
2. Look at the profile card → the user's name and email, the account id with the button 'Copy to clipboard', and the button 'Sign out'
3. Click 'Copy to clipboard' → the copied text is the account id shown
4. Look at 'Memberships' → a row '<org>.<project>' with the kind 'Project' and the user's role badge ('Reader', 'Contributor', 'Contributor + secrets', 'Administrator' or 'Owner')
5. Click the row '<org>.<project>' → URL /<org>/<project>/default/annotations

##### ACC-SGN-06 A new user creates the first organization and project on onboarding
- **Priority** Medium · **Role** Any · **Mode** M · **Test** `SignInTests.Onboarding_NewUser_CreatesFirstOrganization`
- **Preconditions** Mock only. The first navigation uses the scenario 'new-user' instead of 'default': Ada Lovelace is signed in without an account or memberships. Generated organization and project names.

1. Open / with the 'new-user' scenario → URL /onboarding: heading 'Welcome, Ada Lovelace', text 'Create your organization and its first project to start. If a colleague invited you, open the link from the invitation email instead.', button 'Create and continue'
2. Click 'Create and continue' with empty fields → field errors 'Enter a organization name.' and 'Enter a project name.'
3. Type 'invitations' in 'Organization' and click 'Create and continue' → field error 'This organization name is reserved.'
4. Fill 'Organization' and 'First project' with the generated names and 'Project description' with a text that has spaces around it; click 'Create and continue' → URL /<org>/<project>/default/annotations; 'No annotations in this view'
5. Open /<org> → a project card '<project>' with the badge 'Owner', the key '<org>.<project>' and the description without its surrounding spaces

#### Members (MemberTests)

##### ACC-MEM-01 The project Members page lists the members, locks the own role and highlights a member
- **Priority** High · **Role** Admin · **Mode** B · **Test** `MemberTests.Members_ProjectPage_LocksOwnRoleAndHighlights`
- **Preconditions** Administrator or Owner of the target project. Creates nothing.

1. Open /<org>/<project>/members → headings 'Project members' and 'Invitations'; text 'Members of <org>.<project>. An organization role does not grant access to its projects; invite people to the project itself.'; buttons 'Roles' and 'Invite' in the top bar
2. Look at the members table → columns 'Name', 'User' and 'Role'; one row has the badge 'you', with the own name and the own account id
3. Look at the role select of the 'you' row ('Role of <own name>') → it is disabled and shows the own role
4. Click 'Roles' → a popover lists 'Reader', 'Contributor', 'Contributor + secrets', 'Administrator' and 'Owner', each with its description; for Administrator 'Manages members, invitations, shared certificates, the machine policy and views.'
5. Press Escape → the popover closes
6. Open /<org>/<project>/members?highlight=<own account id> → the 'you' row is marked as the current row (aria-current='true'); no other row is

##### ACC-MEM-02 The only Owner of a new project can be neither demoted nor removed
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `MemberTests.Members_OnlyOwner_CannotBeDemotedOrRemoved`
- **Preconditions** Owner of the target organization. The test creates a disposable project, whose creator is its only member and Owner.

1. On /<org> click 'New project' and create the generated project → toast 'Project <project> created'; its annotations open
2. Open /<org>/<project>/members → one member row, with the badge 'you' and the role 'Owner'
3. Look at 'Role of <own name>' → disabled
4. Look at 'Remove <own name>' → disabled
5. Look at the Invitations section → 'No invitations.'

##### ACC-MEM-03 An Owner changes the role of a member and removes another member
- **Priority** Medium · **Role** Owner · **Mode** M · **Test** `MemberTests.Members_ChangeRoleAndRemove_UpdatesTable`
- **Preconditions** Mock only, default dataset: acme/billing with Ada Lovelace (Owner, 'you'), Grace Hopper (Contributor) and Alan Turing (Reader).

1. Open /acme/billing/members → rows 'Ada Lovelace', 'Alan Turing' and 'Grace Hopper' in this order; Grace Hopper 'Contributor', Alan Turing 'Reader'
2. Open 'Role of Grace Hopper' → options 'Reader', 'Contributor', 'ContributorWithSecrets', 'Administrator' and 'Owner'
3. Pick 'Administrator' → toast 'Grace Hopper is now Administrator'; the select shows 'Administrator'
4. Reload the page → Grace Hopper is still 'Administrator'
5. Click 'Remove Alan Turing' → dialog 'Remove Alan Turing?' with 'They lose their Reader role on acme.billing.'
6. Click 'Cancel' → the dialog closes; Alan Turing is still listed
7. Click 'Remove Alan Turing', then 'Remove' in the dialog → toast 'Alan Turing removed'; the row is gone

##### ACC-MEM-04 Below Administrator the Members page offers no Invite and no access
- **Priority** Medium · **Role** Contributor · **Mode** B · **Test** `MemberTests.Members_Contributor_ShowsNoAccess`
- **Preconditions** Exactly Contributor in the project of Target:RoleProjects:Contributor (mock: acme/shipping, where Ada is a Contributor). Creates nothing.

1. Open /<org>/<project>/members of the Contributor's project → headings 'Project members' and 'Invitations'; the button 'Roles' in the top bar; no 'Invite' button
2. Look at the 'Project members' section → 'No access' with the button 'Retry'
3. Look at the 'Invitations' section → 'No access' with the button 'Retry'
4. Click 'Roles' → the roles legend opens with 'Reader', 'Contributor', 'Contributor + secrets', 'Administrator' and 'Owner'

#### Invitations (InvitationTests)

##### ACC-INV-01 Invite one person, resend and copy the link, then revoke the invitation
- **Priority** High · **Role** Admin · **Mode** B · **Test** `InvitationTests.Invitations_InviteResendRevoke_EndsRevoked`
- **Preconditions** Administrator or Owner of the target project. A generated email (a sub-address of Target:EmailDomain). The clipboard recorder is installed before the first navigation.

1. Open /<org>/<project>/members and click 'Invite' in the top bar → dialog 'Invite people' with 'To <org>.<project>. Everyone gets the same role and expiry.'; 'Role' shows 'Reader'; 'Expires after (days)' shows 7
2. Type the email into the field with the placeholder 'ada@example.com' and press Enter → a tag with the email
3. Click 'Send invitations' → toast '1 invitation sent'; the dialog closes
4. Find the email's row in the Invitations section → role 'Reader', status 'Pending', 'Invited by' the signed-in user, buttons 'Copy link', 'Resend' and 'Revoke'
5. Click 'Resend' in the row → toast 'Invitation resent'; the status stays 'Pending'
6. Click 'Copy link' in the row → toast 'Invitation link copied'; the copied text is '<app origin>/invitations/accept?id=<invitation id>'
7. Click 'Revoke' in the row, then 'Cancel' → the dialog 'Revoke the invitation of <email>?' with 'The invitation link stops working.' closes; the status stays 'Pending'
8. Click 'Revoke', then 'Revoke' in the dialog → toast 'Invitation revoked'; the status is 'Revoked'; the row has no 'Copy link', 'Resend' or 'Revoke'

##### ACC-INV-02 Several typed addresses get one invitation each, with one role and expiry
- **Priority** Medium · **Role** Owner · **Mode** B · **Test** `InvitationTests.Invitations_TypedList_SendsOneRoleAndExpiry`
- **Preconditions** Owner of the target project (only Owners are offered 'Owner'). Three generated emails A, B and C.

1. Open /<org>/<project>/members and click 'Invite' → dialog 'Invite people'
2. Type '<A in upper case> <B>,<C>' key by key → tags A (in lower case) and B; C stays as text in the input
3. Open 'Role' → options 'Reader', 'Contributor', 'ContributorWithSecrets', 'Administrator' and 'Owner'
4. Pick 'Contributor' and set 'Expires after (days)' to 14 → both values are shown; C is still in the input
5. Click 'Send invitations' → toast '3 invitations sent'; the dialog closes
6. Find the rows of A, B and C in the Invitations section → each has the role 'Contributor' and the status 'Pending'

##### ACC-INV-03 A list pasted from a mail client invites people to the organization
- **Priority** Medium · **Role** Owner · **Mode** B · **Test** `InvitationTests.Invitations_PastedListToOrganization_SendsEach`
- **Preconditions** Owner, as Administrator or Owner of the target organization itself (a project role does not count). Two generated emails A and B.

1. Open /<org>/members → heading 'Organization members' with 'Roles on <org> itself; they do not grant access to its projects.', a member row with the badge 'you', and the heading 'Invitations'
2. Click 'Invite' → dialog 'Invite people' with 'To <org>. Everyone gets the same role and expiry.'
3. Paste 'First One <A>, "Last, First" <B>;', a line break and '<A>' into the Emails field → exactly two tags, A and B, without the display names
4. Click 'Send invitations' → toast '2 invitations sent'; the dialog closes
5. Find the rows of A and B in the organization's Invitations section → role 'Reader', status 'Pending'

##### ACC-INV-04 Missing, invalid and already invited addresses are refused
- **Priority** High · **Role** Admin · **Mode** B · **Test** `InvitationTests.Invitations_InvalidAndPendingAddresses_AreRefused`
- **Preconditions** Administrator or Owner of the target project. Two generated emails A and B.

1. Open /<org>/<project>/members, click 'Invite', then click 'Send invitations' with an empty field → field error 'Enter at least one email address.'; the dialog stays open
2. Type 'grace.example.com ' (with a trailing space) → a struck-through tag 'grace.example.com' and the error 'Not an email: grace.example.com'; 'Send invitations' is disabled
3. Click 'Cancel', then 'Invite' again → no tag and no error
4. Type A, press Enter and click 'Send invitations' → toast '1 invitation sent'; the dialog closes
5. Click 'Invite', type A and B (Enter after each) and click 'Send invitations' → toast '1 invitation sent'; the dialog stays open with the alert '<A>: A pending invitation already exists.' and only the tag A
6. Click 'Cancel' → the Invitations section lists A once and B once, both 'Pending'; there is no row for grace.example.com

#### My Invitations (MyInvitationTests)

##### ACC-MYI-01 A self-invitation is declined on Invitations, and a second one is accepted
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `MyInvitationTests.MyInvitations_SelfInvitation_DeclineThenAccept`
- **Preconditions** Administrator or Owner of the target project. The signed-in email is read from /invitations. There is no pending invitation of the own email to the target project; a leftover of an aborted run is revoked first.

1. Open /invitations → heading 'Invitations', text 'Organizations and projects that invited <email>.', no entry '<org>.<project>', link 'Go to my organizations'
2. On /<org>/<project>/members, invite the own email with the role 'Reader' → toast '1 invitation sent'
3. Open /invitations/accept?id=x → a signed-in visitor is sent to /invitations; an entry '<org>.<project>' with the badge 'Reader', 'Invited by <name> · expires …', buttons 'Accept' and 'Decline'
4. Click 'Decline' → the entry '<org>.<project>' disappears
5. Open /<org>/<project>/members → the own email's invitation has the status 'Declined'
6. Invite the own email as 'Reader' again → toast '1 invitation sent'
7. Open /invitations and click 'Accept' on '<org>.<project>' → toast 'You joined <org>.<project>'; URL /<org>/<project>/default/annotations
8. Open /<org>/<project>/members → the 'you' row keeps its role (Administrator or Owner, not Reader); the own email has one 'Declined' and one 'Accepted' invitation

##### ACC-MYI-02 An invited user without an account joins through Invitations
- **Priority** Medium · **Role** Any · **Mode** M · **Test** `MyInvitationTests.MyInvitations_InvitedNewUser_JoinsProject`
- **Preconditions** Mock only. The first navigation uses the scenario 'invited': Ada Lovelace has no account and one pending invitation to acme.billing as Contributor, from Grace Hopper.

1. Open / with the 'invited' scenario → URL /invitations: an entry 'acme.billing' with the badge 'Contributor' and 'Invited by Grace Hopper · expires …'; link 'Create my own organization instead'
2. Click 'Accept' → toast 'You joined acme.billing'; URL /acme/billing/default/annotations; 'User menu for Ada Lovelace'
3. Open /orgs → the organization 'acme' with the project link 'billing Contributor'

##### ACC-MYI-03 The invitation landing asks a signed-out visitor to continue to the sign-in
- **Priority** Low · **Role** Any · **Mode** B · **Test** `MyInvitationTests.MyInvitations_LandingSignedOut_ContinuesToSignIn`
- **Preconditions** Any pool user, who signs out first. Creates nothing.

1. Open /orgs, then 'User menu for <name>' and 'Sign out' → URL /login
2. Open /invitations/accept?invitation_token=tok_test&id=inv_test → heading 'You were invited to 2S Platform', text 'Sign in, or create your account, with the email address the invitation was sent to.', button 'Continue'
3. Click 'Continue' → the sign-in starts with the invitation: on the mock through /callback to /invitations (heading 'Invitations'); on live the browser leaves the app host for the AuthKit hosted page

#### Machines (MachineTests)

##### ACC-MCH-01 Create an API key machine, store its one-time credentials and delete it
- **Priority** High · **Role** Editor · **Mode** B · **Test** `MachineTests.Machines_CreateApiKey_ShowsCredentialsOnce`
- **Preconditions** Contributor or higher in the target project, whose machine policy enables ApiKey (the default of a project). The clipboard recorder is installed before the first navigation.

1. Open /<org>/<project>/machines and click 'New machine' in the top bar → dialog 'New machine access' with 'For <org>/<project>.'; 'Annotation.Read' and 'Configuration.Read' checked
2. Keep (or pick) 'ApiKey' in 'Credential kind' and click 'Create' → dialog 'Machine access created' with 'These credentials are shown only once. Store them now.', the alert 'Shown only once' with 'Closing this dialog discards them; a reset issues new ones.', the 'Id' and the 'API key'
3. Press Escape and click outside the dialog → the dialog stays open; it has no close button; 'Done' is disabled
4. Click 'Copy' next to the API key → toast 'The clipboard contains a secret' with 'Paste it where it belongs, then clear the clipboard.'; the copied text is the key shown
5. Expand 'Ready-to-paste snippets' → 'curl' contains 'Authorization: ApiKey <key>'; '.env' contains 'STORYTELLER_API_KEY=<key>'
6. Tick 'I have stored these credentials' and click 'Done' → the dialog closes; the row of the id shows 'Annotation.Read', 'Configuration.Read', Annotation 'any', Credentials 'API key', and the buttons 'Reset' and 'Delete <id>'
7. Click 'Delete <id>', type the id into 'Type <id> to confirm' and click 'Delete' → toast 'Machine <id> deleted'; the row is gone

##### ACC-MCH-02 Reset replaces the API key, and Delete needs the exact id
- **Priority** High · **Role** Editor · **Mode** B · **Test** `MachineTests.Machines_ResetApiKey_ShowsNewKey`
- **Preconditions** Contributor or higher in the target project, with ApiKey enabled. The test first creates an API key machine through 'New machine' and keeps its id and key.

1. Click 'Reset' in the machine's row → dialog 'Reset <id>?' with 'The current secret of this machine stops working at once; a certificate keeps working. The new secret is shown once.'
2. Click 'Reset credentials' → dialog 'New credentials' with an 'API key' that differs from the first; no 'Download .pfx'
3. Tick 'I have stored these credentials' and click 'Done' → the dialog closes; the row still shows 'API key'
4. Click 'Delete <id>' → dialog 'Delete <id>?' with 'The machine loses access immediately.'; 'Delete' is disabled
5. Type the id without its last character → 'Delete' stays disabled
6. Click 'Cancel' → the dialog closes; the row stays
7. Click 'Delete <id>' again, type the exact id and click 'Delete' → toast 'Machine <id> deleted'; the row is gone

##### ACC-MCH-03 Presets and scope rules of New machine
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `MachineTests.Machines_Presets_ReplaceScopes`
- **Preconditions** Administrator or Owner of the target project, who may give management scopes. Creates nothing.

1. Click 'New machine' → group 'Presets' with 'Replace the scopes below with those of a typical machine.'; 'Annotation.Read' and 'Configuration.Read' checked, no management scope checked
2. Click 'CI writer' → 'Annotation.Write' and 'Configuration.Write' are checked as well
3. Click 'Git sync' → 'Configuration.Transfer', 'Schema.Write', 'Template.Write' and 'Annotation.Manage' are checked; 'KeyValue.Manage' is not
4. Click 'Runtime reader', then check 'Annotation.Write' → 'Annotation.Read' stays checked; no management scope is checked
5. Uncheck 'Annotation.Read' → 'Annotation.Write' is unchecked too
6. Uncheck 'Configuration.Read' → the error 'Pick at least one runtime scope.'; 'Create' is disabled
7. Click 'Cancel' and open 'New machine' again → back to Runtime reader: 'Annotation.Read' and 'Configuration.Read' checked

##### ACC-MCH-04 A machine with management and secrets scopes, and its detail panel
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `MachineTests.Machines_ManagementAndSecretsScopes_ShowOnDetail`
- **Preconditions** Administrator or Owner of the target project, which includes the secret rights. ApiKey is enabled. The test deletes the machine in its cleanup.

1. Click 'New machine', then the preset 'Backup' → 'Annotation.Read', 'Configuration.Read' and 'Configuration.Transfer' checked
2. Check 'Secrets.Write' → 'Secrets.Read' is checked too
3. Click 'Create', tick 'I have stored these credentials' and click 'Done' → the row shows 'Annotation.Read', 'Configuration.Read', 'Secrets.Read', 'Secrets.Write' and 'Configuration.Transfer'
4. Click the machine's id link → URL /<org>/<project>/machines/<id>; the detail panel with the heading <id>
5. Read the detail → 'Runtime scopes' lists 'Secrets.Write' with 'Writes key/value secrets. Needs Configuration.Write as well.'; 'Management scopes' lists 'Configuration.Transfer'; 'Annotation' 'any annotation'; 'Credentials' 'API key'
6. Look at the panel's header → from 768 px the list stays visible next to the panel and 'Open in full screen' is offered (from 1024 px also 'Pin the detail'); on a phone the detail fills the screen and the list is hidden
7. Click 'Close' → URL /<org>/<project>/machines; the list is visible
8. Open /<org>/<project>/machines/<a random GUID> → the detail shows 'Not found' with 'Retry'

##### ACC-MCH-05 A Contributor gives neither management nor secrets scopes
- **Priority** High · **Role** Contributor · **Mode** B · **Test** `MachineTests.Machines_Contributor_GivesRuntimeScopesOnly`
- **Preconditions** Exactly Contributor in the project of Target:RoleProjects:Contributor (mock: acme/shipping), whose machine policy enables ApiKey. The test deletes its machine in its cleanup.

1. Open /<org>/<project>/machines of the Contributor's project → 'New machine' in the top bar; no 'Shared certificates' or 'Trusted issuers' section
2. Click 'New machine' → the help texts 'Only Administrators give management scopes.' and 'Only users with secret rights give secrets scopes.'
3. Look at the scopes → 'Secrets.Read', 'Secrets.Write' and every management scope are disabled; 'Annotation.Read', 'Annotation.Write', 'Configuration.Read' and 'Configuration.Write' are enabled
4. Look at the presets → 'Runtime with secrets', 'Git sync', 'Backup' and 'Schema deployer' are disabled; 'Runtime reader' and 'CI writer' are enabled
5. Click 'CI writer' and 'Create', tick 'I have stored these credentials' and click 'Done' → the new row shows 'Configuration.Write', with 'Reset' and 'Delete <id>'

##### ACC-MCH-06 A Contributor can neither reset nor delete machines with elevated scopes
- **Priority** Medium · **Role** Contributor · **Mode** M · **Test** `MachineTests.Machines_Contributor_CannotResetElevatedMachines`
- **Preconditions** Mock only, default dataset, acme/shipping, where Ada is a Contributor. Machines are added as an Owner would through `window.storytellerMock.addMachine('acme.shipping', …)`: a runtime-only API key machine, one with Configuration.Transfer, one with Secrets.Read, and a Federated one with a management scope whose rule names 'github'. Then the page is reloaded.

1. Open /acme/shipping/machines → the runtime-only machine has 'Reset' and 'Delete <id>'
2. Look at the row with 'Configuration.Transfer' → no 'Reset' and no 'Delete <id>'
3. Look at the row with 'Secrets.Read' → no 'Reset' and no 'Delete <id>'
4. Open the Federated machine's detail → the region 'Federation rules' has 'Setup' but no 'Edit rules'
5. Click 'Close', then delete the runtime-only machine with its typed id → toast 'Machine <id> deleted'

##### ACC-MCH-07 A machine restricted to an annotation
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `MachineTests.Machines_AnnotationRestriction_ShowsKey`
- **Preconditions** Owner of the target organization. The test creates a disposable project (it enables ApiKey, the default) and, in its default view, a responsibility with a generated key (rst.<run slug>…). The 'Annotation key' menu of New machine always lists the catalog of the view 'default', whatever Target:View is, so on a shared target the annotation would land in a view the suite does not own.

1. Open the new project's default view (/<org>/<project>/default/annotations), click 'Bulk create', enter the generated key and click 'Create 1 annotation' → the dialog shows 'Created 1 annotation.' and the key's row 'From the list'
2. Click 'Done', open /<org>/<project>/machines, click 'New machine' and type the key into 'Annotation key' (placeholder 'Any annotation') → the key is the only option; help 'Optional: limits the machine to this annotation and its descendants.'
3. Pick the option and click 'Create' → dialog 'Machine access created'
4. Tick 'I have stored these credentials' and click 'Done' → the row's Annotation column shows the key
5. Click the machine's id link → the detail's 'Annotation' shows the key

##### ACC-MCH-08 A Reader has no access to Machines
- **Priority** Low · **Role** Reader · **Mode** B · **Test** `MachineTests.Machines_Reader_ShowsNoAccess`
- **Preconditions** Exactly Reader in the project of Target:RoleProjects:Reader (mock: northwind/portal). Creates nothing.

1. Open /<org>/<project>/machines of the Reader's project → no 'New machine'; the 'Machine access' section shows 'No access' with 'Retry'
2. Look below the list → no 'Shared certificates' and no 'Trusted issuers' section

#### Machine Certificates (MachineCertificateTests)

##### ACC-CRT-01 A Certificate machine shows its certificate once and downloads it
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `MachineCertificateTests.Certificates_CertificateMachine_DownloadsPfx`
- **Preconditions** Owner of the target organization. The test creates a disposable project. The API issues the certificate with the deployment's certificate authority, which is always registered (a local CA unless a Key Vault CA is configured), so there is no run-time skip. The test deletes the machine in its cleanup.

1. In the new project's Settings, check 'Certificate' in 'Enabled credential kinds' and click 'Save machine policy' → 'Default certificate lifetime (days)' appears; toast 'Machine policy saved'
2. Open its Machines, click 'New machine' and pick 'Certificate' in 'Credential kind' → 'Certificate lifetime (days)' shows the policy default; help 'A client certificate per machine (mTLS).'
3. Click 'Create' → dialog 'Machine access created' with 'Download .pfx' and 'Certificate password', no 'API key'; hint 'Closing this dialog discards them; to replace the certificate, create a new machine.'
4. Click 'Download .pfx' → a download named '<id>.pfx' that is not empty
5. Tick 'I have stored these credentials' and click 'Done' → the row shows Credentials 'Certificate', a thumbprint ending in '…' with a '<n> d' badge, 'Delete <id>' and no 'Reset'
6. Click the machine's id link → the detail shows 'Certificate lifetime' '<n> days' and 'Certificate expires'

##### ACC-CRT-02 A CertificateAndApiKey machine uses a shared certificate, and its reset issues only a new API key
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `MachineCertificateTests.Certificates_SharedCertificateMachine_ResetsApiKeyOnly`
- **Preconditions** Owner of the target organization. The test creates a disposable project and deletes the machine in its cleanup.

1. In the new project's Settings, check 'CertificateAndApiKey' and click 'Save machine policy' → toast 'Machine policy saved'
2. Open its Machines, click 'New machine' and pick 'CertificateAndApiKey' → 'Certificate lifetime (days)' and the checkbox 'Use a shared certificate of the project' with 'A certificate is issued for this machine.'
3. Check 'Use a shared certificate of the project' → the lifetime is hidden; help 'No certificate is issued for this machine: it signs in with a shared certificate of the project and its API key.'
4. Click 'Create' → dialog 'Machine access created' with the alert 'Uses a shared certificate', an 'API key' and no 'Download .pfx'
5. Tick 'I have stored these credentials' and click 'Done' → the row shows Credentials 'Certificate and API key', the Certificate badge 'shared' and 'Reset'
6. Click 'Reset', then 'Reset credentials' → dialog 'New credentials' with a different 'API key' and the hint 'Closing this dialog discards them; a reset issues a new API key, and the certificate stays valid.'
7. Store, click 'Done' and open the machine's detail → 'Credentials' 'Certificate and API key'; 'Certificate' 'A shared certificate of the project'

##### ACC-CRT-03 Issue a shared certificate, download it and revoke it with its thumbprint prefix
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `MachineCertificateTests.Certificates_IssueSharedCertificate_DownloadsAndRevokes`
- **Preconditions** Owner of the target organization. The test creates a disposable project and a generated label.

1. Open the new project's Machines → 'No machine access'; in 'Shared certificates' the alert 'Shared certificates are disabled', 'No shared certificates' and a disabled 'Issue certificate'
2. In its Settings, check 'CertificateAndApiKey' and click 'Save machine policy' → toast 'Machine policy saved'
3. Open Machines again and click 'Issue certificate' → no 'Shared certificates are disabled' alert; dialog 'Issue a shared certificate' with 'Lifetime (days)' 365 and 'Issue' disabled while 'Label' is empty
4. Type the label, set 'Lifetime (days)' to 30 and click 'Issue' → dialog 'Shared certificate issued' with the 'Id' (the thumbprint), 'Download .pfx', 'Certificate password' and the hint 'Closing this dialog discards them; to replace the certificate, issue another one.'
5. Click 'Download .pfx' → a download named '<label>.pfx' that is not empty
6. Tick 'I have stored these credentials' and click 'Done' → a row with the label, the thumbprint start, the status 'Expires in 29 d' or 'Expires in 30 d', and 'Revoke'
7. Click 'Revoke' → dialog 'Revoke <label>?' with 'Machines using it can no longer authenticate.'; its 'Revoke' is disabled until the first 8 thumbprint characters are typed
8. Type the first 8 thumbprint characters and click 'Revoke' → toast 'Certificate revoked'; the status is 'Revoked'; the row has no 'Revoke'

#### Federation (FederationTests)

##### ACC-FED-01 Add, edit and delete a trusted issuer from the GitHub template
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `FederationTests.TrustedIssuers_AddEditDelete_UpdatesList`
- **Preconditions** Owner of the target organization. The test creates a disposable project (trusted issuers are a project-wide list); it enables only ApiKey and has no trusted issuer. A generated issuer name. The cleanup deletes the issuer if a step fails.

1. Open the new project's /<org>/<project>/machines → the 'Trusted issuers' section shows the alert 'Federated machines are disabled' and the empty state 'No trusted issuers'
2. Click 'Add trusted issuer' in 'Trusted issuers' → dialog 'Add a trusted issuer' with 'Federated machines of the project exchange tokens of this issuer for Storyteller tokens.'; 'Template' 'GitHub Actions'; 'Name' 'github'; 'Issuer' 'https://token.actions.githubusercontent.com'; 'Claim 1' 'repository_owner_id' with 'Enter the accepted value of repository_owner_id.'; 'Add issuer' disabled
3. Type the generated name into 'Name' and '123456' into 'Value 1' → 'Add issuer' is enabled
4. Click 'Add issuer' → toast 'Trusted issuer <name> saved'; the dialog closes; 'No trusted issuers' is gone; the row '<name>' shows 'https://token.actions.githubusercontent.com', 'Discovery', '1 h (default)' and 'repository_owner_id = 123456'
5. Click 'Edit <name>' → dialog 'Edit trusted issuer <name>' without 'Template'; 'Name' is disabled; 'Issuer' is read-only with 'The issuer cannot change: the rules were written for it. Add a new trusted issuer instead.'
6. Type a 'Description' and click 'Save' → toast 'Trusted issuer <name> saved'; the row shows the description
7. Click 'Delete <name>' → dialog 'Delete <name>?' with 'Rules that use it stop matching new exchanges within a minute; …'; 'Delete' disabled
8. Type the name into 'Type <name> to confirm' and click 'Delete' → toast 'Trusted issuer <name> deleted'; the row is gone and 'No trusted issuers' is shown again

##### ACC-FED-02 Issuer templates fill the dialog, and invalid or taken names and an http issuer are refused
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `FederationTests.TrustedIssuers_TemplatesAndValidation_GuideInput`
- **Preconditions** Owner of the target organization. The test creates a disposable project and first adds a GitHub trusted issuer X with a generated name to clash with, so the template names ('vercel', 'azure', 'kubernetes') are free. The cleanup deletes X.

1. On the new project's /<org>/<project>/machines, click 'Add trusted issuer' and pick 'Vercel' in 'Template' → 'Name' 'vercel'; 'Issuer' 'https://oidc.vercel.com/<team-slug>' with 'Replace <team-slug> with the value of your platform.'; 'Max token lifetime (seconds)' '7,200'; 'Claim 1' 'owner_id'
2. Pick 'Azure managed identity' → 'Name' 'azure'; 'Issuer' 'https://login.microsoftonline.com/<tenant-id>/v2.0' with 'Replace <tenant-id> with the value of your platform.'; lifetime '86,400'; no claim row; 'Add issuer' disabled
3. Pick 'Kubernetes' → 'Issuer' and the lifetime are empty; 'Issuer' shows the placeholder 'https://westeurope.oic.prod-aks.azure.com/<tenant>/<id>/'; the info alert 'Kubernetes' mentions 'oidcIssuerProfile.issuerUrl'
4. Type 'Not valid' into 'Name' → 'Use 1 to 63 lower-case letters, digits or hyphens, starting and ending with a letter or a digit.'
5. Type X's name into 'Name' → 'The trusted issuer <X> exists already; edit it in the list.'
6. Pick 'Custom', type a new generated name and the issuer 'http://idp.example.com', and click 'Add issuer' → the alert 'The issuer http://idp.example.com cannot be discovered: it must use https.'; the dialog stays open
7. Click 'Cancel' → the dialog closes; the list has one row, X, and none for the new name

##### ACC-FED-03 From no trusted issuer to a Federated machine with its setup
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `FederationTests.Federation_NoIssuerToFederatedMachine_ShowsSetup`
- **Preconditions** Owner of the target organization. The test creates a disposable project. A generated issuer name and a GitHub subject built from the run slug. On a live deployment without Federated machine access, 'Create' answers 'Federated machine access is not configured for this deployment.' and the test skips with that message; on the mock it runs. The machine and then the issuer are deleted in the cleanup.

1. In the new project's Settings, check 'Federated' and click 'Save machine policy' → toast 'Machine policy saved'
2. Open its Machines, click 'New machine' and pick 'Federated' in 'Credential kind' → alert 'No trusted issuers' with 'A Federated machine exchanges tokens of a trusted issuer of the project. Add one first, for example for GitHub Actions or Kubernetes.'; 'Create' disabled
3. Activate the link 'Add a trusted issuer' (Enter) → the dialog closes; the URL ends in '#trusted-issuers'; the 'Trusted issuers' section is focused and in view, with 'No trusted issuers'
4. Click 'Add trusted issuer', type the name and '123456' into 'Value 1', and click 'Add issuer' → toast 'Trusted issuer <name> saved'
5. Click 'New machine' and pick 'Federated' → group 'Rule 1' with 'Trusted issuer' <name>, an 'Audience' ending in '/v1/<org>/<project>/access/token', and 'Every token of this issuer must also carry repository_owner_id = 123456.'; 'Create' disabled
6. Type the subject into 'Subject' and click 'Create' → dialog 'Federated machine created' with 'This machine has no secret: its workload exchanges a token of a trusted issuer for a short-lived Storyteller token.'; region 'Token exchange (curl)' contains '-d client_id=<id>'; region 'GitHub Actions step' contains 'id-token: write'; no 'Shown only once'
7. Click 'Done' → the row of the id shows Credentials 'Federated', 'Delete <id>' and no 'Reset'

##### ACC-FED-04 Federation rules are set up and edited on the machine detail
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `FederationTests.Federation_EditRules_SavesAndValidates`
- **Preconditions** Owner of the target organization. The test creates a disposable project with 'Federated' enabled, one GitHub trusted issuer (repository_owner_id = 123456) and a Federated machine with one rule, as in ACC-FED-03 but through page-object helpers. On a live deployment without Federated machine access the setup's 'Create' answers 'Federated machine access is not configured for this deployment.' and the test skips with that message; on the mock it runs. The machine and the issuer are deleted in the cleanup.

1. Click the machine's id link → region 'Federation rules' with 'Rule 1', the issuer badge and the subject; buttons 'Setup' and 'Edit rules'
2. Click 'Setup' → dialog 'Set up <id>' with 'Machine id (client_id)', 'Token endpoint' and the accepted token 'Rule 1: <issuer>, audience …, subject <subject>'
3. Press Escape → the setup dialog closes
4. Click 'Edit rules' → dialog 'Federation rules' with 'A platform token must match one of the rules to be exchanged for <id>. Changes apply to new exchanges.'
5. Change 'Subject' of Rule 1 to the subject with ':environment:staging' and click 'Save rules' → toast 'Federation rules saved'; the region shows the new subject
6. Click 'Edit rules', then 'Add rule' → group 'Rule 2' with the audience of Rule 1, and 'Remove rule 2'
7. In Rule 2, set 'Audience' to 'sts.amazonaws.com', type a subject and click 'Save rules' → alert 'Rule 2: the audience sts.amazonaws.com belongs to another service.'; the dialog stays open
8. Click 'Cancel' → the detail still lists only 'Rule 1', with the saved subject

##### ACC-FED-05 The JWKS URI and Pasted JWKS key sources need their keys, and the Key source column shows them
- **Priority** Low · **Role** Owner · **Mode** B,P · **Test** `FederationTests.TrustedIssuers_KeySources_RequireJwks`
- **Preconditions** Owner of the target organization. The test creates a disposable project, a generated issuer name, and a JWKS with one generated public RSA key (kty 'RSA', a kid, n and e, no private member). The cleanup deletes the issuer.

1. Open the new project's /<org>/<project>/machines and click 'Add trusted issuer' → dialog 'Add a trusted issuer'; 'Key source' 'Discovery' with the help 'The issuer’s discovery document (/.well-known/openid-configuration) names its keys.'
2. Pick 'Custom' in 'Template', then type the generated name into 'Name' and 'https://idp.example.com' into 'Issuer' → the info alert 'Custom' with 'Any OIDC issuer: the exact iss of its tokens (case and a trailing / matter), and its keys from the discovery document, a JWKS URI or a pasted JWKS.'; 'Add issuer' is enabled
3. Pick 'JWKS URI' in 'Key source' → the help 'The keys are public at a JWKS URI, the discovery document is not (a cluster with --service-account-jwks-uri).'; a field 'JWKS URI' with the placeholder 'https://keys.example.com/openid/v1/jwks'; 'Add issuer' is disabled
4. Type 'http://keys.example.com/jwks' into 'JWKS URI' and click 'Add issuer' → the alert 'The JWKS URI http://keys.example.com/jwks is not valid: it must use https.'; the dialog stays open
5. Pick 'Pasted JWKS' in 'Key source' → the help 'The keys are pasted here and never fetched (a self-managed cluster). Paste them again after a key rotation.'; no 'JWKS URI' field; a field 'JWKS' with 'The JSON Web Key Set with 1 to 10 public RSA or EC keys, each with a kid; for example the output of kubectl get --raw /openid/v1/jwks.'; 'Add issuer' is disabled
6. Paste the generated JWKS into 'JWKS' and click 'Add issuer' → toast 'Trusted issuer <name> saved'; the dialog closes; the row '<name>' shows 'https://idp.example.com', 'Pasted JWKS', '1 h (default)' and '–' for the required claims
7. Click 'Edit <name>' → dialog 'Edit trusted issuer <name>'; 'Key source' 'Pasted JWKS'; 'JWKS' holds the pasted key set
8. Pick 'JWKS URI', type 'https://keys.example.com/openid/v1/jwks' into 'JWKS URI' and click 'Save' → toast 'Trusted issuer <name> saved'; the row '<name>' shows 'JWKS URI'

#### Not automated

- Accepting an invitation as another person on a live target, or signing up through the WorkOS invitation email: this needs a second AuthKit identity and the email's invitation_token from a real mailbox. On the mock the 'invited' scenario covers the flow (ACC-MYI-02).
- Invitation email delivery and the warning 'The email could not be sent; use Resend or copy the link.': this depends on the deployment's provider and on WorkOS. The mock always reports the email as sent.
- Expired invitations (the status 'Expired', Resend renewing them, Accept refused with 'The invitation has expired; ask for a new one.'): this needs real time past the expiry, at least one day. The mock never computes 'Expired', and the UI cannot set a past expiry.
- Accept and Decline failures ('The invitation could not be accepted' with EmailMismatch, EmailNotVerified or InvitationNotPending): this needs another identity or an unverified email. The mock has no hook for it.
- Changing the role of, or removing, existing members on a live target: the members are shared pool users, and a removed member can only come back by accepting an invitation with their own sign-in. Covered on the mock only (ACC-MEM-03).
- Leaving an access point (an Owner removes themselves while another Owner exists): this destroys the test user's own access to the target project, and the UI cannot restore it.
- An Administrator who is offered no 'Owner' role in Invite and cannot change Owners ('Only Owners can change an Owner'): this needs a user who is exactly Administrator. The mock has no such project; Target:RoleProjects maps only Contributor and Reader.
- ContributorWithSecrets behaviour (secrets scopes and the 'Runtime with secrets' preset offered without management scopes): this needs a user who is exactly ContributorWithSecrets. The mock has no such project.
- A Contributor creating a Federated machine, and the Contributor's 'No trusted issuers' alert ('Ask an Administrator of the project to add one.'): this needs Federated enabled and the trusted issuers (or none) set by an Administrator in the Contributor's project, which is a second identity on live. The TypeScript e2e covers it on the mock.
- ClientCredentials machines (Client ID, Client secret, token endpoint and scope, the Token request snippet): the mock returns no client secret, and a live target needs a machine identity provider registered in the deployment. That would make it a live-only case.
- Tooltips of disabled member controls ('The last Owner cannot be demoted or removed', 'You cannot change your own role'): disabled controls get no hover events, and reka-ui opens tooltips only on mouse hover. The cases assert the disabled state instead.
- API-only refusals (MemberNotFound, LastOwner, SelfRoleChange and ElevatedRole from the API, a concurrent reset answered with 409, a delete during a reset), the error states with Retry (5xx, an unreachable API), and the failure toasts ('Reset failed', 'Delete failed', 'The action failed'): the UI prevents these inputs, or they need races or fault injection. The mock has no conflict or failure hook for access, and MSW runs in a service worker that page.route cannot intercept.
- Using the issued credentials (the old API key refused after Reset, mTLS with a machine or shared certificate, a revoked shared certificate refused, a federated token exchange with a real platform token), and opening a downloaded .pfx with its password: these are API, mTLS and platform checks outside the browser UI. The mock's .pfx bytes are random, not a real PKCS#12.
- Certificate expiry over time (the 'expired' machine badge, the shared certificate status 'Expired', the 30-day warning of an existing certificate): this needs time to pass; the lifetime is at least one day.
- The 'Not the token endpoint' audience flag on a live target: this depends on the deployment's proxy or public base URL configuration.
- The limit of 20 trusted issuers per project: reaching the refusal 'A project has at most 20 trusted issuers.' takes 20 issuers added through the dialog, which is slow setup for one API message, even in a disposable project. The unit tests against the mock API cover it (test/unit/access/federation.test.ts).
- A deleted trusted issuer flagged 'issuer deleted' on the rules, and its name staying taken ('Federation rules still reference the trusted issuer …'): dropped as a low-value edge case that would need a third disposable federation setup. The TypeScript e2e and the unit tests cover it.
- The sign-in edge flows (the AuthKit hosted pages themselves, sign-up, refresh-token failure, 401 re-authentication), /unsupported for another provider, and boot configuration errors ('The admin UI cannot start'): these are WorkOS UI, token lifetimes and deployment configuration outside the app, with no deterministic trigger. The mock and working targets always boot ready.
- Onboarding on a live target: this needs a signed-in user without a Storyteller account, and every pool user has one. Covered on the mock only (ACC-SGN-06).
- The old project addresses /<org>/<project>/invitations and /<org>/<project>/certificates, and the Members and Machines entries of the navigation per role: owned by the Navigation section. NAV-RTE-05 'Old project links open their new sections' (NavigationRoutingTests.Routing_OldProjectLinks_OpenTheirSections) covers the two redirects (to /members#invitations and /machines#shared-certificates), and NAV-MNU-03 and NAV-MNU-04 cover the menu entries per role.
- The Account page's Preferences (theme, row density, formats, date format, annotation types): owned by the Navigation section (the theme, NAV-PAL-03) and the Settings section (the other preferences, SET-PRF-01 to SET-PRF-03).

### 2. Organizations and Projects

This section covers the pages outside a project's content where a user creates and finds organizations, projects and views. They are the Organizations page (`/orgs`) with "New organization", the organization overview (`/{org}`) with its project cards and the role-gated "New project", and the project's Views page (`/{org}/{project}/views`) with "New view", "Register", "Edit" and the "Compare" link to Compare views. Opening Compare views and its content belong to Modeling (MOD-CMP-01); this section asserts only the link and its href. The context switchers and the command palette belong to Navigation, and onboarding belongs to Access (ACC-SGN-06). Tests create what they act on, with run-id names: one organization (ORG-ORG-01), projects in the target organization, and views only in a disposable project (mode P), because the view list is shared by everyone in the project. The validation cases send nothing that the API would accept. There are three sub-folders, all in `Tests/Projects` with `[Area(Area.Projects)]`: Organizations (`OrganizationTests`), Projects (`ProjectTests`, which takes over the existing `ProjectsTests` case; `ProjectsTests.cs` is deleted) and Views (`ProjectViewTests`). The Elementary case is ORG-PRJ-01, `ProjectTests.Project_Create_OpensSettings` (Testiny 5).

#### Organizations (OrganizationTests)

##### ORG-ORG-01 New organization creates the organization with its first project and lists it
- **Priority** High · **Role** Any · **Mode** B · **Test** `OrganizationTests.Organization_Create_ListsItWithFirstProject`
- **Preconditions** Any signed-in pool user (every user may create an organization). Target view not needed. The test generates an organization {org}, a first project {p} and a description {d} (200 characters), each carrying the run id.

1. Open the Organizations page (`/orgs`) and click "New organization" → Dialog "New organization" with "You become the owner of the organization and its first project." and the fields "Organization", "First project" and "Project description"
2. Fill "Organization" {org}, "First project" {p} and "Project description" {d}; click "Create organization" → Toast "Organization {org} created"; the dialog closes
3. Check the page the app opened → URL `/{org}/{p}/default/annotations` with the empty state "No annotations in this view"
4. Open `/orgs` → A card with the link "{org}" and the badge "Owner", listing the project link "{p} Owner"
5. Click the link "{org}" → URL `/{org}`; heading "{org}" with the badge "Owner"
6. Check "Projects" on the overview → A card with the link "{p}", the badge "Owner", the key "{org}.{p}" and the description {d} (its title attribute holds the full text)
7. Click "{p}" on the card → URL `/{org}/{p}/default/annotations`

##### ORG-ORG-02 New organization validates the names and rejects an existing project
- **Priority** Medium · **Role** Owner · **Mode** B · **Test** `OrganizationTests.Organization_CreateInvalid_ShowsFieldErrors`
- **Preconditions** Owner of Target:Organization, so that the conflict step passes the owner check. The test creates nothing. It generates an unused organization name {org} and project name {p}, and uses the target's existing organization and project for the conflict.

1. Open `/orgs`, click "New organization", then click "Create organization" with the fields empty → "Enter a organization name." under Organization and "Enter a project name." under First project; the dialog stays open
2. Organization "Bad_Name"; click "Create organization" → "Use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit."
3. Organization "orgs"; click "Create organization" → "This organization name is reserved."
4. Organization {org}, First project "settings"; click "Create organization" → "This project name is reserved."
5. First project {p}, Project description of 501 characters; click "Create organization" → "At most 500 characters."
6. Organization {Target organization}, First project {Target project}, Project description empty; click "Create organization" → "This organization already exists, or you are not its owner." under Organization; no toast; the dialog stays open
7. Click "Cancel" → The dialog closes; `/orgs` lists no card "{org}"

#### Projects (ProjectTests)

##### ORG-PRJ-01 New project opens its annotations and its Settings show the description
- **Priority** Elementary · **Role** Owner · **Mode** B · **Test** `ProjectTests.Project_Create_OpensSettings` · existing Testiny case 5 (moved from `ProjectsTests`, whose file is deleted)
- **Preconditions** Owner of Target:Organization. The test generates a project {p} and a description {d} (200 characters) with the run id.

1. Open the overview of the target organization (`/{org}`) → Heading "{org}" and the button "New project"
2. Click "New project", fill "Project" {p} and "Description" {d}, click "Create project" → The dialog "New project" closes; toast "Project {p} created"
3. Check the page the app opened → URL `/{org}/{p}/default/annotations`
4. Open "Settings" in the navigation (the docked sidebar, or the slideover behind "Open navigation") → URL `/{org}/{p}/settings`; "Description" holds {d}; "Save description" is disabled

##### ORG-PRJ-02 New project validates the name and rejects an existing project
- **Priority** Medium · **Role** Owner · **Mode** B · **Test** `ProjectTests.Project_CreateInvalid_ShowsFieldErrors`
- **Preconditions** Owner of Target:Organization. The test creates nothing. It generates an unused project name {p} and uses Target:Project for the conflict.

1. Open the overview of the target organization (`/{org}`) and click "New project" → Dialog "New project" with "The project is created in {org}; you become its owner."
2. Click "Create project" with the fields empty → "Enter a project name."
3. Project "Bad_Name"; click "Create project" → "Use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit."
4. Project "settings"; click "Create project" → "This project name is reserved."
5. Project {p}, Description of 501 characters; click "Create project" → "At most 500 characters."
6. Project {Target project}, Description empty; click "Create project" → "A project with this name already exists."; no toast; the dialog stays open
7. Click "Cancel" → The dialog closes; the URL is still `/{org}`; no project card "{p}"

##### ORG-PRJ-03 A project member without an organization role cannot create projects
- **Priority** Medium · **Role** Reader · **Mode** M · **Test** `ProjectTests.Project_CreateWithoutOrganizationRole_IsDisabled`
- **Preconditions** Acting as Reader through Target:RoleProjects. On the mock this is northwind/portal, where Ada is Reader of the project and has no role on the organization northwind. Mock only: on a live target, a project role without an organization role needs another identity that owns the organization. The test creates nothing.

1. Open `/orgs` → A card with the link "{org}" and the badge "Project member", listing the project link "{project} Reader"
2. Click the link "{org}" → URL `/{org}`; heading "{org}" with the badge "Member of some projects"
3. Check "Projects" → A card with the link "{project}", the badge "Reader" and the key "{org}.{project}"
4. Check below the projects → Alert "You have no role on the organization itself. An organization role does not grant access to its projects, and project roles do not grant the organization's."
5. Check "New project" in the navbar → The button "New project" is disabled

#### Views (ProjectViewTests)

##### ORG-VEW-01 New view creates a view that Compare links to and the list opens
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `ProjectViewTests.Views_Create_LinksCompareAndOpensView`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project {np} in Target:Organization, then generates a view name {v} and a description {vd} with the run id.

1. Open the Views page of {np} (`/{org}/{np}/views`) → A row "default" with the badge "production" and a "Compare" link that is disabled (no href); the button "New view"
2. Desktop: hover "Compare" of "default" → Its description is "The project has no other view to compare with"
3. Click "New view" → Dialog "New view" with "A view is a separate catalog of annotations and configurations in the project." and the fields "Name" and "Description"
4. Fill "Name" {v} and "Description" {vd}; click "Create view" → The dialog closes; toast "View {v} created"; a row "{v}" with {vd} and "Edit", without "production"
5. Desktop: hover "Compare" of "default" again → Still disabled; its description is now "The other views are compared with this one"
6. Check "Compare" of "{v}" without clicking it → It is enabled and its href is `/{org}/{np}/default/modeling/compare?to={v}`
7. Click the link "{v}" → URL `/{org}/{np}/{v}/annotations` with "No annotations in this view"

##### ORG-VEW-02 Register the discovered default view and edit its description
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `ProjectViewTests.Views_RegisterDefault_EditsDescription`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project {np} in Target:Organization, whose default view is not registered yet, and generates a description {d}.

1. Open the Views page of {np} → A row "default" with the badges "production" and "discovered" and the button "Register"; no "Edit"
2. Click "Register" → Toast "View default registered"; "discovered" is gone; the row shows "Edit"
3. Click "Edit" → Dialog "Edit default" with the field "Description"
4. Type {d}; click "Save" → Toast "Description saved"; the dialog closes; the row shows {d}
5. Reload the page → The row "default" still shows {d} and "Edit", without "discovered"
6. Click "Edit", clear "Description", click "Save" → Toast "Description saved"; the row no longer shows {d}

##### ORG-VEW-03 New view validates the name and rejects an existing view
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `ProjectViewTests.Views_CreateInvalid_ShowsNameErrors`
- **Preconditions** Administrator or Owner of the target project. The test runs in the target project and creates nothing (every attempt fails).

1. Open the Views page of the target project and note the listed views → A row "default" with the badge "production"; the button "New view"
2. Click "New view", then "Create view" with the fields empty → Dialog "New view"; "Enter a view name." under Name
3. Name "Bad Name"; click "Create view" → "Use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit."
4. Name "members"; click "Create view" → "This view name is reserved."
5. Name "default"; click "Create view" → "A view with this name already exists."; no toast; the dialog stays open
6. Click "Cancel" → The dialog closes; the page lists the same views as in step 1

##### ORG-VEW-04 A Reader lists the views without Compare or managing actions
- **Priority** Medium · **Role** Reader · **Mode** B · **Test** `ProjectViewTests.Views_Reader_ListsWithoutActions`
- **Preconditions** Acting as Reader through Target:RoleProjects (mock: northwind/portal). The test creates nothing.

1. Open the Views page of the Reader project → A row "default" with the badge "production"
2. Check the page actions and the rows → No "New view" button; no "Compare", "Register" or "Edit" in the list
3. Click the link "default" → URL `/{org}/{project}/default/annotations`

##### ORG-VEW-05 A Contributor compares views but cannot create, register or edit them
- **Priority** Low · **Role** Contributor · **Mode** B · **Test** `ProjectViewTests.Views_Contributor_ComparesWithoutManaging`
- **Preconditions** Acting as Contributor through Target:RoleProjects (mock: acme/shipping). The test creates nothing.

1. Open the Views page of the Contributor project → A row "default" with "production" and a disabled "Compare" without href
2. Check the page actions and the rows → No "New view" button; no "Register" or "Edit" in the list
3. Check the "Compare" of every other listed view → Each is enabled and its href is `/{org}/{project}/default/modeling/compare?to={view}` (the mock lists only default, so there is none to check)
4. Desktop: hover "Compare" of "default" → "The project has no other view to compare with" when default is the only listed view (the mock), otherwise "The other views are compared with this one"

#### Not automated
- Failing API calls (5xx or network errors): the form alert of New organization, New project and New view, the toast "The view was not saved", and the Views page error state with "Retry". These need fault injection. MSW serves the mock API from a service worker that `page.route` cannot intercept, and the mock has no failure hook.
- New organization with an organization that someone else owns: this needs an organization owned by a second identity. The real API also answers 403 (shown as a form alert), while the mock answers 409 (shown as the field message "This organization already exists, or you are not its owner.").
- The API's InvalidName messages on the name fields: the form applies the same rules as the server (the pattern and the reserved names), so the UI never sends a name that the API rejects as invalid.
- The empty states "No organizations yet" (`/orgs`), "No projects you are a member of" (overview) and "No projects you are a member of." (organization card): they need an account without memberships, or an organization role without a project. The UI always creates an organization together with its first project, the mock's new user is sent to onboarding, and no pool user is in that state.
- The tooltip "Requires the Owner role" of the disabled "New project": a disabled button does not dispatch pointer events in every engine (Firefox, WebKit), so a hover cannot open the tooltip reliably. ORG-PRJ-03 asserts the disabled state instead.
- Behaviour of exactly an Administrator, either of the project (New view, Register and Edit without being an Owner) or of the organization (New project disabled): the mock has no project where the user is exactly Administrator, and no organization Administrator role.
- Discovered views other than default (a view that only the project's data holds, listed with "discovered" and "Register"): the mock ignores `?discover` and never scans the data. Only the default view of a new project is unregistered on both the mock and the real API (ORG-VEW-02).
- A New view description over 500 characters: the form shows Zod's default message, which is not product text and changes between Zod versions.
- New organization with an organization the user already owns: the API only adds the project, but the toast still says "Organization {org} created". This is an edge case of the shared endpoint; New project covers creating a project.
- Opening Compare views through "Compare" of a view (the landing URL with `to={view}`, the selected tab "Compare views", the preselected view and the differences): owned by Modeling (MOD-CMP-01). This section asserts only that the link is enabled and its href (ORG-VEW-01, ORG-VEW-05).
- A new view in the view switcher, and a new project or organization in the switchers and the command palette: owned by Navigation (the context switcher NAV-CTX-01 to NAV-CTX-03, NAV-CTX-02 with a new project, and the command palette NAV-PAL-01).
- Onboarding, where a new user creates the first organization: owned by Access (ACC-SGN-06, `SignInTests.Onboarding_NewUser_CreatesFirstOrganization`), mock only with the new-user scenario.
- Deleting organizations, projects or views: neither the UI nor the API offers it. Created items stay under their run-id names.
- A view's relative creation time and the two-line clamp of project descriptions: cosmetic; covered by the visual suite.

### 3. Navigation

The Navigation section covers the shell around every page of the admin UI. That is the navigation menu (the docked sidebar, the icon rail between 1024 and 1279 px, the slideover behind "Open navigation", pinning, and the entries each role sees or finds locked), the context switcher (organization, project and view) and the command palette with the global theme. It also covers the keyboard shortcuts and their help, the routing pages and redirects ("Page not found", "You have no access here", "/" and /{org}/{project} opening the last context, the old project addresses /secrets, /certificates and /invitations, and the page memory across sections), and the frame of the detail panel (overlay, pinned, full screen, keyboard resize and the remembered mode). The tests live in the folder `Tests/Navigation` and carry `[Area(Area.Navigation)]`. The section has six sub-folders, each one test class: Navigation Menu (`NavigationMenuTests`), Context Switcher (`NavigationContextSwitcherTests`), Command Palette (`NavigationCommandPaletteTests`), Keyboard Shortcuts (`NavigationShortcutTests`), Routing (`NavigationRoutingTests`) and Detail Panel (`NavigationDetailPanelTests`). Every case passes on the mock build (MSW, Ada Lovelace, scenario default; desktop 1440×900 and Pixel 7 unless its mode skips one) and on a live target, where it creates only what it acts on. The Elementary PR-gate case is NAV-MNU-01, the existing Testiny case 6 `Navigation_MobileMenu_OpensPages`, which moves from `NavigationTests` to `NavigationMenuTests`; the old class `NavigationTests` is deleted, so the test exists only once. The menu and the palette per role belong to this section (NAV-MNU-03 to NAV-MNU-05). Sign-in, the Account page and onboarding belong to Access (ACC-SGN-*), and organizations, projects and the Views page to Organizations and Projects; they are only visited here.

#### Navigation Menu (NavigationMenuTests)

##### NAV-MNU-01 Navigation menu opens the pages of the view, the project and the app
- **Priority** Elementary · **Role** Any · **Mode** B · **Test** `NavigationMenuTests.Navigation_MobileMenu_OpensPages` · existing Testiny case 6
- **Preconditions** Any pool user, signed in. The test works in the target view (mock: acme/billing/default) and creates nothing.
1. Open the annotations of the target view → URL /{org}/{project}/{view}/annotations; the text box "Filter annotations" is visible.
2. Follow "Configurations" in the navigation menu (the docked sidebar; below 1024 px the slideover behind "Open navigation") → URL /{org}/{project}/{view}/configurations; "Filter annotations" is visible; the slideover has closed.
3. Follow "Values store" → URL /{org}/{project}/{view}/key-values; the text box "Search" is visible.
4. Follow "Settings" → URL /{org}/{project}/settings; the heading "Project" is visible.
5. Follow "Organizations" → URL /orgs; the heading "Organizations" is visible.
6. Follow "Annotations" → back on /{org}/{project}/{view}/annotations with "Filter annotations".

##### NAV-MNU-02 Pages outside the view keep the sections of the last view
- **Priority** High · **Role** Any · **Mode** B · **Test** `NavigationMenuTests.Navigation_OtherPages_KeepLastViewSections`
- **Preconditions** Any pool user, in the target project. The test uses a generated view name {g} (PlatformData.ViewName()) that is never registered. Views are implicit, so opening the view writes nothing.
1. Open /{org}/{project}/{g}/configurations → "No configurations in this view"; the menu's section label reads "View · {g}" without the badge "prod".
2. Follow "Settings" → URL /{org}/{project}/settings; the menu link "Annotations" points to /{org}/{project}/{g}/annotations and "Settings" points to /{org}/{project}/settings.
3. Follow "Overview" → URL /{org}; "Annotations" still points to /{org}/{project}/{g}/annotations and "Settings" to /{org}/{project}/settings.
4. Follow "Organizations" → URL /orgs; "Annotations" still points to /{org}/{project}/{g}/annotations and "Settings" to /{org}/{project}/settings.
5. Follow "Account" → URL /account; the same two links; the label "View · {g}" is still shown.
6. Follow "Annotations" → URL /{org}/{project}/{g}/annotations; "No annotations in this view".

##### NAV-MNU-03 An Administrator's menu lists every entry without locks
- **Priority** High · **Role** Admin · **Mode** B · **Test** `NavigationMenuTests.Navigation_AdministratorMenu_ListsEveryEntryUnlocked`
- **Preconditions** Administrator or Owner of the target project (mock: acme/billing, Owner). The test works in the target view and creates nothing.
1. Open the annotations of the target view → the menu's view section lists, in this order, "Annotations", "Configurations", "Values store" (/{org}/{project}/{view}/key-values), "Graph" and "Modeling".
2. Look at the five view entries → none of them has a lock badge.
3. Look at the "Project" section → links to /{org}/{project}/views, /members, /machines, /settings and /monitor.
4. Look for the old pages that were merged into others → no link to /{org}/{project}/invitations, /certificates, /secrets, /audit or /usage.
5. Look at the "Organization" section and the global entries → "Overview" (/{org}), "Organizations" (/orgs) and "Account" (/account).
6. Follow "Views", then run "Export & Import" in the command palette → the option is listed with the suffix "Modeling"; URL /{org}/{project}/{view}/modeling/export-import.

##### NAV-MNU-04 A Contributor's menu leaves out Members, Monitor and Export & Import
- **Priority** Medium · **Role** Contributor · **Mode** B · **Test** `NavigationMenuTests.Navigation_ContributorMenu_HidesAdministration`
- **Preconditions** The pool user is exactly Contributor in the project of Target:RoleProjects (mock: acme/shipping/default, where Ada is Contributor). The test creates nothing.
1. Open the annotations of the role's project and wait for the catalog → the view entries "Annotations", "Configurations", "Values store", "Graph" and "Modeling" are listed, none with a lock badge.
2. Look at the "Project" section → links to /views, /machines and /settings of the project; no link to /members or /monitor.
3. Open the command palette with an empty search → the actions "New annotation" and "Bulk create annotations" are listed.
4. Type "Schemas" → the option "Schemas" with the suffix "Modeling" is listed.
5. Type "Export & Import" → no option starts with "Export & Import".
6. Press Escape → the dialog "Search…" closes.

##### NAV-MNU-05 A Reader's menu locks the editing sections
- **Priority** Medium · **Role** Reader · **Mode** B · **Test** `NavigationMenuTests.Navigation_ReaderMenu_LocksEditingSections`
- **Preconditions** The pool user is exactly Reader in the project of Target:RoleProjects (mock: northwind/portal/default, with no organization role). The test creates nothing.
1. Open the annotations of the role's project and wait for the catalog → "Annotations", "Configurations", "Values store" and "Modeling" have a lock badge; "Graph" has none.
2. Look at the "Project" section → only "Views" and "Settings"; no link to /members, /machines or /monitor.
3. Desktop only: set the viewport to 1100×900 (the icon rail) and hover the Annotations entry, then the Modeling entry → their accessible descriptions are "Read-only for your role" and "Contributors only".
4. Open the command palette with an empty search → "Switch project" is listed; "New annotation" and "Bulk create annotations" are not.
5. Type "Modeling", then "Schemas" → "Modeling" (suffix "View · {view}") is listed; then no option "Schemas" with the suffix "Modeling" is listed (a locked Modeling lists no tabs).
6. Press Escape → the dialog "Search…" closes.

##### NAV-MNU-06 Unpinning the sidebar moves the menu behind Open navigation and is kept
- **Priority** Medium · **Role** Any · **Mode** B,D · **Test** `NavigationMenuTests.Sidebar_Unpin_MovesMenuBehindButtonAndIsKept`
- **Preconditions** Any pool user, on a desktop viewport (1440×900), in the target view. A fresh browser context starts with the sidebar pinned. The test changes only browser-local preferences.
1. Open the annotations of the target view → the docked sidebar shows the button "Unpin sidebar"; "Open navigation" is not visible.
2. Click "Unpin sidebar" → the docked sidebar is gone; "Open navigation" is visible.
3. Click "Open navigation" → the dialog "Navigation" opens with the menu and the button "Pin sidebar".
4. Follow "Configurations" in the dialog → URL /{org}/{project}/{view}/configurations; the dialog closes.
5. Reload the page → still unpinned: "Open navigation" is visible and there is no docked sidebar.
6. Run "Pin the sidebar" in the command palette → the docked sidebar is back with "Unpin sidebar"; "Open navigation" is hidden.

##### NAV-MNU-07 The icon rail expands and collapses, and [ toggles the docked sidebar
- **Priority** Low · **Role** Any · **Mode** B,D · **Test** `NavigationMenuTests.Sidebar_IconRail_ExpandsCollapsesAndToggles`
- **Preconditions** Any pool user, on a desktop viewport, in the target view. The test sets the viewport itself and creates nothing.
1. Set the viewport to 1100×900 and open the annotations of the target view → the icon rail: "Expand sidebar" is visible and the menu links show icons without names.
2. Click "Expand sidebar" → the link "Configurations" is visible by its name; "Collapse sidebar" is visible.
3. Click "Collapse sidebar" → the icon rail again, with "Expand sidebar".
4. Set the viewport to 1440×900 → the sidebar expands with its labels ("Collapse sidebar").
5. With no text box focused, press "[" → the icon rail ("Expand sidebar").
6. Press "[" again → the expanded sidebar ("Collapse sidebar").

#### Context Switcher (NavigationContextSwitcherTests)

##### NAV-CTX-01 The view switcher changes the view, keeps the section and marks the default view
- **Priority** High · **Role** Any · **Mode** B,D · **Test** `NavigationContextSwitcherTests.ContextSwitcher_PickView_KeepsSection`
- **Preconditions** Any pool user, in the target project, on a desktop viewport (the view button is hidden below 640 px). The test uses a generated view name {g} (PlatformData.ViewName()) that is never registered. It creates nothing: the view menu always offers "default".
1. Open /{org}/{project}/{g}/configurations → the view button in the navigation "Context" reads "{g}"; the menu label "View · {g}" has no "prod".
2. Click the view button → the menu includes the items "default prod" and "Manage views" (mock: also "staging").
3. Pick "default prod" → URL /{org}/{project}/default/configurations; the view button reads "default prod"; the label "View · default" has the badge "prod".
4. Open /{org}/{project}/{g}/modeling/templates and pick "default prod" in the view menu → URL /{org}/{project}/default/modeling/templates (Modeling keeps its tab).
5. Open the view menu and pick "Manage views" → URL /{org}/{project}/views; the view "default" is listed.

##### NAV-CTX-02 The project switcher keeps the project section and the view section
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `NavigationContextSwitcherTests.ContextSwitcher_PickProject_KeepsSection`
- **Preconditions** Owner of the target organization. The test first creates a disposable project {p2} (PlatformData.ProjectKey()) in the target organization with "New project". It only visits the target project.
1. On /{org} create the project {p2} with "New project" → the toast "Project {p2} created"; URL /{org}/{p2}/default/annotations.
2. Open /{org}/{project}/settings → the project button in the navigation "Context" reads "{project}".
3. Click the project button → the menu includes the items "{project}", "{p2}" and "Organization overview" (mock: also "shipping").
4. Pick "{p2}" → URL /{org}/{p2}/settings.
5. Follow "Values store" → URL /{org}/{p2}/default/key-values.
6. Pick "{project}" in the project menu → URL /{org}/{project}/default/key-values.
7. Pick "Organization overview" in the project menu → URL /{org}; the heading "{org}".

##### NAV-CTX-03 The organization switcher lists the organizations and opens an overview
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `NavigationContextSwitcherTests.ContextSwitcher_PickOrganization_OpensOverview`
- **Preconditions** Any pool user, in the target view. The test creates nothing.
1. Open the annotations of the target view and click the organization button "{org}" → the menu includes the items "{org}" and "All organizations" (mock: "acme" and "northwind").
2. Pick "All organizations" → URL /orgs; the heading "Organizations"; no navigation "Context" on the page.
3. Go back in the browser → the annotations of the target view; the navigation "Context" shows "{org}" and "{project}".
4. Pick "{org}" in the organization menu → URL /{org}; the heading "{org}"; the navigation "Context" has only the organization button.

#### Command Palette (NavigationCommandPaletteTests)

##### NAV-PAL-01 The command palette opens pages and projects, also from a text box
- **Priority** High · **Role** Any · **Mode** B · **Test** `NavigationCommandPaletteTests.CommandPalette_PagesAndProjects_Navigate`
- **Preconditions** Any pool user. The test visits the target view first, so that the palette's pages follow it. It creates nothing.
1. Open the annotations of the target view, then follow "Organizations" → URL /orgs.
2. Click "Search…" → the dialog "Search…" opens with the placeholder "Search annotations, pages, projects and actions…" and the groups "Pages", "Projects", "Actions" and "Theme".
3. Type "Settings" and pick the first option → its name starts with "Settings" (suffix "Project"); URL /{org}/{project}/settings; the dialog "Search…" closes.
4. Press Ctrl+K (⌘K on macOS), type "{org} / {project}" and pick that option → the option shows the user's role as its suffix; URL /{org}/{project}/default/annotations.
5. Click into "Filter annotations" and press Ctrl+K → the dialog "Search…" opens although the focus was in "Filter annotations"; Escape closes it and "Filter annotations" keeps its value.
6. Follow "Settings", then run "Switch project" in the palette → URL /orgs; the heading "Organizations".

##### NAV-PAL-02 The palette's editor actions create an annotation that the palette then finds and lists as recent
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `NavigationCommandPaletteTests.CommandPalette_EditorActions_FindAndRecallAnnotation`
- **Preconditions** Contributor or higher in the target project, in the target view. The test creates one responsibility {key} (PlatformData.AnnotationKey("rst")) through the palette's Bulk create.
1. Open the configurations of the target view and wait until "Loading configurations…" is gone → the list (or "No configurations in this view") is shown.
2. Run "Bulk create annotations" in the palette → URL /{org}/{project}/{view}/annotations; the dialog "Bulk create annotations" opens.
3. Enter {key}, click "Create 1 annotation", then "Done" → "Created 1 annotation."; the dialog closes.
4. Run "New annotation" in the palette → the dialog "New annotation" opens; "Cancel" closes it without creating anything.
5. Open the palette and type {key} → an option with {key} is listed in the group "Annotations".
6. Pick it → URL /{org}/{project}/{view}/annotations/{key}; the detail of {key} is shown.
7. Close the detail ("Close the detail"; on a phone "Back to the list") and open the palette with an empty search → the group "Recent" lists {key}.
8. Press Escape → the dialog "Search…" closes.

##### NAV-PAL-03 The theme switches in the palette and on the Account page and is kept
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `NavigationCommandPaletteTests.Theme_SwitchInPaletteAndAccount_IsKept`
- **Preconditions** Any pool user, in the target view. The test emulates a light browser color scheme. A fresh browser context has the theme "System". Only the browser-local theme changes.
1. Open the annotations of the target view → <html> has no class "dark"; the top bar has no button "Color mode".
2. Open the palette with an empty search → the group "Theme" offers "System", "Light" and "Dark".
3. Pick "Dark" → the dialog "Search…" closes; <html> has the class "dark".
4. Reload the page → <html> still has the class "dark".
5. Open the user menu "User menu for {name}" and pick "Account" → URL /account; the Theme tab "Dark" is selected.
6. Click the tab "Light" → <html> has no class "dark"; "Light" is selected.
7. Click the tab "System", then emulate a dark color scheme → <html> has the class "dark"; with the light scheme again it has not.

#### Keyboard Shortcuts (NavigationShortcutTests)

##### NAV-KEY-01 The keyboard shortcuts help opens from ?, the user menu and the palette
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `NavigationShortcutTests.Shortcuts_Help_OpensFromKeyUserMenuAndPalette`
- **Preconditions** Any pool user, in the target view. The test creates nothing.
1. Open the annotations of the target view and, with no text box focused, press "?" → the dialog "Keyboard shortcuts" opens with the groups "General", "Go to" and "Lists and details".
2. Read the dialog → it lists "Command palette", "Toggle the sidebar", "Show this help", "Export & Import" and "Close the detail".
3. Press Escape → the dialog "Keyboard shortcuts" closes.
4. Open the user menu "User menu for {name}" → the items "Account", "Organizations", "Keyboard shortcuts" and "Sign out".
5. Pick "Keyboard shortcuts" → the dialog "Keyboard shortcuts" opens; Escape closes it.
6. Follow "Organizations" and run "Keyboard shortcuts" in the palette → URL /orgs; the dialog "Keyboard shortcuts" opens.

##### NAV-KEY-02 Go-to shortcuts open the sections of the last view and are ignored in text boxes
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `NavigationShortcutTests.Shortcuts_GoTo_OpenSectionsOfLastView`
- **Preconditions** Any pool user, in the target view. The test creates nothing.
1. Open the annotations of the target view and, with no text box focused, press g, then c → URL /{org}/{project}/{view}/configurations.
2. Press g, then g → URL /{org}/{project}/{view}/graph.
3. Press g, then a → URL /{org}/{project}/{view}/annotations.
4. Click "Filter annotations" and type "gc" → the filter holds "gc"; the URL path is still /{org}/{project}/{view}/annotations.
5. Clear the filter, leave the text box and press g, then o → URL /orgs.
6. On /orgs press g, then s → URL /{org}/{project}/{view}/modeling/schemas (the last view).
7. Press g, then t, then g, then e → URL /{org}/{project}/{view}/modeling/templates, then /{org}/{project}/{view}/modeling/export-import.

#### Routing (NavigationRoutingTests)

##### NAV-RTE-01 An unknown address shows Page not found, and Go home opens the last view
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `NavigationRoutingTests.Routing_UnknownAddress_ShowsPageNotFound`
- **Preconditions** Any pool user. The test visits the target view first, so that it is the last context. It creates nothing.
1. Open the annotations of the target view → "Filter annotations" is visible.
2. Open /{org}/{project}/{view}/no-such-page → the heading "Page not found" and "Nothing lives at /{org}/{project}/{view}/no-such-page."
3. Look at the navigation menu → it still offers the last view: "Annotations" points to /{org}/{project}/{view}/annotations.
4. Open /{org}/{project}/{view}, a view without a section → the heading "Page not found".
5. Click "Go home" → URL /{org}/{project}/{view}/annotations.

##### NAV-RTE-02 An organization or a project outside the memberships shows No access
- **Priority** High · **Role** Any · **Mode** B · **Test** `NavigationRoutingTests.Routing_ForeignContext_ShowsNoAccess`
- **Preconditions** Any pool user. The test uses a generated organization {o} (PlatformData.OrganizationKey()) and a generated project {p} (PlatformData.ProjectKey()) that do not exist. It creates nothing.
1. Open /{o} → the heading "You have no access here" with "Your account is not a member of this organization or project. Ask an administrator to invite you."
2. Click "Your organizations" → URL /orgs; the heading "Organizations".
3. Open /{org}/{p}/default/annotations → "You have no access here"; the navigation "Context" shows "{org}" and "{p}".
4. Pick "{project}" in the project menu → URL /{org}/{project}/default/annotations; "Filter annotations" is visible.

##### NAV-RTE-03 Home, the project address and the app root open the last context
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `NavigationRoutingTests.Routing_Home_OpensLastContext`
- **Preconditions** Any pool user. The test visits the target view first, so that it is the last context. It creates nothing.
1. Open the configurations of the target view → "Filter annotations" is visible.
2. Click "2S Admin home" in the navigation menu → URL /{org}/{project}/{view}/annotations.
3. Open /{org}/{project} → URL /{org}/{project}/default/annotations.
4. Follow "Organizations", then open "/" → URL /{org}/{project}/default/annotations (the last context).
5. Open /onboarding → it is replaced by the last context: URL /{org}/{project}/default/annotations.

##### NAV-RTE-04 Each section of a view comes back with its own filter until a reload
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `NavigationRoutingTests.PageMemory_ReturnToSection_RestoresItsFilter`
- **Preconditions** Any pool user, in the target view. The test uses two filters that match nothing: {a} = "key:" + PlatformData.Slug("a") and {c} = "key:" + PlatformData.Slug("c"). It creates nothing.
1. Open the annotations of the target view and filter {a} → URL /{org}/{project}/{view}/annotations with q={a}.
2. Follow "Configurations" and filter {c} → URL /{org}/{project}/{view}/configurations with q={c}.
3. Leave the text box and press g, then a → URL …/annotations with q={a}; "Filter annotations" holds {a}.
4. Follow "Configurations" → URL …/configurations with q={c}; "Filter annotations" holds {c}.
5. Follow "Values store", then "Annotations" → URL …/annotations with q={a} (Annotations kept its own memory).
6. Open the configurations by their address (a reload), then follow "Annotations" → URL …/annotations without q; "Filter annotations" is empty (the memory does not survive a reload).

##### NAV-RTE-05 Old project links open their new sections
- **Priority** Low · **Role** Admin · **Mode** B · **Test** `NavigationRoutingTests.Routing_OldProjectLinks_OpenTheirSections`
- **Preconditions** Administrator or Owner of the target project (mock: acme/billing, Owner): the Shared certificates section is shown to Administrators only. The test opens the old addresses directly and creates nothing.
1. Open /{org}/{project}/secrets → URL /{org}/{project}/settings#secrets; the section with the heading "Secrets" is in view and has the focus, and it shows the heading "Allowed levels".
2. Open /{org}/{project}/certificates → URL /{org}/{project}/machines#shared-certificates; the section with the heading "Shared certificates" is in view and has the focus, with the button "Issue certificate".
3. Open /{org}/{project}/invitations → URL /{org}/{project}/members#invitations; the section with the heading "Invitations" is in view and has the focus.

#### Detail Panel (NavigationDetailPanelTests)

##### NAV-DET-01 The detail pins, resizes by keyboard and keeps its mode for every detail
- **Priority** Medium · **Role** Editor · **Mode** B,D · **Test** `NavigationDetailPanelTests.DetailPanel_PinAndResize_KeptForEveryDetail`
- **Preconditions** Contributor or higher in the target project, on a desktop viewport, in the target view. The test first creates a responsibility {key} with Bulk create. A fresh browser context opens details as an overlay 40 % wide.
1. Open the annotations, bulk-create {key}, filter "key:{key}" and click its row → URL …/annotations/{key}; the detail's mode is "overlay"; the separator "Resize the detail" has the value 40.
2. Click "Pin the detail" → the mode is "pinned"; the button now reads "Unpin the detail".
3. Focus the separator "Resize the detail" and press ArrowLeft 5 times → its value is 50.
4. Double-click the separator → its value is 40 again.
5. Press Escape while the separator has the focus → the detail closes; URL …/annotations with q=key:{key}.
6. Click the row of {key} again → the detail's mode is "pinned" with "Unpin the detail" (remembered).
7. Open /{org}/{project}/{view}/modeling/templates/rst by its address (a reload) → the template detail opens pinned, with "Unpin the detail".
8. Click "Unpin the detail" → the mode is "overlay".

##### NAV-DET-02 The detail goes full screen and stays so after a reload
- **Priority** Medium · **Role** Editor · **Mode** B,D · **Test** `NavigationDetailPanelTests.DetailPanel_FullScreen_SurvivesReload`
- **Preconditions** Contributor or higher in the target project, on a desktop viewport, in the target view. The test first creates a responsibility {key} with Bulk create.
1. Open the annotations, bulk-create {key}, filter "key:{key}" and click its row → the detail's mode is "overlay".
2. Click "Open in full screen" → the mode is "full"; the list's "Filter annotations" is hidden; "Pin the detail" and the separator "Resize the detail" are gone; "Exit full screen" is shown.
3. Reload the detail's address /{org}/{project}/{view}/annotations/{key} → the mode is still "full".
4. Click "Exit full screen" → the mode is "overlay"; "Filter annotations" is visible again.
5. Click "Close the detail" → no detail on the page; URL /{org}/{project}/{view}/annotations.

##### NAV-DET-03 On a phone the detail covers the list until Back to the list
- **Priority** Medium · **Role** Editor · **Mode** B,Ph · **Test** `NavigationDetailPanelTests.DetailPanel_Phone_CoversListUntilBack`
- **Preconditions** Contributor or higher in the target project, on the phone profile (Pixel 7, 412 px), in the target view. The test first creates a responsibility {key} with the palette's "Bulk create annotations".
1. Open the annotations, bulk-create {key} and filter "key:{key}" → the list shows the item {key}.
2. Tap the item → URL …/annotations/{key}; the detail's mode is "full"; "Back to the list" is visible.
3. Look at the frame → no "Open in full screen", "Pin the detail", "Close the detail" or "Resize the detail"; the list's navbar with "Open navigation" and "Filter annotations" is hidden.
4. Tap "Back to the list" → URL …/annotations with q=key:{key}; the item {key} and "Open navigation" are visible.
5. Open the detail's address /{org}/{project}/{view}/annotations/{key} directly (a reload) → the detail's mode is "full"; "Back to the list" is visible.
6. Tap "Back to the list" → URL /{org}/{project}/{view}/annotations without q; "Filter annotations" is empty.

#### Not automated
- Global API error toasts ("The API failed", "The API is not reachable", "Unexpected error") and the account error state "Your account could not be loaded" with "Retry": they need fault injection. The mock's API runs in an MSW service worker, which page.route cannot intercept, and the mock has no failure hook. Live-only cases are not part of this catalogue.
- Telemetry (PostHog pageviews from the telemetry guard, Sentry reports, ph-no-capture): it is off in the mock and e2e builds. On a live target it only sends requests to third-party hosts and shows nothing in the UI.
- The 42 logo in the sidebar and the bare layout, the favicon, the environment badge, the navbar title text from 1536 px, the purple accent colors, the theme pre-paint (no flash) and reduced motion: these are visual and timing checks, covered by the TypeScript visual suite in the Playwright Linux container.
- Pointer drag of the docked sidebar border (12-24 %) and of the detail border: pixel and percentage based, and dependent on the viewport and Nuxt UI internals, so flaky across browsers and BrowserStack. The keyboard resize and the double-click reset of the detail are covered (NAV-DET-01).
- Tooltips of locked entries and disabled controls opened by keyboard or touch: reka-ui opens them only on mouse hover, by design. Hover is covered on the desktop (NAV-MNU-05).
- Behaviour across several tabs (per-tab mock data and session against shared localStorage preferences and lastContext): the order of writes to the shared localStorage between tabs is not deterministic.
- The other redirect stubs of merged pages: /{org}/{project}/audit and /usage, the view-level /schemas, /templates, /export-import, /deleted and /compare, and the old annotations/<key>/* and graph/<key>/* tab addresses: Monitoring owns /usage and /audit (MON-PAG-04), Modeling every redirect of the merged Modeling pages (MOD-TAB-02), Annotations the annotations/<key>/* tab stubs (ANN-DET-06) and Graph the graph/<key>/* stubs (GRA-DET-04). Navigation owns only the old project addresses /secrets, /certificates and /invitations (NAV-RTE-05).
- The page memory of each section's own state (the Annotations filter, detail, tab, selection, sorting and scroll; the last configuration and tab; the Graph's URL state and hidden types; the last Modeling tab) and the reset when the open entry is selected again: owned by those sections (ANN-CAT-05, CFG-NAV-03, GRA-MEM-01, MOD-TAB-05). Navigation keeps only the cross-section guard case NAV-RTE-04: each section's own memory, forgotten on a reload.
- The list and detail shortcuts ("/", J, K, Enter, Meta+S, Meta+Shift+F): they belong to the list and editor pages of Annotations, Configurations and Modeling. Here they are only checked as entries of the help dialog (NAV-KEY-01).
- "[" below 1024 px opening the slideover "Open sidebar": a hardware-keyboard shortcut on a touch layout. The slideover is covered by NAV-MNU-01 and the docked toggle by NAV-MNU-07.
- The organization "Members" entry per organization role: on a live target the organization role of a pool user is not known (the Users:Pool roles are the project roles). The organization Members page belongs to Access.
- The menus of an exact Administrator or ContributorWithSecrets: the mock has no project with those exact roles. An Administrator sees the same menu as an Owner (NAV-MNU-03 runs as either), and ContributorWithSecrets sees the same menu as a Contributor.
- "/" with a remembered context the user has lost access to: a membership would have to be removed by a second identity while the test runs.
- Sign-in, sign-out, the login, callback and unsupported pages, the Account profile and memberships, and onboarding; Organizations, the organization overview, New organization, New project and the Views page: owned by Access (ACC-SGN-01 to ACC-SGN-06: sign-in, the account and onboarding) and by Organizations and Projects (the organizations, projects and views). Navigation only visits them; NAV-RTE-03 checks only that /onboarding with an account opens the last context.
- The Vue Query devtools button: it exists only in the dev mode outside e2e, and is not part of any tested build.

### 4. Annotations

This section covers the catalog of one view at `/{org}/{project}/{view}/annotations`; its tests live in `Tests/Annotations` with `[Area(Area.Annotations)]`. That includes both create flows: Bulk create, and New annotation with its missing ancestors and deleted keys. It also covers the filter language and the phone filter sheet; the grid, the tree, the bulk bar, the empty view, the catalog reload and the page memory; and the detail opened over the list, with its header, More actions, breadcrumb, Lineage, Relations, History and the old annotation tab URLs. Then the Annotation form: Form, JSON and YAML modes, every field, validation and the unsaved-changes guard. Last come deletion from the detail with Undo, the deleted banner, and the Reader and Contributor boundaries. Every test creates its own keys and never changes seed keys or project-wide settings; ANN-CAT-03 and ANN-ROL-01 only read an unused view, and only the mock-only ANN-ROL-03 reads seed annotations (those of the Reader project). Keys are built from generated parts that carry the run id (`<s>`, `<r>`, `<c>`, `<u>`, `<p>` for a shared prefix, `<t>` for a title, `<l>` for a label). `<user>` is the signed-in user's display name (Ada Lovelace on the mock), `{total}` is whatever the view holds, and "phone" means below 640 px (Pixel 7). Other sections own the frame of the detail panel and the command palette (Navigation), the project's default time zone and the type names (Settings), the Deleted list and the old `/{view}/deleted` URL (Modeling), and the graph's node detail with its old tab URLs (Graph). There are eight sub-folders, one class each: Bulk Create (`AnnotationBulkCreateTests`), New Annotation (`AnnotationCreateTests`), Filtering (`AnnotationFilterTests`), Catalog (`AnnotationCatalogTests`), Detail (`AnnotationDetailTests`), Annotation Form (`AnnotationFormTests`), Delete and Restore (`AnnotationDeleteTests`) and Role Access (`AnnotationRoleTests`). The Elementary case is ANN-BLK-01, the existing Testiny case 2 `Annotations_CreateResponsibility_AppearsInGrid`. It moves from `AnnotationsTests` to `AnnotationBulkCreateTests`, and `AnnotationsTests.cs` is deleted, so the test exists only once.

#### Bulk Create (AnnotationBulkCreateTests)

##### ANN-BLK-01 Bulk create a responsibility and find it in the grid
- **Priority** Elementary · **Role** Editor · **Mode** B · **Test** `AnnotationBulkCreateTests.Annotations_CreateResponsibility_AppearsInGrid` · existing Testiny case 2
- **Preconditions** A pool user with Contributor or higher in the target project; the target view (mock: acme/billing/default). A new key rst.`<r>` is generated; nothing is created beforehand.

1. Open the Annotations page and wait until "Reload the catalog" is enabled → The grid "Annotations" (phone: the list "Annotations") and the box "Filter annotations" are shown.
2. Click "Bulk create" (phone: "Search…" › "Bulk create annotations") → Dialog "Bulk create annotations" with the field "Keys".
3. Type rst.`<r>` into "Keys" → Summary "1 new"; the Review row rst.`<r>` shows "New".
4. Click "Create 1 annotation" → The result "Created 1 annotation." replaces the form; no toast.
5. Click "Done" → The dialog closes.
6. Type key:rst.`<r>` into "Filter annotations" → The URL gets q=key:rst.`<r>`; the row (phone: item) rst.`<r>` is listed.
7. Open …/annotations/rst.`<r>` → The identity line reads rst.`<r>`.

##### ANN-BLK-02 Compact lines create their missing ancestors
- **Priority** High · **Role** Editor · **Mode** B · **Test** `AnnotationBulkCreateTests.BulkCreate_CompactLines_CreatesMissingAncestors`
- **Preconditions** Editor in the target view. Generated parts `<s>`, `<r>`, `<r2>`, `<c>`; nothing is created beforehand.

1. Wait for the catalog and open "Bulk create" → Dialog "Bulk create annotations" with "In {org}/{project}, view {view}."
2. Enter the lines exe.`<s>`.`<r>`.`<c>` and `<s>`.`<r2>` → Summary "2 new · 5 missing ancestors". The Review row of line 2 shows the key usg.`<s>`.`<r2>`, the typed text `<s>`.`<r2>` and "New".
3. Click "Create 2 annotations" → "Created 7 annotations: 2 from the list and 5 missing ancestors."
4. Read the region "Created annotations" → In the column "Created as":
   - exe.`<s>`.`<r>`.`<c>` and usg.`<s>`.`<r2>` show "From the list";
   - usg.`<s>`.`<r>`, cnt.`<s>`.`<c>`, rst.`<r>`, rst.`<r2>` and sbt.`<s>` show "Missing ancestor".
5. Click "Create more" → The result is gone; "Keys" is empty and focused.
6. Click "Cancel", wait for the catalog and filter subject:`<s>` → The dialog is closed; the count reads "5 of {total}".
7. Open …/annotations/exe.`<s>`.`<r>`.`<c>` → The identity reads the key; "Title" is empty; "Time zone" shows "Browser time zone (…)".

##### ANN-BLK-03 Review marks existing, duplicate and invalid lines and enforces the limit
- **Priority** High · **Role** Editor · **Mode** B · **Test** `AnnotationBulkCreateTests.BulkCreate_ReviewProblems_BlockUntilRemoved`
- **Preconditions** Editor in the target view. rst.`<r>` is created first with Bulk create and the catalog is reloaded. Generated parts `<s>`, `<a>`, `<b>`. A list of 1,001 lines sbt.`<s>`-1 … sbt.`<s>`-1001 is built in memory.

1. Open "Bulk create" and fill "Keys" with the 1,001 lines → Field error "At most 1,000 keys at once; the list has 1,001."; "Create 1,001 annotations" is disabled.
2. Replace the text with eight lines: rst.`<r>`, sbt.`<s>`, sbt.`<s>`, exe.`<a>`.`<b>`, `<a>`..`<b>`, `<a>`/`<b>`, `<a>`⇥`<b>` (with a tab) and `<a>`.`<b>`.`<a>`.`<b>`.`<a>` → Summary "1 new · 1 already exists · 1 duplicate · 5 invalid".
3. Read the Review rows → rst.`<r>` shows "Exists" and the second sbt.`<s>` shows "Duplicate". The five "Invalid" lines show:
   - "An Execution key has 4 parts: exe.subject.responsibility.context"
   - "Has an empty part: two dots in a row, or a dot at the start or the end."
   - "Contains "/", which a name cannot hold."
   - "Contains a tab: cells copied from a spreadsheet? Join them with dots."
   - "Has 5 parts: a line without a type code has at most 4 (subject.responsibility.context.unit)."
4. Look at the footer → "Create 1 annotation" is disabled; "Remove invalid lines" is shown.
5. Click "Remove invalid lines" → Three lines remain; summary "1 new · 1 already exists · 1 duplicate"; "Remove invalid lines" is gone.
6. Click "Create 1 annotation" → "Created 1 annotation." with "1 key already existed and was skipped. 1 repeated line was left out."

##### ANN-BLK-04 Bulk create over a deleted key offers Restore
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationBulkCreateTests.BulkCreate_DeletedKey_OffersRestore`
- **Preconditions** Editor in the target view. The request recorder (window.__requests) is installed before the first navigation. sbt.`<s>` is bulk-created, then deleted from its detail ("More actions" › "Delete" › "Delete").

1. Open "Bulk create" and enter sbt.`<s>` → Summary "1 new"; the Review row shows "New" (deleted keys are not in the catalog).
2. Click "Create 1 annotation" → A nested dialog "Deleted annotations" lists sbt.`<s>` with "Deleted … by `<user>`". Its buttons are "Cancel", "Create new" and "Restore".
3. Click "Cancel" → The nested dialog closes; "Keys" still holds sbt.`<s>` and "Create 1 annotation" is shown. window.__requests has the POST …/annotations/deleted/lookup and no request to …/annotations/simple or …/annotations/deleted/restore.
4. Click "Create 1 annotation" again, then "Restore" → "No annotations were created." with "1 deleted annotation was restored."
5. Click "Done" and filter key:sbt.`<s>` → The row sbt.`<s>` is listed.
6. Open …/annotations/sbt.`<s>`/history → The region "History" contains "Deleted" and "Restored".

#### New Annotation (AnnotationCreateTests)

##### ANN-NEW-01 New annotation creates an execution with its missing ancestors
- **Priority** High · **Role** Editor · **Mode** B · **Test** `AnnotationCreateTests.NewAnnotation_Execution_CreatesMissingAncestors`
- **Preconditions** Editor in the target view; a fresh browser context, so the project default time zone is UTC. Generated parts `<s>`, `<r>`, `<c>` and title `<t>`.

1. Wait for the catalog and click "New" (phone: "New annotation") → Dialog "New annotation". "Type" shows "Execution", the key box shows "Fill in the names" and "Time zone" shows "UTC".
2. In "Subject" type `<s>` and pick "Create "`<s>`""; do the same for "Responsibility" (`<r>`) and "Context" (`<c>`) → Key exe.`<s>`.`<r>`.`<c>`, and "The API also creates the missing ancestors: usg.`<s>`.`<r>`, cnt.`<s>`.`<c>`, rst.`<r>`, sbt.`<s>`".
3. Fill "Title" with `<t>` and click "Create" → Toast "exe.`<s>`.`<r>`.`<c>` created"; the dialog closes.
4. Look at the detail that opened → URL …/annotations/exe.`<s>`.`<r>`.`<c>`. The identity reads the key, "Title" is `<t>`, the status badge is "Active" and "Time zone" shows "UTC".
5. Close the detail (phone: "Back to the list"), wait for the catalog and filter subject:`<s>` → Count "4 of {total}" (sbt, cnt, usg and exe).

##### ANN-NEW-02 New annotation rejects dotted names and existing keys
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationCreateTests.NewAnnotation_InvalidOrExistingKey_DisablesCreate`
- **Preconditions** Editor in the target view. sbt.`<s>` is bulk-created and the catalog is loaded. Generated parts `<a>`, `<b>`, `<c>`.

1. Open "New", choose "Subject" in "Type" and type `<a>`.`<b>` into "Name" → Error "Names cannot contain dots."; the key box shows "Fill in the names"; "Create" is disabled.
2. Change "Name" to `<s>` → Key sbt.`<s>` and "This annotation already exists."; "Create" is disabled.
3. Choose "Context" in "Type", pick the existing option `<s>` in "Subject" and type `<c>` into "Name" → Key cnt.`<s>`.`<c>`; no missing-ancestors line; "Create" is enabled.
4. Click "Cancel" → The dialog closes.
5. Filter key:cnt.`<s>` → "No annotations match the filter." (nothing was created).

##### ANN-NEW-04 New annotation over a deleted key: Cancel, Restore and update, Create new
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationCreateTests.NewAnnotation_DeletedKey_RestoresOrRecreates`
- **Preconditions** Editor in the target view. The request recorder (window.__requests) is installed before the first navigation. sbt.`<s1>` and sbt.`<s2>` are bulk-created, and sbt.`<s1>` gets the Title `<t>` (saved). Both are then deleted from their detail.

1. Open "New", choose "Subject", type `<s1>` into "Name" and click "Create" → Dialog "Deleted annotations" lists sbt.`<s1>` with `<t>` and "Deleted … by `<user>`". Its buttons are "Cancel", "Create new" and "Restore and update".
2. Click "Cancel" → The deleted-annotations dialog closes; "New annotation" stays open with the key sbt.`<s1>`. window.__requests has the POST …/annotations/deleted/lookup, no POST …/annotations/sbt.`<s1>` and no request to …/annotations/deleted/restore.
3. Click "Create", then "Restore and update" → Toast "sbt.`<s1>` restored and updated"; the detail of sbt.`<s1>` opens with "Title" `<t>`.
4. Click the link "History" → The region "History" contains "Deleted" and "Restored".
5. Leave the detail, open "New", choose "Subject", type `<s2>` into "Name", click "Create", then "Create new" → Toast "sbt.`<s2>` created"; its detail opens with an empty "Title".
6. Click "History" → The region "History" contains "Re-created", below the separator "New lifetime".

#### Filtering (AnnotationFilterTests)

##### ANN-FLT-01 Filter qualifiers, negation, OR, chips and count
- **Priority** High · **Role** Editor · **Mode** B · **Test** `AnnotationFilterTests.AnnotationFilter_Qualifiers_NarrowTheCatalog`
- **Preconditions** Editor in the target view. uxe.`<s>`.`<r>`.`<c1>`.`<u>` and exe.`<s>`.`<r>`.`<c2>` are bulk-created in one run, which makes 9 keys with their ancestors; then wait for the catalog.

1. Filter subject:`<s>` → Count "7 of {total}"; chip "subject: `<s>`".
2. Filter subject:`<s>` type:exe → Count "2 of {total}"; chips "subject: `<s>`" and "type: exe".
3. Filter subject:`<s>` -type:exe,cnt → Count "3 of {total}" (sbt, usg and uxe); chip "not type: exe or cnt".
4. Filter key:exe.`<s>`.*, then ancestor:usg.`<s>`.`<r>` → 2 rows (both executions), then 3 rows (both executions and the unit of execution).
5. Filter responsibility:`<r>`, then context:`<c1>`, then unit:`<u>` → 6, 3 and 2 rows.
6. Filter the free text `<c2>` → 2 rows: cnt.`<s>`.`<c2>` and exe.`<s>`.`<r>`.`<c2>`.
7. Filter subject:`<s>` type:exe and click "Remove type:exe" → The box holds subject:`<s>`; count "7 of {total}".
8. Click "Clear the filter" → The box is empty, the URL has no q, and the count shows only the total.

##### ANN-FLT-02 Invalid filter terms warn and are ignored; the syntax examples fill the box
- **Priority** Low · **Role** Any · **Mode** B · **Test** `AnnotationFilterTests.AnnotationFilter_InvalidTerms_WarnAndAreIgnored`
- **Preconditions** Any pool user in the target view; the catalog is loaded. Nothing is created; `<p>` is a generated word.

1. Filter type:foo → Warning "Unknown type foo." next to the chips; count "{total} of {total}" (the term filters nothing).
2. Filter is:gone → "is: accepts active, disabled, scheduled, expired."
3. Filter has:x → "has: accepts config, labels, values, docs, description."
4. Filter type: → "type: needs a value."
5. Filter zz:`<p>` → "Unknown qualifier zz: (searched as text)."; count "0 of {total}".
6. Click "Filter syntax", then the example "key:exe.*.payments.*" → The popover "Filter syntax" lists the qualifiers; the box and the URL's q hold key:exe.*.payments.*.

##### ANN-FLT-03 Filters bottom sheet on a phone
- **Priority** Medium · **Role** Any · **Mode** B,Ph · **Test** `AnnotationFilterTests.AnnotationFilter_PhoneSheet_BuildsTheQuery`
- **Preconditions** Any pool user in the target view, in the phone profile (below 640 px); nothing is created.

1. Click "Filters" → Bottom sheet "Filters" with "Narrow the annotations of this view."
2. Click "Executions", "Active" and "Has a configuration" → Each shows aria-pressed="true"; "Active filter" lists type:exe, is:active and has:config.
3. Click "Remove is:active" → is:active leaves "Active filter"; "Active" is no longer pressed.
4. Click "Done" → The sheet closes. The URL has q=type:exe has:config, the filter box holds the same text, and "Filters" shows 2.
5. Click "Filters", then "Clear all" and "Done" → The URL has no q; the box is empty.

#### Catalog (AnnotationCatalogTests)

##### ANN-CAT-01 Tree shows every path by subject and by responsibility
- **Priority** High · **Role** Editor · **Mode** B,D · **Test** `AnnotationCatalogTests.AnnotationTree_Perspectives_ShowEveryPath`
- **Preconditions** Editor in the target view, on a desktop viewport. uxe.`<s>`.`<r>`.`<c>`.`<u>` is bulk-created (with its 6 ancestors); then wait for the catalog.

1. Click the tab "Tree" → The URL has mode=tree; "Tree perspective" shows "By subject"; "Expand all" and "Collapse all" are shown.
2. Filter ancestor:sbt.`<s>` → Rows by key:
   - sbt.`<s>` once, dimmed (it does not match);
   - cnt.`<s>`.`<c>` and usg.`<s>`.`<r>` once each;
   - exe.`<s>`.`<r>`.`<c>` and uxe.`<s>`.`<r>`.`<c>`.`<u>` twice each;
   - no rst.`<r>` or unt.`<r>`.`<u>`.
3. Check the first "Select uxe.`<s>`.`<r>`.`<c>`.`<u>`" → Both copies are checked; the toolbar "Bulk actions" shows "1 selected".
4. Choose "By responsibility" in "Tree perspective" and filter ancestor:rst.`<r>` → The URL has by=responsibility. Rows:
   - rst.`<r>` once, dimmed;
   - unt.`<r>`.`<u>`, usg.`<s>`.`<r>` and exe.`<s>`.`<r>`.`<c>` once each;
   - uxe.`<s>`.`<r>`.`<c>`.`<u>` twice;
   - no sbt.`<s>` or cnt.`<s>`.`<c>`.
5. Click the tab "Grid" → The URL has no mode; the filter is kept; every key is listed once.

##### ANN-CAT-02 Bulk bar disables, enables, adds and removes a label, and deletes selected rows with Undo
- **Priority** High · **Role** Editor · **Mode** B,D · **Test** `AnnotationCatalogTests.AnnotationBulkBar_SelectedRows_ChangeDeleteAndUndo`
- **Preconditions** Editor in the target view, on a desktop viewport. sbt.`<p>`-1, sbt.`<p>`-2 and sbt.`<p>`-3 are bulk-created (shared prefix `<p>`). A label `<l>` is generated.

1. Filter key:sbt.`<p>` and check "Select sbt.`<p>`-1" and "Select sbt.`<p>`-2" → The toolbar "Bulk actions" shows "2 selected".
2. Click "Disable" → Toast "Annotations disabled"; the Status of -1 and -2 is "Disabled"; the toolbar is gone.
3. Check -1 and -2 again and click "Enable" → Toast "Annotations enabled"; their Status is "Active".
4. Check "Select all rows", click "Label", type `<l>`, pick "Create "`<l>`"" and click "Add" → "3 selected" before the action; toast "Label `<l>` added"; the Labels cell of every row shows `<l>`.
5. Check "Select sbt.`<p>`-1", click "Label", type `<l>` into "Label", pick the option `<l>` and click "Remove" → "1 selected" before the action; toast "Label `<l>` removed"; the Labels cell of sbt.`<p>`-1 no longer shows `<l>`, and those of -2 and -3 still do.
6. Check -1 and -2 and click "Delete" → Dialog "Delete 2 annotations?" with "The 2 annotations and their descendants move to Deleted, with their configuration. You can restore them from Deleted for 90 days." and the button "Delete 2".
7. Click "Delete 2" → Toast "2 annotations deleted" with "Undo"; only sbt.`<p>`-3 is listed.
8. Click "Undo" in the toast → Toast "2 annotations restored"; all three rows are listed again.

##### ANN-CAT-03 An empty view shows the empty state with create actions
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationCatalogTests.AnnotationCatalog_EmptyView_OffersCreateActions`
- **Preconditions** Editor in the target project. View name `<v>` is generated and never written: the API creates a view on its first write, so the test only reads it.

1. Open /{org}/{project}/`<v>`/annotations and wait for the catalog → Heading "No annotations in this view" and "Create the first annotations, or import them with the sform CLI."; the empty state has the buttons "New annotation" and "Bulk create"; the count reads 0.
2. Click the empty state's "Bulk create" → Dialog "Bulk create annotations" with "In {org}/{project}, view `<v>`."
3. Click "Cancel" → The dialog closes; the empty state is still shown.
4. Click the empty state's "New annotation" → Dialog "New annotation" with the key box "Fill in the names".
5. Click "Cancel" → The dialog closes; the empty state is still shown (nothing was created).

##### ANN-CAT-04 Reload the catalog shows an annotation created elsewhere
- **Priority** Low · **Role** Editor · **Mode** B · **Test** `AnnotationCatalogTests.AnnotationCatalog_Reload_ShowsAnnotationsCreatedElsewhere`
- **Preconditions** Editor in the target view; the Annotations page is open with the catalog loaded. A new key sbt.`<s>` is generated.

1. Another writer creates sbt.`<s>`: on the mock an in-page call to the API, on a live target a second page of the same browser context with Bulk create → Mock: the call answers 200 with a list of one annotation, sbt.`<s>`. Live: the second page shows "Created 1 annotation."
2. On the first page, filter key:sbt.`<s>` → "No annotations match the filter."; count "0 of {total}".
3. Click "Reload the catalog" and wait until it is enabled again → The row (phone: item) sbt.`<s>` is listed; count "1 of {total}".

##### ANN-CAT-05 Returning from another section restores the filter, detail, tab, selection and sorting
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationCatalogTests.AnnotationCatalog_ReturnFromSection_RestoresPageMemory`
- **Preconditions** Editor in the target view. sbt.`<p>`-1 and sbt.`<p>`-2 are bulk-created (shared prefix `<p>`).

1. Filter key:sbt.`<p>`. Desktop: click "Sort by Key" twice (descending) and check "Select sbt.`<p>`-1" → Desktop: sbt.`<p>`-2 is the first row and the toolbar shows "1 selected".
2. Desktop: open sbt.`<p>`-2 and click the link "History" → URL …/annotations/sbt.`<p>`-2/history?q=key:sbt.`<p>`.
3. Open "Configurations" from the navigation → URL …/configurations.
4. Open "Annotations" from the navigation → The box holds key:sbt.`<p>`. Desktop: the region "History" of sbt.`<p>`-2 is open, the URL keeps q, "1 selected" is shown and sbt.`<p>`-2 is still first.
5. Desktop: click "Annotations" in the navigation again → The plain …/annotations; the filter box is empty.

#### Detail (AnnotationDetailTests)

##### ANN-DET-01 Open an annotation from the list and close it back to the filtered list
- **Priority** High · **Role** Editor · **Mode** B · **Test** `AnnotationDetailTests.AnnotationDetail_OpenFromList_ClosesBackToTheFilter`
- **Preconditions** Editor in the target view. sbt.`<s>` is bulk-created.

1. Filter key:sbt.`<s>` and click its row (phone: its list item) → URL …/annotations/sbt.`<s>`?q=key:sbt.`<s>`. The region named `<s>` opens: an overlay on desktop (data-detail-mode "overlay"), full screen on a phone.
2. Look at the header → Heading `<s>`, status badge "Active", the key sbt.`<s>`, the button "More actions" and the links "Annotation" (current) and "History". The stamps start with "Created" and have no "Updated".
3. Look at the Annotation tab → The identity reads sbt.`<s>` with the image "Subject"; there is no textbox "Name"; "Title" is empty.
4. Click "Close the detail" (phone: "Back to the list") → URL …/annotations?q=key:sbt.`<s>`; the detail is gone; the box still holds key:sbt.`<s>` and the row is listed.

##### ANN-DET-02 Keyboard: J and K move, Enter opens, Escape closes, / focuses the filter
- **Priority** Medium · **Role** Editor · **Mode** B,D · **Test** `AnnotationDetailTests.AnnotationDetail_Keyboard_MovesOpensAndCloses`
- **Preconditions** Editor in the target view, on a desktop viewport. sbt.`<p>`-1, sbt.`<p>`-2 and sbt.`<p>`-3 are bulk-created.

1. Filter key:sbt.`<p>` and click "Sort by Key" → The rows are in the order -1, -2, -3.
2. Move the focus to the page body and press J → The live region announces "`<p>`-1, sbt.`<p>`-1".
3. Press J, then K → It announces "`<p>`-2, sbt.`<p>`-2", then "`<p>`-1, sbt.`<p>`-1".
4. Press Enter → URL …/annotations/sbt.`<p>`-1?q=key:sbt.`<p>`; the detail of sbt.`<p>`-1 is open.
5. Move the focus to the page body and press Escape → URL …/annotations?q=key:sbt.`<p>`; the detail is closed.
6. Press / → "Filter annotations" has the focus.

##### ANN-DET-03 More actions copy the link and the key, and the header links lead to the graph and the configuration
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationDetailTests.AnnotationDetail_MoreActions_CopyAndShowInGraph`
- **Preconditions** Editor in the target view. The clipboard recorder is installed before the first navigation. sbt.`<s>` is bulk-created.

1. Open …/annotations/sbt.`<s>` and click "More actions" → Menu items "Show in graph", "Copy link", "Copy key" and "Delete".
2. Click "Copy link" → Toast "Link copied"; the copied text is {origin}/{org}/{project}/{view}/annotations/sbt.`<s>`.
3. Click "More actions" › "Copy key", then the key's "Copy to clipboard" button → Both copy sbt.`<s>`; "Copy key" shows no toast.
4. Look at the configuration link → "No configuration" (data-origin none), or "Inherited configuration" (data-origin inherited) on a project whose type templates apply.
5. Click the configuration link → URL …/configurations/sbt.`<s>`.
6. Go back to the detail and click "More actions" › "Show in graph" → URL …/graph/sbt.`<s>`?focus=sbt.`<s>`.

##### ANN-DET-04 Breadcrumb, Lineage and Relations lead through ancestors and descendants
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationDetailTests.AnnotationDetail_Lineage_LinksAncestorsAndDescendants`
- **Preconditions** Editor in the target view. One bulk create of uxe.`<s>`.`<r>`.`<c1>`.`<u>` and exe.`<s>`.`<r>`.`<c2>` … exe.`<s>`.`<r>`.`<c6>`; then wait for the catalog.

1. Open …/annotations/uxe.`<s>`.`<r>`.`<c1>`.`<u>` → The nav "Annotation ancestors" reads "Subject `<s>`" › "Context `<c1>`" › "Execution `<c1>`" › "Unit of execution `<u>`"; the last item is the current page, not a link.
2. Open the same URL with ?by=responsibility → "Responsibility `<r>`" › "Usage `<r>`" › "Execution `<c1>`" › "Unit of execution `<u>`".
3. Click "Lineage" → "Ancestors, applied top to bottom" with four levels:
   1. sbt.`<s>`
   2. rst.`<r>`, usg.`<s>`.`<r>`, cnt.`<s>`.`<c1>`
   3. unt.`<r>`.`<u>`, exe.`<s>`.`<r>`.`<c1>`
   4. the key itself
4. Look at "Relations" → "Ancestors" links Responsibility rst.`<r>`, Subject sbt.`<s>`, Unit unt.`<r>`.`<u>`, Usage usg.`<s>`.`<r>`, Context cnt.`<s>`.`<c1>` and Execution exe.`<s>`.`<r>`.`<c1>`; there is no "Descendants".
5. Click the Relations link sbt.`<s>` → The detail of sbt.`<s>`; "Lineage" is disabled.
6. Look at "Descendants" → The groups, in order, are Usage 1, Context 6, Execution 6 and Unit of execution 1, with "All descendants (14)". The Execution group shows 5 key links and "Show all 6".
7. Click "Show all 6" in the Execution group → URL …/annotations?q=ancestor:sbt.`<s>` type:exe; the box holds that; count "6 of {total}".

##### ANN-DET-05 History lists the creation, the update and the ancestor cause
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationDetailTests.AnnotationHistory_CreateAndUpdate_ListsEntries`
- **Preconditions** Editor in the target view. exe.`<s>`.`<r>`.`<c>` is bulk-created; title `<t>` is generated.

1. Open …/annotations/rst.`<r>`/history → The region "History" shows "Created", "by `<user>`" and "for exe.`<s>`.`<r>`.`<c>`".
2. Open …/annotations/exe.`<s>`.`<r>`.`<c>`, fill "Title" with `<t>` and click "Save" → Toast "Annotation saved"; the stamps read "Created … · Updated … by `<user>`".
3. Click the stamps → URL …/exe.`<s>`.`<r>`.`<c>`/history; the link "History" is current.
4. Read the first (newest) entry → "Updated" by `<user>`, with "Title" and "`<t>`".
5. Read the last entry → "Created" by `<user>`, without a cause.
6. Click the link "Annotation" → The form is shown again with "Title" `<t>`; the URL has no /history.

##### ANN-DET-06 Old annotation tab URLs open the configuration tabs
- **Priority** Low · **Role** Any · **Mode** B · **Test** `AnnotationDetailTests.AnnotationDetail_OldTabUrls_Redirect`
- **Preconditions** Any pool user in the target view. A subject key sbt.`<s>` is generated and never created; nothing is written.

1. Open …/annotations/sbt.`<s>`/configuration → URL …/configurations/sbt.`<s>` without a query; the tab link "Edit" is current.
2. Open …/annotations/sbt.`<s>`/configuration?cfg=effective&q=type:exe → URL …/configurations/sbt.`<s>`/effective?q=type:exe; the tab link "Effective" is current.
3. Open …/annotations/sbt.`<s>`/configuration?cfg=resolved → URL …/configurations/sbt.`<s>`/resolved; the tab link "Resolved" is current.
4. Open …/annotations/sbt.`<s>`/schema → URL …/configurations/sbt.`<s>`/schema; the tab link "Schema" is current.
5. Open …/annotations/sbt.`<s>`/versions?v=2 → URL …/configurations/sbt.`<s>`/versions?v=2; the tab link "Versions" is current.

#### Annotation Form (AnnotationFormTests)

##### ANN-FRM-01 Title and Markdown description save, and the preview is safe
- **Priority** High · **Role** Editor · **Mode** B · **Test** `AnnotationFormTests.AnnotationForm_TitleAndMarkdown_SaveAndPreviewSafely`
- **Preconditions** Editor in the target view. sbt.`<s>` is bulk-created; title `<t>` is generated.

1. Open …/annotations/sbt.`<s>` and click the tab "Preview" of "Description" → "Nothing to preview."
2. Click "Write", fill "Title" with `<t>` and "Description" with "## Endpoints", a list "- api" "- web" and the line `<b>bold</b> [x](javascript:alert(1)) [ok](https://example.com)` → "Unsaved changes" is shown; the document title starts with "● ".
3. Click "Preview" → The preview shows:
   - the heading "Endpoints" and the list items "api" and "web";
   - the text `<b>bold</b>` literally, with no bold element;
   - "x" not as a link;
   - "ok" as a link that opens in a new tab.
4. Press Ctrl+S → Toast "Annotation saved"; "Unsaved changes" is gone; the document title has no "● ".
5. Reload the detail URL → "Title" is `<t>` and the description holds the text; the stamps read "· Updated … by `<user>`".

##### ANN-FRM-02 Field limits show their messages and block Save
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationFormTests.AnnotationForm_FieldLimits_BlockSave`
- **Preconditions** Editor in the target view. sbt.`<s>` is bulk-created.

1. Open …/annotations/sbt.`<s>` and fill "Title" with 201 characters → "At most 200 characters."
2. Click "Save" → No toast "Annotation saved"; the message stays.
3. Shorten "Title" to 200 characters and fill "Documentation link" with example.com/`<s>` → The Title message is gone; "Use an absolute http(s) URL."
4. Change "Documentation link" to https://example.com/`<s>` → The message is gone; the button "Open the link" is shown.
5. Fill "Description" with 10,001 characters → The hint "10,001 / 10,000" and "At most 10,000 characters."
6. Shorten "Description" to 9,000 characters and click "Save" → Toast "Annotation saved"; no hint and no message.
7. Reload the detail URL → "Title" (200 characters) and "Documentation link" hold the saved values.

##### ANN-FRM-03 Disabled switch and validity dates drive the status
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationFormTests.AnnotationForm_DisabledAndDates_DriveStatus`
- **Preconditions** Editor in the target view. sbt.`<s>` is bulk-created.

1. Open …/annotations/sbt.`<s>`, turn on "Disabled" and click "Save" → Toast "Annotation saved"; the header badge reads "Disabled".
2. Click the radio "JSON", set "IsDisabled" to false and "ValidFrom" to 10 days from now, and save → The badge reads "Scheduled".
3. In JSON, set "ValidFrom" to null and "ExpiresAt" to one day ago, and save → The badge reads "Expired".
4. In JSON, set "ValidFrom" to a time later than "ExpiresAt" and click "Save" → The alert "ExpiresAt Expires must be after Valid from."; no toast; the badge stays "Expired".
5. Click "Revert", then the radio "Form", then "Clear" in the "Expires" field, and click "Save" → "Expires date" is empty; the badge reads "Active".
6. Close the detail (phone: "Back to the list") and filter key:sbt.`<s>` is:active, then key:sbt.`<s>` is:expired → The row is listed, then "No annotations match the filter."

##### ANN-FRM-04 JSON and YAML modes edit the annotation and guard the key fields
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationFormTests.AnnotationForm_JsonAndYaml_EditAndGuardKeyFields`
- **Preconditions** Editor in the target view. sbt.`<s>` is bulk-created; title `<t>` is generated.

1. Open …/annotations/sbt.`<s>` and click the radio "JSON" → The URL has format=json; the textbox "Annotation document" contains "AnnotationKey": "sbt.`<s>`".
2. Set "Title" to `<t>` in the text and click "Save" → Toast "Annotation saved".
3. Change "AnnotationKey" in the text → The alert "AnnotationKey is part of the key and cannot change."; "Save" is disabled.
4. Click the radio "Form" → Toast "Fix the syntax error first"; the mode stays JSON.
5. Click "Revert", then the radio "YAML" → The URL has format=yaml; the text contains "Title: " and `<t>`.
6. Press Ctrl+Shift+F → Mode "Form"; the URL has no format; "Title" is `<t>`.

##### ANN-FRM-05 Labels: create one, reuse it on a second annotation and filter by it
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationFormTests.AnnotationForm_Labels_CreateReuseAndFilter`
- **Preconditions** Editor in the target view. sbt.`<s1>` and sbt.`<s2>` are bulk-created; label `<l>` is generated.

1. Open sbt.`<s1>`, type `<l>` into "Labels", pick "Create "`<l>`"" and click "Save" → Toast "Annotation saved"; the tag `<l>` is shown.
2. Leave the detail and filter label:`<l>` → Count "1 of {total}"; sbt.`<s1>` is listed (desktop: its Labels cell shows `<l>`).
3. Open sbt.`<s2>` and type `<l>` into "Labels" → The option `<l>` shows its count 1; there is no Create option.
4. Pick `<l>` and click "Save" → Toast "Annotation saved".
5. Leave the detail and filter label:`<l>` → Count "2 of {total}".

##### ANN-FRM-06 Values editor: typed rows, duplicate keys and Edit all as JSON
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationFormTests.AnnotationForm_Values_TypedRowsAndDuplicates`
- **Preconditions** Editor in the target view. sbt.`<s>` is bulk-created.

1. Open …/annotations/sbt.`<s>` → "Values" shows "No values."
2. Click "Add value"; set "Value key" to region and "Value" to eu → One row: region, type string, value eu.
3. Click "Add value"; set "Value key" to retries, "Value type" to number and "Value" to 3 → A second row: retries, type number, value 3.
4. Click "Add value" and set its "Value key" to retries → "Duplicate keys: retries".
5. Click the last "Remove retries", then "Save" → The duplicate message is gone; toast "Annotation saved".
6. Reload the detail URL and turn on "Edit all as JSON/YAML" → The editor "Values" contains "region": "eu" and "retries": 3.
7. Leave the detail and filter key:sbt.`<s>` has:values → The row is listed.

##### ANN-FRM-07 Unit fields: unit type, definition and the Cron hint
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationFormTests.AnnotationForm_UnitFields_TypeDefinitionAndCronHint`
- **Preconditions** Editor in the target view. unt.`<r>`.`<u>` is bulk-created, which also creates rst.`<r>`.

1. Open …/annotations/unt.`<r>`.`<u>` → The "Unit" fields are shown; "Unit type" holds event and "Unit definition" holds unknown.
2. Pick "Time" in "Unit type" → The help texts "A job that runs on a schedule." and "A Cron expression. The annotation time zone controls when it runs."
3. Set "Unit definition" to */15 * * → The hint "A Cron expression usually has 5 or 6 fields, for example */15 * * * *."
4. Clear "Unit definition" → "Enter a unit definition."
5. Set "Unit definition" to 0 2 * * * and click "Save" → Toast "Annotation saved"; no hint.
6. Click the radio "JSON" → The text contains "UnitType": "time" and "UnitDefinition": "0 2 * * *".
7. Open …/annotations/rst.`<r>` → No "Unit" fields.

##### ANN-FRM-08 Unsaved changes: Revert, Keep editing and Discard
- **Priority** High · **Role** Editor · **Mode** B · **Test** `AnnotationFormTests.AnnotationForm_UnsavedChanges_RevertKeepOrDiscard`
- **Preconditions** Editor in the target view. sbt.`<p>`-1 and sbt.`<p>`-2 are bulk-created; title `<t>` is generated; the list is filtered by key:sbt.`<p>`.

1. Open sbt.`<p>`-1 from the list and fill "Title" with `<t>` → "Unsaved changes"; "Revert" is enabled.
2. Click "Revert" → "Title" is empty again; "Unsaved changes" is gone.
3. Fill "Title" with `<t>` again and click "Close the detail" (phone: "Back to the list") → Dialog "Discard changes?" with "Your changes have not been saved."
4. Click "Keep editing" → The dialog closes; the detail still shows "Title" `<t>`.
5. Desktop: click the row sbt.`<p>`-2. Phone: click "Back to the list" again → "Discard changes?" again.
6. Click "Discard" → Desktop: the detail of sbt.`<p>`-2. Phone: the list.
7. Open sbt.`<p>`-1 → "Title" is empty (nothing was saved).

#### Delete and Restore (AnnotationDeleteTests)

##### ANN-DEL-01 Delete from the detail with descendants, then Undo
- **Priority** High · **Role** Editor · **Mode** B · **Test** `AnnotationDeleteTests.AnnotationDelete_WithDescendants_UndoRestores`
- **Preconditions** Editor in the target view. exe.`<s>`.`<r>`.`<c>` is bulk-created, so rst.`<r>` has 2 descendants (usg.`<s>`.`<r>` and the exe); then wait for the catalog.

1. Open …/annotations/rst.`<r>` and click "More actions" › "Delete" → Dialog "Delete rst.`<r>`?" with "rst.`<r>` and its 2 descendants move to Deleted, with their configuration. You can restore them from Deleted for 90 days." and the buttons "Cancel" and "Delete".
2. Click "Delete" → Toast "rst.`<r>` deleted" with "With 2 descendants. Restorable from Deleted until <yyyy-mm-dd>." and "Undo"; the list is shown.
3. Filter responsibility:`<r>` → "No annotations match the filter."
4. Click "Undo" in the toast → Toast "rst.`<r>` restored"; URL …/annotations/rst.`<r>`.
5. Leave the detail and filter responsibility:`<r>` → rst.`<r>`, usg.`<s>`.`<r>` and exe.`<s>`.`<r>`.`<c>` are listed again.
6. Open …/annotations/rst.`<r>`/history → The region "History" contains "Deleted" and "Restored".

##### ANN-DEL-02 A deleted key's detail shows the banner and restores with or without descendants
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `AnnotationDeleteTests.AnnotationDelete_DeletedDetail_RestoresFromBanner`
- **Preconditions** Editor in the target view. cnt.`<s>`.`<c>` is bulk-created, which also creates sbt.`<s>`. sbt.`<s>` is then deleted from its detail.

1. Open …/annotations/sbt.`<s>` → The detail shows:
   - the badge "Deleted";
   - the banner "sbt.`<s>` is deleted" with "Deleted on <yyyy-mm-dd> by `<user>` with 1 descendant. Purged automatically on <yyyy-mm-dd>.";
   - no "Annotation" or "History" link;
   - no "Delete" under "More actions".
2. Open …/annotations/cnt.`<s>`.`<c>` → Banner "cnt.`<s>`.`<c>` is deleted" with "… together with sbt.`<s>`."
3. Open sbt.`<s>` and click "More restore options" › "Restore without descendants" → Toast "sbt.`<s>` restored"; the form with the identity sbt.`<s>` is shown.
4. Open cnt.`<s>`.`<c>` → It is still deleted; the banner no longer says "together with".
5. Click "Restore" → Toast "cnt.`<s>`.`<c>` restored"; the form with the identity cnt.`<s>`.`<c>` is shown.
6. Open …/annotations/sbt.`<s>`-missing → "Annotation not found" and "sbt.`<s>`-missing does not exist in this view. It may have been deleted permanently."

#### Role Access (AnnotationRoleTests)

##### ANN-ROL-01 A Reader gets no create actions, not even in an empty view
- **Priority** High · **Role** Reader · **Mode** B · **Test** `AnnotationRoleTests.AnnotationAccess_Reader_HasNoCreateActions`
- **Preconditions** A pool user who is exactly Reader in the project of Target:RoleProjects Reader (mock: northwind/portal/default). The test needs no annotation there and creates nothing. View name `<v>` is generated and never written.

1. Open the Annotations page and wait for the catalog → The box "Filter annotations" is shown; there is no "New", "New annotation" or "Bulk create" button, neither in the navbar nor in an empty state.
2. Open /{org}/{project}/`<v>`/annotations and wait for the catalog → Heading "No annotations in this view" with "Create the first annotations, or import them with the sform CLI."; no buttons "New annotation" and "Bulk create"; the count reads 0.

##### ANN-ROL-02 Exactly a Contributor can create, edit and delete
- **Priority** Medium · **Role** Contributor · **Mode** B · **Test** `AnnotationRoleTests.AnnotationAccess_Contributor_CreatesEditsDeletes`
- **Preconditions** A pool user who is exactly Contributor in the project of Target:RoleProjects Contributor (mock: acme/shipping/default). sbt.`<s>` and title `<t>` are generated.

1. Open the Annotations page and wait for the catalog → "Bulk create" and "New" are shown (phone: "New annotation").
2. Bulk create sbt.`<s>` → "Created 1 annotation."
3. Filter key:sbt.`<s>`. Desktop: check "Select sbt.`<s>`", then click "Clear selection" → The row (phone: item) sbt.`<s>` is listed. Desktop: the toolbar "Bulk actions" shows "1 selected", and after "Clear selection" it is gone.
4. Open sbt.`<s>`, fill "Title" with `<t>` and click "Save" → No "Read-only" badge; toast "Annotation saved".
5. Click "More actions" › "Delete" › "Delete" → Toast "sbt.`<s>` deleted"; the list no longer shows sbt.`<s>`.

##### ANN-ROL-03 A Reader sees an existing annotation read-only, without a bulk bar
- **Priority** Medium · **Role** Reader · **Mode** M · **Test** `AnnotationRoleTests.AnnotationAccess_Reader_SeesDetailReadOnly`
- **Preconditions** Mock only, default dataset. Ada Lovelace is exactly Reader of northwind/portal (the Reader project of Target:RoleProjects on the mock, with no organization role); its default view holds the generated seed annotations of three subjects. The test opens the first annotation listed, whatever its key, and creates nothing.

1. Open the Annotations page of northwind/portal/default and wait for the catalog → The grid "Annotations" (phone: the list "Annotations") lists annotations; "No annotations in this view" is not shown.
2. Desktop: check the first row's "Select …" → The checkbox is checked; there is no toolbar "Bulk actions".
3. Open the first annotation of the list → Badge "Read-only"; "Title" and "Labels" are disabled; no "Save", "Revert" or "Add value".
4. Click the radio "JSON" → The URL has format=json; the textbox "Annotation document" is not editable; the badge "Read-only" stays.
5. Click "More actions" → The items "Show in graph", "Copy link" and "Copy key"; no "Delete".

#### Not automated
- Detail panel frame (pin, resize, full screen and the remembered detail mode): owned by the Navigation section (NAV-DET-01 to NAV-DET-03).
- Personal type names, colors and icons (Preferences) and the project's type appearance shown on this page: owned by the Settings section (SET-BRW-03, SET-BRW-04 and SET-PRF-03).
- The New annotation dialog starting in the project's default time zone set on the Settings page (the former ANN-NEW-03): covered by SET-BRW-01.
- The Modeling › Deleted list (the list, restoring from it, deleting permanently): owned by the Modeling section (MOD-DEL-01 to MOD-DEL-04).
- The old /{org}/{project}/{view}/deleted URL: owned by Modeling (MOD-TAB-02), which checks every redirect of the merged Modeling pages.
- The detail opened from the Graph ("Show in list", the graph query), the graph's empty state, and the old graph/`<key>`/* tab URLs: owned by the Graph section (GRA-DET-01, GRA-DET-02, GRA-DET-04 and GRA-CRE-02).
- Searching annotation keys in the command palette, and its Recent list: owned by Navigation (NAV-PAL-02); the palette is only a means here.
- The command palette's create actions per role (a Reader is offered neither "New annotation" nor "Bulk create annotations"): owned by Navigation (NAV-MNU-05), which checks the menu and the palette per role. ANN-ROL-01 checks the page.
- Concurrent edits of one annotation (412 conflicts): the annotation API and the UI have no ETag or If-Match precondition. The last write wins and there is no conflict to show. ANN-CAT-04 covers another writer's create.
- Catalog load failure ("The catalog could not be loaded" with "Retry") and every error toast or alert: "Delete failed", "Restore failed", "Undo failed", "The bulk action failed", "{n} annotations could not be deleted", "{n} annotations were not restored", "The annotation was not saved", and a failed bulk-create batch ("Try again", "At least N annotations were created before the error…"). They need fault injection: the mock has no failure hooks for annotations, and Playwright routing does not see requests that the MSW service worker answers. Mid-run failures on a live target would leave partial data behind.
- Server-side 400 InvalidAnnotation errors mapped onto the fields: the client schema enforces the same limits (200, 10,000 and 2048) in Form, JSON and YAML, so the UI never sends such a request.
- "(missing)" rows in the tree and struck-through ancestors in the breadcrumb and Lineage: the UI and the API always create missing ancestors and delete descendants together with their ancestor, so a test cannot make an orphan. Only the mock seed has one.
- Tree expand and collapse without a filter ("Expand all", "Collapse all", row toggles): the roots are virtualized and sorted by key. On a live view crowded with other runs' subjects, the test's root may lie outside the rendered window. With a filter the tree is always expanded, which ANN-CAT-01 covers.
- Columns menu, column resize, density and pinned-column visuals: browser-local display preferences and pointer-precision behaviour, which are cosmetic. The Valid from and Expires values are checked in the form instead.
- Scroll-position memory behind a covering detail and after another section: it needs a list longer than a screen of own keys and pixel-tolerance reads of a virtualized scroller, which are timing-sensitive. ANN-CAT-05 covers the filter, detail, tab, selection and sorting memory.
- Typing dates into the segmented date and time fields (year, month and day spinbuttons, the "Your time: …" tooltip): keyboard entry differs between engines and date formats. The tests set dates in JSON mode and use "Clear".
- The native beforeunload prompt when a tab with unsaved changes closes: it is a browser-native dialog that needs user activation and differs per engine and on BrowserStack. The in-app "Discard changes?" guard is covered.
- Bulk-create runs of more than 100 keys (batches of 100, "Sent N of M keys…", 1,000 keys): each run would leave hundreds of annotations on a live target, and the progress is transient. The 1,000-key limit is checked without creating.
- Moving to another view while a bulk create runs ("You moved to another view…"): it is a race. The mock answers instantly, and on a live target the timing cannot be controlled.
- Transient states ("Loaded N annotations…", "Refreshing…", "From the catalog", "Counting descendants…", "The catalog is still loading…"): they last milliseconds on the mock and vary on a live target. The final states are asserted instead.
- History trimming ("Older changes were removed to keep the history small."): it needs a server-side retention threshold of many changes, and the mock never trims.
- The automatic purge after 90 days, and the exact "Restorable from Deleted until" and "Purged automatically on" dates: they depend on time. Only the date pattern is asserted.
- Role boundaries above Contributor (exactly Administrator or ContributorWithSecrets): nothing in this section depends on them, and the mock has no project where the user holds those exact roles.
- Performance with very large catalogs (the mock's large scenario, 20k annotations): this is not a functional check, and the dataset exists only on the mock.

### 5. Configurations

The Configurations section covers the Configurations page of a view (the grid from 768 px and the list on a phone, the shared annotation filter, New configuration) and the configuration detail with its five tabs: Edit (Form generated from the schema, JSON or YAML), Effective (the merged document and its Composition), Resolved (the server-evaluated bindings behind Reveal), Versions (timeline and diffs) and Schema (the applied schemas). It owns how a configuration is created (Create empty, Start from template, New configuration), edited, saved (change counter, Revert, the unsaved-changes guard), deleted (Delete own with the typed key), how it reacts to a schema (form, errors, Save anyway), to a concurrent writer (the conflict dialog) and to its ancestors (inheritance, Override, Composition), the binding assist, and the role gates of Contributors and Readers. Schemas, templates and transfers belong to Modeling: this section only creates an annotation schema of its own annotation, or a type template inside a disposable project, as setup. The old detail-tab links that redirect to a configuration (`…/annotations/<key>/configuration`, `…/graph/<key>/configuration` and their tab stubs) belong to Annotations (ANN-DET-06) and Graph (GRA-DET-04). The tests create their own annotations (Bulk create, keys carrying the run id) and configurations in the target view; only CFG-CRE-03 works in a disposable project, because a type template changes every configuration of its type in the view: it creates the Responsibility template there, never in Target:View. Sub-folders and classes, all in `Tests/Configurations` with `[Area(Area.Configurations)]`: Create and Delete (`ConfigurationCreateTests`), Edit and Versions (`ConfigurationEditTests`), Save Conflicts (`ConfigurationConflictTests`), Schema and Form (`ConfigurationSchemaFormTests`), Inheritance (`ConfigurationInheritanceTests`), Bindings and Resolved (`ConfigurationBindingTests`), List and Navigation (`ConfigurationNavigationTests`) and Roles (`ConfigurationRoleTests`). The Elementary case is the existing Testiny case 3, CFG-CRE-01 `ConfigurationCreateTests.Configurations_SetDocument_EffectiveShowsIt`, which moves out of `ConfigurationsTests`; that class is deleted, so no test exists twice.

#### Create and Delete (ConfigurationCreateTests)

##### CFG-CRE-01 Create empty, save a JSON document, and the Effective tab shows it
- **Priority** Elementary · **Role** Editor · **Mode** B · **Test** `ConfigurationCreateTests.Configurations_SetDocument_EffectiveShowsIt` · existing Testiny case 3
- **Preconditions** An editing pool user (Contributor and up) in the target view. The test Bulk creates a new responsibility `rst.<runSlug>-…` on Annotations (no ancestors, usually no type schema or template) and generates a document with `probe`, `count` and `enabled`.
1. Open `…/configurations/<key>` → The Edit tab shows the heading "No configuration yet" with the buttons "Create empty" and "Start from template".
2. Click "Create empty" → Toast "Configuration created"; the radio "JSON", the textbox "Own configuration" showing `{}` and the badge "v1" appear.
3. Replace the editor text with the generated document → The footer shows "3 changes"; "Save" is enabled.
4. Press Ctrl/Cmd+S in the editor → Toast "Configuration saved (v2)"; the badge reads "v2"; no change counter.
5. Click the tab link "Effective" → The URL ends with `/effective`; the textbox "Effective configuration" contains each `"name": value` of the document.

##### CFG-CRE-02 New configuration offers only annotations without their own configuration
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationCreateTests.Configurations_NewDialog_OffersOnlyKeysWithoutConfiguration`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a new responsibility `<key>` (it has no configuration).
1. Open `…/configurations?q=key:<key>` → Heading "No configuration matches the filter" with "Create one with New, or from the configuration link in the header of an annotation."; two buttons "New configuration" (the navbar's and the empty state's).
2. Click the empty state's "New configuration" → Dialog "New configuration" with "In <org>/<project>, view <view>."; "Open editor" is disabled.
3. Click the button "Annotation" and type the key into "Search keys…" → An option named exactly `<key>` is listed.
4. Click the option → The trigger shows the key; "Open editor" is enabled and focused.
5. Press Enter → The dialog closes; the URL is `…/configurations/<key>?q=key:<key>`; heading "No configuration yet".
6. Click "Create empty" → Toast "Configuration created"; badge "v1".
7. Close the detail ("Close the detail"; phone: "Back to the list") → The URL is `…/configurations?q=key:<key>`; the key's row (phone: list item) shows "v1".
8. Click the navbar "New configuration", click "Annotation" and search the key again → No option named `<key>`; "Cancel" closes the dialog.

##### CFG-CRE-03 Start from template copies the type template, and Create empty still inherits it
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `ConfigurationCreateTests.Configurations_StartFromTemplate_CopiesTypeTemplate`
- **Preconditions** An Owner of the target organization. The test creates a disposable project `at-<runId>-prj-…` (view `default`) and creates the Responsibility template and both annotations only inside it, never in Target:View (a type template changes every Responsibility configuration of its view). It generates a template document (`enabled`, `retries`, `mode`, `owner` = the run slug) and two new responsibility keys A and B.
1. Create the project on the organization overview → Toast "Project <project> created"; the URL is `…/<project>/default/annotations`.
2. Open `…/<project>/default/modeling/templates/rst`, click "Create template" on the empty state "No responsibility template", replace the textbox "Template" with the template document and click "Create" → Toast "Template saved (v1)".
3. Bulk create A and B on the project's Annotations → The dialog reads "Created 2 annotations.".
4. Open A's configuration → Heading "No configuration yet" with the buttons "Create empty" and "Start from template".
5. Click "Start from template" → Toast "Configuration created"; badge "v1".
6. Click the radio "JSON" → The textbox "Own configuration" contains each `"name": value` of the template (e.g. `"owner": "<runSlug>"`).
7. Open B's configuration and click "Create empty" → Toast "Configuration created"; "Own configuration" shows `{}`.
8. Click B's tab link "Effective" → The textbox "Effective configuration" contains each `"name": value` of the template, although B's own document is empty.

##### CFG-CRE-04 Delete own needs the typed key and returns to the empty state
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationCreateTests.Configurations_DeleteOwn_NeedsTypedKey`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, opens `…/configurations/<key>?q=key:<key>`, clicks "Create empty" and saves a generated document (v2).
1. Click "Delete own" → Dialog "Delete the own configuration of <key>?" with "Only the own content is deleted; values inherited from ancestors and the template remain."; "Delete" is disabled.
2. Click "Cancel" → The dialog closes; the editor and the badge "v2" are still shown.
3. Click "Delete own" again and type `x` into "Type <key> to confirm" → "Delete" stays disabled.
4. Fill the field with the key → "Delete" is enabled.
5. Click "Delete" → Toast "Own configuration deleted; inherited values remain"; heading "No configuration yet" with "Create empty".
6. Close the detail ("Close the detail"; phone: "Back to the list") → The URL is `…/configurations?q=key:<key>`; heading "No configuration matches the filter".

#### Edit and Versions (ConfigurationEditTests)

##### CFG-EDT-01 Unsaved changes ask before leaving, and Revert drops them
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationEditTests.Configurations_UnsavedChanges_AskBeforeLeaving`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, clicks "Create empty" and saves `{"count": 1}` (v2).
1. Replace the editor text with `{"count": 2, "extra": true}` → Footer "2 changes"; "Revert" and "Save" are enabled; the page title starts with "● ".
2. Click the tab link "Effective" → Dialog "Discard changes?" with "Your changes have not been saved."; the URL is still the Edit tab's.
3. Click "Keep editing" → The dialog closes; the editor still contains `"extra": true`.
4. Click the radio "YAML" → No dialog (a query-only change); the URL has `format=yaml`; the editor shows `extra: true`; still "2 changes".
5. Click "Revert" → The editor shows `count: 1` and no `extra`; no change counter; "Revert" and "Save" are disabled; the title has no "● ".
6. Replace the text with `count: 3`, click the link "Annotation", then "Discard" → The URL is `…/annotations/<key>` without `q`; the title has no "● ".

##### CFG-EDT-02 A syntax error disables Save and blocks the format switch
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationEditTests.Configurations_SyntaxError_BlocksSaveAndFormatSwitch`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility and clicks "Create empty" (v1).
1. Click the radio "JSON" and replace the editor text with `{"count": }` → Alert "Line 1: Value expected" under the editor; "Save" is disabled.
2. Click the radio "YAML" → Toast "Fix the syntax error first"; the radio "JSON" stays checked; the URL keeps `format=json`.
3. Select all and press Delete → Alert "Line 1: The document is empty."; "Save" is disabled.
4. Insert `{"big": 12345678901234567890}` → The error is gone; warning "A number is larger than 2^53 and loses precision; store it as a string."; footer "1 change"; "Save" is enabled.

##### CFG-EDT-03 YAML mode edits and saves the document, and the format stays in the URL
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationEditTests.Configurations_YamlMode_SavesAndKeepsFormat`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, clicks "Create empty" and saves `{"count": 1}` (v2).
1. Click the radio "YAML" → The URL has `format=yaml`; the textbox "Own configuration" contains `count: 1`.
2. Replace the text with the two lines `count: 2` and `label: from-yaml` → Footer "2 changes".
3. Press Ctrl/Cmd+S → Toast "Configuration saved (v3)".
4. Click the radio "JSON" → The editor contains `"count": 2` and `"label": "from-yaml"`; the URL has `format=json`.
5. Load `…/configurations/<key>?format=yaml` again → The radio "YAML" is checked; the editor shows `count: 2`.

##### CFG-EDT-04 Versions lists every save and compares two versions
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationEditTests.Configurations_Versions_CompareShowsDiff`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility and stores v1 `{}` (Create empty), v2 `{"count": 1}` and v3 `{"count": 2}`.
1. Click the tab link "Versions" → The timeline lists, newest first, v3 with the badge "current", v2 and v1; each has "View"; v3 and v2 have "Compare with previous", v1 has none.
2. Click "Compare with previous" on v3 → The URL has `v=3&from=2`; text "v2 → v3"; the diff stats show "+1" and "−1"; from 1024 px the sides are labelled "v2" and "v3".
3. Click "Back to history" → The URL has neither `v` nor `from`; only the pickers and the timeline are shown.
4. Click "View" on v1 → The URL has `v=1`; text "Version v1"; the textbox "Version 1" contains `{}`.
5. Pick "v1" in "Compare" (the "with" picker already shows "v1") → The button "Compare" is disabled.
6. Pick "v3" in "with" and click "Compare" → The URL has `v=3&from=1`; text "v1 → v3".

#### Save Conflicts (ConfigurationConflictTests)

##### CFG-CON-01 A concurrent change of the same value opens the conflict dialog, and Overwrite saves mine
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationConflictTests.Configurations_ConcurrentChange_OverwriteSavesMine`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, clicks "Create empty" and saves `{"count": 1}` (v2).
1. Replace the editor text with `{"count": 2}` without saving → Footer "1 change".
2. Let another writer save `count` 7 (ConcurrentEdit) → This editor still shows `"count": 2` with the footer "1 change"; on a live target the writer's page shows the toast "Configuration saved (v3)".
3. Press Ctrl/Cmd+S → Dialog "Someone else changed this configuration" with "The stored configuration changed after you opened it. Compare their version (left) with yours (right)."; from 1024 px the sides are labelled "Theirs (stored)" and "Yours".
4. Click "Overwrite" → The dialog closes; toast "Configuration saved (v4)".
5. Click the tab link "Effective" → The document contains `"count": 2` and not `"count": 7`.

##### CFG-CON-02 Reload, discard mine takes the stored version, and Copy mine copies mine
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationConflictTests.Configurations_ConcurrentChange_ReloadTakesTheirs`
- **Preconditions** An editing pool user in the target view; the clipboard recorder is installed before the first navigation. The test Bulk creates a responsibility, clicks "Create empty" and saves `{"count": 1}` (v2).
1. Replace the text with `{"count": 2}`, let another writer save `count` 7 (ConcurrentEdit) and press Ctrl/Cmd+S → Dialog "Someone else changed this configuration".
2. Click "Copy mine" → The button reads "Copied"; the recorded clipboard text contains `"count": 2`.
3. Click "Reload, discard mine" → The dialog closes; the editor contains `"count": 7`; no change counter; "Save" is disabled.
4. Look at the version badge → It reads "v3".

##### CFG-CON-03 A concurrent change of another value merges without a conflict
- **Priority** Low · **Role** Editor · **Mode** B · **Test** `ConfigurationConflictTests.Configurations_ConcurrentChangeElsewhere_MergesWithoutConflict`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, clicks "Create empty" and saves `{"count": 1}` (v2).
1. Replace the editor text with `{"count": 2}` → Footer "1 change".
2. Let another writer add `"note": "theirs"` (ConcurrentEdit) → This editor still shows `"count": 2` without `"note"`, with the footer "1 change"; on a live target the writer's page shows the toast "Configuration saved (v3)".
3. Press Ctrl/Cmd+S → No conflict dialog; toast "Configuration saved (v4)".
4. Click the tab link "Effective" → The document contains `"count": 2` and `"note": "theirs"`.

#### Schema and Form (ConfigurationSchemaFormTests)

##### CFG-SCH-01 The form follows the annotation's schema, and the Schema tab lists it
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationSchemaFormTests.Configurations_SchemaForm_SavesValue`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility and creates its annotation schema in Modeling (`…/modeling/schemas/annotation/<key>`, toast "Schema saved (v1)"): `{"type":"object","properties":{"retries":{"type":"integer","minimum":0,"maximum":10,"title":"Retries"},"enabled":{"type":"boolean","title":"Enabled"}},"required":["retries"]}`.
1. Open the key's configuration and click "Create empty" → Toast "Configuration created"; the radiogroup "Editing mode" has "Form" (checked), "JSON" and "YAML"; the fields "Retries *" and "Enabled"; footer "The form has errors.".
2. Enter 4 in "Retries" and leave the field → Footer "1 change"; "The form has errors." is gone.
3. Switch "Enabled" on → Footer "2 changes".
4. Press Ctrl/Cmd+S → Toast "Configuration saved (v2)".
5. Click the radio "JSON" → The editor contains `"retries": 4` and `"enabled": true`.
6. Click the tab link "Schema" → Heading "Applied schemas, in order"; a layer with the badge "Annotation", a link named `<key>` to `…/modeling/schemas/annotation/<key>` and "v1"; the textbox "Combined schema" contains `"retries"`.

##### CFG-SCH-02 A schema violation lists the errors, and Save anyway stores the document
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationSchemaFormTests.Configurations_SchemaViolation_SaveAnywayStoresIt`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, creates its annotation schema `{"type":"object","properties":{"retries":{"type":"integer","maximum":10}}}` in Modeling, then opens the configuration and clicks "Create empty" (v1).
1. Click the radio "JSON" and replace the text with `{"retries": 42}` → Footer "1 change"; "Save" is enabled.
2. Press Ctrl/Cmd+S → Alert "The configuration does not match its schema" listing `/retries`; no saved toast; still "1 change".
3. Click "Save anyway" in the alert → Dialog "Save although the schema fails?" with "The configuration will be stored with 1 schema violation(s): /retries …".
4. Click "Cancel" → The dialog closes; the alert stays; the badge still reads "v1".
5. Click "Save anyway" in the alert, then "Save anyway" in the dialog → Toast "Configuration saved (v2)"; the alert is gone; no change counter.

##### CFG-SCH-03 An inherited value shows as a placeholder, with Override and Reset to inherited
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationSchemaFormTests.Configurations_InheritedValue_OverrideAndReset`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a unit `unt.<r>.<u>` (with its missing responsibility `rst.<r>`), saves `{"retries": 2}` as the responsibility's configuration (v2), creates the unit's annotation schema `{"type":"object","properties":{"retries":{"type":"integer","title":"Retries"}}}` and clicks "Create empty" on the unit (v1).
1. Open the unit's configuration (Form) → In the field "Retries": the badge "inherited", the input's placeholder "2" and the button "Override".
2. Click "Override" → The input holds 2; the button "Reset to inherited" is shown, the badge "inherited" is gone; footer "1 change".
3. Click "Reset to inherited" → The badge "inherited" and "Override" are back; the input is empty with the placeholder "2"; no change counter.
4. Click "Override" and press Ctrl/Cmd+S → Toast "Configuration saved (v2)"; neither "inherited", "Override" nor "Reset to inherited" is shown.
5. Click the radio "JSON" → The editor contains `"retries": 2` as the unit's own value.

##### CFG-SCH-04 Lists and maps of the form add nested values
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationSchemaFormTests.Configurations_FormListsAndMaps_SaveNestedValues`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, creates its annotation schema `{"type":"object","properties":{"tiers":{"type":"array","title":"Tiers","items":{"type":"object","properties":{"name":{"type":"string","title":"Name"},"limit":{"type":"integer","title":"Limit"}}}},"headers":{"type":"object","title":"Headers","additionalProperties":{"type":"string"}}}}` and clicks "Create empty" (v1).
1. Open the configuration (Form) → The field "Tiers" with "Add item"; the field "Headers" with the input "New key" and a disabled "Add".
2. Click "Add item" → A card "Item 1" with the fields "Name" and "Limit" and the buttons "Move up", "Move down" and "Remove".
3. Type `gold` into the item's "Name" and leave the field → The card is titled "gold"; footer "1 change".
4. Type `x-run` into "New key" and click "Add" → A row with the textbox "Key" holding `x-run`, a value field and "Remove x-run"; footer "2 changes".
5. Type the run slug into the value of `x-run`, leave the field and press Ctrl/Cmd+S → Toast "Configuration saved (v2)".
6. Click the radio "JSON" → The editor contains `"name": "gold"` and `"x-run": "<runSlug>"`.

#### Inheritance (ConfigurationInheritanceTests)

##### CFG-INH-01 A child's Effective merges the parent's configuration and keeps it after Delete own
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationInheritanceTests.Configurations_Effective_MergesParentConfiguration`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a unit `unt.<r>.<u>`, which creates its missing responsibility `rst.<r>`.
1. Open `rst.<r>`'s configuration, click "Create empty", set `{"shared": "<runSlug>", "limit": 1}` and save → Toast "Configuration saved (v2)".
2. Open the unit's configuration, click "Create empty", set `{"limit": 2}` and save → Toast "Configuration saved (v2)"; the editor contains `"limit": 2` and no `"shared"`.
3. Click the tab link "Effective" → Text "Ancestors, then the type template, then the own content, merged in this order."; the document contains `"shared": "<runSlug>"` and `"limit": 2`, not `"limit": 1`.
4. Back on "Edit", click "Delete own", type the unit's key and click "Delete" → Toast "Own configuration deleted; inherited values remain"; heading "No configuration yet".
5. Click the tab link "Effective" → The document contains `"shared": "<runSlug>"` and `"limit": 1`.

##### CFG-INH-02 Composition shows the step that set each line and links the parent
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationInheritanceTests.Configurations_Composition_ShowsStepOfEachLine`
- **Preconditions** An editing pool user in the target view. The test Bulk creates `unt.<r>.<u>` (with `rst.<r>`), saves `{"shared": "<runSlug>", "limit": 1}` on `rst.<r>` and `{"limit": 2}` on the unit, and opens the unit's `…/effective?q=<runSlug>`.
1. Click the radio "Composition" (radiogroup "Effective view") → The URL has `show=composition`; heading "Steps, in the order applied"; a step for `rst.<r>` (a link) and one for the unit's key (its own content).
2. Read the gutter markers of the lines `"shared"` and `"limit"` in "Effective configuration, by step" → `"shared"` carries the responsibility step's order, `"limit"` the unit step's order.
3. Click "Show the lines of step <rst order>" → It is pressed; the status reads "Line <n>: step <rst order> · rst.<r>".
4. Click the `rst.<r>` link of the step → The URL is `…/configurations/rst.<r>?q=<runSlug>` (the parent's Edit tab).
5. Go back and click the radio "Document" → The URL has no `show=`; the textbox "Effective configuration" without the composition gutter.

##### CFG-INH-03 The breadcrumb and Lineage open the parent's configuration, and Annotation opens the annotation
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationInheritanceTests.Configurations_BreadcrumbAndLineage_OpenParent`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a unit `unt.<r>.<u>` (with `rst.<r>`); neither has a configuration.
1. Open `…/configurations/<unit key>?q=<runSlug>` → The navigation "Annotation ancestors" has the link "Responsibility <r>" and the current item "Unit <u>".
2. Click "Lineage" → Popover "Ancestors, applied top to bottom" lists 1 the link `rst.<r>` and 2 the unit's key.
3. Click the link `rst.<r>` → The URL is `…/configurations/rst.<r>?q=<runSlug>`; "Lineage" is disabled; heading "No configuration yet".
4. Go back and click the breadcrumb link "Responsibility <r>" → The URL is `…/configurations/rst.<r>?q=<runSlug>` again.
5. Click the link "Annotation" → The URL is `…/annotations/rst.<r>` without `q`; the annotation detail of `rst.<r>` opens.

#### Bindings and Resolved (ConfigurationBindingTests)

##### CFG-BND-01 Resolved is hidden until Reveal and shows the evaluated bindings
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationBindingTests.Configurations_Resolved_RevealShowsEvaluatedBindings`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility and saves `{"base": "https://<runSlug>.example", "url": "@config(\"/base\")"}` (v2).
1. Click the tab link "Resolved" → Alert "May contain secrets"; the button "Reveal"; the textbox "Resolved configuration" is not in the accessibility tree.
2. Click "Reveal" → The textbox "Resolved configuration" contains `"url": "https://<runSlug>.example"`.
3. Click the tab link "Effective" → The textbox "Effective configuration" contains the unevaluated `"url": "@config(\"/base\")"`.
4. Click the tab link "Resolved" again → "Reveal" is shown again; the resolved document is hidden again.

##### CFG-BND-02 A binding that cannot be evaluated shows the binding error
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationBindingTests.Configurations_Resolved_ShowsBindingError`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility and saves a binding that cannot be evaluated (v2): on the mock `{"broken": "@config(\"/missing\")"}`, on a live target `{"broken": "@[x @config(\"/missing\")]"}`.
1. Click the tab link "Resolved" → Alert "May contain secrets" and the error alert "A binding could not be evaluated" with the server's message.
2. Look for the resolved document → No "Reveal" and no textbox "Resolved configuration".
3. On "Edit", replace the document with `{"broken": "fixed"}` and save → Toast "Configuration saved (v3)".
4. Click the tab link "Resolved" → No binding error; "Reveal" is shown.

##### CFG-BND-03 The JSON editor completes @config paths, explains them on hover, and Bindings filters the reference
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationBindingTests.Configurations_JsonEditor_CompletesAndExplainsBindings`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility and clicks "Create empty" (v1).
1. Insert `{"count": 1}`, then replace the text with `{` and a second line `  "endpoint": "` → The partial text is shown; "Save" is disabled.
2. Type `@con` and click the completion option starting with "config" → A list of paths opens with the option "/count".
3. Click "/count", press End and insert `"` and a last line `}` → The editor contains `"endpoint": "@config(\"/count\")"`.
4. Hover the binding text → The hover card contains "Function config" and "Reads a value of the configuration being resolved".
5. Click the toolbar button "Bindings" and type `config` into "Filter the binding reference" → The region "Binding reference" shows the entry of the function config, without "Use".
6. Filter with `zzzz-nomatch` → "Nothing matches “zzzz-nomatch”.".

##### CFG-BND-04 fx turns a form field into a binding, Use fills it, and Convert to value turns it back
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ConfigurationBindingTests.Configurations_FormBinding_UseAndConvertToValue`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, creates its annotation schema `{"type":"object","properties":{"endpoint":{"type":"string","title":"Endpoint"}}}` and clicks "Create empty" (v1).
1. Open the configuration (Form) and click "fx" in the field "Endpoint" → A monospace textbox with the value `@`; text "Evaluated by the server; schema checks run after evaluation."; the button "Convert to value".
2. Click the field's button "Binding reference" → The region "Binding reference" with the headings "Expressions", "Functions", "Sources" and "Object bindings".
3. Click the first "Use" of the function config's entry → The popover closes; the textbox value starts with `@config(`; the explanation contains "Function config: Reads a value of the configuration being resolved".
4. Press Ctrl/Cmd+S → Toast "Configuration saved (v2)".
5. Click the radio "JSON" → The editor contains `"endpoint": "@config(`.
6. Click the radio "Form", then "Convert to value" → A plain text field whose value starts with `config(`; the "fx" toggle is shown again; footer "1 change".

#### List and Navigation (ConfigurationNavigationTests)

##### CFG-NAV-01 A saved configuration is listed with its version and author, and its row opens the detail
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ConfigurationNavigationTests.Configurations_List_ShowsVersionAndOpensDetail`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility, opens `…/configurations/<key>?q=key:<key>`, clicks "Create empty" and saves a generated document (v2).
1. Close the detail ("Close the detail"; phone: "Back to the list") → The URL is `…/configurations?q=key:<key>`; the counter reads "1 of <total>".
2. Look at the key's row (from 768 px a grid row; phone: a list item) → Grid: the type's name, Name = the key's last part, Key `<key>`, Version "v2", Author = the signed-in user's name, Updated "just now". Phone: the name, "v2" and the key.
3. Click the row's Name cell (phone: tap the item) → The URL is `…/configurations/<key>?q=key:<key>`; the region `<key>` with the tab link "Edit" and the badge "v2".
4. Look at the layout → From 768 px the filter "Filter annotations" stays visible beside the detail and "Close the detail" is shown; on a phone the list is hidden and "Back to the list" is shown.

##### CFG-NAV-02 The tabs keep the filter, and Close returns to the list
- **Priority** Low · **Role** Editor · **Mode** B · **Test** `ConfigurationNavigationTests.Configurations_Tabs_KeepFilterAndCloseToList`
- **Preconditions** An editing pool user in the target view. The test Bulk creates a responsibility and clicks "Create empty" on its configuration (v1).
1. Open `…/configurations/<key>?q=<runSlug>` → The detail `<key>` opens on the Edit tab with the radio "JSON" and the badge "v1".
2. Click the tab link "Effective" → The URL is `…/configurations/<key>/effective?q=<runSlug>`; the text "Ancestors, then the type template, then the own content, merged in this order." and the textbox "Effective configuration".
3. Click the tab link "Resolved" → The URL is `…/configurations/<key>/resolved?q=<runSlug>`; the alert "May contain secrets" and the button "Reveal".
4. Click the tab link "Versions" → The URL is `…/configurations/<key>/versions?q=<runSlug>`; the timeline lists v1 with the badge "current".
5. Click the tab link "Schema" → The URL is `…/configurations/<key>/schema?q=<runSlug>`; "No schema applies" (on the mock), or "Applied schemas, in order" where a Responsibility type schema applies.
6. Click the tab link "Edit" → The URL is `…/configurations/<key>?q=<runSlug>`; the badge "v1".
7. Close the detail ("Close the detail"; phone: "Back to the list") → The URL is `…/configurations?q=<runSlug>`; the filter "Filter annotations" holds `<runSlug>` and the key's row (phone: list item) is listed.

##### CFG-NAV-03 Returning to Configurations reopens the last configuration and tab
- **Priority** Medium · **Role** Editor · **Mode** B,D · **Test** `ConfigurationNavigationTests.Configurations_Section_ReopensLastConfiguration`
- **Preconditions** An editing pool user in the target view, desktop layout (docked sidebar). The test Bulk creates a responsibility and clicks "Create empty" on its configuration (v1).
1. Open `…/configurations/<key>/versions?q=key:<key>` → The Versions timeline of the key lists v1 with the badge "current".
2. Follow the sidebar link "Annotations" → The URL is `…/annotations`.
3. Follow the sidebar link "Configurations" → The URL is again `…/configurations/<key>/versions?q=key:<key>`.
4. Click "Close the detail" → The URL is `…/configurations?q=key:<key>`.
5. Follow the sidebar link "Configurations" → The URL is `…/configurations` without a query (same section, no redirect); the filter "Filter annotations" is empty.

#### Roles (ConfigurationRoleTests)

##### CFG-ROL-01 A Contributor creates and saves a configuration, and Resolved leaves secret values out
- **Priority** Medium · **Role** Contributor · **Mode** B · **Test** `ConfigurationRoleTests.Configurations_Contributor_EditsWithoutSecrets`
- **Preconditions** A pool user who is exactly Contributor, in the Contributor's project of Target:RoleProjects (mock: acme/shipping/default). The test Bulk creates a responsibility there.
1. Open the key's configuration → Heading "No configuration yet" with "Create empty" and "Start from template".
2. Click "Create empty" → Toast "Configuration created"; "Delete own" is shown; no "Read-only" badge.
3. Replace the text with a generated document and press Ctrl/Cmd+S → Toast "Configuration saved (v2)".
4. Click the tab link "Resolved" → Alert "May contain secrets" with "Secret values are left out for your role (ContributorWithSecrets is required to see them).".
5. Click "Reveal" → The textbox "Resolved configuration" contains each value of the document.

##### CFG-ROL-02 A Reader gets no New and no create actions
- **Priority** Medium · **Role** Reader · **Mode** B · **Test** `ConfigurationRoleTests.Configurations_Reader_HasNoCreateActions`
- **Preconditions** A pool user who is exactly Reader, in the Reader's project of Target:RoleProjects (mock: northwind/portal/default). The test creates nothing; it generates a responsibility key that does not exist.
1. Open Configurations → The filter "Filter annotations" is shown; there is no button "New configuration".
2. Open `…/configurations/<generated key>` → Heading "No configuration yet" with "This annotation has no own configuration. It still inherits values from its ancestors and the type template."; no "Create empty" and no "Start from template".
3. Click the tab link "Effective" → "No configuration" with "Nothing is configured or inherited for this annotation yet.".
4. Click the tab link "Versions" → "No configuration, no history".

##### CFG-ROL-03 A Reader sees an existing configuration read-only
- **Priority** Medium · **Role** Reader · **Mode** M · **Test** `ConfigurationRoleTests.Configurations_Reader_SeesConfigurationReadOnly`
- **Preconditions** Mock only. Ada Lovelace as Reader in northwind/portal/default (the Reader's project of Target:RoleProjects), whose seed has configurations. The test creates nothing.
1. Open Configurations and open the first configuration of the list → The detail shows the badge "Read-only"; no "Delete own", no "Revert" and no "Save".
2. Click the radio "JSON" → The textbox "Own configuration" is not editable.
3. Click the tab link "Resolved" → Alert "May contain secrets" with "Secret values are left out for your role (ContributorWithSecrets is required to see them).".

#### Not automated
- Expired versions (badge "expired", "This version is not available", "A version is no longer available" with the server hunks): they need a history older than 30 days, which a test cannot produce; only the mock seed has it.
- Composition warning "Read at different times" with Refresh: it shows only when a document changes between two background reads, a race that cannot be reproduced; the mock hash depends only on the JSON length.
- Scroll-position memory of the Configurations list: it needs a list taller than the viewport (100+ configurations), too costly to create per run on a live target, and the restore depends on the virtualization's timing.
- Native beforeunload prompt when a dirty editor's tab is closed or reloaded: a browser-native dialog that differs between engines and BrowserStack; the in-app "Discard changes?" guard is covered (CFG-EDT-01).
- Load and save failures (ErrorState with Retry on the list, the detail and the New dialog's "The catalog could not be loaded…", the toasts "The configuration was not saved", "The configuration was not created" and "Delete failed"): they need fault injection (500, 429), which neither the mock nor a live target offers.
- Secret values in Resolved for a secrets role (sources of the key-value store, "secret values are included"): the mock resolves no sources and leaves a source binding as written; it needs a live source and secret, and the catalogue has no live-only cases.
- Evaluation of interpolation, math, JSON Logic and JSON-e envelopes in Resolved: the mock leaves them as written; only a live deployment evaluates them.
- Resolved banner of exactly ContributorWithSecrets: no project on the mock where the pool users hold exactly ContributorWithSecrets.
- Author links in the grid and the Versions timeline (a link for Administrators, plain text for Contributors): the shared AuditActorLink is owned by Monitoring (MON-AUD-01 follows an Actor link), and no project on the mock has exactly Administrator.
- Schema tab empty state "No schema applies" and the Edit tab without Form: they depend on the absence of a Responsibility type schema, unknown on a live target, so CFG-NAV-02 accepts "No schema applies" only as one of two outcomes; a disposable project for a static text is not worth it, and the Annotation layer is covered (CFG-SCH-01).
- Type and descendant-type schema layers, schema creation and the compliance check of schema saves: Modeling owns schemas (MOD-SCH-03 type schemas, MOD-SCH-05 the compliance check with Save anyway), and type schemas change every configuration of the type.
- Schema linter marks in the JSON and YAML editor (CodeMirror diagnostics after 400 ms): timing-dependent decorations; the server's 409 panel and the form errors are covered.
- Segmented date, time and date-time controls of the schema form: typed segment by segment in the browser's locale and time zone, brittle across engines and profiles.
- Other form renderers (oneOf tabs, password Reveal/Hide, URI "Open the link", enum multi-select, tags): secondary renderer details whose value per test does not pay for a schema per case; the basic controls, lists and maps are covered.
- Diff view controls (Previous change, Next change, Collapse unchanged, Auto/Split/Unified): view options of the shared DiffEditor, which Modeling defers to this section; the diff itself is covered (CFG-EDT-04).
- Visual effects (Resolved blur, composition step tints, diff colours, type colours in the grid): visual only; the TS visual suite and axe cover them.
- Detail panel resize, pin, full screen and Esc: owned by Navigation (NAV-DET-01 to NAV-DET-03), which tests the shared DetailPanel.
- Compare views of a configuration across views: owned by Modeling (MOD-CMP-01).
- Clipboard of the key's copy button and of the binding reference examples: the same useClipboard path as "Copy mine", which is covered (CFG-CON-02).
- The configuration link in the annotation detail's header ("Configuration · v1", "Inherited configuration"): owned by Annotations (ANN-DET-03), and the mock reports HasContent differently from the real API.
- Old detail-tab links of a configuration (`…/annotations/<key>/configuration` with `?cfg=effective` or `?cfg=resolved`, `…/annotations/<key>/schema` and `/versions`, `…/graph/<key>/configuration` and its tab stubs): redirect stubs owned by Annotations (ANN-DET-06, the annotations/<key>/* stubs) and Graph (GRA-DET-04, the graph/<key>/* stubs); this section starts at the configuration detail and checks its tabs (CFG-NAV-02).

### 6. Modeling

Modeling covers the Modeling page of a view (`…/{view}/modeling`) and its five path tabs. It includes the tab shell, the role locks, the redirects of the old URLs (MOD-TAB-02 owns every redirect of the merged Modeling pages, the old `…/{view}/deleted` included), the remembered tab and the "Discard changes?" guard. Schemas covers the list, the "New schema" dialog and the schema editor: create, meta-schema validation, Form preview, breaking changes with "Save anyway", versions and the typed delete. Templates covers create, patch, versions, delete and the JSON Patch conflict dialog. Export & Import covers folder and Git exports downloaded as zips, and imports of generated zips: "Check", "Import", "Import now", "Overwrite" (the default) and "Skip", force, faulty files, the "Latest import" card and the toaster. The Deleted list of the view covers roots and "Show cascaded", details, restore with and without descendants, bulk restore and "Delete permanently". Compare views is the last tab. The six sub-folders map to the classes `ModelingTabsTests`, `ModelingSchemaTests`, `ModelingTemplateTests`, `ModelingExportImportTests`, `ModelingDeletedTests` and `ModelingCompareTests` in `Tests/Modeling` (`[Area(Area.Modeling)]`). Keys are written as `rst.<r>`, `sbt.<s>`, `cnt.<s>.<c>` and so on. Each part is generated (`PlatformData.AnnotationKey`, "at-<run>-…"), so every key the tests create carries the run id. The tests act only on annotations, schemas and configurations they create in the target view. Type schemas, templates and views change every configuration of a type, or the project, so tests change them only inside a disposable project (mode P). The Elementary case is MOD-SCH-01 "Create an annotation schema for a new annotation".

#### Tabs and Access (ModelingTabsTests)

##### MOD-TAB-01 Modeling opens on Schemas and every tab opens its own page
- **Priority** High · **Role** Admin · **Mode** B · **Test** `ModelingTabsTests.Modeling_OpenTabs_EachTabOpensItsPage`
- **Preconditions** Administrator or Owner of the target project (the "Export & Import" tab is listed only from Administrator up). Target view. Nothing is created.
1. Open `…/{view}/modeling` → The URL becomes `…/modeling/schemas`; tab "Schemas" is selected (aria-selected=true) and the section "Type schemas" shows.
2. Read the tabs of the Modeling toolbar → Exactly "Schemas", "Templates", "Export & Import", "Deleted", "Compare views", in this order.
3. Click tab "Templates" → URL `…/modeling/templates`; "Templates" is selected.
4. Click tab "Export & Import" → URL `…/modeling/export-import`; regions "Export" and "Import" are visible.
5. Click tab "Deleted" → URL `…/modeling/deleted`; the switch "Show cascaded" is visible.
6. Click tab "Schemas", then go Back in the browser → URL `…/modeling/deleted` again, with "Deleted" selected.
7. Focus tab "Deleted" and press ArrowLeft → "Export & Import" has the focus; the URL is still `…/modeling/deleted` (manual activation).
8. Press Enter → URL `…/modeling/export-import`; "Export & Import" is selected and keeps the focus.

##### MOD-TAB-02 Old URLs of the merged pages redirect to their Modeling tabs
- **Priority** Low · **Role** Editor · **Mode** B · **Test** `ModelingTabsTests.Modeling_OldUrls_RedirectToModelingTabs`
- **Preconditions** Contributor or higher in the target project. Target view. Nothing is created; every step is a fresh page load. This case owns every redirect of the merged Modeling pages (`/schemas`, `/templates`, `/export-import`, `/deleted` and `/compare` of a view).
1. Open `…/{view}/schemas` → Path `…/{view}/modeling/schemas`; tab "Schemas" is selected; the section "Type schemas" is visible.
2. Open `…/{view}/templates/rst` → Path `…/modeling/templates/rst`; the template detail (section `#template-detail`) is visible.
3. Open `…/{view}/schemas/type/rst?tab=versions` → Path `…/modeling/schemas/type/rst` with `tab=versions` kept; the detail's tab "Versions" is selected.
4. Open `…/{view}/deleted` → Path `…/modeling/deleted` without `cascaded`; tab "Deleted" is selected; the switch "Show cascaded" is not checked.
5. Open `…/{view}/deleted?cascaded=1` → Path `…/modeling/deleted` with `cascaded=1`; the switch "Show cascaded" is checked.
6. Open `…/{view}/export-import` → Path `…/modeling/export-import`; for an Administrator or Owner tab "Export & Import" is selected, for a Contributor the heading "Administrators only" shows.
7. Open `…/{view}/compare` → Path `…/modeling/compare`; tab "Compare views" is selected.
8. Open `…/modeling/templates/foo` → Empty state "Unknown annotation type".

##### MOD-TAB-03 A Reader gets the Contributors only lock on every Modeling URL
- **Priority** High · **Role** Reader · **Mode** B · **Test** `ModelingTabsTests.Modeling_Reader_GetsContributorsOnlyLock`
- **Preconditions** A pool user who is exactly Reader of the role project (Target:RoleProjects Reader; on the mock northwind/portal, view default). Nothing is created.
1. Open `…/{view}/modeling` → URL `…/modeling/schemas`; heading "Contributors only".
2. Read the lock → Text "The schemas, the templates, the deleted annotations and the comparison of views are available to the project's Contributors, the export and the import to its Administrators."; no element with role tab; no button "New schema".
3. Open `…/modeling/templates/exe` → The same lock; no template detail (no section `#template-detail`).
4. Open `…/modeling/export-import` → The same lock; no region "Export" or "Import".
5. Open `…/modeling/deleted`, then `…/modeling/compare` → The same lock on each; no switch "Show cascaded"; no combobox "Annotation key".

##### MOD-TAB-04 A Contributor gets four tabs, the Administrators only lock and no Delete permanently
- **Priority** High · **Role** Contributor · **Mode** B · **Test** `ModelingTabsTests.Modeling_Contributor_GetsNoAdministratorFeatures`
- **Preconditions** A pool user who is exactly Contributor of the role project (Target:RoleProjects Contributor; on the mock acme/shipping, view default). The test bulk creates a new subject `sbt.<s>` there and deletes it in the Annotations section.
1. Open `…/modeling/schemas` → Only the tabs "Schemas", "Templates", "Deleted", "Compare views" (no "Export & Import"); "Schemas" is selected; the button "New schema" is visible.
2. Open `…/modeling/export-import` → Heading "Administrators only" and the text "The export and the import of the view are available to the project's Administrators."; four tabs, none selected; no region "Export" or "Import".
3. Click tab "Deleted" → URL `…/modeling/deleted`; the row `sbt.<s>` is listed.
4. Open "Actions for sbt.<s>" → Menu items "Show details", "Restore", "Restore without descendants"; no "Delete permanently".
5. Close the menu (Escape) and check "Select sbt.<s>" → Toolbar "Bulk actions" with "1 selected" and "Restore", without "Delete permanently".
6. Click "Restore" in "Bulk actions" → Toast "sbt.<s> restored"; the row is gone.

##### MOD-TAB-05 Modeling returns to the last tab, detail and query after another section
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ModelingTabsTests.Modeling_ReturnFromOtherSection_RestoresLastTab`
- **Preconditions** Contributor or higher in the target project. Target view. Only the first page is opened by URL; every later step navigates by clicks, because the memory lives in the page. Nothing is created; the template detail is only read.
1. Open `…/modeling/deleted` and turn on "Show cascaded" → The URL contains `cascaded=1`.
2. Navigate to "Annotations" in the navigation → `…/annotations` shows.
3. Navigate to "Modeling" → URL `…/modeling/deleted` with `cascaded=1`; "Deleted" is selected; "Show cascaded" is checked.
4. Desktop only: click tab "Templates" and open the Responsibility row → URL `…/modeling/templates/rst`; the template detail is visible.
5. Desktop only: navigate to "Configurations", then to "Modeling" → URL `…/modeling/templates/rst`; the detail is visible; "Templates" is selected.
6. Navigate to "Modeling" again → URL `…/modeling/schemas`; "Schemas" is selected.

##### MOD-TAB-06 A dirty schema editor asks Discard changes? before another tab opens
- **Priority** Medium · **Role** Editor · **Mode** B,D · **Test** `ModelingTabsTests.Modeling_LeaveDirtySchemaEditor_AsksToDiscard`
- **Preconditions** Contributor or higher in the target project. Target view, desktop layout. The test bulk creates `rst.<r>`.
1. Open `…/modeling/schemas/annotation/rst.<r>`, replace the document with `{"type":"object","properties":{"note":{"type":"string"}}}` and press Ctrl+S → Toast "Schema saved (v1)"; "Save" is disabled; the page title has no leading "● ".
2. Change `"string"` to `"number"` in the editor → The page title starts with "● "; "Save" is enabled.
3. Click tab "Templates" → Dialog "Discard changes?" with "Your changes have not been saved."
4. Click "Keep editing" → The dialog closes; the URL is still `…/schemas/annotation/rst.<r>`; "Schemas" stays selected; the editor still holds `"number"`.
5. Click tab "Templates", then "Discard" → URL `…/modeling/templates`; "Templates" is selected; the title has no "● ".
6. Open `…/modeling/schemas/annotation/rst.<r>` → The editor shows the saved v1 with `"string"`.

#### Schemas (ModelingSchemaTests)

##### MOD-SCH-01 Create an annotation schema for a new annotation
- **Priority** Elementary · **Role** Editor · **Mode** B · **Test** `ModelingSchemaTests.Schemas_CreateAnnotationSchema_ListsVersionOne`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates a new responsibility `rst.<r>`, without a configuration.
1. Open `…/modeling/schemas/annotation/rst.<r>` → Detail region "Schema of rst.<r>" with "Validates the configuration of this annotation, on top of its type schema." and "Applies to 0 configurations of this view."; button "Create"; no "Delete".
2. Replace the textbox "Schema document" with `{"type":"object","properties":{"ticket":{"type":"string","title":"Ticket"}}}` → No issue list under the editor; "Create" is enabled.
3. Press Ctrl+S in the editor → Toast "Schema saved (v1)"; "Save" is disabled; "Delete" is visible.
4. Close the detail ("Close"; "Back" on a phone) → URL `…/modeling/schemas`; the section "Annotation schemas" is visible.
5. Read the row of `rst.<r>` in "Annotation schemas" → Link `rst.<r>` (href `…/modeling/schemas/annotation/rst.<r>`) with "v1" in its row.

##### MOD-SCH-02 New schema offers the kinds and only the types that can descend, then opens the editor
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ModelingSchemaTests.Schemas_NewSchemaDialog_OpensDescendantTypeEditor`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates a new unit of execution `uxe.<s>.<r>.<c>.<u>`, which also creates its ancestors, among them the responsibility `rst.<r>`.
1. On `…/modeling/schemas` click "New schema" → Dialog "New schema"; radio "Type schema" checked; combobox "Annotation type" shows "Execution"; "Open editor" enabled.
2. Click "Cancel", then "New schema" again → The dialog closes without navigating (URL still `…/modeling/schemas`), then opens again.
3. Choose the radio "Descendant-type schema" → Combobox "Annotation key" (placeholder "exe.subject.responsibility.context") shows; "Open editor" is disabled.
4. Type `rst.<r>` in "Annotation key" and pick its option → "Annotation type" offers 4 types (Unit, Usage, Execution, Unit of execution); "Execution" stays selected; "Open editor" is enabled.
5. Pick `uxe.<s>.<r>.<c>.<u>` instead → Help "No annotation type descends from uxe.<s>.<r>.<c>.<u>."; "Annotation type" is disabled; "Open editor" is disabled.
6. Pick `rst.<r>` again and click "Open editor" → The dialog closes; URL `…/modeling/schemas/annotation/rst.<r>/type/exe`; region "Execution schema below rst.<r>".
7. Press Ctrl+S in the editor (starter document) → Toast "Schema saved (v1)".
8. Close the detail → Section "Descendant-type schemas" has a row with the link "Execution below" (href `…/schemas/annotation/rst.<r>/type/exe`), the key link `rst.<r>` and "v1".

##### MOD-SCH-03 Create and delete a type schema
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `ModelingSchemaTests.Schemas_CreateAndDeleteTypeSchema_UpdatesTypeRow`
- **Preconditions** Owner of the target organization. The test creates a disposable project (only the default view, no schemas) and works in its default view.
1. Open `…/<project>/default/modeling/schemas` → Section "Type schemas" with 7 rows; the Responsibility row shows "No schema"; "Annotation schemas" and "Descendant-type schemas" show "None.".
2. Click the Responsibility link → Region "Responsibility schema" with "Validates every responsibility configuration of the view."; the starter document; button "Create".
3. Replace the document with `{"type":"object","properties":{"enabled":{"type":"boolean"}}}` and press Ctrl+S → Toast "Schema saved (v1)"; "Delete" is visible.
4. Close the detail → The Responsibility row shows "v1" instead of "No schema".
5. Open the Responsibility row again and click "Delete" → Dialog "Delete the Responsibility schema?" with "Configurations are no longer validated by it, and the form disappears where it was the only schema."; its "Delete" is disabled.
6. Type `delete` in "Type delete to confirm" and click "Delete" in the dialog → Toast "Schema deleted"; the editor shows the starter document and "Create".
7. Close the detail → The Responsibility row shows "No schema" again.

##### MOD-SCH-04 Meta-schema and syntax errors block saving a schema, and Revert restores it
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ModelingSchemaTests.Schemas_InvalidDocument_BlocksSaveUntilFixed`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates `rst.<r>`.
1. Open `…/modeling/schemas/annotation/rst.<r>` and replace the document with `{"type":"objekt"}` → The issue list under the editor shows "/type must match a schema in anyOf"; "Create" is disabled; the Form preview shows "No preview".
2. Replace the document with `{"type":` → An alert starting "Line 1: "; "Create" is disabled.
3. Replace the document with `{"type":"object","properties":{"limit":{"type":"integer","maximum":10}}}` and press Ctrl+S → No alert; toast "Schema saved (v1)".
4. Change `10` to `20` → "Save" and "Revert" are enabled.
5. Click "Revert" → The editor shows `"maximum": 10` again; "Save" and "Revert" are disabled.

##### MOD-SCH-05 A schema that breaks a configuration lists it, and Save anyway forces it
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ModelingSchemaTests.Schemas_BreakingChange_SaveAnywayForcesIt`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates `rst.<r>` and saves its own configuration `{"retries": 5}` ("Create empty", the document, Save).
1. Open `…/modeling/schemas/annotation/rst.<r>` → "Applies to 1 configuration of this view."
2. Replace the document with `{"type":"object","properties":{"retries":{"type":"integer","maximum":1}}}` and press Ctrl+S → No toast; the alert "Existing configurations would not comply".
3. Read its table → Columns "Configuration", "View", "Errors"; a row with the link `rst.<r>` (href `…/{view}/configurations/rst.<r>`), the view's name and "/retries Number too big".
4. Click "Save anyway" in the alert → Dialog "Save although configurations break?" with "1 configuration(s) will no longer comply with the schema. They keep working, but their next save without force fails until they are fixed."
5. Click "Save anyway" in the dialog → Toast "Schema saved (v1)"; the alert is gone; "Save" is disabled.

##### MOD-SCH-06 The form preview renders the schema and keeps sample data
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ModelingSchemaTests.Schemas_FormPreview_RendersObjectSchema`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates `rst.<r>`; nothing is saved.
1. Open `…/modeling/schemas/annotation/rst.<r>` and replace the document with `{"type":"object","properties":{"ticket":{"type":"string","title":"Ticket"}}}` → Heading "Form preview" with a field labelled "Ticket".
2. Fill "Ticket" with `AT-1` and open "Sample data" → The sample shows `"ticket": "AT-1"`.
3. Click "Reset sample" → "Ticket" is empty; the sample is `{}`.
4. Replace the document with `{"type":"array"}` → "No preview" with "The preview appears when the schema is a valid object schema."
5. Click "Revert", then tab "Versions" → The starter document was back; "No schema, no history" (nothing was saved).

##### MOD-SCH-07 Schema versions show the history and a comparison, and a typed delete ends them
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `ModelingSchemaTests.Schemas_VersionsAndDelete_ShowHistoryThenNone`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates `rst.<r>` and saves its annotation schema twice: with `"maximum": 5` (v1), then with `"maximum": 9` (v2).
1. Click the detail's tab "Versions" → The URL has `tab=versions`; the timeline lists v2 with the badge "current" above v1.
2. Click "View" on v1 → The URL has `v=1`; "Version v1"; the read-only textbox "Version 1" contains `"maximum": 5`; button "Back to history".
3. Click "Back to history", then "Compare with previous" on v2 → The URL has `v=2` and `from=1`; "v1 → v2"; the diff summary "+1 −1 · 1 change".
4. Click tab "Edit" → `tab` is removed from the URL; the editor shows `"maximum": 9`.
5. Click "Delete" and type `delet` in "Type delete to confirm" → Dialog "Delete the Schema of rst.<r>?"; its "Delete" stays disabled.
6. Click "Cancel", then "Delete" again, type `delete` and press Enter → Toast "Schema deleted"; the editor shows "Create".
7. Click tab "Versions" → "No schema, no history".
8. Close the detail → Section "Annotation schemas" has no row for `rst.<r>`.

#### Templates (ModelingTemplateTests)

##### MOD-TPL-01 Create, edit, version and delete a template
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `ModelingTemplateTests.Templates_CreateEditAndDelete_KeepsVersions`
- **Preconditions** Owner of the target organization. The test creates a disposable project (no templates) and works in its default view.
1. Open `…/<project>/default/modeling/templates` → 7 rows; the Responsibility row shows "No template" and "0 configurations".
2. Open the Responsibility row → Region "Responsibility template"; "No responsibility template" with "Configurations of this type inherit only from their ancestors."; button "Create template".
3. Click "Create template", replace the textbox "Template" with `{"owner":"<run>","retries":2}` and press Ctrl+S → Toast "Template saved (v1)"; badge "v1"; "Delete" is visible.
4. Change `"retries": 2` to `3` → "1 change(s)"; "Save" is enabled.
5. Press Ctrl+S → Toast "Template saved (v2)"; badge "v2".
6. Click tab "Versions", then "Compare with previous" on v2 → The timeline shows v2 "current" and v1; "v1 → v2" with the diff summary "+1 −1 · 1 change".
7. Click tab "Edit", then "Delete"; type `rst` in "Type rst to confirm" and click "Delete" in the dialog → Dialog "Delete the responsibility template?" with "Configurations of this type stop inheriting its values."; toast "Template deleted"; "No responsibility template" again.
8. Close the detail → The Responsibility row shows "No template".

##### MOD-TPL-02 A concurrent template change opens the conflict dialog
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `ModelingTemplateTests.Templates_ConcurrentChange_OpensConflictDialog`
- **Preconditions** Owner of the target organization. The test creates a disposable project, saves the Responsibility template `{"retries":2}` (v1) in its default view and keeps that editor open. The clipboard recorder is installed before the first page load.
1. Change `"retries": 2` to `5` without saving → "1 change(s)".
2. ConcurrentEdit: another writer saves the template with `"retries": 9` → The stored template is v2: on the mock the in-page PUT answers 200 with Version 2, on live the second page shows the toast "Template saved (v2)"; this editor still shows "1 change(s)".
3. Press Ctrl+S → Dialog "Someone else changed this configuration" with the diff labels "Theirs (stored)" and "Yours" and the buttons "Copy mine", "Reload, discard mine", "Overwrite".
4. Click "Reload, discard mine" → The dialog closes; the editor shows `"retries": 9`; "Save" is disabled.
5. Change `9` to `5`; ConcurrentEdit saves `"retries": 7` (v3); press Ctrl+S → The conflict dialog opens again.
6. Click "Copy mine" → The button reads "Copied"; the copied text contains `"retries": 5`.
7. Click "Overwrite" → Toast "Template saved (v4)"; the dialog closes; the editor keeps `"retries": 5`.

#### Export and Import (ModelingExportImportTests)

##### MOD-EXI-01 Export the view as a folder of JSON files and check it back in
- **Priority** High · **Role** Admin · **Mode** B · **Test** `ModelingExportImportTests.Export_FolderOfJsonFiles_ChecksBackInUnchanged`
- **Preconditions** Administrator or Owner of the target project. Target view. The test bulk creates `rst.<r>`.
1. Open `…/modeling/export-import` → Regions "Export" and "Import"; in "Export" the kinds "Annotations", "Schemas", "Templates", "Configurations" are checked, the format "Folder of JSON files" is selected, and "Never included: secrets, effective documents, the audit, the version history of annotations, and deleted annotations." shows.
2. Click "Export" in "Export" and wait for the download → Status "<file> was downloaded."; a toast starting "Exported "; the file name matches `export-<org>-<project>-<view>-<yyyyMMdd>-<HHmmss>Z.zip`.
3. Open the zip → `export.json` names the target Organization, Project and View, with Include `["Annotations","Schemas","Templates","Configurations"]`; the zip holds `annotations/rst.<r>.json`.
4. Set the downloaded zip on the Import file input and click "Check" → Region "Latest import" with the banner "Checked: nothing was written" and a description starting "<file>: ".
5. Look for `rst.<r>` in the result → No document row for `rst.<r>` (unchanged documents are not listed).

##### MOD-EXI-02 Export only some kinds, require one kind, and export a Git repository
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `ModelingExportImportTests.Export_KindsAndGitFormat_ShapeTheArchive`
- **Preconditions** Administrator or Owner of the target project. Target view. Nothing is created.
1. In "Export" uncheck "Schemas" and "Configurations" and click "Export" → A download; a toast matching "Exported <n> annotation(s) and <m> template(s)".
2. Open the zip → `export.json` has Include `["Annotations","Templates"]`; every other entry is under `annotations/` or `templates/`.
3. Uncheck "Annotations" and "Templates" → Field error "Pick at least one kind."; "Export" is disabled.
4. Check all four kinds, choose "Git repository" and click "Export" → The file name matches `export-<org>-<project>-<view>-git-<yyyyMMdd>-<HHmmss>Z.zip`; the zip holds `.git/HEAD` and no `export.json`.

##### MOD-EXI-03 Check an archive with a new annotation, then Import now
- **Priority** High · **Role** Admin · **Mode** B · **Test** `ModelingExportImportTests.Import_CheckThenImportNow_CreatesAnnotation`
- **Preconditions** Administrator or Owner of the target project. Target view. The test builds a zip in memory: `annotations/rst.<r>.json` `{"Title":"AT import <run>"}` and `notes.txt`.
1. Set the zip on the Import file input and click "Check" → Banner "Checked: nothing was written"; description "<file>: 1 to create, 0 unchanged."
2. Read "Summary" → Row "Annotations" with To create 1; row "Total" with Ignored files 1.
3. Read "Documents" → Row `rst.<r>`: kind "Annotation", outcome "To create", no link.
4. Click "Import now" in the banner → Banner "Imported"; description "<file>: 1 created, 0 unchanged."; outcome "Created"; the link `rst.<r>`.
5. Follow the link `rst.<r>` → `…/annotations/rst.<r>`; the textbox "Title" has "AT import <run>".

##### MOD-EXI-04 Skip keeps an existing document, and the result shows again after a reload
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `ModelingExportImportTests.Import_SkipExisting_KeepsItAfterReload`
- **Preconditions** Administrator or Owner of the target project. Target view. Zip A: `annotations/rst.<a>.json` `{"Title":"First <run>"}`. Zip B: `annotations/rst.<a>.json` `{"Title":"Second <run>"}` and `annotations/rst.<b>.json` `{"Title":"New <run>"}`.
1. Set zip A on the Import file input and click "Import" → Banner "Imported"; row `rst.<a>` with outcome "Created".
2. Set zip B, choose "Skip" under "Existing documents" and click "Import" → Banner "Imported"; description "<file>: 1 created, 0 unchanged, 1 skipped."
3. Read the row `rst.<a>` → Outcome "Skipped", code "DocumentExists", "The view has a different annotation rst.<a>, and Skip keeps it.", with a link.
4. Click the chip "Skipped 1", then "Clear the filter" → First only the `rst.<a>` row; then both rows again.
5. Reload the page → "Latest import" shows "Imported" and the DocumentExists row again, without "Import now".
6. Follow the link `rst.<a>` → The textbox "Title" still has "First <run>".

##### MOD-EXI-05 A file that is not a zip is refused, and excluded, ignored and invalid files are reported
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `ModelingExportImportTests.Import_FaultyArchive_ReportsEveryProblem`
- **Preconditions** Administrator or Owner of the target project. Target view. Payloads built in memory: `not-a-zip.zip` (the text "not a zip"), and a zip with `annotations/rst.<a>.json` `{"Title":"A <run>"}`, `configurations/rst.<a>.conf.json` `{"retries":1}`, `annotations/rst.<b>.json` with the text `{ not json`, and `notes.txt`.
1. Set `not-a-zip.zip` on the Import file input and click "Import" → Alert "The import failed" in region "Import" (on the mock with "The body is not a zip archive."); "Check" and "Import" are enabled again.
2. Set the zip, uncheck "Configurations" among the Import kinds and click "Check" → Banner "Checked: nothing was written"; description "<file>: 1 to create, 0 unchanged, 1 failed."
3. Read "Summary" → No "Configurations" row; "Annotations" with To create 1 and Fails 1; "Total" with Excluded files 1 and Ignored files 1.
4. Read the row `rst.<b>` → Outcome "Fails", code "InvalidDocument".
5. Click the chip "Fails 1", then "Clear the filter" → First only the `rst.<b>` row; then both rows again.

##### MOD-EXI-06 A schema that breaks a configuration fails the import unless forced
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `ModelingExportImportTests.Import_BreakingSchema_FailsUnlessForced`
- **Preconditions** Administrator or Owner of the target project. Target view. Zips X (key `rst.<x>`) and Y (key `rst.<y>`), each with `annotations/<key>.json` `{"Title":"AT force <run>"}`, `configurations/<key>.conf.json` `{"retries":5}` and `schemas/<key>.schema.json` `{"type":"object","properties":{"retries":{"type":"integer","maximum":1}}}`.
1. Set zip X and click "Import" → Banner "Imported"; the schema row has kind "Annotation schema", outcome "Failed", code "SchemaViolation" and "The configuration of rst.<x> violates this schema."; the configuration row is "Created".
2. Check "Store schemas and configurations that break each other", set zip Y and click "Import" → The schema row is "Created" with the warning "SchemaViolation" "Force stores this schema although the configuration of rst.<y> violates it."; chip "Warnings 2".
3. Follow the link of the `rst.<y>` schema row → `…/modeling/schemas/annotation/rst.<y>`; the editor shows `"maximum": 1`.
4. Open `…/modeling/schemas/annotation/rst.<x>` → No schema was stored: the starter document with "Create".

##### MOD-EXI-07 The toaster reports an import that ended after leaving the page
- **Priority** Low · **Role** Admin · **Mode** B · **Test** `ModelingExportImportTests.Import_EndedElsewhere_ToastShowsResult`
- **Preconditions** Administrator or Owner of the target project. Target view. A zip with `annotations/rst.<r>.json` `{"Title":"AT toast <run>"}`.
1. Set the zip, click "Import" and, as soon as the progress status shows, navigate to "Annotations" → URL `…/{view}/annotations`; the textbox "Filter annotations" is visible.
2. Wait for the toast → Toast "Imported <file>" with "1 created, 0 unchanged" and the action "Show the result".
3. Click "Show the result" → URL `…/modeling/export-import`; "Latest import" with the banner "Imported" and the row `rst.<r>` "Created".

##### MOD-EXI-08 Overwrite, the default, updates an existing document
- **Priority** High · **Role** Admin · **Mode** B · **Test** `ModelingExportImportTests.Import_OverwriteExisting_UpdatesDocument`
- **Preconditions** Administrator or Owner of the target project. Target view. The test builds two zips in memory with the same entry `annotations/rst.<a>.json`: zip A `at-<run>-overwrite-a.zip` with `{"Title":"First <run>"}` and zip B `at-<run>-overwrite-b.zip` with `{"Title":"Second <run>"}`. Only the new key `rst.<a>` is written.
1. Open `…/modeling/export-import` → In region "Import", under "Existing documents", the radio "Overwrite" is checked and "Skip" is not; the text "The archive's content replaces them as a new version. Annotations are updated, and deleted ones are re-created with an empty configuration." shows.
2. Set zip A on the Import file input and click "Import" → Banner "Imported"; description "at-<run>-overwrite-a.zip: 1 created, 0 unchanged."; row `rst.<a>` with outcome "Created".
3. Set zip B, leave "Overwrite" selected and click "Check" → Banner "Checked: nothing was written"; description "at-<run>-overwrite-b.zip: 1 to update, 0 unchanged."; the banner offers "Import now".
4. Read "Summary" → Row "Annotations" with To create 0 and To update 1; row "Total" with To update 1, Excluded files 0 and Ignored files 0.
5. Read "Documents" → Row `rst.<a>`: kind "Annotation", outcome "To update", and the link `rst.<a>` (href `…/{view}/annotations/rst.<a>`), because the document exists.
6. Click "Import now" in the banner → Banner "Imported"; description "at-<run>-overwrite-b.zip: 1 updated, 0 unchanged."; row `rst.<a>` with outcome "Updated"; no "Import now".
7. Follow the link `rst.<a>` → URL `…/annotations/rst.<a>`; the textbox "Title" has "Second <run>".

#### Deleted (ModelingDeletedTests)

##### MOD-DEL-01 Deleted lists the root of a deletion and Show cascaded adds its descendants
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ModelingDeletedTests.Deleted_ShowCascaded_ListsDescendants`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates `cnt.<s>.<c>` (which creates `sbt.<s>`) and deletes `sbt.<s>` in the Annotations section; its context goes with it.
1. Open `…/modeling/deleted` → Row `sbt.<s>` (Descendants 1 from 768 px); no row `cnt.<s>.<c>`.
2. Turn on "Show cascaded" → The URL contains `cascaded=1`; column "Root"; the `sbt.<s>` row has the badge "root"; the row `cnt.<s>.<c>` shows `sbt.<s>` as its root.
3. Turn "Show cascaded" off → `cascaded` leaves the URL; the `cnt.<s>.<c>` row is gone.
4. Click "Show sbt.<s>" → Dialog `sbt.<s>` with "The deleted annotation, read-only, and its history."; the banner "sbt.<s> is deleted"; "Annotation document" contains `sbt.<s>`.
5. Close the dialog (Escape) → The dialog closes; the row `sbt.<s>` is still listed.

##### MOD-DEL-02 Restore a deletion with and without its descendants
- **Priority** High · **Role** Editor · **Mode** B · **Test** `ModelingDeletedTests.Deleted_Restore_WithAndWithoutDescendants`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates `cnt.<s1>.<c1>` and `cnt.<s2>.<c2>` (with their subjects) and deletes `sbt.<s1>` and `sbt.<s2>` in the Annotations section.
1. Open `…/modeling/deleted` → Rows `sbt.<s1>` and `sbt.<s2>`; no row for either context.
2. Open "Actions for sbt.<s1>" and choose "Restore" → Toast "sbt.<s1> restored"; the row is gone.
3. Open "Actions for sbt.<s2>" and choose "Restore without descendants" → Toast "sbt.<s2> restored"; the `sbt.<s2>` row is gone; the row `cnt.<s2>.<c2>` appears.
4. Turn on "Show cascaded" → `cnt.<s2>.<c2>` has the badge "root"; no row for `sbt.<s1>` or `cnt.<s1>.<c1>`.
5. Open the annotation `cnt.<s1>.<c1>` → URL `…/annotations/cnt.<s1>.<c1>`; no banner "cnt.<s1>.<c1> is deleted"; the textbox "Title" is visible.

##### MOD-DEL-03 Bulk restore of selected deletions
- **Priority** Low · **Role** Editor · **Mode** B · **Test** `ModelingDeletedTests.Deleted_BulkRestore_RestoresSelection`
- **Preconditions** Contributor or higher in the target project. Target view. The test bulk creates `sbt.<a>` and `sbt.<b>` and deletes both in the Annotations section.
1. On `…/modeling/deleted` check "Select sbt.<a>" and "Select sbt.<b>" → Toolbar "Bulk actions" with "2 selected".
2. Click "Clear selection" → The toolbar is gone; both checkboxes are unchecked.
3. Check both again and click "Restore" in "Bulk actions" → Toast "2 annotations restored"; both rows and the toolbar are gone.
4. Open the annotation `sbt.<a>` → URL `…/annotations/sbt.<a>`; no banner "sbt.<a> is deleted"; the textbox "Title" is visible.

##### MOD-DEL-04 Delete permanently needs the typed key, or the typed count for several
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `ModelingDeletedTests.Deleted_DeletePermanently_NeedsTypedConfirmation`
- **Preconditions** Administrator or Owner of the target project. Target view. The test bulk creates `cnt.<s>.<c>` (with `sbt.<s>`), `sbt.<q1>` and `sbt.<q2>` and deletes `sbt.<s>`, `sbt.<q1>` and `sbt.<q2>` in the Annotations section.
1. Open "Actions for sbt.<s>" and choose "Delete permanently" → Dialog "Delete sbt.<s> permanently?" with "The annotation, its 1 descendant and their change history are removed for good. Their configuration versions stay until they expire."; its "Delete permanently" is disabled.
2. Type `sbt.<s>` in "Type sbt.<s> to confirm" → "Delete permanently" is enabled.
3. Click "Delete permanently" in the dialog → Toast "sbt.<s> deleted permanently"; no row `sbt.<s>`, and with "Show cascaded" no row `cnt.<s>.<c>` either.
4. Check "Select sbt.<q1>" and "Select sbt.<q2>" and click "Delete permanently" in "Bulk actions" → Dialog "Delete 2 annotations permanently?" with "The 2 annotations, their descendants and their change history are removed for good. Their configuration versions stay until they expire."
5. Type `2` in "Type 2 to confirm" and click "Delete permanently" → Toast "2 annotations deleted permanently"; both rows are gone.

#### Compare Views (ModelingCompareTests)

##### MOD-CMP-01 Compare views is disabled without another view, then preselects it and shows the difference
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `ModelingCompareTests.CompareViews_OtherView_PreselectedAndDiffed`
- **Preconditions** Owner of the target organization. The test creates a disposable project (only the default view) and works in it.
1. Open `…/<project>/default/modeling/schemas` → Tab "Compare views" is disabled; "Schemas" is enabled.
2. Open `…/<project>/default/modeling/compare` → "No other view to compare with" with "The project has only this view. An Administrator can create another one on the Views page."; no "Compare a configuration across views".
3. On the project's Views page click "New view", enter the name `<v>` and click "Create view" → Toast "View <v> created".
4. Bulk create `rst.<r>` in default and in `<v>`; save its configuration `{"atCompare":"<run>-default","shared":1}` in default and `{"atCompare":"<run>-other","shared":1}` in `<v>` → Each save shows the toast "Configuration saved (v1)".
5. Open `…/<project>/default/modeling/compare` → Tab "Compare views" is enabled; the URL gains `to=<v>`; the combobox "Compare default with" shows `<v>`; "Compare a configuration across views".
6. Pick `rst.<r>` in the combobox "Annotation key" (placeholder "Pick a key") → The URL has `key=rst.<r>`; the diff summary "+1 −1 · 1 change"; both sides contain `atCompare` (on a desktop the labels "default" and `<v>`).
7. Open `…/modeling/compare?to=default&key=rst.<r>` → `to` is replaced by `<v>`; the diff of default with `<v>` shows (never default with itself).

#### Not automated
- A template feeds a new configuration through "Start from template": owned by the Configurations section, CFG-CRE-03 (Start from template copies the type template, and Create empty still inherits it).
- Dropping a zip on "Drop a zip here" from the operating system: Playwright cannot do a real OS file drop, and the drop zone filters by MIME type; the tests set the files on input[type=file].
- The native beforeunload prompt when a tab with a dirty editor is closed or reloaded: a native browser dialog that differs per engine and that Playwright dismisses; the in-app "Discard changes?" guard is covered by MOD-TAB-06.
- An export still running across a reload (pending exports resumed from sessionStorage), and an import that ends during a reload: depends on timing; mock export jobs live in memory and answer "The job has expired." after a reload, and live job times cannot be controlled.
- The 15-minute waiting limit of transfers ("The job did not end within 15 minutes; it may still end on the server.") and the 24-hour job expiry: they need real waiting time or a server that never finishes.
- Server limits of the import (413 ArchiveTooLarge, 409 TransferLimit, central-directory limits, the 1,000-item truncation) and "Show all <n>" past 200 documents: the mock does not enforce the limits, and on a live target they need huge archives, hundreds of created documents or parallel jobs on a shared project.
- Expired versions of schemas and templates (badge "expired", "This version is not available", "A version is no longer available"): mock versions are at most 9 days old, live history expires after 365 days, and the clock cannot be moved.
- The contents of a real Git export (one commit per version, by its author): the mock writes only .git/HEAD, and inspecting git objects inside a zip is outside the UI; only the name and .git/HEAD are checked (MOD-EXI-02).
- The red "Purged" date when fewer than 3 days are left, and the automatic purge: they need a deletion close to its purge date (90 days); the clock cannot be moved.
- Error states and failure toasts: list and detail error states with "Retry", "The schema was not saved", "The template was not saved", "Delete failed", "Restore failed", "<f> of <n> were not restored", "<n> could not be deleted permanently", "The export failed" and the toaster's "The import failed" for a failed job. They need 4xx/5xx fault injection, which neither the mock build nor a live target offers. The refused upload "The import failed" is covered by MOD-EXI-05.
- The 409 concurrency ErrorResponse of a schema PUT after the server's retries: a race inside the API that cannot be reproduced on purpose; the mock never answers it.
- Behaviour of exactly Administrator or exactly ContributorWithSecrets (for example an Administrator who is not Owner sees "Export & Import" and "Delete permanently"): the mock has no project with these exact roles; Owner stands in (MOD-TAB-01, MOD-EXI-*, MOD-DEL-04).
- The Import fallback alert "Importing needs the Administrator role": unreachable, because the page lock "Administrators only" shows first (MOD-TAB-04).
- YAML editing mode, the binding reference and completions of the template editor, and the DiffEditor controls ("Auto", "Split", "Unified", "Previous change", "Next change", "Collapse unchanged"): shared editor components owned by the Configurations section, which tests them on the configuration editor: the YAML mode in CFG-EDT-03 and the completions and binding reference in CFG-BND-03; the DiffEditor view options are excluded there too. Modeling asserts the documents and the diff summary only.
- The detail panel's pin, full screen, resize and Esc to close, the sidebar entry, the command palette entries and the shortcuts to Modeling: owned by the Navigation section, the detail panel in NAV-DET-01 to NAV-DET-03 (NAV-DET-01 opens a Modeling template detail pinned), the menu per role in NAV-MNU-03 to NAV-MNU-05, the palette in NAV-MNU-03 and NAV-PAL-01, and the shortcuts in NAV-KEY-01.
- The Views page's "Compare" button (its tooltips and its link `…/default/modeling/compare?to={v}`): owned by the Organizations and Projects section (ORG-VEW-01, and ORG-VEW-05 for a Contributor). MOD-CMP-01 opens Compare views by its URL and owns what the tab shows.
- Restore from the details slideover's banner ("Restore", "More restore options"): the banner is the Annotations section's DeletedAnnotationBanner, covered by ANN-DEL-02, and the action is the same as the row menu (MOD-DEL-02).
- Comparing two picked versions with the "Compare" / "with" selects: the same diff as "Compare with previous" (MOD-SCH-07, MOD-TPL-01), so it is redundant.
- Layout checks (Export and Import side by side or stacked, the tab scroll position, the detail width): visual checks covered by the TypeScript suite's visual baselines.

### 7. Graph

The Graph page (`{org}/{project}/{view}/graph`) draws the view's annotations as a network of ancestor-to-descendant links on a WebGL canvas. The tests drive it only through the DOM around the canvas: the toolbar (filter, Layout, Focus depth, Fit, Reheat, Export PNG, Open as list, Focus / Show everything), the legend 'Annotation types', the URL (`focus`, `depth`, `layout`, `q`), the keyboard cursor with its live region, the summary in the graph's accessible name, and the node detail at `graph/{key}`. On a live target the view's content is unknown and keeps growing, so every case works in focus mode on a tree it creates: Bulk create `exe.{s}.{r}.{c}` also creates the missing ancestors `rst.{r}`, `sbt.{s}`, `usg.{s}.{r}` and `cnt.{s}.{c}` ("the tree"; `{s}`, `{r}`, `{c}` are generated name parts that carry the run id). `T` stands for the view's total number of annotations, matched as `[\d,]+`. The empty-graph cases use a view name that was never used (read only), or a disposable project (mode P) when they write. All tests live in the folder `Tests/Graph` with `[Area(Area.Graph)]`; `Area.Graph` is a new value of the Area enum (`Attributes/Area.cs`). The sub-folders are Overview (`GraphOverviewTests`), Legend (`GraphLegendTests`), Focus and Filter (`GraphFocusTests`), Toolbar (`GraphToolbarTests`), Node Detail (`GraphNodeDetailTests`), Create (`GraphCreateTests`) and Remembered State (`GraphMemoryTests`). The node detail is the annotation detail of the Annotations section, so the graph cases check only what the graph adds: links that stay on the graph and keep its query, and the summary and legend after a delete and its Undo. GRA-DET-04 owns the old `graph/{key}/*` tab links. The Elementary case is GRA-OVR-01. It focuses the graph on a created tree, checks the neighbourhood's summary and legend, and opens a node with the keyboard. It runs the same way on desktop and phone.

#### Overview (GraphOverviewTests)

##### GRA-OVR-01 Graph focused on a created tree shows its neighbourhood and opens a node by keyboard
- **Priority** Elementary · **Role** Editor · **Mode** B · **Test** `GraphOverviewTests.Graph_FocusOnCreatedTree_OpensNodeByKeyboard`
- **Preconditions** Editor (Contributor or higher) in the target view (mock: acme/billing/default). On Annotations, the test first bulk-creates `exe.{s}.{r}.{c}`, which also creates rst.{r}, sbt.{s}, usg.{s}.{r} and cnt.{s}.{c} (the tree).
1. On Annotations, Bulk create `exe.{s}.{r}.{c}` and click 'Done' → The result reads 'Created 5 annotations: 1 from the list and 4 missing ancestors.' and the dialog closes.
2. Open `{view}/graph?focus=sbt.{s}` → The graph's name starts with 'Annotation graph, 5 of T annotations around sbt.{s} (depth 2).'. The focus badge shows sbt.{s}, 'Focus depth' is 2 and 'Show everything' is shown.
3. Look at the legend 'Annotation types' (on a phone, click 'Types' first and close the popover with Escape) → It has 7 chips, all pressed. Responsibilities, Subjects, Usages, Contexts and Executions count 1; Units and Units of execution count 0.
4. Wait until the graph shows no loading status, focus the graph and press ArrowDown → The live region reads 'Subject {s}, 2 connections'.
5. Press Enter → The URL is `{view}/graph/sbt.{s}` and keeps focus=sbt.{s}. The detail's identity shows sbt.{s}.

##### GRA-OVR-02 Arrow keys move along the links, Enter opens the annotation and Escape closes it
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `GraphOverviewTests.Graph_ArrowKeys_MoveAlongLinksAndOpenNode`
- **Preconditions** Editor in the target view. Reduced motion is emulated before the first page load. On Annotations, the test first bulk-creates `exe.{s}.{r}.{c}` (the tree).
1. Open `{view}/graph?focus=sbt.{s}`, wait for the layout and focus the graph → The summary reads '5 of T annotations around sbt.{s} (depth 2)' and the graph has the keyboard focus.
2. Press ArrowDown → The live region reads 'Subject {s}, 2 connections'.
3. Press ArrowDown → The live region reads 'Context {c}, 2 connections' or 'Usage {r}, 3 connections' (a connected node one tier lower).
4. Press ArrowDown → The live region reads 'Execution {c}, 2 connections'.
5. Press Enter → The URL is `{view}/graph/exe.{s}.{r}.{c}` and keeps focus=sbt.{s}. The identity shows exe.{s}.{r}.{c}.
6. Press Escape → The URL is `{view}/graph?focus=sbt.{s}` (no key). The identity is gone, the graph is visible and the summary reads '5 of T annotations around sbt.{s} (depth 2)'.

##### GRA-OVR-03 On a phone the legend is a Types popover, the toolbar is icon-only and a node's detail covers the graph
- **Priority** Medium · **Role** Editor · **Mode** B,Ph · **Test** `GraphOverviewTests.Graph_PhoneLayout_CollapsesLegendAndCoversGraph`
- **Preconditions** Editor in the target view, mobile profile (Pixel 7, 412 px). The test first creates the tree `exe.{s}.{r}.{c}` on Annotations (Bulk create from the command palette).
1. Open `{view}/graph?focus=sbt.{s}` → A 'Types' button replaces the group 'Annotation types'. The top bar has the icon button 'New annotation' but neither 'New' nor 'Bulk create'.
2. Look at the toolbar → 'Fit', 'Reheat', 'Export PNG' and 'Open as list' are icon-only buttons with no visible text. 'Layout', 'Focus depth' and 'Show everything' are shown.
3. Click 'Types' → A popover opens with the group 'Annotation types' and its 7 chips, all pressed.
4. Click 'Hide subjects', then press Escape → The popover closes and the button reads 'Types (6/7)'. The summary reads '4 of T annotations around sbt.{s} (depth 2)'.
5. Open 'Types', click 'All types' and press Escape → The button reads 'Types' and the summary reads '5 of T annotations around sbt.{s} (depth 2)'.
6. Focus the graph, then press ArrowDown and Enter → The URL is `{view}/graph/sbt.{s}?focus=sbt.{s}`. The detail fills the screen with 'Back to the list' (no 'Close the detail'), and the graph is hidden.
7. Click 'Back to the list' → The URL is `{view}/graph?focus=sbt.{s}`. The graph and 'Types' are visible again, and the summary reads '5 of T annotations around sbt.{s} (depth 2)'.

#### Legend (GraphLegendTests)

##### GRA-LEG-01 Hiding types removes their nodes, survives a catalog reload, and the legend brings them back
- **Priority** High · **Role** Editor · **Mode** B · **Test** `GraphLegendTests.GraphLegend_HideTypes_RemovesNodesUntilShownAgain`
- **Preconditions** Editor in the target view. The test first creates the tree `exe.{s}.{r}.{c}` on Annotations. On a phone, every chip is used in the 'Types' popover.
1. Open `{view}/graph?focus=sbt.{s}` → The summary reads '5 of T annotations around sbt.{s} (depth 2)' and all 7 chips are pressed.
2. Click 'Hide executions' → The chip is no longer pressed and its title is now 'Show executions'. Executions still counts 1 and 'All types' appears. The summary reads '4 of T annotations around sbt.{s} (depth 2)', and on a phone the button reads 'Types (6/7)'.
3. Click 'Reload the catalog' and wait until it is enabled again → The chip 'Show executions' is still not pressed (on a phone the button still reads 'Types (6/7)'), and the summary still reads '4 of T annotations around sbt.{s} (depth 2)'.
4. Click every chip that is still pressed → The heading 'Every type is hidden' appears with 'Turn types back on in the legend.'. The summary reads '0 of T annotations around sbt.{s} (depth 2)', and on a phone the button reads 'Types (0/7)'.
5. Click 'Show contexts' → The state is gone and the summary reads '1 of T annotations around sbt.{s} (depth 2)'.
6. Click 'All types' → All 7 chips are pressed and 'All types' is gone. The summary reads '5 of T annotations around sbt.{s} (depth 2)'.

##### GRA-LEG-02 Enter on a focused legend chip toggles its type while a node is open
- **Priority** Low · **Role** Editor · **Mode** B,D · **Test** `GraphLegendTests.GraphLegend_EnterOnChip_TogglesTypeWithNodeOpen`
- **Preconditions** Editor in the target view, desktop. The test first creates the tree `exe.{s}.{r}.{c}` on Annotations.
1. Open `{view}/graph/sbt.{s}?focus=sbt.{s}` → The identity shows sbt.{s}. The graph beside the detail has the chip 'Hide subjects', pressed.
2. Focus the chip 'Hide subjects' and press Enter → The chip's title is now 'Show subjects', with aria-pressed false. The summary reads '4 of T annotations around sbt.{s} (depth 2)'. The URL is still `{view}/graph/sbt.{s}` and the detail stays open.
3. Press Enter again → The chip 'Hide subjects' is pressed again and the summary reads '5 of T annotations around sbt.{s} (depth 2)'. The URL is unchanged.

#### Focus and Filter (GraphFocusTests)

##### GRA-FOC-01 Focus starts from the last opened annotation, the depth changes the neighbourhood and Show everything leaves it
- **Priority** High · **Role** Editor · **Mode** B · **Test** `GraphFocusTests.GraphFocus_FocusAndDepth_ChangeNeighbourhood`
- **Preconditions** Editor in the target view, fresh browser context (no recently opened annotations). The test first creates the tree `exe.{s}.{r}.{c}` on Annotations.
1. Open `{view}/graph/cnt.{s}.{c}` → The identity shows cnt.{s}.{c}.
2. Click 'Close the detail' ('Back to the list' on a phone) → The URL is `{view}/graph` without a key. 'Focus' is shown and 'Focus depth' is not.
3. Click 'Focus' → The URL has focus=cnt.{s}.{c} and the summary reads '4 of T annotations around cnt.{s}.{c} (depth 2)'. The badge shows cnt.{s}.{c}, 'Focus depth' is 2, and 'Show everything' replaces 'Focus'.
4. Click 'Decrement' next to 'Focus depth' → The URL has depth=1 and the summary reads '3 of T annotations around cnt.{s}.{c} (depth 1)'.
5. Click 'Increment' twice → The URL has depth=3 and the summary reads '5 of T annotations around cnt.{s}.{c} (depth 3)'.
6. Click 'Show everything' → The URL has no focus. 'Focus' is back, with no 'Focus depth' and no badge, and the summary reads 'T annotations'.
7. Go back in the browser → The URL has focus=cnt.{s}.{c} again with depth=3. The summary reads '5 of T annotations around cnt.{s}.{c} (depth 3)'.

##### GRA-FOC-02 The filter adds its matches to the focus and keeps every node of the whole view
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `GraphFocusTests.GraphFocus_Filter_AddsMatchesAndKeepsWholeView`
- **Preconditions** Editor in the target view. On Annotations, the test first bulk-creates `exe.{s}.{r}.{c}` and an unconnected `rst.{r2}`: 'Created 6 annotations: 2 from the list and 4 missing ancestors.'
1. Open `{view}/graph?focus=sbt.{s}` → The summary reads '5 of T annotations around sbt.{s} (depth 2)' and the filter count shows T.
2. Press '/' and type `key:rst.{r2}` → 'Filter annotations' has the focus and the URL has q=key:rst.{r2}. The filter count reads '1 of T' and the summary '6 of T annotations around sbt.{s} (depth 2 + filter matches)'. Responsibilities counts 2.
3. Click 'Show everything' → The URL keeps q and has no focus. The summary reads 'T annotations', because the nodes that do not match are dimmed, not removed. The filter count still reads '1 of T'.
4. Click 'Clear the filter' → The URL has no q. The filter count shows T and the summary still reads 'T annotations'.

#### Toolbar (GraphToolbarTests)

##### GRA-TBR-01 Layout, Fit and Reheat redraw the graph without changing its annotations, and Export PNG downloads it
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `GraphToolbarTests.GraphToolbar_LayoutAndExport_KeepNodesAndDownloadPng`
- **Preconditions** Editor in the target view. The test first creates the tree `exe.{s}.{r}.{c}` on Annotations.
1. Open `{view}/graph?focus=sbt.{s}&layout=radial` → 'Layout' shows 'Radial' and the summary reads '5 of T annotations around sbt.{s} (depth 2)'.
2. Choose 'Force' in 'Layout' → The URL has layout=force and the summary still reads '5 of T annotations around sbt.{s} (depth 2)'.
3. Choose 'Layered' in 'Layout' → The URL has no layout and 'Layout' shows 'Layered'. The summary still reads '5 of T annotations around sbt.{s} (depth 2)'.
4. Click 'Fit', then 'Reheat' → The URL still has focus=sbt.{s} and no layout, the summary still reads '5 of T annotations around sbt.{s} (depth 2)', and no toast appears.
5. Wait until the graph shows no loading status and click 'Export PNG' → The browser downloads a file named `graph-{org}-{project}-{view}.png`.
6. Read the downloaded file → It is not empty and starts with the PNG signature 89 50 4E 47 0D 0A 1A 0A. No toast 'The graph could not be exported' appears.

##### GRA-TBR-02 Open as list shows the drawn annotations in the grid
- **Priority** High · **Role** Editor · **Mode** B · **Test** `GraphToolbarTests.GraphToolbar_OpenAsList_ShowsDrawnAnnotationsInGrid`
- **Preconditions** Editor in the target view. The test first creates the tree `exe.{s}.{r}.{c}` on Annotations.
1. Open `{view}/graph?focus=sbt.{s}` → The summary reads '5 of T annotations around sbt.{s} (depth 2)' and 'Open as list' is enabled.
2. Click 'Hide executions' (on a phone, in the 'Types' popover) → The summary reads '4 of T annotations around sbt.{s} (depth 2)'.
3. Click 'Open as list' → The URL is `{view}/annotations?q=key:…`, and its keys are rst.{r}, sbt.{s}, usg.{s}.{r} and cnt.{s}.{c} (no exe.{s}.{r}.{c}).
4. Look at the annotations page → 'Filter annotations' holds that query and the count reads '4 of T'. There is a row (an item on a phone) for each of the four keys, and none for exe.{s}.{r}.{c}.

#### Node Detail (GraphNodeDetailTests)

##### GRA-DET-01 A node's detail opens beside the graph, and its tabs, breadcrumb and close keep the graph's query
- **Priority** High · **Role** Editor · **Mode** B · **Test** `GraphNodeDetailTests.GraphDetail_OpenByUrl_KeepsGraphQueryOnTabsAndClose`
- **Preconditions** Editor in the target view. The test first creates the tree `exe.{s}.{r}.{c}` on Annotations.
1. Open `{view}/graph/exe.{s}.{r}.{c}?focus=sbt.{s}&layout=force` → The detail opens with identity exe.{s}.{r}.{c} and the tabs 'Annotation' and 'History'. On a desktop the graph stays visible beside it, with 'Close the detail'. On a phone the graph is hidden and the detail has 'Back to the list'.
2. Look at the breadcrumb 'Annotation ancestors' → It has the links 'Subject {s}' and 'Context {c}', then the current 'Execution {c}'.
3. Click 'History' → The URL is `{view}/graph/exe.{s}.{r}.{c}/history` and keeps focus=sbt.{s} and layout=force. The region 'History' is shown.
4. Click 'Context {c}' in the breadcrumb → The URL is `{view}/graph/cnt.{s}.{c}` with the same query, and the identity shows cnt.{s}.{c}.
5. Click 'Close the detail' ('Back to the list' on a phone) → The URL is `{view}/graph?focus=sbt.{s}&layout=force` and the detail is gone. The summary reads '5 of T annotations around sbt.{s} (depth 2)' and 'Layout' shows 'Force'.

##### GRA-DET-02 Lineage, Show in list and Show in graph move between the graph and the list
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `GraphNodeDetailTests.GraphDetail_LineageAndShowInList_SwitchListAndGraph`
- **Preconditions** Editor in the target view. The test first creates the tree `exe.{s}.{r}.{c}` on Annotations.
1. Open `{view}/graph/exe.{s}.{r}.{c}` → The identity shows exe.{s}.{r}.{c}.
2. Click 'Lineage' → A popover 'Ancestors, applied top to bottom' opens with the links rst.{r}, sbt.{s}, usg.{s}.{r} and cnt.{s}.{c}.
3. Click the link sbt.{s} → The URL is `{view}/graph/sbt.{s}` and the identity shows sbt.{s}.
4. Click 'More actions' → The menu has 'Show in list', 'Copy link', 'Copy key' and 'Delete'.
5. Choose 'Show in list' → The URL is `{view}/annotations/sbt.{s}` and the identity shows sbt.{s}.
6. Click 'More actions' and choose 'Show in graph' → The URL is `{view}/graph/sbt.{s}?focus=sbt.{s}` and the identity shows sbt.{s}.
7. Click 'Close the detail' ('Back to the list' on a phone) → The URL is `{view}/graph?focus=sbt.{s}` and the summary reads '5 of T annotations around sbt.{s} (depth 2)'.

##### GRA-DET-03 Deleting a node from its detail removes it from the graph, and Undo brings it back
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `GraphNodeDetailTests.GraphDetail_DeleteAndUndo_RemovesAndRestoresNode`
- **Preconditions** Editor in the target view. On Annotations, the test first bulk-creates only `cnt.{s}.{c}`: 'Created 2 annotations: 1 from the list and 1 missing ancestor.' (cnt.{s}.{c} and sbt.{s}). It deletes only cnt.{s}.{c}, which it created and which has no descendants.
1. Open `{view}/graph/cnt.{s}.{c}?focus=sbt.{s}` → The identity shows cnt.{s}.{c}.
2. Click 'More actions' and choose 'Delete' → The dialog 'Delete cnt.{s}.{c}?' opens with 'Cancel' and 'Delete'.
3. Click 'Delete' in the dialog → The toast 'cnt.{s}.{c} deleted' appears with 'Undo'. The URL is `{view}/graph?focus=sbt.{s}`, with no key, and the summary reads '1 of T annotations around sbt.{s} (depth 2)'.
4. Look at the legend (on a phone, click 'Types', then click 'Types' again to close it) → Contexts counts 0 and Subjects counts 1.
5. Click 'Undo' in the toast → The URL is `{view}/graph/cnt.{s}.{c}?focus=sbt.{s}` and the identity shows cnt.{s}.{c}.
6. Click 'Close the detail' ('Back to the list' on a phone) → The URL is `{view}/graph?focus=sbt.{s}`. The summary reads '2 of T annotations around sbt.{s} (depth 2)' and Contexts counts 1 (on a phone, in the 'Types' popover).

##### GRA-DET-04 An unknown node shows Annotation not found, and the old tab links of a node redirect to its configuration with their query
- **Priority** Low · **Role** Any · **Mode** B · **Test** `GraphNodeDetailTests.GraphDetail_UnknownKeyAndOldTabs_ShowNotFoundAndRedirect`
- **Preconditions** Any pool user in the target view. The test uses a generated key `{missing}` (sbt.…) that was never created; nothing is written. Every step is a page load.
1. Open `{view}/graph/{missing}` → The heading 'Annotation not found' reads '{missing} does not exist in this view. It may have been deleted permanently.'
2. Open `{view}/graph/{missing}/configuration?layout=force` → The URL is `{view}/configurations/{missing}` (the Edit tab), without layout.
3. Open `{view}/graph/{missing}/configuration?cfg=effective&q=type:sbt` → The URL is `{view}/configurations/{missing}/effective?q=type:sbt`.
4. Open `{view}/graph/{missing}/configuration?cfg=resolved` → The URL is `{view}/configurations/{missing}/resolved`.
5. Open `{view}/graph/{missing}/schema?q=type:sbt` → The URL is `{view}/configurations/{missing}/schema?q=type:sbt`.
6. Open `{view}/graph/{missing}/versions?v=2&from=1` → The URL is `{view}/configurations/{missing}/versions?v=2&from=1`.

#### Create (GraphCreateTests)

##### GRA-CRE-01 New in the Graph top bar creates an annotation and opens it in the graph
- **Priority** High · **Role** Editor · **Mode** B · **Test** `GraphCreateTests.GraphCreate_NewInTopBar_OpensAnnotationInGraph`
- **Preconditions** Editor in the target view. The test uses a generated subject name {s}; sbt.{s} does not exist yet.
1. Open `{view}/graph?layout=force` and wait for the catalog → 'Reload the catalog' is enabled. The top bar has 'New' and 'Bulk create'; on a phone it has only the icon button 'New annotation'.
2. Click 'New' ('New annotation' on a phone) → The dialog 'New annotation' opens and 'Type' shows 'Execution'.
3. Choose 'Subject' in 'Type' and fill 'Name' with {s} → The key preview shows sbt.{s} and 'Create' is enabled.
4. Click 'Create' → A toast 'sbt.{s} created' appears and the dialog closes. The URL is `{view}/graph/sbt.{s}` and keeps layout=force. The identity shows sbt.{s}.
5. Close the detail and click 'Focus' → The URL has focus=sbt.{s} and the summary reads '1 of T annotations around sbt.{s} (depth 2)'. The badge shows sbt.{s}.

##### GRA-CRE-02 The empty graph of a new project offers New annotation and Bulk create, and Bulk create draws the tree
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `GraphCreateTests.GraphCreate_BulkCreateInEmptyProject_DrawsTree`
- **Preconditions** Owner of the target organization. With the organization overview's create project, the test first creates a disposable project {p}, and it works only in that project's default view.
1. Open `{org}/{p}/default/graph` → The heading 'No annotations in this view' reads 'The graph shows annotations and their ancestors once the view has some.'. There is no group 'Annotation types' and no 'Types' button.
2. Look at the graph → 'New annotation' and 'Bulk create' are inside the graph, and 'Open as list' is disabled.
3. Click 'Bulk create' inside the graph → The dialog 'Bulk create annotations' opens.
4. Enter `exe.{s}.{r}.{c}` in 'Keys' and click 'Create 1 annotation' → The result reads 'Created 5 annotations: 1 from the list and 4 missing ancestors.'
5. Click 'Done' → The dialog closes and the URL is still `{org}/{p}/default/graph`. The empty state is gone and the summary reads '5 annotations'. The legend shows Responsibilities, Subjects, Usages, Contexts and Executions with 1 each.
6. Click 'Focus' → The URL has focus=sbt.{s}, the most connected subject or responsibility. The summary reads '5 of 5 annotations around sbt.{s} (depth 2)'.

##### GRA-CRE-03 A Reader has no create actions on Graph
- **Priority** Medium · **Role** Reader · **Mode** B · **Test** `GraphCreateTests.GraphCreate_Reader_HasNoCreateActions`
- **Preconditions** Reader of the project mapped in Target:RoleProjects (mock: northwind/portal, where Ada is a Reader). Nothing is created.
1. Open `{view}/graph` → 'Reload the catalog' is enabled. The graph shows its summary 'T annotations', or 'No annotations in this view'.
2. Look at the top bar → It has no 'New', 'New annotation' or 'Bulk create'.
3. Open the graph of a view name that was never used (`{org}/{project}/{unused}/graph`) → The heading 'No annotations in this view' is shown. There is no button inside the graph and no legend.

##### GRA-CRE-04 A create dialog left open on Graph does not reopen on Annotations after Back
- **Priority** Low · **Role** Editor · **Mode** B · **Test** `GraphCreateTests.GraphCreate_DialogLeftOpen_DoesNotReopenOnAnnotations`
- **Preconditions** Editor in the target view. Nothing is created.
1. Open `{view}/annotations` and follow 'Graph' in the navigation → The URL is `{view}/graph`.
2. Click 'New' ('New annotation' on a phone) → The dialog 'New annotation' opens.
3. Go back in the browser → The URL is `{view}/annotations`, 'Reload the catalog' is enabled and no dialog 'New annotation' is open.
4. Click 'New' ('New annotation' on a phone) on Annotations, then 'Cancel' → The dialog 'New annotation' opens, then closes.

#### Remembered State (GraphMemoryTests)

##### GRA-MEM-01 Coming back to Graph through the navigation restores its URL state and hidden types until a reload
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `GraphMemoryTests.GraphMemory_SidebarReturn_RestoresStateUntilReload`
- **Preconditions** Editor in the target view. The test first creates the tree `exe.{s}.{r}.{c}` on Annotations. Between steps 2 and 5 it moves only inside the app (no page load).
1. Open `{view}/graph?focus=sbt.{s}` → The summary reads '5 of T annotations around sbt.{s} (depth 2)'.
2. Choose 'Force' in 'Layout' and click 'Hide contexts' (on a phone, in the 'Types' popover) → The URL has layout=force and the summary reads '4 of T annotations around sbt.{s} (depth 2)'.
3. Follow 'Annotations' in the navigation → The URL is `{view}/annotations` and 'Filter annotations' is empty.
4. Follow 'Graph' in the navigation → The URL is `{view}/graph` with focus=sbt.{s} and layout=force again, and 'Layout' shows 'Force'. 'Show contexts' is not pressed, and the summary reads '4 of T annotations around sbt.{s} (depth 2)'.
5. Follow 'Graph' in the navigation again, while on Graph → The graph URL is plain again, with no focus and no layout. 'Layout' shows 'Layered' and 'Focus' replaces 'Show everything'. The chip 'Show contexts' is still not pressed (on a phone the button still reads 'Types (6/7)').
6. Open `{view}/graph?focus=sbt.{s}` with a page load → All 7 chips are pressed and the summary reads '5 of T annotations around sbt.{s} (depth 2)'.

#### Not automated
- Pointer actions on nodes (hover tooltip, a click to open a node, a double-click to focus, dragging to pin with 'Release pinned nodes', a double-click on the background, a touch long-press): they need hit-testing of nodes on the WebGL canvas at moving coordinates, which the suite cannot do. Opening and focusing are covered through the keyboard cursor, the URL and the Focus button.
- Wheel or pinch zoom, panning, and the visual effect of Fit, Reheat and the three layouts: only canvas pixels change. There is no DOM or URL state to assert, and a pixel comparison of a force layout is not stable.
- Rendering details (type colors per theme, project and personal colors, arrowheads, labels, filter dimming, highlight rings, the WebGL or Canvas 2D fallback): these are drawn on canvases with no DOM, so they cannot be checked functionally.
- The remembered layout frame, zoom, pan and pinned nodes after a return: they are kept in memory and shown only on the canvas. The observable part (the URL and the hidden types) is covered by GRA-MEM-01.
- Large views (automatic focus mode above 5,000 annotations, the 'Show all N annotations?' confirmation, Show everything disabled above 20,000, the pre-warm): they need a view above 5,000 annotations. Creating that many on a live target is out of proportion to the data rule and the run time. The mock has them only in its 'large' scenario, and mock-only cases are kept for flows that need a second identity. The page objects still confirm the dialog when a live view grows above 5,000.
- The whole-view numbers of the mock dataset (367 annotations, the type counts, 'Open as list' disabled above 300 drawn nodes): they rest on seed data, while a live view's size is unknown and keeps growing. Reaching 300 drawn nodes would need 300 own annotations. The whole view is still covered through Show everything (GRA-FOC-01, GRA-FOC-02).
- The catalog error state 'The catalog could not be loaded' with Retry: it needs fault injection. The mock's service worker answers cannot be faulted, and the catalogue has no live-only cases.
- The loading overlay labels ('Loaded N annotations…', 'Laying out N annotations…') and the catalog snapshot shown at once: they are transient and depend on IndexedDB and worker timing.
- Reduced motion turning off the layout animation, and the FPS and performance targets: neither is observable in the DOM, and performance is not a functional check.
- Out-of-range and unknown URL values (depth=0 or 9, an unknown focus key or layout): these are edge cases of URL parsing, of low value for an end-to-end suite. The normal URL state is covered by the focus, toolbar and memory cases.
- Copy link and Copy key in the node's More actions: they are the same items as in the annotation detail and copy the same /annotations link, so the Annotations section owns them (ANN-DET-03).
- Editing in the node detail (the Annotation tab's form, the History content, a deleted key's history with Restore), and concurrent edits (412): the graph hosts the annotation detail and has no editable state of its own. The Annotations section owns these (ANN-FRM-01 to ANN-FRM-08, ANN-DET-05, ANN-DEL-02); its Not automated list explains why an annotation has no 412 conflict to show.
- The delete dialog's text, the deletion toast's description ('With N descendants.', 'Restorable from Deleted until …') and the '{key} restored' toast, when deleting from the node detail: they come from the same AnnotationDetail as on Annotations, which ANN-DEL-01 covers. GRA-DET-03 checks only what the graph adds: the summary and legend after the delete and the node URL after Undo.
- Pinning, full screen and resizing the detail on the graph: this is the shared detail panel, owned by the Navigation section (NAV-DET-01, NAV-DET-02, NAV-DET-03).
- The go-to shortcut 'G G', the menu entry 'Graph' and the command palette's page entries, Graph among them: owned by the Navigation section (NAV-KEY-02, NAV-MNU-01, NAV-PAL-01).
- The accessibility scan (axe) of the graph page: it is covered by the TypeScript accessibility suite. It is not a functional test of this suite.

### 8. Key-Values

Values store (`/{org}/{project}/{view}/key-values`) keeps a view's key/value pairs: public values and secrets that configurations bind as `@key`. This section covers the New key dialog and its validation, the detail panel (editing in place, the binding, the unsaved-changes guard, a concurrent change), secrets and their protection levels, the Versions tab, the list (search, level filter, row menu, context switcher), the lifecycle (delete, restore, Delete permanently, Purge, bulk delete and restore, a New key over a deleted key), Import and Export, and what a Reader and a Contributor may do. It has eight sub-folders, one test class each, in the folder `Tests/KeyValues/` with the attribute `[Area(Area.KeyValues)]`: Create (`KeyValueCreateTests`), Detail (`KeyValueDetailTests`), Secrets (`KeyValueSecretTests`), Versions (`KeyValueVersionTests`), List (`KeyValueListTests`), Lifecycle (`KeyValueLifecycleTests`), Import and Export (`KeyValueImportExportTests`) and Roles (`KeyValueRoleTests`). The Elementary case is KV-CRE-01, the existing Testiny case 4 `KeyValues_CreateAndRead`, which moves from `KeyValuesTests` to `KeyValueCreateTests`; the old class `KeyValuesTests` is deleted, so the test does not exist twice. Most cases run in the target view and only touch keys they create, which they open by URL or right after creating them. Some cases run in the default view of a disposable project X instead (mode P). These are the cases that find their keys in the list (the list loads only the first page of a view, 1,000 keys on a live target, and a live view grows with every run), the cases that need a level outside the default policy (Public and Standard for a new project in the mock and the API; HSM and HYOK are allowed as setup in X's Settings), and the cases that export secret values or Replace on import. HSM depends on the deployment: KV-SEC-04 checks it at run time and skips with the API's message where no HSM provider is configured; on the mock it runs. Network observations (a request body, a request that was not made) read the init-script fetch recorder `window.__requests`, not Playwright request events, so they hold on every engine. The row menu, the selection and the bulk bar exist only from 768 px, so those cases are desktop only (D).

#### Create (KeyValueCreateTests)

The New key dialog: a public key value created and read back (the section's smoke test), the key grammar and the value size limit, a key that exists already, and the secret reference content type with its Reference badge.

##### KV-CRE-01 Create a public key value and read it back
- **Priority** Elementary · **Role** Editor · **Mode** B · **Test** `KeyValueCreateTests.KeyValues_CreateAndRead` · existing Testiny case 4
- **Preconditions** Editor in the target view. Key K = PlatformData.KeyValueKey() (at_<runId>.<word>_<hex4>), value V = PlatformData.Slug("value").

1. Open Values store of the target view → The search box "Search keys and public values" is visible; the header has "Export", "Import" and "New key"
2. Click "New key" → Dialog "New key" with "In {org}/{project}, view {view}."; Level "Public", Content type "Text"
3. Fill Key K and Value V, click "Create" → The dialog closes; the URL is …/key-values/K; the region named K shows the heading K
4. Look at the Value tab → The textbox "Value" shows V; level badge "Public"; Version "v1"; Binding "@K"
5. Click the "Versions" tab → The URL ends with /versions; the row "v1(current)" has Level "Public" and Expires "—"

##### KV-CRE-02 New key validates the key grammar and the value size
- **Priority** High · **Role** Editor · **Mode** B · **Test** `KeyValueCreateTests.KeyValues_NewKeyInvalidInput_ShowsFieldErrors`
- **Preconditions** Editor in the target view. Key K generated, never created.

1. Open Values store, click "New key", then "Create" with an empty Key → Key error "Enter a key."; the dialog stays open
2. Key "1st.key", click "Create" → Key error "Use dot-separated segments of letters, digits and underscores (not starting with a digit), at most 128 characters."
3. Key "db-password", "Create"; then Key "a..b", "Create" → The same Key error each time
4. A Key of 129 characters that follows the grammar ("at_x." and 124 letters), "Create" → The same Key error
5. Key K, a Value of 24,577 characters "x", "Create" → Value error "The value is longer than 24576 bytes."; no Key error; the dialog stays open
6. Click "Cancel" → The dialog closes; the URL is still …/key-values
7. Open …/key-values/K by URL → The region K shows the alert "Not found", "The key 'K' does not exist in view '{view}'." and "Retry"
8. Click "Retry" → The same "Not found" alert

##### KV-CRE-03 New key with the name of an existing key shows a field error
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `KeyValueCreateTests.KeyValues_NewKeyExistingName_ShowsKeyError`
- **Preconditions** Editor in the target view. The test creates public key K with value A through New key; its detail is open.

1. Close the detail ("Close the detail"; "Back to the list" on a phone) → The URL is …/key-values; the region K is gone
2. "New key" with Key K and Value B, click "Create" → Key error "This key exists already; open it to change its value."; the dialog stays open
3. Change the Key to K in upper case, click "Create" → The same Key error (keys are unique regardless of case)
4. Click "Cancel", open …/key-values/K by URL → Value A; Version "v1"
5. Click "Versions" → Only the row "v1(current)"

##### KV-CRE-04 A secret reference is validated and shows the Reference badge
- **Priority** Medium · **Role** Secrets · **Mode** B · **Test** `KeyValueCreateTests.KeyValues_NewSecretReference_ShowsReferenceBadge`
- **Preconditions** Secrets role in the target view (Standard, allowed by default, gives the Secret switch a secret level). Key K generated; reference R = "kv://at-vault/at-ref-<hex4>".

1. Open Values store, "New key", Content type "Secret reference" → Value help "kv://<vault>/<name> or https://<vault>.vault.azure.net/secrets/<name>"; placeholder "kv://my-vault/db-password"
2. Key K, Value "not-a-ref", click "Create" → A Value error that mentions "secret reference"; the dialog stays open
3. Switch "Secret" on, Value R, click "Create" → Content type error "A reference is a public pair; the platform never resolves it."
4. Switch "Secret" off (Level "Public"), click "Create" → The detail of K: badge "Reference"; Content type "application/vnd.2s.secret-reference"; Value R
5. Look at the Value field and the Binding → Help "A reference to a secret in your own Key Vault; the application resolves it with its own identity."; Binding "@K"

#### Detail (KeyValueDetailTests)

The Value tab of a public key: editing in place with Save and Ctrl+S, the metadata and the binding source with its copy button, the unsaved-changes guard before the Versions tab, and a concurrent change answered with Reload or Overwrite.

##### KV-DET-01 Edit a public value in place and save it
- **Priority** High · **Role** Editor · **Mode** B · **Test** `KeyValueDetailTests.KeyValues_EditPublicValue_SavesNewVersions`
- **Preconditions** Editor in the target view. The test creates public key K with value V1; its detail is open. V2 and V3 generated.

1. Look at the save row of the Value tab → "Save" is disabled; no "Unsaved changes"
2. Change the textbox "Value" to V2 → "Unsaved changes" shows; "Save" is enabled
3. Click "Save" → Toast "Saved as version 2"; "Unsaved changes" is gone; "Save" is disabled; Version "v2"
4. Change the value to V3 and press Ctrl+S (Cmd+S) in the textbox → Toast "Saved as version 3"; Version "v3"
5. Reload the page → The textbox "Value" shows V3; Version "v3"
6. Click "Versions" → Rows "v3(current)", "v2" and "v1"; Expires "—" for v3 and a date for v2 and v1

##### KV-DET-02 The metadata and the binding source of a key
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `KeyValueDetailTests.KeyValues_BindingSource_ChangesCopiedBinding`
- **Preconditions** Editor in the target view. The clipboard recorder is installed before the first navigation. The test creates public key K with value V; its detail is open.

1. Look at the metadata → Level "Public", Version "v1", Author the signed-in user, Created "{user}, {date}", Content type "text/plain"
2. Look at the Binding → The combobox "Binding source" shows "default source"; the binding "@K"
3. Pick "public only" in "Binding source" → The binding "@public(K)"
4. Click the binding's "Copy to clipboard" → The clipboard holds "@public(K)"; no secret toast
5. Pick "secret only", then "default source" → "@secret(K)", then "@K"
6. Click the "Copy to clipboard" next to the heading K → The clipboard holds K

##### KV-DET-03 Unsaved changes ask before switching to Versions
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `KeyValueDetailTests.KeyValues_UnsavedChanges_AskBeforeVersions`
- **Preconditions** Editor in the target view. The test creates public key K with value A; its detail is open.

1. Change "Value" to B without saving → "Unsaved changes"; the page title starts with "● "
2. Click "Versions" → Dialog "Discard changes?", "Your changes have not been saved.", buttons "Keep editing" and "Discard"
3. Click "Keep editing" → Still the Value tab (the URL has no /versions); Value B; "Unsaved changes"
4. Click "Versions", then "Discard" → The URL ends with /versions; the title has no "● "
5. Click "Value" → Value A; no "Unsaved changes"; "Save" disabled

##### KV-DET-04 A concurrent change offers Reload or Overwrite
- **Priority** High · **Role** Editor · **Mode** B · **Test** `KeyValueDetailTests.KeyValues_ConcurrentChange_OffersReloadOrOverwrite`
- **Preconditions** Editor in the target view. The test creates public key K with value A; its detail is open. Values B, C, D generated. The ConcurrentEdit helper is the other writer.

1. Change "Value" to C without saving → "Unsaved changes"
2. Another writer saves B to K (ConcurrentEdit) → This page still shows C and Version "v1"
3. Click "Save" → Alert "Someone else changed this key", "It is at version 2 now ({author}). Reload to see it, or overwrite with your value." with "Reload" and "Overwrite"; no "Saved as" toast
4. Click "Reload" → Value B; the alert is gone; "Save" disabled
5. Change "Value" to C; another writer saves D to K → This page still shows C
6. Click "Save" → The alert says "It is at version 3 now"
7. Click "Overwrite" → Toast "Saved as version 4"; the alert is gone; Value C; Version "v4"
8. Click "Versions" → Rows "v4(current)", "v3", "v2" and "v1"

#### Secrets (KeyValueSecretTests)

Server-side secrets and their protection: Reveal, Hide and the secret copy, the re-mask after 60 seconds, Set new value and a change to Public, Change protection to HSM, a HYOK value encrypted in the browser for a recipient, and bulk Change protection with a key it cannot change.

##### KV-SEC-01 Create a Standard secret, reveal, copy and hide it
- **Priority** High · **Role** Secrets · **Mode** B · **Test** `KeyValueSecretTests.KeyValues_StandardSecret_RevealsCopiesAndHides`
- **Preconditions** Secrets role in the target view (Standard is allowed by default). The clipboard recorder is installed before the first navigation. Key K, secret S = PlatformData.Slug("secret").

1. Open Values store, "New key", switch "Secret" on, pick Level "Standard" → Level help "Encrypted in the database with a software key of the project in Azure Key Vault."
2. Key K, Value S, click "Create" → The detail of K: badge "Standard"; Value "••••••••"; S nowhere in the region; "Reveal" and "Set new value"
3. Click "Reveal" → S shows; help "Masked again after 60 seconds."; "Hide" and "Copy to clipboard" beside the value
4. Click the value's "Copy to clipboard" → Toast "The clipboard contains a secret", "Paste it where it belongs, then clear the clipboard."; the clipboard holds S
5. Click "Hide" → "••••••••"; S gone; "Reveal" back; the 60-second help gone
6. Reload the page → "••••••••"; S nowhere on the page (a reveal is never kept)

##### KV-SEC-02 A revealed secret is masked again after 60 seconds
- **Priority** Low · **Role** Secrets · **Mode** B · **Test** `KeyValueSecretTests.KeyValues_RevealedSecret_MasksAfterSixtySeconds`
- **Preconditions** Secrets role in the target view. Page.Clock is installed before the first navigation. The test creates Standard secret K with S; its detail is open.

1. Click "Reveal" → S visible; "Masked again after 60 seconds."
2. Fast-forward the clock by 59 seconds → S still visible
3. Fast-forward the clock by 2 more seconds → "••••••••"; "Reveal" back; the help is gone

##### KV-SEC-03 Set a new value for a secret, then make it public
- **Priority** High · **Role** Secrets · **Mode** B · **Test** `KeyValueSecretTests.KeyValues_SetNewSecretValue_SavesAndMakesPublic`
- **Preconditions** Secrets role in the target view. The test creates Standard secret K with S1; its detail is open. S2 and public value P generated.

1. Click "Set new value" → Fields "New level" (combobox "New protection level" = "Standard") and "New value"; "Cancel"; "Save" disabled
2. Type S2 in "New value", then click "Cancel" → "Unsaved changes" while typed; after Cancel the fields are gone and "Set new value" is back
3. "Set new value", S2, click "Save" → Toast "Saved as version 2"; Value "••••••••"; Version "v2"
4. Click "Reveal" → S2
5. "Set new value", New level "Public" → Help "Older versions keep their Standard level until they expire. The secret becomes public for every reader."
6. New value P, click "Save" → Toast "Saved as version 3"; badge "Public"; the textbox "Value" shows P; no "Reveal" and no "Set new value"
7. Click "Versions" → "v3(current)" "Public" with "View"; "v2" and "v1" "Standard" with "Reveal"

##### KV-SEC-04 Change the protection of a secret to HSM
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `KeyValueSecretTests.KeyValues_ChangeProtectionToHsm_KeepsValue`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X; in X's Settings, Secrets section, it switches "Allow HSM" on and clicks "Save secrets policy" (toast "Secrets policy saved"). A deployment without an HSM provider refuses the save: the Allowed levels card shows the error "The protection level 'Hsm' is not configured in this deployment." and the test is skipped at run time with Skip.Test(<that message>); on the mock the save succeeds and the test runs. It creates Standard secret K with S in X/default; its detail is open.

1. Look at the Level → Badge "Standard" with the button "Change protection"
2. Click "Change protection" → The menu item "Move to HSM"; no "Move to Standard"
3. Click "Move to HSM" → Dialog "Move 'K' to HSM?", "The value is encrypted again under the new level as a new version. Older versions keep their previous level until they expire."
4. Click "Change protection" in the dialog → Toast "'K' is HSM now"; badge "HSM"; Version "v2"
5. Click "Reveal" → S (the value is unchanged)
6. Click "Versions" → "v2(current)" with Level "HSM", "v1" with Level "Standard"
7. Click "Compare" on v2 → Card "Version 1 → 2" with "No differences."

##### KV-SEC-05 A HYOK value is encrypted in the browser for the recipients
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `KeyValueSecretTests.KeyValues_HyokValue_EncryptsInBrowser`
- **Preconditions** Owner of Target:Organization. The fetch recorder (init script, window.__requests) is installed before the first navigation. The test creates a disposable project X. In X's Settings, Secrets section: "Add recipient" with a Name, Id R (generated) and as Public key a 3072-bit RSA public JWK the test generates (toast "Recipient R added"); then "Allow HYOK" on and "Save secrets policy" (toast "Secrets policy saved"). Key K, values H and H2 generated.

1. Open Values store of X, "New key", switch "Secret" on, Level "HYOK" → Level help contains "Encrypted in your browser for 1 recipient(s)."
2. Key K, Value H, click "Create" → The detail of K: badge "HYOK"; alert "Encrypted on the client", "The platform stores a document encrypted for 1 recipient(s) and cannot read it. Only the holder of a recipient's private key decrypts it."; badge R
3. Read the recorded PUT …/key-values/K of the create from window.__requests, then look at the detail → Exactly one such PUT; its body has "Protection":"Hyok" and does not contain H; H nowhere on the page; no "Reveal" and no "Change protection"
4. Click "Set new value" → "New protection level" shows "HYOK"; help "Encrypted in your browser for 1 recipient(s)."
5. New value H2, click "Save" → Toast "Saved as version 2"
6. Click "Versions" → Alert "The values are encrypted on the client; the platform cannot show or compare them."; "v2(current)" and "v1", both "HYOK"; no "View", "Reveal" or "Compare"

##### KV-SEC-06 Bulk Change protection keeps the keys it could not change selected
- **Priority** Medium · **Role** Owner · **Mode** B,D,P · **Test** `KeyValueSecretTests.KeyValues_BulkChangeProtection_KeepsFailedSelected`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X (default policy: Public and Standard) and in X/default a public key P and a Standard secret S; the list is shown.

1. Check "Select P" and "Select S" → Toolbar "Bulk actions": "2 selected", "Delete", "Change protection", "Clear selection"
2. Click "Change protection" → The only item is "Standard" (the server-side level X allows)
3. Click "Standard" → Dialog "Move 2 key(s) to Standard?", "Each value is encrypted again under the new level as a new version; older versions keep their level until they expire. Public and HYOK keys are skipped."
4. Click "Change protection" in the dialog → Warning toast "1 of 2 keys were not done" with a description starting "P: "; toolbar "1 selected"; "Select P" checked, "Select S" unchecked
5. Click "Clear selection" → The toolbar is gone
6. Look at the rows P and S → P: Level "Public", Version "v1"; S: Level "Standard", Version "v1" (an unchanged key gets no version)

#### Versions (KeyValueVersionTests)

The Versions tab: loaded only when it opens, View and Compare of a JSON value edited in the code editor, and Reveal and Compare of secret versions.

##### KV-VER-01 A JSON value edited in the code editor: versions load on demand, View and Compare
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `KeyValueVersionTests.KeyValues_JsonVersions_LoadOnDemandAndCompare`
- **Preconditions** Editor in the target view. Key K generated. The fetch recorder (init script, window.__requests) is installed before the first navigation.

1. Open Values store, "New key" K, Content type "JSON", Value {"enabled":false}, click "Create" → The detail: Content type "application/json"; a code editor with {"enabled":false}; no textbox "Value"
2. Replace the editor text with {"enabled":true} → "Unsaved changes"
3. Press Ctrl+S (Cmd+S) in the editor → Toast "Saved as version 2"; window.__requests has no GET whose URL path ends with /key-values/K/versions
4. Click "Versions" → window.__requests now has a GET whose URL path ends with /key-values/K/versions; headers Version, Level, Author, Created, Expires; "v2(current)" with Expires "—", "v1" with a date
5. Click "View" on v1 → Card "Version 1" shows {"enabled":false}
6. Click "Compare" on v2 → The "Version 1" card is gone; card "Version 1 → 2" with a "−" line {"enabled":false} and a "+" line {"enabled":true}
7. Click "Compare" on v1 → Card "Version 0 → 1" with a "+" line {"enabled":false}
8. Click "Close the comparison", then "Value" → The card is gone; the editor shows {"enabled":true}

##### KV-VER-02 Reveal and compare secret versions
- **Priority** Medium · **Role** Secrets · **Mode** B · **Test** `KeyValueVersionTests.KeyValues_SecretVersions_RevealAndCompare`
- **Preconditions** Secrets role in the target view. The test creates Standard secret K with S1, then saves S2 through "Set new value" (v2); the detail is open.

1. Click "Versions" → "v2(current)" and "v1", both "Standard", each with "Reveal" and "Compare"; S1 and S2 nowhere
2. Click "Reveal" on v1 → Card "Version 1" shows S1
3. Click "Close the version" → The card is gone
4. Click "Compare" on v2 → Card "Version 1 → 2" with a "−" line S1 and a "+" line S2
5. Click "Close the comparison", then "Value" → The value is masked "••••••••"

#### List (KeyValueListTests)

The list of Values store in a disposable project: search over keys and public values, the server-side level filter and the query kept by the detail, the row menu (Open, Copy key, Copy binding, Reveal), and the context switcher dropping the key, the selection and the secrets opt-in.

##### KV-LST-01 Search, level filter and the query kept by the detail
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `KeyValueListTests.KeyValues_SearchAndLevelFilter_NarrowTheList`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X and in X/default a public key K1 with unique value U and a Standard secret K2 with unique value W; the list shows "2 of 2".

1. Type K1 in "Search" → Only K1 is listed; the URL has q=K1; counter "1 of 2"
2. Search U → K1 is listed (public values are searched)
3. Search W → "No key matches the search" without a "New key" button in it; counter "0 of 2"
4. Clear the search; "Level filter" → "Standard" → The URL has level=Standard; only K2; counter "1 of 1"
5. "Level filter" → "All levels" → No level= in the URL; K1 and K2 listed
6. Search K1, open the row K1 → The URL is …/key-values/K1?q=K1; the region K1
7. Click "Versions" → The URL is …/key-values/K1/versions?q=K1
8. Close the detail ("Close the detail"; "Back to the list" on a phone) → The URL is …/key-values?q=K1; "Search" shows K1; the region is gone

##### KV-LST-02 The row menu copies the key and the binding and opens the detail
- **Priority** Medium · **Role** Owner · **Mode** B,D,P · **Test** `KeyValueListTests.KeyValues_RowMenu_CopiesAndOpens`
- **Preconditions** Owner of Target:Organization. The clipboard recorder is installed before the first navigation. The test creates a disposable project X and in X/default a public key K and a Standard secret S; the list is shown.

1. Click "Actions for K" → Items "Open", "Copy key", "Copy binding", "Delete", "Delete permanently"; no "Reveal"
2. Click "Copy key" → Toast "Key copied"; the clipboard holds K
3. "Actions for K", "Copy binding" → Toast "Binding @K copied"; the clipboard holds "@K"
4. "Actions for K", "Open" → The URL is …/key-values/K; the region K
5. Close the detail, click "Actions for S" → Items include "Reveal"
6. Click "Reveal" → The detail of S opens; the value is still "••••••••"

##### KV-LST-03 Switching the project drops the key, the selection and the secrets opt-in
- **Priority** Low · **Role** Owner · **Mode** B,D,P · **Test** `KeyValueListTests.KeyValues_SwitchProject_DropsKeySelectionAndOptIn`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X and in X/default a public key K; the list is shown.

1. Check "Select K" → Toolbar "Bulk actions" with "1 selected"
2. Click "Export", then "Include secret values" → The menu item "Include secret values" is checked; the menu stays open
3. Press Escape, click the row K → The menu closes; the URL is …/key-values/K; the region K; "1 selected" is still shown
4. In the context switcher pick the project Target:Project → The URL is /{org}/{target project}/default/key-values with no key and no query; no detail; no "1 selected"
5. Click "Export" → The menu item "Include secret values" is not checked
6. Pick X in the context switcher → /{org}/X/default/key-values; K listed; "Select K" unchecked

#### Lifecycle (KeyValueLifecycleTests)

Delete, the Deleted keys and restore: a delete from the row menu restored from the detail, removal for good through Delete permanently and Purge, bulk delete and bulk restore of selected keys, and a New key over a deleted key that continues its version numbers. Desktop only (the phone list has no row menu or selection).

##### KV-LIF-01 Delete a key from the row menu and restore it from the Deleted keys
- **Priority** High · **Role** Owner · **Mode** B,D,P · **Test** `KeyValueLifecycleTests.KeyValues_DeleteFromRowMenu_RestoresFromDetail`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X and in X/default a public key K with value A; the list is shown.

1. "Actions for K", "Delete" → Dialog "Delete 'K'?", "The key can be restored for 90 days with every version; after that it is removed for good."
2. Click "Cancel" → K is still listed
3. "Actions for K", "Delete", confirm "Delete" → Toast "'K' deleted", "Restore it from the Deleted keys."; "No key values in this view"
4. Click "Deleted" → The URL has state=Deleted; the column "Expires"; the row K "in 90 days"
5. Click the row K → Badge "Deleted"; alert "This key is deleted", "Removed for good in 90 day(s), with every version. Restore it to bring back the latest version."; "Value" disabled; no "Save"
6. Click "Restore" → Toast "'K' restored"; the alert and the badge are gone; Value A editable; Version "v1"
7. Close the detail → "No deleted keys", "Deleted keys can be restored here for 90 days."
8. Click "Active" → No state in the URL; K listed

##### KV-LIF-02 Remove keys for good with Delete permanently and Purge
- **Priority** Medium · **Role** Owner · **Mode** B,D,P · **Test** `KeyValueLifecycleTests.KeyValues_DeletePermanentlyAndPurge_RemoveForGood`
- **Preconditions** Owner of Target:Organization (Administrator rights in X). The test creates a disposable project X and in X/default public keys K1 and K2; the list is shown.

1. "Actions for K1", "Delete permanently" → Dialog "Delete permanently?", "'K1' and every version of it are removed for good; nothing can restore it."; field "Type K1 to confirm"; "Delete permanently" disabled
2. Type a wrong text, then K1 → The button stays disabled, then is enabled
3. Click "Delete permanently" → Toast "'K1' removed for good"; K1 not listed
4. "Actions for K2", "Delete", confirm "Delete" → Toast "'K2' deleted"
5. Click "Deleted", check "Select K2" → K1 not listed; toolbar "1 selected" with "Restore", "Purge", "Clear selection"
6. Click "Purge"; type "default" into "Type default to confirm"; click "Purge" → Dialog "Purge the selected keys?", "1 deleted key(s) and every version of them are removed for good, before their retention ends."; toast "1 key(s) purged"; "No deleted keys"
7. Open …/key-values/K1, then …/key-values/K2 by URL → "Not found", "The key 'K1' does not exist in view 'default'." (and the same for K2)

##### KV-LIF-03 Bulk delete and bulk restore of selected keys
- **Priority** Medium · **Role** Owner · **Mode** B,D,P · **Test** `KeyValueLifecycleTests.KeyValues_BulkDeleteAndRestore_ReturnsKeys`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X and in X/default public keys K1 and K2; the list is shown.

1. Check "Select K1" and "Select K2" → Toolbar "Bulk actions": "2 selected", "Delete", "Clear selection"
2. Click "Clear selection" → The toolbar is gone; both checkboxes unchecked
3. Check "Select all" → "2 selected"
4. Click "Delete" → Dialog "Delete 2 key(s)?", "Deleted keys can be restored for 90 days, with every version; after that they are removed for good."
5. Confirm "Delete" → Toast "2 key(s) deleted"; the toolbar is gone; "No key values in this view"
6. Click "Deleted", check "Select all" → "2 selected" with "Restore" and "Purge"
7. Click "Restore" → No confirmation; toast "2 key(s) restored"; "No deleted keys"
8. Click "Active" → K1 and K2 listed

##### KV-LIF-04 New key over a deleted key continues its versions
- **Priority** Low · **Role** Owner · **Mode** B,D,P · **Test** `KeyValueLifecycleTests.KeyValues_NewKeyOverDeleted_ContinuesVersions`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X and in X/default a public key K with value A (v1); the list is shown. Value B generated.

1. "Actions for K", "Delete", confirm "Delete" in the dialog "Delete 'K'?" → Toast "'K' deleted", "Restore it from the Deleted keys."; "No key values in this view"
2. Click "New key", fill Key K and Value B, click "Create" → The dialog closes without a Key error; the URL is …/key-values/K; the region K shows the badge "Public" and no badge "Deleted"; Version "v2"; the textbox "Value" shows B and is enabled
3. Click "Versions" → Rows "v2(current)" with Level "Public" and Expires "—", and "v1" with Level "Public" and a date in Expires
4. Click "View" on v1 → Card "Version 1" shows A
5. Close the detail ("Close the detail") → The URL is …/key-values; the row K with Version "v2"
6. Click "Deleted" → "No deleted keys", "Deleted keys can be restored here for 90 days."

#### Import and Export (KeyValueImportExportTests)

Import in three steps (source, review, result): .env lines with Mark keys matching, a key/value document from a file followed by a flat JSON plan of create, update and unchanged, refused text and invalid keys, Replace under a prefix with Undo deletes; Export as .env, flat JSON and the key/value document, without and with secret values.

##### KV-IEX-01 Import .env lines and mark the password key as a secret
- **Priority** High · **Role** Secrets · **Mode** B · **Test** `KeyValueImportExportTests.KeyValues_ImportDotEnv_MarksPasswordAsSecret`
- **Preconditions** Secrets role in the target view. Prefix P = PlatformData.KeyValuePrefix() (at_<runId>.<word>_<hex4>), E = ToEnvKey(P) ('.' → '__', upper case); secret W generated. Text: "E__HOST=redis.internal" and "E__PASSWORD=W" on two lines.

1. Click "Import" → Dialog "Import key values", "Into {org}/{project}, view {view}."; step "1. Source"; "Next" disabled
2. Paste the text into "Or paste the text", click "Next" → "2. Review": badge ".env", "2 row(s)"; rows P.host and P.password, both "Public", with their values
3. "Mark keys matching" is "*password*"; pick "Standard" in "as", click "Mark" → "Level of P.password" shows "Standard" and its value "••••••••"; "Level of P.host" stays "Public"
4. Click "Check" → "Import" was disabled before; "Plan: 2 create" with the lines Create P.host and Create P.password; "Import" enabled
5. Click "Import" in the dialog → "3. Result": "Imported", "2 create"; lines with "v1"
6. Click "Done", open P.password by URL → Badge "Standard"; "••••••••"; "Reveal" shows W
7. Open P.host by URL → Value "redis.internal"; badge "Public"

##### KV-IEX-02 Import a key/value document from a file, then a flat JSON plan of create, update and unchanged
- **Priority** Medium · **Role** Secrets · **Mode** B · **Test** `KeyValueImportExportTests.KeyValues_ImportDocumentThenFlat_PlansEachAction`
- **Preconditions** Secrets role in the target view. Prefix P; secret W. File kv.json: {"Format":"2s.key-values.v1","Items":[{"Key":"P.a","Protection":"Public","Value":"1"},{"Key":"P.b","Protection":"Public","Value":"2"},{"Key":"P.s","Protection":"Standard","Value":"W"}]}.

1. Click "Import", choose the file kv.json in "File" → "Or paste the text" shows the file's text
2. Click "Next" → Badge "Key/value document", "3 row(s)"; "Level of P.s" shows "Standard" with "••••••••"; no "Mark keys matching"
3. "Check", "Import", "Done" → "Plan: 3 create"; "Imported"; the dialog closes
4. "Import", paste {"P.a":"1","P.b":"20","P.c":"3"}, "Next" → Badge "Flat JSON", "3 row(s)"
5. Click "Check" → The plan contains "1 unchanged", "1 update" and "1 create"; lines Unchanged P.a, Update P.b, Create P.c; no line for P.s
6. Click "Import" in the dialog → "Imported" with the same counts; Update P.b "v2", Create P.c "v1"
7. "Done"; open P.b, then P.s by URL → P.b: Value "20", Version "v2"; P.s: badge "Standard", Version "v1"

##### KV-IEX-03 Import refuses unreadable text and invalid keys
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `KeyValueImportExportTests.KeyValues_ImportUnreadableText_StaysOnSource`
- **Preconditions** Editor in the target view. Prefix P and its env form E generated.

1. Click "Import" → "Next" is disabled while the text is empty
2. Paste "hello world", click "Next" → Error "The text is neither a key/value document, a JSON object nor .env lines."; still "1. Source"
3. Replace the text with "E__OK=1" and "1BAD=2", click "Next" → Badge ".env", "2 row(s)", "1 invalid key(s)"; "Check" and "Import" disabled
4. Click "Back" → "1. Source" with the text kept
5. Remove the line "1BAD=2", click "Next" → "1 row(s)"; no invalid count; "Check" enabled
6. Click "Back", then "Cancel"; open …/key-values/P.ok by URL → The dialog closes; "Not found" (nothing was imported)

##### KV-IEX-04 Replace under a prefix deletes the unlisted keys; Undo deletes restores them
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `KeyValueImportExportTests.KeyValues_ImportReplaceUnderPrefix_UndoRestores`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X and in X/default a public key O outside the prefix. Prefix P = R.N (R = at_<runId>, N = <word>_<hex4>).

1. "Import", paste the nested JSON {"R":{"N":{"keep":"1","drop":"2"}}}, "Next" → Badge "Nested JSON", "2 row(s)": P.keep = 1, P.drop = 2
2. "Check", "Import", "Done" → "Plan: 2 create"; "Imported"
3. "Import", paste {"P.keep":"1"}, "Next"; Mode "Replace: also delete the other keys"; "Only under prefix" = "P." → Badge "Flat JSON"; the prefix field shows the prefix
4. Click "Check" → The plan contains "1 unchanged" and "1 delete"; the only Delete line is P.drop; no line for O
5. Click "Import" in the dialog → Dialog "Replace deletes keys", "1 key(s) of the view that the document does not list will be deleted (restorable for the retention period)."; "Replace" disabled
6. Type "default" into "Type default to confirm", click "Replace" → "Imported" with "1 unchanged" and "1 delete"; "Undo deletes"
7. Click "Undo deletes" → Toast "1 key(s) restored"; "Undo deletes" is gone
8. "Done"; open P.drop, then O by URL → P.drop: no "Deleted" badge, Value "2"; O: Version "v1"

##### KV-IEX-05 Export .env, flat JSON and the key/value document without secret values
- **Priority** High · **Role** Secrets · **Mode** B · **Test** `KeyValueImportExportTests.KeyValues_ExportWithoutSecrets_LeavesSecretsOut`
- **Preconditions** Secrets role in the target view. The test creates public key K with value V and Standard secret S with value W.

1. Click "Export" → Items "Key/value document (JSON)", ".env file", "Flat JSON object"; "Include secret values" unchecked
2. Click ".env file" → Download {org}-{project}-{view}.env; warning toast "{n} secret(s) left out", "The .env and flat formats carry values only; export the key/value document, or include the secret values."
3. Read the .env file → The line ENV(K)=V; no line for ENV(S); W absent
4. "Export", "Key/value document (JSON)" → Download {org}-{project}-{view}.key-values.json; no toast
5. Read the document → Format "2s.key-values.v1"; item K with Value V; item S with Protection "Standard" and no Value; W absent
6. "Export", "Flat JSON object" → Download {org}-{project}-{view}.flat.json with "K": "V" and no S; the same warning toast

##### KV-IEX-06 Export with secret values on request
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `KeyValueImportExportTests.KeyValues_ExportWithSecrets_IncludesValues`
- **Preconditions** Owner of Target:Organization. The test creates a disposable project X and in X/default a public key K with V and a Standard secret S with W.

1. Click "Export", click "Include secret values" → The item is checked; the menu stays open
2. Click "Flat JSON object" → Download {org}-X-default.flat.json with "S": "W" and "K": "V"; toast "The file contains secret values", "Store it where it belongs, then delete it."
3. Click "Export" → "Include secret values" is still checked
4. Click ".env file" → The file has the line ENV(S)=W; the same toast
5. "Export", "Key/value document (JSON)" → Item S with Protection "Standard" and Value W; the same toast
6. Reload the page, click "Export" → "Include secret values" is unchecked

#### Roles (KeyValueRoleTests)

What a Reader and a Contributor get in Values store, in the projects of Target:RoleProjects: read-only with Export for a Reader; public keys only, no secret levels and no secret export for a Contributor.

##### KV-ROL-01 A Reader sees Values store read-only and can export it
- **Priority** Medium · **Role** Reader · **Mode** B · **Test** `KeyValueRoleTests.KeyValues_Reader_SeesReadOnlyStore`
- **Preconditions** Reader: the view of Target:RoleProjects Reader (mock northwind/portal/default). Nothing is created.

1. Open Values store of the Reader project → "Search" visible; "Export" shown; no "Import" and no "New key"
2. Click "Export" → "Key/value document (JSON)", ".env file", "Flat JSON object"; no "Include secret values"
3. Click "Key/value document (JSON)" → Download {org}-{project}-{view}.key-values.json with Format "2s.key-values.v1"; no item of a secret level carries a Value

##### KV-ROL-02 A Contributor creates and edits only public keys
- **Priority** Medium · **Role** Contributor · **Mode** B · **Test** `KeyValueRoleTests.KeyValues_Contributor_CreatesPublicKeysOnly`
- **Preconditions** Contributor: the view of Target:RoleProjects Contributor (mock acme/shipping/default). Key K, values V and V2, prefix P with env form E generated.

1. Open Values store of the Contributor project → "Export", "Import" and "New key" shown
2. "New key", open "Protection level" → The only option is "Public"
3. Switch "Secret" on → Level stays "Public"
4. Key K, Value V, "Create" → The detail of K; Value editable; "Save" disabled; no "Set new value"
5. Change Value to V2, "Save" → Toast "Saved as version 2"
6. Click "Export" → The three formats; no "Include secret values"
7. "Import", paste "E__PASSWORD=x", "Next" → No "Mark keys matching"; "Level of P.password" offers only "Public"
8. "Back", "Cancel"; open …/key-values/P.password by URL → The dialog closes; "Not found"

#### Not automated

- "Retry failed" and "Imported with failures" in the import result: The mock always answers IsComplete true and rejects an invalid document as a whole (400 ImportInvalid); the live API reports per-item failures only when a concurrent writer disturbs a batch, which a test cannot provoke reliably.
- BYOK-protected values, the "Customer key unavailable" banner and the CustomerKeyUnavailable save message: Needs a real customer key in an Azure Key Vault that the test can disable; a live deployment marks the key Unavailable only from its periodic health check, the mock never returns CustomerKeyUnavailable, and the BYOK card belongs to the Settings section (SET-SEC-03).
- ProtectionNotAllowed (409) on create or save: The UI offers only allowed levels; the error needs the policy to change between loading and saving (a second administrator or a race), which no mock hook provides.
- Moving keys to HSM on a deployment without an HSM provider: The deployment must configure an HSM provider before a project can allow HSM; there KV-SEC-04 is skipped at run time with Skip.Test("The protection level 'Hsm' is not configured in this deployment."), the message the Allowed levels card shows when "Save secrets policy" is refused (501 ProtectionNotConfigured).
- Decrypting or revealing a HYOK value in the UI: By design the browser never holds a recipient's private key; only the CLI decrypts.
- A Reader's or Contributor's view of an existing secret ("Secret values are shown to the ContributorWithSecrets role and above.", no Reveal or Set new value, no Delete for a secret row): The secret would have to be created by another identity in the role project; a test runs as one pool user, and the mock's Reader and Contributor projects have no secrets.
- Differences of exactly Administrator or ContributorWithSecrets (e.g. ContributorWithSecrets sees Reveal but no "Delete permanently" or "Purge"): The mock has no project where the pool users hold exactly these roles, so Target:RoleProjects cannot map them.
- Expiry of deleted keys after 90 days and of old versions: Depends on time and Cosmos TTL; only the displayed countdown ("in 90 days", "Removed for good in 90 day(s)") is asserted.
- The native beforeunload prompt when reloading or closing with unsaved changes: A browser-native dialog that behaves differently across engines and remote browsers.
- Listing beyond the first page (mock 100, live 1,000 keys): The UI does not page and the search is client-side, so there is no stable behaviour to assert.
- A delete confirmation that is open while the scope changes (cancelled, or carried over through Back and Forward): Needs a context switch while a modal blocks the page; reachable only through history-navigation timing, which is racy.
- Error states: "The key was not deleted", "The bulk action failed", "The export failed", "The level was not changed", a failed reveal, the list's error state with "Retry": Need fault injection (500, 429, network loss); the mock has no failure hooks.
- Audit events of reveals, exports and imports; the Secrets policy toggles, rotation, recipients and the ProtectionInUse links: The audit events are owned by the Monitoring section (Monitor, Audit tab, MON-AUD-*); the Secrets policy, rotation, recipients and the ProtectionInUse links are owned by the Settings section (Settings #secrets, SET-SEC-* and SET-KEY-*).
- Bulk Delete and Restore offered to Readers and to Contributors on secret keys: The bulk bar shows them without a role check and the API refuses per key; a test would encode a defect as expected behaviour.
- Import of HYOK rows (encrypted in the browser before Check and Import): Needs the same recipient and policy setup as KV-SEC-05, which covers the browser encryption; dropped to keep the catalogue focused.
- Level badge colours and icons, the detail panel's pin, full screen and resize handle: The badge colours and icons are visual (the badge text is asserted instead); the detail panel's pin, full screen and resize are shell behaviour owned by the Navigation section (NAV-DET-01, NAV-DET-02).

### 9. Settings

The Settings section covers the project's Settings page (`/{org}/{project}/settings`) and the browser preferences of the account page (`/account`). Its tests live in the folder `Tests/Settings` and carry `[Area(Area.Settings)]`; no existing test moves here. The Settings page has these cards: Project (access point, your role, members, description), Annotations (default time zone), Dates (date format), Annotation types (names, colors and icons), Machine authentication, Certificate authority, and the Secrets section (`#secrets`: Allowed levels, Key-encryption keys, Bring your own key, Recipients (HYOK)). The account page adds the Preferences card and the personal Annotation types card. Project-wide state is changed only inside a disposable project the test creates (mode P): the description, the machine policy, the secrets policy, key rotation and the HSM key, the customer key and the recipients. The default time zone, the date format and the type appearance are different, although they sit on the project's Settings page: they are browser-local. The code (`stores/project-settings.ts`) keeps them in this browser's localStorage (`2s-admin:project-settings`, one entry per `{organization}.{project}`) and never sends them to the API, and the Preferences live in `2s-admin:preferences`. They are not shared with the project's other users, and each test's fresh browser context isolates them, so mode B on the target project is right for SET-PRJ-04 and the SET-BRW and SET-PRF cases. If these cards ever move to the API, those cases must become P. HSM is a deployment capability and is checked at run time: SET-KEY-03 is skipped with the API's message where the deployment has no HSM provider, and it runs on the mock. The section has six sub-folders, one test class each: Project and Roles (`SettingsProjectTests`), Browser Settings (`SettingsBrowserTests`), Preferences (`SettingsPreferencesTests`), Machine Authentication (`SettingsMachineAuthenticationTests`), Secrets Policy (`SettingsSecretsPolicyTests`) and Keys and Recipients (`SettingsKeysRecipientsTests`). The Elementary case is SET-PRJ-01: the Settings page shows every section to an Administrator. It is read-only and runs everywhere. SET-BRW-01 also covers the dropped ANN-NEW-03: after the default time zone is set, a Subject created in New annotation starts in that zone and is saved with it. Other sections own what these settings touch elsewhere. Monitoring owns the Monitor page (Usage and Audit), including the audit rows that the policy, rotation and recipient changes write, and the old `/usage` and `/audit` links (MON-PAG-04). Navigation owns the old `/secrets` and `/certificates` links (NAV-RTE-05). Access owns the account profile and memberships (ACC-SGN-05) and the effect of the machine policy on New machine (ACC-CRT-01, ACC-CRT-02, ACC-FED-03). Annotations owns the New annotation dialog, which SET-BRW-01 and SET-PRF-03 only use.

#### Project and Roles (SettingsProjectTests)

##### SET-PRJ-01 Settings page shows every section of the project for an Administrator
- **Priority** Elementary · **Role** Admin · **Mode** B · **Test** `SettingsProjectTests.Settings_OpenAsAdministrator_ShowsEverySection`
- **Preconditions** Administrator or Owner of the target project (mock: Ada Lovelace, Owner of acme/billing), in Target:Organization/Project. Read-only: the test creates and changes nothing.

1. Open the target project's Settings → Heading 'Project' (level 2) is visible; 'Access point' shows '{organization}.{project}' (mock 'acme.billing'); 'Your role' shows 'Owner' or 'Administrator' (mock 'Owner'); 'Members' shows a number (mock '3').
2. Look at the cards → Headings 'Annotations', 'Dates', 'Annotation types', 'Machine authentication', 'Certificate authority' and 'Secrets' are visible.
3. Look at the administrator controls → Textbox 'Description' is shown and 'Save description' is disabled; 'Save machine policy' and 'Download CA certificate (PEM)' are visible.
4. Look at the Secrets section → Headings 'Allowed levels', 'Key-encryption keys', 'Bring your own key' and 'Recipients (HYOK)' are visible; switch 'Allow Public' is on and disabled; 'Save secrets policy' is disabled.

##### SET-PRJ-02 Administrator changes the project description and it shows on the organization overview
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `SettingsProjectTests.Settings_SaveDescription_ShowsOnOrganizationOverview`
- **Preconditions** Owner of the target organization. The test creates a disposable project at-<runId>-prj-… without a description and works only in it.

1. Open the new project's Settings → Textbox 'Description' is empty; 'Save description' is disabled.
2. Fill 'Description' with 501 characters → Field error 'At most 500 characters.'; 'Save description' stays disabled.
3. Fill a description of exactly 500 characters (ending in the run slug) and click 'Save description' → The field error is gone; toast 'Description saved'; 'Save description' is disabled again.
4. Reload Settings → 'Description' shows the saved text; 'Save description' is disabled.
5. Open the organization overview → The card of the new project shows the description; its title attribute holds the full 500-character text.
6. Back on Settings, clear 'Description', click 'Save description' and reload → Toast 'Description saved'; after the reload 'Description' is empty and 'Save description' is disabled.

##### SET-PRJ-03 A Reader sees the project settings read-only
- **Priority** High · **Role** Reader · **Mode** B · **Test** `SettingsProjectTests.Settings_OpenAsReader_ShowsEverythingReadOnly`
- **Preconditions** Exactly Reader of the project of Target:RoleProjects:Reader (mock: northwind/portal). Read-only: nothing is created or changed.

1. Open the project's Settings → 'Your role' shows 'Reader'; there is no 'Members' row; 'Description' is plain text or 'No description' (mock "Northwind's customer portal. A small catalog of customers and their responsibilities per context."); there is no textbox 'Description' and no 'Save description'.
2. Look at the Annotations, Dates and Annotation types cards → Button 'Default time zone', combobox 'Date format' and textbox 'Execution name' are disabled; there is no 'Reset to built-in' and no 'Reset Execution'.
3. Look at Machine authentication and Certificate authority → Badge 'Administrator' and the text 'The policy is visible and editable for Administrators.'; there is no 'Save machine policy'; 'Download CA certificate (PEM)' is visible.
4. Open Settings#secrets → The Secrets section is scrolled into view and focused; the Allowed levels card shows the badge 'Administrator'; switch 'Allow Standard', combobox 'Default secret level' and textbox 'Key URI (versionless)' are disabled.
5. Look for the administrator actions of the Secrets section → There is no 'Save secrets policy', 'Rotate now', 'Save key', 'Verify' or 'Add recipient' button.

##### SET-PRJ-04 A Contributor changes the browser settings but not the project-wide ones
- **Priority** Medium · **Role** Contributor · **Mode** B · **Test** `SettingsProjectTests.Settings_OpenAsContributor_ChangesOnlyBrowserSettings`
- **Preconditions** Exactly Contributor of the project of Target:RoleProjects:Contributor (mock: acme/shipping, where Ada is Contributor although she owns the organization acme). Only browser-local settings are changed.

1. Open the project's Settings → 'Your role' shows 'Contributor'; there is no 'Members' row; the description is plain text (mock "Shipping of Acme's orders. A small catalog of customers and their responsibilities per context.") or 'No description', without 'Save description'.
2. Look at the browser settings → Button 'Default time zone', combobox 'Date format' and textbox 'Execution name' are enabled; 'Reset to built-in' is visible and disabled.
3. Fill 'Execution name' with 'Job' and press Tab → The Execution preview shows 'Job'; 'Reset Execution' appears and 'Reset to built-in' is enabled.
4. Click 'Reset to built-in' → The preview shows 'Execution' again; 'Reset to built-in' is disabled.
5. Look at Machine authentication and the Secrets section → Badge 'Administrator' and 'The policy is visible and editable for Administrators.'; switch 'Allow Standard' is disabled; there is no 'Save machine policy', 'Save secrets policy', 'Rotate now' or 'Add recipient'.

#### Browser Settings (SettingsBrowserTests)

##### SET-BRW-01 The default time zone is kept in this browser, and new annotations start and are saved in it
- **Priority** High · **Role** Editor · **Mode** B · **Test** `SettingsBrowserTests.Settings_DefaultTimeZone_NewAnnotationStartsInIt`
- **Preconditions** Editor (Contributor or higher) in the target view; a fresh browser context, so the project has no browser settings and the zone is 'UTC'. The setting lives only in this browser's localStorage, so nothing project-wide changes. The browser runs in Europe/Prague, so the test picks Asia/Tokyo. The test creates the subject sbt.<s> in the target view (<s> = at-<runId>-<word>-<word>).

1. Open the target project's Settings → Button 'Default time zone' shows 'UTC'; help 'New annotations in this project start in this time zone. Saved in this browser.'
2. Click 'Default time zone', type 'Tokyo' into 'Search time zones…' and pick the option 'Asia/Tokyo GMT+9' → The button shows 'Asia/Tokyo'; no toast.
3. Reload Settings → The button still shows 'Asia/Tokyo'; localStorage '2s-admin:project-settings' has defaultTimeZone 'Asia/Tokyo' under '{organization}.{project}' (mock 'acme.billing').
4. Open Annotations of the target view, wait for the catalog and click 'New' ('New annotation' on a phone or in an empty catalog) → Dialog 'New annotation': button 'Time zone' shows 'Asia/Tokyo', with the help 'Default from the project settings.'
5. Pick 'Subject' in combobox 'Type*', fill 'Name*' with <s> and click 'Create' → Toast 'sbt.<s> created'; the dialog closes and the detail of sbt.<s> opens (URL …/annotations/sbt.<s>).
6. Look at the detail's 'Time zone' → Button 'Time zone' shows 'Asia/Tokyo', with the help 'Dates are entered and shown in this zone.'

##### SET-BRW-02 The project's date format changes how dates are shown
- **Priority** High · **Role** Editor · **Mode** B · **Test** `SettingsBrowserTests.Settings_DateFormat_ChangesShownDates`
- **Preconditions** Contributor or higher in the target project. The test creates a public key value at_<runId>.<word>_<hex4> in the target view so that a date is on screen. The date format is browser-local.

1. Create the key value in the Values store → The dialog closes and its detail opens (URL …/key-values/{key}); 'Updated' reads the date as yyyy-mm-dd, the time HH:mm and the zone (e.g. '2026-11-02 10:15 GMT+1').
2. Open Settings and look at combobox 'Date format' → It shows today's date followed by ' (yyyy-mm-dd)' (e.g. '2026-11-02 (yyyy-mm-dd)'); help 'How dates are shown on every page of this project, for everyone who opens it in this browser. Each person can pick their own format in Preferences.'
3. Open 'Date format' and pick the option ending in '(dd.mm.yyyy)' → The combobox shows today's date as dd.mm.yyyy followed by ' (dd.mm.yyyy)' (e.g. '02.11.2026 (dd.mm.yyyy)'); no toast.
4. Open the key value's detail again → 'Updated' reads the date as dd.mm.yyyy with the time and zone (e.g. '02.11.2026 10:15 GMT+1').
5. Reload Settings → 'Date format' still shows the '(dd.mm.yyyy)' item.

##### SET-BRW-03 The project renames, recolors and re-icons an annotation type
- **Priority** High · **Role** Editor · **Mode** B · **Test** `SettingsBrowserTests.Settings_CustomizeType_ShowsOnTemplates`
- **Preconditions** Contributor or higher in the target project; a fresh context with the light color scheme. Browser-local settings only.

1. Open Settings, card 'Annotation types' → The Execution preview shows 'Execution'; the CSS variable --st-exe is '#047857'.
2. Fill 'Execution name' with 'Job' and press Tab → The preview shows 'Job'; 'Execution plural' has the placeholder 'Jobs'; 'Reset Execution' appears.
3. Open combobox 'Execution color' and pick 'Red' → --st-exe is '#b91c1c'.
4. Click 'Execution icon', type 'Rocket' into 'Search icons…' and pick 'Rocket' → The preview shows the rocket icon (the stored icon is 'i-lucide-rocket').
5. Reload Settings → 'Execution name' is 'Job', the preview shows 'Job' and --st-exe is '#b91c1c'.
6. Open the target view's Templates (Modeling) → The Execution row shows 'Job'; no text 'Execution' (exact) is on the page.

##### SET-BRW-04 A type's plural needs a name, and one type or all types can be reset
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `SettingsBrowserTests.Settings_ResetTypes_RestoresBuiltInNames`
- **Preconditions** Contributor or higher in the target project; a fresh context (no project types stored). Browser-local settings only.

1. Open Settings, card 'Annotation types' → Textbox 'Subject plural' is disabled with the tooltip 'Set a name first'; 'Reset to built-in' is disabled.
2. Fill 'Subject name' with 'Batch' and press Tab → 'Subject plural' is enabled with the placeholder 'Batches'; 'Reset Subject' appears; 'Reset to built-in' is enabled.
3. Fill 'Subject plural' with 'Batch runs' and press Tab; pick 'Gray' in combobox 'Unit color' → The Subject preview shows 'Batch'; --st-unt is '#52525b'; 'Reset Unit' appears.
4. Type 45 characters into 'Unit name' and press Tab → The field keeps the first 40 characters and the Unit preview shows them.
5. Click 'Reset Subject' → The Subject preview shows 'Subject'; 'Subject plural' is empty and disabled; 'Reset Unit' is still shown.
6. Click 'Reset to built-in' → The previews show 'Responsibility', 'Unit', 'Subject', 'Usage', 'Context', 'Execution' and 'Unit of execution'; --st-unt is '#a21caf' again; 'Reset to built-in' is disabled.

#### Preferences (SettingsPreferencesTests)

##### SET-PRF-01 Preferences are kept in this browser and 'Reset preferences' restores the defaults
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `SettingsPreferencesTests.Preferences_ChangeAndReset_RestoresDefaults`
- **Preconditions** Any signed-in user; a fresh browser context. Only this browser's preferences change.

1. Open Account (/account) → Heading 'Preferences'; selected tabs 'Compact' (Row density), 'JSON' (Document format) and 'Form' (Annotation editing); combobox 'Date format' shows 'Project default'; 'Reset types' is disabled.
2. Click 'Comfortable', then 'YAML' of Document format and 'YAML' of Annotation editing, and pick the 'Date format' option ending in '(d mmm yyyy)' → The clicked tabs are selected; the combobox shows today's date followed by ' (d mmm yyyy)' (e.g. '2 Nov 2026 (d mmm yyyy)').
3. In the 'Annotation types' card fill 'Execution name' with 'Run' and press Tab → The Execution preview shows 'Run'; 'Reset types' is enabled.
4. Reload Account → Every choice is kept; localStorage '2s-admin:preferences' has density 'comfortable', editorFormat 'yaml', annotationMode 'yaml', dateFormat 'd mmm yyyy' and the Execution label 'Run'.
5. Click 'Reset preferences' → 'Compact', 'JSON', 'Form' and 'Project default' are back; 'Execution name' is empty; 'Reset types' is disabled.

##### SET-PRF-02 A personal date format in Preferences wins over the project's
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `SettingsPreferencesTests.Preferences_PersonalDateFormat_WinsOverProject`
- **Preconditions** Contributor or higher in the target project. The test creates a public key value at_<runId>.<word>_<hex4> in the target view and sets the project's date format to dd.mm.yyyy. Both formats are browser-local.

1. Create the key value; on Settings pick the 'Date format' option ending in '(dd.mm.yyyy)'; open the key's detail → 'Updated' reads the date as dd.mm.yyyy with the time and zone (e.g. '02.11.2026 10:15 GMT+1').
2. On Account open combobox 'Date format' (it shows 'Project default') and pick the option ending in '(mm/dd/yyyy)' → The combobox shows today's date followed by ' (mm/dd/yyyy)' (e.g. '11/02/2026 (mm/dd/yyyy)').
3. Open Settings → The Dates card shows 'Your own Preferences show dates as mm/dd/yyyy.' with the link 'Open Preferences' to /account; the project's 'Date format' still shows '(dd.mm.yyyy)'.
4. Open the key's detail → 'Updated' reads the date as mm/dd/yyyy with the time and zone (e.g. '11/02/2026 10:15 GMT+1').
5. On Account pick 'Project default' → The combobox shows 'Project default'.
6. Open Settings, then the key's detail → The Dates card no longer shows 'Your own Preferences show dates as mm/dd/yyyy.'; 'Updated' reads dd.mm.yyyy again (e.g. '02.11.2026 10:15 GMT+1').

##### SET-PRF-03 Personal type names in Preferences win over the project's on Templates and Annotations
- **Priority** Medium · **Role** Editor · **Mode** B · **Test** `SettingsPreferencesTests.Preferences_PersonalTypeName_WinsOverProject`
- **Preconditions** Contributor or higher in the target project; a fresh context with the light color scheme. Project and personal type settings are browser-local.

1. On Settings fill 'Execution name' with 'Job', press Tab and pick 'Red' in 'Execution color' → The Execution preview shows 'Job'; --st-exe is '#b91c1c'.
2. On Account, card 'Annotation types', fill 'Execution name' with 'Run' and press Tab → The Execution preview shows 'Run'; 'Reset types' is enabled.
3. Open the target view's Templates → The Execution row shows 'Run'; 'Job' is not shown; --st-exe is still '#b91c1c'.
4. Open Settings → Card 'Annotation types' shows 'Your own Preferences override some of these for you.' with the link 'Open Preferences'; the project's 'Execution name' is still 'Job'.
5. Open Annotations, click 'New' ('New annotation' on a phone) and open combobox 'Type*' → Option 'Run' is listed; there is no option 'Execution' or 'Job'.
6. On Account click 'Reset types', then open Templates → 'Reset types' is disabled; the Execution row shows 'Job' again.

#### Machine Authentication (SettingsMachineAuthenticationTests)

##### SET-MAC-01 Administrator changes the credential kinds, the default kind and the certificate lifetime
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `SettingsMachineAuthenticationTests.MachinePolicy_ChangeKindsAndLifetime_IsKept`
- **Preconditions** Owner of the target organization. The test creates a disposable project and works only in it. A new project's policy is ApiKey only, default ApiKey, with the deployment's default lifetime (365).

1. Open the new project's Settings → Checkbox 'ApiKey' is checked and 'Certificate', 'CertificateAndApiKey', 'ClientCredentials' and 'Federated' are not; combobox 'Default for new machines' shows 'ApiKey'; there is no 'Default certificate lifetime (days)'.
2. Check 'Certificate' → 'Default certificate lifetime (days)' appears with the deployment default (365).
3. Uncheck 'ApiKey' → 'Default for new machines' moves to 'Certificate'.
4. Uncheck 'Certificate' → Field error 'Enable at least one kind.'; 'Save machine policy' and 'Default for new machines' are disabled; the lifetime field is gone.
5. Check 'ApiKey' and 'Certificate', pick 'Certificate' in 'Default for new machines', set the lifetime to 30 and click 'Save machine policy' → Toast 'Machine policy saved'.
6. Reload Settings → Only 'ApiKey' and 'Certificate' are checked; the default is 'Certificate'; the lifetime is 30.

##### SET-MAC-02 Anyone downloads the CA certificate as PEM
- **Priority** Medium · **Role** Any · **Mode** B · **Test** `SettingsMachineAuthenticationTests.CertificateAuthority_Download_SavesPem`
- **Preconditions** Any member of the target project. Read-only.

1. Open Settings, card 'Certificate authority' → The text 'The CA that signs machine and shared certificates; servers that verify mTLS clients trust it.' and the button 'Download CA certificate (PEM)'.
2. Click 'Download CA certificate (PEM)' → A download named 'storyteller-ca.pem' starts; no toast 'The CA certificate could not be downloaded'.
3. Read the downloaded file → It starts with '-----BEGIN CERTIFICATE-----' and contains '-----END CERTIFICATE-----'.

#### Secrets Policy (SettingsSecretsPolicyTests)

##### SET-SEC-01 Turning off the mTLS rule asks first; Cancel keeps it and the confirmed change is kept
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `SettingsSecretsPolicyTests.SecretsPolicy_TurnOffMtls_AsksFirstAndIsKept`
- **Preconditions** Owner of the target organization. The test creates a disposable project (its rule is on) and works only in it.

1. Open the new project's Settings#secrets → Switch 'Require mTLS for secret delivery' is on; 'Save secrets policy' is disabled.
2. Turn the switch off and click 'Save secrets policy' → Dialog 'Deliver secrets to machines without mTLS?' with 'Machines with an API key or a bearer token alone will receive secret values when they hold the Secrets.Read scope.'
3. Click 'Cancel' → The dialog closes; the switch is on again; 'Save secrets policy' is disabled; no toast.
4. Turn the switch off, click 'Save secrets policy' and then 'Turn off the mTLS rule' → Toast 'Secrets policy saved'; 'Save secrets policy' is disabled.
5. Reload Settings#secrets → The switch is off.
6. Turn the switch on and click 'Save secrets policy' → No dialog; toast 'Secrets policy saved'.
7. Reload Settings#secrets → The switch is on.

##### SET-SEC-02 Disabling a level that keys still use shows the counts per view
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `SettingsSecretsPolicyTests.SecretsPolicy_DisableLevelInUse_ShowsCountsPerView`
- **Preconditions** Owner of the target organization. The test creates a disposable project and a Standard secret at_<runId>.<word>_<hex4> in its default view (New key with 'Secret' on: the project's default secret level, Standard).

1. Create the secret in the new project's Values store → The dialog closes and the secret's detail opens (URL …/key-values/{key}): level badge 'Standard' and the value '••••••••'.
2. Open Settings#secrets and turn 'Allow Standard' off → 'Save secrets policy' is enabled.
3. Click 'Save secrets policy' → Warning alert 'Standard is still in use' with 'Move these keys to another level first:' and the link '1 in default'; no toast.
4. Click '1 in default' → The Values store opens at /{organization}/{project}/default/key-values?level=Standard&state=All and lists the secret.
5. Open Settings#secrets again → 'Allow Standard' is on (nothing was saved).

##### SET-SEC-03 A customer key can be saved, but BYOK cannot be allowed before it is verified
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `SettingsSecretsPolicyTests.SecretsPolicy_AllowByokUnverified_IsRefused`
- **Preconditions** Owner of the target organization. The test creates a disposable project (no customer key), a key URI https://<runSlug>.vault.azure.net/keys/<word> and a new tenant GUID. The key is never verified.

1. Open the new project's Settings#secrets → The BYOK row shows 'Needs: A verified customer key.' and 'Allow BYOK' is off; card 'Bring your own key' shows 'Save the key first.' and 'Save key' is disabled.
2. Turn 'Allow BYOK' on and click 'Save secrets policy' → Error alert 'Set and verify the customer key before allowing Byok.' (live: 'Set and verify the customer key before enabling the Byok level.'); no toast.
3. Reload; fill 'Key URI (versionless)' and 'Tenant id' → 'Save key' is enabled.
4. Click 'Save key' → Toast 'Customer key saved; verify it next'; the card's badge shows 'Unknown'; 'Health' is 'Unknown' and 'Verified' is 'not yet'; 'Verify' is shown.
5. Reload Settings#secrets → 'Key URI (versionless)' and 'Tenant id' show the saved values; 'Save key' is disabled.
6. Turn 'Allow BYOK' on and click 'Save secrets policy' → The same error alert 'Set and verify the customer key before allowing Byok.' (live: '… before enabling the Byok level.'); after a reload 'Allow BYOK' is off.

##### SET-SEC-04 HYOK is allowed once a recipient exists, becomes the default level and protects its last recipient
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `SettingsSecretsPolicyTests.SecretsPolicy_AllowHyok_ProtectsLastRecipient`
- **Preconditions** Owner of the target organization. The test creates a disposable project (Public and Standard allowed, no recipients), an RSA 3072-bit public key (PEM), a name and an id at-<runId>-rcp.

1. Open the new project's Settings#secrets → 'Allow HYOK' is off with 'Needs: At least one enabled recipient.'; card 'Recipients (HYOK)' shows 'No recipients yet.'
2. Turn 'Allow HYOK' on and click 'Save secrets policy' → Error alert 'Add an enabled recipient before allowing Hyok.' (live: 'Add at least one enabled recipient before enabling client-side encryption (Hyok).'); no toast.
3. Reload; click 'Add recipient', fill 'Name*', 'Id (optional)' and 'Public key*' and click 'Add' → Toast 'Recipient {id} added'; the row shows 'RSA 3072 bits' and 'enabled'.
4. Turn 'Allow HYOK' on, pick 'HYOK' in combobox 'Default secret level' and click 'Save secrets policy' → 'HYOK' is offered as a default; toast 'Secrets policy saved'.
5. Reload Settings#secrets → 'Allow HYOK' is on; 'Default secret level' shows 'HYOK'.
6. Click 'Disable' in the recipient's row → Toast 'The recipient was not changed' with 'The last enabled recipient cannot be disabled while Hyok is allowed.' (live: 'Client-side encryption (Hyok) is allowed and needs at least one enabled recipient; disable the level first.'); the badge stays 'enabled'.
7. Click 'Remove' and then 'Remove' in dialog 'Remove the recipient {name}?' → Toast 'The recipient was not removed' with 'The last enabled recipient cannot be removed while Hyok is allowed.' (live: 'Client-side encryption (Hyok) is allowed and needs at least one enabled recipient; disable the level first.'); the row stays.

##### SET-SEC-05 Saving a secrets policy that another writer changed meanwhile is refused
- **Priority** Low · **Role** Owner · **Mode** B,P · **Test** `SettingsSecretsPolicyTests.SecretsPolicy_SaveStalePolicy_IsRefused`
- **Preconditions** Owner of the target organization. The test creates a disposable project and an RSA 3072-bit public key (PEM) for the other writer (id at-<runId>-other).

1. Open the new project's Settings#secrets → Switch 'Require mTLS for secret delivery' is on; 'No recipients yet.'
2. Another writer (ConcurrentEdit) adds the recipient to the project → The other writer's add succeeds (mock: the in-page call answers 200 with the recipient; live: the second page shows the toast 'Recipient at-<runId>-other added'); the open page still shows 'No recipients yet.'
3. Turn the mTLS switch off, click 'Save secrets policy' and then 'Turn off the mTLS rule' → Error alert 'The policy changed meanwhile; reload the page and try again.'; no toast 'Secrets policy saved'.
4. Reload Settings#secrets → The other writer's recipient is listed; the mTLS switch is on.

#### Keys and Recipients (SettingsKeysRecipientsTests)

##### SET-KEY-01 Administrator rotates the Standard key after confirming
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `SettingsKeysRecipientsTests.SecretKeys_RotateStandardKey_ShowsNewVersion`
- **Preconditions** Owner of the target organization. The test creates a disposable project and a Standard secret at_<runId>.<word>_<hex4> in its default view (a live project gets its Standard key with the first secret).

1. Create the secret in the new project's Values store → The dialog closes and the secret's detail opens (URL …/key-values/{key}): level badge 'Standard' and the value '••••••••'.
2. Open Settings#secrets, card 'Key-encryption keys' → The Standard row shows 'Key', 'Current version' and 'Next rotation' with 'Rotate now'; the HSM row shows 'Created when the HSM level is allowed.'
3. Note 'Current version' and click 'Rotate now' → Dialog 'Rotate the Standard key now?' with 'New values use the new key version at once; the stored values are re-wrapped in the background. The old version stays usable until the re-wrap is complete.'
4. Click 'Cancel' → The dialog closes; 'Current version' is unchanged; no toast.
5. Click 'Rotate now' and then 'Rotate' → Toast 'Standard key rotated'; 'Current version' differs from the noted one.
6. Look under the keys → An alert 'Re-wrap of the Standard values complete' (mock) or 'Re-wrapping the Standard values' (live) whose text ends with 'target key version {new}.'
7. Reload Settings#secrets → 'Current version' shows the new version.

##### SET-KEY-02 Administrator adds, disables, enables and removes a recipient, and invalid keys are refused
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `SettingsKeysRecipientsTests.Recipients_AddDisableRemove_RefusesInvalidKeys`
- **Preconditions** Owner of the target organization. The test creates a disposable project, a name, an id at-<runId>-rcp, an RSA 3072-bit public key (PEM) and an RSA 2048-bit public key (JWK).

1. Open the new project's Settings#secrets, card 'Recipients (HYOK)' → 'No recipients yet.'
2. Click 'Add recipient' and fill only 'Name*' → The form shows 'Name*', 'Id (optional)' and 'Public key*'; 'Add' stays disabled.
3. Fill 'Public key*' with 'not a key' and click 'Add' → Error alert in the form 'The public key is neither PEM nor a JWK object.' (live: 'The public key is not valid: The public key is neither PEM nor a JWK object.'); the form keeps the input; no row is added.
4. Replace the key with the 2048-bit JWK and click 'Add' → Error alert in the form 'The recipient key must have at least 3072 bits.' (live: 'The public key has 2048 bits; at least 3072 are needed.'); no row is added.
5. Replace the key with the 3072-bit PEM, fill 'Id (optional)' and click 'Add' → Toast 'Recipient {id} added' with 'Existing HYOK values are not encrypted for it; re-encrypt them with the CLI (sform story kv reencrypt) using a current recipient's private key.'; the form closes; the row shows the name, the id, 'RSA 3072 bits' and 'enabled'.
6. Click 'Disable' in the row, then 'Enable' → The badge shows 'disabled' with the button 'Enable', then 'enabled' with 'Disable' again (no toast).
7. Click 'Remove', then 'Cancel' in dialog 'Remove the recipient {name}?' → The dialog closes; the row stays.
8. Click 'Remove', then 'Remove' → Toast 'Recipient {id} removed'; the card shows 'No recipients yet.'

##### SET-KEY-03 Allowing HSM creates the HSM key, which an Administrator rotates
- **Priority** Medium · **Role** Owner · **Mode** B,P · **Test** `SettingsKeysRecipientsTests.SecretKeys_AllowHsm_CreatesAndRotatesHsmKey`
- **Preconditions** Owner of the target organization. The test creates a disposable project (Public and Standard allowed, no HSM key) and works only in it. HSM is a deployment capability: where the deployment has no HSM provider, the save in step 2 is refused and the test is skipped with the API's message; on the mock it runs.

1. Open the new project's Settings#secrets → Switch 'Allow HSM' is off, with 'Needs: An HSM provider is configured in the deployment.'; in card 'Key-encryption keys' the HSM row shows 'Created when the HSM level is allowed.' and has no 'Rotate now'.
2. Turn 'Allow HSM' on and click 'Save secrets policy' → Toast 'Secrets policy saved'; 'Save secrets policy' is disabled. (Where the deployment has no HSM provider, the Allowed levels card shows the error alert "The protection level 'Hsm' is not configured in this deployment." instead, and the test is skipped with that text.)
3. Look at the HSM row of 'Key-encryption keys' → It shows 'Key Vault Premium' (live: 'Key Vault Premium' or 'Managed HSM'), 'Key' (mock 'kek-{organization}-{project}'), 'Current version' (mock '0f1e2d3c') and 'Next rotation', with its own 'Rotate now'.
4. Note the HSM 'Current version' and click the HSM row's 'Rotate now' → Dialog 'Rotate the HSM key now?' with 'New values use the new key version at once; the stored values are re-wrapped in the background. The old version stays usable until the re-wrap is complete.'
5. Click 'Rotate' → Toast 'HSM key rotated'; the HSM 'Current version' differs from the noted one; an alert 'Re-wrap of the Hsm values complete' (mock) or 'Re-wrapping the Hsm values' (live) whose text ends with 'target key version {new}.'
6. Reload Settings#secrets → 'Allow HSM' is on; the HSM row's 'Current version' shows the new version.

#### Not automated
- BYOK 'Show the setup' (consent link, role to grant, az command and their Copy buttons): on a live target the answer depends on the deployment's BYOK application (without a client id the API reports that BYOK is not configured), and the mock returns fixed data, which does not justify a mock-only case.
- Verifying a customer key (Healthy or Unavailable), allowing BYOK and changing the key while BYOK is allowed: these need a real customer Key Vault key in a foreign tenant with admin consent, the role assignment and network access (real Azure Key Vault verification). The mock's regex-based health has no live counterpart.
- Allowing HSM on a deployment without an HSM provider: the API refuses the level there ('The protection level 'Hsm' is not configured in this deployment.'), so SET-KEY-03 is reported as skipped with that message, as KV-SEC-04 is.
- Re-wrap progress after a live rotation, and the daily customer-key check (ByokUnavailable): the hourly and daily maintenance timers drive them, and a UI test can neither wait for nor trigger them.
- Error states with Retry and the failure toasts (description, machine policy, secrets policy, CA download, rotation, projects list): they need fault injection, but MSW answers inside the service worker, so page.route cannot see the mock's requests, and a live 500/429 cannot be forced.
- Settings as exactly Administrator or ContributorWithSecrets (e.g. the badge 'Contributor + secrets', or an Administrator who is not Owner): the mock has no project where the pool users hold exactly these roles.
- Effect of the machine policy on the New machine dialog (offered kinds, default kind, lifetime): owned by the Access section; ACC-CRT-01, ACC-CRT-02 and ACC-FED-03 enable Certificate, CertificateAndApiKey and Federated in their disposable project and create the machine.
- Audit rows of the secrets settings (PolicyChange for the mTLS rule and the levels, Rotate, RecipientAdded, RecipientChanged and RecipientRemoved): owned by the Monitoring section, which checks them on the Monitor page's Audit tab in its own disposable project. No case of this section opens the Monitor page.
- Old project links /{org}/{project}/secrets (to Settings#secrets) and /certificates: owned by the Navigation section (NAV-RTE-05); the old /usage and /audit links belong to Monitoring (MON-PAG-04). SET-PRJ-03 opens Settings#secrets directly.
- Effect of the mTLS rule, the credential kinds and a rotation on machines and encryption: this is API behaviour for machine credentials and Key Vault that the admin UI does not show; API tests cover it.
- Recipient limit (20 per project) and duplicate recipient ids: the limit needs 20 additions, and the duplicate refusal is one more API message, shown in the form like the refusals of SET-KEY-02.
- Browser settings kept per project (a second project shows the defaults): SET-BRW-01 checks the per-project entry through the localStorage key '{organization}.{project}'. A dedicated case would need a disposable project only for this check.
- Effects of the Row density, Document format and Annotation editing preferences on grids and editors: they belong to the sections that own those grids and editors. SET-PRF-01 covers their storage and reset.
- Fallback when localStorage is blocked: a normal Playwright context cannot block localStorage for the origin.
- Changed type colors on the graph's WebGL canvas: this would need pixel comparisons, so the CSS variable --st-{code} is checked instead.

### 10. Monitoring

The Monitor page of a project (`/{org}/{project}/monitor`) is for Administrators only. It has two tabs: Usage, the default (no `tab` in the URL), and Audit (`?tab=audit`). Usage shows the key/value usage for a From/To date range (UTC): totals per meter, estimated charges and a daily chart. Audit lists the project's secret events, newest first. It can be filtered by view, key, operation and dates, and each event shows its actor (a link to the member) and a correlation id that can be copied. The old `/usage` and `/audit` links redirect to these tabs. The tests live in `Tests/Monitoring` and carry `[Area(Area.Monitoring)]`, a new value of the `Area` enum. The section has two sub-folders. **Monitor Page** (`MonitorTests`) covers the tabs, the Administrators-only gate, the usage date range and the old links. **Audit Trail** (`MonitorAuditTests`) covers events that the test creates itself and the filters: a Standard secret created and revealed in the target view, and, in a disposable project, a policy change, a key rotation, an added recipient and an export with secret values. Every case runs on the mock and on a live target, desktop and phone. MON-AUD-03 changes the project-wide secrets policy, the Standard key and the recipients, so it runs as an Owner in a disposable project that it creates (mode B,P); every other case is mode B. No case depends on mock seed data: the usage report is checked for its shape and range, never for its values, and the audit checks look only at the events the test produces. On a live target the audit is eventually consistent, so the audit cases poll with a bounded timeout. The Elementary case is MON-PAG-01: Monitor opens on Usage and switches to Audit through the tabs. Navigation owns the Monitor entry of the menu per role (NAV-MNU-05) and the old `/secrets`, `/certificates` and `/invitations` links (NAV-RTE-05).

#### Monitor Page (MonitorTests)

##### MON-PAG-01 Monitor opens on Usage and switches to Audit through the tabs
- **Priority** Elementary · **Role** Admin · **Mode** B · **Test** `MonitorTests.Monitor_SwitchTabs_ShowsUsageAndAudit`
- **Preconditions** An Administrator or Owner of the target project (Target:Organization/Project; on the mock acme/billing). Nothing is created. The test reads the UTC clock once before it opens the page.
1. Open `/{org}/{project}/monitor` → The URL has no `tab`. Tab "Usage" has aria-selected "true" and tab "Audit" has aria-selected "false".
2. Look at the Usage tab → "From" holds the first day of the current month and "To" holds today (UTC, yyyy-MM-dd). The h2 headings "Totals {From} to {To}", "Estimated charges" and "Per day" are shown.
3. Click the tab "Audit" → The URL has `tab=audit` and "Audit" is selected. The toolbar has the textboxes "View filter" and "Key filter", the combobox "Operation filter" showing "Every operation", the empty date inputs "From" and "To", and the counter "{n} event(s)". Below it is either the event table (column "Correlation") or the empty state "No audit events".
4. Reload the page → "Audit" is still selected and "Key filter" is visible and empty.
5. Click the tab "Usage" → The URL has no `tab=`. "Usage" is selected and "Estimated charges" is shown.

##### MON-PAG-02 Monitor is for Administrators only
- **Priority** High · **Role** Reader · **Mode** B · **Test** `MonitorTests.Monitor_OpenAsReader_ShowsAdministratorsOnly`
- **Preconditions** A Reader of the project: on the mock northwind/portal through Target:RoleProjects:Reader (Ada is Reader there with no organization role); on a live target the project of Target:RoleProjects:Reader, or Target:Project with a pool user listed as Reader. Nothing is created.
1. Open `/{org}/{project}/monitor` → The heading "Administrators only" is shown with "The usage and the audit trail of the project's key values are visible to its Administrators.". There are no tabs "Usage" or "Audit" and no "From" input.
2. Open the old link `/{org}/{project}/audit` → The URL becomes `/{org}/{project}/monitor` with `tab=audit`. "Administrators only" is still shown and there is no "Key filter".

##### MON-PAG-03 The usage report follows the chosen date range and refuses a reversed one
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `MonitorTests.MonitorUsage_ChangeRange_ShowsReportOfRange`
- **Preconditions** An Administrator or Owner of the target project. From the UTC clock the test computes the previous calendar month: its first day F, its last day L, and the day before F (P). Nothing is created. The meter values are not asserted, because they depend on the project and, on a live target, on aggregation.
1. Open `/{org}/{project}/monitor` → "Usage" is selected and the heading is "Totals {first day of this month} to {today}".
2. Fill "From" with F, then "To" with L → The heading is "Totals F to L".
3. Look at the totals card → It shows either meter rows (labels such as "Standard keys (key-months)" or "API reads") or "No usage recorded in this range.".
4. Look at "Per day" → It shows either the chart "Daily reads and keys for {n} days", with n between 1 and the number of days of that month and the first and last date under it within F..L, or "No days with data.".
5. Look at "Estimated charges" → It shows either dollar rows with "Total" and "An estimate from the list prices; the invoice is authoritative.", or "The deployment has no prices configured; the meters are informational.".
6. Fill "To" with P (the day before F) → An error alert "Something went wrong" appears with "The end of the range is before its start." and the button "Retry". No "Totals" heading is shown.
7. Fill "To" with L again → The heading "Totals F to L" is back and the alert is gone.

##### MON-PAG-04 Old Usage and Audit links open the Monitor tabs
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `MonitorTests.Monitor_OpenOldLinks_OpensTheirTabs`
- **Preconditions** An Administrator or Owner of the target project. Nothing is created.
1. Open `/{org}/{project}/usage` → The URL becomes `/{org}/{project}/monitor` without `tab=`. "Usage" is selected and the heading "Estimated charges" is shown.
2. Open `/{org}/{project}/audit` → The URL becomes `/{org}/{project}/monitor` with `tab=audit`. "Audit" is selected and "Key filter" is visible.
3. Open `/{org}/{project}/monitor?tab=summary` (an unknown tab) → "Usage" is selected and the heading "Estimated charges" is shown.

#### Audit Trail (MonitorAuditTests)

##### MON-AUD-01 Creating and revealing a secret is recorded in the audit trail
- **Priority** High · **Role** Admin · **Mode** B · **Test** `MonitorAuditTests.MonitorAudit_CreateAndRevealSecret_RecordsBothEvents`
- **Preconditions** An Administrator or Owner of the target project, which includes the secret rights. The project allows the Standard level (the default policy). Before its first navigation the test installs the clipboard recorder. It then creates, in the target view, a secret key value K (`at_<runId>.<word>_<hex4>`) with a generated value V.
1. In the Values store click "New key", fill "Key*" with K, turn on "Secret", pick "Standard" in "Protection level", fill "Value" with V and click "Create" → The detail of K opens with the level badge "Standard" and the masked value "••••••••".
2. Click "Reveal" → V is shown with "Masked again after 60 seconds.".
3. Open `/{org}/{project}/monitor?tab=audit` and type K into "Key filter". On a live target, reload and retype until the events arrive, for at most 60 s → The counter shows "2 event(s)". The first row is "Reveal" and the second is "Create" (newest first).
4. Look at the "Create" row → View is the target view, Key is K, Version is "v1", Level is "Standard", Channel is "User" and Count is "1". The Actor is a link.
5. In the "Reveal" row click "Copy to clipboard" in the Correlation column → The copied text is the whole correlation id: longer than 8 characters and starting with the 8 characters shown before "…".
6. Click the Actor link of the "Reveal" row → The Members page opens at `/{org}/{project}/members?highlight={account id}`.

##### MON-AUD-02 Audit filters narrow the events by operation, view and dates
- **Priority** Medium · **Role** Admin · **Mode** B · **Test** `MonitorAuditTests.MonitorAudit_ApplyFilters_NarrowsEvents`
- **Preconditions** An Administrator or Owner of the target project, which allows the Standard level. At the start the test computes today and yesterday in UTC and a view name that does not exist (`at-<runId>-view-<word>-<word>`). It then creates a Standard secret K (`at_<runId>.<word>_<hex4>`) with a generated value V in the target view and reveals it once, so K has one Create and one Reveal event.
1. Create the Standard secret K: in the Values store click "New key", fill "Key*" with K, turn on "Secret", pick "Standard" in "Protection level", fill "Value" with V and click "Create". Then click "Reveal" in the detail of K → The detail of K opens with the level badge "Standard" and the masked value "••••••••". After "Reveal" it shows V with "Masked again after 60 seconds.".
2. Open Monitor's "Audit" tab and type K into "Key filter" (on a live target, poll as in MON-AUD-01) → "2 event(s)": "Reveal" and "Create". K stays in "Key filter" for the next steps.
3. Pick "Reveal" in "Operation filter", then "Create" → Each time the counter shows "1 event(s)" and only the row of that operation is listed.
4. Pick "Every operation", then type the target view into "View filter" → "2 event(s)".
5. Replace the text in "View filter" with the view name that does not exist → "No audit events" is shown with "Reveals, writes, exports, policy and key changes of secrets are recorded here for 400 days." and the counter "0 event(s)".
6. Clear "View filter" and set "To" to yesterday (UTC) → "No audit events".
7. Clear "To" and set "From" to today (UTC) → "2 event(s)" again.

##### MON-AUD-03 Project-wide secret changes and an export with secret values are recorded in the audit trail
- **Priority** High · **Role** Owner · **Mode** B,P · **Test** `MonitorAuditTests.MonitorAudit_ProjectSecretChanges_AreRecorded`
- **Preconditions** An Owner of Target:Organization. The test creates a disposable project X and works only in X (view default). A new project allows Public and Standard, has the mTLS rule on, has no recipients and an empty audit trail. The test generates a secret key K (`at_<runId>.<word>_<hex4>`) with a value V, a recipient name, a recipient id R (`at-<runId>-rcp`) and an RSA 3072-bit public key in PEM.
1. Open X's Settings#secrets, turn off "Require mTLS for secret delivery", click "Save secrets policy", then "Turn off the mTLS rule" in the dialog "Deliver secrets to machines without mTLS?" → Toast "Secrets policy saved". The switch is off and "Save secrets policy" is disabled.
2. Open X's Values store (`/{org}/X/default/key-values`), click "New key", fill "Key*" with K, turn on "Secret", pick "Standard" in "Protection level", fill "Value" with V and click "Create" → The detail of K opens with the level badge "Standard" and the masked value "••••••••".
3. Open Settings#secrets, note the Standard key's "Current version" in "Key-encryption keys", click "Rotate now", then "Rotate" in the dialog "Rotate the Standard key now?" → Toast "Standard key rotated". "Current version" shows a new version N, different from the noted one.
4. In "Recipients (HYOK)" click "Add recipient", fill "Name*" with the name, "Id (optional)" with R and "Public key*" with the PEM, and click "Add" → Toast "Recipient R added". The row of R shows "RSA 3072 bits" and "enabled".
5. Open X's Values store, click "Export", click "Include secret values", then "Flat JSON object" → The file `{org}-X-default.flat.json` is downloaded. Toast "The file contains secret values" with "Store it where it belongs, then delete it.".
6. Open `/{org}/X/monitor?tab=audit` and pick "PolicyChange" in "Operation filter". On a live target, reload and pick again until the event arrives, for at most 60 s → The counter shows "1 event(s)". The row's Operation is "PolicyChange" with the detail "Allowed: Public, Standard" (mock) or "default=Standard, mtls=False" (live). Channel is "User" and the Actor is a link to `/{org}/X/members?highlight={account id}`.
7. Pick "Rotate", then "RecipientAdded" (on a live target, poll each as in step 6) → Each time the counter shows "1 event(s)". The "Rotate" row's detail contains N ("Key version N" on the mock, "Standard -> N" live). The "RecipientAdded" row's detail is R. Both Actors are links.
8. Pick "Export" (on a live target, poll as in step 6) → The counter shows "1 event(s)". The row has View "default", an empty Key, the detail "1 secrets revealed" (mock) or "1 secret value(s)" (live), Channel "User" and an Actor link.

#### Not automated
- Audit "Load more" paging and its failure toast "The next page did not load": paging needs more than 50 events (mock) or 100 (live). Creating that many through the UI is slow, and the size of a live trail is not under the test's control. The toast also needs fault injection.
- Error and loading states of the Usage and Audit tabs ("The API failed", "No access", "The API is not reachable", each with "Retry"): they need fault injection (500, 403, a network failure). On the mock MSW answers inside the service worker, where Page.RouteAsync cannot see the requests, and the catalogue has no live-only cases. The 400 error of a reversed range is covered by MON-PAG-03.
- A usage range of more than 400 days ("A usage report covers at most 400 days."): only the live API enforces the limit (the mock has none), and the catalogue has no live-only cases.
- Usage meter values, daily key gauges and the amounts of the estimated charges: on a live target, counters are flushed every 60 s, gauges are written daily at 00:10 UTC, and charges need KeyValues:Pricing. The mock's values are derived from its seed store. MON-PAG-03 checks the shape and the range of the report only.
- Machine and System actors (BindingRead over mTLS, Rewrap by the maintenance timer) and the machine actor's link to `/machines/{id}`: no UI action produces these events. Only the mock seed has them, and the admin UI's own TypeScript e2e (key-values.spec.ts) checks them.
- The seeded audit trail of acme/billing (9 events, Grace Hopper as actor, Count 143, "Key version b7c1e0a9"): mock-only seed data with no flow of its own, already covered by the TypeScript e2e. The B cases use the test's own events instead.
- Live BindingRead events: they come from machine binding reads over mTLS and are aggregated per hour and flushed every 60 s, so no UI test can produce them or wait for them reliably.
- BYOK audit events (ByokVerify, ByokUnavailable): ByokVerify needs a customer key that verifies, a Key Vault key in a foreign tenant with admin consent and the Key Vault role. The Settings section excludes verifying for the same reason. ByokUnavailable is written by the daily customer-key check, which a UI test can neither trigger nor wait for.
- RecipientChanged and RecipientRemoved audit events: the recipient endpoints that write RecipientAdded write them too, with the recipient id as the detail (live adds ": enabled=False" for a change). SET-KEY-02 covers Disable, Enable and Remove in the UI, and MON-AUD-03 checks the recipient audit through RecipientAdded.
- Audit rows of Update, Delete, Restore, PermanentDelete, Purge, Reprotect and Import: Key-Values owns these flows. Their rows come from the same audit write as Create (key, version and level; Reprotect and Import add a detail), whose columns MON-AUD-01 checks, and MON-AUD-02 checks the Operation filter. A case per operation would only repeat MON-AUD-01.
- Monitor for a Contributor (acme/shipping on the mock): the gate is the same `manageAccess` capability as in MON-PAG-02, and the page shows the same "Administrators only". A second role test would only repeat the Reader case.
- The Monitor entry of the navigation per role: owned by Navigation, where NAV-MNU-05 checks that a Reader has no link to `/monitor`.
- The old `/secrets`, `/certificates` and `/invitations` links: owned by Navigation (NAV-RTE-05 "Old project links open their new sections"). MON-PAG-04 keeps `/usage` and `/audit`.
- The day tooltips of the daily chart and the "Copy"/"Copied" tooltip of the copy button: hover-only, cosmetic. MON-AUD-01 checks the copied value itself.
