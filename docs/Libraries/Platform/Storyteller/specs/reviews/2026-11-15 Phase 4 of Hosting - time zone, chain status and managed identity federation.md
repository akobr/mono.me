# Phase 4 of Hosting: time zone, chain status and managed identity federation

Linear: [424-256](https://linear.app/424fun/issue/424-256/hosting-time-zone-chain-status-and-managed-identity-federation).

## Overview

Executes phase 4, the managed identity federation in the C# SDK (sections 3.1 to 3.3, with their parts of sections 4 and 5), of [Hosting: time zone, chain status and managed identity federation](../2026-11-12%20Hosting%20-%20time%20zone,%20chain%20status%20and%20managed%20identity%20federation.md). A federated machine on an Azure host now gets its platform token from the managed identity of the host: `STORYTELLER_CLIENT_ID` with `STORYTELLER_MANAGED_IDENTITY_RESOURCE`, or `FederatedTokenOptions.ForAzureManagedIdentity` in code. Any other platform gives its token through a delegate, `SubjectTokenProvider`. The SDK makes the managed identity request itself, a plain `GET` to the endpoint of the host or to IMDS, with no Azure package. `sform`, the configuration provider, Hosting and `IStorytellerDocuments` read the environment through the SDK, so all of them get it. Everything is additive: the existing credentials, their messages and their `ToString()` texts did not change, and neither did `StorytellerUrlRule`. The TypeScript SDK and the server are not part of it (3.4). Nothing ran against a real Azure host: the requests ran against the stubs of the tests only. A review of the change confirmed eleven findings. Two changed the code: the managed identity request no longer goes through a proxy, and its hint is a sentence of its own, which an open circuit leaves out. Four changed comments and documents. Five asked for the record of a deviation or of an addition, which this review holds. The change is `8a7a3768`.

## What Was Done

### 3.1 A built-in source

All in `src/Platform/Storyteller/Sdk.NSwag/src/Credentials/`.

- **The options.** `AzureManagedIdentityTokenOptions` is new, in its own file, as the spec has it: `Resource` (required) and `ManagedIdentityClientId`. `FederatedTokenOptions` gained `AzureManagedIdentity` and the static `ForAzureManagedIdentity(clientId, resource, managedIdentityClientId = null)`, which sets `SubjectTokenType` to `AccessTokenType`.
- **`FederatedTokenOptions.ToString()`** names every source that is set, since options are printed before they are checked. A file prints `SubjectTokenPath = {path}` as before. A managed identity prints `SubjectTokenSource = AzureManagedIdentity, Resource = {resource}, ManagedIdentityClientId = {id}`. A provider prints `SubjectTokenSource = Provider`, never the delegate. Without a source the path prints empty, as it always did.
- **Where the host answers.** `AzureManagedIdentityEndpoint` is new and internal. `Read(getVariable)` gives the URL, the API version and the header of the request:
  - with `IDENTITY_ENDPOINT` and `IDENTITY_HEADER` set: that endpoint, `api-version=2019-08-01` and `X-IDENTITY-HEADER`;
  - otherwise IMDS: the literal `http://169.254.169.254/metadata/identity/oauth2/token`, `api-version=2018-02-01` and `Metadata: true`.
  - The two variables go through `StorytellerVariableReader`, so a blank value is unset.
  - Its `ToString()` prints the URL without its query and `IsImds`, never the header.
- **The names of the host's variables** are the constants `StorytellerVariables.IDENTITY_ENDPOINT` and `IDENTITY_HEADER`, next to the GitHub Actions ones. They are not in `Credentials`.
- **The request.** `TokenEndpointClient.RequestAzureManagedIdentityTokenAsync` is modelled on `RequestGitHubIdTokenAsync`. It appends `api-version`, `resource` (escaped) and, for a user-assigned identity, `client_id` to the URL as written. It sends the header of the endpoint and `Accept: application/json`, and takes `access_token` of the answer, trimmed. It follows no redirect.
- **The source.** `FederatedTokenCredential` has the private `AzureManagedIdentitySource`. It reads the endpoint through the credential's `getVariable` before every exchange, not at creation, because the platform rotates the header. `FederatedTokenCredential.Create` gives it the `getVariable` it took already: the one of the variables for `FromEnvironment`, the process environment for the factories of code. `SubjectTokenSource` gives `AzureManagedIdentity`.
- **`ToString()` of the credential** is `StorytellerCredential { Kind = Federated, ClientId = {id}, SubjectTokenSource = AzureManagedIdentity, Resource = {resource}, ManagedIdentityClientId = {id, or empty} }`. The texts of the file and of the GitHub Actions sources did not change.
- **`StorytellerSubjectTokenSource`** gained `AzureManagedIdentity` (2) and `Provider` (3), after the existing values, whose numbers stay.
- **Failures.** A network failure, a timeout, `408`, `429` or `5xx`, after the retries, is `Unavailable`. A redirect, another `4xx`, or an answer without a token is `Rejected`. The message starts with "The managed identity token request to {endpoint without its query} for the resource {resource}", and ends with "Does the host have a managed identity, and is the resource the identifier URI of the Storyteller app registration (api://<appId>)?" The failure of the request itself is the inner exception.
- **A refused exchange** is the token endpoint's own message. The hint of a GitHub Actions job is not added to it, and the spec asks for no hint of its own here.
- **Internal changes that the two new sources needed.** All are private or internal:
  - `PlatformTokenSource` has a `Kind`, which `SubjectTokenSource` returns, and its `GetTokenAsync` takes a `StorytellerSubjectTokenRequest` instead of the audience;
  - `TokenEndpointClient.ReadGitHubIdToken` became `ReadTokenValue(body, name)`, `EnsureSuccess` has an optional `isImds`, and `SendAsync` takes the client it sends with.
- **Deviation: the rule of `IDENTITY_ENDPOINT` is wider than the spec's.** The spec says "`http` on a loopback host, or `https`".
  - **The rule as implemented:** an absolute URL without user info that is `https`, or `http` to `localhost` or to an address literal that is loopback, link-local (`169.254.0.0/16`, `fe80::/10`) or private (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `fc00::/7`). Never `http` to a name that DNS resolves.
  - **The reason** is what the platform sets: `127.0.0.1` on Windows App Service, `localhost` on Container Apps, and a link-local address on Linux App Service, for example `http://169.254.129.5:8081/msi/token`. The spec's rule would refuse the last one.
  - **A check of its own.** It is `AzureManagedIdentityEndpoint.IsAllowed`. The public `StorytellerUrlRule` did not change, and a test pins that it still refuses these addresses.
  - A refused value is a `Configuration` failure: "IDENTITY_ENDPOINT must be an absolute URL without user info: https, or http to localhost or to a loopback, link-local or private address." It names the variable, never the value.
- **Deviation: `404` and `410` of IMDS are `Unavailable`.** The spec says that another `4xx` is `Rejected`.
  - IMDS answers `404` and `410` while it is updating, so they are worth another try later. A host without a managed identity answers `400`, which stays `Rejected`.
  - These two are not retried: the resilience pipeline retries `408`, `429` and `5xx` only. So "after the retries" does not hold for them.
  - `IDENTITY_ENDPOINT` keeps the spec's rule: there `404` and `410` are `Rejected`.
- **Beyond the spec: Service Fabric and Azure Arc fail at once.** The spec has two cases, the endpoint with its header, and "otherwise IMDS". `Read` knows two more hosts, whose managed identity answers another protocol:
  - **Service Fabric:** `IDENTITY_SERVER_THUMBPRINT` is set with the endpoint and the header.
  - **Azure Arc:** `IMDS_ENDPOINT` is set with `IDENTITY_ENDPOINT` and without `IDENTITY_HEADER`.
  - Both are a `Configuration` failure, instead of a request that cannot work: "The built-in managed identity source covers App Service, Functions, Container Apps and IMDS, not {host} ({the variables}); get the platform token in code with FederatedTokenOptions.SubjectTokenProvider, for example with Azure.Identity."
  - The SDK so reads two variables of the host that the spec does not name. They are private constants of `AzureManagedIdentityEndpoint`, not in `StorytellerVariables`.
  - Cloud Shell is not detected: there the source asks IMDS.
  - Open question 4 stays answered with App Service plus IMDS.
- **Left open by the spec: when the variables of the host are checked.** At the exchange, in `AzureManagedIdentityEndpoint.Read`, not in `StorytellerCredentialVariables.Read`, because they are read before every exchange. So a refused `IDENTITY_ENDPOINT`, or a host that the source does not cover, fails the first token request and not the reading of the variables. `sform` ends with exit code `2`, and for the configuration provider the failure is definitive.
- **Beyond the spec: a blank `ManagedIdentityClientId`** in the options is read as `null`, the system-assigned identity, as an empty variable is unset. No `client_id` is sent, and `ToString()` prints nothing after `ManagedIdentityClientId = `.
- **Fixed after review: the request went through the proxy of the token requests.** It was sent on the one `HttpClient` of the token requests, whose handler uses the proxy of the environment or of `ConfigureHandler`. The endpoint is plain `http` on the host or its network, so a proxy got the whole request with `X-IDENTITY-HEADER`, and could not reach the endpoint.
  - `TokenEndpointClient` now has a second, lazily created client for this request. `CreatePrimaryHandler(configure, useProxy: false)` builds its handler: the callback runs, then `AllowAutoRedirect` and `UseProxy` are set to `false`.
  - Both clients share one resilience pipeline, so one circuit breaker, as before.
  - A primary handler of the caller, the seam of tests, serves both clients and is not disposed.
  - The request never goes through a proxy, also over `https`. The proxy still serves the token endpoint.
  - `ConfigureHandler` runs once for each handler, so twice for a credential that asks a managed identity.
- **Fixed after review: the hint was appended to every failure text.**
  - The text of a network failure may end without a period, as "Connection refused (169.254.169.254:80)" does. The hint is now a sentence of its own. Messages that ended with a period are byte for byte as they were.
  - An open circuit now passes without the hint. One breaker serves the host and the token endpoint, so the last failure it names can be the token endpoint's, and the question about the host does not belong to it.
- **Fixed after review: the comments of `StorytellerAuthenticationFailureReason`.** `Rejected` names a redirect, an answer without a token, the exception of IMDS and a provider that failed. `Unavailable` names `404` and `410` of IMDS, without a retry.

### 3.2 Variables

- **The constants** `StorytellerVariables.MANAGED_IDENTITY_RESOURCE` and `MANAGED_IDENTITY_CLIENT_ID` are new, and both are in `Credentials`. So `StorytellerCredentialVariables.Names` lists them.
- **`StorytellerCredentialVariables.Read`:**
  - the resource with `STORYTELLER_CLIENT_ID`, any id, is a `Federated` machine whose `SubjectTokenSource` is `AzureManagedIdentity`;
  - the resource with the token file of a federated machine is an error that names both: "{resource variable} and {file variable} are both set; a federated machine takes its platform token from one of them." The file is named as it was set, also by its deprecated name;
  - the resource with a variable of another kind, such as a client secret, an API key or a bearer token file, is the error of two kinds, which names them;
  - the resource without `STORYTELLER_CLIENT_ID` is the error of a federated machine without its id;
  - `STORYTELLER_MANAGED_IDENTITY_CLIENT_ID` without the resource is an error: "STORYTELLER_MANAGED_IDENTITY_CLIENT_ID is set without STORYTELLER_MANAGED_IDENTITY_RESOURCE." It is told before a missing `STORYTELLER_CLIENT_ID`;
  - the GitHub Actions variables are ignored when the resource is set, and their URL is then not checked;
  - `STORYTELLER_SUBJECT_TOKEN_TYPE` defaults to `access_token` for this source only, and `STORYTELLER_TOKEN_SCOPE` still applies.
- **The variables of the host**, `IDENTITY_ENDPOINT` and `IDENTITY_HEADER`, are no credential variables. Alone they describe no credential, and they make no machine federated: only the resource does. The GitHub Actions variables differ, as they federate a GUID client id on their own.
- **`EnvironmentCredential`** builds the credential with `AzureManagedIdentity` in its options. The resource and the client id are internal members of `StorytellerCredentialVariables`.
- **The comments** of `StorytellerCredentialKind`, `StorytellerSubjectTokenSource`, `StorytellerVariables` and `StorytellerCredentialVariables` name the new source.
- **The existing messages did not change.** So "A federated machine needs a platform token: set STORYTELLER_TOKEN_FILE …" and "STORYTELLER_CLIENT_ID needs STORYTELLER_CLIENT_SECRET …" name a file and a GitHub Actions job, and not the resource.
- **The default of the token type is the spec's.** It belongs to the variable and to `ForAzureManagedIdentity`. Options that set `AzureManagedIdentity` themselves keep the default of `SubjectTokenType`, `jwt`. The server accepts each of the three types.

### 3.3 Any platform from code

- **The delegate and its request** are new, each in its own file, as the spec has them: `StorytellerSubjectTokenProvider` and `StorytellerSubjectTokenRequest(Audience, ClientId)`. The audience is the token endpoint built from the endpoint as written, as the GitHub runner gets it.
- **`FederatedTokenOptions.SubjectTokenProvider`** is new, and `SubjectTokenPath` is a `string?` that is no longer `required`.
- **Exactly one source.** `FederatedTokenCredential.CreateSource` throws `ArgumentException` with the spec's text for no source and for more than one.
  - The exception has no `ParamName`, so that its message is the spec's text and nothing more. The checks next to it name a parameter.
  - A `null` path without another source threw `ArgumentNullException` before, and throws this `ArgumentException` now. A `catch` of `ArgumentException` gets both.
  - A blank path or a blank resource is an `ArgumentException` that names its parameter, `options.SubjectTokenPath` or `options.AzureManagedIdentity.Resource`.
- **The provider source** is the private `ProviderSource`. It is asked before every exchange, and its token is trimmed.
  - A `StorytellerAuthenticationException` passes as it is.
  - An `OperationCanceledException` that the caller did not request, a `TimeoutException` or an `HttpRequestException` is `Unavailable`.
  - Any other exception is `Rejected`.
  - The message of both is "The subject token provider failed with {type name}.", with the exception as the inner one. It never holds the exception's own message, which may hold anything.
  - An empty token is `Rejected`: "The subject token provider returned no token."
  - A cancellation that the caller requested stays a cancellation.
- **`ToString()`** is `StorytellerCredential { Kind = Federated, ClientId = {id}, SubjectTokenSource = Provider }`. It cannot see the delegate, so two providers of one machine share a cache identity.
- **The factories.** `StorytellerCredential.FromFederatedToken(options)` is new and takes any source. `FromFederatedTokenFile` stays, calls it, and takes any source too.

### The consumers of the SDK

- **Aspire.Hosting.** `StorytellerVariableNames` copies the two new names, among those a resource never sets, and `ContractTests` compares them with the SDK's.
- **`sform`** reads the environment through the SDK, so a federated machine signs in with the managed identity. What it prints is beyond the spec, which names `sform` only as a reader of the variables:
  - `sform account` names the credential variables of the machine, `STORYTELLER_CLIENT_ID` and `STORYTELLER_MANAGED_IDENTITY_RESOURCE`, and never the variables of the host;
  - `MachineAuthenticationService` has a second text of the deadline message. For a machine with a managed identity it ends with "the token endpoint or the managed identity endpoint of the host did not answer, or kept failing." Every other source keeps its text, which names the GitHub Actions runner. `LoadedCredential` gained `SubjectTokenSource` for it;
  - `MachineCreateCommand` prints one more hint for a federated machine, about `STORYTELLER_MANAGED_IDENTITY_RESOURCE` and `STORYTELLER_MANAGED_IDENTITY_CLIENT_ID`. Its "Optional" line no longer tells to set `access_token` for a managed identity: it says that a managed identity defaults to it. Both lines are printed for every federated machine: the rule is not read to guess Azure.
- **The configuration provider.** No code changed but a comment of `CredentialFingerprint`. Its tests pin the texts and the fingerprints of the two new sources: the endpoint of the host and its header are no part of a fingerprint, the resource and the identity are.
- **Hosting** did not change.

### 4. Documentation

- `src/Platform/Storyteller/Sdk.NSwag/README.md`:
  - "Machine credentials": `FromFederatedToken`, the variables and the kinds, and the new section "Azure managed identity" (the request, the host's variables, the resource, the token type, the rule of `IDENTITY_ENDPOINT`, the failures, the other Azure hosts, no proxy, `ToString()`);
  - "Workload identity federation": "The source of the platform token" and "A provider of the platform token", with the Azure.Identity sample of the spec. The sample is documentation: the SDK references no Azure package.
- `docs/Platform/Storyteller/authentication.md`:
  - the variables of `sform`, its kinds, its token requests and its exit codes;
  - "Platform token from an Azure managed identity", under the federation of `sform`;
  - the output of `machine create`, with the hint of an Azure host;
  - the section "Azure managed identity": the rule, and the variables in place of `curl`, with a user-assigned identity. A workload without the C# SDK still finds the two requests there, in one paragraph;
  - "Federated credentials from code" is new.
- `docs/Platform/Storyteller/configuration-provider.md`: the credential variables and kinds, a bullet "Azure managed identity", a bullet "Any platform from code", the app settings of an Azure Function, and the fingerprint of the two new sources.
- `docs/42for.net/cli/sform.md`: the row of the federated machine, a paragraph and the app settings of an Azure host, and the exit codes.
- `docs/Platform/Storyteller/Sdk.NSwag/package-readme.md`: the two lines that list the sources. The spec does not list this file.
- **Fixed after review.**
  - The README said that the subject token type defaults to `access_token` "for this source". It now says where the default applies: the variable, and `ForAzureManagedIdentity`. The row of `AzureManagedIdentity` in `authentication.md` says the same.
  - `configuration-provider.md` said that the credential asks the host for a new token before every exchange. Only the request is new: the host answers with the token it caches. It now says "the identity's token", with that sentence.
  - The README's sentence about failures listed every `4xx` of a managed identity request as `Rejected`. It now names the exception of IMDS, and points to the failures of a provider.
  - The README's note about a proxy told to leave `169.254.169.254` out of it. The three documents now say that the request never goes through a proxy.
  - The README and `authentication.md` say that the message of an open circuit does not ask about the host.

### Tests

- `Sdk.NSwag/test/Credentials`, over `StubHttpHandler` and the `WithPrimaryHandler` seam:
  - `AzureManagedIdentityTokenTests` is new, 71 cases:
    - the App Service request and the IMDS request, with a user-assigned identity;
    - the host's variables read before every exchange, and the header in no other request;
    - an unbound credential that exchanges at the token endpoint of the environment;
    - the options of code;
    - the rule of the endpoint, accepted and refused, next to `StorytellerUrlRule`;
    - the classification of failures with their exact messages;
    - a refused exchange;
    - the hosts the source does not cover;
    - `ToString()`.
  - `SubjectTokenProviderTests` is new, 15 cases: asked per exchange and trimmed, the audience, the mapping of exceptions, an empty token, cancellation, and `ToString()`.
  - `FederatedTokenTests`, 11 cases: exactly one source, a blank source, both factories with every source, the numbers of the sources, `ToString()` of the options, and the handler without a proxy.
  - `StorytellerCredentialVariablesTests`, 21 cases: the managed identity kind for any client id, the default token type, every conflict, the GitHub Actions variables ignored, and the variables of the host.
  - `StorytellerVariablesTests`, 2 cases: `Credentials` leaves out the names of an Azure host. The test of the list has the two new names.
- **After the review**, six of those cases:
  - a proxy of the application serves the token endpoint and never the managed identity request. It runs over two loopback listeners, because the seam replaces the handler that has the proxy. The managed identity request arrives at the host directly with its header, and the exchange arrives at the proxy without it;
  - the handler of the managed identity request uses no proxy, whatever the callback sets, and keeps the rest of the callback;
  - the test of an unreachable endpoint checks the exact message, also for a failure text without a final period;
  - an open circuit after two exchanges that failed with `503` names the token request as the last failure, and does not ask about the host;
  - options that set `AzureManagedIdentity` themselves post `jwt`, unless they set the type, two cases.
- **The consumers:**
  - `Cli/test`: 9 cases in `MachineAuthenticationServiceTests` (the host and IMDS, the unusable answers, the rule of the endpoint, the names of the identity, the deadline message), 5 in `MachineSignInSettingsTests` and 1 in `AccountCommandTests`. `MachineCreateFederatedTests` expects the two lines of `machine create`, and the existing deadline test pins the old text;
  - `Configuration/test`: 2 cases in `CredentialFingerprintTests` and 2 in `SettingsReaderTests`;
  - `Aspire.Hosting/test`: `ContractTests` compares the two new names. No new case.
- **Time.** The tests use the fake time providers they had. No test sleeps. The new deadline test of `sform` has a deadline of zero, so it waits for nothing. The test of the proxy is the one test of the SDK that opens sockets.
- **Not against Azure.** The two requests, with their API versions and headers, and the answers of IMDS ran against `StubHttpHandler` and the scripted handler of the `sform` tests only.
- **Counts.** All pass.
  - The SDK tests went from 377 to 497: 114 with the implementation, and 6 with the fixes after the review.
  - The CLI tests went from 2018 to 2033.
  - The tests of the configuration provider went from 641 to 645. Two of them are for Unix and skip on Windows.
  - Aspire.Hosting has its 164 tests, and Hosting its 562, as before.
  - The coverage checks of `Cli.AutomationTests` pass, 9 of 9.
- **Warnings.** A rebuild of the SDK, the configuration provider, Aspire.Hosting, `sform` and their tests gives no new warning in the files of the phase. `StorytellerCredential.cs` keeps the one it had, SA1202 for `WithPrimaryHandler`, whose place did not change.

### Deviations

- No public member or signature differs from the spec.
- Two rules differ from the spec's sentences, with their reasons in 3.1: the rule of `IDENTITY_ENDPOINT`, which is wider, and `404` and `410` of IMDS, which are `Unavailable` and not retried.
- The spec left open what the sections above decide: when the variables of the host are checked, the `ToString()` texts of the two new sources and of the options, the messages of a provider that failed, the messages of the conflicts of the variables, and the `ArgumentException` without a `ParamName`.
- Beyond the spec:
  - the two hosts that fail at once with a `Configuration` message, Service Fabric and Azure Arc;
  - a blank `ManagedIdentityClientId` read as the system-assigned identity;
  - the managed identity request that never goes through a proxy, on a client of its own;
  - an open circuit without the question about the host;
  - what `sform` prints for a managed identity: the names in `sform account`, the second deadline message and the hints of `machine create`;
  - the SDK's package readme, which names the new sources.
- The spec's test list names "the loopback token endpoint of the existing tests". The exchange end to end runs over the stub handler against `http://localhost:7071/api`, as those tests do. Only the test of the proxy opens sockets.
