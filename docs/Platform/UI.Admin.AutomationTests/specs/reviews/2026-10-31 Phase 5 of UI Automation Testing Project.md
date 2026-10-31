# Phase 5 of UI Automation Testing Project

Linear: [424-133](https://linear.app/424fun/issue/424-133/create-ui-automation-testing-project).

## Overview

Phase 5 covers the fifth row of section 11 of [UI Automation Testing Project](../2026-10-31%20UI%20Automation%20Testing%20Project.md), "CI": the workflow of section 9 (`.github/workflows/ui-admin-automation.yml`) with its environment variables, secrets and artifact upload, and the documentation pages of section 10. It also delivers the session-start check of section 5.2 ("the only assumptions (default project exists, the user is a member) are checked once at session start"). Phase 1 deferred that check to Phase 2, and Phase 2 left it open. It is now an optional target check of the project's setup, which the runtime runs before the first test. The phase is three commits: `aaaca9c7` (the target check), `612548e5` (the workflow) and `baf92244` (the docs). After them, a final review of the whole branch (`main..baf92244`) through six lenses raised 45 findings. It confirmed 24 and rejected 21. All 24 are fixed in the working tree, and the fixes are committed right after this document, together with it and the [library review of Phase 5](../../../../Libraries/Testing/UI.Automation/specs/reviews/2026-10-31%20Phase%205%20of%20UI%20Automation%20Testing%20Project.md). This review describes them as part of the phase, marked "review fix"; the library review covers the runtime side of the check and of the fixes in more detail. The row's done-when, a green hand-run against staging that shows the TUnit summary and the Testiny run URL, is **not met**: the workflow has never run on GitHub, there is no staging deployment, and the GitHub environment `ui-admin-automation` does not exist yet.

| Commit | Content |
|---|---|
| `aaaca9c7` | The target check. Runtime: `IUiAutomationSetup.CheckTargetAsync`, `TargetCheckContext`, `TargetCheckException`, `AutomationSessionOptions.TargetCheck`, `AutomationSession`, `TargetCheckTests`. TUnit adapter: `UiAutomationUnavailableException`, `RetryPolicy`. Platform: `PlatformTargetCheck`, `ViewsPage`, `PlatformAutomationSetup.CheckTargetAsync`, the page objects the check reads, the pointers to the new docs page (25 files) |
| `612548e5` | `.github/workflows/ui-admin-automation.yml` |
| `baf92244` | The docs page, the library readme, four package readmes, the shortened project README, `docs/Platform/development.md`, `CLAUDE.md` (9 files) |
| The next commit | The fixes of the final review (30 changed files and 2 new test files: the workflow, the runtime, the TUnit adapter, `AuthKitSignIn`, `testsettings.json`, the docs, `CLAUDE.md`) and the two Phase 5 reviews |

| Project | Passed (final pass) |
|---|---|
| `src/Libraries/Testing/UI.Automation.Runtime/test` | 220 (189 after Phase 4, 198 after `aaaca9c7`) |
| `src/Libraries/Testing/UI.Automation.Runtime.TUnit/test` | 28 (25 after Phase 4, 26 after `aaaca9c7`) |
| `src/Libraries/Testing/UI.Automation.Reporting.Testiny/test` | 93 |
| `src/Platform/UI.Admin.AutomationTests/src` (`mock`; desktop Chromium, Pixel 7, Firefox, WebKit) | 6 each |

## What Was Done

### 5.2 The session-start check: the target check

The last sentence of section 5.2. Phase 1 planned it as a default member `OnSessionStartedAsync`, called from `UiAutomationSession.StartAsync`. It was built engine-neutral instead, in the runtime's session start.

#### The runtime (`42.Testing.UI.Automation.Runtime`)

- **`IUiAutomationSetup.CheckTargetAsync(TargetCheckContext, CancellationToken)`**: a default interface method that does nothing.
  - `AutomationSessionOptions.FromSetup` turns it into `AutomationSessionOptions.TargetCheck` only when the setup's type implements it. It reads the type's interface map, so a setup that keeps the default gets no check and no extra browser context.
- **Where it runs:** last in `AutomationSession.StartAsync`. The browser provider and the reporters are created by then, the reporters' settings are validated and the start is logged. No reporter has started yet, so a failed check creates no Testiny run.
- **`TargetCheckContext`**: `Session`, `Settings`, `User` (a leased pool user, any role), `Context` and `Page`, signed in through the session's cached sign-ins (so that user's first test does not sign in again), and `Scope`, for another context or user.
  - Its identity `TargetCheckContext.Test` is `TargetCheck.CheckTarget`. It names the artifacts folder `<run>/TargetCheck/CheckTarget/` and the BrowserStack session.
  - It is one more planned test of the browser provider, which `TestCompleted` tells when the check is done, so a session per class closes after it. It is not reported.
  - The screenshot, trace and console log follow the `Execution` policies.
- **`TargetCheckException`**: the check's own refusal. The session turns any failure of the check into `The target check failed: <message> The check's <artifacts> are in <directory>.`.
  - An exception of another type is named in front of its message.
  - A cancellation of the start stays a cancellation.
  - A good check logs `The target check passed in <n> s with <user>.`.
  - Review fix: the message is redacted, and an exception whose text holds a secret is not kept as the inner exception.

#### The TUnit adapter (`42.Testing.UI.Automation.Runtime.TUnit`)

- **`UiAutomationUnavailableException`**: a UI test cannot begin because the settings did not load or the session did not start. `RetryPolicy.ShouldRetry`, the predicate of `[ConfiguredRetry]`, never retries it.
- So a failed check fails every UI test once, at once (0 ms), with `UI automation is not available: The target check failed: …`, and the error is logged once.
- Review fixes: `UiAutomationSession.Require` redacts that message. The raised hook timeout (under "Final review") also covers the session hook that runs the check.

#### The Platform's check (`PlatformTargetCheck`)

- `PlatformAutomationSetup.CheckTargetAsync` binds `PlatformTarget` and runs `PlatformTargetCheck.RunAsync` on the context's page, as its user.
- **Steps:**
  - It opens the annotations of `Target:Organization` / `Project` / `View`. It waits for `AnnotationsPage.CatalogLoaded`, `AnnotationsPage.CatalogError` or `App.NoAccessHeading` ("You have no access here").
  - For a view other than `AppRoutes.DefaultView` (`default`), it also opens the project's Views page (`ViewsPage`) and looks for the view.
  - A good target costs one page load (about 2 s on the mock dev server), two for another view.
- **Not found vs. no access.** The admin UI shows an organization or a project outside the user's memberships as "no access", whether it exists or not.
  - So on "no access" the check reads the user's memberships on `/orgs` (`OrganizationsPage.ListedAsync`).
  - An organization or project that is not listed is "not found", which also covers a missing membership. A listed one is "no access": the user's role there is None.
- **Added for the check:** `ViewsPage` (Phase 2 listed it as not built; no test uses it yet), `AppRoutes.Views`, `App.NoAccessHeading`, `AnnotationsPage.CatalogLoaded` and `CatalogError`, and `OrganizationsPage.ListedAsync`.
- **Pointers to the docs page:** the skip reason (`PlatformAutomationSetup.ReadmeHint`), the empty-pool error and the comments of `testsettings*.json` and of the project file now point to `docs/Platform/UI.Admin.AutomationTests/automation-tests.md` instead of the README.

The messages, after `The target check failed:` (the first three as the mock runs printed them). Each one ends with `The check's failure.png, trace.zip, console.log are in <directory>.`:

| Problem | Message |
|---|---|
| Organization not found | `Target:Organization 'does-not-exist' was not found: it is not among the organizations of Users:Pool:0 (u-9c980b12) (acme, northwind). The organization does not exist, or the user is not a member of it or of one of its projects; fix Target:Organization or the user's memberships.` |
| Project not found | `Target:Project 'does-not-exist' was not found in organization 'acme': it is not among the projects of Users:Pool:0 (u-9c980b12) there (billing, shipping). The project does not exist, or the user has no role in it (a role in the organization does not grant one); fix Target:Project or the user's project role.` |
| View not found | `Target:View 'does-not-exist' was not found in acme/billing: the project's views are default, staging. Create it on the project's Views page (New view, an Administrator; …) or fix Target:View.` |
| No access | `<user> has no access to <org>/<project>: the project is listed for the user, but its annotations show "You have no access here". Give the user a role in the project (Users:Pool lists <roles> for it).` |
| The catalog fails | `The annotations of <org>/<project>/<view> did not load for <user>: <the error state's text>` |
| The Views page fails | `The views of <org>/<project> did not load for <user>; the page is at <url>.` |
| The page shows none of the states | `The annotations of acme/billing/default did not show for Users:Pool:0 (u-9c980b12); the page is at <url>: <Playwright's message>` (seen once, on a cold Firefox; see "Verification") |

Evidence of `aaaca9c7`, against the mock dev server:

- Good targets: desktop 6/6 (check 2.0 s), Pixel 7 6/6 (1.9 s), and a real non-default view, `Target:View=staging` (2.8 s).
- `UITESTS_Target__Organization`, `__Project` and `__View` set to `does-not-exist`: exit 2 after about 4 s each. The check failed once and its error was logged once, all 6 tests failed in 0 ms, and the check's artifacts were in `TargetCheck/CheckTarget/`. No Testiny run was created.
- With `Execution:Retries=2` nothing was retried: the TRX has one 0 ms result per test.
- `TargetCheckTests`: 9 tests, 10 with the review fix.
- **Deviation:** the check verifies **one** leased pool user. The memberships and roles of the other pool users are not checked, so a pool user without the role shows up as a failing (or Blocked) test.

### 9. CI: `ui-admin-automation.yml`

Section 9 planned `workflow_dispatch` (inputs `profile`, `filter`, `target_url`, `report`) and `workflow_call`, the image `v1.64.x-noble`, `UITESTS_*` from the environment `ui-admin-automation`, `--report-trx`, an upload of `TestResults/` on failure, the TUnit GitHub reporter and the Testiny run URL in the step summary, and `browserstack/github-actions` for BrowserStack.

- **Triggers:** `workflow_dispatch` and `workflow_call` with the same six inputs; no push or pull request trigger. `workflow_call` declares the nine secrets (`UITESTS_USER_0_PASSWORD` to `UITESTS_USER_5_PASSWORD`, `UITESTS_BROWSERSTACK_USERNAME`, `UITESTS_BROWSERSTACK_ACCESS_KEY`, `UITESTS_TESTINY_API_KEY`), all optional; a secret of the environment wins over a passed one. `permissions: contents: read`. The `run-name` names the target and the profiles.
- **Inputs:**

  | Input | Default | |
  |---|---|---|
  | `profile` | empty | `UITESTS_PROFILE`, comma-separated |
  | `filter` | `/**[Category=Elementary]` | `--treenode-filter`; empty runs every test |
  | `target_url` | empty | Else the environment's `UITESTS_TARGET_BASE_URL` |
  | `report_testiny` | `false` | **Deviation:** the spec's `report` |
  | `use_mock_ui` | `false` | **Added:** the mock UI in the job (below) |
  | `upload_traces` | `false` | **Added:** keep a deployed target's traces in the artifact (the repository is public) |

- **The job** `tests` ("UI automation tests"): `ubuntu-latest`, `timeout-minutes` 60.
  - It runs in the container `mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`. **Deviation** from `v1.64.x-noble`, following the Playwright pin of Phase 4. The tag exists on MCR, its .NET SDK (10.0.401) satisfies `global.json`, and its browsers are in `/ms-playwright`.
  - The container options are `--ipc=host --user 1001` (review fix).
  - Steps: Check the inputs; `actions/checkout@v4` (`fetch-depth: 0`, because the versioning fails on a shallow clone, and `persist-credentials: false`); Playwright version matches the image; `actions/setup-node@v4`, Mock UI – install and build and Mock UI – serve (mock runs only); Build (`dotnet build -c Release`); Run; Keep the traces private; Upload the test results.
- **Check the inputs** refuses two runs:
  - a run without a target (no `target_url`, no `UITESTS_TARGET_BASE_URL`, no `use_mock_ui`);
  - a `browserstack*` profile together with `use_mock_ui`, because BrowserStack's browsers could reach the job's mock UI only through BrowserStack Local.
- **Environment:** `ui-admin-automation` is attached only for a deployed target, or with `report_testiny` (**deviation**). A mock run without reporting gets no environment and none of its protection rules.
  - Its variables: `UITESTS_TARGET_BASE_URL`, `UITESTS_TARGET_ORGANIZATION`, `_PROJECT`, `_VIEW` and `_EMAIL_DOMAIN`, six fixed pool slots `UITESTS_USER_<n>_EMAIL` and `_ROLES` (n = 0 to 5), `UITESTS_BROWSERSTACK_MAX_PARALLEL_SESSIONS`, `UITESTS_TESTINY_PROJECT_ID` and `UITESTS_TESTINY_TEST_PLAN_ID`.
  - The `Run` step exports only the values that are set (`set_if`), so an empty variable never overrides a profile. A slot without an e-mail is skipped, and the roles are split on commas.
- **The mock UI** (`use_mock_ui`):
  - `actions/setup-node@v4` from the admin UI's `.nvmrc`, with the npm cache of both lock files.
  - `npm install --global npm@11`, the TypeScript SDK (`npm ci && npm run build`), then the admin UI's `npm ci && npm run build:e2e` with `VITE_MOCK_DATASET=default`.
  - `npx vite preview --port 4174 --strictPort` in the background, polled for up to 60 s.
  - The run gets `mock` before the profiles and `UITESTS_Target__BaseUrl=http://localhost:4174`. It needs no secret and runs on any branch; its traces stay in the artifact (the mock's user is fake).
- **BrowserStack:** when the profile list has a `browserstack*` profile, the `Run` step maps `UITESTS_BROWSERSTACK_USERNAME` and `_ACCESS_KEY` (as `CI_BROWSERSTACK_*`) and the session limit.
  - **Deviation: no BrowserStack Local in CI.** The Phase 4 review moved it to this phase. A deployed target needs none, and the mock UI on BrowserStack is refused instead.
  - **Deviation: no `browserstack/github-actions`.** `612548e5` used `browserstack/github-actions/setup-env@v1.0.6`. The review fix removed it: it exported the credentials to every later step through `GITHUB_ENV`, and it handed them to a third-party action.
- **Testiny:** `report_testiny` appends the `reporting-testiny` profile and maps the project, the test plan and the API key. After the run, the `Run` step copies every `ui-automation: <System> run: <url>` line of `run.log` into a "Test case management" list under TUnit's summary.
- **TUnit:**
  - The step runs `dotnet run --project … -c Release --no-build -- --treenode-filter <filter> --report-trx --results-directory TestResults/ui-admin-automation`, with its output teed to `run.log`, and exits with the run's exit code.
  - TUnit's GitHub reporter writes the step summary (`TUNIT_GITHUB_REPORTER_STYLE=collapsible`). Report aggregation is off (`TUNIT_AGGREGATE_REPORTS=off`), and TUnit's own artifact upload is disabled (`TUNIT_DISABLE_ARTIFACT_UPLOAD=true`).
- **Upload:** `actions/upload-artifact@v4` of `TestResults/ui-admin-automation` (TRX, TUnit's HTML and JSON reports, `run.log`, the artifacts with `results.jsonl`), named `ui-admin-automation-<mock|target>-<profile|base>-<attempt>`, kept for 7 days.
- **Concurrency:** every deployed target shares the group `ui-admin-automation-target`, because the runs share its pool users; mock runs share `ui-admin-automation-mock-<ref>`. `cancel-in-progress: false`.

Review fixes of the workflow:

- **Review fix (high): the job runs as the runner's user** (`--user 1001`, Playwright's own CI setup).
  - Why: the image's default user is root, and the runner sets `HOME=/github/home`, owned by uid 1001. Firefox refuses to start as root in a home of another user, so the `firefox` profile could not launch at all. Chromium and WebKit were not affected.
  - The workspace, the tool cache and `HOME` belong to 1001, and `/ms-playwright` is readable by anyone.
- **Review fix (medium): only the `Run` step gets the secrets.**
  - In `612548e5` every `CI_*` value, with the six passwords and the Testiny API key, was job-level `env:`. So the npm install scripts, both `npm ci` runs, `npm run build:e2e`, the background `vite preview`, the actions and the build all saw them.
  - Now the job's `env:` holds the inputs and `CI_TARGET_BASE_URL` (the input check needs it), and the `Run` step's `env:` holds the other variables and every secret.
  - `dotnet run --no-build` runs no package's build targets in that step.
  - This departs from section 9's `browserstack/github-actions` (above). The unpinned `npm install --global npm@11` stays: it is the convention of `ui-admin.yml`, `storyteller-conformance.yml` and `storyteller-sdk-release.yml`, and it no longer sees a secret.
- **Review fix (medium): every `.zip` under the results is deleted before the upload** of a deployed target's run (unless `upload_traces`), not only files named `trace.zip`.
  - Why: the TRX writer copies the artifacts of every attempt of a test into one folder and renames a clash with a `_1` suffix. So a retried test left `trace_1.zip`, with the pool user's session tokens, in the public artifact.
  - This was latent: `Execution:Retries` is 0 in every committed settings file.
- **Review fix (medium): the concurrency group queues** (`queue: max`).
  - Why: with `cancel-in-progress: false` alone, GitHub keeps one pending run per group, and a newer one cancels it. Three runs against the deployed target lost the middle one, while the workflow comment and the docs said that "Nothing is cancelled".
  - Now one run of a group runs and up to 100 wait, first in, first out. The comment and the docs say so.
- **Review fix (low): the image tag is checked.**
  - In `612548e5`, the step "Playwright version matches the image" compared the `Directory.Packages.props` pin only with `PLAYWRIGHT_VERSION`; the image is a literal, because `jobs.<id>.container` has no expression context for `env`.
  - The step now also reads the job's `image:` line from the workflow file, requires `mcr.microsoft.com/playwright/dotnet:v<PLAYWRIGHT_VERSION>-noble`, and prints the pin, `PLAYWRIGHT_VERSION` and the image.
  - This is the CI-image half of section 12's version logging, which the Phase 4 review deferred to this phase. The tag is printed in the job's log, not at session start, because the runtime cannot know the image it runs in.
- **Review fix (low): a profile list with spaces works.** The settings loader trims each profile, but `mobile, browserstack-win-chrome` did not match the workflow's `browserstack` tests: the credentials were missing and the mock-UI guard was bypassed. The input check and the `Run` step drop the spaces first.
- **Review fix (low): a cancelled run uploads its results too.** A run stopped by `timeout-minutes` is cancelled, not failed. The trace removal and the upload now run on `failure() || cancelled()`.

How the workflow was validated, without GitHub:

- **actionlint** could not run: the permission policy blocked downloading and running its binary.
- **The Phase 5 checker** (`p5/validate/check-workflow.mjs` in the scratch folder) parses the file strictly with the `yaml` package of the admin UI's `node_modules`. It checks that both triggers declare the same inputs, that every expression uses a known context and function, that no script embeds `${{ }}`, that every secret is declared for `workflow_call` and that every action is pinned. It caught the problems planted on purpose, and it reports none for the final file.
- `bash -n` passes on all 7 scripts.
- **An extra structural check after the fixes:** no secret in the workflow's or the job's `env:` or in any step but `Run`; `queue: max` with `cancel-in-progress: false`; `--user 1001`; no `browserstack/` action; the cancel conditions; `--no-build`.
- **Simulated steps** in a runner-like environment, with the workflow's own scripts and the exact `dotnet run` command:
  - Before the fixes:
    - The version guard and the Release build passed. The simulation needed a `GITHUB_ENV` file, because the versioning task writes to it when `GITHUB_ACTIONS` is set.
    - The mock UI with the Elementary filter: exit 0, with TUnit's step summary, the TRX, HTML and JSON reports, `run.log` and the artifacts.
    - Mock, `mobile` and Testiny against the fake Testiny server of Phase 3: the summary got `- Testiny: [http://127.0.0.1:5399/42/testruns/1](…)`, and the API key appears 0 times in the output and the results.
    - The deployed-target path, with the mock standing in as the target: `Owner, Contributor` became two roles, an empty slot and its password were skipped, and the passwords showed only as `***`.
    - A failing run failed the step with exit 2.
    - The input check refused a run without a target, and a `browserstack*` profile with the mock UI.
  - After the fixes:
    - The version step passes, and it fails for a `PLAYWRIGHT_VERSION` of 1.64.0 and for an image of `v1.62.0-noble`.
    - The input check refuses `mobile, browserstack-win-chrome` with the mock UI and accepts it for a deployed target.
    - The `Run` step maps that spaced list's BrowserStack credentials and roles.
    - The trace step deletes `trace.zip` and `trace_1.zip` and keeps `failure.png` and `run.log`.
- **Never run on GitHub:** `workflow_dispatch` works only once the file is on `main`, and the environment does not exist (see "Not verified and open").

### 10. Documentation and `CLAUDE.md`

- **`docs/Platform/UI.Admin.AutomationTests/automation-tests.md`** (new): Run, Configure and its profiles, Users and roles, the AuthKit checklist, The target check (with the messages table), The mock target, Data rules, The Elementary set, Write a test, Testiny, BrowserStack, Playwright 1.63, CI: `ui-admin-automation.yml` (inputs, variables, secrets, `gh workflow run` and `workflow_call` examples) and Troubleshooting.
- **`docs/Libraries/Testing/UI.Automation/readme.md`** (new): the packages, how a run works, every setting with its default, the setup (`IUiAutomationSetup`) and its target check, the browser providers, `UiTest`, the attributes and filters, reporting and Testiny, how to add a reporter, adopting the framework, running, and limits.
  - **Deviation:** the adoption recipe documents `[assembly: UiAutomationSetup<T>]` and the other assembly attributes, as built in Phase 1, not the spec's `Session.cs`.
- **A `package-readme.md` per library** (`UI.Automation.Abstractions`, `.Runtime`, `.Runtime.TUnit`, `.Reporting.Testiny`). A `dotnet pack` of Runtime and Reporting.Testiny into the scratch folder showed `package-readme.md` in each `.nupkg` and `<readme>package-readme.md</readme>` in its `.nuspec`; `.artifacts` was not touched.
- **`src/Platform/UI.Admin.AutomationTests/README.md`** is cut down to a 24-line entry point (**deviation**: section 10 does not plan this). Its content moved to the docs page.
- **`docs/Platform/development.md`** links the page and got a short "UI automation tests" section.
- **`CLAUDE.md`:** TUnit 1.76 and Playwright 1.63.0 in the tech stack, the target check in the Platform table, the libraries list and the testing notes, the docs page in the build commands, and the workflow under CI/CD. Review fix: the CI/CD line names the user 1001 and the step-scoped secrets.
- A script of Phase 5 confirmed that every public settings property (`Target`, `Browser`, `Viewport`, `Execution`, `Users`, the pool user, `BrowserStack`, `Reporting`, `Testiny`, `PlatformTarget`) is documented with the default the code uses, and that every relative link and anchor of the new and changed pages resolves.
- **Review fixes of the docs:**
  - Troubleshooting quoted the old skip message with the README path; it now quotes `… see docs/Platform/UI.Admin.AutomationTests/automation-tests.md.`.
  - The mock target's commands build the TypeScript SDK first. The admin UI installs it as a packed copy of its `dist/`, which is not committed, so `npm ci && npm run dev:mock` on a fresh clone could not resolve `@42for.net/2splatform.sdk`.
  - The data rules give the real shapes: `at-<runId>-<kind>-<word>-<word>` with the kind `org`, `prj`, `view` or `mach`; key values `at_<runId with '_' for '-'>.<word>_<hex4>`; e-mails `ui-tests+at-<runId>-<word>-<word>@<EmailDomain>`.
  - The CI section describes the user 1001, the step-scoped secrets, the BrowserStack mapping without `setup-env`, the queue, the `.zip` removal and the upload after a cancel. "Playwright 1.63" describes the image check and no longer calls it the "first step", which it is not.
  - The page and the library readme point to both review folders: Phases 1, 2 and 5 here, and the libraries' Phases 3 to 5 under `docs/Libraries/Testing/UI.Automation/specs/reviews/`.
  - New: "Secrets in messages", the typed AuthKit fields, BrowserStack's masking, the start budget and the hook timeout, a Troubleshooting row for a start that the hook timeout ended, and the Firefox row widened to a cold-start failure of the target check.

### Deviations from the spec

1. The target check is the setup's optional `CheckTargetAsync`, which the runtime runs for every engine, and the Platform's check verifies one pool user, not every user's membership (5.2).
2. A run whose session is not available fails each UI test with `UiAutomationUnavailableException`, which is never retried (5.2, 4).
3. The input `report` is `report_testiny`; `use_mock_ui` and `upload_traces` were added (9).
4. The image is `v1.63.0-noble`, not `v1.64.x-noble` (9, following Phase 4).
5. No BrowserStack Local and no `browserstack/github-actions` in CI; the BrowserStack secrets reach only the `Run` step (9).
6. The environment is attached only for a deployed target or with `report_testiny` (9).
7. The CI image tag is checked and printed by the workflow, not logged at session start (12).
8. The adoption recipe uses the assembly attributes, not `Session.cs` (10).
9. The project README is only an entry point, and the docs page holds the content (10).
10. Added by the final review, not in the spec: the redaction of secret values in reports, messages and the terminal log; `BrowserStack:MaskCommands` and `BrowserStack:PlaywrightLogs`; `UiAutomationSettings.StartBudget` with the raised hook timeout and `TestScopeOptions.TestCancellation`.

Two statements of committed reviews were wrong. The reviews stay as committed, and the corrections are recorded here:

- Phase 1 says "The refresh path (`RefreshAsync`, `IsSignInStorageItem`, `KeepSignInsAsync`) is covered by unit tests only". Only `StorageStateCache.RefreshAsync` had tests. `TestScope`'s hand-off (`KeepSignInsAsync`) had none until `TestScopeTests`, and the Platform's `IsSignInStorageItem` still has none.
- Phase 4 says that with a session per class "a test is done when it released a browser or when its final report arrives", and it lists "a retry" among the cases of the idle close. Both held only while other tests of the class were still to come: the release of an attempt that TUnit retries closed the session. Now a test is done only at its final report (`TestCompleted`).

### Final review

One review of the whole branch, `main..baf92244`, ran six lenses: **security**, **completeness** (against the issue, the spec and the reviews), **docs**, **ci**, **code** and **tests**. Two skeptics checked each finding independently, and a finding was confirmed only when neither could refute it.

| Lens | Findings | Confirmed | Rejected |
|---|---|---|---|
| security | 11 | 5 | 6 |
| completeness | 4 | 3 | 1 |
| docs | 10 | 7 | 3 |
| ci | 6 | 4 | 2 |
| code | 9 | 3 | 6 |
| tests | 5 | 2 | 3 |
| **Total** | **45** | **24** | **21** |

The 24 confirmed findings are 18 distinct problems: six were found by two or three lenses. All are fixed:

| Severity | Lens | Finding | Fix |
|---|---|---|---|
| High | security | A failed AuthKit sign-in put the pool user's password in clear text into `run.log`, the TRX, `results.jsonl`, Testiny and BrowserStack: Playwright's call log keeps the value of a fill, and `StorageStateCache` copied the message and kept the raw inner exception. Through a failed target check it became every test's message | `SignInException` carries the original exception's type and message with the e-mail and the password as `***`, and no inner exception. Session-wide redaction (`SettingsRedaction.SecretValues`, `SettingsRedaction.CreateRedactor`, `AutomationSession.Redact`): every report's failure message, stack trace and output, the target check's message, `UiAutomationUnavailableException` and every `ui-automation:` line (`ConsoleSessionLogger.Redact`). Pool e-mails are `***` in reports |
| High | ci | The `firefox` profile could not launch in the container (Firefox as root with `HOME` owned by uid 1001) | `--user 1001` |
| Medium | security | BrowserStack's session logs kept the pool passwords and the signed-in storage state | `BrowserStack:MaskCommands` (default `sendType, sendPress, setHTTPCredentials, setStorageState`) and `BrowserStack:PlaywrightLogs` (default `false`), sent as `browserstack.maskCommands` and `browserstack.playwrightLogs`. `AuthKitSignIn` types the e-mail and the password (`ClearAsync`, then `PressSequentiallyAsync`), because BrowserStack masks a typed value but not a fill. The Selenium mask names in a comment are gone |
| Medium | security | Deleting only files named `trace.zip` missed the TRX copies of retried tests' traces (`trace_1.zip`) | Every `.zip` under the results |
| Medium | security | Pool passwords and the Testiny API key were job-level `env:`, visible to npm scripts, actions and the preview server | Secrets in the `Run` step's `env:` only, `--no-build`, no `setup-env` |
| Medium | ci, docs; low: code | The concurrency group did not queue: a third run cancelled the pending one, while the docs said "Nothing is cancelled" (3 findings) | `queue: max`; the comment and the docs |
| Medium | completeness, docs | Phase 5 had no review (2 findings) | This review and the library one, with the deviations and the corrections of the Phase 1 and Phase 4 statements |
| Medium | code | `BeginUiTest` ran under TUnit's 5-minute default hook timeout, shorter than the lease, the BrowserStack connection and the first sign-in together. A slow start ended as "A task was canceled", not with the pool's or BrowserStack's message | `UiAutomationSettings.StartBudget` (`Users:LeaseTimeoutSeconds`, plus `BrowserStack:ConnectTimeoutSeconds` on BrowserStack, plus 5 × `Execution:NavigationTimeoutMs`, plus a minute: 5.5 minutes locally, 10.5 on BrowserStack by default). `UiAutomationSession.ConfigureDiscovery` raises `DefaultHookTimeout` to it when it is longer. `UiTest.BeginUiTest` passes `TestContext.Execution.CancellationToken` as `TestScopeOptions.TestCancellation`, so a start that the hook timeout still ends is Blocked (`UserLeaseException`, `BrowserUnavailableException`) with its own message |
| Medium | tests; low: code | With a session per class, an attempt that TUnit retries counted as done, so the class session closed, marked from the failed attempt; `SessionPerClass_MarksATestByItsLastAttempt` could not catch it (2 findings) | A release no longer marks a test done; only its final report does (`TestCompleted`). New tests: `SessionPerClass_KeepsTheSession_ForTheRetryOfTheLastOpenTestOfTheClass`, `SessionPerClass_KeepsTheSessionOfAOneTestClass_ForItsRetry` |
| Medium | tests | `TestScope`'s sign-in hand-off had no test, though the Phase 1 review said it had | `TestScopeTests` (8) and 3 more `StorageStateCacheTests`; `FakeSignInFlow` and `FakeBrowserProvider` can fail and hold |
| Low | security | The redactor used the untrimmed access key, while the caps URL carried the trimmed one | `SecretRedactor` adds the trimmed form of every secret (tested with `\n`, `\r\n` and a space) |
| Low | completeness, docs | The CI image tag was neither checked nor logged, and "whose first step compares them" was wrong (2 findings) | The step checks and prints the image; the docs |
| Low | completeness, docs | Troubleshooting quoted the old skip message (2 findings) | The docs path |
| Low | ci | A profile list with spaces skipped the BrowserStack detection | Spaces dropped |
| Low | ci | A run stopped by `timeout-minutes` uploaded nothing | `failure() \|\| cancelled()` |
| Low | docs | The slug shapes of the data rules omitted the kind segment | Corrected |
| Low | docs | The `dev:mock` command skipped the SDK build | The SDK build first |
| Low | docs | The pointers to the reviews missed the library reviews | Both folders |

Rejected (21: 19 by both skeptics, 2 by one of the two):

- **security (6):**
  - Testiny receives failed tests' traces: the spec and the issue require it, and the docs mean "do not publish" a trace, which Testiny is not.
  - Any `target_url` gets the pool passwords: only a writer can start the workflow, and a caller in another repository gets its own environment.
  - A third-party action pinned to a tag: the repository's convention; moot now, because `setup-env` is gone.
  - The environment's protection rules are not specified: a repository setting, not a defect of the branch.
  - The settings dump prints the pool e-mails and the user info of a `Target:BaseUrl`: e-mails are configuration by the spec, and the user info was deferred in Phase 1.
  - Credentials may go over `http://` (Testiny) or `ws://` (BrowserStack): the owner's own configuration, documented.
- **completeness (1):** section 3.6's Maintenance replay test does not exist: the hook-based replay and its limit are recorded in Phase 1.
- **docs (3):**
  - "Keep traces private" omits Testiny: accurate as written.
  - A pool of six for 4 parallel tests warns on every run: no committed test leases a second user.
  - The Abstractions package readme says every engine adapter uses the `Maintenance` category (one skeptic of two): the readme lists the class's constants; left as is.
- **ci (2):**
  - Job-level secrets (one skeptic of two): the same finding was confirmed through the security lens and fixed.
  - Two calls in one caller run upload artifacts of the same name: nothing calls the workflow yet.
- **code (6):**
  - The untraced sign-in still runs in BrowserStack's browser: the docs promise only trace-free sign-ins. The exposure itself was confirmed through the security lens and fixed by the masking.
  - The target check is a single attempt: fail-fast by design.
  - The settings dump logs the pool e-mails: by the spec.
  - A failed start disposes the browser provider unguarded: neither shipped provider throws there.
  - The version step never checks the image: rejected by its two skeptics, but the same problem was confirmed through two other lenses and fixed.
  - `UiAutomationSession` next to `AutomationSession`: both names are the spec's.
- **tests (3):**
  - The dispatcher's deadlines and isolation are untested, and so are the TUnit adapter's final-attempt and report de-duplication: coverage gaps, not defects; their behaviour was checked end to end in Phases 1 and 3.
  - `StartAsync_RunsTheCheckOnASignedInPage_BeforeAReporterCreatesItsRun` never signs in: the sign-in is `TestScope`'s, now covered by `TestScopeTests`.

## Verification

Evidence of the commits is under 5.2, 9 and 10. After the review fixes:

| Check | Result |
|---|---|
| `dotnet build src/Libraries --no-incremental` | 0 errors, 303 warnings (as in Phases 3 and 4 and after `aaaca9c7`), none from `UI.Automation.*` |
| `dotnet build src/Platform/UI.Admin.AutomationTests/src --no-incremental` | 0 warnings, 0 errors |
| Unit tests | Runtime 220 (was 198), Runtime.TUnit 28 (was 26; `ConsoleSessionLoggerTests`), Reporting.Testiny 93: all passed |
| Mutation check | Putting back each of three old behaviours (the done mark at `ReleaseAsync`, the unredacted `SignInException` with its inner exception, the redactor without trimmed forms) makes 7 of the new tests fail, among them both retry tests, the sign-in test, the report test and the three trimmed-key cases |
| `UITESTS_PROFILE=mock`, headless Chromium | 6/6 passed, exit 0, check 2.0 s (`20261031-233117-v0vu`; on the final build `-234159-e2cy`, 2.0 s) |
| `mock,mobile` (Pixel 7) | 6/6 passed, exit 0, check 1.9 s (`-233148-qegg`; final build `-234244-9qe7`) |
| `mock,webkit` | 6/6 passed, exit 0, check 3.2 s (`-233457-hl1t`; final build `-234451-i85u`, 3.3 s) |
| `mock,firefox` | The first Firefox run of the pass (`-233252-djrt`) failed all 6 tests at once in the target check after about 2 minutes: `The annotations of acme/billing/default did not show for Users:Pool:0 (u-9c980b12); the page is at http://localhost:5174/acme/billing/default/annotations?mock-scenario=default: Locator expected to be visible`. This was Firefox's cold start on the unbundled dev server. The next two runs passed 6/6, exit 0, with the check in 6.0 s (`-233535-vjyj`) and 50.3 s (final build, `-234316-fzai`). Added to Troubleshooting |
| No target configured | 6 skipped (`UITESTS_Target__BaseUrl is not configured; see docs/Platform/UI.Admin.AutomationTests/automation-tests.md.`), exit 0, before and on the final build |
| Workflow | The Phase 5 checker reports no problems, `bash -n` passes on all 7 scripts, the extra structural check passes, and the simulated steps behave as listed under 9 |
| Hook timeout (a TUnit 1.76.45 probe) | A `Before(TestDiscovery)` hook that sets `DefaultHookTimeout` changes the timeout of an instance `Before(Test)` hook. At 2 s, the hook's token is cancelled at 2.0 s while `TestContext.Execution.CancellationToken` is not; at 8 s, a 4 s hook passes |
| BrowserStack's answer to wrong credentials | By accident, one simulated `Run` step (`mobile, browserstack-win-chrome`) ran for real and connected once to `wss://cdp.browserstack.com/playwright` with made-up credentials. All 6 tests failed at once through the target check: `BrowserUnavailableException: The BrowserStack session 'TargetCheck.CheckTarget' did not start at wss://cdp.browserstack.com/playwright: TargetClosedException: Error: Invalid username or password`. The made-up access key and passwords appear nowhere in the output. The library readme records the message |
| Phase 5 done-when | **Not met**: no GitHub run, no staging, no environment `ui-admin-automation` |

Every passing mock run had 1 warning, the pool of 4 for 4 parallel tests. All mock runs went against the dev server on port 5174 (`npm run dev:mock`).

## Not verified and open

- **The deployed environment and AuthKit.** There is no staging deployment, so the real API, the AuthKit hosted page and a bundled build are untested:
  - the typed sign-in (`ClearAsync`, `PressSequentiallyAsync`);
  - the redacted `SignInException` on a real AuthKit failure;
  - the pool's roles and memberships (the target check covers one user);
  - the "no access", catalog-error and Views-error messages of the check, which the mock cannot produce.
- **Testiny.** Only the fake server of Phase 3 received runs; a scratch Testiny project and the `[TestCase]` ids are still to come.
- **BrowserStack.** No session ever started, so nothing confirms that a Playwright 1.63 client starts a session there: the one connection was refused at the credentials. Also open on a real session:
  - `browserstack.maskCommands` (the text logs should show `REDACTED VALUE`);
  - `browserstack.playwrightLogs`, whose key name is inferred from BrowserStack's docs;
  - the session names and marks.
  - Check the first real session for all of these.
- **The workflow on GitHub.** It runs only after it reaches `main`, and nothing of it has run there:
  - the container as user 1001, Firefox in it, `actions/setup-node` and the npm steps as that user;
  - the background `vite preview` staying alive between steps;
  - the empty-string `environment:` expression;
  - `queue: max`;
  - the upload of a cancelled run;
  - TUnit's step summary on a real runner;
  - actionlint.
- **The environment `ui-admin-automation`** is still to be created:
  - its variables (the target, organization, project, view and e-mail domain; the user slots; the Testiny project and plan; the BrowserStack session limit);
  - its secrets (the passwords, BrowserStack, Testiny);
  - its protection rules, such as the deployment branches that may use the secrets (raised by a rejected finding).
- **The admin UI's TypeScript e2e suite** needs `npx playwright install` in `src/Platform/ui.admin/src` before its next local run. Installing Playwright 1.63's browsers in Phase 4 replaced the 1.64 builds in the shared browser cache. This is in the docs' Troubleshooting.
- **`27fd74ea` (Phase 3) does not compile alone:** its `ReporterSelection.CreateAsync` requires a logger that its caller passes only in `3666bd8a`. The two commits build together. This was found by reading the commits, not by building `27fd74ea`.
- **A hook timeout in a real run:** only the probe and the unit tests show the mapping; no run had a start that slow.
- **Open after the rejected findings:**
  - two `workflow_call` jobs of one caller run with the same mode and profile would collide on the artifact name;
  - the dispatcher's deadlines and the adapter's final-attempt logic have no unit tests;
  - the target check has a single attempt, so a transient failure fails the whole run.
- **Open as in Phase 4:**
  - with a session per class, the open sessions can outnumber `MaxParallelSessions`;
  - a retry after `IdleCloseAfter` opens a new session, so the class's mark is split over two sessions.
- **Later:** a pull request trigger on the Elementary set, a staging deployment per pull request, a check of every pool user's memberships at start, and Phase 6 (full-set tests, the `Maintenance` purge, `[Serial]` tests).
