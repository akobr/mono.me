# Phase 4 of Restore a Previous Version

## Overview

Phase 4 of [Restore a Previous Version of a Configuration, Template or Schema](../2026-11-10%20Restore%20a%20Previous%20Version%20of%20a%20Configuration,%20Template%20or%20Schema.md) (Linear 424-221): the MCP server's revert tools on the new restore routes. The spec's section 8, a nice-to-have of the issue.

## What Was Done

### 8. MCP server

- **`revert_configuration`** (`Tools/ConfigurationTools.cs`) calls `RestoreConfigurationVersionAsync` instead of its read and JSON Patch with `test` operations: the restore is atomic, audited as `ConfigurationRestored`, and the server copies the version as stored, so the tool no longer needs the directive guard for it (`DocumentWrites.EnsureNoDirectives` stays for the replace tools). It keeps the annotation guard that the other configuration writes keep, and `force` (sent only when true). It returns the restore result (`Outcome`, `FromVersion`, `Version`, `Modified`) and `Changed` (`Outcome` is `Restored`), built by the new `DocumentWrites.Reverted`; the stored content is no longer in the result, while `Version` and `Changed` keep their meaning.
- **New tools:** `revert_template` (`Tools/TemplateTools.cs`: organization, project, annotationType, version, view) and `revert_schema` (`Tools/SchemaTools.cs`: the `level`, `annotationType` and `key` of the other schema tools through `SchemaTarget` and its level switch, version, view, `force`). Both are `ReadOnly=false`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false`. The names keep "revert", as `restore_annotations` undeletes.
- **Descriptions:** `get_annotation_history` names `ConfigurationRestored` with `FromVersion`; the `list_*_versions` tools mention `RestoredFrom`; `delete_template` and `delete_schema` say that the revert tool brings a version back. `set_schema` and `revert_schema` share the text of `force`.
- **Tests** (751 pass): `ConfigurationToolsTests` (the restore call, `force`, `Changed` for both outcomes), `TemplateToolsTests` and `SchemaToolsTests` (each level, an invalid target that sends nothing), `ManagementToolsHostTests` (the tool list, the `POST …/versions/{n}/restore` URLs with `force`, and a `409` schema violation answered as a tool error with its errors); `CatalogTests` pass unchanged.
- **Docs:** `mcp.md` gives `replace_configuration` and `revert_configuration` their own rows, names `revert_template` and `revert_schema`, and explains the revert tools (the route, `RestoredFrom`, `ConfigurationRestored`, the no-op, the result, `404 VersionNotFound`, `force`).
