import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { setupServer } from 'msw/node'
import {
  acceptViewInboxItem,
  createAccessPoint,
  dismissViewInboxItem,
  getViewExport,
  getViewImport,
  getViewInbox,
  startViewExport,
  startViewImport,
  type AccountRole,
  type StartViewImportParams,
  type ViewExportCreate,
  type ViewInboxItem,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'
import { roleOf } from '@/mock/handlers'
import {
  encodeInboxToken,
  exportNotification,
  exportText,
  inboxOf,
  inboxPageSize,
  pushInboxItem,
  systemActor,
  type InboxPush,
  type StoredInboxItem,
} from '@/mock/inbox'
import { resetTransfers } from '@/mock/transfer-handlers'
import { createZip, textFile, type ZipFile } from '@/mock/zip'

// The inbox of a view in the mock API (424-31), through the SDK: the three routes answer as the API does
// (docs/Platform/Storyteller/inbox.md), but for the acceptance of the action GenerateSchema, which the mock runs. Ada is
// the Owner of acme.billing, a Contributor of acme.shipping and a Reader of northwind.portal; the default views of the
// three projects start with the five seeded items.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  resetTransfers()
  vi.useRealTimers()
})
afterAll(() => server.close())

type Target = readonly [org: string, project: string, view: string]

const billing: Target = ['acme', 'billing', 'default']
const shipping: Target = ['acme', 'shipping', 'default']
const portal: Target = ['northwind', 'portal', 'default']
/** A view of acme.billing that the mock does not seed. */
const empty: Target = ['acme', 'billing', 'empty']

const ada = { Kind: 'Account', Id: 'user_01MOCKADA', Name: 'Ada Lovelace' }
const grace = { Kind: 'Account', Id: 'user_01MOCKGRACE', Name: 'Grace Hopper' }

// The seeded items, by their titles.
const suggestion = 'Generate a schema for 4 similar configurations'
const failedExport = 'Export failed'
const note = 'Schema suggestions are on for this view'
const finishedImport = 'Import finished'
const accepted = 'Generate a schema for 7 similar configurations'

const scopeOf = ([org, project, view]: Target) => ({ org, project, view })
const list = (target: Target, state?: string, continuationToken?: string) =>
  getViewInbox(...target, { state, continuationToken })
const accept = (target: Target, id: string) => acceptViewInboxItem(...target, id)
const dismiss = (target: Target, id: string) => dismissViewInboxItem(...target, id)
const titles = (items: ViewInboxItem[] | undefined) => items?.map((item) => item.Title)

/** Sets the mock user's role on a project in the stored database, which every route reads. */
function setRole(key: string, role: AccountRole) {
  const db = loadDb()
  db.account!.AccessMap![key] = role
  saveDb(db)
}

/** The stored items of a view, whatever the role of the mock user; a seeded view is seeded by it. */
function stored(target: Target): StoredInboxItem[] {
  const db = loadDb()
  const items = inboxOf(db, scopeOf(target))
  saveDb(db)
  return items
}

const storedByTitle = (target: Target, title: string) => stored(target).find((item) => item.Title === title)!

/** Pushes items into the stored inbox of a view, as a producer of the back end would. */
function push(target: Target, ...pushes: InboxPush[]): StoredInboxItem[] {
  const db = loadDb()
  const items = pushes.map((item) => pushInboxItem(db, scopeOf(target), item))
  saveDb(db)
  return items
}

const message = (title: string, more: Partial<InboxPush> = {}): InboxPush => ({
  Kind: 'Message',
  Topic: 'Test',
  Title: title,
  MinimalRole: 'Contributor',
  CreatedBy: systemActor,
  ...more,
})

const action = (title: string, code: string, more: Partial<InboxPush> = {}): InboxPush =>
  message(title, { Kind: 'Action', Topic: 'SchemaSuggestion', Action: code, ...more })

const notFound = (id: string) => ({
  status: 404,
  errorCode: 'NotFound',
  message: `The inbox item '${id}' does not exist or has expired.`,
})
const notAction = {
  status: 409,
  errorCode: 'InboxItemNotAction',
  message: 'The inbox item is not an action; only an action can be accepted.',
}
const alreadyAccepted = { status: 409, errorCode: 'InboxItemClosed', message: 'The inbox item was already accepted.' }
const alreadyDismissed = { status: 409, errorCode: 'InboxItemClosed', message: 'The inbox item was already dismissed.' }

describe('mock inbox: the seed and the roles', () => {
  it('starts a seeded view with five items for its Owner: three open and two done, newest first', async () => {
    const before = Date.now()
    const open = await list(billing)
    const done = await list(billing, 'Done')
    const items = [...(open.Items ?? []), ...(done.Items ?? [])]

    expect(titles(open.Items)).toEqual([suggestion, failedExport, note])
    expect(titles(done.Items)).toEqual([finishedImport, accepted])
    expect(open).toMatchObject({ Count: 3, OpenCount: 3 })
    expect(done).toMatchObject({ Count: 2, OpenCount: 3 })
    expect(open).not.toHaveProperty('ContinuationToken')
    expect(done).not.toHaveProperty('ContinuationToken')

    // every item has an id of its time, arrived some minutes to days ago and has not expired
    for (const item of items) {
      expect(item.Id, item.Title).toMatch(/^[0-9]{21}[0-9a-f]{32}$/)
      expect(item.Id!.slice(0, 17)).toBe(item.CreatedAt!.replace(/\D/g, ''))
      expect(Date.parse(item.CreatedAt!)).toBeLessThan(before - 10 * 60_000)
      expect(Date.parse(item.CreatedAt!)).toBeGreaterThan(before - 7 * 86_400_000)
      expect(Date.parse(item.ExpiresAt!)).toBeGreaterThan(Date.now() + 3_600_000)
    }

    const [suggested, failed, text] = open.Items!
    expect(suggested).toMatchObject({
      Kind: 'Action',
      Topic: 'SchemaSuggestion',
      Level: 'Info',
      MinimalRole: 'Contributor',
      State: 'Open',
      CreatedBy: { Kind: 'System', Id: 'system' },
      Action: 'GenerateSchema',
    })
    expect(suggested).not.toHaveProperty('Closed')
    expect(failed).toMatchObject({
      Kind: 'Notification',
      Topic: 'ViewExport',
      Level: 'Error',
      MinimalRole: 'Administrator',
      State: 'Open',
      CreatedBy: grace,
      Job: { Kind: 'ViewExport', FileName: expect.stringMatching(/^export-acme-billing-default-\d{8}-\d{6}Z\.zip$/) },
    })
    expect(failed!.Text).toHaveLength(500)
    expect(failed!.Text).toMatch(/^The export failed: .*…$/)
    expect(text).toMatchObject({ Kind: 'Message', MinimalRole: 'Contributor', State: 'Open', CreatedBy: systemActor })

    const [imported, acceptedAction] = done.Items!
    expect(imported).toMatchObject({
      Kind: 'Notification',
      Topic: 'ViewImport',
      Level: 'Success',
      MinimalRole: 'Administrator',
      State: 'Dismissed',
      Closed: { At: expect.any(String), By: grace },
    })
    expect(imported).not.toHaveProperty('Job')
    expect(acceptedAction).toMatchObject({
      Kind: 'Action',
      MinimalRole: 'Contributor',
      State: 'Accepted',
      Closed: { At: expect.any(String), By: ada },
      Action: 'GenerateSchema',
    })
  })

  it.each(['Contributor', 'ContributorWithSecrets'] as const)(
    "shows a %s only the items for Contributors, and never an Administrator's",
    async (role) => {
      setRole('acme.shipping', role)

      const open = await list(shipping)
      const done = await list(shipping, 'Done')

      expect(titles(open.Items)).toEqual([suggestion, note])
      expect(titles(done.Items)).toEqual([accepted])
      expect(open).toMatchObject({ Count: 2, OpenCount: 2 })
      expect(done).toMatchObject({ Count: 1, OpenCount: 2 })
      expect([...open.Items!, ...done.Items!].map((item) => item.MinimalRole)).toEqual([
        'Contributor',
        'Contributor',
        'Contributor',
      ])
      // the Administrators' items are in the view all the same
      expect(stored(shipping).map((item) => item.MinimalRole)).toContain('Administrator')
    },
  )

  it.each(['Administrator', 'Owner'] as const)('shows an %s every item', async (role) => {
    expect(roleOf(loadDb(), 'acme.shipping')).toBe('Contributor')
    setRole('acme.shipping', role)

    expect(titles((await list(shipping)).Items)).toEqual([suggestion, failedExport, note])
    expect(titles((await list(shipping, 'Done')).Items)).toEqual([finishedImport, accepted])
    expect((await list(shipping)).OpenCount).toBe(3)
  })

  it('shows a ContributorWithSecrets the items of that role too, and a Contributor none of them', async () => {
    push(shipping, message('For secrets', { MinimalRole: 'ContributorWithSecrets' }), message('For owners', { MinimalRole: 'Owner' }))

    expect(titles((await list(shipping)).Items)).not.toContain('For secrets')
    setRole('acme.shipping', 'ContributorWithSecrets')
    expect(titles((await list(shipping)).Items)).toEqual(['For secrets', suggestion, note])
    setRole('acme.shipping', 'Administrator')
    expect(titles((await list(shipping)).Items)).not.toContain('For owners')
    setRole('acme.shipping', 'Owner')
    expect((await list(shipping)).OpenCount).toBe(5)
  })

  it.each([
    [portal, 'Reader'],
    [['acme', 'returns', 'default'], 'None'],
  ] as [Target, AccountRole][])('refuses the three routes below Contributor: %j, %s', async (target, role) => {
    const key = `${target[0]}.${target[1]}`
    expect(roleOf(loadDb(), key)).toBe(role)
    const items = stored(target)
    const id = items[0]?.Id ?? stored(billing)[0]!.Id
    const before = JSON.stringify(items)

    for (const call of [
      () => list(target),
      () => list(target, 'Done'),
      // the role comes first: a wrong state, a wrong token and an id of another shape answer 403 too
      () => list(target, 'Everything'),
      () => list(target, undefined, 'not-a-token'),
      () => accept(target, id),
      () => dismiss(target, id),
      () => dismiss(target, 'nope'),
    ]) {
      await expect(call()).rejects.toMatchObject({
        status: 403,
        errorCode: 'AccessDenied',
        message: `The Contributor role on ${key} is required.`,
      })
    }
    expect(JSON.stringify(stored(target))).toBe(before)
  })

  it('starts a view that the mock does not seed empty, also the view of a new project', async () => {
    await createAccessPoint({ Organization: 'acme', Project: 'returns' })

    for (const target of [empty, ['acme', 'returns', 'default'] as const]) {
      for (const state of ['Open', 'Done']) {
        const page = await list(target, state)
        expect(page).toEqual({ Items: [], Count: 0, OpenCount: 0 })
      }
    }
    await expect(dismiss(empty, storedByTitle(billing, note).Id)).rejects.toMatchObject({ status: 404 })
  })
})

describe('mock inbox: the list', () => {
  it('lists the open items by default, and the accepted and dismissed ones as Done, in any casing or by number', async () => {
    for (const state of [undefined, '', 'Open', 'open', 'OPEN', '0']) {
      expect(titles((await list(billing, state)).Items), String(state)).toEqual([suggestion, failedExport, note])
    }
    for (const state of ['Done', 'done', 'DONE', '1']) {
      const done = await list(billing, state)
      expect(titles(done.Items), state).toEqual([finishedImport, accepted])
      expect(done.Items!.map((item) => item.State)).toEqual(['Dismissed', 'Accepted'])
    }
  })

  it.each(['Accepted', 'Dismissed', 'All', 'Open,Done', '2', ' '])('refuses the state %j', async (state) => {
    await expect(list(billing, state)).rejects.toMatchObject({
      status: 400,
      errorCode: 'InvalidQuery',
      message: "The query parameter 'state' must be Open, Done.",
    })
  })

  it('refuses a state that is given twice: the API reads the two as one value with a comma', async () => {
    // the SDK sends a name once, so this is a plain request
    const response = await fetch('http://api.test/api/v1/acme/billing/default/inbox?state=Open&state=Done')

    expect(response.status).toBe(400)
    expect(await response.json()).toMatchObject({
      ErrorCode: 'InvalidQuery',
      Message: "The query parameter 'state' must be Open, Done.",
    })
  })

  it('pages 50 items at a time, newest first, with the token of the last item of the page', async () => {
    const start = Date.now() - 3_600_000
    push(
      empty,
      ...Array.from({ length: 120 }, (_, index) =>
        message(`Message ${index}`, { CreatedAt: new Date(start + index * 1000).toISOString() }),
      ),
    )
    const range = (from: number, to: number) =>
      Array.from({ length: from - to + 1 }, (_, index) => `Message ${from - index}`)

    const first = await list(empty)
    expect(inboxPageSize).toBe(50)
    expect(titles(first.Items)).toEqual(range(119, 70))
    expect(first).toMatchObject({ Count: 50, OpenCount: 120 })
    // base64url of 1: and the id, without padding, as the API writes it
    expect(first.ContinuationToken).toBe(encodeInboxToken(first.Items!.at(-1)!.Id!))
    expect(first.ContinuationToken).toMatch(/^[A-Za-z0-9_-]+$/)
    expect(atob(first.ContinuationToken!.replaceAll('-', '+').replaceAll('_', '/'))).toBe(
      `1:${first.Items!.at(-1)!.Id}`,
    )

    // an item that arrives between two pages shifts none: the next page starts below the token
    push(empty, message('Newer'))
    const second = await list(empty, 'Open', first.ContinuationToken)
    expect(titles(second.Items)).toEqual(range(69, 20))
    expect(second).toMatchObject({ Count: 50, OpenCount: 121 })
    expect(second.ContinuationToken).toBe(encodeInboxToken(second.Items!.at(-1)!.Id!))

    const third = await list(empty, 'Open', second.ContinuationToken)
    expect(titles(third.Items)).toEqual(range(19, 0))
    expect(third).toMatchObject({ Count: 20, OpenCount: 121 })
    expect(third).not.toHaveProperty('ContinuationToken')

    // read again from the start, the list begins with the new item
    expect(titles((await list(empty)).Items)!.slice(0, 2)).toEqual(['Newer', 'Message 119'])
    // a blank token is the first page, and the Done list counts the open items too
    expect(titles((await list(empty, 'Open', ' ')).Items)![0]).toBe('Newer')
    expect(await list(empty, 'Done')).toEqual({ Items: [], Count: 0, OpenCount: 121 })
  })

  it('names a next page only when one follows, and pages the done items the same way', async () => {
    const start = Date.now() - 3_600_000
    const items = push(
      empty,
      ...Array.from({ length: 51 }, (_, index) =>
        message(`Message ${index}`, { CreatedAt: new Date(start + index * 1000).toISOString() }),
      ),
    )

    const first = await list(empty)
    expect(first.Count).toBe(50)
    const second = await list(empty, 'Open', first.ContinuationToken)
    expect(titles(second.Items)).toEqual(['Message 0'])
    expect(second).not.toHaveProperty('ContinuationToken')

    // exactly a page of open items is the last page
    await dismiss(empty, items[0]!.Id)
    const open = await list(empty)
    expect(open).toMatchObject({ Count: 50, OpenCount: 50 })
    expect(open).not.toHaveProperty('ContinuationToken')

    for (const item of items.slice(1)) {
      await dismiss(empty, item.Id)
    }
    const done = await list(empty, 'Done')
    expect(done).toMatchObject({ Count: 50, OpenCount: 0 })
    expect(titles(done.Items)![0]).toBe('Message 50')
    expect(titles((await list(empty, 'Done', done.ContinuationToken)).Items)).toEqual(['Message 0'])
  })

  it('refuses a continuation token that the inbox did not issue', async () => {
    const id = storedByTitle(billing, note).Id

    for (const token of ['not-a-token', btoa('1:abc'), btoa(`2:${id}`), btoa(id), btoa(JSON.stringify(['', id]))]) {
      await expect(list(billing, 'Open', token), token).rejects.toMatchObject({
        status: 400,
        errorCode: 'InvalidQuery',
        message: 'The continuation token is not one of the inbox; start again without it.',
      })
    }
    // a token of the inbox is taken as it is: the page below the oldest open item is empty
    expect(await list(billing, 'Open', encodeInboxToken(id))).toEqual({ Items: [], Count: 0, OpenCount: 3 })
  })

  it('refuses a continuation token that is given twice, although each of the two is one of the inbox', async () => {
    const token = encodeInboxToken(storedByTitle(billing, note).Id)
    expect(await list(billing, 'Open', token)).toMatchObject({ Count: 0, OpenCount: 3 })

    const response = await fetch(
      `http://api.test/api/v1/acme/billing/default/inbox?continuationToken=${token}&continuationToken=${token}`,
    )

    expect(response.status).toBe(400)
    expect(await response.json()).toMatchObject({
      ErrorCode: 'InvalidQuery',
      Message: 'The continuation token is not one of the inbox; start again without it.',
    })
  })

  it('counts every open item the caller sees, whatever the page and the filter', async () => {
    expect((await list(billing)).OpenCount).toBe(3)
    expect((await list(billing, 'Done')).OpenCount).toBe(3)
    expect((await list(shipping)).OpenCount).toBe(2)

    await dismiss(billing, storedByTitle(billing, note).Id)
    push(billing, message('One more'), message('For owners', { MinimalRole: 'Owner' }))

    expect(await list(billing)).toMatchObject({ Count: 4, OpenCount: 4 })
    expect(await list(billing, 'Done')).toMatchObject({ Count: 3, OpenCount: 4 })
    setRole('acme.billing', 'Contributor')
    expect(await list(billing)).toMatchObject({ Count: 2, OpenCount: 2 })
  })

  it('neither lists nor counts nor finds an item that has expired', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    const [short, long] = push(
      empty,
      message('Short', { ExpiresAt: new Date(Date.now() + 5 * 60_000).toISOString() }),
      message('Long'),
    )
    expect(await list(empty)).toMatchObject({ Count: 2, OpenCount: 2 })

    vi.setSystemTime(Date.now() + 5 * 60_000)

    const open = await list(empty)
    expect(titles(open.Items)).toEqual(['Long'])
    expect(open.OpenCount).toBe(1)
    await expect(dismiss(empty, short!.Id)).rejects.toMatchObject(notFound(short!.Id))
    await expect(accept(empty, short!.Id)).rejects.toMatchObject(notFound(short!.Id))
    expect((await dismiss(empty, long!.Id)).State).toBe('Dismissed')
    // a closed item expires as an open one does
    vi.setSystemTime(Date.now() + 31 * 86_400_000)
    expect(await list(empty, 'Done')).toEqual({ Items: [], Count: 0, OpenCount: 0 })
  })
})

describe('mock inbox: dismiss', () => {
  it('dismisses an open item of any kind, stamped with the mock user, and lists it as done', async () => {
    const before = Date.now()

    for (const title of [note, failedExport, suggestion]) {
      const item = storedByTitle(billing, title)

      const dismissed = await dismiss(billing, item.Id)

      expect(dismissed).toEqual({ ...item, State: 'Dismissed', Closed: { At: expect.any(String), By: ada } })
      expect(Date.parse(dismissed.Closed!.At!)).toBeGreaterThanOrEqual(before)
    }

    expect(await list(billing)).toEqual({ Items: [], Count: 0, OpenCount: 0 })
    // the done items keep the order of their arrival, not of their closing
    expect(titles((await list(billing, 'Done')).Items)).toEqual([suggestion, failedExport, finishedImport, note, accepted])
  })

  it('refuses an item that was already dismissed or accepted with a conflict that says which', async () => {
    const item = storedByTitle(billing, note)
    await dismiss(billing, item.Id)

    await expect(dismiss(billing, item.Id)).rejects.toMatchObject(alreadyDismissed)
    await expect(dismiss(billing, storedByTitle(billing, finishedImport).Id)).rejects.toMatchObject(alreadyDismissed)
    await expect(dismiss(billing, storedByTitle(billing, accepted).Id)).rejects.toMatchObject(alreadyAccepted)
    // the first stamp stays
    expect(storedByTitle(billing, finishedImport).Closed?.By).toEqual(grace)
  })
})

describe('mock inbox: accept', () => {
  it('runs the mock action GenerateSchema at once: the item is accepted and the notification of its result arrives', async () => {
    const item = storedByTitle(billing, suggestion)
    const before = Date.now()

    const answer = await accept(billing, item.Id)

    expect(answer).toEqual({ ...item, State: 'Accepted', Closed: { At: expect.any(String), By: ada } })
    expect(Date.parse(answer.Closed!.At!)).toBeGreaterThanOrEqual(before)
    expect(answer).not.toHaveProperty('Job')

    const open = await list(billing)
    expect(titles(open.Items)).toEqual(['Schema generation finished', failedExport, note])
    expect(open.OpenCount).toBe(3)
    const result = open.Items![0]!
    expect(result).toEqual({
      Id: expect.stringMatching(/^[0-9]{21}[0-9a-f]{32}$/),
      Kind: 'Notification',
      Topic: 'SchemaSuggestion',
      Level: 'Success',
      Title: 'Schema generation finished',
      Text: expect.stringMatching(/schema .* generated/),
      MinimalRole: 'Contributor',
      State: 'Open',
      CreatedAt: expect.any(String),
      CreatedBy: { Kind: 'System', Id: 'system' },
      ExpiresAt: expect.any(String),
    })
    expect(Date.parse(result.CreatedAt!)).toBeGreaterThanOrEqual(before)
    // it lives as long as any other item
    expect(Date.parse(result.ExpiresAt!) - Date.parse(result.CreatedAt!)).toBe(30 * 86_400_000)
    expect(titles((await list(billing, 'Done')).Items)).toEqual([suggestion, finishedImport, accepted])

    // the accepted item is closed, and the result is a notification: dismissed, never accepted
    await expect(accept(billing, item.Id)).rejects.toMatchObject(alreadyAccepted)
    await expect(dismiss(billing, item.Id)).rejects.toMatchObject(alreadyAccepted)
    await expect(accept(billing, result.Id!)).rejects.toMatchObject(notAction)
    expect((await dismiss(billing, result.Id!)).State).toBe('Dismissed')
  })

  it('gives the notification of the result the minimal role of the accepted action', async () => {
    const [forAdministrators] = push(billing, action('Generate a schema for Administrators', 'GenerateSchema', { MinimalRole: 'Administrator' }))

    await accept(billing, forAdministrators!.Id)

    expect((await list(billing)).Items!.find((item) => item.Title === 'Schema generation finished')).toMatchObject({
      MinimalRole: 'Administrator',
      CreatedBy: systemActor,
    })
    setRole('acme.billing', 'Contributor')
    expect(titles((await list(billing)).Items)).toEqual([suggestion, note])
  })

  it('lets a Contributor accept an action for Contributors', async () => {
    const item = storedByTitle(shipping, suggestion)

    expect(await accept(shipping, item.Id)).toMatchObject({ State: 'Accepted', Closed: { By: ada } })
    expect(titles((await list(shipping)).Items)).toEqual(['Schema generation finished', note])
  })

  it.each(['ExportView', 'generateschema', 'constructor'])(
    'answers 501 for the action code %s, as the API does for every action today, and leaves the item open',
    async (code) => {
      const [item] = push(empty, action('Another action', code))

      await expect(accept(empty, item!.Id)).rejects.toMatchObject({
        status: 501,
        errorCode: 'InboxActionNotAvailable',
        message: `The action '${code}' of the inbox item is not available in this deployment.`,
      })

      expect(await list(empty)).toMatchObject({ Count: 1, OpenCount: 1, Items: [{ State: 'Open' }] })
      expect(stored(empty)[0]).not.toHaveProperty('Closed')
      // it can still be dismissed
      expect((await dismiss(empty, item!.Id)).State).toBe('Dismissed')
    },
  )

  it('refuses to accept a notification or a message, open or closed: only an action can be accepted', async () => {
    for (const title of [failedExport, note, finishedImport]) {
      await expect(accept(billing, storedByTitle(billing, title).Id), title).rejects.toMatchObject(notAction)
    }
    expect(stored(billing).map((item) => item.State)).toEqual(['Accepted', 'Open', 'Dismissed', 'Open', 'Open'])
  })

  it('refuses an action that was dismissed with a conflict, before it looks for what would run it', async () => {
    const [known, unknown] = push(empty, action('Known', 'GenerateSchema'), action('Unknown', 'ExportView'))
    await dismiss(empty, known!.Id)
    await dismiss(empty, unknown!.Id)

    await expect(accept(empty, known!.Id)).rejects.toMatchObject(alreadyDismissed)
    await expect(accept(empty, unknown!.Id)).rejects.toMatchObject(alreadyDismissed)
    expect(await list(empty)).toEqual({ Items: [], Count: 0, OpenCount: 0 })
  })
})

describe('mock inbox: what the caller may not see', () => {
  it("answers a Contributor 404 for an Administrator's item, never a conflict that would tell it exists", async () => {
    expect(roleOf(loadDb(), 'acme.shipping')).toBe('Contributor')
    const open = storedByTitle(shipping, failedExport)
    const closed = storedByTitle(shipping, finishedImport)
    const [forAdministrators] = push(shipping, action('Generate a schema for Administrators', 'GenerateSchema', { MinimalRole: 'Administrator' }))
    const before = JSON.stringify(stored(shipping))

    // an Administrator would get 409 InboxItemNotAction, 200, 409 InboxItemNotAction, 409 InboxItemClosed and 200
    for (const [call, id] of [
      [accept, open.Id],
      [dismiss, open.Id],
      [accept, closed.Id],
      [dismiss, closed.Id],
      [accept, forAdministrators!.Id],
      [dismiss, forAdministrators!.Id],
    ] as const) {
      await expect(call(shipping, id)).rejects.toMatchObject(notFound(id))
    }
    expect(JSON.stringify(stored(shipping))).toBe(before)

    setRole('acme.shipping', 'Administrator')
    await expect(accept(shipping, open.Id)).rejects.toMatchObject(notAction)
    await expect(dismiss(shipping, closed.Id)).rejects.toMatchObject(alreadyDismissed)
    expect((await accept(shipping, forAdministrators!.Id)).State).toBe('Accepted')
    expect((await dismiss(shipping, open.Id)).State).toBe('Dismissed')
  })

  it('answers 404 for an unknown id, an id of another shape and an item of another view', async () => {
    const item = storedByTitle(billing, suggestion)
    const unknown = `${item.Id.slice(0, 21)}${'0'.repeat(32)}`
    const [pushed] = push(billing, message('Only in the default view'))

    for (const id of [unknown, 'nope', item.Id.slice(1), `${item.Id}0`, item.Id.replace('5eed', '5EED')]) {
      await expect(accept(billing, id), id).rejects.toMatchObject(notFound(id))
      await expect(dismiss(billing, id), id).rejects.toMatchObject(notFound(id))
    }
    // an item of the default view is none of another view, seeded or not
    for (const other of [['acme', 'billing', 'staging'], empty, shipping] as Target[]) {
      await expect(accept(other, pushed!.Id)).rejects.toMatchObject(notFound(pushed!.Id))
      await expect(dismiss(other, pushed!.Id)).rejects.toMatchObject(notFound(pushed!.Id))
    }
    expect(stored(billing).map((entry) => entry.State)).toEqual(['Accepted', 'Open', 'Dismissed', 'Open', 'Open', 'Open'])
    expect((await dismiss(billing, pushed!.Id)).State).toBe('Dismissed')
  })
})

describe('mock inbox: the ends of exports and imports', () => {
  const [org, project, view] = billing

  /** Runs an export of the view to its end (the second read of a mock job ends it). */
  async function exportView(body: ViewExportCreate = { Format: 'Folder' }) {
    const started = await startViewExport(org, project, view, body)
    await getViewExport(org, project, view, started.Id!)
    return await getViewExport(org, project, view, started.Id!)
  }

  /** Runs an import of the files into the view to its end. */
  async function importFiles(files: ZipFile[], params: StartViewImportParams = {}) {
    const started = await startViewImport(org, project, view, new Blob([createZip(files)]), params)
    await getViewImport(org, project, view, started.Id!)
    return await getViewImport(org, project, view, started.Id!)
  }

  const edited = () => [textFile('annotations/rst.invoicing.json', '{"Title":"Edited"}')]
  const invalid = () => [textFile('annotations/rst.long.json', JSON.stringify({ Title: 'x'.repeat(201) }))]

  /** Every item of the view that reports the job, open or done. */
  async function itemsOf(jobId: string | undefined) {
    const items = [...((await list(billing)).Items ?? []), ...((await list(billing, 'Done')).Items ?? [])]
    return items.filter((item) => item.Job?.Id === jobId)
  }

  it('leaves one notification of an export that ended, with the counts and the size, which expires with the job', async () => {
    const started = await startViewExport(org, project, view, { Format: 'Folder' })
    expect(await itemsOf(started.Id)).toEqual([])
    // a job in progress has no item
    expect((await getViewExport(org, project, view, started.Id!)).Status).toBe('Running')
    expect(await itemsOf(started.Id)).toEqual([])

    const state = await getViewExport(org, project, view, started.Id!)
    expect(state.Status).toBe('Succeeded')
    // later reads of the job report nothing again
    await getViewExport(org, project, view, started.Id!)
    await getViewExport(org, project, view, started.Id!)

    const items = await itemsOf(state.Id)
    expect(items).toEqual([
      {
        Id: expect.stringMatching(/^[0-9]{21}[0-9a-f]{32}$/),
        Kind: 'Notification',
        Topic: 'ViewExport',
        Level: 'Success',
        Title: 'Export finished',
        Text: exportText(state),
        MinimalRole: 'Administrator',
        State: 'Open',
        CreatedAt: state.CompletedAt,
        CreatedBy: ada,
        ExpiresAt: state.ExpiresAt,
        Job: { Kind: 'ViewExport', Id: state.Id, FileName: state.FileName },
      },
    ])
    expect(items[0]!.Text).toBe(
      `${state.AnnotationCount} annotations, ${state.SchemaCount} schemas, 2 templates, ` +
        `${state.ConfigurationCount} configurations, ${state.VersionCount} versions, ` +
        `${(state.Size! / 1024).toFixed(1)} KB`,
    )
    expect(state.Size).toBeGreaterThan(1024)
    expect(state.Size).toBeLessThan(1024 * 1000)
    expect(items[0]!.Id!.slice(0, 17)).toBe(state.CompletedAt!.replace(/\D/g, ''))
    // the newest item of the view
    expect(titles((await list(billing)).Items)).toEqual(['Export finished', suggestion, failedExport, note])

    // a second push of the same end is the same item
    const db = loadDb()
    expect(pushInboxItem(db, scopeOf(billing), exportNotification(state))).toEqual(items[0])
    saveDb(db)
    expect(await itemsOf(state.Id)).toHaveLength(1)
  })

  it('names no versions in the notification of an export of annotations alone', async () => {
    const state = await exportView({ Format: 'Folder', Include: ['Annotations'] })

    const [item] = await itemsOf(state.Id)

    expect(state.VersionCount).toBe(0)
    expect(item!.Text).toMatch(new RegExp(`^${state.AnnotationCount} annotations, \\d+\\.\\d KB$`))
  })

  it('leaves one notification of an import that ended, with the file and every count, which expires with the job', async () => {
    const state = await importFiles(edited(), { fileName: 'acme.zip' })
    await getViewImport(org, project, view, state.Id!)

    expect(state).toMatchObject({ Status: 'Succeeded', Summary: { Updated: 1, Failed: 0 } })
    expect(await itemsOf(state.Id)).toEqual([
      {
        Id: expect.stringMatching(/^[0-9]{21}[0-9a-f]{32}$/),
        Kind: 'Notification',
        Topic: 'ViewImport',
        Level: 'Success',
        Title: 'Import finished',
        Text: 'acme.zip: 0 created, 1 updated, 0 unchanged, 0 skipped, 0 failed',
        MinimalRole: 'Administrator',
        State: 'Open',
        CreatedAt: state.CompletedAt,
        CreatedBy: ada,
        ExpiresAt: state.ExpiresAt,
        Job: { Kind: 'ViewImport', Id: state.Id, FileName: 'acme.zip' },
      },
    ])
  })

  it('words the notification of a dry run as a check that wrote nothing', async () => {
    const state = await importFiles(edited(), { fileName: 'acme.zip', dryRun: true })

    expect(await itemsOf(state.Id)).toMatchObject([
      {
        Topic: 'ViewImport',
        Level: 'Success',
        Title: 'Check finished',
        Text: 'acme.zip: 0 to create, 1 to update, 0 unchanged, 0 skipped, 0 failed',
        ExpiresAt: state.ExpiresAt,
      },
    ])
  })

  it('reports an import with failed files as a warning, of a dry run as a check that found problems', async () => {
    const imported = await importFiles(invalid())
    const checked = await importFiles(invalid(), { fileName: 'acme.zip', dryRun: true })

    // without a file name the text is only the counts
    expect(await itemsOf(imported.Id)).toMatchObject([
      {
        Level: 'Warning',
        Title: 'Import finished with failures',
        Text: '0 created, 0 updated, 0 unchanged, 0 skipped, 1 failed',
        Job: { Kind: 'ViewImport', Id: imported.Id },
      },
    ])
    expect((await itemsOf(imported.Id))[0]!.Job).not.toHaveProperty('FileName')
    expect(await itemsOf(checked.Id)).toMatchObject([
      {
        Level: 'Warning',
        Title: 'Check found problems',
        Text: 'acme.zip: 0 to create, 0 to update, 0 unchanged, 0 skipped, 1 failed',
      },
    ])
  })

  it('shows the ends of jobs to an Administrator and not to a Contributor, who cannot dismiss them either', async () => {
    const exported = await exportView()
    const imported = await importFiles(edited(), { fileName: 'acme.zip' })
    const [exportItem] = await itemsOf(exported.Id)
    const [importItem] = await itemsOf(imported.Id)

    setRole('acme.billing', 'Contributor')
    expect(await itemsOf(exported.Id)).toEqual([])
    expect(await itemsOf(imported.Id)).toEqual([])
    expect(titles((await list(billing)).Items)).toEqual([suggestion, note])
    await expect(dismiss(billing, exportItem!.Id!)).rejects.toMatchObject(notFound(exportItem!.Id!))
    await expect(accept(billing, importItem!.Id!)).rejects.toMatchObject(notFound(importItem!.Id!))

    setRole('acme.billing', 'Administrator')
    expect(titles((await list(billing)).Items)).toEqual([
      'Import finished',
      'Export finished',
      suggestion,
      failedExport,
      note,
    ])
    // a dismissed result keeps its job, so it still leads to the archive
    expect(await dismiss(billing, exportItem!.Id!)).toMatchObject({
      State: 'Dismissed',
      Job: { Kind: 'ViewExport', Id: exported.Id, FileName: exported.FileName },
    })
    expect((await itemsOf(exported.Id)).map((item) => item.State)).toEqual(['Dismissed'])
    await expect(accept(billing, importItem!.Id!)).rejects.toMatchObject(notAction)
  })

  it('leaves the notification of a job in the view of the job only', async () => {
    const state = await exportView()

    expect(stored(billing).filter((item) => item.Job?.Id === state.Id)).toHaveLength(1)
    expect(Object.keys(loadDb().inbox ?? {})).toEqual(['acme/billing/default'])
  })
})
