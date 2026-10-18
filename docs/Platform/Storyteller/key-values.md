# Key/Value Pairs

Storyteller stores named values that configurations and applications share: endpoints, switches, connection strings, API keys. Every pair belongs to one organization, project and view, like configurations, schemas and templates. Public pairs are plaintext; secrets are envelope-encrypted with keys that live in Azure Key Vault. One catalog lists both kinds, and a configuration binds any of them with `@key`.

The spec is [Native Key-Value Storage](specs/2026-10-11%20Native%20Key-Value%20Storage.md). Phase 1a (storage, the Standard level, lifecycle, import and export, binding) is reviewed in [Phase 1a of Native Key-Value Storage](specs/reviews/2026-10-12%20Phase%201a%20of%20Native%20Key-Value%20Storage.md), Phase 1b (the project policy, the HSM, BYOK and client-side levels, references, rotation, secret delivery, audit and usage) in [Phase 1b of Native Key-Value Storage](specs/reviews/2026-10-13%20Phase%201b%20of%20Native%20Key-Value%20Storage.md), Phase 2 (the API, the SDKs) in [Phase 2 of Native Key-Value Storage](specs/reviews/2026-10-13%20Phase%202%20of%20Native%20Key-Value%20Storage.md), Phase 3 (the CLI) in [Phase 3 of Native Key-Value Storage](specs/reviews/2026-10-13%20Phase%203%20of%20Native%20Key-Value%20Storage.md), and Phase 4 (the admin UI) in [Phase 4 of Native Key-Value Storage](../ui.admin/specs/reviews/2026-10-13%20Phase%204%20of%20Native%20Key-Value%20Storage.md).

## Identity

A pair is identified by **organization + project + view + key**. Views are isolated: the same key in two views is two pairs with their own values and histories.

### Keys

A key is a binding path: segments of letters, digits and `_` that start with a letter or `_`, joined by `.`, at most 128 characters (`^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$`). `db.password` and `api_v2.host` are keys; `db-password` and `1st.key` are not, because the binding language could not address them. Every key can therefore be written as `@db.password` in a configuration.

Keys keep their casing but are unique case-insensitively. `DB.Password` and `db.password` are the same pair, shown with the casing of its first write. A key that breaks the rule is refused with `400 InvalidKey`.

### Values

A value is a string of at most 24,576 bytes in UTF-8 (`400 ValueTooLarge` otherwise; `400 ValueRequired` when it is missing). The empty string is allowed. An optional content type of up to 255 characters (`400 InvalidContentType` otherwise), such as `text/plain` or `application/json`, tells clients how to render the value; it is not validated.

Two content types mark a **reference**: `application/vnd.2s.secret-reference` and the Azure App Configuration format `application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8`. A reference is a public pair whose value points to a secret in the customer's own vault. The platform never resolves it; the consuming application does, with its own identity.

### Protection levels

| Level | Where the value is | Who reads it | Server-side features |
| --- | --- | --- | --- |
| `Public` | Plaintext in Cosmos DB | Every reader of the project | All |
| `Standard` | Envelope-encrypted in Cosmos DB; the data key is wrapped by a software key of the project in a Standard vault | Callers with secret rights | All |
| `Hsm` | As Standard, with an RSA-HSM key of the project in Key Vault Premium, or in the organization's own Managed HSM | Callers with secret rights | All, plus key rotation on the platform's side |
| `Byok` | As Standard, with the customer's own key in the customer's vault and tenant | Callers with secret rights, while the customer allows it | All, plus the customer's revocation and the customer's own audit log |
| `Hyok` | A JWE document the client encrypted for the project's recipients; the platform cannot read it | Holders of a recipient's private key | Storage, versions, listing, binding of the document. No preview, diff or server-side level change |

Everything except `Public` is a secret. The level is chosen when a key is created and is required then, so a secret never becomes public because a client left the field out. An update without a level keeps the level. The [project policy](#project-policy) decides which levels a project allows; a level it does not allow answers `409 ProtectionNotAllowed`.

A write that turns a public pair into a secret or a secret into a public pair needs secret rights, because the old versions keep their old level until they expire.

### Changing the level of a key

`ProtectAsync` (`POST …/key-values/protect` in Phase 2) moves keys between the server-side levels `Standard`, `Hsm` and `Byok`: the current value is decrypted and written again under the target level as a **new version**. Older versions keep their level until they expire, so the history shows both. It takes a list of keys or a prefix, and each key gets its own outcome. `Public` and `Hyok` cannot be reached or left this way (`NotAllowed`, `ReprotectNotPossible`): they need a new value from the client through a normal write.

## Project policy

`KeyValuePolicy` lives on the project's access point (the `core` container, next to the machine authentication policy) and is cached for a minute. Without a stored policy the defaults apply: `Public` and `Standard` allowed, `Standard` the default secret level, and secrets delivered to machines only over mTLS.

| Member | Meaning |
| --- | --- |
| `AllowedProtections` | The levels a write may use; `Public` can never be removed. Disabling a level that keys of the project still carry (active or deleted) fails with `409 ProtectionInUse` and the counts per view |
| `DefaultSecretProtection` | A secret level the clients offer as default; it must be allowed |
| `RequireMtlsForSecretDelivery` | Machines receive secret values only with an mTLS certificate (see [Secret delivery](#secret-delivery)) |
| `Standard`, `Hsm.Key` | Read-only: the project's key-encryption keys, their current version, the next rotation |
| `Hsm.Backend`, `Hsm.RotationInterval` | `KeyVaultPremium` or `ManagedHsm` (read-only), and how often the platform rotates the HSM key |
| `Byok` | The customer's key: `KeyUri` (versionless) and `TenantId`, plus the read-only `VerifiedAt`, `CurrentKeyVersion`, `Health`, `LastCheckedAt`, `LastError` |
| `Recipients` | The RSA public keys that client-encrypted values are made for |
| `Rewrap` | Read-only: the re-wrap in progress after a rotation |
| `Version` | Optimistic concurrency: an update with `ExpectedVersion` fails with `409 PolicyVersionMismatch` when it is stale |

Readers see the policy; Administrators change it (`IKeyValuePolicyService`: `UpdateAsync`, `VerifyByokAsync`, `RotateAsync`, the recipient methods). Every change writes a `PolicyChange` audit event. Enabling a level has requirements:

- `Hsm`: an HSM provider is configured; the project's HSM key is created and recorded, and the backend is noted.
- `Byok`: the customer key is set and verified (`VerifiedAt`), otherwise `409 ByokNotConfigured`.
- `Hyok`: at least one enabled recipient.

### The HSM level

One RSA-HSM 2048 key per project, named like the Standard key, in the Premium vault `AzureKeyVaults:{KeyValues:Protection:Hsm:VaultName}` (`premium` by default), or in the Managed HSM of the organization when `KeyValues:Protection:Hsm:OrganizationManagedHsms:{organization}` names one. Managed HSM pools are provisioned by hand (the pool, the security domain with a 3-of-5 quorum, the local roles `Managed HSM Crypto Officer` and `Managed HSM Crypto User` for the platform identity). RSA-2048 is the default because HSM RSA-3072 allows 500 operations per 10 seconds per vault, RSA-2048 2,000. Lowkey Vault has no HSM-protected keys, so emulator mode creates software keys for this level too.

### The BYOK level

The customer's key stays in the customer's vault and tenant. The platform reaches it as a **multi-tenant Entra application** (`KeyValues:Protection:Byok:ClientId`) whose federated credential is the Functions' **user-assigned managed identity** (`ManagedIdentityClientId`): `ClientAssertionCredential` exchanges the managed identity's token for `api://AzureADTokenExchange` for a token of the customer's tenant, so no client secret exists. Setup, as the admin UI and the CLI show it (`GetByokSetup`):

1. The customer's administrator consents to the application (`https://login.microsoftonline.com/organizations/adminconsent?client_id=…`), which creates its service principal in their tenant.
2. The customer grants that service principal **Key Vault Crypto Service Encryption User** on the key, and lets the platform reach the vault: an approved private endpoint, or a firewall rule for the platform's static egress IP.
3. An Administrator sets `Byok.KeyUri` and `Byok.TenantId` in the policy; the key version in the URI is dropped.
4. `VerifyByokAsync` checks that the key is enabled, RSA of at least 2048 bits, allows `wrapKey` and `unwrapKey`, and that a wrap and unwrap round trip of 32 random bytes works. It records `VerifiedAt`, `CurrentKeyVersion` and `Health`; a failed check answers `424 CustomerKeyInvalid`.

When the customer disables or deletes the key, or removes the role, the vault answers 401, 403 or 404 and every BYOK read of an uncached version, write and binding of the project fails with `424 CustomerKeyUnavailable`. The daily check records `Health = Unavailable` with the error and writes a `ByokUnavailable` audit event once. Cached plaintexts keep working for at most `KeyValues:Protection:Byok:CacheDuration` (5 minutes): that is the revocation latency. The customer rotates the key; the daily check notices the new version and starts a re-wrap, and the customer must keep the old version enabled until the re-wrap is complete. Wrap and unwrap operations on the customer's key are billed by Azure to the customer. The Byok settings cannot be removed or changed while the level is allowed (`409 ProtectionInUse`).

### The client-side level (HYOK)

Values are encrypted before they reach the platform, for the RSA public keys registered as the project's recipients, and only the holder of a recipient's private key reads them. The format is JWE General JSON Serialization (RFC 7516): `A256GCM` content encryption, one `RSA-OAEP-256` entry per recipient with the recipient id as `kid`, and the protected header with `cty` and `2s` = `{organization}/{project}/{view}/{lower-cased key}`. The server validates a document without decrypting it: the format, `enc`, the place, and that every `kid` is an enabled recipient (`400 InvalidClientEncryptedValue`, `400 UnknownRecipient`); the recipient ids are stored for display.

Recipients are RSA keys of at least 3072 bits, added as PEM or JWK, at most 20 per project, with an id of 1 to 64 letters, digits, `_` or `-` (generated when missing). The last enabled recipient cannot be disabled or removed while `Hyok` is allowed. Adding a recipient does not give it access to existing values, and removing one does not take access back: the values have to be encrypted again by a client that holds a recipient's private key.

The project `KeyValues.ClientEncryption` (no dependencies beyond the BCL) has `ClientSecretEncryptor.Encrypt(value, recipients, place)`, `ClientSecretDecryptor.Decrypt(jwe, recipientId, privateKey)`, `JweDocument.Parse` and `RsaPublicJwk` for the key formats. The C# SDK, the CLI and the TypeScript SDK build on it in the next phases.

### References

A reference is a public pair with the content type `application/vnd.2s.secret-reference` and a value `https://{vault}.vault.azure.net/secrets/{name}[/{version}]` or the shorthand `kv://{vault}/{name}[/{version}]`, or with Azure App Configuration's `application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8` and its `{ "uri": … }` document. The shape is checked on write (`400 InvalidSecretReference`); a reference on a secret level is refused too, because the platform never resolves a reference. A binding inserts the value as the text it is, and the consuming application resolves it with its own identity.

## Rotation and re-wrap

The Standard and HSM keys carry a Key Vault rotation policy (`RotationInterval`, 365 days by default), and an Administrator rotates now with `RotateAsync` (`POST …/policy/rotate`), which also starts a **re-wrap**. New writes always wrap with the current key version. A re-wrap (`IKeyValueMaintenanceService.RewrapAsync`) finds every envelope of the level whose key id is not the current version, unwraps its data key with the old version and wraps it with the new one; the ciphertext does not change. The patch keeps the item's `ttl` (Cosmos counts it from the last write) and skips items that changed meanwhile. Progress is in `KeyValuePolicy.Rewrap`. Old key versions are never disabled; an unused HSM key version stops costing after 30 days.

`KeyValueProtectionTimer` (hourly) runs `IKeyValueMaintenanceService.RunAsync`: for every project with a stored policy, the daily key check (at most once per 20 hours) and then the pending re-wrap. The check makes sure of the free Standard key, reads the current version of the HSM key once the level was enabled, records the customer key's health, and starts a re-wrap when a version changed. Timer triggers need `AzureWebJobsStorage`; the Functions host keeps serving HTTP when that storage is missing locally, and only the timers fail to listen.

## Secret delivery

A secret value is delivered by a reveal, a secret version or diff, an export with secrets, or a resolved configuration with secret bindings. `request.GetSecretRightsAsync` decides once per request:

| Caller | Secrets are delivered when |
| --- | --- |
| User | The role is `ContributorWithSecrets` or higher on the project, or the token has the scope `Configuration.Secrets`. HTTPS with the bearer token |
| Machine with an mTLS certificate (`PresentingCertificateThumbprint`) | The scope `Configuration.Secrets` |
| Machine with an API key or a bearer token only: client credentials, or the Storyteller token of a [federated machine](authentication.md#machine-access-with-workload-identity-federation), which never presents a certificate | The scope, and the project policy has `RequireMtlsForSecretDelivery = false` |

Machines get the scope through the machine access scopes `SecretsRead` (`Configuration.Read` and `Configuration.Secrets`) and `SecretsReadWrite` (plus `Configuration.ReadWrite`); see [authentication.md](authentication.md#the-machineaccess-object). A withheld resolved configuration leaves the secrets unbound and carries the header `X-2S-Secrets-Withheld: mtls-required`. Explicit requests (a reveal, a secret version or diff, an export with secrets, a write of a secret) answer `403 MtlsRequired`. Responses with a secret value are `Cache-Control: no-store`. The result is also the request's ambient caller (`KeyValueCallerContext`), which the audit trail and the usage meters use for binding reads.

Machine certificates are in Cosmos now (`CosmosClientCertificateStore`, partition `{project}.access`, id `mcert.{thumbprint}`, expiring 30 days after the certificate), so a certificate issued on one instance authenticates on every other one and survives a restart; hits are cached for 5 minutes and misses for 30 seconds, which bounds the revocation latency. `TransportSecurityMiddleware` refuses plain HTTP outside Development (`400 HttpsRequired`, with `X-Forwarded-Proto` honoured behind the platform's proxy) and sends `Strict-Transport-Security`.

## Audit trail

Every secret-related operation writes a `KeyValueAuditEvent` into the organization container, partition `{project}.audit`, id `kva.{time}.{guid}`, with `ttl` = `KeyValues:AuditRetention` (400 days): the operation, time, view, key, version, level, actor (an [`AuditActor`](audit-stamps.md#the-actor)), channel, correlation id (the Functions invocation id, also sent to Key Vault as `x-ms-client-request-id`) and a detail, never a value. Operations: `Create`, `Update`, `Delete`, `Restore`, `PermanentDelete`, `Purge`, `Reprotect`, `Reveal`, `Export`, `Import`, `PolicyChange`, `Rotate`, `Rewrap`, `ByokVerify`, `ByokUnavailable`, `RecipientAdded`, `RecipientChanged`, `RecipientRemoved`. Public writes are not audited. Binding reads of secrets are counted per actor, key and hour in memory and written as one `BindingRead` event with `Count` by the usage flush. `IKeyValueAuditLog.QueryAsync` pages through the events, newest first, filtered by view, key, operation and time.

## Usage meters

`IKeyValueUsageRecorder` counts operations in memory (`reads.api`, `reads.binding`, `reveals`, `writes`, `kms.standard`, `kms.hsm`, `kms.byok`); `KeyValueUsageFlushService` writes them every `KeyValues:Usage:FlushInterval` (60 seconds) and on shutdown into the day document `kvu.{yyyy-MM-dd}` of the project (organization container, partition `{project}.usage`, `ttl` 400 days). A crashed instance loses at most one interval. `KeyValueUsageSnapshotTimer` (daily at 00:10 UTC) counts the stored keys per level (`keys.public`, `keys.reference`, `keys.standard`, `keys.hsm`, `keys.byok`, `keys.hyok`), `versions` and `storage.bytes` of every project and writes them as gauges of the previous day.

`IKeyValueUsageService.GetReportAsync(from, to)` (at most 400 days) sums the counters and averages the gauges over the range (key-months for a calendar month). With `KeyValues:Pricing` configured (priced item → unit price; `reads` prices `reads.binding` and `reads.api` together per 10,000), the report also carries `EstimatedCharges`. Invoicing is out of scope.

## Storage

Pairs live in the organization container (`org.{organization}`), in the partition `{project}.kv.{view}`. Everything a request does with one view stays in that partition: a point read for a key, one single-partition query for a listing, and atomic transactional batches for writes.

| Item | id | Content |
| --- | --- | --- |
| Catalog item | `kv.{lower-cased key}` | The current version: key, protection, value or envelope, the `Created` and `Modified` [audit stamps](audit-stamps.md), `Deleted` while deleted |
| Version | `kvv.{lower-cased key}.{version}` | A version that is no longer current, with its own `ttl` |

Versions are separate items on purpose. A read of the current value costs about 1 RU whatever the history is, a listing loads catalog items only, and a new version costs one create and one replace in one batch. Embedding the history in the catalog item would make every read and every listing pay for it, and could not expire versions one by one.

The version items of one key are queried by the id prefix `kvv.{key}.` and carry `NormalizedKey` (the lower-cased key) for queries over many keys at once, such as the next-version lookup of an import.

### Lifecycle

| Operation | Effect |
| --- | --- |
| Create | Version 1, or one more than the highest version still stored, so numbering continues after an expired deleted key |
| Update | The current version becomes a version item with the rest of its lifetime as `ttl`; the catalog item gets `Version + 1`. The same value, content type and level is not written again |
| Delete | Soft. The current version is written to history, and the catalog item gets the `Deleted` stamp and a `ttl` of 90 days (`KeyValues:DeletedRetention`). The key is not found by reads and listings unless they ask for deleted keys |
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
    "AuditRetention": "400.00:00:00",
    "MaxRecipients": 20,
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
      },
      "Hsm": {
        "VaultName": "premium",          // a Key Vault Premium vault in AzureKeyVaults
        "KeySize": 2048,
        "RotationInterval": "365.00:00:00",
        "CacheDuration": "00:05:00"
      },
      "OrganizationManagedHsms": {       // organizations with a dedicated Managed HSM
        "acme": "https://acme.managedhsm.azure.net/"
      },
      "Byok": {
        "ClientId": "<multi-tenant app id>",
        "ManagedIdentityClientId": "<user-assigned identity client id>",
        "CacheDuration": "00:05:00",     // the revocation latency
        "RequiredRole": "Key Vault Crypto Service Encryption User"
      }
    },
    "Usage": { "FlushInterval": "00:01:00", "Retention": "400.00:00:00" },
    "Pricing": { "keys.standard": 0.10, "keys.hsm": 0.40, "reads": 0.05 }   // optional, for the estimates
  },
  "AzureKeyVaults": {
    "default": "https://<standard vault>.vault.azure.net/",
    "premium": "https://<premium vault>.vault.azure.net/"
  }
}
```

`AddCosmosDbAnnotations` registers the service, the envelope protector, the policy store and service, the maintenance service, the audit log, the usage recorder, store and service, and the Cosmos certificate store. `AddKeyVaultKeyProtection(configuration, environment)` (project `KeyValues.Azure.KeyVault`) registers the Standard provider when its vault is configured, the HSM provider when the Premium vault or a Managed HSM is configured, and the BYOK provider when `Byok:ClientId` is set (or in emulator mode). Production uses the managed identity (`ManagedIdentityCredential`, then `EnvironmentCredential`); other environments use `DefaultAzureCredential`. The API registers `KeyValueUsageFlushService` as a hosted service and the two timer functions.

### Local development

Microsoft ships no Key Vault emulator. The Aspire host runs [Lowkey Vault](https://github.com/nagyesta/lowkey-vault) (`nagyesta/lowkey-vault`) and passes its address to the API as `AzureKeyVaults__default` with `KeyValues__Protection__Emulator=true`. In emulator mode the clients use a dummy token, skip the challenge resource check, accept the self-signed certificate, and create their keys with the `encrypt` and `decrypt` operations too, because Lowkey Vault checks `decrypt` on an unwrap; the HSM level creates software keys, and the BYOK level treats Lowkey Vault as the customer's vault with the same dummy credential. Unwraps always name the key version, because Lowkey Vault has no versionless unwrap route. Emulator mode fails the startup in the Production environment.

The integration tests of `KeyValues.Azure.KeyVault/testint` start Lowkey Vault with Testcontainers, or use a running instance named by `LOWKEY_VAULT_URL`.

## Service

`IKeyValueService` (`Backend.Core/src/KeyValues/`) is implemented by `CosmosKeyValueService`. Every call takes the organization, project and view, and the writes take a `KeyValueCaller`: the actor (an [`AuditActor`](audit-stamps.md#the-actor)), `CanReadSecrets`, `CanWriteSecrets`, the channel and the correlation id. The API decides the rights; the service enforces them, because only the service knows whether a key is a secret. The service also checks the project policy, validates references and client-encrypted values, writes the audit events and counts the usage.

| Method | Behaviour |
| --- | --- |
| `ListAsync` | One page of catalog items as `KeyValueSummary` (key, level, content type, version, `Modified`, deleted state, expiry), filtered by key prefix, levels and state (`Active`, `Deleted`, `All`), ordered by key, with a continuation token. Values of public pairs only with `IncludeValues`; values of secrets never |
| `GetAsync` | The current version. A secret's value only with `Reveal` and `CanReadSecrets` (`403` otherwise); a deleted key only with `IncludeDeleted` |
| `SetAsync` | Create or update, see the lifecycle |
| `DeleteAsync` | Soft, or permanent with `Permanent`. False when the key does not exist |
| `RestoreAsync`, `PurgeAsync`, `ProtectAsync` | Bulk, by a list of at most 100 keys or by a prefix of at most 1,000 keys. Each key gets its own outcome: `Done`, `Unchanged`, `NotFound`, `Forbidden`, `NotAllowed` or `Failed`. Protect changes the level between the server-side levels and writes a new version; public and client-encrypted pairs need a new value from the client |
| `GetVersionsAsync` | Every stored version with its level, `Modified` (who wrote it and when), expiry and `IsCurrent` |
| `GetVersionAsync`, `GetVersionChangesAsync` | One version, and the text diff of two versions (version 0 is the empty text). Secrets need `CanReadSecrets` |
| `ExportAsync`, `ImportAsync` | The document format below |
| `LookupAsync` | For the binding sources: the current value, or null when missing, deleted, or a secret without secrets included |

Error codes of this area: `InvalidKey`, `ValueRequired`, `ValueTooLarge`, `InvalidContentType`, `ProtectionRequired`, `ProtectionNotAllowed` (409), `ProtectionInUse` (409), `ProtectionNotConfigured` (501), `KeyValueNotFound`, `KeyValueBusy` (409), `KeyValueVersionMismatch` (412), `PolicyVersionMismatch` (409), `KeyEncryptionUnavailable` (503 with `Retry-After`), `CustomerKeyUnavailable` (424), `CustomerKeyInvalid`, `ByokNotConfigured` (409), `MtlsRequired` (403), `HttpsRequired` (400), `ImportInvalid` (400 with the failing items in `Details`), `SecretValueMissing`, `NotSupportedForProtection`, `ReprotectNotPossible`, `InvalidClientEncryptedValue`, `UnknownRecipient`, `RecipientExists`, `RecipientNotFound`, `RecipientInvalid`, `InvalidSecretReference`, `BodyTooLarge` (413 for a body larger than its route reads: 512 KiB for a write, 28 MiB for an import, 64 KiB for a bulk request or a new recipient, 8 KiB for the other bodies), and `AccessDenied` for missing secret rights.

## API

Every endpoint is under `v1/{organization}/{project}` and needs a bearer token (or a machine credential); the roles and scopes are in the table at the end. Bodies and responses are JSON with PascalCase names; the model types are the ones of the [service](#service). Values never travel in a URL: export and import are `POST`s, and the collection actions are `POST` only, so a key named `restore` or `export` is still reached with `GET`, `PUT` and `DELETE`.

### Pairs of a view

Base `K` = `v1/{organization}/{project}/{view}/key-values`.

| Method | Route | Query | Body → response | Errors |
| --- | --- | --- | --- | --- |
| `GET` | `K` | `keyPrefix`, `protection` (repeatable or comma-separated), `state` (`Active`, `Deleted`, `All`), `includeValues`, `continuationToken` | → `KeyValuesResponse`, ordered by key, up to 1,000 per page | 400 |
| `GET` | `K/{key}` | `reveal`, `includeDeleted` | → `KeyValue`; a secret's value only with `reveal=true` and secret rights | 403, 404, 424, 503 |
| `PUT` | `K/{key}` | | `KeyValueWrite` → `KeyValue` without a secret value | 400, 403, 409, 412, 413, 424, 501, 503 |
| `DELETE` | `K/{key}` | `permanent` (Administrator), `expectedVersion` | → `204` | 403, 404, 409, 412 |
| `POST` | `K/restore` | | `KeyValueBulkRequest` → `KeyValueBulkResult` | 400, 403, 413 |
| `POST` | `K/purge` | | `KeyValueBulkRequest` → `KeyValueBulkResult` (Administrator) | 400, 403, 413 |
| `POST` | `K/protect` | | `KeyValueBulkRequest` with `Protection` → `KeyValueBulkResult` | 400, 403, 409, 413, 424, 501, 503 |
| `GET` | `K/{key}/versions` | | → `KeyValueVersion[]`, oldest first | 404 |
| `GET` | `K/{key}/versions/{version}` | `reveal` | → `KeyValue` | 403, 404, 424, 503 |
| `GET` | `K/{key}/versions/{version}/diff`, `…/diff/{versionFrom}` | `format=unified` | → `DiffResult`, or `text/plain`; version 0 is the empty text | 400, 403, 404, 424 |
| `POST` | `K/export` | | `KeyValueExportRequest` (`KeyPrefix`, `Protections`, `IncludeSecrets`; an empty body or `null` exports the whole view, secrets without their values) → `KeyValueDocument` | 400, 403, 413, 424, 503 |
| `POST` | `K/import` | `mode` (`Merge`, `Replace`), `dryRun`, `keyPrefix` | `KeyValueDocument` → `KeyValueImportResult` | 400, 403, 409, 413, 424, 501, 503 |

### Policy, recipients, audit and usage of a project

Base `P` = `v1/{organization}/{project}/key-values`.

| Method | Route | Body → response | Who |
| --- | --- | --- | --- |
| `GET` | `P/policy` | → `KeyValuePolicy` | Reader, machines with a read scope |
| `PUT` | `P/policy` | `KeyValuePolicyUpdate` (`AllowedProtections`, `DefaultSecretProtection`, `RequireMtlsForSecretDelivery`, `HsmRotationInterval`, `Byok.KeyUri`, `Byok.TenantId`, `ExpectedVersion`) → `KeyValuePolicy` | Administrator |
| `GET` | `P/policy/byok/setup` | → `ByokSetupInfo` (`ClientId`, `ConsentUrl`, `RequiredRole`) | Administrator |
| `POST` | `P/policy/byok/verify` | → `KeyValuePolicy` | Administrator |
| `POST` | `P/policy/rotate` | `{ "Protection": "Standard" \| "Hsm" }` → `KeyValuePolicy` | Administrator |
| `GET` | `P/recipients` | → `ClientEncryptionRecipient[]` | Reader, machines with a read scope |
| `POST` | `P/recipients` | `{ "Id"?, "Name", "PublicKey" }` (PEM or JWK) → `ClientEncryptionRecipient` | Administrator |
| `PATCH` | `P/recipients/{id}` | `{ "Name"?, "IsEnabled"? }` → `ClientEncryptionRecipient` | Administrator |
| `DELETE` | `P/recipients/{id}` | → `204` | Administrator |
| `GET` | `P/audit` | query `view`, `key`, `operation`, `from`, `to`, `continuationToken` → `KeyValueAuditResponse`, newest first | Administrator |
| `GET` | `v1/{organization}/{project}/usage/key-values` | query `from`, `to` (`yyyy-MM-dd`, the last 30 days by default, at most 400) → `KeyValueUsageReport` | Administrator |

The Administrator operations are for users only; a machine gets `403` whatever its scopes.

### Who may do what

| Operation | User, project role | Machine, any of these scopes |
| --- | --- | --- |
| List, get without reveal, versions, a public version or diff, export without secrets, policy, recipients | `Reader` | `Configuration.Read`, `Configuration.ReadWrite`, `Default.Read`, `Default.ReadWrite` |
| Reveal, a secret version or diff, export with secrets | `ContributorWithSecrets` | a read scope and `Configuration.Secrets`, under the mTLS rule |
| Set, delete or restore a public pair; import of public items | `Contributor` | `Configuration.ReadWrite`, `Default.ReadWrite` |
| Set, delete, restore or protect a secret; a change to or from a secret level; import with secrets | `ContributorWithSecrets` | a write scope and `Configuration.Secrets`, under the mTLS rule |
| Permanent delete, purge, policy changes, BYOK setup and verify, rotation, recipients, audit, usage | `Administrator` | never |

A machine restricted to an annotation gets `403 AnnotationRestricted` for every operation of this table that machines may call, because key/values belong to the whole view (the Administrator operations refuse every machine with `403 AccessDenied`); it receives the values that the configuration of its annotation binds, through the resolved configuration (see [Annotation restriction of machines](authentication.md#annotation-restriction-of-machines)). A caller without the rights to a secret gets `403 AccessDenied`; a machine whose only missing piece is the mTLS certificate gets `403 MtlsRequired`. Responses that carry a secret value are `Cache-Control: no-store`. A query parameter that does not parse is `400 InvalidQuery`.

### Examples

```bash
API=https://api.example.com/api/v1/acme/billing
AUTH="Authorization: Bearer $TOKEN"

# a public pair, then a Standard secret
curl -X PUT "$API/default/key-values/db.host" -H "$AUTH" -H "Content-Type: application/json" \
  -d '{ "Protection": "Public", "Value": "sql.internal" }'
curl -X PUT "$API/default/key-values/db.password" -H "$AUTH" -H "Content-Type: application/json" \
  -d '{ "Protection": "Standard", "Value": "s3cret", "ExpectedVersion": 0 }'

# the listing never shows a secret value; a reveal does, with the rights
curl "$API/default/key-values?keyPrefix=db.&includeValues=true" -H "$AUTH"
curl "$API/default/key-values/db.password?reveal=true" -H "$AUTH"

# soft delete, restore by prefix, history and a unified diff
curl -X DELETE "$API/default/key-values/db.password?expectedVersion=1" -H "$AUTH"
curl -X POST "$API/default/key-values/restore" -H "$AUTH" -H "Content-Type: application/json" -d '{ "KeyPrefix": "db." }'
curl "$API/default/key-values/db.password/versions" -H "$AUTH"
curl "$API/default/key-values/db.password/versions/2/diff?format=unified" -H "$AUTH"

# export with secrets (ContributorWithSecrets), import as a dry run
curl -X POST "$API/default/key-values/export" -H "$AUTH" -H "Content-Type: application/json" -d '{ "IncludeSecrets": true }' > kv.json
curl -X POST "$API/rollout/key-values/import?mode=Merge&dryRun=true" -H "$AUTH" -H "Content-Type: application/json" -d @kv.json

# the policy: allow HSM, move the secrets there, rotate
curl -X PUT "$API/key-values/policy" -H "$AUTH" -H "Content-Type: application/json" \
  -d '{ "AllowedProtections": ["Public", "Standard", "Hsm"], "DefaultSecretProtection": "Hsm" }'
curl -X POST "$API/default/key-values/protect" -H "$AUTH" -H "Content-Type: application/json" \
  -d '{ "KeyPrefix": "", "Protection": "Hsm" }'
curl -X POST "$API/key-values/policy/rotate" -H "$AUTH" -H "Content-Type: application/json" -d '{ "Protection": "Hsm" }'

# the audit trail of the last day, and the usage of September
curl "$API/key-values/audit?from=$(date -u -d yesterday +%Y-%m-%dT%H:%M:%SZ)" -H "$AUTH"
curl "$API/usage/key-values?from=2026-09-01&to=2026-09-30" -H "$AUTH"
```

### SDKs

The C# SDK (`Sdk.NSwag`, generated by NSwag) has `IKeyValuesApiClient` with one method per operation, registered by `AddStorytellerSdk`, and `KeyValueClientEncryption` (`ClientSideEncryption/`) to encrypt a value for the recipients of a project and to decrypt one with a recipient's private key, on top of `KeyValues.ClientEncryption`. The TypeScript SDK (`@42for.net/2splatform.sdk`) has the generated `key-values` functions, Vue Query composables, Zod schemas and MSW handlers, the helpers of `lib/key-values.ts` (key validation, `.env` and flat JSON conversion, format detection, secret references) in the root entry, and the subpath `./hyok` with `encryptForRecipients` and `decryptClientSecret` on WebCrypto; see [its README](../../../src/Platform/Storyteller/sdk.typescript/README.md).

## CLI

`sform story kv` (also `sform story key-values`) works in the context of the signed-in user: the organization, project and view of `sform` (`-p <organization>.<project>` and `-v <view>` override them). The project commands (`policy`, `recipients`, `audit`, `usage`) ignore the view. The API decides the rights, so a Reader lists and reads public pairs, a `ContributorWithSecrets` reveals and writes secrets, and an Administrator changes the policy; a refusal prints the message and the error code of the API, and a `403 MtlsRequired` says that a machine needs its certificate.

```text
sform story kv [--prefix <prefix>] [--protection <level>]... [--values] [--deleted|--all] [-k <token>]
sform story kv get <key> [--reveal] [--version <n>] [--deleted] [--value-only] [-e <file>] [--private-key <pem> [--kid <id>]]
sform story kv set <key> [<value>] [--protection <level>|--secret] [--stdin] [-i <file>] [--content-type <type>] [--reference] [--expected-version <n>]
sform story kv delete|remove <key> [--permanent [-y]] [--expected-version <n>]
sform story kv restore <key>... | --prefix <prefix>
sform story kv purge <key>... | --prefix <prefix> [-y]
sform story kv protect <key>... | --prefix <prefix> --protection standard|hsm|byok
sform story kv versions <key>
sform story kv diff <key> [to] [from]
sform story kv export [-o <file>] [--format json|flat|env] [--prefix <prefix>] [--protection <level>]... [--include-secrets]
sform story kv import <file> [--format json|flat|env] [--secret <glob>]... [--protection <level>] [--mode merge|replace] [--prefix <prefix>] [--dry-run] [-y]
sform story kv reencrypt <key>... | --prefix <prefix> --private-key <pem> --kid <id>
sform story kv policy
sform story kv policy set [--allow <level>]... [--default <level>] [--require-mtls true|false] [--byok-key <uri>] [--byok-tenant <id>] [--hsm-rotation-days <n>] [--expected-version <n>]
sform story kv policy byok-setup | byok-verify | rotate --protection standard|hsm
sform story kv recipients
sform story kv recipients add --name <name> --public-key <pem|jwk file> [--id <kid>] | enable <id> | disable <id> | remove <id> [-y]
sform story kv audit [--key <key>] [--operation <op>] [--from <time>] [--to <time>] [-k <token>] [-v <view>]
sform story kv usage [--from <yyyy-MM-dd>] [--to <yyyy-MM-dd>]
```

**Listing and reading.** The listing shows the key, the level, the version, the author and the time; `--values` adds the values of public pairs and masks secrets as `••••••`; `--deleted` or `--all` adds when a deleted key expires. `get` prints the pair as JSON; `--reveal` asks for a secret's value, `--value-only` prints the value alone for scripts, and `--version` shows one stored version. A client-encrypted (`hyok`) value is a JWE document; with `--private-key <pem>` (a file or the PEM text) the CLI decrypts it locally, and `--kid` names the recipient when the document has several.

**Writing.** `set` creates or updates a pair as a new version. The level comes from `--protection`, or `--secret` for the project's default secret level; without either a new key is public and an existing key keeps its level. A secret without a value source is asked for with hidden input; `--stdin` and `-i <file>` are the other sources, and a secret given as an argument prints a warning, because it stays in the shell history. `--reference` stores a public reference to a secret in your own Key Vault (`kv://<vault>/<name>` or the vault URI). A `hyok` value is encrypted on your machine for the enabled recipients of the project before it is sent; the platform never sees it.

**Lifecycle.** `delete` soft-deletes and prints the restore command; `--permanent` removes the key with every version and asks you to type the key unless `-y` is given. `restore`, `purge` and `protect` take keys or `--prefix` (an empty prefix means every key) and print the outcome per key; when a key was not done, the exit code is `45` (`WARNING_PARTIAL_RESULT`). `purge` asks for a confirmation, the view name for a prefix. `protect` moves keys between the server-side levels; public and client-encrypted pairs need a new value through `set`.

**Files.** `export` and `import` speak three formats: `json` is the `2s.key-values.v1` document with the levels, `flat` is one JSON object of `key: "value"`, and `env` is `.env` lines (`KEY=VALUE`, `#` comments, an optional `export`, quoted values with `\n`, `\"` and `\\` escapes; `db.password` becomes `DB__PASSWORD`). The format is guessed from the file name and the content when `--format` is missing. `flat` and `env` carry no levels: keys matching a `--secret` glob (`*` and `?`) get `--protection`, or the project's default secret level, and the other keys are public; secrets exported without `--include-secrets` have no value and are left out of `flat` and `env` with a note. An import shows the plan first (a dry run on the server), asks for a confirmation unless `-y` is given, and a `replace` that deletes keys asks you to type the view name. The exit code is `45` when an item failed.

**Recipients and re-encryption.** `recipients add` registers an RSA public key of at least 3072 bits (PEM or JWK). A new recipient cannot read the existing `hyok` values: `reencrypt` decrypts them with the private key of a current recipient (`--private-key`, `--kid`) and writes them again for everybody enabled, one new version per key. `recipients disable` keeps a recipient out of new values; `remove` takes it away (the values it could already read stay readable with its private key).

**Policy and keys.** `policy` prints the project's policy; `policy set` changes the given members only (`--allow` lists every allowed level, `public` is always allowed). `policy byok-setup` prints the consent link and the role a customer grants on their key, `policy byok-verify` checks the key set with `--byok-key` and `--byok-tenant`, and `policy rotate --protection standard|hsm` rotates a platform key and starts the re-wrap, whose progress `policy` shows under `Rewrap`.

```bash
sform story kv set db.host sql.internal                         # public, new key
sform story kv set db.password --secret                         # asks for the value with hidden input
cat token.txt | sform story kv set api.token --protection hsm --stdin
sform story kv set db.password kv://my-vault/db-password --reference
sform story kv --prefix db. --values
sform story kv get db.password --reveal --value-only
sform story kv versions db.password && sform story kv diff db.password
sform story kv delete db.password && sform story kv restore db.password
sform story kv export -o prod.env --include-secrets             # .env with the secrets, needs the rights
sform story kv import staging.env --secret "*password*" --secret "*.token" --dry-run
sform story kv import kv.json --mode replace --prefix db.       # asks for the view name because keys get deleted
sform story kv policy set --allow public --allow standard --allow hsm --default hsm
sform story kv protect --prefix "" --protection hsm             # every secret to HSM, new versions
sform story kv recipients add --name api --public-key api.pem && sform story kv set db.password --protection hyok
sform story kv reencrypt --prefix "" --private-key worker.pem --kid worker
sform story kv audit --operation reveal --from 2026-10-01
sform story kv usage --from 2026-09-01 --to 2026-09-30
```

## Admin UI

The admin UI (`src/Platform/ui.admin`) has a **Values store** section in every view. The project's **Settings** end with a **Secrets** section, and **Monitor** in the project section of the sidebar has the **Usage** and **Audit** tabs.

- **Values store** lists the pairs with the key, a level badge (an icon and a text, so colour is never the only signal; references carry a link icon), the first line of a public value or `••••••••`, the version, the author and the time. The toolbar searches keys and public values, filters by level and switches between the active and the deleted keys; **New key**, **Import** and **Export** sit in the header. A row opens the detail beside the list; its menu copies the key or the binding (`@key`), reveals a secret, deletes the key (restorable for 90 days) or, for Administrators, deletes it for good after typing the key. Selected rows get Delete and Change protection, or Restore and Purge in the deleted state; every bulk call reports the outcome per key and keeps the failed keys selected. When the customer key is unavailable, a banner across the section says so and links to the Secrets section of Settings.
- **The detail** shows the metadata and the binding to copy (`@key`, `@public(key)` or `@secret(key)`). A public value is edited in place (JSON in the code editor when the content type is JSON); a server-side secret shows a masked field with **Reveal** (the value is fetched on request, never cached, and masked again after 60 seconds or when the panel closes) and **Set new value**, where the level can change too; a HYOK value shows its recipients, and **Set new value** encrypts in the browser for the enabled recipients, so the plaintext never leaves. Saves send the expected version: a concurrent change offers Reload or Overwrite, a level the policy does not allow points to the Secrets section of Settings, and a missing customer key is explained. The **Versions** tab loads only when it opens and shows the level of every version, with View and Compare (a secret only after an explicit reveal); HYOK shows the timeline alone.
- **Import** takes a file or pasted text (a key/value document, `.env` or JSON, detected), shows the rows with a level each, marks keys matching a glob as a secret level, runs the server's dry run as **Check**, imports after a confirmation (Replace with deletes asks for the view name), and offers **Retry failed** and **Undo deletes** afterwards. HYOK rows are encrypted in the browser first. **Export** downloads the document, `.env` or flat JSON through a Blob URL; secret values only on request, with a warning.
- **Secrets**, the last section of the project's **Settings** (every reader sees it, Administrators change it): the allowed levels as toggles with their requirement and cost hint (disabling a level in use shows the counts per view with links), the default secret level, the mTLS rule for machines (turning it off asks for a confirmation), the Standard and HSM keys with their versions, the next rotation, **Rotate now** and the re-wrap progress, the BYOK wizard (consent link, the role and the `az` command, the key URI and tenant, Verify and the health) and the recipients (add a PEM or JWK, enable, disable, remove, with the re-encryption warnings).
- **Monitor** (Administrators) has two tabs. **Usage**, the default, shows the totals of a date range per meter, the estimated charges when the deployment has prices, and a daily chart of reads and keys. **Audit** (`?tab=audit`) lists the secret events newest first with filters (view, key, operation, time) and paging; the correlation id is copyable to match the vault logs.

The capabilities follow the roles the API enforces: `edit` (Contributor) for public writes, `secrets` (ContributorWithSecrets) for secret writes, reveals, protection changes and secret export, and `manageAccess` (Administrator) for permanent deletes, purges, the policy, the audit trail and usage. In the mock mode (`npm run dev:mock`), `acme/billing` has seeded pairs of every level, a recipient with a real public key, an audit trail and usage; `northwind/portal` shows the Reader experience.

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
- Each key is looked up once per resolved read, and a changed pair takes effect on the next read. Nothing is cached across reads. A conditional read (`If-None-Match`) of a document with bindings looks the keys up too: its 304 saves the transfer, not the lookups ([conditional reads](inheritance.md#conditional-reads)).
- A vault that keeps throttling fails the read with `503`; a secret is never silently left unbound.

A vault entry of `AzureKeyVaults` named `default`, `public` or `secret` is still a vault, but no longer a raw binding source. Give such a vault another name to read it by its native secret names, for example `@(db.password, shared)`.

## Request charges

Measured on the Cosmos DB emulator with the default indexing policy and small values (the Phase 1a review has the numbers): a point read of a key is about 1 RU, a create about 6-7 RU, an update or a soft delete (history plus catalog item in one batch) about 15-20 RU, and a listing page a few RU plus the loaded rows. The value paths are indexed like everything else; excluding them is left for when a measurement shows a difference (spec D7).
