# Publishing the GitHub Action and Secrets in CI

Linear: [424-104](https://linear.app/424fun/issue/424-104/flat-output-formats-for-configurations-on-the-api-sform-and-a-github).

## Overview

This review covers sections 1 to 4 of [Publishing the GitHub Action and Secrets in CI](../2026-10-27%20Publishing%20the%20GitHub%20Action%20and%20Secrets%20in%20CI.md), delivered in its two phases. Phase 1 makes the pages on secrets in CI recommend a certificate machine over mTLS, with a federated machine and an API key or client credentials as alternatives under a change of the policy. Phase 2 makes the action ready for GitHub Marketplace (branding, a licence, a README with absolute links) and adds a workflow, run by hand from `main`, that releases it into a repository of its own with a tag `vX.Y.Z`, a moving major tag `vX` and a draft release. Nothing ran against GitHub: the dedicated repository does not exist yet and its owner is undecided, so the workflow ran locally against scratch bare repositories. A review of both phases found two gaps in the workflow and four in the docs. Six commits fixed them, and two of them add checks that section 3 does not list.

| Commit | Content |
|---|---|
| `8eb28f54` | Phase 1: the recommendation and its alternatives on four pages (section 1) |
| `0a43ef5b` | Phase 2: branding, `LICENSE`, the README, the release workflow and its docs (sections 2 to 4) |
| `d01c602e` | Review fix: the workflow refuses a version that does not sort above the newest release |
| `e6305fa9` | Review fix: the workflow refuses a README that does not use the repository of the action |
| `66ee7495` | Review fix: the trade-off of the policy names the writes of secrets too |
| `24ac129f` | Review fix: the pages show how to set up a certificate machine |
| `4fc02fe0` | Review fix: the sform page shows a certificate machine first in "Sign in from a pipeline" |
| `489cb7ab` | Review fix: the federated examples read a sandbox without secret bindings |

| Check | Result |
|---|---|
| `src/Platform/Cli/test` | 1055 passed, none failed (no code changes) |
| YAML of `action.yml`, the new workflow and the two existing ones | Parse without errors or warnings (`yaml` package of ui.admin's `node_modules`, unique keys), also at the end of the branch |
| `actionlint` | Not installed, not run |
| Release simulation of phase 2 | Every refusal, a first, a second and a repeated release, and a non-empty target behave as section 3 says; the token is in no log or file |
| Release simulation of the review fixes | Versions below the newest release and a README without the repository are refused |
| VitePress build of `docs/42for.net` | `build complete` with no dead links, the new anchors render; also after the review fixes |
| Other workflows | `ui-admin.yml` and the docs site workflow are unchanged; the new workflow runs only by hand |
| GitHub | Nothing created, changed or pushed |

## What Was Done

### 1. Secrets in CI: a recommendation and its alternatives

Phase 1 (`8eb28f54`) gives four places the same list, with the same wording:

- `docs/42for.net/cli/github-action.md`, "Secrets need more than a credential".
- `src/Platform/GitHubAction/README.md`, the same section.
- `docs/42for.net/cli/sform.md`, the paragraph **Secrets need more than a credential** under "Export to GitHub Actions".
- `docs/Platform/Storyteller/authentication.md`, the paragraph **Secrets in CI** in "Machine sign-in for pipelines", which replaces the note after the examples.

The machine needs `Secrets.Read` and one of three sign-ins. **Recommended:** a certificate machine over mTLS. It receives secret values under the default policy, so the project keeps `RequireMtlsForSecretDelivery` on; the job writes the `.pfx` file from a secret under `$RUNNER_TEMP`; the API needs a front end that forwards the client certificate. **Possible:** a federated machine (GitHub OIDC), with no stored secret, only `permissions: id-token: write` and a GUID client id, which receives secret values only after `sform story kv policy set --require-mtls false`. **Possible:** an API key or client credentials, under the same change of the policy. A sentence after the list states the trade-off: the policy is per project, so turning it off opens secrets to every machine of the project, not only the machine of the job. The certificate examples come first and are named as the recommendation; the federated examples come second, with the note on the policy.

The sentence on the exit code was checked against `sform`: withheld secrets always stop it with exit code `2`, and `--allow-unresolved` applies only to expressions that were not resolved. Other pages that mention the mTLS secret delivery in general (`key-values.md`, `admin-ui.md`, `using-the-platform.md`, `configuration-provider.md`) are not about CI and are unchanged.

**Deviation: more examples changed.** To keep the certificate example first on every page, phase 1 changes more than the secrets sections. The opening and "Until sform is published" examples of `github-action.md` and the README, the "Export to GitHub Actions" example of `sform.md`, and the first example of "Machine sign-in for pipelines" in `authentication.md` sign in with `STORYTELLER_CLIENT_CERTIFICATE` and its password instead of `STORYTELLER_API_KEY`. `authentication.md` keeps an API key and client credentials as one sentence after its example.

**Deviation: three sentences outside the named sections.** `sform.md` "Sign in from a pipeline" points to the recommendation; the security note **Secret delivery** of federation in `authentication.md` states the trade-off and recommends a `Certificate` machine for CI; and the **Secrets in CI** paragraph leads into the list.

**Review fix (`66ee7495`): the trade-off names the writes.** The sentence said only that turning the policy off lets every machine with `Secrets.Read` receive secret values. `HttpRequestDataExtensions.GetSecretRightsAsync` withholds the right to write as well (`canRead && !withheld`, `canWrite && !withheld`), and `key-values.md` says that writes need the same policy. So turned off, the policy also lets every machine with `Secrets.Write` set, delete, protect and import secrets with an API key, client credentials or a federated token. The sentence now says "and every machine with `Secrets.Write` write secrets" in all five places: the four lists and the security note in `authentication.md`.

**Review fix (`24ac129f`): the setup of a certificate machine.** The only command on the pages was `sform account machine create -r --scope secrets-read`. Without `-k`, it creates the default kind of the project. `MachineAuthenticationOptions.DefaultCredentialKind` is `ApiKey`, and `MachineAuthenticationPolicies` enables only that kind for a project without a stored policy. Followed word for word, the recommendation gave an API-key machine whose secrets are withheld, and the action stopped with exit code `2`. `github-action.md` and the README gain a subsection "Set up a certificate machine", `sform.md` a paragraph **Set up a certificate machine.**, and `authentication.md` a paragraph **A `Certificate` machine for CI.** Each says the same:

1. An Administrator runs `sform account machine machine-auth` to print the policy, then sets it with `-k` for the kinds it printed, the default kind first, plus `-k certificate`, and `-l` when the project has a lifetime of its own. `-k` replaces the whole policy.
2. A user with `ContributorWithSecrets` runs `sform account machine create -r --scope secrets-read -k certificate -o storyteller.pfx`, which prints the password of the certificate once.
3. `base64 -w0 storyteller.pfx | gh secret set STORYTELLER_CERTIFICATE_BASE64` and `gh secret set STORYTELLER_CERTIFICATE_PASSWORD` store the file and the password, and the file is deleted.

Each also says that the certificate expires after its lifetime, so the two secrets need a new certificate before then. The Recommended bullets link to the setup.

**Review fix (`4fc02fe0`): `sform.md` "Sign in from a pipeline".** The new sentence there recommends a certificate machine, but the first GitHub Actions example after it still signed in with an API key, while `authentication.md` had replaced the same example. The example now writes the `.pfx` file from `STORYTELLER_CERTIFICATE_BASE64` and signs in with `STORYTELLER_CLIENT_CERTIFICATE` and its password, as `authentication.md` does. A sentence after it says that an API key or client credentials go into `env:` the same way, and the federated example stays second. That sentence names `STORYTELLER_API_KEY` without a `${{ }}` expression: VitePress evaluates one in inline code, and the build logged a `TypeError` on `STORYTELLER_API_KEY` until it was reworded.

**Review fix (`489cb7ab`): the federated examples.** The federated example of `github-action.md` and of the README said that its machine has no `Secrets.Read` and reads a configuration without secret bindings, but it read `exe.northwind.invoicing.production`, the key the certificate examples present with `APP_DB__PASSWORD`. Such a machine gets the expressions of the secret bindings, and the action stops with exit code `2`. Both examples now read `exe.northwind.invoicing.sandbox`, and the sentence before them says that the configuration of the sandbox has no secret bindings.

### 2. A Marketplace-ready action

- **`action.yml`** gains `branding` with `icon: sliders` and `color: purple`, which GitHub's metadata syntax lists as a supported Feather icon and colour. The `name` stays `Storyteller configuration`.
- **`LICENSE`** next to it holds the standard MIT text, `Copyright (c) 2026 Ales Kobr`.
- **`README.md`** uses absolute links: `https://42for.net/...` for the public docs, `https://github.com/akobr/mono.me/blob/main/...` and `.../tree/main/...` for the technical ones, and in-page anchors. No relative link is left, and every monorepo target exists on the branch. The README names the repository of its own as the way to use a release, as `<owner>/<name>@v1`, says that its owner and name are not decided, and keeps `akobr/mono.me/src/Platform/GitHubAction@<commit>` in the examples until the first release and for changes that are not released. It names the licence, and its "Checks" section names the one workflow of the action.

**Deviation: a shorter description.** The description of `action.yml` goes from 164 to 122 characters ("Read a resolved configuration from the 2S Platform, mask the values bindings produced, and export it to the job or a file."), with a comment that says why. The spec changes only the branding. GitHub's release form flags a description of 125 characters or more as a metadata warning, as community pull requests show ([matlab-actions/run-command#12](https://github.com/matlab-actions/run-command/pull/12), [getsentry/action-onlyrobots#11](https://github.com/getsentry/action-onlyrobots/pull/11)); "Publishing actions in GitHub Marketplace" does not state the number. To keep the old wording, revert that one line.

### 3. A release workflow

`.github/workflows/storyteller-action-release.yml` runs on `workflow_dispatch` with the required string input `version`. Its header comment names the spec. As section 3 plans:

- **"Check the run"** runs before anything is checked out or cloned and reports every problem before it fails. It refuses a ref other than `refs/heads/main`, a version that is not `X.Y.Z` (`^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$`, so no `v` and no suffix), a missing or malformed `STORYTELLER_ACTION_REPOSITORY`, and a missing `STORYTELLER_ACTION_TOKEN`. The messages name the variable and the secret, never their values.
- **The token of the run** has `contents: read` only. The input and the variable reach the scripts only through `env:` (`VERSION`, `ACTION_REPOSITORY`), and no run script holds a `${{ }}` expression. `actions/checkout@v4` is pinned by its major tag, as in the other workflows, and runs with `persist-credentials: false`.
- **"Commit and tag the release"** clones the repository of the action through `git -c credential.helper= -c credential.helper='!f() {…}; f'`. The helper answers `x-access-token` and the token from the environment of the step, and the empty helper drops every helper of the runner. The token is never in a URL, an argument, `.git/config` or the log, and `GIT_TERMINAL_PROMPT` is `0`. The step refuses an existing tag `vX.Y.Z` with `ls-remote`, in an assignment, so a failing `ls-remote` fails the step. An empty repository gets the branch `main`. The step removes everything but `.git`, copies `action.yml`, `README.md` and `LICENSE`, and commits "Release vX.Y.Z" as `github-actions[bot]`, with the source commit of the monorepo in the body. It creates the annotated tag `vX.Y.Z`, moves the annotated major tag `vX`, and sends the branch, the new tag and `+refs/tags/vX` in one `--atomic` push.
- **"Create the draft release"** runs `gh release create "vX.Y.Z" --repo <repository> --draft --verify-tag --title "vX.Y.Z" --notes …` with `GH_TOKEN` from the secret. The notes link the source commit and name `@vX` and `@vX.Y.Z`. The summary of the run links the draft and names **Publish this Action to the GitHub Marketplace**.

**Deviation: additions to section 3.** The workflow has a `run-name`, a `concurrency` group that never cancels a running release, and `timeout-minutes: 10`. It refuses a variable that names the monorepo itself. A non-empty repository of the action keeps its own default branch. The release commit uses `--allow-empty`, so a release with unchanged files still records its source commit.

**Review fix (`d01c602e`): a version must sort above the newest release.** The workflow committed on top of the branch and moved `vX` for any version whose tag did not exist. A lower version moved `vX` back in version order. Worse, a fix of an older major version (a `1.x.y` after `2.0.0`), or "Re-run all jobs" of an older failed run after a newer release (a re-run keeps the old input and `GITHUB_SHA`), put the content of the newer major version under the older major tag, so `@v1` could get the `action.yml` of `v2`. The review reproduced it locally: after `3.0.0` then `2.0.1`, `git show v2:action.yml` held the change of `v3`. Right after the check of the existing tag, the step now lists the tags of the repository with `ls-remote --tags --refs`, keeps the `vX.Y.Z` names, and refuses a version that does not sort above the newest one by `sort -V`. It uses `ls-remote` rather than `git tag --list`, so the check does not depend on the tags the clone fetched. **Deviation:** section 3 lists only the refusal of an existing tag. This workflow does not release a fix of an older major version.

**Review fix (`e6305fa9`): the README must name the repository.** The workflow copies `README.md` unchanged, and it becomes the page of the action on the Marketplace. That README calls the repository undecided and pins the monorepo in every example. A new step **"Check the README"** runs after the checkout, since "Check the run" runs before it. It fails unless the README contains `<repository>@vX` for the major version of the release (`grep -qwF`), so `2.0.0` also needs the README switched to `@v2` first. The match is case-sensitive: the grep 3.0 of Git Bash, which the local runs used, aborts on `-i` with `-F`, so the README uses the exact case of the variable. **Deviation:** section 3 has no such step. The README of this branch fails it by design until step 4 of the checklist (section 4).

**Checks.** All ran locally on Windows in Git Bash, in scratch folders outside the repository.

- **Phase 2**, in `C:\Users\kobra\AppData\Local\Temp\followup-424-104-release`, before the review fixes. Every run script of the workflow ran as written, extracted with the `yaml` package, with the environment built from the job and step `env:`. The target was a scratch bare repository served by `git http-backend` behind Basic auth that accepts only `x-access-token:<token>`, and `gh` was a stub that records its arguments.
  - A missing variable and secret gave both messages; one of them gave its own message. A malformed variable, a ref other than `main`, `1.0`, `v1.0.0`, and a variable naming the monorepo were each refused in "Check the run".
  - A wrong token failed the clone with `Authentication failed` and changed nothing.
  - `1.0.0` into an empty repository created `main`, `v1.0.0` and `v1`. `gh` recorded `release create v1.0.0 --repo acme/storyteller-action --draft --verify-tag --title v1.0.0 --notes …` with `GH_TOKEN` set.
  - `1.0.1` added a commit and `v1.0.1`, moved `v1` to it, and left `v1.0.0` alone. A second run of `1.0.1` was refused (`has the tag v1.0.1 already`), with the refs unchanged.
  - A non-empty repository with the default branch `trunk` and other files (`docs/`, `.github/`, `.hidden`, `README`) ended with only `LICENSE`, `README.md` and `action.yml` on `trunk`, and `v2.0.0` and `v2`.
  - The pushed files are byte-equal to the source. The token is in no log, step summary, record of `gh`, request log of the server, `.git` of the clone or file of the bare repository. The credential helper answered for real: a 401, then authenticated requests.
- **The review fixes**, in `C:\Users\kobra\AppData\Local\Temp\followup-424-104-fix-release`, with the scripts extracted from the YAML, a stub `gh` and a bare repository. `1.0.0`, `1.1.0`, `3.1.0`, `3.1.1`, `3.10.0`, `4.0.0`, `4.0.1` and `4.5.1` were released; `1.0.5`, `2.0.2`, `3.9.0` and `4.2.0` were refused, and an existing tag was refused with its own message. The tags `latest`, `v10` and `v9.0.0-rc.1` were ignored. `v1` stayed at `v1.1.0` and `v3` at `v3.10.0`. "Check the README" refused the README of the branch and one with `@v10` for `1.0.0`, and accepted a README with its `uses:` lines switched.
- **YAML.** `action.yml`, the new workflow, `ui-admin.yml` and the docs site workflow parse without errors or warnings with unique keys. `actionlint` is not installed and did not run.
- **Other workflows.** `ui-admin.yml` and the docs site workflow have no diff, and the path filters of `ui-admin.yml` cover no changed file. The docs site workflow runs on the changes of `docs/42for.net/**`, as for any change of the docs.

### 4. Docs of the release

The README gains a section "Releasing" for maintainers, which names the workflow and the spec.

- **Once, before the first release:** the owner creates the repository, public and empty, accepts the Marketplace Developer Agreement, and has two-factor authentication. The repository variable `STORYTELLER_ACTION_REPOSITORY` of the monorepo names it as `<owner>/<name>`. The repository secret `STORYTELLER_ACTION_TOKEN` holds a fine-grained token of that owner, for that repository only, with Contents: Read and write; it expires, so it is renewed before a release.
- **Each release** has four steps. The maintainer runs the workflow from `main` with `version`, and the step says what the workflow checks. The next two say what it clones, replaces and commits, and how it tags, refuses and pushes with the credential helper. The last one is the draft release, which the owner publishes from the release form with **Publish this Action to the GitHub Marketplace**, choosing the categories at the first release.
- **After the steps:** `v1` moves with every `1.x.y`, while `@v1.2.3` and a commit stay where they are. A change that breaks the inputs or the behaviour of the action is a new major version. Every release replaces the whole content of the repository of the action. When the push succeeded and the draft release failed, a run with the same version is refused, and the release of the tag is created from the release form.

`docs/42for.net/cli/github-action.md` keeps `akobr/mono.me/src/Platform/GitHubAction@<commit>`, as section 4 says, and does not mention the repository of its own yet.

**Review fix (`e6305fa9`): the switch of the README.** The checklist had no step that makes the README ready to be the page on the Marketplace. "Once, before the first release" gains step 4: on `main`, the README and `docs/42for.net/cli/github-action.md` switch their `uses:` lines to `<owner>/<name>@v1`, drop the sentences that call the repository undecided or the action unreleased, and keep the monorepo path only for changes that are not released. Item 1 of each release and the paragraph on major versions say that the workflow checks it, and that `2.0.0` needs the README switched to `@v2` first.

**Review fix (`d01c602e`).** Item 3 of each release says that the workflow refuses a version that does not sort above the newest release, and that it does not release a fix of an older major version.

### 6. Left to the product owner

As section 6 says, and unchanged by this work:

- The owner and the name of the repository of the action (`42for-net` does not exist on GitHub), and who creates it.
- The variable, the secret, the first run of the workflow after the merge to `main`, and the Marketplace step of each release.

The review fixes add one step before the first run: step 4 of the checklist, the switch of the README and of `docs/42for.net/cli/github-action.md` to `<owner>/<name>@v1`, in the exact case of the variable. The workflow refuses to release until it is done. The owner may also revert the shorter description of `action.yml` (section 2). Section 7 stays out of scope: `sform` is not on NuGet, and the policy and its default are unchanged. The branch is not pushed.
