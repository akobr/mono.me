# Route collisions of the Functions API

Linear: [424-128](https://linear.app/424fun/issue/424-128/bulk-create-annotations-in-admin-ui-fails-with-400-no-annotation) (bulk create answers `400 "No annotation object in body."`). The same cause is behind [424-131](https://linear.app/424fun/issue/424-131/modeling-deleted-fails-with-400-invalid-annotation-key-deleted) (`GET …/annotations/deleted` answers `400 "Invalid annotation key: deleted"`).

## Problem

Bulk create in the Admin UI sends `POST v1/{organization}/{project}/{view}/annotations/simple` with a JSON array of lines. Against the Azure Functions API it answers in 2 ms with `400 "No annotation object in body."`. That text belongs to `SetAnnotation` (`POST …/annotations/{key}`): the host ran `SetAnnotation` with `key = "simple"` instead of `SetAnnotationsSimple`.

The Functions host does not rank literal segments above parameters as ASP.NET Core endpoint routing does:

1. `FunctionMetadataManager.LoadFunctionMetadata` orders the functions by name (`OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)`), and `WebScriptHostHttpRoutesManager` maps their routes in that order into a `WebJobsRouteBuilder`.
2. `WebJobsRouteBuilder.Build()` puts them into a legacy `RouteCollection` without sorting, and `WebJobsRouter` takes the first route whose template and constraints (the HTTP method included) match.
3. At startup the host refuses only routes whose strings are equal (`ScriptHost.HttpRoutesConflict`); overlapping templates load without a warning.

The behaviour is known and kept for compatibility ([azure-functions-host#9876](https://github.com/Azure/azure-functions-host/issues/9876), "HttpTrigger's Route precedence is different from ASP.NET Core behavior", open). So a request goes to the first function, in name order, whose template matches:

| Request | Should run | Runs today | Why |
| --- | --- | --- | --- |
| `POST …/annotations/simple` | `SetAnnotationsSimple` | `SetAnnotation`, key `simple` | `SetAnnotation` < `SetAnnotationsSimple` |
| `GET …/annotations/deleted` | `GetDeletedAnnotations` | `GetAnnotation`, key `deleted` (424-131) | `GetAnnotation` < `GetDeletedAnnotations` |
| `GET v1/{organization}/{project}/usage/key-values` | `GetKeyValueUsage` | `GetKeyValues`, view `usage` | `GetKeyValues` < `GetKeyValueUsage` |
| `GET …/annotations/{key}/history` | `GetAnnotationHistory` | `GetAnnotationHistory` | correct only because `GetAnnotationHistory` < `GetDescendants` |
| `GET …/annotations/deleted/{key}` | `GetDeletedAnnotation` | `GetDeletedAnnotation` | correct only because `GetDeletedAnnotation` < `GetDescendants` |

The third row is the same defect in the key/value routes, found while inventorying every overlap: the usage report of the Admin UI (`UsageReport.vue` through `useKeyValueUsage`) and `sform` (`KeyValueAuditCommand`) get the key/value list of a view named `usage` (an empty page) instead of the report. The last two rows work by the alphabet; renaming a function would break them.

The tests did not see it: `RouteTableTests.HttpRoutes_AreUniquePerMethodAndShape` replaces every parameter with `{}`, so `annotations/{}` and `annotations/simple` are different shapes; the handler tests call the methods directly; the Admin UI mock (MSW) matches `annotations/simple` before `annotations/:key` and so follows the rule the host does not have.

The UI around the failure:

- The dialog shows the server's text of the `400` correctly. The bulk call is a direct SDK call with its own error state, so it never toasts.
- The toast and the side panel "Storyteller API returned 500 Internal Server Error." come from a different request: the side panel's `GetAnnotation` of the selected Usage (`useAnnotation`, `refetchOnWindowFocus`), whose `5xx` body had no `Message` (the SDK's fallback text). That request is not misrouted (a dotted key meets no literal route), and no code path of `GetAnnotation` that answers `500` without `Message` was found; the API's own middleware always writes `Message`. It is not reproduced and not part of this fix (see 7).
- The ticket quotes the body as `{"message": …}`; the dialog showing the text proves it was `{"Message": …}`, because the SDK reads `Message` only. The error bodies of the Functions API are PascalCase on both paths (MVC `JsonOptions` and the worker serializer).

## Current State

- `Api.Functions/src/Definitions.cs:71-94`, `Routes.Annotations.V1`: `Annotations`, `AnnotationsSimple` (`…/annotations/simple`), `Annotation` (`…/annotations/{key}`), `Descendants` (`…/annotations/{key}/{descendants}`), `History` (`…/annotations/{key}/history`), `Deleted`, `DeletedAnnotation` (`…/annotations/deleted/{key}`), `DeletedLookup`, `DeletedRestore`. The comment at `:81` says "Literal segments win over {key}", which is not true on the host.
- `Definitions.cs:173-202`, `Routes.KeyValues.V1`: `KeyValues` (`v1/{organization}/{project}/{view}/key-values`) is the base of every view-scoped key/value route; `Usage` is `v1/{organization}/{project}/usage/key-values`. No route of the API has an inline constraint.
- `Api.Functions/src/V1/AnnotationsHttp.cs`: `GetAnnotation` (`:120`), `GetDescendants` (`:182`, accepted values `usages`, `contexts`, `executions`, `units`, `units-of-execution`, `all` at `:261-270`), `SetAnnotation` (`:309`, POST and PUT; refuses a non-object body at `:334-337` before it parses the key), `SetAnnotationsSimple` (`:494`), `DeleteAnnotation` (`:539`). `AnnotationsHttp.Deleted.cs`: `GetDeletedAnnotations`, `GetDeletedAnnotation`, `FindDeletedAnnotations`, `RestoreAnnotations`, `PurgeAnnotation`, `GetAnnotationHistory`, and the helper `InvalidKey(key)` (`:240-243`, `400` with `ErrorCode` `InvalidKey`).
- Every `{key}` handler validates with `AnnotationKey.TryParse` and answers `400`; an unparseable key never answers `500`. `GetAnnotation` (`:134`), both branches of `GetDescendants` (`:198`, `:258`) and `DeleteAnnotation` (`:551`) leave out the `ErrorCode`; the other four set `InvalidKey`.
- `Abstractions.Annotations/src/AnnotationKey.cs:70-89`: `TryParse` splits on `.` (2 to 5 segments, a type code of `rst`, `sbt`, `usg`, `cnt`, `exe`, `unt`, `uxe`, any case). Every valid key contains a `.`; `simple`, `deleted`, `lookup`, `restore` and `history` never parse. Empty segments are tolerated (`sbt..payments`).
- `Backend.Core/src/NameRules.cs`: names of organizations, projects and views match `^[a-z0-9][a-z0-9-]{1,62}\z`; `usage` is a reserved project and view name.
- `Api.Functions/test/RouteTableTests.cs`: reflects the `HttpTrigger` routes, normalizes parameters with `{[^}]+}`, checks registrations, that every route parses (`RoutePatternFactory.Parse`), and `LiteralSegments_AreNotAnnotationKeys` (`deleted`, `lookup`, `restore`, `history`; `simple` is missing).
- `Api.Functions/test/OpenApiExportTests.cs`: renders the document in-process (`RenderAsync`) with the extension's own context. The extension strips inline constraints per path segment (`RouteConstraintFilter`, pattern `^(\{)([^\{\:]+)(\:.+)?(\})$`).
- `ui.admin/src/app/mock/annotation-handlers.ts:547-561`: the mock's bulk endpoint answers `500 "Object reference not set to an instance of an object."` for a body that is not a list, which the API answers with `400`; `ui.admin/src/test/unit/annotations/bulk-create.test.ts:129-137` asserts the `500`.
- `docs/Platform/Storyteller/annotation-lifecycle.md:173` repeats the false claim that the literal segments win over `{key}`.
- No test or project runs requests against a Functions host.

## Proposed Changes

### 1. Inline route constraints make same-method templates exclusive

The Functions host resolves inline constraints with ASP.NET Core's `DefaultInlineConstraintResolver` (the Functions docs: "You can use any ASP.NET Core Route Constraint with your parameters"), and `regex` is registered in the host (it calls `AddMvc`, so `AddRouting`). A constraint that rejects the literal siblings makes the templates exclusive, so the order of the host cannot decide anything. The public URLs, the parameter names, the handler bindings, the OpenAPI paths and the SDKs stay as they are.

New in `Definitions.Routes`:

```csharp
// Inline constraints of route parameters. The Functions host tries the routes in the order of the function names and
// takes the first that matches; it doesn't rank a literal segment above a parameter like ASP.NET Core does. Routes of
// one method must therefore exclude each other, and these constraints keep a parameter off its literal siblings.
// Keep them free of braces and slashes (the template syntax and the OpenAPI path filter).
public static class Constraints
{
    // An annotation key always has a '.' after its type code; the literal segments beside {key} have none.
    public const string AnnotationKey = ":regex(\\.)";

    // The values GetDescendants accepts (AnnotationsHttp.GetDescendants); keep both lists the same.
    public const string Descendants = ":regex(^(usages|contexts|executions|units|units-of-execution|all)$)";

    // "usage" is a reserved view name, the literal of the project's key/value usage report.
    public const string KeyValuesView = ":regex(^(?!usage$))";
}
```

Applied to:

| Constant | New template (the constrained part) |
| --- | --- |
| `Annotations.V1.Annotation` | `…/annotations/{key:regex(\.)}` |
| `Annotations.V1.Descendants` | `…/annotations/{key:regex(\.)}/{descendants:regex(^(usages\|…\|all)$)}` |
| `Annotations.V1.History` | `…/annotations/{key:regex(\.)}/history` |
| `KeyValues.V1.KeyValues` (and every route built on it) | `v1/{organization}/{project}/{view:regex(^(?!usage$))}/key-values…` |

Notes:

- The key constraint is unanchored, so it only asks for a `.` somewhere; `AnnotationKey.TryParse` stays the semantic check in the handlers, and a dotted but invalid key (`boots.another-app`) still reaches the handler and its `400`.
- `DeletedAnnotation` (`deleted/{key}`) needs no constraint: after the change no other route of its methods and length can match it (`{key}` of `Descendants` refuses `deleted`).
- The key/value constraint sits on the base constant, so the whole view-scoped family states the same rule; only the list route meets `usage/key-values`, and a view named `usage` cannot exist.
- `RegexRouteConstraint` matches case-insensitively, so `USAGES` passes the constraint and still gets the handler's `400`, as today.
- The constraints use no braces (the `{[^}]+}` normalization of `RouteTableTests` stays valid), no slashes, and no `(?:` or `(?=` (a `)` before `:` or `=` ends a constraint in the template parser).
- The comment at `Definitions.cs:81` is replaced with the reason for the constraints.

**Behaviour change.** A request that no longer matches any route gets the host's `404` without a body instead of the handler's `400`: a key without a dot (`GET …/annotations/nope`, `PUT …/annotations/simple`) and an unknown descendants value (`GET …/annotations/sbt.x/bogus`). Clients send keys with a type code, so only hand-written requests see it. Documented in `annotation-lifecycle.md`.

### 2. The handlers answer an invalid key the same way

`GetAnnotation`, `GetDescendants` (both branches) and `DeleteAnnotation` use `InvalidKey(key)`, so every `{key}` handler answers an unparseable key with `400` and `ErrorCode` `InvalidKey`, before any backend call. The message stays `Invalid annotation key: {key}`.

### 3. Dispatch tests that model the host

New `Api.Functions/test/RouteDispatchTests.cs`:

- Collects every function of the assembly (`[Function]` name, `HttpTrigger` methods and route).
- Parses each route with `TemplateParser`, matches paths with `TemplateMatcher`, and evaluates the inline constraints resolved by the `IInlineConstraintResolver` of `new ServiceCollection().AddRouting()` (a bare `RouteOptions` maps `regex` to a stub that throws), as the host's `Route` does. A constraint name that does not resolve fails the test.
- `[Theory]` of requests and the one function that must match, with exactly one match (so no host order can change the result): `POST …/annotations/simple`, `POST …/annotations`, `GET …/annotations`, `POST`/`PUT`/`GET`/`DELETE …/annotations/{key}`, `GET …/annotations/deleted`, `GET`/`DELETE …/annotations/deleted/{key}`, `POST …/annotations/deleted/lookup` and `…/restore`, `GET …/annotations/{key}/history`, `GET …/annotations/{key}/usages`, `…/all`, `…/units-of-execution`, `GET …/usage/key-values`, `GET …/default/key-values`. The keys include `uxe.a.b.c.d`, mixed-case type codes, leading, trailing and doubled dots, and names equal to the literals (`sbt.deleted`, `rst.history`, `sbt.simple`).
- `[Theory]` of requests no function serves after the change (the `404` of 1).
- `[Fact]`: every route, with a sample value for each parameter (`sbt.sample` for a key, `usages` for descendants, `default` for a view, …), is matched by its own function only. This finds any future literal sibling that a parameter of the same method would capture.
- `[Theory]`: the descendants constraint accepts exactly the six values of `GetDescendants` and refuses `history`.

`RouteTableTests.LiteralSegments_AreNotAnnotationKeys` gets `simple` and asserts that each literal has no `.` (what the constraint checks), with a corrected comment. The other `RouteTableTests` stay valid: the normalization removes the constraints.

### 4. The OpenAPI paths stay plain

New test in `OpenApiExportTests`: the rendered document has `/v1/{organization}/{project}/{view}/annotations/{key}`, `…/{key}/{descendants}`, `…/{key}/history`, `…/annotations/simple`, `…/annotations/deleted` and `/v1/{organization}/{project}/{view}/key-values` for their operations, and no path contains `:` or `regex`. The extension's `RouteConstraintFilter` removes the constraints already, so no filter of our own and no regeneration of the SDKs are expected; if the test shows otherwise, a document filter that strips inline constraints from the path keys is added.

### 5. A smoke test against a running Functions host

New project `Api.Functions/testint/Api.Functions.IntegrationTests.csproj` (in `42.mono.slnx`), with plain `HttpClient` calls and no project reference, following the opt-in pattern of `Mcp/testint` (without its variables a test writes "Skipped: …" and returns):

- `STORYTELLER_TEST_API_BASE_URL` (with `/api`, for example `http://localhost:7071/api`), `STORYTELLER_TEST_TOKEN` (a bearer token of a user who is a Contributor of the project), `STORYTELLER_TEST_ORGANIZATION`, `STORYTELLER_TEST_PROJECT`, and optionally `STORYTELLER_TEST_VIEW` (`default`).
- `POST …/annotations/simple` with `[]` answers `200` and a JSON array (no side effects); `GET …/annotations/deleted` answers `200` and an `AnnotationsResponse`; `GET …/usage/key-values` answers the usage report (`200` with `Days`) or `403` for a non-administrator, never the key/value list.
- The class comment gives the run command and the local start (`dotnet run --project src/Platform/Aspire.Host/src`).

### 6. The Admin UI mock answers like the API

`annotation-handlers.ts`: a missing or blank body answers `400 "The annotations are missing."`, and a body that is not a list of strings answers `400 "The request body is not a list of annotation keys or lines."` (the API's texts). The comment about the literal segment is corrected. `bulk-create.test.ts` expects `400`. The UI's status mapping (`api-errors.ts`, `utils/errors.ts`) does not change: it already keeps the real status.

### 7. Not changed

- `Api.Web`: endpoint routing ranks literals above parameters there; its `SetSimpleAnnotation` is a stub.
- The SDK's error reading: the bodies are PascalCase, so a camelCase fallback is not needed.
- The other overlaps of the inventory are reachable only with input that is invalid anyway: the `configuration-schema/type/{annotationType}…` routes against `configuration-schema/{key}/…` (a key `type` and an annotation type `versions`, `type` or `definition`), and `PUT v1/access/points/views/machine-authentication` against `UpdateView`. `GET v1/access/points/views` and `…/bindings` (the access point of an organization named `views` or `bindings`) reach `GetAccessPoint` by the alphabet; they are recorded in the review as a follow-up.
- The `500` of the side panel: not reproduced; the review lists what to capture (URL, body, host log).

### Phases

1. Constraints, handler codes, dispatch, route-table and OpenAPI tests (1–4).
2. The host smoke test project (5).
3. The Admin UI mock (6).
4. Documentation: `annotation-lifecycle.md` (the routing rule and the `404`s) and `key-values.md` where the routes are listed.

### Inputs considered

The CodeRabbit coding plan on the issue proposed the same constraints and tests. This spec differs where the code says otherwise: no handler returned `500` for an unparseable key (only the `ErrorCode` was missing), the SDK needs no camelCase fallback, and the key/value usage route is included because it is the same defect on a valid request.
