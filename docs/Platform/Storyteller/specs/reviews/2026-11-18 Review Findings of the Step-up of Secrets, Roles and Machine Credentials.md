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

- **The issuer replace is not conditional on the issuer read (FederationHttp).** Left in the first round as too large; fixed in the second, see section 6.
- **The step-up before the role checks of the service** (removing a member, grant and revoke, recipients): documented in `authentication.md` and `access.md` since the branch's review; a new sign-in reveals nothing the role check would not.
- **`Auth:StepUp:Enabled: true` under Entra ID**: an explicit choice, documented ("under Entra ID that is every user, unless their access tokens carry it"); the validator does not refuse it.
- **`UpdateRecipient` guarded for every update**: intentional; removing a recipient stays open as the kill switch.
- **The browser's time of the return in `isSignInOlderThan`**: intentional, so a browser clock ahead of the provider's does not ask again at once after a step-up.
- **The step-up text of the MCP server does not name roles and members**: the server has no tools for them.
- **One shared test of the window's description**: the server's `StepUpRequiredException.Describe` and the SDK's `DescribeWindow` live in assemblies that cannot reference each other; each is tested with the same cases.
- **The service lookups of `JudgeSignIn`**: singletons, once per request; not worth caching.

### 6. The second review (CodeRabbit CLI)

A local `coderabbit review --agent` of the branch found seven more; all held and are fixed. The API (Debug and Release, 3229), Access.Federation (481), .NET SDK (405), CLI (2267), MCP (774) and admin UI (2134) tests pass; the admin UI lints and builds. The Cosmos test of the issuer store (`FederationPersistenceTests.IssuerStore_CreatesOnly_AnIssuerThatDoesNotExist`) is written but was not run: Docker was not available, and the local emulator is never used for these suites.

- **The issuer that appears after the read.** `IFederatedIssuerStore.CreateAsync` creates and never replaces (`ConflictException`, also when Cosmos DB's create meets an item that appeared since); `PutFederatedIssuer` creates through it when its read found no issuer, so a concurrent create is never replaced without the step-up and the actor check. Both stores; tests of the in-memory store, the handler (a store whose read misses the issuer) and the Cosmos store.
- **Several challenges.** The .NET SDK splits `WWW-Authenticate` into its challenges and takes `max_age` from the one whose `error` is the step-up's; without one, from the only challenge, else from the body.
- **The remedy of `sform`.** The code exists only after `sform account`: sign out in the browser, run `sform account logout` and `sform account`, approve the new code (in a private window if the browser is still signed in), repeat. The same order in `sform.md` and `authentication.md`.
- **`key-values.md`** has the `If-None-Match: *` note of `inheritance.md`.
- **One-time credentials.** `OneTimeSecretDialog` counts as unsaved while it is open, for the shared certificates and the machines alike.
- **A Reveal's own error.** `useRevealedValue.reveal` returns the error of that reveal while it is the current one; `KeyValueDetail` remembers the step-up from it, never from the shared error of another reveal.
- **A storage that refuses the pending Reveal** no longer leaves the confirmation redirecting: the sign-in goes on, and only the repeated Reveal is lost.

### 7. The review of PR #203 after the rebase on the access audit

CodeRabbit's second review of the PR (commit `e42fc03f7`) had 10 Major threads and 64 Minor comments (15 in the review body, 49 in two follow-up comments). Each was checked against the code; 60 are fixed, 14 left with a reason in the reply to each.

- **API.** `IFederatedIssuerStore.ReplaceAsync` (replace-only, 409 when the issuer is gone), so a replace never undoes a concurrent delete. Permanent delete and purge refuse a key with any secret version (a COUNT query, only for callers without the secret-write right); a Replace import checks the right on the version it deletes, and import items refused for the step-up carry `StepUpRequired`. The refusals a new sign-in cannot fix come before the step-up where the handler already has the data (a missing machine on reset, a changed `Issuer`, a missing or non-Federated machine for rules, a project without shared certificates); the members routes keep the service's role checks after it (documented). An explicit `Enabled: true` holds for a request without a judgement in Debug builds too.
- **Clients.** The .NET SDK keeps the scheme of each challenge (the error counts only in Bearer or DPoP); the TypeScript SDK bounds the step-up probe (5 s) and handles a rejecting async hook; `config set -r` says the configuration is stored; MCP retries refused bulk items by their keys.
- **Admin UI.** The step-up sign-in reads the last token's `auth_time` without a refresh; scopes are captured before every sign-in check; the bulk Delete opens one confirmation after the loop; the unsaved-changes warning is live and counts settings and one-time credentials; discovery expires after 5 minutes.
- **Deployment.** `stepUpMaxAge` (a string) became `stepUpMaxAgeSeconds` (`int?`, `@minValue(60)`, `@maxValue(86400)`), so the build and the deployment refuse a window the API would not start with; the template writes the `TimeSpan`.
- **Left.** The generated `ApiSdk.g.cs` (stream reads and the bodyless 401 belong in the NSwag template), the Undo deletes targets across the redirect, the hint's `returnedAt` (kept on purpose), the members' role checks before the step-up, and three mock orders that mirror the API.
- The OpenAPI snapshot is `0.10.50` (replacing `0.10.49` of this branch), and both SDKs are regenerated from it.
