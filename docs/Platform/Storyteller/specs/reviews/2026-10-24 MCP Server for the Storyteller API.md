# MCP Server for the Storyteller API

Linear: [424-84 Create MCP interface](https://linear.app/424fun/issue/424-84/create-mcp-interface)

## Overview

An AI assistant can now work with the 2S platform as the signed-in person. `src/Platform/Storyteller/Mcp` is a stateless Streamable HTTP MCP server on `ModelContextProtocol.AspNetCore` 2.3.0. It validates Entra ID tokens of listed MCP client apps against one tenant, serves the protected resource and authorization server metadata itself, and calls the Storyteller API with an on-behalf-of token per request; the inbound token never reaches the API. The API accepts those tokens as users because `Auth:DelegatedClients` lists the MCP app and its tenant. The server offers 66 tools: discovery, annotations, configurations, schemas and templates, machines, the Values store, the analysis of configurations (search, explanation, placement, binding catalog), and three tools that return secrets, offered only with `Mcp:SecretTools:Enabled`. This review covers [MCP Server for the Storyteller API](../2026-10-24%20MCP%20Server%20for%20the%20Storyteller%20API.md) in full, delivered in the phases of its section 14, one review for all of them.

The branch `feat/424-84` starts from `origin/main` at `c53ffbc0`.

| Commit | Subject |
| --- | --- |
| `ed99ca74` | docs(platform): plan the MCP server for the Storyteller API |
| `f01d775d` | Phase 1, feat(platform): accept delegated tokens of listed client apps as users |
| `5bd8064d` | Phase 2, feat(platform): add the MCP host with Entra ID on-behalf-of sign-in |
| `32aae1fd` | Phase 3, feat(platform): manage annotations, configurations, schemas and templates through MCP |
| `6b666b52` | Phase 4, feat(platform): manage machines and the values store through MCP |
| `f308ba18` | Phase 5, feat(platform): search, explain and place configuration through MCP |
| `7cd6db2c` | fix(platform): harden the MCP configuration analysis after review |
| `78258c01` | fix(platform): correct the MCP management tools after review |
| `41b6ba23` | fix(platform): correct the MCP machine, values and host behaviour after review |
| `fbcdc99c` | docs(platform): document the MCP server |

## What Was Done

### 1. Delegated clients in the API

As planned, with these details:

- `Backend.Core/src/Accessing/DelegatedClientOptions.cs` (`ClientId`, `TenantId`) and `UserAuthenticationOptions.DelegatedClients` (empty by default).
- `UserAuthenticationOptionsValidator` refuses an entry that is not two GUIDs, an entry equal to `Auth:ClientId` (compared as GUIDs) and a non-empty list under AuthKit.
- `EntraIdBearerTokenValidator.TryGetMachineId(claims, clientId, delegatedClients)`: a listed `azp`/`appid` from the listed `tid` with a non-blank scope claim and no `idtyp=app` is a user. Both claim spellings are read (`scp` and `…/identity/claims/scope`, `tid` and `…/identity/claims/tenantid`), because validated claims arrive mapped and decoded ones raw. The two-argument overload stays.
- `NormalizeDelegatedClients` writes the configured GUIDs in the dashed form, so `{…}` or 32-digit values in the settings still match the token (added after the review). The validator and `EntraIdClaimNormalizer` share it.
- The identity question is not changed in code. `get_my_account` names the possible `sub` mismatch on a `404`, and `Mcp/testint` checks it (section 12).

### 2. API tests

As planned: the classification cases in `EntraIdBearerTokenValidatorTests` (listed user, other tenant, app-only, `idtyp=app`, unlisted, v1 `appid`, case, empty list, GUID forms), the raw-claim cases in `EntraIdClaimNormalizerTests`, the binding and validation cases in `UserAuthenticationOptionsTests`, two decode-path cases in `BearerAuthenticationMiddlewareTests` and the role checks of a delegated user in `MachineAccessCheckTests`, which run in Release.

### 3. The MCP host project

As planned, with these details:

- `Mcp/src/Mcp.csproj` (Web SDK, `42.Platform.Storyteller.Mcp`), `GlobalUsings.cs` with the `Sdk` alias, `appsettings.json` with every key empty, `launchSettings.json` with `https://localhost:7180` and `http://localhost:5180`.
- `Program.cs` sets the process default regex match timeout (200 ms) first, then `AddStorytellerMcp` and `UseStorytellerMcp` (`Hosting/McpHostExtensions.cs`) do the registration of section 3. The MCP endpoint is `/mcp`.
- Every tool class is registered explicitly in `WithStorytellerTools`; `SecretTools` and `BindingCheckTools` only with the setting. Phase 2 shipped the shared helpers (`AnnotationKeys`, `AnnotationNames`, `StoredConfigurations`) and empty tool classes, so the later phases could be built in parallel.
- `McpInstructions.Text` holds the server instructions.

### 4. Inbound authentication, authorization and OAuth metadata

As planned:

- `McpHostOptions` (`Mcp`), `McpEntraIdOptions`, `McpSecretToolsOptions`, `StorytellerApiOptions` (`Storyteller`) with validators that fail the start on AuthKit, a non-GUID tenant or client, no allowed client, no or two credentials, and a URL that is relative, ends with a slash or is HTTP outside Development.
- `McpAuthSettings` derives the pinned issuers, the audience (default the resource URL), the full scope and both metadata URLs from the resource URL; the tenant is written in lower case.
- `McpAuthentication` configures JWT bearer (`MapInboundClaims = false`, `SaveToken`, RS256, the issuers of one tenant) with `McpTokenRules.Check` in `OnTokenValidated` (tenant, allowed client app, delegated token), and answers `401` and `403` with `WWW-Authenticate` built from the options.
- `OAuthMetadataEndpoints` serves the protected resource metadata (at the path of the resource and at the root) and the authorization server metadata with Entra's endpoints and `S256`. A smoke run of the built host answered both documents and the `401` challenge.

### 5. On-behalf-of and the SDK clients

As planned, with these details:

- `EntraOnBehalfOfTokenProvider`: MSAL confidential client with a secret, a PKCS#12 certificate (`X509CertificateLoader`) or a managed identity's assertion for `api://AzureADTokenExchange`; `AddInMemoryTokenCache` with a two-hour absolute expiration and a 100 MB size limit. `MsalUiRequiredException` becomes `StorytellerSignInRequiredException` (consent, or claims challenge), other MSAL errors `StorytellerSignInFailedException`.
- `StorytellerCallFilter` reads the inbound token through `IHttpContextAccessor`, stores the on-behalf-of token in the scoped `StorytellerAccessToken`, which the scoped `ISdkConfiguration` reads, and maps exceptions to results.
- The SDK clients drop the service defaults' resilience handler and get a standard one without retries for unsafe methods (`#pragma warning disable EXTEXP0001`). After the review, attempt and total timeouts are five minutes, the circuit breaker samples ten, and `HttpClient.Timeout` is infinite, so a slow machine creation is not cut off at ten seconds. A timeout or a lost connection becomes a fixed error text that tells the assistant to check with the matching read before repeating the call.
- `ApiSdk.partial.cs` gives the Annotations, Schemas, Templates and Transfers clients `DateParseHandling.None`, as the Configurations client had. Before, a date-like string in `Values`, a template or a schema came back as a `DateTime`, and `set_annotation` wrote it back in the host's offset; `sform` reads those documents as stored now too.

### 6. Results, pages and errors

As planned, with these details:

- `ToolResults` writes Newtonsoft JSON into the text block and the structured content (arrays as `Items`); an error has the composed text in the text block and the plain message in the structured `Message`.
- `HostPage` pages API lists with an opaque cursor; `ApiPageReader` (Phase 3) follows up to five empty API pages for lists the API filters in memory.
- `ApiErrors` copies message and hint below `500` except `422`, fixed texts for `422` and `500` and above, and never `Error`, `Details` or the raw response. `401` names `Auth:DelegatedClients`.

### 7. Management tools

As planned, with these details:

- **Annotations** (`AnnotationTools`, `AnnotationBodies`, `AnnotationGuards`, `AnnotationSummary`, `CompactAnnotationLines`): `set_annotation` builds the subtype with every required member, reads the stored annotation first, applies `clearFields`, and before a create refuses deleted keys and ancestors (`FindDeletedAnnotationsAsync`) unless `recreateDeleted`. `create_annotations` computes the keys from the lines as the API does and sends the keys; lines the API would misread are refused. The name rule applies to new names only: a name whose responsibility, subject, context or unit does not exist yet.
- `find_annotations` reads the typed list of the type and filters the names in the host (exact names ignore case; `^`/`$` patterns with a 100 ms timeout). The API's own name queries are not used: the review found that they filter almost nothing (see Left for later).
- `list_deleted_annotations` with a `deletionId` sends `roots=false`, so it lists what that delete removed.
- **Configurations** (`ConfigurationTools`, `DocumentWrites`, `Json/JsonPatchBuilder`): forms `effective`, `own` and `version`; writes check that the annotation exists; `replace_configuration` and `revert_configuration` send a top-level JSON Patch with `test` operations, or a set when nothing is stored; `patch_configuration` is refused when nothing is stored. A property name that needs `~0`/`~1` makes the builder test and replace the whole document, because the API does not decode those escapes; the patch tools refuse such pointers.
- **Schemas** (`SchemaTools`, `SchemaTarget`): `level` is checked by the tool. `set_schema` refuses a `$ref` that does not start with `#`.
- **Templates** (`TemplateTools`, `TypeCodes`): as planned, with `replace_template` falling back to a set when the template does not exist.

### 8. Machine tools

As planned, with these details:

- `create_machine` checks scope names against `Sdk.MachineScope` (also in their dotted and kebab forms), converts federation rules member by member, infers `Federated` only for a non-empty rule list, and adds `CredentialNotice` per kind as the first line of the text and the first property of the structured content.
- `reset_machine` reads the machine first and refuses a `Certificate` machine, whose reset the API answers with `500`; the description says how its certificate is renewed and revoked.
- `list_machines` is paged in the host.

### 9. Values store tools and secrets

As planned, with these details:

- `get_key_value` never reveals; for a Hyok pair the API returns no value without `reveal`, so the tool returns the metadata with `ClientEncrypted: true`.
- `set_key_value` refuses `Hyok`, also when `protection` is omitted and the stored pair is Hyok. Its description says that an update without `contentType` clears the stored type.
- `diff_key_value` reads the versions first and refuses a secret or Hyok version, the implicit previous one included, before any diff call.
- `set_key_value_policy` changes only the members it is given, adds `Public` to `allowedProtections`, checks `hsmRotationDays` (1 to 3,650) and sends `expectedVersion` (a stale one is `409 PolicyVersionMismatch`).
- `SecretTools`: `reveal_key_value` reveals the current value through the pair route, so the API records a `Reveal` event, also when the current version number is given; an older version goes to the version route, which the API does not audit, and the description says so. `get_resolved_configuration` checks the key first.
- **Logging**: `LoggingSafety.Clamp` as planned. `CredentialLoggingTests` and `UnfinishedCallTests` check that no credential, body or marker reaches a log.

### 10. Understanding tools

As planned, with these details:

- `ConfigurationSearch`: key and content filters over stored documents with the budgets of the spec; `bindsKey` also matches inside an object binding's `$definition`, as a possible read.
- `CompositionReplay` replays a composition per node as `inheritance.md` describes; its test reproduces the content and origins of the admin UI fixture (`Mcp/test/Fixtures/composition-execution.json`).
- `ConfigurationExplainer`: values with `Via`, `AlsoSetBy` and `BeforeWinner` for scalar values (objects and arrays merge, so they have none), sources, bindings with key/value metadata, `IsStale`, notes. Consumers come in three groups: this document, descendants, and annotations that share descendants with the key (added after the review).
- `BindingExpressions` and `BindingScanner` read the binding language without referencing `Binding.Language`. After the review they follow the parser's math grammar, require an object or `@` string as `$context`, and refuse expressions longer than 8,192 characters or nested deeper than 64 levels.
- `PlacementCandidates` and `PlacementAdvisor`: candidates, replay per system and pointer, evidence and ranking. The replay puts a unique marker at every scalar target, also for a fragment, so an equal value set by a later step does not look effective. A pointer through an array or onto an object or array gets no `Effective` answer.
- `SchemaPreviewer` validates with NJsonSchema through `LocalReferenceResolver`, which refuses URL, file and relative references; a pattern that hits the regex timeout or a cancelled call skips the preview.
- `check_configuration_bindings` (`BindingCheckTools`) never returns a value; a failed resolved read gives `Resolved: unknown` with the error code.

### 11. Aspire

As planned: `Aspire.Host.Platform.csproj` references `Mcp.csproj`, and `AppHost.cs` adds `mcp` with the API's `http` endpoint and `WithExplicitStart()`. `dotnet build src/Platform/Aspire.Host/src` passes; the resource was not started, because it needs an Entra app registration.

### 12. Documentation

As planned: `docs/Platform/Storyteller/mcp.md`, the Entra settings row, the delegated-client paragraph, the section "Signing in through the MCP server" and a "Not available yet" bullet in `authentication.md`, a paragraph under the roles in `access.md`, the `mcp` row in `docs/Platform/Aspire.Host/local-development.md` and the `Mcp/` line in `CLAUDE.md`. Specs and earlier reviews are not changed.

### 13. Tests

As planned, with these details: `Mcp/test` holds 484 tests: the boundary and metadata through `WebApplicationFactory` with a static signing key, the token path with a stub provider and a recording API, options, errors, logging, the catalog rules over all registered tools, every tool against mocked SDK interfaces, host tests over HTTP, and the analysis over fixtures. `Mcp/testint` holds the identity check of section 1; it writes "Skipped: …" without its environment.

### Test results

| Project | Result |
| --- | --- |
| `dotnet build 42.mono.slnx` | 0 errors (a first run hit a file lock on a Crumble source generator held by a build server; it passed after `dotnet build-server shutdown`) |
| `Mcp/test` | 484 passed |
| `Mcp/testint` | skipped (no tenant environment) |
| `Access.AzureAd/test` | 111 passed |
| `Api.Functions/test`, Debug (`DEV_AUTH`) | 2256 passed |
| `Api.Functions/test`, Release | 2256 passed |
| `Cli/test` (the SDK change) | 793 passed |
| `Aspire.Host/src` build | 0 errors |
| `Aspire.ServiceDefaults` and the Crumble projects (the OpenTelemetry bump) | built, 0 errors |

No compiler or StyleCop warning is located in a file under `src/Platform/Storyteller/Mcp`.

### Reviews

- **The spec** was reviewed before the first commit from four lenses: security, feasibility against the libraries, repository facts, and usefulness for an assistant. Accepted findings changed the design before any code: the MCP host accepts only listed client apps, and delegated clients are pinned to a tenant; the host serves both metadata documents from its options and adds authorization server metadata, because Entra publishes no PKCE methods and MCP clients refuse such a server; the App ID URI is the resource URL (`AADSTS9010010`); secrets leave only through separate, non-read-only tools behind a setting, and `diff_key_value` refuses secrets; `500` and `422` messages are not copied; writes refuse missing and deleted annotations and gain replace and revert; lists are paged in the host; search reads stored documents; placement replays compositions; the cache is bounded; the experimental resilience call and the vulnerable `Pkcs` are handled.
- **Phases 3 to 5** were implemented in parallel in separate worktrees on top of Phase 2 and merged in order.
- **The whole branch** was reviewed from five lenses (security, management tools, machines and values, the analysis, conformance and consistency), and a skeptic per lens tried to refute each finding. Of 38 findings, 23 were confirmed, 11 partly confirmed and 4 refuted. The confirmed and partly confirmed ones are fixed in `7cd6db2c`, `78258c01` and `41b6ba23`. The most important: a stack overflow of the whole host on a deeply nested binding string (SEC-1), a schema `pattern` that could hold a thread for hours (SEC-2), `find_annotations` relying on API filters that do not filter (F1), `set_annotation` rewriting stored date strings (F2), lost credentials on a ten-second timeout, machines that read a key's values through a shared descendant missing from the explanation, and `AlsoSetBy` calling merged objects "not applied". Pre-existing API defects found on the way are listed under Left for later. The refuted ones: a race in the secret check of `diff_key_value` (it needs an Administrator to delete and re-create the key between two calls, and the reader may read the secret anyway), vacuous delegated role tests (the classification is tested elsewhere), duplicated helpers (taste), and misnamed tests (the gating is tested elsewhere).

### Deviations from the spec

1. **The SDK keeps date strings as stored** in the Annotations, Schemas, Templates and Transfers clients (`ApiSdk.partial.cs`). The spec did not plan an SDK change; without it `set_annotation` rewrote values and `replace_template` could not test date-holding properties. `sform` reads those documents as stored too; its tests pass.
2. **`find_annotations` filters in the host** instead of passing name queries to the API, which ignores most of them and answers `500` for `rst`.
3. **API calls may take five minutes**, and a timeout becomes an "it may have completed" error. The spec only removed retries.
4. **`get_key_value` returns no Hyok value.** The spec said the JWE comes back as stored, but the API returns no secret value, Hyok included, without `reveal`.
5. **A reveal of an older version is not audited**, which the spec's "an audited event" did not foresee; the tool routes the current version to the audited route and says so.
6. **`set_key_value_policy` changes only the given members**; the spec said it replaces the policy. It stays destructive because the policy has no history.
7. **Parameters**: `list_key_values` takes `protections` (an array), `get_key_value_audit` takes `filterView` for its view filter (a `view` parameter must default to `default`), and `list_machines` is paged.
8. **`create_annotations` sends computed keys**, not the raw lines, and refuses lines the API would misread.
9. **`patch_configuration` checks the annotation and the stored document**; the spec table named only the patch call. **`replace_configuration` and `replace_template` refuse top-level `$remove` and `$patch`**, which a patch would store as properties.
10. **The name rule** applies to names whose responsibility, subject, context or unit does not exist yet, not to every name of a new key, so existing names with other characters can be used.
11. **`reset_machine` refuses `Certificate` machines** and **`set_key_value` refuses an update of an existing Hyok pair**, each with one read before the write.
12. **Placement** uses markers for every scalar target, refuses `$remove`/`$patch` and overlapping pointers, and answers no `Effective` for pointers through arrays or onto containers. Template candidates read the template for evidence; a `uxe` candidate has reach 0 without a call (the API refuses descendants of `uxe`).
13. **The explanation** gives `AlsoSetBy` for scalar values only, has three consumer groups instead of two, looks up key/values of the `default`, `public` and `secret` stores, and reads bindings with its own reader with length and depth limits.
14. **`set_schema` refuses external `$ref`s**, and the host sets a 200 ms default regex timeout.
15. **Packages**: `System.Security.Cryptography.Pkcs` is pinned at 10.0.13 (10.0.12 is vulnerable too); the test projects reference `Microsoft.AspNetCore.App` so restore prunes the vulnerable DataProtection and Cryptography.Xml 10.0.0; OpenTelemetry Api, Exporter and Extensions.Hosting move to 1.15.3 to clear four advisories; `Microsoft.Extensions.Http.Resilience` and `ServiceDiscovery` are pinned at 10.1.0, the version restore already resolved, because 10.0.5 does not exist.
16. **Error structured content**: `Message` is the API's message alone; the composed text stays in the text block.

### Left for later

API defects found by this work, all pre-existing and outside this branch:

- **Typed annotation name queries** (`AnnotationsHttp.cs` around `:590`, `CosmosAnnotationService.cs` around `:642`): conditions are built over model types with `Against` never set, so the backend drops them for every type but `rst`, where the cast to the entity type throws (`500`). The name-query regexes also have no match timeout.
- **Schema parsing** (`CosmosConfigurationSchemaService.cs:914, :928`) uses NJsonSchema's default resolver, which fetches URL `$ref`s from the API's network, and validates `pattern` without a regex timeout.
- **The binding parser** of `Binding.Language` recurses without a depth limit on resolved reads, and a `422 binding.evaluation` message can quote an evaluated secret, which the API also logs at `Warning`.
- **`PatchConfigurationAsync`** patches a cache-only item (`{}`, version 0) and ignores archived versions, so a patch after a delete collides with version 1.
- **`GetVersionAsync`** of key/values records no `Reveal` event.
- **A reset of a `Certificate` machine** answers `500` instead of `400`.
- The host could cache API pages to avoid skipped items when listed items change between pages (F5); today the descriptions warn.

Not done in this change:

- The identity check against a real tenant (`Mcp/testint`) has not been run. Until it passes in a tenant, the MCP server may not find existing accounts there (spec section 1).
- The host was smoke-tested, but no MCP client signed in against Entra end to end, and the `mcp` resource of Aspire was not started.
- AuthKit sign-in, dynamic client registration, elicitation for confirmations, a read-only mode and a public page on 42for.net.

### Merge notes

- `Directory.Packages.props` gains `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, `Microsoft.Identity.Web.TokenCache` 4.5.0, `ModelContextProtocol.AspNetCore` 2.3.0 and `System.Security.Cryptography.Pkcs` 10.0.13, and changes OpenTelemetry Api, Exporter.OpenTelemetryProtocol and Extensions.Hosting to 1.15.3, `Microsoft.Extensions.Http.Resilience` and `Microsoft.Extensions.ServiceDiscovery` to 10.1.0.
- `42.mono.slnx` lists the three `Mcp` projects.
- A deployment that does not list a client in `Auth:DelegatedClients` behaves as before.
