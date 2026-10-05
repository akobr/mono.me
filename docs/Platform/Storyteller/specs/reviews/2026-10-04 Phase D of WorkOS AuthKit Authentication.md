# Phase D of WorkOS AuthKit Authentication

## Overview

Phase D of [2026-09-29 WorkOS AuthKit Authentication for Storyteller](../2026-09-29%20WorkOS%20AuthKit%20Authentication%20for%20Storyteller.md) adds AuthKit M2M applications as a fourth machine credential kind, `ClientCredentials`. In a project with that policy, the following now works end to end:

- `sform machine create` creates a WorkOS Connect M2M application in a configured organization and mints its client secret. Storyteller stores the machine with the application client ID as its ID.
- The machine exchanges its client ID and secret for a JWT at the AuthKit domain. The API accepts that token as a machine identity.
- Reset and delete are routed by the kind stored with the machine, not by the current project policy.

Native API keys and mTLS certificates are unchanged. `ClientCredentials` projects reject them, and other projects reject M2M tokens.

Unit test results:

| Project | Passed |
|---|---|
| `Access.AuthKit.UnitTests` | 117 of 117 |
| `Access.Certificates.UnitTests` | 60 of 60 |
| `Api.Functions.UnitTests` | 57 of 57 |
| `Cli.UnitTests` | 44 of 44 |
| `Access.AzureAd.UnitTests` | 36 of 36 |
| `Backend.CosmosDb.UnitTests` | 107 of 107 |

The new WorkOS integration test (`Access.AuthKit/testint`) skips itself without `WORKOS_TEST_*` variables, and it was not run against WorkOS. The Cosmos integration tests could not run on this machine; see Verification.

Before coding, the WorkOS documentation was checked. It answers the spec's Open Questions 2 and 3:

- M2M tokens carry `sub` = `client_id` = the application client ID.
- Their `aud` is the environment client ID and cannot be configured per application.
- Granted permissions arrive in a space-separated `scope` claim. M2M tokens have no `permissions` claim and no JWT template.

## What Was Done

### 1. Credential kind and contracts

- **`Abstractions.Access/src/MachineCredentialKind.cs`:** `ClientCredentials = 3`, appended so the stored integer values stay the same.
- **`Sdk.NSwag/src/ApiSdk.g.cs`:** `ClientCredentials = 3` is added by hand to the generated `MachineAccessCredentialKind` and `MachineAuthenticationPolicyCredentialKind` enums. The SDK is generated from a stored OpenAPI file, and a running host is needed to export a new one. The next NSwag regeneration should produce the same values.
- **`Backend.Core/src/Accessing/IIdentityProviderMachineAccessService.cs`:** the marker interface from the spec.
- **`Backend.Core/src/Accessing/MachineAccessNotSupportedException.cs`:** derives from `InvalidOperationException`. `AccessHttp` turns it into 400 for create, reset and delete, as the spec asks ("ClientCredentials machine access is not configured for this deployment").
- **`IMachineAccessService`:** gains default-implemented `ResetMachineAccessAsync(MachineAccess existing, org, project)` and `DeleteMachineAccessAsync(MachineAccess existing, org, project)`. The defaults pass on the same identifiers `CosmosAccessService` used before: `Id` for reset and `ObjectId` for delete.
- **`Backend.Core/src/Accessing/MachineAccessScopes.cs`:** the scope-to-roles table for `MachineAccessScope`. It moved here from `Api.Functions/MachineScopeClaims`, which now delegates to it, so `Access.AuthKit` can derive WorkOS permissions from it. A test pins the table to the `Scopes` constants in `Api.Functions`.

### 2. Routing by policy and stored kind

`PolicyAwareMachineAccessService` takes an optional `IIdentityProviderMachineAccessService`:

- **Create:** a `ClientCredentials` policy (or default) is routed to it, and the result is stamped with `CredentialKind = ClientCredentials`. Without the service, creation throws `MachineAccessNotSupportedException`.
- **Reset and delete:** the new overloads switch on `existing.CredentialKind`. `ClientCredentials` goes to the identity provider service; every other kind takes the previous API key and certificate paths unchanged.

`CosmosAccessService` passes the stored entity, through `ToMachineAccess()`, to the new reset and delete overloads, and the created access to the compensating delete. API key and certificate machines get the same identifiers as before through the default implementations.

### 3. `WorkOsManagementClient` and resilience

New calls, each built from the WorkOS Connect reference:

| Method | Call | Notes |
|---|---|---|
| `CreateM2MApplicationAsync` | `POST /connect/applications` | `name`, `description`, `organization_id`, `scopes`, `application_type: "m2m"`, `is_first_party: false`. |
| `DeleteApplicationAsync` | `DELETE /connect/applications/{id}` | 404 returns false. |
| `CreateClientSecretAsync` | `POST /connect/applications/{id}/client_secrets` | A response without `secret` throws. |
| `ListClientSecretsAsync` | `GET …/client_secrets` | The reference shows a bare JSON array; a `{ "data": [...] }` list is accepted too. |
| `DeleteClientSecretAsync` | `DELETE /connect/client_secrets/{id}` | 404 returns false. |

New models: `WorkOsConnectApplication`, `WorkOsClientSecret`, `WorkOsM2MApplicationCreate`.

- **Errors:** failures throw `WorkOsApiException`, an `HttpRequestException` that carries the status code and the WorkOS `message` / `error_description`. It never includes the key or the request body. `GetUserAsync` (Phase B) uses it as well, so it still throws `HttpRequestException`.
- **Resilience:** the typed client gets a retry handler through `Microsoft.Extensions.Http.Resilience`: three attempts, exponential backoff with jitter, and `Retry-After` honoured.
  - HTTP 429 is retried for every method, because WorkOS did not process the call.
  - 5xx responses, transport errors and timeouts are retried only for GET and DELETE.
  - **Deviation:** the spec says "retry on 429/5xx" in general. Repeating a POST after a 5xx could create a second application or secret, so POSTs are not retried in that case.
- **Package version:** `Directory.Packages.props` pins `Microsoft.Extensions.Http.Resilience` to 10.0.5, which does not exist, so NuGet resolves 10.1.0 (NU1603). The two `Aspire.ServiceDefaults` projects already had this warning; the catalog entry was left as it was.

### 4. `AuthKitMachineAccessService`

| Operation | Behaviour |
|---|---|
| Create | Requires `MachineOrganizationId`, and otherwise throws `MachineAccessNotSupportedException`. It creates the application, then its secret. `MachineAccess` is `Id = client_id`, `ObjectId = application id`, `AccessKey = secret`, `CredentialKind = ClientCredentials`. If the secret cannot be created, the application is deleted again and the error is rethrown. |
| Reset | Lists the secrets, mints a new one, then deletes every older one. If WorkOS is already at its limit of five, the oldest secret is deleted first to make room. This settles the spec's "mint before delete" against the limit. |
| Delete | Deletes the application by `ObjectId`. A missing application counts as already deleted. |

Applications are named `42.sform.{org}.{project}.{guid:N}`. Their description is `organization=…|project=…|scope=…[|annotation=…]`, matching the Keycloak service.

**WorkOS scopes for the application:** these are the inverse of `PermissionMap`. Every mapped WorkOS permission whose Storyteller scopes all fall within what the machine scope grants (via `MachineAccessScopes`) is included, in ordinal order.

- A permission that also grants something outside, such as `Configuration.Secrets`, is never included.
- If no permission qualifies, creation fails with 400 before any WorkOS call. A token without permissions would pass no scope check.
- A machine scope that is only partly covered logs a warning.

`AnnotationKey` is recorded in the description only. API key machines have the same limitation: the `annotationKey` claim is not enforced anywhere.

### 5. M2M token validation

`AuthKitBearerTokenValidator` now has two token sources and picks one by the token's unverified `iss`. A token from an unknown issuer returns 401 without any key fetch.

| | User source | M2M source |
|---|---|---|
| Issuer | `Auth:AuthKit:Issuer` | `AuthKitDomain` |
| JWKS | `JwksUri` | `{AuthKitDomain}/oauth2/jwks` |
| Audience | `Audience`, when set | the environment client ID (`Auth:AuthKit:ClientId`), always checked |
| Enabled | always | `AuthKitDomain` and `MachineOrganizationId` both set |

On both sources: RS256, a 30 s clock skew, issuer matching with only a trailing slash allowed to differ, and a key refresh on an unknown `kid`. Each source has its own singleton `ConfigurationManager<JsonWebKeySet>`. A new five-argument constructor takes both managers for tests.

**M2M acceptance:**

- `client_id` must equal `sub` and must not be a `user_` ID (`AuthKitClaimNormalizer.TryGetMachineClientId`).
- `org_id` must equal `MachineOrganizationId`. This is the spec's defense in depth.
- **Not done:** Connect user tokens from the AuthKit domain, as used by Swagger UI, are still rejected. That needs its own audience decision.

**Shared custom domain:** when `Issuer` equals `AuthKitDomain`, a token that passes the user source but has no `user_` subject is tried against the M2M source. Users and machines then each validate against their own keys.

**`AuthKitClaimNormalizer`:**

| Method | Behaviour |
|---|---|
| `NormalizeUser` | The Phase B logic. |
| `NormalizeMachine` | Adds `azp = client_id`. Maps the space-separated `scope` claim (and `permissions`, if present) through `PermissionMap` into `scp`. `DefaultUserScopes` are not applied. Incoming `scp` / `roles` / `azp` are dropped, as for users. `IsMachine = true`, `MachineId = client_id`. |
| `Normalize` (DEV_AUTH path) | Detects M2M claims and calls `NormalizeMachine`; otherwise `NormalizeUser`. |

### 6. Policy enforcement in the API

- **Bearer machine marker:** `BearerAuthenticationMiddleware` sets the new `FunctionContextItemKeys.MachineCredentialKind = ClientCredentials` next to `MachineIdentity` for every machine bearer result.
- **Project check:** new `Security/MachineCredentialPolicy.EnsureAllowedAsync` runs in `CheckAccessToAsync`, after `VerifyAccessForMachineAsync` proved that the project owns the machine. That is the lazy project resolution the spec describes.
  - It reads the project policy (or `MachineAuth:DefaultCredentialKind`).
  - A bearer machine is rejected with `SecurityTokenException` (401) unless the policy is `ClientCredentials`.
  - It lives outside the `#if DEV_AUTH` block so it can be unit tested. Debug builds still skip `CheckAccessToAsync` entirely.
  - The check applies to every bearer machine, Entra machine tokens included. `AddAzureAdMachineAccess` is not wired, so no Entra machine entity exists today.
- **`MachineAuthenticationMiddleware`:** gains an explicit `ClientCredentials => false` arm. An API key or certificate is never enough for such a project; the old `_ => false` already behaved that way.
- **Machine IDs:** `GET`, `PUT` and `DELETE` on `…/access/machines/{id}` validated the ID as a GUID. The new `Security/MachineIds.IsValid` also accepts a WorkOS client ID (`^client_[0-9A-Za-z]+$`). Without it, M2M machines could not be read, reset or deleted.

### 7. Startup

`Program.cs` (AuthKit branch) binds `Auth:AuthKit` and calls `AddAuthKitMachineAccess` when `AuthKitOptions.HasMachineAccess()` is true, that is, when both `AuthKitDomain` and `MachineOrganizationId` are set.

**Deviation:** the spec turns machine access on with `AuthKitDomain` alone. Since Phase B, `AuthKitDomain` also turns on the OpenAPI flows. Requiring the machine organization as an explicit opt-in keeps a Swagger-only setup from also needing the management key.

`AddAuthKitMachineAccess` registers `AuthKitMachineAccessService` and `IIdentityProviderMachineAccessService` (transient, because the typed `WorkOsManagementClient` is transient). It also registers `AuthKitMachineAccessOptionsValidator`, which fails startup without `ApiKey`, without `MachineOrganizationId`, or without an absolute HTTPS `AuthKitDomain`. `AuthKitOptions` gains `HasMachineAccess()` and `GetMachineJwksUri()`.

### 8. CLI and example

- **`sform machine machine-auth`:** `-k|--credential-kind` accepts `ClientCredentials` or `client-credentials`, and undefined numeric values are rejected.
- **`sform machine create`:** for a `ClientCredentials` machine it prints:
  - the client ID and the client secret;
  - the token URL `{AuthKitDomain}/oauth2/token`, with the domain from discovery and a placeholder when it is unknown;
  - a one-line `curl` that reads the secret from `$CLIENT_SECRET`, so the secret does not land in shell history.

  `ResolvedAuthentication` gains `AuthKitDomain` from the discovery cache for this.
- **Deviation:** the spec writes `sform machine create --kind client-credentials`. The server picks the credential kind from the project policy, so the kind is set once with `machine-auth`, and `create` has no `--kind` option.
- **`Examples/CustomClientApp`:** `dotnet run -- authkit` performs a plain `client_credentials` POST and one `GET …/annotations` call with the token. The Entra variant remains the default.

### Documentation

`docs/Platform/Storyteller/authentication.md` gains "Machine access with AuthKit M2M applications":

- the WorkOS organization and permissions to create;
- the `PermissionMap` example and how application scopes are derived;
- the three settings and the startup rule;
- the CLI commands;
- how M2M tokens are checked.

The settings table now lists `MachineOrganizationId`, the OpenAPI limits are updated, and the M2M entry under "Not available yet" is gone.

## Behaviour changes

- New credential kind `ClientCredentials` for project policies and machines.
- AuthKit deployments with `AuthKitDomain` and `MachineOrganizationId` accept M2M tokens from the AuthKit domain and require `ApiKey` at startup.
- Machine endpoints accept `client_…` IDs.
- A bearer machine token (any provider) is accepted only by `ClientCredentials` projects.
- The AuthKit validator no longer fetches keys for a token from an unknown issuer.
- `ClientCredentials` creation without AuthKit machine access answers 400 instead of 500.

## Left for later phases

- **Phase E:** WorkOS user API keys.
- **Not done:**
  - Connect user tokens from the AuthKit domain (Swagger UI sign-in);
  - `CertificateAndClientCredentials`;
  - retrofitting the Entra ID and Keycloak machine services to `IIdentityProviderMachineAccessService`;
  - one WorkOS organization per Storyteller organization;
  - regenerating the NSwag SDK from a fresh OpenAPI document.
- `is_first_party: false` for M2M applications is not confirmed by the reference (the field is required, and its meaning for M2M is not described). The integration test will show whether WorkOS accepts it.

## Tests

`Access.AuthKit.UnitTests` (65 → 117):

- **`AuthKitMachineAccessServiceTests`** (10), using a recording fake `WorkOsManagementClient`:
  - Create sends the expected name pattern, description, organization and derived slugs (`DefaultRead`: the three read permissions plus a two-scope read permission; never a mixed or secrets permission). It returns `client_id` / application ID / secret.
  - `DefaultReadWrite` gets all six slugs. The annotation is recorded in the description.
  - A failed secret deletes the application.
  - No matching permission, or no organization: 400-type exception with no WorkOS call.
  - Reset order is list, create, then delete the old secrets. At the limit of five, the oldest secret is deleted before the create.
  - Delete removes the application by object ID, and a missing application returns false.
- **`WorkOsManagementClientTests`** (16), using a recording `HttpMessageHandler`:
  - the create-application JSON body, including `application_type` and `is_first_party`, and the bearer key;
  - secret creation, and a secret response without a value;
  - listing from an array and from a `data` list;
  - deletes with 204 and 404;
  - a WorkOS error message without the key;
  - the eight retry decisions.
- **`AuthKitMachineTokenValidationTests`** (12), with separate user and machine RSA keys:
  - An M2M token yields a machine with mapped `scp`, `azp` and `org_id`, without touching the user keys.
  - A wrong or missing org, the user audience, a token signed with the user key (key refresh requested), and a non-application subject on the domain are all rejected.
  - With machine access disabled, no keys are fetched. An unknown issuer fetches no keys.
  - A user token still validates. With a shared custom-domain issuer, users and machines are told apart.
- **`AuthKitMachineClaimsTests`** (9): `NormalizeMachine` mapping, with no user defaults and forged `scp` / `azp` dropped; the `TryGetMachineClientId` cases; detection by `Normalize`.
- **`AuthKitRegistrationTests`** (+5): `HasMachineAccess` and `GetMachineJwksUri`; `AddAuthKitMachineAccess` registration; the startup failure listing both `ApiKey` and `AuthKitDomain`.

`Access.Certificates.UnitTests` (+5, `PolicyAwareClientCredentialsTests`, with Moq):

- create routed and stamped, for an explicit policy and for the default kind;
- not supported without the service;
- reset and delete follow the stored kind while the policy says `ApiKey`, without reading the policy;
- reset without the service.

`Api.Functions.UnitTests` (+17, `MachineAccessRulesTests`):

- machine ID validation (7);
- the scope table against the `Scopes` constants (3);
- `MachineCredentialPolicy`: unmarked machines are not checked, a `ClientCredentials` project accepts, the three other kinds reject, and the default kind applies when there is no policy.

The bearer middleware tests now assert that the `MachineCredentialKind` marker is set for machines and absent for users.

`Cli.UnitTests`: the discovery test also asserts `AuthKitDomain`.

New `Access.AuthKit/testint` (`M2MApplicationLifecycleTests`, in `42.mono.slnx`). It runs only when `WORKOS_TEST_API_KEY`, `WORKOS_TEST_CLIENT_ID`, `WORKOS_TEST_AUTHKIT_DOMAIN`, `WORKOS_TEST_ORGANIZATION_ID` and `WORKOS_TEST_PERMISSION` are set. With them, it:

1. creates an application through `AuthKitMachineAccessService`;
2. obtains a `client_credentials` token;
3. validates it with the real JWKS;
4. resets the secret, checks that the new one works and the old one is rejected (polling up to ten seconds);
5. deletes the application.

New Cosmos-backed `Access.Certificates/testint/ClientCredentialsRoutingTests`, with `RecordingIdentityProviderMachineAccessService` registered in `CosmosFixture`:

1. create in a `ClientCredentials` project, then check storage (kind, object ID, masked key) and `VerifyAccessForMachineAsync`;
2. switch the policy to `ApiKey`;
3. reset and delete are still routed to the identity provider, and the machine is gone afterwards.

## Verification

```
dotnet test src/Platform/Storyteller/Access.AuthKit/test/Access.AuthKit.UnitTests.csproj
dotnet test src/Platform/Storyteller/Access.Certificates/test/Access.Certificates.UnitTests.csproj
dotnet test src/Platform/Storyteller/Api.Functions/test/Api.Functions.UnitTests.csproj
dotnet test src/Platform/Cli/test/Cli.UnitTests.csproj
dotnet test src/Platform/Storyteller/Access.AzureAd/test/Access.AzureAd.UnitTests.csproj
dotnet test src/Platform/Storyteller/Backend.CosmosDb/test/Backend.CosmosDb.UnitTests.csproj
```

The results are in the Overview table, all with 0 failures.

**Builds:** the runs used `--artifacts-path` so they would not collide with a locally running Functions host.

- `Api.Functions` and `Cli` build in Release.
- `Api.Web`, `DbCreator`, `Examples/CustomClientApp`, `Access.Certificates.IntegrationTests` and `Access.AuthKit.IntegrationTests` build.
- A no-incremental build reports no new warnings in Phase D files. `MachineAuthenticationMiddleware` SA1117 on the existing log call, `CosmosFixture` SA1210, NU1510 and NU1603 were already there.

**Integration tests:**

- `Access.AuthKit.IntegrationTests` ran and skipped itself, because no `WORKOS_TEST_*` variables were set.
- `Access.Certificates.IntegrationTests` could not run. The reused Testcontainers Cosmos emulator container (`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest`, created 2026-09-21) exits with "The evaluation period has expired". All 23 tests, the existing 22 and the new one, fail in fixture initialization. Pulling a current emulator image and removing the reused container should fix that; neither was done here.

No M2M token from a real WorkOS environment was validated, and no application was created in WorkOS.
