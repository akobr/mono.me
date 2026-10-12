# Phase 1a of Native Key-Value Storage

## Overview

Phase 1a of [Native Key-Value Storage](../2026-10-11%20Native%20Key-Value%20Storage.md) (sections 1–10) delivers the storage and the data model of key/value pairs, item-level TTL on the containers, envelope encryption with the `Standard` level backed by Azure Key Vault, the lifecycle (versions, soft delete, restore, permanent delete and purge), import and export, and the native store as the `default`, `public` and `secret` binding sources. It adds the project `KeyValues.Azure.KeyVault` with its integration tests on Lowkey Vault, and wires the Aspire host and the Functions API. The API endpoints (Phase 2), the other protection levels and the project policy (Phase 1b), the CLI (Phase 3) and the admin UI (Phase 4) are not part of it.

Every test project that the change touches is green: `Backend.CosmosDb.UnitTests` 265 (34 of them new), `KeyValues.Azure.KeyVault.IntegrationTests` 6, `Binding.Language.UnitTests` 103 (6 new), `Binding.Object.UnitTests` 194 and `Api.Functions.UnitTests` 146. The solution builds except `src/Crumble/Playground/Examples`, which fails on an unrelated, pre-existing error (`IChainedCrumbsExecutor` not found).

## What Was Done

### 1. Identity, keys, values and protection

- `Abstractions.Annotations/src/Model/KeyValues/`: `KeyValueProtection` (`Public`, `Standard`, `Hsm`, `Byok`, `Hyok`, with `IsSecret()` and `IsServerSideEncrypted()`), `KeyValueState`, `KeyValueContentTypes` (the two reference content types and `IsReference`), `KeyValue`, `KeyValueSummary` with `KeyValuesResponse`, `KeyValueWrite`, `KeyValueVersion`, the bulk types (`KeyValueBulkRequest`, `KeyValueBulkOutcome`, `KeyValueBulkItemResult`, `KeyValueBulkResult`), `KeyValueDocument` with `KeyValueDocumentItem`, and the import types (`KeyValueImportMode`, `KeyValueImportAction`, `KeyValueImportItemResult`, `KeyValueImportResult`).
- `Backend.Core/src/KeyValues/KeyValueRules.cs`: the key pattern `^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*\z` with 128 characters at most, `Normalize` (lower-case), the value limits (24,576 UTF-8 bytes; 49,152 characters for `Hyok`), and the content type limit.
- **Deviation:** `Protection` is required on create (`400 ProtectionRequired`), as decided in the second round of answers, so a client cannot create a secret as public by leaving the field out. An update without it keeps the level.
- **Deviation:** the `IsReference` flag and the content type constants exist already, because the models carry them; the validation of reference values is Phase 1b, as planned.

### 2. Cosmos data model

- `Backend.CosmosDb/src/Entities/KeyValues/KeyValueEntity.cs` and `KeyValueVersionEntity.cs`, the id prefixes `kv` and `kvv` in `EntityIdPrefixTypes`, and the partition `{project}.kv.{view}` in `PartitionKeys.GetKeyValues` / `GetCosmosKeyValues`.
- `StoredValue` and `EncryptedValue` live in `Backend.Core/src/KeyValues/Protection/StoredValue.cs`, not in the entities, because the protector (`Backend.Core`) produces them and the entities embed them. `StoredValue.Length` keeps the plaintext length for display and later metering.
- Two additions to the spec'd entities: `KeyValueEntity.ETag` maps `_etag`, so items loaded by a query (the import) can be replaced conditionally, and `KeyValueVersionEntity.NormalizedKey` holds the lower-cased key, so an import finds the next versions of many keys in one `GROUP BY` query instead of one query per key. Without that, an import of 1,000 new keys was throttled by the emulator at 1000 RU/s.
- No state item. A create takes `max(stored versions) + 1`; after a permanent delete or purge nothing is left and the numbering starts at 1, as the spec says.

### 3. Container TTL

- `ContainerFactory.CreateContainerIfNotExistsAsync` creates containers with `DefaultTimeToLive = -1`. After the rebase on `main`, the container settings (TTL and the audit index exclusion) come from `main`'s `EnsureContainerSettingsAsync` / `EnsureOrganizationContainersAsync`; the `GetContainerNamesAsync` and `EnableTimeToLiveAsync` methods of this phase were dropped as duplicates.
- `DbCreator`: the plain `2sInitDb` run (which the Aspire `db-creator` resource runs) brings every existing organization container up to date, so no separate `enable-ttl` subcommand is needed.
- Found on the way: `2sInitDb` could not start at all in a Debug build. The Development environment validates the service provider on build, and `AddCosmosDbAnnotations` registers the certificate renewal service of the API, whose `IClientCertificateAuthority` the tool never registers. `Program.cs` now turns that validation off for the tool, which resolves only the container factory and the structure builder.
- `definitions.md` and `templating.md` state that the 365-day history expiry is enforced from now on. `inheritance.md` does not describe history expiry, so it is unchanged.

### 4. Indexing (D7)

Not applied. The measured request charges (section 9) do not show a value-path cost worth a container policy change for small values; the decision stays open for a measurement with 24 KB values.

### 5. Envelope encryption

- `Backend.Core/src/KeyValues/Protection/`: `KeyValueScope`, `KeyValueIdentity`, `WrappedDataKey`, `IKeyEncryptionProvider`, `IValueProtector` and `EnvelopeValueProtector`. The protector encrypts with `AesGcm` (256-bit data key, 96-bit nonce, 128-bit tag), binds the ciphertext with the associated data `2s.kv.v1|org|project|view|key|version|level`, wraps the data key through the level's provider with `RSA-OAEP-256`, zeroes the data key and the plaintext buffers, and caches decrypted values in its own `MemoryCache` (sliding `CacheDuration`, `CacheSizeLimit` in plaintext bytes). A failed tag check is `KeyValueIntegrityException` (500). A level without a provider is `501 ProtectionNotConfigured`; `Hyok` answers the same until Phase 1b.
- **Deviation:** `IKeyEncryptionProvider` has no policy parameter yet (`EnsureKeyAsync`, `WrapAsync`, `UnwrapAsync` take the `KeyValueScope`). Phase 1b adds the policy when the HSM and BYOK providers need it.
- **Deviation:** the Standard key name is derived, `kek-` plus 32 hex characters of SHA-256 of `{organization}/{project}`, instead of a random name stored in the policy. The provider therefore needs no stored state in Phase 1a, and the name is still unique per project and valid for Key Vault. Phase 1b can keep it and only report the key id in `ProtectionKeyInfo`.
- `Backend.Core` references `Microsoft.Extensions.Caching.Memory` and `Microsoft.Extensions.Logging.Abstractions` now.

### 6. The new project `KeyValues.Azure.KeyVault`

- `KeyVaultStandardKeyProvider`: one `KeyClient`; `EnsureKeyAsync` creates the RSA key with `wrapKey` and `unwrapKey` (plus `encrypt` and `decrypt` in emulator mode, because Lowkey Vault checks `decrypt` on an unwrap) and sets a rotation policy of `RotationInterval` as a best effort, once per process per project; `WrapAsync` uses the latest key version through `KeyClient.GetCryptographyClient(name)` and returns the versioned key id; `UnwrapAsync` parses the id with `KeyVaultKeyIdentifier`, refuses another vault or algorithm (`KeyValueIntegrityException`), and unwraps with the recorded version. A `SemaphoreSlim` of `MaxConcurrentKeyOperations` bounds the parallel calls; `429` and `5xx` become `ServiceUnavailableException` (`KeyEncryptionUnavailable`) with the `Retry-After` of the response.
- `KeyVaultClients.CreateKeyClient`: the credential choice of `AddAzureKeyVaultBindings` (managed identity then environment in production, `DefaultAzureCredential` otherwise) and the emulator mode (dummy token, `DisableChallengeResourceVerification`, any server certificate), which throws in production.
- `KeyValueProtectionEntryPoint.AddKeyVaultKeyProtection` binds `KeyValueOptions`, reads `AzureKeyVaults:{Standard.VaultName}`, and registers the provider only when that vault exists.
- `KeyValueOptions` (`Backend.Core/src/KeyValues/KeyValueOptions.cs`): `VersionRetention`, `DeletedRetention`, and `Protection` with `Emulator`, `MaxConcurrentKeyOperations`, `CacheSizeLimit` and `Standard` (`VaultName`, `KeyNamePrefix`, `KeySize`, `RotationInterval`, `CacheDuration`).
- `StorytellerException` gets a constructor with an inner exception, so `ServiceUnavailableException` carries the Key Vault failure. `PreconditionFailedException` (412) and `FeatureNotConfiguredException` (501) are new bases; the HTTP mapping of all three is Phase 2.
- **Deviation:** the specific exception types of the spec (`KeyValueNotFoundException`, `ProtectionNotAllowedException`, …) are the existing sealed bases with the new error codes, created by the factories in `KeyValueErrors`. The codes are in `ErrorCodes`.

### 7. Service and models

- `CosmosKeyValueService` (`Backend.CosmosDb/src/KeyValues/`) with `KeyValueMappings`, the listing rows, and the registration in `EntryPoint.AddCosmosDbAnnotations` (`KeyValueOptions`, `IValueProtector` as `TryAddSingleton`, `IKeyValueService`).
- Writes are transactional batches (history item with the rest of its lifetime as `ttl`, then the catalog item with `IfMatchEtag`), retried up to 8 times with a random pause of 10–40 ms per attempt. Three retries, the template convention, were not enough for the test with 8 concurrent writers of one key.
- The soft delete checks whether the history item of the current version already exists, the restore deletes it when it does, and a write to a deleted key skips it: the three keep the version items consistent with each other. A permanent delete removes the versions in batches of 100 and the catalog item last, so an interrupted one finishes on the next call.
- `ProtectAsync` is implemented in full, because it reuses the update path: it decrypts the current version and writes it under the target level as a new version. With only `Standard` allowed it answers `Unchanged` or `NotAllowed` (`ReprotectNotPossible`).
- `GetVersionChangesAsync` uses the new `JsonContentDiffer.GetTextChanges(string, string)`, the DiffPlex part of the JSON diff.
- The listing projects `c.Content["Value"]` with brackets: `Value` is a keyword of the query language, and `c.Content.Value` is a syntax error.
- `KeyValueCaller` carries the author, `CanReadSecrets`, `CanWriteSecrets` and the channel. Until the project policy exists, `AllowedProtections` in the service is `Public` and `Standard`.
- The request charge of every Cosmos operation is logged at Debug (`kv {operation} in {partition}: {RU} RU`).

### 8. Import and export

As specified, with two refinements. The validation and the plan collect their errors together, so one `400 ImportInvalid` lists invalid keys, duplicates and missing secret values at once (`KeyValueImportInvalidException` carries the items; its HTTP mapping is Phase 2). A caller without secret rights is refused before the plan when the document contains a secret value, so the plan never decrypts existing secrets for a reader; level changes and `Replace` deletes of secrets are refused after the plan, as the spec says. Writes go in batches of 50 keys, with one `GROUP BY` query for the next versions of the new keys of a batch; a batch that another writer disturbed falls back to `SetAsync` per key.

### 9. Binding

- `BindingRequest.Context` (`Binding.Abstractions`) carries `BindingScope.Context` to sources; `BindingEvaluator.ResolveSourceAsync` fills it.
- The source call form in `BindingEvaluator.InvokeFunctionAsync`: `@name(path)` resolves the path against the source `name` when there is no function `name`, the call has one argument, and it is a bare path. `SourceCallFormTests` covers it.
- `Binding.Core/src/KeyValueBindingSource.cs` with `KeyValueSourceKind` and `KeyValueBindingSources` (the keys `default`, `public`, `secret` and `ReservedKeys`); `EntryPoint.AddKeyValueBindings` registers the three sources, and the executor factory fails the startup when a name is both a source and a function. `BindingsOptions` exposes `SourceKeys` and `FunctionNames` for tests and diagnostics.
- The memo is a `ConditionalWeakTable` from the `ConfigurationBindingContext` of the read to a dictionary of `Lazy<Task<KeyValueLookup?>>`; the Lazy matters, because the resolver walks the properties of a document in parallel and `GetOrAdd` alone can run a factory twice.
- `AddAzureKeyVaultBindings` skips a binding source for vault entries named `default`, `public` or `secret`; the `SecretClient` is still registered.
- `Program.cs` calls `AddKeyVaultKeyProtection` after `AddCosmosDbAnnotations` and `.AddKeyValueBindings()` after `AddAzureKeyVaultBindings`; `Api.Functions.csproj` references the new project.
- `Aspire.Host/src/AppHost.cs` runs `nagyesta/lowkey-vault:7.3.112` with `--LOWKEY_VAULT_RELAXED_PORTS=true` and passes `AzureKeyVaults__default` and `KeyValues__Protection__Emulator=true` to the API.

### 10. Tests

- `Backend.CosmosDb/test/CosmosKeyValueServiceTests.cs` (21 tests): the public lifecycle with versions, content and diffs; delete, restore (bulk, mixed outcomes, no new version) and the write to a deleted key; the expiry of a deleted key and of a history version with retentions of seconds on a service instance of its own; permanent delete and purge, including 150 versions in two batches; encrypted items without plaintext, reveal rights, the integrity binding to key, view and version, the plaintext cache; key and size rules, `ProtectionRequired` and `ProtectionNotAllowed`; `ExpectedVersion`; 8 concurrent writers; listing filters, view isolation and paging over 1,050 keys; import validation, plan, dry run, merge, replace with restore, 120 items in batches, the reader refusal; export with and without secrets and the round trip into another view; protect; view discovery; the container TTL on creation and on a legacy container.
- `KeyValueBindingTests.cs` (6 tests): the three sources with and without secrets, case-insensitive keys, view isolation, a missing key at the top level and inside an interpolation, one lookup per read through `CountingKeyValueService`, the 503 of an unavailable key provider (`FakeKeyEncryptionProvider.IsUnavailable`), the name clash at startup, and the vault named `default` that is no longer a source.
- `KeyValueEndToEndTests.cs`: `CosmosKeyValueService` with `KeyVaultStandardKeyProvider` against the Cosmos emulator and Lowkey Vault in Docker.
- `KeyValues.Azure.KeyVault/testint` (6 tests): the key name, one key per project, wrap and unwrap round trips also with an older key version after `RotateKeyAsync`, the refusal of another vault or algorithm, a `429` with `Retry-After` through a throttling `HttpMessageHandler`, and the protector end to end with the vault. The fixture starts `nagyesta/lowkey-vault:7.3.112` with `Testcontainers.LowkeyVault` 4.11.0 (the module exists for that version) or uses `LOWKEY_VAULT_URL`.
- The test `Startup` registers `FakeKeyEncryptionProvider` (AES key wrap in memory), the counting decorator, `AddKeyValueBindings`, and Debug logging for the key/value category.
- A missing key inside an interpolation reaches the caller as `BindingException` (the executor wraps the evaluation error); the test expects that type.

### Request charges

From the Cosmos emulator with the default indexing policy and values of a few bytes to 1 KB:

| Operation | RU |
| --- | --- |
| Create (catalog item) | 10.5–13 |
| Update, soft delete, restore (history and catalog item in one batch) | 22–25 |
| Point read of a key | about 1 (not logged; the Cosmos norm for a 1 KB item) |
| Listing page of 4 rows / 1,000 rows | 3 / 50 |
| Versions of a key, next-version query | 3 |
| Import batch of 50 creates / 50 updates with history | 524 / 1,160 |
| Purge of 100 versions in one batch | 1,067 |

### Documentation

- New `docs/Platform/Storyteller/key-values.md`: identity, levels, storage and lifecycle, TTL, concurrency, envelope encryption, the Standard level and Key Vault, configuration, the emulator, the service, import and export, binding, request charges.
- `binding.md`: the native store as the default source, `public` and `secret`, the source call form, `BindingRequest.Context`, the `AddKeyValueBindings` registration, and the vault named `default`.
- `definitions.md` and `templating.md`: the history expiry is enforced since the containers have TTL.

### Left for the next phases

- Phase 1b: the project policy, `Hsm`, `Byok`, `Hyok` (the protector throws `ProtectionNotConfigured` for it), references validation, rotation and re-wrap, the mTLS rule with the durable certificate store, audit, metering.
- Phase 2: the HTTP mapping of `PreconditionFailedException` (412), `FeatureNotConfiguredException` (501), `ServiceUnavailableException` (503 with `Retry-After`) and `KeyValueImportInvalidException` (400 with the items), and the endpoints.
- The pre-existing build failure of `src/Crumble/Playground/Examples` is unrelated and was not touched.
