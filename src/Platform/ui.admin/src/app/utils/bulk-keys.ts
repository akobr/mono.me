import { getAnnotationTypeInfo, typeMeta, type AnnotationTypeCode } from './annotation-key'

// The lines of Bulk create (424-33): one key, or one compact line, per line of the text. The reading is stricter
// than the API's (CosmosAnnotationService.CreateAnnotationsFromStringAsync), which drops empty parts, keeps spaces
// and reads a code with the wrong number of parts as a compact line; the dialog shows what each line becomes and
// sends only canonical keys, which the API reads back as the same key.

export type BulkLineProblem =
  | 'too-many-parts' // more than 4 parts without a code
  | 'wrong-parts' // starts with a type code, but the number of parts does not fit it
  | 'empty-part' // "a..b", ".a", "a."
  | 'invalid-name' // a name with a tab or another control character, or with / \ ? #

export interface BulkLine {
  /** 1-based line number in the text. */
  line: number
  /** The line as typed, trimmed. */
  text: string
  /** The type code (lower case) when the first part is one and there are more parts. */
  code?: AnnotationTypeCode
  /** The key the line stands for, with a lower-case code; undefined when the line has a problem. */
  key?: string
  problem?: BulkLineProblem
}

/** The type of a compact line by its number of parts, as in the API. */
const compactCodes: readonly AnnotationTypeCode[] = ['sbt', 'usg', 'exe', 'uxe']

/** Characters the Cosmos item id (`{view}.{key}`) cannot hold. */
const unsafeCharacters = ['/', '\\', '?', '#']

const isControl = (character: string) => {
  const code = character.charCodeAt(0)
  return code < 0x20 || code === 0x7f
}

/** The first character of a name that makes it invalid. */
const invalidCharacterOf = (name: string) =>
  [...name].find((character) => isControl(character) || unsafeCharacters.includes(character))

/** Splits the text on line breaks, trims the lines and drops blank ones; each line is read with readBulkLine. */
export function readBulkLines(text: string): BulkLine[] {
  return text
    .split(/\r?\n/)
    .map((line, index) => ({ line: index + 1, text: line.trim() }))
    .filter((entry) => entry.text.length > 0)
    .map((entry) => readBulkLine(entry.text, entry.line))
}

/** Reads one line into a key. */
export function readBulkLine(text: string, line: number): BulkLine {
  const trimmed = text.trim()
  const parts = trimmed.split('.').map((part) => part.trim())
  // A one-part line is always a subject, even when it is a type code ("exe" is sbt.exe), as in the API.
  const info = parts.length > 1 && parts[0]!.length === 3 ? getAnnotationTypeInfo(parts[0]!) : undefined
  const result: BulkLine = { line, text: trimmed, ...(info ? { code: info.code } : {}) }

  if (parts.some((part) => part.length === 0)) {
    return { ...result, problem: 'empty-part' }
  }
  if (info && parts.length !== info.segments) {
    return { ...result, problem: 'wrong-parts' }
  }
  if (!info && parts.length > compactCodes.length) {
    return { ...result, problem: 'too-many-parts' }
  }

  const names = info ? parts.slice(1) : parts
  if (names.some((name) => invalidCharacterOf(name) !== undefined)) {
    return { ...result, problem: 'invalid-name' }
  }

  return { ...result, key: [info ? info.code : compactCodes[parts.length - 1]!, ...names].join('.') }
}

/** The type a key part is named after; the own name of a key is named after its type. */
const partCodes = { subject: 'sbt', responsibility: 'rst', context: 'cnt', unit: 'unt' } as const

/** The names in a key of each type, for the messages: subject, responsibility, context for an Execution. */
const namesOf = (code: AnnotationTypeCode) =>
  typeMeta[code].parts.map((part) => typeMeta[part === 'name' ? code : partCodes[part]].label.toLowerCase())

/**
 * The label with its indefinite article: "an Execution", "a Usage". A "u" followed by a consonant and a vowel
 * sounds like "you" (Unit, Usage), so it takes "a".
 */
export function withArticle(label: string): string {
  return /^[aeiou]/i.test(label) && !/^u[^aeiou][aeiou]/i.test(label) ? `an ${label}` : `a ${label}`
}

const capitalized = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)

/** The message of a line's problem for the review, e.g. "An Execution key has 4 parts: exe.subject.responsibility.context". */
export function describeBulkProblem(line: BulkLine): string {
  switch (line.problem) {
    case 'too-many-parts':
      return `Has ${line.text.split('.').length} parts: a line without a type code has at most 4 (${namesOf('uxe').join('.')}).`
    case 'wrong-parts': {
      const code = line.code!
      const label = capitalized(withArticle(typeMeta[code].label))
      return `${label} key has ${getAnnotationTypeInfo(code)!.segments} parts: ${[code, ...namesOf(code)].join('.')}`
    }
    case 'empty-part':
      return 'Has an empty part: two dots in a row, or a dot at the start or the end.'
    case 'invalid-name': {
      // The tab is the common case: two cells copied from a spreadsheet.
      if (line.text.includes('\t')) {
        return 'Contains a tab: cells copied from a spreadsheet? Join them with dots.'
      }
      const character = invalidCharacterOf(line.text) ?? ''
      return isControl(character)
        ? 'Contains a control character, which a name cannot hold.'
        : `Contains "${character}", which a name cannot hold.`
    }
    default:
      return ''
  }
}
