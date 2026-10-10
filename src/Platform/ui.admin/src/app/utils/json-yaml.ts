import { parse as parseYaml, stringify as stringifyYaml, YAMLParseError } from 'yaml'
import { parse as parseJsonc, printParseErrorCode, type ParseError } from 'jsonc-parser'

export type DocumentFormat = 'json' | 'yaml'

export type ParseResult =
  { ok: true; value: unknown; warning?: string } | { ok: false; message: string; line?: number; column?: number }

/** JSON with 2-space indentation; key order is kept as it is in the value (spec section 9.2). */
export function toJson(value: unknown): string {
  return JSON.stringify(value ?? null, null, 2)
}

/** YAML 1.2 core schema, plain strings and keys, no line wrapping. */
export function toYaml(value: unknown): string {
  return stringifyYaml(value ?? null, {
    indent: 2,
    lineWidth: 0,
    defaultStringType: 'PLAIN',
    defaultKeyType: 'PLAIN',
  })
}

export function serialize(value: unknown, format: DocumentFormat): string {
  return format === 'json' ? toJson(value) : toYaml(value)
}

/** Integer literals beyond 2^53 lose precision in JavaScript; detected in the text so the user can be warned. */
export function hasUnsafeInteger(text: string): boolean {
  for (const match of text.matchAll(/(?<![\w."])-?\d{16,}(?![\w."])/g)) {
    if (!Number.isSafeInteger(Number(match[0]))) {
      return true
    }
  }
  return false
}

const unsafeIntegerWarning = 'A number is larger than 2^53 and loses precision; store it as a string.'

function positionToLine(text: string, position: number): { line: number; column: number } {
  const before = text.slice(0, position)
  const line = before.split('\n').length
  return { line, column: position - before.lastIndexOf('\n') }
}

function parseJson(text: string): ParseResult {
  try {
    const value = JSON.parse(text) as unknown
    return { ok: true, value, warning: hasUnsafeInteger(text) ? unsafeIntegerWarning : undefined }
  } catch (error) {
    // V8 messages no longer carry a position; jsonc-parser (strict options) finds where the syntax breaks.
    const errors: ParseError[] = []
    parseJsonc(text, errors, { disallowComments: true, allowTrailingComma: false, allowEmptyContent: false })
    const first = errors[0]
    const message = first
      ? `${printParseErrorCode(first.error)
          .replace(/([a-z])([A-Z])/g, '$1 $2')
          .toLowerCase()}`
      : error instanceof Error
        ? error.message
        : String(error)
    return first
      ? {
          ok: false,
          message: message.charAt(0).toUpperCase() + message.slice(1),
          ...positionToLine(text, first.offset),
        }
      : { ok: false, message }
  }
}

function parseYamlText(text: string): ParseResult {
  try {
    const value = parseYaml(text, { schema: 'core', uniqueKeys: true, prettyErrors: true }) as unknown
    return { ok: true, value: value ?? null, warning: hasUnsafeInteger(text) ? unsafeIntegerWarning : undefined }
  } catch (error) {
    if (error instanceof YAMLParseError) {
      const position = error.linePos?.[0]
      return { ok: false, message: error.message.split('\n')[0]!, line: position?.line, column: position?.col }
    }
    return { ok: false, message: error instanceof Error ? error.message : String(error) }
  }
}

export function parseDocument(text: string, format: DocumentFormat): ParseResult {
  if (text.trim().length === 0) {
    return { ok: false, message: 'The document is empty.', line: 1, column: 1 }
  }
  return format === 'json' ? parseJson(text) : parseYamlText(text)
}
