# Admin UI automation tests

Browser tests of the integrated 2S Platform: the deployed admin UI (`src/Platform/ui.admin`), the real Storyteller API and the AuthKit sign-in, driven by **Playwright** under **TUnit**. The project is built on the reusable libraries `42.Testing.UI.Automation.*` (`src/Libraries/Testing/UI.Automation.*`): settings, browsers, the user pool, seeded data, artifacts and the reporting pipeline live there, the admin UI's sign-in, page objects, data generators and tests live here.

- **Spec:** [UI Automation Testing Project](../../../docs/Platform/UI.Admin.AutomationTests/specs/2026-10-31%20UI%20Automation%20Testing%20Project.md).
- **Not `dotnet test`:** TUnit runs on Microsoft.Testing.Platform 2.5, which refuses the VSTest mode of `dotnet test` on the .NET 10 SDK, and the repository stays in VSTest mode for its xUnit projects. `dotnet build src` builds this project; `dotnet test src` leaves it out (`Test="false"` in `src/Directory.Build.proj` and `src/Platform/Directory.Build.proj`). Run it with `dotnet run`, options after `--`.
- **Without a target** (`Target:BaseUrl` empty, the committed default) every test is skipped with a reason and the run exits `0`.

## Run

```bash
# once: build, then install the browsers of the Playwright version the project uses
dotnet build src/Platform/UI.Admin.AutomationTests/src
pwsh src/Platform/UI.Admin.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 install chromium firefox webkit   # Linux: install --with-deps

dotnet run --project src/Platform/UI.Admin.AutomationTests/src                                              # everything, headless Chromium
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"   # the PR gate set
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Area-Access]"
UITESTS_PROFILE=mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src                       # Pixel 7 emulation
UITESTS_PROFILE=webkit,mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src                # profiles combine, in order
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --test-parameter Execution:Seed=1383911092   # repeat a run's data
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --maximum-parallel-tests 1 --test-parameter Browser:Headless=false --test-parameter Browser:SlowMoMs=200
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --report-trx                             # also a TRX file
dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --list-tests
```

- A filter that matches no test exits `8` (Microsoft.Testing.Platform's "zero tests ran"); add `--ignore-exit-code 8` where that is fine.
- The terminal shows lines prefixed `ui-automation:`: the run id, the data seed, the effective settings (secrets as `***`), warnings, and at the end the totals and the path of `results.jsonl`.
- Everything goes to `src/Platform/UI.Admin.AutomationTests/src/bin/<configuration>/net10.0/TestResults/` (git-ignored): TUnit's HTML report (and the TRX with `--report-trx`), and `artifacts/<runId>/` with `results.jsonl` (every reported result, replayable through the reporters later) and a folder per test (`<class below the project namespace>/<method>`, e.g. `Tests.Access.SignInTests/SignIn_ShowsOrganizations`) with `failure.png`, `trace.zip` (open with `pwsh src/Platform/UI.Admin.AutomationTests/src/bin/Debug/net10.0/playwright.ps1 show-trace <file>`) and `console.log`. The files are also attached to the test's result in TUnit's reports.
- A `trace.zip` holds the test's network log and the context's options, so the session cookies and tokens of the pool user (never the passwords: the sign-in runs untraced) are in it: keep traces where the test results are private.
- IDE: Visual Studio "Use testing platform server mode", Rider "Enable Testing Platform support", VS Code C# Dev Kit "Use Testing Platform Protocol".

## Configure

The settings are read in layers, each over the one before:

1. `testsettings.json` — the committed defaults (no secrets);
2. `testsettings.<profile>.json` for each profile of `UITESTS_PROFILE` or `--test-parameter Profile=…` (comma-separated);
3. `testsettings.local.json` — your overrides, git-ignored, e.g. the target and the pool e-mails;
4. the user secrets of the project (`UserSecretsId` in the csproj);
5. `UITESTS_*` environment variables, `__` between sections (`UITESTS_Target__BaseUrl`, `UITESTS_Users__Pool__0__Password`);
6. `--test-parameter Section:Key=value` (a value may contain `=`).

| Key | Meaning |
|---|---|
| `Target:BaseUrl` | The admin UI, e.g. `https://admin.staging.example.com`; empty skips every test |
| `Target:Organization` / `Project` / `View` | The pre-provisioned organization, project and view the tests work in |
| `Target:SignIn` | `AuthKit` (deployed environments) or `None` (a target that is signed in already, the mock) |
| `Target:MockScenario` | Mock target only: `?mock-scenario=<name>` on the first navigation of each test |
| `Target:EmailDomain` | Generated e-mail addresses are `ui-tests+<slug>@<domain>` |
| `Browser:*` | `Provider` (`Local`; `BrowserStack` comes later), `Engine` (`chromium`, `firefox`, `webkit`), `Channel`, `Headless`, `SlowMoMs`, `Device` (a Playwright device such as `Pixel 7`), `Viewport`, `Locale`, `TimeZone`, `ColorScheme` |
| `Execution:*` | `MaxParallelTests` (lowered to the pool size), `Seed`, the `*TimeoutMs`, `Retries` (skips and data conflicts never run again), `Trace` / `Screenshot` / `Video` (`Off`, `OnFailure`, `Always`), `ArtifactsPath` |
| `Users:Pool` | The pre-provisioned users: `Email`, `DisplayName`, `Roles` (the user's role in the target's organization and project, which a test's `RequiredUser` matches); `Password` only in the user secrets. A test leases one for its duration |
| `Reporting:*` | The test case management integration; off, and without a reporter until the Testiny phase |

Secrets, from `src/Platform/UI.Admin.AutomationTests/src`:

```bash
dotnet user-secrets set "Users:Pool:0:Password" "…"
dotnet user-secrets set "Users:Pool:1:Password" "…"
dotnet user-secrets set "BrowserStack:Username" "…" ; dotnet user-secrets set "BrowserStack:AccessKey" "…"   # later, for BrowserStack
```

A `testsettings.local.json` for a deployed environment:

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

Profiles: `mobile` (Pixel 7), `firefox`, `webkit`, `mock`.

## The mock target

The admin UI's mock build serves the API with MSW in the browser and has Ada Lovelace signed in, so the suite and its page objects can run without a deployed environment. It is not a sign-in bypass of the API or the UI; the mock build exists for the TypeScript suite already.

```bash
(cd src/Platform/ui.admin/src && npm ci && npm run dev:mock)    # in a second terminal: http://localhost:5174
UITESTS_PROFILE=mock dotnet run --project src/Platform/UI.Admin.AutomationTests/src
UITESTS_PROFILE=mock UITESTS_Browser__Engine=webkit UITESTS_Browser__Device="Pixel 7" dotnet run --project src/Platform/UI.Admin.AutomationTests/src
```

`testsettings.mock.json` targets `http://localhost:5174` with `acme` / `billing` / `default`, `SignIn` `None`, `MockScenario` `default` (`npm run dev:mock` starts the `showcase` dataset otherwise) and four placeholder pool users with the role `Owner` (Ada Lovelace owns `acme` and `acme/billing`), which only bound the parallelism. The mock's data lives in the tab's `sessionStorage`: every test starts from the scenario's seed, and what it creates is gone with its page. The dev server serves the app unbundled, so the profile raises `Execution:ExpectTimeoutMs` to 15 s for slow page loads (Firefox with 4 parallel tests).

## The Elementary set

`--treenode-filter "/**[Category=Elementary]"` runs the PR gate set, one test per area; each creates what it acts on (in the target's view, or a new project in its organization), and the tests that create something lease a user with an editing role:

| Test | Area | What it does |
|---|---|---|
| `SignIn_ShowsOrganizations` | Access | `/orgs` lists the target's organization and project, signed in |
| `Annotations_CreateResponsibility_AppearsInGrid` | Annotations | Bulk create (the toolbar; the command palette on a phone) creates `rst.at-<runId>-…`; the grid (the list on a phone) filtered by `key:` shows it, and its detail, opened by its URL, shows the key in its identity line |
| `Configurations_SetDocument_EffectiveShowsIt` | Configurations | Bulk create creates a responsibility (no ancestors, and usually no type schema or template); Create empty, a generated JSON document in the editor, save (`v2`); the Effective tab shows every value |
| `KeyValues_CreateAndRead` | KeyValues | New key `at_<runId>.<word>_<hex4>` with a value; its detail shows the value and the Versions tab `v1(current)` |
| `Project_Create_OpensSettings` | Projects | A user with the role `Owner` creates a project with a description in the organization; its Settings, opened through the navigation, show the description |
| `Navigation_MobileMenu_OpensPages` | Navigation | The sidebar (the "Open navigation" slideover below 1024 px) opens Configurations, Values store, Settings, Organizations and Annotations |

```bash
UITESTS_PROFILE=mock dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"
UITESTS_PROFILE=mock,mobile dotnet run --project src/Platform/UI.Admin.AutomationTests/src -- --treenode-filter "/**[Category=Elementary]"
```

The page objects in `Pages/` follow the admin UI's layouts: `App` (the shell: ready state, navigation by link name in the sidebar or the slideover, the command palette, toasts), `AppRoutes` (the routes of the target's organization, project and view), `LoginPage` (the public `/login` page of the AuthKit sign-in), `OrganizationsPage`, `OrganizationOverview`, `ProjectSettingsPage`, `AnnotationsPage` with `BulkCreateDialog`, `AnnotationDetail`, `ConfigurationsPage`, `ConfigurationDetail` and `KeyValuesPage`. They branch on the viewport's width, as the app does: the phone layout below 640 px (sm), the list instead of the grid below 768 px (md), the slideover instead of the docked sidebar below 1024 px (lg). An action of a page object returns once its effect shows (the page it opens, its dialog closed, the editor in place of the empty state, or its toast when nothing else shows it); the test asserts the exact text it cares about.

## AuthKit checklist

`Target:SignIn` `AuthKit` signs each pool user in once per run, through `/login`, "Continue with AuthKit" and the AuthKit hosted page (e-mail, Continue, password, Sign in), and gives the user's later tests the captured storage state. The hosted page is WorkOS' UI; its labels are in `Access/AuthKitSignIn.cs`. The AuthKit environment of the target needs, by hand:

- password authentication enabled;
- no MFA (and no other second step) for the pool users;
- verified e-mail addresses of the pool users;
- each pool user a member of the default organization and project with the roles listed in `Users:Pool`;
- the target's redirect URI `https://<admin host>/callback` (and the sign-out redirect `/login`) registered;
- a custom auth domain (`authApiHostname` in the deployment's `app-config.json`) on any host other than `localhost`.

## Write a test

- Derive from `PlatformTest` (`Page`, `App`, `Target`, `Routes`, `PlatformData`, `User`, `Data`); override `RequiredUser` for a test that needs a role (`UserRequirement.WithRole("Owner")`, matched against the pool users' `Roles`): a test that creates or edits something asks for `EditingUser` (Contributor and up), since a Reader gets no create, edit or save action and the pool may hold one; mark it `[Test]`, add `[Elementary]` for the PR gate set and `[Area(Area.Annotations)]`; `[Serial]` for a test that must run alone (after the parallel ones, restoring what it changes); `[TestCase("<id>")]` links it to its test case in the test case management system.
- Restore what a test changed in an override of `CleanUpAsync` (it runs on the still open page; its failure fails the test), not in an `[After(Test)]` hook of your own: TUnit runs those with the result still `Passed`, so the report would miss their failure.
- A test that signs in itself (`SignedIn => false`) types the password on its traced page, and the trace keeps it: set `Execution:Trace` `Off` for such a run.
- Navigate with `App.GotoAsync(Routes.<page>)` or a page object's `OpenAsync`, relative to `Target:BaseUrl`, and follow links with `App.NavigateAsync("<link>")`; locate by role, label and text (`Pages/`).
- The grids and lists of annotations and configurations are virtualized: look for a row only in a filtered result (`key:<key>`), or open the detail's URL.
- Assert a toast (`App.Toast("<title>")`) right after its action: it closes after about 5 seconds, and while it is open it covers the bottom right of the page.
- Create what the test acts on, with names from `PlatformData` (`at-<runId>-…`, in every generated part of an annotation key, so the missing ancestors it creates carry it too; key values `at_<runId>.…`), and delete nothing: the growing data set is intended, and the prefix makes leftovers attributable.
- No `Task.Delay`: Playwright's `Expect` waits up to `Execution:ExpectTimeoutMs`.
