# Phase A of WorkOS AuthKit Authentication

## Overview

Phase A of [2026-09-29 WorkOS AuthKit Authentication for Storyteller](../2026-09-29%20WorkOS%20AuthKit%20Authentication%20for%20Storyteller.md) extracts the existing Entra ID bearer check behind `IBearerTokenValidator`. A valid Entra token is still accepted with the same audiences, issuers, clock skew, and machine rule. The host still selects Entra ID when `Auth:Provider` is missing.

AuthKit itself is not in this phase. Naming `Auth:Provider` as `AuthKit` fails startup, so those tokens are not validated with the Entra rules.

`Access.AzureAd.UnitTests` passes 34 of 34 tests. `Api.Functions.UnitTests` passes 18 of 18 tests. Neither suite calls a live Entra tenant.

## What Was Done

### 1. Options and the validator contract (`Backend.Core`)

New types under `Backend.Core/src/Accessing/`:

| File | Purpose |
| --- | --- |
| `IdentityProviderKind.cs` | `EntraId = 0`, `AuthKit` |
| `UserAuthenticationOptions.cs` | Section `Auth`. `Provider` defaults to `EntraId`. Legacy `TenantId`, `ClientId`, and `AppRoles` bind on the same object. |
| `AuthKitOptions.cs` | Nested AuthKit settings from the spec, including issuer, JWKS, audience, domain, management key, M2M organization, scopes, and permission map. Stored here so Phase B can bind them. Nothing in this phase calls WorkOS. |
| `IBearerTokenValidator.cs` | `ValidateAsync` returns null when the token is not valid. A signing-key failure throws `BearerKeyRetrievalException`. |
| `BearerValidationResult.cs` | Claims, `IsMachine`, and `MachineId`. |
| `BearerKeyRetrievalException.cs` | Wraps a transient key-retrieval failure so the middleware can turn that case into 503. |

`AuthKitOptions` is its own file. The spec snippet kept it beside `UserAuthenticationOptions`; StyleCop SA1402 requires one type per file.

### 2. Entra validator (`Access.AzureAd`)

`EntraIdBearerTokenValidator` replaces the inline `ValidateAccessToken()` path.

- Settings come from `UserAuthenticationOptions`. The metadata address is `https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration`.
- One `ConfigurationManager<OpenIdConnectConfiguration>` is created with the validator and reused. Each call prefetches that snapshot and copies `SigningKeys` onto `TokenValidationParameters`. IdentityModel 8.18 types `TokenValidationParameters.ConfigurationManager` as `BaseConfigurationManager`, so the OpenID manager cannot be assigned there. The manager still caches and refreshes the keys.
- Audiences stay `api://{clientId}` and `{clientId}`. An issuer is accepted when it starts with `https://sts.windows.net/` or `https://login.microsoftonline.com/`, compared ordinal-ignore-case. The trailing slash keeps a host such as `login.microsoftonline.com.evil` out. Clock skew stays at the handler default of five minutes.
- `IdentityModelEventSource.ShowPII` is set only when `IHostEnvironment.IsDevelopment()`. Production leaves the process default.
- `azp` is preferred over `appid`. An empty app id, or one equal to `ClientId` ignoring case, is a user token. Any other app id is `MachineId` and `IsMachine` is true.
- An unreadable token returns null before any key fetch. `SecurityTokenException` and `ArgumentException` return null. Cancellation of the caller's token propagates. `InvalidOperationException`, `HttpRequestException`, `IOException`, and any other `OperationCanceledException` during key retrieval become `BearerKeyRetrievalException`.
- A missing `Auth:TenantId` throws from the constructor that builds the manager. A missing `Auth:ClientId` throws from `ValidateAsync`.

`UserAuthenticationOptionsValidator` runs through `ValidateOnStart`. Entra ID requires both `ClientId` and `TenantId`. AuthKit requires `AuthKit.ClientId` only. `ApiKey` and `AuthKitDomain` stay optional until M2M is enabled. An undefined provider enum fails validation.

`AddEntraIdUserAuthentication` on the existing Azure AD `EntryPoint` binds the `Auth` section, registers the options validator, and registers `EntraIdBearerTokenValidator` as the singleton `IBearerTokenValidator`.

Direct package references were added for `Microsoft.IdentityModel.Protocols.OpenIdConnect` and `System.IdentityModel.Tokens.Jwt`. Both are **8.18.0** in `Directory.Packages.props`. Microsoft.Graph 6.7.0 requires at least 8.18.0; 8.15.0 failed restore with NU1605.

### 3. Bearer middleware and request extensions (`Api.Functions`)

Middleware order is now:

`ExceptionHandlingMiddleware` → `MachineAuthenticationMiddleware` → `BearerAuthenticationMiddleware`

`BearerAuthenticationMiddleware`:

- A non-HTTP invocation, a request that already has `CachedClaims`, or a request without an `Authorization: Bearer ` header calls the next middleware. `ApiKey` authorization is left to the machine middleware.
- In the validating path, a null result writes HTTP 401 through `GetInvocationResult().Value`, the same slot the machine middleware uses. `BearerKeyRetrievalException` writes HTTP 503 and `Retry-After: 5`. The log records the path and the exception type. The raw token and the exception text are omitted, because ShowPII can include the token.
- On success the claims are stored in `CachedClaims`. When `IsMachine` and `MachineId` are set, `MachineId` is stored in `FunctionContextItemKeys.MachineIdentity`.

Debug builds of `Api.Functions` still define `DEV_AUTH`. `BearerValidationMode.DecodeWithoutValidation` defaults to that constant and is internal to the test assembly, so the unit tests can run the validating path against a Debug build. The debug path decodes with `JwtSecurityTokenHandler.ReadJwtToken` and still runs `EntraIdBearerTokenValidator.TryGetMachineId`. An unreadable token passes through.

`GetClaims()` reads `CachedClaims` when the stored object is an `IReadOnlyList<Claim>`. Otherwise it uses the existing authenticated `Identities` fallback and caches that list. Inline JWT parsing and `ValidateAccessToken()` are gone. `TryGetApplicationIdentity` and `IsApplicationIdentity` read `MachineIdentity` only. `CheckScope` and `TryCheckScope` are unchanged: `CheckScope` still fails when none of the requested scopes are present.

`Program.cs` reads `Auth:Provider` as a string. A blank value or `EntraId` (any casing) calls `AddEntraIdUserAuthentication`. `AuthKit` throws `InvalidOperationException` until Phase B registers `AddAuthKitUserAuthentication`. Any other name throws as an unknown provider.

`local.settings.json` sets `Auth:Provider` to `EntraId` next to the existing tenant, client, and app-role values. Commented AuthKit keys were left out; the file is strict JSON.

Both new test projects are in `42.mono.slnx`.

## Behaviour that moved with the extraction

The phase table describes the intended change as none for a valid Entra token. These failure and startup paths changed with the move:

- `ShowPII` is on only in Development. It used to be set on every validation.
- The host fails at startup when Entra `ClientId` or `TenantId` is missing. `local.settings.json` already has both.
- An invalid or unreadable bearer is rejected with 401 by the middleware before the function runs, including on an endpoint that never calls `GetClaims`. Under `DEV_AUTH`, an unreadable bearer still passes through.
- A JWKS or metadata failure is 503 with `Retry-After: 5`. It used to escape as an unhandled exception.
- Signing keys are taken from the singleton configuration manager's snapshot, as described above.
- `Auth:Provider` of `AuthKit`, or any unknown name, fails startup. A missing provider still selects Entra ID.
- An empty `azp` / `appid` is a user token. Machine detection no longer consults the authenticated `Identities` collection; it uses the `MachineIdentity` item set by the machine middleware or the bearer middleware.
- The debug decoder uses the Entra machine rule. Phase B should run the active provider's normalizer on that path. This build cannot select AuthKit, so the debug path only serves Entra.

A request with no bearer, a valid Entra user token, a valid Entra machine token, and an API-key or certificate request that already cached its claims follow the previous success path. `AzureAdMachineAccessService` still reads its Graph settings from environment variables. `AddAzureAdMachineAccess()` stays commented out.

## Left for later phases

Phase B and after are untouched: no `Access.AuthKit` project, no AuthKit validator or claim normalizer, no profile resolver, no provider-aware OpenAPI flows, no `GET v1/auth/configuration`, no CLI auth rewrite, and no M2M client-credentials credential kind.

`PermissionMap` binding was tested with a key that contains no colon (`annotation-read`). A colon inside a configuration key is a path separator, so a WorkOS permission slug that contains `:` will not bind as one dictionary entry. That needs a decision in Phase B.

## Tests

New xUnit + Shouldly projects, matching `Access.Certificates` tests.

`src/Platform/Storyteller/Access.AzureAd/test` (34 tests):

- Missing `Provider` binds as `EntraId`, and the AuthKit defaults (`Issuer`, `ApiBaseUrl`, `ApiKeyScheme`, null audience) are present.
- Flat keys bind tenant, client, app roles, and the nested AuthKit client, issuer, audience, domain, scopes, and permission map.
- Entra validation succeeds with both ids and fails when either id is missing or whitespace. AuthKit validation requires only `AuthKit.ClientId`. An undefined provider fails.
- Host start throws `OptionsValidationException` when the Entra ids are empty, including when the process environment contains `Auth:TenantId` or `Auth:ClientId`. The test configuration is added on the host builder and the environment is forced to Production.
- Host start with Entra settings registers `EntraIdBearerTokenValidator` and binds `AppRoles`.
- Entra tokens: user `azp` equal to the client id is a user; the client id itself is a valid audience; v1 and v2 issuers are accepted; issuer comparison ignores case; `azp` wins over `appid`; `appid` alone can mark a machine; a case-insensitive client-id match is a user; a foreign issuer, a lookalike Microsoft host, a wrong audience, an expired token, a token not yet valid, HS256, and an unknown `kid` return null.
- An unreadable token does not fetch keys. Key retrieval throws `BearerKeyRetrievalException`. Repeated calls use one configuration manager. Cancellation propagates. The constructor throws without a tenant, and `ValidateAsync` throws without a client id.

`src/Platform/Storyteller/Api.Functions/test` (18 tests):

- A non-HTTP invocation calls next and does not validate. The test registers an `IHttpRequestDataFeature` that returns null. The worker's default feature reads `FunctionDefinition`, which the test double does not implement.
- Cached claims, a missing `Authorization` header, and `ApiKey` authorization pass through.
- An invalid token is 401. A key-retrieval failure is 503 with `Retry-After: 5`.
- A user result stores the same claim list and leaves `MachineIdentity` unset. A machine result stores the id.
- The debug decoder reads `sub` and `preferred_username` without calling the validator, sets `MachineIdentity` when `azp` differs from the client id, leaves it unset when `azp` matches ignoring case, and passes an unreadable bearer through.
- `OperationCanceledException` from the validator propagates.
- `GetClaims` prefers cached claims over a bearer header, caches an authenticated identity, and does not parse a bearer on its own. `TryGetApplicationIdentity` returns the stored machine id, and a missing or empty id returns false.

The middleware tests subclass `BearerAuthenticationMiddleware` and override `AssignResponse`. `IFunctionBindingsFeature` is internal to the worker, so the tests cannot supply the feature that `GetInvocationResult` writes through. Production still assigns `context.GetInvocationResult().Value`.

## Verification

```
dotnet test src/Platform/Storyteller/Access.AzureAd/test/Access.AzureAd.UnitTests.csproj --nologo --verbosity minimal
dotnet test src/Platform/Storyteller/Api.Functions/test/Api.Functions.UnitTests.csproj --nologo --verbosity minimal
```

Results: **34 passed, 0 failed** and **18 passed, 0 failed**.

A no-incremental build of `Backend.Core`, `Access.AzureAd`, and `Api.Functions` reports no warnings from the new files. The warnings that remain on touched files were already there: `HttpRequestDataExtensions` CS0162 on the `DEV_AUTH` branches, and `Program.cs` SA1210 / SA1512 / SA1515 / SA1005 on the existing using order and the commented machine-access block. The new using sits in alphabetical order and did not add a warning. NU1510 on `System.Text.Json` in the Abstractions projects is also pre-existing.

These tests sign tokens locally and use a fake configuration manager. A real Entra token against the Functions host was not exercised.
