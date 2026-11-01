Linear: [424-71](https://linear.app/424fun/issue/424-71/make-everything-run-locally-dev-mode)

# Self-Preparing Admin UI Resource for the Aspire Host

## Problem

On a clean clone, `dotnet run --project src/Platform/Aspire.Host/src` starts every resource except `ui-admin`. Its Vite dev server fails because two manual steps were never run:

1. `npm ci` and `npm run build` in `src/Platform/Storyteller/sdk.typescript/src` (the UI depends on the SDK's `dist/`).
2. `npm ci` in `src/Platform/ui.admin/src`.

The dashboard shows a `ui-admin-installer` child, but it does not prepare anything. The developer has to know the steps from a comment in `AppHost.cs` or from the ui.admin README. After an SDK change, the developer also has to know the reinstall trick (`rm -rf node_modules/@42for.net && npm install`).

The goal: starting Aspire is enough. The host installs and builds what the UI needs. It skips work that is already up to date, so restarts stay fast, and the dashboard has a command for a clean reinstall.

## Current State

- `src/Platform/Aspire.Host/src/AppHost.cs:66-76`:
  ```csharp
  builder.AddViteApp("ui-admin", "../../ui.admin/src", "dev:aspire")
      .WithNpm(install: false)
      .WithEndpoint("http", e => { e.Port = 5173; e.TargetPort = 5173; e.IsProxied = false; })
      .WithEnvironment("VITE_API_BASE_URL", ...)
      .WithReference(apiFunctions);
  ```
  `Aspire.Hosting.JavaScript` is 13.1.2 (`Directory.Packages.props`). `AddViteApp` already registers npm with an installer. The later `WithNpm(install: false)` sets only the package manager annotation, which is likely why an inactive `ui-admin-installer` still shows up. `WithNpm(install, installCommand, installArgs)` creates a `JavaScriptInstallerResource` (`<name>-installer`) that runs `npm <installCommand> <installArgs>` in the app folder. The app waits for that resource to complete, and the resource is reachable through `JavaScriptPackageInstallerAnnotation`.
- `src/Platform/ui.admin/src/package.json`:
  - It depends on `"@42for.net/2splatform.sdk": "file:../../Storyteller/sdk.typescript/src"`.
  - `src/.npmrc` sets `install-links=true`, so npm installs the SDK as a **packed copy**, not a symlink. A rebuilt SDK changes neither the lockfile nor the version, so the copy has to be reinstalled.
  - `build/sdk-freshness.ts` (a Vite plugin) already stamps the installed `dist` and re-optimizes the dependencies when the stamp changes.
- `src/Platform/Storyteller/sdk.typescript/src`:
  - `generated/` is committed, so Orval does not need to run.
  - `npm run build` runs tsup with `clean: true`, writing `dist/` from `index.ts`, `vue-query.ts`, `zod.ts`, `msw.ts`, `hyok.ts`, `lib/` and `generated/`.
  - `scripts/generate.mjs` is an existing plain-Node script.
- Node 22.18 or newer is a documented prerequisite (`docs/Platform/development.md`). Node is not installed by any tooling.
- Docs that mention the manual step:
  - the `AppHost.cs` comment;
  - `src/Platform/ui.admin/README.md` (section *Aspire*: "It does not install packages…");
  - `docs/Platform/Aspire.Host/local-development.md` (resource table).

## Proposed Changes

Approach (decided): keep Aspire's built-in installer resource for the UI, and add one resource before it that builds the SDK. Both steps skip work that is up to date, based on content hashes. The `ui-admin` resource gets a dashboard command for a clean reinstall.

Resulting chain in the dashboard:

```
ui-admin-sdk  (executable, completes)  →  ui-admin-installer (completes)  →  ui-admin (Vite, running)
```

### 1. SDK preparation script: `sdk.typescript/src/scripts/prepare-local.mjs`

This is a plain Node ESM script with no dependencies (only `node:` modules), run with `node scripts/prepare-local.mjs [--force]`.

1. **Install the SDK's dependencies.**
   - It computes the SHA-256 of `package-lock.json`.
   - When `node_modules/.prepare-local-install` is missing or holds a different hash, or `--force` is set, it runs `npm ci` and then writes the hash to that file.
   - Otherwise it logs `SDK packages up to date` and skips the install.
2. **Build the SDK.**
   - It computes the SHA-256 over the sorted relative paths and contents of the build inputs: `index.ts`, `vue-query.ts`, `zod.ts`, `msw.ts`, `hyok.ts`, `lib/**`, `generated/**`, `tsup.config.ts`, `tsconfig.json`, `package.json` and `package-lock.json`.
   - When `dist/.prepare-local-build` is missing or different, `dist/` lacks one of the entry `.js` files, or `--force` is set, it runs `npm run build` and then writes the hash.
   - Otherwise it logs `SDK build up to date` and skips the build.
3. **Run commands across platforms.**
   - It spawns `npm` with `shell: true` (`npm.cmd` on Windows) and `stdio: 'inherit'`, so the output appears in the resource's console log in the dashboard.
   - A non-zero exit code from any step ends the script with that code.
4. **Check Node.**
   - It checks `process.versions.node` against the minimum (22.18).
   - If Node is older, it fails with a clear message instead of an obscure tsup error.

The script only stamps files and never deletes them. `tsup` with `clean: true` deletes `dist/` itself, so the build stamp is written after the build.

### 2. UI preparation script: `ui.admin/src/build/prepare-local.mjs`

This is the same kind of script, run as the command of the built-in installer resource.

1. **Compute the install key.** The key is the SHA-256 of `package-lock.json` plus the build stamp of the SDK (`../../Storyteller/sdk.typescript/src/dist/.prepare-local-build`). The SDK is a packed copy (`install-links=true`), so a rebuilt SDK has to change the key.
2. **Decide what to do.**

   | State | Action |
   |---|---|
   | `node_modules/.prepare-local-install` is missing, or the lockfile hash differs, or `--force` | `npm ci` |
   | Only the SDK part of the key differs | Remove `node_modules/@42for.net`, then run `npm install --no-save --no-audit --no-fund`. This is the reinstall the README already prescribes, so `sdk-freshness.ts` picks up the new copy. |
   | Key matches | Log `UI packages up to date` and skip |

   After a successful step, the script writes the key.
3. The script uses the same spawning, exit code and Node checks as script 1. The shared code is small (about 30 lines: hashing and spawning), so each script has its own copy. That keeps both scripts free of dependencies and keeps each package self-contained. Two copies are acceptable.

A `package.json` script is added to expose it: `"prepare:local": "node build/prepare-local.mjs"`. `npm run` works without `node_modules`, so the installer can call it on a clean clone. Developers who don't use Aspire can run `npm run prepare:local` too. It does the same as the two README steps, but in the right order and only when needed. It does not run the SDK script; the SDK keeps its own `"prepare:local"` script.

### 3. `AppHost.cs` wiring

```csharp
var sdkFolder = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../Storyteller/sdk.typescript/src"));

var uiAdminSdk = builder.AddExecutable("ui-admin-sdk", "node", sdkFolder, "scripts/prepare-local.mjs")
    .WithEnvironment("FORCE_COLOR", "1")   // optional, for colored npm output in the dashboard
    .ExcludeFromManifest();

var uiAdmin = builder.AddViteApp("ui-admin", "../../ui.admin/src", "dev:aspire")
    .WithNpm(install: true, installCommand: "run", installArgs: ["prepare:local"])
    .WithEndpoint(...)                     // unchanged
    .WithEnvironment("VITE_API_BASE_URL", ...)
    .WithReference(apiFunctions);

// The installer (built-in, ui-admin-installer) waits for the SDK build; ui-admin already waits for the installer.
if (uiAdmin.Resource.TryGetLastAnnotation<JavaScriptPackageInstallerAnnotation>(out var installer))
{
    builder.CreateResourceBuilder(installer.Resource).WaitForCompletion(uiAdminSdk);
}
```

- Parent-child display: `uiAdminSdk.WithParentRelationship(uiAdmin)`, so the SDK step is nested under `ui-admin` next to the installer.
- If `WithNpm` called a second time adds a second installer instead of replacing the one from `AddViteApp`, the fallback is to look up the installer through the annotation and reconfigure it: set its args with `WithArgs`, or replace them with `WithCommand` / `WithArgs` callbacks. This will be checked against 13.1.2 during implementation, and the review will record which form was used.
- If the SDK step or the installer fails, `ui-admin` stays at *Waiting*, and the failed child shows its exit code and log. That is the intended feedback.
- The comment above the resource is rewritten. The "Run `npm ci` first" sentence goes away, and the new comment describes the chain and the command.

### 4. Dashboard command: "Clean reinstall"

On `ui-admin`, add `WithCommand("clean-reinstall", "Clean reinstall", ...)` with the icon `ArrowSync` and a confirmation message ("Stops the admin UI, then reinstalls and rebuilds the SDK and the UI packages from scratch").

The handler:
1. stops `ui-admin` through `ResourceCommandService` (`KnownResourceCommands.StopCommand`);
2. deletes the three stamps: SDK install, SDK build and UI install;
3. restarts `ui-admin-sdk`, then `ui-admin-installer`, then `ui-admin` (`KnownResourceCommands.StartCommand` / `RestartCommand`, each awaited). With no stamps, every step runs in full.

Deleting stamps does the same as `--force` and doesn't need another way to pass arguments. If it turns out that starting a completed executable doesn't re-run it, or that the wait annotations aren't re-evaluated, there is a fallback: the handler runs the two scripts itself with `--force` (as a `Process` with the output in the host log) and then restarts `ui-admin`. The review records which one was used.

The command is enabled whenever `ui-admin` is not already in `Starting`.

### 5. What is out of scope

- **Installing Node or npm.** Node must be on `PATH`. If `node` is missing, the dashboard shows `ui-admin-sdk` as failed to start. The docs state the requirement next to the resource.
- **Regenerating the SDK from OpenAPI** (`npm run generate`). `generated/` is committed and its freshness is checked by CI.
- **Watching for changes while running.** A lockfile or SDK source change during a session takes effect when the developer restarts `ui-admin` (or uses the command). The skip logic then does only what changed.
- **Running `npm run dev:mock` at the same time.** A full `npm ci` replaces `node_modules` while the mock server uses it. Because of the skip logic, that happens only when the lockfile changes.

### 6. Tests

- The `.mjs` scripts have no unit tests of their own. They are checked by hand in the review:
  - a clean clone (no `node_modules`, no `dist`): full chain;
  - a second start: every step skipped, and `ui-admin` running in a few seconds;
  - an SDK source change: SDK rebuild plus the UI's SDK-only reinstall;
  - a lockfile change: `npm ci`;
  - the command: everything rebuilt.
- `dotnet build src/Platform/Aspire.Host/src` must stay clean (StyleCop).

### 7. Documentation

- `src/Platform/ui.admin/README.md`, section *Aspire*:
  - Replace "It does not install packages…" with a description of the chain, the stamps and the *Clean reinstall* command.
  - Mention `npm run prepare:local` in *Getting started* as the one-step alternative to the two manual commands.
- `src/Platform/Storyteller/sdk.typescript/README.md`: mention `npm run prepare:local`.
- `docs/Platform/Aspire.Host/local-development.md`: add `ui-admin-sdk` and `ui-admin-installer` to the resource table, and note that Node 22.18+ is required and the first start takes a while.
- `docs/Platform/development.md`: same note where `ui-admin` is listed.
- After implementation: a review in `docs/Platform/Aspire.Host/specs/reviews/`.
