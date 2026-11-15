import {
  isCatalogTimeZone,
  type AuditStamp,
  type ProjectSettings,
  type ProjectTypeAppearance,
} from '@42for.net/2splatform.sdk'
import { isValidTimeZone } from '@/forms/annotation.schema'
import { typeCodes } from './annotation-key'
import { defaultDateFormat, isDateFormat, type DateFormat } from './dates'
import { sanitizeTypeAppearances, type TypeAppearances } from './type-appearance'

/**
 * The settings of a project (424-266) as the admin UI edits them: the names, colors and icons of the annotation types
 * (424-13), the time zone new annotations start in (424-12) and the date format (424-23). The API stores them on the
 * project (`GET`, `PUT` and `PATCH v1/{organization}/{project}/settings`, PascalCase); fromApi and toApi map between
 * the two shapes.
 */
export interface ProjectSettingsDraft {
  /** Names, colors and icons of the annotation types; Preferences can override them per field. */
  types: TypeAppearances
  /**
   * IANA time zone of the catalog that new annotations of the project start in. A zone newer than this browser's Intl
   * stays as it is, so a save keeps it; where the UI computes times in it, usableTimeZone gives UTC instead.
   */
  defaultTimeZone: string
  /** How dates are shown in this project; Preferences can override it. */
  dateFormat: DateFormat
}

/** The settings of a project as the server answered them. */
export interface ProjectSettingsState extends ProjectSettingsDraft {
  /** Raised by every change, 0 before the first; If-Match is this number in quotes (projectSettingsEntityTag). */
  version: number
  /** The last change; absent before the first. */
  modified?: AuditStamp
  /** The time zone the server stores, absent when it has none (UTC), so toApi keeps an explicit UTC explicit. */
  storedTimeZone?: string
  /** The date format the server stores, absent when it has none (yyyy-mm-dd). */
  storedDateFormat?: string
}

export const defaultTimeZone = 'UTC'

/**
 * A time zone a project can start new annotations in: one of the catalog of project settings, which the API accepts, in
 * its spelling, that Intl knows too, so the UI can show times in it (Intl also takes offsets such as +01:00, the API not).
 */
export function isProjectTimeZone(value: unknown): value is string {
  return isCatalogTimeZone(value) && isValidTimeZone(value)
}

/**
 * The zone where the UI computes times in it (the New annotation dialog): the zone itself when this browser's Intl knows
 * it, else UTC. A project can store a zone of the catalog that an older browser does not know yet.
 */
export function usableTimeZone(zone: string): string {
  return isValidTimeZone(zone) ? zone : defaultTimeZone
}

/** The settings of a project that has none: every type built-in, UTC and yyyy-mm-dd, version 0. */
export const defaultProjectSettings: Readonly<ProjectSettingsState> = Object.freeze({
  types: Object.freeze({}),
  defaultTimeZone,
  dateFormat: defaultDateFormat,
  version: 0,
})

/**
 * The UI's settings of an answer of the API: the defaults for what it leaves out, and, as defense in depth, only what
 * the UI can show (sanitized types, a zone of the catalog, a known date format). A zone of the catalog stays even when
 * this browser's Intl does not know it: falling back to UTC here would make the next save of any other change send the
 * project's zone back to UTC for everyone.
 */
export function fromApi(settings: ProjectSettings | undefined): ProjectSettingsState {
  const types: Record<string, unknown> = {}
  for (const [code, appearance] of Object.entries(settings?.Types ?? {})) {
    if (appearance && typeof appearance === 'object') {
      types[code.toLowerCase()] = {
        label: appearance.Label,
        plural: appearance.Plural,
        color: appearance.Color,
        icon: appearance.Icon,
      }
    }
  }

  const zone = settings?.DefaultTimeZone
  const format = settings?.DateFormat
  const version = settings?.Version
  return {
    types: sanitizeTypeAppearances(types),
    defaultTimeZone: isCatalogTimeZone(zone) ? zone : defaultTimeZone,
    dateFormat: isDateFormat(format) ? format : defaultDateFormat,
    version: typeof version === 'number' && Number.isSafeInteger(version) && version >= 0 ? version : 0,
    ...(settings?.Modified ? { modified: settings.Modified } : {}),
    ...(zone ? { storedTimeZone: zone } : {}),
    ...(format ? { storedDateFormat: format } : {}),
  }
}

/**
 * The body of a PUT of a draft: the sanitized types in the order of the type codes, and the time zone and the date format
 * unless the draft has the default and the server does not store it, so a PUT of an untouched draft changes nothing and
 * a reset to the default sends it as unset.
 */
export function toApi(
  draft: ProjectSettingsDraft,
  base?: Pick<ProjectSettingsState, 'storedTimeZone' | 'storedDateFormat'>,
): ProjectSettings {
  const types = sanitizeTypeAppearances(draft.types)
  const body: ProjectSettings = { Types: {} }

  for (const code of typeCodes) {
    const appearance = types[code]
    if (!appearance) {
      continue
    }
    const item: ProjectTypeAppearance = {}
    if (appearance.label !== undefined) item.Label = appearance.label
    if (appearance.plural !== undefined) item.Plural = appearance.plural
    if (appearance.color !== undefined) item.Color = appearance.color
    if (appearance.icon !== undefined) item.Icon = appearance.icon
    body.Types![code] = item
  }

  if (draft.defaultTimeZone !== defaultTimeZone || base?.storedTimeZone === draft.defaultTimeZone) {
    body.DefaultTimeZone = draft.defaultTimeZone
  }
  if (draft.dateFormat !== defaultDateFormat || base?.storedDateFormat === draft.dateFormat) {
    body.DateFormat = draft.dateFormat
  }
  return body
}
