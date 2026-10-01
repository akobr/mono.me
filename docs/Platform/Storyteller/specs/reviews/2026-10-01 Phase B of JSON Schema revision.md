# Phase B of JSON Schema revision

## Overview

Phase B of [2026-09-30 JSON Schema revision](../2026-09-30%20JSON%20Schema%20revision.md) wires the regenerated NSwag client into `sform` and adds the schema commands. Configuration `set` and `edit` can pass `-f` or `--force`. The service and HTTP behaviour are unchanged from [Phase A of JSON Schema revision](2026-10-01%20Phase%20A%20of%20JSON%20Schema%20revision.md).

The CLI project builds with 0 errors. The warning count is the same 43 that were already present. The new commands were smoke-tested with `--help`. They were not run against a live API.

## What Was Done

### 9. Clients

The user regenerated `Sdk.NSwag` before this phase. `SetConfigurationAsync` and `PatchConfigurationAsync` now take `force` before the body, and every schema method takes `view`. That is what broke the CLI: three calls still used the old five-argument shape.

- `StorytellerApiExtensions.ReplaceConfigurationAsync` takes `force` and sends it on both the create PUT and the JSON Patch. An empty patch still returns without a request.
- `ConfigSetCommand` passes `Force` on the merge PUT and on `--replace`.
- The generation command in `Sdk.NSwag/README.md` now points at `open.api.v0.8.82.json`, the document that contains the view-scoped schema routes.

`Sdk.Kiota` was not regenerated. Its tree still has no configuration-schema builders, and the generated files sit next to hand-written client code. The CLI does not use Kiota.

### 10. Schema commands

Registered under `sform story` (alias of `storyteller`) through `StorytellerListCommand`, the same way `TemplateGetCommand` is. Organization, project, and view come from `ICommandContext`. Running `schema`, `schema type`, `schema annotation`, or `schema descendant` with no verb prints help.

| Class | Command |
| --- | --- |
| `SchemaCommand` | `schema` |
| `SchemaTypeCommand` and its verbs | `schema type get\|set\|edit\|delete\|versions\|diff {annotationType}` |
| `SchemaAnnotationCommand` and its verbs | `schema annotation get\|set\|edit\|delete\|versions\|diff {annotationKey}` |
| `SchemaDescendantCommand` and its verbs | `schema descendant get\|set\|edit\|delete\|versions\|diff {annotationKey} {annotationType}` |
| `SchemaDefinitionCommand` | `schema definition {annotationKey}` |

- `CommandNames` gained `SCHEMA`, `TYPE`, `ANNOTATION`, `DESCENDANT`, and `DEFINITION`.
- `set` requires `-i|--import`. The file is the PUT body. There is no merge and no inline properties.
- `edit` loads the current schema content into the editor and PUTs the result. A missing schema prints the same "has not been found" line as `get` and does not open the editor.
- `set` and `edit` take `-f|--force` and pass `force=true`. A forced save prints the new version and `Existing configurations were not required to comply.`
- A `409` whose body is `SchemaValidationErrorResponse` prints each annotation key, its view, and the messages, then returns `ERROR_WRONG_INPUT`. Other API failures keep the previous `Error occurred` line.
- `diff` follows `TemplateDiffCommand`: omitted versions compare the latest with the one before it, and `--format` is sent as the `format` query. The generated client still deserializes the body as `DiffResult`, so `unified` is forwarded but this CLI renders the hunk model.
- `get --version` prints one stored version. The spec table does not name the flag. It is the same option template and configuration get already have, so a row from `versions` can be opened.
- A missing schema, version, or diff target prints a not-found line and returns `ERROR_WRONG_INPUT`.
- Type codes are checked with `ValidateAnnotationType`. Annotation keys are checked with `ValidateAnnotationKey`. Both happen before a request.

The three kinds share `SchemaOperations`. Each verb stays its own command class so McMaster can register it.

### 11. Configuration commands

`ConfigSetCommand` and `ConfigEditCommand` gained `-f|--force`.

- `config set` sends it on `SetConfigurationAsync`.
- `config set --replace` and `config edit` send it through `ReplaceConfigurationAsync`, which uses PUT when the configuration is new and PATCH when a JSON Patch is required. Both requests carry the same `force` query.
- A rejected configuration write prints the `SchemaValidationErrorResponse` entries the same way a rejected schema save does.

`docs/Platform/Storyteller/definitions.md` now says the service, the HTTP API, and the `sform` commands implement the contract, and it links this review. The CLI section names `--version` and `--format`.
