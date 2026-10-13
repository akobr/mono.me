# Phase 4 of Native Key-Value Storage

## Overview

Phase 4 of [Native Key-Value Storage](../../../Storyteller/specs/2026-10-11%20Native%20Key-Value%20Storage.md) (section 29) brings the key/value pairs to the administration UI: the **Key values** section of a view with the list, the detail (value, reveal, set new value, versions), import and export; the project's **Secrets** settings (allowed levels, keys and rotation, the customer key, the recipients); and the **Audit** and **Usage** pages. HYOK values are encrypted in the browser with the `./hyok` entry of the TypeScript SDK (Phase 2), so a plaintext never leaves it. A stateful mock of the key/value API backs the mock mode, the unit tests and the end-to-end suite.

The admin UI is green: `vue-tsc` (typecheck), `oxlint` and ESLint, Vitest 174 (17 new), the production build, and Playwright 77 passed and 1 skipped across every flow on desktop and mobile, 22 of them new (the suites ran in the installed Chrome through `PLAYWRIGHT_CHANNEL=chrome`, because Playwright's own browser download is not present on this machine). The visual baselines of the two new screens are not made yet; see "Left out".

## What Was Done

### 29.1 Navigation, routes and data

- `app/utils/navigation.ts`: **Key values** (`i-lucide-key-round`, edit role Contributor) after Configurations in the view section; **Secrets** (`i-lucide-shield`, visible to everyone, locked below Administrator), **Audit** and **Usage** (`i-lucide-scroll-text`, `i-lucide-gauge`, Administrator) in the project section.
- Pages: `[org]/[project]/[view]/key-values.vue` (the list), `key-values/[key].vue` with `index.vue` (Value) and `versions.vue` (Versions) in a `DetailPanel`, `[org]/[project]/secrets.vue`, `audit.vue` and `usage.vue`. **Deviation:** Secrets is a page of its own instead of a tab of Settings; the file-based router and the sidebar fit a page better, and the Settings page stays what it was.
- `queries/scope.ts`: `useKeyValues`, `useKeyValue`, `useKeyValueVersions(key, enabled)`, `useKeyValuePolicy`, `useRecipients`, `useKeyValueAudit`, `useKeyValueUsage` on the generated composables; `queries/invalidation.ts`: `invalidateKeyValues` (`/v1/{org}/{project}/{view}/key-values`) and `invalidateKeyValuePolicy` (`/v1/{org}/{project}/key-values`, which also covers the recipients).
- `forms/names.schema.ts` reserves `key-values`, `audit` and `usage` like the API (Phase 2). `utils/machines.ts` describes the two new machine scopes, so the machine dialog offers them.
- The linked SDK package had to be reinstalled (`node_modules/@42for.net/2splatform.sdk` is a copy of the `file:` dependency, not a link), which is why the generated types of Phase 2 were not visible to the UI before.

### 29.2 Key values list

- `pages/…/key-values.vue`: a `UTable` on desktop and a list on mobile. Columns: a selection checkbox, the key (monospace), the level badge (`KeyValueLevelBadge`: icon and text per level, `Reference` with a link icon), the first line of a public value or `••••••••`, the version, the author, and Updated (or the expiry in the deleted state). The toolbar searches keys and public values (client-side, in the URL as `q`), filters by level (`level`), switches Active and Deleted (`state`), and the header has Export, Import and New key. Row actions: Open, Copy key, Copy binding (`@key`), Reveal (secrets, with the `secrets` capability), Delete (with the restore note), Delete permanently (Administrator, type the key). The selection shows `KeyValueBulkBar`: Delete and Change protection in Active, Restore and Purge (type the view name) in Deleted; one bulk call each, the outcome per key in a toast, and the failed keys stay selected.
- `ByokBanner` shows "Customer key unavailable" with the time and the last error across the section, linked to the Secrets settings, when the policy says `Health = Unavailable`.
- **Deviation:** the per-row Change protection lives in the detail (a dropdown next to the level) and in the bulk bar, not in the row menu, which stayed short.

### 29.3 Detail and editor

- `KeyValueDetail.vue`: the metadata, the binding to copy with a source selector (`@key`, `@public(key)`, `@secret(key)`), and the value by level. A public value is edited in place (`CodeEditor` in JSON mode for `application/json`, a textarea otherwise; a reference is explained). A server-side secret shows a masked field with **Reveal** through `useRevealedValue` (a fresh `GET …?reveal=true`, never cached, masked again after 60 seconds and on unmount; copying shows the clipboard toast) and **Set new value** with a level selector that explains what happens to older versions. A HYOK value shows its recipients and **Set new value** encrypts in the browser with `encryptForRecipients` for the enabled recipients. A deleted key opens read-only with Restore and its expiry.
- Saves send `ExpectedVersion`. A 412 shows an inline notice with the fresh version and author and offers Reload or Overwrite (**deviation:** not the JSON `ConflictDialog`, whose side-by-side document diff does not fit a string value), a 409 `ProtectionNotAllowed` points to the Secrets settings, a 424 explains the customer key. `useUnsavedChanges` guards the route and Ctrl/Cmd+S saves.
- `KeyValueCreateDialog.vue` (New key): VeeValidate with `forms/key-value.schema.ts` (the key rule of the SDK, the levels, the 24,576-byte limit), a **Secret** switch for the project's default secret level, the level from the policy's allowed levels (secret levels only with the `secrets` capability), the content type including "Secret reference", and HYOK encryption before the request; `ExpectedVersion: 0` turns an existing key into a field error.

### 29.4 Versions

- `KeyValueVersions.vue` loads the versions only while the tab is open (`useKeyValueVersions(key, true)` mounts with the route). The table shows the version, the level badge, the author, Created and Expires; **View** fetches one version (a secret with `reveal`, on request, never cached) and **Compare** the diff with the previous version, rendered by the existing `HunkList`. HYOK shows the timeline with the explanation that the platform cannot read the values. **Deviation:** the versions do not go through `VersionsView` and `VersionSource`, whose content model is a JSON document with format toggles; a string value needs the simpler table.

### 29.5 Import and export

- `KeyValueImportDialog.vue`, three steps: the source (a file or pasted text, the format detected with `detectKeyValueFormat` and parsed by `utils/key-values.ts`: a document, flat or nested JSON, `.env`), the review (a level per row, "Mark keys matching … as" with a glob, Merge or Replace with a prefix, **Check** for the server's dry run and its plan, invalid keys marked), and the result (counts per action, **Retry failed** with a Merge of the failed rows, **Undo deletes** with one restore call). Replace with deletes asks for the view name. HYOK rows are encrypted in the browser before both the check and the import.
- `KeyValueExportMenu.vue`: the key/value document, `.env` (`serializeDotEnv`) or flat JSON (`toFlatJson`), downloaded through a Blob URL; "Include secret values" is a checkbox item for the `secrets` capability and the download warns about the secrets, or about the secrets left out of the two value-only formats.

### 29.6 Secrets settings

- `components/secrets/SecretsPolicyCard.vue`: the five levels as switches with the description, the cost hint and the requirement (`levelMeta` in `utils/key-values.ts`); the default secret level; "Require mTLS for secret delivery" (turning it off asks for a confirmation). A 409 `ProtectionInUse` is parsed into the counts per view with links to the filtered lists; a `PolicyVersionMismatch` asks for a reload.
- `SecretsKeysCard.vue`: the Standard and HSM keys (id, current version, next rotation, the backend), **Rotate now** with a confirmation, and the re-wrap progress from `Rewrap`.
- `ByokCard.vue`: the wizard in three parts: the consent link and the role with an `az role assignment create` line (from `GET …/policy/byok/setup`), the key URI and tenant id (saved through the policy), and Verify with the health, the verified time, the key version and the last error.
- `RecipientsCard.vue`: the list with name, id, key size, added and state; add by pasting a PEM or JWK; enable, disable and remove, with the warnings about re-encryption (the CLI command for it is named, since the browser never holds a private key).

### 29.7 Audit and Usage

- `audit.vue`: filters (view, key, operation, from, to), the table (time, operation with the detail, view, key, version, level, actor, channel, count) and "Load more" through the continuation token; the correlation id is shortened and copyable.
- `usage.vue`: a date range (the current month by default), the totals per meter with readable labels, the estimated charges with their sum when the deployment has prices, and a daily chart of reads and keys drawn with CSS bars (`role="img"` with a label, titles per day). **Deviation:** a lightweight chart instead of a chart library.

### 29.8 Security, mocks and tests

- Secret values and HYOK plaintext never enter the query cache: reveals and version views are direct calls; the masked value returns after 60 seconds; copying a secret shows the clipboard toast. Role gating uses `useCan('edit')`, `useCan('secrets')` and `useCan('manageAccess')` as the spec lists them; the API enforces the same rules.
- `app/mock/key-values.ts` and `key-value-handlers.ts`: a stateful store per scope (`acme/billing/default` is seeded with pairs of every level, a reference, a deleted key and histories; `staging` with two), the policy per project (`acme.billing` allows Public, Standard, HSM and HYOK, has an HSM key and a recipient with a real 3072-bit public key, so the browser can encrypt for it), the audit trail with seeded events, and deterministic usage per day. The handlers mirror the service: the key rule, `ProtectionRequired`, `ProtectionNotAllowed`, secret rights by role, `KeyValueVersionMismatch`, references, the JWE checks (place and recipients), bulk outcomes, the import plan with Replace deletes, `ProtectionInUse` with counts per view (the stores of the project's views are seeded first), `ByokNotConfigured`, verification by the key URI, rotation with a completed re-wrap, recipient rules, the audit filters and paging. `mock/diff.ts` gained `diffTexts` for the version diffs. They are registered after the other hand-written handlers and before the generated ones.
- Vitest (`test/unit/key-values/`): `key-values-utils.test.ts` (bindings, previews, expiry days, `ProtectionInUse` parsing, import parsing of the three formats, globs and documents, the form rules, the 60-second masking with fake timers), `key-value-handlers.test.ts` (the mock through the SDK: listing and filters, writes with the optimistic check and the kept level, reveals, versions and diffs, delete, restore, protect and purge outcomes, the import plan and Replace, the policy rules, the reader and contributor refusals), `hyok.test.ts` (encryption for the mock recipient and a round trip with a generated key pair on WebCrypto).
- Playwright `test/e2e/key-values.spec.ts`, desktop and mobile: create a public key with its binding, a Standard secret with reveal and masking, the Reader experience, a HYOK key encrypted in the browser, Change protection to HSM with the old version at Standard, delete, purge and restore, versions loaded only when the tab opens (the request is observed), an `.env` import with a secret pattern and the plan, the `.env` export download, the BYOK wizard, the audit filter and the usage page. `accessibility.spec.ts` checks the list, the detail, the Secrets settings, Audit and Usage; two findings were fixed on the way (the chart's list roles, and a usage body without a focusable element, which moved the date range into the body). `visual.spec.ts` lists the key values list and the Secrets settings.
- Documentation: the `ui.admin` README names the section and its pieces; `key-values.md` has the Admin UI section.

### Left out

- The visual baselines of `key-values` and `secrets` are not generated: they are made in the Playwright Linux container (`npm run test:visual:docker -- --update`), which was not run here. Until then `test:visual` reports the two screens as missing.
- The import dialog does not validate pasted references or JSON content types per row beyond the key rule; the server does on Check.
- From the earlier phases: the manual smoke test against real Azure, `x-ms-client-request-id` on Key Vault calls, and the `hsm.keyVersions` snapshot.
