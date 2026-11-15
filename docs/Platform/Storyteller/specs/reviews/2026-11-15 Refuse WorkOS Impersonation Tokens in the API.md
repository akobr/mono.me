# Refuse WorkOS Impersonation Tokens in the API

Linear: [424-262](https://linear.app/424fun/issue/424-262/refuse-workos-impersonation-tokens-in-the-api-and-lock-down-who-can).

## Overview

This review covers the whole of the spec [Refuse WorkOS Impersonation Tokens in the API](../2026-11-15%20Refuse%20WorkOS%20Impersonation%20Tokens%20in%20the%20API.md), all three phases:

1. The API refuses an AuthKit user token that carries `act`, the token of a WorkOS impersonation.
2. The API logs a changed user name where it reads the account anyway.
3. The documentation records the WorkOS trust boundary, the dashboard controls and the wording for the trust page and the DPA annex.

It was implemented as planned, with the deviations named below. A review of the branch by four reviewers, with a skeptic per finding, confirmed six small findings, all fixed in `e9016431c`.

## What Was Done

### 1. `AuthKitTokenSource`

`Access.AuthKit/src/AuthKitTokenSource.cs`:

- `ValidateTokenAsync(rawToken, cancellationToken)` validates with the source's own audience. The claims-only `ValidateAsync` delegates to it and behaves as before. `AuthKitMachineTokenValidator` still uses `ValidateAsync`.
- `HasActor(SecurityToken)` returns `token is not JsonWebToken jwt || jwt.TryGetPayloadValue<object>("act", out _)`. It is true for a top-level `act` of any value: an object, a string, `null`, `[]`, `{}`, a number, a boolean, a duplicated member in either order, and a JSON-escaped name. It is false for `ACT`, `act ` and `may_act`.
- `DescribeActor(SecurityToken)` gives `act.sub` of an object or a string `act` as it is. Anything else becomes `NoActorSubject` (`(no act.sub)`). The value is cut to `MaxLoggedLength` (256), and control characters become `?`.

### 2. `AuthKitBearerTokenValidator`

- Both constructors take `ILogger<AuthKitBearerTokenValidator>` after the environment: `(options, normalizer, environment, logger)` and `(options, normalizer, environment, logger, keyManager)`. DI keeps choosing the shorter one, and `AuthKitEntryPoint` is unchanged. `AuthKitRegistrationTests` resolves `IBearerTokenValidator` from a built host and still passes.
- `ValidateAsync` runs in this order:
  1. the issuer;
  2. `ValidateTokenAsync`, where an invalid token returns `null` and is not logged;
  3. a `user_` subject, otherwise `null`;
  4. `HasActor`, which logs one Warning, `An AuthKit user token of {Subject} is refused: it names an actor (act.sub {Actor}), a WorkOS impersonation`, and returns `null`;
  5. the normalized claims.
- The comment above the refusal names the separate decision that supporting impersonation would need: `Via`, the secret withholding and a log per request.
- No machine validator takes a `user_` token, so the middleware answers 401 with its own Warning, as the spec expected. `IBearerTokenValidator` is unchanged.

### 3. `AuthKitUserSubjectTokenValidator`

The exchange refuses with `AuthKitTokenSource.HasActor(result.SecurityToken)` in place of `UserSubjectTokenRules.HasActor(rawClaims)`, so `"act": []` is refused there too. The reason text is unchanged.

### 4. Tests

- `Access.AuthKit/test/CapturingLogger.cs` is new, a copy of the `Api.Functions/test` one.
- `AuthKitBearerTokenValidatorTests`:
  - The fixture has a `Logger` and `CreateRawToken` (RS256 over a payload as written, for duplicates and escapes).
  - The object case logs exactly one Warning with `Subject` and `Actor`, without the token, the `act` JSON or an exception.
  - A theory of 13 payload shapes checks each refusal and the logged actor.
  - `ACT`, `act ` and `may_act` are accepted.
  - The actor is cut and its control characters replaced.
  - A wrong key, an expired token and a non-user `sub` with `act` are refused without a log.
  - The regression token logs nothing.
  - `Source_ActClaims_DoNotShowAnEmptyArray` pins the IdentityModel behaviour that makes the payload check necessary.
- `AuthKitUserSubjectTokenValidatorTests.TokenWithAnEmptyActArray_IsRefused`.
- The explicit constructions take `NullLogger<AuthKitBearerTokenValidator>.Instance`. These are in `AuthKitUserSubjectTokenValidatorTests`, `AuthKitMachineTokenValidationTests` and `Api.Functions/test/UserTokenExchangeProviderTests`.
- `Api.Functions/test/AuthKitImpersonationAuthenticationTests` runs in the `BearerValidationMode` collection, with validation on. It has the real validator, the real middleware and a stub key set:
  - an impersonation token gets 401 on `v1/access/account` and on the anonymous `v1/auth/configuration`, with no identity stored, the Storyteller validator not asked, and the validator's Warning logged;
  - with a shared custom-domain issuer, the real `AuthKitMachineTokenValidator` does not take it either;
  - the same token without `act` passes.
- `FunctionTestDoubles.CreateRequest` takes an optional `url`, which the route theory needs.
- Mutation check: with the refusal turned off, 15 AuthKit tests and 3 of the 4 middleware tests fail.

### 5. `AccountUserNameCheck`

`Backend.Core/src/Accessing/AccountUserNameCheck.cs`, as the spec describes:

- `UserNameClaimTypes`, `GetUserName` and `LogIfChanged`.
- Two messages: a plain one, and one for an elevated account that names at most `MaxLoggedAccessPoints` (20) points, then "and N more".
- The skips: no name in the token, a blank stored name, a stored name equal to the account id, and names equal when compared trimmed and ignoring case.
- `HttpRequestDataExtensions.UniqueNameClaimTypes` is now `[.. AccountUserNameCheck.UserNameClaimTypes]`.

**Deviation.** The spec says "the first non-blank value". `GetUserName` takes the first claim that is present, as registration's `GetClaim` does, so the check reads the claims registration read. A blank first claim gives no evidence, so nothing is logged. The docs describe it this way.

### 6. Where it runs

- `AccessHttp.GetAccount`, after the read, with channel `account`. It is skipped for a delegated request (`TryGetDelegatedActor`) and when `BearerValidationMode.DecodeWithoutValidation` is set.
- `UserTokenExchangeService.ExchangeAsync`, after the account is found and before the replay limit, with channel `token-exchange`. A later refusal, by the replay limit or the scopes, has logged the change already. That is intended: the attempt is evidence too.

### 7. Tests

- `Backend.Core/test/AccountUserNameCheckTests` (15 cases) and a `CapturingLogger` copy.
- `Api.Functions/test/AccountUserNameHttpTests`, in the `BearerValidationMode` collection: a change is logged with the elevated message, the stored name is not, and neither is a delegated request or one under `DecodeWithoutValidation`.
- `Access.Federation/test/UserTokenExchangeServiceTests`: an exchange with another user name logs it and still issues the token. With the stored name, nothing is logged.

### 8. `authentication.md`

- "How AuthKit tokens are checked": the `act` bullet (the shapes, why the payload is read, 401, the Warning, what is never logged), the machine-credential boundary, and the decision that supporting impersonation would need. The DEV_AUTH paragraph says Debug builds do not apply the refusal.
- "AuthKit claims": a row for `act`.
- A new section, "Changed user names": what is compared, where, the skips, the two messages, a KQL query for an alert, and the known false positives. Those are renames at the provider and the base account seeded with DbCreator's built-in user name.
- "AuthKit dashboard setup": step 8, impersonation off, and step 9, named members with the least role and MFA.
- "Subject tokens" and "401 and 403" name the AuthKit `act` rule.

### 9. `decisions.md`

The new section "Who can sign in as a user (the WorkOS trust boundary)" has:

- the boundary;
- the three paths, each with what covers it, and the 2026-11-15 check through the WorkOS API that impersonation is off in Production and Staging;
- the privacy of the logs (Application Insights for 30 days; Sentry only as breadcrumbs, with emails scrubbed);
- the alternative providers and their limits;
- the wording for the trust page and the DPA annex;
- the evidence for the ISO 27001 supplier notes.

**Deviation.** The review reframed the alternatives. The heading is now "For customers who refuse WorkOS or a US identity provider", because Entra ID is run by a US company too. The customer-facing text offers "an identity provider of their own": Keycloak, which can be hosted in the EU, or their own Entra ID tenant.

### 10. Runbook, cookbook and the admin UI checklist

- Runbook step 5 has items 9 (impersonation off) and 10 (dashboard members). Step 12 checks the dashboard of both environments, records the result in the ISO 27001 supplier notes, and runs a KQL query over the two Warnings.
- Cookbook: a "Never do" bullet, item 8 of step 13 and a "Who can sign in" row in step 17.
- The admin UI's WorkOS checklist: impersonation off.

**Deviation.** The spec had step 12 verify "that a token with `act` gets 401 with the Warning in Application Insights". To do that in production, impersonation would have to be turned on, which the same change forbids. The runbook says so instead: "Don't turn impersonation on to try the refusal: the API's tests cover it."

## Review

Four reviewers covered security and correctness of the refusal, the user name check, the tests, and the documentation. A skeptic tried to refute each finding.

**Confirmed and fixed** (`e9016431c`):

1. The theory row meant for a JSON-escaped member name used a C# escape. The token therefore carried a plain `act`, which repeated the object case. The row now keeps the backslash, so the payload holds the JSON escape. It still passes: IdentityModel unescapes the name, and `HasActor` sees it.
2. The trust-page wording said the API logs any sign-in under another email. It logs only at `GET v1/access/account` and in the exchange. The wording now names the admin application and the MCP server.
3. The wording said a dedicated deployment is administered "through the API and the CLI". `sform` has no interactive Keycloak sign-in, so it now says that the admin web application is not available.
4. The wording offered Entra ID to customers who refuse a US identity provider. That is reframed as in section 9.
5. Path 1 read as if only an Admin can impersonate. Once an Admin turns impersonation on, any dashboard member can.
6. `authentication.md` said "step 4 of the user token exchange", a number from a code comment that the linked list does not use. It now names the step.

A vacuous assertion (`ShouldNotContain(act)` on a token without `act`) was dropped too.

**Refuted** (kept as they are):

- The user name check runs before the exchange's replay limit and scope refusal. This is intended (section 6).
- `GetUserName` takes the first claim present rather than the first non-blank one. This matches registration (section 5).
- The worker's default adaptive sampling may drop traces under load. It is not introduced here, and the Warnings are not dropped by any filter.
- The spec's "Sentry gets neither" contradicts the docs. The spec's own Current State already says that a Warning is a breadcrumb. The docs are right, and the spec is not changed.

## Validation

- `dotnet test --project src/Platform/Storyteller/Access.AuthKit/test`: 203 passed.
- `dotnet test --project src/Platform/Storyteller/Backend.Core/test`: 1402 passed.
- `dotnet test --project src/Platform/Storyteller/Access.Federation/test`: 474 passed.
- `dotnet test --project src/Platform/Storyteller/Api.Functions/test` (Debug): 3093 passed.
- With `-c Release`, `AuthKitImpersonationAuthenticationTests`, `AccountUserNameHttpTests`, `AccessPointHttpTests` and `UserTokenExchangeProviderTests` passed.
- No new StyleCop warning in the changed files.

## Notes

- **WorkOS, checked 2026-11-15 through the WorkOS API (read only):** `isImpersonationEnabled` is `false` in Production and in Staging. The dashboard members were checked too; the result is in the Linear issue, not here.
- **Outside the repository.** The trust page, the DPA annex and the ISO 27001 supplier notes still need the wording and the evidence of `decisions.md`. Their acceptance criteria close only when they are updated.
- **The base account.** If the production base account was seeded with DbCreator's `BASE_ACCOUNT_USER_NAME` and the owner signs in under another email, the elevated message appears at each session start of the owner. Nothing updates `UserName` yet.

## Left for later

- The payload-level `act` check in the Entra ID and Keycloak subject-token validators (`UserSubjectTokenRules.HasActor` on claims).
- Refreshing the stored `UserName` (a product decision), a per-request user name check, and the Application Insights alert rule on the elevated message (Bicep `scheduledQueryRules` with an action group).
- Supporting impersonation, if a support case ever needs it: `act.sub` as `Via`, no secrets, and a log per request.
