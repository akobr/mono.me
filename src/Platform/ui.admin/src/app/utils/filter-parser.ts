import type { Annotation } from '@42for.net/2splatform.sdk'
import { annotationStatus, type AnnotationStatus } from './annotation-status'
import { codeOfKey, type AnnotationTypeCode, type ParsedAnnotationKey } from './annotation-key'

// GitHub-style filter language (spec section 8):
//   query      := term (WS term)*
//   term       := ['-'] (qualifier ':' value | text)
//   value      := word | '"' chars '"' | value ',' value        (comma = OR)

export const qualifiers = [
  'type',
  'label',
  'is',
  'has',
  'key',
  'subject',
  'responsibility',
  'context',
  'unit',
  'ancestor',
] as const

export type Qualifier = (typeof qualifiers)[number]

export interface FilterTerm {
  /** The term as typed, used to rebuild the query when a chip is removed. */
  raw: string
  negated: boolean
  /** Undefined for free text. */
  qualifier?: Qualifier
  values: string[]
  error?: string
}

export interface ParsedFilter {
  terms: FilterTerm[]
  errors: string[]
}

export interface FilterContext {
  now: number
  /** Keys with a configuration; null when unknown (has:config then matches nothing). */
  configured: ReadonlySet<string> | null
  /** All ancestors of a key (any order). */
  ancestorsOf: (key: string) => readonly string[]
  /** Parsed key parts (cached by the catalog). */
  partsOf: (key: string) => ParsedAnnotationKey | undefined
}

export const typeAliases: Record<string, AnnotationTypeCode> = {
  rst: 'rst',
  responsibility: 'rst',
  unt: 'unt',
  unit: 'unt',
  sbt: 'sbt',
  subject: 'sbt',
  usg: 'usg',
  usage: 'usg',
  cnt: 'cnt',
  context: 'cnt',
  exe: 'exe',
  execution: 'exe',
  uxe: 'uxe',
  'unit-of-execution': 'uxe',
  unitofexecution: 'uxe',
}

const statuses: AnnotationStatus[] = ['active', 'disabled', 'scheduled', 'expired']
const hasValues = ['config', 'labels', 'values', 'docs', 'description'] as const

/** Splits on whitespace outside double quotes. */
export function tokenize(query: string): string[] {
  const tokens: string[] = []
  let current = ''
  let quoted = false

  for (const char of query) {
    if (char === '"') {
      quoted = !quoted
      current += char
    } else if (/\s/.test(char) && !quoted) {
      if (current) {
        tokens.push(current)
      }
      current = ''
    } else {
      current += char
    }
  }

  if (current) {
    tokens.push(current)
  }

  return tokens
}

/** Splits a value on commas outside quotes and removes the quotes. */
function splitValues(value: string): string[] {
  const values: string[] = []
  let current = ''
  let quoted = false

  for (const char of value) {
    if (char === '"') {
      quoted = !quoted
    } else if (char === ',' && !quoted) {
      values.push(current)
      current = ''
    } else {
      current += char
    }
  }

  values.push(current)
  return values.map((entry) => entry.trim()).filter((entry) => entry.length > 0)
}

function validate(qualifier: Qualifier, values: string[]): string | undefined {
  if (values.length === 0) {
    return `${qualifier}: needs a value.`
  }

  const lower = values.map((value) => value.toLowerCase())

  switch (qualifier) {
    case 'type': {
      const unknown = lower.filter((value) => !(value in typeAliases))
      return unknown.length ? `Unknown type ${unknown.join(', ')}.` : undefined
    }
    case 'is': {
      const unknown = lower.filter((value) => !statuses.includes(value as AnnotationStatus))
      return unknown.length ? `is: accepts ${statuses.join(', ')}.` : undefined
    }
    case 'has': {
      const unknown = lower.filter((value) => !(hasValues as readonly string[]).includes(value))
      return unknown.length ? `has: accepts ${hasValues.join(', ')}.` : undefined
    }
    default:
      return undefined
  }
}

export function parseFilter(query: string): ParsedFilter {
  const terms: FilterTerm[] = []

  for (const raw of tokenize(query)) {
    const negated = raw.length > 1 && raw.startsWith('-')
    const body = negated ? raw.slice(1) : raw
    const match = /^([a-z-]+):(.*)$/i.exec(body)
    const qualifier = match?.[1]?.toLowerCase()

    if (match && (qualifiers as readonly string[]).includes(qualifier!)) {
      const values = splitValues(match[2]!)
      terms.push({
        raw,
        negated,
        qualifier: qualifier as Qualifier,
        values,
        error: validate(qualifier as Qualifier, values),
      })
    } else {
      const text = body.replaceAll('"', '').trim()
      const error = match && !body.startsWith('"') ? `Unknown qualifier ${match[1]}: (searched as text).` : undefined
      if (text) {
        terms.push({ raw, negated, values: [text], error })
      }
    }
  }

  return { terms, errors: terms.flatMap((term) => (term.error ? [term.error] : [])) }
}

/** Rebuilds the query without one term (removing a chip). */
export function removeTerm(parsed: ParsedFilter, index: number): string {
  return parsed.terms
    .filter((_, position) => position !== index)
    .map((term) => term.raw)
    .join(' ')
}

/** Adds or replaces a qualifier term (for example from a legend or a quick filter). */
export function setQualifier(query: string, qualifier: Qualifier, values: string[]): string {
  const kept = parseFilter(query).terms.filter((term) => term.qualifier !== qualifier || term.negated)
  const quoted = values.map((value) => (/[\s,]/.test(value) ? `"${value}"` : value))
  return [...kept.map((term) => term.raw), ...(values.length ? [`${qualifier}:${quoted.join(',')}`] : [])].join(' ')
}

function globToRegExp(glob: string): RegExp {
  const escaped = glob.replaceAll(/[.+?^${}()|[\]\\]/g, '\\$&').replaceAll('*', '.*')
  return new RegExp(`^${escaped}$`, 'i')
}

type Predicate = (annotation: Annotation) => boolean

function termPredicate(term: FilterTerm, context: FilterContext): Predicate {
  const values = term.values.map((value) => value.toLowerCase())

  switch (term.qualifier) {
    case 'type': {
      const codes = new Set(values.map((value) => typeAliases[value]).filter(Boolean))
      return (annotation) => codes.has(codeOfKey(annotation.AnnotationKey)!)
    }
    case 'label':
      return (annotation) => (annotation.Labels ?? []).some((label) => values.includes(label.toLowerCase()))
    case 'is':
      return (annotation) => values.includes(annotationStatus(annotation, context.now))
    case 'has':
      return (annotation) =>
        values.some((value) => {
          switch (value) {
            case 'config':
              return context.configured?.has(annotation.AnnotationKey) ?? false
            case 'labels':
              return (annotation.Labels?.length ?? 0) > 0
            case 'values':
              return Object.keys(annotation.Values ?? {}).length > 0
            case 'docs':
              return !!annotation.DocumentationLink
            case 'description':
              return !!annotation.Description?.trim()
            default:
              return false
          }
        })
    case 'key': {
      const matchers = term.values.map((value) =>
        value.includes('*')
          ? globToRegExp(value)
          : { test: (key: string) => key.toLowerCase().startsWith(value.toLowerCase()) },
      )
      return (annotation) => matchers.some((matcher) => matcher.test(annotation.AnnotationKey))
    }
    case 'subject':
    case 'responsibility':
    case 'context':
    case 'unit': {
      const part = term.qualifier
      return (annotation) => {
        const parsed = context.partsOf(annotation.AnnotationKey)
        // A subject's (responsibility's) own name is its subject (responsibility) part.
        const own =
          (part === 'subject' && parsed?.code === 'sbt') || (part === 'responsibility' && parsed?.code === 'rst')
            ? parsed?.name
            : parsed?.[part]
        return !!own && values.includes(own.toLowerCase())
      }
    }
    case 'ancestor':
      return (annotation) =>
        context.ancestorsOf(annotation.AnnotationKey).some((key) => values.includes(key.toLowerCase()))
    default:
      return (annotation) =>
        values.every((value) =>
          [annotation.Name, annotation.AnnotationKey, annotation.Title, annotation.Description].some((field) =>
            field?.toLowerCase().includes(value),
          ),
        )
  }
}

/**
 * Compiles a parsed filter into one predicate: every included term must match (AND), no negated term may
 * match. Terms with errors are ignored, so a half-typed query does not hide everything.
 */
export function compileFilter(parsed: ParsedFilter, context: FilterContext): Predicate {
  const usable = parsed.terms.filter((term) => !term.error || !term.qualifier)
  const include = usable.filter((term) => !term.negated).map((term) => termPredicate(term, context))
  const exclude = usable.filter((term) => term.negated).map((term) => termPredicate(term, context))

  if (include.length === 0 && exclude.length === 0) {
    return () => true
  }

  return (annotation) =>
    include.every((predicate) => predicate(annotation)) && !exclude.some((predicate) => predicate(annotation))
}
