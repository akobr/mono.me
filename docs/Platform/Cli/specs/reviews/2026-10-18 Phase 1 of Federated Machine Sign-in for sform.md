# Phase 1 of Federated Machine Sign-in for sform

Linear: [424-62](https://linear.app/424fun/issue/424-62/sign-in-to-sform-as-a-federated-machine).

## Overview

Phase 1 covers the federated sign-in itself: sections 1–5 of [Federated Machine Sign-in for sform](../2026-10-18%20Federated%20Machine%20Sign-in%20for%20sform.md), plus the `DescribeKind` case of section 7.

`sform` recognizes a `Federated` machine from `STORYTELLER_CLIENT_ID` and either `STORYTELLER_SUBJECT_TOKEN_FILE` or the GitHub Actions ID token variables. It gets a platform token before every exchange, exchanges it at the token endpoint of the project, and keeps the Storyteller token in memory until 60 seconds before it expires.

Commits:
- `80a06a33`: the spec.
- `efa32caa` (feature) and `2e4e10a9` (tests). The feature commit passes the 467 tests that were there before; with the tests there are 540.

Deviations from the plan:
- The two shortened GitHub messages of section 5 are spelled out (section 5 below).
- `DescribeKind` falls back to the enum name instead of "a token file".
- `BindTarget` stores the target for every kind; only `Federated` reads it.

## What Was Done

### 1. Variables

As planned.
- `StorytellerVariables` has `SUBJECT_TOKEN_FILE` and `SUBJECT_TOKEN_TYPE`, between the client-credentials names and `TOKEN_FILE`.
- `GitHubActionsVariables` (new) holds `ID_TOKEN_REQUEST_URL` and `ID_TOKEN_REQUEST_TOKEN`.
- `TokenExchange` (new) holds the grant type and the three token-type URNs. `FederationConsoleExtensions.TokenExchangeGrantType` and `JwtTokenType` are now aliases of these constants, so the printed curl did not change.

### 2. Choosing the kind (`MachineSignInSettings`)

As planned.
- **New properties.** `SubjectTokenFile`, `SubjectTokenType`, `GitHubTokenRequestUrl` and `GitHubTokenRequestToken` are read and trimmed like the others. `HasGitHubTokenSource` is true when both GitHub variables are set.
- **Configured.** Only the two `STORYTELLER_SUBJECT_*` names join `CredentialVariables`. A job with `id-token: write` and no `STORYTELLER_*` credential stays a user sign-in.
- **Families.** `GetKind` counts five families: key or certificate, client credentials (`CLIENT_SECRET`, `TOKEN_URL`), federation, token file, and the shared `CLIENT_ID`/`TOKEN_SCOPE` when neither client credentials nor federation is set. More than one family gives the existing mixed-kinds message.
- **GUID rule.** The ambient GitHub source makes a machine federated only when `CLIENT_ID` is a GUID (`Guid.TryParse`). A `client_…` or `42.sform.…` id without a secret gets the new message:
  `STORYTELLER_CLIENT_ID needs STORYTELLER_CLIENT_SECRET for client credentials, or a platform token for a federated machine: STORYTELLER_SUBJECT_TOKEN_FILE, or a GitHub Actions job with permissions: id-token: write.`
  It replaces the expected message of the existing test case for `CLIENT_ID` alone, which `ClientIdWithoutSourceMessage` now holds.
- **The federation rules** of the spec (machine id required, file before GitHub, https-or-local `ACTIONS_ID_TOKEN_REQUEST_URL`, known token type) use the spec's messages.
- **`NormalizeSubjectTokenType`.** It accepts `jwt`, `id_token`, `access_token` and the three URNs, in any case, and always returns the canonical URN. The server compares the value exactly, so `sform` sends the canonical form whatever was typed.
- **`GetFederatedTokenSource`** returns `File` or `GitHubActions`; `FederatedTokenSource` is a new enum.

### 3. `IMachineSignIn`

As planned: `BindTarget(organization, project)` and `RenewAccessTokenAsync(rejectedToken, cancellationToken)`. No test fake of the interface existed, so nothing else had to change.

**Change from the plan:** `BindTarget` stores the target for every kind instead of ignoring it. Only the federated code paths read it.

### 4. Identity

As planned.
- `Load` builds a federated identity with the client id and no project.
- For the GitHub source, `Variables` adds `ACTIONS_ID_TOKEN_REQUEST_URL` and `ACTIONS_ID_TOKEN_REQUEST_TOKEN`, names only.
- `SignInAsync` returns the identity with the organization, project and expiry of the exchange.

### 5. The exchange (`MachineAuthenticationService`)

As planned, with the code reorganized around the existing client-credentials request.
- **Cache.** `GetClientTokenAsync` became `GetTokenAsync(target, rejectedToken, …)`. It is still one token behind one `SemaphoreSlim`. `ClientToken` carries the target key (`org.project`), and a token for another target is exchanged again. Its `ToString` prints the expiry and the target, never the token.
- **Renewal.** A rejected token that is still the cached one is dropped before the new exchange, under the same lock. If that exchange then fails, no token is cached: this is the one case where the field changes without a successful request, and it is what section 5 asks for.
- **Shared request code.** `PostTokenRequestAsync` holds the POST and the failure mapping for both kinds. `ReadTokenFile` and the new `ReadSubjectTokenFile` share `ReadFile`, and the token-file messages are unchanged.
- **Targets.** `GetAccessTokenAsync` and `RenewAccessTokenAsync` use only the bound target and return `null` without one. `SignInAsync` falls back to `STORYTELLER_PROJECT`, then throws the `Configuration` error of the spec.
- **GitHub request.**
  - The `GET` uses `?` or `&` depending on whether the runner URL already has a query.
  - `Authorization` and `Accept: application/json` are set on that request message only.
  - The request goes out on the named client, which follows no redirects.
- **GitHub messages.** The spec shortened two of them with `…`; they read `The GitHub Actions ID token request failed with {status}.` and `The GitHub Actions ID token request returned no token.` The first message, `The GitHub Actions ID token could not be requested.`, is as written.
- **Audience hint.** A `Denied` exchange from the GitHub source ends with the hint of the spec, naming the audience the token was requested for and `STORYTELLER_CLIENT_SECRET`. `DescribeTokenError` is unchanged, as the spec decided.
- **`DescribeKind`** has explicit `TokenFile` and `Federated` (`workload identity federation`) cases. **Change from the plan:** the fallback `_` returns `kind.ToString()`, so a future kind can never be called "a token file" again.

### Tests (Phase 1 part)

- `MachineSignInSettingsTests`: 12 new tests or theories for the families, the GUID rule, precedence, the GitHub URL rule, the token types and the variables read. `ToString_PrintsNoSecret` also sets `ACTIONS_ID_TOKEN_REQUEST_TOKEN`.
- `MachineAuthenticationServiceTests`: 25 new tests:
  - the endpoint and the form;
  - the file read again for each exchange;
  - reuse until `14:00` of a 900-second token, and a 40-minute job that keeps getting tokens;
  - the target rules;
  - the GitHub request: URL, audience, headers, and that the request token reaches the runner only;
  - every failure mapping;
  - renewal: of the cached token, of an older token, a burst of `401`s;
  - concurrent calls that exchange once.
- `AccountCommandTests`: `Machine_Federated_WithAFile_ExchangesAndShowsTheMachineAndTheProject`, for the new kind text.
