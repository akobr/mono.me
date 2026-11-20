# Review: Deploy the MCP Server to Azure Container Apps

## Overview

This review covers the implementation of the spec [Deploy the MCP Server to Azure Container Apps](../2026-11-17%20Deploy%20the%20MCP%20Server%20to%20Azure%20Container%20Apps.md) ([424-287](https://linear.app/424fun/issue/424-287/deploy-the-mcp-server-to-production-on-azure-container-apps)): everything the deployment needs in the repository, so the owner can run it in production with the [runbook](../../runbook-mcp-server.md). The Bicep templates create the MCP server on Azure Container Apps behind `mcpEnabled` (off by default, so a run without it changes nothing) and turn the API's user token exchange on with the server as the actor `mcp`; a script creates the exchange application; the server's project builds its container image with the .NET SDK; a new workflow tests, builds, pushes and, after approval, deploys it; and the cookbook, decisions, runbook, `mcp.md`, the Deployment README and `CLAUDE.md` describe it. Nothing was deployed: the runbook's phases are the owner's. Four choices differ from the spec, each for a reason found while implementing it (section 12): the custom domain is bound by `main.bicep`, not `domains.bicep`; the environment has workload profiles instead of being Consumption-only; its logs go through a diagnostic setting instead of the workspace's shared key; and the deployment identity gets Container Apps Contributor and the workflow doesn't wait through a subscription-scoped operation.

## What Was Done

### 1. Hosting: Azure Container Apps, consumption, scale to zero

- `src/Platform/Deployment/modules/mcp.bicep` (new) creates the environment `prod-2s-cae`, its diagnostic setting and the Container App `prod-2s-mcp-ca`, all `Microsoft.App` resources at `2026-07-01`, the newest GA version (Bicep 0.49 knows its types).
- **The environment** has workload profiles with the **Consumption profile only** (`workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]`, the app's `workloadProfileName` `Consumption`), no virtual network, `zoneRedundant` false. The spec asked for a Consumption-only environment; Microsoft now calls that type legacy and makes workload profiles the default, at the same price: no plan management fee with the Consumption profile alone, the same monthly free grant, scale to zero.
- **Logs:** `appLogsConfiguration.destination` `azure-monitor` and a diagnostic setting `log-analytics` on the environment (`Microsoft.Insights/diagnosticSettings@2021-05-01-preview`, the newest version; its linter warning is disabled with a comment) that sends `ContainerAppConsoleLogs` and `ContainerAppSystemLogs` to `prod-2s-log` as resource-specific tables (`Dedicated`). The spec's `log-analytics` destination needs the workspace's shared key in the environment and writes through the legacy Data Collector API; this way no key leaves the workspace. The HTTP logs and metrics stay off, so the daily cap holds. The tables are `ContainerAppConsoleLogs`/`ContainerAppSystemLogs`, not the `_CL` ones.
- **The app:** one container `mcp`, 0.25 vCPU and 0.5 GiB, `minReplicas` 0, `maxReplicas` `mcpMaxReplicas` (3), an HTTP scale rule at 20 concurrent requests, single revision mode with 10 inactive revisions kept for rollbacks, external ingress on 8080, `transport` `auto`, `allowInsecure` false, all traffic to the latest revision, the user-assigned identity, no `secrets`.
- **Probes:** TCP startup (30 seconds at most), readiness and liveness probes on 8080. The spec named liveness and readiness; the startup probe is explicit too, because Container Apps documents its default probes only for the portal.
- **Names:** `naming.bicep` has `containerAppsEnvironment`, `mcpApp` and `mcpIdentity` in `resourceNamesType` and `resourceNames`.

### 2. The actor's credential: a managed identity, no secret

- `main.bicep` creates `prod-2s-mcp-mi` with the existing `modules/identity.bicep` (a second call, `platform-mcp-identity`), before the API's settings, which name its object id. Its header comment now says so. The identity has no role anywhere.
- `scripts/New-TokenExchangeApplication.ps1` (new, on `EntraApplications.psm1`) finds or creates `2S Platform Token Exchange` (single tenant, no credential) and its service principal (a managed identity can't get a token for an application without one), sets the App ID URI `api://<app id>`, and returns `ClientId`, `IdentifierUri` and `PrincipalId`. It stops, rather than change anything, when the application has another App ID URI or asks for v2 tokens, since the API gets the v1 pair. Safe to run again.
- **The optional hardening is implemented:** `-McpIdentityName prod-2s-mcp-mi`, run after the identity exists (runbook, phase 4), gives the identity the application's default app role (`00000000-0000-0000-0000-000000000000`, through Microsoft Graph's `appRoleAssignedTo`) and then sets `appRoleAssignmentRequired`, so Entra issues tokens for the audience to that identity only. The assignment comes first, so the server is never locked out.
- `EntraApplications.psm1`: the header names the third script, and the warning about secrets on an application no longer assumes a federation.
- `MaxActorTokenAgeMinutes` stays at its default and maximum, 1440.

### 3. The container image

- `Mcp.csproj`: `ContainerRepository` `akobr/2s-platform-mcp`, `ContainerFamily` `noble-chiseled`, a `ContainerDescription`, and the label `org.opencontainers.image.source` `https://github.com/akobr/mono.me` (the SDK adds it only with `PublishRepositoryUrl`, which would give the folder URL of `Directory.Build.props`).
- Checked locally with SDK 10.0.404: `dotnet publish src/Platform/Storyteller/Mcp/src -c Release --os linux --arch x64 -t:PublishContainer` builds on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (135 MB), user `1654`, port `8080/tcp`, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`; the two-tag quoting of the workflow gives two tags. Without settings the container stops with the validator's list; with the production settings of section 4 (fake ids) it starts, logs the actor's subject audiences, answers `POST /mcp` with `401` and `WWW-Authenticate: Bearer resource_metadata="https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp"`, serves the metadata with the AuthKit domain and `openid`, and `/health` answers `404` (hence the TCP probes). Neither the server nor the projects it references use time zones or cultures, so the chiseled image without ICU and tzdata fits.

### 4. Templates

- **Parameters of `main.bicep`:** `mcpEnabled` (false), `mcpHostName`, `mcpExchangeAppId`, `mcpInitialImage`, `mcpAppExists`, `mcpScope` (`openid`), `mcpMaxReplicas` (3) as in the spec, and **`mcpCustomDomain`** (false; section 12). The modules `mcpIdentity`, `mcpExisting` and `mcp` run only with `mcpEnabled`.
- **Checks before anything is written:** with `mcpEnabled`, `fail()` stops the deployment (and the what-if) when `mcpHostName` is empty, `mcpExchangeAppId` is not 36 characters, `authKitDomain` is empty, or `mcpInitialImage` is empty while `mcpAppExists` is false. All four are top-level variables of parameters only, which ARM evaluates at the start; its `if()` evaluates only the chosen branch, so they never fire with the MCP server off.
- **`modules/mcp-existing.bicep`** (the spec's `mcp-image.bicep`, renamed because it reads more than the image) reads the app when `mcpAppExists`, in a module of its own so it runs before the app is written, and outputs its image and its custom domains. `main.bicep` passes the image on (or `mcpInitialImage` for the run that creates the app), and `mcp.bicep` writes a bound domain back exactly as it runs.
- **The custom domain is in `mcp.bicep`** (section 12): with `mcpCustomDomain`, the app's ingress gets `mcp.2s.42for.net` with the binding type `Auto`, and the environment gets the managed certificate `mcp-2s-42for-net` (`subjectName` the host, `domainControlValidation` `CNAME`, `dependsOn` the app, which must carry the host name first). The platform binds the certificate as soon as it is issued. Later runs write the bound domain back unchanged (with its certificate) and put the certificate again unchanged, which the platform accepts; it refuses only a changed subject or validation. `domains.bicep` and `prod.domains.bicepparam` are unchanged.
- **Outputs:** `mcpAppName`, `mcpDefaultHostName`, `mcpResourceUrl`, `mcpIdentityClientId` and `mcpIdentityPrincipalId` (empty without the server), and `dnsRecords` gets `mcp.2s` (CNAME to the app's FQDN) and `asuid.mcp.2s` (TXT, the app's `customDomainVerificationId`) with `mcpEnabled`.
- **`modules/access.bicep`:** the deployment identity gets **Container Apps Contributor** (`358470bc-b998-42bd-ab17-a7e34c199c0f`) on the Container App only, when the identity exists and `mcpEnabled`. The module always gets the app's name (from the `mcp` module's output with the server on, so the role waits for the app), so the skipped assignment never has a scope with an empty name. The spec said Contributor, or a narrower built-in role if one exists; this is the built-in role of Container Apps, without Contributor's rights on other providers' resources under the app. `az containerapp update --image` PATCHes only the template's containers, so no role on the environment (`join/action`) or the server's identity (`assign/action`) is needed. The runbook says how to fall back to Contributor if the role turned out not to cover the write.
- **`parameters/prod.bicepparam`** sets the six parameters with the server off (`mcpHostName = 'mcp.2s.42for.net'`, the rest empty or false) and a comment that says when each changes.

### 5. The API: the exchange on, the actor trusted

`modules/functions.bicep` gets `tokenExchangeEnabled`, `mcpActorId`, `mcpResourceUrl`, `mcpIdentityPrincipalId` and `mcpExchangeAudience`, and replaces the fixed `Auth__TokenExchange__Enabled: 'false'`:

- Off (the default): exactly `Auth__TokenExchange__Enabled: 'false'`, as before.
- On: `Enabled` `true`; the actor `mcp` with `Authentication__WorkloadIdentity__Issuer` `https://sts.windows.net/<tenant>/` (`tenant().tenantId`), `__Subject` the identity's principal id, `__Audience` `api://<mcpExchangeAppId>`; `SubjectAudiences__0` the resource URL; and the AuthKit domain with its `jwks_uri` (`/oauth2/jwks`) in `AdditionalIssuers__0` **only while it differs from `Auth__AuthKit__Issuer`**, so the custom AuthKit domain (424-247) removes it in the same run instead of stopping the API at start.
- No `JwksUri` for the actor: the API discovers the keys through `https://sts.windows.net/<tenant>/.well-known/openid-configuration`, which exists and names exactly that issuer (checked on 2026-11-20), and follows Microsoft's key rollover.
- The settings pass the API's start validation (`TokenExchangeOptionsValidator`): one actor with a URL-safe id, one credential, an HTTPS issuer, a subject, an audience that belongs to no other service and is a v1 App ID URI with the v1 issuer, a subject audience that is none of the API's bearer audiences, an additional issuer different from the API's. The defaults stay: `User.Impersonation`, `NoSecretReveal`, `AllowedClientIds` empty (the API warns once), 10-minute tokens, the replay limit 10.

### 6. WorkOS production and the audiences of 424-239

No code: the runbook's phase 1 (the staging check, with its stop condition) and phase 7 (dynamic client registration, the resource indicator, the default one only if staging allows it) as in the spec. The MCP audience is only the actor's subject audience, never in `authKitAudiences`.

### 7. Workflow `Platform.Storyteller.Mcp.yml`

- **Triggers:** pull requests and pushes to `main` that touch the server or what it builds on: its `ProjectReference` closure (`Sdk.NSwag`, `Abstractions.Annotations`, `KeyValues.ClientEncryption`, `Aspire.ServiceDefaults`), the catalog `conformance/project-settings.json` that `Abstractions.Annotations` embeds, `Directory.Build.props`, `Directory.Packages.props`, `global.json` and the workflow; `workflow_dispatch`.
- **`test`:** `dotnet test --project src/Platform/Storyteller/Mcp/test` (776 tests pass locally).
- **`image`** (only `main`, a push or a manual run): `packages: write` for this job only, `docker/login-action` with `GITHUB_TOKEN`. **A commit is built once:** if `:<commit>` exists already (a rerun, a redeployment by hand), the job reuses it; otherwise `dotnet publish … --os linux --arch x64 -t:PublishContainer -p:ContainerRegistry=ghcr.io` pushes the tags `<commit>` and `latest` (the full clone, because the versioning fails on a shallow one). Then it reads the digest (`docker buildx imagetools inspect`) and hands `ghcr.io/akobr/2s-platform-mcp@sha256:…` to the deployment, so what the reviewer approves is what every replica pulls; a rebuild of the same commit has another digest (the creation time, a newer base image).
- **`deploy`:** the environment `prod.42.Platform` (reviewer, `main` only), `id-token: write`, `azure/login` with the existing variables, and a check that `AZURE_MCP_APP_NAME` is set. It runs `az containerapp update --image <image by digest> --revision-suffix gh<run id>-<attempt> --no-wait`, then polls the app with GETs at its own scope until the revision of that suffix is provisioned and the latest ready one, fails on a failed or degraded revision or after 15 minutes, checks that the app runs the image, and reads `https://mcp.2s.42for.net/.well-known/oauth-protected-resource/mcp` (computed from the app's `Mcp__ResourceUrl`) with retries, which also wakes the app from zero. The spec's plain `az containerapp update` waits by polling an operation at subscription scope, where the identity has no role; the suffix makes a redeployment of the same image a new revision.
- **Concurrency:** a newer pull request run replaces the older one; runs of `main` don't wait for each other, so a deployment waiting for its approval doesn't hold back the tests and images of later merges. The `deploy` job has its own group (`platform-storyteller-mcp-deploy`, never cancelled): one deployment at a time, and a newer waiting one replaces an older waiting one.
- **Triggers include** `src/.stylecop/**`, `stylecop.json` and `nuget.config` besides the props and `global.json`; the version files are left out on purpose, since a version bump alone would ask for a production deployment.
- `Set-GitHubEnvironment.ps1` has `-McpAppName`, by default the deployment's output `mcpAppName` (read so that a deployment of an older template without it gives an empty value, which leaves the variable as it is), and sets `AZURE_MCP_APP_NAME`.
- `docs/github/workflows.md` and `naming-convention.md` list the workflow.

### 8. Documentation

- `docs/Platform/Deployment/runbook-mcp-server.md`: rewritten for the implementation (the order of phases changed: `mcpAppExists` and `mcpCustomDomain` are set together in phase 6, the hardening of the exchange application is part of phase 4, the CAA check part of phase 5, the real start log lines, the log tables, the 240-second request limit, rollback of every resource, and the later switch to the custom AuthKit domain).
- `cookbook-azure-production.md`: step **17a** (no renumbering of steps 18 and 19, which other specs cite) with what is created and why, the CLI commands that do the same by hand, every setting of the server and of the API, the custom domain, WorkOS and the deployment identity; and rows in steps 8.3, 12, 15 and 18.
- `decisions.md`: "The MCP server" (Container Apps, its own identity with no role, `openid` with the API as the gate, a public image, the domain bound by the template that writes the app).
- `docs/Platform/Storyteller/mcp.md`: "Running in Azure", and the 240-second limit in "Limits".
- `src/Platform/Deployment/README.md`: the new modules, script, parameters, a later-runs note and a check. `CLAUDE.md`: the `Deployment/` row, the `Mcp/` entry and the CI list.

### 9. Security review

As in the spec, with: no shared key of the workspace either (section 1); the exchange application optionally restricted to the server's identity (section 2); and the deployment identity's role narrowed to Container Apps Contributor on the app (section 4).

### 10. Out of scope

Unchanged: no always-on replica, second region or virtual network; no Application Insights for the server; the custom AuthKit domain (the templates follow it, section 5); the secret tools; a WorkOS permission as the scope.

### 11. Acceptance criteria

- `az bicep build` of `main.bicep` and `domains.bicep` and `az bicep build-params` of both parameter files pass (Bicep 0.49.2), PSScriptAnalyzer reports no error (only the existing warnings), and the MCP unit tests pass. With the defaults the templates write the same resources and settings as before (the what-if is the owner's, runbook phase 4).
- The rest needs production and is the runbook's phases 4–9.

### 12. Deviations from the spec

| Spec | Done | Why |
| --- | --- | --- |
| The custom domain in `domains.bicep`, in the API's three steps | In `mcp.bicep`, with `mcpCustomDomain`: the binding type `Auto` and the managed certificate in one run | A Container App has no binding resource: every write of the app must carry its domains or drops them, so `domains.bicep` would have to write the whole app again from what it reads. `Auto` (GA since `Microsoft.App` 2025-07-01) binds the certificate without a second write |
| `modules/mcp-image.bicep` | `modules/mcp-existing.bicep` | it keeps the bound domain too |
| A Consumption-only environment | Workload profiles with the Consumption profile only | Consumption-only is a legacy type; same price |
| Logs with `log-analytics` | `azure-monitor` and a diagnostic setting | no shared key, no legacy Data Collector API |
| Contributor on the app | Container Apps Contributor on the app | the narrower built-in role the spec asked for |
| `az containerapp update --image` | the same with `--revision-suffix` and `--no-wait`, then polling the app | the CLI's wait polls at subscription scope; the suffix gives a redeployment its own revision |
| Liveness and readiness probes | also a startup probe | default probes are documented for the portal only |
| `mcpAppExists = true` in phase 8 | in phase 6, with `mcpCustomDomain` | the app exists since phase 4; one pull request less |

### 13. Risks left for the deployment

- **The first push may create the package as private** (GitHub's docs disagree); phase 3 checks and makes it public once.
- **The custom AuthKit domain later (424-247):** the API will check MCP tokens with the keys of its own issuer (`api.workos.com/sso/jwks/<client id>`), not the AuthKit domain's `oauth2/jwks`, and it can't hold a second key set for one issuer. On 2026-11-20 both URLs of production published the same single key; the runbook's "Later" section asks to compare them again before the switch and to check an exchange after it.
- **Container Apps Contributor and `containerApps/write`:** the role grants `Microsoft.App/containerApps/*/write`; that such a wildcard covers the resource's own write is how Azure treats it (Managed Identity Operator relies on the same form) but not documented. Phase 8 checks it, and the runbook names the fallback.
- **Managed identity tokens** may be close to 24 hours old when the platform hands out a cached one; the API accepts actor tokens up to 1440 minutes old, its maximum, which the spec found to fit. Watch the API's log for refused actor tokens in the first days.
- **240 seconds per request** at the ingress, shorter than the server's five minutes for a call of the API.
- **An environment idle for 90 days** may be deleted by Azure; whether an app scaled to zero counts as idle is not documented. If it happens, `main.bicep` with `mcpAppExists = false` creates it again.

### 14. Review of the change

The change was reviewed in five dimensions (the Bicep and ARM, the token exchange and its security, the workflow, the scripts, the operator docs), each finding checked by a second, skeptical reviewer. Fixed:

| Finding | Fix |
| --- | --- |
| The workflow-level concurrency let one unanswered approval hold back every later run of `main`, its tests included | The workflow group is per pull request (or per run on `main`); the `deploy` job has its own group |
| Phase 3 ignored the run that the merge itself starts | Phase 3 takes its image from that run and rejects its deployment |
| A rerun rebuilt a commit and moved its tag before any approval; replicas pull by tag | A commit is built once, and the deployment runs the image by its digest |
| The path filters missed `nuget.config` and the StyleCop files | Added; the version files left out on purpose, and `docs/github/workflows.md` names the files |
| `Resolve-DnsName` has no CAA type, and only the apex was checked | A DNS-over-HTTPS query of all three names, and the rule of the closest one |
| Phase 4's check used `$exchange` of phase 2's session | The script's output is captured again |
| A re-created identity is refused by Entra once the exchange application requires an assignment | The identity module's comment, the runbook (phase 4 and rollback) and a warning of the script say to run it with `-McpIdentityName` again |
| `mcp.md`'s AuthKit section required a WorkOS permission and a default resource indicator | It names `openid` as the alternative production uses, and what a non-default resource indicator means; so does runbook phase 7 |
| The image check sat in a module parameter, perhaps evaluated only after the identity and the API's settings were written | A top-level variable like the other checks |
| Turning the custom domain off was presented as a plain rollback | The runbook says it takes the server out of service |
| (hardening) The skipped role assignment's scope had an empty name with the server off | The module always gets the name and a separate `mcpEnabled`; the skeptic had shown ARM's engine accepts the skipped empty scope, so this only spares the what-if |

Checked and left as they are: the key set after the custom AuthKit domain (section 13; both key sets were compared on 2026-11-20 and are the same today).
