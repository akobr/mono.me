# Local Cosmos DB Emulator Mode for the Aspire Host

## Problem

The Aspire host always runs Cosmos DB as the Linux vNext emulator in a container (`RunAsPreviewEmulator`). That emulator does not support server-side programming: stored procedures, triggers and user-defined functions fail. Work that needs them has to run against the Windows Cosmos DB Emulator installed on the machine, which the host cannot use today, so that work cannot be orchestrated by Aspire at all.

## Current State

- `src/Platform/Aspire.Host/src/AppHost.cs` adds `AddAzureCosmosDB("cosmos").RunAsPreviewEmulator(...)` (persistent container, Data Explorer, image `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest`). `db-creator` and `api-functions` take it with `WithReference(cosmos)` and `WaitFor(cosmos)`, which gives them `ConnectionStrings__cosmos`.
- `src/Platform/Aspire.Host/src/Properties/launchSettings.json` has the profiles `https` and `http`.
- `AddCosmosDbAnnotations` (`src/Platform/Storyteller/Backend.CosmosDb/src/EntryPoint.cs`) reads `ConnectionStrings:cosmos` and sets `ShouldAcceptAnyCertificate` for `localhost` and `127.0.0.1` endpoints, so the self-signed certificate of either emulator is accepted without changes in the services.

## Proposed Changes

### 1. Configuration switch `Cosmos:Mode`

The host reads `Cosmos:Mode` from its configuration:

| Value | Cosmos resource |
|---|---|
| `container` (default, also when missing) | `AddAzureCosmosDB("cosmos").RunAsPreviewEmulator(...)`, exactly as today |
| `local` | `AddConnectionString("cosmos")`, a connection string taken from `ConnectionStrings:cosmos`; Aspire starts nothing, the Windows emulator runs on the machine |

Any other value fails the startup of the host with an `InvalidOperationException` naming the allowed values, so a typo does not silently start the container.

Both branches give an `IResourceBuilder<IResourceWithConnectionString>` named `cosmos`, so `WithReference(cosmos)` and `WaitFor(cosmos)` of `db-creator` and `api-functions` stay unchanged. `WaitFor` on a connection-string resource completes as soon as the connection string resolves; reachability of the emulator is not checked by Aspire.

### 2. Launch profiles

Two profiles are added to `launchSettings.json`, copies of `https` and `http` with:

- `Cosmos__Mode=local`
- `ConnectionStrings__cosmos=AccountEndpoint=https://localhost:8081/;AccountKey=<well-known emulator key>` (the published key of the emulator, already used across the repository)

Names: `https-local-cosmos`, `http-local-cosmos`. They are run with `dotnet run --project src/Platform/Aspire.Host/src --launch-profile https-local-cosmos`. The existing profiles keep the container mode. The endpoint can be overridden by the usual configuration (user secrets, environment) for an emulator on another port.

### 3. No changes in the services

The services already accept any certificate on `localhost`, and the database and containers are created by `db-creator` (explicit start) in both modes.

### 4. Documentation

A page `docs/Platform/Aspire.Host/local-development.md` describes the resources of the host, the two Cosmos modes, when to pick which, and the prerequisites of the local mode (the Windows emulator installed and started, default port 8081).
