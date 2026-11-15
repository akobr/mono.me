# Require a Recent Sign-in for Secrets, Roles and Machine Credentials

Linear: [424-264](https://linear.app/424fun/issue/424-264/require-recent-sign-in-step-up-before-users-read-or-write-secrets-and).

Line numbers refer to the tree this plan was written against (`7944671ee`). No application code is changed by this document. Three independent critics (security, feasibility against the code, clients) reviewed a first draft before it was committed; their findings are folded in (the defaults, the client remedies, the loop guard, the deployment settings).

## Problem

A user with `ContributorWithSecrets` reads and writes secret values with any valid session. Administrators and Owners change roles, and Contributors create and reset machine credentials, the same way. The API checks the role and never how recently the person actually signed in.

So a stolen session is enough: a refresh token taken from the admin UI (XSS, a malicious browser extension, an unlocked laptop), or a long-lived `sform` or MCP session. It gives secret access, role changes and new machine credentials for the whole session lifetime. Those are exactly what an attacker wants, and what an enterprise security review asks about.

Every AuthKit access token carries `auth_time`, the time of the person's last active authentication. A token refresh does not move it. The AuthKit authorize endpoint accepts `max_age` (RFC 9470): when the last authentication is older, WorkOS asks the person to authenticate again (with MFA when it applies) and issues tokens with a fresh `auth_time`, in the same session (`sid`). The API can therefore require a recent sign-in for the sensitive operations and tell the client how to get one.

The main risk is locking people out: a provider whose tokens carry no `auth_time`, a client that cannot request a fresh sign-in, or a client that loops on the challenge.

## Current State

### `auth_time` in the request

- `UserSubjectTokenRules.GetAuthTime(IReadOnlyList<Claim>)` (`Backend.Core/src/Accessing/UserSubjectTokenRules.cs:62-70`) reads `auth_time` as Unix seconds; it returns null when the claim is absent, blank, not an integer or negative. It has no clock-skew check. The subject-token validators of the exchange use it on raw claims.
- AuthKit: `AuthKitBearerTokenValidator` validates with `JsonWebTokenHandler` (no claim mapping), and `AuthKitClaimNormalizer.CopyKeptClaims` (`Access.AuthKit/src/AuthKitClaimNormalizer.cs:107-120`) keeps every claim except the replaced ones (`IsReplaced`, lines 101-105), so `auth_time` reaches `request.GetClaims()` under its own name, on the validated and the `DEV_AUTH` decode path.
- Keycloak: `KeycloakClaimNormalizer.BuildUserClaims` keeps `auth_time` the same way. Keycloak puts it into access tokens through the `basic` client scope, which also gives `sub`.
- Entra ID: `EntraIdBearerTokenValidator` (`Access.AzureAd/src/EntraIdBearerTokenValidator.cs:82, 166-190`) uses `JwtSecurityTokenHandler` with inbound claim mapping on; IdentityModel 8.23.0 has no mapping for `auth_time`, so a token that carries it keeps the name. Entra ID access tokens do not carry `auth_time` by default; it is an ID token claim. The only test with it adds it by hand (`EntraIdUserSubjectTokenValidatorTests.cs:71-83`).
- No first-party client signs in with Keycloak and `max_age`: the admin UI supports AuthKit only (`ui.admin/src/app/boot/auth.ts:70-72`), `sform` Entra ID and AuthKit (`Cli/src/Authentication/AuthenticationProvider.cs`), and MCP clients sign in at the provider themselves.
- Storyteller user tokens (the MCP server's, `Access.Federation`): `UserTokenExchangeService.ExchangeAsync` copies the subject token's `AuthTime` (`UserTokenExchangeService.cs:189-198`), and `StorytellerTokenIssuer.IssueUserAsync` writes it as a numeric `auth_time` claim when present (`StorytellerTokenIssuer.cs:107-109`). But `StorytellerTokenValidator.ValidateUser` (`StorytellerTokenValidator.cs:280-341`) builds the result's claims from `sub` and `scp` only, so on API routes the claim is lost. `StorytellerUserTokenValidationTests.cs:72-94` pins that exact claim list.
- Nothing in `Api.Functions` reads `auth_time` today.

### The bearer middleware

- `BearerAuthenticationMiddleware.Invoke` (`Api.Functions/src/Security/BearerAuthenticationMiddleware.cs:32-144`) reaches its services through `context.InstanceServices` (lines 54-55, 82, 99). Users come from the provider's `IBearerTokenValidator`, from the `DEV_AUTH` decode path (lines 66-86), or from the Storyteller token validator with a delegation (lines 92-101, 128-140).
- `StoreIdentity` (lines 272-300) writes the request's claims (`CachedClaims`), the delegation (`DelegatedActor`, `DelegatedRestrictions`) and the machine items. Downstream code reads these items, never DI (`HttpRequestDataExtensions.GetClaims`, `TryGetApplicationIdentity`, `TryGetDelegatedActor`).
- Machines are told apart by `FunctionContextItemKeys.MachineIdentity`, set by this middleware or by `MachineAuthenticationMiddleware` (API keys, mTLS).

### Secret rights

- `SecretRights` and `GetSecretRightsAsync` (`Api.Functions/src/Security/HttpRequestDataExtensions.cs:19-40, 121-167`). The machine branch returns early. The user branch gives both rights to `ContributorWithSecrets` and up (every user under `DEV_AUTH`), then, after the `#if DEV_AUTH` block so it holds in every build, withholds the read right from a user acting through an actor under `NoSecretReveal` (`IsReadWithheldByActor`). It never throws; it runs on every key/value route.
- `KeyValueAuthorization` (`Api.Functions/src/V1/KeyValueRequests.cs:9-89`) turns a withheld right into the client's error: `EnsureSecretReadRights`/`EnsureSecretWriteRights` are pre-checks, `UnderSecretReadRuleAsync`/`UnderMtlsWriteRuleAsync` post-checks that convert the service's `AccessDenied` for a secret. `IsWithheld` (lines 70-75): reads by the actor or for mTLS, writes for mTLS only. `Refused` (lines 78-88) returns an `AccessDeniedException` (403): `ActorRestricted`, `MtlsRequired`, or a plain `AccessDenied`.
- `KeyValuesHttp` (`Api.Functions/src/V1/KeyValuesHttp.cs`): `GetKeyValue` and `GetKeyValueVersion` pre-check a reveal against the actor and mTLS before the service is asked, also for a public pair (lines 121-129, 423-431); the diff routes go through `UnderSecretReadRuleAsync` (lines 630-646); an export with secrets through `EnsureSecretReadRights` (line 539); set, delete and import through `UnderMtlsWriteRuleAsync`; protect through `EnsureSecretWriteRights` (line 358); restore and purge answer per item `Outcome = Forbidden, ErrorCode = AccessDenied` with 200 (lines 252-313).
- Resolved configuration reads (`ConfigurationHttp.GetConfigurationResolved`, `ConfigurationHttp.cs:173-260`) resolve secret bindings only with `CanReadSecrets` and answer 200 either way. `SetResolvedHeaders` (lines 859-882) sets `X-2S-Secrets-Withheld` to `mtls-required` or `actor-restricted`; with two reasons the second overwrites the first. `ConfigurationEntityTags.RightsCode` (`ConditionalReads/ConfigurationEntityTags.cs:108-117`) gives each rights state its own letter in the ETag (`s`, `w`, `a`, `p`), so a state never shares a 304 with another.
- `KeyValuePolicyHttp` (`Api.Functions/src/V1/KeyValuePolicyHttp.cs`) uses the rights only as the audit caller; its routes are Administrator-only.

### Role changes and machine credentials

- `AccessHttp` (`Api.Functions/src/V1/AccessHttp.cs`): `PostGrantPermission` (lines 276-309, actor check `EnsureActorMayGrantRole` at 302), `PostRevokePermission` (311-342), `PostMachines` (400-476; role checks, `EnsureActorMayGiveOutSecretReads` at 456, `CreateMachineAccessAsync` inside a `try` that maps `MachineAccessNotSupportedException` with `ErrorResponseMapping.ToActionResult`), `PutMachine` (the reset, 478-520, the same shape).
- `MembersHttp.SetMemberRole` (`MembersHttp.cs:50-87`) and `RemoveMember` (89-110). A member leaving by themselves uses `RemoveMember` too; the service tells it by `actorId == accountId` (`CosmosAccessService.cs:446`).
- `InvitationsHttp.CreateInvitation` (`InvitationsHttp.cs:65-111`): a deferred grant, already treated like one by the actor rule (line 96).
- `FederationHttp.PutMachineFederationRules` (`FederationHttp.cs:243-302`): decides which workload gets the machine's tokens, the reset of a federated machine. `PutFederatedIssuer` (136-213) applies the actor rule only when it replaces an issuer (180-183).
- `CertificatesHttp.PostSharedCertificate` (`CertificatesHttp.cs:148-197`): answers a PKCS#12 with its password.
- `KeyValuePolicyHttp.AddRecipient` and `UpdateRecipient` (lines 205-280): a recipient's private key reads every client-encrypted value made for it afterwards.
- The role checks of grant, revoke, set role and remove run inside the service (`CosmosAccessService.cs:328, 343, 399, 444`).

### Errors

- `ExceptionHandlingMiddleware` (`Api.Functions/src/ErrorHandling/ExceptionHandlingMiddleware.cs`) catches `SecurityTokenException` first and answers a bare 401 (lines 27-30, 77-89). Client errors go through `ErrorResponseMapping.TryMap` (`ErrorResponseMapping.cs:14-46`, no 401 entry) and `ProcessClientErrorAsync` (lines 91-115), which already adds one header for one exception type (`Retry-After` for `ServiceUnavailableException`, lines 108-111).
- `ErrorResponse` (`Api.Functions/src/Models/ErrorResponse.cs`): `Message`, `Hint`, `ErrorCode`, `Details`, PascalCase. `ErrorCodes` (`Backend.Core/src/ErrorCodes.cs`) are stable strings. `StorytellerException(message, errorCode)` is the base of the domain exceptions; `ServiceUnavailableException` carries extra data (`RetryAfter`).
- No API response sets `WWW-Authenticate` today. Every 401 has no body (`docs/Platform/Storyteller/authentication.md:1755-1765`), and "Not available yet" says so (line 1832).
- The comment of `EnsureRole` (`HttpRequestDataExtensions.cs:195`): "An authenticated user without the role gets 403, never 401, so clients do not sign in again for nothing." A step-up is the case where signing in again does help.

### Options, builds and tests

- `UserAuthenticationOptions` (section `Auth`) and `TokenExchangeOptions` (`Auth:TokenExchange`) are bound and validated at start in `UserAuthenticationEntryPoint.AddUserAuthenticationOptions` (`Backend.Core/src/Accessing/UserAuthenticationEntryPoint.cs:15-44`).
- `TimeProvider` is registered (`TryAddSingleton(TimeProvider.System)`) only by `Backend.CosmosDb` and the view transfer; the API never resolves it in a middleware.
- Debug builds define `DEV_AUTH` (`Api.Functions.csproj:22-24`): they skip `User.Impersonation` and the role of users and serve requests without an identity; a test turns the role check on with `UserRoleChecks.Enforce()` (`Security/UserRoleChecks.cs`). The actor rule and the machine checks run in every build.
- Test requests: `TestRequests.User`/`UserCarrying`/`DelegatedUser` (`Api.Functions/test/TestRequests.cs:22-61`) and private builders in `MembersHttpTests`, `PermissionHttpTests`, `MachineHttpTests`, `CertificatesHttpTests`, `FederationHttpTests`, `InvitationsHttpTests` put the claims straight into `CachedClaims`, without `auth_time`. Many create their service provider with `using`, so it is disposed when the handler runs.

### Clients

- The admin UI calls the API cross-origin, and the Function App's CORS settings cannot expose response headers (`sdk.typescript/README.md:270`, `authentication.md:1813`): script reads neither `X-2S-Secrets-Withheld` nor `WWW-Authenticate`. Only the body reaches the UI.
- The TypeScript SDK's fetcher refreshes the token and retries once on every 401, then calls `onUnauthorized()` (`sdk.typescript/src/lib/fetcher.ts:158-166`); the admin UI's `onUnauthorized` signs in again with a plain redirect (`ui.admin/src/app/main.ts:25`, `boot/auth.ts:127-132`). A refresh keeps `auth_time`, and a plain AuthKit redirect reuses the session, so a step-up answered this way would loop.
- `@workos-inc/authkit-js` 0.20.4 has no `maxAge` option: `signIn` and `getSignInUrl` take `state`, `loginHint`, `invitationToken`, `organizationId`, `passwordResetToken`. `getSignInUrl` creates the PKCE verifier exactly as `signIn` does, so a parameter appended to its URL survives the code exchange.
- `sform` signs users in with the AuthKit device flow (no `max_age` there) and sends the provider's token itself. `ApiRefusal.Of` (`Cli/src/Output/ApiRefusal.cs:16-35`) prints a generic message for every 401; `ConfigExportCommand` prints an mTLS remedy for any withheld reason (`ConfigExportCommand.cs:94-98`).
- The MCP server maps a 401 to "Sign in to the MCP server again …" (`Mcp/src/Tools/ApiErrors.cs:15-38`) and reads the code only from `ApiException<ErrorResponse>`; a 401 without a documented body is a plain `ApiException`. Its secret reads are blocked by `NoSecretReveal` by default, but its secret writes and `create_machine`/`reset_machine` reach the API.
- The .NET configuration provider fails definitively on any withheld reason with an mTLS message (`Libraries/Platform/Storyteller/Configuration/src/Reading/DocumentReader.cs:51-58`).

### WorkOS (checked 2026-11-15 through the WorkOS API)

- Production: sign-in by Magic Auth and SSO, passwords off, `mfaEnabled: Off`, email verification required, impersonation off.
- Staging: passwords and social sign-in, `mfaEnabled: Off`. The automation user pools sign in there with passwords and without MFA.
- WorkOS docs (AuthKit, Reauthentication): `auth_time` is in every access token and only an active authentication moves it; `max_age` on the authorization URL forces a re-authentication when the last one is older, `max_age=0` always. The MFA setting `Required` makes every user enrol a factor; passkey, external and (by default) SSO sign-ins skip the challenge.
- The repository has no ISO 27001 notes and no MFA policy.

## Proposed Changes

### Phase 1: The API

#### 1. The setting `Auth:StepUp`

`Backend.Core/src/Accessing/StepUpOptions.cs`, bound and validated at start next to `TokenExchangeOptions` in `AddUserAuthenticationOptions`:

```csharp
public class StepUpOptions
{
    public const string SectionName = "Auth:StepUp";

    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MinMaxAge = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxMaxAge = TimeSpan.FromHours(24);

    // A sign-in this far in the future still counts (the provider's clock may be ahead); further is no sign-in.
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    // Null: on under AuthKit, whose access tokens carry auth_time and whose admin UI can ask for a new sign-in with
    // max_age; off under Entra ID (no auth_time in access tokens) and Keycloak (no client can ask for a new sign-in yet);
    // off in Debug builds (DEV_AUTH) whatever the provider. True or false decides in every build.
    public bool? Enabled { get; set; }

    public TimeSpan MaxAge { get; set; } = DefaultMaxAge;

    public bool IsEnabledFor(IdentityProviderKind provider, bool developmentAuthentication)
        => Enabled ?? (!developmentAuthentication && provider == IdentityProviderKind.AuthKit);
}
```

| Setting | App setting | Default | Meaning |
| --- | --- | --- | --- |
| `Auth:StepUp:Enabled` | `Auth__StepUp__Enabled` | on under AuthKit; off under Entra ID and Keycloak; off in Debug builds | Whether the sensitive operations need a recent sign-in. `true` turns it on for any provider and build, and then refuses every user token without `auth_time` (under Entra ID that is every user, unless their access tokens carry it). |
| `Auth:StepUp:MaxAge` | `Auth__StepUp__MaxAge` | `00:10:00` | The oldest sign-in that still counts, as `hh:mm:ss`. 1 minute to 24 hours, whole seconds. |

`StepUpOptionsValidator` refuses a `MaxAge` outside the range or with a fraction of a second; its message names the format, because a bare `600` binds as 600 days and the host would not start. The deployment gets two optional Bicep parameters (section 16), so the window and an emergency `false` survive the next deployment.

#### 2. `auth_time` on every user request

- `UserSubjectTokenRules.GetAuthTime` is reused on the request's claims. A test of `EntraIdBearerTokenValidator` with an `auth_time` pins that the Entra ID path keeps the name.
- `StorytellerTokenValidator.ValidateUser` adds an `auth_time` claim (invariant Unix seconds) when the validated payload has an integer `auth_time`, and nothing otherwise. The exact-claims test keeps passing without it; new tests cover present, absent and non-integer.

#### 3. The middleware judges the sign-in once

`Api.Functions/src/Security/SignInRecency.cs`:

```csharp
// How recent the sign-in of the user of a request is, judged once by BearerAuthenticationMiddleware against
// Auth:StepUp. NotRequired when the step-up is off.
public sealed record SignInRecency(bool IsRecent, TimeSpan MaxAge)
{
    public static SignInRecency NotRequired(TimeSpan maxAge) => new(true, maxAge);

    public static SignInRecency Evaluate(
        IReadOnlyList<Claim> claims,
        StepUpOptions options,
        IdentityProviderKind provider,
        DateTimeOffset now,
        bool developmentAuthentication);
}
```

- `Evaluate`: off (`IsEnabledFor` false) gives `NotRequired`. Otherwise `IsRecent` when `GetAuthTime(claims)` has a value and `now - authTime` is at most `MaxAge` and at least `-ClockSkew`. A missing or unreadable `auth_time`, or one more than `ClockSkew` in the future, is not recent. Neither is a token with an `act` claim (a WorkOS impersonation: the sign-in is the impersonator's), whatever its `auth_time`; this complements 424-262, which refuses such tokens. A Storyteller user token keeps its actor in the delegation, never in the claims.
- `BearerAuthenticationMiddleware.StoreIdentity` stores it for every user result, under the new key `FunctionContextItemKeys.SignInRecency`: the provider's validated users, the `DEV_AUTH` decoded users and the delegated users of Storyteller tokens. Machine results get none. The middleware resolves `IOptions<StepUpOptions>`, `IOptions<UserAuthenticationOptions>` (the provider) and `TimeProvider` from `context.InstanceServices`, with the defaults when absent (`TimeProvider.System`).
- `AddUserAuthenticationOptions` also does `TryAddSingleton(TimeProvider.System)`.

Judging in the middleware keeps the downstream code free of DI: handlers and `GetSecretRightsAsync` read an item, as they read the claims and the delegation, and the test helpers that dispose their service provider keep working.

#### 4. Reading it downstream

In `HttpRequestDataExtensions`:

```csharp
// Machines never need a step-up. A user request without the item never passed the bearer middleware: in Debug builds
// (DEV_AUTH, requests without an identity) it needs none, in Release builds it is not recent.
public static SignInRecency GetSignInRecency(this HttpRequestData @this);

public static bool HasRecentSignIn(this HttpRequestData @this) => @this.GetSignInRecency().IsRecent;

// 401 StepUpRequired when the sign-in is not recent; nothing for machines.
public static void EnsureRecentSignIn(this HttpRequestData @this);
```

#### 5. The step-up error

- `ErrorCodes.StepUpRequired = "StepUpRequired"`.
- `Backend.Core/src/Accessing/StepUpRequiredException.cs`: `StepUpRequiredException : StorytellerException` with `TimeSpan MaxAge`. Not a `SecurityTokenException`, which would end in the bare 401 and which `InvitationsHttp` catches (line 268). Message: `This operation needs a recent sign-in: sign in again, then repeat it. A sign-in counts for {10 minutes}.` (the duration written out: seconds, minutes or hours).
- `ErrorResponseMapping.TryMap`: `StepUpRequiredException e => (HttpStatusCode.Unauthorized, e.ErrorCode)`, with `Details = new StepUpDetails(MaxAge: <whole seconds>)` (`{"MaxAge":600}`).
- `ExceptionHandlingMiddleware.ProcessClientErrorAsync` adds, next to `Retry-After`:

  ```
  WWW-Authenticate: Bearer error="insufficient_user_authentication", error_description="A recent sign-in is required", max_age="600"
  ```

  as in RFC 9470, Figure 3. The log stays the existing Warning (`Request rejected with 401 (StepUpRequired)`); Sentry gets no event.
- The body repeats `max_age` because a browser cannot read the header cross-origin.
- Guards throw before any `try` that maps exceptions with `ErrorResponseMapping.ToActionResult`, so the header is never lost.

#### 6. Secrets

`SecretRights` gets `IsReadWithheldByStepUp`, `IsWriteWithheldByStepUp`, `StepUpMaxAge` (init properties, as `IsReadWithheldByActor`) and `public const string WithheldByStepUp = "step-up-required"`.

`GetSecretRightsAsync`, user branch, after the actor rule and outside `#if DEV_AUTH`:

```csharp
var recency = @this.GetSignInRecency();
var readWithheldByStepUp = canReadSecrets && !withheldByActor && !recency.IsRecent;
var writeWithheldByStepUp = canWriteSecrets && !recency.IsRecent;
var user = new KeyValueCaller(
    actor,
    canReadSecrets && !withheldByActor && !readWithheldByStepUp,
    canWriteSecrets && !writeWithheldByStepUp,
    KeyValueChannel.User) { CorrelationId = correlationId };
```

A right is reported withheld only when the role gives it, as for the actor. The actor wins over the step-up for reads: signing in again does not lift `NoSecretReveal`. Machines are untouched (their branch returns first).

`KeyValueAuthorization`:

- `IsWithheld`: reads by the actor, the step-up or mTLS; writes by the step-up or mTLS.
- `Refused` returns a `StorytellerException`: reads `ActorRestricted` (403), then `StepUpRequired` (401), then `MtlsRequired` (403); writes `StepUpRequired`, then `MtlsRequired`; else `AccessDenied`. A user is never withheld by mTLS and a machine never by the actor or the step-up, so only the actor and the step-up can meet.
- `UnderMtlsWriteRuleAsync` becomes `UnderSecretWriteRuleAsync`.

`KeyValuesHttp`:

| Route | Stale sign-in, with the role |
| --- | --- |
| `GetKeyValue`, `GetKeyValueVersion` with `reveal` | The service call goes through `UnderSecretReadRuleAsync`: a secret is refused with 401 `StepUpRequired`, a public pair is answered. The actor and mTLS pre-checks stay as they are. |
| The diff routes | 401 for a secret only (unchanged rule, new reason). |
| `ExportKeyValues` with `IncludeSecrets` | 401 (pre-check). Without secrets: answered. |
| `SetKeyValue`, `DeleteKeyValue`, `ImportKeyValues` | 401 for a secret key or item (`UnderSecretWriteRuleAsync`); public writes are answered. |
| `ProtectKeyValues` | 401 (pre-check). |
| `RestoreKeyValues`, `PurgeKeyValues` | 200 as today; each item the service refused with `Forbidden`/`AccessDenied` carries `ErrorCode` `StepUpRequired` instead, so a client knows what helps. |
| List, versions without reveal | Unchanged. |

Resolved configuration reads:

- `SetResolvedHeaders` sets one reason: `mtls-required`, else `actor-restricted`, else `step-up-required`. The comment that at most one reason applies is corrected.
- `RightsCode` gets `u` for a read right withheld by the step-up, after `a`. Its comment says so.
- The read stays 200: non-secret values load as before, secret bindings stay unresolved.

`KeyValuePolicyHttp.AddRecipient` and `UpdateRecipient` call `EnsureRecentSignIn` after the body is validated. The items that `WithStepUpOutcomes` changes carry the step-up message too, since the service leaves `Message` empty.

#### 7. Roles, members and machine credentials

`request.EnsureRecentSignIn()` runs after the refusals that signing in cannot fix (machines, the request's own role checks, the actor rule, a bad body) and before the operation:

| Route | Where |
| --- | --- |
| `PostGrantPermission` | after `EnsureActorMayGrantRole` |
| `PostRevokePermission` | after `TryValidatePermission` |
| `SetMemberRole` | after `EnsureActorMayGrantRole` |
| `RemoveMember` | after `EnsureUser`, unless the route's `accountId` is the caller's own (`GetIdentityUniqueId`, ordinal): leaving needs no step-up |
| `CreateInvitation` | after `EnsureActorMayGrantRole` |
| `PostMachines` (create) | after `EnsureActorMayGiveOutSecretReads`, before the `try` |
| `PutMachine` (reset) | after `EnsureActorMayGiveOutSecretReads`, before the `try` |
| `PutMachineFederationRules` | after the rules are validated, before the `try` |
| `PutFederatedIssuer` | where the actor rule runs: only when it replaces an issuer |
| `PostSharedCertificate` | after the label is validated, before the `try` |

Not guarded: deleting a machine, an issuer, a certificate or a member's own membership (they give nothing out), resending, revoking, accepting or declining invitations, the machine authentication policy and the key/value policy (they change no holder of a credential; follow-up if needed), creating projects and accounts.

The role checks of grant, revoke, set role and remove member stay in the service, so a stale user without the role gets 401 first and 403 after signing in again. The admin UI offers these actions only to the roles that may use them.

#### 8. Discovery

`GET v1/auth/configuration` adds `"StepUp": { "Enabled": true, "MaxAge": 600 }` (`StepUpConfiguration`, `MaxAge` in seconds; `Enabled` as `IsEnabledFor` decides for this build and provider). Clients show the window and can tell a stale session before they ask; the API still decides.

#### 9. OpenAPI

- `Definitions.Descriptions.ResponseUnauthorizedStepUp`: the credentials are missing or invalid (no body), or the operation needs a recent sign-in: `ErrorResponse` with `ErrorCode` `StepUpRequired`, `Details.MaxAge`, and `WWW-Authenticate: Bearer error="insufficient_user_authentication", max_age` (RFC 9470).
- The routes of sections 6 and 7 that answer the step-up document their 401 as `OpenApiResponseWithBody(Unauthorized, Json, typeof(ErrorResponse), CustomHeaderType = typeof(StepUpChallengeHeaders))`, a new `IOpenApiCustomResponseHeader` with `WWW-Authenticate`.
- `Definitions.Descriptions.SecretsWithheld` lists `mtls-required`, `actor-restricted` and `step-up-required` (it names only the first today), and `ResolvedConfigurationReadResponseHeaders` says so.
- `OperationAccess.StepUp` lists the operation ids, and `OpenApiExportTests` checks that exactly those document the 401 body and header.

#### 10. Tests of Phase 1

- `TestRequests.UserCarrying` and `DelegatedUser` store a recent `SignInRecency` by default, with a parameter for a stale one or none; the private builders of the guarded routes do the same, so the existing tests pass in Debug and Release builds.
- `Backend.Core/test`: `StepUpOptions.IsEnabledFor` per provider and build, the validator's range and whole seconds, the window written out.
- `Access.Federation/test`: a Storyteller user token with `auth_time` gives the claim; without it, none; a non-integer, none.
- `Access.AzureAd/test`: an Entra ID token's `auth_time` keeps its name.
- `Api.Functions/test`:
  - `SignInRecencyTests`: fresh, at the limit, stale, missing, unreadable, slightly and far in the future, an `act` claim, off per provider, off in Debug unless enabled.
  - `BearerAuthenticationMiddlewareTests`/`StorytellerUserTokenAuthenticationTests`: the item for a provider user (fresh, stale), a delegated user whose Storyteller token carries an old `auth_time` (stale), none for a machine.
  - `SecretRightsTests`: stale and missing withhold both rights with the step-up flags only where the role gives them; the actor wins for reads; machines with the same items are unaffected; a recent sign-in changes nothing.
  - `KeyValuesHttpTests`: a stale reveal of a secret is 401 `StepUpRequired` and of a public pair 200; set, delete, import and protect of secrets 401, public writes 200; export with secrets 401; restore items carry `StepUpRequired`.
  - `ConfigurationConditionalReadTests`: the `step-up-required` header on 200 and 304, the fifth tag, the actor before the step-up.
  - `ErrorResponseMappingTests`, `KeyValueErrorMappingTests`: 401, the code, `Details.MaxAge`, the `WWW-Authenticate` header.
  - Grant, revoke, set role, remove member (and leaving), create invitation, machine create and reset, federation rules, issuer replace, shared certificate, recipients: 401 with a stale sign-in, unchanged with a recent one, machines refused as before (`UserOnlyRoutes.ShouldRequireStepUpAsync`).
  - `AuthConfigurationHttpTests`: `StepUp`.
  - `OpenApiExportTests`: section 9.

### Phase 2: The SDKs

#### 11. OpenAPI snapshot and generated clients

- Export the document (`STORYTELLER_OPENAPI_EXPORT`, `OpenApiExportTests`) as `open.api.v0.10.<n>.json` into `Sdk.NSwag/` (kept with the history) and `sdk.typescript/` (replacing the old one), regenerate `Sdk.NSwag/src/ApiSdk.g.cs` with NSwag 14.6.3 and the TypeScript SDK with `npm run generate`, and compare the hand-written `ApiSdk.Conditional.cs` and `ApiSdk.Annotations.cs` with their generated twins. With the 401 body documented, NSwag throws `ApiException<ErrorResponse>` for it, still an `ApiException`.

#### 12. Detecting the step-up

- `Sdk.NSwag/src/StepUp.cs`: `public static bool IsStepUpRequired(this ApiException exception, out TimeSpan? maxAge)`: a 401 whose `ErrorCode` (typed result, or the JSON text) is `StepUpRequired`, or whose `WWW-Authenticate` names `insufficient_user_authentication`; `maxAge` from `max_age` (quoted or not) or `Details.MaxAge`. Plus `SecretsWithheldReasons` with the three values. Tests.
- TypeScript SDK:
  - `lib/errors.ts`: `isStepUpRequired(error)` and `stepUpMaxAge(error)` (from `Details.MaxAge`).
  - `lib/fetcher.ts`: a 401 with a JSON body is classified on `response.clone()` (within the attempt's signal), so the original stays unread for `read` and is still cancelled before a retry. A step-up 401 is neither refreshed nor retried and does not call `onUnauthorized`; it calls the new `onStepUpRequired?(error)` and throws the `StorytellerError`. A retried response is classified the same way. Other 401s keep today's path.
  - `lib/conditional.ts`: `SecretsWithheldReason` (`'mtls-required' | 'actor-restricted' | 'step-up-required' | (string & {})`) and its doc comment.
  - Tests in `fetcher.test.ts`, `errors` and `conditional.test.ts`; the README.

### Phase 3: The clients

#### 13. The admin UI

- `boot/auth.ts`: `SignInOptions.maxAge?: number` and a step-up marker in the AuthKit `state` (`{ returnTo, stepUp: <start time> }`; `onRedirectCallback` reads it back). With `maxAge`, `signIn` builds the URL with `getSignInUrl` (same state, login hint and PKCE verifier) and appends `max_age`, then navigates; without it, nothing changes. `AuthService.getAuthTime()` reads `auth_time` from the current access token (`getClaims` of authkit-js); the mock auth service implements both.
- The auth configuration (discovery) is kept in the boot state, also in mock mode (the mock's `auth/configuration` gains `StepUp`), so screens know `StepUp.Enabled` and `StepUp.MaxAge`.
- A step-up coordinator (`composables/useStepUp.ts` with a small store): the SDK's `onStepUpRequired`, and any bulk item with `ErrorCode` `StepUpRequired` (restore, purge, the import dialog's undo), open one confirm modal however many requests failed ("Confirm it's you": "For your security, this needs a recent sign-in. Sign in again to continue; you come back to this page."; with unsaved changes on the page, it says they are lost). Confirm signs in with `maxAge: 0` (a forced re-authentication: the person confirmed, and a positive `max_age` could be met by WorkOS while the API, whose clock may differ, still finds it stale), the current route as `returnTo` and the user's email as login hint. Cancel leaves the screen's error as it is.
- A loop guard: the time a step-up started travels in the AuthKit state and is recorded only when the callback brings it back. A step-up 401 after such a return whose token's `auth_time` is not newer than that start shows an error ("Your sign-in could not be confirmed. Sign out and sign in again.") instead of another redirect; otherwise the modal opens again.
- Before the person types what a redirect would lose (a new secret value, a secret in the create dialog, a new machine, an invitation, a role change), the screen checks the client hint (the token's `auth_time` against `StepUp.MaxAge`, less a minute) and runs the confirmation first.
- After the return, the screen loads again, so reads (the Resolved tab, the values) run again; a pending reveal (the key of the Reveal that failed) is kept in `sessionStorage` and repeated once. Writes are never replayed: a secret value is never stored. A toast says "You signed in again. Repeat the change if it did not finish."
- `utils/errors.ts`: `describeError` gives `StepUpRequired` its own title and text; `MembersTable`'s message map and the key/value, machine and certificate screens use it.
- `ConfigurationResolved.vue`: when the role gives secrets but the client hint finds the sign-in stale, the alert says that secret values are hidden until the person confirms, with the confirm button, instead of claiming they are included.
- The MSW mock: a "stale sign-in" flag in `sessionStorage` (`window.storytellerMock.staleSignIn()`), under which the guarded handlers answer the step-up 401, restore and purge answer `StepUpRequired` items, the resolved read leaves secret bindings unresolved, and the mock token's `auth_time` is old; the mock sign-in with `maxAge` clears it. Off by default, so the automation tests' mock profile is unchanged.
- Tests: Vitest for `signIn` with `maxAge` and the state marker, the coordinator (one modal, the loop guard, the return, bulk items), the error text, the hint, the mock; a Playwright flow (reveal with a stale sign-in, confirm, the value shows).
- `src/Platform/ui.admin/README.md`: the step-up, and MFA in the WorkOS dashboard checklist.

#### 14. `sform`

- `ApiRefusal.Of` and `KeyValueConsoleExtensions.WriteApiError` use `IsStepUpRequired`: `! This needs a recent sign-in: a sign-in counts for 10 minutes.`, the error code, and a hint, exit 44, no machine hint. `sform account logout` clears only the local session, and a device code approved in a browser that is still signed in to AuthKit may not count as a new sign-in, so the hint says: sign out in the browser first (the admin UI's Sign out ends the WorkOS session) or approve the code in a private window, then `sform account logout` and `sform account`, and repeat the command.
- `config get` and `config export`: a withheld reason gets its own hint (`ConfigurationReads.WithheldHint`): mTLS for `mtls-required`, the actor for `actor-restricted`, signing in again for `step-up-required`.
- Per-item results of restore and purge show the step-up text of the items.
- Tests next to `ApiRefusalTests`, `ConfigGetCommandTests`, `ConfigExportCommandTests`. No new command or option, so the coverage checks and the Testiny catalogue do not change.

#### 15. The MCP server and the .NET provider

- `ApiErrors.ToResult` checks the step-up first (the API answers the actor rule before it): `StepUpRequiredCode` and a fixed text: "The Storyteller API needs a recent sign-in of the person for this (within the last {N}): reading or writing secret values, creating or resetting machine credentials, changing federation rules. A reconnect that reuses the person's browser session keeps the old sign-in: ask the person to sign out at the identity provider first (for AuthKit, Sign out in the admin UI), then reconnect the MCP server in the client and retry, or to make the change in the admin UI. The assistant cannot do this itself." `Read` also reads the code of a plain `ApiException`'s JSON. Tests.
- `get_resolved_configuration` uses the conditional read and, with `step-up-required`, says that secret bindings stayed unresolved and why; bulk tools pass the item texts on.
- `DocumentReader`: the message of a withheld read names the reason's remedy (mTLS, the actor, a new sign-in). Tests.

#### 16. Deployment settings

`Deployment/modules/functions.bicep` writes the whole set of app settings on each run, so a setting added by hand is removed. `main.bicep` and `functions.bicep` get two optional parameters, `stepUpEnabled` (`''`, `'true'` or `'false'`) and `stepUpMaxAge` (`''` or `hh:mm:ss`), emitted as `Auth__StepUp__Enabled` and `Auth__StepUp__MaxAge` only when set (as `sentryDsnSetting`); `prod.bicepparam` leaves both empty, and the README parameter table lists them. `false` is the escape hatch if a client cannot satisfy the step-up.

#### 17. Rollout and limits

- Deploy the admin UI together with the API (or first): an older admin UI answers a step-up 401 with a plain sign-in redirect and never succeeds. Installed `sform` versions print their generic 401 text.
- For SSO users, and for any user with a live identity provider session, the step-up protects against a token taken out of the browser, not against code running in the admin UI's origin: such code can start the `max_age` sign-in itself. authentication.md says so.
- Whether WorkOS counts a device-code approval in a signed-in browser as an active authentication is not documented; if it does, the device flow is a way around the step-up for code that controls the browser. authentication.md says so, and the review lists a staging check.

### Phase 4: Documentation

#### 18. Documents

- `docs/Platform/Storyteller/authentication.md`: a section "Recent sign-in (step-up)": the setting, the providers (on by default under AuthKit only, and why), Debug builds, what is withheld and what answers 401, the response, the discovery, machines never, actors (the actor wins), the MCP and `sform` paths, the clock skew. "Claims seen by the API": `auth_time`. "Storyteller user tokens": the claim reaches the API. "401 and 403": the step-up row with its body and header. "Not available yet": the `WWW-Authenticate` line. "AuthKit dashboard setup": MFA. A section "MFA policy": MFA required in the production WorkOS environment for every sign-in without SSO (Magic Auth there today), the customer's identity provider responsible for SSO users, and the limit: `auth_time` proves a recent sign-in, not MFA; WorkOS enforces MFA, the API cannot check it.
- `access.md`: the roles, the actors and the error codes (`401 StepUpRequired`).
- `key-values.md`: secret delivery (the step-up row and paragraph), the error codes, who may do what, the CLI and the admin UI.
- `inheritance.md`: five rights values, the header table.
- `mcp.md`: the step-up message and when the MCP meets it.
- `configuration-provider.md`, `conformance.md` (no vector change), `Sdk.NSwag/package-readme.md`, the SDK READMEs.
- `docs/42for.net`: `platform/admin-ui.md` (signing in again to confirm), `platform/using-the-platform.md` (the withheld value), `cli/sform.md` (the sign-in window, the message, exit code 44), `cli/github-action.md` (creating the machine needs a recent sign-in).
- `docs/Platform/Deployment`: `cookbook-azure-production.md` step 13 and `runbook-azure-production.md` step 5: MFA Required in the production environment; the verify steps: a stale sign-in gets the step-up. `decisions.md`: the window can change any time.
- `docs/Platform/UI.Admin.AutomationTests/automation-tests.md` and `docs/Platform/Cli.AutomationTests/automation-tests.md`: a run against a deployed target longer than the window meets the step-up; the pools stay in staging without MFA.

### Decisions

- **D1. The middleware judges, the handlers read an item.** One evaluation per request, the same time for every check, no DI in static helpers.
- **D2. No `auth_time`, no recent sign-in.** As the issue asks; a value more than five minutes in the future counts as none.
- **D3. On by default only under AuthKit.** Its tokens carry `auth_time` and its admin UI can ask for a new sign-in. Entra ID access tokens carry no `auth_time` (Entra ID's own step-up is Conditional Access, out of scope), and no platform client can ask Keycloak for a new sign-in yet, so both stay off unless set. Debug builds keep skipping the user checks unless it is set.
- **D4. The actor wins for reads.** A new sign-in does not lift `NoSecretReveal`, so the client learns `ActorRestricted`.
- **D5. A reveal asks for a sign-in only for a secret.** Unlike the actor's pre-check, a public reveal stays answered.
- **D6. 401 with a body and `WWW-Authenticate`.** RFC 9470 for clients that read headers, the body for browsers that cannot.
- **D7. The admin UI forces the re-authentication (`max_age=0`).** The person confirmed first; a positive `max_age` and two clocks could loop.
- **D8. No replay of writes.** Secret values are never kept across the redirect; reads run again on return.
- **D9. Leaving and deleting need no step-up.** They give nothing out.
- **D10. Names.** `StepUpRequired`, `step-up-required`, ETag letter `u`, `Auth:StepUp:MaxAge` as a `TimeSpan`.
- **D11. `sform` only explains.** The device flow has no `max_age`; signing out in the browser and in `sform`, then in again, is the documented way.
- **D12. An impersonated token is never recent.** Its `auth_time` is the impersonator's sign-in.

### Out of scope

- Proving MFA in the API (WorkOS returns the method at sign-in, not in the access token).
- Entra ID Conditional Access authentication contexts (claims challenges).
- A forced fresh sign-in option in `sform`, and refreshing the MCP client's sign-in.
- Guarding the key/value policy and the machine authentication policy.
- Re-signing in the automation tests' cached sessions on a deployed target (a follow-up).
- Turning on MFA in the production WorkOS environment: the documents say it is required; the owner switches it in the dashboard.

### Phases and commits

1. `docs(platform): plan the step-up of secrets, roles and machine credentials (424-264)`: this spec.
2. `feat(platform): require a recent sign-in for secrets, roles and machine credentials (424-264)`: Phase 1.
3. `chore(platform): regenerate the SDKs from OpenAPI 0.10.<n> (424-264)` and `feat(platform): detect the step-up in the SDKs (424-264)`: Phase 2.
4. `feat(platform): confirm a recent sign-in in the admin UI, sform and the MCP server (424-264)`: Phase 3.
5. `docs(platform): document the step-up and the MFA policy (424-264)`: Phase 4, with the Bicep parameters of section 16.
6. `docs(platform): review the step-up of secrets, roles and machine credentials (424-264)`: the reviews.
