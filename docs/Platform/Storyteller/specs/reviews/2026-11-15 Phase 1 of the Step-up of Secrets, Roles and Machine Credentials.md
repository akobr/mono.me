# Phase 1 of the Step-up of Secrets, Roles and Machine Credentials

Linear: [424-264](https://linear.app/424fun/issue/424-264/require-recent-sign-in-step-up-before-users-read-or-write-secrets-and).

## Overview

This review covers Phase 1 (the API, sections 1-10) of the spec [Require a Recent Sign-in for Secrets, Roles and Machine Credentials](../2026-11-15%20Require%20a%20Recent%20Sign-in%20for%20Secrets,%20Roles%20and%20Machine%20Credentials.md). Three critics (security, feasibility against the code, clients) reviewed the first draft of the spec before it was committed (`c3ae9924c`), and their findings went into it: on by default under AuthKit only, an impersonated token never recent, the guard of the federation rules after the validation, two 401 descriptions, and the Bicep parameters of Phase 4. Phase 1 is commit `c7057e9d4`. The tests of `Api.Functions` pass in Debug and Release builds (3,139 each at that commit, 3,141 with the route tests the review added), and so do those of `Backend.Core` (1,411), `Access.Federation` (478), `Access.AzureAd` (141), `Access.AuthKit` (180) and `Access.Keycloak` (138).

## What Was Done

### 1. The setting `Auth:StepUp`

- `Backend.Core/src/Accessing/StepUpOptions.cs`: `Enabled` (`bool?`), `MaxAge` (`TimeSpan`, 10 minutes), `DefaultMaxAge`, `MinMaxAge` (1 minute), `MaxMaxAge` (24 hours), `ClockSkew` (5 minutes) and `IsEnabledFor(provider, developmentAuthentication)`: `Enabled ?? (!developmentAuthentication && provider == AuthKit)`.
- `StepUpOptionsValidator`: the range, and whole seconds. Its message names the `hh:mm:ss` format, because a bare `600` binds as 600 days.
- `UserAuthenticationEntryPoint.AddUserAuthenticationOptions` binds and validates the section at start, and adds `TryAddSingleton(TimeProvider.System)`.
- **Deviation from the first draft:** on by default under AuthKit only. Under Keycloak no client of the platform can ask for a new sign-in (the admin UI supports AuthKit only, `sform` Entra ID and AuthKit), so the default there would have locked users out after ten minutes.

### 2. `auth_time` on every user request

- `StorytellerTokenValidator.ValidateUser` adds an `auth_time` claim (invariant Unix seconds, `ClaimValueTypes.Integer64`) when the validated payload has a non-negative integer `auth_time`; a string, a fraction, a negative number, `null` or `true` give none. Its comment says so.
- `UserSubjectTokenRules.GetAuthTime` is reused on the claims of the request, with `AuthTimeClaimType`.
- **Deviation:** the spec's draft also read `ClaimTypes.AuthenticationInstant`. A test of `EntraIdBearerTokenValidator` with an `auth_time` showed that IdentityModel 8.23.0 keeps the name, so the alias was dropped.

### 3. The middleware judges the sign-in once

- `Api.Functions/src/Security/SignInRecency.cs`: `SignInRecency(IsRecent, MaxAge)`, `NotRequired`, `Stale` and `Evaluate`, as planned, plus: a token with an `act` claim (a WorkOS impersonation) is never recent.
- `FunctionContextItemKeys.SignInRecency`.
- `BearerAuthenticationMiddleware.StoreIdentity` stores `JudgeSignIn(context, claims)` for every user result (validated, `DEV_AUTH` decoded, delegated), never for a machine. `JudgeSignIn` reads `IOptions<StepUpOptions>`, the provider of `IOptions<UserAuthenticationOptions>` and `TimeProvider` from `context.InstanceServices`, with their defaults.

### 4. Reading it downstream

`HttpRequestDataExtensions.GetSignInRecency`, `HasRecentSignIn` and `EnsureRecentSignIn`, as planned. Machines read as recent; a user without the item is recent in Debug builds and stale in Release builds.

### 5. The step-up error

- `ErrorCodes.StepUpRequired`.
- `Backend.Core/src/StepUpRequiredException.cs` (a `StorytellerException`, not a `SecurityTokenException`): `MaxAge`, `MaxAgeSeconds`, and `Describe(duration)` for the message, `This operation needs a recent sign-in: sign in again, then repeat it. A sign-in counts for 10 minutes.`
- `ErrorResponseMapping`: 401 with `Details = new StepUpDetails(MaxAge)` (`Api.Functions/src/Models/StepUpDetails.cs`).
- `ExceptionHandlingMiddleware.ProcessClientErrorAsync` adds `WWW-Authenticate` from `ErrorHandling/StepUpChallenge.cs`: `Bearer error="insufficient_user_authentication", error_description="A recent sign-in is required", max_age="600"`, quoted as in RFC 9470, Figure 3.

### 6. Secrets

- `SecretRights`: `WithheldByStepUp`, `IsReadWithheldByStepUp`, `IsWriteWithheldByStepUp`, `StepUpMaxAge`, and `ReadWithheldReason` (mTLS, then the actor, then the step-up), which `ConfigurationHttp.SetResolvedHeaders` sends. The old comment that at most one reason applies is corrected there.
- `GetSecretRightsAsync`: the step-up after the actor rule, outside `#if DEV_AUTH`, as planned.
- `KeyValueAuthorization` (`V1/KeyValueRequests.cs`): `IsWithheld` and `Refused` (now a `StorytellerException`), `UnderMtlsWriteRuleAsync` renamed `UnderSecretWriteRuleAsync`, and `WithStepUpOutcomes` for restore and purge, which also sets the item's `Message` (the service leaves it empty).
- `KeyValuesHttp`: the reveals of `GetKeyValue` and `GetKeyValueVersion` go through `UnderSecretReadRuleAsync`, so only a secret needs a sign-in; restore and purge return `WithStepUpOutcomes`.
- `ConfigurationEntityTags.RightsCode`: `u`.
- `KeyValuePolicyHttp.AddRecipient` and `UpdateRecipient`: `EnsureRecentSignIn` after the body is validated.

### 7. Roles, members and machine credentials

`EnsureRecentSignIn` in `PostGrantPermission`, `PostRevokePermission`, `SetMemberRole`, `RemoveMember` (not for oneself), `CreateInvitation`, `PostMachines` and `PutMachine` (before their `try`), `PutMachineFederationRules` (after the rules are validated, before the `try`), `PutFederatedIssuer` (a replace only) and `PostSharedCertificate` (after the label, before the `try`).

### 8. Discovery

`V1/Models/StepUpConfiguration.cs` (`Enabled`, `MaxAge` in seconds), `AuthConfiguration.StepUp`, and `AuthConfigurationHttp.Describe(options, tokenExchangeOptions, stepUpOptions)`; the operation's description names it.

### 9. OpenAPI

- `Definitions.Descriptions`: `SecretsWithheld` lists the three values; `ResponseUnauthorizedStepUp` and `ResponseUnauthorizedStepUpUserOnly` are the 401 texts of the callers plus `StepUpUnauthorized`; `StepUpChallenge` describes the header.
- **Deviation:** two descriptions instead of one, so the user-only routes keep the `User.Impersonation` text that `Document_EveryOperationForMachines_NamesItsScopesInIts403` checks, and the key/value routes keep the machine text.
- `OpenApi/StepUpChallengeHeaders.cs` and `OperationAccess.StepUp` (21 operation ids); the routes of sections 6 and 7 document their 401 with `ErrorResponse` and the header. `ResolvedConfigurationReadResponseHeaders`' comment names the three values.

### 10. Tests of Phase 1

- `TestRequests`: `RecentSignIn`, `StaleSignIn`, `WithSignIn`, and a recent sign-in on every user of `UserCarrying` and of the private builders of the guarded routes (`MembersHttpTests`, `PermissionHttpTests`, `MachineHttpTests`, `CertificatesHttpTests`, `InvitationsHttpTests`, `FederationHttpTests`). `UserOnlyRoutes.ShouldRequireStepUpAsync`.
- `Backend.Core/test/StepUpOptionsTests.cs`: the binding, the defaults per provider and build, the validator, a host that does not start with a bad window, the written-out window.
- `Access.Federation`: a Storyteller user token carries `auth_time` as a claim, and the exchange's token reaches the validator with it; non-integers give none.
- `Access.AzureAd`: an Entra ID token keeps `auth_time`.
- `Api.Functions/test/StepUpAuthenticationTests.cs`: the judgement of provider users at eight ages (in the window, at its limit, past it, the skew, none), of delegated users, of an impersonated token, none for machines, the defaults, the `DEV_AUTH` decode path, a request without the item, `EnsureRecentSignIn`.
- `SecretRightsTests`, `KeyValuesHttpTests`, `ConfigurationConditionalReadTests` (the header on 200 and 304, the fifth tag, the actor first), `KeyValueErrorMappingTests` (the mapping and the challenge header), `AuthConfigurationHttpTests`, `OpenApiExportTests` (`Document_StepUpOperations_DocumentTheChallengeInTheir401`), and the route classes of section 7.
