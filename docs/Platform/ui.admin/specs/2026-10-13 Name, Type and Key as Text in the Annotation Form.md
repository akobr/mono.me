# Name, Type and Key as Text in the Annotation Form

Linear: [424-24](https://linear.app/424fun/issue/424-24/render-name-type-key-more-simply-than-text-boxes-in-annotation-details)

## Problem

The **Annotation** tab of the annotation detail (`src/Platform/ui.admin`) starts its form with three disabled text boxes: **Name**, **Type** and **Key**. The fields are part of the key and can never be edited, but they look like inputs, take a whole row of the form (three columns from `sm` up, three rows on a phone), and the Key box cuts long keys off (`exe.northwind.invoi…`).

The issue asks to render them more compactly, as text only:

- the type as an icon in the type's color;
- the key and the name combined, with the name in bold.

## Current State

- **Form:** `app/components/annotations/AnnotationForm.vue`, lines 156–166, is the first block of the form (Form mode only; JSON and YAML edit the whole document):

  ```vue
  <div class="grid gap-4 sm:grid-cols-3">
    <UFormField label="Name"><UInput :model-value="annotation.Name" disabled class="w-full font-mono" /></UFormField>
    <UFormField label="Type"><UInput :model-value="annotation.AnnotationType" disabled class="w-full" /></UFormField>
    <UFormField label="Key"><UInput :model-value="annotation.AnnotationKey" disabled class="w-full font-mono" /></UFormField>
  </div>
  ```

- **Name is the last key segment.** The API sets `Annotation.Name` from `AnnotationKey.Name`, which is `_segments[^1]` (`Abstractions.Annotations/src/AnnotationKey.cs`, line 56; `Backend.Core/src/Annotating/AnnotationFactory.cs`). The SDK's `parseAnnotationKey` does the same. So the key always contains the name as its last segment.
- **Existing building blocks:**
  - `app/components/annotations/AnnotationTypeBadge.vue` has an `icon` variant: the type icon in the type color (`text-st-{code}`), with `role="img"`, `aria-label` and `title` set to the type name, so color is not the only signal. It accepts a type name (`Execution`) or a code (`exe`) through `metaOf`. Names, icons and colors follow the overrides of project settings and Preferences once branch `feat/424-13` is merged, with no change here.
  - `app/components/common/KeyText.vue` renders a key in monospace with the type code and separators dimmed and the last segment (the name) in `font-semibold text-highlighted`. Its size is fixed to `text-xs`. It is used in the detail header, the create dialog, the configuration page, Deleted and the deleted-keys dialog.
- **Header:** `app/components/annotations/AnnotationDetail.vue` already shows the type badge with its label, the name as a heading and the key with a copy button. The form row repeats them at the top of the tab, so the row only has to identify the annotation, not to be the main place for these values.
- **Tests:**
  - `test/e2e/annotations.spec.ts` edits `sbt.northwind` through the form but does not look at the three disabled boxes.
  - The visual baselines `annotation-detail-{desktop,mobile}-{light,dark}.png` in `test/e2e/__screenshots__/` show the three boxes and must be updated.
  - No unit test mounts `AnnotationForm`.
- **Docs:** `docs/42for.net/platform/admin-ui.md`, line 50, describes the Annotation tab ("the fields as a form, or as JSON or YAML") without the key fields.

## Proposed Changes

### 1. One line of text instead of the three boxes (`AnnotationForm.vue`)

Replace the grid with one line:

```vue
<p class="flex min-w-0 items-center gap-2" data-testid="annotation-identity">
  <AnnotationTypeBadge :type="annotation.AnnotationType" variant="icon" />
  <KeyText :value="annotation.AnnotationKey" size="sm" :truncate="false" />
</p>
```

- **Type:** the icon in the type color. Its tooltip and accessible name are the type name (`Execution`, or the project's own name for the type).
- **Key and name combined:** the whole key in monospace, the name (the last segment) in bold, the code and the other segments dimmed. A long key wraps (`break-all`) instead of being cut off, so the whole key is always visible.
- No labels, no borders, no copy button (the header has one, and the text can be selected). It stays the first block of the form, so the tab still starts with what the annotation is.
- JSON and YAML modes do not change; they keep the read-only check of `Name`, `AnnotationType` and `AnnotationKey`.

### 2. A `size` for `KeyText` (`app/components/common/KeyText.vue`)

Add an optional `size?: 'xs' | 'sm'` prop, `'xs'` by default, mapped to `text-xs` / `text-sm`. The form uses `sm` so the line reads like the field values below it; every existing use keeps `xs` and does not change.

### 3. Tests

- **Unit** (`test/unit/annotations/annotation-form.test.ts`): mount `AnnotationForm` with `mountScreen` and an `Execution` annotation. Expect:
  - no input with the name, type or key as its value;
  - the identity line holds the type icon named `Execution` and the key `exe.northwind.invoicing.prod`, with `prod` as the bold segment.
- **End to end** (`test/e2e/annotations.spec.ts`): in the existing edit test, after opening `sbt.northwind`, check that the identity line shows the key and the `Subject` icon, and that there is no **Name** text box.
- **Visual:** regenerate `annotation-detail-*` with `npm run test:visual:docker -- --update`. Only the annotation detail screens may change.

### 4. Documentation

`docs/42for.net/platform/admin-ui.md`, the **Annotation** tab bullet: say that the form starts with the type icon and the key, with the name in bold, because they are part of the key and cannot be edited.
