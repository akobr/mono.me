# Replace Machine Scope Presets with a Flat Scope Set

## Overview

### What changed

A machine is now exactly one list of scopes, `MachineAccess.Scopes`, of the enum `MachineScope`:

- **Runtime:** `Annotation.Read`, `Annotation.Write`, `Configuration.Read`, `Configuration.Write`, `Secrets.Read`, `Secrets.Write`.
- **Management:** `Configuration.Transfer`, `Schema.Write`, `Template.Write`, `Annotation.Manage`, `KeyValue.Manage`.

The presets (`MachineAccessScope`), `Default.Read`, `Default.ReadWrite`, `Configuration.Secrets` and the separate `ManagementScopes` list are gone. Creation adds `X.Read` for `X.Write`, and nothing expands at request time.

### How the API enforces it

- **Machines.** Every operation names its scopes in the access check it already made. Each scope must be stored on the machine **and** carried by its credentials, in every build, or the call answers `403 AccessDenied`. One scope per handler: reads need `X.Read`, writes `X.Write`, a management operation its runtime scope and its management scope. The federated excess check is gone.
- **Users.** The role decides. A user token needs `User.Impersonation` and no data scope; user secret rights come from `ContributorWithSecrets` only. User-only routes refuse every machine in every build.
- **Giving scopes.** Creating, resetting or re-pointing a machine needs `ContributorWithSecrets` for a secrets scope and `Administrator` for a management scope.

### How the scopes reach every credential

- API keys, certificate records and federated tokens carry the stored list.
- AuthKit, Entra ID and Keycloak map one permission, app role or claim value per scope, and refuse before they create anything. Entra ID no longer collapses scopes.
- `Register-MachineScopes.ps1` creates the Entra app roles and the WorkOS permissions.
- The OpenAPI document and both SDKs (0.9.46), `sform account machine create --scope`, the admin UI and its mock, and the docs follow.

### Spec and issue

This review covers [Replace Machine Scope Presets with a Flat Scope Set](../2026-10-21%20Replace%20Machine%20Scope%20Presets%20with%20a%20Flat%20Scope%20Set.md) in full: the six phases of spec section 14 and two corrections from the final code review, one review for all of them. Linear: [424-79](https://linear.app/424fun/issue/424-79/replace-machine-scope-presets-with-a-flat-scope-set).

| Commit | Content |
|---|---|
| `007fb165` | The spec |
| `063daae8` | Phase 1: the server |
| `fe356004` | Phase 2: the provisioning script |
| `3bb10721` | Phase 3: the SDKs from OpenAPI 0.9.46 |
| `24d80e92` | Phase 4: `sform` |
| `aad33b7d` | Phase 5: the admin UI |
| `5f755853` | Phase 6: the docs |
| `dc787918`, `8b0e1040` | Corrections from the final code review |

## What Was Done

### 1. The scope catalogue

As specified:

- `Abstractions.Access/src/MachineScope.cs` (values 0–10, Newtonsoft `StringEnumConverter`) replaces `MachineAccessScope.cs` and `MachineManagementScope.cs`.
- `Backend.Core/src/Accessing/MachineScopes.cs` replaces `MachineAccessScopes.cs`: `ToScopeString`, `TryParseScopeString`, `Runtime`, `Management`, `All`, `IsRuntime`, `IsManagement`, `IsSecrets`, `Normalize`, `WithImpliedReads`, `FindUndefined`, `ToScopeStrings`.
  - `TryParseScopeString` takes a `string?`, so claim values go in directly.
  - `ToScopeStrings` keeps the given order; callers that want enum order call `Normalize` first.
- `Api.Functions/src/Security/Scopes.cs` keeps the constants for the OpenAPI texts. `MachineAccessRulesTests` pins all 11 to `MachineScopes.ToScopeString`.
- Enum names go on the wire, in Cosmos and in settings keys; scope strings in claims, tokens, identity-provider values and messages.

### 2. The model

- `MachineAccess.Scopes` and `IMachineAccess.Scopes` are `required`. `MachineAccessCreate.Scopes` is optional, so a missing list reaches the handler's own `400`.
- The credential records carry one list: `IApiKeyHashStore.StoreAsync`, `ApiKeyHashEntry`, `ApiKeyValidationResult`, `CertificateRecord`, `ClientCertificateValidationResult` (a shared certificate carries an empty list). `IClientCertificateAuthority.IssueCertificateAsync` lost its unused scope parameter.
- `MachineAccessEntity.Scopes` and `MachineCertificateEntity.Scopes` are not `required`; a document without them reads as a machine without scopes.
- Deleted dead code: `ApiKeyAuthenticationMiddleware`, `CosmosApiKeyHashStore`, `ApiKeyHashEntity`.

### 3. Creating a machine

`AccessHttp.PostMachines` follows the spec order: the user and Contributor check, the body, the annotation key, `FindUndefined` ("The machine scope 99 doesn't exist."), `WithImpliedReads`, the runtime-scope rule ("A machine needs at least one runtime scope: …"), the role rule of section 4, then the create path.

- Every service returns exactly the requested list. AuthKit keeps the given order; Entra ID and Keycloak normalize it, which changes nothing after the handler.
- **Addition (final code review):** `Api.Functions/src/Json/MachineScopeJsonConverter.cs`. `JsonStringEnumConverter` read `"AnnotationWrite, ConfigurationRead"` as the ORed value `ConfigurationWrite`, although `MachineScope` is no `[Flags]` enum. A comma-joined string is now `400` "The request body is not a machine (at $.Scopes[n])."; names in any case, numbers and numeric strings are read as before. The converter is registered before the general enum converter in `Program.cs` and in the test options. The exported OpenAPI document did not change.

### 4. Who manages a machine

`AccessHttp.EnsureRoleForMachineScopesAsync` has two overloads: one for the requested list (create) and one that reads the stored machine (reset and the federation-rules `PUT`). Management scopes need `Administrator`, secrets scopes `ContributorWithSecrets`; both are user role checks that Debug skips.

### 5. Credentials carry the stored list

As specified:

- The API key service stores, re-stores on reset, and validates the list. `CosmosMergedApiKeyHashStore` writes it on the machine document.
- Both certificate records carry it; the validator returns it for a machine certificate and an empty list for a shared one.
- `MachineAuthenticationMiddleware` emits one `roles` claim per stored scope string; an empty list is `401`.
- `FederatedTokenExchangeService` allows the stored scope strings. A request without `scope` gets all of them in enum order, a requested subset keeps its order, and a machine without scopes gets `invalid_scope`.
- `FederatedMachineAccess.EnsureAllowedAsync` keeps the project, existence and kind checks; the excess check is removed.

### 6. Identity providers

**AuthKit.**
- `GetPermissionSlugs` looks up the one slug per scope and throws "No WorkOS permission in Auth:AuthKit:PermissionMap maps to the scope Secrets.Write." before WorkOS. The subset matching and the partial-coverage warning are gone.
- `AuthKitMachineAccessOptionsValidator` checks the map at startup: one known scope string (or `User.Impersonation`) per slug, and no machine scope mapped twice. It runs only where AuthKit machine access is registered.
- The description is `…|scopes=AnnotationRead,ConfigurationRead|…`.

**Entra ID.**
- `GetRoleKey(MachineScope)` replaces the four role constants and `GetManagementRoleKey`.
- Every requested scope needs its role before any Graph call, or creation answers `400`. One `ResourceAccess` and one assignment per scope; no collapse.
- The validator: set keys must be GUIDs, no two keys may share one, and the removed keys `DefaultRead`, `DefaultReadWrite` and `SecretsReadWrite` (in any case) stop the start with the new key names.
- **Addition (code review):** the API service principal is looked up before any Graph write. When it is missing, creation answers `400`; it used to skip the assignments silently and store scopes that were never assigned. A created service principal without an ID throws.

**Keycloak.** `storyteller_scope` holds the scope strings, the attribute `42.scopes` the enum names.

**The script.** `src/Platform/Storyteller/scripts/Register-MachineScopes.ps1`, PowerShell 7, idempotent, `-WhatIf`.
- **Deviation:** WorkOS runs with the switch `-WorkOs`; the key comes from `-WorkOsApiKey` or `WORKOS_API_KEY`. The spec named only the key, but with an environment default the script could not tell whether WorkOS was meant.
- Tested against a fake WorkOS API (paging, `-WhatIf`, creation of the missing slugs only) and a fake `az` (existing roles kept with writable members only, an `App.`-prefixed value counts, `-WhatIf` sends no `PATCH`). There is no test tenant.

### 7. Enforcement in the API

**The check** (`HttpRequestDataExtensions`):
- `CheckScope` and `TryCheckScope` are removed. The data helpers take the role and at least one scope; `CheckAccessToProjectMetadataAsync` takes any one of its scopes.
- Machine order: document (`401`), credential policy (`401`), restriction (`403 AnnotationRestricted`), each scope stored then carried (`403 AccessDenied`, the spec messages). The machine is cached in `FunctionContextItemKeys.StoredMachine`.
- Users: `User.Impersonation` (`401`), then the role (`403`), both skipped under `DEV_AUTH`.
- `EnsureUser` and `CheckUserAccessToAsync`, `CheckUserAccessToOrganizationAsync`, `CheckUserAccessToProjectAsync` guard the user-only routes; they replace `EnsureNotMachine`, the inline check of `PurgeAnnotation` and the old `CheckAccessToAsync`.

**Secret rights.**
- Machines: `CanReadSecrets` from `Secrets.Read`, `CanWriteSecrets` from `Secrets.Write`, each stored and carried. Users: `ContributorWithSecrets` only; the role is looked up only for a token that carries `User.Impersonation`.
- **Deviation (code review): withholding is reported per direction.** Both rights are still masked when the project requires mTLS and the request has none. `SecretRights` has `IsReadWithheldByMtls` and `IsWriteWithheldByMtls`, each set only for a right the machine has, so `403 MtlsRequired` and `X-2S-Secrets-Withheld` appear only when mTLS would actually help. `UnderMtlsRuleAsync` became `UnderMtlsReadRuleAsync` (diffs) and `UnderMtlsWriteRuleAsync` (set, delete, import); `EnsureSecretRights` became `EnsureSecretReadRights` (export with secrets) and `EnsureSecretWriteRights` (protect, which used to check the read bit).
- A restore still reports a refused secret key as `Forbidden` in its result, whether the scope or only the certificate is missing. The docs say so.
- `GetKeyValue?reveal=true` keeps its old shape: the handler checks the mTLS withholding and the service refuses a secret without the right.

**The handlers.** All functions of `V1/*.cs` follow the table of spec 7.5; the integration step checked every one. The 12 schema version reads name their scope once in `PrepareSchemaReadAsync`.

**`GetViews`** accepts `Annotation.Read` or `Configuration.Read`.

**The view transfer.**
- `ChecksScopes` is gone. Every route checks `Configuration.Transfer` in the access check, then, for machines only, the kinds on the stored-and-carried set (`GetGrantedMachineScopes`).
- **Not in the spec:** the "no credentials" `401` now applies in Debug too, so anonymous Debug exports are refused; for the job reads it also comes before the job is read.

### 8. OpenAPI, descriptions and the SDKs

- `MachineScopeDocumentFilter` replaces `MachineManagementScopeDocumentFilter`: one shared `MachineScope` enum without a default for both models.
- **The descriptions:**
  - The 401 of data routes (`ResponseUnauthorized`, the transfer routes included after the code review) lists no scopes; user-only routes use `ResponseUnauthorizedUserOnly` and `ResponseForbiddenUserOnly`.
  - Every machine-callable operation has an explicit 403 naming its scopes: `ResponseForbiddenAnnotationRead/Write`, `…ConfigurationRead/Write`, `…Management{AnnotationManage,SchemaWrite,TemplateWrite,KeyValueManage}`, `…Views`, `…BindingCatalog`, the `ResponseMtlsRequired…` variants of the key/value routes, and the transfer texts ("Users need no data scopes, only the Administrator role.").
  - `ResponseForbiddenMachineScopes` states the role rule; `ResponseBadRequestMachineAccessCreate` the `400`s.
  - Renamed constants: `ResponseForbiddenMachineManagementScopes` → `ResponseForbiddenMachineScopes`; `ResponseMtlsRequiredConfigurationTransfer` → `…KeyValueExport` and `…KeyValueImport`. `ResponseUnauthenticated` has no users left and is removed.
- The Entra implicit flow and `AuthConfigurationHttp` hand out only `User.Impersonation`.
- **The snapshot:** `open.api.v0.9.46.json` (`info.version` `0.9.46.25482`) in `Sdk.NSwag/` and, replacing `0.9.45`, in `sdk.typescript/`. `ApiSdk.g.cs` was regenerated with nswag 14.6.3 and the TypeScript SDK with `npm run generate`, which set `package.json` to `0.9.46`. The federation test strings and the README comment of the TypeScript SDK use the new scope strings.

### 9. `sform`

As specified:

- `--scope` (long only), `-m` for management scopes ("Secrets.Read is not a management scope; give it with --scope. …"), and `-r`/`-w` as shortcuts. The union goes out in enum order; without a runtime scope the `-r` set is added.
- `--scope secrets-read` alone sends only that scope. The default applies only when no runtime scope is given.
- `Commands/MachineAccess/MachineScopes.cs` replaces `ManagementScopes.cs`. It uses only the SDK enum, so there is no `CS0104`.
- `Describe` prints `Scopes` and `CredentialKind` by name. The console serializer is unchanged, so no other output changes.
- The list columns are `Id`, `Scopes`, `Kind`, `Secret`, `RestrictionTo`, `Management`.
- The Entra fallback is `User.Impersonation` only (`EntraIdAuthenticationService.GetScopes`, tested). The story export and import help name the read and write scopes.
- The 403 fixtures of the annotation and template commands use "This operation needs the scope X, …".

### 10. Admin UI

As specified:

- `utils/machines.ts` has the catalogue, the six presets and `requiredRoleFor`. It also exports `machineScopes`, `scopeLabel` (unknown values shown raw), `impliedReads` and `ScopePreset`.
- **The dialog:**
  - presets, then a Runtime and a Management group; write picks read, and clearing read clears write;
  - management scopes only for Administrators, secrets scopes only with `useCan('secrets')`, a preset disabled when it needs either;
  - every open starts from Runtime reader; Create needs a runtime scope; the body sends `Scopes`.
  - **Addition (code review):** the secrets descriptions say they need `Configuration.Read` or `Configuration.Write` as well.
- **The list and the detail** show one badge per scope (outline runtime, subtle management) and two rows on the detail. Reset and Edit rules follow `requiredRoleFor`.
- **The mock:**
  - it reads scopes like the server: names in any case, numbers, numeric strings; an unknown value or a comma-joined one is `400` at `$.Scopes[i]`, before an undefined number is named;
  - it adds the implied reads and applies the role rules of create, reset and the rules `PUT`;
  - `TokenScope` is the constant `api://storyteller/.default`;
  - the seed has `Scopes`, and the persisted key is `2s-admin:mock-db:2`.
- `SecretsPolicyCard` names `Secrets.Read`. `test/e2e/support.ts` types `addMachine` with `MachineAccess`, so the e2e machine bodies are type-checked now.
- The four `machines-*` visual baselines changed; no other baseline did.

### 11. Existing machines and settings

No migration, as specified.

- A machine document or `mcert.` record without `Scopes` has none. Its API key and certificate get `401`, a federated machine gets no token (`invalid_scope`), and a client-credentials machine gets `403`.
- `Api.Functions/src/local.settings.json` no longer has `Auth:AppRoles:DefaultRead` and `DefaultReadWrite`; the new validator would refuse them.

### 12. Documentation

- **`authentication.md`:**
  - "Management scopes of machines" became "Scopes of machines" (`#scopes-of-machines`); every link in the docs follows.
  - A new subsection, "Scopes in the identity providers", has one table (scope, group, Entra role key, WorkOS slug) and the script.
  - Rewritten: the user settings, "Scopes for AuthKit users", discovery, `DEV_AUTH`, machine creation, the `MachineAccess` table, AuthKit, Entra ID, Keycloak, the restriction, federation, the token endpoint, Storyteller tokens, the security notes, the caveats, 401 and 403, and "Not available yet".
- **Other Storyteller docs:** `access.md`, `key-values.md`, `export-import.md` (snapshot `open.api.v0.9.46.json`), `annotation-lifecycle.md`, `templating.md`, `definitions.md`, `binding.md`, `inheritance.md`, `audit-stamps.md`.
- **The public site and READMEs:** `admin-ui.md`, `using-the-platform.md`, `configuration.md`, `annotations.md` and `cli/sform.md` (a table of all 11 scopes with their `--scope` spellings), plus the READMEs of `ui.admin`, `sdk.typescript` and `Sdk.NSwag`.
- **Corrections (final code review):**
  - a `401` has no body; the reason is only logged;
  - a restore of secrets reports refused keys as `Forbidden`;
  - the view transfer checks `Configuration.Transfer` before the kinds.
- The site builds. A script checked every anchor link into `docs/Platform/Storyteller`.
- The role-driven example slug `storyteller:user` in `authentication.md` is an example name; nothing in the code needs it.

### 13. Tests

| Project | Before | After |
|---|---|---|
| `Api.Functions/test`, Debug | 1331 passed | 2166 passed |
| `Api.Functions/test`, Release | 1322 passed, 9 failed | 2166 passed |
| `Backend.Core/test` | 824 | 867 |
| `Access.Certificates/test` | 107 | 107 |
| `Access.Federation/test` | 257 | 268 |
| `Access.AuthKit/test` | 137 | 159 |
| `Access.AzureAd/test` | 58 | 79 |
| `Access.Keycloak/test` | 26 | 27 |
| `Cli/test` | 757 | 793 |
| `sdk.typescript` (Vitest) | — | 130 passed, 1 skipped |
| `ui.admin` (Vitest) | — | 1426 passed |
| `ui.admin` Playwright, desktop and mobile | — | 358 passed, 8 skipped (existing skips) |
| `ui.admin` visual (Docker) | — | 64 passed, 4 `machines-*` baselines updated |

**The 9 Release-only failures are fixed.** They were test-double defects in files this change rewrote: an Administrator double for the purge, an Administrator `RecordingAccess` for the machine authentication policy, and role lookups kept out of the exact `Calls` lists.

**What the new tests cover:**
- **The shared doubles.** `RoleAccess` stores the six runtime scopes plus the given management scopes, or an explicit list. `TestRequests` builds users with only `User.Impersonation` and machines with scope strings. `UserOnlyRoutes` builds a machine that carries every scope and `User.Impersonation`.
- **Every handler, from its table** (`MachineRestrictionHttpTests.Handlers`, the key/value table): refused when the runtime scope is not stored or not carried, refused without the management scope, the runtime scope named first, passes with exactly its scopes.
- **The check** (`MachineAccessCheckTests`, `MachineAccessRulesTests`):
  - stored, carried, the `App.` prefix and exact case;
  - the order (document, policy, restriction, scopes);
  - the any-of check;
  - federated tokens without the excess check;
  - `User.Impersonation` opening no user-only route;
  - a user without data scopes.
- **Secret rights:** read and write per direction, stored and carried, mTLS per direction, and users from the role only.
- **Creation and roles** (`MachineHttpTests`): every `400` (including comma-joined names), the implied reads, and the role rule for create and reset. `FederationHttpTests` covers the rules `PUT`.
- **OpenAPI** (`OpenApiExportTests`): all 133 operations are in one of three tables (84 machine, 45 user-only, 4 anonymous). Each machine operation's 403 names exactly its scopes, and no 401 names one.
- **The providers:** one permission, role or claim per scope; failure before the provider; the startup validation. The CLI, the SDK and the UI as in sections 9 and 10.

**Cosmos round trips.**
- `Access.Certificates/testint`: 44 passed against a Testcontainers emulator. As in 424-57, this needed a temporary local switch of `CosmosFixture` (environment variable and `https` connection string), which was reverted.
- `Backend.CosmosDb/test`: the 7 tests of `CosmosMachineAccessScopesTests` and `CosmosClientCertificateStoreTests` passed against a container with the same temporary switch.
- The rest of `Backend.CosmosDb/test` was not run. Its fixture targets the local emulator, whose databases it deletes. The container emulator exited under the full suite on this machine.

**E2E at the default worker count:** each run had one page load fail with `net::ERR_CONNECTION_REFUSED` on the preview server, each time in another, unrelated spec. The failed test passes alone, and the suite passes with 4 workers. CI runs 2.

### 14. Phases and commits

As planned, plus the two correction commits listed in the overview. Phase 1 is one commit, because every server project compiles against the new model together.

### 15. Out of scope

Unchanged, as listed in the spec: editing the scopes of a machine, the 424-58 allow-list, the stale API key reset, the missing `mcert.` record on renewal, Debug decoding of Keycloak tokens, and `Api.Web`.

Entra ID still has no compensation after a failed Graph call. Two failure cases now come before any write: a missing role and a missing API service principal.

### 16. Code review

The work was reviewed three times:
- Phase 1 alone, with six lenses: enforcement, the core check, credentials and persistence, providers, OpenAPI, tests;
- `sform` and the admin UI, each by its own reviewer;
- the whole branch, with three lenses: contracts across components, docs against code, the late server fixes.

Two independent skeptics verified each finding.

**Confirmed or split, and fixed:**
1. MtlsRequired was answered for a right the machine does not have (section 7).
2. The transfer 401 text missed `User.Impersonation`, and the 403 text said users need "no scopes" (section 8).
3. Entra ID stored scopes it never assigned when the API service principal was missing (section 6).
4. Comma-joined scope names were ORed into another scope (section 3).
5. Docs quoted a 401 body that is never sent, promised `MtlsRequired` for restore, and missed the view transfer exception (section 12).
6. In the admin UI: gating tests could not fail, the secrets descriptions omitted their configuration scope, the mock read scopes more strictly and in another order than the server, and the README claimed a test that did not exist (section 10).
7. Missing tests: the runtime scope named first when both scopes are missing, the policy before the scope, and the reveal of a key/value version by a machine.

**Refuted:**
- the API key reset that hides a scope drop (the stale upsert, out of scope);
- anonymous Debug transfers (intended);
- the mock accepting bodies without `Organization`;
- the reset of a certificate in the mock.

### Deviations from the spec

- **Secret rights:** withholding is reported per direction (section 7).
- **The script:** `-WorkOs` switch (section 6).
- **The view transfer:** the "no credentials" 401 also applies in Debug (section 7).
- **Additions from the code review:** the Entra service-principal check (section 6), the comma-joined names (section 3) and the secrets descriptions in the UI (section 10).
- **Renamed constants:** see section 8.

### Rebase on the description of a project and the showcase mock data

The branch was rebased on `main` after PR #123 (the showcase mock data of the admin UI) and the description of a project (424-77):

- The two new user-only routes, `PutAccessPoint` (describe a project) and `GetProjects`, use `EnsureUser` with their own messages and `CheckUserAccessToAsync`, and document the user-only 401 and 403. `OpenApiExportTests` lists them among the user-only operations.
- `AccessPointHttpTests.MachineRequest` carries the new scope strings.
- The SDKs were regenerated from `open.api.v0.9.46.json` (`info.version` `0.9.46.25482`), which replaces `0.9.45` in `sdk.typescript/`.
- The persisted mock database key `2s-admin:mock-db:2` replaces the key of `main`, next to its new scenario list.
- The counts in section 13 are those after the rebase. The visual baselines passed unchanged.
