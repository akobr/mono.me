# Remember the Last Modeling Tab and Detail of a View

Linear: [424-67](https://linear.app/424fun/issue/424-67/remember-the-last-modeling-tab-and-detail-of-a-view)

## Overview

This review covers the whole spec [2026-10-19 Remember the Last Modeling Tab and Detail of a View](../2026-10-19%20Remember%20the%20Last%20Modeling%20Tab%20and%20Detail%20of%20a%20View.md), delivered in one phase. **Modeling** is now a remembered section of the page memory. Its sidebar entry and its command palette entry return, per view, to the last tab, the open template or schema and their query, such as `?cascaded=1` on Deleted or `?key=…&to=…` on Compare views. The plain `…/modeling` redirects to Schemas before any guard runs, so the page memory guard reads it from `to.redirectedFrom`. The guard lets its own redirect through by the identity of the navigation, so a location in the browser's spelling cannot loop. A dirty editor now asks "Discard changes?" once when the page memory redirects, on every section. The code follows the spec, with two small deviations in the tests (section 4) and wording changes from the code review (sections 2, 3 and 6).

## What Was Done

### 1. Modeling as a remembered section (`app/stores/page-memory.ts`)

As planned:

- `rememberedSections` is `['annotations', 'configurations', 'graph', 'modeling']`.
- `ModelingMemory = Record<string, never>` is the state of Modeling in `SectionMemories`, and `empty.modeling` returns `{}`.
- `location`, `rememberLocation`, `page` and `clear` did not change, and no page calls `usePageMemory('modeling')`.
- The doc comments of `rememberedSections` and of the store name Modeling and the new spec.

The new type is exported, so `app/auto-imports.d.ts` lists `ModelingMemory`. That regenerated line is a commit of its own.

### 2. The plain Modeling URL behind its redirect (`app/router/guards/page-memory.ts`)

As planned:

- `pageMemoryGuard` first lets through a pass whose `to.redirectedFrom` is in the module-level `restored` `WeakSet`.
- It takes the requested plain list from `to` when `to` is a root, and otherwise from `redirectedList(to, target)`. That function accepts `to.redirectedFrom` only when it is a root of the same section and view as `to`.
- The query, hash, `START_LOCATION` and same-section rules apply to the requested list as before.
- Before it returns the remembered location, the guard adds `to.redirectedFrom ?? to` to `restored`. The `to.fullPath` comparison now only skips a redirect to the location the navigation already has.
- `rememberPageLocation`, the redirects, `utils/modeling.ts`, `utils/navigation.ts`, `utils/context-paths.ts` and `ModelingHeader.vue` did not change.

The doc comment of `pageMemoryGuard` names Modeling and the `to.redirectedFrom` reading. After the code review, the comment above `restored` says which location is the key: `to.redirectedFrom` behind Modeling's redirect, and `to` itself otherwise.

### 3. One Discard prompt per navigation (`app/composables/useUnsavedChanges.ts`)

As planned, in its own `fix` commit:

- Each `useUnsavedChanges` keeps a `discarded` `WeakSet`.
- A pass whose `to.redirectedFrom` is in it goes through without a prompt.
- After **Discard**, the guard adds `to.redirectedFrom ?? to`.

This fixes the double prompt that Annotations, Configurations and Graph already had behind a page memory redirect, and keeps it away from Modeling. After the code review, the comment names the key the same way as in the guard.

### 4. Unit tests

`test/unit/router/page-memory.test.ts`, now 18 tests:

- **Deviation:** the existing test "does not redirect links with a query, unremembered sections or the first navigation" became "… unremembered pages …". Its middle step uses `/acme/billing/settings` as the other page, as planned. Its unremembered example is the values store (`…/key-values/db.password`, Settings, then `…/key-values`), a view page that the page memory does not remember. The plan named only a project page, which would show nothing about the plain list of a view page.
- The new `describe('the page memory of Modeling')` runs the generated routes with a counting `beforeEach` registered before `installPageMemoryGuard`, and an `afterEach` counter. It has 11 tests:
  - the four restores (Deleted with `?cascaded=1`, the Execution template, Compare views with `?key=&to=`, a schema detail), each in two passes and one landing, with Back returning to Annotations;
  - the trailing slash;
  - the browser's spelling: `?key=sbt.O%27Brien` comes back as `?key=sbt.O'Brien` in two passes;
  - what stays as it is: the explicit tabs, `?x=1`, `#x`, the old `…/schemas` and `…/deleted?cascaded=1`, and the first navigation of a fresh router;
  - Schemas without a remembered location and from within Modeling;
  - the views apart;
  - the query changed with `router.replace`, a cancelled navigation into Modeling, and `clear()`.
- **Deviation:** the two "other sections behind a redirect" cases of the spec are one test. It covers Annotations from `?q=type%3Asbt` in two passes, and the project index redirect `/acme/billing`.

`test/unit/components/unsaved-changes.test.ts`, new, 5 tests:

- It mocks `useConfirm` and mounts an always-dirty editor in a `RouterView`, with the page memory guard.
- It covers one prompt through the Modeling redirect and through the plain Configurations list, **Keep editing**, a new prompt after a navigation that another guard cancelled after **Discard**, and one prompt without a redirect.
- After the code review, the tests unmount their hosts, as the other component tests do.

Checks against broken code:

- With the guard of `main`, 8 of the 11 new Modeling tests fail.
- Without the `restored` check (the plan's string comparison), the browser-spelling test and the Annotations test fail with "Infinite redirect in navigation guard".
- With the `useUnsavedChanges` of `main`, 3 of the 5 tests fail: the two restores and the retry after a cancelled navigation ask twice.

### 5. End-to-end test (`test/e2e/navigation.spec.ts`)

As planned, the test "comes back to the tab, the detail and the query of Modeling after another section" runs on desktop and on mobile:

1. It opens `…/modeling/deleted` with `openApp` and turns on **Show cascaded**.
2. It leaves for Annotations and returns through **Modeling** in the menu: the URL is `…/modeling/deleted?cascaded=1`, the Deleted tab is selected and **Show cascaded** is on.
3. On desktop only, it opens the Execution template with `getByRole('link', { name: /^Execution\b/ })`, leaves for Configurations and returns to `…/modeling/templates/exe` with the Templates tab selected and the "Execution template" region visible.
4. It selects **Modeling** again and lands on `…/modeling/schemas`.

The URLs are compared through `relativeUrl`, because `openApp`'s `mock-scenario` stays in the remembered query. With the guard of `main`, the test fails on both projects: it lands on `…/modeling/schemas`.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - A paragraph after the list of what the three screens keep says what **Modeling** remembers, and that its sidebar and command palette entries return there. It also says that the switcher keeps the tab you are on, and that the palette's tab entries and `G` then `T`, `S` or `E` open the tab they name.
  - "Selecting the screen you are already on" adds that **Modeling** opens **Schemas**.
  - The Modeling section says that the sidebar entry returns to the tab you left. Modeling opens **Schemas** the first time, when its entry is selected while it is open, and from a typed or bookmarked `…/modeling`.
- `src/Platform/ui.admin/README.md`:
  - The `page-memory` store entry names the last tab, detail and query of Modeling. The first version described the guard's internals there. After the code review, they moved to the Modeling note.
  - The Modeling note now covers four points:
    - when the guard returns to the last Modeling location: from outside the Modeling of the view;
    - why it reads `to.redirectedFrom`;
    - how it lets its own redirect through;
    - that the plain `modeling` stays on Schemas from within Modeling.
  - The note on `useUnsavedChanges` says that it asks once per navigation, also when a guard redirects it.
  - The e2e list names the return to the last tab, detail and query of Modeling.

### Results

- `npm test`: 859 tests, 858 passed. The one failure, "selects the first type that can descend when the key changes" in `templates-schemas.test.ts`, also fails on `origin/main` (`f84046ba`). It expects the schema path from before Modeling (`…/schemas/annotation/…`).
- `npm run lint`, `npm run typecheck` and `npm run build` pass.
- The whole e2e suite on desktop and mobile: 306 passed, 7 skipped and 1 failed. The failure was `net::ERR_CONNECTION_REFUSED` from the preview server on "dark › members has no serious accessibility violations" (mobile), a page this change does not touch. The rerun of the navigation, annotations, configuration and accessibility specs passed: 223 passed, 5 skipped.
- No visual baseline changes, because the screens look the same.

### Notes

- **Spec review before implementation.** Three reviewers checked the spec, and each finding was verified against Vue Router 5.4.0. They found that the plan's string comparison could loop forever in production, and that the leave guards ask twice behind a guard redirect. They also found the order of the counting spy and the wording of `admin-ui.md`. The spec took all of these before it was committed.
- **Code review after implementation.** It found the key comments, the missing unmount in the new component test, and the README wording. All of them were fixed. Two other findings were refuted.
- **Still out of scope:**
  - the context switcher, which keeps the current Modeling tab in the other view;
  - the palette's tab entries and the `G` shortcuts, which open the tab they name;
  - the hash, which Deleted and Compare views drop when they replace their query;
  - a remembered Page not found under Modeling;
  - remembering across reloads.

## The CodeRabbit Plan

As the spec says, the plan was a second opinion:

- **Taken:** Modeling as a location-only section, `to.redirectedFrom` as the requested plain list with the same-section and same-view check, no change to the redirects and the utilities, the unit tests on the generated routes and an e2e test with clicks only.
- **Changed:**
  - the guard's own redirect goes through by identity, not by the `to.fullPath` comparison of the plan, which loops for a location in the browser's spelling;
  - the double Discard prompt is fixed;
  - the template detail of the e2e test runs on desktop only;
  - the tests cover the browser's spelling and the project index redirect.
