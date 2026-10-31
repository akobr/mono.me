# Revealing a key-value version records no Reveal audit event

Linear: [424-101](https://linear.app/424fun/issue/424-101/revealing-a-keyvalue-version-records-no-reveal-audit-event). Found while building the MCP server (424-84, review `reviews/2026-10-24 MCP Server for the Storyteller API.md`, "Left for later"). A CodeRabbit coding plan on the issue was read as a second opinion; where this spec differs from it, the difference is called out.

## Problem

Two service paths deliver a secret's plaintext without a `Reveal` audit event and without counting the `reveals` meter:

1. **A secret version.** `GET …/key-values/{key}/versions/{version}?reveal=true` returns the plaintext of any stored version to a caller with secret rights. `CosmosKeyValueService.GetVersionAsync` never records the reveal, neither for the current version (read from the catalog item) nor for an older one (read from its history item). A secret can be read through this route with no audit trail and no usage count.
2. **A secret diff.** `GET …/key-values/{key}/versions/{version}/diff` and `…/diff/{versionFrom}` return the text diff of two versions. For a secret, the diff carries the plaintext of both sides, and the route has no `reveal` flag: the service always resolves the values with secret rights. Nothing is recorded either.

`docs/Platform/Storyteller/key-values.md` says that "every secret-related operation writes a `KeyValueAuditEvent`", and lists "a reveal, a secret version or diff" among the ways a secret value is delivered. Only the reveal of the current value through the pair route (`GetAsync`) is audited. The admin UI reveals and compares secret versions in its versions panel, `sform story kv get --version --reveal` and `sform story kv diff` do the same, and none of it shows in the audit trail.

## Current State

- `Backend.CosmosDb/src/KeyValues/CosmosKeyValueService.cs`:
  - `GetAsync` (`:102-124`) resolves the value, counts `reads.api`, and calls `RecordRevealAsync` when the value is not null and the level is a secret (`:118-121`).
  - `GetVersionAsync` (`:319-341`): the catalog path (`:326-330`) resolves `catalog` with `ResolveValueAsync(place, catalog, reveal, caller)`; the history path (`:332-340`) reads the history item and resolves `item.Content`. Neither records anything.
  - `GetVersionChangesAsync` (`:343-355`) calls `GetVersionTextAsync` for both sides, throws `InvalidOperationException` for an unknown version (the API maps it to `404`), and returns `_differ.GetTextChanges(fromText, toText)`.
  - `GetVersionTextAsync` (`:753-783`) returns `string.Empty` for version 0, reads the catalog item or the history item, rejects `Hyok` with `400 NotSupportedForProtection`, and resolves with `reveal: true`, so a caller without secret rights gets `SecretRightsRequired` (`403`). It returns only the text: the level and the stored key are lost to the caller.
  - `ResolveValueAsync` (`:715-733`) returns a public value as is, `null` for a secret without `reveal`, throws `SecretRightsRequired` without `CanReadSecrets`, and otherwise unprotects.
  - `RecordRevealAsync` (`:1524-1528`) adds one `reveals` and writes an event with `Detail = null`. `AuditAsync` (`:1530-1552`) writes the event with the caller's actor, channel and correlation id.
  - `ExportAsync` (`:397-404`) counts one `reveals` per secret and writes one `Export` event. `LookupWithMarkerAsync` (`:545-550`) counts one `reveals` per secret binding read and aggregates `BindingRead` events.
- The audit records the stored key: `GetAsync` and the writes use `entity.Key`, the casing the key was written with. `CosmosKeyValueAuditLog.QueryAsync` filters with `c.Key = @key`, an exact match.
- `Api.Functions/src/V1/KeyValuesHttp.cs`: `GetKeyValueVersion` (`:409-441`) refuses `reveal` under the actor and mTLS rules and calls `GetVersionAsync`. `DiffAsync` (`:630-646`) runs the diff under `KeyValueAuthorization.UnderSecretReadRuleAsync`, which turns the service's `SecretRightsRequired` into the actor or mTLS refusal. Neither writes audit events: the service does.
- Tests: `Backend.CosmosDb/test/KeyValueAuditAndUsageTests.cs` covers the audit of `GetAsync`, exports, writes and binding reads, and the usage meters. Nothing covers a version read or a diff of a secret. `CosmosKeyValueServiceTests` covers their values and rights.
- MCP: `Mcp/src/Tools/SecretTools.cs` (`:18-58`) reveals the current version through the pair route "because the API audits a reveal through the pair, not through its version route", and its description tells the assistant that "a reveal by an older version number is not audited by the API". `Mcp/test/SecretToolsTests.cs:83-90` asserts that text. `docs/Platform/Storyteller/mcp.md:364` says the same. `diff_key_value` refuses secrets because the diff carries plaintext; that refusal is a choice of the MCP server, not a gap of the audit.
- Admin UI mock (`ui.admin/src/app/mock/key-value-handlers.ts`): the pair route records a `Reveal` in the mock audit (`:499-504`); the version route (`:689-708`) and the diff route (`:710-749`) record nothing, like the API today.

## Proposed Changes

### 1. `RecordRevealAsync` takes a detail

`RecordRevealAsync(Place place, KeyValueCaller caller, string key, ulong version, KeyValueProtection protection, KeyValueAuditOperation operation, string? detail = null)` passes `detail` to `AuditAsync`. The call in `GetAsync` stays as it is, so its events keep `Detail = null`. No new operation: a version read and a diff are reveals, so `sform story kv audit --operation reveal`, the admin UI filter and the audit queries find them without changes, and `KeyValueAuditOperation`, the SDKs and the OpenAPI document stay as they are.

### 2. `GetVersionAsync` records a reveal on both paths

After `ResolveValueAsync` returns, on the catalog path and on the history path, when the value is not null and the level is a secret (the condition of `GetAsync`):

```csharp
await RecordRevealAsync(place, caller, catalog.Key, catalog.Version, catalog.Content.Protection, KeyValueAuditOperation.Reveal, $"version {catalog.Version}");
// history path: item.Key, item.Version, item.Content.Protection
```

- The detail is `version {n}`. The event's `Version` already holds the number; the detail tells a version-route reveal from a pair-route reveal (`Detail = null`).
- A secret without `reveal` resolves to `null` and records nothing. A public value records nothing. A caller without secret rights gets `SecretRightsRequired` before anything is recorded.
- A `Hyok` version with `reveal` returns the stored JWE document and is recorded, as `GetAsync` records it.
- A deleted key's current version is recorded too: the route returns it.
- `reads.api` stays uncounted on the version routes, as today (see "Left out").

### 3. A secret diff records a reveal per secret version

`GetVersionTextAsync` returns what the diff needs to audit, or `null` for an unknown version:

```csharp
private async Task<(string Text, string? Key, KeyValueProtection? Protection)?> GetVersionTextAsync(Place place, string key, ulong version, KeyValueCaller caller)
```

- Version 0 is `(string.Empty, null, null)`: the empty text reveals nothing.
- Otherwise the text, the stored key (`catalog.Key` or `item.Key`) and the stored level. The `Hyok` refusal and the forced `reveal: true` stay.

`GetVersionChangesAsync` resolves both sides first, then records, then diffs:

- One `Reveal` per distinct secret version the diff resolved, each with its own `Version` and level, and the detail `diff {from}..{to}` (the request's order, for example `diff 1..2`, or `diff 0..1` for the first version).
- `from == to` records the version once.
- When one side is unknown, `Hyok`, or refused for rights, the call throws before anything is recorded. Recording after both sides resolve keeps a failed diff out of the audit.
- A public side records nothing, so a diff between a public and a secret version (a level change) records only the secret side.
- The event's key is the stored key, as in `GetAsync`, not the casing of the request. *(Differs from the CodeRabbit plan, which passes the request's key: the audit query matches the key exactly, so `DB.Password` in the request would hide the event from a query for `db.password`.)*

The `reveals` meter counts one per recorded event, as an export counts one per secret.

### 4. Tests

In `Backend.CosmosDb/test/KeyValueAuditAndUsageTests.cs`, with the existing `Admin` caller and a fresh project per test:

- `SecretVersionReads_AreAuditedAsReveals`: a secret with three versions; `GetVersionAsync(reveal: true)` of the current version (catalog path) and of version 1 (history path) write one `Reveal` each, with the right `Version`, level, detail `version {n}`, actor, channel and correlation id. `reveal: false` on the secret, `reveal: true` on a public key and a call without secret rights (which throws) write none.
- `SecretDiffs_AreAuditedPerSecretVersion`: a diff `1..2` of a secret writes two `Reveal` events with versions 1 and 2 and detail `diff 1..2`; `0..1` writes one; `2..2` writes one; a diff of a public key writes none; a diff with an unknown version and a diff without secret rights throw and write none; a diff of a key whose level changed from public to secret writes only the secret side; a diff asked with another casing of the key records the stored key.
- `Usage_CountsRevealsOfSecretVersionsAndDiffs`: after `Usage.FlushAsync()`, `TakeSnapshotAsync` and `GetReportAsync`, `reveals` equals the number of recorded reveals.

The existing tests keep their counts: none of them reads a secret version or diffs a secret in a project whose audit or usage it asserts. The Api.Functions tests use a fake service and assert no audit.

### 5. The MCP server's texts

The routing of `reveal_key_value` stays: revealing the current version through the pair is still audited, and keeps an MCP server in front of an API without this fix from revealing unaudited. Only the texts change:

- The description of `reveal_key_value`: the API records every reveal of a secret, of the current value or of a stored version, as a `Reveal` event. The assertion in `SecretToolsTests` follows.
- The comment above the routing says why it stays.
- `docs/Platform/Storyteller/mcp.md:364`: the same statement.

`diff_key_value` keeps refusing secrets: its reason is the plaintext in the conversation, not the audit.

### 6. The admin UI mock

The mock follows the API: the version route records a `Reveal` with `Detail: 'version {n}'` when it returns a secret's value (`reveal=true`, a secret, secret rights), and the diff route records one `Reveal` per distinct secret version with `Detail: 'diff {from}..{to}'` after both sides resolve. No component changes: the Audit tab already shows `Detail`.

### 7. Documentation

`docs/Platform/Storyteller/key-values.md`:

- **Audit trail**: which deliveries write a `Reveal` (a reveal of the pair, a secret version read with `reveal`, each secret version a diff resolves) and their details (none, `version {n}`, `diff {from}..{to}`); a diff that fails writes nothing.
- **Usage meters**: `reveals` counts each of these, plus each secret of an export and each secret binding read.
- **The service table**: `GetVersionAsync` and `GetVersionChangesAsync` audit the secret plaintext they return.

## Phases

1. Sections 1 to 4: the service and its tests (one `fix` commit).
2. Sections 5 to 7: the MCP texts, the mock, the docs.
3. The review.

## Left out

- `reads.api` on the version and diff routes: `GetAsync` and `ListAsync` count it, the version routes never did. Metering reads is a separate decision.
- Removing the MCP routing workaround of `reveal_key_value` and letting `diff_key_value` compare secrets: a follow-up for the MCP server once every deployed API audits version reads.
