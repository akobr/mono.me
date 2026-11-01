# Phase 4 of the NuGet Upgrade: Monorepo, c0ded0c, Texo, Libraries and Games

## Overview

Phase 4 of [Upgrade All NuGet Packages and Move to .NET 10](../2026-11-01%20Upgrade%20All%20NuGet%20Packages%20and%20Move%20to%20.NET%2010.md) (Linear [424-143](https://linear.app/424fun/issue/424-143/upgrade-all-nuget-packages-to-the-latest-versions-and-move-every)): MSBuild 18.9, the console and tool packages, YamlDotNet 18, Markdig 1.5, TestableIO 22.3 with the new members of the trace library, the removal of the unused NuGet references of the c0ded0c CLI, and the `mrepo` scaffolding on .NET 10. After this phase the audit reports no vulnerable package anywhere in `src`. Commits: `367bdd3b`, `df04e158`, `5048be7c`.

## What Was Done

### 1. Build and NuGet

- `Microsoft.Build` and `Microsoft.Build.Framework` 18.4.0 → 18.9.6, held below 18.10 with a comment: `Microsoft.Build` 18.10.1 and 18.11.0 ship only `net11.0` and `net472`, and `.Framework` stays on the same 18.9.x (the MSBuild of the .NET 10.0.4xx SDK that the Locator loads at run time). The first wording of the comment said that `.Framework` had no `net10.0`-usable asset either, which is wrong (it ships `netstandard2.0`); the final review corrected it.
- **Deviation:** `Microsoft.Build.Framework` 18.9 depends on `Microsoft.NET.StringTools`, which arrived with runtime assets, and `Microsoft.Build.Locator` failed the build with `MSBL001` in `c0ded0c/MsBuild`, `c0ded0c/Cli` and `Repo.Generator`. A central `Microsoft.NET.StringTools` 18.9.6 entry and a reference with `ExcludeAssets="runtime" PrivateAssets="all"` in those three projects, with a comment, fix it; the SDK's copy is loaded at run time, as for the other MSBuild assemblies.
- The four `NuGet.*` references of `src/c0ded0c/Cli/src/Cli.csproj` (no c0ded0c code uses NuGet APIs) and their central entries are gone, and with them the vulnerable `System.Security.Cryptography.Pkcs` 8.0.1 of `NuGet.Packaging`.

### 2. Console and tools

- `LibGit2Sharp` 0.32.0 (a security release of libgit2), `Sharprompt` 3.2.0, `Spectre.Console.Json` 0.57.2, `YamlDotNet` 18.1.0, `Markdig` 1.5.1 in `src/Texo/Directory.Packages.props` (`Core.Markdown` keeps `netstandard2.1`; Markdig still ships it). No code change was needed. `McMaster.Extensions.*` 5.1.0, `Semver`, `QuikGraph`, `Colorful.Console`, `ConsoleTableExt`, `ShellProgressBar`, `DiffPlex`, `Alba.CsConsoleFormat-NoXaml` and `Microsoft.Build.Locator` were already the newest.
- Markdig: four documents of the repository (`CLAUDE.md` and three docs) were rendered with the pipeline of `MarkdownService` under 0.26.0 and 1.5.1 in a scratch project. `Normalize` and `ToHtml` gave identical output; `ToPlainText` of 1.5.1 keeps the quotes and angle brackets inside code blocks, which 0.26.0 dropped.
- Smoke runs: `codedoc --help` and `mrepo --help` work, and `Repo.Generator` gets past `MSBuildLocator.RegisterDefaults()` with SDK 10.0.403. `codedoc` itself reads a hard-coded `c:\Working\c0ded0c\c0ded0c.sln`, so its MSBuild workspace loading could not be run here.

### 3. TestableIO 22.3.0

- `TestableIO.System.IO.Abstractions`, `.TestingHelpers` and `.Wrappers` 22.3.0 add `IFileSystem.RandomAccess` (`IRandomAccess`) and `IFile.OpenHandle(...)` without default implementations.
- `src/Libraries/Testing/System.IO.Abstractions.Trace`: new `DiagnosticRandomAccess` (traces each call, a span as its length or as an array like `DiagnosticFile` does, and delegates) and `ReadonlyRandomAccess` (reads through; `Write`, `WriteAsync`, `SetLength` and `FlushToDisk` do nothing, like the writes of `ReadonlyFile`). `DiagnosticFile.OpenHandle` traces and delegates. `ReadonlyFile.OpenHandle` opens a handle only to read an existing file, never with `FileOptions.DeleteOnClose` (added after the final review: the option deleted the real file when the handle closed), and otherwise throws `NotSupportedException`, because a handle cannot be simulated in memory as its streams are. `DiagnosticFileSystem` and `ReadonlyFileSystem` expose the new `RandomAccess`.

### 4. Scaffolding

- `src/Monorepo/Cli/src/Configuration/BasicOptionsExtensions.cs`: without `dotnetVersion` `mrepo` scaffolds .NET 10 (was 9).
- `GlobalJsonT4` writes SDK 10.0.401 (the template keeps its own `rollForward`). `RootDirectoryPackagesPropsT4` writes the new versions of the packages it shares with the root catalog: `Microsoft.Extensions.*` 10.0.14, `coverlet.collector` 10.1.0, `Microsoft.NET.Test.Sdk` 18.11.0, `Moq` 4.21.0, `TUnit` 1.78.0, `xunit.runner.visualstudio` 4.0.1, `StyleCop.Analyzers` 1.3.151-beta. The template-only `NUnit`, `NUnit3TestAdapter` and `SonarAnalyzer.CSharp` versions were left out at first; after the final review they are 5.0.0, 6.3.1 and 10.35.0.4138. `Repo.Generator`'s `ProjectCsprojT4` writes `net10.0` (was `net6.0`). Every `.tt` and its committed preprocessed `.cs` changed together.
- `docs/Monorepo/mrepo-json.md` shows `"dotnetVersion": 10`.
- Not done: `mrepo new` was not run end to end.

### Validation

- `dotnet build src`: 0 errors, no new warning.
- The tests of the touched code pass when run alone: `Platform/Cli` 1062, `ConfigurationFormats` 416, `Binding.JsonE` 2429, `Utils/Async` 5 (no test project exists for c0ded0c, Texo, the Monorepo CLI or the trace library).
- Audit: no `NU1901`–`NU1904` in any `project.assets.json` of `src`.
