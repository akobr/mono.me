# GitHub Action

::: danger
The platform is under development, see [the road map](/platform/road-map).
:::

The Storyteller configuration action brings the resolved configuration of an annotation into a GitHub Actions job: into the environment of the job, where every later step finds its values as variables, into a file, or both. It installs [`sform`](/cli/sform) and runs `sform story config <key> --resolved` once, so the names, the masks and the refusals are those of [Export to GitHub Actions](/cli/sform#export-to-github-actions).

``` yaml
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

The action lives in the [mono.me repository](https://github.com/akobr/mono.me/tree/main/src/Platform/GitHubAction) and has no release tags yet: pin a commit. It runs on Linux, macOS and Windows runners. Until a `sform` with these options is on NuGet, the workflow packs `sform` itself, see [Until sform is published](#until-sform-is-published).

## Inputs

| Input | Default | Meaning |
|---|---|---|
| `key` | required | The annotation key of the configuration |
| `project` | | `<organization>.<project>`, passed as `STORYTELLER_PROJECT` |
| `view` | | Passed as `STORYTELLER_VIEW` |
| `endpoint` | | The address of the API, `/api` included, passed as `STORYTELLER_ENDPOINT` |
| `client-id` | | Passed as `STORYTELLER_CLIENT_ID`; a GUID in a job with `permissions: id-token: write` signs in with the job's OIDC token |
| `export-env` | `false` | Export the resolved configuration into the environment of the job |
| `env-prefix` | | A prefix of every name, for example `APP_`; with `export-env`, and for `output-file` in the `env` format |
| `allow-unprefixed` | `false` | Let `export-env` write the names of the configuration without a prefix |
| `mask` | `bound` | `bound` masks the values that bindings produced, `all` every string and number |
| `output-file` | | Also write the resolved configuration to this file, best under `$RUNNER_TEMP`; a relative path is relative to the workspace |
| `format` | `env` | The format of `output-file`: `env`, `flat`, `properties`, `yaml`, or `json` for the content alone |
| `cli-version` | required | The exact version of `42.Platform.Cli` (`sform`) to install |
| `cli-source` | | An additional NuGet source of the tool, a URL or an absolute path |

Set `export-env`, `output-file` or both; without either, the action fails before it installs anything. `project`, `view`, `endpoint` and `client-id` become `STORYTELLER_*` variables only when they are not empty, so an empty input keeps the variable that the job sets.

## What the action does

1. It checks the inputs.
2. When the runner has no .NET 10 SDK, it runs `actions/setup-dotnet@v6` (see [.NET on the runner](#net-on-the-runner)).
3. It installs the `cli-version` of `sform` into a folder of its own under `$RUNNER_TEMP`, from nuget.org and `cli-source`, without reading the `nuget.config` of your repository. A second use of the action in the job reuses that folder.
4. It runs `sform` once, with one resolved and one effective read: `sform` writes the `::add-mask::` commands first, then the variables to `GITHUB_ENV` with `export-env`, then `output-file`, and a summary with the names of the variables and the number of masked values, never a value.

The step fails with the exit code of `sform`, `2` when it refuses the configuration or the options; see [Read a configuration](/cli/sform#read-a-configuration). `sform` sends anonymous usage data unless the job sets `DO_NOT_TRACK: 1`, see [Telemetry](/cli/sform#telemetry).

## Sign-in

Credentials are not inputs: `sform` reads them from the environment of the job or of the step, as in [Sign in from a pipeline](/cli/sform#sign-in-from-a-pipeline). An API key or a certificate names its project; client credentials and federated machines need `project`, or `STORYTELLER_PROJECT` or `STORYTELLER_KEY` in the job.

A certificate machine gets its `.pfx` file from a secret:

``` yaml
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

A federated machine needs no secret. This one has no `Secrets.Read` and reads a configuration without secret bindings, into a file for the one step that needs it:

``` yaml
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

A configuration whose bindings read secrets reaches the job only when the machine may receive them:

- The machine needs `Secrets.Read` (`sform account machine create -r --scope secrets-read`).
- Under the default policy of a project, only a **certificate machine over mTLS** receives secret values, which needs an API behind a front end that forwards the client certificate. An **API key, client credentials or a federated machine** receives them only when an Administrator turns that policy off: `sform story kv policy set --require-mtls false` (`RequireMtlsForSecretDelivery` set to `false`).
- Otherwise the API withholds the secrets or returns the expressions of the secret bindings, and the action stops with exit code `2` before it exports or writes anything.

## What later steps see

A variable in `GITHUB_ENV` reaches **every later step of the job**, actions of other authors included, and the log prints it in the `env:` block of every such step. Only masked values are hidden there, and a mask hides a value in the log, not from a step that reads the variable.

`mask: bound`, the default, masks the values that bindings produced, because the resolved read does not say which values are secret: it also masks public values that bindings produced, cannot recognise a secret stored as a plain value in the configuration, and masks a secret that a binding put into a longer string only as that whole string. Values shorter than four characters are not masked under `bound`; the summary names them. So export only what the job needs:

- a prefix keeps the names apart;
- `mask: all` masks every string and number, for a configuration that holds secrets as plain values;
- `output-file` under `$RUNNER_TEMP`, without `export-env`, keeps the values out of the environment of the other steps.

## Names

The names come from the configuration, so whoever edits the configuration could steer the later steps: `PATH`, `NODE_OPTIONS`, `LD_PRELOAD` or `RESTORESOURCES` change which tools run and where they get packages. `export-env` therefore needs `env-prefix`, for example `APP_`, or `allow-unprefixed: true` for a job where the people who edit the configuration may steer the job anyway.

Either way, `sform` refuses names that the runner reserves or that change how later steps or their tools run, compared without regard to case and with the prefix: `PATH`, `HOME`, `BASH_ENV`, `NODE_OPTIONS`, `CLASSPATH`, `HTTP_PROXY`, `RESTORESOURCES` and others, and every name that starts with `GITHUB_`, `RUNNER_`, `ACTIONS_`, `LD_`, `DOTNET_`, `NUGET_`, `MSBUILD`, `GIT_` or `STORYTELLER_`. The whole list is under [The refused names](/cli/sform#export-to-github-actions). No such list is complete, which is why the prefix is required. A value with U+0000, a variable longer than 32,767 characters and more than 512 KiB of variables are refused too.

## Until sform is published

The `sform` on nuget.org is older than these options. Until a release has them, the workflow packs `sform` from the repository and passes the folder as `cli-source` and the packed version as `cli-version`:

``` yaml
steps:
  - uses: actions/checkout@v4
    with:
      repository: akobr/mono.me
      ref: <commit>
      path: mono.me
      fetch-depth: 0   # the version of sform is computed from the history
  - uses: actions/setup-dotnet@v6
    with:
      dotnet-version: 10.0.x
  - id: sform
    shell: bash
    working-directory: mono.me   # so its global.json chooses the SDK, not the one of your repository
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
      STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
```

## .NET on the runner

`sform` is a .NET 10 tool: installing it needs the .NET 10 SDK, which also brings the runtime it runs on. When the runner has that SDK, the action uses it and changes nothing. When it does not, also when it has only the .NET 10 runtime or an older SDK, the action runs `actions/setup-dotnet@v6` with `10.0.x`, which changes `DOTNET_ROOT` and `PATH` **for the rest of the job**: the later steps run the .NET it installed. A job that needs another .NET sets it up itself, after the action or before it with .NET 10 included.
