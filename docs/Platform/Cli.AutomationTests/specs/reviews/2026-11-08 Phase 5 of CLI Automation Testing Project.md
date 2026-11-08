# Phase 5 of CLI Automation Testing Project

Linear: [424-215](https://linear.app/424fun/issue/424-215/create-cli-automation-testing-project-sform-against-a-real-backend).

## Overview

Phase 5 is the last row of section 7 of [CLI Automation Testing Project](../2026-11-08%20CLI%20Automation%20Testing%20Project.md). It covers two parts of the spec. The first is the automation of every case of section 4.4: each row of the catalogue [test-cases.md](../../test-cases.md) (Testiny project `2SC`) now has its test in `src/Platform/Cli.AutomationTests/src/Tests/`, until the coverage test of section 4.2 passes with only the listed exceptions. The second is the CI workflow of section 5, `.github/workflows/cli-automation.yml`. Seven agents wrote the 217 cases phase 3 had left, one group of sections each, and the groups were merged into `feat/424-215`. The defects these tests found in `sform` and in the API were then fixed ([sform Defects Found by the CLI Test Cases](../../../Cli/specs/reviews/2026-11-08%20sform%20Defects%20Found%20by%20the%20CLI%20Test%20Cases.md), §14 to §18). The helpers that several groups had each written were consolidated into the shared base classes, and the feature users got a rule of their own. The docs are [automation-tests.md](../../automation-tests.md), the project's `README.md` and [the library's readme](../../../../Libraries/Testing/UI.Automation/readme.md). This review covers the whole phase. It follows the [Phase 4 review](2026-11-08%20Phase%204%20of%20CLI%20Automation%20Testing%20Project.md).

| Run (Windows 11, no target) | Result |
|---|---|
| `dotnet build src/Platform/Cli.AutomationTests/src` | 0 errors, 0 warnings (builds `src/Platform/Cli/src` and the fake editor through the project references) |
| `dotnet test --project src/Platform/Cli.AutomationTests/src` | 312 tests, 0 failed: 83 passed (the self tests and the 9 coverage checks), 229 skipped (the case tests, without `Target:BaseUrl`) |
| `… --treenode-filter "/**[Category=Coverage]"` | 9 of 9 passed, among them the two gap tests of phase 4 (`EveryCommandOptionArgumentAndValue_IsCovered`, `EveryCase_IsAutomated`) |
| `dotnet test --project src/Libraries/Testing/UI.Automation.Runtime/test` | 260 of 260 passed (`UserRequirement.Except`, §5) |
| `dotnet test --project src/Platform/Cli/test` | 1324 of 1324 passed (28 new, the defect fixes of §4) |
| `STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*CosmosConfigurationServiceTests"` | 36 of 36 passed (1 new), against a Testcontainers emulator |

**Consequence for the branch.** The two gap tests pass, so this project no longer turns `dotnet test --solution 42.mono.slnx` red. That was the condition phase 4 set for merging the branch.

**Not run against a target.** No case test ran against a Storyteller API: there was no deployed target, no local Aspire stack, and the CLI pool does not exist in AuthKit yet. What that leaves open is listed in [§7](#7-what-could-not-be-verified).

## What Was Done

### 1. How the work was split

The 217 cases that phase 3 had not automated (the 12 Elementary cases already existed) were split into seven groups along the catalogue's sections. Each group was written by its own agent, in its own git worktree and branch (for example `feat/424-215-p5-annotations`), against the same base: the phase 4 tree with its coverage and catalogue checks.

- **Where a group could write:** only in its `Tests/<Folder>/` folders and in its rows of the catalogue.
- **Where it could not:** the shared code (`CliTest`, `Running/`, `Access/`, the settings, the docs) was read-only. A group that needed something there wrote it in its own folder and reported it as an infrastructure need (§5).
- **What each group checked before it finished:**
  - the build, with no warning in its files;
  - `SelfTest`, 68 of 68 at the time;
  - the `Coverage` checks: `EveryAutomatedCase_MatchesItsRow`, `EveryTest_HasACase`, `Covers_NameOnlyWhatSformHas`, `CoverageValues_MatchTheCli` and `CoverageExceptions_ListTheFeatureGatedItems` had to pass, and the two gap tests had to list nothing of its sections;
  - its `Area` tests found and skipped without a target.

The groups were merged one after another into `feat/424-215`, one commit per section or class group:

| Group | Sections (catalogue cases) | Test classes | New cases | Commits |
|---|---|---|---|---|
| signin-account-general | Sign-in (14), Account and Access Points (15), Context and Errors (10) | `DeviceCodeSignInTests`, `MachineSignInTests`, `LogoutTests`; `AccountSetTests`, `AccessPointTests`, `AccountToolsTests`; `ContextTests`, `CommandLineTests` | 36 (+3 Elementary) | `25003e6a`, `cde9027c`, `44566227` |
| machines-certs | Machines (30), Shared Certificates (6) | `MachineLifecycleTests`, `MachineCredentialTests`, `MachineAuthPolicyTests`, `MachineFederationTests`, `MachineContextTests`; `SharedCertificateTests` | 35 (+1) | `9c2e59e3`, `6ce9a109` |
| annotations | Annotations (38) | `AnnotationListTests`, `AnnotationEditTests`, `AnnotationDeleteTests`, `AnnotationTransferTests`, `AnnotationContextTests` | 36 (+2) | `8b2d2a08` |
| config-templates | Configurations (32), Templates (9) | `ConfigGetTests`, `ConfigWriteTests`, `ConfigHistoryTests`, `ConfigGitHubTests`, `ConfigContextTests`; `TemplateTests` | 37 (+4) | `94d3adcb`, `3e52d557` |
| schemas | Schemas (21) | `SchemaTypeTests`, `SchemaAnnotationTests`, `SchemaDescendantTests`, `SchemaDefinitionTests`, `SchemaContextTests` | 21 | `d2309eed` |
| kv-a | Key-Values: pairs, secrets, bulk, context (27) | `KeyValuePairTests`, `KeyValueSecretTests`, `KeyValueBulkTests`, `KeyValueContextTests` | 25 (+2) | `748f897f`, `1bc93fbb` (catalogue) |
| kv-b | Key-Values: export and import, policy, client encryption, audit (27) | `KeyValueTransferTests`, `KeyValuePolicyTests`, `KeyValueRecipientTests`, `KeyValueAuditTests` | 27 | `7a0a0b8e`, `58a363a0`, `b2124c98`, `c146759b` |
| **Total** | 10 sections, 229 cases | 38 classes | **217** (+12 = 229) | |

Each section also has helpers next to its classes. These were not tests, and the catalogue check takes no test from them:

- `SignInOutput`, `AccessPointOutput`, `GeneralChecks`;
- the abstract `MachineAccessTest` with `IssuedCertificate` and `PlatformTokenSigner`;
- `AnnotationOutput` and `ViewArchive`;
- `ConfigOutput`, also used by `TemplateTests`;
- `SchemaChecks`;
- `KeyValuesSupportA` and `KeyValuesSupportB`.

**Seen only while merging.** Until the kv-a group was merged, `CoverageExceptions_ListTheFeatureGatedItems` failed in the kv-b branch for `storyteller kv protect`. There, only the feature-gated KV-POL-07 and KV-POL-08 covered it. KV-BLK-08 (kv-a) covers it in every run, so no exception was added, and the failure went away at the merge.

The CI workflow (`273ca20b`) and the `CLAUDE.md` rows of the project (`00e3cf98`) were committed before the case groups.

### 2. The automated cases

- **229 of 229 cases are automated**, one `[Test]` each, with no `[Skip]` and no manual case.
- **How the cases split by mode:**
  - 81 `[DisposableProject]` (P);
  - 15 `[FreshSignIn]` (F);
  - 10 `[Terminal]` (T);
  - 6 `[MachineOnly]` among the 13 M cases;
  - 5 `[RequiresFeature]` (X): KV-POL-05 and KV-POL-07 (`Byok`), KV-POL-08 (`Hsm`), SGN-DEV-06 (`MultiOrganization`) and SGN-OUT-04 (`Registration`).
- **How a case test is shaped:** `[TestCase(<id>)]` with the catalogue's Testiny id, the `// <Reference>:` comment, and 368 `[Covers]` attributes in total.
- **Coverage.** `CommandCoverageTests` reads sform's command tree: 99 commands, 35 aliases, 349 options (600 forms), 81 arguments and 130 fixed values, so 945 items. **Every command, alias, option form, argument and fixed value is covered.** `EveryCommandOptionArgumentAndValue_IsCovered` passes. Phase 4 had left 907 of 946 items uncovered. The one item fewer is D7 (`525ff637`), which made `kv import --protection public` a value the CLI refuses.
- **The catalogue.** `CatalogueTests` passes:
  - `Catalogue_IsWellFormed`;
  - `EveryCase_IsAutomated`;
  - `EveryAutomatedCase_MatchesItsRow`: place, id, role, modes, Elementary and Covers;
  - `EveryTest_HasACase`.
- **`coverage-exceptions.json`** is unchanged since phase 4 (its last change was the D7 fix, `525ff637`). It lists 13 items, each covered only by a gated test:
  - `Byok` (7): `kv policy set --byok-key` and `--byok-tenant` (KV-POL-05); `kv policy set --allow=byok` and `--default=byok`; `--protection=byok` of `kv set`, `kv protect` and `kv import` (KV-POL-07).
  - `Hsm` (6): `kv policy set --allow=hsm` and `--default=hsm`; `--protection=hsm` of `kv set`, `kv protect` and `kv import`; `kv policy rotate --protection=hsm` (all KV-POL-08).
  - SGN-DEV-06 and SGN-OUT-04 need no entry: `-o` and `register` are also covered by SGN-DEV-07 and SGN-OUT-03, which run everywhere.

  `CoverageExceptions_ListTheFeatureGatedItems` passes: no gated-only item is unlisted, and no entry is stale.
- **`coverage-values.json`** is unchanged: 30 options.
- **What the tests check.** They check what the code prints, read from the sources of sform and the API. The signin-account-general group also ran what works offline against the built sform: the help of every command, parse errors, the redirected-input hints, the placeholders, the machine logout and project mismatch, a closed port and the `SFORM_TOKEN_STORE` notice. The annotations group checked `ViewArchive.CommitSubjects` (the commit subjects of a `--format git` export) against a real git pack.
- **Data decisions worth knowing:**
  - **Generated views.** The Reader list cases and the delete cases that read `story` or `story deleted` put their data in a generated view of `Target:Project`. The default view holds more than a page (1000) of every run's annotations. The configuration cases that need a template or a type schema without being P cases also use a generated view (CFG-GET-03/04, CFG-SET-07/10, CFG-HIS-04/06, TPL-TPL-01).
  - **Bulk cases** load the target and are slow:
    - ANN-LST-07 imports 2001 responsibilities in one `story import` run with a 25-minute timeout;
    - ANN-DEL-08 imports a parent with 1001 units the same way;
    - KV-BLK-03 imports 1000 pairs and adds one key.
  - **GEN-CTX-01** (role Any) needs two projects, and only the target project is sure to be readable by every pool user. Its `<A>` is therefore a generated project of the organization that does not exist (`account set` stores any well-formed key, ACC-SET-04), and `<B>` is the target project. The last step, `story -v <v2>`, shows that `-v` alone kept the default project through the API's refusal "No Reader access to '<A>'.", not through a listing of A's view. A setting for a second project every pool user reads (proposed as `Target:SecondProject`) was not added.
  - **ANN-TRN-05.** sform has no command that reads an export job by its id. The test checks exit 40, the "still queued|running; it continues on the server" message and that no zip was written, then only that a later `story export` of the view succeeds.
  - **Per-instance caches.** CRT-SHC-04 reads nothing before it revokes, so no API instance has the certificate cached. The lists after a change, and the key/value writes and reveals after a policy change, are re-read within the cache window (`EventuallyAsync`, §5).
- **The Elementary CFG-SET-01 of phase 3 was corrected.** Its assertion on the `Content` of the first write would have failed against any real target (§4, item 5). No test reads `Content` from a first write's printed model now. They check its `Version` and read the content back.

### 3. Catalogue and Testiny changes made while automating

Automating the cases found rows whose steps could not produce what they expected. The catalogue stays the source of truth: each change was made in `test-cases.md` first, then in Testiny `2SC`.

**Updated in the catalogue and in Testiny (steps, preconditions or expected results; titles unchanged):**

| Case | Testiny | Change |
|---|---|---|
| GEN-CLI-03 | 393 | `assist` on redirected input names the subcommand ("Name it: sform assist key."). Only `points create` and `assist key` say to run in a terminal (step 6). |
| CFG-SET-07 | 403 | The `--force` step writes `-x a=other`. The API stores no version for a content equal to the stored one, so repeating `-x a=text` could not give a new version. |
| CFG-SET-11 | 407 | After each delete ("The configuration for '<key>' has been deleted."), `config <rst>` ends with 0 and returns the configuration without the deleted values, not "not found". The annotation stays. |
| SCH-TYP-02 | 264 | The `--force` step uses another schema the configurations break (`requires-req2.json`) and expects a new version. The API returns the stored schema for an equal document. |
| SCH-ANN-02 | 280 | The same as SCH-TYP-02 for `schema annotation create`. |
| KV-SEC-06 | 385 | The machines are created with `-r --scope SecretsRead`. A `--scope SecretsRead`-only machine has no Configuration scope and gets 44 (§4, not fixed). |
| KV-BLK-01 | 386 | Keys `<p>.app.a`, `<p>.app.b`, `<p>.other.c` and prefix `<p>.app.`. A `/` is refused by `KeyValueRules.KeyPattern` with exit 2. |
| KV-BLK-03 | 388 | New precondition: more active keys than a page (1000) and two soft-deleted keys. The token comes from "More keys follow; next page with -k <token>". |
| KV-BLK-06 | 409 | Keys and `--prefix` under `<p>.app.` (no `/`). |
| KV-BLK-07 | 410 | `kv purge --prefix <p>.tmp. --yes` (no `/`). |
| KV-BLK-08 | 411 | Secrets under `<p>.sec.`, set "at the level standard" rather than relying on the target's default. |
| KV-POL-07 | 448 | Step 1 is `kv policy set --allow public --allow standard --allow byok --default byok`. After the fix of §4 item 4, `--allow byok` alone ends with exit 2 and `ProtectionInUse` (standard is still in use), no longer "Public pairs can not be disabled.". Updated twice in Testiny. |
| KV-POL-08 | 449 | The same with `hsm`. |

**Changed in the catalogue only. The Testiny edit was denied by the permission classifier ("Modify Shared Resources"), so these Testiny cases must be updated by hand:**

- **392 (GEN-CLI-02).**
  - Step 1's expected result: replace `prints "The most similar command/argument is" with storyteller` with `names the unknown command ("Unrecognized command or argument 'storytelr'") and prints "The most similar commands/arguments are" with storyteller among the suggestions`. McMaster offers two suggestions, `storyteller` and `scheduler`.
  - Step 2's expected result becomes `Exit -1 (input parsing); prints "Unrecognized option '--nope'".`.
- **320 (ANN-DEL-08).**
  - The precondition becomes "a deleted parent with more deleted children than a page (1000), deleted after another root".
  - The expected results of steps 0, 2 and 3: the default list shows the roots, newest first, without the children and **without** a continuation token (step 0 wrongly says "with a continuation token"); `-a`/`--all` add the children as a page with a token.
  - Steps 4 and 5 become `story deleted --all -k <token>` and `story deleted --all --continuation-token <token>`. A token belongs to the query that printed it, and the old `story deleted -k <token>` sent the token of the `--all` list to the roots-only query.
- **401 (CFG-SET-05), left from the defects round of phase 4.** Its title still reads "`-c` and `-l` are accepted and ignored: the write stores only the content". It should be the catalogue's title, "`config set` has no `-c` or `-l`: they end with exit -1 and store nothing" ([defects review](../../../Cli/specs/reviews/2026-11-08%20sform%20Defects%20Found%20by%20the%20CLI%20Test%20Cases.md), §11).

### 4. sform and API defects found by phase 5

The details, files and tests are in [sform Defects Found by the CLI Test Cases](../../../Cli/specs/reviews/2026-11-08%20sform%20Defects%20Found%20by%20the%20CLI%20Test%20Cases.md), §14 to §18 and "Not fixed, out of scope". After the fixes, `Cli.UnitTests` has 1324 tests (28 new) and `CosmosConfigurationServiceTests` 36 (1 new), all green.

**Fixed:**

1. **Every `story kv` command crashed (a blocker), §14.** Found by the signin-account-general and kv-b groups. `KeyValueListCommand`, the parent of every kv command, took an optional `TimeProvider` that McMaster's constructor injection resolves anyway, and the container had none. Every kv command line, every `--help` included, ended with exit 1 and "Total mayhem". This blocked the whole Key-Values section (the Elementary KV-PAR-01 and KV-SEC-01 among it), GEN-CLI-01/03/05, and the setup of CFG-GHA-01 and CFG-GET-02.
   - **The fix:** `Startup` registers `TimeProvider.System`, and the parameter is required.
   - **New tests:** `CommandConstructionTests` builds every command of the tree through sform's real host. `ProgramProcessTests` gained five out-of-process cases.
2. **`schema type|annotation|descendant diff --format unified` crashed, §15.** Found by the schemas group. The API answers `unified` as `text/plain`, and the NSwag client reads a 200 only as JSON, so the command ended with exit 1 and "The request failed with 200 OK.".
   - **The fix:** for `unified`, sform asks for the JSON diff and writes the unified text itself (`DiffResultConsoleExtensions.WriteUnifiedDiff`), raw on standard output in UTF-8 with LF line ends. A diff without changes writes nothing there and "No changes detected." to standard error.
   - **New tests:** `SchemaDiffCommandTests` (9). SCH-TYP-03, SCH-ANN-03 and SCH-DSC-03 expect exactly this.
3. **`kv export` and `kv get --value-only` wrote indented, wrapped lines, §16.** Found by the kv-b group. The output got a two-space margin and was wrapped at the width, so `> .env` was broken and a long value was split.
   - **The fix:** both write the payload raw in UTF-8. The export uses LF line ends and a final one, and only the CRLF of the json indentation is converted. The note on secrets left out goes to standard error.
4. **`kv policy set --allow` without `public`, §17.** Found by the kv-b group. The help promised "Public is always allowed", but sform sent only the given levels, which the API refused.
   - **The fix:** sform always sends Public, first and once. The help says that the given levels replace the stored ones.
   - **Catalogue and Testiny:** KV-POL-07 and Testiny 448/449 follow (§3).
5. **The API's first write of a configuration answered without `Content`, §18 (`Backend.CosmosDb`).** Found by the config-templates group.
   - **The fix:** the create branch of `CosmosConfigurationService.WriteConfigurationAsync` returns the stored content, as every later write does.
   - **New test:** `FirstWrite_ReturnsTheStoredContent`.

**Docs:**

- `docs/42for.net/cli/sform.md`, new section "Output for scripts". Only the payload of a successful run is alone on standard output. Refusals and failures still write their message there and end with a non-zero exit code, and `config --content-only` is the exception.
- `docs/Platform/Storyteller/definitions.md`: the diff `--format` values.
- `docs/Platform/Storyteller/key-values.md`: the `--value-only` and export output, and that `--allow` always sends public.

**Left as they are.** The defects review lists each item with what the tests expect now:

- `story history` with one hidden entry prints "1 older entries are not shown." (ANN-DEL-09 expects it).
- `story -t -b rst` hides a subject's single context (ANN-LST-06 gives the subject two).
- The input errors of `machine create` and `machine-auth` end with `-1` or `2` inconsistently (the tests expect the catalogue's codes).
- `machine create --scope SecretsRead` alone gives a machine that cannot read a pair (KV-SEC-06 adds `-r`).
- `kv get <secret> --value-only` without `--reveal` exits 44 rather than 2.
- The `→` of `config patch` goes through the OEM code page on Windows (CFG-SET-08 never matches the arrow).
- The API's per-instance caches, evicted only on the instance that made the change:
  - shared certificates and client certificates, 5 minutes;
  - federated issuers, the machine authentication policy and the key/value policy, 60 seconds.

  On a scaled-out API, a revoked shared certificate with a valid API key keeps authenticating on another instance for up to 5 minutes. That one deserves its own fix.
- A limit, not a defect: sform cannot read an export or import job by its id (ANN-TRN-05).

The earlier items of that list, such as `sform --version` printing the toolkit's version, stay as they were.

**Messages left on standard output.** Error messages stay on standard output, as in every command but `config --content-only`. KV-TRN-04 (`KeyValueTransferTests.cs:200`–`:202`) reads the refusal of `kv export --include-secrets` from standard output, and moving only the `--value-only` messages would leave the export inconsistent.

### 5. The shared helpers consolidated afterwards, and the feature users

After the merge, the infrastructure needs the groups had reported were moved into the shared code. No section keeps a private copy of a helper that a second section needs. The files touched are everything under `src/Platform/Cli.AutomationTests/**` (with `Tests/**`) and `UserRequirement` of the runtime library, with its tests and docs. Nothing in `src/Platform/Cli` or `test-cases.md` was touched.

**Moved into the shared code:**

- **`SformResult`** (`Running/SformResult.cs`):
  - `ShouldNotMatch(Regex, stream)` replaces the extension in `GeneralChecks`.
  - `VersionRows(headerStart)` reads the table that every `versions` command writes through `WriteVersions` (Version, Author, Created, Expires, newest first). It replaces `SchemaChecks.Versions` and the parsers of the other sections.
- **`CliTest`:**
  - **`EventuallyAsync`.** Two overloads: one for a run as the leased user, one for any read. It re-reads every 5 s until the check passes or the window ends, a read that passes returns at once, and each run is attached. It replaces three retry loops: `MachineAccessTest.EventuallyAsync`, `KeyValuesSupportA.EventuallyAsync` and the wait for the binding reads in `KeyValueAuditTests`. The windows stay with the sections: `IssuerCacheWindow` 75 s and `SharedCertificateCacheWindow` 315 s in `MachineAccessTest`, `PolicyCacheWait` 90 s in `KeyValuesSupportA`.
  - **`ArrangeAsAsync(UserRequirement)`.** `ArrangeAsEditorAsync()` is now `ArrangeAsAsync(EditingUser)`. It replaces the private `ArrangeAsSecretsUserAsync` copies of `KeyValueSecretTests` and `KeyValueBulkTests`.
  - **`AsRoleUserAsync(Role)` and `RoleProjectOf(Role)`.** The "second user" of the catalogue in `<contributor project>` or `<reader project>`: another pool user in the project `Target:RoleProjects` maps the role to, else a pool user with the role in `Target:Project`. They replace the private `AsContributorAsync` of `KeyValueBulkTests`.
  - **The machine helpers** in `CliTest.Machines.cs`: `RunCreatingMachineAsync` (any `machine create`/`set` command line, with the alias and the context options as the test gives them, deleted at cleanup if it creates a machine), `DeleteMachineAtCleanup`, `TryMachineIdOf` and `MachineIdOf`. They were moved from `MachineAccessTest`.
  - **`FeatureUser` and `WithoutFeatureUsers`** (below), moved from `SignInOutput`.
- **`KeyValueSettings.cs`** (top level). The BYOK customer key of KV-POL-05 and KV-POL-07 (`KeyValues:ByokKeyUri`, `KeyValues:ByokTenantId`), moved from `KeyValuesSupportB`.
  - `testsettings.json` has a `KeyValues` section with nulls.
  - `CliAutomationSetup.Validate` requires both keys when `Target:Features` lists `Byok`, and checks their shape whenever they are set: a versionless `https://<vault>.vault.azure.net/keys/<name>` and a GUID.
- **`SformHome.ResetDefaultProject`.** `CreateDisposableProjectAsync` removes only `access.default.json` that `points create` set, so the editor setting survives. A test of modes E and P may now call `UseEditorAsync` before or after creating its project; before, it had to call it after.

**The feature users.** `Registration` (an unregistered user for SGN-OUT-04) and `MultiOrganization` (a user in two WorkOS organizations for SGN-DEV-06) are users rather than capabilities of a deployment (`CliFeatures.OfUsers`). An unregistered user breaks every test that leases it by chance, and a multi-organization user signs in without `-o` only with `Target:AuthKitOrganizationId`.

- **The marker.** The target marks such a user with the feature's name among its `Roles` (`[ "Reader", "Registration" ]`) and lists the feature in `Target:Features`.
- **In the runtime library**, `UserRequirement` has `ExcludedRoles` and `Except(params string[])`. The class stays immutable, and roles compare without case. `IsSatisfiedBy` also rejects a user with an excluded role, and `ToString` adds "and none of …". `UserRequirementTests` is new, and `UserPoolTests` is extended.
- **Leasing.** A `[RequiresFeature]` test of a user feature leases that feature's user (`FeatureUser`). Every other lease goes through `WithoutFeatureUsers`, which adds `Except(<the features>)` unless the requirement asks for the feature itself: `RequiredUser`, `AsAnotherUserAsync`, `ArrangeAsAsync`, `AsRoleUserAsync` and `AsGrantedUserAsync`.
- **The target check** leases with `UserRequirement.Any`, so it could get a feature user. When it does, `CliTargetCheck.ReplacementOf` gives a requirement without the features, and `UserOfCheckAsync` leases another user through `context.Scope.LeaseAnotherUserAsync`. That user signs in and reads for the check, and goes back to the pool when the check's scope ends. When every pool user stands for a feature, the check fails with a `TargetCheckException` that says to add the role users. New self test: `SettingsSelfTests.TargetCheck_ReplacesAFeatureUser`.
- **Pool layering.** The highest settings layer that sets any `Users:Pool:<n>:Email` defines the pool alone, and only the passwords merge across layers (`UiAutomationSettingsLoader.ResolvePool`). A layer that adds a feature user must therefore restate the role users, with the feature users last so the passwords keep their positions. In CI the workflow maps 8 slots and the committed pool has 8 role users, so a feature user takes one of those slots. The `testsettings.json` comment, "Feature users" and Troubleshooting in `automation-tests.md` say so.

**Docs:**

- `automation-tests.md`:
  - phase 5 is marked done: 229 cases, all automated, 945 items covered;
  - "JSON" is rewritten: `WriteJson` now renders 1,000,000 columns wide, and `WrappedJson` stays as a safety net for an older sform;
  - new sections "`KeyValues`", "Feature users" and "Section helpers";
  - the CI section describes the workflow as it is;
  - new Troubleshooting rows: the caches, the BYOK settings, the feature-user leases, the target check's replacement;
  - the `logs/` that GEN-CLI-06 writes into the CLI's build folder.
- The project's `README.md`.
- The library's readme: `Except`.

### 6. CI: `.github/workflows/cli-automation.yml`

As section 5 of the spec planned. Its structure follows the admin UI's `ui-admin-automation.yml`: the queued concurrency group, the `CI_*` copies mapped to `UITESTS_*` settings, and the private traces. It has not run yet (no `cli-automation` environment, no target; §7).

- **Triggers.** `workflow_dispatch` and `workflow_call`. There is no push or pull request trigger yet: gating pull requests on the Elementary set is a follow-up.
- **Inputs** (the same for both triggers):

  | Input | Default | Meaning |
  |---|---|---|
  | `profile` | `""` | Settings profiles after `ci`, which is always applied |
  | `filter` | `/**[Category=Elementary]` | TUnit's `--treenode-filter`; empty runs every test |
  | `target_url` | `""` | The API, `/api` included; empty takes the environment's `UITESTS_TARGET_BASE_URL` |
  | `report_testiny` | `false` | Adds the `reporting-testiny` profile and the Testiny key; the run's URL goes into the job summary |

  A caller of `workflow_call` may pass the secrets under the same names; the environment's win.
- **Job settings.**
  - `ubuntu-latest`, `timeout-minutes: 90`, `permissions: contents: read`.
  - Concurrency group `cli-automation-target`, never cancelled and queued (`queue: max`). The runs share the CLI pool, so only one runs at a time.
- **Steps.**
  1. Check the inputs: without `target_url` or `UITESTS_TARGET_BASE_URL` the job fails at once.
  2. Check out the whole history, which the versioning needs, without persisted credentials.
  3. Set up `actions/setup-dotnet` with the SDK of `global.json`.
  4. Build Release: the tests, sform and the fake editor.
  5. Install **Playwright's Chromium only** (`pwsh …/playwright.ps1 install --with-deps chromium`), for the AuthKit device page. Not the Playwright container. The terminal cases use util-linux `script` of the image.
  6. Run: `dotnet run -c Release --no-build -- --treenode-filter <filter> --report-trx --results-directory TestResults/cli-automation`, with its log in `run.log`.
- **The environment `cli-automation`.** The workflow's comment says to restrict it to `main`. Its variables and secrets reach only the `Run` step; an empty or unset variable keeps the committed value.
  - Variables:
    - `UITESTS_TARGET_BASE_URL`, `UITESTS_TARGET_ORGANIZATION`, `UITESTS_TARGET_PROJECT`, `UITESTS_TARGET_VIEW`, `UITESTS_TARGET_AUTHKIT_ORGANIZATION_ID`;
    - `UITESTS_TARGET_FEATURES`, comma-separated;
    - `UITESTS_USER_<n>_EMAIL` and `UITESTS_USER_<n>_ROLES` for n = 0…7 (any e-mail replaces the committed pool);
    - `UITESTS_MACHINE_APIKEY_PROJECT`, `UITESTS_MACHINE_APIKEY_VIEW`;
    - `UITESTS_TESTINY_PROJECT_ID`, `UITESTS_TESTINY_TEST_PLAN_ID`.
  - Secrets:
    - `UITESTS_USER_<n>_PASSWORD` (0…7);
    - `UITESTS_MACHINE_APIKEY_API_KEY`;
    - `UITESTS_TESTINY_API_KEY`, passed only with `report_testiny`.
  - The step maps them to `UITESTS_*` settings and unsets its `CI_*` copies of the secrets before the tests start. The build ran in an earlier step without secrets, hence `--no-build`.
- **Artifacts.** On a failure or a cancellation, the artifact `cli-automation-<profile or base>-<attempt>` (kept 7 days) holds `TestResults/cli-automation`:
  - the TRX report and `run.log`;
  - `artifacts/<runId>/` with `results.jsonl` and each test's redacted `sform-<n>.txt`, plus the device page's screenshot and text of a failed sign-in.

  Any `.zip` is deleted first: a trace, which every committed profile turns off, would hold the typed password. The homes with the pool sessions are in the runner's temporary folder and are never uploaded.
- **The BYOK key:** the variables `UITESTS_KEYVALUES_BYOK_KEY_URI` and `UITESTS_KEYVALUES_BYOK_TENANT_ID` of the environment become `KeyValues:ByokKeyUri` and `ByokTenantId` (added after the review of this phase), so the environment may list `Byok` once it has a customer key.
- **Not mapped:** only 8 user slots, so a feature user in CI replaces a role user.

### 7. What could not be verified

- **Nothing ran against a real target.** All 229 case tests skip without `Target:BaseUrl`. What ran is the build, the self tests, and the coverage and catalogue checks. The first real run (`UITESTS_PROFILE=dev` against Aspire, or the workflow against a deployment) needs:
  - **The CLI pool** in AuthKit: `cli-test-<role>-<n>@42for.software`, 2 each of Owner, Administrator, Contributor and Reader. They need their roles in `cli-tests/automation`, password sign-in without MFA, verified e-mails, and the device code grant enabled for sform's client. Their passwords go in the user secrets or the environment.
  - **The `apikey` machine** (`Machines:apikey`) created on the target. Without its key, the machine sign-in cases skip.
  - **The projects** of `Target:RoleProjects` for the exact Contributor and Reader cases.
  - **For the X cases:** a customer key (`KeyValues:*`) for `Byok`, HSM-backed keys for `Hsm`, and the feature users for `Registration` and `MultiOrganization`.
- **The AuthKit device page.** Its labels and steps (the e-mail and password, a code field or not, the name of the approve and decline buttons in `AuthKitDevicePage`) and the cached session of a second approval are still unverified. The list in the [Phase 3 review](2026-11-08%20Phase%203%20of%20CLI%20Automation%20Testing%20Project.md#not-verified-without-a-target) still applies, as do the JSON members and lines read from a real API, the roles against a Release API, and the Linux runner (`script`, 255 read as -1, the `/proc` scan of `ProcessJob`), which the first CI run checks.
- **Eventual consistency.** The cache windows (75 s, 315 s, 90 s) come from the API's code (60 s, 5 min, 60 s plus a margin). They have not been measured on a scaled-out deployment. A test that still sees a stale read after its window fails with the last read attached.
- **Durations and load.** ANN-LST-07, ANN-DEL-08 and KV-BLK-03 import thousands of items. How long they take on a real target, and whether a full run (empty `filter`) fits the workflow's 90 minutes, is unknown.
- **Tests that expect behaviour still to be decided.** The items of §4 left as they are (the history grammar, the responsibility tree, the mixed exit codes, the `SecretsRead` machine, the `→` arrow) are expected as they are today. Fixing one in sform means changing its case and test.
- **Open question 1 of the spec (a Windows CI job): left for later.** The runner supports Windows (ConPTY, the OEM decoding of redirected output), and so does sform's DPAPI token store. Every local run of this phase was on Windows 11. A Windows job would need a `windows-latest` leg. It would also need a setting that leaves out `SFORM_TOKEN_STORE=file`, which the runner always sets today (a single run can remove it through `SformRunOptions.Environment`), so that the DPAPI store is the one under test.

## Deviations from the spec

- **Cases changed while automating.** 15 rows changed in the catalogue (§3) where the planned steps could not produce the expected result against the code. Two Testiny edits were denied and one title is still open.
- **Current behaviour expected.** Some tests expect behaviour the review of the defects left unfixed (§4) instead of the catalogue's first intent. Each such item is recorded with its test.
- **GEN-CTX-01** shows the default project through a refusal, not a listing, for lack of a second readable project (§2).
- **CI artifacts are uploaded only on a failure or a cancellation.** The spec named the artifacts without a condition. The TRX report and `run.log` are added, and the workflow has a concurrency group.
- **A rule beyond the spec.** Feature users are a rule of their own (`UserRequirement.Except`, the target check's replacement). The spec named only `Target:Features`.

## Review findings

The two pieces of follow-up work after the merge, the defect fixes and the consolidation, were each reviewed. All four findings were real, and all were fixed.

1. **Low: `kv export` to standard output changed some values** (`KeyValueExportCommand.ToPayload`). `ReplaceLineEndings("\n")` also turned the U+2028, U+2029, U+0085 and form feed that a quoted env value keeps as they are into line ends, so `> .env` and an import read another value than the `-o` file. `ToPayload` now converts only the CRLF of the json indentation; every other CR or LF of a value is already escaped. New tests: a `ToPayload_HasLfLineEndsAndAFinalOne` case and `Export_Env_ToTheStandardOutput_KeepsTheUnicodeSeparatorsOfAValue`. Both fail with the old code.
2. **Low: "Output for scripts" in `sform.md` promised too much.** `kv get --value-only` without a value, the decrypt errors, unknown formats, the API's refusals and the schema `unified` diff errors all write to standard output. The docs now say that only the payload of a successful run is alone there; refusals and failures still write their message there with a non-zero exit code. The code was not changed, because KV-TRN-04 reads the refusal of `kv export --include-secrets` from standard output.
3. **Low: the target check could sign in a feature user** (`CliTargetCheck`). Fixed by the replacement of §5, with `SettingsSelfTests.TargetCheck_ReplacesAFeatureUser`.
4. **Low: the advice to add a feature user "after these" would have replaced the whole pool** (`testsettings.json`). The library does not merge pool entries by index. The comment and the docs now say to restate the role users in the same layer, and that the 8 CI slots limit the pool.

**Left open in this branch** (the comments and the `coverage-values.json` sources that still described the old `--format unified` after §15 were brought up to date before the commit):

- **Testiny cases 392, 320 and 401.** They are to be edited by hand (§3).

## Final review of the tests

Reviewers read the case tests against the catalogue, sform and the API, and predicted how they would fail on a real target. There were 7 findings: 1 high, 1 medium and 5 low. All 7 were real and all were fixed: 4 in the tests alone, 1 in the catalogue alone, and 2 in both. None was rejected. None was a defect of sform, so `src/Platform/Cli` did not change. Two of them describe behaviour of the API, and the "Not fixed" list of the [defects review](../../../Cli/specs/reviews/2026-11-08%20sform%20Defects%20Found%20by%20the%20CLI%20Test%20Cases.md#not-fixed-out-of-scope) now records them.

1. **High: KV-TRN-04 expected a message the API never sends on that path.** The export handler checks the secret read right before it calls the service (`KeyValuesHttp.cs:537`–`:540`). So a Contributor's `kv export --include-secrets` gets "The caller has no rights to the secrets of the project." (403 `AccessDenied`). The service's "… no rights to export secrets." is never reached. The test now expects the real message and "Error code: AccessDenied". The catalogue row and Testiny case 416 were corrected the same way.
2. **Medium: writes right after a policy change ran once.** Each API instance caches the key/value policy for a minute, and a write is checked against that copy. So the first hyok write after hyok is allowed or a recipient changes could be refused at random on a scaled-out target, and so could the first byok or hsm write after that level is allowed. Those writes are KV-HYK-03 to KV-HYK-06 (the sets and the re-encryptions) and the `kv set`, `kv protect` and `kv import` of KV-POL-07 and KV-POL-08. Each one now goes again until it succeeds, for at most `KeyValuesSupportA.PolicyCacheWait`, as KV-CTX-01 already did (`KeyValueRecipientTests.AfterPolicyChangeAsync`, `KeyValuePolicyTests.AtNewLevelAsync`). A refused write stores nothing. `GET …/recipients` reads past the cache, so sform always encrypts for the current recipients.
3. **Low: KV-CTX-02 and KV-POL-04 needed the platform's BYOK application, but no setting said so.** Without it the API answers `byok-setup` and `byok-verify` with 501 `ProtectionNotConfigured`, before it looks at the project. The suite now has a feature for it, `ByokApplication` (`CliFeatures`). `Byok` implies it (`CliTarget.HasFeature`, tested by `SettingsSelfTests.Features_ByokImpliesTheByokApplication`), and the `dev` profile lists it, because the Aspire host's Key Vault emulator stands in for the application. KV-POL-04 is now `[RequiresFeature(CliFeatures.ByokApplication)]`, mode `P, X:ByokApplication`. KV-CTX-02 still runs on every target: without the feature, its BYOK steps expect exit 2 and `ProtectionNotConfigured`, which still shows that the options reached the API. The two commands stay covered by KV-CTX-02, so `coverage-exceptions.json` did not change. The catalogue's legend, the KV-POL-04 and KV-CTX-02 rows, `automation-tests.md` and `testsettings.json` describe the feature.
4. **Low: KV-CTX-02 compared the Standard key of the shared project.** The first Standard secret of a project records its key in the policy, and other tests write Standard secrets into `Target:Project` at the same time. So the version could go from none to one between the two reads. The test now compares the version only when the first read already had one.
5. **Low: CFG-SET-10 did not check the server's message for the failing `test` op.** It now also expects "JSON Patch operation 0 failed:", the message that `ApplyPatch` sends with 412 `PatchTestFailed`.
6. **Low: CFG-HIS-01 did not check the authors.** `ConfigOutput.VersionAuthors` reads the first word of each row's Author cell. The test now requires one author for all three versions, and that author must not be "unknown", "system" or a machine (`AuditConsoleExtensions.FormatActor`). The name comes from the provider, so it is not compared with a value.
7. **Low: CFG-GET-01's catalogue row expected labels.** No write path stores labels on a configuration: they belong to the annotation (`story set -l`). The row now expects the content and the version. Testiny case 368 was corrected the same way. The test was already right.

**Testiny:** cases 368 (the expected results of both steps), 416 (the Contributor's refusal), 445 (the precondition: mode `P, X:ByokApplication` and the requirement) and 462 (the expected results of the `byok-setup` and `byok-verify` steps) were edited in project 2. Only the text concerned changed, and no title. No edit was denied.

**Verified:** the build of `src/Platform/Cli.AutomationTests/src` has no errors and no warnings in the project. `dotnet test --project src/Platform/Cli.AutomationTests/src` ran 313 tests: 84 succeeded (the self tests and the coverage and catalogue checks) and 229 case tests skipped without a target. As before, none of the changed case tests has run against a target.

## After the rebase onto #170 to #176

The branch was rebased onto `main` after seven pull requests merged. Their descriptions and code were checked for the cases they affect. The coverage and catalogue checks found the structural effects (new commands and options, removed options); the behavioural ones were found by reading the changes.

| Pull request | Effect on the cases | What changed here |
|---|---|---|
| #170 (424-214) Hosting: who am I and a catalog for any annotation | None: no sform change | — |
| #171 (424-216) Short help of export and import; `TimeProvider` | None: GEN-CLI-01 checks the forms in the help, not the descriptions. Its `story kv` crash fix is the one of section 14 of the defects review, so the two `Startup` registrations became one, and `StartupTests` and `CommandConstructionTests` both stay | — |
| #172 (424-213) `-x` names are paths | CFG-SET-01: `-x b.c=true` now nests | Its test and row check `{ "b": { "c": true } }`, and `-x "['e.f']=2"` for a dotted name (Testiny 395) |
| #173 (424-217) `sform story edit` | A new command, uncovered | A new sub-folder **Edit** (`AnnotationEditorTests`, Testiny folder 121) with ANN-EDT-01 to 08 (464 to 471), and ANN-CTX-02 (473) for its context options; two self tests run `story edit` against the stub API in a pseudo terminal and with redirected streams |
| #174 (424-210) ETags, `If-Match` and a merge `PATCH` | `story set` merges by default and has `--replace` | ANN-SET-10 (472) covers `--replace`; ANN-SET-06 (290) now checks that `--restore-deleted` keeps and merges the restored content. The other `story set` steps only add to new keys, so they stay as they are |
| #175 (424-220) `story config export` | `--github-env`, `--mask`, `--allow-unprefixed` and `--allow-unresolved` left `story config` | CFG-GHA-01 to 05 (436 to 440) and GEN-CLI-05 (396) run `config export`; CFG-GHA-06 (463) covers `-e` and every `--format`; CFG-CTX-01 (441) adds the export's context options; `coverage-values.json` moves `--mask` to `config export` and adds its formats |
| #176 (424-218) `sform account settings` and `sform.settings.json` | New commands; the default project and the editor moved into the settings file | `SformHome` resets and reads `sform.settings.json`, the fake editor is set in its `editor` section, and the tests read the default project from it; a new sub-folder **Settings** (`AccountSettingsTests`, Testiny folder 122) with ACC-STG-01 to 11 (474 to 484); SGN-OUT-04, ACC-PNT-04, ACC-SET-04, GEN-CTX-02 and GEN-CLI-03 follow the new file and messages. `coverage-values.json` can now list the fixed values of an argument (`argN=value`): the editor type and the keys of `set` and `unset` |

The catalogue has **251 cases** (22 new, all created in Testiny 2SC, no edit denied), each automated; sform has 105 commands, 35 aliases, 618 option forms, 87 arguments and 147 fixed values, 992 items, every one covered. In sform itself the rebase joined `CliPaths` with the settings store of #176 (see the review of phase 1), and the newest OpenAPI document of the TypeScript SDK (`open.api.v0.10.31.json`) describes `Pkcs12` as base64 like the C# one, so a new generation keeps the generated files as they are.

Noted, not changed: the redirected-input stop of `story edit` (43) is a plain line on standard output, not the "! sform cannot ask for …" of the other prompts on standard error (ANN-EDT-04 expects what the code does).
