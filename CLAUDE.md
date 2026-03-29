# CLAUDE.md — mono.me (42for.net)

Personal mono-repository by Ales Kobr. All projects live under `src/`, documentation under `docs/`. The solution file is `42.mono.slnx`.

---

## Tech Stack

- **Primary language:** C# on .NET 10.0 (`net10.0`), `LangVersion: latest`, nullable enabled
- **Build system:** MSBuild with `Microsoft.Build.Traversal` SDK (v4.1.0)
- **Central package management:** `Directory.Packages.props` — all NuGet versions live here; projects reference packages without versions
- **Versioning:** Nerdbank.GitVersioning (`version.json` at root and per-project). Releases only from `main`
- **Code style:** StyleCop.Analyzers (configured via `stylecop.json`)
- **Test frameworks:** xUnit (primary), FluentAssertions, Shouldly, Moq, Testcontainers, Bogus
- **TypeScript (ui.admin):** Vue 3 + Vite + TypeScript
- **TypeScript (sdk.typescript):** Plain TypeScript with `tsc`
- **CLI framework:** McMaster.Extensions.CommandLineUtils
- **DI / Hosting:** Microsoft.Extensions.Hosting (generic host pattern)

---

## Repository Structure

```
mono.me.second/
├── 42.mono.slnx              # Solution file (all projects listed here)
├── Directory.Build.props     # Global MSBuild props (repo root, StyleCop, NuGet packaging defaults)
├── Directory.Packages.props  # Central NuGet version catalog (keep alphabetical, no grouping)
├── global.json               # .NET SDK pin (10.0.201), MSBuild SDK versions
├── mrepo.json                # Monorepo tooling config (items, types, release rules)
├── version.json              # Root versioning (Nerdbank.GitVersioning)
├── stylecop.json             # StyleCop rules (usings outside namespace, system-first)
├── nuget.config              # NuGet sources: NuGet.org + local .artifacts
├── docs/                     # Documentation (VitePress site at docs/42for.net)
└── src/
    ├── Platform/             # 2S Platform (main product)
    ├── Monorepo/             # mrepo CLI tooling
    ├── Crumble/              # Orleans-based actor framework
    ├── c0ded0c/              # Code documentation/analysis tool
    ├── Texo/                 # Text-based UI framework
    ├── Libraries/            # Reusable NuGet libraries
    ├── Games/                # Cetris (Tetris for CLI)
    └── tHolistic/            # Holistic test runner
```

---

## Project Areas

### Platform (`src/Platform/`)
The flagship **2S Platform** — a serverless annotation/metadata platform.

| Sub-project | Purpose |
|---|---|
| `Cli/` | `sform` dotnet tool — manages/controls the 2S platform |
| `Sdk/` | C# SDK (RestSharp + Polly based HTTP client) |
| `sdk.typescript/` | TypeScript SDK (`@42for.net/2splatform.sdk`) |
| `ui.admin/` | Vue 3 admin UI (uses the TypeScript SDK) |
| `Aspire.Host/` | .NET Aspire orchestrator for local dev |
| `Aspire.ServiceDefaults/` | Shared Aspire service defaults |
| `Storyteller/` | Core annotation service (see below) |
| `Scheduler/` | Azure Functions scheduler |
| `Supervisor/` | Azure Functions supervisor |
| `Examples/CustomClientApp/` | Example Platform client |

**Storyteller** is the heart of the Platform:
- `Abstractions.Access/` — access roles, machine scopes
- `Abstractions.Annotations/` — annotation models, AnnotationKey, FullKey, AnnotationType
- `Backend.Core/` — domain logic (no infrastructure)
- `Backend.CosmosDb/` — CosmosDB persistence
- `Backend.AzureAd/` — Azure AD/Entra authentication
- `Backend.Keycloak/` — Keycloak authentication
- `Binding.Abstractions/` / `Binding.Core/` / `Binding.Azure.KeyVault/` — configuration bindings
- `Api.Functions/` — Azure Functions HTTP API (isolated worker model)
- `Api.Web/` — ASP.NET Core Web API
- `DbCreator/` — CosmosDB schema creation tool

### Monorepo (`src/Monorepo/`)
The `mrepo` dotnet tool for monorepo management (build, release, versioning, conventional commits).
- `Cli/` — `mrepo` CLI entry point
- `Repo.Generator/` — scaffold new repos/projects

### Crumble (`src/Crumble/`)
Orleans-based actor (grain) framework with Azure Storage backing.
- `Abstractions/` — `IFlowClient`, `CrumbAttribute`, action interfaces
- `Runtime.Orleans/` — Orleans grain runtime implementation
- `Runtime.Core/` — core runtime abstractions
- `Volume.*/` / `Actions.Volume.*/` — blob storage volumes
- `Actions.Message.*/` — message queue actions (Azure Storage Queues)
- `Actions.Time.*/` — timer/reminder actions
- `Journal.AzureStorageTables/` — event journaling
- `Playground/` — local Aspire-hosted demo

### c0ded0c (`src/c0ded0c/`)
Code documentation and analysis tool (`codedoc` CLI).
- `Core/` — engine, mechanism, plugin/middleware pipeline
- `Core.Yaml/` — YAML configuration
- `MsBuild/` — MSBuild project introspection
- `PlantUml/` — PlantUML diagram generation
- `Cli/` — `codedoc` entry point

### Libraries (`src/Libraries/`)
Reusable NuGet packages (suppressed as top-level mrepo item, referenced internally):
- `CLI/Toolkit/` — `BaseCommand`, `IAsyncCommand`, output helpers for CLI apps
- `Functional/Monads/` — `IMonad<T>`, `IFunctor<T>`, `IBifunctor<T,U>`, Result/Option types
- `Functional/Monads.Trees/` — tree monads
- `Roslyn/Compose/` — Roslyn code composition helpers
- `Structures/Graph/` — graph data structures (QuikGraph-based)
- `Testing/System.IO.Abstractions.Trace/` — tracing wrapper for IFileSystem
- `Utils/Async/` — `AsyncLazy<T>`
- `Utils/Configuration.Substitute/` — configuration token substitution
- `Utils/Configuration.Substitute.KeyVault/` — Azure Key Vault substitution

### Other
- `Texo/Core.Markdown/` — Markdown rendering for text UIs
- `Games/Cetris/` — Tetris clone for the terminal
- `tHolistic/` — holistic/integration test runner

---

## Namespace & Assembly Conventions

- Assembly names: `42.<Area>.<SubProject>` (e.g. `42.Platform.Storyteller.Backend.Core`)
- Root namespaces: `_42.<Area>.<SubProject>` (underscore prefix because `42` is not a valid C# identifier start)
- Test projects: suffix `.UnitTests` or `.IntegrationTests`
- Test projects are NOT packable (`IsPackable=false`, `IsTestProject=true`)

---

## Project File Conventions

- All projects target `net10.0` with `Nullable>enable</Nullable>`
- Source lives in `<project>/src/`, tests in `<project>/test/`
- **Never** specify package versions in `.csproj` — all versions are in `Directory.Packages.props`
- Packages in `Directory.Packages.props` are kept in **alphabetical order, no grouping**
- Packable projects set `<IsPackable>true</IsPackable>` and can set `<PackAsTool>true</PackAsTool>`
- Output goes to `.artifacts/` at repo root
- `PackageProjectUrl`, `PackageLicenseExpression`, `Authors`, `PackageIcon` are set globally via `Directory.Build.props`

---

## Build Commands

```bash
# Build entire solution
dotnet build src

# Build a specific project area
dotnet build src/Platform

# Run all tests
dotnet test src

# Pack NuGet packages (debug)
dotnet pack src

# Pack for release/CI
dotnet pack src -c RELEASE /p:ContinuousIntegrationBuild=true

# Restore packages
dotnet restore src

# Clean
dotnet clean src
```

For the TypeScript projects:
```bash
# TypeScript SDK
cd src/Platform/sdk.typescript/src
npm run build

# Admin UI
cd src/Platform/ui.admin/src
npm run dev    # development server
npm run build  # production build
npm run lint   # ESLint fix
```

---

## Development Workflow

### Local Development with Aspire
Use the Aspire host for Platform development:
```bash
dotnet run --project src/Platform/Aspire.Host/src
```
This orchestrates: Azure Functions API, CosmosDB emulator, Azure Storage emulator.

### CLI Tools (dotnet global tools)
- `mrepo` — monorepo management (`src/Monorepo/Cli`)
- `sform` — 2S Platform management (`src/Platform/Cli`)
- `codedoc` — code documentation (`src/c0ded0c/Cli`)

### Conventional Commits
The repo uses conventional commits (enforced by `mrepo`):
| Type | Release bump |
|---|---|
| `feat` | minor |
| `fix`, `perf`, `refactor` | patch |
| `style`, `test`, `build`, `ci`, `docs`, `chore` | harmless (no bump) |
| breaking change | major |

Branch `feat/claude/*` is the convention for AI-assisted feature branches.

### Versioning
- `version.json` at root sets the base version (`0.1.0`)
- Each sub-project may have its own `version.json` to override
- Public releases only from `main` branch
- CI builds include commit metadata in non-public release versions

---

## Testing

- Framework: **xUnit** with `FluentAssertions` (pinned to `[7.2.0]`)
- Test containers: `Testcontainers.CosmosDb`, `Testcontainers.Azurite`, `Testcontainers.PostgreSql`
- Fake data: `Bogus`, `NBuilder`
- Mocking: `Moq`
- File system abstraction: `TestableIO.System.IO.Abstractions` (use `IFileSystem` injection — never `System.IO` directly)

Run a specific test project:
```bash
dotnet test src/Platform/Storyteller/Backend.CosmosDb/test
```

---

## Code Style

StyleCop is enabled by default (opt-out with `<EnableStyleCop>false</EnableStyleCop>`):
- `using` directives go **outside** the namespace
- System usings come **first**
- Documentation is NOT required for internal/exposed elements (see `stylecop.json`)
- Suppression file: `src/.stylecop/GlobalStylecopSuppressions.cs`

---

## CI/CD

- GitHub Actions workflow: `.github/workflows/azure-static-web-apps-victorious-cliff-019dda503.yml`
  - Triggers on push/PR to `main` for `docs/42for.net/**`
  - Deploys the VitePress docs site to Azure Static Web Apps
- NuGet local feed: `.artifacts/` directory (configured in `nuget.config`)
- Issues tracked in ClickUp (`https://app.clickup.com/t/{id}`)

---

## Key Architectural Patterns

1. **Layered abstractions**: Each area has `Abstractions.*` projects that define interfaces — implementations are in separate projects (`Backend.CosmosDb`, `Backend.AzureAd`, etc.)
2. **Generic Host everywhere**: Both CLIs and Azure Functions use `Microsoft.Extensions.Hosting` with DI
3. **CLI pattern**: Commands extend `BaseCommand` / `IAsyncCommand` from `Libraries/CLI/Toolkit`
4. **Middleware pipeline in Azure Functions**: `ExceptionHandlingMiddleware`, auth middleware
5. **Configuration bindings**: `Binding.*` projects manage configuration injection into the domain (Key Vault, Azure)
6. **Actor model (Crumble)**: Orleans grains implement `ICrumbGrain`, registered via `CrumbToGrainRegistry`

---

## Important Notes for AI Assistants

- The `_42` namespace prefix is intentional — `42` cannot start a C# namespace
- `Directory.Packages.props` is the single source of truth for package versions — never add version attributes to `<PackageReference>` in `.csproj` files
- StyleCop is enabled globally; respect the `using` placement rules
- Test projects use `Testcontainers` for integration tests (real CosmosDB, Azurite) — do not mock infrastructure unless absolutely necessary
- The `mrepo.json` `custom.dotnetVersion` field is informational (set to 9, but actual target is `net10.0`)
- Build artifacts go to `.artifacts/` — this is the local NuGet feed source
