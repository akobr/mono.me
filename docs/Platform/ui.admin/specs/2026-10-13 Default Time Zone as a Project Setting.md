# Default Time Zone as a Project Setting

Linear: [424-12](https://linear.app/424fun/issue/424-12/add-default-time-zone-as-a-project-setting)

## Problem

An annotation has an optional IANA `TimeZone`. Its **Valid from** and **Expires** are entered and shown in that zone. The Administration UI (`src/Platform/ui.admin`) creates every annotation without one, so a new annotation's dates fall back to each viewer's browser zone. Two people in different countries then see different wall-clock times for the same annotation, until someone sets the zone by hand on every annotation.

A project should have a **default time zone** as a setting:

- `UTC` for every project until someone changes it.
- Every annotation created in the project starts in that zone.

## Current State

- **Project settings page:** `app/pages/[org]/[project]/settings.vue` (spec [Administration UI for 2S Platform](2026-10-09%20Administration%20UI%20for%202S%20Platform.md), section 13) has three cards:
  - **Project:** access point key, your role, number of members.
  - **Machine authentication:** stored in the API (`PUT /v1/access/points/{key}/machine-authentication`), Administrators only.
  - **Certificate authority:** the CA download.
- **No project settings storage in the API.** `AccessPoint` only has `Key`, `AccessMap` and `MachineAuthentication`. The OpenAPI document (`open.api.v0.9.1.json`) has no other per-project document. The issue scopes the change to `ui.admin`.
- **Client state:** `app/stores/preferences.ts` keeps personal UI state in `localStorage` (`2s-admin:preferences`) through VueUse `useStorage` with `mergeDefaults`. It is per browser and is not tied to a project. Issue 424-13 plans project defaults that personal Preferences can override, so project settings should not live in `preferences`.
- **Creating annotations:** `app/components/annotations/AnnotationCreateDialog.vue` is the only place that creates a new key. The **New** button and the command palette open it (`useAnnotationCreate`).
  - It calls `setAnnotation` with `withRelations({ ...restored, ProjectName, ViewName, AnnotationKey, Name, AnnotationType, Title })`. `TimeZone` is never set.
  - When the key or an ancestor is deleted, the user picks **Restore and update** (the restored annotation keeps its content) or **Create new**.
- **Ancestors:** the API creates missing ancestors with `AnnotationFactory.CreateDefault(key, …, copyFrom)`, which copies `ValidFrom`, `ExpiresAt`, `TimeZone`, `IsDisabled` and `Labels` from the new descendant. The mock API (`app/mock/annotation-handlers.ts`, `defaultAncestor`) copies `TimeZone` and `Labels` the same way. A time zone set on the new annotation therefore reaches its new ancestors too.
- **Time zone input:** `app/components/annotations/TimeZoneSelect.vue` is a searchable `USelectMenu` of `Intl.supportedValuesOf('timeZone')`, with `UTC` first and the current offset of each zone. Its placeholder is "Browser time zone (…)", shown when the value is empty. `isValidTimeZone` (`app/forms/annotation.schema.ts`) checks a name with `Intl.DateTimeFormat`.
- **Annotation form:** `AnnotationForm.vue` edits `TimeZone` with the same select ("Dates are entered and shown in this zone."). It is not affected.

## Proposed Changes

### 1. Where the setting lives: the browser, per project

There is no project document in the API to store the setting in, and the issue limits the change to `ui.admin`. The default time zone is therefore stored in the browser's `localStorage`, keyed by the project's access point key (`org.project`).

Consequences:

- The setting applies in the browser where it was set. Another member, or another browser, starts from `UTC` until they change it.
- The Settings page says so next to the field, so nobody expects it to be shared.
- The store keeps the storage behind one small API, so it can move to the API later without touching its users. That needs a project settings document on the access point, like `MachineAuthentication`. It is a follow-up and not part of this change.

### 2. Store: `app/stores/project-settings.ts`

A new Pinia setup store, `useProjectSettingsStore` (auto-imported from `app/stores`, like `preferences`).

```ts
export interface ProjectSettings {
  /** IANA time zone for new annotations. */
  defaultTimeZone: string
}

export const defaultProjectSettings: ProjectSettings = { defaultTimeZone: 'UTC' }
```

- **Storage:** `useStorage<Record<string, Partial<ProjectSettings>>>('2s-admin:project-settings', {}, localStorage)`. Each entry holds only the values someone changed.
- **`settingsOf(projectKey?: string): ProjectSettings`:** the defaults merged with the stored entry of the project.
  - Without a project, or with nothing stored, it returns the defaults, so every project starts in `UTC`.
  - A stored time zone that `Intl` does not know (a hand-edited value, or a zone the browser does not support) falls back to `UTC`.
- **`current`:** a computed `settingsOf(context.projectKey)`, which follows the project of the route.
- **`defaultTimeZone`:** a computed `current.defaultTimeZone`.
- **`update(projectKey: string, patch: Partial<ProjectSettings>)`:** merges the patch into the project's entry. An invalid time zone is ignored, so only valid zones are stored.
- **`reset(projectKey: string)`:** removes the project's entry, so it is back on the defaults.

Like `preferences`, nothing in it is secret, and it is kept on sign-out.

### 3. Settings page: an **Annotations** card

In `settings.vue`, a new card **Annotations** between **Project** and **Machine authentication**:

- **Default time zone:** a `TimeZoneSelect` bound to the store (get: `projectSettings.defaultTimeZone`, set: `projectSettings.update(context.projectKey, { defaultTimeZone })`). It gets `aria-label="Default time zone"`, which overrides the select's own "Time zone".
- Help text: "New annotations in this project start in this time zone. Saved in this browser."
- Like Preferences, a change applies immediately, with no Save button.
- Contributors and above (`useCan('edit')`), the people who can create annotations, can change it. Readers see it disabled.

### 4. New annotation dialog

In `AnnotationCreateDialog.vue`:

- A `timeZone` ref is set to `projectSettings.defaultTimeZone` each time the dialog opens, in the existing `watch(open)`. A changed setting is therefore picked up the next time the dialog opens.
- A **Time zone** field (`TimeZoneSelect`) below **Title**, with the help text "Default from the project settings." The user can pick another zone for this annotation.
- The body sent to `setAnnotation` gets `TimeZone`:
  - **Create new:** the zone of the field.
  - **Restore and update:** the restored annotation keeps its own `TimeZone`. The field's zone is used only when the restored annotation has none, in the same way as the title.

  ```ts
  TimeZone: restored?.TimeZone || timeZone.value || undefined,
  ```

- The API copies the zone to the missing ancestors it creates with the key (see Current State), so they start in the same zone.

Nothing else creates annotations. The annotation form, bulk edits, restore and copy keep the zone the annotation already has.

### 5. Tests

- **Unit**, new `test/unit/stores/project-settings.test.ts`:
  - every project starts in `UTC`;
  - a change is kept per project and written to `2s-admin:project-settings`;
  - `current` follows the context project;
  - an invalid stored or updated zone falls back to, or keeps, `UTC`;
  - `reset` brings a project back to the defaults.
- **E2E**, in `test/e2e/annotations.spec.ts`, desktop and mobile against the mock API:
  - The New annotation dialog starts in `UTC`.
  - After **Default time zone** is set to `Europe/Prague` on Settings, the dialog starts in `Europe/Prague`. A subject created from it shows `Europe/Prague` in its detail.
- The existing accessibility check of the Settings page (`accessibility.spec.ts`) covers the new card.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - the Settings row of "People, machines and settings" lists the default time zone;
  - the annotations section says that new annotations start in the project's default time zone.
- `src/Platform/ui.admin/README.md`: the client state list names the `project-settings` store next to `preferences`.
