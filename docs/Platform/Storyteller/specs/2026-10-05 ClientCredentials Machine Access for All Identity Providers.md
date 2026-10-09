# ClientCredentials Machine Access for All Identity Providers

## Problem

Phase D of [2026-09-29 WorkOS AuthKit Authentication for Storyteller](2026-09-29%20WorkOS%20AuthKit%20Authentication%20for%20Storyteller.md) introduced the `ClientCredentials` machine credential kind. It works only with AuthKit M2M applications, and only when AuthKit is also the user identity provider. The deployment should be able to pick one identity provider for machine client credentials: Entra ID app registrations, Keycloak confidential clients or AuthKit M2M applications. The choice is made by the `Add…MachineAccess` entry point the composition root calls. When a project's policy is `ClientCredentials`, that provider then creates, resets and deletes the machine credential, and its tokens are accepted.

## Current State

- **Routing:** `PolicyAwareMachineAccessService` (`Access.Certificates`) routes `ClientCredentials` to an optional `IIdentityProviderMachineAccessService`. Only `AuthKitMachineAccessService` implements it.
- **`Access.AzureAd/src/AzureAdMachineAccessService.cs`:**
  - It implements `IMachineAccessService` directly. `AddAzureAdMachineAccess()` (commented out in `Program.cs`) registers it as `IMachineAccessService`, so it would replace the policy-aware router.
  - Settings are read with `Environment.GetEnvironmentVariable` (`Auth:ClientId`, `Auth:AppRoles:*`). The Graph credential tenant is a hard-coded GUID with a `[P1]` TODO.
  - Every call creates a `GraphServiceClient` with `DefaultAzureCredential`, so the class cannot be unit tested.
- **`Access.Keycloak/src/KeycloakMachineAccessService.cs`:**
  - The same `IMachineAccessService` registration problem.
  - Settings come from `Keycloak:*` environment variables.
  - Clients get no roles or scopes (`TODO: [P2] Assign roles/scopes`), so their tokens would fail every scope check.
- **Machine tokens are validated only by the user-provider validator:**
  - `EntraIdBearerTokenValidator` classifies an Entra token with a foreign `azp` as a machine, but only when Entra ID is the user provider.
  - `AuthKitBearerTokenValidator` holds the AuthKit M2M source, but only when AuthKit is the user provider.
  - Nothing validates Keycloak tokens.
- **Response:** `MachineAccess` does not tell the caller where to exchange the credentials. `sform machine create` assumes the AuthKit token URL.
- **Tests:** `Access.Keycloak` has no test project. `Access.AzureAd` tests cover only the Entra user validator.

## Proposed Changes

### 1. Machine token validators (`Backend.Core`)

Add `Accessing/IMachineTokenValidator.cs`:

```csharp
public interface IMachineTokenValidator
{
    // True when tokens from this (unverified) issuer belong to this provider.
    bool CanValidate(string issuer);

    // Null when the token is not a valid machine token of this provider. Throws BearerKeyRetrievalException on key faults.
    Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default);
}
```

Each `Add…MachineAccess` registers one. `BearerAuthenticationMiddleware` keeps calling the user `IBearerTokenValidator` first. When that returns null, it reads the token's issuer and asks every machine validator with `CanValidate(issuer)`. The first non-null result wins, and only machine results (`IsMachine`) are accepted from them. The existing rules stay as they are:

- `BearerKeyRetrievalException` is 503;
- the `MachineCredentialKind = ClientCredentials` marker is set;
- the project policy check in `CheckAccessToAsync` still applies.

`AddUserAuthenticationOptions` becomes idempotent. Machine entry points need the `Auth` options too, and binding twice would duplicate array values.

### 2. One identity provider for machines

Each of `AddAuthKitMachineAccess`, `AddAzureAdMachineAccess` and `AddKeycloakMachineAccess` throws `InvalidOperationException` when another `IIdentityProviderMachineAccessService` is already registered. None of them registers `IMachineAccessService`; the policy-aware router stays in charge. `Program.cs` registers AuthKit machine access as today. Entra ID and Keycloak are documented alternatives, left as commented lines.

### 3. Token endpoint in `MachineAccess`

Add `TokenEndpoint` and `TokenScope` (both `string?`) to `MachineAccess` / `IMachineAccess`, and by hand to the NSwag SDK model. Identity provider services fill them on create. They are not stored in Cosmos, so get, list, and reset omit them. Reset returns the machine with the regenerated secret only. `sform machine create` prints them and builds the `curl` example from them. It falls back to the discovered AuthKit domain only when they are absent.

| Provider | `TokenEndpoint` | `TokenScope` |
|---|---|---|
| AuthKit | `{AuthKitDomain}/oauth2/token` | none |
| Entra ID | `https://login.microsoftonline.com/{MachineAuth:AzureAd:TenantId}/oauth2/v2.0/token` | `api://{Auth:ClientId}/.default` |
| Keycloak | `{ServerUrl}/realms/{Realm}/protocol/openid-connect/token` | none |

### 4. AuthKit

- Move the M2M source out of `AuthKitBearerTokenValidator` (users only again) into `AuthKitMachineTokenValidator : IMachineTokenValidator`. The checks stay as in Phase D: issuer `AuthKitDomain`, JWKS `{AuthKitDomain}/oauth2/jwks`, audience the environment client ID, `sub == client_id`, `org_id == MachineOrganizationId`.
- `AddAuthKitMachineAccess` no longer depends on `AddAuthKitUserAuthentication`. It adds the options, the normalizer and the WorkOS client itself (idempotently), so AuthKit machines can be used with Entra ID users.
- `AuthKitMachineAccessOptionsValidator` also requires `Auth:AuthKit:ClientId` (the M2M audience).

### 5. Entra ID (`Access.AzureAd`)

- **Options:** new `AzureAdMachineAccessOptions`, section `MachineAuth:AzureAd`, with `TenantId`: the directory where machine app registrations are created and where machines get tokens. It replaces the hard-coded tenant. The API application (`Auth:ClientId`) and the app roles (`Auth:AppRoles:DefaultRead`, `Auth:AppRoles:DefaultReadWrite`) come from `UserAuthenticationOptions`. A validator requires all four.
- **Service:** `AzureAdMachineAccessService` implements `IIdentityProviderMachineAccessService` and takes a `GraphServiceClient` and the options from DI. `AddAzureAdMachineAccess(configuration)` registers the client with `DefaultAzureCredential` for the configured tenant. The Graph calls are unchanged:
  - create: create the application, `addPassword`, create the service principal, find the API service principal, assign the app role;
  - reset: remove the `42.sform.generated` passwords, then add one;
  - delete: delete the application.

  On create it fills `TokenEndpoint` / `TokenScope`. Reset and delete use `existing.ObjectId`.
- **Tokens:** `EntraIdMachineTokenValidator` wraps `EntraIdBearerTokenValidator` and returns only machine results. `CanValidate` accepts the issuers the Entra validator accepts (`https://login.microsoftonline.com/…`, `https://sts.windows.net/…`). When Entra ID is the user provider, the user validator already returns machine results, so the machine validator only matters for other user providers.

### 6. Keycloak (`Access.Keycloak`)

- **Options:** new `KeycloakOptions`, section `Keycloak`, with the same keys the environment variables used: `ServerUrl`, `Realm`, `AdminRealm` (`master`), `AdminClientId` (`admin-cli`), `AdminClientSecret` or `AdminUsername` / `AdminPassword`. It adds `Audience` (default `storyteller`). A validator requires an absolute `ServerUrl`, a `Realm`, an `Audience` and one admin credential.
- **Service:** `KeycloakMachineAccessService` implements `IIdentityProviderMachineAccessService` and reads the options. The created client carries two protocol mappers, so its tokens describe themselves:
  - `oidc-audience-mapper` adds `Audience` to `aud`;
  - `oidc-hardcoded-claim-mapper` adds the claim `storyteller_scope` with the machine's Storyteller scopes (`MachineAccessScopes`, space-separated).

  It fills `TokenEndpoint`. Reset and delete use `existing.ObjectId`. The returned `Scope` stays the machine scope the caller asked for, because the mapper carries the exact scopes.
- **Tokens:** `KeycloakMachineTokenValidator` reads the realm's OpenID metadata (`{ServerUrl}/realms/{Realm}/.well-known/openid-configuration`; HTTP allowed only for loopback hosts) with a singleton `ConfigurationManager<OpenIdConnectConfiguration>`. It checks:
  - issuer `{ServerUrl}/realms/{Realm}` exactly;
  - audience `Audience`;
  - RS256, with a 30 s clock skew.

  The machine ID is `azp`. Incoming `scp` / `roles` / `azp` are dropped; `storyteller_scope` becomes `scp`, and `azp` is re-added from the validated value.

### 7. Tests

- **`Access.AzureAd/test`:**
  - `AzureAdMachineAccessService` against a stub Graph `HttpMessageHandler` (`GraphServiceClient(HttpClient, AnonymousAuthenticationProvider)`): the create sequence and returned values, reset (remove then add), delete;
  - `EntraIdMachineTokenValidator`: machine accepted, user rejected, issuers;
  - registration: identity provider service, machine validator, `IMachineAccessService` not replaced, options validation, a second provider throws.
- **New `Access.Keycloak/test`:**
  - `KeycloakMachineAccessService` against a stub `IHttpClientFactory`: admin token, client representation with both mappers, secret, reset, delete;
  - `KeycloakMachineTokenValidator` with locally signed tokens;
  - registration and options validation.
- **`Access.AuthKit/test`:** the M2M tests move to `AuthKitMachineTokenValidator`. Registration without user authentication.
- **`Api.Functions/test`:** the middleware falls back to a machine validator only after the user validator returns null and only for a matching issuer, ignores user results from machine validators, and maps key faults to 503.
