# Developer Guide of the 2S Platform on 42for.net

## Overview

This review covers the first version of [Developer Guide of the 2S Platform on 42for.net](../2026-11-19%20Developer%20Guide%20of%20the%202S%20Platform%20on%2042for.net.md): a short, playful guide for developers on the docs site, with one page per package the platform ships. It lives in `docs/42for.net/platform/documentation/` as a collapsed **Developer guide** group of the `2S Platform` sidebar. Commits: `6048542ef` (the spec) and `60dd8d48a` (the guide), on the branch `feat/claude/platform-developer-guide`.

## What Was Done

### 1. A collapsed sidebar group "Developer guide"

`docs/42for.net/.vitepress/config.mjs` has the nested group after `Examples` and before `Live demo`, with `collapsed: true` and the twelve pages of the spec, in its order. VitePress opens the group when the current page is inside it, and keeps it closed everywhere else.

### 2. The voice

Every page follows the same shape: the package name in bold and one sentence, a quoted one-liner joke, **Why you'll like it**, install, the smallest code that works, the next two or three things, **Good to know**, and a **Reference** link to the documentation on GitHub. The jokes stay in intros, titles and tips; the code samples, options, variables and defaults were checked against the reference documentation and the sources on `main` (for example `KeyVaultReferenceResolverOptions.AllowedVaults` as a get-only `IList<string>`, `FormattedConfiguration.Text`, the signatures of `GetAnnotationsAsync`, `KeyValueClientEncryption` and `StorytellerCredential`). The examples share one story: the organization `house`, the project `main`, Northwind using invoicing in production, and the `retries: 7` that nobody can explain.

### 3. The start page (`index.md`)

As planned: 2S in one minute (the `appsettings.*.FINAL-v2-really.json` folder and a Mermaid diagram of the merge), the seven annotations as a table, the anatomy of a full key, the "Which package do I need?" flowchart and the package map of twelve rows. `Abstractions.Annotations` and `KeyValues.ClientEncryption` link to sections of the C# SDK page, `2sInitDb` is one sentence for self-hosters, and **Getting the bits** says that the packages are `0.10.x-alpha` prereleases and how to pack one from the repository until it is on nuget.org.

### 4. The quick start (`quick-start.md`)

Five steps, each ending in **You now have**, with `:::tabs` for `sform` and the admin UI in the steps that write to the platform, and for bash and PowerShell when the variables are set. A warning says that the `sform` on nuget.org (`0.8`) is older than the guide, and shows `dotnet pack` and `dotnet tool install --add-source`. "The party trick" changes a value while the service runs and finds its author with `sform story config versions`.

### 5. One page per package

All as planned, with these decisions:

- `configuration.md` shows the refresher behind two endpoints (a health check over `Status.IsDegraded` and a refresh button over `TryRefreshAsync`), as the package ships no health check.
- `hosting.md` catches `OptionsValidationException` and `StorytellerConfigurationException` per customer in the worker, as the reference does, in a shorter form.
- `csharp-sdk.md` holds the two packages that come along, under **The packages that come along** and **Secrets the platform cannot read**.
- `typescript-sdk.md` installs `@42for.net/2splatform.sdk` without a tag, because the release workflow publishes as `latest`; an info box says that npm has an old `0.8` today.
- `cli.md` is a short tour (ten commands, machines per job, the GitHub Action, five exit codes) that links to the existing `/cli/sform` and `/cli/github-action` pages instead of repeating them.
- `mcp.md` does not name a hosted address (`https://<the MCP host>/mcp`), because the hosted server is not announced on the site yet.
- `cheat-sheet.md` has the variables, the keys, the roles, the scopes and **When things go sideways**, a table of ten symptoms.

The API endpoint of the examples is `https://api.2s.42for.net/api`, as in the Aspire documentation and the readme of the TypeScript SDK.

### 6. Links from the existing pages

`platform/introduction.md` got a **For developers** section, and the paragraph about the .NET packages in `platform/using-the-platform.md` links to the guide and its configuration page.

### 7. Verification

- `npm ci` and `npm run build` in `docs/42for.net`: the build completes, so no link of the guide is dead (VitePress fails the build on dead links).
- `npm run preview` on port 4173: the start page, the quick start and the sidebar were checked in a browser. The Mermaid diagrams render, the tabs switch, and the group is open on its own pages.
- Not verified: the code samples were not compiled into a project, and the quick start was not run against a deployment.

### Out of scope, for follow-ups

- Once the packages are on nuget.org and npm, drop the warnings about packing from the repository (start page, quick start, TypeScript SDK, `sform & CI`).
- Name the hosted MCP server's address on `mcp.md` once it is announced.
- A compiled sample per page (or a link to the playground's projects for each) would keep the snippets honest as the APIs move.
