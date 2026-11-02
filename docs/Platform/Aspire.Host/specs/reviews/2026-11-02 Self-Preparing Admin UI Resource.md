Linear: [424-71](https://linear.app/424fun/issue/424-71/make-everything-run-locally-dev-mode)

# Self-Preparing Admin UI Resource

## Overview

This review covers the whole of [Self-Preparing Admin UI Resource for the Aspire Host](../2026-11-01%20Self-Preparing%20Admin%20UI%20Resource%20for%20the%20Aspire%20Host.md). The Aspire host now gets the admin UI running on a clean clone without any manual step:
- `ui-admin-sdk` installs and builds the TypeScript SDK;
- `ui-admin-installer`, Aspire's own installer, installs the UI's packages;
- `ui-admin` starts Vite after both finish.

Each step skips work that is already up to date, based on content-hash stamps. A **Clean reinstall** command on `ui-admin` redoes everything. Both points the spec left open were settled in favor of the primary design, so neither fallback was needed.

## What Was Done

### 1. SDK preparation script

`src/Platform/Storyteller/sdk.typescript/src/scripts/prepare-local.mjs` is a plain Node ESM script that uses only `node:` modules. It is exposed as `npm run prepare:local`.
- **Install:** it runs `npm ci --no-audit --no-fund` when `node_modules/.prepare-local-install` does not match the SHA-256 of `package-lock.json`.
- **Build:** it runs `npm run build` when `dist/.prepare-local-build` does not match the hash of the build inputs, or when an entry `.js` file is missing from `dist/`. The build inputs are the five entry `.ts` files, `lib/`, `generated/`, `tsup.config.ts`, `tsconfig.json`, `package.json` and the lockfile. The hash covers sorted relative paths and file contents.
- `--force` runs both steps.
- It checks for Node 22.18 or newer.
- npm is spawned with `shell` only on Windows, and its output is inherited.

The hashes use file contents, not modification times, so a `git checkout` of unchanged files does not trigger a rebuild.

### 2. UI preparation script

`src/Platform/ui.admin/src/build/prepare-local.mjs`, run with `npm run prepare:local`, has the same structure. Its stamp holds `<lockfile hash> <SDK build stamp>`.
- If the lockfile changed, it runs `npm ci`.
- If only the SDK changed, it removes `node_modules/@42for.net` and runs `npm install --no-save`. The lockfile is not touched, and `sdk-freshness.ts` re-optimizes Vite's pre-bundled dependencies.
- If the SDK was never built, it stops with a message that names the SDK command.

Deviation: the file is `.mjs` like the SDK script. The other scripts in `build/` are `.ts` files run through Node's type stripping, but `.mjs` keeps the two scripts identical in form. The shared helpers (`checkNode`, `run`, `read`) are copied into both scripts, as the spec allowed. A lint pass (oxlint) asked for `toSorted()` and a simpler version check, and both were applied.

### 3. `AppHost.cs` wiring

The wiring lives in a new file, `src/Platform/Aspire.Host/src/UiAdminPreparation.cs`, as the extension `WithLocalPreparation(platformDirectory)`. `AppHost.cs` calls it in place of `.WithNpm(install: false)`, and its comment now describes the chain.
- The source of `Aspire.Hosting.JavaScript` 13.1.2 (`AddInstaller`) confirmed the spec's first open point. `AddViteApp` already creates `ui-admin-installer` and makes `ui-admin` wait for it. A second `WithNpm(install: true, "run", ["prepare:local"])` keeps that installer, and its arguments come from the last `JavaScriptInstallCommandAnnotation` at `BeforeStartEvent`.
  - This also explains the original problem. The old `WithNpm(install: false)` removed the wait and made the installer an explicit start, so the installer showed in the dashboard but never ran.
- `ui-admin-sdk` is `AddExecutable("ui-admin-sdk", "node", <sdk folder>, "scripts/prepare-local.mjs")`, with a parent relationship to `ui-admin` and excluded from the manifest. The installer resource, found through `JavaScriptPackageInstallerAnnotation`, gets `WaitForCompletion(ui-admin-sdk)`.
- The whole setup applies in run mode only. In publish mode, the install command annotation would otherwise turn into the Dockerfile's install step.
- The `FORCE_COLOR` environment variable from the spec was left out. npm and tsup already color their output in the dashboard.

### 4. "Clean reinstall" command

`WithCommand("clean-reinstall", "Clean reinstall", ...)` uses the `ArrowSync` icon, asks for confirmation, and is disabled while `ui-admin` is `Starting`. The handler does the following:
1. stops `ui-admin` through `ResourceCommandService`;
2. deletes the three stamps;
3. starts `ui-admin-sdk`, then `ui-admin-installer`, and waits after each step. `ResourceNotificationService.WaitForResourceAsync` waits for `Finished`, `Exited` or `FailedToStart`, using `StartTimeStamp` to tell the new run from the previous one;
4. starts `ui-admin`.

A non-zero exit code returns a failure that names the step. The spec's second open point is settled: starting a finished executable runs it again, so the fallback of running the scripts from the host process was not needed.

### 5. Out of scope

Unchanged from the spec. Node must be on `PATH`, SDK regeneration stays manual, and changes made during a session are picked up by starting `ui-admin-sdk` and `ui-admin-installer` again before restarting `ui-admin` (a restart of `ui-admin` alone does not re-run them; their earlier completed runs satisfy its wait), or by restarting the host.

### 6. Verification

Checked by hand on Windows with Node 23.11 and npm 10.9.2:
- **Scripts alone:**
  - an SDK source edit led to an SDK rebuild and then the UI's SDK-only reinstall (`added 1 package`);
  - reverting the edit did the same again;
  - with nothing changed, both scripts printed `up to date` in about 1 s.
- **Clean clone under Aspire** (both `node_modules` folders and `dist/` deleted): `ui-admin-sdk` finished, `ui-admin-installer` ran `npm run prepare:local`, and `ui-admin` served `http://localhost:5173`.
- **Second start of the host:** both steps logged `up to date`, and Vite was up 12 s after the host started.
- **Clean reinstall,** run through the dashboard's MCP endpoint (`execute_resource_command`):
  - the SDK ran a full `npm ci` (238 packages) and build;
  - the UI ran a full `npm ci` (715 packages);
  - the command returned after about 80 s, and Vite answered again shortly after.
- `dotnet build src/Platform/Aspire.Host/src` builds without errors or StyleCop warnings. The only warnings are the existing NU190x advisories about MessagePack.

Not checked: the failure path, where a step exits non-zero, and a Linux or macOS host.

### 7. Documentation

- `docs/Platform/Aspire.Host/local-development.md` has the two new resources in its table and a new **Admin UI** section covering the chain, the stamps, timing, the command, and the clash with `dev:mock`.
- `docs/Platform/development.md`: the `ui-admin` row says it prepares itself.
- `src/Platform/ui.admin/README.md`: *Getting started* uses `prepare:local`, and the *Aspire* section describes the chain and the command in place of "It does not install packages".
- `src/Platform/Storyteller/sdk.typescript/README.md` lists `npm run prepare:local`.
- `CLAUDE.md`: the Aspire paragraph mentions the self-preparing `ui-admin`.
