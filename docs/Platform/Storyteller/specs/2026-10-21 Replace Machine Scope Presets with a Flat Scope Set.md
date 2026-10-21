# Replace Machine Scope Presets with a Flat Scope Set

Linear: [424-79 Replace machine scope presets with a flat scope set](https://linear.app/424fun/issue/424-79/replace-machine-scope-presets-with-a-flat-scope-set)

## Problem

After 424-57 (PR #121, merged), a machine has two scope sources:

- a scalar preset `MachineAccessScope` (8 values, expanded by `MachineAccessScopes.Get`);
- an additive `ManagementScopes` list (`MachineManagementScope`, 5 values).

This leaves three problems:

1. **Combinations are missing.** The presets cannot express, for example, annotation writes with secrets, or configuration writes with secret reads only.
2. **Every handler accepts an OR-list.** Each `CheckScope` takes the umbrella scopes `Default.Read` / `Default.ReadWrite` as well as the specific ones, and it checks only the credential, and only in Release builds.
3. **Entra ID collapses scopes.** `AzureAdMachineAccessService` stores and assigns `DefaultRead` / `DefaultReadWrite` for most presets, so a machine can get more than requested.

The goal of the issue: **scopes describe machines, roles describe people.** A machine is exactly its set of scopes; the server knows no presets. Users are authorized by their role on the access point only.

The platform has no production users yet, so **no migration and no backward compatibility** is needed.

The inputs to this spec are:

- the issue;
- a read-only map of the code, tests, clients and docs at `origin/main` `247c3ccb` (nine parallel readers, one per area);
- the CodeRabbit coding plan attached to the issue, used as a second opinion. Section 16 lists where this spec differs from it.

## Current State

All paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### Model and storage

- **Enums.** `Abstractions.Access/src/MachineAccessScope.cs` (`AnnotationRead = 0`, `ConfigurationRead`, `DefaultRead`, `AnnotationReadWrite`, `ConfigurationReadWrite`, `DefaultReadWrite`, `SecretsRead = 6`, `SecretsReadWrite = 7`) and `MachineManagementScope.cs` (`ConfigurationTransfer = 0` … `KeyValueManage = 4`). Both carry the Newtonsoft `[JsonConverter(typeof(StringEnumConverter))]`, so Cosmos and the API carry enum names.
- **Expansion.** `Backend.Core/src/Accessing/MachineAccessScopes.cs:6-71`: `Get(preset)` into strings (for example `DefaultReadWrite` into `Default.Read, Default.ReadWrite, Annotation.Read, Annotation.ReadWrite, Configuration.Read, Configuration.ReadWrite`; `SecretsRead` into `Configuration.Read, Configuration.Secrets`), `Get(management)`, the union, `Normalize`, `FindUndefined`.
- **Constants.** `Api.Functions/src/Security/Scopes.cs`: `Default.Read`, `Default.Write = "Default.ReadWrite"`, `Annotation.Write = "Annotation.ReadWrite"`, `Configuration.Write = "Configuration.ReadWrite"`, `Configuration.Secrets`, and the five management strings.
- **Records with a scope.** `MachineAccess` / `IMachineAccess` (`Scope`, `ManagementScopes`), `MachineAccessCreate` (the HTTP body itself: `required Scope`, optional `ManagementScopes`), `IApiKeyHashStore.StoreAsync` and `ApiKeyHashEntry`, `ApiKeyValidationResult`, `CertificateRecord` (positional), `ClientCertificateValidationResult` (a shared certificate passes `default`, that is `AnnotationRead`), `IClientCertificateAuthority.IssueCertificateAsync(…, MachineAccessScope scope, …)` (the parameter is unused).
- **Cosmos.** `Backend.CosmosDb/src/Entities/Access/MachineAccessEntity.cs` (`required Scope`, `ManagementScopes`), `MachineCertificateEntity.cs` (`mcert.{thumbprint}`, its own copy), `ApiKeyHashEntity.cs` (`akh.`, used only by the unregistered `CosmosApiKeyHashStore`), `AccessMappings.ToMachineAccess`, `CosmosAccessService.CreateMachineAccessAsync` (stores the scope **the provider returned**, `:452`), `CosmosMergedApiKeyHashStore` (the registered store; it keeps the hash and the scopes on the machine document), `CosmosCertificateRenewalService` (passes `entity.Scope` to the CA).
- **Serializers.** Writes and queries use the Cosmos SDK's Newtonsoft serializer (enums through the type attribute), point reads use System.Text.Json with `JsonStringEnumConverter` (which enforces C# `required`), the API body uses System.Text.Json with the MVC `JsonOptions` (`Api.Functions/src/Program.cs:52-60`, case-insensitive, integers accepted as enum values).

### Credentials

| Credential | Where the scopes of a request come from today |
|---|---|
| API key | `MachineAuthenticationMiddleware` expands `ApiKeyValidationResult.Scope` + `ManagementScopes` into `roles` claims on every request |
| Machine certificate (alone or with its API key) | The same, from the `mcert.` record, which wins over the API key |
| Shared certificate with an API key | The API key |
| `ClientCredentials` | The provider token: AuthKit `scp` (slugs mapped through `PermissionMap`, subset matching at creation, a partial-coverage warning); Entra `roles` passed through unfiltered (one collapsed runtime role per machine); Keycloak `storyteller_scope` hardcoded at creation |
| `Federated` | The Storyteller token's `scope`; the exchange (`Access.Federation/src/FederatedTokenExchangeService.cs:133`) allows the union of the preset and the management scopes, all of them by default; `FederatedMachineAccess.EnsureAllowedAsync` (`Api.Functions/src/Security/FederatedMachineAccess.cs:49-60`) answers `401` when the token carries anything outside it |

`ApiKeyAuthenticationMiddleware` is not registered (`Program.cs:34-37`) and is dead code.

### Checks

- **`CheckScope(params string[])`** (`Api.Functions/src/Security/HttpRequestDataExtensions.cs:79-97`): any-of over `GetScopes()` (the `scp`, `roles`, `ClaimTypes.Role` and `*/scope` claims, without an `App.` prefix), `401` with a claim dump, skipped under `DEV_AUTH`. About 107 calls in 20 files, for users and machines alike.
- **`CheckAccessAsync`** (`:357-398`): for a machine, the stored document (`401`), the credential policy (`401`), the annotation restriction (`403 AnnotationRestricted`) and, for the 22 management operations, `EnsureManagementScope` (stored **and** carried, `403 AccessDenied`, in every build). The machine branch ignores the role. For a user, the role (`403`), skipped under `DEV_AUTH`.
- **`GetSecretRightsAsync`** (`:134-168`): one bit from `TryCheckScope(Configuration.Secrets)` (true for everybody under `DEV_AUTH`) gives both secret read and write; a user also gets them from `ContributorWithSecrets`.
- **User-only routes** keep machines out only through `CheckScope(User.Impersonation)` (Release only), except `KeyValueAuthorization.CheckAdministratorAsync`, `ViewsHttp.EnsureNotMachine` and `PurgeAnnotation`, which refuse machines in every build. A machine whose credential carries `User.Impersonation` would pass the others; today the federated excess check stops that for Storyteller tokens.
- **View transfer.** `ViewTransferScopes` (`Api.Functions/src/Security/ViewTransferScopes.cs`) checks per-kind OR-lists against the credential, for users too, switched off in Debug by `ViewTransferHttp.ChecksScopes`.
- **`GetViews`** (`V1/ViewsHttp.cs:49`) needs the annotation scopes, so a configuration-only machine cannot list views, although `CheckAccessToProjectMetadataAsync` says every machine of the project reads them.

### Identity providers and user tokens

- **AuthKit.** `Backend.Core/src/Accessing/AuthKitOptions.cs`: `DefaultUserScopes` (default empty) and `PermissionMap` (slug → one or more scopes). `AuthKitMachineAccessService.GetPermissionSlugs` (`Access.AuthKit/src/AuthKitMachineAccessService.cs:103-149`) does subset matching and warns about partial coverage. `AuthKitClaimNormalizer` maps user `permissions` and machine `scope` through the same map. `AuthKitMachineAccessOptionsValidator` does not look at the map. `WorkOsManagementClient` cannot create permissions.
- **Entra ID.** `Access.AzureAd/src/AzureAdMachineAccessService.cs`: role keys `DefaultRead`, `DefaultReadWrite`, `SecretsRead`, `SecretsReadWrite` plus one optional key per management scope; the collapse at `:156`; a missing runtime role is a `500`. `AzureAdMachineAccessOptionsValidator` requires `DefaultRead` and `DefaultReadWrite`. Entra machine access is not wired in `Program.cs`.
- **Keycloak.** `Access.Keycloak/src/KeycloakMachineAccessService.cs`: attribute `42.scope` = the preset name, `storyteller_scope` = the expansion; not wired in `Program.cs`. Keycloak has no users.
- **User tokens.** `OpenApi/OAuthFlows.cs:83-100` advertises eight Entra delegated scopes; `V1/AuthConfigurationHttp.cs:59-63` hands Entra clients `User.Impersonation` and `Default.ReadWrite`; `sform` (`src/Platform/Cli/src/Authentication/EntraIdAuthenticationService.cs:27-29`) falls back to the same two. The admin UI and `sform` request no scopes from AuthKit.
- No script provisions Entra app roles or WorkOS permissions.

### Clients

- **`sform account machine create`** (`src/Platform/Cli/src/Commands/MachineAccess/MachineCreateCommand.cs`): `-r` (never read), `-w` (`DefaultReadWrite`, else `DefaultRead`), `-m|--manage` (management scopes, parsed by `ManagementScopes.cs`). `Describe` prints `Scope` and `CredentialKind` as integers. The list (`MachineListCommand.cs`) has the columns `Id`, `Role` (the preset), `Kind`, `Secret`, `RestrictionTo`, `Management`.
- **Admin UI** (`src/Platform/ui.admin/src/app/`): `components/access/MachineCreateDialog.vue` (a preset `USelect` and a management `UCheckboxGroup`, Administrator-only), `pages/[org]/[project]/machines.vue` (a preset badge plus management badges), `machines/[id].vue` (Scope and Management scopes rows), `utils/machines.ts`, `mock/access-handlers.ts`, the seed in `mock/db.ts`. `useCan('secrets')` is `ContributorWithSecrets`, `useCan('manageAccess')` is `Administrator`.
- **SDKs** generated from `open.api.v0.9.44.json`: `Sdk.NSwag/src/ApiSdk.g.cs` and `sdk.typescript/src/generated/**` (`MachineAccessScope`, `MachineAccessCreateScope`, `MachineManagementScope`).

### Documentation

`docs/Platform/Storyteller/authentication.md` (the user settings, the `MachineAccess` table, AuthKit, Entra, Keycloak, "Management scopes of machines", federation, caveats, 401 and 403), `access.md`, `key-values.md`, `export-import.md`, `annotation-lifecycle.md`, `templating.md`, `definitions.md`, `binding.md`, `inheritance.md`, the public site (`docs/42for.net/platform/admin-ui.md`, `using-the-platform.md`, `configuration.md`, `annotations.md`, `cli/sform.md`) and the READMEs of `ui.admin`, `sdk.typescript` and `Sdk.NSwag`.

## Proposed Changes

### 1. The scope catalogue

1. **One enum.** `Abstractions.Access/src/MachineScope.cs` replaces `MachineAccessScope.cs` and `MachineManagementScope.cs`, with the same Newtonsoft `StringEnumConverter` attribute:

   ```csharp
   [JsonConverter(typeof(StringEnumConverter))]
   public enum MachineScope
   {
       // Runtime: per-document reads and writes.
       AnnotationRead = 0,
       AnnotationWrite = 1,
       ConfigurationRead = 2,
       ConfigurationWrite = 3,
       SecretsRead = 4,
       SecretsWrite = 5,

       // Management: bulk and structural operations, on top of their runtime scope.
       ConfigurationTransfer = 6,
       SchemaWrite = 7,
       TemplateWrite = 8,
       AnnotationManage = 9,
       KeyValueManage = 10,
   }
   ```

2. **Two spellings, one catalogue.**
   - **Enum names** (`AnnotationRead`) in the API JSON, Cosmos, the SDKs, configuration keys (`Auth:AppRoles:AnnotationRead`) and the `sform` output. This is the existing convention of every enum, and it keeps dots out of environment variables.
   - **Scope strings** (`Annotation.Read`) in claims and tokens (`roles`, `scp`, the Storyteller `scope`), Entra app-role values, WorkOS permission maps, `storyteller_scope`, error messages and the docs.

   | `MachineScope` | Scope string | Group |
   |---|---|---|
   | `AnnotationRead` | `Annotation.Read` | Runtime |
   | `AnnotationWrite` | `Annotation.Write` | Runtime |
   | `ConfigurationRead` | `Configuration.Read` | Runtime |
   | `ConfigurationWrite` | `Configuration.Write` | Runtime |
   | `SecretsRead` | `Secrets.Read` | Runtime |
   | `SecretsWrite` | `Secrets.Write` | Runtime |
   | `ConfigurationTransfer` | `Configuration.Transfer` | Management |
   | `SchemaWrite` | `Schema.Write` | Management |
   | `TemplateWrite` | `Template.Write` | Management |
   | `AnnotationManage` | `Annotation.Manage` | Management |
   | `KeyValueManage` | `KeyValue.Manage` | Management |

   - `X.ReadWrite` becomes `X.Write`. `Configuration.Secrets` splits into `Secrets.Read` and `Secrets.Write`.
   - `Default.Read`, `Default.ReadWrite`, `Configuration.Secrets`, `MachineAccessScope`, `MachineManagementScope`, `MachineAccess.Scope` and `MachineAccess.ManagementScopes` are removed.
   - `User.Impersonation` is not a machine scope and is not in the enum.

3. **The catalogue helper.** `Backend.Core/src/Accessing/MachineScopes.cs` replaces `MachineAccessScopes.cs`:
   - `ToScopeString(MachineScope)` (the table above; `null` for an undefined value) and `TryParseScopeString(string, out MachineScope)` (ordinal, exact);
   - `Runtime`, `Management` and `All`, each in enum order; `IsRuntime`, `IsManagement`, `IsSecrets`;
   - `Normalize(IEnumerable<MachineScope>?)`: the defined values, distinct, in enum order; an empty list for none;
   - `WithImpliedReads(IEnumerable<MachineScope>?)`: `Normalize`, plus `X.Read` for every `X.Write` of Annotation, Configuration and Secrets;
   - `FindUndefined(IEnumerable<MachineScope>?)`: the first undefined value (a JSON integer such as `99`), or `null`;
   - `ToScopeStrings(IEnumerable<MachineScope>)`.
4. **Write implies read only at creation.** `WithImpliedReads` runs once, when a machine is created. The stored list holds both, so every check needs one scope, and nothing expands at request time. `Secrets.*` imply no `Configuration.*`, and management scopes imply nothing.
5. **The API constants.** `Api.Functions/src/Security/Scopes.cs` keeps `User.Impersonation`, drops `Default` and `Configuration.Secrets`, sets `Annotation.Write = "Annotation.Write"` and `Configuration.Write = "Configuration.Write"`, and gets `Secrets.Read` / `Secrets.Write`. They stay for the `const` OpenAPI descriptions. A test pins all 11 to `MachineScopes.ToScopeString`.

### 2. The model

1. **`MachineAccess` and `IMachineAccess`**: `required IReadOnlyList<MachineScope> Scopes` replaces `Scope` and `ManagementScopes`. `required` makes the compiler find every construction site.
2. **`MachineAccessCreate`** (the HTTP body): `IReadOnlyList<MachineScope>? Scopes` replaces `Scope` and `ManagementScopes`. It is not C# `required`, so a missing list reaches the handler and gets the clear `400` of section 3 instead of "The request body is not a machine (at $)".
3. **Credential records** carry one list:
   - `IApiKeyHashStore.StoreAsync(organization, project, machineAccessId, hashedSecret, IReadOnlyList<MachineScope> scopes)` and `ApiKeyHashEntry(HashedSecret, Scopes, CredentialKind?)`;
   - `ApiKeyValidationResult(Organization, Project, MachineAccessId, Scopes, CredentialKind?)`;
   - `CertificateRecord` and `ClientCertificateValidationResult`: `Scopes` in place of `Scope` and `ManagementScopes`; a shared certificate result has an empty list;
   - `IClientCertificateAuthority.IssueCertificateAsync` loses its unused `scope` parameter.
4. **Cosmos entities.** `MachineAccessEntity.Scopes` and `MachineCertificateEntity.Scopes` (`List<MachineScope>?`) replace `Scope` and `ManagementScopes`.
   - **Not `required`.** A document written before this change has no `Scopes`; it reads as a machine with no scopes instead of throwing on the System.Text.Json point read of every machine call.
   - `AccessMappings.ToMachineAccess` and `MachineCertificateEntity.ToRecord` map `null` to an empty list.
   - The old `Scope` and `ManagementScopes` members are dropped; nothing reads them.
5. **Dead code is deleted** rather than ported: `Api.Functions/src/Security/ApiKeyAuthenticationMiddleware.cs`, `Backend.CosmosDb/src/Accessing/CosmosApiKeyHashStore.cs` and `Entities/Access/ApiKeyHashEntity.cs`.

### 3. Creating a machine

`AccessHttp.PostMachines`, after the user check, the Contributor check, the body and the annotation key:

1. **An undefined value** is `400` "The machine scope 99 doesn't exist.". An unknown name already fails to read the body: "The request body is not a machine (at $.Scopes[0]).". Scope strings such as `"Annotation.Read"` are not enum names and fail the same way; only `sform` accepts them (section 9).
2. **The list is completed**: `MachineScopes.WithImpliedReads`.
3. **At least one runtime scope.** Otherwise `400` "A machine needs at least one runtime scope: AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead or SecretsWrite.". This covers a missing list, `[]` and a management-only list; a management scope grants nothing without the runtime scope of its operation.
4. **Who may grant what** (section 4): a management scope needs `Administrator`, a secrets scope `ContributorWithSecrets`. Both are user role checks (`403 AccessDenied` "No Administrator access to '{organization}.{project}'.").
5. **The create path keeps the list.** The router (`PolicyAwareMachineAccessService`) passes the model on with `model with`, and every service returns exactly the requested `Scopes`: API key, certificate, federated, AuthKit, Entra ID (no collapse any more), Keycloak and the test doubles. `CosmosAccessService.CreateMachineAccessAsync` stores the returned list, which therefore equals the request.
6. **The response** carries `Scopes` as enum names, in enum order.

### 4. Who manages a machine

The creator of a machine can use its credentials, so a machine's scopes may not exceed what its creator may do or grant:

| Stored or requested scopes | Create, reset (`PutMachine`), replace the federation rules (`PutMachineFederationRules`) |
|---|---|
| Runtime scopes without secrets | `Contributor` |
| Any `Secrets.*` | `ContributorWithSecrets` |
| Any management scope | `Administrator` |

- `AccessHttp.EnsureAdministratorForManagementScopesAsync` becomes `EnsureRoleForMachineScopesAsync`: one point read of the stored machine, then the highest role its `Scopes` need. A missing machine keeps each route's own answer. `FederationHttp` calls it as before.
- Delete, get and list stay at `Contributor`.
- These are user role checks, so `DEV_AUTH` skips them.
- The secrets row is new: today a Contributor without secret rights can create a `SecretsRead` machine and read secrets through it. It closes the same kind of gap that 424-57 closed for management scopes.

### 5. Credentials carry the stored list

1. **API keys.** `ApiKeyMachineAccessService` stores `model.Scopes` on create, stores `existing.Scopes` again on reset, and returns `entry.Scopes` in the validation result. `CosmosMergedApiKeyHashStore` writes and reads `Scopes` on the machine document.
2. **Certificates.** `CertificateMachineAccessService` puts `model.Scopes` into both records it builds and into a certificate-only result; the `CertificateAndApiKey` result keeps the API key half through `existingAccess with {…}`. `ClientCertificateValidator` returns the record's `Scopes` for a machine certificate and an empty list for a shared one. `CosmosCertificateRenewalService` no longer passes a scope to the CA.
3. **The authentication middleware.** `MachineAuthenticationMiddleware` takes `Scopes` from the same result as today (the certificate record for a machine certificate, the API key otherwise) and emits one `roles` claim per scope string. A credential with no scopes is `401`, as a preset without runtime roles is today. `MachineScopeClaims` becomes `GetRoleClaims(IEnumerable<MachineScope>)`.
4. **Federated machines.** `FederatedMachineAccessService` returns `model.Scopes`. `FederatedTokenExchangeService` allows the scope strings of the stored `Scopes`; a request without `scope` gets all of them; a requested scope outside them stays `invalid_scope`. The old strings (`Default.Read`, `Annotation.ReadWrite`, `Configuration.Secrets`) are simply unknown.
5. **`FederatedMachineAccess.EnsureAllowedAsync`** keeps the project, existence and kind checks and **loses the excess check**: the check of section 7.1 replaces it. A carried scope that is not stored grants nothing, and a machine never passes a user-only route (section 7.4), whatever its token carries.

### 6. Identity providers: one permission or role per scope

A provider grants exactly the requested scopes, 1:1, or refuses the creation with `MachineAccessNotSupportedException` (`400`) **before** it creates anything.

#### 6.1 AuthKit

1. **`PermissionMap` stays slug → scope string**, one scope per slug. It also serves user tokens, where a mapped permission can give `User.Impersonation` (the role-driven mode).
2. **Startup validation.** `AuthKitMachineAccessOptionsValidator` fails when a value is not exactly one known scope string (the 11 machine scope strings or `User.Impersonation`), naming the slug, or when two slugs map the same machine scope. A stale value such as `Default.ReadWrite` or `"Annotation.Read Configuration.Read"` fails the start.
3. **`GetPermissionSlugs(IReadOnlyList<MachineScope>)`**: for each scope the one slug that maps its string. A scope without a slug throws "No WorkOS permission in Auth:AuthKit:PermissionMap maps to the scope Secrets.Write." before WorkOS is called. The subset matching, the "no runtime slug" rule and the partial-coverage warning are removed.
4. **The description** of the application becomes `organization=…|project=…|scopes=AnnotationRead,ConfigurationRead|annotation=…`.
5. **`AuthKitClaimNormalizer`** does not change: a machine token's slugs become their scope strings in `scp`; a user token gets `DefaultUserScopes` plus its mapped permissions. Only `User.Impersonation` matters for users now.
6. **Recommended slugs** (docs and script): `storyteller:annotation-read`, `storyteller:annotation-write`, `storyteller:configuration-read`, `storyteller:configuration-write`, `storyteller:secrets-read`, `storyteller:secrets-write`, `storyteller:configuration-transfer`, `storyteller:schema-write`, `storyteller:template-write`, `storyteller:annotation-manage`, `storyteller:keyvalue-manage`.

#### 6.2 Entra ID

1. **One app role per scope.** The key is `Auth:AppRoles:<EnumName>` (`AnnotationRead` … `KeyValueManage`); the role value is the scope string (an `App.` prefix stays allowed); allowed member type `Application`. `AzureAdMachineAccessService.GetRoleKey(MachineScope)` returns the key for the service and the validator.
2. **Startup validation.** `AzureAdMachineAccessOptionsValidator` requires `MachineAuth:AzureAd:TenantId`, `Auth:TenantId` and `Auth:ClientId` as today. Every role key that is set must be a GUID, and no two keys may share one. The removed keys `DefaultRead`, `DefaultReadWrite` and `SecretsReadWrite` fail the start with a message naming the new keys. No role key is required at startup; creation checks the requested ones.
3. **Before any Graph call**, every requested scope needs its role: "Machines with the scope Annotation.Write need the app role Auth:AppRoles:AnnotationWrite, which this deployment does not configure." (`MachineAccessNotSupportedException`, `400`). This replaces the `500` of a missing runtime role and the `ProtectionNotConfigured` of a missing secrets role.
4. **The application** lists one `ResourceAccess` per role; admin consent posts one `AppRoleAssignment` per scope, in enum order. `Notes` become `organization=…|project=…|scopes=…|annotation=…`.
5. **The collapse is gone.** The result's `Scopes` equal the requested and assigned set.

#### 6.3 Keycloak

- `storyteller_scope` holds the space-joined scope strings of `Scopes`.
- The attribute `42.scope` becomes `42.scopes`, the comma-joined enum names, and the description `scopes=…`.
- `KeycloakMachineTokenValidator.Normalize` does not change.

#### 6.4 The provisioning script

`src/Platform/Storyteller/scripts/Register-MachineScopes.ps1` (PowerShell 7, as `init.ps1`), idempotent, never deletes anything, supports `-WhatIf`:

- **`-EntraApplicationId <appId>`** (Azure CLI signed in): reads the API application with `az ad app show`, adds the missing app roles (value = scope string, a new GUID, `allowedMemberTypes: Application`, enabled), sends one `PATCH /applications/{id}` through `az rest` with the existing roles preserved (Graph replaces the whole collection), and prints the `Auth__AppRoles__<EnumName>=<id>` settings.
- **`-WorkOsApiKey <key>`** (or `WORKOS_API_KEY`): lists `GET https://api.workos.com/authorization/permissions` (paged), creates the missing slugs of section 6.1 with `POST /authorization/permissions` (`slug`, `name`, `description`), and prints the `Auth__AuthKit__PermissionMap__storyteller:<slug>=<scope string>` settings.
- Both parameter sets can run in one call. The script is documented in `authentication.md`.

### 7. Enforcement in the API

#### 7.1 The check

The check stays in the machine branch of `HttpRequestDataExtensions.CheckAccessAsync`, where the stored machine is already read on every call.

1. **The helpers take their scopes.** `CheckScope` and `TryCheckScope` are removed. The data helpers name their role and their machine scopes:

   ```csharp
   Task CheckAccessToProjectAsync(this HttpRequestData request, IAccessService accessService, string organization, string project,
       AccountRole minimalRole, MachineScope scope, params MachineScope[] moreScopes);
   Task CheckAccessToAnnotationAsync(…, string annotationKey, AccountRole minimalRole, MachineScope scope, params MachineScope[] moreScopes);
   Task<MachineAnnotationRestriction> CheckAccessToAnnotationsAsync(…, IEnumerable<string?> annotationKeys, AccountRole minimalRole, MachineScope scope, params MachineScope[] moreScopes);
   Task CheckAccessToProjectMetadataAsync(…, AccountRole minimalRole, MachineScope scope, params MachineScope[] orScopes);
   ```

   - The first three need **all** their scopes, checked in the order given: the runtime scope first, then the management scope.
   - `CheckAccessToProjectMetadataAsync` needs **any one** of its scopes (section 7.6).
   - The signature requires at least one scope, so no machine route can forget it.
2. **The order for a machine:**
   1. the machine document (`401`); a federated machine gets its project and kind check (`401`);
   2. the credential policy (`401`);
   3. the reach of the annotation restriction (`403 AnnotationRestricted`);
   4. **each required scope** (`403 AccessDenied`).
3. **Stored and carried.** For each scope, both must hold:
   1. **Stored**: `machine.Scopes` contains it. Otherwise "This operation needs the scope Annotation.Write, which the machine was not created with.".
   2. **Carried**: `request.GetScopes()` contains its scope string. Otherwise "This operation needs the scope Annotation.Write, which the credentials of the machine do not carry.". This is a federated token that asked for a subset, or a provider token without the permission or role.

   The any-of form says "one of the scopes Annotation.Read or Configuration.Read". The machine document is the source of truth and the token can only narrow it: an Entra app role or a WorkOS permission added outside Storyteller grants nothing.
4. **`403`, not `401`.** A missing runtime scope used to be `401` from `CheckScope`. The machine is authenticated, so it is now `403 AccessDenied`, like a missing management scope and like a user without the role ("clients do not sign in again for nothing").
5. **Every build.** The machine branch runs in Debug too. Under `DEV_AUTH`, only the user checks of 7.2 are skipped.
6. **The stored machine is cached** on the function context (`FunctionContextItemKeys.StoredMachine`), so the secret rights (7.3) and the view transfer (7.5) reuse the same point read.

#### 7.2 Users

- **The user branch of `CheckAccessAsync`**: the token must carry `User.Impersonation` (`401` "The token does not act for a user: the scope User.Impersonation is missing."), then the role on the access point (`403`). Data scopes in a user token are ignored.
- **Under `DEV_AUTH`** both are skipped, as `CheckScope` and the role check are today. Requests without an identity are still served in Debug and stamped `System`.

#### 7.3 Secret rights

`GetSecretRightsAsync` splits read and write:

- **A machine** gets `CanReadSecrets` from `Secrets.Read` and `CanWriteSecrets` from `Secrets.Write`, each stored and carried, in every build. When the project requires mTLS for secrets and the request has none, both are withheld (`X-2S-Secrets-Withheld: mtls-required`), as today for the single bit.
- **A user** gets both from `ContributorWithSecrets` or higher, and from nothing else. Under `DEV_AUTH` the role check is skipped, so a Debug user gets them, as today.
- **`KeyValueAuthorization.EnsureSecretRights`** becomes `EnsureSecretReadRights` (reveal, export with secrets) and `EnsureSecretWriteRights` (protect). Protect checked the read bit in the handler and the write bit in the service; it now checks the write bit in both.
- `KeyValueAuthorization.ReadScopes` and `WriteScopes` are removed.

#### 7.4 User-only routes

One guard replaces `CheckScope(User.Impersonation)`, `ViewsHttp.EnsureNotMachine`, the inline check of `PurgeAnnotation` and the first lines of `CheckAdministratorAsync`:

- **`EnsureUser(string? machineMessage = null)`**: a machine is `403 AccessDenied` in **every** build ("Only users may call this operation; machines cannot.", or the route's existing message); then `User.Impersonation` (`401`), skipped under `DEV_AUTH`.
- **`CheckUserAccessToAsync`, `CheckUserAccessToOrganizationAsync`, `CheckUserAccessToProjectAsync`**: `EnsureUser`, then the role (skipped under `DEV_AUTH`).
- They cover every route of `AccessHttp` (except the anonymous certificate authority), `MembersHttp`, `InvitationsHttp`, the shared-certificate routes of `CertificatesHttp`, the issuer and rules routes of `FederationHttp`, the key/value Administrator operations, `PurgeAnnotation`, `CreateView`, `UpdateView` and `GetViews?discover=true`.
- Machines on these routes now get `403`, not `401`. `PostRenewCertificate` stays machine-only by identity.

#### 7.5 The handlers

One scope per handler: reads need `X.Read`, writes need `X.Write`; a management operation needs its runtime scope and its management scope.

| Handlers | Machine scopes | User role |
|---|---|---|
| Annotation reads: `GetAnnotations`, `GetAnnotation`, `GetDescendants`, `GetResponsibilities`, `GetSubjects`, `GetUsages`, `GetContexts`, `GetExecutions`, `GetUnits`, `GetUnitsOfExecution`, `GetDeletedAnnotations`, `GetDeletedAnnotation`, `FindDeletedAnnotations` (a POST that reads), `GetAnnotationHistory` | `Annotation.Read` | Reader |
| `SetAnnotation` | `Annotation.Write` | Contributor |
| `SetAnnotations`, `SetAnnotationsSimple`, `DeleteAnnotation`, `RestoreAnnotations` | `Annotation.Write` + `Annotation.Manage` | Contributor |
| Configuration reads: `GetConfigurations`, `GetConfiguration`, `GetConfigurationResolved`, the five version and diff reads, `GetConfigurationViewDiff`, `GetConfigurationComposition` | `Configuration.Read` | Reader |
| `SetConfiguration`, `PatchConfiguration`, `DeleteConfiguration` | `Configuration.Write` | Contributor |
| Schema reads: the type, annotation, descendant-type and combined schema, `GetConfigurationSchemas`, the 12 version reads (`PrepareSchemaReadAsync`, once) | `Configuration.Read` | Reader |
| The six schema writes and deletes | `Configuration.Write` + `Schema.Write` | Contributor |
| Template reads (`GetTemplates`, `GetTemplate`, four version reads) | `Configuration.Read` | Reader |
| `SetTemplate`, `PatchTemplate`, `DeleteTemplate` | `Configuration.Write` + `Template.Write` | Contributor |
| `GetBindingCatalog` | `Configuration.Read` | Reader |
| `GetViews` without discovery | `Annotation.Read` **or** `Configuration.Read` (7.6) | Reader |
| Key/value reads: `GetKeyValues`, `GetKeyValue`, the version and diff reads, `GetKeyValuePolicy`, `GetRecipients` | `Configuration.Read` (secret values also `Secrets.Read`, 7.3) | Reader (secret values `ContributorWithSecrets`) |
| `SetKeyValue`, soft `DeleteKeyValue` | `Configuration.Write` (secret levels also `Secrets.Write`) | Contributor (secret levels `ContributorWithSecrets`) |
| `RestoreKeyValues` | `Configuration.Write` + `KeyValue.Manage` (secret keys also `Secrets.Write`) | Contributor |
| `ProtectKeyValues` | `Configuration.Write` + `KeyValue.Manage` + `Secrets.Write` | Contributor + `ContributorWithSecrets` |
| `ExportKeyValues` | `Configuration.Read` + `Configuration.Transfer` (with secrets also `Secrets.Read`) | Reader |
| `ImportKeyValues` | `Configuration.Write` + `Configuration.Transfer` (secret items also `Secrets.Write`) | Contributor |
| View export and its two job reads | `Configuration.Transfer`, then the read scope of each included kind | Administrator |
| View import and its job read | `Configuration.Transfer`, then the write scope of each included kind | Administrator |
| User-only routes (7.4) | refused | as today |

Notes:

- **Writes that return content.** A federated token narrowed to `X.Write` still sees what its writes return (a patched document, created ancestors, schema conflicts). This is accepted: the stored machine has `X.Read` as well, and the token narrowed itself.
- **`SetAnnotation` stays runtime**, as in 424-57.
- **Body before access.** `SetAnnotations`, `FindDeletedAnnotations` and `RestoreAnnotations` keep reading the body before the access check; a refused machine still gets its `403` before any `400` of the body.

#### 7.6 `GetViews`

`GetViews` without discovery accepts any read scope of a machine: `Annotation.Read` or `Configuration.Read` (the any-of helper). A machine created for configuration only can now list the views, as `CheckAccessToProjectMetadataAsync` promises. With `discover=true` it stays a user-only Administrator operation.

#### 7.7 The view transfer

- `ViewTransferHttp.ChecksScopes` is removed, and users need no scopes: their role (`Administrator`) decides.
- **For a machine**, every route first runs the access check with `Configuration.Transfer` (membership, policy, restriction, the transfer scope), then the per-kind check: `ViewTransferScopes.Ensure(granted, include, direction, isJob)` with `granted` = the stored and carried scopes of the machine. One scope per kind and direction:

  | Kind | Export, and reading an export | Import, and reading an import |
  |---|---|---|
  | Annotations | `Annotation.Read` | `Annotation.Write` |
  | Schemas, templates, configurations | `Configuration.Read` | `Configuration.Write` |

  Messages keep their shape, for example "Missing scope for annotations in include: exporting them needs Annotation.Read.".
- **The start order changes**: `Configuration.Transfer` is now checked before the kinds, on every route, because the per-kind check needs the stored machine of the access check. The job reads already had this order.
- The "no credentials" `401` of the transfer routes stays, for every caller.

### 8. OpenAPI, descriptions and the SDKs

1. **The document filter.** `MachineManagementScopeDocumentFilter` becomes `MachineScopeDocumentFilter`: a shared `MachineScope` string-enum schema (11 values, no default), referenced by the items of `MachineAccess.Scopes` and `MachineAccessCreate.Scopes`. Without it, NSwag names an enum per list and Orval and Zod give every item a default.
2. **Descriptions.**
   - The 401 of a data route no longer lists scopes: "missing or invalid credentials, a user token without User.Impersonation, or a machine the project does not know or whose credential kind it does not enable".
   - Every machine-callable operation documents a 403 that names its machine scopes, for example "… A machine needs the scope Annotation.Read (AccessDenied)." or "… the scopes Configuration.Write and Schema.Write …". The constants are built from `Scopes.*` in `Definitions.Descriptions`.
   - User-only routes document the 401 for `User.Impersonation` and a 403 for machines.
   - The transfer 403s list the new per-kind scopes; `PostMachines`, `PutMachine` and `PutMachineFederationRules` document the role rule of section 4; the `400` of `PostMachines` names an undefined scope and a list without a runtime scope.
3. **OAuth flows.** The Entra implicit flow advertises `api://{clientId}/User.Impersonation` only; the client-credentials flow keeps `.default`; AuthKit is unchanged. `AuthConfigurationHttp` hands Entra clients `User.Impersonation` only.
4. **A new snapshot**, after the API code is final, as a separate `chore(platform)` commit: export `open.api.v0.9.N.json` with `OpenApiExportTests` (`STORYTELLER_OPENAPI_EXPORT`), add it to `Sdk.NSwag/` and replace `open.api.v0.9.44.json` in `sdk.typescript/`, regenerate `ApiSdk.g.cs` (`nswag openapi2csclient`, as in `Sdk.NSwag/README.md`) and the TypeScript SDK (`npm run generate`), and bump the input name in `Sdk.NSwag/README.md` and `export-import.md`.

### 9. `sform`

1. **`sform account machine create`:**
   - `-r|--read-only`: `AnnotationRead`, `ConfigurationRead`.
   - `-w|--write`: the same plus `AnnotationWrite`, `ConfigurationWrite`.
   - **`--scope <scope>`** (new, long only because `-s` is taken): any of the 11, repeatable or comma-separated, as the enum name, the scope string or kebab case, in any case (`SecretsRead`, `Secrets.Read`, `secrets-read`).
   - **`-m|--manage <scope>`** stays for the management scopes; a runtime value there is refused with a hint to use `--scope`.
   - The command sends the union. **When the union holds no runtime scope, the `-r` set is added**, so `sform account machine create` alone, or with `-m configuration-transfer`, behaves as before.
   - An unknown value prints the allowed names and returns `ERROR_WRONG_INPUT` (2) before any call. The server adds the implied reads.
2. **The output.** `Describe` prints `Scopes` and `CredentialKind` as names (they print as integers today). The machine list gets the columns `Id`, `Scopes` (the runtime scope strings, where `Role` was), `Kind`, `Secret`, `RestrictionTo`, `Management` (the management scope strings).
3. **The helper** `Commands/MachineAccess/ManagementScopes.cs` becomes `MachineScopes.cs` for all 11 scopes: parsing, scope strings, `IsManagement`.
4. **Sign-in.** The Entra fallback scopes become `api://{ClientId}/User.Impersonation` only. A discovery cached before this change keeps its old scopes until `sform account logout`; the docs say so.
5. **Help.** `sform story export` ends with "A machine needs the read scopes of the included kinds and Configuration.Transfer instead of a role.", `sform story import` with "… the write scopes …".

### 10. Admin UI

All paths are relative to `src/Platform/ui.admin/src/app/`.

1. **`utils/machines.ts`**: `scopeLabels` (scope strings) and `scopeDescriptions` for the 11 scopes, `runtimeScopes` and `managementScopes` in API order, `isManagementScope`, `isSecretsScope`, `withImpliedReads`, `inApiOrder`, and the client-side presets:

   | Preset | Scopes |
   |---|---|
   | Runtime reader | `Annotation.Read`, `Configuration.Read` |
   | Runtime with secrets | `Annotation.Read`, `Configuration.Read`, `Secrets.Read` |
   | CI writer | `Annotation.Read`, `Annotation.Write`, `Configuration.Read`, `Configuration.Write` |
   | Git sync | CI writer + `Configuration.Transfer` |
   | Backup | Runtime reader + `Configuration.Transfer` |
   | Schema deployer | `Configuration.Read`, `Configuration.Write`, `Schema.Write`, `Template.Write` |

   `needsAdministrator` becomes `requiredRoleFor(machine)`, from section 4.
2. **The New machine dialog** (`components/access/MachineCreateDialog.vue`):
   - preset buttons at the top; a preset replaces the picked scopes, which can then be adjusted; nothing about presets is sent or stored;
   - a "Runtime scopes" `UCheckboxGroup` (6) and a "Management scopes" `UCheckboxGroup` (5) with the existing warning;
   - picking `X.Write` also picks `X.Read`; dropping `X.Read` also drops `X.Write`;
   - the management group is disabled below Administrator ("Only Administrators give management scopes."), the secrets items below ContributorWithSecrets ("Only users with secret rights give secrets scopes."); a preset that needs what the user cannot give is disabled;
   - every open starts from Runtime reader; Create needs at least one runtime scope ("Pick at least one runtime scope.");
   - the body sends `Scopes` in API order.
3. **The list** (`pages/[org]/[project]/machines.vue`): one badge per scope, outline for runtime and subtle for management, wrapping. Reset is offered when the user has the role of section 4.
4. **The detail** (`machines/[id].vue`): "Runtime scopes" and "Management scopes" rows, each with descriptions, or "None". Edit rules follows section 4.
5. **The mock API** (`mock/access-handlers.ts`) follows the API: the 400s of section 3, the implied reads, the roles of section 4 for create, reset and the rules `PUT`, stored `Scopes`. The seed (`mock/db.ts`) gets `Scopes` lists for its three machines; the persisted mock database key is bumped so a dev tab does not keep the old shape.
6. **`components/secrets/SecretsPolicyCard.vue`** names `Secrets.Read` instead of `Configuration.Secrets`.
7. **Tests.** Vitest for the dialog (presets, implied reads, gating, the payload) and the mock rules; Playwright for the create flow and axe on the dialog. The four `machines` visual baselines change, because the badges change; they are regenerated with `npm run test:visual:docker -- --update`.

### 11. Existing machines and settings

- **No migration.** A machine document or `mcert.` record without `Scopes` has no scopes: its API key and certificate get `401`, its other credentials `403`. Machines are recreated with their scopes.
- **Settings that change:** `PermissionMap` values (one scope each, new strings), the Entra role keys (`Auth:AppRoles:<EnumName>`), `DefaultUserScopes` (only `User.Impersonation`), and `Auth:AppRoles:DefaultRead` / `DefaultReadWrite` in the tracked `Api.Functions/src/local.settings.json` (replaced by placeholders of the new keys or removed). Users who got secrets through a scope need `ContributorWithSecrets`.
- **Provider objects:** Entra app roles and WorkOS permissions are created by the script of section 6.4; old ones can be deleted by hand. Keycloak clients and WorkOS applications created before keep their old scopes and are recreated.

### 12. Documentation

- **`authentication.md`:** the user settings and the "Scopes for AuthKit users" section (only `User.Impersonation`), the discovery sample, `DEV_AUTH`; the machine creation paragraph and the `MachineAccess` table (`Scopes`); AuthKit, Entra and Keycloak with **one table** (scope, group, Entra role key and value, WorkOS slug) and the script; "Management scopes of machines" becomes "Scopes of machines" (the catalogue, write implies read, the stored-and-carried check, the role rule of section 4, the presets as typical machines, the limits, existing machines); the annotation restriction; federation (the token endpoint and the per-call checks without the excess check); the security notes; the caveats; 401 and 403 with the rule "scopes describe machines, roles describe people".
- **`access.md`:** the rule, the roles table and "Machines have no role".
- **`key-values.md`:** "Secret delivery" and "Who may do what" with single scopes.
- **`export-import.md`:** the per-kind table, the order of the checks, the messages, the OpenAPI quotes, the snapshot name, the CLI.
- **`annotation-lifecycle.md`, `templating.md`, `definitions.md`, `binding.md`, `inheritance.md`:** the machine scopes of their operations.
- **The public site:** `admin-ui.md` (the New machine dialog), `using-the-platform.md`, `configuration.md`, `annotations.md`, `cli/sform.md` (`-r`, `-w`, `--scope`, `-m`).
- **READMEs:** `ui.admin/README.md` (e2e and axe lists), `sdk.typescript/README.md` (the `scope` comment), `Sdk.NSwag/README.md` (the snapshot name).
- Anchors linked from other files keep working, or every link is updated.

### 13. Tests

- **`Backend.Core/test`:** `MachineScopesTests` (strings 1:1 and back, groups, `Normalize`, `WithImpliedReads`, `FindUndefined`, the enum names and values pinned as the wire format).
- **`Api.Functions/test`, run in Debug and in Release:**
  - the shared doubles first: `KeyValueTestDoubles.RoleAccess` stores the six runtime scopes plus the given management scopes (or an explicit list), `KeyValueRequests` users carry only `User.Impersonation`, one machine request builder carries scope strings;
  - `MachineAccessCheckTests`: for each helper, a scope stored and carried passes, not stored or not carried is `403` with its message, the restriction comes first, an unknown machine is `401`, a user needs `User.Impersonation` and the role but no data scope;
  - table-driven theories over every handler (`MachineRestrictionHttpTests.Handlers` and the management tables): refused without the runtime scope, refused without the management scope, passes with exactly the scopes of the table in 7.5;
  - `GetViews` for a configuration-only machine; secret read and write split (`SecretRightsTests`, `KeyValuesHttpTests`); a user with a secrets scope but without the role gets nothing; the user-only guard for machines in both builds; the view transfer per kind for machines only; `MachineHttpTests` for the 400s, the implied reads and the role rule of section 4;
  - federated machines: the excess check is gone, a carried scope not stored is `403`, `User.Impersonation` in a federated token does not open a user-only route;
  - `MachineAuthenticationMiddlewareTests`, `OAuthFlowsTests`, `AuthConfigurationHttpTests`, `MachineScopeDocumentFilterTests` and `OpenApiExportTests` (the shared enum, the 403 of every machine-callable operation names its scopes);
  - the `DevAuth` early returns that guarded machine checks are removed;
  - the nine Release-only failures that predate this change are test-double defects in files this change rewrites; they are fixed so that a Release run is green.
- **`Access.Certificates/test` and `testint`, `Access.Federation/test`, `Backend.CosmosDb/test`:** the records, the router, the validator, the exchange subset and default, and the Cosmos round trip.
- **`Access.AuthKit/test`, `Access.AzureAd/test`, `Access.Keycloak/test`:** one permission, role or claim per scope; a missing mapping fails before the provider is called; the startup validation of the map and of the role keys; no collapse.
- **`Cli/test`:** `-r`, `-w`, `--scope` in every spelling, `-m`, the union and the default, the output and the list columns, the help, the Entra fallback.
- **`sdk.typescript`:** the federation test strings.
- **`ui.admin`:** section 10.7.
- **The script:** parsed and run with `-WhatIf` against no tenant (syntax only); there is no test tenant.

### 14. Phases and commits

1. `docs(platform): plan the flat set of machine scopes` (this spec)
2. **Phase 1**, `feat(platform): replace the machine scope presets with a flat scope set`: sections 1 to 8.3 and their tests. The server is one atomic change: every project of the model, the credentials, the providers and the API compiles against the new model together.
3. **Phase 2**, `feat(platform): script the Entra ID app roles and WorkOS permissions of the machine scopes`: section 6.4.
4. **Phase 3**, `chore(platform): regenerate the SDKs from OpenAPI 0.9.N`: section 8.4.
5. **Phase 4**, `feat(platform): pick single machine scopes with sform account machine create`: section 9.
6. **Phase 5**, `feat(platform): give the scopes of a machine in two groups with presets in the admin UI`: section 10.
7. **Phase 6**, `docs(platform): document the flat set of machine scopes`: section 12.
8. `docs(platform): review the flat set of machine scopes`: one review of all phases.

### 15. Out of scope

- **Editing the scopes of an existing machine.** Recreate the machine.
- **The API allow-list of 424-58.** It should use these flat scopes.
- **Defects found in 424-57, not changed here** (the code is touched, the behaviour is not):
  - an API key reset writes back the old hash (`CosmosAccessService.ResetMachineAccessAsync` upserts its stale copy);
  - certificate renewal writes no `mcert.` record;
  - Entra ID creation has no compensation after a failed Graph call (one assignment per scope adds calls, and the provider is not wired in `Program.cs`).
- **Debug decoding of Keycloak machine tokens** (`BearerAuthenticationMiddleware` decodes foreign tokens with the user normalizer under `DEV_AUTH`). Keycloak is not wired.
- **The legacy `Api.Web` project**, which has its own scopes.

### 16. Differences from the CodeRabbit coding plan

The plan's choices that this spec keeps:

- one enum serialized by name, dotted strings in tokens and docs, one catalogue;
- `Normalize` and write implies read at creation; the stored list on every credential record;
- the stored-and-carried check in `CheckAccessAsync`, in every build, with the federated excess check removed;
- users by role only, `User.Impersonation` as the user marker, user secret rights from `ContributorWithSecrets` only, `DEV_AUTH` skipping only user checks;
- the 1:1 providers and a PowerShell script in `src/Platform/Storyteller/scripts/`;
- the machine cached on the function context;
- `-r`, `-w` and a repeatable `--scope`, with `-r` as the default; the six client-side presets.

It differs in these points:

1. **424-57 is merged.** The plan was written for a base without management scopes; this spec replaces the existing `ManagementScopes` and keeps the 424-57 allocation of the management scopes, which the issue confirms ("as in 424-57"): `Configuration.Transfer` covers the view and key/value transfers, `Annotation.Manage` bulk create, soft delete and restore, `KeyValue.Manage` protect and bulk restore.
2. **`GetViews` accepts `Annotation.Read` or `Configuration.Read`**, as the issue names them. The plan adds `Secrets.Read`; under the flat model a machine that reads secrets also needs `Configuration.Read` to read key/values at all.
3. **Secrets scopes need `ContributorWithSecrets`** to be given, as management scopes need `Administrator`. The plan and the issue do not mention it; without it a Contributor without secret rights reads secrets through a machine.
4. **A missing machine scope is `403`, not `401`.**
5. **At least one runtime scope** is required at creation; the plan refuses only an empty or unknown set.
6. **`-m` stays** for management scopes next to `--scope`; the plan drops it because its base had none.
7. **Entra role keys are optional at startup** and checked per requested scope at creation, like the AuthKit map; the plan requires all 11 at startup. The removed keys fail the start.
8. **The view transfer checks `Configuration.Transfer` before the kinds**, on every route.
9. **The nine Release-only test failures** are fixed, because their files are rewritten anyway.
