# Vendored packages

NuGet packages that no public feed has, kept in the repository so a clean machine, CI and a container restore them.
`nuget.config` lists this folder as the source `Vendored packages`; `.gitignore` keeps its `*.nupkg` files.

| Package | Version | SHA-256 of the file |
|---|---|---|
| `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` | `2.0.0-preview-prop-naming` | `ea283bccda17cc99e96e4f898383256ad3b98f53700f7e655408d1f9aca04ca3` |
| `Microsoft.Azure.WebJobs.Extensions.OpenApi.Core` | `2.0.0-preview-prop-naming` | `689c4671dc0ea9faae6f5cebc89f3f1fb6d6f381de3d20a5e720572e6eca08cb` |

Both are a custom build (MIT, like the upstream project) of the naming-strategy change of the open pull request
[Azure/azure-functions-openapi-extension#646](https://github.com/Azure/azure-functions-openapi-extension/pull/646), at
commit `f49d978243f9e2312ce4fc55eb6ab472dae92bb7` (the `repository` of their nuspec). The Storyteller API needs it for
`OpenApiConfigurationOptions.NamingStrategy` (`src/Platform/Storyteller/Api.Functions/src/OpenApi/OpenApiConfigurationOptions.cs`);
the public `2.0.0-preview1` and `2.0.0-preview2`, which NuGet sorts above this version, do not have it. The files are the
ones the local builds restored until now (the global packages folder, whose content hashes they match). The worker
package's nuspec depends on the public `OpenApi.Core` `1.0.0`, so the Storyteller API, the Scheduler and the Supervisor
each reference `OpenApi.Core` themselves, and `Directory.Packages.props` pins both packages to the custom build.

Remove a package, its row and, with the last one, the source in `nuget.config` once a public build has the change and
none of the three projects needs the custom version any more.
