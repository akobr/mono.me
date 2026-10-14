import {
  http,
  HttpResponse,
  type DefaultBodyType,
  type HttpHandler,
  type HttpResponseResolver,
  type PathParams,
} from 'msw'
import type {
  AccessPointCreate,
  AccountCreate,
  AccountRole,
  BindingCatalog,
  View,
  ViewCreate,
  ViewUpdate,
} from '@42for.net/2splatform.sdk'
import { roleRank } from '@42for.net/2splatform.sdk'
import { fallbackFunctions } from '@/utils/binding-language'
import { stamp } from './audit'
import { loadDb, saveDb, type MockDb } from './db'

// Hand-written handlers for the operations the mock scenarios need to be coherent (accounts, access points,
// views, …). Every other operation falls through to the Orval-generated Faker handlers.

const api = '*/api/v1'

export const errorResponse = (status: number, message: string, errorCode?: string, hint?: string) =>
  HttpResponse.json({ Message: message, ErrorCode: errorCode, Hint: hint }, { status })

export function roleOf(db: MockDb, key: string): AccountRole {
  return (db.account?.AccessMap?.[key] as AccountRole | undefined) ?? 'None'
}

export function requireRole(db: MockDb, key: string, minimal: AccountRole) {
  return roleRank(roleOf(db, key)) >= roleRank(minimal)
    ? undefined
    : errorResponse(403, `The ${minimal} role on ${key} is required.`, 'AccessDenied')
}

type ResolverInfo<Params extends PathParams> = Parameters<HttpResponseResolver<Params, DefaultBodyType>>[0]

/** Runs a handler against the stored database and saves it afterwards. */
export const withDb =
  <Params extends PathParams = PathParams>(
    handler: (db: MockDb, info: ResolverInfo<Params>) => Response | Promise<Response>,
  ): HttpResponseResolver<Params, DefaultBodyType> =>
  async (info) => {
    const db = loadDb()
    const before = JSON.stringify(db)
    const response = await handler(db, info)
    // Only writes persist: a read that finishes late must not overwrite a newer write.
    if (JSON.stringify(db) !== before) {
      saveDb(db)
    }
    return response
  }

const namePattern = /^[a-z0-9][a-z0-9-]{1,62}$/

export const accessHandlers: HttpHandler[] = [
  http.get(`${api}/auth/configuration`, () =>
    HttpResponse.json({ Provider: 'AuthKit', ClientId: 'client_mock', Scopes: [] }),
  ),

  http.get(
    `${api}/access/account`,
    withDb((db) =>
      db.account ? HttpResponse.json(db.account) : errorResponse(404, 'The account does not exist.', 'NotFound'),
    ),
  ),

  http.post(
    `${api}/access/account`,
    withDb(async (db, { request }) => {
      const body = (await request.json()) as AccountCreate
      if (db.account) {
        return errorResponse(409, 'The account already exists.', 'AccountExists')
      }
      if (!namePattern.test(body.Organization ?? '') || !namePattern.test(body.Project ?? '')) {
        return errorResponse(400, 'The organization or project name is not valid.', 'InvalidName')
      }

      const projectKey = `${body.Organization}.${body.Project}`
      db.account = {
        Id: db.user.id,
        UserName: db.user.email,
        Name: db.user.name,
        AccessMap: { [body.Organization!]: 'Owner', [projectKey]: 'Owner' },
      }
      db.accessPoints[body.Organization!] = { Key: body.Organization, AccessMap: { [db.user.id]: 'Owner' } }
      db.accessPoints[projectKey] = { Key: projectKey, AccessMap: { [db.user.id]: 'Owner' } }
      return HttpResponse.json(db.account, { status: 201 })
    }),
  ),

  http.get(
    `${api}/access/points`,
    withDb((db) =>
      HttpResponse.json(
        Object.values(db.accessPoints).filter(
          (point) => roleRank(roleOf(db, point.Key ?? '')) >= roleRank('Administrator'),
        ),
      ),
    ),
  ),

  http.post(
    `${api}/access/points`,
    withDb(async (db, { request }) => {
      const body = (await request.json()) as AccessPointCreate
      const org = body.Organization ?? ''
      const projectKey = `${org}.${body.Project}`

      if (!namePattern.test(org) || !namePattern.test(body.Project ?? '')) {
        return errorResponse(400, 'The organization or project name is not valid.', 'InvalidName')
      }
      if (db.accessPoints[projectKey]) {
        return errorResponse(409, `The project ${projectKey} already exists.`, 'AccessPointExists')
      }
      if (db.accessPoints[org] && roleOf(db, org) !== 'Owner') {
        return errorResponse(409, `The organization ${org} already exists.`, 'AccessPointExists')
      }

      db.accessPoints[org] ??= { Key: org, AccessMap: { [db.user.id]: 'Owner' } }
      db.accessPoints[projectKey] = { Key: projectKey, AccessMap: { [db.user.id]: 'Owner' } }
      db.account!.AccessMap = {
        ...db.account!.AccessMap,
        [org]: db.account!.AccessMap?.[org] ?? 'Owner',
        [projectKey]: 'Owner',
      }
      return HttpResponse.json(db.accessPoints[projectKey], { status: 201 })
    }),
  ),

  http.get(
    `${api}/access/points/:key`,
    withDb((db, { params }) => {
      const key = String(params.key)
      return (
        requireRole(db, key, 'Administrator') ?? HttpResponse.json(db.accessPoints[key] ?? { Key: key, AccessMap: {} })
      )
    }),
  ),
]

const viewsOf = (db: MockDb, projectKey: string): View[] =>
  (db.views[projectKey] ??= [{ Name: 'default', IsDefault: true, IsRegistered: false }])

export const viewHandlers: HttpHandler[] = [
  http.get(
    `${api}/:org/:project/views`,
    withDb((db, { params }) => {
      const key = `${params.org}.${params.project}`
      return requireRole(db, key, 'Reader') ?? HttpResponse.json(viewsOf(db, key))
    }),
  ),

  http.post(
    `${api}/:org/:project/views`,
    withDb(async (db, { params, request }) => {
      const key = `${params.org}.${params.project}`
      const denied = requireRole(db, key, 'Administrator')
      if (denied) {
        return denied
      }

      const body = (await request.json()) as ViewCreate
      if (!namePattern.test(body.Name ?? '')) {
        return errorResponse(400, `The view name '${body.Name}' is not valid.`, 'InvalidName')
      }
      if (viewsOf(db, key).some((view) => view.Name === body.Name && view.IsRegistered)) {
        return errorResponse(409, `The view ${body.Name} already exists.`, 'ViewExists')
      }

      const view: View = {
        Name: body.Name,
        Description: body.Description,
        IsDefault: false,
        IsRegistered: true,
        Created: stamp(db),
      }
      db.views[key] = [...viewsOf(db, key).filter((existing) => existing.Name !== body.Name), view]
      return HttpResponse.json(view, { status: 201 })
    }),
  ),

  http.put(
    `${api}/:org/:project/views/:view`,
    withDb(async (db, { params, request }) => {
      const key = `${params.org}.${params.project}`
      const denied = requireRole(db, key, 'Administrator')
      if (denied) {
        return denied
      }

      const body = (await request.json()) as ViewUpdate
      const existing = viewsOf(db, key).find((view) => view.Name === params.view)
      const view: View = {
        ...existing,
        Name: String(params.view),
        Description: body.Description,
        IsDefault: params.view === 'default',
        IsRegistered: true,
        Created: existing?.Created ?? stamp(db),
      }
      db.views[key] = [...viewsOf(db, key).filter((other) => other.Name !== params.view), view]
      return HttpResponse.json(view)
    }),
  ),
]

// The catalog of a deployment with one Azure Key Vault, as GetBindingCatalog describes it (424-10).
export const mockBindingCatalog: BindingCatalog = {
  Functions: [...fallbackFunctions],
  Sources: [
    {
      Name: 'primaryVault',
      Summary:
        'A secret of the Azure Key Vault registered as `primaryVault`. The path becomes the secret name joined by `--`. Resolved only for callers that may read secrets.',
      IsDefault: false,
      IsSecret: true,
      Examples: [{ Expression: '@(db.password, primaryVault)', Description: 'The secret `db--password`.' }],
    },
  ],
}

export const bindingHandlers: HttpHandler[] = [
  http.get(
    `${api}/:org/:project/bindings`,
    withDb(
      (db, { params }) =>
        requireRole(db, `${params.org}.${params.project}`, 'Reader') ?? HttpResponse.json(mockBindingCatalog),
    ),
  ),
]
