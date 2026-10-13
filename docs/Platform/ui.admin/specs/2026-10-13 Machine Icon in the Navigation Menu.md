# Machine Icon in the Navigation Menu

Linear: [424-27](https://linear.app/424fun/issue/424-27/change-icon-of-machines-section-in-navigation-menu)

## Problem

The **Machines** entry of the navigation menu (`src/Platform/ui.admin`) uses the `cpu` icon. The same icon is the icon of the **Unit of execution** annotation type, which appears in the annotation grid, the tree, the type badges, the type filter and the graph. A machine (a service or pipeline with its own credentials) and a unit of execution (an annotation) are unrelated, but they look the same.

The issue asks to use the `computer` icon for machines.

## Current State

- **Navigation:** `app/utils/navigation.ts`, line 69, the **Project** section:

  ```ts
  { label: 'Machines', icon: 'i-lucide-cpu', to: `${base}/machines`, minRole: 'Contributor' },
  ```

- **Unit of execution:** `app/utils/annotation-key.ts`, line 117, `typeMeta.uxe.icon` is `'i-lucide-cpu'`. All the type icons are read from `typeMeta` (through `AnnotationTypeBadge`, `metaOf`, the tree and the graph), so this is the only place that defines it.
- **Other machine screens use the same icon:**
  - `app/pages/[org]/[project]/machines.vue`, line 92: the empty state ("No machine access").
  - `app/pages/[org]/[project]/machines/[id].vue`, line 30: the icon next to the machine name in the detail panel header.
  - `app/components/access/MachineCreateDialog.vue`, line 81: the **Create** button of the "New machine" dialog.
- **Icon set:** `@iconify-json/lucide` (installed `^1.2.142`) has `computer` (a monitor above a desktop unit). Icons are bundled at build time from the literal names in `app/**/*.{vue,ts}` (`README.md`, "Icons"), so a new literal `i-lucide-computer` is picked up with no configuration change.
- **Tests:** `test/unit/utils/shell.test.ts` tests `buildNavigation` (sections, roles, locks) but not icons. No e2e test looks at the Machines icon. The visual baselines show the sidebar on desktop (`annotations`, `members`, `machines`, … `-desktop-*`); the icon is 16 × 16 px, below the 0.2 % tolerance of the visual suite, so the baselines may or may not need an update.
- **Docs:** `docs/42for.net/platform/admin-ui.md` lists the Machines screen but does not describe icons.

## Proposed Changes

### 1. `computer` for the Machines entry (`app/utils/navigation.ts`)

Change the icon of the **Machines** entry to `i-lucide-computer`. Unit of execution keeps `i-lucide-cpu`; it is the annotation type's icon and is used in many more places.

### 2. The same icon on the other machine screens

So that a machine looks the same everywhere, use `i-lucide-computer` also in:

- the Machines empty state (`machines.vue`);
- the machine detail panel header (`machines/[id].vue`);
- the **Create** button of `MachineCreateDialog.vue`.

After the change, `i-lucide-cpu` is used only by `typeMeta.uxe`.

### 3. Tests

- **Unit** (`test/unit/utils/shell.test.ts`, `buildNavigation`): a project-scope navigation for a Contributor has the **Machines** entry with `i-lucide-computer`, and no navigation entry uses the icon of any annotation type (`typeMeta[*].icon`), so a menu entry cannot be mistaken for a type again.
- **Visual:** run `npm run test:visual:docker`. If a baseline differs, regenerate it with `npm run test:visual:docker -- --update`; only screens with the project sidebar or the machines screen may change.
- `npm test`, `npm run typecheck` and `npm run lint` must pass.

### 4. Documentation

No user-facing documentation describes the icons, so no documentation change beyond this spec and its review.
