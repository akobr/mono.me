# Review Findings of the Step-up of Secrets, Roles and Machine Credentials

Linear: [424-264](https://linear.app/424fun/issue/424-264/require-recent-sign-in-step-up-before-users-read-or-write-secrets-and).

## Overview

This review covers the fixes of the automated reviews (CodeRabbit and Grok) of PR #203, for the spec [Require a Recent Sign-in for Secrets, Roles and Machine Credentials](../2026-11-15%20Require%20a%20Recent%20Sign-in%20for%20Secrets,%20Roles%20and%20Machine%20Credentials.md). Every finding was checked against the code of the branch rebased on `main`; the ones that still held are fixed, the others are listed with the reason they were left. The API tests pass in Debug and Release (3228), and so do the .NET SDK (400), CLI (2267), MCP (774), TypeScript SDK (367) and admin UI (2131) tests; the admin UI lints and builds, and `main.bicep` builds.

## What Was Done

### 1. The API

- `HttpRequestDataExtensions.GetSignInRecency`: a user request without the bearer middleware's judgement now follows the host's `Auth:StepUp` and `Auth:Provider` when they are registered: no step-up where they turn it off (Entra ID and Keycloak by default, or `Enabled: false`), with their window. Release builds still find no recent sign-in where the step-up is on or no settings are registered. `StepUpAuthenticationTests.UserWithoutAJudgement_FollowsTheSettingsOfTheHost`; `PermissionHttpTests` no longer disposes the services of its requests before they are read.
- `KeyValueAuthorization.WithStepUpOutcomes`: checked against `CosmosKeyValueService`. In a restore and a purge the only item with `Forbidden` and `AccessDenied` is the secret-rights refusal (the mTLS and actor refusals have their own codes), and while the write right is withheld by the step-up nothing else removes it, so the renaming is right. The test now pins that an item with another code keeps it.

### 2. The SDKs

- `Sdk.NSwag` `StepUp`: the challenge is read as auth-params (a token or a quoted string, left to right), so `max_age` or the error inside a quoted `error_description` names nothing; the error must be the `error` parameter. Tests for both.
- TypeScript `failOnStepUp`: a throwing `onStepUpRequired` no longer replaces the step-up error. Test.
- MCP `ApiErrors`: two stray blank lines.

### 3. The admin UI

- `MembersTable.changeRole` keeps the access point it was asked on and drops the change when the table moved meanwhile.
- `KeyValueDetail` and `KeyValueVersions` take the target of a Reveal before the request. `useRevealedValue` drops a value that arrives after `hide()`, and the detail hides on a view change (the route keeps the component).
- `InviteDialog` counts the address still being typed as unsaved.
- `ConfigurationResolved`: within 30 seconds of the window's end it says "Secret values may be hidden", because the browser knows only when the response arrived by its own clock.
- The mock's import refuses a value of another level over an active secret, as the API's plan does.

### 4. The documents

- `cli/sform.md`: the step-up applies where the platform turns it on; a resolved read is the exception (a warning, exit code `0`; `story config export` stops with `2`). The same qualification in `cli/github-action.md` and the GitHub Action README.
- `inheritance.md`: the first read after a new sign-in answers `200` when it sends its tag; `If-None-Match: *` still answers `304`.
- Deployment README, `main.bicep` and `modules/functions.bicep`: `stepUpMaxAge` is whole seconds.
- `authentication.md`, "Who is judged": a delegated token of another application under Entra ID is a machine, not judged, with only a registered machine's rights.

### 5. Left as they are

- **The issuer replace is not conditional on the issuer read (FederationHttp).** Closing the window between the read and `SetAsync` needs an ETag in `IFederatedIssuerStore` and its Cosmos implementation; abusing it needs a legitimate create of that name in the window, and the issuer's rules need a step-up of their own. The actor check has the same window. Not worth the change here.
- **The step-up before the role checks of the service** (removing a member, grant and revoke, recipients): documented in `authentication.md` and `access.md` since the branch's review; a new sign-in reveals nothing the role check would not.
- **`Auth:StepUp:Enabled: true` under Entra ID**: an explicit choice, documented ("under Entra ID that is every user, unless their access tokens carry it"); the validator does not refuse it.
- **`UpdateRecipient` guarded for every update**: intentional; removing a recipient stays open as the kill switch.
- **The browser's time of the return in `isSignInOlderThan`**: intentional, so a browser clock ahead of the provider's does not ask again at once after a step-up.
- **The step-up text of the MCP server does not name roles and members**: the server has no tools for them.
- **One shared test of the window's description**: the server's `StepUpRequiredException.Describe` and the SDK's `DescribeWindow` live in assemblies that cannot reference each other; each is tested with the same cases.
- **The service lookups of `JudgeSignIn`**: singletons, once per request; not worth caching.
