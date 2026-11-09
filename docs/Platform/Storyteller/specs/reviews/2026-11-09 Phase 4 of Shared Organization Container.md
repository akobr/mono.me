# Phase 4 of Shared Organization Container

## Overview

Phase 4 of the spec [Shared Organization Container](../2026-11-09%20Shared%20Organization%20Container.md) (424-230) covers section 11, the documentation. The probe against the Azure account `prod-2s-42fornet-cdb` is not part of this change: the account does not exist yet. It is the commit `docs(platform): describe the shared organizations container and the vNext emulator`.

## What Was Done

### 11. Documentation

- `annotation-lifecycle.md`, section *Containers*: the layout (two containers, the hierarchical key, `Organization` on every item, the predicate and the prefix of organization-wide queries, `OrganizationContainer`, no container per organization), the settings of `organizations`, and DbCreator's refusal of the earlier layout.
- The storage sentences that named the organization container or `org.{organization}`: `access.md` (names, the view discovery), `definitions.md`, `templating.md`, `key-values.md` (pairs, audit, usage, the snapshot's organization catalog, the TTL sentence that said every container has TTL), `inheritance.md`, `binding.md`, `export-import.md`, `authentication.md` (issuers, machine documents, machine certificates). `audit-operations.md` notes that the procedures of its "before" side were removed.
- `docs/Platform/Aspire.Host/local-development.md`: `db-creator` and its refusal, the container mode on vNext with its volume and Data Explorer, the move from the classic volume `platform-cosmos-data` and the re-seed, and the rate-limit hint now about the database's throughput.
- `docs/Platform/development.md`: Docker runs the emulators; the Windows emulator is optional (its stored-procedure requirement is gone).
- `CLAUDE.md`: the emulator selector, the trait, the cleanup of the platform database only, the Cosmos DB layout, the Docker sizes of the test emulators, and the workflow `storyteller-cosmos.yml`.
- `aspire.md` (the Storyteller Aspire hosting library) and `mcp.md` needed no change.
- **Not done:** the probe against `prod-2s-42fornet-cdb` (the cookbook of the probe, run 4) waits for the account; the deployment issue 424-231 has it as a step before DbCreator's first run there.
