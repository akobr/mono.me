# Move Storyteller Hosting into Libraries

Linear: [424-169](https://linear.app/424fun/issue/424-169/move-nuget-packages-in-to-library-workstead).

## Problem

`42.Platform.Storyteller.Hosting` is a reusable NuGet package like the four packages that [Move Storyteller NuGet packages into Libraries](2026-11-03%20Move%20Storyteller%20NuGet%20packages%20into%20Libraries.md) moved, but it stayed in `src/Platform/Storyteller`.

## Current State

- The project and its tests are `src/Platform/Storyteller/Hosting/src/Hosting.csproj` and `test/Hosting.UnitTests.csproj`. Only the tests reference the project.
- `Hosting.csproj` references the provider in `src/Libraries/Platform/Storyteller/Configuration`.
- The package readme is `docs/Platform/Storyteller/Hosting/package-readme.md`, found through the project's path by `Directory.Build.props`.

## Proposed Changes

### 1. The move

- Move `src/Platform/Storyteller/Hosting` to `src/Libraries/Platform/Storyteller/Hosting`. Assembly name, package id, namespaces and behaviour stay.
- Shorten the reference to `Configuration.csproj`, and move the two projects to the `/Libraries/Platform/Storyteller/` folder of `42.mono.slnx`.
- Move the package readme to `docs/Libraries/Platform/Storyteller/Hosting/`, so the package keeps it.
- The workstead's `version.json` applies; no file of its own.

### 2. Documentation

- `docs/Platform/Storyteller/hosting.md` names the new code location. The generic page stays where it is.
- `CLAUDE.md` lists Hosting under Libraries.
