# Phase 4 of Hosting, Informer and Aspire for .NET

Linear: [424-91](https://linear.app/424fun/issue/424-91/hosting-featuremanagement-and-aspire-packages-and-the-subscription).

## Overview

Phase 4 covers section 8 of [Hosting, Informer and Aspire for .NET](../2026-10-27%20Hosting,%20Informer%20and%20Aspire%20for%20.NET.md): the subscriptions. `AddSubscriptions` adds a hosted informer for one responsibility in one context that keeps the resolved document of every subscription in memory, behind `IStorytellerSubscriptions`, and `AddSubjectOptions<TOptions>` binds `IOptionsMonitor<TOptions>.Get(subject)` to those documents with one change notification per subject. A follow-up drops the named options of a change before its watchers hear of it, and reports a broken informer. The final review of the whole branch closed the construction of `StorytellerSubscription` and found that the documented worker could stop its host on one tenant's options; both are listed under "Final review".

| Commit | Content |
|---|---|
| `135cae9e` | Section 8: the subscriptions, their status, the named options, the registration |
| `749f7bb0` | Follow-up: named options dropped under the publish lock, a broken informer reported, the order of `ConfigureAll` |
| `29d18041` | Final review: `StorytellerSubscription` without public construction, the shared poll interval rule |
| `51400328` | Final review: a test of a watcher that fell behind; the worker of `hosting.md` handles a failure per subject |

| Project | Passed (final state of the branch) |
|---|---|
| `Hosting/test`, the classes of this phase | `SubscriptionsTests` 28, `SubjectOptionsTests` 12, and the `AddSubscriptions`/`AddSubjectOptions` cases of `RegistrationTests` |
| `Hosting/test`, all | 218 |

## What Was Done

### 8. Subscriptions: the informer, the documents and named options

The public types are as specified: the two `AddSubscriptions` overloads and `AddSubjectOptions<TOptions>` (in `Microsoft.Extensions.DependencyInjection`, `StorytellerHostingBuilderExtensions`), `StorytellerSubscriptionOptions`, `IStorytellerSubscriptions`, `StorytellerSubscription`, `StorytellerSubscriptionChange`, `StorytellerSubscriptionsStatus` and `StorytellerPendingSubscription`.

- **Options** (`Subscriptions/SubscriptionSettings.cs`), read and validated at registration. **Beyond the spec:** `PollInterval` is zero or 1 s to 45 days, as the provider's `RefreshInterval`; `StartTimeout` is positive or infinite; `Mode` must be a read mode. A bad option throws `StorytellerConfigurationException` that names the option.
- **The status** prints messages, not exceptions: `ToString()` of `StorytellerSubscriptionsStatus` and of `StorytellerPendingSubscription` leave out the inner exceptions, which Hosting does not vouch for.

### 8.1 The loop

`Subscriptions/SubscriptionsService.cs` is the `BackgroundService`, registered once as `SubscriptionsService`, `IStorytellerSubscriptions` and a hosted service. It owns a `PollingSubscriptionSource` over the catalog's reader and a private `StorytellerDocuments` built from the Hosting settings. Per batch it applies the changes, reads the document of every held subscription at most `MaxConcurrentReads` at once through `GetEntryAsync`, and publishes: a new view and status, then the events. `MaxConcurrentReads` also limits the informer's reads of the poll. With `WaitUntilReadyOnStart`, `StartAsync` waits up to `StartTimeout` on the system clock, as `ReadTimeout` runs, and a definitive failure of the poll fails the start at once. Stopping cancels the loop and disposes the private documents.

### 8.2 Rules

As specified: `Added` with the first good document, pending until then (in `Status.Pending` with its last error, a warning per failed read); `Changed` for a change of the execution or the usage, or of the document's data (`HasSameData`), not for a `304`, a new `ETag` with the same data or a version that changed only labels or values of the configuration; a 304 whose secrets resolved to new values is `Changed`; a failed read keeps the last good document and sets `LastError`/`LastErrorAt` for every class of failure; `Removed` evicts the entry; ready after the first complete poll and one read per subscription in it. A failure of a resolver of the application is classified as transient and named by its type. A pending subscription that leaves raises nothing.

### 8.3 Responsibility and key

As specified. The context-only overload takes the responsibility from the chain of the key and throws for a key without one; the named overload throws for a key whose chain names another responsibility, and takes only the view from a key without one (`sbt`, `cnt`). Both throw without a key, and a second `AddSubscriptions` in the container, or one on a collection without `services.AddStoryteller(…)`, throws `InvalidOperationException`.

### 8.4 Named options

- `SubjectOptionsConfigurator<TOptions>` binds the document, or its section, of a name that `Find` resolves, and remembers which instance it bound for which subject; `SubjectOptionsValidator<TOptions>` fails any other name than the default one with "no subscription of the subject S in R/C (not ready yet, pending, or not subscribed)". A value that does not convert throws `StorytellerConfigurationException` naming the key, the type and the section, never the value.
- `SubjectOptionsMonitor<TOptions>`, registered closed over `TOptions`, wraps the framework's `OptionsMonitor<TOptions>`. On `Added` and `Changed` it drops the subject from the cache and raises `OnChange` with `(Get(S), S)`; on `Removed` it drops it and raises nothing. A `Get` that fails after a change skips `OnChange` with a warning; a listener that throws does not stop the others.
- **Beyond the spec:** `AddSubjectOptions` once per options type (a second call throws `InvalidOperationException`), and an empty section throws `ArgumentException`. Without `AddSubscriptions`, the first resolution throws `InvalidOperationException` naming the missing call.

### 8.5 and 8.6

The two watch APIs and the separate `IStorytellerDocuments` are as specified; the docs say which to use (phase 6).

### Follow-up

`749f7bb0`:

- The monitor dropped a subject's options when it notified, after the watchers and `WaitUntilReadyAsync` had seen the change, so a watcher that called `Get(subject)` could get the cached options of the previous document, or a cached validation failure. It now drops them under the lock the loop publishes under, before anyone can see the change (`Listen(invalidate, notify)`), and notifies after the lock.
- An informer loop that broke looked like a stop. It now reports its failure as `LastPollError` (the status is degraded), ends the watchers with it, also those that start later, and `WaitUntilReadyAsync` throws it.
- `AddSubjectOptions` says that `ConfigureAll` before it gives defaults that the documents override, and after it overrides them; a test pins the order.
- The `MaxConcurrentReads` test holds the reads on gates instead of a real latency.

## Final review

- **`StorytellerSubscription` had `required` and `init` members no caller could use** (`29d18041`). Its constructor is internal and takes the internal `DocumentEntry`. Its members are get-only now and set by that constructor; `SnapshotTests` checks that it has no public constructor and no setters, and `hosting.md` says why (the document belongs to the subscriptions, and `StorytellerDocument` has no public constructor either).
- **The poll interval rule** is shared with `WatchExecutionsAsync` (`29d18041`, see phase 3).
- **A watcher that falls behind, and a document that does not bind** (`51400328`). Each watcher has its own unbounded queue, and `Get(subject)` binds the subscriptions as they are now. A watcher a poll behind gets `Added` or `Changed` for a subject that left since, whose `Get` fails validation; a new document whose value does not convert makes `Get` throw, and the options of the previous document are gone. The worker of `hosting.md` called `Get` without a guard, so either exception left `ExecuteAsync`, stopped the host under the default `StopHost`, and after a restart the replay met the same document again. The library's behaviour stays as documented; the worker now catches both per subject, the Named options section says when "in step with the watchers" holds, how to bind the event's own document, that a document that does not bind leaves the subject without options until one that binds arrives, and that the status does not show it. `SubjectOptionsTests.AWatcherThatFellBehind_…` pins the lagging watcher: `Get` fails for the subject that left, the event's document still binds, and its `Removed` follows. **Not changed:** keeping the last good options of a subject whose new document does not bind, and an API that binds the options of an event's own document through the options pipeline. Both are possible later without a break.
