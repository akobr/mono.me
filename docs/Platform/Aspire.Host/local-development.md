# Local Development with the Aspire Host

`src/Platform/Aspire.Host` orchestrates the Platform locally:

```bash
dotnet run --project src/Platform/Aspire.Host/src
```

| Resource | What it is |
|---|---|
| `storage` | Azurite in a persistent container on the default ports (10000 blob, 10001 queue, 10002 table), so `UseDevelopmentStorage=true` works |
| `cosmos` | Cosmos DB emulator, see [Cosmos DB modes](#cosmos-db-modes) |
| `db-creator` | Creates the database and containers, and seeds the organization `42dotnet` with its owner; explicit start, run it from the dashboard. `BaseAccount:Id` in the user secrets of the host names the owner's account id: the lower-case `{tid}:{oid}` under Entra ID, the `sub` under AuthKit or Keycloak. Without it, DbCreator seeds its legacy Entra ID `sub`, which matches no account |
| `keyvault` | Lowkey Vault, the Key Vault emulator of key/value protection |
| `api-functions` | The Storyteller Azure Functions API |
| `ui-admin` | The Vite dev server of the admin UI on port 5173; it waits for its two preparation steps below, see [Admin UI](#admin-ui) |
| `ui-admin-sdk` | Installs and builds the TypeScript SDK the admin UI depends on (`node scripts/prepare-local.mjs`), then finishes |
| `ui-admin-installer` | Installs the admin UI's packages (`npm run prepare:local`), then finishes |
| `mcp` | The [MCP server](../Storyteller/mcp.md) for AI assistants; explicit start, see [MCP server](#mcp-server) |

## Admin UI

The admin UI prepares itself, so a clean clone needs nothing but Node 22.18 or newer on `PATH`:

1. `ui-admin-sdk` runs `src/Platform/Storyteller/sdk.typescript/src/scripts/prepare-local.mjs`: `npm ci` when `package-lock.json` changed, `npm run build` when a build input changed (the entry files, `lib/`, `generated/`, the tsup and TypeScript configs, `package.json`, the lockfile).
2. `ui-admin-installer` (Aspire's own installer of the Vite app) runs `npm run prepare:local` in `src/Platform/ui.admin/src`: `npm ci` when the lockfile changed, or only a reinstall of the SDK's packed copy when just the SDK was rebuilt.
3. `ui-admin` starts Vite.

Each step keeps a SHA-256 of its inputs in a stamp file (`node_modules/.prepare-local-install`, `dist/.prepare-local-build`) and skips what is up to date. The first start of a clean clone takes a minute or two while npm downloads packages; later starts take seconds. A failed step keeps `ui-admin` waiting; the step's console log says why.

Changes made while the host runs (a new lockfile, SDK sources) are picked up when `ui-admin` restarts. The **Clean reinstall** command of `ui-admin` stops it, deletes the stamps, runs both steps in full and starts it again; use it when `node_modules` is broken. A full `npm ci` replaces `node_modules`, so stop `npm run dev:mock` of the same folder first.

Without Aspire, `npm run prepare:local` in the SDK folder and then in the admin UI folder does the same.

## Cosmos DB modes

The configuration value `Cosmos:Mode` picks the emulator:

| Mode | Emulator | Use it when |
|---|---|---|
| `container` (default) | Classic Linux emulator in a persistent container with the named data volume `platform-cosmos-data` (it supports the stored procedures the backend registers); x64 hosts only, it does not run on Apple silicon or Windows on Arm | Everyday work on an x64 machine; nothing to install besides Docker |
| `local` | Windows Cosmos DB Emulator installed on the machine, reached through `ConnectionStrings:cosmos` | The Windows emulator is preferred, or triggers or UDFs are needed |

The data volume was added with the .NET 10 upgrade (424-143): data of a `cosmos` container created before it is not in the volume. If you need that data, export it from the old container first (for example with the Data Migration Tool or your own queries); otherwise run `db-creator` once after the first start to seed the database again.

The launch profiles `https-local-cosmos` and `http-local-cosmos` set the local mode and point at `https://localhost:8081/` with the well-known emulator key:

```bash
dotnet run --project src/Platform/Aspire.Host/src --launch-profile https-local-cosmos
```

In the local mode Aspire does not start or watch the emulator: install the [Windows emulator](https://learn.microsoft.com/azure/cosmos-db/emulator), start it before the host, and run `db-creator` once against it. For another endpoint, override `ConnectionStrings__cosmos` (environment or user secrets). An unknown mode fails the startup of the host. The services accept the self-signed certificate of either emulator on `localhost`.

## MCP server

The host gives the `mcp` resource `Storyteller:BaseUrl`, the `http` endpoint of `api-functions` with `/api`. The MCP server signs its clients in with the deployment's identity provider and exchanges their tokens at the API, so both need settings:

- The user secrets of the MCP server: `Mcp:Provider` and `Mcp:Authentication:*` (the same provider as the API's `Auth:Provider`), and its actor, `Mcp:Actor:Id` with `Mcp:Actor:ClientSecret`, which only the Development environment accepts. `Mcp:ResourceUrl` is already `https://localhost:7180/mcp` in Development; set it only for another URL.
- The `local.settings.json` of the API: `Auth:TokenExchange:Enabled` set to `true`, and the actor in `Auth:TokenExchange:Actors`: its `Id`, the hex SHA-256 of the secret as `Authentication:ClientSecretHash`, and the MCP server's audiences as `SubjectAudiences`. The MCP server logs the audiences to list when it starts. With the exchange off, every tool answers that the API has the user token exchange turned off; without the actor, that the API does not accept the MCP server as an actor.

The setup per provider is in [MCP server](../Storyteller/mcp.md), and the whole recipe in its [Local development](../Storyteller/mcp.md#local-development).
