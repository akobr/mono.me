# Phase 3 of Hosting: who am I for any annotation, and a catalog and informer for any descendants

Linear: [424-214](https://linear.app/424fun/issue/424-214/hosting-who-am-i-for-any-annotation-and-a-catalog-and-informer-for-any).

## Overview

Executes phase 3, the playground and the documentation (sections 10 and 12), of [Hosting: who am I for any annotation, and a catalog and informer for any descendants](../2026-11-08%20Hosting%20-%20who%20am%20I%20for%20any%20annotation,%20and%20a%20catalog%20and%20informer%20for%20any%20descendants.md). `DotNet.SimpleExecutor` lists the units of a responsibility from `IStorytellerCatalog`, so the unit that no execution runs shows, and the documentation describes the typed snapshots, the catalog, freshness and restrictions. This closes the narrow case of 424-208.

## What Was Done

### 10. The playground's executor

- `36375ab6` injects `IStorytellerCatalog` into `ExecutorCommand`; `Program.cs` needed no change, as `AddStoryteller` registers it.
- For an `rst.R` key with `all` or `units`, the run lists `StorytellerTarget.UnitsOf("rst.R")` once, before the executions, in one shot and with `--watch`, and writes each unit's document by key. The set of written units stays, so the executions do not write a listed unit again.
- `units` on an `rst.R` key needs no `--context`: it writes the units and ends, as a `unt` key does. `all` without `--context` is still refused before anything is listed.
- A failed list prints `! the units of rst.R: {message}` (the target's `ToString()`), the executions go on, and the run exits with `1`.
- `usg`, `unt`, `exe` and `uxe` keys keep their behaviour.
- `ExecutionRenderer.WriteWho` prints the typed members of the key's annotation: `Units` of a responsibility or an execution, `Contexts` and `Responsibilities` of a subject, `Contexts` of a usage, `Responsibilities` of a context, `Unit type` of a unit. The names are escaped as the other lines from the platform are.
- `data/` adds `unt.playground.archive` with its configuration, the unit that no execution runs. A unit without `UnitType` gets the defaults of a create on import.
- **Tests.** `FakeApi` records the paths it is asked for, gives `rst.playground` its `UnitNames`, `unt.playground.nightly-export` a `UnitType`, and adds `unt.playground.archive`, its document and the `units` list of `rst.playground`. `ExecutorTests` adds: the unit no execution names, written before the executions and listed once; `units` without a context; `executions` and `units-of-execution` list no units; a `usg` key lists no units and keeps those of its executions; a failed list; who am I of a responsibility and of a unit. The 35 tests pass.

### 12. Documentation

- `d6820a62` updates `docs/Platform/Storyteller/hosting.md`: the services table, the typed snapshots of who am I and `Find<T>()`, a table of the snapshot types with what comes from the key and from the answer and the discriminator's fallback, a new section "Any annotation and its descendants" (the members, targets with their table of combinations, filters and status, lists, watching a target, freshness), "The catalog" renamed "The catalog of executions", the comparison of the three watches, what a restricted machine can target, the logging category, the cost of the catalog, and the limit on relation names.
- `docs/Platform/Storyteller/configuration-provider.md` follows the renamed anchor.
- `docs/Platform/Storyteller/playground.md`: the executor's three Hosting services, the `archive` unit of the import, the units of an `rst` key from the catalog and `units` without a context, the typed who am I and its output, the failed list among the exit codes, and the limit on units now covers the other keys only.
- `docs/Libraries/Platform/Storyteller/Hosting/package-readme.md` and `CLAUDE.md` name `IStorytellerCatalog` and the typed snapshots; the package description in `Hosting.csproj` changed with phase 2 (`80a1101a`).
- `89f552ed` adds to `hosting.md` that `GetAsync<T>` of a type the application derived throws.

### Deviations

- The playground also prints the typed who am I, and `units` of an `rst` key needs no context; both are in the spec's section 10, beyond what the issue asked.
