# Phase 3 of Hosting, Informer and Aspire for .NET

Linear: [424-91](https://linear.app/424fun/issue/424-91/hosting-featuremanagement-and-aspire-packages-and-the-subscription).

## Overview

Phase 3 covers sections 7.1-7.5 of [Hosting, Informer and Aspire for .NET](../2026-10-27%20Hosting,%20Informer%20and%20Aspire%20for%20.NET.md): the informer. `ISubscriptionCatalog.WatchExecutionsAsync` streams the subscriptions of a responsibility in a context, an active execution with its active usage per subject, from a polling change source behind the internal seam the product owner asked for. A poll lists the active executions and usages of the responsibility, joins them, diffs them against what it holds by fingerprints of the list items, and takes snapshots and removal reasons from point reads only. A follow-up ends a cancelled watch as a stop when a read of the poll failed. The final review of the whole branch bound the informer's interval to the rule of the subscriptions; that is listed under "Final review".

| Commit | Content |
|---|---|
| `cf0c7ffd` | Sections 7.1-7.5: the polling source, the seam, the list reads, the public events |
| `a005d9c3` | Follow-up: a stop during a failing poll ends with `OperationCanceledException` |
| `29d18041` | Final review: the poll interval rule of `WatchExecutionsAsync` |

| Project | Passed (final state of the branch) |
|---|---|
| `Hosting/test`, the classes of this phase | `InformerTests` 56, `ListTests` 13 |

## What Was Done

### 7. `ISubscriptionCatalog`

The public types are as specified: `WatchExecutionsAsync`, `StorytellerExecutionChange`, `StorytellerChangeKind`, `StorytellerRemoval` and `StorytellerRemovalReason`. `Catalog/SubscriptionCatalog.cs` checks the arguments at the call, not at the first `MoveNextAsync`, and builds a new `PollingSubscriptionSource` per enumeration, so each runs its own loop.

### 7.1 A subscription

An execution `exe.S.R.C` that is active, whose usage `usg.S.R` is active. Every event carries both snapshots.

### 7.2 Scope and names

- `Informer/SubscriptionScope.cs`: organization, project and view from the options' key, which may be a key of any type in the view (a test scopes the informer by a key of another type); without a key, `StorytellerConfigurationException` naming `STORYTELLER_KEY` and the option. Names are one key segment each, compared ordinally; an empty one or one with a dot throws `ArgumentException` with the parameter's name.
- The interval: 30 s by default, with the provider's jitter (`StorytellerConfigurationEngine.ComputeDelay`) on the options' `TimeProvider`, counted from the end of a poll; zero polls once.

### 7.3 One poll

- **The lists.** `Reading/CatalogReader.cs` reads pages of `…/annotations/rst.{R}/executions` and `…/annotations/rst.{R}/usages`, active only, and follows `ContinuationToken` (escaped) until it is absent, through empty pages. A key listed twice keeps the last item. A token answered twice, or more than 1000 pages (`PollingSubscriptionSource.MaxPages`), fails the list with a message that asks to update the API, as one without the paging fix of phase 1 answers the first page again. Any failed page fails the poll.
- **Join, fingerprints, diff, order** as specified. `Informer/ListFingerprint.cs` fingerprints a list item by its `Modified` stamp, or by a hash of its members by name when it has none. A candidate not held is read; a held one whose fingerprint moved has what changed read again; a held one that is no candidate is confirmed by reads with `includeDeleted`, and the first of execution and usage that is deleted, inactive or gone gives the reason. Both active means the list missed it: no event, a debug log. The changes of a poll are `Removed`, `Changed`, `Added`, each by key.
- **Point reads that fail** leave the subscription as it was and are diffed again at the next poll; a failure other than a `404` warns, a `403` names the restriction or the missing scope. An annotation gone since the list raises nothing and logs at debug level. A candidate or a changed subscription read as inactive is not added or changed.
- **Reads** run at most 4 at once for the catalog's informer (a `SemaphoreSlim` per enumeration).
- **A failed poll** emits nothing, keeps the held set, warns with the failure's message and polls again at the next interval.
- **Deviation: the responsibility is read until a poll completed**, not only by the first poll, so a first poll that failed, at its read of `rst.R` or later, does not leave the responsibility unchecked. A `404` fails the poll as a definitive `StorytellerConfigurationException`, "The responsibility R does not exist".
- **The wrong context.** A first poll whose list holds executions of R in other contexts only warns once, naming up to ten of them.
- **Beyond the spec: a zero interval throws the failure of its one poll**, as there is no next poll to try again; a longer interval logs and polls again.

### 7.4 `GetExecutionAsync`

Phase 2.

### 7.5 The seam for a watch transport

- `Informer/ISubscriptionChangeSource.cs` and `Informer/SubscriptionBatch.cs` as specified. **Deviation:** `SubscriptionBatch` also carries a `Failure`, the failure of a poll that could not tell what changed, with no changes and no dirty keys, so a consumer that keeps a status (the subscriptions of phase 4) learns of failed polls; `WatchExecutionsAsync` yields the `Changes` only.
- `Reading/IAnnotationPages.cs` is the internal interface of the two lists and the point reads; `CatalogReader` answers it over HTTP, and the tests' `ScriptedPages` without HTTP.
- The polling source marks every held key dirty in every batch.

### Tests

`Hosting/test/Informer`: `FakeCatalog` answers the lists and point reads by URL path, `ScriptedPages` scripts pages, and `PollProbe` advances the fake clock once the loop armed its timer. The cases of the spec's list are covered, each removal reason for the execution and for the usage, the jitter bounds, and cancellation while waiting and during a poll.

### Follow-up: cancellation during a failing poll

`a005d9c3`: a read of a poll may fail while another read still runs, and the poll throws only after all of them ended. A stop in that time ended the enumeration with the read's `StorytellerConfigurationException`. `ReadAsync` now turns a failure that arrives after cancellation into an `OperationCanceledException` with the failure as its inner exception. Tests stop the watch while one list pages on after the other failed, and while a point read fails.

## Final review

- **The informer's interval had no lower bound** (`29d18041`). `WatchExecutionsAsync` refused only a negative interval, and `PollingSubscriptionSource` only checked `>= 0`, so `TimeSpan.FromMilliseconds(5)` polled in a near-tight loop, two list requests and point reads per poll, while `AddSubscriptions` refused the same value; an interval above 49.7 days would have thrown from `Task.Delay` after the first poll. The rule now lives in `PollingSubscriptionSource` (`MinimumPollInterval` 1 s, `MaximumPollInterval` 45 days, `IsValidPollInterval`, `PollIntervalRule`) and is applied by `WatchExecutionsAsync` (`ArgumentOutOfRangeException` naming `pollInterval`), by `SubscriptionSettings` and by the source's constructor. `InformerTests` covers the bounds; the interface comment and `hosting.md` ("The interval") state the range.
