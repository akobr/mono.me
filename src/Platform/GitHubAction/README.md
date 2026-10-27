# Storyteller configuration action

A composite GitHub Action that reads the resolved configuration of an annotation from the 2S Platform and exports it into the environment of the job, into a file, or both. It installs [`sform`](https://42for.net/cli/sform) and runs `sform story config <key> --resolved` once, with `--github-env` or `--mask`, so the names, the masks, the heredocs and the refusals are those of `sform` and its tests ([Export to GitHub Actions](https://42for.net/cli/sform#export-to-github-actions)). The public page is [GitHub Action](https://42for.net/cli/github-action).

- **Specs:** [Flat Output Formats for Configurations](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/specs/2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md), sections 8 and 9; [Publishing the GitHub Action and Secrets in CI](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/specs/2026-10-27%20Publishing%20the%20GitHub%20Action%20and%20Secrets%20in%20CI.md).
- **Source:** [`src/Platform/GitHubAction`](https://github.com/akobr/mono.me/tree/main/src/Platform/GitHubAction) of the monorepo `akobr/mono.me`. Releases are copied into a repository of their own, see [Releasing](#releasing).
- **Runners:** Linux, macOS and Windows; every step runs in `bash` (Git Bash on Windows).
- **Licence:** MIT, in the file `LICENSE` next to this README.

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
    STORYTELLER_CLIENT_CERTIFICATE: ${{ runner.temp }}/storyteller.pfx   # a certificate machine, see Credentials
    STORYTELLER_CLIENT_CERTIFICATE_PASSWORD: ${{ secrets.STORYTELLER_CERTIFICATE_PASSWORD }}
- run: dotnet run --project src/Billing   # AddEnvironmentVariables("APP_")
```

The action is released into a repository of its own, listed on GitHub Marketplace, with a tag `vX.Y.Z` per release and a major tag `vX` that moves with them (see [Releasing](#releasing)). Once the first release is out, use it from there as `<owner>/<name>@v1`; the owner and the name of that repository are not decided yet. Until then, and for changes that are not released, pin a commit of the monorepo, `akobr/mono.me/src/Platform/GitHubAction@<commit>`, as the examples here do. A workflow of the monorepo uses `./src/Platform/GitHubAction` after `actions/checkout`. Until a `sform` with these options is on NuGet, the workflow packs it first, see [Until sform is published](#until-sform-is-published).

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

Credentials are not inputs. `sform` reads them from the environment of the job or of the step, as described in [Sign in from a pipeline](https://42for.net/cli/sform#sign-in-from-a-pipeline): a certificate (`STORYTELLER_CLIENT_CERTIFICATE` and `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD`), a federated machine (`client-id` with `permissions: id-token: write`), `STORYTELLER_API_KEY` or client credentials.

For a configuration with secrets, a certificate machine over mTLS is the recommendation (see [Secrets need more than a credential](#secrets-need-more-than-a-credential)). The workflow writes its `.pfx` file from a secret under `$RUNNER_TEMP`:

```yaml
- run: printf '%s' "$CERTIFICATE" | base64 --decode > "$RUNNER_TEMP/storyteller.pfx"
  env:
    CERTIFICATE: ${{ secrets.STORYTELLER_CERTIFICATE_BASE64 }}
- uses: akobr/mono.me/src/Platform/GitHubAction@<commit>
  with:
    key: exe.northwind.invoicing.production
    export-env: true
    env-prefix: APP_
    cli-version: 0.8.115-alpha-gd665130ae2
  env:
    STORYTELLER_ENDPOINT: https://storyteller.example.com/api
    STORYTELLER_CLIENT_CERTIFICATE: ${{ runner.temp }}/storyteller.pfx
    STORYTELLER_CLIENT_CERTIFICATE_PASSWORD: ${{ secrets.STORYTELLER_CERTIFICATE_PASSWORD }}
```

A federated machine is possible too, and needs no stored secret. With `Secrets.Read`, it receives secret values only when an Administrator sets `RequireMtlsForSecretDelivery` to `false` for the project, see below. This one has no `Secrets.Read` and reads a configuration without secret bindings:

```yaml
permissions:
  id-token: write
  contents: read
steps:
  - uses: akobr/mono.me/src/Platform/GitHubAction@<commit>
    with:
      key: exe.northwind.invoicing.production
      project: acme.billing
      endpoint: https://storyteller.example.com/api
      client-id: 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07
      output-file: ${{ runner.temp }}/billing.env
      cli-version: 0.8.115-alpha-gd665130ae2
  - run: docker compose --env-file "$RUNNER_TEMP/billing.env" up --detach
```

## Secrets need more than a credential

The machine needs `Secrets.Read` (`sform account machine create -r --scope secrets-read`) and one of these sign-ins:

- **Recommended: a certificate machine over mTLS.** It receives secret values under the default policy of a project, so the project keeps `RequireMtlsForSecretDelivery` on and every machine without a certificate still gets no secret value. The workflow writes the `.pfx` file from a secret under `$RUNNER_TEMP`. The API must sit behind a front end that forwards the client certificate (`MachineAuth:IsCertificateHeaderFromClientAllowed`). See [Set up a certificate machine](#set-up-a-certificate-machine).
- **Possible: a federated machine** (GitHub OIDC). It needs no stored secret, only `permissions: id-token: write` and the machine id, a GUID, in `client-id`. It receives secret values only when an Administrator sets `RequireMtlsForSecretDelivery` to `false` for the project (`sform story kv policy set --require-mtls false`).
- **Possible: an API key or client credentials**, under the same change of the policy.

The policy is per project: turned off, it lets every machine of the project with `Secrets.Read` receive secret values, and every machine with `Secrets.Write` write secrets, without mTLS, with an API key, client credentials or a federated token, not only the machine of the workflow.

Without `Secrets.Read` or such a sign-in, the API withholds the secrets (`X-2S-Secrets-Withheld`) or returns the expressions of the secret bindings, and the action fails with exit code `2` and writes nothing. A machine without `Secrets.Read` can read a configuration without secret bindings.

### Set up a certificate machine

`sform account machine create` without `-k` creates the default kind of the project, which is `ApiKey` unless an Administrator changed it (`MachineAuth:DefaultCredentialKind` for a project without a stored policy). So a certificate machine takes `-k certificate`, and the project enables the kind first. Once:

1. An Administrator enables `Certificate` for the project. `sform account machine machine-auth` prints the policy. With `-k` it sets the whole policy, so name the kinds it printed, the default kind first, add `-k certificate`, and add `-l` when the project has a certificate lifetime of its own.
2. A user with the `ContributorWithSecrets` role creates the machine with `Secrets.Read` and writes its certificate to a file. `machine create` prints the password of the certificate once.
3. The repository gets two secrets: `STORYTELLER_CERTIFICATE_BASE64`, the file in base64 on one line, and `STORYTELLER_CERTIFICATE_PASSWORD`, the password. Then delete the file.

```bash
sform account machine machine-auth                              # the enabled kinds, the default kind and the lifetime
sform account machine machine-auth -k api-key -k certificate    # the kinds it printed, and certificate
sform account machine create -r --scope secrets-read -k certificate -o storyteller.pfx
base64 -w0 storyteller.pfx | gh secret set STORYTELLER_CERTIFICATE_BASE64
gh secret set STORYTELLER_CERTIFICATE_PASSWORD                  # paste the password that machine create printed
rm storyteller.pfx
```

The certificate expires after the certificate lifetime of the project, or after the days that `-l` of `machine create` gives. Before then, the two secrets need a new certificate and its password.

## What later steps see

Every variable in `GITHUB_ENV` reaches every later step of the job, actions of other authors included, and the log prints it in the `env:` block of every such step. Only masked values are hidden there, and a mask hides a value in the log, not from a step. `mask: bound` is a heuristic: it masks the values that bindings produced, also public ones, and cannot recognise a secret stored as a plain value in the configuration. So export only what the job needs: a prefix keeps the names apart, `mask: all` masks every string and number, and `output-file` under `$RUNNER_TEMP` without `export-env` keeps the values out of the environment of the other steps, for the one step that reads the file.

## Names

The names come from the configuration, so whoever edits the configuration could steer the later steps (`PATH`, `NODE_OPTIONS`, `LD_PRELOAD`, `RESTORESOURCES`). `export-env` therefore needs `env-prefix`, or `allow-unprefixed: true` for a job where the people who edit the configuration may steer the job anyway. Either way, `sform` refuses names that the runner reserves or that change how later steps or their tools run, with and without the prefix, without regard to case: among them `PATH`, `HOME`, `BASH_ENV`, `NODE_OPTIONS`, `CLASSPATH`, `HTTP_PROXY`, `RESTORESOURCES`, and every name that starts with `GITHUB_`, `RUNNER_`, `ACTIONS_`, `LD_`, `DOTNET_`, `NUGET_`, `MSBUILD`, `GIT_` or `STORYTELLER_`. The [whole list](https://42for.net/cli/sform#export-to-github-actions) is in the `sform` documentation. No such list is complete, which is why the prefix is required. `sform` also refuses a value with U+0000, a variable longer than 32,767 characters and more than 512 KiB of variables.

## Until sform is published

`42.Platform.Cli` on nuget.org is older than these options. Until a release has them, the workflow packs `sform` from the monorepo and installs it through `cli-source`:

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
    working-directory: mono.me   # so its global.json chooses the SDK, not the one of the calling repository
    run: |
      dotnet pack src/Platform/Cli/src -o "$RUNNER_TEMP/sform"
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
      STORYTELLER_CLIENT_CERTIFICATE: ${{ runner.temp }}/storyteller.pfx   # see Credentials
      STORYTELLER_CLIENT_CERTIFICATE_PASSWORD: ${{ secrets.STORYTELLER_CERTIFICATE_PASSWORD }}
```

A workflow of the monorepo checks out the monorepo itself (`fetch-depth: 0`), packs `src/Platform/Cli/src` and uses `./src/Platform/GitHubAction`.

## .NET on the runner

`actions/setup-dotnet` runs only when the runner has no .NET 10 SDK (a runner with only the .NET 10 runtime, or with an older SDK, gets it too), and then it changes `DOTNET_ROOT` and `PATH` for the rest of the job: the later steps run the .NET it installed. A job that needs another .NET sets it up itself, before the action or after it.

## Checks

No workflow tests the action (section 9 of the first spec): nothing in GitHub builds or tests .NET yet, and there is no Storyteller instance for a smoke test. The one workflow of the action releases it, see [Releasing](#releasing). The logic that matters for security (names, masks, heredocs, refusals) is in `sform`, with its unit tests ([`src/Platform/Cli/test`](https://github.com/akobr/mono.me/tree/main/src/Platform/Cli/test): `ConfigGetCommandTests`, `ConfigurationMasksTests`, `GitHubEnvironmentFileTests`). [The review of phase 5](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/specs/reviews/2026-10-27%20Phase%205%20of%20Flat%20Output%20Formats%20for%20Configurations.md) records the local checks: `action.yml` parses, and its scripts ran in bash against a packed `sform` and a stub API, with stub `RUNNER_TEMP`, `GITHUB_ENV` and `GITHUB_OUTPUT`; `actionlint` was not available.

## Releasing

For maintainers. The workflow [Storyteller action release](https://github.com/akobr/mono.me/blob/main/.github/workflows/storyteller-action-release.yml) of the monorepo copies the action into a repository of its own, which holds the action alone and which GitHub Marketplace lists. The spec is [Publishing the GitHub Action and Secrets in CI](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/specs/2026-10-27%20Publishing%20the%20GitHub%20Action%20and%20Secrets%20in%20CI.md).

Once, before the first release:

1. The owner creates that repository, public and empty, and accepts the GitHub Marketplace Developer Agreement; the account needs two-factor authentication.
2. The repository variable `STORYTELLER_ACTION_REPOSITORY` of the monorepo names it, as `<owner>/<name>`.
3. The repository secret `STORYTELLER_ACTION_TOKEN` of the monorepo holds a fine-grained personal access token whose resource owner is that owner, with access to that repository only and the permission Contents: Read and write. The token expires, so renew it before a release.
4. On `main`, this README and the page [GitHub Action](https://42for.net/cli/github-action) (`docs/42for.net/cli/github-action.md`) switch to that repository: their `uses:` lines become `<owner>/<name>@v1`, the sentences that call the repository undecided or the action unreleased go, and the monorepo path stays only for changes that are not released. This README is the page of the action on the Marketplace.

Each release:

1. Run **Storyteller action release** from `main` (Actions, Run workflow) with the input `version`, `X.Y.Z`, for example `1.0.0`. The workflow fails before it clones anything when it runs from another branch, when the version is not `X.Y.Z`, or when the variable or the secret is missing; the message names them. It then fails when this README does not use `<owner>/<name>@vX` for the major version of the release. The token of the run only reads the monorepo.
2. It clones the repository of the action (an empty one too, which gets the branch `main`), replaces everything but `.git` with `action.yml`, `README.md` and `LICENSE` of `src/Platform/GitHubAction`, and commits `Release vX.Y.Z` with the source commit of the monorepo in the body. No workflow file reaches that repository.
3. It refuses a tag `vX.Y.Z` that exists, creates it, moves the major tag `vX` to it, and pushes the branch and the two tags in one atomic push. It refuses a version that does not sort above the newest release; a fix of an older major version is not released by this workflow. The token never appears in a URL or in the log: git gets it from a credential helper that reads the environment of the step.
4. It creates a **draft** release `vX.Y.Z` whose notes link the source commit, and names it in the summary of the run. Open it in the repository of the action, check **Publish this Action to the GitHub Marketplace**, choose the categories at the first release, and publish it. The API cannot list an action on the Marketplace, so this step stays by hand.

`v1` moves with every `1.x.y`, so `@v1` gets every compatible release, while `@v1.2.3` and a commit stay where they are. A change that breaks the inputs or the behaviour of the action is a new major version: this README switches to `@v2`, then `2.0.0` starts `v2` and leaves `v1` at the last `1.x.y`. Every release replaces the whole content of the repository of the action, so a change made there directly is gone with the next release. When the push succeeded but the draft release failed, a run with the same version is refused; create the release of the tag from the release form instead.
