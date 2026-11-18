// Project settings: the guards of the catalog (generated/catalog/project-settings.ts, written from the conformance suite's
// project-settings.json), the ETag of a version, and validateProjectSettings with the rules of ProjectSettingsJson and
// ProjectSettingsRules of the .NET Abstractions.Annotations, so a client checks settings as the API will before it writes
// them. The vectors of the conformance suite pin the rules for every language; test/conformance runs them here.
import {
  catalogTimeZones,
  dateFormats,
  projectSettingsFormat,
  projectSettingsLimits,
  projectSettingsTypeCodes,
  typeColors,
  typeIconNames,
  type CatalogTimeZone,
  type DateFormat,
  type TypeColor,
  type TypeIconName,
} from '../generated/catalog/project-settings'
import { isPlainObject, toPropertyPath } from './json'
import type { AnnotationTypeCode } from './keys'

/** The rules of a problem of project settings, as the conformance vectors and the API's Details spell them. */
export const projectSettingsRules = [
  'type-code',
  'label',
  'plural',
  'control-character',
  'plural-without-label',
  'color',
  'icon',
  'time-zone',
  'date-format',
  'unknown-member',
  'case-collision',
  'type',
  'format',
] as const

export type ProjectSettingsRule = (typeof projectSettingsRules)[number]

/** A broken rule: the JSON path of the value with its member in the canonical spelling ($.Types.sbt.Color), the rule, and a message. */
export interface ProjectSettingsProblem {
  path: string
  rule: ProjectSettingsRule
  message: string
}

/** The appearance of a type as the API stores it: trimmed names, the catalog's spelling; a member without a value is left out. */
export interface NormalizedProjectTypeAppearance {
  Label?: string
  Plural?: string
  Color?: TypeColor
  Icon?: TypeIconName
}

/**
 * Settings as the API stores them, in its shape without Version and Modified: lower-case type codes in the catalog's order
 * (a type without any value is left out), and no DefaultTimeZone (UTC) or DateFormat (yyyy-mm-dd) when none is set.
 */
export interface NormalizedProjectSettings {
  Types: { [Code in AnnotationTypeCode]?: NormalizedProjectTypeAppearance }
  DefaultTimeZone?: CatalogTimeZone
  DateFormat?: DateFormat
}

/** The settings a write would store, or every problem the API would answer with 400 InvalidSettings. */
export type ProjectSettingsValidation =
  | { valid: true; settings: NormalizedProjectSettings; problems: readonly ProjectSettingsProblem[] }
  | { valid: false; settings?: undefined; problems: readonly ProjectSettingsProblem[] }

const root = '$'
const typesPath = '$.Types'
const documentMembers = ['Format', 'Types', 'DefaultTimeZone', 'DateFormat', 'Version', 'Modified'] as const
const appearanceMembers = ['Label', 'Plural', 'Color', 'Icon'] as const

type AppearanceInput = { [Member in (typeof appearanceMembers)[number]]?: string }

interface Lookups {
  typeCodes: Map<string, AnnotationTypeCode>
  colors: Map<string, TypeColor>
  icons: Map<string, TypeIconName>
  dateFormats: Map<string, DateFormat>
  timeZones: Map<string, CatalogTimeZone>
}

let lookups: Lookups | undefined

/** The strong ETag of settings at a version, "<Version>", for If-Match: a browser on another origin cannot read the ETag header, but the body has the Version. */
export function projectSettingsEntityTag(version: number): string {
  if (!Number.isSafeInteger(version) || version < 0) {
    throw new RangeError(`The version of project settings is a whole number from 0, not ${version}.`)
  }

  return `"${version}"`
}

/** A color of the catalog, in its spelling. */
export function isTypeColor(value: unknown): value is TypeColor {
  return typeof value === 'string' && catalog().colors.get(fold(value)) === value
}

/** An icon of the catalog, in its spelling (i-lucide-building-2). */
export function isTypeIconName(value: unknown): value is TypeIconName {
  return typeof value === 'string' && catalog().icons.get(fold(value)) === value
}

/** A date format of the catalog, in its spelling (dd.mm.yyyy). */
export function isDateFormat(value: unknown): value is DateFormat {
  return typeof value === 'string' && catalog().dateFormats.get(fold(value)) === value
}

/** An IANA time zone id of the catalog, in its spelling (Europe/Prague, Asia/Calcutta, UTC): the zones the API accepts. */
export function isCatalogTimeZone(value: unknown): value is CatalogTimeZone {
  return typeof value === 'string' && catalog().timeZones.get(fold(value)) === value
}

/**
 * Checks a settings document of any client (a GET result, a data set's project-settings.json, a PUT body) as the API
 * does: member names and type codes in any ASCII case, Format optional and the marker, Version and Modified ignored, any
 * other member a problem; then the rules of the catalog. Returns the settings a PUT of the document would store, or every
 * problem. The document is not changed.
 */
export function validateProjectSettings(document: unknown): ProjectSettingsValidation {
  const problems: ProjectSettingsProblem[] = []

  if (!isPlainObject(document)) {
    problems.push({ path: root, rule: 'type', message: 'The project settings must be a JSON object.' })
    return { valid: false, problems }
  }

  const members = readMembers(document, root, documentMembers, problems, 'Types, DefaultTimeZone and DateFormat (and the read-only Format, Version and Modified)')
  const format = readText(members, 'Format', root, problems)

  if (format !== undefined && format !== projectSettingsFormat) {
    problems.push({ path: toPropertyPath(root, 'Format'), rule: 'format', message: `The format '${format}' is not ${projectSettingsFormat}.` })
  }

  const types = readTypes(members.get('Types'), problems)
  const timeZone = readText(members, 'DefaultTimeZone', root, problems)
  const dateFormat = readText(members, 'DateFormat', root, problems)
  const settings = normalize(types, timeZone, dateFormat, problems)

  return problems.length === 0 ? { valid: true, settings, problems } : { valid: false, problems }
}

// Built on the first use, so an application that never checks settings never builds them.
function catalog(): Lookups {
  lookups ??= {
    typeCodes: index(projectSettingsTypeCodes),
    colors: index(typeColors),
    icons: index(typeIconNames),
    dateFormats: index(dateFormats),
    timeZones: index(catalogTimeZones),
  }

  return lookups
}

function index<T extends string>(values: readonly T[]): Map<string, T> {
  return new Map(values.map((value) => [fold(value), value]))
}

// ASCII only (A-Z to a-z), as every runtime folds the same way: no long s or Kelvin sign becomes a letter of a code.
function fold(value: string): string {
  return value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
}

// Unicode White_Space, as .NET's string.Trim and string.IsNullOrWhiteSpace see it (String.prototype.trim differs: it
// removes U+FEFF and keeps U+0085).
function trimmed(value: string | undefined): string | undefined {
  const text = value?.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '')
  return text ? text : undefined
}

// A control character (C0 or C1, as .NET's char.IsControl), a line or paragraph separator, or a bidirectional control.
// eslint-disable-next-line no-control-regex -- the controls are what the rule refuses
const controlCharacter = /[\u0000-\u001F\u007F-\u009F\u2028\u2029\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/

// Checked after trimming, so a white space control at either end (a tab, a line break) is trimmed rather than refused.
function addControlCharacter(value: string | undefined, path: string, field: 'label' | 'plural', problems: ProjectSettingsProblem[]): void {
  const found = value === undefined ? null : controlCharacter.exec(value)
  if (found) {
    const code = found[0].charCodeAt(0).toString(16).toUpperCase().padStart(4, '0')
    problems.push({
      path,
      rule: 'control-character',
      message: `The ${field} contains the character U+${code}; control characters, line and paragraph separators and bidirectional controls are not allowed.`,
    })
  }
}

function isBlank(value: string | undefined): boolean {
  return value === undefined || /^\p{White_Space}*$/u.test(value)
}

// The known members by their canonical name; an unknown member and two members that differ only in case are problems
// (and the colliding members are not read).
function readMembers(
  node: Record<string, unknown>,
  path: string,
  known: readonly string[],
  problems: ProjectSettingsProblem[],
  membersText: string,
): Map<string, unknown> {
  const members = new Map<string, unknown>()
  const collisions = new Set<string>()

  for (const [name, value] of Object.entries(node)) {
    const canonical = known.find((member) => fold(member) === fold(name))

    if (canonical === undefined) {
      problems.push({ path: toPropertyPath(path, name), rule: 'unknown-member', message: `'${name}' is not a member; the members are ${membersText}.` })
    } else if (members.has(canonical)) {
      collisions.add(canonical)
    } else {
      members.set(canonical, value)
    }
  }

  for (const canonical of known.filter((member) => collisions.has(member))) {
    members.delete(canonical)
    problems.push({ path: toPropertyPath(path, canonical), rule: 'case-collision', message: `The member ${canonical} is given more than once, in different cases.` })
  }

  return members
}

// Keyed by the type code in the catalog's spelling; an unknown code and two codes that differ only in case are problems.
function readTypes(node: unknown, problems: ProjectSettingsProblem[]): Map<AnnotationTypeCode, AppearanceInput> {
  const types = new Map<AnnotationTypeCode, AppearanceInput>()

  if (node === undefined || node === null) {
    return types
  }

  if (!isPlainObject(node)) {
    problems.push({ path: typesPath, rule: 'type', message: 'Types must be an object of type codes, or null.' })
    return types
  }

  const byCode = new Map<AnnotationTypeCode, unknown>()
  const collisions: AnnotationTypeCode[] = []

  for (const [key, value] of Object.entries(node)) {
    const code = catalog().typeCodes.get(fold(key))

    if (code === undefined) {
      problems.push({
        path: toPropertyPath(typesPath, key),
        rule: 'type-code',
        message: `'${key}' is not a type code; the codes are ${projectSettingsTypeCodes.join(', ')}.`,
      })
    } else if (!byCode.has(code)) {
      byCode.set(code, value)
    } else if (!collisions.includes(code)) {
      collisions.push(code)
    }
  }

  for (const code of collisions) {
    byCode.delete(code)
    problems.push({ path: toPropertyPath(typesPath, code), rule: 'type-code', message: `The type code ${code} is given more than once, in different cases.` })
  }

  for (const [code, value] of byCode) {
    const path = toPropertyPath(typesPath, code)

    if (value === undefined || value === null) {
      continue
    }

    if (!isPlainObject(value)) {
      problems.push({ path, rule: 'type', message: `The type ${code} must be an object of Label, Plural, Color and Icon, or null.` })
      continue
    }

    const members = readMembers(value, path, appearanceMembers, problems, 'Label, Plural, Color and Icon')
    types.set(code, {
      Label: readText(members, 'Label', path, problems),
      Plural: readText(members, 'Plural', path, problems),
      Color: readText(members, 'Color', path, problems),
      Icon: readText(members, 'Icon', path, problems),
    })
  }

  return types
}

// A string, or undefined for null and a missing member; any other value is a problem (and then read as none).
function readText(members: Map<string, unknown>, name: string, parent: string, problems: ProjectSettingsProblem[]): string | undefined {
  const value = members.get(name)

  if (value === undefined || value === null) {
    return undefined
  }

  if (typeof value === 'string') {
    return value
  }

  problems.push({ path: toPropertyPath(parent, name), rule: 'type', message: `${name} must be a string or null, not ${describe(value)}.` })
  return undefined
}

function describe(value: unknown): string {
  if (Array.isArray(value)) {
    return 'an array'
  }

  switch (typeof value) {
    case 'object':
      return 'an object'
    case 'number':
    case 'bigint':
      return 'a number'
    case 'boolean':
      return 'a boolean'
    default:
      return 'this value'
  }
}

function normalize(
  types: Map<AnnotationTypeCode, AppearanceInput>,
  timeZone: string | undefined,
  dateFormat: string | undefined,
  problems: ProjectSettingsProblem[],
): NormalizedProjectSettings {
  const settings: NormalizedProjectSettings = { Types: {} }

  for (const code of projectSettingsTypeCodes) {
    const input = types.get(code)
    const appearance = input && normalizeAppearance(input, toPropertyPath(typesPath, code), problems)

    if (appearance) {
      settings.Types[code] = appearance
    }
  }

  const zone = normalizeChoice(timeZone, toPropertyPath(root, 'DefaultTimeZone'), 'time-zone', catalog().timeZones, problems,
    (value) => `'${value}' is not an IANA time zone id of the tz database, such as Europe/Prague or UTC.`)
  const format = normalizeChoice(dateFormat, toPropertyPath(root, 'DateFormat'), 'date-format', catalog().dateFormats, problems,
    (value) => `'${value}' is not a date format; use one of ${dateFormats.join(', ')}.`)

  if (zone !== undefined) {
    settings.DefaultTimeZone = zone
  }

  if (format !== undefined) {
    settings.DateFormat = format
  }

  return settings
}

function normalizeAppearance(input: AppearanceInput, path: string, problems: ProjectSettingsProblem[]): NormalizedProjectTypeAppearance | undefined {
  const label = trimmed(input.Label)
  const plural = trimmed(input.Plural)

  if (label !== undefined && label.length > projectSettingsLimits.label) {
    problems.push({
      path: toPropertyPath(path, 'Label'),
      rule: 'label',
      message: `The label has ${label.length} characters; at most ${projectSettingsLimits.label} are allowed.`,
    })
  }

  if (plural !== undefined && plural.length > projectSettingsLimits.plural) {
    problems.push({
      path: toPropertyPath(path, 'Plural'),
      rule: 'plural',
      message: `The plural has ${plural.length} characters; at most ${projectSettingsLimits.plural} are allowed.`,
    })
  }

  addControlCharacter(label, toPropertyPath(path, 'Label'), 'label', problems)
  addControlCharacter(plural, toPropertyPath(path, 'Plural'), 'plural', problems)

  if (plural !== undefined && label === undefined) {
    problems.push({
      path: toPropertyPath(path, 'Plural'),
      rule: 'plural-without-label',
      message: 'A plural needs a label; set the label too, or leave the plural out.',
    })
  }

  // Not trimmed: a color or an icon is a name of the catalog, and an empty one is none of them.
  const color = input.Color === undefined ? undefined : catalog().colors.get(fold(input.Color))

  if (input.Color !== undefined && color === undefined) {
    problems.push({
      path: toPropertyPath(path, 'Color'),
      rule: 'color',
      message: `'${input.Color}' is not a color of the catalog; use one of ${typeColors.join(', ')}.`,
    })
  }

  const icon = input.Icon === undefined ? undefined : catalog().icons.get(fold(input.Icon))

  if (input.Icon !== undefined && icon === undefined) {
    problems.push({
      path: toPropertyPath(path, 'Icon'),
      rule: 'icon',
      message: `'${input.Icon}' is not an icon of the catalog, such as ${typeIconNames[0]} or i-lucide-building-2.`,
    })
  }

  if (label === undefined && plural === undefined && color === undefined && icon === undefined) {
    return undefined
  }

  const appearance: NormalizedProjectTypeAppearance = {}

  if (label !== undefined) appearance.Label = label
  if (plural !== undefined) appearance.Plural = plural
  if (color !== undefined) appearance.Color = color
  if (icon !== undefined) appearance.Icon = icon

  return appearance
}

// A blank value means none; any other must be a value of the catalog, in any ASCII case, and comes back in its spelling.
function normalizeChoice<T extends string>(
  value: string | undefined,
  path: string,
  rule: ProjectSettingsRule,
  values: Map<string, T>,
  problems: ProjectSettingsProblem[],
  message: (value: string) => string,
): T | undefined {
  if (value === undefined || isBlank(value)) {
    return undefined
  }

  const canonical = values.get(fold(value))

  if (canonical === undefined) {
    problems.push({ path, rule, message: message(value) })
  }

  return canonical
}
