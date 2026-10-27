# Phase 5 of Flat Output Formats for Configurations

Linear: [424-104](https://linear.app/424fun/issue/424-104/flat-output-formats-for-configurations-on-the-api-sform-and-a-github).

## Overview

Phase 5 covers sections 8 and 9 of [Flat Output Formats for Configurations](../2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md) and the phase 5 docs of section 11. A composite action in `src/Platform/GitHubAction` installs an exact `sform` into a folder of its own and runs `sform story config <key> --resolved` once, exporting into `GITHUB_ENV`, into a file, or both; the names, masks, heredocs and refusals stay in `sform` with its tests (phase 4). There is no workflow for the action (section 9); the local checks below ran for this review. Three commits after the first corrected the action and its recipe, and three of them deviate from section 8 as written.

| Commit | Content |
|---|---|
| `8b63aef2` | `action.yml`, its README, the public page `cli/github-action`, the links and `CLAUDE.md` (sections 8, 9) |
| `56f669bf` | Review fix: the unused-prefix warning follows the format the read step uses |
| `dbc6a024` | Review fix: setup-dotnet is skipped only when a .NET 10 SDK is present |
| `8b090dbb` | Review fix: the recipe packs `sform` inside the mono.me checkout |

## What Was Done

### 8. The GitHub Action

**Inputs.** The inputs of the spec (`key`, `project`, `view`, `endpoint`, `client-id`, `export-env`, `env-prefix`, `allow-unprefixed`, `mask`, `output-file`, `format`, `cli-version`, `cli-source`). Each reaches a script only through the step `env:` under a private `SA_*` name; `STORYTELLER_PROJECT`, `STORYTELLER_VIEW`, `STORYTELLER_ENDPOINT` and `STORYTELLER_CLIENT_ID` are exported only for inputs that are set; the `sform` arguments are a bash array; every step has `shell: bash`. **Addition:** `format` also takes `json`, which `sform` writes as the content alone.

**Steps.** (1) "Check the inputs", (2) "Look for .NET 10", (3) `actions/setup-dotnet@v6` with `10.0.x` when no SDK was found, (4) "Install sform" with `dotnet tool install 42.Platform.Cli --version "$SA_CLI_VERSION" --tool-path "$RUNNER_TEMP/storyteller-action/sform-<version>"` (plus `--add-source` for `cli-source`, never `--prerelease`) in `working-directory: ${{ runner.temp }}`, reusing the folder when the tool is there, (5) "Read the configuration".

**Deviations from section 8.**

1. **Step (1) of the spec looked for a runtime.** It said to skip setup-dotnet when `dotnet --list-runtimes` lists `Microsoft.NETCore.App 10.`. The next step installs `sform`, a `net10.0` tool, which an older SDK cannot install and a runtime alone installs nothing with, so a runner with the .NET 10 runtime next to SDK 8 or 9, or a job on a `dotnet/runtime:10` image, failed in the install. Since `dbc6a024` the step looks for an SDK 10 in `dotnet --list-sdks`, which also brings the runtime `sform` runs on, and outputs its root; the read step exports it as `DOTNET_ROOT` when the job has none, as a tool in a folder of its own finds .NET through `DOTNET_ROOT`. The intent of the spec, to skip setup-dotnet only when the runner can install and run `sform`, stays.
2. **The check of `export-env` and `output-file` runs first, not after the install.** The spec placed it as step (3). It runs in "Check the inputs", before anything is installed, so a wrong use fails in seconds. That step also checks what the spec did not ask for: `key` is set and does not start with `-` (so it cannot become an option of `sform`), `cli-version` is an exact version (`^[0-9]+(\.[0-9]+){1,3}(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$`, as it also names the install folder), `export-env` and `allow-unprefixed` are booleans, and it warns when `env-prefix` is not used. **Review fix (`56f669bf`):** the warning tested the raw `format` input, so `format: ''` warned although the read step defaults an empty format to `env` and passes the prefix; it now defaults the format the same way.
3. **`--mask` is always passed.** The spec said `--github-env` when `export-env` is true and `--mask "$MASK"` otherwise. The read step always passes `--mask "${SA_MASK:-bound}"` and adds `--github-env` for `export-env`, so `mask: all` also applies to the variables; `sform` accepts both options together, and `bound` is the default of both.

**Docs.** The README and the public page `docs/42for.net/cli/github-action.md` (in the sidebar after `sform`, linked from `sform.md` and `authentication.md`) state what a machine needs to receive secrets, what later steps see, the prefix and the refused names, the packing recipe and what setup-dotnet changes for the rest of the job; `CLAUDE.md` lists the formats library and the action. **Review fix (`8b090dbb`):** the recipe ran `dotnet pack mono.me/src/Platform/Cli/src` from the root of the workspace, where the `global.json` of the calling repository chose the SDK (`NETSDK1045` for a repository on .NET 8 or 9); it now runs in the checkout (`working-directory: mono.me`).

**Not done, as planned:** a dedicated repository, Marketplace, `@v1` tags, and a `sform` with these options on NuGet (section 14).

### 9. CI for the action

No workflow, as planned. The checks that were possible locally ran on 2026-10-27 against the branch after its final review fixes, on Windows with Git Bash 5.2 (scratch scripts outside the repository):

- **`action.yml` parses** with the `yaml` package of ui.admin's `node_modules`; `actionlint` is not installed on the machine and did not run.
- **The scripts ran in bash against a packed `sform` and a stub API.** `dotnet pack src/Platform/Cli/src -c Release` gave `42.Platform.Cli.0.8.118-alpha-g5652d7d7ce.nupkg`. A Node driver ran every `run` step of the parsed `action.yml` as the runner does (`bash --noprofile --norc -eo pipefail`, the inputs with their defaults through the step `env:`, `working-directory`, a fresh `GITHUB_OUTPUT` per step, the job's `RUNNER_TEMP` and `GITHUB_ENV`, and the `if` of the setup-dotnet step), with `cli-source` set to the pack folder and an API key of the job. A Python stub answered the effective read (a `@config` and a vault binding) and the resolved read (their values, the password on two lines).
  - `export-env: true`, `env-prefix: APP_`: "Look for .NET 10" output `C:\Program Files\dotnet`, setup-dotnet was skipped, the tool was installed, and the read step printed only `::add-mask::` for the two bound values and the two lines of the password, then appended five heredocs (`APP_DB__HOST`, `APP_DB__PASSWORD` with both lines, `APP_DB__PORT`, `APP_FEATURES__0`, `APP_NAME`) to `GITHUB_ENV`.
  - `output-file` in `yaml` with `mask: all`, in the same job: the installed tool was reused, every string and number was masked, the file held the resolved configuration, and `GITHUB_ENV` was unchanged.
  - `format: ''` with `env-prefix` and `output-file`: no warning, and the file was `env` with the prefix.
  - Neither `export-env` nor `output-file`, `cli-version: 0.8.*`, `key: --help`, and `export-env: yes` each failed in "Check the inputs" with its `::error::` message, and nothing was installed.
  - Each run made one resolved and one effective read of the stub.

The README of the action points to this review for these checks.
