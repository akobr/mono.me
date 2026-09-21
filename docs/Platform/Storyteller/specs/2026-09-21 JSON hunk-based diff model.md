# JSON Hunk-Based Diff Model

**Date:** 2026-09-21
**Status:** Proposed
**Area:** Platform / Storyteller / Configuration API

---

## Problem

The configuration diff endpoints return `IReadOnlyCollection<string>` — flat arrays of text lines prefixed with `+ `, `- `, or `  `. This format:

- Is difficult for clients to parse programmatically (regex on prefixes)
- Lacks summary statistics (clients must count lines themselves)
- Cannot convey word-level change detail for inline highlighting
- Does not group changes into logical hunks with context

## Solution

Replace the flat text response with a structured JSON hunk-based model, similar to GitHub and GitLab diff APIs. DiffPlex (already in use) produces the necessary data — we serialize a thin DTO layer over its output.

---

## API Response Model

### `DiffResult`

```json
{
  "stats": {
    "additions": 2,
    "deletions": 1,
    "unchanged": 4
  },
  "hunks": [
    {
      "oldStart": 1,
      "oldCount": 5,
      "newStart": 1,
      "newCount": 6,
      "lines": [
        {
          "type": 0,
          "content": "{",
          "oldLineNumber": 1,
          "newLineNumber": 1
        },
        {
          "type": 2,
          "content": "  \"key\": \"old-value\"",
          "oldLineNumber": 2,
          "segments": [
            { "text": "  \"key\": \"", "isChange": false },
            { "text": "old-value", "isChange": true },
            { "text": "\"", "isChange": false }
          ]
        },
        {
          "type": 1,
          "content": "  \"key\": \"new-value\"",
          "newLineNumber": 2,
          "segments": [
            { "text": "  \"key\": \"", "isChange": false },
            { "text": "new-value", "isChange": true },
            { "text": "\"", "isChange": false }
          ]
        },
        {
          "type": 0,
          "content": "}",
          "oldLineNumber": 5,
          "newLineNumber": 6
        }
      ]
    }
  ]
}
```

### Type Definitions

| Type | Fields | Description |
|------|--------|-------------|
| `DiffResult` | `stats: DiffStats`, `hunks: DiffHunk[]` | Top-level response |
| `DiffStats` | `additions: int`, `deletions: int`, `unchanged: int` | Summary counts |
| `DiffHunk` | `oldStart: int`, `oldCount: int`, `newStart: int`, `newCount: int`, `lines: DiffLine[]` | Contiguous change group with context (maps to `@@ -old,count +new,count @@`) |
| `DiffLine` | `type: DiffChangeType`, `content: string`, `oldLineNumber: int?`, `newLineNumber: int?`, `segments: DiffSegment[]?` | Single line in a hunk |
| `DiffSegment` | `text: string`, `isChange: bool` | Word/character-level sub-change for inline highlighting |
| `DiffChangeType` | `Unchanged = 0`, `Addition = 1`, `Deletion = 2` | Line change classification |

### Field Details

- **`DiffLine.oldLineNumber`** — Line number in the "from" document. `null` for additions.
- **`DiffLine.newLineNumber`** — Line number in the "to" document. `null` for deletions.
- **`DiffLine.segments`** — Only populated for modified lines (a deletion immediately followed by an insertion). Contains character-level diff segments where `isChange: true` marks the portion that actually changed. `null` for purely added, deleted, or unchanged lines.
- **Hunk context** — 3 unchanged lines before/after each change group. Hunks separated by 6 or fewer unchanged lines are merged.

---

## Unified Diff Format (Alternative Representation)

Clients that prefer a raw patch format can request it via query parameter:

```
GET .../versions/2/diff?format=unified
```

Returns `text/plain` unified diff:

```diff
@@ -1,5 +1,6 @@
 {
-  "key": "old-value"
+  "key": "new-value"
   "other": "unchanged"
 }
```

| `format` value | Response type | Description |
|----------------|--------------|-------------|
| _(omitted)_ or `json` | `application/json` — `DiffResult` | Structured hunk model (default) |
| `unified` | `text/plain` — string | Raw unified diff text |

---

## Affected Endpoints

All three configuration diff endpoints return `DiffResult` (or unified text when `format=unified`):

| Endpoint | Route |
|----------|-------|
| Version diff (vs previous) | `GET v1/{org}/{project}/{view}/configuration/{key}/versions/{version}/diff` |
| Custom version range | `GET v1/{org}/{project}/{view}/configuration/{key}/versions/{version}/diff/{versionFrom}` |
| Cross-view diff | `GET v1/{org}/{project}/{view}/configuration/{key}/diff/{viewTo}` |

---

## Implementation Plan

### Phase A — Backend

#### Step 1: New DTOs

**New file:** `src/Platform/Storyteller/Abstractions.Annotations/src/Model/DiffResult.cs`

C# `record class` types following the existing pattern (see `Configuration.cs`):

```csharp
public enum DiffChangeType { Unchanged = 0, Addition = 1, Deletion = 2 }

public record class DiffSegment
{
    public required string Text { get; init; }
    public required bool IsChange { get; init; }
}

public record class DiffLine
{
    public required DiffChangeType Type { get; init; }
    public required string Content { get; init; }
    public int? OldLineNumber { get; init; }
    public int? NewLineNumber { get; init; }
    public IReadOnlyList<DiffSegment>? Segments { get; init; }
}

public record class DiffHunk
{
    public required int OldStart { get; init; }
    public required int OldCount { get; init; }
    public required int NewStart { get; init; }
    public required int NewCount { get; init; }
    public required IReadOnlyList<DiffLine> Lines { get; init; }
}

public record class DiffStats
{
    public required int Additions { get; init; }
    public required int Deletions { get; init; }
    public required int Unchanged { get; init; }
}

public record class DiffResult
{
    public required DiffStats Stats { get; init; }
    public required IReadOnlyList<DiffHunk> Hunks { get; init; }
}
```

**New file:** `src/Platform/Storyteller/Abstractions.Annotations/src/Model/DiffFormatter.cs`

Static utility `DiffFormatter.ToUnifiedDiff(DiffResult)` — renders standard unified diff text from the structured model.

#### Step 2: Update Interface

**File:** `src/Platform/Storyteller/Backend.Core/src/Configuring/IConfigurationService.cs`

Change return types from `Task<IReadOnlyCollection<string>>` to `Task<DiffResult>` for:
- `GetConfigurationVersionChangesAsync(FullKey key, uint version)`
- `GetConfigurationVersionChangesAsync(FullKey key, uint fromVersion, uint toVersion)`
- `GetConfigurationViewChangesAsync(FullKey sourceKey, string toView)`

#### Step 3: Update Implementation

**File:** `src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`

Rewrite `GetConfigurationChangesAsync` (currently lines 241-285):

1. Keep existing JSON serialization + `InlineDiffBuilder.Diff()` call
2. Build `DiffLine` list tracking old/new line numbers
3. Detect modified pairs (deletion followed by insertion) — compute word-level `DiffSegment`s via `DiffPlex.Differ.CreateCharacterDiffs()`
4. Group into `DiffHunk`s with 3-line context, merging nearby hunks
5. Compute `DiffStats`

#### Step 4: Update API Endpoints

**File:** `src/Platform/Storyteller/Api.Functions/src/V1/ConfigurationHttp.cs`

For all three diff endpoints:
- Change `[OpenApiResponseWithBody]` type to `typeof(DiffResult)`
- Add optional `format` query parameter
- Return unified text when `format=unified`

**File:** `src/Platform/Storyteller/Api.Functions/src/Definitions.cs`
- Add `Format` parameter constant

#### Step 5: Update Tests

**File:** `src/Platform/Storyteller/Backend.CosmosDb/test/CosmosConfigurationServiceTests.cs`

- Migrate string-prefix counting assertions to `DiffResult` property assertions
- Add test for word-level segments on modified lines
- Add unit test for `DiffFormatter.ToUnifiedDiff()`

### Phase B — CLI Client

#### Step 6: Update CLI Diff Command (after SDK regeneration)

**File:** `src/Platform/Cli/src/Commands/Configuration/ConfigDiffCommand.cs`

- Consume structured `DiffResult` instead of `ICollection<string>`
- Stats header: `+N -N ~N`
- Hunk headers: `@@ -old,count +new,count @@`
- Dual line numbers (old/new) per line
- Word-level highlighting via Spectre.Console (`bold` + `on green`/`on red` for changed segments)

---

## Design Decisions

1. **`InlineDiffBuilder` over `SideBySideDiffBuilder`** — The input is sequential JSON text. Inline diff gives `ChangeType` per line which maps directly to `DiffChangeType`. Word-level detail is added separately via `Differ.CreateCharacterDiffs()`.

2. **DTOs in `Abstractions.Annotations`** — Pure data models with no infrastructure dependencies. Already referenced by Backend.Core, Backend.CosmosDb, Api.Functions, and Cli.

3. **`DiffChangeType` as enum (int serialization)** — Compact JSON. Clients switch on numeric values. A `JsonStringEnumConverter` can be added later if string names are preferred.

4. **`segments` nullable, not empty list** — Avoids JSON bloat for the common case (unchanged lines, pure additions/deletions). Only populated for paired modification lines.

5. **3-line context, merge gap <= 6** — Matches git's default behavior. Keeps hunks compact while providing enough context.

6. **DiffPlex stays in Backend.CosmosDb** — The diff algorithm is an infrastructure concern. No need to reference DiffPlex from Abstractions or Backend.Core.

---

## Verification

1. `dotnet build src/Platform/Storyteller` — all projects compile
2. `dotnet test src/Platform/Storyteller/Backend.CosmosDb/test` — updated tests pass
3. Run API locally, `GET .../versions/2/diff` — verify JSON hunk structure
4. `GET .../versions/2/diff?format=unified` — verify plain text unified diff
5. After SDK regen + CLI update: `sform config diff <key>` — verify colored hunk output with word-level highlights
