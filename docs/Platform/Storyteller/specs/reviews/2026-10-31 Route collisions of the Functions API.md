# Route collisions of the Functions API

Linear: [424-128](https://linear.app/424fun/issue/424-128/bulk-create-annotations-in-admin-ui-fails-with-400-no-annotation). The same change fixes [424-131](https://linear.app/424fun/issue/424-131/modeling-deleted-fails-with-400-invalid-annotation-key-deleted).

## Overview

This review covers all four phases of [Route collisions of the Functions API](../2026-10-31%20Route%20collisions%20of%20the%20Functions%20API.md), delivered in one branch. The Functions host runs the first function, in the order of the function names, whose route matches, so a parameter took its literal siblings: `POST …/annotations/simple` ran `SetAnnotation`, `GET …/annotations/deleted` ran `GetAnnotation`, and `GET …/usage/key-values` ran `GetKeyValues`. Inline route constraints (`Definitions.Routes.Constraints`) now keep every pair of routes of one method apart, so the order decides nothing; the public URLs, the OpenAPI document and the SDKs are unchanged. Every `{key}` function answers an invalid key with `400 InvalidKey`, `RouteDispatchTests` models the host's dispatch, an opt-in project checks a running host, and the Admin UI mock answers the bulk create like the API. The review of the branch widened the fix to two crosswise overlaps that worked only by the alphabet (section 8). A local Functions host reproduced all three misroutes with the build of `main` and dispatched every request correctly with this branch (section 9).

| Commit | Content |
|---|---|
| `d9d7869b` | The spec |
| `d4823cdc` | Section 1 and 3–4: the constraints, the dispatch, route-table and OpenAPI tests |
| `a4b67515` | Section 2: `InvalidKey` in every `{key}` function |
| `7e458dee` | Section 5: the host smoke test project |
| `c49b7f8f` | Section 6: the Admin UI mock |
| `c4499475` | Section 7: the docs |
| `de54c8f7` | Fixes from the review: the crosswise overlaps, the key/value constraint on the list only |
| `82ecd161` | Fixes from the review: the mock skips null lines and names the JSON path |
| `7d43f428` | Fixes from the review: the docs name the constrained routes exactly |
| this commit | This review |

## What Was Done

### 1. Inline route constraints

`Api.Functions/src/Definitions.cs` has `Routes.Constraints` with a comment on why they exist (the host's order, azure-functions-host#9876) and what they must avoid (braces, slashes, `):` and `)=`):

| Constant | Text | Routes |
| --- | --- | --- |
| `AnnotationKey` | `:regex(\.)` | `{key}` of `Annotation`, `Descendants`, `History`; after the review also `SchemaAnnotation`, `SchemaDescendantType`, `SchemaCombined` and the version routes built on them |
| `Descendants` | `:regex(^(usages\|contexts\|executions\|units\|units-of-execution\|all)$)` | `{descendants}` of `Descendants` |
| `KeyValuesView` | `:regex(^(?!usage$))` | `{view}` of the new `KeyValues.V1.List`, the route of `GetKeyValues` |
| `Organization` | `:regex(^(?!access$))` | `{organization}` of `Views.V1.Views`, `Views.V1.View`, `Bindings.V1.Bindings` (added in the review) |

Deviations from the spec:

- The spec put `KeyValuesView` on the base `KeyValues`, which spreads it to every view-scoped key/value route. The review pointed out that `NameRules` checks only new names, so a view named `usage` from before the name was reserved can exist. The constraint now sits on a separate `List` constant used by `GetKeyValues` only, the one route that met the usage report; the other routes of such a view keep working. `RouteTableTests.KeyValueActions_ArePostOnly_AndKeyRoutesNeverPost` finds the action routes by the prefix `{KeyValues}/` instead of the length of the base, which the longer list route would have joined.
- The `Organization` constraint and the schema `{key}` constraint are new (section 8).
- `DeletedAnnotation` keeps an unconstrained `{key}`, as planned: `GET`/`DELETE …/deleted/{key}` meets no route of its method once `{key}` of `Descendants` needs a dot, and `lookup` and `restore` are `POST` only.

The generated `functions.metadata` carries the templates as written (`"route": "…/annotations/{key:regex(\\.)}"` in JSON), and the host lists them so at startup.

### 2. `InvalidKey` in every `{key}` function

`GetAnnotation`, both branches of `GetDescendants` and `DeleteAnnotation` return `InvalidKey(key)` (`AnnotationsHttp.Deleted.cs`), so all seven `{key}` functions answer `400` with `ErrorCode` `InvalidKey` and the same message before any backend call. The value switch of `GetDescendants` has a comment that ties it to `Constraints.Descendants`. `AnnotationsHttpTests` has `KeyFunctions_AnswerAnInvalidKeyWithInvalidKey` (a dotted key that doesn't parse, `boots.another-app`, for each function, and `nope` for `GetAnnotation`) and `Descendants_ReadsTheValuesOfTheRouteConstraint` (each of the six values with a fitting key type answers `200`, `history` answers the function's `400`).

### 3. Dispatch tests

New `Api.Functions/test/RouteDispatchTests.cs`. It reads every `[Function]` with an `HttpTrigger` route, orders them like the host (by name, ignoring case), parses each route with `TemplateParser`, matches with `TemplateMatcher` and evaluates the inline constraints resolved by the `IInlineConstraintResolver` of `AddRouting()`; a constraint that doesn't resolve throws.

- `Request_MatchesExactlyItsFunction`: 35 requests and the one function each must match, among them the issue's (`POST …/simple`, `GET …/deleted`, `GET …/usage/key-values`), keys like `uxe.a.b.c.d`, `SBT.Northwind`, `.sbt.northwind.`, `sbt..northwind`, names equal to literals (`sbt.deleted`, `rst.history`, `sbt.simple`), and after the review `GET v1/access/points/views`, `…/bindings`, `PUT v1/access/points/views/machine-authentication` and the schema routes.
- `Request_MatchesNoFunction`: the new `404`s (`GET`/`PUT …/simple`, `DELETE …/deleted`, `…/nope`, `…/{key}/bogus`, `…/{key}/subjects`).
- `EveryRoute_WithSampleValues_MatchesOnlyItsFunction`: each route with a sample value in every parameter (`sbt.sample` for a key, `usages` for descendants, `sample-{name}` otherwise) is matched by its own function only.
- `NoTwoRoutesOfOneMethod_TakeTheSameRequest` (added in the review): for every pair of routes that share a method and a length, the requests with the literals of both and a sample of either where both have a parameter. No pair may match the same request; before the review fixes it listed the 12 pairs of section 8.
- `DescendantsConstraint_TakesTheValuesOfGetDescendants`, `AnnotationKeyConstraint_TakesADottedKeyOnly`, `KeyValuesViewConstraint_RefusesTheUsageLiteral`.

`RouteTableTests.LiteralSegments_AreNotAnnotationKeys` has `simple` and asserts that no literal has a dot, with a corrected comment. The other route-table tests pass unchanged: the `{[^}]+}` normalization removes the constraints.

### 4. The OpenAPI paths

`OpenApiExportTests.Document_Paths_HaveNoRouteConstraints` asserts the paths of 11 operations (the annotation `{key}`, descendants, history, simple and deleted routes, the key/value list, a key and the usage report) and that no path contains `:` or `regex`. The extension's `RouteConstraintFilter` strips the constraints, so no filter of ours was needed. The document rendered from this branch equals `sdk.typescript/open.api.v0.10.11.json` except `info.version`; the SDKs are not regenerated.

### 5. The host smoke test

New `Api.Functions/testint/Api.Functions.IntegrationTests.csproj` (in `42.mono.slnx`; the traversal build picks it up), with no project reference. `HostTestSettings` reads `STORYTELLER_TEST_API_BASE_URL`, `STORYTELLER_TEST_TOKEN`, `STORYTELLER_TEST_ORGANIZATION`, `STORYTELLER_TEST_PROJECT` and optionally `STORYTELLER_TEST_VIEW`; without them each test writes "Skipped: …" and returns, as in `Mcp/testint`. `HostRoutingTests`:

- `BulkCreate_OfNoLines_ReachesSetAnnotationsSimple`: `POST …/simple` with `[]` answers `200 []`.
- `DeletedList_ReachesGetDeletedAnnotations`: `200` with `Annotations`.
- `UsageReport_ReachesGetKeyValueUsage`: `200` with `Days` and without `Items`, or `403` for a non-administrator.
- `KeyWithoutADot_ReachesNoFunction` (not in the spec): `GET …/annotations/nope` answers `404`.

### 6. The Admin UI mock

`ui.admin/src/app/mock/annotation-handlers.ts` reads the bulk body like the API's `ReadJsonBodyAsync<List<string>>`: a blank body or JSON `null` answers `400 "The annotations are missing."`, a body that is not a list or an entry that is neither a string nor `null` answers `400 "The request body is not a list of annotation keys or lines (at $…)."` with the JSON path, and `null` entries are skipped (the review found that the first version refused them and left out the path). The comments no longer say that literal segments win over `{key}` on the API. `bulk-create.test.ts` expects the `400`s and has a test for a `null` entry. The UI's status mapping is unchanged.

### 7. Documentation

- `annotation-lifecycle.md`: the false "literal segments win" sentence is replaced with the host's rule and the constraints, which `{key}` routes they cover (not `…/deleted/{key}`), the case-insensitive `{descendants}`, and the `404` of a key without a dot against the `400 InvalidKey` of a dotted one.
- `key-values.md`: the list's `{view}` is never `usage`, and what that means for a legacy view of that name.
- `definitions.md`: `{key}` of the schema routes needs a dot.
- `development.md`: how to run the host smoke test.

## The review of the branch

A review workflow read the diff with four lenses (routing correctness, clients and contracts, tests, docs) and had each finding checked by two independent verifiers. Confirmed and fixed:

1. The new OpenAPI test was inserted between the 424-79 comment and the test it describes; the comment is back above `Document_Scopes_AreAListOfTheSharedMachineScopeEnum`.
2. `annotation-lifecycle.md` stated the dot rule for every `{key}` and named `lookup` and `restore` as its siblings; it now names the constrained routes and says why `…/deleted/{key}` needs no constraint.
3. The mock refused `null` entries, which the API skips, and its messages lacked the JSON path.
4. This review file, which the spec defers two items to (section 8 and "Left for later").

Split, then acted on:

5. The key/value view constraint on the base constant (section 1).
6. Crosswise overlaps that the sample test cannot see. The new pair test listed 12, all from the spec's section 7:
   - `GET v1/access/points/views` and `…/bindings` (`GetAccessPoint` against `GetViews` and `GetBindingCatalog`) and `PUT v1/access/points/views/machine-authentication` (`PutMachineAuthentication` against `UpdateView`). They are valid requests for the access point of an organization named `views` or `bindings`, and reached it only because `GetAccessPoint` and `PutMachineAuthentication` sort first. The organization of the view and binding routes is now never `access`, a reserved organization name.
   - Nine pairs of `configuration-schema/type/{annotationType}…` against `configuration-schema/{key}/…` for the key `type`. `{key}` there is an annotation key, so it now needs a dot too.

   With both, the pair test asserts that no two routes of one method take the same request. The spec had left both groups for later; the cost was three constants and the tests.

Rejected by both verifiers: an OpenAPI `404` for the host's no-route answer (it is not an answer of the operation), `sform story get deleted` (the CLI validates keys elsewhere; out of scope), and the repeated value lists in the two descendants tests.

## Compared with the CodeRabbit coding plan

The plan proposed the same constraints, the dispatch test, an OpenAPI path check and an opt-in host test. This branch differs where the code says otherwise:

- No `{key}` function answered `500` for a key that doesn't parse; only the `ErrorCode` was missing in three of them.
- The SDK needs no camelCase fallback: the error bodies are PascalCase on both paths, and the dialog showing the server's text proves the body was `{"Message": …}` (the ticket's `message` is a transcription).
- The key/value usage route and the crosswise overlaps are included: the same defect, found by the inventory and the pair test.
- The `500` of the side panel was not traced to the `GET …/deleted` misroute: that misroute answered `400 "Invalid annotation key: deleted"` (424-131).

## Validation

- `Api.Functions.UnitTests`: 2680/2680.
- `Api.Functions.IntegrationTests` without variables: 4/4, skipped by the opt-in.
- Admin UI: `vitest` 1452/1452 before the review fixes and the bulk-create file (27 tests) after them; `npm run lint` and `vue-tsc --build` pass.
- The OpenAPI document equals the committed one except `info.version`.
- A local Functions host (Core Tools 4.15.2, runtime 4.1053.200) with Azurite and the Cosmos DB emulator (on port 18081, as 8081 is in a Windows excluded port range), a database from `DbCreator`, and an unsigned token that a Debug build decodes (`DEV_AUTH`):

  | Request | `main` (08afd3d9) | This branch |
  | --- | --- | --- |
  | `POST …/annotations/simple` `[]` | `SetAnnotation`: `400 "No annotation object in body."` | `SetAnnotationsSimple`: `200 []` |
  | `GET …/annotations/deleted` | `GetAnnotation`: `400 "Invalid annotation key: deleted"` | `GetDeletedAnnotations` |
  | `GET …/annotations/nope` | `GetAnnotation`: `400` | no function: `404` |
  | `GET v1/{o}/{p}/usage/key-values` | `GetKeyValues` | `GetKeyValueUsage` |

  The four `HostRoutingTests` pass against this branch and fail against `main`. Two sweeps, 19 requests before the review fixes and 15 after them (the annotation, deleted, history and descendants routes, the key/value list and usage report, views, bindings, the access points `views` and `bindings`, and the schema routes), reached the expected function every time, as the host's log of executed functions shows; the smoke tests passed again after the fixes. The issue's own bulk create (`rst.another-app`, `boots.another-app`) answered `200` with the two keys and the missing ancestor.

## Left for later

- The `500` toast and side panel of the issue. The side panel runs `GetAnnotation` of the selected Usage, which this change does not touch, and the body had no `Message`, which the API's middleware always writes. On the local host every `GET` of a Usage answered `200`. If it comes back, capture the request URL, the raw body and the Functions host log of that moment.
- The host smoke test runs only by hand. A CI job with the Aspire host would need a token for the deployment's provider.
- `key-values.md` says the Functions host keeps serving HTTP when `AzureWebJobsStorage` is missing locally. With Core Tools 4.15.2 the host shut down when Azurite was not running (the queue triggers' listeners); not checked further.
