# Date Format as a Project Setting and a Preference

## Overview

This review covers the whole of the spec [Date Format as a Project Setting and a Preference](../2026-10-13%20Date%20Format%20as%20a%20Project%20Setting%20and%20a%20Preference.md), delivered in one phase (Linear [424-23](https://linear.app/424fun/issue/424-23/use-the-same-date-format-yyyy-mm-dd-everywhere)).

- Every date in the Administration UI now has one format. It is `yyyy-mm-dd` by default.
- A project can pick another format on its Settings page, and each person can pick their own in Preferences, which wins in every project.
- The date fields of annotations and configurations show their segments in the same order.
- The work builds on the `project-settings` and `preferences` layers of 424-12 and 424-13. The branch therefore starts from `feat/424-13`, and the PR targets that branch.

## What Was Done

### 1. The formats

As planned, there are five formats: `yyyy-mm-dd` (default), `dd.mm.yyyy`, `dd/mm/yyyy`, `mm/dd/yyyy` and `d mmm yyyy`.

- A date with a time adds the 24-hour time and the short zone name, for example `2026-10-13 15:04 UTC`.
- Digits are Latin and month names are English in every browser locale.

### 2. Layers

The format in effect is the personal one, if set, then the project's, then `yyyy-mm-dd`. On pages without a project (the account page, invitations, organizations), the project layer is the default.

### 3. Formatting: `app/utils/dates.ts`

- New: `DateFormat`, `defaultDateFormat`, `dateFormats`, `isDateFormat`, `dateFormat` (a read-only view of a module-level `shallowRef`), `applyDateFormat`, `dateInputLocale` and `formatDateParts`.
- `formatDate` and `formatDateTime` take an optional `format` as a third parameter, which defaults to the format in effect. Their callers did not change:
  - `RelativeTime`;
  - the grid's `dateCell`;
  - `DeletedAnnotationBanner`, `useDeletionToast`, `certificates.vue` and `deleted.vue`;
  - the "Your time" tooltip of `ZonedDateTimeInput`.

  They all follow a change at once, because the formatters read the ref while rendering.
- The fields come from one `Intl.DateTimeFormat('en-US', { …, hourCycle: 'h23', timeZoneName: 'short', timeZone })` and `formatToParts`. As before, an unknown zone falls back to the browser's zone.
- **Deviations from the spec:**
  - The parts are read with `month` and `day` set to `numeric` and padded in `formatDateParts`. `numberingSystem` is not set, because `en-US` is always Latin.
  - `dateFormats` entries have no `label`, because the value is the pattern itself. A new helper, `dateFormatItems(today)`, builds the select items ("2026-10-13 (yyyy-mm-dd)") for both pages.
  - `formatDateTime` trims the end, so a zone name that `Intl` does not return leaves no trailing space.
- `formatRelative` is unchanged.

### 4. Project settings

`app/stores/project-settings.ts`:

- `ProjectSettings.dateFormat` is `yyyy-mm-dd` by default.
- `settingsOf` falls back to the default for a stored value that `isDateFormat` rejects.
- `update` ignores a patch with an unknown format, like one with an unknown zone.
- New computed: `dateFormat`.

### 5. Personal preferences

`app/stores/preferences.ts`:

- `Preferences.dateFormat?` is not set by default.
- New computed `dateFormat`: `undefined` unless the stored value is a known format.
- New `setDateFormat(value)`. `undefined` deletes the field. Beyond the spec, an unknown format is ignored rather than stored.
- **Reset preferences** clears it, because it restores `defaultPreferences`.

### 6. Store: `app/stores/date-format.ts`

`useDateFormatStore` has `project`, `personal` and `effective` (`personal ?? project`). A `watchEffect` calls `applyDateFormat(effective)`.

- `App.vue` creates it next to `useTypeAppearanceStore()`.
- `app/auto-imports.d.ts` was regenerated with `useDateFormatStore`.

### 7. Settings page

`settings.vue` has a new **Dates** card between **Annotations** and **Annotation types**:

- **Date format** is a `USelect` of `dateFormatItems()`, labelled by its `UFormField`, with the help text of the spec.
- It is disabled without `useCan('edit')`.
- When the person has a personal format, a `UAlert` says "Your own Preferences show dates as …" and links to Preferences.

### 8. Account page

The **Preferences** card has a **Date format** `USelect`:

- "Project default" uses the value `project`, and `setDateFormat` gets `undefined` for it.
- The help text is the one of the spec.

### 9. Date inputs

- **`ZonedDateTimeInput.vue`:** `UInputDate` gets `:locale="dateInputLocale()"` through a computed. Reka re-creates the segments when the locale changes, so a new format applies without a reload.
- **`DateTimeControl.vue`:**
  - A JSON Schema `date` is a `UInputDate` (`parseDate`, then `toString()`). A `date-time` is a `ZonedDateTimeInput` without a zone, so it edits in the browser's zone and stores `toAbsoluteString()`.
  - A value that `parseDate` or `parseAbsolute` cannot read keeps the plain text input with the raw value. So does every other format.
  - The inherited value is shown as "Inherited: …" next to the date fields, in the date format.
  - `time` keeps the native time input.
  - **Beyond the spec:** the date field got a **Clear** button, the same as `ZonedDateTimeInput`. A `UInputDate` cannot be emptied otherwise. Its `aria-label` is "{label} date", as in `ZonedDateTimeInput`.

### 10. What is not changed

As planned:

- relative times;
- ISO values in the documents, the API and the stored configuration values;
- the time zone select;
- Storyteller, the OpenAPI document and the SDK.

### 11. Tests

- **Unit**, new `test/unit/utils/dates.test.ts`, 13 tests:
  - the default;
  - each format, as a date, a date-time and `formatDateParts`;
  - zones (`Asia/Tokyo` across midnight, `America/New_York`);
  - the unknown-zone fallback and year padding;
  - `isDateFormat`;
  - the segment order that `Intl` gives for every `inputLocale`;
  - a `computed` that follows `applyDateFormat`;
  - `dateFormatItems`.
- **Unit**, `test/unit/stores/project-settings.test.ts`: the defaults include `dateFormat`, and 2 new tests cover the per-project format, ignored updates and the fallback for unknown stored formats.
- **Unit**, new `test/unit/stores/date-format.test.ts`, 4 tests:
  - the default;
  - the project of the context;
  - the personal format wins, until it is removed or Preferences are reset;
  - unknown personal formats are ignored.
- **Unit**, `test/unit/configuration/schema-form.test.ts`:
  - the "every kind of property" test looks for the date group instead of a native date input;
  - 4 new tests:
    - segments in `yyyy-mm-dd`, then `dd.mm.yyyy` order, and **Clear**;
    - a `date-time` in `mm/dd/yyyy` order with its time field;
    - the inherited date;
    - a value the field cannot read stays in a text input.
- **E2E**, `test/e2e/settings.spec.ts`, "dates follow the date format of the project settings and Preferences", desktop and mobile:
  - The **Expires** date of the mock certificate (`/acme/billing/certificates`) starts as `yyyy-mm-dd`.
  - It becomes `dd.mm.yyyy` after the project's **Date format** changes.
  - It becomes `mm/dd/yyyy` with a personal format.
  - It goes back to `dd.mm.yyyy` with "Project default".
- **Results:**
  - `vue-tsc --build` and `npm run lint` pass.
  - `npm test`: 232 passed, 23 of them new.
  - `npm run test:e2e` with `PLAYWRIGHT_CHANNEL=chrome`: 65 passed and 1 skipped (the phone-only filter sheet on desktop). This includes the accessibility checks of Settings and Account with the new fields.
  - `npm run test:visual:docker`: the 40 screenshots match the committed baselines, so no baseline was updated.

### 12. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - the Settings row lists the date format (Contributor);
  - a new paragraph and list explain the formats, the personal override, **Project default**, where the format applies and what stays ISO;
  - the account page paragraph lists the date format.
- `src/Platform/ui.admin/README.md`: the client state list names the `date-format` store, and the date format in `preferences` and `project-settings`.
