# Phase A Review: JSON Hunk-Based Diff Model — Backend

## Overview

Backend implementation of the JSON hunk-based diff model, replacing flat `IReadOnlyCollection<string>` responses with structured `DiffResult` objects. Spec: `docs/Platform/Storyteller/specs/2026-09-21 JSON hunk-based diff model.md`.

## What Was Done

### 1. New DTOs in Abstractions.Annotations

Created one file per type (SA1402 compliance) under `src/Platform/Storyteller/Abstractions.Annotations/src/Model/`:

- `DiffChangeType.cs` — enum: `Unchanged = 0`, `Addition = 1`, `Deletion = 2`
- `DiffSegment.cs` — `record class` with `Text` and `IsChange` for word-level highlighting
- `DiffLine.cs` — `record class` with `Type`, `Content`, nullable `OldLineNumber`/`NewLineNumber`, nullable `Segments`
- `DiffHunk.cs` — `record class` with `OldStart`, `OldCount`, `NewStart`, `NewCount`, `Lines`
- `DiffStats.cs` — `record class` with `Additions`, `Deletions`, `Unchanged`
- `DiffResult.cs` — `record class` with `Stats` and `Hunks`

All types use `required` init properties following the existing `Configuration.cs` pattern.

### 2. DiffFormatter Utility

Created `src/Platform/Storyteller/Abstractions.Annotations/src/Model/DiffFormatter.cs` — static `ToUnifiedDiff(DiffResult)` method that renders standard `@@ -old,count +new,count @@` unified diff output.

### 3. Interface Update

`src/Platform/Storyteller/Backend.Core/src/Configuring/IConfigurationService.cs` — changed three method return types from `Task<IReadOnlyCollection<string>>` to `Task<DiffResult>`.

### 4. Implementation Rewrite

`src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`:

- Added `using DiffPlex;` for the `Differ` class
- Changed three public method signatures to return `Task<DiffResult>`
- Rewrote `GetConfigurationChangesAsync` to:
  - Build `DiffLine` list with tracked old/new line numbers
  - Call `ComputeWordSegments()` to detect deletion/insertion pairs and produce character-level `DiffSegment`s via `Differ.CreateCharacterDiffs()`
  - Call `BuildHunks()` to group lines into hunks with 3-line context (merging hunks with gap <= 6 unchanged lines)
  - Compute `DiffStats` from line counts
- Added three private static helper methods: `ComputeWordSegments`, `BuildSegments`, `BuildHunks`

### 5. API Endpoint Updates

`src/Platform/Storyteller/Api.Functions/src/V1/ConfigurationHttp.cs`:

- All three diff endpoints (`GetConfigurationVersionDiff`, `GetConfigurationVersionDiffCustom`, `GetConfigurationViewDiff`):
  - Changed `[OpenApiResponseWithBody]` type from `typeof(IEnumerable<string>)` to `typeof(DiffResult)`
  - Added `[OpenApiParameter]` for optional `format` query param
  - Replaced `null` check + `OkObjectResult` with `FormatDiffResult()` helper
- Added `FormatDiffResult(DiffResult, HttpRequestData)` private method:
  - Reads `format` from query string
  - Returns `ContentResult` with `text/plain` unified diff when `format=unified`
  - Returns `OkObjectResult(diff)` otherwise (JSON, default)

`src/Platform/Storyteller/Api.Functions/src/Definitions.cs`:

- Added `Format = "format"` in `Parameters`
- Added `DiffFormat` description in `Descriptions`
- Added `PlainText = "text/plain"` in `ContentTypes`

### 6. Test Updates

`src/Platform/Storyteller/Backend.CosmosDb/test/CosmosConfigurationServiceTests.cs`:

- Version diff test: replaced all `line.StartsWith("+ ")` / `line.StartsWith("- ")` / `line.StartsWith("  ")` counting with `Stats.Additions`, `Stats.Deletions`, `Stats.Unchanged` assertions
- View diff test: replaced string-prefix assertions with `DiffChangeType` enum-based assertions on `Hunks.SelectMany(h => h.Lines)`

### Build Verification

All three projects compile with 0 errors:
- `dotnet build src/Platform/Storyteller/Backend.CosmosDb/src`
- `dotnet build src/Platform/Storyteller/Api.Functions/src`
- `dotnet build src/Platform/Storyteller/Backend.CosmosDb/test`
