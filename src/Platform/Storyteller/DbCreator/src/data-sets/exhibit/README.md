# Data set `exhibit`: Acme Corp and Globex

Fourth Wall Software builds CRM, HR, CAD and Social solutions for two customers: Acme Corp (cartoon physics, rocket skates, anvils) and Globex Corporation (hammocks, relocation packages, a doomsday device). The full model, with every design decision and worked examples, is in [the spec](../../../../../../../docs/Platform/Storyteller/specs/2026-11-13%20Demo%20Data%20Set%20of%20Acme%20Corp%20and%20Globex.md).

| Path | Content |
| --- | --- |
| `set.json` | the manifest: target `fourth-wall` / `toons-and-tyrants` / `default` |
| `annotations/` | 30 annotations |
| `schemas/` | 11 schemas (type, annotation, descendant type) |
| `templates/` | 3 templates, gated per Company with JSON-e |
| `configurations/` | 30 configurations |
| `key-values.json` | 20 pairs: 18 public pairs, a secret reference (public too) and one `Standard` pair (a demo value, not a secret) |
| `project-settings.json` | the type names (Company, Department, Solution, Application, AppUsage, Job, JobInstance) for the admin UI |

A seed counts `README.md` and `project-settings.json` as ignored files. They are part of the set's hash, so changing them makes the next seed read the set again; that writes nothing, because a seed never overwrites.

## Seed

The seed writes the pairs of `key-values.json` too ([data sets](../../../../../../../docs/Platform/Storyteller/data-sets.md#key-valuesjson)). The `Standard` pair needs a Standard vault: without one, the seed refuses the set before it writes anything (exit `2`), unless `--public-pairs-only` skips it (`seed exhibit --public-pairs-only`; a resolved read then leaves the binding of the `Standard` pair as it is stored, until a seed with a vault adds it). Locally, seed into the database and the Lowkey Vault of the Aspire host, with the values the dashboard shows: `ConnectionStrings__cosmos` in the environment of `db-creator`, and `AzureKeyVaults__default` in the environment of `db-seed`. Copy the vault's URI as it is (`https://localhost:<port>/`): Lowkey Vault puts the host of a request into the ids of its keys, and the API, which reaches it as `localhost`, refuses to unwrap with a key of another host such as `127.0.0.1`. See [Running it](../../../../../../../docs/Platform/Storyteller/data-sets.md#running-it).

```sh
ConnectionStrings__cosmos='<from the dashboard>' AzureKeyVaults__default=https://localhost:<port>/ KeyValues__Protection__Emulator=true \
  dotnet run --project src/Platform/Storyteller/DbCreator/src -- seed exhibit
```

Many configurations interpolate the pairs (`@[...]`), so a resolved read answers `422` without them. To add the pairs to a project that was seeded or imported without them, import them through the API:

```sh
sform account set -p fourth-wall.toons-and-tyrants -v default
sform story kv import src/Platform/Storyteller/DbCreator/src/data-sets/exhibit/key-values.json
```

The `Standard` pair needs the right to write secrets there.

## Import instead of seeding

The set is also an archive folder for `sform story import`, which ignores `set.json`, `key-values.json`, `project-settings.json` and this file:

```sh
sform story import src/Platform/Storyteller/DbCreator/src/data-sets/exhibit --dry-run
sform story import src/Platform/Storyteller/DbCreator/src/data-sets/exhibit
```

Then set the type names in the admin UI (Project settings → Type names) from `project-settings.json`; neither the seed nor the import stores them.

## Things to try

```sh
sform story config uxe.acme.cad.anvil-logistics.gravity-suspension-worker --resolved
sform story config exe.globex.social.doomsday-ux --resolved
sform story config composition exe.acme.cad.anvil-logistics
sform story config exe.acme.hr.rocket-skates --resolved --format env
```
