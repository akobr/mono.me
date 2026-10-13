# Default Time Zone as a Project Setting

## Overview

This review covers the whole of the spec [Default Time Zone as a Project Setting](../2026-10-13%20Default%20Time%20Zone%20as%20a%20Project%20Setting.md), delivered in one phase (Linear [424-12](https://linear.app/424fun/issue/424-12/add-default-time-zone-as-a-project-setting)).

- Every project of the Administration UI now has a **default time zone**, `UTC` until someone changes it, on a new **Annotations** card of the project's Settings page.
- The **New annotation** dialog starts in that zone and lets the user pick another one. The API copies the zone to the missing ancestors it creates with the key.
- The API has no project settings document, and the issue limits the change to `ui.admin`. The setting is therefore kept in the browser's `localStorage`, per project, behind a store that can move to the API later.

## What Was Done

### 1. Where the setting lives

As planned, the setting lives in `localStorage` under `2s-admin:project-settings`, keyed by the access point key (`org.project`). Nothing was changed in Storyteller, the OpenAPI document or the SDK.

The Settings page and the user documentation both say that the value is kept in this browser. A shared setting needs a project settings document on the access point, next to `MachineAuthentication`. That remains a follow-up.

### 2. Store: `app/stores/project-settings.ts`

- `useProjectSettingsStore`, a Pinia setup store, is auto-imported like `preferences`. `app/auto-imports.d.ts` was regenerated with `useProjectSettingsStore`, `defaultProjectSettings` and the `ProjectSettings` type.
- `ProjectSettings { defaultTimeZone: string }` and `defaultProjectSettings = { defaultTimeZone: 'UTC' }`.
- **Storage:** `useStorage<Record<string, Partial<ProjectSettings>>>('2s-admin:project-settings', {}, undefined, { onError })`. An entry only holds what was changed. No storage argument is passed, so VueUse resolves `localStorage` itself: when the browser blocks it, `onError` logs a warning and the store keeps working in memory with the defaults instead of throwing.
- **`settingsOf(projectKey)`** returns the defaults merged with the stored entry. A missing project key, a missing entry, or a stored zone that `isValidTimeZone` rejects all give `UTC`.
- **`current`** (`settingsOf(context.projectKey)`) and **`defaultTimeZone`** follow the project of the route.
- **`update(projectKey, patch)`:**
  - merges the patch into the project's entry;
  - a patch with a zone that `Intl` does not know is ignored as a whole. The spec said "an invalid time zone is ignored", and with one field this is the same.
- **`reset(projectKey)`** removes the entry.
- `isValidTimeZone` is imported from `app/forms/annotation.schema.ts`, where it already lived, rather than moved.

### 3. Settings page: an **Annotations** card

- `app/pages/[org]/[project]/settings.vue` has a new **Annotations** card between **Project** and **Machine authentication**.
- **Default time zone** is a `TimeZoneSelect` (`max-w-sm`) bound to a writable computed. The getter reads `projectSettings.defaultTimeZone`, and the setter calls `projectSettings.update(context.projectKey, …)`.
- `aria-label="Default time zone"` overrides the select's own "Time zone" through attribute fallthrough. `TimeZoneSelect.vue` did not need a change.
- Help text: "New annotations in this project start in this time zone. Saved in this browser."
- Like Preferences, a change applies at once, with no Save button and no toast.
- The field is disabled without `useCan('edit')`, that is for Readers.

### 4. New annotation dialog

`app/components/annotations/AnnotationCreateDialog.vue`:

- **`timeZone`:** a ref that starts as `projectSettings.defaultTimeZone`. The existing `watch(open)` sets it again each time the dialog opens.
- **Time zone field:** a `TimeZoneSelect` below **Title**, with the help text "Default from the project settings."
- **Body:** gets `TimeZone: restored?.TimeZone || timeZone.value || undefined`. A new key takes the field's zone. **Restore and update** keeps the restored annotation's own zone, and falls back to the field's zone only when the restored annotation has none.
- The doc comment of the component names the default and the copy to new ancestors.

No other code path creates annotations, so nothing else changed.

### 5. Tests

- **Unit**, new `test/unit/stores/project-settings.test.ts`, five tests:
  - `UTC` for every project, and without a project;
  - a change is kept per project and written to `2s-admin:project-settings`;
  - `defaultTimeZone` follows `context.set(...)`;
  - unknown stored zones fall back to `UTC`, and unknown updates are ignored;
  - `reset` affects only its own project.
- **E2E**, `test/e2e/annotations.spec.ts`, "new annotations start in the default time zone of the project", desktop and mobile:
  - The New annotation dialog starts in `UTC`.
  - On Settings, **Default time zone** is set to `Asia/Tokyo`.
  - The dialog then starts in `Asia/Tokyo`, and the created `sbt.cyberdyne` shows `Asia/Tokyo` in its detail.
- **Deviation from the spec (e2e):**
  - The spec named `Europe/Prague`. The test uses `Asia/Tokyo`, because the e2e browser runs in `Europe/Prague`: an annotation without a zone shows "Browser time zone (Europe/Prague)" as its placeholder, and the assertion would pass even if no zone had been saved.
  - The select is a `USelectMenu`, whose trigger has the `button` role, so the test finds it by `button`, not `combobox`.
- **Results:**
  - `npm run typecheck` and `npm run lint` pass.
  - `npm test`: 176 passed, 5 of them new.
  - `npm run test:e2e` with `PLAYWRIGHT_CHANNEL=chrome`: 61 passed and 1 skipped, desktop and mobile. This includes the new flow and the accessibility check of the Settings page.
  - The visual suite was not run, because it needs the Playwright Linux image. Its screenshots do not include Settings or the New annotation dialog.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - A new paragraph in "The catalog" says that **New** starts an annotation in the project's default time zone, that the dialog lets you pick another one, and that new ancestors get the same zone.
  - The Settings row of "People, machines and settings" lists the default time zone (Contributor).
  - A paragraph below the table says the default is `UTC` and is kept in the browser.
- `src/Platform/ui.admin/README.md`: the client state list names `project-settings`.
