# Configuration Inheritance

This document describes how Storyteller builds the effective configuration JSON for one annotation, and how ancestor annotations change that JSON.

The read path is `CosmosConfigurationService.GetRawConfigurationAsync`. The merge itself is `CalculateAndCacheConfigurationAsync`. The ancestor graph is `BuildInheritanceGraph`. All three live in `src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`.

A product-level walkthrough of the same model is in [Configuration](../../42for.net/platform/configuration.md). Type templates, which are merged during the same calculation, are specified in [templating.md](templating.md). `@` expressions are evaluated later, on the resolved read, and are specified in [binding.md](binding.md).

## Two JSON documents

Each annotation may have one configuration item in the organization container.

| Field | Meaning |
| --- | --- |
| `Content` | The document written at this annotation. Version history stores this object. |
| `CalculatedContent` | The effective document: ancestors, this annotation's type template, and `Content`, merged. |
| `CalculatedContentHash` | Identity of `CalculatedContent`. Eight lowercase hex digits, MurmurHash3 32-bit, seed `42`, of the calculated JSON serialized with `JsonSettingNames.Unique`. |

`GetRawConfigurationAsync` returns a `Configuration` whose `Content` property is the effective document. `Version` and `Modified` come from the target annotation's configuration item. `Hash` is `CalculatedContentHash`.

The item id is `{viewName}.cnf.{annotationKey}`. Subject and context items use the subject partition `{project}.sbt.{subject}`. Every other annotation type uses the responsibility partition `{project}.rst.{responsibility}`. Ancestor keys are read from their own partitions. The walk crosses partitions.

## The ancestor graph

Seven annotation types form one graph. Responsibility and subject are roots. Deeper keys embed the names of their ancestors, so `BuildInheritanceGraph` derives every ancestor key from the key being read. It uses the same organization, project, and view. A second view is a separate catalog: `default` and `rollout` never inherit from each other.

```mermaid
flowchart TB
  rst([responsibility])
  unt([unit])
  sbt([subject])
  usg([usage])
  cnt([context])
  exe([execution])
  uxe([unit of execution])

  rst --> unt
  rst --> usg
  sbt --> usg
  sbt --> cnt
  usg --> exe
  cnt --> exe
  unt --> uxe
  exe --> uxe
```

| Annotation | Key | Direct ancestors, in merge order |
| --- | --- | --- |
| Responsibility | `rst.{responsibility}` | — |
| Subject | `sbt.{subject}` | — |
| Unit | `unt.{responsibility}.{unit}` | Responsibility |
| Context | `cnt.{subject}.{context}` | Subject |
| Usage | `usg.{subject}.{responsibility}` | Responsibility, then subject |
| Execution | `exe.{subject}.{responsibility}.{context}` | Usage, then context |
| Unit of execution | `uxe.{subject}.{responsibility}.{context}.{unit}` | Unit, then execution |

Merge order is `AnnotationType`: Responsibility (0), Unit (1), Subject (2), Usage (3), Context (4), Execution (5), Unit of execution (6). `InheritanceGraphNode.GetAncestors` sorts the direct parents by that value. The enum order only sorts one node's parents. It does not flatten the whole graph into a single list.

Two nodes are shared:

- On an execution, the usage branch and the context branch point at one subject node.
- On a unit of execution, the unit branch and the usage branch point at one responsibility node.

`InheritanceGraphNode.Descendant` is maintained while the graph is built and is unused by the calculation. The walk is from the requested annotation toward the roots.

## How a read is assembled

`GetRawConfigurationAsync` does the following.

1. Load `{view}.cnf.{annotationKey}` from the annotation's partition. An item stamped with `AnnotationDeletionId` belongs to a soft-deleted annotation and returns `null` ([annotation-lifecycle.md](annotation-lifecycle.md)).
2. When that item is missing, require an active annotation record (`{view}.{annotationKey}` in the same partition, without `Deletion`). A missing or deleted annotation and a missing configuration item return `null`. An empty effective document is returned when the annotation exists and nothing contributes JSON.
3. When the item already has `CalculatedContent`, return it. Ancestors and templates are left unread.
4. Otherwise build the graph and run `CalculateAndCacheConfigurationAsync` on the requested node.
5. Reload the configuration item so the response carries the hash just written.
6. Return that item with the calculated object as `Content`. When no item exists after calculation, return the calculated object with version `0`, stamped as the calculation by `System`.

`CalculateAndCacheConfigurationAsync` runs for the requested node and, recursively, for every ancestor:

1. Load this node's configuration item.
2. When `CalculatedContent` is already stored, return it.
3. Start from an empty object.
4. For each direct ancestor in `AnnotationType` order, calculate that ancestor and `MergeInto` its calculated document.
5. `MergeInto` the view template of this node's type (`{view}.gen.{typeCode}` in partition `{project}.template`), when that item exists. See [templating.md](templating.md).
6. `MergeInto` this node's stored `Content`, when the configuration item exists.
7. When the merged object has at least one property, persist it. An existing item is upserted with `CalculatedContent` and `CalculatedContentHash`; stored `Content`, `Version`, and `Modified` stay as they were. A missing item is created with empty `Content`, `Modified` stamped by `System`, and the merge in `CalculatedContent`. An empty merge is returned and left unstored, so the next read calculates it again.

The value merged from an ancestor is that ancestor's full calculated document. It already contains the ancestor's own ancestors, the ancestor's template, and the ancestor's stored content. A node that has no configuration item contributes an empty object, plus anything its ancestors and its template produced.

Because a shared ancestor is reached through more than one parent, its calculated document is merged once on each branch. The second merge writes those properties again. The second branch reuses the cache row written by the first branch only when that write is visible to its read, which requires the Cosmos account to provide session consistency (or stronger). `CosmosClientProvider` does not set a consistency level, so the client uses the account default. Under a weaker level the second branch may recalculate the shared ancestor, which produces the same document.

## Merge rules

`JObject.MergeInto` (`Backend.CosmosDb/src/JsonExtensions.cs`) calls Newtonsoft `JObject.Merge` with:

- `MergeArrayHandling.Union` — array items are appended. An item that is already present, compared by deep equality, is skipped.
- `MergeNullValueHandling.Ignore` — a null in the incoming object leaves the current value in place.

Objects merge property by property, including nested objects. A later scalar replaces an earlier scalar. A later object overlays the earlier object and keeps earlier properties that the later object does not mention. Arrays grow by union along the whole sequence. A later document cannot drop an inherited property, and it cannot replace an inherited array; it can add items and it can replace scalars.

`$remove` and `$patch` are applied when a document is written, inside `CreateOrUpdateConfigurationAsync`, before `Content` is stored. Calculation only calls `MergeInto`, so those properties are ordinary JSON if they appear in an already stored document or in a template.

Schema documents are checked against stored `Content` at write time. They are assembled separately by `CosmosConfigurationSchemaService` and stay out of the calculated configuration.

## What an ancestor changes

Inside one node's calculation the sequence is fixed: ancestor documents, then this node's template, then this node's stored `Content`. For a property that appears on only one path, the later step wins. The requested annotation's stored scalars therefore win against its own template and against the documents of its direct ancestors.

A shared ancestor breaks that reading as soon as a second branch is merged. The later branch carries the shared ancestor again, and that second copy is applied after the earlier branch's overrides.

### Execution

Direct parents are the usage document, then the context document. Each is already calculated:

- Usage = responsibility, then subject, then the usage template, then the usage content.
- Context = subject, then the context template, then the context content.

The execution template and the execution content are merged after both parents.

Responsibility values flow only through the usage. A usage scalar that replaced a responsibility scalar is still in the usage document, and the context document does not carry the responsibility, so that usage scalar survives unless the context or the execution sets the same property.

Subject values flow through both parents. The context document contains the subject again. Merging the context after the usage writes the subject's scalars back over the usage wherever the context left them untouched.

Subject `sbt.northwind`:

```json
{ "currency": "NOK", "residency": "norway" }
```

Usage `usg.northwind.invoicing`:

```json
{ "currency": "USD", "plan": "enterprise" }
```

Context `cnt.northwind.production`:

```json
{ "endpoint": "https://northwind.example/invoices" }
```

The usage calculation produces `{ "currency": "USD", "residency": "norway", "plan": "enterprise" }`. The context calculation produces `{ "currency": "NOK", "residency": "norway", "endpoint": "https://northwind.example/invoices" }`. Merging the context after the usage restores `currency` to `NOK`. `plan` stays, because the context document has no `plan`. The effective execution document is:

```json
{
  "currency": "NOK",
  "residency": "norway",
  "plan": "enterprise",
  "endpoint": "https://northwind.example/invoices"
}
```

A scalar the context itself sets still wins over the usage, and a scalar the execution stores wins over both. In the public guide's invoicing example, `retries` moves from `3` on the responsibility to `5` on the context for that reason, and the usage never set `currency`, so the subject's `NOK` is the only currency either branch contributes.

Arrays do not show this reversal. Union of the same items is idempotent, and a usage that added an item keeps it when the context is merged, because the context's copy of the subject array has no extra item that would remove it. Nothing in the merge removes an item.

### Unit of execution

Direct parents are the unit document, then the execution document:

- Unit = responsibility, then the unit template, then the unit content.
- Execution = the execution calculation from the previous section.

The unit-of-execution template and stored content are merged last, so they win against both parents.

The execution document contains the responsibility again, through the usage. A unit scalar that replaced a responsibility scalar is overwritten by that responsibility value, unless the usage, the context, or the execution also set the property. A unit property the execution path never mentions is kept. The execution path's scalars win over the unit for every property both documents contain.

Responsibility `rst.invoicing` stored `{ "retries": 1 }`. Unit `unt.invoicing.export` stored `{ "retries": 9, "schedule": "0 * * * *" }`. No usage, context, or execution document sets `retries`. The unit of execution stores `{ "schedule": "0 2 * * *" }`.

The unit calculation holds `retries: 9`. The execution calculation holds `retries: 1` from the responsibility. Merging the execution after the unit restores `retries` to `1`. The unit-of-execution document then replaces `schedule` and leaves `retries` alone:

```json
{ "retries": 1, "schedule": "0 2 * * *" }
```

A usage that stored `retries: 4` would put `4` into the execution document, and `4` would replace the unit's `9`. The unit-of-execution document can set `retries` itself and then win.

### Nested objects

A nested object follows the same property merge. Ancestor fields inside it remain when a later document sets only some of the fields.

Responsibility `{ "address": { "city": "Oslo", "zip": "0001" } }` and execution `{ "address": { "city": "Bergen" } }` calculate to:

```json
{ "address": { "city": "Bergen", "zip": "0001" } }
```

## Cache and descendant freshness

A later read of an item whose `CalculatedContent` is present returns that object and skips the walk. `ConfigurationEntity.IsCachingDisabled` is stored on the item and is unread by this path, so the flag leaves caching on.

These writes clear the target item's `CalculatedContent` and `CalculatedContentHash`, and clear the same fields on the descendants whose effective document includes this annotation. The patch also increments `AffectedCounter`.

| Written annotation | Descendants whose cache is cleared |
| --- | --- |
| Responsibility | Every configuration item in that responsibility partition and view |
| Unit | Unit-of-execution items in that partition and view whose id ends with `.{unit}` |
| Usage | Execution and unit-of-execution items in that partition and view for that subject |
| Subject | Context items in the subject partition and view, plus usage, execution, and unit-of-execution items of that subject in the same project and view |
| Context | Execution items of that subject ending in `.{context}`, and unit-of-execution items of that subject whose id contains `.{context}.`, in the same project and view |
| Execution | Unit-of-execution items in that partition and view under that subject, responsibility, and context |
| Unit of execution | None |

`CreateOrUpdateConfigurationAsync` on an item that already exists, and `PatchConfigurationAsync`, first invalidate descendants and then null the target cache in the same write as the new `Content`. `ClearConfigurationAsync` and `DeleteAsync` remove the item when it has stored content, or delete a system cache row directly, and then invalidate descendants.

The first insert, when no configuration item exists yet, creates the item with `Content` and no cache, and then invalidates descendants. An import of a key without a configuration item takes the same path. A system cache row counts as an existing item. A first document written over one, by `CreateOrUpdateConfigurationAsync` or by an import that reports `Created`, takes the existing-item path, which invalidates before it writes.

The first insert invalidates after the item is created, not before. A calculation caches its result on an existing item only while the item still has the ETag that was read before the walk, and the invalidation patch changes that ETag. A descendant calculation that read the ancestor as missing is therefore either cached before the invalidation, which clears it, or fails to cache after it.

This holds for each item, not for the invalidation as a whole:

- The patches go out in transactional batches of at most 100 items per partition, several at a time and in no fixed order. A descendant cleared in an earlier batch can be calculated again from an intermediate descendant whose stale cache is still in a later batch, and cached with the old document. For example, an execution can be recalculated from its usage before the usage is cleared.
- A descendant item that a calculation creates after the invalidation has queried the items is not reached.

Both gaps apply to every write that invalidates, not only to the first insert.

A type-template write through the service invalidates every configuration that merges the template. A template item written directly into Cosmos leaves existing `CalculatedContent` in place. [templating.md](templating.md#cache) covers both cases.

The invalidation skips configuration items of soft-deleted annotations (`AnnotationDeletionId` set), so it never rewrites them and never pushes back their expiry. A restore clears `CalculatedContent` and `CalculatedContentHash` of every configuration it brings back, because templates or ancestors may have changed while the annotation was deleted.

## Other reads

| Read | JSON it returns |
| --- | --- |
| `GetRawConfigurationAsync` | The effective document, including ancestors and templates. |
| `GetResolvedConfigurationAsync`, with or without secrets | That same document, then `@` bindings evaluated in place. |
| `GetConfigurationHierarchyViewAsync` | One property per visited annotation key. The value is that annotation's stored `Content`. Ancestors are walked with the same graph, and a shared node is recorded once. A key is omitted when it has no configuration item. A system cache row from an earlier calculation has empty stored `Content`, so that key appears as `{}`. |
| `GetConfigurationVersionContentAsync` | The stored document of that version. |
| `GetConfigurationViewChangesAsync` | A diff of the effective document of one annotation in two views. |
| `GetConfigurationCompositionAsync` | The effective document composed again from the stored documents, with every merged document, the calculations, the merges in order and the merge that set each value. See "Composition read". |

`GetConfigurationHierarchyViewAsync` is the per-level breakdown. `GetRawConfigurationAsync` is the single merged object.

## Composition read

`GET v1/{organization}/{project}/{view}/configuration/{key}/composition` explains the effective document: which documents are merged, in which order, and which one set each value. The service method is `GetConfigurationCompositionAsync` (`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.Composition.cs`), the model is `ConfigurationComposition` (`Abstractions.Annotations/src/Model/ConfigurationComposition.cs`). The admin UI shows it in the Composition view of the Effective tab, and `sform story config composition <key>` prints it.

### What it does

It walks the same graph as the calculation, node by node: the finished document of each direct ancestor in enum order, then the template of the node's type, then the node's stored `Content`. It reads the stored documents only. It never reads `CalculatedContent`, never writes, and creates no cache item. Each configuration item and each template is read once per request, and a shared node reuses the read, but the merges are repeated per branch, as in the calculation. Deleted ancestors are not filtered, as in the calculation.

The root follows the effective read: a soft-deleted annotation answers `404 AnnotationDeleted`, an annotation without an item answers `404` unless it is active, and an existing item is composed without a state check.

### The response

| Field | Content |
| --- | --- |
| `Content` | The effective document, composed again. |
| `Hash` | The hash of `Content`, calculated as `CalculatedContentHash`; null for an empty document. |
| `Sources` | Every merged document once: `Kind` (`Configuration` or `Template`), `AnnotationType`, `AnnotationKey` (configurations), `Presence`, `Version`, `Modified` and the stored `Content`. Ids are `configuration:{annotationKey}` and `template:{typeCode}`. |
| `Nodes` | The calculations, unfolded into a tree: `Index`, `AnnotationKey`, `Depth` (0 for the target) and `MergedInto`, the node whose calculation merges this one (null for the target). A shared ancestor has one node per branch. Nodes are listed in the order they finish, so the target is last. |
| `Steps` | The merges of stored documents in the order of application: `Order` (from 1), `SourceId` and `Node`. Each node applies its template, when the view has one for the type, and then its configuration. |
| `Origins` | `Path`, an RFC 6901 JSON Pointer into `Content`, and `Step`, the merge that set the value. One per value and container, the root left out, in document order. |

Every node has a configuration step, so the whole lineage shows. A configuration without an item is `Missing` and applies nothing. So is an item with version 0 and empty `Content`: the calculation writes such an item only to cache an effective document (see "How a read is assembled"), and every write gives version 1 or more. The composition therefore stays the same after the first effective read. `Empty` is a stored document without properties. A template exists as a step only when the view has it.

On an execution with a template for every type the nodes are `rst` (via `usg`), `sbt` (via `usg`), `usg`, `sbt` (via `cnt`), `cnt`, `exe`, and the twelve steps are each node's template and then its configuration. On a unit of execution the nine nodes are `rst` (via `unt`), `unt`, `rst` (via `usg`), `sbt` (via `usg`), `usg`, `sbt` (via `cnt`), `cnt`, `exe`, `uxe`.

### Origins

The origins come from the merge itself. Each applied document is cloned and every value in it is marked with its step (a Newtonsoft annotation). `MergeInto` clones the values it takes, and a clone keeps the mark; the values it keeps keep theirs. `ConfigurationCompositionOrigins` reads the marks after the target.

So the rules are the merge rules:

- a value added to a missing property, or replacing another value, keeps the step it had in the incoming document;
- a scalar equal to the existing one (same type and value) is not replaced, so the value keeps the step that first set it; `1` and `1.0` differ;
- an object merged into an object keeps its step, and its properties are attributed one by one;
- an incoming `null` on an existing property changes nothing;
- an array merged into an array keeps its step and its items; incoming items already present are skipped, and the appended ones keep their steps.

In the execution example of "What an ancestor changes", `currency` comes from the subject's configuration merged through the context, `residency` from the subject's configuration merged through the usage (the second merge repeats the same value), `plan` from the usage and `endpoint` from the context. A step can set no value at all.

### Replaying

A client can rebuild `Content` from the response. For each node in order: start from `{}`, merge the result of every node whose `MergedInto` is this node, in order, then merge the `Content` of this node's steps, in order, skipping `Missing`. The result of the last node is `Content`.

A flat replay of the steps is not the same. Merging a finished document is not the same as merging its parts one after the other. On an execution, if the usage branch has already put `a: {x: 1}` into the execution's document, and the context is composed of a template `a: 5` and its own `a: {y: 2}`, the context's document is `a: {y: 2}`, and merging it gives `a: {x: 1, y: 2}`. Merging the context's template and then its own document gives `a: {y: 2}`, because `5` replaces the object first.

### Access and tags

Like the effective read, the read needs the Reader role, or the scope `Configuration.Read` for a machine. Like the templates, it needs access to the whole project (`CheckAccessToProjectAsync`). The response shows ancestor documents in full, including values the annotation overrides, and the templates. A machine restricted to one annotation therefore gets `403 AnnotationRestricted`, also for its own annotation; it gets its effective configuration from the effective read. Bindings are not evaluated.

The read answers with a weak `ETag` of kind `c` (see "Conditional reads").

### Consistency

The composition reads up to seven configuration items in two partitions and up to seven templates, one after the other, without a snapshot. A write between two reads can give a composition that no single moment had. The effective read can also serve a stale cache (see "Cache and descendant freshness"). In both cases the composition's `Hash` differs from the effective `Hash`. The admin UI then reads the effective document again and warns if they still differ.

## Conditional reads

The effective read (`GET .../configuration/{key}`), the resolved read (`.../resolved`) and the composition read (`.../composition`) answer with a weak `ETag`. A client sends it back in `If-None-Match`. When the configuration is the same, the API answers `304 Not Modified` without a body. `If-None-Match: *` matches any configuration that exists, and a header that is not a valid list of entity tags is ignored. The code is in `Api.Functions/src/ConditionalReads` and `ConfigurationHttp`.

### The tags

A tag is `W/"<kind>.<digest>"`. The digest is 16 bytes of SHA-256, base64url, over these inputs: the full key (organization, project, view, annotation key), `Version`, `Hash` and the JSON of `Content`. Each input is length-prefixed.

| Kind | Read | Also hashed |
| --- | --- | --- |
| `e` | Effective | – |
| `r` | Resolved, document without bindings | The secret rights |
| `b` | Resolved, document with bindings | The secret rights; `Content` is the resolved one, and the digest is HMAC-SHA256 |
| `c` | Composition | Instead of `Version`, `Hash` and `Content`: the whole composition as JSON, so a change of any merged document changes the tag, even one that leaves the effective document as it was |

- **`Hash`.** `Hash` (`CalculatedContentHash`) is part of every tag, but it is not the tag:
  - it is null for an empty document;
  - after a lost cache race it can come from another write than `Content`;
  - it has 32 bits;
  - it stays the same when an own write bumps `Version` without changing the merge.

  With `Version`, `Hash` and `Content` hashed together, the tag follows every field of the body that can change.
- **`Modified`.** Not hashed in `e`, `r` and `b`. Past version 0 it changes only with `Version`. A configuration without an item gets `Modified` set to the time of the read, which would defeat every revalidation. The `c` tag hashes the stamps of the merged documents: a stored document's stamp changes only with its version, and a `Missing` one has none.
- **The full key.** The same document under another key gets another tag, so nobody can make the server compute someone else's tag in their own configuration.
- **The secret rights.** There are three values:
  - the caller may read secrets;
  - secrets are withheld from a machine with `Secrets.Read` that has no mTLS, which also gets `X-2S-Secrets-Withheld`;
  - neither.

  Callers with different rights never share a resolved tag.

### The resolved read

`IConfigurationService.HasBindings` asks the registered binding resolver whether resolving could change the effective document. `ConfigurationBindingResolver.ContainsBindings` looks for a string that starts with `@` and for an object with a string `$binding`. A resolver that does not override the check is always run.

| Document | What the read does | What a 304 saves |
| --- | --- | --- |
| No bindings | The effective document is the resolved one. It is tagged `r` and answered without calling the resolver. | Everything but the configuration point read and the access checks |
| Bindings | `ResolveConfigurationAsync` evaluates it, and the resolved body is tagged `b`. | The transfer only |

For a document with bindings, every conditional read still evaluates the bindings:
- key/value lookups with their usage counters and audit entries;
- Key Vault calls;
- `@annotation` reads;
- JSON-e.

A document that reads the clock (JSON-e `now`, `$fromNow`) gets a new tag on every read.

The `b` tags are keyed because a resolved body can carry secret values, and a plain hash in a header could be brute-forced for a weak secret. The key comes from `ConditionalReads:EntityTagKey`: base64 of at least 32 bytes, validated on start.
- Set it as an app setting or a Key Vault reference, never in a committed settings file.
- Without it, each process draws a random key. Tags stay correct but differ between instances, so a poller that lands on another instance gets a 200.

The `e` and `r` tags are not keyed. They hash the stored document, which every reader of the project gets from the effective read anyway. Secrets belong in key/value pairs or Key Vault.

### Headers

The headers are the same on a 200 and a 304, and are never sent on a 400, 404 or 422. The scope check, the access check and the `404` run before the tag is compared, so a caller without access learns nothing from it.

| Header | Value |
| --- | --- |
| `ETag` | The tag |
| `Cache-Control` | `no-store` when the caller may read secrets (resolved read). Otherwise `private, no-cache`: shared caches do not store the response, and a browser revalidates its copy before each use. |
| `X-2S-Secrets-Withheld` | `mtls-required` when secrets were withheld |

The admin UI calls the API cross-origin. It cannot read the `ETag`, but the browser revalidates on its own and turns a 304 back into the stored 200.

### Limits

- **Writes ignore `If-Match`.** The tags are weak, and `If-Match` needs a strong tag. A JSON Patch `test` operation guards a write instead.
- **The `e`, `r` and `b` tags follow the effective cache.** A descendant whose `CalculatedContent` is stale (see "Cache and descendant freshness") keeps its tag until the cache is cleared. The `c` tag follows the stored documents, so it changes at once, and the composition's `Hash` can then differ from the effective `Hash` (see "Consistency").
- **The generated SDK reads return only the body.** They drop the `ETag` and throw on a 304. A polling client uses the conditional methods instead, which return the body, the `ETag`, `Cache-Control` and `X-2S-Secrets-Withheld`, and a 304 without throwing: `GetConfigurationConditionalAsync` and `GetResolvedConfigurationConditionalAsync` of the C# SDK ([readme](../../../src/Platform/Storyteller/Sdk.NSwag/README.md#conditional-reads)), and `getConfigurationConditional` and `getResolvedConfigurationConditional` of the TypeScript SDK ([readme](../../../src/Platform/Storyteller/sdk.typescript/README.md#conditional-reads)). The [.NET configuration provider](configuration-provider.md) polls with them.
- **Not covered:** `HEAD`, `If-Modified-Since` and the listings.
