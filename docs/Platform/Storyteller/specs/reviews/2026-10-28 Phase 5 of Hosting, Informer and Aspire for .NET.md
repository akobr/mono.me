# Phase 5 of Hosting, Informer and Aspire for .NET

Linear: [424-91](https://linear.app/424fun/issue/424-91/hosting-featuremanagement-and-aspire-packages-and-the-subscription).

## Overview

Phase 5 covers section 10 of [Hosting, Informer and Aspire for .NET](../2026-10-27%20Hosting,%20Informer%20and%20Aspire%20for%20.NET.md): the Aspire hosting integration `42.Platform.Storyteller.Aspire.Hosting`. An AppHost adds a Storyteller resource with its endpoint, view and machine credential, and `WithReference`, `WithStorytellerKey` and `WithExecution` pass the `STORYTELLER_*` variables to projects, executables and containers, the contract every SDK and `sform` read. The package references `Aspire.Hosting` only. A follow-up masks the `STORYTELLER_*` variables a process would inherit from the AppHost, and refuses values that exist only on the AppHost's machine where they cannot work. The final review of the whole branch found nothing in this phase.

| Commit | Content |
|---|---|
| `7070d16e` | Section 10: the project, the resource, the extensions, the rules, the tests |
| `fa953e8f` | Follow-up: masking in run mode, AppHost-only values refused, literal endpoints checked however written |

| Project | Passed (final state of the branch) |
|---|---|
| `Aspire.Hosting/test` (new) | 164: `ContractTests` 67, `CredentialTests` 15, `KeyTests` 23, `ReferenceTests` 35, `ResourceTests` 24 |

## What Was Done

### 10.1 Project

- `Aspire.Hosting/src/Aspire.Hosting.csproj`: assembly `42.Platform.Storyteller.Aspire.Hosting`, root namespace `_42.Platform.Storyteller.AspireHosting`, packable, `InternalsVisibleTo` for its tests, the one reference `Aspire.Hosting`. `Directory.Packages.props` pins `Aspire.Hosting` at 13.1.2, in alphabetical order. The public types are in Aspire's namespaces: the resource in `Aspire.Hosting.ApplicationModel`, the extensions in `Aspire.Hosting`.
- `Aspire.Hosting/test/Aspire.Hosting.UnitTests.csproj` references `Aspire.Hosting`, the src project, `Sdk.NSwag` and `Abstractions.Annotations`, for the comparisons. Both projects are in `42.mono.slnx`.
- The SDK's names, URL rule and key rule are internal copies (`StorytellerVariableNames`, `EndpointRule`, `StorytellerKeyRule`), so an AppHost does not depend on the SDK's version.

### 10.2 The resource

- `StorytellerResource` with `Endpoint`, `Organization`, `Project` and `View`, and the four `AddStoryteller` overloads (a URL, a parameter, a `ReferenceExpression`, an interpolated string handler) as specified. No connection string; excluded from the manifest; initial state `Running` with resource type `Storyteller` and the endpoint as a property (the URL, or the expression's text, never a resolved parameter).
- **Endpoint rule.** A literal URL is checked with the copied SDK rule: absolute `https` without user info, query or fragment, or `http` to `localhost` or a loopback address literal. The message names the parameter, never the value. **Follow-up** (`fa953e8f`): an expression without parameters or endpoint references is a literal too, however it was built, so a non-constant interpolated string of plain strings, which binds to the handler overload, is checked.
- **Credentials.** `WithApiKey`, `WithClientCredentials` and `WithClientCertificate` as specified: secrets are parameters only; one kind per resource, an API key with a certificate allowed (the SDK's `CertificateAndApiKey`), any other combination throws `InvalidOperationException` at the call that adds it; a certificate path is made absolute against the AppHost directory.

### 10.3 `WithReference`

- An environment callback writes `STORYTELLER_ENDPOINT` and the credential's variables; parameters stay parameters, so Aspire resolves them at start and a manifest holds their references. A reference relationship is added, and a second `WithReference` to a Storyteller resource replaces the first, its relationship included.
- **Follow-up: masking in run mode** (`fa953e8f`). DCP starts a project or an executable with the AppHost's environment, so a `STORYTELLER_*` set there, for `sform` say, would mix with the referenced machine. In run mode, for projects and executables, every other name the SDK reads (`STORYTELLER_API_URL`, `STORYTELLER_KEY` and every credential name, the not offered and deprecated ones included) is written empty, which every SDK reads as unset. A value another callback wrote, such as the key, is kept. A container and a published process inherit nothing, so nothing is masked for them. The provider's own settings and `sform`'s `STORYTELLER_PROJECT` and `STORYTELLER_VIEW` are not masked; `aspire.md` says so.
- **Follow-up: values of the AppHost's machine** (`fa953e8f`). The spec refused a certificate for a container. A client certificate and a literal loopback endpoint are now refused for a container and, in publish mode, for every destination, as a deployed process runs elsewhere. A certificate added after `WithReference` is checked again in the callback. Messages name resources, never values.

### 10.4 `WithStorytellerKey` and `WithExecution`

As specified: a full key is used as written; an annotation key is prefixed in the callback with the view of the referenced resource, so the order of the calls does not matter; the last call wins, with one callback per destination, so an earlier key never fails a start; both forms are checked with the copied key rule at the call; a short key without a referenced resource that has a view throws `InvalidOperationException` from the callback, naming `WithView` and `WithReference`. `WithExecution` checks that each name is one segment.

### 10.5 What stays out

No client package, no change to `src/Platform/Aspire.Host/src/AppHost.cs`, no token-file or federated credentials.

### 10.6 Tests

Through `ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig().BuildAsync(…)`, without DCP, for a project, an executable and a container (`TestAppHost`), in run and publish mode, asserting the `STORYTELLER_*` and `ConnectionStrings__*` names only, without their order (`VariableAssertions`, `fa953e8f`). `ContractTests` compares the internal names with `StorytellerVariables.ENDPOINT`, `KEY` and `Credentials` minus the listed names that are not offered, ties the offered credential names to what the resources write, and checks the key rule against `FullKey.TryParse` and the endpoint rule against the SDK's over tables. The other cases of the spec's list are covered by `CredentialTests`, `KeyTests`, `ReferenceTests` and `ResourceTests`.
