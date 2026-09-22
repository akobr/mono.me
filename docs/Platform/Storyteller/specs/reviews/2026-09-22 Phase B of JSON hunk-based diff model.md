# Phase B Review: JSON Hunk-Based Diff Model — CLI Client

## Overview

CLI client update to consume the new structured `DiffResult` API response with hunk-based rendering and word-level highlighting. Spec: `docs/Platform/Storyteller/specs/2026-09-21 JSON hunk-based diff model.md`.

## What Was Done

### 1. Updated ConfigDiffCommand

**File:** `src/Platform/Cli/src/Commands/Configuration/ConfigDiffCommand.cs`

- Changed `ICollection<string> diffLines` to `DiffResult diff` — all three API call paths now use the SDK's `DiffResult` return type
- Added `null` as the `format` parameter to all SDK calls (CLI always uses JSON format)
- Replaced flat line iteration with structured hunk-based rendering:
  - **Stats header:** `+N -N ~N` in green/red/dim colors
  - **Hunk headers:** `@@ -old,count +new,count @@` in cyan
  - **Dual line numbers:** old and new line numbers displayed side-by-side in grey
  - **Unchanged lines:** dim with `  ` prefix
  - **Added/deleted lines:** green/red with `+`/`-` prefix
- Added `RenderLineWithSegments()` static method for word-level highlighting:
  - Changed segments (`IsChange = true`): rendered as **bold underline** in the base color
  - Unchanged segments: rendered in the base color without emphasis
  - Falls back to whole-line coloring when no segments are present
- Removed unused `System.Collections.Generic` using; added `System` for `Math.Max`
- Trimmed the XML doc comment (removed redundant `<remarks>` and `<returns>` that restated the obvious)

### Build Verification

`dotnet build src/Platform/Cli/src` — 0 errors.
