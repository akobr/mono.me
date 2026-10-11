# Review of the PR 65 findings

## Overview

Pull request [#65](https://github.com/akobr/mono.me/pull/65) delivers the Administration UI of [Administration UI for 2S Platform](../2026-10-09%20Administration%20UI%20for%202S%20Platform.md) (phases B to H, see the other reviews in this folder). The JetBrains Air code review left 24 inline comments on it, in two runs (on `d443daa` and on `8e93e7a`); several comments of the second run repeat a finding of the first, so they describe 18 distinct findings. Every finding was checked against the current code of the branch and of the API (`src/Platform/Storyteller`) before anything was changed. 17 were still valid and are fixed, one commit per finding; one was already fixed on the branch. Nothing was pushed by this work.

The checks were: the UI unit tests (`npm test`, 157 tests), `npm run lint`, `npm run build` (vue-tsc and Vite), the Playwright flows against the mocked API (`npm run test:e2e` in the installed Chrome, `PLAYWRIGHT_CHANNEL=chrome`; 45 passed, 1 skipped, desktop and mobile), and the API unit tests of `Api.Functions` (146 tests). The visual suite needs the Playwright Linux image and was not run. The real API was not run; the three annotation-write findings rely on reading `AnnotationsHttp` and `CosmosAnnotationService`, and the change of the API is covered by new unit tests of the handler only.

## What Was Done

The numbers follow the order of the commits. "Comments" names the inline comments by their file.

### 1. Expired invitations keep Resend and Revoke (`InvitationsTable.vue`)

Valid. The API reports a pending invitation past its expiry as `Expired` and still accepts `ResendInvitation` (which renews it) and `RevokeInvitation` (`EnsurePending(entity, allowExpired: true)`), but the table rendered the actions for `Pending` only. Resend and Revoke now show for `Pending` and `Expired`; Copy link stays with `Pending`.

### 2. Invitation validity capped at 30 days (`InviteDialog.vue`)

Valid. The dialog allowed 1 to 90 days, `CosmosInvitationService.CreateInvitationAsync` rejects more than `InvitationOptions.MaxExpiresInDays` (30). The input is capped at 30.

### 3. Client credentials in the one-time dialog (`OneTimeSecretDialog.vue`, `utils/machines.ts`)

Valid. For a `ClientCredentials` policy `AuthKitMachineAccessService` returns the WorkOS client ID as `Id` and the client secret as `AccessKey`; the dialog labelled the secret "API key" and offered `Authorization: ApiKey` and `.env` snippets that cannot authenticate, and the token snippet lacked `client_id` and `client_secret`. `OneTimeSecret` now carries `credentialKind` (filled by `machineSecret` from `MachineAccess.CredentialKind`); for client credentials the dialog shows "Client ID" and "Client secret", a complete token request and a bearer `curl`, and no ApiKey snippets. On reset the API omits `TokenEndpoint` and `TokenScope` (`MachineAccess.cs`), so the snippet uses `<token-endpoint>` and `<scope>` placeholders there. Unit tests cover the mapping and the dialog.

### 4. Number values keep their fraction digits (`ValuesEditor.vue`)

Valid. `UInputNumber` formats with the default `Intl.NumberFormat` (3 fraction digits) and writes the rounded value back on blur. The editor passes `formatOptions` with 20 fraction digits and no grouping. `NumberControl.vue` of the schema form already sets its own `formatOptions` (6 digits, a deliberate choice) and was left alone.

### 5. Binding expressions on object arrays (`ArrayLayout.vue`)

Valid. The `fx` toggle and the expression input called `addItem(path, value)`, which only returns a dispatcher and would append rather than replace. `useJsonFormsArrayControl` has no `handleChange`, so the layout takes it from `useJsonFormsControl` and writes the value with it. A unit test clicks `fx` on an object array and checks the emitted document.

### 6. Inherited values in the schema form (`ControlField.vue`, `SchemaForm.vue`, `renderers/context.ts`, `ConfigurationEditor.vue`)

Valid. The "inherited" value was read from the effective document, which already contains the own content, so a cleared own value showed an inherited chip and placeholder with its old value, and Reset to inherited was offered on every own value. The form now also receives the own content as saved (`own`, the editor's `original`), and a path counts as inherited only where that content has nothing; Reset to inherited is offered only where an inherited value exists. The suggested alternative (an own-less effective document) is not available from the API, so a value that is both own and inherited shows no inherited hint until the own value is removed and saved.

### 7. Map entries with dotted keys (`MapControl.vue`)

Valid. JSON Forms data paths are dot-delimited and lodash parses brackets, so a map key such as `Microsoft.AspNetCore` rendered empty and an edit wrote `LogLevel.Microsoft = { AspNetCore: ... }` next to the real key. Keys containing `.`, `[` or `]` are now edited inline with the JSON editor and written back by replacing the whole map at the control's path. Not covered: schema property names (not map keys) that contain a dot, which the whole JSON Forms path model shares.

### 8. Cmd+S saved twice (`CodeEditor.vue` and the editors)

Valid. The CodeMirror binding prevented the default but let the keydown bubble to the window shortcut (`defineShortcuts` does not check `defaultPrevented`), so two identical requests went out and the second failed its `test` guard with 412 or applied an unguarded array add twice. The binding now sets `stopPropagation`, and `ConfigurationEditor`, `TemplateDetail`, `SchemaDetail` and `AnnotationForm` ignore a save while one is running.

### 9. Enter and Escape on the annotation list (`annotations.vue`)

Valid. `defineShortcuts` calls `preventDefault()` before the handler, so Enter on a focused Save, Revert, Delete or confirm button only re-opened the active row. Rather than binding Enter to the grid element (which would break the J/K cursor followed by Enter when the focus is on the page body), Enter and Escape are now a plain `keydown` listener on the window that ignores controls, links, menus and dialogs and acts without preventing anything else. J/K and the arrows stay shortcuts.

### 10. Switching the configuration sub-tabs (`[key]/configuration.vue`)

Valid. The Edit, Effective and Resolved sub-tabs are a `?cfg=` query change, which `useUnsavedChanges` skips (`to.path === from.path`), and `v-if` unmounted the editor with its draft. The editor stays mounted (`v-show`); the other two tabs still mount on demand.

### 11. Local changes during a background reload (`stores/catalog.ts`)

Valid. While a snapshot was shown or a forced reload ran, the load collected into its own map and replaced the catalog when it finished, so an annotation deleted, edited or created meanwhile came back or was lost, and was written into the IndexedDB snapshot. `upsert` and `remove` now also record the change while such a load runs, and the changes are replayed onto the fresh map before it is swapped in. A unit test deletes and creates during a gated load.

### 12. Self role change (`utils/roles.ts`, `MembersTable.vue`, mock)

Valid. `CosmosAccessService.SetMemberRoleAsync` rejects every change of the caller's own role with 409 `SelfRoleChange`, Owners included; the UI and the mock allowed it for Owners and the message spoke of Administrators. `memberRules` returns `canChange: false` for the caller in every role with the reason "You cannot change your own role"; `canRemove` stays what it was (an Owner who is not the last may leave). The mock answers 409 with the API's message.

### 13. SetAnnotation creates or replaces (`Api.Functions/src/V1/AnnotationsHttp.cs`)

Valid, and the root of the three annotation-write findings. `SetAnnotation` decided create or update with `request.Method == Definitions.Methods.Post`, where the constant is `"post"` and ASP.NET Core reports `"POST"`, so every request went to `UpdateAnnotationAsync` and creating a key always failed with "doesn't exist". Two further problems sat in the same method: the body was read a second time from `request.Body` after the `[FromBody]` binding, and it was deserialized with System.Text.Json options without a string enum converter, although `AnnotationType` carries only Newtonsoft's `StringEnumConverter`.

The handler now reads the body once as text, picks the runtime type from its `AnnotationType`, deserializes with `JsonStringEnumConverter`, overrides the key, project and view from the route, and updates when `ExistAnnotationAsync` is true, otherwise creates (which also creates the missing ancestors). POST and PUT behave the same; this is the upsert the UI, the mock and the OpenAPI-generated clients (POST only) assume, and it keeps every client working, unlike a case-insensitive method comparison, which would have turned the existing clients' edits into "already exist" errors. A body without the type-specific members (`SubjectKey`, `UnitType`, ...) is rejected with a 400 that names them, because the runtime types declare them `required`. New unit tests (`AnnotationsHttpTests`) cover create, update, the rejected body and an unknown type; `TestHttpRequestData` accepts a method and a body for that.

Not done here: deriving the relation members from the key on the server (the UI comment "relation fields are derived by the API" was false and is corrected); the OpenAPI document and the SDK were not regenerated (only the operation description changed); `SetAnnotationsSimple` still stores into the default project and view regardless of the route (`CreateAnnotationsFromStringAsync`), which is why the suggestion to create through it was not followed.

### 14. The form body keeps the type-specific members (`forms/annotation.schema.ts`, `AnnotationForm.vue`)

Valid. `toAnnotationBody` sent the 14 base fields only; the API replaces the whole document, so a usage, context, execution, unit or unit of execution failed to save and a subject or responsibility lost `ContextNames`, `ResponsibilityNames` or `UnitNames` (which the delete cascade iterates). The body is now the loaded annotation with the edited base fields over it. The JSON and YAML modes still edit the base fields only; the relation members are shown read-only in the References section as before.

### 15. New annotations carry their relation members (`AnnotationCreateDialog.vue`, `utils/annotation-relations.ts`, mock)

Valid. The dialog sent only the base fields. `withRelations` (moved from the mock into `utils/annotation-relations.ts`) derives the members from the key the way `CreateAnnotationsFromStringAsync` does, with the API's defaults `event` / `unknown` for a unit's `UnitType` and `UnitDefinition`, and the dialog sends them. The mock's single and bulk handlers now reject a body without the required members of its type, like the API, instead of storing it. Creation stays on `SetAnnotation`; see 13 for why `SetAnnotationsSimple` was not used.

### 16. Bulk edits go through SetAnnotation per key (`composables/useAnnotationActions.ts`, mock)

Valid. Enable, Disable, Add label and Remove label used `SetAnnotations`, whose backend (`CreateAnnotationsAsync`) only creates and silently skips every existing key with a 200, while the UI applied the change to the catalog and reported success. `saveMany` now updates each key with `SetAnnotation`, four at a time, upserts what the server returned and throws with the keys that failed. The mock's bulk handler skips existing keys and returns the created list. `chunk` stays as a helper (it is unit tested) but is no longer used by `saveMany`.

### 17. Create flow for a configuration without a stored version (`composables/useConfiguration.ts`, `ConfigurationEditor.vue`, mock)

Valid. `GetRawConfigurationAsync` answers 200 with `Version` 0 for an annotation without a stored entity, so `missing` (effective 404) never became true, the editor opened an empty draft and Save sent a PATCH, which `PatchConfigurationAsync` rejects with 404. `useConfiguration` keeps `missing` for "annotation not found" and adds `noOwn` (the own version answers 404); the editor shows "No configuration yet" with Create empty / Start from template for both, which create the first content with POST. The mock now answers 404 only for an unknown annotation and otherwise returns `Version` 0 with the inherited content, like the API. A unit test opens an unconfigured execution and creates its first content.

### 18. Delete cascade (`AnnotationDetail.vue`, `AnnotationBulkBar.vue`, `useAnnotationActions.ts`, `utils/annotation-key.ts`)

Valid. `DeleteAnnotationAsync` deletes the whole partition of a responsibility, the usages, executions, units of execution and contexts of a subject, the executions and units of execution of a usage, the units of execution of a unit, and the executions of a context; an execution and a unit of execution go alone. The confirmation claimed the descendants merely lose an ancestor, the catalog kept them, and a bulk delete of a parent with its children reported the children's 404 as failures. `deletedWith(key, keys)` encodes that cascade (unit tested); the detail confirmation counts and names the descendants the API deletes, the bulk confirmation states the rule, the descendants leave the catalog with their parent, and a 404 counts as deleted. Noted, not changed: a context's delete leaves the units of execution of its executions in the database, and an execution's delete leaves its units of execution; the catalog keeps those, as the API still lists them.

### Already fixed: the SDK build in CI (`.github/workflows/ui-admin.yml`)

The first run reported that the workflow installed the SDK without building it, so the UI received a packed copy without `dist/`. Commit `8e93e7a` on the branch already builds the SDK (`npm ci && npm run build`) in the verify, e2e and deploy jobs before the UI's `npm ci`; the second review run did not repeat the finding. No change.
