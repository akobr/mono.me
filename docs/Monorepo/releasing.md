# Release process

## 1. Development

### 1a. Version update

If you want to work on a beta version like `0.9-beta`, set it by the CLI tool:

```bash
mrepo update version
```

### 1b. Coding and testing

The development happens in feature branches, where you should probably create some feature environments to simplify development and testing. Another option is to develop everything behind feature flagging and test/develop on the main, this is well known as [trunk development](https://trunkbaseddevelopment.com/).

Everything is merged down to the main branch when one or multiple features are ready and should be released with the next version. To keep clear and readable git history the merging should be done with the `rebase and fast forward` approach.

## 2. Crete new release

### 2a. Prepare release

The release is prepared with notes and version updates if needed. Trigger it by single CLI command:

```bash
mrepo release
```

The command scans git history and interacts with you about the final version and how the release will be done. The simplest way is to generate a new release branch containing a release commit with notes and the new version.

The commit looks like `release: release of workstead/project/v.1.0`. These commits are essential, and the same as versioning, the releasing is based on git history, which is powerful because everything can be determined just by the history.

### 2b. Merge release commit

The next step is to put the release commit into the main. A pull request should probably do this, and don't forget to use the `rebase and fast forward` strategy. When the commit is on the HEAD of main, the last step is to archive the release by tagging and CLI command:

```bash
mrepo release tag
```

The command creates a git tag based on the last release commit.
