# Revealing a key-value version records no Reveal audit event

Linear: [424-101](https://linear.app/424fun/issue/424-101/revealing-a-keyvalue-version-records-no-reveal-audit-event).

## Overview

This review covers all of [Revealing a key-value version records no Reveal audit event](../2026-10-31%20Revealing%20a%20key-value%20version%20records%20no%20Reveal%20audit%20event.md), delivered in one branch.

- A secret version read with `reveal` writes a `Reveal` event with the detail `version {n}` and counts one `reveals`, on both the catalog path and the history path of `GetVersionAsync`.
- A diff writes one `Reveal` per distinct secret version it compares, with the detail `diff {from}..{to}`, only after both sides resolved. The events carry the stored key.
- The MCP texts, the admin UI mock and the docs follow.

A multi-angle review of the branch (backend correctness, tests, docs and texts, each finding checked by an independent verifier) confirmed one finding: a new doc sentence said that every delivery of a secret's plaintext is a `Reveal`, which exports and binding reads are not. It was fixed (section 7).

| Commit | Content |
|---|---|
| `6332cac2` | The spec |
| `8ee5110f` | Sections 1 to 4: the service and its tests |
| `35fa0d16` | Section 5: the MCP texts |
| `aeb06c46` | Section 6: the admin UI mock and an e2e test |
| `7dcb6e7f` | Section 7: the docs |
| `470b9b75` | Fix from the review: the audit paragraph names the operations of the other deliveries |
| This commit | This review |

## What Was Done

### 1. `RecordRevealAsync` takes a detail

`CosmosKeyValueService.RecordRevealAsync` has a last parameter `string? detail = null`, passed to `AuditAsync`. The call in `GetAsync` is unchanged, so a reveal of the pair keeps `Detail = null`. No operation, model, SDK or OpenAPI change.

### 2. `GetVersionAsync` records a reveal on both paths

As the spec says. After `ResolveValueAsync`, the catalog path records with `catalog.Key`, `catalog.Version`, `catalog.Content.Protection` and `version {n}`; the history path with `item.Key`, `item.Version`, `item.Content.Protection`. The condition is the one of `GetAsync`: a non-null value of a secret level. A secret without `reveal`, a public value and a caller without secret rights (`SecretRightsRequired` throws first) record nothing. `reads.api` is still not counted on this route.

### 3. A secret diff records a reveal per secret version

- `GetVersionTextAsync` returns `(string Text, string? Key, KeyValueProtection? Protection)?`: `(string.Empty, null, null)` for version 0, `null` for an unknown version, otherwise the text, the stored key and the stored level. The `Hyok` refusal and the forced `reveal: true` stay. A null resolved text still reads as an unknown version, as before.
- `GetVersionChangesAsync` resolves both sides, then records the `from` side when it is a secret, then the `to` side when it is a secret and `toVersion != fromVersion`, then diffs. The two checks use property patterns (`from is { Key: { } fromKey, Protection: { } fromProtection }`) instead of null-forgiving operators.
- As planned, and unlike the CodeRabbit plan, the events carry the stored key, not the request's casing: `CosmosKeyValueAuditLog.QueryAsync` matches the key exactly. A test diffs `db.password` of a key written as `Db.Password` and expects `Db.Password` in the events.

### 4. Tests

Three tests in `Backend.CosmosDb/test/KeyValueAuditAndUsageTests.cs`, with a new `Reader` caller without secret rights:

- `SecretVersionReads_AreAuditedAsReveals`: version 3 (catalog) and version 1 (history) of a secret with `reveal` give exactly two `Reveal` events with their key, version, detail, level, view, actor, correlation id and channel. `reveal: false` on the current and on an older version, `reveal: true` on both paths of a public key, an unknown version and a reader without rights (which throws `AccessDeniedException`) give none.
- `SecretDiffs_AreAuditedPerSecretVersion`: a local helper returns the `Reveal` events a call added. `1..2` gives two events, `0..1` one, `2..2` one, a level change from public to secret one (the secret side, with its level), a public diff none, an unknown second version and a reader none, both throwing.
- `Usage_CountsRevealsOfSecretVersionsAndDiffs`: two version reveals, a diff `1..2` and a diff `0..1` of a secret, plus calls that reveal nothing, give `reveals = 5` in the usage report and five `Reveal` events.

No existing test changed: none reads a secret version or diffs a secret in a project whose audit or usage it asserts.

### 5. The MCP server's texts

- The description of `reveal_key_value` says that every reveal of a secret, of the current value or of a stored version, is recorded as a `Reveal` event. `SecretToolsTests.RevealKeyValue_DescriptionSaysWhichRevealsAreAudited` asserts the new sentence and that the description no longer says "not audited".
- The routing of the current version through the pair stays. Its comment now says why: an API from before this change still records that reveal.
- `mcp.md`: the row of `reveal_key_value` says the same as the description.
- `diff_key_value` is unchanged: it refuses secrets because their diff would put plaintext in the conversation.

### 6. The admin UI mock

`app/mock/key-value-handlers.ts`: the version route records a `Reveal` with `Detail: 'version {n}'` when it returns a secret's value with secret rights, and the diff route records one `Reveal` per version of `new Set([from, to])` that is a secret, with `Detail: 'diff {from}..{to}'`, after both sides resolved.

Added to the plan: the e2e test `reveals and compares secret versions, and the audit trail records both` in `test/e2e/key-values.spec.ts` reveals v1 of `db.password` and compares v2 in the versions panel, then finds one `version 1` and two `diff 1..2` reveals on the Audit tab of Monitor. It runs on desktop and mobile.

### 7. Documentation

`docs/Platform/Storyteller/key-values.md`:

- **Audit trail**: every direct read of a secret's value is a `Reveal` (the pair with `reveal`, a secret version with `reveal`, each secret version of a diff), with the details, and a failed diff writes nothing. After the review: exports and resolved configurations deliver secret values under `Export` and `BindingRead`, so a list of everyone who received a secret needs all three operations.
- **Usage meters**: `reveals` counts each `Reveal` event, each secret of an export and each secret binding read.
- **The service table**: `GetVersionAsync` and `GetVersionChangesAsync` write a `Reveal` for the secret plaintext they return.

## Verification

- **Backend.CosmosDb**, against a Testcontainers Linux emulator (`STORYTELLER_TESTS_USE_CONTAINER=true`, `TESTCONTAINERS_REUSE_ENABLE=true`, `TESTCONTAINERS_RYUK_DISABLED=true`), not the local one, whose databases the fixture deletes:
  - `KeyValueAuditAndUsageTests`: 8 of 8.
  - Every key/value test (`KeyValue*`, `CosmosKeyValueServiceTests`, `ClientEncryptionAndReferenceTests`, `BindingDependencyTests`): 85 of 85. The first run passed 71; the emulator crashed with a core dump during `Import_ManyItems_WritesInBatches`, as in earlier reviews, and the 14 tests after it failed to connect. After a restart of the container those 14 passed.
  - The full suite was not run, because of these emulator crashes; the change touches only the key/value service.
- **Mutation checks**: with the service of the base commit, the three new tests fail. Recording the request's key instead of the stored one fails the diff test, and recording the `from` side before the `to` side resolved fails it too.
- **Api.Functions**: the key/value and secret-rights tests, 258 of 258.
- **Mcp**: 727 of 727.
- **Admin UI**: `npm run lint`, `vue-tsc --build`, `npm test` (1452 of 1452), and `test/e2e/key-values.spec.ts` on desktop and mobile (27 passed, 1 skipped as before). With the mock of the base commit, the new e2e test fails.

## Left for Later

- `reads.api` is still not counted on the version and diff routes, as before this change.
- The MCP server can reveal every version through the version route, and `diff_key_value` could compare secrets, once every deployed API audits version reads. Both are choices of the MCP server, left to a ticket of their own.
