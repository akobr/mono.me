# Phase 4a of Aligning the OpenAPI Document and sform with the Provider Contract

Linear: [424-96](https://linear.app/424fun/issue/424-96/align-openapi-document-and-sform-with-the-provider-contract-security).

## Overview

Phase 4a covers section 10 of [Align the OpenAPI Document and sform with the Provider Contract](../2026-10-26%20Align%20the%20OpenAPI%20Document%20and%20sform%20with%20the%20Provider%20Contract.md), without the `STORYTELLER_KEY` target (phase 4b). `sform` now reads the machine variables through the SDK's shared contract and gets its tokens from the SDK credential: its own token client, discovery, exchange, GitHub request and token cache are gone. What is about the command stays in `sform`: the endpoint it needs, the target, the machine's identity and the certificate/key cross-checks, and the exit codes. Two independent reviews (behaviour parity, spec and tests) followed the implementation; their findings are fixed in the second commit.

| Commit | Content |
|---|---|
| `a4158d74` | Section 10 without the target |
| `bf4b5c57` | Review fixes: a deadline for token requests, `STORYTELLER_API_URL` reported whenever it is set, two tests made honest |

| Project | Passed (end of the phase) |
|---|---|
| `Cli/test` | 777 (793 before: the reading and kind tests moved to the SDK, see below) |
| `Sdk.NSwag/test` | 284 (276 before: 8 cases moved from the CLI) |
| `Configuration/test` | 548, 2 skipped |
| `Configuration.KeyVault/test` | 87 |

## What Was Done

### 10. sform on the shared contract

**Settings.** `MachineSignInSettings` is built from the SDK:
- `IsConfigured` and `CredentialVariables` come from `StorytellerVariables.Credentials`; they never throw and exist for invalid variables too. The GitHub variables alone still configure no machine.
- `StorytellerCredentialVariables.Read` runs once; its error is kept, and `GetKind()` and `GetCredential()` throw a new `AuthenticationException(Configuration)` on every call.
- `Endpoint` (was `ApiUrl`) is `STORYTELLER_ENDPOINT` or the deprecated `STORYTELLER_API_URL`, with the conflict rule. A conflict is kept and reported by `GetKind()`, not thrown while reading. `GetEndpoint()` checks it with `StorytellerUrlRule.RequireEndpoint` and names the variable as it was set.
- `DeprecatedVariables`: the SDK's `DeprecatedNames`, and `STORYTELLER_API_URL` whenever it is set (**review fix**: at first only when it gave the endpoint, which reported a deprecated credential name next to its canonical one but not the endpoint's).
- `MachineSignInKind` maps one to one from `StorytellerCredentialKind`; `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` moved to the new `TargetVariables`.
- **Deviation:** new public members `GetCredential()`, `GetEndpoint()`, `EndpointVariable` and `DeprecatedVariables`. The order of errors changed: a credential error is reported before a missing endpoint.

**Credentials.** `MachineAuthenticationService`:
- loads once: the kind, `CreateCredential`, the identity (structured API key, the SAN of the certificate through `MachineCertificate.ReadIdentity`, the client id), the certificate/key cross-checks, and "a shared certificate needs a key";
- keeps one bound copy (`StorytellerCredentialBinding` with the endpoint, and for a federated machine the organization and project of `BindTarget`), bound again when the target changes, under a lock;
- `GetAccessTokenAsync` and `RenewAccessTokenAsync` answer from `GetAuthorizationAsync`; renewal now works for every credential that `CanRenew`, client credentials too; a federated machine without a target returns null and never calls the SDK;
- the certificate is the credential's (`StorytellerClientCertificate.Load`; `MachineCertificate.Load` is gone);
- `TokenExpiresAt` is the JWT `exp`. **Deviation:** "Signed in; the expiry of the token is unknown." is printed only for a client-credentials or federated token, the ones `sform` asked for; an opaque token file keeps "The API checks the credentials on the first call.";
- the identity of a GitHub-sourced machine names `Names` and the two GitHub variables, as before.

**Errors.** `CredentialFailures` wraps every call into the SDK (`Read`, `RequireEndpoint`, `CreateCredential`, the bound copy, `GetAuthorizationAsync`): `Configuration` → exit `2`, `Rejected` → `Denied` (`44`), `Unavailable` → `ServiceError` (`3`); an `ArgumentException` → `Configuration`. **Deviation:** the mapped exception carries the SDK exception's inner exception, not the SDK exception itself. A test per entry point.

**Review fix: a deadline.** The SDK honours any `Retry-After`, and the token factory and the renewal handler pass no cancellation, so a token request could wait without a limit. `MachineAuthenticationService` now ends it after 100 seconds, retries included, with `ServiceError` (exit `3`) "The machine got no token within 100 seconds, retries and Retry-After waits included: …" (the wording after the branch review), as one request ended at the default timeout of `HttpClient` before. A cancelled caller still gets `OperationCanceledException`. The tests set a short deadline through `MachineCredentialHooks.TokenDeadline`.

**Changes against today**, as listed in the spec, all pinned by tests: `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID` federates; `STORYTELLER_API_KEY=ApiKey` is an error; the stricter URL rule (also no query or fragment on the endpoint); `44` for a `403`, `404`, redirect or 2xx without a bearer token of the token endpoint, and for a 3xx or 4xx of the GitHub runner; direct AuthKit discovery; retries of transient failures; the renewal of client credentials; the renewal margin. The configuration messages are the SDK's (they name the variables as set).

**Deprecated names.** `sform account` prints `! X is deprecated; use Y.` per deprecated name in use, after the outcome, on success and on failure.

**CLI types.** Deleted: `StorytellerVariables`, `GitHubActionsVariables`, `FederatedTokenSource`, `MachineCertificate.Load`, and the URL and token-type helpers of the settings. **Deviation:** `TokenExchange` stays with `GrantType` and `JwtTokenType`, which the federation output of `machine create` prints.

**Startup.** Reads `Endpoint`, uses `StorytellerUrlRule.IsLocalHttp`, and no longer registers the named `HttpClient`; `MachineAuthenticationService` lost its `IHttpClientFactory` and resolver parameters. `AccessTokenFactory`, the development certificate header, `MachineTokenRenewalHandler`, `AuthenticationServiceSelector` and the user sign-in did not change. `machine create` prints the same names as before (phase 4b changes them).

**Test seam.** The SDK grants `InternalsVisibleTo` to `42.Platform.Cli.UnitTests`. `MachineCredentialHooks` (`Decorate`, `Resilience`, `TokenDeadline`) goes through an internal constructor; production uses `MachineCredentialHooks.None`. `TestMachines` scripts the token requests with `WithPrimaryHandler` and no retries; `FederatedSignInWiringTests` builds the service through the internal constructor.

**Tests.**
- `MachineSignInSettingsTests` 72 → 37 cases: the reading and kind rules are tested once, in `Sdk.NSwag/test/Credentials`. The cases only the CLI had pinned moved there (8: four mixed-kind rows, a scope row of the client id alone, a client-credentials case without a secret, another kind in a GitHub Actions job).
- `MachineAuthenticationServiceTests` 75 → 87: the error mapping per entry point, a row per changed exit code, discovery over HTTP, retries, a failed early renewal, the renewal of client credentials, the deadline and a cancelled caller. The form tests of the token request, the exchange and the GitHub request are covered by the SDK's `ClientCredentialsTests`, `FederatedTokenTests` and `GitHubActionsTokenTests`.
- `AccountCommandTests` 19 → 24, `MachineTokenRenewalHandlerTests` 10 → 11 (client credentials are renewed; a token file is not), `FederatedSignInWiringTests` 3 → 4 (the redirect test of the named client went to `FederatedTokenTests`; client-credentials renewal and DI resolution added).
- **Review fixes:** the binding test is named for what it covers (the guard around the bound copy; the SDK's `Bind` throws only for a null binding), and the "no secret" test also checks the credential's text, since the settings have no `ToString` of their own.
