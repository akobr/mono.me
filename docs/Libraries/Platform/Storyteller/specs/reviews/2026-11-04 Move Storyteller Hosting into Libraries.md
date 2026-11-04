# Move Storyteller Hosting into Libraries

Linear: [424-169](https://linear.app/424fun/issue/424-169/move-nuget-packages-in-to-library-workstead).

## Overview

Executes [Move Storyteller Hosting into Libraries](../2026-11-04%20Move%20Storyteller%20Hosting%20into%20Libraries.md). The package and its tests now live in `src/Libraries/Platform/Storyteller/Hosting`. Assembly name, package id, namespaces and behaviour are unchanged.

## What Was Done

### 1. The move

- `09d58087` moves the project, its tests and `package-readme.md`, shortens the reference in `Hosting.csproj` to `..\..\Configuration\src\Configuration.csproj`, and moves both projects to the `/Libraries/Platform/Storyteller/` folder of `42.mono.slnx`.
- The workstead's `version.json` applies, with no file of its own. The move restarts the commit count of the package's directory, so its version falls below the `0.10.8-alpha` it had, as Configuration and ConfigurationFormats did in their move.
- The 218 tests of `Hosting.UnitTests` pass, and the packed package contains its readme.

### 2. Documentation

- `2f82abcd` updates the code location in `docs/Platform/Storyteller/hosting.md` and lists Hosting under Libraries in `CLAUDE.md`.
