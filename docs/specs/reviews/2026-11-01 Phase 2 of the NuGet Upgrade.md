# Phase 2 of the NuGet Upgrade: Platform

## Overview

Phase 2 of [Upgrade All NuGet Packages and Move to .NET 10](../2026-11-01%20Upgrade%20All%20NuGet%20Packages%20and%20Move%20to%20.NET%2010.md) (Linear [424-143](https://linear.app/424fun/issue/424-143/upgrade-all-nuget-packages-to-the-latest-versions-and-move-every)): Aspire 13.6.4 without the MessagePack pin, the Functions and Api.Web packages, Testcontainers 4.16.0 and the .NET 10 Dockerfile images. The Platform's audit warnings are gone: `MessagePack` 2.5.192, `System.Security.Cryptography.Pkcs` 10.0.10, the ASP.NET Core 2.1 packages with `Newtonsoft.Json` 11.0.2 of Scheduler and Supervisor, and `SSH.NET` 2025.1.0. The exported OpenAPI document did not change, so neither SDK was regenerated. Commits: `3ba8a75f`, `be9ba0db`, `f34beea7`, `272eb004`, `d50df882`.

## What Was Done

### 1. Aspire 13.6.4

- The nine `Aspire.*` entries and the `Aspire.AppHost.Sdk` of `src/Platform/Aspire.Host/src/Aspire.Host.Platform.csproj` and `src/Crumble/Playground/Aspire.Host/src/Aspire.Host.Crumble.csproj` are 13.6.4. The SDK version stays in the two `<Sdk>` elements (it is not a package reference), and both have to stay equal to the packages.
- `src/Platform/Aspire.Host/src/AppHost.cs`: `RunAsPreviewEmulator` → `RunAsEmulator`. The 13.6.4 documentation of both says "the Linux-based (vNext) emulator"; the old name is obsolete, and `RunAsClassicEmulator` now names the classic emulator. The retired `ASPIRECOSMOSDB001` pragmas are gone. The image override (`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest`) was kept in this phase; the final review found that it selects the classic emulator, which the vNext setup of `RunAsEmulator` cannot health-check, and phase 5 removed it.
- Both AppHosts set `<NoWarn>$(NoWarn);ASPIRE010</NoWarn>` with a comment: 13.5 warns when the AppHost does not use the Aspire CLI bundle, and both keep taking the DCP and the Dashboard from their NuGet packages.
- The Storyteller Aspire hosting integration (`src/Platform/Storyteller/Aspire.Hosting`) needed no change; its 164 tests pass.

### 2. MessagePack pin removed

- `Aspire.Hosting` 13.6.4 depends on `MessagePack` 2.5.307 and `StreamJsonRpc` 2.25.29 itself. Both `MessagePack` `PackageReference`s and the central entry are gone, and `Storyteller/Aspire.Hosting` (src and test), which never had the pin, no longer reports `NU1902`/`NU1903`.
- `docs/Platform/Storyteller/aspire.md` and `docs/Platform/Storyteller/Aspire.Hosting/package-readme.md` name 13.6.4; the restore-warning note says that the warnings are gone.

### 3. Functions

- `Microsoft.ApplicationInsights.WorkerService` 2.22.0 → 2.23.0 (the last 2.x); 3.x stays out (see the table of the spec). `Microsoft.Azure.Functions.Worker.Extensions.Storage.Queues` 5.5.5, `.DurableTask` 1.21.0 (unused). `Microsoft.Azure.Functions.Worker`, `.Sdk`, `.ApplicationInsights`, `.Extensions.Http`, `.Http.AspNetCore`, `.Timer` and `.Abstractions` were already the newest.
- The custom OpenAPI extension stays at `2.0.0-preview-prop-naming`: its nuspec points at fork commit `f49d9782` ("feat: configurable serialisation naming strategy"), the head of the still open upstream PR #646; the public `2.0.0-preview2` (2023) and 1.6.0 do not contain the change. The package comes from a local folder outside `nuget.config` (`D:\work\.packages`), so a clean machine, CI or a container cannot restore it; this is unchanged.
- Scheduler and Supervisor `Api.Functions.csproj` reference `Microsoft.Azure.WebJobs.Extensions.OpenApi.Core` directly, with a comment. They resolved the public `OpenApi.Core` 1.0.0 under the custom worker build (its nuspec asks for `>= 1.0.0`), and with it `Microsoft.AspNetCore.Http` 2.1.0 and `Newtonsoft.Json` 11.0.2 with known vulnerabilities. Now all three apps resolve the same custom build.

### 4. Api.Web

- `Asp.Versioning.Http` 8.1.1 → 10.2.3. No API we use changed. The package's new analyzer reports `AV0013` at `Program.cs:68`: without `Asp.Versioning.Mvc` and `.AddMvc()` the `[ApiVersion]` attribute of the controller is not read. That was already so with 8.1.1; the versioning behaviour stays as it is, as the spec says, and the warning stays visible.
- `Microsoft.Identity.Web.TokenCache` 4.16.1 still depends on `System.Security.Cryptography.Pkcs` 10.0.10 (CVE-2026-84008 and CVE-2026-70162, fixed in 10.0.13). A central `System.Security.Cryptography.Pkcs` 10.0.14 entry and a direct reference in `Api.Web.csproj`, with a comment, pin it.

### 5. Testcontainers

- All five `Testcontainers*` entries 4.16.0. `Testcontainers.LowkeyVault` was at 4.11.0 (it came with `main` after PR #60 was opened), whose `Testcontainers` 4.11.0 brought `SSH.NET` 2025.1.0; the warning is gone.
- `Backend.CosmosDb/test/Startup.cs` and `Access.Certificates/testint/CosmosFixture.cs` pass the emulator image to the `CosmosDbBuilder` constructor; the obsolete parameterless constructor (`CS0618`) is gone. `LowkeyVaultFixture` already used the image constructor.

### 6. Dockerfiles

- `src/Platform/Storyteller/Api.Functions/src/Dockerfile`: `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0` and `mcr.microsoft.com/dotnet/sdk:10.0`; `src/Platform/Storyteller/Api.Web/src/Dockerfile`: `mcr.microsoft.com/dotnet/aspnet:10.0` and `mcr.microsoft.com/dotnet/sdk:10.0`. All three tags were checked on MCR.
- Known older defects, not fixed here: both files `COPY` project files that no longer exist (`Storyteller/Abstraction`, `AzureAd`, `CosmosDb`, `Api.Web/Api.csproj`), copy neither `global.json` nor the referenced projects before the restore, and the Functions image cannot restore the custom OpenAPI package. `Api.Web` still exposes 80/443, while the .NET 8+ images listen on 8080.

### 7. OpenAPI

- The document was exported with `STORYTELLER_OPENAPI_EXPORT` and the `OpenApiExportTests` filter before and after the phase. The two exports differ only in `info.version` (the build height), so `Sdk.NSwag` and `sdk.typescript` were not regenerated.

### Validation

- `dotnet build src`: 0 errors; new warnings only the `AV0013` above and the Aspire 13.6 obsolete members in the Crumble AppHost, which phase 3 moves.
- `dotnet test src --no-build` with containers: the Platform tests pass as in the baseline. In the full parallel run `KeyValues.Azure.KeyVault.IntegrationTests` failed 3 of 13 with "The SSL connection could not be established" against Lowkey Vault (the baseline had 1); run alone it passed 13 of 13 twice.
- Audit: only `System.Security.Cryptography.Pkcs` 8.0.1 of `c0ded0c/Cli` was left, for phase 4.
