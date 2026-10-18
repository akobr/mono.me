# Local Development with the Aspire Host

`src/Platform/Aspire.Host` orchestrates the Platform locally:

```bash
dotnet run --project src/Platform/Aspire.Host/src
```

| Resource | What it is |
|---|---|
| `storage` | Azurite in a persistent container on the default ports (10000 blob, 10001 queue, 10002 table), so `UseDevelopmentStorage=true` works |
| `cosmos` | Cosmos DB emulator, see [Cosmos DB modes](#cosmos-db-modes) |
| `db-creator` | Creates the database and containers; explicit start, run it from the dashboard |
| `keyvault` | Lowkey Vault, the Key Vault emulator of key/value protection |
| `api-functions` | The Storyteller Azure Functions API |
| `ui-admin` | The Vite dev server of the admin UI on port 5173 |

## Cosmos DB modes

The configuration value `Cosmos:Mode` picks the emulator:

| Mode | Emulator | Use it when |
|---|---|---|
| `container` (default) | Linux vNext emulator in a persistent container, with Data Explorer | Everyday work; nothing to install besides Docker |
| `local` | Windows Cosmos DB Emulator installed on the machine, reached through `ConnectionStrings:cosmos` | Stored procedures, triggers or UDFs are needed; the container emulator does not support them |

The launch profiles `https-local-cosmos` and `http-local-cosmos` set the local mode and point at `https://localhost:8081/` with the well-known emulator key:

```bash
dotnet run --project src/Platform/Aspire.Host/src --launch-profile https-local-cosmos
```

In the local mode Aspire does not start or watch the emulator: install the [Windows emulator](https://learn.microsoft.com/azure/cosmos-db/emulator), start it before the host, and run `db-creator` once against it. For another endpoint, override `ConnectionStrings__cosmos` (environment or user secrets). An unknown mode fails the startup of the host. The services accept the self-signed certificate of either emulator on `localhost`.
