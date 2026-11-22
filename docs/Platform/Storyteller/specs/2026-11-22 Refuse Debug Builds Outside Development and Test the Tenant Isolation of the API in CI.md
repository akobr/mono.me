# Refuse Debug Builds Outside Development and Test the Tenant Isolation of the API in CI

Linear: [424-242](https://linear.app/424fun/issue/424-242/arch-debug-builds-turn-off-every-user-tenant-check-with-no-startup).

Line numbers refer to the tree this plan was written against (`20ef5b5a2`). No application code is changed by this document.

## Problem

Since every organization lives in the one shared `organizations` container (424-230), the access checks of the API are the
only thing between tenants. Two things weaken them:

- A Debug build of `Api.Functions` (the compile constant `DEV_AUTH`) decodes bearer tokens without checking their
  signature, skips `User.Impersonation` and the role check of users, gives every user the secret rights and serves requests
  without an identity. Deployed anywhere reachable (a staging slot, a demo, a container built with the wrong
  `BUILD_CONFIGURATION`), it opens the data of every organization. Nothing refuses to start such a build outside a
  developer's machine.
- No workflow builds or tests `Api.Functions`. The unit tests run in Debug by default, where most role refusals return
  early, and the backend's isolation suites (`Backend.CosmosDb/test`, `*IsolationTests`) prove the scoping of the storage,
  not who may reach which organization. A regression of the gate (for example 424-241) would merge with no test failing.

## Current State

### `DEV_AUTH`

- `src/Platform/Storyteller/Api.Functions/src/Api.Functions.csproj:22-24` defines `DEV_AUTH` for `Debug|AnyCPU`.
- What it changes (`src/Platform/Storyteller/Api.Functions/src`):
  - `Security/BearerValidationMode.cs:7-12`: `DecodeWithoutValidation` is true, so `BearerAuthenticationMiddleware.cs:64-82`
    stores the claims of any unsigned token of a provider (any `sub`, any machine client id). Storyteller tokens keep their
    own validator, also in Debug (`:60-62`).
  - `Security/HttpRequestDataExtensions.cs:126-131` (a request without `sub` is `System`), `:182-196` (every user gets the
    secret rights), `:316-318` (`EnsureUser` skips `User.Impersonation`), `:587-593` (the role check is skipped while
    `UserRoleChecks.AreSkipped`).
  - `Security/UserRoleChecks.cs:10-26`: skipped by default in Debug; a test turns it on for its own flow with `Enforce()`.
- `Program.cs:29` applies `ProcessDefaults`, `:31` builds a bare `new HostBuilder()`, `:61` is `ConfigureServices`, `:200`
  `Build()`, `:202` `RunAsync()`. Nothing looks at `DEV_AUTH` at start.

### The host environment

Verified with probe hosts on the repo's worker packages (Worker 2.52.0, Http.AspNetCore 2.1.1) and Core Tools 4.15.2:

- The worker's `IHostEnvironment.EnvironmentName` comes from `ASPNETCORE_ENVIRONMENT`, then `AZURE_FUNCTIONS_ENVIRONMENT`,
  else `Production`. `DOTNET_ENVIRONMENT` is ignored by this host.
- `func start` sets `AZURE_FUNCTIONS_ENVIRONMENT=Development` (unless the shell already has it). Aspire starts the API
  through `dotnet run`, which the Worker SDK turns into `func start`, so local runs are Development. The repo already relies
  on it: `TransportSecurityMiddleware.cs:19` refuses plain HTTP outside Development, and the Aspire host's
  `KeyValues__Protection__Emulator=true` throws in Production.
- In Azure, `src/Platform/Deployment/modules/functions.bicep:124-125` sets `AZURE_FUNCTIONS_ENVIRONMENT` (and the
  ineffective `DOTNET_ENVIRONMENT`) to `runtimeEnvironment`, which `main.bicep:139` defaults to `Production`. Nothing stops
  a deployment that sets it to `Development`.
- `docs/Platform/development.md:15, :53` and `docs/Platform/Storyteller/mcp.md:208` say the Development environment comes
  from `DOTNET_ENVIRONMENT` in `local.settings.json`. It comes from Core Tools.
- A `LogCritical` written before `RunAsync` does not reach the `func start` output (the worker's logger goes over gRPC,
  which is not up yet). An unhandled exception thrown before `RunAsync` does: the host logs
  `Unhandled exception. System.InvalidOperationException: <message>` from the worker's stderr, retries, and serves nothing.
- The worker's own URL is always `http://localhost:<port>` behind the host, locally and in Azure, so "the host is loopback"
  cannot be checked from the worker. Azure sets hosting variables on every instance (`WEBSITE_SITE_NAME`,
  `WEBSITE_INSTANCE_ID` on App Service and Functions; `CONTAINER_APP_NAME` on Container Apps) that Core Tools does not set.

### Tests

- `Api.Functions/test` (xUnit v3 on MTP, 3394 tests) passes in Debug and in Release (`dotnet test --project
  src/Platform/Storyteller/Api.Functions/test [-c Release]`). It needs no Docker, emulator, network or secret.
- About 15 test classes return early from the role refusals under `DEV_AUTH` (`if (DevAuth) { return; }`), so those
  refusals are asserted only in Release, which nothing runs.
- No test runs the authentication middlewares in front of a real handler. Handlers are called directly with
  `TestRequests.User()`/`Machine()` writing the middlewares' items; the access doubles give every account the same role on
  every key and every machine every project (`KeyValueTestDoubles.cs:83-132`, `FederationAccessService.cs`), so none can
  show a refusal by tenant.
- `RouteTableTests.cs:16-25` and `RouteDispatchTests.cs:206-218` enumerate the `[Function]` methods and their `HttpTrigger`
  routes by reflection.
- The real store's semantics (`Backend.CosmosDb/src/Accessing/CosmosAccessService.cs`): `GetAccountRoleAsync` (L104-114) is
  `AccessMap.GetValueOrDefault(key)`, an exact, case-sensitive key with no inheritance from the organization to its
  projects; `GetMachineAccessAsync` (L479-489) is a point read in partition `(organization, "{project}.access")`, case
  sensitive too.

### Route families

The 150 HTTP functions of the app assembly (the 4 of the OpenAPI extension are elsewhere) are:

- 121 with `{organization}` in the route, guarded in the API by `CheckAccessToProject(Metadata)Async`,
  `CheckAccessToAnnotation(s)Async`, `CheckUserAccessToProjectAsync` or `KeyValueAuthorization.CheckAdministratorAsync`,
  except `PostRenewCertificate` (the machine identity must equal `{id}`; the renewal service reads the machine in the
  route's project) and `PostMachineToken` (anonymous; the exchange resolves `client_id` in the route's project);
- 12 on `v1/access/points/{key}`: the access point, its audit and machine authentication are checked in the API
  (`CheckUserAccessToAsync`); members and invitations by the access service (`CosmosAccessService`,
  `CosmosInvitationService`);
- 4 with the tenant in the body: `v1/access/grant`, `v1/access/revoke`, `POST v1/access/points`, `POST v1/access/account`,
  checked by the access service;
- 8 scoped to the caller (account, its access points and projects, its invitations, the exhibition) and 5 anonymous.

Traced against the code: a user of organization A gets `403` on every route of B and on every case and dotted variant of
B's name (exact keys). A machine of A (API key, certificate, client credentials) gets `401` on B, because the point read of
the machine in B's project misses; a federated machine of A gets `401` before any read, because its token names A's project
(`FederatedMachineAccess.cs:33-36`, compared ignoring case, then the case-sensitive read misses). A dotted value inside A's
own organization (`a.app/x`, `a/app.x`) passes the machine check and reaches a phantom organization or project of A: that is
424-241 and is not in this plan.

### CI

- `.github/workflows` has no reference to `Api.Functions`. `Platform.Storyteller.CosmosDb.yml:11-12` says "no other workflow
  runs the Storyteller unit tests".
- **Blocker:** `Directory.Packages.props:51,55` pins `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` and
  `Microsoft.Azure.WebJobs.Extensions.OpenApi.Core` to `2.0.0-preview-prop-naming`, a custom build of the open upstream PR
  Azure/azure-functions-openapi-extension#646 (commit `f49d978`). It is not on nuget.org and `nuget.config` lists only
  nuget.org and `./.artifacts`; this machine restores it from its global packages folder, filled once from
  `D:\work\.packages`. On a runner NuGet would fall back to `2.0.0-preview1`, which has no `NamingStrategy`, so
  `OpenApi/OpenApiConfigurationOptions.cs:32` fails to compile. `docs/specs/reviews/2026-11-01 Phase 2 of the NuGet
  Upgrade.md:24` records it. This is why no workflow builds the API.
- Outputs are per project in `bin/<Configuration>` and `obj/<Configuration>`, so Debug and Release builds do not collide.

## Proposed Changes

### Phase 1: The startup guard

#### 1. `Security/DevelopmentAuthenticationGuard.cs` (new, internal static)

```csharp
internal static class DevelopmentAuthenticationGuard
{
    // Set on every instance by the hosting platforms, never by Core Tools or Aspire.
    public static readonly IReadOnlyList<string> HostingVariables = ["WEBSITE_SITE_NAME", "WEBSITE_INSTANCE_ID", "CONTAINER_APP_NAME"];

    // Null when the build may start; otherwise the reason, one line without values other than the environment's name
    // and the names of the variables.
    public static string? GetRefusal(string environmentName, Func<string, string?> getVariable, bool developmentAuthentication);

    // Throws InvalidOperationException with the refusal.
    public static void Ensure(IHostEnvironment environment);   // Environment.GetEnvironmentVariable, IsDevelopmentAuthentication
    internal static void Ensure(IHostEnvironment environment, Func<string, string?> getVariable, bool developmentAuthentication);
}
```

- A Release build (`developmentAuthentication` false) always passes.
- A Debug build passes only when the environment is Development (`IsDevelopment()`, ignoring case, as the rest of the API)
  **and** none of `HostingVariables` is set. The hosting check stands in for the issue's "the host is loopback", which the
  worker cannot see, and also refuses a deployment whose `runtimeEnvironment` was set to `Development`.
- The message starts with `DEV_AUTH` so it can be found in the host log (`traces | where message has "DEV_AUTH"`), says that
  a Debug build skips the checks of users, names the environment (or the hosting variable), and says what to do: deploy a
  Release build (`dotnet publish -c Release`), or run locally in Development.
- The constant stays a parameter (as `StepUpOptions.IsEnabledFor(..., developmentAuthentication)`), so both branches are
  tested in either build.

#### 2. `Program.cs`

The first statement of `ConfigureServices` (`:61`) is `DevelopmentAuthenticationGuard.Ensure(context.HostingEnvironment);`
with a comment. There the build is refused before any service of the API is registered (no Cosmos client, no vault access,
no hosted service), and the exception ends the worker before it connects to the host; the host logs it. A `LogCritical`
would not be seen at that point (see Current State), so the exception message is the log line.

#### 3. Tests (`Api.Functions/test/DevelopmentAuthenticationGuardTests.cs`)

- The decision over `Development`, `development`, `Production`, `Staging` and a custom name × `developmentAuthentication`
  true/false, and each hosting variable (set, empty, missing) in Development.
- `Ensure` with a `TestHostEnvironment` throws `InvalidOperationException` with the refusal, and passes in Development.
- The build: `HttpRequestDataExtensions.IsDevelopmentAuthentication` is true exactly in a Debug build (as
  `UserRoleChecksTests`).
- The wiring: in a Debug build, the app's entry point (`typeof(DevelopmentAuthenticationGuard).Assembly.EntryPoint`)
  invoked in a process without a Development environment throws the guard's exception from `Build()`, before anything else
  of the API runs. Skipped with a reason in a Release build (there the entry point would start the host) and when the test
  process itself runs in Development (`ASPNETCORE_ENVIRONMENT`/`AZURE_FUNCTIONS_ENVIRONMENT`).

#### 4. Documentation

- `docs/Platform/Storyteller/authentication.md` (the `DEV_AUTH` paragraph at :99 and :2013): a Debug build starts only in
  the Development environment and outside the hosting platforms.
- `docs/Platform/Storyteller/access.md:167`: the same, in one sentence.
- `docs/Platform/development.md:15, :53` and `docs/Platform/Storyteller/mcp.md:208`: the Development environment comes from
  Core Tools (`AZURE_FUNCTIONS_ENVIRONMENT`), not `DOTNET_ENVIRONMENT`; a troubleshooting row for the guard's message.
- `docs/Platform/Deployment/cookbook-azure-production.md` (16.1 and the app settings at :296-297) and
  `runbook-azure-production.md:384`: a Debug publish does not start; `AZURE_FUNCTIONS_ENVIRONMENT` is never `Development`.
  `src/Platform/Deployment/README.md`: the parameter `runtimeEnvironment`.

### Phase 2: A workflow of the API's unit tests

#### 5. The OpenAPI packages in the repository

The custom build must be restorable on a clean machine. The two packages (MIT, `repository commit` `f49d978` in their
nuspec) are added to a new tracked folder `.packages/` at the root, with a `README.md` that records their origin, the
upstream PR, their SHA-256 and when to remove them (once a public build has `NamingStrategy`). `nuget.config` gets the
source `<add key="Vendored packages" value="./.packages" />`; `.gitignore` gets `!/.packages/*.nupkg`. Nothing else
changes for restore: the folder holds only those two versions.

Alternatives (in the review if chosen later): a private feed (needs a credential in CI), building the fork in the job
(slow, needs old targeting packs), or dropping the custom build (changes the OpenAPI document).

#### 6. `.github/workflows/Platform.Storyteller.Api.Functions.UnitTests.yml` (new)

Named after the test project, as `Platform.Cli.UnitTests` (`docs/github/naming-convention.md:32-33`).

- Triggers: `pull_request` and `push` to `main` on `src/Platform/Storyteller/**` (the issue's filter; it also covers every
  Storyteller project the API builds on), `src/Libraries/Platform/Storyteller/ConfigurationFormats/**`,
  `src/Libraries/Utils/Async/**`, `.packages/**`, `src/.stylecop/**`, `Directory.Build.props`, `Directory.Packages.props`,
  `global.json`, `nuget.config`, `stylecop.json`, `version.json`, `src/Platform/version.json` and the workflow itself; also
  `workflow_dispatch` and `workflow_call` (a future deployment of the API can gate on it).
- `concurrency`: `storyteller-api-functions-${{ github.workflow }}-${{ github.ref }}`, cancelled only on a pull request (the
  pattern that is safe under `workflow_call`, from the conformance workflow).
- One job with a matrix `configuration: [Release, Debug]`, `fail-fast: false`, on `ubuntu-latest`, 30 minutes: checkout with
  the whole history, `setup-dotnet` from `global.json`, `dotnet test --project
  src/Platform/Storyteller/Api.Functions/test -c ${{ matrix.configuration }} --report-trx --results-directory TestResults`,
  and the results as an artifact. Release is the run that matters: the checks of users are on, as in production. Debug runs
  the `DEV_AUTH` branches and the guard's refusal through the entry point.
- `Api.Functions/testint` stays out: it needs a running host with Cosmos DB, Azurite, a seed and a user token.

#### 7. Documentation of the workflow

- `docs/github/workflows.md` (a row in "Automatic checks") and `docs/github/naming-convention.md` (the table of current
  workflows).
- `CLAUDE.md` (the CI/CD list).
- `Platform.Storyteller.CosmosDb.yml:11-12`: no longer "no other workflow runs the Storyteller unit tests".
- `docs/Platform/Storyteller/export-import.md:609` and `access.md:167`: the role refusals run in Release in CI.

### Phase 3: Tenant isolation tests of the API

#### 8. The harness (`Api.Functions/test/TenantIsolation/`)

Everything in process, with the real middlewares in production order and the real handlers:

- **Two tenants with the same project and view names**: A = `acme/app/default`, B = `globex/app/default`, so only the
  organization tells them apart (as the backend's isolation suites do).
- **`TenantAccess`** (an `IAccessService` through `DispatchProxy`) with the store's semantics: accounts with an Ordinal
  access map and no inheritance (`user-a` Owner of `acme` and `acme.app`; `user-b` Owner of `globex` and `globex.app`),
  machines keyed `(organization, project, id)` Ordinal, `GetAccountAsync` for Storyteller user tokens. Only these
  "gate" members answer; every other member records the call and throws `TenantIsolationTripwire`.
- **Tripwire services**: every other constructor dependency of a handler that is an interface is a `DispatchProxy` that
  records the call and throws the tripwire; options are `Options.Create(new T())` (or the API's JSON options), loggers
  `NullLogger<T>`, `TimeProvider.System`. Handlers are created with `ActivatorUtilities`, so a new constructor parameter needs
  no change to the tests.
- **A policy store** that enables every credential kind on both projects, so a refusal can only come from the tenant.
- **The pipeline**: `ExceptionHandlingMiddleware` → `TransportSecurityMiddleware` (Production) →
  `MachineAuthenticationMiddleware` → `BearerAuthenticationMiddleware` → the handler, called by reflection with the route
  values bound by parameter name, `[FromQuery]` parameters at their defaults and `uint` versions as 1. The outcome is the
  status code of the middleware's response or of the handler's `IActionResult`/`HttpResponseData`, plus the tripwire's
  record.
- **Callers of A**, each presenting real credentials to the middlewares:
  - a user (`user-a`): a provider bearer token answered by a `RecordingBearerTokenValidator`, with `User.Impersonation`
    and a fresh `auth_time`;
  - a delegated user: a real Storyteller user token of `user-a` with the actor `mcp` (`StorytellerTokenIssuer`,
    validated by the real `StorytellerTokenValidator`);
  - API key, certificate (the `X-ARR-ClientCert` header with a self-signed certificate) and client-credentials machines of
    `acme/app` with every scope, through fake validators that return A's triple;
  - a federated machine: a real Storyteller machine token for `acme/app`.
  The tests run with `BearerValidationMode.DecodeWithoutValidation` off (in the existing `BearerValidationMode` collection)
  and under `UserRoleChecks.Enforce()`, so they assert the same in Debug and in Release.

#### 9. The route table (`TenantRoutes.cs`)

Every HTTP function of the assembly by `nameof`, in a family, with what decides its tenant:

- `Api`: the API's checks refuse (families 1-28 and 29, 30, 33 of the coverage list);
- `Service`: the access, invitation, renewal or exchange service decides (members, invitations, grant/revoke, access point
  and account creation, certificate renewal, the machine token);
- `Caller` (caller-scoped) and `Anonymous`: no tenant.

Per function: whether machines may call it, and the body or query a request needs to pass its own input checks.

A reflection test asserts that the table holds exactly the `[Function]` methods with an `HttpTrigger` of the assembly, and
that every function with `{organization` in its route or on `v1/access/points/{key}` is `Api` or `Service`: a new function
fails the test until it is classified.

#### 10. The tests (`TenantIsolationHttpTests.cs`)

A theory over every `Api` function × every caller kind:

- **Control on A**: the same request to `acme/app` is not refused (not `401` or `403`; it reaches a service or answers on
  its own), except for a machine on a users-only function, which is refused on both (`403`).
- **Denial on B**, for each of `globex/app`, `GLOBEX/app`, `Globex/APP`, `globex/app.x`, `globex.app/x` and `acme.globex/app`
  (on the `{key}` routes: `globex`, `globex.app`, `GLOBEX.APP`, ` globex.app `, `Globex`): `401` for machines and `403` for
  users, and no tripwire, so no service of the API was called for B. A dotted value inside A's own organization is left to
  424-241.

For the `Service` functions:

- machines are refused with `403` before the service, on A and B;
- a user's request reaches the service with the caller's own account id from the token and B's key as given (normalized
  on `{key}`), never an identity from the route or the body; the service's rule (an actor needs a role on the access
  point) is covered by `CosmosAccessServiceTests` (`Grant_ByNonMember_ThrowsAccessDenied`, `Revoke_ByNonMember_...`,
  `GetMembers_ByContributor_...`), `CosmosInvitationServiceTests` (`Resend_FromAnotherAccessPoint_ThrowsNotFound`, ...) and
  `AccessIsolationTests.CertificateRenewal_OfTheSameMachineAndCertificate_RenewsOnlyInItsOrganization`;
- `PostRenewCertificate`: the certificate machine of A reaches the renewal service only with its own id, and with B's
  organization; `PostMachineToken`: the exchange is asked with B's organization and project.

#### 11. Documentation

- `docs/Platform/Storyteller/annotation-lifecycle.md:161-165` (Containers): the storage isolation suites are matched by the
  API's tenant isolation tests.
- `docs/Platform/Storyteller/access.md:167` and `authentication.md` "401 and 403": what a caller of another organization
  gets.

## Decisions

- **The guard throws in `ConfigureServices`, not after `Build()`**: nothing of the API is registered or built for a refused
  build, and the exception is what reaches the host log; a logger is not wired at that point.
- **Hosting variables instead of loopback**: the worker cannot see the host's address. The variables are set by Azure on
  every instance and not by Core Tools; a developer can still run Debug locally in Development.
- **Vendored packages over a private feed**: no credential in CI and the same bits as the local builds. The commit is
  separate, so it can be replaced.
- **Same project name in both tenants**: only the organization separates them, the strongest case for a gate keyed by
  organization.
- **Status plus tripwire**: a refusal is `401`/`403` and no service of the API was called; the control on A proves the
  refusal is the tenant's and not a broken request.
- **Not here**: the dotted route values inside one organization and the match of a credential's own organization with the
  route (424-241); seeding a machine with the same id in both tenants would pass today for that reason.

## Phases and commits

1. `docs(platform)`: this spec.
2. `fix(platform)`: the startup guard, its tests and docs.
3. `build(platform)`: the vendored OpenAPI packages.
4. `ci(platform)`: the workflow and its docs.
5. `test(platform)`: the tenant isolation tests and docs.
6. `docs(platform)`: the review.

## Out of scope

- 424-241 (dotted route values and the credential's own organization).
- `Api.Functions/testint` in CI, and the stale `Dockerfile` (it copies projects that no longer exist).
- A Bicep rule on `runtimeEnvironment`: the hosting variables refuse a Debug build there whatever its value.
