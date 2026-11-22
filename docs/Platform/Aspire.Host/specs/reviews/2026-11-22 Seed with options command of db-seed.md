# Seed with options command of db-seed

## Overview

This adds the dashboard commands **Seed with options…** to `db-seed` and **Create with options…** to `db-creator`. Each asks for DbCreator's options in a dialog and runs the resource once with them, and the plain Start keeps today's defaults. Spec: [2026-11-22 Seed with options command of db-seed](../2026-11-22%20Seed%20with%20options%20command%20of%20db-seed.md).

## What Was Done

1. **`src/Platform/Aspire.Host/src/DbSeedCommands.cs`** (new): `WithSeedOptions` and `WithCreateOptions`. The inputs are declared through `CommandOptions.Arguments`, which the dashboard asks for itself, so the code doesn't call `IInteractionService`. They are read from `ExecuteCommandContext.Arguments`.
2. **The invocation holder** (`PendingInvocation<T>`, set with a compare-and-swap so two invocations can't overlap): `db-seed`'s arguments and `db-creator`'s environment are callbacks. The command starts the resource through `KnownResourceCommands.StartCommand`. It doesn't clear the holder when the start returns, as the spec said. It waits until a run that began after the request is `Running`, `Finished` or `Exited` (or `FailedToStart`), with a timeout of 2 minutes, and clears the holder only then. This way the options can't be dropped before the arguments are evaluated.
3. **Seed with options…**: one checkbox per folder of `DbCreator/src/data-sets` that has a `set.json`, found at the host's startup. `showcase` is checked, and only `showcase` is offered if the folder is missing. It also has the inputs for `--path`, `--owner` (prefilled with `BaseAccount:Id`, else the staging user when the staging accounts are seeded), `--organization`, `--project`, `--view` and the four flags. The validation asks for a set or a folder and checks that the folder exists. The command is off while the resource is running, starting or waiting, and it logs the arguments it runs with. `baseAccount__id` and the vault environment of `db-seed` stay as they were.
4. **Create with options…**: `Base account id` (passed as `baseAccount__id` when given) and `Seed the AuthKit staging accounts` (passed as `stagingSeed__enabled`), both prefilled with the host's decision.
5. **`AppHost.cs`**: `baseAccountId` and the default seed owner are computed once, and both resources use the new extensions instead of the fixed `WithArgs`/`WithEnvironment` and the `if`/`else` that followed them.
6. **Docs**: the resource table of `docs/Platform/Aspire.Host/local-development.md` and the Seed data section of `docs/Platform/development.md`.

The host compiles. The dialog hasn't been tried in a running dashboard yet: the host that was running locked the build output, so it was compiled into a separate folder.
