# MCP Server

The MCP server lets an AI assistant work with the 2S platform as the signed-in person. It is an HTTP server over the [Model Context Protocol](https://modelcontextprotocol.io/) (Streamable HTTP, stateless) in front of the Storyteller API. Every tool call is a call of the API with a token of that person, so the person's [roles](access.md#roles) decide what succeeds, and the audit actor of every change is the person's account.

The code is in:

- `src/Platform/Storyteller/Mcp/src`: the host (`Program.cs`, `Hosting/`), sign-in and on-behalf-of (`Authentication/`), the tools (`Tools/`) and the configuration analysis (`Understanding/`);
- `src/Platform/Storyteller/Mcp/test`: unit and in-memory host tests;
- `src/Platform/Storyteller/Mcp/testint`: the identity check against a real tenant;
- `src/Platform/Storyteller/Access.AzureAd/src/EntraIdBearerTokenValidator.cs`: the API side, delegated clients.

The design and its decisions are in [the spec](specs/2026-10-24%20MCP%20Server%20for%20the%20Storyteller%20API.md) and [its review](specs/reviews/2026-10-24%20MCP%20Server%20for%20the%20Storyteller%20API.md).

## How a call reaches the API

1. An MCP client signs the person in with Entra ID and calls `POST /mcp` with a token for the **MCP app**.
2. The server checks the token: signature, the issuer of the configured tenant, the audience of the MCP app, the `tid`, a client app listed in `Mcp:EntraId:AllowedClientIds`, a delegated token (`scp`, no `idtyp=app`) and the scope `Mcp.Access`.
3. Before the tool runs, the server exchanges the token on behalf of the person (Entra ID on-behalf-of) for a token for the **Storyteller API** with `User.Impersonation`. The MCP token itself never reaches the API.
4. The API classifies the token as a user because the MCP app is listed in its `Auth:DelegatedClients` (see [Entra ID settings](authentication.md#entra-id-settings)), finds the account by `sub` and checks the role.

Entra ID is the only provider. AuthKit issues no token for the Storyteller API on behalf of a user, so the server refuses to start with `Mcp:Provider` set to `AuthKit`.

## Setting up Entra ID

Two app registrations take part besides the MCP clients:

**The MCP app** (the server):

- single tenant, a confidential client only: no public-client flows, no SPA or mobile redirect URIs. Then the only delegated tokens it can get for the API are on-behalf-of tokens of the server;
- **Expose an API**: the App ID URI is the resource URL of the server, for example `https://mcp.example.com/mcp`, on a verified domain of the tenant, without a trailing slash. Entra accepts the RFC 8707 `resource` parameter that MCP clients send only when it is the App ID URI of the requested scope. Add a delegated scope `Mcp.Access`;
- **Expose an API → Authorized client applications** (or `knownClientApplications` in the manifest): the MCP client apps, so a person consents once;
- **API permissions**: the Storyteller API's delegated `User.Impersonation`, with admin consent; or pre-authorize the MCP app on the Storyteller API registration;
- a credential for on-behalf-of: a federated credential for a managed identity (no secret), a certificate, or a client secret.

**The Storyteller API** (the existing `Auth:ClientId` registration):

- list the MCP app and its tenant in `Auth:DelegatedClients` of the API:

  ```json
  "Auth": {
    "DelegatedClients": [
      { "ClientId": "<MCP app client id>", "TenantId": "<tenant id>" }
    ]
  }
  ```

- add the optional claim `idtyp` to access tokens. It makes app-only tokens recognizable beyond the missing `scp`.

**The MCP clients** sign in with a client id registered in the tenant: Entra has no dynamic client registration, so each client is configured with its client id, and the id is listed in `Mcp:EntraId:AllowedClientIds`.

## Settings of the server

| Key | Required | Notes |
| --- | --- | --- |
| `Mcp:Provider` | no | `EntraId` (default). `AuthKit` fails the start. |
| `Mcp:ResourceUrl` | yes | The public URL of the MCP endpoint, for example `https://mcp.example.com/mcp`, no trailing slash. HTTPS outside Development. Every metadata URL is built from it. |
| `Mcp:EntraId:TenantId` | yes | The tenant GUID; `common` and `organizations` are refused. |
| `Mcp:EntraId:ClientId` | yes | The MCP app. |
| `Mcp:EntraId:Audience` | no | The App ID URI of the MCP app; default `Mcp:ResourceUrl`. |
| `Mcp:EntraId:Scope` | no | Default `Mcp.Access`. |
| `Mcp:EntraId:AllowedClientIds:*` | yes | The MCP client apps whose tokens the server accepts. |
| `Mcp:EntraId:ClientSecret` | one credential | A client secret of the MCP app. Keep it in Key Vault or user secrets. |
| `Mcp:EntraId:CertificatePath`, `Mcp:EntraId:CertificatePassword` | one credential | A PKCS#12 file of the MCP app's certificate. |
| `Mcp:EntraId:UseManagedIdentity`, `Mcp:EntraId:ManagedIdentityClientId` | one credential | A managed identity trusted by a federated credential of the MCP app; the client id selects a user-assigned identity. |
| `Mcp:SecretTools:Enabled` | no | `false` (default) leaves out the [tools that return secrets](#tools-that-return-secrets). |
| `Storyteller:BaseUrl` | yes | The API base URL with `/api`. HTTPS outside Development. |
| `Storyteller:Scopes:*` | yes | The scopes requested on behalf of the person, for example `api://<API client id>/User.Impersonation`. |

Settings are validated at start; a missing or wrong value stops the host.

Local development: `dotnet run --project src/Platform/Aspire.Host/src` lists the `mcp` resource, started from the dashboard because it needs the settings above in its user secrets. On a machine where the App ID URI cannot be the local URL, set `Mcp:EntraId:Audience` to `api://<MCP app client id>` and use a client that does not send the `resource` parameter.

## Connecting a client

Clients discover everything from the server:

- an unauthenticated call of `/mcp` answers `401` with `WWW-Authenticate: Bearer resource_metadata="{origin}/.well-known/oauth-protected-resource/mcp"`;
- that document (RFC 9728) names the resource, the scope `{Audience}/Mcp.Access` and the server's own origin as the authorization server;
- `{origin}/.well-known/oauth-authorization-server` (RFC 8414) lists Entra's authorize and token endpoints of the tenant and `S256` as the PKCE method. Entra supports PKCE but its own metadata does not say so, and MCP clients refuse an authorization server that does not. Entra issues every token;
- a token without the scope gets `403` with `error="insufficient_scope"`.

The client needs a pre-registered client id (see above). The server is stateless: every request stands alone, so a read can simply be repeated. A write that was interrupted may still have completed on the server; check it with the matching read before repeating it.

## Identity

The API finds an account by the `sub` claim, and Entra documents `sub` as pairwise per application. Whether the `sub` of an on-behalf-of token equals the `sub` of the person's `sform` token is not documented; check it once per tenant before a rollout:

```bash
# A token of the person for the MCP app, and an API token of the same person from sform.
export MCP_TEST_TENANT_ID=… MCP_TEST_CLIENT_ID=… MCP_TEST_CLIENT_SECRET=…
export MCP_TEST_STORYTELLER_SCOPE=api://<API client id>/User.Impersonation
export MCP_TEST_USER_TOKEN=… MCP_TEST_SFORM_TOKEN=…
dotnet test src/Platform/Storyteller/Mcp/testint --logger "console;verbosity=detailed"
```

The test prints `sub`, `oid` and `tid` of both tokens and fails when the `sub` differs. Then the server cannot act for existing accounts in that tenant; `get_my_account` answers with a hint that names this cause. Mapping accounts by `tid` and `oid` for delegated clients is a separate change.

## Tools

Every tool takes `organization` and `project` (from `list_projects`, keys `organization.project`) where it works on a project, and `view` (default `default`) where it works on a view. Lists are paged by the server: `limit` (default 50, at most 200) and the `ContinuationToken` of the previous result. A page can be empty and still carry a `ContinuationToken` when the list is filtered page by page; follow it. Deleting, restoring or changing listed items between pages can skip items, so after acting on a page start again from the first one. Results are JSON with the API's PascalCase names, in a text block and as structured content. An error has the composed text in the text block and `Status`, `ErrorCode`, `Message` (the API's message alone), `Hint` and `Errors` in the structured content.

Tools carry the MCP hints: reads are read-only; deletes, purges, resets and replacements without version history are destructive; no tool is open-world. Clients that confirm destructive or non-read-only calls ask the person before them.

### Discovery

| Tool | What it does |
| --- | --- |
| `get_my_account` | The signed-in account and its roles per organization and project |
| `list_projects` | The person's projects with role and description |
| `list_views` | The registered views of a project |

### Annotations

| Tool | What it does |
| --- | --- |
| `list_annotations`, `find_annotations`, `list_descendants`, `list_deleted_annotations` | Paged lists in a compact form (key, type, name, title, status, labels, modified) |
| `get_annotation`, `get_annotation_history` | One annotation; its audit trail |
| `set_annotation` | Creates or updates one annotation; omitted fields keep their stored values as stored, `clearFields` removes some. A create refuses a deleted key or ancestor and names `restore_annotations`, unless `recreateDeleted` is set |
| `create_annotations` | Up to 100 keys or compact lines at once; existing keys are skipped |
| `delete_annotation`, `restore_annotations`, `purge_annotation` | Soft delete with descendants, restore, permanent delete (Administrator) |

- `find_annotations` lists the annotations of one type and filters them by the names in their keys (`responsibility`, `subject`, `context`, `unit`) in the server, page by page. An exact name ignores case. A filter that starts with `^` or ends with `$` is a .NET regular expression, case-sensitive unless it starts with `(?i)`, with a match timeout of 100 ms; an invalid or too slow pattern is refused. The API's own name queries are not used, because they filter almost nothing today.
- `list_deleted_annotations` with a `deletionId` lists everything that delete removed, descendants included, unless `roots` is `true`.
- New names follow the rule of `sform`: 1 to 50 letters, numbers, dashes or underscores. A name is new when its responsibility, subject, context or unit does not exist yet, deleted ones included; existing names are used as they are.

See [annotation-lifecycle.md](annotation-lifecycle.md).

### Configurations, schemas and templates

| Tool | What it does |
| --- | --- |
| `list_configurations` | Summaries of stored configurations |
| `get_configuration` | `form`: `effective` (ancestors and templates merged, bindings not evaluated), `own` (what is stored on the annotation) or a stored `version` |
| `set_configuration` | Merges into the stored document (objects merge, arrays union, `null` never erases; `$remove` takes JSONPaths) |
| `replace_configuration`, `revert_configuration` | Make the stored document equal a given document or a previous version; every top-level property they change or remove is tested first, so a concurrent change of it fails with `412` |
| `patch_configuration` | RFC 6902 operations on the stored document; refused when nothing is stored yet (`set_configuration` or `replace_configuration` writes a first document) |
| `delete_configuration`, `list_configuration_versions`, `diff_configuration` | Clear, versions, diffs between versions or views |
| `list_schemas`, `get_schema`, `set_schema`, `delete_schema`, `get_combined_schema`, `list_schema_versions`, `diff_schema` | Schemas of a type, an annotation, or a descendant type (`level`); the combined schema a write is checked against |
| `list_templates`, `get_template`, `set_template`, `replace_template`, `patch_template`, `delete_template`, `list_template_versions`, `diff_template` | View templates per type code |

Writes address the annotation's own stored document. Writing back an edited effective document would copy every inherited value onto the annotation. A configuration write on an annotation that does not exist is refused, because such a configuration is inherited but not listed. `set_schema` refuses a `$ref` that does not point inside the document (`#/…`), because the API would fetch a URL or file reference from its own network. See [inheritance.md](inheritance.md), [templating.md](templating.md), [definitions.md](definitions.md) and [binding.md](binding.md).

### Understanding configuration

| Tool | What it does |
| --- | --- |
| `search_configurations` | Finds configurations by key parts, and by property, value text or bound key/value key in the stored documents, within read budgets |
| `explain_configuration` | Where each value comes from: the step, source and node of every value, the path of nodes to the target, what was overridden, the bindings and the key/values they read (metadata only), whether the effective read is stale, and which machines can read the values |
| `suggest_configuration_placement` | Where a new value should be written for one or more systems, with the meaning of each level, whether a write there would take effect for each system (a replay of its composition), what would have to be created and a schema preview |
| `get_binding_catalog` | The binding functions and sources with examples |

The platform records no runtime reads of configurations. `explain_configuration` lists the machines whose scopes and [annotation restriction](authentication.md#annotation-restriction-of-machines) let them read the values; that is the evidence the platform has for "how is it used". They come in three groups: machines that read this document (unrestricted, or restricted to the key or an ancestor), machines restricted to a descendant, and machines restricted to an annotation that shares descendants with the key (for example `rst.billing` and `sbt.northwind` through `usg.northwind.billing`). The last two groups read the values only through such a descendant when it exists, and it may override them.

More details of the analysis:

- What was overridden (`AlsoSetBy`) is listed for scalar values only. Objects and arrays merge, so their members and items carry their own steps.
- Bindings are read by the server's own reader of the binding language. An expression longer than 8,192 characters or nested more than 64 levels deep is not read and is reported as not valid. A string that starts with `@` inside an object binding's `$definition` is reported as a possible read: it is bound only if the program emits it.
- `suggest_configuration_placement` treats each pointer as one scalar value outside any array. A pointer through an array or onto an object or array gets no `Effective` answer; place such values as a `fragment`. The schema preview is skipped when a pattern of the schema takes longer than the server's regular-expression timeout (200 ms).

### Machines

| Tool | What it does |
| --- | --- |
| `list_machines`, `get_machine` | Machines of a project; keys are masked |
| `create_machine`, `reset_machine` | Create a machine or give it a new key. The result holds the one-time credentials of its kind and a notice that they cannot be read again. A `Certificate` machine has no key to reset: it renews its certificate itself over mTLS, and is revoked by deleting it |
| `delete_machine`, `set_machine_federation_rules` | Delete; replace the federation rules |
| `get_machine_authentication_policy`, `set_machine_authentication_policy` | The credential kinds a project allows |

See [Scopes of machines](authentication.md#scopes-of-machines) for the roles. The `Federated` kind returns no secret and suits workloads with their own identity. Certificate renewal, shared certificates and trusted issuers stay with `sform`, the admin UI and the API.

### Values store

| Tool | What it does |
| --- | --- |
| `list_key_values`, `get_key_value`, `list_key_value_versions`, `diff_key_value` | Read pairs and versions without secret values; a diff of a secret is refused |
| `set_key_value`, `delete_key_value` | Write and delete with `expectedVersion` (`412 KeyValueVersionMismatch` on a mismatch); Hyok writes, also updates of an existing Hyok pair, stay with `sform` |
| `restore_key_values`, `purge_key_values`, `protect_key_values` | Bulk operations with per-key outcomes |
| `get_key_value_policy`, `set_key_value_policy`, `get_key_value_audit`, `get_key_value_usage` | Policy, audit and usage of a project (Administrator for writes, audit and usage); `set_key_value_policy` changes only the members it is given and always keeps `Public` allowed |

`set_key_value` replaces the stored content type: an update without `contentType` clears it, so pass the `ContentType` from `get_key_value` to keep it (a secret reference needs `application/vnd.2s.secret-reference` on every update). A secret value given to the tool enters the conversation; `sform` or the admin UI keep it out. See [key-values.md](key-values.md).

### Tools that return secrets

With `Mcp:SecretTools:Enabled` set to `true`, three more tools are offered. None of them is read-only, so clients that confirm writes confirm them too:

| Tool | What it does |
| --- | --- |
| `reveal_key_value` | The secret value of a pair or a version; needs `ContributorWithSecrets`. The API records a reveal of the current value as a `Reveal` event; a reveal of an older version is not audited |
| `get_resolved_configuration` | The configuration with bindings evaluated; secret bindings are filled in with secret rights, and the API records every secret it reads |
| `check_configuration_bindings` | Per binding, whether it resolves; never the value, but the API reads the secrets for the check |

A value returned by a tool enters the model's context, the client's transcript and the model provider. Without the setting, people read secrets with `sform` or the admin UI.

## Safety

- Credentials returned by `create_machine` and `reset_machine` are shown once. The server's instructions ask the assistant to tell the person to copy them from the tool result.
- The server logs tool names, statuses and error codes, never arguments, results, tokens or response bodies. Outside Development no logging rule can lower the `ModelContextProtocol` categories below `Information`, because the MCP SDK writes whole messages at `Trace`.
- API errors reach the assistant with their status, error code, message and hint; `500` and `422` answers get fixed texts, because their messages can carry storage messages or evaluated secrets. Stack traces never pass.
- Writes are not retried by the HTTP client, so a write runs once. A call may take up to five minutes. A call that times out or loses its connection answers that it may have completed on the server and names the read that checks it; lost one-time credentials mean a reset of the machine (or a new `Certificate` machine).
- The on-behalf-of tokens are cached in memory per person for at most two hours.
- Regular expressions in the server, the schema preview's `pattern` checks included, time out after 200 ms, so a hostile pattern cannot hold the server.

## Limits

- Entra ID only; AuthKit sign-in needs a token exchange the API does not offer.
- No dynamic client registration; clients use pre-registered client ids.
- No sessions, subscriptions, server-initiated requests or elicitation.
- Not offered: view and key/value export and import, BYOK setup, key rotation, Hyok writes and decryption, recipients, shared certificates, trusted issuers, invitations and members.
- Search reads stored documents, not effective ones: a value inherited from an ancestor is found on the ancestor.
