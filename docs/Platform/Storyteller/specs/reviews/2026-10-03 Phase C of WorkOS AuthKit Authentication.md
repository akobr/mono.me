# Phase C of WorkOS AuthKit Authentication

## Overview

Phase C of [2026-09-29 WorkOS AuthKit Authentication for Storyteller](../2026-09-29%20WorkOS%20AuthKit%20Authentication%20for%20Storyteller.md) moves the `sform` CLI (`src/Platform/Cli`) off its MSAL-shaped `IAuthenticationService` onto a provider-neutral one with two implementations:

- Entra ID, wrapping the existing MSAL code.
- AuthKit, implementing the CLI Auth device grant, rotating refresh tokens, and storing the session in OS-protected storage.

`sform` picks the provider from `app.config.json` or from the server's `GET v1/auth/configuration` (Phase B) and caches the answer, so one build follows whichever provider a deployment uses. `sform account`, `account logout` and `account register` use the new interface. The SDK takes its token from the same service.

`Cli.UnitTests` passes 44 of 44 tests; 36 of them are new. The built CLI was run once against `api.workos.com` with a dummy client ID. No real AuthKit sign-in was completed.

## What Was Done

### 1. Provider-neutral authentication contract

`src/Platform/Cli/src/Authentication/IAuthenticationService.cs` is replaced with the spec's interface:

- `GetSignedInUserAsync`
- `GetAccessTokenAsync`
- `LoginWithDeviceCodeAsync`
- `LogoutAsync`

New types, one per file (SA1402):

| File | Content |
|---|---|
| `DeviceCodePrompt.cs` | `UserCode`, `VerificationUri`, `VerificationUriComplete?`, `ExpiresIn`. |
| `SignedInUser.cs` | `Id`, `UserName`, `Name?`. |
| `AuthenticationException.cs`, `AuthenticationFailureReason.cs` | `Reason` is `ServiceError`, `Cancelled`, `Expired` or `Denied`. Messages never contain tokens. |
| `OrganizationChoice.cs` | `Id`, `Name` of a WorkOS organization. |
| `AuthenticationProvider.cs` | `EntraId`, `AuthKit`. The CLI does not reference `Backend.Core`, so it has its own enum. |
| `ResolvedAuthentication.cs` | The settings in effect: provider, client ID, tenant ID, scopes, AuthKit API base URL. |
| `AuthKitDefaults.cs` | `https://api.workos.com`. |

**Deviation:** `LoginWithDeviceCodeAsync` takes an optional second parameter, `Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization`. The spec asks the CLI to list organizations when WorkOS returns `organization_selection_required`, and the service should not prompt by itself. When the parameter is null, the first organization is used. The Entra implementation ignores it.

### 2. `EntraIdAuthenticationService`

`EntraIdAuthenticationService.cs` replaces `AuthenticationService.cs`. The MSAL setup is unchanged: the same `~/.42for.net/msal.cache`, the same `DEBUG && !TESTING` plaintext exception, and the same authority. It takes `ResolvedAuthentication` instead of `AuthenticationOptions`.

- **Scopes** are the discovered ones when the server sends them. Otherwise they are the previous defaults (`api://{clientId}/User.Impersonation`, `api://{clientId}/Default.ReadWrite`), which Phase B's discovery also returns.
- **Silent calls** (`GetSignedInUserAsync`, `GetAccessTokenAsync`) catch `MsalUiRequiredException` and return null. Previously `AccountCommand` threw that exception itself to get to the device code.
- **Device code:** MSAL's `DeviceCodeResult` maps to `DeviceCodePrompt`. Exceptions are translated:
  - `OperationCanceledException` → `Cancelled`
  - `MsalServiceException` with `authorization_declined` / `access_denied` → `Denied`
  - `MsalServiceException` with `code_expired` / `expired_token` → `Expired`
  - any other `MsalServiceException` → `ServiceError`
  - `MsalClientException` → `Expired`, the timeout case the old code reported.
- **Logout** removes every cached account. The old `ClearAuthenticationAsync` removed only the first.

### 3. `AuthKitAuthenticationService`

The flow follows the WorkOS CLI Auth documentation, using `HttpClient` with no new package. All requests are `application/x-www-form-urlencoded` to `{AuthKitApiBaseUrl}/user_management/…`.

1. `POST authorize/device` with `client_id`. A missing `device_code`, `user_code` or absolute `verification_uri` is a `ServiceError`. Any error response becomes `ServiceError` with the WorkOS message.
2. `onPrompt` gets the code, `verification_uri`, `verification_uri_complete` and `expires_in`.
3. Wait for `interval` (5 s by default), then `POST authenticate` with `grant_type=urn:ietf:params:oauth:grant-type:device_code`, `device_code` and `client_id`.
   - `authorization_pending` keeps polling.
   - `slow_down` adds one second to the interval, as the spec and the WorkOS page say.
   - `access_denied` throws `Denied`.
   - `expired_token` throws `Expired`.
   - Any other error is `ServiceError`.
   - The loop stops with `Expired` once `expires_in` has passed, without another poll.
4. `organization_selection_required` (`code`, `pending_authentication_token`, `organizations[]`) asks the selector, then posts `grant_type=urn:workos:oauth:grant-type:organization-selection` with the pending token, `organization_id` and `client_id`.
5. On success the session is written under the process lock and returned as a `SignedInUser`: the email (or the ID) as the user name, and `first last` as the name.

Two error shapes are read:

- `error` / `error_description`: the OAuth shape. `api.workos.com` returned `{"error":"invalid_client","error_description":"Unknown client."}` with HTTP 400 for a dummy client ID on 2026-10-03.
- `code` / `message`: other WorkOS errors.

Computed `Code` and `Description` properties on the error DTO are `[JsonIgnore]`. `ReadFromJsonAsync` uses web defaults, where `Code` collides with the `code` JSON name.

**Session and refresh:**

- The session (`AuthKitSession.cs`, `AuthKitSessionUser.cs`) stores the refresh token, access token, access-token expiry, user (`id`, `email`, `first_name`, `last_name`) and `organization_id`, as the spec lists.
- The access-token expiry is read from the JWT `exp` without verification. The API validates the token; the CLI only needs to know when to refresh. The CLI does not reference IdentityModel, so the base64url decoding is a few lines of local code. A token without a readable `exp` is treated as valid for five minutes.
- `GetAccessTokenAsync` returns the stored token while `exp - 60 s` is in the future. Otherwise it takes an in-process `SemaphoreSlim`, then the cross-process lock, and reads the store again, in case another `sform` process rotated the token in the meantime. Only then does it exchange `grant_type=refresh_token`. The new session replaces the old one. An `organization_id` missing from the refresh response keeps the previous value.
- A refresh rejected with HTTP 400, 401 or 403 clears the store and returns null, so the user signs in again. The spec names `invalid_grant`. The other client errors are treated the same way, because the token cannot be used either way.
- HTTP 429, 5xx or a network failure throws `ServiceError` and keeps the session.
- `LogoutAsync` clears the store under the lock. Revoking the server-side session through `sid` is optional in the spec and is not done.

A second internal constructor takes a `TimeProvider` and the delay function, so the tests run the polling loop without waiting.

### 4. Secure storage (`ITokenStore`, `ProtectedTokenStore`)

`ITokenStore` has `ReadAsync`, `WriteAsync`, `ClearAsync` and `LockAsync`. The tests use an in-memory fake, as the spec asks.

`ProtectedTokenStore` uses `Microsoft.Identity.Client.Extensions.Msal.Storage`, which was already referenced:

- **File:** `authkit.cache` in `~/.42for.net`.
- **Protection:**
  - Windows: DPAPI.
  - macOS: Keychain (`WithMacKeyChain("net.42for.sform", "authkit")`).
  - Linux: libsecret (`WithLinuxKeyring(...)` in the default collection).
  - Builds with `DEBUG && !TESTING`: an unprotected `authkit.cache.plaintext`, as for MSAL.
- **Lock:** `LockAsync` creates the directory through `IFileSystem` and takes MSAL's `CrossPlatLock` on `authkit.cache.lockfile` (100 ms × 600 retries, about a minute).
- **Damaged data:** a store that does not deserialize reads as no session.

The existing MSAL cache code configures neither Keychain nor libsecret. The new store sets both, so the spec's macOS and Linux protection actually applies.

### 5. Discovery (`AuthenticationConfigurationResolver`)

`IAuthenticationConfigurationResolver.ResolveAsync` decides in this order:

1. `authentication.provider` set: that provider with `clientId` (and `tenantId`). An unknown name or a missing `clientId` throws `AuthenticationException`.
2. `auth.discovery.json` in the application directory, next to `access.default.json`, if its `baseUrl` equals `general.baseUrl`.
3. `GET {general.baseUrl}/v1/auth/configuration`:
   - anonymous, with a 15 s timeout and case-insensitive JSON, because the API uses PascalCase;
   - the answer (`baseUrl`, `provider`, `clientId`, `tenantId`, `scopes`, `authKitDomain`) is written to `auth.discovery.json`.
4. If discovery fails (non-2xx, network error, timeout, unreadable body) and `clientId` is configured: Entra ID from `app.config.json`. This is what a server from before Phase B needs. The fallback is not cached.
5. Otherwise `AuthenticationException` names the base URL and the settings to add.

`ForgetAsync` deletes the cache file. `AuthKitApiBaseUrl` always comes from `authentication.authKitApiBaseUrl` or the default, because the discovery response has no such field.

The spec puts provider selection in `Startup` ("registers the implementation by provider"). With discovery, the provider is known only at run time. `AuthenticationServiceSelector` is therefore the registered `IAuthenticationService`. It resolves the settings on first use (`AsyncLazy`) and creates the Entra or AuthKit implementation. The first API call or sign-in triggers resolution, because the SDK asks for a token. Commands that make no API call never call the discovery endpoint. Its `LogoutAsync` also calls `ForgetAsync`, so a deployment that switched providers is discovered again on the next sign-in.

### 6. Startup, SDK and error handling

`Startup.ConfigureServices` registers:

- `AddHttpClient()`;
- `ITokenStore` → `ProtectedTokenStore`;
- `IAuthenticationConfigurationResolver`;
- `IAuthenticationService` → `AuthenticationServiceSelector`, all as singletons.

`GeneralOptions` is now bound with `services.Configure`, because the resolver needs `BaseUrl` through `IOptions`.

`ConfigureStorytellerSdk` no longer creates its own `AuthenticationService` with `new FileSystem()`, as the spec asks. It registers `ISdkConfiguration` from DI before `AddStorytellerSdk()`, whose `TryAddSingleton` then keeps it. `AccessTokenFactory` calls `IAuthenticationService.GetAccessTokenAsync()` synchronously, as before, and returns an empty string on any failure.

`Program.Main` catches `AuthenticationException` from any command. It logs a warning with the reason, prints `! {message}`, and returns `WARNING_UNAUTHORIZED_ACCESS` (44). A discovery failure therefore no longer ends in "Total mayhem".

### 7. Commands

- **`AccountCommand`:**
  - `GetSignedInUserAsync()` decides between "already logged in" and `LoginWithDeviceCodeAsync`.
  - The prompt keeps the `Sign in` header. It prints the address and code, the address with the code filled in when WorkOS sends one, and the remaining minutes.
  - The new `-b|--browser` option opens the address with `Process.Start` (`UseShellExecute`). If no browser can start, it prints a hint instead.
  - Multiple organizations are offered with `Console.Select`.
  - `AuthenticationException` reasons print the earlier messages (cancelled, failed with details) or new ones (declined, code expired), still with `ERROR_WRONG_INPUT`.
- **`AccountLogoutCommand`:** uses `GetSignedInUserAsync` and `LogoutAsync`. Without a signed-in user it still calls `LogoutAsync`, which drops an expired session and the discovery cache.
- **`AccountRegisterCommand`:** builds the suggested organization name from `SignedInUser.UserName` instead of reading `unique_name` / `upn` / `preferred_username` from the MSAL claims. The `live#john.doe@outlook.com` fallback is gone; the user name is always set.

`MachineCreateCommand` and `MachineAuthSetCommand` belong to Phase D and are unchanged.

### 8. Configuration

`Configuration/AuthenticationOptions.cs` gains `Provider` and `AuthKitApiBaseUrl`. All properties are now nullable.

`Cli.csproj` adds `InternalsVisibleTo` for the test assembly, and `Cli.UnitTests.csproj` enables `Nullable`. `Constants.AUTH_DISCOVERY_JSON` names the cache file.

**Deviation:** `src/Platform/Cli/src/app.config.json` is not changed. The working copy has uncommitted local edits (base URL comment, client and tenant IDs, Sentry DSN). The new keys are optional, and leaving `provider` out is what turns discovery on. The documentation shows the keys instead.

### Documentation

`docs/Platform/Storyteller/authentication.md` gains a "Signing in with sform" section:

- the resolution order and an `authentication` example;
- the `auth.discovery.json` cache and its reset on logout;
- the AuthKit device flow, organization choice, storage locations and refresh rotation with the lock.

The dashboard setup gains a step to enable CLI Auth. The "Not available yet" entry for the CLI is removed.

## Behaviour changes

- `sform` calls `GET v1/auth/configuration` once per `general.baseUrl` unless `authentication.provider` is set. Against a server without the endpoint, it keeps using Entra ID from `app.config.json`.
- An AuthKit deployment can be signed in to with `sform account`.
- Entra ID:
  - An expired MSAL session now leads `sform account` into a new device sign-in through `GetSignedInUserAsync`, instead of relying on `MsalUiRequiredException` from `AcquireTokenSilent`.
  - `sform account logout` removes every cached account and the discovery cache.
  - `sform account logout` with an expired session no longer throws.
- Sign-in configuration errors print one line and exit with 44.
- `sform account --browser` is new.

## Left for later phases

- **Phase D:**
  - `sform machine create --kind client-credentials`;
  - `MachineAuthSetCommand`;
  - the AuthKit M2M path;
  - the client-credentials example app.
- **Phase E:** WorkOS user API keys.
- **Not done:**
  - revoking the WorkOS session (`sid`) on logout;
  - an AuthKit API base URL in the discovery response;
  - accepting AuthKit-domain user tokens on the server (Phase B, section 4).
- `EntraIdAuthenticationService` has no unit tests. MSAL's `IPublicClientApplication` is not mocked here, and the class is mostly the moved MSAL code.

## Tests

`src/Platform/Cli/test/Authentication/` (xUnit + Shouldly).

Test doubles:

- `InMemoryTokenStore`: counts locks, writes and clears, and can script reads to simulate another process.
- `ScriptedHttpHandler`: queued responses; records method, URI, `Authorization` and form fields.
- `StubHttpClientFactory`.
- `ManualTimeProvider`.
- `TestTokens`: unsigned JWTs carrying only `exp`, and WorkOS session JSON in the shape of the documentation.

`AuthKitAuthenticationServiceTests` (22):

- **Polling:** `authorization_pending`, then `slow_down`, then success.
  - The delays are 5 s, 5 s and 6 s.
  - The authorize form holds only `client_id`. Every poll sends the device-code grant, `device_code` and `client_id`, without `Authorization`.
  - The prompt carries code, URIs and lifetime.
  - The stored session holds the tokens, the JWT expiry and the organization, written under one lock. The returned user is `ada@example.com` / `Ada Lovelace`.
- **Terminal errors:** `access_denied` → `Denied`, `expired_token` → `Expired`, `invalid_client` → `ServiceError`. Polling stops and nothing is stored.
- **Code lifetime and cancellation:** when the lifetime runs out, the sign-in stops without another poll. Cancellation during the wait → `Cancelled`.
- **Authorize rejected:** the real WorkOS shape (`error` / `error_description`) and the `code` / `message` shape both surface the message after one request.
- **Organization selection:** the selector receives both organizations, and the organization-selection grant posts the pending token and the chosen ID. Without a selector, the first organization is used.
- **Session reads:**
  - No session: null and no HTTP.
  - A fresh token is returned without HTTP and without a lock.
- **Refresh rotation:**
  - The first refresh posts `refresh-1`. A fresh call makes no request. After expiry the next refresh posts `refresh-2`, never `refresh-1` again, and `refresh-3` is stored. Each refresh takes the lock.
  - A missing `organization_id` keeps the old one.
  - Another process that refreshed during the lock wait means no request and no write.
- **Refresh failures:** 400 `invalid_grant` and 401 clear the store and return null. A 503 throws `ServiceError` and keeps the session.
- **Other:**
  - `GetSignedInUserAsync` maps the stored user. `LogoutAsync` clears under the lock.
  - `ReadExpiry` reads `exp` and returns null for non-JWT or non-base64 input. An opaque token falls back to five minutes.

`AuthenticationConfigurationResolverTests` (9) run on a temporary directory:

- A configured provider uses no HTTP and honours `authKitApiBaseUrl`.
- An unknown provider, or AuthKit without `clientId`, throws.
- Discovery is anonymous at `{baseUrl}/v1/auth/configuration`, even with a trailing slash in `baseUrl`. It writes the cache, and a second resolver reads the cache without HTTP.
- Entra discovery carries the tenant and scopes.
- A cache for another base URL is ignored.
- A 404 falls back to Entra from settings and writes no cache. A 404 without settings throws a message with the base URL.
- `ForgetAsync` deletes the cache.

`AuthenticationServiceSelectorTests` (3):

- AuthKit settings use the token store and resolve once.
- Logout clears the store and forgets the discovery.
- A resolution failure reaches the caller.

`ProtectedTokenStoreTests` (2, Windows only; they return early elsewhere):

- A session round-trips through real DPAPI in a temporary directory, the file does not contain the refresh token in clear text, and clear empties it.
- The lock creates `authkit.cache.lockfile` and can be taken again after release.

## Verification

```
dotnet test src/Platform/Cli/test/Cli.UnitTests.csproj --nologo -v q
dotnet build src/Platform/Cli/src/Cli.csproj -c Release --nologo -v q
```

Results: **44 passed, 0 failed**. The 8 existing `JsonPatchBuilder` tests are included. The Release build reports 0 errors.

A no-incremental build reports no warnings from the new files. The remaining warnings on touched files were already there:

- SA1518 in `AccountLogoutCommand.cs` and `AccountRegisterCommand.cs`;
- SA1507 in `Startup.cs`.

**Smoke test.** The Debug build of `sform account` was run once with a temporary copy of `app.config.json` in `bin/Debug/net10.0`: `provider: AuthKit`, a dummy `client_000…` ID, and an empty Sentry DSN, so no event left the machine. The source `app.config.json` was not touched, and the copy was restored afterwards.

- The log shows the full chain: `Startup`, the selector, the resolver with the configured provider, and `AuthKitAuthenticationService`. `POST https://api.workos.com/user_management/authorize/device` returned 400, and `ReportSignInFailure` ran.
- Printing the message then failed in `ExtendedConsole.WriteExactDocument`, with `IOException: The handle is invalid` from `Console.BufferWidth`. The CLI toolkit's renderer needs a real console, and the tool's output was redirected. This is not caused by this change.
- With `curl`, WorkOS returned `{"error":"invalid_client",…}` (400) for the authorize, device-code and refresh calls. That is the shape the parser reads, and it is now a test case.
- The run created no `~/.42for.net` directory. The log file it wrote was deleted.

A full AuthKit sign-in, a real refresh rotation, organization selection against a real multi-organization user, and the Keychain and libsecret storage on macOS and Linux were not exercised. They need a WorkOS environment with CLI Auth enabled and non-Windows machines.
