# Storyteller User Token Exchange for the MCP Server

Linear: [424-106 MCP server: provider-neutral sign-in through a Storyteller RFC 8693 user token exchange](https://linear.app/424fun/issue/424-106). It builds on [424-84 Create MCP interface](https://linear.app/424fun/issue/424-84/create-mcp-interface) and its open [PR #131](https://github.com/akobr/mono.me/pull/131) (branch `feat/424-84`).

## Problem

The MCP server of 424-84 acts for the signed-in person, and it must not forward the person's token to the API: that token was issued for the MCP server (MCP authorization specification, "token passthrough"). So the server needs a second token, for the Storyteller API, that names the same person.

424-84 gets that token with **Entra ID on-behalf-of**. That has four costs:

1. **Entra ID only.** WorkOS AuthKit has no on-behalf-of or token exchange grant, and Keycloak is not supported at all.
2. **Identity rests on an undocumented detail.** The API keys accounts by `sub`, Entra documents `sub` as pairwise per application, and whether an on-behalf-of token carries the `sub` of the person's `sform` token is not documented.
3. **The API cannot see the delegation.** An on-behalf-of token is a user token with another `azp`. Nothing in it says "the MCP server acted for this person", and the API cannot apply a policy to such calls (for example: no secret reveals).
4. **Provider code in the MCP server.** MSAL, a confidential-client credential and its cache live in the MCP host.

The proper design is provider-neutral: **the Storyteller API exchanges a token of the person for a Storyteller user token, following RFC 8693, with an `act` claim that names the acting party.** The MCP server authenticates as an actor. Whatever identity provider the deployment uses (Entra ID, AuthKit or Keycloak) is the source of the person's token.

**Deployment model.** A deployment has exactly **one** user identity provider, chosen by `Auth:Provider`: `EntraId`, `AuthKit` or `Keycloak`. Several providers at once are not supported, so there is no account linking between providers.

The inputs to this spec are:

- the explanation of the authentication design given after 424-84, and the discussion of its open questions;
- the 424-84 spec and review (`docs/Platform/Storyteller/specs/2026-10-24 MCP Server for the Storyteller API.md`, `reviews/…`);
- RFC 8693 (OAuth 2.0 Token Exchange), RFC 9068 (JWT access tokens), RFC 7523 (JWT client assertions), RFC 9728 and RFC 8414 (metadata), RFC 8707 (resource indicators), and the MCP authorization specification.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`. Line numbers refer to `feat/424-84` at `fa68e6ed`.

### User tokens in the API

- `Auth:Provider` (`Backend.Core/src/Accessing/UserAuthenticationOptions.cs`, `IdentityProviderKind`) is `EntraId` (`Access.AzureAd/src/EntraIdBearerTokenValidator.cs`) or `AuthKit` (`Access.AuthKit/src/AuthKitBearerTokenValidator.cs`, one issuer: `Auth:AuthKit:Issuer`). Keycloak exists for **machines** only (`Access.Keycloak/src/KeycloakMachineTokenValidator.cs`, `KeycloakOptions`).
- An account is stored with `Id` = the `sub` of the token that registered it (`Backend.CosmosDb/src/Entities/Access/AccountEntity.cs`: `Id`, `UserName`, `Name`, `AccessMap`). `GetIdentityUniqueId` (`Api.Functions/src/Security/HttpRequestDataExtensions.cs`) reads `sub`, and audit actors, access maps, members and invitations use that id.
- There are **no Entra ID accounts in any deployment yet**, so the account id of Entra users can change without a migration.
- 424-84 adds `Auth:DelegatedClients` (`Backend.Core/src/Accessing/DelegatedClientOptions.cs`, `TryGetMachineId` of the Entra validator): a delegated token of a listed Entra client app and tenant is a user.

### The Storyteller token issuer

- `Access.Federation` already issues Storyteller tokens for **Federated machines**. `POST v1/{organization}/{project}/access/token` (`Api.Functions/src/V1/FederationHttp.cs:312-365`) is an RFC 8693 exchange: `grant_type=urn:ietf:params:oauth:grant-type:token-exchange`, `client_id` (the machine), and `subject_token` (a token of a trusted issuer of the project).
- `StorytellerTokenIssuer` (`Access.Federation/src/StorytellerTokenIssuer.cs`) signs RFC 9068 access tokens (`typ at+jwt`, RS256) with `TokenSigningKeyProvider`.
  - Claims: `sub` = `client_id` = machine id, `scope`, `organization`, `project`, the federation issuer and subject, and optionally `annotation_key`.
  - Settings: `MachineAuth:Federation` (`Issuer`, `Audience`, `TokenLifetimeMinutes` 5–60, `ClockSkewSeconds`, `MaxSubjectTokenBytes`; `Backend.Core/src/Accessing/FederationOptions.cs`).
- `StorytellerTokenValidator` (`Access.Federation/src/StorytellerTokenValidator.cs`) **requires** `organization`, `project` and `scope`; its results are machines. `BearerAuthenticationMiddleware` (`Api.Functions/src/Security/BearerAuthenticationMiddleware.cs:55, 80, 161`) hands a token of an owned issuer to that validator alone and passes the result through `AsMachine`.
- `SubjectTokenVerifier` and `FederatedIssuerKeyResolver` verify tokens of OIDC issuers: discovery, JWKS with refresh, a public-address filter against SSRF, and size limits.
- `docs/Platform/Storyteller/authentication.md` notes "Storyteller publishes no JWKS or RFC 8414 metadata for its tokens".

### The MCP server on `feat/424-84`

- **Inbound:** `Mcp/src/Authentication/McpAuthentication.cs`, `McpTokenRules.cs` and `Hosting/McpAuthSettings.cs` validate Entra tokens of one tenant and listed client apps. `OAuthMetadataEndpoints.cs` serves RFC 9728 metadata, plus an RFC 8414 document that points at Entra and adds `S256`.
- **Downstream:** `Authentication/EntraOnBehalfOfTokenProvider.cs` (MSAL, `Microsoft.Identity.Web.TokenCache`) behind `IStorytellerTokenProvider`. `Tools/StorytellerCallFilter.cs` stores the result in the scoped `StorytellerAccessToken`, which the SDK clients read.
- **Settings:** `Mcp:Provider`, `Mcp:ResourceUrl`, `Mcp:EntraId:*` and `Storyteller:*`. `Mcp:SecretTools:Enabled` hides the three tools that return secrets.
- **`Mcp/testint`** compares the `sub` of an on-behalf-of token with the person's `sform` token.

## Proposed Changes

### 1. One provider, one stable account id (`Backend.Core`, `Access.*`, `Api.Functions`)

**Keycloak as a user provider.** `IdentityProviderKind` gains `Keycloak`.

- `Access.Keycloak` gets `KeycloakBearerTokenValidator` for user tokens of the realm (`Auth:Keycloak:Issuer`, `Audience`, JWKS from discovery).
- Its normalizer builds `scp` from realm or client roles, mapped the way `AuthKitClaimNormalizer` maps permissions (`Auth:Keycloak:DefaultUserScopes`, `Auth:Keycloak:RoleMap`).
- `sform` signs in with the device flow of the realm, which Keycloak supports (follow-up in `src/Platform/Cli`, see Out of scope). Until then the API accepts Keycloak user tokens from any client of the realm that is allowed by `Auth:Keycloak:AllowedClientIds`.

**The account id.** One function, `UserIdentity.GetAccountId(claims, provider)` in `Backend.Core/src/Accessing`, decides the id. It is used both by `GetIdentityUniqueId` (every user call) and by the exchange (section 3):

| Provider | Account id | Why |
| --- | --- | --- |
| `EntraId` | `{tid}:{oid}`, lower case | `oid` is the same for a person in every app of the tenant; `sub` is pairwise per application. The tenant prefix keeps tenants apart, because the API's Entra validator does not pin the tenant |
| `AuthKit` | `sub` (`user_…`) | The WorkOS user id is the same in every client and issuer of the environment |
| `Keycloak` | `sub` | The user id of the realm, the same for every client |

- The Entra validator requires `tid` and `oid` on user tokens; a user token without them is `401`.
- **No migration.** No Entra accounts exist yet, so Entra accounts are created with the new id from the start. AuthKit and Keycloak keep `sub`, which existing AuthKit accounts already use.
- **Guard.** At account registration under Entra, an id that is not of the `{guid}:{guid}` form is refused as a programming error. The startup log notes the rule once, so a deployment that switches `Auth:Provider` sees why old accounts no longer resolve (`authentication.md` already warns that a provider switch orphans accounts).
- **Removed:** `Auth:DelegatedClients` and its validator rule from 424-84 (section 6), and the `sub` comparison of `Mcp/testint`.

**Exchange settings.** New options `Auth:TokenExchange` (`TokenExchangeOptions`, validated on start):

```jsonc
"Auth": {
  "TokenExchange": {
    "Enabled": true,
    // Issuers of the deployment's provider whose user tokens may be exchanged, besides the API's own.
    // AuthKit: the AuthKit domain, which issues MCP sign-in tokens, next to the user-management issuer.
    "AdditionalIssuers": [ { "Issuer": "https://auth.example.com", "JwksUri": "https://auth.example.com/oauth2/jwks" } ],
    "Actors": [ /* section 2 */ ],
    "TokenLifetimeMinutes": 10
  }
}
```

The subject token is validated by the API's own provider rules: the Entra issuer and tenant, the AuthKit issuer (or an `AdditionalIssuers` entry of the same WorkOS environment), or the Keycloak realm. Keys come from discovery or `JwksUri`, through the existing `FederatedIssuerKeyResolver` (public-address filter, refresh, size limits). An `AdditionalIssuers` entry is accepted only for the exchange, never as a bearer token on API routes.

### 2. Actors: who may exchange, and for what (`Auth:TokenExchange:Actors`)

The MCP server is an **actor**. The API trusts it explicitly by configuration:

```jsonc
"Actors": [
  {
    "Id": "mcp",                                   // the act.sub of issued tokens
    "Authentication": {                            // how the actor proves itself; one of:
      "WorkloadIdentity": { "Issuer": "https://login.microsoftonline.com/{tid}/v2.0", "Subject": "{managed identity oid}", "Audience": "api://storyteller-token-exchange" },
      // or "ClientAssertion": { "JwksUri": "https://mcp.example.com/.well-known/jwks.json" }  (RFC 7523, private_key_jwt)
      // or "ClientSecretHash": "…"                (local development only; refused outside Development)
    },
    "SubjectAudiences": [ "https://mcp.example.com/mcp" ],   // the subject token must be issued for this actor
    "AllowedClientIds": [ "…" ],                   // optional: azp/client_id of the subject token
    "Scopes": [ "User.Impersonation" ],            // the most an issued token may carry
    "Restrictions": [ "NoSecretReveal" ],          // the default when omitted; section 6
    "AllowSecrets": false                          // true removes NoSecretReveal from the defaults
  }
]
```

- **Proving the actor.**
  - `WorkloadIdentity` reuses the federation verifier. The actor sends `actor_token` (RFC 8693), a token of its platform identity: an Azure managed identity, a Kubernetes service account, or a GitHub OIDC token. No secret is stored anywhere.
  - `ClientAssertion` is an RFC 7523 `client_assertion` signed with the actor's published keys.
  - A hashed client secret exists for local development only.
- **Audience binding.** The subject token's `aud` must be one of the actor's `SubjectAudiences` (its resource URL), so a token the person gave to another app cannot be exchanged by this actor. Without the actor proof, a subject token yields nothing, and neither does the proof without a subject token.
- **Not a machine.** An actor has no project, role or scopes of its own. It can only act for a person, within that person's roles and the actor's `Scopes` and `Restrictions`.
- **Secrets are refused by default.** Every actor gets `NoSecretReveal` unless it sets `AllowSecrets: true`. A secret read through an assistant enters the model's context, the client's transcript and the model provider's logs. Prompt injection in content the model reads can ask for such a read, and the API refusal holds whatever the model is persuaded to do. Writing secrets is not affected: a write returns no value.

### 3. The exchange endpoint (`Api.Functions/src/V1/TokenExchangeHttp.cs`, `Access.Federation`)

`POST v1/access/token` (anonymous route, `application/x-www-form-urlencoded`, RFC 8693):

| Parameter | Value |
| --- | --- |
| `grant_type` | `urn:ietf:params:oauth:grant-type:token-exchange` |
| `subject_token`, `subject_token_type` | the person's token; `urn:ietf:params:oauth:token-type:access_token` (or `…:jwt`) |
| `actor_token`, `actor_token_type` | the actor's workload token (with `WorkloadIdentity`); `…:jwt` |
| `client_assertion`, `client_assertion_type` | with `ClientAssertion` (`urn:ietf:params:oauth:client-assertion-type:jwt-bearer`) |
| `client_id` | the actor `Id` |
| `audience` / `resource` | optional; must be the API (`MachineAuth:Federation:Audience`) |
| `scope` | optional; a subset of the actor's `Scopes` |
| `requested_token_type` | optional; only `urn:ietf:params:oauth:token-type:access_token` |

Processing, in order. Every refusal is `400` with `error` set to `invalid_request`, `invalid_client`, `invalid_grant`, `invalid_scope` or `invalid_target`. Each error has one fixed `error_description`, and the reason is logged without tokens.

1. The actor exists and its proof is valid (`invalid_client`).
2. The subject token is a valid user token of the deployment's provider (section 1): signature, issuer (the API's own or an `AdditionalIssuers` entry), `exp`/`nbf` with skew, and size (`invalid_grant`).
3. Provider rules (`invalid_grant`):
   - Entra: `tid` and `oid` present, a delegated token (`scp`, no `idtyp=app`);
   - AuthKit: `sub` starts with `user_`;
   - Keycloak: a user token, not a service account;
   - the client id is checked against `AllowedClientIds`.
4. `aud` is one of the actor's `SubjectAudiences` (`invalid_grant`).
5. `UserIdentity.GetAccountId` gives the account id, and the account must exist. If it doesn't, the answer is `invalid_grant` with "No Storyteller account exists for this sign-in. Register once with sform or the admin UI.". The exchange never creates accounts.
6. The scope is the requested scope ∩ the actor's `Scopes` (`invalid_scope` when empty).
7. `StorytellerTokenIssuer.IssueUserAsync` signs the token (section 4). The answer is `{ access_token, issued_token_type: "urn:ietf:params:oauth:token-type:access_token", token_type: "Bearer", expires_in, scope, restrictions }`. `restrictions` is a non-standard member that lists the actor's restrictions, so the MCP server can adapt its tools (section 7).

Limits and what stays unchanged:

- The lifetime is at most `TokenLifetimeMinutes` (5–15), and never longer than the subject token's `exp`.
- Replay limit: the same subject token `jti` may be exchanged at most N times per minute per actor (in memory, per instance).
- The existing machine exchange `v1/{org}/{project}/access/token` is unchanged.

### 4. The Storyteller user token

RFC 9068, signed by the existing key provider, with the existing `Issuer` and `Audience`:

```json
{
  "typ": "at+jwt",
  "iss": "https://storyteller.example.com", "aud": "https://storyteller.example.com/api",
  "sub": "<account id>",
  "token_use": "user",
  "scope": "User.Impersonation",
  "act": { "sub": "mcp" },
  "client_id": "mcp",
  "restrictions": [ "NoSecretReveal" ],
  "auth_time": 1793000000, "iat": …, "nbf": …, "exp": …, "jti": "…"
}
```

- `act` (RFC 8693 §4.1) names the actor; this is delegation, not impersonation. Nested `act` is refused on input and never produced.
- `token_use: user` tells it apart from machine tokens, which keep their claims and have no `token_use`.
- `restrictions` is carried in the token, so the API can enforce them without looking up the configuration.
- No name or email in the token: the API reads them from the account.

### 5. Discovery of the Storyteller issuer

- `GET /.well-known/oauth-authorization-server` under the API base (RFC 8414): `issuer`, `token_endpoint` (`…/v1/access/token`), `grant_types_supported` (`urn:ietf:params:oauth:grant-type:token-exchange`), `token_endpoint_auth_methods_supported` (`private_key_jwt`, plus `client_secret_post` in Development), and `jwks_uri`.
- `GET …/jwks` publishes the current and the next public signing keys.
- `GET v1/auth/configuration` adds `Keycloak` as a provider value and `TokenExchange: { Enabled }`.

### 6. Accepting user tokens in the API (`Access.Federation/src/StorytellerTokenValidator.cs`, `BearerAuthenticationMiddleware`)

- `StorytellerTokenValidator` returns a **user** result for `token_use: user`, with claims `sub` (the account id), `scope`, `act` and `restrictions`. Machine tokens are handled as today.
- `BearerAuthenticationMiddleware` no longer forces `AsMachine` on the owned issuer's results. A user result is stored as a user (no `MachineIdentity` item), with a `DelegatedActor` item (`act.sub`) and a `DelegatedRestrictions` item.
- `GetIdentityUniqueId` returns `sub` for these tokens: it already is the account id. `EnsureUser` and the role checks apply unchanged.
- **`NoSecretReveal`** answers `403 ActorRestricted` for `reveal=true`, secret version reads and diffs, and exports with secrets. A resolved read under the restriction is evaluated without secrets, as for a user without secret rights, and carries the header `X-2S-Secrets-Withheld: actor-restricted`. The list of restrictions is closed and documented; a new restriction needs a spec. Restrictions run in every build, `DEV_AUTH` included.
- **Audit.** `AuditActor` gets an optional `Via` (the actor id), so a change made through the MCP server reads "Ada via mcp". `GetAuditActor` fills it from `DelegatedActor`, and key/value audit events record it too.
- **Removed:** `Auth:DelegatedClients`, `DelegatedClientOptions` and the delegated-client rule of the Entra validator and normalizer from 424-84. That PR is not merged, so nothing depends on them.

### 7. The MCP server (`Mcp/src`, on `feat/424-84`)

**Inbound, per provider.** `Mcp:Provider` becomes `EntraId | AuthKit | Keycloak`, and must match the API's `Auth:Provider`. One settings shape:

| Key | Meaning |
| --- | --- |
| `Mcp:Authentication:Issuer` | The authorization server MCP clients sign in with (Entra tenant issuer, AuthKit domain, Keycloak realm) |
| `Mcp:Authentication:Audience` | default `Mcp:ResourceUrl` |
| `Mcp:Authentication:Scope` | default `Mcp.Access` (AuthKit and Keycloak: the scope or permission configured there) |
| `Mcp:Authentication:AllowedClientIds` | required for Entra; optional for AuthKit and Keycloak (dynamic registration) |
| `Mcp:Authentication:TenantId` | Entra only |

- **Validation** keeps the pinned issuer, the audience, RS256, the lifetime and the delegated-token rule of each provider.
- **Authorization server metadata.** For AuthKit and Keycloak, the protected resource metadata names **their** issuer: both publish metadata with PKCE methods and support dynamic client registration. The host's own RFC 8414 document stays for Entra only.

**Downstream.** `EntraOnBehalfOfTokenProvider` and MSAL are removed. `StorytellerTokenExchangeProvider` (an `IStorytellerTokenProvider`) calls `POST {Storyteller:BaseUrl}/v1/access/token` with the person's token as `subject_token`, plus the actor proof:

- `Mcp:Actor:Id`;
- one credential: `Mcp:Actor:WorkloadIdentity` (for example a managed identity read with `Azure.Identity`), `Mcp:Actor:ClientAssertionKey` (a certificate whose JWKS the server publishes at `/.well-known/jwks.json`), or `Mcp:Actor:ClientSecret` (Development only).

It caches per subject-token hash until 60 s before `exp`, bounded in size. It maps `invalid_grant` (no account) and `invalid_client` to the fixed sign-in errors of `StorytellerCallFilter`. `Storyteller:Scopes` goes away.

**Secret tools follow the API.** `Mcp:SecretTools:Enabled` stays as a local switch that can only remove tools. The server hides `reveal_key_value`, `get_resolved_configuration` and `check_configuration_bindings` when either:

- the setting is off, or
- the exchange reports `NoSecretReveal` for the actor (from the `restrictions` of the first exchange, refreshed with the cache).

So the tools offered never contradict what the API allows. The API restriction remains the enforcement, and the setting remains a presentation choice.

**What stays and what goes:**

- The call filter, the scoped token holder, the SDK wiring, the other tools and the safety rules are unchanged.
- `get_my_account` explains a missing account as "register once with sform or the admin UI".
- `Microsoft.Identity.Client`, `Microsoft.Identity.Web.TokenCache` and the `Pkcs` pin that came with them leave `Mcp.csproj`. `Azure.Identity` is added only if workload identity needs it.
- **Removed:** the AuthKit refusal at start, `Mcp:EntraId:*` (replaced by `Mcp:Authentication:*`) and the `sub` comparison of `Mcp/testint`. `testint` instead runs the exchange against a configured environment, and is skipped without one.

### 8. API surface, OpenAPI and SDKs

- New routes: `POST v1/access/token`, and `GET /.well-known/oauth-authorization-server` and `jwks` under the API base. `v1/auth/configuration` gains its new members.
- New error code `ActorRestricted`, and the response header `X-2S-Secrets-Withheld: actor-restricted`.
- A new OpenAPI snapshot; the C# (NSwag) and TypeScript (Orval) SDKs are regenerated in their own commit.

### 9. Security notes

- **Stolen tokens.** The exchange requires the actor proof **and** a subject token issued to that actor. Tokens live a short time, the subject token's `exp` caps the lifetime, and replay is limited.
- **No leaks in refusals.** Clients get fixed texts; the reasons are only logged, never with tokens. Key resolution keeps the SSRF filter.
- **Who adds trust.** Actors and additional issuers are deployment configuration, so only operators add them. Projects keep their own trusted issuers for machines.
- **Restrictions** run in every build.
- **Signing keys** rotate with the existing provider; the JWKS publishes the next key ahead of use.

### 10. Tests

- `Access.Federation/test`:
  - the exchange per provider, with every refusal of section 3 and its error;
  - audience binding;
  - the actor proof by workload identity, client assertion and secret (Development);
  - scope narrowing, the lifetime cap, the replay limit, a nested `act` refused;
  - the default `NoSecretReveal` and `AllowSecrets`;
  - the user token's claims, and the validator's user and machine shapes.
- `Access.AzureAd/test`: the Entra account id `{tid}:{oid}`, case, and the missing `tid`/`oid` refusals. `Access.Keycloak/test`: the user validator and its role map. `Access.AuthKit/test`: the additional issuer is accepted for the exchange only.
- `Api.Functions/test`:
  - the middleware stores a Storyteller user token as a user with `DelegatedActor`;
  - role checks;
  - `NoSecretReveal` on every guarded route in Debug and Release, and the withheld header on resolved reads;
  - audit `Via`;
  - the metadata, JWKS and configuration routes;
  - Entra account registration with the new id.
- `Mcp/test`:
  - inbound per provider and the metadata it serves;
  - the exchange client (request shape, cache, error mapping);
  - the secret tools hidden when the API reports `NoSecretReveal`;
  - no MSAL left.
- `Mcp/testint`: the exchange end to end against a configured environment.

### 11. Documentation

- `docs/Platform/Storyteller/authentication.md`:
  - Keycloak as a user provider;
  - the account id per provider;
  - a section "User token exchange" (actors, the token, restrictions);
  - "Signing in through the MCP server" rewritten for any provider;
  - the `Auth:DelegatedClients` row and paragraph removed;
  - "Not available yet" updated (JWKS and metadata are published; AuthKit and Keycloak MCP sign-in work).
- `docs/Platform/Storyteller/mcp.md`: setup per provider, the actor registration, the restrictions and the secret tools. The identity section is replaced.
- `docs/Platform/Storyteller/access.md`: actor restrictions. `audit-stamps.md`: `Via`.
- The 424-84 spec and review stay as they are. This spec's review records what changed on the branch.

### 12. Phases and commits (on `feat/424-84`)

1. `docs(platform): plan the Storyteller user token exchange` (this spec, into `docs/Platform/Storyteller/specs/`).
2. **Phase 1**, `feat(platform): key Entra accounts by tenant and object id and add Keycloak users` (section 1).
3. **Phase 2**, `feat(platform): exchange user tokens for Storyteller tokens with an actor` (sections 2–5).
4. **Phase 3**, `feat(platform): accept Storyteller user tokens with actor restrictions and audit` (section 6, and the removal of `Auth:DelegatedClients`).
5. `chore(platform): regenerate the SDKs from OpenAPI 0.9.N` (section 8).
6. **Phase 4**, `feat(platform): sign in to the MCP server with any provider through the token exchange` (section 7).
7. `docs(platform): document the user token exchange`.
8. `docs(platform): review the user token exchange`.

Every commit body names `Refs: 424-106`.

### 13. Verification

- `dotnet build 42.mono.slnx`.
- Tests of `Access.Federation`, `Access.AzureAd`, `Access.AuthKit`, `Access.Keycloak`, `Backend.CosmosDb` (emulator), `Api.Functions` (Debug and Release), `Mcp`, `Cli`, and the TypeScript SDK.
- The exchange end to end with Entra and with AuthKit (staging): an existing account is found, and a sign-in without an account is refused with the fixed text.

### Decisions

1. **`NoSecretReveal` is the default for every actor**, removed only with `AllowSecrets: true` (section 2).
2. **`Mcp:SecretTools` stays**, as a switch that can only remove tools. The MCP server also hides the secret tools whenever the API reports `NoSecretReveal`, so the two never disagree (section 7).
3. **No account linking.** A deployment has one provider. The account id is stable per provider: `{tid}:{oid}` for Entra, `sub` for AuthKit and Keycloak (section 1). No Entra accounts exist, so no migration is needed.

### Out of scope

- Several identity providers in one deployment, and account linking between them.
- `sform` device sign-in with Keycloak (the API side is part of this).
- Exchanges by actors other than MCP servers, chained actors, and refresh tokens from the exchange.
- Per-project actors, and dynamic client registration in Storyteller itself.

## Implementation Plan

Added while planning, after mapping the code of `feat/424-84` at `a19c0a39` and reading the CodeRabbit coding plan of 424-106 as a second opinion. The sections above stay the intent. This part fixes the details they leave open and the places where the code differs from them. Each item names the section it refines.

### Work branch

The work is on `feat/424-106`, branched from `feat/424-84` at `a19c0a39`. It goes to `feat/424-84` as a pull request stacked on PR #131, so PR #131 gets these commits when that pull request is merged. The commits are those of section 12, each with `Refs: 424-106`.

### Decisions on the open points

1. **Where the account id is applied (section 1).** The provider modules apply `UserIdentity.GetAccountId` at their boundary and write the result as `sub`:
   - the Entra validator on its validated path, which today builds its result without the normalizer;
   - the Entra normalizer on the `DEV_AUTH` path.

   Both remove the original `sub` and `ClaimTypes.NameIdentifier`, keep the pairwise value as `entra_sub`, and read `tid`/`oid` in both raw and mapped claim types. `GetIdentityUniqueId` keeps reading `sub`, so every consumer and every test double stays unchanged. The exchange calls the same function. A user token without `tid` or `oid` is `401` (on the `DEV_AUTH` path, `sub` is dropped and the call is `401`).
2. **Registration guard (section 1).** The `{guid}:{guid}` guard runs in both places that create accounts: `AccessHttp.PostAccount` and invitation acceptance (`InvitationsHttp`). The Entra registration logs the account-id rule once at start.
3. **Keycloak user settings (section 1).** `Auth:Keycloak` is a new `KeycloakUserOptions` in `Backend.Core`: `Issuer`, `Audience`, `ClientId` (optional, for discovery), `AllowedClientIds`, `DefaultUserScopes` and `RoleMap`. It is separate from the root `Keycloak` section of machines. A token is a user token when it has `sub` and is not a service account: no `client_id`/`clientId` claim, and no `preferred_username` that starts with `service-account-`. Roles come from `realm_access.roles` and `resource_access.{client}.roles`. Every place that treats "not AuthKit" as Entra gets an explicit Keycloak branch:
   - `UserAuthenticationOptionsValidator`;
   - `Program.cs`;
   - `AuthConfigurationHttp`, where `AuthConfiguration` gains `Issuer`;
   - `OAuthFlows`;
   - `InvitationsHttp.IsEmailVerified`, where Keycloak requires `email_verified`;
   - the test doubles.
4. **Subject tokens are validated by the provider modules (sections 1 and 3).** A new contract `IUserSubjectTokenValidator` in `Backend.Core` takes the raw token and the actor's audiences. It returns the claims, the account id and the client id, or a refusal reason that is logged. Each provider implements it with its own key source, the configuration managers it already has:
   - Entra: the configured tenant's v2 and v1 issuers, or any Entra issuer when `Auth:TenantId` is `common` or `organizations`;
   - AuthKit: the `Auth:AuthKit:Issuer` and the `AdditionalIssuers` entries, with keys from `JwksUri`;
   - Keycloak: the realm.

   This differs from section 1, which names `FederatedIssuerKeyResolver`. That resolver is keyed by organization and project, and the provider modules do not reference `Access.Federation`. The URLs are operator configuration, never taken from a token. `AdditionalIssuers` is accepted for AuthKit only; the other providers refuse it at start. An entry equal to `Auth:AuthKit:Issuer` is also refused, because that issuer's tokens already pass on API routes.
5. **Tokens issued to an actor never pass as API bearer tokens (sections 1 and 9).** The bearer middleware refuses (`401`) a provider user token whose `aud` is one of an actor's `SubjectAudiences`. That is the token passthrough the MCP specification forbids, and it makes "only for the exchange" hold even when an AuthKit audience is not configured.
6. **Actor proof (section 2).** `ActorAuthenticator` in `Access.Federation` covers three proofs:
   - `WorkloadIdentity` and `ClientAssertion` keys come through `FederatedIssuerKeyResolver`, with a synthetic deployment scope and its SSRF-safe HTTP client;
   - the client assertion (RFC 7523) needs `iss` = `sub` = the actor id, `aud` = the token endpoint or the Storyteller issuer, a lifetime of at most 5 minutes, and a `jti` that was not seen before (a bounded replay cache);
   - `ClientSecretHash` is a SHA-256 hash compared in constant time, accepted only in Development, and sent as `client_secret` (`client_secret_post`).

   The shared verification core takes the audience and lifetime policy as parameters. The machine rules (one `aud`, 3,600 s) would refuse Entra access tokens and managed identity tokens.
7. **Exchange errors (section 3).** `OAuthTokenExchange.Errors` gains `invalid_client` and `invalid_grant`. Each refusal has a fixed `error_description`. `invalid_grant` has two of them: the "No Storyteller account exists…" text of step 5, and a generic one for steps 2–4. Three more answers apply:
   - a disabled exchange answers `400 unsupported_grant_type`;
   - key or signing failures answer `503 temporarily_unavailable` with `Retry-After`;
   - the body limit is 64 KiB, because the request carries two JWTs.

   The replay limit counts per actor and per subject token (`jti`, else the SHA-256 of the token). The default is 10 a minute, set by `Auth:TokenExchange:MaxExchangesPerSubjectTokenPerMinute`.
8. **Actor scopes (sections 2 and 4).** An actor's `Scopes` default to, and must contain, `User.Impersonation`. Without it an issued token could not act for the person. The validator projects `scope` into `scp` claims, the claim type the API reads.
9. **Name of the person (sections 4 and 6).** The token carries no name, as section 4 says. The bearer middleware reads the account once per call on the Storyteller user path (one point read) and adds `name` and `preferred_username` from it. So `GetAuditActor` stays synchronous and the audit reads "Ada via mcp". A token whose account no longer exists is `401`.
10. **Who may return users (section 6).** Only an owner of a marker contract (`IUserTokenIssuerValidator`, implemented only by `StorytellerTokenValidator`) may return a user result with a `BearerDelegation` (actor id and restrictions). The `AsMachine` rule stays for every other owner. The delegation goes into the `DelegatedActor` and `DelegatedRestrictions` context items, never from raw claims. A user token is accepted only when its `act.sub` is a configured actor, and every restriction in it is known; unknown ones refuse the token.
11. **`NoSecretReveal` in the API (section 6).** `GetSecretRightsAsync` splits the user's read and write rights and applies the restriction after the `DEV_AUTH` block, so it runs in every build. `SecretRights` gains `IsReadWithheldByActor` (true when the role grants secret reads and the actor withholds them) and `WithheldByActor = "actor-restricted"`. The refusal is `403 ActorRestricted` on four routes:
    - reveal reads, decided before the service is asked, as for mTLS;
    - secret version reads;
    - diffs of secret versions only, so `diff_key_value` on public values keeps working;
    - exports with secrets.

    The resolved read is evaluated without secrets, adds the header, and gets its own ETag rights code. `X-2S-Secrets-Withheld` already exists for mTLS; only the value `actor-restricted` is new.
12. **Signing keys and discovery (section 5).** The key store holds one key and has no rotation, so the JWKS publishes the keys the validator accepts: today, the current one. A "next" key needs key rotation, which is out of scope. The metadata and JWKS are served under the API base as `GET /api/.well-known/oauth-authorization-server` and `GET /api/.well-known/jwks.json`. The metadata `issuer` is `MachineAuth:Federation:Issuer`, so RFC 8414 clients need it to be an https URL; the documentation says so.
13. **The MCP server (section 7).** It has these settings:
    - `Mcp:Authentication:ClientId` (Entra only) stays an accepted audience, because Entra v2 tokens carry the client id as `aud`. The Entra issuers are derived from `TenantId` (v2 and v1).
    - The workload identity of `Mcp:Actor` is either `ManagedIdentity` (`Azure.Identity`, which section 7 allows; it brings MSAL in transitively, so "no MSAL" means no direct reference and no use) or `TokenFile` (a projected token file).

    Its behaviour:
    - The exchange client is a singleton on its own `HttpClient` without retries, because retries would count against the replay limit. It caches per subject-token hash in a bounded cache and coalesces exchanges in flight.
    - The secret tools are removed by a `tools/list` filter, with a private cache scope and fail-closed behaviour.
    - The call filter refuses them too, because a hidden tool is still callable.

### Recorded for the review, not changed here

- The API's Entra bearer validator accepts any Entra issuer and does not pin `RS256`.
- sform refuses `Provider: Keycloak` with "Unknown sign-in provider"; Keycloak sign-in in sform is out of scope.
- Under `NoSecretReveal`, an actor with write rights can still compare a guessed value through unchanged writes and import dry runs.
- `Api.Web` builds its own audit actor from `sub`.
