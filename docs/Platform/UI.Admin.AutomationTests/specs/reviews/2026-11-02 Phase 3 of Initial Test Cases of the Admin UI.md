# Phase 3 of Initial Test Cases of the Admin UI: the Tests per Section

## Overview

Phase 3 of [2026-11-02 Initial Test Cases of the Admin UI and Their Automation](../2026-11-02%20Initial%20Test%20Cases%20of%20the%20Admin%20UI%20and%20Their%20Automation.md) (Linear [424-152](https://linear.app/424fun/issue/424-152/create-initial-tests-cases-and-automate-them-for-admin-ui)) covers §4: one automated test for each of the 233 cases of the catalogue, in the ten section folders of `src/Platform/UI.Admin.AutomationTests/src/Tests`. The six existing tests stay in their new classes, so 227 tests are new. Each section was developed on its own branch (`feat/424-152-<section>`, in its own worktree) and cherry-picked into `feat/424-152`. The commits are:

- 61 commits of tests, one per sub-folder.
- 2 follow-up fixes (`f285d1f7`, the re-read of an annotation's history; `6504ad9a`, the Graph filter counts).
- `de3e2d4e`: eight placeholder pool users in the mock profile, so that the user secrets of a deployed pool of up to eight passwords still give every mock entry an e-mail.
- 10 commits of review fixes.

The tests and their page objects add 162 files and about 22,700 lines.

## What Was Done

### 4. Tests

#### Structure and attributes

Each class is a Testiny sub-folder and each method is one case, as the plan specifies:

- The class and method names, the folder, `[Area]`, `[TestCase(<id>)]` (the ids of the Phase 1 mapping), `[UserRole]`, the mode attributes and `[Elementary]` all follow the catalogue.
- A comment that starts with the reference sits above each test.

A static cross-check of the code against the catalogue finds all 233 methods once each, and no difference in class, folder, id, role, mode or Elementary. No test uses `Task.Delay` or `WaitForTimeoutAsync`.

| Section | Classes | Tests | Review findings (must / should) |
|---|---|---|---|
| Access | 7 | 33 | 1 / 6 |
| Organizations and Projects | 3 | 10 | 0 / 4 |
| Navigation | 6 | 23 | 1 / 5 |
| Annotations | 8 | 34 | 3 / 5 |
| Configurations | 8 | 28 | 3 / 4 |
| Modeling | 6 | 28 | 3 / 7 |
| Graph | 7 | 18 | 1 / 5 |
| Key-Values | 8 | 31 | 3 / 2 |
| Settings | 6 | 21 | 1 / 4 |
| Monitoring | 2 | 7 | 0 / 7 |

#### Page objects and helpers per section

Each section owns its page objects. It adds members to shared types only in its own partial files (`<Type>.<SECTION>.cs`), so no two sections touched the same file:

- **Access**:
  - Sign-in and account: `LoginPage.ACC`, `AccountPage.ACC`, `UnsupportedPage`, `OnboardingPage`.
  - Members and invitations: `MembersPage`, `OrganizationMembersPage`, `MembersTable`, `InvitationsTable`, `InviteDialog`, `RoleLegend`, `MyInvitationsPage`, `InvitationLandingPage`.
  - Machines: `MachinesPage`, `MachineCreateDialog`, `OneTimeSecretDialog`, `MachineDetail`.
  - Federation: `FederatedSetupDialog`, `FederationRulesDialog`, `FederationRuleGroup`, `TrustedIssuersSection`, `TrustedIssuerDialog`.
  - Certificates: `SharedCertificatesSection`, `IssueCertificateDialog`.
  - `PlatformTest.ACC`: the AuthKit hosted-page sign-in for a deployed target, with the trace chunk dropped around the password, and the main-frame navigation recorder.
- **Organizations and Projects**: partials of `OrganizationsPage`, `OrganizationOverview` and `ViewsPage`.
- **Navigation**: `ShortcutsHelpDialog`, and partials of `App`, `ConfigurationsPage`, `ContextSwitcher` and `ProjectSettingsPage`.
- **Annotations**:
  - New: `AnnotationForm`, `AnnotationBulkBar`, `AnnotationFilterSheet`, `AnnotationHistory`, `DeletedKeysDialog`, `DeletedAnnotationBanner`.
  - Partials of `BulkCreateDialog`, `AnnotationsPage`, `AnnotationDetail`, `NewAnnotationDialog` and `PlatformData`.
- **Configurations**:
  - New: `NewConfigurationDialog`, `ConfigurationConflictDialog`, `SchemaForm`, `CombinedSchemaView`, `ConfigurationEffective`, `ConfigurationResolved`, `BindingReferencePopover`.
  - `ConfigurationSteps`, the setups the classes share.
  - Partials of `ConfigurationDetail`, `ConfigurationsPage`, `VersionsView`, `CodeEditor` and `DiscardChangesDialog`.
- **Modeling**:
  - New: `NewSchemaDialog`, `ModelingDeletedPage`, `ModelingExportImportPage`, `ModelingComparePage`.
  - `ViewArchiveBuilder`, which builds an export zip in memory for the imports.
  - Partials of `ModelingPage`, `ModelingSchemaPage`, `ModelingTemplatePage`, `CodeEditor`, `DiscardChangesDialog`, `ConfigurationConflictDialog`, `AppRoutes` and `PlatformTest`.
- **Graph**: `GraphPage`, `GraphLegend`, `GraphNodeDetail`, `GraphTree` (the generated tree of a test), and partials of `PlatformData` and `PlatformTest`.
- **Key-Values**:
  - New: `KeyValueDetail`, `KeyValueVersions`, `KeyValueExportMenu`, `KeyValueImportDialog`, `KeyValueExportFiles`.
  - Partials of `KeyValuesPage`, `ProjectSettingsPage` and `PlatformData`.
- **Settings**:
  - New: `DateFormatSelect`, `TypeAppearanceEditor`, `AllowedLevelsCard`, `KeyEncryptionKeysCard`, `ByokCard`, `RecipientsCard`.
  - Partials of `App`, `ProjectSettingsPage`, `AccountPage`, `AnnotationDetail`, `KeyValuesPage`, `PlatformData` and `PlatformTest`.
- **Monitoring**: `MonitorPage`, `UsageReport`, `AuditTrail`, `UtcDates` and `Eventually` (a bounded retry of an eventually consistent read on a deployed target).

In-place edits of shared code, all small:

- `KeyValuesPage.CurrentVersion` finds the versions table by its "Created" column, because the list's Deleted state also has "Expires".
- `SignInTests`, `AnnotationBulkCreateTests`, `KeyValueCreateTests` and `ProjectTests` became `partial` for their `GeneratedRegex` members. The sections added their cases to all six moved classes, and some of the Elementary tests now assert more of their case's steps.

#### Concurrent edits and deployed-target paths

- The cases with another writer (ANN-CAT-04, CFG-CON-01 to 03, KV-DET-04, MOD-TPL-02, SET-SEC-05) write as "someone else" through `MockApi` on the mock. On a deployed target they write through `OpenSecondPageAsync`, a second page that saves through the UI.
- The sign-in cases complete the AuthKit hosted page with the leased user on a deployed target.
- The audit cases poll with `Eventually`.
- The live paths of the graph handle a view above 5,000 annotations, where the graph starts focused.

None of these deployed-target paths could run here (§5 of the Phase 4 review).

#### Independent review and fixes

An independent reviewer per section checked every test against its case (and against the code, the dossiers and the mock), then a fixer applied the findings, re-ran the section on both profiles and committed. The 65 findings (16 must, 49 should) led to 74 changes, and 1 finding was rejected with its reason. The must-fix findings:

- **Assertions that could not fail**:
  - NAV-PAL-01: the filter's value after Escape was read from an empty, fresh field.
  - CFG-NAV-01: the phone branch's checks of a row already filtered by its key.
  - KV-SEC-01: "Copy to clipboard" beside the value matched another button.
  - KV-IEX-01: the import steps are always rendered; their state is now checked.
- **Races**:
  - ANN-FRM-05: a save could pass on the toast of the previous save.
  - MOD-EXI-02: the Git export could pass on the first export's toast.
  - KV-SEC-02: a 1 s margin before the 60-second mask; the clock is now paused before Reveal.
- **Wrong matches**:
  - ANN-DEL-02: the Deleted badge could match the History's "Deleted" action.
  - GRA-CRE-03: a strict-mode violation in an empty Reader view.
- **Deployed targets**:
  - ACC-MYI-01 failed from the second run on, because the API keeps every invitation. It now counts the own e-mail's Declined and Accepted invitations first.
- **The Elementary test ANN-BLK-01** now asserts every expected result of its case.
- **Known bugs encoded as expected behaviour** (below): CFG-CRE-04, CFG-INH-01, MOD-SCH-03, MOD-TPL-01, SET-SEC-02.

#### Bugs of the admin UI found by the tests

The tests found four probable bugs of the admin UI (`src/Platform/ui.admin`, not changed by this issue). Each test accepts both today's and the fixed behaviour, with a comment "Admin UI bug (424-152 review)", and checks the outcome strictly. Their Testiny cases keep the intended result with a note:

1. **Bulk delete counts too few**: `AnnotationBulkBar.vue` `deleteSelected()` computes the count from `props.keys` after `removeMany()` has removed the keys, so the toast reads "1 annotations deleted" for two (ANN-CAT-02).
2. **"Discard changes?" after Delete own**: `ConfigurationEditor.vue` `remove()` leaves the draft `{}` while the original is undefined, so leaving the detail or opening another tab asks to discard (CFG-CRE-04, CFG-INH-01).
3. **A deleted schema or template stays `{}`**: after Delete, `SchemaDetail.vue` and `TemplateDetail.vue` (with `CodeEditor.vue`) keep `{}` instead of showing the starter document or the empty state, and leaving asks "Discard changes?" (MOD-SCH-03, MOD-SCH-07, MOD-TPL-01).
4. **The in-use link of a protection level lists the deleted keys**: `key-values.vue:32` reads `state=All` as the Deleted keys, so the link from the secrets policy's usage list shows "No deleted keys" (SET-SEC-02).

#### Deviations from the catalogue

The implementers and fixers recorded where the real UI differs from a case's steps; 28 Testiny cases were edited to match the tests:

- **Access**: MYI-01, SGN-04, INV-02, MCH-06, FED-04.
- **Annotations**: CAT-02.
- **Configurations**: CRE-02, CRE-04, EDT-01, INH-01.
- **Modeling**: SCH-02, SCH-03, SCH-07, TPL-01, TPL-02, CMP-01.
- **Graph**: CRE-03, FOC-01, MEM-01.
- **Key-Values**: ROL-02, IEX-05, IEX-06, SEC-01, SEC-02.
- **Settings**: SEC-02.
- **Navigation**: PAL-01.
- **Monitoring**: PAG-01, AUD-01.

Examples:

- MOD-CMP-01: Create empty stores v1, so the first save is v2. The key picker is a button with a search box, not a combobox.
- MOD-SCH-02: picking the key again selects the first type that can descend.
- KV-ROL-02: on a phone the full-screen detail covers Export, so the test closes it first.
- ACC-SGN-04: the "Sign-in failed" message is a Nuxt UI alert box without the ARIA role `alert`.
- MOD-SCH-03: the mock gives every view built-in schemas, so "None." is asserted on a deployed target only.

The plan itself is not changed; this review and Testiny hold the differences.

#### Verification of the sections

Every section's chain ran its classes on `mock` and `mock,mobile` and once more on desktop after each sub-folder, and again after the review fixes, until every test passed or was skipped only by its mode. The suite as a whole is verified in Phase 4.
