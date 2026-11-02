# All State in the Values Store for the Secrets Policy Links

Linear: [424-159](https://linear.app/424fun/issue/424-159/admin-ui-secrets-policy-in-use-link-opens-the-deleted-keys-instead-of) — Admin UI: secrets policy "in use" link opens the deleted keys instead of the keys in use.

## Problem

In **Settings › Secrets**, turning off a level that keys still carry fails with `409 ProtectionInUse`. The card then lists the views that use the level, each with a link to the Values store of that view (`…/key-values?level=<level>&state=All`).

The Values store knows only two states, Active and Deleted, and maps `state=All` to **Deleted**. A Standard secret that is active is therefore not listed: the page shows "No deleted keys", and the user has to click **Active** to find it.

The issue leaves the choice open: support an "All" state in the Values store, or link to the active keys. The counts of the 409 include the deleted keys too (`CosmosKeyValuePolicyService.CountInUseAsync` has no filter on the `Deleted` stamp; `docs/Platform/Storyteller/key-values.md` says "active or deleted"). A link to the active keys alone would show fewer keys than the count, and the user would not see the deleted keys that still block the change. The link must show **both**, so the Values store gets an **All** state. The API already accepts `state=All` (`KeyValueState.All`), and so does the mock.

## Current State

- `app/components/secrets/SecretsPolicyCard.vue:147–153` — the links of the `ProtectionInUse` alert: `query: { level: inUse.level, state: 'All' }`.
- `app/pages/[org]/[project]/[view]/key-values.vue`:
  - line 32: `state` is `'Active' | 'Deleted'`; `'Deleted'` and `'All'` both give `'Deleted'`.
  - line 44: the list asks the API for `state: 'Deleted'` or no state (Active).
  - lines 155–170: the row actions already look at `row.IsDeleted` (no Reveal and no Delete for a deleted row).
  - lines 194–201: the last column is **Expires** in the Deleted state and **Updated** otherwise.
  - lines 237–240: the **Active** and **Deleted** buttons of the toolbar.
  - lines 245–251: `KeyValueBulkBar` gets the state of the page.
  - lines 254–262: the empty state ("No deleted keys" in the Deleted state, **New key** only in the Active state).
  - lines 275–284: the mobile list shows the key, the level, the value and the version.
- `app/components/key-values/KeyValueBulkBar.vue:19,131–139` — `state: 'Active' | 'Deleted'`: Delete and Change protection for active keys, Restore and Purge for deleted keys. The bulk calls of the API work on keys of one state only.
- `app/pages/[org]/[project]/[view]/key-values/[key].vue:15` — the detail keeps `state` in the query of its tabs and of Close, so it already carries `All` back to the list.
- `app/mock/key-value-handlers.ts:62` — the mock lists active, deleted or all keys.
- Mock data: `acme/billing/default` has the deleted `legacy.endpoint` (Public) beside the active keys.
- Test SET-SEC-02 (Testiny 170, `SettingsSecretsPolicyTests`, from 424-152) accepts both behaviours today; it lives outside this repository and is tightened there.

## Proposed Changes

### 1. The All state of the Values store (`key-values.vue`)

- `state` becomes `'Active' | 'Deleted' | 'All'`: `route.query.state` `Deleted` → Deleted, `All` → All, anything else → Active.
- The list asks the API for `state: 'Deleted'`, `state: 'All'` or nothing (Active).
- The toolbar gets a third button, **All**, after **Deleted** (`setQuery({ state: 'All' })`).
- In the All state:
  - a deleted row carries a **Deleted** badge (`UBadge`, `i-lucide-trash-2`, warning, subtle, as in the detail header) after the key, in the table and in the mobile list;
  - the last column is **Updated / expires**: an active row shows the relative time, a deleted row "expires in N days";
  - the row actions keep following `row.IsDeleted` (unchanged).
- The empty state of the All state: "No key values in this view" with the description of the Active state; **New key** shows in the Active and All states.
- A helper `isDeletedRow(row)` = `state === 'Deleted' || (state === 'All' && !!row.IsDeleted)` drives the badge and the expiry cell.

### 2. The bulk bar with a mixed selection (`KeyValueBulkBar.vue`)

The bulk calls work on one state at a time. In the All state the page passes the state of the selection:

- every selected key active → `'Active'` (Delete, Change protection);
- every selected key deleted → `'Deleted'` (Restore, Purge);
- both → `'Mixed'`: the bar shows "Select only active or only deleted keys for bulk actions." and **Clear selection**, no actions.

The prop type becomes `'Active' | 'Deleted' | 'Mixed'`. The page computes it from the selected keys and the loaded rows (`list.data.value?.Items`, not the searched rows, so a search does not change it).

### 3. The link of the Secrets policy card

`SecretsPolicyCard.vue` keeps `state: 'All'`: with change 1 it opens the keys of the level in both states. The comment of the card says so.

### 4. Tests

- `test/unit/key-values/key-values-page.test.ts`: a new `describe` for the All state on `acme/billing/default`:
  - `?state=All` lists the active `db.host` and the deleted `legacy.endpoint`, the **All** button is pressed, and only the deleted row carries the **Deleted** badge;
  - `?level=Standard&state=All` (the link of the card) lists `db.password`;
  - selecting an active and a deleted key shows the mixed hint and no Delete or Restore;
  - `?state=Deleted` still lists only `legacy.endpoint`.
- A unit test of `SecretsPolicyCard` is not added: the link target is unchanged; the page test covers the URL it opens.

### 5. Documentation

`docs/Platform/Storyteller/key-values.md`, section **Admin UI**: the toolbar switches between the active, the deleted and all keys; a deleted row in the All state carries a badge; a bulk action needs keys of one state; the Secrets links open all the keys of the level in the view.
