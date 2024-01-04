# We ❤ mono-repository

This project is trying to tackle problems around [a mono-repository](https://en.wikipedia.org/wiki/Monorepo) and .NET technical stack, but not exlusively. The other goal is to put together useful tooling for a monorepo which is already out there and explain best practices which I recomend to use.

> For more info go to [the documentation](https://github.com/akobr/mono.me/blob/main/docs/Monorepo/README.md) on GitHub.

## Install

```powershell
dotnet tool install 42.Monorepo.Cli --global --prerelease
```

## Available commands

To see all available commands:

```powershell
mrepo
```

The full list of available commands:

- exploring
  - `info`: display information of current location
  - `list`: show a list of items in current location or for entire mono-repo
- creating
  - `new workstead`: create a new workstead
  - `new project`: create a new project
  - `new repository`: initiate a new mono repository
- building
  - `build`: build, clear, restore, test, pack or run current location
- versioning
  - `show versions`: show details about all versions of a current location
  - `new version`: create a new version definition file
  - `update version`: update a version specified in version.json
- releasing
  - `release`: prepare a release for current location
  - `release tag`: prepare a release tag from the last release commit
- dependency management
  - `new package`: add new package to current location
  - `update package`: change version of a package in current location
  - `fix packages`: fix all hardcoded versions in current project
  - `show packages`: show all available packages in current location
  - `show usages`: show usages of current project
  - `show dependency-tree`: show full dependency tree of current location
- executing
  - `run`: run any pre-configured command/script
- learning
  - `explain`: display explanation of an item inside a mono-repo
  - `--help`: display list of all available commands
  - `--version`: show current version of the tool

