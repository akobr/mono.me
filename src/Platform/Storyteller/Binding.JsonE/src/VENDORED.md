# Vendored JsonE.Net

This project is a source copy of `JsonE.Net` 3.0.1 with a metering hook for Storyteller object bindings.
See `docs/Platform/Storyteller/specs/2026-10-09 Generic evaluation meter for object bindings.md`.

| | |
|---|---|
| Upstream | https://github.com/json-everything/json-everything, folder `src/JsonE` |
| Commit | `8b8ab34027de5ad9f4ed50808b8e4889ca69cf4d` (the commit recorded in the `JsonE.Net` 3.0.1 nuspec) |
| License | MIT, copyright .NET Foundation and Contributors. See `LICENSE`. The NuGet binaries carry the Open Source Maintenance Fee EULA; a source build is MIT only. |
| Namespaces | Unchanged (`Json.JsonE.*`). Nothing in the solution may reference both this project and the `JsonE.Net` package. |
| Assembly | `42.Platform.Storyteller.Binding.JsonE` |

## Removed from upstream

- `JsonE.csproj` (replaced by `Binding.JsonE.csproj`, `net10.0` only, no strong-name key, no `netstandard2.0` polyfills).
- `assembly.cs` (`InternalsVisibleTo` for the upstream test assembly).
- `README.md`.

## Patch

Every patched line calls `Metering` or `OrdinalSearch`, or is marked `Storyteller patch`. `grep -rn "Metering\|OrdinalSearch\|Storyteller patch"` lists the whole patch.

| File | Change |
|---|---|
| `Metering.cs` | New. Public `IEvaluationMeter` (`Tick`, `EnterFrame`, `ExitFrame`, `Reserve`). Internal `[ThreadStatic]` `Metering.Current`, `Metering.Tick()`, `Metering.Frame()`, `Metering.ReserveChars()`. With no meter they are no-ops. |
| `JsonE.cs` | New public overload `Evaluate(JsonNode? template, JsonNode? context, IEvaluationMeter? meter)`. It sets `Metering.Current` for the call and restores the previous value. `Evaluate(JsonNode?, EvaluationContext)` ticks and opens a frame. `MaybeEvaluateChildren` ticks after each object property and array item. `Interpolate` ticks per `${` hole, and every `string.Replace` goes through `ReplaceReserved`, which counts the occurrences and reserves the size of the result first (`string.Replace` replaces all occurrences in one call). |
| `OrdinalSearch.cs` | New. Public `OrdinalSearch.Contains(source, value)`: ordinal substring search with a linear worst case (Knuth–Morris–Pratt for needles longer than 64 characters, `string.Contains` otherwise). `string.Contains` costs \|source\| × \|value\| on periodic inputs (`abab…` in `abab…aa`: 0.6 s for 400,000 / 200,000 characters, without allocating). `Binding.Object` uses it for JSON Logic `in` too. |
| `Expressions/Operators/InOperator.cs` | String `in` calls `OrdinalSearch.Contains` instead of `string.Contains`. Same answer. |
| `Expressions/Functions/JoinFunction.cs` | Reserves the running joined length per part, before `string.Join` builds the result. |
| `Operators/JsonOperator.cs` | Reserves an upper bound of the serialized size (6 characters per string character, for worst-case escaping) from an iterative walk over the nodes, before serializing. |
| `Expressions/ExpressionParser.cs` | `TryParse` ticks and opens a frame. Every operand parser (unary, array, object, function arguments, index and slice) recurses through `TryParse`. |
| `Expressions/*ExpressionNode.cs` | Every `Evaluate` override ticks. The recursive ones (array, binary, function, object, unary, value accessor) also open a frame. `BuildString` of binary, unary and function nodes opens a frame, because error messages print expressions. |
| `Expressions/Functions/RangeFunction.cs` | Ticks per produced item. `range` is the only builtin whose output size is set by an argument instead of by its input. |

No other behavior is changed. With a `null` meter the code behaves like `JsonE.Net` 3.0.1.

## Audits (at 3.0.1)

- **Output larger than the memory already allocated for the input.** Copies of a string value share one .NET string instance (`JsonValue.DeepClone`), so the logical size of a value can be far larger than what it cost to build: `[s,s,…]` costs one small node per item. A primitive whose output is bounded only by the logical size of its input must reserve before it builds. Of the 20 builtins (`abs`, `ceil`, `defined`, `floor`, `fromNow`, `join`, `len`, `lowercase`, `lstrip`, `max`, `min`, `number`, `range`, `rstrip`, `split`, `sqrt`, `str`, `strip`, `typeof`, `uppercase`), the 16 template operators, and the expression operators, these are:
  - `range`: output size set by a number. Ticks per item.
  - `join`, `$json`, and `${}` interpolation: flatten many (possibly shared) strings into one. Reserve before building.

  Every other primitive works on one string or on arrays and objects, which are deep-copied (and metered) whenever they are reused; string `+` takes two operands, so its result is at most twice the largest string already paid for. `**` uses `Math.Pow` (constant time; an overflow throws). Every operator loop (`$map`, `$reduce`, `$find`, `$sort` `by`, `$match`, `$switch`) evaluates its body through a ticking entry point.
- **CPU larger than linear in the input, without allocating.** The meter cannot interrupt one call, so its CPU must also be bounded by its paid-for input. The only such primitive is substring search: string `in`, now `OrdinalSearch`. `split`, `lowercase`, `uppercase`, `strip`, `len`, indexing, slicing, and `number` scan their string linearly; `==` and array `in` compare element by element.
- **Catch blocks.** `SpanExtensions.TryParseValue` catches around literal parsing that never reaches a meter. `SortOperator` catches `InvalidOperationException` and rethrows its inner exception. Neither can swallow a meter exception.

## Re-sync

1. Copy upstream `src/JsonE/**/*.cs` from the new commit over `src` (keep `Metering.cs`, `Binding.JsonE.csproj`, `LICENSE`, `VENDORED.md`; delete `assembly.cs`).
2. Re-apply the patch above (the previous diff of this folder is the reference).
3. Repeat both audits.
4. Run `dotnet test src/Platform/Storyteller/Binding.JsonE/test` (JSON-e specification suite) and the `Binding.Object` tests.
5. Update the commit in this file and in the JSON-e specification file header.
