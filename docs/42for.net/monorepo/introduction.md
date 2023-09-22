# Introduction to the mono repository <Badge type="warning" text="70% done" />

::: danger
The mrepo tool is under development, see [the road map](road-map).
:::

I'm trying to solve the problem of a mono repository for the .net platform and come up with a suite of handy tools and recommendations on how to manage a large codebase, versioning, releasing, and code standards in a simple way.

::: info
You should ❤ mono-repository. Monorepo ≠ monolith❗ It is possible to manage a large codebase of multiple projects in a centralized, standardized, and sustainable way.
:::

Everything is built around the developer's favorite tool Git, at least versioning and releasing + release notes are fully generated based on Git's history of commits. The served tool is a [CLI interface called mrepo](../cli/mrepo) with an MIT license and the open-source codebase same as all the other tools on this website. Here is a list of the most important features:

- centralized dependency management
- git-based versioning
- git-based releasing with automatically generated release notes
- support for code standards and code analyzers
- recommendations on how to manage and define infrastructure
- a basic support for custom PowerShell scripting

To see examples and the tool in action, go to [section CLI](../cli/mrepo).

::: tip
To know more please [visit my monorepo](https://github.com/akobr/mono.me/tree/main) with all my projects, where you can find all the code and content of these websites. The detailed documentation around the mrepo CLI is located [here](https://github.com/akobr/mono.me/blob/main/docs/Monorepo/README.md).
:::
