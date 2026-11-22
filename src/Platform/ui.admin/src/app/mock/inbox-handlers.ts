import { http, HttpResponse, type HttpHandler } from 'msw'
import type { AccountRole, AuditActor } from '@42for.net/2splatform.sdk'
import { mockActor } from './audit'
import type { MockDb } from './db'
import type { ScopeRef } from './documents'
import { errorResponse, requireRole, roleOf, withDb } from './handlers'
import {
  acceptInboxItem,
  decodeInboxToken,
  dismissInboxItem,
  isInboxRefusal,
  listInbox,
  parseInboxFilter,
  pushInboxItem,
  systemActor,
  type InboxActionHandler,
  type InboxRefusal,
  type StoredInboxItem,
} from './inbox'

// GET v1/{organization}/{project}/{view}/inbox and POST …/inbox/{id}/accept and …/inbox/{id}/dismiss (424-31) as the API
// answers them (docs/Platform/Storyteller/inbox.md, "Routes"). The Contributor role on the project comes first (403),
// then the `state` of the list (400) and its continuation token (400). The role also decides which items there are:
// one above it is not found (404), before its kind and its state are looked at, as an unknown id, an id of another
// shape and an expired item are. The view is not checked: the inbox of a view without items is empty. The mock user is
// always a person, so the 403 of machines is not mocked.
//
// Accept is the one rule where the mock leaves the API. The API registers no handler of an action yet and answers 501
// InboxActionNotAvailable for every open action. The mock runs the action GenerateSchema at once: the item is Accepted,
// stamped with the mock user, and a notification of the result arrives (Topic SchemaSuggestion, Success, "Schema
// generation finished", for the role of the accepted item, by System, without a Job). Nothing is generated: it stands
// for what 424-8 adds, so the flow can be seen and tested. Any other action code answers the 501 of the API.

const api = '*/api/v1/:org/:project/:view/inbox'

type ScopeParams = { org: string; project: string; view: string }
type ItemParams = ScopeParams & { id: string }

const scopeOf = (params: ScopeParams): ScopeRef => ({ org: params.org, project: params.project, view: params.view })
const projectKey = (params: ScopeParams) => `${params.org}.${params.project}`

/** The actions the mock runs, by their code; the API has none yet. */
const mockActions: ReadonlyMap<string, InboxActionHandler> = new Map<string, InboxActionHandler>([
  [
    'GenerateSchema',
    ({ db, scope, item }) => {
      pushInboxItem(db, scope, {
        Kind: 'Notification',
        Topic: 'SchemaSuggestion',
        Level: 'Success',
        Title: 'Schema generation finished',
        Text: 'A schema with 6 properties was generated from the similar configurations of this view.',
        MinimalRole: item.MinimalRole,
        CreatedBy: systemActor,
      })
      // the mock starts no job
      return undefined
    },
  ],
])

const invalidQuery = (message: string) => errorResponse(400, message, 'InvalidQuery')

const answer = (result: StoredInboxItem | InboxRefusal) =>
  isInboxRefusal(result) ? errorResponse(result.status, result.message, result.errorCode) : HttpResponse.json(result)

/** Accept or dismiss: the Contributor role first, then the item, with the caller's role and the mock user as the actor. */
const close = (
  run: (db: MockDb, scope: ScopeRef, id: string, role: AccountRole, actor: AuditActor) => StoredInboxItem | InboxRefusal,
) =>
  withDb<ItemParams>((db, { params }) => {
    const key = projectKey(params)
    return (
      requireRole(db, key, 'Contributor') ??
      answer(run(db, scopeOf(params), params.id, roleOf(db, key), mockActor(db)))
    )
  })

export const inboxHandlers: HttpHandler[] = [
  http.get<ScopeParams>(
    api,
    withDb((db, { params, request }) => {
      const key = projectKey(params)
      const denied = requireRole(db, key, 'Contributor')
      if (denied) {
        return denied
      }
      // a name that is given twice is one value with a comma, as the API reads its query: no state and no token
      const query = new URL(request.url).searchParams
      const filter = parseInboxFilter(query.getAll('state').join(','))
      if (!filter) {
        return invalidQuery("The query parameter 'state' must be Open, Done.")
      }
      const before = decodeInboxToken(query.getAll('continuationToken').join(','))
      if (before === null) {
        return invalidQuery('The continuation token is not one of the inbox; start again without it.')
      }

      return HttpResponse.json(listInbox(db, scopeOf(params), { filter, role: roleOf(db, key), before }))
    }),
  ),

  http.post<ItemParams>(
    `${api}/:id/accept`,
    close((db, scope, id, role, actor) => acceptInboxItem(db, scope, id, role, actor, mockActions)),
  ),

  http.post<ItemParams>(`${api}/:id/dismiss`, close(dismissInboxItem)),
]
