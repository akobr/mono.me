# Inbox of a View

Linear: [424-31 Prepare notification center (inbox) in Admin UI](https://linear.app/424fun/issue/424-31/prepare-notification-center-inbox-in-admin-ui).

Line numbers refer to the tree this plan was written against (`20ef5b5a2`). No application code is changed by this document. Paths are under `src/Platform/Storyteller/` unless they start with `src/` or `docs/`; `UI/` stands for `src/Platform/ui.admin/src/`. The issue has no comments and no CodeRabbit plan.

The work is delivered in three phases, each with its own review:

1. **Backend**: the model, the publisher and the service, the Cosmos DB store, and the ends of exports and imports as the first notifications.
2. **API and SDKs**: three routes for users, the OpenAPI snapshot, the C# and TypeScript SDKs.
3. **Administration UI**: the page **Inbox** of the view section, the mark of open items in the menu, the mock API, tests and documentation.

## Problem

Work that the platform does in the background has no place where its results stay. The only background jobs today, the export and the import of a view (424-15), are known to one browser tab:

- The tab that started a job polls it by id and keeps the id in `sessionStorage`. Another tab, another device or another Administrator never sees the job, and a job started by a machine (CI) is invisible in the UI.
- The archive of a finished export is downloaded once, by the tab that waited for it. After a reload, or when the tab was closed before the job ended, the archive lies on the server for a day and nothing in the UI leads to it.
- The API has no list of jobs. The first transfer spec left that to this issue: "Listing past exports and imports; the notification center of the Administration UI (424-31) can build on the job endpoints." (`docs/Platform/Storyteller/specs/2026-10-14 Export and Import of Configurations.md:588`).

The platform also has no way to ask people for a decision. 424-8 will detect configurations that look alike and offer to generate a schema for them; the offer needs a place where a Contributor or an Administrator sees it, accepts it, and later finds the report of the job the acceptance started. Nothing like that exists: no stored item, no route, no page.

The issue asks for that place: an **inbox per view** with the results of background jobs (notifications), the actions the back end suggests (to accept or dismiss, some of them only for Administrators) and plain messages, on a new page **Inbox** of the view section with the icon `inbox`.

## Current State

### Background jobs: export and import of a view

- **Lifecycle.** `Backend.Core/src/Transferring/ViewTransferService.cs` creates a job in the request (`StartExportAsync` 64-101, `StartImportAsync` 134-191), a queue function runs it (`ProcessAsync` 199-250, `Api.Functions/src/Transfers/ViewTransferFunctions.cs:21-34`), and the poison queue fails it (`AbandonAsync` 252-265, after three deliveries: `Api.Functions/src/host.json:15`). The statuses are `Queued`, `Running`, `Succeeded`, `Failed` (`Abstractions.Annotations/src/Model/ViewTransfer.cs:19-25`).
- **The three places where a job ends**, all private code of the service:
  - an export succeeds in `ExportAsync` (308-330): `TryReplaceAsync` with `Status = Succeeded`; `saved is null` means another worker owns the job;
  - an import succeeds in `ImportAsync` (397-408): `SaveImportAsync(current, completed, …)`, null for a lost job;
  - both fail in `FailAsync` (424-448), the only writer of `Failed`, called by the `catch` of `ProcessAsync` (240-249), by `AbandonAsync` and by `EnqueueAsync` (456-461, inside the request, with `CancellationToken.None`). It returns false for a job that already ended (430-433).
- Every writer of an end sets `CompletedAt`, and nothing writes a job after its end.
- **A redelivered message** of a job that already ended returns at once (`ProcessAsync` 209-213).
- **The `catch` of `ProcessAsync` deletes the archive of an export** whatever `FailAsync` answered (243-248). Anything that throws after the export's last save would leave a succeeded job without its archive.
- **The job document** lives in the partition `{project}.transfer` with the id `{view}.exp.{id}` or `{view}.imp.{id}` and a `ttl` of the retention, restarted by every write (`Backend.CosmosDb/src/Transferring/CosmosViewTransferStore.cs:29-32, 141`; `ViewTransferOptions.RetentionHours` 24, 1 to 168). `ExpiresAt` on the job says the same. The archive is purged an hour later (`ViewTransferService.cs:22-23, 267-271`); the archive of a failed export is deleted at once.
- **The store** has `CreateAsync`, `GetAsync`, `TryReplaceAsync` and `CountActiveAsync` (`Backend.Core/src/Transferring/IViewTransferStore.cs`): a job is reachable only by its id.
- **The constructor** of `ViewTransferService` is kept stable on purpose (comment at line 60); it is the only one, registered by `TryAddSingleton` (`ViewTransferEntryPoint.cs:29`) and called by hand in `Backend.Core/test/Transferring/ViewTransferServiceTests.cs:34`, `Backend.CosmosDb/test/CosmosViewTransferTests.cs:852` and `Backend.CosmosDb/test/TransferIsolationTests.cs:446`.
- **Access.** The five routes need the Administrator role, reads included (424-66, `Api.Functions/src/V1/ViewTransferHttp.cs:69-71`); a machine needs `Configuration.Transfer` and the scopes of the kinds. `CreatedBy` of a job is `request.GetAuditActor()`, so it may be a machine: a pipeline that exports every night is a documented use (`docs/Platform/Storyteller/export-import.md:579`).

### Access

- Roles are granted on an organization or a project, never on a view (`docs/Platform/Storyteller/access.md`). `AccountRole` is `None < Reader < Contributor < ContributorWithSecrets < Administrator < Owner` (`Abstractions.Access/src/AccountRole.cs`).
- Every helper of `Api.Functions/src/Security/HttpRequestDataExtensions.cs` only asserts a minimal role (`CheckUserAccessToProjectAsync` 337-345, `EnsureRole` 298-304). None returns the caller's role; `IAccessService.GetAccountRoleAsync` is called directly at lines 193 and 598.
- Debug builds (`DEV_AUTH`) skip the role of users (`UserRoleChecks.AreSkipped`, line 590, read before the identity) and serve requests without an identity; a test turns the check on with `UserRoleChecks.Enforce()`. Machines have no role.
- A route for users only calls `request.EnsureUser(…)` and `CheckUserAccessTo…Async`, and its operation id is listed in `Api.Functions/src/OpenApi/OperationAccess.cs` (`UserOnly`, 10-65) and in the tables of `Api.Functions/test/OpenApiExportTests.cs` (`UserOnlyOperations`, 32-86). A new machine scope would touch about 67 files, the identity providers and the CLI coverage check.

### The API and the SDKs

- An endpoint class is `Api.Functions/src/V1/<Name>Http.cs` with routes, operation ids, tags and descriptions in `Api.Functions/src/Definitions.cs` (the transfers: routes 171-182, ids 392-399, tag 541). The nearest precedent for a paged list for users is `V1/AccessAuditHttp.cs`: `Items`, `ContinuationToken`, `Count` (`Abstractions.Annotations/src/Model/AccessAudit.cs:111-118`), keyset paging on an id whose order is the time order (`Backend.CosmosDb/src/Accessing/CosmosAccessAuditLog.cs:79-171`).
- The C# keyword `required` does not reach the OpenAPI document: the members of `AccessAuditEntry`, `ViewExport` and `ViewImport` are optional in both SDKs.
- The host tries routes in the order of the function names and does not rank a literal above a parameter; `RouteDispatchTests` and `RouteTableTests` check every route. `inbox` after `{view}` meets no existing route of five, six or seven segments.
- The tag of an operation decides its SDK client: seven tags give seven clients (`Sdk.NSwag/src/ApiSdk.partial.cs`, `Sdk.NSwag/src/ServicesCollectionExtensions.cs:31-37`, `src/Platform/Cli/test/Sdk/SdkHttpClientConfigurationTests.cs:12-29`) and seven folders of `sdk.typescript/src/generated/vue-query` (`sdk.typescript/src/msw.ts:3-28`, checked by `sdk.typescript/src/test/msw.test.ts:109-125`).
- The OpenAPI document is exported by `OpenApiExportTests` with `STORYTELLER_OPENAPI_EXPORT`; its name carries the version of the API assembly (`open.api.v0.10.56.json` today, in `Sdk.NSwag/` and `sdk.typescript/`).

### Storage

- One container `organizations` with the key (`/Organization`, `/PartitionKey`) and `DefaultTimeToLive = -1`: only items with a `ttl` expire (`Backend.CosmosDb/src/ContainerFactory.cs`). The partitions of a project start with `{project}.` (`Backend.CosmosDb/src/PartitionKeys.cs`): `{project}.kv.{view}` for the pairs of a view, `{project}.transfer` for the jobs.
- A property `ViewName` in such a partition makes its view a discovered view (`Backend.CosmosDb/src/Annotating/CosmosViewService.cs:146-149`); `ViewEntity` avoids the name for that reason.
- The index leaves out `/Created/*`, `/Modified/*` and `/Actor/*` (`ContainerFactory.cs:21-28`); sorting or filtering on them fails on the classic emulator and in Azure. The vNext emulator, the default of the suite and of a pull request, has no such indexes and expires no item within a test's wait (`Backend.CosmosDb/test/CosmosTestEmulator.cs:18-21`).
- `Backend.CosmosDb/test/EntityOrganizationTests.cs` requires `Organization` on every stored entity, and `OrganizationQueryGuard` requires a full partition key or the predicate `c.Organization = @organization` on every query.

### The administration UI

- **Menu.** `UI/app/utils/navigation.ts:97-125` builds the view section: Annotations, Configurations, Values store, Graph, Modeling. An entry has `minRole` (hidden below it) or `editRole` with `lockedHint` (shown with a lock: Modeling for a Reader). `buildNavigation` is a pure function of the context, used by the sidebar and the command palette. `UI/app/components/shell/AppSidebar.vue:33-40` maps the entries; the lock is the trailing badge. `UI/app/utils/context-paths.ts:9` lists the sections a view or project switch keeps. No entry has a count or a mark.
- **Transfers.** `UI/app/stores/transfers.ts` follows the jobs of the tab (delays of 1 to 3 seconds, 15 minutes at most), downloads the archive of an export by itself, and `UI/app/components/transfer/TransferToaster.vue` (mounted in the layout) reports the ends. An import that the page shows gets no toast (`showInPage`): the UI is built not to report an end twice. The page is the tab Export & Import of Modeling, for Administrators (`UI/app/pages/[org]/[project]/[view]/modeling/export-import.vue`).
- **Queries.** An error of a query goes to the global toast and to Sentry unless the query has `meta: { silent: true }` (`UI/app/boot/query.ts:19-25`); the queries of the shell are silent. Writes are plain SDK calls in a `try`, not mutations. No query polls (`refetchInterval`: not found), and window focus does not refetch.
- **Lists.** A first page by a query and **Load more** with the continuation token (`UI/app/components/access/AccessAuditTrail.vue:51-97, 233-235`); a changed first page starts the list again.
- **Mock API.** Hand-written MSW handlers per area (`UI/app/mock/all-handlers.ts:12-24`) over a JSON database in `sessionStorage` (`UI/app/mock/db.ts:50-90`); the jobs of the mock live in page memory (`UI/app/mock/transfer-handlers.ts:97`). The mock user has one role per project of the data set: Owner in `acme.billing`, Contributor in `acme.shipping`, Reader in `northwind.portal`.
- **Tests that name the view entries**: `UI/test/unit/utils/navigation.test.ts:14-22`, `UI/test/e2e/navigation.spec.ts:243-249`, the desktop visual baselines, and `src/Platform/UI.Admin.AutomationTests/src/Tests/Navigation/NavigationMenuTests.cs:102, 145` (cases 24 and 27, run only with a target).
- **Deployment.** The admin UI deploys by itself on a push to `main` (`.github/workflows/Platform.UI.Admin.yml:130-135`); the API is published by hand.

### What does not exist

- 424-8 (the schema suggestion) is in the backlog: there is no detection, no AI abstraction and no job to start.
- A channel that pushes to the browser (SignalR, server-sent events): not found.

## Proposed Changes

### 1. The model

`Abstractions.Access/src/Model/ViewInbox.cs`, namespace `_42.Platform.Storyteller` (`Abstractions.Access` has `AccountRole` and references `Abstractions.Annotations` with `AuditActor`). The types are named `ViewInbox…`, as the jobs are `ViewTransfer…`; the billing plan of 2026-10-01 uses "inbox" for a webhook inbox, which this is not.

```csharp
[JsonConverter(typeof(StringEnumConverter))]
public enum ViewInboxItemKind
{
    // The result of work that ran in the background.
    Notification = 0,

    // Something the platform suggests; a person accepts or dismisses it.
    Action,

    // A plain text of the platform.
    Message,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewInboxItemState
{
    Open = 0,
    Accepted,
    Dismissed,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewInboxItemLevel
{
    Info = 0,
    Success,
    Warning,
    Error,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewInboxJobKind
{
    ViewExport = 0,
    ViewImport,
}

// The background job an item reports, or the one its acceptance started.
public record class ViewInboxJob
{
    public required ViewInboxJobKind Kind { get; init; }

    public required string Id { get; init; }

    // The archive of an export, or the uploaded file of an import, for display and the download.
    public string? FileName { get; init; }
}

public record class ViewInboxItem
{
    public required string Id { get; init; }

    public required ViewInboxItemKind Kind { get; init; }

    // The stable code of what the item is about (ViewExport, ViewImport); clients may branch on it.
    public required string Topic { get; init; }

    public ViewInboxItemLevel Level { get; init; }

    public required string Title { get; init; }

    public string? Text { get; init; }

    // The lowest role of the project that sees the item and may accept or dismiss it.
    public required AccountRole MinimalRole { get; init; }

    public required ViewInboxItemState State { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    // Who caused the item: the actor that started the job, or System.
    public required AuditActor CreatedBy { get; init; }

    // The item is removed after this time.
    public required DateTimeOffset ExpiresAt { get; init; }

    // Who accepted or dismissed it, and when; null while open.
    public AuditStamp? Closed { get; init; }

    // Of an Action: the code of what the acceptance runs.
    public string? Action { get; init; }

    public ViewInboxJob? Job { get; init; }
}

public record class ViewInboxResponse
{
    public required IReadOnlyList<ViewInboxItem> Items { get; init; }

    public string? ContinuationToken { get; init; }

    public int Count { get; init; }

    // Every open item the caller sees, whatever the page and the filter.
    public int OpenCount { get; init; }
}
```

- **One shared state.** An item is open for everybody who sees it and closed for everybody once one of them accepts or dismisses it. There is no read mark per person.
- **The model stays flat.** A producer that needs more facts later adds an optional member; no schema refers to itself (`OpenApiDocumentTests`).
- **Title and Text are written by the producer**, in English as the API's messages are, so a new producer needs no change of a client. `Topic`, `Level`, `Action` and `Job` are what a client branches on.
- **Every member is optional in the generated types**, as in `AccessAuditEntry` and `ViewExport`: the model has `required` and no `[JsonProperty(Required = …)]`. The page treats `Id`, `State` and `OpenCount` as possibly undefined.

### 2. The publisher and the service (`Backend.Core/src/Notifying/`, namespace `_42.Platform.Storyteller.Notifying`)

Two interfaces, so that what pushes never depends on what an acceptance runs: a producer takes the publisher, a handler may take a producer, and the service takes the handlers.

```csharp
// For producers of the back end.
public interface IViewInboxPublisher
{
    // With a Key the push is idempotent: the same key and time give the same item.
    Task<ViewInboxItem> PushAsync(ViewInboxPush push, CancellationToken cancellationToken = default);
}

// For the routes.
public interface IViewInboxService
{
    Task<ViewInboxResponse> GetItemsAsync(string organization, string project, string view, ViewInboxQuery query, CancellationToken cancellationToken = default);

    Task<ViewInboxItem> AcceptAsync(string organization, string project, string view, string id, AccountRole role, AuditActor actor, CancellationToken cancellationToken = default);

    Task<ViewInboxItem> DismissAsync(string organization, string project, string view, string id, AccountRole role, AuditActor actor, CancellationToken cancellationToken = default);
}

public sealed record ViewInboxPush
{
    public required string Organization { get; init; }
    public required string Project { get; init; }
    public required string View { get; init; }
    public required ViewInboxItemKind Kind { get; init; }
    public required string Topic { get; init; }
    public ViewInboxItemLevel Level { get; init; }
    public required string Title { get; init; }
    public string? Text { get; init; }

    // No default: a producer that forgets it does not show its item to Contributors by accident.
    public required AccountRole MinimalRole { get; init; }

    public required AuditActor CreatedBy { get; init; }

    // Null: now plus the retention of the options.
    public DateTimeOffset? ExpiresAt { get; init; }

    // Of an Action: the code of its handler.
    public string? Action { get; init; }

    public ViewInboxJob? Job { get; init; }

    // Makes the push idempotent, with CreatedAt as the time of the event (required with a Key).
    public string? Key { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

public enum ViewInboxFilter { Open = 0, Done }

public sealed record ViewInboxQuery
{
    public const int PageSize = 50;

    public ViewInboxFilter Filter { get; init; }
    public required AccountRole Role { get; init; }
    public string? ContinuationToken { get; init; }
}
```

**Ids** (`ViewInboxIds`). An id is 53 characters, `{yyyyMMddHHmmssfffffff}{32 hex}`, so its order is the time order and it is a clean path segment. The time is `time.UtcDateTime` formatted with the invariant culture. `NewId(time)` ends with a new GUID (`N`); `ForKey(time, key)` ends with the first 32 characters of the lower-case hex of the SHA-256 over the UTF-8 bytes of the key. `IsValid` is `^[0-9]{21}[0-9a-f]{32}\z`; any other id is "not found" and never reaches the store.

**Push** (`ViewInboxPublisher`: the store, the options, the clock, a logger).

- A programming error throws `ArgumentException`: a blank organization, project, view or title; a `Topic` or an `Action` that is not `^[A-Za-z][A-Za-z0-9]{0,63}$`; an `Action` kind without an `Action` code, or another kind with one; a `MinimalRole` below `Contributor`; a `Key` without `CreatedAt`.
- `Title` is cut at 200 characters and `Text` at 2000, with `…`, as the jobs cut their messages.
- `ExpiresAt` is at most 365 days from now and at least one minute; a time outside is moved to the limit.
- With a `Key`, an item of the same id already stored is returned as it is (also when it was closed meanwhile); nothing is written twice. When the create conflicts and the read finds nothing (the stored item expired in between), the create is repeated once; a second conflict throws `InvalidOperationException`.

**Visibility.** A caller with the role R sees the items whose `MinimalRole` is between `Contributor` and R. The list, the count, accept and dismiss apply it **before anything else is looked at**: an item above the role is `NotFoundException` ("The inbox item '{id}' does not exist or has expired."), the same answer as an unknown id, whatever its kind and state.

**Expiry** is the `ttl` of Cosmos DB. Accept and dismiss also treat a record whose `ExpiresAt` is not after now as missing, so their answer does not depend on when the store removes it; the list and the count rely on the `ttl`.

**Listing.** Newest first, keyset paging on the id, 50 items a page. The token is `base64url("1:" + lastId)`, as the access audit's; a token that does not decode to a valid id is `InvalidInputException` with `InvalidQuery`. One item more than the page is read to know whether a page follows. `OpenCount` is a count query of its own.

**Dismiss.** Every kind, while `Open`: the state becomes `Dismissed` with `Closed = AuditStamp.Of(actor, now)`, guarded by the ETag. An item that is not open is `ConflictException` with the new code `InboxItemClosed` ("The inbox item was already accepted." or "… dismissed.").

**Accept.** Only an `Action`, while `Open`:

1. Another kind: `ConflictException`, `InboxItemNotAction`. Not open: `InboxItemClosed`.
2. No handler registered for the item's `Action`: `FeatureNotConfiguredException` (501), `InboxActionNotAvailable`; the item stays open.
3. The item is claimed first: `Accepted` with the stamp, guarded by the ETag, so two people cannot start the work twice.
4. **The claim is the point of no return.** The request's token is used up to and including the claim; the handler, the reopening after a failure and the save of the job run with `CancellationToken.None`, as `EnqueueAsync` fails its job, so a caller that goes away leaves no accepted item without its work.
5. The handler runs. When it throws, the item is put back to `Open` with `Closed` cleared (best effort, logged when it fails) and the exception goes on to the caller, so a `ConflictException` with `TransferLimit` of a job that could not start arrives as it is. A handler throws only when it started nothing.
6. A `Job` the handler returns is saved on the item (best effort).

**A lost replace** (of a dismiss or of the claim: the store answers null for a changed ETag and for a removed item) reads the item again: gone or expired is `NotFoundException`; accepted or dismissed is `InboxItemClosed` with the message of that state; open again (the other caller's handler failed) repeats the replace, three times at most, then `ConflictException` with `Conflict` ("The inbox item changed; try again.").

```csharp
// Runs what the acceptance of an action starts, typically a background job whose result arrives as a notification.
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

Handlers are registered as `IViewInboxActionHandler` services and injected into `ViewInboxService` (the store, the handlers, the clock, a logger); two with the same code fail its construction, which stops the inbox routes and nothing else, because no producer depends on the service. **This change registers no handler**: the first one comes with 424-8, together with what its action must carry.

**Error codes** added to `Backend.Core/src/ErrorCodes.cs`: `InboxItemClosed`, `InboxItemNotAction`, `InboxActionNotAvailable`.

**Options** (`ViewInboxOptions`, section `ViewInbox`): `RetentionDays` 30, validated to 1 through 365: how long an item lives when its producer names no `ExpiresAt`.

**Registration** (`ViewInboxEntryPoint.AddViewInbox(configuration)`): the options with their validation, `TryAddSingleton(TimeProvider.System)`, `TryAddSingleton<IViewInboxPublisher, ViewInboxPublisher>()`, `TryAddSingleton<IViewInboxService, ViewInboxService>()`. `Api.Functions/src/Program.cs` calls it next to `AddViewTransfers`.

**The store** (`IViewInboxStore`, record `ViewInboxRecord { Organization, Project, View, Item, ETag }`):

```csharp
public interface IViewInboxStore
{
    // Null when an item with the id exists already.
    Task<ViewInboxRecord?> TryCreateAsync(ViewInboxRecord record, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    Task<ViewInboxRecord?> GetAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default);

    // Null when the ETag no longer matches or the item is gone.
    Task<ViewInboxRecord?> TryReplaceAsync(ViewInboxRecord record, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    // Newest first: at most take items below the item id before, of the roles and the filter; without ETags.
    Task<IReadOnlyList<ViewInboxRecord>> QueryAsync(string organization, string project, string view, ViewInboxFilter filter, IReadOnlyCollection<AccountRole> roles, string? before, int take, CancellationToken cancellationToken = default);

    Task<int> CountOpenAsync(string organization, string project, string view, IReadOnlyCollection<AccountRole> roles, CancellationToken cancellationToken = default);
}
```

The publisher and the service own the clock: they give every write the time the item still has (`ExpiresAt - now`), so a later write does not move the expiry.

### 3. The ends of exports and imports

`ViewTransferService` gets one optional constructor parameter at the end, `IViewInboxPublisher? inbox = null`, so the three hand-written constructions and a host without the inbox stay as they are (the container gives the default to a parameter whose service is not registered). A private `NotifyAsync(ViewTransferRecord record)` pushes the notification of a job that ended. It **never throws**: it builds the push inside its `try`, pushes with `CancellationToken.None` (a host that shuts down would otherwise lose the push, since the invocation still completes), and logs a failure as a warning. It is called:

- in `ExportAsync` after the save of `Succeeded`, when `saved` is not null;
- in `ImportAsync` after the save of `Succeeded`, when it is not null;
- in `FailAsync`, when its replace saved `Failed` (the method keeps the saved record for it);
- in `FailAsync`, when the job it reads has already ended: the poison queue after the last delivery, and an end whose save succeeded while its answer was lost;
- in `ProcessAsync` for a job that is already `Succeeded` or `Failed`: a message redelivered after a crash between the save and the push.

The push has the key `ViewExport.{id}` or `ViewImport.{id}` and the job's `CompletedAt` as `CreatedAt`, so every call for one job gives the same item.

| Job | Level | Title | Text | Expires |
| --- | --- | --- | --- | --- |
| Export succeeded | `Success` | `Export finished` | `12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB`: the counts of the exported kinds, then the size | with the job |
| Export failed | `Error` | `Export failed` | the job's `Error` | after the inbox's retention |
| Import succeeded, no failed file | `Success` | `Import finished`, of a dry run `Check finished` | `acme.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed`; without a file name only the counts | with the job |
| Import succeeded, failed files | `Warning` | `Import finished with failures`, `Check found problems` | the same | with the job |
| Import failed | `Error` | `Import failed`, `Check failed` | the job's `Error` | after the inbox's retention |

Every one of them: `Kind = Notification`, `Topic` `ViewExport` or `ViewImport` (`ViewInboxTopics`), `MinimalRole = Administrator` (the role of the job routes), `CreatedBy` = the job's `CreatedBy`, `Job` = kind, id and file name. The texts are built by a static `ViewTransferNotifications` with the invariant culture, so they are tested without the service.

- **A result lives exactly as long as its job** (`ExpiresAt` = the job's): when the job and its archive or report are gone, so is the item that leads to them.
- **A failure lives as long as any other item** (`ExpiresAt` null, 30 days by default): its whole content is the error text, it needs nothing of the job, and a pipeline's import that failed on Friday night is still there on Monday.

### 4. Storage (`Backend.CosmosDb`)

- **Partition** `{project}.inbox.{view}` of the organization container: `PartitionKeys.InboxPartitionInfix = "inbox"`, `GetInbox(project, view)`, `GetCosmosInbox(organization, project, view)`. One view's inbox is one logical partition, so the list and the count name the full key and need no organization predicate.
- **Entity** `Entities/Inbox/ViewInboxEntity.cs`: `id` = `inb.{item id}` (`IdPrefix = "inb."`), `PartitionKey`, `Organization`, `Project`, `View`, `ItemId`, the members of the item (`Kind`, `Topic`, `Level`, `Title`, `Text`, `MinimalRole`, `State`, `CreatedAt`, `CreatedBy`, `ExpiresAt`, `Closed`, `Action`, `Job`) and `ttl` (a non-nullable `int` with both JSON attributes, as `ViewTransferEntity`). It has **no `ViewName`**: an inbox item must not make an empty view a discovered one. None of its names is left out of the index.
- **Store** `Notifying/CosmosViewInboxStore.cs`, registered in `AddCosmosDbAnnotations` (`EntryPoint.cs`) as a singleton; it needs only the repository provider and the serializer settings, so DbCreator and the test fixtures resolve it.
  - `TryCreateAsync`: `CreateItemAsync`, null on 409.
  - `GetAsync`: a point read with the ETag; null for an entity of another view.
  - `TryReplaceAsync`: `ReplaceItemAsync` with `IfMatchEtag`, null on 412 and 404.
  - `QueryAsync`: `SELECT TOP @take * FROM c WHERE c.PartitionKey = @partition AND STARTSWITH(c.id, @prefix) AND ARRAY_CONTAINS(@roles, c.MinimalRole)`, plus `AND c.State = 'Open'` or `AND c.State != 'Open'` for the filter, plus `AND c.id < @before`, then `ORDER BY c.id DESC`. `@before` is `IdPrefix` + the item id, `@roles` the role names; the options are `repository.QueryOptions(partition, maxItemCount: take)`; the store reads until it has `take` entities.
  - `CountOpenAsync`: `SELECT VALUE COUNT(1)` with the same partition, roles and `c.State = 'Open'`.
  - `ttl` = `Math.Max(1, (int)Math.Ceiling(timeToLive.TotalSeconds))`.
- **No change of the container**: no new index path, no composite index (one `ORDER BY` on the id), the `ttl` works with the container's `-1`.
- Nothing deletes a view, a project or an organization today, so there is no cascade to extend; an item ends by its `ttl`.

### 5. The HTTP API

`Api.Functions/src/V1/ViewInboxHttp.cs`, tag `Inbox` (new), routes in `Definitions.Routes.Inbox.V1`, ids in `Definitions.RouteIds.Inbox`:

| Method | Route | Operation | Answers |
| --- | --- | --- | --- |
| `GET` | `v1/{organization}/{project}/{view}/inbox` | `GetViewInbox` | 200 `ViewInboxResponse`; 400 `InvalidQuery` |
| `POST` | `v1/{organization}/{project}/{view}/inbox/{id}/accept` | `AcceptViewInboxItem` | 200 `ViewInboxItem`; 404; 409 `InboxItemNotAction`, `InboxItemClosed`, `Conflict`, or the handler's own; 501 `InboxActionNotAvailable` |
| `POST` | `v1/{organization}/{project}/{view}/inbox/{id}/dismiss` | `DismissViewInboxItem` | 200 `ViewInboxItem`; 404; 409 `InboxItemClosed`, `Conflict` |

- **Query of the list**: `state` = `Open` (the default) or `Done` (accepted and dismissed), documented as a string like the `state` of the key/values, read with `KeyValueQuery.GetEnum<ViewInboxFilter>`; `continuationToken`.
- **Access: users only, the Contributor role on the project.** A machine is refused with 403 in every build; no machine scope is added. The three ids go into `OperationAccess.UserOnly`.
- **The caller's role.** A new helper in `HttpRequestDataExtensions`:

  ```csharp
  // A route for users only whose answer depends on the role: EnsureUser, the minimal role (403), and the role itself.
  // Where the role check is skipped (Debug builds, DEV_AUTH), the caller counts as Owner.
  public static async Task<AccountRole> GetUserRoleInProjectAsync(
      this HttpRequestData @this, IAccessService accessService, string organization, string project, AccountRole minimalRole)
  ```

  It reads `UserRoleChecks.AreSkipped` before the identity, as `CheckUserAccessAsync` does, and then returns `Owner` without a lookup, so a Debug build still serves a request without an identity. Every handler calls it with `AccountRole.Contributor` and passes the role to the service. `EnsureRole` gives the usual 403 "No Contributor access to 'org.project'."
- **The actor** of accept and dismiss is `request.GetAuditActor()`, so a call through an actor (the MCP server) is stamped with `Via`.
- **No request body.** The two `POST` routes read none, as `JoinExhibition`.
- **No step-up, no access audit record**: the routes change no access and reveal no secret. What an accepted action writes is stamped by its handler's own writes.
- **The view is not checked**, as on the other view routes: the inbox of a view without items is empty.
- **Descriptions**: `ResponseForbiddenViewInbox = ResponseForbiddenUserOnly + " The inbox needs the Contributor role. An item above the caller's role is not found (404)."` The 401 is `ResponseUnauthorizedUserOnly`.

### 6. OpenAPI snapshot and SDKs

- **Tests of the document and the routes** (`Api.Functions/test`): the three ids in `UserOnlyOperations`; a `Document_HasTheViewInboxOperations_UnderTheInboxTag`; a `ViewInboxSchemas_ArePublished` in `OpenApiDocumentTests`; `InboxRoutes_AreRegistered` in `RouteTableTests`; rows in `RouteDispatchTests`.
- **Snapshot.** After the API commit, the document is exported with its new version to `Sdk.NSwag/open.api.v<version>.json` and copied to `sdk.typescript/`, where the older file is removed.
- **C#.** `ApiSdk.g.cs` regenerated with NSwag 14.6.3 and the command of `Sdk.NSwag/README.md`: `IInboxApiClient` / `InboxApiClient`. By hand: the partial of the new client in `ApiSdk.partial.cs`, its registration in `ServicesCollectionExtensions.cs`, the eighth name in `SdkHttpClientConfigurationTests`, "eight typed clients" in the README.
- **TypeScript.** `npm run generate`: `generated/{client,vue-query,zod}/inbox/` and the models. By hand: `getInboxMock` in `src/msw.ts`; rows of the three operations in `test/msw.test.ts`; the section of the tag in the README.
- **`sform` and the MCP server get nothing**: no command and no tool (see Out of scope). Their coverage checks look at commands and registered tools only.

### 7. The administration UI

#### 7.1 Menu and routes

- `UI/app/utils/navigation.ts`: a sixth entry at the end of the view section, `{ label: 'Inbox', icon: 'i-lucide-inbox', to: `${base}/inbox`, editRole: 'Contributor', lockedHint: 'Contributors only' }`. A Reader sees it with the lock, as Modeling. A Contributor sees the entry now, although only Administrators get items until 424-8: the page is where the first suggested action will arrive.
- `UI/app/utils/context-paths.ts`: `'inbox'` joins `viewSections`, so a switch of the view or the project keeps the page.
- Shortcut **G I** ("Go to Inbox") in `UI/app/components/shell/CommandPalette.vue` and `UI/app/composables/useShortcutsHelp.ts`; the palette lists the page with the other pages.
- Page `UI/app/pages/[org]/[project]/[view]/inbox.vue`, route `/:org/:project/:view/inbox`. `UI/app/typed-router.d.ts`, `components.d.ts` and `auto-imports.d.ts` are regenerated and committed.

#### 7.2 The mark of open items

- `buildNavigation(context, marks?: { inbox?: boolean })` sets `marked: true` on the Inbox entry only, and only when asked; every other entry stays as it is, and the command palette passes nothing. `AppSidebar.vue` maps `marked` to the `chip` of the menu item: a dot on the icon, also in the collapsed rail and in the menu of a phone. It is a dot and not a number, so the text of the link stays `Inbox` and the trailing badge stays the lock.
- The marked entry also gets the tooltip "Open items" and, where the menu item passes it to the link, the accessible description of the same text, so the mark is not a colour alone.
- `AppSidebar.vue` asks for the open items of the view it shows (`useNavigationContext()`, the remembered view outside view pages) when the project role is at least Contributor, and marks the entry when `OpenCount` is above zero. It is the query the page observes too, so they share one request; the sidebar never polls. On any error the entry has no mark and nothing is reported.

#### 7.3 The page

- **Header**: `AppNavbar` with the title **Inbox** and a **Refresh** button.
- **Toolbar**: two tabs, **Open** (with the count when it is above zero) and **Done**.
- **A Reader** sees the lock, "Contributors only", and nothing is requested. A 403 of the list (the role was lost while the page was open) shows the same lock.
- **States**: loading, the error state with **Retry** (a 404 of a deployment without the routes, a 5xx), an empty state, the list.
- **The list**, newest first, **Load more** with the continuation token (the pattern of `AccessAuditTrail.vue`, with its guard of generations). A re-read that changes the first page starts the list again from it, because the next pages continue behind the old one; an unchanged answer keeps what was loaded. One item shows:
  - an icon of its kind and level (`UI/app/utils/inbox.ts`, literal icon names: an action `i-lucide-sparkles`, a message `i-lucide-message-square`, a notification by its level);
  - the title, the text, when it was created (`RelativeTime`) and by whom (`AuditActorLink`, with "via" for an actor; **System** for the platform);
  - a badge **Administrators only** when its role is Administrator or Owner;
  - of a closed item, "Accepted by … " or "Dismissed by …" with the time.
- **Buttons of an item**:
  - an open action: **Accept** (after a confirmation that names the title and says that the work runs in the background) and **Dismiss**;
  - a notification of an export that succeeded, open or done: **Download**, the archive through the existing route under the job's file name, and "Expires" with the time; a 404 says that the archive has expired;
  - a notification of an import whose level is not `Error`, open or done: **Show the result**, a wide slideover (`max-w-5xl`) with the report of the job (`ImportResult.vue`, read with `getViewImport`), without **Import now**; a 404 closes it with "The report has expired";
  - every open item: **Dismiss**;
  - an item of the level `Error` offers nothing that needs its job.
- **Answers of accept and dismiss**, plain SDK calls in a `try`:
  - 409 with `InboxItemClosed`: the toast "The item was already handled" and a fresh list;
  - any other 409 or 4xx (`InboxItemNotAction`, a handler's own such as `TransferLimit`): the API's message under "The action could not start", and a fresh list in which the item is still open;
  - 404: "The item is gone" and a fresh list;
  - 501: "This action is not available in this deployment".
- **Freshness**: while the page is open it observes the open items with a re-read every 20 seconds, whatever the tab, and the count of the tab and the mark of the menu both come from that answer. The interval stops while the query is in error. The Done list is a second query, read while its tab is shown, on **Refresh** and after every accept and dismiss.
- **Empty states**: Open, "Nothing to do" (icon `i-lucide-check-check`, not the inbox icon of the default) with "Results of exports and imports and suggested actions of this view arrive here." for an Administrator and "Suggested actions of this view arrive here." below; Done, "Nothing done yet".
- The list carries `ph-no-capture`: it shows names of people.
- A phone gets the same list in one column, with the buttons under the text.

#### 7.4 Results the tab already showed

A job that this tab followed to its end was reported there: the toast, the automatic download, or the result on the Export & Import page. Its notification would report it a second time and light the mark for the person who just saw it. So when `TransferToaster.vue` hears the end of a job, it invalidates the inbox of the view and **dismisses the job's item**: it reads the open items, finds the one whose `Job.Id` is the job's, and dismisses it (once more after two seconds when the item is not there yet; a failure is ignored). The item is then under **Done**, dismissed by that person, with its **Download** or **Show the result**.

What stays open and marked is what nobody was shown: a job of a tab that was closed, of another device, of a machine.

#### 7.5 Code

- `UI/app/queries/inbox.ts`: `useViewInbox(scope, filter, { enabled, poll })` over the generated query, always with an explicit `state` (so the sidebar and the page share the key) and `meta: { silent: true }`; `invalidateInbox(client, scope)`; `dismissJobItem(client, scope, jobId)`.
- `UI/app/utils/inbox.ts`: icons, labels and what an item offers (`inboxOffers(item)`), pure and tested.
- `UI/app/components/inbox/InboxItemRow.vue` and `InboxImportResult.vue` (the slideover).
- `UI/app/stores/transfers.ts`: `downloadArchive(scope, id, fileName)` next to the download of a followed job. The store, the automatic download and the toasts stay as they are.
- `UI/app/components/transfer/ImportResult.vue`: a `title` property with the default "Latest import".

#### 7.6 Mock API

- `UI/app/mock/inbox.ts` and `inbox-handlers.ts`, registered in `all-handlers.ts`: the three routes with the rules of the API (the Contributor role, the visibility by role before anything else, `state`, keyset paging, 404 and 409 with their codes).
- The items are in the mock database (`MockDb.inbox`, keyed `org/project/view`, an optional member seeded on first use, so the storage key stays).
- **Seed**, for the scopes the mock seeds with data (`scopeSeed` of the legacy data sets, the views of the showcase projects); any other view, a new project of the onboarding included, starts empty. An id is the 21 digits of the item's seeded time plus a fixed suffix; tests find seeded items by their title. The items: an open action `SchemaSuggestion` / `GenerateSchema` for Contributors ("Generate a schema for 4 similar configurations", by System), an open notification of a failed export for Administrators (by another user), an open message for Contributors, and two closed items (an import that finished, dismissed, without a job to open; an action, accepted).
- **Accept is the one rule where the mock leaves the API.** The API has no handler yet and answers 501; the mock runs the action at once: the item is accepted and a notification arrives (`Topic` `SchemaSuggestion`, `Success`, "Schema generation finished", no `Job`). It stands for what 424-8 adds, so the flow can be seen and tested; the README lists it among the mock's simplifications, and a test of the page runs against a 501.
- `UI/app/mock/transfer-handlers.ts` adds the notification when a job of the mock ends, with the texts and lifetimes of section 3. The jobs of the mock live in page memory, so the **Download** of such an item works until the tab reloads and answers 404 afterwards.

#### 7.7 Tests

- **Unit** (`UI/test/unit/inbox/`, `UI/test/unit/mock/`): the mock handlers per role (a Contributor gets 404, never 409, for an Administrator's item); `inbox.ts` of the utils; the row; the page through `mountScreen` (the lock of a Reader and of a 403, the error state, the tabs, accept with its confirmation and each answer including a handler's 409 and the 501, dismiss, download and its 404, the result of an import, Load more and its restart after a dismiss); the mark of the menu and its absence on an error; the toaster dismissing the item of a job it reported. Updated: `utils/navigation.test.ts` (six entries, the mark, the shortcut), `utils/shell.test.ts` (`inbox` as a section), the tests of the toaster (the invalidated keys).
- **End to end** (`UI/test/e2e/inbox.spec.ts`, desktop and phone): open the page from the menu, accept the action and find its result, dismiss an item and find it under Done, a Contributor without the Administrators' items, the lock of a Reader, an export started on Export & Import whose item is under Done with **Download** (reached through the menu, without a reload). No step waits for the 20 seconds. Updated: `navigation.spec.ts` (the six entries), `accessibility.spec.ts` (the page, light and dark; the menus of its other screens carry the mark), `visual.spec.ts` (the page) with baselines regenerated in Docker: about 30 desktop baselines change, 4 are new.
- **UI automation suite** (`src/Platform/UI.Admin.AutomationTests`): `AppRoutes.Inbox`, and the six entries in the two assertions of `NavigationMenuTests`. No new Testiny case in this change.

### 8. Documentation

| File | Change | Phase |
| --- | --- | --- |
| `docs/Platform/Storyteller/inbox.md` | New: what the inbox is, the model, kinds and states, roles, how a producer pushes, action handlers, the routes, storage and retention, the page | 1 to 3 |
| `docs/Platform/Storyteller/export-import.md` | Jobs: the notification of an ended job; Administration UI: the inbox next to the toaster; Mock API | 1, 3 |
| `docs/Platform/Storyteller/annotation-lifecycle.md` | Containers: the partition `{project}.inbox.{view}` and its expiry | 1 |
| `docs/Platform/Storyteller/audit-stamps.md` | The item among the records with an actor: `CreatedBy` and the stamp `Closed` | 1 |
| `docs/Platform/Storyteller/access.md` | Roles: the inbox for Contributors and the items for Administrators; the three error codes | 2 |
| `docs/Platform/Storyteller/authentication.md` | The three routes among the routes for users only | 2 |
| `docs/Platform/Storyteller/mcp.md` | Limits: the inbox is not offered | 2 |
| `src/Platform/Storyteller/Sdk.NSwag/README.md`, `sdk.typescript/README.md`, `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md` | The snapshot name, the eighth client, the tag `Inbox` | 2 |
| `CLAUDE.md` | The inbox in the `Api.Functions/` bullet of Storyteller | 2 |
| `docs/Platform/Deployment/runbook-azure-production.md` | The API with the inbox routes is published before the admin UI that calls them | 3 |
| `src/Platform/ui.admin/README.md` | The page, the mock handlers, the seed and the simplification, the e2e list | 3 |
| `docs/42for.net/platform/admin-ui.md` | A section **Inbox**: what a deployment shows today (the ends of exports and imports for Administrators), suggested actions as what comes; **G I** in the keyboard table | 3 |

No workflow and no path filter changes, so `docs/github/workflows.md` stays.

### 9. Tests of the back end

- **`Backend.Core/test/Notifying/`** (xUnit, Shouldly, hand-written fakes as the transfer tests, an `InMemoryViewInboxStore`):
  - `ViewInboxPublisherTests`: the validation and the limits, the default and the moved expiry, the idempotent push (a closed item is returned as it is; a conflict whose read finds nothing), the time to live of a write;
  - `ViewInboxServiceTests`: visibility per role (a Contributor accepting or dismissing an Administrator's item gets "not found", never a conflict), paging and tokens, `OpenCount`, dismiss and its conflicts, an expired record, accept (the claim, the handler's job, no handler, not an action, a handler failure reopens and passes its exception on, a token cancelled after the claim neither stops the handler nor leaves the item accepted), the three outcomes of a lost replace, two handlers of one code;
  - `ViewInboxIdsTests`: the order, a time with another offset, `IsValid(ForKey(…))`;
  - `ViewInboxRegistrationTests`: a container with `AddViewInbox`, `AddViewTransfers` and fakes resolves `IViewTransferService` and `IViewInboxService` with a handler that itself takes `IViewTransferService`; without `AddViewInbox` the transfer service gets no publisher.
- **`Backend.Core/test/Transferring/`**: `ViewTransferNotificationsTests` (every row of the table), and in `ViewTransferServiceTests`: each end pushes once, a redelivered message and the poison queue of an ended job push the same item, a lost job pushes nothing, a throwing publisher neither fails the job nor deletes the archive, a cancelled invocation still pushes, no publisher.
- **`Backend.CosmosDb/test/CosmosViewInboxStoreTests.cs`** (FluentAssertions, as the suite): create and its conflict, the point read through the view only, replace with the ETag, the query per filter and role in id order, a second page with `before`, the count, the stored `ttl` read from the raw document (about `ExpiresAt - now` after a create, smaller and not restarted after a replace), the item does not discover a view. No test waits for an expiry. `ViewInboxIsolationTests`: two organizations with the same project, view and ids. The tests carry no trait, so a pull request runs them on vNext; the classic emulator is run by hand before the review of phase 1.
- **`Api.Functions/test/ViewInboxHttpTests.cs`**: each route with a recording service: the role given to the service, a Reader and a machine refused, `state` and its 400, the actor of accept and dismiss; without `Enforce()` in a Debug build the service gets `Owner` and no role is looked up, also for a request without an identity; a token without `User.Impersonation` is 401 in a Release build. Run in Debug and Release.

### 10. Decisions

1. **An item is its own document, not a view over the jobs.** The inbox must also hold actions and messages that no job stands behind, and a producer must be able to push without the inbox knowing it. The job stays the source of its own details; the item refers to it.
2. **Pushing and accepting are two services.** A handler starts a job through the job's service, and that service pushes the report. One service for both would close a circle the container refuses, and a wrong handler would stop every export.
3. **Roles of the project decide, with a minimal role per item.** There is no role on a view. Readers are out: every item is a result of a write or an offer to write.
4. **The results of exports and imports are for Administrators**, because reading those jobs is (424-66). The issue names Contributors as readers of the inbox; they get it, and the first items they see are the actions of 424-8.
5. **Users only.** The inbox is where people decide. A machine scope would cost a change of every identity provider and of existing machines for nothing a machine needs.
6. **One state for everybody, no read mark per person.** It is a shared work list: an action accepted by one Administrator is done for all. A mark per person can be added later without changing an item.
7. **A result that a person was shown is closed by the tab that showed it.** The UI never reported an end twice, and it keeps to that. What stays open is what nobody saw; a pipeline that exports every night therefore keeps the mark lit until somebody dismisses its results or they expire, which is the point of the mark.
8. **A result expires with its job, a failure does not.** A longer life of a result would show a download whose archive is gone; a failure is complete in itself.
9. **The push is idempotent and never fails the job.** The save of a job and the push are two writes; a crash between them is repaired by the redelivered message or the poison queue, and a broken inbox costs a notification, never an export.
10. **An accepted action is claimed before it runs, and runs to its end.** The opposite order would start a job twice under a race. The price: a crash between the claim and the run leaves an accepted item without its job.
11. **Actions run through handlers, and none is registered yet.** The issue calls this the preparation; the first real action is 424-8, which also decides what an action carries for its handler. The accept route answers 501 for an action without a handler instead of pretending.
12. **No push channel to the browser.** The page re-reads itself while it is open, and the mark of the menu follows the reads of the sidebar. A long poll in the shell would add a request to every page and every test.
13. **A dot in the menu, the number on the page.** A number inside the link changes its text and its trailing badge, which the tests of the menu and the UI automation suite use for the entries and the lock.
14. **Inbox is the last entry of the view section**: the documents come first, and the positions the tests address by index stay.
15. **The toaster and the automatic download stay.** They are pinned by three test suites and still serve the tab that started the job; the inbox adds what they cannot: other tabs, other people, a second download.
16. **No route for one item, no filter for everything.** The page needs the list and the answers of accept and dismiss; a route without a caller would be untested surface.

### 11. Phases and commits

Every commit subject ends with `(424-31)`. The living documentation of a phase is committed in that phase.

1. `docs(platform): plan the inbox of a view`
2. **Phase 1**: `feat(platform): inbox of a view in the backend, fed by the ends of exports and imports`, `docs(platform): describe the inbox of a view`, `docs(platform): review phase 1 of the inbox of a view, the backend` (`docs/Platform/Storyteller/specs/reviews/<date> Phase 1 of Inbox of a View.md`).
3. **Phase 2**: `feat(platform): inbox routes of the API for users`, `chore(platform): regenerate the SDKs from OpenAPI <version>`, `docs(platform): describe the inbox routes and the SDK clients`, `docs(platform): review phase 2 of the inbox of a view, the API and the SDKs` (`docs/Platform/Storyteller/specs/reviews/<date> Phase 2 of Inbox of a View.md`).
4. **Phase 3**: `feat(platform): mock the inbox in the admin UI`, `feat(platform): Inbox page of the view section in the admin UI`, `test(platform): the Inbox entry in the visual baselines and the UI automation tests`, `docs(platform): describe the Inbox page`, `docs(platform): review phase 3 of the inbox of a view, the admin UI` (`docs/Platform/ui.admin/specs/reviews/<date> Phase 3 of Inbox of a View.md`).

**Merge notes.** The admin UI deploys by itself when the pull request reaches `main`; the API does not. Until the API with the routes is published, the menu shows no mark and the page shows its error state. Publish the API first.

### 12. Verification

```bash
dotnet build src/Platform
dotnet test --project src/Platform/Storyteller/Backend.Core/test
dotnet test --project src/Platform/Storyteller/Api.Functions/test
dotnet test --project src/Platform/Storyteller/Api.Functions/test -c Release
dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*EntityOrganizationTests"
STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*ViewInbox*"
STORYTELLER_TESTS_USE_CONTAINER=true STORYTELLER_TESTS_EMULATOR=classic dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*ViewInbox*"
STORYTELLER_TESTS_USE_CONTAINER=true dotnet test --project src/Platform/Storyteller/Backend.CosmosDb/test --filter-class "*Transfer*"
dotnet test --project src/Platform/Storyteller/Sdk.NSwag/test
dotnet test --project src/Platform/Cli/test
dotnet test --project src/Platform/Storyteller/Mcp/test
(cd src/Platform/Storyteller/sdk.typescript/src && npm run generate && npm run typecheck && npm run lint && npm test && npm run build)
(cd src/Platform/ui.admin/src && npm run prepare:local && npm run lint && npm test && npm run build && npm run test:e2e && npm run test:visual:docker)
```

### 13. Differences from the issue

- **"Per view" is the place, not the permission.** An item belongs to a view; who sees it is decided by the role on the project.
- **Exports and imports appear for Administrators only**, not for Contributors. Until 424-8 the inbox of a Contributor is empty.
- **A job in progress has no item.** The inbox shows a job when it ends; the page Export & Import shows it while it runs.
- **The person who started a job in the tab finds its result under Done**, because the tab already showed it.
- **The offer of 424-8 is not in this change.** The inbox, the acceptance and the handler interface are; the mock of the UI shows the offer so the flow can be seen and tested.
- **"Push" means stored for the next read.** The back end pushes an item into the inbox; the browser reads it.

### Out of scope

- The detection of patterns, the generation of schemas and the AI abstraction (424-8), and what an action carries for its handler.
- A push channel to the browser, e-mail or other deliveries of an item.
- A read mark per person, and settings of what a person wants to see.
- Machines on the inbox routes, `sform` commands and MCP tools.
- Inboxes of a project or an organization, and items that address one person.
- A route for one item, a list of jobs on the transfer routes; the items refer to the jobs instead.
- New Testiny cases for the page, and the edit of the steps of cases 24 and 27 in Testiny.
- Removing or changing the toaster, the automatic download or the 15 minutes of the tab's waiting.
