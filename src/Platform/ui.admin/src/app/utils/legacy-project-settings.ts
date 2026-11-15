import { defaultDateFormat, isDateFormat, type DateFormat } from './dates'
import { defaultTimeZone, isProjectTimeZone, type ProjectSettingsDraft } from './project-settings'
import { sanitizeTypeAppearances, type TypeAppearances } from './type-appearance'

/**
 * The project settings the admin UI kept in each browser before the API stored them for everyone (424-12, 424-13,
 * 424-23): per access point key (`org.project`) in `localStorage['2s-admin:project-settings']`, as
 * `{ types, defaultTimeZone, dateFormat }`. The Settings page offers to save them for the project while the project has
 * no settings on the server (424-266); this module only reads and removes the entries, nothing writes them any more.
 */
export const legacyProjectSettingsKey = '2s-admin:project-settings'

/** What an entry holds that differs from the defaults, sanitized as the UI shows it. */
export interface LegacyProjectSettings {
  types?: TypeAppearances
  defaultTimeZone?: string
  dateFormat?: DateFormat
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

/** The default local storage, or undefined where it cannot be used (blocked, or no window). */
function browserStorage(): Storage | undefined {
  try {
    return typeof localStorage === 'undefined' ? undefined : localStorage
  } catch {
    return undefined
  }
}

function readEntries(storage: Storage | undefined): Record<string, unknown> | undefined {
  try {
    const text = storage?.getItem(legacyProjectSettingsKey)
    const value: unknown = text ? JSON.parse(text) : undefined
    return isObject(value) ? value : undefined
  } catch {
    return undefined
  }
}

/**
 * The old settings of a project in this browser: the types that sanitize to something, a zone of the catalog that Intl
 * knows other than UTC, a known date format other than yyyy-mm-dd. Undefined when there is no entry or nothing in it to
 * save, so a project whose entry holds only defaults shows no offer.
 */
export function readLegacyProjectSettings(
  projectKey: string,
  storage: Storage | undefined = browserStorage(),
): LegacyProjectSettings | undefined {
  const entry = readEntries(storage)?.[projectKey]
  if (!isObject(entry)) {
    return undefined
  }

  const settings: LegacyProjectSettings = {}
  const types = sanitizeTypeAppearances(entry.types)
  if (Object.keys(types).length > 0) {
    settings.types = types
  }
  if (isProjectTimeZone(entry.defaultTimeZone) && entry.defaultTimeZone !== defaultTimeZone) {
    settings.defaultTimeZone = entry.defaultTimeZone
  }
  if (isDateFormat(entry.dateFormat) && entry.dateFormat !== defaultDateFormat) {
    settings.dateFormat = entry.dateFormat
  }
  return Object.keys(settings).length > 0 ? settings : undefined
}

/** Removes the old settings of a project from this browser, and the key once no project has any. */
export function removeLegacyProjectSettings(projectKey: string, storage: Storage | undefined = browserStorage()): void {
  const entries = readEntries(storage)
  if (!storage || !entries || !(projectKey in entries)) {
    return
  }

  const { [projectKey]: _removed, ...others } = entries
  try {
    if (Object.keys(others).length > 0) {
      storage.setItem(legacyProjectSettingsKey, JSON.stringify(others))
    } else {
      storage.removeItem(legacyProjectSettingsKey)
    }
  } catch (error) {
    console.warn('Project settings: the settings of this browser could not be removed.', error)
  }
}

/** The settings a save of old settings sends: the old values, the defaults for the rest. */
export function legacyProjectSettingsDraft(settings: LegacyProjectSettings): ProjectSettingsDraft {
  return {
    types: settings.types ?? {},
    defaultTimeZone: settings.defaultTimeZone ?? defaultTimeZone,
    dateFormat: settings.dateFormat ?? defaultDateFormat,
  }
}
