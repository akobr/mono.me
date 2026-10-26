# Audit Stamps

Every Storyteller record that names who changed it uses the same two types, in the API, the SDKs and Cosmos DB. The design and the alternatives that were rejected are in [the spec](specs/2026-10-14%20Unified%20author%20and%20audit%20actor.md).

## The actor

`AuditActor` (`Abstractions.Annotations/src/Model/AuditActor.cs`) says who did something:

| Property | Content |
| --- | --- |
| `Kind` | `Account`, `Machine` or `System` |
| `Id` | The account id (`Account.Id`): the lower-case `{tid}:{oid}` of the person under Entra ID, the `sub` under AuthKit and Keycloak (see [Account ids](authentication.md#account-ids)). Or the machine id, or `system` |
| `Name` | A display name taken from the token when the change was made: the `name` claim, or the unique name. A Storyteller user token carries no name, so for it the name comes from the account: its `Name`, or else its `UserName`. Machines and `System` have none. |
| `Via` | The actor that acted for the account: the `act.sub` of a Storyteller user token, such as `mcp` for the [MCP server](mcp.md): a change Ada makes through it stores `Name` "Ada" and `Via` "mcp" (see [Actors](access.md#actors); the clients do not show it yet, see [Clients](#clients)). Optional: null for a direct call, and then left out of stored and returned documents. Machines and `System` have none. |

`Kind` and `Id` are the reference. A client links an account to the members of the project and a machine to its page, and resolves the current name there when it needs one. `Name` is a snapshot of the moment of the change: it is taken from the token then, or for a Storyteller user token from the account read on that call, and it is never resolved again when the record is read. It goes stale after a rename, but it renders without an extra call and survives the account's removal from the project. `Via` only adds who acted for the person; the reference stays the account.

The API names the caller with `HttpRequestDataExtensions.GetAuditActor()`. It never calls the profile resolver, so it adds no latency. For a Storyteller user token, the bearer middleware has already read the account once per call, for its names. `GetAuditActor` fills `Via` from the delegated actor, which only the Storyteller token validator gives, in every build; the claims of a token that `DEV_AUTH` decodes without validation never set it. Every record that takes the actor of the request carries `Via` with it: the stamps, the audit entries of annotations, and the key/value audit events (`KeyValueAuditEvent`), including the hourly `BindingRead` counts, which count reads through an actor apart from the person's direct reads. Debug builds (`DEV_AUTH`) also serve requests without an identity; those are `System`. The export and import of a view are the exception: they answer such a request with `401`, as in every build. The platform's own writes (the calculation cache of configurations, the key/value maintenance and key rotation) are `System` too.

## The stamp

`AuditStamp { At, By }` says who made a change and when. Records use three names:

| Property | Meaning |
| --- | --- |
| `Created` | The write that created the object. It never changes. |
| `Modified` | The write that produced the current state. On a version, the write that produced that version, so a document's `Modified` equals the `Modified` of its newest version. |
| `Deleted` | The soft delete, present only while the object is deleted. |

Audit-log entries have their own time and carry only `Actor`.

| Record | Stamps |
| --- | --- |
| `Annotation` | `Created`, `Modified` |
| `AnnotationDeletion` | `DeletedAt`, `DeletedBy` (an actor): a record of the delete operation, see [annotation-lifecycle.md](annotation-lifecycle.md) |
| `AnnotationAuditEntry`, `KeyValueAuditEvent` | `Actor` |
| `Configuration`, `ConfigurationTemplate`, `ConfigurationSchema` | `Modified` |
| `ConfigurationVersion` (configurations, templates and schemas) | `Modified` |
| `ConfigurationSummary`, `ConfigurationTemplateSummary`, `ConfigurationSchemaSummary` | `Modified` |
| `View` | `Created` (null for a view that is not registered) |
| `KeyValue` | `Created`, `Modified`, `Deleted` |
| `KeyValueSummary`, `KeyValueVersion` | `Modified` |
| `KeyValuePolicy` | `Modified` (null for the defaults) |
| `ClientEncryptionRecipient` | `Created` |

A configuration version keeps the time of its write. The configuration item is also rewritten by the platform when the effective document is cached or invalidated, but that changes neither `Modified` nor the listed time. A configuration that was never written (version 0, calculated on read) is stamped as the calculation by `System`.

## Clients

Both SDKs carry the two types (`AuditActor`, `AuditStamp` in `Sdk.NSwag` and `@42for.net/2splatform.sdk`), with `Via` since the OpenAPI document 0.9.54. `sform` prints an actor as its name, `machine:{id}` or `system` (`AuditConsoleExtensions.FormatActor`), in the version lists of configurations, templates and schemas, the key/value listing, versions, recipients and audit. The admin UI renders every actor with `AuditActorLink`: an account links to its row on the members page for Administrators, a machine to its page, and the subtle variant keeps table cells and version lists muted. Neither shows `Via` yet: both render the person alone, so a change through the MCP server looks like a direct change of the person there.

## Storage and request units

The stamps are stored as they are returned. Organization containers keep them out of the index (`ContainerFactory.AuditStampPaths`): `/Created/*`, `/Modified/*`, `/Deleted/By/*`, `/Deletion/DeletedBy/*` and `/Actor/*`, next to the audit entries (`/Entries/*`). `Via` is inside these paths, so it is excluded too. Nothing filters or sorts on them; listings only project them. The deletion times stay indexed, because the deleted listings filter on `IS_DEFINED(c.Deleted.At)` and sort on `c.Deletion.DeletedAt`. `ContainerFactory.EnsureContainerSettingsAsync` adds the exclusions to existing containers.

Measured on the Cosmos emulator, as the average of 20 creates of the same item:

| Item | Indexed as before | Stamps excluded |
| --- | --- | --- |
| Annotation with `Created` and `Modified` | 14.9 RU | 10.3 RU |
| Configuration with the old `Author` string | 9.7 RU | – |
| Configuration with `Modified` | 11.6 RU | 9.3 RU |

Excluding only the names saves little (14.5 RU for the annotation); the ids and times account for most of the cost.

## Stored data

Every stored item carries its stamps, and the entities declare them required. Nothing in the repository migrates or reads items written before the stamps (with `Author`, `CreatedBy` or `DeletedAt` fields instead), so **upgrading to this version requires recreating the Cosmos database** (drop it and create it again with DbCreator, `2sInitDb`). Keeping such records is unsupported unless a migration or a legacy mapping is added; they fail to read or show without a stamp. No deployment held data to keep when the stamps were introduced.

`Via` came later and needs no migration: it is optional, and items written without it read with `Via` null.
