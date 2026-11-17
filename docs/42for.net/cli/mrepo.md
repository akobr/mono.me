# mrepo

Here is a short taste of what the mrepo CLI interface can do.

<YouTubeVideo id="HtQ6UTA5d9w" title="mrepo: introduction" />

::: tip
For a more detailed description, please visit [the technical documentation of the tool](https://github.com/akobr/mono.me/blob/main/docs/Monorepo/mrepo-cli.md). 
:::

## Create new mono-repository

A new mono-repository can be created just by calling a single command in any empty target directory:

``` powershell
mrepo new
```

<YouTubeVideo id="og5a7Zs6IHU" title="mrepo: create a new mono-repository" />

## Create new content inside

Creating the new content is simple, and it will help set up the correct structure.

<YouTubeVideo id="Z2LTVxDoZVc" title="mrepo: create new content inside" />

## Get information

You can use the tool to learn which Nuget packages are used and which versions, where the project is referenced, and even outside the repository as a Nuget package. Get the exact dependency tree, usages, get a version, and much more.

## Build any subtree

By calling a single command from any place in the repository. The MsBuild will be triggered, and the corresponding subtree will be built. Or set custom scripts for your custom content.

``` powershell
mrepo build [--clean|restore|build|test|pack|run]
```

<YouTubeVideo id="dYmqYdOqNs8" title="mrepo: build any subtree" />

## Prepare release + notes

A possibility to fully automate releases and release notes, just with the help of git history and clean commits.

``` powershell
# initiate the step-by-step release process
mrepo release
# when finished, you can generate a release tag
mrepo release tag
```

<YouTubeVideo id="gJ6Vk2Hny5w" title="mrepo: prepare a release and its notes" />
