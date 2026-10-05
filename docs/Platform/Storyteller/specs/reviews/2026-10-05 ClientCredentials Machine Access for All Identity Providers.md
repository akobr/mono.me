# ClientCredentials Machine Access for All Identity Providers

## Overview

This review covers [2026-10-05 ClientCredentials Machine Access for All Identity Providers](../2026-10-05%20ClientCredentials%20Machine%20Access%20for%20All%20Identity%20Providers.md). `ClientCredentials` machine access now works with any of three identity providers, whichever entry point the composition root calls:

| Provider | Entry point | Machine credential |
|---|---|---|
| AuthKit | `AddAuthKitMachineAccess` | M2M application |
| Entra ID | `AddAzureAdMachineAccess` | app registration |
| Keycloak | `AddKeycloakMachineAccess` | confidential client |

Each entry point registers two things:

- the provider's `IIdentityProviderMachineAccessService`, which `PolicyAwareMachineAccessService` uses for `ClientCredentials` projects;
- an `IMachineTokenValidator` for the provider's machine tokens.

The machine provider no longer has to be the user provider. At most one machine provider can be registered.

Unit test results:

| Project | Passed |
|---|---|
| `Access.AuthKit.UnitTests` | 125 of 125 |
| `Access.AzureAd.UnitTests` | 54 of 54 |
| `Access.Keycloak.UnitTests` (new) | 20 of 20 |
| `Api.Functions.UnitTests` | 62 of 62 |
| `Access.Certificates.UnitTests` | 60 of 60 |
| `Cli.UnitTests` | 44 of 44 |

No provider was called for real.

## What Was Done

### 1. Machine token validators

- **`Backend.Core/src/Accessing/IMachineTokenValidator.cs`:** `CanValidate(issuer)` and `ValidateAsync(rawToken)`, as specified.
- **`BearerAuthenticationMiddleware`:**
  - It still calls the user `IBearerTokenValidator` first.
  - Only when that returns null does it read the unverified `iss` (with `JwtSecurityTokenHandler`) and ask the registered machine validators that claim the issuer.
  - A result counts only when it has `IsMachine` and a non-empty `MachineId`, so a machine validator cannot sign users in.
  - `BearerKeyRetrievalException` from either validator is still 503 with `Retry-After`.
  - A machine result gets the Phase D `MachineCredentialKind = ClientCredentials` marker, so `MachineCredentialPolicy` checks the project policy.
  - The `DEV_AUTH` decode path does not consult machine validators.
- **`AddUserAuthenticationOptions`:** now idempotent, through a private marker registration. Machine entry points call it too, and the test `UserAndMachineRegistration_BindTheOptionsOnce` confirms that `DefaultUserScopes` is not doubled.

### 2. One identity provider for machines

- `UserAuthenticationEntryPoint.EnsureNoOtherIdentityProviderMachineAccess(provider)` throws when an `IIdentityProviderMachineAccessService` is already registered. Every `Add…MachineAccess` calls it first.
- None of them registers `IMachineAccessService` any more. Before, `AddAzureAdMachineAccess()` and `AddKeycloakMachineAccess()` would have replaced the policy-aware router.
- `Program.cs` registers the machine provider after the user provider:
  - AuthKit when `Auth:AuthKit` has the domain and machine organization, for any user provider;
  - otherwise one of the commented `AddAzureAdMachineAccess` / `AddKeycloakMachineAccess` lines.
- `Api.Functions.csproj` now references `Access.Keycloak`, so switching provider means editing only `Program.cs`.

### 3. Token endpoint in `MachineAccess`

- `MachineAccess` / `IMachineAccess` and the SDK model (added to `ApiSdk.g.cs` by hand) gain `TokenEndpoint` and `TokenScope`. They are set on create and are not stored, so reset returns them empty.
- `sform machine create` prints the server's token URL, and the scope when there is one, and adds `-d scope=…` to the `curl` line. It falls back to the discovered AuthKit domain only when the server sends no endpoint.

### 4. AuthKit

- **Validators:**
  - The M2M source moved from `AuthKitBearerTokenValidator` into the new `AuthKitMachineTokenValidator`, with the same checks as Phase D.
  - The user validator is users-only again. It returns null without fetching keys for a foreign issuer, and returns null for a non-`user_` subject on its own issuer, which covers the shared custom-domain case where the middleware then asks the machine validator.
  - Both use a new internal `AuthKitTokenSource`: key retrieval, RS256, 30 s skew, issuer matching and key refresh, previously inline in the validator.
- **`AddAuthKitMachineAccess`:**
  - It registers the options, `AuthKitClaimNormalizer` (`TryAddSingleton`) and the WorkOS client itself. The client goes through a private idempotent `AddWorkOsManagementClient`, so the resilience handler is not added twice.
  - It works without `AddAuthKitUserAuthentication`.
  - `AuthKitMachineAccessOptionsValidator` also requires `Auth:AuthKit:ClientId`, the M2M audience.
- **Service:** `AuthKitMachineAccessService` sets `TokenEndpoint = {AuthKitDomain}/oauth2/token`.

### 5. Entra ID

- **`AzureAdMachineAccessOptions`** (`MachineAuth:AzureAd:TenantId`) replaces the hard-coded Graph tenant and builds the machine token endpoint.
- **`AzureAdMachineAccessOptionsValidator`** (registered for `UserAuthenticationOptions`) requires that tenant, `Auth:TenantId`, `Auth:ClientId`, and GUIDs in `Auth:AppRoles:DefaultRead` / `DefaultReadWrite`.
- **`AzureAdMachineAccessService`:**
  - It implements `IIdentityProviderMachineAccessService` and takes `GraphServiceClient` and the options from DI instead of environment variables and per-call clients. The Graph calls are the same.
  - It returns `CredentialKind = ClientCredentials`, `TokenEndpoint` and `TokenScope = api://{ClientId}/.default`.
  - Reset and delete overloads use `ObjectId`.
  - Delete now returns false on a Graph 404 (`ODataError`) instead of throwing.
  - The missing read-role error no longer names the read/write key.
- **`EntraIdMachineTokenValidator`:** wraps `EntraIdBearerTokenValidator` and returns only machine results. `CanValidate` uses the new `EntraIdBearerTokenValidator.IsEntraIssuer`, which is the existing issuer rule, extracted.
- **`AddAzureAdMachineAccess(configuration)`:**
  - It registers `GraphServiceClient` with `DefaultAzureCredential` for the configured tenant (`TryAddSingleton`), the concrete `EntraIdBearerTokenValidator` (`TryAddSingleton`), the service and the validator.
  - **Signature change:** it now takes `IConfiguration`. The only caller was a commented line.
- **`AddEntraIdUserAuthentication`:** maps `IBearerTokenValidator` to the same concrete singleton, so Entra users and Entra machines share one OpenID configuration manager.
- **Behaviour change:** Graph calls no longer use the hard-coded tenant `8ddd03c1-…`. Set `MachineAuth:AzureAd:TenantId` to it to keep the previous directory.

### 6. Keycloak

- **`KeycloakOptions`** (section `Keycloak`) uses the keys and defaults the environment variables had. It adds `Audience` (default `storyteller`) and the issuer and token endpoint helpers.
- **`KeycloakOptionsValidator`** requires an absolute `ServerUrl`, a `Realm`, an `Audience`, and an admin secret or a username with a password.
- **`KeycloakMachineAccessService`:**
  - It implements `IIdentityProviderMachineAccessService` with `IOptions<KeycloakOptions>`.
  - The client representation now disables the standard and direct-grant flows and carries two protocol mappers: `oidc-audience-mapper` (`included.custom.audience = Audience`) and `oidc-hardcoded-claim-mapper` (`storyteller_scope` = the `MachineAccessScopes` of the machine scope). This answers the old `TODO: [P2] Assign roles/scopes`.
  - The returned `Scope` is the requested scope, no longer collapsed to `DefaultRead` / `DefaultReadWrite`, because the mapper carries it exactly.
  - A failed secret read deletes the client again.
  - Delete returns false on 404 and now throws on other failures, so Storyteller does not forget a machine whose client still works. Before, any failure returned false.
  - Admin calls use a named `HttpClient`.
- **`KeycloakMachineTokenValidator`:**
  - Keys come from the realm's OpenID metadata through a singleton `ConfigurationManager<OpenIdConnectConfiguration>`, with HTTPS required unless the server is a loopback address.
  - It checks the exact realm issuer (a trailing slash may differ), the configured audience, RS256 and a 30 s skew.
  - `azp` must be present and is the machine ID.
  - `scp` is rebuilt from `storyteller_scope` only; `roles`, `scp`, `azp` and `*/scope` claims from the token are dropped.
  - Key faults become `BearerKeyRetrievalException`, and an unknown `kid` requests a refresh.
- **Project file:** `Access.Keycloak.csproj` gains the Options, Configuration and IdentityModel packages and `InternalsVisibleTo` for the new tests. All versions are already in the catalog.

### Documentation

`authentication.md`: "Machine access with AuthKit M2M applications" became "Machine access with client credentials". It covers:

- the common flow and token rules;
- one subsection each for AuthKit, Entra ID and Keycloak, with their settings, registration, token endpoint and checks.

## Behaviour changes

- `AddAuthKitMachineAccess` is registered for any user provider when the AuthKit machine settings are present. It used to be registered only with AuthKit users.
- A token the user validator rejects can still be accepted as a machine by the registered machine provider.
- Entra ID and Keycloak machine access require the settings listed above at startup. They no longer read environment variables directly.
- The Graph tenant is configurable. The hard-coded tenant is gone.
- `MachineAccess` responses carry `TokenEndpoint` / `TokenScope` on create.

## Left for later

- **Not done:**
  - `CertificateAndClientCredentials`;
  - Connect user tokens from the AuthKit domain;
  - machine validators on the `DEV_AUTH` decode path;
  - storing `TokenEndpoint` so reset can return it;
  - regenerating the NSwag SDK;
  - a WorkOS organization per Storyteller organization.
- **Entra machine tokens** carry the app role values in `roles`. Those values must match the API scope names, optionally with the `App.` prefix. Storyteller does not create or check the app roles.
- **Live checks:** no Keycloak or Graph instance was exercised. The Keycloak mapper names and config keys follow the Keycloak admin REST representation and should be checked against a running Keycloak once. The Graph requests come from the real Graph SDK against a stub handler.

## Tests

**`Access.AzureAd.UnitTests`** (36 → 54):

- **`AzureAdMachineAccessServiceTests`** (7). A `GraphServiceClient` built over `GraphStubHandler` with `AnonymousAuthenticationProvider`.
  - Create, for three scopes:
    - the request sequence: application, `addPassword`, service principal, API service principal by `$filter=appId eq 'api-client'`, app role assignment;
    - the application body: name pattern, notes with the annotation, the required resource access with the right role;
    - the assignment body;
    - the returned ID, object ID, secret, scope, kind, token endpoint and token scope.
  - Create fails without a secret.
  - Reset removes only the generated password, then adds one.
  - Delete answers 204 → true and 404 (`ODataError`) → false.
- **`EntraIdMachineTokenValidatorTests`** (7): a machine token is accepted; a user token (`azp` is the API) and a token for another API are rejected; `CanValidate` for v1, v2, a lookalike host and a foreign issuer.
- **`AzureAdMachineAccessRegistrationTests`** (4):
  - the provider next to AuthKit users, without `IMachineAccessService`;
  - the startup failure naming the machine tenant and the malformed role;
  - Entra users and machines share one `EntraIdBearerTokenValidator`;
  - a second machine provider throws.

**`Access.Keycloak.UnitTests`** (new project in `42.mono.slnx`, 20 tests):

- **`KeycloakMachineAccessServiceTests`** (7), with `KeycloakStub` as both handler and `IHttpClientFactory`:
  - create: the admin token form, the bearer on admin calls, the client representation with both mappers and their config, and the returned values including the token endpoint;
  - compensation when the secret read fails;
  - reset;
  - delete answers 204 → true and 404 → false; a 500 throws;
  - the password grant for the admin token.
- **`KeycloakMachineTokenValidatorTests`** (10):
  - a machine token gets `scp` from `storyteller_scope`, with forged `scp` / `roles` dropped;
  - wrong audience, other realm, missing `azp`, and an unknown key (with a refresh) are rejected;
  - a key fault throws `BearerKeyRetrievalException`;
  - `CanValidate` cases.
- **`KeycloakMachineAccessRegistrationTests`** (3): registration without `IMachineAccessService` and with the default audience; the startup failure for a bad URL and no admin credential; a second provider throws.

**`Access.AuthKit.UnitTests`** (117 → 125):

- `AuthKitMachineTokenValidationTests` now drives the user and machine validators side by side. It adds tests that the user validator leaves M2M tokens alone without fetching keys, that the machine validator needs the machine organization, and that `CanValidate` matches only the domain. In the shared-issuer case, the user validator rejects the machine token and the machine validator accepts it.
- `AuthKitRegistrationTests` adds: the machine validator registration; machine access with Entra users and no `IBearerTokenValidator`; options bound once; a second provider throws.

**`Api.Functions.UnitTests`** (57 → 62, `MachineTokenFallbackTests`):

- after a user rejection, the validator for the issuer accepts and sets the machine identity and marker;
- no fallback after a user success;
- no validator for the issuer → 401;
- a user result from a machine validator → 401;
- a machine key fault → 503.

## Verification

```
dotnet test src/Platform/Storyteller/Access.AuthKit/test/Access.AuthKit.UnitTests.csproj
dotnet test src/Platform/Storyteller/Access.AzureAd/test/Access.AzureAd.UnitTests.csproj
dotnet test src/Platform/Storyteller/Access.Keycloak/test/Access.Keycloak.UnitTests.csproj
dotnet test src/Platform/Storyteller/Api.Functions/test/Api.Functions.UnitTests.csproj
dotnet test src/Platform/Storyteller/Access.Certificates/test/Access.Certificates.UnitTests.csproj
dotnet test src/Platform/Cli/test/Cli.UnitTests.csproj
```

All pass; see the table in the Overview. `Access.AuthKit.IntegrationTests` ran and skipped itself, because no `WORKOS_TEST_*` variables are set.

**Builds:**

- `Api.Functions` and `Cli` build in Release.
- `Api.Web`, `DbCreator`, `Examples/CustomClientApp` and `Access.Certificates.IntegrationTests` build. The runs used `--artifacts-path`.
- A no-incremental build shows no new warnings in the changed files. The `Program.cs` SA1515 / SA1005 on the existing commented lines were already there.

The Cosmos integration tests still cannot start their emulator on this machine; see the Phase D review.
