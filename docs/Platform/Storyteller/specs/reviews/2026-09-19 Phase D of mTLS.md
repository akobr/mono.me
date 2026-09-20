# Phase D of mTLS — CLI Commands (Completed 2026-09-19)

## Overview

Phase D adds CLI commands to the `sform` tool for certificate management. Without this, the mTLS feature could only be exercised by hand from raw JSON. The CLI now supports certificate renewal, machine-authentication policy management, shared certificate lifecycle, and CA certificate download.

The full plan is described in `2026-09-16 mTLS Per-Machine Authentication for Storyteller API.md`.

Previous phases: `2026-09-17 Phase A of mTLS.md`, `2026-09-18 Phase B of mTLS.md`, `2026-09-18 Phase C of mTLS.md`.

---

## What Was Done

### 1. SDK Extension Methods

**`Sdk.NSwag/src/CertificateApiExtensions.cs`** — manual HTTP methods added to the partial `AccessApiClient` class for the new endpoints that aren't yet in the NSwag-generated SDK. These will be replaced by generated code when NSwag is re-run in Phase E.

Methods added:
- `GetCertificateAuthorityPemAsync()` — GET `/v1/access/certificate-authority`
- `SetMachineAuthenticationPolicyAsync(key, policy)` — PUT `/v1/access/points/{key}/machine-authentication`
- `RenewMachineCertificateAsync(org, project, id)` — POST `.../machines/{id}/certificate/renew`
- `GetSharedCertificatesAsync(org, project)` — GET `.../access/certificates`
- `IssueSharedCertificateAsync(org, project, label, lifetime)` — POST `.../access/certificates`
- `RevokeSharedCertificateAsync(org, project, thumbprint)` — DELETE `.../access/certificates/{thumbprint}`

DTO classes: `MachineAuthenticationPolicyDto`, `CertificateRenewalResponse`, `SharedCertificateDto`.

### 2. New CLI Commands

#### `sform account machine renew <id>` (`MachineRenewCommand`)
Self-service certificate renewal. Calls the renewal API endpoint.

| Option | Description |
|---|---|
| `<id>` | Machine access ID (positional argument) |
| `-o\|--output` | Output file path for renewed PKCS#12 (.pfx) |

On success: writes PKCS#12 to file and displays password.
On already-renewed: displays the descriptive message from the API.

#### `sform account machine machine-auth` (`MachineAuthSetCommand`)
Sets the machine authentication policy for a project.

| Option | Description |
|---|---|
| `-k\|--credential-kind` | `ApiKey`, `Certificate`, or `CertificateAndApiKey` |
| `-l\|--lifetime-days` | Default certificate lifetime for the project |

#### `sform account shared-cert` (`SharedCertListCommand`)
Lists all shared certificates for a project. Displays table with label, thumbprint (truncated), expiry date, and status (Active/REVOKED).

#### `sform account shared-cert issue <label>` (`SharedCertIssueCommand`)
Issues a new shared certificate.

| Option | Description |
|---|---|
| `<label>` | Certificate label (positional argument) |
| `-l\|--lifetime-days` | Certificate lifetime in days |
| `-o\|--output` | Output file path for PKCS#12 (.pfx) |

Writes PKCS#12 to file (defaults to `shared-{label}.pfx`), displays password.

#### `sform account shared-cert revoke <thumbprint>` (`SharedCertRevokeCommand`)
Revokes a shared certificate by thumbprint.

#### `sform account ca` (`CaDownloadCommand`)
Downloads the CA public certificate as PEM.

| Option | Description |
|---|---|
| `-o\|--output` | Output file path for PEM file |

Without `-o`: prints PEM to stdout. With `-o`: writes to file.

### 3. Updated `MachineCreateCommand`

Added options for certificate-aware machine creation:

| Option | Description |
|---|---|
| `-k\|--credential-kind` | `ApiKey` (default), `Certificate`, `CertificateAndApiKey` |
| `-l\|--lifetime-days` | Certificate lifetime in days |

Note: PKCS#12 file writing from `MachineCreateCommand` is deferred to Phase E (requires NSwag regeneration to expose `Certificate`/`CertificatePassword` fields on the generated `MachineAccess` model).

### 4. Command Registration

**`MachineListCommand`** — added subcommands: `MachineRenewCommand`, `MachineAuthSetCommand`

**`AccountCommand`** — added subcommands: `SharedCertListCommand`, `CaDownloadCommand`

**`CommandNames`** — added: `RENEW`, `DOWNLOAD`, `ISSUE`, `SHARED_CERT`, `CERTIFICATE`, `CA`, `AUTH`, `MACHINE_AUTH`

---

## Command Tree

```
sform account
├── machine                          (list machine accesses)
│   ├── create                       (create — gains -k, -l options)
│   ├── reset <id>                   (reset secret)
│   ├── delete <id>                  (delete)
│   ├── renew <id>                   (NEW — self-service certificate renewal)
│   └── machine-auth                 (NEW — set project credential policy)
├── shared-cert                      (NEW — list shared certificates)
│   ├── issue <label>                (NEW — issue shared certificate)
│   └── revoke <thumbprint>          (NEW — revoke shared certificate)
├── ca                               (NEW — download CA certificate as PEM)
├── register                         (register account)
├── set                              (set default project)
├── points                           (list access points)
└── logout                           (logout)
```

---

## Files Created

```
src/Platform/Storyteller/Sdk.NSwag/src/CertificateApiExtensions.cs
src/Platform/Cli/src/Commands/MachineAccess/MachineRenewCommand.cs
src/Platform/Cli/src/Commands/MachineAccess/MachineAuthSetCommand.cs
src/Platform/Cli/src/Commands/SharedCertificates/SharedCertListCommand.cs
src/Platform/Cli/src/Commands/SharedCertificates/SharedCertIssueCommand.cs
src/Platform/Cli/src/Commands/SharedCertificates/SharedCertRevokeCommand.cs
src/Platform/Cli/src/Commands/CaDownloadCommand.cs
```

## Files Modified

```
src/Platform/Cli/src/Commands/CommandNames.cs
src/Platform/Cli/src/Commands/MachineAccess/MachineCreateCommand.cs
src/Platform/Cli/src/Commands/MachineAccess/MachineListCommand.cs
src/Platform/Cli/src/Commands/Account/AccountCommand.cs
```

---

## Design Decisions

### Why Concrete `AccessApiClient` Instead of `IAccessApiClient`

The new certificate API methods are added as manual methods on the partial `AccessApiClient` class. Since the generated `IAccessApiClient` interface doesn't include them, the new commands inject `AccessApiClient` (concrete) directly. This is a temporary measure until NSwag regeneration in Phase E adds the methods to the interface.

### PKCS#12 File Writing

Commands that issue certificates (`renew`, `shared-cert issue`) write the PKCS#12 to a file using `IFileSystem` (testable abstraction). Default filenames follow the pattern:
- Renewal: `{machineId}-renewed.pfx`
- Shared cert: `shared-{label}.pfx`

### CA Download Is Unauthenticated

The `ca` command works without authentication — matching the API endpoint which is intentionally unauthenticated since the CA public certificate is not a secret.

---

## Implementation Notes for Phase E

### What Phase E Adds

Investigation of the practical impact of the `mutualTLS` security scheme on NSwag/Kiota SDK generators. Deliverables: working generator configuration, documentation of workarounds, verification of generated SDK code.

### Key Tasks

- Re-run NSwag generation to pick up new endpoints and models
- Verify `Certificate`, `CertificatePassword`, `CertificateThumbprint` appear on generated `MachineAccess` model
- Move manual `CertificateApiExtensions.cs` methods into the generated interface
- Update `MachineCreateCommand` to write PKCS#12 when certificate is returned
- Test TypeScript SDK generation for mTLS endpoints
