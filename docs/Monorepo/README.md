# We ❤ mono-repository

> Monorepo ≠ monolith❗ It is possible to manage a large codebase or multiple projects in a centralized, standardized, and sustainable way.

This project tackles problems around [a mono-repository](https://en.wikipedia.org/wiki/Monorepo) and .NET technical stack, but not exclusively. The other goal is to put together ideas for mono repository tooling that are already out there and explain the best practices.

## Main benefits

- centralized dependency management
- intelligent versioning based only on git history
- flexible build system and custom scripting
- automated releasing and generating release notes
- transparent view and navigation through the codebase

## Before started

- [Why mono-repo?](why-monorepo.md)
- [Why centralized dependencies?](why-centralized-dependencies.md)

## Get started

- [Structure](structure.md)
- [CLI interface](mrepo-cli.md)
- [Configuration](mrepo-json.md)
- [Versioning](versioning.md)
- [Building](building.md)
- [Releasing](releasing.md)
- [Good habits](good-habits.md)
- [Feature list](features.md)
- [Road map](road-map.md)

## mrepo toolset

The heart of the toolset is [a CLI application](mrepo-cli.md) served as dotnet tool under the command `mrepo`. This powerful command helps you to manage, create, and release everything inside a mono-repository.

## Configuration

Basic [configuration of a mono-repository](mrepo-json.md) is stored in the `mrepo.json` file in the repository's root. Detailed pieces and custom scripts should be located in folder `.mrepo`.
