# Annotation Type Names, Colors and Icons as Project Settings

Linear: [424-13](https://linear.app/424fun/issue/424-13/extend-settings-page-for-project)

## Problem

The Administration UI (`src/Platform/ui.admin`) shows the seven annotation types the same way in every project. Each type has a built-in name ("Execution", "Executions"), a color and an icon. A team that calls its executions "jobs", or that wants its subjects to stand out in red, cannot change that.

The project's Settings page should hold **defaults for the names, colors and icons of the annotation types**:

- They override how a type is shown on every page of the Administration UI.
- Each person can override them again in their own **Preferences** on the account page.

## Current State

- **Type metadata:** `app/utils/annotation-key.ts` exports `typeMeta: Record<AnnotationTypeCode, AnnotationTypeMeta>`, a plain constant. Each entry has:
  - `type` and `code`: the API name (`Execution`) and the code (`exe`);
  - `label` and `plural`: the shown names;
  - `icon`: a lucide icon name (`i-lucide-play`);
  - `textClass` and `bgClass`: `text-st-exe` and `bg-st-exe/10`;
  - `mergeOrder` and `parts`: the key model.

  `metaOf(typeOrCode)` returns an entry from a type name or a code.
- **Where the names and icons are shown:** about twenty places read `typeMeta[code]` or `metaOf(...)`, mostly in templates or computeds. Examples:
  - `AnnotationTypeBadge.vue`: chips in the grid, the lists, the detail and the breadcrumb;
  - `GraphLegend.vue` and the node labels of `AnnotationGraph.vue`;
  - the filter sheet, the Templates and Schemas pages, `configuration-origin.ts`, `schema-targets.ts`, `CommandPalette.vue` and `AnnotationLineage.vue`.

  Three places copy names once, in `setup`, and never update:
  - `AnnotationCreateDialog.vue`: `typeItems` and the `partLabels` of the key parts;
  - `SchemaCreateDialog.vue`: `types`.
- **Colors:** `app/assets/css/main.css` defines the tokens `--st-{code}`, with light values (Tailwind 700, or 800 for amber and orange) and dark values (Tailwind 400). They meet WCAG AA against the panel. `@theme inline` maps them to `--color-st-{code}`, so `text-st-exe` and `bg-st-exe/10` follow the variables.
  - `GraphLegend.vue` uses `var(--st-{code})` directly.
  - `AnnotationGraph.vue` reads the variables with `getComputedStyle` for the WebGL renderer. It reads them again only when the `class` of `<html>` changes, that is on a theme switch.
- **Icons:** `vite.config.ts` bundles only the lucide icons whose names appear in `app/**/*.{vue,ts}` (`icon.clientBundle.scan`). Nothing is fetched from the Iconify API, because the CSP blocks it. An icon name typed by a user would not render.
- **Project settings (424-12, branch `feat/424-12`):** `app/stores/project-settings.ts` keeps settings per project in `localStorage` (`2s-admin:project-settings`), keyed by the access point key (`org.project`), because the API has no project settings document. Its API:
  - `settingsOf(projectKey)`;
  - `current` and `defaultTimeZone`, which follow the project of the route;
  - `update(projectKey, patch)` and `reset(projectKey)`.

  `settings.vue` has an **Annotations** card with **Default time zone**, which Contributors and above can change (`useCan('edit')`).
- **Personal preferences:** `app/stores/preferences.ts` keeps `Preferences` (`2s-admin:preferences`, `useStorage` with `mergeDefaults`). It is per browser and not tied to a project. The account page (`app/pages/account.vue`) edits it in a **Preferences** card with **Reset preferences**.
- **Context:** the context guard clears the context store on routes without `:org`, so on `/account` there is no current project.
- **Filter:** the `type:` qualifier (`app/utils/filter-parser.ts`, `typeAliases`) takes the codes and the built-in type names. It does not use `typeMeta`.

## Proposed Changes

This work builds on 424-12 and reuses its `project-settings` store, so the branch starts from `feat/424-12`.

### 1. Where the settings live

As with the default time zone, the API has nowhere to keep project settings, and the issue limits the change to `ui.admin`. Both layers are kept in the browser:

- **Project defaults:** in the `project-settings` store, per project. Contributors and above edit them on the project's Settings page. They are kept in the browser where they were set, like the default time zone. The page says so.
- **Personal overrides:** in the `preferences` store, for every project. Anyone edits them on the account page.

The effective appearance of a type is resolved **per field** (name, color, icon), highest layer first:

1. personal Preferences;
2. the project's Settings;
3. the built-in default.

Example: if the project calls `exe` "Job" and colors it red, and a person picks only a different icon, that person sees a red "Job" with their icon.

### 2. Model: `app/utils/type-appearance.ts`

```ts
export type TypeColor =
  | 'red' | 'orange' | 'amber' | 'yellow' | 'lime' | 'green' | 'emerald' | 'teal' | 'cyan'
  | 'sky' | 'blue' | 'indigo' | 'violet' | 'purple' | 'fuchsia' | 'pink' | 'rose' | 'gray'

/** One layer's overrides for one type; a missing field falls through to the next layer. */
export interface TypeAppearance {
  label?: string
  plural?: string
  color?: TypeColor
  icon?: string
}

export type TypeAppearances = Partial<Record<AnnotationTypeCode, TypeAppearance>>

/** The resolved appearance of a type. */
export interface ResolvedTypeAppearance {
  label: string
  plural: string
  color: TypeColor
  icon: string
}
```

- **`typeColors: Record<TypeColor, { label: string; light: string; dark: string }>`:** a palette with a light value (Tailwind 700, or 800 for the yellowish and orange tones and green) and a dark value (Tailwind 400, Zinc 600 and 400 for gray).
  - Every value keeps at least 4.5:1 against the panel and against the 10 % tinted background of a chip, in both themes.
  - The seven built-in colors are in the palette with exactly the values of `main.css`: violet, fuchsia, blue, amber, cyan, emerald and orange.
  - Users pick from the palette and cannot enter any color. That keeps the WCAG AA promise of the type colors.
- **`builtInTypeColors: Record<AnnotationTypeCode, TypeColor>`:** the palette names of the built-in tokens.
- **`typeIcons: { name: string; label: string }[]`:** a curated list of about fifty lucide icons, including the seven built-in ones. The names are literals in this file, so the icon scan of the Vite build bundles them. Any other icon could not be shown, so it is not offered.
- **`maxTypeNameLength = 40`**.
- **`sanitizeTypeAppearances(value: unknown): TypeAppearances`:** cleans what is read from storage or from the editor:
  - drops unknown type codes, unknown colors and icons outside `typeIcons`;
  - trims names, cuts them to 40 characters and drops empty ones;
  - drops `plural` when there is no `label` (see the name rule below);
  - drops types with nothing left.
- **Name rule:** `label` and `plural` belong together.
  - The highest layer that sets a `label` decides both names.
  - If that layer has no `plural`, the plural is derived from the label with `pluralize`: `y` after a consonant becomes `ies`; `s`, `x`, `z`, `ch` and `sh` get `es`; anything else gets `s`.
  - So a personal "Run" never shows next to the project's "Jobs".
- **`mergeTypeAppearances(...layers): TypeAppearances`:** the per-field merge of the layers, highest first, still without the built-in values. It follows the name rule.
- **`resolveTypeAppearance(code, ...layers): ResolvedTypeAppearance`** and **`resolveTypeAppearances(...layers): Record<AnnotationTypeCode, ResolvedTypeAppearance>`:** apply the layers, highest first, over the built-in defaults.
- **`typeColorsCss(appearances: TypeAppearances): string`:** CSS for the colors the layers change, and nothing for the rest:

  ```css
  :root:not(.dark) { --st-exe: #b91c1c; }
  :root.dark { --st-exe: #f87171; }
  ```

  The selectors are more specific than `:root` and `.dark` in `main.css`, so they win in whatever order the style sheets load.

### 3. Reactive type metadata

In `app/utils/annotation-key.ts`:

- The literal becomes **`builtInTypeMeta`** (readonly).
- **`typeMeta`** becomes `reactive(structuredClone(builtInTypeMeta))`. Every template and computed that reads `typeMeta[code].label`, `.plural` or `.icon`, directly or through `metaOf`, now follows a change without being rewritten.
- `type`, `code`, `textClass`, `bgClass`, `mergeOrder` and `parts` never change. The classes follow the colors through the `--st-*` variables.

In `type-appearance.ts`, **`applyTypeAppearance(appearances: TypeAppearances)`** writes the resolved `label`, `plural` and `icon` of each type into `typeMeta`. Types without overrides get their built-in values back.

The three `setup`-time copies become computeds:

- `typeItems` in `AnnotationCreateDialog.vue`;
- `partLabels` in `AnnotationCreateDialog.vue`, which takes the subject, responsibility, context and unit labels from `typeMeta`;
- `types` in `SchemaCreateDialog.vue`.

### 4. Store: `app/stores/type-appearance.ts`

A Pinia setup store, `useTypeAppearanceStore` (auto-imported):

- **`project`:** `projectSettings.current.types`, the project layer of the route.
- **`personal`:** `preferences.types`, the personal layer.
- **`effective`:** `mergeTypeAppearances(personal, project)`, the per-field merge that `applyTypeAppearance` and `typeColorsCss` take.
- **`resolved`:** `resolveTypeAppearances(personal, project)`.
- **`css`:** `typeColorsCss(effective)`.
- **Side effects, in the store's effect scope:**
  - a `watchEffect` calls `applyTypeAppearance(effective)`;
  - a second one writes `css` into `<style id="st-type-colors">` in `document.head`, creating the element on first use and leaving it empty when nothing is overridden.

`App.vue` calls `useTypeAppearanceStore()` once, so the overrides apply on every page, including those of the bare layout.

`AnnotationGraph.vue` also watches `typeAppearance.css` (`flush: 'post'`) and reads the colors again after `nextTick`, next to its existing theme observer. Changed colors then reach the WebGL nodes too.

### 5. Project settings

- `ProjectSettings` gets **`types: TypeAppearances`**, `{}` by default.
- `settingsOf` returns `sanitizeTypeAppearances(stored.types)`.
- `update(projectKey, { types })` stores the sanitized value. An empty value removes the field from the entry, so the entry keeps only what was changed.

`settings.vue` gets a new card, **Annotation types**, below **Annotations**:

- Text: "Names, colors and icons of the annotation types on every page, for everyone who opens this project in this browser. Each person can override them in Preferences on the account page."
- A `TypeAppearanceEditor` (section 7) bound to `projectSettings.current.types`. Its inherited values are the built-in ones.
- **Reset to built-in** in the card footer, which empties the project's `types`.
- Like the time zone, Contributors and above (`useCan('edit')`) can edit it, and Readers see it disabled.
- A change applies at once, with no Save button.

### 6. Personal preferences

- `Preferences` gets **`types: TypeAppearances`**, `{}` in `defaultPreferences`. `mergeDefaults` adds it to stored preferences that do not have it yet.
- The store gets **`types`**, a computed over the sanitized `state.types`, and **`setTypes(value)`**.
- **Reset preferences** already restores the defaults, so it also clears the personal types.

`account.vue` gets a new card, **Annotation types**, below **Preferences**:

- Text: "Your own names, colors and icons, in every project. Empty fields use the project's settings, or the built-in ones."
- A `TypeAppearanceEditor` bound to `preferences.types`. Its inherited values are the built-in ones, because the account page has no project. The project's values show on the pages of the project.
- **Reset types** in the card footer, which clears only the personal types.

### 7. Component: `app/components/annotations/TypeAppearanceEditor.vue`

```ts
const model = defineModel<TypeAppearances>({ required: true })
defineProps<{
  /** What applies to an empty field. */
  inherited: Record<AnnotationTypeCode, ResolvedTypeAppearance>
  disabled?: boolean
}>()
```

One row per type, in the order of `typeCodes`:

- **Preview:** the icon and name in the resolved color, with the code (`exe`). It shows what the row's values plus the inherited ones give.
- **Name:** a `UInput` (`v-model.lazy`, `maxlength` 40). Its placeholder is the inherited name. It updates on change rather than on every key, so a name can contain spaces.
- **Plural:** like the name. Its placeholder is the derived plural of the row's name, or the inherited plural.
- **Color:** a `USelectMenu` of "Default (Violet)" plus the palette, each with a swatch.
- **Icon:** a searchable `USelectMenu` of "Default" plus `typeIcons`, each with its icon.
- **Reset** (a ghost button with `aria-label` "Reset {type}"), shown when the row has overrides.

Accessibility and layout:

- The fields have visible labels.
- The selects have `aria-label` "{Type} color" and "{Type} icon".
- On narrow screens the preview and Reset sit on top, and the four fields form a two-column grid below them.

Every change emits `sanitizeTypeAppearances` of the whole model.

### 8. What is not changed

- The **codes** (`exe`) in chips and keys, and the **API type names** (`Execution`) in API bodies, the filter's `type:` qualifier and the JSON/YAML documents. These are part of the data, not of how it is shown.
- The help texts that describe the key model in general.
- Storyteller, the OpenAPI document and the SDK.

### 9. Tests

- **Unit**, new `test/unit/utils/type-appearance.test.ts`:
  - `sanitizeTypeAppearances` drops unknown codes, colors and icons, trims and cuts names, and drops a plural without a label;
  - the per-field precedence is personal, then project, then built-in;
  - names are taken as a pair, with the derived plural;
  - `pluralize`;
  - `typeColorsCss` outputs only the overridden types;
  - every palette color keeps 4.5:1 against the panel and the tinted chip background in both themes;
  - the built-in colors and icons are in the palette and in `typeIcons`.
- **Unit**, new `test/unit/stores/type-appearance.test.ts`:
  - `typeMeta` and `metaOf` follow the project and personal layers and switch with the project of the context;
  - `<style id="st-type-colors">` holds the overridden colors;
  - resetting brings the built-in values back.
- **Unit**, `test/unit/stores/project-settings.test.ts`: the expected settings include `types: {}`, and the stored types are sanitized.
- **E2E**, new `test/e2e/settings.spec.ts`, desktop and mobile against the mock API:
  - on Settings, Execution is renamed to "Job", given the red color and the rocket icon;
  - Templates then lists "Job";
  - `--st-exe` on `<html>` is the red value;
  - on the account page, a personal name "Run" wins, and the Templates page lists "Run";
  - **Reset types** brings "Job" back.
- The accessibility checks of Settings and Account (`accessibility.spec.ts`) cover the new cards.

### 10. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - the Settings row lists the type names, colors and icons;
  - a paragraph explains the layers (Preferences, then project, then built-in) and that codes and API names do not change;
  - the accessibility list says that the type colors come from a palette that keeps AA contrast.
- `src/Platform/ui.admin/README.md`: the client state list names the `type-appearance` store, and says that `project-settings` and `preferences` hold type overrides.
