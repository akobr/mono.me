# Good habits

## Unit testing and TDD

I highly recommend using the TDD development approach. My CLI tooling always creates a unit-testing part with any newly created project.

> Supported frameworks are [xUnit](https://xunit.net/) or [NUnit](https://nunit.org/), both of them are great, but for simplicity, I recommend using xUnit, which is more lightweight and will force you to use good practices.

## Configuration for toolset

- .editorconfig
- .gitattributes
- .vsconfig
- global.json
- mrepo.json
- nuget.config
- stylecop.json
- version.json

> More information is coming soon.

## Technical documentation

The code should be self-documenting, but some overview technical documentation is always helpful and should be created and managed in the same way as source code (*by git*). Place it into the `docs` folder next to the `src` folder and follow the same structure as the code itself.

> Later, I will explain how to generate some nice-looking static websites from the documentation by [VuePress](https://v2.vuepress.vuejs.org/) or [GitHub Pages](https://pages.github.com/).

List of excellent static side generators worth looking at: [Jekyll](https://jekyllrb.com/), [Hugo](https://gohugo.io/), [VuePress](https://v2.vuepress.vuejs.org/), [Gridsome](https://gridsome.org/docs/), [Eleventry](https://www.11ty.dev/), [Nikola](https://getnikola.com/), and [Statiq Web](https://www.statiq.dev/web).

## Code standards

It's good to use some library to force the same code standards in the entire team or company. In a mono-repository, it is even easier because this needs to be configured just ones, and all new worksteads will automatically adapt to it.

> Recommended configuration is to use [StyleCop](https://github.com/DotNetAnalyzers/StyleCopAnalyzers) for code styling rules, and [Sonar](https://rules.sonarsource.com/csharp) as an excellent tool against code smells and vulnerabilities.

By default, the rules are processed and evaluated on each build. If your mono-repo overgrows, I recommend using pre-commit and running Release build on it and keeping Debug builds minimal.

## Conventional commit messages

For smooth releases and the possibility to generate release notes automatically, you should use the concept of [conventional commits](https://www.conventionalcommits.org/en/v1.0.0/). Even the git history would be cleaner and more readable.

> The CLI tool uses the conventional commits for subsequent version detection and release notes generation.

## Work with the flexibility of MsBuild

MsBuild can be a powerful and flexible tool when you know how to use it. One of the excellent features is the usage of automatically processed directory `props` and `targets` files during the build. Please read the official documentation about them.

- Directory.Build.props
- Directory.Build.targets

If you need to implement any custom steps during the build or somehow affect the build properties, I recommend [using them](https://docs.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022#directorybuildprops-and-directorybuildtargets).

> These files are used in the monorepo to force the versioning, centralized package system and code analyzers.

## Centralized artifacts

It is nice to have one centralized place where all NuGet packages are stored. This folder can be easily used as a source for continuous deployment systems or as a local NuGet feed for a different repository. For these needs, the monorepo has the `.artifacts` folder in the root.

## Separate human and machine view

TBC

## Configuration about details

TBC (mrepo.json)
