# Phase 1 of Move Storyteller NuGet packages into Libraries

Linear: [424-169](https://linear.app/424fun/issue/424-169/move-nuget-packages-in-to-library-workstead).

## Overview

Phase 1 is section 1 of [Move Storyteller NuGet packages into Libraries](../2026-11-03%20Move%20Storyteller%20NuGet%20packages%20into%20Libraries.md). The four published packages moved from `src/Platform/Storyteller` into `src/Libraries/Platform/Storyteller`. Assembly names, package ids, namespaces and behaviour are unchanged. The version line stays `0.10-alpha`. One `version.json` in the workstead sets `hierarchicalVersion`, so the packages share that line and a commit in one package directory bumps only that package.

| Commit | Content |
|---|---|
| `b9b15b84` | The move, the solution folder, the project references and the workstead `version.json` |
| `edb7b701` | The workstead file briefly used one shared version (`pathFilters`) |
| `4b889332` | The workstead file returns to `hierarchicalVersion` |

## What Was Done

### 1. Move the projects and retarget the build

`git mv` of the tracked files only. `bin/` and `obj/` stayed behind and were not deleted. Hosting, the conformance suite, `Sdk.NSwag`, `Abstractions.Annotations` and the other Storyteller projects stayed in `src/Platform/Storyteller`.

| Package | New folder | Informational version at `4b889332` (Debug) | At `05a6afcc` |
|---|---|---|---|
| `42.Platform.Storyteller.Aspire.Hosting` | `src/Libraries/Platform/Storyteller/Aspire.Hosting` | `0.10.2-alpha` | `0.10.2-alpha` |
| `42.Platform.Storyteller.Configuration` | `src/Libraries/Platform/Storyteller/Configuration` | `0.10.2-alpha` | `0.10.4-alpha` |
| `42.Platform.Storyteller.Configuration.KeyVault` | `src/Libraries/Platform/Storyteller/Configuration.KeyVault` | `0.10.2-alpha` | `0.10.0-alpha` |
| `42.Platform.Storyteller.ConfigurationFormats` | `src/Libraries/Platform/Storyteller/ConfigurationFormats` | `0.10.2-alpha` | `0.10.5-alpha` |

`42.mono.slnx` lists the eight projects under `/Libraries/Platform/Storyteller/`. Sibling references stayed `..\..\…`. References back into the product use five `..` segments from a moved `src` or `test` directory. `Hosting/src`, `Api.Functions/src` and `Cli/src` point at the new folders. The conformance embed stays `src/Platform/Storyteller/conformance`. No source comment named an old project path, so none was rewritten. The four package readmes moved to `docs/Libraries/Platform/Storyteller/<Name>/package-readme.md`. Their links to the generic pages are unchanged.

`src/Libraries/Platform/Storyteller/version.json` is the only version file for the workstead. It keeps `0.10-alpha` and `inherit`, and sets `hierarchicalVersion` to true, as in `docs/Monorepo/versioning.md`. The packages share the `0.10-alpha` line. A later commit under one package directory bumps that package and leaves the others. Without the file the packages fall back to the repository `0.1.0`.

**Versioning, and where it differs from the spec.** The spec also kept `hierarchicalVersion`, and added a per-project `version.json` if a package fell below its pre-move version. There is no per-project file. At `4b889332` the four packable projects and their test projects are `0.10.2-alpha`. Aspire matches its pre-move version. Configuration was `0.10.4-alpha` and ConfigurationFormats was `0.10.5-alpha`, so those two are lower. Key Vault is above `0.10.0-alpha`. `edb7b701` had used `pathFilters` so every package moved together; `4b889332` replaces that with the hierarchical file. Hosting, which was not moved, is `0.10.8-alpha` because the reference edit in `Hosting/src` is one more commit in its own directory (it was `0.10.7-alpha`).

`Configuration`, `ConfigurationFormats`, `Configuration.KeyVault`, `Aspire.Hosting` and `Hosting` build with 0 errors. `Api.Functions` and `Cli` built with 0 errors after the reference edits. The four moved test projects pass: ConfigurationFormats 416, Configuration 641 with 2 Unix-only skips on Windows, Configuration.KeyVault 87, Aspire.Hosting 164.
