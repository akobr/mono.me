# Phase 4 of Initial Test Cases of the Admin UI: Verification and Documentation

## Overview

Phase 4 of [2026-11-02 Initial Test Cases of the Admin UI and Their Automation](../2026-11-02%20Initial%20Test%20Cases%20of%20the%20Admin%20UI%20and%20Their%20Automation.md) (Linear [424-152](https://linear.app/424fun/issue/424-152/create-initial-tests-cases-and-automate-them-for-admin-ui)) covers §5 and §6. The merged suite of 233 tests was run against the mock e2e build on desktop and on the phone profile, twice on desktop. The Testiny cases were brought in line with the tests. The living documentation was updated, and a final cross-cutting review covered the documents, the consistency of the ten sections' code, and readiness for a deployed target. Its fixes were verified with another full run on both profiles.

## What Was Done

### 5. Verification

- **Build**: `dotnet build src/Platform/UI.Admin.AutomationTests/src --no-incremental` gives 0 warnings and 0 errors.
- **Library tests**: Runtime.TUnit 31, Runtime 227 and Reporting.Testiny 111 tests pass.
- **Target**: the mock e2e build (`VITE_MOCK_DATASET=default npm run build:e2e`), served with `vite preview --host 127.0.0.1 --port 4174` and `UITESTS_Target__BaseUrl=http://127.0.0.1:4174`. On the first full phone run, one asset request to `localhost:4174` was refused (`net::ERR_CONNECTION_REFUSED`): the preview listened on `::1` only, and one test failed before the app loaded. Serving on `127.0.0.1` removed it, and `automation-tests.md` gives the cause and the fix under Troubleshooting.
- **Full suite** (`--treenode-filter "/**"`, 4 parallel tests). The skips in `results.jsonl` are exactly the mode skips: "Phone only: the viewport is 1440 px wide …" on desktop and "Desktop only: the viewport is 412 px wide (Pixel 7) …" on the phone.

  | Run | Profile | Passed | Failed | Skipped | Duration |
  |---|---|---|---|---|---|
  | `20261102-044113-s2sv` | `mock` | 230 | 0 | 3 (PhoneOnly) | 7 min 18 s |
  | `20261102-044832-oask` | `mock,mobile` | 215 | 0 | 18 (DesktopOnly) | 5 min 50 s |
  | `20261102-045423-j2dd` | `mock` | 230 | 0 | 3 (PhoneOnly) | 6 min 14 s |
  | `20261102-060438-2u1q` | `mock,mobile`, after the final fixes (§ Final review) | 215 | 0 | 18 (DesktopOnly) | |
  | `20261102-061106-owsu` | `mock`, after the final fixes | 230 | 0 | 3 (PhoneOnly) | |
- **The Elementary set** (`/**[Category=Elementary]`): 10 tests, one per section. 10 passed on `mock` (13 s) and on `mock,mobile` (21 s).
- **Skip mode** (no `Target:BaseUrl`): 233 skipped, exit 0.
- **Static check**: every catalogue case has exactly one test, with its class, folder, `[TestCase]` id, `[UserRole]`, mode attributes and `[Elementary]`. No test sleeps.
- **The mock pool**:
  - The user secrets of the project now hold the passwords of a deployed pool of eight users (`Users:Pool:0..7:Password`). With the mock profile's four placeholders, the settings' validation failed (`Users:Pool:4:Email is required`).
  - The mock profile now has eight placeholder users (commit `de3e2d4e`). The secrets were not changed.
- **Not run**: no deployed target was available. These paths were reviewed but never run:
  - The AuthKit hosted-page sign-in of ACC-SGN-02/03 and ACC-MYI-03.
  - The second-page writers of the concurrent-edit cases.
  - The audit polling (`Eventually`) of the Monitoring cases.
  - The branches for large views of the Graph cases.
  - The `Reporting:UnmappedTests=Warn` check against Testiny.
  
  A first run against a deployed target with `reporting-testiny` and `--test-parameter Reporting:UnmappedTests=Warn` confirms that every id exists in `2SP` and exercises these paths.

### Testiny after the implementation

28 cases were edited to describe what their tests do:

- **Access**: 104, 19, 79, 161, 216.
- **Annotations**: 114.
- **Configurations**: 81, 187, 63, 83.
- **Modeling**: 93, 96, 105, 147, 149, 208.
- **Graph**: 133, 60, 138.
- **Key-Values**: 213, 194, 195, 84, 85.
- **Settings**: 170.
- **Navigation**: 72.
- **Monitoring**: 7, 25.

Each edit records a changed step, an added step, or a precondition about data that accumulates on a deployed target. The cases hit by the four admin UI bugs (Phase 3) keep their intended expected result, with the note "Known admin UI bug (found in 424-152): …; the automated test accepts both." Two implementation notes needed no Testiny change (GRA-TBR-01, SET-SEC-01).

### Final review

Three independent reviewers read the whole branch:

- **Documentation**: 12 findings, 1 must.
- **Consistency of the ten sections' code**: 23 findings, all should.
- **Readiness for a deployed target**: 7 findings, 3 must.

Applied:

- **AuthKit password** (must, `fix(platform): keep the AuthKit password out of the traces and errors of the sign-in tests`, `2d60982d`), in the hosted-page sign-in of `PlatformTest.ACC.cs`:
  - After a failed sign-in, the trace chunk restarts only once the hosted page's inputs are emptied (or the page is `about:blank`). Otherwise Playwright's snapshot of the failure screenshot would have kept the typed password in `trace.zip`.
  - A failure is rethrown as a `SignInException` without the inner exception, with the password and the e-mail as `***`, like `StorageStateCache.Failed`. Before, Playwright's call log carried the typed text into the message.
- **Deployed-target limits** (`da3f3c6c`):
  - `ModelingExportImportTests` is `[NotInParallel(nameof(ModelingExportImportTests))]`, because the API refuses a fourth active view transfer per project (`409 TransferLimit`, `ViewTransferService.EnsureCapacityAsync`).
  - `GraphPage.ShowEverythingAsync` skips with its reason above 20,000 annotations, where the admin UI disables Show everything; a deployed target view only grows.
- **Consolidation** (`d01bc53e`, the 23 code findings). The latent defects among them:
  - One `DiscardChangesDialog.DiscardIfShownAsync(outcome)` on `DetailPanel.Gone` replaces a blind 3 s wait.
  - `BulkCreateDialog.CreatedRow` matches the exact key (a substring matched two rows from ten keys up).
  - The HSM refusal reads the Settings card's error alert only.
  
  The rest:
  - `App.ToastItem`, `App.PickOptionAsync`, `ProtectionLevels`, `NuxtLocators` (renamed from `AccessLocators`), `MockApi.ViewPath`, `RecordedRequest.Path` and `WaitForRequestAsync`.
  - Shared `DownloadAsync`, `CodeEditor.ExpectTextAsync`, `AppRoutes.HrefEndingWith` and `ContextSwitcher` menu opening.
  - One `VersionsView.SplitDiffWidth`, `DirtyTitle` and `AnnotationPart`.
  - Dead page-object members removed, and section partials that duplicated shared members deleted.
  
  Nothing a test checks changed, except that `CreatedRow` and the schema-list links are now stricter.
- **CI**: `.github/workflows/ui-admin-automation.yml` serves the mock UI with `vite preview --host 127.0.0.1` and tests `http://127.0.0.1:4174`, as the local commands do.
- **Documentation**:
  - The `mock` profile row (eight users, the role projects).
  - The `RoleProjects` key and its validation, and the target check's `/orgs` load and its two new messages.
  - The mode combinations, and the tests that create lasting data outside `[DisposableProject]` (ORG-ORG-01, the Elementary ORG-PRJ-01).
  - The 20,000 ceiling, and the serialized transfers.
  - The download note, and the CI's six pool users without `Target:RoleProjects`.
  - The README's path table, and the Phase 3 review's file lists and commit counts.

Not applied, as follow-ups:

- The CI workflow's pool slots 6 and 7 and a `Target:RoleProjects` variable. The docs say that CI passes six users and no role projects.
- A category that leaves out ORG-ORG-01 and ORG-PRJ-01. The docs name both tests.

### 6. Documentation

- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md`:
  - **Users and roles**: the `Role` table with what each role leases and how many tests use it, `Target:RoleProjects`, and a pool of eight.
  - **The Elementary set**: ten tests, one per section, with their case ids.
  - **The catalogue** (new): the folders, classes and `[Area]`, the mode attributes with their counts and skips, the full-suite and per-section commands, and the helpers.
  - The mock commands use `127.0.0.1:4174`.
  - **Testiny**: every test is linked to its case in the section folders, and `Elementary` is empty.
  - **Troubleshooting**: two new rows, the pool's e-mails and the refused `localhost` asset.
  - The spec line names the new plan.
  - Phase 2 had already updated "Data rules" and "Write a test".
- `docs/Platform/UI.Admin.AutomationTests/test-cases.md` (new): every case with its reference, Testiny id, title, priority, role, mode and test, grouped by section and sub-folder.
- `docs/Libraries/Testing/UI.Automation/readme.md`: `GetSkipReason` (Phase 2).
- `src/Platform/UI.Admin.AutomationTests/README.md` (the specs, the case list and a path table with `PlatformTest*.cs`, `Attributes/` and `Support/`) and the `UI.Admin.AutomationTests` row of `CLAUDE.md` name the catalogue, the attributes and the role projects.
