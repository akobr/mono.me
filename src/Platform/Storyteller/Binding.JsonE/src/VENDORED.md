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

Every patched line calls `Metering`. `grep -rn Metering` lists the whole patch.

| File | Change |
|---|---|
| `Metering.cs` | New. Public `IEvaluationMeter` (`Tick`, `EnterFrame`, `ExitFrame`). Internal `[ThreadStatic]` `Metering.Current`, `Metering.Tick()`, `Metering.Frame()`. With no meter both are no-ops. |
| `JsonE.cs` | New public overload `Evaluate(JsonNode? template, JsonNode? context, IEvaluationMeter? meter)`. It sets `Metering.Current` for the call and restores the previous value. `Evaluate(JsonNode?, EvaluationContext)` ticks and opens a frame. `MaybeEvaluateChildren` ticks after each object property and array item. `Interpolate` ticks per `${` hole. |
| `Expressions/ExpressionParser.cs` | `TryParse` ticks and opens a frame. Every operand parser (unary, array, object, function arguments, index and slice) recurses through `TryParse`. |
| `Expressions/*ExpressionNode.cs` | Every `Evaluate` override ticks. The recursive ones (array, binary, function, object, unary, value accessor) also open a frame. `BuildString` of binary, unary and function nodes opens a frame, because error messages print expressions. |
| `Expressions/Functions/RangeFunction.cs` | Ticks per produced item. `range` is the only builtin whose output size is set by an argument instead of by its input. |

No other behavior is changed. With a `null` meter the code behaves like `JsonE.Net` 3.0.1.

## Audits (at 3.0.1)

- **Output larger than input.** Of the 20 builtins (`abs`, `ceil`, `defined`, `floor`, `fromNow`, `join`, `len`, `lowercase`, `lstrip`, `max`, `min`, `number`, `range`, `rstrip`, `split`, `sqrt`, `str`, `strip`, `typeof`, `uppercase`) and the 16 template operators, only `range` builds output whose size is set by a number. `**` uses `Math.Pow` (constant time; an overflow throws). Every operator loop (`$map`, `$reduce`, `$find`, `$sort` `by`, `$match`, `$switch`) evaluates its body through a ticking entry point.
- **Catch blocks.** `SpanExtensions.TryParseValue` catches around literal parsing that never reaches a meter. `SortOperator` catches `InvalidOperationException` and rethrows its inner exception. Neither can swallow a meter exception.

## Re-sync

1. Copy upstream `src/JsonE/**/*.cs` from the new commit over `src` (keep `Metering.cs`, `Binding.JsonE.csproj`, `LICENSE`, `VENDORED.md`; delete `assembly.cs`).
2. Re-apply the patch above (the previous diff of this folder is the reference).
3. Repeat both audits.
4. Run `dotnet test src/Platform/Storyteller/Binding.JsonE/test` (JSON-e specification suite) and the `Binding.Object` tests.
5. Update the commit in this file and in the JSON-e specification file header.
