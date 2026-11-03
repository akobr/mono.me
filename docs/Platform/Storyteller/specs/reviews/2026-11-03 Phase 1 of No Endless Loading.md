# Phase 1 of No Endless Loading: The Local API Connects to the Emulators over IPv4

Linear: [424-166](https://linear.app/424fun/issue/424-166/admin-ui-stalls-on-slow-api-calls-catalog-history-bulk-create).

## Overview

Phase 1 covers sections 1–5 of [No Endless Loading in the Admin UI on a Slow API](../../../ui.admin/specs/2026-11-03%20No%20Endless%20Loading%20in%20the%20Admin%20UI%20on%20a%20Slow%20API.md), the Storyteller part of the fix: the API's clients of the local emulators connect `localhost` over IPv4 first, so a new pooled connection no longer waits about 2 s for the refused IPv6 attempt, and the repository cache is safe for parallel requests. The spec lives with the admin UI, which leads the fix; the reviews of phases 2 and 3 are in `docs/Platform/ui.admin/specs/reviews/`.

| Commit | Content |
|---|---|
| `0bade7f1` | Sections 1–5: `EmulatorHttpHandler`, the Cosmos DB and Key Vault emulator clients, the repository cache, tests, docs |

## What Was Done

### 1. `EmulatorHttpHandler`

`Backend.Core/src/EmulatorHttpHandler.cs`, as specified: `Create()` returns a `SocketsHttpHandler` without proxy, with a 15 s connect timeout, accepting any server certificate, whose `ConnectCallback` is `ConnectAsync`. `Candidates` gives `127.0.0.1` then `::1` (only where the OS has IPv6) for `localhost` in any case and with a trailing dot, and the endpoint itself for any other host; `ConnectAsync` tries them in order with a socket of the candidate's family (a dual-mode socket for a DNS endpoint, as the default connect does), disposes a failed socket, and throws the first `SocketException` (IPv4's) when every candidate fails. `Candidates` and `ConnectAsync` are internal (the unit tests see them through the existing `InternalsVisibleTo`), following `FederationMetadataHttpHandler`.

### 2. Cosmos DB client

`CosmosClientProvider`: in the `ShouldAcceptAnyCertificate` branch the provider creates one handler and gives the SDK `new HttpClient(handler, disposeHandler: false)` on every factory call; `Dispose` disposes the handler after the client. The comment says why (the SDK configures and disposes the clients it gets). The connection strings keep `localhost`.

### 3. Key Vault emulator

`KeyVaultClients` holds one lazily created handler for the process and `internal CreateEmulatorTransport()` (`HttpClientTransport` over `new HttpClient(handler, disposeHandler: false)`); `CreateKeyClient` and `CustomerKeyProvider.GetKeyClient` use it, so the BYOK provider no longer creates a connection pool per call.

### 4. Repository cache

`ContainerRepositoryProvider` uses `ConcurrentDictionary.GetOrAdd` with a static factory and the client as its argument.

### 5. Tests, measurement and docs

- `Backend.Core/test/EmulatorHttpHandlerTests.cs` (10 cases with the theories): the candidates; `ConnectAsync` to `localhost` reaches a listener on `127.0.0.1` only (on Windows in under 1 s, the default connect takes 2 s); the fallback to a listener on `::1` only (skipped without IPv6 loopback or when something listens on IPv4 at the same port); nothing listening throws the `SocketException`; an HTTP request through `Create()` reaches a minimal responder on `127.0.0.1` by the name `localhost` and keeps the `Host` header. Run: the whole `Backend.Core/test`, 1231 passed.
- `Backend.CosmosDb/test/ContainerRepositoryProviderTests.cs`: a plain class with a mocked `CosmosClient`, 300 parallel lookups of three names give one repository per name (a smoke test). Run with `--filter-class "*ContainerRepositoryProviderTests"`: 1 passed, no emulator started. The Cosmos suites were not run (they would wipe the local emulator); the A/B below exercised the new handler end-to-end against the Windows emulator (TLS with its self-signed certificate, Gateway mode).
- The A/B, read-only, `func start --port 7072` of `Api.Functions` (Debug) against the Windows emulator, the seven type lists (`includeInactive=true`) and the configurations list of `ui-tests/automation/default` (423 annotations) requested at once:

  | | Before (`1f3c678d`) | After (`0bade7f1`) |
  |---|---|---|
  | First request after the start | 3.38 s | 1.59 s |
  | The eight lists, warm | 20–70 ms each | 20–75 ms each |
  | The eight lists after 75–80 s without requests | 1.92–2.12 s each | 15–39 ms each |
  | 4 pages at once (32 requests), warm | median 151 ms, p90 640 ms | median 235 ms, p90 681 ms |
  | 8 pages at once (64 requests), warm | median 0.8–2.2 s, p90 2.2–3.6 s | median 1.0–1.8 s, p90 2.4–3.4 s |

  The pause case is the fix: the pool drops idle connections, and each new one paid the IPv6 attempt. The rest of the first request is the worker's cold start.
- Found during the measurement, not caused by the connections: under 64 or more concurrent requests the emulator answers `429` (substatus 3200, the request-rate budget). The emulator's database has no shared throughput, so each organization container has its own 400 RU/s; the SDK retries the `429` for seconds, and the API answers `500` once the retries run out (before the fix 1 of 64 requests at 8 pages at once and 91 of 128 at 16 pages; after it 1 of 64 at 8 pages). This limits heavy parallel UI test runs on the local stack; the docs say how to lift it (start the emulator with `/DisableRateLimiting`, or raise the container's throughput). Mapping an exhausted `429` to `503` instead of `500` is left out.
- The Functions host listens on IPv4 only: a request to `[::1]:7072` is refused after 2.03 s. The browser's and the MCP server's `localhost:7071` stay out of scope (spec, Out of scope); the docs name the workaround.
- Docs: `docs/Platform/Aspire.Host/local-development.md` (Cosmos DB modes: the IPv4-first connect in the local mode, the emulator's rate limit), `docs/Platform/development.md` (Troubleshooting: two rows), `docs/Platform/Storyteller/key-values.md` (Local development: the emulator transport).
