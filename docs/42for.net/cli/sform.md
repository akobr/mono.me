# sform

::: danger
The platform is under development, see [the road map](/platform/road-map).
:::

A simple CLI interface for developers, dev ops engineers, and your infrastructure specialists to interact with the **2S** platform, consisting of a storyteller, supervisor, and scheduler, no matter where they are hosted and how.

## Sign in

Sign in with your account. The tool prints an address and a code, and waits while you confirm them in a browser. `--browser` opens the address for you.

``` powershell
sform account
```

Every other command uses the stored session and refreshes it silently. `sform account logout` signs you out.

## Sign in from a pipeline

In a CI/CD pipeline or a GitHub Action nobody can type a code, so `sform` signs in as a machine instead. Create the machine once, then give the job the variables that `sform account machine create` prints. With any of them set, `sform` never asks anyone to sign in, and it stores no credential or token.

`sform` reads the same `STORYTELLER_*` variables as the .NET configuration provider and the C# SDK, so one set of variables serves a job that runs `sform` and an application that reads its configuration.

| Machine | Variables |
|---|---|
| API key | `STORYTELLER_API_KEY` |
| Certificate | `STORYTELLER_CLIENT_CERTIFICATE`, `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD` |
| Certificate and API key | all three of the above |
| Client credentials | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE` |
| Federated | `STORYTELLER_CLIENT_ID`, the machine id, and `STORYTELLER_TOKEN_FILE`, a file with a token of your platform; in a GitHub Actions job with `permissions: id-token: write`, the machine id alone |
| Any bearer token | `STORYTELLER_TOKEN_FILE` without `STORYTELLER_CLIENT_ID`, a file the token is read from before every request |

Every machine also needs `STORYTELLER_ENDPOINT`, the address of the API, `/api` included. An API key or a certificate already names its project. For the other kinds, set `STORYTELLER_PROJECT=<organization>.<project>`, set `STORYTELLER_KEY` to a full key of the project, or pass `-p` to each command. `STORYTELLER_KEY` is the key the configuration provider reads, `@<organization>.<project>.<view>.<annotation key>`: `sform` works on its project and, unless `-v` or `STORYTELLER_VIEW` names a view, in its view; `-p` or `STORYTELLER_PROJECT` naming another project replaces both.

The older names still work, but are deprecated, and `sform account` names them: `STORYTELLER_API_URL` (now `STORYTELLER_ENDPOINT`), `STORYTELLER_CERTIFICATE_FILE` and `STORYTELLER_CERTIFICATE_PASSWORD` (now `STORYTELLER_CLIENT_CERTIFICATE` and `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD`), and `STORYTELLER_SUBJECT_TOKEN_FILE` (now `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID`). A new and an old name with different values stop the job.

A federated machine has no secret. `sform` exchanges the platform token for a short-lived Storyteller token at the token endpoint of the project, `<STORYTELLER_ENDPOINT>/v1/<organization>/<project>/access/token`, and exchanges again before that token expires. In GitHub Actions it asks the runner for the platform token itself, for the audience of that address, so the audience of the machine's GitHub rule must be exactly that address, in the same case: use the `TokenEndpoint` and the variables that `sform account machine create` prints. A token from a file is sent as it is, so its audience is whatever your platform put in it, and the rule's audience must match that instead. The `TokenEndpoint` is a good choice where you pick the audience yourself, as in the Kubernetes example below.

`STORYTELLER_CLIENT_ID` decides what `STORYTELLER_TOKEN_FILE` holds: without it, a token the API accepts, sent as it is; with it, a token of your platform, which `sform` exchanges.

``` yaml
# GitHub Actions
- run: dotnet tool install 42.Platform.Cli --global --prerelease
- run: sform story config set sbt.northwind -i configuration/northwind.json
  env:
    STORYTELLER_ENDPOINT: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
```

``` yaml
# GitHub Actions job, federated machine: no secret, no file
runs-on: ubuntu-latest
permissions:
  id-token: write
  contents: read
env:
  STORYTELLER_ENDPOINT: https://storyteller.example.com/api
  STORYTELLER_KEY: "@acme.billing.production.sbt.northwind"   # the project and view of sform, the key of the provider
  STORYTELLER_CLIENT_ID: 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07
steps:
  - uses: actions/checkout@v4
  - run: dotnet tool install 42.Platform.Cli --global --prerelease
  - run: sform story config set sbt.northwind -i configuration/northwind.json
```

``` yaml
# Kubernetes, federated machine: a projected token for the token endpoint
containers:
  - name: sync
    env:
      - { name: STORYTELLER_ENDPOINT, value: https://storyteller.example.com/api }
      - { name: STORYTELLER_PROJECT, value: acme.billing }
      - { name: STORYTELLER_CLIENT_ID, value: 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07 }
      - { name: STORYTELLER_TOKEN_FILE, value: /var/run/secrets/storyteller/token }
    volumeMounts:
      - { name: storyteller-token, mountPath: /var/run/secrets/storyteller, readOnly: true }
volumes:
  - name: storyteller-token
    projected:
      sources:
        - serviceAccountToken:
            path: token
            audience: https://storyteller.example.com/api/v1/acme/billing/access/token
```

`sform account` checks the variables without calling the account API, which is a good first step of a job. For client credentials and a federated machine it also gets a token; a federated machine needs `STORYTELLER_PROJECT` or `STORYTELLER_KEY` for that, as `sform account` has no `-p`. A wrong setting or a missing token file stops the job with exit code `2`. A token endpoint or GitHub request that fails, after up to three retries, or gets no answer within 100 seconds stops it with `3`. Rejected credentials, a refused exchange, or a token request that is refused or redirected stop it with `44`.

A machine can do what its scopes allow, and it gets them when it is created. **Runtime scopes** read and write single documents. **Management scopes** add bulk and structural work, on top of the runtime scope of the operation. `sform account machine create` picks them:

| Option | Scopes |
|---|---|
| `-r` | `Annotation.Read` and `Configuration.Read`. The default: `sform` adds them whenever no runtime scope is given. |
| `-w` | The scopes of `-r`, and `Annotation.Write` and `Configuration.Write` |
| `--scope` | Any scope of the table below |
| `-m` | A management scope; a runtime scope is refused, with a hint to use `--scope` |

`--scope` and `-m` take several values separated by commas, or repeat. A value is the kebab name, the scope string or the enum name, in any case (`secrets-read`, `Secrets.Read`, `SecretsRead`). An unknown value, or a runtime scope given to `-m`, prints the allowed names and stops with exit code `2` before any call. The API adds the read scope of every write scope, so `--scope configuration-write` gives `Configuration.Read` too.

| `--scope` | Scope | Enum name | Group | Needed by |
|---|---|---|---|---|
| `annotation-read` | `Annotation.Read` | `AnnotationRead` | Runtime | `sform story` (the list), `get`, `deleted` and `history` |
| `annotation-write` | `Annotation.Write` | `AnnotationWrite` | Runtime | `sform story set` |
| `configuration-read` | `Configuration.Read` | `ConfigurationRead` | Runtime | the reads of `sform story config`, `schema`, `template` and `kv` |
| `configuration-write` | `Configuration.Write` | `ConfigurationWrite` | Runtime | the writes of `sform story config` and `kv`; with `schema-write` or `template-write`, those of `schema` and `template` |
| `secrets-read` | `Secrets.Read` | `SecretsRead` | Runtime | the values of secrets: `sform story kv get --reveal`, `sform story config <key> --resolved`, `sform story kv export --include-secrets` |
| `secrets-write` | `Secrets.Write` | `SecretsWrite` | Runtime | `sform story kv set` of a secret, and `sform story kv protect` |
| `configuration-transfer` | `Configuration.Transfer` | `ConfigurationTransfer` | Management | `sform story export` and `import`, `sform story kv export` and `import`; an import of a view also needs `schema-write`, `template-write` and `annotation-manage` for those kinds |
| `schema-write` | `Schema.Write` | `SchemaWrite` | Management | the writes and deletes of `sform story schema`, and `sform story import` of schemas |
| `template-write` | `Template.Write` | `TemplateWrite` | Management | `sform story template set`, `edit` and `delete`, and `sform story import` of templates |
| `annotation-manage` | `Annotation.Manage` | `AnnotationManage` | Management | `sform story delete` and `restore`, `sform story set --restore-deleted`, and `sform story import` of annotations |
| `keyvalue-manage` | `KeyValue.Manage` | `KeyValueManage` | Management | `sform story kv protect` and `restore` |

``` powershell
sform account machine create                                    # Annotation.Read and Configuration.Read
sform account machine create -r --scope secrets-read            # a service that also receives its secrets
sform account machine create -w -m configuration-transfer,schema-write,template-write,annotation-manage   # a pipeline that keeps a view in a git repository
sform account machine create --scope configuration-write -m schema-write,template-write   # a pipeline that deploys schemas and templates
```

`--scope secrets-read` alone gives only `Secrets.Read`, because it is a runtime scope: add `-r` or `-w` for the documents themselves. Creating a machine needs the Contributor role, the `ContributorWithSecrets` role when it gets `Secrets.Read` or `Secrets.Write`, and the Administrator role when it gets a management scope; resetting it, replacing its federation rules and deleting it (`sform account machine delete`) need the same role. `sform account machine` lists the machines with the columns `Id`, `Scopes` (the runtime scopes), `Kind`, `Secret`, `RestrictionTo` and `Management` (the management scopes).

Without a scope it needs, the API answers `403` with a message that names the scope, such as "This operation needs the scope Annotation.Manage, which the machine was not created with.", and `sform` prints it. Machines created before these scopes existed have none: their API keys and certificates get `401`, their client credentials `403`, and a federated machine gets no token. A machine cannot be edited: create a new one with the scopes the job needs.

::: warning
Commands print what they read or write, and the job log keeps everything a step prints. A step that prints a resolved configuration with secrets puts those values into the log. To use a configuration in a job, write it to a file with `--export`, or into the environment of the job with `--github-env`, which masks the values that bindings produced before it writes anything else; see [Read a configuration](#read-a-configuration).
:::

::: tip
The details, such as the token URL rules, the certificate header for a local API, how a federated machine gets and renews its token, and every exit code, are in [the technical documentation](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#machine-sign-in-for-pipelines). [Scopes of machines](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#scopes-of-machines) lists the operations of every scope.
:::

## Read a configuration

`sform story config <key>` prints the configuration of an annotation: its whole model, with the version and the audit stamp, as coloured JSON. `-r` reads the resolved configuration, with its bindings evaluated, and `--version <n>` the stored content of a version.

``` text
sform story config <key> [-r|--resolved | --version <n>] [-f|--format json|flat|properties|env|yaml]
                         [--content-only] [-e|--export <file>] [--env-prefix <PREFIX>]
                         [--github-env] [--mask bound|all] [--allow-unprefixed] [--allow-unresolved]
```

`--format` writes the content alone in a flat format: `flat`, a JSON object of the .NET `IConfiguration` keys (`db:hosts:0`); `properties` for Spring Boot (`db.hosts[0]`); `env`, a `.env` file (`DB__HOSTS__0`); or `yaml`. `sform` formats the content itself, with the library the API uses for `?format=`, so the text is the same as the API's, also for `--version`. `--env-prefix APP_` puts a prefix before every `env` name (`APP_DB__HOST`), as .NET `AddEnvironmentVariables("APP_")` expects. A document a format cannot write, for example a name with a space in `env`, stops the command with the rule and the JSON path of the name. The formats and their rules are in [Configuration Formats](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-formats.md).

| Options | Standard output | The `--export` file |
|---|---|---|
| None, or `--format json` | The model, with line numbers and colours | The model; `sform` asks before it replaces a file |
| `--format flat`, `properties`, `env` or `yaml` | The text with line numbers, without colours; nothing with `--export` | The text |
| `--content-only` | The content alone, as it is: the text of the format, or for `json` the content of the model, indented by two spaces (`{}` without content); nothing with `--export` | The same |
| `--github-env` or `--mask` | Only the `::add-mask::` commands | The content in the format of `--format`; for `json`, the content of the model |

- With `--format`, `--content-only`, `--github-env` or `--mask`, the content is written as UTF-8 without a byte order mark and with LF line ends, also to a Windows console. The `--export` file is then replaced without asking, and on Linux and macOS a file of a resolved configuration is readable and writable by its owner alone (mode `0600`).
- With `--content-only`, `--github-env` or `--mask`, every message of the command goes to standard error, one line each, so standard output holds the content alone and can be piped or redirected. Parsing errors, sign-in failures, refusals of the API (`401`, `403`) and crashes are still printed to standard output; they end with a non-zero exit code, and the `--export` file is not written.
- Windows PowerShell 5.1 re-encodes what `>` redirects, so use `--export` there. PowerShell 7 and the other shells keep the bytes.

``` powershell
sform story config exe.billing.prod -r -f env --content-only > billing.env
sform story config exe.billing.prod -r -f yaml -e values.yaml
sform story config exe.billing.prod --version 3 -f properties --content-only
```

A resolved configuration holds secret values only for a caller with the right to read secrets: a user with the `ContributorWithSecrets` role, or a machine with `Secrets.Read`, which by default must call with its certificate over mTLS. When the API withholds the secrets (`X-2S-Secrets-Withheld: mtls-required`), the values of secret bindings are their expressions, and `sform` warns about it. A machine without `Secrets.Read` gets the expressions without that header: when a resolved value starts with `@`, `sform` also reads the effective configuration, and warns about every value that is still the expression the effective configuration has at the same path. Warnings and errors name paths, never a value.

| Exit code | When |
|---|---|
| `0` | The configuration was printed or written |
| `2` | Nothing was written: an invalid key, format, mask or prefix; options that do not go together (`--version` with `--resolved`, `--env-prefix` without `--format env` or `--github-env`, `--github-env` or `--mask` without `--resolved` or with `--version`, `--content-only` with `--github-env` or `--mask` but without `--export`, `--github-env` with neither `--env-prefix` nor `--allow-unprefixed`, `--allow-unprefixed` or `--allow-unresolved` without `--github-env` or `--mask`); `--github-env` outside a GitHub Actions job; a configuration or version that does not exist; a document the format cannot write; a binding that cannot be evaluated (`422`); and for `--github-env` and `--mask`, withheld secrets, unresolved bindings and refused variables |
| `-1` | The command line does not parse: an unknown option, an option without its value, an option given twice, or `--version abc`. Shells on Linux and macOS show `255` |
| `44` | The API refused the read (`401`, `403`) |
| `1` | Another failure of the API, or a crash |

### Export to GitHub Actions

`--github-env` exports a resolved configuration into the environment of a GitHub Actions job: every value becomes a variable with its `env` name in `GITHUB_ENV`, which the runner reads after the step. The options of this section are newer than the `sform` on NuGet; until a release has them, install a `sform` packed from the repository.

``` yaml
# GitHub Actions: the later steps read APP_DB__HOST, APP_DB__PASSWORD, …
- run: sform story config exe.billing.prod -r --github-env --env-prefix APP_
  env:
    STORYTELLER_ENDPOINT: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
- run: dotnet run --project src/Billing   # AddEnvironmentVariables("APP_")
```

In one run, with one resolved and one effective read, `sform`:

1. checks the variables, and writes nothing when a name or a value is refused (see below);
2. writes the `::add-mask::` commands to standard output, before any value reaches `GITHUB_ENV` or a file;
3. appends every variable to `GITHUB_ENV` as `NAME<<ghadelimiter_…`, the value, and the delimiter, with one random delimiter that no value contains, so a value with quotes or line breaks arrives as it is; when an earlier command left the last line of the file without its line break, `sform` ends that line first;
4. writes the `--export` file, when it is given;
5. writes a summary to standard error: the names of the variables, how many values it masked, and the names it did not mask because they are shorter than four characters.

**What later steps see.** A variable in `GITHUB_ENV` reaches every later step of the job, actions of other authors included, and the log prints it in the `env:` block of every such step; only masked values are hidden there. Export what the job needs: a prefix keeps the names apart, `--mask all` hides every string and number, and `--export` to a file under `$RUNNER_TEMP` with `--mask` instead of `--github-env` keeps the values out of the environment of the other steps.

**Names.** The names come from the configuration, so whoever edits the configuration could steer the later steps: `PATH`, `NODE_OPTIONS`, `LD_PRELOAD` or `RESTORESOURCES` change which tools run and where they get packages. `--github-env` therefore needs `--env-prefix`, for example `APP_`, or `--allow-unprefixed` for a job where the people who edit the configuration may steer the job anyway. Either way, names that the runner reserves or that change how later steps or their tools run are refused, compared without regard to case and with the prefix. No such list is complete (MSBuild reads every variable as a property), which is why the prefix is required.

::: details The refused names
`NODE_OPTIONS`, `PATH`, `HOME`, `SHELL`, `ENV`, `BASH_ENV`, `CDPATH`, `IFS`, `PS4`, `PROMPT_COMMAND`, `SHELLOPTS`, `BASHOPTS`, `GLOBIGNORE`, `CLASSPATH`, `CC`, `CXX`, `MAKEFLAGS`, `JAVA_TOOL_OPTIONS`, `_JAVA_OPTIONS`, `JDK_JAVA_OPTIONS`, `JAVA_OPTS`, `MAVEN_OPTS`, `GRADLE_OPTS`, `SBT_OPTS`, `PYTHONPATH`, `PYTHONSTARTUP`, `PYTHONHOME`, `PERL5OPT`, `PERL5LIB`, `PERLLIB`, `RUBYOPT`, `RUBYLIB`, `NODE_PATH`, `NODE_EXTRA_CA_CERTS`, `NODE_TLS_REJECT_UNAUTHORIZED`, `LUA_INIT`, `RUSTC_WRAPPER`, `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `NO_PROXY`, `SSL_CERT_FILE`, `SSL_CERT_DIR`, `REQUESTS_CA_BUNDLE`, `CURL_CA_BUNDLE`, `GOFLAGS`, `GOPROXY`, `GONOSUMDB`, `GOSUMDB`, `GOINSECURE`, `RESTORESOURCES`, `KUBECONFIG`, `PATHEXT`, `COMSPEC` and `PSMODULEPATH`, and every name that starts with `GITHUB_`, `RUNNER_`, `ACTIONS_`, `INPUT_`, `STATE_`, `LD_`, `DYLD_`, `BASH_FUNC_`, `DOTNET_`, `COREHOST_`, `CORECLR_`, `COMPLUS_`, `MSBUILD`, `NUGET_`, `NPM_CONFIG_`, `YARN_`, `PIP_`, `CARGO_`, `DOCKER_`, `GIT_` or `STORYTELLER_`.
:::

`sform` also refuses a value with the character U+0000, a variable longer than 32,767 characters as `NAME=value` (the limit of Windows), and variables of more than 512 KiB together.

**Masking.** `--mask bound`, the default, masks the values that bindings produced. The resolved read does not say which values are secret, so `sform` compares it with the effective configuration: a string or a number is masked when its path has no value in the effective configuration, or another one. When the two reads return different versions, or the effective read fails, every string and number is masked, and the summary says why. Booleans and empty or blank values are never masked. Under `bound`, a value shorter than four characters is not masked either, as masking `1` or `on` would hide those characters everywhere in the log; the summary names it. `--mask all` masks every string and number, whatever its length. A value with line breaks gets one mask for the whole value and one for each of its lines, whatever their length.

`bound` is a heuristic. It also masks the public values that bindings produced; it cannot recognise a secret stored as a plain value in a configuration (use `--mask all`, or keep secrets as secret key/values or vault references); and it masks a secret that a binding put into a longer string only as that whole string.

**Secrets need more than a credential.** The machine needs `Secrets.Read` (`sform account machine create -r --scope secrets-read`). Under the default policy of a project, only a certificate machine that calls over mTLS receives secret values; for an API key, client credentials or a federated machine, an Administrator sets `sform story kv policy set --require-mtls false`. Otherwise the export stops with exit code `2` on the withheld secrets, or on the expressions of the bindings that were not resolved; `--allow-unresolved` exports such expressions as they are.

`--mask` without `--github-env` writes only the masks and the `--export` file, for a job that keeps the configuration in a file for the one step that needs it:

``` yaml
- run: sform story config exe.billing.prod -r --mask bound -f env -e "$RUNNER_TEMP/billing.env"
- run: docker compose --env-file "$RUNNER_TEMP/billing.env" up --detach
```

## Telemetry

`sform` sends anonymous usage data to PostHog in the EU cloud: which commands run, how they end and how long they take. It sends one event per run, after the command has finished. The output and the exit code stay the same, the run waits for the event about two seconds at most, and an event that cannot be sent is dropped.

| Sent | Example |
|---|---|
| The command, by its full name, without arguments or option values | `sform storyteller kv get` |
| The outcome and the exit code | `success`, `0` |
| How long the run took, in milliseconds | `840` |
| The type of a failure, the reason of a failed sign-in, the HTTP status of a refused API call; never a message | `ApiException`, `403` |
| The version of `sform` and whether it is a release or a development build | `0.8.85-alpha+0cab16204f`, `Production` |
| The operating system, its architecture and the .NET version | `linux`, `X64`, `10.0.8` |
| Whether the run is in a CI pipeline, and whether it signed in as a machine | `true`, `false` |
| A random installation id | `3f1c2a9e5b7d4c119e0a2d4f6b8c1a07` |

Never sent: arguments, option values, error messages, names of organizations, projects, views or keys, addresses, file paths, your name or e-mail, the name of the machine, and tokens. The events create no person profile, and PostHog does not look up a location for them.

The installation id is a random number, not derived from the machine or your account. It is kept in `telemetry.json`, next to the stored session: in `%LOCALAPPDATA%\.42for.net` on Windows and in `~/.42for.net` elsewhere, so it survives tool updates. On the first run `sform` creates it and prints a notice to standard error. When `sform` reports a failure to Sentry, the report carries the same id.

Set `DO_NOT_TRACK` or `SFORM_TELEMETRY_OPTOUT` to `1`, `true`, `yes` or `on` to turn telemetry off. `DO_NOT_TRACK` is honoured by other tools too, `SFORM_TELEMETRY_OPTOUT` only by `sform`. With telemetry off, `sform` sends nothing and creates no id.

``` powershell
# This session only
$env:DO_NOT_TRACK = "1"

# Every new session of your user
[Environment]::SetEnvironmentVariable("SFORM_TELEMETRY_OPTOUT", "1", "User")
```

In a pipeline, set the variable in the `env` of the job like the sign-in variables above.
