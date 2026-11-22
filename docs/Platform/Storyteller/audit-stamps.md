# Audit Stamps

Every Storyteller record that names who changed it uses the same two types, in the API, the SDKs and Cosmos DB. The design and the alternatives that were rejected are in [the spec](specs/2026-10-14%20Unified%20author%20and%20audit%20actor.md).

## The actor

`AuditActor` (`Abstractions.Annotations/src/Model/AuditActor.cs`) says who did something:

| Property | Content |
| --- | --- |
| `Kind` | `Account`, `Machine` or `System` |
| `Id` | The account id (`Account.Id`): the lower-case `{tid}:{oid}` of the person under Entra ID, the `sub` under AuthKit and Keycloak (see [Account ids](authentication.md#account-ids)). Or the machine id, or `system` |
| `Name` | A display name taken from the token when the change was made: the `name` claim, or the unique name. A Storyteller user token carries no name, so for it the name comes from the account: its `Name`, or else its `UserName`. Machines and `System` have none. |
| `Via` | The actor that acted for the account: the `act.sub` of a Storyteller user token, such as `mcp` for the [MCP server](mcp.md): a change Ada makes through it stores `Name` "Ada" and `Via` "mcp" (see [Actors](access.md#actors) and [Clients](#clients)). Optional: null for a direct call, and then left out of stored and returned documents. Machines and `System` have none. |
| `ViaClient` | The client app the person used with the actor: the `subject_client_id` of a Storyteller user token, which is the `azp`, `appid` or `client_id` of the token the person signed in to the actor with, such as `client_01…` for an AuthKit client that registered itself (see [Client apps of the exchange](authentication.md#client-apps-of-the-exchange)). 1 to 256 printable ASCII characters. Set only together with `Via`; null for a direct call and for a token exchanged before the API recorded the client, and then left out of stored and returned documents. |

`Kind` and `Id` are the reference. A client links an account to the members of the project and a machine to its page, and resolves the current name there when it needs one. `Name` is a snapshot of the moment of the change: it is taken from the token then, or for a Storyteller user token from the account read on that call, and it is never resolved again when the record is read. It goes stale after a rename, but it renders without an extra call and survives the account's removal from the project. `Via` and `ViaClient` only add who acted for the person, and through which app; the reference stays the account. A client app is known by its id only: the name an app gives itself at registration is no identity, and a dynamically registered app has a new id with every registration.

The API names the caller with `HttpRequestDataExtensions.GetAuditActor()`. It never calls the profile resolver, so it adds no latency. For a Storyteller user token, the bearer middleware has already read the account once per call, for its names. `GetAuditActor` fills `Via` from the delegated actor and `ViaClient` from the delegated client, which only the Storyteller token validator gives, in every build; the claims of a token that `DEV_AUTH` decodes without validation never set them. Every record that takes the actor of the request carries `Via` and `ViaClient` with it: the stamps, the audit entries of annotations, the key/value audit events (`KeyValueAuditEvent`), including the hourly `BindingRead` counts, which count reads through an actor apart from the person's direct reads and each client app apart from the others, the [access audit](access-audit.md) records (`AccessAuditEntry`), which take it from `HttpRequestDataExtensions.GetAccessCaller()` (the actor and the invocation id), and the view exports, whose `export.json` writes both as `null` when they are not set. Debug builds (`DEV_AUTH`) also serve requests without an identity; those are `System`. The export and import of a view are the exception: they answer such a request with `401`, as in every build. The platform's own writes (the calculation cache of configurations, the key/value maintenance and key rotation) are `System` too.

## The stamp

`AuditStamp { At, By }` says who made a change and when. Records use three names:

| Property | Meaning |
| --- | --- |
| `Created` | The write that created the object. It never changes. |
| `Modified` | The write that produced the current state. On a version, the write that produced that version, so a document's `Modified` equals the `Modified` of its newest version. |
| `Deleted` | The soft delete, present only while the object is deleted. |

Audit-log entries have their own time and carry only `Actor`.

A fourth name, `Closed`, is used by one record only: the item of the [inbox of a view](inbox.md). It is the accept or the dismiss of the item, which is neither a change of its content nor a delete.

| Record | Stamps |
| --- | --- |
| `Annotation` | `Created`, `Modified` |
| `AnnotationDeletion` | `DeletedAt`, `DeletedBy` (an actor): a record of the delete operation, see [annotation-lifecycle.md](annotation-lifecycle.md) |
| `AnnotationAuditEntry`, `KeyValueAuditEvent` | `Actor` |
| `Configuration`, `ConfigurationTemplate`, `ConfigurationSchema` | `Modified` |
| `ConfigurationVersion` (configurations, templates and schemas) | `Modified` |
| `VersionRestoreResult` | `Modified` (the write of its `Version`) |
| `ConfigurationSummary`, `ConfigurationTemplateSummary`, `ConfigurationSchemaSummary` | `Modified` |
| `View` | `Created` (null for a view that is not registered) |
| `KeyValue` | `Created`, `Modified`, `Deleted` |
| `KeyValueSummary`, `KeyValueVersion` | `Modified` |
| `KeyValuePolicy` | `Modified` (null for the defaults) |
| `ProjectSettings` | `Modified` (null only before the first change; it stays after a reset to the defaults, so `Version` and `Modified` tell reset settings from settings never set); see [Project settings](access.md#project-settings) |
| `ClientEncryptionRecipient` | `Created` |
| `ViewInboxItem` | `CreatedAt`, `CreatedBy` (an actor: who caused the item, so the actor that started the job it reports, or `System`), and `Closed` (who accepted or dismissed the item and when; null while the item is open, and cleared when the handler of an accepted action fails and the item opens again): see [inbox.md](inbox.md#the-model) |

A configuration version keeps the time of its write. The configuration item is also rewritten by the platform when the effective document is cached or invalidated, but that changes neither `Modified` nor the listed time. A configuration that was never written (version 0, calculated on read) is stamped as the calculation by `System`.

A version restore of a configuration, template or schema is a write: the new version's `Modified` is the caller who restored it, at the time of the restore, not the author of the version it copies. The copied version is named in the new version's `RestoredFrom`, so a version list shows both. A restore that changes nothing writes no stamp, and its `VersionRestoreResult` carries the `Modified` of the current version.

An inbox item takes its `CreatedBy` from its producer: the notification of an export or an import copies the `CreatedBy` of its job, `Via` included. The actor of `Closed` is the one that the caller of `IViewInboxService` passes to accept or dismiss.

## Clients

Both SDKs carry the two types (`AuditActor`, `AuditStamp` in `Sdk.NSwag` and `@42for.net/2splatform.sdk`), with `Via` since the OpenAPI document 0.9.54 and `ViaClient` since 0.10.58. The C# `AuditActor` keeps unknown members in `AdditionalProperties`, so an older SDK reads documents with `ViaClient` too. `sform` prints an actor as its name, `machine:{id}` or `system` (`AuditConsoleExtensions.FormatActor`), in the version lists of configurations, templates and schemas, the key/value listing, versions, recipients and audit; it shows neither `Via` nor `ViaClient`. The admin UI renders every actor with `AuditActorLink`: an account links to its row on the members page for Administrators, a machine to its page, and the subtle variant keeps table cells and version lists muted. Only its access audit trail (the organization's **Audit** page) shows who acted for the person: "Ada Lovelace via mcp (client client_01…)", or "Ada Lovelace via mcp" without `ViaClient`; a long id may wrap. Its other audit views, and every view of `sform`, render the person alone, so a change through the MCP server looks like a direct change of the person there. The MCP server returns the SDK's objects as they are, so its tool results carry both.

## Storage and request units

The stamps are stored as they are returned. Organization containers keep them out of the index (`ContainerFactory.AuditStampPaths`): `/Created/*`, `/Modified/*`, `/Deleted/By/*`, `/Deletion/DeletedBy/*` and `/Actor/*`, next to the audit entries (`/Entries/*`). `Via` and `ViaClient` are inside these paths, so they are excluded too. Nothing filters or sorts on them; listings only project them. `CreatedBy` and `Closed` of an inbox item are not among these paths, so they are indexed with the rest of the item, although nothing filters or sorts on them either. The deletion times stay indexed, because the deleted listings filter on `IS_DEFINED(c.Deleted.At)` and sort on `c.Deletion.DeletedAt`. `ContainerFactory.EnsureContainerSettingsAsync` adds the exclusions to existing containers.

Measured on the Cosmos emulator, as the average of 20 creates of the same item:

| Item | Indexed as before | Stamps excluded |
| --- | --- | --- |
| Annotation with `Created` and `Modified` | 14.9 RU | 10.3 RU |
| Configuration with the old `Author` string | 9.7 RU | – |
| Configuration with `Modified` | 11.6 RU | 9.3 RU |

Excluding only the names saves little (14.5 RU for the annotation); the ids and times account for most of the cost.

## Stored data

Every stored item carries its stamps, and the entities declare them required. Nothing in the repository migrates or reads items written before the stamps (with `Author`, `CreatedBy` or `DeletedAt` fields instead), so **upgrading to this version requires recreating the Cosmos database** (drop it and create it again with DbCreator, `2sInitDb`). Keeping such records is unsupported unless a migration or a legacy mapping is added; they fail to read or show without a stamp. No deployment held data to keep when the stamps were introduced.

`Via` and `ViaClient` came later and need no migration: they are optional, and items written without them read with them null.
