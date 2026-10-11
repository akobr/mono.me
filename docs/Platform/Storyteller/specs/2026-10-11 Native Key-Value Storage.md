# Native Key-Value Storage

> Linear: [424-5 Add support for native key value pair storage](https://linear.app/424fun/issue/424-5/add-support-for-native-key-value-pair-storage). Branch `feat/platform/key-value-storage`.
>
> Two rounds of questions were answered on 2026-10-11 and are worked into this version (see [Resolved questions](#resolved-questions-2026-10-11)). The second round replaced Key Vault secrets as the storage of secret values: every version, public or secret, now lives in Cosmos DB. Secret versions are envelope-encrypted, and Key Vault holds only the keys that wrap their data keys. That is the only way to get a physical 1-year version TTL and HSM protection, see [Key Vault facts](#key-vault-facts).

## Problem

Storyteller stores annotations, configurations, schemas and templates, but it has no plain key/value pairs. Values that many configurations or applications share, such as endpoints, switches, connection strings and API keys, have no home:

- **No storage for plain pairs.** Values get copied into configuration documents, so one change means editing many documents.
- **Secrets can be read, but not managed.** `KeyVaultBindingSource` reads a secret from a vault configured for the whole deployment (`AzureKeyVaults`), under a global name (`db.password` becomes `db--password`). Nobody can create, change, list or delete a secret through Storyteller. Secrets are not scoped to an organization or a project, and they have no history, no author, no protection choice and no UI.

Issue 424-5 asks for:

| # | Requirement |
| --- | --- |
| R1 | CRUD: add, update, delete ("remove" and "delete" in the issue are the same operation) |
| R2 | Versions and the author of each version, read-only |
| R3 | Public pairs and secrets: two stores, managed as one in the UI |
| R4 | Secrets protected by Azure Key Vault, using its native versioning |
| R5 | Public pairs in Cosmos DB, with partitioning and data shape chosen for the best RU cost. Can versions be part of the same document? |
| R6 | History and versions are loaded only when the user explicitly asks for them |
| R7 | Import and export of many pairs |
| R8 | A new section in the Storyteller API: manipulation, read-only version endpoints, import and export |

The answers of 2026-10-11 add:

| # | Requirement |
| --- | --- |
| R9 | Delete is a soft delete. A deleted key is kept for 90 days, never blocks its name, and creating it again continues its history |
| R10 | A restore endpoint brings one or more deleted keys back to their latest version, without creating a new version |
| R11 | Lifetimes are handled by Cosmos DB TTL: versions live one year, like configuration versions |
| R12 | The native store is the default binding source: `@db.password` binds a public pair always, and a secret only when the caller may see it. `@(db.password, public)` or `@public(db.password)` reads only public pairs, `@(db.password, secret)` or `@secret(db.password)` only secrets |
| R13 | Permanent delete of a key, and purge of soft-deleted keys |
| R14 | A security level per key: Standard, HSM, BYOK, HYOK, or Reference only (a public pair that points to a secret elsewhere) |
| R15 | A project policy decides which levels are allowed |
| R16 | The level of a key can change between Standard, HSM and BYOK. Changes to or from Public or HYOK need a new value from the client |
| R17 | Secrets travel only over secure lines: HTTPS everywhere, mTLS for machines, private networks inside the platform |
| R18 | Usage is metered per project, so that the levels can be priced |

## Current State

### Cosmos DB

- One database `42.Platform.2S`, with the container `core` and one container per organization, `org.{organization}` (`CosmosConstants`). The partition key path is `/PartitionKey` (`Backend.CosmosDb/src/ContainerFactory.cs:28-30`). Throughput is shared at database level, autoscale with a maximum of 1000 RU/s by default (`CosmosDbOptions.AutoscaleMaxThroughput`). Every RU spent by one operation is taken from all other operations of the deployment.
- Logical partitions of an organization container:

  | Partition | Content |
  | --- | --- |
  | `{project}.rst.{responsibility}`, `{project}.sbt.{subject}` | Annotations, configurations, configuration history |
  | `{project}.schema` | Schemas, their history and state items |
  | `{project}.template` | Templates, their history and state items |
  | `{project}.meta` | Registered views (`ViewEntity`) |
  | `{project}.access` | Shared certificates (`CosmosSharedCertificateStore`) |

- The `core` container holds accounts and access points (partition `access`). The project's machine authentication policy is a property of `AccessPointEntity` (`MachineAuthentication`), read and cached by `CosmosMachineAuthenticationPolicyStore`.
- Ids carry the view and a kind prefix: `{view}.cnf.{key}`, `{view}.gen.{type}`, and so on (`Entities/EntityIdPrefixTypes.cs`).
- **Versioning pattern** (`Configuring/CosmosConfigurationService.cs`, `CosmosConfigurationTemplateService.cs`):
  - The current item is accompanied by immutable history items with `ttl` = 365 days.
  - A create sets `Version = max(history.Version) + 1`, so numbering continues after a delete, as long as history exists.
  - Each write is one transactional batch (history and replace with `IfMatchEtag`), retried up to 3 times (templates: `CreateOrUpdateTemplateAsync` lines 76-155, `TryExecuteWriteAsync` 423-441).
  - A delete writes the history of the current version and deletes the item in one batch.
  - Listings are single-partition projections filtered by id prefix.
- **TTL is not enabled on any container.** `ContainerFactory` never sets `DefaultTimeToLive` (decision 17 of the mTLS spec says so). Cosmos ignores an item's `ttl` unless the container has TTL enabled, so the 365-day histories never expire, although `ConfigurationVersion.ExpirationTime` and the admin UI's "expired" badge say they do.
- **Diffs:** `Configuring/JsonContentDiffer.GetChangesAsync` (lines 23-88) serializes two `JObject`s and runs DiffPlex on the text. The text part works for any two strings.
- **View discovery:** `CosmosViewService` (line 145) runs `SELECT DISTINCT VALUE c.ViewName … WHERE STARTSWITH(c.PartitionKey, @prefix) AND IS_DEFINED(c.ViewName)`.

### Key Vault in Storyteller

- **`Binding.Azure.KeyVault`.** `AddAzureKeyVaultBindings` registers one named `SecretClient` and one `KeyVaultBindingSource` per entry of `AzureKeyVaults` (name → URI). The entry name becomes the binding source key, so a vault named `default` is the default binding source today. Credentials: `ManagedIdentityCredential` + `EnvironmentCredential` in production, `DefaultAzureCredential` otherwise.
- **`Access.Certificates.Azure.KeyVault`.** It reads the same `AzureKeyVaults` section and registers a `KeyClient` for the machine CA key. Its registration is commented out in `Api.Functions/src/Program.cs:87`.
- **No local vault.** The Aspire host runs only the Cosmos emulator, the Functions API and the admin UI (`Aspire.Host/src/AppHost.cs`).

### Key Vault facts

These facts, checked against the Microsoft documentation, shaped this design:

| Fact | Consequence |
| --- | --- |
| Single secret versions cannot be deleted. They go only when the whole secret is deleted and purged | A 1-year version TTL cannot be physical for values stored as Key Vault secrets |
| A secret's `exp` is informational: a get still returns an expired version | Expiry cannot be enforced by the vault either |
| HSM protection (Key Vault Premium, Managed HSM) applies to **keys**. Secrets are encrypted the same way in Standard and Premium, and Managed HSM stores no secrets at all | The HSM and BYOK levels need envelope encryption with an HSM key or a customer key anyway |
| Microsoft recommends adding "a separate protection key" for highly sensitive data | Envelope encryption is the expected pattern |
| Limits per vault and region, per 10 s: 300 secret creates; 4,000 other operations; RSA-2048 HSM key operations 2,000; RSA-3072 HSM key operations 500 | HSM keys use RSA-2048 by default. Plaintext is cached, so unwraps happen on cache misses only |
| Per-key billing for HSM keys: every version used in the last 30 days counts as one key | One key-encryption key per project, never one per value |
| When another tenant's key is used, the key owner is billed for the operations | BYOK operations show up on the customer's Azure bill |

### Binding language

- **Identifiers** start with a letter or `_` and continue with letters, digits and `_` (`Binding.Language/src/Tokenizer.cs:65-72`). A path is identifiers joined by `.`.
- **Sources** have no scope. `BindingRequest` carries only `Path` and `IncludeSecrets`. `BindingScope.Context`, a `ConfigurationBindingContext` with the configuration's `FullKey`, reaches only functions (`Binding.Language/src/BindingEvaluator.cs:124-160`).
- **Bare path arguments** of a function call are resolved through the default source before the function runs (`EvaluateArgumentAsync`).
- **Registration.** `BindingsOptions.AddSource` stores sources by key, and the last registration of a key wins.
- **Resolution** runs on every resolved read. Only `CalculatedContent`, from before binding, is cached.

### Secrets, machines and transport

- **Role.** `AccountRole.ContributorWithSecrets` (3) sits between `Contributor` and `Administrator`. It is checked only in `GetConfigurationResolved` (`Api.Functions/src/V1/ConfigurationHttp.cs:144-149`), which needs the scope `Configuration.Secrets` or a user with `ContributorWithSecrets` or higher, and passes the result down as `includeSecrets`.
- **Machine scopes.** `MachineAccessScopes.Get` never grants `Configuration.Secrets`, so API-key and certificate machines cannot read secrets. AuthKit machines can, through `Auth:AuthKit:PermissionMap`.
- **mTLS.** `MachineAuthenticationMiddleware` validates a client certificate (forwarded in a configured header) and records `FunctionContextItemKeys.PresentingCertificateThumbprint`. Machine certificates are looked up in `IClientCertificateStore`, whose only implementation is `InMemoryClientCertificateStore`: a certificate works only on the instance that issued it, and a restart forgets it (`docs/Platform/Storyteller/authentication.md`, known limitations).
- **Authors** are `account: {id}` or `application: {appId}` (`HttpRequestDataExtensions.GetAuthor`, line 79).
- **Errors.** `StorytellerException` subclasses are mapped by `ErrorResponseMapping`: 403, 404, 409, 400. The stable codes are in `Backend.Core/src/ErrorCodes.cs`.
- **No usage metering** exists. The billing spec of 2026-10-01 is about the subscriptions of the customers' own applications, not about charging Storyteller tenants.

### Clients

- **CLI** (`src/Platform/Cli`). The `sform story …` commands use the `Sdk.NSwag` clients, one per OpenAPI tag. Templates are the closest model (`Commands/Templates/`).
- **TypeScript SDK** (`Storyteller/sdk.typescript`). Generated by Orval from `open.api.v0.8.91.json`, one file per tag.
- **Admin UI** (`src/Platform/ui.admin`):
  - The view section of the navigation (`app/utils/navigation.ts`) has Annotations, Configurations, Graph, Templates, Schemas and Compare views. The project section has Views, Members, Invitations, Machines, Certificates and Settings.
  - `VersionSource` (`components/versions/version-source.ts`) drives `VersionTimeline` and `VersionCompare`. `VersionTimeline` already marks entries by `ExpirationTime`.
  - The capability `secrets` means `ContributorWithSecrets` (`composables/useRole.ts`). Resolved configurations are never cached (`gcTime: 0`).

## Proposed Changes

### Overview

```text
                              IKeyValueService  (Backend.Core)
                                       │
                         CosmosKeyValueService  (Backend.CosmosDb)
              ┌────────────────────────┴─────────────────────────┐
  Cosmos DB, partition {project}.kv.{view}              IValueProtector  (Backend.Core)
  - one catalog item per key                            - AES-256-GCM per version, data key per version
  - every version, public or encrypted                  - plaintext cache
  - deleted keys and old versions expire by TTL         │
                                                        IKeyEncryptionProvider, one per level
                                                        (new: KeyValues.Azure.KeyVault)
                                                        - Standard: software key per project in a Standard vault
                                                        - HSM: HSM key per project in Key Vault Premium or Managed HSM
                                                        - BYOK: the customer's key, cross-tenant
```

- **Protection levels.** Every key has one:

  | Level | Where the value is | Who can read it | Server-side features |
  | --- | --- | --- | --- |
  | `Public` | Plaintext in Cosmos | Every reader of the project | All |
  | `Standard` | Encrypted in Cosmos; data key wrapped by a software key of the project in a Standard vault | Secret rights | All |
  | `Hsm` | Encrypted in Cosmos; data key wrapped by an HSM key of the project (Key Vault Premium, or a dedicated Managed HSM) | Secret rights | All, plus key rotation and an audit trail |
  | `Byok` | Encrypted in Cosmos; data key wrapped by the customer's key in the customer's vault | Secret rights, while the customer allows it | All, plus customer revocation and the customer's own audit log |
  | `Hyok` | A JWE blob, encrypted by the client for the project's recipients | Only the holders of a recipient private key | Storage, versions, listing, binding of the blob. No preview, diff or validation of the content |

  **Reference only** is a `Public` pair with a well-known content type whose value points to a secret in the customer's own vault (section 15). The platform never resolves it.
- **Catalog.** Every key has one catalog item in Cosmos. Listing, filtering, existence, optimistic concurrency and the deleted state all come from it. The UI gets one list (R3), and the vault is never enumerated.
- **Versions** are separate items next to the catalog item, with a 1-year TTL (R11). Public and encrypted versions are stored the same way. Only the value field differs.
- **Key Vault** holds the key-encryption keys (KEKs). Their native versioning is used for rotation (R4).
- **Policy.** A per-project `KeyValuePolicy` lists the allowed levels, the HSM and BYOK settings, the HYOK recipients, and the mTLS rule (R15, R17).
- **Binding.** The native store is the `default` binding source and is also registered as `public` and `secret` (R12).
- **Usage** is metered per project and per day (R18).

| Phase | Content | Review |
| --- | --- | --- |
| **1a** | Storage and data model, container TTL, the `Standard` level with envelope encryption, delete, restore, permanent delete and purge, import and export, binding sources, local emulator | `reviews/YYYY-MM-DD Phase 1a of Native Key-Value Storage.md` |
| **1b** | Project policy, `Hsm`, `Byok`, `Hyok`, references, protection changes, key rotation and re-wrap, transport security with the mTLS rule and a durable certificate store, audit trail, usage metering | `reviews/YYYY-MM-DD Phase 1b of Native Key-Value Storage.md` |
| **2** | API for all of it, authorization, machine scopes, OpenAPI, regenerated C# and TypeScript SDKs, client-side encryption helpers | `reviews/YYYY-MM-DD Phase 2 of Native Key-Value Storage.md` |
| **3** | CLI: `sform story kv …` | `reviews/YYYY-MM-DD Phase 3 of Native Key-Value Storage.md` |
| **4** | Admin UI: the Key values section, the project's secrets settings, audit and usage pages | `docs/Platform/ui.admin/specs/reviews/YYYY-MM-DD Phase 4 of Native Key-Value Storage.md` |

---

## Phase 1a — Storage, Standard encryption, lifecycle and binding

### 1. Identity, keys, values and protection

A pair is identified by **organization + project + view + key** (Q1: per view, with the same isolation as configurations, schemas and templates).

- **Key.**
  - It is a binding path: segments of `[A-Za-z_][A-Za-z0-9_]*`, joined by `.`, at most 128 characters in total. Every key can then be written as `@db.password` in a configuration (R12). Hyphens and segments that start with a digit are not allowed.
  - Keys keep their casing but are unique case-insensitively. The id uses the key lower-cased with `ToLowerInvariant`. `Key` keeps the casing of the first write. A binding `@DB.Password` finds `db.password`.
  - No key is reserved. Collection actions use their own routes (section 23).
  - Validation lives in a new `KeyValueRules` class in `Backend.Core`, next to `NameRules`. A failure throws `InvalidInputException` with `ErrorCodes.InvalidKey`.
- **Value.**
  - It is a string (Q4) of at most **24,576 bytes** in UTF-8 for `Public`, `Standard`, `Hsm` and `Byok`. A `Hyok` value is a JWE document of at most 49,152 characters, to leave room for the encoding (section 14).
  - The empty string is allowed. `null` is not.
  - A larger value fails with `InvalidInputException` and `ErrorCodes.ValueTooLarge`.
- **ContentType.** Optional, at most 255 characters, for example `text/plain` or `application/json`. Clients use it to render the value. Two content types mark references (section 15). Otherwise the value is not validated against it.
- **Protection.** `KeyValueProtection` is `Public`, `Standard`, `Hsm`, `Byok` or `Hyok`. A pair is a secret when its protection is not `Public`.
  - The level is chosen per key on create, from the levels the project policy allows (section 11). In Phase 1a only `Public` and `Standard` exist.
  - Each version records its own protection, so a key can change level (section 16). The current version decides how the key behaves.

### 2. Cosmos data model

#### 2.1 Partition

Pairs are stored in the partition `{project}.kv.{view}` of the organization container. Add `PartitionKeys.GetKeyValues(project, view)` and `GetCosmosKeyValues(project, view)`.

- **Normal use stays inside one logical partition.** Listing a view, reading or writing a key, its versions, deleting, restoring and purging keys, and importing into a view all touch one partition. Point reads and single-partition queries are the cheapest Cosmos operations. A transactional batch (atomic, up to 100 operations) only works within one partition, and that is what makes an import of up to 50 keys per batch atomic (section 7).
- **Per view rather than per project.** Views are isolated anyway. A partition per view spreads storage (20 GB per logical partition) and throughput (a logical partition lives on one physical partition) across views, and a later "delete view" can delete the whole partition.
- **No collision** with annotation partitions, which always contain `.rst.` or `.sbt.` after the project.

#### 2.2 Items

| Item | id | Type | `ttl` |
| --- | --- | --- | --- |
| Catalog item | `kv.{key}` | `KeyValueEntity` | None while active; `DeletedRetention` (90 days) while deleted |
| Version, history | `kvv.{key}.{version}` | `KeyValueVersionEntity` | `VersionRetention` (365 days) after the version was created |

`EntityIdPrefixTypes` gets `KeyValue = "kv"` and `KeyValueVersion = "kvv"`. `{key}` is the lower-cased key. The longest id is `kvv.` + 128 + `.` + 20 digits = 153 characters, below the 255 limit.

```csharp
// Entities/KeyValues/KeyValueEntity.cs
// Standalone like ViewEntity, because a pair has no annotation key. It carries ViewName,
// so view discovery finds a view that holds only pairs.
public record class KeyValueEntity
{
    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }                // kv.{lower-cased key}

    [JsonProperty("_ts")]
    [JsonPropertyName("_ts")]
    public uint LastUpdatedEpochTimestamp { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int? TimeToLiveInSeconds { get; init; }          // set only while deleted

    public required string ProjectName { get; init; }

    public required string ViewName { get; init; }

    public required string Key { get; init; }               // casing of the first write

    public required ulong Version { get; init; }            // current version, starts at 1

    public required StoredValue Content { get; init; }      // the current version's value, section 2.3

    public required string Author { get; init; }            // author of Version

    public required DateTimeOffset UpdatedAt { get; init; } // creation time of Version

    public required string CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }         // set = deleted (soft)

    public string? DeletedBy { get; init; }
}

// Entities/KeyValues/KeyValueVersionEntity.cs. Immutable, except a re-wrap of its data key (section 17).
public record class KeyValueVersionEntity
{
    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }                // kvv.{lower-cased key}.{version}

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public required int TimeToLiveInSeconds { get; init; }  // the rest of VersionRetention, see 2.4

    public required string ProjectName { get; init; }

    public required string ViewName { get; init; }

    public required string Key { get; init; }

    public required ulong Version { get; init; }

    public required StoredValue Content { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset CreationTime { get; init; }
}
```

- **No state item**, unlike templates. A create after a delete takes `max(version) + 1` over the existing history, as configurations do (section 2.4).
- **Active keys keep their current version.** The catalog item has no `ttl` while the key is active. Older versions expire one year after they were written.

#### 2.3 `StoredValue`

The same shape is used in the catalog item and in version items:

```csharp
public record class StoredValue
{
    public required KeyValueProtection Protection { get; init; }

    public string? ContentType { get; init; }

    public string? Value { get; init; }                     // Public: the value. Hyok: the JWE document

    public EncryptedValue? Encrypted { get; init; }         // Standard, Hsm, Byok

    public IReadOnlyList<string>? Recipients { get; init; } // Hyok: the recipient ids found in the JWE, for display

    public int Length { get; init; }                        // plaintext length in bytes (Public, Standard, Hsm, Byok), JWE length (Hyok); for metering and display
}

public record class EncryptedValue
{
    public int Format { get; init; } = 1;

    public required string KeyId { get; init; }             // KEK identifier with its version, e.g. https://v.vault.azure.net/keys/kek-…/3f2…

    public required string Algorithm { get; init; }         // wrap algorithm, RSA-OAEP-256

    public required string WrappedKey { get; init; }        // base64url of the wrapped 256-bit data key

    public required string Nonce { get; init; }             // base64url, 96 bits

    public required string Ciphertext { get; init; }        // base64url, AES-256-GCM ciphertext followed by the 128-bit tag
}
```

The envelope encryption itself is section 5.

#### 2.4 Lifecycle and cost

`VersionRetention` (default 365 days) and `DeletedRetention` (default 90 days) are options of `KeyValueOptions` (section `KeyValues`). "Remaining lifetime" of a version means `CreationTime + VersionRetention − now`.

| Operation | Cosmos work |
| --- | --- |
| Get a key | Point read of `kv.{key}` (about 1 RU). A deleted key is not found unless asked for |
| Create | `Version = max(kvv.{key}.*) + 1`, or 1 without history (a single-partition `MAX` query, only when the catalog item does not exist). Then `CreateItem`. A `409` from a concurrent create retries as an update |
| Update | One batch: create `kvv.{key}.{Version}` from the current item with `ttl` = its remaining lifetime, then replace the catalog item with `IfMatchEtag` and `Version + 1`. The same value, content type and protection means no write |
| **Delete (soft)** | One batch: create `kvv.{key}.{Version}` for the current version with `ttl` = its remaining lifetime (skipped when nothing remains), then replace the catalog item with `DeletedAt`, `DeletedBy` and `ttl` = `DeletedRetention`. The catalog item keeps its content, so a restore needs nothing else |
| **Restore** | One batch: replace the catalog item with `DeletedAt`, `DeletedBy` and `ttl` cleared, and delete `kvv.{key}.{Version}` when it exists. **No new version**: the key is back at its latest version (R10) |
| **Write to a deleted key** | The same as an update, without the history item, which the delete already wrote. The key is active again, and its history is still there (R9) |
| **Create after the deleted key expired** | The catalog item is gone, but the versions younger than one year are not, so the create continues after them (R9) |
| **Permanent delete** (active key) | Delete every `kvv.{key}.*` item, then the catalog item, in batches of at most 100 operations, the catalog item last (R13) |
| **Purge** (deleted key) | The same as a permanent delete, for keys that are deleted (R13). After a purge or a permanent delete nothing is left, and a new create starts at version 1 |
| Versions | `SELECT c.Version, c.Author, c.CreationTime, c.Content.Protection, c.ttl, c._ts FROM c WHERE STARTSWITH(c.id, 'kvv.{key}.')` in one partition, plus the catalog item when the key is active. For a deleted key, the current version comes from its history item, or from the catalog item when the version had no lifetime left |
| Version content | Point read of `kvv.{key}.{n}`, or the catalog item when its `Version` is `n` |

The listing query:
- returns a projection without `Content.Value` and `Content.Encrypted` unless `includeValues` is set; values of secrets are never listed;
- filters by `state`: `Active` adds `NOT IS_DEFINED(c.DeletedAt)` (the default), `Deleted` adds `IS_DEFINED(c.DeletedAt)`, `All` adds nothing;
- filters by `protection` (one or more levels) and by `keyPrefix`, which becomes `STARTSWITH(c.id, 'kv.' + lower(prefix))`;
- sorts with `ORDER BY c.id` and pages by `CosmosConstants.MaxItemCountPerPage` with a continuation token.

`ExpirationTime` of a version is `CreationTime + VersionRetention` for history, none for the current version of an active key, and `DeletedAt + DeletedRetention` for the current version of a deleted key that has no history item.

**Concurrency.**
- Every replace uses the ETag of the item that was read, with up to 3 retries on `409` or `412`, as templates do.
- A caller can pass `ExpectedVersion` (section 6). A mismatch returns `412 KeyValueVersionMismatch` and is not retried.
- Permanent delete and purge delete the catalog item last. A failure leaves the key in place, partly without history, and the same request finishes the job when it is sent again.

**Cost.** An update or a delete is one create (about 6 RU for 1 KB) and one replace (about 10 RU) in one batch. A restore is one replace and one delete. Public and encrypted values cost the same in Cosmos. An encrypted 1 KB value is about 1.4 KB stored.

#### 2.5 Why versions are separate items

R5 asks whether versions can be part of the same document. They can, since an item may be up to 2 MB. Embedding makes the frequent operations more expensive, though, and it cannot work with a per-version TTL:

| | Versions embedded (an array in the catalog item) | Versions as separate items (chosen) |
| --- | --- | --- |
| Read the current value (frequent, and on every `@key` binding) | Reads the whole history. Point-read cost grows with size: about 1 RU for 1 KB, about 10 RU for 100 KB | About 1 RU, whatever the history |
| List a view (frequent) | Query cost grows with the size of the loaded documents, so every list pays for every history | Only catalog items are loaded |
| Write a new version | Replaces and re-indexes the whole document, so the cost grows with every version | Constant: one create and one replace in one atomic batch |
| Expire old versions after one year | Needs a job that rewrites documents. Cosmos TTL works per item | Cosmos TTL per version item, no job |
| Size limit | 2 MB per item: about 80 versions of a 24 KB value | None in practice (20 GB per logical partition) |
| Read the history (rare, R6) | Comes with every read | One single-partition prefix query, only when asked |

The RU figures are Microsoft's published orders of magnitude for default indexing. The integration tests log `RequestCharge` for each operation, and the Phase 1a review records the measured numbers.

### 3. Container TTL (required by R11)

Per-item `ttl` works only when the container has TTL enabled.

- `ContainerFactory.CreateContainerIfNotExistsAsync` creates containers with `DefaultTimeToLive = -1`: TTL is on, and an item expires only when it has its own `ttl`.
- A new `DbCreator` command, `enable-ttl`, updates every existing `org.*` container once (`ReplaceContainerAsync` with `DefaultTimeToLive = -1`). The Aspire `db-creator` resource runs it too.
- **Side effect, intended:** configuration, schema and template history items have `ttl` = 365 days. Once TTL is on, the ones last written more than 365 days ago are deleted, as `ExpirationTime` and the docs promise. Items without `ttl` (annotations, configurations, schemas, templates, state items, views, certificates) are not affected.
- `inheritance.md`, `definitions.md` and `templating.md` get a note that the history expiry is enforced from this change on.

### 4. Indexing (recommendation, decided in Phase 1a)

The organization containers use the default indexing policy, which indexes every path.
- `Content.Value`, `Content.Encrypted` and `Content.Recipients` are never queried. Indexing up to 48 KB of base64 per version adds write RU.
- For new containers, add `/Content/Value/?`, `/Content/Encrypted/*` and `/Content/Recipients/*` to `ExcludedPaths` in `ContainerFactory`. Existing containers get the merged policy from the same `DbCreator` command as section 3.
- Only pairs have these paths. Configurations use `Content` too, but their content is an object below `/Content/*`, which these exact paths do not touch.

The change is made only if the integration tests show a measurable difference (D7).

### 5. Envelope encryption

#### 5.1 Format

For every `Standard`, `Hsm` or `Byok` version:

1. Generate a random 256-bit data key (DEK) and a random 96-bit nonce with `RandomNumberGenerator`.
2. Encrypt the UTF-8 value with `System.Security.Cryptography.AesGcm`, with a 128-bit tag and associated data (AAD) `2s.kv.v1|{organization}|{project}|{view}|{lower-cased key}|{version}|{protection}`. The AAD binds the ciphertext to its place: a ciphertext copied to another key, view, project or version fails to decrypt.
3. Wrap the DEK with the level's KEK (`RSA-OAEP-256`, through `IKeyEncryptionProvider`, section 5.2).
4. Store `EncryptedValue` (section 2.3), and zero the DEK buffer with `CryptographicOperations.ZeroMemory`.

Decryption reverses the steps. The AAD is rebuilt from the item, never read from it. A failed tag check throws `KeyValueIntegrityException` (500, logged as an error with the key but never with data).

Each version has its own DEK, so one wrap per write and one unwrap per read of an uncached version.

#### 5.2 Abstractions (`Backend.Core/src/KeyValues/Protection/`)

```csharp
// Wraps and unwraps data keys for one level. Implemented in KeyValues.Azure.KeyVault.
public interface IKeyEncryptionProvider
{
    KeyValueProtection Protection { get; }                  // Standard, Hsm or Byok

    // Creates the project's KEK for Standard and Hsm when missing; checks the customer key for Byok. Returns the versionless key id.
    Task<string> EnsureKeyAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default);

    Task<WrappedDataKey> WrapAsync(KeyValueScope scope, KeyValuePolicy policy, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default);

    Task<byte[]> UnwrapAsync(KeyValueScope scope, KeyValuePolicy policy, EncryptedValue value, CancellationToken cancellationToken = default);

    Task<string> GetCurrentKeyVersionAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default);

    Task RotateAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default);   // Standard and Hsm only
}

public sealed record KeyValueScope(string Organization, string Project);

public sealed record WrappedDataKey(string KeyId, string Algorithm, byte[] Value);

// Encrypts and decrypts values; owns the plaintext cache. Implemented in Backend.Core (EnvelopeValueProtector).
public interface IValueProtector
{
    Task<StoredValue> ProtectAsync(KeyValueIdentity identity, KeyValueProtection protection, string value, string? contentType, CancellationToken cancellationToken = default);

    Task<string> UnprotectAsync(KeyValueIdentity identity, StoredValue value, CancellationToken cancellationToken = default);
}

public sealed record KeyValueIdentity(string Organization, string Project, string View, string Key, ulong Version);
```

- `EnvelopeValueProtector` picks the provider by `Protection`. A level without a configured provider throws `ProtectionNotConfiguredException` (501, `ErrorCodes.ProtectionNotConfigured`). `Public` passes the value through. `Hyok` is validated and stored as is (section 14). `UnprotectAsync` on `Hyok` returns the JWE document.
- **Plaintext cache.** `(organization, project, view, key, version) → value` in `IMemoryCache` with a size limit. Versions are immutable, so the cache can never serve a stale value: a new version has a new cache key. `CacheDuration` per level: Standard 10 minutes, Hsm 5 minutes, Byok 5 minutes. The BYOK duration is the longest time a revoked customer key keeps working (section 13). `00:00:00` turns the cache off.

#### 5.3 The `Standard` level

- One RSA-2048 software key per project, `kek-{guid:N}`, in the Standard vault named by `KeyValues:Protection:Standard:VaultName` (a key of `AzureKeyVaults`). It is created on the first `Standard` write of the project, and its versionless id is stored in the project policy (section 11).
- A rotation policy rotates it every `RotationInterval` (default 365 days, Key Vault charges about $1 per scheduled rotation). Rotation and re-wrap are section 17.
- One key per project isolates tenants cryptographically and lets a project be crypto-shredded later. Software keys cost only their operations ($0.03 per 10,000), so a key per project costs nothing extra.

#### 5.4 New project `Storyteller/KeyValues.Azure.KeyVault`

- **Project.**
  - Assembly `42.Platform.Storyteller.KeyValues.Azure.KeyVault`, root namespace `_42.Platform.Storyteller.KeyValues`.
  - References `Backend.Core` and `Libraries/Utils/Async`.
  - Packages, all already in `Directory.Packages.props`: `Azure.Security.KeyVault.Keys`, `Azure.Identity`, `Microsoft.Extensions.Azure`, and the options packages.
- **Providers.** `KeyVaultStandardKeyProvider` here in Phase 1a. `KeyVaultHsmKeyProvider` and `CustomerKeyProvider` follow in Phase 1b. All of them use `KeyClient` to create and rotate keys and `CryptographyClient` to wrap and unwrap.
- **Registration.** `KeyValueProtectionEntryPoint.AddKeyVaultKeyProtection(this IServiceCollection, IConfiguration, IHostEnvironment?)` registers the clients, with the same credential choice as `AddAzureKeyVaultBindings`, and the providers. `Program.cs` calls it after `AddCosmosDbAnnotations`.
- **Throttling.** Software RSA-2048 keys allow 4,000 operations per 10 seconds per vault. The SDK retry policy honours `429` with `Retry-After`. `MaxConcurrentKeyOperations` (default 8) caps parallel operations per instance. A `429` or `5xx` that survives the retries becomes `KeyEncryptionUnavailableException`: `503` with `Retry-After`, `ErrorCodes.KeyEncryptionUnavailable`.
- **Role.** The Storyteller identity needs **Key Vault Crypto Officer** on its own vaults, to create keys and set their rotation policies, and **Key Vault Crypto Service Encryption User** to wrap and unwrap.
- **Tests.** New test project `KeyValues.Azure.KeyVault/testint` (`42.Platform.Storyteller.KeyValues.Azure.KeyVault.IntegrationTests`) against Lowkey Vault (section 5.5).

#### 5.5 Local development and tests

Microsoft ships no Key Vault emulator. Use **Lowkey Vault** (`nagyesta/lowkey-vault`), a test double of the Key Vault REST API.

- **Aspire.**
  - `builder.AddContainer("keyvault", "nagyesta/lowkey-vault", "<tag>")` with its HTTPS endpoint.
  - The endpoint is passed to the API as `AzureKeyVaults__default`, together with `KeyValues__Protection__Emulator=true`.
- **Emulator mode.** The clients use a static dummy token credential, `DisableChallengeResourceVerification = true`, and a transport that accepts the emulator's self-signed certificate. `Emulator` fails the startup in the Production environment.
- **Tests** use `Testcontainers.LowkeyVault`, added to `Directory.Packages.props` with the same version as `Testcontainers`. At implementation time, check that the module exists for that version, which image tag it pins, and that the emulator supports RSA key creation, `wrapKey` and `unwrapKey` with `RSA-OAEP-256`, and key rotation. Any operation it lacks is covered by unit tests with a mocked `CryptographyClient`.
- **Cosmos service tests** use `FakeKeyEncryptionProvider` (AES key wrap in memory) from the test project, so they need no second container.
- **TTL in tests.** The Cosmos emulator honours TTL. The tests use retentions of a few seconds.

### 6. Service and models

#### 6.1 Models (`Abstractions.Annotations/src/Model/KeyValues/`)

```csharp
[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueProtection
{
    Public = 0,
    Standard = 1,
    Hsm = 2,
    Byok = 3,
    Hyok = 4,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueState
{
    Active = 0,
    Deleted,
    All,
}

public record class KeyValue
{
    public required string Key { get; init; }

    public required KeyValueProtection Protection { get; init; }

    public bool IsSecret => Protection != KeyValueProtection.Public;

    public string? Value { get; init; }             // public always; a secret only when revealed; Hyok: the JWE document

    public string? ContentType { get; init; }

    public bool IsReference { get; init; }          // section 15

    public IReadOnlyList<string>? Recipients { get; init; }   // Hyok

    public required ulong Version { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required string CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }

    public string? DeletedBy { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; } // deleted: DeletedAt + DeletedRetention
}

// A listing row. Value only for public pairs, and only with includeValues.
public record class KeyValueSummary
{
    public required string Key { get; init; }

    public required KeyValueProtection Protection { get; init; }

    public string? Value { get; init; }

    public string? ContentType { get; init; }

    public bool IsReference { get; init; }

    public required ulong Version { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsDeleted { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}

public record class KeyValuesResponse
{
    public required IReadOnlyList<KeyValueSummary> Items { get; init; }

    public string? ContinuationToken { get; init; }

    public int Count { get; init; }
}

public record class KeyValueWrite
{
    public KeyValueProtection? Protection { get; init; }  // required on create, so a secret never becomes public by omission; update: unchanged when missing

    public required string Value { get; init; }

    public string? ContentType { get; init; }

    public ulong? ExpectedVersion { get; init; }          // null: no check; 0: the key must not exist or must be deleted
}

public record class KeyValueVersion
{
    public required ulong Version { get; init; }

    public required KeyValueProtection Protection { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset CreationTime { get; init; }

    public DateTimeOffset? ExpirationTime { get; init; }

    public bool IsCurrent { get; init; }
}

// Bulk operations: restore, purge, protect.
public record class KeyValueBulkRequest
{
    public IReadOnlyList<string>? Keys { get; init; }     // 1 to 100 keys, or

    public string? KeyPrefix { get; init; }               // every matching key, at most 1,000

    public KeyValueProtection? Protection { get; init; }  // protect only: the target level
}

[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueBulkOutcome
{
    Done = 0,
    Unchanged,          // already in the requested state
    NotFound,           // never existed, already expired or purged, or not deleted (purge)
    Forbidden,          // a secret, and the caller has no secret rights
    NotAllowed,         // the target level is not allowed, or cannot be reached server-side
    Failed,
}

public record class KeyValueBulkItemResult
{
    public required string Key { get; init; }

    public required KeyValueBulkOutcome Outcome { get; init; }

    public ulong? Version { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}

public record class KeyValueBulkResult
{
    public required IReadOnlyList<KeyValueBulkItemResult> Items { get; init; }
}
```

The import and export models are in section 7. The policy, audit and usage models are in Phase 1b.

#### 6.2 `IKeyValueService` (`Backend.Core/src/KeyValues/IKeyValueService.cs`)

The API checks the base role. Only the service knows the protection of a key, so the caller's secret rights travel with the call and the service enforces them:

```csharp
// The author of a write, and what the caller may do with secrets (decided by the API, section 24).
public sealed record KeyValueCaller(string Author, bool CanReadSecrets, bool CanWriteSecrets, KeyValueChannel Channel);

[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueChannel
{
    User = 0,
    MachineMtls,
    MachineToken,
}

public interface IKeyValueService
{
    Task<KeyValuesResponse> ListAsync(string organization, string project, string view, KeyValueListRequest request);

    Task<KeyValue?> GetAsync(string organization, string project, string view, string key, KeyValueReadOptions options, KeyValueCaller caller);

    Task<KeyValue> SetAsync(string organization, string project, string view, string key, KeyValueWrite write, KeyValueCaller caller);

    Task<bool> DeleteAsync(string organization, string project, string view, string key, KeyValueDeleteOptions options, KeyValueCaller caller);

    Task<KeyValueBulkResult> RestoreAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller);

    Task<KeyValueBulkResult> PurgeAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller);

    Task<KeyValueBulkResult> ProtectAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller);

    Task<IReadOnlyList<KeyValueVersion>> GetVersionsAsync(string organization, string project, string view, string key);

    Task<KeyValue?> GetVersionAsync(string organization, string project, string view, string key, ulong version, bool reveal, KeyValueCaller caller);

    Task<DiffResult> GetVersionChangesAsync(string organization, string project, string view, string key, ulong fromVersion, ulong toVersion, KeyValueCaller caller);

    Task<KeyValueDocument> ExportAsync(string organization, string project, string view, KeyValueExportRequest request, KeyValueCaller caller);

    Task<KeyValueImportResult> ImportAsync(string organization, string project, string view, KeyValueDocument document, KeyValueImportOptions options, KeyValueCaller caller);

    // For the binding sources: the current value, or null when missing, deleted, or a secret without includeSecrets.
    Task<KeyValueLookup?> LookupAsync(string organization, string project, string view, string key, bool includeSecrets);
}
```

- **Request records.**
  - `KeyValueListRequest` is `{ KeyPrefix?, Protections?, State = Active, IncludeValues, ContinuationToken? }`.
  - `KeyValueReadOptions` is `{ Reveal, IncludeDeleted }`.
  - `KeyValueDeleteOptions` is `{ Permanent, ExpectedVersion? }`.
  - `KeyValueExportRequest` is `{ KeyPrefix?, Protections?, IncludeSecrets }`.
- **Secret rules in the service.**
  - A reveal, a secret version, a secret diff, or an export with secrets needs `CanReadSecrets`. Without it, the service throws `AccessDeniedException`.
  - A write, delete, restore, purge or protect of a secret needs `CanWriteSecrets`. A write that turns a public key into a secret, or a secret into a public key, needs it too. In a bulk request, such an item gets `Forbidden`.
  - Permanent delete and purge need the Administrator role, which the API checks (section 24).
- **Policy check.** A write's target level must be allowed by the project policy (section 11). Otherwise it fails with `409 ProtectionNotAllowed`. In Phase 1a, before the policy exists, `Public` and `Standard` are allowed.
- **Diffs.**
  - `GetVersionChangesAsync` diffs the two plaintexts. `JsonContentDiffer` gets a `GetTextChanges(string from, string to)` method with the existing DiffPlex code, and the JSON overload serializes and calls it.
  - Version 0 is the empty string.
  - A `Hyok` version cannot be diffed: `400 NotSupportedForProtection`.
- **Bulk operations** handle at most 100 listed keys, or at most 1,000 keys by prefix (more gives `400` with "narrow the prefix"). Each key gets its own outcome, and one failing key does not stop the others.
- **Implementation:** `Backend.CosmosDb/src/KeyValues/CosmosKeyValueService.cs`. Mappings go in `Backend.CosmosDb/src/KeyValueMappings.cs`.
- **Registration in `AddCosmosDbAnnotations`:**
  - `AddSingleton<IKeyValueService, CosmosKeyValueService>()`;
  - `AddSingleton<IValueProtector, EnvelopeValueProtector>()`;
  - `Configure<KeyValueOptions>` for the section `KeyValues`.

#### 6.3 Exceptions and error codes

| Exception (`Backend.Core/src/KeyValues/`) | Base and status | `ErrorCodes` |
| --- | --- | --- |
| `KeyValueNotFoundException` | `NotFoundException` → 404 | `KeyValueNotFound` |
| `ProtectionNotAllowedException` | `ConflictException` → 409 | `ProtectionNotAllowed` |
| `ProtectionInUseException` (policy change, section 11) | `ConflictException` → 409 | `ProtectionInUse` |
| `KeyValueVersionMismatchException` | new `PreconditionFailedException` → 412 | `KeyValueVersionMismatch` |
| `ProtectionNotConfiguredException` | new `FeatureNotConfiguredException` → 501 | `ProtectionNotConfigured` |
| `KeyEncryptionUnavailableException` | new `ServiceUnavailableException` → 503 with `Retry-After` | `KeyEncryptionUnavailable` |
| `CustomerKeyUnavailableException` (section 13) | new `FailedDependencyException` → 424 | `CustomerKeyUnavailable` |
| `MtlsRequiredException` (section 18) | `AccessDeniedException` → 403 | `MtlsRequired` |
| `KeyValueIntegrityException` | `StorytellerException` → 500 | `KeyValueIntegrity` |
| Invalid key, value, import, client-encrypted value or reference | `InvalidInputException` → 400 | `InvalidKey`, `ValueTooLarge`, `ImportInvalid`, `InvalidClientEncryptedValue`, `UnknownRecipient`, `InvalidSecretReference`, `NotSupportedForProtection`, `ReprotectNotPossible` |

`ErrorResponseMapping` gets the 412, 424, 501 and 503 cases. `ExceptionHandlingMiddleware` writes `Retry-After` for 503.

### 7. Import and export

#### 7.1 Document format

```json
{
  "Format": "2s.key-values.v1",
  "Items": [
    { "Key": "db.host", "Protection": "Public", "Value": "sql.internal", "ContentType": "text/plain" },
    { "Key": "db.password", "Protection": "Hsm", "Value": "…" },
    { "Key": "api.token", "Protection": "Standard" },
    { "Key": "partner.key", "Protection": "Hyok", "Value": "{\"protected\":\"…\",\"recipients\":[…],\"iv\":\"…\",\"ciphertext\":\"…\",\"tag\":\"…\"}" }
  ]
}
```

```csharp
public record class KeyValueDocument
{
    public string Format { get; init; } = "2s.key-values.v1";

    public required IReadOnlyList<KeyValueDocumentItem> Items { get; init; }
}

public record class KeyValueDocumentItem
{
    public required string Key { get; init; }

    public required KeyValueProtection Protection { get; init; }

    public string? Value { get; init; }         // omitted for a secret exported without values

    public string? ContentType { get; init; }
}
```

- The API reads and writes only this JSON document. The CLI and the UI convert `.env` and flat JSON on the client (Phases 3 and 4).
- A secret exported without its value is `{ Key, Protection }`. Importing such an item leaves an existing secret unchanged, and fails with `SecretValueMissing` for a key that does not exist.
- **`Hyok` values travel as their JWE documents.** Their AAD names the project, view and key (section 14), so a `Hyok` item imports only into the same place. For another view, it has to be encrypted again by a client.

#### 7.2 Export

`ExportAsync` pages through the active keys with `KeyPrefix` and `Protections`. With `IncludeSecrets`, it decrypts every secret's current version, with at most `MaxConcurrentKeyOperations` parallel unwraps (cached), and includes `Hyok` documents as they are. Items are ordered by key.

#### 7.3 Import

`KeyValueImportOptions` is `{ Mode = Merge | Replace, DryRun, KeyPrefix? }`.

1. **Validate everything first.** Any error returns `400 ImportInvalid` with a list of the failing items, and nothing is written. The checks are:
   - at most **1,000 items**;
   - the key rules, the value size, `Hyok` and reference formats, and duplicate keys (case-insensitive);
   - every item's level is allowed by the policy (`ProtectionNotAllowed`);
   - with `Replace` and `KeyPrefix`, every item key starts with the prefix;
   - without `CanWriteSecrets`, no secret item with a value, no level change to or from secret, and no secret that `Replace` would delete. A violation throws `AccessDeniedException` (403) for the whole import.
2. **Plan** against the current state: one catalog listing with `State = All`, plus decryption of the current secrets to find unchanged values (cached; `Hyok` documents are compared as text). Each item gets one action:
   - `Create`, also for a deleted key, whose history continues;
   - `Update`, including a change of level;
   - `Unchanged`;
   - `Delete`, only in `Replace`: active keys under `KeyPrefix`, or all active keys of the view, that the document does not list. They are soft-deleted and can be restored.
3. **Dry run.** With `DryRun`, return the plan.
4. **Execute.**
   - Values are encrypted first, with at most `MaxConcurrentKeyOperations` parallel wraps.
   - The writes go in transactional batches of at most 50 keys (100 operations: history and catalog item).
   - Deletes run last.
   - A batch that fails because of a concurrent change is planned again once for its keys.
5. **Return** a `KeyValueImportResult`.

```csharp
public enum KeyValueImportAction
{
    Create = 0,
    Update,
    Unchanged,
    Delete,
}

public record class KeyValueImportItemResult
{
    public required string Key { get; init; }

    public required KeyValueProtection Protection { get; init; }

    public required KeyValueImportAction Action { get; init; }

    public ulong? Version { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}

public record class KeyValueImportResult
{
    public required bool IsDryRun { get; init; }

    public required bool IsComplete { get; init; }   // false when an item failed

    public required IReadOnlyList<KeyValueImportItemResult> Items { get; init; }
}
```

An import is not atomic across batches. It is idempotent, so running it again after a partial failure writes the rest. Keys that a `Replace` deleted by mistake come back with one restore call (R10).

### 8. The native store as binding source (R12)

#### 8.1 Sources

`Binding.Core/src/KeyValueBindingSource.cs` implements `IBindingSource` over `IKeyValueService.LookupAsync`, through a `Lazy<IKeyValueService>` (the same cycle break as `@annotation`). It is registered three times:

| Source key | Reads | A secret without secret rights |
| --- | --- | --- |
| `default` | Public pairs and secrets | Declines |
| `public` | Public pairs only. A secret declines | — |
| `secret` | Secrets only. A public pair declines | Declines |

```json
{
  "host": "@db.host",
  "password": "@db.password",
  "onlyPublic": "@(db.host, public)",
  "onlySecret": "@secret(db.password)",
  "url": "@[https://@api.host/v1]"
}
```

- **Scope.** The organization, project and view come from the `ConfigurationBindingContext` of the read, so a configuration sees only the pairs of its own view. Without that context, the sources decline.
- **Key.** `BindingRequest.PathString` is the key, looked up case-insensitively.
- **Values.**
  - `Public`: the value as a JSON string, references included (section 15).
  - `Standard`, `Hsm`, `Byok`: the decrypted value, only when `IncludeSecrets` is set.
  - `Hyok`: the JWE document as a JSON string, only when `IncludeSecrets` is set. The consuming application decrypts it (section 14).
  - `IncludeSecrets` is set exactly when the caller may receive secrets (section 18), the check that `GetConfigurationResolved` already does, extended by the mTLS rule.
- **Not found.** A missing or deleted key declines, as every other source does. A top-level statement then stays as stored, and a statement inside an interpolation or a math expression throws `BindingEvaluationException` (422).
- **Memo per read.** Each key is looked up at most once per resolved read, through a `ConditionalWeakTable<ConfigurationBindingContext, …>` that lives exactly as long as the read. It is not shared across reads, so a changed pair takes effect on the next read.
- **Key failures are not declines.** `KeyEncryptionUnavailableException` fails the read with 503 and `CustomerKeyUnavailableException` with 424, because a silently unbound secret would look like a configuration error.

#### 8.2 Changes in the binding language

- **`BindingRequest.Context`.** `BindingRequest` gets `public object? Context { get; init; }`, and `BindingEvaluator.ResolveSourceAsync` fills it from `BindingScope.Context`, as `BindingFunctionRequest` already gets it. `KeyVaultBindingSource` ignores it.
- **Source call form.** `@name(path)` is evaluated as `@(path, name)` when:
  - `name` is not a registered function;
  - `name` is a registered source;
  - the call has exactly one argument, and it is a bare path.

  The argument is not resolved first, unlike function arguments. `BindingEvaluator.InvokeFunctionAsync` checks the condition before it evaluates the arguments. It works for every source, so `@primaryVault(db.password)` equals `@(db.password, primaryVault)`.
- **Name clashes.** A function and a source with the same name fail the startup in `AddConfigurationBindings`.

#### 8.3 Registration

```csharp
services
    .AddConfigurationBindings(options => options
        .AddFunction<ConfigBindingFunction>("config")
        .AddFunction<AnnotationBindingFunction>("annotation"))
    .AddAzureKeyVaultBindings(context.Configuration, context.HostingEnvironment)
    .AddKeyValueBindings();   // new: default, public and secret, registered last
```

`AddAzureKeyVaultBindings` no longer registers a raw vault source under the names `default`, `public` and `secret`, which the native store now owns. Other vault entries stay available as raw sources, for example `@(db.password, shared)` or `@shared(db.password)`. A vault named `default` is no longer a binding source. No migration is planned (decided 2026-10-11).

### 9. Tests (Phase 1a)

`Backend.CosmosDb/test/CosmosKeyValueServiceTests.cs` runs on Testcontainers Cosmos with `FakeKeyEncryptionProvider` and retentions of a few seconds:

1. **Lifecycle, public and `Standard`.** Create, update, update with the same value (no new version), versions list with protection and `ExpirationTime`, version content and diff.
2. **Version TTL.** A history item expires after `VersionRetention`. The current version of an active key never expires.
3. **Delete and restore.**
   - A deleted key is not found by get and is listed with `State = Deleted`.
   - A bulk restore of three keys (one public, one secret, one already active) gives `Done`, `Done` and `Unchanged`, keeps the version numbers, and creates no version.
   - A write to a deleted key continues the numbering, and the old versions are still there.
   - After `DeletedRetention`, the catalog item is gone, the younger versions stay until their own TTL, and a new create continues after them.
4. **Permanent delete and purge.**
   - A permanent delete removes the catalog item and all versions; a new create starts at 1.
   - A purge of two deleted keys and one active key gives `Done`, `Done` and `NotFound`.
   - A purge interrupted after the first batch of a key with 150 versions finishes on the second call.
5. **Encryption.**
   - A ciphertext moved to another key, view or version fails with `KeyValueIntegrityException`.
   - The catalog and version items hold no plaintext of a secret (a search of the raw JSON).
   - The plaintext cache: a second read makes no unwrap.
6. **Keys and values.** Case-insensitive uniqueness with the casing kept; invalid keys (hyphen, leading digit, `..`); a value of exactly 24,576 bytes and one byte more.
7. **`ExpectedVersion`.** `0` on create and on a deleted key; a mismatch gives 412.
8. **Concurrency.** Concurrent writes produce consecutive versions, without gaps or duplicates.
9. **Secret rules** of `KeyValueCaller`: a reveal and a write without rights give 403, and a public-to-secret change needs `CanWriteSecrets`.
10. **Listing.** Prefix, protection, state, paging with a continuation token, `includeValues` (never for secrets), and view isolation.
11. **Import.**
    - Validation failures write nothing.
    - The plan and the result of a merge; replace with and without a prefix, after which the deleted keys can be restored.
    - A dry run writes nothing.
    - 120 items (three batches).
    - A secret without a value keeps the existing secret.
    - A second run of the same import changes nothing.
12. **Export.** Secrets without values by default and with values on request, and an export imported into another view.
13. **View discovery** finds a view that holds only pairs.
14. **Container TTL.** A container from `ContainerFactory` has `DefaultTimeToLive = -1`; `enable-ttl` turns TTL on for an existing container; an item without `ttl` does not expire.
15. **RU log** of get, list, write, delete, restore, purge and the versions list, recorded in the review.

`KeyValues.Azure.KeyVault/testint/KeyVaultStandardKeyProviderTests.cs` (Lowkey Vault):
1. `EnsureKeyAsync` creates one key per project, and calling it again creates none.
2. Wrap and unwrap round trip; unwrap with an older key version after a rotation.
3. A `429` becomes `KeyEncryptionUnavailableException` after the retries (mocked transport).

Binding tests:
1. The source call form: `@public(db.host)` equals `@(db.host, public)`; the argument is not pre-resolved; a function name still wins; a function and a source with the same name fail the startup.
2. `BindingRequest.Context` reaches sources.
3. `default`: a public pair binds; a secret binds with secrets and stays unbound without them; a missing key stays as stored, and inside an interpolation gives 422.
4. `public` declines secrets, and `secret` declines public pairs.
5. View isolation; the memo per read (one lookup for a key used three times); 503 when the key provider is unavailable.
6. `AddAzureKeyVaultBindings` no longer registers `default`, `public` or `secret`.

One end-to-end test runs `CosmosKeyValueService` with `KeyVaultStandardKeyProvider` against both containers.

### 10. Documentation (Phase 1a)

- New `docs/Platform/Storyteller/key-values.md`:
  - the concepts and levels, and the key and value rules;
  - the data model and the lifecycle (versions, delete, restore, permanent delete, purge, retentions);
  - the envelope encryption;
  - the Key Vault setup (roles, options, emulator);
  - the import and export format, and the RU notes.
- `docs/Platform/Storyteller/binding.md`: the native store as the `default` source, the `public` and `secret` sources, the source call form, `BindingRequest.Context`, and the vault named `default`.
- `inheritance.md`, `definitions.md`, `templating.md`: the history expiry is enforced from section 3 on.

---

## Phase 1b — Security levels, policy, delivery, audit and metering

### 11. Project policy

```csharp
// Abstractions.Annotations/src/Model/KeyValues/KeyValuePolicy.cs
public record class KeyValuePolicy
{
    public IReadOnlyList<KeyValueProtection> AllowedProtections { get; init; } = [KeyValueProtection.Public, KeyValueProtection.Standard];

    public KeyValueProtection DefaultSecretProtection { get; init; } = KeyValueProtection.Standard;

    public bool RequireMtlsForSecretDelivery { get; init; } = true;    // section 18

    public ProtectionKeyInfo? Standard { get; init; }                   // read-only, set by Storyteller

    public HsmSettings? Hsm { get; init; }

    public ByokSettings? Byok { get; init; }

    public IReadOnlyList<ClientEncryptionRecipient> Recipients { get; init; } = [];   // Hyok, section 14

    public RewrapStatus? Rewrap { get; init; }                          // read-only, section 17

    public ulong Version { get; init; }

    public string? Author { get; init; }
}

public record class ProtectionKeyInfo
{
    public required string KeyId { get; init; }              // versionless KEK id

    public required string CurrentKeyVersion { get; init; }

    public DateTimeOffset? NextRotationAt { get; init; }
}
```

- **Storage.** A new property `KeyValues` on `AccessPointEntity` of the project (`core` container, partition `access`), next to `MachineAuthentication`.
  - `IKeyValuePolicyStore` and `CosmosKeyValuePolicyStore` follow `CosmosMachineAuthenticationPolicyStore`: cached in memory, written with `IfMatchEtag`.
  - A project without a policy uses the defaults above.
- **Who.**
  - Readers can read the policy (the allowed levels and the recipients' public keys).
  - Administrators change it.
- **Enabling a level.**
  - `Standard`: nothing to set up. The KEK is created on the first write.
  - `Hsm`: Storyteller creates the project's HSM key (section 12).
  - `Byok`: needs `Byok` settings that verify (section 13).
  - `Hyok`: needs at least one enabled recipient.

  An enable that fails leaves the policy unchanged and returns the error.
- **Disabling a level** that active keys or deleted keys still use returns `409 ProtectionInUse` with the count per view. The keys have to be changed to another level first (section 16). Versions in history keep their level until they expire.
- **Policy changes are audited** (section 19).

### 12. The `Hsm` level

- **Default backend: Key Vault Premium, shared.** One RSA-HSM 2048 key per project, `kek-{guid:N}`, in the Premium vault named by `KeyValues:Protection:Hsm:VaultName`.
  - The HSMs are FIPS 140-3 Level 3 validated (Microsoft, June 2025).
  - RSA-2048 because HSM RSA-3072 allows only 500 operations per 10 seconds per vault, against 2,000 for RSA-2048. `KeySize` can be raised per deployment.
  - Cost: about $1 per key version used in the last 30 days, plus operations.
- **Dedicated backend: Managed HSM** (single-tenant, FIPS 140-3 Level 3) for organizations that pay for it.
  - The deployment maps the organization to its pool: `KeyValues:Protection:Hsm:OrganizationManagedHsms: { "acme": "https://acme.managedhsm.azure.net/" }`.
  - The pool is provisioned by hand: creation, the security domain with a quorum of 3 of 5 keys, and the local RBAC roles `Managed HSM Crypto Officer` and `Managed HSM Crypto User` for the Storyteller identity. `key-values.md` has the runbook.
  - Cost: about $3.20 per hour per pool, roughly $2,340 per month.
- **`HsmSettings`**: `{ Backend (read-only: KeyVaultPremium | ManagedHsm), Key (ProtectionKeyInfo, read-only), RotationInterval (default 365 days) }`.
- **Rotation** through the Key Vault rotation policy, or on demand (section 17).
- **Audit:**
  - the Storyteller audit trail (section 19);
  - Key Vault diagnostic logs (`AuditEvent`) sent to Log Analytics. Every Key Vault call carries the Storyteller correlation id as `x-ms-client-request-id`, so a log entry can be matched with an audit event.
- **`KeyVaultHsmKeyProvider`** (`KeyValues.Azure.KeyVault`) uses `KeyClient` against the vault or the Managed HSM URI, with `HardwareProtected = true` on create.

### 13. The `Byok` level (customer-managed key)

The customer's KEK lives in the customer's Key Vault, in the customer's Entra tenant. Storyteller wraps and unwraps through a cross-tenant identity. It decrypts on the server, so the values are visible at runtime and in the admin UI. The customer gets control: revoking the key crypto-shreds the data on the Storyteller side, and every unwrap shows in the customer's own vault logs.

- **Cross-tenant identity.** Microsoft's documented pattern for cross-tenant customer-managed keys:
  - a **multi-tenant Entra app registration** in the Storyteller tenant (`KeyValues:Protection:Byok:ClientId`);
  - with a **federated identity credential** whose subject is the Functions' **user-assigned managed identity** (`ManagedIdentityClientId`);
  - Storyteller gets a token for the customer tenant with `ClientAssertionCredential(customerTenantId, clientId, assertion)`, where the assertion is the managed identity's token for `api://AzureADTokenExchange`. No client secret exists.
  - Credentials are cached per customer tenant.
- **Setup:**
  1. The admin UI shows the app id and the admin-consent URL `https://login.microsoftonline.com/{tenant}/adminconsent?client_id={clientId}`. The customer's administrator consents, which creates the app's service principal in the customer tenant.
  2. The customer grants that service principal **Key Vault Crypto Service Encryption User** (get, wrapKey, unwrapKey) on the key. Purge protection on the customer vault is recommended.
  3. A Storyteller Administrator sets `Byok = { KeyUri, TenantId }` in the policy.
  4. Verify (`POST …/policy/byok/verify`, also part of the policy update) checks four things:
     - the key is enabled;
     - it is RSA 2048 or larger, software or HSM;
     - it allows `wrapKey` and `unwrapKey`;
     - a wrap and unwrap round trip of 32 random bytes works.

     Verify stores `VerifiedAt` and `CurrentKeyVersion`.
- **`ByokSettings`** has the customer-set `KeyUri` (versionless) and `TenantId`, and these read-only fields: `VerifiedAt`, `CurrentKeyVersion`, `Health` (`Healthy`, `Unavailable` or `Unknown`), `LastCheckedAt` and `LastError`.
- **Revocation.**
  - When the customer disables or deletes the key, or removes the role, unwrap and wrap fail with 401, 403 or 404.
  - Storyteller then throws `CustomerKeyUnavailableException` (424): reads of uncached versions, writes, bindings and exports of `Byok` keys fail.
  - It sets `Health = Unavailable`, writes an audit event, and the admin UI shows a banner.
  - Cached plaintexts stay usable for at most `CacheDuration` (5 minutes). That is the documented revocation latency.
  - Once the key comes back, the next successful call sets `Healthy`.
- **Key rotation by the customer.**
  - A daily health check (`KeyValueProtectionTimer`, section 17) reads the current key version.
  - A new version starts a re-wrap. The customer must keep the old version enabled until the admin UI shows the re-wrap as complete.
  - `POST …/policy/rotate { Protection: Byok }` re-wraps now.
- **Billing.** Wrap and unwrap operations on the customer's key are billed by Azure to the customer, as the key owner.
- **Networking.** The customer vault can allow Storyteller in two ways. The first is a private endpoint from the Storyteller network into the customer's vault, which the customer approves. The second is a vault firewall that allows Storyteller's static egress IP (NAT gateway). `key-values.md` documents both.
- **`CustomerKeyProvider`** (`KeyValues.Azure.KeyVault`). Tests mock the cross-tenant credential, and a manual smoke test against a second tenant is recorded in the Phase 1b review.

### 14. The `Hyok` level (client-side encryption, zero knowledge)

Values are encrypted before they reach the platform, for public keys of the consuming applications. Storyteller stores and returns opaque documents and cannot read them.

- **Recipients** are registered per project in the policy (Q answered: dedicated encryption keys per project):

  ```csharp
  public record class ClientEncryptionRecipient
  {
      public required string Id { get; init; }               // the JWE kid, [A-Za-z0-9_-]{1,64}

      public required string Name { get; init; }

      public required JObject PublicKey { get; init; }        // JWK, RSA with a modulus of 3072 bits or more

      public bool IsEnabled { get; init; } = true;

      public required DateTimeOffset CreatedAt { get; init; }

      public required string CreatedBy { get; init; }
  }
  ```

  - At most `MaxRecipients` (20) per project. Private keys never reach Storyteller.
  - Recipients are long-lived, and the customer rotates them by adding a new one and removing the old one.
- **Value format.** JWE (RFC 7516) General JSON Serialization:
  - `enc` is `A256GCM`, and the protected header also carries `"cty": "text/plain"` and `"2s": "{organization}/{project}/{view}/{lower-cased key}"`;
  - one entry in `recipients` per recipient, each with a header holding `alg` (`RSA-OAEP-256`) and `kid`.

  Only RSA-OAEP-256 is accepted, because .NET and WebCrypto support it without extra libraries. EC recipients are out of scope.
- **Server validation on write**, without decryption:
  - the JSON parses;
  - the protected header decodes, has `enc` `A256GCM`, and its `2s` equals the target place;
  - at least one recipient, and every `kid` is a known, enabled recipient (`UnknownRecipient` otherwise);
  - at most 49,152 characters.

  The recipient ids are stored in `StoredValue.Recipients` for display.
- **What the platform cannot do for `Hyok`:**
  - preview or reveal plaintext (a reveal returns the JWE document);
  - diff versions (400);
  - validate the content;
  - detect an unchanged value on import, other than an identical document;
  - change the level server-side (section 16).
- **Reading.** The binding, a reveal and an export return the document only to callers with secret rights, under the mTLS rule, as defense in depth. The consuming application decrypts it with its private key, using the helpers of the C# and TypeScript SDKs (section 25).
- **Recipient changes do not touch stored values.**
  - Adding a recipient needs a client with an existing recipient's private key to encrypt the values again. The CLI can do it with `sform story kv reencrypt` (section 27).
  - Removing a recipient does not take its access to values it could already read. The values have to be changed.
  - The admin UI warns about both.
- **Encryption in the admin UI** happens in the browser with WebCrypto (section 25). The plaintext never leaves the browser.

### 15. Reference only

A reference is a `Public` pair whose value points to a secret in the customer's own vault. The consuming application resolves it with its own identity, so the platform is never in the secret path. Azure App Configuration's Key Vault references are the prior art.

- **Two content types** mark a reference, and both are validated on write (`InvalidSecretReference`):

  | Content type | Value |
  | --- | --- |
  | `application/vnd.2s.secret-reference` | A URI: `https://{vault}.vault.azure.net/secrets/{name}[/{version}]`, or the shorthand `kv://{vault}/{name}[/{version}]` |
  | `application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8` | App Configuration's JSON `{ "uri": "https://…/secrets/{name}[/{version}]" }`, so customers can move references over as they are |

- `KeyValue.IsReference` is set for both. The UI shows a Reference badge.
- **Binding** inserts the value unchanged, as for every public pair.
- **No server-side resolution.** `key-values.md` has a sample resolver with `SecretClient` and `DefaultAzureCredential`. SDK resolvers are out of scope.
- **Literal references** written directly into configurations need nothing from this spec.

### 16. Changing the protection of a key (R16)

- **Server-side, between `Standard`, `Hsm` and `Byok`:**
  - `ProtectAsync` (`POST …/key-values/protect`, section 23) decrypts the latest version and encrypts it under the target level as a **new version** `n + 1`.
  - The version's author is the caller. The audit trail records `Reprotect`.
  - Older versions keep their level until they expire, so the history shows both. The UI warns that old versions stay at the old level for up to a year.
  - The target level must be allowed, and the caller needs `CanWriteSecrets`.
- **To or from `Public` or `Hyok`:** only with a new value from the client, through a normal write (`PUT` with `Protection` and `Value`).
  - `ProtectAsync` returns `NotAllowed` with `ReprotectNotPossible` for such a change.
  - Public to secret: the old public versions stay readable by readers until they expire. The API response and the UI say so.
  - Secret to public: the old versions stay secret.
- **Bulk.** Protect takes keys or a prefix (section 6.1), which is how a project moves its secrets to HSM after enabling it.

### 17. Key rotation and re-wrap

- **Rotation.**
  - The `Standard` and `Hsm` KEKs have a Key Vault rotation policy (`RotationInterval`, default 365 days). An Administrator can also rotate now with `POST …/policy/rotate { Protection }`, which calls `RotateKeyAsync`.
  - `Byok` keys are rotated by the customer. The daily check notices the new version.
- **New writes** always wrap with the current key version.
- **Re-wrap** moves existing envelopes to the current key version: unwrap with the old version, wrap with the new one. The ciphertext does not change.
  - `KeyValueProtectionTimer` (Api.Functions, timer trigger, hourly) handles the projects whose policy has a pending `Rewrap`.
  - It also runs the daily key checks: the current versions, BYOK health, and the next rotation date.
  - It pages through the project's `kv.` and `kvv.` items in all `{project}.kv.*` partitions whose `Content.Encrypted.KeyId` is not the current version of that level.
  - It patches `Content.Encrypted` with `IfMatchEtag`, at most `MaxConcurrentKeyOperations` in parallel.
  - **TTL is preserved.** A patch resets an item's `_ts`, and Cosmos counts TTL from `_ts`, so the same patch sets `ttl` to the item's remaining lifetime. Catalog items of deleted keys get the rest of `DeletedRetention`.
  - Progress is kept in `KeyValuePolicy.Rewrap`: `{ Protection, TargetKeyVersion, StartedAt, Processed, Remaining, CompletedAt, LastError }`.
- **Old key versions stay enabled.** Storyteller never disables them. An unused HSM key version stops costing after 30 days, because only versions used in the last 30 days are billed.
- **Cost.** Two key operations per envelope. A project with 1,000 secrets and 12,000 versions needs 24,000 operations, about two minutes at the HSM limit.

### 18. Secret delivery: HTTPS, mTLS and private networks (R17)

#### 18.1 Who may receive a secret value

A secret value is a decrypted value or a `Hyok` document, delivered by a reveal, a secret version or diff, an export with secrets, or a resolved configuration with secrets (native or raw vault bindings). It is delivered only when:

| Caller | Rule |
| --- | --- |
| User (`User.Impersonation`) | Role `ContributorWithSecrets` or higher on the project. HTTPS with the bearer token; browsers cannot practically do mTLS |
| Machine, authenticated by an mTLS certificate | Scope `Configuration.Secrets`. The request has `PresentingCertificateThumbprint`: a machine or shared certificate validated by `MachineAuthenticationMiddleware` |
| Machine with an API key or a bearer token only | Scope `Configuration.Secrets`, and only when the policy has `RequireMtlsForSecretDelivery = false` |

- `RequireMtlsForSecretDelivery` defaults to `true`.
- **Without the rule met,** an explicit request (a reveal, a secret version, or an export with secrets) returns `403 MtlsRequired`.
- **A resolved configuration** leaves the secrets unbound, as for a caller without rights, and adds the response header `X-2S-Secrets-Withheld: mtls-required`, so that the cause can be found.
- **Behaviour change:** the rule also covers raw vault bindings in resolved configurations, so AuthKit token machines with `Configuration.Secrets` stop receiving secrets in projects that keep the default.
- `request.GetSecretRightsAsync(access, policyStore, organization, project)` computes `KeyValueCaller` (rights and channel) once per request. `GetConfigurationResolved` and the key-value endpoints both use it.

#### 18.2 Durable certificate store

The mTLS rule needs certificates that work on every instance and survive restarts.

- **`CosmosClientCertificateStore : IClientCertificateStore`** (`Backend.CosmosDb/src/Accessing/`) stores machine certificate records in the organization container:
  - partition `{project}.access`, next to the shared certificates;
  - id `mcert.{thumbprint}`, with `MachineAccessId`, `NotAfter`, and `ttl` until `NotAfter` + 30 days.
- **Lookups.**
  - `GetByThumbprintAsync` is a point read.
  - `GetByMachineAccessIdAsync` is a single-partition query, used only by renewal.
  - Both are cached in memory: 5 minutes for a hit, 30 seconds for a miss.
  - `RevokeAsync` deletes the item and evicts the local cache. Other instances notice within 5 minutes, the documented revocation latency.
- **Registration.** `AddCosmosDbAnnotations` replaces `InMemoryClientCertificateStore`.
- **Existing certificates** issued before the change are only in memory, so they work again once their machine is reset or renewed. `authentication.md` drops the known limitation.

#### 18.3 Transport

- **HTTPS only.**
  - The Function App runs with `httpsOnly` and a minimum TLS 1.2 (1.3 where the platform offers it).
  - A new `TransportSecurityMiddleware` returns `400 HttpsRequired` outside Development when the request scheme, or `X-Forwarded-Proto` behind a trusted proxy, is not `https`.
  - It adds `Strict-Transport-Security: max-age=31536000; includeSubDomains`.
- **No secrets in URLs.** Values travel in bodies only. Export and import are `POST`s.
- **`Cache-Control: no-store`** is set on every response that contains a secret value.
- **No secrets in logs or telemetry.**
  - Request and response bodies are never logged.
  - Log messages carry keys and versions, never values.
  - A test runs every endpoint with a recording logger and searches the output for the plaintext.
- **Memory.** Data keys are zeroed after use, and the plaintext cache is bounded by size and time.
- **Private networks** (a deployment requirement; there is no IaC in this repository, so `key-values.md` documents the setup):
  - Functions with VNet integration;
  - Cosmos DB, the Standard and Premium vaults and any Managed HSM behind private endpoints, with public network access disabled;
  - BYOK customer vaults reached through an approved private endpoint, or through a firewall that allows the static egress IP (section 13).

### 19. Audit trail

Secrets get an audit trail that customers can read. It is required for `Hsm` and `Byok`, and kept for every secret level.

```csharp
public record class KeyValueAuditEvent
{
    public required string Id { get; init; }

    public required DateTimeOffset Time { get; init; }

    public string? View { get; init; }

    public string? Key { get; init; }

    public ulong? Version { get; init; }

    public KeyValueProtection? Protection { get; init; }

    public required KeyValueAuditOperation Operation { get; init; }

    public required string Actor { get; init; }             // GetAuthor()

    public required KeyValueChannel Channel { get; init; }

    public int Count { get; init; } = 1;                    // aggregated binding reads

    public string? CorrelationId { get; init; }             // also sent to Key Vault as x-ms-client-request-id

    public string? Detail { get; init; }                    // e.g. the old and new level, the error of a failed unwrap
}

public enum KeyValueAuditOperation
{
    Reveal, Export, BindingRead, Create, Update, Delete, Restore, PermanentDelete, Purge, Reprotect,
    PolicyChange, Rotate, Rewrap, ByokVerify, ByokUnavailable, RecipientAdded, RecipientRemoved,
}
```

- **Storage.**
  - Organization container, partition `{project}.audit`, id `kva.{yyyyMMddHHmmssfff}.{guid:N}`.
  - `ttl` = `AuditRetention` (default 400 days, a little over a year, to cover yearly audits).
- **Writes.**
  - Management operations, reveals, exports, policy changes and key events write one event each, inline, at about 6 RU.
  - Binding reads of secrets would be too many to record one by one. They are counted per actor, key and hour by the in-memory aggregator of section 20, and written as one event with `Count`.
  - Audit events are not deleted by a purge or a permanent delete. They record the purge.
- **Read:** `GET v1/{org}/{project}/key-values/audit` (Administrator), newest first, filtered by view, key, operation and time range, paged.

### 20. Usage metering (R18)

#### 20.1 Meters

Per project and per UTC day:

| Meter | Unit | Source | Priced item (section 30) |
| --- | --- | --- | --- |
| `keys.public`, `keys.reference` | Active keys, at the daily snapshot | Snapshot | Public, Reference |
| `keys.standard`, `keys.hsm`, `keys.byok`, `keys.hyok` | Active keys, at the daily snapshot | Snapshot | Per level |
| `versions` | Stored version items, at the snapshot | Snapshot | Informational |
| `storage.bytes` | Sum of `Content.Length` over catalog and version items | Snapshot | Informational |
| `reads.binding` | Key lookups by bindings (after the memo of section 8) | Counter | Reads |
| `reads.api` | Gets, lists and version reads | Counter | Reads |
| `reveals` | Secret values delivered: reveals, exported secrets, secret bindings | Counter | Informational, audit |
| `writes` | Versions written | Counter | Informational |
| `kms.standard`, `kms.hsm`, `kms.byok` | Wrap and unwrap calls | Counter | Informational (`kms.byok` is billed by Azure to the customer) |
| `hsm.keyVersions` | HSM KEK versions used in the last 30 days | Snapshot | HSM project fee |

Key-months for a calendar month are the sum of the daily key counts divided by the number of days.

#### 20.2 Collection

- **Counters.**
  - `IKeyValueUsageRecorder` (`Backend.Core`) adds to an in-memory `ConcurrentDictionary<(organization, project, meter), long>`.
  - `KeyValueUsageFlushService`, an `IHostedService` in the Functions worker, flushes it every `FlushInterval` (60 seconds) and on shutdown. Each flush is one `PatchItemAsync` with `Increment` operations per project, on the day document `kvu.{yyyy-MM-dd}` in the organization container, partition `{project}.usage`, created when missing.
  - A crashed instance loses at most one interval of counts, so meters can undercount but never overcount.
- **Snapshots.**
  - `KeyValueUsageSnapshotTimer` (Api.Functions, daily at 00:10 UTC) enumerates the `org.*` containers (`GetContainerQueryIterator`).
  - It runs one aggregate query per container: `SELECT c.ProjectName, c.Content.Protection, c.Content.ContentType, COUNT(1), SUM(c.Content.Length) … WHERE STARTSWITH(c.id, 'kv.') AND NOT IS_DEFINED(c.DeletedAt) GROUP BY …`, and a second one for the `kvv.` items.
  - It writes the gauges into the previous day's document.
- **Retention.** Day documents have `ttl` = `Usage:Retention` (default 400 days).

#### 20.3 Report

```csharp
public record class KeyValueUsageReport
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public required IReadOnlyDictionary<string, decimal> Totals { get; init; }  // meter → total; key meters as key-months

    public required IReadOnlyList<KeyValueUsageDay> Days { get; init; }

    public IReadOnlyDictionary<string, decimal>? EstimatedCharges { get; init; } // only when KeyValues:Pricing is configured
}
```

- `GET v1/{org}/{project}/usage/key-values?from=&to=` (Administrator, at most 400 days) reads the day documents in one single-partition query.
- **Estimates.** `KeyValues:Pricing` (optional list prices per priced item, section 30) turns totals into `EstimatedCharges`.
- **Out of scope:** invoicing and charging tenants.

### 21. Tests (Phase 1b)

- **Policy:** defaults; enable and disable per level; `ProtectionInUse`; ETag concurrency; cache invalidation.
- **`Hsm`:** the key is created with `HardwareProtected`; the organization override to Managed HSM (mocked `KeyClient`); wrap and unwrap through the provider (Lowkey Vault, if it accepts HSM key types; otherwise mocked).
- **`Byok`:**
  - verify passes and fails (key disabled, too small, missing operations);
  - a revocation gives 424, sets `Health`, and writes an audit event;
  - cached values keep working until the cache expires;
  - a customer key rotation detected by the daily check starts a re-wrap.

  The cross-tenant credential is mocked.
- **`Hyok`:**
  - documents encrypted by the SDK helpers pass validation;
  - a wrong `2s` place, an unknown or disabled `kid`, a wrong `enc`, and an oversized document are rejected;
  - reveal returns the document;
  - diff gives 400.
- **References:** both content types; invalid URIs; `IsReference`.
- **Protect:**
  - Standard → Hsm → Byok creates new versions and keeps the old levels in history;
  - Public and Hyok targets give `NotAllowed`;
  - bulk by prefix;
  - the policy and rights checks.
- **Rotation and re-wrap:**
  - after a rotation, every envelope ends on the new version;
  - `ttl` is preserved: an item's expiry time is unchanged within a second;
  - an interrupted re-wrap resumes.
- **Delivery:**
  - the mTLS rule for users, certificate machines and API-key machines;
  - 403 `MtlsRequired`;
  - the withheld header on resolved configurations, also with raw vault bindings.
- **Certificate store:** a certificate issued on one instance authenticates on another (two service providers); revocation; TTL; renewal lookup.
- **Transport:** `HttpsRequired`; HSTS; `no-store`; the recording-logger test finds no plaintext in logs.
- **Audit:** events per operation; aggregated binding reads; filters and paging; events survive a purge.
- **Usage:**
  - counters flush and increment concurrently from two recorders;
  - the snapshot gauges per level, references counted apart;
  - key-month arithmetic over a month;
  - estimates with and without pricing.

### 22. Documentation (Phase 1b)

- `key-values.md`:
  - the protection levels, and the policy;
  - the HSM backends and the Managed HSM runbook;
  - the BYOK setup for customers: consent, role, networking, revocation and rotation;
  - HYOK: the recipients, the JWE format, and how to decrypt;
  - references, protection changes, rotation and re-wrap;
  - the delivery rules, private networking, the audit trail, and the usage meters.
- `authentication.md`: the durable certificate store, which removes the known limitation, and the mTLS rule for secrets.
- `access.md`: who may change the policy, read the audit trail and read usage.

---

## Phase 2 — API

### 23. Routes

View-scoped, base `K = v1/{organization}/{project}/{view}/key-values`:

| Method | Route | Query | Body → response | Status codes |
| --- | --- | --- | --- | --- |
| GET | `K` | `keyPrefix`, `protection` (repeatable), `state`, `includeValues`, `continuationToken` | → `KeyValuesResponse` | 200, 400, 401, 403 |
| GET | `K/{key}` | `reveal`, `includeDeleted` | → `KeyValue` | 200, 400, 403, 404, 424, 503 |
| PUT | `K/{key}` | | `KeyValueWrite` → `KeyValue`, never with a secret value | 200, 400, 403, 409, 412, 424, 501, 503 |
| DELETE | `K/{key}` | `permanent`, `expectedVersion` | → `204`; a soft delete, or a permanent delete with `permanent=true` (Administrator) | 204, 403, 404, 409, 412 |
| POST | `K/restore` | | `KeyValueBulkRequest` → `KeyValueBulkResult` | 200, 400, 403 |
| POST | `K/purge` | | `KeyValueBulkRequest` → `KeyValueBulkResult` (Administrator) | 200, 400, 403 |
| POST | `K/protect` | | `KeyValueBulkRequest` with `Protection` → `KeyValueBulkResult` | 200, 400, 403, 409 |
| GET | `K/{key}/versions` | | → `KeyValueVersion[]`, oldest first | 200, 403, 404 |
| GET | `K/{key}/versions/{version}` | `reveal` | → `KeyValue` | 200, 403, 404, 424, 503 |
| GET | `K/{key}/versions/{version}/diff`, `…/diff/{versionFrom}` | `format=unified` | → `DiffResult`, or `text/plain` | 200, 400, 403, 404, 424 |
| POST | `K/export` | | `KeyValueExportRequest` → `KeyValueDocument` | 200, 403, 424, 503 |
| POST | `K/import` | `mode`, `dryRun`, `keyPrefix` | `KeyValueDocument` → `KeyValueImportResult` | 200, 400, 403, 409, 424, 501, 503 |

Project-scoped, base `P = v1/{organization}/{project}/key-values`:

| Method | Route | Body → response | Role |
| --- | --- | --- | --- |
| GET | `P/policy` | → `KeyValuePolicy` | Reader |
| PUT | `P/policy` | `KeyValuePolicyUpdate` (`AllowedProtections`, `DefaultSecretProtection`, `RequireMtlsForSecretDelivery`, `Hsm.RotationInterval`, `Byok.KeyUri`, `Byok.TenantId`, `ExpectedVersion`) → `KeyValuePolicy` | Administrator |
| GET | `P/policy/byok/setup` | → `{ ClientId, ConsentUrl, RequiredRole }` | Administrator |
| POST | `P/policy/byok/verify` | → `KeyValuePolicy` | Administrator |
| POST | `P/policy/rotate` | `{ Protection }` → `KeyValuePolicy` | Administrator |
| GET | `P/recipients` | → `ClientEncryptionRecipient[]` | Reader |
| POST | `P/recipients` | `{ Id?, Name, PublicKey }` → `ClientEncryptionRecipient` | Administrator |
| PATCH | `P/recipients/{id}` | `{ Name?, IsEnabled? }` → `ClientEncryptionRecipient` | Administrator |
| DELETE | `P/recipients/{id}` | → `204` | Administrator |
| GET | `P/audit` | query `view`, `key`, `operation`, `from`, `to`, `continuationToken` → `KeyValueAuditResponse` | Administrator |
| GET | `v1/{organization}/{project}/usage/key-values` | query `from`, `to` → `KeyValueUsageReport` | Administrator |

- **Definitions.**
  - Route ids go in `RouteIds.KeyValues`, with the operation names `GetKeyValues`, `GetKeyValue`, `SetKeyValue`, `DeleteKeyValue`, `RestoreKeyValues`, `PurgeKeyValues`, `ProtectKeyValues`, `GetKeyValueVersions`, `GetKeyValueVersion`, `GetKeyValueVersionDiff(Custom)`, `ExportKeyValues`, `ImportKeyValues`, `GetKeyValuePolicy`, `SetKeyValuePolicy`, `GetByokSetup`, `VerifyByok`, `RotateKeyValueKey`, `GetRecipients`, `AddRecipient`, `UpdateRecipient`, `RemoveRecipient`, `GetKeyValueAudit`, `GetKeyValueUsage`.
  - Tag `Tags.KeyValues = "KeyValues"`.
- **No reserved keys.**
  - The collection actions `restore`, `purge`, `protect`, `export` and `import` are `POST` only, and the routes of a key never use `POST`.
  - `RouteTableTests` checks that `GET`, `PUT` and `DELETE` on keys with those names reach the key functions.
  - If the Functions router cannot tell the routes apart by method, the actions move to a sibling segment `…/{view}/key-value-actions/{action}`.
- **Reserved view and project names.** The project-scoped routes have the literal `key-values` where view routes have `{view}`. Their next segments (`policy`, `recipients`, `audit`) never follow `key-values` in a view route, so they do not collide. The admin UI adds project pages `/{org}/{project}/audit` and `/{org}/{project}/usage`, so `audit` and `usage` join `NameRules.ReservedProjectAndViewNames`. Only new names are checked.
- **Headers.** `Cache-Control: no-store` on every response with a secret value. `X-2S-Secrets-Withheld` on resolved configurations (section 18).
- **Diff operations** are added to `DiffUnifiedResponseDocumentFilter`.

### 24. Authorization

The endpoints use the `CheckScope` and `CheckAccessToProjectAsync` pattern. `request.GetSecretRightsAsync` (section 18) supplies `KeyValueCaller`.

| Operation | User, project role | Machine, any of these scopes |
| --- | --- | --- |
| List, get without reveal, versions list, public version content and diff, export without secrets, policy, recipients | Reader | `Configuration.Read`, `Configuration.ReadWrite`, `Default.Read`, `Default.ReadWrite` |
| Reveal, secret version content and diff, export with secrets | ContributorWithSecrets | a read scope and `Configuration.Secrets`, under the mTLS rule |
| Set, delete or restore a public pair; import of public items | Contributor | `Configuration.ReadWrite`, `Default.ReadWrite` |
| Set, delete, restore or protect a secret; level changes to or from secret; import with secrets | ContributorWithSecrets | a write scope and `Configuration.Secrets`, under the mTLS rule |
| Permanent delete, purge, policy changes, BYOK setup and verify, rotation, recipients, audit, usage | Administrator | Not allowed for machines |
| Resolved configuration with secret bindings | ContributorWithSecrets | `Configuration.Secrets`, under the mTLS rule |

- **Audit events** are written by the service (section 19), so the API only passes the correlation id.
- **Machine scopes (D6).** Two scopes are appended to `MachineAccessScope`:
  - `SecretsRead = 6` → `["Configuration.Read", "Configuration.Secrets"]`;
  - `SecretsReadWrite = 7` → `["Configuration.Read", "Configuration.ReadWrite", "Configuration.Secrets"]`.

  Entra ID machines need the optional app roles `Auth:AppRoles:SecretsRead` and `SecretsReadWrite`; without them, creating such a machine returns 400.

### 25. OpenAPI and SDKs

1. Export the OpenAPI document from `SerialOpenApiTriggerFunction` into `Sdk.NSwag/` and `sdk.typescript/` as `open.api.v<version>.json`. Remove the superseded copies from `sdk.typescript/`.
2. **NSwag.** Regenerate `Sdk.NSwag/src/ApiSdk.g.cs`, and register `IKeyValuesApiClient` in `ServicesCollectionExtensions.AddStorytellerSdk`.
3. **C# client-side encryption** (`Sdk.NSwag/src/ClientSideEncryption/`), BCL only:
   - `ClientSecretEncryptor.Encrypt(string value, IEnumerable<ClientEncryptionRecipient> recipients, string place)` builds the JWE with AES-256-GCM (`AesGcm`) and RSA-OAEP-256 (`RSA.Encrypt` with `OaepSHA256`);
   - `ClientSecretDecryptor.Decrypt(string jwe, RSA privateKey, string kid)` reads it.

   The CLI uses both. Applications use `Decrypt`.
4. **TypeScript SDK.**
   - Run `SPEC=../open.api.v<version>.json npm run generate`, and commit the generated `key-values` client, composables, Zod schemas and MSW handlers.
   - `lib/key-values.ts`, exported from the root entry:
     - `isValidKeyValueKey`;
     - `.env` parse and serialize, with `KEY=VALUE` lines, `#` comments, an optional `export ` prefix, double-quoted values with `\n`, `\"` and `\\` escapes, and `.` ↔ `__` in keys;
     - `flattenJson` and `toFlatJson`;
     - `detectKeyValueFormat`;
     - `isSecretReference` and `parseSecretReference`.
   - New subpath `./hyok` with `encryptForRecipients(value, recipients, place)` and `decryptClientSecret(jwe, privateKey, kid)`, on WebCrypto (`RSA-OAEP` with SHA-256, `AES-GCM`). It works in browsers and in Node 22 or later, with no runtime dependencies. The tests use `jose` as a dev dependency to check interoperability both ways.

### 26. Tests and documentation (Phase 2)

- **`Api.Functions/test/KeyValuesHttpTests.cs`:**
  - the role matrix (above);
  - a bulk restore with mixed levels by a Contributor gives `Forbidden` for the secret only;
  - the machine scopes and the mTLS rule;
  - `no-store`;
  - keys named like the actions;
  - 412, 424, 501 and 503;
  - policy, recipients, audit and usage endpoints.
- **`ErrorResponseMappingTests`, `RouteTableTests`, `OpenApiDocumentTests`, `MachineAccessRulesTests`:** the new mappings, routes, tag and scopes.
- **SDK tests:**
  - `key-values.test.ts` (keys, `.env`, flattening, format detection, references);
  - `hyok.test.ts` (round trip; decryption of `jose` output; `jose` decryption of SDK output; several recipients);
  - C# tests of `ClientSecretEncryptor` and `ClientSecretDecryptor` with the same vectors.
- **Documentation:**
  - `key-values.md`: the API section with `curl` examples;
  - `access.md` and `authentication.md`: the roles and the machine scopes;
  - `sdk.typescript/README.md`: the helpers and `./hyok`.

---

## Phase 3 — CLI

### 27. Commands (`src/Platform/Cli/src/Commands/KeyValues/`)

Under `sform storyteller` (alias `story`), next to `config` and `template`:

```text
sform story kv [-p|--prefix <prefix>] [--protection public|standard|hsm|byok|hyok]... [--values] [--deleted|--all] [-k|--continuation-token <token>]
sform story kv get <key> [--reveal] [--version <n>] [--value-only] [-e|--export <file>]
sform story kv set <key> [<value>] [--protection <level>|--secret] [--stdin] [-i|--import <file>] [--content-type <type>] [--reference] [--expected-version <n>]
sform story kv delete|remove <key> [--permanent [-y|--yes]] [--expected-version <n>]
sform story kv restore <key>... | --prefix <prefix>
sform story kv purge <key>... | --prefix <prefix> [-y|--yes]
sform story kv protect <key>... | --prefix <prefix> --protection standard|hsm|byok
sform story kv versions <key>
sform story kv diff <key> [to] [from] [--reveal]
sform story kv export [-o|--output <file>] [--format json|flat|env] [-p|--prefix <prefix>] [--include-secrets]
sform story kv import <file> [--format json|flat|env] [--secret <glob>]... [--protection <level>] [--mode merge|replace] [-p|--prefix <prefix>] [--dry-run] [-y|--yes]
sform story kv reencrypt <key>... | --prefix <prefix> --private-key <pem> --kid <id>
sform story kv policy [set --allow <level>... --default <level> --require-mtls true|false --byok-key <uri> --byok-tenant <id>]
sform story kv policy byok-setup | byok-verify | rotate --protection standard|hsm|byok
sform story kv recipients [add --name <name> --public-key <pem|jwk> [--id <kid>] | disable <id> | remove <id>]
sform story kv audit [--key <key>] [--operation <op>] [--from <date>] [--to <date>]
sform story kv usage [--from <date>] [--to <date>]
```

`CommandNames` gets `KEY_VALUES = "key-values"`, `KV = "kv"`, `RESTORE`, `PURGE`, `PROTECT`, `EXPORT`, `IMPORT`, `REENCRYPT`, `POLICY`, `RECIPIENTS`, `AUDIT` and `USAGE`. `StorytellerListCommand` adds `typeof(KeyValueListCommand)` to its `[Subcommand(...)]` list. Project commands (`policy`, `recipients`, `audit`, `usage`) ignore the view of the context.

- **Listing** shows Key, Level, Value (only with `--values`; secrets show `••••••`), Version, Author and Updated. `--deleted` adds "expires in N days".
- **Secret input.**
  - With a secret level and no value source, `set` prompts with hidden input (`Prompt.GetPassword`).
  - A secret given as an argument works, but prints a warning about shell history.
- **`Hyok` writes** encrypt locally with `ClientSecretEncryptor`, for the project's enabled recipients (`GET …/recipients`). The plaintext never leaves the machine. Reading a `Hyok` value with `get --reveal` prints the JWE document. `--private-key` with `--kid` decrypts it locally.
- **`reencrypt`** decrypts `Hyok` values with the given recipient private key and encrypts them for the current recipients, one `PUT` per key. It is how a newly added recipient gets access (section 14).
- **`--reference`** sets the content type `application/vnd.2s.secret-reference`, so the value is checked as a reference.
- **Dangerous operations.**
  - `delete --permanent` and `purge` ask for the key, or the view name for a prefix, unless `--yes` is given.
  - `delete` prints how to restore.
- **Formats** (`Commands/KeyValues/KeyValueFormats.cs`) follow the SDK conventions. In `flat` and `env`, keys that match a `--secret` glob get `--protection`, or the project default secret level. All other keys are `Public`.
- **Import** runs a dry run first, shows the plan, asks for confirmation unless `--yes`, and then imports. Replace lists the deletes and needs `--yes` or the view name typed.
- **Exit codes.** Partial bulk results and partial imports return a non-zero exit code. A 403 `MtlsRequired` explains that a certificate is needed.

### 28. Tests and documentation (Phase 3)

- **Tests (`Cli/test/Commands/KeyValues/`):**
  - format round trips;
  - glob matching;
  - the hidden prompt;
  - `Hyok` encryption, decryption and `reencrypt` with test keys;
  - rendering of plans, bulk outcomes, audit and usage;
  - exit codes.
- **Documentation.** `key-values.md` gets a CLI section with an example per command.

---

## Phase 4 — Admin UI

### 29. Screens

#### 29.1 Navigation, routes and data

- **Navigation** (`app/utils/navigation.ts`):
  - **View section:** "Key values" after Configurations (`i-lucide-key-round`, edit role `Contributor`).
  - **Project section:**
    - "Secrets" (`i-lucide-shield`), a tab of Settings, visible to every reader and editable by Administrators;
    - "Audit" (`i-lucide-scroll-text`) and "Usage" (`i-lucide-gauge`), both with minimum role Administrator.
- **Pages:**
  - `[org]/[project]/[view]/key-values.vue`, the list;
  - `key-values/[key].vue`, a `DetailPanel` with `index.vue` (Value) and `versions.vue` (Versions);
  - `[org]/[project]/settings` gets a Secrets tab;
  - `[org]/[project]/audit.vue` and `[org]/[project]/usage.vue`.
- **Queries.** `queries/scope.ts` gets `useKeyValues`, `useKeyValue`, `useKeyValueVersions(key, { enabled })`, `useKeyValuePolicy`, `useRecipients`, `useKeyValueAudit` and `useKeyValueUsage`.
- **Invalidation** after a mutation: `invalidateByPath` on `/v1/{org}/{project}/{view}/key-values`, plus `/v1/{org}/{project}/key-values/policy` after policy changes.

#### 29.2 Key values list

- **Columns:**
  - Key, monospace and copyable;
  - Level: a badge with an icon and text, so color is never the only signal. Public, or Reference with a link icon; Standard, HSM and BYOK with lock icons in rising strength; HYOK with an eye-off icon;
  - Value: the first line of a public value, `••••••••` for a secret;
  - Version, Author, Updated.
- **Toolbar:**
  - search on key and public value;
  - a level filter;
  - a state switch, **Active** or **Deleted**;
  - the buttons New, Import and Export.
- **Row actions:**
  - Edit; Copy key; Copy binding (`@key`);
  - Reveal (secret, `secrets` capability);
  - Change protection (Standard, HSM, BYOK);
  - Delete, with a confirmation that says the key can be restored for N days;
  - Delete permanently (Administrator, type-to-confirm).
- **Selection.**
  - In Active: Delete and Change protection.
  - In Deleted: Restore and Purge (Administrator, type-to-confirm).
  - Each sends one bulk call and shows the outcome per key. Failed rows stay selected with their reason.
- **BYOK unavailable.** A banner across the section shows "Customer key unavailable since {time}" with the last error, linked to the Secrets settings.

#### 29.3 Detail and editor

- **Value tab.**
  - Metadata: level, version, author, created and updated.
  - The binding to copy: `@key`, `@public(key)` or `@secret(key)`.
  - Public values open in `CodeEditor`, in JSON mode for `application/json`. A reference shows its target and is validated.
  - Server-side secrets show a masked field with **Reveal** and **Set new value**.
  - `Hyok` shows its recipients, and offers **Set new value** that encrypts in the browser with `./hyok` for the enabled recipients. It has no reveal of plaintext.
  - A deleted key opens read-only, with Restore and its expiry.
- **`KeyValueForm`** (VeeValidate with the generated Zod schema and the SDK key rule) has these fields:
  - Key;
  - Level, from the policy's allowed levels, defaulting to Public or the default secret level;
  - Value;
  - Content type, including "Secret reference".

  Changing the level of an existing key explains what happens to old versions (section 16).
- **Saving.**
  - Save sends `ExpectedVersion`.
  - A 412 opens `ConflictDialog`.
  - A 409 `ProtectionNotAllowed` links to the Secrets settings.
  - A 424 explains the customer key.
- **Editing comfort:** `useUnsavedChanges` and Cmd+S.

#### 29.4 Versions (R6)

- **Loaded only on request.** The Versions tab enables its query only while it is open.
- **Adapter.** `VersionSource.list` returns a `VersionEntry { Version; Author?; CreationTime; ExpirationTime?; Badge? }`, which both `ConfigurationVersion` and `KeyValueVersion` satisfy. The level of each version is its badge. `ExpirationTime` shows when it goes.
- **Public:** View and Compare as today.
- **Server-side secrets:** View and Compare after an explicit Reveal, with `gcTime: 0`.
- **`Hyok`:** the timeline only, with the explanation that the platform cannot read the values.

#### 29.5 Import and export

- **Import dialog**, in three steps:
  1. **Source:** a file or pasted text, with the format detected.
  2. **Review:**
     - the parsed items with a level per row;
     - "Mark keys matching … as" a chosen secret level;
     - Merge or Replace;
     - **Check** runs a dry run and shows the plan.

     `Hyok` rows are encrypted in the browser before the import.
  3. **Apply:** shows the result, with "Retry failed" and, after a Replace, "Undo deletes" (one restore call).

  Replace with deletes needs the view name typed.
- **Export menu:** JSON, `.env` or flat JSON, with "Include secret values" for the `secrets` capability, with a warning. The download goes through a Blob URL, never through the query cache.

#### 29.6 Secrets settings (project)

- **Allowed levels**, as toggles with a short explanation, cost hint and requirement for each.
  - Disabling a level that is in use shows the counts from `ProtectionInUse`, with a link to the filtered list.
- **Default secret level**, and **Require mTLS for machines** (on by default; turning it off asks for confirmation).
- **Standard and HSM keys:**
  - key id, current version and next rotation;
  - a **Rotate now** button;
  - the re-wrap progress (`Rewrap`).
- **BYOK wizard:**
  1. Shows the app id, a copyable consent link, and the role to grant, with the `az` command.
  2. Takes the key URI and the tenant id.
  3. Verifies, and shows the result and the health.
- **Recipients (HYOK):**
  - a list with name, id, key size and state;
  - add by pasting a PEM or JWK public key, then disable or remove;
  - warnings about re-encryption (section 14).

#### 29.7 Audit and Usage

- **Audit:**
  - a table of events (time, operation, view, key, version, level, actor, channel, count);
  - filters and paging;
  - the correlation id is copyable, to match the customer's vault logs.
- **Usage:**
  - the current month's totals per meter: key-months per level, reads, reveals and writes, plus the estimated charges when pricing is configured;
  - a daily chart of keys per level and reads;
  - a date range picker.

#### 29.8 Security, mocks and tests

- **Security and caching:**
  - secret values and `Hyok` plaintext are never cached or persisted;
  - revealed values are masked again after 60 seconds and when the panel closes;
  - copying a secret shows the clipboard toast.
- **Role gating** (the API enforces the same rules):
  - `useCan('edit')`: public writes;
  - `useCan('secrets')`: secret writes, reveal, protect, and secret export;
  - `useCan('manageAccess')`: permanent delete, purge, the policy, audit and usage.
- **Mocks.** `app/mock/key-value-handlers.ts` with `app/mock/db.ts`: a stateful store with levels, versions with TTL, the deleted state, bulk outcomes, the policy, recipients, BYOK health, audit and usage. The handlers are registered ahead of the generated ones.
- **Vitest (`test/unit/key-values/`):** filters, form rules, import parsing and plans, the version adapter, masking, `Hyok` encryption in the browser (WebCrypto in happy-dom, or the Node implementation), and the policy toggles.
- **Playwright (`test/e2e/key-values.spec.ts`)**, desktop and mobile:
  - create public, Standard and HYOK keys;
  - reveal with and without the role;
  - change protection to HSM;
  - delete, restore and purge;
  - versions loaded only on request;
  - `.env` import with a secret pattern;
  - export;
  - the BYOK wizard against the mock;
  - the audit filter;
  - the usage page.

  `accessibility.spec.ts` gets axe checks of the list, the import dialog and the Secrets settings. `visual.spec.ts` gets screenshots of the same.
- **Documentation.** The `ui.admin` README section list, and a UI section in `key-values.md`.

---

## 30. Cost model and suggested prices

These are business recommendations, not code. Azure list prices are single-region, in USD, and change by region and over time; check them before publishing prices.

**Azure prices used:**
- **Cosmos DB:** storage $0.25 per GB per month; serverless $0.25 per million RU; autoscale about $0.012 per 100 RU/s per hour.
- **Key Vault:** software key operations $0.03 per 10,000; RSA-2048 HSM key $1 per key version per month (only versions used in the last 30 days) plus operations; RSA-3072 or larger, or EC HSM keys, $5 per month; scheduled rotation about $1 each.
- **Managed HSM:** about $3.20 per hour per pool.

**Cost of goods per key per month.** An application reads each key about 9,000 times a month (every 5 minutes), and the server caches plaintext for 5 to 10 minutes:

| Level | Our cost | Main driver |
| --- | --- | --- |
| Public, Reference | under $0.005 | Cosmos reads at about 1 RU. Storage is negligible: about 15 KB with a year of versions |
| Standard | about $0.01–0.02 | Cosmos reads, plus unwraps on cache misses (at most about 4,400 per API instance) at $0.03 per 10,000 |
| HSM | about $0.01–0.02 per key, plus $1–2 per project for the HSM key versions | The fee per project dominates for small projects |
| HSM, dedicated Managed HSM | about $2,340 per month per customer pool | Fixed |
| BYOK | about $0.005 for us; the unwraps are billed to the customer | Setup, health monitoring and support time |
| HYOK | about the same as Public | Storage and reads of blobs only |

**Market anchors:**
- AWS Secrets Manager: $0.40 per secret per month plus $0.05 per 10,000 calls.
- Google Secret Manager: $0.06 per active version per location per month plus $0.03 per 10,000 accesses.
- Azure Key Vault: operations only.

**Suggested list prices.** The cost per key is cents, so prices follow value and the fixed costs per project:

| Priced item | Price | Included per project |
| --- | --- | --- |
| Public and Reference keys | $0.01 per key-month | 1,000 |
| Standard keys | $0.10 per key-month | 100 |
| HYOK keys | $0.10 per key-month | — |
| HSM keys | $0.40 per key-month, plus $15 per project-month | — |
| BYOK keys | $0.50 per key-month, plus $49 per project-month | — |
| Dedicated Managed HSM | from $3,500 per month | — |
| Reads (bindings and API) | $0.05 per 10,000 | 1,000,000 per month |

- **Margin.** Roughly 80% gross after an overhead of about 30% for compute, egress and support. Standard still costs a quarter of AWS's $0.40.
- **Read overage** keeps projects that poll heavily profitable. Without it, reads would be the one cost that grows without a price.
- **Meters to prices.** Section 20 meters each priced item: the `keys.*` meters as key-months, `reads.*`, and `hsm.keyVersions` for the HSM fee. `KeyValues:Pricing` holds the list prices for the estimates in the usage report.

---

## Decisions

- **D1 — One catalog in Cosmos** for every level. The vault is never enumerated.
- **D2 — Versions are separate items** with a 1-year TTL (R11), in the partition of their key.
- **D3 — Envelope encryption for `Standard`, `Hsm` and `Byok`** (decided 2026-10-11, option A):
  - AES-256-GCM with a data key per version, and the AAD bound to the version's place;
  - the data key wrapped with RSA-OAEP-256 by the level's KEK;
  - every version in Cosmos.

  Key Vault keeps the KEKs, and its native key versioning serves rotation. This replaces "values stored as Key Vault secrets" from the first rounds, which could not provide a physical version TTL or HSM protection.
- **D4 — One KEK per project and level.** Standard: an RSA-2048 software key in a Standard vault. HSM: an RSA-HSM 2048 key in Key Vault Premium, or in a dedicated Managed HSM per organization. BYOK: the customer's key.
- **D5 — A plaintext cache per version**, which can never be stale. Its duration per level bounds the BYOK revocation latency (5 minutes).
- **D6 — Existing scopes are reused,** plus the machine scopes `SecretsRead` and `SecretsReadWrite`.
- **D7 — Excluding value paths from indexing** is decided in Phase 1a from the measured RU difference.
- **D8 — The level is stored per version.** It can change between Standard, HSM and BYOK on the server (a new version). Changes to or from Public or HYOK need a new value from the client (decided 2026-10-11, option A).
- **D9 — The API speaks one import and export format** (canonical JSON). `.env` and flat JSON are converted in the clients.
- **D10 — Lowkey Vault** for local development and tests, plus one manual smoke test against real Azure (Premium vault, Managed HSM if available, and a second tenant for BYOK), recorded in the Phase 1b review.
- **D11 — Delete is soft** for `DeletedRetention` (90 days), and the name is never blocked. Restore returns to the latest version without a new version. Permanent delete and purge remove everything at once (Administrator).
- **D12 — Container TTL is enabled** on every organization container. Existing histories start to expire, as documented.
- **D13 — The native store is the `default` binding source**, with `public` and `secret` as explicit sources and the source call form for every source. A vault named `default` is no longer a binding source. No migration.
- **D14 — Keys are binding paths**, so every key can be bound with `@key`.
- **D15 — Secrets reach machines only over mTLS** by default (`RequireMtlsForSecretDelivery`), backed by a durable certificate store in Cosmos (decided 2026-10-11). Users receive secrets over HTTPS with their token.
- **D16 — HYOK recipients are dedicated encryption keys per project** (decided 2026-10-11). Only RSA-OAEP-256 with keys of 3072 bits or more, as JWE General JSON Serialization with A256GCM.
- **D17 — References are public pairs** with a well-known content type, compatible with App Configuration. They are never resolved by the platform.
- **D18 — Usage is metered per project and per day** (decided 2026-10-11): daily snapshots for keys, in-memory counters flushed every minute for operations. Charging tenants is a separate concern.

## Resolved questions (2026-10-11)

First round:

- **Q1 — Scope.** Per view, like configurations, schemas and templates.
- **Q2 — "Remove" and "delete".** The same operation, a soft delete that keeps the key for 90 days without blocking it, with a restore endpoint for one or more keys that returns to the latest version without a new version.
- **Q3 — Configuration binding.** The native store is the default binding source. `public` and `secret` are explicit sources, in both syntaxes.
- **Q4 — Value type.** Strings, with an optional content type.

Second round:

- **Versions** live one year, the same as configuration versions.
- **No migration** for a vault configured as `default`.
- **Permanent delete and purge** of soft-deleted keys are added.
- **Security levels:** Standard, HSM (Premium or Managed HSM, with rotation and audit), BYOK, HYOK and Reference only, chosen per key and allowed per project.
- **Transport:** secrets only over HTTPS and mTLS, and over private networks inside the platform.
- **Where the encrypted values live:** envelope encryption with every version in Cosmos (option A).
- **mTLS:** a project policy, on by default, with a durable certificate store.
- **HYOK recipients:** dedicated encryption keys per project.
- **Protection changes:** between Standard, HSM and BYOK on the server; others with a new value.
- **Metering:** per-project usage meters are part of this spec.

## Concerns

1. **Cryptography is implemented by us.** It uses only .NET and WebCrypto primitives (`AesGcm`, RSA-OAEP-256, `RandomNumberGenerator`), one data key and a random nonce per version, and an AAD bound to the place. A review by someone outside the implementation is recommended before release.
2. **HSM throughput is shared** by all projects of a Premium vault: 2,000 RSA-2048 operations per 10 seconds. The plaintext cache and one unwrap per uncached version keep that far away. Heavy organizations can get their own vault or Managed HSM.
3. **BYOK revocation is an outage by design.** The customer can stop their own reads at any time, and the cache delays it by up to 5 minutes. The banner, the 424 code and the audit event make the cause visible.
4. **BYOK rotation needs the customer's old key version** until the re-wrap is complete. The UI shows the progress, and the documentation says so.
5. **HYOK gives up server features.** Recipient changes need a client with a private key to encrypt values again, and removing a recipient does not take back past access.
6. **Enabling container TTL deletes old history** of configurations, schemas and templates older than 365 days. It is what the docs promise, but it is one-way for the shared environments.
7. **The mTLS rule changes behaviour.** Token machines lose secrets in resolved configurations, including raw vault bindings, unless the project turns the rule off. Certificates issued before the durable store must be renewed or reset.
8. **Every bare `@path` costs a Cosmos read** (about 1 RU, once per key per read), plus an unwrap on a cache miss for secrets.
9. **Metering undercounts after a crash,** by at most one flush interval per instance. That is acceptable for list-price billing, not for exact accounting.
10. **Lowkey Vault is not Key Vault.** RBAC, HSM key types, throttling and cross-tenant identities are covered by mocks and by the manual smoke test (D10).

## Out of scope

- Inheritance of pairs between views, projects or organizations, and organization-level pairs.
- Copying or promoting pairs between views, beyond export and import (and HYOK values cannot be moved by the server).
- Rotation of the secret values themselves (for example new database passwords), expiry dates and notifications.
- Access control per key, finer than the project roles.
- Labels, tags and descriptions on pairs.
- EC recipients for HYOK.
- SDK resolvers for references.
- Automated provisioning of Managed HSM pools, and infrastructure as code for private networking.
- Invoicing and charging tenants; only metering and estimates are in scope.
- Crypto-shredding of a whole project on deletion (there is no project deletion yet).
- `Sdk.Kiota`.

## Files to create / change

| Path | Change | Phase |
| --- | --- | --- |
| `src/Platform/Storyteller/Abstractions.Annotations/src/Model/KeyValues/*` | New models: values, versions, bulk, import and export, policy, recipients, audit, usage | 1a, 1b |
| `src/Platform/Storyteller/Backend.Core/src/KeyValues/*` | `IKeyValueService`, `KeyValueCaller`, `KeyValueRules`, `KeyValueOptions`, request records, exceptions | 1a |
| `src/Platform/Storyteller/Backend.Core/src/KeyValues/Protection/*` | `IKeyEncryptionProvider`, `IValueProtector`, `EnvelopeValueProtector`, JWE validation for HYOK, reference validation | 1a, 1b |
| `src/Platform/Storyteller/Backend.Core/src/KeyValues/Policy/*`, `Audit/*`, `Usage/*` | `IKeyValuePolicyStore`, `IKeyValueAuditLog`, `IKeyValueUsageRecorder` | 1b |
| `src/Platform/Storyteller/Backend.Core/src/ErrorCodes.cs`, `PreconditionFailedException.cs`, `FeatureNotConfiguredException.cs`, `ServiceUnavailableException.cs`, `FailedDependencyException.cs` | Codes and exception bases | 1a |
| `src/Platform/Storyteller/Backend.Core/src/NameRules.cs` | Reserve `audit` and `usage` | 2 |
| `src/Platform/Storyteller/Backend.CosmosDb/src/KeyValues/*`, `KeyValueMappings.cs` | `CosmosKeyValueService`, policy store, audit log, usage store and flush | 1a, 1b |
| `src/Platform/Storyteller/Backend.CosmosDb/src/Entities/KeyValues/*` | `KeyValueEntity`, `KeyValueVersionEntity`, `StoredValue`, `EncryptedValue`, audit and usage entities | 1a, 1b |
| `src/Platform/Storyteller/Backend.CosmosDb/src/Entities/Access/AccessPointEntity.cs` | `KeyValues` policy property | 1b |
| `src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosClientCertificateStore.cs` | New durable certificate store | 1b |
| `src/Platform/Storyteller/Backend.CosmosDb/src/Entities/EntityIdPrefixTypes.cs`, `PartitionKeys.cs`, `EntryPoint.cs` | Prefixes, partitions, registrations | 1a, 1b |
| `src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/JsonContentDiffer.cs` | `GetTextChanges` | 1a |
| `src/Platform/Storyteller/Backend.CosmosDb/src/ContainerFactory.cs`, `DbCreator/src/**` | `DefaultTimeToLive = -1`, the `enable-ttl` command, indexing (D7) | 1a |
| `src/Platform/Storyteller/KeyValues.Azure.KeyVault/src/**` | New project: Standard, HSM and customer key providers, options, entry point, emulator mode | 1a, 1b |
| `src/Platform/Storyteller/KeyValues.Azure.KeyVault/testint/**` | New integration tests on Lowkey Vault | 1a, 1b |
| `src/Platform/Storyteller/Backend.CosmosDb/test/*` | `CosmosKeyValueServiceTests`, policy, audit, usage, certificate store tests, `FakeKeyEncryptionProvider` | 1a, 1b |
| `src/Platform/Storyteller/Binding.Abstractions/src/BindingRequest.cs`, `Binding.Language/src/BindingEvaluator.cs` | `Context`, the source call form | 1a |
| `src/Platform/Storyteller/Binding.Core/src/KeyValueBindingSource.cs`, `EntryPoint.cs`, `BindingsOptions.cs` | Sources, `AddKeyValueBindings`, the name clash check | 1a |
| `src/Platform/Storyteller/Binding.Azure.KeyVault/src/EntryPoint.cs` | Skip `default`, `public` and `secret` | 1a |
| `src/Platform/Storyteller/Api.Functions/src/Program.cs` | Protection, bindings, flush service | 1a, 1b |
| `src/Platform/Storyteller/Api.Functions/src/Timers/KeyValueProtectionTimer.cs`, `KeyValueUsageSnapshotTimer.cs` | Re-wrap, key checks, usage snapshots | 1b |
| `src/Platform/Storyteller/Api.Functions/src/Security/TransportSecurityMiddleware.cs`, `HttpRequestDataExtensions.cs` | HTTPS and HSTS; `GetSecretRightsAsync` with the mTLS rule | 1b |
| `src/Platform/Storyteller/Api.Functions/src/V1/ConfigurationHttp.cs` | The mTLS rule and the withheld header | 1b |
| `src/Platform/Storyteller/Api.Functions/src/Definitions.cs`, `V1/KeyValuesHttp.cs`, `V1/KeyValuePolicyHttp.cs`, `V1/KeyValueUsageHttp.cs` | Routes and endpoints | 2 |
| `src/Platform/Storyteller/Api.Functions/src/ErrorHandling/*`, `OpenApi/Filters/*` | 412, 424, 501, 503; diff operations | 2 |
| `src/Platform/Storyteller/Abstractions.Access/src/MachineAccessScope.cs`, `Backend.Core/src/Accessing/MachineAccessScopes.cs`, `Access.AzureAd/src/AzureAdMachineAccessService.cs` | `SecretsRead`, `SecretsReadWrite` | 2 |
| `src/Platform/Storyteller/Api.Functions/test/*` | Endpoint, mapping, route, OpenAPI, scope, transport tests | 1b, 2 |
| `src/Platform/Storyteller/Sdk.NSwag/**` | Regenerated client, registration, `ClientSideEncryption/*` | 2 |
| `src/Platform/Storyteller/sdk.typescript/**` | New spec, regenerated output, `lib/key-values.ts`, `./hyok`, tests | 2 |
| `src/Platform/Aspire.Host/src/AppHost.cs`, `Directory.Packages.props`, `42.mono.slnx` | Lowkey Vault, `Testcontainers.LowkeyVault`, new projects | 1a |
| `src/Platform/Cli/src/Commands/KeyValues/*`, `CommandNames.cs`, `Storyteller/StorytellerListCommand.cs`, `Output/*` | Commands, formats, encryption | 3 |
| `src/Platform/Cli/test/Commands/KeyValues/*` | New | 3 |
| `src/Platform/ui.admin/src/app/**` | Navigation, pages, components (`key-values/*`, `secrets/*`, `audit/*`, `usage/*`), queries, version source, mocks | 4 |
| `src/Platform/ui.admin/src/test/**` | Unit, e2e, accessibility and visual tests | 4 |
| `docs/Platform/Storyteller/key-values.md` (new), `binding.md`, `authentication.md`, `access.md`, `inheritance.md`, `definitions.md`, `templating.md`, `src/Platform/ui.admin/README.md`, `sdk.typescript/README.md` | Documentation | 1a–4 |
