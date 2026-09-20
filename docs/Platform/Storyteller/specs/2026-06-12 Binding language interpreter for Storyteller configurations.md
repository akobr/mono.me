
# Binding Language Interpreter for Storyteller Configurations
## Problem
Replace the regex\-based configuration data binding with a proper interpreter pipeline \(tokenizer, parser, evaluator\) in a new project, and extend the syntax to support statements, function calls, string interpolation, and math expressions\.
## Current state
* Binding is detected when a JSON string property value starts with `@`\. `CosmosConfigurationService.GetResolvedConfigurationInternalAsync` walks the configuration `JObject` and calls `TryProcessDataBinding` \(`src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs:641` and `:701`\)\.
* `BindingService` uses one compiled `Regex` to validate and dispatch \(`src/Platform/Storyteller/Binding.Core/src/BindingService.cs (10-53)`\)\. It supports `@path.to.prop`, `@(path, source)`, and has an unimplemented `@word(param)` branch\.
* Strategies currently implement `IBindingStrategy.TryBinding(BindingContext)` and mutate `property.Value`; `KeyVaultBindingStrategy` gates on `IncludeSecrets`, maps `.` to `--`, and writes the secret into the property \(`src/Platform/Storyteller/Binding.Azure.KeyVault/src/KeyVaultBindingStrategy.cs`\)\.
* DI is wired through `Binding.Core/EntryPoint.AddConfigurationBindings` and `Binding.Azure.KeyVault/EntryPoint.AddAzureKeyVaultBindings`, used in `src/Platform/Storyteller/Api.Functions/src/Program.cs:72`\.
* Conventions: `net10.0`, central package versions, StyleCop, xUnit \+ FluentAssertions, layout `Name/src/*.csproj` and `Name/test/Name.UnitTests.csproj`, registered in `42.mono.slnx`\.
## Decisions from Q&A
* Deliver the full interpreter, phased as tokenizer, parser, evaluator, and replace `BindingService` end to end\.
* JSONPath/JPointer arguments use double\-quoted string literals, e\.g\. `@path("$.store.book[0]")`\.
* Math operators are `+ - * / %`; `*` is multiplication\.
* Identifiers are `[A-Za-z_][A-Za-z0-9_]*`; number literals allow decimals such as `3.14`\.
* Source resolution returns values through a new value\-returning resolver contract; `IBindingExecutor` remains the JSON entry point; KeyVault is updated\.
* Values starting with `@` throw descriptive syntax/evaluation exceptions on malformed input, including property path and character offset\.
* Interpolation `@[ ... ]` uses backslash escapes `\@`, `\]`, and `\\`; whitespace is preserved verbatim\.
* Add one new `Binding.Language` project for lexer, parser, evaluator, and the executor; keep `Binding.Abstractions`; reuse `Binding.Core` for DI/options\.
## Grammar
The whole property value is one binding and begins with `@`\.
```text
Binding       := Statement | Interpolation | Math
Statement     := '@' ( Sourced | Function | Path )
Sourced       := '(' Path ',' Identifier ')'
Function      := Identifier '(' [ Arg { ',' Arg } ] ')'
Arg           := Statement | Path | StringLiteral
Path          := Identifier { '.' Identifier }
Identifier    := [A-Za-z_][A-Za-z0-9_]*
StringLiteral := '"' { '\\' any | not('"','\\') } '"'
Interpolation := '@[' { Text | Statement } ']'
Text          := chars; '\\' escapes \@ \] \\ ; unescaped '@' starts a Statement; unescaped ']' ends
Math          := '@{' Expr '}'
Expr          := Term { ('+'|'-') Term }
Term          := Factor { ('*'|'/'|'%') Factor }
Factor        := Number | Statement | '(' Expr ')'
Number        := [0-9]+ ('.' [0-9]+)?
```
Rules: expressions never nest; only statements may appear inside `@[...]` and `@{...}`\. Statements may nest because a function argument may itself be a statement\. `@[` and `@{` are valid only at top level; nested `@` begins a statement\. A single `Identifier` is a one\-segment `Path`\.
## New project structure
Add `src/Platform/Storyteller/Binding.Language/src/Binding.Language.csproj` and `src/Platform/Storyteller/Binding.Language/test/Binding.Language.UnitTests.csproj`, both registered under `/Platform/Storyteller/` in `42.mono.slnx`\.
`Binding.Language` references `Binding.Abstractions` and `Newtonsoft.Json`, uses root namespace `_42.Platform.Storyteller.Binding.Language`, and contains the tokenizer, parser, evaluator, AST, syntax exceptions, and `BindingExecutor : IBindingExecutor`\.
## Tokenizer plan
* Implement `TokenType` and immutable `Token(type, lexeme, start, length)` carrying offsets for diagnostics\.
* Token types: `At`, `OpenInterpolation`, `OpenMath`, `LParen`, `RParen`, `Comma`, `Dot`, `Identifier`, `Number`, `StringLiteral`, `Plus`, `Minus`, `Star`, `Slash`, `Percent`, `Text`, `CloseBracket`, `CloseBrace`, `Eof`\.
* Use a mode\-stack lexer with Default, Interpolation, Math, and Statement contexts\.
* Default/Statement mode scans identifiers, dots, parens, commas, string literals, and top\-level `@`, `@[`, `@{`\.
* Interpolation mode emits decoded `Text` until unescaped `@` or `]`; unescaped `@` switches temporarily to Statement mode; `]` ends interpolation\.
* Math mode scans numbers, arithmetic tokens, parens, and statement starts; `}` ends math\.
* `@[` and `@{` are recognized only at top level; inside interpolation or math an `@` always starts a statement, never another expression\.
* Throw `BindingSyntaxException` with offset on illegal characters, unterminated strings, unterminated expressions, unsupported escapes, or unmatched delimiters\.
## Parser plan
* Implement a recursive\-descent parser over the token stream\.
* AST nodes: `PathStatement`, `SourcedStatement`, `FunctionStatement`, `PathNode`, `StringLiteralNode`, `InterpolationExpression`, `TextPart`, `StatementPart`, `MathExpression`, `NumberNode`, `BinaryNode`, `StatementOperand`\.
* Parse math with standard precedence: `* / %` above `+ -`, left associative, parentheses for grouping\.
* Parse function arguments as statement, path, or string literal; validate separator and closing tokens with position\-aware errors\.
* Reject recursive expressions and unexpected expression tokens in statement arguments\.
## Evaluator and contracts
* Add value\-returning contracts to `Binding.Abstractions`: `IBindingSource`, `IBindingFunction`, `BindingRequest`, `BindingFunctionRequest`, and `BindingValue` wrapping a `JToken`\.
* Extend or replace `IBindingRegistry` to register sources by key and functions by name\. Keep `IBindingExecutor.TryBinding(JProperty, bool includeSecrets)` unchanged as the entry point\.
* `BindingEvaluator` resolves `@path` via the default source, `@(path, source)` via the named source, and `@word(...)` via the function registry\.
* Interpolation evaluates statement parts and concatenates stringified values with literal text\.
* Math evaluates numeric literals and numeric statement values as `decimal`, emitting a JSON number; non\-numeric operands and divide/modulo by zero throw descriptive evaluation exceptions\.
## Integration plan
* Replace the regex `BindingService` role with the new `BindingExecutor` pipeline: if value does not start with `@`, return false; otherwise tokenize, parse, evaluate, and assign `property.Value`\.
* `Binding.Core` keeps service registration/options and is updated to register `BindingExecutor`, sources, and functions\. The regex service is removed or reduced to a compatibility wrapper\.
* Convert `KeyVaultBindingStrategy` into a value\-returning source while preserving `IncludeSecrets` behavior and `.` to `--` secret\-name mapping\.
* `CosmosConfigurationService.TryProcessDataBinding` remains a thin `@` guard around `IBindingExecutor`; include property path in thrown errors\.
* Fix the array branch in `GetResolvedConfigurationInternalAsync`, where string array items currently call `TryProcessDataBinding(property, includeSecrets)` on the parent property instead of the actual item \(`src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs:683`\)\.
## Testing plan
* Lexer tests: all syntax forms, mode transitions, escaped interpolation text, string literals, identifiers, paths, decimals, delimiter errors, and exact offsets\.
* Parser tests: AST shape for each binding kind, nested statement arguments, function arguments, math precedence/associativity/grouping, and parse failures\.
* Evaluator tests: fake default/named sources, fake functions, unresolved values, `IncludeSecrets`, interpolation concatenation, math success/failure cases, and JSON token output types\.
* Integration tests: `BindingExecutor` over `JProperty` samples, unchanged non\-binding strings, throw\-with\-property\-path behavior, and KeyVault source contract behavior with a fake client if feasible\.
## Open decisions for review
* Function roots: whether `@path("$...")` and `@pointer("/...")` query the current resolved configuration document, a source, or a caller\-provided root object\.
* Whether to fully remove `IBindingStrategy`/`BindingContext` or keep deprecated compatibility shims for one release\.
* Interpolation stringification for object/array values: use compact JSON, formatted JSON, or reject non\-scalar values\.
* Unary minus is not included; negative numbers require `0 - value` unless we extend the grammar\.
## Phases
1. Scaffold `Binding.Language` src/test projects and register them in `42.mono.slnx`; add new contracts/exceptions to `Binding.Abstractions`\.
2. Implement tokenizer and lexer tests\.
3. Implement AST/parser and parser tests\.
4. Implement evaluator and evaluator tests with fake sources/functions\.
5. Integrate executor, DI/options, KeyVault source conversion, and Cosmos traversal/array fix\.
6. Validate with targeted unit tests, Storyteller build, and repository lint/style checks\.
