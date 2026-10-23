# Phase 1 of the Configuration Provider for .NET

Linear: [424-85](https://linear.app/424fun/issue/424-85/create-config-provider-for-net-and-c).

## Overview

Phase 1 covers sections 1-4 of [Configuration Provider for .NET](../2026-10-23%20Configuration%20Provider%20for%20.NET.md), and the SDK readme parts of section 16: the prerequisites of the provider. `FullKey` parses the eight-segment key of a unit of execution. The C# SDK gains conditional reads of configurations, machine credentials with a token cache and a credential handler, and an opt-in resilience pipeline. The TypeScript SDK gains conditional reads. Nothing in `sform` or the server changes. The final code review of the whole branch found five defects in this phase's code and package; their fixes are listed under "Review fixes".

| Commit | Content |
|---|---|
| `b2409bf2` | The spec |
| `cf898f6b` | Section 1: eight-segment full keys |
| `13bc3d89` | Sections 2 and 3: conditional reads, credentials and resilience in the C# SDK |
| `b535eac3` | Section 4: conditional reads in the TypeScript SDK |

| Project | Passed (final state of the branch) |
|---|---|
| `Backend.Core/test` | 893 |
| `Sdk.NSwag/test` (new) | 199 |
| `Cli/test` | 793 |
| `sdk.typescript/src` (Vitest) | 139, 1 skipped |

## What Was Done

### 1. Full keys with eight segments

- `Abstractions.Annotations/src/FullKey.cs`: `TryParse` and `Parse` accept 5 to 8 segments. `AnnotationKey.IsValid` still decides the count per type, so an 8-segment key whose annotation is not a `uxe` fails. Any empty segment fails, not only an empty organization, project or view.
- A private `TryParse(string?, out FullKey, out string error)` carries the reason. `Parse` throws `FormatException` with it, for example `'xyz' is not an annotation type code.`
- New `Backend.Core/test/FullKeyTests.cs`, with Shouldly: the parts of a 7-segment `exe` and an 8-segment `uxe` key, the rejected shapes of the spec, the reason in each `FormatException`, and the round trip of `FullKey.Create(…).ToString()` for every annotation type.

### 2. Conditional reads in the C# SDK

- `Sdk.NSwag/src/ApiSdk.Conditional.cs` adds `GetConfigurationConditionalAsync` and `GetResolvedConfigurationConditionalAsync` to the partial `IConfigurationsApiClient` and `ConfigurationsApiClient`, as specified: the route and escaping of the generated methods, the hook order (`PrepareRequest` with the builder, `RequestUri`, `PrepareRequest` with the URL, `ResponseHeadersRead`, `ProcessResponse`), `If-None-Match` through `TryAddWithoutValidation`, and the generated exceptions and messages for every other status. The methods are `virtual`, and the client keeps no state.
- `Sdk.NSwag/src/ConditionalResponse.cs` has the members of the spec's table. It has a public constructor, so a test or a fake client can build one.
- **Headers** are copied from `Headers.NonValidated` and `Content.Headers.NonValidated`, case-insensitive. The parsed `CacheControlHeaderValue` would reorder the directives, and the spec asks for the raw value. `IsNoStore` matches the `no-store` directive by name.
- `Sdk.NSwag/README.md` names the members of the generated code that `ApiSdk.Conditional.cs` relies on, for the next regeneration.

### 3. Credentials and resilience in the C# SDK

**Credentials** (`Sdk.NSwag/src/Credentials`, namespace `_42.Platform.Storyteller.Sdk.Credentials`):

- `StorytellerCredential` with `GetAuthorizationAsync(rejected, …)`, `ClientCertificate`, `CanRenew`, `WithClientCertificate`, `Bind` and the factories of the spec. The kinds are internal: `ApiKeyCredential`, `CertificateCredential`, `TokenFileCredential`, and the two that request tokens, `ClientCredentialsCredential` and `FederatedTokenCredential`, which share `CachedTokenCredential`.
- `StorytellerCredentialBinding`, `ClientCredentialsOptions` and `FederatedTokenOptions` (records), `StorytellerClientCertificate.Load`, `StorytellerAuthenticationException` with `Reason` and `IsTransient`, and `StorytellerVariables` with the variable names.
- `EnvironmentCredential` reads the table of the spec, with the `sform` names as fallbacks. `EnvironmentVariables` trims values, treats an empty value as unset, reports a name and its fallback that disagree, and remembers names, never values. `UnboundTarget` reads the endpoint and the project of an unbound credential from the environment.
- `TokenEndpointClient` sends token and discovery requests through its own `HttpClient`: no redirects, no `Authorization`, and the resilience pipeline with `RetryUnsafeMethods = true`. An open circuit names the last failure that led to it.
- `StorytellerCredentialHandler` and `AddStorytellerCredential`, as specified. The renewed request is the same message, as retry handlers send it.
- `LocalUrlRule` (internal) implements the local URL rule. It reads the host as written, because `Uri` reports `loopback` as `localhost`.
- **Token cache, beyond the spec:**
  - a token that lives less than two minutes is renewed halfway through its life instead of 60 s before it expires;
  - callers that waited for a request share its token or its failure;
  - an early renewal that fails keeps the token the API still accepts until it expires.
- `WithPrimaryHandler` (internal) is the test seam that the spec calls the handler override.

**Resilience** (`Sdk.NSwag/src/Resilience`):

- `StorytellerResilienceOptions`, `StorytellerResilience.CreatePipeline` and `AddStorytellerResilience`, with the defaults of the spec.
- **Deviation: the circuit breaker sits outside the retries.** It counts the outcome of a call after its retries, not every attempt. With the breaker inside, the four attempts of one failing call would open it at the minimum throughput of 3 and block every other request of the client. The attempt timeout is innermost, as in the standard handler.
- **Deviation: `501` and `505` are definitive.** The default predicate of `Microsoft.Extensions.Http.Resilience` retries and counts every `5xx`. The pipeline neither retries nor counts these two: a retry gets the same answer, and a key that the server cannot serve must not open the circuit for the others. A connect timeout of the handler, a cancellation the caller did not request, is transient; the predicate overload that includes it is experimental, so the check is repeated.
- **Package.** `Microsoft.Extensions.Http.Resilience` moved from 10.0.5 to 10.1.0 in `Directory.Packages.props`, the version that restored anyway (NU1603). `Microsoft.Extensions.TimeProvider.Testing` 10.1.0 is new, for the test project.
- `InternalsVisibleTo` for `42.Platform.Storyteller.Sdk.NSwag.UnitTests`.

**Tests** (new `Sdk.NSwag/test`, namespace `_42.Platform.Storyteller.Sdk.UnitTests`, in `42.mono.slnx`): every item of the Phase 1 list in spec section 17, with a recording `StubHttpHandler`, `FakeTimeProvider` and `MockFileSystem`. Beyond the list: the breaker counts calls and not attempts, `501` and `505` are neither retried nor counted, an attempt timeout is retried, and the option types print no user info and survive a relative URL.

**Readme.** `Sdk.NSwag/README.md` describes conditional reads, the credentials and their variables, the handler with `AccessTokenFactory` left null, and resilience, including the advice not to stack it on Aspire's standard handler.

### 4. Conditional reads in the TypeScript SDK

- `lib/fetcher.ts`: the request part of `storytellerFetch` moved to `storytellerRequest`, which returns the response unread. `readBody` is exported within the package. `storytellerFetch` behaves as before, and `fetcher.test.ts` passes unchanged.
- New `lib/conditional.ts`, exported from `index.ts`: `getConfigurationConditional`, `getResolvedConfigurationConditional`, `ConditionalResult<T>` and `ConditionalReadOptions`, with the URLs of the generated helpers. A `304` is handled before the error mapping.
- **Beyond the spec:** an empty `ifNoneMatch` removes an `If-None-Match` that the caller put in `options.headers`, so no stale tag goes out.
- New `test/conditional.test.ts` covers the cases of the spec. The README has a "Conditional reads" section with the cross-origin limits.

## Reviews

- **The spec, before it was committed.** Four reviewers read it through the lenses of fit with the code, .NET configuration semantics, security and HTTP, and coverage of the issue. They reported 51 findings, and a consistency pass of the revised spec 11 more. They changed the spec before `b2409bf2`:
  - a property and a factory both named `ClientCertificate`, which does not compile;
  - credentials that ignored a key set in code, now bound with `Bind`;
  - renewal against the rejected value, so a burst of 401s renews once;
  - classes for credential and resolver failures, and `Optional` limited to reachability, as the issue defines it;
  - the rollback of a document that does not bind, and sequence numbers when publishing;
  - the trust and protection rules of the cache;
  - a resolver contract that is neutral about vaults;
  - the AuthKit discovery check, and one definition of the local URL rule.
- **The C# SDK.** One reviewer found five defects, all fixed before `13bc3d89`:
  - the circuit breaker opened on the retries of a single call, which is why it now sits outside them;
  - `501` was retried;
  - a token that lives a minute or less was never fresh;
  - `ToString()` printed user info;
  - every `Bind` created a connection pool. This one is documented rather than changed: bind once and reuse the copy.
- **The TypeScript SDK.** One reviewer found five small issues, all fixed before `b535eac3`:
  - an empty `ifNoneMatch` let a caller's header through;
  - the precedence was tested with a plain object only;
  - two README sentences, about `AbortSignal.timeout` and `X-2S-Secrets-Withheld`;
  - a comment on `storytellerRequest`.

## Review fixes

The final review of the branch found these in Phase 1 code. All are fixed and tested, in `240d75be`:

1. **`FullKey.Parse(null)`** threw `FormatException` with the message "must start with '@'". It throws `ArgumentNullException` again, the .NET convention for `Parse`. `FormatException` stays for malformed text. Test: `Parse_Null_ThrowsArgumentNullException`.
2. **An endpoint with a query or fragment** passed the SDK's local URL rule. Paths are appended to an endpoint, so a token request went to `https://host/api?x=1/v1/…`, and the user got a `Rejected` token request instead of an error about the endpoint. New `LocalUrlRule.RequireEndpoint` also refuses a query or fragment, with the provider's message. It checks the binding's endpoint, `ClientCredentialsOptions.Endpoint`, `FederatedTokenOptions.Endpoint` and the `STORYTELLER_ENDPOINT` of an unbound credential. `LocalUrlRule` and the provider's copy, `LocalUrl`, now point to each other. `SettingsReaderTests.TheLocalUrlRule_IsTheOneOfTheSdkCredentials` pins that the provider and the credentials accept the same endpoints.
3. **Negative resilience values were skipped silently.** A `MaxRetryAttempts` of `-1` or an `AttemptTimeout` of `-5s` turned the retries or the attempt timeout off without an error. `CreatePipeline` now throws `ArgumentOutOfRangeException` for both. Zero and `Timeout.InfiniteTimeSpan` still turn them off.
4. **`StorytellerResilienceOptions.Clone()` is public.** The provider copied the options property by property, so a property added later would have been dropped. Test: `Clone_CopiesEveryProperty`.
5. **Package metadata.** `42.Platform.Storyteller.Configuration` depends on `42.Platform.Storyteller.Sdk.NSwag`, which was packed with the description "Package Description" and no readme. `Sdk.NSwag.csproj` now sets `IsPackable`, `Description` and `PackageTags`, and the new `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md` goes into the package.

The SDK readme also says now that a token request answered `501` or `505` is not retried, and that a negative resilience value throws.
