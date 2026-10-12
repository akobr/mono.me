# Key/Value Pairs

Storyteller stores named values that configurations and applications share: endpoints, switches, connection strings, API keys. Every pair belongs to one organization, project and view, like configurations, schemas and templates. Public pairs are plaintext; secrets are envelope-encrypted with keys that live in Azure Key Vault. One catalog lists both kinds, and a configuration binds any of them with `@key`.

The spec is [Native Key-Value Storage](specs/2026-10-11%20Native%20Key-Value%20Storage.md). Phase 1a (storage, the Standard level, lifecycle, import and export, binding) is reviewed in [Phase 1a of Native Key-Value Storage](specs/reviews/2026-10-12%20Phase%201a%20of%20Native%20Key-Value%20Storage.md). The HSM, BYOK and client-side levels, the project policy, the API, the CLI and the admin UI follow in later phases.

## Identity

A pair is identified by **organization + project + view + key**. Views are isolated: the same key in two views is two pairs with their own values and histories.

### Keys

A key is a binding path: segments of letters, digits and `_` that start with a letter or `_`, joined by `.`, at most 128 characters (`^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$`). `db.password` and `api_v2.host` are keys; `db-password` and `1st.key` are not, because the binding language could not address them. Every key can therefore be written as `@db.password` in a configuration.

Keys keep their casing but are unique case-insensitively. `DB.Password` and `db.password` are the same pair, shown with the casing of its first write. A key that breaks the rule is refused with `400 InvalidKey`.

### Values

A value is a string of at most 24,576 bytes in UTF-8 (`400 ValueTooLarge` otherwise). The empty string is allowed. An optional content type of up to 255 characters, such as `text/plain` or `application/json`, tells clients how to render the value; it is not validated.

Two content types mark a **reference**: `application/vnd.2s.secret-reference` and the Azure App Configuration format `application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8`. A reference is a public pair whose value points to a secret in the customer's own vault. The platform never resolves it; the consuming application does, with its own identity.

### Protection levels

| Level | Where the value is | Who reads it | Available |
| --- | --- | --- | --- |
| `Public` | Plaintext in Cosmos DB | Every reader of the project | Phase 1a |
| `Standard` | Envelope-encrypted in Cosmos DB; the data key is wrapped by a software key of the project in a Standard vault | Callers with secret rights | Phase 1a |
| `Hsm` | As Standard, with an HSM key (Key Vault Premium or Managed HSM) | Callers with secret rights | Phase 1b |
| `Byok` | As Standard, with the customer's own key | Callers with secret rights, while the customer allows it | Phase 1b |
| `Hyok` | A document the client encrypted for the project's recipients | Holders of a recipient key | Phase 1b |

Everything except `Public` is a secret. The level is chosen when a key is created and is required then, so a secret never becomes public because a client left the field out. An update without a level keeps the level. Until the project policy of Phase 1b exists, `Public` and `Standard` are the allowed levels; the others answer `409 ProtectionNotAllowed`.

A write that turns a public pair into a secret or a secret into a public pair needs secret rights, because the old versions keep their old level until they expire.

## Storage

Pairs live in the organization container (`org.{organization}`), in the partition `{project}.kv.{view}`. Everything a request does with one view stays in that partition: a point read for a key, one single-partition query for a listing, and atomic transactional batches for writes.

| Item | id | Content |
| --- | --- | --- |
| Catalog item | `kv.{lower-cased key}` | The current version: key, protection, value or envelope, author, timestamps, the deleted state |
| Version | `kvv.{lower-cased key}.{version}` | A version that is no longer current, with its own `ttl` |

Versions are separate items on purpose. A read of the current value costs about 1 RU whatever the history is, a listing loads catalog items only, and a new version costs one create and one replace in one batch. Embedding the history in the catalog item would make every read and every listing pay for it, and could not expire versions one by one.

The version items of one key are queried by the id prefix `kvv.{key}.` and carry `NormalizedKey` (the lower-cased key) for queries over many keys at once, such as the next-version lookup of an import.

### Lifecycle

| Operation | Effect |
| --- | --- |
| Create | Version 1, or one more than the highest version still stored, so numbering continues after an expired deleted key |
| Update | The current version becomes a version item with the rest of its lifetime as `ttl`; the catalog item gets `Version + 1`. The same value, content type and level is not written again |
| Delete | Soft. The current version is written to history, and the catalog item gets `DeletedAt`, `DeletedBy` and a `ttl` of 90 days (`KeyValues:DeletedRetention`). The key is not found by reads and listings unless they ask for deleted keys |
| Restore | The catalog item loses its deleted state and `ttl`, and the history item of the current version is removed again. **No new version** |
| Write to a deleted key | Continues the numbering; the old versions are still there |
| Permanent delete, purge | Every version item, then the catalog item. Nothing is left, and a new create starts at version 1 |

Versions expire 365 days after they were written (`KeyValues:VersionRetention`). The current version of an active key never expires. A deleted key expires with its catalog item; its versions keep their own expiry, so a create within a year of them continues the numbering.

The expiry is done by Cosmos DB TTL. Every container of the platform has TTL enabled without a default (`DefaultTimeToLive = -1`), so only items with their own `ttl` expire. `ContainerFactory` sets it on new organization containers, and the plain `2sInitDb` run of `DbCreator` switches it on for containers that existed before (see the [container settings](annotation-lifecycle.md#containers)). **Turning it on also starts the expiry of configuration, schema and template history older than 365 days**, as their `ttl` has always said.

### Concurrency

Every write replaces the catalog item with the ETag it read, and retries a conflict up to 8 times with a short random pause. A caller can also pass `ExpectedVersion`: `null` means no check, `0` means the key must not exist or must be deleted, and any other value must equal the current version (`412 KeyValueVersionMismatch` otherwise, without a retry). A key that is contended beyond the retries answers `409 KeyValueBusy`.

## Envelope encryption

A `Standard` (later also `Hsm` and `Byok`) version is encrypted on the server:

1. A random 256-bit data key and a random 96-bit nonce are generated for the version.
2. The UTF-8 value is encrypted with AES-256-GCM (128-bit tag). The associated data is `2s.kv.v1|{organization}|{project}|{view}|{lower-cased key}|{version}|{level}`, so a ciphertext copied to another key, view, project or version fails to decrypt (`KeyValueIntegrityException`, 500).
3. The data key is wrapped with RSA-OAEP-256 by the key-encryption key (KEK) of the level, and the data key is zeroed.
4. The catalog or version item stores the envelope: the versioned KEK id, the algorithm, and the base64url wrapped key, nonce and ciphertext-plus-tag.

Plaintext never reaches Cosmos DB or the logs. Decrypted values are cached in process memory per version, for `KeyValues:Protection:Standard:CacheDuration` (10 minutes, sliding) and up to `CacheSizeLimit` bytes (64 MiB). A version is immutable and a new version has a new cache key, so the cache can never serve a stale value.

### The Standard level and Key Vault

The Standard KEK is one RSA-2048 software key per project, named `kek-` plus 32 hex characters of SHA-256 of `{organization}/{project}`, in the Standard vault `AzureKeyVaults:{KeyValues:Protection:Standard:VaultName}` (`default` by default). It is created on the first Standard write of the project, with the operations `wrapKey` and `unwrapKey` and a yearly rotation policy. Wrapping uses the latest key version and records it in the envelope; unwrapping uses the recorded version, so a rotation never breaks an old envelope.

The Storyteller identity needs the roles **Key Vault Crypto Officer** (create keys, set rotation policies) and **Key Vault Crypto Service Encryption User** (wrap, unwrap) on that vault. A deployment without the vault still works for public pairs; a Standard write then answers `501 ProtectionNotConfigured`.

Key Vault allows 4,000 operations per 10 seconds per vault for software RSA-2048 keys. `MaxConcurrentKeyOperations` (8) caps the parallel operations of one instance, the SDK retries `429` with `Retry-After`, and a vault that keeps throttling or failing answers `503 KeyEncryptionUnavailable` with `Retry-After`. A resolved configuration with a secret binding fails the same way rather than leaving the secret silently unbound.

### Configuration

```jsonc
{
  "KeyValues": {
    "VersionRetention": "365.00:00:00",
    "DeletedRetention": "90.00:00:00",
    "Protection": {
      "Emulator": false,                 // Lowkey Vault for local work, never in production
      "MaxConcurrentKeyOperations": 8,
      "CacheSizeLimit": 67108864,
      "Standard": {
        "VaultName": "default",          // a key of AzureKeyVaults
        "KeyNamePrefix": "kek-",
        "KeySize": 2048,
        "RotationInterval": "365.00:00:00",
        "CacheDuration": "00:10:00"      // 00:00:00 turns the cache off
      }
    }
  },
  "AzureKeyVaults": { "default": "https://<vault>.vault.azure.net/" }
}
```

`AddCosmosDbAnnotations` registers the service and the envelope protector. `AddKeyVaultKeyProtection(configuration, environment)` (project `KeyValues.Azure.KeyVault`) registers the Standard key provider when the vault is configured. Production uses the managed identity (`ManagedIdentityCredential`, then `EnvironmentCredential`); other environments use `DefaultAzureCredential`.

### Local development

Microsoft ships no Key Vault emulator. The Aspire host runs [Lowkey Vault](https://github.com/nagyesta/lowkey-vault) (`nagyesta/lowkey-vault`) and passes its address to the API as `AzureKeyVaults__default` with `KeyValues__Protection__Emulator=true`. In emulator mode the client uses a dummy token, skips the challenge resource check, accepts the self-signed certificate, and creates its keys with the `encrypt` and `decrypt` operations too, because Lowkey Vault checks `decrypt` on an unwrap. Emulator mode fails the startup in the Production environment.

The integration tests of `KeyValues.Azure.KeyVault/testint` start Lowkey Vault with Testcontainers, or use a running instance named by `LOWKEY_VAULT_URL`.

## Service

`IKeyValueService` (`Backend.Core/src/KeyValues/`) is implemented by `CosmosKeyValueService`. Every call takes the organization, project and view, and the writes take a `KeyValueCaller`: the author, `CanReadSecrets`, `CanWriteSecrets`, and the channel. The API decides the rights; the service enforces them, because only the service knows whether a key is a secret.

| Method | Behaviour |
| --- | --- |
| `ListAsync` | One page of catalog items as `KeyValueSummary` (key, level, content type, version, author, updated, deleted state, expiry), filtered by key prefix, levels and state (`Active`, `Deleted`, `All`), ordered by key, with a continuation token. Values of public pairs only with `IncludeValues`; values of secrets never |
| `GetAsync` | The current version. A secret's value only with `Reveal` and `CanReadSecrets` (`403` otherwise); a deleted key only with `IncludeDeleted` |
| `SetAsync` | Create or update, see the lifecycle |
| `DeleteAsync` | Soft, or permanent with `Permanent`. False when the key does not exist |
| `RestoreAsync`, `PurgeAsync`, `ProtectAsync` | Bulk, by a list of at most 100 keys or by a prefix of at most 1,000 keys. Each key gets its own outcome: `Done`, `Unchanged`, `NotFound`, `Forbidden`, `NotAllowed` or `Failed`. Protect changes the level between the server-side levels and writes a new version; public and client-encrypted pairs need a new value from the client |
| `GetVersionsAsync` | Every stored version with its level, author, creation time, expiry and `IsCurrent` |
| `GetVersionAsync`, `GetVersionChangesAsync` | One version, and the text diff of two versions (version 0 is the empty text). Secrets need `CanReadSecrets` |
| `ExportAsync`, `ImportAsync` | The document format below |
| `LookupAsync` | For the binding sources: the current value, or null when missing, deleted, or a secret without secrets included |

Error codes of this area: `InvalidKey`, `ValueTooLarge`, `ProtectionRequired`, `ProtectionNotAllowed`, `ProtectionNotConfigured`, `KeyValueNotFound`, `KeyValueBusy`, `KeyValueVersionMismatch`, `KeyEncryptionUnavailable`, `ImportInvalid`, `SecretValueMissing`, `NotSupportedForProtection`, `ReprotectNotPossible`, and `AccessDenied` for missing secret rights.

## Import and export

The API speaks one format; clients convert `.env` and flat JSON to it.

```json
{
  "Format": "2s.key-values.v1",
  "Items": [
    { "Key": "db.host", "Protection": "Public", "Value": "sql.internal", "ContentType": "text/plain" },
    { "Key": "db.password", "Protection": "Standard", "Value": "…" },
    { "Key": "api.token", "Protection": "Standard" }
  ]
}
```

An export lists the active keys, filtered by prefix and levels, ordered by key. Secret values are included only with `IncludeSecrets`, which needs `CanReadSecrets`; otherwise a secret item has no `Value`. Such an item imports as "leave the existing secret alone", and fails with `SecretValueMissing` for a key that does not exist.

An import of up to 1,000 items first validates everything (keys, sizes, duplicates, allowed levels, and with `Replace` the prefix) and plans every item against the current state, deleted keys included. Any error returns `400 ImportInvalid` with the failing items, and nothing is written. The plan gives each item `Create`, `Update`, `Unchanged` or, in `Replace` mode, `Delete` for the active keys under the prefix that the document does not list. `DryRun` returns the plan. The writes go in atomic batches of 50 keys; a batch that another writer disturbed falls back to single writes. An import is idempotent: a second run of the same document changes nothing, and a run after a partial failure writes the rest. The keys a `Replace` deleted come back with one restore by prefix.

A caller without secret rights can import public items only: a secret value in the document, a level change to or from a secret, or a `Replace` that would delete a secret answers `403`.

## Binding

The native store is the **default binding source** of resolved configurations, and also the sources `public` and `secret`. See [binding.md](binding.md) for the language.

```json
{
  "host": "@db.host",
  "password": "@db.password",
  "onlyPublic": "@public(db.host)",
  "onlySecret": "@(db.password, secret)",
  "url": "@[https://@api.host/v1]"
}
```

- A configuration sees the pairs of its own organization, project and view. Keys are matched case-insensitively.
- A public pair always binds. A secret binds only when the caller may receive secrets (`ContributorWithSecrets`, or the scope `Configuration.Secrets`); otherwise the string stays as stored, like any unresolved binding.
- `public` declines secrets, and `secret` declines public pairs.
- A missing or deleted key declines. At the top level the string stays as stored; inside an interpolation or a math expression the read fails with a binding error (`422`).
- Each key is looked up once per resolved read, and a changed pair takes effect on the next read. Nothing is cached across reads.
- A vault that keeps throttling fails the read with `503`; a secret is never silently left unbound.

A vault entry of `AzureKeyVaults` named `default`, `public` or `secret` is still a vault, but no longer a raw binding source. Give such a vault another name to read it by its native secret names, for example `@(db.password, shared)`.

## Request charges

Measured on the Cosmos DB emulator with the default indexing policy and small values (the Phase 1a review has the numbers): a point read of a key is about 1 RU, a create about 6-7 RU, an update or a soft delete (history plus catalog item in one batch) about 15-20 RU, and a listing page a few RU plus the loaded rows. The value paths are indexed like everything else; excluding them is left for when a measurement shows a difference (spec D7).
