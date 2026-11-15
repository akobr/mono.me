# Refuse WorkOS Impersonation Tokens in the API

Linear: [424-262](https://linear.app/424fun/issue/424-262/refuse-workos-impersonation-tokens-in-the-api-and-lock-down-who-can).

Line numbers refer to the tree this plan was written against (`7944671ee`). No application code is changed by this document.

## Problem

2S authorizes a person with the roles stored in Cosmos DB (EU). The identity those roles are applied to comes from the identity provider. Under AuthKit, any valid WorkOS access token whose `sub` is `user_X` gets user X's roles. "WorkOS authenticates, 2S authorizes" holds only while nobody but the person can get a token for them. Three paths give someone else such a token:

1. **WorkOS impersonation.** A WorkOS dashboard Admin can turn on impersonation for an environment and then sign in as any user. The access token carries an `act` claim naming the impersonator (by email). `AuthKitBearerTokenValidator` does not look at `act`, so the API serves the impersonator as the user: full `User.Impersonation` and every role of the user. The audit stamps record the user, not the impersonator, so 2S keeps no trace of the impersonation.
2. **Account takeover through the WorkOS dashboard or API.** A dashboard member can change a user's email or delete their MFA factors, then sign in as that user. The `sub` stays the same, so it is the same 2S account with the same roles.
3. **WorkOS itself**: a leaked signing key, a legal order. Code cannot prevent this.

The user token exchange (`AuthKitUserSubjectTokenValidator`, used by the MCP server) already refuses a subject token with `act`. The direct API path does not, so the two paths disagree.

## Current State

### The direct path

- `Api.Functions/src/Program.cs:42-50` registers, for every HTTP function, `ExceptionHandlingMiddleware`, `TransportSecurityMiddleware`, `MachineAuthenticationMiddleware` and `BearerAuthenticationMiddleware`, in that order. All functions use `AuthorizationLevel.Anonymous`; authentication is the middleware.
- `BearerAuthenticationMiddleware` (`Api.Functions/src/Security/BearerAuthenticationMiddleware.cs`):
  - skips the bearer when a machine credential (API key, certificate) already authenticated the request (`CachedClaims`, lines 42-46);
  - calls `IBearerTokenValidator.ValidateAsync`, and when it returns `null`, the machine validators whose `CanValidate(issuer)` is true (lines 97-101, 174-194);
  - answers **401** when nothing accepts the token (lines 114-119), on every route, the anonymous ones (`v1/auth/configuration`, `.well-known/*`) included;
  - in Debug builds (`DEV_AUTH`, `BearerValidationMode.DecodeWithoutValidation`) decodes tokens it does not own without validating them and never calls the validator (lines 66-86).
- No machine validator accepts an AuthKit user token: `AuthKitMachineTokenValidator` requires `sub == client_id` and a `client_id` that is not `user_…` (`AuthKitClaimNormalizer.TryGetMachineClientId`, lines 73-83); `StorytellerTokenValidator` owns another issuer. So a `null` from the user validator for a `user_` token always ends in 401.
- `Access.AuthKit/src/AuthKitBearerTokenValidator.cs:62-80` checks the issuer, validates through `AuthKitTokenSource.ValidateAsync` (claims only, `AuthKitTokenSource.cs:70-74`), requires `sub` `user_…`, and normalizes. It has no logger. Its constructors are `(options, normalizer, environment)` (lines 20-26, the one DI uses) and `(options, normalizer, environment, keyManager)` (lines 28-57).
- `AuthKitClaimNormalizer.NormalizeUser` passes `act` through (`IsReplaced`, lines 101-105), so today an impersonation token's `act` sits in the request's claims, read by nothing.

### The exchange path

- `AuthKitUserSubjectTokenValidator.ValidateAsync` (`Access.AuthKit/src/AuthKitUserSubjectTokenValidator.cs:87-90`) refuses with `UserSubjectTokenRules.HasActor(rawClaims)`, a claim-type check (`Backend.Core/src/Accessing/UserSubjectTokenRules.cs:37-40`).

### How IdentityModel 8.23.0 shows `act`

Probed with signed RS256 tokens through `JsonWebTokenHandler.ValidateTokenAsync` and the parameters of `AuthKitTokenSource`; every token below is valid:

| Payload | Claims of type `act` | `TryGetPayloadValue<object>("act")` |
|---|---|---|
| `"act": {"sub":"admin@example.com"}` | 1, the JSON text | true, a `JsonElement` object |
| `"act": "admin@example.com"` | 1, the string | true, a string |
| `"act": null` | 1, empty | true, null |
| `"act": []` | **0** | true, an array |
| `"act": ["a","b"]` | 2 | true, an array |
| `"act": {}`, `42`, `true` | 1 | true |
| `"act"` twice, the last `[]` | **0** (the last wins) | true, an array |
| no `act`, or `"ACT"`, or `"act "` | 0 | false |

A claim-type check therefore misses `[]` and a duplicate whose last value is `[]`. Such a token is signed by WorkOS, so only WorkOS could produce it; the check should still fail closed. `TryGetPayloadValue<object>` is true exactly when a top-level `act` member is present, whatever its value.

### Accounts and user names

- `Account` (`Abstractions.Access/src/Model/Account.cs`) has `Id`, `UserName`, `Name` and `AccessMap`. `UserName` is written once, when the account is created (`CosmosAccessService.CreateAccountAsync`, `CosmosInvitationService.EnsureAccountAsync`, DbCreator's seeds), from the first of `preferred_username`, `unique_name`, `upn` (`HttpRequestDataExtensions.UniqueNameClaimTypes`, line 54), and never refreshed.
- Under AuthKit, `preferred_username` is the token's `email` (`AuthKitClaimNormalizer.NormalizeUser`, lines 52-56), from the JWT template, with verified emails required in the production environment.
- DbCreator seeds an owner other than the base account with its account id as its user name (`DbCreator/src/DataSets/DataSetOwner.cs:25`), and the base account with `Constants.BASE_ACCOUNT_USER_NAME`.
- The account is read without extra cost at two places that see the provider's current name: `AccessHttp.GetAccount` (`Api.Functions/src/V1/AccessHttp.cs:66-77`, the admin UI's session start and `sform`'s account commands) and step 4 of `UserTokenExchangeService.ExchangeAsync` (`Access.Federation/src/UserTokenExchangeService.cs:127-136`, every MCP sign-in). On a delegated API request the middleware replaces the token's names with the stored ones (`BearerAuthenticationMiddleware.WithAccountNamesAsync`, lines 218-249), so a comparison there would always be equal.
- `AccountRole` orders `Administrator` (4) below `Owner` (5); an account's own `AccessMap` already lists every point where it is an Administrator or Owner.

### Logs

- The worker sends logs to Application Insights from Information up (`Program.cs:68-69, 86-97`); nothing filters a Warning of `_42.*` categories. The Log Analytics workspace keeps them 30 days (`Deployment/modules/monitoring.bicep:13`). Sentry takes events from Error up, with emails scrubbed; a Warning is only a breadcrumb.
- No Application Insights alert rule exists in `src/Platform/Deployment`.

### The WorkOS environments (checked 2026-11-15 through the WorkOS API)

- Production and Staging: `isImpersonationEnabled: false`.
- The dashboard team has one member, an Admin, with MFA configured, not managed by a directory.

### Documentation

- `docs/Platform/Storyteller/authentication.md`: "How AuthKit tokens are checked" (line 81), the DEV_AUTH paragraph (line 93), "AuthKit claims" (line 125), "AuthKit dashboard setup" (line 177), "Subject tokens" (line 710), "401 and 403" (line 1755). Nothing mentions WorkOS impersonation.
- `docs/Platform/Deployment/decisions.md`, `runbook-azure-production.md` (step 5 WorkOS, step 12 Verify), `cookbook-azure-production.md` (Never do, step 13 WorkOS, step 17 Verify), and the admin UI's WorkOS dashboard checklist (`src/Platform/ui.admin/README.md:161`): no impersonation item.
- The trust page, the DPA annex and the ISO 27001 supplier notes are not in the repository.

## Proposed Changes

### Phase 1: Refuse impersonation tokens

#### 1. `AuthKitTokenSource`

- `public Task<TokenValidationResult> ValidateTokenAsync(string rawToken, CancellationToken cancellationToken)`: the existing overload with the source's own audience (`null` or `[Audience]`). The claims-only `ValidateAsync` delegates to it; its behaviour does not change.
- `public static bool HasActor(SecurityToken token)`: `token is not JsonWebToken jwt || jwt.TryGetPayloadValue<object>(UserSubjectTokenRules.ActorClaimType, out _)`. True for a top-level `act` member of any value, duplicates included; fails closed for a token that is not a `JsonWebToken`.
- `public static string DescribeActor(SecurityToken token)`: the value to log. `act.sub` when `act` is an object with a string `sub`, the string when `act` is a string, `(no act.sub)` otherwise. At most 256 characters, control characters replaced by `?`. Never the raw JSON.

#### 2. `AuthKitBearerTokenValidator`

- Constructors `(options, normalizer, environment, ILogger<AuthKitBearerTokenValidator> logger)` and `(options, normalizer, environment, logger, keyManager)`. The longer one stays a superset, so DI keeps choosing the shorter one (no `IConfigurationManager<JsonWebKeySet>` is registered). The logger is null-checked with the other arguments. `AuthKitEntryPoint` does not change.
- `ValidateAsync`:
  1. Another issuer: `null` (unchanged).
  2. `_source.ValidateTokenAsync(rawToken, cancellationToken)`; not valid: `null`, nothing logged (a forged or expired token names nobody).
  3. `sub` not `user_…`: `null` (unchanged; an M2M token of a custom domain is the machine validator's).
  4. `HasActor(result.SecurityToken)`: one Warning, `null`:
     `An AuthKit user token of {Subject} is refused: it names an actor (act.sub {Actor}), a WorkOS impersonation`
     with `Subject` the validated `sub` and `Actor` from `DescribeActor`. Not the token, not the `act` JSON, no exception.
  5. Otherwise the normalized claims (unchanged).
- A comment says why: 2S does not support impersonated sessions; accepting them would record the user, not the impersonator. Supporting impersonation would be a separate decision: `act.sub` into `AuditActor.Via`, no secret read or write for the session (as `IsReadWithheldByActor` in `GetSecretRightsAsync`), a log of every request.
- `null` keeps the `IBearerTokenValidator` contract. The middleware then asks the machine validators, which never accept a `user_` token, and answers 401 with its own Warning (`Invalid bearer token presented for {Url}`). No terminal-refusal result is added to the interface.

#### 3. `AuthKitUserSubjectTokenValidator`

- The `act` refusal uses `AuthKitTokenSource.HasActor(result.SecurityToken)` too, so the exchange path also refuses `"act": []`. The refusal reason does not change. The Entra ID and Keycloak subject-token validators keep their claim check (see Out of scope).

#### 4. Tests

- `Access.AuthKit/test/AuthKitBearerTokenValidatorTests.cs`:
  - the fixture takes a logger (a new `CapturingLogger<T>` in `Access.AuthKit/test`, as in `Api.Functions/test`) and signs raw payloads (RS256 with its key and `kid`, for shapes a claims dictionary cannot hold: duplicates);
  - a user token with `"act": {"sub":"admin@example.com"}` returns `null` and logs exactly one Warning with `Subject` `user_01` and `Actor` `admin@example.com`, without the token or the `act` JSON, without an exception;
  - `act` as a string, `null`, `[]`, `{}`, `["a"]`, an object without `sub`, and twice in both orders (one ending in `[]`): each returns `null` and logs one Warning;
  - an over-long or control-character actor is cut and cleaned in the log;
  - the same token without `act` still returns the normalized claims, and logs nothing;
  - a token with `act` and a wrong signature, and an M2M-shaped token (`sub` not `user_…`) with `act`, return `null` and log nothing.
- `AuthKitUserSubjectTokenValidatorTests`: a subject token with `"act": []` is refused with the existing reason.
- The explicit constructions take `NullLogger<AuthKitBearerTokenValidator>.Instance` (`AuthKitBearerTokenValidatorTests`, `AuthKitUserSubjectTokenValidatorTests`, `AuthKitMachineTokenValidationTests`, `Api.Functions/test/UserTokenExchangeProviderTests`). `AuthKitRegistrationTests` already resolves `IBearerTokenValidator` from a built host, which proves DI still constructs it.
- `Api.Functions/test/AuthKitImpersonationAuthenticationTests.cs`, in the `BearerValidationMode` collection with `DecodeWithoutValidation` set to false and restored: `BearerAuthenticationMiddleware` with the real `AuthKitBearerTokenValidator` (stub key set) and, as in production, a machine validator that owns the Storyteller issuer:
  - a signed AuthKit user token with `act` gets 401, the next function is not called, no identity is stored, and the validator's Warning is logged;
  - the same token without `act` passes.

### Phase 2: Log a changed user name

The issue's optional point 3. Log only; nothing is refused and the stored name is not changed (it is the evidence).

#### 5. `AccountUserNameCheck` in Backend.Core

`Backend.Core/src/Accessing/AccountUserNameCheck.cs`:

```csharp
public static class AccountUserNameCheck
{
    // The claims the user name is taken from at registration, in that order.
    public static IReadOnlyList<string> UserNameClaimTypes { get; } = ["preferred_username", "unique_name", ClaimTypes.Upn];

    // True when it logged a change.
    public static bool LogIfChanged(ILogger logger, Account account, IEnumerable<Claim> claims, string channel);
}
```

- The current name is the first non-blank value of `UserNameClaimTypes`, trimmed. `HttpRequestDataExtensions` takes its `UniqueNameClaimTypes` from here, so registration and the check read the same claims.
- No evidence, no log: the current name is missing, the stored `UserName` is blank, or the stored name is the account id (an owner DbCreator seeded before the person signed in).
- Equal names, compared trimmed and ignoring case (emails and Entra ID names are not case-sensitive), log nothing.
- Otherwise one Warning, with `AccountId`, `Channel`, `StoredUserName`, `TokenUserName`:
  - an account that is an Administrator or Owner of any point of its `AccessMap` (no extra read; `GetAccessPointsAsync` is not needed): `The user name of the elevated account {AccountId} differs from the stored one at {Channel}: {StoredUserName} -> {TokenUserName} (Administrator or Owner of {ElevatedAccessPoints})`;
  - any other: `The user name of the account {AccountId} differs from the stored one at {Channel}: {StoredUserName} -> {TokenUserName}`.
- Warning, not Error: Error would page through Sentry at every session start of a renamed account. The alert belongs in Application Insights on the elevated message (a query is documented; the alert rule itself is later work).

#### 6. Where it runs

- `AccessHttp.GetAccount`, after the account is read, `channel` `account`. Skipped for a delegated request (`TryGetDelegatedActor`: its names are the stored ones) and under `DEV_AUTH` (`BearerValidationMode.DecodeWithoutValidation`: the token is not verified).
- `UserTokenExchangeService.ExchangeAsync`, after step 4 found the account, `channel` `token-exchange`, on the subject token's normalized claims.
- No check on every request: it would cost nothing more per read, but every role check would log while a change lasts, and the test doubles of `GetAccountRoleAsync` would all change.

#### 7. Tests

- `Backend.Core/test/AccountUserNameCheckTests.cs`: equal (case, spaces), missing claim, blank stored name, stored name equal to the account id, a plain change (Warning with the four values), an elevated change (Administrator, Owner; the elevated message with the points), the claim order (`preferred_username` before `unique_name` before `upn`).
- `Api.Functions/test`: `GetAccount` logs a change, and logs nothing for a delegated request.
- `Access.Federation/test`: an exchange whose subject token carries another email logs the change and still issues the token.

### Phase 3: Documentation and operations

#### 8. `docs/Platform/Storyteller/authentication.md`

- "How AuthKit tokens are checked": a user token with a top-level `act` (any value) gets 401, and the validator logs a Warning with the subject and `act.sub`. Under `DEV_AUTH` the validator does not run, so Debug builds do not apply it; a request that a machine credential already authenticated ignores its bearer token and runs as the machine.
- "AuthKit claims": `act` is not passed through; its token is refused.
- "AuthKit dashboard setup": keep impersonation off in every environment (Authentication → Features), name the dashboard members, require MFA or SSO.
- "Subject tokens": the exchange refuses a top-level `act` of any value.
- "401 and 403": the AuthKit user token with `act`.
- A section on the user name check: what it compares, where, the skips, the two messages, the Application Insights query for an alert, and the known false positives (Entra ID and Keycloak renames, the base account seeded with `BASE_ACCOUNT_USER_NAME`).

#### 9. `docs/Platform/Deployment/decisions.md`

A section "Who can sign in as a user (the WorkOS trust boundary)":

- WorkOS authenticates; the roles in Cosmos DB (EU) authorize.
- The three paths and what covers each: the code refuses impersonation tokens and impersonation stays off; named dashboard members with MFA or SSO, and the user name log; a WorkOS-side compromise is accepted and disclosed.
- Privacy: the refusal logs the impersonator's email, and the user name check the old and new user names, into Application Insights (30 days); Sentry gets neither (Warning).
- The alternatives for customers who refuse a US identity provider: Entra ID and Keycloak (`Auth:Provider`), with their limits: one provider per deployment; switching orphans the accounts (the account ids differ) and nothing migrates them; `sform` cannot sign in under Keycloak; the admin UI signs in through AuthKit only.
- The approved wording for the trust page and the DPA annex, which are kept outside the repository, and where the evidence goes (the ISO 27001 supplier notes).

#### 10. Runbook, cookbook and the admin UI checklist

- `runbook-azure-production.md` step 5: impersonation off (only a dashboard Admin can turn it on, per environment), one or two named members with the least role, MFA for each (or SSO with the identity provider's MFA). Step 12: verify those three, and that a token with `act` gets 401 with the Warning in Application Insights; a WorkOS Admin records the date, the reviewer and the result in the ISO 27001 supplier notes at each verification.
- `cookbook-azure-production.md`: "Never do" gets "turn on WorkOS impersonation"; step 13 and the step 17 table get the same checks.
- `src/Platform/ui.admin/README.md`, "WorkOS dashboard checklist": impersonation off.

### Decisions

- **Payload member, not claim type.** `TryGetPayloadValue<object>` on the validated token is a superset of the claim check, needs no second parse, and does not refuse other duplicated members. (The CodeRabbit plan re-parses the payload with `System.Text.Json`; the probe shows that is not needed.)
- **User subject first, then `act`.** The Warning always names a WorkOS user; an M2M token stays the machine validator's, which never sees impersonation.
- **`null`, not a new result type.** No validator accepts the token after it, so the response is 401 already; the interface stays.
- **DEV_AUTH unchanged.** Debug builds accept unsigned tokens anyway; refusing `act` there would add a provider-neutral rule that validating builds do not have for Entra ID and Keycloak.
- **The user name check now, log only, at two places.** The CodeRabbit plan defers it (unreliable names, per-request cost). Here it runs only where the account is read anyway, skips what is no evidence, and logs at Warning; the per-request check, refreshing the stored name, and the alert rule stay for later.

### Phases and commits

1. `docs(platform): plan the refusal of WorkOS impersonation tokens (424-262)`: this spec.
2. `fix(platform): refuse WorkOS impersonation tokens on API routes (424-262)`: Phase 1.
3. `feat(platform): log a changed user name at the account read and the token exchange (424-262)`: Phase 2.
4. `docs(platform): document the WorkOS trust boundary and the dashboard controls (424-262)`: Phase 3.
5. `docs(platform): review the refusal of WorkOS impersonation tokens (424-262)`: the review.

### Out of scope

- Supporting impersonation (mapping `act.sub` to `Via`, withholding secrets, a log per request).
- The payload-level `act` check in the Entra ID and Keycloak subject-token validators (`UserSubjectTokenRules.HasActor`): only those issuers could sign such a token. The same rule on their direct paths: neither provider is known to issue `act` for an impersonation.
- Refreshing the stored `UserName`, the per-request user name check, and the Application Insights alert rule (Bicep `scheduledQueryRules` with an action group).
- The trust page, the DPA annex and the ISO 27001 supplier notes themselves: their owner copies the wording and records the evidence.
