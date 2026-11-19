# Developer Guide of the 2S Platform on 42for.net

## Problem

The 2S Platform is about to ship its first packages to developers: the .NET configuration provider, Hosting, the Aspire integration and the packages they bring, and the TypeScript SDK. A developer who meets the platform on 42for.net finds:

- the concept pages of `docs/42for.net/platform/` (annotations, configuration, modeling, using the platform), which explain the model well but address the HTTP API, not the packages;
- one paragraph in `using-the-platform.md` ("A .NET service does not have to write that loop…") that names three packages and links to GitHub;
- the reference documentation of each package in `docs/Platform/Storyteller/` (`configuration-provider.md`, `hosting.md`, `aspire.md`, `configuration-formats.md`) and in the readmes of the SDKs. These are precise and complete, and long: the provider's page alone has 644 lines. They answer "what exactly happens when", not "why would I want this, and how do I start".

Nothing on the site tells a developer which package to take, what it gives them, or how to get from nothing to a running service in a few minutes. The owner asked for documentation that is clear, short, easy to understand, enjoyable and a little funny, as a new sub-section of the 2S Platform part of the site, collapsed by default.

## Current State

- `docs/42for.net/.vitepress/config.mjs`: one sidebar for the whole site. The group `2S Platform` (lines 79–103) lists the concept pages, a nested `Examples` group with `collapsed: false`, `Live demo` and `Road map`. VitePress 2 alpha, the tabs plugin (`:::tabs` / `== name`) and Mermaid fences (rendered by `.vitepress/theme/Mermaid.vue`) are available.
- `docs/42for.net/platform/documentation/` does not exist.
- The packages that `.github/workflows/Platform.Storyteller.Sdk-release.yml` packs and releases together:

  | Package | Project | Reference documentation |
  | --- | --- | --- |
  | `42.Platform.Storyteller.Configuration` | `src/Libraries/Platform/Storyteller/Configuration` | `docs/Platform/Storyteller/configuration-provider.md` |
  | `42.Platform.Storyteller.Configuration.KeyVault` | `src/Libraries/Platform/Storyteller/Configuration.KeyVault` | the same, section "The Key Vault resolver" |
  | `42.Platform.Storyteller.Hosting` | `src/Libraries/Platform/Storyteller/Hosting` | `docs/Platform/Storyteller/hosting.md` |
  | `42.Platform.Storyteller.Aspire.Hosting` | `src/Libraries/Platform/Storyteller/Aspire.Hosting` | `docs/Platform/Storyteller/aspire.md` |
  | `42.Platform.Storyteller.ConfigurationFormats` | `src/Libraries/Platform/Storyteller/ConfigurationFormats` | `docs/Platform/Storyteller/configuration-formats.md` |
  | `42.Platform.Storyteller.Sdk.NSwag` | `src/Platform/Storyteller/Sdk.NSwag` | `src/Platform/Storyteller/Sdk.NSwag/README.md` |
  | `42.Platform.Storyteller.Abstractions.Annotations` | `src/Platform/Storyteller/Abstractions.Annotations` | none (the models, a dependency) |
  | `42.Platform.Storyteller.KeyValues.ClientEncryption` | `src/Platform/Storyteller/KeyValues.ClientEncryption` | the C# SDK readme, "Client-side encryption" |
  | `@42for.net/2splatform.sdk` (npm) | `src/Platform/Storyteller/sdk.typescript` | `src/Platform/Storyteller/sdk.typescript/README.md` |

- Beside them, developers use the `sform` tool (`42.Platform.Cli`, documented on `docs/42for.net/cli/sform.md`), the GitHub Action (`docs/42for.net/cli/github-action.md`) and the MCP server (`docs/Platform/Storyteller/mcp.md`). `2sInitDb` (`DbCreator`) is packable too, but only those who host the platform need it.
- The NuGet libraries are versioned `0.10-alpha` (`src/Libraries/Platform/Storyteller/version.json`) and are not on nuget.org yet; nuget.org has old `0.8` prereleases of `42.Platform.Cli`, npm an old `0.8.2` of the TypeScript SDK.
- `docs/42for.net/platform/introduction.md` and `using-the-platform.md` do not point to any developer guide.

## Proposed Changes

### 1. A collapsed sidebar group "Developer guide"

In `config.mjs`, a nested group inside `2S Platform`, after `Examples` and before `Live demo`, with `collapsed: true`:

| Page | File |
| --- | --- |
| Start here | `platform/documentation/index.md` |
| Quick start | `platform/documentation/quick-start.md` |
| Configuration provider | `platform/documentation/configuration.md` |
| Key Vault secrets | `platform/documentation/key-vault.md` |
| Hosting | `platform/documentation/hosting.md` |
| Aspire | `platform/documentation/aspire.md` |
| Configuration formats | `platform/documentation/formats.md` |
| C# SDK | `platform/documentation/csharp-sdk.md` |
| TypeScript SDK | `platform/documentation/typescript-sdk.md` |
| sform & CI | `platform/documentation/cli.md` |
| AI assistants (MCP) | `platform/documentation/mcp.md` |
| Cheat sheet | `platform/documentation/cheat-sheet.md` |

### 2. The voice

- **Short.** A package page fits on a few screens: what it is (one sentence and a joke), why you want it (three to five benefits), install, the smallest code that works, the two or three things you will want next, "good to know" and a link to the reference. Details stay in the reference documentation on GitHub; the guide links there instead of copying it.
- **Plain words.** The concepts of the platform are introduced once, on the start page, in a cheat sheet, and every other page uses the same words.
- **Playful, never at the expense of accuracy.** Jokes live in titles, intros and tips; every code sample, option, variable and default is taken from the code's reference documentation as it is on `main` today.
- **VitePress building blocks:** `::: tip`, `::: info`, `::: warning`, `::: details` for "under the hood" parts, `:::tabs` for the .NET CLI and the project file, and Mermaid for the one "which package do I need" decision diagram.

### 3. The start page (`index.md`)

- 2S in one minute: the problem (configuration copied per customer and environment), the idea (describe the product once as annotations, store each difference once, read the merge).
- The seven annotations as a cheat sheet table, and the anatomy of a full key, `@house.main.default.exe.northwind.invoicing.production`.
- "Which package do I need?": a Mermaid flowchart and a table.
- The package map: every package of the table above, with its one-line purpose and its page. `Abstractions.Annotations` and `KeyValues.ClientEncryption` are listed as packages that come along. `2sInitDb` is mentioned for self-hosters.
- The preview status: prereleases, `--prerelease`, and `dotnet pack` from the repository until a package reaches nuget.org.

### 4. The quick start (`quick-start.md`)

From nothing to `IOptionsMonitor<T>` in five steps: install `sform` and sign in; create an execution with its parents (`sform story set` or the admin UI); write the configuration of the responsibility and the subject; create a machine restricted to the execution; add the provider and read the options. Each step names what the reader just achieved.

### 5. One page per package

- `configuration.md`: `AddStoryteller()`, the variables, `IOptionsMonitor<T>`, the read modes, what happens when the platform is down (memory, the last-known-good cache, `Optional`), the refresher (`TryRefreshAsync`, `SwitchKeyAsync`, `Status`) for a health check, `IStorytellerDocuments` for jobs.
- `key-vault.md`: references (`kv://vault/name`), resolution with the application's own identity, `AllowedVaults`, rotation, the trust model in one tip.
- `hosting.md`: who am I (`IExecutionContext`, the subscription status), the worker for every subject (`AddSubscriptions`, `AddSubjectOptions`, `WatchAsync`), the catalog (`IStorytellerCatalog`, targets), and the machine each needs.
- `aspire.md`: the resource, credentials as parameters, `WithReference`, `WithExecution`, `WithStorytellerKey`.
- `formats.md`: one document, four formats (`flat`, `properties`, `env`, `yaml`), from the library, the API (`?format=`) and `sform`.
- `csharp-sdk.md`: `AddStorytellerSdk`, machine credentials, conditional reads, versioned annotation writes, resilience, client-side encryption; the two packages that come along.
- `typescript-sdk.md`: the entry points, `configureStoryteller`, errors, Vue Query, MSW mocks, conditional reads, key/value helpers and `./hyok`.
- `cli.md`: `sform` in ten commands, machines for pipelines, and the GitHub Action, linking to the CLI pages of the site.
- `mcp.md`: what the MCP server gives an assistant, how it signs a person in, what it can and cannot do with secrets, and how to connect a client. The address of a hosted server is not named, because it is not announced yet.
- `cheat-sheet.md`: the `STORYTELLER_*` variables, the type codes, the machine scopes, the roles that matter for developers, and "it does not work" answers keyed by symptom (`401`, `403 AnnotationRestricted`, `X-2S-Secrets-Withheld`, `@…` values that stay unresolved, a degraded start).

### 6. Links from the existing pages

- `platform/introduction.md`: a short "For developers" section that points to the guide.
- `platform/using-the-platform.md`: the paragraph about the .NET packages points to the guide's pages next to the GitHub reference.

### 7. Verification

- `npm ci` and `npm run build` in `docs/42for.net`: the build fails on dead links, so every link of the guide is checked.
- `npm run preview`, and a look at the start page, the collapsed group and one package page in a browser.
