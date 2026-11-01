# Phase 3 of the NuGet Upgrade: Crumble

## Overview

Phase 3 of [Upgrade All NuGet Packages and Move to .NET 10](../2026-11-01%20Upgrade%20All%20NuGet%20Packages%20and%20Move%20to%20.NET%2010.md) (Linear [424-143](https://linear.app/424fun/issue/424-143/upgrade-all-nuget-packages-to-the-latest-versions-and-move-every)): Orleans 10.4.2, the Microsoft dashboard instead of the deprecated OrleansContrib one, a clean-build fix of the playground's `Examples`, Cronos 0.13.0 and the playground AppHost on the Aspire 13.6 APIs. The Crumble source generator, its Roslyn 5.3 and the code it emits are unchanged. Commits: `08b28e11`, `63c1f5b4`, `72085e71`.

## What Was Done

### 1. Orleans 10.4.2

- All ten `Microsoft.Orleans.*` entries 10.0.1 → 10.4.2. No Orleans API that Crumble uses changed (grain base class, grain call filters, `IPersistentState`, `StatelessWorker`, `RequestContext`, `UseOrleans`, `UseOrleansClient`).
- `IReminderRegistry.GetReminder` returns `IGrainReminder?` since 10.4. `MessageActionPoolingGrain` and `VolumeActionPoolingGrain` unregister the reminder only when they found one. **Note:** the `UnregisterReminder` call is still not awaited (`CS4014`, as before); awaiting it would change when the grain deactivates, so it is left as it was.
- The generator: nothing it emits (`global::Orleans.Grain`, `[global::Orleans.Concurrency.StatelessWorker]`, `IGrainFactory.GetGrain<T>(string)`, the executor types, `AddCrumbs()`) changed in 10.4. After deleting the `obj` and `bin` of `Examples.Crumble` and `Examples`, the build regenerated the files under `obj/Generated/42.Crumble.SourceGenerator.Orleans`, and Orleans' own (now incremental) generator wrote 17 proxies for `Examples`, among them `IChainedCrumbsFirstGrain` and `IChainedCrumbsSecondGrain`.

### 2. Dashboard

- `OrleansDashboard` 8.2.0 (OrleansContrib, built for Orleans 8; it fails at silo start on Orleans 10) → `Microsoft.Orleans.Dashboard` 10.4.2, a new central entry in alphabetical order.
- `src/Crumble/Playground/Orleans.Silo/src/Program.cs`: `siloBuilder.AddDashboard()` instead of `UseDashboard(HostSelf = false)`, and `app.MapOrleansDashboard(routePrefix: "/dashboard")` instead of `app.Map("/dashboard", x => x.UseOrleansDashboard())`. The new dashboard is always mapped into the host's endpoints, so the old `HostSelf = false` is its only mode. The Aspire command "Open Dashboard" keeps its `{endpoint}/dashboard` URL.
- The silo was built but not started: it needs the playground's Redis and Aspire environment.

### 3. Clean builds of `Examples`

- `Examples.csproj` compiled the generated files of `Examples.Crumble` with a static glob, evaluated before `Examples.Crumble` builds, so a clean `dotnet build src` failed with `CS0246 IChainedCrumbsExecutor` (one of the baseline's three errors).
- The glob moved into a target, `IncludeCrumbleGeneratedFiles`, that runs after `ResolveProjectReferences` and before `BeforeCompile` (and `CompileDesignTime`, for the IDE), with `Compile Remove` before the `Include`. The clean build of the playground and the clean `dotnet build src` at the end of phase 5 both pass. (The final review added an `UpToDateCheckInput` for Visual Studio, see phase 5.)

### 4. Cronos

- 0.11.1 → 0.13.0; `CronExpression.TryParse(string, CronFormat, out)` and `GetOccurrences(...)` of `Actions.Time.Orleans` are unchanged.

### 5. Crumble AppHost

- `ResourceBuilderExtensions.cs`: `WithCommand` with `CommandOptions { UpdateState, IconName, IconVariant }` instead of the obsolete overload, and `ExecuteCommandResult.Message` instead of `ErrorMessage` (the result stays `Success = false`, which is how the command shows the URL).
- `ExplicitStartupExtensions.cs`: compares with `KnownResourceCommands.StartCommand` (`"start"`; Aspire 13.2 renamed it from `"resource-start"`, so the extension had silently stopped finding the command) and clones it through the constructor with `Arguments`, `Visibility` and `ValidateArguments` instead of the obsolete `Parameter`. Nothing calls the extension (both AppHosts use Aspire's own `WithExplicitStart`); it is kept, as it was, without obsolete members.
- The playground AppHost builds without the Aspire `CS0618` warnings of phase 2.

### Validation

- `dotnet build src`: 0 errors, no new warning. Crumble has no tests.
- Audit: nothing new.
