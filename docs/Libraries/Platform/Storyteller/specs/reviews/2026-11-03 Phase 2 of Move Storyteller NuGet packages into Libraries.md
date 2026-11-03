# Phase 2 of Move Storyteller NuGet packages into Libraries

Linear: [424-169](https://linear.app/424fun/issue/424-169/move-nuget-packages-in-to-library-workstead).

## Overview

Phase 2 is section 2 of [Move Storyteller NuGet packages into Libraries](../2026-11-03%20Move%20Storyteller%20NuGet%20packages%20into%20Libraries.md). Living docs and the two Storyteller workflows now name `src/Libraries/Platform/Storyteller`. Files under `docs/**/specs/` were not edited. The generic pages stay in `docs/Platform/Storyteller`, so `docs/42for.net` is unchanged.

| Commit | Content |
|---|---|
| `85527aab` | The code locations in the living docs and in `CLAUDE.md` |
| `a3f90623` | The conformance workflow and the NuGet pack loop |

## What Was Done

### 2. Living documentation and CI

- `docs/Platform/Storyteller/configuration-provider.md`, `configuration-formats.md` and `aspire.md` name the new `src/Libraries/Platform/Storyteller/…/src` locations. `Sdk.NSwag` stays where it was.
- `docs/Platform/Storyteller/conformance.md` and `src/Platform/Storyteller/conformance/README.md` run the formats and provider tests from the new folders. The TypeScript command is unchanged.
- `CLAUDE.md` drops Configuration, Configuration.KeyVault, ConfigurationFormats and Aspire.Hosting from the Storyteller product list and lists them under Libraries at `Platform/Storyteller/`. The note that the generic Storyteller docs stay under `docs/Platform/Storyteller/` is on the formats entry.
- `.github/workflows/storyteller-conformance.yml` triggers on `src/Libraries/Platform/Storyteller/Configuration/**`, `ConfigurationFormats/**` and `version.json`, on both `push` and `pull_request`. The two `dotnet test` steps use those projects. The filters for the conformance suite, `Abstractions.Annotations`, `KeyValues.ClientEncryption`, `Sdk.NSwag` and `src/Platform/version.json` stay.
- `.github/workflows/storyteller-sdk-release.yml` packs `ConfigurationFormats`, `Configuration` and `Configuration.KeyVault` from `src/Libraries/Platform/Storyteller/$project/src`. `Abstractions.Annotations`, `KeyValues.ClientEncryption` and `Sdk.NSwag` still pack from `src/Platform/Storyteller`.
