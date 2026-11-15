# Import Files of the Demo Data Set

## Overview

This phase writes the data set of [Demo Data Set of Acme Corp and Globex](../2026-11-13%20Demo%20Data%20Set%20of%20Acme%20Corp%20and%20Globex.md) as files that `sform story import` and `sform story kv import` load, in `src/Platform/Storyteller/Playground/demo/` (§11 of the spec). The files were extracted from the spec's JSON blocks, so they are identical to it.

## What Was Done

### 11. Files and import

- `story/annotations/` (30), `story/schemas/` (11), `story/templates/` (3) and `story/configurations/` (30) hold the documents of §4, §7, §6 and §8, one file each, named as in the spec, two-space indented, UTF-8, LF line ends.
- `key-values.json`, `key-values.secrets.json` and `project-settings.json` sit next to `story/`, outside the import folder.
- `README.md` gives the story, the files, the import commands and a few reads to try.
- `docs/Platform/Storyteller/playground.md` has a section, "The demo data set", that points to the folder.

Before the files were written, a script checked the spec's documents: every file parses, every configuration passes its combined schema (Python `jsonschema`, draft-07, standing in for NJsonSchema), and every binding resolves on all 30 annotations (the merge rules of `inheritance.md`, the binding rules of `binding.md`, the official JSON-e library). The data set has not yet been imported into a running platform.
