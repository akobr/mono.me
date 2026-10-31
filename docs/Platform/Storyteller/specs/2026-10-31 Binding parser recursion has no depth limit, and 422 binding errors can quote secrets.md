# Binding parser recursion has no depth limit, and 422 binding errors can quote secrets

Linear: [424-99](https://linear.app/424fun/issue/424-99/binding-parser-recursion-has-no-depth-limit-and-422-binding-errors-can). Found while building the MCP server (424-84, review `reviews/2026-10-24 MCP Server for the Storyteller API.md`, "Left for later").

## Problem

1. **Unbounded recursion.** The parser of `Binding.Language` recurses with no depth limit: `ParseStatement` → `ParseArgument` → `ParseStatement` for nested calls, and `ParseExpression` → `ParseTerm` → `ParseFactor` → `ParseExpression` for math parentheses. The evaluator recurses the same way (`EvaluateStatementAsync` → `InvokeFunctionAsync` → `EvaluateArgumentAsync`, and `EvaluateNumericAsync` over the binary tree). Writes store binding strings without parsing them, and a stored value may be up to 4 MiB (`RequestBodyLimits.Document`). A value like `"@" + "f(@"×10000 + "x" + ")"×10000` (about 40 KB) makes a resolved read overflow the stack. A `StackOverflowException` cannot be caught: it takes down the Functions worker, and with it every request in flight. Any caller with `ConfigurationRead` can trigger it on every resolved read, before any secret right is checked.
2. **Secrets in error messages.** `BindingEvaluator.ToDecimal` puts the evaluated operand into its message (`Operand '{token}' is not a number.`). If the operand is a secret inside math, the error carries its plaintext. Other messages do the same with a value that a statement or a `$context` produced: the `JsonQuery` messages of `@config` and `@annotation`, the annotation-type message of `@annotation`, and four messages of the vendored JSON-e that `ObjectBindingEngine` forwards as they are. `ExceptionHandlingMiddleware` logs the message of every binding error at `Warning`.
3. **Found during the analysis: every failure of an `@` string answers `500`, not `422`.** `BindingExecutor.BindAsync` catches every `BindingException` and rethrows it as a plain `BindingException`, which the middleware does not map. `BindingSyntaxException` is not a `BindingEvaluationException` either. So a syntax error, a non-number operand, an unresolved operand or a failing `@config` ends in the middleware's catch-all:
   - it logs the exception object at `Error`;
   - it answers `500` with `Message`, which embeds the whole stored string (up to 4 MiB) and the inner message;
   - the body also holds `Error`, an `ExceptionWrapper` with the stack trace and the inner chain.

   The `ToDecimal` value thus reaches the response twice and an `Error` log. `binding.md` says that these failures answer `422`. A limit error thrown inside the executor would be downgraded to a `500` the same way, so this has to be fixed together with item 1.

## Current State

- **The only evaluation path.** It runs in Api.Functions:
  - `ConfigurationHttp.GetConfigurationResolved` (`Api.Functions/src/V1/ConfigurationHttp.cs:211-226`);
  - `CosmosConfigurationService.ResolveConfigurationAsync` (`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs:234`);
  - `ConfigurationBindingResolver.ResolveAsync` → `BindStringAsync` (`Binding.Object/src/ConfigurationBindingResolver.cs:258-287`);
  - `BindingExecutor.TryBinding` → `BindAsync` (`Binding.Language/src/BindingExecutor.cs:82-115`).

  Only Api.Functions registers bindings (`Program.cs:172`). Api.Web, the MCP server, the configuration provider and the CLI never evaluate bindings in-process.
- `Binding.Language/src/BindingExecutor.cs:93-106` builds a new `Tokenizer`, `Parser` and `BindingEvaluator` per `@` string. Its catch is `catch (BindingException exception) { throw new BindingException($"Failed to process the data binding '{raw}' for '{current.Path}': {exception.Message}", exception); }`. No length check.
- `Binding.Language/src/Parser.cs`: `ParseStatement` (:72), `ParseArgument` (:135), and the math rules `ParseExpression` / `ParseTerm` / `ParseFactor` (:186-238). The `+ -` and `* / %` chains are loops that build left-deep `BinaryNode` trees. The parenthesis branch of `ParseFactor` (:229-235) recurses. No depth or stack guard. `ParseFactor` also calls `decimal.Parse` on the number lexeme (:221): a literal above `decimal.MaxValue` throws `OverflowException`, which is not a `BindingException` and answers `500`.
- `Binding.Language/src/Tokenizer.cs` is iterative. `LexBalancedParentheses` (:148-211) keeps a depth counter and does not recurse. Its messages quote one character of the expression and an offset.
- `Binding.Language/src/BindingEvaluator.cs`:
  - `ToDecimal` (:83-92) is the value-quoting message of the issue;
  - `EvaluateStatementAsync` (:104), `InvokeFunctionAsync` (:141), `EvaluateArgumentAsync` (:189) and `EvaluateNumericAsync` (:254-279) recurse;
  - `EvaluateNumericAsync` evaluates `binary.Left` and then `binary.Right` recursively. A flat `@{1+1+…+1}` of 8,192 characters is a left-deep tree of about 4,000 nodes. The `ValueTask` methods complete synchronously, so each level costs several stack frames.
- `Binding.Language/src/BindingSyntaxException.cs` is `sealed` and derives from `BindingException`. Its message ends with ` (at offset N)`.
- `Binding.Language/src/JsonQuery.cs`: `'{expression}' is neither a JSON Pointer …` (:42-43), `Invalid JSONPath expression '{expression}': {exception.Message}` (:54-55), and `Invalid JSON Pointer escape at position {i} in token '{segment}' …` (:118-119). The expression is the first argument of `@config` or `@annotation`. `BindingFunctionArguments.RequireString` accepts any string, and `EvaluateArgumentAsync` evaluates path and statement arguments first. So `@config(@secret(db.password))` puts the secret into these messages.
- `Binding.Core/src/AnnotationBindingFunction.cs:97-102`: `'{typeName}' is not a known annotation type for function '{FunctionName}'.` The type name is the second argument and can be evaluated in the same way.
- `Binding.Abstractions/src/EvaluationLimitKind.cs`: `Steps`, `Time`, `Memory`, `Depth` (documented as "recursion depth of the interpreter, the expression parser, or the stack"), `Result`, `EnvelopeDepth`, `EnvelopeCount`. Its summary and `EvaluationLimitExceededException`'s say "object-binding read". `Api.Functions/src/ErrorHandling/ExceptionExtensions.cs:25` turns a kind into `binding.limit.<camelCaseKind>`.
- `Binding.Object/src/ObjectBindingEngine.cs:66-91`:
  - `BindingException` is rethrown;
  - `JsonEException` and `JsonLogicException` become `BindingEvaluationException(exception.Message, exception)`;
  - `InsufficientExecutionStackException` becomes a `Depth` limit;
  - any other exception becomes `Object binding evaluation failed: {exception.Message}`.
- **Vendored JSON-e messages that quote an evaluated value** (`Binding.JsonE/src`):
  - `Expressions/FunctionExpressionNode.cs:31`: `{functionNode} is not callable`. The callee's value, for example `pin()` with `pin = 1234`.
  - `ContextAccessor.cs:68`: `{name} is not a valid accessor`. Reached from `FunctionExpressionNode.cs:29` when the callee is a string value.
  - `EvaluationContext.cs:79`: `unknown context value {identifier}`. From `ContextAccessorExpressionNode` the identifier is authored. From `FunctionExpressionNode.cs:29` it is the callee's string value.
  - `Expressions/Functions/DefinedFunction.cs:20`: `'{str}' is not a valid context accessor`. The evaluated argument of `defined(x)`.

  The fork's parity tests (`Binding.JsonE/test/SpecificationParityTests.cs`) compare its exception messages with upstream JsonE.Net 3.0.1, and `VENDORED.md` allows no other change. The messages therefore stay in the fork.
- `Binding.Object/src/ConfigurationBindingResolver.cs`:
  - `:182-193` rewraps a limit with its kind, limit and path (the pattern to follow);
  - `:194-200` keeps a `BindingEvaluationException`;
  - `:201-206` rethrows any other `BindingException` as a plain one. That case is unreachable today, but it would answer `500`.
- `Api.Functions/src/ErrorHandling/ExceptionHandlingMiddleware.cs`:
  - `:31` catches `BindingEvaluationException`;
  - `:106-131` logs `"Binding evaluation failed ({ErrorCode}) at {Path}: {Message}"` at `Warning`, then answers `422` with `Message`, `Hint` and `ErrorCode`, without `Error`;
  - the catch-all `:39-62` logs the exception at `Error` and answers `500` with `Error`.
- **MCP reference.** `Mcp/src/Understanding/BindingExpressions.cs` limits its own reader:
  - **length:** `MaxLength = 8 * 1024` UTF-16 code units of the whole string, `@` included, checked before reading;
  - **depth:** `MaxDepth = 64`, with one counter that `Enter()` increments at the start of every statement (the outermost, nested arguments, interpolation parts, math operands and the innermost `@x`) and at every math `(`;
  - the `@[` and `@{` wrappers, a call's parentheses, bare path and string arguments, and operator chains add nothing;
  - the check is `_depth > MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack()`, so 64 levels are accepted and 65 refused.
- **Docs.** `docs/Platform/Storyteller/binding.md`:
  - :46 says statements "can nest arbitrarily";
  - :48 describes the syntax and evaluation exceptions;
  - :131 (Errors) lists the `binding.limit.*` codes and says that `@config` and `@annotation` failures answer `422`;
  - :153 lists the limit kinds.

  `mcp.md:331` states the MCP reader's limits. The MCP review lists both defects under "Left for later" (:176).
- **Tests.**
  - `Binding.Language/test`: `BindingExecutorTests.cs:106-117` (`@a.`, type and path only) and `BindingEvaluatorTests.cs:252-263` (non-number operand, type only).
  - `Binding.Object/test/ConfigurationBindingResolverTests.cs:343-366, 827-834` (engine messages forwarded, `*index out of bounds*`).
  - `Api.Functions/test/ExceptionHandlingMiddlewareTests.cs` has no binding case and logs to `NullLogger`.
  - No test runs a failing `@` string through the middleware.

## Proposed Changes

### 1. The limits of an `@` string

New `Binding.Language/src/BindingExpressionLimits.cs`:

```csharp
/// <summary>
/// The bounds of one <c>@</c> binding string. They are the bounds of the MCP server's reader
/// (<c>Mcp/src/Understanding/BindingExpressions.cs</c>), so both agree on which strings are too long or too deep.
/// </summary>
public static class BindingExpressionLimits
{
    /// <summary>The length of the whole string, the leading '@' included, in UTF-16 code units.</summary>
    public const int MaxLength = 8 * 1024;

    /// <summary>Nested statements and math parentheses (see <see cref="Parser"/>).</summary>
    public const int MaxDepth = 64;
}
```

Constants, not options: the issue asks for no configuration, and constants keep the API and the MCP reader identical. The MCP reader is not changed (it does not reference `Binding.Language`).

In `Binding.Abstractions/src/EvaluationLimitKind.cs`, append `ExpressionLength` ("The length of one `@` binding string, in UTF-16 code units."). Existing values do not move. Widen two docs:

- `Depth`: "Recursion depth of the interpreter or the expression parser (also the nesting of an `@` string), or the stack."
- The summaries of the enum and of `EvaluationLimitExceededException`: "binding read" instead of "object-binding read".

`ExceptionExtensions` gives `binding.limit.expressionLength` and `binding.limit.depth` without a change. The generic `binding.limit.<kind>` of `Definitions.cs:552` and the conformance vectors stay as they are.

### 2. The length cap in the executor

In `BindingExecutor.BindAsync`, after the `@` check and before `new Tokenizer(raw)`:

```csharp
if (raw.Length > BindingExpressionLimits.MaxLength)
{
    throw new EvaluationLimitExceededException(
        EvaluationLimitKind.ExpressionLength,
        BindingExpressionLimits.MaxLength,
        $"Failed to process the data binding for '{current.Path}': the expression is longer than {BindingExpressionLimits.MaxLength} characters.",
        current.Path,
        innerException: null);
}
```

8,192 characters are accepted and 8,193 refused, as in the MCP reader. The ticket's 40 KB value stops here, before any lexing.

### 3. The depth guard of the parser

In `Parser.cs`:

- Add a private `int _depth` and `Enter()`:

  ```csharp
  // A nested statement or math parenthesis: beyond MaxDepth, or close to the end of the thread's stack, parsing stops.
  private void Enter()
  {
      _depth++;
      if (_depth > BindingExpressionLimits.MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
      {
          throw DepthExceeded();
      }
  }
  ```

  `DepthExceeded()` returns `new EvaluationLimitExceededException(EvaluationLimitKind.Depth, BindingExpressionLimits.MaxDepth, "The expression nests deeper than 64 levels or than the stack allows.")`.

- `ParseStatement` calls `Enter()` first and decrements `_depth` in a `finally`. That covers the outermost statement, statement arguments, interpolation parts, math operands and the innermost `@x`.
- The parenthesis branch of `ParseFactor` calls `Enter()` after `(` and decrements in a `finally`.
- Nothing else counts. The `@[` and `@{` wrappers, a call's parentheses, path and string arguments, sibling statements and operator chains are not levels. These are the MCP rules: 64 nested statements (the innermost `@x` included) are accepted and 65 refused, and so are 64 and 65 parentheses.
- The 10,000-level stream of the ticket, given to the parser directly, now fails with `Depth` at level 65.
- **Number literals.** `ParseFactor` uses `decimal.TryParse`. A literal it cannot hold throws `BindingSyntaxException("The number is too large.", number.Start)` instead of an `OverflowException`, so it answers `422` like every other malformed string.

The parser throws the limit exception itself: it is a `BindingException`, so the class contract does not change. `BindingSyntaxException` stays as it is.

### 4. The evaluator: no recursion over operator chains, and a guard

In `BindingEvaluator.cs`:

- **Left spine without recursion.** `EvaluateNumericAsync` walks the left spine of a `BinaryNode` into a stack of nodes. It evaluates the leftmost operand, then pops each node and applies its operator with its right operand. The order of evaluation, left associativity, and the division-by-zero, modulo-by-zero and overflow handling do not change. Only right operands and statement operands recurse, and parentheses bound their depth. A flat chain of any length costs constant stack.
- **Statement depth.** The depth travels as a parameter, not a field, so one evaluator stays safe to share:
  - `EvaluateStatementAsync(statement, depth)`, `InvokeFunctionAsync(function, depth)` and `EvaluateArgumentAsync(argument, depth)` take it;
  - a statement argument is evaluated at `depth + 1`;
  - top-level statements, interpolation parts and math operands start at 1;
  - past `BindingExpressionLimits.MaxDepth` the evaluator throws the same `Depth` limit as the parser.

  The parser counts statements and parentheses on one counter, so a parsed expression never reaches this bound. It guards an AST built by hand and given to the public `BindingEvaluator`.
- **Stack probe.** `EvaluateStatementAsync` and `EvaluateNumericAsync` start with `if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw …Depth…`. This bounds right-deep trees built by hand, which no counter sees.

No depth counter for numeric nodes: the evaluator does not see parentheses. Numeric recursion is about twice the parser's depth for `1+2*(…)`, so the parser's limit would refuse valid expressions. The stack probe covers this case instead.

### 5. The executor keeps the type: `422` for every failure of the content

`BindingExecutor.BindAsync` replaces its single catch:

```csharp
catch (EvaluationLimitExceededException exception)
{
    throw new EvaluationLimitExceededException(
        exception.Kind,
        exception.Limit,
        $"Failed to process the data binding for '{current.Path}': {exception.Message}",
        current.Path,
        exception);
}
catch (BindingException exception)
{
    throw new BindingEvaluationException(
        $"Failed to process the data binding for '{current.Path}': {exception.Message}",
        current.Path,
        exception);
}
```

- The raw expression leaves the message. The path finds the stored value, and the inner messages carry no value after section 6.
- A syntax error, an evaluation error and a limit all reach the middleware's `422` branch:
  - a syntax or evaluation error with `binding.evaluation`;
  - a limit with `binding.limit.depth` or `binding.limit.expressionLength`.
- The only plain `BindingException`s in `src` are the two rewraps (here and in the resolver), so no infrastructure failure becomes a `422`. A source's own exceptions, for example a Key Vault `RequestFailedException`, are not `BindingException`s and still answer `500`.
- `ConfigurationBindingResolver.cs:201-206` rethrows a plain `BindingException` as a `BindingEvaluationException` with its path, for the same reason (the case is unreachable today).

### 6. Messages name kinds, never values (`@` language)

- **`BindingEvaluator.ToDecimal`.** `A math operand of type '{kind}' is not a number.`, where `kind` is the lower-cased `JTokenType` (`string`, `boolean`, `object`, `array`, `null`, …).
- **`JsonQuery`.**
  - `The query is neither a JSON Pointer (must start with '/') nor a JSONPath expression (must start with '$').`
  - `The JSONPath query is not valid: {exception.Message}` for a `JsonException`. Newtonsoft's parser messages quote at most one character, and a test pins that the query does not appear.
  - Newtonsoft can also throw exceptions other than `JsonException` for a query: `OverflowException` or `FormatException` for an array index it cannot convert, `ArgumentException` (`RegexParseException`) for an invalid `=~` pattern, and `RegexMatchTimeoutException` when a `=~` pattern runs into the process default timeout. Today they escape as `500`. `ResolveJsonPath` catches these types as well. The first three become the fixed `The JSONPath query is not valid.`, because a regex message quotes the pattern. A timeout becomes `The JSONPath query took too long to evaluate.`
  - `Invalid JSON Pointer escape at offset {offset} of the query: '~' must be followed by '0' or '1'.` The offset is computed in the whole pointer, instead of quoting the token.
- **`AnnotationBindingFunction`.** `The second argument of '{FunctionName}' is not a known annotation type ({names}).` The names are the `AnnotationType` names. The other messages name functions, positions and enum values only.
- **Kept.** The other messages of the evaluator, tokenizer, parser, `ConfigBindingFunction` and `BindingFunctionArguments` were checked. They quote node types, function names, operators, positions, or one character of the expression with its offset, and stay as they are.

### 7. Object-binding engine messages

In `Binding.Object`, a new internal `EngineMessages` decides what part of an engine's message may leave `ObjectBindingEngine`:

- **JSON-e.** The message is forwarded, except the four messages that quote an evaluated value (Current State). They are replaced:
  - `… is not callable` → `A called value is not a function.`
  - `'…' is not a valid context accessor` → `The argument of defined() is not a valid context accessor.`
  - `… is not a valid accessor` → `A called name is not a valid context accessor.`
  - `unknown context value X` keeps `X` only when `X` occurs in the authored `$definition`, the common typo case. Otherwise it becomes `unknown context value`.

  The matching is anchored to the start or end of the message, the way the fork builds it. A test per message pins the fork's wording: if the fork's text changes, that test fails.
- **JSON Logic.** The upstream library's messages are not all verified, and at least one quotes a value (``Cannot determine truthiness of `{node}`.``). It becomes the fixed `JSON Logic evaluation failed.`
- **Any other engine exception.** It becomes `Object binding evaluation failed ({ExceptionTypeName}).`, without the exception's message.
- **Inner exceptions are kept.** The `422` branch neither serializes nor logs them (section 8).

`ObjectBindingEngine.Evaluate` passes the definition to `EngineMessages` for the JSON-e case. The resolver keeps prefixing the path.

### 8. The middleware logs code and path only

`ExceptionHandlingMiddleware.ProcessBindingEvaluationExceptionAsync` drops `{Message}` from its `Warning`:

- an `EvaluationLimitExceededException`: `"Binding limit {Kind} ({Limit}) exceeded ({ErrorCode}) at {Path}."`;
- any other binding error: `"Binding evaluation failed ({ErrorCode}) at {Path}."`.

No exception object is attached. The `422` body does not change (`Message`, `Hint`, `ErrorCode`, no `Error`). After sections 5 to 7, its message comes only from sources without values.

### 9. Tests

**`Binding.Language/test`**

- `ParserTests`:
  - 64 nested `@f(…@x…)` statements parse, and 65 throw `EvaluationLimitExceededException` with `Kind == Depth` and `Limit == 64`;
  - 64 and 65 math parentheses behave the same, and so do statements and parentheses mixed on one counter;
  - siblings in an interpolation do not add up;
  - the ticket's 10,000-level stream and 10,000 parentheses throw `Depth` without crashing;
  - a number above `decimal.MaxValue` throws `BindingSyntaxException`.
- `BindingExecutorTests`:
  - exactly 8,192 characters are accepted;
  - 8,193 characters and the ticket's 40 KB value throw `ExpressionLength` with `Path == "settings.value"`, and the message does not contain the expression;
  - 65 levels under 8,192 characters throw `Depth` with the path;
  - a syntax error is a `BindingEvaluationException` with the path, without the expression in its message (the `@a.` test is updated);
  - a non-number secret operand gives a message without the value.
- `BindingEvaluatorTests`:
  - the non-number test checks the kind and the absence of the value;
  - a flat `@{1+1+…}` near 8,192 characters sums correctly;
  - a hand-built left-deep chain of 100,000 nodes sums without recursion;
  - a hand-built right-deep chain of 100,000 nodes throws `Depth` (stack probe) without crashing;
  - 65 hand-built nested statements throw `Depth`;
  - the evaluation order of a chain is unchanged (a recording source).
- `JsonQueryTests` and `ConfigBindingFunctionTests`: the messages do not contain an evaluated query.

**`Binding.Object/test`**

- Each JSON-e message of section 7, reached with a sentinel in `$context`, does not contain the sentinel. `unknown context value` keeps an authored name.
- A JSON Logic error gives the fixed message.
- The unexpected-exception test checks the fixed message.

**`Api.Functions/test`, end to end through the middleware.** A new `CapturingLogger<T>` records the level, the formatted message, the state values and the exception. `ExceptionHandlingMiddlewareTests` gets a `next` that runs a real `ConfigurationBindingResolver` with a real `BindingExecutor`, with no hand-made exception. The source double returns `SECRET_SENTINEL` only when `IncludeSecrets` is true. Cases:

- **`settings.value = "@{@value + 1}"`.** The source saw `IncludeSecrets == true`. The answer is `422` with `binding.evaluation`. Neither the serialized body (which has no `Error`) nor any captured message, state value or exception (inner exceptions excluded, since they are not logged) contains the sentinel.
- **A JSON-e envelope whose `$context` binds `@value` and whose definition calls it (`value()`).** The answer is `422` with `binding.evaluation`, and the same checks apply.
- **The ticket's 40 KB value.** `422` with `binding.limit.expressionLength`, and the body does not contain the expression.
- **65 levels.** `422` with `binding.limit.depth`.
- **A syntax error.** `422` with `binding.evaluation` (it was `500`).

### 10. Docs

- `docs/Platform/Storyteller/binding.md`:
  - the string-syntax section replaces "nest arbitrarily" with the two limits and the counting rules;
  - the description of errors (:48) says what the executor throws;
  - Errors (:131) adds `binding.limit.expressionLength`, states that syntax, evaluation and limit failures of `@` strings answer `422`, that messages name kinds and never values, and that the log carries code and path only;
  - the Abstractions list (:153) adds `ExpressionLength`.
- `docs/Platform/Storyteller/mcp.md:331`: the API enforces the same limits.
- The historical MCP review is not edited. The review of this work closes its item, as the review of 424-97 did.

### 11. Differences from the CodeRabbit coding plan

The plan attached to the issue was used as a second opinion. It agrees on the length cap, the depth guard with the MCP rules, the iterative left spine, keeping the exception type in the executor, the type-only `ToDecimal` message, and the log without the message. This spec differs in these points:

- **Numeric depth.** The plan gives the evaluator a depth counter at both statement and numeric entry, with the parser's limit of 64. Numeric recursion is not the parser's depth (`1+2*(…)` recurses twice per parenthesis), so that counter would refuse valid expressions. Here the evaluator counts statements only and probes the stack at both entries.
- **The counter is a parameter, not a field.** An evaluator stays safe to share.
- **The JSON-e messages.** The plan edits `DefinedFunction.cs` and `FunctionExpressionNode.cs` in the vendored fork. That fails the fork's parity tests with upstream and breaks `VENDORED.md`, and it misses two more sites (`ContextAccessor.cs:68`, `EvaluationContext.cs:79`). Here the redaction is in `Binding.Object`, and it keeps the helpful `unknown context value` for authored names.
- **Fixed messages for unexpected engine exceptions.** They keep the exception type name.
- **Also covered here:**
  - the `JsonQuery` and `@annotation` messages that quote an evaluated argument;
  - the `decimal.Parse` overflow of the parser and the non-`JsonException` failures of a JSONPath query (both `500` today);
  - the resolver's unreachable plain rewrap;
  - the end-to-end syntax-error case, which shows the `500` → `422` change.
- **The MCP review is not edited.** It is a historical document. The new review closes its item.

### Out of scope

- The tokenizer quotes one character of the expression with its offset. The expression is authored, except when a JSON-e or JSON Logic result emits an `@` string that the resolver walks again. In that case one character of a computed string can appear in a message.
- JSON paths built from keys that a JSON-e result computed appear in messages and in the log as `Path`.
- `JsonLogicDebugLogger` logs the values of the `log` rule at `Debug`, by design.
- JSONPath filters with `=~` run a regular expression on Newtonsoft's backtracking engine. They stay bounded only by the process default timeout of 200 ms per match. Section 6 turns a timeout into a `422`, but one query can still match many values.
- `ProcessClientErrorAsync` logs the message of domain errors. They are not binding errors and carry no evaluated values.
