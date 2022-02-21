# Versioning

I recomend to use [NerdBank.GetVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) library for version control based on git history, where every single commit can be built and produce a unique version.

> This package adds precise, semver-compatible git commit information to every assembly, VSIX, NuGet and NPM package, and more. It implicitly supports all cloud build services and CI server software because it simply uses git itself and integrates naturally in MSBuild, gulp and other build scripts.

For needs of a mono-repository, we slightly [bend its functionality](https://github.com/akobr/Nerdbank.GitVersioning).

- the path filters are not interited by default
  - explicit inheritance can be set by `inheritPathFilters` flag
- **hierarchical version**: the version in current directory structure is hierarchical and calculated independently per each sub-directory
  - must be explicitly set by `hierarchicalVersion` flag
  - When this option is enabled the functionality of path filters is ignored and version of each folder is affected only by its own content.

## Install manualy

The versioning can be added into .NET project by the Visual Studio
NuGet package manager GUI, or the NuGet package manager console:

```
Install-Package 42.Monorepo.GitVersioning
```

## Global version settings for entire mono-repository

To define one version for all unversioned projecs or create shared configure, e.g. cloud build settings, add `version.json` file to the root of the mono-repository.

```json
{
  "$schema": "https://raw.githubusercontent.com/AArnott/Nerdbank.GitVersioning/master/src/NerdBank.GitVersioning/version.schema.json",
  "version": "0.8.0-alpha",
  "publicReleaseRefSpec": [
    "^refs/heads/main$" // we release only out of main
  ],
  "cloudBuild": {
    "setVersionVariables": true,
    "buildNumber": {
      "enabled": true,
      "includeCommitId": {
        "when": "nonPublicReleaseOnly",
        "where": "buildMetadata"
      }
    }
  }
}
```

## Specific version for a concreate point

To specify an exact version for any project or an entire workstead just simply add minimal `version.json` file to it.

```json
{
  "version": "1.2",
  "pathFilters": [ "." ]
}
```

The `pathFilters` is quite important because the default behavior of the versioning library is to calculate version height by all commits in entire repository. With the filter `.`, which is relative to the `version.json`, the version will be bump when a commit was made within that subtree. For more information how to specify filters, please refer to the [original documentation](https://github.com/dotnet/Nerdbank.GitVersioning/blob/master/doc/pathFilters.md).

> If you need to inherit some important settings you can add `inherit` property with value `true` and the version file higher in the structure is used as the parent.

### A workstead with single version

In case of a single version for a project group or workstead:

- all projects has the same version
- any change/commit affects the vesion (version of all projects)

This can or cannot be good. In some cases we want one exclusive version for all projects and any change means version update for all of them, there are strictly connected together.

Second posibility is to synchronize version only on main milestones like major and minor verions only. This is described in next section.

## Synchronized version for all projects under a workstead

If a single version for entire workstead is required but you still want to allow small changes between releases, in each project independently, please use `hierarchivalVersion`. This approach will still defines one and centralized version for all sub-projects, but in the same time an independent changes are allowed.

```json
{
  "version": "0.9.1-beta",
  "inherit": true,
  "hierarchicalVersion": true
}
```