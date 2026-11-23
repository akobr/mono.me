# PR Review Findings of Inbox of a View

Linear: [424-31 Prepare notification center (inbox) in Admin UI](https://linear.app/424fun/issue/424-31/prepare-notification-center-inbox-in-admin-ui).

## Overview

The review of PR #220 by CodeRabbit on [Inbox of a View](../2026-11-22%20Inbox%20of%20a%20View.md), checked against the branch. It left six comments: one is fixed in code, two in documentation, and three are left as they are, with the reason.

## What Was Done

### 1. Fixed: a cancellation during the claim could leave an accepted item without its work

`ViewInboxService.AcceptAsync` gave the caller's token to the write of the claim. A write that reached the store while its caller went away threw before the handler's `try`, so nothing reopened the item: it stayed `Accepted`, the handler never ran, and a second accept answered `InboxItemClosed`. The plan said "the request's token is used up to and including the claim"; the claim itself was the gap.

`AcceptAsync` now checks the token once before the claim (a call that is already cancelled starts nothing) and writes the claim with `CancellationToken.None`, as the handler, the reopening and the save of the job already ran. `InMemoryViewInboxStore.TryReplaceAsync` models the lost answer: it saves, then throws when the token is cancelled. With the claim listening to the caller's token again, three tests fail: `Accept_WithATokenCancelledWhileTheClaimIsWritten_StillRunsTheHandler` (new) and the two `Accept_WithATokenCancelledAfterTheClaim_…` tests. `inbox.md` (Accept, step 6) says the same.

Dismiss keeps the caller's token: a dismiss that reached the store is a dismissed item, which is what the caller asked for.

### 2. Fixed: the public page promised an item for every job

`docs/42for.net/platform/admin-ui.md` said that every export and import that ends is kept in the inbox. The push is a best effort (a failed push is logged and costs the notification, never the job) and an item expires, so the page now says that a job that ends leaves an item, and that the inbox is not a history of the jobs.

### 3. Fixed: the example of the TypeScript SDK invalidated the view of the refs

The README's example of `useDismissViewInboxItem` built the path of its invalidation from the refs, which may name another view by the time the call ends. It now takes the organization, the project and the view from the variables of the mutation. The admin UI was not affected: its page calls the plain functions and invalidates with the scope it took when the button was pressed.

### 4. Left: no predicate on `ExpiresAt` in the list and the count

An item's `ttl` counts from the commit of its write and is rounded up to a second, so the list and the count can show an item for about a second after its `ExpiresAt`. Accept and dismiss already treat such an item as missing. A predicate would need the expiry as a sortable value next to the stored `DateTimeOffset` and a clock in the store, for a second of difference.

### 5. Left: the partition of a view with a dot in its name

`{project}.inbox.{view}` is the same string for `("p.inbox.a", "b")` and `("p", "a.inbox.b")`, as `{project}.kv.{view}` is for the pairs of a view. New names have no dot (`NameRules`), and for older ones the point read, the list and the count compare the project and the view. Two such pairs that push the same key at the same time would get one id; the keys of the jobs carry a new GUID per job. A partition that cannot collide would leave the form every other partition of a project has.

### 6. Left: the toaster looks for a job's item on the first page only

`dismissJobItem` reads the 50 newest open items, twice at most. The item of a job that just ended is among the newest; with more than 50 newer open items it stays open, which costs a mark, not a result. Following the continuation tokens would read the whole open list of a view after every job.

## Second round (2026-11-23)

The rebase onto main brought in its rule for the menu (424-272: a lock means read-only, a page the role must not open has `minRole`) and the refusal of a client app that may only read on every write (424-302). CodeRabbit's second review and the comments outside the diff of its first one gave five more findings: four fixed, one in the docs only.

### 7. Fixed: an item that expired between its read and its write could still be accepted

`GetVisibleAsync` checks `ExpiresAt` against the time of the read; `CloseAsync` took the time again for the write and did not look. An item that expired in between was replaced with a time to live of one second, and an accept ran its handler. `CloseAsync` now checks `ExpiresAt` against its own time before every replace and answers "not found". Test: `AcceptAndDismiss_OfAnItemThatExpiresAfterItWasRead_AreNotFound_AndRunNothing`, with a clock that passes the end between the two reads; without the check both cases fail.

### 8. Fixed: the 409 of accept promised that the item stays open

When a handler throws, the item is opened again on a best effort; a failed reopening leaves it accepted. The OpenAPI description of the `409` of accept and the table of `inbox.md` now say so, and tell a client to read the item again before it offers another try.

### 9. Fixed: the 400 of the list did not say which tokens it refuses

A token of the inbox of another view is well formed and is not refused: it pages the view below the item it names. The description of the `400` of the list and `inbox.md` (Listing) now say that only a token the inbox does not issue is refused, and that a client starts again without a token after a change of the view. Binding a token to its view would refuse a case that does no harm.

### 10. Fixed: the public page promised that the item of a job the tab showed is under Done

The tab dismisses that item only when the end it showed is the server's end and the inbox answers. `docs/42for.net/platform/admin-ui.md` now says that the item is usually under **Done**, and stays under **Open** when the tab stopped waiting, could not download the archive or could not reach the inbox.

### 11. Changed by the rebase: the Inbox entry is hidden from a Reader

Main removed `lockedHint` and made the lock of an entry mean read-only. The inbox is closed to a Reader, so its entry now has `minRole: 'Contributor'`: a Reader does not see it, and the page at its address still shows the lock "Contributors only". This changes section 7.1 of the spec (a locked entry for a Reader); the menu's tests, the e2e test of a Reader and the docs follow.

## Grok review (2026-11-23)

A review of PR #220 by Grok (verdict approve-ready), checked against the rebased branch: four important points and nine nits. Five changes are made, the rest is left with the reason.

### 12. Fixed: the toaster dismissed a shared item in the name of the person, whoever caused it

The state of an item is shared, and the dismissal is stamped with the person of the tab without a click of theirs. `dismissJobItem` now dismisses only a `Notification` of the job whose `CreatedBy` is the signed-in account, and nothing while the account is not known. The tab follows only jobs it started, so the rule changes nothing today; it keeps the automatic dismissal from closing an item that somebody else caused once 424-8 adds producers. `inbox.md` (Results the tab already showed) says the dismissal is automatic. Test: `dismisses nothing that another account started, and nothing without an account`. A read state per person stays out of scope (spec, decision 6).

### 13. Fixed: a wrong `ViewInbox:RetentionDays` failed every failure notification with only a warning

The options are validated at the start of the host (`ValidateOnStart`), so a wrong setting stops the API where it is seen. The publisher still reads the setting only in a push without an expiry, so a container without a host builds the transfers all the same. `ViewInboxRegistrationTests` checks `IStartupValidator`; without `ValidateOnStart` it fails.

### 14. Pinned: the archive stays when the failure of an export cannot be saved

When `FailAsync` cannot save the failure because the store throws, the job's state is unknown: it may have ended, with its archive. Deleting the archive then could take a succeeded export's download away, so it stays, and the purge removes it after the retention; only a succeeded export is downloaded. `export-import.md` (Jobs) already said so; the test `ProcessExport_KeepsTheArchive_WhenItsFailureCannotBeSaved` now pins it. A third state of `FailAsync` would change nothing a client can see.

### 15. Documented: a keyed push is the same item only with the same key and time

The comment of `ViewInboxPush.Key` and `inbox.md` (Pushing an item) say that the id comes from the key **and** `CreatedAt`, so a producer passes the time of its event, never the time of the push. `Push_WithAKey_GivesAnotherItemForAnotherKeyOrAnotherTime` already shows the second item.

### 16. Nits fixed

- **Handlers are singletons**: said in `IViewInboxActionHandler` and `inbox.md`. Two handlers of one code already stop only the inbox service at its first build; catching them at the start of the host is left.
- **The answer of accept names a job the item may not store**: a comment in `SaveJobAsync` says it is deliberate (the caller learns the job its acceptance started).
- **A cut no longer splits a character of two UTF-16 units**: `Push_CutsBeforeACharacterOfTwoUnits_NeverInItsMiddle`.
- **`canRead` of the page is `canUseInbox`.**

### 17. Nits left

- `?state=1` is accepted as `Done`, as on the key/value routes (`KeyValueQuery.GetEnum`); `inbox.md` names it.
- The Done tab is ordered by arrival, not by the time of closing: the keyset paging needs one sort key, and an index on the closing time would sort the open items too. `inbox.md` says so.
- A changed first page drops the pages loaded with **Load more**: keeping them while the old last item is still on the new first page is a refinement, not a fix; the behaviour is documented and tested.
- `dismissJobItem` looks at the 50 newest open items (finding 6 above).
- The version header of the generated TypeScript files is Orval's; changing it is outside this change.

### Checks

| Suite | Result |
| --- | --- |
| `Backend.Core/test` | 1865 passed (1864 before) |
| `Backend.Core/test`, `--filter-class "*ViewInboxServiceTests"`, with the claim on the caller's token again | 3 of 76 failed, the three named above; restored, 76 passed |

The checks of the pull request: every check passed except `vNext emulator`. Since about 01:49 UTC on 2026-11-22 the vNext emulator of that job answers a create of a taken id with `500` ("cannot drop portal … while it is being set up") instead of `409`. The existing test `CosmosViewServiceTests.CreateView_Twice_ThrowsViewExists` fails with it on every pull request since then, and so do the two inbox tests that create a taken id (`Create_ATakenId_ReturnsNullAndKeepsTheStoredItem`, `KeyedPush_FindsOnlyTheItemOfItsOrganization`); the other 998 tests of the suite passed there, and all 23 inbox tests pass on the classic emulator.
