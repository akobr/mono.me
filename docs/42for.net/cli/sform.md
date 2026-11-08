# sform

::: danger
The platform is under development, see [the road map](/platform/road-map).
:::

A simple CLI interface for developers, dev ops engineers, and your infrastructure specialists to interact with the **2S** platform, consisting of a storyteller, supervisor, and scheduler, no matter where they are hosted and how.

## Sign in

Sign in with your account. The tool prints an address and a code, and waits while you confirm them in a browser. `-b` or `--browser` opens the address for you, without a shell:

- with the program in `SFORM_BROWSER`, when it is set. The variable names the program only, a name on the `PATH` or a full path, without arguments, and gets the address as its only argument; a program that needs arguments goes into a small script;
- else with `BROWSER`, read as other tools read it: a list of commands separated by `:` (`;` on Windows), each a program that gets the address, or a command line with `%s` where the address goes, such as `BROWSER='firefox --new-window %s'`. The commands are tried in order, then your default browser;
- else with your default browser.

When the browser cannot be started, `sform` says so and waits for the sign-in all the same.

``` powershell
sform account
sform account --browser
sform account -o org_01JC3W6ZT4B2Q5N8M0R7K9X1YD
```

If you belong to several organizations, `sform` asks which one to sign in to after you confirm the code. `-o` or `--organization` with the id of the organization answers it up front, and is required where `sform` has no terminal to ask in (see [Run without a terminal](#run-without-a-terminal)): then `sform` stops with exit code `2` and lists your organizations with their ids and names on standard error. An id you are not a member of also ends with exit code `2`, the organization named, and no session is kept. `-o` takes the id of the organization in the sign-in service (WorkOS), not the name of a 2S organization of `-p <organization>.<project>`. With a stored session, `-o` checks that the session belongs to that organization: another one, or none, ends with exit code `2`, and `sform account logout` comes first. A deployment that signs in with Entra ID has no organizations, and refuses `-o`.

A declined sign-in ends with exit code `44`, a code that expired before you confirmed it with `2`, and a sign-in service that cannot be reached with `3`.

Every other command uses the stored session and refreshes it silently. `sform account logout` signs you out.

`sform account set -p <organization>.<project>` picks the default project and view of the commands without `-p` and `-v`; without `-p` it offers your projects to choose from, in a terminal. They are kept with your other preferences in `sform.settings.json`, see [Settings](#settings).

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

A job that receives secret values, such as a resolved configuration with secret bindings, best uses a certificate machine over mTLS. A federated machine, an API key and client credentials receive them only when an Administrator turns the mTLS policy of the project off, which then holds for every machine of the project, see **Secrets need more than a credential** under [Export to GitHub Actions](#export-to-github-actions).

Every machine also needs `STORYTELLER_ENDPOINT`, the address of the API, `/api` included. An API key or a certificate already names its project. For the other kinds, set `STORYTELLER_PROJECT=<organization>.<project>`, set `STORYTELLER_KEY` to a full key of the project, or pass `-p` to each command. `STORYTELLER_KEY` is the key the configuration provider reads, `@<organization>.<project>.<view>.<annotation key>`: `sform` works on its project and, unless `-v` or `STORYTELLER_VIEW` names a view, in its view; `-p` or `STORYTELLER_PROJECT` naming another project replaces both.

The older names still work, but are deprecated, and `sform account` names them: `STORYTELLER_API_URL` (now `STORYTELLER_ENDPOINT`), `STORYTELLER_CERTIFICATE_FILE` and `STORYTELLER_CERTIFICATE_PASSWORD` (now `STORYTELLER_CLIENT_CERTIFICATE` and `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD`), and `STORYTELLER_SUBJECT_TOKEN_FILE` (now `STORYTELLER_TOKEN_FILE` with `STORYTELLER_CLIENT_ID`). A new and an old name with different values stop the job.

A federated machine has no secret. `sform` exchanges the platform token for a short-lived Storyteller token at the token endpoint of the project, `<STORYTELLER_ENDPOINT>/v1/<organization>/<project>/access/token`, and exchanges again before that token expires. In GitHub Actions it asks the runner for the platform token itself, for the audience of that address, so the audience of the machine's GitHub rule must be exactly that address, in the same case: use the `TokenEndpoint` and the variables that `sform account machine create` prints. A token from a file is sent as it is, so its audience is whatever your platform put in it, and the rule's audience must match that instead. The `TokenEndpoint` is a good choice where you pick the audience yourself, as in the Kubernetes example below.

`STORYTELLER_CLIENT_ID` decides what `STORYTELLER_TOKEN_FILE` holds: without it, a token the API accepts, sent as it is; with it, a token of your platform, which `sform` exchanges. Earlier versions of `sform` refused the two together, so check that a job with a bearer token file has no `STORYTELLER_CLIENT_ID` left over. They also ended with exit code `3` where a refused token request now ends with `44`, and they made one token request where `sform` now retries transient failures.

``` yaml
# GitHub Actions, certificate machine: the recommendation for secrets
- run: printf '%s' "$CERTIFICATE" | base64 --decode > "$RUNNER_TEMP/storyteller.pfx"
  env:
    CERTIFICATE: ${{ secrets.STORYTELLER_CERTIFICATE_BASE64 }}
- run: dotnet tool install 42.Platform.Cli --global --prerelease
- run: sform story config set sbt.northwind -i configuration/northwind.json
  env:
    STORYTELLER_ENDPOINT: https://storyteller.example.com/api
    STORYTELLER_CLIENT_CERTIFICATE: ${{ runner.temp }}/storyteller.pfx
    STORYTELLER_CLIENT_CERTIFICATE_PASSWORD: ${{ secrets.STORYTELLER_CERTIFICATE_PASSWORD }}
```

An API key or client credentials go into `env:` the same way, from secrets of the repository: `STORYTELLER_API_KEY`, or the variables of client credentials in the table above.

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
| `annotation-read` | `Annotation.Read` | `AnnotationRead` | Runtime | `sform story` (the list), `get`, `deleted` and `history`, and the reads of `edit` |
| `annotation-write` | `Annotation.Write` | `AnnotationWrite` | Runtime | `sform story set` and `edit` |
| `configuration-read` | `Configuration.Read` | `ConfigurationRead` | Runtime | the reads of `sform story config`, `schema`, `template` and `kv` |
| `configuration-write` | `Configuration.Write` | `ConfigurationWrite` | Runtime | the writes of `sform story config` and `kv`; with `schema-write` or `template-write`, those of `schema` and `template` |
| `secrets-read` | `Secrets.Read` | `SecretsRead` | Runtime | the values of secrets: `sform story kv get --reveal`, `sform story config <key> --resolved`, `sform story config export <key>`, `sform story kv export --include-secrets` |
| `secrets-write` | `Secrets.Write` | `SecretsWrite` | Runtime | `sform story kv set` of a secret, and `sform story kv protect` |
| `configuration-transfer` | `Configuration.Transfer` | `ConfigurationTransfer` | Management | `sform story export` and `import`, `sform story kv export` and `import`; an import of a view also needs `schema-write`, `template-write` and `annotation-manage` for those kinds |
| `schema-write` | `Schema.Write` | `SchemaWrite` | Management | the writes and deletes of `sform story schema`, and `sform story import` of schemas |
| `template-write` | `Template.Write` | `TemplateWrite` | Management | `sform story template set`, `edit` and `delete`, and `sform story import` of templates |
| `annotation-manage` | `Annotation.Manage` | `AnnotationManage` | Management | `sform story delete` and `restore`, `sform story set --restore-deleted`, the **Restore and edit** of `sform story edit`, and `sform story import` of annotations |
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
Commands print what they read or write, and the job log keeps everything a step prints. A step that prints a resolved configuration with secrets puts those values into the log, and `--export` alone still prints the whole model. To use a configuration in a job, run `sform story config export`: it masks the values that bindings produced before it writes anything else, then puts the configuration into the environment of the job with `--github-env`, into a file with `--export`, or both, and prints no value. `sform story config` with `--export` and `--format` or `--content-only` also prints nothing, but masks nothing either. See [Export to GitHub Actions](#export-to-github-actions).

The runner also reads `##[command]` anywhere in a line that a step prints as a workflow command, so a printed configuration can set outputs or annotations of the step through its names and values. The messages of `sform` write every `#` as `\u0023`.
:::

::: tip
The details, such as the token URL rules, the certificate header for a local API, how a federated machine gets and renews its token, and every exit code, are in [the technical documentation](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#machine-sign-in-for-pipelines). [Scopes of machines](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#scopes-of-machines) lists the operations of every scope.
:::

## Read a configuration

`sform story config <key>` prints the configuration of an annotation: its whole model, with the version and the audit stamp, as coloured JSON. `-r` reads the resolved configuration, with its bindings evaluated, and `--version <n>` the stored content of a version.

::: warning
`--format`, `--content-only`, `--env-prefix` and `sform story config export` are newer than the `sform` on NuGet, which ends such a command as an unrecognized option or argument. Until a release has them, install a `sform` packed from the repository, as [Until sform is published](/cli/github-action#until-sform-is-published) shows.
:::

``` text
sform story config <key> [-r|--resolved | --version <n>] [-f|--format json|flat|properties|env|yaml]
                         [--content-only] [-e|--export <file>] [--env-prefix <PREFIX>]
```

`--format` writes the content alone in a flat format: `flat`, a JSON object of the .NET `IConfiguration` keys (`db:hosts:0`); `properties` for Spring Boot (`db.hosts[0]`); `env`, a `.env` file (`DB__HOSTS__0`); or `yaml`. `sform` formats the content itself, with the library the API uses for `?format=`, so the text is the same as the API's, also for `--version`. `--env-prefix APP_` puts a prefix before every `env` name (`APP_DB__HOST`), as .NET `AddEnvironmentVariables("APP_")` expects; it applies to `--format env` only. A document a format cannot write, for example a name with a space in `env`, stops the command with the rule and the JSON path of the name. The formats and their rules are in [Configuration Formats](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-formats.md).

| Options | Standard output | The `--export` file |
|---|---|---|
| None, or `--format json` | The model, with line numbers and colours | The model; `sform` asks before it replaces a file |
| `--format flat`, `properties`, `env` or `yaml` | The text with line numbers, without colours; nothing with `--export` | The text |
| `--content-only` | The content alone, as it is: the text of the format, or for `json` the content of the model, indented by two spaces (`{}` without content); nothing with `--export` | The same |

- With `--format flat`, `properties`, `env` or `yaml`, or `--content-only`, the content is written as UTF-8 without a byte order mark and with LF line ends, also to a Windows console. The `--export` file is then replaced without asking, and on Linux and macOS a file of a resolved configuration is readable and writable by its owner alone (mode `0600`) from the moment it exists: `sform` writes a new file next to it and moves it over the old one, so a reader that opened the old file keeps that file, and a symbolic link at the path is replaced, not followed. The model (no option, or `--format json` alone) is exported as before: `sform` asks before it replaces a file, and the file gets the default permissions.
- With `--content-only`, every message of the command goes to standard error, one line each, so standard output holds the content alone and can be piped or redirected. Parsing errors, sign-in failures, refusals of the API (`401`, `403`) and crashes are still printed to standard output; they end with a non-zero exit code, and the `--export` file is not written.
- Windows PowerShell 5.1 re-encodes what `>` redirects, so use `--export` there. PowerShell 7 and the other shells keep the bytes.

``` powershell
sform story config exe.northwind.invoicing.production -r -f env --content-only > billing.env
sform story config exe.northwind.invoicing.production -r -f yaml -e values.yaml
sform story config exe.northwind.invoicing.production --version 3 -f properties --content-only
```

A resolved configuration holds secret values only for a caller with the right to read secrets: a user with the `ContributorWithSecrets` role, or a machine with `Secrets.Read`, which by default must call with its certificate over mTLS. When the API withholds the secrets (`X-2S-Secrets-Withheld: mtls-required`), the values of secret bindings are their expressions, and `sform` warns about it. A machine without `Secrets.Read` gets the expressions without that header: when a resolved value starts with `@`, `sform` also reads the effective configuration, and warns about every value that is still the expression the effective configuration has at the same path. Warnings and errors name paths, never a value. To hand a resolved configuration to a CI job with masks, use `sform story config export`, see [Export to GitHub Actions](#export-to-github-actions).

| Exit code | When |
|---|---|
| `0` | The configuration was printed or written |
| `2` | Nothing was written: an invalid key, format or prefix; options that do not go together (`--version` with `--resolved`, `--env-prefix` without `--format env`); a configuration or version that does not exist; a request the API rejects (`400`, `409`, `412`, `501`); a document the format cannot write; a binding that cannot be evaluated (`422`). Also an `--export` file of the content that cannot be written |
| `-1` | The command line does not parse: an unknown option, an option of `config export` such as `--github-env` or `--mask`, an option without its value, an option that takes a value given twice (`--format env --format yaml`), or `--version abc`. Shells on Linux and macOS show `255` |
| `44` | The API refused the read (`401`, `403`) |
| `3` | The service failed (`5xx` but `501` and `505`, `408`, `429`) or could not be reached; a retry may help |
| `1` | A crash of the tool |

### Export to GitHub Actions

`sform story config export <key>` hands a resolved configuration to a CI job. It always reads the resolved configuration and always masks. With `--github-env` it exports the configuration into the environment of a GitHub Actions job: every value becomes a variable with its `env` name in `GITHUB_ENV`, which the runner reads after the step. With `--export` it writes the configuration to a file. Like the options of [Read a configuration](#read-a-configuration), the command needs a `sform` packed from the repository until a release has it. The [GitHub Action](/cli/github-action) installs `sform` and runs this command in one step, and shows how to pack it.

``` text
sform story config export <key> [--github-env] [--mask bound|all] [--prefix <PREFIX>]
                                [--allow-unprefixed] [--allow-unresolved]
                                [-e|--export <file> [-f|--format env|flat|properties|yaml|json]]
```

| Option | Meaning |
|---|---|
| `--github-env` | Append every value to `GITHUB_ENV` as a variable with its `env` name. Needs `--prefix` or `--allow-unprefixed`, and a step of a GitHub Actions job (`GITHUB_ENV` set) |
| `--mask bound\|all` | `bound`, the default, masks the values that bindings produced, `all` every string and number; see **Masking** below |
| `--prefix <PREFIX>` | A prefix before every variable name, for example `APP_` (an ASCII letter, then ASCII letters, digits and `_`): for `--github-env`, and for an `--export` file in `env` |
| `--allow-unprefixed` | Let `--github-env` write the names of the configuration without a prefix |
| `--allow-unresolved` | Export binding expressions that were not resolved as they are, instead of refusing |
| `-e\|--export <file>` | Also write the content alone to this file. It is replaced without asking, and on Linux and macOS it is readable and writable by its owner alone (mode `0600`) from the moment it exists, as for `story config` |
| `-f\|--format` | The format of the `--export` file: `env`, the default, `flat`, `properties`, `yaml`, or `json` for the content of the model; it needs `--export`. Without `--format` the file is `env`: an `--export` file of the old `story config --mask`, which was JSON, now needs `-f json` |

The command needs a target: `--github-env`, `--export`, or both; `--mask` alone only prints the masks. `--resolved` and `--content-only` are implied, so `story config export` takes neither, nor `--version`. Standard output holds only the `::add-mask::` commands, every message of the command goes to standard error as one line, and the content is written as UTF-8 without a byte order mark and with LF line ends. Parsing errors, sign-in failures, refusals of the API (`401`, `403`) and crashes are still printed to standard output; they end with a non-zero exit code before anything reaches `GITHUB_ENV` or the `--export` file. `export` is a subcommand of `config`, so `sform story config export` never reads `export` as a key, as with `set` or `versions`; an annotation key has at least two segments anyway.

``` yaml
# GitHub Actions: the later steps read APP_DB__HOST, APP_DB__PASSWORD, …
# A certificate machine over mTLS, the recommendation for secrets (see below)
- run: printf '%s' "$CERTIFICATE" | base64 --decode > "$RUNNER_TEMP/storyteller.pfx"
  env:
    CERTIFICATE: ${{ secrets.STORYTELLER_CERTIFICATE_BASE64 }}
- run: sform story config export exe.northwind.invoicing.production --github-env --prefix APP_
  env:
    STORYTELLER_ENDPOINT: https://storyteller.example.com/api
    STORYTELLER_CLIENT_CERTIFICATE: ${{ runner.temp }}/storyteller.pfx
    STORYTELLER_CLIENT_CERTIFICATE_PASSWORD: ${{ secrets.STORYTELLER_CERTIFICATE_PASSWORD }}
- run: dotnet run --project src/Billing   # AddEnvironmentVariables("APP_")
```

In one run, with one resolved and one effective read, `sform`:

1. checks the variables, and writes nothing when a name or a value is refused (see below);
2. writes the `::add-mask::` commands to standard output, before any value reaches `GITHUB_ENV` or a file;
3. appends every variable to `GITHUB_ENV` as `NAME<<ghadelimiter_…`, the value, and the delimiter, with one random delimiter that no value contains, so a value with quotes or line breaks arrives as it is; when an earlier command left the last line of the file without its line break, `sform` ends that line first;
4. writes the `--export` file, when it is given;
5. writes a summary to standard error: the names of the variables, how many values it masked, and the names it did not mask because they are shorter than four characters.

| Exit code | When |
|---|---|
| `0` | The configuration was exported |
| `2` | Nothing was written: an invalid key, format, mask or prefix; no target; options that do not go together (`--format` without `--export`, `--prefix` with neither `--github-env` nor an `--export` file in `env`, `--github-env` with neither `--prefix` nor `--allow-unprefixed`, `--allow-unprefixed` without `--github-env`); `--github-env` outside a GitHub Actions job; a configuration that does not exist; a request the API rejects (`400`, `409`, `412`); a document the format cannot write; a binding that cannot be evaluated (`422`); withheld secrets, unresolved bindings and refused variables. Also `GITHUB_ENV` or an `--export` file that cannot be written: the masks are printed by then, and when the `--export` file fails, `GITHUB_ENV` was already appended |
| `-1` | The command line does not parse: an unknown option, an option of `story config` such as `--resolved` or `--content-only`, an option without its value, or an option that takes a value given twice. Shells on Linux and macOS show `255` |
| `44` | The API refused a read (`401`, `403`) |
| `1` | Another failure of the API (`500` and the other statuses), or a crash |

**What later steps see.** A variable in `GITHUB_ENV` reaches every later step of the job, actions of other authors included, and the log prints it in the `env:` block of every such step; only masked values are hidden there. Export what the job needs: a prefix keeps the names apart, `--mask all` hides every string and number, and `--export` to a file under `$RUNNER_TEMP` without `--github-env` keeps the values out of the environment of the other steps.

**Names.** The names come from the configuration, so whoever edits the configuration could steer the later steps: `PATH`, `NODE_OPTIONS`, `LD_PRELOAD` or `RESTORESOURCES` change which tools run and where they get packages. `--github-env` therefore needs `--prefix`, for example `APP_`, or `--allow-unprefixed` for a job where the people who edit the configuration may steer the job anyway. Either way, names that the runner reserves or that change how later steps or their tools run are refused, compared without regard to case and with the prefix. No such list is complete (MSBuild reads every variable as a property), which is why the prefix is required.

::: details The refused names
`NODE_OPTIONS`, `PATH`, `HOME`, `SHELL`, `ENV`, `BASH_ENV`, `CDPATH`, `IFS`, `PS4`, `PROMPT_COMMAND`, `SHELLOPTS`, `BASHOPTS`, `GLOBIGNORE`, `CLASSPATH`, `CC`, `CXX`, `MAKEFLAGS`, `JAVA_TOOL_OPTIONS`, `_JAVA_OPTIONS`, `JDK_JAVA_OPTIONS`, `JAVA_OPTS`, `MAVEN_OPTS`, `GRADLE_OPTS`, `SBT_OPTS`, `PYTHONPATH`, `PYTHONSTARTUP`, `PYTHONHOME`, `PERL5OPT`, `PERL5LIB`, `PERLLIB`, `RUBYOPT`, `RUBYLIB`, `NODE_PATH`, `NODE_EXTRA_CA_CERTS`, `NODE_TLS_REJECT_UNAUTHORIZED`, `LUA_INIT`, `RUSTC_WRAPPER`, `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `NO_PROXY`, `SSL_CERT_FILE`, `SSL_CERT_DIR`, `REQUESTS_CA_BUNDLE`, `CURL_CA_BUNDLE`, `GOFLAGS`, `GOPROXY`, `GONOSUMDB`, `GOSUMDB`, `GOINSECURE`, `RESTORESOURCES`, `KUBECONFIG`, `PATHEXT`, `COMSPEC` and `PSMODULEPATH`, and every name that starts with `GITHUB_`, `RUNNER_`, `ACTIONS_`, `INPUT_`, `STATE_`, `LD_`, `DYLD_`, `BASH_FUNC_`, `DOTNET_`, `COREHOST_`, `CORECLR_`, `COMPLUS_`, `MSBUILD`, `NUGET_`, `NPM_CONFIG_`, `YARN_`, `PIP_`, `CARGO_`, `DOCKER_`, `GIT_` or `STORYTELLER_`.
:::

`sform` also refuses a value with the character U+0000, a variable longer than 32,767 characters as `NAME=value` (the limit of Windows), and variables of more than 512 KiB together.

**Masking.** `--mask bound`, the default, masks the values that bindings produced. The resolved read does not say which values are secret, so `sform` compares it with the effective configuration: a string or a number is masked when its path has no value in the effective configuration, or another one. When the two reads return different versions, or the effective read fails, every string and number is masked, and the summary says why. Booleans and empty or blank values are never masked. Under `bound`, a value shorter than four characters is not masked either, as masking `1` or `on` would hide those characters everywhere in the log; the summary names it. `--mask all` masks every string and number, whatever its length. A value with line breaks gets one mask for the whole value and one for each of its lines, whatever their length. The runner also hides a few encodings of a masked value, its JSON escape among them, which is how `flat` and the JSON content write it; with `--export` in `properties`, `env` or `yaml`, a masked value whose text in the file is neither gets one more mask for that text, such as `P\u00E4ss` for `Päss` in `properties`.

`bound` is a heuristic. It also masks the public values that bindings produced; it cannot recognise a secret stored as a plain value in a configuration (use `--mask all`, or keep secrets as secret key/values or vault references); and it masks a secret that a binding put into a longer string only as that whole string.

**Secrets need more than a credential.** The machine needs `Secrets.Read` (`sform account machine create -r --scope secrets-read`) and one of these sign-ins:

- **Recommended: a certificate machine over mTLS**, as in the example above. It receives secret values under the default policy of a project, so the project keeps `RequireMtlsForSecretDelivery` on and every machine without a certificate still gets no secret value. The job writes the `.pfx` file from a secret under `$RUNNER_TEMP`. It needs an API behind a front end that forwards the client certificate. **Set up a certificate machine** below shows the steps.
- **Possible: a federated machine** (GitHub OIDC), as in [Sign in from a pipeline](#sign-in-from-a-pipeline). It needs no stored secret, only `permissions: id-token: write` and the machine id, a GUID, in `STORYTELLER_CLIENT_ID`. It receives secret values only when an Administrator sets `RequireMtlsForSecretDelivery` to `false` for the project: `sform story kv policy set --require-mtls false`.
- **Possible: an API key or client credentials**, under the same change of the policy.

The policy is per project: turned off, it lets every machine of the project with `Secrets.Read` receive secret values, and every machine with `Secrets.Write` write secrets, without mTLS, with an API key, client credentials or a federated token, not only the machine of the job.

Without `Secrets.Read` or such a sign-in, the export stops with exit code `2` on the withheld secrets, or on the expressions of the bindings that were not resolved; `--allow-unresolved` exports such expressions as they are.

**Set up a certificate machine.** `sform account machine create` without `-k` creates the default kind of the project, which is an API key unless an Administrator changed it. So a certificate machine takes `-k certificate`, and an Administrator enables the kind for the project first: `sform account machine machine-auth` prints the policy, and with `-k` it sets the whole policy, so name the kinds it printed, the default kind first, add `-k certificate`, and add `-l` when the project has a certificate lifetime of its own. A user with the `ContributorWithSecrets` role then creates the machine. `machine create` prints the password of the certificate once. The repository keeps the password as the secret `STORYTELLER_CERTIFICATE_PASSWORD`, and the file, in base64 on one line, as `STORYTELLER_CERTIFICATE_BASE64`:

``` bash
sform account machine machine-auth                              # the enabled kinds, the default kind and the lifetime
sform account machine machine-auth -k api-key -k certificate    # the kinds it printed, and certificate
sform account machine create -r --scope secrets-read -k certificate -o storyteller.pfx
base64 -w0 storyteller.pfx | gh secret set STORYTELLER_CERTIFICATE_BASE64
gh secret set STORYTELLER_CERTIFICATE_PASSWORD                  # paste the password that machine create printed
rm storyteller.pfx
```

The certificate expires after the certificate lifetime of the project, or after the days that `-l` of `machine create` gives. Before then, the two secrets need a new certificate and its password.

`--export` without `--github-env` writes only the masks and the file, `env` unless `--format` says otherwise, for a job that keeps the configuration in a file for the one step that needs it:

``` yaml
- run: sform story config export exe.northwind.invoicing.production -e "$RUNNER_TEMP/billing.env"
- run: docker compose --env-file "$RUNNER_TEMP/billing.env" up --detach
```

## Write a configuration

`sform story config set <key>` merges a document into the stored content of the configuration of an annotation; with `--replace` the document replaces the content. The document comes from a JSON file (`-i`) and from inline properties (`-x path=value`), in this order. `sform story template set <type>` takes the same `-i`, `-x` and `--replace` for the template of a type.

::: warning
The paths of `-x` are newer than the `sform` on NuGet, which stores `-x db.port=5432` as a top-level property named `db.port`, and `-x "hosts[0]=a"` as one named `hosts[0]`, without an error. Until a release has them, install a `sform` packed from the repository, as [Until sform is published](/cli/github-action#until-sform-is-published) shows.
:::

``` text
sform story config set <key> [-i|--import <file>] [-x|--properties <path=value> ...] [--replace] [-f|--force]
```

``` bash
sform story config set exe.northwind.invoicing.production -x "db.host=sql.example.com" -x "db.port=5432"
sform story config set exe.northwind.invoicing.production -x "Logging.LogLevel['Microsoft.AspNetCore']=Warning"
sform story config set exe.northwind.invoicing.production -i invoicing.json -x "hosts[0]=a" -x "hosts[1]=b" --replace
```

**The path.** The name before `=` is a path, written as the errors of `sform` and of the API write paths:

| Step | Written | Example |
|---|---|---|
| A name of ASCII letters, digits, `_` and `-` that starts with a letter or `_` | `.name`; the first step without the dot | `db.port` |
| Any other name | `['name']`; inside, `\'` is a quote and `\\` a backslash | `Logging.LogLevel['Microsoft.AspNetCore']`, `['$schema']` |
| An item of an array | `[index]` | `hosts[0]`, `list[1].name` |

- The values are written one by one, in the order of the command line, over the `-i` file, and the objects and arrays on the way are created: `-x db.host=x -x db.port=5432` sets `{ "db": { "host": "x", "port": 5432 } }`.
- The value is the rest of the argument after the `=` that ends the path, so `-x "['a=b']=c=d"` sets the name `a=b` to `c=d`. `true` and `false` become booleans and numbers become numbers, as before.
- An index names an existing item, which it replaces, or the next one, which it appends: `-x "hosts[0]=a" -x "hosts[1]=b"` builds `["a", "b"]`.
- A path from an error, such as `$.Logging.LogLevel['Microsoft.AspNetCore']`, can be given as it is, `$` included; in bash and zsh, a path that starts with `$[` is given without its `$` (see **Shells**). Inside the quotes, `\n`, `\r`, `\t` and `\uXXXX` are read as errors print them (a `#` is printed `\u0023`), so a backslash of a name is always written `\\`.

**Errors.** When a value cannot be set, `sform` sends nothing, writes one line to standard error and ends with exit code `2`. The line names the inline property, its character or its path, never a value. A value cannot be set when:

- the path is not valid, or no `=` follows it;
- the path goes through a value that is not an object or an array, `-x a=1 -x a.b=2`, also when `a` comes from the `-i` file; or it uses an object as an array, or the other way round;
- an index is past the end of the array: `-x "hosts[1]=b"` without `hosts[0]`;
- the path starts with an index, or holds an empty name, `['']`.

**What the API does with the document.** Without `--replace`, the API merges the document into the stored content: objects merge, arrays are joined (an item that is already there is not added again), and `null` never erases. The checks above see the document that `sform` builds, not the stored content. So:

- `-x "hosts[0]=x"` adds `x` to a stored `hosts` array rather than replacing its first item, and `-x "list[0].name=n"` adds the item `{ "name": "n" }`;
- a value of another kind replaces the stored one: `-x db.port=5432` over a stored text `db` stores `{ "db": { "port": 5432 } }`.

To change the items of a stored array, write the whole content with `-i <file> --replace`, or use `sform story config edit` or `sform story config patch`.

**Shells.** Quote every `-x` argument:

- **bash and zsh:** use double quotes. Inside them, write `\$` for a `$` of a name, as in `"retries['\$binding']=jlogic"`, and `\\\\` for the `\\` of a name, and give a path from an error that starts with `$[` without its `$`, as `"['my.key'].x=1"`: `"$[…"` is arithmetic expansion there, while `"$.db.port=1"` passes as written. zsh stops on an unquoted `hosts[0]=a`, which it reads as a pattern.
- **PowerShell:** a `$name` inside double quotes is a variable. Use single quotes, with the quotes of `['…']` doubled: `'retries[''$binding'']=jlogic'`.
- **GitHub Actions:** write the command in a `run: |` block, where a `#` or a `: ` does not end it.

An unquoted `a['b.c']=1` reaches `sform` as `a[b.c]=1`, and an expanded `$binding` leaves `['']`; the errors of both say so.

**Changed.** The name of `-x` used to be a top-level property as written. Now a name that is not plain (with a dot, a space, `:`, `$`, a leading digit or `-`, or a letter outside ASCII) is written `['name']`, and an empty name is refused: `-x "['a.b']=1"` sets the property `a.b`, as `-x a.b=1` did before. The `IConfiguration` spelling `-x "Logging:LogLevel:Default=Warning"` now fails; write `Logging.LogLevel.Default`. A name matches a property of the file in any case, as `IConfiguration` keys do, and keeps the spelling of the file. A date-like value of `-x` becomes a date, while one of the `-i` file stays its text.

**Patch and edit.** `sform story config patch <key>` applies a JSON Patch, from `-i <file>` or from the changes made in the editor, and `sform story config edit <key>` replaces the stored content with what you saved in the editor.

A configuration has no custom properties or labels of its own: they belong to the annotation, and `sform story set <key> -c <name=value> -l <label>` sets them. Earlier versions of `config set` accepted `-c` and `-l` and sent nothing; they are now unknown options, which end the command with `-1`.

| Exit code | When |
|---|---|
| `0` | The configuration was stored |
| `42` | Nothing changed (`--replace`, `patch` and `edit`) |
| `40` | You declined the confirmation of `patch` or `edit` |
| `43` | `patch` or `edit` without `-y\|--yes` and without a terminal, see [Run without a terminal](#run-without-a-terminal) |
| `2` | The content violates the schema, which lists the failing configurations; a file that is not a JSON object; a request the API rejects (`400`, `404`, `409`, `501`); a `test` operation of a patch that fails (`412` `PatchTestFailed`) |
| `44` | The API refused the write (`401`, or `403` with its message, for example for a Reader or a machine without `Configuration.Write`) |
| `3` | The service failed (`5xx` but `501` and `505`, `408`, `429`) or could not be reached |
| `1` | The editor of `edit` or `patch` exited with a non-zero code |

## Run without a terminal

`sform` never waits for an answer that cannot come. When its standard input is redirected, as in a pipe, from a file, in a CI job or in a script started by another program, a command that would ask something stops instead: it writes what it would have asked and the option that answers it to standard error, and ends with an exit code.

| Command | Asks for | Without a terminal | Exit code |
|---|---|---|---|
| `sform account` | the organization, when you belong to several | pass `-o` (`--organization`) with its id | `2` |
| `sform account set` | the default project | pass `-p` (`--projectKey`) with `<organization>.<project>` | `2` |
| `sform account points get`, `grant`, `revoke` | the access point | pass `-p` (`--pointKey`, `--point`) | `2` |
| `sform story set` | what happens to deleted annotations | pass `--restore-deleted` or `--recreate` | `2` |
| `sform story kv set` | the value of a secret | pass `--stdin`, `-i` (`--import`) with a file, or the value | `2` |
| `sform account machine renew -c` | the password of the certificate | pass `--password-stdin` and the password as the first line of standard input (an empty line for none) | `2` |
| `sform story delete --permanent`, `story kv delete --permanent`, `story kv purge`, `story kv import`, `story kv recipients remove` | a confirmation | pass `-y` (`--yes`); `kv import` also takes `--dry-run` | `43` |
| `sform story config patch` | a confirmation of the patch | pass `-y` (`--yes`) | `43` |
| `sform story config edit`, `template edit`, `schema type edit`, `schema annotation edit`, `schema descendant edit` | a confirmation of the changes | pass `-y` (`--yes`) | `43` |
| `sform account register`, `sform account points create`, `sform assist` and `assist key` | names and choices without options | run them in a terminal | `43` |

A missing value ends with `2`, as when it is missing on the command line; a confirmation, or a command that needs a terminal, with `43`. A command still checks what comes before its question and ends with that code: `sform account register` of a registered user says so and ends with `0`, `story kv import` with nothing to change and `story config patch -i` with an empty patch end with `42`, and an unknown key or a missing file with `2`; `story kv import` shows its plan before it stops. The edit commands, and `story config patch` without `-i`, stop before the editor opens, so no edit is lost to a question nobody can answer. An edit command without a configured editor (and without `$VISUAL` or `$EDITOR`) stops with `43` too and names `sform account settings editor` to set one. `-e` of a `get` command rewrites an existing file without asking. Standard input is read only where an option asks for it (`--stdin`, `--password-stdin`).

Without any console at all, as when a program redirects standard input, output and error, `sform` writes its output as plain text: lines of at most `COLUMNS` characters (250 when it is not set), without trailing spaces.

### Output for scripts

These commands write their payload to standard output as it is, in UTF-8, without the margin and the line wrapping of the other output. A successful run writes nothing else there: its notes go to standard error, so a pipe or a redirect gets the payload alone, however long its lines are:

| Command | Standard output of a successful run | Standard error |
|---|---|---|
| `sform story config <key> --content-only` | The content, see [Read a configuration](#read-a-configuration) | The messages of the command, also of a failed run |
| `sform story kv export` without `-o` | The document in its format (`json`, `flat` or `env`), with LF line ends | The note on secrets without a value, which `flat` and `env` leave out |
| `sform story kv get <key> --value-only` | The value, and a line end | Nothing |
| `sform story schema type\|annotation\|descendant diff … --format unified` | The unified diff: `@@ -a,b +c,d @@` hunks of lines that start with `+`, `-` or a space, with LF line ends; nothing for a diff without changes | "No changes detected." |

Only the payload of a successful run is alone on standard output. A run that is refused or fails writes its message to standard output, as every other command does, and ends with a non-zero exit code (see [Exit codes](#exit-codes)): an unknown key or format, a refusal of the API, a value that cannot be decrypted. `config --content-only` is the exception: it writes its own messages to standard error, and only parsing errors, sign-in failures, refusals of the API (`401`, `403`) and crashes to standard output. `kv get --value-only` of a pair without a value, such as a secret without `--reveal`, prints "The value is a secret; add --reveal." or "The pair has no value." and ends with `44`. So a script checks the exit code before it uses what it captured:

``` bash
set -e
sform story kv export --format env > .env
TOKEN="$(sform story kv get api.token --reveal --value-only)"
sform story schema type diff exe 3 1 --format unified > exe.diff
```

### The device code for programs

A program that runs `sform account` and confirms the code itself, such as an automated test, sets `SFORM_DEVICE_CODE_OUTPUT=json`. The prompt is then also written to standard error as one JSON line, at once, before any other output of the prompt and before `sform` starts to wait:

``` json
{"userCode":"BCDF-GHJK","verificationUri":"https://auth.example.com/device","verificationUriComplete":"https://auth.example.com/device?user_code=BCDF-GHJK","expiresIn":599}
```

`verificationUriComplete` is `null` where the provider has no such address, and `expiresIn` is in seconds. Only the value `json`, in any case, turns it on; the text prompt stays the same either way.

## Exit codes

Every command ends with one of these codes. A refusal or a failure of the API is never a crash: `sform` prints the server's message and, when the response has one, its error code (`Error code: AccessDenied`), and ends with the code of the status. A refusal without a message of the server prints "You are not authorized to execute the command or you don't have access to requested resources.", any other status without one "The request failed with N Status.". `story export` and `story import` print the message of a refused start or download without the error code.

| Exit code | When |
|---|---|
| `0` | Success |
| `-1` | The command line does not parse: an unknown command or option, or an option without its value. Shells on Linux and macOS show `255` |
| `2` | Wrong input: a value `sform` refuses before any call, a missing file or a file that is not a JSON object, a key, configuration or version that does not exist, or a request the API rejects (`400`, `404`, `409`, `412`, `413`, `422` and the other `4xx`), for example a key it cannot parse (`InvalidKey`), a certificate renewal before its window (`NotInRenewalWindow`) or the revoke of a role lower than the member's (`ElevatedRole`). Also a `501` or `505`, which no retry changes: a deployment without the feature, such as `story kv policy byok-setup` without a customer-managed key provider (`ProtectionNotConfigured`) |
| `3` | The service failed or could not be reached: a `5xx` but `501` and `505`, a `408` or `429`, a refused connection, a name that does not resolve, or a timeout. A retry may help |
| `40` | The command stopped before it finished: you declined a confirmation, or a job of `story export` or `story import` outlasted the wait and continues on the server |
| `42` | Nothing to do: no changes, or nothing to import or delete |
| `43` | The command needs a terminal or a confirmation, see [Run without a terminal](#run-without-a-terminal); or your account is not registered yet (`account`, `account set` and the `account points` commands), run `sform account register` |
| `44` | Not authorized: the sign-in failed or was declined, or the API refused the request (`401`, `403`) |
| `45` | A partial result, for example a bulk operation that skipped some keys |
| `1` | A crash of the tool, with the log file named in the output; a job of `story export` or `story import` that failed on the server, with its error; or an editor of an `edit` command (or of `config patch` without `-i`) that exited with a non-zero code |

## Where sform keeps its files

| File | Default location |
|---|---|
| `sform.settings.json` (the editor and the default project, see [Settings](#settings)), the session (`authkit.cache`, or `msal.cache` for Entra ID) and `telemetry.json` | your `.42for.net` folder: `%LOCALAPPDATA%\.42for.net` on Windows, `~/.42for.net` elsewhere |
| `auth.discovery.json` (the sign-in settings of the server), `logs/`, and the `access.default.json` and `editor.config.json` of earlier versions, which are still read | next to `sform` itself, the folder of the tool |

`SFORM_HOME` moves every one of them into one folder, so that two sessions, two default projects or two users can live side by side, each in its own folder. It must be an absolute path: a relative one, or a path to a file, stops every command with exit code `2` before anything is read or written. A folder that does not exist yet is created by the first command that writes to it. `app.config.json` stays next to `sform`.

``` bash
export SFORM_HOME="$HOME/.sform/staging"
sform account
```

The session is encrypted for your user: with DPAPI on Windows, in the Keychain on macOS and with libsecret on Linux. The DPAPI file lives in the folder; a Keychain item or a libsecret entry does not, so with `SFORM_HOME` the item of the session is named after the folder, and each folder keeps a session of its own (without `SFORM_HOME` the names stay as they were, and so does your session). This holds for the AuthKit session; a deployment that signs in with Entra ID keeps sessions apart on macOS and Linux only with `SFORM_TOKEN_STORE=file`. A machine without any of them, such as a CI container without a keyring, sets `SFORM_TOKEN_STORE=file` next to `SFORM_HOME`: the session is then a plain file in `SFORM_HOME` (`authkit.cache.plaintext`, `msal.cache.plaintext`) that only your user may read, and `sform account` warns that it is in use. Treat that folder like a password and delete it when the job ends. Without `SFORM_HOME` the variable is ignored, and `sform account` says so; `file` is its only value.

| Variable | Effect |
|---|---|
| `SFORM_HOME` | The folder of every file `sform` writes, and of the session |
| `SFORM_TOKEN_STORE` | `file`: the session as a plain file in `SFORM_HOME`; ignored without `SFORM_HOME` |
| `SFORM_DEVICE_CODE_OUTPUT` | `json`: the device code prompt also as one JSON line on standard error |
| `SFORM_BROWSER`, then `BROWSER` | The program `sform account --browser` opens the sign-in page with, see [Sign in](#sign-in) |
| `SFORM_<section>__<key>` | A setting of `app.config.json` for this run, e.g. `SFORM_general__baseUrl` for the address of the API; it wins over the files |
| `COLUMNS` | The width of the output when `sform` has no console at all |
| `DO_NOT_TRACK`, `SFORM_TELEMETRY_OPTOUT` | Turn telemetry off, see [Telemetry](#telemetry) |

``` bash
# One run against the local API
SFORM_general__baseUrl=http://localhost:7071/api sform story -p acme.billing
```

An API that cannot be reached, a refused connection, a name that does not resolve or a request that times out, ends a command with exit code `3` and names the address `sform` called.

A build of `sform` from source (Debug) runs as `Development` and reads the user secrets of the CLI, unless `DOTNET_ENVIRONMENT` names another environment; a released `sform` always runs as `Production`.

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

The installation id is a random number, not derived from the machine or your account. It is kept in `telemetry.json`, next to the stored session: in `%LOCALAPPDATA%\.42for.net` on Windows and in `~/.42for.net` elsewhere, so it survives tool updates, or in `SFORM_HOME` when it is set. On the first run `sform` creates it and prints a notice to standard error. When `sform` reports a failure to Sentry, the report carries the same id.

Set `DO_NOT_TRACK` or `SFORM_TELEMETRY_OPTOUT` to `1`, `true`, `yes` or `on` to turn telemetry off. `DO_NOT_TRACK` is honoured by other tools too, `SFORM_TELEMETRY_OPTOUT` only by `sform`. With telemetry off, `sform` sends nothing and creates no id.

``` powershell
# This session only
$env:DO_NOT_TRACK = "1"

# Every new session of your user
[Environment]::SetEnvironmentVariable("SFORM_TELEMETRY_OPTOUT", "1", "User")
```

In a pipeline, set the variable in the `env` of the job like the sign-in variables above.

## Settings

`sform` keeps your preferences in `sform.settings.json`, in the same folder as the stored session and `telemetry.json`: `%LOCALAPPDATA%\.42for.net` on Windows and `~/.42for.net` elsewhere., or `SFORM_HOME` when it is set (see [Where sform keeps its files](#where-sform-keeps-its-files)). Tool updates keep it.

| Key | Values | When it is not set |
|---|---|---|
| `editor.type` | `vscode`, `neovim`, `vim` or `custom`: the editor of `story config edit`, `config patch`, `template edit` and the schema edit commands | `$VISUAL`, then `$EDITOR`, else `sform` asks on the first edit and saves the answer |
| `editor.command` | The command line of a `custom` editor, e.g. `code --wait`; `sform` appends the file path | – |
| `access.project` | `<organization>.<project>`: the default project of the commands without `-p` | `house.main` |
| `access.view` | The default view of the commands without `-v` | `default` |

`sform account settings` lists every key with the value the commands use and where it comes from: `settings`, a legacy file (see below), `$VISUAL` or `$EDITOR`, `STORYTELLER_PROJECT` or `STORYTELLER_VIEW`, a machine sign-in, which never uses the default project, or `default`. None of these commands needs a sign-in.

``` powershell
sform account settings                                           # the keys, their values and sources
sform account settings --path                                    # only the file, e.g. code "$(sform account settings --path)"
sform account settings --json                                    # the values as JSON
sform account settings editor                                    # pick the editor from a list
sform account settings editor vscode                             # or name it: vscode, neovim, vim
sform account settings editor custom --command "code --wait"    # any command; it should wait until you close the file
sform account settings set access.project contoso.payments
sform account settings set access.view preview
sform account settings unset editor.type                         # ask again on the next edit
```

`sform account set` sets the default project too, from the projects of your account, and `sform account register` and `sform account points create` set the project they create.

A custom command, and `$VISUAL` or `$EDITOR`, is the executable, or a path in quotes, followed by its arguments, e.g. `"C:\Program Files\Notepad++\notepad++.exe" -multiInst -nosession`; no shell expands it. On Windows `sform` finds the executable on the `PATH` with the extensions of `PATHEXT`, so `code --wait` starts `code.cmd`. When the editor of `$VISUAL` or `$EDITOR` cannot be started, `sform` asks for one instead.

Earlier versions kept the editor in `editor.config.json` and the default project in `access.default.json`, next to the `sform` assembly. `sform` still reads them while `sform.settings.json` has no section of its own for them, and moves both into it on the first change. `dotnet tool update` deletes the folder of the old version with the files in it, so after the update that brings `sform.settings.json` you choose the editor and run `sform account set` once more; from then on they survive updates.

A change rewrites the whole file, so comments in it are not kept. When the file is damaged, `sform` says so on standard error and uses no editor and no default project from it until you fix it: `sform account settings --path` shows where it is.
