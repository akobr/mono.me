# Why centralized dependencies

If no centralized dependency system is in place then the dependencies need to be managed on each project. This is well described for .NET platform at official documentation: [Manage package dependencies in .NET applications](https://docs.microsoft.com/en-us/dotnet/core/tools/dependencies).

## Disadvantages without a centralized system

- difficult to update a version of a dependency; need to be done for each project separately
- keep the same version of a dependency in all projects is complex, and it is easy to forget some
- has a global knowledge of all used dependencies is difficult
- It's easy to smuggle in an inconvenient dependency

## Advantages of centralized system

- simple way to control dependencies and their versions in "one" place
- true ease to update version of a dependency
- keep all projects up to date; force to have same version everywhere

## A couple of possible solutions

- **Central package version management** *[Preview but recommended]*: A baked-in solution into .NET Core SDK (from 3.1.300), using Directory.Packages.props file, but still a preview feature. https://bit.ly/3oKJCpq
- **External MsBuild SDK**: A custom MsBuild SDK built by NuGet team, named Microsoft.Build.CentralPackageVersions. https://bit.ly/3GMloRG
- **Directory.Build.props** *[MsBuild 15+]*: Use of hierarchical Directory.Build.props and the possibility to update version of package reference by MsBuild 15 and newer.
- **Paket package manager**: An alternative package manager for NuGet and .NET projects, which has some great features. Currently not yet supported by my tooling. https://bit.ly/3oHTJLp

## Used system

In my mono-repo is used an out-of-box solution of central package version management, which is part of .NET Core SDK *from version 3.1.300*. Everything is described at documentation about [dependency management](dependency-management.md).
