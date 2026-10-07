# Envelope evaluation limits

## Overview

This review records limits added after [2026-10-04 Object-level binding with JSON Logic and JSON-e](../2026-10-04%20Object-level%20binding%20with%20JSON%20Logic%20and%20JSON-e.md). A resolved read stops at 32 nested envelopes and at 256 envelope evaluations. Each evaluation also has fixed caps on JSON-e `range`, operator nesting, and string growth, and on JSON Logic `cat`, `merge`, and `reduce`. A value-size budget on the same 100000-character limit covers results that grow by copying themselves. The spec's resolution-order section still describes only the depth counter of 32.

## What Was Done

### Evaluation budget

`MaxEnvelopeEvaluations` is 256, next to `MaxEnvelopeDepth`. `ResolveAsync` creates one `EvaluationBudget` and passes that same instance through the walk. The count increments immediately before `ObjectBindingEngine.Evaluate`, after `$context` has been resolved, so envelopes inside the context spend the same budget. The 257th envelope throws `BindingEvaluationException` with the JSON path and does not call the engine. Depth 32 is unchanged. A straight chain of 32 still succeeds.

`Resolve_EnvelopeFanOutOfTwo_ResolvesBothBranches` evaluates a height-3 binary fan-out to `[["done","done"],["done","done"]]`. `Resolve_EnvelopeFanOutPastEvaluationBudget_Throws` builds the smallest height whose full evaluation count is above 256 and expects the budget exception.

### JSON-e range

`JsonE.Evaluate` receives a context `range` function built with `JsonFunction.Create`. The function replaces the built-in, including a `range` value stored in `$context`. It counts the integers the built-in loop would emit and throws `BindingEvaluationException` before allocating when the count is above `MaxRangeItems` (1000). `range(0, 1000)` still returns 1000 numbers. A wider `range`, including one consumed by `$map`, throws.

### JSON Logic cat, merge, and reduce

`ObjectBindingEngine` registers replacement handlers with `RuleRegistry.AddRule` for the model-less path used by `JsonLogic.Apply`. `cat` stops before the result passes `MaxConcatLength` (100000 characters). `merge` stops before the flattened result passes `MaxMergeItems` (1000). `reduce` stops before it walks an input longer than `MaxReduceItems` (1000). The handlers check the size while they build the result. A `reduce` whose rule is `cat` of the accumulator with itself hits the character cap while the input array is still short.

### JSON-e template depth and string growth

`MaxJsonEOperatorDepth` is 16. The resolver counts objects in `$definition` that have a key starting with a single `$`, and throws `BindingEvaluationException` before the engine runs. Keys that start with `$$` stay data, so an escaped envelope chain still fits under the cap. A nesting cap alone leaves a shallow `$reduce` of `acc+acc` unbounded, and `${acc}${acc}` is the same shape. Before evaluation, `$eval` expressions that contain `+` are rewritten to `storytellerAdd`, and strings that contain `${...}` are rewritten to `storytellerConcat`. Both functions stop at the same 100000-character limit. The names `range`, `storytellerAdd`, and `storytellerConcat` are written onto the context for that call.

### Value size

The operator caps still left other ways to grow a value inside one evaluation. A `$reduce` or JSON Logic `reduce` whose step builds `[accumulator, accumulator]` doubles a tree. `$json` of that pair, `join` of that pair, and nested `$map` over small values grow without touching `range`, `cat`, or the concatenation rewrite.

`MaxConcatLength` is that budget. `JsonLogicBoundedRules.Reduce` measures the serialized accumulator after each step and throws `BindingEvaluationException` when the length is above 100000. A `reduce` whose rule is `cat` still throws the `cat` message first. Each JSON-e evaluation creates a `JsonESizeBudget`. `PrepareDefinition` wraps every `$map` or `$reduce` `each(...)` body and every `$json` value in `{"$let":{"v":...},"in":{"$eval":"storytellerBound(v)"}}` after the children are rewritten, so the injected `$let` is not part of the operator-depth count. `storytellerBound` adds the serialized length and returns the same node. `join` replaces the built-in, stops while it builds the string, and charges the same budget. The JSON-e `+` and interpolation doublings now report the value-size message, because the running total passes 100000 before any one string does.

The new tests use the smallest counts that pass the budget: 15 array-pair steps, 17 `join` steps, the `$json` doubling step whose running total first passes the budget, and three nested `$map`s over `range(0, 50)`. An unchecked run of those fixtures stays at a few hundred kilobytes. `join(['a','b'],'-')`, a one-step `[acc,acc]`, and `$json` of `["x","x"]` still return their normal results.

### Tests and documentation

`Binding.Object.UnitTests` passed 44 tests. The over-cap cases use a range of 1001, a `$map` over that range, nested `$let` just past the operator cap, a short `cat`/`reduce` doubling, a merge of 1001 items, the shallow JSON-e `+` and interpolation doublings, and the five value-size shapes above. They finished with the rest of the suite in under a second. `binding.md` describes the envelope counters, the operator caps, and the value-size budget.
