# Local Cosmos DB Emulator Mode

## Overview

The Aspire host can now use either the containerized Linux Cosmos DB emulator (default) or the Windows Cosmos DB Emulator installed on the machine, which supports stored procedures. Spec: [2026-10-18 Local Cosmos DB Emulator Mode for the Aspire Host](../2026-10-18%20Local%20Cosmos%20DB%20Emulator%20Mode%20for%20the%20Aspire%20Host.md).

## What Was Done

### 1. Configuration switch `Cosmos:Mode`

`src/Platform/Aspire.Host/src/AppHost.cs` reads `Cosmos:Mode` (case-insensitive, default `container`) and builds `cosmos` as an `IResourceBuilder<IResourceWithConnectionString>`: `RunAsPreviewEmulator` with the previous settings for `container`, `AddConnectionString("cosmos")` for `local`, and an `InvalidOperationException` for anything else. The `ASPIRECOSMOSDB001` suppression is now restored after the switch, and the commented-out `RunAsEmulator` variant was removed. `db-creator` and `api-functions` are unchanged.

### 2. Launch profiles

`Properties/launchSettings.json` has `https-local-cosmos` and `http-local-cosmos`, copies of `https` and `http` that add `Cosmos__Mode=local` and `ConnectionStrings__cosmos` with `https://localhost:8081/` and the well-known emulator key (the same key used elsewhere in the repository).

### 3. No changes in the services

As planned: `AddCosmosDbAnnotations` already accepts any certificate on `localhost`.

### 4. Documentation

New page `docs/Platform/Aspire.Host/local-development.md` with the resources of the host and the two Cosmos modes.

The host builds with no errors. Neither mode was run end to end against a live emulator as part of this change.
