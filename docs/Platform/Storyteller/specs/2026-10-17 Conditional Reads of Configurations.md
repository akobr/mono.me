# Conditional Reads of Configurations

Linear: [424-47](https://linear.app/424fun/issue/424-47/add-conditional-reads-in-api).

## Problem

A provider or an agent finds out that a configuration changed by polling the effective or the resolved read and comparing the bodies. Every poll pays for the whole read:
- The effective read costs a Cosmos point read, and the whole document goes over the wire.
- The resolved read also evaluates the bindings: key/value lookups, Key Vault calls, annotation reads and JSON-e.

HTTP already has a mechanism for this. The response carries an `ETag`, the next request sends it back in `If-None-Match`, and the server answers `304 Not Modified` without a body when nothing changed. Neither read sends a validator today, so no client and no browser cache can revalidate.

## Current State

Paths are relative to `src/Platform/Storyteller`.

- **Effective read.** `Api.Functions/src/V1/ConfigurationHttp.cs:95-120` (`GetConfiguration`) checks the scope and access, parses the key, calls `IConfigurationService.GetRawConfigurationAsync` and returns `OkObjectResult`. It sends no cache headers.
- **Resolved read.** `GetConfigurationResolved` (`:136-173`):
  - It computes the secret rights and calls `GetResolvedConfigurationAsync(fullKey, rights.CanReadSecrets)`.
  - It then sets `X-2S-Secrets-Withheld: mtls-required` when secrets were withheld from a machine without mTLS.
  - It sets `Cache-Control: no-store` whenever the caller may read secrets.
- **`GetRawConfigurationAsync`** (`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs:138-184`):
  - It costs one point read when `CalculatedContent` is cached. Otherwise it calculates, caches and reloads the item.
  - `Hash` is `CalculatedContentHash`: MurmurHash3 32-bit, eight hex digits.
  - `Hash` is null for an empty effective document, because an empty document is never cached.
  - On the reload path (`:176-183`), `Hash`, `Version` and `Modified` come from the reloaded item. `Content` comes from the local calculation, so the two can belong to different writes.
- **Fields that change independently.** `Version` and `Modified` come from the target's own item:
  - An own write that does not change the merge bumps `Version` and keeps the same `Hash`. An example is a value equal to the inherited one.
  - Ancestor and template writes change `Content` and `Hash` without touching `Version`.
  - A version-0 configuration with no item gets `Modified` = the time of the read (`Mapping.cs:50-60`).
- **Resolution.** `GetResolvedConfigurationInternalAsync` (`:806-828`) reads the effective configuration, then calls `_bindingResolver.ResolveAsync(config.Content, includeSecrets, scope)`, which changes the content in place. The resolved body keeps the effective `Version`, `Modified` and `Hash`.
- **What the resolver touches.** `Binding.Object/src/ConfigurationBindingResolver.cs`:
  - A string is bound when it starts with `@` (`BindStringAsync`).
  - An object is an envelope when it has a string `$binding` (`TryReadEnvelope`).
  - Everything else, property names included, is left as stored.
- **The resolved body depends on more than the effective document.** It also depends on:
  - key/value pairs. [key-values.md](../key-values.md) promises: "a changed pair takes effect on the next read";
  - Key Vault secrets;
  - annotation values read by `@annotation`;
  - the clock (JSON-e `now`, `$fromNow`);
  - the caller's secret rights.

  So the effective hash identifies the resolved body only when the document has no bindings.
- **Response headers.** `SetResponseHeader` and `MarkNoStore` (`Api.Functions/src/Security/HttpRequestDataExtensions.cs:170-185`) write to the ASP.NET Core response. The `IActionResult` runs after the worker middleware, so these headers go out with a 200, a 304 and an error alike. In unit tests there is no `HttpContext`, and they do nothing.
- **No validators anywhere.** `Api.Functions` has no `ETag`, `If-None-Match` or `304`.
- **Clients.** No client in the repo sends `If-None-Match`.
  - The admin UI calls the API cross-origin, and platform CORS cannot expose response headers.
  - The browser's own cache still revalidates with a stored `ETag` without exposing it to script.

## Proposed Changes

### 1. Entity tags (`Api.Functions`)

New `Api.Functions/src/ConditionalReads/ConfigurationEntityTags.cs`.

**Form.** A tag is weak, `W/"<prefix>.<digest>"`. The digest is the first 16 bytes of SHA-256 (or of HMAC-SHA256, see section 4), base64url-encoded, 22 characters. Weak, because the body also carries `Modified`, which is not part of the tag, and because the JSON writer is not byte-stable across deployments. `If-None-Match` uses weak comparison anyway.

**Inputs.** Every input is length-prefixed, so two different inputs never produce the same string. `Content` is serialized as compact JSON with `JToken.ToString(Formatting.None)`.

| Read | Prefix | Digest input | Keyed |
| --- | --- | --- | --- |
| Effective | `e` | full key, `Version`, `Hash`, `Content` | no |
| Resolved, document without bindings | `r` | full key, secret rights, `Version`, `Hash`, `Content` | no |
| Resolved, document with bindings | `b` | full key, secret rights, `Version`, `Hash`, the resolved `Content` | yes |

- **`Hash`.** The effective `Hash` is part of every tag, so a tag is a fingerprint of the effective hash. It is not the whole tag:
  - it is null for an empty document;
  - after a lost cache race it can belong to another write than `Content`;
  - it has only 32 bits;
  - it does not change with `Version`, and the admin UI loads the own version by `Version` (`ui.admin/src/app/composables/useConfiguration.ts:27`).

  With `Version`, `Hash` and `Content` in the tag, the tag is a function of the fields of the body.
- **`Modified`.** Left out. For `Version > 0` it changes only together with `Version`. For a version-0 configuration with no item, it is the time of the read and would defeat every revalidation.
- **Full key.** The `FullKey` of the read (organization, project, view and annotation key) ties a tag to one configuration.
  - The same content under another key gets another tag.
  - Nobody can use their own configuration to make the server compute the tag of someone else's.
- **Prefixes.** A tag of the effective read never matches the resolved read. A tag over resolved bindings never matches a tag of a document without bindings.
- **Secret rights.** Three values:
  - `s`: the caller may read secrets;
  - `w`: secrets are withheld from a machine without mTLS;
  - `p`: neither.

  A caller who may read secrets and a caller who may not never share a resolved tag. `w` and `p` get the same body, but only `w` gets `X-2S-Secrets-Withheld`. With separate tags, a client that moves from one to the other gets a 200 with the right headers, not a 304 that keeps the old ones.

### 2. Conditional answer (`Api.Functions`)

New `Api.Functions/src/ConditionalReads/ConditionalRequestExtensions.cs`, extensions of `HttpRequestData`:

- **`MatchesIfNoneMatch(EntityTagHeaderValue tag)`**
  - Reads every `If-None-Match` value with `Headers.TryGetValues`, so several header lines and comma lists both count.
  - Parses them with `Microsoft.Net.Http.Headers.EntityTagHeaderValue.TryParseStrictList`. The lenient `TryParseList` would skip junk and still find a tag inside it.
  - `*` matches, and is checked on its own: `Compare` never matches `*` with a tag. Otherwise any listed tag matches by weak comparison (RFC 9110 §13.1.2).
  - A header that does not parse is ignored, and the read answers 200.
- **`OkOrNotModified(object value, EntityTagHeaderValue tag)`** sets `ETag`. It returns `StatusCodeResult(304)` when `If-None-Match` matches, and `OkObjectResult(value)` otherwise.
- **`MarkNoCache()`** sets `Cache-Control: private, no-cache`. Shared caches never store the response, and a browser revalidates its copy before each use. That makes the revalidation deterministic instead of heuristic.

The helpers live in a new file, not in `HttpRequestDataExtensions.cs`, because open PRs edit that file.

### 3. The two reads (`ConfigurationHttp`)

The scope check, the access check, the key parsing and the `404` run first and stay as they are. A caller without access never learns a tag, nor whether the document changed.

**Effective read**
1. `GetRawConfigurationAsync`, as today.
2. `MarkNoCache`, then `OkOrNotModified` with the `e` tag.

**Resolved read**
1. The secret rights, as today.
2. `GetRawConfigurationAsync(fullKey)`. Null answers 404, as today.
3. When `IConfigurationService.HasBindings(effective)` is false, the resolved document is the effective document. The read answers with the `r` tag and does not call the resolver. A poll costs one point read, like the effective read.
4. Otherwise `ResolveConfigurationAsync(fullKey, effective, rights.CanReadSecrets)`, then the answer with the `b` tag over the resolved body.
   - A 304 saves the transfer, not the evaluation.
   - Every key/value lookup and vault call still runs, with its usage counters and audit entries.
   - A document that reads the clock gets a new tag on every read.
5. Headers, set only on 200 and 304 and never on 400, 404 or 422. Both paths set them:
   - `X-2S-Secrets-Withheld` when secrets were withheld;
   - `Cache-Control: no-store` when the caller may read secrets, `private, no-cache` otherwise;
   - `ETag`.

   RFC 9110 §15.4.5 asks a 304 to repeat what the 200 would send. Today these headers are set after the resolve, so a `422` carries none of them; that stays.

### 4. Key of the tags over resolved bindings

A resolved body with bindings can carry secret plaintext. A plain hash of it in a header could be brute-forced offline for a low-entropy secret if headers end up in a log. These tags are therefore HMAC-SHA256.

- **Options.** New `ConditionalReadOptions` in `Api.Functions/src/ConditionalReads`, section `ConditionalReads`, one setting `EntityTagKey`: base64 of at least 32 bytes.
  - `Program.cs` binds it and validates it on start.
  - The validation message names the setting, never its value.
- **Where the key lives.** Never in a committed settings file: `local.settings.json` is committed. A deployment sets `ConditionalReads__EntityTagKey` as an app setting, preferably a Key Vault reference. The key serves only the entity tags.
- **Without a key.** Each process draws a random 32-byte key once with `RandomNumberGenerator`.
  - Tags stay correct but differ between instances, so a poller that lands on another instance gets a 200.
  - Set the key to share tags across instances.
- **No constructor change.** The handlers read the options with `FunctionContext.InstanceServices.GetService<IOptions<ConditionalReadOptions>>()`, as `MachineCredentialPolicy` does, and fall back to the process key when there are none. The `ConfigurationHttp` constructor stays as it is.

The other two tags are not keyed, so they are equal across instances with or without the setting. They hash the stored document, which is not secret: every reader of the project gets it from the effective read, browsers may keep it, and exports and version diffs carry it. Secrets belong in key/value pairs or Key Vault, which only the `b` tag can contain.

### 5. Binding detection (`Binding.Abstractions`, `Binding.Object`)

The registered resolver decides whether a document has bindings, not the handler. `AddConfigurationBindings` registers the resolver with `TryAddSingleton`, so a deployment can replace it, and only the resolver knows what it changes.

- **`IConfigurationBindingResolver.ContainsBindings(JToken content)`.** A new member whose default implementation returns `true`. A resolver that does not say otherwise is always run.
- **`ConfigurationBindingResolver.ContainsBindings`**:
  - returns true for any string that starts with `@` and for any object with a string `$binding`;
  - looks at values only, never at property names;
  - shares its two predicates with `BindStringAsync` and `TryReadEnvelope`, so the detection and the resolver cannot drift apart;
  - errs towards resolving. A `$binding` with no known kind and no `$definition` is left as stored by the resolver, but it still counts.

### 6. Resolve step (`Backend.Core`, `Backend.CosmosDb`)

Two new members of `IConfigurationService`, next to the resolved reads:

- **`bool HasBindings(Configuration effective)`**: whether the resolved read would change the effective document. It is false without a binding resolver, which is when `GetResolvedConfigurationAsync` already returns the effective document. Otherwise it asks the resolver's `ContainsBindings`.
- **`Task<Configuration> ResolveConfigurationAsync(FullKey key, Configuration effective, bool includeSecrets = false)`**: evaluates the bindings of a configuration returned by `GetRawConfigurationAsync`, in place, and returns it. `Version`, `Modified` and `Hash` stay the effective ones.

`CosmosConfigurationService.GetResolvedConfigurationInternalAsync` becomes `GetRawConfigurationAsync` followed by `ResolveConfigurationAsync`.

`FakeConfigurationService` (`Backend.Core/test/Transferring/TransferFakes.cs`) gets both members, throwing `NotSupportedException` like its other reads.

### 7. OpenAPI

On both operations:
- a `304 Not Modified` response without a body;
- an `ETag` response header on the 200 and the 304, through an `IOpenApiCustomResponseHeader` class;
- an operation description that says to send the tag back in `If-None-Match`.

`If-None-Match` is not declared as a parameter. NSwag would add a positional parameter without a default to `GetConfigurationAsync` and `GetResolvedConfigurationAsync`, and the `sform` callers would have to change.

The OpenAPI document and the SDKs are not regenerated in this change. PRs #99 and #105 both regenerate them, and the next regeneration picks this change up.

### 8. Tests

- **`Binding.Object/test`**
  - Detection cases, including a `@` in a property name, which does not count.
  - A document without bindings passes through `ResolveAsync` unchanged, with a string executor that fails when called.
  - The default `ContainsBindings` of the interface returns true.
- **`Backend.CosmosDb/test`** (no Cosmos; the service is built with a null repository provider)
  - `ResolveConfigurationAsync` passes `includeSecrets`, gives the resolver a copy of the document and the configuration key, and returns the same configuration.
  - Without a resolver it returns the configuration as it is, and `HasBindings` is false.
  - `HasBindings` asks the resolver.
- **`Api.Functions/test`**: a new `ConfigurationConditionalReadTests`.
  - **Setup.**
    - A `DispatchProxy` fake of `IConfigurationService`, so later interface changes do not break it.
    - An ASP.NET Core `DefaultHttpContext` under the function context item `HttpRequestContext`, which is where `GetHttpContext()` looks, so the response headers can be asserted.
    - A service provider that stays alive for the call. It registers `Options.Create(...)` for the cases with a configured key.
    - Secret rights come from machines, because a `DEV_AUTH` build gives every user secret rights:
      - a machine with a certificate is `s`;
      - a machine without one, in a project with `RequireMtlsForSecretDelivery`, is `w`;
      - a machine without the secrets scope is `p`.
  - **Effective read.**
    - `ETag` and `Cache-Control` on the 200.
    - 304 for the same tag, for its strong form, inside a list, on a second header line, and for `*`.
    - 200 for another tag and for a header that does not parse, including junk around a valid tag.
    - The tag changes with `Version`, `Hash`, `Content` and the full key.
    - No tag on 404.
  - **Resolved read.**
    - A document without bindings skips the resolver, and its tag differs from the effective one.
    - `s`, `w` and `p` get three different tags. `no-store` and the withheld header repeat on the 304.
    - Tags over bindings follow the resolved content. They are equal for one key across service providers and differ between keys.
    - No tag on 422.
  - **OpenAPI.** The document declares the 304 and the `ETag` header.
- **Both builds:** Debug (`DEV_AUTH`) and Release.
- **Manual check.** If the local host can run (Aspire or `func start` with the Cosmos emulator), send the same read twice with `curl -i`, the second time with `If-None-Match`. Confirm a 304 without a body that carries `ETag` and `Cache-Control`.

### 9. Documentation

- **`docs/Platform/Storyteller/inheritance.md`:** a new `## Conditional reads` after "Other reads". It covers:
  - the tags, the 304 and the headers;
  - the key;
  - what a 304 does not save;
  - that writes ignore `If-Match`.
- **`docs/Platform/Storyteller/key-values.md`:** the bullet "Each key is looked up once per resolved read" says that a conditional read looks the keys up too.
- **`docs/Platform/Storyteller/binding.md`:** one sentence in the overview.
- **`docs/42for.net/platform/using-the-platform.md`:** "What a service reads", the public page that shows both reads, tells clients to send the tag back in `If-None-Match`.

## Out of scope

- **`If-Match` on writes.** Writes keep ignoring `If-Match`. The tags are weak, and `If-Match` uses strong comparison, so a tag could never satisfy it. A JSON Patch `test` operation stays the way to guard a write.

- **A cheap 304 for documents with bindings.** It would need to record the key/values, vault versions and annotations a resolve reads, and validate them without evaluating. A later change can collect them during a resolve.
- **Client helpers.** A conditional-read helper in the SDKs. The TypeScript fetcher throws on a 304 when script sets `If-None-Match` itself.
- **Other conditional forms.** `HEAD`, `If-Modified-Since`, and conditional listings.
- **The `ETag` in cross-origin JavaScript.** Platform CORS cannot expose response headers.

## Merge notes

- **PR #106 (424-48).** `MachineRestrictionHttpTests` expects `GetConfigurationResolved` to call `GetResolvedConfigurationAsync` exactly once. Whichever change merges second updates that expectation to `GetRawConfigurationAsync`.
- **PR #103 (424-44).** It invalidates descendants on the first write of a configuration. Until it lands, the effective tag shares that staleness with the body.
