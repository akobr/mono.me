# Phase 1 of Workload Identity Federation

Linear: [424-46 Add workload identity federation](https://linear.app/424fun/issue/424-46/add-workload-identity-federation)

## Overview

Phase 1 covers the API (sections 1–12 and the `authentication.md` / `key-values.md` parts of 16) of [Workload Identity Federation for Machine Access](../2026-10-17%20Workload%20Identity%20Federation%20for%20Machine%20Access.md). A project can now trust OIDC issuers, a `Federated` machine carries federation rules instead of a secret, and `POST v1/{organization}/{project}/access/token` exchanges a platform token (RFC 8693) for a 15-minute Storyteller token (`at+jwt`, RS256) that the bearer pipeline accepts with per-call checks of the stored machine. The SDKs, sform and the admin UI follow in Phases 2–4. Before committing, four reviewers (security, correctness, HTTP/OAuth, test adequacy) and one skeptic per reviewer went over the code; the eight confirmed findings are fixed (see "Review fixes").

| Project | Passed |
|---|---|
| `Backend.Core/test` | 445 |
| `Access.Federation/test` (new) | 249 |
| `Access.Certificates/test` | 89 |
| `Access.Certificates/testint` (Cosmos emulator) | 40 |
| `Api.Functions/test` | 468 |
| `Backend.CosmosDb/test` (Cosmos emulator) | 354 |
| `Access.AuthKit/test`, `Access.AzureAd/test`, `Access.Keycloak/test` | 132, 55, 24 |
| `Cli/test` | 190 |

## What Was Done

### 1. Credential kind and models

- `MachineCredentialKind.Federated` appended (value 4).
- `FederatedIssuer` and `FederationRule` (`Abstractions.Access/src/Model/`) with plain `string.Empty` members, so a missing value reaches validation (400). `FederatedIssuerKeySource` is in its own file at the project root, like the other enums (**deviation**: the spec showed it next to the record; StyleCop SA1201 and the repo convention).
- `FederationRules` on `MachineAccess`, `IMachineAccess` and `MachineAccessCreate`; the `TokenEndpoint`/`TokenScope` comment now covers `Federated`.

### 2. Validation

- `FederationValidation` (`Backend.Core/src/Accessing/Federation/`, namespace `_42.Platform.Storyteller.Accessing.Federation`) implements both tables of the spec: the audience deny list (exact entries ignoring one trailing `/`, the prefixes, the issuer's own `Issuer`), separator-anchored subject wildcards, host-based detection of name-derived issuers (GitHub incl. enterprise paths and `token.actions.*.ghe.com`, Vercel global and team), immutable-id issuers (Google incl. scheme-less, Entra), the pin rule for wildcards on other issuers, `RequiredClaims` counting as a pin, and the URL rules (https, 443, `HostNameType == Dns`, `IdnHost == Host`, no user info).
- **More public API than specified:** `ValidateIssuerChange` (immutable `Issuer`, 20-issuer limit), `ValidateMetadataUrl` (also used for a discovered `jwks_uri`), `IsValidName`, message constants, and `FederationClaimKeys` (claim names and RFC 6901 pointers, shared with the matcher).
- **Deviation:** `AllowPrivateNetworks` lifts https, the port and the DNS-host rule only; user info, non-ASCII hosts and fragments stay refused (section 3 lists exactly those limits; section 2 could be read more widely).
- **Beyond the table:** an RSA key of a stored `Jwks` needs `n` and `e`, an EC key `crv` P-256/384/521 with `x` and `y`, and `kid`s must be distinct.

### 3. Options

- `FederationOptions`, `FederationOptionsValidator` and `FederationOptionsRegistration.AddFederationOptions` (bind, `PostConfigure` from `MachineAuth:TrustDomain`, `ValidateOnStart`).
- **Bounds the spec did not give:** `MaxSubjectTokenBytes` 1,024–32,768, `MetadataRefreshMinutes` 1–1,440, `PublicBaseUrl` an absolute http(s) URL without query or fragment.

### 4. Persistence

- `IFederatedIssuerStore` / `CosmosFederatedIssuerStore`: partition `{project}.federation`, id = name, `CreatedAt`/`UpdatedAt`, 60 s list cache invalidated on local writes. The store itself refuses a 21st issuer (uncached `COUNT`) and a changed `Issuer` (`InvalidInputException`, `InvalidFederation`), so both rules hold under concurrency; a create conflict or 412 becomes `ConflictException`.
- `ITokenSigningKeyStore` / `CosmosTokenSigningKeyStore`: core container, partition `signing-keys`, id `tsk.1`, a create conflict returns false.
- `MachineAccessEntity.FederationRules`, copied on create and mapped by `AccessMappings`. The integration tests confirm the round trip through Newtonsoft writes, System.Text.Json point reads and Newtonsoft queries.
- `IAccessService.SetMachineFederationRulesAsync` (ETag replace, one retry; a second 412 → 409) and `FindMachinesUsingIssuerAsync` (`TOP @max` `EXISTS` query) as default interface members, so the five test fakes compile unchanged.
- New error codes `InvalidFederation`, `MachineNotFederated`, `FederatedIssuerInUse`.

### 5. Machine lifecycle

- New project `Access.Federation` (`src`, `test`, both in `42.mono.slnx`), assembly `42.Platform.Storyteller.Backend.Federation`.
- `FederatedMachineAccessService`: create validates the rules against the project's issuers (400 `InvalidFederation`), reset throws `MachineAccessNotSupportedException`, delete returns true.
- `PolicyAwareMachineAccessService`: a `Federated` arm, rules kept through the rewrite, rules on other kinds refused only when the list is non-empty (`MachineNotFederated`), the "not configured" 400, and reset/delete by stored kind. **Deviation:** a stored `Federated` machine stays deletable when federation is not registered (nothing external exists).
- `InMemoryFederatedIssuerStore` and `InMemoryTokenSigningKeyStore` behave like the Cosmos stores.

### 6. Platform token verification

- `FederatedIssuerKeyResolver` keeps its own `IMemoryCache` entries per organization, project, name and settings fingerprint (1 h sliding), one shared fetch per entry, discovery `issuer` and `jwks_uri` checks.
- **Deviations, all documented in `authentication.md`:**
  - A `kid` still unknown right after the immediate refetch answers 503 (the issuer may be publishing a rotated key); a stored `Jwks` with an unknown `kid` is refused.
  - A fetch failure is kept for 5 s and a misconfiguration for 1 minute, so an outage fails fast instead of queueing 10-second fetches.
  - A retrieval failure in one trusted issuer's group does not stop the next group; the 503 comes only when no group verified the token.
  - Only signing keys (`use` `sig` or absent) of kty RSA (`n`, `e`) or EC (`crv`, `x`, `y`) are used; `oct` and encryption keys are ignored.
- `FederationMetadataHttpHandler`: `UseProxy = false`, no redirects, 10 s, 512 KiB, and a `ConnectCallback` through `PublicAddressFilter`. **Deviation:** IPv6 outside the embedded-IPv4 forms must be global unicast `2000::/3`; within `64:ff9b:1::/48` only the `/96` form is checked as IPv4 and the rest is refused, because the RFC 6052 prefix length there cannot be read from the address.
- `SubjectTokenVerifier` follows steps 1–4, including the grouping by trusted issuer record, the single-audience rule, `ValidateRule` at exchange time and claim matching with pointers (array indexes too). **Deviation:** its signature takes the organization and project (for the key cache) and returns a `SubjectTokenMatch` (issuer, rule, rule number, platform `sub`) for the token claims. Duplicate claims in a token are refused.

### 7. Token endpoint

- `FederationHttp.PostMachineToken`: `HttpRequestData` in, `HttpResponseData` out, 32 KiB body, `QueryHelpers.ParseQuery`, snake_case models with both Newtonsoft and System.Text.Json names, headers on the response. `FederatedTokenExchangeService` runs the checks in the spec's order.
- **Deviations:**
  - Any `Authorization` header is refused with `invalid_request` (the spec gave no outcome; this also covers `client_secret_basic`).
  - Unknown parameters are ignored; one given twice is refused without echoing its name; names match case-insensitively (the `ParseQuery` dictionary).
  - A malformed `scope` is `invalid_scope` (RFC 6749 §5.2), not a shape `invalid_request`.
  - The policy is resolved from the access point read of step 3 rather than the 60 s policy cache, so disabling `Federated` stops new exchanges at once.
  - The service also refuses, opaquely and before any read, a non-GUID `client_id` and organization or project names with separators or control characters.
  - `audience` and `resource` are single strings in the OpenAPI schema although they repeat on the wire.

### 8. Storyteller tokens

- `TokenSigningKeyProvider`, `StorytellerTokenIssuer`, `StorytellerTokenValidator` as specified (RFC 7638 `kid`, `typ` `at+jwt` or `application/at+jwt`, RS256 only, 30 s reload gate on the validating path, no bootstrap there).
- **Deviations:** the validation key is re-read after 10 minutes so that deleting `tsk.1` reaches every instance; the issuing path hands its key to the validation state of the same instance; store failures answer 503 (`TokenSigningKeyUnavailableException`).

### 9. Bearer pipeline

- `BearerValidationResult` (kind, organization, project) and `IMachineTokenValidator.OwnsIssuer`. `BearerAuthenticationMiddleware` lets an owning validator run alone, before the `DEV_AUTH` decode path; `StoreIdentity` stores the kind and the new `MachineOrganization` / `MachineProject` items.
- The per-call checks live in a new `Security/FederatedMachineAccess` (project comparison before any read, then one `GetMachineAccessAsync`: exists, stored `Federated`, scopes within the stored scope). Token scopes are read by a new `GetScopes()` that `CheckScope` and `TryCheckScope` now share, so no scope form escapes the subset check.
- `MachineCredentialPolicy` checks any recorded bearer kind.
- Under `DEV_AUTH` a Storyteller token is validated, but `CheckScope` and `CheckAccessToAsync` (and with them the per-call checks) are no-ops, as for every caller; the docs say so.

### 10. Management API

- `FederationHttp`: issuer list/get (Contributor), put/delete (Administrator), rules put (Contributor), all with `User.Impersonation`; immutable `Issuer` (400), 409 `FederatedIssuerInUse` naming up to 10 machines, 20-issuer limit through the store, 404 without a body for unknown names.
- `TokenEndpoint` for `Federated` machines in `PostMachines`, `GetMachines`, `GetMachine` and the rules put, from `PublicBaseUrl` or the request's scheme and host plus `/api` (no `routePrefix` in `host.json`), never `X-Forwarded-Host`.
- Route constants `FederatedIssuers`, `FederatedIssuer`, `MachineFederationRules`, `MachineToken`, a `Name` parameter and six route ids.

### 11. Composition

- `Access.Federation/src/EntryPoint.AddFederatedMachineAccess` registers everything (idempotent, `AddMemoryCache`, `TryAdd` for `TimeProvider` and the in-memory stores) without `EnsureNoOtherIdentityProviderMachineAccess`; `Program.cs` calls it after `AddCertificateMachineAccess`; the Cosmos stores are registered by `AddCosmosDbAnnotations`.

### 12. Tests

- Every test class the spec lists exists, plus `InMemoryStoresTests`, `FederationRegistrationTests`, `FederationPersistenceTests` (Cosmos), `FederatedRoutingTests` (Cosmos: real router, unknown issuer, concurrent bootstrap, the registered stores are the Cosmos ones) and an end-to-end `FederatedExchangeTests` (Cosmos: exchange, validation, refusal after disabling `Federated` and after deleting the machine).
- `BearerAuthenticationMiddlewareTests` and `MachineTokenFallbackTests` now share an xUnit collection, because both switch the static `BearerValidationMode.DecodeWithoutValidation`; the new tests made that race visible once.

### 16. Documentation (Phase 1 part)

- `authentication.md`: opening summary, `DEV_AUTH` note, kinds table, policy section, `MachineAccess` table, a new "Machine access with workload identity federation" section (design, trusted issuers, rules and validation, token endpoint, Storyteller tokens, revocation timing, configuration, security notes, setup for GitHub Actions, Kubernetes, Vercel, Azure managed identity, AWS and Google Cloud), the scale section, 401/403 and "Not available yet". It documents the implemented behaviour where it deviates from the spec.
- `key-values.md`: the bearer-token row names client credentials and federated machines.

## Review fixes

| Finding | Fix |
|---|---|
| The 10 s metadata timeout stopped at the response headers (`ResponseHeadersRead`), so a slow-drip issuer host could hold the per-issuer gate | A linked token with `CancelAfter` covers the body read; a timeout still becomes `BearerKeyRetrievalException`; test with a stalling body |
| With `SigningKeyAutoBootstrap = false`, deleting `tsk.1` left the exchange signing with the deleted key (`CachedAsync` keeps a value on error) | "No key" and "unusable key" are results, not errors: 503 and a fresh store read next time; the validation state drops the key at once |
| A missing or `null` JSON body on the issuer and rules `PUT` answered 500 | Both handlers read the body themselves after the auth check; empty, `null` and invalid JSON answer 400 |
| The rules `PUT` could answer 409 without declaring it | 409 added to the OpenAPI attributes and checked |
| An unknown `kid` answers 503 only once project, machine and a rule audience match — an exception to "every refusal looks the same" | Kept (the spec's 503), documented as the one exception, test that unknown machines and audiences still get the opaque 400 |
| No test pinned that the Cosmos stores win the registration | `FederationStores_AreTheCosmosOnes`; the bootstrap test reads `tsk.1` from Cosmos |
| The missing-`kid` test passed without the check | A discovery-issuer test that a kid-less token is refused without a refetch |
| The docs promised an `ErrorResponse` for a missing issuer | The docs say 404 without a body |

Rejected by the skeptics: an issuing/validation key divergence (the handover and the re-read cover it) and "the per-call checks are only tested in Release" (they are tested directly through `FederatedMachineAccess`).

## Verification

```
dotnet test src/Platform/Storyteller/Backend.Core/test
dotnet test src/Platform/Storyteller/Access.Federation/test
dotnet test src/Platform/Storyteller/Access.Certificates/test
dotnet test src/Platform/Storyteller/Access.Certificates/testint      # Cosmos emulator at https://localhost:8081
dotnet test src/Platform/Storyteller/Api.Functions/test
dotnet test src/Platform/Storyteller/Backend.CosmosDb/test            # Cosmos emulator
dotnet test src/Platform/Storyteller/Access.AuthKit/test src/Platform/Storyteller/Access.AzureAd/test src/Platform/Storyteller/Access.Keycloak/test
dotnet test src/Platform/Cli/test
```

- `dotnet build src/Platform` stops at `Supervisor/Abstraction/test`, which targets net6.0 and is refused by `Microsoft.NET.Test.Sdk` 18.3.0 (commit `0cda68b5`, unrelated). Every other Platform project, the CLI included, builds without errors through a traversal over the remaining project files. No new warnings in new or changed lines.
- All suites ran in Debug. Under `-c Release`, five `MachineHttpTests` and `AnnotationsHttpTests.Purge_ByUser_CallsTheService` fail because they assume `DEV_AUTH`; they predate this work. Every new test passes in Release too.
- Not run: a live Functions host (the token endpoint headers were checked on the `HttpResponseData` test doubles and the rendered OpenAPI document), and no real GitHub, Kubernetes, Vercel, Entra, AWS or Google token was exchanged; platform tokens are signed locally in the tests.

## Left for later

- Phases 2–4: OpenAPI export and both SDKs, sform, admin UI.
- Behind a TLS-terminating proxy that forwards plain http, `TokenEndpoint` uses the request scheme; deployments should set `MachineAuth:Federation:PublicBaseUrl` (documented).
- The other ~20 handlers with `[FromBody] X?` answer a missing body with 500, as before.
- `FederatedMachineAccessService` validates rules against the 60 s cached issuer list, so an issuer created on another instance within that minute can make a machine create answer 400.
