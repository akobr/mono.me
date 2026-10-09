# Generic evaluation meter for object bindings

Line numbers refer to PR #57 (`feat/platform/binding-objects`) at head `3cafac1`. This document changes no application code. Phase A is the next piece of work. Phase B is a design for later and is not scheduled.

Measurements marked *(analysis)* come from the sandboxing-alternatives analysis of PR #57 at `3cafac1` (.NET 10.0.401, Linux x64, prototypes outside this repository). This plan did not re-run them. Facts about library APIs and licensing were re-checked for this plan against the restored packages and the upstream sources.

## Problem

An object binding (`$binding` set to `jlogic` or `jsone`) runs a program written by the configuration author. It runs inside the shared Azure Functions worker on every resolved read. Anyone with `Configuration.Write` on a project can store an envelope of a few hundred bytes that occupies the worker for minutes, allocates tens of gigabytes, or kills the process. That includes machine API keys with `ConfigurationReadWrite` or `DefaultReadWrite` (`MachineScopeClaims.cs`).

PR #57 bounds evaluation by capping operators one at a time. That approach does not converge:

- There were 19 JetBrains Air review rounds, and all of them requested changes. Each round closed the reported vectors, and the next round found new ones at the new head. Round 19 is still open: `$sort` `by(...)`, JSON-e `number`, and JSON Logic `missing`, `missing_some` and `var` are unmetered.
- The limiting layer is about 2,770 lines wrapped around a 131-line engine, and it includes a second JSON-e expression parser. It has broken at least 7 ordinary templates:
  - `$if` without `else`
  - `$switch` with `$default`
  - apostrophes and backslashes in interpolated text
  - `$${` escapes
  - slices and object literals next to `+`
  - `split(s, 0)`
  - `$let` values charged twice
- A 15 KB `{"$eval":"!!!…true"}` overflows the stack inside `Json.JsonE.Expressions.ExpressionParser.TryParse` *(analysis: 10,000 `!` pass, 15,000 abort with exit code 134; `-`×50,000 and nested `[` behave the same)*. .NET cannot catch a stack overflow. The Functions worker dies, and so does every in-flight request of every organization. No per-operator cap addresses this.

The caps count operators, but the resources that run out are CPU time, allocated memory and stack. Both libraries are recursive tree-walking interpreters with a small number of dispatch points. A meter charged at those dispatch points bounds every operator at once, including operators nobody has listed yet. Its overshoot is limited to the cost of one primitive operation.

## Current State

### Resolution pipeline

`CosmosConfigurationService.GetResolvedConfigurationInternalAsync` loads the calculated document and calls `IConfigurationBindingResolver.ResolveAsync(content, includeSecrets, scope)`. The only HTTP caller is `ConfigurationHttp.GetConfigurationResolved` (`Api.Functions/src/V1/ConfigurationHttp.cs`, lines 76–122).

`ConfigurationBindingResolver` (`Binding.Object/src/ConfigurationBindingResolver.cs`) walks the document asynchronously:

- It sends `@` strings to `IBindingExecutor`. This is async I/O for `@config`, `@annotation` and Key Vault.
- It resolves the `$context` of each envelope first, and then calls `ObjectBindingEngine.Evaluate` synchronously (line 114).
- It walks each result again so that emitted envelopes and `@` strings resolve.

`ResolveAsync` creates one `EvaluationBudget` per read (line 40), defined at lines 263–289. It holds the envelope count and a step counter.

`ObjectBindingEngine` (`Binding.Object/src/ObjectBindingEngine.cs`, 131 lines) converts `JToken` to `JsonNode` with `JsonTokenConverter` and calls `JsonLogic.Apply` or `JsonE.Evaluate`:

- **Static constructor (lines 16–21):** pins `LogRule.Logger` to `JsonLogicDebugLogger` and calls `JsonLogicBoundedRules.Register()`.
- **`EvaluateJsonE` (lines 74–121):** runs the definition through `JsonELimits.PrepareDefinition`, which is the expression rewriter. It then injects 18 context functions that override builtins and implement the `storyteller*` helpers: `range`, `join`, `split`, `len`, `lowercase`, `uppercase`, `strip`, `lstrip`, `rstrip`, and the `storytellerAdd`, `Concat`, `Bound`, `Let`, `Step`, `In`, `Equals`, `Index` and `Slice` helpers.
- **`EnsureResultFits` (lines 123–130):** checks each envelope result against 100,000 serialized characters, using the bounded walk in `EvaluationSize`.

### Limits at `3cafac1`

| Constant (`ConfigurationBindingResolver`, lines 12–19) | Value | Scope |
|---|---|---|
| `MaxEnvelopeDepth` | 32 | nested envelope evaluations in one read |
| `MaxEnvelopeEvaluations` | 256 | envelope evaluations in one read |
| `MaxJsonEOperatorDepth` | 16 | static `$` operator nesting of one JSON-e template |
| `MaxRangeItems`, `MaxMergeItems`, `MaxReduceItems` | 1000 | per call of JSON-e `range` and JSON Logic `merge` and `reduce` |
| `MaxConcatLength` | 100,000 | `+`, `${}`, `cat`, `join`, the value-size budget, and the envelope result size |
| `MaxEvaluationSteps` | 100,000 | one read. Charged by wrapped iterations, scans and copies, with per-operator weights |

`docs/Platform/Storyteller/binding.md` (lines 95–103) documents every per-operator rule and the 18 reserved context names.

### Cost of the per-operator layer

| File (`Binding.Object/src`) | Lines | Role |
|---|---|---|
| `JsonELimits.cs` | 1,201 | builtin overrides, `storyteller*` helpers, `BoundWrap`/`WrapGrowth`/`ChargeWrap`/`LetWrap`, reserved names, `$switch` special cases, `OperatorDepth` |
| `JsonEExpressionRewriter.cs` | 743 | second JSON-e expression parser. Rewrites `+`, `in`, `==`, `!=`, index and slice |
| `JsonLogicBoundedRules.cs` | 631 | replacement or wrapped `cat`, `merge`, `reduce`, `map`, `all`, `some`, `none`, `filter`, `in`, `==`, `!=`, `===`, `!==`, comparisons, arithmetic, `min`, `max`, `log` |
| `EvaluationSize.cs` | 150 | bounded serialized-length walk |
| `JsonESizeBudget.cs` | 42 | per-evaluation value-size budget |

`test/ConfigurationBindingResolverTests.cs` has 2,481 lines and 110 tests. By name, about 70 of them test individual caps, charges or reserved names.

### Review history (condensed)

| Air round | On commit | What it found | Fix |
|---|---|---|---|
| 1 | 95e4739 | envelopes that emit envelopes fan out 2^n | `MaxEnvelopeEvaluations` (9cebf30) |
| 2 | 9cebf30 | `range`, `$map`, `$let` `s+s`, JSON Logic `reduce`+`cat` | per-operator caps (09acc5c) |
| 3 | 09acc5c | `$reduce [acc,acc]`, `$json`, `join`, nested `$map` | value-size budget, `BoundWrap` (25aafec) |
| 4–5 | 25aafec, 8256594 | regressions: `$if` without `else`, `$switch` `$default`. `$let` fan-out, JSON Logic `map` | 8256594, d26e449 |
| 6–7 | d26e449, 8da873c | loops that return small values. Step cap per evaluation × 256 | step counter, then per read (8da873c, 67eab30) |
| 8–11 | 67eab30 … 433a148 | `+` in `$if`, `$switch` keys, `by`, `each`, interpolated keys, `$${`. Rewriter regressions | 0e5c2d6 … 81952a8 |
| 12 | 81952a8 | root result never charged. Size check serialized first | bounded walk (858252f) |
| 13–15 | 858252f … 41bae15 | scanning operators, `JsonElement` string copies, string index and slice, strict equality, coercion, uncharged operand clones | f598931 … 6244bf3 |
| 16–17 | 6244bf3, f3fb29d | template rebinds `storytellerStep` through `$let`, `each(...)`, an interpolated key | reserved names (f3fb29d, faec65c) |
| 18 | faec65c | iterations charged a flat 1 regardless of body size. `$find` uncounted | weighted steps (3cafac1) |
| 19 | 3cafac1 | **open:** `$sort` `by`, `number(s)`, JSON Logic `missing`/`missing_some`/`var` | none |

Rounds 8 to 17 used the escape hatches the rewriter itself created: unrewritten expression slots, escapes, and rebinding of injected helper names. A meter inside the interpreter has no injected names and no rewriting, so this whole class of problem goes away.

### Libraries (verified for this plan)

**JsonLogic 6.1.0** (`json-everything` commit `6fe4913`):

- `IRule.Apply(JsonNode?, EvaluationContext)`, `RuleRegistry.GetHandler(string)`, `RuleRegistry.AddRule(string, IRule)` and `JsonLogic.Apply(JsonNode?, EvaluationContext)` are public.
- The assembly declares 35 operators through `[Operator]` on `IRule` types: `+ - * / %`, `== != === !==`, `< <= > >=`, `! !!`, `and or if ?:`, `all some none filter map reduce merge in cat substr`, `var missing missing_some`, `min max log`.
- Every rule object dispatches through the registry. Rules recurse through `JsonLogic.Apply`. Literals and arrays do not dispatch.
- `RuleRegistry` cannot enumerate its handlers, so the operator names have to come from the `[Operator]` attributes.

**JsonE.Net 3.0.1** (`json-everything` commit `8b8ab34`, `src/JsonE`, 96 files):

- The only public entry point is `JsonE.Evaluate(JsonNode?, JsonNode?)`. `JsonE.Evaluate(JsonNode?, EvaluationContext)`, `MaybeEvaluateChildren`, `Interpolate` and the following types are all `internal`:
  - the 16 template operators (`Json.JsonE.Operators.*`, `OperatorRepository`);
  - the expression parser (`ExpressionParser` and five operand parsers);
  - the 8 `ExpressionNode` types;
  - the 20 builtins (`Json.JsonE.Expressions.Functions.*`).
- There is no cancellation token, step hook or depth limit.
- `ExpressionParser.TryParse` handles binary chains and parentheses iteratively. Unary, array, object and function-argument operands recurse back into `TryParse`, and this is where the stack overflows.
- A left-deep binary chain parses iteratively, but it evaluates recursively, with depth equal to the number of terms.
- `MaybeEvaluateChildren` clones every child result.
- `RangeFunction` builds its whole list in one loop. The number of items is set by its arguments, not by the size of its input.
- Expression strings are parsed when the operator or the `${}` hole that holds them is evaluated, so parse work is part of evaluation work.

**Licensing.** The `json-everything` source is MIT ("Copyright (c) .NET Foundation and Contributors"). The NuGet binaries carry the Open Source Maintenance Fee EULA (`OSMFEULA.txt`). The EULA says the fee is not a license fee and does not restrict self-compiling. A vendored source build of JSON-e is therefore MIT-only. `JsonLogic` stays a NuGet binary, as it is today. See the License section of the 2026-10-04 object-binding spec.

### Hosting and errors

- `Api.Functions` runs the isolated worker (`dotnet-isolated`, Functions v4, `net10.0`). `host.json` sets no `functionTimeout`. One worker serves all organizations. The repository does not show the hosting plan (Consumption, Flex, Premium or container).
- The resolver catches `BindingException` from the engine (lines 112–121) and rethrows it as a **base** `BindingException` with the JSON path. Any type-specific handling is lost.
- `ExceptionHandlingMiddleware` (`Api.Functions/src/ErrorHandling/ExceptionHandlingMiddleware.cs`, lines 30–51) turns every unhandled exception into HTTP 500 and serializes the exception object into `ErrorResponse.Error`. A broken or abusive binding therefore returns 500, logs at Error, and leaks exception details.
- `JsonTokenConverter.ToToken` re-reads every result with a default `JsonTextReader`, which has `MaxDepth` 64. A deeper result fails with a raw `JsonReaderException`, which becomes a 500.

## Proposed Changes

Three layers. Phase A builds layers 1 and 2 and keeps the existing structural limits. Phase B is the optional isolation layer.

1. **Generic meter, per read** (Phase A): steps (deterministic), time inside the engines, allocated bytes, and recursion depth. JSON Logic gets it through a decorator on its public rule registry. JSON-e gets it through a vendored fork with a small number of `Tick()` and frame calls.
2. **Structural limits and errors** (Phase A): keep the envelope depth and count limits and the bounded result check. Map every evaluation failure to 422 instead of 500.
3. **Process isolation** (Phase B, design only): a warm child-process pool with hard memory caps and kill on deadline, behind an evaluator interface. Do this only if the meter turns out not to be enough.

### Phase A — Generic in-process meter

#### A.1 Limits model

Add a new public record in `Binding.Object`:

```csharp
public sealed record ObjectBindingLimits
{
    public static ObjectBindingLimits Default { get; } = new();

    // Per read (shared by every envelope of one ResolveAsync call).
    public long MaxSteps { get; init; } = 1_000_000;            // primary, deterministic
    public long MaxAllocatedBytes { get; init; } = 64L << 20;   // backstop, thread allocation inside engines
    public TimeSpan MaxEvaluationTime { get; init; } = TimeSpan.FromMilliseconds(500); // backstop, time inside engines
    public int MaxEnvelopeDepth { get; init; } = 32;            // unchanged
    public int MaxEnvelopeEvaluations { get; init; } = 256;     // unchanged

    // Per engine call / per envelope result.
    public int MaxDepth { get; init; } = 512;                   // interpreter + parser + expression frames
    public int MaxResultLength { get; init; } = 100_000;        // serialized characters, bounded walk (was MaxConcatLength)
    public int MaxResultDepth { get; init; } = 64;              // matches JsonTextReader.MaxDepth used by ToToken
}
```

- **Steps** are the user-facing limit. They are the same on every machine, so the same template always passes or always fails.
- **Time and allocation** are backstops for work inside a single primitive, which steps cannot see. Examples are `in` over a 50k array or `missing` over 100k keys. They measure only time and allocation inside engine calls, not the async `@` I/O between envelopes.
- **Calibration of the defaults.** On a slow instance, `MaxEvaluationTime` must stay well above the time that `MaxSteps` steps of plain dispatch take. Then interpreter-bound templates always fail on steps (deterministic) before they fail on time. *(analysis: 2M steps did not finish within 250 ms for the `$let` fan-out vector.)* Phase A calibrates the defaults with the regression corpus (A.10) and records the final numbers in the review.
- **Removed:** `MaxJsonEOperatorDepth`, `MaxRangeItems`, `MaxMergeItems`, `MaxReduceItems`, `MaxConcatLength` (renamed `MaxResultLength`), and `MaxEvaluationSteps` (renamed `MaxSteps`). The public constants on `ConfigurationBindingResolver` go away with them.

#### A.2 `EvaluationMeter`

Add a new internal sealed class in `Binding.Object`. There is one instance per read, owned by the read's `EvaluationBudget`.

```csharp
internal sealed class EvaluationMeter : Json.JsonE.IEvaluationMeter
{
    [ThreadStatic] private static EvaluationMeter? _current;
    public static EvaluationMeter? Current => _current;

    public EvaluationMeter(ObjectBindingLimits limits, TimeProvider timeProvider);

    public long Steps { get; }
    public long AllocatedBytes { get; }     // accumulated across scopes
    public TimeSpan Elapsed { get; }        // accumulated across scopes
    public EvaluationLimitKind? Exceeded { get; }   // latched

    public Scope Enter();                   // sets _current, snapshots GC.GetAllocatedBytesForCurrentThread() and timestamp
    public void Tick(int weight = 1);       // adds steps; checks steps, allocation, time; throws
    public void EnterFrame();               // ++depth; checks MaxDepth and RuntimeHelpers.TryEnsureSufficientExecutionStack(); throws
    public void ExitFrame();                // --depth

    public readonly struct Scope : IDisposable { /* adds alloc/time deltas, restores the previous _current, resets depth */ }
}
```

**Thread affinity.** Engine calls are synchronous, but the resolver awaits between envelopes and can continue on a different thread. So `Enter()` wraps exactly one synchronous engine call. It takes a fresh allocation and timestamp baseline on the current thread. `Dispose` adds the deltas to the per-read totals. `Tick` compares `accumulated + (current − baseline)` with the limits.

**Latching.** The first exceeded limit is stored in `Exceeded`, and every later `Tick` or `EnterFrame` throws again. A `catch` inside a library therefore cannot let evaluation continue for long. After the engine returns, the caller checks `Exceeded` (A.5).

**Stack safety.** `EnterFrame` enforces `MaxDepth` deterministically. It also calls `RuntimeHelpers.TryEnsureSufficientExecutionStack()`, which is catchable and needs no exception to probe. That way thread-pool threads with small stacks (1 MB on Windows) fail with a Depth error instead of a process abort.

**Cost.** Steps are checked on every tick. Time (`TimeProvider.GetTimestamp`) and allocation (`GC.GetAllocatedBytesForCurrentThread`, a cheap FCALL) are also checked on every tick *(analysis: about +2 µs per small JSON-e evaluation and +0.3 µs per small JSON Logic rule)*. If profiling shows this matters, those two checks may run every N ticks with N ≤ 64. The review records the choice.

`TimeProvider` is injected, with `TimeProvider.System` as the default, so that tests can drive the time limit deterministically.

#### A.3 JSON Logic: metered rule decorator

Add `JsonLogicMetering.cs` (internal static). It replaces `JsonLogicBoundedRules.cs`.

- `Install()` runs once from `ObjectBindingEngine`'s static constructor, after `LogRule.Logger` is pinned. It reads the operator names from the `[Operator]` attributes of the `IRule` types in the `JsonLogic` assembly (35 at 6.1.0). It wraps each handler: `RuleRegistry.AddRule(name, new MeteredRule(RuleRegistry.GetHandler(name)!))`.
- `MeteredRule.Apply(args, context)` runs as follows:

  ```csharp
  var meter = EvaluationMeter.Current;
  if (meter is null) return _inner.Apply(args, context);   // pass-through outside a binding read
  meter.Tick();
  meter.EnterFrame();
  try { return _inner.Apply(args, context); }
  finally { meter.ExitFrame(); }
  ```

- JSON Logic semantics go back to the library's own rules. The replacement implementations of `cat`, `merge`, `reduce`, `map`, `all`, `some`, `none`, `filter`, `in`, `==` and `!=`, and the operand copying in `Metered`, are removed.
- The custom `log` rule is removed too. The library's `LogRule` writes through `JsonLogicDebugLogger`, which already serializes only when Debug is enabled.
- Loops (`all`, `map`, `reduce` and the rest) call `JsonLogic.Apply` for each item. A body that contains a rule ticks once per item. A literal body (`{"all":[[…], true]}`) does not tick, but its cost per item is constant and the literal array is bounded by the stored document. Time and allocation cover what steps miss *(analysis: JSON Logic nested `all` 600³ was stopped in 124 ms on allocation)*.

#### A.4 JSON-e: vendored fork with meter hooks

Add a new project `src/Platform/Storyteller/Binding.JsonE/src/Binding.JsonE.csproj`:

- `AssemblyName` `42.Platform.Storyteller.Binding.JsonE`, target `net10.0` only.
- Upstream namespaces (`Json.JsonE.*`) are kept, so that diffs against upstream stay small. Binding.Object stops referencing the `JsonE.Net` package, so the type names cannot conflict. A guard test (A.10) checks that no project references both.
- `<EnableStyleCop>false</EnableStyleCop>`, and upstream formatting is kept. `Nullable` and `LangVersion` follow upstream.
- Packability matches `Binding.Object`, because a packed `Binding.Object` must be able to depend on it.
- References: `Json.More.Net` 3.0.1, added to `Directory.Packages.props` in alphabetical order. The `netstandard2.0` polyfills (`IndexRange`, `PolySharp`, `Microsoft.Bcl.Memory`), SourceLink, strong-name signing and the upstream `InternalsVisibleTo` are dropped.
- `LICENSE` is the upstream MIT text. `VENDORED.md` records the upstream repository, the commit (`8b8ab34027de5ad9f4ed50808b8e4889ca69cf4d`, JsonE.Net 3.0.1), every patched file with its purpose, and the re-sync procedure (copy upstream `src/JsonE`, re-apply the patch, run the spec suite).
- `JsonE.Net` is removed from `Directory.Packages.props` once nothing references it. The project is added to `42.mono.slnx`.

**Patch:**

1. **Hook contract.** New public `IEvaluationMeter`, with `Tick(int weight)`, `EnterFrame()` and `ExitFrame()`. Also a new public overload `JsonE.Evaluate(JsonNode? template, JsonNode? context, IEvaluationMeter? meter)`. It sets an internal `[ThreadStatic]` `Metering.Current` for the duration of the call (try/finally, restoring the previous value), then runs the existing evaluation. The two-argument overload passes `null`. Internal helpers are `Metering.Tick(int weight = 1)` and a `Metering.Frame()` struct whose `Dispose` exits the frame. With no meter, both are no-ops.
2. **Ticks and frames.**

   | Site | Call |
   |---|---|
   | `JsonE.Evaluate(JsonNode?, EvaluationContext)` | `Tick()` + frame |
   | `MaybeEvaluateChildren`, after each object-property and array-item clone | `Tick()` |
   | Each of the 8 `ExpressionNode.Evaluate` overrides (array, binary, context accessor, function, object, primitive, unary, value accessor) | `Tick()` + frame |
   | `ExpressionParser.TryParse`, and any operand parser that recurses without going through `TryParse` | `Tick()` + frame |
   | `Interpolate`, per `${}` hole | `Tick()` |
   | `RangeFunction.Invoke`, per produced item | `Tick()` |

   These frames fix the stack-overflow abort for unary chains, nested array, object and function literals, and left-deep binary chains (evaluation recursion). They do so whether the overflow would happen while parsing or while evaluating.
3. **Builtin audit.** For each of the 20 builtins and 16 template operators, check whether its output size can exceed a constant factor of its input size. Any primitive whose output is driven by a number rather than by its input needs a tick inside its loop. At 3.0.1 the only known case is `range`. The review records the audit, one line per builtin and operator.
4. **Catch audit.** List every `catch` in the fork. Any block that could swallow the meter's exception is narrowed or rethrows it. Latching (A.2) is the backstop.

No semantic change is allowed. With a `null` meter the fork must behave exactly like JsonE.Net 3.0.1 (A.10).

In parallel, and optionally, propose an upstream `EvaluationOptions { IEvaluationMeter? Meter }` to json-everything. Acceptance is unknown, and Phase A does not depend on it.

#### A.5 Engine and resolver changes

`ObjectBindingEngine.Evaluate(kind, definition, context, budget)`:

```csharp
using var scope = budget.Meter.Enter();
JsonNode? result = kind switch
{
    JsonLogicKind => JsonLogic.Apply(definitionNode, contextNode),
    JsonEKind => JsonE.Evaluate(definitionNode, contextNode, budget.Meter),
    _ => throw new BindingEvaluationException($"Unknown object binding '{kind}'."),
};
budget.Meter.ThrowIfExceeded();        // latched limit swallowed by a library catch
ResultCheck.Ensure(result, budget.Limits);
return JsonTokenConverter.ToToken(result);
```

- **Conversions:** `ToNode` and `ToToken` stay inside the scope, so their allocations and time count.
- **Removed:** `PrepareDefinition`, the 18 context-function overrides and `JsonESizeBudget`.
- **Exception mapping, in this order:**
  1. `EvaluationLimitExceededException` and `BindingEvaluationException` pass through.
  2. `JsonEException` and `JsonLogicException` become `BindingEvaluationException`.
  3. Any other exception except `OutOfMemoryException` becomes `BindingEvaluationException("Object binding evaluation failed: <message>")`. Examples are `InvalidOperationException`, `ArgumentException`, `FormatException`, `OverflowException`, `JsonException` and `KeyNotFoundException`. These come from author-supplied input, so the client gets 422 instead of 500.

`ConfigurationBindingResolver`:

- **Constructor:** `ConfigurationBindingResolver(IBindingExecutor stringBindings, ILogger<ConfigurationBindingResolver>? logger = null, ObjectBindingLimits? limits = null, TimeProvider? timeProvider = null)`. The new parameters are appended, so existing call sites still compile.
- **`EvaluationBudget`:** holds `Limits`, `Meter` and `Count`. `AddStep` and `AddSteps` are removed.
- **Envelope limits:** the `MaxEnvelopeDepth` and `MaxEnvelopeEvaluations` checks stay where they are. They now throw `EvaluationLimitExceededException` with `Kind` `EnvelopeDepth` or `EnvelopeCount`. The `MaxJsonEOperatorDepth` check (lines 96–101) is removed.
- **Path wrapping (lines 112–121):** the exception keeps its type. A `BindingEvaluationException` is rethrown as a new instance of the same type, `EvaluationLimitExceededException` keeps its `Kind` and `Limit`. The message gets the `Failed to process the object binding for '<path>':` prefix, `Path` is set, and the original is the inner exception. Only a non-evaluation `BindingException` stays a base `BindingException`.
- **Logging:**
  - On a limit failure: one Warning with kind, path, steps, elapsed ms, allocated bytes and `scope.Context`. `scope.Context` is the `ConfigurationBindingContext` that identifies the configuration.
  - On success, when Debug is enabled: one Debug line per read with the meter totals. This data enables a later per-organization budget (A.13).

#### A.6 Result check

`EvaluationSize.cs` is renamed to `ResultCheck.cs`. It keeps the bounded walk that stops early: it never serializes an oversized value. It adds a depth bound:

- `Ensure(JsonNode? result, ObjectBindingLimits limits)` throws `EvaluationLimitExceededException(Kind = Result)` when the serialized length passes `MaxResultLength` or the nesting passes `MaxResultDepth`.
- The walk tracks depth and stops at `MaxResultDepth + 1`, so the check itself cannot overflow the stack.
- `MaxResultDepth = 64` turns today's raw `JsonReaderException` in `ToToken` into a proper limit error.

This check is still needed with the meter. A structurally shared result such as `[s,s,…]` costs about 3.5 MB to allocate but has a logical size of 360 MB *(analysis)*. Only the result walk catches that case.

#### A.7 Exceptions and HTTP mapping

`Binding.Abstractions`:

- `BindingEvaluationException` stops being `sealed`. It gains an optional `Path` (`string?`) and a constructor that takes one.
- New `EvaluationLimitExceededException : BindingEvaluationException`, with `Kind`, `Limit` (`long`) and `Path`.
- New enum `EvaluationLimitKind { Steps, Time, Memory, Depth, Result, EnvelopeDepth, EnvelopeCount }`.

`Api.Functions`:

- `ExceptionHandlingMiddleware` catches `BindingEvaluationException` before the generic `catch`:
  - It logs at Warning.
  - It responds **422 Unprocessable Content** with `ErrorResponse { Message, ErrorCode, Hint }` and no `Error` object, so neither stack trace nor inner exception reaches the client.
  - `ErrorCode` is `binding.evaluation`, or `binding.limit.<kind>` (lower camel case) for `EvaluationLimitExceededException`.
- `ExceptionExtensions.TryGetErrorCode` returns those codes.
- `GetConfigurationResolved` gains `[OpenApiResponseWithBody(HttpStatusCode.UnprocessableEntity, …, typeof(ErrorResponse))]`.
- `@annotation` and `@config` evaluation failures are also `BindingEvaluationException`, so they move from 500 to 422 as well. That is intended: they are author errors, not server faults.
- Check that the C# SDK, the TypeScript SDK and `sform` show the 422 message the same way they show other 4xx responses. Change them only if they don't.

#### A.8 Deletions

| Delete | Replaced by |
|---|---|
| `JsonEExpressionRewriter.cs` (743) | ticks and frames in the fork's parser and `ExpressionNode.Evaluate` |
| `JsonELimits.cs` (1,201) | the meter and frames. Reserved names are no longer needed, because no helpers are injected into the context. |
| `JsonESizeBudget.cs` (42) | the allocation backstop |
| `JsonLogicBoundedRules.cs` (631) | `JsonLogicMetering.cs` (about 40 lines) |
| `EvaluationSize.cs` (150) | `ResultCheck.cs` (same walk, plus depth) |
| per-operator constants on `ConfigurationBindingResolver` | `ObjectBindingLimits` |
| about 70 per-operator tests | the corpus and limit tests in A.10 |

Kept: `JsonTokenConverter` (including the fix that creates string values directly), `JsonLogicDebugLogger`, the envelope depth and count limits, and the logger fix that only sets the logger when one is passed (resolver constructor).

Expected net change: about −2,500 lines of production code in `Binding.Object`, plus about 250 new lines, plus the vendored fork with a small patch. *(Estimate: the analysis prototype covered the meter, the decorator and the fork patch. It did not implement the frames, the result-depth check or the 422 mapping.)*

#### A.9 Configuration and DI

- `BindingsOptions` gains `ObjectBindingLimits ObjectBindingLimits { get; set; } = ObjectBindingLimits.Default;`.
- `AddConfigurationBindings` (`Binding.Core/src/EntryPoint.cs`) passes it, and `TimeProvider.System`, to the resolver.
- `Api.Functions` binds no configuration section in Phase A. The defaults apply. A host can override them in code through `configure`.

#### A.10 Tests

`Binding.Object/test`:

1. **Functional tests stay.** The roughly 40 tests that check envelope semantics stay unchanged: replacement, context-before-engine, composition, root-object rule, secrets forwarding, ISO strings, `$if` without `else`, `$switch` `$default`, interpolation with `'` and `\`, slices and object literals with `+`, large `$let` values. They must pass against plain library semantics.
2. **Limit tests.** There is one test per `EvaluationLimitKind`, each with small limits passed to the constructor. Time uses a fake `TimeProvider` that advances on every timestamp read, so no real time is spent. Steps are shared across sibling envelopes in one read. `Path` and `Kind` are set on the thrown exception.
3. **Attack corpus.** One `[Theory]` over every vector from Air rounds 1–19, plus:
   - `!`×15,000 and `-`×50,000 in `$eval`;
   - `[`×5,000 nested in an expression;
   - a left-deep `1+1+…` chain of 50,000 terms;
   - a 100-level-deep result built by `$reduce`.

   It runs with default limits. Each case must throw `EvaluationLimitExceededException` within 2 s of wall time. A stack overflow would abort the test host, and that abort counts as a failure. The vectors are stored as JSON files under `test/Corpus/`, so that new findings can be added without writing code.
4. **Metering installed.** For every `[Operator]` name in the `JsonLogic` assembly, `RuleRegistry.GetHandler(name)` is a `MeteredRule`. This catches a package upgrade that adds a rule.
5. **No mixed references.** Reflection check: the `Binding.Object` assembly references `42.Platform.Storyteller.Binding.JsonE` and does not reference `JsonE.Net`.

New `Binding.JsonE/test` (`Binding.JsonE.UnitTests`):

- Port upstream's JSON-e specification-suite test, the json-e `specification.yml` that json-everything runs. It must pass on the fork with a `null` meter and with an unlimited meter.

`Backend.CosmosDb/test`:

- Existing assertions on `BindingException` still pass, because the new types derive from it. Assertions that use exact types are updated.

`Api.Functions`:

- There is no test project at `3cafac1`. Verify the 422 mapping by hand against the Aspire host: `BindingEvaluationException` gives 422 with an error code and no `Error`. Record the result in the review.

#### A.11 Documentation

- `docs/Platform/Storyteller/binding.md`, lines 95–103: replace the per-operator text with a section on read-level limits:
  - the `ObjectBindingLimits` table with defaults and scope;
  - what each kind means;
  - steps are deterministic, time and allocation are backstops;
  - limit failures return 422 with `binding.limit.<kind>`.
- The same file: remove the reserved context names. In the Binding.Object project description, say that JSON-e is a vendored build of JsonE.Net 3.0.1 with metering hooks.
- `docs/Platform/Storyteller/templating.md`: check it for references to the per-operator caps.
- After implementation: `docs/Platform/Storyteller/specs/reviews/YYYY-MM-DD Phase A of Generic evaluation meter.md`, including the calibration numbers, the builtin audit, the catch audit and the overhead measurement.

#### A.12 Acceptance criteria

- Every corpus vector throws `EvaluationLimitExceededException` in under 1 s in a Release build on a developer machine, and none crashes the process.
- All kept functional tests and the JSON-e specification suite pass.
- A small realistic JSON-e template takes at most 20 % or 5 µs longer, whichever is larger. A small JSON Logic rule takes at most 0.5 µs longer. This is measured once and recorded in the review, not run in CI.
- `Binding.Object/src` has at least 2,000 fewer lines than at `3cafac1`.
- The resolved-configuration endpoint returns 422, not 500, for every evaluation failure.

#### A.13 Out of scope for Phase A (follow-ups)

- **Write-time dry run:** evaluate envelopes on `SetConfiguration` under the same limits and return 422 then. This is UX only. It cannot replace read-time limits, because the referenced data changes after the write.
- **Negative cache:** remember (configuration id, etag, `includeSecrets`) pairs that hit a limit for a few minutes and fail fast. This stops one bad write from becoming a sustained CPU burn.
- **Per-organization evaluation budget:** a token bucket on the steps and milliseconds the meter reports.
- **Cancellation:** a `CancellationToken` in `ResolveAsync`, checked in `Tick`, so evaluation stops when the client disconnects or the host shuts down.
- **Weighted ticks for scanning primitives** (`in`, `==`, string index and slice in the fork) would make data-heavy work deterministic too. Add them only if calibration shows that time-based failures are flaky for realistic templates.

### Phase B — Child-process sandbox (design only, not scheduled)

#### B.1 When to do it

The in-process meter has two limits:

- **Memory is soft.** It measures allocation on the evaluating thread. It cannot cap live heap, and it cannot stop another thread.
- **Stack safety depends on guard placement.** A recursion path that the frames miss can still abort the process.

Phase B is justified if any of these happens:

- bindings become authorable by less-trusted parties;
- an uncaught crash path shows up in the fork after Phase A;
- the platform needs hard memory caps per evaluation.

#### B.2 Seam

Add `IObjectBindingEvaluator` in `Binding.Object`:

```csharp
internal interface IObjectBindingEvaluator
{
    EvaluationResult Evaluate(EvaluationRequest request);   // kind, definition, context, remaining budget
}
// EvaluationResult: result JSON or (error kind, message), plus consumed steps / ms / bytes
```

- The Phase A engine becomes `InProcessEvaluator`.
- `ProcessPoolEvaluator` sends the same request to a child.
- The resolver, the envelope limits, the result check, the error mapping and the API stay unchanged. The read-level budget crosses the process boundary as "remaining" in the request and "consumed" in the response.

#### B.3 Child host

- A new console project, `Binding.Object.Sandbox`, framework-dependent on `net10.0` and published next to the Functions app. It hosts the same `InProcessEvaluator`, so the child also runs the meter. Deterministic limit errors keep coming from the meter, and the process kill is only for what the meter cannot stop.
- Child environment:
  - `DOTNET_GCHeapHardLimit`, for example 256 MB. A managed OOM is reported as `Memory` and the child is recycled.
  - `DOTNET_gcServer=0`.
  - A minimal set of environment variables, with no secrets.
- `RLIMIT_AS` is not used, because it breaks CoreCLR start-up *(analysis)*. On Windows plans, a Job Object can add a hard memory and CPU cap where the sandbox allows nested jobs. That is unverified.

#### B.4 Protocol

- Length-prefixed UTF-8 JSON frames over stdin and stdout, one request in flight per child. stderr goes to the parent's logger with a correlation id.
- **Request:** `{ id, kind, definition, context, limits: { steps, bytes, ms, depth, resultLength, resultDepth } }`.
- **Response:** `{ id, ok, result? , error?: { kind, message }, used: { steps, bytes, ms } }`.
- The `$context` is already resolved by the parent, so the child needs no Key Vault, Cosmos or network access, and can run without credentials.

#### B.5 Pool, deadlines, kill and respawn

- **Pool:** N warm children per worker instance, default `Environment.ProcessorCount`. They are pre-spawned at host start, and the cold spawn is about 163 ms *(analysis)*.
- **Deadline:** the parent waits for the remaining time budget plus a margin, for example 250 ms. Past that it calls `Process.Kill(entireProcessTree: true)`, returns `Time` and respawns in the background (about 125 ms *(analysis)*).
- **Recycling:** a child is recycled after K evaluations, or when its working set passes a threshold.
- **Crash-loop protection:**
  - Respawns back off exponentially.
  - After M failures in a window, the pool trips a circuit breaker and fails closed with 503 and a metric. It does not fall back silently to in-process evaluation.
- **Latency:** about +0.25 ms per warm evaluation, including JSON over pipes, against about 15 µs in process *(analysis)*. A killed child costs about 125 ms of CPU to replace. An attacker can force respawns, so the negative cache and per-organization budget from A.13 become prerequisites.

#### B.6 Failure mapping

| Child outcome | Error |
|---|---|
| meter error in the child | same `EvaluationLimitExceededException` kind |
| managed OOM under `GCHeapHardLimit` | `Memory` |
| deadline passed, child killed | `Time` |
| child process exits (stack overflow, exit code 134 on Linux, `0xC00000FD` on Windows) | `Depth` |
| protocol error or unexpected exit | 503, logged at Error, child recycled |

#### B.7 Hosting prerequisites (open)

- Is starting child processes allowed on the chosen Functions plan, and do they survive? App Service and Functions generally allow it, but this is not verified for Consumption, Flex Consumption or containers.
- Does the platform restrict process count or memory per instance in a way that dictates the size of the pool?
- Cgroup delegation should not be expected on Functions *(speculation)*.
- Alternative: evaluation as its own Function App or Container App behind HTTP. It isolates at the platform level at the cost of a network hop, and needs no process management in the API worker.

#### B.8 Further alternative

WASM with Wasmtime gives deterministic fuel and a hard linear-memory cap, and stack exhaustion traps instead of aborting the process. It would replace the .NET libraries with Rust `json-e` and `datalogic-rs`, which changes semantics and adds a Rust and wasm toolchain to CI. It is recorded here for completeness. It is not part of Phase B.

## Open Questions

1. **Default time limit:** this plan proposes 500 ms per read. The analysis used 250 ms. Phase A calibration settles it, but the risk choice belongs to the owner. A lower limit means less CPU burned per abusive read, at a higher risk of time failures on cold or throttled instances.
2. **Fork naming:** `Binding.JsonE` with upstream namespaces kept (proposed), or a fully renamed namespace. Renaming avoids any future type clash, at the cost of a larger diff against upstream.
3. **Upstream:** should a metering hook be proposed to json-everything, so that the fork can eventually be dropped?
4. **Phase B prerequisite:** which Azure Functions plan production runs on (B.7).
