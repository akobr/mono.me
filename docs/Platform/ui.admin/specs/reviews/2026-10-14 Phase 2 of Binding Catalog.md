# Phase 2 of Binding Catalog

## Overview

Phase 2 covers the Administration UI (sections 2.1 to 2.9) of [Binding Catalog with Examples and IntelliSense](../../../Storyteller/specs/2026-10-14%20Binding%20Catalog%20with%20Examples%20and%20IntelliSense.md), Linear 424-10. It builds on the `GetBindingCatalog` endpoint and the regenerated TypeScript SDK of [Phase 1](../../../Storyteller/specs/reviews/2026-10-14%20Phase%201%20of%20Binding%20Catalog.md) (`open.api.v0.9.2.json`).

- **Code editors** (configuration Edit and Effective tabs, templates): typing `@` in a string value completes the functions, the sources, and the interpolation and math forms. Inside `@config("…")` and `@annotation("…")` the editor offers JSON Pointers of the configuration or of the annotation's `Values`, and the second argument of `@annotation` offers the ancestor types. Hovering a binding or an envelope key shows a card that explains it.
- **Binding reference:** a floating view with the expression forms, the functions and sources of the server, and the object bindings, each with examples to copy. It opens from **fx Bindings** above the editors and from the **fx** badge of a form field, where **Use** puts an example into the value.
- **Schema form:** a line under a binding field explains the expression, or points at a likely mistake.
- **Mock API:** it serves the catalog, and its sample binding is now a valid expression.

## What Was Done

### 2.1 Binding language model (`app/utils/binding-language.ts`)

- `builtInExpressions` (path, sourced path, function call, interpolation, math) and `builtInEnvelopes` (JSON Logic, JSON-e), each with a title, a Markdown summary, the syntax and examples.
- `fallbackFunctions`: `annotation` and `config` with the same texts as the server. `catalogView(server?)` merges them into a `BindingCatalogView`. `known` tells whether the functions and sources came from the server.
- `explainBinding(text, catalog)` returns the kind, the name, a title, a summary, the syntax, an example, and an optional `problem`. It reports:
  - a character that cannot follow a path (`@sbt.northwind/baseUrl`);
  - a malformed sourced path;
  - a missing `)`, `]` or `}`;
  - an unquoted argument of a function whose signature quotes it (`@config(maxPrice)`);
  - a path when the server has no `default` source.
- An unknown function or source is reported only when `known` is set, so nothing is flagged before the catalog arrives.
- `bindingCompletionContext(prefix)` returns `start` (with `nested` inside interpolation or math), `source` or `argument` (with the function, the argument index and the text typed so far).
- **Deviation:** the spec had a separate `annotationType` context. The `argument` context carries the index instead, and the completion source maps the second argument of `annotation` to the types. That also covers any function whose parameter lists `Values`.
- Also added:
  - `quotedArguments(text, fn)`, which reads the type of `@annotation` that follows the cursor;
  - `quoteFor(delimiter)`;
  - `pointersOf(value, limit = 200)`, breadth first, which does not descend into envelopes;
  - `ancestorKeyOf(key, type)`, case-insensitive like the server;
  - `ancestorTypesOf(key)` and `ancestorTypesOfType(type)` for templates.

### 2.2 Binding assist (`app/composables/useBindingAssist.ts`)

- `useBindingAssist({ annotationKey, annotationType, documents })` uses `useGetBindingCatalog` (stale for 10 minutes, no retry, `meta: { silent: true }`).
- `annotationPointers(type)` fetches `GetAnnotation` through `queryClient.fetchQuery` with the query key of the generated composable, so the annotation the detail already loaded comes from the cache.
- **Deviation:** `document` became `documents`, and the configuration pointers are the union of all of them. The editor passes the effective document and the draft, so keys that are not saved yet are offered too.
- **Deviation:** a template has no annotation key, so the option `annotationType` was added. The ancestor types of a template come from a placeholder key of that type.
- `staticBindingAssist` (the fallback catalog, no pointers) is what `injectBindingAssist()` returns without a provider. The schema form therefore works in the unit tests, where `mountInApp` has no Vue Query.

### 2.3 CodeMirror: completion and hover (`components/editor/setup/bindings.ts`)

- `bindingDecorations(assist = staticBindingAssist)` adds `autocompletion()`, a completion source through `EditorState.languageData`, and a `hoverTooltip`.
- `stringAt(line, pos)` finds the string around the cursor: JSON or YAML `"…"` (with escapes), YAML `'…'` (with `''`), or a YAML plain scalar after `: ` or `- `. An unclosed string runs to the end of the line. An object key is never a binding. `decodeString` turns the raw text into the stored text.
- **Completion:**
  - Functions are snippets. Quoted parameters become empty fields with the quote of the string: `\"` in JSON and YAML `"…"`, `"` in YAML `'…'`. **Deviation:** the spec had a `${/path}` placeholder. The field is empty instead, and the path list opens right away (`startCompletion`).
  - Sources are offered by name and insert `(path.to.value, name)`. After `@(path, ` they complete by name only.
  - The `[…]` and `{…}` forms are offered at the top level only.
  - Each option has an info card.
- **Fix found by the end-to-end test:** CodeMirror keeps the state of a completion source by its identity. The first version created a new function on every call of the language data provider, so results were dropped and no list ever opened in the browser. The source is now created once per extension, and a unit test checks the identity.
- **Hover:** the card has the kind and name, the summary, the syntax, the first example, the problem, and "Evaluated by the server when the resolved configuration is read". Over an envelope key it shows the JSON Logic or JSON-e card, or both when `$binding` is not on the same line.
- **Highlighting:** the pattern also marks YAML `'@…'` strings. The YAML serializer writes those for values that contain `"`, so `@config("/x")` was not highlighted in YAML before. It also marks the `$binding` and `$context` keys, which the server uses.

### 2.4 Binding reference (`components/configuration/BindingReference.vue`)

- A `UPopover` whose default slot is the trigger (by default **fx Bindings**). The content is a region "Binding reference" with a filter and four sections: Expressions, Functions, Sources and Object bindings.
- Entries show the signature or syntax, the parameters with their allowed values, and badges (`secret`, `default`).
- Examples have **Copy** (`CopyButton`), and **Use** when `insertable` is set. **Use** emits `insert` and closes the popover. Envelope examples are pretty-printed JSON, with **Copy** only.
- An empty Sources section says "No source is registered on this server", or "Loading…" until the catalog arrives.
- A link button opens the Bindings section of the documentation.

### 2.5 Schema form

- `BindingExpressionInput.vue`:
  - The "fx" badge is now a link-style button (aria-label "Binding reference") that opens the reference with **Use**.
  - A line under the input reads "Function config: Reads a value of …". When there is a problem, the problem replaces the summary, in the warning color with `role="status"`. The existing line "Evaluated by the server; …" and **Convert to value** stay.
  - Envelopes use `bindingDecorations(assist)` in their JSON editor.
- `ControlField.vue`: the fx toggle's tooltip is "Use a binding expression (@…): a function, a source, text or math".

### 2.6 Editors

- `ConfigurationEditor.vue` provides the assist (annotation key, effective document and draft) and uses one `bindingDecorations(assist)` for every mode. **Deviation:** **fx Bindings** sits next to **Delete own** in every mode, the form included, not only in JSON and YAML.
- `ConfigurationEffective.vue`: the same assist for hover, and **fx Bindings** in the editor actions.
- `TemplateDetail.vue`: an assist with `annotationType` and the draft, **fx Bindings** in the actions, and a constant extension array. The inline `[bindingDecorations()]` used to reconfigure the editor on every render.

### 2.7 Mock API

- `bindingHandlers` in `app/mock/handlers.ts` serves `GET */api/v1/:org/:project/bindings` (Reader) with `mockBindingCatalog`: the fallback functions and a secret `primaryVault` source. It is registered in `all-handlers.ts` after the view handlers.
- **Deviation:** the spec suggested `@annotation("/baseUrl", "Subject")` for the mock `endpoint`. The mock subjects have no `baseUrl` in their values, but the mock contexts configure one, so the value is `@config("/baseUrl")`. The schema description says the same.

### 2.8 Tests

- **Unit** (Vitest, 341 pass in 36 files; there were 308 in 33 files):
  - `test/unit/utils/binding-language.test.ts` (14): every kind and mistake of `explainBinding`, the behavior before the catalog is known, every completion context, `quotedArguments`, `quoteFor`, `pointersOf`, `ancestorKeyOf` for every type of an `uxe` key, and the ancestor types of a key and of a type.
  - `test/unit/configuration/binding-editor.test.ts` (13): the completion source on JSON and YAML `EditorState`s: options after `@` and inside interpolation, the range that is replaced, the snippet quotes, sources, config and annotation pointers (and which ancestor is asked), ancestor types, nothing in plain strings, keys or numbers. Also the stable identity of the source, `stringAt`/`decodeString`, and the hover card.
  - `test/unit/configuration/binding-reference.test.ts` (4): the sections and entries, the filter, **Use** emitting and closing, and no **Use** for object bindings.
  - `schema-form.test.ts` (+2): the explanation of a valid expression and of `@sbt.northwind/baseUrl`, and **Use** from the reference replacing a field's value.
- **End to end** (`test/e2e/configuration.spec.ts`, +2, desktop and mobile):
  - "the JSON editor completes a binding and explains it on hover" types `@con`, picks `config`, then `/retries` from the path list, closes the string and hovers.
    - The options are clicked, not accepted with Enter: a key pressed within CodeMirror's `interactionDelay` (75 ms) after the list opens is ignored.
    - In the full parallel run, one pointer move sometimes did not open the hover, so the test moves the pointer away and back inside `toPass`.
  - "the fx reference of a form field lists the sources of the server and fills the value".
- **Results:**
  - `npm run typecheck` and `npm run lint` report no problems. Prettier reports no problems in the changed files.
  - `npm run test:e2e` with `PLAYWRIGHT_CHANNEL=chrome`: 83 passed and 3 skipped. The axe check of the configuration detail passes with the new buttons.
  - `npm run test:visual:docker` (compare only): 40 passed, so no baseline was changed. The template screen gained **fx Bindings**, which stays within the suite's 0.2 % pixel tolerance. Docker Desktop was started for this run and stopped afterwards.

### 2.9 Documentation

- `docs/42for.net/platform/admin-ui.md`, Configuration: the invalid `@sbt.northwind/baseUrl` is replaced by `@config("/baseUrl")`, with the explanation line, the binding reference, and completion and hover in the JSON and YAML editors.
- `docs/42for.net/platform/configuration.md`, Bindings: a paragraph that the functions and sources depend on the deployment and that the admin UI lists and completes them.

### Notes

- `app/utils/bindings.ts` still detects envelopes by `$jsonlogic`, `$jsone` or `$definition` (the original admin UI design). The server's envelope is `$binding` plus `$definition`, so real envelopes are still detected through `$definition`. The detection was left unchanged; only the highlighting learned `$binding` and `$context`.
- Two unit tests still use `@rst.invoicing/retries` as a sample binding. They test detection only, and the explanation line now marks it as a likely mistake, which they do not check.
- Completion inside a YAML plain scalar that starts with `@` is not offered for function arguments. Such a scalar is not valid YAML anyway (`@` is reserved), and the serializer always quotes it.
