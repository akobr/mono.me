# Binding parser recursion has no depth limit, and 422 binding errors can quote secrets

Linear: [424-99](https://linear.app/424fun/issue/424-99/binding-parser-recursion-has-no-depth-limit-and-422-binding-errors-can).

## Overview

This review covers all of [Binding parser recursion has no depth limit, and 422 binding errors can quote secrets](../2026-10-31%20Binding%20parser%20recursion%20has%20no%20depth%20limit%2C%20and%20422%20binding%20errors%20can%20quote%20secrets.md), delivered in one branch.

- **No stored binding can overflow the stack any more.** An `@` string is at most 8,192 characters and 64 levels deep, with the counting rules of the MCP reader. The evaluator walks operator chains in a loop. A JSONPath query of `@config` and `@annotation` is bounded as well.
- **Every failure of the content of a binding answers `422`.** Before, a syntax error, a non-number operand or a failed `@config` answered `500` with the stack trace and the raw expression, because the executor rewrapped every failure as a plain `BindingException`.
- **Messages name kinds, never values.** That covers the `@` language, the forwarded engine messages, and failures inside the result of an envelope, which are reported at the path of the stored envelope. The middleware logs code and path only.

The review of the branch found that the spec had missed a path with the same crash: a JSONPath query of `@config`, computed (`@config(@config("/q"))`) or literal, overflowed the stack inside Newtonsoft, while parsing and while evaluating. It also found that computed keys and definitions of an envelope's result still carried secrets. All of this is fixed in section 12.

This review closes the "binding parser" item under "Left for later" in [the MCP review](2026-10-24%20MCP%20Server%20for%20the%20Storyteller%20API.md).

| Commit | Content |
|---|---|
| `44f86e87` | The spec |
| `f2f5e6a9` | Sections 1 to 5: the limits, the length cap, the parser and evaluator guards, the executor that keeps the type; Binding.Language tests |
| `a360ad07` | Sections 6 to 8: value-free messages, `EngineMessages`, the middleware log; language, object and end-to-end tests |
| `e4562a0d` | Section 10: the docs |
| `20b61a25` | Fixes from the first review round (section 12) |
| `84178a33` | Fixes from the second review round (section 12) |
| this commit | This review |

## What Was Done

### 1. The limits of an `@` string

`Binding.Language/src/BindingExpressionLimits.cs` follows the spec: `MaxLength = 8 * 1024` and `MaxDepth = 64`, with the counting rules in the XML docs.

- `EvaluationLimitKind.ExpressionLength` is appended. The `Depth` doc and the summaries of the enum and of `EvaluationLimitExceededException` now say "binding read".
- `ExceptionExtensions` gives `binding.limit.expressionLength` without a change. Nothing in the SDKs or the conformance vectors lists the limit codes one by one.
- The MCP reader stays as it is. Each side pins the shared values in a test (`Limits_ArePinnedToTheMcpReadersValues`, `Limits_MatchTheApi`), because no project references both.

### 2. The length cap in the executor

As planned: `BindingExecutor.BindAsync` refuses a string longer than 8,192 characters before tokenizing it, with `ExpressionLength`, the path and a message without the expression.

### 3. The depth guard of the parser

- `Enter()` increments one counter and throws `Depth` past 64 or when `RuntimeHelpers.TryEnsureSufficientExecutionStack()` fails.
- `ParseStatement` became a wrapper with `Enter()` and a `finally` around the old body, now `ParseStatementBody`. The spec planned the `try` inline. The wrapper keeps the old body unchanged, at one extra frame per level.
- The parenthesis branch of `ParseFactor` counts too.
- Number literals use `decimal.TryParse` with `NumberStyles.Number`, the style `decimal.Parse` used. A literal past `decimal` is the syntax error `The number is too large.` at the literal's offset.

### 4. The evaluator

- `EvaluateChainAsync` collects the left spine into a `Stack<BinaryNode>`, evaluates the leftmost operand, then applies each operator with its right operand. A test with a recording source pins the order of evaluation.
- The statement depth is a parameter of `EvaluateStatementAsync`, `InvokeFunctionAsync` and `EvaluateArgumentAsync`. The stack probe is at the entry of statement and numeric evaluation.
- All guards throw the same message as the parser: `The expression nests deeper than 64 levels or than the stack allows.`

### 5. The executor keeps the type

As planned: a limit stays an `EvaluationLimitExceededException` and every other `BindingException` becomes a `BindingEvaluationException`, both with the path and `Failed to process the data binding for '{path}': {inner}`. The resolver's unreachable plain rewrap keeps the evaluation type too.

### 6. Messages name kinds, never values (`@` language)

- `ToDecimal`: `A math operand of type '{kind}' is not a number.`
- `JsonQuery`:
  - no message quotes the query;
  - a JSON Pointer escape names its offset in the whole pointer;
  - `ArgumentException`, `FormatException` and `OverflowException` of Newtonsoft answer `The JSONPath query is not valid.`, and a timeout answers `The JSONPath query took too long to evaluate.`
  - The review added `IndexOutOfRangeException` and `InvalidCastException`, and bounds on the query itself (section 12).
- `AnnotationBindingFunction` names the accepted types. Beyond the spec, it also refuses a number that is not an `AnnotationType`. Before, `Enum.TryParse` accepted `"424242"`, whose digits then appeared in the "has no ancestor" message.

### 7. Object-binding engine messages

`Binding.Object/src/EngineMessages.cs` follows the spec. Implementation choices and later changes:

- **Whole words, not substrings.** `unknown context value X` keeps `X` only when `X` is a **whole word** of the `$definition`. The spec said "occurs in". A substring test would keep any one-letter prefix of a secret, because the serialized definition always contains letters such as those of `$eval`.
- **Linear check.** The review made the check one pass over the definition and skips names longer than 256 characters (section 12).
- **Emitted envelopes.** An envelope emitted by an earlier result forwards no JSON-e text at all (`JSON-e evaluation failed.`), from the review (section 12).
- JSON Logic failures answer `JSON Logic evaluation failed.`, and other engine exceptions answer `Object binding evaluation failed ({Type}).`, as planned.

### 8. The middleware log

As planned: `Binding limit {Kind} ({Limit}) exceeded ({ErrorCode}) at {Path}.` or `Binding evaluation failed ({ErrorCode}) at {Path}.`, with no message and no exception object. The `422` body is unchanged.

### 9. Tests

- **Binding.Language** (127 → 192):
  - `BindingLimitsTests` (30) covers:
    - the boundaries of length and depth, and the counter that statements and parentheses share;
    - wrappers and siblings;
    - the ticket's 10,000-level stream and 10,000 parentheses, without a crash;
    - number literals;
    - the executor's types, paths and messages;
    - the secret operand;
    - flat, left-deep and right-deep chains (the right-deep chain of 100,000 nodes hits the stack probe), and hand-built statements;
    - the unchanged order of evaluation.
  - `JsonQueryLimitsTests` (29) is from the review:
    - the bounds of a query, with literal and computed deep filters and chains of segments through `@config`;
    - the refused exponential shapes, next to a root comparison and a regex anchor that stay allowed;
    - queries that end inside an indexer;
    - filters that compare incompatible types;
    - the catastrophic `=~` filter that now times out;
    - `BigInteger` operands.
  - `JsonQueryTests` checks the value-free messages.
  - Two existing tests are updated for the new type and message.
- **Binding.Object** (219 → 238): `EngineMessagesTests` covers:
  - each of the four JSON-e messages, reached with a sentinel in `$context`;
  - the whole-word rule;
  - authored names that are kept;
  - JSON Logic failures and unexpected exceptions;
  - from the review: the linear check on a 900 KB definition, envelopes emitted from a secret (computed kind, property, definition and property path), and data bindings under a computed key, for both evaluation and limit failures.
- **Backend.CosmosDb**: `AnnotationBindingFunctionTests` checks that an unknown type is not quoted.
- **Api.Functions** (+6, in `ExceptionHandlingMiddlewareTests`): a new `CapturingLogger<T>` records the messages, the structured values and the exceptions. The tests run a real `ConfigurationBindingResolver` and `BindingExecutor` through the middleware, with no hand-made exception. The cases:
  - a secret operand in math;
  - a secret in a JSON-e `$context`;
  - a failing binding under a key computed from a secret;
  - the 40 KB value;
  - 65 levels;
  - a syntax error.

  Every case answers `422` with its code, and neither the body nor the log holds the secret or the expression.
- **Mcp**: `Limits_MatchTheApi`.

### 10. Docs

- `binding.md`:
  - the "Limits of a string" section, with the counting rules and examples;
  - the failures that `IBindingExecutor` throws;
  - in Errors: the new code, the `422` of every failure of an `@` string, the message rules (with JSON Logic worded as the code has it), the reporting inside results and the log;
  - the bounds of a JSONPath query in the `@config` section;
  - the project structure.
- `mcp.md` says that the API refuses the same expressions.
- The historical MCP review is not edited (see the Overview).

### 11. Differences from the CodeRabbit coding plan

Section 11 of the spec still holds. The review added three more differences:

- the plan did not cover JSONPath queries, emitted envelopes or computed paths;
- it would have changed the vendored fork;
- it would have refused valid expressions with its numeric depth counter.

### 12. The review of the branch

The branch had three review rounds. Each finding was checked by a skeptic who tried to refute it and reproduced it with a scratch harness against the committed code.

**First round.** Five reviewers covered the limits, secrets, status codes, tests and docs, and attacks. All confirmed findings are fixed in `20b61a25`.

| Finding | Fix |
|---|---|
| A computed JSONPath query (`@config(@config("/q"))`, 22 characters, with a 30 KB query in the document) overflowed the stack inside Newtonsoft's JPath parser. So did a literal one within the cap on a 1 MB thread. Pre-existing, but the same crash as the ticket. | `JsonQuery` refuses a query longer than 8,192 characters (`ExpressionLength`), or with more than 64 `[` and `(` in total (`Depth`). Counting every bracket and parenthesis bounds the recursion even when quotes would hide a closing one. |
| The new `RegexMatchTimeoutException` catch could never fire. Without settings, Newtonsoft matches `=~` with `Regex.InfiniteMatchTimeout`, which ignores the process default, and Api.Functions sets no default anyway. The spec's premise was wrong. | `SelectToken(expression, new JsonSelectSettings { RegexMatchTimeout = 200 ms })` (`JsonQuery.RegexMatchTimeout`). |
| Queries that end inside an indexer or a filter (`$[ `, `$[?(-`) threw `IndexOutOfRangeException`, which answered `500`. | Added to the caught types. |
| An operand past `long` is a `BigInteger` in Newtonsoft, so `Convert.ToDecimal` threw `InvalidCastException` (`500`). Cosmos returns integers between 2^63 and 2^64 as such. | `(decimal)big`. A value past `decimal` throws `OverflowException`, which becomes the existing `422` numeric overflow. |
| The resolver walks an envelope's result again. A result built from a secret could quote it in three places: as the `$binding` kind or a property name of an emitted envelope (`Unknown object binding '…'`, `Unexpected property '…'`); in the computed definition of an emitted envelope (`unknown context value <secret>`, which passed the whole-word rule, or `object has no property "<secret>"`); and as a computed key, which reached the `422` message and the middleware's `Warning` log as `Path`. | `ConfigurationBindingResolver` passes `emittedBy`, the path of the stored envelope, through the walk of every result (the root re-walk, arrays, nested envelopes and their `$context`). Inside a result:<br>- every failure is reported at that path;<br>- a data binding fails with `a data binding in its result cannot be evaluated` or `… exceeds the <kind> limit of <limit>`, keeping `Kind` and `Limit`;<br>- `ObjectBindingEngine` forwards no JSON-e text.<br>The messages of a malformed envelope are fixed everywhere. |
| `IsWordOf` was quadratic and ran outside the engine meter: 24 s of CPU per read for a 900 KB definition, against 67 ms before the branch. | One pass over maximal word runs, length first, and names longer than 256 characters are skipped. |
| binding.md said every JSON Logic failure answers the fixed message. An `OverflowException` from JSON Logic arithmetic gets the type-named one. | Reworded. |
| The parity test of the limits compared against literals only, so the MCP side could drift. | Pinned on both sides. |

**Second round, on `20b61a25`.** Three reviewers covered the resolver walk, the JSONPath bounds and the remaining leaks. Fixed in `84178a33`:

| Finding | Fix |
|---|---|
| Newtonsoft also recurses once per path segment while it evaluates, because each `.name` chains a lazy filter that the first match runs through. A literal `@config("$.a.a…")` of exactly 8,192 characters, with no brackets, still overflowed the stack, and so did a computed one. | `JsonQuery` counts `.`, `[` and `(` together, at most 64. The worst case is about 60 KB of stack. |
| Chained recursive descents (`$..a..a…` over a 34-deep document, 106 characters, minutes of CPU) and nested root references in filters (`$[?($[?($[?(…)])])]`, N^k) cost exponential time with a short query. Pre-existing. | A query may use `..` once, and `$` once after its start. That keeps a root comparison such as `[?(@.price < $.maxPrice)]`, which a test covers, and the end anchor of a regular expression. |
| A filter that compares a date (Newtonsoft reads ISO strings as dates on the API's reads) or a `BigInteger` with a number or a boolean threw `InvalidCastException`, which answered `500`. | Added to the caught types. |
| binding.md presented the fixed messages inside a result as a closed list. | Reworded as examples. |

The second round also found that a `?format=` read can put a key computed from a secret into its `422` message, its `Details.Path` and a `Warning` log, because a format error is not a binding error. The bug is older than this branch and lives in `ConfigurationHttp.TryFormat` and the formats contract, which the conformance vectors pin. It is listed under "Left for later".

**Third round, on `84178a33`.** Two reviewers attacked the JSONPath bounds for stack overflows, super-quadratic time and uncaught exception types. They built harnesses on 1 MB threads and with documents of up to 4 MB, and found no query within the bounds that crashes, costs more than the accepted residual time, or escapes as a `500`.

The reviewers of the first round rejected two findings after verification:

- **Quoting by Newtonsoft.** Newtonsoft's JSONPath messages quote one character of a query. A query that starts with `$`, as many hashed secrets do, resolves without an error.
- **Memory amplification.** An 8 KB interpolation of `@config("")` copies the whole document hundreds of times. This happens at the base commit too, and it is listed below.

## Validation

All runs are on Windows, at `84178a33`:

| Test project | Result |
|---|---|
| `Binding.Language.UnitTests` | 192/192 |
| `Binding.Object.UnitTests` | 238/238 |
| `Binding.JsonE.UnitTests` | 2429/2429 (the fork is unchanged) |
| `Api.Functions.UnitTests` | 2609/2609 |
| `Mcp.UnitTests` (`BindingExpressionsTests`) | 52/52 |
| `Backend.CosmosDb.UnitTests` (`AnnotationBindingFunctionTests`, `KeyValueBindingTests` with the Cosmos emulator) | 22/22 |

The reviewers reproduced every confirmed finding with scratch harnesses against the committed code, outside the worktree, before the fixes.

## Behaviour changes to note

- **`@` string failures answer `422` instead of `500`.** A syntax error, a non-number operand, a failed `@config` or `@annotation`, or a limit answers `422` with `binding.evaluation` or `binding.limit.*`. The .NET configuration provider treats a `422` as definitive, so it keeps the cached document and stops retrying such a read. The conformance vectors already expect this.
- **New code `binding.limit.expressionLength`.**
- **Fewer details in messages.** The JSON-e messages that quoted values, JSON Logic messages, unexpected engine exceptions, and failures inside an envelope's result now have fixed texts.
- **Bounded JSONPath queries.** These queries are refused:
  - a query longer than 8,192 characters;
  - a query with more than 64 `.`, `[` and `(`;
  - a query with two recursive descents;
  - a query that uses `$` twice after its start.

  A `=~` filter also times out after 200 ms per value.
- **The OpenAPI document does not change**, so no SDK needs to be regenerated.

## Left for later

- **JSONPath evaluation is bounded, not metered.** The bounds stop crashes and exponential shapes, but some costs remain:
  - a filter can still cost time proportional to the square of a large document, for example `$.*[?(@ == $..x)]` over an object of 100,000 keys;
  - a `=~` filter can take up to 200 ms per value.

  A budget per query, like the one of the name queries of 424-97, needs hooks that Newtonsoft does not offer. A JSONPath evaluator of our own, with a step meter, would close this.
- **A `?format=` 422 on the resolved read can quote a computed key.** `ConfigurationHttp.TryFormat` logs `{Path}` at `Warning` and returns the path in `Message` and `Details.Path`. On a resolved read, a key that a binding computed, even from a secret, can be part of it. `sform` prints the same before any mask under `--github-env`. A fix would report the nearest stored ancestor, as the resolver now does with `emittedBy`. That changes the formats contract, which the conformance vectors pin, so it belongs in its own change.
- **Memory amplification.** One `@` string within the cap can multiply memory. For example, `@[@config("") @config("") …]` copies the whole document once per part (pre-existing).
- **Shared code with the MCP reader.** The MCP reader and the API share their limits only by pinned tests. A shared library would remove the copy.
- **`JsonLogicDebugLogger`** logs the values of the JSON Logic `log` rule at `Debug`, by design.
