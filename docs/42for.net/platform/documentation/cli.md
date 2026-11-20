# sform & CI

**`sform`** is the platform's command line: for you at the keyboard, and for your pipelines at 2 a.m. The **GitHub Action** wraps it, so a workflow gets a resolved configuration in one step.

> If the admin UI is the living room, `sform` is the workshop. Same house, more tools on the walls.

## Install and sign in

```bash
dotnet tool install 42.Platform.Cli --global --prerelease
sform account                   # sign in through your browser
sform account set -p house.main # the default project (and -v for a view)
```

`--prerelease` gets the newest version; our prereleases are safe to use. Every command has `--help`, and the help says which role a command needs.

## Ten commands to start with

| You want to | Run |
| --- | --- |
| See the catalog | `sform story` |
| Create or update an annotation | `sform story set exe.northwind.invoicing.production` |
| Read an annotation | `sform story get sbt.northwind` |
| Read a configuration, merged | `sform story config exe.northwind.invoicing.production` |
| …resolved, as a `.env` | `sform story config exe.northwind.invoicing.production -r -f env --content-only` |
| See which document set each value | `sform story config composition exe.northwind.invoicing.production` |
| Write a configuration | `sform story config set sbt.northwind -x "invoicing.currency=NOK"` |
| Undo a change | `sform story config restore sbt.northwind --version 2` |
| Take a view home as a git repository | `sform story export -f git -x` |
| Create a machine for a service | `sform account machine create -a exe.northwind.invoicing.production` |

The full tour, with every option and exit code, is on the [sform page](/cli/sform).

## Machines for pipelines

Nobody types a code in a pipeline, so a pipeline signs in as a **machine**. Create it once; it prints the `STORYTELLER_*` variables the job needs. With them set, `sform` never asks anyone anything.

| The job | Create the machine with |
| --- | --- |
| Reads configurations | `sform account machine create` (`Annotation.Read`, `Configuration.Read`) |
| Reads secrets too | `sform account machine create -r --scope secrets-read` |
| Writes configurations | `sform account machine create -w` |
| Keeps a view in git (export and import) | `sform account machine create -w -m configuration-transfer,schema-write,template-write,annotation-manage` |

::: tip No secrets in your CI at all
A **federated** machine trusts the job's own token (GitHub Actions OIDC, Kubernetes service accounts, cloud identities) and stores no secret anywhere. See [Sign in from a pipeline](/cli/sform#sign-in-from-a-pipeline).
:::

## The GitHub Action

```yaml
- uses: akobr/mono.me/src/Platform/GitHubAction@<commit>
  with:
    key: exe.northwind.invoicing.production
    export-env: true
    env-prefix: APP_
    cli-version: <the version of sform>
  env:
    STORYTELLER_ENDPOINT: https://api.2s.42for.net/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
- run: dotnet run --project src/Invoicing   # reads APP_INVOICING__CURRENCY, …
```

- Every value the bindings produced is **masked** in the log before anything else happens.
- `env-prefix` is required (or `allow-unprefixed: true`): a configuration must not be able to set `PATH` for your next step.
- `output-file` writes the configuration to a file too, as `env`, `flat`, `properties`, `yaml` or `json`.
- Pin a commit: the action has no release tags yet.

The whole story, including secrets over mTLS, is on the [GitHub Action page](/cli/github-action).

## Exit codes worth knowing

| Code | Means |
| --- | --- |
| `0` | Done |
| `2` | Wrong input: a key that doesn't exist, a file that isn't JSON, a request the API rejects |
| `3` | The service failed or couldn't be reached. Try again |
| `42` | Nothing to do: no changes |
| `44` | Not authorized: the sign-in failed, or the API said `401` or `403` |

Scripts can tell "fix it" (`2`, `44`) from "retry it" (`3`) without parsing a word of output.
