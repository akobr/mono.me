import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AccountRole, ViewExport, ViewImport } from '@42for.net/2splatform.sdk'
import { createSeed, mockUser, type MockDb } from '@/mock/db'
import {
  cutText,
  decodeInboxToken,
  encodeInboxToken,
  exportNotification,
  exportText,
  importNotification,
  importText,
  inboxIdForKey,
  inboxOf,
  isInboxId,
  isVisibleTo,
  newInboxId,
  parseInboxFilter,
  pushInboxItem,
  seededInboxId,
  systemActor,
  type InboxPush,
} from '@/mock/inbox'

// The rules of the inbox that the mock API ports from the back end (424-31, docs/Platform/Storyteller/inbox.md): the ids
// of ViewInboxIds, the token and the visibility of ViewInboxService, the push of ViewInboxPublisher, and the texts of
// ViewTransferNotifications with the vectors of its tests. The routes are tested in inbox-handlers.test.ts.

afterEach(() => {
  vi.useRealTimers()
})

const day = 86_400_000
const billing = { org: 'acme', project: 'billing', view: 'default' }
const empty = { org: 'acme', project: 'billing', view: 'empty' }

/** A message for Contributors by the platform, the simplest push there is. */
const message = (title = 'A message', more: Partial<InboxPush> = {}): InboxPush => ({
  Kind: 'Message',
  Topic: 'Test',
  Title: title,
  MinimalRole: 'Contributor',
  CreatedBy: systemActor,
  ...more,
})

describe('inbox ids', () => {
  const time = '2026-11-22T09:14:07.123Z'

  it('builds an id from the 21 digits of the time in UTC and 32 lower-case hexadecimal digits', () => {
    const id = newInboxId(time)

    expect(id).toMatch(/^202611220914071230000[0-9a-f]{32}$/)
    expect(id).toHaveLength(53)
    expect(isInboxId(id)).toBe(true)
    expect(newInboxId(time)).not.toBe(id)
    // one instant has one text, whatever its offset and its type
    expect(newInboxId('2026-11-22T10:14:07.123+01:00').slice(0, 21)).toBe('202611220914071230000')
    expect(newInboxId(new Date(time)).slice(0, 21)).toBe('202611220914071230000')
    expect(newInboxId(Date.parse(time)).slice(0, 21)).toBe('202611220914071230000')
  })

  it('orders the ids by their times, whatever the second half', () => {
    const earlier = Array.from({ length: 20 }, () => newInboxId('2026-11-22T09:14:07.123Z'))
    const later = Array.from({ length: 20 }, () => newInboxId('2026-11-22T09:14:07.124Z'))

    expect(earlier.every((before) => later.every((after) => before < after))).toBe(true)
    expect(newInboxId('2026-12-01T00:00:00Z') > newInboxId('2026-11-30T23:59:59.999Z')).toBe(true)
  })

  it('gives the same key and time the same id, and another key or time another one', () => {
    const id = inboxIdForKey(time, 'ViewExport.job1')

    expect(inboxIdForKey(time, 'ViewExport.job1')).toBe(id)
    expect(isInboxId(id)).toBe(true)
    expect(id.slice(0, 21)).toBe('202611220914071230000')
    expect(inboxIdForKey(time, 'ViewImport.job1')).not.toBe(id)
    expect(inboxIdForKey(time, 'ViewExport.job2')).not.toBe(id)
    expect(inboxIdForKey('2026-11-22T09:14:07.124Z', 'ViewExport.job1').slice(21)).toBe(id.slice(21))
  })

  it('builds a seeded id from the digits of its time and a fixed suffix', () => {
    expect(seededInboxId(time, 3)).toBe('2026112209140712300005eed0000000000000000000000000003')
    expect(isInboxId(seededInboxId(time, 12))).toBe(true)
  })

  it('takes only an id of that shape', () => {
    const digits = '202611220914071230000'
    const id = `${digits}0123456789abcdef0123456789abcdef`
    expect(isInboxId(id)).toBe(true)

    for (const other of [
      id.slice(1),
      `${id}0`,
      // upper-case hexadecimal digits, a letter among the digits of the time, a letter that is no hexadecimal digit
      `${digits}0123456789ABCDEF0123456789ABCDEF`,
      `${digits.slice(0, 20)}a${id.slice(21)}`,
      `${id.slice(0, 52)}g`,
      `${id}\n`,
      ` ${id}`,
      'nope',
      '',
      undefined,
      null,
    ]) {
      expect(isInboxId(other), String(other)).toBe(false)
    }
  })
})

describe('inbox continuation tokens', () => {
  const id = '20261122091407000000079aab47ca406989d11506daca106041b'
  // the token of this id in docs/Platform/Storyteller/inbox.md
  const token = 'MToyMDI2MTEyMjA5MTQwNzAwMDAwMDA3OWFhYjQ3Y2E0MDY5ODlkMTE1MDZkYWNhMTA2MDQxYg'

  it('writes a token as the base64url of 1: and the last id, without padding, as the API does', () => {
    expect(encodeInboxToken(id)).toBe(token)
    expect(decodeInboxToken(token)).toBe(id)

    for (let index = 0; index < 50; index++) {
      const other = newInboxId(Date.now() + index)
      expect(encodeInboxToken(other)).toMatch(/^[A-Za-z0-9_-]+$/)
      expect(decodeInboxToken(encodeInboxToken(other))).toBe(other)
    }
  })

  it('reads no token and a blank one as the first page, and a padded or spaced one as its id', () => {
    expect(decodeInboxToken(undefined)).toBeUndefined()
    expect(decodeInboxToken(null)).toBeUndefined()
    expect(decodeInboxToken('')).toBeUndefined()
    expect(decodeInboxToken('  ')).toBeUndefined()
    expect(decodeInboxToken(` ${token} `)).toBe(id)
    expect(decodeInboxToken(btoa(`1:${id}`))).toBe(id)
  })

  it('refuses whatever the inbox did not issue', () => {
    for (const other of [
      'not-a-token',
      '%%%',
      'a',
      btoa(id),
      btoa(`2:${id}`),
      btoa(`1:${id}0`),
      btoa('1:abc'),
      btoa(`1:${id.toUpperCase()}`),
      btoa(JSON.stringify(['2026-11-22T09:14:07Z', id])),
    ]) {
      expect(decodeInboxToken(other), other).toBeNull()
    }
  })
})

describe('inbox visibility', () => {
  const roles: AccountRole[] = ['None', 'Reader', 'Contributor', 'ContributorWithSecrets', 'Administrator', 'Owner']

  it.each([
    ['None', []],
    ['Reader', []],
    ['Contributor', ['Contributor']],
    ['ContributorWithSecrets', ['Contributor', 'ContributorWithSecrets']],
    ['Administrator', ['Contributor', 'ContributorWithSecrets', 'Administrator']],
    ['Owner', ['Contributor', 'ContributorWithSecrets', 'Administrator', 'Owner']],
  ] as [AccountRole, AccountRole[]][])('shows a %s the items for %j', (role, seen) => {
    expect(roles.filter((minimal) => isVisibleTo({ MinimalRole: minimal }, role))).toEqual(seen)
  })

  it('shows nobody an item without a role', () => {
    expect(roles.some((role) => isVisibleTo({}, role))).toBe(false)
  })
})

describe('inbox state filter', () => {
  it('reads Open without a state, and one name in any casing or the number of a value', () => {
    for (const raw of [undefined, null, '', 'Open', 'open', 'OPEN', ' Open ', '0']) {
      expect(parseInboxFilter(raw), String(raw)).toBe('Open')
    }
    for (const raw of ['Done', 'done', 'DONE', '1', '+1']) {
      expect(parseInboxFilter(raw), raw).toBe('Done')
    }
  })

  it('refuses the state of an item, two names, a number without a value and a blank', () => {
    for (const raw of ['Accepted', 'Dismissed', 'All', 'Open,Done', 'Open Done', '2', '-1', '1.0', ' ']) {
      expect(parseInboxFilter(raw), raw).toBeUndefined()
    }
  })
})

describe('inbox push', () => {
  const now = new Date('2026-11-22T09:00:00.000Z')

  function start(): MockDb {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(now)
    return createSeed('default')
  }

  it('stores an open item of the level Info that lives for 30 days, now', () => {
    const db = start()

    const item = pushInboxItem(db, empty, message('Hello'))

    expect(item).toEqual({
      Id: expect.stringMatching(/^202611220900000000000[0-9a-f]{32}$/),
      Kind: 'Message',
      Topic: 'Test',
      Level: 'Info',
      Title: 'Hello',
      Text: undefined,
      MinimalRole: 'Contributor',
      State: 'Open',
      CreatedAt: '2026-11-22T09:00:00.000Z',
      CreatedBy: { Kind: 'System', Id: 'system' },
      ExpiresAt: '2026-12-22T09:00:00.000Z',
      Action: undefined,
      Job: undefined,
    })
    expect(inboxOf(db, empty)).toEqual([item])
    expect(db.inbox).toEqual({ 'acme/billing/empty': [item] })
  })

  it('keeps what the producer names: the level, the text, the action, the job and the time of the event', () => {
    const db = start()
    const job = { Kind: 'ViewExport', Id: 'job1', FileName: 'export.zip' } as const

    const action = pushInboxItem(db, empty, {
      Kind: 'Action',
      Topic: 'SchemaSuggestion',
      Level: 'Warning',
      Title: 'Suggested',
      Text: 'Why',
      MinimalRole: 'Administrator',
      CreatedBy: { Kind: 'Account', Id: 'user_01MOCKGRACE', Name: 'Grace Hopper' },
      Action: 'GenerateSchema',
      Job: job,
      CreatedAt: '2026-11-20T10:00:00+01:00',
    })

    expect(action).toMatchObject({
      Id: expect.stringMatching(/^202611200900000000000/),
      Level: 'Warning',
      Text: 'Why',
      MinimalRole: 'Administrator',
      CreatedAt: '2026-11-20T09:00:00.000Z',
      CreatedBy: { Kind: 'Account', Id: 'user_01MOCKGRACE', Name: 'Grace Hopper' },
      // the retention counts from the push, also when the event is older
      ExpiresAt: '2026-12-22T09:00:00.000Z',
      Action: 'GenerateSchema',
      Job: job,
    })
  })

  it('cuts a title at 200 characters and a text at 2000, with …', () => {
    const db = start()

    const cut = pushInboxItem(db, empty, message('t'.repeat(201), { Text: 'x'.repeat(2001) }))
    const kept = pushInboxItem(db, empty, message('t'.repeat(200), { Text: 'x'.repeat(2000) }))

    expect(cut.Title).toBe(`${'t'.repeat(199)}…`)
    expect(cut.Text).toBe(`${'x'.repeat(1999)}…`)
    expect(kept.Title).toBe('t'.repeat(200))
    expect(kept.Text).toBe('x'.repeat(2000))
    expect(cutText('abcdef', 5)).toBe('abcd…')
    expect(cutText('abcde', 5)).toBe('abcde')
  })

  it('keeps an expiry between one minute and 365 days from now, and moves any other to the nearer limit', () => {
    const db = start()
    const expiry = (ms: number) => pushInboxItem(db, empty, message('m', { ExpiresAt: new Date(now.getTime() + ms).toISOString() })).ExpiresAt

    expect(expiry(day)).toBe('2026-11-23T09:00:00.000Z')
    expect(expiry(10_000)).toBe('2026-11-22T09:01:00.000Z')
    expect(expiry(-day)).toBe('2026-11-22T09:01:00.000Z')
    expect(expiry(400 * day)).toBe('2027-11-22T09:00:00.000Z')
  })

  it('writes the same key and time once, and answers the stored item as it is, also a closed one', () => {
    const db = start()
    const event = { Key: 'ViewExport.job1', CreatedAt: '2026-11-22T08:59:00Z' }

    const first = pushInboxItem(db, empty, message('First', event))
    first.State = 'Dismissed'
    first.Closed = { At: now.toISOString(), By: systemActor }
    const second = pushInboxItem(db, empty, message('Second', { ...event, Text: 'Other', ExpiresAt: '2027-01-01T00:00:00Z' }))

    expect(second).toBe(first)
    expect(second).toMatchObject({ Title: 'First', State: 'Dismissed', ExpiresAt: '2026-12-22T09:00:00.000Z' })
    expect(second.Text).toBeUndefined()
    expect(inboxOf(db, empty)).toHaveLength(1)

    // another key, another time and no key are other items
    pushInboxItem(db, empty, message('Import', { ...event, Key: 'ViewImport.job1' }))
    pushInboxItem(db, empty, message('Later', { ...event, CreatedAt: '2026-11-22T08:59:01Z' }))
    pushInboxItem(db, empty, message('No key', { CreatedAt: event.CreatedAt }))
    pushInboxItem(db, empty, message('No key', { CreatedAt: event.CreatedAt }))
    expect(inboxOf(db, empty).map((item) => item.Title)).toEqual(['First', 'Import', 'Later', 'No key', 'No key'])
    // and the same key in another view is another inbox
    expect(pushInboxItem(db, { ...empty, view: 'other' }, message('Other view', event))).not.toBe(first)
  })

  it('creates the item of a key again once the stored one has expired', () => {
    const db = start()
    const event = { Key: 'ViewExport.job1', CreatedAt: '2026-11-22T08:59:00Z', ExpiresAt: '2026-11-22T09:10:00Z' }
    const first = pushInboxItem(db, empty, message('First', event))

    vi.setSystemTime(new Date('2026-11-22T09:10:00Z'))
    const second = pushInboxItem(db, empty, message('Second', { ...event, ExpiresAt: undefined }))

    expect(second).not.toBe(first)
    expect(second).toMatchObject({ Id: first.Id, Title: 'Second', ExpiresAt: '2026-12-22T09:10:00.000Z' })
    expect(inboxOf(db, empty)).toEqual([second])
  })

  it.each([
    ['a blank title', message('  '), 'needs a title'],
    ['a topic that is not a code', message('m', { Topic: 'view-export' }), "The topic 'view-export' is not a code"],
    ['a topic of 65 characters', message('m', { Topic: 'T'.repeat(65) }), 'is not a code'],
    ['an action without its code', message('m', { Kind: 'Action' }), 'An action needs the code of its handler.'],
    ['a message with an action code', message('m', { Action: 'GenerateSchema' }), 'Only an action has the code of a handler, not a message.'],
    ['an action code that is not a code', message('m', { Kind: 'Action', Action: 'generate schema' }), "The action 'generate schema' is not a code"],
    ['a role below Contributor', message('m', { MinimalRole: 'Reader' }), 'The minimal role of an inbox item is Contributor or above, not Reader.'],
    ['no role', message('m', { MinimalRole: 'None' }), 'not None'],
    ['a key without the time of its event', message('m', { Key: 'ViewExport.job1' }), 'A push with a key needs the time of its event.'],
  ] as [string, InboxPush, string][])('refuses %s, an error of the producer, and stores nothing', (_name, push, error) => {
    const db = start()

    expect(() => pushInboxItem(db, empty, push)).toThrow(error)
    expect(inboxOf(db, empty)).toEqual([])
  })
})

describe('inbox notifications of exports and imports', () => {
  const id = '0123456789abcdef0123456789abcdef'
  const archive = 'export-acme-main-default-20261001-080000Z.zip'
  const actor = { Kind: 'Account', Id: 'user_01', Name: 'Ada Lovelace' } as const
  const completedAt = '2026-10-01T08:03:00.000Z'
  const expiresAt = '2026-10-02T08:03:00.000Z'

  // An export of every kind that succeeded three minutes after its start (ViewTransferNotificationsTests of the API).
  const exported: ViewExport = {
    Id: id,
    Status: 'Succeeded',
    Format: 'Folder',
    FileName: archive,
    CreatedAt: '2026-10-01T08:00:00.000Z',
    CreatedBy: actor,
    StartedAt: '2026-10-01T08:01:00.000Z',
    CompletedAt: completedAt,
    ExpiresAt: expiresAt,
    AnnotationCount: 12,
    SchemaCount: 3,
    TemplateCount: 2,
    ConfigurationCount: 40,
    VersionCount: 58,
    Size: 1_258_291,
  }

  // An import of acme.zip that succeeded at the same time, without a failed file.
  const imported: ViewImport = {
    Id: id,
    Status: 'Succeeded',
    FileName: 'acme.zip',
    CreatedAt: '2026-10-01T08:00:00.000Z',
    CreatedBy: actor,
    StartedAt: '2026-10-01T08:01:00.000Z',
    CompletedAt: completedAt,
    ExpiresAt: expiresAt,
    TotalCount: 16,
    ProcessedCount: 16,
    Summary: { Created: 3, Updated: 2, Unchanged: 10, Skipped: 1, Failed: 0 },
  }

  const bare: ViewExport = {
    ...exported,
    AnnotationCount: undefined,
    SchemaCount: undefined,
    TemplateCount: undefined,
    ConfigurationCount: undefined,
    VersionCount: undefined,
  }

  it('reports an export that succeeded as a success with the counts and the size, which expires with the job', () => {
    expect(exportNotification(exported)).toEqual({
      Kind: 'Notification',
      Topic: 'ViewExport',
      Level: 'Success',
      Title: 'Export finished',
      Text: '12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB',
      MinimalRole: 'Administrator',
      CreatedBy: actor,
      ExpiresAt: expiresAt,
      Job: { Kind: 'ViewExport', Id: id, FileName: archive },
      Key: `ViewExport.${id}`,
      CreatedAt: completedAt,
    })
  })

  it('reports an export that failed as an error with the error of the job, which names no expiry', () => {
    const failed: ViewExport = { ...bare, Status: 'Failed', Size: undefined, Error: 'The export failed: The view has no annotations.' }

    expect(exportNotification(failed)).toEqual({
      Kind: 'Notification',
      Topic: 'ViewExport',
      Level: 'Error',
      Title: 'Export failed',
      Text: 'The export failed: The view has no annotations.',
      MinimalRole: 'Administrator',
      CreatedBy: actor,
      // not the day of the job: the push gives the retention of the inbox
      ExpiresAt: undefined,
      Job: { Kind: 'ViewExport', Id: id, FileName: archive },
      Key: `ViewExport.${id}`,
      CreatedAt: completedAt,
    })
  })

  it('reports an import that succeeded as a success with the file and every count, which expires with the job', () => {
    expect(importNotification(imported)).toEqual({
      Kind: 'Notification',
      Topic: 'ViewImport',
      Level: 'Success',
      Title: 'Import finished',
      Text: 'acme.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed',
      MinimalRole: 'Administrator',
      CreatedBy: actor,
      ExpiresAt: expiresAt,
      Job: { Kind: 'ViewImport', Id: id, FileName: 'acme.zip' },
      Key: `ViewImport.${id}`,
      CreatedAt: completedAt,
    })
  })

  it.each([
    [false, 0, 'Success', 'Import finished'],
    [true, 0, 'Success', 'Check finished'],
    [false, 1, 'Warning', 'Import finished with failures'],
    [true, 4, 'Warning', 'Check found problems'],
  ] as const)('names an import that succeeded by its dry run (%s) and its %i failed files: %s, %s', (isDryRun, failed, level, title) => {
    const push = importNotification({ ...imported, IsDryRun: isDryRun, Summary: { ...imported.Summary, Failed: failed } })

    expect(push).toMatchObject({ Level: level, Title: title, ExpiresAt: expiresAt })
    expect(push.Text?.endsWith(`1 skipped, ${failed} failed`)).toBe(true)
  })

  it.each([
    [false, 'Import failed'],
    [true, 'Check failed'],
  ])('reports an import that failed (dry run: %s) as the error %s, whatever files failed before', (isDryRun, title) => {
    const failed: ViewImport = {
      ...imported,
      Status: 'Failed',
      IsDryRun: isDryRun,
      Error: 'The import failed: The uploaded archive no longer exists.',
      Summary: { Created: 1, Failed: 2 },
    }

    expect(importNotification(failed)).toEqual({
      Kind: 'Notification',
      Topic: 'ViewImport',
      Level: 'Error',
      Title: title,
      Text: 'The import failed: The uploaded archive no longer exists.',
      MinimalRole: 'Administrator',
      CreatedBy: actor,
      ExpiresAt: undefined,
      Job: { Kind: 'ViewImport', Id: id, FileName: 'acme.zip' },
      Key: `ViewImport.${id}`,
      CreatedAt: completedAt,
    })
  })

  it('writes the text of an import without a file name as the counts, of a dry run as what a real run would write', () => {
    const unnamed = importNotification({ ...imported, FileName: undefined })
    expect(unnamed.Text).toBe('3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed')
    expect(unnamed.Job).toEqual({ Kind: 'ViewImport', Id: id, FileName: undefined })
    expect(importText({ ...imported, FileName: '' })).toBe('3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed')

    expect(importText({ ...imported, IsDryRun: true, Summary: { Created: 1, Updated: 1, Skipped: 2, Failed: 5 } })).toBe(
      'acme.zip: 1 to create, 1 to update, 0 unchanged, 2 skipped, 5 failed',
    )
    expect(importText({ ...imported, Summary: { Created: 1234567, Unchanged: 10 } })).toBe(
      'acme.zip: 1234567 created, 0 updated, 10 unchanged, 0 skipped, 0 failed',
    )
  })

  it('writes the text of an import without a summary as its file name, or none', () => {
    expect(importText({ ...imported, Summary: undefined })).toBe('acme.zip')
    expect(importText({ ...imported, Summary: undefined, FileName: undefined })).toBeUndefined()
    expect(importNotification({ ...imported, Summary: undefined }).Level).toBe('Success')
  })

  it('names only the kinds that were exported, in the singular and the plural', () => {
    // a kind that was left out has no count; one that was exported and found nothing has zero
    expect(
      exportText({ ...exported, SchemaCount: undefined, TemplateCount: 0, ConfigurationCount: undefined, VersionCount: 0, Size: 2048 }),
    ).toBe('12 annotations, 0 templates, 0 versions, 2.0 KB')

    for (const [count, expected] of [
      [0, '0 annotations, 0 schemas, 0 templates, 0 configurations, 0 versions'],
      [1, '1 annotation, 1 schema, 1 template, 1 configuration, 1 version'],
      [2, '2 annotations, 2 schemas, 2 templates, 2 configurations, 2 versions'],
      [1234567, '1234567 annotations, 1234567 schemas, 1234567 templates, 1234567 configurations, 1234567 versions'],
    ] as const) {
      expect(
        exportText({
          ...exported,
          AnnotationCount: count,
          SchemaCount: count,
          TemplateCount: count,
          ConfigurationCount: count,
          VersionCount: count,
          Size: undefined,
        }),
      ).toBe(expected)
    }
  })

  it('names the versions only when the job counted them and a kind with versions was exported', () => {
    expect(exportText({ ...exported, VersionCount: undefined })).toBe(
      '12 annotations, 3 schemas, 2 templates, 40 configurations, 1.2 MB',
    )
    // the job always counts the versions; without a kind that has versions the count is zero and says nothing
    expect(
      exportText({ ...exported, SchemaCount: undefined, TemplateCount: undefined, ConfigurationCount: undefined, VersionCount: 0, Size: 512 }),
    ).toBe('12 annotations, 512 B')
  })

  it('gives an export without counts and without a size no text', () => {
    expect(exportText(bare)).toBe('1.2 MB')
    expect(exportText({ ...bare, Size: undefined })).toBeUndefined()
    expect(exportNotification({ ...bare, Size: undefined })).toMatchObject({ Title: 'Export finished', Text: undefined })
  })

  it.each([
    [0, '0 B'],
    [1, '1 B'],
    [1023, '1023 B'],
    [1024, '1.0 KB'],
    [1536, '1.5 KB'],
    [1587, '1.5 KB'],
    [1588, '1.6 KB'],
    [1_048_063, '1023.5 KB'],
    // rounded before the unit is chosen: never 1024.0 KB
    [1_048_575, '1.0 MB'],
    [1_048_576, '1.0 MB'],
    [1_258_291, '1.2 MB'],
    [20 * 1024 * 1024, '20.0 MB'],
    [1_073_741_823, '1.0 GB'],
    [(5 * 1024 * 1024 * 1024) / 2, '2.5 GB'],
    // no unit above GB
    [1024 * 1024 * 1024 * 1024 * 3, '3072.0 GB'],
  ])('ends the text of an export of %i bytes with %s', (size, expected) => {
    expect(exportText({ ...bare, Size: size })).toBe(expected)
  })

  it('reports the actor of the job, also a machine and a person through an actor, under a key per kind', () => {
    const pipeline = { Kind: 'Machine', Id: 'machine-1', Name: 'Nightly export' } as const

    expect(exportNotification({ ...exported, CreatedBy: pipeline }).CreatedBy).toEqual(pipeline)
    expect(importNotification({ ...imported, CreatedBy: { ...actor, Via: 'mcp' } }).CreatedBy).toEqual({ ...actor, Via: 'mcp' })
    expect(exportNotification(exported).Key).toBe(`ViewExport.${id}`)
    expect(importNotification(imported).Key).toBe(`ViewImport.${id}`)
  })

  it('reports an end without its time at the last time of the job', () => {
    expect(exportNotification({ ...exported, CompletedAt: undefined }).CreatedAt).toBe('2026-10-01T08:01:00.000Z')
    expect(exportNotification({ ...exported, CompletedAt: undefined, StartedAt: undefined }).CreatedAt).toBe('2026-10-01T08:00:00.000Z')
    expect(importNotification({ ...imported, CompletedAt: undefined }).CreatedAt).toBe('2026-10-01T08:01:00.000Z')
    expect(importNotification({ ...imported, CompletedAt: undefined, StartedAt: undefined }).CreatedAt).toBe('2026-10-01T08:00:00.000Z')
  })

  it.each(['Queued', 'Running'] as const)('refuses a job that has not ended: %s', (status) => {
    expect(() => exportNotification({ ...exported, Status: status })).toThrow(
      `The export job '${id}' has not ended; it is ${status}.`,
    )
    expect(() => importNotification({ ...imported, Status: status })).toThrow(
      `The import job '${id}' has not ended; it is ${status}.`,
    )
  })

  it.each([
    ['export', 'Succeeded', 1],
    ['export', 'Failed', 30],
    ['import', 'Succeeded', 1],
    ['import', 'Failed', 30],
  ] as const)('stores the end of an %s that %s as one item per job, which lives %i days', (kind, status, days) => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(completedAt))
    const db = createSeed('default')
    const push = () =>
      kind === 'export'
        ? exportNotification({ ...exported, Status: status, Error: 'The export failed: boom' })
        : importNotification({ ...imported, Status: status, Error: 'The import failed: boom' })

    const first = pushInboxItem(db, empty, push())
    // a second push of the job, a minute later
    vi.setSystemTime(new Date(Date.parse(completedAt) + 60_000))
    const second = pushInboxItem(db, empty, push())

    expect(second).toBe(first)
    expect(inboxOf(db, empty)).toHaveLength(1)
    expect(first).toMatchObject({
      Kind: 'Notification',
      MinimalRole: 'Administrator',
      State: 'Open',
      CreatedAt: completedAt,
      CreatedBy: actor,
      ExpiresAt: new Date(Date.parse(completedAt) + days * day).toISOString(),
      Job: { Kind: kind === 'export' ? 'ViewExport' : 'ViewImport', Id: id },
    })
  })
})

describe('inbox seed', () => {
  const titles = [
    'Generate a schema for 7 similar configurations',
    'Schema suggestions are on for this view',
    'Import finished',
    'Export failed',
    'Generate a schema for 4 similar configurations',
  ]

  it.each([
    ['default', 'acme', 'billing', 'default'],
    ['default', 'acme', 'billing', 'staging'],
    ['default', 'acme', 'shipping', 'default'],
    ['default', 'northwind', 'portal', 'default'],
    ['large', 'acme', 'billing', 'default'],
    ['showcase', 'fabrikam', 'invoicing-suite', 'default'],
    ['showcase', 'fabrikam', 'invoicing-suite', 'feature-delivery1'],
    ['showcase', 'bluefin', 'money-suite', 'staging'],
  ] as const)('starts the view %s: %s/%s/%s, which the mock seeds with data, with five items', (scenario, org, project, view) => {
    const db = createSeed(scenario)

    const items = inboxOf(db, { org, project, view })

    expect(items.map((item) => item.Title)).toEqual(titles)
    // stored once: the next read is the same list
    expect(inboxOf(db, { org, project, view })).toBe(items)
  })

  it.each([
    ['default', 'acme', 'billing', 'empty'],
    ['default', 'acme', 'returns', 'default'],
    ['default', 'globex', 'portal', 'default'],
    ['new-user', 'globex', 'portal', 'default'],
    ['showcase', 'fabrikam', 'invoicing-suite', 'empty'],
    ['showcase', 'fabrikam', 'returns', 'default'],
    // a scope of the legacy datasets is none of the showcase
    ['showcase', 'acme', 'billing', 'default'],
  ] as const)('starts any other view empty: %s: %s/%s/%s', (scenario, org, project, view) => {
    const db = createSeed(scenario)

    expect(inboxOf(db, { org, project, view })).toEqual([])
    expect(db.inbox).toEqual({ [`${org}/${project}/${view}`]: [] })
  })

  it('seeds the items some minutes to days ago, each with an id of its time and a fixed suffix, none expired', () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-10T08:00:00.000Z'))

    const items = inboxOf(createSeed('default'), billing)

    expect(items.map((item) => [item.Id, item.CreatedAt, item.ExpiresAt])).toEqual([
      ['2026100708000000000005eed0000000000000000000000000001', '2026-10-07T08:00:00.000Z', '2026-11-06T08:00:00.000Z'],
      ['2026100808000000000005eed0000000000000000000000000002', '2026-10-08T08:00:00.000Z', '2026-11-07T08:00:00.000Z'],
      // the result of a job expires with the job, a day after its end
      ['2026101003000000000005eed0000000000000000000000000003', '2026-10-10T03:00:00.000Z', '2026-10-11T03:00:00.000Z'],
      ['2026101005000000000005eed0000000000000000000000000004', '2026-10-10T05:00:00.000Z', '2026-11-09T05:00:00.000Z'],
      ['2026101007350000000005eed0000000000000000000000000005', '2026-10-10T07:35:00.000Z', '2026-11-09T07:35:00.000Z'],
    ])
    expect(items.every((item) => isInboxId(item.Id) && item.Id === seededInboxId(item.CreatedAt, Number(item.Id.slice(-2))))).toBe(true)
    expect(items.map((item) => item.Closed?.At)).toEqual([
      '2026-10-08T08:00:00.000Z',
      undefined,
      '2026-10-10T04:00:00.000Z',
      undefined,
      undefined,
    ])
  })

  it('seeds an accepted action, an open message, a dismissed import without a job, a failed export and an open action', () => {
    const [accepted, note, imported, failed, suggested] = inboxOf(createSeed('default'), billing)
    const ada = { Kind: 'Account', Id: mockUser.id, Name: mockUser.name }
    const grace = { Kind: 'Account', Id: 'user_01MOCKGRACE', Name: 'Grace Hopper' }

    expect(accepted).toMatchObject({
      Kind: 'Action',
      Topic: 'SchemaSuggestion',
      Level: 'Info',
      MinimalRole: 'Contributor',
      State: 'Accepted',
      CreatedBy: systemActor,
      Closed: { By: ada },
      Action: 'GenerateSchema',
    })
    expect(note).toMatchObject({ Kind: 'Message', Level: 'Info', MinimalRole: 'Contributor', State: 'Open', CreatedBy: systemActor })
    expect(note).not.toHaveProperty('Action')
    expect(imported).toMatchObject({
      Kind: 'Notification',
      Topic: 'ViewImport',
      Level: 'Success',
      Text: 'catalog.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed',
      MinimalRole: 'Administrator',
      State: 'Dismissed',
      CreatedBy: ada,
      Closed: { By: grace },
    })
    // the mock never had the job of this import, so the item leads to none
    expect(imported).not.toHaveProperty('Job')
    expect(failed).toMatchObject({
      Kind: 'Notification',
      Topic: 'ViewExport',
      Level: 'Error',
      MinimalRole: 'Administrator',
      State: 'Open',
      CreatedBy: grace,
      Job: {
        Kind: 'ViewExport',
        Id: expect.stringMatching(/^[0-9a-f]{32}$/),
        FileName: expect.stringMatching(/^export-acme-billing-default-\d{8}-\d{6}Z\.zip$/),
      },
    })
    // an error message of a job, as the API cuts it at 500 characters
    expect(failed!.Text).toHaveLength(500)
    expect(failed!.Text).toMatch(/^The export failed: .*…$/)
    expect(suggested).toMatchObject({
      Kind: 'Action',
      Topic: 'SchemaSuggestion',
      Level: 'Info',
      Text: expect.stringContaining('4 configurations'),
      MinimalRole: 'Contributor',
      State: 'Open',
      CreatedBy: systemActor,
      Action: 'GenerateSchema',
    })
    expect(suggested).not.toHaveProperty('Closed')
    expect(suggested).not.toHaveProperty('Job')
  })

  it.each([
    // Grace administers the organization acme, Alan owns northwind.portal, Margaret owns fabrikam.fitness-cloud
    ['default', 'acme', 'billing', 'Grace Hopper'],
    ['default', 'acme', 'shipping', 'Grace Hopper'],
    ['default', 'northwind', 'portal', 'Alan Turing'],
    ['showcase', 'fabrikam', 'fitness-cloud', 'Margaret Hamilton'],
  ] as const)('names another account that administers the project as the one behind its jobs: %s: %s.%s, %s', (scenario, org, project, name) => {
    const items = inboxOf(createSeed(scenario), { org, project, view: 'default' })

    expect(items.find((item) => item.Title === 'Export failed')?.CreatedBy).toMatchObject({ Kind: 'Account', Name: name })
    expect(items.find((item) => item.Title === 'Import finished')?.Closed?.By).toMatchObject({ Kind: 'Account', Name: name })
  })

  it('puts a pushed item after the seeded ones of its view, and leaves the other views alone', () => {
    const db = createSeed('default')

    const pushed = pushInboxItem(db, billing, message('Pushed'))

    expect(inboxOf(db, billing).map((item) => item.Title)).toEqual([...titles, 'Pushed'])
    expect(inboxOf(db, billing).at(-1)).toBe(pushed)
    expect(Object.keys(db.inbox ?? {})).toEqual(['acme/billing/default'])
  })
})
