# Move Storyteller NuGet packages into Libraries

Linear: [424-169 Move NuGet packages in to Library workstead](https://linear.app/424fun/issue/424-169/move-nuget-packages-in-to-library-workstead)

## Problem

The published Storyteller NuGet packages sit inside the 2S Platform product tree (`src/Platform/Storyteller/`). A reader cannot tell a product project from a package that ships on its own. The Libraries workstead (`src/Libraries`) is the place for published packages. These four packages, and only these four, move there. Assembly names, package ids, namespaces and behaviour stay the same.

The issue names the projects. There is no CodeRabbit comment and no attached coding plan.

## Current State

Each package is a project folder with `src/` and `test/`, listed in `42.mono.slnx` under `/Platform/Storyteller/`. `AssemblyName` is the NuGet id. `IsPackable` is true on `src` and false on `test`.

| Folder today | Assembly and package id | Informational version at `05a6afcc` (Debug) |
|---|---|---|
| `src/Platform/Storyteller/Aspire.Hosting` | `42.Platform.Storyteller.Aspire.Hosting` | `0.10.2-alpha` |
| `src/Platform/Storyteller/Configuration` | `42.Platform.Storyteller.Configuration` | `0.10.4-alpha` |
| `src/Platform/Storyteller/Configuration.KeyVault` | `42.Platform.Storyteller.Configuration.KeyVault` | `0.10.0-alpha` |
| `src/Platform/Storyteller/ConfigurationFormats` | `42.Platform.Storyteller.ConfigurationFormats` | `0.10.5-alpha` |

Versions come from `src/Platform/version.json` (`0.10-alpha`, `inherit`, `hierarchicalVersion`). Height is per project directory, so `src` and `test` of the same package already differ. The repository root `version.json` is `0.1.0`. A project that leaves `src/Platform` without its own `version.json` falls back to `0.1.0`.

`Directory.Build.props` builds `ProjectStructurePath` by stripping every `src` segment from the project directory. A packable project packs `docs/$(ProjectStructurePath)/package-readme.md` when that file exists, and `RepositoryUrl` points at `src/$(ProjectStructurePath)/src`. The readmes are:

- `docs/Platform/Storyteller/Aspire.Hosting/package-readme.md`
- `docs/Platform/Storyteller/Configuration/package-readme.md`
- `docs/Platform/Storyteller/Configuration.KeyVault/package-readme.md`
- `docs/Platform/Storyteller/ConfigurationFormats/package-readme.md`

They link to the generic pages, which stay: `docs/Platform/Storyteller/configuration-provider.md`, `configuration-formats.md`, `aspire.md`.

Project references that cross the boundary:

- `Configuration/src` references `Abstractions.Annotations`, `ConfigurationFormats` and `Sdk.NSwag` (`..\..\…`).
- `Configuration.KeyVault/src` references `Configuration` (`..\..\Configuration\src`).
- `Aspire.Hosting/test` references `Abstractions.Annotations` and `Sdk.NSwag` for comparisons. The package itself references only `Aspire.Hosting`.
- `Configuration/test` and `ConfigurationFormats/test` embed `..\..\conformance\**\*.json` (`src/Platform/Storyteller/conformance`).
- `Configuration/test` references `src/Libraries/Testing/System.IO.Abstractions.Trace` through four `..` segments.
- `Hosting/src` references `Configuration`. Hosting is not in the issue and stays.
- `Api.Functions/src` and `src/Platform/Cli/src` reference `ConfigurationFormats`.

CI:

- `.github/workflows/storyteller-conformance.yml` path filters and the two `dotnet test --project` steps use `src/Platform/Storyteller/Configuration` and `ConfigurationFormats`.
- `.github/workflows/storyteller-sdk-release.yml` packs `ConfigurationFormats`, `Configuration` and `Configuration.KeyVault` from `src/Platform/Storyteller/$project/src`.

`src/Libraries/Directory.Build.proj` and `src/Platform/Directory.Build.proj` both traverse `**\*.*?proj` (Platform excludes `bin` and `obj`). `mrepo.json` already has a suppressed Libraries item. No new mrepo item is required. Historical specs under `docs/Platform/Storyteller/specs/` stay as written.

## Proposed Changes

Two phases. Assembly names, root namespaces, `InternalsVisibleTo`, public APIs and package ids do not change. `bin/` and `obj/` are build output and are not moved.

### 1. Move the projects and retarget the build

Destination, from the assembly name. `42.` is the repository prefix (it is not a folder). `Platform` and `Storyteller` are directories. The remainder, dots included, is the project folder, the same way `42.Utils.Configuration.Substitute.KeyVault` is `src/Libraries/Utils/Configuration.Substitute.KeyVault`:

| Assembly | New folder |
|---|---|
| `42.Platform.Storyteller.Aspire.Hosting` | `src/Libraries/Platform/Storyteller/Aspire.Hosting` |
| `42.Platform.Storyteller.Configuration` | `src/Libraries/Platform/Storyteller/Configuration` |
| `42.Platform.Storyteller.Configuration.KeyVault` | `src/Libraries/Platform/Storyteller/Configuration.KeyVault` |
| `42.Platform.Storyteller.ConfigurationFormats` | `src/Libraries/Platform/Storyteller/ConfigurationFormats` |

Move with `git mv` of the tracked files only, `src/` and `test/` included. Do not move Hosting, the conformance suite, `Sdk.NSwag`, `Abstractions.Annotations` or any other Storyteller project.

`42.mono.slnx`: remove the eight projects from `/Platform/Storyteller/` and add them under `/Libraries/Platform/Storyteller/`.

Project references after the move:

- Sibling references stay `..\..\…`: `Configuration.KeyVault` to `Configuration`, each test project to its own `src`.
- From a moved `src` or `test` directory to a project that stays in the product, five levels up to the repository `src` folder, then `Platform\Storyteller\…`:
  - `Configuration/src` to `Abstractions.Annotations` and `Sdk.NSwag`
  - `Aspire.Hosting/test` to the same two projects
- Conformance embed, from both moved test projects: `..\..\..\..\..\Platform\Storyteller\conformance\**\*.json`
- `Configuration/test` to the trace library: `..\..\..\..\Testing\System.IO.Abstractions.Trace\src\System.IO.Abstractions.Trace.csproj`
- `Hosting/src` to Configuration: `..\..\..\..\Libraries\Platform\Storyteller\Configuration\src\Configuration.csproj`
- `Api.Functions/src` to ConfigurationFormats: `..\..\..\..\Libraries\Platform\Storyteller\ConfigurationFormats\src\ConfigurationFormats.csproj`
- `Cli/src` to ConfigurationFormats: `..\..\..\Libraries\Platform\Storyteller\ConfigurationFormats\src\ConfigurationFormats.csproj`

Comments that name the old source path are updated in `DocumentRules.cs`, `DocumentFlattener.cs` and both `ConformanceFiles.cs` files. They name `src/Libraries/Platform/Storyteller/…` or, for the suite, keep `src/Platform/Storyteller/conformance`.

Package readmes move to the path `ProjectStructurePath` will compute (`src` segments stripped):

- `docs/Libraries/Platform/Storyteller/Aspire.Hosting/package-readme.md`
- `docs/Libraries/Platform/Storyteller/Configuration/package-readme.md`
- `docs/Libraries/Platform/Storyteller/Configuration.KeyVault/package-readme.md`
- `docs/Libraries/Platform/Storyteller/ConfigurationFormats/package-readme.md`

Their links to the generic pages stay. Those pages do not move.

Versioning. Add `src/Libraries/Platform/Storyteller/version.json` with the same body as `src/Platform/version.json` (`version` `0.10-alpha`, `inherit` true, `hierarchicalVersion` true), so the packages stay on the platform line. After the move, build each packable project and read `AssemblyInformationalVersion`. The pre-move Debug versions are in the table above. A version that is lower than that project's pre-move version is a NuGet downgrade. In that case add a `version.json` in that project directory whose `pathFilters` include `.` and the old `src/Platform/Storyteller/<folder>` path (relative to the new file), so the height still counts the history, and rebuild. The new version of each packable project must be greater than or equal to its pre-move version. Test projects are not packed; their height may change.

Build `Configuration`, `ConfigurationFormats`, `Configuration.KeyVault`, `Aspire.Hosting`, `Hosting`, `Api.Functions` and `Cli`. Run the four moved test projects.

### 2. Living documentation and CI

Update path strings in the documents that describe the current tree. Do not edit files under `docs/**/specs/`.

- `docs/Platform/Storyteller/configuration-provider.md`, `configuration-formats.md` and `aspire.md`: the code locations of the moved projects.
- `docs/Platform/Storyteller/conformance.md` and `src/Platform/Storyteller/conformance/README.md`: the `dotnet test --project` paths.
- `CLAUDE.md`: drop the four packages from the Storyteller product list and add them under Libraries, at `Platform/Storyteller/`. The generic Storyteller docs stay under `docs/Platform/Storyteller/`.
- `.github/workflows/storyteller-conformance.yml`: path filters for `Configuration` and `ConfigurationFormats` point at `src/Libraries/Platform/Storyteller/…`, and both test steps use those projects. Add `src/Libraries/Platform/Storyteller/version.json` next to the existing `src/Platform/version.json` filter. Keep the filters for the conformance suite, `Abstractions.Annotations`, `KeyValues.ClientEncryption` and `Sdk.NSwag`.
- `.github/workflows/storyteller-sdk-release.yml`: pack `ConfigurationFormats`, `Configuration` and `Configuration.KeyVault` from `src/Libraries/Platform/Storyteller/$project/src`. The other three packages in that loop stay under `src/Platform/Storyteller`.

`docs/42for.net` links the generic pages by their GitHub paths. Those pages stay, so the site does not change.
