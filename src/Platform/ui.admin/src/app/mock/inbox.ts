import type {
  AccountRole,
  AuditActor,
  ViewExport,
  ViewImport,
  ViewInboxItem,
  ViewInboxItemKind,
  ViewInboxItemLevel,
  ViewInboxItemState,
  ViewInboxJob,
  ViewInboxResponse,
} from '@42for.net/2splatform.sdk'
import { roleRank } from '@42for.net/2splatform.sdk'
import { getArchiveFileName } from './archive-names'
import { accountActor, mockActor } from './audit'
import { scopeSeed, type MockDb } from './db'
import type { ScopeRef } from './documents'
import { showcaseProject } from './showcase'

// The inbox of a view in the mock API (424-31, docs/Platform/Storyteller/inbox.md): the items of every view, kept in
// the mock database, with the rules of the API's publisher (ViewInboxPublisher) and service (ViewInboxService): the ids,
// an idempotent push, the visibility by role, the list with its filter and pages, accept and dismiss with their
// refusals, and the notifications of exports and imports (ViewTransferNotifications). The store of the API removes an
// expired item by its ttl; the mock keeps it and never lists or finds it. The views the mock seeds with data start with
// five items; any other view starts empty.

/** Items per page, as the API pages them (ViewInboxQuery.PageSize). */
export const inboxPageSize = 50

/** Days an item lives when its producer names no ExpiresAt (ViewInbox:RetentionDays of the API). */
export const inboxRetentionDays = 30

const minute = 60_000
const hour = 60 * minute
const day = 24 * hour

const maxTitleLength = 200
const maxTextLength = 2000
/** The longest message of a job, as ViewTransferService cuts the Error it stores. */
const maxJobMessageLength = 500
// An item lives at least a minute and at most a year, whatever its producer asks for.
const minLifetime = minute
const maxLifetime = 365 * day

const codePattern = /^[A-Za-z][A-Za-z0-9]{0,63}$/
const idPattern = /^[0-9]{21}[0-9a-f]{32}$/
const tokenVersion = '1'

/** The platform as the one who caused an item. */
export const systemActor: AuditActor = { Kind: 'System', Id: 'system' }

/** An item as the mock stores it: the members the API always writes are there. */
export interface StoredInboxItem extends ViewInboxItem {
  Id: string
  Kind: ViewInboxItemKind
  Topic: string
  Level: ViewInboxItemLevel
  Title: string
  MinimalRole: AccountRole
  State: ViewInboxItemState
  CreatedAt: string
  CreatedBy: AuditActor
  ExpiresAt: string
}

/** What a producer pushes (ViewInboxPush of the API, without the scope). */
export interface InboxPush {
  Kind: ViewInboxItemKind
  Topic: string
  /** Info when left out. */
  Level?: ViewInboxItemLevel
  Title: string
  Text?: string
  MinimalRole: AccountRole
  CreatedBy: AuditActor
  /** Left out: now plus the retention of the inbox. */
  ExpiresAt?: string
  /** Of an Action: the code of what its acceptance runs. */
  Action?: string
  Job?: ViewInboxJob
  /** Makes the push idempotent, with CreatedAt as the time of the event (required with a Key). */
  Key?: string
  CreatedAt?: string
}

/** The answer of an accept or a dismiss that the API refuses. */
export interface InboxRefusal {
  status: 404 | 409 | 501
  errorCode: string
  message: string
}

export const isInboxRefusal = (answer: StoredInboxItem | InboxRefusal): answer is InboxRefusal => 'status' in answer

/** What the acceptance of an action runs (IViewInboxActionHandler of the API); it may answer the job it started. */
export type InboxActionHandler = (context: {
  db: MockDb
  scope: ScopeRef
  item: StoredInboxItem
  actor: AuditActor
}) => ViewInboxJob | undefined

// --- ids and tokens (ViewInboxIds, ViewInboxService) ---

/** The 21 digits of a time in UTC that an id starts with (yyyyMMddHHmmssfffffff; a Date ends at the milliseconds). */
const timeDigits = (time: string | number | Date) => `${new Date(time).toISOString().replace(/\D/g, '')}0000`

/**
 * 32 hexadecimal digits of a key. The API takes the start of the key's SHA-256; the mock only needs the same digits
 * for the same key, so it needs no real hash.
 */
function keyDigits(key: string): string {
  let digits = ''
  for (let pass = 1; pass <= 4; pass++) {
    let hash = (0x811c9dc5 ^ Math.imul(pass, 0x9e3779b1)) >>> 0
    for (const char of key) {
      hash = Math.imul(hash ^ char.charCodeAt(0), 0x01000193) >>> 0
    }
    digits += hash.toString(16).padStart(8, '0')
  }
  return digits
}

/** An id of the inbox: 21 digits and 32 lower-case hexadecimal digits. Any other id is not found. */
export const isInboxId = (id: string | null | undefined): id is string => !!id && idPattern.test(id)

/** A new id of the time: its order is the time order. */
export const newInboxId = (time: string | number | Date) =>
  `${timeDigits(time)}${crypto.randomUUID().replaceAll('-', '')}`

/** The id of a push with a key: the same key and time give the same id. */
export const inboxIdForKey = (time: string | number | Date, key: string) => `${timeDigits(time)}${keyDigits(key)}`

/** The id of a seeded item: the digits of its seeded time and a fixed suffix with its number. */
export const seededInboxId = (time: string | number | Date, number: number) =>
  `${timeDigits(time)}5eed${String(number).padStart(28, '0')}`

/** The continuation token of a page: base64url, without padding, of `1:` and the id of its last item. */
export const encodeInboxToken = (lastId: string) =>
  btoa(`${tokenVersion}:${lastId}`).replace(/=+$/, '').replaceAll('+', '-').replaceAll('/', '_')

/** The last id of the page before, undefined for the first page, or null for a token the inbox did not issue. */
export function decodeInboxToken(token: string | null | undefined): string | undefined | null {
  if (!token?.trim()) {
    return undefined
  }

  let text: string
  try {
    const base64 = token.trim().replaceAll('-', '+').replaceAll('_', '/')
    text = atob(base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '='))
  } catch {
    return null
  }

  const separator = text.indexOf(':')
  const id = text.slice(separator + 1)
  return separator >= 0 && text.slice(0, separator) === tokenVersion && isInboxId(id) ? id : null
}

// --- visibility and the filter ---

/** A caller sees the items whose minimal role is between Contributor and the caller's own. */
export const isVisibleTo = (item: Pick<ViewInboxItem, 'MinimalRole'>, role: AccountRole) =>
  roleRank(item.MinimalRole) >= roleRank('Contributor') && roleRank(item.MinimalRole) <= roleRank(role)

export type InboxFilter = 'Open' | 'Done'

const filters: readonly InboxFilter[] = ['Open', 'Done']

/**
 * The `state` of the list as the API reads it: Open without one, else one name in any casing or the number of a value;
 * undefined for anything else.
 */
export function parseInboxFilter(raw: string | null | undefined): InboxFilter | undefined {
  if (!raw) {
    return 'Open'
  }
  const text = raw.trim()
  return /^[+-]?[0-9]+$/.test(text)
    ? filters[Number(text)]
    : filters.find((name) => name.toLowerCase() === text.toLowerCase())
}

// --- the store ---

const scopeIdOf = (scope: ScopeRef) => `${scope.org}/${scope.project}/${scope.view}`

const isExpired = (item: StoredInboxItem, now: number) => Date.parse(item.ExpiresAt) <= now

/** The views the mock seeds with data: the scopes of scopeSeed in the legacy datasets, the views of the showcase projects. */
const isSeededScope = (db: MockDb, scope: ScopeRef) =>
  db.scenario === 'showcase'
    ? !!showcaseProject(scope.org, scope.project)?.views.some((view) => view.name === scope.view)
    : !!scopeSeed(db.scenario, scope.org, scope.project, scope.view)

/**
 * The stored items of a view, in the order they were written. Seeded on first use: the views the mock seeds with data
 * start with five items; any other view, a new project of the onboarding included, starts empty.
 */
export function inboxOf(db: MockDb, scope: ScopeRef): StoredInboxItem[] {
  const inboxes = (db.inbox ??= {})
  return (inboxes[scopeIdOf(scope)] ??= isSeededScope(db, scope) ? seedItems(db, scope) : [])
}

// --- the push (ViewInboxPublisher) ---

/** As the jobs cut their messages: the text up to the length, ending with … when it was longer. */
export const cutText = (text: string, length: number) =>
  text.length > length ? `${text.slice(0, length - 1)}…` : text

// A wrong push is an error of its producer, not of a request.
function validate(push: InboxPush): void {
  if (!push.Title?.trim()) {
    throw new Error('An inbox item needs a title.')
  }
  if (!codePattern.test(push.Topic ?? '')) {
    throw new Error(`The topic '${push.Topic}' is not a code: a letter, then up to 63 letters or digits.`)
  }
  if (push.Kind === 'Action' && push.Action === undefined) {
    throw new Error('An action needs the code of its handler.')
  }
  if (push.Kind !== 'Action' && push.Action !== undefined) {
    throw new Error(`Only an action has the code of a handler, not a ${push.Kind.toLowerCase()}.`)
  }
  if (push.Action !== undefined && !codePattern.test(push.Action)) {
    throw new Error(`The action '${push.Action}' is not a code: a letter, then up to 63 letters or digits.`)
  }
  if (roleRank(push.MinimalRole) < roleRank('Contributor')) {
    throw new Error(`The minimal role of an inbox item is Contributor or above, not ${push.MinimalRole}.`)
  }
  if (push.Key !== undefined && !push.CreatedAt) {
    throw new Error('A push with a key needs the time of its event.')
  }
}

/**
 * Pushes an item into the inbox of a view, open. With a Key the push is idempotent: the same key and time meet the
 * stored item, which is answered as it is, also when somebody closed it meanwhile, and nothing is written.
 */
export function pushInboxItem(db: MockDb, scope: ScopeRef, push: InboxPush): StoredInboxItem {
  validate(push)

  const now = Date.now()
  const createdAt = new Date(push.CreatedAt ?? now).toISOString()
  const id = push.Key === undefined ? newInboxId(createdAt) : inboxIdForKey(createdAt, push.Key)
  const items = inboxOf(db, scope)
  const stored = items.findIndex((item) => item.Id === id)

  if (stored >= 0) {
    if (!isExpired(items[stored]!, now)) {
      return items[stored]!
    }
    // the store of the API removed the expired item, so the push creates it again
    items.splice(stored, 1)
  }

  const expiresAt = push.ExpiresAt === undefined ? now + inboxRetentionDays * day : Date.parse(push.ExpiresAt)
  const item: StoredInboxItem = {
    Id: id,
    Kind: push.Kind,
    Topic: push.Topic,
    Level: push.Level ?? 'Info',
    Title: cutText(push.Title, maxTitleLength),
    Text: push.Text === undefined ? undefined : cutText(push.Text, maxTextLength),
    MinimalRole: push.MinimalRole,
    State: 'Open',
    CreatedAt: createdAt,
    CreatedBy: push.CreatedBy,
    // a time outside the limits is moved to the nearer one
    ExpiresAt: new Date(Math.min(Math.max(expiresAt, now + minLifetime), now + maxLifetime)).toISOString(),
    Action: push.Action,
    Job: push.Job,
  }
  items.push(item)
  return item
}

// --- the list, accept and dismiss (ViewInboxService) ---

/** The items of the view that the role sees and that have not expired. */
function visibleItems(db: MockDb, scope: ScopeRef, role: AccountRole): StoredInboxItem[] {
  const now = Date.now()
  return inboxOf(db, scope).filter((item) => !isExpired(item, now) && isVisibleTo(item, role))
}

/**
 * One page of the items the role sees, newest first: the open ones, or the accepted and dismissed ones. `before` is the
 * id of a continuation token: the page holds the items just below it. OpenCount is every open item the role sees.
 */
export function listInbox(
  db: MockDb,
  scope: ScopeRef,
  query: { filter: InboxFilter; role: AccountRole; before?: string },
): ViewInboxResponse {
  const visible = visibleItems(db, scope, query.role)
  const matching = visible
    .filter((item) => (item.State === 'Open') === (query.filter === 'Open'))
    .filter((item) => query.before === undefined || item.Id < query.before)
    .toSorted((left, right) => (left.Id < right.Id ? 1 : left.Id > right.Id ? -1 : 0))
  const page = matching.slice(0, inboxPageSize)
  const last = page.at(-1)

  return {
    Items: page,
    ContinuationToken: matching.length > inboxPageSize && last ? encodeInboxToken(last.Id) : undefined,
    Count: page.length,
    OpenCount: visible.filter((item) => item.State === 'Open').length,
  }
}

const refusal = (status: InboxRefusal['status'], errorCode: string, message: string): InboxRefusal => ({
  status,
  errorCode,
  message,
})

const closed = (item: StoredInboxItem) =>
  refusal(
    409,
    'InboxItemClosed',
    item.State === 'Accepted' ? 'The inbox item was already accepted.' : 'The inbox item was already dismissed.',
  )

/**
 * The item as the caller may see it. The role is looked at before the kind and the state: an item above the role, an
 * expired one, one of another view and an id of another shape all answer as an unknown id does.
 */
function findVisible(db: MockDb, scope: ScopeRef, id: string, role: AccountRole): StoredInboxItem | InboxRefusal {
  const item = isInboxId(id) ? visibleItems(db, scope, role).find((candidate) => candidate.Id === id) : undefined
  return item ?? refusal(404, 'NotFound', `The inbox item '${id}' does not exist or has expired.`)
}

/** Dismisses an open item of any kind, stamped with the actor. */
export function dismissInboxItem(
  db: MockDb,
  scope: ScopeRef,
  id: string,
  role: AccountRole,
  actor: AuditActor,
): StoredInboxItem | InboxRefusal {
  const item = findVisible(db, scope, id, role)
  if (isInboxRefusal(item)) {
    return item
  }
  if (item.State !== 'Open') {
    return closed(item)
  }

  item.State = 'Dismissed'
  item.Closed = { At: new Date().toISOString(), By: actor }
  return item
}

/**
 * Accepts an open action, stamped with the actor, and runs the handler of its action code. The checks come in the order
 * of the API: visible (404), an action (409 InboxItemNotAction), open (409 InboxItemClosed), a handler (501
 * InboxActionNotAvailable, and the item stays open).
 */
export function acceptInboxItem(
  db: MockDb,
  scope: ScopeRef,
  id: string,
  role: AccountRole,
  actor: AuditActor,
  handlers: ReadonlyMap<string, InboxActionHandler> = new Map(),
): StoredInboxItem | InboxRefusal {
  const item = findVisible(db, scope, id, role)
  if (isInboxRefusal(item)) {
    return item
  }
  if (item.Kind !== 'Action') {
    return refusal(409, 'InboxItemNotAction', 'The inbox item is not an action; only an action can be accepted.')
  }
  if (item.State !== 'Open') {
    return closed(item)
  }
  const handler = item.Action === undefined ? undefined : handlers.get(item.Action)
  if (!handler) {
    return refusal(
      501,
      'InboxActionNotAvailable',
      `The action '${item.Action ?? ''}' of the inbox item is not available in this deployment.`,
    )
  }

  // claimed before the work starts, as in the API
  item.State = 'Accepted'
  item.Closed = { At: new Date().toISOString(), By: actor }
  const job = handler({ db, scope, item, actor })
  if (job) {
    item.Job = job
  }
  return item
}

// --- the notifications of exports and imports (ViewTransferNotifications) ---

const sizeUnits = ['KB', 'MB', 'GB'] as const
const bytesPerUnit = 1024

/** One decimal, a half away from zero. A size in a unit is a multiple of a power of two, so the product is exact. */
const round = (value: number) => Math.round(value * 10) / 10

/** "512 B", "1.0 KB", "1.2 MB": one decimal from KB up, and GB for everything above. */
function formatSize(bytes: number): string {
  if (bytes < bytesPerUnit) {
    return `${bytes} B`
  }

  let unit = -1
  let value = bytes
  // rounded before the next unit is asked for, so 1023.96 KB reads 1.0 MB and never 1024.0 KB
  do {
    value /= bytesPerUnit
    unit += 1
  } while (round(value) >= bytesPerUnit && unit < sizeUnits.length - 1)

  return `${round(value).toFixed(1)} ${sizeUnits[unit]}`
}

/**
 * "12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB": the counts of the exported kinds,
 * then the size. A kind that was left out has no count and is not named.
 */
export function exportText(state: ViewExport): string | undefined {
  const parts: string[] = []
  const addCount = (count: number | null | undefined, noun: string) => {
    if (count != null) {
      parts.push(`${count} ${noun}${count === 1 ? '' : 's'}`)
    }
  }

  addCount(state.AnnotationCount, 'annotation')
  addCount(state.SchemaCount, 'schema')
  addCount(state.TemplateCount, 'template')
  addCount(state.ConfigurationCount, 'configuration')
  // versions belong to schemas, templates and configurations: an export of annotations alone has none to name
  if (state.SchemaCount != null || state.TemplateCount != null || state.ConfigurationCount != null) {
    addCount(state.VersionCount, 'version')
  }
  if (state.Size != null) {
    parts.push(formatSize(state.Size))
  }

  return parts.length ? parts.join(', ') : undefined
}

/**
 * "acme.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed", without a file name only the counts. A dry run
 * names what a real run would create and update.
 */
export function importText(state: ViewImport): string | undefined {
  const fileName = state.FileName || undefined
  const summary = state.Summary
  if (!summary) {
    return fileName
  }

  const counts = [
    `${summary.Created ?? 0} ${state.IsDryRun ? 'to create' : 'created'}`,
    `${summary.Updated ?? 0} ${state.IsDryRun ? 'to update' : 'updated'}`,
    `${summary.Unchanged ?? 0} unchanged`,
    `${summary.Skipped ?? 0} skipped`,
    `${summary.Failed ?? 0} failed`,
  ].join(', ')
  return fileName ? `${fileName}: ${counts}` : counts
}

/** Whether the job failed; a job that has not ended has nothing to report. */
function isFailed(kind: 'export' | 'import', state: ViewExport | ViewImport): boolean {
  if (state.Status !== 'Failed' && state.Status !== 'Succeeded') {
    throw new Error(`The ${kind} job '${state.Id}' has not ended; it is ${state.Status}.`)
  }
  return state.Status === 'Failed'
}

/** The members every notification of a job has: for Administrators, by the one who started the job, at its end. */
const jobNotification = (state: ViewExport | ViewImport) => ({
  Kind: 'Notification' as const,
  // the role of the job routes: who may not read a job does not see its end
  MinimalRole: 'Administrator' as const,
  CreatedBy: state.CreatedBy ?? systemActor,
  // every writer of an end sets CompletedAt; the earlier times keep a job without it reportable
  CreatedAt: state.CompletedAt ?? state.StartedAt ?? state.CreatedAt,
})

/**
 * The notification of an export that ended. The key and the time of the end make every push of one job the same item.
 * A result expires with its job, which it leads to; a failure names no expiry and lives as long as any other item.
 */
export function exportNotification(state: ViewExport): InboxPush {
  const failed = isFailed('export', state)
  return {
    ...jobNotification(state),
    Topic: 'ViewExport',
    Level: failed ? 'Error' : 'Success',
    Title: failed ? 'Export failed' : 'Export finished',
    Text: (failed ? state.Error : exportText(state)) ?? undefined,
    ExpiresAt: failed ? undefined : state.ExpiresAt,
    Job: { Kind: 'ViewExport', Id: state.Id, FileName: state.FileName ?? undefined },
    Key: `ViewExport.${state.Id}`,
  }
}

/** The notification of an import that ended; a dry run is a check, and a job that succeeded may hold files that failed. */
export function importNotification(state: ViewImport): InboxPush {
  const failed = isFailed('import', state)
  const level = failed ? 'Error' : (state.Summary?.Failed ?? 0) > 0 ? 'Warning' : 'Success'
  const titles = {
    Error: state.IsDryRun ? 'Check failed' : 'Import failed',
    Warning: state.IsDryRun ? 'Check found problems' : 'Import finished with failures',
    Success: state.IsDryRun ? 'Check finished' : 'Import finished',
  }
  return {
    ...jobNotification(state),
    Topic: 'ViewImport',
    Level: level,
    Title: titles[level],
    Text: (failed ? state.Error : importText(state)) ?? undefined,
    ExpiresAt: failed ? undefined : state.ExpiresAt,
    Job: { Kind: 'ViewImport', Id: state.Id, FileName: state.FileName ?? undefined },
    Key: `ViewImport.${state.Id}`,
  }
}

// --- the seed ---

/** What a job stores when Cosmos DB throttles its reads: the message of the exception, which the job cuts. */
const throttledExport =
  'The export failed: Response status code does not indicate success: TooManyRequests (429); Substatus: 3200; ' +
  'ActivityId: 4c1f0b2a-7d3e-4f5a-9b6c-8d7e6f5a4b3c; Reason: (Message: {"Errors":["Request rate is large. More ' +
  'Request Units may be needed, so no changes were made. Please retry this request later. Learn more: ' +
  'http://aka.ms/cosmosdb-error-429"]} ActivityId: 4c1f0b2a-7d3e-4f5a-9b6c-8d7e6f5a4b3c, Request URI: ' +
  '/apps/0a6b3d9e-1c2f-4e5a-8b7c-9d0e1f2a3b4c/services/7e8f9a0b-1c2d-4e3f-a4b5-c6d7e8f9a0b1/partitions/' +
  '2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e/replicas/134058273650918273p/, RequestStats: , SDK: Windows/10.0.20348 ' +
  'cosmos-netstandard-sdk/3.38.0);'

const suggestionText = (count: number) =>
  `${count} configurations of this view have the same members. ` +
  'A schema generated from them would check their later changes.'

/**
 * Another account that administers the project, as the one who started a job there: of the project first, then of its
 * organization; Grace when nobody else does.
 */
function otherAdministrator(db: MockDb, scope: ScopeRef): AuditActor {
  const members = [`${scope.org}.${scope.project}`, scope.org].flatMap((key) =>
    Object.entries(db.accessPoints[key]?.AccessMap ?? {}),
  )
  const other = members.find(([id, role]) => id !== db.user.id && roleRank(role) >= roleRank('Administrator'))
  return accountActor(other?.[0] ?? 'user_01MOCKGRACE')
}

/**
 * The five items a seeded view starts with, oldest first, with times relative to now: two closed ones (an accepted
 * action; the result of an import, dismissed, whose job the mock never had, so it names none) and three open ones (a
 * message, a failed export and a suggested action). The results of jobs are for Administrators, the others for
 * Contributors. An id is the digits of the item's time and a fixed suffix; tests find the items by their titles.
 */
function seedItems(db: MockDb, scope: ScopeRef): StoredInboxItem[] {
  const now = Date.now()
  const at = (ago: number) => new Date(now - ago).toISOString()
  const retention = inboxRetentionDays * day
  const other = otherAdministrator(db, scope)

  return [
    {
      Id: seededInboxId(at(3 * day), 1),
      Kind: 'Action',
      Topic: 'SchemaSuggestion',
      Level: 'Info',
      Title: 'Generate a schema for 7 similar configurations',
      Text: suggestionText(7),
      MinimalRole: 'Contributor',
      State: 'Accepted',
      CreatedAt: at(3 * day),
      CreatedBy: systemActor,
      ExpiresAt: at(3 * day - retention),
      Closed: { At: at(2 * day), By: mockActor(db) },
      Action: 'GenerateSchema',
    },
    {
      Id: seededInboxId(at(2 * day), 2),
      Kind: 'Message',
      Topic: 'SchemaSuggestion',
      Level: 'Info',
      Title: 'Schema suggestions are on for this view',
      Text:
        'The platform compares the configurations of this view and suggests a schema where several of them have the ' +
        'same members. A suggestion arrives here as an action: accept it to generate the schema, or dismiss it.',
      MinimalRole: 'Contributor',
      State: 'Open',
      CreatedAt: at(2 * day),
      CreatedBy: systemActor,
      ExpiresAt: at(2 * day - retention),
    },
    {
      Id: seededInboxId(at(5 * hour), 3),
      Kind: 'Notification',
      Topic: 'ViewImport',
      Level: 'Success',
      Title: 'Import finished',
      Text: importText({
        FileName: 'catalog.zip',
        Summary: { Created: 3, Updated: 2, Unchanged: 10, Skipped: 1, Failed: 0 },
      }),
      MinimalRole: 'Administrator',
      State: 'Dismissed',
      CreatedAt: at(5 * hour),
      CreatedBy: mockActor(db),
      // a result expires with its job, a day after its end
      ExpiresAt: at(5 * hour - day),
      Closed: { At: at(4 * hour), By: other },
    },
    {
      Id: seededInboxId(at(3 * hour), 4),
      Kind: 'Notification',
      Topic: 'ViewExport',
      Level: 'Error',
      Title: 'Export failed',
      Text: cutText(throttledExport, maxJobMessageLength),
      MinimalRole: 'Administrator',
      State: 'Open',
      CreatedAt: at(3 * hour),
      CreatedBy: other,
      ExpiresAt: at(3 * hour - retention),
      Job: {
        Kind: 'ViewExport',
        Id: '0a1b2c3d4e5f40718293a4b5c6d7e8f9',
        FileName: getArchiveFileName(scope.org, scope.project, scope.view, 'Folder', new Date(now - 3 * hour - minute)),
      },
    },
    {
      Id: seededInboxId(at(25 * minute), 5),
      Kind: 'Action',
      Topic: 'SchemaSuggestion',
      Level: 'Info',
      Title: 'Generate a schema for 4 similar configurations',
      Text: suggestionText(4),
      MinimalRole: 'Contributor',
      State: 'Open',
      CreatedAt: at(25 * minute),
      CreatedBy: systemActor,
      ExpiresAt: at(25 * minute - retention),
      Action: 'GenerateSchema',
    },
  ]
}
