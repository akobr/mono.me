# All State in the Values Store for the Secrets Policy Links

## Overview

The links of a secret level in use (Settings › Secrets, after a `409 ProtectionInUse`) opened `…/key-values?level=<level>&state=All`, which the Values store showed as the deleted keys. The Values store now has an **All** state with the active and the deleted keys, so the links list every key that the count covers. Spec: [2026-11-02 All State in the Values Store for the Secrets Policy Links](../2026-11-02%20All%20State%20in%20the%20Values%20Store%20for%20the%20Secrets%20Policy%20Links.md). Linear: [424-159](https://linear.app/424fun/issue/424-159/admin-ui-secrets-policy-in-use-link-opens-the-deleted-keys-instead-of).

## What Was Done

### 1. The All state of the Values store

`app/pages/[org]/[project]/[view]/key-values.vue`:

- `state` is `'Active' | 'Deleted' | 'All'`; `?state=All` no longer becomes Deleted. The list asks the API for `state=All` (or `Deleted`, or nothing for Active).
- The toolbar has a third button, **All**, after **Deleted**.
- `isDeletedRow(row)` decides the last cell: in All, a deleted row shows "expires in N days" and an active one the relative time; the column is **Updated / expires**. The "expires" prefix was added during implementation: "in 80 days" beside "2 days ago" read like a time in the future.
- In All, a deleted row carries the **Deleted** badge of the detail header (`UBadge`, warning, `i-lucide-trash-2`) after the key, in the table (resolved `UBadge` in the render function) and in the mobile list.
- The empty state of All is the one of Active, with **New key**.
- The row actions follow `row.IsDeleted` as before.

### 2. The bulk bar with a mixed selection

- The page computes `bulkState` from the selected keys and the loaded rows (not the searched ones): `Active`, `Deleted` or `Mixed`; outside All it is the state of the page.
- `app/components/key-values/KeyValueBulkBar.vue`: the prop accepts `'Mixed'`, which shows "Select only active or only deleted keys for bulk actions." and **Clear selection** only.

### 3. The link of the Secrets policy card

`app/components/secrets/SecretsPolicyCard.vue` keeps `state: 'All'`; its comment says why (the counts include the deleted keys).

### 4. Tests

- `test/unit/key-values/key-values-page.test.ts`, new `Values store in the All state`: All lists `db.host` and the deleted `legacy.endpoint` with the badge and the expiry, and its button is pressed; `?level=Standard&state=All` (the link of the card) lists `db.password`; a mixed selection has no action and a deleted-only one has Restore; Deleted still lists only the deleted key.
- The button check compares the classes of the three buttons instead of looking for a variant class name, which Nuxt UI does not emit.
- The visual baselines `key-values-mobile-{light,dark}.png` were updated in the Playwright Linux image: the third button moves the "8 of 8" count to its own line on a phone. The desktop baselines did not change.
- Run: the key-values unit tests and `page-memory.test.ts` pass (the latter timed out only once under the load of the full suite and passes alone), `npm run lint`, `npm run build`, the key-values Playwright flows (desktop) and the visual suite (68 passed).
- SET-SEC-02 (Testiny 170, `SettingsSecretsPolicyTests`) can now require the secret to be listed directly; that test lives outside this repository.

### 5. Documentation

`docs/Platform/Storyteller/key-values.md`, **Admin UI**: the three states, the badge in All, no bulk action for a mixed selection, and the Secrets links opening all the keys of the level.
