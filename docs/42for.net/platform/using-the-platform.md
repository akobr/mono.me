# Using the platform

Storyteller is the actor you talk to. It stores the annotation catalog and the configuration that hangs from it. Supervisor and the scheduler sit beside it; they are introduced in the [overview](overview) and mature on the [road map](road-map). Everything on this page is the catalog itself: how it is addressed, how you write it, and how a running service reads it.

## Organization, project, view

| Scope | Meaning | Conventional name |
| --- | --- | --- |
| Organization | Who owns the catalog | `house` |
| Project | One product inside that organization | `main` |
| View | One edition of that product's catalog and configuration | `default` |

A full key puts the three scopes in front of the annotation key:

```text
@house.main.default.exe.northwind.invoicing.production
```

Use a second view (`preview`, `2026-q4`) when you want a parallel configuration you can diff and then promote. The annotation keys stay the same, so a service can be pointed at a view without a new model.

A view comes into existence with its first write; nothing has to be created first. To give views a description and list them cheaply, register them: `GET /v1/{org}/{project}/views` lists the registered views and `default`, `POST` registers one, and `PUT .../views/{view}` sets its description. Administrators can add `?discover=true` to also find views that hold data but are not registered.

New organization, project, and view names use 2 to 63 lower-case letters, digits, or hyphens, start with a letter or a digit, and contain no dots, because the dot separates the parts of a key. A few words that the API and the admin UI use in their addresses are reserved, for example `access`, `views`, and `members`.

## What a service reads

At runtime a satellite already knows which subject, responsibility, and context it is serving. It asks for the execution:

```text
GET /v1/house/main/default/configuration/exe.northwind.invoicing.production
```

The body is the effective configuration: ancestors merged, `@` expressions still visible. That is the document to log, to diff, and to show in a support tool.

```text
GET /v1/house/main/default/configuration/exe.northwind.invoicing.production/resolved
```

The body is the same document with expressions evaluated. Secret sources are included only when the caller has the `Configuration.Secrets` scope. A caller without that scope can still read secrets when it has both the `User.Impersonation` scope and a project role of `ContributorWithSecrets` or higher. Everyone else receives the document with those secret expressions left unresolved.

The service does not merge customer files, environment files, and default files itself. The execution key is the merge.

## What a person browses

The catalog is queryable by type. These reads are how you answer the operational questions the model was built for.

| You want | Read |
| --- | --- |
| Every subject, responsibility, usage, context, and execution | `GET /v1/{org}/{project}/{view}/annotations` |
| Subjects, optionally filtered by name | `GET /v1/{org}/{project}/{view}/subjects` |
| Responsibilities | `GET /v1/{org}/{project}/{view}/responsibilities` |
| Usages of a responsibility or a subject | `GET /v1/{org}/{project}/{view}/usages` |
| Contexts | `GET /v1/{org}/{project}/{view}/contexts` |
| Executions | `GET /v1/{org}/{project}/{view}/executions` |
| Units and units of execution | `GET /v1/{org}/{project}/{view}/units` and `.../units-of-execution` |
| One record | `GET /v1/{org}/{project}/{view}/annotations/{key}` |
| What hangs under a record | `GET /v1/{org}/{project}/{view}/annotations/{key}/{descendants}` |

`descendants` is one of `usages`, `contexts`, `executions`, `units`, `units-of-execution`, or `all`. A responsibility has no contexts. A unit's only descendants are units of execution.

A continuation token pages large catalogs. Name filters on the collection routes find a customer or a module without scanning the whole graph by eye.

## Writing the catalog

Send an annotation to:

```text
PUT /v1/{org}/{project}/{view}/annotations/{key}
```

A batch is `POST /v1/{org}/{project}/{view}/annotations`.

You can also post a list of compact lines to `POST /v1/{org}/{project}/{view}/annotations/simple`. A line that already starts with a type code is that key. A line without a code is read by its length:

| Line | Becomes |
| --- | --- |
| `northwind` | Subject `sbt.northwind` |
| `northwind.invoicing` | Usage `usg.northwind.invoicing` |
| `northwind.invoicing.production` | Execution `exe.northwind.invoicing.production` |
| `northwind.invoicing.production.nightly-export` | Unit of execution |
| `rst.invoicing` | Responsibility |
| `cnt.northwind.production` | Context |
| `unt.invoicing.nightly-export` | Unit, with type `event` until you set a real trigger |

The records are stored in the project and view of the route, like every other write.

Creating an execution creates the missing subject, context, responsibility, and usage, and records the names on the parents. Creating a unit of execution does the same, including the unit and the execution. After that, set titles and configuration on the parents you care about.

Deleting a subject removes its contexts and the usages and executions that belong to it. Deleting a responsibility removes that capability's records: the responsibility, its units, and the usages and executions of that capability. The subject itself stays, so the customer remains in the catalog after a module is retired.

A delete is reversible for 90 days. The deleted records, with their configuration, leave every read and wait under `.../annotations/deleted`:

| You want | Call |
| --- | --- |
| What was deleted, newest first | `GET /v1/{org}/{project}/{view}/annotations/deleted` |
| Which of these keys are deleted | `POST .../annotations/deleted/lookup` with `{ "Keys": [...] }` |
| Bring records back, with what was deleted with them | `POST .../annotations/deleted/restore` with `{ "Keys": [...] }` |
| Remove a deleted record now (Administrator) | `DELETE .../annotations/deleted/{key}` |
| Who changed a record, and what | `GET .../annotations/{key}/history` |

Writing a key that is deleted creates it again with the new content and an empty configuration. To keep the old configuration, restore the key first and then write it.

## Writing configuration

```text
PUT /v1/{org}/{project}/{view}/configuration/{key}
```

The body is the JSON object to merge into the stored document. `PATCH` on the same path applies a JSON Patch to the stored document. `DELETE` clears it.

All configurations of a view, without their documents, one page of up to 1000 at a time:

```text
GET /v1/{org}/{project}/{view}/configurations?annotationType=exe&keyPrefix=exe.northwind&continuationToken=…
```

Each entry carries the annotation key and type, the version, the author, the last change, the hash of the effective document, and whether the annotation has a document of its own.

Versions of that stored document:

```text
GET /v1/{org}/{project}/{view}/configuration/{key}/versions
GET /v1/{org}/{project}/{view}/configuration/{key}/versions/{version}
GET /v1/{org}/{project}/{view}/configuration/{key}/versions/{version}/diff
GET /v1/{org}/{project}/{view}/configuration/{key}/versions/{version}/diff/{versionFrom}
```

A diff between views compares the effective configuration:

```text
GET /v1/{org}/{project}/{view}/configuration/{key}/diff/{viewTo}
```

The response is a structured diff, or a unified diff when you ask for that format. Use it before pointing a service at the new view.

## Writing schemas

Schemas belong to a view, like configuration: each view has its own schemas, with their own versions. A second view can try a schema change while `default` keeps its schemas.

| Layer | Path |
| --- | --- |
| Every annotation of a type | `PUT /v1/{org}/{project}/{view}/configuration-schema/type/{annotationType}` |
| One annotation | `PUT /v1/{org}/{project}/{view}/configuration-schema/{key}` |
| Descendants of one annotation | `PUT /v1/{org}/{project}/{view}/configuration-schema/{key}/type/{annotationType}` |
| The merged schema a write will be checked against | `GET /v1/{org}/{project}/{view}/configuration-schema/{key}/definition` |

`annotationType` is the short code: `rst`, `sbt`, `cnt`, `usg`, `exe`, `unt`, `uxe`. The body is a JSON Schema. A write of configuration that fails the combined schema is rejected, and the error names the key.

## Exporting and importing a view

A view can be exported as one zip and imported into any view of any project: its annotations, schemas, templates and configurations, or some of these kinds. [Configuration](configuration#export-and-import) explains what an archive holds. Both run as jobs: the start answers `202` with the job, and you read the job until its `Status` is `Succeeded` or `Failed`.

| You want | Call |
| --- | --- |
| Start an export | `POST /v1/{org}/{project}/{view}/exports` with `{ "Format": "Git", "Include": ["Annotations", "Templates"] }` |
| Its state, then the zip | `GET .../exports/{id}`, then `GET .../exports/{id}/archive` |
| Start an import | `POST .../imports?include=annotations,templates&existing=skip&dryRun=true` with the zip as the body |
| Its result | `GET .../imports/{id}` |

- `Format` is `Folder` (the default) or `Git`.
- `Include`, and the comma-separated `include` of an import, name the kinds: `Annotations`, `Schemas`, `Templates` and `Configurations`. Without them, all four are included.
- `existing` is `Overwrite` (the default) or `Skip`. `force=true` stores schemas and configurations that break each other, with a warning. `dryRun=true` reports every outcome without writing anything.
- The result of an import counts the documents per kind and in total, and lists every document that was not unchanged, with its kind, outcome, error code and warnings.
- Exporting needs any role on the project, and importing `Contributor`. Each included kind also needs its scopes: annotations those of annotations, the other three those of configuration. A machine that may only write configuration imports with `include=schemas,templates,configurations`.

`sform` starts the job, waits for it, and saves or prints the result:

```text
sform story export -f git -x                          # the view as a checked-out repository
sform story import ./my-view --existing skip --dry-run
```

## A minimal session

The following is the whole loop for one customer of one module. Paths assume the organization `house`, the project `main`, and the view `default`.

Create the running instance. Parents appear with it.

```json
["rst.invoicing", "exe.northwind.invoicing.production"]
```

Post that array to `.../annotations/simple`.

Set the product default and the customer override.

```text
PUT .../configuration/rst.invoicing
{ "currency": "EUR", "retries": 3, "features": ["vat"] }

PUT .../configuration/sbt.northwind
{ "currency": "NOK", "residency": "norway" }

PUT .../configuration/usg.northwind.invoicing
{ "plan": "enterprise", "features": ["e-invoice"] }
```

Require a plan on every usage of invoicing.

```text
PUT .../configuration-schema/rst.invoicing/type/usg
{
  "type": "object",
  "required": ["plan"],
  "properties": {
    "plan": { "type": "string" }
  }
}
```

Read the document the production service should use.

```text
GET .../configuration/exe.northwind.invoicing.production/resolved
```

From here the work is repetition of the same loop: a new context for a sandbox, a unit for the nightly export, a second view when the next change should be reviewed as a diff. The [examples](examples/) are that loop filled in for products with very different nouns.
