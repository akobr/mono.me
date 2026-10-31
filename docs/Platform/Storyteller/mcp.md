# MCP Server

The MCP server lets an AI assistant work with the 2S platform as the signed-in person. It is an HTTP server over the [Model Context Protocol](https://modelcontextprotocol.io/) (Streamable HTTP, stateless) in front of the Storyteller API. People sign in to it with the identity provider of the deployment: Entra ID, AuthKit or Keycloak. Before a tool runs, the server exchanges the person's token at the API for a short-lived Storyteller user token that names the person and, as its actor, the server. Every tool call is a call of the API with that token, so the person's [roles](access.md#roles) decide what succeeds, the [restrictions of the actor](access.md#actors) apply on top, and the audit actor of every change is the person's account with the server as `Via` (see [Audit Stamps](audit-stamps.md#the-actor)).

The code is in:

- `src/Platform/Storyteller/Mcp/src`: the host (`Program.cs`, `Hosting/`), sign-in and the token exchange client (`Authentication/`), the tools (`Tools/`) and the configuration analysis (`Understanding/`);
- `src/Platform/Storyteller/Mcp/test`: unit and in-memory host tests;
- `src/Platform/Storyteller/Mcp/testint`: the token exchange end to end against a configured environment;
- `src/Platform/Storyteller/Api.Functions/src/V1/TokenExchangeHttp.cs` and `src/Platform/Storyteller/Access.Federation/src` (`UserTokenExchangeService`, `ActorAuthenticator`): the API side, the user token exchange.

The design and its decisions are in [the spec](specs/2026-10-24%20MCP%20Server%20for%20the%20Storyteller%20API.md) and [its review](specs/reviews/2026-10-24%20MCP%20Server%20for%20the%20Storyteller%20API.md). The token exchange, which replaced the Entra ID on-behalf-of flow of the first version, is in [its own spec](specs/2026-10-26%20Storyteller%20User%20Token%20Exchange%20for%20the%20MCP%20Server.md) and [review](specs/reviews/2026-10-26%20Storyteller%20User%20Token%20Exchange%20for%20the%20MCP%20Server.md).

## How a call reaches the API

1. An MCP client signs the person in with the deployment's identity provider and calls `POST /mcp` with a token for the **MCP server**: its `aud` is the server's audience (`Mcp:Authentication:Audience`, by default `Mcp:ResourceUrl`).
2. The server checks the token: an RS256 signature, the pinned issuer, the audience, the lifetime, the provider's rules for a person's token, an accepted client app and the scope (see [Setting up the identity provider](#setting-up-the-identity-provider)).
3. Before the tool runs, the server exchanges the token at the API (RFC 8693): `POST {Storyteller:BaseUrl}/v1/access/token` with the token as `subject_token`, `Mcp:Actor:Id` as `client_id` and the proof of its actor: an `actor_token`, a `client_assertion` or, in Development, a `client_secret`. The request has no `Authorization` header.
4. The API checks the proof against the actor of that id in `Auth:TokenExchange:Actors`, validates the subject token with the rules of its own provider, requires its `aud` to be one of the actor's `SubjectAudiences`, and finds the person's [account](#identity). It answers with a Storyteller user token: `sub` is the account id, `act.sub` the actor id, `scope` the actor's `Scopes` (`User.Impersonation` by default), and `restrictions` those of the actor (`NoSecretReveal` by default). The token lives at most `Auth:TokenExchange:TokenLifetimeMinutes` (10 by default) and never past the subject token's `exp`. The exchange never creates accounts.
5. The server calls the API with the Storyteller token. The API treats it as the person: the person's role decides, the actor's restrictions apply on top, and the audit names the person with the actor as `Via`.

The MCP token itself never reaches the API's routes, and the API refuses it there with `401`: a token whose `aud` is in an actor's `SubjectAudiences` is for the exchange only. `Mcp:Provider` must be the API's `Auth:Provider`; nothing compares the two, and with different providers the API refuses every exchange. The API side (the settings, the token and the restrictions) is described in [User Authentication](authentication.md#user-token-exchange).

## Setting up the identity provider

### Entra ID

**The MCP app** is the app registration that stands for the server. People get tokens for it; the server only validates them, so it needs no credential and no redirect URI:

- single tenant;
- **Expose an API**: the App ID URI is the resource URL of the server, for example `https://mcp.example.com/mcp`, on a verified domain of the tenant, without a trailing slash. Entra accepts the RFC 8707 `resource` parameter that MCP clients send only when it is the App ID URI of the requested scope. Add a delegated scope `Mcp.Access` (`Mcp:Authentication:Scope`);
- **Expose an API → Authorized client applications** (or `knownClientApplications` in the manifest): the MCP client apps, so a person consents once;
- the optional claim `idtyp` on its access tokens. It makes app-only tokens recognizable beyond the missing `scp`; the server and the API refuse `idtyp` `app`.

Set `Mcp:Authentication:TenantId` to the tenant and `Mcp:Authentication:ClientId` to the MCP app. The server derives the tenant's issuers, v2 `https://login.microsoftonline.com/{tenant}/v2.0` and v1 `https://sts.windows.net/{tenant}/`, and accepts two audiences: the App ID URI (`Mcp:Authentication:Audience`, by default `Mcp:ResourceUrl`), which v1 tokens carry, and the client id, which v2 tokens carry. A token must name the tenant in `tid` and a client app of `Mcp:Authentication:AllowedClientIds` in `azp` (v1: `appid`), and must be delegated: a non-empty `scp` that holds the scope.

**The MCP clients** sign in with client ids registered in the tenant, with their redirect URIs and the delegated permission `Mcp.Access` of the MCP app. Entra has no dynamic client registration, so each client is configured with its client id, and the id is listed in `Mcp:Authentication:AllowedClientIds`.

**One tenant.** An MCP deployment serves one tenant; `common` and `organizations` are refused. The API's `Auth:TenantId` must name the same tenant, or be `common` or `organizations` when the API serves several; people of another tenant then need an MCP deployment of their own tenant. A B2B guest signs in to the server's tenant as its guest object there, so its account id is `{the server's tenant}:{guest oid}`, not the account it registered through its home tenant, and the exchange refuses it as a sign-in without an account. Accounts are not linked. The server logs its tenant and this rule once at start.

### AuthKit

- `Mcp:Authentication:Issuer` is the AuthKit domain, `https://<subdomain>.authkit.app` or a custom domain, without a trailing slash. MCP clients find it in the server's resource metadata and sign in there: AuthKit publishes its own authorization server metadata, with PKCE and dynamic client registration. Turn dynamic client registration on in the WorkOS dashboard.
- **Scope.** The scope is a WorkOS permission: create one, for example with the slug `mcp:access`, and set `Mcp:Authentication:Scope` to its slug. The server checks only that the token carries it, in `permissions`, `scope` or `scp`. WorkOS assigns no scopes per client to dynamically registered clients (nor to clients registered by a client ID metadata document): their tokens get the OpenID Connect scopes and, beyond them, the environment-wide default scopes of the environment, which WorkOS support sets up on request ([access token claims](https://workos.com/docs/authkit/connect/token-claims)). So the slug must be in that default set, or every MCP sign-in is refused for the missing scope. Such tokens carry no `permissions` claim, so the server finds the slug in `scope`.
- **Who may use the server.** The scope is the same for everyone who signs in through such a client and does not follow WorkOS roles: giving the permission to roles neither puts it into these tokens nor refuses anyone. Treat the scope as the gate of the server, not as the authorization of people. What a person may do is decided by the API: the exchange refuses a sign-in without a Storyteller account, and the person's roles on organizations and projects decide every call (see [Identity](#identity)).
- **Audience.** Add the MCP resource URL (`Mcp:ResourceUrl`, which the server publishes in its resource metadata) as a resource indicator of the environment in the WorkOS dashboard; tokens then carry the requested `resource` as `aud`. A client that omits `resource` gets the audience of the default resource indicator, and without a default the environment's client id ([MCP](https://workos.com/docs/authkit/mcp)). So make the MCP resource URL the default resource indicator, or the server refuses those tokens for their `aud`; when `Mcp:Authentication:Audience` is set, it must be that same URL.
- The server reads the domain's keys through `{Issuer}/.well-known/openid-configuration` and accepts a token whose `iss` is the domain, whose `aud` is `Mcp:Authentication:Audience` (by default `Mcp:ResourceUrl`) and whose `sub` is a WorkOS user (`user_…`); M2M tokens are refused. Check one token of a test sign-in for these three claims and the scope before a rollout.
- `Mcp:Authentication:AllowedClientIds` is optional: empty accepts any client, which dynamic registration needs; a list is compared with `client_id`, else `azp`.

When the AuthKit domain is not the API's `Auth:AuthKit:Issuer` (by default `https://api.workos.com/`), the API lists it in `Auth:TokenExchange:AdditionalIssuers` (see [the sample](#registering-the-actor-in-the-api)). The API accepts its tokens for the exchange only, never as bearer tokens. When `Auth:AuthKit:Issuer` already is the AuthKit domain, the API exchanges its tokens without that entry, and listing it there stops the API at start.

### Keycloak

- `Mcp:Authentication:Issuer` is the realm, `https://<host>/realms/<realm>`: the same realm as the API's `Auth:Keycloak:Issuer`, because the API exchanges tokens of its own realm only.
- **Audience mapper.** Keycloak puts the server's URL into `aud` only through an Audience mapper: add one whose included custom audience is `Mcp:Authentication:Audience` (by default `Mcp:ResourceUrl`), added to access tokens.
- **Client scope.** Create a client scope named like `Mcp:Authentication:Scope` (by default `Mcp.Access`), included in the token scope, and put the mapper into it. Make it an optional client scope of the realm, so only the tokens of clients that ask for it carry the server's audience. The server finds the scope in `scope` or `scp`.
- **Clients.** MCP clients register themselves through the realm's dynamic client registration, within the realm's client registration policies; or create a client for each. `Mcp:Authentication:AllowedClientIds` is optional: empty accepts any client, a list is compared with `azp`.
- The server reads the realm's keys through `{Issuer}/.well-known/openid-configuration` (over plain HTTP in Development only) and accepts a person's access token: a `sub`, no `client_id` or `clientId` claim and no `preferred_username` that starts with `service-account-`, so service accounts are refused, and `typ` `Bearer`, so ID tokens and tokens without `typ` are refused.

## Registering the actor in the API

The API trusts the server only by its configuration: `Auth:TokenExchange:Enabled` set to `true`, and an entry of `Auth:TokenExchange:Actors` whose `Id` is `Mcp:Actor:Id`. For the MCP server:

- `SubjectAudiences` lists every audience the server accepts: `Mcp:Authentication:Audience` (by default `Mcp:ResourceUrl`), and under Entra ID also `Mcp:Authentication:ClientId`, which v2 tokens carry. A missing one makes the API refuse those sign-ins with `invalid_grant`. The server logs the list once at start, under Entra ID for example `The API's Auth:TokenExchange:Actors entry 'mcp' must list these SubjectAudiences: <MCP app client id>, https://mcp.example.com/mcp.`
- `AllowedClientIds` is optional and limits the MCP client apps (`azp`, `appid` or `client_id` of the person's token). Empty, the API warns once at start that it accepts any client's token for the actor's `SubjectAudiences`.
- `Scopes` defaults to `User.Impersonation`, which it must contain.
- Every actor gets `NoSecretReveal` unless `AllowSecrets` is `true` (see [Tools that return secrets](#tools-that-return-secrets)). `Restrictions` adds more restrictions on top of it (`NoSecretReveal` is the only one so far), and an empty list does not remove it. The API refuses to start when `AllowSecrets` is `true` and `Restrictions` names `NoSecretReveal`. See [Configuration of the exchange](authentication.md#configuration-of-the-exchange).
- `Authentication` names one credential, the counterpart of the server's `Mcp:Actor` (see [Credentials of the actor](#credentials-of-the-actor)).
- Actors that share an entry of `SubjectAudiences`, such as two instances of the server with their own managed identities, must end up with the same restrictions and scopes (as `Restrictions`, `AllowSecrets` and `Scopes` give them), or the API does not start. The replay limit (`Auth:TokenExchange:MaxExchangesPerSubjectTokenPerMinute`, 10 by default) counts per actor and subject token.

Under Entra ID, with a managed identity of the server (`Mcp:Actor:WorkloadIdentity` with `Kind` `ManagedIdentity` and `Audience` `api://<exchange app id>`):

```json
"Auth": {
  "Provider": "EntraId",
  "TenantId": "<tenant id>",
  "TokenExchange": {
    "Enabled": true,
    "Actors": [
      {
        "Id": "mcp",
        "Authentication": {
          "WorkloadIdentity": {
            "Issuer": "https://sts.windows.net/<tenant id>/",
            "Subject": "<object id of the managed identity>",
            "Audience": "api://<exchange app id>"
          }
        },
        "SubjectAudiences": [ "https://mcp.example.com/mcp", "<MCP app client id>" ],
        "AllowedClientIds": [ "<MCP client app id>" ]
      }
    ]
  }
}
```

Under AuthKit, with a client assertion key (`Mcp:Actor:ClientAssertionKey`). The AuthKit domain `https://auth.example.com` is an additional issuer here, because the API's `Auth:AuthKit:Issuer` is another one, and a `ClientAssertion` actor needs `MachineAuth:Federation:PublicBaseUrl`:

```json
"Auth": {
  "Provider": "AuthKit",
  "TokenExchange": {
    "Enabled": true,
    "AdditionalIssuers": [
      { "Issuer": "https://auth.example.com", "JwksUri": "https://auth.example.com/oauth2/jwks" }
    ],
    "Actors": [
      {
        "Id": "mcp",
        "Authentication": {
          "ClientAssertion": { "JwksUri": "https://mcp.example.com/.well-known/jwks.json" }
        },
        "SubjectAudiences": [ "https://mcp.example.com/mcp" ]
      }
    ]
  }
},
"MachineAuth": {
  "Federation": { "PublicBaseUrl": "https://api.example.com/api" }
}
```

Under Keycloak, with a projected Kubernetes service account token (`Mcp:Actor:WorkloadIdentity` with `Kind` `TokenFile` and `TokenFilePath` `/var/run/secrets/storyteller/token`):

```json
"Auth": {
  "Provider": "Keycloak",
  "TokenExchange": {
    "Enabled": true,
    "Actors": [
      {
        "Id": "mcp",
        "Authentication": {
          "WorkloadIdentity": {
            "Issuer": "<the cluster's service account issuer>",
            "Subject": "system:serviceaccount:mcp:mcp-server",
            "Audience": "https://api.example.com/api/v1/access/token"
          }
        },
        "SubjectAudiences": [ "https://mcp.example.com/mcp" ]
      }
    ]
  }
}
```

Every credential works with every provider; the samples show one each.

### Credentials of the actor

| `Mcp:Actor` | `Authentication` of the API's actor | Sent as |
| --- | --- | --- |
| `WorkloadIdentity` with `Kind` `ManagedIdentity` | `WorkloadIdentity`: the token's issuer, the identity's object id as `Subject`, and the `Audience` | `actor_token` |
| `WorkloadIdentity` with `Kind` `TokenFile` | `WorkloadIdentity`: the issuer, subject and audience of the projected token | `actor_token` |
| `ClientAssertionKey` | `ClientAssertion` with `JwksUri` `https://<server>/.well-known/jwks.json` | `client_assertion` (RFC 7523, `private_key_jwt`) |
| `ClientSecret` (Development only) | `ClientSecretHash`, the hex SHA-256 of the secret (Development only) | `client_secret` |

- **Managed identity.** The server asks Azure.Identity for a token for `{Audience}/.default`, as the user-assigned identity of `Mcp:Actor:WorkloadIdentity:ClientId`, or as the system-assigned one when it is empty. The audience is an app registration that stands for the Storyteller exchange and nothing else, as for an [Azure managed identity of a machine](authentication.md#azure-managed-identity), and equals the API actor's `WorkloadIdentity:Audience`. The registration's `requestedAccessTokenVersion` decides the pair: v1 (the default) has the issuer `https://sts.windows.net/{tid}/` with the App ID URI as `aud`, v2 has `https://login.microsoftonline.com/{tid}/v2.0` with the client id. `api://AzureADTokenExchange` and the audiences of Azure resources are refused at start on both sides, because a token for them is a credential there. The API also refuses the v2 issuer with an audience that is not a GUID.
- **Token file.** A token that the platform projects and rotates, such as a Kubernetes service account token with an audience of its own; not `AZURE_FEDERATED_TOKEN_FILE`, whose audience is Entra's. The server reads the file for every exchange. The API reads the keys of a workload identity's issuer through its OpenID discovery, or from `WorkloadIdentity:JwksUri` when that is set.
- **Client assertion key.** A PKCS#12 file with an RSA key of at least 2,048 bits. Every exchange carries a new assertion: `iss` and `sub` are the actor id, `aud` is `{Storyteller:BaseUrl}/v1/access/token`, with a new `jti`, a lifetime of 55 seconds (the API accepts at most 60), RS256 and the RFC 7638 thumbprint as `kid`. The API compares `aud` exactly with `{MachineAuth:Federation:PublicBaseUrl}/v1/access/token` and with `MachineAuth:Federation:Issuer`, and a `ClientAssertion` actor needs `PublicBaseUrl`. So by default `Storyteller:BaseUrl` must be exactly `PublicBaseUrl`; when the server reaches the API by another URL than the published one, set `Mcp:Actor:ClientAssertionKey:Audience` to one of the two.
- **The key set.** The server publishes the public key at `GET /.well-known/jwks.json`, anonymous, with `Cache-Control: public, max-age=300`. The path answers `404` without a client assertion key, and `503` with `Retry-After: 30` while the file cannot be read; the server reads it again 30 seconds later at the earliest. The API fetches the key set through the public-address filter of federation, so `JwksUri` is an HTTPS URL on a public address. A replaced key file takes effect after a restart.
- **Client secret.** For local development only: the server refuses `Mcp:Actor:ClientSecret` outside Development, and the API refuses `ClientSecretHash`.

## Settings of the server

| Key | Required | Notes |
| --- | --- | --- |
| `Mcp:Provider` | no | `EntraId` (default), `AuthKit` or `Keycloak`: the provider people sign in with, the API's `Auth:Provider`. |
| `Mcp:ResourceUrl` | yes | The public URL of the MCP endpoint, for example `https://mcp.example.com/mcp`, no trailing slash. HTTPS outside Development. Every metadata URL is built from it. |
| `Mcp:Authentication:Issuer` | AuthKit, Keycloak | The AuthKit domain or the Keycloak realm (`https://<host>/realms/<realm>`), no trailing slash, HTTPS outside Development. Under Entra ID empty or the tenant's v2 issuer: the issuers come from the tenant. |
| `Mcp:Authentication:Audience` | no | The `aud` of inbound tokens, no trailing slash; default `Mcp:ResourceUrl`. Under Entra ID the App ID URI of the MCP app. |
| `Mcp:Authentication:Scope` | no | One scope name; default `Mcp.Access`. Entra ID clients request `{Audience}/{Scope}`; under AuthKit it is the slug of a permission. |
| `Mcp:Authentication:AllowedClientIds:*` | Entra ID | The MCP client apps whose tokens the server accepts: GUIDs under Entra ID (`azp`, v1 `appid`). Optional under AuthKit (`client_id`, else `azp`) and Keycloak (`azp`), where an empty list accepts any client. |
| `Mcp:Authentication:TenantId` | Entra ID | The tenant GUID; `common` and `organizations` are refused. Refused under AuthKit and Keycloak. |
| `Mcp:Authentication:ClientId` | Entra ID | The MCP app, also an accepted audience (v2 tokens). Refused under AuthKit and Keycloak. |
| `Mcp:Actor:Id` | yes | The `Id` of the server's actor in the API's `Auth:TokenExchange:Actors`, sent as `client_id`, for example `mcp`. |
| `Mcp:Actor:WorkloadIdentity:Kind` | one credential | `ManagedIdentity` or `TokenFile`. |
| `Mcp:Actor:WorkloadIdentity:Audience`, `Mcp:Actor:WorkloadIdentity:ClientId` | `ManagedIdentity` | The audience the token is requested for, without `/.default` (required); the client id of a user-assigned identity, empty for the system-assigned one. Refused with `TokenFile`. |
| `Mcp:Actor:WorkloadIdentity:TokenFilePath` | `TokenFile` | The token file, read for every exchange. Refused with `ManagedIdentity`. |
| `Mcp:Actor:ClientAssertionKey:CertificatePath`, `Mcp:Actor:ClientAssertionKey:CertificatePassword` | one credential | A PKCS#12 file with an RSA key of at least 2,048 bits. Keep the password in Key Vault or user secrets. |
| `Mcp:Actor:ClientAssertionKey:Audience` | no | The `aud` of the client assertions, an absolute URI; default `{Storyteller:BaseUrl}/v1/access/token`. |
| `Mcp:Actor:ClientSecret` | one credential | Development only. Keep it in user secrets. |
| `Mcp:SecretTools:Enabled` | no | `false` (default) leaves out the [tools that return secrets](#tools-that-return-secrets). |
| `Storyteller:BaseUrl` | yes | The API base URL with `/api`, no trailing slash. HTTPS outside Development. The exchange goes to `{Storyteller:BaseUrl}/v1/access/token`. |

Settings are validated at start; a missing or wrong value stops the host. `Mcp:Actor` takes exactly one credential, and a credential counts as set when any of its values is, so a half-filled block is reported rather than ignored. `Mcp:EntraId:*` and `Storyteller:Scopes` of the on-behalf-of version are no longer read and do not stop the host, so move them before an upgrade:

| Before (on-behalf-of) | Now |
| --- | --- |
| `Mcp:EntraId:TenantId`, `ClientId`, `Audience`, `Scope`, `AllowedClientIds:*` | `Mcp:Authentication:TenantId`, `ClientId`, `Audience`, `Scope`, `AllowedClientIds:*`, with `Mcp:Provider` `EntraId` |
| `Mcp:EntraId:ClientSecret`, `CertificatePath`, `CertificatePassword`, `UseManagedIdentity`, `ManagedIdentityClientId` (credentials of the MCP app at Entra ID) | Removed. The server proves itself to the API as an actor: `Mcp:Actor:Id` and one credential of `Mcp:Actor` (a workload identity, a client assertion key, or a client secret in Development), registered in the API's `Auth:TokenExchange:Actors` (see [Registering the actor in the API](#registering-the-actor-in-the-api)) |
| `Storyteller:Scopes:*` | Removed. The API grants the actor's `Scopes` (`User.Impersonation` by default); `Storyteller` keeps only `BaseUrl` |

## Local development

`dotnet run --project src/Platform/Aspire.Host/src` lists the `mcp` resource, started from the dashboard; Aspire gives it `Storyteller:BaseUrl`, the local API. In Development the server listens on `https://localhost:7180`, and `Mcp:ResourceUrl` is `https://localhost:7180/mcp`. With a client secret as the actor's credential:

1. Put the server's settings (see [Settings of the server](#settings-of-the-server)) into its user secrets: `Mcp:Provider`, `Mcp:Authentication:*`, `Mcp:Actor:Id` and `Mcp:Actor:ClientSecret`:

   ```bash
   cd src/Platform/Storyteller/Mcp/src
   dotnet user-secrets set "Mcp:Actor:Id" "mcp"
   dotnet user-secrets set "Mcp:Actor:ClientSecret" "<a random secret>"
   ```

2. Hash the secret: `printf '%s' '<the secret>' | sha256sum` gives the 64 hex digits.
3. Turn the exchange on in the `Values` of the API's `local.settings.json`, whose `DOTNET_ENVIRONMENT` is `Development`:

   ```json
   "Auth:TokenExchange:Enabled": "true",
   "Auth:TokenExchange:Actors:0:Id": "mcp",
   "Auth:TokenExchange:Actors:0:Authentication:ClientSecretHash": "<64 hex digits>",
   "Auth:TokenExchange:Actors:0:SubjectAudiences:0": "https://localhost:7180/mcp"
   ```

   Under Entra ID add the MCP app's client id as `Auth:TokenExchange:Actors:0:SubjectAudiences:1`. Under AuthKit, unless `Auth:AuthKit:Issuer` already is the AuthKit domain, add the domain as `Auth:TokenExchange:AdditionalIssuers:0:Issuer`, with `Auth:TokenExchange:AdditionalIssuers:0:JwksUri`.
4. Start the API and the server. The server's start log names the audiences to list.

With the exchange off, every tool answers that the API has the user token exchange turned off. Under Entra ID, on a machine where the App ID URI cannot be the local URL, set `Mcp:Authentication:Audience` to `api://<MCP app client id>`, list that in `SubjectAudiences` instead of the local URL, and use a client that does not send the `resource` parameter.

## Connecting a client

Clients discover everything from the server:

- an unauthenticated call of `/mcp` answers `401` with `WWW-Authenticate: Bearer resource_metadata="{origin}/.well-known/oauth-protected-resource/mcp"`; a refused token adds `error="invalid_token"`, so the client signs in again;
- that document (RFC 9728) names the resource, the scope and the authorization server. Under AuthKit and Keycloak these are `Mcp:Authentication:Scope` and the issuer, whose own metadata lists the PKCE methods and the endpoint of dynamic client registration. Under Entra ID the scope is `{Audience}/{Scope}`, for example `https://mcp.example.com/mcp/Mcp.Access`, and the authorization server is the server's own origin;
- under Entra ID only, `{origin}/.well-known/oauth-authorization-server` (RFC 8414) lists Entra's authorize and token endpoints of the tenant, `S256` as the PKCE method and `none` as the client authentication. Entra supports PKCE but its own metadata does not say so, and MCP clients refuse an authorization server that does not. Entra issues every token. Under AuthKit and Keycloak the path answers `404`;
- a token without the scope gets `403` with `error="insufficient_scope"` and the scope.

Under AuthKit and Keycloak a client registers itself; under Entra ID it needs a pre-registered client id (see [Entra ID](#entra-id)). The server is stateless: every request stands alone, so a read can simply be repeated. A write that was interrupted may still have completed on the server; check it with the matching read before repeating it.

## Identity

The exchange finds the person's account by the account id of the deployment's provider. It is the id the API gives the person's own sign-ins, so an account registered with `sform` or the admin UI is found:

| Provider | Account id |
| --- | --- |
| `EntraId` | `{tid}:{oid}` in lower case. `oid` is the same for a person in every app of the tenant, so a token for the MCP app and a token of `sform` give the same id. `tid` is the server's tenant (see [One tenant](#entra-id)). |
| `AuthKit` | `sub` (`user_…`), the WorkOS user id, the same in every client and issuer of the environment. |
| `Keycloak` | `sub`, the user id of the realm, the same for every client. |

The exchange never creates accounts. For a person without one, every tool call, `get_my_account` included, answers with the API's text: "No Storyteller account exists for this sign-in. Register once with sform or the admin UI." A refused exchange is not cached, so the first call after the registration works. When an account is removed while the server still holds a Storyteller token for the person, the API answers `401`, and the tool asks the person to sign in again or to register again. Under Keycloak, `sform` and the admin UI cannot sign in yet: a person registers with `POST v1/access/account` and a token issued for the API, not the token they sign in to this server with (see [Registering a Keycloak user](authentication.md#registering-a-keycloak-user)).

`Mcp/testint` runs the exchange end to end against an environment, with the server's own exchange client and SDK clients. Check it once per environment before a rollout:

```bash
# The API with the exchange on and the actor listed; a token of the person for the MCP server.
export MCP_TEST_STORYTELLER_BASE_URL=https://api.example.com/api MCP_TEST_ACTOR_ID=mcp
# One credential: MCP_TEST_ACTOR_CLIENT_SECRET (an API in Development), MCP_TEST_ACTOR_TOKEN_FILE,
# or MCP_TEST_ACTOR_CERTIFICATE_PATH with MCP_TEST_ACTOR_CERTIFICATE_PASSWORD (and MCP_TEST_ACTOR_ASSERTION_AUDIENCE).
export MCP_TEST_ACTOR_CERTIFICATE_PATH=… MCP_TEST_ACTOR_CERTIFICATE_PASSWORD=…
export MCP_TEST_USER_TOKEN=…
dotnet test src/Platform/Storyteller/Mcp/testint --logger "console;verbosity=detailed"
```

With an account, the test checks that the issued token has `token_use` `user`, the actor id as `act.sub` and the scope `User.Impersonation`, and that `GET v1/access/account` with it returns the account its `sub` names. With `MCP_TEST_EXPECT_NO_ACCOUNT=true` and the token of a person without an account, it expects the refusal text above. Without the variables, both tests print `Skipped:` and pass.

## When the exchange fails

The tool answers with a text that names the cause; the server logs the status and the OAuth error code, never a token or a body.

| The API answers | The tool says |
| --- | --- |
| `400 invalid_grant` for a person without an account | The API's "No Storyteller account exists for this sign-in. Register once with sform or the admin UI." (see [Identity](#identity)). |
| `400 invalid_grant` otherwise | The API's "The subject token is not accepted for this actor.": the token is not a valid token of a person for this actor (issuer, audience, client, lifetime), or the replay limit is reached. The server's log adds a hint to check `SubjectAudiences`; the API's log names the reason. |
| `400` or `401` `invalid_client` | A fixed text: the API does not accept the server as an actor. Compare `Auth:TokenExchange:Actors` with `Mcp:Actor`, the `aud` of the client assertion, or the issuer and audience of the workload identity. |
| `400 unsupported_grant_type` | The API has the user token exchange turned off (`Auth:TokenExchange:Enabled`). |
| `502`, `503`, `504` or `temporarily_unavailable`, a timeout (30 seconds) or a broken connection | The exchange is temporarily unavailable; try again in a moment. |
| No request: the server cannot produce its proof (no managed identity token, no token file, or no readable key) | The server could not produce the credential of its actor (`Mcp:Actor`). |
| Anything else | "The MCP server could not get a token for the Storyteller API ({error})." |

## Tools

Every tool takes `organization` and `project` (from `list_projects`, keys `organization.project`) where it works on a project, and `view` (default `default`) where it works on a view. Lists are paged by the server: `limit` (default 50, at most 200) and the `ContinuationToken` of the previous result. A page can be empty and still carry a `ContinuationToken` when the list is filtered page by page; follow it. Deleting, restoring or changing listed items between pages can skip items, so after acting on a page start again from the first one. Results are JSON with the API's PascalCase names, in a text block and as structured content. An error has the composed text in the text block and `Status`, `ErrorCode`, `Message` (the API's message alone), `Hint` and `Errors` in the structured content.

Tools carry the MCP hints: reads are read-only; deletes, purges, resets and replacements without version history are destructive; no tool is open-world. Clients that confirm destructive or non-read-only calls ask the person before them.

### Discovery

| Tool | What it does |
| --- | --- |
| `get_my_account` | The signed-in account and its roles per organization and project |
| `list_projects` | The person's projects with role and description |
| `list_views` | The registered views of a project |

### Annotations

| Tool | What it does |
| --- | --- |
| `list_annotations`, `find_annotations`, `list_descendants`, `list_deleted_annotations` | Paged lists in a compact form (key, type, name, title, status, labels, modified) |
| `get_annotation`, `get_annotation_history` | One annotation; its audit trail |
| `set_annotation` | Creates or updates one annotation; omitted fields keep their stored values as stored, `clearFields` removes some. A create refuses a deleted key or ancestor and names `restore_annotations`, unless `recreateDeleted` is set |
| `create_annotations` | Up to 100 keys or compact lines at once; existing keys are skipped |
| `delete_annotation`, `restore_annotations`, `purge_annotation` | Soft delete with descendants, restore, permanent delete (Administrator) |

- `find_annotations` lists the annotations of one type and filters them by the names in their keys (`responsibility`, `subject`, `context`, `unit`) in the server, page by page. An exact name ignores case. A filter that starts with `^` or ends with `$` is a .NET regular expression, case-sensitive unless it starts with `(?i)`, with a match timeout of 100 ms; an invalid or too slow pattern is refused. The tool does not use the API's name queries ([annotation-lifecycle.md](annotation-lifecycle.md)), which compare exact names case-sensitively and run patterns on the non-backtracking engine.
- `list_deleted_annotations` with a `deletionId` lists everything that delete removed, descendants included, unless `roots` is `true`.
- New names follow the rule of `sform`: 1 to 50 letters, numbers, dashes or underscores. A name is new when its responsibility, subject, context or unit does not exist yet, deleted ones included; existing names are used as they are.

See [annotation-lifecycle.md](annotation-lifecycle.md).

### Configurations, schemas and templates

| Tool | What it does |
| --- | --- |
| `list_configurations` | Summaries of stored configurations |
| `get_configuration` | `form`: `effective` (ancestors and templates merged, bindings not evaluated), `own` (what is stored on the annotation) or a stored `version` |
| `set_configuration` | Merges into the stored document (objects merge, arrays union, `null` never erases; `$remove` takes JSONPaths) |
| `replace_configuration`, `revert_configuration` | Make the stored document equal a given document or a previous version; every top-level property they change or remove is tested first, so a concurrent change of it fails with `412` |
| `patch_configuration` | RFC 6902 operations on the stored document; refused when nothing is stored yet (`set_configuration` or `replace_configuration` writes a first document) |
| `delete_configuration`, `list_configuration_versions`, `diff_configuration` | Clear, versions, diffs between versions or views |
| `list_schemas`, `get_schema`, `set_schema`, `delete_schema`, `get_combined_schema`, `list_schema_versions`, `diff_schema` | Schemas of a type, an annotation, or a descendant type (`level`); the combined schema a write is checked against |
| `list_templates`, `get_template`, `set_template`, `replace_template`, `patch_template`, `delete_template`, `list_template_versions`, `diff_template` | View templates per type code |

Writes address the annotation's own stored document. Writing back an edited effective document would copy every inherited value onto the annotation. A configuration write on an annotation that does not exist is refused, because such a configuration is inherited but not listed. `set_schema` refuses a `$ref` that does not point inside the document (`#/…`), because the API would fetch a URL or file reference from its own network. See [inheritance.md](inheritance.md), [templating.md](templating.md), [definitions.md](definitions.md) and [binding.md](binding.md).

### Understanding configuration

| Tool | What it does |
| --- | --- |
| `search_configurations` | Finds configurations by key parts, and by property, value text or bound key/value key in the stored documents, within read budgets |
| `explain_configuration` | Where each value comes from: the step, source and node of every value, the path of nodes to the target, what was overridden, the bindings and the key/values they read (metadata only), whether the effective read is stale, and which machines can read the values |
| `suggest_configuration_placement` | Where a new value should be written for one or more systems, with the meaning of each level, whether a write there would take effect for each system (a replay of its composition), what would have to be created and a schema preview |
| `get_binding_catalog` | The binding functions and sources with examples |

The platform records no runtime reads of configurations. `explain_configuration` lists the machines whose scopes and [annotation restriction](authentication.md#annotation-restriction-of-machines) let them read the values; that is the evidence the platform has for "how is it used". They come in three groups: machines that read this document (unrestricted, or restricted to the key or an ancestor), machines restricted to a descendant, and machines restricted to an annotation that shares descendants with the key (for example `rst.billing` and `sbt.northwind` through `usg.northwind.billing`). The last two groups read the values only through such a descendant when it exists, and it may override them.

More details of the analysis:

- What was overridden (`AlsoSetBy`) is listed for scalar values only. Objects and arrays merge, so their members and items carry their own steps.
- Bindings are read by the server's own reader of the binding language. An expression longer than 8,192 characters or nested more than 64 levels deep is not read and is reported as not valid. A string that starts with `@` inside an object binding's `$definition` is reported as a possible read: it is bound only if the program emits it.
- `suggest_configuration_placement` treats each pointer as one scalar value outside any array. A pointer through an array or onto an object or array gets no `Effective` answer; place such values as a `fragment`. The schema preview is skipped when a pattern of the schema takes longer than the server's regular-expression timeout (200 ms).

### Machines

| Tool | What it does |
| --- | --- |
| `list_machines`, `get_machine` | Machines of a project; keys are masked |
| `create_machine`, `reset_machine` | Create a machine or give it a new key. The result holds the one-time credentials of its kind and a notice that they cannot be read again. A `Certificate` machine has no key to reset: it renews its certificate itself over mTLS, and is revoked by deleting it |
| `delete_machine`, `set_machine_federation_rules` | Delete; replace the federation rules |
| `get_machine_authentication_policy`, `set_machine_authentication_policy` | The credential kinds a project allows |

See [Scopes of machines](authentication.md#scopes-of-machines) for the roles. The `Federated` kind returns no secret and suits workloads with their own identity. Certificate renewal, shared certificates and trusted issuers stay with `sform`, the admin UI and the API.

While the server's actor has `NoSecretReveal`, the default, the API refuses `create_machine` and `reset_machine` for a machine with a `Secrets.*` scope, and `set_machine_federation_rules` for such a machine, with `403 ActorRestricted` (see [Restrictions of actors](authentication.md#restrictions-of-actors)). Deleting such a machine stays open.

### Values store

| Tool | What it does |
| --- | --- |
| `list_key_values`, `get_key_value`, `list_key_value_versions`, `diff_key_value` | Read pairs and versions without secret values; a diff of a secret is refused |
| `set_key_value`, `delete_key_value` | Write and delete with `expectedVersion` (`412 KeyValueVersionMismatch` on a mismatch); Hyok writes, also updates of an existing Hyok pair, stay with `sform` |
| `restore_key_values`, `purge_key_values`, `protect_key_values` | Bulk operations with per-key outcomes |
| `get_key_value_policy`, `set_key_value_policy`, `get_key_value_audit`, `get_key_value_usage` | Policy, audit and usage of a project (Administrator for writes, audit and usage); `set_key_value_policy` changes only the members it is given and always keeps `Public` allowed |

`set_key_value` replaces the stored content type: an update without `contentType` clears it, so pass the `ContentType` from `get_key_value` to keep it (a secret reference needs `application/vnd.2s.secret-reference` on every update). A secret value given to the tool enters the conversation; `sform` or the admin UI keep it out. See [key-values.md](key-values.md).

### Tools that return secrets

With `Mcp:SecretTools:Enabled` set to `true`, three more tools are registered. The server offers them only while the API lets its actor read secrets: the answer of the token exchange lists the actor's `restrictions`, and under `NoSecretReveal`, the default of every actor, `tools/list` leaves them out. An answer without `restrictions` counts as `NoSecretReveal`, so the server fails closed; an empty array means no restrictions. So offering them needs both the setting and `AllowSecrets: true` on the API's actor. None of them is read-only, so clients that confirm writes confirm them too:

| Tool | What it does |
| --- | --- |
| `reveal_key_value` | The secret value of a pair or a version; needs `ContributorWithSecrets`. The API records a reveal of the current value as a `Reveal` event; a reveal of an older version is not audited |
| `get_resolved_configuration` | The configuration with bindings evaluated; secret bindings are filled in with secret rights, and the API records every secret it reads |
| `check_configuration_bindings` | Per binding, whether it resolves; never the value, but the API reads the secrets for the check |

- **Fail closed.** `tools/list` asks the token exchange for the person (cached, so the tool calls that follow cost no second exchange). Without a token, or when the exchange fails, the list leaves the three tools out and still answers; the server logs the error code or the exception type.
- **Per person.** The list then differs per person, so every answer of `tools/list` is marked private for caches (`CacheScope` `Private`).
- **Refused when called.** A hidden tool can still be called. Under `NoSecretReveal` the server refuses it before the tool calls the API, with the same fixed text and `ErrorCode` `ActorRestricted` as the API's refusal: the API withholds secret values from the assistant, so read them with `sform` or the admin UI. The refusal needs the `restrictions` from the token exchange, so when the person's exchange is not cached (it usually is, from `tools/list`), the exchange runs first.
- **The API decides.** Hiding is presentation; the API enforces the restriction whatever the server offers: through a restricted actor it refuses secret reads, credentials that read secrets and grants of roles that read secrets with `403 ActorRestricted`, and resolves configurations without secrets, with the header `X-2S-Secrets-Withheld: actor-restricted` (see [Restrictions of actors](authentication.md#restrictions-of-actors)). Writing secrets is not affected.
- **Changes.** The API applies the actor's current restrictions also to tokens already issued, so turning `AllowSecrets` off takes effect at once, and turning it on applies to the tokens of later exchanges. The server cannot announce a change of its tool list; the client sees it on its next `tools/list`.

A value returned by a tool enters the model's context, the client's transcript and the model provider. Without the tools, people read secrets with `sform` or the admin UI.

## Safety

- Credentials returned by `create_machine` and `reset_machine` are shown once. The server's instructions ask the assistant to tell the person to copy them from the tool result.
- The server logs tool names, statuses and error codes, never arguments, results, tokens or response bodies; the token exchange is logged with its status and OAuth error code only. Outside Development no logging rule can lower the `ModelContextProtocol` categories below `Information`, because the MCP SDK writes whole messages at `Trace`.
- API errors reach the assistant with their status, error code, message and hint. `500` and `422` answers get fixed texts, because their messages can carry storage messages or evaluated secrets, and `403 ActorRestricted` gets a fixed text that says what the actor may not do. Stack traces never pass.
- Writes are not retried by the HTTP client, so a write runs once. A call may take up to five minutes. A call that times out or loses its connection answers that it may have completed on the server and names the read that checks it; lost one-time credentials mean a reset of the machine (or a new `Certificate` machine).
- The token exchange has an HTTP client of its own: no retries, because every attempt counts against the API's replay limit; no redirects, so the person's token goes to `Storyteller:BaseUrl` only; and a timeout of 30 seconds for the whole exchange, the actor's credential included.
- The Storyteller tokens are cached in memory by the SHA-256 of the person's token, until 60 seconds before they expire; a token that expires within the minute is kept until 5 seconds before. The cache holds at most 10,000 tokens, concurrent calls with one token share one exchange, and a failed exchange is not cached.
- Inbound tokens are checked with a clock skew of 30 seconds, and a token with less than 10 seconds left is refused with `401` and `error="invalid_token"`, so the client gets a new one instead of calling with a token the exchange would refuse.
- Regular expressions in the server, the schema preview's `pattern` checks included, time out after 200 ms, so a hostile pattern cannot hold the server.

## Limits

- Under Entra ID: one tenant per deployment, and no dynamic client registration; clients use pre-registered client ids.
- No sessions, subscriptions, server-initiated requests or elicitation. So no `notifications/tools/list_changed` either: a change of the tool list shows on the client's next `tools/list`.
- Not offered: view and key/value export and import, BYOK setup, key rotation, Hyok writes and decryption, recipients, shared certificates, trusted issuers, invitations and members.
- Search reads stored documents, not effective ones: a value inherited from an ancestor is found on the ancestor.
