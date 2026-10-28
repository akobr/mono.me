# Storyteller conformance suite

Language-neutral JSON vectors that every Storyteller configuration provider and SDK runs, so that they flatten, format, fail and refresh the same way. `suite.json` holds the version and lists every file with its kind; `schema/` has a JSON Schema per kind.

| File | Kind | Pins |
| --- | --- | --- |
| `flattening/flat.json`, `flattening/properties.json`, `flattening/env.json` | `flattening` | The keys of a document per target, or its error |
| `formats.json` | `formats` | The exact text of the formats `flat`, `properties`, `env` and `yaml` |
| `errors.json` | `errors` | The status, error code and transient flag of a failed read |
| `refresh.json` | `refresh` | Keys, notifications, sent tags and failures across refresh runs |

Run it:

```bash
dotnet test src/Platform/Storyteller/ConfigurationFormats/test
dotnet test src/Platform/Storyteller/Configuration/test --filter "FullyQualifiedName~Conformance"

cd src/Platform/Storyteller/sdk.typescript/src
npx vitest run test/conformance
```

The shapes, the versioning, the portability rules, the runners and the release gate are described in [docs/Platform/Storyteller/conformance.md](../../../../docs/Platform/Storyteller/conformance.md).
