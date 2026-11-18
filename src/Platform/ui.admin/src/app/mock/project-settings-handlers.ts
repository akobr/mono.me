import { http, HttpResponse, type HttpHandler } from 'msw'
import { validateProjectSettings, type ProjectSettings, type ProjectSettingsProblem } from '@42for.net/2splatform.sdk'
import type { MockDb } from './db'
import { errorResponse, requireRole, withDb } from './handlers'
import {
  invalidSettingsMessage,
  patchProjectSettings,
  projectSettingsOf,
  storeProjectSettings,
} from './project-settings'

// GET, PUT and PATCH v1/{organization}/{project}/settings (424-266) as the API answers them (access.md, "Project
// settings"): every member reads them with their ETag, the Version in quotes; Administrators replace or patch them, under
// an optional If-Match, against the rules of the SDK's validateProjectSettings. The mock user is always a person, so
// the 403 of machines is not mocked.

const api = '*/api/v1'
const mergePatch = 'application/merge-patch+json'

const entityTag = (version: number | undefined) => `"${version ?? 0}"`

/** A strong or weak entity tag, or *, as one element of an If-Match or If-None-Match list. */
const tagPattern = /^(?:W\/)?"[\x21\x23-\x7e\x80-￿]*"$/

/** The elements of a list of entity tags, or undefined when the header is not such a list. */
function tagList(header: string): string[] | undefined {
  const tags = header.split(',').map((part) => part.trim())
  return tags.length > 0 && tags.every((tag) => tag === '*' || tagPattern.test(tag)) ? tags : undefined
}

/** The version of a strong tag in its plain spelling ("7", not "07" or W/"7"); undefined for any other tag. */
function versionOf(tag: string): number | undefined {
  const text = /^"(0|[1-9][0-9]*)"$/.exec(tag)?.[1]
  return text === undefined ? undefined : Number(text)
}

type Condition = { kind: 'none' } | { kind: 'any' } | { kind: 'versions'; versions: number[] }

const invalidPrecondition = (message: string) => errorResponse(400, message, 'InvalidPrecondition')

/** The condition of a write (ProjectSettingsConditions.TryRead), or the 400 of a header the API refuses. */
function conditionOf(request: Request): Condition | Response {
  if (request.headers.has('If-None-Match')) {
    return invalidPrecondition('The project settings always exist; a write takes If-Match.')
  }
  const header = request.headers.get('If-Match')
  if (header === null) {
    return { kind: 'none' }
  }
  const tags = header.trim() ? tagList(header) : undefined
  if (!tags || (tags.includes('*') && tags.length > 1)) {
    return invalidPrecondition(
      'If-Match takes * or a list of entity tags, such as the ETag of a read or the Version of the settings in quotes.',
    )
  }
  return tags[0] === '*'
    ? { kind: 'any' }
    : { kind: 'versions', versions: tags.map(versionOf).filter((version) => version !== undefined) }
}

const holds = (condition: Condition, version: number) =>
  condition.kind !== 'versions' || condition.versions.includes(version)

const preconditionFailed = (version: number) =>
  HttpResponse.json(
    {
      Message: `The project settings changed meanwhile; they are at version ${version}.`,
      ErrorCode: 'SettingsPreconditionFailed',
      Details: { Version: version },
    },
    { status: 412 },
  )

const invalidSettings = (problems: readonly ProjectSettingsProblem[]) =>
  HttpResponse.json(
    {
      Message: invalidSettingsMessage(problems),
      ErrorCode: 'InvalidSettings',
      Details: problems.map((problem) => ({ Path: problem.path, Rule: problem.rule, Message: problem.message })),
    },
    { status: 400 },
  )

const settingsResponse = (settings: ProjectSettings) =>
  HttpResponse.json(settings, { headers: { ETag: entityTag(settings.Version), 'Cache-Control': 'private, no-cache' } })

/** The role check and the project of a write: the 403 of a caller below Administrator, or the 404 of a missing project. */
function writeTarget(db: MockDb, key: string): Response | undefined {
  return (
    requireRole(db, key, 'Administrator') ??
    (db.accessPoints[key] ? undefined : errorResponse(404, `The project '${key}' doesn't exist.`, 'NotFound'))
  )
}

/** The media type of a PATCH: merge patch, JSON, or none at all. */
function isMergePatch(request: Request): boolean {
  const type = request.headers.get('Content-Type')?.split(';')[0]?.trim().toLowerCase()
  return !type || type === mergePatch || type === 'application/json'
}

// A patch sent to the PUT, which would reset every type it leaves out, as the API refuses it.
function isPatch(request: Request): boolean {
  const type = request.headers.get('Content-Type')?.split(';')[0]?.trim().toLowerCase()
  return type === mergePatch || type === 'application/json-patch+json'
}

export const projectSettingsHandlers: HttpHandler[] = [
  http.get(
    `${api}/:org/:project/settings`,
    withDb((db, { params, request }) => {
      const key = `${params.org}.${params.project}`
      const denied = requireRole(db, key, 'Reader')
      if (denied) {
        return denied
      }
      if (!db.accessPoints[key]) {
        return errorResponse(404, `The project '${key}' doesn't exist.`, 'NotFound')
      }

      const settings = projectSettingsOf(db, key)
      const tag = entityTag(settings.Version)
      // A weak comparison: W/"7" and * match too; a header that is no list of tags is ignored.
      const ifNoneMatch = request.headers.get('If-None-Match')
      const tags = ifNoneMatch ? tagList(ifNoneMatch) : undefined
      if (tags?.some((candidate) => candidate === '*' || candidate.replace(/^W\//, '') === tag)) {
        return new HttpResponse(null, { status: 304, headers: { ETag: tag, 'Cache-Control': 'private, no-cache' } })
      }
      return settingsResponse(settings)
    }),
  ),

  http.put(
    `${api}/:org/:project/settings`,
    withDb(async (db, { params, request }) => {
      const key = `${params.org}.${params.project}`
      const refused = writeTarget(db, key)
      if (refused) {
        return refused
      }
      const condition = conditionOf(request)
      if (condition instanceof Response) {
        return condition
      }
      if (isPatch(request)) {
        return errorResponse(
          415,
          `A PUT replaces the whole project settings with an application/json body; send a merge patch (${mergePatch}) with PATCH.`,
          'InvalidContentType',
        )
      }

      const text = await request.text()
      let document: unknown = null
      if (text.trim()) {
        try {
          document = JSON.parse(text)
        } catch (error) {
          const reason = error instanceof Error ? error.message : String(error)
          return invalidSettings([{ path: '$', rule: 'type', message: `The body is not JSON: ${reason}` }])
        }
      }
      const check = validateProjectSettings(document)
      if (!check.valid) {
        return invalidSettings(check.problems)
      }

      const version = projectSettingsOf(db, key).Version ?? 0
      if (!holds(condition, version)) {
        return preconditionFailed(version)
      }
      return settingsResponse(storeProjectSettings(db, key, check.settings))
    }),
  ),

  http.patch(
    `${api}/:org/:project/settings`,
    withDb(async (db, { params, request }) => {
      const key = `${params.org}.${params.project}`
      const refused = writeTarget(db, key)
      if (refused) {
        return refused
      }
      const condition = conditionOf(request)
      if (condition instanceof Response) {
        return condition
      }
      if (!isMergePatch(request)) {
        const response = errorResponse(415, `A patch of the project settings is ${mergePatch}.`, 'InvalidContentType')
        response.headers.set('Accept-Patch', mergePatch)
        return response
      }

      let patch: unknown
      try {
        patch = JSON.parse(await request.text())
      } catch (error) {
        const reason = error instanceof Error ? error.message : String(error)
        return errorResponse(400, `The body is not a merge patch of the project settings: ${reason}`, 'PatchInvalid')
      }
      if (typeof patch !== 'object' || patch === null || Array.isArray(patch)) {
        return errorResponse(
          400,
          'The body is not a merge patch of the project settings: A merge patch (RFC 7396) of the project settings is a JSON object.',
          'PatchInvalid',
        )
      }

      const stored = projectSettingsOf(db, key)
      if (!holds(condition, stored.Version ?? 0)) {
        return preconditionFailed(stored.Version ?? 0)
      }
      const result = patchProjectSettings(stored, patch as Record<string, unknown>)
      if (!result.valid) {
        return invalidSettings(result.problems)
      }
      return settingsResponse(storeProjectSettings(db, key, result.settings))
    }),
  ),
]
