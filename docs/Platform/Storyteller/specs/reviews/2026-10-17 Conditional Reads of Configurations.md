# Conditional Reads of Configurations

## Overview

The effective and the resolved configuration reads now send a weak `ETag` and answer an `If-None-Match` that lists it with `304 Not Modified` and no body. A resolved document without bindings is answered from the effective read without calling the resolver, so polling it costs one point read. A document with bindings is still resolved on every read, and its tag is an HMAC over the resolved body. This review covers [Conditional Reads of Configurations](../2026-10-17%20Conditional%20Reads%20of%20Configurations.md) in full, for Linear 424-47.

## What Was Done

### 1. Entity tags (`Api.Functions`)

New `Api.Functions/src/ConditionalReads/ConfigurationEntityTags.cs`. As planned:
- A tag is `W/"<kind>.<digest>"`. The digest is the first 16 bytes of SHA-256, base64url (22 characters).
- The inputs are length-prefixed, with -1 for null:
  - the kind;
  - `FullKey.ToString()`;
  - the secret rights code (resolved reads only);
  - `Version`;
  - `Hash`;
  - `Content.ToString(Formatting.None)`.
- `ForEffective` (`e`) and `ForResolved` (`r`) use SHA-256. `ForResolvedBindings` (`b`) uses HMAC-SHA256 with the key from section 4.
- The rights code is `s` (may read secrets), `w` (withheld from a machine without mTLS) or `p` (neither).
- `Modified` is not hashed.

### 2. Conditional answer (`Api.Functions`)

New `Api.Functions/src/ConditionalReads/ConditionalRequestExtensions.cs`, extensions of `HttpRequestData`:
- **`MatchesIfNoneMatch`** reads every value through `Headers.TryGetValues` and parses them with `EntityTagHeaderValue.TryParseStrictList`.
  - `*` matches, and is checked before the weak comparison.
  - A header that is not a strict list never matches, so the read answers 200.
- **`OkOrNotModified`** sets `ETag`, then returns `StatusCodeResult(304)` or `OkObjectResult`.
- **`MarkNoCache`** sets `Cache-Control: private, no-cache`.
- **`GetEntityTagKey`**, added during the implementation, reads the key of section 4.

The helpers live in their own folder; `HttpRequestDataExtensions.cs` is untouched.

### 3. The two reads (`ConfigurationHttp`)

As planned.
- **Effective read.** After the unchanged scope check, access check, key parsing and 404, it calls `MarkNoCache` and `OkOrNotModified` with the `e` tag.
- **Resolved read.**
  - It reads the effective configuration with `GetRawConfigurationAsync` and asks `IConfigurationService.HasBindings`.
  - Without bindings, it answers with the `r` tag and never calls the resolver.
  - With bindings, it calls `ResolveConfigurationAsync(fullKey, effective, rights.CanReadSecrets)` and answers with the `b` tag over the resolved body.
- **Headers.** The withheld header, `no-store` (when the caller may read secrets) or `private, no-cache`, and `ETag` are set after the resolve. They go out with the 200 and the 304, never with a 400, 404 or 422.
- **Constructor.** It did not change.

### 4. Key of the tags over resolved bindings

- **`ConditionalReadOptions`** (section `ConditionalReads`) has `EntityTagKey`: base64 of at least 32 bytes.
  - The decoded key is cached on the options instance.
  - `IsValid` accepts an empty setting.
- **`ConditionalReadsEntryPoint.AddConditionalReads`** binds and validates it with `ValidateOnStart`. The message names the setting, never its value.
  - `Program.cs` calls it right after `AddCosmosDbAnnotations`.
  - No key was added to `local.settings.json`.
- **Lookup.** `GetEntityTagKey` reads `IOptions<ConditionalReadOptions>` with `GetService`. Without options or a key, it falls back to a process key drawn once with `RandomNumberGenerator`.

### 5. Binding detection (`Binding.Abstractions`, `Binding.Object`)

As planned.
- `IConfigurationBindingResolver.ContainsBindings(JToken)` is a default interface member that returns `true`.
- `ConfigurationBindingResolver.ContainsBindings` walks the values. It returns true for a string that starts with `@` and for an object with a string `$binding`.
- `BindStringAsync` and `TryReadEnvelope` now use the same two private predicates, `IsBindingString` and `HasBindingProperty`.

### 6. Resolve step (`Backend.Core`, `Backend.CosmosDb`)

As planned.
- `IConfigurationService` gets `HasBindings(Configuration)` and `ResolveConfigurationAsync(FullKey, Configuration, bool)`, next to the resolved reads.
- In `CosmosConfigurationService`:
  - `HasBindings` asks the resolver, and is false without one.
  - `ResolveConfigurationAsync` holds the code that used to be in `GetResolvedConfigurationInternalAsync`, which is now the raw read followed by the new step.
- `FakeConfigurationService` gets both members as `NotSupportedException`.

### 7. OpenAPI

As planned.
- **Header class.** New `Api.Functions/src/OpenApi/EntityTagResponseHeader.cs` (`IOpenApiCustomResponseHeader`) describes `ETag`.
- **Operations.** Both operations carry it on the 200 and on a new `304` without a body. Their descriptions say to send the tag back in `If-None-Match`, and the resolved one says that a document with bindings is still evaluated.
- **Descriptions.** The texts are new constants in `Definitions.Descriptions`.
- **Not regenerated.** The OpenAPI document and the SDKs were not regenerated, because PRs #99 and #105 regenerate them.

### 8. Tests

| Project | Result |
| --- | --- |
| `Api.Functions/test`, Debug | 382 pass (348 before) |
| `Api.Functions/test`, Release | 376 pass. The 6 failures in `MachineHttpTests` and `AnnotationsHttpTests` were there before: their user fakes return a role that is too low. |
| `Binding.Object/test` | 212 pass (194 before) |
| `Backend.Core/test` | 169 pass |
| `Backend.CosmosDb/test`, `ConfigurationResolutionTests` | 4 pass |

**Flaky test.** `MachineTokenFallbackTests.MachineKeyFailure_IsServiceUnavailable` fails about one run in six, in both builds. It races `BearerAuthenticationMiddlewareTests` on the static `BearerValidationMode`, which PR #106 fixes with a shared collection.

**Not run.** The Cosmos-backed tests of `Backend.CosmosDb`: their fixture deletes every database of the local emulator.

**New test classes:**
- **`ConfigurationConditionalReadTests`** (34 tests).
  - It uses a `DispatchProxy` fake of `IConfigurationService` and a `DefaultHttpContext` under the function context item `HttpRequestContext`, so the response headers are asserted.
  - It covers every case in section 8 of the spec, the start-time validation of the key, and the OpenAPI document.
- **`ConfigurationBindingDetectionTests`** (18 tests).
- **`ConfigurationResolutionTests`** (4 tests, no Cosmos).

**Deviation.** Section 8 of the spec assumed that a machine without the secrets scope is `p`. A `DEV_AUTH` build gives every caller the scope, so no request can be `p` there. All three codes are tested on the tag function in both builds. The handler test with a `p` caller runs in Release only, like `SecretRightsTests`.

**Manual check.** The Functions host ran locally with Core Tools 4.1.0 (64-bit), the Cosmos emulator, and Azurite in a throwaway container. It read an existing configuration of the emulator twice with `curl -i`:
- **Effective read.** A 200 with `Cache-Control: private, no-cache` and `ETag: W/"e.…"`. The same request with `If-None-Match` got a 304 without a body, with the same two headers.
- **Resolved read.** A 200 with `Cache-Control: no-store` (a Debug build gives every caller secret rights) and `ETag: W/"r.…"`. Then a 304 with the same headers.
- **The effective tag sent to the resolved read** got a 200.

The 32-bit Core Tools 4.0.7032 on the `PATH` cannot start this host: it fails to load `System.Memory.Data` from the Queues extension. This is unrelated to the change.

### 9. Documentation

- **`docs/Platform/Storyteller/inheritance.md`** has a new `## Conditional reads`: the tags, the resolved read, the key, the headers and the limits.
  - The limits include writes ignoring `If-Match` and the effective-cache staleness.
  - After the code review, one more limit was added: the SDKs do not send `If-None-Match` yet.
- **`key-values.md`** says that a conditional read of a document with bindings still looks the keys up.
- **`binding.md`** says that the API asks `ContainsBindings` first.
- **`docs/42for.net/platform/using-the-platform.md`**, "What a service reads", tells a polling service to send the tag back.

### Reviews

- **Before the spec was committed.** Three skeptics reviewed it, through the lenses of HTTP semantics, security, and fit with the code. Their findings went into the spec before it was committed:
  - the full key in every digest;
  - three secret-rights codes instead of two;
  - strict parsing and an explicit `*`;
  - detection on the registered resolver instead of a static method;
  - where the key lives;
  - writes ignoring `If-Match`;
  - the public doc target.
- **After the implementation.** Four finders reviewed the code (HTTP correctness, security, tests, compatibility), and two skeptics checked every finding.
  - The HTTP and security finders found nothing.
  - The four other findings were all refuted as defects. Three were missing tests: the `includeSecrets` passed to the resolve step, a `p` caller at the handler, and `ValidateOnStart`. The fourth was the SDK gap.
  - The three tests were added anyway (`ee766228`), and the SDK gap is documented.

### Merge notes

- **PR #106 (424-48).** `MachineRestrictionHttpTests` expects `GetConfigurationResolved` to call `GetResolvedConfigurationAsync` exactly once. Its `RecordingProxy` returns null for other members, so `HasBindings` would fail to unbox. Whichever change merges second updates those tests to `GetRawConfigurationAsync`, and answers `HasBindings` in the proxy.
- **PR #103 (424-44).** Until it lands, the effective tag shares the first-insert staleness of the cached document.
- **PRs #99 and #105.** Whichever regenerates the OpenAPI document after this merge also picks up the 304 and the `ETag` header.
