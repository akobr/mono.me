# Storyteller configuration action

A composite GitHub Action that reads the resolved configuration of an annotation from the 2S Platform and exports it into the environment of the job, into a file, or both. It installs [`sform`](https://42for.net/cli/sform) and runs `sform story config <key> --resolved` once, with `--github-env` or `--mask`, so the names, the masks, the heredocs and the refusals are those of `sform` and its tests ([Export to GitHub Actions](https://42for.net/cli/sform#export-to-github-actions)). The public page is [GitHub Action](https://42for.net/cli/github-action).

- **Spec:** [Flat Output Formats for Configurations](../../../docs/Platform/Storyteller/specs/2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md), sections 8 and 9.
- **Runners:** Linux, macOS and Windows; every step runs in `bash` (Git Bash on Windows).

## Use

```yaml
# The later steps read APP_DB__HOST, APP_DB__PASSWORD, …
- uses: akobr/mono.me/src/Platform/GitHubAction@<commit>
  with:
    key: exe.northwind.invoicing.production
    export-env: true
    env-prefix: APP_
    cli-version: 0.8.115-alpha-gd665130ae2
  env:
    STORYTELLER_ENDPOINT: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
- run: dotnet run --project src/Billing   # AddEnvironmentVariables("APP_")
```

The action has no release tags yet: pin a commit of this repository. A workflow of this repository uses `./src/Platform/GitHubAction` after `actions/checkout`. Until a `sform` with these options is on NuGet, the workflow packs it first, see [Until sform is published](#until-sform-is-published).

## Inputs

| Input | Default | Meaning |
|---|---|---|
| `key` | required | The annotation key of the configuration |
| `project` | | `<organization>.<project>`, passed as `STORYTELLER_PROJECT` |
| `view` | | Passed as `STORYTELLER_VIEW` |
| `endpoint` | | The address of the API, `/api` included, passed as `STORYTELLER_ENDPOINT` |
| `client-id` | | Passed as `STORYTELLER_CLIENT_ID`; a GUID in a job with `permissions: id-token: write` signs in with the job's OIDC token |
| `export-env` | `false` | Export the resolved configuration into `GITHUB_ENV` (`--github-env`) |
| `env-prefix` | | `--env-prefix`, for example `APP_`; with `export-env`, and for `output-file` in the `env` format |
| `allow-unprefixed` | `false` | `--allow-unprefixed`: let `export-env` write the names of the configuration without a prefix |
| `mask` | `bound` | `--mask`: `bound` masks the values that bindings produced, `all` every string and number |
| `output-file` | | Also write the resolved configuration to this file (`--export <file> --format <format> --content-only`); best under `$RUNNER_TEMP`. A relative path is relative to the workspace |
| `format` | `env` | The format of `output-file`: `env`, `flat`, `properties`, `yaml` or `json` (the content alone) |
| `cli-version` | required | The exact version of `42.Platform.Cli` to install |
| `cli-source` | | An additional NuGet source of the tool, a URL or an absolute path, for example the folder the workflow packed `sform` into |

`project`, `view`, `endpoint` and `client-id` are exported as `STORYTELLER_*` only when they are not empty, so an empty input keeps the variable the job sets. The action has no output.

## Steps

1. **Check the inputs.** `key` and `cli-version` are required, `cli-version` is an exact version (no range, no path), `export-env` and `allow-unprefixed` are `true` or `false`, and `export-env`, `output-file` or both are set. Otherwise the step fails with a message that names the input, before anything is installed. An `env-prefix` that neither `export-env` nor an `env` file uses is a warning.
2. **Look for .NET 10.** When `dotnet --list-sdks` lists an SDK `10.`, the runner keeps its .NET, and `sform` gets its root as `DOTNET_ROOT` when the job has none. Otherwise `actions/setup-dotnet@v6` installs `10.0.x`. The check is on the SDK, not the runtime: an older SDK cannot install a `net10.0` tool and a runtime alone installs nothing, while an SDK 10 brings the runtime `sform` runs on.
3. **Install sform.** `dotnet tool install 42.Platform.Cli --version <cli-version> --tool-path "$RUNNER_TEMP/storyteller-action/sform-<cli-version>"`, with `--add-source <cli-source>` when it is set and never `--prerelease`. It runs in `$RUNNER_TEMP`, so the `nuget.config` and `global.json` of the repository are not read. A later use of the action in the job with the same version finds the tool in that folder.
4. **Read the configuration.** One run of `sform story config <key> --resolved --mask <mask>`, with `--github-env` when `export-env` is true, `--export <output-file> --format <format> --content-only` when `output-file` is set, and `--env-prefix` and `--allow-unprefixed`. One resolved and one effective read give the variables, the masks and the file. `sform` writes the `::add-mask::` commands first, then `GITHUB_ENV`, then the file, and a summary to standard error: the names of the variables and the number of masked values, never a value.

Inputs never reach a script through `${{ inputs.* }}`: every step gets them in `env:` under private names (`SA_KEY`, `SA_PROJECT`, …), quotes them, and builds the arguments of `sform` as a bash array. The step fails with the exit code of `sform`, `2` for a configuration or options it refuses (see [Read a configuration](https://42for.net/cli/sform#read-a-configuration)).

## Credentials

Credentials are not inputs. `sform` reads them from the environment of the job or of the step, as described in [Sign in from a pipeline](https://42for.net/cli/sform#sign-in-from-a-pipeline): `STORYTELLER_API_KEY`, client credentials, a certificate (`STORYTELLER_CLIENT_CERTIFICATE`, a `.pfx` file the workflow writes under `$RUNNER_TEMP` from a secret, and `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD`), or a federated machine: `client-id` with `permissions: id-token: write`.

## Secrets need more than a credential

- The machine needs `Secrets.Read` (`sform account machine create -r --scope secrets-read`).
- Under the default policy of a project (`RequireMtlsForSecretDelivery`), only a certificate machine over mTLS receives secret values: the API must sit behind a front end that forwards the client certificate (`MachineAuth:IsCertificateHeaderFromClientAllowed`). An API-key, client-credentials or federated machine receives them only when an Administrator sets the policy to false (`sform story kv policy set --require-mtls false`).
- Otherwise the API withholds the secrets (`X-2S-Secrets-Withheld`) or returns the expressions of the secret bindings, and the action fails with exit code `2` and writes nothing. A machine without `Secrets.Read` can read a configuration without secret bindings.

## What later steps see

Every variable in `GITHUB_ENV` reaches every later step of the job, actions of other authors included, and the log prints it in the `env:` block of every such step. Only masked values are hidden there, and a mask hides a value in the log, not from a step. `mask: bound` is a heuristic: it masks the values that bindings produced, also public ones, and cannot recognise a secret stored as a plain value in the configuration. So export only what the job needs: a prefix keeps the names apart, `mask: all` masks every string and number, and `output-file` under `$RUNNER_TEMP` without `export-env` keeps the values out of the environment of the other steps, for the one step that reads the file.

## Names

The names come from the configuration, so whoever edits the configuration could steer the later steps (`PATH`, `NODE_OPTIONS`, `LD_PRELOAD`, `RESTORESOURCES`). `export-env` therefore needs `env-prefix`, or `allow-unprefixed: true` for a job where the people who edit the configuration may steer the job anyway. Either way, `sform` refuses names that the runner reserves or that change how later steps or their tools run, with and without the prefix, without regard to case: among them `PATH`, `HOME`, `BASH_ENV`, `NODE_OPTIONS`, `CLASSPATH`, `HTTP_PROXY`, `RESTORESOURCES`, and every name that starts with `GITHUB_`, `RUNNER_`, `ACTIONS_`, `LD_`, `DOTNET_`, `NUGET_`, `MSBUILD`, `GIT_` or `STORYTELLER_`. The [whole list](https://42for.net/cli/sform#export-to-github-actions) is in the `sform` documentation. No such list is complete, which is why the prefix is required. `sform` also refuses a value with U+0000, a variable longer than 32,767 characters and more than 512 KiB of variables.

## Until sform is published

`42.Platform.Cli` on nuget.org is older than these options. Until a release has them, the workflow packs `sform` from this repository and installs it through `cli-source`:

```yaml
steps:
  - uses: actions/checkout@v4
    with:
      repository: akobr/mono.me
      ref: <commit>
      path: mono.me
      fetch-depth: 0   # Nerdbank.GitVersioning computes the version from the history
  - uses: actions/setup-dotnet@v6
    with:
      dotnet-version: 10.0.x
  - id: sform
    shell: bash
    run: |
      dotnet pack mono.me/src/Platform/Cli/src -o "$RUNNER_TEMP/sform"
      package=$(basename "$RUNNER_TEMP"/sform/42.Platform.Cli.*.nupkg .nupkg)
      echo "version=${package#42.Platform.Cli.}" >> "$GITHUB_OUTPUT"
  - uses: akobr/mono.me/src/Platform/GitHubAction@<commit>
    with:
      key: exe.northwind.invoicing.production
      export-env: true
      env-prefix: APP_
      cli-version: ${{ steps.sform.outputs.version }}
      cli-source: ${{ runner.temp }}/sform
    env:
      STORYTELLER_ENDPOINT: https://storyteller.example.com/api
      STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
```

A workflow of this repository checks out the repository itself (`fetch-depth: 0`), packs `src/Platform/Cli/src` and uses `./src/Platform/GitHubAction`.

## .NET on the runner

`actions/setup-dotnet` runs only when the runner has no .NET 10 SDK (a runner with only the .NET 10 runtime, or with an older SDK, gets it too), and then it changes `DOTNET_ROOT` and `PATH` for the rest of the job: the later steps run the .NET it installed. A job that needs another .NET sets it up itself, before the action or after it.

## Checks

There is no workflow for the action (spec section 9): nothing in GitHub builds or tests .NET yet, and there is no Storyteller instance for a smoke test. The logic that matters for security (names, masks, heredocs, refusals) is in `sform`, with its unit tests (`src/Platform/Cli/test`: `ConfigGetCommandTests`, `ConfigurationMasksTests`, `GitHubEnvironmentFileTests`). The review of phase 5 records the local checks: `action.yml` parses, and its scripts ran in bash against a packed `sform` and a stub API, with stub `RUNNER_TEMP`, `GITHUB_ENV` and `GITHUB_OUTPUT`.
