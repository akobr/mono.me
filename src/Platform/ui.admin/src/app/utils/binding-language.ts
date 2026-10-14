import {
  annotationTypes,
  buildAnnotationKey,
  parseAnnotationKey,
  type AnnotationTypeCode,
  type BindingCatalog,
  type BindingExample,
  type BindingFunctionDescriptor,
  type BindingSourceDescriptor,
} from '@42for.net/2splatform.sdk'
import { isEnvelope } from './bindings'

/**
 * A small model of the `@` binding language (docs/Platform/Storyteller/binding.md) for completion, hover and the
 * binding reference (424-10). It never evaluates anything: the server is the only judge of an expression.
 */

export interface BindingSyntax {
  id: 'path' | 'sourced' | 'function' | 'interpolation' | 'math' | 'jlogic' | 'jsone'
  title: string
  /** One or two sentences, inline Markdown. */
  summary: string
  syntax: string
  examples: BindingExample[]
}

export interface BindingCatalogView {
  /** The string forms (`@…`). */
  expressions: BindingSyntax[]
  /** The object forms (`$binding` envelopes). */
  envelopes: BindingSyntax[]
  functions: BindingFunctionDescriptor[]
  sources: BindingSourceDescriptor[]
  /** The functions and sources come from the server; otherwise they are the fallback every deployment has. */
  known: boolean
}

export const builtInExpressions: readonly BindingSyntax[] = [
  {
    id: 'path',
    title: 'Path',
    summary: 'A value of the `default` source. Paths are names joined by dots; a name has letters, digits and `_`.',
    syntax: '@path.to.value',
    examples: [
      { Expression: '@database.connectionString', Description: '`database.connectionString` of the default source.' },
    ],
  },
  {
    id: 'sourced',
    title: 'Sourced path',
    summary: 'A value of a named source, for example an Azure Key Vault.',
    syntax: '@(path.to.value, source)',
    examples: [
      { Expression: '@(db.password, primaryVault)', Description: 'The secret `db--password` of `primaryVault`.' },
    ],
  },
  {
    id: 'function',
    title: 'Function call',
    summary:
      'Calls a registered function. Text the function reads itself must be a quoted string: a bare path is read from the default source first.',
    syntax: '@name("text", path, @statement)',
    examples: [{ Expression: '@config("/maxPrice")', Description: 'The `maxPrice` value of the same configuration.' }],
  },
  {
    id: 'interpolation',
    title: 'Interpolation',
    summary:
      'Text with statements inside. Escape a literal `@`, `]` or `\\` with a backslash. Statements cannot contain another interpolation or math.',
    syntax: '@[text @statement text]',
    examples: [
      { Expression: '@[https://@config("/host")/api]', Description: 'A URL built from the `host` value.' },
      { Expression: '@[Hello @annotation("/name")]', Description: 'A greeting with the annotation name.' },
    ],
  },
  {
    id: 'math',
    title: 'Math',
    summary: 'A number from `+ - * / %` and parentheses. Operands are numbers or statements that resolve to numbers.',
    syntax: '@{(@statement + 10) * 2}',
    examples: [
      { Expression: '@{@config("/timeoutMs") * 2}', Description: 'Twice the `timeoutMs` value.' },
      { Expression: '@{(@config("/base") + 10) % 7}', Description: 'Operators follow the usual precedence.' },
    ],
  },
]

export const builtInEnvelopes: readonly BindingSyntax[] = [
  {
    id: 'jlogic',
    title: 'JSON Logic',
    summary:
      'An object with `"$binding": "jlogic"` is replaced by the result of the [JSON Logic](https://jsonlogic.com/) rule in `$definition`. `$context` is resolved first and is the data of the rule.',
    syntax: '{ "$binding": "jlogic", "$definition": { … }, "$context": { … } }',
    examples: [
      {
        Expression: JSON.stringify({
          $binding: 'jlogic',
          $definition: { if: [{ '<': [{ var: 'tier' }, 2] }, 1, 5] },
          $context: { tier: '@config("/plan/tier")' },
        }),
        Description: '1 retry for tier 1, 5 otherwise.',
      },
    ],
  },
  {
    id: 'jsone',
    title: 'JSON-e',
    summary:
      'An object with `"$binding": "jsone"` is replaced by the [JSON-e](https://json-e.js.org/) template in `$definition`, rendered with `$context`.',
    syntax: '{ "$binding": "jsone", "$definition": { … }, "$context": { … } }',
    examples: [
      {
        Expression: JSON.stringify({
          $binding: 'jsone',
          $definition: { host: { $eval: 'host' }, port: 5432 },
          $context: { host: '@config("/endpoints/db")' },
        }),
        Description: 'An object built from the `endpoints.db` value.',
      },
    ],
  },
]

const ancestorTypeNames = annotationTypes.map((info) => info.type).filter((type) => type !== 'UnitOfExecution')

/** `config` and `annotation`: registered by every deployment, shown until the catalog of the server arrives. */
export const fallbackFunctions: readonly BindingFunctionDescriptor[] = [
  {
    Name: 'annotation',
    Summary: 'Reads a field of the `Values` of the annotation being configured, or of one of its ancestors.',
    Signature: 'annotation("<expr>"[, "<type>"])',
    Parameters: [
      {
        Name: 'expr',
        Description: "A quoted JSON Pointer (`/a/b`) or JSONPath (`$.a.b`) into the annotation's `Values`.",
        IsOptional: false,
        Values: [],
      },
      {
        Name: 'type',
        Description: 'A quoted annotation type name. Reads the ancestor of that type instead of the annotation itself.',
        IsOptional: true,
        Values: ancestorTypeNames,
      },
    ],
    Examples: [
      { Expression: '@annotation("/owner")', Description: 'The `owner` value of the annotation being configured.' },
      {
        Expression: '@annotation("/currency", "Subject")',
        Description: 'The `currency` value of the subject the annotation belongs to.',
      },
    ],
  },
  {
    Name: 'config',
    Summary: 'Reads a value of the configuration being resolved, from the snapshot taken before evaluation.',
    Signature: 'config("<expr>")',
    Parameters: [
      {
        Name: 'expr',
        Description: 'A quoted JSON Pointer (`/a/b`) or JSONPath (`$.a.b`) into the configuration.',
        IsOptional: false,
        Values: [],
      },
    ],
    Examples: [
      { Expression: '@config("/maxPrice")', Description: 'The value of `maxPrice` in the same configuration.' },
      { Expression: '@config("$.endpoints.db")', Description: 'The same lookup written as JSONPath.' },
    ],
  },
]

/** The catalog shown to the user: the built-in forms and the functions and sources of the server (or the fallback). */
export function catalogView(server?: BindingCatalog): BindingCatalogView {
  return {
    expressions: [...builtInExpressions],
    envelopes: [...builtInEnvelopes],
    functions: server ? (server.Functions ?? []) : [...fallbackFunctions],
    sources: server?.Sources ?? [],
    known: !!server,
  }
}

export type BindingKind = 'path' | 'sourced' | 'function' | 'interpolation' | 'math' | 'invalid'

export interface BindingExplanation {
  kind: BindingKind
  /** The function or source name, when there is one. */
  name?: string
  title: string
  /** Inline Markdown. */
  summary: string
  /** The call shape or the syntax of the form. */
  syntax?: string
  example?: BindingExample
  /** A likely mistake; a hint only, the server stays the judge and saving is never blocked. */
  problem?: string
}

const name = '[A-Za-z_][A-Za-z0-9_]*'
const pathPattern = `${name}(?:\\.${name})*`
const sourcedPattern = new RegExp(`^\\(\\s*(${pathPattern})\\s*,\\s*(${name})\\s*\\)$`)
const leadingPath = new RegExp(`^${pathPattern}`)

function syntaxOf(id: BindingSyntax['id']): BindingSyntax {
  return builtInExpressions.find((entry) => entry.id === id)!
}

/** Whether `text` ends with `closing` that is not escaped by a backslash. */
function endsWithUnescaped(text: string, closing: string): boolean {
  if (!text.endsWith(closing)) {
    return false
  }
  let slashes = 0
  for (let index = text.length - 2; index >= 0 && text[index] === '\\'; index--) {
    slashes++
  }
  return slashes % 2 === 0
}

/** Names of the functions called anywhere in the text (`@name(`), for nested statements. */
function calledFunctions(text: string): string[] {
  return [...text.matchAll(new RegExp(`(?<!\\\\)@(${name})\\(`, 'g'))].map((match) => match[1]!)
}

function unknownFunction(names: string[], catalog: BindingCatalogView): string | undefined {
  if (!catalog.known) {
    return undefined
  }
  const unknown = names.find((called) => !catalog.functions.some((entry) => entry.Name === called))
  return unknown ? `No function \`${unknown}\` is registered.` : undefined
}

/**
 * Classifies a stored binding string (it starts with `@`) and finds likely mistakes: a `/` in a path, an unknown
 * function or source, a missing closing bracket, or an unquoted argument of a function that reads text.
 */
export function explainBinding(text: string, catalog: BindingCatalogView): BindingExplanation | undefined {
  if (!text.startsWith('@')) {
    return undefined
  }

  const body = text.slice(1)
  const first = body[0]

  if (first === '(') {
    const syntax = syntaxOf('sourced')
    const match = sourcedPattern.exec(body)
    if (!match) {
      return {
        kind: 'invalid',
        title: syntax.title,
        summary: syntax.summary,
        syntax: syntax.syntax,
        problem: 'Write a sourced path as `@(path.to.value, source)`: names joined by dots, then the source name.',
      }
    }
    const source = catalog.sources.find((entry) => entry.Name === match[2])
    return {
      kind: 'sourced',
      name: match[2],
      title: source?.IsSecret ? 'Secret source' : 'Source',
      summary: source?.Summary || syntax.summary,
      syntax: syntax.syntax,
      example: source?.Examples?.[0] ?? syntax.examples[0],
      problem: catalog.known && !source ? `No source \`${match[2]}\` is registered.` : undefined,
    }
  }

  if (first === '[' || first === '{') {
    const kind = first === '[' ? 'interpolation' : 'math'
    const syntax = syntaxOf(kind)
    const closed = kind === 'interpolation' ? endsWithUnescaped(body, ']') : body.endsWith('}')
    return {
      kind: closed ? kind : 'invalid',
      title: syntax.title,
      summary: syntax.summary,
      syntax: syntax.syntax,
      example: syntax.examples[0],
      problem: closed
        ? unknownFunction(calledFunctions(body), catalog)
        : kind === 'interpolation'
          ? 'An interpolation ends with `]`. Escape a literal `]` as `\\]`.'
          : 'A math expression ends with `}`.',
    }
  }

  const path = leadingPath.exec(body)?.[0]
  if (!path) {
    return {
      kind: 'invalid',
      title: 'Binding',
      summary: 'A binding starts with a name, `(`, `[` or `{` right after `@`.',
      problem: body
        ? `\`${body[0]}\` cannot follow \`@\`. Start with a name, \`(\`, \`[\` or \`{\`.`
        : 'Nothing follows `@`.',
    }
  }

  const rest = body.slice(path.length)

  if (rest.startsWith('(') && !path.includes('.')) {
    return explainCall(path, rest, catalog)
  }

  const syntax = syntaxOf('path')
  if (rest) {
    return {
      kind: 'invalid',
      title: syntax.title,
      summary: syntax.summary,
      syntax: syntax.syntax,
      problem: `\`${rest[0]}\` cannot follow \`${path}\`. A path is names joined by dots, for example \`a.b.c\`.`,
    }
  }

  const fallback = catalog.sources.find((entry) => entry.IsDefault)
  return {
    kind: 'path',
    title: 'Path in the default source',
    summary: fallback?.Summary || syntax.summary,
    syntax: syntax.syntax,
    example: syntax.examples[0],
    problem:
      catalog.known && !fallback
        ? 'No `default` source is registered, so the value stays as it is. Did you mean a function such as `@config("/…")`?'
        : undefined,
  }
}

function explainCall(called: string, rest: string, catalog: BindingCatalogView): BindingExplanation {
  const entry = catalog.functions.find((candidate) => candidate.Name === called)
  const source = entry ? undefined : catalog.sources.find((candidate) => candidate.Name === called)
  // A server where a source can be called by its name (`@secret(db.password)`) lists it as a source, not a function.
  if (source) {
    return {
      kind: 'sourced',
      name: called,
      title: source.IsSecret ? 'Secret source' : 'Source',
      summary: source.Summary || syntaxOf('sourced').summary,
      syntax: `@${called}(path.to.value)`,
      example: source.Examples?.[0],
      problem: rest.endsWith(')') ? undefined : 'A source call ends with `)`.',
    }
  }

  const syntax = syntaxOf('function')
  const explanation: BindingExplanation = {
    kind: 'function',
    name: called,
    title: 'Function',
    summary: entry?.Summary || syntax.summary,
    syntax: entry?.Signature ?? syntax.syntax,
    example: entry?.Examples?.[0] ?? syntax.examples[0],
  }

  if (!rest.endsWith(')')) {
    return { ...explanation, kind: 'invalid', problem: 'A function call ends with `)`.' }
  }

  if (catalog.known && !entry) {
    return { ...explanation, problem: `No function \`${called}\` is registered.` }
  }

  // A function whose signature quotes its first parameter reads text: a bare path would be resolved first.
  const argument = rest.slice(1).trimStart()
  if (entry?.Signature?.includes('("') && argument && argument[0] !== '"' && argument[0] !== '@' && argument !== ')') {
    return {
      ...explanation,
      problem: `Quote the argument: \`@${called}("${argument.slice(0, -1).split(',')[0]!.trim()}")\`. A bare path is read from the default source first.`,
    }
  }

  return { ...explanation, problem: unknownFunction(calledFunctions(rest), catalog) }
}

export type BindingCompletionContext =
  | { kind: 'start'; nested: boolean; word: string }
  | { kind: 'source'; word: string }
  | { kind: 'argument'; fn: string; index: number; word: string }
  | { kind: 'none' }

const argumentPattern = new RegExp(`(?<!\\\\)@(${name})\\(\\s*((?:"[^"]*"\\s*,\\s*)*)"([^"]*)$`)
const sourcePattern = new RegExp(`(?<!\\\\)@\\(\\s*${pathPattern}\\s*,\\s*([A-Za-z0-9_]*)$`)
const startPattern = /(?<!\\)@([A-Za-z_][A-Za-z0-9_]*)?$/

/**
 * What may follow the text before the cursor: `prefix` is the decoded binding string from its `@` to the cursor.
 * - `start`: right after `@` (also inside an interpolation or math, then `nested`), with the name typed so far;
 * - `source`: the source name of `@(path, …`;
 * - `argument`: inside the quoted argument `index` of a function call;
 * - `none`: anything else.
 */
export function bindingCompletionContext(prefix: string): BindingCompletionContext {
  if (!prefix.startsWith('@')) {
    return { kind: 'none' }
  }

  const argument = argumentPattern.exec(prefix)
  if (argument) {
    const before = argument[2] ?? ''
    return { kind: 'argument', fn: argument[1]!, index: (before.match(/"[^"]*"/g) ?? []).length, word: argument[3]! }
  }

  const source = sourcePattern.exec(prefix)
  if (source) {
    return { kind: 'source', word: source[1]! }
  }

  const start = startPattern.exec(prefix)
  if (start) {
    return { kind: 'start', nested: start.index > 0, word: start[1] ?? '' }
  }

  return { kind: 'none' }
}

/** The quoted arguments of the first call of `fn` in a binding string, for example the type of `@annotation`. */
export function quotedArguments(text: string, fn: string): string[] {
  const call = new RegExp(`@${fn}\\(([^)]*)\\)`).exec(text)
  return call ? [...call[1]!.matchAll(/"([^"]*)"/g)].map((match) => match[1]!) : []
}

/** How a `"` of the language is written inside a string delimited by `delimiter` (JSON or YAML). */
export function quoteFor(delimiter: '"' | "'" | ''): string {
  return delimiter === '"' ? '\\"' : '"'
}

function escapePointerSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

/**
 * The JSON Pointers of a document, breadth first, up to `limit`. Containers come before their children, and the
 * inside of a binding envelope is skipped (its `$definition` is a program, not data).
 */
export function pointersOf(value: unknown, limit = 200): string[] {
  const result: string[] = []
  let level: Array<[string, unknown]> = [['', value]]

  while (level.length > 0 && result.length < limit) {
    const next: Array<[string, unknown]> = []
    for (const [pointer, current] of level) {
      if (current === null || typeof current !== 'object' || isEnvelope(current)) {
        continue
      }
      const entries = Array.isArray(current)
        ? current.map((item, index) => [String(index), item] as const)
        : Object.entries(current as Record<string, unknown>)
      for (const [key, child] of entries) {
        if (result.length >= limit) {
          break
        }
        const childPointer = `${pointer}/${escapePointerSegment(key)}`
        result.push(childPointer)
        next.push([childPointer, child])
      }
    }
    level = next
  }

  return result
}

/**
 * The key of the ancestor of `type` (a type name, case-insensitive like the server), mirroring
 * `AnnotationKeyExtensions.TryGetAncestorKey`: undefined when the type is not an ancestor; the key itself for its
 * own type; a unit of execution is never an ancestor.
 */
export function ancestorKeyOf(key: string, type: string): string | undefined {
  const parsed = parseAnnotationKey(key)
  const info = annotationTypes.find((candidate) => candidate.type.toLowerCase() === type.trim().toLowerCase())
  if (!parsed || !info || info.type === 'UnitOfExecution') {
    return undefined
  }

  try {
    const { subject, responsibility, context, unit } = parsed
    return buildAnnotationKey(info.type, { subject, responsibility, context, unit })
  } catch {
    return undefined
  }
}

/** The ancestor types `@annotation("…", "<type>")` accepts for a key (all of them when the key is unknown). */
export function ancestorTypesOf(key: string | undefined): string[] {
  return key ? ancestorTypeNames.filter((type) => ancestorKeyOf(key, type) !== undefined) : [...ancestorTypeNames]
}

/** The same for every annotation of a type (a template), through a key of that type with placeholder names. */
export function ancestorTypesOfType(typeOrCode: string): string[] {
  try {
    const parts = { subject: 's', responsibility: 'r', context: 'c', unit: 'u', name: 'n' }
    return ancestorTypesOf(buildAnnotationKey(typeOrCode as AnnotationTypeCode, parts))
  } catch {
    return [...ancestorTypeNames]
  }
}
