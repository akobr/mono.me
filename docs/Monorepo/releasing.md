# Release process

## 1. Development

### 1a. Version update (optional)

A version can be updated anytime. For example, if you want to work on a beta version `0.9-beta`, set it by the CLI tool:

```bash
mrepo update version
```

### 1b. Coding and testing

The development happens in feature branches, where you should create feature environments to simplify development and testing. Another option is to develop everything behind feature flagging and test/develop on the main, known as [trunk development](https://trunkbaseddevelopment.com/).

Ultimately, everything is merged to the main branch when one or multiple features are ready to be released with the next version. To keep clear and readable git history, the merging should be done with the `rebase and fast forward` approach.

## 2. Crete new release

### 2a. Prepare release

The release is prepared with notes and version updates if needed. Trigger it by a single CLI command:

```bash
mrepo release
```

The command scans the git history and interacts with you about the final version and how the release will be done. The simplest way is to generate a new release branch containing a release commit with notes and the new version.

The commit looks like `release: release of workstead/project/v.1.0`. These commits are essential, and the same as versioning, the releasing is based on git history, which is powerful because everything can be determined just by the git history.

> **The git history is the single point of the truth.** Nothing else is needed to reconstruct anything around your codebase. Reconstructed can be any past version, release, or release notes. I'm also preparing to store all the reasons why something has been published/redeployed due to a change in dependency in the shape of a simple change log. Driving everything from git history will help you have a transparent and straightforward view of the entire lifecycle of your projects. The CI/CD pipelines are safe to be triggered just by changes in the history, as well.

### 2b. Merge release commit

The next step is to put the release commit into the main. A pull request should probably do this, and don't forget to use the `rebase and fast forward` strategy.

### 2c. Create a release tag (optional)

When the commit is on the HEAD of main, the last step is to archive the release by tagging and CLI command:

```bash
mrepo release tag
```

The command creates a git tag based on the last release commit. This step is optional, only if you want to add another tracking of the releases, but it is unnecessary because, in the git history, every release is already represented as a custom release commit.

After the creation of the tag, it should be pushed to the origin:

```bash
git push origin workstead/project/v.1.0
```
