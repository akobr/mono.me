# Demo Data Set as the DbCreator Set `exhibit`

## Overview

The data set of [Demo Data Set of Acme Corp and Globex](../2026-11-13%20Demo%20Data%20Set%20of%20Acme%20Corp%20and%20Globex.md) moves from `src/Platform/Storyteller/Playground/demo/` (§11 of the spec, [the first review](2026-11-13%20Import%20Files%20of%20the%20Demo%20Data%20Set.md)) to the built-in data sets of DbCreator, as the set `exhibit`. The data sets come with Linear 424-252 (PR #195); the key/value pairs of a set with Linear 424-265, whose format the set already follows.

## What Was Done

### 11. Files and import

- **Location.** `src/Platform/Storyteller/DbCreator/src/data-sets/exhibit/`. The documents of `story/` moved to the root of the set: `annotations/` (30), `schemas/` (11), `templates/` (3), `configurations/` (30), unchanged.
- **Manifest.** `set.json`: `Name` `exhibit`, `Version` 1, target `fourth-wall` / `toons-and-tyrants` / `default`, with descriptions of the set, the project and the view. No `DependsOn`.
- **Key/value pairs.** One `key-values.json` at the root of the set, as 424-265 defines it: the 19 public pairs and the reference, and the `Standard` pair `globex.lairforce.escrowPin` that was `key-values.secrets.json`, in one `2s.key-values.v1` document ordered by key. Every item has a `Value` and a level of `Public` or `Standard`, so the file passes the refusals of 424-265; the `Standard` pair needs a Standard vault when it is seeded. `key-values.secrets.json` is gone.
- **Other files.** `README.md` (rewritten for the seed) and `project-settings.json` stay in the set. A seed counts them as ignored files and hashes them; `sform story import` ignores them too. Until 424-265 is done, `key-values.json` is an ignored file of the seed as well, and the pairs come from `sform story kv import`.
- **Docs.** `playground.md` points to the set. The spec is left as written.

The data-sets folder exists on the branch of PR #195 only; the set is added to the working tree of `main` and lands with it, or after it. Not seeded yet: DbCreator on `main` has no `seed` command.
