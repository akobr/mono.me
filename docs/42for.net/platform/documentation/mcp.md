---
pageClass: dev-guide
---

# AI assistants (MCP)

The **MCP server** lets an AI assistant work with your catalog, **as you**: browse annotations, explain where a value comes from, write configurations, revert a bad change. It speaks the [Model Context Protocol](https://modelcontextprotocol.io/), so any MCP client can use it.

> Your assistant gets a library card, not the keys to the vault.

## Why you'll like it

- **Ask, don't click.** "Why does Northwind have seven retries?" or "where should a new timeout for all EU customers go?" are questions with tools behind them.
- **It's you, not a bot account.** You sign in; your role decides what succeeds; the audit says "you, via mcp".
- **Secrets stay out of the chat.** By default the API refuses the assistant every secret read. Values the model never sees can't end up in a transcript.
- **Careful by design.** Destructive tools are marked as such, so your client asks before it deletes anything.

## What it can do

| Area | For example |
| --- | --- |
| Discovery | `get_my_account`, `list_projects`, `list_views` |
| Annotations | `list_annotations`, `find_annotations`, `get_annotation_history`, `set_annotation`, `create_annotations`, `delete_annotation`, `restore_annotations` |
| Configurations | `get_configuration`, `set_configuration`, `patch_configuration`, `diff_configuration`, `revert_configuration`, plus schemas and templates |
| Understanding | `explain_configuration` (where every value comes from), `suggest_configuration_placement` (where a new value should go), `search_configurations` |
| Machines | `list_machines`, `create_machine` (without secret scopes), `delete_machine` |
| Key/value pairs | `list_key_values`, `set_key_value`, `get_key_value_policy`, without secret values |

The two "understanding" tools are the fun ones: they replay the merge, so the assistant can show you exactly which document set a value and what it overrode.

## Connect a client

Point your MCP client at the server of your deployment, `https://<the MCP host>/mcp` (Streamable HTTP). The client finds everything else on its own: the server answers an anonymous call with the address of its metadata, the client learns the identity provider from it, and you sign in in your browser.

- **AuthKit and Keycloak** deployments let clients register themselves: nothing to set up.
- **Entra ID** deployments need a client id registered in the tenant; ask your administrator for it.

Then just ask: *"List my projects"* is a good first sentence.

::: tip The calls are plain API calls
Every tool call is a call of the Storyteller API with a short-lived token that names you, and the server as the one acting for you. Whatever your role can't do, the assistant can't either.
:::

## Good to know

- **Secret tools are off** unless the deployment turns them on, and then the API still has to allow the server's actor to read secrets. Read secrets with `sform` or the admin UI.
- **A value you type into the chat is in the chat.** Writing a secret through `set_key_value` puts it into the model's context; `sform` and the admin UI keep it out.
- **Not everything is a tool.** View exports and imports, key rotation, client-side encryption, members and invitations stay with `sform` and the admin UI.
- **Interrupted write?** The server says so and names the read that checks whether it went through. Ask the assistant to check before repeating.

## Reference

[MCP Server](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/mcp.md) on GitHub: every tool, the sign-in per identity provider, and how to host the server yourself.
