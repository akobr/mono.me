# Phase 1 of Inbox of a View

Linear: [424-31 Prepare notification center (inbox) in Admin UI](https://linear.app/424fun/issue/424-31/prepare-notification-center-inbox-in-admin-ui).

## Overview

Phase 1 delivers the back end of [Inbox of a View](../2026-11-22%20Inbox%20of%20a%20View.md): the model of an inbox item, the publisher for producers and the service for readers with its action handlers, the ends of exports and imports as the first notifications, the Cosmos DB store, their tests and the living documentation (spec sections 1 to 4, the phase 1 rows of section 8, and the back-end tests of section 9). Nothing reads the inbox yet: the routes and the SDKs are phase 2, the page of the administration UI is phase 3.

| Commit | Subject |
| --- | --- |
| `b4b68ee16` | docs(platform): plan the inbox of a view (424-31) |
| `a4a15ccb5` | feat(platform): inbox of a view in the backend, fed by the ends of exports and imports (424-31) |
| `188aa7336` | fix(platform): a retention of the inbox outside its range no longer stops exports and imports (424-31) |
| `6ff7ff537` | docs(platform): describe the inbox of a view (424-31) |

The code was written in three steps: the model with the publisher and the service, the ends of exports and imports, and the store. The first step left no report: it was cut off by a usage limit after its code and tests were complete, and its build and its tests were checked afterwards. A review step and a fix step followed, then two more changes (see [Reviews](#reviews)).

## What Was Done

### 1. The model

As planned: `Abstractions.Access/src/Model/ViewInbox.cs` has the enums `ViewInboxItemKind`, `ViewInboxItemState`, `ViewInboxItemLevel` and `ViewInboxJobKind` (each with `StringEnumConverter`) and the records `ViewInboxJob`, `ViewInboxItem` and `ViewInboxResponse`, member for member as in the spec.

### 2. The publisher and the service

As planned, with these details:

- `Backend.Core/src/Notifying/`: `IViewInboxPublisher`, `ViewInboxPublisher`, `ViewInboxPush`; `IViewInboxService`, `ViewInboxService`, `ViewInboxQuery`, `ViewInboxFilter`; `IViewInboxActionHandler`, `ViewInboxActionContext`, `ViewInboxActionResult`; `ViewInboxIds`, `ViewInboxTopics`, `ViewInboxOptions`, `IViewInboxStore`, `ViewInboxRecord` and `ViewInboxEntryPoint`. `ErrorCodes` has `InboxItemClosed`, `InboxItemNotAction` and `InboxActionNotAvailable`. `Api.Functions/src/Program.cs` calls `AddViewInbox` after the transfer registrations.
- **Push.** The pattern of a topic and an action code ends with `\z`, not `$`. A push without a key whose new id is taken throws `InvalidOperationException` instead of answering with the other item. `CreatedAt` is taken without a `Key` too, as the time of the event. Without `ExpiresAt` an item lives for the retention from the push, also when its `CreatedAt` is in the past.
- **List.** The token is decoded before the role is looked at, so a wrong token is `InvalidQuery` for every caller. A role below `Contributor` is answered with an empty list without asking the store. The list and the count run next to each other. `EncodeToken` and `DecodeToken` are public static members of the service.
- **Accept.** The checks run in the order visibility, kind, state, handler. The codes of handlers are compared ordinally. A reopening that finds the item changed or expired is logged as a warning, one that throws as an error, and the exception of the handler goes on to the caller either way.
- **A lost replace** is repeated three times for an item that is open again, so the fourth lost replace in a row is the `Conflict`.
- **Options.** The range of `ViewInbox:RetentionDays` is checked when the setting is first read, not on start, as the options of the transfers are. The publisher reads it in the push that names no `ExpiresAt`, not in its constructor: the writer of the documentation found that a value outside the range failed the construction of the publisher and, through it, of `ViewTransferService`, against decision 9. Fixed in `188aa7336` with the test `ARetentionOutsideItsRange_FailsThePushThatNeedsIt_AndNotTheTransfers`.
- No handler is registered, as planned.

### 3. The ends of exports and imports

As planned, with the differences listed under [Deviations from the spec](#deviations-from-the-spec):

- `Backend.Core/src/Transferring/ViewTransferNotifications.cs`: a static `Create(ViewTransferRecord)` that returns the `ViewInboxPush` of an ended job, with the levels, titles, texts, lifetimes, key and `CreatedAt` of the table.
- `ViewTransferService`: the last constructor parameter `IViewInboxPublisher? inbox = null`, the private `NotifyAsync(ViewTransferRecord)` and its five calls (`ExportAsync`, `ImportAsync`, twice in `FailAsync`, `ProcessAsync`). `FailAsync` keeps the saved record for the push and still answers true only when its own replace saved `Failed`; a missing job is not reported. `NotifyAsync` returns at once without a publisher, builds the push inside its `try`, pushes with `CancellationToken.None` and logs a failure as a warning.
- A job that cannot be queued is failed by `EnqueueAsync` through `FailAsync`, so it pushes its failure inside the start request.
- The catch of `ProcessAsync` deletes the archive of an export only when `FailAsync` answered true (review finding 3).

### 4. Storage

As planned, with these details:

- `Backend.CosmosDb/src/PartitionKeys.cs`: `InboxPartitionInfix = "inbox"`, `GetInbox(project, view)`, `GetCosmosInbox(organization, project, view)`.
- `Entities/Inbox/ViewInboxEntity.cs`: id `inb.{item id}`, `PartitionKey`, `Organization`, `Project`, `View`, `ItemId`, the members of the item flat, and `ttl`; no `ViewName` and no `ProjectName`. `GetId`, `FromRecord(record, ttl)` and `ToRecord(etag)` do the mapping. `ttl` is `required`.
- `Notifying/CosmosViewInboxStore.cs`: `IViewInboxStore` over `IContainerRepositoryProvider` and the serializer settings, registered by `AddCosmosDbAnnotations` (`EntryPoint.cs`) as a singleton. `GetTimeToLive(TimeSpan)` is the public static formula of the `ttl`.
- The list and the count also filter on `c.Project` and `c.View`, the count also on the id prefix, and the point read also checks the project. A replace without an ETag is refused. The list cuts its result at `take` in memory as well.
- No change of the container: no index path, no composite index.
- `Backend.CosmosDb/test/EntityOrganizationTests.cs` names `ViewInboxEntity` in `TheEntityTypes_AreFound`.

### 8. Documentation (the phase 1 rows)

As planned, with these details:

- `docs/Platform/Storyteller/inbox.md` (new): the items, the roles, the ids, pushing, listing, dismiss, accept and the handlers, the two services, the errors, the notifications of exports and imports, the storage, the configuration, the registration and the tests. It describes no route and no page; a last section names what the next phases and 424-8 add.
- `export-import.md`: a job that ends pushes its notification (Jobs), with the titles, who sees it and the lifetimes; the rule of the archive of an export that fails; the message of an ended job and the poison queue (Background functions, Retries); the partition of the notification (Storage); the two retentions (Limits). The Administration UI and Mock API sections are left for phase 3.
- `annotation-lifecycle.md` (Containers): the partition `{project}.inbox.{view}` and the expiry of its items. The list of what expires in the container also names the jobs of exports and imports and the audit events, which it had left out.
- `audit-stamps.md`: `ViewInboxItem` with `CreatedAt`, `CreatedBy` and `Closed`; `Closed` as a fourth name of a stamp, used only there; where `CreatedBy` and the actor of `Closed` come from; that neither is left out of the index.

### 9. Tests of the back end

As planned, with these details:

- `Backend.Core/test/Notifying/`: `ViewInboxIdsTests`, `ViewInboxPublisherTests`, `ViewInboxServiceTests` and `ViewInboxRegistrationTests`, over the fakes `InMemoryViewInboxStore`, `RecordingActionHandler` and `RecordingViewInboxPublisher` and the data of `ViewInboxTestData`.
- `ViewInboxRegistrationTests` builds its container with `ValidateOnBuild` and `ValidateScopes`. Its first test runs the whole circle: an action is accepted, its handler starts an export through `IViewTransferService`, the export ends, and its notification is in the inbox. A third test, beyond the plan, shows that two handlers of one action stop `IViewInboxService` and not the transfers.
- `Backend.Core/test/Transferring/`: `ViewTransferNotificationsTests` (new) and 25 new cases in `ViewTransferServiceTests`. `InMemoryTransferStore` of `TransferFakes.cs` got the hook `Replacing`, which the tests of a job that another worker took and of a failure that was not saved use. No existing test of the transfers was changed.
- `Backend.CosmosDb/test/`: `CosmosViewInboxStoreTests` (21 cases) and `ViewInboxIsolationTests` (2 cases), without a trait. Beyond the plan: a list of 52 items through the real `ViewInboxService` with its token, a push with a key in two organizations through the real `ViewInboxPublisher`, and two pairs of a project and a view in one partition.
- The steps also ran temporary mutations of the new code and restored every file: 14 in the transfers (the five calls, the null check, a rethrow, the push outside the `try`, the invocation's token, details of the texts), all failing a new test; 23 in the store, of which 21 failed a new test and 2 survive alone by design (the limit of the list is enforced three times); and 2 for the review fixes.
- `Api.Functions/test/ViewInboxHttpTests.cs` of section 9 belongs to the routes and comes with phase 2.

### Test results

Run on the committed tree on 2026-11-22:

| Project | Result |
| --- | --- |
| `Backend.Core/test` | 1864 passed, 0 failed (1788 before the phase) |
| `Api.Functions/test` (Debug) | 3394 passed, 0 failed |
| `Backend.CosmosDb/test`, `--filter-class "*EntityOrganizationTests"` | 32 passed |
| `Backend.CosmosDb/test` on the classic emulator (container), `--filter-class "*ViewInbox*"` | 23 passed |
| `Backend.CosmosDb/test` on the classic emulator (container), `--filter-class "*Transfer*"` | 28 passed |

**The vNext emulator.** The suite's reused vNext container of this machine was broken that day: every delete of the database failed with PostgreSQL `E58P01`, so the fixture threw before any test ran (23 of 23 and 28 of 28 failed that way). The image's own package update had replaced `plpgsql.so` at a restart of the container. On a fresh vNext container the storage step saw 23 of 23 and 28 of 28 pass; a fresh container stayed reliable only until its own package update ended, about a minute after its start. The final runs on the committed tree were on the classic emulator only.

**Not run:** the whole `Backend.CosmosDb` suite, `Api.Functions/test -c Release` (phase 2 runs it), `Api.Functions/testint`, and the test run of the whole solution.

### Reviews

Three reviewers read the change, and a second reader checked every finding. Two reviewers reported three findings, the third none; all three were confirmed:

1. **No test pinned that accept tells the state before the missing handler** (confirmed; fixed, a test only). With the two checks swapped, an accepted or dismissed action without a handler would answer `501 InboxActionNotAvailable` instead of `409 InboxItemClosed`, and the suite stayed green. `Accept_OfAnActionWithoutAHandler_IsNotAvailable_AndLeavesTheItemOpen` now accepts the item again after its dismiss and expects `ConflictException` with `InboxItemClosed` and "The inbox item was already dismissed."
2. **The text of a dry-run import is worded differently from the table of section 3** (confirmed; the code stays). `3 to create, 2 to update` is right for a check that wrote nothing, and it is how the administration UI and `sform` word a dry run. It is recorded as deviation 1 and described in `inbox.md` and `export-import.md`, so the mock of phase 3 writes the same words.
3. **An export whose save of `Succeeded` lost its answer was reported as finished while the same invocation deleted its archive** (confirmed; fixed). `ExportAsync` throws out of the save, the catch of `ProcessAsync` calls `FailAsync`, which finds the job `Succeeded`, pushes "Export finished" and answers false; the catch then deleted the archive whatever the answer was. The delete is old code, and the new push made it visible in the inbox for a day. The catch now deletes only when `FailAsync` answered true, as `AbandonAsync` does; the comment above `NotifyAsync` was reworded. New test: `ProcessExport_AnEndWhoseSaveLostItsAnswerIsStillPushed_AndKeepsItsArchive`.

With the unguarded delete and the swapped checks put back, exactly the new assertions failed; both were then restored.

Two more changes followed the fix step, both in `a4a15ccb5`:

1. **`ViewTransferNotifications`**: the text of an export names the versions only when a kind with versions (schemas, templates, configurations) was exported. The job always counts versions, so an export of annotations alone read `12 annotations, 0 versions, 512 B`; it reads `12 annotations, 512 B`. Test: `ExportText_OfAnnotationsAlone_NamesNoVersions`.
2. **`CosmosViewInboxStore`**: the list and the count got the predicates `c.Project = @project AND c.View = @view`, which the point read already checked. Two pairs of a project and a view that share one partition through names with a dot never see each other's items. `Get_AnItemOfAnotherViewInTheSamePartition_IsNotFound` asserts it for the list and the count too.

### Deviations from the spec

1. **The text of a dry-run import** reads `acme.zip: 3 to create, 2 to update, 10 unchanged, 1 skipped, 0 failed`. The table of section 3 gives one text for every import; "created" would claim writes that a check never made.
2. **The catch of `ProcessAsync` deletes the archive of an export only when it failed the job itself.** The plan's Current State left that delete as it was; with the new push in `FailAsync` it would have announced a finished export whose archive the same invocation removed.
3. **The text of an export names the versions only when schemas, templates or configurations were exported.** The table shows the versions after the counts of the kinds and does not say when they are left out; `0 versions` after annotations alone says nothing.
4. **The list and the count of the store also filter on `c.Project` and `c.View`**, and the count also on `STARTSWITH(c.id, @prefix)`. The spec names the partition, the roles and the state only; the partition value alone does not tell two pairs with dotted names apart.
5. **More checks in the store than the spec names.** `GetAsync` also compares the project, not only the view. `TryReplaceAsync` throws `ArgumentException` for a record without an ETag, because Cosmos DB would replace unconditionally. `QueryAsync` cuts its result at `take` in memory, so "at most `take`" does not depend on the engine keeping `TOP`. `ttl` is `required` on the entity, unlike on `ViewTransferEntity`.
6. **`ViewTransferNotifications.Create` throws `ArgumentException`** for a job that has not ended and for a record without the model of its kind. Such a record counts as failed (`ViewTransferRecord.Status`), but it has no actor and no time to report. `NotifyAsync` catches it, and a test pins that.
7. **A terminal job without `CompletedAt` is still reported**, at its `StartedAt` or else its `CreatedAt`. The spec does not cover the case; every writer of an end sets `CompletedAt`.
8. **In `ImportAsync` the push comes before the delete of the upload**, both after the save. A delete that throws under cancellation then cannot skip the push.
9. **The pattern of a topic and an action code ends with `\z`** where the spec writes `$`, as the pattern of an id does: `$` would accept a code followed by a line feed.
10. **Texts the table leaves open.** A succeeded import without a `Summary` has only the file name as its text, or none; an export without counts and without a size has none. Sizes use steps of 1024 and are rounded before the unit is chosen (`1.0 MB`, never `1024.0 KB`), with `GB` as the largest unit.
11. **Tests beyond section 9**: the third registration test (two handlers of one action), the list through the real service and the keyed push in two organizations on Cosmos DB, and the export twin of the lost-answer test.

### Left for later

- **Phases 2 and 3**: the routes, the OpenAPI snapshot and the SDKs; the page of the administration UI, its mock and its documentation. The mock of the transfers must write the texts as the code does: the dry-run wording and the rule of the versions.
- **The first handler, and what an action carries for it**, come with 424-8. Until then no producer pushes an action, and an action answers `InboxActionNotAvailable`.
- **A leftover archive waits for the purge** (the retention plus one hour) when the job was not failed by the call that caught the exception: a job that is gone, one that another worker failed or took, and a failure that could not be saved. It is not downloadable in the meantime, and a job left `Running` is still failed and cleaned by `AbandonAsync`.
- **The second scenario of finding 3 has no test of its own**: a worker that throws after another worker ended the same export. The same guard covers it.
- **The vNext emulator of this machine.** The broken reused container was left alone because the suites share it; removing it lets Testcontainers create a new one. Whether the image updates itself the same way in CI was not checked.
- **The whole `Backend.CosmosDb` suite** was not run on either emulator; only the classes of the inbox, of the transfers and `EntityOrganizationTests` were.
