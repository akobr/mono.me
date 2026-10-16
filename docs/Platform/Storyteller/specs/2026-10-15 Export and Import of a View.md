# Export and Import of a View

Linear: [424-15 Export and import all configuration as git repository](https://linear.app/424fun/issue/424-15/export-and-import-all-configuration-as-git-repository)

Extends [Export and Import of Configurations](2026-10-14%20Export%20and%20Import%20of%20Configurations.md) (PR #88). An archive now holds four kinds of documents of a view: **annotations, schemas, templates and configurations**. The first revision (2026-10-15) added templates and schemas. The second revision (2026-10-16) adds:

- annotations;
- file names that carry the kind, so folders don't matter;
- the include and existing-item options;
- a forced import;
- the **Export & Import** page.

Four phases:

1. Backend: file names, the archive layout, the readers and writers, and the imports of every kind.
2. API, OpenAPI document and both SDKs.
3. CLI.
4. Administration UI.

## Problem

1. **An export is not the whole content of a view.** Three dependencies are missing from the archive:
   - a configuration belongs to an **annotation**;
   - its effective document also depends on the **templates** (defaults per annotation type);
   - it is validated by the **schemas** (type, annotation and descendant-type schemas).

   The first spec left all three out ("no templates, schemas, annotations"). As a result:
   - an archive cannot seed or restore a view: a configuration of a missing annotation is skipped;
   - a git history of a view does not show the template or schema change that broke a configuration.
2. **The kinds share name spaces, and folders are fragile.** An annotation and its configuration have the same key. A template and a type schema of `exe` have the same type code. Folders would separate them, but the folder structure does not survive everything users do: a zip is flattened, files are moved, a repository is reorganized. The kind must be in the **file name**.
3. **The import has one mode.** Every file replaces the stored document, and only `force` changes anything. With four kinds, the caller must choose:
   - which kinds to import;
   - what happens to documents the view already has.
4. **The controls are in the wrong place.** Export and Import sit in the header of the Configurations page, but the archive is no longer about configurations only.

The issue's rules stay:
- raw JSON;
- no **secrets** (bindings stay unresolved);
- no effective or resolved documents.

Two more rules apply:
- no **audit**: the audit of annotations is neither exported nor imported;
- no **history** is imported: only the current document of each item.

## Current State

### Annotations (`Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs`, [annotation-lifecycle.md](../annotation-lifecycle.md))

- An annotation is the item `{view}.{key}` in its annotation partition, with its audit `{view}.adt.{key}`. It has **no versions**. Each change is an entry of the audit, which is also the only history.
- The editable members are the ones `AnnotationFactory.Normalize` (`Backend.Core/src/Annotating/AnnotationFactory.cs:30`) takes:
  - `IsDisabled`, `ValidFrom`, `ExpiresAt`, `TimeZone`;
  - `Title`, `Description`, `DocumentationLink`;
  - `Labels`, `Values`;
  - for units, `UnitType` and `UnitDefinition`.
- The rest is server-owned and a write ignores it:
  - the members the key decides (type, name, ancestor keys and names);
  - the relation arrays (`UnitNames`, `ContextNames`, `ResponsibilityNames`);
  - the stamps `Created` and `Modified` (`{ At, By }`);
  - `Deletion`.
- `SetAnnotationAsync` (line 98) creates, updates or re-creates.
  - An **update** (`UpdateCoreAsync`, line 399) **replaces** the editable members. It writes nothing when `AnnotationAuditDiff` finds no change; otherwise it writes the annotation with an `Updated` audit entry in one batch.
  - A **create** (`CreateCoreAsync`, line 297) also creates every missing ancestor with `AnnotationFactory.CreateDefault(…, copyFrom)`. The ancestor copies the validity window, the time zone, the disabled flag and the labels of the descendant, and gets the audit cause `Ancestor`. A deleted key or ancestor is **re-created**: its configuration goes to the versions and starts empty.
  - Writing needs `Contributor` and the scope `Annotation.ReadWrite` or `Default.ReadWrite`. Reading needs any role and `Annotation.Read`, `Annotation.ReadWrite` or a `Default` scope.
- A configuration of a deleted annotation is hidden from every configuration read and refuses writes with `AnnotationDeleted`. A re-create moves its content to the versions and deletes it.
- `GetAnnotationsAsync(AnnotationsRequest)` pages through the active annotations of a view, every type.

### Templates (`Backend.CosmosDb/src/Configuring/CosmosConfigurationTemplateService.cs`, [templating.md](../templating.md))

- They live in the partition `{project}.template` of the organization container.
  - The current document is `{view}.gen.{type}` (`GenerateTemplateEntity`: `Content`, `Author`, `Version`).
  - Earlier versions are `{view}.gnv.{type}.{n}` (`GenerateTemplateHistoryEntity`, `ttl` 365 days).
  - `{view}.gns.{type}` (`GenerateTemplateStateEntity`) holds the highest version ever allocated (`LastVersion`, versions are never reused) and a pending cache invalidation.
- `CreateOrUpdateTemplateAsync` **merges** the request into the stored document (line 134, `MergeInto`) and then invalidates the effective documents of the type. `DeleteTemplateAsync` archives the current version and deletes it.
- A template is merged only into the **effective** document. No schema validates it, and it validates nothing.
- Writing a template needs `Contributor` and `Configuration.ReadWrite` or `Default.ReadWrite` (`TemplateHttp.cs:122`).

### Schemas (`Backend.CosmosDb/src/Configuring/CosmosConfigurationSchemaService.cs`)

- They live in the partition `{project}.schema`. A `SchemaIdentity` (line 923) names three kinds:

  | Kind | Current | Versions | State |
  | --- | --- | --- | --- |
  | Type | `{view}.cfs.t.{type}` | `{view}.csv.t.{type}.{n}` | `{view}.css.t.{type}` |
  | Annotation | `{view}.cfs.a.{key}` | `{view}.csv.a.{key}.{n}` | `{view}.css.a.{key}` |
  | Descendant type | `{view}.cfs.d.{type}.{ancestorKey}` | `{view}.csv.d.{type}.{ancestorKey}.{n}` | `{view}.css.d.{type}.{ancestorKey}` |

- Schemas validate the **own document** of a configuration, never the effective one (`ValidateContentAsync`, line 177; `CollectComplianceErrorsAsync`, line 574). So templates play no part in validation.
- `SaveAsync` (line 346):
  - **replaces** the document;
  - checks that it is a valid JSON Schema (`EnsureValidSchemaAsync`);
  - leaves an equal document alone;
  - checks that every stored configuration it affects still complies (`CollectComplianceErrorsAsync`, line 535). It rejects the save with `SchemaValidationException` unless `force`.
- `SaveAsync` does not check that the annotation of an annotation or descendant-type schema exists.
- `DeleteAsync` archives and deletes. Versions expire after 365 days.
- Writing a schema needs `Contributor` and `Configuration.ReadWrite` or `Default.ReadWrite` (`ConfigurationSchemaHttp.cs:98`).

### Export and import as of PR #88

- Routes: `…/{view}/configurations/exports` and `…/configurations/imports`, tag `Configurations` (`Api.Functions/src/V1/ConfigurationTransferHttp.cs`). The model is `ConfigurationExport`, `ConfigurationImport` and the related types in `Abstractions.Annotations/src/Model/ConfigurationTransfer.cs`.
- The archive holds configurations only, flat at the root, with `export.json`.
  - `ConfigurationFileNames` names a configuration `<key>.json` and a version `<key>.<n>.json`.
  - `GitRepositoryWriter` writes one flat tree.
- `ConfigurationArchiveReader` takes every `<key>.json` in any folder as a configuration. It skips `.git`, `__MACOSX` and `._*`.
- `ConfigurationTransferService` imports file by file through `IConfigurationService.ImportConfigurationAsync`:
  - the only option is `force`, besides the dry run;
  - a document always replaces a different stored one;
  - documents are validated against the schemas **stored** at that moment.
- The CLI has `sform story config export` and `sform story config import`.
- The admin UI has **Export** and **Import** in the header of the Configurations page (`pages/[org]/[project]/[view]/configurations.vue:70`). They open `ConfigurationExportDialog` and `ConfigurationImportDialog`.
- PR #88 is merged into `main` but not released: no `Platform/*` tag exists.

**So versioning is available for templates and schemas,** with the same model as configurations:
- a current item;
- archived versions with their author and creation time;
- a 365-day TTL;
- deletes that archive the last version.

Annotations have only their current content, the two stamps and the audit.

## Design

- **Four kinds:** annotations, schemas, templates and configurations. Export and import take the **kinds to include**, all four by default.
- **The file name tells the kind.** Each kind has its own suffix, and folders play no part in the import:
  - `<key>.json` is an annotation;
  - `<key>.conf.json` is a configuration;
  - `<type>.template.json` is a template;
  - `<type>.schema.json` is a type schema;
  - `<key>.schema.json` is an annotation schema;
  - `<key>.<type>.schema.json` is a descendant-type schema;
  - `<name>.<n>.json` is version `n` of any of them except annotations.

  A flat zip, the export's folders and any other layout import the same. Hidden folders and files are skipped.
- **Folders are hygiene.** The export writes `annotations/`, `schemas/`, `templates/` and `configurations/`.
- **Versions in the export, never in the import:**
  - The folder format writes the earlier versions of schemas, templates and configurations as version files.
  - The git format commits every version of the three kinds in one time-ordered history.
  - The import ignores version files and never replays a history; it imports the current document of each item.
- **Annotations travel without versions and audit.** A file holds the editable members. The stamps `Created` and `Modified` are listed in `export.json` for information only.
- **Import options:**
  - `include`: the kinds to import, all four by default;
  - `existing`: what happens to a document the view already has. `Overwrite` (the default) writes the file as a new version, or as an update of an annotation. `Skip` keeps the stored document;
  - `force`: store schemas and configurations that break each other, as PR #88 does for configurations;
  - `dryRun`: validate, compare and report every outcome and warning without writing.
- **The import writes in dependency order:** annotations (ancestors first), then schemas, then templates, then configurations. See [Order](#order).
- **Checks see the view as the import leaves it.** A schema is checked against the configurations the archive brings, and a configuration in a dry run against the schemas the archive brings. So an archive that changes a schema and its configurations together imports without `force`, and a dry run predicts the real run.
- **Imports replace, never delete,** for every kind, as in PR #88.
- **No backward compatibility.** Archives of PR #88 are not supported, and the routes, types and commands of PR #88 are renamed to view-level names.
- **Any view.** An archive can be imported into any view of any project. The names in `export.json` are only for information.
- **The admin UI gets an Export & Import page** in the view section of the sidebar. The controls leave the Configurations page.

### Order

The import must follow the dependencies between the kinds. The export doesn't have to, except in the order of its git history.

| Pass | Must come after | Why |
| --- | --- | --- |
| 1. Annotations | — | Annotations depend on nothing. Within the pass, **ancestors come first** (graph depth, then type, then key). Otherwise a descendant would create a missing ancestor with defaults, and the ancestor's own file would then find it existing: `Skip` would keep the defaults, and its audit would show an automatic ancestor. |
| 2. Schemas | annotations | An annotation or descendant-type schema names an annotation. The import skips a schema whose annotation is not in the view. Within the pass: type schemas, then descendant-type schemas, then annotation schemas. With the overlay, this order only makes the run deterministic. |
| 3. Templates | — | A template depends on no other kind: it is per type, and no schema validates it. Running it before configurations keeps the git history readable: rules, then defaults, then data. It costs nothing: each template write invalidates the cached effective documents of its type anyway. |
| 4. Configurations | annotations, schemas | A configuration needs its annotation. It is validated against its schemas when it is written, so the schemas must already be in place. The opposite order would validate it against the old schemas. |

**So the proposed order is right, with two refinements:**
- ancestors first within annotations;
- templates are independent, so their position before configurations is a convention, not a dependency.

**Schemas and configurations depend on each other.** A schema is checked against configurations, and a configuration against schemas. No order removes the cycle, so the check of a schema uses an **overlay** of the configurations the archive will write (1.10).

**The export reads in the reverse order:** configurations, templates, schemas, annotations. The reads are not a snapshot. A document read first had its dependency at that moment, and the later read still finds the dependency unless it was deleted in between. Creating during an export is more common than deleting. In the forward order, a configuration created during the export would land in the archive without its annotation.

The git history is ordered by time. Within one time, the import order applies.

## Proposed Changes

### Phase 1 — Backend

#### 1.1 Public model (`Abstractions.Annotations/src/Model/ViewTransfer.cs`, replaces `ConfigurationTransfer.cs`)

The archive is no longer about configurations, so the public types get view-level names (decision 13).

| PR #88 | New |
| --- | --- |
| `ConfigurationArchiveFormat` | `ViewArchiveFormat` |
| `ConfigurationTransferStatus` | `ViewTransferStatus` |
| `ConfigurationImportOutcome` | `ViewImportOutcome` |
| `ConfigurationExportCreate` | `ViewExportCreate` |
| `ConfigurationExport` | `ViewExport` |
| `ConfigurationImport` | `ViewImport` |
| `ConfigurationImportSummary` | `ViewImportSummary` |
| `ConfigurationImportItem` | `ViewImportItem` |

```csharp
// The kinds of documents an archive holds, in import order; a list of them is the include filter.
[JsonConverter(typeof(StringEnumConverter))]
public enum ViewArchiveContent
{
    Annotations = 0,
    Schemas,
    Templates,
    Configurations,
}

// One document of an archive.
[JsonConverter(typeof(StringEnumConverter))]
public enum ViewDocumentKind
{
    Annotation = 0,
    TypeSchema,
    DescendantTypeSchema,
    AnnotationSchema,
    Template,
    Configuration,
}

// What an import does with a document the view already has.
[JsonConverter(typeof(StringEnumConverter))]
public enum ViewImportExisting
{
    // The file replaces it: a new version, or an update of an annotation.
    Overwrite = 0,

    // It stays as it is; a different file is Skipped with DocumentExists.
    Skip,
}

public record class ViewImportWarning
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}
```

- `ViewExportCreate` gains `IReadOnlyList<ViewArchiveContent>? Include`. Null or empty means all four.
  - It is a list, not a flags enum, because both SDK generators handle lists of string enums well and flags poorly.
- `ViewExport` gains:
  - `Include`;
  - `AnnotationCount`, `SchemaCount` and `TemplateCount`, next to `ConfigurationCount` (the documents with a current version);
  - `VersionCount`, which now counts every exported version of schemas, templates and configurations, the current ones included.
- `ViewImport` gains:
  - `Include` and `Existing`;
  - `Summaries`: one `ViewImportSummary` per included kind, in import order. `Summary` stays the total.
- `ViewImportSummary` gains:
  - `Content`, null on the total;
  - `Warnings`, the written documents with a warning;
  - `ExcludedFiles` on the total: the files of kinds left out by `Include`. `IgnoredFiles` stays: version files, `export.json`, and names that are not documents.
- `ViewImportItem` gains:
  - `Kind`;
  - `AnnotationType`: the type code of a template, a type schema or a descendant-type schema;
  - `AnnotationKey`: now also the key of an annotation, the annotation of an annotation schema, and the ancestor of a descendant-type schema;
  - `Warnings`.
- `Items` keeps every outcome except `Unchanged`, as before. Created and updated items with warnings are among them.
- The internal types of `Backend.Core/src/Transferring/` that now cover every kind drop the `Configuration` prefix: `ViewTransferService`, `ViewArchiveReader`, `ViewArchiveWriter`, `ViewArchiveNames`, `ViewArchiveFile`, `ViewArchiveSource`.
- The storage names stay: the queue and blob container `configuration-transfers`, the partition `{project}.transfer`, and the options section `ConfigurationTransfers`. Jobs live 24 hours, so nothing needs to be migrated, but renaming deployed resources gains nothing.

#### 1.2 Error codes

New:

| Code | Use | When |
| --- | --- | --- |
| `DocumentExists` | `Skipped` | `existing=Skip` and the view has a different document. An equal one is `Unchanged`. |
| `DependencyFailed` | `Skipped` | The annotation the document belongs to failed in this import. For an annotation, one of its ancestors failed. |
| `InvalidSchema` | `Failed` | Not a valid JSON Schema (the draft of `EnsureValidSchemaAsync`). |
| `AncestorCreated` | Warning | Creating the annotation also created missing ancestors that the archive doesn't hold, with defaults. |
| `AnnotationRecreated` | Warning | The annotation or its ancestors were deleted and are re-created: their configurations went to the versions and start empty, and the children deleted with them stay deleted. |

Reused:
- `SchemaViolation`:
  - `Failed` without `force`;
  - a warning on a forced write.

  A schema lists the non-compliant keys in the message (at most 20, then `… and N more`).
- `AnnotationNotFound`, `AnnotationDeleted`, `EmptyDocument`, `InvalidDocument`, `DuplicateFile`, `PatchInvalid`, as in PR #88.
- `InvalidAnnotation`: an annotation document breaks `AnnotationRules`.
- `InvalidKey`: a descendant-type schema whose type cannot descend from its annotation.

`UnknownAnnotationType` of the first revision is dropped. A name with an unknown type code is not a document, so it is ignored.

#### 1.3 File names (`ViewArchiveNames`, replaces `ConfigurationFileNames`)

| Document | Current | Earlier version |
| --- | --- | --- |
| Annotation | `<key>.json` | (none: annotations have no versions) |
| Configuration | `<key>.conf.json` | `<key>.conf.<n>.json` |
| Template | `<type>.template.json` | `<type>.template.<n>.json` |
| Type schema | `<type>.schema.json` | `<type>.schema.<n>.json` |
| Annotation schema | `<key>.schema.json` | `<key>.schema.<n>.json` |
| Descendant-type schema | `<ancestor-key>.<type>.schema.json` | `<ancestor-key>.<type>.schema.<n>.json` |

- `<type>` is the lower-case type code (`rst`, `sbt`, `unt`, `usg`, `cnt`, `exe`, `uxe`).
- `<key>` is encoded as in PR #88: each segment percent-encodes the characters file systems reject.
- **Reserved second segment.** A key whose second segment is `template` or `schema` (in any case) gets the first character of that segment percent-encoded. For example, the responsibility `rst.schema` is written `rst.%73chema.json`. Otherwise `rst.schema.json` would be both that annotation and the type schema of `rst`, and `unt.template.3.json` both a unit and version 3 of the template of `unt`. Decoding is the one of PR #88.

`ViewArchiveNames.TryParse(fileName)` returns the kind, the type code, the key and the version, or nothing:

1. Strip `.json` (case-insensitive) and split at `.`. The first segment must be a type code (in any case; it is lower-cased).
2. If the second segment is literally `template` or `schema` (in any case, not encoded), the file is the template or type schema of that type. A third, numeric segment is the version. Any other name of this shape is not a document.
3. Otherwise the key takes as many segments as its type has: two for `rst` and `sbt`, three for `unt`, `usg` and `cnt`, four for `exe`, five for `uxe`. The segments are decoded as in PR #88. The rest of the name is the suffix:

   | Suffix | Document |
   | --- | --- |
   | (none) | Annotation |
   | `conf` | Configuration |
   | `schema` | Annotation schema |
   | `<type>.schema` | Descendant-type schema of `<type>` below the key |

   Every suffix except none may end with `.<n>`, the version.
4. Any other name is not a document and counts as ignored.
5. A descendant type that cannot descend from the key (`AnnotationHierarchy.GetAncestorSchemaSources`) is a document, but it fails with `InvalidKey`. For example, `exe.s.r.c.rst.schema.json`.

| File | Document |
| --- | --- |
| `rst.payments.json` | Annotation `rst.payments` |
| `rst.payments.conf.json` | Configuration of `rst.payments` |
| `rst.payments.conf.3.json` | Version 3 of that configuration |
| `rst.payments.schema.json` | Schema of `rst.payments` |
| `rst.payments.exe.schema.json` | Schema of the executions below `rst.payments` |
| `exe.template.json` | Template of `exe` |
| `exe.template.4.json` | Version 4 of that template |
| `exe.schema.json` | Type schema of `exe` |
| `rst.%73chema.json` | Annotation `rst.schema` |
| `rst.payments.3.json` | Nothing: annotations have no versions (a version file of PR #88) |

**Folders.** Only the file name is parsed, so the folders of a path don't matter.
- The same document in two files is a duplicate (1.9), also across folders.
- A path is **skipped**, and not counted, when any of its segments starts with `.`. This covers `.git`, `.github`, `.vscode`, `.DS_Store` and `._*`. `__MACOSX` stays skipped.

#### 1.4 Archive layout and manifest

```
export.json
annotations/
  rst.payments.json
  sbt.northwind.json
  usg.northwind.payments.json
  exe.northwind.payments.prod.json
schemas/
  exe.schema.json
  exe.schema.1.json
  rst.payments.schema.json
  sbt.northwind.exe.schema.json
  sbt.northwind.exe.schema.2.json
templates/
  exe.template.json
  exe.template.3.json
configurations/
  rst.payments.conf.json
  rst.payments.conf.1.json
  exe.northwind.payments.prod.conf.json
```

- There is one folder per included kind, and no folder for a kind without documents. Version files sit next to their current file.
- Documents are formatted as in PR #88: indented by two spaces, with `\n` line ends, a final new line and no BOM.
- The zip is named `export-{organization}-{project}-{view}[-git]-{yyyyMMdd-HHmmss}Z.zip`, with the time of the request in UTC.
- `export.json` becomes version 2 of the manifest:
  - `ManifestVersion: 2`, `Organization`, `Project`, `View`, `ExportedAt`, `ExportedBy`, `Include`;
  - `Annotations`: per annotation its `Key`, `File`, `Created` and `Modified` (both `{ At, By }`);
  - `Schemas`: `Kind`, `AnnotationType`, `AnnotationKey`, `File`, `Version`, `Author`, `UpdatedAt` and `Versions`;
  - `Templates`: `AnnotationType`, `File`, `Version`, `Author`, `UpdatedAt` and `Versions`;
  - `Configurations`: as in PR #88, with paths into `configurations/`.

  A deleted template, schema or configuration (versions, no current document) has `File` and `Version` null. The import never reads the manifest.

#### 1.5 Annotation documents (`ViewAnnotationDocument`)

**Written:** the editable members in the API's PascalCase names, null members left out:
- `Title`, `Description`, `DocumentationLink`;
- `Labels`, `Values`;
- `IsDisabled`, `ValidFrom`, `ExpiresAt`, `TimeZone`;
- for units, `UnitType` and `UnitDefinition`.

```json
{
  "Title": "Payments in production",
  "Labels": [
    "eu"
  ],
  "Values": {
    "tier": "gold"
  },
  "TimeZone": "Europe/Prague"
}
```

**Not written:**
- the key, the type and the names (the file name holds them);
- `ProjectName` and `ViewName`;
- the relation keys and arrays;
- `Created` and `Modified` (only in the manifest);
- `Deletion`;
- the audit.

**Read** (`ViewAnnotationDocument.TryRead`):
- The file is a JSON object. Comments are allowed and duplicate properties rejected, and dates are parsed as annotation reads parse them, so an exported annotation compares equal.
- **Ignored members.** The server-owned members of a `GET` response are ignored, so a response body can be saved as a file. When `AnnotationKey` is present, it must equal the key of the file name; otherwise the file fails with `InvalidDocument`.
- **Unknown members.** Any other member fails the file with `InvalidDocument` (`Unknown member 'X'.`). `UnitType` on a type other than a unit is unknown too. This also catches a configuration document read as an annotation, for example from a PR #88 archive.
- **Missing members.** A member that is not in the file is cleared, because an import **replaces** the content. A unit without `UnitType` or `UnitDefinition` gets the defaults of a create (`event`, `unknown`).
- **`{}` is a valid annotation:** the key without content. With `Overwrite`, it clears the content of an existing annotation.
- `AnnotationRules` applies, and a violation fails the file with `InvalidAnnotation`.

`$remove` and `$patch` are not directives here: annotations have no merge.

#### 1.6 Documents of a view

- **Annotations:** `IAnnotationService.GetViewAnnotationsAsync(organization, project, view, cancellationToken)` pages through `GetAnnotationsAsync` (every type, active only). It returns each annotation with its stamps.
- **Templates:** `IConfigurationTemplateService.GetTemplateHistoriesAsync(organization, project, view, cancellationToken)` returns one `TemplateHistory` per type code, built from one single-partition query over `{project}.template`:
  - the current item `{view}.gen.*`;
  - its versions `{view}.gnv.*`;
  - an empty document is not a version.
- **Schemas:** `IConfigurationSchemaService.GetSchemaHistoriesAsync(…)` returns one `SchemaHistory` per identity (kind, type, key). It runs one query over `{project}.schema` for `{view}.cfs.*` and `{view}.csv.*`.
  - The identity is parsed from the id suffix (`t.`, `a.`, `d.`); the state items `css` are ignored.
  - **Change from the first revision:** annotation and descendant-type schemas of an annotation that is not active are left out. That covers deleted annotations (`ReadDeletedAnnotationKeysAsync`, the same read the configurations use) and missing ones. A deleted annotation and everything that hangs on it are not exported, so the dry run of a fresh view raises no `AnnotationNotFound` for them.
- **Configurations:** `GetConfigurationHistoriesAsync`, as in PR #88.
- Templates and schemas reuse `ConfigurationDocument` (version, author, created at, content). The times are the history item's `CreationTime` and the current item's `_ts`, with the same caveat as configurations. A template or schema that was deleted has versions and no current document.
- **Read order.** `ViewTransferService` reads the included kinds in reverse dependency order: configurations, templates, schemas, annotations ([Order](#order)).
- `ViewArchiveSource` gains `Annotations`, `Templates`, `Schemas` and `Include`.

#### 1.7 Folder format (`ViewArchiveWriter`)

Writes the folders of the included kinds (1.4) and the manifest v2.
- The entry time of a version file is the time of its version.
- The entry time of an annotation is its `Modified.At`.

#### 1.8 Git format (`Git/GitRepositoryWriter`)

- **Nested trees.** A path with `/` builds subtrees.
  - A tree is ordered like git: by name bytes, with a directory compared as `name/`.
  - Only the trees on the changed path are written again. Each is a delta against the previous version of the same tree; `GitPackWriter` already takes any base.
  - `.git/index` lists full paths in byte order, as git requires.
- **One history for all kinds.** `ViewArchiveWriter` merges every included document. The order is by time, then kind (annotations, schemas, templates, configurations), then name, then version. Each document's versions stay in order.
- **Annotations** have one commit each, with the current content. The commit is at the `Created` stamp: its time, and its actor as author and committer.
  - That is when the key started to exist, so its schemas and configurations come after it.
  - A re-created annotation has a new `Created` stamp, while the configuration versions of its previous lifetime stay. Those versions come before the annotation file.
  - The content is the current one, which the manifest's `Modified` stamp dates.
- **Signatures.** An actor becomes a signature like an author string: `account: <id>`, `machine: <id>`, or the system signature.
- **Messages.** PR #88's configuration messages change, because "Add rst.payments" would now be ambiguous:

  | Document | First version in the export | Next versions |
  | --- | --- | --- |
  | Annotation | `Add annotation rst.payments` | (none) |
  | Type schema | `Add type schema exe (version 1)` | `Update type schema exe to version 2` |
  | Descendant-type schema | `Add exe schema below sbt.northwind (version 1)` | `Update exe schema below sbt.northwind to version 3` |
  | Annotation schema | `Add schema of rst.payments (version 1)` | `Update schema of rst.payments to version 2` |
  | Template | `Add template exe (version 1)` | `Update template exe to version 4` |
  | Configuration | `Add configuration of rst.payments (version 1)` | `Update configuration of rst.payments to version 3` |

- **The last commit** removes every schema, template and configuration that has versions but no current document. Its message is `Remove deleted documents`, with the paths in the body, at the export time, by the system signature. A deleted annotation is not exported at all, so it never appears.
- `GitRepositoryTests` gain nested-tree cases:
  - the independent reader walks subtrees;
  - `git fsck --strict`, `git ls-files` and `git log -- templates/exe.template.json` run on an export with every kind.

#### 1.9 Reading an archive (`ViewArchiveReader`)

- `Read(stream, options, include)` takes every entry that is not skipped (1.3) and parses its file name.
  - A file of an included kind is a `ViewArchiveFile`: path, kind, type code, key, content, or an error code and message.
  - A file of a kind left out by `include` counts as excluded, and is not decompressed.
  - Version files, `export.json` and every other name count as ignored.
- **Duplicates** are found per document identity, in any folders: for example `templates/exe.template.json` and `exe.template.json`. The first path in ordinal order wins; the others fail with `DuplicateFile`. Encoded and plain spellings of the same key (`rst.pay%6Dents.json`) are the same identity.
- The limits of PR #88 apply to all included files together. `MaxDocumentBytes` applies to every kind.
- `Validate` (the central directory, at upload) is unchanged.

#### 1.10 Import options and the overlay

```csharp
public sealed record class ViewImportOptions
{
    public required string Author { get; init; }        // the Author of written versions

    public required AuditActor Actor { get; init; }     // the actor of audit entries

    public ViewImportExisting Existing { get; init; }

    public bool Force { get; init; }

    public bool DryRun { get; init; }
}

// The view as the import will leave it, for the checks that look beyond one document.
public sealed class ViewImportOverlay
{
    // Keys the import makes active (created or re-created); in a dry run, nothing is written, so later passes ask here first.
    public IReadOnlySet<string> ActiveAnnotations { get; }

    // The deleted keys among them: their configurations are emptied by the re-create, so a configuration file is Created.
    public IReadOnlySet<string> RecreatedAnnotations { get; }

    // Keys whose annotation failed in this import.
    public IReadOnlySet<string> FailedAnnotations { get; }

    // Schemas of the archive the import will write: valid, included, and allowed by Existing.
    public IReadOnlyDictionary<SchemaKey, JObject> Schemas { get; }               // kind, type, key → schema

    // Configurations of the archive the import will write: included, not empty, allowed by Existing, and with an annotation.
    public IReadOnlyDictionary<string, JObject> Configurations { get; }           // annotation key → own document
}
```

- The overlay is built after the annotation pass, when the annotations are known. Before that, the existence of each stored document is read once, so `Existing` can be applied.
- A document that then fails or is skipped is removed from the overlay.
- Earlier checks may have counted on a document that later failed. The configuration pass of a real run validates against what is actually stored, so no configuration is written against a schema that is not there.

#### 1.11 Importing an annotation (`IAnnotationService.ImportAnnotationAsync`)

```csharp
Task<ViewImportResult> ImportAnnotationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay);
```

1. **Content.** The document is read (1.5). A broken one fails with `InvalidDocument` or `InvalidAnnotation`.
2. **Failed ancestor.** An ancestor that failed in this import skips the annotation with `DependencyFailed`. Creating it would create that ancestor with defaults.
3. **Active key.**
   - An equal document (`AnnotationAuditDiff` finds no change) is `Unchanged`.
   - With `Skip`, a different document is `Skipped` with `DocumentExists`.
   - With `Overwrite`, `UpdateCoreAsync` replaces the content and writes an `Updated` audit entry. The outcome is `Updated`.
4. **Deleted key, or a deleted ancestor.**
   - With `Skip`, the file is `Skipped` with `AnnotationDeleted`, which names the deleted key.
   - With `Overwrite`, `CreateCoreAsync` **re-creates** the deleted keys, as any write of the key through the API does (decision 9):
     - the item is replaced by the file's content, with empty relation arrays and a new `Created` stamp;
     - its configuration goes to the versions and is deleted, so the next version continues the numbering;
     - its audit continues with a `Recreated` entry;
     - the children deleted with it stay deleted and become roots of **Deleted**, unless the archive holds them; then they are re-created by their own files, later in the pass.

     A deleted ancestor that the archive doesn't hold is re-created with defaults, like a missing one. The warning `AnnotationRecreated` lists every re-created key. The outcome is `Created`: the key had no active annotation.
5. **Missing key.** `CreateCoreAsync` creates it and its missing ancestors. The outcome is `Created`.
   - Ancestors from the archive were imported before (pass order), so the only ones created here are those the archive doesn't hold. They get the warning `AncestorCreated` with their keys.
6. **Dry run.** Steps 1–5 decide without writing. The overlay records the keys that would become active, and among them the re-created ones (1.10).

**Audit.** Written annotations get their usual audit entries (`Created`, `Recreated`, `Updated`) by the importing caller. The archive carries no audit, and the import adds no cause of its own.

#### 1.12 Importing a schema (`IConfigurationSchemaService.ImportSchemaAsync`)

```csharp
Task<ViewImportResult> ImportSchemaAsync(ViewSchemaTarget target, JObject content, ViewImportOptions options, ViewImportOverlay overlay);

public sealed record class ViewSchemaTarget(string Organization, string Project, string View, ViewDocumentKind Kind, string? AnnotationType, string? AnnotationKey);
```

1. **Annotation.** An annotation or descendant-type schema whose annotation is not active, neither stored nor in the overlay, is `Skipped`:
   - with `AnnotationNotFound` when it is missing;
   - with `AnnotationDeleted` when it is deleted;
   - with `DependencyFailed` when it failed in this import.
2. **Content.**
   - `{}` is `Skipped` with `EmptyDocument`.
   - A document that is not a valid JSON Schema is `Failed` with `InvalidSchema`, even with `force`.
3. **Existing.**
   - An equal document is `Unchanged`.
   - With `Skip`, a different stored document makes the file `Skipped` with `DocumentExists`.
4. **Compliance.** `CollectComplianceErrorsAsync` gets the overlay and checks the configurations as the import leaves them:
   - a stored own document is replaced by the overlay's;
   - the overlay's configurations of keys with no stored document are checked too, for example those of new annotations;
   - the schema layers are taken from the overlay where the archive brings them.

   A violation is `Failed` with `SchemaViolation`. With `force`, it is written with the warning `SchemaViolation` and logged as on a save.
5. **Dry run.** It stops here, with `Created` or `Updated`. Otherwise `SaveAsync` writes the document (the compliance check is not repeated).

#### 1.13 Importing a template (`IConfigurationTemplateService.ImportTemplateAsync`)

```csharp
Task<ViewImportResult> ImportTemplateAsync(string organization, string project, string view, string annotationType, JObject content, ViewImportOptions options);
```

- `$remove` and `$patch` apply to the document alone, as on a create. A broken directive is `Failed` with `PatchInvalid`.
- `{}` is `Skipped` with `EmptyDocument`: a template is cleared by `DELETE`.
- An equal document is `Unchanged`. With `Skip`, a different stored template is `Skipped` with `DocumentExists`.
- Otherwise the template is written with **replace** semantics. `CreateOrUpdateTemplateAsync` and the import share one write path with a `merge` flag, like configurations.
  - The version comes from the state's `LastVersion`.
  - The effective documents are invalidated as on any template write.
- Templates have no schema, so `force` does not apply.

#### 1.14 Importing a configuration (`IConfigurationService.ImportConfigurationAsync`)

```csharp
Task<ViewImportResult> ImportConfigurationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay);
```

PR #88's steps, with three changes:
- **Annotation.**
  - A missing or deleted annotation that the overlay makes active counts as active, which matters in a dry run.
  - An annotation that failed in this import makes the file `Skipped` with `DependencyFailed`.
  - Otherwise `AnnotationNotFound` and `AnnotationDeleted` stay. With `Skip`, a deleted annotation stays deleted, so its configuration is `Skipped` with `AnnotationDeleted`.
- **Existing.** With `Skip`, a different own document makes the file `Skipped` with `DocumentExists`. The configuration of a re-created annotation is not existing: the re-create emptied it, so the file is `Created` as the next version.
- **Validation.** In a dry run, the combined schema takes its layers from the overlay where the archive brings them. In a real run, the schemas are already written.
  - With `force`, a violation is written with the warning `SchemaViolation`, not silently as in PR #88.

#### 1.15 Processing (`ViewTransferService`)

- **The export** reads the included kinds (1.6) and writes them (1.7, 1.8).
- **The import:**
  1. It reads every file (1.9) with the job's `Include`.
  2. It reads the existence of the stored documents that the files name, by kind: one query per kind.
  3. **Annotations**, ordered by depth, type and key.
  4. It builds the overlay (1.10).
  5. **Schemas**, ordered type schemas, descendant-type schemas, annotation schemas, then by type and key. A schema that fails or is skipped leaves the overlay.
  6. **Templates**, by type.
  7. **Configurations**, by key.
- Files that failed while reading (`InvalidDocument`, `DuplicateFile`) are reported in their kind's pass.
- The items are reported in pass order, with their kind. `Summaries` holds one summary per included kind. Progress counts every included file.
- **Retries.** An import is idempotent. If an invocation is cut off (the Functions timeout, a crash), the queue runs the job again. The documents the first run wrote are then reported as `Unchanged`. Imports of thousands of annotations are much slower than imports of configurations. The docs name the limit, and resuming is out of scope.

#### 1.16 Tests (Phase 1)

- `Backend.Core/test`:
  - `ViewArchiveNames`:
    - every kind and version;
    - the reserved second segment, both ways;
    - unrecognized names;
    - an invalid descendant type;
    - case of type codes and suffixes;
  - the writer: folders, manifest v2, include filters, annotation documents;
  - the reader:
    - kinds;
    - a flat zip and a zip with foreign folders give the same files;
    - hidden folders are skipped;
    - duplicates across folders and encodings;
    - excluded and ignored counts;
  - `ViewAnnotationDocument`: round trip, ignored server-owned members, a mismatched `AnnotationKey`, unknown members, units;
  - git:
    - nested trees through the independent reader and the `git` CLI;
    - the merged history order with annotations;
    - the remove commit;
  - the service:
    - the pass order;
    - ancestors first;
    - the overlay;
    - `DependencyFailed`;
    - include filters;
    - `Existing`;
    - the summaries per kind.
- `Backend.CosmosDb/test`:
  - histories:
    - template and schema histories, with deleted documents and other views;
    - schemas of deleted annotations left out;
    - view annotations without deleted ones;
  - `ImportAnnotationAsync`:
    - create;
    - create below a missing ancestor (`AncestorCreated`);
    - unchanged;
    - overwrite and skip;
    - re-create of a deleted key with `Overwrite` (the configuration goes to the versions, a `Recreated` entry, deleted children stay deleted and become roots);
    - re-create of a deleted ancestor that the archive doesn't hold (`AnnotationRecreated`);
    - `AnnotationDeleted` with `Skip`;
    - a dry run that writes nothing;
    - the audit entries;
  - `ImportTemplateAsync`: replace, unchanged, skip, invalidation of an effective document;
  - `ImportSchemaAsync`:
    - invalid schema, unchanged, skip;
    - a violation fixed by the archive's configurations (passes without `force`);
    - a violation left (fails without `force`; with `force`, it is written with a warning);
    - a configuration of a new annotation in the overlay;
    - a dry run that writes nothing;
    - a missing annotation;
  - `ImportConfigurationAsync`: skip, the overlay's annotation in a dry run, a forced write with a warning.

### Phase 2 — API and SDKs

#### 2.1 Routes (`Api.Functions/src/V1/ViewTransferHttp.cs`, replaces `ConfigurationTransferHttp.cs`)

All below `v1/{organization}/{project}/{view}`, tag `Transfers`:

| Method | Route | Answer |
| --- | --- | --- |
| `POST` | `…/exports` with `{ "Format": "Folder" \| "Git", "Include": [ … ] }` | `202` `ViewExport` |
| `GET` | `…/exports/{id}` | `200` `ViewExport` |
| `GET` | `…/exports/{id}/archive` | `200` `application/zip` |
| `POST` | `…/imports?include=&existing=&force=&dryRun=&fileName=` with the zip | `202` `ViewImport` |
| `GET` | `…/imports/{id}` | `200` `ViewImport` |

- The status codes and errors stay as in PR #88. PR #88's routes below `…/configurations/` are removed; they were never released.
- `include` is a comma-separated list, case-insensitive: `annotations,schemas,templates,configurations`. An empty list means all four. An unknown name answers `400`.
- `existing` is `overwrite` (the default) or `skip`.
- The routes do not collide with others: `exports` and `imports` are literal segments after `{view}`, and `views` is a reserved view name.

#### 2.2 Access

The scopes depend on the included kinds. The roles stay as in PR #88: any role for an export, `Contributor` for an import.

| Included kind | Export, and reading an export | Import, and reading an import |
| --- | --- | --- |
| Annotations | `Annotation.Read`, `Annotation.ReadWrite`, `Default.Read`, `Default.ReadWrite` | `Annotation.ReadWrite`, `Default.ReadWrite` |
| Schemas, Templates, Configurations | `Configuration.Read`, `Configuration.ReadWrite`, `Default.Read`, `Default.ReadWrite` | `Configuration.ReadWrite`, `Default.ReadWrite` |

- A request needs the scopes of every kind it includes. Reading a job checks the scopes of the job's `Include`.
- A machine with only `Configuration.ReadWrite` must pass `include` without `annotations`. The `403` message says which scope is missing and names `include`.

#### 2.3 OpenAPI and SDKs

- The OpenAPI document is exported again, and NSwag and the TypeScript SDK are regenerated. The SDKs get the `View*` types; `Include` is a list of `ViewArchiveContent` strings.
- `ViewTransferHttpTests` cover:
  - the options and their defaults;
  - `400` for an unknown kind;
  - the scopes per kind;
  - the removed routes.
- The OpenAPI schemas test covers `Kind`, `Summaries`, `Include`, `Existing` and `Warnings`.

### Phase 3 — CLI

The commands move from `sform story config` to `sform story`, because they now cover the view (`Cli/src/Commands/Storyteller/StorytellerExportCommand.cs`, `StorytellerImportCommand.cs`):

```
sform story export [-f|--format folder|git] [-i|--include <kinds>] [-o|--output <path>] [-x|--extract] [--timeout <minutes>]
sform story import <zip-or-folder> [-i|--include <kinds>] [-e|--existing overwrite|skip] [--dry-run] [--force] [--timeout <minutes>]
```

- `--include` takes `annotations,schemas,templates,configurations`; all four by default.
- `sform story config export` and `sform story config import` are removed (not released).
- **Packing a folder** takes every `*.json` below it, without the hidden folders and files (any segment starting with `.`), the same as the server's rule.
- **The import table:**
  - columns **Kind**, **Document** (the key, or the type code), **Outcome**, **Code** and **Message**;
  - warnings are printed under their row, marked `!`;
  - the summary is printed per kind, then the total with the excluded and ignored files.
- The exit code is `2` when a file failed or the server rejected the upload. Warnings do not change the exit code.

### Phase 4 — Administration UI

#### 4.1 The Export & Import page (`pages/[org]/[project]/[view]/export-import.vue`)

- **Navigation:**
  - a sidebar entry **Export & Import** in the view section (`utils/navigation.ts`), after **Compare views**, with the icon `i-lucide-arrow-down-up` and `editRole: 'Contributor'`. A reader sees it marked as locked and can still export;
  - a command palette page and the shortcut `g e` (`components/shell/CommandPalette.vue`, `composables/useShortcutsHelp.ts`).
- **Layout.** Two sections, **Export** and **Import**, side by side on desktop and stacked on mobile. Below them, full width, is the **result of the latest import** of the view.
- **The configurations page loses its header buttons and both dialogs.** The panels replace the dialogs: a page has the room for a long result, and every option stays visible.

#### 4.2 Export section (`components/transfer/ExportPanel.vue`)

- Four checkboxes, **Annotations**, **Schemas**, **Templates** and **Configurations**, all checked. At least one is required.
- The format as radio cards: **Folder of JSON files** and **Git repository**.
- **Export** starts the job. The progress shows in place, and the download starts when the export is ready, as in PR #88.
- A line says what is never included: secrets, effective documents, the audit, version history of annotations, and deleted annotations.

#### 4.3 Import section (`components/transfer/ImportPanel.vue`)

- **Who.** For a role below `Contributor`, the section shows "Importing needs the Contributor role" instead of the form.
- **The file:** `UFileUpload` takes a zip. Its hint: "Folders don't matter: files are recognized by their names. Version files, export.json and hidden folders are ignored."
- **Kinds:** the same four checkboxes, all checked. They say what may be written.
- **Existing documents:** radio cards.
  - **Overwrite** (the default): "The archive's content replaces them as a new version. Annotations are updated, and deleted ones are re-created with an empty configuration."
  - **Skip**: "They stay as they are; only new documents are written."
- **Force:** the checkbox "Store schemas and configurations that break each other", described as "Without it, they fail and the rest is imported."
- **Buttons:** **Check** (a dry run) and **Import**. After a check, **Import now** runs the same file with the same options.
- The progress shows in place. Leaving the page doesn't stop the job; `TransferToaster` reports its end.

#### 4.4 Result (`components/transfer/ImportResult.vue`)

- **Banner:** "Checked: nothing was written" after a dry run, or "Imported".
- **Summary:** one row per included kind, with the counts of created, updated, unchanged, skipped, failed and warnings. A total row also counts the excluded and ignored files.
- **Filters:** chips by kind and by outcome (**Failed**, **Skipped**, **Warnings**).
- **Table:**
  - columns: kind badge, document, outcome, version, code and message;
  - warnings are listed under their message;
  - mobile shows a list.
- **Links** go to the written or affected document:

  | Document | Link |
  | --- | --- |
  | Annotation | `…/annotations/{key}` |
  | Configuration | `…/configurations/{key}` |
  | Template | `…/templates/{type}` |
  | Type schema | `…/schemas/type/{type}` |
  | Annotation schema | `…/schemas/annotation/{key}` |
  | Descendant-type schema | `…/schemas/annotation/{key}/type/{type}` |

- After a reload, the page shows the view's latest import, as the dialog does today.

#### 4.5 Store, toaster and caches

- `stores/transfers.ts`:
  - `startExport(scope, { format, include })`;
  - `startImport(scope, file, { include, existing, force, dryRun })`;
  - the new SDK calls.
- `TransferToaster` moves to `components/transfer/` and links a finished import to the page.
- After an import that wrote documents, the toaster invalidates by kind:
  - annotations: the catalog and the annotation queries;
  - schemas: the schema queries, and every configuration query of the view, since validation changes;
  - templates: the template queries, and the effective documents of the view;
  - configurations: the configuration queries, as before.

#### 4.6 Mock API, tests

- **Mock API** (`mock/transfer-handlers.ts`, `mock/archive-names.ts`, `mock/zip.ts`):
  - the new routes and options over the mock database: annotations, `template:` and `schema:` documents, and configurations;
  - the file names of 1.3, the folder layout, warnings and `DocumentExists`.

  The git export stays the folder layout plus `.git/HEAD`.
- **Vitest:**
  - `archive-names` (the same cases as `ViewArchiveNames`);
  - the store's options;
  - the navigation entry.
- **Playwright:**
  - the page from the sidebar and from `g e`;
  - an export with only annotations and templates;
  - a dry run of an archive with an edited annotation, template and schema: the result per kind, a warning, and **Import now**;
  - an import with **Skip** that reports `DocumentExists`;
  - a reader sees the import notice;
  - axe on the page, desktop and mobile.
- **Visual baselines:**
  - the configurations page changes, since its header loses two buttons;
  - the sidebar gains an entry;
  - the new page gets its own baseline.

### Documentation

- `docs/Platform/Storyteller/configuration-transfer.md` becomes `export-import.md`, rewritten for:
  - the kinds;
  - the file names and the reserved segment;
  - folders and hidden paths;
  - the manifest v2;
  - the options;
  - the order;
  - the overlay;
  - the warnings;
  - the routes and scopes;
  - the CLI;
  - the page.
- `annotation-lifecycle.md`: an import writes the usual entries, and re-creates deleted annotations with `Overwrite` (a fourth way to re-create, next to `SetAnnotation`, `SetAnnotations` and `SetAnnotationsSimple`).
- `templating.md`: an import replaces a template rather than merging into it.
- `docs/42for.net/platform/admin-ui.md`, `configuration.md`, `annotations.md` and `using-the-platform.md`: the Export & Import page, and the `sform story export` and `sform story import` commands.

## Delivery phases

| Phase | Content | Depends on | Review |
| --- | --- | --- | --- |
| **1** | Backend: 1.1-1.16 | PR #88 | `docs/Platform/Storyteller/specs/reviews/YYYY-MM-DD Phase 1 of the Export and Import of a View.md` |
| **2** | API, OpenAPI, SDKs | 1 | `… Phase 2 of the Export and Import of a View.md` |
| **3** | CLI | 2 | `… Phase 3 of the Export and Import of a View.md` |
| **4** | Administration UI, public docs | 2 | `docs/Platform/ui.admin/specs/reviews/YYYY-MM-DD Phase 4 of the Export and Import of a View.md` |

## Files to create / change

| Path | Phase | Change |
| --- | --- | --- |
| `Storyteller/Abstractions.Annotations/src/Model/ViewTransfer.cs` (replaces `ConfigurationTransfer.cs`) | 1 | renamed types, contents, kinds, options, warnings |
| `Storyteller/Backend.Core/src/ErrorCodes.cs` | 1 | `DocumentExists`, `DependencyFailed`, `InvalidSchema`, `AncestorCreated`, `AnnotationRecreated` |
| `Storyteller/Backend.Core/src/Annotating/IAnnotationService.cs` | 1 | `GetViewAnnotationsAsync`, `ImportAnnotationAsync` |
| `Storyteller/Backend.Core/src/Configuring/IConfigurationService.cs`, `IConfigurationTemplateService.cs`, `IConfigurationSchemaService.cs` | 1 | histories, imports with options and overlay |
| `Storyteller/Backend.Core/src/Transferring/ViewArchiveNames.cs`, `ViewAnnotationDocument.cs`, `ViewImportOptions.cs`, `ViewImportOverlay.cs`, `TemplateHistory.cs`, `SchemaHistory.cs` (new) | 1 | names, annotation files, options, overlay |
| `Storyteller/Backend.Core/src/Transferring/ViewArchiveReader.cs`, `ViewArchiveWriter.cs`, `ViewArchiveFile.cs`, `ViewArchiveSource.cs`, `ViewTransferService.cs` (renamed from `Configuration*`) | 1 | kinds, layout, passes |
| `Storyteller/Backend.Core/src/Transferring/Git/GitRepositoryWriter.cs`, `GitIndexWriter.cs`, `GitPathComparer.cs` | 1 | nested trees |
| `Storyteller/Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs` (or a new `CosmosAnnotationService.Import.cs`) | 1 | view annotations, import |
| `Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationTemplateService.cs`, `CosmosConfigurationSchemaService.cs`, `CosmosConfigurationService.cs` | 1 | histories, imports, overlay |
| `Storyteller/Backend.Core/test/Transferring/*`, `Backend.CosmosDb/test/*` | 1 | tests |
| `Storyteller/Api.Functions/src/V1/ViewTransferHttp.cs` (replaces `ConfigurationTransferHttp.cs`), `Definitions.cs`, `Transfers/*`, tests | 2 | routes, options, scopes |
| `Storyteller/Sdk.NSwag/**`, `Storyteller/sdk.typescript/**` | 2 | regenerated |
| `Cli/src/Commands/Storyteller/StorytellerExportCommand.cs`, `StorytellerImportCommand.cs` (replace `Configuration/ConfigExportCommand.cs`, `ConfigImportCommand.cs`), `Services/ConfigurationArchives.cs`, `Output/TransferConsoleExtensions.cs`, tests | 3 | commands, `--include`, `--existing`, kinds |
| `ui.admin/src/app/pages/[org]/[project]/[view]/export-import.vue` (new), `configurations.vue` | 4 | new page; buttons and dialogs removed |
| `ui.admin/src/app/components/transfer/ExportPanel.vue`, `ImportPanel.vue`, `ImportResult.vue`, `TransferToaster.vue` (new or moved); `components/configuration/ConfigurationExportDialog.vue`, `ConfigurationImportDialog.vue`, `ConfigurationImportResult.vue` (deleted) | 4 | panels |
| `ui.admin/src/app/utils/navigation.ts`, `components/shell/CommandPalette.vue`, `composables/useShortcutsHelp.ts`, `stores/transfers.ts`, `mock/transfer-handlers.ts`, `mock/archive-names.ts` (new), `mock/zip.ts`, tests | 4 | navigation, store, mock |
| `docs/Platform/Storyteller/export-import.md` (renamed from `configuration-transfer.md`), `annotation-lifecycle.md`, `templating.md`, `docs/42for.net/platform/*.md` | 1-4 | documentation |

## Decisions

1. **All four kinds by default.** An archive should be able to seed or restore a view. The include options cover a partial export or import.
2. **The kind is in the file name; folders are hygiene.** Users flatten zips, move files and reorganize repositories, and the import must survive that. A name that tells its kind needs no folder, no manifest and no position. The export still writes one folder per kind, because people browse it.
3. **Reserved second segment.** Two-segment keys (`rst`, `sbt`) named `template` or `schema` would collide with type-level files. Three-segment keys like `unt.template.3` would collide with version files. Encoding one character of that segment keeps every name unique, with a one-line parse rule. Such names are rare, and the decoding is the existing one.
4. **Annotations without history or audit.** They have no versions to export. Their audit stays with the platform, because it records who did what in this view, not content. The stamps go to the manifest for information. The git commit of an annotation uses its `Created` stamp, so its schemas and configurations follow it in the history.
5. **Import order: annotations → schemas → templates → configurations,** with ancestors first among annotations. Templates are independent, and their place is a convention. The export reads in the reverse order, so a document created during the export is less likely to lose its dependency. See [Order](#order).
6. **Checked against the view as the import leaves it.** Writing schemas first would fail their compliance check on the old configurations, and configurations first would fail on the old schemas. The overlay avoids both without `force`. A dry run uses the same overlay, so it predicts the real run.
7. **`force` bypasses compliance only:** a configuration that violates its schema, and a schema that configurations violate. It never bypasses invalid JSON, an invalid JSON Schema, an annotation rule or a broken `$patch`. Forced writes carry a warning instead of passing silently. The default stays off, as in PR #88.
8. **`Existing`: `Overwrite` by default, `Skip` keeps the stored document.** An equal document is `Unchanged` in both modes, so a skip never hides a difference that didn't exist. The option applies per document: a skipped annotation doesn't skip its configuration.
9. **Deleted annotations are re-created with `Overwrite`.** The archive wins, so a deleted annotation comes back, with exactly what the archive holds: its content, and its configuration only if the archive has one. The configuration of the previous lifetime stays in the versions and doesn't come back silently, and neither do the children deleted with it. That is what any write of a deleted key through the API does, so the import adds no new behavior, and the audit records it as `Recreated`. Restoring alone was considered: it would keep the previous configuration even when the archive has none. With `Skip`, a deleted annotation is left alone.
10. **Replace for annotations and templates too.** An import makes the stored document equal to the file. Merging would keep members the file removed.
11. **No deletes, no history.** As in PR #88, an import never deletes a document that the archive lacks, and it never replays versions or commits.
12. **Documents of deleted annotations are not exported.** This changes the first revision. Annotation and descendant-type schemas now follow the annotation, as configurations already did, so a dry run on a fresh view doesn't report them as orphans.
13. **View-level names.** The archive holds the view, not its configurations. PR #88 is not released, so its routes, types, CLI commands and zip name can be renamed now without a transition. The storage names stay.
14. **A page instead of dialogs.** Four kinds, two options and a per-kind result don't fit a modal well, and a page can show the latest import after a reload.
15. **One git history for all kinds,** ordered by time and then by dependency. A blame shows a configuration change and the template change of the same minute in the right order.

## Open questions

Answered on 2026-10-15:

- templates and schemas are included by default (now decision 1, extended to annotations);
- a schema import is checked against the archive's configurations, and `force` remains the only bypass (decisions 6 and 7);
- this work ships in its own pull request after PR #88 is merged, on a branch from `main`.

Superseded on 2026-10-16: PR #88's flat archives no longer import as configurations. Backward compatibility is not needed, and `<key>.json` is now an annotation.

Answered on 2026-10-16:

- annotations are included, without versions and audit;
- file names carry the kind, folders are hygiene, and hidden folders are skipped;
- `force` stays an option of the import;
- no history is imported;
- `existing` chooses between overwriting and skipping;
- export and import take the kinds to include, all four by default;
- the order is annotations, schemas, templates, configurations;
- the dry run stays and reports warnings;
- the controls move to an Export & Import page;
- `force` is off by default, as in PR #88 (decision 7);
- with `Overwrite`, a deleted annotation is re-created, not restored or skipped (decision 9);
- the routes, types, commands and zip get view-level names (decision 13).

No questions are open.

## Out of scope

- A mirror import that deletes what the archive lacks.
- Exporting several views at once.
- Restoring version history or audit from an archive.
- Exporting deleted annotations.
- Resuming an interrupted import from where it stopped; a retried invocation runs the whole job again (1.15).
