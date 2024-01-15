# mrepo

Here is a short taste of what the mrepo CLI interface can do.

<iframe style="width: 100%; max-width: 560px; height: 315px; margin: auto;" src="https://www.youtube.com/embed/HtQ6UTA5d9w?si=0MOG6cKDUOvjI8zH" 
title="YouTube video player" frameborder="0" 
allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>

::: tip
For a more detailed description, please visit [the technical documentation of the tool](https://github.com/akobr/mono.me/blob/main/docs/Monorepo/mrepo-cli.md). 
:::

## Create new mono-repository

A new mono-repository can be created just by calling a single command in any empty target directory:

``` powershell
mrepo new
```

<iframe style="width: 100%; max-width: 560px; height: 315px; margin: auto;" src="https://www.youtube.com/embed/og5a7Zs6IHU?si=ykA9OThQmqRJ5wmM" 
title="YouTube video player" frameborder="0" 
allow="accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>

## Create new content inside

Creating the new content is simple, and it will help set up the correct structure.

<iframe style="width: 100%; max-width: 560px; height: 315px; margin: auto;" src="https://www.youtube.com/embed/Z2LTVxDoZVc?si=DUqKPjyCbLXbt4N-" 
title="YouTube video player" frameborder="0" 
allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>

## Get information

You can use the tool to learn which Nuget packages are used and which versions, where the project is referenced, and even outside the repository as a Nuget package. Get the exact dependency tree, usages, get a version, and much more.

## Build any subtree

By calling a single command from any place in the repository. The MsBuild will be triggered, and the corresponding subtree will be built. Or set custom scripts for your custom content.

``` powershell
mrepo build [--clean|restore|build|test|pack|run]
```

<iframe style="width: 100%; max-width: 560px; height: 315px; margin: auto;" src="https://www.youtube.com/embed/dYmqYdOqNs8?si=TfuHJYJ8azafL8a-" 
title="YouTube video player" frameborder="0" 
allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>

## Prepare release + notes

A possibility to fully automate releases and release notes, just with the help of git history and clean commits.

``` powershell
# initiate the step-by-step release process
mrepo release
# when finished, you can generate a release tag
mrepo release tag
```

<iframe style="width: 100%; max-width: 560px; height: 315px; margin: auto;" src="https://www.youtube.com/embed/gJ6Vk2Hny5w?si=4TcWWWCX0y0H42MC" 
title="YouTube video player" frameborder="0" 
allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>
