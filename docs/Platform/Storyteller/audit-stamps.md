# Audit Stamps

Every Storyteller record that names who changed it uses the same two types, in the API, the SDKs and Cosmos DB. The design and the alternatives that were rejected are in [the spec](specs/2026-10-14%20Unified%20author%20and%20audit%20actor.md).

## The actor

`AuditActor` (`Abstractions.Annotations/src/Model/AuditActor.cs`) says who did something:

| Property | Content |
| --- | --- |
| `Kind` | `Account`, `Machine` or `System` |
| `Id` | The account's subject (`sub`, which is `Account.Id`), the machine id, or `system` |
| `Name` | A display name taken from the token when the change was made: the `name` claim, or the unique name. Machines and `System` have none. |

`Kind` and `Id` are the reference. A client links an account to the members of the project and a machine to its page, and resolves the current name there when it needs one. `Name` is a snapshot: it is never looked up and goes stale after a rename, but it renders without an extra call and survives the account's removal from the project.

The API names the caller with `HttpRequestDataExtensions.GetAuditActor()`. It never calls the profile resolver, so it adds no latency. Debug builds (`DEV_AUTH`) also serve requests without an identity; those are `System`. The platform's own writes (the calculation cache of configurations, the key/value maintenance and key rotation) are `System` too.

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

## Storage and request units

The stamps are stored as they are returned. Organization containers keep them out of the index (`ContainerFactory.AuditStampPaths`): `/Created/*`, `/Modified/*`, `/Deleted/By/*`, `/Deletion/DeletedBy/*` and `/Actor/*`, next to the audit entries (`/Entries/*`). Nothing filters or sorts on them; listings only project them. The deletion times stay indexed, because the deleted listings filter on `IS_DEFINED(c.Deleted.At)` and sort on `c.Deletion.DeletedAt`. `ContainerFactory.EnsureContainerSettingsAsync` adds the exclusions to existing containers.

Measured on the Cosmos emulator, as the average of 20 creates of the same item:

| Item | Indexed as before | Stamps excluded |
| --- | --- | --- |
| Annotation with `Created` and `Modified` | 14.9 RU | 10.3 RU |
| Configuration with the old `Author` string | 9.7 RU | – |
| Configuration with `Modified` | 11.6 RU | 9.3 RU |

Excluding only the names saves little (14.5 RU for the annotation); the ids and times account for most of the cost.

## Items stored before the stamps

Configurations, configuration history, schemas, templates, their history, and views stored before the stamps carry the author as a string (`account: {sub}`, `application: {machineId}` or `system`) and a separate time (`CreationTime`, `CreatedAt`, or none). They are not migrated. Their entities keep those fields as nullable legacy properties, and the reads build the stamp from them (`LegacyAuthors`): `account:` gives an `Account`, `application:` a `Machine`, `system` or nothing `System`, and anything else an `Account` with the text as its id. The time is the old time field, or the item's last write (`_ts`) where there was none. The next write of the item stores the stamp and drops the legacy fields; a history item archived from a legacy item gets the converted stamp.

Key/value items, their versions and audit events, and the key/value policy changed shape without a compatibility path. They were merged on the day of the change, and no deployment held key/value data to keep.
