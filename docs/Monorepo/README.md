# We ❤ mono-repository

> Monorepo ≠ monolith❗ It is a possibility how to manage a large codebase or multiple projects in a centralized, standardized, and sustainable way.

This project is trying to tackle problems around [a mono-repository](https://en.wikipedia.org/wiki/Monorepo) and .NET technical stack, but not exlusively. The other goal is to put together useful tooling for a monorepo which is already out there and explain best practices which I recomend to use.

## Before started

- [Why mono-repo?](why-monorepo.md)
- [Why centralized dependencies?](why-centralized-dependencies.md)

## Get started

- [Structure](structure.md)
- [CLI interface](mrepo-cli.md)
- [Configuration](mrepo-json.md)
- [Versioning](versioning.md)
- [Releasing](releasing.md)
- [Good habits](good-habits.md)
- [Feature list](features.md)
- [Road map](road-map.md)

## mrepo toolset

The heart of the toolset is [a CLI application](mrepo-cli.md) served as dotnet tool under the command `mrepo`. This powerful command helps you to manage, create and release everything inside a mono-repository.

## Configuration

Basic [configuration of a mono-repository](mrepo-json.md) is stored in `mrepo.json` file in the root of the repository. Detailed pieces and custom scripts should be located in folder `.mrepo`.
