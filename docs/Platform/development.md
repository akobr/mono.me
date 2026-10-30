# Running the Platform Locally

A cookbook for running the Storyteller API, the admin UI and the MCP server on one machine, signed in with the **AuthKit staging** environment. It collects the setup steps and the problems met along the way. The resources of the Aspire host are described in [Local Development with the Aspire Host](Aspire.Host/local-development.md), the MCP server in [MCP Server](Storyteller/mcp.md), and the token exchange in [User Authentication](Storyteller/authentication.md#user-token-exchange).

## What runs where

| Piece | Where | Address |
| --- | --- | --- |
| Storyteller API (`api-functions`) | Aspire, Azure Functions host | `http://localhost:7071/api` |
| MCP server (`mcp`) | Aspire, explicit start | `https://localhost:7180/mcp` (Aspire's proxy; the process itself listens on a random port) |
| Admin UI (`ui-admin`) | Aspire, Vite | `http://localhost:5173` |
| Cosmos DB, Azurite, Lowkey Vault | Aspire (containers) or the local Windows emulator | see the Aspire page |
| Sign-in | WorkOS AuthKit staging | `https://voluntary-entry-98-staging.authkit.app` |

The API runs in `Development` (`DOTNET_ENVIRONMENT` in `local.settings.json`), so the MCP server can prove itself to the API with a client secret: no certificate and no tunnel are needed.

## 1. Tools

- .NET SDK 10 (`global.json`), Node 22.18 or newer, Docker Desktop.
- Azure Functions Core Tools 4, the current 64-bit build (`func --version`).
- The Windows Cosmos DB emulator, recommended: the Linux container emulator does not support stored procedures, which the Platform requires. Run the host with the `https-local-cosmos` profile to use it. It needs an elevated (administrator) start; `Backend.CosmosDb` tests use it at `https://localhost:8081`, and some of them (`KeyValueEndToEndTests`) also need Docker for Lowkey Vault.
- HTTPS dev certificate: `dotnet dev-certs https --trust`.

## 2. Trust the dev certificate in Node

The MCP Inspector and Claude Code run on Node, which ignores the Windows certificate store. Without this, Claude Code fails with *certificate signature failure* and the Inspector with *fetch failed*.

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.aspnet\https" | Out-Null
dotnet dev-certs https -ep "$env:USERPROFILE\.aspnet\https\localhost.pem" --format PEM --no-password
Remove-Item "$env:USERPROFILE\.aspnet\https\localhost.key"
[Environment]::SetEnvironmentVariable("NODE_EXTRA_CA_CERTS", "$env:USERPROFILE\.aspnet\https\localhost.pem", "User")
```

Open a new terminal afterwards. Export again after `dotnet dev-certs https --clean`.

## 3. WorkOS AuthKit (staging)

Switch the dashboard to the **Staging** environment first; settings of Production have no effect on the staging AuthKit domain.

1. **Connect → Configuration**: turn on **Client ID Metadata Document (CIMD)** and **Dynamic Client Registration (DCR)**. MCP clients register themselves.
2. **Resource indicator**: add exactly `https://localhost:7180/mcp` (lower case, the port, no trailing slash) and, in its **…** menu, **Set as default**.
   Without it, AuthKit issues tokens whose `aud` is the environment's client id (`client_01M3MMV9SP2W3D7VDZ3242KJNR`), and the MCP server answers `401` with `error="invalid_token"` (in its log: `IDX10214: Audience validation failed`). Do **not** work around it by making that client id the MCP audience: it is also the audience of the admin UI and `sform` tokens, and listing it in the actor's `SubjectAudiences` makes the API refuse every ordinary user token.
3. **Scope**: dynamically registered clients get the OpenID scopes plus the environment's default scopes, which WorkOS support sets. For local testing use `openid` as the MCP scope; a real permission such as `mcp:access` needs WorkOS support to add it to the default scopes.

After changing WorkOS settings, clear the client's OAuth state and sign in again: a refresh token can keep the old audience.

## 4. API settings (`src/Platform/Storyteller/Api.Functions/src/local.settings.json`)

The AuthKit part already points at staging (`Auth:Provider` `AuthKit`, `Auth:AuthKit:Issuer` the AuthKit domain). For the MCP server add the actor of the token exchange:

```json
"Auth:TokenExchange:Enabled": true,
"Auth:TokenExchange:Actors:0:Id": "mcp",
"Auth:TokenExchange:Actors:0:Authentication:ClientSecretHash": "<hex SHA-256 of the MCP secret>",
"Auth:TokenExchange:Actors:0:SubjectAudiences:0": "https://localhost:7180/mcp"
```

- An actor without `ClientSecretHash` (or another proof) and `SubjectAudiences` stops the API at start.
- Do not add `Auth:TokenExchange:AdditionalIssuers` for the AuthKit domain: `Auth:AuthKit:Issuer` already is that domain, and listing it again stops the API at start.
- A start warning that actor `mcp` has no `AllowedClientIds` is expected: dynamically registered clients have unknown ids.

Create the secret and its hash once:

```powershell
$secret = "<a random string>"
-join ([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($secret)) | % { $_.ToString("x2") })
```

The **hash** goes to the API, the **secret itself** to the MCP server. Giving the MCP server the hash makes every tool call answer *"The Storyteller API does not accept the MCP server as an actor of its token exchange…"*.

## 5. MCP server settings

`src/Platform/Storyteller/Mcp/src/appsettings.Development.json` (tracked, so keep secrets out of it):

```json
{
  "Mcp": {
    "Provider": "AuthKit",
    "ResourceUrl": "https://localhost:7180/mcp",
    "Authentication": {
      "Issuer": "https://voluntary-entry-98-staging.authkit.app",
      "Scope": "openid"
    },
    "Actor": {
      "Id": "mcp"
    }
  },
  "Storyteller": {
    "BaseUrl": "http://localhost:7071/api"
  }
}
```

The secret goes into user secrets:

```sh
cd src/Platform/Storyteller/Mcp/src
dotnet user-secrets set "Mcp:Actor:ClientSecret" "<the secret, not its hash>"
```

`Mcp:Provider` must be the API's `Auth:Provider`. To see why a token is refused, add `"Logging": { "LogLevel": { "Microsoft.AspNetCore.Authentication": "Information" } }` to the same file.

## 6. Start everything

```sh
dotnet run --project src/Platform/Aspire.Host/src --launch-profile https-local-cosmos   # or without the profile for the Cosmos container
```

1. Wait for `cosmos`, `storage`, `keyvault` and `api-functions`. Run `db-creator` once for a new database (with `BaseAccount:Id` = your WorkOS `user_…` id in the host's user secrets).
2. Start `mcp` from the dashboard. Its console log shows `The API's Auth:TokenExchange:Actors entry 'mcp' must list these SubjectAudiences: https://localhost:7180/mcp.` and `Now listening on: https://localhost:<random port>`; always use port 7180, the proxy.
3. Make sure you have an account in the local database: sign in once through the admin UI or `sform` against `http://localhost:7071/api` (sform reads the AuthKit details from `v1/auth/configuration`).

Checks:

```sh
curl http://localhost:7071/api/v1/auth/configuration                       # "TokenExchange": { "Enabled": true }
curl -k https://localhost:7180/.well-known/oauth-protected-resource/mcp     # the AuthKit domain as authorization server
```

Opening `https://localhost:7180/mcp` in a browser answers `405`: the server is stateless and only accepts `POST`. A `POST` without a token answers `401` with `WWW-Authenticate: Bearer resource_metadata=…`, which is correct.

## 7. Connect an MCP client

```sh
npx @modelcontextprotocol/inspector
```

Streamable HTTP, URL `https://localhost:7180/mcp`, **Connect**, sign in at AuthKit. Or Claude Code: `claude mcp add --transport http storyteller-local https://localhost:7180/mcp`, then `/mcp` to sign in.

What to try:

- `get_my_account`, `list_projects`: your account (`user_…`) and projects.
- `set_annotation`, then `get_annotation_history`: you, with `Via: "mcp"`.
- A WorkOS user without a local account: *"No Storyteller account exists for this sign-in. Register once with sform or the admin UI."*
- `Mcp:SecretTools:Enabled=true`: the secret tools stay hidden under the default `NoSecretReveal`; `"Auth:TokenExchange:Actors:0:AllowSecrets": true` on the API (restart, sign in again) shows them.
- The MCP sign-in token sent straight to the API (`curl -H "Authorization: Bearer <token>" http://localhost:7071/api/v1/access/account`) answers `401`.

## 8. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `https://localhost:7180/mcp` in a browser: `405` | Expected; use an MCP client |
| *certificate signature failure* (Claude Code), *fetch failed* (Inspector) | Node does not trust the dev certificate; see [step 2](#2-trust-the-dev-certificate-in-node) |
| `401 invalid_token` from the MCP server; log `IDX10214 … Audiences: 'client_…'` | The resource indicator is missing in WorkOS staging; see [step 3](#3-workos-authkit-staging) |
| `403 insufficient_scope` | The token lacks `Mcp:Authentication:Scope`; use `openid` locally |
| *The Storyteller API does not accept the MCP server as an actor…* | The MCP secret does not hash to the API's `ClientSecretHash` (often the hash was given to the MCP server), the actor id differs, or the API does not run in `Development` |
| *The subject token is not accepted for this actor.* | `SubjectAudiences` does not hold the token's `aud`; the API's log names the reason |
| *The API has the user token exchange turned off* | `Auth:TokenExchange:Enabled` is not `true`, or the API was not restarted |
| `aspire run` ignores the profile | Use `dotnet run --project src/Platform/Aspire.Host/src --launch-profile <name>` |

Decoding a token's claims without a tool:

```powershell
$t = "<access token>"; $p = $t.Split('.')[1].Replace('-','+').Replace('_','/'); $p += '=' * ((4 - $p.Length % 4) % 4)
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p))
```

Expect `iss` = the AuthKit domain, `aud` = `https://localhost:7180/mcp`, `sub` = `user_…` and `scope` with the MCP scope.

## Against a deployed API

The staging API does not run in `Development`, so a client secret is refused there. The MCP server then proves itself with a client assertion key (`Mcp:Actor:ClientAssertionKey`), and the API must download its public key from `https://<server>/.well-known/jwks.json` over public HTTPS on port 443. Locally that needs a tunnel, for example Microsoft Dev Tunnels:

```sh
winget install Microsoft.devtunnel
devtunnel user login
devtunnel create storyteller-mcp --allow-anonymous
devtunnel port create storyteller-mcp -p 7180 --protocol https
devtunnel host storyteller-mcp
```

Use the tunnel URL for `Mcp:ResourceUrl`, the WorkOS resource indicator, the actor's `SubjectAudiences` and its `ClientAssertion:JwksUri`, and set `MachineAuth:Federation:PublicBaseUrl` on the API to exactly the MCP server's `Storyteller:BaseUrl`. See [Credentials of the actor](Storyteller/mcp.md#credentials-of-the-actor).
