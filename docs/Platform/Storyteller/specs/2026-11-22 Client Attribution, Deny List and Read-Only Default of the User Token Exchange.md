# Client Attribution, Deny List and Read-Only Default of the User Token Exchange

Linear: [424-302](https://linear.app/424fun/issue/424-302/arch-production-turns-on-the-mcp-token-exchange-with-no-client-allow). Source: the review of [PR #210](https://github.com/akobr/mono.me/pull/210) ([424-287](https://linear.app/424fun/issue/424-287/deploy-the-mcp-server-to-production-on-azure-container-apps)). Related: [424-263](https://linear.app/424fun/issue/424-263/record-an-append-only-audit-trail-for-role-membership-and-machine) (the access audit trail).

Line numbers refer to the tree this plan was written against (`20ef5b5a2`). No application code is changed by this document.

## Problem

With `mcpEnabled`, production turns on the user token exchange for the actor `mcp`:

- `SubjectAudiences__0` is the MCP resource URL.
- `AllowedClientIds` is empty.
- The MCP server asks for the scope `openid`, which gates nothing.

So an arbitrary client needs only two things to act as a person with that person's Storyteller rights: a subject token whose `aud` is `https://mcp.2s.42for.net/mcp`, and a Storyteller account for the person. Any client can register itself at the AuthKit domain through dynamic client registration (DCR), ask for that resource, and after one consent screen drive every MCP tool with `User.Impersonation`. It can create, update and delete annotations, configurations, schemas, templates, key/value pairs, members and machines. `NoSecretReveal` and the 10-minute step-up cover only secret values, role changes and machine credentials.

DCR is how generic MCP clients work, so accepting them is a reasonable choice. But this is the platform's first production path where the client that acts is unknown and unbounded. It is also not recorded:

1. **Nothing attributes an action to a client.** The exchange logs the subject token's client once, when it issues the token. The issued Storyteller token, the API's request logs, the audit stamps and the access audit trail name only the person and the actor (`Ada via mcp`).
2. **One client cannot be cut off.** The only switches are for everyone: `mcpEnabled = false`, the actor removed, or the Container App's ingress turned off.
3. **No default is narrower than full rights.** A phishing client, a malicious client, or a legitimate one with a prompt-injection problem gets write and delete on everything the person can touch.
4. **The accepted risk is not written down.** [decisions.md](../../Deployment/decisions.md#the-mcp-server) argues why `openid` is needed, not what it exposes.

## Current State

### The exchange and its subject token

- `Access.Federation/src/UserTokenExchangeService.cs:61-218` runs the exchange in the order of its spec:
  1. the form;
  2. the actor and its proof;
  3. the subject token (`ValidateSubjectTokenAsync`, 293-319);
  4. the account;
  5. the replay limit;
  6. the scope;
  7. the token.
- The subject token's client is known after step 3 (`UserSubjectTokenResult.ClientId`, `Backend.Core/src/Accessing/UserSubjectTokenResult.cs:20-21`), but it is used only in the success log (210-215). It is never put into the issued token.
- Each provider reads the client with `UserSubjectTokenRules.GetClientId` (`Backend.Core/src/Accessing/UserSubjectTokenRules.cs:45-49`: `azp`, then `appid`, then `client_id`). Each provider then checks it against the actor's `AllowedClientIds` with `IsAllowedClient` (51-57). The comparison is ordinal for AuthKit (`Access.AuthKit/src/AuthKitUserSubjectTokenValidator.cs:101-107`) and Keycloak, and ignores case for Entra ID (`Access.AzureAd/src/EntraIdUserSubjectTokenValidator.cs:136`). An empty list accepts any client.
- The value is not checked: AuthKit DCR ids look like `client_01…`, Client ID Metadata Document (CIMD) clients have HTTPS URLs, Entra ID gives GUIDs, and Keycloak client ids are free-form names. Only the subject token's size bounds the value (`FederationOptions.MaxSubjectTokenBytes`).

### The Storyteller user token

- `Access.Federation/src/StorytellerTokenIssuer.cs:76-125` (`IssueUserAsync`) writes these claims:
  - `sub` is the account;
  - `token_use` is `user`;
  - `scope`;
  - `act` is `{ "sub": <actor id> }`;
  - `client_id` is the actor id;
  - `restrictions`;
  - `jti`;
  - and, when known, `auth_time`.
- `client_id` follows RFC 9068 and RFC 8693 section 4.3: it names the party that requested the token, the MCP server's actor. Nothing in the token names the MCP client.
- `Access.Federation/src/StorytellerTokenValidator.cs:280-351` (`ValidateUser`) re-reads the actor's settings on every request:
  - it refuses the token when the exchange is off or the actor is gone;
  - the enforced restrictions are the token's restrictions plus the actor's current ones;
  - the scopes are narrowed to the actor's current ones.
  It returns `BearerDelegation(ActorId, Restrictions)` (`Backend.Core/src/Accessing/BearerDelegation.cs:6`). An unknown restriction in a token refuses it (`TryGetRestrictions`, 182-208).
- The settings are `IOptions<TokenExchangeOptions>`, bound once at start (`UserAuthenticationEntryPoint.cs:19-22`). "Current" therefore means the settings the API started with. On Azure, changing an app setting restarts the Function App.

### Delegation in the API

- `Api.Functions/src/Security/BearerAuthenticationMiddleware.cs:274-283` (`StoreIdentity`) stores `Items[DelegatedActor]` and `Items[DelegatedRestrictions]` (`FunctionContextItemKeys.cs:23-24`). These items are the only carrier of the delegation.
- `Api.Functions/src/Security/HttpRequestDataExtensions.cs:119-139` (`GetAuditActor`) returns `AuditActor.Account(sub, name) with { Via = actorId }`. This is the only place `Via` enters audit stamps, annotation audit entries, key/value audit events and access audit records. `GetAccessCaller` (143-146) wraps it with the invocation id.
- `AuditActor` (`Abstractions.Annotations/src/Model/AuditActor.cs:17-34`) has `Kind`, `Id`, `Name` and `Via`. Every serializer it passes through ignores nulls: Cosmos System.Text.Json and Newtonsoft (`Backend.CosmosDb` `EntryPoint.cs:62,78`), the Cosmos SDK serializer (`CosmosClientProvider.cs:47`) and the API's responses (`Program.cs:82`). The view export manifest is the exception: it writes nulls (`ViewDocumentJson.cs:25-31`).
- `Api.Functions/src/ConditionalReads/DependencyManifests.cs:41-49` (`CallerOf`) keys the manifests of resolved reads, and with them the `BindingRead` audit, by `{Kind}[.{Via}]:{Id}:{Channel}`.
- The API logs requests without the actor or the client. No middleware opens a logging scope (no `BeginScope` in `Api.Functions/src`). The worker's Application Insights logger includes scopes as custom dimensions.

### Actor restrictions

- `Backend.Core/src/Accessing/ActorRestrictions.cs:3-17` is a closed list with one member, `NoSecretReveal`. Its comment says a new restriction needs a spec.
- `NoSecretReveal` is enforced in seven handler-level places, all after the role check: `HttpRequestDataExtensions.cs:199` (`GetSecretRightsAsync`), `KeyValueRequests.cs:126`, `AccessHttp.cs:709` and `:720`, `FederationHttp.cs:180`, and `KeyValuesHttp.cs:123` and `:428`. They all raise `KeyValueErrors.ActorRestricted()` (403, `ErrorCodes.ActorRestricted`, `Backend.Core/src/ErrorCodes.cs:18-20`).
- No chokepoint guards every write:
  - The handlers call their own role guards (`HttpRequestDataExtensions.cs:298-600`).
  - The service layer checks roles of its own (`CosmosAccessService.EnsureCanManage`, `CosmosInvitationService.EnsureAdministratorAsync`).
  - Several writes need no role at all: `PostAccount`, `PostAccessPoints` for a new organization, `JoinExhibition`, and a member leaving their own membership.
  - Debug builds (`DEV_AUTH`) skip the user role checks.
- The worker middleware runs in this order (`Api.Functions/src/Program.cs:44-51`): Sentry scope, exception handling, transport security, machine authentication, bearer authentication. 150 HTTP functions exist, 69 of them not `GET`. Two `POST` routes are reads: `FindDeletedAnnotations` (`annotations/deleted/lookup`) and `ExportKeyValues` (`key-values/export`). `FunctionTransactionNames.All` (`ErrorReporting/FunctionTransactionNames.cs:16-104`) maps every function name to its methods and route.

### Options validation and the start notice

- `Backend.Core/src/Accessing/TokenExchangeOptionsValidator.cs` collects every failure. For each actor it checks:
  - `AllowedClientIds` entries are not blank (95-98);
  - `Restrictions` are known (269-272);
  - `AllowSecrets` and an explicit `NoSecretReveal` are not set together (274-277).
- Actors that share a subject audience must have the same policy (`ValidateSharedSubjectAudiences`, 211-238). `HasSamePolicy` (242-246) compares only restrictions and scopes.
- `Access.Federation/src/TokenExchangeActorNotice.cs:24-37` warns at start about every actor of an enabled exchange that has no `AllowedClientIds`. Its tests and four docs quote the text.

### The MCP server

- 68 tools carry explicit hints (`CatalogTests` pins that every tool has one): 36 have `ReadOnly = true` and 32 have `ReadOnly = false`. The three secret reads (`SecretToolNames`, `Mcp/src/Tools/SecretToolNames.cs`) are `ReadOnly = false` on purpose. The read-only tool `suggest_configuration_placement` can `POST` `annotations/deleted/lookup` (`UnderstandingTools.cs:320`).
- `SecretToolsFilter` (`Mcp/src/Tools/SecretToolsFilter.cs`) hides the secret tools in `tools/list` under `NoSecretReveal`. It is registered only with `Mcp:SecretTools:Enabled` (`Hosting/McpHostExtensions.cs:50-56`), which production sets to `false`. `StorytellerCallFilter` (`Mcp/src/Tools/StorytellerCallFilter.cs:33-97`) is always registered and refuses a secret tool under `NoSecretReveal` before the tool runs.
- `StorytellerTokenExchangeProvider` parses `restrictions` as plain strings and keeps unknown ones. It caches a token per subject token until about a minute before it expires and has no eviction (`:103-220`). An `invalid_grant` description reaches the person verbatim, and every `invalid_grant` other than the no-account one logs a hint about `SubjectAudiences` (`:283-299`).
- `ApiErrors.ToResult` (`Mcp/src/Tools/ApiErrors.cs:86-89`) replaces the message of every `ActorRestricted` answer with a fixed text about secrets.

### Production

- `Deployment/modules/functions.bicep:185-207` writes the actor: its id, its workload identity, and `SubjectAudiences__0`. The comment at 185-186 says "any MCP client (dynamic registration)". `Deployment/modules/mcp.bicep:49,115` sets the scope `openid`.
- `Deployment/parameters/prod.bicepparam:78` still has `mcpEnabled = false`, so the exchange is not on in production yet. Phase 4 of `runbook-mcp-server.md` turns it on.
- **The precondition of this plan.** A client registered at the AuthKit domain reaches the API only through the actor because its tokens are not API bearer tokens:
  - Today the issuers differ: the API's bearer tokens come from the WorkOS API issuer, and the MCP sign-in tokens from the AuthKit domain.
  - With the custom AuthKit domain (`authKitCustomDomain`, `functions.bicep:121,179-180`), both issuers are the same. Then only `aud` keeps a Connect token out of the API's bearer check: `AuthKitBearerTokenValidator.cs:12-15,55-57` checks `iss` and `aud`, not the client.
  - If WorkOS gives a dynamically registered client a token with an `aud` the API accepts (no resource, or `resource` set to the API's audience), that client calls the API directly. It then has full rights, with no `Via`, no `ViaClient`, no `ReadOnly` and no deny check. `IsIssuedToAnActor` blocks only `aud` values of an actor.
  - Closing that path (a client allow-list for AuthKit bearer tokens, like `Auth:Keycloak:AllowedClientIds`) is out of scope here. The runbook gets a check for it (5.4), and the gap is left for later.

### What AuthKit and the MCP clients do (web research, 2026-11-22)

- **DCR.** A dynamically registered client becomes a WorkOS Connect application with a `client_id` of the form `client_01<ULID>`. WorkOS documents the `client_id` claim of Connect access tokens; it documents no `azp`. Claude registers a new client on every fresh connection, and ChatGPT once per server connection. So DCR ids are random. Denying one blocks a single registration, and the client can register again.
- **CIMD.** WorkOS supports CIMD since 2025-11-30. It is off by default and is turned on in the Dashboard (Connect, Configuration). Several clients have stable metadata URLs:
  - Claude: `https://claude.ai/oauth/mcp-oauth-client-metadata`;
  - Claude Code: `https://claude.ai/oauth/claude-code-client-metadata`;
  - ChatGPT: `https://chatgpt.com/oauth/client.json`;
  - Codex: `https://chatgpt.com/oauth/codex/client.json`;
  - VS Code: `https://vscode.dev/oauth/client-metadata.json`.
  
  Claude uses CIMD only when the server's metadata has `client_id_metadata_document_supported: true` and `none` among the token endpoint auth methods. That the token's `client_id` is the URL for a CIMD client is inferred, not verified for WorkOS. Cursor has no CIMD.
- **The MCP specification.** Version 2026-07-28 deprecates DCR in favor of CIMD. CIMD cannot prevent localhost impersonation: any local process can present a loopback client's URL. The specification leaves access policy per client to the server.
- **Kill switches at WorkOS.** WorkOS documents no allow-list or block-list of clients. Deleting a DCR application does not block the client, and access tokens it already issued stay valid until they expire. DCR and CIMD can each be turned off only for the whole environment.
- **RFC 8693 section 4.1.** Only the top-level claims and the current actor (`act`) may be used for access control. A nested `act` is informational only.

## Proposed Changes

### Phase 1: Attribution (API)

#### 1.1 The client id of a subject token

- `UserSubjectTokenRules` gets `MaxClientIdLength = 256` and `IsRecordableClientId(string? clientId)`. It is true for `null`, and for a value of 1 to 256 characters, each printable ASCII (`'!'` to `'~'`, U+0021 to U+007E).
  - That rules out spaces at either end, controls, format characters (bidirectional overrides, zero-width spaces) and look-alike letters.
  - Every real id is printable ASCII: Entra ID GUIDs, AuthKit `client_…` ids, HTTPS URLs and Keycloak names.
- `UserTokenExchangeService` checks the accepted subject's `ClientId` with it right after the subject token is accepted (step 3). If the check fails, the exchange is refused as a subject that is not accepted (`invalid_grant`, `SubjectNotAcceptedDescription`). The log says "the subject token's client id is not 1 to 256 printable ASCII characters" and does not print the value.
- The bound keeps every audit stamp, log line and manifest key small.

#### 1.2 The claim `subject_client_id`

- `StorytellerTokenClaims.SubjectClientId = "subject_client_id"`: "the client the subject token was issued to (its `azp`, `appid` or `client_id`)".
- `StorytellerUserTokenRequest.SubjectClientId` (`string?`). `UserTokenExchangeService` sets it from `subject.ClientId`.
- `StorytellerTokenIssuer.IssueUserAsync` writes the claim when the value is not null, and leaves it out otherwise. `client_id` and `act` are unchanged.

#### 1.3 The delegation carries the client

- `BearerDelegation(string ActorId, IReadOnlyList<string> Restrictions, string? ClientId = null)`.
- `StorytellerTokenValidator.ValidateUser` reads `subject_client_id`:
  - an absent claim gives `null`;
  - a present claim must be a JSON string that passes `IsRecordableClientId`, or the token is refused ("its subject_client_id is not a client id").
  - The value goes into the delegation, never into the claims.
- `FunctionContextItemKeys.DelegatedClient`. `BearerAuthenticationMiddleware.StoreIdentity` stores it when the delegation names a client, and `HttpRequestDataExtensions.TryGetDelegatedClient` reads it.

#### 1.4 `AuditActor.ViaClient`

- `AuditActor` gets `public string? ViaClient { get; init; }`: "the client app the person used with the actor (the subject token's `azp`, `appid` or `client_id`); set only together with `Via`, null for a direct call and for a token exchanged before this change, and then left out of stored and returned documents".
- `GetAuditActor` sets `account with { Via = actorId, ViaClient = clientId }`. The value then flows, with no further change, into:
  - every audit stamp;
  - the annotation audit;
  - the key/value audit, including its `BindingRead` buckets (`CosmosKeyValueAuditLog.BindingReadKey` holds the whole actor, so the hourly counts split per client);
  - the access audit records;
  - the view exports.
- Older documents read with `ViaClient` null. No migration is needed.

#### 1.5 Manifests tell clients apart

`DependencyManifests.CallerOf` becomes `{Kind}.{Via}@{client}:{Id}:{Channel}` when the actor names a client:

- `{client}` is the base64url encoding (no padding) of its UTF-8 bytes;
- `@` appears neither in actor ids (unreserved characters) nor in base64url, so the parts stay apart;
- without a client the key is unchanged.

So the manifest of one client never answers another client's read, and every client's binding reads are audited. Read rights were already safe: the `r` tag carries the rights code, and a manifest answers only an equal tag (`ConfigurationHttp.cs:842`).

#### 1.6 Logs

- `BearerAuthenticationMiddleware`, for a request with a delegation:
  - opens a logging scope `{ DelegatedActor: <actor id>, DelegatedClient: <client id or "(none)"> }` around `next(context)`, so the handlers' own logs carry both (Application Insights shows scopes as custom dimensions);
  - writes one Information line after `next(context)` returns: "A request {FunctionName} {Method} of the account {AccountId} via {ActorId} (client {ClientId}) answered {StatusCode}". So every action of a client is in the logs, reads too. The MCP server's traffic is small, so the volume is too.
- `ExceptionHandlingMiddleware` sits outside the bearer middleware, so the scope has ended when it logs a refusal or an error. It opens the same scope from `Items[DelegatedActor]` and `Items[DelegatedClient]` inside its catch blocks.
- `UserTokenExchangeService` names the client (shortened by `ForLog`, at most 64 characters) in every log line written after the subject token is accepted: the missing account, the replay limit, the dropped scopes, the missing scope, and the issued token (the success line already does).

#### 1.7 The OpenAPI description

The description of `GetAccessAudit` (`AccessAuditHttp.cs:29`) names `ViaClient` next to `Via`.

#### 1.8 Tests

- Access.Federation:
  - the issued token carries `subject_client_id`, or none without a client;
  - the validator: an absent claim gives no client; a non-string claim, a 257-character claim and one with a space or a non-ASCII letter are refused; the client reaches the delegation;
  - the exchange refuses a client id of 257 characters or with a character outside printable ASCII.
- Api.Functions:
  - a token passes through the middleware to `Items[DelegatedClient]`;
  - the request line and the scope name the actor and the client;
  - `ExceptionHandlingMiddleware` logs a refusal with the delegated actor and client;
  - `GetAuditActor` and `GetAccessCaller` carry the client;
  - `CallerOf` keys a client apart, and the pinned keys without a client are unchanged;
  - `TestRequests.DelegatedUser` takes an optional client;
  - a `ProjectSettings` body with a 256-character `ViaClient` still fits `RequestBodyLimits`.
- Backend.CosmosDb: `AuditActorSerializationTests` round-trip `ViaClient` with both serializers, leave it out when null, and read an older document. An access audit record of a caller through an actor with a client keeps the client.

### Phase 2: The client policy of the exchange (API)

#### 2.1 The restriction `ReadOnly`

- `ActorRestrictions.ReadOnly = "ReadOnly"`: "the token may not change anything. Every request other than `GET`, `HEAD` and `OPTIONS` is refused, except the operations that only read (`ReadOnlyOperations`). Reads keep their side effects: audit records of reveals and binding reads, and usage metering".
- `All` becomes `[NoSecretReveal, ReadOnly]`.
- An actor may name it in `Restrictions` (all its tokens read only) or in `UntrustedClientRestrictions` (2.2).
- `ReadOnly` says nothing about secrets: a reveal and a resolved read are `GET`s. Secrets stay governed by `NoSecretReveal`. So the production default for untrusted clients names both (5.1).

#### 2.2 Client lists

- `TokenExchangeOptions.DeniedClientIds` (string array, empty by default) is exchange-wide: a client blocked there is blocked for every actor.
  - It is the kill switch for clients with stable ids: CIMD URLs, Entra ID apps and Keycloak clients.
  - It is not a kill switch for DCR clients, which register again under a new id; `AllowedClientIds` is (below).
  - Exchange-wide, so an emergency entry never trips the shared-audience check (2.6).
- `TokenExchangeActorOptions` gets two string arrays, both empty by default:

| Option | Meaning |
| --- | --- |
| `TrustedClientIds` | Clients that act with the actor's own policy only. |
| `UntrustedClientRestrictions` | Restrictions added for every client outside `TrustedClientIds`, including a subject token that names no client. Known restrictions only. |

All lists are arrays, never dictionaries keyed by client id: a CIMD id contains `:`, which is the configuration path separator.

The lists combine in this order:

1. `AllowedClientIds` (the actor's, unchanged) stays the hard gate. With entries, every other client is refused, a token without a client too. Listing only the trusted clients there refuses every DCR registration. That is the switch for a hostile DCR client.
2. A client in `DeniedClientIds` is refused, even when the actor allows or trusts it.
3. A client in `TrustedClientIds` gets `GetRestrictions()`.
4. Every other client gets `GetRestrictions()` plus `UntrustedClientRestrictions`.

Trust never removes a restriction of the actor itself: `NoSecretReveal` unless `AllowSecrets`, and the actor's `Restrictions`.

Helpers:

- `TokenExchangeOptions.IsDeniedClient(string? clientId)`: false for `null`. Entries are trimmed and compared ignoring case. A wider match is the safe direction for a deny list.
- `TokenExchangeActorOptions.IsTrustedClient(string? clientId)`: false for `null`. Entries are trimmed and compared ordinally (exact).
  - A trust list must not widen: Keycloak's dynamic registration lets a registrant pick a case variant of a trusted id, and URL paths are case-sensitive.
  - Entra ID tokens carry lowercase GUIDs, so the docs say to write them that way.
- `TokenExchangeActorOptions.GetRestrictions(string? clientId)`: `GetRestrictions()`, plus `UntrustedClientRestrictions` unless the client is trusted; distinct, in this order.

#### 2.3 The exchange applies the policy

In `UserTokenExchangeService`, after the subject token is accepted and the client id checked (step 3), a denied client is refused:

- `invalid_grant` with a new `UserTokenExchangeResult.ClientBlockedDescription`: "The client app the person signed in with is blocked for this deployment. Use another app, sform or the admin UI."
- The log is at Information: "A user token exchange by the actor {ActorId} is refused: the client {ClientId} is in Auth:TokenExchange:DeniedClientIds".

The refusal comes before the account is read, so a blocked client learns nothing about the account. Step 7 issues the token with `actor.GetRestrictions(subject.ClientId)`. The exchange's answer lists them, `ReadOnly` included, so the MCP server knows them (Phase 4).

#### 2.4 Live tokens follow the current policy

`StorytellerTokenValidator.ValidateUser`, after it has found the actor:

- It refuses a token whose `subject_client_id` is now denied. The token gets a 401, like a token of a removed actor, and the log names the client.
- It enforces the token's restrictions plus `actor.GetRestrictions(clientId)` of now. So a client that leaves `TrustedClientIds`, or a new entry of `UntrustedClientRestrictions`, reaches the client's live tokens once the API runs with the new settings.
- A token from before this change names no client. It is treated as untrusted.
- `AllowedClientIds` is checked at the exchange only, as today. A client removed from it keeps its live tokens until they expire (at most `TokenLifetimeMinutes`, 15 minutes). The docs say so, and point to `DeniedClientIds` for an immediate cut.

#### 2.5 `ReadOnly` is enforced by a middleware

- New `Api.Functions/src/Security/ActorRestrictionMiddleware.cs`, registered right after `BearerAuthenticationMiddleware`, inside `ExceptionHandlingMiddleware`.
- For an HTTP invocation whose `Items[DelegatedRestrictions]` contains `ReadOnly`:
  - it lets `GET`, `HEAD` and `OPTIONS` pass;
  - it lets the functions in `ReadOnlyOperations.All` pass, a frozen set of function names: `FindDeletedAnnotations` and `ExportKeyValues`;
  - it refuses everything else. Before it throws, it logs a Warning with the actor, the client, the function name and the method: a refused write leaves no audit record, so this line is its trace. It then throws `AccessDeniedException` with the new `ErrorCodes.ActorReadOnly`: 403, "The token acts for the person through a client app that may only read; the change is refused."
- Every other invocation passes unchanged: machines, direct users, anonymous endpoints, timers.
- The refusal comes before the role checks and the step-up, unlike the `NoSecretReveal` checks:
  - A method rule refuses uniformly and reveals nothing about roles.
  - No new sign-in or role lifts it, so a 401 for a step-up would mislead.
  - It covers the writes that need no role and the service-level role checks, and it also holds in `DEV_AUTH` builds.
- Every HTTP trigger declares its methods, and no middleware overrides them, so the method cannot be spoofed. `FunctionDefinition.Name` comes from the host.
- View exports, dry runs of imports and the BYOK verification are refused: they create a job or record state, and the MCP server uses none of them.
- A test walks `FunctionTransactionNames.All`. Every HTTP function that accepts a method other than `GET`, `HEAD` or `OPTIONS` must be refused under `ReadOnly`, unless it is in `ReadOnlyOperations.All`. That set lists only functions that exist and only read. So a new write route is refused by default.
- The OpenAPI document says so. `ForbiddenResponseDocumentFilter` (or a sibling filter) appends one sentence to the 403 description of every user operation that is not a `GET` and not in `ReadOnlyOperations.All`: "Through an actor, a client app that may only read gets 403 with ErrorCode ActorReadOnly." The pinned description tests of `OpenApiExportTests` are updated.

#### 2.6 Validation

`TokenExchangeOptionsValidator` adds these failures, in the existing style:

- `Auth:TokenExchange:DeniedClientIds entries must not be blank.`
- `{path}:TrustedClientIds entries must not be blank.`
- `{path}:UntrustedClientRestrictions names '{r}', which is not one of NoSecretReveal, ReadOnly.`

Overlaps and lists without effect are not failures, because a failed start takes the whole API down. Those cases are warnings (2.7).

`HasSamePolicy` also compares, as sets of trimmed entries:

- `AllowedClientIds` and `TrustedClientIds` (ordinal);
- `UntrustedClientRestrictions` (ordinal).

Its message reads "…also lists with other Restrictions, AllowSecrets, Scopes or client lists; a token issued for one actor could be exchanged by the other." `DeniedClientIds` is exchange-wide, so it is not part of a policy.

#### 2.7 The start notice

`TokenExchangeActorNotice` logs at start, while the exchange is enabled:

- **Warning**, per actor, when `AllowedClientIds` and `UntrustedClientRestrictions` are both empty. This is today's exposure, and the text is kept with one sentence more: "The actor {ActorId} of Auth:TokenExchange:Actors has no AllowedClientIds: a token any client got for its SubjectAudiences ({SubjectAudiences}) is accepted for the exchange. List the clients to narrow it, or set UntrustedClientRestrictions (for example ReadOnly and NoSecretReveal) for the clients outside TrustedClientIds."
- **Information**, per actor, when `AllowedClientIds` is empty and `UntrustedClientRestrictions` is set: "The actor {ActorId} of Auth:TokenExchange:Actors accepts a token of any client for {SubjectAudiences}; the clients outside its {TrustedCount} TrustedClientIds get {UntrustedClientRestrictions}."
- **Warning**, per actor, when `AllowSecrets` is true, `AllowedClientIds` is empty, and `UntrustedClientRestrictions` lacks `NoSecretReveal`: "…: every client may read the person's secrets through it; add NoSecretReveal to UntrustedClientRestrictions."
- **Warning**, per actor, when `TrustedClientIds` has entries but `UntrustedClientRestrictions` is empty: "…: TrustedClientIds has no effect without UntrustedClientRestrictions."
- **Warning** for each id of `DeniedClientIds` that an actor's `TrustedClientIds` or `AllowedClientIds` also lists: "…; it is denied."
- **Information** with the number of denied clients, when there are any. The ids themselves are in the settings.

#### 2.8 Tests

- Backend.Core:
  - the closed list now has two members;
  - the validation messages, and a shared audience with different client lists;
  - the binding of the three arrays;
  - `IsDeniedClient` (case, trimming, null), `IsTrustedClient` (exact, trimming, null) and `GetRestrictions(clientId)`.
- Access.Federation:
  - a denied client is refused with the new description before the account is read, and is logged;
  - the issued restrictions differ for a trusted and an untrusted client;
  - live tokens: a client denied later is refused, a client no longer trusted gets `ReadOnly`, and a token without a client counts as untrusted;
  - the notice texts.
- Api.Functions:
  - the middleware: a `ReadOnly` delegation is refused for `POST`, `PUT`, `PATCH` and `DELETE` with 403 `ActorReadOnly` and a logged warning, passes `GET` and the allow-listed functions, and leaves other callers alone;
  - the catalog walk of 2.5;
  - a mapping test of the error code;
  - the 403 descriptions of the OpenAPI document.

### Phase 3: The SDKs and the admin UI

1. Export the OpenAPI document with `OpenApiExportTests` (`STORYTELLER_OPENAPI_EXPORT`) after Phases 1 and 2. `AuditActor` gains `ViaClient`, `GetAccessAudit` gets its new description, and the write operations get the `ActorReadOnly` sentence.
   - Name the file after `info.version` without its fourth part. Keep the previous file in `Sdk.NSwag` and replace it in `sdk.typescript`.
   - Regenerate `Sdk.NSwag/src/ApiSdk.g.cs` with NSwag 14.6.3, using the command of `Sdk.NSwag/README.md`, and update the README's `/input:` name.
   - Regenerate the TypeScript SDK with `npm run generate`, and update the example name in `sdk.typescript/README.md`.
2. The admin UI's access audit trail (`AccessAuditTrail.vue:222`) renders ` via mcp (client <id>)` when `ViaClient` is set, and ` via mcp` otherwise. A long id may wrap.
   - A unit test covers both.
   - The mock data stays without `ViaClient`, so the visual baselines stay unchanged.
   - The other audit views do not show `Via` today and are not changed.

### Phase 4: The MCP server

1. `StorytellerToken.ReadOnly = "ReadOnly"`, and `IsReadOnly`.
2. **The read-only tool set.** A tool may run under `ReadOnly` when its `ReadOnlyHint` is `true` or it is a secret read (`SecretToolNames`). The secret reads stay governed by `NoSecretReveal` alone.
3. **`tools/list`.** One list filter, `ToolListFilter`, replaces `SecretToolsFilter` and is always registered. It gets the person's token once per list, through the cached exchange.
   - It sets `CacheScope = Private`.
   - It hides the secret tools under `NoSecretReveal`, and when no token could be had. That is today's behavior, which fails closed.
   - It hides every tool outside the read-only tool set when the token is `ReadOnly`. This part fails open: with no token, a failed exchange, or an exchange that takes longer than 5 seconds, the filter keeps those tools and logs a warning.
     - The wait is cut by a linked cancellation; the shared exchange goes on and is cached for the next call.
     - Hiding is presentation only: the call filter and the API refuse anyway. A cold start of the API must not leave a session with a read-only list.
   - Tests that pin "no exchange on `tools/list`", the list's cache scope and the recorded user tokens are updated (`SecretToolsRegistrationTests`).
4. **`tools/call`.** `StorytellerCallFilter` refuses, before the tool runs, a tool outside the read-only tool set when the token is `ReadOnly`.
   - The tool is identified by the matched primitive's `ReadOnlyHint` (`RequestContext.MatchedPrimitive`) and the secret tool names. It fails closed when no primitive matched.
   - The result is an error with `ErrorCode` `ActorReadOnly` and a fixed text: "The 2S API lets this client app only read for you, so the change was not made. Make it in the admin UI or with sform, or ask the operator of the deployment to trust this app."
5. `ApiErrors.ToResult` maps a 403 `ActorReadOnly` from the API to the same text. `ActorRestricted` keeps its message about secrets.
6. **A blocked client.**
   - `StorytellerSignInRefusedException` gets `ClientBlockedMessage`, equal to the API's `ClientBlockedDescription`.
   - The exchange provider leaves it out of the hint about `SubjectAudiences` and logs "the API blocks the client app of this sign-in" instead.
   - The person sees the description, as today.
7. **Eviction on a plain 401.**
   - `IStorytellerTokenProvider.Forget(string subjectToken, string accessToken)` drops the cached entry only while it still holds that access token, so a fresh token another call already cached stays.
   - `StorytellerCallFilter` calls it when a tool's API call answers 401 and the answer is not a step-up refusal (`!Sdk.StepUp.IsStepUpRequired(exception)`). A step-up 401 cannot be fixed by a new exchange, which copies the same `auth_time`, and each exchange counts against the replay limit.
   - The next call then runs a fresh exchange, which names the reason: a blocked client, or an exchange turned off.
8. `McpInstructions.Text`, under Safety, gets: "When a tool answers that this client app may only read, tell the person to make the change in the admin UI or with sform; do not retry it."
9. Tests:
   - the list filter: `ReadOnly` hides the write tools and keeps the read and secret tools; `NoSecretReveal` hides the secret tools; it fails open for writes on a failed and on a slow exchange; it exchanges once per list; the list is private;
   - the call filter: it refuses a write tool under `ReadOnly`, and passes a read tool and `suggest_configuration_placement`;
   - `ApiErrors` maps `ActorReadOnly`;
   - the provider logs no hint for a blocked client;
   - `Forget` drops only the refused token, the call filter forgets on a plain 401, and it keeps the token on a step-up 401.

### Phase 5: Deployment

1. `main.bicep` gets four parameters, each with a `@description`:
   - `mcpAllowedClientIds` (`string[]`, default `[]`): the hard gate;
   - `mcpTrustedClientIds` (`string[]`, default `[]`);
   - `mcpUntrustedClientRestrictions` (`string[]`, `@allowed(['ReadOnly', 'NoSecretReveal'])`, default `['ReadOnly', 'NoSecretReveal']`);
   - `mcpDeniedClientIds` (`string[]`, default `[]`).
   
   With `mcpEnabled`, `fail()` refuses blank entries in the three id lists, so a mistake fails the deployment and does not stop the API. The parameters go to the `functions` module.
2. `modules/functions.bicep` writes the settings only with `tokenExchangeEnabled`, with the existing `toObject(range(0, length(x)), …)` pattern, and joins them in the `union`:
   - `Auth__TokenExchange__Actors__0__AllowedClientIds__<n>`;
   - `Auth__TokenExchange__Actors__0__TrustedClientIds__<n>`;
   - `Auth__TokenExchange__Actors__0__UntrustedClientRestrictions__<n>`;
   - `Auth__TokenExchange__DeniedClientIds__<n>`.
   
   The comment at 185-186 describes the client policy.
3. `parameters/prod.bicepparam` sets literals: `mcpAllowedClientIds = []`, `mcpTrustedClientIds = []`, `mcpUntrustedClientRestrictions = ['ReadOnly', 'NoSecretReveal']` and `mcpDeniedClientIds = []`. A comment says how to fill the trusted list (5.4).
4. Docs:
   - `src/Platform/Deployment/README.md`: the parameter rows, and the two cut-off bullets.
   - `runbook-mcp-server.md`:
     - Phase 1 (staging), for AuthKit:
       - turn CIMD on at WorkOS (Connect, Configuration), and check that the AuthKit metadata has `client_id_metadata_document_supported: true` and `none` among the token endpoint auth methods;
       - sign in once through a DCR client and once through a CIMD client, and record the `ViaClient` the API stored (the access audit trail) or the exchange's log line;
       - a negative check: an authorization request with a CIMD `client_id` and a `redirect_uri` that is not in its document must be refused by WorkOS;
       - a check of the precondition: a Connect token requested without a resource, and one with `resource` set to the API's audience, must both get 401 from a plain API route.
     - Phase 4: the what-if lists the new settings, and the expected start log is now the Information notice with the untrusted restrictions and the number of trusted clients, not the warning.
     - Verify: an untrusted client reads but cannot write (`ActorReadOnly`), and the access audit trail shows `ViaClient`.
     - Trust a client, a new section. Copy the id the API recorded, never one read from a decoded token: the API reads `azp`, then `appid`, then `client_id`. Then add it to `mcpTrustedClientIds` and run the template. Rules:
       - only a CIMD client with web redirects, never one whose document lists only loopback redirects (any local process can present its URL);
       - never a DCR id: it is random per registration, and anyone can register a client with the same name.
     - Cut off one client, a new section, for stable ids: add the recorded id to `mcpDeniedClientIds` and run the template. The emergency path is the app setting by hand, followed by the same commit before the next run, because the template removes settings set by hand. The Function App restarts and the client's live tokens end.
     - Cut off every unlisted client, a new section, the switch for DCR clients: set `mcpAllowedClientIds` to the trusted list. Every DCR registration is then refused at the exchange, and its live tokens run out within `TokenLifetimeMinutes`. Turning DCR off at WorkOS does the same for the whole environment, including Cursor and other clients without CIMD.
     - Rolling back: an API build from before this change ignores the client lists and refuses tokens that carry `ReadOnly`. Before rolling the API back past this change, turn the exchange off (`mcpEnabled = false`). Turn it on again only with a build that knows `ReadOnly`.
   - `cookbook-azure-production.md` step 17a: the parameters.
   - `decisions.md`, "The MCP server":
     - the `openid` bullet now names the exposure;
     - a new bullet "Every MCP client reads only, until it is trusted" records the client policy and the accepted risk (below);
     - the table "Can change any time" names the client lists.

The accepted risk, as `decisions.md` records it:

- A client the person consents to, trusted or not, reads everything the person can read, except secret values: annotations, configurations, members' and invitees' emails, the audit trails and the machines' metadata.
- Every read and every refused write is logged with the client. Every change is in the audit with `ViaClient`.
- A DCR client is attributed per registration, not per app.
- The deny list does not hold against a client that registers again. `AllowedClientIds`, or turning DCR off, does.
- Every change of the lists needs the API to restart with the new settings, which takes minutes.
- A trusted client writes with the person's full roles, as today, and only reads secrets while the actor allows secrets.
- Loopback CIMD clients are not trusted.

### Phase 6: Documentation of Storyteller

- `mcp.md`:
  - clients and attribution;
  - read-only mode: hidden and refused tools, and the instructions;
  - a blocked client;
  - the corrected "at once" (it means after the API restarts with the new settings).
- `authentication.md`:
  - the claims table (`subject_client_id`);
  - the actor options table and `DeniedClientIds`;
  - the comparisons: the deny list ignores case, the trust list is exact;
  - the restrictions (`ReadOnly`, the middleware, the allow-list);
  - the revocation table: a client by `DeniedClientIds`, every unlisted client by `AllowedClientIds`;
  - the start notice.
- `access.md`: the restrictions, and `ViaClient` on the audit.
- `audit-stamps.md`: the actor table; and the Clients section, which is already stale (the access audit trail shows `Via`).
- `access-audit.md`: `ViaClient`.
- `development.md`: the quoted notice text.
- `42for.net/platform/admin-ui.md`: the access audit trail line.

## Decisions

1. **A top-level private claim `subject_client_id`, not `client_id` and not a nested `act`.** `client_id` names the requester of the token (RFC 8693 section 4.3, RFC 9068), which is the actor. A nested `act` is for prior actors and "informational only". The deny list and the trust list are access control, which RFC 8693 section 4.1 allows only on top-level claims and the current actor. The name says what it is: the client of the subject token.
2. **`ViaClient` on `AuditActor`, not only on access audit records.** Most of what MCP tools do is recorded in stamps, the annotation audit and the key/value audit, not in the access audit trail. `Via` is already on `AuditActor`. The SDKs are regenerated either way.
3. **The deny list ignores case and is exchange-wide; the trust list is exact and per actor.** A wider match is safe only for refusing. An exchange-wide deny list never makes two actors that share an audience differ in policy, so an emergency entry cannot stop the API.
4. **Deny wins, and conflicts are warnings.** A start failure takes down the API, the admin UI and `sform`. The kill switch must work in a hurry.
5. **A denied live token gets 401, the exchange a clear `invalid_grant`.** This matches a removed actor. The MCP server forgets its cached token on a plain 401, so the person soon sees the clear text.
6. **`ReadOnly` is a method rule with an allow-list, in a middleware.** No chokepoint guards every write (Current State). A deny-by-default rule also covers future routes, and a catalog test keeps the allow-list honest.
7. **Production: every client reads only and reveals no secret, until it is trusted.**
   - `mcpEnabled` is still `false`, so nobody loses anything today.
   - The trusted list stays empty until a real sign-in of a CIMD client has been recorded, because it is not verified that AuthKit's `client_id` is the URL. A wrong entry fails safe: the client simply stays read only.
   - DCR clients cannot be trusted by id, since their ids are random per registration.
   - `NoSecretReveal` is in the untrusted restrictions, so secrets never depend on the actor-wide `AllowSecrets`.
8. **The kill switch needs a restart, not a live store.** The settings are bound at start. A change of the app settings restarts the Function App within minutes. A deny list in Cosmos DB, without a restart, is left for later.
9. **Client ids of 1 to 256 printable ASCII characters.** Every provider's ids fit. Anything else is refused, not cut or cleaned, so a stamp, a log line or the admin UI never shows an id that looks like another.
10. **The MCP server's list filter fails open for writes, closed for secrets.** The API enforces `ReadOnly`. A failed or slow exchange during `tools/list`, such as a cold start of the API, must not leave a session with a read-only list. The secret tools keep today's fail-closed behavior.

## Phases and commits

| Phase | Commit |
| --- | --- |
| Spec | `docs(platform): spec of the client attribution, deny list and read-only default of the user token exchange (424-302)` |
| 1 | `fix(platform): record the client of the subject token on Storyteller user tokens, the audit actor and the API logs (424-302)` |
| 2 | `fix(platform): deny, trust and restrict the clients of the user token exchange, with the ReadOnly restriction (424-302)` |
| 3 | `chore(platform): regenerate the SDKs with AuditActor.ViaClient, and show the client in the access audit trail (424-302)` |
| 4 | `fix(platform): hide and refuse the write tools of a read-only client in the MCP server (424-302)` |
| 5 | `fix(platform): deploy the MCP actor with a read-only default and the client lists (424-302)` |
| 6 | `docs(platform): document the clients of the user token exchange (424-302)` |
| Review | `docs(platform): review of the client attribution, deny list and read-only default (424-302)` |

## Out of scope

- **The precondition** (Current State, Production): a client allow-list for AuthKit bearer tokens, so that a Connect token of a self-registered client can never be an API bearer token once the custom AuthKit domain is on. The runbook checks it, and it is left for a follow-up issue.
- A deny list that works without a restart (a Cosmos DB store with an admin route).
- A negative cache of a blocked client's refusal in the MCP server.
- A client deny list or client logging in the MCP server itself. The API is the gate, and the MCP server's console logs print no scopes.
- Display names of clients. DCR's `client_name` and MCP's `clientInfo` are asserted by the client, so they are no identity.
- `sform` printing `Via` or `ViaClient`; it prints neither today.
- WorkOS-side controls (turning DCR or CIMD off, deleting a registration). They are documented as coarse switches only.
- The order in which the API and the MCP server read the client claim (`azp` first, or `client_id` first). AuthKit's Connect tokens carry only `client_id`, and the runbook says to copy the id the API recorded.
