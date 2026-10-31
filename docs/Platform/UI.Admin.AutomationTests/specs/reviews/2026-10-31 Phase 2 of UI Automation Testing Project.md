# Phase 2 of UI Automation Testing Project

Linear: [424-133](https://linear.app/424fun/issue/424-133/create-ui-automation-testing-project).

## Overview

Phase 2 covers the second row of section 11 of [UI Automation Testing Project](../2026-10-31%20UI%20Automation%20Testing%20Project.md), "Elementary set": the page objects of section 5.3 that the set needs, and tests 2 to 6 (`Annotations_CreateResponsibility_AppearsInGrid`, `Configurations_SetDocument_EffectiveShowsIt`, `KeyValues_CreateAndRead`, `Project_Create_OpensSettings` and `Navigation_MobileMenu_OpensPages`) next to test 1 of phase 1. It touches only the Platform project `src/Platform/UI.Admin.AutomationTests`: the libraries, the admin UI and `CLAUDE.md` are unchanged, so there is no library review for this phase. As in phase 1 there is no deployed environment, so every run went against the admin UI's mock build (`npm run dev:mock`) through the `mock` profile, headless. The `[TestCase]` ids of the row are not set, because the cases do not exist in Testiny yet. The done-when, "Elementary set green in 3 consecutive runs with `MaxParallelTests=4` and 4 pool users", holds on that target for desktop Chromium and Pixel 7 Chromium.

| Commit | Content |
|---|---|
| `7b2cde6f` | 9 new page objects, 5 new test classes, `App`, `OrganizationsPage`, `PlatformTest`, `PlatformData`, `SignInTests`, `testsettings.mock.json`, `README.md` (21 files) |

A review of the implementation confirmed 14 findings (three of them duplicates, so 11 fixes) and rejected 6. All 14 were fixed before the commit; the fixes are marked "review fix" below and listed under "Review findings".

| Project | Passed (final pass) |
|---|---|
| `src/Platform/UI.Admin.AutomationTests/src` (`mock`; desktop Chromium, Firefox and WebKit, Pixel 7 Chromium) | 6 |

## What Was Done

### 5. `42.Platform.UI.Admin.AutomationTests`

- **`PlatformTest`** gains:
  - `Routes`, which is `AppRoutes.For(Target)`;
  - review fix: `protected static UserRequirement EditingUser`, which is `UserRequirement.WithAnyRole("Owner", "Administrator", "ContributorWithSecrets", "Contributor")`. Editing starts at Contributor in the admin UI (`capabilityRoles.edit` in `src/Platform/ui.admin/src/app/composables/useRole.ts`), and a Reader gets no create, edit or save action. A test that creates or edits overrides `RequiredUser` with it.
- **`SignInTests`** builds `OrganizationsPage(App, Routes)` and is otherwise unchanged.
- **`testsettings.mock.json`**: the four placeholder pool users get `Roles: ["Owner"]`, and `Execution` sets `ExpectTimeoutMs` 15000 and `NavigationTimeoutMs` 60000 (see "Deviations").

#### 5.2 Data (`PlatformData`)

- **Review fix: every generated part of an annotation key carries the run slug.**
  - `AnnotationKey(code, parts)` generates each missing part as `Cut(Data.Slug(RunSlug), 40)`, i.e. `at-<runId>-<word>-<word>` cut to 40 characters. Before, only the first generated part had the slug and the others were `<word>-<hex4>`.
  - So the key and every missing ancestor that Bulk create or the API creates with it carry the run slug, also when the caller passes an existing subject. The `rst.{r}` of an `exe` key was the case the review found: it had no slug and repeated in every rerun with a fixed `Execution:Seed`.
  - The longest key is a `uxe` at 168 characters (the comment says so); generated `exe` keys measured 108 to 122.
- **`PartCounts` became `PartNames`**: the parts of each code by what they name (`r`, `s`, `ru`, `sc`, `sr`, `src`, `srcu`), behind `PartsOf(code, paramName)`.
- **New: `static AncestorKey(key, code)`**, the ancestor of a key's type made of the key's own parts, e.g. `cnt.{s}.{c}` of `exe.{s}.{r}.{c}`.
  - Review fix: a key with an unknown code of its own blames `key`, not `code`.
  - No test calls it since test 3 changed (see 5.3). It stays for the full-set candidate "bulk create with missing ancestors" of section 5.3.
- **Review fix: `KeyValueKey(name)`** defaults to `at_<runId>.<word>_<hex4>`. With a word alone a run had about 180 possible keys, so a retry or a second key-value test could collide. `<word>_<hex4>` is still a valid key segment.
- **`ConfigurationDocument()`** is `probe` (a slug), `count` (1 to 99) and `enabled` (a flag). The implementation had added `retries` (0 to 10) for the mock's execution schema; the review fix of test 3 removed it with its comment.

#### 5.3 Page objects

- **Built:** `AppRoutes`, `OrganizationOverview`, `ProjectSettingsPage`, `AnnotationsPage` with `BulkCreateDialog`, `AnnotationDetail`, `ConfigurationsPage`, `ConfigurationDetail` and `KeyValuesPage`, all in `src/Pages/`. `App` and `OrganizationsPage` changed.
- **Not built:** `MembersPage`, `MachinesPage`, `ViewsPage` and `ModelingPage` of the spec's list. No Elementary test needs them; they come with the full-set tests of phase 6.
- **`App`** (the shell):
  - The breakpoints `DockedSidebarWidth` (1024, lg) and `PhoneWidth` (640, sm), with `HasDockedSidebar` and `IsPhone`.
  - `NavigateAsync(link)`: follows a link by its name in the docked `Sidebar`, or below 1024 px in the slideover (`OpenNavigationAsync`, `NavigationDialog`) and waits for it to close. It takes the first link of a name, because two are named "Members".
  - `RunCommandAsync(command)`: opens the command palette from "Search…", fills `CommandInput` and clicks the first option once its accessible name starts with the command (see "Flaky spots removed").
  - `Notifications` (the region "Notifications (F8)", found with `IncludeHidden`, because an open modal hides the rest of the page from the accessibility tree), `Toast(string)` and `Toast(Regex)`.
  - `ExpectAtAsync(path)`: the URL ends in the path, whatever its query and hash.
  - The D5 rule of phase 1 is kept: only the first `GotoAsync` of a test carries `?mock-scenario=<name>`. Review fix: the header comment now says that every page load goes through `GotoAsync`, and that links, `NavigateAsync` and palette actions route inside the loaded app.
- **`AppRoutes`** is a record of `Organization`, `Project` and `View`, made by `AppRoutes.For(PlatformTarget)`; another project is `Routes with { Project = … }`. It has `DefaultView` (`default`), `Organizations`, `OrganizationOverview`, `Settings`, `Annotations`, `Configurations`, `KeyValues` and `Annotation(key)`, `Configuration(key)`, `KeyValue(key)` (lower case, as the store keeps it). Review fix: `Organizations` is an instance property, and `OrganizationsPage` takes `AppRoutes` and opens `routes.Organizations` instead of the literal `"orgs"`.
- **`OrganizationOverview`**: the organization's `Heading`, "New project" (enabled for an Owner of the organization only) and `CreateProjectAsync(project, description)`, which returns once the dialog closed. The project field is matched by `^Project`, because a required field's name ends in `*`.
- **`ProjectSettingsPage`**: `ProjectHeading` (the "Project" card's heading, because the navbar's title is hidden below 2xl), `Description`, `SaveDescriptionButton` and `SaveDescriptionAsync`, which waits for "Description saved" and has no caller yet. Review fix: `ProjectCard` was renamed `ProjectHeading`.
- **`AnnotationsPage`**:
  - `Item(key)`: the grid's row from 768 px (md, `GridWidth`), the list's item below. Both are virtualized, so a row is looked for only in a filtered result.
  - `WaitForCatalogAsync` waits for "Reload the catalog" to be enabled; Bulk create's review and the palette's annotation actions need the loaded catalog.
  - `FilterAsync(query)` returns once the URL has `q=`.
  - `OpenBulkCreateAsync` uses the navbar's "Bulk create" from 640 px and the palette's "Bulk create annotations" on a phone. `BulkCreateAsync(keys)` creates new keys with their missing ancestors, checks each key's "From the list" row and closes the dialog: the setup of a test that acts on them.
  - Review fix: `BulkCreateButton` takes `.First`. An empty view's empty state has a second "Bulk create" button, so tests 2 and 3 hit a strict-mode violation on a fresh target. No mock view starts empty; a throwaway test (run once, then deleted) created a project, saw 2 matching buttons in its empty default view, and bulk-created and found an `exe` key there, on desktop and on mobile.
- **`BulkCreateDialog`** (built from the `IPage`): `Keys`, `Summary` (the live `p[aria-live=polite]`), `ReviewRow(key)`, `CreateButton(count)` ("Create 1 annotation", "Create N annotations"), `Result` (the title, `^(Created \d|No annotations were created)`), `CreatedRow(key)` and `DoneButton`. Bulk create shows no toast.
- **`AnnotationDetail`**: `OpenAsync(key)` opens the detail's URL and waits for nothing, because on a phone the full-screen detail hides the navbar and its user menu. `Identity` is the identity line by the test id `annotation-identity`, which `AnnotationForm.vue` already had and the TypeScript e2e suite uses. Review fix: `BackToTheList` was renamed `BackToTheListButton`.
- **`ConfigurationsPage`**: `Filter`, `NewConfigurationButton` and `OpenAsync`; only the navigation test uses it.
- **`ConfigurationDetail`**:
  - `CreateEmptyAsync` waits for the JSON mode in place of the empty state.
  - `SetDocumentAsync(json)` switches to JSON, selects all and inserts the text with `Keyboard.InsertTextAsync`, because typing would get CodeMirror's closed brackets and completions.
  - `SaveAsync` saves by keyboard (see "Deviations").
  - `OpenEffectiveAsync` follows the Effective tab and returns the read-only document.
  - Review fixes: `OpenAsync` waits for `NoConfiguration.Or(JsonMode)`; `SaveAsync` waits for the toast `^Configuration saved \(v\d+\)$`, the only sign of the save (the test still asserts the exact version); the Save regex is `^Save(?! anyway)`, so it no longer matches the schema panel's "Save anyway".
- **`KeyValuesPage`**: `CreateAsync(key, value)` fills "New key" and returns once the dialog closed and the key's detail is the URL; `Value(key)` is the text box of the detail region named by the key; `OpenVersionsAsync(key)` and `CurrentVersion`, the "(current)" row of the versions table (the table with an "Expires" column).
- **Locators.** By role, label and text, except `App.Sidebar` (a `nav` with the Organizations link), `BulkCreateDialog.Summary` and the existing test id above. No `data-testid` was added to the UI, which phase 2 does not touch. The regexes have no options, as in phase 1.
- **One rule for actions (review fix):** an action of a page object returns once its effect shows: the page it opens, its dialog closed, the editor in place of the empty state, or its toast when nothing else shows it. The test asserts the exact text it cares about.

#### 5.3 Elementary set

Each test is `[Test]`, `[Elementary]` and has its `[Area]`; none has a `[TestCase]`. Tests 2 to 4 lease an `EditingUser` (review fix), test 5 an Owner, tests 1 and 6 any user.

1. **`SignIn_ShowsOrganizations`** (`Tests/Access/SignInTests.cs`, Access): unchanged apart from the `Routes` argument.
2. **`Annotations_CreateResponsibility_AppearsInGrid`** (`Tests/Annotations/AnnotationsTests.cs`, Annotations).
   - Steps: a new `rst.at-<runId>-…` key into Bulk create (the toolbar, or the palette on a phone), then Create and Done; the catalog filtered by `key:<key>`; the detail opened by its URL.
   - Verifies: the summary is exactly "1 new" and the key's review row says "New"; the result is exactly "Created 1 annotation."; the filtered grid (the list on a phone) shows the key; the detail's identity line is the key.
   - Beyond the spec, which keeps the test off the virtualized grid, it also checks the row of the filtered result, which is always in view.
3. **`Configurations_SetDocument_EffectiveShowsIt`** (`Tests/Configurations/ConfigurationsTests.cs`, Configurations).
   - Steps: `BulkCreateAsync` of a new responsibility; its configuration opened by URL; Create empty; the generated document in the JSON editor; Save; the Effective tab.
   - Verifies: the toasts "Configuration created" and exactly "Configuration saved (v2)"; the effective document contains `"<name>": <value>` for every top-level value of the document.
   - A responsibility has no ancestors and usually no type schema or template, so the test depends on none of the target's modeling (see "Deviations").
4. **`KeyValues_CreateAndRead`** (`Tests/KeyValues/KeyValuesTests.cs`, KeyValues).
   - Steps: "New key" with `at_<runId>.<word>_<hex4>` and a slug as the public value; the Versions tab.
   - Verifies: the dialog closes on the key's detail URL; the detail's Value text box holds the value; the current version row contains "v1(current)".
5. **`Project_Create_OpensSettings`** (`Tests/Projects/ProjectsTests.cs`, Projects; `RequiredUser => UserRequirement.WithRole("Owner")`, because only an Owner of the organization may create a project).
   - Steps: on the target organization's overview, "New project" with a generated key and a description of up to 200 characters; then `App.NavigateAsync("Settings")`.
   - Verifies: the toast "Project <key> created"; the URL is the new project's default view annotations (`Routes with { Project = project, View = AppRoutes.DefaultView }`), then its settings; the description field holds the description and "Save description" is disabled.
6. **`Navigation_MobileMenu_OpensPages`** (`Tests/Navigation/NavigationTests.cs`, Navigation).
   - Steps: from the annotations, `App.NavigateAsync` to Configurations, Values store, Settings, Organizations and back to Annotations.
   - Verifies: for each, the route (`App.ExpectAtAsync`) and the page's own content: `ConfigurationsPage.Filter`, `KeyValuesPage.Search`, `ProjectSettingsPage.ProjectHeading`, `OrganizationsPage.Heading`, `AnnotationsPage.Filter`.
   - The slideover is used below 1024 px (the `mobile` profile, and any tablet width); a desktop run uses the docked sidebar (see "Deviations").

#### 5.4 Parallelism and order

- **Parallel up to `Execution:MaxParallelTests`** (4 in `testsettings.json`), bounded by the pool. No Elementary test is `[Serial]`, and none depends on another: each creates what it acts on, in the target's view or in a new project of its organization.
- **On the mock target** the data lives in the tab's `sessionStorage`, so what a test creates is gone with its page. On a deployed target it stays, attributable by `at-<runId>`.
- **Leases.** A test whose requirement no pool user meets is Blocked with a `UserLeaseException` before any browser work (see the Reader check under "Verification").
- **The pool of 4 for 4 parallel tests** gives the phase 1 warning "MaxParallelTests × 2 > pool" on every run.
- **Seeds.** No shuffle. A fixed `Execution:Seed` repeats the generated words; since every generated key part carries the run id, it no longer repeats a key across runs.

### 10. Documentation

- **Project `README.md`.**
  - A new section "The Elementary set": what each test does, in a table, and the `mock` and `mock,mobile` commands with `--treenode-filter "/**[Category=Elementary]"`.
  - The page-object conventions: the list of page objects (with `LoginPage`, review fix), the sm / md / lg breakpoints they branch on, and the action rule of 5.3.
  - "Write a test": `Routes`, `RequiredUser` and `EditingUser`, `App.NavigateAsync`, the virtualized grids, asserting a toast right after its action, and the run slug in every generated part of an annotation key.
  - The `Users:Pool` row: `Roles` are the user's roles in the target's organization and project, which `RequiredUser` matches.
  - "The mock target": the placeholder users' `Owner` role (Ada Lovelace owns `acme` and `acme/billing`) and the raised `ExpectTimeoutMs`.
  - Review fix of the README's inaccuracies: row 2 now names the list on a phone and the identity line, and the intro says "in the target's view, or a new project in its organization".
- **Gap:** the README names only the raised `ExpectTimeoutMs`. `NavigationTimeoutMs` 60000 was added after the README pass and is explained only in the comment of `testsettings.mock.json`.
- **Not written in this phase**, as in phase 1: the docs pages of section 10 (phase 5). `CLAUDE.md` needed no change.

### Deviations from the spec

- **Save by keyboard.** `ConfigurationDetail.SaveAsync` waits for Save to be enabled, then presses `ControlOrMeta+S` in the editor instead of clicking Save. For its 5 seconds, the "Configuration created" toast covers the Save button at the bottom right, and a pointer over a toast keeps it open: the click waited about 4.7 s for the toast to close.
- **Test 3 configures a new `rst`, not an `exe` (after review).** The implementation bulk-created a `cnt` and an `exe` together and configured the `exe`. Its document had to carry `retries` because the mock's execution schema requires it, so the test was fitted to the mock's schema and template. Review fix: it configures a new responsibility, and `ConfigurationDocument()` lost `retries`.
- **`EditingUser` requirement.** The spec leaves a test's role implicit. Tests 2 to 4 first leased any pool user, so whether they passed depended on pool order and concurrency once a Reader was in the pool. Review fix: they require `EditingUser`.
- **Every generated key part carries the run slug** (review fix, see 5.2). The spec puts `at-<runId>` in the slug only.
- **Key-value keys are `at_<runId>.<word>_<hex4>`** (review fix), not `at_<runId>.<word>`.
- **The mock profile.**
  - The four placeholder users have the role `Owner`. Each stands for Ada Lovelace, the Owner of `acme` and `acme/billing` in the default dataset, so test 5 and the `EditingUser` tests can lease any of them.
  - `Execution:ExpectTimeoutMs` 15000 (the default stays 10000): the dev server serves the app unbundled, and with 4 parallel tests the first render after a Firefox page load took up to 5.6 s.
  - `Execution:NavigationTimeoutMs` 60000 (the default stays 30000), added by the orchestrator after the review fixes: one cold-Firefox run timed out on its first navigations (see "Verification").
  - A deployed (bundled) build keeps the defaults.
- **The session-start check of section 5.2 is still not built.** Phase 1 deferred the check of the default organization, project and membership to phase 2. It needs a library change (a default member on `IUiAutomationSetup`, called from `UiAutomationSession.StartAsync`), which was outside this phase's task. The review raised it again and rejected it as a phase 2 code fix; it stays open.
- **No `[TestCase]` ids**, until the cases exist in Testiny.
- **The navigation test on desktop** uses the docked sidebar; the slideover is exercised by the `mobile` profile only.

### Flaky spots removed

- **The command palette** filters about 100 ms after the input. On Pixel 7 the click landed on "New annotation" of the unfiltered list and broke tests 2 and 3. `RunCommandAsync` now clicks the first option once its name starts with the command.
- **The toast over Save**: the keyboard save above.
- **Slow Firefox page loads** with 4 parallel tests: the raised `ExpectTimeoutMs` of the mock profile.

### Review findings

Confirmed and fixed (14; the lenses were stability, spec and UI, and conventions):

| Finding | Severity | Fix |
|---|---|---|
| `BulkCreateButton` hits two buttons in an empty view | high | `.First` (5.3) |
| Tests 2 to 4 lease any pool user and fail with a Reader (two findings) | high, medium | `PlatformTest.EditingUser`, README and mock profile comments |
| Test 3 is fitted to the mock's execution schema and template (two findings) | medium, medium | Test 3 configures a new `rst`; no `retries`; Save regex `^Save(?! anyway)` |
| Test 3's responsibility ancestor has no run slug and repeats with a fixed seed (two findings) | medium, low | Every generated part `at-<runId>-<word>-<word>` |
| A key-value key has about 180 values per run | low | `<word>_<hex4>` |
| `ConfigurationDetail.SaveAsync` returns before the save is done | medium | Waits for "Configuration saved (v<n>)"; `OpenAsync` waits for the empty state or the editor; the action rule in the README |
| `AppRoutes.Organizations` is static and `OrganizationsPage` hard-codes `"orgs"` | low | Instance property; `OrganizationsPage(App, AppRoutes)` |
| `App`'s header comment says every navigation goes through `GotoAsync` | low | "Every page load" |
| `ProjectCard` is a heading, `BackToTheList` has no `Button` suffix | low | `ProjectHeading`, `BackToTheListButton` |
| `AncestorKey` blames `code` for the key's own unknown code | low | `PartsOf(code, paramName)` |
| README Elementary table and page-object list are slightly inaccurate | low | Row 2, `LoginPage`, the intro, the data rule |

The fixes also re-wrapped three comment lines over 120 characters.

Rejected (6):

- **`FilterAsync` accepts any `q=`**, so a second filter on the same page could return before the new one applies. True in principle, but `FilterAsync` has one caller (test 2), which filters once.
- **The session-start check of section 5.2 is missing.** The facts are right, but the check needs library changes, not a phase 2 fix; see "Deviations".
- **`Navigation_MobileMenu_OpensPages` never opens the slideover in a desktop run.** A documented choice; the `mobile` profile runs the slideover path.
- **Tab routes are built outside `AppRoutes`, with two URL checks.** The line in question builds on `routes.KeyValue(key)`; no defect and no broken rule.
- **The md breakpoint is private to `AnnotationsPage`.** `App` holds the shell's breakpoints only; the grid's breakpoint belongs to the page that has the grid.
- **`BulkCreateDialog` takes `IPage` while other page objects take `App`.** No such convention exists, and the change would gain nothing.

## Verification

Done-when of phase 2 (section 11), against what was run:

| Criterion | State |
|---|---|
| Elementary set green in 3 consecutive runs with `MaxParallelTests=4` and 4 pool users | Against the mock target, after the review fixes: desktop Chromium 3 consecutive runs and Pixel 7 Chromium 3 consecutive runs with `--maximum-parallel-tests 4`, 6/6 each, exit 0. The pool is the mock profile's 4 placeholder users |

Commands and checks:

| Check | Result |
|---|---|
| `dotnet build src/Platform/UI.Admin.AutomationTests/src --no-incremental` | 0 errors, 0 warnings, before and after the review fixes |
| Before the review fixes | Desktop Chromium with `--maximum-parallel-tests 4`, 5 runs; Pixel 7; desktop Firefox (44.6 s) and WebKit (15.7 s); the `--treenode-filter "/**[Category=Elementary]"` run; WebKit on Pixel 7 and Firefox at the Pixel 7 viewport: 6/6 each, exit 0. The desktop Chromium tests took about 11 s per run |
| After the review fixes: `mock` Chromium, `mock,mobile` and `mock` WebKit | 6/6 each, exit 0 (runs `20261031-192923-wetv`, `-193003-s4pp`, `-193215-0mzg`) |
| After the review fixes: `mock` Firefox, first run | 2 passed, 4 failed, exit 2 (`20261031-193034-aor3`): each of the first 4 parallel tests failed with `TimeoutException: Timeout 30000ms exceeded` on its first `Page.GotoAsync`, while the page was loading and before any test logic; the next 2 passed |
| The same Firefox run again, twice | 6/6 each, exit 0 (`-193411-r26v`, `-193500-3j9s`) |
| `mock` Firefox with the committed `NavigationTimeoutMs` 60000 | 6/6, twice, exit 0 (`-193659-arb8`, `-193745-afqw`); the output copy of `testsettings.mock.json` has 60000 and predates both runs |
| Mobile paths | The trace of a Pixel 7 run shows Bulk create through "Search…" and the palette's option, and the navigation through "Open navigation" and the slideover's links |
| Reader negative check, the target | Before the fix, with the target set to the mock's Reader project `northwind/portal`, tests 2 to 5 failed and tests 1 and 6 passed: the tests really check the edit flows |
| Reader negative check, the pool | After the fix, with all 4 pool users listed as `Reader`: tests 2 to 4 Blocked with "No user of Users:Pool has role Owner or Administrator or ContributorWithSecrets or Contributor; the pool has 4.", test 5 with "… has role Owner …", tests 1 and 6 passed; exit 2 (`20261031-192638-q7ew`: 2 passed, 4 blocked) |
| Empty view (the `BulkCreateButton` fix) | A throwaway test against a new project's empty default view found 2 "Bulk create" buttons and passed with `.First`, on desktop and on mobile; deleted again |
| Warnings | Every run shows 1 warning, the pool of 4 for 4 parallel tests |

The run ids are those of `results.jsonl` under the project's `bin/Debug/net10.0/TestResults/artifacts/`.

## Not verified and deferred

- **The deployed environment.** None exists yet, so the real API, the AuthKit sign-in and a bundled build are untested. The mock's texts (toasts, Bulk create's summary and result) are assumed to be the real ones, since the mock runs the same UI.
- **AuthKit.** `AuthKitSignIn` has still not run (see phase 1).
- **A real type schema.** Neither a real execution schema (test 3's first form) nor a deployed `rst` schema or template was tried. Test 3 assumes the target's `rst` accepts `probe`, `count` and `enabled`; a schema that rejects them shows "The configuration does not match its schema" and fails the test.
- **The pool's roles on a deployed target.** `Roles` in `Users:Pool` are taken on trust. Test 5 needs a user listed as `Owner` who is an Owner of the target organization, and the `EditingUser` tests need Contributor or higher in the project. Nothing checks this until the session-start check of section 5.2 exists.
- **The Firefox navigation timeout** was attributed to a cold Firefox against the unbundled dev server from the pattern of the failures, not proven. Only two runs used the raised `NavigationTimeoutMs`.
- **Not run at all:** a width between 640 and 1023 px (the toolbar's Bulk create with the slideover), headed runs, Linux.
- **`FilterAsync`** returns on any `q=`; a test that filters twice on one page needs a stricter wait (rejected finding).
- **A UX note for the admin UI's owner:** for 5 s after "Create empty", the toast covers the configuration editor's Save button.
- **Later phases:** the `[TestCase]` ids once the Testiny cases exist (phase 3), BrowserStack (phase 4), CI and the docs pages (phase 5), the remaining page objects and the full-set tests (phase 6).
