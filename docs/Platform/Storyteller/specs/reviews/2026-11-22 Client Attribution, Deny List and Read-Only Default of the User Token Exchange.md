# Client Attribution, Deny List and Read-Only Default of the User Token Exchange

Linear: [424-302](https://linear.app/424fun/issue/424-302/arch-production-turns-on-the-mcp-token-exchange-with-no-client-allow).

## Overview

This review covers all six phases of [the spec](../2026-11-22%20Client%20Attribution,%20Deny%20List%20and%20Read-Only%20Default%20of%20the%20User%20Token%20Exchange.md).

- **Attribution.** Every Storyteller user token now names the client the subject token was issued to (`subject_client_id`), and the API carries that client into its logs, every audit stamp and the access audit trail (`AuditActor.ViaClient`).
- **Client policy.** The exchange gets an exchange-wide deny list (`Auth:TokenExchange:DeniedClientIds`), and each actor gets a trust list (`TrustedClientIds`) and the restrictions of every client outside it (`UntrustedClientRestrictions`). All three also apply to live tokens.
- **`ReadOnly`.** A new restriction, which a middleware enforces for every change. The MCP server hides and refuses its write tools under it.
- **Production.** Every MCP client app reads only and reveals no secret until it is trusted (`ReadOnly`, `NoSecretReveal`). `mcpAllowedClientIds` becomes the switch for self-registered (DCR) clients. The accepted risk is written into `decisions.md`.

Before the plan, five read-only agents mapped the code, the configuration, the docs, and the facts about AuthKit, CIMD and RFC 8693. Two critics then reviewed the plan before it was committed (spec commit `b6dea38eb`). They caused these changes to the plan:

- the deny list became exchange-wide;
- the trust list compares exactly;
- client ids must be printable ASCII;
- `NoSecretReveal` joined the production default;
- `mcpAllowedClientIds` became a template parameter;
- refused writes are logged;
- the MCP server's eviction skips step-up 401s.

## What Was Done

### 1. Attribution (Phase 1, `dc37979a6`)

- **1.1 The client id check.** `UserSubjectTokenRules.MaxClientIdLength` (256) and `IsRecordableClientId`. A valid id has 1 to 256 characters, each from `'!'` to `'~'`. `UserTokenExchangeService` checks it right after the subject token is accepted, before the expiry check. A bad id gets `invalid_grant` with `SubjectNotAcceptedDescription`; the log does not print the value.
- **1.2 The claim.** `StorytellerTokenClaims.SubjectClientId` (`subject_client_id`) and `StorytellerUserTokenRequest.SubjectClientId`. `IssueUserAsync` writes the claim only for a client. `client_id` and `act` are unchanged.
- **1.3 The delegation.** `BearerDelegation(ActorId, Restrictions, ClientId = null)`.
  - `StorytellerTokenValidator.ValidateUser` reads the claim: absent is `null`, and a present value that is not a recordable client id refuses the token.
  - `FunctionContextItemKeys.DelegatedClient`, stored by `StoreIdentity` and read by `TryGetDelegatedClient`.
- **1.4 `AuditActor.ViaClient`.** It is set with `Via` in `GetAuditActor`, and from there flows into the access caller and the key/value caller. No serializer needed a change: they all ignore nulls.
- **1.5 Manifests.** `DependencyManifests.CallerOf` is `{Kind}.{Via}@{base64url(client)}:{Id}:{Channel}` with a client. The keys without one are unchanged, and the tests that pin them still pass.
- **1.6 Logs.**
  - The new helper `Api.Functions/src/Security/DelegationLogScope.cs` builds the scope `{DelegatedActor, DelegatedClient}` (`"(none)"` without a client) as a dictionary, which Application Insights turns into custom dimensions.
  - `BearerAuthenticationMiddleware` opens the scope around `next` and, after it, writes "A request {FunctionName} {Method} of the account {AccountId} via {ActorId} (client {ClientId}) answered {StatusCode}". The status comes from the invocation result. The client is logged in full.
  - `ExceptionHandlingMiddleware` opens the same scope in each of its four catch blocks.
  - The exchange names the client in every line after the subject token is accepted. Two more than the spec listed also do: the expiry refusal and the signing-key error.
- **1.7** The `GetAccessAudit` description names `ViaClient`.
- **1.8 Tests.** The listed ones, plus `IsRecordableClientId` tests and the check that every log line names the client.
  - `CapturingLogger` records scopes.
  - `StorytellerUserTokenAuthenticationTests` gives its requests a function definition, so the invocation result exists.
  - The access audit record tests now use callers with `ViaClient`, so every record they check round-trips the client.

### 2. The client policy (Phase 2, `58c29b1ec`)

- **2.1 `ReadOnly`.** `ActorRestrictions.All` is `[NoSecretReveal, ReadOnly]`.
- **2.2 The lists.**
  - `TokenExchangeOptions.DeniedClientIds` and `IsDeniedClient`: trimmed entries, compared ignoring case.
  - `TokenExchangeActorOptions.TrustedClientIds` and `IsTrustedClient`: trimmed entries, compared exactly.
  - `UntrustedClientRestrictions`, and `GetRestrictions(clientId)`.
  - The comment of `AllowedClientIds` now says it is checked only at the exchange, and that a token without a client is refused when the list has entries.
- **2.3 The exchange.**
  - A denied client gets `invalid_grant` with `UserTokenExchangeResult.ClientBlockedDescription`. The check sits right after the client id check, before the expiry and the account. The log shortens the id with `ForLog`.
  - The issued restrictions are `actor.GetRestrictions(subject.ClientId)`.
- **2.4 Live tokens.** `ValidateUser` refuses a denied client: 401, with the full id in the log. It enforces the token's restrictions plus `GetRestrictions(clientId)` of now. A token without a client is untrusted.
- **2.5 The middleware.**
  - `Api.Functions/src/Security/ActorRestrictionMiddleware.cs` is registered right after the bearer middleware.
  - `ReadOnlyOperations.cs` lists `FindDeletedAnnotations` and `ExportKeyValues` (by `nameof`).
  - Methods are compared ignoring case. The host matches trigger methods that way, so a lowercase `get` can only reach a `GET` trigger.
  - The refusal's Warning also names the account: "A request {FunctionName} {Method} of the account {AccountId} via {ActorId} (client {ClientId}) is refused: the client app may only read (ReadOnly)". An exception skips the request line, so this is the only trace of whose write it was.
  - `ReadOnlyRefused()` throws an `AccessDeniedException` with `ErrorCodes.ActorReadOnly`. The existing mapping makes it a 403, so `ErrorResponseMapping` needed no change.
  - **OpenAPI.** `Definitions.Descriptions.ForbiddenActorReadOnly` is appended at most once by `ForbiddenResponseDocumentFilter` to every user operation that is not a `GET` and not on the allow-list. An operation without an id counts as a write.
  - `ActorRestrictionMiddlewareTests` walks `FunctionTransactionNames.All`.
- **2.6 Validation.**
  - The blank checks: the deny list's runs exchange-wide even while the exchange is off.
  - The unknown-restriction check of `UntrustedClientRestrictions`.
  - `HasSamePolicy` compares the trimmed sets of `AllowedClientIds`, `TrustedClientIds` and `UntrustedClientRestrictions`, and its message names "client lists".
- **2.7 The notice.** The spec's texts. Its three abbreviated warnings were written out in full:
  - "…has AllowSecrets and no AllowedClientIds: every client may read the person's secrets through it; add NoSecretReveal to UntrustedClientRestrictions.";
  - "…lists {TrustedCount} TrustedClientIds: TrustedClientIds has no effect without UntrustedClientRestrictions.";
  - "The client {ClientId} of Auth:TokenExchange:DeniedClientIds is also in the TrustedClientIds or AllowedClientIds of the actor {ActorId}; it is denied."
  
  The count line reads "Auth:TokenExchange:DeniedClientIds blocks {DeniedCount} clients for every actor of the exchange."

### 3. The SDKs and the admin UI (Phase 3, `a001d069f`)

- **The document.** It is `open.api.v0.10.58.json`, numbered by the branch's git height. `Sdk.NSwag` keeps `v0.10.56`, and `sdk.typescript` replaced it. The diff of the document is `ViaClient`, the `GetAccessAudit` description, the `ActorReadOnly` 403 sentences and the version.
- **The C# SDK.** NSwag 14.6.3 regenerated `ApiSdk.g.cs`. The hand-written twins in `ApiSdk.Annotations.cs` and `ApiSdk.ProjectSettings.cs` took the new 403 sentence, as `Sdk.NSwag/README.md` asks, and their tests compare it.
- **The TypeScript SDK.** Orval regenerated it at version 0.10.58. Orval emits no response descriptions, so only `ViaClient` and the description changed. The lockfiles were left as earlier regenerations left them.
- **The admin UI.** `AccessAuditTrail.vue` shows ` via mcp` and then, in a span of its own, ` (client <id>)` that may wrap. The mock data has no `ViaClient`, so the visual baselines are unchanged. The test is in `access-audit-page.test.ts`.

### 4. The MCP server (Phase 4, `c0bcec36a`)

- **The token.** `StorytellerToken.ReadOnly` and `IsReadOnly`.
- **The tool set.** `ReadOnlyToolSet` holds a hint of `true` or a secret read (`SecretToolNames`). For a call it uses the matched primitive (`McpServerTool.ProtocolTool`), and a call that matched none is refused.
- **`tools/list`.** `ToolListFilter` replaces `SecretToolsFilter` and is always registered. It runs one exchange per list.
  - The secret tools are hidden fail closed, as before.
  - The write tools are hidden fail open. A failed exchange, no token, or a wait over `TokenWait` (5 seconds) lists them; the wait is a linked `CancelAfter`.
  - The list is always private.
- **`tools/call`.** `StorytellerCallFilter` refuses outside the set under `ReadOnly`, with `ApiErrors.ActorReadOnlyCode` and `ActorReadOnlyMessage`. `ApiErrors.ToResult` maps the API's 403 `ActorReadOnly` to the same text.
- **A blocked client.** `StorytellerSignInRefusedException.ClientBlockedMessage` equals the API's description. The provider logs "…; the API blocks the client app of this sign-in." and not the `SubjectAudiences` hint.
- **Eviction.** `IStorytellerTokenProvider.Forget(userToken, accessToken)` removes the cached entry only while it holds that token. The call filter calls it on a 401 from a tool's API call that is not a step-up (`Sdk.StepUp.IsStepUpRequired`).
- **The instructions.** `McpInstructions` has the read-only sentence.
- **Tests.**
  - `ReadOnlyClientTests` (16 tests): listing, fail open on a failed and on a slow exchange, refusal, reads, the secret read, `suggest_configuration_placement`, and the plain 401, step-up and 403 cases of eviction.
  - Two `SecretToolsRegistrationTests` are updated: the list now runs one exchange and is always private.
  - `ApiErrorsTests` and `TokenExchangeProviderTests` (the blocked client, `Forget`).
  - `StubTokenProvider` has `Pending` and `Forgotten`.

### 5. Deployment (Phase 5, `d67f52223`)

- **`main.bicep`.** It gets `mcpAllowedClientIds`, `mcpTrustedClientIds` and `mcpDeniedClientIds` (empty by default), and `mcpUntrustedClientRestrictions` (`@allowed(['ReadOnly', 'NoSecretReveal'])`, both by default). With `mcpEnabled`, `fail()` stops a blank id.
- **`modules/functions.bicep`.** It writes `Auth__TokenExchange__Actors__0__AllowedClientIds__<n>`, `…TrustedClientIds__<n>`, `…UntrustedClientRestrictions__<n>` and `Auth__TokenExchange__DeniedClientIds__<n>`, only with the exchange.
- **`prod.bicepparam`.** It sets the literals and a comment on how to fill the trusted list.
- **The runbook (`runbook-mcp-server.md`).**
  - A phase 0 prerequisite: the API build knows `ReadOnly`.
  - Phase 1 checks at staging: CIMD on, its metadata, recording the ids of a DCR and of a CIMD sign-in, the refused foreign `redirect_uri`, and the precondition (no Connect token is an API bearer token).
  - Phase 4: the what-if lists the new settings, and the expected start log is the Information notice.
  - Verify rows.
  - The new sections Trust a client, Cut off one client and Cut off every unlisted client.
  - Rolling back: turn the exchange off before going back past this change.
- **`decisions.md`.** The `openid` bullet now names the exposure and the custom-domain precondition. A new bullet records the client policy and the accepted risk, and the client lists are in "Can change any time".
- **Also updated.** The cookbook's step 17a, the Deployment README, and the Deployment row of `CLAUDE.md`.
- **Deviations.**
  - The trust rule is stricter than the spec's: a CIMD client is trusted only if its document lists web redirects and no loopback one. VS Code's lists both.
  - Live tokens of a client taken out of `mcpAllowedClientIds` run out within 10 minutes, the default `TokenLifetimeMinutes`, not 15.
  - The ids to copy come from the request line or `ViaClient`. The exchange's log line cuts ids at 64 characters.

### 6. Documentation (Phase 6, `a42a3ae91`)

The Phase 6 commit updates:

- `mcp.md`: client apps, read-only mode, a blocked client app, and "at once" corrected;
- `authentication.md`: the claims, client apps of the exchange, `ReadOnly`, the revocation table and the start notice;
- `access.md`, `audit-stamps.md` and `access-audit.md`. The stale Clients section of `audit-stamps.md` is fixed, and `ViaClient` is in the SDKs since 0.10.58.
- `development.md`: the notice texts;
- `42for.net/platform/admin-ui.md`.

`key-values.md` and `inheritance.md` each got one clause: binding reads, and their manifests, are now counted per client.

### 7. Review of the branch (`6118ac6f2`)

Three reviewers read the diff, one each for the API's policy, the API's HTTP layer, and the MCP server with the deployment, and a verifier would have checked each important finding. They reported no important finding and two minor ones, both fixed:

- **A notice that narrowed nothing.** `TokenExchangeActorNotice` took any non-empty `UntrustedClientRestrictions` as a narrowing. `['NoSecretReveal']` without `AllowSecrets` only repeats the actor's own restriction, so every client kept full write rights while the warning stayed silent. Both warnings now ask whether `GetRestrictions(null)` adds to `GetRestrictions()`. The trust warning ends "…without UntrustedClientRestrictions that add to the actor's own restrictions." A test covers the case, and `authentication.md`'s table follows.
- **A switch that did nothing while the trust list is empty.** The README and the `@description` of `mcpAllowedClientIds` said that setting it to the trusted list refuses every DCR client. With no trusted client the list is empty, which writes no setting and accepts every client. Both now say so, and point to `mcpEnabled = false`, as the runbook already did.

### 8. Rebase and the CodeRabbit review (2026-11-23)

The branch was rebased onto `main`, which in the meantime turned the MCP server on in production (`mcpEnabled = true`, `mcpMinReplicas = 1`). So the read-only default now applies to the live server the first time this branch is deployed: every MCP client reads only until it is trusted. The conflicts in `main.bicep`, `prod.bicepparam` and the Deployment README kept both sides. The OpenAPI document was exported again: still `0.10.58`, only its build number changed, and both SDKs were regenerated.

CodeRabbit's comments, and what was done with them:

- **A sign-in without a client passed the deny list.** It did: `IsDeniedClient(null)` was false. Now a token that names no client, and a Storyteller token issued before `subject_client_id`, counts as denied while `DeniedClientIds` has an entry. This applies at the exchange and on live tokens, and three tests are flipped.
- **The race in `Forget`.** A `Lock` now guards the compare-and-remove of `Forget` and the publication of an answer, so a stale `Forget` cannot remove the fresh token another call cached.
- **The runbook:**
  - the bearer-token check of phase 1 runs against a Release build, because a Debug build (`DEV_AUTH`) decodes tokens without the audience check;
  - a new verify row writes to the API directly with a read-only token and expects `403 ActorReadOnly`;
  - the cut-off times add the 30-second clock skew, here and in the README.
- **`development.md`.** The `AllowSecrets` example says that `NoSecretReveal` in `UntrustedClientRestrictions` keeps the secret tools hidden.
- **`prod.bicepparam`.** The comment says that cutting off unlisted clients needs a trusted client.
- **The OpenAPI 400 of `POST v1/access/token`.** It names the blocked-client refusal.
- **Not done: a `PUT` operation of `SetAnnotation` in the OpenAPI document.** The generator emits one operation per function, and that gap predates this change. The middleware refuses `PUT` like `POST`, and the catalog test checks every method of every function.

## Verification

| Project | Result |
| --- | --- |
| `Backend.Core/test` | 1693 passed |
| `Access.Federation/test` | 529 passed |
| `Access.AuthKit/test`, `Access.AzureAd/test`, `Access.Keycloak/test` | 242, 141 and 138 passed |
| `Api.Functions/test` | 3441 passed |
| `Backend.CosmosDb/test` (the touched classes, Testcontainers vNext) | 12 and 43 passed |
| `Sdk.NSwag/test` | 595 passed |
| `Cli/test` | 2578 passed |
| `Mcp/test` | 795 passed (`Mcp/testint` builds) |
| `Libraries/Platform/Storyteller` (Aspire.Hosting, Configuration, Configuration.KeyVault, Hosting) | 164, 663 (2 skipped), 87 and 565 passed |
| `sdk.typescript` | 505 passed (1 skipped), typecheck, lint, build |
| `ui.admin` | 2450 passed, lint, `vue-tsc` and `vite build` |
| Bicep | `az bicep build` of `main.bicep` and `domains.bicep`, and `build-params` of both parameter files: 0 errors, 0 warnings |

## Left for later

- **The precondition.** Once the custom AuthKit domain is on, a Connect token of a self-registered client could be an API bearer token, with full rights and no attribution. That needs a client allow-list for AuthKit bearer tokens, like `Auth:Keycloak:AllowedClientIds`. The runbook checks for it.
- **A live deny list.** Every change of the lists needs the API to restart with the new settings.
- **Sampling.** The worker does not configure Application Insights sampling. If adaptive sampling is on, which is the SDK default, request lines can be sampled out, so "every request is in the logs" holds only while sampling keeps them. The audit records are not sampled.
- **Rebasing.** After a rebase onto `main`, or any change of the API's git height before the merge, the OpenAPI document has to be exported again and both SDKs regenerated.
- **Other views.** `ViaClient` is not shown in the admin UI's other audit views or in `sform`.
- **A negative cache.** The MCP server does not cache a blocked client's refusal.
