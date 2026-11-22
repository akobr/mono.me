# Inbox of a View

Every view has an **inbox**: the place where the platform leaves things for the people of the project. An item is a stored document that a producer of the back end pushes. It is the result of work that ran in the background (a notification), something the platform suggests (an action, to accept or dismiss), or a plain text (a message).

The inbox is there for two things. The result of background work gets a place where it stays, for everybody with the role, and not only for the browser tab that started the job. And the platform gets a way to ask people for a decision. The first producer is the [export and import of a view](export-import.md): every job that ends pushes a notification.

This page describes what exists today: the model, the publisher for producers, the service for readers, the Cosmos DB store, and the three [routes](#routes) of the API with their SDK clients. People with the Contributor role on the project read the inbox of a view and accept or dismiss its items; machines cannot. No page of the administration UI reads the inbox yet (see [Not there yet](#not-there-yet)). The design and its decisions are in [the spec](specs/2026-11-22%20Inbox%20of%20a%20View.md).

The code is in:

- `src/Platform/Storyteller/Abstractions.Access/src/Model/ViewInbox.cs`: the public model (`ViewInboxItem`, `ViewInboxJob`, `ViewInboxResponse` and their enums);
- `Api.Functions/src/V1/ViewInboxHttp.cs`: the three routes, with the role of the caller from `Security/HttpRequestDataExtensions.cs` (`GetUserRoleInProjectAsync`);
- `Backend.Core/src/Notifying/`: the publisher (`IViewInboxPublisher`, `ViewInboxPublisher`, `ViewInboxPush`), the service (`IViewInboxService`, `ViewInboxService`, `ViewInboxQuery`, `ViewInboxFilter`), the handlers of actions (`IViewInboxActionHandler`, `ViewInboxActionContext`, `ViewInboxActionResult`), the ids (`ViewInboxIds`), the topics (`ViewInboxTopics`), the options (`ViewInboxOptions`), the contract of the store (`IViewInboxStore`, `ViewInboxRecord`) and the registration (`ViewInboxEntryPoint`);
- `Backend.Core/src/Transferring/`: the notifications of exports and imports (`ViewTransferNotifications`, pushed by `ViewTransferService`);
- `Backend.CosmosDb/src/`: the store (`Notifying/CosmosViewInboxStore.cs`), the entity (`Entities/Inbox/ViewInboxEntity.cs`) and the partition (`PartitionKeys.cs`).

## Items

An item has a kind, a state and a level.

| Kind | Meaning | Closed by |
| --- | --- | --- |
| `Notification` | The result of work that ran in the background | dismiss |
| `Action` | Something the platform suggests | accept or dismiss |
| `Message` | A plain text of the platform | dismiss |

| State | Meaning |
| --- | --- |
| `Open` | Nobody has handled the item. Every pushed item starts here. |
| `Accepted` | Somebody accepted the action. Only an `Action` gets here. |
| `Dismissed` | Somebody dismissed the item. |

```mermaid
stateDiagram-v2
  [*] --> Open: push
  Open --> Accepted: accept (an action with a handler)
  Accepted --> Open: the handler failed
  Open --> Dismissed: dismiss
  Open --> [*]: the ttl runs out
  Accepted --> [*]: the ttl runs out
  Dismissed --> [*]: the ttl runs out
```

The level is `Info` (the default), `Success`, `Warning` or `Error`. The producer sets it, and the back end does not branch on it.

### The model

`ViewInboxItem`:

| Member | Content |
| --- | --- |
| `Id` | 53 characters whose order is the time order (see [Ids](#ids)) |
| `Kind`, `State`, `Level` | As above |
| `Topic` | The stable code of what the item is about: `ViewExport` or `ViewImport` today (`ViewInboxTopics`). Clients may branch on it, so an existing value never changes. |
| `Title` | Written by the producer, in English as the messages of the API are; at most 200 characters |
| `Text` | Optional, at most 2000 characters |
| `MinimalRole` | The lowest role of the project that sees the item and may accept or dismiss it |
| `CreatedAt` | The time of the event the item reports, or the time of the push |
| `CreatedBy` | An [`AuditActor`](audit-stamps.md#the-actor): who caused the item. The actor that started the job, or `System`. |
| `ExpiresAt` | The item is removed after this time |
| `Closed` | An `AuditStamp`: who accepted or dismissed the item, and when. Null while the item is open. |
| `Action` | Of an `Action`: the code of what the acceptance runs |
| `Job` | A `ViewInboxJob`: the background job the item reports, or the one its acceptance started. `Kind` (`ViewExport` or `ViewImport`), `Id`, and `FileName` (the archive of an export, or the uploaded file of an import). |

The model is flat. The producer writes the title and the text, so a new producer needs no change of a reader. `Topic`, `Level`, `Action` and `Job` are what a reader branches on.

The notification of an export that finished, as an Administrator reads it (members that are null are left out):

```json
{
  "Id": "20261122091407000000079aab47ca406989d11506daca106041b",
  "Kind": "Notification",
  "Topic": "ViewExport",
  "Level": "Success",
  "Title": "Export finished",
  "Text": "12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB",
  "MinimalRole": "Administrator",
  "State": "Open",
  "CreatedAt": "2026-11-22T09:14:07+00:00",
  "CreatedBy": { "Kind": "Account", "Id": "user_01", "Name": "Ada Lovelace" },
  "ExpiresAt": "2026-11-23T09:14:07+00:00",
  "Job": {
    "Kind": "ViewExport",
    "Id": "5f0c2d7e9a414b6c8e3d1f2a4b6c8d0e",
    "FileName": "export-acme-billing-default-20261122-091358Z.zip"
  }
}
```

An open action. No producer pushes one today; the topic and the action code are the ones the tests use:

```json
{
  "Id": "2026112208000000000003f2b8c1d4e5f4a6b9c0d1e2f3a4b5c6d",
  "Kind": "Action",
  "Topic": "SchemaSuggestion",
  "Level": "Info",
  "Title": "Generate a schema for 4 similar configurations",
  "MinimalRole": "Contributor",
  "State": "Open",
  "CreatedAt": "2026-11-22T08:00:00+00:00",
  "CreatedBy": { "Kind": "System", "Id": "system" },
  "ExpiresAt": "2026-12-22T08:00:00+00:00",
  "Action": "GenerateSchema"
}
```

A list is a `ViewInboxResponse`: `Items`, `ContinuationToken`, `Count` (the items of this page) and `OpenCount` (see [Listing](#listing)).

### One state for everybody

An item has one state. It is open for everybody who sees it, and closed for everybody once one of them accepts or dismisses it; `Closed` names that person. There is no read mark per person: the inbox is a shared work list.

## Roles and visibility

Roles are granted on a project, never on a view ([access.md](access.md#roles)). An item belongs to a view, and the caller's role on the project decides who sees it.

- **The minimal role of an item** is set by its producer. `Contributor` is the lowest: the publisher refuses a push below it. So a Reader has no inbox.
- **A caller with the role R** sees the items whose `MinimalRole` is between `Contributor` and R:

  | Role of the caller | Sees items for |
  | --- | --- |
  | `None`, `Reader` | nothing |
  | `Contributor` | `Contributor` |
  | `ContributorWithSecrets` | `Contributor`, `ContributorWithSecrets` |
  | `Administrator` | those, and `Administrator` |
  | `Owner` | all four |

- **An item above the role is not found.** Accept and dismiss answer `NotFoundException` with "The inbox item '{id}' does not exist or has expired.", which is also the answer for an unknown id, an id of another shape, an item of another view and an expired item. The role is looked at before the kind and the state, so a Contributor who accepts or dismisses an Administrator's item never gets a conflict that would tell that the item exists.
- **The list and the count** apply the same rule: they ask the store only for the roles the caller sees.
- **The service does not look the role up.** It takes it as a parameter (`ViewInboxQuery.Role`, the `role` of accept and dismiss); whoever calls the service gives the caller's role on the project. The [routes](#access) read it once per request and hand it over.

## Ids

An id is `{yyyyMMddHHmmssfffffff}{32 hex}`: the 21 digits of a time in UTC and 32 lower-case hexadecimal characters, 53 characters in all (`ViewInboxIds`).

- **`NewId(time)`** ends with a new GUID (format `N`).
- **`ForKey(time, key)`** ends with the first 32 characters of the SHA-256 of the UTF-8 bytes of the key, so the same key and time give the same id.
- **The time** is the item's `CreatedAt`. It is formatted in UTC with the invariant culture, so one instant has one text, whatever its offset and the culture of the thread.
- **The order of the ids is the order of the times**, so the list pages on the id, and an id is a clean path segment.
- **`IsValid`** is `^[0-9]{21}[0-9a-f]{32}\z`. Accept and dismiss answer "not found" for any other id without asking the store.

The time comes from a clock of the back end: the time of the event for a push that names it, else the clock of the publisher. Items of one tick are ordered by their second half, and items written by instances whose clocks differ can come in another order than they happened.

## Pushing an item

A producer takes `IViewInboxPublisher` and calls `PushAsync(ViewInboxPush push, CancellationToken cancellationToken = default)`. The answer is the stored `ViewInboxItem`.

| Member of `ViewInboxPush` | |
| --- | --- |
| `Organization`, `Project`, `View` | Required: the inbox |
| `Kind` | Required |
| `Topic` | Required: a code |
| `Level` | `Info` when left out |
| `Title` | Required |
| `Text` | Optional |
| `MinimalRole` | Required, without a default: a producer that forgets it does not compile, so it never shows its item to Contributors by accident |
| `CreatedBy` | Required |
| `ExpiresAt` | Optional: when left out, now plus the retention of the [configuration](#configuration) |
| `Action` | Of an `Action`: the code of its [handler](#action-handlers) |
| `Job` | Optional |
| `Key` | Optional: makes the push idempotent |
| `CreatedAt` | The time of the event. Required with a `Key`; when left out, now. |

- **Validation.** A wrong push is an error of its producer, not of a request: it throws `ArgumentException` and stores nothing. That is a blank organization, project, view or title; a `Topic` or an `Action` that is not a code (`^[A-Za-z][A-Za-z0-9]{0,63}\z`: a letter, then up to 63 letters or digits); an `Action` kind without an `Action` code, or another kind with one; a `MinimalRole` below `Contributor`; a `Key` without `CreatedAt`.
- **Cuts.** A `Title` longer than 200 characters is cut to 199 and `…`, a `Text` longer than 2000 to 1999 and `…`, as the jobs cut their messages.
- **Lifetime.** Without `ExpiresAt` the item lives for `ViewInbox:RetentionDays` from the push, also when `CreatedAt` is in the past. A given `ExpiresAt` is kept when it is between one minute and 365 days from now; a time outside is moved to the nearer limit. The store gets the time the item still has (`ExpiresAt` minus now) as the time to live of the write.
- **Without a `Key`** every push stores a new item.
- **With a `Key`** the id comes from the key and `CreatedAt`, so a second push of the same event meets the stored item. That item is returned as it is, also when somebody closed it meanwhile; nothing is written, and the title, the text and the expiry of the second push are neither compared nor applied. When the create conflicts and the read finds nothing (the stored item expired in between), the create is repeated once; a second conflict throws `InvalidOperationException`.
- **The token** of the caller stops the push.

The push of a finished export, as `ViewTransferNotifications` builds it, with the text of one export written out:

```csharp
var item = await inbox.PushAsync(new ViewInboxPush
{
    Organization = record.Organization,
    Project = record.Project,
    View = record.View,
    Kind = ViewInboxItemKind.Notification,
    Topic = ViewInboxTopics.ViewExport,
    Level = ViewInboxItemLevel.Success,
    Title = "Export finished",
    Text = "12 annotations, 512 B",
    MinimalRole = AccountRole.Administrator,
    CreatedBy = export.CreatedBy,
    ExpiresAt = export.ExpiresAt,
    Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = record.Id, FileName = export.FileName },
    Key = $"{ViewInboxTopics.ViewExport}.{record.Id}",
    CreatedAt = export.CompletedAt,
});
```

## Listing

`IViewInboxService.GetItemsAsync(organization, project, view, query)` reads one page. `ViewInboxQuery` has the `Filter`, the `Role` of the caller and the `ContinuationToken`.

- **Filter.** `Open` (the default) or `Done`: the accepted and the dismissed items.
- **Order.** Newest first, by the id. The `Done` list has the same order: by the time the items arrived, not by the time they were closed.
- **Pages.** 50 items (`ViewInboxQuery.PageSize`). The service reads one item more to know whether a page follows, and then answers with a `ContinuationToken`.
- **The token** is the base64url text, without padding, of `1:` and the id of the last item of the page. The next page holds the items just below that id, so nothing is repeated. An item whose id is above it (a new item, or one pushed later with the time of an earlier event) shows when the list is read again from the start. The token is not tied to the filter or the role: send the same ones with it.
- **A token this inbox did not issue** is `InvalidInputException` with `InvalidQuery` and "The continuation token is not one of the inbox; start again without it." It is checked first, for every role. A blank token is the first page.
- **`Count`** is the number of items of the page. **`OpenCount`** is every open item the caller sees, whatever the page and the filter. It is a count query of its own, run next to the list.
- **A role below `Contributor`** gets an empty answer with `OpenCount` 0; the store is not asked. A caller of the API never gets that answer: the route refuses the role first (`403`).
- **The view is not checked.** The inbox of a view that does not exist is empty.
- **Expiry.** The list and the count do not compare `ExpiresAt` with the clock. They rely on the `ttl` of the store (see [Storage](#storage)).

## Dismiss

`DismissAsync(organization, project, view, id, role, actor)` closes an item of any kind while it is `Open`. The state becomes `Dismissed` and `Closed` is the actor and the time (`AuditStamp.Of(actor, now)`), saved under the ETag of the item. The answer is the item. `ExpiresAt` stays: the write gets the time the item still has.

An item that is not open is `ConflictException` with `InboxItemClosed` and "The inbox item was already accepted." or "The inbox item was already dismissed."

## Accept

`AcceptAsync(organization, project, view, id, role, actor)` accepts an open `Action` and runs its handler. The checks come in this order, and the first failure answers:

1. **Visible.** An item the caller does not see, or one that is gone or expired, is not found.
2. **An action.** Another kind is `ConflictException` with `InboxItemNotAction`: "The inbox item is not an action; only an action can be accepted."
3. **Open.** An accepted or dismissed item is `InboxItemClosed`, as for dismiss.
4. **A handler.** An action whose code has no registered handler is `FeatureNotConfiguredException` with `InboxActionNotAvailable`: "The action '{code}' of the inbox item is not available in this deployment." The item stays open.

Then the work starts:

5. **The claim.** The item is saved as `Accepted` with the `Closed` stamp, under its ETag, before the handler runs. So two people cannot start the work twice.
6. **The point of no return.** The cancellation token of the caller is listened to up to and including the claim; a token cancelled before it starts nothing. From the claim on, the handler, the reopening after a failure and the save of the job run with `CancellationToken.None`. A caller that goes away leaves no accepted item without its work.
7. **The handler** runs with the accepted item and the actor (`ViewInboxActionContext`).
8. **A handler that throws** reopens the item: it is saved as `Open` with `Closed` cleared, and the exception goes on to the caller as it is. A `ConflictException` with `TransferLimit` of a job that could not start arrives as such. The reopening is best effort: when it fails, the item stays accepted without its work, and the failure is logged.
9. **A job the handler returns** is saved on the item as `Job`, best effort. The answer carries the job whether or not the save succeeded.

The answer is the accepted item. A crash of the host between the claim and the start of the work leaves an accepted item whose work never ran; nothing repairs that.

### A lost replace

The save of a dismiss or of a claim is guarded by the ETag. The store answers nothing for a changed ETag and for a removed item, and the service then reads the item again:

| The item is now | Answer |
| --- | --- |
| gone, expired or no longer visible | `NotFoundException` |
| accepted or dismissed by somebody else | `ConflictException` with `InboxItemClosed` and the message of that state |
| open again (the handler of another caller's acceptance failed) | the replace is repeated, three times at most; then `ConflictException` with `Conflict` and "The inbox item changed; try again." |

### Action handlers

What an accepted action runs is an `IViewInboxActionHandler`:

```csharp
public interface IViewInboxActionHandler
{
    // The code of the actions it runs (ViewInboxItem.Action).
    string Action { get; }

    Task<ViewInboxActionResult> RunAsync(ViewInboxActionContext context, CancellationToken cancellationToken);
}

public sealed record ViewInboxActionContext(string Organization, string Project, string View, ViewInboxItem Item, AuditActor Actor);

public sealed record ViewInboxActionResult
{
    public static ViewInboxActionResult None { get; } = new();

    // The job the acceptance started.
    public ViewInboxJob? Job { get; init; }
}
```

- **The code** of `Action` is compared exactly, case included, with the `Action` of the item.
- **`RunAsync`** is called after the item is accepted, and its token is never the caller's: the work runs to its end. A typical handler starts a background job, whose result arrives later as a notification, and returns the job.
- **Throw only when nothing was started.** The item is then open again and can be accepted once more.
- **Registration.** A handler is a registered `IViewInboxActionHandler` service; `ViewInboxService` takes all of them. Two handlers with one code fail the construction of the service ("The inbox action '{code}' has two handlers: …"), which stops the readers of the inbox and nothing else (see [Two services](#two-services)).

A handler that starts an export, in the shape `ViewInboxRegistrationTests` uses:

```csharp
public sealed class ExportViewHandler(IViewTransferService transfers) : IViewInboxActionHandler
{
    public string Action => "ExportView";

    public async Task<ViewInboxActionResult> RunAsync(ViewInboxActionContext context, CancellationToken cancellationToken)
    {
        var export = await transfers.StartExportAsync(
            context.Organization,
            context.Project,
            context.View,
            ViewArchiveFormat.Folder,
            include: null,
            context.Actor,
            cancellationToken);

        return new ViewInboxActionResult
        {
            Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = export.Id, FileName = export.FileName },
        };
    }
}

services.AddSingleton<IViewInboxActionHandler, ExportViewHandler>();
```

**No handler is registered today**, and no producer pushes an action. An action without a handler answers `InboxActionNotAvailable`. The first handler comes with 424-8, which also decides what an action carries for its handler besides the members of the item.

## Two services

Pushing and reading are two services on purpose:

- `IViewInboxPublisher` is for producers. It needs the store, the options and the clock.
- `IViewInboxService` is for readers: the list, accept and dismiss. It needs the store, the clock and every registered handler.

A handler starts a job through the service of the jobs, and that service pushes the end of the job. With one inbox service, the job service would need the inbox, the inbox its handlers, and a handler the job service: a circle the container refuses. With two, the job service takes the publisher only, and nothing that pushes depends on what an acceptance runs. A wrong handler therefore stops the readers of the inbox and never an export.

## Errors

| Exception | `ErrorCode` | Status | When |
| --- | --- | --- | --- |
| `NotFoundException` | `NotFound` | `404` | Accept or dismiss of an item that does not exist in this view, has an id of another shape, has expired, or is above the caller's role |
| `ConflictException` | `InboxItemNotAction` | `409` | Accept of a notification or a message |
| `ConflictException` | `InboxItemClosed` | `409` | Accept or dismiss of an item that is already accepted or dismissed |
| `ConflictException` | `Conflict` | `409` | The item kept changing: a replace was lost four times in a row |
| `FeatureNotConfiguredException` | `InboxActionNotAvailable` | `501` | Accept of an action whose code has no handler |
| `InvalidInputException` | `InvalidQuery` | `400` | A continuation token this inbox did not issue |
| the handler's own | | by its type | Accept, when the handler throws; the item is open again |

`InboxItemClosed`, `InboxItemNotAction` and `InboxActionNotAvailable` are new in `Backend.Core/src/ErrorCodes.cs`. The status is the one the API answers: the [routes](#routes) catch nothing, and `ErrorResponseMapping` (`Api.Functions/src/ErrorHandling/`) answers an exception by its type, with an `ErrorResponse` that carries its message and its `ErrorCode`. The routes add the `401` and the `403` of their own checks, and the `400` of a wrong `state`.

The publisher throws `ArgumentException` for a wrong push, and `InvalidOperationException` when an item can be neither created nor read. Both are errors of a producer or of the store, not answers to a reader.

## Routes

Three routes of the API read the inbox of a view and close its items. They are below `v1/{organization}/{project}/{view}`, with the tag `Inbox`:

- `Api.Functions/src/V1/ViewInboxHttp.cs`: the three routes;
- `Definitions.cs`: the routes (`Routes.Inbox.V1`), the operation ids (`RouteIds.Inbox`), the tag (`Tags.Inbox`) and the descriptions of the answers;
- `Security/HttpRequestDataExtensions.cs`: the role of the caller (`GetUserRoleInProjectAsync`);
- `OpenApi/OperationAccess.cs`: the three operations among those for users only.

| Method | Route | Operation | Answers |
| --- | --- | --- | --- |
| `GET` | `…/inbox?state=&continuationToken=` | `GetViewInbox` | `200` `ViewInboxResponse`; `400` `InvalidQuery` |
| `POST` | `…/inbox/{id}/accept` | `AcceptViewInboxItem` | `200` `ViewInboxItem`; `404`; `409` `InboxItemNotAction`, `InboxItemClosed`, `Conflict`, or the handler's own; `501` `InboxActionNotAvailable` |
| `POST` | `…/inbox/{id}/dismiss` | `DismissViewInboxItem` | `200` `ViewInboxItem`; `404`; `409` `InboxItemClosed`, `Conflict` |

- Every route also answers `401` and `403` (see [Access](#access)).
- `inbox` is a literal segment after the view, as `exports` and `imports` are, so the routes meet no other route.
- **An item has no route of its own**, only its two actions, and the list is only read. `GET` or `DELETE …/inbox/{id}`, `GET …/inbox/{id}/accept`, `PUT …/inbox/{id}/dismiss`, another action such as `POST …/inbox/{id}/reopen`, and `POST …/inbox` match no function, so the host answers `404`.
- **Today an acceptance never answers `200`**: no handler is registered, so an open action answers `501`, and any other item `409` or `404` (see [Accept and dismiss](#accept-and-dismiss)).

### Access

- **Users only.** A machine is refused in every build with `403 AccessDenied` and "Only users may call this operation; machines cannot.", whatever its credentials carry, `User.Impersonation` included, and before a role is looked up. No machine scope gives the inbox. So a pipeline that exports or imports as a machine never reads the notification of its own job: it polls the job, and the item stays open until a person dismisses it or it expires.
- **A token that acts for a user.** Without `User.Impersonation` the answer is `401` without a body, as on every route for users.
- **The `Contributor` role on the project.** `GetUserRoleInProjectAsync` reads the caller's role on `{organization}.{project}` (`IAccessService.GetAccountRoleAsync`), once per request. Below `Contributor` the answer is `403 AccessDenied` with the message of every role check of the API, for example `No Contributor access to 'acme.billing'.` The service is not asked, so a Reader learns nothing about an item, not even whether its id exists.
- **The role is handed to the service.** The helper answers the role itself, not only whether it is enough, and the route gives it to the service as `ViewInboxQuery.Role` or as the `role` of accept and dismiss. The service lists and counts what that role sees, and an item above it is not found: `404`, as for an unknown id, whatever is asked of the item (see [Roles and visibility](#roles-and-visibility)). So the `403` tells that the caller has no inbox, and nothing tells that an item exists which the caller may not see.
- **Through an actor**, such as the MCP server's token of a person, the role is the person's, and the stamp of an acceptance or a dismissal names the person with the actor as `Via`. No restriction of an actor applies.
- **No step-up.** A sign-in that is not recent is served as a recent one: the routes change no access and reveal no secret.
- **No access audit record**, and no event of another audit trail. What records an acceptance or a dismissal is the stamp `Closed` of the item.
- **The view is not checked**, as on the other routes of a view. The inbox of a view without items, or of a view that does not exist, is an empty list, and accept and dismiss answer `404` there.
- **Debug builds** (`DEV_AUTH`) skip the checks of users, `User.Impersonation` and the role, as on every route. No role is looked up, and the caller counts as `Owner`: the service hides nothing, also from an account that is only a Reader. A request without an identity is served as an `Owner`'s too, and its acceptance or dismissal is stamped `System`. The refusal of machines runs in every build. A unit test turns the role check back on with `UserRoleChecks.Enforce()`; a request without an identity is then `401`.

A route checks in this order, and the first failure answers: a machine (`403`), `User.Impersonation` (`401`), the role (`403`), the query of the list (`400`), and then the service.

### The list

`GET …/inbox` answers one page of the items the caller's role sees, as a `ViewInboxResponse`.

| Query parameter | Values | Default |
| --- | --- | --- |
| `state` | `Open`, or `Done`: the accepted and the dismissed items | `Open` |
| `continuationToken` | The `ContinuationToken` of the page before | the first page |

- **`state`** is read as the enums of the key/value queries are (`KeyValueQuery.GetEnum<ViewInboxFilter>`): a name in any casing (`open`, `DONE`), or the number of a value (`0` for `Open`, `1` for `Done`). Empty means `Open`. Anything else answers `400 InvalidQuery` with "The query parameter 'state' must be Open, Done." and reads no inbox: a state of an item (`Accepted`, `Dismissed`), `All`, two names (`Open,Done`), a number without a value (`2`). The OpenAPI document publishes the parameter as a plain string.
- **`continuationToken`** goes to the service as it came. A token this inbox did not issue answers `400 InvalidQuery` with "The continuation token is not one of the inbox; start again without it." (see [Listing](#listing)).
- **The role comes first.** It is checked before the query is read, so a Reader or a machine with a wrong `state` gets `403`, not `400`.
- **The page** is newest first and holds at most 50 items. `Count` is the number of its items, and `OpenCount` every open item the caller sees, whatever the page and the `state`. `ContinuationToken` is there only when a page follows; send it back with the same `state`.
- **The token of the invocation** goes to the service, so a caller that goes away stops the read.

A page of an Administrator, with the end of an export and of an import (members that are null are left out):

```json
{
  "Items": [
    {
      "Id": "20261122093012000000061c5e0d2a7b94f3e8a1d0c6b5f4e3d2c",
      "Kind": "Notification",
      "Topic": "ViewImport",
      "Level": "Warning",
      "Title": "Import finished with failures",
      "Text": "acme.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 1 failed",
      "MinimalRole": "Administrator",
      "State": "Open",
      "CreatedAt": "2026-11-22T09:30:12+00:00",
      "CreatedBy": { "Kind": "Account", "Id": "user_01", "Name": "Ada Lovelace" },
      "ExpiresAt": "2026-11-23T09:30:12+00:00",
      "Job": {
        "Kind": "ViewImport",
        "Id": "9b1d4c7a2e5f4083a6c9d2e5f8b1a4c7",
        "FileName": "acme.zip"
      }
    },
    {
      "Id": "20261122091407000000079aab47ca406989d11506daca106041b",
      "Kind": "Notification",
      "Topic": "ViewExport",
      "Level": "Success",
      "Title": "Export finished",
      "Text": "12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB",
      "MinimalRole": "Administrator",
      "State": "Open",
      "CreatedAt": "2026-11-22T09:14:07+00:00",
      "CreatedBy": { "Kind": "Account", "Id": "user_01", "Name": "Ada Lovelace" },
      "ExpiresAt": "2026-11-23T09:14:07+00:00",
      "Job": {
        "Kind": "ViewExport",
        "Id": "5f0c2d7e9a414b6c8e3d1f2a4b6c8d0e",
        "FileName": "export-acme-billing-default-20261122-091358Z.zip"
      }
    }
  ],
  "Count": 2,
  "OpenCount": 2
}
```

This page is the last one, so it has no `ContinuationToken`. A page that another one follows carries the token of its last item: `MToyMDI2MTEyMjA5MTQwNzAwMDAwMDA3OWFhYjQ3Y2E0MDY5ODlkMTE1MDZkYWNhMTA2MDQxYg` for a page that ends with the export above.

### Accept and dismiss

`POST …/inbox/{id}/accept` and `POST …/inbox/{id}/dismiss` take the id of an item of the list and answer the item as the call left it.

- **No request body.** The routes read none: whatever a client sends is not read, so it cannot fail the request.
- **The actor** is the audit actor of the request (`request.GetAuditActor()`): the account with its display name, and `Via` when an actor acts for the person. It becomes the stamp `Closed` of the item.
- **Dismiss** closes an open item of any kind and starts nothing (see [Dismiss](#dismiss)).
- **Accept** takes an open `Action` only and runs its handler (see [Accept](#accept)). The answer carries the `Job` the handler started, when there is one.
- **The token of the invocation** goes to the service. A caller that goes away stops a dismissal, and an acceptance up to its claim; from the claim on the work runs to its end.

| Status | `ErrorCode` | Route | When |
| --- | --- | --- | --- |
| `200` | | both | The item, `Accepted` or `Dismissed`, with its stamp `Closed` |
| `401` | | both | Missing or invalid credentials, or a token that does not act for a user |
| `403` | `AccessDenied` | both | A machine, or a user whose role on the project is below `Contributor` |
| `404` | `NotFound` | both | The item does not exist in this view, has an id of another shape, has expired, or needs a higher role than the caller's: "The inbox item '{id}' does not exist or has expired." |
| `409` | `InboxItemNotAction` | accept | The item is a notification or a message: "The inbox item is not an action; only an action can be accepted." |
| `409` | `InboxItemClosed` | both | The item was already accepted or dismissed: "The inbox item was already accepted." or "The inbox item was already dismissed." |
| `409` | `Conflict` | both | The item kept changing while the call saved it: "The inbox item changed; try again." |
| `409` | the handler's own | accept | What the action starts could not start, such as `TransferLimit` of a job. The exception of the handler goes out as it is and is answered by its type, so another type is another status. The item is open again. |
| `501` | `InboxActionNotAvailable` | accept | No handler is registered for the action of the item: "The action '{code}' of the inbox item is not available in this deployment." The item stays open. |

The checks of the item come in the order of the service, and the first failure answers: visible (`404`), an action (`409 InboxItemNotAction`, accept only), open (`409 InboxItemClosed`), a handler (`501`, accept only).

**Today every acceptance of an action answers `501`**, because no handler is registered ([Action handlers](#action-handlers)). No producer pushes an action either: the items of a deployment are the notifications of exports and imports, whose acceptance answers `409 InboxItemNotAction`. Dismiss works for all of them.

The body of a refusal is an `ErrorResponse`:

```json
{
  "Message": "The inbox item was already dismissed.",
  "ErrorCode": "InboxItemClosed"
}
```

### Examples

```bash
API=https://api.example.com/api/v1/acme/billing
AUTH="Authorization: Bearer $TOKEN"

# the open items of the view default, then the accepted and dismissed ones
curl "$API/default/inbox" -H "$AUTH"
curl "$API/default/inbox?state=Done" -H "$AUTH"

# the next page: the ContinuationToken of the page before, with the same state
curl "$API/default/inbox?state=Done&continuationToken=$NEXT" -H "$AUTH"

# dismiss the notification of an export; accept an action
curl -X POST "$API/default/inbox/20261122091407000000079aab47ca406989d11506daca106041b/dismiss" -H "$AUTH"
curl -X POST "$API/default/inbox/2026112208000000000003f2b8c1d4e5f4a6b9c0d1e2f3a4b5c6d/accept" -H "$AUTH"
```

The `Job` of a notification names its job, so its archive or report is read with the routes of the [export and import](export-import.md#routes): `GET …/exports/{Job.Id}/archive` or `GET …/imports/{Job.Id}`, while the job lives.

### OpenAPI and SDKs

The document publishes the three operations with the tag `Inbox`, each without a request body:

- **`401`** is the description of the routes for users (`ResponseUnauthorizedUserOnly`), without a body.
- **`403`** has a description of its own (`ResponseForbiddenViewInbox`): "Only users may call this operation: a machine is refused (ErrorCode AccessDenied), and so is a user whose role on the organization or project does not allow it (ErrorCode AccessDenied). The inbox needs the Contributor role. An item above the caller's role is not found (404)."
- **`404`** of accept and dismiss is `ResponseNotFoundInboxItem`: "The item doesn't exist in this view, has expired, or needs a higher role than the caller's."
- **The schemas** are `ViewInboxResponse`, `ViewInboxItem` and `ViewInboxJob`. The enums are written inline, and `CreatedBy` and `Closed` reference the shared `AuditActor` and `AuditStamp`.
- **Every member is optional in the generated types.** The model has the C# keyword `required` and no `[JsonProperty(Required = …)]`, so no schema has a `required` list, as `AccessAuditEntry` and `ViewExport` have none. A client treats `Id`, `State` and `OpenCount` as possibly missing.

The tag gives each SDK a client of its own:

- The C# SDK has `IInboxApiClient` (`InboxApiClient`, the eighth typed client, registered by `AddStorytellerSdk`): `GetViewInboxAsync(organization, project, view, string state = null, string continuationToken = null)`, `AcceptViewInboxItemAsync(organization, project, view, id)` and `DismissViewInboxItemAsync(organization, project, view, id)`. `state` is a string, as in the document.
- The TypeScript SDK (`@42for.net/2splatform.sdk`) has the `inbox` tag: `getViewInbox(organization, project, view, { state, continuationToken })`, `acceptViewInboxItem` and `dismissViewInboxItem`, with their Vue Query composables (`useGetViewInbox` with `getGetViewInboxQueryKey`, `useAcceptViewInboxItem` and `useDismissViewInboxItem`), Zod schemas and MSW handlers (`getInboxMock()`, part of `getStorytellerMock()` in the `./msw` entry).

`sform` has no command for the inbox, and the [MCP server](mcp.md#limits) no tool.

## Notifications of exports and imports

An [export or an import](export-import.md#jobs) that ends pushes one notification into the inbox of its view. `ViewTransferNotifications.Create` builds the push from the stored job:

| Job | Level | Title | Title of a dry run | Text | Expires |
| --- | --- | --- | --- | --- | --- |
| Export succeeded | `Success` | `Export finished` | | the counts and the size | with the job |
| Export failed | `Error` | `Export failed` | | the job's `Error` | after the inbox's retention |
| Import succeeded, no file failed | `Success` | `Import finished` | `Check finished` | the file and the counts | with the job |
| Import succeeded, files failed | `Warning` | `Import finished with failures` | `Check found problems` | the file and the counts | with the job |
| Import failed | `Error` | `Import failed` | `Check failed` | the job's `Error` | after the inbox's retention |

Every one of them has `Kind` `Notification`, the `Topic` `ViewExport` or `ViewImport`, and:

- **`MinimalRole` `Administrator`**: the role of the job routes. Who may not read a job does not see its end.
- **`CreatedBy`**: the job's `CreatedBy`, so an account (with `Via` through an actor) or a machine.
- **`Job`**: the kind, the id and the file name of the job.
- **`Key`** `ViewExport.{id}` or `ViewImport.{id}` and **`CreatedAt`** the job's `CompletedAt`. Every writer of an end sets `CompletedAt`; a record without it is still reported, at its `StartedAt` or else its `CreatedAt`.

**The text of an export** is the counts of the exported kinds, then the versions, then the size: `12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB`.

- A kind that was left out has no count and is not named. A kind that was exported and found nothing reads `0 templates`. One document is singular: `1 annotation`.
- **Versions** are named only when schemas, templates or configurations were exported. The job always counts versions, so an export of annotations alone would read `0 versions`; it reads `12 annotations, 512 B`.
- **The size** is in bytes below 1024 (`512 B`), and above it in `KB`, `MB` or `GB` with one decimal, in steps of 1024. The value is rounded before the unit is chosen, so 1,048,575 bytes read `1.0 MB`, never `1024.0 KB`. `GB` is the largest unit.
- An export without counts and without a size has no text.

**The text of an import** is the file name and the five counts of the job's `Summary`, all five always: `acme.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed`.

- **A dry run** wrote nothing, so it reads `acme.zip: 3 to create, 2 to update, 10 unchanged, 1 skipped, 0 failed`, as the administration UI words a check.
- Without a file name the text is only the counts. Without a `Summary` it is only the file name, or nothing.

Every number is written with the invariant culture and without a thousands separator. The text of a failed job is its `Error`, which the job cut at 500 characters.

**Lifetime.**

- **A result lives as long as its job.** Its `ExpiresAt` is the job's (`ConfigurationTransfers:RetentionHours` after the end, 24 hours by default): when the job and its archive or report are gone, so is the item that leads to them.
- **A failure lives as long as any other item**: it names no `ExpiresAt`, so it gets `ViewInbox:RetentionDays` from its first push, 30 days by default. Its whole content is the error text. It outlives its job, so its `Job` then names a job that no longer exists.

**Where an end is pushed.** `ViewTransferService.NotifyAsync` is called in five places:

1. in `ExportAsync`, after the save of `Succeeded`;
2. in `ImportAsync`, after the save of `Succeeded` and before the delete of the upload;
3. in `FailAsync`, when its replace saved `Failed`: an exception of the job, the poison queue, and a job that could not be queued, which fails inside the start request;
4. in `FailAsync`, when the job it reads has already ended: the poison queue after the last delivery, and an end whose save reached the store while its answer was lost;
5. in `ProcessAsync`, for a message of a job that is already `Succeeded` or `Failed`: a message delivered again after a crash between the save and the push.

A worker that lost the job to another worker, a failure that could not be saved and a job that no longer exists push nothing. A job in progress has no item: the inbox shows a job when it ends.

**One item per job.** The key and the time of the end are the same in every call, so a call for a job that was already reported (places 4 and 5) meets the stored item and writes nothing. A notification that somebody dismissed stays dismissed.

**The push never fails the job.** `NotifyAsync` builds the push and sends it inside one `try`, and logs a failure as a warning ("The end of the … job … was not pushed to the inbox."). A broken inbox costs a notification, never an export or an import, and never the archive of an export. The push does not listen to the token of the invocation: a host that shuts down still completes the invocation, and nothing would push again. Nothing retries a failed push by itself; only a later message of the same job (a redelivery, the poison queue) pushes again.

`ViewTransferNotifications.Create` throws `ArgumentException` for a job that has not ended and for a record without the model of its kind. `NotifyAsync` catches it like any other failure.

**Without the inbox.** The publisher is the last, optional constructor parameter of `ViewTransferService` (`IViewInboxPublisher? inbox = null`). A host that does not call `AddViewInbox` gets no publisher, and its jobs report their ends nowhere.

## Storage

An item is a document of the container `organizations`, in the logical partition (organization, `{project}.inbox.{view}`): `PartitionKeys.GetInbox(project, view)` and `GetCosmosInbox(organization, project, view)`. One view's inbox is one logical partition, so the list and the count name the full key and need no predicate on the organization.

`ViewInboxEntity`, as stored:

```json
{
  "id": "inb.20261122091407000000079aab47ca406989d11506daca106041b",
  "PartitionKey": "billing.inbox.default",
  "Organization": "acme",
  "Project": "billing",
  "View": "default",
  "ItemId": "20261122091407000000079aab47ca406989d11506daca106041b",
  "Kind": "Notification",
  "Topic": "ViewExport",
  "Level": "Success",
  "Title": "Export finished",
  "Text": "12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB",
  "MinimalRole": "Administrator",
  "State": "Open",
  "CreatedAt": "2026-11-22T09:14:07+00:00",
  "CreatedBy": { "Kind": "Account", "Id": "user_01", "Name": "Ada Lovelace" },
  "ExpiresAt": "2026-11-23T09:14:07+00:00",
  "Job": {
    "Kind": "ViewExport",
    "Id": "5f0c2d7e9a414b6c8e3d1f2a4b6c8d0e",
    "FileName": "export-acme-billing-default-20261122-091358Z.zip"
  },
  "ttl": 86400
}
```

- **The id** is `inb.` and the id of the item (`ViewInboxEntity.IdPrefix`), so the order of the stored ids is the time order too.
- **The members of the item are flat** on the document. The minimal role and the state are stored as their names, which the queries compare.
- **No `ViewName` and no `ProjectName`.** View discovery lists every `ViewName` it finds in the partitions of a project (`CosmosViewService`), and a job of an export or import carries one. An inbox item must not make an empty view a discovered one, so its properties are `Project` and `View`. It has no `AnnotationType` either, so the annotation queries never read it.

`CosmosViewInboxStore` implements `IViewInboxStore`:

| Method | On Cosmos DB | Answer |
| --- | --- | --- |
| `TryCreateAsync` | `CreateItemAsync` | The record with its ETag; null on `409`, when the id is taken |
| `GetAsync` | A point read | The record with its ETag; null when it is missing, or when its `Project` or `View` is not the one asked for |
| `TryReplaceAsync` | `ReplaceItemAsync` with `IfMatchEtag` | The record with its new ETag; null on `412` and `404`. A record without an ETag is refused with `ArgumentException`: it would overwrite whatever another caller saved. |
| `QueryAsync` | The list query | At most `take` records, newest first, without ETags |
| `CountOpenAsync` | The count query | The number of open items of the roles |

The list, with the filter and the continuation:

```sql
SELECT TOP @take * FROM c
WHERE c.PartitionKey = @partition AND STARTSWITH(c.id, @prefix)
  AND c.Project = @project AND c.View = @view
  AND ARRAY_CONTAINS(@roles, c.MinimalRole)
  AND c.State = 'Open'        -- Done: c.State != 'Open'
  AND c.id < @before          -- only for a next page: 'inb.' and the id of the token
ORDER BY c.id DESC
```

The count:

```sql
SELECT VALUE COUNT(1) FROM c
WHERE c.PartitionKey = @partition AND STARTSWITH(c.id, @prefix)
  AND c.Project = @project AND c.View = @view
  AND ARRAY_CONTAINS(@roles, c.MinimalRole)
  AND c.State = 'Open'
```

- **`c.Project` and `c.View`.** The partition value already holds both names. A name with a dot, older than the rules of names, can still make two pairs of a project and a view share one partition: the project `a.inbox.b` with the view `c`, and the project `a` with the view `b.inbox.c`. The point read, the list and the count compare the stored names, so neither pair sees the items of the other.
- **The page.** The list asks for `take` items (`TOP` and the page size of the query), reads on until it has them, because a page of the store may hold fewer, and cuts the result at `take`.
- **Indexes.** The queries filter on `PartitionKey`, `id`, `Project`, `View`, `MinimalRole` and `State` and sort on the id. None of these paths, and no other path of the entity, is excluded from the index of the container, and one `ORDER BY` on the id needs no composite index. The settings of the container did not change.
- **The cost of the count** grows with the items of the inbox: it is a query on every list call, and its request charge was not measured.

**The `ttl`** of a write is `Math.Max(1, (int)Math.Ceiling(timeToLive.TotalSeconds))`: rounded up, so an item never expires before its time, and at least a second, because Cosmos DB refuses a `ttl` of zero. Cosmos DB counts a `ttl` from the last write of the document. So every write of an item (the create, a dismiss, a claim, a reopening, the save of a job) stores the time the item still has, and no write moves the expiry. This differs from a job, whose every write restarts its retention. The container has TTL on without a default expiry (`DefaultTimeToLive = -1`), so only documents with a `ttl` expire ([annotation-lifecycle.md](annotation-lifecycle.md#containers)).

**Expiry.** Accept and dismiss treat an item whose `ExpiresAt` is not after now as missing, so their answer does not depend on when the store removes it. The list and the count leave it to the store.

**Nothing cascades.** No code deletes an inbox item, and an item and the job it names are not linked in storage. The notification of a result gets the job's `ExpiresAt`, so the two expire together, each by its own `ttl`. The back end has no operation that deletes a view, a project or an organization, so there is nothing that would have to remove their items.

## Configuration

Section `ViewInbox` (`ViewInboxOptions`):

| Setting | Default | Meaning |
| --- | --- | --- |
| `ViewInbox:RetentionDays` | `30` | Days an item lives when its producer names no `ExpiresAt`; between 1 and 365 |

The range is checked when the setting is first read, and the publisher reads it in the push that needs it, not when it is built and not at the start of the host. A value outside the range fails every push that names no `ExpiresAt` with "ViewInbox:RetentionDays must be between 1 and 365."; a push with its own expiry, such as the result of a job, still works. The service of exports and imports takes the publisher and is built all the same: a wrong setting costs the notifications of failed jobs (each logged as a warning), never an export. The limits of one minute and 365 days of an item's life are constants of the publisher.

## Registration

- **`AddViewInbox(configuration)`** (`ViewInboxEntryPoint`, `Backend.Core`) registers the options with their validation, `TimeProvider.System`, `IViewInboxPublisher` and `IViewInboxService`, each as a singleton and only when nothing else registered it (`TryAddSingleton`). `Api.Functions/src/Program.cs` calls it after `AddViewTransfers`. It is the only host that does.
- **`AddCosmosDbAnnotations`** (`Backend.CosmosDb/src/EntryPoint.cs`) registers `CosmosViewInboxStore` as `IViewInboxStore`, a singleton. It needs only the repository provider and the serializer settings, so every host of the Cosmos DB services has the store: the API, DbCreator and the test fixtures.
- **Handlers** are added by the host: `services.AddSingleton<IViewInboxActionHandler, …>()`.

## Tests

| Project | Class | Covers |
| --- | --- | --- |
| `Backend.Core/test/Notifying/` | `ViewInboxIdsTests` | The shape of an id, the order, a time with another offset, the culture of the thread, `ForKey`, `IsValid` |
| | `ViewInboxPublisherTests` | The validation, the cuts, the default and the moved expiry, the time to live of a write, pushes with and without a key, a closed item returned as it is, a conflict whose read finds nothing, the token of the caller |
| | `ViewInboxServiceTests` | Visibility per role and "not found" instead of a conflict, paging and tokens, the filters, `OpenCount`, dismiss and its conflicts, an expired item, accept (the claim, the job, no handler, another kind, a failing handler, a token cancelled before and after the claim), the outcomes of a lost replace, two handlers of one code |
| | `ViewInboxRegistrationTests` | A container with `AddViewTransfers`, `AddViewInbox` and fakes, validated on build: a handler that takes `IViewTransferService` starts an export whose end comes back as a notification; without `AddViewInbox` the transfers push nothing; two handlers of one action stop the inbox service and not the transfers |
| `Backend.Core/test/Transferring/` | `ViewTransferNotificationsTests` | Every row of the table, the texts (kinds, singular and plural, versions, sizes, a dry run, the invariant culture), the keys, an end without its time, the refusals, and one item per job with its lifetime through the real publisher |
| | `ViewTransferServiceTests` | Each end pushes once; a job that cannot be queued; a message delivered again and the poison queue of an ended job give the same item; an end whose push or whose answer was lost, where an export keeps its archive; nothing for a job another worker took, a failure that was not saved and a missing job; a throwing inbox neither fails a job nor deletes an archive; a cancelled invocation still pushes; no inbox |
| `Backend.CosmosDb/test/` | `CosmosViewInboxStoreTests` | The create and its conflict, the point read through the view only, two pairs in one partition, the replace with the ETag, the list per filter and role in id order, a next page, the count, the stored `ttl` of every write read from the raw document, view discovery, and a list of 52 items through the service with its token |
| | `ViewInboxIsolationTests` | Two organizations with the same project, view and ids, in the store and through a push with a key |
| | `EntityOrganizationTests` | `ViewInboxEntity` among the stored entities that carry `Organization` |
| `Api.Functions/test/` | `ViewInboxHttpTests` | Each route with a recording service: the role handed over for the four roles, `None` and `Reader` refused before the service is asked, a machine refused in every build, a token that does not act for a user, the caller as `Owner` without a lookup where the role check is skipped, a request without an identity, `state` (the default, the casing, the `400`), the continuation token, the actor with `Via`, a delegated user, the token of the invocation, no request body, no step-up, the refusals of the service with their statuses, the routes and the tag |
| | `HttpRequestDataExtensionsTests` | `GetUserRoleInProjectAsync`: the caller's own role at or above the minimal one, `403` below it, `Owner` where the role check is skipped, a machine refused |
| | `OpenApiExportTests` | `Document_HasTheViewInboxOperations_UnderTheInboxTag`: the three operations, their parameters, statuses and descriptions, no request body, the three schemas without a `required` list. `OperationAccess_MatchesTheTablesOfTheTests`: the three ids among the operations for users only |
| | `OpenApiDocumentTests` | `ViewInboxSchemas_ArePublished`: the members, enums, formats and references of the three schemas |
| | `RouteTableTests`, `RouteDispatchTests` | The three routes are registered and are the only ones with `/inbox`; a request matches exactly its function, also for a view named `inbox`; a request for one item, for another method or for another action matches none |
| | `FunctionTransactionNamesTests` | The transaction name of `AcceptViewInboxItem` is its route template |

The tests of `Backend.Core` use fakes (`InMemoryViewInboxStore`, `RecordingActionHandler`, `RecordingViewInboxPublisher`) and need nothing else:

```bash
dotnet test --project src/Platform/Storyteller/Backend.Core/test --filter-class "*ViewInbox*"
dotnet test --project src/Platform/Storyteller/Backend.Core/test --filter-class "*ViewTransfer*"
```

The tests of the routes need nothing else either. Run them in Debug and in Release: a Debug build (`DEV_AUTH`) skips the checks of users, so the `401` of a token that does not act for a user is tested in Release only, and the caller as `Owner` without a lookup, also for a request without an identity, in Debug only. The tests of the roles turn the role check on with `UserRoleChecks.Enforce()` and run the same in both builds.

```bash
dotnet test --project src/Platform/Storyteller/Api.Functions/test --filter-class "*ViewInboxHttpTests"
dotnet test --project src/Platform/Storyteller/Api.Functions/test -c Release --filter-class "*ViewInboxHttpTests"
dotnet test --project src/Platform/Storyteller/Api.Functions/test
dotnet test --project src/Platform/Storyteller/Api.Functions/test -c Release
```

The store tests need a Cosmos DB emulator. Without `STORYTELLER_TESTS_USE_CONTAINER=true` the suite uses the local Windows emulator and deletes its database `42.Platform.2S`:

```bash
STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*ViewInbox*"
STORYTELLER_TESTS_USE_CONTAINER=true STORYTELLER_TESTS_EMULATOR=classic dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*ViewInbox*"
```

- The inbox tests carry no trait, so a pull request runs them on the vNext emulator, which has no range indexes. They run on the classic emulator, with the indexes of the service, only by hand: locally, or in the workflow `Platform.Storyteller.CosmosDb` started by hand, which runs the whole suites on it.
- No test waits for an expiry: the vNext emulator expires no item within a test's wait. The tests read the stored `ttl` instead.

## Not there yet

- **The page of the administration UI** and its mock API come with phase 3 of 424-31. Until then nothing of the platform calls the routes: the SDKs have the clients, and the administration UI still learns the end of a job by polling it.
- **The first suggested action**, its handler and what an action carries for its handler come with 424-8. Until then no item is an action, and the acceptance of one answers `501`.
