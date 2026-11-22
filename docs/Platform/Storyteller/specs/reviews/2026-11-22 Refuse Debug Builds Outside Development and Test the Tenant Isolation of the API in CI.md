# Refuse Debug Builds Outside Development and Test the Tenant Isolation of the API in CI

Linear: [424-242](https://linear.app/424fun/issue/424-242/arch-debug-builds-turn-off-every-user-tenant-check-with-no-startup).

## Overview

This review covers all three phases of [the spec](../2026-11-22%20Refuse%20Debug%20Builds%20Outside%20Development%20and%20Test%20the%20Tenant%20Isolation%20of%20the%20API%20in%20CI.md).

1. A Debug build of the API (`DEV_AUTH`) now refuses to start outside the `Development` environment and on the hosting
   platforms.
2. A new workflow runs `Api.Functions/test` in Release and in Debug on every pull request that touches Storyteller. It
   was blocked on a package that no public feed has; that package is now vendored.
3. A new set of in-process tests calls every HTTP function of a tenant through the real middlewares and handlers, as the
   users and machines of organization A, against organization B.

Everything passes:

- 4233 tests in Debug, and 4232 in Release plus 1 skipped by design, on Windows;
- the same in a clean Linux container (an LF checkout and an empty NuGet cache, the way CI restores).

## What Was Done

### 1. `Security/DevelopmentAuthenticationGuard.cs`

The class is new and internal. It has the following members.

- `GetRefusal(environmentName, getVariable, developmentAuthentication)` returns `null` when the build may start, and otherwise one line that starts with `DEV_AUTH:`.
  - A Release build always passes.
  - A Debug build passes only in `Development`. The name is compared ignoring case, as `IsDevelopment()` does.
  - It also needs none of `HostingVariables` to be set.
  - The message names the environment or the variable, never a value, and says what to do instead: publish with `-c Release`, or run only locally.
- `Ensure(IHostEnvironment)` is used by Program.cs, with the process's variables and `IsDevelopmentAuthentication`. An internal overload takes both of them, so either build tests both branches.
- `HostingVariables` holds:
  - `WEBSITE_SITE_NAME` (App Service and every Functions plan);
  - `WEBSITE_INSTANCE_ID` (App Service plans, Windows Consumption);
  - `WEBSITE_POD_NAME` and `LEGION_SERVICE_HOST` (Flex and Linux Consumption);
  - `CONTAINER_APP_NAME` (Container Apps);
  - `KUBERNETES_SERVICE_HOST` (Kubernetes pods).

Deviation from the spec:

- The spec planned three variables, and its Current State says `WEBSITE_INSTANCE_ID` is set "on App Service and Functions". The review found that this is not true on Flex Consumption, which is the plan of the API: the Functions host tells the consumption SKUs apart by that variable being absent.
- So the Flex and Linux Consumption markers and `KUBERNETES_SERVICE_HOST` were added.
- On other hosts (a VM, plain Docker, Container Instances) only the environment name refuses a Debug build. The docs say so.

### 2. `Program.cs`

`DevelopmentAuthenticationGuard.Ensure(context.HostingEnvironment)` is the first statement of `ConfigureServices`, as planned.

Checked with Core Tools 4.15.2 on a Debug build:

- `AZURE_FUNCTIONS_ENVIRONMENT=Production func start`: the worker exits. The host logs `Unhandled exception. System.InvalidOperationException: DEV_AUTH: this Debug build of the Storyteller API skips the checks of users, so it starts only in the Development environment, and the host environment is 'Production'. …`, gives up after its retries, and serves nothing.
- `func start` in `Development`: the API starts and lists its functions. This was checked again after the variables were added.

Not checked: the guard on the deployed Function App. Nobody ran a Debug publish there, and a check would show which of the variables its worker sees.

### 3. Tests of the guard (`DevelopmentAuthenticationGuardTests`, 22 tests)

- The decision:
  - each environment name (`Development` in three cases; `Production`, `Staging`, `Test`, `Developmental`, empty, null);
  - each hosting variable in `Development`;
  - `WEBSITE_HOSTNAME`, which Core Tools sets, and an empty variable, both of which still start;
  - Release in every case.
- `Ensure` of the process follows the build.
- The wiring: in a Debug build, the app's own entry point, invoked in the test process, fails in `Build()` with the guard's message. The stack trace shows the guard.
  - The test process is not in Development: no variable is set, so the environment is `Production`.
  - The test skips itself in Release, where the entry point would start the host, and when the shell of the test run sets `Development`.

### 4. Documentation of the guard

- `authentication.md`:
  - the `DEV_AUTH` paragraph: the guard, the variables by platform, and that other hosts never set `Development`;
  - the summary under "401 and 403".
- `access.md` (Roles).
- `development.md`:
  - `func start` sets `AZURE_FUNCTIONS_ENVIRONMENT`, and the worker ignores `DOTNET_ENVIRONMENT`;
  - a troubleshooting row for the guard's message.
- `mcp.md:208`: the same correction about the environment.
- `cookbook-azure-production.md`: the `AZURE_FUNCTIONS_ENVIRONMENT` row of the settings table, and the note that 16.1 always publishes a Release build.
- `runbook-azure-production.md` (11.1).
- The Deployment README: the parameter `runtimeEnvironment`. No Bicep was changed.

### 5. The vendored OpenAPI packages

`.packages/` holds `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` and `Microsoft.Azure.WebJobs.Extensions.OpenApi.Core` `2.0.0-preview-prop-naming`. Its `README.md` gives their origin (PR #646 at `f49d978`), their SHA-256, and when to remove them. Other changes:

- `nuget.config` has the source `Vendored packages`;
- `.gitignore` has `!/.packages/*.nupkg`.

`D:\work\.packages` holds a different build of the worker package than the global packages folder: its DLLs differ, and it depends on the custom `OpenApi.Core` instead of the public `1.0.0`. The vendored files are the ones every local build restored until now; their content hashes match the global packages folder.

The Scheduler and the Supervisor also pin `OpenApi.Core` to the custom build, so they restore from the folder too. The review caught a wrong claim about them in the README.

A clean Linux container with an empty NuGet cache restores and builds the API, and its tests pass in both configurations.

The alternatives stay open if a binary in the repository is not wanted: a private feed (a credential in CI), building the fork in the job, or dropping the custom build.

### 6. `.github/workflows/Platform.Storyteller.Api.Functions.UnitTests.yml`

Done as planned. The workflow has:

- a matrix of `Release` and `Debug`, with `fail-fast: false`, on `ubuntu-latest`;
- `dotnet test --project … -c <configuration> --report-trx`;
- the results as an artifact per configuration.

It runs on pull requests and on pushes to `main` that touch any of:

- `src/Platform/Storyteller/**`;
- `ConfigurationFormats` and `Utils/Async`;
- `.packages/**`;
- the StyleCop files, the root build files and the version files (`src/Libraries/Platform/Storyteller/version.json` was added after the review).

It also runs by hand and when called.

### 7. Documentation of the workflow

- `docs/github/workflows.md` and `naming-convention.md`.
- `CLAUDE.md`: the CI/CD list, and `.packages/` in the repository structure.
- The header of `Platform.Storyteller.CosmosDb.yml`.
- `export-import.md` and `access.md`: the role refusals run in Release in CI.

### 8. The harness (`Api.Functions/test/TenantIsolation/`, one type per file)

- **`TenantWorld`**: one shared world, created once.
  - A = `acme/app/default`; B = `globex/app/default`, with its own owner and machines.
  - Account ids are in the Entra ID form.
  - The services the middlewares read include the real `AddFederatedMachineAccess`. It issues a Storyteller user token of A's user through the actor `mcp`, and a Storyteller machine token of A's federated machine. The real `StorytellerTokenValidator` validates both.
  - Fakes return A's triple for the API key, the certificate and the provider's client-credentials token.
  - A policy store enables every kind.
- **`TenantAccess`**: an `IAccessService` with the store's semantics: an exact, Ordinal key of the access map with no inheritance, and machines keyed by organization, project and id. Only the members of the gate answer; every other member is a tripwire.
- **Tripwires**:
  - `TripwireProxy` stands in for every interface a handler takes;
  - `TripwireServiceProvider` gives ActivatorUtilities everything else: JSON options, null loggers, default options, and the platform's service classes built over tripwires;
  - `TripwireRecorder` (an AsyncLocal) records each call with the strings of its arguments, even when a handler catches the exception.
- **`TenantPipeline`**: ExceptionHandling → TransportSecurity (Production) → MachineAuthentication → BearerAuthentication → the handler.
  - Calls are made by reflection: route values by name, `[FromQuery]` at their defaults, versions as 1.
  - Runs under `UserRoleChecks.Enforce()`.
  - The outcome is the status code with the error text of a client error, plus what was reached.
- The tests switch `BearerValidationMode.DecodeWithoutValidation` off in the existing `BearerValidationMode` collection, so Debug and Release assert the same.

### 9. The route table (`TenantRoutes`, `TenantRoutesTests`)

All 150 HTTP functions are listed by `nameof`, each in a family, with:

- the gate: `Api`, `Service`, `Caller` or `Anonymous`;
- the address: `Route`, `Key`, `Body` or none;
- whether it is for users only;
- the body its own input checks need.

There are 124 `Api` functions in 29 families, 13 `Service` functions, 8 caller-scoped and 5 anonymous.

The coverage tests check that:

- the table holds exactly the functions of the assembly;
- every `{organization` route and every `v1/access/points/{key}` route is `Api` or `Service`;
- a family addresses its tenant one way only.

### 10. The tests (`TenantIsolationHttpTests`, 817 cases)

**`Api` functions**: each of the 124, for each of the 6 callers. One case:

- **Control**: the request to A must get past the gate. Either it reached a service, or it answered on its own below 500. A machine on a function for users only gets 403 instead.
- **B**: six names of B must each give 401 for a machine, 403 for a user, or 403 for a machine on a users-only function, with nothing reached. The names are:
  - `globex/app`;
  - `GLOBEX`;
  - `Globex/APP`;
  - `globex/app.x`;
  - `globex.app/x`;
  - `acme.globex/app`.
- The `{key}` routes use six keys instead: `globex`, `globex.app`, `GLOBEX.APP`, ` globex.app `, `Globex`, `acme.globex`.

What the 744 controls did:

| Outcome | Controls |
|---|---|
| Reached a service | 588 |
| 403 for users only | 132 |
| 400 from their own input check after the gate | 20 |
| 404 from the machine read past the gate | 4 |

**`Service` functions**: the 12 others, for each caller.

- A machine is refused with 403 before the service.
- A user's request reaches the service with A's user and B's access point (normalized on `{key}`). On the grants, the body's `createdById` claims B's owner, and the service still gets A's user.
- `PostAccount` answers 400 before the service, because A's user already has an account.
- `PostRenewCertificate`:
  - only the certificate machine reaches the renewal service, with its own id and the route's organization;
  - every other caller gets 401.
- The rules of these services are the backend's: `CosmosAccessServiceTests`, `CosmosInvitationServiceTests`, and `AccessIsolationTests.CertificateRenewal_…`.

**`PostMachineToken`** is anonymous. One test shows that the exchange is asked for the route's organization and project, for A and for every name of B.

**Mutation checks**, run by hand on a temporary change and then restored:

| Mutation | Failed cases |
|---|---|
| The role check reads A's access point whatever the route | 248 (124 functions × the 2 user callers) |
| The machine read uses A's organization whatever the route | 273 (91 functions × the 3 non-federated kinds; the federated machine is refused earlier) |
| `GetAnnotation` without its access check | its 6 |

### 11. Documentation of the tests

- `annotation-lifecycle.md` (Containers): what the API tests prove next to the storage suites.
- `authentication.md` (the 403 row): a user of another organization gets 403, and a machine gets 401, or 403 on users-only routes.
- `access.md` (Roles).

## Review

An adversarial review of the branch (four reviewers, each finding checked by a skeptic) confirmed six findings, all low, and refuted none.

- `WEBSITE_INSTANCE_ID` is not set on Flex Consumption: more variables were added (section 1).
- The docs promised that a Debug build "can never serve a deployment". They now name the platforms the check covers.
- The vendored packages README was wrong about the Scheduler and the Supervisor.
- The workflow missed `src/Libraries/Platform/Storyteller/version.json`.
- `CLAUDE.md` did not list `.packages/`.

Fixed in `f453e4bee`.

## Validation

- `dotnet test --project src/Platform/Storyteller/Api.Functions/test`: 4233 passed (Debug).
- With `-c Release`: 4232 passed and 1 skipped, the entry-point test, which is for Debug only.
- The same two runs in `mcr.microsoft.com/dotnet/sdk:10.0` on a fresh LF clone with an empty NuGet cache: the same results.
- `func start` of the Debug build: refused as `Production`, started in `Development`.

## Left for later

- **424-241**: dotted route values inside A's own organization (`acme.app/x`, `acme/app.x`) pass the machine check. Matching a credential's own organization against the route belongs there too. A machine id that is the same in two tenants would pass today for that reason, so the tests do not seed one.
- **The guard on the deployed Function App**: confirm once, from its log, which of the hosting variables its worker sees.
- **Twenty controls** answer 400 from their own input checks after the gate. Bodies that pass those checks would make them reach a service as well, for example a key/value document, a zip archive, a recipient or a key rotation.
- **`Api.Functions/testint`** and the stale `Dockerfile` (out of scope).
