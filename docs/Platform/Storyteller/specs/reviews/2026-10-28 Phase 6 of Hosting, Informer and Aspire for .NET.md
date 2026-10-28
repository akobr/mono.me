# Phase 6 of Hosting, Informer and Aspire for .NET

Linear: [424-91](https://linear.app/424fun/issue/424-91/hosting-featuremanagement-and-aspire-packages-and-the-subscription).

## Overview

Phase 6 covers section 12 of [Hosting, Informer and Aspire for .NET](../2026-10-27%20Hosting,%20Informer%20and%20Aspire%20for%20.NET.md): the documentation of the two packages and the pages that point at them. A follow-up corrected notes that the review of the docs found wrong against the code. The final review of the whole branch found the documented worker unsafe for a multi-tenant host, the status's `ToString()` described too broadly, and these reviews missing; their fixes are listed under "Final review". The backlog issues of section 14 are created with the PR, not on the branch.

| Commit | Content |
|---|---|
| `72391d26` | Section 12: `hosting.md`, `aspire.md`, the package readmes, the links of the other pages, `CLAUDE.md` |
| `e5e38fba` | Follow-up: masking, paging, registration, restriction, status and versioning notes |
| `99074b88`, `29d18041`, `51400328` | Final review: the doc parts of those fixes |
| This commit | Final review: the reviews of phases 1-6 |

## What Was Done

### 12. Documentation

- **`docs/Platform/Storyteller/hosting.md`** (new): the package and its namespaces (registration needs no `using`; `AuditStamp` named qualified when both namespaces are imported), a service of one subject and a worker for every subject, registration and its options (which provider options Hosting ignores, one credential, separate transports), who am I with its reads, failures, refresh and the status table, entitlements as facts, snapshots, the catalog and the informer (events, removal reasons, one poll step by step, what `Changed` sees), subscriptions (options, loop, rules, `IStorytellerSubscriptions`, status with a health check, start and stop), named options, which watch to use, scopes and restricted machines, errors and log categories, cost and limits, and the FeatureManagement note.
- **`docs/Platform/Storyteller/aspire.md`** (new): the resource and its endpoint forms, credentials as parameters, keys (`WithStorytellerKey`, `WithExecution`, `WithView`), the local platform with `Api.Functions`, containers, and what reaches non-.NET processes.
- **Package readmes** `docs/Platform/Storyteller/Hosting/package-readme.md` and `docs/Platform/Storyteller/Aspire.Hosting/package-readme.md`, in the shape of the Configuration readme.
- **`configuration-provider.md`** links to the new pages where it says what the provider does not do; **`annotation-lifecycle.md`** says that every annotation list pages; **`docs/42for.net/platform/using-the-platform.md`** lists the two packages next to the provider; **`CLAUDE.md`** lists `Configuration/`, `Configuration.KeyVault/`, `Hosting/` and `Aspire.Hosting/` among the Storyteller projects.
- **Deviation: the versions.** The spec assumed that Hosting and Configuration share a version. They do not: each version counts the commits that changed its own project. The docs say to let Hosting bring Configuration and not to update Configuration on its own, as the dependency is not pinned exactly yet (`e5e38fba`).

### Follow-up

`e5e38fba` corrected, against the code:

- the names `WithReference` masks in run mode, and that the provider's settings and `sform`'s `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` are still inherited from the AppHost;
- the paging note: a token of a type the list does not read ends the list with an empty page, only a token whose Cosmos DB part Cosmos DB rejects answers `400 InvalidQuery`, and a token goes back to the list that returned it;
- the registration rule of `AddSubscriptions`: only the context-only overload throws for a key that names no responsibility;
- a machine restricted to its execution fails the informer at its read of `rst.{responsibility}`, before any list;
- `Subscriptions` and `Failing` of the status are counts;
- the versioning note above.

## Final review

- **The documented worker could stop its host on one tenant** (`51400328`). It called `IOptionsMonitor.Get(subject)` on every `Added` and `Changed` without a guard. A watcher a poll behind fails validation for a subject that left since the event, and a document whose value does not convert makes `Get` throw; either exception left `ExecuteAsync`, stopped the host under the default `StopHost`, and the replay after a restart met the same document again. The worker now catches `OptionsValidationException` and `StorytellerConfigurationException` per subject and logs the latter's message, which names keys and types only. The bullets under the worker say why. The Named options section now says that "in step with the watchers" holds for a watcher that is not behind, how to bind an event's own document, that a document that does not bind leaves the subject without options until one that binds arrives, that the status does not show it, and that a watcher handles a failure per subject. The Hosting package readme says so in one line.
- **What the status prints** (`51400328`). "`ToString()` prints the errors by their messages, which name keys and statuses only" was too broad: it prints the failure of the latest poll, the pending subscriptions by their keys and the failing ones as a count, and the messages can carry the endpoint with a network failure's message, the platform's error message and hint, or an exception's type name. The section now says that, and where the per-subscription errors are (`Pending[i].LastError`, `LastError` in `GetAll()`).
- **Test doubles and the interval** (`29d18041`): a "Test doubles" bullet under Snapshots, and the range of the informer's `pollInterval`, as in phases 2 and 3.
- **The deleted list's 400** (`99074b88`): the row of `…/deleted` in `annotation-lifecycle.md`, as in phase 1.
- **These reviews.** Section 13 of the spec asks for one review per phase, committed after its phase, and the branch had none. The six reviews were written after the final review of the whole branch, and record the follow-up and final-review fixes of each phase. They are dated with the day each phase was completed.
