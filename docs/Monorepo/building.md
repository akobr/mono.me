# Build system

The build system is a combination of a couple of concepts: 

- From .net point of view the most important is **msbuild** system which is used as main actor and the runtime of building any .net content. The toolkit calls **dotnet CLI** to perform the builds.
- Any other technology can be connected to the toolkit by **custom scripts**.
  - Scripts can defined as in-line scripts directly in [mrepo.json](mrepo-json.md) configuration file or complex script files in `.mrepo/scripts`.
  - Scripting engine is using **PowerShell** as the runner.
- The dependencies between .net projects are automatically handled by msbuild.
- The dependencies between different platforms and uncompatible projects can be explicitly defined in [mrepo.json](mrepo-json.md) configuration file as `item/dependencies`. *[this feature is under development]*

