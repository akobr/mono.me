# Phase 1 of the NuGet Upgrade: SDK, Shared Packages and Target Frameworks

## Overview

Phase 1 of [Upgrade All NuGet Packages and Move to .NET 10](../2026-11-01%20Upgrade%20All%20NuGet%20Packages%20and%20Move%20to%20.NET%2010.md) (Linear [424-143](https://linear.app/424fun/issue/424-143/upgrade-all-nuget-packages-to-the-latest-versions-and-move-every)). The branch `feat/424-143` starts from `origin/main` with PR #60 merged into it (`caeb70a4`). This phase sets the SDK requirement, moves the two remaining `net6.0` test projects to `net10.0`, upgrades the packages that many areas share, Roslyn and StyleCop, keeps the Crumble source generator on Roslyn 5.3, removes three obsolete catalog entries and the in-box package references that restore reported. `dotnet build src` has no errors any more (before: 3), and the audit warnings that remained belong to the later phases. Commits: `df533e10`, `8f34db40`, `5a6d8234`, `0a9ade79`.

## What Was Done

### 1. SDK

- `global.json`: `"version": "10.0.401"`, `"rollForward": "latestFeature"`, `"allowPrerelease": false`; `Microsoft.Build.Traversal` 4.1.82.
- The build and all checks of this branch ran with SDK 10.0.403 (runtime 10.0.14), installed side by side from the official zip (SHA-512 checked against the release metadata) because the machine had only 10.0.204. With `latestFeature` the CLI takes the newest installed 10.0 SDK from 10.0.401 on, and never a .NET 11 preview.

### 2. mrepo

- `mrepo.json` `custom.dotnetVersion` 10. The `CLAUDE.md` note follows in phase 5.

### 3. Target frameworks

- `src/Monorepo/Repo.Generator/test/Repo.Generator.Tests.csproj` and `src/Platform/Supervisor/Abstraction/test/Abstraction.Tests.csproj` target `net10.0`. Both projects contain no tests yet; they build, and `dotnet test` finds nothing in them.
- No other target framework changed. The `netstandard` and multi-targeted projects keep their targets, as the owner asked.

### 4. Shared packages

All versions are the newest on nuget.org on 2026-11-01:

- `Microsoft.Extensions.*` 10.0.x, `Microsoft.AspNetCore.Authentication.JwtBearer`, `.OpenIdConnect`, `.Mvc.Testing`, `.OpenApi`, `Microsoft.Bcl.AsyncInterfaces`, `System.CodeDom`, `System.Collections.Immutable`, `System.Diagnostics.DiagnosticSource` and `System.Text.Json`: 10.0.12 → 10.0.14.
- `Microsoft.Extensions.Http.Resilience` and `.ServiceDiscovery` 10.10.0 → 10.11.0, `Microsoft.Extensions.TimeProvider.Testing` 10.1.0 → 10.11.0, in the same commit as the 10.0.14 move (they need 10.0.13 or newer).
- The six `OpenTelemetry.*` packages 1.15.x → 1.20.1 together.
- `Azure.Security.KeyVault.Secrets` 4.12.0, `Microsoft.Azure.Cosmos` 3.63.3, `Microsoft.Identity.Client` and `.Extensions.Msal` 4.92.0, `Microsoft.Identity.Web`, `.UI` and `.MicrosoftGraph` 4.16.1, `Microsoft.Graph` 6.9.0, `Polly` 8.9.0, `PostHog` 2.18.0, `Sentry` and `Sentry.Serilog` 6.12.1, `Serilog.Sinks.ApplicationInsights` 5.0.1, `Swashbuckle.AspNetCore` 10.3.1, `Nerdbank.GitVersioning` 3.10.94.
- The other entries of these families were already the newest (for example `Azure.Identity` 1.21.0, the `Microsoft.IdentityModel.*` 8.23.0, `Newtonsoft.Json` 13.0.4, the json-everything packages).

### 5. Roslyn

- All `Microsoft.CodeAnalysis.*` entries 5.3.0 → 5.9.0.
- New `src/Crumble/SourceGenerator.Orleans/Directory.Packages.props`: it imports the root catalog with `GetPathOfFileAbove` (the pattern of `src/Texo/Directory.Packages.props`) and sets `PackageVersion Update` 5.3.0 for `Microsoft.CodeAnalysis.CSharp` and `.Analyzers`, with the reason as a comment. `obj/project.assets.json` of the generator shows `Microsoft.CodeAnalysis.CSharp`/`.Common`/`.Analyzers` 5.3.0, those of `c0ded0c/MsBuild` and `Compose` 5.9.0.
- `Libraries/Roslyn/Compose` no longer references `Microsoft.CodeAnalysis.Workspaces.MSBuild`: it has no `netstandard2.0` asset, Compose uses no workspace type, and the `NU1701` warnings for `Microsoft.IO.Redist` and `Microsoft.VisualStudio.SolutionPersistence` are gone.

### 6. StyleCop.Analyzers

- 1.2.0-beta.556 → 1.3.151-beta (the newest of the 1.3 prerelease line, published by the StyleCop maintainers on 2026-10-21; `stylecop.json` needed no change).
- A clean build of the merged baseline in a temporary worktree and the clean build at the end of phase 5 were compared by file and line. The new or wider rules add 77 warnings: `SA1315` 22 (parameter names differ from the overridden member), `SA1143` 19 (layout of wrapped conditional expressions), `SA1407` +16 (bitwise and shift operators in the three `MurmurHash3` copies), `SA1413` +11 (trailing commas, now also in collection expressions), `SA1504` 3, `SA1017` 3, `SA1111`, `SA1202` and `SA1503` +1 each. `SA1516` and `SA1515` report 7 fewer.
- **Deviation:** the spec said to fix new warnings when they are few and mechanical. They are spread over about 45 files in every area (including the generator's `MurmurHash3.cs`), the repository already carries about 680 StyleCop warnings and builds without `TreatWarningsAsErrors`, so none was fixed and no rule was suppressed. They are left for a style pass of their own.

### 7. Obsolete entries

- Removed: `Microsoft.AspNetCore.App` 2.2.8, `Microsoft.Orleans.Runtime.Abstractions` 3.8.0, `System.ComponentModel.Annotations` 5.0.0. Nothing referenced them.
- The other unused entries (`coverlet.collector`, the unused Functions extensions, `Microsoft.Extensions.Caching.Abstractions`, `Microsoft.Identity.Web.MicrosoftGraph`, `Microsoft.Orleans.Reminders.Redis`, `Microsoft.Orleans.Serialization.Abstractions`, `NBuilder`, `Nerdbank.GitVersioning`, `Serilog.Sinks.ApplicationInsights`, `Serilog.Sinks.Console`, `Swashbuckle.AspNetCore`, `Testcontainers`, `Testcontainers.Azurite`, `Testcontainers.PostgreSql`) stay and are on their newest versions.

### 8. In-box references

The `PackageReference`s that restore reported with `NU1510` are gone: `System.Text.Json` (`Abstractions.Access`, `Abstractions.Annotations`, `c0ded0c/Core`), `System.Collections.Immutable` (`c0ded0c/Core`, `c0ded0c/MsBuild`), `System.Diagnostics.DiagnosticSource` (`Crumble/Runtime.Core`), `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options` (`Api.Web`), `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Logging.Console` (`Crumble/Playground/Orleans.Silo`), `Microsoft.CSharp` (`Repo.Generator`). All these projects are `net10.0` only, so the shared framework provides the same assemblies. The central entries of the packages that still have consumers stay; `System.Collections.Immutable`, `System.Diagnostics.DiagnosticSource` and `System.Text.Json` lost their last consumer here, and the final review of phase 5 removed their entries.

### Validation

- `dotnet build src`: 0 errors. The compiler warnings are the baseline's apart from the StyleCop ones above.
- `dotnet test src --no-build` with `STORYTELLER_TESTS_USE_CONTAINER=true`: the same results as the baseline (see the spec's Current State); the Cosmos- and Lowkey-Vault-backed failures are the container environment of this machine.
- Audit after the phase: the `MessagePack`, `Pkcs`, ASP.NET Core 2.1, `Newtonsoft.Json` 11 and `SSH.NET` warnings of the baseline, which phases 2 and 4 remove.
