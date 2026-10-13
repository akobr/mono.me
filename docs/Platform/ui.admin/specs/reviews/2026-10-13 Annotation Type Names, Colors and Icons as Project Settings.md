# Annotation Type Names, Colors and Icons as Project Settings

## Overview

This review covers all of the spec [Annotation Type Names, Colors and Icons as Project Settings](../2026-10-13%20Annotation%20Type%20Names,%20Colors%20and%20Icons%20as%20Project%20Settings.md). It was delivered in one phase, in two code commits (Linear [424-13](https://linear.app/424fun/issue/424-13/extend-settings-page-for-project)).

- **Project defaults:** a new **Annotation types** card on the project's Settings page gives each of the seven types a name and plural, a color and an icon.
- **Every page:** chips, lists, the graph and its legend, filters, dialogs, Templates and Schemas all use them.
- **Personal overrides:** the account page has the same editor. Its values win over the project's, field by field.
- **Storage:** as with the default time zone (424-12), both layers are kept in the browser. The branch therefore starts from `feat/424-12` and reuses its `project-settings` store.

## What Was Done

### 1. Where the settings live

As planned:

- **Project layer:** `ProjectSettings.types` in `2s-admin:project-settings`, per access point key.
- **Personal layer:** `Preferences.types` in `2s-admin:preferences`, for every project.
- **Resolution:** per field, personal first, then project, then built-in.
- **Untouched:** Storyteller, the OpenAPI document and the SDK.

### 2. Model: `app/utils/type-appearance.ts`

- **Types:** `TypeColor`, `TypeAppearance`, `TypeAppearances` and `ResolvedTypeAppearance`, as specified.
- **`typeColors`:** 18 palette entries.
  - The light values are Tailwind 700, and 800 for amber, orange, yellow, lime and green. Green 700 gave only 4.39:1 on a chip's tint, so green uses 800.
  - The dark values are Tailwind 400. Gray is Zinc 600 and 400.
  - The seven built-in colors match `main.css` exactly, and a unit test reads `main.css` to check this.
- **`builtInTypeColors`:** maps each type to its built-in palette color.
- **`typeIcons`:** 65 lucide icons with labels, including the seven built-in ones. The spec said "about fifty". All names were checked against `@iconify-json/lucide`. They are literals, so the icon scan of the Vite build bundles them.
- **`sanitizeTypeAppearances`:**
  - drops unknown codes, colors and icons;
  - trims names and cuts them to `maxTypeNameLength` (40);
  - drops a plural without a label;
  - drops types with nothing left.
- **Name rule:** `pluralize` derives the plural with the regular English endings. `mergeTypeAppearances` takes the name and its plural from the same layer, as specified.
- **`resolveTypeAppearances`:** fills the remaining fields from `builtInTypeMeta` and `builtInTypeColors`.
- **`typeColorsCss`:** writes `:root:not(.dark)` and `:root.dark` blocks for the overridden types only.
- **`applyTypeAppearance`:** writes the resolved `label`, `plural` and `icon` into `typeMeta`. It lives in this file, as planned, not in `annotation-key.ts`.

### 3. Reactive type metadata

- **`app/utils/annotation-key.ts`:**
  - the literal is now `builtInTypeMeta` (readonly);
  - `typeMeta` is `reactive(structuredClone(builtInTypeMeta))`.

  No user of `typeMeta` or `metaOf` had to change.
- **Copies made once in `setup`, now computeds:**
  - `typeItems` and `partLabels` in `AnnotationCreateDialog.vue`. `partLabels` takes the subject, responsibility, context and unit names from `typeMeta`; "Name" stays.
  - `types` in `SchemaCreateDialog.vue`.

### 4. Store: `app/stores/type-appearance.ts`

- **`useTypeAppearanceStore`** exposes `project`, `personal`, `effective`, `resolved` and `css`. It is auto-imported, and `auto-imports.d.ts` was regenerated with `typeColorsStyleId` too.
- **Two `watchEffect`s:**
  - one calls `applyTypeAppearance(effective)`;
  - the other keeps `<style id="st-type-colors">` in `document.head` in sync with `css`, creating it on first use.
- **`App.vue`** creates the store once.
- **`AnnotationGraph.vue`** watches `typeAppearance.css` with `flush: 'post'`. After `nextTick` it reads the `--st-*` tokens again for the WebGL renderer. This is in addition to the existing theme observer.

### 5. Project settings

- `ProjectSettings.types` defaults to `{}`, and `settingsOf` returns the sanitized stored value.
- **`update`:**
  - stores the sanitized types;
  - removes the `types` field when it is empty, so an entry keeps only what was changed;
  - still ignores a patch with an unknown time zone.
- **`settings.vue`:**
  - The **Annotation types** card sits below **Annotations**, with the text from the spec.
  - The editor's inherited values are the built-in ones (`resolveTypeAppearances()`).
  - The editor is disabled without `useCan('edit')`.
  - **Reset to built-in** in the footer is shown only to Contributors and above, and is disabled when nothing is overridden.
- **Addition:** a neutral `UAlert` ("Your own Preferences override some of these for you.", with **Open Preferences**) appears when the person has personal overrides. Without it, someone editing the project's values could wonder why the rest of the UI shows different ones.

### 6. Personal preferences

- `Preferences.types` defaults to `{}`, and `mergeDefaults` adds it to older stored preferences.
- The store has `types`, the sanitized computed, and `setTypes`. **Reset preferences** clears the types as well.
- **`account.vue`:** the **Annotation types** card sits below **Preferences**, with the text from the spec. It uses the built-in values as inherited ones, because the context is empty on `/account`. **Reset types** is in the footer.

### 7. Component: `TypeAppearanceEditor.vue`

Implemented as specified: one row per type, in `typeCodes` order.

- **Preview:** icon and name in the resolved color, plus the code. It has `data-testid="type-preview-{code}"`.
- **Name and Plural:**
  - `UInput` with `lazy` model modifiers, so they update on change, and `maxlength` 40.
  - **Plural** is disabled until the row has a name ("Set a name first"). Its placeholder is the derived plural.
- **Color:** a `USelect` with a swatch in the trigger and in every item.
- **Icon:** a searchable `USelectMenu` with the icons.
- **Reset {type}:** a ghost button, shown when the row has overrides.
- **Labels:**
  - The fields have visible labels.
  - Their accessible names use the built-in type name, which never changes: "Execution name", "Execution plural", "Execution color" and "Execution icon".
  - The four fields form a group named after the type.
- **Layout:** two columns on narrow screens and four from `lg`.
- **Swatches in both themes:** they use the CSS variables `--swatch-light` and `--swatch-dark` with the `dark:` variant.

Deviations from the spec:

- **Default color label:** the default item of the color select is "Default", not "Default (Violet)". The longer label was cut off in the four-column layout and on phones. The list names the inherited color as a muted hint next to "Default".
- **Swatch alignment:** each swatch sits in a 20 px box, so it lines up with the text of the item.

### 8. What is not changed

As planned:

- codes in chips and keys;
- API type names in bodies, documents and the `type:` filter (`typeAliases`);
- help texts about the key model;
- Storyteller, the OpenAPI document and the SDK.

### 9. Tests

- **Unit**, new `test/unit/utils/type-appearance.test.ts`, 28 tests:
  - sanitizing;
  - `pluralize`;
  - per-field precedence and the name pair;
  - resolution to the built-in values;
  - `typeColorsCss`;
  - one contrast test per palette color: at least 4.5:1 on the panel and on the 10 % chip tint, light against `#ffffff` and dark against `#18181b`;
  - the built-in colors are the values of `main.css`;
  - the built-in icons are offered, and no icon is listed twice.
- **Unit**, new `test/unit/stores/type-appearance.test.ts`, 3 tests:
  - built-in values without overrides;
  - the project layer follows the context into `typeMeta`, `metaOf` and the style element;
  - Preferences win, and **Reset preferences** brings the project's name back.

  `typeMeta` is shared at module level. A store left over from an earlier test, which listens to the same `localStorage` keys, overwrote it during the last test. `afterEach` therefore calls `$dispose()` on the store, puts back the built-in values and removes the style element.
- **Unit**, `test/unit/stores/project-settings.test.ts`:
  - the defaults include `types: {}`;
  - two new tests: types are stored sanitized per project and leave the entry when emptied, and unknown stored types are ignored.
- **E2E**, new `test/e2e/settings.spec.ts`, desktop and mobile:
  - On Settings, Execution becomes "Job", red, with the rocket icon. The preview and the derived plural placeholder ("Jobs") update, and `--st-exe` becomes `#b91c1c`.
  - Templates lists "Job".
  - A personal "Run" on the account page wins, and the project's red stays.
  - **Reset types** brings "Job" back, and **Reset to built-in** brings back "Execution" and `#047857`.
- **Results:**
  - `npm run typecheck` and `npm run lint` pass.
  - `npm test`: 209 passed, 33 of them new.
  - `npm run test:e2e` with `PLAYWRIGHT_CHANNEL=chrome`: 63 passed and 1 skipped, desktop and mobile. This includes the new flow and the axe checks of Settings and Account with the new cards.
  - The new screens were checked on screenshots, desktop and mobile, light and dark.
  - The visual suite was not run, because it needs the Playwright Linux image. The built-in names and colors did not change, so the existing baselines should still match.
  - The graph's re-read of the colors has no automated test.

### 10. Documentation

- **`docs/42for.net/platform/admin-ui.md`:**
  - The Settings row lists the type names, colors and icons ("time zone and types: Contributor").
  - A new paragraph explains the layers, the palette and the fixed icon set, and that codes and API names do not change.
  - The themes section names the personal type settings on the account page.
  - The accessibility list says that renamed and recolored types still carry an icon and text.
- **`src/Platform/ui.admin/README.md`:** the client state list names the type overrides in `preferences` and `project-settings`, and the new `type-appearance` store.

### 11. Follow-up: full names in the Type column

Requested after review, and a deviation from section 8 ("codes in chips"):

- **`grid/annotation-columns.ts`:** the Type column of the grid renders `AnnotationTypeBadge` with `variant: 'label'`, so it shows the full name (custom names included) in the type color, still in the monospace font. The column is 160 px wide instead of 96 px; in Tree mode it is 260 px instead of 220 px.
- **`grid/TreeCell.vue`:** the tree row uses the same `label` variant, next to the child count.
- **`AnnotationList.vue`** (phones) keeps `variant="icon"`, with the name as its title and accessible label, to save room.
- The chips of the Deleted dialog keep the code; the detail header already showed the name.
- **Tests:** `npm test` 225 passed, `npm run lint` clean, `npm run test:e2e` 64 passed and 2 skipped. The visual suite (`test:visual:docker`) changed only the six desktop screens with the grid (annotations, annotation detail and history, light and dark); their baselines were regenerated, and the suite then passed with 40 of 40.
- **`docs/42for.net/platform/admin-ui.md`:** the catalog section says the Type column shows the full names, and only the icon on a phone.
