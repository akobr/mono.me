# WorkOS AuthKit Authentication for Storyteller

## Problem

Storyteller authenticates users with Microsoft Entra ID only, and that choice is hard-wired: the token validator, the OpenAPI OAuth flows, the admin CLI and the machine-access identity rules all assume Entra. We want to support [WorkOS AuthKit](https://workos.com/docs) as an alternative user identity provider, and pick one per deployment through configuration.

This spec answers three questions and turns the answers into an implementation plan:

1. How to add AuthKit user authentication and switch between Entra ID and AuthKit through configuration.
2. Whether the `sform` CLI can keep a CLI-triggered sign-in (today it uses Entra device code) with AuthKit.
3. Whether AuthKit has an equivalent of API keys, client credentials or mTLS certificates that can be used for machine access.

## Current State

**Hosting.** `Api.Functions` (`src/Platform/Storyteller/Api.Functions/src`) is a .NET 10 isolated-worker Azure Functions app. `Program.cs (24-25)` registers `ExceptionHandlingMiddleware`, then `MachineAuthenticationMiddleware`. `Api.Web` is outdated and stays out of scope, as in the mTLS spec.

**User token validation** lives in static helpers in `Api.Functions/src/Security/HttpRequestDataExtensions.cs`:

* `GetClaims()` (lines 18-62) returns cached claims from `FunctionContext.Items[CachedClaims]` if they exist. Otherwise it reads `Authorization: Bearer`, validates it with `ValidateAccessToken()` and caches the resulting claims. Under `DEV_AUTH` (Debug builds) the token is only decoded, never validated.
* `ValidateAccessToken()` (lines 227-263) is Entra-specific. It reads `Auth:TenantId` / `Auth:ClientId` straight from environment variables, uses authority `https://login.microsoftonline.com/{tenant}/v2.0` and audiences `api://{clientId}` or `{clientId}`, and accepts issuers starting with `sts.windows.net` or `login.microsoftonline.com`. It is not injectable, has no JWKS caching beyond a per-call `ConfigurationManager`, and sets `ShowPII = true`.
* `CheckScope()` / `TryCheckScope()` collect scopes from `scp`, `roles`, `ClaimTypes.Role` and `*/scope` claims and strip the `App.` prefix.
* `TryGetApplicationIdentity()` treats a token as a machine when `azp`/`appid` is present and differs from `Auth:ClientId`. The Entra `azp` of a user token delegated through the CLI equals the API's own client ID, which is why the comparison works.
* `GetIdentityUniqueId()` uses `sub`. `GetIdentityUniqueName()` uses `preferred_username` / `unique_name` / UPN. `GetIdentityName()` uses `name`. `AccessHttp.PostAccount` uses all three to create the Cosmos `Account`.

**Authorization** is Storyteller's own. `CheckAccessToAsync()` resolves either `VerifyAccessForMachineAsync(org, project, appId)` for machines or `GetAccountRoleAsync(sub, accessPointKey)` for users against Cosmos. The IdP only proves *who* the caller is and which coarse scopes (`Default.*`, `Annotation.*`, `Configuration.*`, `User.Impersonation`) the caller holds.

**OpenAPI.** `Api.Functions/src/OpenApi/OAuthFlows.cs` hard-codes the Entra implicit and client-credentials flows.

**Machine access** has two independent layers:

* *Storyteller-native credentials*, which work without any IdP: structured API keys `2s.<base64url(org:project:id)>.<secret>` (`Access.ApiKeys`), and per-machine or shared mTLS certificates issued by Storyteller's own CA (`Access.Certificates`, `Access.Certificates.Azure.KeyVault`). `MachineAuthenticationMiddleware` validates them, applies the per-project `MachineAuthenticationPolicy` (`ApiKey | Certificate | CertificateAndApiKey`) and synthesizes `azp`/`sub`/`roles` claims. `PolicyAwareMachineAccessService` is the registered `IMachineAccessService`.
* *IdP-issued client credentials*: `Access.AzureAd` creates an Entra app registration, secret and app-role assignment through Microsoft Graph, and `Access.Keycloak` creates a confidential client. Neither is wired in `Program.cs` today (`AddAzureAdMachineAccess()` is commented out). Their JWTs would reach the API as bearer tokens and be classified as machines by `TryGetApplicationIdentity()`.

**CLI** (`src/Platform/Cli/src`):

* `Authentication/AuthenticationService.cs` builds an MSAL `IPublicClientApplication` (`TenantId`, `ClientId` from `app.config.json` `authentication` section) with an encrypted `MsalCacheHelper` cache in `~/.42for.net/msal.cache`, and requests the scopes `api://{clientId}/User.Impersonation` and `api://{clientId}/Default.ReadWrite`.
* `IAuthenticationService` leaks MSAL types (`IPublicClientApplication`, `AuthenticationResult`).
* `Commands/Account/AccountCommand.cs (109-174)` runs `AcquireTokenWithDeviceCode` when a silent acquisition throws `MsalUiRequiredException`.
* `Startup.ConfigureStorytellerSdk` feeds `AccessTokenFactory` from `GetAuthenticationAsync()`.

## AuthKit Capabilities (research summary)

| Need | AuthKit feature | Fit |
|---|---|---|
| User sign-in for the API | AuthKit access token (JWT, RS256). Claims: `sub` (`user_…`), `sid`, `org_id`, `role`, `permissions[]`, `iat`, `exp`, and `client_id` on newer tokens. Issuer is `https://api.workos.com/` or the custom auth domain. JWKS: `https://api.workos.com/sso/jwks/{clientId}`. **JWT templates** can add `aud`, `email`, `name` and other claims. | Good. Validate locally with JWKS. |
| CLI-triggered sign-in | **CLI Auth**, which is the OAuth 2.0 Device Authorization Grant. `POST https://api.workos.com/user_management/authorize/device` (`client_id`) returns `device_code`, `user_code`, `verification_uri(_complete)`, `expires_in` (~300 s) and `interval` (~5 s). Then poll `POST https://api.workos.com/user_management/authenticate` with `grant_type=urn:ietf:params:oauth:grant-type:device_code`. Poll errors are `authorization_pending`, `slow_down`, `access_denied` and `expired_token`. Refresh uses `grant_type=refresh_token`, and **refresh tokens rotate** on every exchange. This is a public client with no secret. | Same UX as the Entra device code flow. |
| Machine client credentials (≈ Entra app registration or Keycloak client) | **M2M applications (WorkOS Connect)**. Created via API with `POST /connect/applications {name, application_type:"m2m", organization_id, scopes[]}`. Secrets come from `POST /connect/applications/{id}/client_secrets` (at most 5 per app, shown once, no expiry) and are revoked with `DELETE /connect/client_secrets/{id}`. Machines use `grant_type=client_credentials` at `https://<authkit_domain>/oauth2/token` and get a short-lived JWT with `org_id`. Verify with JWKS at `https://<authkit_domain>/oauth2/jwks`, or with the Token Introspection API. | Direct equivalent of `Access.AzureAd` / `Access.Keycloak`. |
| API keys | **WorkOS API Keys**, which are opaque and long-lived. Owners are an organization or a user (the user variant also holds `owner.organization_id`). Keys carry `permissions[]` and optional `expires_at`. Validation is `POST /api_keys/validations {value}`, **a network call to WorkOS on every request**. Management endpoints: `/organizations/{id}/api_keys`, `/user_management/users/{id}/api_keys`, `DELETE /api_keys/{id}` and `POST /api_keys/{id}/expire`. | Weaker than Storyteller's own `2s.` keys, which are validated locally with a point read and are project-scoped. Useful only as optional *personal access tokens* for users (Phase E). |
| mTLS / client certificates | **None.** No client-certificate auth, `private_key_jwt` or `tls_client_auth` is documented for M2M apps; the only credential is a client secret. | Keep Storyteller's own CA. It is IdP-independent and keeps working unchanged under AuthKit. |

Sources: [CLI Auth](https://workos.com/docs/authkit/cli-auth), [Sessions / access token](https://workos.com/docs/authkit/sessions), [M2M applications](https://workos.com/docs/authkit/connect/m2m), [Connect applications API](https://workos.com/docs/reference/workos-connect/applications), [API keys](https://workos.com/docs/authkit/api-keys), [API keys reference](https://workos.com/docs/reference/authkit/api-keys), [Verifying WorkOS access tokens](https://workos.com/blog/verify-workos-access-tokens-in-your-own-api), [API keys vs M2M](https://workos.com/blog/api-keys-vs-m2m-applications).

### Answers to the three questions

1. **Switchable providers.** Yes. Move token validation behind an injectable `IBearerTokenValidator` with two implementations, choose one with `Auth:Provider`, and normalize both providers' claims into the shape the existing helpers already expect. Sections 1-4.
2. **CLI-triggered sign-in.** Yes. AuthKit CLI Auth is a standard device authorization grant, so `sform account` keeps the same experience: print a code and URL, poll, then cache the tokens. Only the plumbing changes: raw HTTP instead of MSAL, and a refresh token instead of the MSAL cache. Section 6.
3. **Machine access equivalents.**
   * *Client credentials:* yes, **M2M applications**, the counterpart of `Access.AzureAd` and `Access.Keycloak`. They can be created fully through the API, so a new `Access.AuthKit` project can implement `IMachineAccessService`. Section 7.
   * *API keys:* yes, but Storyteller's own `2s.` keys are a better machine credential (local validation, project scope). WorkOS keys are worth adopting only for user-owned personal tokens (optional Phase E).
   * *mTLS:* no AuthKit equivalent. Storyteller's native API keys and mTLS certificates are independent of the user IdP and keep working when `Auth:Provider = AuthKit`.

## Proposed Changes

### 1. Configuration: `Auth` section and provider switch

Replace the ad-hoc `Environment.GetEnvironmentVariable("Auth:…")` reads with a bound options class in `Backend.Core/src/Accessing/UserAuthenticationOptions.cs`:

```csharp
public enum IdentityProviderKind { EntraId = 0, AuthKit }

public class UserAuthenticationOptions
{
    public const string SectionName = "Auth";

    public IdentityProviderKind Provider { get; set; } = IdentityProviderKind.EntraId;

    // Legacy flat keys stay valid: Auth:TenantId / Auth:ClientId / Auth:AppRoles:* bind here.
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public Dictionary<string, string> AppRoles { get; set; } = new();

    public AuthKitOptions AuthKit { get; set; } = new();
}

public class AuthKitOptions
{
    public string ClientId { get; set; } = string.Empty;          // client_… (user management / CLI Auth)
    public string Issuer { get; set; } = "https://api.workos.com/"; // or the custom auth domain
    public string? JwksUri { get; set; }                           // default https://api.workos.com/sso/jwks/{ClientId}
    public string? Audience { get; set; }                          // set when a JWT template adds "aud"; null => audience not validated
    public string AuthKitDomain { get; set; } = string.Empty;      // https://<subdomain>.authkit.app or custom; used for Connect/M2M
    public string? ApiKey { get; set; }                            // sk_… management key; resolved through Key Vault binding, never in plain settings
    public string ApiBaseUrl { get; set; } = "https://api.workos.com";
    public string? MachineOrganizationId { get; set; }             // org_… that owns M2M apps (section 7)
    public string[] DefaultUserScopes { get; set; } = [];          // scopes granted to every signed-in user, see section 3
    public Dictionary<string, string> PermissionMap { get; set; } = new(); // WorkOS permission slug => Storyteller scope
    public string ApiKeyScheme { get; set; } = "WorkOS";           // Phase E only
}
```

Binding: `services.Configure<UserAuthenticationOptions>(configuration.GetSection("Auth"))`. `local.settings.json` flat keys (`Auth:Provider`, `Auth:AuthKit:ClientId`, …) map to this without changes. When `Provider` is missing it defaults to `EntraId`, so existing deployments behave exactly as today.

Validation runs on startup (`ValidateOnStart`): `EntraId` requires `ClientId`/`TenantId`, and `AuthKit` requires `AuthKit.ClientId`. `AuthKit.ApiKey` and `AuthKit.AuthKitDomain` are required only once M2M machine access (section 7) is enabled.

**One active user provider per deployment.** Running both at once would have accounts keyed by two unrelated `sub` namespaces (section 5). The validator abstraction dispatches on the configured provider, not on the token's issuer. Accepting several issuers at once is possible later but is deliberately not part of this spec.

### 2. Server-side token validation abstraction

New types in `Backend.Core/src/Accessing/`:

```csharp
public interface IBearerTokenValidator
{
    // null => token not valid for this deployment (401). Throws only on transient faults (JWKS unreachable).
    Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default);
}

public sealed record BearerValidationResult(
    IReadOnlyList<Claim> Claims,       // already normalized, see section 3
    bool IsMachine,
    string? MachineId);                // == MachineAccess.Id when IsMachine
```

Implementations:

* **`Access.AzureAd/src/EntraIdBearerTokenValidator.cs`**, a move of today's `ValidateAccessToken()` with three changes: it uses `UserAuthenticationOptions` instead of environment variables, keeps a **singleton** `ConfigurationManager<OpenIdConnectConfiguration>` (JWKS cached and refreshed automatically), and sets `ShowPII` only in Development. It moves the machine detection currently in `TryGetApplicationIdentity()` (`azp`/`appid` ≠ `ClientId`) into `IsMachine`/`MachineId`.
* **`Access.AuthKit/src/AuthKitBearerTokenValidator.cs`** (new project, section 7) validates with `JsonWebTokenHandler` against **two token sources**:
  * user tokens: issuer `AuthKit.Issuer`, JWKS `AuthKit.JwksUri`;
  * M2M tokens (only when `AuthKitDomain` is set): issuer `AuthKit.AuthKitDomain`, JWKS `{AuthKitDomain}/oauth2/jwks`.

  Both keep a singleton `ConfigurationManager<JsonWebKeySet>`-style cache (`Microsoft.IdentityModel.Protocols` + `JsonWebKeySet` retriever). Validation parameters: RS256 only, `ValidateLifetime`, clock skew 30 s, `ValidateAudience` only when `Audience` is configured, and the issuer must equal the configured value exactly.

Registration: `AddEntraIdUserAuthentication()` / `AddAuthKitUserAuthentication(configuration)`, each registering `IBearerTokenValidator` as a singleton. `Program.cs` switches on `Auth:Provider`:

```csharp
var authOptions = context.Configuration.GetSection(UserAuthenticationOptions.SectionName).Get<UserAuthenticationOptions>() ?? new();
switch (authOptions.Provider)
{
    case IdentityProviderKind.AuthKit: services.AddAuthKitUserAuthentication(context.Configuration); break;
    default: services.AddEntraIdUserAuthentication(context.Configuration); break;
}
```

**New `BearerAuthenticationMiddleware`** (`Api.Functions/src/Security/`), registered **after** `MachineAuthenticationMiddleware`:

```
ExceptionHandlingMiddleware → MachineAuthenticationMiddleware → BearerAuthenticationMiddleware
```

* If `CachedClaims` is already set (a machine credential was accepted), or there is no `Bearer` header, it passes through. Anonymous endpoints keep working, and endpoints that need identity still fail in `GetRequiredClaim`, as today.
* Otherwise it calls `IBearerTokenValidator`. A `null` result is a 401 response (same shape as `MachineAuthenticationMiddleware.RespondUnauthorizedAsync`). On success it stores `Claims` in `CachedClaims`, and `MachineId` in `FunctionContextItemKeys.MachineIdentity` when `IsMachine`.
* JWKS retrieval failures return 503 with `Retry-After`, not 401, so clients don't discard valid tokens.
* Under `DEV_AUTH` the middleware decodes without validation, preserving today's Debug behaviour. The normalizer (section 3) still runs so local AuthKit tokens resolve `name` / `preferred_username`.

`HttpRequestDataExtensions` changes:

* `GetClaims()` becomes a pure read of `CachedClaims` (plus the existing `Identities` fallback). The inline JWT parsing and `ValidateAccessToken()` are removed.
* `TryGetApplicationIdentity()` / `IsApplicationIdentity()` read `FunctionContextItemKeys.MachineIdentity`. This is already set by `MachineAuthenticationMiddleware` and is now also set by the bearer middleware, which removes the static `ClientId` field and the `// TODO: [P1] find how to best detect application identity`.
* `CheckScope()` / `TryCheckScope()` are unchanged. They keep reading `scp`/`roles`, which the normalizer guarantees.

### 3. Claim normalization

Each validator emits the same canonical claim set, so no endpoint code changes:

| Canonical claim | Entra ID source | AuthKit user source | AuthKit M2M source |
|---|---|---|---|
| `sub` | `sub` | `sub` (`user_…`) | `sub` (M2M `client_id`) |
| `azp` (machines only) | `azp`/`appid` | not emitted | `client_id` / `sub` |
| `name` | `name` | `name` from JWT template, else `given_name family_name`, else email | n/a |
| `preferred_username` | `preferred_username`/`upn` | `email` from JWT template | n/a |
| `scp` / `roles` | as issued | `permissions[]` mapped through `PermissionMap`, plus `DefaultUserScopes` | `scope`/`permissions` mapped through `PermissionMap` |
| `org_id` | n/a | passed through | passed through |

**AuthKit dashboard setup (documented, not code):** add a JWT template

```json
{ "aud": "https://storyteller.42for.net", "email": "{{ user.email }}", "name": "{{ user.first_name }} {{ user.last_name }}" }
```

and set `Auth:AuthKit:Audience` to the same value. Without the template, `PostAccount` could not fill `UserName`/`Name`. As a fallback, `AuthKitBearerTokenValidator` exposes an `IUserProfileResolver` that `AccessHttp.PostAccount` calls **only on account registration**: `GET /user_management/users/{sub}` with the management key. The result is not cached per request, and it is a one-time cost per user.

**Scopes for AuthKit users.** Entra users obtain `User.Impersonation`/`Default.ReadWrite` because the CLI requests them as delegated scopes, and real per-project authorization happens afterwards in `CheckAccessToAsync` against Cosmos roles. AuthKit has no per-resource delegated scopes; its `permissions` come from the organization role. Two supported modes:

* **Default (recommended):** `DefaultUserScopes = ["User.Impersonation", "Default.Read", "Default.ReadWrite", "Annotation.Read", "Annotation.ReadWrite", "Configuration.Read", "Configuration.ReadWrite"]`. Every authenticated user has the coarse scopes, and project access is still enforced by Storyteller's account roles, which is equivalent to today's Entra behaviour. `Configuration.Secrets` is deliberately **not** in the default. It must come from a mapped WorkOS permission (for example `storyteller:configuration-secrets`).
* **Role-driven:** leave `DefaultUserScopes` empty and define WorkOS permissions such as `storyteller:annotation-read`, mapped through `PermissionMap` (`"storyteller:annotation-read": "Annotation.Read"`, …). This requires users to be members of an organization with a role, so that `permissions` is populated.

### 4. OpenAPI security definitions

`OAuthFlows` becomes provider-aware, reading `UserAuthenticationOptions` from environment/config because it is instantiated by the OpenAPI extension via a parameterless constructor:

* `EntraId`: today's definitions, unchanged.
* `AuthKit`: `ClientCredentials` with `TokenUrl = {AuthKitDomain}/oauth2/token`, plus `AuthorizationCode` with PKCE against `{AuthKitDomain}/oauth2/authorize` / `{AuthKitDomain}/oauth2/token`. This requires a first-party Connect OAuth application registered for Swagger UI. If `AuthKitDomain` is empty, only the bearer scheme is documented.

The existing `ApiKeySecuritySchemeDocumentFilter` and `MtlsSecuritySchemeDocumentFilter` are unaffected.

### 5. Accounts and switching an existing deployment

`Account.Id` is the IdP `sub`. Entra and AuthKit subjects are unrelated (`00000000-…` vs `user_01H…`), so **flipping `Auth:Provider` on a populated deployment orphans every existing account**, and access maps, ownership and the `author` stamped on annotations (`GetAuthor()` → `account: {sub}`) stop resolving.

In scope:

* Document this prominently and log a warning at startup when `Provider = AuthKit` and the `core` container contains Entra-shaped account IDs. This check is optional and cheap: one `SELECT TOP 1` on accounts.
* Tip: AuthKit can itself federate to Microsoft (Microsoft OAuth social login or an Entra SSO connection per organization), so users can keep signing in with their Microsoft identity through AuthKit. Their `sub` still changes, though.

Out of scope, with a follow-up spec if needed: an account migration tool (`DbCreator` sub-command) that re-keys `Account` documents by matching `preferred_username` (Entra) to WorkOS user email, optionally setting WorkOS `external_id` to the old Entra object ID.

### 6. CLI: provider-agnostic authentication with AuthKit device flow

**Configuration** (`app.config.json`, `AuthenticationOptions`):

```jsonc
"authentication": {
  "provider": "EntraId",            // EntraId | AuthKit
  "tenantId": "common",             // EntraId
  "clientId": "303a7632-…",         // EntraId app ID or AuthKit client_… ID
  "authKitApiBaseUrl": "https://api.workos.com" // AuthKit, optional
}
```

**Server discovery (recommended).** Add an anonymous `GET v1/auth/configuration` endpoint to `Api.Functions` that returns `{ provider, clientId, tenantId?, scopes?, authKitDomain? }` built from `UserAuthenticationOptions`. The response contains public values only and never the management key. The CLI calls it when `authentication.provider` is absent and caches the result next to `access.default.json`. With discovery, one `sform` build can talk to both an Entra deployment and an AuthKit deployment just by changing `general.baseUrl`.

**Abstraction.** Replace the MSAL-leaking `IAuthenticationService` with:

```csharp
public interface IAuthenticationService
{
    Task<SignedInUser?> GetSignedInUserAsync(CancellationToken ct = default);       // silent; null => login needed
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);              // silent refresh
    Task<SignedInUser> LoginWithDeviceCodeAsync(Func<DeviceCodePrompt, Task> onPrompt, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
}

public sealed record DeviceCodePrompt(string UserCode, Uri VerificationUri, Uri? VerificationUriComplete, TimeSpan ExpiresIn);
public sealed record SignedInUser(string Id, string UserName, string? Name);
```

* **`EntraIdAuthenticationService`**: today's `AuthenticationService`, wrapped. MSAL exceptions are translated to a CLI `AuthenticationException` with a reason (`Cancelled`, `Expired`, `Denied`, `ServiceError`).
* **`AuthKitAuthenticationService`** (new, no new NuGet dependency, `HttpClient`):
  1. `POST {ApiBaseUrl}/user_management/authorize/device` with `client_id`, then invoke `onPrompt`. The CLI prints the code and `verification_uri_complete`, and can open the browser with `Process.Start` when a `--browser` flag is set.
  2. Poll `POST {ApiBaseUrl}/user_management/authenticate` with `grant_type=urn:ietf:params:oauth:grant-type:device_code`, `device_code` and `client_id` every `interval` seconds. `authorization_pending` means continue, `slow_down` means `interval += 1`, `access_denied` means Denied, `expired_token` means Expired. Stop at `expires_in`.
  3. On success, store `{ refresh_token, access_token, access_token_exp, user{id,email,first_name,last_name}, organization_id }`.
  4. `GetAccessTokenAsync()` returns the cached access token while `exp - 60 s` is in the future. Otherwise it refreshes with `grant_type=refresh_token` and **atomically replaces** the stored refresh token, because tokens rotate. `invalid_grant` clears the store and returns `null`.
  5. `LogoutAsync()` deletes the store. Server-side session revocation via `sid` is optional and not required.
* **Secure storage:** reuse `Microsoft.Identity.Client.Extensions.Msal.Storage` (already referenced) with its own `StorageCreationProperties` (`authkit.cache`, same `~/.42for.net` directory, DPAPI on Windows, Keychain on macOS and libsecret on Linux, with the same plaintext `DEBUG && !TESTING` exception as MSAL). This gives cross-platform encrypted storage without a new dependency. A `SemaphoreSlim` plus a file lock guards concurrent refreshes from parallel `sform` processes.
* **Organization selection:** if `authenticate` returns `organization_selection_required` (the user belongs to several WorkOS orgs), the CLI lists the orgs and retries with `grant_type=urn:workos:oauth:grant-type:organization-selection` and `pending_authentication_token`. Storyteller does not need `org_id` for authorization, so the only reason to pick an org is role-driven scopes (section 3). With `DefaultUserScopes`, the first org is fine.

`Startup` registers the implementation by provider. `ConfigureStorytellerSdk` resolves `IAuthenticationService` from DI instead of `new`-ing it. `AccountCommand`, `AccountLogoutCommand` and `AccountRegisterCommand` use the new interface: `LoginWithDeviceCodeAsync` replaces the `MsalUiRequiredException` → `AcquireTokenWithDeviceCode` path, and the prompt callback renders `Console.WriteHeader("Sign in")` with the code and URL, as today.

### 7. Machine access with AuthKit M2M applications (`Access.AuthKit`)

New project `src/Platform/Storyteller/Access.AuthKit/src/Access.AuthKit.csproj` (`42.Platform.Storyteller.Access.AuthKit`, root namespace `_42.Platform.Storyteller`), mirroring `Access.Keycloak`:

* `AuthKitOptions` binding (section 1) and a typed `WorkOsManagementClient` (`IHttpClientFactory`, `Authorization: Bearer {ApiKey}`, Polly retry on 429/5xx through `Microsoft.Extensions.Http.Resilience`, or the Polly package already central). It covers `POST/DELETE /connect/applications`, `POST/GET /connect/applications/{id}/client_secrets`, `DELETE /connect/client_secrets/{id}`, `GET /user_management/users/{id}` and (Phase E) `POST /api_keys/validations`. It is a thin client rather than the WorkOS .NET SDK, to avoid SDK coverage gaps for Connect / device flow and to match the Keycloak implementation style.
* `AuthKitBearerTokenValidator` (section 2).
* **`AuthKitMachineAccessService : IMachineAccessService`**:
  * `CreateMachineAccessAsync(model)`: `POST /connect/applications` with `name = 42.sform.{org}.{project}.{guid:N}`, `application_type = "m2m"`, `organization_id = MachineOrganizationId`, `scopes = mapped(model.Scope)` (the inverse of `PermissionMap`, for example `DefaultReadWrite` → all read/write slugs) and `description = organization=…|project=…|scope=…[|annotation=…]` (the same convention as Entra/Keycloak). Then `POST …/client_secrets`. Returns `MachineAccess { Id = client_id, ObjectId = application id, AccessKey = secret, Scope = … }`.
  * `ResetMachineAccessAsync(objectId, …)`: list the secrets, mint a new one, then delete the old ones. This order avoids hitting the limit of 5, and the machine is never without a valid secret mid-operation.
  * `DeleteMachineAccessAsync(objectId, …)`: `DELETE /connect/applications/{objectId}`.
* **Why one configured `MachineOrganizationId` rather than one WorkOS organization per Storyteller organization:** Storyteller already enforces org/project isolation itself (`VerifyAccessForMachineAsync(org, project, client_id)` against the Cosmos `MachineAccessEntity`), and WorkOS orgs are billed and managed entities. A per-organization mapping (WorkOS `external_id = storyteller org`) can be added later without changing the token path. As defense in depth, the validator additionally requires `org_id == MachineOrganizationId` on M2M tokens.

**Plugging into the policy model.** Extend `MachineCredentialKind` (`Abstractions.Access/src/MachineCredentialKind.cs`) with `ClientCredentials`, which means an IdP-issued client ID and secret exchanged for a JWT:

* `PolicyAwareMachineAccessService` gets an optional `IIdentityProviderMachineAccessService`. This is a marker interface extending `IMachineAccessService`, implemented by `AuthKitMachineAccessService` and later retrofittable to the AzureAd and Keycloak services. When the resolved project policy is `ClientCredentials`, creation is routed there and the result is stamped with `CredentialKind = ClientCredentials`. If the service is not registered, creation fails with a clear 400 (`"ClientCredentials machine access is not configured for this deployment"`).
* Reset/Delete must route by the **stored** credential kind, not by the current policy (the policy may have changed since creation). `CosmosAccessService.ResetMachineAccessAsync` / `DeleteMachineAccessAsync` already load the `MachineAccessEntity`. Add default-implemented overloads `ResetMachineAccessAsync(MachineAccess existing)` / `DeleteMachineAccessAsync(MachineAccess existing)` to `IMachineAccessService`, and have `PolicyAwareMachineAccessService` switch on `existing.CredentialKind`.
* `MachineAuthenticationMiddleware` is untouched. Client-credentials machines arrive as `Bearer` JWTs and are handled by `BearerAuthenticationMiddleware` (`IsMachine = true`, `azp = client_id`), and `CheckAccessToAsync` then runs `VerifyAccessForMachineAsync` exactly as for Entra machines. Policy enforcement for `ClientCredentials` projects: the bearer middleware rejects an M2M token when its project's policy is not `ClientCredentials`. The project is resolved lazily in `CheckAccessToAsync`, because the token itself does not name the project; the `MachineAccessEntity` lookup already proves project membership. Symmetrically, `MachineAuthenticationMiddleware` rejects API-key/certificate credentials for a `ClientCredentials` project (extend the `policySatisfied` switch).
* Combining mTLS with client-credentials JWTs (`CertificateAndClientCredentials`) is technically possible with the same reconciliation as `CertificateAndApiKey`, but it is **not** part of this spec.

`Program.cs`:

```csharp
services.AddApiKeyMachineAccess();
services.AddCertificateMachineAccess(context.Configuration);
if (authOptions.Provider == IdentityProviderKind.AuthKit && !string.IsNullOrEmpty(authOptions.AuthKit.AuthKitDomain))
{
    services.AddAuthKitMachineAccess(context.Configuration); // registers IIdentityProviderMachineAccessService
}
```

`AuthKit.ApiKey` is read through the existing Key Vault binding (`Binding.Azure.KeyVault`) in production, and from user secrets/`local.settings.json` locally.

**CLI:** `sform machine create --kind client-credentials` (the `MachineAuthSetCommand` gains the new policy value). The output prints `client_id`, `client_secret`, the token URL `{AuthKitDomain}/oauth2/token` and a one-line `curl` example. `Examples/CustomClientApp` gets an AuthKit variant using a plain `client_credentials` POST.

### 8. (Optional, Phase E) WorkOS user API keys as personal access tokens

For CI pipelines that should act *as a user* rather than as a project machine: accept `Authorization: WorkOS <key>` (scheme from `AuthKit.ApiKeyScheme`, which is distinct from `ApiKey`, so it cannot collide with native `2s.` keys). `BearerAuthenticationMiddleware` validates it via `POST /api_keys/validations`. It accepts only `owner.type == "user"` and synthesizes `sub = owner.id` with scopes from `permissions[]` through `PermissionMap`, so the caller is authorized through the normal account-role path. Results are cached in `IMemoryCache` for 60 s keyed by `SHA-256(value)`, which bounds WorkOS round-trips at the cost of up to 60 s revocation delay (the same trade-off as the machine policy cache). Organization-owned WorkOS keys are rejected, because native `2s.` keys already cover machines.

### 9. Security notes

* Accept only RS256, enforce `exp`/`nbf` with 30 s skew, require an exact issuer match, and validate `aud` whenever configured. The docs recommend a JWT template audience, so production must set one.
* Never log raw tokens, secrets or the WorkOS management key. `ShowPII` is Development-only.
* The WorkOS management key (`sk_…`) can create and delete M2M apps and read users. It lives only in Key Vault, and the Function's managed identity is the only principal that can read it.
* M2M secrets are returned once, the same as today. Cosmos stores only the masked `xxx***` value (`CosmosAccessService` existing behaviour).
* The CLI refresh-token store is encrypted at rest (DPAPI, Keychain, libsecret) outside `DEBUG`.
* The discovery endpoint exposes public identifiers only.

### 10. Delivery phases

| Phase | Scope | Behaviour change |
|---|---|---|
| **A** | `UserAuthenticationOptions`, `IBearerTokenValidator`, `EntraIdBearerTokenValidator`, `BearerAuthenticationMiddleware`, `HttpRequestDataExtensions` refactor, machine detection via `MachineIdentity` | None. Entra keeps working, verified by tests. |
| **B** | `Access.AuthKit` project, `AuthKitBearerTokenValidator` (user tokens), claim normalization, `IUserProfileResolver`, provider switch in `Program.cs`, provider-aware `OAuthFlows`, `GET v1/auth/configuration` | AuthKit users can call the API. |
| **C** | CLI `IAuthenticationService` refactor, `EntraIdAuthenticationService`, `AuthKitAuthenticationService` (device flow, refresh rotation, secure store), discovery | `sform account` works against AuthKit. |
| **D** | `MachineCredentialKind.ClientCredentials`, `AuthKitMachineAccessService`, `WorkOsManagementClient`, M2M token validation, policy routing by stored kind, CLI `--kind client-credentials`, example app | AuthKit M2M machine access. |
| **E** (optional) | WorkOS user API keys as personal access tokens | Opt-in. |

Phases A to C are the minimum for "switch between Entra and AuthKit". Native API keys and mTLS work under AuthKit from Phase B without further change.

## Testing

* **Unit, `Access.AuthKit/test` (xUnit + Shouldly):**
  * The validator accepts a token signed by a test RSA key served from a stub JWKS (`HttpMessageHandler` fake). It rejects a wrong issuer, a wrong audience, an expired token, HS256, and an unknown `kid`.
  * The normalizer covers user vs M2M detection, `PermissionMap`, `DefaultUserScopes`, and the name/email fallbacks.
  * `AuthKitMachineAccessService` against a fake `WorkOsManagementClient` covers the create, reset ordering (mint before delete) and delete paths.
* **Unit, `Api.Functions`:** `BearerAuthenticationMiddleware` tests cover pass-through when claims are already cached, 401 on an invalid token, 503 on a JWKS fault, and the `MachineIdentity` item being set. Add regression tests showing that Entra behaviour is unchanged (Phase A gate).
* **Unit, CLI:** `AuthKitAuthenticationService` against a fake handler covers `authorization_pending` → `slow_down` → success, `access_denied`, `expired_token`, refresh rotation (the old refresh token must never be reused), and `invalid_grant` clearing the store. The storage is abstracted behind `ITokenStore` with an in-memory fake, since `IFileSystem` does not cover the protected-storage APIs.
* **Integration, `Access.AuthKit/testint`:** skipped unless `WORKOS_TEST_API_KEY`/`WORKOS_TEST_CLIENT_ID` are set. It creates an M2M app in a WorkOS *staging* environment, obtains a `client_credentials` token, validates it, then cleans up. `CosmosFixture`-based tests cover `PolicyAwareMachineAccessService` routing by stored credential kind.

## Files to create / change

Create:

* `src/Platform/Storyteller/Backend.Core/src/Accessing/UserAuthenticationOptions.cs`, `IdentityProviderKind.cs`, `IBearerTokenValidator.cs`, `BearerValidationResult.cs`, `IIdentityProviderMachineAccessService.cs`, `IUserProfileResolver.cs`
* `src/Platform/Storyteller/Access.AzureAd/src/EntraIdBearerTokenValidator.cs`
* `src/Platform/Storyteller/Access.AuthKit/src/` with `Access.AuthKit.csproj`, `EntryPoint.cs`, `AuthKitOptions.cs`, `WorkOsManagementClient.cs`, `AuthKitBearerTokenValidator.cs`, `AuthKitClaimNormalizer.cs`, `AuthKitMachineAccessService.cs`, `AuthKitUserProfileResolver.cs`, plus `test/` and `testint/`
* `src/Platform/Storyteller/Api.Functions/src/Security/BearerAuthenticationMiddleware.cs`
* `src/Platform/Storyteller/Api.Functions/src/V1/AuthConfigurationHttp.cs`
* `src/Platform/Cli/src/Authentication/EntraIdAuthenticationService.cs`, `AuthKitAuthenticationService.cs`, `ITokenStore.cs`, `ProtectedTokenStore.cs`, `DeviceCodePrompt.cs`, `SignedInUser.cs`, `AuthenticationException.cs`
* `docs/Platform/Storyteller/authentication.md`: provider switch, AuthKit dashboard setup (JWT template, permissions, CLI Auth enablement, M2M org) and the account-switch warning

Change:

* `Api.Functions/src/Program.cs`: provider switch, middleware order, `AddAuthKitMachineAccess`
* `Api.Functions/src/Security/HttpRequestDataExtensions.cs`: remove inline validation and static `ClientId`, use `MachineIdentity`
* `Api.Functions/src/OpenApi/OAuthFlows.cs`: provider-aware
* `Api.Functions/src/Api.Functions.csproj`: reference `Access.AuthKit`
* `Api.Functions/src/local.settings.json`: `Auth:Provider` (default `EntraId`) and commented AuthKit keys
* `Abstractions.Access/src/MachineCredentialKind.cs`: `ClientCredentials`
* `Backend.Core/src/Accessing/IMachineAccessService.cs`: overloads taking the existing `MachineAccess`
* `Access.Certificates/src/PolicyAwareMachineAccessService.cs`: route `ClientCredentials`, route reset/delete by stored kind
* `Api.Functions/src/Security/MachineAuthenticationMiddleware.cs`: `ClientCredentials` in `policySatisfied`
* `Backend.CosmosDb/src/Accessing/CosmosAccessService.cs`: call the new overloads
* `src/Platform/Cli/src/Authentication/IAuthenticationService.cs`, `Configuration/AuthenticationOptions.cs`, `Startup.cs`, `app.config.json`, `Commands/Account/*.cs`, `Commands/MachineAccess/MachineCreateCommand.cs`, `MachineAuthSetCommand.cs`
* `42.mono.slnx`: add the `Access.AuthKit` projects
* `Directory.Packages.props`: no new packages expected (`Microsoft.IdentityModel.*`, `Microsoft.Identity.Client.Extensions.Msal` and Polly are already present). Verify during Phase B.

## Open Questions (verify in Phase B/D before coding against them)

1. The exact `iss` of AuthKit user tokens issued through the **device flow** (`https://api.workos.com/` vs `https://api.workos.com/user_management/{client_id}`). The validator reads it from config, so this only affects documentation defaults. Decode a real token from the staging environment.
2. The exact M2M token claims: whether scopes arrive as `scope` (space-delimited) or `permissions[]`, and whether `sub` equals `client_id`. The normalizer accepts both scope shapes; confirm on a real token.
3. Whether the M2M `aud` can be controlled (JWT template or Connect resource indicator). If it can't, M2M tokens are validated by issuer + `org_id` + JWKS only.
4. Whether AuthKit's device authorization must be explicitly enabled per environment in the dashboard (CLI Auth toggle), and whether it is available on the plan in use.
