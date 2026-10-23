# Phase 3 of the Configuration Provider for .NET

Linear: [424-85](https://linear.app/424fun/issue/424-85/create-config-provider-for-net-and-c).

## Overview

Phase 3 covers sections 13-16 of [Configuration Provider for .NET](../2026-10-23%20Configuration%20Provider%20for%20.NET.md), and the resolver rows of the startup table in section 11. The provider now resolves secret references locally, with the application's own identity. The new package `42.Platform.Storyteller.Configuration.KeyVault` resolves them in Azure Key Vault. `IStorytellerDocuments` reads any configuration on demand, for jobs. The documentation covers the provider, both packages and the SDK changes.

| Commit | Content |
|---|---|
| `4d1bf280` | Sections 13-15: substitution, the Key Vault resolver and documents on demand |
| `f4791390` | Section 16: the documentation |

| Project | Passed (final state of the branch) |
|---|---|
| `Configuration/test` | 541, 2 skipped (the Unix file-mode tests, on Windows) |
| `Configuration.KeyVault/test` (new) | 87 |

## What Was Done

### 13. Secret references

- **Contract.** `ISecretReferenceResolver`, `SecretResolutionException` and `SecretReferenceResolver.FirstOf`, from Phase 2, as specified.
- **`SecretReference`** and `SecretReferenceForm` parse the three forms strictly, as specified. Stricter than the spec's table:
  - the shorthand's scheme is lowercase, and it has no trailing slash;
  - `/secrets/` is lowercase in a secret URI, which has no `%` escape or dot segment, and no port, not even a written `443`;
  - the host is a DNS name, and so is its ASCII (IDN) form, without an empty label or a trailing dot. A Unicode host whose fullwidth delimiters or full stops map to `/`, `@`, `:` or `.` is not one.

  The accepted and rejected shapes of `ClientEncryptionAndReferenceTests` are test vectors in `SecretReferenceTests`.
- **Substitution.** `References/ReferenceSubstitution.cs`, used by the new `Engine/ContentApplication.cs`, step 3 of the apply pipeline that the engine and the document service share. It follows the spec: `Resolved` mode only, string values only, no fallback to the reference text, an unclaimed `kv://` shorthand fails, and other unclaimed reference forms and a missing resolver give one warning per applied document. The server's document is never changed, so the cache keeps it as it came.
- **Failure classes beyond the spec:**
  - an `OperationCanceledException` that the provider did not request, a timeout of the resolver's own, is transient;
  - a `CanResolve` that throws, and a `null` value, are definitive;
  - a resolver that ignores cancellation cannot hold a run past `ReadTimeout`.
- **Memory of resolved values.** The provider keeps them in a cache shared by the documents it applies, per value text. **Deviation:** the document service keeps them only in the results of its cached documents, not in a cache of its own, so a document that is evicted or expires takes its secrets with it, and a changed document reuses the values of its own previous version.
- **Rotation.** A `304` resolves references older than `SecretRefreshInterval` again and reloads only when a value changed. A rotated value that does not bind is rolled back once. The snapshot keeps the rejected data (`RejectedData`) in place of a rejected tag, so the same values are not applied and rolled back every poll; a value that rotates again is tried.
- **Startup.** The resolver rows of the startup table, including a cache entry whose references cannot be resolved, which is not usable.
- **Hand-over.** The hand-over of the logger factory now also logs the warnings of the applied document (unbound bindings, unresolved references), because a document that does not change is applied only once, at the startup.

### 14. Key Vault resolver

- `Configuration.KeyVault/src` (assembly `42.Platform.Storyteller.Configuration.KeyVault`, namespace `_42.Platform.Storyteller.Sdk.Configuring.KeyVault`) and its test project, in `42.mono.slnx`. It references `Azure.Security.KeyVault.Secrets` and not `Azure.Identity`.
- `KeyVaultReferenceResolver` and `KeyVaultReferenceResolverOptions`, as specified: the host match on the IDN host with the dot boundary, `AllowedVaults`, one `SecretClient` per vault origin (a `Lazy` in a `ConcurrentDictionary`, so a race creates one), and the version passed through.
- **Options** are copied when the resolver is created. Suffixes are trimmed, lose a leading dot, and an IDN suffix becomes punycode. An invalid suffix or an empty vault name throws `ArgumentException`. Vault names compare without case.
- **Failure classes beyond the spec:**
  - status `0` (no answer) is transient;
  - when a `SecretClient` gives up after its own retries, Azure.Core throws an `AggregateException` with every attempt. It is transient when every attempt was, and the message names the number of attempts and describes the last;
  - a failed token request of the credential (Azure.Identity's `AuthenticationFailedException`, matched by name) is classified by its cause. MSAL's `MsalServiceException` counts by its `StatusCode`. `CredentialUnavailableException` stays definitive, also for a managed identity endpoint that does not answer, because Azure.Identity reports both alike. The docs state the consequence for an `Optional` provider;
  - after the caller's cancellation, any failure comes out as `OperationCanceledException`.
- **Tests** with `StubSecretClient` (a `SecretClient` subclass through `SecretClientFactory`) and `SecretModelFactory`: every item of the spec's list. `IssueProgramTests` compiles the issue's `Program.cs` with its configuration lines unchanged, so the test project references `Azure.Identity` and the `Microsoft.AspNetCore.App` framework.

### 15. Documents on demand

- `IStorytellerDocuments`, `StorytellerDocument`, `Documents/StorytellerDocuments.cs` and `Documents/DocumentEntry.cs`, and `AddStorytellerDocuments` in `StorytellerConfigurationServiceCollectionExtensions`. `SettingsReader.ReadForDocuments` reads the options without the provider's own (`RefreshInterval`, `Optional`, `CacheDirectory`, `CacheProtector`, `Placement`), which are ignored from code and from the environment, even when their variables hold invalid values.
- As specified: revalidation with `If-None-Match` on every call, a `MemoryCache` of 1024 entries with a sliding expiration of twice `SecretRefreshInterval`, eviction on `401`, `403`, `404` and withheld secrets, in-flight sharing apart from the cache, no disk, and disposal by the container.
- **Beyond the spec:**
  - `FileSystem` applies too, for credential files;
  - every call returns a document of its own: what a caller changes in `Content`, `Values`, `Modified` or `Configuration` reaches neither the cache nor another caller;
  - a shared request is bounded by `ReadTimeout`, and a caller's token ends only its own wait;
  - `Get<T>` throws `StorytellerConfigurationException` when there is nothing to bind and when a value does not convert, and names the type and the section, never the value;
  - a federated credential that names no project makes every read without `Key` fail definitively, with a message that names `STORYTELLER_KEY`, and no request is sent;
  - the first registration wins.
- **Tests** in `Documents/StorytellerDocumentsTests.cs`: every item of the spec's list, sharing beyond the size limit included.

### 16. Documentation

- New `docs/Platform/Storyteller/configuration-provider.md` with every topic and limit of the spec, plus the limits found during implementation: references that the parser rejects stay plain text, a reload from elsewhere can hide a rejection, and a switch lasts for the process. It also shows the setup for the Lowkey Vault emulator of the Aspire host.
- New package readmes `docs/Platform/Storyteller/Configuration/package-readme.md` and `docs/Platform/Storyteller/Configuration.KeyVault/package-readme.md`.
- `docs/Platform/Storyteller/inheritance.md`: the limit "The SDKs do not send `If-None-Match` yet" became a pointer to the conditional methods of both SDKs and to the provider.
- `docs/42for.net/platform/using-the-platform.md`: "What a service reads" names the .NET provider.
- The SDK readmes changed with their code in Phase 1.

## Reviews

- **Three reviewers** read the phase through the lenses of references and Key Vault security, documents with their engine integration, and accuracy of the docs. They found seven defects, all fixed before `4d1bf280` and `f4791390`:
  - when a `SecretClient` gives up after its own retries, network errors and timeouts became definitive;
  - a rotated secret that did not bind was applied and rolled back on every `304`;
  - a host with fullwidth delimiters made `CanResolve` throw instead of decline;
  - the warnings of the startup document were lost before the logger hand-over;
  - the document service kept resolved values after their document left the cache;
  - the docs claimed that a local emulator works with `AllowedHostSuffixes` alone;
  - the docs overstated how a federated credential picks its project.
- **A verifier** found two more, also fixed:
  - Entra throttling (`MsalServiceException` 429 or 5xx) was definitive;
  - the Lowkey Vault example did not match the emulator of the Aspire host.
- **The final review of the whole branch**, one reviewer and the end-to-end run below, found eight issues. They are fixed in `240d75be`, and the reviews of Phases 1 and 2 list them. One finding was that the branch had no review documents; these three files are that fix.

## Manual check

- **The Functions host was not run.** It would write into the local Cosmos DB emulator that other sessions share.
- **An end-to-end run over real HTTP instead.** A sample app built from the minimal `Program.cs` of the docs: `WebApplication`, `AddStoryteller`, `AddStorytellerConfiguration`, `AddStorytellerDocuments`, and an `IOptionsMonitor` listener. It ran against a Kestrel server that implements the contract of the two reads:
  - weak tags, and a `304` with the same headers;
  - `private, no-cache` or `no-store`, and the withheld header;
  - a `401` without the API key;
  - `503`s and connection resets on demand.
- **What it showed:**
  - The source sat before the environment variables, and an environment variable overrode a Storyteller value.
  - A changed document was applied by the next poll and seen by `IOptionsMonitor`.
  - Polls sent `If-None-Match` every 2 s within the jitter and got `304`s.
  - `SwitchKeyAsync` applied an annotation key, failed and kept the key for a missing one, and refused another project.
  - An outage kept the last document, degraded, and opened the circuit.
  - A restart with the server down started from the plain cache entry, required or optional. Without an entry, a required source threw a transient error and an optional one started empty. An optional one still failed on `401`, `404` and withheld secrets.
  - A `no-store` answer left no file.
  - `IStorytellerDocuments` read a `200`, then a `304`.
  - A document that did not bind was rolled back, and its tag was kept for the next polls.
  - The logs went through the host's logger, without values.
  - A graceful stop cancelled the run in flight.

## Merge notes

- **PR #126 (424-51)** also edits `docs/Platform/Storyteller/inheritance.md` and `docs/42for.net/platform/using-the-platform.md`. The limits of conditional reads, and "What a service reads", may conflict as text. The code does not overlap.
- **SDK regeneration.** The next regeneration of `ApiSdk.g.cs` must keep the members that `ApiSdk.Conditional.cs` uses, as the SDK readme lists.
- **Package pin.** `Microsoft.Extensions.Http.Resilience` is now pinned to 10.1.0, which `Access.AuthKit` and the Aspire projects restored anyway.
