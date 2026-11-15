import {
  projectSettingsTypeCodes,
  validateProjectSettings,
  type AuditActor,
  type NormalizedProjectSettings,
  type ProjectSettings,
  type ProjectSettingsProblem,
} from '@42for.net/2splatform.sdk'
import { mockActor } from './audit'
import type { MockDb } from './db'
import { recordAudit } from './key-values'
import { applyMergePatch } from './merge'

// The settings of a project in the mock API (424-266), on its access point as the API keeps them
// (db.accessPoints[key].Settings, in the API's shape). The rules are the SDK's validateProjectSettings, the rules of the
// API; a change raises the version, stamps Modified and appends a ProjectSettingsChange event to the audit trail.

/** The settings of a project; a project whose settings were never set has every default and version 0. */
export function projectSettingsOf(db: MockDb, projectKey: string): ProjectSettings {
  return structuredClone(db.accessPoints[projectKey]?.Settings) ?? { Types: {}, Version: 0 }
}

/** Settings in the API's shape: Types, DefaultTimeZone and DateFormat when set, Version, and Modified when set. */
export function apiShape(
  settings: NormalizedProjectSettings,
  version: number,
  modified?: ProjectSettings['Modified'],
): ProjectSettings {
  return {
    Types: structuredClone(settings.Types),
    ...(settings.DefaultTimeZone ? { DefaultTimeZone: settings.DefaultTimeZone } : {}),
    ...(settings.DateFormat ? { DateFormat: settings.DateFormat } : {}),
    Version: version,
    ...(modified ? { Modified: modified } : {}),
  }
}

/** The values of stored settings, normalized as a write stores them, for the comparison with a write. */
function valuesOf(settings: ProjectSettings): NormalizedProjectSettings {
  const check = validateProjectSettings(settings)
  return check.valid ? check.settings : { Types: settings.Types ?? {} }
}

const sameJson = (left: unknown, right: unknown) => JSON.stringify(left ?? null) === JSON.stringify(right ?? null)

/** The detail of a ProjectSettingsChange event, as the API writes it: the versions and the names of the changed parts. */
export function changeDetail(
  before: NormalizedProjectSettings,
  after: NormalizedProjectSettings,
  from: number,
  to: number,
): string {
  const parts = projectSettingsTypeCodes
    .filter((code) => !sameJson(before.Types[code], after.Types[code]))
    .map((code) => `Types.${code}`)
  if (before.DefaultTimeZone !== after.DefaultTimeZone) {
    parts.push('DefaultTimeZone')
  }
  if (before.DateFormat !== after.DateFormat) {
    parts.push('DateFormat')
  }
  return `v${from}→v${to}: ${parts.join(', ')}`
}

/**
 * Stores normalized settings: a write that changes nothing keeps the version and records nothing; a change raises the
 * version, stamps Modified with the actor and appends a ProjectSettingsChange event. Returns the stored settings.
 */
export function storeProjectSettings(
  db: MockDb,
  projectKey: string,
  settings: NormalizedProjectSettings,
  actor: AuditActor = mockActor(db),
): ProjectSettings {
  const stored = projectSettingsOf(db, projectKey)
  const before = valuesOf(stored)
  const version = stored.Version ?? 0
  if (sameJson(before, settings)) {
    return stored
  }

  const next = apiShape(settings, version + 1, { At: new Date().toISOString(), By: actor })
  db.accessPoints[projectKey] = { ...db.accessPoints[projectKey], Settings: next }
  recordAudit(db, projectKey, {
    Operation: 'ProjectSettingsChange',
    Actor: actor,
    Detail: changeDetail(before, settings, version, version + 1),
  })
  return structuredClone(next)
}

const rootMembers = ['Format', 'Types', 'DefaultTimeZone', 'DateFormat', 'Version', 'Modified']
const appearanceMembers = ['Label', 'Plural', 'Color', 'Icon']
const fold = (value: string) => value.replace(/[A-Z]/g, (letter) => letter.toLowerCase())
const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

/** The members of a node by their canonical names; an unknown name and names that differ only in case are left out. */
function canonicalMembers(node: Record<string, unknown>, known: readonly string[]): Record<string, unknown> {
  const byName = new Map<string, unknown[]>()
  for (const [name, value] of Object.entries(node)) {
    const canonical = known.find((member) => fold(member) === fold(name))
    if (canonical !== undefined) {
      byName.set(canonical, [...(byName.get(canonical) ?? []), value])
    }
  }
  return Object.fromEntries(
    [...byName].filter(([, values]) => values.length === 1).map(([name, [value]]) => [name, value]),
  )
}

/**
 * The names of a merge patch, checked as the API checks them (ProjectSettingsJson.ReadMergePatch) before it merges: a
 * name that is no member or no type code (also with null) and two names that differ only in case are problems and are
 * not applied. Returns the patch with the names it applies, and the problems.
 */
function checkPatchNames(patch: Record<string, unknown>): {
  named: Record<string, unknown>
  problems: readonly ProjectSettingsProblem[]
} {
  // Only the names matter: the values become null, so the rules of the values report nothing here.
  const skeleton = Object.fromEntries(
    Object.entries(patch).map(([name, value]) => [
      name,
      fold(name) === 'types' && isObject(value)
        ? Object.fromEntries(
            Object.entries(value).map(([code, type]) => [
              code,
              isObject(type) ? Object.fromEntries(Object.keys(type).map((member) => [member, null])) : null,
            ]),
          )
        : null,
    ]),
  )
  const problems = validateProjectSettings(skeleton).problems

  const named = canonicalMembers(patch, rootMembers)
  if (isObject(named.Types)) {
    const codes = canonicalMembers(named.Types, projectSettingsTypeCodes)
    named.Types = Object.fromEntries(
      Object.entries(codes).map(([code, type]) => [
        code,
        isObject(type) ? canonicalMembers(type, appearanceMembers) : type,
      ]),
    )
  }
  return { named, problems }
}

/** The settings after a merge patch, or every problem of its names and of the result, as a PATCH of the API answers. */
export function patchProjectSettings(
  stored: ProjectSettings,
  patch: Record<string, unknown>,
):
  { valid: true; settings: NormalizedProjectSettings } | { valid: false; problems: readonly ProjectSettingsProblem[] } {
  const { named, problems } = checkPatchNames(patch)
  const check = validateProjectSettings(applyMergePatch(stored, named))
  if (problems.length === 0 && check.valid) {
    return { valid: true, settings: check.settings }
  }
  return { valid: false, problems: [...problems, ...check.problems] }
}

/** The message of 400 InvalidSettings: the first problem, and how many there are. */
export function invalidSettingsMessage(problems: readonly ProjectSettingsProblem[]): string {
  const [first] = problems
  if (!first) {
    return 'The project settings are not valid.'
  }
  const text = `${first.path}: ${first.message}`
  return problems.length === 1
    ? `The project settings are not valid: ${text}`
    : `The project settings have ${problems.length} problems; the first is ${text}`
}
