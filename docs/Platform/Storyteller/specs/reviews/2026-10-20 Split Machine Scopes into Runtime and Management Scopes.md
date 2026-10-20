# Split Machine Scopes into Runtime and Management Scopes

## Overview

### What changed

Machines now have two kinds of scopes:

- **Runtime scopes** are the eight `MachineAccessScope` presets, which do not change. They keep every read and the per-document writes.
- **Management scopes** are a separate, explicit list on the machine: `Configuration.Transfer`, `Schema.Write`, `Template.Write`, `Annotation.Manage` and `KeyValue.Manage`. They are never part of a preset.

### How the API enforces them

- **Which operations.** Twenty-two bulk and structural operations need a management scope from a machine, on top of their runtime scopes. These are the export and import of a view (with job reads and the archive), the export and import of key/values, the six schema writes, the three template writes, bulk create, soft delete and restore of annotations, and key/value protect and bulk restore.
- **Where and in which order.** The check runs in the machine branch of `CheckAccessAsync`. It runs after the membership, the credential policy and the annotation restriction, and in every build.
- **What it checks.** The scope must be stored on the machine **and** carried by its credentials. If either is missing, the call answers `403` `AccessDenied`.
- **Who may grant them.** Only an Administrator creates a machine with management scopes. Resetting such a machine or replacing its federation rules also needs Administrator.

### How the scopes reach every credential

- API keys, certificates and federated tokens carry them.
- AuthKit, Entra ID and Keycloak provision them, or refuse before they create anything.
- The OpenAPI document and both SDKs (0.9.43), `sform account machine create -m`, the admin UI and its mock, and the docs follow.

### What existing machines get

Existing machines keep runtime access only, the strict choice of the spec.

### Spec and issue

This review implements [Split Machine Scopes into Runtime and Management Scopes](../2026-10-20%20Split%20Machine%20Scopes%20into%20Runtime%20and%20Management%20Scopes.md) in full. It was delivered in the seven phases of spec section 14, one review for all of them. Linear: [424-57](https://linear.app/424fun/issue/424-57/split-machine-scopes-into-runtime-and-management-scopes).

## What Was Done

### 1. Runtime scopes and management scopes

As specified:

- The eight presets keep their names, values 0–7 and expansions.
- The five management scopes cover the operations of the spec's table (section 7.2). Each one adds to the runtime scopes and never replaces them.
- One transfer scope covers both directions.
- A view import needs no more than `Configuration.Transfer`, although it writes schemas, templates and annotations.
- Users are unchanged.

### 2. The model

**The enum and the strings.**
- `Abstractions.Access/src/MachineManagementScope.cs`: `ConfigurationTransfer = 0`, `SchemaWrite`, `TemplateWrite`, `AnnotationManage`, `KeyValueManage = 4`. It uses the Newtonsoft `StringEnumConverter`, like `MachineAccessScope`.
- `Api.Functions/src/Security/Scopes.cs`: `Scopes.Configuration.Transfer`, `Scopes.Schema.Write`, `Scopes.Template.Write`, `Scopes.Annotation.Manage` and `Scopes.KeyValue.Manage`. `MachineAccessRulesTests.ManagementScopes_MatchTheApiScopeNames` pins them to the strings of `MachineAccessScopes`.

**The new members of `MachineAccessScopes`** (`Get(MachineAccessScope)` is unchanged):
- `Get(MachineManagementScope)`;
- the union `Get(MachineAccessScope, IEnumerable<MachineManagementScope>?)`: the preset's strings first, then the management strings in enum order;
- `Normalize`: defined values, distinct and ordered, `null` when none;
- `FindUndefined`.

**The models.** `MachineAccess`, `IMachineAccess` and `MachineAccessCreate` have an optional `IReadOnlyList<MachineManagementScope>? ManagementScopes`. It is never `required`, and `null` means none.

### 3. Creating a machine

**`AccessHttp.PostMachines`**, after the annotation key is normalized:
1. An undefined value is `400` with "The machine management scope 99 doesn't exist.".
   - Only numbers reach this check. An unknown name already fails to read the body: `400` "The request body is not a machine (at $.ManagementScopes[0]).".
2. The list is normalized.
3. A non-empty list needs `Administrator`.

**Storage.**
- The router passes the list on with `model with`, and every service returns it.
- `MachineAccessEntity` stores it as `List<MachineManagementScope>?`.
- `CosmosAccessService.CreateMachineAccessAsync` writes it, and `AccessMappings.ToMachineAccess` reads it back.
- Reset, the rules `PUT` and certificate renewal keep it through `with`.

### 4. Who manages a machine with management scopes

- **The helper.** `AccessHttp.EnsureAdministratorForManagementScopesAsync` reads the stored machine and needs `Administrator` when it has management scopes. A missing machine keeps the route's own answer.
- **Where it is called.** `PutMachine` (reset) and `FederationHttp.PutMachineFederationRules` call it. In the rules `PUT`, the check runs before the body is read: a Contributor who sends an invalid body for such a machine gets `403`, not `400`.
- **What stays at Contributor.** Delete, get and list.
- **`DEV_AUTH`.** These are user role checks, so Debug builds skip them.

### 5. Credentials carry the management scopes

**API keys.**
- `IApiKeyHashStore.StoreAsync` and `ApiKeyHashEntry` have the list.
- `CosmosMergedApiKeyHashStore` writes it on the machine document and reads it back.
- The unregistered `CosmosApiKeyHashStore` and `ApiKeyHashEntity` follow.
- `ApiKeyMachineAccessService` stores the list on create, stores it again on reset, and returns it in `ApiKeyValidationResult`.

**Certificates.**
- `CertificateRecord`, `MachineCertificateEntity` (`ToRecord`, `FromRecord`) and `ClientCertificateValidationResult` have the list.
- `CertificateMachineAccessService` puts it into both records and into a certificate-only result.
- `ClientCertificateValidator` returns it for a machine certificate, and none for a shared certificate.
- There is no backfill of older `mcert.` records.

**The middleware.**
- `MachineAuthenticationMiddleware` takes the list from the same result that supplies the preset.
- `MachineScopeClaims.GetRoleClaimsForScope(scope, managementScopes)` returns the union.
- The `401` for empty runtime roles still depends on the preset only.

**Federated machines.**
- `FederatedMachineAccessService` returns the list.
- `FederatedTokenExchangeService` uses the union for the subset check and for the default token: a request without `scope` gets the management scopes too.
- `FederatedMachineAccess.EnsureAllowedAsync` checks the excess against the same union.

### 6. Identity providers

**Every provider** normalizes the requested list once. It uses that list for provisioning, for the description and for the result.

**AuthKit.**
- `GetPermissionSlugs(scope, managementScopes)` works on the union.
- Each requested management scope must be covered by a picked slug. Otherwise it throws "No WorkOS permission in Auth:AuthKit:PermissionMap maps to the management scope …" before WorkOS is called.
- **Deviation.** The "no slug" rule now asks whether a picked slug covers a runtime scope, instead of `slugs.Count == 0`. Otherwise a management-only slug would let a preset without any permission through. For machines without management scopes, both checks behave the same.
- The description gets `|manage=…`, placed right after `scope=` and before `|annotation=`.

**Entra ID.**
- **Role keys.** The optional `Auth:AppRoles:<EnumName>` keys are resolved before any Graph call. A missing key, or a value that is not a GUID, throws "Machines with the management scope … need the app role Auth:AppRoles:…, which this deployment does not configure.".
- **Requests.** The machine gets one `ResourceAccess` and one `AppRoleAssignment` per role: the runtime role first, then the management roles in enum order.
- **`Notes`** get `|manage=…`.
- **Startup.** `AzureAdMachineAccessOptionsValidator` does not require the keys. A key that is set must be a GUID, or startup fails.
- **Helper.** `AzureAdMachineAccessService.GetManagementRoleKey(scope)` returns the key name. The service and the validator share it.
- **Unchanged.** The `Scope` collapse.

**Keycloak.** `storyteller_scope` holds the union, the description gets `|manage=…`, and the result carries the list.

### 7. Enforcement in the API

**The check.**
- `CheckAccessToProjectAsync`, `CheckAccessToAnnotationAsync` and `CheckAccessToAnnotationsAsync` take a trailing optional `MachineManagementScope? managementScope`.
- In the machine branch of `CheckAccessAsync`, `EnsureManagementScope` runs after `ensureReach`. It throws `AccessDeniedException` (`AccessDenied`):
  - "This operation needs the management scope X, which the machine was not created with." when the scope is not stored;
  - "…, which the credentials of the machine do not carry." when it is not carried.
- It runs in every build. Users ignore the parameter.

**The 22 call sites.** All of them pass their scope with the role they already passed, as in the table of spec section 7.2. Nothing else changed:

- `CheckScope`;
- the roles;
- the secret rights;
- the mTLS rule;
- the restrictions;
- the order of the transfer checks.

The key/value import checks the scope outside `UnderMtlsRuleAsync`, so a missing scope is never reported as `MtlsRequired`.

**Unchanged and runtime:**

- `SetAnnotation`;
- the configuration writes;
- `SetKeyValue`;
- the soft `DeleteKeyValue`;
- every read.

### 8. OpenAPI and SDKs

**The document filter.** `OpenApi/Filters/MachineManagementScopeDocumentFilter.cs`:
- adds the shared `MachineManagementScope` enum schema without a default;
- points `ManagementScopes.items` of `MachineAccess` and `MachineAccessCreate` at it.

**The descriptions.**
- `ResponseTransferForbiddenExport` and `ResponseTransferForbiddenImport` get "A machine also needs the management scope Configuration.Transfer (AccessDenied).". Their pinned start and end are unchanged.
- New constants, each the shared text plus the scope sentence:
  - `ResponseForbiddenManagementSchemaWrite`, `…TemplateWrite`, `…AnnotationManage` and `…KeyValueManage` (the shared text is `ResponseForbidden`);
  - `ResponseMtlsRequiredConfigurationTransfer` and `…KeyValueManage` (the shared text is `ResponseMtlsRequired`).
- These go as explicit `403` responses on the schema, template and annotation writes, `RestoreKeyValues`, and the key/value export, import and protect.
- `PostMachines` (`400` and `403`), `PutMachine` and `PutMachineFederationRules` (`403`) document the Administrator rule. They share `ResponseForbiddenMachineManagementScopes`.
- **Not in the spec.** The operation description of `PutMachineFederationRules` now says "Contributor; Administrator for a machine with management scopes.".

**The snapshot and the SDKs.**
- `OpenApiExportTests` exported `info.version` `0.9.43.53580`.
- `Sdk.NSwag/open.api.v0.9.43.json` was added. In `sdk.typescript/`, it replaces `open.api.v0.9.32.json`.
- `ApiSdk.g.cs` was regenerated with nswag 14.6.3. It has `ICollection<MachineManagementScope> ManagementScopes` and the new enum.
- `npm run generate` added `generated/model/machineManagementScope.ts` and the optional arrays in the models, Zod and MSW, and set `package.json` to `0.9.43`. A second `generate` leaves `generated/` unchanged.

### 9. `sform`

**`sform account machine create -m|--manage <scope>`.**
- It is a `MultipleValue` option, repeatable or comma-separated.
- It accepts the enum name, the scope string or kebab case, in any case. The new helper `Commands/MachineAccess/ManagementScopes.cs` parses it.
- An unknown value prints the allowed names and returns `ERROR_WRONG_INPUT` (2) before any call.
- A value that names no scope (`-m ","`) sends nothing.

**The output.**
- The printed machine has `ManagementScopes` as names. The anonymous object moved into `MachineCreateCommand.Describe(machine)` so a test can check it.
- The machine list (`sform account machine`) has a last column `Management`.
- The help of `sform story export` and `sform story import` ends with "A machine needs the scopes of the included kinds and the management scope Configuration.Transfer instead of a role.".

**Addition (code review): the server's message for a missing scope.** `sform story delete`, `restore` and `template delete` left the `403` to `Program.Main`. It printed the general sentence and a hint to check the machine's credentials. `story set` and `template set`/`edit` printed the SDK text and exited with 1.

- **The fix in `Program.Main`.** The new `Output/ApiRefusal` gives `Program.Main` the server's message and `ErrorCode` for a `403` that has them, without the credentials hint.
- **The fix in the commands.** The six commands catch the `403` with `Console.WriteApiError` and exit with 44, as the key/value commands do. `story restore` first lists what earlier batches restored.

### 10. Admin UI

**Texts.** `utils/machines.ts` has `managementScopeDescriptions`, `managementScopeLabels` (the scope strings) and `managementScopes`.

**The New machine dialog.**
- It has a "Management scopes" `UCheckboxGroup` under the scope.
- The group is empty whenever the dialog opens.
- The warning is the field's description, coloured `text-warning`, not a separate alert.
- Below Administrator the group is disabled, with "Only Administrators give management scopes.".
- The body sends the list only when a scope is picked.

**The list and the detail.**
- The list shows one neutral subtle badge per scope after the scope badge.
- The detail has a "Management scopes" row with descriptions, or "None".

**The mock API.**
- Create stores the list in canonical form. It refuses an unknown value with `400`, and needs Administrator for a non-empty list.
- Reset and the rules `PUT` of such a machine need Administrator.
- The seed is unchanged, so the visual baselines are too.

**Addition (code review).**
- **Reset and Edit rules.** Below Administrator, Reset (list) and Edit rules (detail) are hidden for a machine with management scopes (`needsAdministrator(machine)`), since the API always refuses them. Delete stays.
- **E2E hook.** The e2e tests add machines through a new mock hook, `window.storytellerMock.addMachine`. Its type moved to `test/e2e/support.ts`.

### 11. Existing machines

- Strict, as specified: existing machines have no management scopes, and no migration or toggle gives them any.
- Users are unchanged.
- The docs describe the migration: an Administrator creates a new machine with the scopes, the pipeline switches to it, and the old machine is deleted.

### 12. Documentation

**`authentication.md`** has a new section "Management scopes of machines" with:
- the catalogue;
- the check, its order and messages;
- how each credential kind carries the scopes;
- who manages them;
- the profiles;
- the limits;
- the migration.

These places in it were also updated:
- the `DEV_AUTH` paragraph;
- `DefaultUserScopes`;
- pipeline variables;
- creating a machine;
- every machine call;
- the `MachineAccess` table (`Scope` as runtime presets, a new `ManagementScopes` row);
- AuthKit, Entra ID and Keycloak;
- the annotation restriction;
- federation (the token endpoint, the per-call checks, the security notes);
- the caveats;
- the 401/403 table and a `400` row;
- the role summary;
- "Not available yet".

**Other Storyteller docs:**
- `access.md`.
- `export-import.md`, including the snapshot name `open.api.v0.9.43.json`.
- `key-values.md`. The rows are split, so single-key set and delete stay runtime.
- `annotation-lifecycle.md`.
- `templating.md` and `definitions.md`. The wrong scope strings `Configuration.Write` and `Default.Write` became `Configuration.ReadWrite` and `Default.ReadWrite` on the edited lines.

**The public site and READMEs:**
- `admin-ui.md`, `using-the-platform.md`, `configuration.md`, `annotations.md` and `cli/sform.md`, including a table of `-m` values per `sform` command;
- `sdk.typescript/README.md`;
- `ui.admin/README.md`.

The site builds without dead links.

Specs and earlier reviews are unchanged.

### 13. Tests

| Project | Before | After |
|---|---|---|
| `Api.Functions/test`, Debug | 1134 passed | 1313 passed |
| `Api.Functions/test`, Release | 1125 passed, 9 failed | 1304 passed, the same 9 failed |
| `Backend.Core/test` | 810 | 824 |
| `Access.Federation/test` | 252 | 257 |
| `Access.Certificates/test` | 89 | 107 |
| `Access.AuthKit/test` | 132 | 137 |
| `Access.AzureAd/test` | 55 | 58 |
| `Access.Keycloak/test` | 24 | 26 |
| `Cli/test` | 692 | 733 |
| `sdk.typescript` (Vitest) | 126 passed, 1 skipped | 126 passed, 1 skipped |
| `ui.admin` (Vitest) | — | 960 passed |
| `ui.admin` Playwright, desktop and mobile | — | 328 passed, 8 skipped (existing skips); later runs of the changed specs passed |
| `ui.admin` visual (Docker) | — | 64 passed, no baseline changed |

**The 9 Release failures existed before this branch.** In each, a test double returns a role that is too low:

- `AnnotationsHttpTests.Purge_ByUser_CallsTheService`;
- eight `MachineHttpTests` rows: `GetMachine_StoredRecord_ReturnsIt`, `KeycloakId_GetResetAndDelete_ReachTheAccessService`, `MachineAuthentication_OfAnOrganization_IsBadRequest`, and the five `PutMachineAuthentication_*` rows.

**New tests.**

- **The model.** `Backend.Core/test/MachineAccessScopesTests.cs`.
- **The check.** In `MachineAccessCheckTests`, for each helper that takes the parameter:
  - a scope stored and carried passes;
  - a scope not stored, or not carried, gives `403` with its message;
  - a machine with runtime scopes only gives `403`;
  - an unknown machine gives `401` first;
  - the restriction is checked first;
  - a user passes.
- **Federated machines.** `MachineAccessRulesTests` (addition from code review): the same three cases through the access check.
- **Every operation.** `ViewTransferHttpTests` (every route), `KeyValuesHttpTests`, the new `ConfigurationSchemaAuthorizationTests` and `TemplateHttpTests`, `AnnotationsHttpTests` and `MachineRestrictionHttpTests` cover:
  - a runtime-only machine gives `403` with no service call;
  - a machine with the right scope passes;
  - other management scopes do not help;
  - users are unchanged.
- **Claims and creation.**
  - `MachineAuthenticationMiddlewareTests`: the role claims per credential.
  - `MachineHttpTests`: create, the `400`s, the Administrator rule and reset.
  - `FederationHttpTests`: the rules `PUT`.
- **OpenAPI.**
  - `MachineManagementScopeDocumentFilterTests`;
  - `OpenApiExportTests`: the list, the shared enum and the 403 text of all 22 operations.
- **The other projects.**
  - Federation: subset, default and excess.
  - `PolicyAwareManagementScopesTests`: every kind and both certificate records.
  - The provider tests: scopes only when requested, and a failure before any provider call.
  - The CLI: every spelling, the list column, the help and the server's 403 messages.
  - The UI: the dialog, the mock rules, `needsAdministrator`, two e2e flows and axe for the dialog.

**The Cosmos round trip, `Access.Certificates/testint/ManagementScopesPersistenceTests` (4 tests).** It covers create, get, validate and reset of API key, certificate and certificate-with-API-key machines.

- **With a local fix, all 44 tests passed.** That run used Testcontainers with a temporary local change to `CosmosFixture` (an `https` connection string).
- **As committed, the suite fails** in the fixture with Testcontainers, for every test, before and after this branch. `CosmosFixture` pins the HTTPS `azure-cosmos-emulator:latest` image, while Testcontainers.CosmosDb 4.11 hands out an `http` connection string.

`Backend.CosmosDb/test` was only built, because it deletes the databases of the local emulator.

### Code review

The review used seven lenses:

- enforcement completeness across every HTTP function;
- the credential plumbing and its serialization;
- who may give or take over such a machine;
- the identity providers;
- the clients and the SDKs;
- the docs;
- the tests.

Two independent skeptics verified each finding.

**The security lenses found nothing.** Enforcement, credentials and roles found no bypass, lost grant or escalation path.

**Five findings were confirmed and fixed:**

1. **`sform` hid the server's 403 for a missing scope.** On `story delete`, `restore` and `template delete` it printed the general credentials text, and `story set` and `template set`/`edit` exited with 1. Fixed in section 9.
2. **The admin UI offered Reset and Edit rules to Contributors** on machines with management scopes. The API always refuses them. Fixed in section 10.
3. **`sform.md` promised the server's message** before fix 1 made it true.
4. **The docs named a non-existent `sform account machine list`.** Fixed to "the machine list (`sform account machine`)". Spec section 9.2 still has the old name, and specs stay unchanged.
5. **The docs gave the "doesn't exist" `400` for every unknown value.** It applies to numbers only. Names fail when the body is read, now documented and pinned by two `MachineHttpTests` rows.

**One finding split the skeptics:** no test went through the access check with a federated machine and a management scope. The code was right, and the tests were added.

**Three findings were refuted:**
- A machine whose token carries `User.Impersonation` passes the Administrator gate. This existed before the branch, for every user-only route that checks `CheckScope`, and is not made worse.
- The API key reset keeps the old hash. This existed before the branch and is listed in the spec's out of scope.
- Entra ID accepts any GUID as a role. A misconfiguration, as for the runtime roles.

### Deviations from the spec

- **AuthKit.** The "no slug" rule is now "no picked slug covers a runtime scope" (section 6).
- **The rules `PUT`.** The Administrator check runs before the body is read (section 4).
- **One extra read.** `PutMachine` reads the stored machine first. `MachineHttpTests.KeycloakId_GetResetAndDelete_ReachTheAccessService` now expects two `get:` calls; it is still one of the 9 known Release failures.
- **The 403 constants.** There is no `ResponseForbiddenManagement…` constant for `Configuration.Transfer`: its routes use the transfer texts and `ResponseMtlsRequiredConfigurationTransfer`.
- **sform.**
  - The option property is `ManagementScope` (singular), so it does not clash with the helper class.
  - The list column joins the scope strings with ", ".
- **Admin UI.** Reset and Edit rules are hidden from Contributors for machines with management scopes, which section 10 did not ask for.
- **Code-review fixes beyond the spec.**
  - `sform` prints the server's 403 message for the annotation and template commands, and `Program.Main` prints it too.
  - `admin-ui.md` describes the hidden buttons.

### Left for later

These are pre-existing defects, listed in the spec's out of scope:

- **API key reset.** `CosmosAccessService.ResetMachineAccessAsync` upserts its stale copy of the machine document. The new key fails and the old key keeps working.
- **Certificate renewal.** It writes no new `mcert.` record.
- **Entra ID.** It has no compensation after a failed Graph call.
- **`Access.Certificates/testint`.** It does not run with Testcontainers as committed (see section 13).

These are smaller follow-ups:

- **The `sform` schema commands** print the server's 403 message but exit with 1. This is existing, documented behaviour.
- **The detail of a federated machine** still tells a Contributor to "change it with Edit rules" when Edit rules is hidden from them.
- **`authentication.md`** still says `sform machine …` in two older places (around lines 428-432 and 1017).
- **Editing the management scopes of an existing machine** is not possible. The machine has to be recreated.
- **The API allow-list of 424-58** should allow the transfer functions to machines with `Configuration.Transfer`.
