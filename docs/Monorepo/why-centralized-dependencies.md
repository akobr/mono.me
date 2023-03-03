# Why centralized dependencies

If no centralized dependency system is in place, the dependencies need to be managed on each project. This is well described for .NET platform in the official documentation: [Manage package dependencies in .NET applications](https://docs.microsoft.com/en-us/dotnet/core/tools/dependencies).

## Disadvantages without a centralized system

- challenging to update a version of a dependency; needs to be done for each project separately
- to keep the same version of a dependency in all projects is complex, and it is easy to forget some
  - build in tools of IDEs are having issues to work with large solutions
- has global knowledge of all used dependencies is difficult
- it's easy to smuggle in an inconvenient dependency

## Advantages of centralized system

- a simple way to control dependencies and their versions in "one" place
- true ease in updating a version of a dependency
- keep all projects up to date; force to have the same version everywhere

## A couple of possible solutions

- **Central package version management** *[Preview but recommended]*: A baked-in solution into .NET Core SDK (from 3.1.300), using Directory.Packages.props file, but still a preview feature. https://bit.ly/3oKJCpq
- **External MsBuild SDK**: A custom MsBuild SDK built by NuGet team, named Microsoft.Build.CentralPackageVersions. https://bit.ly/3GMloRG
- **Directory.Build.props** *[MsBuild 15+]*: Use of hierarchical Directory.Build.props and the possibility to update version of package reference by MsBuild 15 and newer.
- **Paket package manager**: An alternative package manager for NuGet and .NET projects, which has some great features. Currently not yet supported by my tooling. https://bit.ly/3oHTJLp

## Used system

My mono-repo uses an out-of-box solution of central package version management, which is part of .NET Core SDK *from version 3.1.300*. Everything is described in the documentation about [dependency management](dependency-management.md).

## Edge cases

If some workstead or project still need a custom dependency versions you can introduce a specific ` Directory.Packages.props` file for it. Use inheritance or explicit reference to connect it into the rest of monorepo.

### Inheritance in package versions

Put a specific `Directory.Packages.props` in the structure and set the inheritance explicitly, because by default MsBuild process will stop on first hit of these props file:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
    <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Packages.props', '$(MSBuildThisFileDirectory)..\'))" />

    <ItemGroup>
        <!-- ...package versions -->
    </ItemGroup>
</Project>
```
