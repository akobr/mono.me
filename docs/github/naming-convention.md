# GitHub Naming Convention

The mono repository holds many projects, so its GitHub workflows, their YAML files and its GitHub environments follow
the same dotted structure as the source code: the workspace (area) and the project, as in the assembly names
`42.<Area>.<SubProject>` (see [CLAUDE.md](../../CLAUDE.md)).

## The dotted path

The dotted path of a workspace or project is its path under `src/` with `/` replaced by `.`, written in the casing of
its assembly name:

| Source folder | Dotted path |
|---|---|
| `src/Platform` | `Platform` |
| `src/Platform/Storyteller` | `Platform.Storyteller` |
| `src/Platform/Storyteller/Api.Functions` | `Platform.Storyteller.Api.Functions` |
| `src/Platform/ui.admin` | `Platform.UI.Admin` |
| `src/Platform/Cli` | `Platform.Cli` |

A workflow, file or environment belongs to the narrowest workspace or project it serves: a workflow of a single
project uses the project's path, a workflow that spans a whole area uses the area's path.

## Workflows

| Item | Format | Example |
|---|---|---|
| Workflow name (`name:`) | `<dotted-path>[-<suffix>]` | `Platform.UI.Admin`, `Platform.Storyteller-conformance` |
| YAML file | `<dotted-path>[-<suffix>].yml` | `Platform.UI.Admin.yml`, `Platform.Storyteller.Sdk-release.yml` |

- Workflows and their files do not carry the `42` prefix: it is used only in environment names.
- The file name is the workflow name with `.yml`, so a run in the Actions list maps to its file at a glance.
- The dotted path names the project the workflow serves, in Pascal case as in the assembly names; it may point to a
  test project (`Platform.Cli.UnitTests`, `Platform.UI.Admin.AutomationTests`) or a folder outside `src/`
  (`Docs.42for.net` for `docs/42for.net`).
- The optional `-<suffix>` after a dash is a custom description or purpose of the workflow, in lower case
  (`-release`, `-conformance`, `-deploy`). Use it when the path alone does not say what the workflow does, or when a
  project has more than one workflow.

### Current workflows

| File | Serves |
|---|---|
| `Docs.42for.net.yml` | the docs site `docs/42for.net` |
| `Platform.UI.Admin.yml` | the admin UI |
| `Platform.UI.Admin.AutomationTests.yml` | the admin UI automation tests |
| `Platform.Cli.UnitTests.yml` | the CLI unit tests and the automation self checks |
| `Platform.Cli.AutomationTests.yml` | the CLI automation tests |
| `Platform.Deployment.yml` | the Azure deployment templates |
| `Platform.Storyteller-conformance.yml` | the Storyteller conformance suite |
| `Platform.Storyteller.CosmosDb.yml` | the Cosmos DB suites of the Storyteller backend |
| `Platform.Storyteller.Sdk-release.yml` | the release of the Storyteller SDKs |
| `Platform.GitHubAction-release.yml` | the release of the GitHub Action |

## Environments

| Format | Examples |
|---|---|
| `<environment>.42.<dotted-path>` | `prod.42.Platform`, `dev.42.Platform.Storyteller.Api.Functions` |

- `<environment>` is the stage in lower case: `dev`, `test`, `stage`, `prod`.
- The dotted path is the workspace or project whose deployment or secrets the environment holds. An environment shared
  by a whole area uses the area's path (`prod.42.Platform`); one that only a single project needs uses the project's
  path.
- The stage comes first, so the environments of one stage sort together in the repository settings.

## Applying the convention

The workflows follow this convention (see [workflows.md](workflows.md)); the environments are renamed when they are
touched. A rename should keep in mind that:

- renaming a workflow file starts a new run history in the Actions list, and every `uses: ./.github/workflows/<file>`
  of a calling workflow and every status badge must follow the new name;
- renaming an environment means recreating it with its secrets, variables, protection rules and deployment branch
  policy, and updating every `environment:` that refers to it (and any federated credential whose subject names it,
  e.g. `repo:<owner>/<repo>:environment:prod.42.Platform`).
