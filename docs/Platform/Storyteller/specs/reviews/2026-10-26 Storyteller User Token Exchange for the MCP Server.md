# Storyteller User Token Exchange for the MCP Server

Linear: [424-106 MCP server: provider-neutral sign-in through a Storyteller RFC 8693 user token exchange](https://linear.app/424fun/issue/424-106)

## Overview

The MCP server no longer uses Entra ID on-behalf-of. The Storyteller API now exchanges a token of the person for a Storyteller user token (RFC 8693, `POST v1/access/token`). The token names the server as the actor in `act` and carries the actor's `restrictions`. The MCP server proves itself as an actor configured in `Auth:TokenExchange:Actors`: with a workload identity, a client assertion, or a client secret in Development. The person's token comes from the deployment's one identity provider: Entra ID, AuthKit, or Keycloak, which is now also a user provider. Entra accounts are keyed by `{tid}:{oid}`. The API accepts the issued tokens as users and records the actor as `Via` in audit stamps. Under `NoSecretReveal`, the default of every actor, it refuses secret reads and the credentials that would give them out. The issuer publishes RFC 8414 metadata and a JWKS, and `Auth:DelegatedClients` of 424-84 is gone. This review covers [Storyteller User Token Exchange for the MCP Server](../2026-10-26%20Storyteller%20User%20Token%20Exchange%20for%20the%20MCP%20Server.md) in full, delivered in the phases of its section 12, one review for all of them.

The work branch `feat/424-106` starts from `feat/424-84` at `a19c0a39`. It goes into `feat/424-84` as a pull request stacked on [PR #131](https://github.com/akobr/mono.me/pull/131), not as pushes to `feat/424-84`, so merging it updates PR #131. The issue asked for the work on `feat/424-84` itself, and the AI workflow asks for a branch and a pull request of its own; the stacked pull request does both. This review is the last commit of the branch.

| Commit | Subject |
| --- | --- |
| `1eec9f52` | docs(platform): plan the Storyteller user token exchange |
| `a03608e6` | Phase 1, feat(platform): key Entra accounts by tenant and object id and add Keycloak users |
| `33fbb37b` | Phase 2, feat(platform): exchange user tokens for Storyteller tokens with an actor |
| `1e9920dd` | Phase 3, feat(platform): accept Storyteller user tokens with actor restrictions and audit |
| `debdbf81` | chore(platform): regenerate the SDKs from OpenAPI 0.9.54 |
| `03671fb2` | Phase 4, feat(platform): sign in to the MCP server with any provider through the token exchange |
| `d8f99902` | fix(platform): close the gaps found in the review of the user token exchange |
| `f6b38cb3` | docs(platform): document the user token exchange |

## What Was Done

### 1. One provider, one stable account id

As planned, with these details:

- `Backend.Core/src/Accessing/UserIdentity.cs`: `GetAccountId(claims, provider)` gives the lower-case `{tid}:{oid}` under Entra ID (both GUIDs, read under the raw and the mapped claim types) and the trimmed `sub` under AuthKit and Keycloak. `IsEntraAccountId` accepts only the `{guid}:{guid}` form. `IdentityProviderKind` gains `Keycloak`.
- The id is applied at the provider boundary (Implementation Plan decision 1). `EntraIdBearerTokenValidator` (validated path) and `EntraIdClaimNormalizer` (`DEV_AUTH` path) replace `sub` and `NameIdentifier` with the account id and keep the pairwise value as `entra_sub`; a token-supplied `entra_sub` is dropped. A user token without a GUID `tid` and `oid` is `401`. `GetIdentityUniqueId` still reads `sub`.
- `HttpRequestDataExtensions.GetRegistrationAccountId` guards `AccessHttp.PostAccount` and invitation acceptance and decline. `EntraIdAccountIdNotice` logs the rule once at start. `Definitions.Descriptions.AccountId` names the id per provider.
- **Keycloak users**:
  - `KeycloakUserOptions` (`Auth:Keycloak`: `Issuer`, `Audience`, `ClientId`, `AllowedClientIds`, `DefaultUserScopes`, `RoleMap`), separate from the root `Keycloak` section of machines.
  - `KeycloakBearerTokenValidator`: RS256, the exact issuer, the audience, 30 s skew, keys from the realm's metadata, person tokens only. A token of another issuer is refused before any key fetch.
  - `KeycloakClaimNormalizer` builds `scp` from `DefaultUserScopes` and the `RoleMap` entries of the token's realm roles and `{client}:{role}` client roles. It also sets `name` and `preferred_username`.
- `Program.cs`, `UserAuthenticationOptionsValidator`, `AuthConfigurationHttp`, `OAuthFlows`, `InvitationsHttp` and the test doubles have an explicit Keycloak branch. Keycloak invitations match the `email` claim only and need `email_verified`.
- After the review: `Auth:Keycloak:AllowedClientIds` is required, and `ClientId` must be one of them. `KeycloakMachineTokenValidator` accepts only service-account tokens, so a refused person token can no longer pass as a machine. DbCreator seeds the owner with `baseAccount:id` (`BaseAccountOptions`, checked before anything is written), and the Aspire host passes it on from `BaseAccount:Id`.

### 2. Actors: who may exchange, and for what

As planned, with these details:

- `TokenExchangeOptions` (`Auth:TokenExchange`):
  - `Enabled`, `AdditionalIssuers`, `Actors`;
  - `TokenLifetimeMinutes`, 5 to 15, default 10;
  - `MaxExchangesPerSubjectTokenPerMinute`, 1 to 60, default 10.
- `TokenExchangeActorOptions`: `Id`, `Authentication` (one of `WorkloadIdentity`, `ClientAssertion`, `ClientSecretHash`), `SubjectAudiences`, `AllowedClientIds`, `Scopes`, `Restrictions` and `AllowSecrets`. `GetScopes()` defaults to `User.Impersonation`, and `GetRestrictions()` adds `NoSecretReveal` unless `AllowSecrets` is set. The bound arrays stay empty, so the configuration binder cannot append to a default.
- `TokenExchangeOptionsValidator` runs at start and reports every failure. It refuses:
  - ids that are not unique (ignoring case) or are longer than 64 characters, and an actor without exactly one proof;
  - `ClientSecretHash` outside Development, and `ClientAssertion` without `MachineAuth:Federation:PublicBaseUrl`;
  - scopes that are not Storyteller scopes or lack `User.Impersonation`, and unknown restrictions;
  - `SubjectAudiences` that name the API's own audience (case and a trailing slash ignored);
  - a `SubjectAudiences` entry that two actors with different policies share;
  - a workload identity audience of another service, and an Entra v2 issuer paired with an audience that is not a GUID;
  - `AdditionalIssuers` under any provider but AuthKit, and an entry equal to `Auth:AuthKit:Issuer`.
- `ActorAuthenticator` (`Access.Federation`):
  - **Workload identity**: keys come through `FederatedIssuerKeyResolver` under a synthetic scope (organization `~token-exchange`, the actor id as project), behind its SSRF filter.
  - **Client assertion** (RFC 7523): `iss` = `sub` = the actor id, and `aud` the token endpoint (from `PublicBaseUrl`) or the Storyteller issuer. It may live at most 5 minutes, and its `jti` is used once, kept in a bounded replay record.
  - **Secret**: the SHA-256 is compared in constant time, in Development only.
  - `jku` and `jwk` headers are never followed.
- `IssuerTokenVerifier` is the shared verification core, with the audience and lifetime policy as parameters. `SubjectTokenVerifier` calls it with the machine policy and keeps its results and log reasons.

### 3. The exchange endpoint

As planned, with these details:

- `TokenExchangeHttp.PostUserToken` (`ExchangeUserToken`) is anonymous and form only. It refuses an `Authorization` header and caps the body at 64 KiB. `OAuthTokenForm` holds the form reading it shares with `FederationHttp`.
- `UserTokenExchangeService` checks, in order:
  1. the exchange is enabled (`unsupported_grant_type`);
  2. the request shape;
  3. the actor and its proof (`invalid_client`);
  4. the subject token (`invalid_grant`): its size, the provider's `IUserSubjectTokenValidator` with the actor's `SubjectAudiences` and `AllowedClientIds`, no `act`, and at least 5 s of life left;
  5. the account;
  6. the replay limit;
  7. the scope: requested ∩ the actor's, which must include `User.Impersonation` (`invalid_scope`).

  Then it issues the token. Unavailable keys or signing key give `503 temporarily_unavailable` with `Retry-After`.
- **Subject token validators** (Implementation Plan decision 4):
  - `EntraIdUserSubjectTokenValidator`: RS256, the v2 or v1 issuer of the pinned tenant, a delegated token, GUID `tid` and `oid`.
  - `AuthKitUserSubjectTokenValidator`: `Auth:AuthKit:Issuer`, and the `AdditionalIssuers` with keys from their `JwksUri`; `sub` starts with `user_`.
  - `KeycloakUserSubjectTokenValidator`: the realm, person tokens, and `azp` against the actor's `AllowedClientIds`.
  - `UserSubjectTokenRules` holds the shared checks: no nested `act`, and the client id from `azp`, `appid` or `client_id`.
- After the review, the Entra tenant is pinned for every kind of `Auth:TenantId`. A GUID pins itself. `common` takes the token's tenant, and `organizations` does the same but refuses personal Microsoft accounts. A domain name is pinned to the tenant in the metadata issuer, or every token is refused.
- `invalid_grant` has two fixed texts: "No Storyteller account exists for this sign-in. Register once with sform or the admin UI." and a generic one for every other refusal. Reasons are logged without tokens; IdentityModel failures are logged as the exception type only.
- The replay limit counts per actor and subject token (`jti`, else the SHA-256 of the token) in a one-minute window. The record holds at most 50,000 entries and refuses when full.

### 4. The Storyteller user token

As planned: `StorytellerTokenIssuer.IssueUserAsync` signs an RFC 9068 token (`at+jwt`, RS256, the existing key provider, `Issuer` and `Audience`). It holds:

- `sub` (the account id), `token_use: user`, `scope` and `client_id`;
- `act` as the object `{"sub": actorId}`;
- `restrictions`, an empty array when there are none;
- `auth_time` when known, `jti`, `iat`, `nbf` and `exp`.

The lifetime is the shorter of `TokenLifetimeMinutes` and the subject token's remaining life. The token has no name and no email.

### 5. Discovery of the Storyteller issuer

As planned, with these details:

- `AuthorizationServerHttp` serves `GET /api/.well-known/oauth-authorization-server`:
  - `issuer` is `MachineAuth:Federation:Issuer`;
  - `token_endpoint` and `jwks_uri`;
  - the token exchange grant;
  - `private_key_jwt`, plus `client_secret_post` in Development;
  - the client assertion algorithms;
  - `response_types_supported: []`, which RFC 8414 requires.
- It also serves `GET /api/.well-known/jwks.json` from `TokenSigningKeyProvider.GetPublicJsonWebKeysAsync`, which never creates a key.
- The URLs come from `PublicBaseUrl`, else from the request, never from `X-Forwarded-Host`. The metadata is `no-store` without `PublicBaseUrl` and `public, max-age=300` with it. An empty JWKS is `no-cache`, and a failing key store answers `503`. The metadata is served also while the exchange is off.
- The JWKS publishes the current key only (Implementation Plan decision 12).
- `v1/auth/configuration` gains `Keycloak`, `Issuer` and `TokenExchange: { Enabled }`, and no actor details.

### 6. Accepting user tokens in the API

As planned, with these details:

- `StorytellerTokenValidator` reads the payload once (duplicate claims are refused) and branches on `token_use` before it reads any machine claim. A user token needs:
  - a string `sub` and `scope`, and no `organization`, `project` or `annotation_key`;
  - one `act` object whose `sub` is a configured actor while the exchange is enabled, with no nested `act`;
  - a `restrictions` array of known values (a missing claim is refused).

  The scope becomes `scp` claims. A machine token that carries `act` is refused.
- After the Phase 3 review, the restrictions enforced are the union of the token's and the actor's current ones.
- `IUserTokenIssuerValidator` (a marker that only `StorytellerTokenValidator` implements) and `BearerDelegation` (decision 10). The middleware keeps a user result of an owner only when the owner implements the marker and the result has a delegation; every other owner result goes through `AsMachine`. `DelegatedActor` and `DelegatedRestrictions` come only from the delegation.
- For a Storyteller user token, the middleware reads the account once per call and sets `name` and `preferred_username` from it. Without the account, the call is `401`.
- A token issued to an actor is refused as an API bearer token (decision 5): any result not from an owner whose `aud` is an actor's `SubjectAudiences` is `401`, on the validated and the `DEV_AUTH` path.
- **`NoSecretReveal`** (decision 11):
  - `GetSecretRightsAsync` applies it after the `DEV_AUTH` block, and `SecretRights.IsReadWithheldByActor` records it.
  - `403 ActorRestricted` (`ErrorCodes.ActorRestricted`, `KeyValueErrors.ActorRestricted()`) answers a reveal (decided before the service is asked, public pairs too), a secret version read, a diff of secret versions and an export with secrets.
  - A delegated `Reader` gets `AccessDenied`, as on a direct call.
  - The resolved read is evaluated without secrets and carries `X-2S-Secrets-Withheld: actor-restricted` on the `200` and the `304`. It gets the ETag rights code `a`, and `DependencyManifests.CallerOf` includes `Via`.
- The reviews extended the restriction to credentials that read secrets, with `403 ActorRestricted`: creating or resetting a machine with a Secrets scope and replacing its federation rules (Phase 3), and replacing an existing trusted issuer (the whole-branch review). Deleting a machine or a trusted issuer, and creating a new trusted issuer, stay open.
- A call through an actor is never email-verified. `GET v1/access/invitations/mine` answers `[]`, and accepting or declining answers `403`.
- **Audit**: `AuditActor.Via` is optional and left out when null. `GetAuditActor` fills it from `DelegatedActor`, so key/value audit events carry it too.
- `DelegatedClientOptions`, `UserAuthenticationOptions.DelegatedClients` and the delegated-client rules of the Entra validator and normalizer are removed.
- After the Phase 2 review, a project cannot store the deployment's own issuer as a trusted issuer, and `SubjectTokenVerifier` refuses Storyteller tokens. So a user token cannot become a machine token through federation.

### 7. The MCP server

As planned, with these details:

- **Inbound**:
  - `McpIdentityProvider` gains `Keycloak`.
  - `McpAuthenticationOptions` (`Mcp:Authentication`): `Issuer`, `Audience`, `Scope` (default `Mcp.Access`), `AllowedClientIds`, and `TenantId` and `ClientId` for Entra only.
  - `McpAuthSettings` is built per provider. Under Entra it derives the v2 and v1 issuers from the tenant and accepts `[ClientId, Audience]`; under AuthKit and Keycloak it uses the `Issuer` and its OpenID metadata.
  - `McpTokenRules.Check` keeps the Entra rules. Under AuthKit, `sub` must start with `user_` and the client must be listed; under Keycloak, the token must be a person token. Scopes are read from `scp`, `scope`, and `permissions` under AuthKit.
- After the review, inbound tokens get 30 s clock skew and need 10 s of remaining life. Otherwise the answer is `401 invalid_token`, so the client refreshes.
- The protected resource metadata names the provider's issuer for AuthKit and Keycloak. The host's RFC 8414 document stays for Entra.
- **`StorytellerTokenExchangeProvider`** (singleton) posts the RFC 8693 form to `{Storyteller:BaseUrl}/v1/access/token`. The proof comes from `McpActorCredential`:
  - a managed identity token (`Azure.Identity`) or a projected token file, sent as `actor_token`;
  - an RS256 client assertion of 2 minutes, signed with `Mcp:Actor:ClientAssertionKey`, whose `aud` is the token endpoint or the optional `ClientAssertionKey:Audience`;
  - or `client_secret` in Development.
- Its own `HttpClient` has no resilience handler and follows no redirects. 30 s bound the whole exchange.
- The cache holds up to 10,000 entries, keyed by the SHA-256 of the subject token, until 60 s before expiry. An answer that expires within the minute is kept until 5 s before expiry. Concurrent calls for one token share one exchange.
- **Errors**:
  - `invalid_grant` shows the API's text, with control characters removed and cut at 300 characters.
  - `invalid_client` gives a fixed text that names `Auth:TokenExchange:Actors` and `Mcp:Actor`.
  - `unsupported_grant_type` says the exchange is off.
  - `502`, `503`, `504` and transport failures are "temporarily unavailable".
  - The exceptions are `StorytellerSignInRefusedException`, `StorytellerActorException`, `StorytellerSignInUnavailableException` and `StorytellerSignInFailedException`.
- `GET /.well-known/jwks.json` publishes the client assertion key. Without a key it answers `404`; while the key file cannot be read it answers `503`, and the file is read again only after 30 s.
- **Secret tools**:
  - `SecretToolNames` holds the three names.
  - `SecretToolsFilter` hides them from `tools/list` when the exchange reports `NoSecretReveal`. It sets `CacheScope = Private`, and it fails closed: any failed exchange hides them.
  - `StorytellerCallFilter` refuses them under `NoSecretReveal` with the `ActorRestricted` text, without calling the API.
- `get_my_account` uses the API's no-account text, and the `401` text no longer names `Auth:DelegatedClients`. The `ActorRestricted` text covers both refused reads and refused credentials.
- `McpStartupNotice` (whole-branch review) logs which `SubjectAudiences` the API's actor must list and, under Entra, the one tenant the server serves.
- **Removed**: `EntraOnBehalfOfTokenProvider`, `StorytellerSignInRequiredException`, `McpEntraIdOptions`, `Storyteller:Scopes` and the AuthKit refusal. `Mcp.csproj` drops `Microsoft.Identity.Client`, `Microsoft.Identity.Web.TokenCache` and the `Pkcs` pin, and adds `Azure.Identity` and `TestableIO.System.IO.Abstractions.Wrappers`. `DependencyTests` checks that no MSAL package, namespace or assembly is referenced directly.
- `Mcp/testint` holds `TokenExchangeTests` and `ExchangeTestSettings` (`MCP_TEST_*`). The `AppHost.cs` comment holds the local recipe of the server and the API's `Auth:TokenExchange`.

### 8. API surface, OpenAPI and SDKs

As planned, with these details:

- `debdbf81` regenerates from OpenAPI 0.9.54 (`info.version` 0.9.54.7833). The paths go from 101 to 104 and the schemas from 128 to 134.
- **New** operations `ExchangeUserToken`, `GetAuthorizationServerMetadata` and `GetJsonWebKeySet` (tag Access), and the schemas `UserTokenRequest`, `UserTokenResponse`, `AuthorizationServerMetadata`, `PublicJsonWebKeySet`, `PublicJsonWebKey` and `TokenExchangeConfiguration`.
- **Changed**: `AuthConfiguration.Issuer` and `.TokenExchange`, `AuditActor.Via`, and the descriptions of `accountId` and of the `403`s of key/value reads and machine routes. Servers and security schemes are unchanged.
- **C# (NSwag 14.6.3)**: `ApiSdk.g.cs` is regenerated.
- **TypeScript (Orval 8.41.0)**: the package is 0.9.54, and the 0.9.50 snapshot of `sdk.typescript` is replaced.
- Both READMEs gain a note on the user token exchange.
- `X-2S-Secrets-Withheld` is still not declared in the document.

### 9. Security notes

As planned, except the next signing key:

- **Stolen tokens.** The exchange needs the actor proof and a subject token issued to that actor, and the API refuses such a subject token as its own bearer token. Tokens are short-lived, and the replay limit applies.
- **No leaks.** Clients get fixed texts, the logs get reasons without tokens, and key resolution keeps the SSRF filter.
- **Who adds trust.** Actors and additional issuers come only from configuration. The deployment's own issuer cannot become a project's trusted issuer.
- **Restrictions** run in every build, `DEV_AUTH` included.
- **Signing keys.** The JWKS publishes the current key only. Rotation is still the emergency procedure (see Left for later).
- **Added by the reviews**: actors that share an audience must have the same policy, foreign workload audiences are refused, and the Entra tenant is pinned for domain names and `organizations`.

### 10. Tests

As planned, with these details:

- **Spec section 10's "exchange per provider"** is split as decision 4 implies:
  - the provider rules in `Access.AzureAd/test`, `Access.AuthKit/test` and `Access.Keycloak/test`;
  - the exchange itself in `Access.Federation/test`, with a fake subject validator;
  - the composition in `Api.Functions/test/UserTokenExchangeProviderTests` (whole-branch review), which runs the real provider modules in `Program.cs` order with only the key source stubbed.
- **`Access.Federation/test`**: `ActorAuthenticatorTests`, `UserTokenExchangeServiceTests`, `StorytellerUserTokenTests`, `StorytellerUserTokenValidationTests`, `TokenSigningKeyProviderJwksTests`, `ReplayRecordTests`, `UserTokenExchangeRegistrationTests`.
- **`Api.Functions/test`**:
  - `TokenExchangeHttpTests` (form, size, every refusal, an issued token checked against the JWKS);
  - `AuthorizationServerHttpTests`, `StorytellerUserTokenAuthenticationTests`, `KeycloakBearerAuthenticationTests`;
  - `NoSecretReveal` cases in `SecretRightsTests`, `KeyValuesHttpTests`, `ConfigurationConditionalReadTests`, `MachineHttpTests` and `FederationHttpTests`, in Debug and Release;
  - the route template and anonymity checks in `RouteTableTests` and `OpenApiExportTests`.
- **`Mcp/test`**: per-provider `BoundaryTests`, `McpTokenRulesTests` and `OptionsTests`, plus `TokenExchangeProviderTests`, `ActorKeyEndpointTests`, `DependencyTests`, `SecretToolsRegistrationTests`, `TokenPathTests` (the real exchange provider in the host) and `StartupNoticeTests`.
- **`Backend.CosmosDb/test`**: `AuditActorSerializationTests`, and four emulator `Via` round-trips in `AuditStampTests` and `KeyValueAuditAndUsageTests`.

### 11. Documentation

As planned, with these details:

- **`authentication.md`**:
  - Keycloak settings, its token checks and claims, and registering a Keycloak user;
  - account ids per provider, and "Scopes for users";
  - "User token exchange", with actors and their proof, the endpoint, subject tokens, the user token, restrictions, configuration, discovery and security;
  - "Not available yet" is updated, and the `Auth:DelegatedClients` row and paragraph are gone.
- **`mcp.md`**: setup per provider, "Registering the actor in the API", the actor's credentials, the settings, local development, "Identity" (replaced) and "When the exchange fails".
- **`access.md`**: "Actors", `ActorRestricted`, and invitations through an actor. **`audit-stamps.md`**: `Via`.
- **Smaller changes**: the `actor-restricted` header in `key-values.md`, `inheritance.md` and `docs/42for.net/platform/using-the-platform.md`, and the 0.9.54 document in `export-import.md`.
- **`docs/Platform/Aspire.Host/local-development.md`**: `BaseAccount:Id` for `db-creator`, and a section on the MCP server.
- The 424-84 spec and review, and `CLAUDE.md`, are not changed.

### 12. Phases and commits

As planned, with two differences. The commits are on `feat/424-106` instead of `feat/424-84` (see Overview). `d8f99902`, the fixes from the whole-branch review, comes between Phase 4 and the documentation. Every commit body names `Refs: 424-106`.

### 13. Verification

Partly done:

- The test projects of section 13 ran, the `Backend.CosmosDb` emulator suite included.
- The build was checked per project and with `dotnet build src/Platform`, which stops at one pre-existing error.
- The end-to-end sign-in with Entra ID and AuthKit against staging was not done.

See Test results.

### Test results

The last run is on `d8f99902`. In brackets: the count before this branch.

| Project | Result |
| --- | --- |
| `Backend.Core/test`, Debug and Release | 1031 passed (867) |
| `Access.AzureAd/test` | 140 passed (111) |
| `Access.AuthKit/test` | 180 passed (159) |
| `Access.Keycloak/test` | 136 passed (27) |
| `Access.Federation/test`, Debug and Release | 456 passed (268) |
| `Api.Functions/test`, Debug (`DEV_AUTH`) | 2453 passed (2256) |
| `Api.Functions/test`, Release | 2453 passed (2256) |
| `Mcp/test`, Debug and Release | 724 passed (486) |
| `Cli/test` | 793 passed |
| `Backend.CosmosDb/test`, filter `AuditActorSerializationTests` | 6 passed |
| `Backend.CosmosDb/test`, the emulator suite | 508 passed (507 on `998e9c31`; the Lowkey Vault test then passed on `11a9d8f1` with Docker) |
| `Mcp/testint` | builds; skipped without the `MCP_TEST_*` variables |
| `Aspire.Host/src` build | 0 errors |
| TypeScript SDK (`sdk.typescript/src`) | typecheck and lint OK, 130 passed and 1 skipped (the smoke test needs a running API), build OK, regeneration reproducible |
| `ui.admin` with the packed SDK | lint OK, 1427 passed, build OK |
| `dotnet build src/Platform` | 1 error, in `Supervisor/Abstraction/test` (net6.0, pre-existing, see Left for later) |

The emulator suite ran later, on `998e9c31`, against the Windows Cosmos DB emulator, the four new `Via` round-trips included. In its first run, `KeyValueEndToEndTests.Secret_WrittenWithTheVaultKey_RoundTripsThroughCosmos` failed because Docker was not running for its Lowkey Vault container; it passed in a rerun with Docker on `11a9d8f1`, which changes only `CLAUDE.md`.

The TypeScript SDK and `ui.admin` ran with the SDK commit; nothing in them changed afterwards. The Mcp projects build without compiler or StyleCop warnings. In the other touched projects, the warnings are on lines this branch did not change.

Not run:

- **The end-to-end sign-in with Entra ID and AuthKit against staging**, which the issue's "Done when" lists. There were no staging credentials.
- **The real Functions host.** `func start` fails on this machine in the Storage Queues extension (`System.Memory.Data` 8.0.0.1), which is unrelated to this branch. The `/api/.well-known` routes are checked only by the route-template tests and the rendered OpenAPI document.

### Reviews

- **The plan.** The CodeRabbit coding plan on the Linear issue was a second opinion during planning, not the source of truth. Ideas taken from it include the per-provider `IUserSubjectTokenValidator` and the marker contract for the one issuer that may return users. The Implementation Plan records the resulting decisions.
- **Each phase** was implemented, then reviewed by three independent reviewers (spec conformance, security, tests and conventions). A skeptic verified every finding, and the confirmed ones were fixed before the phase's commit:
  - **Phase 1, 8 findings**: DbCreator seeded the owner under the pairwise Entra `sub`; an empty `AllowedClientIds` let any Keycloak client in; refused person tokens fell through to the Keycloak machine validator; `Mcp/testint` still compared `sub`.
  - **Phase 2, 11 findings**:
    - a domain-name `Auth:TenantId` accepted any tenant, and `organizations` admitted personal accounts;
    - client assertions naming the published token endpoint were refused without `PublicBaseUrl`;
    - `invalid_grant` had three texts;
    - a user token could become a machine token through project federation;
    - a trailing slash bypassed the own-audience guard;
    - metadata with Host-derived URLs was publicly cacheable.
  - **Phase 3, 8 findings**:
    - restrictions came only from the token, so tightening an actor did not reach issued tokens;
    - invitation matching trusted the account's user name through an actor;
    - **a first `NoSecretReveal` bypass**: creating or resetting a machine with a Secrets scope, or replacing its federation rules, gave out the secret reads the actor may not do;
    - test gaps.
  - **Phase 4, 10 findings**:
    - the inbound clock skew was the default 5 minutes;
    - answers with less than a minute left were never cached;
    - the `ActorRestricted` text always spoke of reads;
    - the client assertion `aud` could not be set;
    - the guidance pointed the managed identity at `api://AzureADTokenExchange`;
    - the anonymous JWKS read and decrypted the PKCS#12 file on every request;
    - four test gaps.
- **The whole branch** was reviewed from four lenses: end to end, adversarial security, spec completeness and regressions. Two skeptics confirmed each finding. Of the 8 findings, all were fixed in `d8f99902`:
  - **a second `NoSecretReveal` bypass**: replacing an existing trusted issuer let a restricted token take over a Federated Secrets machine;
  - the spec's workload identity sample, an Entra v2 issuer with an App ID URI, never matches a real token;
  - the audiences the MCP server accepts were not tied to the actor's `SubjectAudiences`;
  - the single-tenant MCP server met a multi-tenant API, and guests were not explained;
  - the `404` branch of `get_my_account` cannot be reached;
  - the local Aspire setup had no place for the actor;
  - two actors could share a `SubjectAudiences` entry with different policies;
  - no test ran the real subject token validators through the exchange.

  The fix also refuses foreign workload audiences in the API, a follow-up from Phase 4.
- **The documentation** was reviewed for accuracy against the code, consistency and style before its commit. 27 findings were confirmed, and all are fixed in `f6b38cb3`.

### Deviations from the spec

1. **The account id is applied by the provider modules**, which write it as `sub` and keep the pairwise Entra value as `entra_sub`. Section 1 put `UserIdentity.GetAccountId` behind `GetIdentityUniqueId` (Implementation Plan decision 1).
2. **Subject tokens are validated by the provider modules** through `IUserSubjectTokenValidator`, with their own key sources, not by `FederatedIssuerKeyResolver`. That resolver is keyed by project, and the providers do not reference `Access.Federation` (decision 4). `AdditionalIssuers` is accepted for AuthKit only.
3. **DbCreator seeds its owner from `baseAccount:id`.** The spec missed this third place that writes an account; without the setting, a local database under Entra seeds a pairwise `sub` that matches no account.
4. **Keycloak `AllowedClientIds` is required.** The spec made it optional, which let any client of the realm in. `ClientId` must be one of the allowed clients. Keycloak machine tokens must be service-account tokens.
5. **Keycloak details the spec did not fix**: client roles are mapped as `{client}:{role}`, and invitations match the `email` claim only.
6. **`ClientAssertion` actors require `MachineAuth:Federation:PublicBaseUrl`**, checked at start. The token endpoint accepted as `aud` is built from it, never from the request's `Host`. `client_id` is required with every proof, although RFC 7523 makes it optional for assertions.
7. **`invalid_grant` has two fixed texts.** The replay limit answers with the generic one, and the account is looked up before counting. So a sign-in without an account always gets the registration hint and is not counted.
8. **The replay limit counts per actor and per subject token**, default 10 a minute (`MaxExchangesPerSubjectTokenPerMinute`), not per actor alone. A subject token with less than 5 s left is refused.
9. **A disabled exchange answers `unsupported_grant_type`**, and missing keys answer `503 temporarily_unavailable`; section 3 named only `400` errors (decision 7).
10. **`SubjectAudiences` rules**:
    - an entry may not name the API's own audience;
    - actors that share an entry must have the same `Restrictions`, `AllowSecrets` and `Scopes`;
    - under Entra, the actor lists both `Mcp:Authentication:Audience` and the MCP client id, which the spec's sample did not show.
11. **Workload identity samples and audiences.** The spec's sample (an Entra v2 issuer with an App ID URI) never matches a managed identity token, and that pair now fails at start. The API and the MCP server both refuse audiences of other services, such as `api://AzureADTokenExchange`, Azure resources, AWS and Kubernetes.
12. **The JWKS publishes one key, with no next key.** There is no key rotation (decision 12). The routes are `/api/.well-known/…`, and the metadata is served also while the exchange is off.
13. **The person's name is read from the account on every call** of a Storyteller user token, a single point read, and a token whose account is gone is `401` (decision 9).
14. **Restrictions are enforced as the union** of the token's and the actor's current ones. Tightening an actor applies to issued tokens at once, and loosening it waits for new tokens. The `restrictions` claim is required.
15. **`NoSecretReveal` covers more than the four routes of decision 11.** It also refuses creating or resetting a machine with a Secrets scope, replacing that machine's federation rules, and replacing an existing trusted issuer. A reveal is refused even for a public pair.
16. **Calls through an actor are never email-verified**, so invitations are answered with the provider's own sign-in. The spec did not cover invitations.
17. **The refusal of tokens issued to an actor** covers every result not from an owner, machine tokens included. It holds whether or not the exchange is enabled.
18. **The MCP server caches an answer that expires within a minute** until 5 s before expiry. Section 7 said until 60 s before, which would have cost one exchange per call in a token's last minute.
19. **MCP inbound**:
    - 30 s clock skew and 10 s of required remaining life;
    - under Entra, a set `Mcp:Authentication:Issuer` must be the tenant's v2 issuer;
    - under AuthKit and Keycloak, `TenantId` and `ClientId` are refused;
    - the RFC 8414 path stays mapped and answers `404` for AuthKit and Keycloak.
20. **The MCP server publishes its own `/.well-known/jwks.json`** for `ClientAssertion`: always mapped, `404` without a key, and `503` with a 30 s back-off. It also has the optional `Mcp:Actor:ClientAssertionKey:Audience`.
21. **Mcp reads the token file and the certificate through `IFileSystem`** (TestableIO packages added), as the repository convention asks. `Azure.Identity` brings MSAL in transitively, so "no MSAL" means no direct reference and no use (decision 13).
22. **`McpStartupNotice`** logs the `SubjectAudiences` the actor must list and, under Entra, the one tenant served. The spec did not plan it.

### Left for later

Recorded during planning, unchanged:

- **The API's Entra bearer validator** accepts any Entra issuer and does not pin `RS256`.
- **`sform` refuses `Provider: Keycloak`** with "Unknown sign-in provider". Keycloak sign-in in `sform` is out of scope.
- **An equality oracle under `NoSecretReveal`**: an actor with write rights can still test a guessed value through unchanged writes and import dry runs.
- **`Api.Web` builds its own audit actor** from `sub` (`AnnotationsController.GetAuditActor`), without `Via`.

Found on the way:

- **Clients do not show `Via`.** `sform` (`AuditConsoleExtensions.FormatActor`) and the admin UI (`AuditActorLink.vue`) ignore it, and `sform` has no special output for `ActorRestricted` as it has for `MtlsRequired`.
- **`X-2S-Secrets-Withheld` is not declared in OpenAPI**, for either value.
- **The next SDK regeneration** picks up the changed `403` description of `SetFederatedIssuer` from `d8f99902`. It is a description only, so the shape of the API is unchanged.
- **Signing-key rotation**, and a next key in the JWKS.
- **Real AuthKit and Keycloak token shapes, to confirm in staging**: whether AuthKit MCP tokens carry the resource URL as `aud` and the scope in `scope` or `permissions`, the issuer of the AuthKit domain's OpenID metadata, and the audience mapper a Keycloak realm needs.
- **Key rotation at AuthKit and Keycloak.** Their validators request a key refresh on an unknown `kid` but do not retry, so the first token after a rotation is refused. Entra retries once.
- **Keycloak has no profile resolver**, so registration and invitation acceptance need `preferred_username`, `name` and `email` in the token.
- **The 64 KiB body limit** is about 300 bytes short when `MaxSubjectTokenBytes` is raised to 32 KiB and both tokens use it all.
- **`StorytellerSignInRefusedException.NoAccountMessage` copies the API's text.** Only `Mcp/testint` with `MCP_TEST_EXPECT_NO_ACCOUNT=true` notices drift.
- **The stateless MCP server cannot send `notifications/tools/list_changed`.** A change of the actor's restrictions shows on the client's next `tools/list`; a call in between is still refused.
- **The `CLAUDE.md` line on the Mcp project** still says "Entra ID on-behalf-of". It was not edited, because it is the project instruction file.
- **`Supervisor/Abstraction/test` targets net6.0** and fails `dotnet build src/Platform`. This is pre-existing and unrelated.

Not done in this change:

- The end-to-end sign-in with Entra ID and AuthKit against staging, and `Mcp/testint` against a real environment.
- A run on the real Functions host.
- A real Azure managed identity, and the API fetching the MCP server's JWKS through its public-address filter.

### Merge notes

- **Merge order**: this pull request into `feat/424-84`, then PR #131 into `main`.
- **`Directory.Packages.props`** loses `Microsoft.Identity.Web.TokenCache` and `System.Security.Cryptography.Pkcs`, which nothing else referenced directly. `Mcp.csproj` references `Azure.Identity` and `TestableIO.System.IO.Abstractions.Wrappers`.
- **MCP settings**: `Mcp:EntraId:*` and `Storyteller:Scopes` are replaced by `Mcp:Authentication:*` and `Mcp:Actor:*`. `Storyteller` keeps only `BaseUrl`.
- **API settings**:
  - `Auth:DelegatedClients` is removed; a leftover key is ignored.
  - The MCP server works only with `Auth:TokenExchange:Enabled` and an actor for it.
  - An https `MachineAuth:Federation:Issuer` is needed for RFC 8414 clients, and `PublicBaseUrl` for `ClientAssertion` actors.
  - `Auth:Keycloak:AllowedClientIds` is required under Keycloak.
  - An actor with an Entra v2 issuer and an App ID URI audience, or with a foreign workload audience, now fails at start. Neither could ever have exchanged a token.
- **Accounts under Entra** are keyed by `{tid}:{oid}`. Existing local databases need DbCreator's `baseAccount:id` and a re-run, and accounts registered under the pairwise `sub` do not resolve. No deployment has Entra accounts yet.
- **To verify after deployment**:
  - `GET /api/.well-known/oauth-authorization-server` and `GET /api/.well-known/jwks.json` on the real Functions host;
  - an Entra ID and an AuthKit sign-in through the MCP server, with an account and without one (the fixed text), which `Mcp/testint` can run per environment.
