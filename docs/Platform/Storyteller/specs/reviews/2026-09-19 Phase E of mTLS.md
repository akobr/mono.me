# Phase E of mTLS — OpenAPI and SDK Generators (Completed 2026-09-19)

## Overview

Phase E investigates the practical impact of the `mutualTLS` security scheme on NSwag and Kiota SDK code generation, implements the necessary workarounds, and extends the SDK configuration to support client certificate authentication. It also completes the `MachineCreateCommand` PKCS#12 file writing that was deferred from Phase D.

The full plan is described in `2026-09-16 mTLS Per-Machine Authentication for Storyteller API.md`.

Previous phases: `2026-09-17 Phase A of mTLS.md`, `2026-09-18 Phase B of mTLS.md`, `2026-09-18 Phase C of mTLS.md`, `2026-09-19 Phase D of mTLS.md`.

---

## Investigation Findings

### OpenAPI 3.1 `mutualTLS` Security Scheme

OpenAPI 3.1 introduces `type: mutualTLS` natively. However:

- **Azure Functions OpenAPI extension** uses OpenAPI 3.0.x (`OpenApiVersionType.V3`), which does **not** support `mutualTLS` as a security scheme type.
- **NSwag** (v14.x, NJsonSchema v11.x) generates clients from OpenAPI 3.0.x specs. It does not recognize `type: mutualTLS`. An unknown type would cause a generation warning or be silently ignored — no client certificate configuration code is produced.
- **Kiota** (v1.30.0) similarly does not generate transport-layer certificate configuration from a `mutualTLS` scheme. Kiota generates authentication provider hooks, but mTLS is a transport concern configured on `HttpClientHandler`, not an API parameter.
- **OpenAPI Generator** (TypeScript SDK) also does not produce certificate configuration code from `mutualTLS`.

### Conclusion

None of the three SDK generators natively produce client code for `mutualTLS`. This is expected — client certificate attachment is a transport-layer concern that belongs on `HttpClientHandler` (C#) or TLS agent options (Node.js), not in generated API method signatures.

### Adopted Approach

The `mtls` security scheme is represented in the OpenAPI spec as an `apiKey` type with `in: header` and `name: X-ARR-ClientCert`. This is a documentation convention that:

1. All generators can parse without errors
2. Communicates to developers which endpoints accept/require client certificates
3. Does not produce misleading generated code (no one will try to set an API key header manually — the description explains it's transport-layer)

Client certificate configuration is handled through `ISdkConfiguration.ClientCertificate` and `HttpClientHandler.ClientCertificates`.

---

## What Was Done

### 1. OpenAPI Document Filter

**`Api.Functions/src/OpenApi/Filters/MtlsSecuritySchemeDocumentFilter.cs`**

Adds the `mtls` security scheme to the OpenAPI document at generation time:

```json
{
  "mtls": {
    "type": "apiKey",
    "in": "header",
    "name": "X-ARR-ClientCert",
    "description": "Mutual TLS client certificate authentication..."
  }
}
```

Registered in `OpenApiConfigurationOptions.DocumentFilters` alongside the existing `AnnotationDocumentFilter`.

### 2. SDK Configuration Extension

**`Sdk.NSwag/src/SdkConfiguration.cs`** — `ISdkConfiguration` gains:

```csharp
X509Certificate2? ClientCertificate { get; }
```

When set, the `HttpClientHandler` is configured with this certificate for mutual TLS.

### 3. HttpClient Certificate Support

**`Sdk.NSwag/src/ServicesCollectionExtensions.cs`** — `ConfigurePrimaryHttpMessageHandler` now checks for a client certificate on `ISdkConfiguration` and adds it to the handler's `ClientCertificates` collection:

```csharp
services.AddHttpClient<TInterface, TImplementation>()
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var config = sp.GetService<ISdkConfiguration>();
        var handler = new HttpClientHandler();
        if (config?.ClientCertificate is not null)
        {
            handler.ClientCertificates.Add(config.ClientCertificate);
        }
        return handler;
    });
```

This applies to all three API clients (Access, Annotations, Configuration).

### 4. MachineCreateCommand PKCS#12 File Writing

**`Cli/src/Commands/MachineAccess/MachineCreateCommand.cs`** — now extracts certificate data from `AdditionalProperties` (where NSwag puts unknown fields via `[JsonExtensionData]`) and writes PKCS#12 to file:

```csharp
if (machine.AdditionalProperties.TryGetValue("Certificate", out var certObj)
    && certObj is string certificate ...)
{
    var pkcs12Bytes = Convert.FromBase64String(certificate);
    fileSystem.File.WriteAllBytes(filePath, pkcs12Bytes);
}
```

Options: `-o|--output` for custom file path, defaults to `{machineId}.pfx`.

---

## Files Created

```
src/Platform/Storyteller/Api.Functions/src/OpenApi/Filters/MtlsSecuritySchemeDocumentFilter.cs
```

## Files Modified

```
src/Platform/Storyteller/Api.Functions/src/OpenApi/OpenApiConfigurationOptions.cs
src/Platform/Storyteller/Sdk.NSwag/src/SdkConfiguration.cs
src/Platform/Storyteller/Sdk.NSwag/src/ServicesCollectionExtensions.cs
src/Platform/Cli/src/Commands/MachineAccess/MachineCreateCommand.cs
```

---

## SDK Usage Examples

### C# (NSwag SDK) — Machine with Client Certificate

```csharp
var cert = X509CertificateLoader.LoadPkcs12(File.ReadAllBytes("machine.pfx"), "password");

services.AddStorytellerSdk(() => new SdkConfiguration
{
    BaseUrl = "https://api.example.com",
    ClientCertificate = cert,
    // AccessTokenFactory can also be set for CertificateAndApiKey mode
});
```

### C# (Kiota SDK) — Client Certificate

```csharp
var handler = new HttpClientHandler();
handler.ClientCertificates.Add(cert);
var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
var client = new ApiClient(adapter);
```

### TypeScript SDK — Client Certificate

```typescript
import * as https from 'https';
import * as fs from 'fs';

const agent = new https.Agent({
    pfx: fs.readFileSync('machine.pfx'),
    passphrase: 'password',
});

// Pass agent to HTTP client options
```

---

## Verification

| Project | Build | Tests |
|---|---|---|
| `Sdk.NSwag` | 0 errors | N/A |
| `Api.Functions` | 0 errors | N/A |
| `Cli` | 0 errors | N/A |
| `Access.Certificates.UnitTests` | 0 errors | 55/55 pass |

---

## Summary of All Phases

| Phase | Description | Status |
|---|---|---|
| **A** | Foundation: abstractions, local CA, middleware, entity consolidation | Complete |
| **B** | Key Vault: remote signing, auto-bootstrap, race-safe | Complete |
| **C** | Renewal: ETag-guarded idempotent renewal, shared certificates | Complete |
| **D** | CLI: certificate commands for sform tool | Complete |
| **E** | OpenAPI/SDK: mutualTLS scheme, generator investigation, SDK certificate support | Complete |

The mTLS per-machine authentication feature is now fully implemented across all five phases.
