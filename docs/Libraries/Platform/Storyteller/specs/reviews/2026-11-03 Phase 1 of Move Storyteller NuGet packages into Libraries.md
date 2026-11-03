# Phase 1 of Move Storyteller NuGet packages into Libraries

Linear: [424-169](https://linear.app/424fun/issue/424-169/move-nuget-packages-in-to-library-workstead).

## Overview

Phase 1 is section 1 of [Move Storyteller NuGet packages into Libraries](../2026-11-03%20Move%20Storyteller%20NuGet%20packages%20into%20Libraries.md). The four published packages moved from `src/Platform/Storyteller` into `src/Libraries/Platform/Storyteller`. Assembly names, package ids, namespaces and behaviour are unchanged. The version line stays `0.10-alpha`. A height offset keeps each packed version at or above the version it had at `05a6afcc`.

| Commit | Content |
|---|---|
| `b9b15b84` | The move, the solution folder, the project references and the shared `version.json` |
| `21732150` | Path filters on the four projects. They did not change the height |
| `f5ca3d1f` | `hierarchicalVersion: false` on those files. The parent flag still won |
| `721e9f11` | `versionHeightOffset` on the three packages whose height had dropped. This is the version that ships |

## What Was Done

### 1. Move the projects and retarget the build

`git mv` of the tracked files only. `bin/` and `obj/` stayed behind and were not deleted. Hosting, the conformance suite, `Sdk.NSwag`, `Abstractions.Annotations` and the other Storyteller projects stayed in `src/Platform/Storyteller`.

| Package | New folder | Informational version at `721e9f11` (Debug) | At `05a6afcc` |
|---|---|---|---|
| `42.Platform.Storyteller.Aspire.Hosting` | `src/Libraries/Platform/Storyteller/Aspire.Hosting` | `0.10.2-alpha` | `0.10.2-alpha` |
| `42.Platform.Storyteller.Configuration` | `src/Libraries/Platform/Storyteller/Configuration` | `0.10.4-alpha` | `0.10.4-alpha` |
| `42.Platform.Storyteller.Configuration.KeyVault` | `src/Libraries/Platform/Storyteller/Configuration.KeyVault` | `0.10.1-alpha` | `0.10.0-alpha` |
| `42.Platform.Storyteller.ConfigurationFormats` | `src/Libraries/Platform/Storyteller/ConfigurationFormats` | `0.10.5-alpha` | `0.10.5-alpha` |

`42.mono.slnx` lists the eight projects under `/Libraries/Platform/Storyteller/`. Sibling references stayed `..\..\…`. References back into the product use five `..` segments from a moved `src` or `test` directory. `Hosting/src`, `Api.Functions/src` and `Cli/src` point at the new folders. The conformance embed stays `src/Platform/Storyteller/conformance`. No source comment named an old project path, so none was rewritten. The four package readmes moved to `docs/Libraries/Platform/Storyteller/<Name>/package-readme.md`. Their links to the generic pages are unchanged.

`src/Libraries/Platform/Storyteller/version.json` matches `src/Platform/version.json` (`0.10-alpha`, `inherit`, `hierarchicalVersion`). Without it the packages fall back to the repository `0.1.0`.

**Versioning, and where it differs from the spec.** The first build after the move was `0.10.1-alpha` for every package: hierarchical height counts only the new `src/` directory, and only the move commit touches it. That is below Aspire (`0.10.2`), Configuration (`0.10.4`) and ConfigurationFormats (`0.10.5`).

Path filters did not recover the old history. In this fork (`42.Monorepo.GitVersioning` 1.2.3) `hierarchicalVersion: true` replaces path filters with the project directory. A child that sets `hierarchicalVersion: false` does not override the parent: `false` is the default, and the serializer leaves the unset value for inherit to fill from the parent. Each commit is also scored with the `version.json` that existed in that commit. Commits from before the move, read from the new directory, resolve to the root `0.1.0` and reset the height. `21732150` and `f5ca3d1f` are that attempt. They stay in the history. They do not describe the files at the tip.

`721e9f11` sets `versionHeightOffset` where the new height of 1 was short of the old package version: Aspire `1`, Configuration `3`, ConfigurationFormats `4`. Key Vault is already `0.10.1`, above `0.10.0`, so it has no project `version.json`. The offset is applied to a non-zero height, and a later commit in that package's `src/` still increments it. Hosting, which was not moved, is `0.10.8-alpha` because the reference edit in `Hosting/src` is one more commit in its own directory (it was `0.10.7-alpha`).

`Configuration`, `ConfigurationFormats`, `Configuration.KeyVault`, `Aspire.Hosting` and `Hosting` build with 0 errors. `Api.Functions` and `Cli` built with 0 errors after the reference edits. The four moved test projects pass: ConfigurationFormats 416, Configuration 641 with 2 Unix-only skips on Windows, Configuration.KeyVault 87, Aspire.Hosting 164.
