# Phase B of WorkOS AuthKit Authentication

## Overview

This review covers Phase B of [2026-09-29 WorkOS AuthKit Authentication for Storyteller](../2026-09-29%20WorkOS%20AuthKit%20Authentication%20for%20Storyteller.md). Phase B was delivered in two parts on the same day:

1. The new `Access.AuthKit` project, `AuthKitBearerTokenValidator` for user tokens, claim normalization, and startup registration behind `Auth:Provider` (sections 1-3).
2. `IUserProfileResolver` with a thin `WorkOsManagementClient`, provider-aware `OAuthFlows`, and the anonymous `GET v1/auth/configuration` endpoint (sections 4-6).

With `Auth:Provider = AuthKit` the Functions host starts, AuthKit users can call the API and register an account, clients can discover the provider, and the OpenAPI document follows the provider. Entra ID stays the default and behaves as before.

`Access.AuthKit.UnitTests` passes 65 of 65 tests, `Access.AzureAd.UnitTests` 36 of 36, and `Api.Functions.UnitTests` 40 of 40 (after the swagger fix in section 4). No test calls a live WorkOS environment.

## What Was Done

### 1. Configuration: shared binding and AuthKit rules

The options binding moved to `Backend.Core`, so both providers register it the same way and `Access.AuthKit` does not depend on `Access.AzureAd`:

| File | Change |
|---|---|
| `Backend.Core/src/Accessing/UserAuthenticationEntryPoint.cs` | New. `AddUserAuthenticationOptions(configuration)` binds `Auth`, rebuilds `PermissionMap`, registers the validator once (`TryAddEnumerable`), and calls `ValidateOnStart`. |
| `Backend.Core/src/Accessing/UserAuthenticationOptionsValidator.cs` | Moved from `Access.AzureAd`. The namespace is now `_42.Platform.Storyteller.Accessing`. |
| `Backend.Core/src/Accessing/AuthKitOptions.cs` | New `GetJwksUri()`: `JwksUri` when set, otherwise `{ApiBaseUrl}/sso/jwks/{ClientId}`. |
| `Backend.Core/src/Backend.Core.csproj` | References `Microsoft.Extensions.Configuration.Abstractions`, `DependencyInjection.Abstractions`, `Options`, and `Options.ConfigurationExtensions`. All of them were already in the catalog. |

`AddEntraIdUserAuthentication` now calls `AddUserAuthenticationOptions` instead of binding the section itself.

**PermissionMap and `:` in slugs.** Phase A left this open. The spec's direction (WorkOS slug ⇒ Storyteller scope) and its `Dictionary<string, string>` type are kept. After the binder runs, a `Configure` step replaces the map with every leaf under `Auth:AuthKit:PermissionMap`, using the leaf's relative path as the key. `storyteller:annotation-read` therefore binds as one entry from JSON objects, flat keys, and `__` environment variables. The rebuilt map uses ordinal comparison. A value can hold several scopes separated by spaces. That is an addition to the spec, and it costs nothing because `scp` is space-delimited anyway.

**AuthKit validation** (startup) now requires:

- `AuthKit.ClientId` (Phase A).
- A non-blank `AuthKit.Issuer`.
- A resolved JWKS URL that is absolute HTTPS. `HttpDocumentRetriever` refuses plain HTTP, so an HTTP URL would otherwise produce a 503 on every request.

`AuthKitDomain`, `ApiKey`, and `MachineOrganizationId` are still optional, as the spec says, until M2M lands. In this phase `ApiKey` enables the profile lookup (section 5), and `AuthKitDomain` enables the AuthKit OAuth flows and is returned by the discovery endpoint (sections 4 and 6).

The default `Issuer` stays `https://api.workos.com/`. The WorkOS sessions documentation gives that value for user access tokens, with the custom auth domain as the alternative. That settles the default for Open Question 1. Confirming it on a real device-flow token is still Phase C work.

### 2. Server-side token validation (`Access.AuthKit`)

New project `src/Platform/Storyteller/Access.AuthKit/src/Access.AuthKit.csproj`. The assembly is `42.Platform.Storyteller.Access.AuthKit`, the root namespace is `_42.Platform.Storyteller`, and it is in `42.mono.slnx`.

| File | Purpose |
|---|---|
| `AuthKitBearerTokenValidator.cs` | `IBearerTokenValidator` for AuthKit user tokens. |
| `AuthKitClaimNormalizer.cs` | Section 3. |
| `JsonWebKeySetRetriever.cs` | Internal `IConfigurationRetriever<JsonWebKeySet>`. AuthKit serves a bare JWKS rather than OpenID metadata, and IdentityModel has no public retriever for that. |
| `AuthKitEntryPoint.cs` | `AddAuthKitUserAuthentication(configuration)`. |

The entry class is `AuthKitEntryPoint` in `AuthKitEntryPoint.cs`, not `EntryPoint.cs`. `Access.ApiKeys` and `Access.AzureAd` already define `_42.Platform.Storyteller.EntryPoint`, and StyleCop SA1649 wants the file name to match the class.

`Directory.Packages.props` gains `Microsoft.IdentityModel.JsonWebTokens` and `Microsoft.IdentityModel.Protocols` at **8.18.0**. The spec expected no new packages. Both were already in the graph through `Microsoft.IdentityModel.Protocols.OpenIdConnect`, and they are now referenced directly because the project uses `JsonWebTokenHandler` and `ConfigurationManager<T>`. Their versions match the rest of the IdentityModel family.

**`AuthKitBearerTokenValidator`:**

- `JsonWebTokenHandler` validates the token. Claim types are not remapped, so `sub` stays `sub`.
- One `ConfigurationManager<JsonWebKeySet>` per validator, and the validator is a singleton. It caches the key set and refreshes it automatically. The second constructor takes an `IConfigurationManager<JsonWebKeySet>` for tests, as the Entra validator does.
- Validation parameters: `ValidAlgorithms = [RS256]`, `RequireExpirationTime`, `ValidateLifetime`, a 30 s clock skew, and audience checking only when `Audience` is set.
- Issuer check: ordinal and case-sensitive, but one trailing `/` may differ on either side. **This deviates from the spec's "exact" match.** WorkOS pages show the issuer both as `https://api.workos.com/` and as `https://api.workos.com`. Strict equality would turn a copy-paste difference into a 401 for every user. A lookalike host, a different case, a path suffix, or a double slash is still rejected.
- **Added guard:** `sub` must start with `user_`. The spec allows `Issuer` to be the custom auth domain, which is also the M2M issuer. Without the guard, an M2M token sharing that issuer and key set would be accepted as a user with `DefaultUserScopes`. M2M tokens get their own source in Phase D.
- **Added:** a `SecurityTokenSignatureKeyNotFoundException` result calls `RequestRefresh()`, so a rotated signing key is picked up on a later request. The manager rate-limits refreshes. This matches the JwtBearer handler. The Entra validator does not do this.
- Error handling follows Phase A. A blank or unreadable token returns null before any key fetch. Cancellation of the caller's token propagates. `InvalidOperationException`, `HttpRequestException`, `IOException`, `ArgumentException`, and any other `OperationCanceledException` while fetching keys become `BearerKeyRetrievalException`, which the middleware turns into a 503. An invalid token returns null, which becomes a 401.
- `ShowPII` is enabled only when `IHostEnvironment.IsDevelopment()`.
- The constructor throws if `Issuer` is blank, as a second check behind `ValidateOnStart`.

Only the user token source is implemented. The M2M source (`{AuthKitDomain}/oauth2/jwks`, `org_id == MachineOrganizationId`) is Phase D, as the phase table says.

**Startup registration.** `AddAuthKitUserAuthentication` registers:

- The shared options.
- `AuthKitClaimNormalizer` as a singleton.
- `IBearerClaimsNormalizer`, resolving to that same instance.
- `AuthKitBearerTokenValidator` as the singleton `IBearerTokenValidator`.
- The profile lookup services from section 5.

In `Program.cs`, `Auth:Provider = AuthKit` (any casing) now calls `AddAuthKitUserAuthentication` instead of throwing. A missing value or `EntraId` still selects Entra ID, and an unknown name still fails startup. `Api.Functions.csproj` references `Access.AuthKit`.

**`DEV_AUTH` path.** Phase A asked Phase B to run the active provider's normalizer on the debug decoder. A new `Backend.Core/src/Accessing/IBearerClaimsNormalizer.cs` (`BearerValidationResult Normalize(IReadOnlyList<Claim>)`) is registered by each provider:

- `EntraIdClaimNormalizer` (`Access.AzureAd`) returns the claims unchanged and applies the existing `azp`/`appid` machine rule through `EntraIdBearerTokenValidator.TryGetMachineId`.
- `AuthKitClaimNormalizer` is the section 3 normalizer.

`BearerAuthenticationMiddleware` now resolves `IBearerClaimsNormalizer` on the debug path. It no longer references `EntraIdBearerTokenValidator` or `IOptions<UserAuthenticationOptions>`. Both paths store identity from a `BearerValidationResult`. `MachineIdentity` is still set only when `IsMachine` is true and `MachineId` is non-empty.

### 3. Claim normalization (`AuthKitClaimNormalizer`)

User tokens only. Every result has `IsMachine = false`.

| Canonical claim | Rule |
|---|---|
| `sub`, `sid`, `org_id`, `role`, `permissions`, others | Passed through. |
| `name` | `name`, else `first_name` + `last_name`, else `given_name` + `family_name`, else `email`. Values are trimmed, and blank or space-only values are skipped. |
| `preferred_username` | `preferred_username`, else `email`. |
| `scp` | `DefaultUserScopes`, then each `permissions` entry looked up in `PermissionMap`, with every value split on spaces, tabs, or commas. Deduplicated (ordinal) in first-seen order and emitted as one space-separated claim. No claim is added when the result is empty. |

Decisions beyond the spec table:

- `first_name` / `last_name` are checked before the OIDC `given_name` / `family_name`, because the WorkOS JWT template examples use those names. WorkOS renders a missing value inside a string template as an empty string. The spec's own template therefore produces `" "` for a user with no names, and that blank value falls through to the next source.
- Incoming `scp`, `roles`, `ClaimTypes.Role`, any `*/scope` claim, `azp`, `name`, and `preferred_username` are dropped before the canonical values are added. A JWT template therefore cannot grant scopes or mark a machine directly.
- Unmapped permissions grant nothing. Slug lookup is ordinal and case-sensitive. The normalizer copies the map into an ordinal dictionary whatever comparer it was given.
- `Configuration.Secrets` in `DefaultUserScopes` is not blocked in code. The spec states it as the recommended default, and the documentation repeats that.

### 4. OpenAPI security definitions (`OAuthFlows`)

`Api.Functions/src/OpenApi/OAuthFlows.cs` now depends on the provider. The OpenAPI extension creates it with `Activator`, outside DI. The parameterless constructor therefore builds an `IConfiguration` from environment variables and passes it to an internal constructor that tests use. Environment variables cover both `Auth:TenantId` (Windows, `local.settings.json`) and `Auth__TenantId` (Linux app settings). The old code read only the first form.

- **Entra ID** (`Auth:Provider` missing or `EntraId`): the same implicit flow with eight `api://{clientId}/…` scopes, and the same client-credentials flow with `.default`.
- **AuthKit with `AuthKitDomain`:**
  - an authorization-code flow: `{AuthKitDomain}/oauth2/authorize`, token and refresh URL `{AuthKitDomain}/oauth2/token`, scopes `openid`, `profile`, `email`;
  - a client-credentials flow: `{AuthKitDomain}/oauth2/token`, no scopes.
- **AuthKit without `AuthKitDomain`:** a marked stand-in client-credentials flow only (see the fix below).

The `integrated` scheme is attached by `[OpenApiSecurity(..., Flows = typeof(OAuthFlows))]` on every operation, so an AuthKit deployment without a domain would otherwise publish an OAuth2 scheme with no flows. A new `OpenApi/Filters/OAuthSecuritySchemeDocumentFilter.cs`, registered in `OpenApiConfigurationOptions`, handles that case. When the scheme has no real flow, the filter:

- removes the scheme from `components`;
- removes its key from every operation requirement;
- drops requirements left empty.

The `manual` bearer scheme stays, as the spec asks. The filter matches requirement keys by reference id. A test pins that against the extension's own `DocumentHelper.GetOpenApiSecurityRequirement`.

**Fix after manual testing (2026-10-03).** With `Auth:Provider = AuthKit` and no `AuthKitDomain`, `GET /api/swagger.json` failed with `InvalidOperationException: Flow MUST be provided`. The cause: `DocumentHelper.GetSecurityOAuthScopes` reads the scopes of every OAuth2 requirement while the document is built, before any document filter runs, and it throws when the flows object has no flow.

The fix:

- `OAuthFlows` now sets a stand-in `ClientCredentials` flow (`https://placeholder.invalid/oauth2/token`) carrying the `x-storyteller-placeholder` extension.
- `OAuthFlows.IsPlaceholder` recognizes it, and the filter treats a placeholder like a missing flow. The stand-in never reaches the published document.

The earlier tests missed this. They built requirements only with Entra settings, and they fed the filter a hand-made document. A new test runs the extension's `GetOpenApiSecurityRequirement` and `GetOpenApiSecuritySchemes` on a sample operation whose flows type is AuthKit without a domain, and then applies the filter. Before the fix it failed with the same exception.

**Fix after manual testing (2026-10-06).** `GET /api/swagger.json` failed with `An item with the same key has already been added. Key: AccountCreate` inside `TypeVisitor.Visit`. One request builds the document. The extension keeps a single `IOpenApiHttpTriggerContext` for the process, and its schema dictionary is filled during that build. A second request that overlaps the first inserts `AccountCreate` again. `SerialOpenApiTriggerFunction` replaces the extension's trigger function and runs one render at a time.

**Deviations and limits, found while checking WorkOS Connect documentation:**

- **PKCE:** OpenAPI 3.0 has no field to mark a flow as PKCE. The Swagger UI page in this extension calls `SwaggerUIBundle` without `initOAuth`, so `usePkceWithAuthorizationCodeGrant` is never on. A public OAuth application, which WorkOS requires to use PKCE, therefore cannot complete the flow from Swagger UI. A confidential application, with its secret entered in Swagger UI, can. Turning PKCE on would need a custom Swagger UI script, which is not done.
- **Token issuer:** tokens from `{AuthKitDomain}/oauth2/*` are WorkOS Connect tokens. Their `iss` is the AuthKit domain, they are signed with `{AuthKitDomain}/oauth2/jwks`, their `aud` is the client ID or a resource indicator, and they carry `scope` instead of `permissions`. The Phase B validator has one token source (`Issuer` + `JwksUri`). It accepts Swagger UI tokens only when those settings point at the AuthKit domain, and that rejects device-flow tokens from `https://api.workos.com/`.
- **Client credentials:** client-credentials tokens are M2M, so the `user_` guard rejects them. The spec plans the AuthKit-domain source with M2M in Phase D. Until then, both AuthKit flows are documented but not usable against a deployment that also serves device-flow users.

### 5. Profile lookup on account registration (`IUserProfileResolver`)

New in `Backend.Core/src/Accessing/`:

- `IUserProfileResolver`: `Task<UserProfile?> ResolveAsync(string subject, CancellationToken)`.
- `UserProfile(string? UserName, string? Name)`. It is a separate file because of SA1402.

New in `Access.AuthKit`:

| File | Purpose |
|---|---|
| `WorkOsManagementClient.cs` | Typed `HttpClient` with base address `{ApiBaseUrl}/`. It sends `Authorization: Bearer {ApiKey}` per request and never logs it. `GetUserAsync(id)` calls `GET user_management/users/{escaped id}`: 404 returns null, and other failures throw `HttpRequestException`. `IsConfigured` is false without `ApiKey`, and a call then throws `InvalidOperationException`. This is the client the spec puts in Phase D, started here with the one call this phase needs. Retries through `Microsoft.Extensions.Http.Resilience` are left for Phase D, which adds the Connect calls. |
| `WorkOsUser.cs` | `id`, `email`, `first_name`, `last_name` from the WorkOS user object. |
| `AuthKitUserProfileResolver.cs` | Returns null without a management key or a subject, without calling WorkOS. Otherwise the user name is the trimmed `email`, and the name is `first_name last_name` (non-blank parts) or else `email`. It reuses `AuthKitClaimNormalizer.JoinNames`, so the token and the API produce the same name. |

`AddAuthKitUserAuthentication` adds `AddHttpClient<WorkOsManagementClient>()` and registers `IUserProfileResolver` → `AuthKitUserProfileResolver` as transient, because typed clients are transient. `Access.AuthKit.csproj` references `Microsoft.Extensions.Http`, which is already in the catalog. Entra ID registers no resolver.

**Spec wording:** the spec says `AuthKitBearerTokenValidator` "exposes" the resolver. It is a separate service instead, because validation and the management API have different lifetimes and secrets.

`AccessHttp` takes an optional `IUserProfileResolver? profileResolver = null`, like its other optional services. `PostAccount` now calls `HttpRequestDataExtensions.GetIdentityProfileAsync(resolver)`:

- Token claims win: `preferred_username` / `unique_name` / UPN, then `name`.
- The resolver runs only when one of them is missing or empty, and only when a resolver is registered. It fills only the missing value.
- Anything still missing throws `SecurityTokenException("Missing preferred_username claim.")` or `("Missing name claim.")`. Those are the same messages as before, which the error middleware turns into 401.
- Without a resolver (Entra ID), the outcome is the same as the old `GetIdentityUniqueName()` / `GetIdentityName()` path, including an empty claim being accepted. Those helpers are kept.

The lookup runs once per registration and is not cached, as the spec says. A WorkOS failure propagates as `HttpRequestException`, which `ExceptionHandlingMiddleware` turns into 500. Turning it into a missing-claim 401 would point at the wrong cause.

### 6. Discovery endpoint (`GET v1/auth/configuration`)

- `Api.Functions/src/V1/AuthConfigurationHttp.cs`: function `GetAuthConfiguration`, `AuthorizationLevel.Anonymous`, no scope check and no `[OpenApiSecurity]`.
- Constants: route `Definitions.Routes.Auth.V1.Configuration = "v1/auth/configuration"` and route id `Definitions.RouteIds.Access.GetAuthConfiguration`.
- OpenAPI tag: `Access`. A new tag would add a new client group to the generated SDKs for one call.
- `Api.Functions/src/V1/Models/AuthConfiguration.cs`: `Provider`, `ClientId`, `TenantId?`, `Scopes?`, `AuthKitDomain?`.

`AuthConfigurationHttp.Describe(options)` builds the response once per function instance from `UserAuthenticationOptions`:

- **Entra ID:** `TenantId`, `ClientId`, and the two scopes `sform` requests today (`api://{clientId}/User.Impersonation`, `api://{clientId}/Default.ReadWrite`).
- **AuthKit:** `AuthKit.ClientId` and, when set, `AuthKitDomain` without a trailing slash. Leftover Entra values are not returned, and there are no scopes, because AuthKit device authorization takes only `client_id`.

The management key, permission map, default scopes, and machine organization are never part of the model.

**Deviation:** property names are PascalCase (`Provider`, `ClientId`, …), not the spec's camelCase. The API serializes with `NoChangeNamingPolicy` and `DefaultNamingStrategy` everywhere. Null values are omitted (`WhenWritingNull`).

The bearer middleware still runs before this endpoint, so a request carrying an invalid bearer gets 401. Clients should call it without `Authorization`.

### Documentation

New `docs/Platform/Storyteller/authentication.md`. It covers:

- The provider switch.
- The Entra and AuthKit settings tables.
- The validation rules and the 401 / 503 behaviour.
- The claim mapping.
- The two scope modes. Account endpoints require `User.Impersonation`, which role-driven setups must map.
- `PermissionMap` slug syntax and a caveat about app-setting name restrictions on Linux plans.
- The AuthKit dashboard setup (issuer, JWT template with `aud`, `email`, `name`).
- The account-orphaning warning for switching providers.
- The `ApiKey` profile lookup at registration, the discovery endpoint with sample responses, and the provider-dependent OpenAPI flows with their current limits (sections 4-6).
- What is not available yet.

`local.settings.json` is unchanged. It stays strict JSON without commented AuthKit keys, and the documentation shows the keys instead.

## Behaviour changes

- `Auth:Provider = AuthKit` starts the host and validates AuthKit user tokens. It used to fail at startup.
- AuthKit settings now fail startup when `Issuer` is blank or the JWKS URL is not absolute HTTPS.
- Entra ID: valid and invalid tokens behave as in Phase A. The options validator moved assemblies and namespaces, but its Entra rules are unchanged. The debug decoder gives the same result through `EntraIdClaimNormalizer`.
- `PermissionMap` keys containing `:` now bind as single entries.
- New anonymous `GET v1/auth/configuration`.
- The OpenAPI `integrated` scheme follows the provider. Entra output is unchanged. `OAuthFlows` now also reads `Auth__TenantId`-style variables.
- AuthKit account registration can fill a missing email or name from WorkOS when `ApiKey` is set. Entra registration is unchanged.

## Left for later phases

- **Not assigned to a phase:**
  - The optional startup warning about Entra-shaped account IDs (spec section 5).
  - Accepting user tokens from the AuthKit domain next to device-flow tokens, which the Swagger UI flows need (section 4).
  - PKCE in Swagger UI.
- **Phase C:** the CLI `IAuthenticationService` refactor, AuthKit device flow, and the CLI side of discovery. `sform account` still signs in with Entra only.
- **Phase D:** the AuthKit-domain token source for M2M, `MachineCredentialKind.ClientCredentials`, `AuthKitMachineAccessService`, the rest of `WorkOsManagementClient` with retries, and normalizer rules for M2M tokens.
- **Phase E:** WorkOS user API keys.
- Open Questions 2 and 3 (M2M claims and audience) remain open.

## Tests

New xUnit + Shouldly project `src/Platform/Storyteller/Access.AuthKit/test` (65 tests), added to `42.mono.slnx`.

`AuthKitBearerTokenValidatorTests`:

- A valid user token yields the normalized `sub`, `org_id`, `name`, `preferred_username`, and `scp`, and is not a machine.
- With an audience configured, a matching `aud` is accepted. A different or missing `aud` returns null. Without a configured audience, any or no `aud` is accepted.
- Wrong issuers return null: a foreign host, a lookalike host, a different case, the `user_management/{client}` path, and a double trailing slash. A single trailing-slash difference is accepted in both directions.
- Expiry 10 s ago is accepted. Expiry 2 min ago is rejected, which would pass under Entra's five-minute skew. A future `nbf` is rejected.
- HS256, RS512 signed with the right key, and `alg: none` return null.
- An unknown `kid` returns null and requests one key refresh.
- `client_…`, unprefixed, or wrong-case subjects, and a missing `sub`, return null.
- An unreadable token returns null without fetching keys. A key-retrieval failure throws `BearerKeyRetrievalException`. Cancellation propagates.
- Against a stub `HttpMessageHandler` serving JWKS JSON through the real `ConfigurationManager` and `JsonWebKeySetRetriever`, the key set is fetched once from `https://api.workos.com/sso/jwks/client_123` and reused. A 500 from the endpoint becomes `BearerKeyRetrievalException`.
- A blank issuer makes the constructor throw.

`AuthKitClaimNormalizerTests`:

- Name fallbacks: trimmed `name`, a blank template `name`, first name only, given/family names, and email only.
- `preferred_username` comes from `email`, and an existing value wins.
- No profile claims means no `name` or `preferred_username` claim.
- Default scopes plus mapped permissions, including a multi-scope value and deduplication. Unmapped permissions are ignored. No scopes means no `scp`. Lookup is case-sensitive.
- Incoming `scp`, `roles`, role, `*/scope`, and `azp` are dropped. Other claims pass through. The result is never a machine.

`AuthKitRegistrationTests`:

- `PermissionMap` slugs with one or two colons bind from flat keys and from a JSON object.
- `GetJwksUri` covers the default, a custom `ApiBaseUrl` with a trailing slash, and an explicit `JwksUri`.
- The validator accepts AuthKit defaults. It rejects a blank issuer, an HTTP or relative JWKS URL, and an HTTP `ApiBaseUrl`.
- Host start registers `AuthKitBearerTokenValidator`, and the `IBearerClaimsNormalizer` is the same `AuthKitClaimNormalizer` instance. It also resolves `AuthKitUserProfileResolver` and an unconfigured `WorkOsManagementClient`. A missing `AuthKit:ClientId` throws `OptionsValidationException`.

`AuthKitUserProfileResolverTests` run against a stub `HttpMessageHandler`:

- A user maps to email and `first last`. The request is `GET https://api.workos.com/user_management/users/user_01` with `Bearer sk_…`.
- Blank names fall back to the email.
- A 404 returns null. A 500 throws `HttpRequestException` whose message does not contain the key.
- A missing or blank key returns null without a request.
- The user id is escaped, and a custom `ApiBaseUrl` is honoured.
- `GetUserAsync` without a key throws.

`Access.AzureAd.UnitTests` gains `EntraIdClaimNormalizerTests` (a user when `azp` matches the client ignoring case, with the same claim list; a machine otherwise). It asserts that `AddEntraIdUserAuthentication` registers `EntraIdClaimNormalizer` and no `IUserProfileResolver`. The existing options tests run unchanged against the moved validator.

`Api.Functions.UnitTests` registers the provider's normalizer in `FunctionTestDoubles`. It adds a debug-decode test where an AuthKit token gets `name`, `preferred_username`, and `scp` from the AuthKit normalizer, a foreign `azp` does not set `MachineIdentity`, and the validator is not called. It also adds:

- `IdentityProfileTests` (6):
  - Complete claims skip the resolver.
  - Missing claims are filled.
  - Only the missing one is taken.
  - Without a resolver, the old message is thrown.
  - A resolver without a profile reports the missing claim.
  - The UPN fallback still counts.
- `AuthConfigurationHttpTests` (5):
  - The Entra shape with the CLI scopes.
  - The AuthKit shape, with a trimmed domain and no leftover Entra values.
  - The domain is omitted when unset.
  - Serialized JSON contains no key, machine organization, or permission slug.
  - The function returns `OkObjectResult`.
- `OAuthFlowsTests` (10):
  - Entra flows with a missing and an explicit provider.
  - AuthKit with a domain: authorization code and client credentials.
  - AuthKit without a domain: only the marked stand-in. Entra ID and AuthKit with a domain are not placeholders.
  - The filter removes a flowless scheme and its requirements, and keeps one with flows.
  - The extension references `integrated` by id and instantiates `OAuthFlows`.
  - The extension builds an AuthKit-without-domain operation, and the filter leaves only `manual` (the swagger regression).

## Verification

```
dotnet test src/Platform/Storyteller/Access.AuthKit/test/Access.AuthKit.UnitTests.csproj --nologo -v q
dotnet test src/Platform/Storyteller/Access.AzureAd/test/Access.AzureAd.UnitTests.csproj --nologo -v q
dotnet test src/Platform/Storyteller/Api.Functions/test/Api.Functions.UnitTests.csproj --nologo -v q
```

Results: **65 passed**, **36 passed**, and **40 passed**, with 0 failures.

`Api.Web`, `DbCreator`, `Access.Certificates.UnitTests`, and `Backend.CosmosDb.UnitTests` build in Debug, and `Api.Functions` also builds in Release, all with 0 errors. A no-incremental build reports no warnings from the new or changed lines. The remaining warnings on touched files were already there before this phase:

- `Program.cs`: SA1210, SA1512, SA1515, SA1005.
- `OpenApiConfigurationOptions.cs`: SA1210 on the existing using order.
- `AccessHttp.cs`: SA1515 on an existing TODO comment.
- `HttpRequestDataExtensions.cs`: CS0162 on the `DEV_AUTH` branches.
- NU1510.

`dotnet build src/Platform` reports one error: NU1008 in `Api.Functions/src/obj/.../WorkerExtensions/WorkerExtensions.csproj`. The traversal glob `**\*.*?proj` picks up that generated project once the Functions app has been built. It is not related to this change.

Tokens in the tests are signed locally, and WorkOS HTTP calls go to stub handlers. No token or user from a real WorkOS environment was used.

Two attempts were made to start the Functions host locally (`func start` with `Auth:Provider = AuthKit`) to check `v1/auth/configuration` and `swagger.json`. Both failed before the worker ran. The installed Core Tools (Chocolatey, 4.0.7032) are x86-only and launch `C:\Program Files (x86)\dotnet`, which has no .NET 10 runtime. Setting `languageWorkers:dotnet-isolated:defaultExecutablePath` did not change that. The rendered Swagger document and the endpoint were therefore checked only through unit tests and the extension's `DocumentHelper`.
