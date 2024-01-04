# mrepo

Here is a short taste of what the mrepo CLI interface can do.

**TODO: add a video**

::: tip
For a more detailed description, please visit [the technical documentation of the tool](https://github.com/akobr/mono.me/blob/main/docs/Monorepo/mrepo-cli.md). 
:::

## Get information

**TODO: add a video**

You can use the tool to learn which Nuget packages are used and which versions, where the project is referenced, and even outside the repository as a Nuget package. Get the exact dependency tree, get a version, and much more.

## Build any subtree

By calling a single command from any place in the repository. The MsBuild will be triggered, and the corresponding subtree will be built.

``` powershell

mrepo build [--clean|restore|build|test|pack|run]

```

## Prepare release + notes

**TODO: add a video**

A possibility to fully automate releases and release notes, just with the help of git history and clean commits.

## Create new content

**TODO: add a video**

Creating the new content is simple, and it will help set up the correct structure.