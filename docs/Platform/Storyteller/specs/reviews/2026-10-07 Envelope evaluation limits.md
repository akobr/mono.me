# Envelope evaluation limits

## Overview

This review records limits added after [2026-10-04 Object-level binding with JSON Logic and JSON-e](../2026-10-04%20Object-level%20binding%20with%20JSON%20Logic%20and%20JSON-e.md). A resolved read stops at 32 nested envelopes and at 256 envelope evaluations. Each evaluation also has fixed caps on JSON-e `range`, operator nesting, and string growth, and on JSON Logic `cat`, `merge`, and `reduce`. A value-size budget on the same 100000-character limit covers JSON-e operator results and JSON Logic `map` and `reduce`. JSON Logic iterations and wrapped JSON-e evaluations share one limit of 100000 steps for that read. The spec's resolution-order section still describes only the depth counter of 32.

## What Was Done

### Evaluation budget

`MaxEnvelopeEvaluations` is 256, next to `MaxEnvelopeDepth`. `ResolveAsync` creates one `EvaluationBudget` and passes that same instance through the walk. The count increments immediately before `ObjectBindingEngine.Evaluate`, after `$context` has been resolved, so envelopes inside the context spend the same budget. The 257th envelope throws `BindingEvaluationException` with the JSON path and does not call the engine. Depth 32 is unchanged. A straight chain of 32 still succeeds.

`Resolve_EnvelopeFanOutOfTwo_ResolvesBothBranches` evaluates a height-3 binary fan-out to `[["done","done"],["done","done"]]`. `Resolve_EnvelopeFanOutPastEvaluationBudget_Throws` builds the smallest height whose full evaluation count is above 256 and expects the budget exception.

### JSON-e range

`JsonE.Evaluate` receives a context `range` function built with `JsonFunction.Create`. The function replaces the built-in, including a `range` value stored in `$context`. It counts the integers the built-in loop would emit and throws `BindingEvaluationException` before allocating when the count is above `MaxRangeItems` (1000). `range(0, 1000)` still returns 1000 numbers. A wider `range`, including one consumed by `$map`, throws.

### JSON Logic cat, merge, and reduce

`ObjectBindingEngine` registers replacement handlers with `RuleRegistry.AddRule` for the model-less path used by `JsonLogic.Apply`. `cat` stops before the result passes `MaxConcatLength` (100000 characters). `merge` stops before the flattened result passes `MaxMergeItems` (1000). `reduce` stops before it walks an input longer than `MaxReduceItems` (1000). The handlers check the size while they build the result. A `reduce` whose rule is `cat` of the accumulator with itself hits the character cap while the input array is still short.

### JSON-e template depth and string growth

`MaxJsonEOperatorDepth` is 16. The resolver counts objects in `$definition` that have a key starting with a single `$`, and throws `BindingEvaluationException` before the engine runs. Keys that start with `$$` stay data, so an escaped envelope chain still fits under the cap. A nesting cap alone leaves a shallow `$reduce` of `acc+acc` unbounded, and `${acc}${acc}` is the same shape. Before evaluation, every JSON-e expression that contains `+` is rewritten to `storytellerAdd`, and strings that contain `${...}` are rewritten to `storytellerConcat`. Both functions stop at the same 100000-character limit. The names `range`, `storytellerAdd`, and `storytellerConcat` are written onto the context for that call. The expression slots are `$eval`, the `$if` condition, `$switch` and `$match` keys other than `$default`, `$sort` `by(...)`, and `$find` `each(...)`. `$fromNow` is left unchanged, because a leading `+` is part of the duration. A rewritten case key that collides with another key is rejected. An object key that contains `${...}` is renamed to `${storytellerConcat(...)}` before its properties are walked, so `+` inside the hole and a chain of holes share that limit. A rewritten key that collides with another key is rejected.

`Resolve_JsonEIfPlus_ReturnsThen` evaluates `s+'b' == 'ab'` with `s` of `a` to `1`. `Resolve_JsonESwitchPlusKey_ReturnsMatch` matches the same expression as a `$switch` key and keeps `$default`. `Resolve_JsonEIfPlusPastConcatLimit_Throws` and `Resolve_JsonESwitchPlusKeyPastConcatLimit_Throws` use one `s` of length `MaxConcatLength / 2 + 1`, so the first `s+s` is 100002 characters and throws the concatenation message. `Resolve_JsonESortByPlus_OrdersByKey` sorts by `x.a + 10`. `Resolve_JsonEFindEachPlus_ReturnsFirstMatch` keeps `aa` when `len(x+x) > 3`. `Resolve_JsonEMatchPlusKey_ReturnsMatch` returns `["yes"]` for a `$match` key. `Resolve_JsonEInterpolatedKey_RendersName` renders `${s+'b'}` as `ab` and `pre-${s}-mid}{-${s+'}'}` as `pre-a-mid}{-a}`. `Resolve_JsonEInterpolatedKeyPastConcatLimit_Throws` uses the same 50001-character `s` inside a key `${len(s+s)}`. Each over-cap fixture is one string of about 50 KB.

### Value size

The operator caps still left other ways to grow a value inside one evaluation. A `$reduce` or JSON Logic `reduce` whose step builds `[accumulator, accumulator]` doubles a tree. `$json` of that pair, `join` of that pair, and nested `$map` over small values grow without touching `range`, `cat`, or the concatenation rewrite.

`MaxConcatLength` is that budget. `JsonLogicBoundedRules.Reduce` measures the serialized accumulator after each step and throws `BindingEvaluationException` when the length is above 100000. A `reduce` whose rule is `cat` still throws the `cat` message first. Each JSON-e evaluation creates a `JsonESizeBudget`. `PrepareDefinition` wraps every `$map` or `$reduce` `each(...)` body and every `$json` value in `{"$let":{"v":...},"in":{"$eval":"storytellerBound(v)"}}` after the children are rewritten, so the injected `$let` is not part of the operator-depth count. `storytellerBound` adds the serialized length and returns the same node. `join` replaces the built-in, stops while it builds the string, and charges the same budget. The JSON-e `+` and interpolation doublings now report the value-size message, because the running total passes 100000 before any one string does.

The new tests use the smallest counts that pass the budget: 15 array-pair steps, 17 `join` steps, the `$json` doubling step whose running total first passes the budget, and three nested `$map`s over `range(0, 50)`. An unchecked run of those fixtures stays at a few hundred kilobytes. `join(['a','b'],'-')`, a one-step `[acc,acc]`, and `$json` of `["x","x"]` still return their normal results.

### Operator results and JSON Logic map

Charging only `each`, `$json`, and `join` still left two holes. A `$let` binding can repeat a value inside an `$eval` array, and JSON Logic `map` was still the library rule. The first wrapper also bound the body with `$let` directly, so a delete marker never became `v`. The usual filter, `$if` without `else` inside `$map`, then failed with an unknown `v`.

`PrepareDefinition` now passes every operator object that is a property value or an array item through the size wrapper, along with `each` and `$json` values that are not operators. The wrapper holds the body in a one-element array and calls `storytellerBound` only when that array is not empty, so a delete marker still drops the value. `JsonLogicBoundedRules` registers `map`. It pushes each element, adds the serialized length of the mapped value, and throws `BindingEvaluationException` when the running total passes 100000.

### Iteration and step caps

Size checks still miss loops whose result stays small. JSON Logic `all`, `some`, `none`, and `filter` were the library rules, so nested scans could return `true` or `[]` after a cubic number of iterations. A JSON-e body that renders to a delete marker skipped `storytellerBound`, so nested `$map`s with `$if` and no `else` were charged only for the empty arrays.

`MaxEvaluationSteps` is 100000 for the whole read. `ResolveAsync` keeps that count on the same `EvaluationBudget` it already passes through the walk, and `ObjectBindingEngine.Evaluate` receives it. `JsonLogicBoundedRules` points a thread-static field at that budget before `JsonLogic.Apply` and does not zero the count between envelopes, because the handlers are process-wide singletons. `all`, `some`, `none`, and `filter` replace the library rules and keep their short-circuit and empty-input results. Those rules, plus `map` and `reduce`, call `EvaluationBudget.AddStep` once per element. Each JSON-e evaluation still has its own `JsonESizeBudget` for characters. `storytellerStep` calls the shared `AddStep` before it decides whether to keep the value, throws past the cap, and returns whether the held array is non-empty, so a dropped value still counts. Sibling envelopes share the total. The context name `storytellerStep` is reserved with the other checks.

`Resolve_JsonLogicNestedAllPastIterationLimit_Throws` uses three `all`s whose width is the smallest cube past the cap, at most 50. `Resolve_JsonENestedMapDeletedPastStepLimit_Throws` uses the same width with `$if` and no `else`. An unchecked run of either fixture is about 100000 cheap iterations. `Resolve_SiblingEnvelopesShareStepLimit_Throws` uses the largest width still under the cap: one `jsone` envelope of deleted `$map`s resolves, and the same envelope beside a `jlogic` envelope of nested `all`s throws. `Resolve_JsonLogicAll_ReturnsTrue` still returns true for `[1,2]`.

The `$switch` cases object is the exception. Its `$default` key starts with `$`, so `HasOperator` would treat the cases as an operator and the wrapper would replace the condition keys. `ShouldBound` leaves the `$switch` value unwrapped. `Rewrite` still walks the cases, so an operator-valued case is charged, and a parent still charges the `$switch` result. `Resolve_JsonESwitchDefault_ReturnsDefault` evaluates `{"x == 2":"two","$default":"other"}` with `x` of `1` to `other`.

`Resolve_JsonEMapIfWithoutElse_DropsFalseItems` maps `[1,2,3]` with `$if` and no `else` to `[2,3]`. `Resolve_JsonELetFanOutPastValueSize_Throws` uses three levels of ten copies of a 100-character seed, about 100 KB if the charge were absent. `Resolve_JsonLogicNestedMapPastValueSize_Throws` uses three `map`s of 30 items; an unchecked result stays under a megabyte. `Resolve_JsonLogicMap_RepeatsLiteral` still returns `["a","a"]`.

### Tests and documentation

`Binding.Object.UnitTests` passed 62 tests. The over-cap cases use a range of 1001, a `$map` over that range, nested `$let` just past the operator cap, a short `cat`/`reduce` doubling, a merge of 1001 items, the shallow JSON-e `+` and interpolation doublings, the five earlier value-size shapes, the `$let` fan-out, the nested JSON Logic `map`, the nested `all`, the nested `$map` of dropped values, two sibling envelopes that share the step total, `+` inside an `$if` condition and a `$switch` key, and `+` inside an interpolated object key. `$switch` with `$default` returns the default value. A small `+` in `$if`, `$switch`, `$match`, `$sort` `by(...)`, `$find` `each(...)`, and an object key still returns its value. They finished with the rest of the suite in about three seconds. `binding.md` describes the envelope counters, the operator caps, the value-size budget, the shared step limit for one read, the `+` limit on every JSON-e expression, interpolation in string values and object keys, JSON Logic `map`, and `$if` without `else`.
