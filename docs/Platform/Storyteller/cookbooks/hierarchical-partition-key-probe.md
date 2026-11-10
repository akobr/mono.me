# Cookbook: Hierarchical Partition Key Probe

Storyteller moves from a container per organization to one shared container `organizations`, partitioned by `/Organization` and then by the `PartitionKey` the code uses today (see [decisions](../../Deployment/decisions.md), item 9). Before the backend changes, the probe checks that every Cosmos DB you develop and deploy against handles that layout, and every kind of operation the backend uses.

The probe is one C# file run with `dotnet run`: [`src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs`](../../../../src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs). It creates its own database `hpk-probe-<time>`, works only inside it, and deletes it at the end (unless `--keep`). It never touches `42.Platform.2S` or any other database, so it is safe against an emulator that holds your development data.

## What it checks

| Check | Where the platform does it |
| --- | --- |
| Database with shared throughput, a control container with `/PartitionKey`, the container with `/Organization`, `/PartitionKey` and the indexing policy of `ContainerFactory` | `ContainerFactory`, `DbCreator` |
| The same `id` and `PartitionKey` in two organizations | every item: ids are unique per full key only |
| Point read with the full key; a read in another organization is a miss | every read of an item |
| Single-partition query (full key in the options) | listings of key/values, schemas, templates, machines |
| Organization query with the prefix key and `WHERE c.Organization`, and with `WHERE` only | listings of configurations and annotations of a project, the re-wrap, counts per view |
| Prefix key **without** `WHERE c.Organization` (INFO) | never: the platform always filters by organization; this only shows what the option alone does |
| `GROUP BY`, `ORDER BY`, paging with continuation tokens over an organization | counts per view, ordered listings, every paged API listing |
| Transactional batch on the full key, and a failing batch that rolls back | key/value writes (history plus catalog item), annotation batches |
| Conditional patch (`set`, `remove`) | access and machine updates |
| `ReadMany` with full keys of two organizations | the dependency check of conditional reads |
| Stored procedure on the full key (INFO) | none: the two procedures `ContainerFactory` registers are never called |
| Item with `ttl` expires | versions, deleted keys, audit and usage items |
| Delete one organization by query and batches | a future organization deletion (there is no delete by key prefix) |
| List the containers of a database, list the databases (INFO) | `ContainerFactory` (organization containers), the cleanup of the Cosmos test suites |

`PASS` and `FAIL` decide; `INFO` lines only report. The exit code is the number of failures. Every line shows the request charge, so the classic emulator and Azure give comparable RU numbers (the vNext emulator reports a flat 1 RU per request).

## Prerequisites

- The .NET 10 SDK (`dotnet --version` 10.0.401 or newer).
- Docker Desktop with 4 GB or more of memory for the Linux emulators.
- The first `dotnet run` restores `Microsoft.Azure.Cosmos` and `Newtonsoft.Json` at the versions of `Directory.Packages.props`.

Run every command from the repository root. In PowerShell, the line continuation is a backtick instead of `\`.

## Run 1: the classic Linux emulator (Aspire and Testcontainers)

This is the emulator of the Aspire host (`RunAsClassicEmulator`) and of the Cosmos DB integration tests (`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest`). Publish it on port 18081, because the Windows emulator may already use 8081.

```sh
docker run --rm -d --name hpk-probe-classic -p 18081:8081 \
  -e AZURE_COSMOS_EMULATOR_PARTITION_COUNT=3 \
  -e AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE=false \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest
```

Wait for the line `Started` on its own (after `Started 4/4 partitions`); it takes one to three minutes:

```sh
docker logs -f hpk-probe-classic
```

Then run the probe and stop the emulator:

```sh
dotnet run src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs -- --endpoint https://localhost:18081/ --label "classic Linux emulator"
docker stop hpk-probe-classic
```

**Result of 2026-11-09** (run while writing the probe): 19 passed, 0 failed, 2 informational. The prefix key alone kept the other organization out, and the stored procedure ran. A point read cost 1 RU, organization queries about 3 RU.

## Run 2: the vNext Linux emulator

The vNext emulator is lighter and starts in seconds, but it has no stored procedures, no request charges and no range indexes yet, and it answers an `ORDER BY` query from a Linux client (whose SDK gets the query plan from the emulator) in one page, whatever its `MaxItemCount`. An expired item stays in the way of a create with its id (409 Conflict) for about a second after reads stop returning it. The .NET SDK needs it in HTTPS mode.

```sh
docker run --rm -d --name hpk-probe-vnext -p 28081:8081 -p 28080:8080 \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest --protocol https
curl http://localhost:28080/ready
dotnet run src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs -- --endpoint https://localhost:28081/ --label "vNext emulator"
docker stop hpk-probe-vnext
```

Expected: the stored procedure line says `not supported here`, and the charges are placeholders (a flat 1 RU per request). Any `FAIL` tells whether the vNext emulator could replace the classic one for the tests later.

**Result of 2026-11-09** (image of 2026-10-19, 1.7 GB, about 590 MB of memory while running, ready within seconds): 19 passed, 0 failed, 4 informational. The prefix key alone kept the other organization out, the stored procedure answered `400` (not supported), and listing containers and databases worked, although the emulator's documentation lists the database and collection feeds as not implemented.

## Run 3: the Windows emulator

The Windows emulator supports hierarchical partition keys only when it is started with `/EnablePreview`. The Cosmos DB test suites wipe this emulator unless `STORYTELLER_TESTS_USE_CONTAINER=true` is set, and the probe itself only adds and deletes its own database.

First run it as the emulator runs today, to see whether `/EnablePreview` is needed (a `FAIL` on the container line means it is):

```sh
dotnet run src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs -- --endpoint https://localhost:8081/ --label "Windows emulator"
```

Then restart it with the preview features, in PowerShell, and run the probe again:

```powershell
& "$env:ProgramFiles\Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe" /Shutdown
& "$env:ProgramFiles\Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe" /EnablePreview
dotnet run src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs -- --endpoint https://localhost:8081/ --label "Windows emulator, /EnablePreview"
```

**Result of 2026-11-09** (version 2.14.27, started without arguments, so without `/EnablePreview`): 19 passed, 0 failed, 4 informational, with the same request charges as the classic Linux emulator. The stored procedure ran, and listing containers and databases worked. **Version 2.14.27 needs no `/EnablePreview`** for hierarchical partition keys; Microsoft's page still asks for it. The two levels were really in effect: the container reported both paths, and the same id under different `PartitionKey` values of one organization was accepted, which a container keyed only by `/Organization` would refuse with `409`.

To check how your emulator runs, in PowerShell:

```powershell
Get-CimInstance Win32_Process -Filter "Name = 'Microsoft.Azure.Cosmos.Emulator.exe'" | Select-Object ProcessId, CommandLine, CreationDate | Format-List
```

When it passes only with `/EnablePreview`, the local development setup (`Cosmos:Mode=local` of the Aspire host) has to start the emulator that way from now on.

## Run 4 (optional): the real account in Azure

The emulators imitate Cosmos DB; the account `prod-2s-42fornet-cdb` is the final answer. Run it **after the account exists and before `DbCreator` creates `42.Platform.2S`**: the probe database is then the only one, and its autoscale 1000 RU/s stays inside the free tier. Run it later, next to the production database, with `--throughput manual` (400 RU/s for the minutes the probe database lives; a few cents at most).

```sh
KEY=$(az cosmosdb keys list --resource-group prod-2s-eusec-rg --name prod-2s-42fornet-cdb --query primaryMasterKey -o tsv)
dotnet run src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs -- --endpoint https://prod-2s-42fornet-cdb.documents.azure.com:443/ --key "$KEY" --label "Azure, Sweden Central"
```

Azure gives the real request charges, which are worth keeping for the spec of the shared container.

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `--endpoint <url>` | `https://localhost:8081/` | The account endpoint |
| `--key <key>` | the well-known emulator key | The account key |
| `--throughput autoscale\|manual\|none` | `autoscale` | Throughput of the probe database: autoscale 1000 max, manual 400, or none |
| `--insecure` | on for `localhost` and `127.0.0.1` | Accept any TLS certificate (the emulators' self-signed ones) |
| `--keep` | off | Keep the probe database to look at it in the Data Explorer; delete it yourself afterwards |
| `--label <text>` | the endpoint | The name of the run in the summary |

## Reading the results

- **All `PASS`**: the emulator or account supports the layout. Send the summary lines (`dotnet run … > probe-classic.txt`).
- **`FAIL` on the container line**: no hierarchical partition keys there. The control container above it tells whether creating containers works at all.
- **`FAIL` on a query line**: note the reason; that query shape needs another form in the backend, or that emulator can't run the tests of that area.
- **`INFO` prefix key without `WHERE`**: when it reports items of other organizations, the option alone does not isolate. The backend never relies on it either way.
- **`INFO` stored procedure `not supported here`**: expected on vNext, and harmless: the platform calls no stored procedure.
- **`INFO` listing `not supported here`**: that emulator can't list containers or databases; the code that lists them (`ContainerFactory`, the test cleanup) would need another way there.
- **`INFO` ttl `still there`**: some emulators expire lazily; Azure removes the item within seconds.

## Troubleshooting

- **`Connection refused`**: the emulator is still starting; wait for `Started` (classic) or `/ready` (vNext).
- **Port already allocated**: another emulator uses the port; pick other host ports in `-p` and `--endpoint`.
- **TLS errors against Azure**: never pass `--insecure` there; check the endpoint name.
- **`400` "special mandatory indexing path"**: an indexing policy without `/*` included; the probe sets it like `ContainerFactory`, so this only appears after editing the probe.
- **The classic emulator stops at `Started 1/4 partitions`**: give Docker more memory, or lower `AZURE_COSMOS_EMULATOR_PARTITION_COUNT`.
