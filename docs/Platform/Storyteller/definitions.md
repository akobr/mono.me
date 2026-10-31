# Configuration schemas

This document defines how a JSON Schema constrains the stored configuration of an annotation in Storyteller: where a schema lives, how layers combine, how a write is checked, and how a breaking change is published.

The contract below is what the Storyteller service, HTTP API, and `sform` commands implement. A schema belongs to one view, carries an author and a version history, and a write that does not comply is rejected unless the caller passes `force=true` on the API or `-f`/`--force` on the CLI. The service and API review is [Phase A of JSON Schema revision](specs/reviews/2026-10-01%20Phase%20A%20of%20JSON%20Schema%20revision.md). The CLI review is [Phase B of JSON Schema revision](specs/reviews/2026-10-01%20Phase%20B%20of%20JSON%20Schema%20revision.md).

A schema documents the shape of a configuration and lists the values that are allowed. It is a JSON Schema document stored by Storyteller and evaluated with NJsonSchema. Keywords such as `description`, `title`, and `examples` are kept and returned with the document. Keywords such as `type`, `required`, `properties`, `enum`, `const`, `minimum`, and `additionalProperties` are what a write is checked against.

## What a schema applies to

A schema is stored for one project and one view. Views are isolated, the same way [configuration templates](templating.md) are. A schema in the `default` view is a different document from a schema in the `rollout` view, with its own author and its own version history. Saving a schema in one view leaves every other view untouched.

Three stored kinds exist. A read of the schema that will actually be enforced for one annotation combines them.

| Kind | Identifies | Applies to |
| --- | --- | --- |
| Type | Annotation type code (`rst`, `sbt`, `usg`, `cnt`, `exe`, `unt`, `uxe`) | Every configuration of that type in the view |
| Descendant type | An ancestor annotation, plus a descendant type code | Configurations of that descendant type in the view whose ancestor list includes that annotation |
| Annotation | One annotation key | The configuration of that annotation in the view |

The combined schema for an annotation is produced in this order, and only from the same view:

1. The type schema for the annotation's type.
2. Each ancestor's descendant-type schema, in the order `AnnotationHierarchy.GetAncestorSchemaSources` returns.
3. The annotation schema for that exact key.

A missing layer is skipped. When none of the three exist, the annotation has no schema and a write is stored without a schema check.

### Ancestor descendant-type schemas

A descendant-type schema sits on an ancestor and names the type of configuration it constrains.

| Configuration being checked | Ancestors that can define a descendant-type schema |
| --- | --- |
| Unit `unt.{responsibility}.{unit}` | The responsibility, for type `unt` |
| Context `cnt.{subject}.{context}` | The subject, for type `cnt` |
| Usage `usg.{subject}.{responsibility}` | The responsibility and the subject, for type `usg` |
| Execution `exe.{subject}.{responsibility}.{context}` | The responsibility, the subject, the usage, and the context, for type `exe` |
| Unit of execution `uxe.{subject}.{responsibility}.{context}.{unit}` | The responsibility, the subject, the usage, the context, the execution, and the unit, for type `uxe` |

Responsibility and subject configurations have no ancestors, so only a type schema and an annotation schema can apply to them.

A descendant-type schema can be written only for these pairs (`DescendantTypeRules`). The `PUT` of any other pair, such as `rst` below `exe.s.r.c`, is rejected with `400` and `InvalidKey` before anything is read or stored, also with force; such a layer would never apply to anything. An import fails such a file with the same code and message. The reads, the versions, the diffs and the `DELETE` take any pair, so a document stored before this check can still be read and deleted.

### How the layers combine

Combination is a deep merge of the schema documents, from the type schema through the ancestor schemas to the annotation schema. A later layer wins where both documents set the same keyword. Two exceptions keep the merge additive:

- Nested objects merge property by property, so a later layer can add a property without dropping the ones declared earlier.
- `required` arrays are unioned. A property required by any layer stays required. A later layer cannot remove a name from `required`.

`default.cfs` for executions in `billing` / `default` might require a retry count:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["retries"],
  "properties": {
    "retries": {
      "type": "integer",
      "minimum": 0,
      "description": "How many times an execution repeats a failed call."
    }
  }
}
```

The responsibility `rst.invoicing` can add a descendant-type schema for `exe` that requires a currency, and the execution `exe.northwind.invoicing.prod` can narrow `retries` further. The combined schema for that execution in the `default` view then requires both `retries` and `currency`, and the `retries` keyword is the later, stricter one (`minimum: 1`). The `rollout` view does not see any of these documents unless it has its own.

## Identity

Schema documents live in the organization container (`org.{organization}` in database `42.Platform.2S`), in the partition `{project}.schema`. The view and the kind are part of the id.

| Kind | Id |
| --- | --- |
| Type | `{view}.cfs.t.{typeCode}` |
| Annotation | `{view}.cfs.a.{annotationKey}` |
| Descendant type | `{view}.cfs.d.{descendantType}.{annotationKey}` |

`default.cfs.t.exe` in `billing.schema` is the execution schema of the `default` view in project `billing`. `rollout.cfs.t.exe` in the same partition is a different schema.

Each document stores:

| Field | Role |
| --- | --- |
| `Content` | The JSON Schema object. |
| `Modified` | The [audit stamp](audit-stamps.md) of the current version: who wrote it (the caller) and when. |
| `Version` | Current version number, starting at `1`. |
| `ViewName` | The view this document belongs to. |

History and a small state record live in the same partition. See [Versions](#versions).

### Schemas written before views were part of the identity

Older documents were stored once per project, with an empty view:

- type schemas as `cfs.{typeCode}` in `{project}.schema`
- annotation schemas as `cfs.{annotationKey}` in the annotation partition
- descendant-type schemas as `cfs.dt.{descendantType}.{annotationKey}` in the annotation partition

Those items are ignored. A read looks only at the view-scoped id. When that id is absent, the annotation has no schema, and a configuration write is stored without a schema check. The old items are left in place and are not migrated.

## Writing a schema

Send the JSON Schema object itself. The service stamps `Modified` with the caller and the time and assigns `Version`. A schema write replaces the stored schema document. It does not merge into the previous schema the way a configuration write merges into stored content. The merge described above happens only when layers are combined for a check or for a combined read.

The body has to be an object that NJsonSchema can parse. A document it cannot parse is rejected with `400` and `ErrorCode` `InvalidSchema` (`Invalid JSON Schema: …`), including when the caller passes force. Force covers compliance of configuration content. It does not accept a body that is not a schema.

### References

A `$ref` is resolved inside the schema document only: `#` (the whole schema) or a JSON Pointer such as `#/definitions/currency`. The service never fetches a URL and never reads a file, because a schema is data written by a project member and the API would otherwise send requests from its own network position.

- A `$ref` that does not start with `#` is refused with `400` and `ErrorCode` `ExternalSchemaReference`. That includes a URL (`https://…`, also as a meta-schema such as `http://json-schema.org/draft-07/schema#`), a file (`file:///…`, `/etc/…`, `C:/…`), a relative path (`other.json#/definitions/a`) and a URN. The message quotes the reference, cut to 100 characters: `External $ref not allowed: 'https://example.com/a.json'. Only '#' and JSON Pointers inside the schema ('#/…') are resolved.`
- A `$ref` that starts with `#` but is not a JSON Pointer, such as `#foo` or `#definitions/a`, is not supported and is refused with `InvalidSchema`.
- A pointer to a part of the document that does not exist (`#/definitions/missing`) is refused with `InvalidSchema`.

Every reference is resolved when the schema is saved, used or not, so a schema with an unused external reference is refused too. Force does not change any of these.

Before the schema is stored, it is also checked once against an empty object, and the violations are ignored. A schema whose check cannot even start is refused with `InvalidSchema`, also with force. That is a schema that applies itself to the same value again at its root, such as `{ "not": { "$ref": "#" } }` or `{ "oneOf": [ { "$ref": "#" }, … ] }`, or one that is its own `allOf` (`Cyclic references detected.`). That trial check has the [limits of a check](#limits-of-a-check) too: one that runs out of them is refused with `SchemaValidationTimeout`, also with force.

A useful schema is a JSON object schema (`"type": "object"`), because stored configuration content is a JSON object. `description` on properties is how the schema acts as documentation. `enum` and `const` are how it lists the allowed values. `additionalProperties: false` refuses names the schema does not declare.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "additionalProperties": false,
  "required": ["currency", "retries"],
  "properties": {
    "currency": {
      "type": "string",
      "enum": ["EUR", "NOK", "USD"],
      "description": "Settlement currency for this execution."
    },
    "retries": {
      "type": "integer",
      "minimum": 0,
      "maximum": 5
    },
    "features": {
      "type": "array",
      "items": { "type": "string", "enum": ["vat", "audit", "dunning"] }
    }
  }
}
```

`$schema` may name the draft. The dialect that is actually evaluated is the one NJsonSchema accepts.

### HTTP

Routes include the view, like configuration and template routes. The caller sends the schema object as `application/json`.

| Method | Route | Operation |
| --- | --- | --- |
| GET | `v1/{organization}/{project}/{view}/configuration-schemas` | Every schema of the view without its content: `Kind` (`Type`, `Annotation`, `DescendantType`), `AnnotationType`, `AnnotationKey`, `Version`, `Modified` |
| GET, PUT, DELETE | `v1/{organization}/{project}/{view}/configuration-schema/type/{annotationType}` | Type schema |
| GET, PUT, DELETE | `v1/{organization}/{project}/{view}/configuration-schema/{key}` | Annotation schema |
| GET, PUT, DELETE | `v1/{organization}/{project}/{view}/configuration-schema/{key}/type/{annotationType}` | Descendant-type schema on annotation `{key}` for descendant type `{annotationType}` |
| GET | `v1/{organization}/{project}/{view}/configuration-schema/{key}/definition` | Combined schema for `{key}` |

Version routes, on each of the three stored kinds (the combined schema is computed and has no history of its own):

| Method | Route | Operation |
| --- | --- | --- |
| GET | `…/versions` | List versions, oldest first |
| GET | `…/versions/{version}` | Content of one version |
| GET | `…/versions/{version}/diff` | Diff of the previous version into this one |
| GET | `…/versions/{version}/diff/{versionFrom}` | Diff of any pair |

PUT accepts a query parameter `force`. It defaults to false. See [Force](#force).

- Reads require the `Reader` role on the project. A machine needs the scope `Configuration.Read` instead.
- Writes (`PUT` and `DELETE` of the three kinds) require the `Contributor` role on the project. A machine has no role; it needs `Configuration.Write` and the management scope `Schema.Write`, both stored on the machine and carried by its credentials, or the write answers `403 AccessDenied` (see [Scopes of machines](authentication.md#scopes-of-machines)).
- `400` means the body is not JSON or is not a schema NJsonSchema can parse, or the type code or annotation key is unknown. On the descendant-type `PUT` it also means that the type cannot descend from `{key}`; that response carries `ErrorCode` `InvalidKey`. A schema that cannot be used carries one of these codes, also with force (see [References](#references) and [Limits of a check](#limits-of-a-check)):
  - `InvalidSchema`: the body cannot be read or its check cannot start;
  - `ExternalSchemaReference`: a `$ref` points outside the schema;
  - `SchemaValidationTimeout`: reading the schema, or its trial check against an empty object, takes too long.

  Without force, the compliance step answers `400` with the same codes when the configurations cannot be checked, for example because another stored layer refers outside itself or a check runs out of time. With force, the schema is stored (see [Force](#force)).
- `404` means the requested schema or version is absent, or (for a patch of configuration) the configuration has no stored version: it was never written, or it was deleted. A document emptied by an earlier write is still a stored version and can be patched.
- `413` with `ErrorCode` `BodyTooLarge` means the body of a schema, configuration or JSON Patch write is larger than 4 MiB; it is refused before it is parsed. A stored document is one Cosmos item of at most 2 MB.
- `409` with `SchemaValidationErrorResponse` means a compliance check failed and force was not set. Each entry carries `AnnotationKey`, `ViewName`, and the list of messages. A message is the JSON path and the NJsonSchema error kind, for example `#/currency: NotInEnumeration`.
- `409` with `ErrorResponse` means a concurrent writer won the race after the retries. That body is a different type from the compliance failure.
- `500` means storage failed.

Diff endpoints return a `DiffResult`, or unified diff text with `?format=unified`.

### CLI

The `sform` context already carries organization, project, and view. Schema commands use that view.

```text
sform story schema type get exe
sform story schema type set exe --import execution.schema.json
sform story schema type edit exe
sform story schema type delete exe
sform story schema type versions exe
sform story schema type diff exe

sform story schema annotation get rst.invoicing
sform story schema annotation set rst.invoicing --import invoicing.schema.json

sform story schema descendant get rst.invoicing exe
sform story schema descendant set rst.invoicing exe --import exe-under-invoicing.schema.json

sform story schema definition exe.northwind.invoicing.prod
```

`set` creates the schema or replaces it. It takes `-i|--import` and sends that file as the body. `edit` opens the current document in the editor and replaces it with the result; there has to be a schema already. `get` takes `--version` to print one stored version. `diff` takes the target version, an optional source version, and `--format`. `definition` is read-only. Add `-f` or `--force` to `set` and `edit` when the new schema should be stored even though configurations already in the view do not comply. See [Force](#force).

When the API refuses a schema command, `sform` prints the server's message where it has one:

- A `400` prints the message, and its error code on the next line when the response has one, for example `Error code: InvalidKey` for a descendant type that cannot descend from the key. The command exits with `2`.
- A failed compliance check prints the message, then each configuration that does not comply as `annotationKey (view)` with its messages. The command exits with `2`.
- A `404` of `get`, `delete`, `versions`, `diff`, `definition` or of the read of `edit` prints a sentence of `sform`, such as "The schema for 'exe' has not been found.", and exits with `2`.
- A `403`, a `500` or the `409` of a concurrent change on `delete` prints the server's message and exits with `1`.
- A `401` prints the general sentence of `sform` that the request was not authorized and exits with `44`.
- Anything else prints the text of the error and exits with `1`. This includes the `409` of a concurrent change on `set` or `edit`: the SDK reads that response as a compliance failure without entries, so the server's message is not shown.

## What is checked

Validation looks at the stored configuration document, the `Content` that is about to be written. It does not look at the calculated document, the resolved document, inherited values, or template output. A property that exists only because a template or an ancestor contributed it is invisible to the schema. A required property has to be present in the stored document of the annotation being written.

The check runs at two moments. An import of a view runs a variant of both.

**Saving a configuration.** `CreateOrUpdateConfigurationAsync` and `PatchConfigurationAsync` validate the document after merge, `$remove`, and `$patch`, and only when that work actually changed the content. The document is checked against the combined schema of that annotation in that view. An empty object is not checked, so `required` does not apply until the stored document has at least one property. When the view has no applicable schema, the write is stored as given.

**Saving a schema.** Before the new schema is stored, every stored configuration in that same view that the new schema would apply to is checked against the combined schema as it will be after this write. The candidate document is substituted for the layer being saved. The other layers are the ones already stored for the view. The scan is:

| Schema being saved | Configurations checked |
| --- | --- |
| Type `T` in view `V` | Configurations in `V` whose annotation type is `T` |
| Annotation `K` in view `V` | The configuration of `K` in `V` |
| Descendant type `D` on ancestor `A`, in view `V` | Configurations in `V` of type `D` that list `A` as an ancestor for `D` |

Empty stored documents are skipped. Other views are skipped. A schema that is identical to the current document is not written again and is not checked.

**Importing a view.** An [import](export-import.md#overlay) checks a schema against the configurations as the import leaves them. The archive's configurations replace the stored ones, the stored configurations of annotations the import re-creates are not checked, and the other layers come from the archive where it brings them. In a dry run, a configuration is validated against the archive's schemas too. A document that is not a JSON Schema fails with `InvalidSchema`, and one with a reference outside itself with `ExternalSchemaReference`, also with force. When the configurations or a configuration cannot be checked, the file fails with the code, or, with force, is stored with a warning of the code (see [export-import.md](export-import.md#schemas)).

Reads of a configuration do not validate it. A document that fails its schema remains readable, including its calculated and resolved forms, until the next write.

### Limits of a check

A schema is written by any project member, so reading it and checking a document against it are bounded. Hostile and accidental schemas both stay within these limits:

- **A pattern match** of `pattern` or of a `patternProperties` key takes at most `PatternMatchTimeout`, 200 ms by default. The check matches with that timeout itself, so a hostile pattern such as `^(a+)+$` cannot hold the worker. NJsonSchema then repeats the same match with the process default match timeout, which the API host sets to 200 ms too. In the API host, the effective limit is therefore the lower of the two: raising `PatternMatchTimeout` above 200 ms has no effect there, and the message names the limit that was hit. The process default also covers the regular expressions of the `format` validators, among others.
- **The whole check** of one document stops at `CheckBudget`, 10 s by default. The budget is checked at every step of the validator, that is every value checked against every (sub)schema. A step itself stays short, because NJsonSchema's own loops within one step are guarded too:
  - `patternProperties` is matched with the budget checked between matches;
  - an `enum` checked against an object or an array is refused when the comparison would serialize more than 50,000,000 characters (entries × the length of the value);
  - the names of `properties` and `required` that the object lacks are refused when NJsonSchema would compare them with the object's properties more than 50,000,000 times (missing names × properties);
  - whether a schema is nullable, which NJsonSchema asks at every step and for every property whose value is `null`, is answered by a walk through `oneOf` and references. That walk is refused when it needs more than 100,000 calls.

  A check therefore ends within about the budget plus one such step.
- **Reading a schema** takes at most `CheckBudget` too. NJsonSchema resolves a `$ref` to a large map by serializing the map, so many such references could otherwise take minutes.
- **A schema that applies itself to the same value again** makes a check run forever, either through validation (for example `"not": { "$ref": "#" }` below a property) or through the nullability walk (for example `"oneOf": [ { "$ref": "#/properties/x" }, … ]` for `"x": null`). Such a cycle is detected when it is met and refused with `InvalidSchema` ("the schema applies itself to the same value again …, so its check would never end"). A cycle at the root is refused on save (see [References](#references)).
- A pattern that is not a valid regular expression (`"("`) or a `multipleOf` that is 0 is also an `InvalidSchema` when it is first used. That includes `1e-30`, which is 0 as a decimal.

When a limit is exceeded, the write answers `400` with `ErrorCode` `SchemaValidationTimeout`:

- `The pattern '…' of the schema took longer than 200 ms to match a value; simplify the pattern.`
- `Checking the document against its schema takes longer than 10 s; simplify the schema or the document.`
- `Reading the schema takes longer than 10 s; simplify its references.`

These limits measure wall-clock time on a shared host, so a document near them may pass on one attempt and fail on another. They are options of the backend, in the configuration section `ConfigurationSchemas`:

| Option | Default | Range |
| --- | --- | --- |
| `ConfigurationSchemas:PatternMatchTimeout` | `00:00:00.200` | 10 ms to 10 s |
| `ConfigurationSchemas:CheckBudget` | `00:00:10` | 1 s to 5 min |

A configuration `PUT` or `PATCH` that is checked against a stored schema the check cannot use answers `400` with that schema's code (`InvalidSchema`, `ExternalSchemaReference` or `SchemaValidationTimeout`). That includes a schema stored before references outside the schema were refused. Such a schema fails every write it applies to until it is replaced or deleted. Replacing the broken layer itself needs no force, because only the other layers are read from the store. Force is needed when another layer of the same configurations is broken too (see [Force](#force)). A forced configuration write is stored without a check, as before.

Templates are not checked against configuration schemas. Template content is merged at calculation time and is not part of stored `Content`.

## Force

By default a write that fails the check is rejected and nothing is stored. The response is `409` and lists every failing annotation.

That default produces a deadlock when the schema and the stored documents have to change together:

- Publishing the stricter schema first is rejected, because the documents already stored do not comply.
- Updating a document to the new shape first is rejected, because the schema still in force does not allow it.

Force breaks one side of that deadlock for a single call. The caller passes it explicitly. Omitting it leaves the rejection in place.

| Call | Without force | With force |
| --- | --- | --- |
| PUT a schema | Rejected when any in-scope configuration in the view would fail the combined schema after the write (`409`), or when the configurations cannot be checked (`400`, see below) | The schema is stored. Existing configurations are left as they are |
| PUT or PATCH a configuration | Rejected when the document about to be stored fails the combined schema (`409`), or when that schema cannot be used (`400`) | The document is stored without a check |

On the HTTP API the parameter is the query `force=true`. On the CLI it is `-f` or `--force` on `schema type|annotation|descendant set|edit` and on `config set`, `config edit`, and `config patch`. Force is not remembered. The next call defaults to rejection again. Force is not a separate permission: a caller who can write the project can pass it. The author recorded on the new version is the audit of who published the break.

Force does not change the other failure modes. A body that is not JSON, a body that is not a schema, a schema with a reference outside itself or whose check cannot start, an unknown type code or annotation key, a descendant type that cannot descend from its key, a patch of a configuration without a stored version, and a lost concurrency race still fail.

Force covers the check of the other side, whatever stops it. On a schema `PUT`, the compliance step can fail without a violation:

- another stored layer of a combined schema refers outside itself (`ExternalSchemaReference`) or cannot be used (`InvalidSchema`);
- a check runs out of its limits (`SchemaValidationTimeout`).

Without force, that answers `400` with the code. With force, the schema is stored and the failure is logged as "Schema compliance check could not finish, bypassed with force". This is how a stored layer that refers outside itself is replaced when another layer of the same configurations is broken too. A `DELETE` of the layer also works, because a delete runs no check.

Two sequences both work.

**Publish the schema first.** PUT the schema once, read the `409` list, then PUT it again with `force=true`. The new schema is in force. Each listed configuration stays stored and readable. The next ordinary write of one of those configurations is rejected until its stored document complies. Repair them one by one, or pass `--force` on a configuration write that must remain outside the schema.

**Repair the documents first.** PUT each configuration with `force=true` so the stored document already matches the schema you intend to publish. Then PUT the schema without force. The compliance scan sees documents that already match, and the schema is stored under the default rule.

An [import](export-import.md) avoids the deadlock without force: an archive that holds the new schema and the repaired documents is checked as the import leaves the view. With force, an import stores what breaks and reports it with the warning `SchemaViolation`.

Deleting a schema does not take force. A delete removes that view's document. The next read of the same id finds no schema.

## Versions

Versioning follows configuration and template versioning.

- A new schema starts at version `1`. Each write that changes the schema document increments `Version` and stamps the caller as `Modified`.
- An equal document does not produce a new version.
- Before a change, the current document is copied to a history item in the same `{project}.schema` partition. History ids:

  | Kind | History id |
  | --- | --- |
  | Type | `{view}.csv.t.{typeCode}.{version}` |
  | Annotation | `{view}.csv.a.{annotationKey}.{version}` |
  | Descendant type | `{view}.csv.d.{descendantType}.{annotationKey}.{version}` |

- A delete writes the history item of the deleted version. A schema created again later continues after the highest version ever allocated for that id, including after history has expired. The counter is kept on a state item (`{view}.css.…` in the same partition) that survives deletion.
- History items expire after 365 days. The current version does not expire. The expiry is enforced since the containers got item-level TTL with the [key/value pairs](key-values.md#lifecycle) (`DefaultTimeToLive = -1`, set by `ContainerFactory` on new organization containers and by `2sInitDb` on existing ones); before that, the `ttl` was stored but ignored.
- `ConfigurationVersion` describes a version: `Version`, `Modified` (who wrote the version and when, see [audit stamps](audit-stamps.md)), `ExpirationTime` (`DateTimeOffset.MaxValue` for the current version).
- Diffs compare stored schema documents. Version `0` is an empty object. The diff of version `N` is `N-1 → N`. Any pair can be requested.

The kind segment (`t`, `a`, `d`) keeps a type-schema history prefix from matching an annotation key that starts with the same characters.

Every write uses the ETag of the item it read. The history item, the new schema, and the state record are written in one transactional batch. When another writer changed the schema in the meantime, the write is retried on fresh data, up to three times. After that the call returns `409` with `ErrorResponse`.

Schema changes do not clear calculated configuration caches. A schema is consulted when content is written. It is not merged into the calculated document.

## Worked change

Project `billing`, view `default`, executions currently stored as `{ "retries": 3 }`. The type schema for `exe` requires `retries` only.

You want `currency` to be required, limited to `EUR`, `NOK`, and `USD`.

1. `sform story schema type set exe --import execution.schema.json` returns a conflict listing `exe.northwind.invoicing.prod` and any other execution in `default` whose stored document has no `currency`. Executions in `rollout` are not on the list.
2. `sform story schema type set exe --import execution.schema.json --force` stores version 2, authored by you. The old schema remains available as version 1.
3. `sform story config set exe.northwind.invoicing.prod -x currency=NOK` stores a compliant document under the default rule.
4. An execution you have not repaired yet still reads as before. `sform story config set` of that execution, without `--force`, is rejected until `currency` is present and one of the allowed values.

`sform story schema definition exe.northwind.invoicing.prod` shows the combined schema and the layers that were applied, each with its version and author. That response is the documentation of the values the next ordinary write has to satisfy.
