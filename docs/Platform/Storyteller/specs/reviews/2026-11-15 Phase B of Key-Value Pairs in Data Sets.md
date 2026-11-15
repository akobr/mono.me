# Phase B of Key-Value Pairs in Data Sets

## Overview

Phase B of the spec [Key/Value Pairs in Data Sets of DbCreator](../2026-11-15%20Key-Value%20Pairs%20in%20Data%20Sets%20of%20DbCreator.md) (424-265) covers section 6, the vault of the Aspire resource `db-seed`, and section 8.2, the integration tests of `DbCreator/testint` against a Cosmos DB emulator and Lowkey Vault. Section 5, the vault in DbCreator itself, came with phase A (see its review). It is one commit, `test(platform): seed key/value pairs against Cosmos DB and Lowkey Vault, and give db-seed the vault (424-265)`.

## What Was Done

### 6. Aspire

`Aspire.Host/src/AppHost.cs`: after the `keyvault` container, `db-seed` gets `AzureKeyVaults__default` (the `https` endpoint of `keyvault`) and `KeyValues__Protection__Emulator=true`, and waits for the vault, as `api-functions` does. `db-seed` is declared before the vault, so the settings are added where the vault exists rather than by moving the declarations. The project is a Debug build under Aspire, so the tool runs as Development and takes the emulator.

### 8.2 `DbCreator/testint`

- **`DbCreatorFixture`**: `Services` are built as before, without a key-encryption provider. `GetVaultServicesAsync` starts Lowkey Vault on its first call (`nagyesta/lowkey-vault:7.3.112`, the tag of the other suites and of Aspire, reused with the label `net.42for.tests.lowkey-vault=dbcreator-integrationtests`, or `LOWKEY_VAULT_URL`), and builds a second provider over the same database with `AzureKeyVaults:default` and `KeyValues:Protection:Emulator`, and `AddKeyVaultKeyProtection` as `Startup` calls it (no host environment, so not Production). Both providers have the organization guard, and, unlike the tool, the bindings of the API (`ConfigBindingFunction`, `AnnotationBindingFunction`, `AddConfigurationBindings`, `AddKeyValueBindings`), so a test can read what a seed wrote as a resolved configuration: the first run showed that `AddCosmosDbAnnotations` alone leaves `@smtp.host` unresolved. The project references `Binding.Core` and `Testcontainers.LowkeyVault`. `DisposeAsync` disposes the vault provider and the container.
- **`SeedTestsBase`**: `KeyValues`, and `Command(services)` for the command over the vault's services.
- **`KeyValueSeedTests`** (new), each in an organization of its own:
  - `PublicPairs_SeedWithoutAVault_AndASeedAgainChangesNothing`: four public pairs (plain, JSON, a reference, a content type) without a vault; their values, content types, version 1 and the `System` stamp; the record (`KeyValues` 4); a seed with `--again` is `Unchanged` 4, and of every item of the organization only the record changed (no pair, no audit event).
  - `StandardPairs_WithoutAVault_AreRefusedBeforeAnythingIsWritten`: the seeder's exception, and the command's exit `2` with and without `--dry-run`; the organization, its project and the owner's account hold nothing.
  - `StandardPairs_SeedWithLowkeyVault`: the command exits `0`; the secret reveals; the stored item has the envelope (`RSA-OAEP-256`) and not the value; the project's policy records the Standard key; a seed with `--again` decrypts and finds both pairs `Unchanged`.
  - `ExistingAndDeletedPairs_AreSkipped_AndAChangedSetCreatesOnlyTheNewPairs`: after a seed, `a` gets another value (written as `A`, so the key matches ignoring case) and `b` is deleted through the service; the changed set creates `d` only: `a` is `Skipped` with `KeyValueExists` and keeps its value and version 2, `b` is `Skipped` with `KeyValueDeleted` and stays deleted with its version and stamp, `c` is `Unchanged`, and the set is recorded.
  - `DryRun_DecidesEveryPairAndWritesNothing`: into a new target (nothing stored at all), and into a seeded one with a changed pair (`Unchanged` 3, `Skipped` 1, the items unchanged).
  - `RefusedLevelsAndItemsWithoutAValue_ExitWithTwoAndWriteNothing`: `Hsm`, `Byok`, `Hyok` and a `Standard` item without a value, with the vault, exit `2`, nothing stored.
  - `AProjectPolicyWithoutStandard_FailsTheStandardPair_AndTheOthersAreCreated`: the policy updated to `Public` only (`IKeyValuePolicyService.UpdateAsync`), then the `Standard` pair fails with `ProtectionNotAllowed` and its `Public` neighbour is created; exit `4`, no record.
  - `ABindingToASeededPair_ResolvesInAResolvedRead`: a configuration with `@smtp.host`, `@[https://@smtp.host/api]`, `@public(smtp.host)` and `@(smtp.password, secret)` resolves to the seeded values, the secret included.
- **`BuiltInSetsTests`**: `showcase` is seeded with the vault's services, and its pairs are all created; `Exhibit_SeedsWithoutFailures_AndItsConfigurationsBindItsPairs` (new) seeds `exhibit` with the vault (its documents created, `README.md` and `project-settings.json` ignored, the 20 pairs created) and reads `usg.globex.crm` and `sbt.globex` resolved: the interpolated hosts, the public pair and the `Standard` escrow pin are bound. It is the first seed of the set in a test.

### Checks

- `STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/DbCreator/testint` (the vNext emulator and Lowkey Vault in a local Docker): 25 tests pass in 9 min 40 s, among them the 11 of `KeyValueSeedTests` and the 5 of `BuiltInSetsTests`. A first run without the bindings failed the two resolved reads, which led to registering them in the fixture.
- `dotnet test --project src/Platform/Storyteller/DbCreator/test`: 182 tests pass. `Aspire.Host`, `Backend.CosmosDb/test` and `Access.Certificates/testint`, which reference DbCreator, build.
- CI runs `DbCreator/testint` in the vNext job of `Platform.Storyteller.CosmosDb.yml` (and in the classic job outside pull requests) on `ubuntu-latest`, whose Docker starts Lowkey Vault as it starts the emulators; the workflow needs no change.
