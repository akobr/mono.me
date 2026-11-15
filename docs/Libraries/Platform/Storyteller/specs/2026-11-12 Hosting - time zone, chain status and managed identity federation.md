# Hosting: time zone, chain status and managed identity federation

Linear: [424-256](https://linear.app/424fun/issue/424-256/hosting-time-zone-chain-status-and-managed-identity-federation). Related: [424-214](https://linear.app/424fun/issue/424-214/hosting-who-am-i-for-any-annotation-and-a-catalog-and-informer-for-any) (who am I and the catalog for any annotation), [424-46](https://linear.app/424fun/issue/424-46/add-workload-identity-federation) (workload identity federation).

Found in the [investigation of the khosmos migration](2026-11-12%20Migration%20of%20the%20khosmos%20subscriptions%20to%20the%202S%20Platform.md), gaps 1.1 to 1.3. Three small gaps keep an application that serves many subscriptions from using Hosting as it is. Each one is general, not specific to khosmos.

## Problem

1. **Hosting does not show the time zone.**
   - Every annotation stores `TimeZone`: "the zone in which that window, or a unit's schedule, is interpreted" ([annotations](../../../../42for.net/platform/annotations.md)).
   - The snapshots of Hosting leave it out, so a scheduler that runs a job per subject in the subject's local time cannot read it.
   - Nothing says which time zone applies when only an ancestor has one. The khosmos scheduler groups its runs by a time zone that the run inherits from its instance, customer, usage or application.
2. **The status of a subscription ignores the subject and the context.**
   - `GetSubscriptionStatus(now)` and the informers count the key's annotation, its execution and its usage.
   - Disabling a subject (a tenant), a context (one of its environments) or a responsibility stops none of its executions.
   - An application that wants those to count has no way to say so.
   - Who am I is the only read of an annotation together with its ancestors, and it reads the key of the options alone. A worker cannot get the ancestors of the executions it lists without reading them one by one.
3. **An Azure workload cannot federate with its managed identity.**
   - The server trusts Azure managed identity tokens for `Federated` machines (424-46, [Azure managed identity](../../../../Platform/Storyteller/authentication.md#azure-managed-identity)).
   - The SDK takes the platform token from a file, or from the GitHub Actions runner, and from nothing else.
   - So an Azure Function, an App Service or a VM needs a script that writes the token to a file and renews it, or a client secret.

## Current State

- **Snapshots.**
  - `StorytellerAnnotation` (`src/Libraries/Platform/Storyteller/Hosting/src/StorytellerAnnotation.cs`) has `Key`, `AnnotationKey`, `Type`, `Name`, `Title`, `Description`, `Labels`, `Values`, `IsDisabled`, `ValidFrom`, `ExpiresAt`, `Created`, `Modified` and `DeletedAt`. There is no `TimeZone`.
  - Its copy constructor (line 31) copies each member, and `Reading/AnnotationMapper.Map` fills them from the SDK's `Annotation`.
  - The SDK's `Annotation` has `TimeZone` as a `string` (`src/Platform/Storyteller/Sdk.NSwag/src/ApiSdk.g.cs`). The server stores it as text, without checking it.
- **The time zone in the admin UI.** A new annotation starts with the project's default time zone, which the browser keeps (`UTC` until changed, [admin UI](../../../../42for.net/platform/admin-ui.md)). The API stores no project settings yet. Imported and API-created annotations keep `TimeZone` null unless it is set.
- **Who am I.**
  - `Executions/ExecutionContextService.ReadAsync` (line 230) reads the key with `includeInactive` and `includeDeleted`. It then reads the ancestors from `AnnotationKey.GetAllAncestorKeys()` (`Abstractions.Annotations/src/AnnotationKeyExtensions.cs`, line 32) at once.
  - A `403 AnnotationRestricted` puts the ancestor in `Unreadable`, a `404` leaves it out, and any other failure fails the read.
  - The result is a `StorytellerExecutionContext`.
- **The status.** `StorytellerExecutionContext.GetSubscriptionStatus(now)` (line 84):
  - `Deleted` when the key's annotation or its execution is deleted;
  - for keys other than `exe` and `uxe`, the status of the key's annotation;
  - otherwise the first status that is not active among the key's annotation, its execution and its usage;
  - `Unknown` when the usage (or the execution of a `uxe`) is unreadable.
  - `StorytellerAnnotation.GetStatus(now)` counts the annotation alone.
- **The catalog.**
  - `IStorytellerCatalog` has `GetAsync`, `GetAsync<T>`, `ListAsync(target)` and `WatchAsync(target, pollInterval)`. `Catalog/StorytellerCatalog` implements it.
  - `ListAsync` (line 65) reads the target's ancestor, the pages of the descendants, then each descendant that passes the filters, at most 4 at once.
  - `Informer/DescendantPollingSource` diffs list fingerprints and point-reads what changed. `PollReads.ReasonOf` gives a removal reason from the descendant's own status.
  - `StorytellerRemoval(AnnotationKey, Reason)` names the annotation that ended a subscription: the execution or the usage, or for a target the descendant itself.
- **The subscriptions.** `ISubscriptionCatalog.WatchExecutionsAsync` and `AddSubscriptions` join the executions with their usages, and count those two only (`Informer/PollingSubscriptionSource`).
- **Federation in the SDK.**
  - `FederatedTokenOptions` (`src/Platform/Storyteller/Sdk.NSwag/src/Credentials/FederatedTokenOptions.cs`) has a `required` `SubjectTokenPath` (line 25).
  - `FederatedTokenCredential` asks a `PlatformTokenSource` before every exchange: a `FileSource`, or a `GitHubActionsSource` that requests the runner's ID token for the audience of the token endpoint.
  - `StorytellerSubjectTokenSource` has `File` and `GitHubActions`.
  - `StorytellerCredentialVariables.ReadFederated` (line 226) makes a federated credential from `STORYTELLER_CLIENT_ID` with `STORYTELLER_TOKEN_FILE` or the GitHub Actions variables.
  - `StorytellerUrlRule` allows `http` only for a loopback address.
  - The SDK depends on no Azure package.
  - For Azure, [the docs](../../../../Platform/Storyteller/authentication.md#azure-managed-identity) show `curl` against the managed identity endpoint. The rule's audience is that of the Storyteller app registration (`<appId>` or `api://<appId>`), not the token endpoint, and the subject token type is `access_token`.

## Proposed Changes

Every change is additive. Applications, their test doubles of `IStorytellerCatalog` and `IExecutionContext`, and the existing credentials keep working.

### 1. The time zone

#### 1.1 `TimeZone` on the snapshots

- `StorytellerAnnotation.TimeZone` (`string?`, `init`): the zone as stored, an IANA or a Windows id. Hosting does not check or convert it. `TimeZoneInfo.TryFindSystemTimeZoneById` takes both on .NET with ICU.
- `AnnotationMapper.Map` sets it from `Annotation.TimeZone`, and a blank value becomes `null`.
- The copy constructor copies it, so `WithDeletedAt` and the typed snapshots keep it.
- `GetStatus(now)` does not change: `ValidFrom` and `ExpiresAt` are instants, and the zone only says how to show and enter them.

#### 1.2 The chain of a key

A new member of `StorytellerExecutionContext`:

```csharp
// The key's annotation and its ancestors that were read, nearest first. The order of Chain below.
public IReadOnlyList<StorytellerAnnotation> Chain { get; }
```

The **chain order** puts the nearer annotation first. At the same level, the place comes before the capability (context, then usage, then unit), and the subject before the responsibility:

| Key | Chain |
| --- | --- |
| `uxe.S.R.C.U` | `uxe`, `exe`, `cnt`, `usg`, `unt`, `sbt`, `rst` |
| `exe.S.R.C` | `exe`, `cnt`, `usg`, `sbt`, `rst` |
| `usg.S.R` | `usg`, `sbt`, `rst` |
| `cnt.S.C` | `cnt`, `sbt` |
| `unt.R.U` | `unt`, `rst` |
| `rst.R`, `sbt.S` | the key alone |

- `Chain` is computed from `Annotation` and `Ancestors`. An unreadable or missing ancestor is left out.
- A test double built with an initializer gets it too.

#### 1.3 The effective time zone

```csharp
// The first TimeZone set along Chain; null when none is set or read.
public string? GetEffectiveTimeZone();
```

- **It reads nothing.** An ancestor in `Unreadable` is skipped, so a restricted machine can get another answer than an unrestricted one, or `null`.
- **The fallback is the caller's**, for example `?? "UTC"`. The project's default time zone is not in the API.
- **It is not the configuration's rule.** A scalar of the configuration takes the subject after the usage, and the responsibility after the unit ([inheritance.md](../../../../Platform/Storyteller/inheritance.md#what-an-ancestor-changes)). A time zone takes the nearest annotation that sets one.
- **The difference from khosmos.** khosmos takes the customer before the usage. The migration flags the runs whose usage and customer set different zones.

### 2. A status over the whole chain

#### 2.1 The scope of a status

```csharp
public enum StorytellerStatusScope
{
    // The key's annotation, its execution and its usage: today's rule.
    Subscription = 0,

    // The key's annotation and every ancestor of its chain.
    Chain,
}
```

#### 2.2 Who am I

New members of `StorytellerExecutionContext`. `GetSubscriptionStatus(now)` stays as it is and means `Subscription`.

```csharp
public StorytellerSubscriptionStatus GetSubscriptionStatus(DateTimeOffset now, StorytellerStatusScope scope);

// The annotation that decides a status other than Active and Unknown, in the order of the scope; null otherwise.
public StorytellerAnnotation? FindInactiveAnnotation(DateTimeOffset now, StorytellerStatusScope scope);
```

With `Chain`:

1. `Deleted` when the key's annotation or its execution is deleted, as today. A deleted ancestor deletes its descendants, and a `404` leaves it out of the read.
2. Otherwise the status of the first annotation of `Chain` that is not active: `Disabled`, `Scheduled` or `Expired`.
3. Otherwise `Unknown` when an ancestor of the chain is in `Unreadable`.
4. Otherwise `Active`.

- **Every key type counts its ancestors.** `Subscription` gives the status of the key's own annotation for keys other than `exe` and `uxe`. `Chain` counts the ancestors of those too: a usage under a disabled subject is `Disabled`.
- **`FindInactiveAnnotation`** gives the deleted annotation for `Deleted`, so a log can name it: "exe.qqm.jobs.dev is not run: sbt.qqm is disabled".

#### 2.3 Any key with its ancestors

A new member of `IStorytellerCatalog`, who am I for any key, read on every call:

```csharp
// The annotation of the key and its ancestors, as who am I reads them; null when the key does not exist or is deleted.
Task<StorytellerExecutionContext?> GetWithAncestorsAsync(string key, CancellationToken cancellationToken = default);
```

- **Keys and failures** are those of `GetAsync`: a full key, or an annotation key in the scope of the options' `Key`.
- **The reads** follow who am I's rules: the key and its ancestors at once, with `includeInactive`. A restricted ancestor goes to `Unreadable`, a missing one is left out, and any other failure throws.
- **`ReadAt`** is when the read started.
- **The shared reader.** The real catalog shares the reads of `ExecutionContextService`, which move to a new `Reading/AncestorReader`. `ExecutionContextService` keeps its own rules: the deleted key, the cache and the refresh.
- **A default interface implementation** reads the key and each ancestor with `GetAsync`, so a test double compiles unchanged and gets it for free. It knows no restrictions, because `GetAsync` throws on a `403`, and the docs say so.

#### 2.4 A target with its ancestors

```csharp
// ListAsync, and a context of each descendant with its ancestors. Each distinct ancestor is read once.
Task<IReadOnlyList<StorytellerExecutionContext>> ListWithAncestorsAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
    where TAnnotation : StorytellerAnnotation;
```

- **Reads.** It lists as `ListAsync` does. Then it reads every distinct ancestor of the listed descendants once, at most 4 at once, and builds a context per descendant, by key.
- **Cost.** The executions of `rst.R` under 10 subjects in 20 contexts cost the list, 20 point reads of the executions, and 1 + 10 + 10 + 20 reads of the responsibility, subjects, usages and contexts. One call gives every subscription with its chain status and its time zone.
- **A restricted ancestor** goes to `Unreadable` in each context that has it, as in who am I.
- **A default interface implementation** builds on `ListAsync` and `GetAsync`, as in 2.3.

#### 2.5 Targets that require active ancestors

```csharp
public StorytellerTarget<TAnnotation> RequiringActiveAncestors();

public bool RequireActiveAncestors { get; }
```

`IncludingInactive()` decides about the descendant's own status, and this decides about its ancestors. The two combine.

- **In `ListAsync` and `ListWithAncestorsAsync`**, a descendant is kept when every ancestor of its chain is active at `now`. The ancestors are read as in 2.4.
  - A missing ancestor leaves its descendants out, because they were deleted with it.
  - A restricted ancestor fails the call with a definitive `StorytellerConfigurationException`: the requirement cannot be decided. The message says that requiring active ancestors needs a machine that may read them: an unrestricted one for the executions of a responsibility, whose subjects and contexts are not under it.
- **In `WatchAsync`**, every poll reads the distinct ancestors of the held and the listed descendants again, after the list and before the diff.
  - **Removed.** A held descendant whose ancestor is inactive or gone leaves as `Removed`, with `StorytellerRemoval(ancestor's annotation key, reason)`.
  - **Added.** A listed descendant whose ancestors are all active again comes back as `Added`.
  - **A failed ancestor read** keeps the descendants that depend on it as they are, with a warning, as a failed point read does today.
  - **A definitive failure before the first completed poll**, a restricted ancestor included, ends the watch as the other failures of a target do. After a completed poll it is logged and polled again.
  - **The order of events** is unchanged: `Removed`, then `Changed`, then `Added`, each by key.
  - **Cost.** One point read per distinct ancestor per poll. An ancestor's change moves no fingerprint of its descendants, so there is nothing to diff it by.
- **`ToString()`** adds ", under active ancestors".
- **`StorytellerRemoval`** and its documentation name the ancestor as well: "the annotation that ended it: the descendant, or for a target that requires active ancestors, one of them".
- **Without the option**, nothing changes.

#### 2.6 Not in this change

`WatchExecutionsAsync` and `AddSubscriptions` keep today's rule. A `StatusScope` option for them can reuse the ancestor reads of 2.5, as a later issue. Nothing asks for it yet: a worker of many subjects in many contexts uses `IStorytellerCatalog` (see the investigation).

### 3. Federation with an Azure managed identity (SDK)

#### 3.1 A built-in source

New options in `Sdk.NSwag/src/Credentials/`:

```csharp
public sealed record AzureManagedIdentityTokenOptions
{
    // The identifier URI of the app registration that stands for Storyteller, api://<appId>.
    public required string Resource { get; init; }

    // The client id of a user-assigned identity; the system-assigned one when null.
    public string? ManagedIdentityClientId { get; init; }
}
```

- **`FederatedTokenOptions.AzureManagedIdentity`** (`AzureManagedIdentityTokenOptions?`) is a third source, next to `SubjectTokenPath` and `SubjectTokenProvider` (3.3).
- **`FederatedTokenOptions.ForAzureManagedIdentity(clientId, resource, managedIdentityClientId = null)`** returns options with `SubjectTokenType = AccessTokenType`.
- **The request**, made with the SDK's own HTTP client and no Azure package:
  - With `IDENTITY_ENDPOINT` and `IDENTITY_HEADER` set (App Service, Functions, Container Apps): `GET {IDENTITY_ENDPOINT}?api-version=2019-08-01&resource={resource}[&client_id={id}]`, with `X-IDENTITY-HEADER`.
  - Otherwise IMDS (virtual machines and scale sets): `GET http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource={resource}[&client_id={id}]`, with `Metadata: true`.
  - It takes the answer's `access_token`, follows no redirect, and sends the identity header to that endpoint only. The header is never logged or printed.
- **The URL rule.** The two endpoints are exempt from `StorytellerUrlRule`. `IDENTITY_ENDPOINT` may be `http` on a loopback host, or `https`. IMDS is the fixed literal.
- **Failures.** A network failure, a timeout, `408`, `429` or `5xx`, after the retries, is `Unavailable`. Another `4xx`, or an answer without a token, is `Rejected`. The message names the endpoint without its query and the resource, and asks whether the host has a managed identity and whether the resource is the identifier URI of the Storyteller app registration.
- **When it asks.** The source is asked before every exchange, as the GitHub runner is. The managed identity endpoint caches its token, and an exchange happens only when the Storyteller token is renewed.
- **The audience** of the machine's rule is the app registration's (`<appId>` with token version 2, `api://<appId>` with 1), as [authentication.md](../../../../Platform/Storyteller/authentication.md#azure-managed-identity) says. It is not the token endpoint. The server does not change.
- **Its name.** `StorytellerSubjectTokenSource.AzureManagedIdentity`. `ToString()` names the kind, the client id, the source, the resource and the managed identity client id, which are part of the provider's cache identity.
- **Other Azure hosts.** AKS keeps the Kubernetes path. Azure Arc, Service Fabric and Cloud Shell use 3.3 with Azure.Identity.

#### 3.2 Variables

| Variable | Meaning |
| --- | --- |
| `STORYTELLER_MANAGED_IDENTITY_RESOURCE` | With `STORYTELLER_CLIENT_ID`, a federated machine that gets its platform token from the managed identity, for this resource (`api://<appId>`) |
| `STORYTELLER_MANAGED_IDENTITY_CLIENT_ID` | Optional: the client id of a user-assigned identity |

- **One kind.**
  - The resource with a token file or a client secret is an error that names them.
  - The resource without `STORYTELLER_CLIENT_ID` is an error.
  - `STORYTELLER_MANAGED_IDENTITY_CLIENT_ID` without the resource is an error.
  - The GitHub Actions variables are ignored when the resource is set.
- **`STORYTELLER_SUBJECT_TOKEN_TYPE`** is optional and defaults to `access_token` for this source.
- **The constants** go to `StorytellerVariables`, and `StorytellerCredentialVariables.Names` lists the variables.
- **Who gets it.** The provider, Hosting, `IStorytellerDocuments` and `sform` all read the environment through the SDK, so all of them get it. An Azure Function needs only its app settings:

```text
STORYTELLER_ENDPOINT=https://api.example.com/api
STORYTELLER_KEY=@acme.billing.default.rst.invoicing
STORYTELLER_CLIENT_ID=<machine id>
STORYTELLER_MANAGED_IDENTITY_RESOURCE=api://<appId>
```

#### 3.3 Any platform from code

```csharp
public delegate ValueTask<string> StorytellerSubjectTokenProvider(StorytellerSubjectTokenRequest request, CancellationToken cancellationToken);

// Audience: the token endpoint, as the GitHub runner gets it; a provider whose rule names another audience ignores it.
public sealed record StorytellerSubjectTokenRequest(string Audience, string ClientId);
```

- **`FederatedTokenOptions.SubjectTokenProvider`** (`StorytellerSubjectTokenProvider?`) is asked before every exchange, and the token it returns is trimmed. `SubjectTokenPath` is no longer `required`.
- **Exactly one source.** The options take exactly one of `SubjectTokenPath`, `SubjectTokenProvider` and `AzureManagedIdentity`. Otherwise the factory throws `ArgumentException`: "Set exactly one of SubjectTokenPath, SubjectTokenProvider and AzureManagedIdentity."
- **Failures of the provider:**
  - a `StorytellerAuthenticationException` passes as it is;
  - an `OperationCanceledException` that the caller did not request, a `TimeoutException` or an `HttpRequestException` is `Unavailable`;
  - an empty token, or any other exception, is `Rejected`, with the exception's type name in the message and the exception as the inner one.
- **Its name.** `StorytellerSubjectTokenSource.Provider`. `ToString()` cannot see the delegate, so two providers of one machine share a cache identity.
- **The factories.** A new `StorytellerCredential.FromFederatedToken(options)` takes any source. `FromFederatedTokenFile` stays, and takes any source too.

With Azure.Identity, for the hosts that 3.1 does not cover:

```csharp
var azure = new DefaultAzureCredential();

options.Credential = StorytellerCredential.FromFederatedToken(new FederatedTokenOptions
{
    ClientId = machineId,
    SubjectTokenType = FederatedTokenOptions.AccessTokenType,
    SubjectTokenProvider = async (request, cancellationToken) =>
        (await azure.GetTokenAsync(new TokenRequestContext([$"api://{appId}/.default"]), cancellationToken)).Token,
});
```

No new package: the example is documentation.

#### 3.4 Not in this change

The TypeScript SDK. The server, whose Azure issuers and rules exist already.

### 4. Documentation

- `docs/Platform/Storyteller/hosting.md`:
  - the snapshot table (`TimeZone`);
  - who am I (`Chain`, `GetEffectiveTimeZone`, the status scope table, `FindInactiveAnnotation`);
  - the catalog (`GetWithAncestorsAsync`, `ListWithAncestorsAsync`, `RequiringActiveAncestors`, the removal by an ancestor);
  - "Cost and limits", and "Scopes and restricted machines" (requiring active ancestors needs a machine that reads them).
- `docs/Platform/Storyteller/configuration-provider.md`: the credential variables and kinds.
- `docs/Platform/Storyteller/authentication.md`:
  - the variables of `sform`;
  - the Azure managed identity section, where the variables replace `curl`, with a user-assigned identity and the rule;
  - the federated credentials from code.
- `src/Platform/Storyteller/Sdk.NSwag/README.md`: "Workload identity federation".
- `docs/Libraries/Platform/Storyteller/Hosting/package-readme.md`: one line each for the time zone and the chain status.

### 5. Tests

- **Hosting** (`Hosting/test`, over `ScriptedPages` and the reader's fakes):
  - the mapper with a set, null and blank `TimeZone`, and a copy that keeps it;
  - `Chain` for every key type, with unreadable and missing ancestors;
  - `GetEffectiveTimeZone` along each chain;
  - `GetSubscriptionStatus` and `FindInactiveAnnotation` with `Chain`: a disabled subject, a scheduled context, an expired responsibility, an unreadable ancestor (`Unknown`), a deleted execution;
  - `GetWithAncestorsAsync`: a deleted key gives `null`, a restricted ancestor goes to `Unreadable`, an ancestor's `404` is left out, a `500` throws;
  - `ListWithAncestorsAsync`: one read per distinct ancestor, counted;
  - `RequiringActiveAncestors` on `ListAsync`: a disabled subject drops its executions, and a restricted ancestor fails definitively;
  - on `WatchAsync`: a subject that is disabled gives `Removed` with `sbt.S` and `Disabled`, enabled again gives `Added`, and a failed ancestor read keeps the state;
  - the default interface implementations over a test double.
- **SDK** (`Sdk.NSwag/test`):
  - the variables (the managed identity kind, every conflict, the default token type);
  - the App Service request (query, header, no redirect) and the IMDS request;
  - the classification of failures;
  - `ToString()`;
  - the provider (called per exchange, trimmed, the exception mapping);
  - one source exactly;
  - an exchange end to end against the loopback token endpoint of the existing tests.

### 6. Phases

1. **The time zone:** 1.1 to 1.3.
2. **The chain status in who am I and the lists:** 2.1 to 2.4, and 2.5 for `ListAsync` and `ListWithAncestorsAsync`.
3. **The chain status in `WatchAsync`:** 2.5 for watches.
4. **Managed identity federation:** 3.1 to 3.3.

Each phase gets its review in `specs/reviews/`.

## Open questions

1. **The order within a level** (context, usage, unit; subject, responsibility) decides the time zone. Should a unit's time zone, the zone of its schedule, come before the context's for a unit of execution?
2. **The admin UI fills in a time zone.** It gives a new annotation the project's default zone, so an execution created there has its own zone, and the walk stops at it. Is that wanted, or should a filled-in default be told apart from a chosen zone? The API has no marker for it today.
3. **`WatchExecutionsAsync` and `AddSubscriptions`** could get the status scope (2.6), as a later issue.
4. **Is App Service plus IMDS enough** for the built-in source? Other Azure hosts use 3.3.
