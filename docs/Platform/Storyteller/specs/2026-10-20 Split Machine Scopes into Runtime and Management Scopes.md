# Split Machine Scopes into Runtime and Management Scopes

Linear: [424-57 Split machine scopes into runtime and management scopes](https://linear.app/424fun/issue/424-57/split-machine-scopes-into-runtime-and-management-scopes)

## Problem

Machines have no role, only a `MachineAccessScope` preset. For a machine, the project check (`CheckAccessAsync`) reads the machine document, checks the credential policy and the annotation restriction, and returns before the role check. The role argument of a handler therefore means nothing for a machine. A machine with a write scope acts as a Contributor of its project, and for the export and import of a view even as an Administrator. `DefaultReadWrite` opens every write a machine can reach, and `DefaultRead` already exports a whole view.

A runtime or CI machine therefore gets bulk and structural operations it rarely needs:

- **The import of a view** rewrites many documents, possibly a whole view, in one call.
- **The export of a view and the export of key/values** move data out in bulk. A key/value export with secret rights returns every secret at once, while normal reads go key by key and are audited per key.
- **Schema writes and deletes** govern the validation of every configuration of their type in the project.
- **Template writes and deletes** are merged into every configuration of their type in the view.
- **Annotation structure changes**: a soft delete cascades to the descendants, and restore and bulk create touch many annotations.
- **Key/value protect and bulk restore** change many keys at once.

The issue asks for two groups of scopes:

- **runtime scopes**, the per-document reads and writes, which the presets keep;
- **management scopes**, granted explicitly and never part of the defaults.

A sync pipeline, as in 424-15 (configuration as a git repository), stays possible: the Administrator grants it the transfer scope on purpose.

The inputs to this spec are:

- the issue;
- the analysis comment on the issue: a scope catalogue and a matrix of all 132 HTTP functions, read from `main` at `896299db`, which `main` has not changed in these areas since;
- the CodeRabbit coding plan attached to the issue, used as a second opinion.

Section 15 lists where this spec differs from that plan.

## Current State

All paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### Scope model

- **The preset.** `Abstractions.Access/src/MachineAccessScope.cs` holds a scalar enum: `AnnotationRead = 0`, `ConfigurationRead`, `DefaultRead`, `AnnotationReadWrite`, `ConfigurationReadWrite`, `DefaultReadWrite`, `SecretsRead = 6` and `SecretsReadWrite = 7`.
  - It carries the Newtonsoft `[JsonConverter(typeof(StringEnumConverter))]`, so Cosmos stores the names and the OpenAPI document shows a string enum.
  - The project has no `ImplicitUsings`.
- **The expansion.** `Backend.Core/src/Accessing/MachineAccessScopes.cs:6-20` turns each preset into scope strings, for example `DefaultReadWrite` into `Default.Read`, `Default.ReadWrite`, `Annotation.Read`, `Annotation.ReadWrite`, `Configuration.Read` and `Configuration.ReadWrite`.
- **The constants.** `Api.Functions/src/Security/Scopes.cs` keeps the constants of these strings. `Scopes.X.Write` holds the string `X.ReadWrite`.
- **The create contract.** `Backend.Core/src/Accessing/Model/MachineAccessCreate.cs` has a `required` scalar `Scope`.
- **The response.** `Abstractions.Access/src/Model/MachineAccess.cs` and `IMachineAccess.cs` hold the stored `Scope`.
- **No edits after creation.** No endpoint changes a machine's scope once it exists:
  - `PutMachine` only rotates the credential;
  - the federation-rules `PUT` replaces only the rules.

### Creating and storing a machine

- **`AccessHttp.PostMachines`** (`Api.Functions/src/V1/AccessHttp.cs:332-369`):
  - It needs `User.Impersonation` and `Contributor`.
  - It reads the body, normalizes `AnnotationKey` and calls `IAccessService.CreateMachineAccessAsync`.
  - It answers `MachineAccessNotSupportedException` with `400` and no `ErrorCode`.
- **`PutMachine`** (reset, `371-405`) and **`FederationHttp.PutMachineFederationRules`** (`Api.Functions/src/V1/FederationHttp.cs:251-305`) also need `Contributor`.
- **The router.** `Access.Certificates/src/PolicyAwareMachineAccessService.cs` is the registered `IMachineAccessService`.
  - It copies the model with `model with {…}` (`65-73`), so new members of `MachineAccessCreate` flow through.
  - It dispatches by credential kind:
    - `ApiKeyMachineAccessService` (`Access.ApiKeys`);
    - the helper `CertificateMachineAccessService` (`Access.Certificates`);
    - `IFederatedMachineAccessService` (`Access.Federation`);
    - `IIdentityProviderMachineAccessService`, which is AuthKit, Entra ID or Keycloak; only AuthKit is wired in `Api.Functions/src/Program.cs:128-142`.
  - It returns `result with { CredentialKind = … }`.
- **Storage.** `Backend.CosmosDb/src/Accessing/CosmosAccessService.cs:426-483` builds the stored `MachineAccessEntity` from the **returned** `MachineAccess` (`Scope = machineAccess.Scope`, line 452), not from the model.
- **The API key hash.** The registered store is `CosmosMergedApiKeyHashStore` (`Backend.CosmosDb/src/EntryPoint.cs:99`).
  - It keeps `HashedSecret` and `Scope` on the `MachineAccessEntity` itself and reads them back as an `ApiKeyHashEntry` (`IApiKeyHashStore.cs:18`).
  - `CosmosApiKeyHashStore` and `ApiKeyHashEntity` (`akh.` documents) are not registered.
- **Certificates.** `CertificateMachineAccessService` copies `Scope` into a `CertificateRecord` (`Backend.Core/src/Accessing/Model/CertificateRecord.cs`) at creation.
  - The record is stored as `MachineCertificateEntity` (`mcert.{thumbprint}`).
  - It is read through `CosmosClientCertificateStore`, which caches hits for 5 minutes.

### Claims of each credential kind

| Credential | Where the scopes of the request come from |
|---|---|
| API key | `MachineAuthenticationMiddleware` (`Api.Functions/src/Security/MachineAuthenticationMiddleware.cs:166`, claims at `206-227`): one `roles` claim per string of `MachineScopeClaims.GetRoleClaimsForScope(apiKeyResult.Scope)`; the scope comes from the `ApiKeyHashEntry`, that is the machine document |
| Machine certificate, alone or with an API key | The same middleware, from `certResult.Scope` (lines 126 and 158), that is the `CertificateRecord` |
| Shared certificate with an API key | The API key entry (line 142) |
| `ClientCredentials` | The identity provider's token. AuthKit: the slugs that `GetPermissionSlugs` picked from `PermissionMap` (`Access.AuthKit/src/AuthKitMachineAccessService.cs:100-132`). Entra ID: the values of the one app role assigned at creation (`Access.AzureAd/src/AzureAdMachineAccessService.cs:44-138`), passed through unfiltered. Keycloak: the `storyteller_scope` hardcoded claim mapper (`Access.Keycloak/src/KeycloakMachineAccessService.cs:149-155`) |
| `Federated` | The `scope` of the Storyteller token. The exchange takes it from `MachineAccessScopes.Get(machine.Scope)`, and a request without `scope` gets all of them (`Access.Federation/src/FederatedTokenExchangeService.cs:132-144`). Each call checks it again against the stored machine, and a token scope outside it is `401` (`Api.Functions/src/Security/FederatedMachineAccess.cs:49-58`) |

Two more facts about claims:

- `ApiKeyAuthenticationMiddleware` is not registered (`Program.cs:34-37`).
- `HttpRequestDataExtensions.GetScopes()` (lines 70-77) reads `scp`, `roles`, `ClaimTypes.Role` and `*/scope`, strips an `App.` prefix, and has no allow-list.

### Checks

- **`CheckScope`** (`HttpRequestDataExtensions.cs:79-97`):
  - it passes when any of the listed scopes is present;
  - it throws `SecurityTokenException` (`401`);
  - it is skipped under `DEV_AUTH`, which Debug builds of `Api.Functions` define.
- **The machine branch of `CheckAccessAsync`** (lines 352-386) runs in every build:
  - it reads the stored machine;
  - for a federated machine, `FederatedMachineAccess.EnsureAllowedAsync` reads it and checks the kind and the excess scopes;
  - it applies `MachineCredentialPolicy` and the annotation restriction;
  - it uses only `AnnotationKey` from the stored machine.
- **`ViewTransferScopes.Ensure`** answers a missing per-kind scope with `403 AccessDenied` and names the scope. `ViewTransferHttp.ChecksScopes` switches it off in Debug.
- **Operations that refuse every machine:**
  - `PurgeAnnotation`;
  - `KeyValueAuthorization.CheckAdministratorAsync` (purge, permanent delete, policy, audit, usage);
  - `ViewsHttp.EnsureNotMachine`.

### The operations in question

| Functions | Runtime scope today | User role |
|---|---|---|
| `StartViewExport`, `GetViewExport`, `GetViewExportArchive` (`V1/ViewTransferHttp.cs:72, 107, 135`) | per-kind read scopes | Administrator |
| `StartViewImport`, `GetViewImport` (`179, 232`) | per-kind write scopes | Administrator |
| `ExportKeyValues` (`V1/KeyValuesHttp.cs:518-519`) | read | Reader (+ secret rights with `IncludeSecrets`) |
| `ImportKeyValues` (`568-569`) | write | Contributor |
| `ProtectKeyValues` (`335-336`) | write + secret rights | Contributor |
| `RestoreKeyValues` (`255-256`) | write | Contributor |
| `SetConfigurationSchema`, `DeleteConfigurationSchema`, `SetAnnotationSchema`, `DeleteAnnotationSchema`, `SetDescendantTypeSchema`, `DeleteDescendantTypeSchema` (`V1/ConfigurationSchemaHttp.cs:97-98, 167-168, 248-249, 318-319, 408-409, 487-488`) | Configuration write | Contributor |
| `SetTemplate` (POST and PUT), `PatchTemplate`, `DeleteTemplate` (`V1/TemplateHttp.cs:122-123, 196-197, 272-273`) | Configuration write | Contributor |
| `SetAnnotations`, `SetAnnotationsSimple`, `DeleteAnnotation` (`V1/AnnotationsHttp.cs:409-415, 472-473, 518-519`) | Annotation write | Contributor |
| `RestoreAnnotations` (`V1/AnnotationsHttp.Deleted.cs:139-143`) | Annotation write | Contributor |

`SetAnnotations` and `RestoreAnnotations` read the body before the access check, on purpose: the keys are authorized before any `400` of the body.

### Clients

- **`sform account machine create`** (`src/Platform/Cli/src/Commands/MachineAccess/MachineCreateCommand.cs`):
  - only `DefaultRead` and `DefaultReadWrite` (`-w`) are reachable;
  - the created machine is printed as an anonymous object (lines 166-181);
  - `MachineListCommand` prints a table with the columns `Id`, `Role` (the scope), `Kind`, `Secret` and `RestrictionTo`.
- **The help of `sform story export` and `sform story import`** ends with "A machine needs the scopes of the included kinds instead of a role.". `StorytellerTransferCommandTests` asserts it.
- **The admin UI** (`src/Platform/ui.admin/src/app/`):
  - `components/access/MachineCreateDialog.vue` has a scope select;
  - `pages/[org]/[project]/machines.vue` shows a scope badge, and `machines/[id].vue` the scope with its description (`utils/machines.ts`, `scopeDescriptions`);
  - the mock API (`mock/access-handlers.ts:407-448`) copies only known fields of a created machine.
- **The SDKs** are generated from `Sdk.NSwag/open.api.v0.9.32.json`:
  - `Sdk.NSwag/src/ApiSdk.g.cs`;
  - `sdk.typescript/src/generated/**`.

### Documentation

- `docs/Platform/Storyteller/authentication.md`:
  - the `MachineAccess` table, `Scope` row (line 453);
  - AuthKit `PermissionMap` (467-491);
  - Entra app roles (495);
  - Keycloak (502-521);
  - the annotation restriction (523-539);
  - the token endpoint and Storyteller tokens (682-772);
  - the caveats (979-1035);
  - 401 and 403 (1037-1068).
- `access.md` (roles, lines 44-54).
- `export-import.md` (Access, the errors, the CLI).
- `key-values.md` ("Who may do what").
- `annotation-lifecycle.md` (the API table).
- `templating.md` and `definitions.md` (the access notes). These two name the non-existent scope strings `Configuration.Write` and `Default.Write`.
- The public site: `docs/42for.net/platform/admin-ui.md`, `using-the-platform.md`, `configuration.md`, `annotations.md` and `cli/sform.md`.

## Proposed Changes

### 1. Runtime scopes and management scopes

1. **Runtime scopes** are the eight `MachineAccessScope` presets, unchanged: names, values 0–7 and expansions.
   - They cover per-document reads and writes of annotations, configurations and key/values, and all reads.
   - `DefaultRead` and `DefaultReadWrite` stay runtime presets.
2. **Management scopes** are a separate, additive list on the machine. None of them is part of any preset, and a machine without the list has none.

   | `MachineManagementScope` | Scope string | Operations |
   |---|---|---|
   | `ConfigurationTransfer` | `Configuration.Transfer` | The export and import of a view, including their job reads and the archive download (`StartViewExport`, `GetViewExport`, `GetViewExportArchive`, `StartViewImport`, `GetViewImport`); the export and import of key/values (`ExportKeyValues`, `ImportKeyValues`) |
   | `SchemaWrite` | `Schema.Write` | The six schema writes and deletes: type, annotation and descendant-type schemas |
   | `TemplateWrite` | `Template.Write` | `SetTemplate` (POST and PUT), `PatchTemplate`, `DeleteTemplate` |
   | `AnnotationManage` | `Annotation.Manage` | Bulk create (`SetAnnotations`, `SetAnnotationsSimple`), soft delete (`DeleteAnnotation`), restore (`RestoreAnnotations`) |
   | `KeyValueManage` | `KeyValue.Manage` | Key/value protect (`ProtectKeyValues`) and bulk restore (`RestoreKeyValues`) |

3. **A management scope adds to the runtime scopes and never replaces them.** Each operation still needs its runtime scope, its secret rights and its mTLS rule, and the annotation restriction still applies. For example:
   - an export of a view needs the per-kind read scopes and `Configuration.Transfer`;
   - a key/value protect needs a write scope, the secret rights and `KeyValue.Manage`.
4. **One transfer scope covers both directions.** The per-kind runtime scopes already set the direction: `DefaultRead` plus `Configuration.Transfer` exports and cannot import.
5. **A view import needs no more than the transfer scope.** It writes schemas, templates and annotations through a background job, and that job needs no `Schema.Write`, `Template.Write` or `Annotation.Manage`. The Administrator who grants `Configuration.Transfer` opts in to that.
6. **Users are unchanged.** Their role decides, as before. Management scopes are never added to:
   - the delegated scopes of `OpenApi/OAuthFlows.cs`;
   - the AuthKit `DefaultUserScopes`;
   - any check of a user.
7. **Typical profiles** (documented in `authentication.md`):

   | Machine | Grants |
   |---|---|
   | Runtime app | `DefaultRead`, or `SecretsRead` |
   | CI writer (per-document writes) | `DefaultReadWrite` |
   | Git sync (424-15) | `DefaultReadWrite` + `ConfigurationTransfer` |
   | Backup | `DefaultRead` + `ConfigurationTransfer` |
   | Schema and template deployer | `ConfigurationReadWrite` + `SchemaWrite` + `TemplateWrite` |
   | Annotation provisioning or clean-up | `AnnotationReadWrite` + `AnnotationManage` |
   | Key/value re-protection | `SecretsReadWrite` + `KeyValueManage` |

### 2. The model

1. **The enum.** `Abstractions.Access/src/MachineManagementScope.cs` is new, with the same Newtonsoft `StringEnumConverter` attribute as `MachineAccessScope`:

   ```csharp
   [JsonConverter(typeof(StringEnumConverter))]
   public enum MachineManagementScope
   {
       ConfigurationTransfer = 0,
       SchemaWrite = 1,
       TemplateWrite = 2,
       AnnotationManage = 3,
       KeyValueManage = 4,
   }
   ```

2. **The scope strings.**
   - `Api.Functions/src/Security/Scopes.cs` gets `Scopes.Configuration.Transfer`, `Scopes.Schema.Write`, `Scopes.Template.Write`, `Scopes.Annotation.Manage` and `Scopes.KeyValue.Manage`, with the strings of section 1.
   - `MachineAccessScopes` keeps its own literals, as it does today. A test pins both to the same values.
3. **The expansion.** `Backend.Core/src/Accessing/MachineAccessScopes.cs`:
   - `Get(MachineAccessScope)` does not change.
   - `Get(MachineManagementScope)` returns the one scope string; an undefined value returns `null`.
   - `Get(MachineAccessScope, IEnumerable<MachineManagementScope>?)` returns the union without duplicates: the preset's strings first, then the management strings in enum order. Undefined values add nothing.
   - `Normalize(IEnumerable<MachineManagementScope>?)` returns the defined values, distinct and in ascending enum order, or `null` when nothing is left.
   - `FindUndefined(IEnumerable<MachineManagementScope>?)` returns the first undefined value, or `null`.
4. **The contracts.** `MachineAccess` and `IMachineAccess` get `IReadOnlyList<MachineManagementScope>? ManagementScopes`, and `MachineAccessCreate` gets the same optional member.
   - **Not `required`.** System.Text.Json enforces `required` on request bodies and on Cosmos reads, and existing clients and documents lack the member.
   - **Null means none.** `null` and an empty list both mean no management scope.
   - **The stored form** is `null` for none, or distinct values in ascending enum order. A `null` is left out of the API JSON (`WhenWritingNull`).
5. **Names on the wire.** The API reads enum names in any case, like `Scope`. The scope strings are not enum names, so they are not accepted in the JSON body.

### 3. Creating a machine

`AccessHttp.PostMachines`, after the body is read and the annotation key is normalized:

1. **An undefined value is `400`** with the message "The machine management scope {value:D} doesn't exist.", with no `ErrorCode`, like the other `400`s of the route. The JSON converter accepts integers, so `[99]` reaches this check.
2. **The list is normalized** (`MachineAccessScopes.Normalize`) and set on the model.
3. **A non-empty list needs `Administrator`** on the project (section 4): `CheckAccessToProjectAsync(…, AccountRole.Administrator)`, so `403 AccessDenied` with "No Administrator access to '{organization}.{project}'.". The `Contributor` check at the start of the route stays for machines without management scopes.
4. **The create path keeps the list.**
   - The router passes the model through (`model with`).
   - Each service sets `ManagementScopes` on the `MachineAccess` it returns: API key, certificate, federated, AuthKit, Entra ID, Keycloak, and the test doubles `RecordingIdentityProviderMachineAccessService` and the Moq router of `FederationPersistenceTests`.
   - `CosmosAccessService.CreateMachineAccessAsync` stores `machineAccess.ManagementScopes` on the new `MachineAccessEntity` (`List<MachineManagementScope>?`, optional), and `AccessMappings.ToMachineAccess` reads it back.
   - Reset (`entity with`), the rules `PUT` and certificate renewal keep it through `with`.

### 4. Who manages a machine with management scopes

An Administrator decides on management scopes. A Contributor must not reach them through the credential of an existing machine. So every operation that gives out or redirects the credentials of a machine **with** management scopes needs `Administrator`:

| Operation | Machine without management scopes | Machine with management scopes |
|---|---|---|
| Create (`PostMachines`) | Contributor | Administrator |
| Reset (`PutMachine`): returns a new secret | Contributor | Administrator |
| Replace the federation rules (`PutMachineFederationRules`): decides which workload gets its tokens | Contributor | Administrator |
| Delete, get, list | Contributor | Contributor |

- **Reading the stored machine.** `PutMachine` and `PutMachineFederationRules` read it first with `IAccessService.GetMachineAccessAsync`, which is one point read. A missing machine keeps today's answer of each route.
- **The role check.** When the stored machine has management scopes, they call `CheckAccessToProjectAsync(…, AccountRole.Administrator)`.
- **`DEV_AUTH`.** All three role checks are user role checks, so Debug builds skip them, as every role check today.

### 5. Credentials carry the management scopes

A management scope must be stored on the machine **and** carried by the credential of the request (section 7). So every credential kind carries it.

#### 5.1 API keys

1. **The store.** `IApiKeyHashStore.StoreAsync` gets a trailing optional parameter `IReadOnlyList<MachineManagementScope>? managementScopes = null`, and `ApiKeyHashEntry` gets a trailing optional `ManagementScopes`.
2. **The merged store.** `CosmosMergedApiKeyHashStore` writes the list on update and on the partial entity, and returns it from `GetAsync`.
3. **The unregistered store.** `CosmosApiKeyHashStore` and `ApiKeyHashEntity` get the same member, so the two implementations stay alike.
4. **The service.** `ApiKeyMachineAccessService`:
   - create stores `model.ManagementScopes` and returns them;
   - reset stores `existing.ManagementScopes` again;
   - validation returns them in `ApiKeyValidationResult`, as a new trailing optional parameter.

#### 5.2 Certificates

1. **The records.** `CertificateRecord` and `ClientCertificateValidationResult` get a trailing optional `ManagementScopes`. `MachineCertificateEntity` gets the member and maps it in `ToRecord` and `FromRecord`.
2. **Creation.** `CertificateMachineAccessService` puts `model.ManagementScopes` into both records it builds (`Certificate` and `CertificateAndApiKey`) and into the `MachineAccess` of a certificate-only machine. The `CertificateAndApiKey` result comes from `existingAccess with {…}`, so it keeps what the API key service set.
3. **Validation.** `ClientCertificateValidator` returns the record's management scopes for a machine certificate, and none for a shared certificate: the identity, and with it the scopes, then come from the API key.
4. **Older records get no backfill.** An `mcert.` record without the member belongs to a machine created before this change, which has no management scopes. `CosmosClientCertificateStore.ToRecordAsync` keeps its `CredentialKind` backfill only.

#### 5.3 The authentication middleware

- `MachineAuthenticationMiddleware` takes the management scopes from the same result that supplies `scope` today: the certificate for a machine certificate, the API key for an API key alone or with a shared certificate.
- `MachineScopeClaims.GetRoleClaimsForScope(scope, managementScopes)` returns the union, one `roles` claim per string.
- The `401` for empty runtime roles stays, and depends on the preset only.
- `ApiKeyAuthenticationMiddleware` is not registered and does not change.

#### 5.4 Federated machines

1. **Creation.** `FederatedMachineAccessService` returns `model.ManagementScopes`.
2. **The exchange.** `FederatedTokenExchangeService` builds `machineScopes` from `MachineAccessScopes.Get(machine.Scope, machine.ManagementScopes)`.
   - A request may name management scopes in `scope`, as a subset as before.
   - **A request without `scope` gets all the machine's scopes, management scopes included**, as the token endpoint promises ("None: every scope of the machine").
   - A pipeline that needs `Configuration.Transfer` on some runs only can ask for it on those runs and leave it out on the others. The docs recommend that.
3. **Each call.** `FederatedMachineAccess.EnsureAllowedAsync` checks the excess against the same union. Without that, a token that carries a management scope would get `401` on every call.

#### 5.5 Identity providers

See section 6.

### 6. Identity providers (`ClientCredentials`)

Each provider must put exactly the requested management scopes into the machine's tokens, or refuse the creation **before** it creates anything at the provider. A refusal is a `MachineAccessNotSupportedException`, answered `400`.

#### 6.1 AuthKit

1. `GetPermissionSlugs(scope, managementScopes)` builds `granted` from the union. It still picks every `PermissionMap` slug whose mapped scopes all fall within `granted`.
2. **Every requested management scope must be covered** by a picked slug. Otherwise the call throws, before WorkOS is called, with "No WorkOS permission in Auth:AuthKit:PermissionMap maps to the management scope Configuration.Transfer.".
   - The rule for no slug at all, and the warning when a runtime preset is only partly covered, do not change.
   - A machine without management scopes never gets a management slug, because a slug's scopes must all be granted.
3. **The description** of the application gets `|manage=ConfigurationTransfer,SchemaWrite` when the list is not empty.
4. **The result** carries `ManagementScopes`.
5. **No change** to `AuthKitClaimNormalizer` or to the token validation. A mapped slug becomes its scope string in `scp`.
6. **Example permissions** in the docs: `storyteller:configuration-transfer`, `storyteller:schema-write`, `storyteller:template-write`, `storyteller:annotation-manage` and `storyteller:keyvalue-manage`, each mapped to its scope string.

#### 6.2 Entra ID

1. **Optional role keys.** `Auth:AppRoles:ConfigurationTransfer`, `SchemaWrite`, `TemplateWrite`, `AnnotationManage` and `KeyValueManage` (the enum names) hold the IDs of app roles of the API registration.
   - The role values must be the scope strings (`Configuration.Transfer`, and so on); an `App.` prefix is allowed.
   - `AzureAdMachineAccessOptionsValidator` does not require these keys.
2. **Before anything is created in Graph**, each requested scope needs its key and a GUID value.
   - A missing key throws "Machines with the management scope Configuration.Transfer need the app role Auth:AppRoles:ConfigurationTransfer, which this deployment does not configure.".
   - This is not `ProtectionNotConfigured`, which is the key/value code.
3. **The application** lists the runtime role and one `ResourceAccess` per management role in `RequiredResourceAccess`. Admin consent posts one `AppRoleAssignment` per role: the runtime role first, then the management roles in enum order.
4. **The `Notes`** get `|manage=…` when the list is not empty. The result carries `ManagementScopes` exactly as requested.
5. **Unchanged:**
   - the collapse of `Scope` (line 133);
   - the missing compensation of a failed create, which is outside this issue.

#### 6.3 Keycloak

- The `storyteller_scope` hardcoded claim gets the union of the preset's scopes and the management scopes.
- The description gets `|manage=…` when the list is not empty, and the result carries `ManagementScopes`.
- `KeycloakMachineTokenValidator` rebuilds `scp` from that claim and does not change.

### 7. Enforcement in the API

#### 7.1 The check

The check sits in the machine branch of `HttpRequestDataExtensions.CheckAccessAsync`, where the stored machine is already read on every call.

1. **A new parameter.** `CheckAccessToProjectAsync`, `CheckAccessToAnnotationAsync` and `CheckAccessToAnnotationsAsync` get a trailing optional parameter `MachineManagementScope? managementScope = null`, and pass it to `CheckAccessAsync`. The other wrappers do not get it: the organization access, the access point access and the project metadata guard no management operation.
2. **The order for a machine:**
   1. the machine document (`401` when it is missing; federated machines get their kind and excess check, `401`);
   2. the credential policy (`401`);
   3. the reach of the annotation restriction (`403 AnnotationRestricted`);
   4. **then the management scope** (`403 AccessDenied`).

   So a machine restricted to an annotation still learns first that a whole-project operation is out of its reach, and unknown machines get `401` before any `403`.
3. **Two conditions.** Both must hold, or the call throws `AccessDeniedException` with `ErrorCodes.AccessDenied`:
   1. **Stored.** `machine.ManagementScopes` contains the scope. Otherwise: "This operation needs the management scope Configuration.Transfer, which the machine was not created with.".
   2. **Carried.** `request.GetScopes()` contains its scope string. Otherwise: "This operation needs the management scope Configuration.Transfer, which the credentials of the machine do not carry.". This is a federated token that asked for a subset, or a provider token without the permission or role.

   The machine document is the source of truth, and the token can only narrow it:
   - an Entra ID app role or a WorkOS permission added outside Storyteller grants nothing;
   - a federated token can still be scoped per job.
4. **Every build.** The check runs in Debug builds too, like the other machine checks of `CheckAccessAsync`. Under `DEV_AUTH`, `CheckScope` and the role checks of users are skipped, and machines are checked in full.
5. **Users.** The parameter is ignored for users.
6. **`AccessDenied`, no new error code.** A missing transfer scope already answers `403 AccessDenied` with the scope's name (`ViewTransferScopes`), and a new code would add nothing a client can act on beyond the message.

#### 7.2 The handlers

Each handler passes its scope to the access helper it already calls, with the role it already passes. No `CheckScope`, role, secret right, mTLS rule or restriction changes.

| Handler | Helper call (line today) | Scope |
|---|---|---|
| `ViewTransferHttp.StartViewExport`, `GetViewExport`, `GetViewExportArchive`, `StartViewImport`, `GetViewImport` | `CheckAccessToProjectAsync(…, Administrator)` (72, 107, 135, 179, 232) | `ConfigurationTransfer` |
| `KeyValuesHttp.ExportKeyValues` | `CheckAccessToProjectAsync(…)` (519) | `ConfigurationTransfer` |
| `KeyValuesHttp.ImportKeyValues` | `CheckAccessToProjectAsync(…, Contributor)` (569), outside `UnderMtlsRuleAsync` | `ConfigurationTransfer` |
| `KeyValuesHttp.ProtectKeyValues`, `RestoreKeyValues` | `CheckAccessToProjectAsync(…, Contributor)` (336, 256) | `KeyValueManage` |
| `ConfigurationSchemaHttp.SetConfigurationSchema`, `DeleteConfigurationSchema` | `CheckAccessToProjectAsync(…, Contributor)` (98, 168) | `SchemaWrite` |
| `ConfigurationSchemaHttp.SetAnnotationSchema`, `DeleteAnnotationSchema`, `SetDescendantTypeSchema`, `DeleteDescendantTypeSchema` | `CheckAccessToAnnotationAsync(…, Contributor)` (249, 319, 409, 488) | `SchemaWrite` |
| `TemplateHttp.SetTemplate`, `PatchTemplate`, `DeleteTemplate` | `CheckAccessToProjectAsync(…, Contributor)` (123, 197, 273) | `TemplateWrite` |
| `AnnotationsHttp.SetAnnotations` | `CheckAccessToAnnotationsAsync(…, Contributor)` (415) | `AnnotationManage` |
| `AnnotationsHttp.SetAnnotationsSimple` | `CheckAccessToProjectAsync(…, Contributor)` (473) | `AnnotationManage` |
| `AnnotationsHttp.DeleteAnnotation` | `CheckAccessToAnnotationAsync(…, Contributor)` (519) | `AnnotationManage` |
| `AnnotationsHttp.Deleted.RestoreAnnotations` | `CheckAccessToAnnotationsAsync(…, Contributor)` (143) | `AnnotationManage` |

**The view transfer.** The start routes keep their order: the per-kind scopes (`ViewTransferScopes`), then the access check with the transfer scope. The three job reads keep theirs: the access check with the transfer scope, then the job, then the per-kind scopes of the job.

**Unchanged and runtime:**

- `SetAnnotation` (one annotation, also re-creating it and its ancestors);
- the configuration writes;
- `SetKeyValue` (one key, also changing its protection with secret rights);
- the soft `DeleteKeyValue`;
- every read.

**Unchanged and users only:** `PurgeAnnotation`, the key/value Administrator operations, and the view management of `ViewsHttp`.

### 8. OpenAPI and SDKs

1. **A document filter for the list.** `OpenApi/Filters/MachineManagementScopeDocumentFilter.cs`, registered in `OpenApiConfigurationOptions`:
   - it adds a shared `MachineManagementScope` string enum schema;
   - it points `ManagementScopes.items` of `MachineAccess` and `MachineAccessCreate` at it;
   - it removes the default the generator puts on enum items.

   `MachineCredentialKindDocumentFilter` does the same for `EnabledCredentialKinds`. Without the filter, NSwag names an enum after each list, and Orval and Zod add a default.
2. **403 descriptions** in `Definitions.Descriptions`, built from constants:
   - `ResponseTransferForbiddenExport` and `ResponseTransferForbiddenImport` get the sentence "A machine also needs the management scope Configuration.Transfer (AccessDenied)." after the restriction sentence. The start and the end that `ViewTransferHttpTests` pins stay.
   - A new `ResponseForbiddenManagement…` per scope: the text of `ResponseForbidden`, plus "A machine also needs the management scope {scope} (AccessDenied)." These go as explicit 403 responses on the schema, template and annotation writes, and on `RestoreKeyValues`.
   - The key/value export, import and protect keep `ResponseMtlsRequired`, plus the same sentence for their scope.
3. **The machine routes:**
   - the `400` of `PostMachines` names an undefined management scope;
   - `PostMachines`, `PutMachine` and `PutMachineFederationRules` document a `403` for "a machine with management scopes needs the Administrator role".
4. **A new snapshot.** After the API code is final:
   - export a new `open.api.v0.9.N.json` with `OpenApiExportTests` (`STORYTELLER_OPENAPI_EXPORT`);
   - copy it to `sdk.typescript/` and remove `open.api.v0.9.32.json` there;
   - regenerate `ApiSdk.g.cs` with `nswag openapi2csclient` and the TypeScript SDK with `npm run generate`;
   - update the input name in `Sdk.NSwag/README.md`.

   N is the third part of `info.version` of the export. This is a separate `chore(platform)` commit.

### 9. `sform`

1. **The option.** `sform account machine create -m|--manage <scope>` is new, `MultipleValue`, repeatable or comma-separated.
   - It accepts the enum name, the scope string or kebab case, in any case: `ConfigurationTransfer`, `Configuration.Transfer`, `configuration-transfer`.
   - An unknown value prints the allowed names and returns `ERROR_WRONG_INPUT`.
   - The values are sent as `ManagementScopes`. No option sends no member.
   - The help says: "A management scope to grant: ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage. Repeat it or separate the values with commas. Needs the Administrator role; never part of -r or -w."
2. **The output.**
   - The printed machine gets `ManagementScopes` as enum names.
   - `sform account machine list` gets a last column `Management`, with the scope strings separated by commas. It is last so that the existing columns keep their positions.
3. **Story export and import.** The help of `sform story export` and `sform story import` ends with "A machine needs the scopes of the included kinds and the management scope Configuration.Transfer instead of a role.". The test that pins it follows.
4. **No client-side role check.** The server's `403` message is printed as today (`WriteApiError`, `WriteRefusal`).

### 10. Admin UI

All paths are relative to `src/Platform/ui.admin/src/app/`.

1. **Texts.** `utils/machines.ts` gets:
   - `managementScopeDescriptions: Record<MachineManagementScope, string>`, what each scope opens;
   - `managementScopeLabels`, the scope strings;
   - `managementScopes`, the ordered list.
2. **The new machine dialog.** `components/access/MachineCreateDialog.vue`:
   - a `UCheckboxGroup` "Management scopes" under the scope, empty by default and reset whenever the dialog opens;
   - a warning: "Bulk and structural operations. They are never part of the scope above; give them only to machines that need them.";
   - below Administrator (`useCan('manageAccess')`), the group is disabled with the help "Only Administrators give management scopes.";
   - the body sends `ManagementScopes` only when at least one is picked.
3. **The list.** `pages/[org]/[project]/machines.vue` shows one badge per management scope after the scope badge, in the same cell. A machine without management scopes looks as today, so the visual baselines of the seeded machines do not change.
4. **The detail.** `pages/[org]/[project]/machines/[id].vue` gets a row "Management scopes", with each label and its description, or "None".
5. **The mock API.** `mock/access-handlers.ts` follows the API:
   - create copies `ManagementScopes`, refuses an unknown value with `400`, and needs Administrator when the list is not empty (`403`);
   - reset and the rules `PUT` of a machine with management scopes need Administrator.

   The seed does not change.

### 11. Existing machines

1. **The strict choice.** Existing machines, `DefaultReadWrite` included, have no management scopes and keep runtime access only. Neither a migration nor a toggle grants them anything. This is the issue's security goal: management scopes are never part of the defaults. It also behaves the same for every credential kind.
2. **What they lose:**
   - the export and import of a view, and of key/values;
   - schema and template writes;
   - bulk create, soft delete and restore of annotations;
   - key/value protect and bulk restore.

   Users are unchanged.
3. **The migration**, documented in `authentication.md`:
   1. An Administrator creates a new machine with the management scopes it needs. The `ClientCredentials` provider must map them first.
   2. The pipeline switches to the new machine.
   3. The old machine is deleted.

   No endpoint edits a machine. This "create new, delete old" path is already the documented way to change a scope.

### 12. Documentation

- **`docs/Platform/Storyteller/authentication.md`:**
  - creating a machine and every machine call (412-414);
  - a `ManagementScopes` row in the `MachineAccess` table, and the `Scope` row as runtime presets;
  - the profiles of section 1;
  - AuthKit permissions and coverage;
  - Entra ID role keys;
  - Keycloak `storyteller_scope`;
  - the annotation restriction table;
  - the federation token endpoint (`scope` and the default);
  - the per-call checks of Storyteller tokens;
  - the security notes on who creates machines;
  - the caveats;
  - the `DEV_AUTH` paragraph;
  - the 401 and 403 table and the role summary;
  - the migration of existing machines;
  - the limits:
    - gating bulk create limits the reach of one call, but `SetAnnotation` still creates keys one by one, with missing ancestors, and re-creates deleted ones;
    - `ProtectKeyValues` only moves keys between server-side levels, and `SetKeyValue` with secret rights still changes the level of one key.
- **`access.md`:** the roles table (delete, restore and re-create; export and import) and "Machines have no role".
- **`export-import.md`:** the code map, the error table, the order of the checks, Access, the OpenAPI quotes, the snapshot name, the CLI help and the exit codes.
- **`key-values.md`:** "Who may do what" (export and import, protect and bulk restore split from the single-key rows), the import and export section, and the CLI rights sentence.
- **`annotation-lifecycle.md`:** the API table, re-create, and the export and import sentence.
- **`templating.md` and `definitions.md`:** the machine scope of the writes. Fix `Configuration.Write` and `Default.Write` to `Configuration.ReadWrite` and `Default.ReadWrite` on the same lines.
- **The public site:**
  - `docs/42for.net/platform/admin-ui.md`: the export rule, the Machines row and the New machine paragraph;
  - `using-the-platform.md`: lines 45 and 168;
  - `configuration.md`: line 173;
  - `annotations.md`: lines 166-167;
  - `cli/sform.md`: a note on machine grants in "Sign in from a pipeline".
- **`src/Platform/ui.admin/README.md`:** the e2e list, if e2e tests are added.
- **`sdk.typescript/README.md`:** the `scope` comment of `createFederatedTokenProvider`.
- Specs and earlier reviews are not changed.

### 13. Tests

**`Backend.Core/test`:**

- the expansion and union, with no management scope in any preset;
- `Normalize` and `FindUndefined`;
- the enum names, pinned like `MachineAuthenticationPoliciesTests` pins its names;
- the scope strings equal to the `Scopes` constants. That last test goes in `Api.Functions/test`, where both are visible.

**`Api.Functions/test`, run in Debug and in Release (`-c Release`):**

- **`MachineAccessCheckTests`.** For each helper with the parameter:
  - a machine that has the scope stored and carried passes;
  - a missing stored scope or a missing carried scope is `403 AccessDenied` with the message;
  - the restriction still comes first;
  - a user passes without it.
- **`MachineAccessRulesTests`:**
  - the role claims of the union;
  - federated: a token with a stored management scope passes, and one without it in the store is `401`.
- **`MachineAuthenticationMiddlewareTests`.** The `roles` claims hold the management scopes:
  - from the API key;
  - from the certificate when both are presented;
  - from the API key with a shared certificate;
  - and an older result without the member gives runtime claims only.
- **`MachineHttpTests` (`PostMachines`):**
  - the list is passed through;
  - none is sent when none is given;
  - names are read in any case;
  - duplicates are dropped;
  - an undefined value is `400`;
  - a Contributor gets `403` with a list, in Release.
- **Reset and the rules `PUT`** of a machine with management scopes: a Contributor gets `403`, in Release, and an Administrator passes.
- **Per operation** of section 7.2:
  - a machine with the runtime scopes only gets `403` and no service call;
  - the same machine with the management scope passes;
  - users are unchanged.

  The doubles carry the scope both stored (`RoleAccess`, `ViewTransferHttpTests.ProjectAccess`) and in the claims (`KeyValueRequests.Machine`, `MachineRestrictionHttpTests.MachineRequest`, `ViewTransferHttpTests.Request`). The existing machine tests of these operations get the scope, so they keep testing what they test.
- **Protect without secret rights** is still refused, and still `MtlsRequired` without mTLS.
- **The OpenAPI tests:**
  - the `ManagementScopes` members and the shared enum;
  - the filter;
  - the 403 texts.

**`Access.Federation/test`:**

- a requested management scope is accepted when stored and refused otherwise;
- the default token carries the stored management scopes;
- `FederatedMachineAccessService` returns the list.

**`Access.Certificates/test`:**

- the router passes the list to every kind;
- the certificate records carry it;
- the validator returns it for machine certificates and not for shared ones.

**`Access.Certificates/testint` and `Backend.CosmosDb/test`** (Cosmos emulator or Testcontainers): the round trip of create, get and validate for an API key and a certificate machine, and of the `mcert.` record.

**`Access.AuthKit/test`, `Access.AzureAd/test`, `Access.Keycloak/test`:** for each provider:

- management slugs, roles or claims only when requested;
- none for a machine without the list;
- a missing mapping or role fails before the provider is called.

**`Cli/test`:**

- the option parses every spelling and refuses an unknown one;
- the list is sent, or not sent without the option;
- the list column;
- the transfer help.

**`ui.admin`:**

- Vitest: the dialog sends the picked scopes, sends none by default, and disables the group below Administrator; the mock API rules.
- Playwright: an Owner creates a machine with a management scope and sees it on the detail; axe on the dialog.
- The visual baselines do not change.

### 14. Phases and commits

1. `docs(platform): plan the split of machine scopes into runtime and management scopes` (this spec)
2. **Phase 1**, `feat(platform): give machines management scopes and carry them in every credential`:
   - sections 2, 3, 4 and 5 without 5.5;
   - the API key, certificate and federated paths;
   - their tests.
3. **Phase 2**, `feat(platform): require the management scopes from machines for bulk and structural operations`:
   - section 7;
   - section 8, items 1 to 3;
   - their tests.
4. **Phase 3**, `feat(platform): provision the management scopes of machines in AuthKit, Entra ID and Keycloak`: section 6 and its tests.
5. **Phase 4**, `chore(platform): regenerate the SDKs from OpenAPI 0.9.N`: section 8, item 4.
6. **Phase 5**, `feat(platform): grant management scopes with sform account machine create`: section 9.
7. **Phase 6**, `feat(platform): give and show the management scopes of machines in the admin UI`: section 10.
8. **Phase 7**, `docs(platform): document the management scopes of machines`: section 12.
9. `docs(platform): review the split of machine scopes into runtime and management scopes`: one review of all phases.

### 15. Differences from the CodeRabbit coding plan

The plan's choices that this spec keeps:

- the additive `ManagementScopes` list with presets 0–7 untouched;
- a `403` naming the scope, for machines only;
- strict handling of existing machines;
- the transfer scope alone covers an import with schemas;
- a missing AuthKit mapping fails the creation;
- the dedicated `KeyValue.Manage` for protect.

It differs in these points:

1. **`Template.Write` is a fifth management scope.** A template is merged into every configuration of its type in the view, as far-reaching as a type schema. The analysis on the issue recommends a grant for it, and the plan, like the issue text, keeps templates runtime.
2. **`RestoreKeyValues` needs `KeyValue.Manage`.** It is the key/value counterpart of `RestoreAnnotations`, which is in `Annotation.Manage`. The plan leaves it runtime. A single key/value soft delete stays runtime: it touches one key, while an annotation delete cascades.
3. **The check reads the stored machine and the token.** The plan checks only the token's scopes, in a helper called before the access check and skipped under `DEV_AUTH`. This spec checks inside the machine branch of `CheckAccessAsync`:
   - Both conditions must hold, so a provider-side permission or role added outside Storyteller grants nothing. Entra ID passes its `roles` claims through unfiltered.
   - It runs after the membership, the policy and the restriction, so a non-member machine gets `401`, not `403`.
   - It runs in every build, like the other machine checks. Debug tests therefore cover it.
4. **The federated paths are in scope.** The plan does not mention them: `FederatedTokenExchangeService` and `FederatedMachineAccess` use the union (section 5.4). Without that, the exchange would answer `invalid_scope` for a management scope, and every call of such a token would get `401`.
5. **Granting needs Administrator.** The plan lets a Contributor create a machine with any grant, and reset it or change its federation rules. That would hand the management operations, the transfer above all, to a Contributor who otherwise needs `Administrator` (424-66). Section 4 closes it.
6. **Entra ID refuses with `MachineAccessNotSupportedException`**, not with `ProtectionNotConfigured`, which is the key/value error code. It refuses before any Graph call, so no application is left behind.
7. **No new `ErrorCode`.** `AccessDenied` is kept, as for the missing scope of a transfer.

### Out of scope

- **Editing the management scopes of an existing machine.** Recreate the machine (section 11).
- **The API allow-list of 424-58.** That issue's list of what machines cannot call is wrong: the export and import of a view and of key/values are open to machines today. In its allow-list, these functions should be machine-allowed **with** `Configuration.Transfer`, not users only.
- **Findings from the analysis, for follow-ups:**
  - `GetViews` needs the Annotation scopes, so `ConfigurationRead`, `ConfigurationReadWrite` and the secrets presets cannot list the views.
  - The secrets presets carry no `Annotation.*`. `Configuration.Secrets` could become an additive grant like the management scopes.
- **Defects found while mapping the code, not changed here:**
  - **Reset of an API key.** `CosmosAccessService.ResetMachineAccessAsync` reads the machine document before the router writes the new `HashedSecret` onto it through `CosmosMergedApiKeyHashStore`. It then upserts its stale copy, so the old hash comes back: the new key fails and the old key keeps working. The only reset test does not validate the key.
  - **Certificate renewal.** `CosmosCertificateRenewalService` rewrites only the machine document and stores no new `mcert.` record, while `ClientCertificateValidator` pins against the newest record.
  - **Entra ID cleanup.** `AzureAdMachineAccessService.CreateMachineAccessAsync` has no compensation when a later Graph call fails.
  - **The legacy `Api.Web` controllers** (`Api.Web/src/Controllers/AnnotationsController.cs`) expose a bulk create guarded by `Annotation.Write` only. The Functions checks do not cover that project.
