# Date Format as a Project Setting and a Preference

Linear: [424-23](https://linear.app/424fun/issue/424-23/use-the-same-date-format-yyyy-mm-dd-everywhere)

## Problem

The Administration UI (`src/Platform/ui.admin`) shows dates in several formats, sometimes on the same screen:

- Text dates follow the browser locale. In `en-US` they read "Oct 13, 2026" and "Oct 13, 2026, 03:04 PM UTC". In `en-GB` they read "13 Oct 2026". In `cs` they read "13. 10. 2026".
- The date inputs of the annotation form always show month/day/year segments ("10/13/2026"), whatever the browser locale is.
- The date fields of configuration forms are native `<input type="date">` and `datetime-local` fields. Their format depends on the browser and the operating system.

The issue asks for one format:

- `yyyy-mm-dd` on every page by default;
- configurable per project;
- each person can pick their own format as a preference.

## Current State

- **Formatting:** `app/utils/dates.ts` has three functions:
  - `formatRelative(date, now)`: "3 hours ago", "in 2 days" (`Intl.RelativeTimeFormat`, browser locale);
  - `formatDateTime(date, timeZone?)`: browser locale with `year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', timeZoneName: 'short'`. An unknown zone falls back to the browser's zone;
  - `formatDate(date, timeZone?)`: browser locale with `dateStyle: 'medium'`.
- **Where dates are shown:**
  - `RelativeTime.vue`: relative text with the absolute `formatDateTime` in a tooltip. It is used for created, updated, deleted, expiry and renewal times in about fifteen places (invitations, machines, views, templates, schemas, configurations, versions, the annotation detail and history, Deleted).
  - `annotation-columns.ts` (`dateCell`): the **Valid from** and **Expires** columns of the annotation grid, `formatDateTime` in the annotation's zone.
  - `formatDate`:
    - `DeletedAnnotationBanner.vue`: "Deleted on …", "Purged automatically on …";
    - `useDeletionToast.ts`: "Restorable from Deleted until …";
    - `certificates.vue`: the **Not before** and **Not after** columns;
    - `deleted.vue`: the tooltip of the purge countdown.
  - `ZonedDateTimeInput.vue`: the "Your time: …" tooltip (`formatDateTime`).
- **Date inputs:**
  - `ZonedDateTimeInput.vue` (annotation **Valid from** and **Expires**) uses `UInputDate` and `UInputTime` (`hour-cycle` 24). `UInputDate` wraps the Reka UI `DateFieldRoot`. Reka orders the segments and picks the separators with `Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts`. `UApp` sets no locale, so Nuxt UI's default `en` gives month/day/year. `UInputDate` takes a `locale` prop and forwards it to `DateFieldRoot`.
  - `configuration/renderers/DateTimeControl.vue` renders the JSON Schema formats `date`, `time` and `date-time` as native inputs (`date`, `time`, `datetime-local`). A `date-time` is edited in local time and stored as an ISO instant. `ControlField` passes the inherited value as the placeholder.
- **Settings layers (424-12, 424-13):**
  - `app/stores/project-settings.ts` keeps settings per project (`2s-admin:project-settings`, keyed by `org.project`) in `localStorage`, because the API has no project settings document. It has `settingsOf`, `current`, `update` and `reset`.
  - `app/stores/preferences.ts` keeps personal `Preferences` (`2s-admin:preferences`, `mergeDefaults`). The account page edits them in the **Preferences** card, which has **Reset preferences**.
  - `app/stores/type-appearance.ts` resolves personal over project over built-in. While it lives, it applies the result to the module-level `typeMeta`. `App.vue` creates it once.
- **Not dates in a format:** relative times ("3 hours ago"); ISO 8601 instants in the JSON and YAML documents and in API bodies.

## Proposed Changes

### 1. The formats

A fixed list, `DateFormat`:

| Value | Example | Date input segments |
|---|---|---|
| `yyyy-mm-dd` (default) | 2026-10-13 | yyyy-mm-dd |
| `dd.mm.yyyy` | 13.10.2026 | dd.mm.yyyy |
| `dd/mm/yyyy` | 13/10/2026 | dd/mm/yyyy |
| `mm/dd/yyyy` | 10/13/2026 | mm/dd/yyyy |
| `d mmm yyyy` | 13 Oct 2026 | dd/mm/yyyy |

- Free patterns are not offered. The list keeps every format unambiguous, and each one maps to a known order of input segments.
- A date with a time adds the time, always on the 24-hour clock (`HH:mm`, the same as `UInputTime` with `hour-cycle` 24), and the short zone name: `2026-10-13 15:04 UTC`.
- Digits are always Latin and month names are English, because the UI is in English.

### 2. Layers

The format in effect is resolved like the type appearance (424-13), highest first:

1. the personal preference, if one is set ("Project default" means none is set);
2. the project's setting;
3. the built-in `yyyy-mm-dd`.

### 3. Formatting: `app/utils/dates.ts`

```ts
export type DateFormat = 'yyyy-mm-dd' | 'dd.mm.yyyy' | 'dd/mm/yyyy' | 'mm/dd/yyyy' | 'd mmm yyyy'

export const defaultDateFormat: DateFormat = 'yyyy-mm-dd'

/** The formats offered in Settings and Preferences, in this order. */
export const dateFormats: readonly { value: DateFormat; label: string; inputLocale: string }[]

export function isDateFormat(value: unknown): value is DateFormat

/** The format in effect; `applyDateFormat` sets it (the date-format store does, while it lives). */
export const dateFormat: Readonly<Ref<DateFormat>>
export function applyDateFormat(format: DateFormat): void

export function formatDate(date: Date, timeZone?: string, format?: DateFormat): string
export function formatDateTime(date: Date, timeZone?: string, format?: DateFormat): string
export function formatDateParts(year: number, month: number, day: number, format?: DateFormat): string

/** The locale that gives UInputDate the segment order of the format. */
export function dateInputLocale(format?: DateFormat): string
```

- `format` defaults to `dateFormat.value`. Every template, computed or render function that calls a formatter reads the ref, so it follows a change of the format without being rewritten. This is the same idea as the reactive `typeMeta`.
- The parts come from one `Intl.DateTimeFormat('en-US', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short', numberingSystem: 'latn', timeZone })` and `formatToParts`. The formatter puts them together in the order of the format.
- As today, an unknown zone falls back to the browser's zone.
- `formatDateParts` formats a calendar date that has no time and no zone, for example a JSON Schema `date` value.
- `inputLocale` per format:
  - `en-CA` for `yyyy-mm-dd`;
  - `en-CH` for `dd.mm.yyyy`;
  - `en-GB` for `dd/mm/yyyy` and `d mmm yyyy`;
  - `en-US` for `mm/dd/yyyy`.

  All of them are English locales, so the segment placeholders stay `yyyy`, `mm` and `dd`.
- `formatRelative` is not changed.

### 4. Project settings

- `ProjectSettings` gets **`dateFormat: DateFormat`**, `yyyy-mm-dd` by default.
- `settingsOf` falls back to the default when the stored value is not a known format.
- `update(projectKey, { dateFormat })` ignores an unknown format, like an unknown time zone.
- The store gets a computed **`dateFormat`**, the format of the route's project.

### 5. Personal preferences

- `Preferences` gets **`dateFormat?: DateFormat`**. It is not set by default, so the project's format applies.
- The store gets:
  - **`dateFormat`**, a computed that is `undefined` unless the stored value is a known format;
  - **`setDateFormat(value: DateFormat | undefined)`**. `undefined` removes the field.
- **Reset preferences** already restores the defaults, so it also clears the personal format.

### 6. Store: `app/stores/date-format.ts`

A Pinia setup store, `useDateFormatStore` (auto-imported):

- **`project`:** `projectSettings.dateFormat`;
- **`personal`:** `preferences.dateFormat`;
- **`effective`:** `personal ?? project`;
- **Side effect:** a `watchEffect` calls `applyDateFormat(effective)`.

`App.vue` calls `useDateFormatStore()` once, next to `useTypeAppearanceStore()`, so the format applies on every page. On pages without a project, such as the account page and invitations, the project layer is the default `yyyy-mm-dd`.

### 7. Settings page

`settings.vue` gets a new card, **Dates**, between **Annotations** and **Annotation types**:

- **Date format:** a `USelect` of `dateFormats`. Each item shows today's date in that format and the pattern, for example "2026-10-13 (yyyy-mm-dd)". It is bound to `projectSettings.dateFormat` and `projectSettings.update(context.projectKey, { dateFormat })`.
- Help text: "How dates are shown on every page of this project, for everyone who opens it in this browser. Each person can pick their own format in Preferences."
- When the person has a personal format, a note says so and links to Preferences, like the note of the **Annotation types** card.
- Like the other settings kept in the browser, Contributors and above (`useCan('edit')`) can change it, and Readers see it disabled. A change applies at once.

### 8. Account page

The **Preferences** card gets a **Date format** `USelect`:

- The items are "Project default" and then the formats, as on Settings. Select items cannot have an empty value, so "Project default" uses the value `project`, which maps to `undefined`.
- Help text: "How dates are shown. Project default follows the Settings of each project."

### 9. Date inputs

- **`ZonedDateTimeInput.vue`:** `UInputDate` gets `:locale="dateInputLocale()"`, so its segments follow the format in effect. The time input is not changed.
- **`DateTimeControl.vue`** (configuration forms):
  - `date`: a `UInputDate` with the same locale. The model goes through `parseDate` and `CalendarDate.toString()`, so the stored value stays `YYYY-MM-DD`.
  - `date-time`: a `ZonedDateTimeInput` without a zone. It edits in the browser's zone, as `datetime-local` did, and stores an ISO instant. Clearing the field removes the value.
  - A value that cannot be parsed keeps the plain text input with the raw value, so nothing is hidden or lost.
  - The inherited value, which was the native input's placeholder, is shown formatted next to the field ("Inherited: 2026-10-13").
  - `time` keeps the native time input. It is not a date.

### 10. What is not changed

- Relative times ("3 hours ago") and their wording.
- ISO 8601 values in the JSON and YAML documents, in the API and in the stored configuration values.
- The time zone select and the default time zone.
- Storyteller, the OpenAPI document and the SDK.

### 11. Tests

- **Unit**, new `test/unit/utils/dates.test.ts`:
  - each format, as a date and as a date with a time, in a given zone;
  - midnight is `00:00`;
  - an unknown zone falls back to the browser's zone;
  - `formatDateParts`;
  - `isDateFormat`;
  - `dateInputLocale` gives the segment order of each format (`Intl` `formatToParts`);
  - the formatters follow `applyDateFormat`.
- **Unit**, `test/unit/stores/project-settings.test.ts`: the expected settings include `dateFormat: 'yyyy-mm-dd'`, a format is kept per project, and unknown formats are ignored.
- **Unit**, new `test/unit/stores/date-format.test.ts`:
  - `yyyy-mm-dd` without settings;
  - the project's format follows the context;
  - the personal format wins;
  - **Reset preferences** brings back the project's format.
- **Unit**, `test/unit/configuration/schema-form.test.ts`: a `date` property renders the date field with segments in the `yyyy-mm-dd` order, not a native date input.
- **E2E**, `test/e2e/settings.spec.ts`, desktop and mobile against the mock API:
  - the **Not after** date of the mock certificate is `yyyy-mm-dd`;
  - after **Date format** is set to `dd.mm.yyyy` on Settings, it is `dd.mm.yyyy`;
  - a personal `mm/dd/yyyy` on the account page wins;
  - "Project default" brings back `dd.mm.yyyy`.
- The accessibility checks of Settings and Account (`accessibility.spec.ts`) cover the new fields.

### 12. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - the Settings row lists the date format;
  - a paragraph explains the format, its layers and where it applies;
  - the account page paragraph lists the date format among the preferences.
- `src/Platform/ui.admin/README.md`: the client state list names the `date-format` store, and says that `project-settings` and `preferences` hold the date format.
