# Composition of the Effective Configuration

## Overview

The effective configuration can now be explained like a blame view. A new read, `GET v1/{organization}/{project}/{view}/configuration/{key}/composition`, returns every document merged into the effective document (ancestor configurations and type templates, each once with its content), the calculations of the inheritance graph unfolded into a flat node list, the merges in the order of application, and the merge that set each value as RFC 6901 origins. Storyteller composes it read-only from the stored documents and lets the real `MergeInto` carry the step of every value, so the attribution cannot drift from the merge. Both SDKs are regenerated from `open.api.v0.9.42.json`. `sform story config composition <key>` prints the steps and the effective document with a step column. The admin UI shows it as the **Composition** view of the Effective tab, with a gutter per line, the ordered steps and their paths. This review covers [Composition of the Effective Configuration](../2026-10-20%20Composition%20of%20the%20Effective%20Configuration.md) in full, delivered in the six phases of its section 12, one review for all of them, for Linear [424-36](https://linear.app/424fun/issue/424-36/add-possibility-to-see-how-configuration-is-build).

## What Was Done

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### 1. The composition model

As planned, in `Abstractions.Annotations/src/Model/ConfigurationComposition.cs`: `ConfigurationComposition` (`AnnotationKey`, `Content`, `Hash`, `Sources`, `Nodes`, `Steps`, `Origins`), `ConfigurationCompositionSource`, `ConfigurationCompositionNode`, `ConfigurationCompositionStep`, `ConfigurationCompositionOrigin`, and the enums `ConfigurationCompositionSourceKind` and `ConfigurationCompositionSourcePresence` with `StringEnumConverter`. The file holds several types, as `ConfigurationSummary.cs` does, so it carries the same SA1201 and SA1402 warnings.

### 2. The read-only composition

As planned:

- `IConfigurationService.GetConfigurationCompositionAsync(FullKey)`, with `FakeConfigurationService` throwing `NotSupportedException`.
- `CosmosConfigurationService` is `partial`; `Backend.CosmosDb/src/Configuring/CosmosConfigurationService.Composition.cs` holds the read.
  - The root rules are those of `GetRawConfigurationAsync`.
  - `ComposeNodeAsync` mirrors `CalculateAndCacheConfigurationAsync`: each `GetAncestors()` parent's finished document, then a template step when the view has the template, then the configuration step, which is always there.
  - Nodes are added in post-order; a node fills in `MergedInto` of its parents when it is added.
  - A private `CompositionWalk` reads each configuration item and each template once per request and lists each source once, at its first step.
  - An item with version 0 and empty `Content` (a cache-only item) is `Missing`, like no item.
  - `Hash` uses `CalculateMurmurHash32Bits` with the `Unique` settings, only for a non-empty document.

**Deviation.** Templates are read through the existing private `ReadTemplateAsync` (the ETag is ignored) instead of a new `TryReadItemAsync` call: the same id and partition as the Effective calculation, one read per type per request.

### 3. Origins by annotation

As planned, `Backend.CosmosDb/src/Configuring/ConfigurationCompositionOrigins.cs` (public static): `Mark` clones a document without annotations and marks every value token (the root too, every token but `JProperty`) with a private `StepOrigin`; `Collect` walks the values in document order with RFC 6901 pointers and throws on a value without a mark; `Strip` clones without annotations. Newtonsoft keeps its line info as annotations, so `Mark` and `Strip` clone with `CopyAnnotations = false` and only the step mark is carried.

### 4. The HTTP read

As planned:

- `Definitions`: the route `Routes.Configuration.V1.ConfigurationComposition`, the operation id `RouteIds.Configuration.GetConfigurationComposition` and `Descriptions.ConfigurationComposition`.
- `ConfigurationHttp` is `partial`; `Api.Functions/src/V1/ConfigurationHttp.Composition.cs` has `GetConfigurationComposition`: scopes, `CheckAccessToProjectAsync`, key parse (400), the service, `NotFoundAsync` (404, `AnnotationDeleted`), `MarkNoCache` and `OkOrNotModified`. The OpenAPI attributes have the shapes of `GetConfiguration`.
- `ConfigurationEntityTags.ForComposition` with `CompositionPrefix = "c"` hashes the prefix, the full key and the composition serialized by Newtonsoft (`Formatting.None`).

**Deviation.** The digest code of `ConfigurationEntityTags` moved into a private `Finish` used by `Create` and `ForComposition`; the `e`, `r` and `b` tags are unchanged, and their tests pass.

### 5. API tests

As planned, plus four:

- `ConfigurationCompositionHttpTests` (14 tests): 200 with the `c` tag and `private, no-cache`; 304 for matching `If-None-Match` (4 rows); a new source version with the same `Content` and `Hash` gives another tag; 400 for invalid keys without a service call; 404, and 404 `AnnotationDeleted`.
  - Beyond the spec: a direct test of the tag inputs (source version, origin step, content, view, key), an unrestricted machine reaching the service, a user without a role getting `AccessDenied` (asserted in Release), and the System.Text.Json shape of the response.
- `MachineRestrictionHttpTests.Handlers.cs`: a `ProjectWideHandlers` row; a restricted machine gets `403 AnnotationRestricted` for its own annotation.
- `ConfigurationConditionalReadTests`: the operation id in the document theory.
- `RouteTableTests.ConfigurationCompositionRoute_IsRegisteredOnce`.
- `OpenApiDocumentTests.ConfigurationCompositionSchemas_ArePublished`: the five schemas, the inline enums and the nullable `MergedInto`.

### 6. OpenAPI snapshot and SDKs

As planned. The document was exported after the API commit through `OpenApiExportTests`: `info.version` `0.9.42.14388`, so `open.api.v0.9.42.json` in `Sdk.NSwag/` and `sdk.typescript/`, and `sdk.typescript/open.api.v0.9.32.json` removed. Against `v0.9.32` the document adds one path and the five schemas and changes nothing else.

- `nswag openapi2csclient` 14.6.3 with the README command; the README names the new input.
- `ApiSdk.partial.cs`: `ConfigurationsApiClient.UpdateJsonSerializerSettings` sets `DateParseHandling.None` (section 6.5).
- `npm ci`, `npm run generate` (Orval 8.41), typecheck, lint, test and build in `sdk.typescript/src`; a second `generate` left `generated/` unchanged.
- `test/msw.test.ts` has the `getConfigurationComposition` Zod row and `test/helpers.test.ts` the composition query key under the configuration's prefix.

### 7. `sform config composition`

As planned: `ConfigCompositionCommand`, `CommandNames.COMPOSITION`, the `[Subcommand]` entry of `ConfigGetCommand`, the `story-config-composition` launch profile, `Output/CompositionConsoleExtensions.cs` and `Output/CompositionLines.cs`. For the execution example of `inheritance.md` (the fixture of `ConfigCompositionCommandTests`, with an execution template), the header, table rows and blame lines are these (column spacing approximate):

```text
# Composition of the configuration for 'exe.northwind.invoicing.prod'
 Step  Applied at                    Source              Version  Presence  Values
 1         rst.invoicing             configuration       —        missing   0
 2         sbt.northwind             configuration       v3       present   1
 3       usg.northwind.invoicing     configuration       v2       present   1
 4         sbt.northwind             configuration       v3       present   1
 5       cnt.northwind.prod          configuration       v1       present   1
 6     exe.northwind.invoicing.prod  template Execution  v4       present   1
 7     exe.northwind.invoicing.prod  configuration       v1       empty     0

# Effective configuration, by step
    │ {
  4 │   "currency": "NOK",
  2 │   "residency": "norway",
  3 │   "plan": "enterprise",
  5 │   "endpoint": "https://prod.northwind.example",
  6 │   "mode": "batch"
    │ }
```

Tests: `ConfigCompositionCommandTests` (exact SDK call, table, blame lines, 404, 403 `AnnotationRestricted` with `WARNING_UNAUTHORIZED_ACCESS`, 401 left to `Program`, invalid keys, export, parsing, and a table rendered at 80 columns), `CompositionLinesTests` (text identical to Newtonsoft's indented output, and a date-like string read through the real `ConfigurationsApiClient`), and `ConfigurationsApiClientDateTests` (the client keeps date strings in `Content` while `AuditStamp.At` parses).

**Deviations.**

- The blame lines go through `WriteExactDocument` with character wrapping and no margin, not `WriteLine`: Alba drops one or two characters at each break of a long line with a margin. A blame line wider than the console wraps to column 0.
- The step table gives `Applied at` a star column, so the short columns stay on one line in 80 columns; at 60 columns Alba can still squeeze the key.
- `GetStepRows` is a public helper, and `--export` also prints the composition, as `config <key> --export` does.

### 8. Admin UI

As planned, under `src/Platform/ui.admin/src/`:

- `ConfigurationEffective.vue`: a `Document | Composition` radiogroup bound to `?show=composition`; the Document view is unchanged.
- `composables/useConfigurationComposition.ts`: enabled only while shown, `staleTime: 0`, refetch on focus.
- `utils/composition-lines.ts`: `compositionLines` (the text of `toJson`, steps by pointer), `compositionSteps` (source, node, `via`, `again`, the paths a step set) and `stepSource`.
- `components/editor/setup/composition.ts`: the gutter (full-contrast number, the type color as a tint, `T` on template steps), the selected-step decorations and effect, marker clicks and cursor reports.
- `components/configuration/ConfigurationComposition.vue`: the explanation, the ordered steps indented by depth with sibling toggle and link, the `<details>` of paths, the read-only editor, the polite status line, the loading, 404, error and empty states, and the stale-hash flow.
- `mock/configuration-handlers.ts`: `compositionOf` and the handler; `inMergeOrder` and `mockHash` are now shared with the mock Effective read.
- Tests: `composition-lines.test.ts` (7), `composition-editor.test.ts` (5, a real `EditorView` in happy-dom), `configuration-composition.test.ts` (18, against the mock and a fixture shaped like the server's execution), the composition e2e test on desktop and mobile, and a Composition screen in `accessibility.spec.ts`.

**Deviations.**

- The marker's hover is a native `title`, as the `fx` marker of `bindings.ts` does: CodeMirror's hover tooltip does not fire over the gutter.
- The type badge uses `variant="icon"`, because the `type-names` test allows the code chip on Schemas only.
- The read-only editor content has `tabindex="0"`, so a keyboard user can move the cursor that the status line names.
- An ancestor that is not in the loaded catalog is struck through in `text-muted` (contrast) and has a **Deleted** badge; while the catalog is loading, it is plain text.
- Selecting a step scrolls with `y: 'nearest'`, not `'center'`: CodeMirror also scrolls the detail panel around the editor, and centring took the focused toggle out of view.
- The hashes are compared once the composition is read, the recheck is reset per configuration, the warning is also announced through a `role="status"` text, and **Refresh** moves the focus to the steps heading, because the alert and its button disappear while both documents are read again.

### 9. Documentation

As planned: "Composition read" in `docs/Platform/Storyteller/inheritance.md` (with the `c` tag in "Conditional reads" and its limits), a row and the CLI paragraph in `templating.md` (whose execution row now gives the real recursive order), "The effective document" in `docs/42for.net/platform/configuration.md`, the Effective tab and its Composition view in `admin-ui.md`, the route in `using-the-platform.md`, and the e2e list, the mock note and an architecture bullet in `src/Platform/ui.admin/README.md`.

### 10. Consistency

As planned, the composition reads without a snapshot and reports through `Hash`. The admin UI compares it with the Effective hash only once the composition is read and after one more Effective read.

### 11. Tests of the backend

As planned:

- `ConfigurationCompositionOriginsTests` (14, no emulator): every merge rule of section 11, the equal scalar, `1` against `1.0`, the array rules, the replacements, and the non-associative example.
- `CosmosConfigurationCompositionTests` (9, emulator), each in its own `cmp-tests-*` project with its own copies of the helpers. A shared check compares the composition with the cold and the cached Effective read (content with property order, hash), runs the replay rule, and composes twice. Covered: a chain, the execution (NOK over USD, with the expected nodes, steps and origins), the unit of execution (`retries` 1 over 9, nine nodes), an ancestor template applied twice, the twelve steps with a template for every type, a cold composition that writes nothing, the presence values, deleted and unknown annotations, and an active annotation without an item.

**Equivalence experiment.** During the branch review, a temporary test generated 300 random cases over executions and units of execution (nested objects, arrays with duplicates and objects, nulls, equal and differing scalars, type changes, `1` against `1.0`, empty objects, names with `~` and `/`, missing ancestors and templates). The composition's content and hash equalled the Effective read's in every case, and the replay rebuilt the content. The test was removed afterwards.

### Test results

| Project | Before | After |
| --- | --- | --- |
| `Backend.CosmosDb.UnitTests` (emulator) | 415 passed | 438 passed |
| `Backend.Core.UnitTests` | 810 passed | 810 passed |
| `Api.Functions.UnitTests`, Debug | 1134 passed | 1152 passed |
| `Api.Functions.UnitTests`, Release | — | 1143 passed, 9 known failures |
| `Cli.UnitTests` | 692 passed | 715 passed |
| `sdk.typescript` Vitest | 126 passed, 1 skipped | 127 passed, 1 skipped |
| `ui.admin` Vitest | 950 passed | 980 passed |
| `ui.admin` Playwright, configuration and accessibility specs, desktop and mobile | — | 158 passed |
| `ui.admin` Playwright, whole suite | — | 321 passed, 8 skipped, 1 failed |

- The 9 Release failures existed before this branch: `MachineHttpTests` (8 rows) and `AnnotationsHttpTests.Purge_ByUser_CallsTheService`, whose test users have too low a role for a build without `DEV_AUTH`.
- The one failure of the whole Playwright suite was `ERR_CONNECTION_REFUSED` on port 4174 in "Export & Import lock of a contributor" (mobile, dark), unrelated; it passed when run again. The suite ran before the review fixes; the configuration and accessibility specs ran again after them.
- The emulator tests reset the local emulator's databases, as the test project always does.
- Lint, typecheck and build pass in both TypeScript projects.

### Reviews

- **Before the spec commit**, three skeptics (backend, API/SDK/CLI, admin UI) checked the spec against the code. Their 23 findings changed the spec before it was committed: the equal-scalar rule of the merge, cache-only items as `Missing`, the nine nodes of a unit of execution, the inline OpenAPI enums, `DateParseHandling` in the C# client, the public `WriteApiError`, `show` instead of `mode`, the e2e data of the mock, and the accessibility of the gutter (status line, paths list, focus).
- **After each phase**, two reviewers per phase: none found a defect in phases 1 and 2. In phase 4 one test that could not fail now goes through the real client. In phase 5 the e2e test now checks the scroll and waits for the Effective re-read before checking that no warning shows.
- **On the branch**, six lenses (equivalence by experiment, API and security, `sform`, UI correctness, UI accessibility, docs) found 15 issues, each checked by a skeptic. 12 were confirmed and fixed in `9b4f7222`, `de590abc` and `10cd4006`: the stale warning flashing while a cached composition refetched, the scroll that took the focused toggle out of view, the recheck memo, the quadratic path lists, the empty-document status, the unannounced warning, the deleted ancestor marked by strikethrough only, the 80-column table, and three doc statements. Refuted: the status line announcing every cursor move (it is meant to), the 9 Release failures (pre-existing), and the wrapped blame line (the console library wraps there).
- **One more change** after the branch review: the **Deleted** badge waits for the catalog to be loaded, so it never appears while the catalog is still loading.

### Deviations from the spec

Listed per section above. None changes the model or the API.

### Left for later

The spec's out-of-scope list stands: blame of the YAML and resolved documents, older versions and other views, and the descendant invalidation of the admin UI when an ancestor's configuration is saved. Also:

- A blame line wider than the console wraps to column 0 in `sform`, and `IExtendedConsole.WriteLine` in the toolkit loses characters at a wrapped margin, in every command.
- The mock composition stays flat, like the mock Effective read; shared ancestors and depth are covered by the unit-test fixture.

### Merge notes

`feat/424-57` regenerates the SDKs from `open.api.v0.9.43.json`. Whichever branch lands second exports the document again on top of the other, regenerates both SDKs, and runs the `MachineRestrictionHttpTests`, `OpenApiExportTests` and `Cli` tests again. The composition needs no management scope.
