# Phase A of Generic evaluation meter

## Overview

This review covers Phase A of [2026-10-09 Generic evaluation meter for object bindings](../2026-10-09%20Generic%20evaluation%20meter%20for%20object%20bindings.md), implemented on `feat/platform/binding-objects` on top of `3cafac1`. The per-operator limiting layer of PR #57 is gone. One `EvaluationMeter` per resolved read now bounds steps, allocated bytes, time inside the engines, and recursion depth for both engines:

- **JSON Logic:** through a wrapper around all 35 built-in rules in the public `RuleRegistry`.
- **JSON-e:** through a vendored copy of `JsonE.Net` 3.0.1 with a metering hook.

The envelope depth and count limits and the bounded result walk stay. Every evaluation failure now keeps its type and reaches the API as 422 instead of 500.

All 50 attack inputs from the review (Air rounds 1–19 plus the stack-overflow inputs) stop on a limit, and none crashes the process. The fork matches the unpatched binary on all 1,195 cases of the JSON-e specification suite. `Binding.Object/src` shrank from 3,279 to 1,099 lines. Phase B (child-process sandbox) was not started.

## What Was Done

### A.1 Limits model

`ObjectBindingLimits` (`Binding.Object/src/ObjectBindingLimits.cs`) is a public sealed record with the defaults from the spec:

| Limit | Default |
|---|---|
| `MaxSteps` | 1,000,000 |
| `MaxAllocatedBytes` | 64 MiB |
| `MaxEvaluationTime` | 500 ms |
| `MaxEnvelopeDepth` | 32 |
| `MaxEnvelopeEvaluations` | 256 |
| `MaxDepth` | 512 |
| `MaxResultLength` | 100,000 |
| `MaxResultDepth` | 64 |

`Validate()` rejects non-positive values; the resolver constructor calls it.

All public constants were removed from `ConfigurationBindingResolver`: `MaxEnvelopeDepth`, `MaxEnvelopeEvaluations`, `MaxJsonEOperatorDepth`, `MaxRangeItems`, `MaxMergeItems`, `MaxReduceItems`, `MaxConcatLength` and `MaxEvaluationSteps`.

**Calibration.** The defaults were kept. The table shows which limit stopped the 50 attack inputs (Release, see A.12):

| Kind | Inputs | Typical shape |
|---|---|---|
| `Memory` | 21 | loops that build or copy values (`$map`, `$reduce`, `$let` fan-out, JSON Logic `map`, `all`, `filter`, `missing`, `var`) |
| `Depth` | 16 | parser and evaluation recursion, long `||`, `&&`, `+`, `*` chains (more than about 500 terms), `$sort` `by` with 15,000 terms |
| `Time` | 6 | repeated scans of large context data (`in`, `!==`, `<` and `+` coercion, `number`) |
| `Result` | 4 | structurally shared results (`[s,s,…]`), the 100-deep value |
| `Steps` | 2 | `range`-driven loops |
| `EnvelopeCount` | 1 | an envelope that emits two copies of itself |

`Memory` stops loop-heavy attacks before `Steps` does. At 64 MiB the interpreter allocates roughly 100–300 bytes per step, so allocation is in practice the first limit for value-building loops, and it is nearly deterministic for one runtime version. Legitimate templates stay far below every limit: the realistic benchmark template uses 61 steps.

### A.2 `EvaluationMeter`

`Binding.Object/src/EvaluationMeter.cs` implements `Json.JsonE.IEvaluationMeter`, as specified:

- `Current` is a `[ThreadStatic]` field.
- `Enter()` returns a scope that sets `Current` and baselines `GC.GetAllocatedBytesForCurrentThread()` and `TimeProvider.GetTimestamp()`. Its `Dispose` adds the deltas to the per-read totals and restores the previous meter.
- `Tick`, `EnterFrame` / `ExitFrame` and `ThrowIfExceeded` are implemented.
- The first exceeded limit is latched.
- `EnterFrame` checks `MaxDepth` and `RuntimeHelpers.TryEnsureSufficientExecutionStack()`.
- A nested `Enter()` throws `InvalidOperationException`; it would mean a programming error.

Steps are checked first, so a read that passes several limits at once reports the deterministic one.

**Deviation: the clock is sampled.** Steps and allocation are checked on every tick. The clock is read every 16 steps. The first benchmark (every check on every tick) measured +4.6 µs (27 %) per small JSON-e template, most of it from `Stopwatch` reads. Sampling the clock brought it to +3.0 µs. Allocation is not sampled, because a value can double on every step (`cat` and `[acc,acc]` doubling), and sampling would multiply the overshoot exponentially. Time grows linearly, so sampling adds a bounded overshoot of 16 primitives.

### A.3 JSON Logic: metered rule decorator

In `Binding.Object/src/JsonLogicMetering.cs`, `Operators` reads the names from `[Operator]` on the `IRule` types of the `JsonLogic` assembly. There are 35; `if` and `?:` share one type.

`Install()` wraps every handler in a `MeteredRule` exactly once, under a lock. It is called from `ObjectBindingEngine`'s static constructor after `LogRule.Logger` is pinned.

`MeteredRule.Apply` does the following:

- With no current meter, it calls the inner handler directly.
- With a meter, it ticks, enters a frame, calls the inner handler, and exits the frame.

`JsonLogicBoundedRules.cs` was deleted, including the custom `log` rule. The library's `LogRule` writes through `JsonLogicDebugLogger`, which serializes only at Debug.

### A.4 JSON-e: vendored fork with meter hooks

The new project `src/Platform/Storyteller/Binding.JsonE/src/Binding.JsonE.csproj` (`42.Platform.Storyteller.Binding.JsonE`) is set up as follows:

- **Target and analyzers:** `net10.0`, StyleCop off, upstream namespaces and formatting kept.
- **References and license:** references `Json.More.Net` 3.0.1 (added to `Directory.Packages.props`), and includes the upstream MIT `LICENSE`.
- **Source:** the 94 `.cs` files of upstream `src/JsonE` at `8b8ab34027de5ad9f4ed50808b8e4889ca69cf4d`, fetched through the GitHub API.
- **Removed from upstream:** `JsonE.csproj`, `assembly.cs` and `README.md`.
- **`VENDORED.md`:** lists the upstream source, the patch, both audits and the re-sync steps.

The patch is the new `Metering.cs` (69 lines) plus 47 added lines in 11 upstream files. Every patched line calls `Metering`:

- `JsonE.Evaluate(JsonNode?, JsonNode?, IEvaluationMeter?)`: the new public overload. It sets and restores `Metering.Current` in `try`/`finally`.
- `JsonE.Evaluate(JsonNode?, EvaluationContext)`: ticks and opens a frame.
- `MaybeEvaluateChildren`: ticks after each object property and array item.
- `Interpolate`: ticks per `${` hole.
- `ExpressionParser.TryParse`: ticks and opens a frame. Every recursive operand parser goes through it. This was checked with grep: no parser recurses in another way.
- **Expression node `Evaluate` overrides:** all 8 tick. The 6 recursive ones also open a frame; the primitive and context-accessor leaves do not.
- **`BuildString` frames (not in the spec):** `BuildString` of the binary, unary and function nodes opens a frame, because error messages print expressions, and a printed left-deep chain recurses once per term.
- `RangeFunction.Invoke`: ticks per item.

**Audits.** Both are recorded in `VENDORED.md`:

- *Output size:* `range` is the only builtin or operator whose output size is set by a number. `**` is `Math.Pow`.
- *Catch blocks:* there are two, `SpanExtensions.TryParseValue` (literal parsing, never meters) and `SortOperator` (rethrows the inner exception of an `InvalidOperationException`). Neither can swallow the meter's exception.

**Deviation: `JsonE.Net` stays in `Directory.Packages.props`, test-only.** `Binding.JsonE.UnitTests` references it with `Aliases="upstream"` to compare the fork with the unpatched binary. No production project references it. `JsonLogicMeteringTests.BindingObject_ReferencesVendoredJsonE_NotThePackage` checks that.

An upstream proposal for a metering hook was not filed.

### A.5 Engine and resolver changes

`ObjectBindingEngine.Evaluate(kind, definition, context, EvaluationMeter meter)` works as planned:

- **Order:** meter scope around the engine call, then `ThrowIfExceeded`, `ResultCheck.Ensure`, `ToToken`.
- **Removed:** `PrepareDefinition`, the 18 context-function overrides and `JsonESizeBudget`.
- **Exception mapping, as planned:** limit and evaluation exceptions pass through. `JsonEException` and `JsonLogicException` become `BindingEvaluationException`. `InsufficientExecutionStackException` becomes a `Depth` limit. Any other exception except `OutOfMemoryException` becomes `BindingEvaluationException("Object binding evaluation failed: …")`. Example: `2 ** 100000` threw a raw `OverflowException`, which was a 500.

**Deviation: conversions are not metered.** The spec placed `ToNode` and `ToToken` inside the meter scope. With that, a legitimate read of a 100,001-item context array failed on `Memory`, because `JsonTokenConverter` builds every number with `JsonNode.Parse`, about 600 bytes each. The conversions are linear in data that is already bounded: the stored document, and results that already passed `ResultCheck`. So they now run outside the scope.

**New: input depth guard.** `ToNode` takes `maxDepth` (= `MaxDepth`) and throws a `Depth` limit past it. The attack set found a stack overflow in the recursive `ToNode` on a programmatically built 5,000-deep JSON Logic rule. Stored documents cannot be that deep, because `CosmosDefaultJsonSerializer` reads with `MaxDepth = 64`. The guard is defense in depth.

`ConfigurationBindingResolver`:

- **Constructor:** `(IBindingExecutor, ILogger<…>? logger = null, ObjectBindingLimits? limits = null, TimeProvider? timeProvider = null)`.
- **`EvaluationBudget`:** holds `Meter` and `Count`.
- **Envelope limits:** the depth and count checks throw `EvaluationLimitExceededException` (`EnvelopeDepth`, `EnvelopeCount`) with `Path`.
- **Removed:** the JSON-e operator-depth pre-check.
- **Rethrowing keeps the type:** a limit exception keeps `Kind` and `Limit`, a `BindingEvaluationException` stays one with `Path`, and only another `BindingException` stays a base `BindingException`.
- **Malformed envelopes and non-object roots or contexts:** carry `Path` too.
- **Logging:** a Warning on every limit (kind, limit, path, `scope.Context`, envelopes, steps, ms, bytes), guarded by `IsEnabled`. A Debug summary per read that evaluated envelopes.

### A.6 Result check

`EvaluationSize.cs` was replaced by `ResultCheck.cs`. It is the same bounded serialized-length walk, carried by a `Walk` object, plus a nesting bound that stops before `MaxResultDepth`. Both failures are `EvaluationLimitExceededException(Kind = Result)` with distinct messages.

A test pins the default boundary: a 64-deep result converts, and a 65-deep result stops on `Result` before `JsonTextReader` (`MaxDepth` 64) would throw a raw `JsonReaderException`.

### A.7 Exceptions and HTTP mapping

`Binding.Abstractions`:

- `BindingEvaluationException` is no longer `sealed`. It gained `Path` and a `(message, path, innerException)` constructor.
- New `EvaluationLimitExceededException` (`Kind`, `Limit`).
- New `EvaluationLimitKind`.

`Api.Functions`:

- `ExceptionHandlingMiddleware` catches `BindingEvaluationException` before the generic handler. It logs at Warning and returns 422 with `ErrorResponse { Message, Hint, ErrorCode }` and no `Error`.
- `ExceptionExtensions.TryGetErrorCode` returns `binding.limit.<kind>` (lower camel case) or `binding.evaluation`.
- `GetConfigurationResolved` declares the 422 response in its OpenAPI attributes.
- `WriteAsJsonAsync` in `Microsoft.Azure.Functions.Worker.Core` 2.x no longer changes the status code. This was checked in the upstream source. So `CreateResponse(422)` followed by `WriteAsJsonAsync` keeps 422. The 500 path was not changed.

**Not verified end to end.** The 422 path was checked only by build and code review:

- `Api.Functions` has no test project.
- Running the Aspire host here needs an Azure Cosmos account and real authentication.
- How the SDKs and `sform` show a 422 was not checked either.

### A.8 Deletions

Deleted:

- `JsonEExpressionRewriter.cs`
- `JsonELimits.cs`
- `JsonESizeBudget.cs`
- `JsonLogicBoundedRules.cs`
- `EvaluationSize.cs` (replaced)

`Binding.Object/src` went from **3,279 lines to 1,099 (−2,180)**. Added: `EvaluationMeter` (201), `ResultCheck` (196), `JsonLogicMetering` (82), `ObjectBindingLimits` (70). `ConfigurationBindingResolver` grew from 290 to 344 because of logging and type-preserving rethrows.

The test file went from 2,481 lines (110 tests, about 70 of them per-operator) to 2,035 lines in ten files (one type per file, including the unchanged `FakeStringBinding`) with 169 test cases.

### A.9 Configuration and DI

`BindingsOptions.ObjectBindingLimits` defaults to `ObjectBindingLimits.Default`. `AddConfigurationBindings` passes it, and `TimeProvider.System`, to the resolver. `Api.Functions` binds no configuration section.

### A.10 Tests

**`Binding.Object.UnitTests`: 169 test cases, all pass.**

| File | Content |
|---|---|
| `ConfigurationBindingResolverTests` | Envelope semantics, unchanged, plus plain library behavior as theories. The scan and charge tests became 18 JSON-e builtin cases and 21 JSON Logic rule cases. Also: the large-context-data test (a 100,001-item array and a 100,001-character string pass under default limits) and engine-error typing. |
| `EvaluationLimitTests` | One test per kind with small limits. Time uses a stepping `TimeProvider`. Steps shared across siblings is calibrated through the Debug read summary. Also: warning log, latching, scope accumulation, nested scope, validation. |
| `AttackCorpus` / `AttackCorpusTests` | 50 vectors in a collection that runs alone, each required to throw `EvaluationLimitExceededException` within 2 s. |
| `JsonLogicMeteringTests` | All 35 rules wrapped, idempotent install, pass-through outside a read, ticks per rule, assembly references. |
| `Fixtures`, `SteppingTimeProvider`, `CapturingLogger`, `AttackCorpusCollection` | Shared builders, a clock that advances on every read, a structured log capture, and the collection that runs the attack set alone. |

**Deviation: the attack inputs are C# generators, not JSON files.** Reported sizes (90,000- and 500,000-character strings, 15,000-term expressions, 200,000-item arrays) would make multi-megabyte fixture files.

**Vectors left out** because they do not attack plain JSON-e or JSON Logic:

| Vector | Why it is left out |
|---|---|
| `$${…}` escape in a value | Not evaluated by the library. |
| JSON Logic `<` on a 200k array | 54 ms in the raw library. |
| `len(xs[0])` | 96 ms raw. |
| JSON Logic `log` chains | Debug-gated. |
| `s<'a'` ×20,000 | Fixed earlier by `ToNode`. |
| Rebinding `storyteller*` names | The helpers no longer exist. |

**Tests whose expectations changed.** They had asserted the rewriter's behavior, not JSON-e's:

- **`$${x} and ${x}`.** JsonE.Net 3.0.1 renders `2 and {x}`, because `Interpolate` uses `string.Replace` on the whole string. The resolver tests now use distinct holes (`$${y} and ${x}` → `${y} and 2`). `Binding.JsonE` parity tests pin the quirk.
- **`join` with `"join": "nope"` in `$context`.** A context value now shadows the builtin, as in JSON-e. The test no longer passes that context.
- **Interpolated numbers changed from `1.5` to `2`.** `Interpolate` formats numbers with the current culture.

**`Binding.JsonE.UnitTests`: 2,413 test cases, all pass.**

| Test | What it checks |
|---|---|
| `SpecificationParityTests` | All 1,195 cases of json-e `specification.yml` (json-e commit `6d7b7af`, MPL-2.0, license file included) and json-everything's `more-tests.yml`. Each case runs against the unpatched `JsonE.Net` binary, without a meter and with a counting meter. The result JSON or exception type and message must be identical, and every frame must be balanced. |
| `Fork_MatchesUpstream_OnReviewedTemplates` | 11 templates from the review. |
| `Fork_ConformsToSpecification_ExactlyLikeUpstream` | Upstream and fork conform on the same 1,191 cases. |
| `MeteringHookTests` | Ticks, frames, a throwing meter, restoring `Current`, `range` and interpolation ticks, and depth stops for `!`×15,000, `-`×50,000, `[`×5,000 and a 50,000-term `+` chain. |

**Other projects:**

- `Backend.CosmosDb.UnitTests`: all 119 pass against the Testcontainers Cosmos emulator. That includes the binding tests in `CosmosConfigurationServiceTests` and `AnnotationBindingFunctionTests`, whose `BindingEvaluationException` assertions hold.
- `Binding.Language.UnitTests`: all 95 pass.
- Every project that depends on the binding projects builds.
- `dotnet build src/Platform` fails on generated `obj/**/WorkerExtensions.csproj` files picked up by the traversal glob (`NU1008`). This also happens without this change.

### A.11 Documentation

In `docs/Platform/Storyteller/binding.md`:

- The per-operator paragraph and the reserved names are replaced by "Evaluation limits" (limits table, determinism, typical trip kinds, overshoot, stack safety, result walk, envelope limits, exception and logging) and "Errors" (type preservation, 422 and error codes).
- The project structure lists `EvaluationLimitExceededException`, the meter, `ResultCheck`, the new `Binding.JsonE` section and `BindingsOptions.ObjectBindingLimits`.

`templating.md` has no limit text, so it was left unchanged. `42.mono.slnx` lists both new projects.

### A.12 Acceptance criteria

| Criterion | Result |
|---|---|
| Every attack input throws a limit in under 1 s (Release), with no crash | **49 of 50.** The exception is `!==` on two 200,000-item arrays ×4,000, at 1.14 s. Converting the 400,000 context numbers takes 0.31–0.42 s, measured, outside the meter (A.5, same cost as at `3cafac1`). The rest is the 0.5 s time limit plus at most 16 sampled primitives. The next slowest inputs take 0.61 s. No input crashes the process. |
| Kept functional tests and the JSON-e specification suite pass | Yes. |
| Overhead ≤ max(20 %, 5 µs) for JSON-e, ≤ 0.5 µs for JSON Logic | **JSON-e +3.2 µs (17–19 %)** against the upstream binary (16.8–17.2 µs → 20.0–20.1 µs). **JSON Logic +0.13 µs** (0.52 → 0.65 µs). |
| `Binding.Object/src` at least 2,000 lines smaller | −2,180 lines. |
| 422 instead of 500 for every evaluation failure | Implemented, not verified end to end (A.7). |

About the JSON-e overhead: the fork without a meter is already about 2 µs slower than the upstream binary. That is most likely the `try`/`finally` the frames add to every evaluation method. This was not profiled further. The benchmark was a throwaway Release console program outside the repository: 20,000 warm-up iterations, best of 5 rounds of 200,000.

### A.13 Out of scope

Not done, as planned:

- write-time dry run
- negative cache
- per-organization budget
- cancellation
- weighted ticks for scanning primitives
- Phase B

## Follow-ups found during implementation

1. **Cheaper context conversion.** `JsonTokenConverter.ToNode` creates every number with `JsonNode.Parse`, about 1 µs and 600 bytes each. Integers could become `JsonValue.Create(long)`. This needs a careful parity check for number formatting.
2. **Upstream JsonE.Net quirks** (not changed, the fork keeps upstream semantics):
   - The same `${…}` hole written escaped and live in one string renders wrongly (`string.Replace`).
   - `Interpolate` formats numbers, and `number()` parses them, with the current culture.

   Both are candidates for an upstream fix together with a metering hook.
3. **Shared log sink.** `JsonLogicDebugLogger` is process-wide, so parallel resolvers share one log sink (CodeRabbit's earlier remark). Tests account for it.
