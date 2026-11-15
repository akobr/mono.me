# Storyteller conformance suite

Language-neutral JSON vectors that every Storyteller configuration provider and SDK runs, so that they flatten, format, fail and refresh the same way, and validate project settings by the same catalog and rules. `suite.json` holds the version and lists every file with its kind; `schema/` has a JSON Schema per kind.

| File | Kind | Pins |
| --- | --- | --- |
| `flattening/flat.json`, `flattening/properties.json`, `flattening/env.json` | `flattening` | The keys of a document per target, or its error |
| `formats.json` | `formats` | The exact text of the formats `flat`, `properties`, `env` and `yaml` |
| `errors.json` | `errors` | The status, error code and transient flag of a failed read |
| `refresh.json` | `refresh` | Keys, notifications, sent tags and failures across refresh runs |
| `project-settings.json` | `project-settings` | The catalog of project settings (type codes, built-in appearances, colors, icons, date formats, defaults, limits, time zones) and the normalized settings or the problems of a settings document |

`project-settings.json` is also the catalog of the platform itself: `Abstractions.Annotations` embeds it (`ProjectSettingsCatalog`, with the rules in `ProjectSettingsRules` and `ProjectSettingsJson`). Its `timeZones` are the ids of the tz database (now release 2026f, from NodaTime 3.3.7), printed by `tools/time-zones.cs`, a file-based app: run `dotnet run src/Platform/Storyteller/conformance/tools/time-zones.cs` and paste its lines into the catalog after a NodaTime update that brings a new tz database release.

Run it:

```bash
dotnet test --project src/Libraries/Platform/Storyteller/ConfigurationFormats/test
dotnet test --project src/Libraries/Platform/Storyteller/Configuration/test --filter "FullyQualifiedName~Conformance"
dotnet test --project src/Platform/Storyteller/Backend.Core/test --filter-class "*ProjectSettingsConformanceTests"

cd src/Platform/Storyteller/sdk.typescript/src
npx vitest run test/conformance
```

The shapes, the versioning, the portability rules, the runners and the release gate are described in [docs/Platform/Storyteller/conformance.md](../../../../docs/Platform/Storyteller/conformance.md).
