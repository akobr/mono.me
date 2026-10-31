# Typed annotation name queries do not filter, and rst answers 500

Linear: [424-97](https://linear.app/424fun/issue/424-97/typed-annotation-name-queries-do-not-filter-and-rst-answers-500).

## Overview

This review covers all of [Typed annotation name queries do not filter, and rst answers 500](../2026-10-31%20Typed%20annotation%20name%20queries%20do%20not%20filter%2C%20and%20rst%20answers%20500.md), delivered in one branch.

- The name queries of the seven typed lists filter now. Before, they were dropped for every type but `rst`, and `rst` answered `500`.
- The model type of a condition decides which annotations it applies to. The Cosmos service applies the conditions to the mapped page.
- One `NameQuery` parses every name query. It strips the `%` markers. Its patterns run on the non-backtracking engine and are bounded three ways: a length cap, a match timeout, and a matching budget per request. Every refusal is `400 InvalidQuery`.

The review of the branch found that the non-backtracking engine alone does not bound a page: one short pattern cost about 5 s of CPU per page with no timeout. The budget and the length cap were added for that (section 3). This review also closes the "Typed annotation name queries" item under "Left for later" in [the MCP review](2026-10-24%20MCP%20Server%20for%20the%20Storyteller%20API.md).

| Commit | Content |
|---|---|
| `7021adf6` | The spec |
| `d007969c` | Sections 1 and 2: the condition contract, `CompileConditions`, the Cosmos service; Backend.Core and Cosmos tests |
| `15ebf2d1` | Sections 3 to 6: `NameQuery`, the Functions routes, Api.Web; parser and route tests |
| `64e3627a` | Section 7: the docs |
| `acb70f5f` | Fixes from the review: the matching budget and the length cap, the engine's message for a too large pattern, a stronger multi-type Cosmos test, doc wording, the MCP comments |

## What Was Done

### 1. The condition contract

`Backend.Core/src/Annotating/AnnotationsRequest.cs` follows the spec.

- `Condition<TAnnotation>` is constrained to `Annotation`.
- `Against` is `AnnotationTypes.GetType(typeof(TAnnotation))`, read-only. No caller assigned it.
- `CompileConditions<TAnnotation>()` selects `Conditions.OfType<Condition<TAnnotation>>()` and compiles each predicate once. It returns a static pass-through without conditions, the single predicate for one, and `TrueForAll` over the list for more.

The method sits between `ICondition` and `Condition<T>`. In that place it adds no StyleCop warning. The file's existing warning, an interface after properties, stays.

### 2. The Cosmos service

`CosmosAnnotationService.GetAnnotationsAsync<TAnnotation, TEntity>` changes in three places:

- the selection by `Against` and the cast to `Condition<TEntity>` are gone;
- so is the loop that added the predicates to the Cosmos query;
- `CompileConditions<TAnnotation>()` is taken once per type (`:655`) and applied after `ToModel` (`:758`).

Paging, `TotalCount`, the continuation, the partition and the server-side filters are unchanged. An `InvalidInputException` raised while the page is filtered (a timeout or the budget) leaves the service as it is, and the middleware answers `400`.

### 3. `NameQuery`

`Backend.Core/src/Annotating/NameQuery.cs` and `NameQueryKind.cs` (one type per file).

- `Parse(text, parameter)`, `IsMatch(name)`, `ToCondition<T>(name)`, `Kind`, `Value`, `IsExact` and `Parameter` follow the spec. `ToCondition` composes `query.IsMatch(<name>)` into the selector's expression.
- **Bounds.** Three bounds hold together; the first and the last come from the review:
  - `PatternMaxLength` = 256 characters, which bounds the construction;
  - `MatchTimeout` = 50 ms per match;
  - `MatchBudget` = 250 ms of matching per query. Each pattern match adds its `Stopwatch` time to the query, with `Interlocked`, and the query throws once the total passes the budget. A query is parsed per request, so the budget covers one request. Names have at most 63 characters (`NameRules.Pattern`), so ordinary patterns use microseconds of it per page.
- **Messages**, all `InvalidInputException` with `InvalidQuery`:
  - `The {parameter} is not a valid regular expression: {RegexParseException.Message}`. The spec planned `'{text}' … {error} at offset {offset}`. The engine's message already quotes the pattern and gives the offset and the reason.
  - `The {parameter} '{query}' is not supported by name queries: {NotSupportedException.Message}`. The spec planned a fixed list of constructs. The engine also refuses, with the same exception, a pattern whose automaton would pass 10,000 nodes (`^.{0,2000}$`), and its message says which case it is.
  - `The {parameter} pattern has {n} characters, at most 256 are allowed.` (new)
  - `The {parameter} took too long to match the names; simplify the pattern.` One message for both the timeout and the budget.
- `Backend.Core.csproj` makes the internals visible to `42.Platform.Storyteller.Backend.Core.UnitTests`. An internal `Parse` overload takes the timeout and the budget, so the tests can force a timeout.

### 4. The Functions routes

`Api.Functions/src/V1/AnnotationsHttp.cs`: the 14 inline three-branch blocks of the seven routes became calls to the private `AddNameQuery<TAnnotation>` (`:978`). It parses the query, adds its condition and answers the query. A route then sets the partition of an exact query with `is { IsExact: true } name`. The parameter names come from `nameof`. The matrix is the one in spec section 4. The exact `subjectNameQuery` of usages compares `SubjectName`, as planned. `using System.Text.RegularExpressions;` is gone, and the file has 339 fewer lines. The OpenAPI document does not change.

### 5. Api.Web

`Api.Web/src/Controllers/AnnotationsController.cs` uses the same helper (`:475`) with the same matrix. Its `nameQuery` of responsibilities and subjects changed from a plain `Contains` to the shared syntax with the partition of an exact query. Before the fix, that query answered `500` on responsibilities and filtered nothing on subjects. The duplicate `using System.Text.Json;` is gone.

The mixed-type conditions of `GetDescendants` now filter, as intended. Api.Web still has no exception mapping, so a refused query answers `500` there.

### 6. Tests

- **Backend.Core.** `AnnotationsRequestConditionsTests` has 6 tests. `NameQueryTests` has 16 tests and 34 cases:
  - the parsing rules;
  - the `^[` and unsupported-construct refusals;
  - the too-large automaton and the length cap;
  - the catastrophic pattern on 30 and 1000 characters;
  - the forced timeout;
  - the budget: 26 branches of `.*X.{60}` over 1000 seeded random names of 63 characters. In the slow mode that is about 5 s, and it stops after 250 ms;
  - an ordinary pattern over 1000 names that stays within the budget;
  - `ToCondition`.
- **Api.Functions.**
  - `RecordingAnnotations` has `Listed`, and it filters through `CompileConditions<T>()`.
  - `NameQueries` runs 42 cases: every row of the matrix, each with an exact, a contains and a pattern query. Each case checks the keys and the recorded partition.
  - `rst` with all three kinds of query.
  - Six conjunctions on usages, executions and units of execution.
  - `^[` on all 14 parameters: `InvalidQuery`, and the service is not called.
  - The catastrophic pattern through a route.
  - An `InvalidQuery` row in `ErrorResponseMappingTests`.
- **Backend.CosmosDb.** `CosmosAnnotationConditionTests` has four tests. The spec called this class `CosmosAnnotationNameQueryTests`; it tests plain conditions, so it got another name:
  - a regex condition on responsibilities (the old `InvalidCastException`);
  - contains and AND on usages;
  - an exact context in a subject partition;
  - one multi-type request. After the review, a context in it fails its condition.

### 7. Documentation

- `annotation-lifecycle.md` has the **Name queries** bullet: parameters per route, the forms, `(?i)` after the `^`, the engine with its unsupported constructs and size limit, the length cap, the timeout and the budget, partition narrowing, and short pages. After the review it separates the refusals before the read (invalid, too long, unsupported) from those while a page is filtered (timeout, budget).
- `docs/42for.net/platform/using-the-platform.md` names the forms and the case rule. After the review it also says that filtered pages can be empty with a token, and links the lifecycle page.
- `mcp.md:296`, and the comments in `Mcp/src/Tools/AnnotationTools.cs` and `AnnotationToolsTests.cs`, give the real reason the MCP tool keeps its host filter. The MCP code does not change.

## The review of the branch

Four reviewers covered correctness, security, tests, and docs with the API contract. A skeptic per finding then tried to refute it. Ten findings held (some overlapped) and five were refuted.

**Confirmed and fixed:**

- **The page was not bounded (high).** A pattern that overflows the lazy automaton of the non-backtracking engine falls back to a slower mode. In that mode the cost per character grows with the pattern, and the match timeout is not checked. 26 branches of `.*X.{60}` (242 characters) took 4.6 s per 1000 names of 63 characters, and 19 s for 200 characters, with no exception. The spec's claim "linear in the name, the timeout as a second bound" was wrong. Fixed with the budget and the length cap (section 3). The budget test proves it.
- **A too large automaton was reported as an unsupported construct.** It now carries the engine's message.
- The lifecycle doc said a timeout answers "before any read".
- The multi-type Cosmos test never saw the context condition reject anything.
- `using-the-platform.md` did not mention short pages and simplified the case rule.
- The MCP comments still said the API drops its name queries.
- This review was missing when the reviewers looked.

**Refuted:**

- That Api.Web's `GetDescendants` now filters by the raw route key. It does, and the spec intends it.
- That the Functions "500 regression" test does not run the Cosmos code. The Cosmos tests cover that path.
- Two `sform` messages about empty pages. They were not touched by this diff and did not get worse.
- That the lifecycle tables should list `InvalidQuery`. The bullet on the same page does.

## Compared with the CodeRabbit coding plan

The issue carried a CodeRabbit coding plan, and it was used as a second opinion. Where this change differs:

- **The engine.** The plan chose a backtracking regex with a 50 ms timeout. This change runs the non-backtracking engine, with a length cap and a budget per request. A timeout per match bounds one match, not a page of 1000. The review then showed that the non-backtracking engine needs the budget too.
- **The `%` rule.** The plan stripped one leading and one trailing `%`. `Trim('%')` strips all of them.
- **The exact usage subject.** The plan kept `SubjectKey == AnnotationKey.CreateSubject(q)`. The route compares `SubjectName`, which is equivalent and removes the special case.
- **The docs.** The plan edited the historical ui.admin spec and the MCP review. `CLAUDE.md` treats `docs/**/specs/` as historical, so the current docs changed instead (section 7), and this review closes the MCP item.
- **The input continuation.** The plan asked for a separate issue about the clearing of the input continuation in the Cosmos service. The clearing is unchanged and is correct for one type per token, and filtered pages that are short or empty were already documented. No issue was opened.

## Validation

- `dotnet build 42.mono.slnx`: 0 errors. The first run failed once in `Crumble/Playground` with a missing source-generated type. That project builds alone, and the second full build passed.
- `dotnet test`:

  | Project | Passed |
  |---|---|
  | Backend.Core | 1106 |
  | Api.Functions | 2603 |
  | Mcp | 727 |

- **Backend.CosmosDb** ran against a Testcontainers Linux emulator (`STORYTELLER_TESTS_USE_CONTAINER=true`), not the local one, whose databases the fixture deletes:
  - The annotation, listing, view, configuration and binding tests passed: 357.
  - The full suite did not finish. The evaluation build of the Linux emulator crashed with a core dump during the key/value tests, and the fixture's connection failed after that. Those tests don't use the changed code.

## Left for later

- The MCP `find_annotations` could use the API's name queries for the parameters a list has. Its case-insensitive exact names would need a pattern such as `^(?i)name$`.
- Exact and contains queries could become Cosmos SQL, so that their pages are not short.
- Api.Web has no exception mapping, so its refusals answer `500`.
