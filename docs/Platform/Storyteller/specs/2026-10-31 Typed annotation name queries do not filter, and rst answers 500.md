# Typed annotation name queries do not filter, and rst answers 500

Linear: [424-97](https://linear.app/424fun/issue/424-97/typed-annotation-name-queries-do-not-filter-and-rst-answers-500). Found while building the MCP server (424-84, review `reviews/2026-10-24 MCP Server for the Storyteller API.md`, "Left for later").

## Problem

The typed list routes of the API (`GET …/responsibilities`, `…/subjects`, `…/usages`, `…/contexts`, `…/executions`, `…/units`, `…/units-of-execution`) take name queries: `nameQuery`, `responsibilityNameQuery`, `subjectNameQuery`, `contextNameQuery` and `unitNameQuery`. A query is exact, `%contains%`, or a regular expression that starts with `^` or ends with `$`. Almost none of them filter:

1. The routes build `AnnotationsRequest.Condition<TModel>` over the **model** types (`Responsibility`, `Usage`, …) and never set `Against`, which stays `AnnotationType.Responsibility` (its default).
2. `CosmosAnnotationService` keeps only the conditions whose `Against` is the listed type. For every type but `rst` all conditions are dropped, so the filter is ignored. Only an exact query that also sets `PartitionKey` narrows anything, and only to a partition.
3. For `rst`, the kept conditions go through `.Cast<AnnotationsRequest.Condition<ResponsibilityEntity>>()`, but they are `Condition<Responsibility>`. The cast throws `InvalidCastException`, so every name query on `…/responsibilities` answers `500`.
4. The regular expressions are built with `new Regex(query, RegexOptions.Compiled)`, without a match timeout. Once the conditions run, a pattern like `^([a-zA-Z0-9_-]+)+!` costs seconds of CPU per name (catastrophic backtracking, ReDoS). An invalid pattern throws `ArgumentException` in the route, which answers `500`.
5. A `%…%` query is compared with the `%` markers still in the text (`Name.Contains("%foo%")`), so it matches nothing.

## Current State

- `Backend.Core/src/Annotating/AnnotationsRequest.cs:36-46`: `ICondition { AnnotationType Against { get; } }` and `Condition<TEntity> : ICondition` with a settable `Against` (default `Responsibility`) and `Expression<Func<TEntity, bool>> Predicate`. No caller sets `Against`; every caller uses a model type as `TEntity`.
- `Backend.CosmosDb/src/Annotating/CosmosAnnotationService.cs:630-771`, `GetAnnotationsAsync<TAnnotation, TEntity>`:
  - `:653-655` selects `Conditions.Where(b => b.Against == annotationType).Cast<AnnotationsRequest.Condition<TEntity>>()`.
  - `:732-735` adds each `Predicate` to the Cosmos LINQ query (`matches.Where(by.Predicate)`) before `ToFeedIterator()`.
  - `:758-763` filters the page in memory (descendants of `DescendantsOf`, validity window), then maps with `ToModel<TEntity, TAnnotation>()`. `TotalCount` counts the mapped items; the continuation comes from the raw feed page.
- `Api.Functions/src/V1/AnnotationsHttp.cs:568-1323`: the seven typed routes. Each name parameter repeats the same three branches inline: regex (`^`/`$`, `new Regex(q, RegexOptions.Compiled)`), contains (`%`, markers kept), exact. Exact queries set the partition: `PartitionKeys.GetResponsibility(project, q)` for `nameQuery` of responsibilities and `responsibilityNameQuery` of usages, executions, units and units of execution; `PartitionKeys.GetSubject(project, q)` for `nameQuery` of subjects and `subjectNameQuery` of contexts. The exact `subjectNameQuery` of usages compares `SubjectKey == AnnotationKey.CreateSubject(q)`; the `contextNameQuery` of executions compares `Name` (an execution's name is its context name).
- `Api.Web/src/Controllers/AnnotationsController.cs:58-413`: the legacy ASP.NET Core API builds the same model-typed conditions (`nameQuery` of responsibilities and subjects as a plain `Contains`, the three branches for the other parameters) and the mixed-type conditions of `GetDescendants` (`:488-555`). Api.Web has no exception mapping, so every exception answers `500`.
- `Backend.CosmosDb/src/PartitionKeys.cs`: `GetResponsibility` and `GetSubject`.
- `Abstractions.Annotations/src/AnnotationTypes.cs:33-46`: `AnnotationTypes.GetType(Type)` maps a model type to its `AnnotationType`.
- `Backend.Core/src/ErrorCodes.cs:76`: `InvalidQuery` exists. `Api.Functions/src/ErrorHandling/ErrorResponseMapping.cs:20` maps every `InvalidInputException` to `400` with its code; `ArgumentException` is not mapped (`500`).
- `Mcp/src/Tools/AnnotationTools.cs:520-551`: the MCP `find_annotations` filters in the host with a regex of 100 ms timeout because the API's name queries do not work. `docs/Platform/Storyteller/mcp.md:296` says so.
- `Api.Functions/test/AnnotationsHttpTests.cs`: `RecordingAnnotations` records the request and answers an empty page; `Lists(http)` calls every list route without name queries. Nothing tests a name query.
- Docs: `docs/Platform/Storyteller/annotation-lifecycle.md:46-49` (lists and paging) and `docs/42for.net/platform/using-the-platform.md:65-80` (the typed reads) mention the name filters without their syntax.

## Proposed Changes

### 1. The condition contract: the model type is the discriminator

In `Backend.Core/src/Annotating/AnnotationsRequest.cs`:

- Rename the type parameter of `Condition<TEntity>` to `TAnnotation` and constrain it: `where TAnnotation : Annotation`. Every caller already uses model types, and the constraint makes the entity types impossible.
- `Against` becomes read-only and derived: `public AnnotationType Against => AnnotationTypes.GetType(typeof(TAnnotation));`. `ICondition.Against` stays for readers. (Checked with a search: no caller assigns `Against`.)
- Add the evaluator to `AnnotationsRequest`:

  ```csharp
  // The conditions of one annotation type, compiled once and combined with AND; conditions of other types don't apply.
  public Func<TAnnotation, bool> CompileConditions<TAnnotation>()
      where TAnnotation : Annotation
  ```

  It selects `Conditions.OfType<Condition<TAnnotation>>()`, compiles each `Predicate` once, and returns a pass-through predicate without conditions, the predicate itself for one, and their conjunction for more. A request may mix types (the descendants of Api.Web): each condition applies to its own type only.

### 2. The Cosmos service applies the conditions in memory

In `CosmosAnnotationService.GetAnnotationsAsync<TAnnotation, TEntity>`:

- Remove the selection by `Against` and the cast (`:653-655`) and the `foreach` that adds the predicates to the Cosmos query (`:732-735`).
- Get `model.Request.CompileConditions<TAnnotation>()` once per type and apply it after `.Select(entity => entity.ToModel<TEntity, TAnnotation>())`, before `.ToList()`.

Why in memory and not in Cosmos DB: the conditions are over models, the regular expressions cannot be translated to Cosmos SQL, and rewriting model expressions into entity expressions would be a second, fragile contract. The page already filters in memory (descendants, validity window), so a name query behaves like those filters: the server-side filters, the `PartitionKey` option, one `ReadNextAsync` per page, `TotalCount` of the returned items and the continuation of the raw page stay as they are. A filtered page can hold fewer items than the page size, or none, and still have a continuation token, as the lifecycle documentation already says. An exact query on a partition parameter still narrows the read to one partition.

The callers of Api.Web start to filter too, including the mixed-type descendant conditions that were dropped until now. `DbCreator` sets no conditions.

### 3. `NameQuery`: one parser of the name-query syntax, bounded

New `Backend.Core/src/Annotating/NameQuery.cs` (namespace `_42.Platform.Storyteller.Annotating`):

```csharp
public enum NameQueryKind { Exact, Contains, Pattern }

public sealed class NameQuery
{
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    public string Parameter { get; }      // the query parameter, for messages
    public NameQueryKind Kind { get; }
    public string Value { get; }          // the exact name, the contained text, or the pattern
    public bool IsExact { get; }

    // null for null, empty or white space text
    public static NameQuery? Parse(string? text, string parameter);

    public bool IsMatch(string? name);

    // The condition "name matches", over the given name of the model, for example u => u.ResponsibilityName.
    public AnnotationsRequest.Condition<TAnnotation> ToCondition<TAnnotation>(Expression<Func<TAnnotation, string?>> name)
        where TAnnotation : Annotation;
}
```

Rules of `Parse` (the text is trimmed first):

| Text | Kind | Match |
| --- | --- | --- |
| starts with `^` or ends with `$` | `Pattern` | .NET regular expression, case-sensitive unless it uses `(?i)` |
| otherwise starts or ends with `%` | `Contains` | the text without its leading and trailing `%`, ordinal `Contains`; `%` alone matches every name |
| otherwise | `Exact` | ordinal, case-sensitive equality |

The pattern check comes first, as today, so `%a$` is a pattern. Exact stays case-sensitive because the partition it narrows to is built from the same text.

**Bounding the patterns.** The regex is built with `RegexOptions.NonBacktracking | RegexOptions.CultureInvariant` and a `MatchTimeout` of 50 ms, without `RegexOptions.Compiled`. A match timeout alone bounds one match, not the request: one page maps up to 1000 annotations, so a pattern tuned to stay just under the timeout would still cost up to 50 s of CPU per request. The non-backtracking engine matches in time linear in the length of the name, so the ReDoS patterns of the issue take microseconds; the timeout stays as a second bound. The engine does not support backreferences, lookarounds, atomic groups, conditionals and `RegexOptions.RightToLeft`; a name filter does not need them, and such a pattern is refused with `400`. (The MCP host keeps its own backtracking filter with a 100 ms timeout; its single regex runs over the names of a page the host already holds.)

Errors, all `InvalidInputException` with `ErrorCodes.InvalidQuery`, which the Functions API answers with `400`:

- `Parse`: an invalid pattern (`RegexParseException`, an `ArgumentException`) → `The {parameter} '{text}' is not a valid regular expression: {error} at offset {offset}.`; a construct the engine does not support (`NotSupportedException`) → `The {parameter} '{text}' uses a construct name queries don't support (backreferences, lookarounds, atomic groups or conditionals).`
- `IsMatch`: `RegexMatchTimeoutException` → `The {parameter} took too long to match a name; simplify the pattern.`

No general mapping of `ArgumentException` or `RegexMatchTimeoutException` is added to `ErrorResponseMapping`: the conversion stays where the query is parsed and matched.

`ToCondition` composes `name` into `annotation => query.IsMatch(<name>)` (`Expression.Call` on the query constant with the body of `name`), so the predicate reads like the inline ones. `IsMatch(null)` is `false`.

### 4. The Functions routes use `NameQuery`

In `Api.Functions/src/V1/AnnotationsHttp.cs`, every name parameter of the seven typed routes becomes:

```csharp
var responsibility = NameQuery.Parse(responsibilityNameQuery, "responsibilityNameQuery");

if (responsibility is not null)
{
    conditions.Add(responsibility.ToCondition<Usage>(u => u.ResponsibilityName));

    if (responsibility.IsExact)
    {
        dataRequest.PartitionKey = PartitionKeys.GetResponsibility(project, responsibility.Value);
    }
}
```

through a private helper of the class, `AddNameQuery<TAnnotation>(conditions, text, parameter, name)`, which answers the parsed query (or `null`) so that the route sets the partition of an exact query. The matrix stays:

| Route | Parameter | Property | Exact query narrows to |
| --- | --- | --- | --- |
| responsibilities | `nameQuery` | `Name` | `GetResponsibility(project, q)` |
| subjects | `nameQuery` | `Name` | `GetSubject(project, q)` |
| usages | `responsibilityNameQuery` | `ResponsibilityName` | `GetResponsibility(project, q)` |
| usages | `subjectNameQuery` | `SubjectName` | — |
| contexts | `subjectNameQuery` | `SubjectName` | `GetSubject(project, q)` |
| contexts | `nameQuery` | `Name` | — |
| executions | `responsibilityNameQuery` | `ResponsibilityName` | `GetResponsibility(project, q)` |
| executions | `subjectNameQuery` | `SubjectName` | — |
| executions | `contextNameQuery` | `Name` (the context name) | — |
| units | `responsibilityNameQuery` | `ResponsibilityName` | `GetResponsibility(project, q)` |
| units of execution | `responsibilityNameQuery` | `ResponsibilityName` | `GetResponsibility(project, q)` |
| units of execution | `unitNameQuery` | `UnitName` | — |
| units of execution | `subjectNameQuery` | `SubjectName` | — |
| units of execution | `contextNameQuery` | `ContextName` | — |

The exact `subjectNameQuery` of usages compares `SubjectName` like every other exact query instead of `SubjectKey == AnnotationKey.CreateSubject(q)`: the subject name is the second segment of the subject key, so both are equal for every stored usage, and the route loses its only special case. An exact query keeps its predicate next to the partition, because the partition holds other names too (a responsibility partition holds its units, usages and executions).

The routes parse before they call the service, so an invalid pattern answers `400` without a read. They add no local `catch`: `ExceptionHandlingMiddleware` maps `InvalidInputException`. The `using System.Text.RegularExpressions;` of the file goes if nothing else needs it. The OpenAPI document does not change (the routes already declare `400`).

### 5. Api.Web uses `NameQuery` too

Once the service applies the conditions, Api.Web's unbounded regexes would start to run, so `AnnotationsController` switches to `NameQuery` with the same matrix: responsibilities and subjects (`nameQuery`, which was a plain `Contains` and now follows the shared syntax with the partition of an exact query), usages, contexts (`subjectNameQuery`, `contextNameQuery`) and executions. The mixed-type conditions of `GetDescendants` stay. Api.Web has no exception mapping, so an invalid pattern still answers `500` there, as before; adding one is outside this change. The duplicate `using System.Text.Json;` goes.

### 6. Tests

`Backend.Core/test/` (xUnit, Shouldly):

- `NameQueryTests`: null, empty and white space give no query; the text is trimmed; exact queries match only the same name, case-sensitive; `%foo%`, `%foo` and `foo%` strip the markers; `%` matches every name; `^…` and `…$` patterns match and don't; `%a$` is a pattern; `(?i)` ignores case; `^[` and a backreference answer `InvalidInputException` with `InvalidQuery` and the parameter in the message; `^([a-zA-Z0-9_-]+)+!` against a 30-character name (and against a 1000-character one) answers `false` within a second; `IsMatch(null)` is `false`; `ToCondition` filters models.
- `AnnotationsRequestConditionsTests`: `Against` is the type of the type parameter for all seven types; without conditions everything passes; a `Condition<Subject>` applies to subjects and not to responsibilities; mixed-type requests apply each condition to its own type; two conditions of one type combine with AND.

`Api.Functions/test/AnnotationsHttpTests.cs` (Shouldly):

- `RecordingAnnotations` gets `Listed`, a seeded catalog of all seven types; `GetAnnotationsAsync` answers the listed annotations of the requested types filtered through `CompileConditions<T>()`, as the Cosmos service does. Without seeds it answers an empty page, as before.
- A theory over every row of the matrix in section 4, each with an exact, a contains and a pattern query: the returned names, and the recorded `PartitionKey` (narrowed for exact queries on partition parameters, absent otherwise).
- `rst` with each kind of query returns the filtered responsibilities (the `500` regression).
- Conjunctions on usages, executions and units of execution: one name query matches, the other does not, and nothing is returned.
- An invalid pattern on every name parameter of every route throws `InvalidInputException` with `InvalidQuery`, and the service is not called. The ReDoS pattern returns no match without delay.
- `ErrorResponseMappingTests`: a row for `InvalidInputException` with `InvalidQuery` → `400`.

`Backend.CosmosDb/test/` (FluentAssertions): `CosmosAnnotationNameQueryTests` lists responsibilities with a `Condition<Responsibility>` pattern, usages with a `Condition<Usage>` contains query and contexts with an exact `Condition<Context>`, against the emulator, and asserts the filtered results (no `InvalidCastException`). It runs only with `STORYTELLER_TESTS_USE_CONTAINER=true` or a disposable emulator: the fixture deletes the databases of the emulator it uses.

### 7. Documentation

- `docs/Platform/Storyteller/annotation-lifecycle.md`: a **Name queries** bullet next to **Lists** and **Paging**: the parameters of each list, the three forms, case sensitivity, `%` stripped, the non-backtracking engine with its unsupported constructs and the 50 ms timeout, `400 InvalidQuery`, the partition narrowing of exact queries, and that filtered pages can be short or empty and still have a token.
- `docs/42for.net/platform/using-the-platform.md`: the sentence on name filters gets the three forms and a link to the lifecycle page.
- `docs/Platform/Storyteller/mcp.md:296`: the reason the MCP tool keeps its own filter changes from "they filter almost nothing today" to the semantic difference (the tool's exact names ignore case and it filters by any name in the key).
- Historical specs and reviews under `docs/**/specs/` are not edited (CLAUDE.md). The MCP review's "Left for later" item is closed by the review of this change instead.

### Out of scope

- `Mcp/src/Tools/AnnotationTools.cs` keeps filtering in the host; moving `find_annotations` to the API's name queries is a follow-up.
- Translating exact and contains queries into Cosmos SQL to avoid short pages.
- An exception mapping for Api.Web.
