# Publishing the GitHub Action and Secrets in CI

Linear: [424-104 Flat output formats for configurations on the API, sform and a GitHub Action (C5)](https://linear.app/424fun/issue/424-104/flat-output-formats-for-configurations-on-the-api-sform-and-a-github)

## Problem

[Flat Output Formats for Configurations](2026-10-27%20Flat%20Output%20Formats%20for%20Configurations.md) (section 14) left three questions to the product owner. Their answers on 2026-10-27:

1. **Publish `sform` to NuGet?** Not yet. The recipe that packs `sform` in the workflow (`cli-source`) is enough for now.
2. **Where is the action published?** In a dedicated repository, listed on GitHub Marketplace, with `@v1` tags.
3. **How do CI jobs receive secrets?** Document certificate machines for CI as the recommendation. Also allow and mention `RequireMtlsForSecretDelivery=false` and federated machine authentication.

Answer 1 needs no change: the docs already present the recipe as the way to get a `sform` with the new options. Answers 2 and 3 do:

- The action lives in `src/Platform/GitHubAction` of the monorepo and is used as `akobr/mono.me/src/Platform/GitHubAction@<ref>`. Nothing copies it into a repository of its own, and its metadata and README are not ready for the Marketplace.
- The docs list the machines that receive secrets side by side, without saying which one to use in CI.

## Current State

- **The action.** `src/Platform/GitHubAction/action.yml` (`name: Storyteller configuration`, no `branding`) and `README.md`. The README links into the monorepo with relative paths (`../../../docs/…`), which break outside it. The monorepo has no `LICENSE` file; packages declare MIT through `Directory.Build.props` (`PackageLicenseExpression`).
- **Marketplace requirements** ([Publishing actions in GitHub Marketplace](https://docs.github.com/en/actions/how-tos/create-and-publish-actions/publish-in-github-marketplace)): a public repository with one `action.yml` at its root and only the files the action needs; a `name` unique on the Marketplace (no action is named "Storyteller configuration" on 2026-10-27); the owner accepts the Marketplace Developer Agreement and has two-factor authentication; a release is published to the Marketplace from the release form ("Publish this Action to the GitHub Marketplace"), not through the API.
- **The owner.** `42for-net`, the owner the study names, does not exist on GitHub. The `gh` session of this machine is `akobr`.
- **CI.** `.github/workflows` holds the docs site and `ui-admin.yml`, both pin actions by major tag. A `workflow_dispatch` workflow runs only from the default branch, where releases come from anyway ("Public releases only from `main`").
- **Secrets in the docs.** `docs/42for.net/cli/github-action.md` ("Secrets need more than a credential"), the action `README.md`, `docs/42for.net/cli/sform.md` ("Export to GitHub Actions") and `docs/Platform/Storyteller/authentication.md` (the GitHub Actions examples and the note after them) say that, under the default policy, only a certificate machine over mTLS receives secrets, and that other machines need the policy off. None of them recommends one.

## Proposed Changes

### 1. Secrets in CI: a recommendation and its alternatives

The pages above present one recommendation and two documented alternatives, with the same wording:

- **Recommended: a certificate machine over mTLS.** It receives secrets under the default policy, so the project keeps `RequireMtlsForSecretDelivery` on and every other caller keeps getting the expressions. The job writes the `.pfx` file from a secret under `$RUNNER_TEMP`. It needs an API behind a front end that forwards the client certificate.
- **Possible: a federated machine (GitHub OIDC).** No stored secret at all (`permissions: id-token: write` and a GUID `client-id`). It receives secret values only when an Administrator sets `RequireMtlsForSecretDelivery` to `false` for the project (`sform story kv policy set --require-mtls false`).
- **Possible: an API key or client credentials**, under the same policy change.
- **The trade-off of the policy** is stated with it: the policy is per project, so turning it off lets every token caller with `Secrets.Read` in that project receive secrets without mTLS, not only the CI machine.
- The examples follow the order: the certificate example first and named as the recommendation, the federated example second, with the policy note.

### 2. A Marketplace-ready action

- `action.yml` gains `branding` (`icon: sliders`, `color: purple`). The `name` stays `Storyteller configuration`.
- `README.md` uses absolute links (`https://42for.net/...` for the public docs, `https://github.com/akobr/mono.me/blob/main/...` for the technical ones), so it reads the same in the monorepo and in the dedicated repository. It names the dedicated repository as the way to use the action once it is released, and keeps the monorepo path for unreleased changes.
- `src/Platform/GitHubAction/LICENSE`: the MIT licence the packages of the monorepo already declare, copied with the action.

### 3. A release workflow

New `.github/workflows/storyteller-action-release.yml`, run by hand (`workflow_dispatch`) with an input `version` (`X.Y.Z`):

- It runs only on `refs/heads/main` and refuses a version that is not `X.Y.Z`.
- The dedicated repository is the repository variable `STORYTELLER_ACTION_REPOSITORY` (`<owner>/<name>`), and it pushes with the secret `STORYTELLER_ACTION_TOKEN`, a fine-grained token with `contents: write` on that repository only. Without either, the job fails with a message that names them. The workflow's own token has `contents: read`.
- It clones the dedicated repository (an empty one included), replaces everything but `.git` with `action.yml`, `README.md` and `LICENSE` from `src/Platform/GitHubAction`, and commits "Release vX.Y.Z" with the source commit of the monorepo in the body. No workflow file reaches the dedicated repository.
- It refuses a tag `vX.Y.Z` that exists, creates it, moves the major tag `vX` to it, and pushes the branch and the tags.
- It creates a **draft** release `vX.Y.Z` with notes that link the source commit. The owner publishes it from the release form with "Publish this Action to the GitHub Marketplace"; the API cannot list an action.
- Actions are pinned by major tag, as in the other workflows; inputs reach scripts only through `env:`.

### 4. Docs of the release

- The action `README.md` gains a "Releasing" section for maintainers: the variable and the secret, the workflow, the Marketplace step, and that `v1` moves with every `1.x.y`.
- `docs/42for.net/cli/github-action.md` keeps `akobr/mono.me/src/Platform/GitHubAction@<ref>` until the first release; the switch to `<owner>/<name>@v1` follows once the owner and name are decided (section 6).

### 5. Phases and commits

| Phase | Content | Commits |
|---|---|---|
| — | This spec | `docs(platform): plan the release of the GitHub Action and the secrets of CI jobs` |
| 1 | Section 1 | `docs(platform): recommend certificate machines for secrets in CI` |
| 2 | Sections 2-4 | `ci(platform): release the GitHub Action into a repository of its own` |
| — | One review | `docs(platform): review the release of the GitHub Action and the secrets of CI jobs` |

`src/Platform/Cli/test` stays green (no code changes); the workflow is checked locally against a scratch bare repository standing in for the dedicated one.

### 6. Left to the product owner

- The owner and the name of the dedicated repository (`42for-net/storyteller-action` needs the `42for-net` organization first), and who creates it.
- The variable, the secret, the first run of the workflow after the merge to `main`, and the Marketplace step of each release.

### 7. Out of scope

- Publishing `sform` to NuGet (answer 1).
- Changing the policy or its default.
