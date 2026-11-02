# Admin UI Automation Tests

Browser tests of the integrated 2S Platform: the deployed admin UI (`src/Platform/ui.admin`), the real Storyteller API and the AuthKit sign-in, driven by Playwright under TUnit from `src/Platform/UI.Admin.AutomationTests` (`42.Platform.UI.Admin.AutomationTests`). The project is built on the [UI automation libraries](../../Libraries/Testing/UI.Automation/readme.md) (`42.Testing.UI.Automation.*`), which own the settings, browsers, user pool, data, artifacts, retries and reporting and document every setting; this page is what the Platform adds: its target, users, sign-in, page objects, tests, profiles, Testiny and BrowserStack use and the CI workflow.

The admin UI's own TypeScript Playwright suite (`src/Platform/ui.admin/src/test/e2e`) stays the fast, hermetic regression suite of the UI alone, against the mock API; this suite covers the integrated system.

Spec: [2026-10-31 UI Automation Testing Project](specs/2026-10-31%20UI%20Automation%20Testing%20Project.md); reviews of Phases 1, 2 and 5 (the Platform project, the CI workflow) in [`specs/reviews/`](specs/reviews/), of Phases 3 to 5 for the libraries (Testiny, BrowserStack, the target check) in [`docs/Libraries/Testing/UI.Automation/specs/reviews/`](../../Libraries/Testing/UI.Automation/specs/reviews/).

## Run

```bash
# once: build, then install the browsers of the pinned Playwright (1.63.0)
dotnet build src/Platform/UI.Admin.AutomationTests/src
pwsh src/Platform/UI.Admin.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install chromium firefox webkit   # Linux: install --with-deps

dotnet run --project src/Platform/UI.Admin.AutomationTests/src                                                   # everything, headless Chromium
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"   # the PR gate set
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Area-Access]"
UITESTS_PROFILE=mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src                            # Pixel 7 emulation
UITESTS_PROFILE=webkit,mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src                     # profiles combine, in order
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --test-parameter Execution:Seed=1383911092     # repeat a run's data
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --maximum-parallel-tests 1 --test-parameter Browser:Headless=false --test-parameter Browser:SlowMoMs=200
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --report-trx                                   # also a TRX file
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --list-tests
```

- **Not `dotnet test`**: TUnit runs on Microsoft.Testing.Platform 2.5, which refuses the VSTest mode of `dotnet test` on the .NET 10 SDK, and the repository stays in VSTest mode for its xUnit projects. `dotnet build src` builds the project and `dotnet test src` leaves it out (`Test="false"` in `src/Directory.Build.proj` and `src/Platform/Directory.Build.proj`).
- **The committed target** is `http://localhost:5173`, the local admin UI (`npm run dev` or Aspire); a run against it needs the passwords of the committed pool in the user secrets (`Users:Pool:<n>:Password`). **Without a target** (an empty `Target:BaseUrl`, e.g. `UITESTS_Target__BaseUrl=`) every test is skipped with a reason and the run exits `0`. A filter that matches nothing exits `8`.
- The terminal shows the session's lines prefixed `ui-automation:`: the run id, the data seed, the effective settings (secrets as `***`), the target check, warnings, and at the end the totals, every reporter's run URL and the path of `results.jsonl`.
- Results go to `src/Platform/UI.Admin.AutomationTests/src/bin/<configuration>/net10.0/TestResults/` (git-ignored): TUnit's HTML report (and the TRX), and `artifacts/<runId>/` with `results.jsonl` and a folder per test (`Tests.Access.SignInTests/SignIn_ShowsOrganizations/`) with `failure.png`, `trace.zip` (`pwsh …/playwright.ps1 show-trace <file>`) and `console.log`. The target check has its own folder, `TargetCheck/CheckTarget/`.
- **Keep traces private**: a `trace.zip` holds the network log and the context's storage state, so the pool user's session cookies and tokens (never a password: the sign-in runs untraced). Do not publish traces of a deployed target; the CI workflow leaves them (every `.zip` of the results) out of its artifact.
- **Secrets in messages**: Playwright's call log of a fill or a type keeps the typed value, so a failed sign-in could carry the e-mail and the password in its message. The runtime shows the settings' secrets (the pool passwords, `BrowserStack:AccessKey`, `Reporting:Testiny:ApiKey`) as `***` in every report (`results.jsonl`, Testiny, BrowserStack's session reason) and in the `ui-automation:` log, and the pool users' e-mails as `***` in the reports; a failed sign-in throws a `SignInException` without the original exception. TUnit's own TRX and HTML reports show a test body's exception as thrown.
- IDE: Visual Studio "Use testing platform server mode", Rider "Enable Testing Platform support", VS Code C# Dev Kit "Use Testing Platform Protocol".

## Configure

The layers, from the bottom: `testsettings.json`, `testsettings.<profile>.json` (each profile of `UITESTS_PROFILE` or `--test-parameter Profile=…`), `testsettings.local.json` (git-ignored), the user secrets of the project (`UserSecretsId` `3d07d28d-5b62-432e-9cfc-b53b6b883740`), `UITESTS_*` variables (`UITESTS_Target__BaseUrl`), `--test-parameter Section:Key=value`. The library's [settings reference](../../Libraries/Testing/UI.Automation/readme.md#settings) lists every key of `Browser`, `Execution`, `Users`, `BrowserStack` and `Reporting`; the committed `testsettings.json` sets every one of them, with these Platform values over the library's defaults: `Target:SignIn` `AuthKit`, `Browser:TimeZone` `Europe/Prague`, `BrowserStack:Project` `2S Platform - admin UI`.

The Platform's keys of `Target` (`PlatformTarget`, read with `Settings.Bind<PlatformTarget>("Target")`), with their committed values:

| Key | `testsettings.json` | Meaning |
|---|---|---|
| `BaseUrl` | `http://localhost:5173` | The admin UI, by default the local dev server (`npm run dev` / Aspire), e.g. `https://admin.staging.example.com`; empty skips every test |
| `SignIn` | `AuthKit` | `AuthKit` (deployed environments) or `None` (a target that is signed in already: the mock build) |
| `Organization` / `Project` / `View` | `ui-tests` / `automation` / `default` | The pre-provisioned organization, project and view the tests work in (placeholders until the environment exists); Platform names: 2 to 63 of `a-z`, `0-9` and `-` |
| `MockScenario` | `null` | Mock build only: `?mock-scenario=<name>` (`default`, `new-user`, `invited`, `large`, `showcase`) on the first navigation of each test |
| `EmailDomain` | `example.com` | Generated e-mail addresses are `ui-tests+<slug>@<domain>` |

`PlatformAutomationSetup.Validate` checks those names, the scenario name and the domain, and requires a non-empty `Users:Pool`.

A `testsettings.local.json` for a deployed environment, and its secrets (from `src/Platform/UI.Admin.AutomationTests/src`):

```json
{
  "Target": { "BaseUrl": "https://admin.staging.example.com", "Organization": "ui-tests", "Project": "automation" },
  "Users": {
    "Pool": [
      { "Email": "ui-test-01@example.com", "DisplayName": "UI Test 01", "Roles": [ "Owner" ] },
      { "Email": "ui-test-02@example.com", "DisplayName": "UI Test 02", "Roles": [ "Contributor" ] }
    ]
  }
}
```

```bash
dotnet user-secrets set "Users:Pool:0:Password" "…"
dotnet user-secrets set "Users:Pool:1:Password" "…"
dotnet user-secrets set "BrowserStack:Username" "…" ; dotnet user-secrets set "BrowserStack:AccessKey" "…"   # the browserstack-* profiles
dotnet user-secrets set "Reporting:Testiny:ApiKey" "…"                                                       # the reporting-testiny profile
```

### Profiles

| Profile | What it changes |
|---|---|
| `mock` | The mock build on `http://localhost:5174` (`npm run dev:mock`), `acme` / `billing` / `default`, `SignIn` `None`, `MockScenario` `default`, four placeholder pool users with the role `Owner`, and longer timeouts for the unbundled dev server (`NavigationTimeoutMs` 60000, `ExpectTimeoutMs` 15000) |
| `mobile` | `Browser:Device` `Pixel 7` (412×839, scale 2.625, touch, mobile user agent; the TypeScript suite narrows it to 390×844, the same layout below 640 px) |
| `firefox` / `webkit` | `Browser:Engine` |
| `browserstack-win-chrome` | BrowserStack, Chrome `latest` on Windows 11, 1920×1080 |
| `browserstack-mac-safari` | BrowserStack, `playwright-webkit` (Safari's engine; BrowserStack runs no branded Safari for Playwright) on OS X Tahoe, the newest macOS BrowserStack lists for Playwright 1.59 to 1.63 |
| `reporting-testiny` | Reporting on, to Testiny (`Reporting:Testiny` with every key; the project id and the API key are not committed) |

## Users and roles

`Users:Pool` lists pre-provisioned users; a test leases one for its duration, so the pool bounds the parallelism (`Execution:MaxParallelTests` is lowered to the pool size). `Roles` are the user's roles in the target's organization and project, matched by a test's `RequiredUser`:

- `PlatformTest.EditingUser` (Owner, Administrator, ContributorWithSecrets or Contributor; the admin UI gives a Reader no create, edit or save action) for every test that creates or edits: annotations, configurations, key values;
- `UserRequirement.WithRole("Owner")` for `Project_Create_OpensSettings` ("New project" is enabled only for an Owner of the organization);
- any user for the sign-in and navigation tests.

The committed `testsettings.json` holds a pool of 8 users (their e-mails and roles; the passwords go to the user secrets as `Users:Pool:<n>:Password`), enough for the default 4 parallel tests. A pool set in a higher layer (a profile such as `mock`, `testsettings.local.json`, `UITESTS_*` variables) replaces it entirely: the highest layer that sets any `Users:Pool:<n>:Email` defines the pool, and passwords of any layer apply to it by index. A lease that waits longer than `Users:LeaseTimeoutSeconds` (120 s) blocks the test (`No user of Users:Pool has role …` when none could ever match).

## AuthKit checklist

`Target:SignIn` `AuthKit` (`Access/AuthKitSignIn.cs`) signs each pool user in once per run, through `/login`, "Continue with AuthKit" and the AuthKit hosted page (Email, Continue, Password, Sign in, WorkOS' labels; the e-mail and the password are typed, not filled, because BrowserStack can mask only a typed value in its logs), then gives the user's later tests the captured storage state; authkit-js rotates the refresh token on every page load, so the cookies and the `workos:*` local storage items of a test that ended signed in carry over to the user's next test. The AuthKit environment of the target needs, by hand:

- password authentication enabled;
- no MFA (and no other second step) for the pool users;
- verified e-mail addresses of the pool users;
- each pool user a member of the default organization and project with the roles listed in `Users:Pool`;
- the target's redirect URI `https://<admin host>/callback` (and the sign-out redirect `/login`) registered;
- a custom auth domain (`authApiHostname` in the deployment's `app-config.json`) on any host other than `localhost`.

## The target check

Before the first test, `PlatformAutomationSetup.CheckTargetAsync` (`PlatformTargetCheck.cs`) opens, as a pool user on a signed-in page, the annotations of `Target:Organization` / `Project` / `View` and waits for the catalog; for a view other than `default` it also looks for the view on the project's Views page. A good target costs one page load (about 2 s on the mock dev server), two for another view. A bad target fails every test at once with one message, logged once, and no Testiny run is created:

| Problem | Message (after `The target check failed:`) |
|---|---|
| Organization not found | `Target:Organization 'x' was not found: it is not among the organizations of Users:Pool:0 (u-…) (acme, northwind). The organization does not exist, or the user is not a member of it or of one of its projects; …` |
| Project not found | `Target:Project 'x' was not found in organization 'acme': it is not among the projects of Users:Pool:0 (u-…) there (billing, shipping). The project does not exist, or the user has no role in it (a role in the organization does not grant one); …` |
| View not found | `Target:View 'x' was not found in acme/billing: the project's views are default, staging. Create it on the project's Views page (New view, an Administrator; …) or fix Target:View.` |
| No access | `Users:Pool:0 (u-…) has no access to acme/billing: the project is listed for the user, but its annotations show "You have no access here". …` |
| The catalog fails | `The annotations of acme/billing/default did not load for Users:Pool:0 (u-…): <the API's error>` |

The admin UI shows an organization or a project outside the user's memberships as "no access" whether it exists or not, so the check reads the user's memberships on `/orgs` to tell "not found" (which also covers a missing membership) from "no access" (listed, but the role is None). The check's screenshot, trace and console log are in `artifacts/<runId>/TargetCheck/CheckTarget/`; the message names the folder.

## The mock target

The admin UI's mock build serves the API with MSW in the browser and has Ada Lovelace signed in, so the suite and its page objects run without a deployed environment (it is the mock build of the TypeScript suite, not a sign-in bypass):

```bash
# once: the TypeScript SDK, which the admin UI installs as a packed copy of its dist/ (not committed; see the admin UI's README)
(cd src/Platform/Storyteller/sdk.typescript/src && npm ci && npm run build)

(cd src/Platform/ui.admin/src && npm ci && npm run dev:mock)    # in a second terminal: http://localhost:5174
UITESTS_PROFILE=mock dotnet run --project src/Platform/UI.Admin.AutomationTests/src
UITESTS_PROFILE=mock,mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"

# the bundled e2e build on :4174, as the CI workflow serves it
(cd src/Platform/ui.admin/src && VITE_MOCK_DATASET=default npm run build:e2e && npx vite preview --port 4174 --strictPort)
UITESTS_PROFILE=mock UITESTS_Target__BaseUrl=http://localhost:4174 dotnet run --project src/Platform/UI.Admin.AutomationTests/src
```

`MockScenario` `default` matters: `npm run dev:mock` starts the `showcase` dataset otherwise, which has no `acme`. In the default dataset Ada is Owner of `acme` and `acme/billing`, Contributor of `acme/shipping` and Reader of `northwind/portal` (no role on `northwind`); `acme/billing` has the views `default` and `staging`. The mock's data lives in the tab's `sessionStorage`: every test starts from the scenario's seed, and what it creates is gone with its page.

## Data rules

Tests create what they act on, in the target's view (or a new project in its organization), and delete nothing: the growing data set is intended load. Every generated name carries the run id (`yyyyMMdd-HHmmss-xxxx`, `PlatformData`): slugs `at-<runId>-<kind>-<hex4>-<word>-<word>` with the kind `org`, `prj`, `view` or `mach` (organizations, projects, views, machines; e.g. `at-20261031-142233-k3x9-prj-1f2e-dolor-amet`), every generated part of an annotation key (`rst.at-<runId>-<hex4>-<word>-<word>`, cut to 40, so the missing ancestors Bulk create adds carry it too), key values `at_<runId with '_' for '-'>.<word>_<hex4>` (e.g. `at_20261031_142233_k3x9.dolor_1f2e`; a segment must not start with a digit), labels `at-<runId>-lbl-<hex4>-<word>`, titles `<words> <hex4> (at-<runId>)`, e-mails `ui-tests+at-<runId>-<hex4>-<word>-<word>@<EmailDomain>`. The 4 random hex digits before the words keep the names of a run unique after the cuts to 40 and 63 characters (Bogus has only 181 words). Search the target for `at-<runId>` and `at_<runId with '_'>` to find a run's data. The data is seeded per test from the logged session seed and the test id, so `Execution:Seed` repeats a run's data (a retry gets fresh names); the run id still differs per run.

## The Elementary set

`--treenode-filter "/**[Category=Elementary]"`, one test per area; each creates what it acts on:

| Test | Area | User | What it does |
|---|---|---|---|
| `SignIn_ShowsOrganizations` | Access | any | `/orgs` lists the target's organization and project, signed in |
| `Annotations_CreateResponsibility_AppearsInGrid` | Annotations | editing | Bulk create (the toolbar; the command palette on a phone) creates `rst.at-<runId>-…`; the grid (the list on a phone) filtered by `key:` shows it, and its detail, opened by its URL, shows the key in its identity line |
| `Configurations_SetDocument_EffectiveShowsIt` | Configurations | editing | Bulk create creates a responsibility; Create empty, a generated JSON document in the editor, save (`v2`); the Effective tab shows every value |
| `KeyValues_CreateAndRead` | KeyValues | editing | A new key `at_<runId with '_' for '-'>.<word>_<hex4>` with a value; its detail shows the value and the Versions tab `v1(current)` |
| `Project_Create_OpensSettings` | Projects | Owner | Creates a project with a description in the organization; its Settings, opened through the navigation, show the description |
| `Navigation_MobileMenu_OpensPages` | Navigation | any | The sidebar (the "Open navigation" slideover below 1024 px; from 1024 to 1279 px the icon rail, expanded first) opens Configurations, Values store, Settings, Organizations and Annotations |

## Write a test

- Derive from `PlatformTest` (`Page`, `App`, `Target`, `Routes`, `PlatformData`, `User`, `Data`); mark it `[Test]`, `[Area(Area.<area>)]` (`Access`, `Annotations`, `Configurations`, `Graph`, `KeyValues`, `Modeling`, `Monitoring`, `Navigation`, `Projects`, `Settings`), `[Elementary]` for the PR gate set, `[Serial]` for a test that must run alone (after the parallel ones, restoring what it changes), and `[TestCase(<id>)]` with its Testiny case id (a number; a string such as `"TC-1234"` works too).
- Mark the role a test acts as with `[UserRole(Role.<role>)]` on the method (or the class; the method wins): `Editor` for anything that creates or edits, `Admin`, `Secrets`, `Owner`, or exactly `Contributor` or `Reader`, which act in the project `Target:RoleProjects` maps them to (on the mock `acme/shipping` and `northwind/portal`); `Routes` follows the role. Overriding `RequiredUser` still works for a whole class. `[DesktopOnly]` and `[PhoneOnly]` skip a test before its lease on the other layout, `[MockOnly("<why>")]` skips it on a deployed target, and `[DisposableProject]` marks a test that creates its own project (`CreateDisposableProjectAsync`).
- Restore what a test changed in an override of `CleanUpAsync`, not in an own `[After(Test)]` hook.
- Navigate with `App.GotoAsync(Routes.<page>)` or a page object's `OpenAsync`, relative to `Target:BaseUrl` (the first navigation carries the mock scenario); follow links with `App.NavigateAsync("<link>")`; locate by role, label and text.
- The page objects in `Pages/` follow the app's layouts: `App` (the shell: ready state, the no-access state, navigation in the sidebar or the slideover, the user menu, the command palette, toasts and their actions), `AppRoutes`, `LoginPage`, `AccountPage`, `OrganizationsPage`, `OrganizationOverview`, `ProjectSettingsPage`, `ViewsPage`, `AnnotationsPage` with `BulkCreateDialog` and `NewAnnotationDialog`, `AnnotationDetail`, `ConfigurationsPage`, `ConfigurationDetail`, `KeyValuesPage`, and Modeling's `ModelingPage`, `ModelingSchemaPage` and `ModelingTemplatePage`. The parts several pages share have their own: `ConfirmDialog` (a confirmation by its title, also the typed one), `DiscardChangesDialog`, `CodeEditor` (a CodeMirror editor by its name), `DetailPanel` (the detail's modes, Close and Back), `VersionsView` (the history of a configuration, a template or a schema), `TimeZoneSelect` and `ContextSwitcher`. Every one is `partial`: a section adds its members in `<Type>.<Section>.cs`. They branch on the viewport like the app: the phone layout below 640 px (sm), the list instead of the grid below 768 px (md), the slideover instead of the docked sidebar below 1024 px (lg), and the docked sidebar as the icon rail, whose links have no name until "Expand sidebar" widens it, from 1024 to 1279 px (below xl). An action returns once its effect shows (the page it opens, its dialog closed, the editor in place of the empty state, or its toast); the test asserts the exact text it cares about.
- The grids and lists of annotations and configurations are virtualized: look for a row only in a filtered result (`key:<key>`), or open the detail's URL.
- Assert a toast (`App.Toast("<title>")`) right after its action: it closes after about 5 s and covers the bottom right while open.
- No `Task.Delay`: Playwright's `Expect` waits up to `Execution:ExpectTimeoutMs`.

## Testiny

The Testiny reporter is registered by `PlatformAutomationSetup` and turned on by the `reporting-testiny` profile (`UITESTS_PROFILE=<target>,reporting-testiny`). The project id goes into `testsettings.local.json` (`{ "Reporting": { "Testiny": { "ProjectId": 12, "TestPlanId": null } } }`) or `UITESTS_Reporting__Testiny__ProjectId`, the API key (best restricted to the project) into the user secrets or `UITESTS_Reporting__Testiny__ApiKey`. Every key and what lands in Testiny: the library's [Reporting:Testiny](../../Libraries/Testing/UI.Automation/readme.md#reportingtestiny) and [Testiny](../../Libraries/Testing/UI.Automation/readme.md#testiny) sections.

The Elementary tests are linked to cases 1–6 of the Testiny project `2SP` (numeric id 1, folder Elementary). These IDs are resolved within the configured project; changing `ProjectId` does not remap them. If you select another project, update each `[TestCase(...)]` attribute to a case ID from that project. With `--test-parameter Reporting:UnmappedTests=Warn`, missing IDs are listed and their results are not reported. The default `Ignore` policy skips this validation and uses the IDs unchecked. The default run URL template is not verified against Testiny's app; set `Reporting:Testiny:RunUrlTemplate` to the form "Copy link to test run" shows.

```bash
UITESTS_PROFILE=mock,reporting-testiny dotnet run --project src/Platform/UI.Admin.AutomationTests/src
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --test-parameter Profile=reporting-testiny --test-parameter "Reporting:RunName=Nightly {Date:yyyy-MM-dd} {Engine} {Branch}"
# send a run again (e.g. one made while Testiny was down) as a new Testiny run; no test runs
UITESTS_PROFILE=mock,reporting-testiny dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --test-parameter "Reporting:ReplayFile=TestResults/artifacts/<runId>/results.jsonl"
```

## BrowserStack

The `browserstack-*` profiles run the browsers on BrowserStack Automate (keys and behaviour: the library's [BrowserStack](../../Libraries/Testing/UI.Automation/readme.md#browser-providers) section). The credentials go into the user secrets (or `UITESTS_BrowserStack__Username` and `UITESTS_BrowserStack__AccessKey`); set `BrowserStack:MaxParallelSessions` to the plan's parallel limit (`[assembly: BrowserStackSessionLimit]` applies it). The sign-in and every signed-in context run in BrowserStack's browser, so by default the sessions mask what is typed and the storage state a context starts from in BrowserStack's text and raw logs (`BrowserStack:MaskCommands`) and send no Playwright logs, which BrowserStack never masks (`BrowserStack:PlaywrightLogs` `false`); check a first session's text logs for `REDACTED VALUE`.

```bash
UITESTS_PROFILE=browserstack-win-chrome dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"
UITESTS_PROFILE=browserstack-mac-safari,reporting-testiny dotnet run --project src/Platform/UI.Admin.AutomationTests/src
UITESTS_PROFILE=browserstack-win-chrome,mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src      # Pixel 7, emulated in Chrome
UITESTS_PROFILE=browserstack-win-chrome UITESTS_BrowserStack__Browser=edge dotnet run --project src/Platform/UI.Admin.AutomationTests/src
```

A deployed target is reachable as it is; the mock on `localhost` needs BrowserStack Local (`BrowserStackLocal --key <access key> --local-identifier <id>`, then `BrowserStack:Local` `true` and `BrowserStack:LocalIdentifier` `<id>`). Without an account the provider can be checked against a local Playwright server: `npx -y playwright@1.63.0 run-server --port 3999` (besides `npm run dev:mock`), then `UITESTS_PROFILE=mock UITESTS_Browser__Provider=BrowserStack UITESTS_BrowserStack__Endpoint=ws://localhost:3999/ UITESTS_BrowserStack__Username=local UITESTS_BrowserStack__AccessKey=local-key-0000 dotnet run --project src/Platform/UI.Admin.AutomationTests/src`.

## Playwright 1.63

`Microsoft.Playwright` is pinned to **1.63.0** in `Directory.Packages.props` (the spec planned 1.64.1): a Playwright server refuses a client of another minor version, and BrowserStack's [version table](https://www.browserstack.com/docs/automate/playwright/browsers-and-os) ends at 1.63. Raise it only when BrowserStack runs the new version, and change with it the CI image and `PLAYWRIGHT_VERSION` in `.github/workflows/ui-admin-automation.yml` (its step "Playwright version matches the image", after the checkout, fails unless the pin, `PLAYWRIGHT_VERSION` and the job's image `mcr.microsoft.com/playwright/dotnet:v<version>-noble` agree, and prints all three) and the local browsers (`playwright.ps1 install`). The browsers of 1.63 share the machine's Playwright cache with the admin UI's TypeScript suite (`@playwright/test` 1.64): installing one version's browsers can remove the other's, so run `npx playwright install` in `src/Platform/ui.admin/src` before its e2e suite again.

## CI: `ui-admin-automation.yml`

`.github/workflows/ui-admin-automation.yml` runs the suite by hand (`workflow_dispatch`) or from another workflow (`workflow_call`); it has no push or pull request trigger yet (gating pull requests on the Elementary set, and a staging deployment per pull request, are follow-ups). The job runs in `mcr.microsoft.com/playwright/dotnet:v1.63.0-noble` (the browsers of the pinned Playwright, and a .NET 10 SDK that `global.json` accepts) as the runner's user 1001 (`--user 1001`, Playwright's own CI setup: Firefox refuses to start as the image's root when `HOME` belongs to another user), builds the project in Release and runs:

```bash
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -c Release --no-build -- --treenode-filter "<filter>" --report-trx --results-directory TestResults/ui-admin-automation
```

| Input | Default | Meaning |
|---|---|---|
| `profile` | empty | `UITESTS_PROFILE`, comma-separated (`mobile`, `firefox`, `webkit`, `browserstack-win-chrome`, `browserstack-mac-safari`; spaces are dropped); empty for the base settings |
| `filter` | `/**[Category=Elementary]` | The tree node filter; empty runs every test |
| `target_url` | empty | The admin UI to test; empty for the environment's `UITESTS_TARGET_BASE_URL` |
| `report_testiny` | `false` | Adds the `reporting-testiny` profile with the environment's project and API key |
| `use_mock_ui` | `false` | Builds the TypeScript SDK and the admin UI's e2e build (`npm run build:e2e` with `VITE_MOCK_DATASET=default`), serves it with `vite preview` on port 4174 and runs with `UITESTS_PROFILE=mock` (plus `profile`) and `UITESTS_Target__BaseUrl=http://localhost:4174`. Needs no secret, so it runs on any branch; a `browserstack*` profile is refused with it (BrowserStack's browsers would need BrowserStack Local) |

A run against a deployed target (and a mock run with `report_testiny`) uses the GitHub environment **`ui-admin-automation`**; a mock run without reporting uses none, so none of its protection rules apply. **Restrict the environment to `main`** (Settings, Environments, `ui-admin-automation`, Deployment branches and tags: Selected branches, `main`): its secrets are the pool users' passwords and the BrowserStack and Testiny keys, and without the rule a run of any branch, with that branch's version of the workflow, could read them; branch runs then use `use_mock_ui` (without `report_testiny`). Only the variables that are set reach the run, so a profile's values stay where a variable is empty:

| Variable | Becomes |
|---|---|
| `UITESTS_TARGET_BASE_URL` | `Target:BaseUrl` when `target_url` is empty |
| `UITESTS_TARGET_ORGANIZATION` / `_PROJECT` / `_VIEW` / `_EMAIL_DOMAIN` | `Target:Organization` / `Project` / `View` / `EmailDomain` |
| `UITESTS_USER_<n>_EMAIL`, `UITESTS_USER_<n>_ROLES` (n = 0…7; roles comma-separated, e.g. `Owner` or `Contributor, Reader`) | `Users:Pool:<n>:Email` and `Users:Pool:<n>:Roles`; a slot without an e-mail is skipped; any e-mail makes these slots replace the committed pool entirely |
| `UITESTS_BROWSERSTACK_MAX_PARALLEL_SESSIONS` | `BrowserStack:MaxParallelSessions` (with a `browserstack*` profile) |
| `UITESTS_TESTINY_PROJECT_ID` / `UITESTS_TESTINY_TEST_PLAN_ID` | `Reporting:Testiny:ProjectId` / `TestPlanId` (with `report_testiny`) |

| Secret | Becomes |
|---|---|
| `UITESTS_USER_<n>_PASSWORD` (n = 0…7) | `Users:Pool:<n>:Password`, applied by index to the pool (the committed one when no slot has an e-mail) |
| `UITESTS_BROWSERSTACK_USERNAME` / `UITESTS_BROWSERSTACK_ACCESS_KEY` | `BrowserStack:Username` / `AccessKey` when a profile starts with `browserstack` |
| `UITESTS_TESTINY_API_KEY` | `Reporting:Testiny:ApiKey` (with `report_testiny`) |

A caller passes the secrets explicitly (or `secrets: inherit`); a secret of the environment wins over the passed one. The `Run` step gets only the secrets of its path (the passwords for a deployed target, the BrowserStack ones with a `browserstack*` profile, the Testiny key with `report_testiny`; none for a mock run without reporting), and unsets its copies before the tests start, which see them only as their `UITESTS_*` settings.

```bash
# once the workflow is on main; --ref runs a branch's version of it
gh workflow run ui-admin-automation.yml --ref feat/my-branch -f use_mock_ui=true
gh workflow run ui-admin-automation.yml -f use_mock_ui=true -f profile=mobile -f filter=''
gh workflow run ui-admin-automation.yml -f target_url=https://admin.staging.example.com -f report_testiny=true
gh workflow run ui-admin-automation.yml -f profile=browserstack-win-chrome
```

```yaml
jobs:
  ui-mock:
    uses: ./.github/workflows/ui-admin-automation.yml
    with:
      use_mock_ui: true
  ui-staging:
    uses: ./.github/workflows/ui-admin-automation.yml
    with:
      target_url: https://admin.staging.example.com
      profile: browserstack-win-chrome
      report_testiny: true
    secrets: inherit
```

- The job's **step summary** gets TUnit's results (failures grouped by cause) and, with reporting, the Testiny run's link.
- A failed or cancelled run (`timeout-minutes` 60 included) uploads `TestResults/ui-admin-automation/` (TRX, TUnit's HTML and JSON reports, `run.log`, the artifacts with `results.jsonl`) as `ui-admin-automation-<mock|target>-<profile>-<attempt>` for 7 days. Artifacts of this public repository are readable by anyone, so the traces of a deployed target (every `.zip` under the results: the TRX keeps a retried test's traces as `trace.zip`, `trace_1.zip`, …) are always removed first; the mock's user is fake, so its traces stay. Run a deployed target's failing test locally to get its trace.
- Runs against a deployed target share its pool users, so every deployed target shares one concurrency group, `ui-admin-automation-target`; mock runs share one per branch. One run of a group runs at a time and up to 100 wait in order (`queue: max`; without it GitHub keeps one pending run per group, and a newer one cancels it); a running run is never cancelled. A caller that starts several target runs can also chain them with `needs:`.
- The inputs and variables reach the scripts only through `env:`, the secrets only the `Run` step's `env:` (no npm install script, action or build step sees them; `dotnet run --no-build` runs no build targets there). The settings dump shows secrets as `***`, the runtime redacts them in every message, and GitHub masks them in its log view (not in uploaded files).

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Every test skipped: `UITESTS_Target__BaseUrl is not configured; see docs/Platform/UI.Admin.AutomationTests/automation-tests.md.` | No target: set `Target:BaseUrl` (local settings, `UITESTS_Target__BaseUrl`) or a profile such as `mock` |
| Every test fails at once: `UI automation is not available: The target check failed: …` | The target lacks the configured organization, project or view, or the pool user lacks access; the message says which ("The target check") |
| Every test fails at once: `… The profile 'x' has no testsettings.x.json …`, `Users:Pool is empty …`, `Users:Pool:0:Password is required …` | The settings did not load; the first `ui-automation: error:` line lists every problem |
| `Executable doesn't exist at …ms-playwright…` on start | The browsers of Playwright 1.63 are not installed: `pwsh …/bin/Debug/net10.0/playwright.ps1 install chromium` (the channel's name with `Browser:Channel`) |
| A BrowserStack session refused for its version | The client (1.63) is not in BrowserStack's table; see "Playwright 1.63" |
| Tests Blocked: `No free user with role …` / `No user of Users:Pool has role …` | The pool is too small for the parallelism, or no user has the role the test asks for (`Roles` in `Users:Pool`) |
| Tests Blocked: `… before the engine's time for the start of the test ran out (e.g. its hook timeout)` | TUnit's hook timeout ended the start before the pool's or the provider's own timeout; the adapter raises it to the settings' start budget (library readme, "Tests: `UiTest`"), so another hook timeout (a `[Timeout]` on a hook) is lower than the budget |
| The sign-in fails (`sign-in-failure.png` in the test's folder) | The AuthKit hosted page changed or a checklist item is missing ("AuthKit checklist") |
| Exit code `8` | The filter matched no test; check the category (`Area-Access`, not `Access`) or add `--ignore-exit-code 8` |
| `dotnet test` on the project: "Testing with VSTest target is no longer supported …" | Run it with `dotnet run` ("Run") |
| Page loads time out in Firefox on the mock, or the first Firefox run fails at once in the target check (`The annotations of acme/billing/default did not show …`) | The unbundled dev server is slow in Firefox on a cold start (the first Firefox run after the dev server started took over 2 minutes for the check's first page); run again, use `--maximum-parallel-tests 2`, or the e2e build on 4174 |
| The admin UI's TypeScript e2e suite cannot find its browsers | The 1.63 browsers replaced its 1.64 ones in the shared cache: `npx playwright install` in `src/Platform/ui.admin/src` |
